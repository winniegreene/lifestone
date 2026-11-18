using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>Everything the coin rule reads.</summary>
/// <param name="Rule">The ladder's values.</param>
/// <param name="Coin">The coin ACE's line says she lost, or null when the line names none.</param>
/// <param name="Cell">The corpse's cell.</param>
/// <param name="Ground">The ground that holds the corpse, or null.</param>
/// <param name="Gate">The rung owner's answer for that ground.</param>
public readonly record struct CorpseCoinAsk(LadderCorpseCoin Rule, long? Coin, uint Cell, HuntingGround? Ground, CorpseRunAnswer Gate);

/// <summary>The rule's answer.</summary>
/// <param name="Fetch">True when she walks to the corpse for its coin.</param>
/// <param name="Reason">The name: <c>met</c>, <c>coin-destroyed</c>, <c>coin-unread</c>, <c>coin-short</c>, <c>cell-unsafe</c> or <c>gate-held:&lt;rung&gt;</c>.</param>
public readonly record struct CorpseCoinAnswer(bool Fetch, string Reason);

/// <summary>
/// M6 7 AY (the owner's decision of 2026-11-06): she fetches her corpse for coin when it is safe to reach: the corpse holds at least <see cref="LadderCorpseCoin.MinCoin"/>, its cell is on the ground's <see cref="GroundCorpseFetch"/> list and outside every keep-out, and the
/// rung owner lets her go (<see cref="RungOwner.CorpseRun"/>). The conditions are read in that order after the first: the server's own fact, <see cref="LadderCorpseCoin.OnCorpse"/>: with <c>corpse_destroy_pyreals</c> on (ACE's default, @95ef426d <c>WorldObjects/Player_Death.cs:569-578</c>; the server mod turns it off, retail's rule) a dead player's coin
/// is destroyed and the corpse holds none, so the rule answers <c>coin-destroyed</c> and no walk is made for nothing. Pure. Only the handler calls it, once a death, for a death that owes no item; a corpse that owes an item goes by the gate alone.
/// </summary>
public static partial class CorpseCoinRule
{
    /// <summary>The coin of ACE's <c>You've lost 4,640 Pyreals, and your ...!</c> (<c>Player_Death.cs:700-725</c> @95ef426d: the stack in <c>N0</c>, then the plural), or null when the line names none.</summary>
    /// <param name="lostLine">The line, or null.</param>
    public static long? CoinLost(string? lostLine)
    {
        if (lostLine is null || CoinText().Match(lostLine) is not { Success: true } match)
        {
            return null;
        }

        return long.TryParse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var coin) ? coin : null;
    }

    /// <summary>True when the corpse's cell is safe to reach: on the ground's list and outside the keep-outs, whatever the level.</summary>
    /// <param name="ground">The ground that holds the corpse, or null.</param>
    /// <param name="cell">The corpse's cell.</param>
    public static bool Safe(HuntingGround? ground, uint cell)
    {
        if (ground?.CorpseFetch is not { } list || !ground.InLandblocks(cell))
        {
            return false;
        }

        return list.Holds(cell) && (ground.Hunt is null || (!ground.Hunt.KeepOut.Contains(cell) && !ground.Hunt.DropBack.Contains(cell)));
    }

    /// <summary>The answer (see the class).</summary>
    /// <param name="ask">What is known.</param>
    public static CorpseCoinAnswer Decide(in CorpseCoinAsk ask)
    {
        if (!ask.Rule.OnCorpse)
        {
            return new(false, "coin-destroyed");
        }

        if (ask.Coin is not { } coin)
        {
            return new(false, "coin-unread");
        }

        if (coin < ask.Rule.MinCoin)
        {
            return new(false, "coin-short");
        }

        if (!Safe(ask.Ground, ask.Cell))
        {
            return new(false, "cell-unsafe");
        }

        return ask.Gate.Open ? new(true, "met") : new(false, ask.Gate.Reason);
    }

    /// <summary>The decision on one log line, met or not, with every input.</summary>
    /// <param name="ask">What was read.</param>
    /// <param name="answer">The answer.</param>
    public static string Line(in CorpseCoinAsk ask, CorpseCoinAnswer answer)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c, $"corpse fetch: coin={ask.Coin?.ToString(c) ?? "unread"} min={ask.Rule.MinCoin} on_corpse={(ask.Rule.OnCorpse ? "yes" : "no")} cell=0x{ask.Cell:X8} ground={ask.Ground?.Key ?? "none"} safe={(Safe(ask.Ground, ask.Cell) ? "yes" : "no")} gate={(ask.Gate.Open ? "open" : "shut")} ({ask.Gate.Reason}) -> {(answer.Fetch ? "fetch" : "no-fetch")}:{answer.Reason}");
    }

    /// <summary>M6 7 BB: whether a corpse that owes something she needs is worth the walk, and why not (see <see cref="CorpseTripAsk"/>). The need first, then the cell, then the gate: a corpse she does not need, or whose cell is not safe, is left by name.</summary>
    /// <param name="ask">What is known.</param>
    public static CorpseCoinAnswer DecideTrip(in CorpseTripAsk ask)
    {
        var need = ask.WornMissing > 0 ? "worn-gear"
            : !ask.CasterKept ? "caster"
            : ask.KitLost ? "kit"
            : ask.QuestItemLost ? "quest-item"
            : ask.ItemValue is { } value && value >= ask.Rule.MinItemValue ? "value"
            : ask.Rule.OnCorpse && ask.Coin is { } coin && coin >= ask.Rule.MinCoin ? "coin"
            : ask.Unread ? "unread"
            : null;
        if (need is null)
        {
            return new(false, "nothing-needed");
        }

        if (!ask.Gate.Open)
        {
            return new(false, ask.Gate.Reason); // a shut gate speaks first (AY: a shut gate means no fetch, whatever the cell)
        }

        return Safe(ask.Ground, ask.Cell) ? new(true, need) : new(false, "cell-unsafe");
    }

    /// <summary>The trip decision on one log line, with every input.</summary>
    /// <param name="ask">What was read.</param>
    /// <param name="answer">The answer.</param>
    public static string TripLine(in CorpseTripAsk ask, CorpseCoinAnswer answer)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c, $"corpse trip: worn_missing={ask.WornMissing} caster_kept={(ask.CasterKept ? "yes" : "no")} kit_lost={(ask.KitLost ? "yes" : "no")} quest_item_lost={(ask.QuestItemLost ? "yes" : "no")} item_value={ask.ItemValue?.ToString(c) ?? "unread"} min_value={ask.Rule.MinItemValue} coin={ask.Coin?.ToString(c) ?? "unread"} min_coin={ask.Rule.MinCoin} on_corpse={(ask.Rule.OnCorpse ? "yes" : "no")} diff_unread={(ask.Unread ? "yes" : "no")} cell=0x{ask.Cell:X8} ground={ask.Ground?.Key ?? "none"} safe={(Safe(ask.Ground, ask.Cell) ? "yes" : "no")} gate={(ask.Gate.Open ? "open" : "shut")} ({ask.Gate.Reason}) -> {(answer.Fetch ? "walk" : "leave-corpse")}:{answer.Reason}");
    }

    [GeneratedRegex(@"\b(\d[\d,]*) Pyreals?\b")]
    private static partial Regex CoinText();
}

/// <summary>What the trip rule reads (M6 7 BB; the coin, M6 7 finish): the trip is for what she needs: worn gear, the kit, a caster, a quest item, an item worth <see cref="LadderCorpseCoin.MinItemValue"/> or more, or at least <see cref="LadderCorpseCoin.MinCoin"/> coin where the server leaves it on the corpse.</summary>
/// <param name="Rule">The ladder's values.</param>
/// <param name="WornMissing">Pieces worn before the death that she does not hold now.</param>
/// <param name="CasterKept">False when the wielded caster left her hands.</param>
/// <param name="KitLost">True when ACE's drop line names a healing kit.</param>
/// <param name="QuestItemLost">True when ACE's drop line names a quest item (none is known yet: TODO(corpse-quest-item)).</param>
/// <param name="ItemValue">The Value of the dearest item the line names, or null: the corpse is unopened and the line carries no Value (TODO(corpse-item-value)).</param>
/// <param name="Unread">True when ACE's drop lines could not be tied to the death, so the diff is unread and the corpse might hold anything.</param>
/// <param name="Cell">The corpse's cell.</param>
/// <param name="Ground">The ground that holds the corpse, or null.</param>
/// <param name="Gate">The rung owner's answer for that ground.</param>
/// <param name="Coin">The coin ACE's line says she lost, or null when the line names none.</param>
public readonly record struct CorpseTripAsk(LadderCorpseCoin Rule, int WornMissing, bool CasterKept, bool KitLost, bool QuestItemLost, long? ItemValue, bool Unread, uint Cell, HuntingGround? Ground, CorpseRunAnswer Gate, long? Coin = null);

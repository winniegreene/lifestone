using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.UseManaStone"/>: GameAction 0x0035 UseWithTarget {stone, target}, retail <c>CM_Inventory::Event_UseWithTargetEvent</c> (11.6096 0x006AD3E0), closing <c>TODO(m4-gear-mana)</c>.
/// ACE (<c>ManaStone.HandleActionUseOnTarget</c>, ACE 95ef426d <c>ManaStone.cs:59-233</c>) answers in one step with no motion: the F7E0 line "The Mana Stone gives N points of mana to ..." (ChatMessageType Broadcast), the stone's destroy roll
/// ("The Mana Stone is destroyed." and 0x0024 for a retail charge, whose ManaStoneDestroyChance is 1), then GameEvent 0x01C7 UseDone. A refusal is a line and a UseDone with a code ("You have no items equipped that need mana.", 0x0036), or, for an item
/// already full, the line "The X is already full of mana." and a UseDone 0.
/// <para>Confirmed iff the UseDone 0 came and the "gives" line (or, M5 7 S2c, an empty stone's "drains" line) was read or the stone was removed; the detail is the line's numbers. Lane <see cref="ActionLane.Heal"/> (one use with a UseDone at a time); it holds nothing after its UseDone (ACE runs no chain for it).</para>
/// </summary>
internal sealed partial class ManaStoneTracker(ActionHandle handle, uint stone, uint target) : ActionTracker(handle, ActionLane.Heal, ActionPolicy.HealSeconds)
{
    /// <summary>ACE's <c>ActionCancelled</c>: the stone used on the player with no equipped item that needs mana, or an empty stone on the player.</summary>
    internal const uint ActionCancelledCode = 0x0036;

    /// <summary>ACE's <c>ItemDoesntHaveEnoughMana</c>: an empty stone used on an item with no mana to drain.</summary>
    internal const uint NotEnoughManaCode = 0x04C8;

    /// <summary>ACE's <c>YouDoNotOwnThatItem</c>: the target item is not the player's.</summary>
    internal const uint NotOwnedCode = 0x04BE;

    private bool _useDoneSeen;
    private bool _removed;
    private ManaStoneLine? _line;
    private string? _refusal;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0035 UseWithTarget (mana stone) " + ActionText.Hex(stone) + " on " + ActionText.Hex(target);

    /// <inheritdoc/>
    internal override bool ConsumesUseDone => true;

    /// <inheritdoc/>
    /// <remarks>
    /// M5 7 D141's before-check: in the world, the stone held by the player, and the target the player (a charge for the worn items) or an item the player holds (ACE answers YouDoNotOwnThatItem 0x04BE for
    /// any other, <c>ManaStone.cs</c> 59-233 @95ef426d).
    /// </remarks>
    internal override string? Refusal(ActionApi api)
    {
        if (WorldTrackers.NotInWorld(api) is { } notInWorld)
        {
            return notInWorld;
        }

        var inventory = api.State.Self.Inventory;
        if (!inventory.Find(stone).Held)
        {
            return Refuse(ActionReason.NotHeld, "the mana stone " + ActionText.Hex(stone) + " is not held by the player");
        }

        if (target != api.State.Self.Guid && !inventory.Find(target).Held)
        {
            return Refuse(ActionReason.NotHeld, "the stone's target " + ActionText.Hex(target) + " is neither the player nor an item the player holds");
        }

        // M6 item 14: an empty stone used on an item drains it and destroys it, a worn one too (ManaStone.cs:84-113 @95ef426d). So automation's use of a stone on a WORN item is sent only when the stone is known to hold mana: its
        // appraisal's ItemCurMana is above 0, or it is a Mana Charge (sold full, destroyed by its one use). A player's own use (ActionSource.Host) stays the player's to make.
        if (Handle.Source == ActionSource.Automation && target != api.State.Self.Guid && inventory.Find(target).Kind == InventoryPlaceKind.Equipped && !KnownCharged(api))
        {
            return Refuse(ActionReason.ContraryResult, "the mana stone " + ActionText.Hex(stone) + " is not known to hold mana (no appraisal says so and it is no Mana Charge): used on the worn item " + ActionText.Hex(target) + " an empty stone would drain and destroy it, so automation does not send it");
        }

        return null;
    }

    /// <summary>True when the stone is known to hold mana: the session's appraisal of it reads an ItemCurMana above 0, or its weenie is a Mana Charge's.</summary>
    private bool KnownCharged(ActionApi api) =>
        (api.State.Appraisals.TryGet(stone, out var appraisal) && appraisal.ItemMana is > 0)
        || (api.State.Objects.TryGet(stone, out var o) && Lifestone.Client.Session.Mage.CasterManaRule.ChargeMana.ContainsKey(o.Weenie.WeenieClassId));

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _useDoneSeen = _removed = false;
        _line = null;
        _refusal = null;
        session!.SendUseWithTarget(stone, target);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid == stone && e.Kind == InventoryChangeKind.Removed)
        {
            _removed = true; // the destroy roll consumed the stone (0x0024)
        }
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (_useDoneSeen)
        {
            return false;
        }

        if (line.Kind == ChatKind.System && _line is null && ManaStoneLine.TryParse(line.Text, out var read))
        {
            _line = read;
        }
        else if (line.Kind == ChatKind.System && (line.Text.StartsWith(NoItemsLine, StringComparison.Ordinal) || AlreadyFull().IsMatch(line.Text)))
        {
            _refusal = line.Text;
        }
        else if (line.Kind is ChatKind.WeenieError or ChatKind.Transient && line.Text.Length > 0)
        {
            _refusal = line.Text;
        }

        return false; // an observer: other trackers read the same line
    }

    /// <inheritdoc/>
    internal override bool OnUseDone(in UseDone e)
    {
        if (_useDoneSeen)
        {
            return false;
        }

        _useDoneSeen = true;
        Resolve(Settle(e.WeenieError));
        return true;
    }

    private ActionOutcome Settle(uint code)
    {
        if (code != 0)
        {
            return new ActionOutcome(ActionOutcomeKind.ServerError, code, "the server refused the mana stone (" + Describe(code) + (_refusal is null ? string.Empty : ": " + _refusal) + ")", ReasonOf(code, _refusal));
        }

        if (_line is { } line)
        {
            return new ActionOutcome(ActionOutcomeKind.Confirmed, 0, line.Describe() + (_removed ? "; the stone was consumed" : string.Empty));
        }

        if (_refusal is not null)
        {
            return new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server refused the mana stone (" + _refusal + "); nothing was given", ReasonOf(0, _refusal));
        }

        return _removed
            ? new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "the stone was consumed; the mana line was not read")
            : new ActionOutcome(ActionOutcomeKind.ServerError, 0, "nothing happened: UseDone 0, but no mana line, no refusal line and the stone was not removed (the stone not held, or no target?)", ActionReason.NoEffect);
    }

    /// <summary>
    /// M5 7 D141: the typed reason of a stone's refusal: an item already full, or nothing equipped that needs mana (<see cref="ActionReason.AlreadyDone"/>), an empty stone on an item with no mana
    /// (<see cref="ActionReason.NotEnoughMana"/>), a target the player does not own (<see cref="ActionReason.NotHeld"/>), else the code's own meaning.
    /// </summary>
    internal static ActionReason ReasonOf(uint code, string? line)
    {
        if (line is not null && (line.StartsWith(NoItemsLine, StringComparison.Ordinal) || AlreadyFull().IsMatch(line)))
        {
            return ActionReason.AlreadyDone;
        }

        return code switch
        {
            NotEnoughManaCode => ActionReason.NotEnoughMana,
            NotOwnedCode => ActionReason.NotHeld,
            _ => ActionReasons.FromCode(code),
        };
    }

    private static string Describe(uint code)
    {
        var name = code switch
        {
            ActionCancelledCode => "ActionCancelled",
            NotEnoughManaCode => "ItemDoesntHaveEnoughMana",
            NotOwnedCode => "YouDoNotOwnThatItem",
            _ => null,
        };
        return "WeenieError 0x" + code.ToString("X4", CultureInfo.InvariantCulture) + (name is null ? string.Empty : " " + name);
    }

    /// <summary>ACE's line for a stone used on the player with nothing equipped below its maximum (<c>ManaStone.cs:166</c>).</summary>
    internal const string NoItemsLine = "You have no items equipped that need mana.";

    [GeneratedRegex(@"^The .+ is already full of mana\.$", RegexOptions.CultureInvariant)]
    private static partial Regex AlreadyFull();
}

/// <summary>
/// The F7E0 line of a mana stone that gave its mana (ACE 95ef426d <c>ManaStone.cs:171-180</c>, <c>:216</c>): "The Mana Stone gives N points of mana to the following items: A, B.\nYour items are fully charged." (or "\nYou need M more mana to fully
/// charge your items.") for the player, "The Mana Stone gives N points of mana to the X." for one item. The numbers are written with the server's group separators (<c>{0:N0}</c>).
/// </summary>
/// <param name="Given">The mana the stone gave.</param>
/// <param name="Items">The items named (one for a use on an item).</param>
/// <param name="StillNeeded">The mana the equipped items still lack after the use (the "You need M more" line), 0 when "Your items are fully charged." or for a use on an item.</param>
/// <param name="Drained">M5 7 S2c: true for an empty stone's capture line, "The Mana Stone drains N points of mana from the X.\nThe X is destroyed." (ManaStone.cs:108-110): <see cref="Given"/> is then the mana the stone took (its efficiency times the item's mana, :108) and the item is gone.</param>
internal sealed partial record ManaStoneLine(int Given, IReadOnlyList<string> Items, int StillNeeded, bool Drained = false)
{
    /// <summary>Reads the line, or false when it is not a mana stone's "gives" line.</summary>
    internal static bool TryParse(string text, out ManaStoneLine line)
    {
        line = null!;
        var drain = Drains().Match(text);
        if (drain.Success && int.TryParse(drain.Groups["n"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var drained))
        {
            line = new ManaStoneLine(drained, [drain.Groups["one"].Value], 0, Drained: true); // M5 7 S2c: the capture's line, read as the success it is
            return true;
        }

        var m = Gives().Match(text);
        if (!m.Success || !int.TryParse(m.Groups["n"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var given))
        {
            return false;
        }

        var items = m.Groups["list"].Success ? m.Groups["list"].Value.Split(", ", StringSplitOptions.None) : [m.Groups["one"].Value];
        var more = 0;
        var need = MoreNeeded().Match(text);
        if (need.Success && !int.TryParse(need.Groups["m"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out more))
        {
            return false;
        }

        line = new ManaStoneLine(given, items, more);
        return true;
    }

    /// <summary>The outcome's detail: <c>gave 5000 mana to 13 items; fully charged</c>.</summary>
    internal string Describe() => Drained
        ? "drained " + ActionText.Count(Given) + " mana from the " + Items[0] + " into the stone; the " + Items[0] + " is destroyed"
        : "gave " + ActionText.Count(Given) + " mana to " + (Items.Count == 1 ? "the " + Items[0] : ActionText.Count(Items.Count) + " items") + (StillNeeded > 0 ? "; " + ActionText.Count(StillNeeded) + " more needed" : "; fully charged");

    [GeneratedRegex(@"^The Mana Stone gives (?<n>[\d,]+) points of mana to (?:the following items: (?<list>.+?)\.(?:\n|$)|the (?<one>.+?)\.$)", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Gives();

    /// <summary>ACE's capture line (ManaStone.cs:110): "The Mana Stone drains {N:N0} points of mana from the {X}.\nThe {X} is destroyed.", the line end as sent (a space once the chat log has cleaned it).</summary>
    [GeneratedRegex(@"^The Mana Stone drains (?<n>[\d,]+) points of mana from the (?<one>.+?)\.(?:\s+The .+ is destroyed\.)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Drains();

    [GeneratedRegex(@"You need (?<m>[\d,]+) more mana", RegexOptions.CultureInvariant)]
    private static partial Regex MoreNeeded();
}

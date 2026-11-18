using System.Globalization;
using System.Text.Json;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// One row of a quest file's <c>returns</c> table (M6 2 d): an attuned piece an NPC takes back and what he gives for it. The rows are his Give emotes in the pinned world SQL v0.9.296 (<c>weenie_properties_emote</c> category 6, the
/// <see cref="Emote"/> id, whose actions are TakeItems, Tell and Give of the <see cref="RewardWcid"/> x 1).
/// </summary>
/// <param name="ItemWcid">The piece the NPC takes.</param>
/// <param name="ItemName">Its display name (PropertyString 1).</param>
/// <param name="RewardWcid">The weenie class he gives for it.</param>
/// <param name="RewardName">The reward's label.</param>
/// <param name="Emote">The emote id of the row (the SELECT's key).</param>
/// <param name="Source">The row's citation.</param>
public sealed record GearReturnRow(uint ItemWcid, string ItemName, uint RewardWcid, string RewardName, uint Emote, string Source);

/// <summary>
/// The <c>returns</c> block of a quest file: the NPC and the pieces he takes back. It is data read here, not by <c>Quest.cs</c>'s shape (the quest loader skips the block, <c>QuestCatalog.Reader.Quest</c>).
/// </summary>
/// <param name="Quest">The quest file's key.</param>
/// <param name="GiverWcid">The NPC's weenie class.</param>
/// <param name="GiverName">The NPC's label.</param>
/// <param name="Rows">The pieces he takes.</param>
public sealed record GearReturnTable(string Quest, uint GiverWcid, string GiverName, IReadOnlyList<GearReturnRow> Rows)
{
    /// <summary>Where the NPC stands (the quest's first <c>go</c> step), or null when the table was parsed from text alone.</summary>
    public QuestPlace? Place { get; init; }

    /// <summary>The row that takes a piece of <paramref name="wcid"/>, or null.</summary>
    /// <param name="wcid">The piece's weenie class.</param>
    public GearReturnRow? RowFor(uint wcid) => Rows.FirstOrDefault(r => r.ItemWcid == wcid);

    /// <summary>The tables of every quest file that has a <c>returns</c> block, with the NPC's place from the quest.</summary>
    public static IReadOnlyList<GearReturnTable> All => Loaded.Value;

    private static readonly Lazy<IReadOnlyList<GearReturnTable>> Loaded = new(() =>
    {
        var list = new List<GearReturnTable>();
        foreach (var quest in QuestCatalog.All)
        {
            if (QuestCatalog.ResourceText(quest.Key + ".json") is { } text && Parse(text) is { } table)
            {
                list.Add(table with { Place = quest.Steps.FirstOrDefault(s => s.Kind == QuestStepKind.Go)?.Place });
            }
        }

        return list;
    });

    /// <summary>Reads the <c>returns</c> block of a quest file's text, or null when the file has none; a malformed block throws, naming the field.</summary>
    /// <param name="questJson">The quest file's text.</param>
    public static GearReturnTable? Parse(string questJson)
    {
        ArgumentNullException.ThrowIfNull(questJson);
        using var doc = JsonDocument.Parse(questJson);
        if (!doc.RootElement.TryGetProperty("returns", out var block))
        {
            return null;
        }

        var key = Text(doc.RootElement, "key", "$");
        var giver = Object(block, "giver", "$.returns");
        var rows = new List<GearReturnRow>();
        if (!block.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("TODO(m6-returns-file): $.returns.rows is not an array");
        }

        var i = 0;
        foreach (var r in rowsElement.EnumerateArray())
        {
            var path = $"$.returns.rows[{i++}]";
            var item = Object(r, "item", path);
            var reward = Object(r, "reward", path);
            rows.Add(new GearReturnRow(Wcid(item, path + ".item"), Text(item, "name", path + ".item"), Wcid(reward, path + ".reward"), Text(reward, "name", path + ".reward"), UInt(r, "emote", path), Text(r, "source", path)));
        }

        if (rows.Select(r => r.ItemWcid).Distinct().Count() != rows.Count)
        {
            throw new FormatException("TODO(m6-returns-file): $.returns.rows names one piece twice");
        }

        return new GearReturnTable(key, Wcid(giver, "$.returns.giver"), Text(giver, "name", "$.returns.giver"), rows);
    }

    private static JsonElement Object(JsonElement e, string name, string path) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : throw new FormatException($"TODO(m6-returns-file): {path}.{name} is not an object");

    private static string Text(JsonElement e, string name, string path) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : throw new FormatException($"TODO(m6-returns-file): {path}.{name} is not a text");

    private static uint UInt(JsonElement e, string name, string path) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var n) ? n : throw new FormatException($"TODO(m6-returns-file): {path}.{name} is not a whole number");

    private static uint Wcid(JsonElement e, string path) => UInt(e, "wcid", path);
}

/// <summary>A piece the pack holds, with its weenie class (a <see cref="GearCandidate"/> has none).</summary>
/// <param name="Piece">The piece, with its appraisal.</param>
/// <param name="Wcid">Its weenie class id.</param>
public sealed record GearReturnHeld(GearCandidate Piece, uint Wcid);

/// <summary>A piece to give back.</summary>
/// <param name="Guid">The piece.</param>
/// <param name="Wcid">Its weenie class.</param>
/// <param name="Name">Its name.</param>
/// <param name="Row">The table row that takes it.</param>
/// <param name="Why">Why it goes back, with the numbers.</param>
public sealed record GearReturnItem(uint Guid, uint Wcid, string Name, GearReturnRow Row, string Why);

/// <summary>An attuned piece the plan keeps, and why.</summary>
/// <param name="Guid">The piece.</param>
/// <param name="Name">Its name.</param>
/// <param name="Reason">Why it is kept.</param>
public sealed record GearReturnKept(uint Guid, string Name, string Reason)
{
    /// <summary>M6 16 E5e6: the named line of the guard that kept it (<see cref="GearReturn.UnlitLine"/>), or null when no guard did. The trip logs it once a piece a visit.</summary>
    public string? Guard { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"return: kept {Name} ({Reason})";
}

/// <summary>What <see cref="GearReturn.Plan"/> decided: the pieces to give back and the attuned pieces it keeps, each named with its reason.</summary>
/// <param name="Returns">The pieces to give back.</param>
/// <param name="Kept">The attuned pieces kept.</param>
public sealed record GearReturnPlan(IReadOnlyList<GearReturnItem> Returns, IReadOnlyList<GearReturnKept> Kept)
{
    /// <summary>The plan once the NPC is known to be out of reach: every piece to give back is kept, named with the reason.</summary>
    /// <param name="giver">The NPC's name.</param>
    /// <param name="why">Why he could not be reached.</param>
    public GearReturnPlan Unreachable(string giver, string why) =>
        new([], [.. Kept, .. Returns.Select(r => new GearReturnKept(r.Guid, r.Name, $"{giver} is out of reach: {why}"))]);

    /// <summary>
    /// M6 16 E5e6: the plan with every piece of <paramref name="guarded"/> kept, never given: a piece the guard <see cref="GearReturn.UnlitGuard"/> kept at one table of a visit is given by no later table of the same visit, whatever
    /// that table's plan reads by then (a worn piece's spells may light between two tables). The same plan when it gives none of them.
    /// </summary>
    /// <param name="guarded">The pieces the guard kept earlier this visit.</param>
    public GearReturnPlan Withheld(IReadOnlySet<uint> guarded)
    {
        ArgumentNullException.ThrowIfNull(guarded);
        return Returns.Any(r => guarded.Contains(r.Guid))
            ? new([.. Returns.Where(r => !guarded.Contains(r.Guid))], [.. Kept, .. Returns.Where(r => guarded.Contains(r.Guid)).Select(r => new GearReturnKept(r.Guid, r.Name, $"the guard {GearReturn.UnlitGuard} kept it earlier this visit: it is not given back in the same visit"))])
            : this;
    }
}

/// <summary>
/// M6 2 d: the return of an attuned piece. ACE refuses to drop an attuned piece (<c>Player_Inventory.cs</c>:1389-1393, :866-871) and a sale of one pays nothing the quest pieces are worth (Value 0, <c>Player_Commerce.cs</c>:268-280 @95ef426d),
/// but an NPC's Give emote takes it (<c>GiveObjectToNPC</c>, <c>Player_Inventory.cs</c>:3395-3443 has no attuned check; only <c>GiveObjectToPlayer</c> :3262-3266 has). So an attuned piece in the pack that the equip pass did not wear is given back
/// to the NPC whose <see cref="GearReturnTable"/> names it, when it is
/// <list type="bullet">
/// <item>outgrown: a worn piece in its slot (its coverage, or its jewellery location) scores higher by <see cref="GearScore"/>; or</item>
/// <item>blocked: ACE's coverage rule keeps it off while a worn piece covers its parts (<c>Creature_Equipment.cs</c>:50-58, 93-105; the Pathwarden robe's 0x17F00 holds the feet, so the sollerets cannot go on).</item>
/// </list>
/// Never a piece that has a free slot (the equip pass decides those), never a caster (the floor keeps two), never a piece with no row, and never a chest-covering piece while nothing worn covers the chest (the Pathwarden robe is the
/// climber's only chest cover until something better is worn). The act is the trip's, with check, act, check (M5 D141): the piece held and the NPC in reach, the Give, the piece gone and the reward arrived (<see cref="Failure"/>).
/// <para>
/// M6 16 E5e6, the guard <see cref="UnlitGuard"/> (<c>TODO(engine-gear-return-unlit-guard)</c>, docs/automation-engine.md section 6): a Give cannot be undone, so a piece is not given back for a worn piece whose magic is off for
/// her. When every worn piece that scores above it has an activation requirement she does not meet (<see cref="GearScoreResult.SpellsOff"/>: her Arcane Lore under its ItemDifficulty, or a skill limit; such a piece is scored on its
/// armour alone since E5e5, where it scored 0), the piece is kept and named (<see cref="UnlitLine"/>), and is neither outgrown nor blocked this visit. A worn piece whose spells are on, or that has no such requirement, marks it
/// outgrown as before.
/// </para>
/// </summary>
public static class GearReturn
{
    /// <summary>M6 16 E5e6: the name of the guard that keeps an attuned piece a worn piece beats only with its magic off for her.</summary>
    public const string UnlitGuard = "gear-return-unlit";

    /// <summary>M6 16 E5e6: the guard's line, logged by the trip once a piece a visit.</summary>
    /// <param name="attuned">The attuned piece kept.</param>
    /// <param name="worn">The worn piece that scores above it with its magic off.</param>
    public static string UnlitLine(string attuned, string worn) => $"gear: return: '{attuned}' kept: the worn '{worn}' beats it only on armour, its magic is off for her (guard: {UnlitGuard})";

    /// <summary>The attuned pieces of <paramref name="held"/> to give back and to keep.</summary>
    /// <param name="table">The NPC and the pieces he takes.</param>
    /// <param name="held">The pieces in the packs, not worn.</param>
    /// <param name="worn">The pieces worn now.</param>
    /// <param name="context">The character and the ground, for the scores.</param>
    public static GearReturnPlan Plan(GearReturnTable table, IReadOnlyList<GearReturnHeld> held, IReadOnlyList<WornPiece> worn, GearContext context)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(context);
        var returns = new List<GearReturnItem>();
        var kept = new List<GearReturnKept>();
        var chestWorn = GearFloor.ChestPiece(worn);
        foreach (var h in held)
        {
            var c = h.Piece;
            if ((c.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                continue; // the floor keeps two casters (GearFloor): a caster is never given back
            }

            var row = table.RowFor(h.Wcid);
            if (c.Appraisal is not { IsNotFound: false })
            {
                if (row is not null)
                {
                    kept.Add(new GearReturnKept(c.Guid, c.Name, "not appraised: whether it is attuned is unread"));
                }

                continue;
            }

            if (!GearScore.Attuned(c.Appraisal))
            {
                continue; // an ordinary piece is the sale's and the keep-aside's
            }

            if (row is null)
            {
                kept.Add(new GearReturnKept(c.Guid, c.Name, $"no Give row of {table.GiverName} takes it"));
                continue;
            }

            var coversChest = (c.ValidLocations & GearFloor.ChestArmor) != 0 || (c.Coverage & GearFloor.OuterwearChest) != 0;
            if (coversChest && chestWorn is null)
            {
                kept.Add(new GearReturnKept(c.Guid, c.Name, "it covers the chest and nothing worn does: it is the only chest cover"));
                continue;
            }

            var rivals = worn.Where(w => c.Coverage != 0 ? (w.Coverage & c.Coverage) != 0 : (w.Location & c.ValidLocations) != 0).ToList();
            var own = GearScore.Of(c, context);
            var scored = rivals.Select(w => (Piece: w, Score: GearScore.Of(w, context))).OrderByDescending(p => p.Score.Total).ToList();
            var best = scored.FirstOrDefault();
            if (best.Piece is not null && best.Score.Total > own.Total && best.Score.SpellsOff is { } spellsOff)
            {
                // M6 16 E5e6, the guard: the worn piece it loses to has its magic off for her. A worn piece whose magic is not off and that scores above it too marks it outgrown, as before E5e5 (which scored the unlit one 0);
                // with none, it is kept this visit: not outgrown, and not "blocked" by that piece either, for the Give cannot be undone.
                var lit = scored.FirstOrDefault(p => p.Score.SpellsOff is null && p.Score.Total > own.Total);
                if (lit.Piece is null)
                {
                    kept.Add(new GearReturnKept(c.Guid, c.Name, string.Create(CultureInfo.InvariantCulture, $"the worn '{best.Piece.Name}' 0x{best.Piece.Guid:X8} scores {best.Score.Total:0.##} against its {own.Total:0.##} with its spells off for her ({spellsOff}): it is not given back to {table.GiverName} for {row.RewardName} ({row.RewardWcid})")) { Guard = UnlitLine(c.Name, best.Piece.Name) });
                    continue;
                }

                best = lit;
            }

            if (best.Piece is not null && best.Score.Total > own.Total)
            {
                returns.Add(new GearReturnItem(c.Guid, h.Wcid, c.Name, row, string.Create(CultureInfo.InvariantCulture, $"outgrown: the worn '{best.Piece.Name}' 0x{best.Piece.Guid:X8} scores {best.Score.Total:0.##} against its {own.Total:0.##}")));
            }
            else if (SelfEquipJudge.FreeLocation(c, worn, out var blocker) is null)
            {
                returns.Add(new GearReturnItem(c.Guid, h.Wcid, c.Name, row, "blocked: " + blocker));
            }
            else
            {
                kept.Add(new GearReturnKept(c.Guid, c.Name, "it has a free slot: the equip pass wears it, or says why not"));
            }
        }

        return new GearReturnPlan(returns, kept);
    }

    /// <summary>
    /// The check after a Give: null when the piece left the pack and the reward arrived, else why not (the piece is then kept and named).
    /// </summary>
    /// <param name="item">The piece given.</param>
    /// <param name="pieceStillHeld">True when the piece is still in the pack.</param>
    /// <param name="rewardBefore">The reward's stack count held before the Give.</param>
    /// <param name="rewardAfter">The reward's stack count held after.</param>
    public static string? Failure(GearReturnItem item, bool pieceStillHeld, int rewardBefore, int rewardAfter)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (pieceStillHeld)
        {
            return "the Give was not taken: the piece is still in the pack";
        }

        return rewardAfter > rewardBefore
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"the piece left the pack but no {item.Row.RewardName} ({item.Row.RewardWcid}) arrived (held {rewardBefore} -> {rewardAfter})");
    }
}

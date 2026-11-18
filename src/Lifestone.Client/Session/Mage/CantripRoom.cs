using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 14 keep every great cantrip (the owner, 2026-11-15): one piece of sell loot she carries in her main pack, as the make-room rule reads it (<see cref="CantripRoom.SellLoot"/>): a piece the rule book's verdict sells and no
/// keep of the field stands on.
/// </summary>
/// <param name="Guid">The item's object id.</param>
/// <param name="Name">The item's name, for the log.</param>
/// <param name="Value">The Value of the whole stack.</param>
/// <param name="Burden">The burden of the whole stack.</param>
/// <param name="Rule">The pack rule that sells it (the verdict's row: <c>gear-loot</c>, <c>trade-goods</c>, <c>scrolls</c>, a components row and the like).</param>
public readonly record struct CarriedLoot(uint Guid, string Name, long Value, long Burden, string Rule = "gear-loot")
{
    /// <summary>The value per burden unit: infinite for a weightless item with a value, 0 for one without.</summary>
    public double ValuePerBurden => Value <= 0 ? 0 : Burden <= 0 ? double.PositiveInfinity : (double)Value / Burden;
}

/// <summary>M6 14 keep every great cantrip: one drop the loot plan makes room with: the sell loot dropped and the great-cantrip piece it makes room for (or, M6 14 hour load, the better sell loot it is swapped for).</summary>
/// <param name="Loot">The sell loot dropped.</param>
/// <param name="ForGuid">The great-cantrip piece's guid, or the swap's find's.</param>
/// <param name="ForName">The great-cantrip piece's name, or the swap's find's.</param>
/// <param name="Swap">M6 14 hour load (the owner, 2026-11-16): true when the drop is a swap's: the find is sell loot of a higher value per burden.</param>
/// <param name="ForRatio">M6 14 hour load: the swap's find's value per burden (0 for a great cantrip's drop).</param>
/// <param name="BySlot">M6 14 (the slots): true when the swap's measure is the value (the slots bind, the burden does not).</param>
/// <param name="ForValue">M6 14 (the slots): the swap's find's value.</param>
/// <param name="Want">M6 14 want room: true when the drop makes room for the item the fight's quest wants (<see cref="MageLootRules.IsQuestWant"/>), which outranks every sell loot line.</param>
public readonly record struct RoomDrop(CarriedLoot Loot, uint ForGuid, string ForName, bool Swap = false, double ForRatio = 0, bool BySlot = false, long ForValue = 0, bool Want = false)
{
    /// <summary>The line the drop logs before it is sent: what, its value, burden and value per burden, and why.</summary>
    public string Reason => Want
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"'{Loot.Name}' 0x{Loot.Guid:X8} value {Loot.Value} burden {Loot.Burden} v/b {CantripRoom.Ratio(Loot.ValuePerBurden)} (pack rule {Loot.Rule}: sell loot, the lowest value per burden she carries) to make room for the wanted item '{ForName}' 0x{ForGuid:X8} (m6-want-room: a quest's want outranks every sell loot line)")
        : Swap && BySlot
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"'{Loot.Name}' 0x{Loot.Guid:X8} value {Loot.Value} burden {Loot.Burden} v/b {CantripRoom.Ratio(Loot.ValuePerBurden)} (pack rule {Loot.Rule}: sell loot, the lowest value she carries, under the find's {ForValue}: the slots bind) to swap for the better sell loot '{ForName}' 0x{ForGuid:X8} (m6-hour-load)")
        : Swap
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"'{Loot.Name}' 0x{Loot.Guid:X8} value {Loot.Value} burden {Loot.Burden} v/b {CantripRoom.Ratio(Loot.ValuePerBurden)} (pack rule {Loot.Rule}: sell loot, the lowest value per burden she carries, under the find's {CantripRoom.Ratio(ForRatio)}) to swap for the better sell loot '{ForName}' 0x{ForGuid:X8} (m6-hour-load)")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"'{Loot.Name}' 0x{Loot.Guid:X8} value {Loot.Value} burden {Loot.Burden} v/b {CantripRoom.Ratio(Loot.ValuePerBurden)} (pack rule {Loot.Rule}: sell loot, the lowest value per burden she carries) to make room for the great cantrip '{ForName}' 0x{ForGuid:X8}");
}

/// <summary>M6 14 keep every great cantrip: a great-cantrip piece the plan could not make room for, with the reason (the loud line's).</summary>
/// <param name="Guid">The piece's guid.</param>
/// <param name="Name">The piece's name.</param>
/// <param name="Burden">The piece's burden.</param>
/// <param name="Verdict">What left it: <see cref="MageLootVerdict.Load"/> or <see cref="MageLootVerdict.Slots"/>.</param>
/// <param name="Why">Why no room could be made.</param>
/// <param name="Want">M6 14 want room: true for the item the fight's quest wants (its line is an error).</param>
public readonly record struct RoomShort(uint Guid, string Name, long Burden, MageLootVerdict Verdict, string Why, bool Want = false);

/// <summary>
/// M6 14 keep every great cantrip (the owner's ruling, 2026-11-15: asked whether she was looting and saving all major cantrips then a ruling that these must be kept; live ladder27-29: of 14 Major-cantrip pieces seen from 01:46 UTC she took 2, the other 12 were left on the
/// corpse over the load line while her pack held 17,664 of 17,850 in ordinary sell loot). A great-cantrip piece (<see cref="MageLootItem.GreatCantrip"/>) outranks every piece of sell loot: when the load line or the slots would refuse it, she
/// makes room by dropping, from her main pack, the sell loot with the lowest value per burden first, until the piece fits under the line and a slot is free (<see cref="MageLootRules.Plan"/> plans it; the fight's loot step drops, checks and
/// takes). This class reads what she carries that may be dropped (<see cref="SellLoot"/>) and says what fills the pack when nothing may (<see cref="PackFill"/>).
/// </summary>
/// <remarks>
/// <para><b>What may be dropped</b> (M6 16 E5c2). Only a main-pack item, not worn, that the rule book's verdict sells (<see cref="SaleVerdict.Of"/>: the one answer the town trip's sale gives, docs/pack-rules.md), and on which
/// none of the field's own keeps stands (<see cref="FieldKeep"/>, the one place they are named, each with its reason). Before this slice the rule had a list of its own (<c>WhyKept</c>) and then asked the table's middle part.
/// What changed with the move, by name: a scroll the every-spell rule keeps is no longer sell loot here; a component the rule book sells (a pea, a scarab of a level her book does not hold, an account-formula component while
/// every focus is held) may be dropped for room, where every component was kept; a piece of the leather set is kept only while the swap is still to come, as the sale keeps it; an item whose drop was tried before is not sell
/// loot again; a Bread or an Arrow (a class ACE refuses to buy) is no sell loot; a caller that gave no caster, stone or scroll rule now reads the session's (it kept every caster, stone and scroll before).</para>
/// <para><b>The drop.</b> A player's own drop from the pack (GameAction 0x001B, the pack rules' junk drop, <c>ResupplyRun.DropJunk</c>): ACE puts it on the ground at her feet, where it rots in 5 minutes (WorldObject_Decay.cs:18 @95ef426d).</para>
/// </remarks>
public static class CantripRoom
{
    /// <summary>ACE's PropertyBool Retained (91): ACE refuses the sale of a retained item (Player_Commerce.cs 268-273 @95ef426d); the sale keeps it, so the drop does too.</summary>
    public const uint RetainedBool = 91;

    /// <summary>The heaviest pieces <see cref="PackFill"/> names.</summary>
    public const int HeaviestNamed = 5;

    /// <summary>A value per burden as the log prints it ("-" for a weightless item).</summary>
    /// <param name="ratio">The ratio.</param>
    public static string Ratio(double ratio) => double.IsPositiveInfinity(ratio) ? "-" : ratio.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// M6 14 useful cantrips (the owner's ruling, 2026-11-16: keep only the useful cantrips, meaning basic attributes or skills she has trained, specialized or plans to train; earlier: she only keeps amazing items or major cantrips): THE predicate
    /// of a great piece, used everywhere one is decided: true when the item's appraisal casts a Major, Epic or Legendary cantrip (<see cref="KeepAsideRule.IsGreat"/>) of any family that <see cref="CantripJudge"/> calls useful: it raises a
    /// basic attribute, or a skill she holds trained or specialised now (read live from <paramref name="state"/>), or a skill <paramref name="plan"/> trains; a cantrip whose target cannot be read is kept, loudly
    /// (<see cref="CantripJudge.UnreadTodo"/>). False with no appraisal, or when every great cantrip is not useful: the piece is then ordinary sell loot, judged by its value per burden like any other item. Only these pieces go to the review
    /// pack (<see cref="PackJob.Review"/>; the owner's ruling, 2026-11-16: only what she judges the owner will want to see goes in that side pack), outrank sell loot at the corpse (<see cref="MageLootItem.GreatCantrip"/>) and are never dropped
    /// (<see cref="FieldKeep"/>).
    /// </summary>
    /// <param name="state">The session's read model (its appraisals, her skills, the item's name for the log).</param>
    /// <param name="guid">The item.</param>
    /// <param name="spellName">The name of a spell id (the SpellTable's), or null for an id it does not know.</param>
    /// <param name="plan">The skills her plan trains; null: <see cref="CantripPlan.Mage"/>.</param>
    /// <param name="log">Writes the decision line (<see cref="CantripVerdictLog.Once"/>: each piece's verdicts once), or null for none.</param>
    /// <param name="where">Where the decision is made, for the line.</param>
    public static bool IsUsefulGreatPiece(GameState state, uint guid, Func<uint, string?> spellName, CantripPlan? plan = null, Action<string>? log = null, string where = "the packs")
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spellName);
        if (!state.Appraisals.TryGet(guid, out var appraisal))
        {
            return false;
        }

        var verdicts = CantripJudge.Of(state, plan).GreatOf(appraisal, spellName);
        if (log is not null && CantripVerdictLog.Once(guid, state.Objects.TryGet(guid, out var o) ? o.Name : "?", verdicts, where) is { } line)
        {
            log(line);
        }

        return verdicts.Exists(v => v.Keeps);
    }

    /// <summary>The same predicate with the spell names from the SpellTable's <paramref name="spells"/>.</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="guid">The item.</param>
    /// <param name="spells">The SpellTable's spells (their names).</param>
    /// <param name="plan">The skills her plan trains; null: <see cref="CantripPlan.Mage"/>.</param>
    /// <param name="log">Writes the decision line, or null for none.</param>
    /// <param name="where">Where the decision is made, for the line.</param>
    public static bool IsUsefulGreatPiece(GameState state, uint guid, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo> spells, CantripPlan? plan = null, Action<string>? log = null, string where = "the packs")
    {
        ArgumentNullException.ThrowIfNull(spells);
        return IsUsefulGreatPiece(state, guid, id => spells.TryGetValue(id, out var info) ? info.Name : null, plan, log, where);
    }

    /// <summary>The old name of <see cref="IsUsefulGreatPiece(GameState, uint, IReadOnlyDictionary{uint, Lifestone.Data.Magic.SpellInfo}, CantripPlan, Action{string}, string)"/>, a thin call to it (M6 14 useful cantrips).</summary>
    /// <param name="state">The session's read model (its appraisals).</param>
    /// <param name="guid">The item.</param>
    /// <param name="spells">The SpellTable's spells (their names).</param>
    public static bool IsGreatPiece(GameState state, uint guid, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo> spells) => IsUsefulGreatPiece(state, guid, spells);

    /// <summary>The old name of <see cref="IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/>, a thin call to it (M6 14 useful cantrips).</summary>
    /// <param name="state">The session's read model (its appraisals).</param>
    /// <param name="guid">The item.</param>
    /// <param name="spellName">The name of a spell id, or null for an id it does not know.</param>
    public static bool IsGreatPiece(GameState state, uint guid, Func<uint, string?> spellName) => IsUsefulGreatPiece(state, guid, spellName);

    /// <summary>The first of the field's keeps: a wearable or a caster she has not appraised.</summary>
    public const string KeepUnappraised = "not appraised: never dropped blind";

    /// <summary>The second: a piece that casts a useful great cantrip of any family.</summary>
    public const string KeepGreatCantrip = "a useful great cantrip (any family)";

    /// <summary>The third: a drink she uses.</summary>
    public const string KeepDrink = "a drink she uses: never dropped in the field";

    /// <summary>
    /// M6 16 E5c2: the field's own keeps, in one place: why the make-room rule never drops an item the rule book's verdict sells, or null when it may. They are the deliberate differences between what the town sells and what
    /// she may leave on the ground between two visits; everything else is the verdict's.
    /// <list type="number">
    /// <item><see cref="KeepUnappraised"/>: a caster or a wearable (<see cref="KeepAsideRule.NeedsAppraisal"/>) the session holds no appraisal of. The owner's cantrip rule: it may cast a great cantrip, and a drop cannot be
    /// taken back. The rule book says sell (<c>gear-loot</c>) because the town trip appraises every such piece before its sale; the field has no such step.</item>
    /// <item><see cref="KeepGreatCantrip"/>: an appraised caster or wearable that casts a Major, Epic or Legendary cantrip <see cref="CantripJudge"/> calls useful, of any family, whatever keep-aside rule is in force on the
    /// ground. The owner's standing rule: such a piece is never sold or dropped. The verdict keeps it by its keep-aside row only where the ground's rule names the family (and never asks that row of a caster). This is the
    /// keeper question, the predicate of <see cref="IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/> (the judge's <c>Keeps</c>), never the wear pick's question of whether the
    /// cantrip would light on her body (<c>GearScore.GreatOn</c>, M6 16 E5e5): a keeper that cannot light for her yet is still never dropped.</item>
    /// <item><see cref="KeepDrink"/>: a drink the vitals rules drink (<c>ResupplyRun.DrinkInUse</c>), even a stack the pack budget would sell (<c>drinks-surplus</c>). The budget outside a trip is measured to now, part of
    /// the way between two visits: it under-reads what she drinks before the next one, and a drink is what keeps her alive on the way to it. The town sells the surplus; the field drops none.</item>
    /// </list>
    /// </summary>
    /// <param name="guid">The item's guid.</param>
    /// <param name="item">The item.</param>
    /// <param name="book">The verdict's context (its appraisals, its cantrip judge, its spell names).</param>
    public static string? FieldKeep(uint guid, WorldObject item, SaleContext book)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(book);
        if (KeepAsideRule.NeedsAppraisal(item.ItemType))
        {
            if (book.Appraisal(guid) is not { IsNotFound: false } appraisal)
            {
                return KeepUnappraised;
            }

            if (book.Cantrips.GreatOf(appraisal, book.SpellName).Exists(v => v.Keeps))
            {
                return KeepGreatCantrip;
            }
        }

        return ResupplyRun.DrinkInUse(item.Weenie.WeenieClassId) ? KeepDrink : null;
    }

    /// <summary>
    /// The sell loot she carries in her main pack that the make-room rule may drop (see the class remarks), in the main pack's order: every item, not worn, that <paramref name="book"/>'s verdict sells and no keep of the field
    /// stands on (<see cref="FieldKeep"/>). Allocates one list.
    /// </summary>
    /// <param name="state">The session's read model (the inventory and the objects).</param>
    /// <param name="book">The verdict's context: the session's (<see cref="SaleContext.For"/>), or a fight's over it (<see cref="FieldBook"/>).</param>
    /// <param name="census">M6 14 (the slots): when given, every main-pack item is counted by why it is or is not sell loot (the field's keep, or the pack rule and its action), for the log.</param>
    public static List<CarriedLoot> SellLoot(GameState state, SaleContext book, Dictionary<string, int>? census = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(book);
        var list = new List<CarriedLoot>();
        foreach (var entry in state.Self.Inventory.Pack)
        {
            if (entry.ContainerType != 0 || !state.Objects.TryGet(entry.Guid, out var o) || o.Weenie.WielderId is { } wielder && wielder != 0)
            {
                continue;
            }

            var answer = SaleVerdict.Of(entry.Guid, o, false, book);
            if (answer.Action != PackAction.Sell)
            {
                Count(census, "pack rule " + answer.Rule + " (" + answer.Action.ToString().ToLowerInvariant() + ")");
                continue;
            }

            if (FieldKeep(entry.Guid, o, book) is { } kept)
            {
                Count(census, kept);
                continue;
            }

            // M6 14 (live ladder42 19:51:19 UTC): every item the pack rules sell is sell loot (gear, trade goods, scrolls the scroll rule leaves, essences, healing kits, salvage), not gear and trade goods alone
            list.Add(new CarriedLoot(entry.Guid, o.Name, o.Weenie.Value ?? 0, o.Weenie.Burden ?? 0, answer.Rule));
            Count(census, "sell loot");
        }

        return list;
    }

    /// <summary>
    /// M6 16 E5c2: the verdict's context for a fight's make-room rule: the session's (<see cref="SaleContext.For"/>) with what the fight itself decides by named over it, so each stays the answer the fight gave before: its
    /// keep-aside rule and spell names (its loot thresholds'), its scroll hook asked first (the scroll rule and the fight's quest wants), and, when given, the gear keeps it worked out and logged for this corpse (the casters
    /// her gear rules need, the stones the kit keeps).
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="rules">The fight's loot rules (their thresholds' keep-aside rule and scroll hook, their spell names).</param>
    /// <param name="keeps">The gear keeps the fight worked out (<see cref="GearKeeps"/>), or null: the session's own, worked out when first asked.</param>
    public static SaleContext FieldBook(GameState state, HuntingGround ground, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo> spells, MageLootRules rules, (Func<uint, string?> Casters, Func<uint, bool> Stones, List<CasterKeepVerdict> Verdicts)? keeps = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        var session = SaleContext.For(state, ground, state.Self.Name, spells);
        var book = session with
        {
            KeepAside = rules.Thresholds.KeepAside,
            SpellName = rules.SpellName,
            ScrollKeep = rules.Thresholds.KeepScroll is { } fights ? item => fights(item) ?? session.ScrollKeep(item) : session.ScrollKeep,
        };
        if (keeps is not { } made)
        {
            return book;
        }

        var kept = new HashSet<uint>(made.Verdicts.Where(v => v.KeptFor is not null).Select(v => v.Guid));
        return book with { CasterNeed = made.Casters, StoneKept = made.Stones, FloorCaster = kept.Contains };
    }

    private static void Count(Dictionary<string, int>? census, string why)
    {
        if (census is not null)
        {
            census[why] = census.TryGetValue(why, out var n) ? n + 1 : 1;
        }
    }

    /// <summary>
    /// M6 14 (the owner, 2026-11-16): the gear rules' keeps over what she carries now: the casters her gear rules need (<see cref="CasterKeep.Decide"/>, each with its reason) and the mana stones the kit keeps (<see cref="ItemManaKeeper.Kept"/>;
    /// a stone not appraised is kept). The judges are the session's for each element (<see cref="Quartermaster.Wardrobe.KeepJudges"/>: the useful cantrips in force for her, the ground's damage types, the ladder's caster terms of that element).
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="ground">The ground (the plan's element, its damage types).</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="castChance">The cast chance a bolt level must reach to count as cast (the fight's).</param>
    public static (Func<uint, string?> Casters, Func<uint, bool> Stones, List<CasterKeepVerdict> Verdicts) GearKeeps(GameState state, HuntingGround ground, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo> spells, double castChance)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        string? Name(uint id) => spells.TryGetValue(id, out var info) ? info.Name : null;
        var plan = Quartermaster.Wardrobe.For(state).ElementOn(ground, spells, out _, castChance); // M6 16 E5e3: the one element every judge reads, the fight's own choice on the ground, at the chance this keep was asked with
        var verdicts = CasterKeep.Decide(state, spells, Quartermaster.Wardrobe.For(state).KeepJudges(ground, spells), plan, castChance, g => IsUsefulGreatPiece(state, g, Name), ElementsUsed.For(state).Used); // M6 16 E5e1, E5e2: the session's one wear judge, the element the keep's own question
        var byGuid = verdicts.ToDictionary(v => v.Guid, v => v.KeptFor);
        var stones = WornManaRefill.StonesOf(state);
        var kept = ItemManaKeeper.Kept(stones, _ => false);
        var read = stones.Select(s => s.Guid).ToHashSet();
        return (g => byGuid.TryGetValue(g, out var why) ? why : "not read when the keeps were worked out: kept", g => !read.Contains(g) || kept.Contains(g), verdicts);
    }

    /// <summary>M6 14 (the slots): the census as the log prints it: the counts by reason, the most first.</summary>
    /// <param name="census">The census <see cref="SellLoot"/> filled.</param>
    public static string CensusLine(IReadOnlyDictionary<string, int> census)
    {
        ArgumentNullException.ThrowIfNull(census);
        return string.Join(", ", census.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} {kv.Value}")));
    }

    /// <summary>
    /// M6 14 hour load: the burden of the sell loot she carries (<see cref="SellLoot"/> over the session's context on <paramref name="ground"/> for <paramref name="character"/>): the Strength floor leaves it out (the trip
    /// sells it). M6 16 E5c2: a caster her gear rules do not need, a scroll no rule keeps and a mana stone beyond the kit's keep are in it now (with no rule to ask, this read kept every caster, scroll and stone).
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="character">The character's name, or null.</param>
    /// <param name="spells">The SpellTable's spells (their names).</param>
    public static long SellLootBurden(GameState state, HuntingGround ground, string? character, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);
        return SellLoot(state, SaleContext.For(state, ground, character, spells)).Sum(c => c.Burden);
    }

    /// <summary>
    /// What fills the pack, for the loud line when no room can be made: the burden and the capacity, then the worn pieces, the sell loot the rule may drop, the kept-aside pieces and the rest (supplies, kit, components and the other keeps),
    /// each as a count and a burden, and the <see cref="HeaviestNamed"/> heaviest held items that may not be dropped, by name and burden.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="sellLoot">The sell loot the rule may drop (<see cref="SellLoot"/>).</param>
    public static string PackFill(GameState state, IReadOnlyCollection<CarriedLoot> sellLoot)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sellLoot);
        var load = Fighter.LootContext.Of(state);
        var droppable = new HashSet<uint>(sellLoot.Select(s => s.Guid));
        int worn = 0, aside = 0, rest = 0;
        long wornBurden = 0, asideBurden = 0, restBurden = 0;
        var heavy = new List<(string Name, long Burden)>();
        foreach (var e in state.Self.Inventory.Equipped)
        {
            if (state.Objects.TryGet(e.Guid, out var o))
            {
                worn++;
                wornBurden += o.Weenie.Burden ?? 0;
            }
        }

        foreach (var item in PackLayout.Items(state))
        {
            if (droppable.Contains(item.Guid) || !state.Objects.TryGet(item.Guid, out var o))
            {
                continue;
            }

            long burden = o.Weenie.Burden ?? 0;
            if (SaleMemory.Shared.KeptAside.Contains(item.Guid))
            {
                aside++;
                asideBurden += burden;
            }
            else
            {
                rest++;
                restBurden += burden;
            }

            heavy.Add((o.Name, burden));
        }

        var named = string.Join(", ", heavy.OrderByDescending(h => h.Burden).ThenBy(h => h.Name, StringComparer.Ordinal).Take(HeaviestNamed).Select(h => string.Create(CultureInfo.InvariantCulture, $"'{h.Name}' {h.Burden}")));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"burden {load.Burden} of {load.Capacity}; worn {worn} piece(s) burden {wornBurden}; sell loot she may drop {sellLoot.Count} item(s) burden {sellLoot.Sum(s => s.Burden)}; kept aside {aside} piece(s) burden {asideBurden}; supplies, kit and the other keeps {rest} item(s) burden {restBurden}{(named.Length == 0 ? string.Empty : "; the heaviest she may not drop: " + named)}");
    }
}

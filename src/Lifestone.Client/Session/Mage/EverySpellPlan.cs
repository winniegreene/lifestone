using System.Globalization;
using System.Runtime.CompilerServices;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>The every-spell rule's order (<see cref="EverySpellPlan"/>): the gates' armour lines first, then her bootstrap, then the damage lines, then her buffs and the protections the fights read, then the rest.</summary>
public enum EverySpellTier
{
    /// <summary>
    /// M6 14 gate lines (live 2026-11-17, ladder52: at level 79 her book held Armor Self I alone, the bot's round fell short, and every gate of the ladder read shut on its armour lines): the armour lines the ladder's gates read
    /// (<see cref="EverySpellPlan.GateArmourLines"/>), bought first even while the buff bot casts them for her.
    /// </summary>
    Gate = 0,

    /// <summary>
    /// M6 14 own buffs (the owner, 2026-11-17: she relies on her self-buffs, she does not need the bot; live ladder57: her book held no Focus, Willpower or mastery line, so when the bot's boons lapsed her War Magic fell from 378 to
    /// 326): Focus Self, Willpower Self and the mastery lines of her trained schools (<see cref="EverySpellPlan.BootstrapLines"/>), in the bootstrap's order, right after the gates, whoever casts them for her now. Their level is read
    /// on her own bootstrap (<see cref="EverySpellInput.OwnSkill"/>): a level only the bot's boons let her cast is no level of hers.
    /// </summary>
    Bootstrap = 1,

    /// <summary>Each element's bolt, Harm Other, Drain Health Other and the war shapes the picker offers.</summary>
    Damage = 2,

    /// <summary>Her buffs and the protections the fights read: the armour, protection and defence lines first (her profile's protection group: Armor, Bludgeon Protection, Invulnerability, Impregnability, Magic Resistance), then the rest of her buffs.</summary>
    Buff = 3,

    /// <summary>Every other line a catalog scrivener sells (the bot's lines that are no line of her own profile among them: Sprint, Jumping Mastery).</summary>
    Rest = 4,
}

/// <summary>What the every-spell rule says of one line.</summary>
public enum EverySpellVerdict
{
    /// <summary>Its scroll is bought on this trip and read after (check, act, check: the spellbook's update is the proof).</summary>
    Buy,

    /// <summary>A scroll of it is in the pack: read on this trip, nothing bought.</summary>
    ReadHeld,

    /// <summary>The book holds the target level.</summary>
    Known,

    /// <summary>The spell plan decides the line (<see cref="ScrivenerLearnPlan"/>), or the trip reads the spell already.</summary>
    PlanLine,

    /// <summary>The line's school is not trained.</summary>
    Untrained,

    /// <summary>No level reaches the chance on her standing skill.</summary>
    NoLevel,

    /// <summary>The SpellTable given to the trip lacks a level's spell: <c>TODO(m6-every-spell-table)</c>.</summary>
    NoSpellData,

    /// <summary>No catalog scrivener sells it (the war picker's ring is the loot's).</summary>
    Unsold,

    /// <summary>A read of it was refused earlier this session (ACE's answer): the scroll is kept and never bought again.</summary>
    RefusedBefore,

    /// <summary>ACE would refuse the read (<c>Player.CanReadScroll</c>): not bought.</summary>
    Unreadable,

    /// <summary>Waits: the purse above the reserve does not pay it.</summary>
    Purse,

    /// <summary>Waits: the pack's room on this trip (its free slots above the loot's floor, its burden under the trip's line).</summary>
    Room,
}

/// <summary>A spell's facts from the SpellTable (the dats): the name, the school, the power, and whether it is beneficial (a buff reads the buff bar).</summary>
/// <param name="Name">The spell's name.</param>
/// <param name="School">Its school.</param>
/// <param name="Power">Its power.</param>
/// <param name="Beneficial">SpellFlags.Beneficial.</param>
public readonly record struct EverySpellFact(string Name, MagicSchool School, uint Power, bool Beneficial);

/// <summary>What the spell plan said of one of its lines on this trip (<see cref="ScrivenerLearnPlan.Needs"/>).</summary>
/// <param name="Kind">The plan's answer (the most active of its needs on the line).</param>
/// <param name="LineKind">The catalog line's kind.</param>
/// <param name="Target">The plan's target level (0 when none).</param>
public sealed record EverySpellPlanLine(LearnNeedKind Kind, ScrivenerLineKind LineKind, int Target);

/// <summary>
/// The coin the every-spell rule never spends (<see cref="EverySpellPlan.ReserveOf"/>), derived from the live state and the ground's data, never hand-sized: her supplies at their keeps, the buff bot's fee, the villa (while on), and
/// the trip's planned needs.
/// </summary>
/// <param name="Supplies">The ground's stock and town stock lines at their keeps, at their vendors' prices (the fee's ware left out).</param>
/// <param name="BuffBot">The buff bot's fee line at its keep (from <see cref="BuffBotWire.FeeLevel"/> while the bot applies), else 0.</param>
/// <param name="Villa">The villa's price and first rent with no house, the next rent with one, 0 while the track is off.</param>
/// <param name="Planned">The trip's planned needs: the spell plan's scrolls, the focus and the leather set's missing pieces.</param>
/// <param name="Why">The numbers, for the log.</param>
public sealed record EverySpellReserve(long Supplies, long BuffBot, long Villa, long Planned, string Why)
{
    /// <summary>The whole reserve.</summary>
    public long Total => Supplies + BuffBot + Villa + Planned;

    /// <summary>No reserve.</summary>
    public static EverySpellReserve None { get; } = new(0, 0, 0, 0, "none");
}

/// <summary>The every-spell rule's inputs, read live by the trip (or a test).</summary>
public sealed record EverySpellInput
{
    /// <summary>True when the spellbook holds the spell.</summary>
    public required Func<uint, bool> Knows { get; init; }

    /// <summary>The SpellTable's facts of a spell, or null when the table lacks it.</summary>
    public required Func<uint, EverySpellFact?> Fact { get; init; }

    /// <summary>Her standing skill in a school (<see cref="ScrivenerLearnPlan.Standing"/>: the lines she keeps standing, never what is on her at the read), or null when the school is not trained.</summary>
    public required Func<MagicSchool, uint?> Skill { get; init; }

    /// <summary>The scrivener catalog with its stock (<see cref="ScrivenerCatalog.Stock"/>): the lines, the scriveners' places and rates.</summary>
    public required ScrivenerCatalog Catalog { get; init; }

    /// <summary>True when a scrivener of that weenie class stands in the ground's home vendor view (<see cref="HuntingGround.VendorOf"/>).</summary>
    public required Func<uint, bool> AtHome { get; init; }

    /// <summary>The combat bar (<see cref="Ladder.PlanCastChance"/>).</summary>
    public double CastChance { get; init; } = 0.95;

    /// <summary>The buff bar (<see cref="Ladder.PlanBuffChance"/>), read for a beneficial spell.</summary>
    public double BuffChance { get; init; } = 0.75;

    /// <summary>The spell plan's lines on this trip, by the shop's name (<see cref="EverySpellPlan.StockLine(ScrivenerCatalog, string)"/>).</summary>
    public IReadOnlyDictionary<string, EverySpellPlanLine> PlanLines { get; init; } = new Dictionary<string, EverySpellPlanLine>(StringComparer.Ordinal);

    /// <summary>
    /// True for a line the buff bot casts for her now (<see cref="BuffBotCover.Covers"/>). M6 14 own buffs (the owner, 2026-11-17: she relies on her self-buffs): no reason to skip or defer her own line: it keeps its tier and is
    /// bought as her coin allows, and its pick says so (<see cref="EverySpellPlan.CoveredNote"/>).
    /// </summary>
    public Func<string, bool> Covered { get; init; } = static _ => false;

    /// <summary>
    /// M6 14 own buffs: her bootstrap lines, by the shop's names, in the bootstrap's order (<see cref="EverySpellPlan.BootstrapLines"/>): the <see cref="EverySpellTier.Bootstrap"/> tier, right after the gates. Empty: no such tier.
    /// </summary>
    public IReadOnlyList<string> BootstrapLines { get; init; } = [];

    /// <summary>
    /// M6 14 own buffs: her standing skill in a school on her own bootstrap alone (<see cref="ScrivenerLearnPlan.Standing"/> with the bot's lines not counted), or null when the school is not trained; the level of a
    /// <see cref="EverySpellTier.Bootstrap"/> line is read on it, so a level is one she casts with no bot about. Null (the default): <see cref="Skill"/>.
    /// </summary>
    public Func<MagicSchool, uint?>? OwnSkill { get; init; }

    /// <summary>
    /// M6 14 gate lines: the armour lines the ladder's gates read, by the shop's names (<see cref="EverySpellPlan.GateArmourLines"/>): the first tier, in this order, bought even while the buff bot covers them (a gate reads her book
    /// first, and a round of the bot that falls short leaves her with what she owns).
    /// </summary>
    public IReadOnlyList<string> GateLines { get; init; } = [];

    /// <summary>The damage elements the ground's targets deal (<see cref="ScrivenerLearnPlan.IncomingElements"/>): their protection lines are the fights'.</summary>
    public IReadOnlyList<string> Incoming { get; init; } = [];

    /// <summary>Her coin and the faces of her trade notes.</summary>
    public long Purse { get; init; }

    /// <summary>The coin never spent (<see cref="EverySpellPlan.ReserveOf"/>).</summary>
    public EverySpellReserve Reserve { get; init; } = EverySpellReserve.None;

    /// <summary>The scrolls one place of the trip may take (the free main-pack slots above the loot's floor and a note's split).</summary>
    public int Room { get; init; } = int.MaxValue;

    /// <summary>The burden the trip may add (under the trip's burden line).</summary>
    public long BurdenRoom { get; init; } = long.MaxValue;

    /// <summary>True for a spell whose read was refused earlier this session.</summary>
    public Func<uint, bool> RefusedBefore { get; init; } = static _ => false;

    /// <summary>True when the pack holds a scroll of that weenie class.</summary>
    public Func<uint, bool> Held { get; init; } = static _ => false;

    /// <summary>The spells this trip reads already (the ground's list and the spell plan's): not planned twice.</summary>
    public IReadOnlySet<uint> OnTrip { get; init; } = new HashSet<uint>();
}

/// <summary>One line's answer.</summary>
/// <param name="Line">The line's name.</param>
/// <param name="Tier">Its tier.</param>
/// <param name="Spell">The target level's scroll, or null.</param>
/// <param name="Verdict">What the rule says.</param>
/// <param name="Home">True when a scrivener of the home town sells it.</param>
/// <param name="VendorWcid">The home scrivener's weenie class (0 off home).</param>
/// <param name="Sellers">The scriveners who sell it (off home: the Town Network's choice is the trip's).</param>
/// <param name="Price">The price at the vendor (off home: the cheapest seller's).</param>
/// <param name="Why">The numbers.</param>
public sealed record EverySpellPick(string Line, EverySpellTier Tier, ScrivenerStockSpell? Spell, EverySpellVerdict Verdict, bool Home, uint VendorWcid, IReadOnlyList<uint> Sellers, int Price, string Why)
{
    /// <summary>The verdict as the log's token (kebab case).</summary>
    public string Token => EverySpellPlan.Token(Verdict);
}

/// <summary>The scrolls bought at one vendor (home: the scrivener; off home: the set of scriveners who sell them, a town chosen by the trip).</summary>
/// <param name="Key">The vendor's weenie class at home; the sellers' weenie classes joined by '+' off home.</param>
/// <param name="Home">True at home.</param>
/// <param name="Picks">The scrolls, in the rule's order.</param>
public sealed record EverySpellBatch(string Key, bool Home, IReadOnlyList<EverySpellPick> Picks)
{
    /// <summary>What the batch costs.</summary>
    public long Cost => Picks.Sum(p => (long)p.Price);
}

/// <summary>The rule's answer for every line, with the purse's numbers.</summary>
/// <param name="Picks">Every line, in the rule's order.</param>
/// <param name="Purse">Her coin and note faces.</param>
/// <param name="Reserve">The reserve.</param>
/// <param name="Spent">What the buys cost.</param>
public sealed record EverySpellResult(IReadOnlyList<EverySpellPick> Picks, long Purse, EverySpellReserve Reserve, long Spent)
{
    /// <summary>The purse above the reserve (never under 0).</summary>
    public long Spendable => Math.Max(0, Purse - Reserve.Total);

    /// <summary>The scrolls bought on this trip, in the rule's order.</summary>
    public IReadOnlyList<EverySpellPick> Buys => [.. Picks.Where(p => p.Verdict == EverySpellVerdict.Buy)];

    /// <summary>The lines she can cast and lacks (bought, held to read, or waiting).</summary>
    public int Wanted => Picks.Count(p => p.Verdict is EverySpellVerdict.Buy or EverySpellVerdict.ReadHeld or EverySpellVerdict.Purse or EverySpellVerdict.Room);

    /// <summary>The buys grouped by vendor, each batch in the order of its first scroll.</summary>
    public IReadOnlyList<EverySpellBatch> Batches =>
        [.. Buys.GroupBy(p => p.Home ? p.VendorWcid.ToString(CultureInfo.InvariantCulture) : string.Join('+', p.Sellers.Order()), StringComparer.Ordinal).Select(g => new EverySpellBatch(g.Key, g.First().Home, [.. g]))];

    /// <summary>The count of one verdict.</summary>
    /// <param name="verdict">The verdict.</param>
    public int Count(EverySpellVerdict verdict) => Picks.Count(p => p.Verdict == verdict);

    /// <summary>The summary line's fields.</summary>
    public string Summary => string.Create(
        CultureInfo.InvariantCulture,
        $"purse {Purse} (coin and note faces), reserve {Reserve.Total}, above it {Spendable}; buy {Count(EverySpellVerdict.Buy)} for {Spent} in {Batches.Count} batch(es), read held {Count(EverySpellVerdict.ReadHeld)}, wait purse {Count(EverySpellVerdict.Purse)} room {Count(EverySpellVerdict.Room)}, known {Count(EverySpellVerdict.Known)}, the plan's {Count(EverySpellVerdict.PlanLine)}, refused {Picks.Count(p => p.Verdict is EverySpellVerdict.Untrained or EverySpellVerdict.NoLevel or EverySpellVerdict.NoSpellData or EverySpellVerdict.Unsold or EverySpellVerdict.RefusedBefore or EverySpellVerdict.Unreadable)}");
}

/// <summary>
/// M6 14 (the owner, 2026-11-16: with the money she has, she buys every spell, eventually when affordable): beyond the spell plan (<see cref="ScrivenerLearnPlan"/>) and a job's needs, she learns every spell a scrivener she can reach sells
/// and she can cast. Pure: the trip (<c>ResupplyRun</c>) reads the inputs live and acts on the answer.
/// <list type="bullet">
/// <item><b>Which.</b> Every line of the catalog's scriveners' shops (<see cref="ScrivenerStock"/>: the whole create lists of the 40 scriveners of the catalog's towns; the Town Network's towns are where the V and VI are sold), in a school she has trained,
/// at its highest level whose chance on her standing skill (<see cref="ScrivenerLearnPlan.Standing"/>, the lines she keeps standing) is the plan's: the buff bar (<see cref="Ladder.PlanBuffChance"/>) for a beneficial spell, the combat bar
/// (<see cref="Ladder.PlanCastChance"/>) for the rest. ACE asks no lower level before a higher one (<c>Player.CanReadScroll</c>, Player_Skills.cs:458-471 @95ef426d: the school trained and its current skill at least the power less 50, or a power under
/// 50 or from 300 read by anyone; <c>Scroll.ActOnUse</c>, Scroll.cs:45-127, checks only that and whether the spell is known), so the one level is bought. A line the plan decides keeps the plan's answer; a plan line whose level the
/// book holds, or that the plan leaves to the buff bot or to no caster, is the rule's like any other.</item>
/// <item><b>The order.</b> First the armour lines the ladder's gates read (<see cref="GateArmourLines"/>; M6 14 gate lines: the buff bot's cover does not move them), then her bootstrap (<see cref="BootstrapLines"/>: Focus Self, Willpower Self and her trained schools' masteries; M6 14 own buffs), then the damage lines
/// (<see cref="DamageLines"/>: each element's bolt, Harm Other, Drain Health Other, the war shapes the picker offers); then her buffs and the protections the fights read (<see cref="BuffLines"/>: the armour, protection and defence lines, then the
/// rest of her buffs); then the rest, the cheapest scroll first. M6 14 own buffs (the owner, 2026-11-17: she relies on her self-buffs, she does not need the bot): a line the buff bot casts for her keeps its own place; the bot stays a
/// convenience whose stronger casts still stand over hers.</item>
/// <item><b>What she pays.</b> The purse (her coin and the faces of her trade notes: a scrivener takes a note at its face, <c>TownTripPurse</c>) above the reserve (<see cref="ReserveOf"/>); in the order, each scroll while the purse
/// above the reserve still pays it, else it waits; the price is the vendor's (<see cref="Scrivener.PriceOf"/>: the scroll's Value times his sell rate, ACE <c>Vendor.GetSellCost</c>). Nothing of her kit is sold to pay.</item>
/// <item><b>The pack.</b> At home and on the Town Network's town each, the scrolls bought stay within the room (free slots above the loot's floor) and under the burden line: the rest waits for the next trip. A gate's armour line
/// (<see cref="EverySpellTier.Gate"/>) is held by the room alone: its scroll is read on the trip, so its burden leaves with the read (live 2026-11-17, ladder52: Armor Self VI waited on a burden room of 0 at every trip).</item>
/// <item><b>Never twice.</b> A held scroll is read, not bought; a read refused earlier this session is not tried again, and its scroll is kept (the sale's keep reason).</item>
/// </list>
/// </summary>
public static class EverySpellPlan
{
    /// <summary>The health drain the fight casts (<c>FightScenario.Drain.cs</c>'s line).</summary>
    public const string HealthDrainLine = "Drain Health Other";

    /// <summary>The scroll level the rule keeps, a VI: the catalog's highest (VII is the loot's, D4).</summary>
    private const int TopLevel = 6;

    /// <summary>The protection line of each war element (the SpellTable's names: "Fire Protection Self" cuts fire), the fights' when the ground's targets deal that element.</summary>
    private static readonly Dictionary<string, string> ProtectionOf = new(StringComparer.Ordinal)
    {
        ["fire"] = "Fire Protection Self",
        ["cold"] = "Cold Protection Self",
        ["electric"] = "Lightning Protection Self",
        ["acid"] = "Acid Protection Self",
        ["slash"] = "Blade Protection Self",
        ["pierce"] = "Piercing Protection Self",
        ["bludgeon"] = "Bludgeoning Protection Self",
    };

    /// <summary>
    /// The war shapes the picker offers (<c>FightScenario.WarPick.cs</c>): the profile's war option lines (<see cref="ArenaProfile"/>, <see cref="ProfileRole.WarOption"/>) with an arc left out, as the picker leaves it
    /// (<c>TODO(m5-war-pick-arc-line)</c>); the ring is <see cref="RingSpellIds"/>.
    /// </summary>
    public static IReadOnlyList<string> WarShapeLines { get; } =
        [.. ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarOption && l.Spells.Select(s => WarSpellData.TryGet(s.Id, out var row) ? row : null).FirstOrDefault(r => r is not null) is { Kind: not WarKind.Arc }).Select(l => l.DatRoot)];

    /// <summary>The war picker's ring spells (<see cref="WarSpellData"/>'s ring rows: Cassius' Ring of Fire 1785).</summary>
    public static IReadOnlyList<uint> RingSpellIds { get; } = [.. WarSpellData.Rows.Where(r => r.Kind == WarKind.Ring).Select(r => r.Id)];

    /// <summary>
    /// The shop's name of a catalog line (<see cref="ScrivenerStockSpell.Line"/> of its first spell the stock sells): the catalog names a few lines short ("Spirit Drinker" for "Aura of Spirit Drinker Self"), the shop by the spell's own name.
    /// The catalog's name when the stock sells none of its spells.
    /// </summary>
    /// <param name="catalog">The scrivener catalog and its stock.</param>
    /// <param name="line">The catalog line's name.</param>
    public static string StockLine(ScrivenerCatalog catalog, string line)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(line);
        foreach (var spell in catalog.LineSpells(line))
        {
            if (catalog.Stock.Spell(spell.SpellId) is { } sold)
            {
                return sold.Line;
            }
        }

        return line;
    }

    /// <summary>The shop's name of a profile line (the line of its first spell the stock sells), else its dat root.</summary>
    private static string StockLine(ScrivenerCatalog catalog, ProfileLine line) => line.Spells.Select(s => catalog.Stock.Spell(s.Id)?.Line).FirstOrDefault(l => l is not null) ?? line.DatRoot;

    /// <summary>
    /// M6 14 gate lines: the armour lines the ladder's gates read (<see cref="Ladder.Gates"/>), by the shop's names, in the gates' order (the first gate's lines first): each gate line found in her profile
    /// (<see cref="RungGateRule.LineOf"/>) and kept when the catalog sells it as an armour, protection or item armour line. A gate line of no other kind (the masteries, the attributes) is left to its own tier.
    /// </summary>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="ladder">The ladder.</param>
    public static IReadOnlyList<string> GateArmourLines(ScrivenerCatalog catalog, Ladder ladder)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ladder);
        var armour = catalog.Lines.Where(l => l.Kind is ScrivenerLineKind.Armor or ScrivenerLineKind.Protection or ScrivenerLineKind.ItemArmor).Select(l => StockLine(catalog, l.Name)).ToHashSet(StringComparer.Ordinal);
        return [.. ladder.Gates.SelectMany(g => g.Lines).Select(l => GateLineStockName(catalog, l.Line)).Where(armour.Contains).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>M6 14 gate lines: the shop's name of a gate's line (a profile name, <see cref="GateLine.Line"/>), through her profile's line (<see cref="RungGateRule.LineOf"/>).</summary>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="gateLine">The gate's line name.</param>
    public static string GateLineStockName(ScrivenerCatalog catalog, string gateLine)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(gateLine);
        return StockLine(catalog, RungGateRule.LineOf(BuffProfiles.Climber, gateLine));
    }

    /// <summary>M6 14 own buffs: what a pick of a line the buff bot casts for her says (the owner's direction of 2026-11-17).</summary>
    public const string CoveredNote = "bought even though the bot casts it (owner 2026-11-17: she relies on her self-buffs)";

    /// <summary>The mastery lines of the bootstrap after Focus and Willpower, each with the skill it raises, in the buff pass's order (<see cref="BuffCycle"/>: Creature Enchantment, then War, Item, Life and Mana Conversion).</summary>
    private static readonly (string Line, uint Skill)[] MasteryLines =
    [
        ("Creature Enchantment Mastery Self", MageTemplate.CreatureEnchantment),
        ("War Magic Mastery Self", MageTemplate.WarMagic),
        ("Item Enchantment Mastery Self", MageTemplate.ItemEnchantment),
        ("Life Magic Mastery Self", MageTemplate.LifeMagic),
        ("Mana Conversion Mastery Self", MageTemplate.ManaConversion),
    ];

    /// <summary>
    /// M6 14 own buffs (the owner's bootstrap of 2026-11-06: Focus, Willpower and Creature Enchantment Mastery first, then the rest by the buffed skill; the owner, 2026-11-17: she relies on her self-buffs): her bootstrap lines by the
    /// shop's names, in order: Focus Self, Willpower Self, then the mastery line of each of her casting schools and Mana Conversion that <paramref name="trained"/> says is trained (Creature Enchantment, War Magic, Item Enchantment,
    /// Life Magic, Mana Conversion: the buff pass's order).
    /// </summary>
    /// <param name="catalog">The scrivener catalog and its stock.</param>
    /// <param name="trained">Whether a skill (by id) is trained.</param>
    /// <param name="gearNeedsLore">M6 14 own lore (the owner, 2026-11-17: the bot's Aliester's Boon lapsed and her worn shoes' spells went dark): true when a piece she wears or the gear rules want to wear asks more Arcane Lore than her
    /// unbuffed lore (<see cref="GearNeedsLore"/>): Arcane Enlightenment Self joins the bootstrap right after Focus and Willpower, her gear depends on it.</param>
    public static IReadOnlyList<string> BootstrapLines(ScrivenerCatalog catalog, Func<uint, bool> trained, bool gearNeedsLore = false)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(trained);
        IEnumerable<string> head = gearNeedsLore ? [BuffReach.Bootstrap[0], BuffReach.Bootstrap[1], StandingLore.EnlightenmentLine] : [BuffReach.Bootstrap[0], BuffReach.Bootstrap[1]];
        return [.. head.Concat(MasteryLines.Where(m => trained(m.Skill)).Select(m => m.Line)).Select(l => StockLine(catalog, l)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// M6 14 own lore: true when any of <paramref name="needs"/> (the ItemDifficulty of each piece she wears and of each the gear rules want her to wear) is above <paramref name="unbuffedLore"/> (her Arcane Lore with no enchantment:
    /// her ranks and Focus); <paramref name="why"/> names the highest. The every-spell rule then buys Arcane Enlightenment Self in her bootstrap.
    /// </summary>
    /// <param name="needs">The pieces' names and ItemDifficulty.</param>
    /// <param name="unbuffedLore">Her unbuffed Arcane Lore, or null when unread (false).</param>
    /// <param name="why">The reading.</param>
    public static bool GearNeedsLore(IEnumerable<(string Name, int Difficulty)> needs, int? unbuffedLore, out string why)
    {
        ArgumentNullException.ThrowIfNull(needs);
        var top = needs.Where(n => n.Difficulty > 0).OrderByDescending(n => n.Difficulty).FirstOrDefault();
        if (unbuffedLore is not { } lore || top.Name is null)
        {
            why = unbuffedLore is null ? "her Arcane Lore is unread" : "no piece she wears or wants asks any Arcane Lore";
            return false;
        }

        var over = top.Difficulty > lore;
        why = string.Create(CultureInfo.InvariantCulture, $"'{top.Name}' asks Arcane Lore {top.Difficulty} against her unbuffed {lore}: {(over ? StandingLore.EnlightenmentLine + " joins her bootstrap" : "met unbuffed")}");
        return over;
    }

    /// <summary>The damage lines in the rule's order (the shop's names): the catalog's bolt lines (every element), its attack lines (Harm Other), the fight's health drain, the war shapes.</summary>
    /// <param name="catalog">The scrivener catalog.</param>
    public static IReadOnlyList<string> DamageLines(ScrivenerCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return [.. catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Bolt).Select(l => StockLine(catalog, l.Name))
            .Concat(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Attack).Select(l => StockLine(catalog, l.Name)))
            .Append(HealthDrainLine)
            .Concat(WarShapeLines)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Her buffs and the protections the fights read, in the rule's order: the protections first (the profile's protection lines, <see cref="ArenaProfile.ProtectionLineNames"/> and the item armour lines; the catalog's armour, protection
    /// and item armour lines; the protection line of each element the ground's targets deal), then the profile's other buff lines in the cycle's order, then the catalog's self buff, recovery and caster aura lines. The profile's protection
    /// group holds her defence lines (<see cref="ArenaProfile.ProtectionLineNames"/>: Invulnerability, Impregnability, Magic Resistance), so the defence and armour lines lead her buffs.
    /// </summary>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="incoming">The ground's incoming elements.</param>
    public static IReadOnlyList<string> BuffLines(ScrivenerCatalog catalog, IReadOnlyList<string> incoming)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(incoming);
        var buffs = ArenaProfile.Lines.Where(l => l.IsBuff).ToList();
        return [.. buffs.Where(l => ArenaProfile.RefreshGroupOf(l) == RefreshGroup.Protection).Select(l => StockLine(catalog, l))
            .Concat(catalog.Lines.Where(l => l.Kind is ScrivenerLineKind.Armor or ScrivenerLineKind.Protection or ScrivenerLineKind.ItemArmor).Select(l => StockLine(catalog, l.Name)))
            .Concat(incoming.Select(e => ProtectionOf.GetValueOrDefault(e)).OfType<string>())
            .Concat(buffs.Select(l => StockLine(catalog, l)))
            .Concat(catalog.Lines.Where(l => l.Kind is ScrivenerLineKind.SelfBuff or ScrivenerLineKind.Recovery or ScrivenerLineKind.CasterAura).Select(l => StockLine(catalog, l.Name)))
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The reserve (see <see cref="EverySpellReserve"/>), every number from the ground's data and the live state: each stock line a home vendor sells and each town stock line at its keep and its price (what a refill from empty costs), the buff
    /// bot's fee line at its keep from the fee's level while the bot applies (<see cref="BuffBotFee"/>: four stops an hour, three hours kept), the villa's price and first rent (<see cref="VillaFacts.PricePyreals"/>,
    /// <see cref="VillaFacts.RentPyreals"/>: the guardian's create list) with no house or the next rent with one while the villa track is on, and the trip's planned needs.
    /// </summary>
    /// <param name="ground">The trip's ground.</param>
    /// <param name="level">Her level.</param>
    /// <param name="botOn">True while the buff bot applies to the run.</param>
    /// <param name="villaOn">True while the villa track runs.</param>
    /// <param name="house">Her house's status.</param>
    /// <param name="planned">The trip's planned needs, in pyreals.</param>
    /// <param name="plannedWhy">What they are.</param>
    /// <param name="drinksDitched">M6 audit (fix 6): true while the stamina dance replaced the drinks (<see cref="PackRoomMemory.DrinksDitched"/>): the drink lines are left out.</param>
    public static EverySpellReserve ReserveOf(HuntingGround ground, int level, bool botOn, bool villaOn, HouseOwnership house, long planned, string plannedWhy, bool drinksDitched = false)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var c = CultureInfo.InvariantCulture;
        long home = 0, town = 0, fee = 0;
        int homeLines = 0, townLines = 0, ditched = 0;
        string feeLine = "no fee line";
        foreach (var line in ground.Supplies.Stock)
        {
            if (ground.PriceOf(line.Wcid) is not { } price || price <= 0)
            {
                continue; // carried: no home vendor sells it, no coin buys it
            }

            if (drinksDitched && PackBudgetRule.DrinkBurdens.ContainsKey(line.Wcid))
            {
                ditched++; // M6 audit (fix 6, the owner's decision of 2026-11-19): the stamina dance replaced the drinks: no coin is held back for a line no trip buys (LogisticsCounters.IsSuspended)
                continue;
            }

            if (line.Wcid == BuffBotWire.FeeWcid)
            {
                fee = (long)price * line.Keep;
                feeLine = string.Create(c, $"{line.Keep} {line.Name} at {price}");
                continue;
            }

            home += (long)price * line.Keep;
            homeLines++;
        }

        foreach (var line in ground.TownStock)
        {
            if (drinksDitched && PackBudgetRule.DrinkBurdens.ContainsKey(line.Wcid))
            {
                ditched++;
                continue;
            }

            if (line.Wcid == BuffBotWire.FeeWcid)
            {
                fee = Math.Max(fee, (long)line.Price * line.Keep);
                feeLine = string.Create(c, $"{line.Keep} {line.Name} at {line.Price} in {line.Town}");
                continue;
            }

            town += (long)line.Price * line.Keep;
            townLines++;
        }

        var paysFee = botOn && level >= BuffBotWire.FeeLevel;
        var bot = paysFee ? fee : 0;
        var botWhy = paysFee ? string.Create(c, $"the buff bot's fee {bot} ({feeLine}, from level {BuffBotWire.FeeLevel})") : botOn ? string.Create(c, $"the buff bot's fee 0 (level {level} is under its level {BuffBotWire.FeeLevel})") : "the buff bot's fee 0 (the bot does not apply)";
        var villa = !villaOn ? 0 : house == HouseOwnership.Owned ? VillaFacts.RentPyreals : VillaFacts.PricePyreals + VillaFacts.RentPyreals;
        var villaWhy = !villaOn ? "the villa 0 (its track is off)" : house == HouseOwnership.Owned ? string.Create(c, $"the villa {villa} (owned: the next rent)") : string.Create(c, $"the villa {villa} (the price {VillaFacts.PricePyreals} and the first rent {VillaFacts.RentPyreals}; house {house})");
        var why = string.Create(c, $"supplies {home + town} ({homeLines} home stock line(s) {home}, {townLines} town stock line(s) {town}, each at its keep and price{(ditched > 0 ? string.Create(c, $"; {ditched} drink line(s) left out: the stamina dance replaced them") : string.Empty)}), {botWhy}, {villaWhy}, the trip's planned needs {planned} ({plannedWhy})");
        return new EverySpellReserve(home + town, bot, villa, planned, why);
    }

    /// <summary>The rule's answer (see the class). Allocates: once a trip.</summary>
    /// <param name="input">The inputs.</param>
    public static EverySpellResult Plan(EverySpellInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var stock = input.Catalog.Stock;
        var damage = DamageLines(input.Catalog);
        var buffs = BuffLines(input.Catalog, input.Incoming);
        var evals = new List<(EverySpellPick Pick, int Order, long Burden)>();
        foreach (var line in stock.LineNames)
        {
            var (tier, order, covered) = TierOf(input, line, damage, buffs);
            var pick = Evaluate(input, line, tier, stock.LineSpells(line), covered);
            evals.Add((pick, order, pick.Spell?.Burden ?? 0));
        }

        foreach (var id in RingSpellIds.Where(id => stock.Spell(id) is null))
        {
            var name = input.Fact(id)?.Name ?? string.Create(CultureInfo.InvariantCulture, $"spell {id}");
            evals.Add((new EverySpellPick(name, EverySpellTier.Damage, null, EverySpellVerdict.Unsold, false, 0, [], 0, string.Create(CultureInfo.InvariantCulture, $"the war picker's ring ({name}, {id}): no catalog scrivener sells its scroll (it is on no vendor's create list: the loot's, D4)")), damage.Count, 0));
        }

        var ordered = evals.OrderBy(e => e.Pick.Tier).ThenBy(e => e.Order).ThenBy(e => e.Pick.Tier == EverySpellTier.Rest ? e.Pick.Price : 0).ThenBy(e => e.Pick.Line, StringComparer.Ordinal).ToList();
        var spendable = Math.Max(0, input.Purse - input.Reserve.Total);
        long spent = 0;
        int homeCount = 0, townCount = 0;
        long homeBurden = 0, townBurden = 0;
        var picks = new List<EverySpellPick>(ordered.Count);
        var c = CultureInfo.InvariantCulture;
        foreach (var (pick, _, burden) in ordered)
        {
            if (pick.Verdict != EverySpellVerdict.Buy)
            {
                picks.Add(pick);
                continue;
            }

            if (spent + pick.Price > spendable)
            {
                picks.Add(pick with { Verdict = EverySpellVerdict.Purse, Why = pick.Why + string.Create(c, $"; waits: {pick.Price} is over the {spendable - spent} the purse {input.Purse} holds above the reserve {input.Reserve.Total} after this trip's {spent}") });
                continue;
            }

            var (count, load) = pick.Home ? (homeCount, homeBurden) : (townCount, townBurden);
            var burdenHeld = pick.Tier != EverySpellTier.Gate && load + burden > input.BurdenRoom; // M6 14 gate lines: a gate's scroll is read on the trip, its burden leaves with the read; the slot it takes meanwhile is still counted
            if (count + 1 > input.Room || burdenHeld)
            {
                picks.Add(pick with { Verdict = EverySpellVerdict.Room, Why = pick.Why + string.Create(c, $"; waits for the next trip: {(pick.Home ? "the home walk" : "the town trip")} takes {count} scroll(s) already, the room is {(input.Room == int.MaxValue ? "open" : input.Room.ToString(c))} and the burden {load} + {burden} against {(input.BurdenRoom == long.MaxValue ? "no line" : input.BurdenRoom.ToString(c))}") });
                continue;
            }

            spent += pick.Price;
            var counted = pick.Tier == EverySpellTier.Gate ? 0 : burden;
            if (pick.Home)
            {
                (homeCount, homeBurden) = (homeCount + 1, homeBurden + counted);
            }
            else
            {
                (townCount, townBurden) = (townCount + 1, townBurden + counted);
            }

            picks.Add(pick);
        }

        return new EverySpellResult(picks, input.Purse, input.Reserve, spent);
    }

    /// <summary>The rule's answer for one line alone, before the purse and the room (the sale's keep check asks it of a held scroll's line).</summary>
    /// <param name="input">The inputs.</param>
    /// <param name="line">The line's name (<see cref="ScrivenerStockSpell.Line"/>).</param>
    public static EverySpellPick Judge(EverySpellInput input, string line)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(line);
        var (tier, _, covered) = TierOf(input, line, DamageLines(input.Catalog), BuffLines(input.Catalog, input.Incoming));
        return Evaluate(input, line, tier, input.Catalog.Stock.LineSpells(line), covered);
    }

    /// <summary>
    /// A line's tier and its place in it: a gate's armour line first, in <see cref="EverySpellInput.GateLines"/>' order; her bootstrap next, in <see cref="EverySpellInput.BootstrapLines"/>' order; a damage line by <see cref="DamageLines"/>'s order;
    /// a buff by <see cref="BuffLines"/>'s; the rest 0 (sorted by price after). M6 14 own buffs: whether the buff bot casts the line for her moves it nowhere (the owner, 2026-11-17).
    /// </summary>
    private static (EverySpellTier Tier, int Order, bool Covered) TierOf(EverySpellInput input, string line, IReadOnlyList<string> damage, IReadOnlyList<string> buffs)
    {
        var covered = input.Covered(line);
        var g = Index(input.GateLines, line);
        if (g >= 0)
        {
            return (EverySpellTier.Gate, g, covered); // M6 14 gate lines: first, whoever casts it for her now
        }

        var s = Index(input.BootstrapLines, line);
        if (s >= 0)
        {
            return (EverySpellTier.Bootstrap, s, covered); // M6 14 own buffs: her bootstrap right after the gates, whoever casts it for her now
        }

        var d = Index(damage, line);
        if (d >= 0)
        {
            return (EverySpellTier.Damage, d, covered);
        }

        var b = Index(buffs, line);
        return b >= 0 ? (EverySpellTier.Buff, b, covered) : (EverySpellTier.Rest, 0, covered);
    }

    private static int Index(IReadOnlyList<string> list, string line)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], line, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static EverySpellPick Evaluate(EverySpellInput input, string line, EverySpellTier tier, IReadOnlyList<ScrivenerStockSpell> levels, bool covered)
    {
        var c = CultureInfo.InvariantCulture;
        EverySpellPick Say(EverySpellVerdict verdict, string why, ScrivenerStockSpell? spell = null) => new(line, tier, spell, verdict, false, 0, spell?.Sellers.Select(s => s.VendorWcid).ToList() ?? [], 0, why);
        if (input.PlanLines.TryGetValue(line, out var plan) && plan.Kind is not (LearnNeedKind.BotCovers or LearnNeedKind.NoGain) && !(plan.Kind == LearnNeedKind.Known && plan.LineKind is not (ScrivenerLineKind.Dance or ScrivenerLineKind.Revitalize or ScrivenerLineKind.Sustain))) // M6 audit (finding 7, the owner 2026-11-19): a known sustain line keeps the plan's level (0.95), never the buff bar's 0.75: the fight casts a heal only at 0.95 (VitalsMeans.Pick)
        {
            return Say(EverySpellVerdict.PlanLine, string.Create(c, $"the spell plan's line ({ResupplyRun.LearnNeedToken(plan.Kind)}, target {ScrivenerLearnPlan.Numeral(plan.Target)}): its rule decides"));
        }

        var usable = levels.Where(s => s.Level <= TopLevel).ToList();
        if (usable.Count == 0)
        {
            return Say(EverySpellVerdict.Unsold, "no level of it is sold");
        }

        var facts = new List<EverySpellFact>(usable.Count);
        foreach (var spell in usable)
        {
            if (input.Fact(spell.SpellId) is not { } fact)
            {
                return Say(EverySpellVerdict.NoSpellData, string.Create(c, $"TODO(m6-every-spell-table): the SpellTable given to the trip lacks {spell.Name} ({spell.SpellId}): its school and power are unknown, nothing is bought on a guess"), spell);
            }

            facts.Add(fact);
        }

        var school = facts[0].School;
        var own = tier == EverySpellTier.Bootstrap && input.OwnSkill is not null; // M6 14 own buffs: her bootstrap's level is one she casts on her own
        if ((own ? input.OwnSkill!(school) : input.Skill(school)) is not { } skill)
        {
            return Say(EverySpellVerdict.Untrained, string.Create(c, $"{school} is not trained"));
        }

        var buff = facts.Any(f => f.Beneficial);
        var chance = buff ? input.BuffChance : input.CastChance;
        var target = SpellPlan.LevelAt(skill, chance, [.. facts.Select(f => f.Power)]);
        var numbers = string.Create(c, $"{school} skill {skill} ({(own ? "standing on her own bootstrap, the bot's lines not counted" : "standing")}), the {(buff ? "buff" : "combat")} bar {chance:0.00}");
        if (target == 0)
        {
            return Say(EverySpellVerdict.NoLevel, numbers + string.Create(c, $": no level reaches it (level {(usable[0].Level == 0 ? "-" : ScrivenerLearnPlan.Numeral(usable[0].Level))} power {facts[0].Power}, chance {SpellPlan.Chance(skill, facts[0].Power):0.000})"));
        }

        var top = usable[target - 1];
        var power = facts[target - 1].Power;
        var place = tier switch
        {
            EverySpellTier.Gate => "; a gate's armour line: first" + (covered ? " (the buff bot casts it for her now, and a gate reads her book)" : string.Empty),
            EverySpellTier.Bootstrap => "; her bootstrap (Focus, Willpower, her schools' masteries): right after the gates",
            _ => string.Empty,
        };
        var why = numbers + string.Create(c, $": target {top.Name} (power {power}, chance {SpellPlan.Chance(skill, power):0.000}){place}{(covered ? "; " + CoveredNote : string.Empty)}");
        if (input.Knows(top.SpellId))
        {
            return Say(EverySpellVerdict.Known, why + ": the book holds it", top);
        }

        if (input.OnTrip.Contains(top.SpellId))
        {
            return Say(EverySpellVerdict.PlanLine, why + ": the trip reads it already (the ground's list or the spell plan)", top);
        }

        if (input.RefusedBefore(top.SpellId))
        {
            return Say(EverySpellVerdict.RefusedBefore, why + ": its read was refused earlier this session: the scroll is kept, never bought again", top);
        }

        if (ResupplyRun.CanRead(skill, school, power, out var readable) != LearnReason.None)
        {
            return Say(EverySpellVerdict.Unreadable, why + ": ACE would refuse the read (" + readable + ")", top);
        }

        var homeSeller = top.Sellers.FirstOrDefault(s => input.AtHome(s.VendorWcid) && input.Catalog.ScrivenerOf(s.VendorWcid) is not null);
        var sellers = top.Sellers.Select(s => s.VendorWcid).Where(w => input.Catalog.ScrivenerOf(w) is not null).ToList();
        if (sellers.Count == 0)
        {
            return Say(EverySpellVerdict.Unsold, why + ": no scrivener of the catalog sells it", top);
        }

        if (input.Held(top.ScrollWcid))
        {
            return new EverySpellPick(line, tier, top, EverySpellVerdict.ReadHeld, homeSeller is not null, homeSeller?.VendorWcid ?? 0, sellers, 0, why + string.Create(c, $": a scroll (weenie {top.ScrollWcid}) is in the pack: read, nothing bought"));
        }

        if (homeSeller is not null)
        {
            var scrivener = input.Catalog.ScrivenerOf(homeSeller.VendorWcid)!;
            var price = scrivener.PriceOf(top.ScrollValue);
            return new EverySpellPick(line, tier, top, EverySpellVerdict.Buy, true, homeSeller.VendorWcid, sellers, price, why + string.Create(c, $"; {readable}; {price} pyreals at {scrivener.Name} ({homeSeller.VendorWcid}, create-list row {homeSeller.CreateListId}) at home"));
        }

        var cheapest = sellers.Select(w => input.Catalog.ScrivenerOf(w)!).OrderBy(s => s.PriceOf(top.ScrollValue)).First();
        var towns = input.Catalog.Scriveners.Where(s => sellers.Contains(s.Wcid)).SelectMany(s => s.Places).Select(p => p.Town).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return new EverySpellPick(line, tier, top, EverySpellVerdict.Buy, false, 0, sellers, cheapest.PriceOf(top.ScrollValue), why + string.Create(c, $"; {readable}; {cheapest.PriceOf(top.ScrollValue)} pyreals, sold only off home ({string.Join(", ", towns)}): the Town Network"));
    }

    /// <summary>The verdict as the log's token (kebab case).</summary>
    /// <param name="verdict">The verdict.</param>
    public static string Token(EverySpellVerdict verdict) => verdict switch
    {
        EverySpellVerdict.Buy => "buy",
        EverySpellVerdict.ReadHeld => "read-held",
        EverySpellVerdict.Known => "known",
        EverySpellVerdict.PlanLine => "plan-line",
        EverySpellVerdict.Untrained => "untrained",
        EverySpellVerdict.NoLevel => "no-level",
        EverySpellVerdict.NoSpellData => "no-spell-data",
        EverySpellVerdict.Unsold => "unsold",
        EverySpellVerdict.RefusedBefore => "refused-before",
        EverySpellVerdict.Unreadable => "unreadable",
        EverySpellVerdict.Purse => "purse",
        EverySpellVerdict.Room => "room",
        _ => "other",
    };
}

/// <summary>
/// The every-spell rule's memory of one session (keyed by the trips' <see cref="SaleMemory"/>): the scrolls it bought (kept from the sale until read), the spells whose read ACE refused (kept, never bought again; logged loud), and the
/// last trip's numbers for the cycle line's <c>every_spell=</c>.
/// </summary>
public sealed class EverySpellMemory
{
    private static readonly ConditionalWeakTable<SaleMemory, EverySpellMemory> PerSession = new();

    /// <summary>The memory of the session behind <paramref name="sale"/>.</summary>
    /// <param name="sale">The trips' sale memory.</param>
    public static EverySpellMemory For(SaleMemory sale) => PerSession.GetOrCreateValue(sale ?? throw new ArgumentNullException(nameof(sale)));

    /// <summary>The scroll weenies the rule bought this session.</summary>
    public HashSet<uint> Bought { get; } = [];

    /// <summary>The spells whose read ACE refused after a buy or a held scroll, with the reason: the scroll is kept and never bought again.</summary>
    public Dictionary<uint, string> Unreadable { get; } = [];

    /// <summary>The spells learned by the rule this session.</summary>
    public int Learned { get; set; }

    /// <summary>The last trip's numbers (null before the first).</summary>
    public EverySpellMarker? Last { get; set; }

    /// <summary>The cycle line's field: <c>off</c>, or <c>on,wanted:n,buy:n,learned:n,refused:n,reserve:n,next:&lt;spell id&gt;@&lt;price&gt;</c> (<c>unread</c> before the first trip).</summary>
    /// <param name="on">The switch.</param>
    public string Marker(bool on)
    {
        if (!on)
        {
            return "off";
        }

        if (Last is not { } last)
        {
            return string.Create(CultureInfo.InvariantCulture, $"on,unread,learned:{Learned},refused:{Unreadable.Count}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"on,wanted:{last.Wanted},buy:{last.Buys},learned:{Learned},refused:{Unreadable.Count},reserve:{last.Reserve},next:{(last.NextSpell == 0 ? "none" : string.Create(CultureInfo.InvariantCulture, $"{last.NextSpell}@{last.NextPrice}"))}");
    }
}

/// <summary>The last trip's numbers for the cycle line.</summary>
/// <param name="Wanted">The lines she can cast and lacks.</param>
/// <param name="Buys">The scrolls the trip planned to buy.</param>
/// <param name="Reserve">The reserve.</param>
/// <param name="NextSpell">The first spell still waiting (0: none).</param>
/// <param name="NextPrice">Its price.</param>
public sealed record EverySpellMarker(int Wanted, int Buys, long Reserve, uint NextSpell, int NextPrice);

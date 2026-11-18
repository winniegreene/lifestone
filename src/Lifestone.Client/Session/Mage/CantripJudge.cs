using System.Globalization;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>What a great cantrip does for her, as <see cref="CantripJudge.Judge"/> reads it.</summary>
public enum CantripUse
{
    /// <summary>It raises a basic attribute (Strength, Endurance, Coordination, Quickness, Focus or Self): useful.</summary>
    Attribute,

    /// <summary>It raises a skill she holds specialised now: useful.</summary>
    Specialised,

    /// <summary>It raises a skill she holds trained now: useful.</summary>
    Trained,

    /// <summary>It raises a skill her plan trains (<see cref="CantripPlan"/>): useful.</summary>
    Planned,

    /// <summary>It raises something else (a ward, a property of the item, a vital, a skill she neither holds nor plans): ordinary sell loot.</summary>
    NotUseful,

    /// <summary>What it raises could not be read (no row in the spell table's extract, a row that names no one stat, or her skills not described yet): kept, loudly (<see cref="CantripJudge.UnreadTodo"/>).</summary>
    Unread,
}

/// <summary>One great cantrip's verdict: the spell, what it does for her and why.</summary>
/// <param name="SpellId">The spell id.</param>
/// <param name="SpellName">The spell's name (the SpellTable's).</param>
/// <param name="Use">What it does for her.</param>
/// <param name="Why">The reason, for the log ("Major Focus raises Focus (a basic attribute)").</param>
public readonly record struct CantripVerdict(uint SpellId, string SpellName, CantripUse Use, string Why)
{
    /// <summary>True when the cantrip keeps the piece: useful, or its target unread (kept loudly, never dropped on a guess).</summary>
    public bool Keeps => Use != CantripUse.NotUseful;

    /// <summary>The verdict as the log prints it.</summary>
    public override string ToString() => Use switch
    {
        CantripUse.NotUseful => "not useful: " + Why,
        CantripUse.Unread => "UNREAD, kept until it is read (" + CantripJudge.UnreadTodo + "): " + Why,
        _ => "useful: " + Why,
    };
}

/// <summary>
/// M6 14 (the owner, 2026-11-16: keep only the useful cantrips: basic attributes, or skills she has trained or specialised, or plans to train): the skills her plan trains, which make a cantrip of theirs useful before she holds them.
/// </summary>
/// <param name="Skills">The skill ids the plan trains.</param>
/// <param name="Source">Where the list comes from, for the log.</param>
public sealed record CantripPlan(IReadOnlySet<uint> Skills, string Source)
{
    /// <summary>
    /// The mage's skill plan: War Magic (specialised at creation), the credit order the level-up credits train (<see cref="MageTemplate.ClimberCreationOrder"/> and, with the buff bot, <see cref="MageTemplate.ClimberCreationOrderWithBot"/>: Life Magic,
    /// Creature Enchantment, Item Enchantment, Mana Conversion), the skills her experience raises (<see cref="MageTemplate.ClimberSpendSkills"/>) and the built mage's <see cref="MageTemplate.TrainedLater"/>: the same five skills. Alchemy
    /// (<see cref="MageTemplate.ClimberCreditsOnNeed"/>, M6 14 D136) is trained only when a job needs it, so it is not planned: an Alchemy cantrip is useful once she holds it trained.
    /// </summary>
    public static CantripPlan Mage { get; } = new(
        new HashSet<uint>([MageTemplate.WarMagic, .. MageTemplate.ClimberCreationOrder, .. MageTemplate.ClimberCreationOrderWithBot, .. MageTemplate.ClimberSpendSkills, .. MageTemplate.TrainedLater]),
        "the mage's skill plan (War Magic, the credit order, the experience's skills)");

    /// <summary>No plan: only what she holds now counts.</summary>
    public static CantripPlan None { get; } = new(new HashSet<uint>(), "no plan");
}

/// <summary>
/// M6 14 (the owner's ruling, 2026-11-16: keep only the useful cantrips, meaning basic attributes or skills she has trained, specialized or plans to train; earlier the same day: she only keeps amazing items or major cantrips): the one judge of a great
/// cantrip (Major, Epic or Legendary, <see cref="KeepAsideRule.IsGreat"/>). What a cantrip raises is read from ACE's spell table by spell id (<see cref="AceCantripStatMods"/>: <c>stat_Mod_Type</c> and <c>stat_Mod_Key</c>), never from
/// its name; whether she holds the skill is read live from her qualities (the skill's advancement class); whether she plans it from <see cref="CantripPlan"/>.
/// </summary>
/// <remarks>
/// <para><b>Useful.</b> An attribute row (any of the six is a basic attribute); a skill row whose skill she holds specialised or trained now, or her plan trains. <b>Not useful:</b> every other row: a ward (a resistance float), an item's
/// property (Impenetrability, Blood Thirst, Defender, Spirit Thirst, Hermetic Link), a regeneration rate (Mana Gain), a vital, body armour, or a skill she neither holds nor plans (Heavy Weapons, Leadership; Missile Defense for
/// Impregnability while it is untrained).</para>
/// <para><b>Unread.</b> A great cantrip whose row is missing, whose row names more than one stat or none, or whose skill cannot be judged because her skills have not been described: a named <see cref="UnreadTodo"/>, kept (never dropped on
/// a guess) and logged as a warning.</para>
/// </remarks>
public sealed class CantripJudge
{
    /// <summary>The name an unread target carries in the log.</summary>
    public const string UnreadTodo = "TODO(m6-cantrip-target-unread)";

    private readonly Func<uint, uint?> _skillClass;
    private readonly Func<uint, CantripStatMod?> _statOf;

    /// <summary>A judge.</summary>
    /// <param name="skillClass">The advancement class she holds a skill at (0 or 1 untrained, 2 trained, 3 specialised), or null while her skills are not described.</param>
    /// <param name="plan">The skills her plan trains; null: <see cref="CantripPlan.Mage"/>.</param>
    /// <param name="statOf">A spell's row of the spell table; null: <see cref="AceCantripStatMods.Of"/>.</param>
    public CantripJudge(Func<uint, uint?> skillClass, CantripPlan? plan = null, Func<uint, CantripStatMod?>? statOf = null)
    {
        ArgumentNullException.ThrowIfNull(skillClass);
        _skillClass = skillClass;
        Plan = plan ?? CantripPlan.Mage;
        _statOf = statOf ?? AceCantripStatMods.Of;
    }

    /// <summary>The plan in force.</summary>
    public CantripPlan Plan { get; }

    /// <summary>A judge that reads her skills live from <paramref name="state"/> at each verdict (her qualities: the advancement class; null until the description arrives).</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="plan">The plan; null: <see cref="CantripPlan.Mage"/>.</param>
    public static CantripJudge Of(GameState state, CantripPlan? plan = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new CantripJudge(id => SkillClassOf(state.Self.Qualities, id), plan);
    }

    /// <summary>A judge that knows no skill she holds (every skill outside the plan is <see cref="CantripUse.Unread"/>): the callers with no character state.</summary>
    public static CantripJudge Stateless { get; } = new(_ => null);

    /// <summary>The advancement class <paramref name="q"/> holds <paramref name="skill"/> at (0 when not listed), or null while the description has not arrived.</summary>
    /// <param name="q">Her qualities.</param>
    /// <param name="skill">The skill id.</param>
    public static uint? SkillClassOf(PlayerQualities q, uint skill)
    {
        ArgumentNullException.ThrowIfNull(q);
        return !q.IsKnown ? null : q.TryGetSkill(skill, out var s) ? s.AdvancementClass : 0;
    }

    /// <summary>The verdict on one cantrip spell (see the class remarks).</summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="spellName">The spell's name (the SpellTable's), for the log.</param>
    public CantripVerdict Judge(uint spellId, string spellName)
    {
        if (_statOf(spellId) is not { } row)
        {
            return new(spellId, spellName, CantripUse.Unread, string.Create(CultureInfo.InvariantCulture, $"{spellName} (spell {spellId}) has no row in the spell table's extract ({AceCantripStatMods.FileName}): what it raises is not known"));
        }

        if ((row.Type & 0xFF) == 0 || (row.IsMultipleStat && (row.IsAttribute || row.IsSkill)))
        {
            return new(spellId, spellName, CantripUse.Unread, string.Create(CultureInfo.InvariantCulture, $"{spellName} (spell {spellId}) has stat_Mod_Type 0x{row.Type:X} key {row.Key}: no one stat named"));
        }

        if (row.IsAttribute)
        {
            return AttributeName(row.Key) is { } attribute
                ? new(spellId, spellName, CantripUse.Attribute, $"{spellName} raises {attribute} (a basic attribute)")
                : new(spellId, spellName, CantripUse.Unread, string.Create(CultureInfo.InvariantCulture, $"{spellName} (spell {spellId}) raises attribute {row.Key}, which is none of the six"));
        }

        if (row.IsSkill)
        {
            var skill = GameExaminePanel.SkillName((int)row.Key);
            var held = _skillClass(row.Key);
            if (held >= SkillRules.SpecializedClass)
            {
                return new(spellId, spellName, CantripUse.Specialised, $"{spellName} raises {skill} (specialised)");
            }

            if (held == SkillRules.TrainedClass)
            {
                return new(spellId, spellName, CantripUse.Trained, $"{spellName} raises {skill} (trained)");
            }

            if (Plan.Skills.Contains(row.Key))
            {
                return new(spellId, spellName, CantripUse.Planned, $"{spellName} raises {skill} (not trained yet: {Plan.Source} trains it)");
            }

            return held is null
                ? new(spellId, spellName, CantripUse.Unread, $"{spellName} raises {skill}, which her plan does not train, and her skills are not described yet")
                : new(spellId, spellName, CantripUse.NotUseful, $"{spellName} raises {skill} (untrained), not an attribute or a trained or planned skill");
        }

        return new(spellId, spellName, CantripUse.NotUseful, $"{spellName} raises {TargetOf(row)}, not an attribute or a trained or planned skill");
    }

    /// <summary>
    /// The verdicts on every great cantrip (<see cref="KeepAsideRule.IsGreat"/>) the appraisal casts, in the appraisal's order, each spell once; empty with no appraisal, a not-found one, or no great cantrip. A spell the SpellTable
    /// cannot name is no great cantrip (its tier is the name's).
    /// </summary>
    /// <param name="appraisal">The item's appraisal, or null.</param>
    /// <param name="spellName">The name of a spell id (the SpellTable's), or null for an id it does not know.</param>
    /// <param name="tiers">The tiers that count (a keep-aside block's); null: <see cref="KeepAsideRule.GreatTiers"/>.</param>
    public List<CantripVerdict> GreatOf(Appraisal? appraisal, Func<uint, string?> spellName, IReadOnlyCollection<string>? tiers = null)
    {
        ArgumentNullException.ThrowIfNull(spellName);
        var verdicts = new List<CantripVerdict>();
        if (appraisal is null || appraisal.IsNotFound)
        {
            return verdicts;
        }

        foreach (var id in appraisal.Spells)
        {
            if (spellName(id) is { } name && KeepAsideRule.IsGreat(name) && (tiers is null || tiers.Any(t => name.StartsWith(t + " ", StringComparison.OrdinalIgnoreCase))) && !verdicts.Exists(v => v.SpellId == id))
            {
                verdicts.Add(Judge(id, name));
            }
        }

        return verdicts;
    }

    /// <summary>The name of attribute <paramref name="id"/> (ACE's <c>PropertyAttribute</c>), or null for none of the six.</summary>
    /// <param name="id">The attribute id.</param>
    public static string? AttributeName(uint id) => id switch
    {
        MageTemplate.Strength => "Strength",
        MageTemplate.Endurance => "Endurance",
        MageTemplate.Quickness => "Quickness",
        MageTemplate.Coordination => "Coordination",
        MageTemplate.Focus => "Focus",
        MageTemplate.Self => "Self",
        _ => null,
    };

    /// <summary>What a row that is neither an attribute nor a skill raises, for the log (ACE's flag and key: the property's own name is not read here).</summary>
    private static string TargetOf(CantripStatMod row) => (row.Type & 0xFF) switch
    {
        var t when (t & CantripStatMod.VitalFlag) != 0 => string.Create(CultureInfo.InvariantCulture, $"a vital (PropertyAttribute2nd {row.Key})"),
        var t when (t & CantripStatMod.FloatFlag) != 0 => string.Create(CultureInfo.InvariantCulture, $"a float property (PropertyFloat {row.Key}: a ward's resistance, a regeneration rate or an item's modifier)"),
        var t when (t & CantripStatMod.IntFlag) != 0 => string.Create(CultureInfo.InvariantCulture, $"an int property (PropertyInt {row.Key}: an item's armour level or damage)"),
        var t when (t & CantripStatMod.BodyArmorFlag) != 0 => "body armour",
        _ => string.Create(CultureInfo.InvariantCulture, $"stat_Mod_Type 0x{row.Type:X} key {row.Key}"),
    };
}

/// <summary>
/// M6 14 useful cantrips: the decision lines of <see cref="CantripJudge"/>, each piece's verdicts once per process (the pack's layout asks about the same pieces many times a minute): "cantrip keep (the corpse): 'Necklace' 0x80001234: sell
/// loot: not useful: Major Impregnability raises Missile Defense (untrained), ...".
/// </summary>
public static class CantripVerdictLog
{
    private static readonly HashSet<string> Logged = [];

    /// <summary>The line for <paramref name="guid"/>'s verdicts the first time they are seen (a changed verdict is a new line), or null when already logged or there are none.</summary>
    /// <param name="guid">The piece.</param>
    /// <param name="name">The piece's name.</param>
    /// <param name="verdicts">Its great cantrips' verdicts (<see cref="CantripJudge.GreatOf"/>).</param>
    /// <param name="where">Where the decision is made ("the corpse", "the packs").</param>
    public static string? Once(uint guid, string name, IReadOnlyList<CantripVerdict> verdicts, string where)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        if (verdicts.Count == 0)
        {
            return null;
        }

        var line = Line(guid, name, verdicts, where);
        var key = string.Create(CultureInfo.InvariantCulture, $"{guid:X8}|{string.Join(",", verdicts.Select(v => v.SpellId + ":" + (int)v.Use))}");
        lock (Logged)
        {
            return Logged.Add(key) ? line : null;
        }
    }

    /// <summary>The decision line for a piece (see the class summary).</summary>
    /// <param name="guid">The piece.</param>
    /// <param name="name">The piece's name.</param>
    /// <param name="verdicts">Its great cantrips' verdicts.</param>
    /// <param name="where">Where the decision is made.</param>
    public static string Line(uint guid, string name, IReadOnlyList<CantripVerdict> verdicts, string where)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        var kept = verdicts.Any(v => v.Keeps);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"cantrip keep ({where}): '{name}' 0x{guid:X8}: {(kept ? "a great piece, kept" : "sell loot, judged by its value per burden")}: {string.Join("; ", verdicts)} (the owner, 2026-11-16: only the useful cantrips)");
    }

    /// <summary>True when a verdict is <see cref="CantripUse.Unread"/>: the line is a warning.</summary>
    /// <param name="verdicts">The verdicts.</param>
    public static bool HasUnread(IReadOnlyList<CantripVerdict> verdicts) => verdicts.Any(v => v.Use == CantripUse.Unread);
}

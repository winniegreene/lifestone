using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>What the wardrobe read for a judge, for the asking site's log lines.</summary>
/// <param name="Character">The session's character, whose keep-aside default counts (null: not read yet).</param>
/// <param name="Element">The war element (ACE DamageType bits), 0 for none.</param>
/// <param name="ElementSource">Where the element came from, or the TODO that names its absence.</param>
/// <param name="Caster">The caster score's terms.</param>
/// <param name="BoltSource">The terms' bolt, or why there is none.</param>
public sealed record WearInputs(string? Character, uint Element, string ElementSource, CasterTerms Caster, string BoltSource);

/// <summary>
/// M6 item 16, slices E5e1 and E5e2 (docs/automation-engine.md section 2.5; m6-plan.md D139): the quartermaster's wear service, one a session. Two things live here.
/// <para><b>The one answer to "is this piece better"</b> (<see cref="JudgeOn(HuntingGround, IReadOnlyDictionary{uint, SpellInfo}, GearScoreNotes?)"/>). The judge was built at six places (the town trip,
/// the gear keeps, the worn restorer, the fight's own loot judge and three empty ones for the rearm) and a seventh no scan counted (the autopilot's caster pick), each from its own reading of the ground, so one piece could be
/// worn at the trip and passed over elsewhere. The wardrobe builds the judge from the session (who she is, her level, her book and skills, what is lit on her), the ground and the job, and a site passes no piece of it.
/// E5e2 took the sites' differences away one at a time; each is said at its input below, with what a site read before. Two named rules stay, each in its method: the gear keeps ask the element
/// (<see cref="KeepJudges"/>), and a rearm where no job stated a ground judges on none (<see cref="RearmJudge"/>).</para>
/// <para><b>The useful cantrips</b> (E5e2, the first difference): the keep-aside in force on the ground for the session's character (<see cref="KeepAsideRule.SettingFor"/>: the profile's own block, else the character's default),
/// for every site. Before, the worn restorer read the ground's own block alone (on a ground without one a ring casting a useful major cantrip was worn at the trip and passed over by the restore), the fight's own judge the
/// loot rules' names (the same list on a journey; the ground's block alone in a plain fight) and the rearm none.</para>
/// <para><b>The element and the damage types</b> (E5e2, the second; E5e3): the element is the fight's own choice on the ground (<see cref="ElementOn"/>: what deals most of her damage on its creatures from her book), and only
/// where that is not known her rung's when the rung of her level names the ground, else the spell plan's family (<see cref="WornRestorer.ElementFor"/>), and the
/// armour score reads the ground's creatures' damage types (<see cref="SelfEquipJudge.DamageTypesOf"/>), for every site. Before, the rearm read no element (a caster's bonus on her element did not count: with a plain
/// wand and a fire sceptre in the pack the trip's rearm wielded the sceptre, the recovery table's the wand) and the fight's own judge read the plan's family alone and the mean of the seven protections.</para>
/// <para><b>The caster terms</b> (E5e2, the third): the ladder's weights, the bolt her live War Magic casts on the element and the auras her book holds (<see cref="CasterTerms.OfLadder"/>), read at each ask, for every site.
/// Before, the fight's own judge and the rearm scored a caster on the default terms: no bolt and no aura, so a Mana Conversion bonus counted its own percent of the skill where the trip counted the mana model's bolts
/// a mana bar, and her Hermetic Link did not multiply it.</para>
/// <para><b>The lit pieces</b> (E5e2, the fourth): every judge reads which worn pieces are lit on her now (<see cref="WearScore.LitOn"/> over the session's registry: ACE reads her Arcane Lore once, at the wield, so a piece
/// lit while she was buffed keeps its spells whatever her lore reads after). Before, the town trip alone read it. The reading decides nothing without the wear pick's terms, which only the trip's judge is given today, so
/// no answer changed; it is carried by every judge so that none can be given those terms and read a lit piece dark (the live loss of 2026-11-14: a lit Major Mana Conversion bracelet read dark, swapped for armour and
/// sold).</para>
/// <para><b>The held-back casters</b> (E5e2, the fifth): one memory a session (<see cref="HeldBack"/>). Before, a journey kept its own memory for the trip and every other site read one static memory of the process: on the
/// autopilot the two were one object, on a plain journey they were two, so after a trip's <c>caster-wield-failed</c> a rearm could wield that caster again; and the static one outlived a session.</para>
/// <para><b>Still one</b> (docs/automation-engine.md section 6). The wear pick's terms (<see cref="WearTerms"/>) are made here since Q10c (<see cref="JudgeWithTerms"/>, <see cref="WithWearTerms"/>; <c>Wardrobe.Terms.cs</c>) for the restorer's wearable pick and the
/// corpse plan's verdict as for the trip. <c>TODO(engine-wear-bolt-chance)</c>: the caster terms' bolt is the level the ladder's <c>plan_cast_chance</c> casts, whatever the command line's
/// <c>--buff-cast-chance</c>.</para>
/// <para><b>The gear keeps' cast chance</b> (<see cref="KeepCastChance"/>). The caster keep counts an element as cast when a level of its bolt line in her book reaches a cast chance (<see cref="CasterKeep.Decide"/>: the fight's).
/// The level up job's fight casts at the ladder's <c>plan_cast_chance</c> (0.95 on the current skill, m6-plan.md D41; D90 keeps 0.95 the bar of combat and gives the buffs a bar of their own), the den's at
/// <see cref="BuffOptions.DefaultCastChance"/> (0.9, unchanged for the den's character by D41). The rule is the keep's own: the chance is the fight's. The job states it once, and the field, the trip and the sale's context
/// all read it here.</para>
/// </summary>
public sealed partial class Wardrobe
{
    private static readonly ConditionalWeakTable<GameState, Wardrobe> PerSession = new();
    private readonly GameState _state;
    private double? _keepCastChance;
    private HuntingGround? _jobGround;
    private IReadOnlyDictionary<uint, SpellInfo>? _jobSpells;

    private Wardrobe(GameState state) => _state = state;

    /// <summary>The wardrobe of a session, made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static Wardrobe For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static s => new Wardrobe(s));
    }

    /// <summary>The cast chance a gear keep reads when no fight is at hand (the town trip, the sale's context): the job's statement, else <see cref="BuffOptions.DefaultCastChance"/> (the den's fight's).</summary>
    public double KeepCastChance => _keepCastChance ?? BuffOptions.DefaultCastChance;

    /// <summary>Who stated <see cref="KeepCastChance"/>, for a log line.</summary>
    public string KeepCastChanceSource { get; private set; } = "no job stated one: the den's fight's (BuffOptions.DefaultCastChance)";

    /// <summary>The cast chance a gear keep reads in a fight: the job's statement, else that fight's own chance (<paramref name="fight"/>: what the field read before this slice).</summary>
    /// <param name="fight">The fight's own cast chance (its buff options').</param>
    public double KeepCastChanceIn(double fight) => _keepCastChance ?? fight;

    /// <summary>The job states the chance its fight casts at (the level up job: the ladder's <c>plan_cast_chance</c>).</summary>
    /// <param name="chance">The chance, in (0, 1].</param>
    /// <param name="source">Who states it, for a log line.</param>
    public void StateKeepCastChance(double chance, string source)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chance, 1.0);
        ArgumentNullException.ThrowIfNull(source);
        _keepCastChance = chance;
        KeepCastChanceSource = source;
    }

    /// <summary>
    /// The job states the ground it works and the SpellTable it reads, once a session's read model: what a rearm with no ground at hand judges on (<see cref="RearmJudge"/>: the recovery table and the recall home know neither).
    /// </summary>
    /// <param name="ground">The job's ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    public void StateGround(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        (_jobGround, _jobSpells) = (ground, spells);
    }

    /// <summary>The ground the job stated (<see cref="StateGround"/>), or null.</summary>
    public HuntingGround? JobGround => _jobGround;

    /// <summary>
    /// The session's one memory of casters held back: a swap whose wield did not land (ACE refused it, or the hands did not hold it after; the town trip's <c>caster-wield-failed</c>) is not sent again this session. Every
    /// judge the wardrobe builds reads and writes this one, so the trip's pick, the restore's, the audit's, the spend and every rearm pass the same casters over.
    /// </summary>
    public CasterHeldBack HeldBack { get; } = new();

    /// <summary>The wear judge of the session on <paramref name="ground"/> (see the class): the one answer, whoever asks.</summary>
    /// <param name="ground">The ground (its keep-aside block, its plan, its creatures' damage types).</param>
    /// <param name="spells">The SpellTable's spells (the cantrips' names, the bolt's row).</param>
    /// <param name="notes">The asking run's TODO latch for the scores (the town trip writes each once to its log), or null for one of the judge's own: where the notes go, no part of the answer.</param>
    public SelfEquipJudge JudgeOn(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, GearScoreNotes? notes = null) => JudgeOn(ground, spells, out _, notes);

    /// <summary>The wear judge of the session on <paramref name="ground"/>, with what was read for it (<paramref name="inputs"/>: the asking site's log lines).</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="inputs">What the wardrobe read.</param>
    /// <param name="notes">The asking run's TODO latch for the scores, or null.</param>
    public SelfEquipJudge JudgeOn(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, out WearInputs inputs, GearScoreNotes? notes = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        var character = _state.Self.Name;
        var element = ElementOn(ground, spells, out var source);
        var terms = CasterTerms.OfLadder(_state, spells, element, out var bolt);
        inputs = new WearInputs(character, element, source, terms, bolt);
        return Make(Useful(ground, character), element, Names(spells), SelfEquipJudge.DamageTypesOf(ground), notes, terms);
    }

    /// <summary>
    /// The gear keeps' judge for an element's bits (<see cref="CantripRoom.GearKeeps"/>). A named rule: the element is the keep's question (the best caster for each element she casts), so it is asked, not read from
    /// the rung. The rest is the session's: the useful cantrips in force, the ground's damage types, the ladder's caster terms of that element, the lit pieces; the cantrips and the damage types are read once for every element.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    public Func<uint, SelfEquipJudge> KeepJudges(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        var useful = Useful(ground, _state.Self.Name);
        var damage = SelfEquipJudge.DamageTypesOf(ground);
        var names = Names(spells);
        return element => Make(useful, element, names, damage, null, CasterTerms.OfLadder(_state, spells, element, out _));
    }

    /// <summary>
    /// The rearm's judge (<see cref="HeldCaster.Rearm"/> where no ground's judge is at hand: the recovery table, the recall home, a quest step): the session's judge on the job's ground (<see cref="StateGround"/>), so the
    /// caster a rearm wields is the one the trip's pick would keep in her hands: the useful cantrips in force, the element of her rung on that ground, its damage types, the ladder's caster terms.
    /// <para>A named rule, the judge of no ground: where no job stated a ground (a quest step of a plain journey, a fighter's recall) there is no plan to read an element from and no SpellTable to name a cantrip by, so the
    /// judge reads none. Her hands are empty and she cannot cast until a caster is in them (ACE sets peace on a magic mode with none held, Player_Combat.cs 872-882 @95ef426d): any usable caster at once is right, and the
    /// next trip's pick puts the best one there.</para>
    /// </summary>
    public SelfEquipJudge RearmJudge()
    {
        if (_jobGround is not { } ground || _jobSpells is not { } spells)
        {
            return Make([], 0, static _ => null, null, null, null);
        }

        var element = ElementOn(ground, spells, out _);
        return Make(Useful(ground, _state.Self.Name), element, Names(spells), SelfEquipJudge.DamageTypesOf(ground), null, CasterTerms.OfLadder(_state, spells, element, out _));
    }

    /// <summary>
    /// M6 16 E5e3: the war element every judge of the session reads on <paramref name="ground"/> (ACE DamageType bits, 0 for none): the fight's own choice, the element that deals most of her damage on the ground's creatures
    /// from her book (<see cref="CastElement.OnGround"/>, on the fight's terms: the chance is <see cref="KeepCastChance"/> until a fight has stated its own there). Only where that is not known (a ground that names no
    /// creature, her War Magic or her book unread, no castable line) the rule of before stands: the rung's label when the rung of her level names the ground, else the plan's family (<see cref="WornRestorer.ElementFor"/>),
    /// and <paramref name="source"/> says both. Before, that rule was the only reading: past a ground's own rung and plan rows (level 82 on the Tusker Quarters) it read none, and the pick counted no caster's bonus on the
    /// bolts she was casting.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="source">Where the element came from, or the TODO that names its absence.</param>
    /// <param name="chance">The cast chance of a site that was given the fight's own (the gear keeps), or null for <see cref="KeepCastChance"/>.</param>
    public uint ElementOn(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, out string source, double? chance = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        var cast = CastElement.For(_state).OnGround(ground, spells, chance ?? KeepCastChance);
        if (cast.Element is { } name && SelfEquipJudge.ElementBits.TryGetValue(name, out var bits))
        {
            source = string.Create(CultureInfo.InvariantCulture, $"{cast.Source} 0x{bits:X}");
            return bits;
        }

        var element = WornRestorer.ElementFor(ground, _state.Self.Qualities.Level, out var how);
        source = how + "; the fight's own choice is not known: " + cast.Source;
        return element;
    }

    private bool _blindNoted;

    /// <summary>
    /// M6 16 E5e3, the loud stub: a caster pick that reads no element (<paramref name="inputs"/>) on a ground where a fight of this session has a line in hand is an error, once a session: every caster's bonus on her bolts
    /// counts nothing in that pick, so she may hold the wrong caster for the line she casts (live, 2026-11-18: five runs on a piercing sceptre). Each site that picks a caster calls it with its own log; true when it wrote.
    /// </summary>
    /// <param name="log">The asking site's log.</param>
    /// <param name="category">Its category.</param>
    /// <param name="ground">The ground the judge was asked on.</param>
    /// <param name="inputs">What the wardrobe read for that judge.</param>
    public bool NoteBlindElement(ILogSink log, string category, HuntingGround ground, WearInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Element != 0 || _blindNoted || !CastElement.For(_state).FoughtOn(ground.Key))
        {
            return false;
        }

        _blindNoted = true;
        var used = ElementsUsed.For(_state).Used;
        log.Error(category, $"gear: TODO(m6-caster-element-blind): the caster pick on {ground.Key} reads no element while the fight there has a line in hand ({(used.Count == 0 ? "none noted" : string.Join(", ", used.Order(StringComparer.Ordinal)))}): no caster's bonus on her bolts counts in the pick, so she may hold the wrong caster for the line she casts ({inputs.ElementSource})");
        return true;
    }

    /// <summary>The useful cantrips: the keep-aside in force on <paramref name="ground"/> for <paramref name="character"/> (the profile's own block, else the character's default).</summary>
    private static IEnumerable<string> Useful(HuntingGround ground, string? character) => KeepAsideRule.SettingFor(ground, character)?.SpellNames() ?? [];

    private static Func<uint, string?> Names(IReadOnlyDictionary<uint, SpellInfo> spells) => id => spells.TryGetValue(id, out var s) ? s.Name : null;

    /// <summary>
    /// The one construction of the wear judge. Every judge of a session reads what is lit on her and the session's held-back memory, and carries the keep-great-cantrips rule as a guard (<see cref="SelfEquipJudge.UsefulGreat"/>): a worn piece that casts a great cantrip useful to her is never taken
    /// off for a piece that casts none, asked of the piece itself (<see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/>), not of the list's names.
    /// E5e5: that read answers "is this a keeper"; with what is lit on her and her numbers the judge answers the second question from it, "does its great cantrip count for her on her body" (<see cref="GreatOnBody"/>),
    /// and the guard holds only for a piece that does.
    /// </summary>
    private SelfEquipJudge Make(IEnumerable<string> useful, uint element, Func<uint, string?> names, IReadOnlyList<uint>? damage, GearScoreNotes? notes, CasterTerms? terms) =>
        new SelfEquipJudge(useful, element, names, damage, notes, terms) { LitBy = LitOnHer, HeldBack = HeldBack, UsefulGreat = guid => CantripRoom.IsUsefulGreatPiece(_state, guid, names) };

    private bool LitOnHer(uint guid) => WearScore.LitOn(_state.Self.Qualities, guid);
}

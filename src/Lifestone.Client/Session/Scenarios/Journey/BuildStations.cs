using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// <c>build-verify</c> (M4 P5b J3b, lanes.md kind table): the journey character's build, in the world, verify-only. The mage's (<see cref="MageBuildScenario"/>) or the fighter's (<see cref="FighterBuildScenario"/>) scenario is chosen by the
/// context's character and runs embedded (<c>endSession: false</c>: no login, no place, no log off), so it adds only what is missing, exactly as the build does today (experience, trains, raises, the spellbook including Lifestone Tie 2644, the
/// kit), and a ready character is only checked. Fields are the build's own: <c>mage=ready|built|failed level= template= spells= built_steps=</c> or <c>fighter=ready|built|failed level= template= kit= ...</c>. A fighter's build here also recharges
/// its magic gear (<see cref="GearRecharge"/>: the kit's mana charge when a piece's mana is under half; closes <c>TODO(m4-gear-mana)</c>) and reports <c>gear_mana_min= recharged=</c>, report-only in their first gate.
/// <para><b>The mage's build grants the ground's kit</b> (M5 item 3 w1, D8, D46, D48): the mage's scenario is made by <see cref="MageBuildOf"/> with <see cref="GearKit.KitPlan.For"/> over the journey's dungeon ground
/// (<see cref="JourneyContext.Dungeon"/>, <c>--fight-ground</c>), so a fresh build ends with the vendor robe and wand, the four foci, the stock and the purse (stage <c>granting</c>); the headless host builds its mage the same way. <b>Only a fresh build grants</b> (M5 3 F2, closes 3-R4, D38, D49): the scenario decides from what the character holds (<see cref="MageBuildScenario.DecideGrant"/>), logs it
/// (<c>build: fresh (...)</c> or <c>build: ready (...)</c>), and a ready mage (at its target with its kit's worn, wielded and focus lines in the pack) is verified only: no kit top-up, no purse and no first tie, each stock line under its keep named
/// (<c>build: &lt;line&gt; is &lt;n&gt; under its keep; play does not top it up (M5 DoD 6)</c>) and left for the resupply, so a journey started after a day of play does not reset the economy that <c>granted=0</c> is judged on.</para>
/// </summary>
public sealed class BuildVerifyStation : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "build-verify";

    private readonly XpTableData? _table;
    private readonly IReadOnlyList<uint>? _retailClasses;
    private readonly int _mageLevel;
    private readonly string? _accountName;

    /// <summary>The station. Both arguments are for a test without dats: by default the XpTable and the skill classes are read from the journey's dats.</summary>
    /// <param name="table">The XpTable, or null to load it from <see cref="JourneyContext.Dats"/>.</param>
    /// <param name="retailClasses">The retail skill classes (only a creation needs them; the station never creates), or null.</param>
    /// <param name="mageLevel">M5 7 F6 (D87, amended): the level of the mage's build (default <see cref="MageTemplate.TargetLevel"/>, the gate build); a level below it is the traditional mage (<see cref="MageTemplate.BuildFor"/>: no Melee Defense), judged by that level's plan. M5 7 F14: the journey passes <c>--mage-level</c> (<see cref="JourneyStations.MageLevelOf"/>), so <c>--scenario=journey --journey=mage --mage-level=80</c> verifies the level-80 traditional build; closes <c>m5-7-journey-mage-level (closed)</c>.</param>
    /// <param name="accountName">M6 1 fix (1-R9): the login name the server hashes for the kit's component slots (the host's; never the secrets key, which <see cref="JourneyContext.Account"/> is); null: the key, with <c>TODO(j4-kit-account)</c> logged.</param>
    public BuildVerifyStation(XpTableData? table = null, IReadOnlyList<uint>? retailClasses = null, int mageLevel = MageTemplate.TargetLevel, string? accountName = null)
    {
        _accountName = accountName;
        _mageLevel = mageLevel;
        _table = table;
        _retailClasses = retailClasses;
    }

    /// <summary>The level of the mage's build this station judges (M5 7 F14).</summary>
    public int MageLevel => _mageLevel;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var table = _table ?? (context.Dats is { } dats ? XpTableData.Load(dats) : throw new InvalidOperationException("TODO(m4-p5b-build-dats): build-verify needs the dats' XpTable (the journey's context holds none)"));
        var classes = _retailClasses ?? (context.Dats is { } d ? HeadlessCharacter.RetailSkillClasses(d) : []);
        if (string.Equals(context.Character, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
        {
            var mage = MageBuildOf(table, classes, context.Log, context.Dungeon, context.Spells, KitAccountOf(context), endSession: false, level: _mageLevel);
            return new BuildRun(mage, mage.VerdictFields);
        }

        var template = FighterTemplate.ForName(context.Character) ?? throw new InvalidOperationException($"TODO(m4-p5b-build-character): build-verify knows no build for '{context.Character}'");
        // The gear's recharge (closes TODO(m4-gear-mana)): the journey's fighters keep their magic gear over many gates, so build-verify appraises its mana and uses the kit's mana charge when a piece runs low (GearRecharge, report-only).
        var fighter = new FighterBuildScenario(table, classes, context.Log, template: template, spells: context.Spells, account: KitAccountOf(context), endSession: false, rechargeGear: true);
        return new BuildRun(fighter, fighter.VerdictFields);
    }

    /// <summary>
    /// M6 1 fix (1-R9, narrows <c>TODO(j4-kit-account)</c>): the name the kit's components are hashed for: the host's login name when it gave one (the live hosts all do), else the context's account key with the tag logged (a scripted
    /// host's; the key differs from the login, so the taper slots may not match the account's). Hashed by the caller, never logged.
    /// </summary>
    /// <param name="context">The journey's context.</param>
    internal string KitAccountOf(JourneyContext context)
    {
        if (_accountName is { Length: > 0 } name)
        {
            return name;
        }

        context.Log.Warn(JourneyVerdict.LogCategory, "TODO(j4-kit-account): the host gave no login name; the build's kit is made for the account key (its taper slots may not match the account's)");
        return context.Account;
    }

    /// <summary>
    /// The mage's build scenario with the kit plan of <paramref name="ground"/> (M5 item 3 w1, closes m1's leftover: the hosts passed no plan): the one place a host makes the mage build, so the journey's build-verify and the headless
    /// <c>--scenario=mage-build</c> both grant the ground's kit, foci, stock and purse once the template is met (<see cref="GearKit.KitPlan.For"/>; a ground with no vendor kit fails by name, <c>ground-plan-gap</c>).
    /// </summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="retailClasses">The retail skill classes.</param>
    /// <param name="log">The log.</param>
    /// <param name="ground">The ground whose supplies the mage is built with (<c>--fight-ground</c>, <see cref="FightOptions.GroundProfile"/>).</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="account">The account name (<see cref="GearKit.Build"/>).</param>
    /// <param name="endSession">False when embedded in a journey.</param>
    /// <param name="freshCharacter">True for the live gate's re-creation (<c>--fresh-character</c>).</param>
    /// <param name="level">M5 7 F3 (D87): the level the build reaches (<c>--mage-level</c>, default 126); the kit's load is judged at the Strength of that level's plan (<see cref="MageTemplate.PlanAt"/>).</param>
    public static MageBuildScenario MageBuildOf(XpTableData table, IReadOnlyList<uint> retailClasses, ILogSink log, HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, string account, bool endSession = true, bool freshCharacter = false, int level = MageTemplate.TargetLevel)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(table);
        var strength = MageTemplate.PlanAt(table, level).RanksOf(MageStatKind.Attribute, MageTemplate.Strength);
        return new MageBuildScenario(table, retailClasses, log, endSession: endSession, freshCharacter: freshCharacter, kitPlan: GearKit.KitPlan.For(ground, spells, account, capacityStrength: strength), level: level);
    }

    /// <summary>A build scenario as a station run: it ended well when it reached <c>done</c> without a failure.</summary>
    private sealed class BuildRun(IScenario scenario, Func<string> verdict) : IStationRun
    {
        public string Name => StationName;

        public string Stage => scenario.Stage;

        public string? Failure => scenario.Failure;

        public double? WhereMaxMetres => scenario.WhereMaxMetres;

        public long Unmatched => scenario.Unmatched;

        public bool Ok => scenario.Stage == "done" && scenario.Failure is null;

        public string VerdictFields() => verdict() + (scenario.Failure is null ? string.Empty : " why=" + scenario.Failure.Replace(' ', '_'));

        public ScenarioStatus Step(IGameSessionView session, double now) => scenario.Step(session, now);
    }
}

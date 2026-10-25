using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// <c>build-verify</c> (M4 P5b J3b, lanes.md kind table): the journey character's build, in the world, verify-only. The mage's (<see cref="MageBuildScenario"/>) or the fighter's (<see cref="FighterBuildScenario"/>) scenario is chosen by the
/// context's character and runs embedded (<c>endSession: false</c>: no login, no place, no log off), so it adds only what is missing, exactly as the build does today (experience, trains, raises, the spellbook including Lifestone Tie 2644, the
/// kit), and a ready character is only checked. Fields are the build's own: <c>mage=ready|built|failed level= template= spells= built_steps=</c> or <c>fighter=ready|built|failed level= template= kit= ...</c>. A fighter's build here also recharges
/// its magic gear (<see cref="GearRecharge"/>: the kit's mana charge when a piece's mana is under half; closes <c>TODO(m4-gear-mana)</c>) and reports <c>gear_mana_min= recharged=</c>, report-only in their first gate.
/// </summary>
public sealed class BuildVerifyStation : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "build-verify";

    private readonly XpTableData? _table;
    private readonly IReadOnlyList<uint>? _retailClasses;

    /// <summary>The station. Both arguments are for a test without dats: by default the XpTable and the skill classes are read from the journey's dats.</summary>
    /// <param name="table">The XpTable, or null to load it from <see cref="JourneyContext.Dats"/>.</param>
    /// <param name="retailClasses">The retail skill classes (only a creation needs them; the station never creates), or null.</param>
    public BuildVerifyStation(XpTableData? table = null, IReadOnlyList<uint>? retailClasses = null)
    {
        _table = table;
        _retailClasses = retailClasses;
    }

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
            var mage = new MageBuildScenario(table, classes, context.Log, endSession: false);
            return new BuildRun(mage, mage.VerdictFields);
        }

        var template = FighterTemplate.ForName(context.Character) ?? throw new InvalidOperationException($"TODO(m4-p5b-build-character): build-verify knows no build for '{context.Character}'");
        // The gear's recharge (closes TODO(m4-gear-mana)): the journey's fighters keep their magic gear over many gates, so build-verify appraises its mana and uses the kit's mana charge when a piece runs low (GearRecharge, report-only).
        var fighter = new FighterBuildScenario(table, classes, context.Log, template: template, spells: context.Spells, account: context.Account, endSession: false, rechargeGear: true);
        return new BuildRun(fighter, fighter.VerdictFields);
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

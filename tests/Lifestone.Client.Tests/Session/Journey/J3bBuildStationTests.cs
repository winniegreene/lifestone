using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The builds in a journey (M4 P5b J3b): <see cref="MageBuildScenario"/> and <see cref="FighterBuildScenario"/> with <c>endSession: false</c> skip their login when the session already holds the character in the world, never place or log off,
/// and still learn Lifestone Tie 2644 (idempotent, only adds); <see cref="BuildVerifyStation"/> picks the build by the context's character. The scripted ACE has no dats (the XpTable is synthetic).
/// </summary>
public sealed class J3bBuildStationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>A host that logs the named character into the world first (the journey's entry), then hands the session to the embedded run.</summary>
    private sealed class WarmLogin(string character, Func<IScenario> inner) : IScenario
    {
        private ActionHandle? _pending;
        private IScenario? _run;
        private string _stage = "start";

        public string Name => "j3b-warm";

        public string Stage => _run?.Stage ?? _stage;

        public string? Failure => _run?.Failure ?? (_stage == "failed" ? "the warm login failed" : null);

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public IScenario? Inner => _run;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (_run is not null)
            {
                return _run.Step(session, now);
            }

            switch (_stage)
            {
                case "start":
                    _pending = session.Actions.Login(ActionSource.Host);
                    _stage = "login";
                    break;
                case "login" when _pending is { Outcome: { } login }:
                    if (login.Kind != ActionOutcomeKind.Confirmed)
                    {
                        _stage = "failed";
                        return ScenarioStatus.Failed;
                    }

                    var slot = session.State.Characters.Entries.First(c => string.Equals(c.Name, character, StringComparison.OrdinalIgnoreCase));
                    _pending = session.Actions.EnterWorld(slot.Guid, ActionSource.Host);
                    _stage = "entering";
                    break;
                case "entering" when _pending is { Outcome: { } enter }:
                    if (enter.Kind != ActionOutcomeKind.Confirmed)
                    {
                        _stage = "failed";
                        return ScenarioStatus.Failed;
                    }

                    _stage = "in-world";
                    break;
                case "in-world" when session.State.Phase == Lifestone.Net.Session.SessionPhase.InWorld && session.State.Self.Guid != 0:
                    _run = inner();
                    break;
                case "failed":
                    return ScenarioStatus.Failed;
            }

            return ScenarioStatus.Running;
        }
    }

    private sealed class Run : IDisposable
    {
        public Run(string character, Func<WarmLogin> make, Action<ScriptedAceSession>? server, Func<ScriptedAceSession, XpTableData, ScriptedMage?>? mage, Func<ScriptedAceSession, XpTableData, ScriptedFighter?>? fighter)
        {
            Table = ScriptedMage.SyntheticTable();
            Scenario = make();
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 600 },
                Scenario,
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => World,
                Time,
                Log,
                Recording,
                Sockets.Open,
                (_, sink, tap) =>
                {
                    Server = new ScriptedAceSession(sink, tap, Time);
                    server?.Invoke(Server);
                    Server.Characters.Insert(0, new Lifestone.Tests.Shared.Net.AceCharacter(CharacterGuid, character));
                    Mage = mage?.Invoke(Server, Table);
                    Fighter = fighter?.Invoke(Server, Table);
                    Mage?.Attach();
                    Fighter?.Attach();
                    return Server;
                });
        }

        public const uint CharacterGuid = 0x50000050;

        public XpTableData Table { get; }

        public WarmLogin Scenario { get; }

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public ScriptedMage? Mage { get; private set; }

        public ScriptedFighter? Fighter { get; private set; }

        public HeadlessRunResult Finish(double maxSeconds = 600)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
                if (Server.PlayerGuid is not null)
                {
                    Fighter?.Pump();
                }
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }

    private static Run MageRun(Action<ScriptedMage>? shape = null)
    {
        Run? run = null;
        run = new Run(
            MageTemplate.CharacterName,
            () => new WarmLogin(MageTemplate.CharacterName, () => new MageBuildScenario(run!.Table, HeadlessCharacterTests.StandInClasses(), run.Log, endSession: false)),
            null,
            (server, t) =>
            {
                var mage = ScriptedMage.Ready(t, server, MageTemplate.CharacterName);
                shape?.Invoke(mage);
                return mage;
            },
            null);
        return run;
    }

    [Fact]
    public void An_embedded_mage_build_skips_its_login_and_its_log_off_and_only_checks_a_ready_mage()
    {
        using var run = MageRun();
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var scenario = Assert.IsType<MageBuildScenario>(run.Scenario.Inner);
        Assert.Equal("done", scenario.Stage); // not logging-off, not quitting
        Assert.False(scenario.Created);
        Assert.Single(run.Server.Credentials); // the host's login only
        Assert.Equal([Run.CharacterGuid], run.Server.EnterRequests); // the host's entry only
        Assert.Equal(Run.CharacterGuid, scenario.EnteredGuid);
        Assert.All(scenario.SentSteps, s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal)); // the spellbook is asked about and nothing is taught
        Assert.Equal(0, scenario.BuiltSteps);
        Assert.Matches(@"^mage=ready level=\d+ template=ok spells=\d+/\d+ built_steps=0$", scenario.VerdictFields());
        Assert.Contains(run.Log.Entries, e => e.Message.StartsWith("verdict mage=ready", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mage_that_lacks_only_Lifestone_Tie_learns_exactly_that_spell_and_a_second_run_teaches_nothing()
    {
        Assert.Contains(2644u, ArenaProfile.SpellsToLearn);

        using var first = MageRun(mage => mage.Known.Remove(2644));
        var firstResult = first.Finish();
        Assert.True(firstResult.Ok, firstResult.Failure + " / " + first.Scenario.Failure);
        var built = Assert.IsType<MageBuildScenario>(first.Scenario.Inner);
        Assert.Contains(2644u, first.Mage!.Known); // learned
        Assert.Equal(1, built.BuiltSteps);
        Assert.Equal($"mage=built level={MageTemplate.TargetLevel} template=ok spells={ArenaProfile.SpellsToLearn.Count}/{ArenaProfile.SpellsToLearn.Count} built_steps=1", built.VerdictFields());
        Assert.Equal(ArenaProfile.SpellsToLearn.Count, first.Mage.Known.Count);

        using var second = MageRun();
        Assert.True(second.Finish().Ok);
        Assert.Equal(0, Assert.IsType<MageBuildScenario>(second.Scenario.Inner).BuiltSteps);
        Assert.Contains(2644u, second.Mage!.Known);
    }

    [Fact]
    public void The_build_verify_station_builds_by_the_contexts_character_in_the_world_and_never_places()
    {
        // The Hunter stands outside Holtburg: the standalone build would place it there; the embedded one leaves that to the journey.
        Run? run = null;
        run = new Run(
            FighterTemplate.HunterName,
            () => new WarmLogin(FighterTemplate.HunterName, () =>
            {
                var context = new JourneyContext
                {
                    Character = FighterTemplate.HunterName,
                    Account = "test",
                    Log = run!.Log,
                    Travel = new PlaceTravel(),
                    Spells = FighterComponentsTests.SyntheticSpells(),
                };
                var station = new BuildVerifyStation(run.Table, HeadlessCharacterTests.StandInClasses());
                Assert.Equal("build-verify", station.Name);
                return station.Start(context);
            }),
            null,
            null,
            (server, t) => ScriptedFighter.Ready(t, server, FighterTemplate.HunterName));
        using var _ = run;
        run.Fighter!.InHoltburg = false;
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var station = Assert.IsAssignableFrom<IStationRun>(run.Scenario.Inner);
        Assert.True(station.Ok, station.Failure);
        Assert.Equal("done", station.Stage);
        Assert.Matches(@"^fighter=ready level=\d+ template=ok kit=ok ", station.VerdictFields());
        Assert.Single(run.Server.Credentials);
        Assert.DoesNotContain(run.Log.Entries, e => e.Message.StartsWith("place to Holtburg", StringComparison.Ordinal));
        Assert.Empty(run.Server.WorldActionSends);
        Assert.False(run.Fighter.InHoltburg); // still where the journey left it
    }

    [RequiresDatsTheory]
    [InlineData(80)]
    [InlineData(126)]
    public void The_build_verify_station_judges_the_mage_by_the_journeys_level_the_traditional_build_below_126_M5_7_F14(int level)
    {
        // The journey's --mage-level reaches the station: a ready mage of that level's plan (the traditional mage below 126, the gate's at 126) meets the template at that level and goes on to the
        // granting stage (the scripted ACE has no inventory, as in the granting proofs above); a station judging another level would not call the build met.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Run? run = null;
        run = new Run(
            MageTemplate.CharacterName,
            () => new WarmLogin(MageTemplate.CharacterName, () =>
            {
                var context = new JourneyContext { Character = MageTemplate.CharacterName, Account = "test", Log = run!.Log, Travel = new PlaceTravel(), Spells = spells };
                return new BuildVerifyStation(run.Table, HeadlessCharacterTests.StandInClasses(), level).Start(context);
            }),
            null,
            (server, t) => ScriptedMage.Ready(t, server, MageTemplate.CharacterName, MageTemplate.PlanAt(t, level)),
            null);
        using var _ = run;
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("granting", result.Failure ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(run.Log.Lines, l => l.Contains("the build ended met; granting the kit", StringComparison.Ordinal));
        Assert.Equal(level < MageTemplate.TargetLevel ? MageBuild.Traditional : MageBuild.Gate126, MageTemplate.BuildFor(level));
    }

    [Fact]
    public void The_journeys_factory_hands_the_build_verify_station_the_mage_level_option_and_126_by_default_M5_7_F14()
    {
        BuildVerifyStation Made(params string[] arguments) =>
            Assert.IsType<BuildVerifyStation>(JourneyStations.Create(JourneyPlans.Mage, arguments, null).Create(new StationSpec("build-verify", StationKinds.BuildVerify, null, 60)));

        Assert.Equal(MageTemplate.TargetLevel, Made().MageLevel); // the gate's journey is unchanged
        Assert.Equal(MageTemplate.TargetLevel, Made("--fight-ground=black-spawn-den-aluvian").MageLevel);
        Assert.Equal(80, Made("--mage-level=80").MageLevel);
        Assert.Equal(MageTemplate.MinLevel, Made($"--mage-level={MageTemplate.MinLevel}").MageLevel);
        Assert.Equal(80, JourneyStations.MageLevelOf(CommandLineArgument.ParseAll(["--mage-level=80"])));
        Assert.Equal(MageTemplate.TargetLevel, JourneyStations.MageLevelOf([]));
        foreach (var bad in new[] { "--mage-level=eighty", $"--mage-level={MageTemplate.TargetLevel + 1}", $"--mage-level={MageTemplate.MinLevel - 1}" })
        {
            var e = Assert.Throws<ArgumentException>(() => JourneyStations.Create(JourneyPlans.Mage, [bad], null));
            Assert.Contains("--mage-level", e.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_build_verify_station_knows_the_mage_and_the_fighters_and_refuses_any_other_character_loudly()
    {
        var spells = FighterComponentsTests.SyntheticSpells();
        var log = new RecordingLogSink();
        JourneyContext Context(string name) => new() { Character = name, Account = "test", Log = log, Travel = new PlaceTravel(), Spells = spells };
        var station = new BuildVerifyStation(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses());

        // M5 3 w1: the mage's build is made with the ground's kit plan, built from the context's SpellTable; this table holds the fighters' spells with their fighter formulas, so the plan fails by name (the mage's station with the dats' table is proved below).
        var noMageKit = Assert.ThrowsAny<Exception>(() => station.Start(Context(MageTemplate.CharacterName))); // M5 7 F16: the Lead Scarab line lets the plan reach a profile spell this table lacks: the failure is loud and by name either way
        Assert.True(noMageKit.Message.Contains("ground-plan-gap", StringComparison.Ordinal) || noMageKit.Message.Contains("is not in the SpellTable", StringComparison.Ordinal), noMageKit.Message);
        foreach (var name in new[] { FighterTemplate.Melee.Name, FighterTemplate.Archer.Name, FighterTemplate.HunterName })
        {
            Assert.Equal("fighter", station.Start(Context(name)).VerdictFields().Split('=')[0]);
        }

        var unknown = Assert.Throws<InvalidOperationException>(() => station.Start(Context("Lifestone Throwaway")));
        Assert.Contains("TODO(m4-p5b-build-character)", unknown.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new BuildVerifyStation().Start(Context(MageTemplate.CharacterName))); // no table, no dats: loud
    }

    /// <summary>Runs the mage build <paramref name="make"/> makes in a warm session over the scripted ACE (which answers every item request "does not do that"): the run ends in the granting stage, naming the grant, when the build carries a kit plan.</summary>
    private static (Run Run, HeadlessRunResult Result) GrantingRun(Func<Run, IScenario> make)
    {
        Run? run = null;
        run = new Run(
            MageTemplate.CharacterName,
            () => new WarmLogin(MageTemplate.CharacterName, () => make(run!)),
            null,
            (server, t) => ScriptedMage.Ready(t, server, MageTemplate.CharacterName),
            null);
        return (run, run.Finish());
    }

    [RequiresDatsFact]
    public void The_build_verify_station_builds_the_mage_with_the_journeys_dungeon_kit_plan_so_the_granting_stage_runs_M5_3_w1()
    {
        // M5 3 w1 (D46): the station reads the ground from the journey's context (--fight-ground), not the default alone: Tusker Temple's mage is granted Tusker Temple's kit.
        var tethana = GroundCatalog.Get("tusker-temple-fort-tethana");
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var plan = GearKit.KitPlan.For(tethana, spells, "test");
        var (run, result) = GrantingRun(r =>
        {
            var context = new JourneyContext { Character = MageTemplate.CharacterName, Account = "test", Log = r.Log, Travel = new PlaceTravel(), Spells = spells, Dungeon = tethana };
            return new BuildVerifyStation(r.Table, HeadlessCharacterTests.StandInClasses()).Start(context);
        });
        using var _ = run;

        Assert.False(result.Ok); // the scripted ACE has no inventory: the grant is refused, and that is the proof the stage ran
        Assert.Contains("granting", result.Failure ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(run.Log.Lines, l => l.Contains("the build ended met; granting the kit, the four foci, the stock and the purse of " + tethana.Name, StringComparison.Ordinal) && l.Contains(plan.LoadLine, StringComparison.Ordinal));
        Assert.Contains(run.Log.Lines, l => l.Contains("kit of " + tethana.Key + ":", StringComparison.Ordinal));
        Assert.Contains(plan.Kit.Items, i => i.Kind == GearKind.Armour && i.Wcid == 5910); // Tusker Temple's Dho robe, not the den's Faran robe
    }

    [RequiresDatsFact]
    public void The_headless_hosts_mage_build_carries_the_fight_grounds_kit_plan_so_the_granting_stage_runs_M5_3_w1()
    {
        // Program.cs makes --scenario=mage-build through BuildVerifyStation.MageBuildOf with options.Fight.GroundProfile (the den by default): the same factory, so the same granting stage.
        var ground = GroundCatalog.Get(FightOptions.DefaultGround);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var (run, result) = GrantingRun(r => BuildVerifyStation.MageBuildOf(r.Table, HeadlessCharacterTests.StandInClasses(), r.Log, ground, spells, "test", endSession: false));
        using var _ = run;

        Assert.False(result.Ok);
        Assert.Contains("granting", result.Failure ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(run.Log.Lines, l => l.Contains("the build ended met; granting the kit, the four foci, the stock and the purse of " + ground.Name, StringComparison.Ordinal));

        var program = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Headless", "Program.cs"));
        Assert.Contains("BuildVerifyStation.MageBuildOf(", program, StringComparison.Ordinal);
        Assert.Contains("options.Fight.GroundProfile", program[program.IndexOf("BuildVerifyStation.MageBuildOf(", StringComparison.Ordinal)..][..400], StringComparison.Ordinal);
        Assert.DoesNotContain("new MageBuildScenario(", program, StringComparison.Ordinal); // the host has no second way to make the mage build
    }

    [Fact]
    public void Both_fighters_learn_Lifestone_Tie_and_their_kits_hold_its_components()
    {
        var spells = FighterComponentsTests.SyntheticSpells();
        foreach (var profile in FighterProfile.All)
        {
            Assert.Contains(2644u, profile.SpellsToLearn);
            Assert.Equal(2644u, profile.LifestoneTie.Spells.Single().Id);
            Assert.Equal(ProfileRole.Travel, profile.LifestoneTie.Role);
            Assert.DoesNotContain(profile.LifestoneTie, profile.Lines.Where(l => l.IsBuff)); // a travel line, never in the buff cycle
            Assert.DoesNotContain(profile.BuffOrder, l => l.Name == profile.LifestoneTie.Name);
            Assert.Equal(FighterComponents.CastsPerGate(profile.LifestoneRecall), FighterComponents.CastsPerGate(profile.LifestoneTie));
            Assert.NotEmpty(FighterComponents.For(profile, spells, "test"));
        }

        // The mage learns it too, and its kit sizes the travel casts small (the burden has little headroom).
        Assert.Contains(2644u, ArenaProfile.FightExtras);
        Assert.Equal([1635u, 2644u], GearKit.KitTravelSpells);
        Assert.True(GearKit.TravelCasts <= FighterComponents.RecallCasts);
    }
}

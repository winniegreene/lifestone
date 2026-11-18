using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.World.Data;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 item 4 t3b (D50, D51, D43, D10, DoD 5), against the bundled ACE: the rung change by play, by the station the autopilot's Travel state runs (<see cref="RungChangeStation"/>, through the factory's <see cref="StationKinds.RungChange"/>), on
/// Lifestone Throwaway (account <c>test-tools</c>; the only character levelled freely, never Winnie: D10 keeps the live walk off the climber). The lead runs it; no slice does.
/// <para><b>What it proves.</b> (a) <see cref="The_Throwaway_at_level_10_without_Lifestone_Recall_runs_to_the_Burrow_by_the_Refuge_and_home_by_portals"/>: <see cref="ThrowawayReset"/> leaves <c>test-tools</c> with no Throwaway; the build
/// (<see cref="MageBuildScenario"/>, not fresh, level 10, then <see cref="ThrowawayReset.Refill"/>'s one level-up: 11) withholds Lifestone Recall 1635 from its spell grant (<c>withheldSpells</c>; the mod's <c>spell learn</c> simply does not
/// teach it, no server write removes it), so the book lacks it as the climber's does below 16 (D43); one setup place (D10's, counted) puts it at Holtburg's point of interest, the travel file's start; then one journey of two
/// stations: <c>rung-change</c> direction=out to the Tusker Burrow (the Facility Hub's Burrow portal is 15, so the travel file's <c>by-refuge</c>: Holtburg's Town Network portal, the hub's Refuge portal 43002 (10), the 609 m island run to the
/// entry 22663, the Use; present in landblock 0x5748), and <c>rung-change</c> direction=home (no Lifestone Recall: the Burrow's Surface portal 22664, then the island's way home by portals to Holtburg). Both <c>rung_change:</c> lines are
/// printed; each must say <c>ok=yes</c>, the out run's <c>enter</c> leg must end in 0x5748 and the home run's last leg in Holtburg.
/// (b) <see cref="The_Throwaway_at_level_60_runs_to_the_Holding_by_the_Barracks_drop"/>: the same reset and build to level 60 (61 after the refill; the book keeps Lifestone Recall), the setup place at Holtburg, then
/// <c>rung-change</c> direction=out to the Tusker Holding (the travel file's first route since M6 7 J, <c>by-facility-hub-barracks</c>: Holtburg's Town Network portal, the Facility Hub, the hub's Barracks portal (40), the island run of about 1.5 km to the
/// entry 22680 (60), the Use; present in 0x5B48): DoD 5's live walk, on the Throwaway; the case asserts the route the file chooses at 60. The <c>rung_change:</c> line is printed and must say <c>ok=yes</c>, its <c>enter</c> leg in 0x5B48.
/// (c) <see cref="The_Throwaway_at_level_15_runs_to_the_Burrow_by_the_Facility_Hub"/> (M6 7 J): the same reset and build to level 15, then <c>rung-change</c> direction=out to the Tusker Burrow by <c>by-facility-hub</c>: the first live walk of the hub (dats:
/// <c>FacilityHubWalkDatTests</c>), the two closed doors on it (0x78A020C7, 0x78A0209C) Used by the runner, the Steward and Curmudgeon the Drudge, who stand by the ramps' feet and no plan knows (<c>TODO(m6-npc-indoor-bodies)</c>), passed; the hub's Burrow portal 42177 (15), the 20.9 m run, the entry's Use.</para>
/// <para><b>Grounds.</b> The Burrow and the Holding are item 3's real profiles from <see cref="GroundCatalog"/> (<c>tusker-burrow</c>, <c>tusker-holding</c>); the journey is built on the ground object (<see cref="StandInJourney"/>, the context and the station factory on it) as <c>--fight-ground</c> would choose it, the only part that is test-side.</para>
/// <para><b>M6 4 F6 (the live round 4: the Throwaway, 15 health, never bound and unbuffed, was killed by island tuskers on the run and respawned at its sanctuary, the Training Academy).</b> The Throwaway is built as a viable mage: the
/// climber's creation (Strength, Endurance and Quickness 10, Coordination, Focus and Self 100: <see cref="MageBuild.Traditional"/>, Winnie's) and the climber's experience shares at the case's level (<see cref="MageTemplate.SharesAt"/>, the dance
/// split <see cref="LevelUpSpend"/> spends with; asserted), with the ground's supplies granted by the build (<see cref="GearKit.KitPlan.For"/>: the components the bind's tie, the buffs and the bolts burn, and the purse; the Throwaway may be granted,
/// Winnie never); the journey then binds it at Holtburg's lifestone (the autopilot's <see cref="AutopilotScenario.BindSpec"/>: Use, then Lifestone Tie; its <c>bound=</c> and <c>tied=</c> checked) and buffs it (the autopilot's start pass,
/// <see cref="AutopilotScenario.BuffSpec"/>) before the rung change. A death on a leg is <c>fail=died:&lt;leg&gt;:respawned=&lt;cell&gt;</c> and the run fights back an attacker (M5 D88), both the station's (TravelLegSafety.cs).</para>
/// <para>The test deletes and purges Lifestone Throwaway on <c>test-tools</c>, creates it again and changes it (level, spells, its place, where it walks); it enters <c>Lifestone Tools</c> only to purge (creating it once when absent) and touches
/// no other character. It leaves its Throwaway behind; the next Throwaway class's reset removes it.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[Trait("Lane", "solo")]
[LoopbackCharacter(Throwaway)]
public sealed class RungChangeLiveTests(ITestOutputHelper output)
{
    /// <summary>The character (D39).</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>The verdict line's prefix (<see cref="RungChangeStation"/>).</summary>
    private const string VerdictPrefix = "rung_change: ";

    /// <summary>The Tusker Burrow: the real profile (item 3's <c>tusker-burrow</c>: entry 22663, arrival, Surface exit and Holtburg's home are its data).</summary>
    private static HuntingGround Burrow => GroundCatalog.Get("tusker-burrow");

    /// <summary>The Tusker Holding: the real profile (item 3's <c>tusker-holding</c>: entry 22680, MinLevel 60, arrival, Surface exit and Holtburg's home are its data).</summary>
    private static HuntingGround Holding => GroundCatalog.Get("tusker-holding");

    private static StationSpec Rung(string direction) =>
        new(RungChangeStation.StationName, StationKinds.RungChange, null, 900, new Dictionary<string, string>(StringComparer.Ordinal) { [RungChangeStation.DirectionArgument] = direction });

    [LoopbackFact]
    public void The_Throwaway_at_level_10_without_Lifestone_Recall_runs_to_the_Burrow_by_the_Refuge_and_home_by_portals()
    {
        using var dats = DatSet.Open();
        var lines = BuildPlaceAndRun(dats, 10, [RecallHome.LifestoneRecallSpell], Burrow, [.. Ready, Rung("out"), Rung("home")], 2700);
        var verdicts = lines.Where(l => l.Contains(VerdictPrefix, StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, verdicts.Length);
        var (outLine, homeLine) = (Verdict(verdicts[0]), Verdict(verdicts[1]));
        output.WriteLine(outLine);
        output.WriteLine(homeLine);
        Assert.Contains(" route=by-refuge ", outLine, StringComparison.Ordinal);
        Assert.Contains(" ok=yes fail=none direction=out", outLine, StringComparison.Ordinal);
        Assert.Contains(" ok=yes fail=none direction=home", homeLine, StringComparison.Ordinal);
        AssertReady(lines);
        Assert.Contains(lines, l => l.Contains("rung_change plan: direction=home ", StringComparison.Ordinal) && l.Contains(" book: lifestone_recall=no ", StringComparison.Ordinal)); // the decision read the book without it
        var legs = lines.Where(l => l.Contains("rung_change_leg: ", StringComparison.Ordinal)).ToArray();
        Assert.Contains(legs, l => l.Contains(" kind=enter ", StringComparison.Ordinal) && l.Contains(" to=0x5748 ", StringComparison.Ordinal) && l.Contains(" ok=yes", StringComparison.Ordinal));
        var holtburg = TravelRoutes.Default.PlaceOf(TravelRoutes.Default.StartPlace)!.Landblocks.Select(RungRouteHex).ToArray();
        Assert.Contains(holtburg, lb => legs[^1].Contains(" to=" + lb + " ", StringComparison.Ordinal));
    }

    [LoopbackFact]
    public void The_Throwaway_at_level_60_runs_to_the_Holding_by_the_Barracks_drop()
    {
        using var dats = DatSet.Open();
        var lines = BuildPlaceAndRun(dats, 60, null, Holding, [.. Ready, Rung("out")], 2700);
        var verdict = Verdict(Assert.Single(lines, l => l.Contains(VerdictPrefix, StringComparison.Ordinal)));
        output.WriteLine(verdict);
        Assert.Contains(" route=" + TravelRoutes.Default.FirstAdmitted(Holding.Key, 60, 60).Route!.Key + " ", verdict, StringComparison.Ordinal); // the file's choice at 60 (the entry's gate 60)
        Assert.Contains(" route=by-facility-hub-barracks ", verdict, StringComparison.Ordinal);
        Assert.Contains(" ok=yes fail=none direction=out", verdict, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("rung_change_leg: ", StringComparison.Ordinal) && l.Contains(" kind=enter ", StringComparison.Ordinal) && l.Contains(" to=0x5B48 ", StringComparison.Ordinal) && l.Contains(" ok=yes", StringComparison.Ordinal));
        AssertReady(lines);
    }

    /// <summary>
    /// M6 7 J: case (c) of the summary, the walk the dats cannot prove whole: the hop out of the Facility Hub among its real objects. What it proves: from the drop the body walks down both ramps, the runner Uses the two doors the walk crosses, the two NPCs by the ramps' feet do not stop it, and the Burrow's portal is Used at
    /// level 15; the run's own <c>rung_change_leg:</c> lines give each leg's metres and milliseconds.
    /// </summary>
    [LoopbackFact]
    public void The_Throwaway_at_level_15_runs_to_the_Burrow_by_the_Facility_Hub()
    {
        using var dats = DatSet.Open();
        var lines = BuildPlaceAndRun(dats, 15, null, Burrow, [.. Ready, Rung("out")], 1800);
        var verdict = Verdict(Assert.Single(lines, l => l.Contains(VerdictPrefix, StringComparison.Ordinal)));
        output.WriteLine(verdict);
        Assert.Contains(" route=by-facility-hub ", verdict, StringComparison.Ordinal);
        Assert.Contains(" ok=yes fail=none direction=out", verdict, StringComparison.Ordinal);
        var legs = lines.Where(l => l.Contains("rung_change_leg: ", StringComparison.Ordinal)).ToArray();
        Assert.Contains(legs, l => l.Contains(" edge=hub-burrow ", StringComparison.Ordinal) && l.Contains(" ok=yes", StringComparison.Ordinal));
        Assert.Contains(legs, l => l.Contains(" kind=enter ", StringComparison.Ordinal) && l.Contains(" to=0x5748 ", StringComparison.Ordinal) && l.Contains(" ok=yes", StringComparison.Ordinal));
        AssertReady(lines);
    }

    /// <summary>M6 4 F6: the stations that make the Throwaway ready before the run, the autopilot's own: the bind at Holtburg's lifestone (Use, then Lifestone Tie) and the start's buff pass.</summary>
    private static StationSpec[] Ready => [AutopilotScenario.BindSpec, AutopilotScenario.BuffSpec];

    /// <summary>M6 4 F6: the bind and the buff stations ended ok, and the bind's fields say bound and tied (check after).</summary>
    private static void AssertReady(IReadOnlyList<string> lines)
    {
        Assert.Contains(lines, l => l.Contains("journey station=" + AutopilotScenario.BindSpec.Name + " ok=true ", StringComparison.Ordinal) && l.Contains(" bound=yes tied=yes ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("journey station=" + AutopilotScenario.BuffSpec.Name + " ok=true ", StringComparison.Ordinal));
    }

    private static string RungRouteHex(ushort landblock) => RungRoute.Hex(landblock);

    /// <summary>The verdict from <c>rung_change: </c> on.</summary>
    private static string Verdict(string line) => line[line.IndexOf(VerdictPrefix, StringComparison.Ordinal)..];

    /// <summary>The reset, the build to <paramref name="level"/> (withholding <paramref name="withheld"/>), the refill, the one setup place at Holtburg, then the journey of <paramref name="stations"/> on <paramref name="ground"/>; its log lines.</summary>
    private IReadOnlyList<string> BuildPlaceAndRun(DatSet dats, int level, IReadOnlyCollection<uint>? withheld, HuntingGround ground, IReadOnlyList<StationSpec> stations, double quitAfterSeconds)
    {
        var table = XpTableData.Load(dats);
        var account = LoopbackAccounts.For(Throwaway);

        // (0) the reset: no Throwaway left on test-tools (deleted if live, purged from Lifestone Tools, M4 D43).
        ThrowawayReset.Run(output, dats);

        // (1) the build (D39, D10): created, raised to the level, the arena spells granted but the withheld ones; M6 4 F6: the ground's supplies granted (the components and the purse); then the vitals refilled by one level-up.
        var buildLog = new RecordingLogSink();
        var kit = GearKit.KitPlan.For(ground, SpellTableData.Load(dats).Spells, account, stockOnly: true); // a kit-less ground: the foci and the stock (TODO(m6-loopback-burrow-kit) closed, M6 5)
        var build = new MageBuildScenario(table, HeadlessCharacter.RetailSkillClasses(dats), buildLog, freshCharacter: false, kitPlan: kit, level: level, characterName: Throwaway, withheldSpells: withheld);
        Assert.Equal(MageBuild.Traditional, build.Plan.Build); // M6 4 F6: the climber's creation (Winnie's: 10, 10, 10, 100, 100, 100; no Melee Defense)
        Assert.Same(MageShares.Dance, MageTemplate.SharesAt(level)); // and the climber's experience shares at this level (LevelUpSpend's XpSpendRule follows the level's shares)
        output.WriteLine($"[mage] the climber's build at {level}: {build.Plan.Build}, shares {MageTemplate.SharesAt(level).Name}; the ground's supplies {kit.LoadLine}");
        var built = Run("build", build, dats, buildLog, account, null, 1800);
        output.WriteLine("[mage] verdict " + build.VerdictFields());
        Assert.True(built.Ok, built.Failure ?? build.Failure);
        Assert.True(build.Created, "the build did not create the Throwaway, though the reset left none on test-tools");
        ThrowawayReset.Refill(output, dats, table);

        // (2) one setup place (D10's, counted): Holtburg's point of interest, the travel file's start place.
        var place = 0;
        using (var client = LiveNetClient.Create(dats, retail: false, Throwaway))
        {
            try
            {
                EnterTheWorld(client);
                Unhide(client);
                var holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg")).Location.Position;
                var arrival = PlaceAndArrive(client, new PlaceLocRequest(ToLocation(new AcPosition(holtburg.CellId, holtburg.Local, Quaternion.Identity)), NextId()));
                place++;
                output.WriteLine($"placed (setup, D10) at {arrival.Where.Cell} ({arrival.Where.X:F1}, {arrival.Where.Y:F1}): Holtburg, the travel file's start ({TravelRoutes.Default.StartPlace})");
                LogOffAndQuit(client);
            }
            finally
            {
                LeaveTheWorld(client);
            }
        }

        Assert.Equal(1, place);

        // (3) the rung change: no teleport budget (D40 is over: the travel is by play).
        var log = new RecordingLogSink();
        var plan = new JourneyPlan("rung-change", Throwaway, account, StationWhere.Here, 0, stations) { CheatFree = true };
        using var journey = new StandInJourney(plan, ground, dats, SpellTableData.Load(dats).Spells, log, new HeadlessJourneyRunnerHost(dats, log));
        var result = Run("rung-change", journey, dats, log, account, Throwaway, quitAfterSeconds);
        Assert.True(result.Ok, result.Failure ?? journey.Failure);
        return log.Lines;
    }

    /// <summary>Runs <paramref name="scenario"/> live as <paramref name="account"/> and prints its summary and every line of its log (the log-everything rule), as <c>BuyTripLiveTests</c> does.</summary>
    private HeadlessRunResult Run(string phase, IScenario scenario, DatSet dats, RecordingLogSink log, string account, string? character, double quitAfterSeconds)
    {
        var files = BackendProbe.MainCheckoutFiles;
        using var recording = new MemoryStream();
        LiveCharacter.Release(output, account);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = account, Character = character, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = quitAfterSeconds,
                },
                scenario,
                secrets,
                File.ReadAllBytes(files.PidsFile),
                dats,
                TimeProvider.System,
                log,
                recording);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }

        HeadlessRunResult result;
        var context = new GameThreadSynchronizationContext();
        try
        {
            using (context.Install())
            using (runner)
            {
                result = runner.Run(context, CancellationToken.None);
            }

            output.WriteLine(result.SummaryLine("memory"));
        }
        finally
        {
            var lines = log.Lines;
            output.WriteLine($"===== {phase}: {lines.Count} log lines =====");
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }

            output.WriteLine($"===== end of {phase} =====");
        }

        return result;
    }

    /// <summary>
    /// <see cref="HeadlessJourneyScenario"/>'s steps with a ground object in place of <c>--fight-ground</c> (the ground object is passed, not looked up by the command line): the context and the station factory are built on <paramref name="ground"/>, the keeper as
    /// <see cref="HeadlessJourneyScenario"/> builds it.
    /// </summary>
    private sealed class StandInJourney(JourneyPlan plan, HuntingGround ground, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, HeadlessJourneyRunnerHost host) : IScenario, IDisposable
    {
        private JourneyScenario? _journey;

        public string Name => JourneyScenario.ScenarioName;

        public string Character => plan.Character;

        public string Stage => _journey?.Stage ?? "start";

        public string? Failure => _journey?.Failure;

        public double? WhereMaxMetres => _journey?.WhereMaxMetres;

        public long Unmatched => _journey?.Unmatched ?? 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (_journey is null)
            {
                var keeper = JourneyStations.CreateKeeper(plan, session, spells, [], log); // the station's mana need (20%) is met by the keeper's pass, as in the headless journey
                var context = JourneyScenario.NewContext(plan, log, spells, keeper, dats, host, dungeon: ground);
                _journey = new JourneyScenario(plan, JourneyStations.Create(plan, [], dats, null, ground), context);
                log.Info("rung-change-live", $"the ground {ground.Key}: landblocks {string.Join(",", ground.Landblocks.Select(RungRouteHex))}, entry {ground.Entry?.Wcid.ToString(CultureInfo.InvariantCulture) ?? "none"}, arrival 0x{ground.Arrival.Cell:X8}");
            }

            host.Tick(session, now);
            return _journey.Step(session, now);
        }

        public void Dispose()
        {
            _journey?.Dispose();
            host.Dispose();
        }
    }
}

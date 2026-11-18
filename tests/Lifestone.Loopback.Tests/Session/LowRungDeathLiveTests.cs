using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
/// M6 item 6 slice H (DoD 13: "a scripted death at a low rung recovered"), against the bundled ACE: the autopilot's scripted death (the journey's death station, <see cref="AutopilotScenario.DeathSpec"/>: undress, lie down, die) on Lifestone Throwaway
/// (account <c>test-tools</c>; the only character levelled and killed freely, never Winnie, BSD Mage or another: D10), at two low rungs. <b>The lead runs it</b>, after slice C1 (<c>DeathHandler</c>'s <c>corpse_recovered=none-needed</c>: no corpse run below level 11,
/// ACE <c>Player_Death.cs</c> 512-516 and <c>Creature_Death.cs</c> 495-518 @95ef426d, where an empty corpse rots in 15 s) and slice D (the journey's <c>died:</c> recovery and its death station) are merged; no slice runs it (slices compile it only).
/// <para><b>What it proves.</b> <see cref="ThrowawayReset"/> leaves <c>test-tools</c> with no Throwaway; the climber's build at level L (<see cref="MageBuildScenario"/>, not fresh, <see cref="MageBuild.Traditional"/>, the book without Lifestone Recall as the climber's below 16,
/// D43; then <see cref="ThrowawayReset.Refill"/>'s one level-up), the stock-only kit (<see cref="GearKit.KitPlan.For"/> with <c>stockOnly: true</c>: the foci, the stock and the den vendor's Wand, in the pack, not wielded), one counted setup place at Holtburg (D10); the journey then
/// <b>wields the pack's caster first</b> (the loop's <c>StandInJourney</c> does what <see cref="PortalTieLeg"/> does before the magic mode: a wield into the Held slot, checked before and after; <c>TODO(m6-quest-loopbacks-wield)</c>, item 5's round 3: "no caster held" at the bind's tie)
/// and runs the stations <c>bind</c> (Use, Lifestone Tie), <c>buff</c>, [the rung change out, case (b)], <c>death</c> (the scripted lie-down; the ground is the journey's, <c>ground=&lt;rung&gt;</c>) and a short <c>den-fight</c> on the rung's ground.</para>
/// <para><b>(a) <see cref="The_Throwaway_at_level_6_dies_at_the_outskirts_and_the_climb_resumes"/></b> (<c>holtburg-outskirts</c>, level 6; 7 after the refill): the death station's line must say <c>deaths=1 route=walk corpse_recovered=none-needed vitae_grants=0 rebuffed=yes rearm=none</c>
/// (nothing drops below level 11, the wand and the robe are safe to 35, so the respawn, the rebuff and the fight are the whole recovery), and the fight after it must report <c>kills&gt;=1</c> ("the climb resumed"). <c>vitae=</c> is printed, report-only (the floor is 0.85 at level 6).
/// A one-kill fight first takes the Throwaway onto the ground, so the lie-down is on the outskirts (the lead, at the merge: a death at the lifestone would not prove a death at the rung); the respawn is at Holtburg's lifestone.</para>
/// <para><b>(b) <see cref="The_Throwaway_at_level_12_dies_in_the_Burrow_and_is_recovered_REPORT_ONLY"/></b> (<c>tusker-burrow</c>, level 12; 13 after the refill), <b>report-only in its first run</b> (D27: a new judge is report-only): after the rung change out to the Burrow the death station
/// is expected to say <c>route=rung-change</c> (no tie, untieable, D43) or <c>route=leg</c> (a death on the run leg, D67; live round 2) and <c>corpse_recovered=yes</c> or <c>none-needed</c> (0-1 unwielded item at 11-20), the items back; each expectation is printed met or not met and none fails the test. It also prints every corpse-arrival line
/// for <c>m4-corpse-run-range</c> (the distance at the corpse) and the death line's <c>items_back=</c>. The 609 m island run is item 4 round 4's death risk, so the buff and the run's fight-back matter.</para>
/// <para>The test deletes and purges Lifestone Throwaway on <c>test-tools</c>, creates it again and changes it (level, spells, supplies, its place, where it walks, dies and fights); it enters <c>Lifestone Tools</c> only to purge (creating it once when absent) and touches no other
/// character. It leaves its Throwaway behind; the next Throwaway class's reset removes it.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[Trait("Lane", "solo")]
[LoopbackCharacter(Throwaway)]
public sealed class LowRungDeathLiveTests(ITestOutputHelper output)
{
    /// <summary>The character (D39).</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>The death station's line prefix (the journey's verdict line of the station named <c>death</c>).</summary>
    private const string DeathLinePrefix = "journey station=death ok=";

    /// <summary>The fight station's name in this loopback's journeys (the autopilot's own, <see cref="AutopilotScenario.FightSpec"/>).</summary>
    private const string FightName = "den-fight";

    /// <remarks>M6 7 B (TODO(m6-lowrung-loopback-book) closed, M6 6 live round 2's finding): the build teaches the climber's book at the level (<see cref="ClimberBook"/>), not the den mage's 24 buff lines, whose mana the buff station's 900 s could not carry at level 6.</remarks>
    [LoopbackFact]
    public void The_Throwaway_at_level_6_dies_at_the_outskirts_and_the_climb_resumes()
    {
        using var dats = DatSet.Open();
        var ground = GroundCatalog.Get("holtburg-outskirts");
        var lines = BuildPlaceAndRun(dats, 6, ground, LowRungStations.Outskirts(ground), 2400, requireOk: true); // M6 7 P (live round 1c: skipped=precondition:where): the den-travel station (an outdoor ground's walk from Holtburg) before each fight, as the climb does
        var death = DeathLine(lines);
        output.WriteLine("the death station: " + death);
        output.WriteLine("vitae (report-only): " + (Field(death, "vitae") ?? "not on the line"));
        AssertField(death, "deaths", "1");
        AssertField(death, "route", "walk");
        AssertField(death, "corpse_recovered", "none-needed");
        AssertField(death, "vitae_grants", "0");
        AssertField(death, "rebuffed", "yes");
        AssertField(death, "rearm", "none");
        AssertReady(lines);
        var fights = lines.Where(l => l.Contains("journey station=" + FightName + " ok=", StringComparison.Ordinal)).ToArray();
        Assert.True(fights.Length == 2, $"expected two fight station lines (one before the death, one after), got {fights.Length}");
        var fight = fights[^1];
        output.WriteLine("the fight after the death: " + fight);
        var kills = Field(fight, "kills");
        Assert.True(kills is not null && int.Parse(kills, CultureInfo.InvariantCulture) >= 1, $"the fight after the death reports kills={kills ?? "(no kills= field)"}, not >= 1 (the climb did not resume): {fight}");
    }

    [LoopbackFact]
    public void The_Throwaway_at_level_12_dies_in_the_Burrow_and_is_recovered_REPORT_ONLY()
    {
        using var dats = DatSet.Open();
        var ground = GroundCatalog.Get("tusker-burrow");
        var rung = new StationSpec(RungChangeStation.StationName, StationKinds.RungChange, null, 900, new Dictionary<string, string>(StringComparer.Ordinal) { [RungChangeStation.DirectionArgument] = "out" });
        var lines = BuildPlaceAndRun(dats, 12, ground, [AutopilotScenario.BindSpec, AutopilotScenario.BuffSpec, rung, AutopilotScenario.DeathSpec, ShortFight(ground)], 3600, requireOk: false);
        var deathLines = lines.Where(l => l.Contains(DeathLinePrefix, StringComparison.Ordinal)).ToArray();
        output.WriteLine($"REPORT-ONLY (D27, a new judge): {deathLines.Length} death station line(s)");

        // M6 6 FXD2 (6-D12): the planned lie-down's line is taken by position. When the rung change itself died, the journey inserted a death station right after it (the corpse trip of the real death), so the planned lie-down is the second
        // death line and is a second scripted death on a character that is not where the test meant it to be: no expectation is meaningful for it, so the inserted station's line is judged instead and the planned one is only printed.
        var rungName = "journey station=" + RungChangeStation.StationName + " ok=";
        var rungDied = lines.Any(l => l.Contains(rungName + "false", StringComparison.Ordinal)) && lines.Any(l => l.Contains("the death station follows", StringComparison.Ordinal));
        var plannedAt = rungDied ? 1 : 0;
        var planned = deathLines.Length > plannedAt ? deathLines[plannedAt][deathLines[plannedAt].IndexOf("journey station=", StringComparison.Ordinal)..] : string.Empty;
        var death = planned;
        if (rungDied)
        {
            death = deathLines.Length > 0 ? deathLines[0][deathLines[0].IndexOf("journey station=", StringComparison.Ordinal)..] : string.Empty;
            output.WriteLine("REPORT-ONLY: the rung change died before the planned lie-down; the journey's inserted death station handled that death and is the one judged below.");
            output.WriteLine("the planned lie-down (a second scripted death, not judged): " + (planned.Length > 0 ? planned : "(none: the journey ended first)"));
        }

        output.WriteLine("the death station" + (rungDied ? " (the inserted one)" : string.Empty) + ": " + (death.Length > 0 ? death : "(none: the journey ended before the death station)"));
        Report("route=rung-change|leg", Field(death, "route") is "rung-change" or "leg", death); // live round 2: a death on the island run goes by route=leg to the travel place holding the corpse (D67)
        Report("corpse_recovered=yes|none-needed", Field(death, "corpse_recovered") is "yes" or "none-needed", death);
        Report("items_back= on the line", Field(death, "items_back") is not null, death);
        output.WriteLine($"items_back={Field(death, "items_back") ?? "n/a"} vitae={Field(death, "vitae") ?? "n/a"} vitae_grants={Field(death, "vitae_grants") ?? "n/a"} rebuffed={Field(death, "rebuffed") ?? "n/a"}");
        var rungLine = lines.FirstOrDefault(l => l.Contains("rung_change: ", StringComparison.Ordinal));
        output.WriteLine("the rung change (report-only): " + (rungLine ?? "(no rung_change: line)"));
        var arrivals = lines.Where(l => l.Contains("corpse", StringComparison.OrdinalIgnoreCase) && (l.Contains("arriv", StringComparison.OrdinalIgnoreCase) || l.Contains("range", StringComparison.OrdinalIgnoreCase) || l.Contains("distance", StringComparison.OrdinalIgnoreCase))).ToArray();
        output.WriteLine($"m4-corpse-run-range: {arrivals.Length} corpse arrival/range line(s)");
        foreach (var line in arrivals)
        {
            output.WriteLine("  " + line);
        }

        var fight = lines.FirstOrDefault(l => l.Contains("journey station=" + FightName + " ok=", StringComparison.Ordinal));
        output.WriteLine("the fight after the death (report-only): " + (fight ?? "(none)"));
    }

    /// <summary>Prints one report-only expectation, met or not, and never fails.</summary>
    private void Report(string expectation, bool met, string line) =>
        output.WriteLine($"REPORT-ONLY expectation {(met ? "MET" : "NOT MET")}: {expectation}" + (met ? string.Empty : $" (death line: {(line.Length > 0 ? line : "none")})"));

    private static StationSpec ShortFight(HuntingGround ground) => LowRungStations.ShortFight(ground);

    /// <summary>The first death station line. Case (a) has no station before the planned lie-down that could die, so the first is the planned one; case (b) takes its line by position itself (an inserted station comes first when the rung change dies).</summary>
    private static string DeathLine(IReadOnlyList<string> lines)
    {
        var line = lines.FirstOrDefault(l => l.Contains(DeathLinePrefix, StringComparison.Ordinal));
        Assert.True(line is not null, "no 'journey station=death ok=' line: the journey never ran the death station");
        return line[line.IndexOf("journey station=", StringComparison.Ordinal)..];
    }

    /// <summary>The value of <paramref name="field"/>= in <paramref name="line"/> (a whole token, so <c>kills</c> never reads <c>den_kills</c>), or null.</summary>
    private static string? Field(string line, string field)
    {
        var match = Regex.Match(line, @"(?:^|\s)" + Regex.Escape(field) + @"=(\S+)", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void AssertField(string line, string field, string expected) =>
        Assert.True(Field(line, field) == expected, $"the death station's {field}= is '{Field(line, field) ?? "(absent)"}', expected '{expected}': {line}");

    /// <summary>The bind and the buff stations ended ok, and the bind's fields say bound and tied (check after, as <c>RungChangeLiveTests</c> does).</summary>
    private static void AssertReady(IReadOnlyList<string> lines)
    {
        Assert.Contains(lines, l => l.Contains("journey station=" + AutopilotScenario.BindSpec.Name + " ok=true ", StringComparison.Ordinal) && l.Contains(" bound=yes ", StringComparison.Ordinal) && (l.Contains(" tied=yes ", StringComparison.Ordinal) || l.Contains(" tied=skipped ", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.Contains("journey station=" + AutopilotScenario.BuffSpec.Name + " ok=true ", StringComparison.Ordinal));
    }

    /// <summary>The reset, the climber's build at <paramref name="level"/> (no Lifestone Recall, as the climber's below 16), the refill, the one setup place at Holtburg, then the journey of <paramref name="stations"/> on <paramref name="ground"/>; its log lines.</summary>
    private IReadOnlyList<string> BuildPlaceAndRun(DatSet dats, int level, HuntingGround ground, IReadOnlyList<StationSpec> stations, double quitAfterSeconds, bool requireOk)
    {
        var table = XpTableData.Load(dats);
        var account = LoopbackAccounts.For(Throwaway);

        // (0) the reset: no Throwaway left on test-tools (deleted if live, purged from Lifestone Tools, M4 D43).
        ThrowawayReset.Run(output, dats);

        // (1) the build (D39, D10): the climber's creation, raised to the level, the arena spells but Lifestone Recall, the stock-only kit; then the vitals refilled by one level-up.
        var buildLog = new RecordingLogSink();
        // Live round 1 (the lead): the climber below 16 holds no Lifestone Recall (withheld below), so the kit carries no travel spell's components: an outdoor ground's supplies stock none (ground-plan-gap on Copper Scarab at the outskirts).
        var spells = SpellTableData.Load(dats).Spells;
        var kit = GearKit.KitPlan.For(ground, spells, account, stockOnly: true, withheld: [.. ClimberBook.WithheldAt(level), BindStation.LifestoneTieSpell]); // live round 2: the withheld recall, and the tie the bind then skips (tied=skipped), burn no Copper Scarab (no outskirts stock line)
        var build = new MageBuildScenario(table, HeadlessCharacter.RetailSkillClasses(dats), buildLog, freshCharacter: false, kitPlan: kit, level: level, characterName: Throwaway, withheldSpells: ClimberBook.WithheldAt(level)); // M6 7 B (TODO(m6-lowrung-loopback-book)): the climber's book at the level, not the den mage's 24 buff lines
        Assert.Equal(MageBuild.Traditional, build.Plan.Build);
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
                output.WriteLine($"placed (setup, D10) at {arrival.Where.Cell} ({arrival.Where.X:F1}, {arrival.Where.Y:F1}): Holtburg");
                LogOffAndQuit(client);
            }
            finally
            {
                LeaveTheWorld(client);
            }
        }

        Assert.Equal(1, place);

        // (3) the journey, cheat-free (no teleport budget): the caster wielded first, then the stations.
        var log = new RecordingLogSink();
        var plan = new JourneyPlan("low-rung-death", Throwaway, account, StationWhere.Here, 0, stations) { CheatFree = true, BuffProfile = BuffProfiles.Climber }; // M6 7 H (round 1: every one of the den's 24 lines refused 0x03FC, the book holds the climber's): the climb's own profile, so the station buffs the lines the book holds
        using var journey = new StandInJourney(plan, ground, dats, SpellTableData.Load(dats).Spells, log, new HeadlessJourneyRunnerHost(dats, log));
        var result = Run("low-rung-death", journey, dats, log, account, Throwaway, quitAfterSeconds);
        if (requireOk)
        {
            Assert.True(result.Ok, result.Failure ?? journey.Failure);
        }
        else
        {
            output.WriteLine($"REPORT-ONLY: the journey ended ok={result.Ok} failure={result.Failure ?? journey.Failure ?? "none"}");
        }

        return log.Lines;
    }

    /// <summary>Runs <paramref name="scenario"/> live as <paramref name="account"/> and prints its summary and every line of its log (the log-everything rule), as <c>RungChangeLiveTests</c> does.</summary>
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
    /// <see cref="HeadlessJourneyScenario"/>'s steps with a ground object in place of <c>--fight-ground</c> (as <c>RungChangeLiveTests</c>), plus the one step before them: the pack's caster wielded into the Held slot once the player is present
    /// (<see cref="CasterWield"/>, <c>TODO(m6-quest-loopbacks-wield)</c>). The journey is not stepped while the wield runs, so the bind's tie finds a caster in hand; no caster after <see cref="CasterWield.WieldSeconds"/> is a loud failure, never a guess.
    /// </summary>
    private sealed class StandInJourney(JourneyPlan plan, HuntingGround ground, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, HeadlessJourneyRunnerHost host) : IScenario, IDisposable
    {
        private readonly CasterWield _wield = new(log, "low-rung-death-live");
        private JourneyScenario? _journey;

        public string Name => JourneyScenario.ScenarioName;

        public string Character => plan.Character;

        public string Stage => _wield.Done ? _journey?.Stage ?? "start" : "wield";

        public string? Failure => _wield.Failure ?? _journey?.Failure;

        public double? WhereMaxMetres => _journey?.WhereMaxMetres;

        public long Unmatched => _journey?.Unmatched ?? 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (!_wield.Done && session.State.Self.Travel == SelfTravel.Present && _wield.Step(session, now) is { } status)
            {
                return status;
            }

            if (_journey is null)
            {
                var keeper = JourneyStations.CreateKeeper(plan, session, spells, [], log);
                var context = JourneyScenario.NewContext(plan, log, spells, keeper, dats, host, dungeon: ground);
                _journey = new JourneyScenario(plan, JourneyStations.Create(plan, [], dats, null, ground), context);
                log.Info("low-rung-death-live", $"the ground {ground.Key}: landblocks {string.Join(",", ground.Landblocks.Select(lb => RungRoute.Hex(lb)))}, arrival 0x{ground.Arrival.Cell:X8}");
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

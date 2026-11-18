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
using Lifestone.Client.Session.Quests;
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
/// M6 item 5 (Q3; DoD 7's loopback, D54), against the bundled ACE: the kill task's shape by play, through the quest station (<see cref="QuestStation"/>, the factory's <see cref="StationKinds.Quest"/>) on Lifestone Throwaway (account <c>test-tools</c>;
/// the only character levelled freely, never Winnie: D10). The lead runs it, after Q4 (the fight's <c>ground=</c>, <c>want=</c>, <c>until=</c>), Q6a (the quest file <c>blood-shreth-kill-task</c>) and Q6b (the travel places Shoushi and
/// the <c>cave-of-alabree</c> profile and route) are merged; no slice does. Gate area <c>m6-quests</c>.
/// <para><b>What it proves.</b> <see cref="ThrowawayReset"/> leaves <c>test-tools</c> with no Throwaway; the build (<see cref="MageBuildScenario"/>, the climber's creation and shares, level <see cref="ClimberBook.ItemTrainedBuildLevel"/>, 33 after <see cref="ThrowawayReset.Refill"/>; Blood
/// Shreth 4110 is level 8) keeps Lifestone Recall 1635 (D60: the way home from the cave is the recall; the climber's own walk home from the Surface exit is `TODO(m6-alabree-way-home)`, item 9, placed with the climber's kill-task call, since the travel file has no run-home form for that exit) and grants the <c>cave-of-alabree</c> ground's supplies (<see cref="GearKit.KitPlan.For"/>); one setup place (D10's, counted) at Holtburg, the
/// travel file's start; then, in two journeys with one more counted setup place between them (D62). <b>M6 7 H (round 1: at level 10 Item Enchantment was untrained, tied=no, and the den's 24 lines took 900 s):</b> the level is the first whose credits pay for Item Enchantment in the build's order, so the tie and the
/// recall are cast at 0.99 and more (the pin in <c>ClimberBookTests</c>; the case asserts <c>tied=yes</c>), the book is the climber's (<see cref="ClimberBook.WithheldAt"/>: the recall kept, the buff lines by play), and the journeys buff by <see cref="BuffProfiles.Climber"/> (<see cref="JourneyPlan.BuffProfile"/>).
/// <b>Journey 1</b> = the bind and the buff (the autopilot's <see cref="AutopilotScenario.BindSpec"/> and <see cref="AutopilotScenario.BuffSpec"/>), then <c>quest quest=blood-shreth-kill-task from=home steps=0-1</c>: to San Ming 31641 at Shoushi by the Town Network (42840), the talk that arms the task
/// (SetQuestCompletions, the start text); its verdict reads <c>done=partial</c> and records nothing. <b>The setup place</b> (counted, the Throwaway only, as <c>RungChangeLiveTests</c> counts its one) puts the armed Throwaway into the Cave of Alabree at the entry portal's destination 0x016201D1 (60, -10, 12) (the world's portal row 4937; notes 13:30). <b>Journey 2</b> =
/// <c>quest quest=blood-shreth-kill-task from=ground steps=2-4</c>: the ground fight on the Cave of Alabree as the station's sub-run (no travel, the body is on the ground; <c>until=chat:Your task is complete!</c>), ACE's counter lines (QuestManager.HandleKillTask,
/// @95ef426d Managers/QuestManager.cs:583-626) up to "Your task is complete!", the recall home (Lifestone Recall), the Town Network hop to San Ming, the collect talk: the reward line <c>You've earned N experience.</c> with N above 0 (AwardLevelProportionalXP, Player_Xp.cs:114) and the
/// experience property risen, and the quest's timer recorded and saved through the host's store (<see cref="JourneyContext.CompleteQuest"/>). Asserted: journey 1's verdict <c>done=partial ok=yes steps=0-1</c> and the arm line; journey 2's verdict <c>done=yes ok=yes timer=saved</c> with <c>xp=+</c> and <c>reward_line=</c> above 0; the kill step's counter read (the last counter line n=10 and complete, the step line's
/// counter=10/10; not the number of lines, which the chat's 500-line ring may have cut: review 5-R8); the store's file holds the quest's entry.</para>
/// <para><b>What it does not prove.</b> The walk into the cave from the entry portal's room (<c>TODO(m6-alabree-entry-walk)</c>, with the ramp's step-down <c>m6-ramp-step-down</c>; both placed item 9 with the climber's kill-task call, D62), so the cave is entered by the setup place; the climber's walk home from the Surface exit (<c>TODO(m6-alabree-way-home)</c>);
/// the autopilot's call of a kill task between hunts (item 9's round, DoD 12); the Tusker Guard task (500 kills); a death on the run (the travel legs' fight-back and death names are item 4's); the 20 h timer's expiry (only its next-due time is read); a second run within the timer (San Ming's "give them a day" reply). <b>Places counted: two</b> (Holtburg, the travel file's start; then the cave).</para>
/// <para>The test deletes and purges Lifestone Throwaway on <c>test-tools</c>, creates it again and changes it (level, spells, supplies, its place, where it walks and fights, its quest registry); it enters <c>Lifestone Tools</c> only to purge
/// (creating it once when absent) and touches no other character. It leaves its Throwaway behind; the next Throwaway class's reset removes it.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[Trait("Lane", "solo")]
[LoopbackCharacter(Throwaway)]
public sealed partial class KillTaskLiveTests(ITestOutputHelper output)
{
    /// <summary>The character (D39).</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>The quest file (Q6a's, D54).</summary>
    private const string QuestKey = "blood-shreth-kill-task";

    /// <summary>The ground the kill step hunts (Q6b's profile, D54).</summary>
    private const string GroundKey = "cave-of-alabree";

    /// <summary>The verdict line's prefix.</summary>
    private const string VerdictPrefix = "quest: key=";

    /// <summary>The bolt level the fight picks at this build (round 1c's log: Flame Bolt IV; no row of the cave's plan caps it past level 20).</summary>
    private const int OffPlanBoltLevel = 4;

    /// <summary>The casts of it the rig's kit carries: ten Blood Shreths and the misses (the rig's; the kill task needs no more, and the fight falls to the stock's levels if they run out).</summary>
    private const int OffPlanCasts = 60;

    private static StationSpec[] Ready => [AutopilotScenario.BindSpec, AutopilotScenario.BuffSpec];

    private static StationSpec Quest(string from, string steps) => new(
        QuestStation.StationName,
        StationKinds.Quest,
        null,
        5400,
        new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyPlans.QuestArgument] = QuestKey, [JourneyPlans.FromArgument] = from, [JourneyPlans.StepsArgument] = steps });

    [LoopbackFact]
    public void The_Throwaway_at_level_32_arms_San_Ming_kills_ten_Blood_Shreths_collects_and_saves_the_timer()
    {
        using var dats = DatSet.Open();
        var store = new MemoryCheckpointStore();
        var ground = GroundCatalog.Get(GroundKey);
        var account = LoopbackAccounts.For(Throwaway);
        Build(dats, ClimberBook.ItemTrainedBuildLevel, ground, account);
        var places = PlaceAt(dats, Holtburg(), "Holtburg, the travel file's start");

        // journey 1: the bind, the buff, the go to Shoushi and the arm (steps 0-1).
        var first = RunJourney(dats, "arm", [.. Ready, Quest(JourneyPlans.FromHome, "0-1")], ground, account, store, 3600);
        var armVerdict = Verdict(Assert.Single(first, l => l.Contains(VerdictPrefix + QuestKey, StringComparison.Ordinal)));
        output.WriteLine(armVerdict);
        Assert.Contains(" done=partial ", armVerdict, StringComparison.Ordinal);
        Assert.Contains(" ok=yes fail=none steps=0-1", armVerdict, StringComparison.Ordinal);
        Assert.Contains(first, l => l.Contains("is the kill task's arm", StringComparison.Ordinal));
        Assert.DoesNotContain(QuestKey, store.Json ?? string.Empty, StringComparison.Ordinal); // a partial range records nothing
        Assert.Contains(first, l => l.Contains("journey station=" + AutopilotScenario.BindSpec.Name + " ok=true ", StringComparison.Ordinal) && l.Contains(" bound=yes tied=yes ", StringComparison.Ordinal));

        // the second counted setup place (D62, D10): into the Cave of Alabree, the entry portal's destination (the walk in is TODO(m6-alabree-entry-walk)).
        places += PlaceAt(dats, new AcPosition(0x016201D1, new Vector3(60f, -10f, 12f), Quaternion.Identity), "the Cave of Alabree, the entry portal's destination");
        Assert.Equal(2, places);

        // journey 2: the kill in the cave, the recall home, the hop to Shoushi, the collect (steps 2-4).
        var lines = RunJourney(dats, "kill-collect", [Quest(JourneyPlans.FromGround, "2-4")], ground, account, store, 7200);
        var verdict = Verdict(Assert.Single(lines, l => l.Contains(VerdictPrefix + QuestKey, StringComparison.Ordinal)));
        output.WriteLine(verdict);
        Assert.Contains(" from=ground ", verdict, StringComparison.Ordinal);
        Assert.Contains(" done=yes ", verdict, StringComparison.Ordinal);
        Assert.Contains(" timer=saved ok=yes fail=none steps=2-4", verdict, StringComparison.Ordinal);
        Assert.True(Number(verdict, "xp=+") > 0, "the experience property did not rise: " + verdict);
        Assert.True(Number(verdict, "reward_line=") > 0, "no reward line named experience: " + verdict);

        var counter = Assert.Single(lines, l => l.Contains("kill: counter lines ", StringComparison.Ordinal));
        output.WriteLine(counter);
        Assert.Contains(QuestReads.TaskComplete, counter, StringComparison.Ordinal);
        Assert.Contains("(n=10, complete=yes)", counter, StringComparison.Ordinal); // the last counter line, whatever the ring kept before it
        Assert.Contains(lines, l => l.Contains("quest_step: ", StringComparison.Ordinal) && l.Contains(" kind=kill ", StringComparison.Ordinal) && l.Contains(" ok=yes ", StringComparison.Ordinal) && l.Contains("counter=10/10 complete=yes", StringComparison.Ordinal));
        Assert.Contains(lines, l => RewardLine().IsMatch(l));
        Assert.Contains(lines, l => l.Contains("quest key=" + QuestKey + " ", StringComparison.Ordinal) && l.Contains("saved=yes", StringComparison.Ordinal));
        Assert.Contains(QuestKey, store.Json ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("no-components after", StringComparison.Ordinal)); // M6 7 K: the fight cast only what the pack paid for: not one 0x0400
    }

    private static AcPosition Holtburg()
    {
        var holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg")).Location.Position;
        return new AcPosition(holtburg.CellId, holtburg.Local, Quaternion.Identity);
    }

    [GeneratedRegex(@"reward: ""You've earned [1-9][0-9,]* experience\.""", RegexOptions.CultureInvariant)]
    private static partial Regex RewardLine();

    /// <summary>The integer after <paramref name="field"/> in <paramref name="line"/>.</summary>
    private static long Number(string line, string field)
    {
        var at = line.IndexOf(field, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {field} in: {line}");
        var digits = new string(line[(at + field.Length)..].TakeWhile(char.IsAsciiDigit).ToArray());
        return long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>The verdict from <c>quest: key=</c> on.</summary>
    private static string Verdict(string line) => line[line.IndexOf(VerdictPrefix, StringComparison.Ordinal)..];

    /// <summary>The reset, the build to <paramref name="level"/> (the ground's supplies granted; Lifestone Recall kept, D60), the refill.</summary>
    private void Build(DatSet dats, int level, HuntingGround ground, string account)
    {
        var table = XpTableData.Load(dats);

        // (0) the reset: no Throwaway left on test-tools (deleted if live, purged from Lifestone Tools, M4 D43).
        ThrowawayReset.Run(output, dats);

        // (1) the build (D39, D10): the climber's creation and shares, raised to the level, the arena spells, the ground's supplies; then the vitals refilled by one level-up.
        var buildLog = new RecordingLogSink();
        // M6 7 K (round 1c: the cave's plan stops at level 20, so the fight at 33 chose Flame Bolt IV by its skill alone, and the stock held no scarab for it): the rig's kit carries that level beside the stock.
        var offPlan = new GearKit.OffPlanBolts([ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells[OffPlanBoltLevel - 1].Id], OffPlanCasts);
        var kit = GearKit.KitPlan.For(ground, SpellTableData.Load(dats).Spells, account, stockOnly: true, offPlan: offPlan); // a kit-less ground: the foci and the stock (TODO(m6-loopback-burrow-kit) closed, M6 5)
        var build = new MageBuildScenario(table, HeadlessCharacter.RetailSkillClasses(dats), buildLog, freshCharacter: false, kitPlan: kit, level: level, characterName: Throwaway, withheldSpells: ClimberBook.WithheldAt(level)); // M6 7 H: the climber's book at the level (the recall kept: D60)
        Assert.Equal(MageBuild.Traditional, build.Plan.Build);
        output.WriteLine($"[mage] the climber's build at {level}: {build.Plan.Build}, shares {MageTemplate.SharesAt(level).Name}; the ground's supplies {kit.LoadLine}");
        var built = Run("build", build, dats, buildLog, account, null, 1800);
        output.WriteLine("[mage] verdict " + build.VerdictFields());
        Assert.True(built.Ok, built.Failure ?? build.Failure);
        Assert.True(build.Created, "the build did not create the Throwaway, though the reset left none on test-tools");
        ThrowawayReset.Refill(output, dats, table);
    }

    /// <summary>One setup place (D10's, counted; the tools' place, the Throwaway only): enter, place at <paramref name="where"/>, log the arrival, leave. Returns 1.</summary>
    private int PlaceAt(DatSet dats, AcPosition where, string why)
    {
        using var client = LiveNetClient.Create(dats, retail: false, Throwaway);
        try
        {
            EnterTheWorld(client);
            Unhide(client);
            var arrival = PlaceAndArrive(client, new PlaceLocRequest(ToLocation(where), NextId()));
            output.WriteLine($"placed (setup, D10, counted) at {arrival.Where.Cell} ({arrival.Where.X:F1}, {arrival.Where.Y:F1}, {arrival.Where.Z:F1}): {why}");
            LogOffAndQuit(client);
            return 1;
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>One cheat-free journey of <paramref name="stations"/> (no teleport budget: the travel is by play; the quest timers through <paramref name="store"/>); its log lines.</summary>
    private IReadOnlyList<string> RunJourney(DatSet dats, string phase, IReadOnlyList<StationSpec> stations, HuntingGround ground, string account, MemoryCheckpointStore store, double quitAfterSeconds)
    {
        var log = new RecordingLogSink();
        var plan = new JourneyPlan("kill-task-" + phase, Throwaway, account, StationWhere.Here, 0, stations) { CheatFree = true, BuffProfile = BuffProfiles.Climber }; // M6 7 H: the climb's own profile: the lines the book holds
        using var journey = new StandInJourney(plan, ground, dats, SpellTableData.Load(dats).Spells, log, new HeadlessJourneyRunnerHost(dats, log), store);
        var result = Run(phase, journey, dats, log, account, Throwaway, quitAfterSeconds);
        Assert.True(result.Ok, result.Failure ?? journey.Failure);
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

    /// <summary><c>RungChangeLiveTests</c>' stand-in journey with the quest timers' store: the context and the station factory on <paramref name="ground"/>, the keeper as the headless journey builds it, and the pack's caster wielded first (<see cref="CasterWield"/>).</summary>
    private sealed class StandInJourney(JourneyPlan plan, HuntingGround ground, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, HeadlessJourneyRunnerHost host, IAutopilotCheckpointStore store) : IScenario, IDisposable
    {
        private readonly CasterWield _wield = new(log, "kill-task-live");
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

            // M6 7 B (TODO(m6-quest-loopbacks-wield)): the Wand is in the pack, not wielded, so the pack's caster is wielded before the bind's tie (a caster already in hand needs none: journey 2).
            if (!_wield.Done && session.State.Self.Travel == SelfTravel.Present && _wield.Step(session, now) is { } status)
            {
                return status;
            }

            if (_journey is null)
            {
                var keeper = JourneyStations.CreateKeeper(plan, session, spells, [], log);
                var context = JourneyScenario.NewContext(plan, log, spells, keeper, dats, host, dungeon: ground, questStore: store);
                _journey = new JourneyScenario(plan, JourneyStations.Create(plan, [], dats, null, ground), context);
                log.Info("kill-task-live", $"the ground {ground.Key}: landblocks {string.Join(",", ground.Landblocks.Select(l => RungRoute.Hex(l)))}, entry {ground.Entry?.Wcid.ToString(CultureInfo.InvariantCulture) ?? "none"}, arrival 0x{ground.Arrival.Cell:X8}; the quest {QuestKey}");
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

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
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.World.Data;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 item 5 (Q5; DoD 8, D57, D58), against the bundled ACE: the trophy quest <c>tusk-female</c> (Q6a's file: hunt-until the Female Tusker Tusk 22423 on <c>tusker-burrow</c>, go to Brighteyes 22642, give; the 20 h timer) by play, through the
/// quest station the autopilot's errand runs (<see cref="QuestStation"/>, <see cref="AutopilotScenario.QuestSpec"/>), on Lifestone Throwaway (account <c>test-tools</c>; the only character levelled freely or granted, never Winnie: D10). The lead
/// runs it; no slice does. Gate area <c>m6-quests</c>.
/// <para><b>Both cases.</b> <see cref="ThrowawayReset"/> leaves <c>test-tools</c> with no Throwaway; the build (<see cref="MageBuildScenario"/>, the climber's creation and shares) grants the Burrow's supplies (<see cref="GearKit.KitPlan.For"/>); <see cref="ThrowawayReset.Refill"/>'s level-up; one setup
/// place (D10's, counted) at Holtburg, the travel file's start; then one cheat-free journey that binds at Holtburg's lifestone and buffs (the autopilot's <see cref="AutopilotScenario.BindSpec"/> and <see cref="AutopilotScenario.BuffSpec"/>, on the climb's own profile <see cref="BuffProfiles.Climber"/>,
/// <see cref="JourneyPlan.BuffProfile"/>), with the quest timers through a store (<see cref="JourneyContext.CompleteQuest"/>). The quest runs by the station's <c>steps=&lt;from&gt;-&lt;to&gt;</c> (D62: the file's steps are 0 hunt-until, 1 go, 2 give). A death on the run or a go that fails is a named failure before
/// the verdict is read (<see cref="TrophyRunRead"/>), the Refuge's six Sliding Door weenies 0x7F682000-05 named by <c>brighteyes-walk:door</c>: they stand at the sills and were never tested live.</para>
/// <para><b>(a) <see cref="A_granted_tusk_is_walked_to_Brighteyes_and_given_and_the_Throwaway_goes_home_by_the_Refuge"/></b>: level 14 (15 after the refill), the climber's book at the level (<see cref="ClimberBook.WithheldAt"/>: no Lifestone Recall, D43, so the bind's tie is skipped, <c>tied=skipped</c>): the path the
/// climber takes below 16. The journey = the grant of one Female Tusker Tusk 22423 (the mod's item verb; D10, the Throwaway only; a grant is not a corpse pick-up, so the item's 20 h pick-up gate does not apply), the bind, the buff, <c>quest quest=tusk-female from=home steps=1-2</c> (the go: the Town Network hops
/// <c>holtburg-to-network</c>, <c>network-to-refuge</c> and the walk from the Refuge's arrival up the turned stairs, M6 7 A; the give: her reply, the reward line <c>You've earned 100,000 experience.</c> from AwardXP, @95ef426d Player_Xp.cs:114, the gem 22729 arrived, the timer saved) and <c>rung-change</c> home by the Refuge's
/// portals (<c>route=refuge</c>; the walk back down the stairs is <c>BrighteyesWalkDatTests</c>'). Asserted: <c>tied=skipped</c>; the go step's <c>via=town-network+walk</c>; the <c>quest:</c> verdict <c>done=yes ok=yes fail=none timer=saved steps=1-2</c>, <c>xp=+</c> at least 100,000 and <c>reward_line=100000</c>; the gem line
/// (<c>give: Aphus Lassel Portal Gem arrived</c>) and the give step's line; the store holds <c>tusk-female</c>; the rung change home <c>ok=yes</c> with <c>lifestone_recall=no</c>. <b>Places counted: one</b> (Holtburg; D62's place beside her is gone: the walk replaced it).</para>
/// <para><b>(b) <see cref="The_Burrow_hunt_takes_a_tusk_live_the_Throwaway_gives_it_to_Brighteyes_and_recalls_home"/></b>: a viable mage at level <see cref="HuntLevel"/> (61 after the refill: Item Enchantment trained, the tie and the recall at 0.99 and more, the pin in <c>ClimberBookTests</c>; RungChangeLiveTests' level for the
/// island's longest run, since round 1's level 30 died on the 609 m run, 69 health, three hits 86 damage), the whole book; one journey = the bind, the buff, <c>rung-change</c> out to the Burrow (by the Refuge's portals), <c>quest quest=tusk-female from=ground steps=0-2</c> (the hunt-until fight: the den fight with <c>ground=tusker-burrow</c>,
/// <c>want=22423</c>, <c>until=item:22423x1</c>, until the tusk is held: a corpse pick-up of a carrier 22589, ACE's pick-up gate passed for a fresh character; the go: the book holds the recall, so the home plan's first leg is no exit hop: the rung change home, the Town Network hops, the walk; the give), then <c>rung-change</c> home (the recall). Asserted:
/// the tusk held (the fight's <c>item 22423 held 1 of 1</c>, the step's <c>held=1/1 ... until=met</c>), the go's <c>via=rung-change-home+town-network+walk</c>, the verdict <c>done=yes ok=yes timer=saved steps=0-2</c> and the store's entry, the rung change home <c>ok=yes</c>. A death on the island run is
/// <c>trophy:died-on-the-run:rung-change</c> by name, not a pass and not a rig failure (<c>m6-facility-hub-rooms</c> would remove the run from 15 up). Places counted: one (Holtburg).</para>
/// <para><b>What it does not prove.</b> The exit hop and the island walk from the Burrow's drop to the Refuge (the errand's <c>from=ground</c> without Lifestone Recall, D58, 616.6 m on the dats, <c>BrighteyesWalkDatTests</c>; gate A's Winnie below 16 runs it); the autopilot's own call (the rule, the want on the level up fight, the call at the trip,
/// the resume and the refusals are <c>TrophyQuestTests</c>' over the scripted ACE; gate A runs it on Winnie); the timer's expiry and a second turn-in within 20 h (ACE's pick-up refusal is <c>FightWantTests</c>' read); a death on the run (item 4's leg names); the other eight tusks.</para>
/// <para>The test deletes and purges Lifestone Throwaway on <c>test-tools</c>, creates it again and changes it (level, spells, supplies, one granted tusk, its places, where it walks and fights, its quest registry); it enters <c>Lifestone
/// Tools</c> only to purge (creating it once when absent) and touches no other character. It leaves its Throwaway behind; the next Throwaway class's reset removes it.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[Trait("Lane", "solo")]
[LoopbackCharacter(Throwaway)]
public sealed partial class TrophyLiveTests(ITestOutputHelper output)
{
    /// <summary>The character (D39).</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>The quest file (Q6a's, D57).</summary>
    private const string QuestKey = "tusk-female";

    /// <summary>The Female Tusker Tusk (the quest file's hunt-until item).</summary>
    private const uint Tusk = 22423;

    /// <summary>The verdict line's prefix.</summary>
    private const string VerdictPrefix = "quest: key=";

    /// <summary>Case (a)'s level: below <see cref="ClimberBook.RecallLevel"/>, the climber's path by portals, and past the Refuge portal's gate 10.</summary>
    private const int WalkLevel = 14;

    /// <summary>Case (b)'s level: M6 7 H: <c>RungChangeLiveTests</c>' level for the island's longest run; the book holds every line and Item Enchantment is trained.</summary>
    public const int HuntLevel = 60;

    private static HuntingGround Burrow => GroundCatalog.Get("tusker-burrow");

    private static StationSpec[] Ready => [AutopilotScenario.BindSpec, AutopilotScenario.BuffSpec];

    private static StationSpec Rung(string direction) =>
        new(RungChangeStation.StationName, StationKinds.RungChange, null, 900, new Dictionary<string, string>(StringComparer.Ordinal) { [RungChangeStation.DirectionArgument] = direction });

    /// <summary>The autopilot's own spec (<see cref="AutopilotScenario.QuestSpec"/>) with the journey's longer bound for the hunt.</summary>
    private static StationSpec Quest(string from, string steps) =>
        AutopilotScenario.QuestSpec(QuestKey, from) with { ExpectedSeconds = 5400, Args = new Dictionary<string, string>(AutopilotScenario.QuestSpec(QuestKey, from).Arguments, StringComparer.Ordinal) { [JourneyPlans.StepsArgument] = steps } };

    [LoopbackFact]
    public void A_granted_tusk_is_walked_to_Brighteyes_and_given_and_the_Throwaway_goes_home_by_the_Refuge()
    {
        using var dats = DatSet.Open();
        var store = new MemoryCheckpointStore();
        var account = LoopbackAccounts.For(Throwaway);
        Build(dats, WalkLevel, Burrow, account, ClimberBook.WithheldAt(WalkLevel));
        Assert.Equal(1, PlaceAt(dats, Holtburg(), "Holtburg, the travel file's start"));

        // the grant, the bind, the buff; the go by the hops and the walk, the give (steps 1-2); home by the Refuge's portals, the climber's path below 16.
        var lines = RunJourney(dats, "give", [.. Ready, Quest(JourneyPlans.FromHome, "1-2"), Rung("home")], Burrow, account, store, grant: Tusk, 2700);
        Assert.Contains(lines, l => l.Contains("journey station=" + AutopilotScenario.BindSpec.Name + " ok=true ", StringComparison.Ordinal) && l.Contains(" bound=yes tied=skipped ", StringComparison.Ordinal));
        var verdict = Verdict(Assert.Single(lines, l => l.Contains(VerdictPrefix + QuestKey, StringComparison.Ordinal)));
        output.WriteLine(verdict);
        Assert.Contains(" from=home ", verdict, StringComparison.Ordinal);
        Assert.Contains(" done=yes ", verdict, StringComparison.Ordinal);
        Assert.Contains(" timer=saved ok=yes fail=none steps=1-2", verdict, StringComparison.Ordinal);
        Assert.True(Number(verdict, "xp=+") >= 100_000, "the experience property did not rise by Brighteyes's 100,000: " + verdict);
        Assert.Equal(100_000, Number(verdict, "reward_line="));
        Assert.Contains(lines, l => l.Contains("quest_step: n=1 kind=go", StringComparison.Ordinal) && l.Contains(" ok=yes ", StringComparison.Ordinal) && l.Contains("via=town-network+walk ", StringComparison.Ordinal));
        Assert.Contains(lines, l => RewardLine().IsMatch(l));
        Assert.Contains(lines, l => l.Contains("give: Aphus Lassel Portal Gem arrived: held ", StringComparison.Ordinal) && l.Contains(" -> ", StringComparison.Ordinal)); // the gem arrived: the give's check-after line (review 5-R1)
        Assert.Contains(lines, l => l.Contains("quest_step: n=2 kind=give", StringComparison.Ordinal) && l.Contains(" ok=yes ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("quest_step: n=0 ", StringComparison.Ordinal)); // the hunt-until was not run
        Assert.Contains(lines, l => l.Contains("quest key=" + QuestKey + " ", StringComparison.Ordinal) && l.Contains("saved=yes", StringComparison.Ordinal));
        Assert.Contains(QuestKey, store.Json ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("rung_change plan: direction=home ", StringComparison.Ordinal) && l.Contains(" lifestone_recall=no ", StringComparison.Ordinal) && l.Contains(" route=refuge ", StringComparison.Ordinal)); // the decision read the book without it
        Assert.Contains(lines, l => l.Contains("rung_change: ", StringComparison.Ordinal) && l.Contains(" ok=yes fail=none direction=home", StringComparison.Ordinal)); // then home
    }

    [LoopbackFact]
    public void The_Burrow_hunt_takes_a_tusk_live_the_Throwaway_gives_it_to_Brighteyes_and_recalls_home()
    {
        using var dats = DatSet.Open();
        var store = new MemoryCheckpointStore();
        var account = LoopbackAccounts.For(Throwaway);
        Build(dats, HuntLevel, Burrow, account, null);
        Assert.Equal(1, PlaceAt(dats, Holtburg(), "Holtburg, the travel file's start"));

        var lines = RunJourney(dats, "hunt", [.. Ready, Rung("out"), Quest(JourneyPlans.FromGround, "0-2"), Rung("home")], Burrow, account, store, grant: null, 7200);
        Assert.Contains(lines, l => l.Contains("journey station=" + AutopilotScenario.BindSpec.Name + " ok=true ", StringComparison.Ordinal) && l.Contains(" bound=yes tied=yes ", StringComparison.Ordinal));
        var verdict = Verdict(Assert.Single(lines, l => l.Contains(VerdictPrefix + QuestKey, StringComparison.Ordinal)));
        output.WriteLine(verdict);
        Assert.Contains(" from=ground ", verdict, StringComparison.Ordinal);
        Assert.Contains(" done=yes ", verdict, StringComparison.Ordinal);
        Assert.Contains(" timer=saved ok=yes fail=none steps=0-2", verdict, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("fight: until met: ", StringComparison.Ordinal) && l.Contains("item 22423 held 1 of 1", StringComparison.Ordinal)); // the hunt-until's end, the fight's own read (Q4): the tusk is held
        Assert.Contains(lines, l => l.Contains("quest_step: n=0 kind=hunt-until", StringComparison.Ordinal) && l.Contains(" ok=yes ", StringComparison.Ordinal) && l.Contains("held=1/1", StringComparison.Ordinal) && l.Contains("until=met", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("quest_step: n=1 kind=go", StringComparison.Ordinal) && l.Contains(" ok=yes ", StringComparison.Ordinal) && l.Contains("via=rung-change-home+town-network+walk ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("give: Aphus Lassel Portal Gem arrived: held ", StringComparison.Ordinal));
        Assert.Contains(QuestKey, store.Json ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("rung_change: ", StringComparison.Ordinal) && l.Contains(" ok=yes fail=none direction=home", StringComparison.Ordinal)); // then home by the recall
    }

    /// <summary>Holtburg, the travel file's start.</summary>
    private static AcPosition Holtburg()
    {
        var holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg")).Location.Position;
        return new AcPosition(holtburg.CellId, holtburg.Local, Quaternion.Identity);
    }

    [GeneratedRegex(@"reward: ""You've earned 100,000 experience\.""", RegexOptions.CultureInvariant)]
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

    /// <summary>The reset, the build to <paramref name="level"/> (the ground's supplies granted; the spells <paramref name="withheld"/> not taught), the refill.</summary>
    private void Build(DatSet dats, int level, HuntingGround ground, string account, IReadOnlyCollection<uint>? withheld)
    {
        var table = XpTableData.Load(dats);

        // (0) the reset: no Throwaway left on test-tools (deleted if live, purged from Lifestone Tools, M4 D43).
        ThrowawayReset.Run(output, dats);

        // (1) the build (D39, D10): the climber's creation and shares, raised to the level, the arena spells, the ground's supplies; then the vitals refilled by one level-up.
        var buildLog = new RecordingLogSink();
        var kit = GearKit.KitPlan.For(ground, SpellTableData.Load(dats).Spells, account, stockOnly: true); // a kit-less ground: the foci and the stock (TODO(m6-loopback-burrow-kit) closed, M6 5)
        var build = new MageBuildScenario(table, HeadlessCharacter.RetailSkillClasses(dats), buildLog, freshCharacter: false, kitPlan: kit, level: level, characterName: Throwaway, withheldSpells: withheld);
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

    /// <summary>One cheat-free journey of <paramref name="stations"/> (after the grant of <paramref name="grant"/>, when given; the quest timers through <paramref name="store"/>); its log lines.</summary>
    private IReadOnlyList<string> RunJourney(DatSet dats, string phase, IReadOnlyList<StationSpec> stations, HuntingGround ground, string account, MemoryCheckpointStore store, uint? grant, double quitAfterSeconds)
    {
        var log = new RecordingLogSink();
        var plan = new JourneyPlan("trophy-" + phase, Throwaway, account, StationWhere.Here, 0, stations) { CheatFree = true, BuffProfile = BuffProfiles.Climber }; // M6 7 H: the climb's own profile: the lines the book holds, each while its school is trained
        using var journey = new StandInJourney(plan, ground, dats, SpellTableData.Load(dats).Spells, log, new HeadlessJourneyRunnerHost(dats, log), store, grant);
        var result = Run(phase, journey, dats, log, account, Throwaway, quitAfterSeconds);
        if (TrophyRunRead.Named(log.Lines) is { } named)
        {
            Assert.Fail(named); // M6 7 H: a death on the run or a go that failed (a door by name), before the journey's own failure
        }

        Assert.True(result.Ok, result.Failure ?? journey.Failure);
        return log.Lines;
    }

    /// <summary>Runs <paramref name="scenario"/> live as <paramref name="account"/> and prints its summary and every line of its log (the log-everything rule), as <c>KillTaskLiveTests</c> does.</summary>
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
    /// <c>KillTaskLiveTests</c>' stand-in journey with the quest timers' store, and the grant: when <paramref name="grant"/> is given, the mod's item verb makes one in the pack first (check before: none held; act: the verb; check after: the
    /// verb confirmed and the item in the pack within <see cref="GrantSeconds"/>, else the run fails by name), then the journey starts.
    /// </summary>
    private sealed class StandInJourney(JourneyPlan plan, HuntingGround ground, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, HeadlessJourneyRunnerHost host, IAutopilotCheckpointStore store, uint? grant) : IScenario, IDisposable
    {
        private const double GrantSeconds = 15;
        private JourneyScenario? _journey;
        private LoginScenario? _login;
        private ToolHandle<ItemResult>? _grant;
        private double _grantAt;
        private readonly CasterWield _wield = new(log, "trophy-live");
        private string? _failure;

        public string Name => JourneyScenario.ScenarioName;

        public string Character => plan.Character;

        public string Stage => _journey?.Stage ?? "grant";

        public string? Failure => _failure ?? _wield.Failure ?? _journey?.Failure;

        public double? WhereMaxMetres => _journey?.WhereMaxMetres;

        public long Unmatched => _journey?.Unmatched ?? 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (_journey is null)
            {
                // M6 5 round 2: the journey logs in, but the grant comes before it, so the wrapper logs in itself (the journey then skips its login: in-world).
                if (grant is not null && session.State.Phase != SessionPhase.InWorld)
                {
                    _login ??= new LoginScenario(plan.Character) { StayInWorld = true };
                    if (_login.Step(session, now) == ScenarioStatus.Failed)
                    {
                        _failure = "grant: login: " + (_login.Failure ?? "the login failed without saying why");
                        log.Error("trophy-live", _failure);
                        return ScenarioStatus.Failed;
                    }

                    return ScenarioStatus.Running;
                }

                if (grant is { } wcid && session.State.Phase == SessionPhase.InWorld && session.State.Self.Travel == SelfTravel.Present && Held(session, wcid) == 0)
                {
                    if (_grant is null)
                    {
                        log.Info("trophy-live", string.Create(CultureInfo.InvariantCulture, $"grant (D10, the Throwaway only): none of {wcid} held; asking the mod's item verb for one"));
                        _grant = session.Tools.SpawnItem(wcid, null, ActionSource.Host);
                        _grantAt = now;
                        return ScenarioStatus.Running;
                    }

                    if (_grant.Outcome is { Succeeded: false } refused || now - _grantAt > GrantSeconds)
                    {
                        _failure = string.Create(CultureInfo.InvariantCulture, $"grant: the item {wcid} is not in the pack {now - _grantAt:0} s after the verb ({_grant.Outcome?.ToString() ?? "no outcome"})");
                        log.Error("trophy-live", _failure);
                        return ScenarioStatus.Failed;
                    }

                    return ScenarioStatus.Running;
                }

                if (grant is { } given && Held(session, given) == 0)
                {
                    return ScenarioStatus.Running; // not in the world yet
                }

                if (grant is { } got)
                {
                    log.Info("trophy-live", string.Create(CultureInfo.InvariantCulture, $"grant: {Held(session, got)} of {got} in the pack (check after); the journey starts"));
                }

                var keeper = JourneyStations.CreateKeeper(plan, session, spells, [], log);
                var context = JourneyScenario.NewContext(plan, log, spells, keeper, dats, host, dungeon: ground, questStore: store);
                _journey = new JourneyScenario(plan, JourneyStations.Create(plan, [], dats, null, ground), context);
                log.Info("trophy-live", $"the ground {ground.Key}: landblocks {string.Join(",", ground.Landblocks.Select(l => RungRoute.Hex(l)))}, entry {ground.Entry?.Wcid.ToString(CultureInfo.InvariantCulture) ?? "none"}, arrival 0x{ground.Arrival.Cell:X8}; the quest {QuestKey}");
            }

            // M6 7 B (TODO(m6-quest-loopbacks-wield)): the Wand is in the pack, not wielded, so the pack's caster is wielded before the bind's tie, once the player is present (after the grant, which logs in itself, at once; else after the journey's own login).
            // The journey is not stepped while the wield runs; a caster already in hand needs none (journey 2).
            if (!_wield.Done && session.State.Phase == SessionPhase.InWorld && session.State.Self.Travel == SelfTravel.Present && _wield.Step(session, now) is { } wielding)
            {
                return wielding;
            }

            host.Tick(session, now);
            return _journey.Step(session, now);
        }

        public void Dispose()
        {
            _journey?.Dispose();
            host.Dispose();
        }

        private static int Held(IGameSessionView session, uint wcid) =>
            session.State.Self.Inventory.Pack.Count(e => session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);
    }
}

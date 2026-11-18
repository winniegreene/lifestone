using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 item 1 c (D39, D40, D10), against the bundled ACE: the first VI learned at its band, by the station the climber will use, on Lifestone Throwaway (account <c>test-tools</c>; the only character levelled freely, never Winnie). The lead runs it;
/// no slice does.
/// <para><b>What it proves</b> (the line <c>learn_vi: level=&lt;n&gt; plan_band=&lt;rung&gt; skill=&lt;current&gt; bought=1 learned=1 place=1 granted=&lt;n&gt;</c>): (0) <see cref="ThrowawayReset"/> leaves <c>test-tools</c> with no Throwaway (a live one deleted, every copy purged
/// from <c>Lifestone Tools</c>, M4 D43: the mod refuses a Throwaway's purge of its own name, so the build's <c>--fresh-character</c> path cannot serve; its line <c>throwaway-reset: listed= deleted= purged= waits=</c>); (1) the build (<see cref="MageBuildScenario"/> with the Throwaway's name, D39,
/// not fresh: it creates the absent Throwaway, then <see cref="ThrowawayReset.Refill"/> refills the vitals by the fresh path's one level-up) makes a mage at
/// the first level whose projected War Magic <see cref="SpellPlan"/> plans VI at the ladder's chance (D10's grant path: the experience and the arena profile's spells by the mod, the Flame Bolt line from VI up withheld so the run must learn
/// it); (2) one budgeted teleport (D40: no Town Network hop exists yet, item 4's) places it at Fort Tethana's lifestone, a Grand Master Scrivener's town, and the purse is granted (counted in <c>granted</c>, D10's); (3) the resupply station on
/// the Fort Tethana ground (the catalog's Grand Master Scrivener of War Magic 20224 in its vendor view) plans Flame Bolt VI by D41 on the current skill, buys the scroll 6002 (50,000 pyreals) and reads it with check, act, check: the book
/// gains 85 from the server's MagicUpdateSpell. <c>skill</c> and <c>plan_band</c> are read from the station's plan line; a skill short of VI's 95% names itself in the failure.</para>
/// <para>The test deletes and purges Lifestone Throwaway on <c>test-tools</c>, creates it again and changes it (level, spells, pyreals, its place); it enters <c>Lifestone Tools</c> only to purge (creating it once when absent) and touches no other
/// character. It leaves its Throwaway behind; the next Throwaway class's reset removes it (this class and <c>BuyTripLiveTests</c> run one after the other in the collection).</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[Trait("Lane", "solo")]
[LoopbackCharacter(Throwaway)]
public sealed class ScrivenerLearnLiveTests(ITestOutputHelper output)
{
    /// <summary>The character (D39).</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>The ground of the Grand Master's town: Fort Tethana's (its home lifestone, the catalog's scriveners in its vendor view).</summary>
    private const string FortTethanaGround = "tusker-temple-fort-tethana";

    /// <summary>ACE's pyreal (<c>coinstack</c>, WeenieClassName.cs:253).</summary>
    private const uint Pyreal = 273;

    /// <summary>The Grand Master Scrivener of War Magic (20224; <c>cache/m6/1/sel-scriveners.tsv</c>), who sells Flame Bolt VI (scroll 6002) in Fort Tethana (0x25810175).</summary>
    private const uint GrandMasterOfWar = 20224;

    /// <summary>The purse granted: four requests of the mod's largest stack (the VI scroll is 50,000 at the Grand Master's SellPrice 50, and the trip restocks the ground's lines on the way).</summary>
    private const int PurseRequests = 4;

    [LoopbackFact]
    public void The_Throwaway_built_to_the_VI_band_buys_Flame_Bolt_VI_at_a_Grand_Master_Scrivener_and_reads_it()
    {
        using var dats = DatSet.Open();
        var table = XpTableData.Load(dats);
        var ladder = LadderLoader.WarMage;
        var level = Enumerable.Range(2, MageTemplate.TargetLevel - 1).First(l => SpellPlan.LevelAt(SpellPlan.ProjectedWarBase(table, l), ladder.PlanCastChance) >= 6);
        var fireLine = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells;
        var withheld = fireLine.Where(s => s.Position >= 6).Select(s => s.Id).ToHashSet(); // VI, VII, VIII: the book must lack VI and every higher level, or the plan says known
        output.WriteLine($"the plan first gives VI at level {level} (projected base War Magic {SpellPlan.ProjectedWarBase(table, level)}, chance {ladder.PlanCastChance}); withheld from the build: {string.Join(", ", withheld)}");
        var account = LoopbackAccounts.For(Throwaway);

        // (0) the reset: no Throwaway left on test-tools (deleted if live, purged from Lifestone Tools, M4 D43; the mod refuses the Throwaway's own purge).
        ThrowawayReset.Run(output, dats);

        // (1) the build: the Throwaway created (it is absent after the reset) and built to the level (D39, D10), then the vitals refilled by one level-up as the fresh build did.
        var buildLog = new RecordingLogSink();
        var build = new MageBuildScenario(table, HeadlessCharacter.RetailSkillClasses(dats), buildLog, freshCharacter: false, level: level, characterName: Throwaway, withheldSpells: withheld);
        var built = Run("build", build, dats, buildLog, account, null, 1800);
        output.WriteLine("[mage] verdict " + build.VerdictFields());
        Assert.True(built.Ok, built.Failure ?? build.Failure);
        Assert.True(build.Created, "the build did not create the Throwaway, though the reset left none on test-tools");
        ThrowawayReset.Refill(output, dats, table);

        // (2) the one budgeted teleport to Fort Tethana's lifestone, and the purse (both D10's, counted).
        var ground = GroundCatalog.Get(FortTethanaGround);
        var granted = 0;
        var place = 0;
        using (var client = LiveNetClient.Create(dats, retail: false, Throwaway))
        {
            try
            {
                EnterTheWorld(client);
                Unhide(client);
                for (var i = 0; i < PurseRequests; i++)
                {
                    AskData<ItemData>(client, new ItemRequest(Pyreal, ToolRequestParser.MaxItemStack, NextId()));
                    granted++;
                }

                // M6 1 L2 (live, 2026-11-02): placed at the Grand Master's own place, not the town's lifestone (0x2780, two landblocks from his room 0x2581). Since M6 4 W1 a leg from the lifestone plans past
                // its tiles (the goal-tile horizon), but his building is inside the fort's walls, whose gate opens only by its lever (TODO(door-lever), lever-doors.json; M6 4 t4a's TownNetworkTripDatTests):
                // the station's walk from his place is in his building (and Zisaha's restock, in 0x2481 outside the walls, is skipped by name: the walk out passes the same gate).
                var master = ScrivenerCatalog.Default.Scriveners.Single(s => s.Wcid == GrandMasterOfWar).Places.First(p => p.Town == ground.Home.Town);
                var at = new AcPosition(master.Cell, master.Point, Quaternion.Identity);
                var arrival = PlaceAndArrive(client, new PlaceLocRequest(ToLocation(at), NextId()));
                place++;
                output.WriteLine($"placed (D40, the journey's budgeted teleport until item 4's Town Network hop) at {arrival.Where.Cell} ({arrival.Where.X:F1}, {arrival.Where.Y:F1}), by the Grand Master Scrivener of War Magic in {ground.Home.Town}");
                LogOffAndQuit(client);
            }
            finally
            {
                LeaveTheWorld(client);
            }
        }

        // (3) the resupply station at the Grand Master's town: the plan's VI bought and read.
        var learnLog = new RecordingLogSink();
        var plan = new JourneyPlan("scrivener-vi", Throwaway, account, StationWhere.Here, 0, [new StationSpec("resupply", StationKinds.Resupply, null, 300)]) { CheatFree = true };
        using var journey = new HeadlessJourneyScenario(plan, ["--fight-ground=" + FortTethanaGround], dats, SpellTableData.Load(dats).Spells, learnLog, null, new HeadlessJourneyRunnerHost(dats, learnLog));
        var learned = Run("learn", journey, dats, learnLog, account, Throwaway, 900);

        var due = learnLog.Lines.FirstOrDefault(l => l.Contains("Flame Bolt VI (85): due:", StringComparison.Ordinal)) ?? "no plan line for Flame Bolt VI";
        var bought = learnLog.Lines.Count(l => l.Contains("learn: bought the scroll Flame Bolt VI (weenie 6002)", StringComparison.Ordinal));
        var read = learnLog.Lines.Count(l => l.Contains("learn: learned Flame Bolt VI (85)", StringComparison.Ordinal));
        var summary = string.Create(CultureInfo.InvariantCulture, $"learn_vi: level={level} plan_band={Word(due, "rung ")} skill={Word(due, "WarMagic skill ")} bought={bought} learned={read} place={place} granted={granted}");
        output.WriteLine(due);
        output.WriteLine(summary + " (granted: the purse requests, D10's; place: the teleport to the Grand Master's town, D40's)");
        Assert.True(learned.Ok, learned.Failure ?? journey.Failure);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"learn_vi: level={level} plan_band={Word(due, "rung ")} skill={Word(due, "WarMagic skill ")} bought=1 learned=1 place=1 granted={PurseRequests}"), summary);
    }

    /// <summary>The word after <paramref name="key"/> in <paramref name="line"/>, or "?".</summary>
    private static string Word(string line, string key)
    {
        var at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0)
        {
            return "?";
        }

        var rest = line[(at + key.Length)..];
        var end = rest.IndexOfAny([' ', ':']);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>Runs <paramref name="scenario"/> live as <paramref name="account"/> and prints its summary and every line of its log (the log-everything rule), as <c>AcademyChestLiveTests</c> does.</summary>
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
}

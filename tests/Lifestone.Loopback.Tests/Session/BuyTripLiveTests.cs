using System.Globalization;
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
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 item 1 d (D12, D39, D40, D10), against the bundled ACE: one buy-trip line, by the station the climber will use, on Lifestone Throwaway (account <c>test-tools</c>; the only character levelled freely, never Winnie). The lead runs it; no
/// slice does. It shares slice c's gate area <c>scriveners</c> (the Throwaway on <c>test-tools</c>, solo), so no other item's change selects it.
/// <para><b>What it proves</b> (the station's line <c>buy_trip: scarab=Pyreal_Scarab town=Fort_Tethana vendor=2314 want=&lt;n&gt; bought=&lt;n&gt; price=1800 ... travel=budgeted-teleport ok=yes reason=bought</c>): (0) <see cref="ThrowawayReset"/> leaves
/// <c>test-tools</c> with no Throwaway (the one <c>ScrivenerLearnLiveTests</c> left is deleted, waiting out ACE's hold after its log off, and purged from <c>Lifestone Tools</c>, M4 D43; its line <c>throwaway-reset: listed= deleted= purged= waits=</c>); (1) the build
/// (<see cref="MageBuildScenario"/> with the Throwaway's name, as <c>ScrivenerLearnLiveTests</c> builds it: not fresh, it creates the absent Throwaway, then <see cref="ThrowawayReset.Refill"/> refills the vitals) makes a mage at the first level whose projected War Magic plans VI (D10's grant path), so the den's carried Pyreal Scarab
/// (no Holtburg vendor sells it; the build grants no kit, so none is held) is under its stop line and the plan burns it (D4, the den's components: VI); (2) the purse is granted in pyreal stacks (D10's, counted in <c>granted</c>) to cover the
/// line's keep at Zisaha's price; (3) a journey of the one <see cref="StationKinds.BuyTrip"/> station on the den's ground (<c>--fight-ground</c>), with a teleport budget of one (D40: no Town Network hop exists yet, item 4's) and the spec's
/// <see cref="BuyTripStation.NoBackArgument"/> (the trip ends at the archmage): the station decides the trip due, places once to Fort Tethana's arrival by the journey's budgeted teleport, walks to Zisaha bint Jaidka 2314 (the catalog's,
/// create-list row 70986), opens her shop and buys the line to its keep with check, act, check (the pack's count rose by the bought count, the coin fell).</para>
/// <para>The test deletes and purges Lifestone Throwaway on <c>test-tools</c>, creates it again and changes it (level, spells, pyreals, its place); it enters <c>Lifestone Tools</c> only to purge (creating it once when absent) and touches no other
/// character. It leaves its Throwaway behind; the next Throwaway class's reset removes it.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[Trait("Lane", "solo")]
[LoopbackCharacter(Throwaway)]
public sealed class BuyTripLiveTests(ITestOutputHelper output)
{
    /// <summary>The character (D39).</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>The ground whose Pyreal Scarab is carried (no Holtburg vendor sells it, D8): the den's.</summary>
    private const string DenGround = "black-spawn-den-aluvian";

    /// <summary>ACE's pyreal (<c>coinstack</c>, WeenieClassName.cs:253).</summary>
    private const uint Pyreal = 273;

    /// <summary>The Pyreal Scarab (the den's components: level VI).</summary>
    private const uint PyrealScarab = 690;

    /// <summary>The most pyreal requests the purse may take (the mod clamps a stack to the coin's MaxStackSize, 25,000 in the pinned world SQL).</summary>
    private const int MaxPurseRequests = 40;

    [LoopbackFact]
    public void The_Throwaway_built_to_the_VI_band_buys_the_Pyreal_Scarabs_to_their_keep_at_Zisaha_by_the_buy_trip()
    {
        using var dats = DatSet.Open();
        var table = XpTableData.Load(dats);
        var ladder = LadderLoader.WarMage;
        var level = Enumerable.Range(2, MageTemplate.TargetLevel - 1).First(l => SpellPlan.LevelAt(SpellPlan.ProjectedWarBase(table, l), ladder.PlanCastChance) >= 6);
        var den = GroundCatalog.Get(DenGround);
        var line = den.Supplies.StockOf(PyrealScarab) ?? throw new InvalidOperationException($"the den's supplies have no Pyreal Scarab line");
        var seller = ScrivenerCatalog.Default.ScarabSellersOf(PyrealScarab)[0];
        var need = (long)line.Keep * seller.PriceOf(seller.SaleOf(PyrealScarab)!.Value);
        output.WriteLine($"the plan first gives VI at level {level}; the den's {line.Name} keep {line.Keep}, stop {line.StopUnder}; {seller.Name} ({seller.Wcid}) sells it at {seller.PriceOf(1000)}: the purse needs {need}");
        var account = LoopbackAccounts.For(Throwaway);

        // (0) the reset: no Throwaway left on test-tools (deleted if live, purged from Lifestone Tools, M4 D43; the mod refuses the Throwaway's own purge).
        ThrowawayReset.Run(output, dats);

        // (1) the build: the Throwaway created (it is absent after the reset) and built to the level (D39, D10), then the vitals refilled by one level-up, as ScrivenerLearnLiveTests builds it.
        var buildLog = new RecordingLogSink();
        var build = new MageBuildScenario(table, HeadlessCharacter.RetailSkillClasses(dats), buildLog, freshCharacter: false, level: level, characterName: Throwaway);
        var built = Run("build", build, dats, buildLog, account, null, 1800);
        output.WriteLine("[mage] verdict " + build.VerdictFields());
        Assert.True(built.Ok, built.Failure ?? build.Failure);
        Assert.True(build.Created, "the build did not create the Throwaway, though the reset left none on test-tools");
        ThrowawayReset.Refill(output, dats, table);

        // (2) the purse (D10's grant, counted): pyreal stacks until the keep is covered at the archmage's price.
        var granted = 0;
        long purse = 0;
        using (var client = LiveNetClient.Create(dats, retail: false, Throwaway))
        {
            try
            {
                EnterTheWorld(client);
                Unhide(client);
                while (purse < need && granted < MaxPurseRequests)
                {
                    var data = AskData<ItemData>(client, new ItemRequest(Pyreal, ToolRequestParser.MaxItemStack, NextId()));
                    granted++;
                    purse += data.Stack;
                }

                output.WriteLine($"granted {granted} pyreal stacks, {purse} pyreals (D10)");
                LogOffAndQuit(client);
            }
            finally
            {
                LeaveTheWorld(client);
            }
        }

        Assert.True(purse >= need, $"the purse {purse} does not cover {need} after {granted} requests");

        // (3) the buy-trip: one station, one budgeted teleport (D40), the trip's end at the archmage.
        var tripLog = new RecordingLogSink();
        var spec = new StationSpec("buy-trip", StationKinds.BuyTrip, null, 900, new Dictionary<string, string>(StringComparer.Ordinal) { [BuyTripStation.NoBackArgument] = "1" });
        var plan = new JourneyPlan("buy-trip", Throwaway, account, StationWhere.Here, 1, [spec]) { CheatFree = true };
        using var journey = new HeadlessJourneyScenario(plan, ["--fight-ground=" + DenGround], dats, SpellTableData.Load(dats).Spells, tripLog, null, new HeadlessJourneyRunnerHost(dats, tripLog));
        var result = Run("buy-trip", journey, dats, tripLog, account, Throwaway, 1200);

        var tripLine = tripLog.Lines.FirstOrDefault(l => l.Contains(BuyTripStation.LinePrefix + " scarab=", StringComparison.Ordinal)) ?? "no buy_trip line";
        output.WriteLine(tripLine + string.Create(CultureInfo.InvariantCulture, $" (granted={granted}: the purse requests, D10's; travel: the budgeted teleport to the archmage's town, D40's)"));
        Assert.True(result.Ok, result.Failure ?? journey.Failure);
        Assert.Contains("scarab=Pyreal_Scarab town=Fort_Tethana vendor=2314 ", tripLine, StringComparison.Ordinal);
        Assert.Contains(" travel=budgeted-teleport ok=yes reason=bought", tripLine, StringComparison.Ordinal);
        Assert.True(int.Parse(Word(tripLine, "bought="), CultureInfo.InvariantCulture) >= 1, tripLine);
    }

    /// <summary>The word after <paramref name="key"/> in <paramref name="line"/>, or "0".</summary>
    private static string Word(string line, string key)
    {
        var at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0)
        {
            return "0";
        }

        var rest = line[(at + key.Length)..];
        var end = rest.IndexOf(' ', StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>Runs <paramref name="scenario"/> live as <paramref name="account"/> and prints its summary and every line of its log (the log-everything rule), as <c>ScrivenerLearnLiveTests</c> does.</summary>
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

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
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
/// M6 item 16, slice E5b (docs/automation-engine.md, stage E5's proof "a quest buy paid from notes"), against the bundled ACE: a quest's buy step whose coin does not cover the price is paid from a trade note changed at the vendor's
/// counter, through the quest station's own run (<see cref="QuestRun"/>, its <c>buy</c> step) and the vendor counter (<c>VendorCounter.Buy</c>), on Lifestone Throwaway (account <c>test-tools</c>; the only character granted freely, never
/// the climber: D10). The lead runs it; no slice does. Gate area <c>m6-quests</c>.
/// <para><b>What it proves.</b> <see cref="ThrowawayReset"/> leaves <c>test-tools</c> with no Throwaway; the build (<see cref="MageBuildScenario"/>, no kit) makes one at level <see cref="Level"/>; one setup place (D10's, counted) at
/// Holtburg. Then one cheat-free journey of one station: the grant of one Trade Note (10,000) 2625 (the mod's item verb; D10, the Throwaway only), the coin read, and a quest built by hand with two steps: go to Archmage Cindrue (the
/// outskirts profile's vendor: her mask 0x000CB000 holds PromissoryNote 0x40000, the profile's <c>buys_item_types</c> from the pinned world SQL v0.9.296), buy from her as many Prismatic Tapers 20631 as cost just more than the coin held
/// (the count is <c>coin / price + 1</c> at the profile's price, so the coin is short by less than one taper and the one note covers the rest). Asserted from the log: the step's check before says a note is changed at the counter; the
/// counter's line <c>buy: purse: the coin was short: changed Trade Note (10,000) to coin at Archmage Cindrue: paid 10000 pyreals</c> (ACE pays a note at its face, <c>Vendor.GetBuyCost</c>, Vendor.cs 591-599 @95ef426d); the step's
/// <c>buy: paid from the purse: 1 trade note(s) changed</c>; its check after (<c>buy: check after: &lt;n&gt; Prismatic Taper (+&lt;n&gt;), paid &lt;cost&gt;</c>) and its <c>quest_step: n=1 kind=buy ... ok=yes</c>.</para>
/// <para><b>M6 16 E5a2: the note inside a side pack</b> (<see cref="A_quest_buy_the_coin_does_not_cover_is_paid_from_a_trade_note_inside_a_side_pack_sold_where_it_lies"/>). The same run with one more setup step before
/// the journey: a Pack 136 is granted when no side pack is held (the mod's item verb; D10, the Throwaway only) and the granted note is moved into it by the player's own move (<c>IActionApi.MoveToPack</c>), checked after
/// in the read model. What it proves against the server, which the scripted vendor of <c>VendorCounterTests</c> cannot: ACE sells a note that lies inside a side pack (<c>Player.VerifySellItems</c>,
/// <c>Player_Commerce.cs</c> 232-298, looks the guid up in <c>GetAllPossessions()</c>, <c>Player_Inventory.cs</c> 29-44 @95ef426d), so the counter's line <c>buy: purse: Trade Note (10,000) 0x... (x1) lies in the side
/// pack 0x...: a single note, sold where it lies</c> is followed by the same change, payment and check-after lines as the main pack's. It does not prove the split of one note off a stack inside a side pack (one single
/// note is granted), nor the split into a side pack when the main pack is full.</para>
/// <para><b>What it does not prove.</b> A catalog quest's buy (the Atlan wand's Magic Glyph at Silencia: the step is the same code, her counter and its mask are not visited here); a note split off a stack (one single note is granted);
/// the buy-trip's and the lockpick buy's note change (the same counter: <c>BuyTripTests</c>, <c>LockpickBuyStationTests</c> over the scripted vendor).</para>
/// <para>The test deletes and purges Lifestone Throwaway on <c>test-tools</c>, creates it again and changes it (level, one granted note, its place, its coin and pack); it enters <c>Lifestone Tools</c> only to purge (creating it once when
/// absent) and touches no other character. It leaves its Throwaway behind; the next Throwaway class's reset removes it.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[Trait("Lane", "solo")]
[LoopbackCharacter(Throwaway)]
public sealed class QuestBuyFromNotesLiveTests(ITestOutputHelper output)
{
    /// <summary>The character (D39).</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>The build's level: past the Academy, low enough to build fast.</summary>
    private const int Level = 10;

    /// <summary>The ground whose home is Holtburg and whose profile lists Archmage Cindrue with her mask and the taper's price.</summary>
    private const string Outskirts = "holtburg-outskirts";

    /// <summary>The Prismatic Taper (the ware).</summary>
    private const uint Taper = 20631;

    /// <summary>Trade Note (10,000) (ACE-World-Database v0.9.296: weenie 2625, Value 10,000, ItemType PromissoryNote).</summary>
    private const uint Note10k = 2625;

    [LoopbackFact]
    public void A_quest_buy_the_coin_does_not_cover_is_paid_from_a_trade_note_changed_at_Cindrues_counter() => BuyPaidFromANote(inSidePack: false);

    /// <summary>M6 16 E5a2: the same buy with the note inside a side pack: the counter sells it where it lies (see the class). Written to compile; the lead runs it.</summary>
    [LoopbackFact]
    public void A_quest_buy_the_coin_does_not_cover_is_paid_from_a_trade_note_inside_a_side_pack_sold_where_it_lies() => BuyPaidFromANote(inSidePack: true);

    private void BuyPaidFromANote(bool inSidePack)
    {
        using var dats = DatSet.Open();
        var ground = GroundCatalog.Get(Outskirts);
        var cindrue = ground.VendorSelling(Taper) ?? throw new InvalidOperationException("the outskirts profile lists no seller of the Prismatic Taper");
        var price = ground.PriceOf(Taper) ?? throw new InvalidOperationException("the outskirts profile gives the Prismatic Taper no price");
        Assert.True((cindrue.BuysItemTypes & VendorStock.PromissoryNoteType) != 0, $"{cindrue.Name}'s profile mask 0x{cindrue.BuysItemTypes:X8} takes no trade note: the test's vendor must change one");
        Assert.InRange(price, 1, 10_000); // the coin is short by less than one taper, so one 10,000 note covers it
        var account = LoopbackAccounts.For(Throwaway);

        // (0) the reset and (1) the build: no kit, so the Throwaway holds what creation gave it.
        var table = XpTableData.Load(dats);
        ThrowawayReset.Run(output, dats);
        var buildLog = new RecordingLogSink();
        var build = new MageBuildScenario(table, HeadlessCharacter.RetailSkillClasses(dats), buildLog, freshCharacter: false, level: Level, characterName: Throwaway);
        var built = Run("build", build, dats, buildLog, account, null, 1800);
        output.WriteLine("[mage] verdict " + build.VerdictFields());
        Assert.True(built.Ok, built.Failure ?? build.Failure);
        Assert.True(build.Created, "the build did not create the Throwaway, though the reset left none on test-tools");
        ThrowawayReset.Refill(output, dats, table);

        // (2) one setup place (D10's, counted): Holtburg.
        Assert.Equal(1, PlaceAt(dats, Holtburg(), "Holtburg, Archmage Cindrue's town"));

        // (3) the journey: the grant of the note, the coin read, the hand-built quest (go, buy) by the quest station's run.
        var log = new RecordingLogSink();
        var spec = new StationSpec("quest-buy", StationKinds.Quest, null, 900);
        var plan = new JourneyPlan("quest-buy-from-notes", Throwaway, account, StationWhere.Here, 0, [spec]) { CheatFree = true };
        using var journey = new StandInJourney(plan, ground, cindrue, price, dats, SpellTableData.Load(dats).Spells, log, new HeadlessJourneyRunnerHost(dats, log), inSidePack);
        var result = Run("quest-buy", journey, dats, log, account, Throwaway, 1500);
        var lines = log.Lines;

        Assert.True(result.Ok, result.Failure ?? journey.Failure);
        Assert.Contains(lines, l => l.Contains("buy: check before: " + cindrue.Name, StringComparison.Ordinal) && l.Contains("so a note is changed at the counter", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("buy: purse: the coin was short: changed Trade Note (10,000) to coin at " + cindrue.Name + ": paid 10000 pyreals", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("buy: paid from the purse: 1 trade note(s) changed to coin at " + cindrue.Name, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("buy: check after: ", StringComparison.Ordinal) && l.Contains(" Prismatic Taper (+", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("quest_step: n=1 kind=buy", StringComparison.Ordinal) && l.Contains(" ok=yes ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("buy:purse-short", StringComparison.Ordinal));
        if (inSidePack)
        {
            Assert.Contains(lines, l => l.Contains("check after: the Trade Note (10,000) 0x", StringComparison.Ordinal) && l.Contains(" lies inside the side pack 0x", StringComparison.Ordinal)); // the setup's own check
            Assert.Contains(lines, l => l.Contains("buy: purse: Trade Note (10,000) 0x", StringComparison.Ordinal) && l.Contains(" (x1) lies in the side pack 0x", StringComparison.Ordinal) && l.Contains(": a single note, sold where it lies", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("buy: purse: split one ", StringComparison.Ordinal)); // a single note is not split
        }
        else
        {
            Assert.DoesNotContain(lines, l => l.Contains(" lies in the side pack 0x", StringComparison.Ordinal));
        }
    }

    /// <summary>Holtburg, the travel file's start.</summary>
    private static AcPosition Holtburg()
    {
        var holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg")).Location.Position;
        return new AcPosition(holtburg.CellId, holtburg.Local, Quaternion.Identity);
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

    /// <summary>Runs <paramref name="scenario"/> live as <paramref name="account"/> and prints its summary and every line of its log (the log-everything rule), as <c>TrophyLiveTests</c> does.</summary>
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

    /// <summary>The journey's one station: the quest station's own run over a quest built by hand (the catalog holds no quest that buys in Holtburg).</summary>
    private sealed class HandBuiltQuestStation(Quest quest) : IStation, IStationFactory
    {
        public string Name => "quest-buy";

        public StationNeeds Needs { get; } = new(Where: StationWhere.Here, Peace: true);

        public IStation Create(StationSpec spec) => this;

        public IStationRun Start(JourneyContext context) => new QuestRun(context, quest, JourneyPlans.FromHome, null);
    }

    /// <summary>
    /// <c>TrophyLiveTests</c>' stand-in journey, with this test's setup before the journey: the wrapper logs in, asks the mod's item verb for one Trade Note (10,000) when none is held (check before; act; check after: the note in the
    /// pack within <see cref="GrantSeconds"/>, else the run fails by name), reads the coin, builds the quest (the count just over the coin) and starts the journey.
    /// </summary>
    private sealed class StandInJourney(JourneyPlan plan, HuntingGround ground, GroundVendor vendor, int price, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, HeadlessJourneyRunnerHost host, bool inSidePack = false) : IScenario, IDisposable
    {
        private const double GrantSeconds = 15;
        private const string Category = "quest-buy-live";

        /// <summary>Pack (ACE-World-Database v0.9.296: weenie 136, a Container of 24 items).</summary>
        private const uint PackWcid = 136;
        private JourneyScenario? _journey;
        private LoginScenario? _login;
        private ToolHandle<ItemResult>? _grant;
        private double _grantAt;
        private ToolHandle<ItemResult>? _packGrant;
        private double _packAt;
        private ActionHandle? _move;
        private double _moveAt;
        private bool _moved;
        private string? _failure;

        public string Name => JourneyScenario.ScenarioName;

        public string Stage => _journey?.Stage ?? "grant";

        public string? Failure => _failure ?? _journey?.Failure;

        public double? WhereMaxMetres => _journey?.WhereMaxMetres;

        public long Unmatched => _journey?.Unmatched ?? 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (_journey is null)
            {
                if (session.State.Phase != SessionPhase.InWorld)
                {
                    _login ??= new LoginScenario(plan.Character) { StayInWorld = true };
                    if (_login.Step(session, now) == ScenarioStatus.Failed)
                    {
                        return Fail("grant: login: " + (_login.Failure ?? "the login failed without saying why"));
                    }

                    return ScenarioStatus.Running;
                }

                if (session.State.Self.Travel != SelfTravel.Present || !session.State.Self.Qualities.IsKnown)
                {
                    return ScenarioStatus.Running;
                }

                if (Held(session, Note10k) == 0 && NoteInSidePack(session) is null)
                {
                    if (_grant is null)
                    {
                        log.Info(Category, "grant (D10, the Throwaway only): no Trade Note (10,000) held; asking the mod's item verb for one");
                        _grant = session.Tools.SpawnItem(Note10k, null, ActionSource.Host);
                        _grantAt = now;
                        return ScenarioStatus.Running;
                    }

                    if (_grant.Outcome is { Succeeded: false } || now - _grantAt > GrantSeconds)
                    {
                        return Fail(string.Create(CultureInfo.InvariantCulture, $"grant: the Trade Note (10,000) is not in the pack {now - _grantAt:0} s after the verb ({_grant.Outcome?.ToString() ?? "no outcome"})"));
                    }

                    return ScenarioStatus.Running;
                }

                // M6 16 E5a2: the note into a side pack (the pack granted when none is held), by the player's own move; check after in the read model.
                if (inSidePack && !_moved)
                {
                    var inventory = session.State.Self.Inventory;
                    var pack = inventory.SidePacks.FirstOrDefault(e => e.ContainerType == 1).Guid;
                    if (pack == 0)
                    {
                        if (_packGrant is null)
                        {
                            log.Info(Category, "grant (D10, the Throwaway only): no side pack held; asking the mod's item verb for one Pack");
                            _packGrant = session.Tools.SpawnItem(PackWcid, null, ActionSource.Host);
                            _packAt = now;
                            return ScenarioStatus.Running;
                        }

                        if (_packGrant.Outcome is { Succeeded: false } || now - _packAt > GrantSeconds)
                        {
                            return Fail(string.Create(CultureInfo.InvariantCulture, $"grant: the Pack is not held {now - _packAt:0} s after the verb ({_packGrant.Outcome?.ToString() ?? "no outcome"})"));
                        }

                        return ScenarioStatus.Running;
                    }

                    if (NoteInSidePack(session) is not { } inside)
                    {
                        if (_move is null)
                        {
                            var note = inventory.Pack.First(e => session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == Note10k).Guid;
                            log.Info(Category, string.Create(CultureInfo.InvariantCulture, $"check before: the Trade Note (10,000) 0x{note:X8} lies in the main pack; moving it into the side pack 0x{pack:X8}"));
                            _move = session.Actions.MoveToPack(note, pack, 0, ActionSource.Host);
                            _moveAt = now;
                            return ScenarioStatus.Running;
                        }

                        if (now - _moveAt > GrantSeconds || (_move.IsCompleted && _move.Outcome is not { Kind: ActionOutcomeKind.Confirmed }))
                        {
                            return Fail(string.Create(CultureInfo.InvariantCulture, $"setup: the Trade Note (10,000) is not inside the side pack {now - _moveAt:0} s after the move ({_move.Outcome?.ToString() ?? "no outcome"})"));
                        }

                        return ScenarioStatus.Running;
                    }

                    _moved = true;
                    log.Info(Category, string.Create(CultureInfo.InvariantCulture, $"check after: the Trade Note (10,000) 0x{inside.Note:X8} lies inside the side pack 0x{inside.Pack:X8}; the main pack holds {Held(session, Note10k)} of it"));
                }

                // The coin read (check before): the count just over what the coin pays at the profile's price, so the coin is short by less than one taper and the one note covers the rest.
                var coin = session.State.Self.Qualities.CoinValue;
                var count = (coin / price) + 1;
                var cost = (long)count * price;
                log.Info(Category, string.Create(CultureInfo.InvariantCulture, $"check before: the coin {coin}, 1 Trade Note (10,000) held; {count} Prismatic Taper at {vendor.Name}'s {price} (the profile's) cost {cost}: the coin is short by {cost - coin}, the note covers it"));
                var cindrue = new QuestThing(vendor.Wcid, vendor.Name);
                var taper = new QuestThing(Taper, "Prismatic Taper");
                var quest = new Quest
                {
                    Key = "live-buy-from-notes",
                    Name = "a buy paid from a trade note (M6 16 E5b's loopback; no quest of the game)",
                    Giver = null,
                    Start = new QuestPlace(ground.Home.LifestoneCell, null, ground.Home.Town),
                    Steps =
                    [
                        Step(0, QuestStepKind.Go, "to Archmage Cindrue", new QuestPlace(vendor.StopCell, vendor.StopPoint ?? vendor.Point, "her shop"), cindrue, null, null),
                        Step(1, QuestStepKind.Buy, "Prismatic Tapers from Archmage Cindrue", null, cindrue, taper, count),
                    ],
                    Done = new QuestDone(null, null, taper),
                    TimerSeconds = null,
                    Sources = ["tests/Lifestone.Loopback.Tests/Session/QuestBuyFromNotesLiveTests.cs"],
                };
                var station = new HandBuiltQuestStation(quest);
                var keeper = JourneyStations.CreateKeeper(plan, session, spells, [], log);
                var context = JourneyScenario.NewContext(plan, log, spells, keeper, dats, host, dungeon: ground);
                _journey = new JourneyScenario(plan, station, context);
            }

            host.Tick(session, now);
            return _journey.Step(session, now);
        }

        public void Dispose()
        {
            _journey?.Dispose();
            host.Dispose();
        }

        private ScenarioStatus Fail(string why)
        {
            _failure = why;
            log.Error(Category, why);
            return ScenarioStatus.Failed;
        }

        private static QuestStep Step(int index, QuestStepKind kind, string what, QuestPlace? place, QuestThing? target, QuestThing? item, int? count) =>
            new() { Index = index, Kind = kind, What = what, Place = place, Target = target, Item = item, Expect = new QuestExpect(null, null, null), Source = "the loopback test", Todo = null, Count = count };

        private static int Held(IGameSessionView session, uint wcid) =>
            session.State.Self.Inventory.Pack.Count(e => session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);

        /// <summary>The Trade Note (10,000) that lies inside a side pack, with its pack, or null.</summary>
        private static (uint Note, uint Pack)? NoteInSidePack(IGameSessionView session)
        {
            var inventory = session.State.Self.Inventory;
            foreach (var side in inventory.SidePacks)
            {
                foreach (var entry in inventory.ItemsIn(side.Guid))
                {
                    if (session.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == Note10k)
                    {
                        return (entry.Guid, side.Guid);
                    }
                }
            }

            return null;
        }
    }
}

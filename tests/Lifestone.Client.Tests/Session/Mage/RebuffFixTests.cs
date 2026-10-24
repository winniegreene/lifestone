using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Diagnostics;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M4 4.1 fix, reviewer findings R2 to R4 on the re-buff driver and the armour memory: a mage that logs in buffed with a saved store is seeded and restored before any armour line counts due (no combat mode, no pass, no failure, no back-off);
/// the saved seconds are one-shot (read and cleared at the login); with no armour worn the armour lines are not wanted and no pass casts or backs off for them; a pass that finds nothing due and casts nothing is not a failure.
/// The host-side rows (the store read at the login with the switch off, the seconds played before the driver counted) are in <c>RebuffHostLogicTests</c>.
/// </summary>
public sealed class RebuffFixTests
{
    private const string Impen = "Impenetrability";
    private const string Bane = "Bludgeon Bane";
    private const double Margin = 10;

    private sealed class MemoryStore : IArmourLedgerStore
    {
        public Dictionary<uint, string> Saved { get; } = [];

        public int Loads { get; private set; }

        public string? Load(uint characterGuid)
        {
            Loads++;
            return Saved.GetValueOrDefault(characterGuid);
        }

        public void Save(uint characterGuid, string json) => Saved[characterGuid] = json;
    }

    /// <summary>The mage of <see cref="ScriptedCaster"/> whose worn pieces answer an appraisal with the spells in <see cref="Worn"/>, a ledger-owning cycle and a driver over it.</summary>
    private sealed class RebuffFixRig
    {
        private int _answered;

        public RebuffFixRig()
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            Options = new BuffOptions { RebuffMarginSeconds = Margin, Auto = true };
            View = new FixView(R);
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffOptions Options { get; }

        public IGameSessionView View { get; }

        public Dictionary<uint, uint[]> Worn { get; } = [];

        public double Now => R.H.R.Clock.Now;

        public uint Character => R.H.State.Self.Guid;

        public BuffCycle Cycle(ArmourBuffLedger ledger, IArmourLedgerStore? store = null) => new(R.Api, R.H.State, Caster.Table, Options, R.H.Log, ledger, store);

        public RebuffDriver Driver(BuffCycle cycle) => new(cycle, R.Api, View, Options, R.H.Log);

        /// <summary>The first cycle by hand, every line cast; the ledger then holds the armour casts.</summary>
        public void Build(BuffCycle cycle)
        {
            Assert.True(cycle.Start(Now));
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running)
            {
                R.Tick(0.25);
                Caster.Pump();
                AnswerIdentifies();
                R.Tick();
                status = cycle.Step(Now);
            }

            Assert.Equal(BuffStatus.Done, status);
        }

        /// <summary>One frame without a driver: the clock, the scripted ACE and the appraisal answers.</summary>
        public void Pump()
        {
            R.Tick(0.25);
            Caster.Pump();
            AnswerIdentifies();
            R.Tick();
        }

        public void Frame(RebuffDriver driver, double dt = 0.25)
        {
            R.Tick(dt);
            Caster.Pump();
            AnswerIdentifies();
            R.Tick();
            driver.Tick(Now);
        }

        public bool RunUntil(RebuffDriver driver, Func<bool> done, double maxSeconds = 300)
        {
            var end = Now + maxSeconds;
            while (Now < end)
            {
                if (done())
                {
                    return true;
                }

                Frame(driver);
            }

            return done();
        }

        /// <summary>Puts on every worn piece the armour spells the ledger holds for it (what ACE would say in an appraisal).</summary>
        public void WearWhatTheLedgerHolds(ArmourBuffLedger ledger)
        {
            foreach (var piece in Caster.PieceGuids)
            {
                var ids = new List<uint>();
                foreach (var line in new[] { Impen, Bane })
                {
                    if (ledger.TryGet(piece, line, out var entry))
                    {
                        ids.Add(entry.SpellId);
                    }
                }

                Worn[piece] = [.. ids];
            }
        }

        private void AnswerIdentifies()
        {
            var sends = R.H.Net.Of(FakeSendKind.Identify);
            for (; _answered < sends.Count; _answered++)
            {
                var guid = sends[_answered].Guid;
                var book = Worn.TryGetValue(guid, out var ids) ? ids.Select(i => i | 0x80000000u).ToArray() : [];
                R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, new AceAppraisal { Success = true, SpellBook = book }));
            }
        }

        public int Identifies => R.H.Net.Of(FakeSendKind.Identify).Count;
    }

    private sealed class FixView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    // ------------------------------------------------------------------------------------------------ R2

    [Fact]
    public void A_buffed_arrival_with_a_saved_store_is_seeded_and_restored_first_and_then_nothing_is_due_no_mode_no_pass_no_failure()
    {
        var t = new RebuffFixRig();
        var first = new ArmourBuffLedger();
        t.Build(t.Cycle(first));
        var store = new MemoryStore();
        store.Saved[t.Character] = first.Export(t.Character, t.Now); // the log-off of the session that ran the ledger
        t.WearWhatTheLedgerHolds(first);
        t.R.CombatModeIs(CombatModes.NonCombat);
        var identifiesBefore = t.Identifies;

        // A new login: an empty ledger, the saved store, a driver switched on.
        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger, store);
        using var driver = t.Driver(cycle);
        Assert.True(cycle.SeedPending);
        t.Frame(driver); // the first tick: it seeds, it does not count a due line

        Assert.Equal(RebuffDriverState.Running, driver.State);
        Assert.Equal(0, driver.Passes);
        Assert.Empty(t.Caster.ModesSent); // no combat mode before the seed

        Assert.True(t.RunUntil(driver, () => !cycle.SeedPending && driver.State == RebuffDriverState.Idle, 30));
        Assert.Equal(identifiesBefore + ScriptedCaster.PieceNames.Length, t.Identifies); // each worn piece appraised once
        Assert.Equal(1, store.Loads);
        foreach (var piece in t.Caster.PieceGuids)
        {
            Assert.InRange(ledger.Remaining(piece, Impen, t.Now), 1000, 4000); // restored with its seconds, not unknown (NaN)
            Assert.InRange(ledger.Remaining(piece, Bane, t.Now), 1000, 4000);
        }

        t.Frame(driver);
        t.Frame(driver);
        for (var i = 0; i < 80; i++)
        {
            t.Frame(driver); // 20 s: checks keep finding nothing due
        }

        Assert.Equal(0, driver.Passes);
        Assert.Equal(RebuffDriverState.Idle, driver.State);
        Assert.Null(driver.LastFailure);
        Assert.Empty(t.Caster.ModesSent);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.DoesNotContain(t.R.H.Log.Lines, static l => l.Contains("did not succeed", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, static l => l.Contains("backing off", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pass_that_finds_nothing_due_and_casts_nothing_is_not_a_failure()
    {
        // Review R2: if a restore (or anything else) removes the due lines between the check and the pass, the pass ends having cast nothing; that is Idle, not a 60 s back-off and a chat failure.
        var t = new RebuffFixRig();
        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger);
        t.Build(cycle);
        t.R.CombatModeIs(CombatModes.NonCombat);
        var impen = ArenaProfile.LineNamed(Impen)!.Spells[0].Id;
        foreach (var piece in t.Caster.PieceGuids)
        {
            ledger.Record(piece, Impen, impen, t.Now, duration: 5); // Impenetrability is inside the margin; everything else has half an hour
        }

        using var driver = t.Driver(cycle);
        Assert.Equal(["Impenetrability"], cycle.RebuffDue(t.Now, Margin));
        Assert.True(t.RunUntil(driver, () => driver.Passes == 1, 30));
        foreach (var piece in t.Caster.PieceGuids)
        {
            ledger.Record(piece, Impen, impen, t.Now, duration: 1800); // refreshed meanwhile
        }

        Assert.True(t.RunUntil(driver, () => driver.State != RebuffDriverState.Running, 60));
        Assert.Equal(0, driver.LastResult!.Cast);
        Assert.Null(driver.LastFailure);
        Assert.Equal(RebuffDriverState.Idle, driver.State);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode); // put back
        Assert.Contains(t.R.H.Log.Lines, static l => l.Contains("cast nothing; that is not a failure", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ R3 (the seeder)

    [Fact]
    public void The_seed_reads_the_saved_seconds_once_and_clears_the_store()
    {
        var t = new RebuffFixRig();
        var first = new ArmourBuffLedger();
        t.Build(t.Cycle(first));
        var store = new MemoryStore();
        store.Saved[t.Character] = first.Export(t.Character, t.Now);
        Assert.NotEmpty(ArmourBuffLedger.ReadSnapshot(store.Saved[t.Character], t.Character, out _));
        t.WearWhatTheLedgerHolds(first);

        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger, store);
        using var driver = t.Driver(cycle);
        Assert.True(t.RunUntil(driver, () => ledger.Seeded, 30));

        var after = ArmourBuffLedger.ReadSnapshot(store.Saved[t.Character], t.Character, out var why);
        Assert.Null(why); // a valid snapshot of this character...
        Assert.Empty(after); // ...with nothing in it: only the login that follows a save restores
        Assert.True(ledger.Count > 0);
    }

    [Fact]
    public void Seconds_played_since_the_login_are_taken_off_a_restored_line_and_a_line_they_used_up_is_due_at_once()
    {
        var t = new RebuffFixRig();
        var first = new ArmourBuffLedger();
        foreach (var piece in t.Caster.PieceGuids)
        {
            first.Record(piece, Impen, ArenaProfile.LineNamed(Impen)!.Spells[0].Id, doneAt: 0, duration: 1000);
            first.Record(piece, Bane, ArenaProfile.LineNamed(Bane)!.Spells[0].Id, doneAt: 0, duration: 100);
        }

        var store = new MemoryStore();
        store.Saved[t.Character] = first.Export(t.Character, now: 0); // 1000 s and 100 s left at the log-off
        t.WearWhatTheLedgerHolds(first);
        var loginAt = t.Now;
        t.R.Tick(300); // 300 s played before the seed
        var ledger = new ArmourBuffLedger { LoggedInAt = loginAt };
        var cycle = t.Cycle(ledger, store);
        cycle.StartSeed(t.Now);
        for (var i = 0; i < 100 && cycle.StepSeed() == BuffStatus.Running; i++)
        {
            t.Pump();
        }

        Assert.True(ledger.Seeded);

        foreach (var piece in t.Caster.PieceGuids)
        {
            Assert.InRange(ledger.Remaining(piece, Impen, t.Now), 695, 705); // 1000 - 300
            Assert.True(ledger.TryGet(piece, Bane, out var bane));
            Assert.False(bane.TimeKnown); // 100 s was used up by the play: present, time unknown, due at once
        }
    }

    // ------------------------------------------------------------------------------------------------ R4

    [Fact]
    public void With_no_armour_worn_the_armour_lines_are_not_due_no_pass_casts_them_and_nothing_backs_off()
    {
        var t = new RebuffFixRig();
        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger);
        t.Build(cycle);
        foreach (var piece in t.Caster.PieceGuids)
        {
            t.R.Raw(AceWireWriters.ObjectDelete(piece, 1)); // everything taken off
        }

        Assert.DoesNotContain(t.R.H.State.Self.Inventory.Equipped, e => t.Caster.PieceGuids.Contains(e.Guid));
        var due = cycle.RebuffDue(t.Now, double.PositiveInfinity);
        Assert.DoesNotContain(Impen, due);
        Assert.DoesNotContain(Bane, due);
        Assert.NotEmpty(due); // the self lines are still wanted

        // A driver pass for the self lines casts no armour line and logs no miss.
        var landedBefore = t.Caster.Landed.Count();
        t.R.CombatModeIs(CombatModes.NonCombat);
        var left = cycle.MinRemainingSeconds(t.Now);
        t.R.Tick(left - 8);
        using var driver = t.Driver(cycle);
        Assert.True(t.RunUntil(driver, () => driver.Passes >= 1 && driver.State is RebuffDriverState.Idle or RebuffDriverState.BackingOff, 300));
        Assert.Null(driver.LastFailure);
        Assert.Equal(0, driver.LastResult!.ArmourCast);
        Assert.DoesNotContain(driver.LastResult.Misses, static m => m.Contains("worn-piece", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Caster.Landed.Skip(landedBefore), id => ArenaProfile.LineNamed(Impen)!.Spells.Any(s => s.Id == id) || ArenaProfile.LineNamed(Bane)!.Spells.Any(s => s.Id == id));
        Assert.Equal(1, t.R.H.Log.Lines.Count(static l => l.Contains("armour lines are not wanted", StringComparison.Ordinal))); // logged once, not per check
    }
}

using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The play host's logic for the re-buff driver (M4 4.1 lane c, <see cref="RebuffHostLogic"/>) on the scripted mage: the switch (option and key), the profile rule and its notice, the driver's notices in the chat, the
/// ledger's save when the world is left, the SpellTable request, the key's name in the option help, and the idle tick allocating nothing.
/// </summary>
public sealed class RebuffHostLogicTests
{
    private sealed class MemoryStore : IArmourLedgerStore
    {
        public Dictionary<uint, string> Files { get; } = [];

        public string? Load(uint characterGuid) => Files.GetValueOrDefault(characterGuid);

        public void Save(uint characterGuid, string json) => Files[characterGuid] = json;
    }

    private sealed class HostRig
    {
        public HostRig(string name = "BSD Mage", bool auto = false, double margin = 10, MemoryStore? store = null)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            R.H.State.Self.Name = name;
            Options = new BuffOptions { RebuffMarginSeconds = margin, Auto = auto };
            View = new HostView(R);
            Store = store ?? new MemoryStore();
            Logic = new RebuffHostLogic(Options, R.H.Log, Store);
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffOptions Options { get; }

        public IGameSessionView View { get; }

        public MemoryStore Store { get; }

        public RebuffHostLogic Logic { get; }

        public double Now => R.H.R.Clock.Now;

        /// <summary>What ACE says each worn piece wears (spell ids), answered to the host's seed appraisals (M4 4.1 fix).</summary>
        public Dictionary<uint, uint[]> Worn { get; } = [];

        private int _answered;

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

        /// <summary>A snapshot as the previous session's log-off would have kept it: every worn piece holds both armour lines' first level with <paramref name="seconds"/> left, and the pieces wear those spells.</summary>
        public string SavedSnapshot(double seconds)
        {
            var saved = new ArmourBuffLedger();
            foreach (var piece in Caster.PieceGuids)
            {
                var impen = ArenaProfile.LineNamed("Impenetrability")!.Spells[0].Id;
                var bane = ArenaProfile.LineNamed("Bludgeon Bane")!.Spells[0].Id;
                saved.Record(piece, "Impenetrability", impen, doneAt: 0, duration: seconds);
                saved.Record(piece, "Bludgeon Bane", bane, doneAt: 0, duration: seconds);
                Worn[piece] = [impen, bane];
            }

            return saved.Export(R.H.State.Self.Guid, now: 0);
        }

        public List<string> Chat()
        {
            var lines = new List<string>();
            var chat = R.H.State.Chat;
            for (var i = 0; i < chat.Count; i++)
            {
                if (chat[i].Kind == ChatKind.Notice)
                {
                    lines.Add(chat[i].Text);
                }
            }

            return lines;
        }

        public void Frame(double dt = 0.25)
        {
            R.Tick(dt);
            Caster.Pump();
            AnswerIdentifies();
            R.Tick();
            Logic.Tick(View);
        }

        public bool RunUntil(Func<bool> done, double maxSeconds = 600)
        {
            var end = Now + maxSeconds;
            while (Now < end)
            {
                if (done())
                {
                    return true;
                }

                Frame();
            }

            return done();
        }

        /// <summary>Makes the driver and builds the first cycle by hand (the logic is not ticked while the cycle runs, or the driver would wait for it).</summary>
        public void BuildBuffs()
        {
            Logic.SetSpells(Caster.Table);
            Logic.Tick(View);
            var cycle = Logic.Cycle!;
            Assert.True(cycle.Start(Now));
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running)
            {
                R.Tick(0.25);
                Caster.Pump();
                R.Tick();
                status = cycle.Step(Now);
            }

            Assert.Equal(BuffStatus.Done, status);
        }
    }

    private sealed class HostView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    [Fact]
    public void Only_bsd_mage_has_a_buff_profile()
    {
        Assert.True(BuffProfiles.Has("BSD Mage"));
        Assert.True(BuffProfiles.Has("bsd mage"));
        Assert.False(BuffProfiles.Has("Holtburg Hunter"));
        Assert.False(BuffProfiles.Has(null));
    }

    [Fact]
    public void The_key_turns_the_driver_on_and_off_and_says_so_in_the_chat()
    {
        var t = new HostRig();
        t.Logic.Tick(t.View);
        Assert.False(t.Logic.On);
        Assert.False(t.Logic.NeedsSpells);
        Assert.Null(t.Logic.Driver);

        t.Logic.Toggle(t.View);
        Assert.True(t.Logic.On);
        Assert.True(t.Logic.NeedsSpells); // the shell loads the SpellTable
        Assert.Equal(["[Lifestone] auto-buff on"], t.Chat());

        t.Logic.SetSpells(t.Caster.Table);
        Assert.False(t.Logic.NeedsSpells);
        t.Logic.Tick(t.View);
        Assert.NotNull(t.Logic.Driver);
        Assert.True(t.Logic.Driver!.Enabled);

        t.Logic.Toggle(t.View);
        Assert.False(t.Logic.On);
        Assert.False(t.Logic.Driver.Enabled);
        Assert.Equal(["[Lifestone] auto-buff on", "[Lifestone] auto-buff off"], t.Chat());
    }

    [Fact]
    public void After_a_fault_stops_the_host_the_key_only_logs_that_the_driver_is_off_0_r9()
    {
        var t = new HostRig(auto: true);
        t.Logic.SetSpells(t.Caster.Table);
        t.Logic.Tick(t.View);
        Assert.NotNull(t.Logic.Driver);
        Assert.True(t.Logic.On);
        var chatBefore = t.Chat().Count;

        t.Logic.Stop(); // the host's frame faulted

        Assert.True(t.Logic.Stopped);
        Assert.False(t.Logic.On);
        Assert.Null(t.Logic.Driver);
        t.Logic.Toggle(t.View);
        t.Logic.Toggle(t.View);
        Assert.False(t.Logic.On); // the key flipped nothing
        Assert.Null(t.Logic.Driver); // and made no driver
        Assert.False(t.Logic.NeedsSpells);
        Assert.Equal(chatBefore, t.Chat().Count); // no "auto-buff on" notice with nothing behind it
        Assert.Equal(2, t.R.H.Log.Lines.Count(l => l.Contains("the driver is off for this run after an error", StringComparison.Ordinal))); // one log line per press
    }

    [Fact]
    public void The_option_turns_it_on_at_the_arrival_with_a_notice()
    {
        var t = new HostRig(auto: true);
        Assert.True(t.Logic.NeedsSpells);
        t.Logic.SetSpells(t.Caster.Table);
        t.Logic.Tick(t.View);

        Assert.True(t.Logic.On);
        Assert.True(t.Logic.HasProfile);
        Assert.True(t.Logic.Driver!.Enabled);
        Assert.Equal(["[Lifestone] auto-buff on"], t.Chat());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_character_without_a_profile_gets_the_notice_and_stays_off(bool option)
    {
        var t = new HostRig("Holtburg Hunter", auto: option);
        t.Logic.Tick(t.View);
        if (!option)
        {
            t.Logic.Toggle(t.View);
        }

        Assert.False(t.Logic.On);
        Assert.False(t.Logic.NeedsSpells);
        Assert.Null(t.Logic.Driver);
        Assert.Equal(["[Lifestone] no buff profile for this character"], t.Chat());

        t.Logic.Toggle(t.View); // again: still no profile
        Assert.False(t.Logic.On);
        Assert.Equal(2, t.Chat().Count);
        t.Frame();
        Assert.Null(t.Logic.Driver);
    }

    [Fact]
    public void A_pass_starts_and_ends_with_notices_and_casts_from_the_margin()
    {
        var t = new HostRig(auto: true);
        t.BuildBuffs();
        var before = t.Chat().Count;
        var left = t.Logic.Cycle!.MinRemainingSeconds(t.Now);
        t.R.Tick(left - 8); // inside the margin of 10 s for the soonest line

        Assert.True(t.RunUntil(() => t.Logic.Driver!.Passes >= 1 && t.Logic.Driver.State != RebuffDriverState.Running, 300));
        var notices = t.Chat().Skip(before).ToList();
        Assert.Equal(2, notices.Count);
        Assert.StartsWith("[Lifestone] re-buffing ", notices[0], StringComparison.Ordinal);
        Assert.Matches(@"^\[Lifestone\] re-buffed [1-9]\d* lines?$", notices[1]);
        Assert.Null(t.Logic.Driver!.LastFailure);
        Assert.Equal(24, t.Logic.Driver.LastResult!.Active);
    }

    [Fact]
    public void Turning_it_off_lets_a_pass_under_way_finish_and_starts_no_other()
    {
        var t = new HostRig(auto: true);
        t.BuildBuffs();
        t.R.Tick(t.Logic.Cycle!.MinRemainingSeconds(t.Now) - 8);
        Assert.True(t.RunUntil(() => t.Logic.Driver!.State == RebuffDriverState.Running, 60));

        t.Logic.Toggle(t.View);
        Assert.False(t.Logic.Driver!.Enabled);
        Assert.True(t.RunUntil(() => t.Logic.Driver.State != RebuffDriverState.Running, 300));
        Assert.Equal(1, t.Logic.Driver.Passes);
        Assert.Equal(RebuffDriverState.Off, t.Logic.Driver.State);
        Assert.Contains(t.Chat(), l => l.StartsWith("[Lifestone] re-buffed ", StringComparison.Ordinal));
    }

    [Fact]
    public void While_the_autopilot_owns_the_buffs_the_driver_stands_down_once_and_goes_on_after_4_r4()
    {
        var t = new HostRig(auto: true);
        t.BuildBuffs();
        var owned = true;
        t.Logic.StandDownWhile = () => owned;
        t.R.Tick(t.Logic.Cycle!.MinRemainingSeconds(t.Now) - 8); // a line is inside the margin: the driver would start a pass

        for (var i = 0; i < 40; i++)
        {
            t.Frame();
        }

        Assert.True(t.Logic.StandingDown);
        Assert.Equal(0, t.Logic.Driver!.Passes);
        Assert.False(t.Logic.Driver.Enabled);
        Assert.True(t.Logic.On); // the switch is the person's: it stays on
        Assert.Equal(1, t.R.H.Log.Lines.Count(l => l.Contains(RebuffHostLogic.StandDownLine, StringComparison.Ordinal))); // logged once, not per frame

        owned = false; // an interrupt or Done
        Assert.True(t.RunUntil(() => t.Logic.Driver.Passes >= 1, 300));
        Assert.False(t.Logic.StandingDown);
        Assert.Contains(t.R.H.Log.Lines, static l => l.Contains(RebuffHostLogic.ResumeLine, StringComparison.Ordinal));
    }

    [Fact]
    public void Leaving_the_world_saves_the_ledger_under_the_guid_and_stops_the_driver()
    {
        var t = new HostRig(auto: true);
        t.BuildBuffs();
        Assert.Empty(t.Store.Files);

        t.R.H.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
        t.Logic.Tick(t.View);

        Assert.Null(t.Logic.Driver);
        var guid = t.R.H.State.Self.Guid;
        Assert.True(t.Store.Files.ContainsKey(guid));
        var entries = ArmourBuffLedger.ReadSnapshot(t.Store.Files[guid], guid, out var why);
        Assert.Null(why);
        Assert.NotEmpty(entries); // the cycle cast the armour lines, and their seconds left are kept
        Assert.DoesNotContain("BSD", t.Store.Files[guid], StringComparison.Ordinal); // keyed by the guid, never the name
    }

    [Fact]
    public void The_saved_seconds_are_read_and_cleared_at_the_login_even_with_the_switch_off_and_a_session_without_the_driver_keeps_nothing()
    {
        // Review R3: the store is one-shot. A session played without the driver must not leave the old seconds for a later login.
        var t = new HostRig();
        var guid = t.R.H.State.Self.Guid;
        t.Store.Files[guid] = t.SavedSnapshot(1500);
        Assert.NotEmpty(ArmourBuffLedger.ReadSnapshot(t.Store.Files[guid], guid, out _));

        t.Logic.Tick(t.View); // the login: bound, profile found, switch off
        Assert.Null(t.Logic.Driver);
        var cleared = ArmourBuffLedger.ReadSnapshot(t.Store.Files[guid], guid, out var why);
        Assert.Null(why);
        Assert.Empty(cleared);

        t.R.Tick(600);
        t.R.H.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
        t.Logic.Tick(t.View); // the log-off of a session that never ran the ledger
        Assert.Empty(ArmourBuffLedger.ReadSnapshot(t.Store.Files[guid], guid, out _));
        Assert.DoesNotContain(t.R.H.Log.Lines, static l => l.Contains("are kept for the next login", StringComparison.Ordinal));
    }

    [Fact]
    public void Seconds_played_before_the_driver_was_switched_on_are_not_counted_as_kept()
    {
        // Review R3: ACE's clock runs while the character is online, so a restore counts from the login, not from the appraisal.
        var t = new HostRig();
        var guid = t.R.H.State.Self.Guid;
        t.Store.Files[guid] = t.SavedSnapshot(1000);
        t.Logic.Tick(t.View); // the login
        t.R.Tick(600); // 600 s played with the switch off
        t.Logic.Toggle(t.View);
        t.Logic.SetSpells(t.Caster.Table);

        var ledger = ArmourBuffLedger.For(t.R.H.State);
        Assert.True(t.RunUntil(() => ledger.Seeded, 60));
        foreach (var piece in t.Caster.PieceGuids)
        {
            Assert.InRange(ledger.Remaining(piece, "Impenetrability", t.Now), 380, 410); // 1000 - 600, not 1000
            Assert.InRange(ledger.Remaining(piece, "Bludgeon Bane", t.Now), 380, 410);
        }

        var due = t.Logic.Cycle!.RebuffDue(t.Now, 10); // 400 s left against the 10 s margin: the armour lines are not due (the self lines are, this session has none cast)
        Assert.DoesNotContain("Impenetrability", due);
        Assert.DoesNotContain("Bludgeon Bane", due);
    }

    [Fact]
    public void A_failed_spell_table_turns_it_off_loudly()
    {
        var t = new HostRig(auto: true);
        t.Logic.Tick(t.View);
        t.Logic.SpellsFailed("no dats");

        Assert.False(t.Logic.On);
        Assert.Contains(t.Chat(), l => l.Contains("spell table could not be read", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("SpellTable could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void An_idle_tick_between_checks_allocates_nothing()
    {
        var t = new HostRig(auto: true);
        t.BuildBuffs();
        t.Frame();
        t.Frame(); // the first check ran and found nothing due
        for (var i = 0; i < 20; i++)
        {
            t.Logic.Tick(t.View);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            t.Logic.Tick(t.View);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void The_auto_buff_key_is_pause()
    {
        Assert.Equal(Lifestone.Client.RetailUi.Input.DikScanCodes.Pause, LifestoneHotkeys.AutoBuffKey);
    }
}

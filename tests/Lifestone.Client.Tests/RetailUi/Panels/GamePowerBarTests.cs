using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Diagnostics;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The power bar's driver (<see cref="AttackPowerDriver"/>, M4 1.3 s1; docs/retail-ui.md section 23.8) over the real Action API, reducer and attack sequence of item 1.2's rig, with the live order of cache/m4/1-2/capture.md: ONE send
/// gives a whole run of swings (01A7 {0} after each, 01B8 before each repeat, 01A7 {0x0036} at the end). The bar is begun by the attack (the press and the click of the combat window, or the send of any other source), levelled while
/// the power charges at one per second, capped at the power asked for, kept through the swings and finished by the 0x01A7 that ends the run. Nothing here needs the dats.
/// </summary>
public sealed class GamePowerBarTests
{
    private sealed class View(AttackRig rig) : IGameSessionView
    {
        public GameState State => rig.W.H.State;

        public IEventBus Events => rig.W.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.W.H.R.Clock;

        public ActionLog ActionLog => rig.W.H.ActionLog;

        public LinkStats Link => default;

        public AttackSequence Attack => rig.Seq;
    }

    private sealed class Notices : IPowerBarNotices
    {
        public List<string> Log { get; } = [];

        public PowerBarMode Open { get; private set; }

        public float Level { get; private set; }

        public int Begins { get; private set; }

        public int Finishes { get; private set; }

        public void Begin(PowerBarMode mode)
        {
            Begins++;
            Open = mode;
            Log.Add("begin " + mode);
        }

        public void SetLevel(PowerBarMode mode, float level)
        {
            Level = level;
            Log.Add($"level {mode} {level:0.00}");
        }

        public void Finish(PowerBarMode mode)
        {
            Finishes++;
            Open = PowerBarMode.Undefined;
            Log.Add("finish " + mode);
        }
    }

    private sealed class Rig : IDisposable
    {
        public Rig(float slider = 0.5f, bool advanced = false, uint selected = AttackRig.Reedshark, int mode = CombatModes.Melee)
        {
            Attack = new AttackRig();
            Attack.Creature(AttackRig.Reedshark, "Adult Reedshark");
            Attack.Mode(mode);
            Slider = slider;
            Advanced = advanced;
            Selected = selected;
            Driver = new AttackPowerDriver(new View(Attack), Notices, () => Now, () => Selected, () => Slider, () => Advanced, Said.Add, null);
        }

        public AttackRig Attack { get; }

        public Notices Notices { get; } = new();

        public List<string> Said { get; } = [];

        public AttackPowerDriver Driver { get; }

        public double Now { get; private set; } = 100.0;

        public float Slider { get; set; }

        public bool Advanced { get; set; }

        public uint Selected { get; set; }

        public int Melee => Attack.Net.Of(FakeSendKind.MeleeAttack).Count;

        /// <summary>Time passes: the window's per-frame use time, then the Action API's update (which sends what is queued).</summary>
        public void Advance(double seconds)
        {
            Now += seconds;
            Driver.Tick();
            Attack.Tick(seconds);
        }

        public void Dispose() => Driver.Dispose();
    }

    [Fact]
    public void A_press_begins_the_bar_and_the_level_rises_one_per_second_until_the_click_and_the_attack_goes_when_it_reaches_the_slider()
    {
        using var r = new Rig(slider: 0.5f);
        r.Driver.PressHeight(AttackHeight.Medium);
        Assert.Equal(["begin Combat", "level Combat 0.00"], r.Notices.Log);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode); // the basic UI: the combat window's own meter
        Assert.True(r.Driver.Building);
        Assert.Equal(0, r.Melee); // a press sends nothing

        r.Advance(0.25);
        Assert.Equal(0.25f, r.Notices.Level, 3);
        r.Driver.ReleaseHeight(AttackHeight.Medium);
        Assert.Equal(0.5f, r.Driver.RequestedPower, 3); // the slider is above the bar: the bar goes on charging to it
        Assert.Equal(0, r.Melee);
        Assert.Equal(0, r.Driver.Sends);

        r.Advance(0.1);
        Assert.Equal(0.35f, r.Notices.Level, 3);
        Assert.Equal(0, r.Melee);

        r.Advance(0.15); // the level reaches 0.5: sent
        Assert.Equal(0.5f, r.Notices.Level, 3);
        var sent = Assert.Single(r.Attack.Net.Of(FakeSendKind.MeleeAttack));
        Assert.Equal(AttackRig.Reedshark, sent.Guid);
        Assert.Equal(2u, sent.Amount);
        Assert.Equal(0.5f, sent.Extent);
        Assert.False(r.Driver.Building); // the build ends at the send
        Assert.True(r.Driver.ResponsePending);
        Assert.Equal(1, r.Driver.Sends);
    }

    [Fact]
    public void A_long_press_stops_at_one_and_the_click_sends_the_held_power_at_once()
    {
        using var r = new Rig(slider: 0.6f);
        r.Driver.PressHeight(AttackHeight.High);
        r.Advance(2.0);
        Assert.Equal(1f, r.Notices.Level, 3); // the level never passes 1
        Assert.Equal(0, r.Melee);
        r.Driver.ReleaseHeight(AttackHeight.High);
        r.Advance(0);
        var sent = Assert.Single(r.Attack.Net.Of(FakeSendKind.MeleeAttack));
        Assert.Equal(1u, sent.Amount); // the height
        Assert.Equal(1f, sent.Extent); // the held power, above the slider's
    }

    [Fact]
    public void A_click_sends_a_missile_attack_in_missile_mode_with_the_power_as_the_accuracy()
    {
        using var r = new Rig(slider: 0.4f, mode: CombatModes.Missile);
        r.Driver.PressHeight(AttackHeight.Low);
        r.Driver.ReleaseHeight(AttackHeight.Low);
        r.Advance(0.5);
        Assert.Empty(r.Attack.Net.Of(FakeSendKind.MeleeAttack));
        var shot = Assert.Single(r.Attack.Net.Of(FakeSendKind.MissileAttack));
        Assert.Equal(3u, shot.Amount);
        Assert.Equal(0.4f, shot.Extent);
    }

    [Fact]
    public void The_bar_is_begun_by_the_action_send_and_a_click_alone_with_nothing_selected_never_shows_it()
    {
        using var r = new Rig(selected: 0);
        r.Driver.PressHeight(AttackHeight.Medium);
        r.Driver.ReleaseHeight(AttackHeight.Medium);
        r.Advance(1.0);
        Assert.Contains(AttackPowerDriver.NoTargetText, r.Said);
        Assert.Empty(r.Notices.Log); // no begin, no level
        Assert.Equal(PowerBarMode.Undefined, r.Driver.Mode);
        Assert.Equal(0, r.Melee);
        Assert.Equal(0, r.Driver.Sends);
    }

    [Fact]
    public void A_refusal_before_the_send_never_shows_the_bar()
    {
        using var r = new Rig();
        var refused = r.Attack.Api.Attack(0, AttackHeight.Medium, 0.5f, ActionSource.Host); // no target
        var badPower = r.Attack.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 2f, ActionSource.Host);
        var badHeight = r.Attack.Api.Attack(AttackRig.Reedshark, (AttackHeight)9, 0.5f, ActionSource.Host);
        r.Advance(0.2);
        Assert.Equal(ActionOutcomeKind.Refused, refused.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, badPower.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, badHeight.Outcome!.Value.Kind);
        Assert.Empty(r.Notices.Log);
        Assert.Empty(r.Attack.Net.Of(FakeSendKind.MeleeAttack));
        Assert.Equal(0, r.Notices.Begins);
    }

    [Fact]
    public void An_attack_sent_by_the_Host_source_shows_the_bar_too_and_it_stops_at_the_power_sent()
    {
        using var r = new Rig();
        r.Attack.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.4f, ActionSource.Host); // the hunt loop's send
        Assert.Empty(r.Notices.Log); // queued, not sent yet
        r.Advance(0); // the send
        Assert.Single(r.Attack.Net.Of(FakeSendKind.MeleeAttack));
        Assert.Equal(["begin Combat", "level Combat 0.00"], r.Notices.Log);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode);

        r.Advance(0.2);
        Assert.Equal(0.2f, r.Notices.Level, 3);
        r.Advance(0.5); // 0.7 s of charge: capped at the power sent
        Assert.Equal(0.4f, r.Notices.Level, 3);
        var count = r.Notices.Log.Count;
        r.Advance(1.0);
        Assert.Equal(count, r.Notices.Log.Count); // the charge stopped: nothing more is shown
        Assert.Equal(0.4f, r.Driver.LatestLevel, 3);
        Assert.Equal(0, r.Driver.Sends); // this driver sent nothing itself
    }

    [Fact]
    public void A_Host_run_is_never_re_sent_at_the_slider_after_a_swing_1_3_R1()
    {
        using var r = new Rig(slider: 0.5f);
        r.Attack.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.4f, ActionSource.Host);
        r.Advance(0);
        r.Attack.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Attack.Done(0);
        r.Advance(0.1);
        Assert.Equal(1, r.Melee); // the hunt's one send, and nothing from the driver
        Assert.Equal(0, r.Driver.Sends);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode); // the bar is still shown and driven
    }

    [Fact]
    public void A_Host_run_with_nothing_selected_prints_no_target_line_after_a_swing_1_3_R1()
    {
        using var r = new Rig(slider: 0.5f, selected: 0);
        r.Attack.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.4f, ActionSource.Host);
        r.Advance(0);
        r.Attack.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Attack.Done(0);
        r.Advance(0.1);
        Assert.DoesNotContain(AttackPowerDriver.NoTargetText, r.Said);
        Assert.Equal(1, r.Melee);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode);
    }

    [Fact]
    public void A_Ui_run_is_re_sent_at_the_slider_when_it_differs_after_a_swing_1_3_R1()
    {
        using var r = new Rig(slider: 0.5f);
        r.Driver.PressHeight(AttackHeight.Medium);
        r.Driver.ReleaseHeight(AttackHeight.Medium);
        r.Advance(0.5);
        Assert.Equal(1, r.Melee);
        r.Slider = 0.8f;
        r.Attack.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Attack.Done(0);
        r.Advance(0.1);
        Assert.Equal(2, r.Melee);
        Assert.Equal(2, r.Driver.Sends);
    }

    [Fact]
    public void An_automation_attack_under_the_advanced_option_uses_the_power_bar_window_mode()
    {
        using var r = new Rig(advanced: true);
        r.Attack.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        r.Advance(0);
        Assert.Equal(["begin Advanced", "level Advanced 0.00"], r.Notices.Log);
        r.Attack.Done(AttackRig.Over);
        Assert.Equal(PowerBarMode.Undefined, r.Driver.Mode);
        Assert.Equal("finish Advanced", r.Notices.Log[^1]);
    }

    [Fact]
    public void One_send_with_three_swings_keeps_the_bar_through_01A7_0_and_01B8_and_finishes_it_on_01A7_0x0036()
    {
        using var r = new Rig(slider: 0.5f);
        r.Driver.PressHeight(AttackHeight.Medium);
        r.Driver.ReleaseHeight(AttackHeight.Medium);
        r.Advance(0.5); // charged to the slider: ONE send
        Assert.Equal(1, r.Melee);

        // Swing one: the swing shows the bar at the power; 01A7 {0} (the swing is over) keeps it and starts the next charge from 0.
        r.Attack.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        Assert.Equal(0.5f, r.Notices.Level, 3);
        Assert.False(r.Driver.Building);
        r.Attack.Done(0);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode); // not hidden between swings
        Assert.Equal(0, r.Notices.Finishes);
        Assert.Equal(0f, r.Notices.Level);
        Assert.True(r.Driver.Building);
        r.Advance(0.3);
        Assert.Equal(0.3f, r.Notices.Level, 3);

        // 01B8 before the repeat: the bar shows the power and the build ends (HandleCommenceAttackEvent).
        r.Attack.Commence();
        Assert.Equal(0.5f, r.Notices.Level, 3);
        Assert.False(r.Driver.Building);
        r.Attack.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark); // swing two
        r.Attack.Done(0);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode);
        r.Advance(0.2);
        r.Attack.Commence();
        r.Attack.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark); // swing three
        Assert.Equal(0.5f, r.Notices.Level, 3);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode);

        // The run ends: 01A7 {0x0036}. The meter empties and the mode is forgotten.
        r.Attack.Done(AttackRig.Over);
        Assert.Equal(PowerBarMode.Undefined, r.Driver.Mode);
        Assert.Equal(0f, r.Notices.Level);
        Assert.Equal("level Combat 0.00", r.Notices.Log[^1]); // HidePowerBar, mode 1: the combat meter to 0
        Assert.False(r.Driver.Building);
        Assert.False(r.Driver.ResponsePending);
        Assert.Equal(1, r.Melee); // ONE send for all of it
        Assert.Equal(1, r.Driver.Sends);
        r.Advance(2.0);
        Assert.Equal(0f, r.Notices.Level); // nothing charges after the end
    }

    [Fact]
    public void A_refused_run_ends_the_bar_on_the_01A7_that_follows_with_no_swing()
    {
        using var r = new Rig();
        r.Attack.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Host);
        r.Advance(0);
        Assert.Equal(PowerBarMode.Combat, r.Driver.Mode);
        r.Attack.Error028B(0x0550, "Target out of range"); // ACE refuses...
        r.Attack.Done(AttackRig.Over); // ...and ends the run: no swing ever came
        Assert.Equal(PowerBarMode.Undefined, r.Driver.Mode);
        Assert.Equal(0f, r.Notices.Level);
        Assert.False(r.Driver.Building);
    }

    [Fact]
    public void A_second_press_during_a_run_waits_for_the_01A7_and_is_then_sent()
    {
        using var r = new Rig(slider: 0.5f);
        r.Driver.PressHeight(AttackHeight.Medium);
        r.Driver.ReleaseHeight(AttackHeight.Medium);
        r.Advance(0.5);
        Assert.Equal(1, r.Melee);
        r.Attack.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);

        // A click while the reply is pending is kept (EndAttackRequest sets attackWhenResponseReceived) and made when the 01A7 comes.
        r.Driver.PressHeight(AttackHeight.High);
        r.Driver.ReleaseHeight(AttackHeight.High);
        r.Advance(0.1);
        Assert.Equal(1, r.Driver.Sends);
        r.Attack.Done(0);
        r.Advance(0);
        Assert.Equal(2, r.Driver.Sends);
        Assert.Equal(2, r.Melee);
        Assert.Equal(1u, r.Attack.Net.Of(FakeSendKind.MeleeAttack)[1].Amount);
    }
}

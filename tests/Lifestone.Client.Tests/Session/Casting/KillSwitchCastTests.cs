using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Casting;

/// <summary>
/// The kill switch and dry run over the callers' casts (M5 item 1 lane K3, D13): now that the buff cycle's casts, mode changes and drinks are <see cref="ActionSource.Automation"/>, the Action API's kill switch
/// reaches the whole pass and not only a cast, and dry run answers every cast. On the rig the buff tests use (the real Action API, <see cref="ScriptedCaster"/> as the server): a cast queued in the Action API and a
/// try waiting its spacing are cancelled with nothing sent, a buff pass stopped mid-cycle by the kill switch ends named with no cast sent after it, dry run answers DryRun for every cast of a pass, and releasing the
/// kill switch lets a new pass run.
/// </summary>
public sealed class KillSwitchCastTests
{
    private sealed class Rig
    {
        public Rig(uint mana = 426, uint stamina = 275)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, mana, stamina);
            Cycle = new BuffCycle(R.Api, R.H.State, Caster.Table, new BuffOptions(), R.H.Log);
            Attempt = new CastAttempt(R.Api, R.H.State, R.H.Log, Caster.Table);
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffCycle Cycle { get; }

        public CastAttempt Attempt { get; }

        public double Now => R.H.R.Clock.Now;

        /// <summary>The casts that went out on the wire (targeted and untargeted).</summary>
        public int Sent => R.H.Net.Of(FakeSendKind.CastUntargetedSpell).Count + R.H.Net.Of(FakeSendKind.CastTargetedSpell).Count;

        /// <summary>One frame, as the buff tests'.</summary>
        public BuffStatus Frame(double dt = 0.25)
        {
            R.Tick(dt);
            Caster.Pump();
            R.Tick();
            return Cycle.Step(Now);
        }

        /// <summary>Runs the pass to its end, calling <paramref name="each"/> after every frame.</summary>
        public BuffStatus Run(Action<Rig>? each = null, double maxSeconds = 4000)
        {
            var end = Now + maxSeconds;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && Now < end)
            {
                status = Frame();
                each?.Invoke(this);
            }

            return status;
        }
    }

    [Fact]
    public void A_cast_queued_in_the_Action_API_is_cancelled_by_the_kill_switch_and_nothing_is_sent()
    {
        var t = new Rig();
        var line = t.Cycle.Profile.BuffOrder[0].Spells.Select(s => s.Id).ToArray();
        Assert.Equal(CastEnd.Running, t.Attempt.Start(new CastRequest { Levels = line, Index = 0, Rules = CastRules.BuffLine, Purpose = "queued" }, t.Now)); // queued: the Action API sends it at its next update
        Assert.Equal(0, t.Sent);
        t.R.Api.KillSwitch = true;
        Assert.Equal(ActionOutcomeKind.Cancelled, t.Attempt.Handle!.Outcome!.Value.Kind);
        Assert.Equal(CastEnd.Cancelled, t.Attempt.Step(t.Now));
        t.R.Tick(1);
        t.Caster.Pump();
        t.Attempt.Step(t.Now);
        Assert.Equal(0, t.Sent);
        Assert.Empty(t.Caster.Answered);
    }

    [Fact]
    public void A_try_waiting_its_spacing_is_cancelled_by_the_kill_switch_with_nothing_more_sent()
    {
        var t = new Rig(mana: 5); // far under any ceiling: ACE answers 0x0401, the dance waits its spacing
        var line = t.Cycle.Profile.BuffOrder[0].Spells.Select(s => s.Id).ToArray();
        t.Attempt.Start(new CastRequest { Levels = line, Index = line.Length - 1, Rules = CastRules.ManaDance(40, 2) }, t.Now);
        t.R.Tick();
        t.Caster.Pump();
        t.R.Tick();
        Assert.Equal(CastEnd.Running, t.Attempt.Step(t.Now)); // refused, waiting the 2 s spacing
        var sent = t.Sent;
        Assert.Equal(1, sent);
        t.R.Api.KillSwitch = true;
        Assert.Equal(CastEnd.Cancelled, t.Attempt.Step(t.Now));
        t.R.Tick(5);
        t.Caster.Pump();
        t.Attempt.Step(t.Now);
        Assert.Equal(sent, t.Sent);
        Assert.Contains("kill switch", t.Attempt.Report.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_buff_pass_stopped_by_the_kill_switch_after_its_second_cast_sends_nothing_after_it_and_ends_named()
    {
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        var switched = false;
        var status = t.Run(rig =>
        {
            if (!switched && rig.Caster.Answered.Count >= 2)
            {
                switched = true;
                rig.R.Api.KillSwitch = true;
            }
        });

        Assert.True(switched);
        Assert.Equal(BuffStatus.Failed, status);
        Assert.Equal(2, t.Caster.Answered.Count);
        Assert.Equal(2, t.Sent);
        var result = t.Cycle.Result!;
        Assert.Contains("the cast was not carried out", result.Failure, StringComparison.Ordinal);
        Assert.Contains("kill switch", result.Failure, StringComparison.Ordinal);
        Assert.True(result.Cast <= 2);
        Assert.Contains(t.R.H.Log.Entries, e => e.Message.StartsWith("FAILED: ", StringComparison.Ordinal) && e.Message.Contains("kill switch", StringComparison.Ordinal) && e.Message.EndsWith("proved=0", StringComparison.Ordinal));

        // It stays stopped: more frames send nothing.
        for (var i = 0; i < 20; i++)
        {
            t.Frame();
        }

        Assert.Equal(2, t.Sent);
    }

    [Fact]
    public void A_mode_change_of_the_pass_is_stopped_too_because_it_is_an_automation_send()
    {
        var t = new Rig();
        t.R.Api.KillSwitch = true;
        var change = t.R.Api.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.Refused, change.Outcome!.Value.Kind);
        Assert.Contains("kill switch", change.Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(t.Caster.ModesSent);
    }

    [Fact]
    public void Dry_run_answers_DryRun_for_every_cast_of_a_pass_and_sends_none()
    {
        var t = new Rig();
        t.R.Api.DryRun = true;
        var lines = t.Cycle.Profile.BuffOrder;
        for (var i = 0; i < 6; i++)
        {
            var ids = lines[i].Spells.Select(s => s.Id).ToArray();
            Assert.Equal(CastEnd.DryRun, t.Attempt.Start(new CastRequest { Levels = ids, Index = ids.Length - 1, Rules = CastRules.BuffLine }, t.Now));
            Assert.Equal(ActionOutcomeKind.DryRun, t.Attempt.LastOutcome!.Value.Kind);
        }

        Assert.Equal(0, t.Sent);

        // The pass itself: its first cast is answered DryRun, so the pass ends named with nothing sent.
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Failed, t.Run());
        Assert.Equal(0, t.Sent);
        Assert.Contains("DryRun: would send", t.Cycle.Result!.Failure, StringComparison.Ordinal);
        Assert.Empty(t.Caster.Answered);
    }

    [Fact]
    public void Releasing_the_kill_switch_lets_a_new_pass_run_to_its_end()
    {
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        var switched = false;
        Assert.Equal(BuffStatus.Failed, t.Run(rig =>
        {
            if (!switched && rig.Caster.Answered.Count >= 2)
            {
                switched = true;
                rig.R.Api.KillSwitch = true;
            }
        }));

        t.R.Api.KillSwitch = false;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(24, t.Cycle.Result!.Active);
        Assert.Empty(t.Cycle.Result.Misses);
        Assert.True(t.Sent > 2);
    }
}

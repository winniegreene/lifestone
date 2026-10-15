using Lifestone.Client.Movement;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session;

/// <summary>The hosted ring's jump leg (m2 wave 3, dod-8-9-2): the keys it presses, the order, and the two ways it fails.</summary>
public sealed class JumpLegTests
{
    private sealed class FakeIntents(double airSeconds, bool leaves = true) : IPlayerMovementIntents
    {
        private double _landAt = double.NaN;
        public double Now;
        public List<string> Log { get; } = [];
        public bool Forward { get; private set; }
        public bool Run { get; private set; }

        public CellFrame Position => default;

        public bool OnGround => double.IsNaN(_landAt) || Now >= _landAt;

        public void HandleCommand(uint command, bool start, float speed = 1f)
        {
            Assert.Equal(MotionCommands.WalkForward, command);
            Forward = start;
            Log.Add(start ? "forward-down" : "forward-up");
        }

        public void SetHoldRun(bool on)
        {
            Run = on;
            Log.Add(on ? "run-on" : "run-off");
        }

        public void SetHoldSidestep(bool on) => throw new NotSupportedException();

        public void ToggleAutoRun() => throw new NotSupportedException();

        public void CommenceJump(double now) => Log.Add(Forward ? "charge-running" : "charge-standing");

        public void DoJump(double now)
        {
            Log.Add(Forward ? "jump-running" : "jump-standing");
            if (leaves)
            {
                _landAt = now + airSeconds;
            }
        }
    }

    private static void Run(JumpLeg leg, FakeIntents intents, double maxSeconds = 60)
    {
        for (var t = 0.0; t < maxSeconds && !leg.IsDone; t += 1.0 / 60.0)
        {
            intents.Now = t;
            leg.Tick(t);
        }
    }

    [Fact]
    public void A_standing_jump_then_a_running_jump_press_the_keys_in_order_and_both_land()
    {
        var intents = new FakeIntents(airSeconds: 1.0);
        var leg = new JumpLeg(intents, runByDefault: false);

        Run(leg, intents);

        Assert.True(leg.IsDone);
        Assert.Null(leg.Failure);
        Assert.Equal(2, leg.Jumps);
        Assert.Equal(2, leg.Landed);
        Assert.Equal(
            ["charge-standing", "jump-standing", "forward-down", "run-on", "charge-running", "jump-running", "run-off", "forward-up"],
            intents.Log);
        Assert.False(intents.Forward);
    }

    [Fact]
    public void With_run_by_default_on_the_run_key_is_not_held_for_the_run_up()
    {
        var intents = new FakeIntents(airSeconds: 0.8);
        var leg = new JumpLeg(intents, runByDefault: true);

        Run(leg, intents);

        Assert.Null(leg.Failure);
        Assert.Contains("run-off", intents.Log); // SetHoldRun(!runByDefault) with the option on: the key stays up and forward runs
        Assert.DoesNotContain("run-on", intents.Log);
    }

    [Fact]
    public void A_jump_that_never_leaves_the_ground_fails_the_leg_naming_it()
    {
        var intents = new FakeIntents(airSeconds: 1.0, leaves: false);
        var leg = new JumpLeg(intents, runByDefault: false);

        Run(leg, intents);

        Assert.True(leg.IsDone);
        Assert.NotNull(leg.Failure);
        Assert.Contains("standing jump did not leave the ground", leg.Failure);
        Assert.Equal(1, leg.Jumps);
        Assert.Equal(0, leg.Landed);
    }

    [Fact]
    public void A_jump_that_never_comes_down_fails_the_leg_naming_it()
    {
        var intents = new FakeIntents(airSeconds: 1000.0);
        var leg = new JumpLeg(intents, runByDefault: false);

        Run(leg, intents);

        Assert.Contains("standing jump did not come down", leg.Failure);
    }
}

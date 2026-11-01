using System.Globalization;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// M5 7 D145: the movement keys' lock while the autopilot runs (<see cref="HumanKeyLock"/>, asked by <see cref="MovementBindings"/> before an action reaches the player's movement). Locked, every action of the movement map does nothing,
/// each press is logged and the chat notice is rate-limited; a key pressed before the lock is let go; unlocked, everything goes through as before. No dats: the actions are handed to the bindings as the router would.
/// </summary>
public sealed class MovementKeyLockTests
{
    private static RetailActivation Down => new(true, 1f, RetailToggleType.Impulse, 0, 0);

    private static RetailActivation Up => new(false, 0f, RetailToggleType.Impulse, 0, 0);

    private sealed class Rig
    {
        public Rig()
        {
            Bindings = new MovementBindings(Intents, () => Now) { Lock = Lock };
            Bindings.HumanKey += Human.Add;
            Lock.Notice += Notices.Add;
        }

        public RecordingIntents Intents { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public HumanKeyLock Lock => _lock ??= new HumanKeyLock(Log);

        public MovementBindings Bindings { get; }

        public List<string> Human { get; } = [];

        public List<string> Notices { get; } = [];

        public double Now { get; set; } = 100;

        private HumanKeyLock? _lock;

        public bool Press(uint action) => Bindings.OnInputAction(UiInputRouter.MovementMap, action, Down);

        public bool Release(uint action) => Bindings.OnInputAction(UiInputRouter.MovementMap, action, Up);
    }

    public static TheoryData<uint> EveryMovementAction => new()
    {
        MovementBindings.MoveForward, MovementBindings.MoveBackward, MovementBindings.TurnLeft, MovementBindings.TurnRight, MovementBindings.StrafeLeft, MovementBindings.StrafeRight,
        MovementBindings.Jump, MovementBindings.RunKey, MovementBindings.StopMoving, MovementBindings.AutoRun, MovementBindings.FirstEmoteAction,
    };

    [Theory]
    [MemberData(nameof(EveryMovementAction))]
    public void Locked_a_movement_key_reaches_nothing_and_is_logged_and_is_reported_handled(uint action)
    {
        var rig = new Rig();
        rig.Lock.Lock("the autopilot runs");
        Assert.True(rig.Press(action)); // handled: it goes nowhere else either
        Assert.True(rig.Release(action));
        Assert.Empty(rig.Intents.Calls); // forward, back, turns, strafes, jump, run/walk, stop, autorun and the emotes do nothing
        Assert.Empty(rig.Human); // and no HumanKey: nothing downstream sees a person's key
        Assert.Equal(1, rig.Lock.Swallowed);
        Assert.Contains(rig.Log.Lines, l => l.Contains("autopilot keys: swallowed " + MovementBindings.NameOf(action), StringComparison.Ordinal));
    }

    [Fact]
    public void Unlocked_the_keys_go_through_as_before_and_the_camera_map_is_never_asked()
    {
        var rig = new Rig();
        Assert.True(rig.Press(MovementBindings.MoveForward));
        Assert.True(rig.Release(MovementBindings.MoveForward));
        Assert.Equal(2, rig.Intents.Calls.Count);
        Assert.Equal(["MoveForward"], rig.Human);

        rig.Lock.Lock("the autopilot runs");
        Assert.False(rig.Bindings.OnInputAction(5, 0x10, Down)); // another map (the camera's): not the lock's, not the bindings'
        Assert.Equal(0, rig.Lock.Swallowed);

        rig.Lock.Unlock("/stop");
        Assert.True(rig.Press(MovementBindings.TurnLeft));
        Assert.Equal(3, rig.Intents.Calls.Count);
        Assert.Equal(["MoveForward", "TurnLeft"], rig.Human);
    }

    [Fact]
    public void The_notice_is_shown_at_most_once_every_few_seconds_and_every_press_is_logged()
    {
        var rig = new Rig();
        rig.Lock.Lock("the autopilot runs");
        rig.Press(MovementBindings.MoveForward);
        rig.Now += 1;
        rig.Press(MovementBindings.TurnLeft);
        rig.Now += 1;
        rig.Press(MovementBindings.Jump);
        Assert.Equal([HumanKeyLock.NoticeLine], rig.Notices);
        Assert.Equal((3, 1), (rig.Lock.Swallowed, rig.Lock.Notices));

        rig.Now += HumanKeyLock.NoticeSeconds;
        rig.Press(MovementBindings.MoveBackward);
        Assert.Equal(2, rig.Notices.Count);
        Assert.Equal(4, rig.Log.Lines.Count(l => l.Contains("autopilot keys: swallowed", StringComparison.Ordinal)));
        Assert.Equal(2, rig.Log.Lines.Count(l => l.Contains("notice shown", StringComparison.Ordinal)));
        Assert.Equal(2, rig.Log.Lines.Count(l => l.Contains("notice rate-limited", StringComparison.Ordinal)));
        Assert.Contains("/stop", HumanKeyLock.NoticeLine, StringComparison.Ordinal);

        // A new lock (the autopilot started again) says it at the first press, whatever the clock.
        rig.Lock.Unlock("/stop");
        rig.Lock.Lock("/auto");
        rig.Press(MovementBindings.MoveForward);
        Assert.Equal(3, rig.Notices.Count);
    }

    [Fact]
    public void A_key_held_when_the_lock_comes_on_is_let_go_and_a_swallowed_press_stays_swallowed_after_the_unlock()
    {
        var rig = new Rig();
        rig.Press(MovementBindings.MoveForward); // the person holds W
        rig.Lock.Lock("/auto");
        Assert.Equal(1, rig.Lock.HeldByPerson);
        rig.Release(MovementBindings.MoveForward); // its release goes through: the walk is not left on
        Assert.Equal(["cmd 45000005 True", "cmd 45000005 False"], rig.Intents.Calls);
        Assert.Equal(0, rig.Lock.HeldByPerson);
        Assert.Contains(rig.Log.Lines, l => l.Contains("released MoveForward while locked", StringComparison.Ordinal));

        rig.Press(MovementBindings.TurnRight); // swallowed
        rig.Release(MovementBindings.TurnRight); // its release while locked: swallowed too
        Assert.Equal(2, rig.Intents.Calls.Count);

        rig.Press(MovementBindings.TurnRight); // swallowed; the person still holds it when /stop unlocks
        rig.Lock.Unlock("/stop");
        rig.Release(MovementBindings.TurnRight); // after the unlock a release goes on (a stop of a turn that never started: harmless)
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"cmd {MovementBindings.CommandOf(MovementBindings.TurnRight):X8} False"), rig.Intents.Calls[^1]);
        Assert.Equal(3, rig.Intents.Calls.Count);
    }

    [Fact]
    public void Lock_and_unlock_log_once_each_change()
    {
        var rig = new Rig();
        rig.Lock.Lock("a");
        rig.Lock.Lock("b");
        rig.Lock.Unlock("c");
        rig.Lock.Unlock("d");
        Assert.Equal(1, rig.Log.Lines.Count(l => l.Contains("autopilot keys: locked", StringComparison.Ordinal)));
        Assert.Equal(1, rig.Log.Lines.Count(l => l.Contains("autopilot keys: unlocked", StringComparison.Ordinal)));
        Assert.False(rig.Lock.Locked);
        Assert.Equal("d", rig.Lock.Why);
    }

    internal sealed class RecordingIntents : IPlayerMovementIntents
    {
        public List<string> Calls { get; } = [];

        public CellFrame Position => default;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f) => Calls.Add(string.Create(CultureInfo.InvariantCulture, $"cmd {command:X8} {start}"));

        public void SetHoldRun(bool on) => Calls.Add($"run {on}");

        public void SetHoldSidestep(bool on) => Calls.Add($"side {on}");

        public void ToggleAutoRun() => Calls.Add("autorun");

        public void CommenceJump(double now) => Calls.Add("commence");

        public void DoJump(double now) => Calls.Add("dojump");
    }
}

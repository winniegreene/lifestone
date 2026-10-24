using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The re-buff driver's rig (M4 4.1 lane a, <see cref="RebuffDriver"/>): the scripted mage of <see cref="ScriptedCaster"/> over the real read model, clock, bus and Action API, a <see cref="BuffCycle"/> built as
/// <c>BuffCycleTests</c> builds it and a driver over it. One <see cref="Frame"/> is the clock advancing, the Action API sending, the scripted ACE answering, the Action API completing and the driver ticking.
/// </summary>
internal sealed class RebuffRig
{
    /// <summary>The margin the rig's options use unless a test gives its own: short, so the cycle's casts (a few tens of seconds apart at most) put only the earliest lines inside it.</summary>
    public const double Margin = 10;

    private byte _healthSequence;

    private ushort _teleport = 1;

    public RebuffRig(BuffOptions? options = null, bool magic = true, bool auto = true, bool present = true)
    {
        R = new WorldActionRig(present);
        Caster = new ScriptedCaster(R, 426, 275);
        Options = (options ?? new BuffOptions { RebuffMarginSeconds = Margin }) with { Auto = auto };
        View = new RebuffView(R);
        Cycle = new BuffCycle(R.Api, R.H.State, Caster.Table, Options, R.H.Log);
        Driver = new RebuffDriver(Cycle, R.Api, View, Options, R.H.Log);
        if (!magic)
        {
            R.CombatModeIs(CombatModes.NonCombat);
        }
    }

    /// <summary>The session view over the rig: the real read model, bus, clock and Action API.</summary>
    private sealed class RebuffView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    public WorldActionRig R { get; }

    public ScriptedCaster Caster { get; }

    public BuffOptions Options { get; }

    public IGameSessionView View { get; }

    public BuffCycle Cycle { get; }

    public RebuffDriver Driver { get; }

    public double Now => R.H.R.Clock.Now;

    /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers, the Action API completes, then the driver ticks.</summary>
    public void Frame(double dt = 0.25)
    {
        R.Tick(dt);
        Caster.Pump();
        R.Tick();
        Driver.Tick(Now);
    }

    /// <summary>Frames until <paramref name="done"/> holds or <paramref name="maxSeconds"/> of session time have passed; false when the time ran out.</summary>
    public bool RunUntil(Func<bool> done, double maxSeconds = 600, double dt = 0.25)
    {
        var end = Now + maxSeconds;
        while (Now < end)
        {
            if (done())
            {
                return true;
            }

            Frame(dt);
        }

        return done();
    }

    /// <summary>Frames for <paramref name="seconds"/> of session time.</summary>
    public void Advance(double seconds, double dt = 0.25)
    {
        var end = Now + seconds;
        while (Now < end)
        {
            Frame(dt);
        }
    }

    /// <summary>The first cycle by hand (the driver is not ticked): every line cast, so the schedule has expiry times to work from. Returns the cycle's status at the end.</summary>
    public BuffStatus BuildBuffs(double maxSeconds = 4000)
    {
        Assert.True(Cycle.Start(Now));
        var end = Now + maxSeconds;
        var status = BuffStatus.Running;
        while (status == BuffStatus.Running && Now < end)
        {
            R.Tick(0.25);
            Caster.Pump();
            R.Tick();
            status = Cycle.Step(Now);
        }

        return status;
    }

    /// <summary>Moves the session clock so the soonest line has <paramref name="remaining"/> seconds left (one big tick: the driver is not ticked, so no check runs inside it).</summary>
    public void SkipTo(double remaining)
    {
        var left = Cycle.MinRemainingSeconds(Now);
        Assert.False(double.IsNaN(left));
        R.Tick(left - remaining);
    }

    /// <summary>The player's current health becomes <paramref name="health"/> (ACE's 0x02E9 for health, key 2).</summary>
    public void Health(uint health) => R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_healthSequence, 2, health));

    /// <summary>A defender notice (GameEvent 0x01B2): something hit the player for 12.</summary>
    public void Hit() => R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Attacker", 4, 0.1, 12, 4, false, 0));

    /// <summary>The player is teleporting (ACE's F751 PlayerTeleport).</summary>
    public void Teleport() => R.H.R.Reducer.OnPlayerTeleport(R.H.R.Context, new PlayerTeleportMsg(++_teleport));

    /// <summary>The log lines of the driver (<c>[buff] re-buff driver: ...</c>).</summary>
    public List<string> DriverLines() => R.H.Log.Lines.Where(l => l.Contains("re-buff driver", StringComparison.Ordinal)).ToList();
}

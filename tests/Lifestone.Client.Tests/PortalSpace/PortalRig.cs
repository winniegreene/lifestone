using Lifestone.Client.Audio;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>A tunnel whose frame the test sets, counting the machine's restarts and stops. Allocation-free.</summary>
internal sealed class FakeTunnel : IPortalTunnel
{
    /// <summary>The frame <see cref="CurrentFrame"/> reports while spinning.</summary>
    public int Frame { get; set; } = 1;

    public bool Spinning { get; private set; }

    public int Restarts { get; private set; }

    public int Stops { get; private set; }

    public double LastAdvance { get; private set; } = double.NaN;

    public int CurrentFrame => Spinning ? Frame : 0;

    public void Restart()
    {
        Spinning = true;
        Restarts++;
    }

    public void Stop()
    {
        Spinning = false;
        Stops++;
    }

    public void AdvanceTo(double now) => LastAdvance = now;

    public int DrainEvents(List<EffectEvent> into) => 0;
}

/// <summary>
/// A roll source that hands out scripted values in order (then falls back to the retail generator), recording each call's
/// range so a test can check retail's draw order.
/// </summary>
internal sealed class ScriptedRandom(params double[] values) : IPortalRandom
{
    private readonly Queue<double> _values = new(values);
    private readonly MsvcCrtRandom _fallback = new();

    public List<(double Min, double Max, double Value)> Calls { get; } = [];

    public double RandDouble(double min, double max)
    {
        var value = _values.Count > 0 ? _values.Dequeue() : _fallback.RandDouble(min, max);
        Calls.Add((min, max, value));
        return value;
    }
}

/// <summary>One frame a <see cref="PortalRig"/> stepped.</summary>
internal readonly record struct PortalSample(long Index, double Time, PortalFrame Frame);

/// <summary>
/// Drives a <see cref="PortalSpaceMachine"/> the way the director does (PS11's frame order): the machine's update, then the
/// teleport flags' tick with the destination's readiness, then the tunnel's advance while it shows. Time is
/// <see cref="Index"/> × <see cref="Dt"/>; the default step, 1/64 s, is exact in binary, so the 1 s and 2 s boundaries fall
/// on frames.
/// </summary>
internal sealed class PortalRig
{
    /// <summary>f_game of Lifestone's 60° main camera: √3.</summary>
    public static readonly float Sixty = PortalZoom.FocalFromVerticalFov(Math.PI / 3.0);

    public PortalRig(
        IPortalTunnel? tunnel = null,
        IPortalRandom? random = null,
        LogOffTunnelPolicy policy = LogOffTunnelPolicy.HoldUntilCharacterList,
        double dt = 1.0 / 64.0,
        bool record = true)
    {
        Tunnel = tunnel ?? new TimedTunnel();
        Machine = new PortalSpaceMachine(Tunnel, random ?? new MsvcCrtRandom(7), () => GameFocal, policy);
        Dt = dt;
        Record = record;
    }

    public PortalSpaceMachine Machine { get; }

    public IPortalTunnel Tunnel { get; }

    public TeleportProgress Progress { get; } = new();

    /// <summary>What <see cref="PortalSpaceMachine.GameFocal"/> returns.</summary>
    public float GameFocal { get; set; } = Sixty;

    /// <summary>The destination's readiness, passed to <see cref="TeleportProgress.Tick"/> after each update.</summary>
    public bool Ready { get; set; } = true;

    public double Dt { get; }

    public bool Record { get; }

    public long Index { get; private set; }

    public double Now => Index * Dt;

    public List<PortalSample> Samples { get; } = [];

    public PortalFrame Last { get; private set; }

    /// <summary>One frame at the next time step.</summary>
    public PortalFrame Step()
    {
        Index++;
        var now = Now;
        var frame = Machine.Update(now, Progress);
        Progress.Tick(Ready);
        if (frame.TunnelVisible)
        {
            Tunnel.AdvanceTo(now);
        }

        if (Record)
        {
            Samples.Add(new PortalSample(Index, now, frame));
        }

        Last = frame;
        return frame;
    }

    /// <summary>Steps until <paramref name="done"/> holds for a frame, and returns that frame; fails after <paramref name="maxSeconds"/>.</summary>
    public PortalFrame RunUntil(Func<PortalFrame, bool> done, double maxSeconds = 60.0)
    {
        var end = Now + maxSeconds;
        while (Now < end)
        {
            var frame = Step();
            if (done(frame))
            {
                return frame;
            }
        }

        throw new Xunit.Sdk.XunitException($"the condition did not hold within {maxSeconds} s (state {Machine.State} at {Now} s)");
    }

    /// <summary>Steps until the machine is in <paramref name="state"/>.</summary>
    public PortalFrame RunUntil(TeleportAnimState state, double maxSeconds = 60.0) =>
        RunUntil(f => f.State == state, maxSeconds);

    /// <summary>Steps for <paramref name="seconds"/>.</summary>
    public PortalFrame RunFor(double seconds)
    {
        var end = Now + seconds;
        var frame = Last;
        while (Now + (Dt / 2.0) < end)
        {
            frame = Step();
        }

        return frame;
    }

    /// <summary>The frames recorded with any of <paramref name="cues"/>.</summary>
    public IEnumerable<PortalSample> With(PortalCues cues) => Samples.Where(s => (s.Frame.Cues & cues) != 0);

    /// <summary>The first recorded frame in <paramref name="state"/>.</summary>
    public PortalSample First(TeleportAnimState state) => Samples.First(s => s.Frame.State == state);
}

using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The window frame times of the station that is running (M4 4.2 J1; DoD 15's <c>attack_frame_max_ms</c>, <c>loot_frame_max_ms</c> and <c>vendor_frame_max_ms</c>): what only an engine with a frame clock can measure. The Godot host
/// gives one (<see cref="JourneyWindowSamplers"/>, through <see cref="JourneyContext.WindowFrames"/>); the headless host gives none, so the station lines print <c>n/a</c> there. Each value is the slowest frame inside its window during
/// the current station, or null when no window was sampled.
/// </summary>
public interface IJourneyWindowFrames
{
    /// <summary>The slowest frame from an attack's send to its run's Done (<see cref="AttackFrameSampler"/>), or null.</summary>
    double? AttackFrameMaxMs { get; }

    /// <summary>The slowest frame from a corpse's open being sent to its close being settled (<see cref="LootFrameSampler"/> over <see cref="HuntScenario.CorpseWindow"/>), or null.</summary>
    double? LootFrameMaxMs { get; }

    /// <summary>The slowest frame from a vendor's open being sent to the visit's end (<see cref="VendorFrameSampler"/> over <see cref="HuntScenario.VendorWindow"/> or <see cref="VendorFactsRun.VendorWindow"/>), or null.</summary>
    double? VendorFrameMaxMs { get; }
}

/// <summary>
/// The three window samplers of the retired hunt leg (<c>HuntLegRunner</c>, M4 1.3 s2, 2.2 s0, 3.2 s4a) over the journey's stations (M4 4.2 J1): fresh samplers for each station run, the attack window opened and closed by the session's
/// own events, the loot and vendor windows read from the station's scenario after each step. The host calls <see cref="Frame"/> once per frame before the journey's step (the delta is the previous frame's whole time) and
/// <see cref="Track"/> after it. Engine-agnostic: no clock of its own, nothing allocated per frame (a station's start allocates its samplers).
/// </summary>
/// <remarks>
/// <para><b>The last frame of a window.</b> A station's line is written inside the journey's step that ends it, before the next <see cref="Frame"/>: a window still open in the station's last step (the vendor-facts visit to Renald,
/// which ends with the station) loses that step's frame from the line. Windows that close earlier (a corpse, an attack run, the vendor station's visit) are whole.</para>
/// <para><b>Judged elsewhere.</b> The station's own <c>ok</c> never reads these values: the parallel phase is contended, so smoke judges them (75 ms fails, over 50 flags, only uncontended: J2's <c>Get-JourneyVerdict</c>).</para>
/// </remarks>
public sealed class JourneyWindowSamplers : IJourneyWindowFrames, IDisposable
{
    private AttackFrameSampler _attack = new();
    private LootFrameSampler _loot = new();
    private VendorFrameSampler _vendor = new();
    private IDisposable? _attached;
    private IStationRun? _run;

    /// <inheritdoc />
    public double? AttackFrameMaxMs => _attack.AttackFrameMaxMs;

    /// <inheritdoc />
    public double? LootFrameMaxMs => _loot.LootFrameMaxMs;

    /// <inheritdoc />
    public double? VendorFrameMaxMs => _vendor.VendorFrameMaxMs;

    /// <summary>The current station's attack sampler (its window and frame counts, for the log).</summary>
    public AttackFrameSampler Attack => _attack;

    /// <summary>The current station's loot sampler.</summary>
    public LootFrameSampler Loot => _loot;

    /// <summary>The current station's vendor sampler.</summary>
    public VendorFrameSampler Vendor => _vendor;

    /// <summary>Takes one frame in each sampler (call before the journey's step).</summary>
    /// <param name="deltaSeconds">The engine's delta, in seconds: the previous frame's whole time.</param>
    /// <param name="skip">True for a frame to leave out (the one after a saved review frame: the read-back stalls it).</param>
    public void Frame(double deltaSeconds, bool skip)
    {
        _attack.Step(deltaSeconds, skip);
        _loot.Step(deltaSeconds, skip);
        _vendor.Step(deltaSeconds, skip);
    }

    /// <summary>
    /// Follows the journey's current station run (call after the journey's step): a new run gets fresh samplers (the attack one attached to <paramref name="session"/>'s events), and the loot and vendor windows follow the
    /// run's scenario (<see cref="HuntScenario.CorpseWindow"/>, <see cref="HuntScenario.VendorWindow"/>; <see cref="VendorFactsRun.VendorWindow"/>). No run (between stations) detaches.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="run">The journey's current station run (<c>JourneyScenario.CurrentRun</c>), or null.</param>
    public void Track(IGameSessionView session, IStationRun? run)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(run, _run))
        {
            Detach();
            _run = run;
            _attack = new AttackFrameSampler();
            _loot = new LootFrameSampler();
            _vendor = new VendorFrameSampler();
            if (run is not null)
            {
                _attached = _attack.Attach(session.Events);
            }
        }

        switch (run)
        {
            case IStationInner { Scenario: HuntScenario hunt }:
                _loot.Track(hunt.CorpseWindow); // after the step: the frame that sent the open does not count, the one that settled the close does (LootFrameSampler)
                _vendor.Track(hunt.VendorWindow);
                break;
            case VendorFactsRun facts:
                _vendor.Track(facts.VendorWindow);
                break;
        }
    }

    /// <summary>Detaches the attack sampler; the values stay readable.</summary>
    public void Dispose() => Detach();

    private void Detach()
    {
        _attached?.Dispose();
        _attached = null;
    }
}

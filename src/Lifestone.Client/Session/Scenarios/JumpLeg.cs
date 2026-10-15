using System.Globalization;
using Lifestone.Client.Movement;
using Lifestone.Data.Animation;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The jump leg of a hosted ring (m2 wave 3, dod-8-9-2): after the laps, on the straight stretch where the ring ended, a standing jump and then a running jump through the player's own
/// movement intents (the keys and the jump bar the person uses: <see cref="IPlayerMovementIntents.CommenceJump"/> and <see cref="IPlayerMovementIntents.DoJump"/>, which send MoveToState and
/// then the Jump action, <c>ClientCombatSystem::CommenceJump</c> 11.6096 0x0056BCD0 and <c>DoJump</c> 0x0056BE50). The caller asks the server where the player is afterwards and the audit counts
/// any force-position correction; this class only drives the keys and says whether each jump left the ground and came down.
/// </summary>
/// <remarks>
/// <para><b>Sequence</b> (seconds on the session clock): wait on the ground; a standing jump (charge <see cref="ChargeSeconds"/>, release); wait for the air and the landing; rest; run forward
/// <see cref="RunUpSeconds"/> with the run key; a running jump (charge, release with the forward key still down); wait for the landing; release the keys; rest so retail's one-second
/// cadence sends the resting position.</para>
/// <para>A jump that never leaves the ground or never lands within <see cref="AirSeconds"/> fails the leg with a message naming it. The run may be cut short by a wall (the ring's straight stretch is
/// long enough for the 1 s run-up, and a collision stop is not a failure here); the jump's physics belong to the player's controller.</para>
/// <para><b>Unconfirmed against ACE:</b> the Jump action's 56-byte pack (<c>WriteJump</c>) and ACE's answer to it have not run live yet; the first live run is the lead's (<c>TODO(jump-live)</c>). ACE answers a jump
/// with a vector update, not a position event, so the audit's unmatched count (positions the server did not echo) rises by one per jump; it is reported, not judged.</para>
/// </remarks>
public sealed class JumpLeg
{
    /// <summary>How long the jump key is held before release, in seconds (the bar fills in one second: half power).</summary>
    public const double ChargeSeconds = 0.5;

    /// <summary>The run-up before the running jump, in seconds.</summary>
    public const double RunUpSeconds = 1.0;

    /// <summary>The longest a jump may take to leave the ground (<see cref="AirSeconds"/> to come down), in seconds.</summary>
    public const double TakeOffSeconds = 2.0;

    /// <summary>The longest a jump may take to come down after leaving the ground, in seconds.</summary>
    public const double AirSeconds = 6.0;

    /// <summary>The rest on the ground between the steps, in seconds.</summary>
    public const double RestSeconds = 1.2;

    private enum Phase
    {
        Start,
        StandingCharge,
        StandingTakeOff,
        StandingAir,
        StandingRest,
        RunUp,
        RunningCharge,
        RunningTakeOff,
        RunningAir,
        EndRest,
        Done,
    }

    private readonly IPlayerMovementIntents _intents;
    private readonly bool _runByDefault;
    private Phase _phase = Phase.Start;
    private double _phaseStart = double.NaN;

    /// <summary>A jump leg over <paramref name="intents"/>.</summary>
    /// <param name="intents">The player's movement intents.</param>
    /// <param name="runByDefault">The player's run-by-default option (<see cref="MovementOptions.RunByDefault"/>): the run key is held only when it is not what the option already gives.</param>
    public JumpLeg(IPlayerMovementIntents intents, bool runByDefault)
    {
        ArgumentNullException.ThrowIfNull(intents);
        (_intents, _runByDefault) = (intents, runByDefault);
    }

    /// <summary>True when the leg ended, passed or failed.</summary>
    public bool IsDone => _phase == Phase.Done;

    /// <summary>Why the leg failed, or null while it has not.</summary>
    public string? Failure { get; private set; }

    /// <summary>Jumps released (a standing and a running one when the leg passes).</summary>
    public int Jumps { get; private set; }

    /// <summary>Jumps that left the ground and came down.</summary>
    public int Landed { get; private set; }

    /// <summary>The leg's phase, for a failure message and the log.</summary>
    public string PhaseName => _phase.ToString();

    /// <summary>One line: the jumps released, the landings and the phase.</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture, $"jumps={Jumps} landed={Landed} phase={_phase}{(Failure is null ? string.Empty : " failed")}");

    /// <summary>Steps the leg at <paramref name="now"/> (session clock seconds); call once per frame after the controller ticked. Does nothing once <see cref="IsDone"/>.</summary>
    public void Tick(double now)
    {
        if (_phase == Phase.Done)
        {
            return;
        }

        if (double.IsNaN(_phaseStart))
        {
            _phaseStart = now;
        }

        var elapsed = now - _phaseStart;
        switch (_phase)
        {
            case Phase.Start:
                if (_intents.OnGround && elapsed >= RestSeconds)
                {
                    _intents.CommenceJump(now);
                    Go(Phase.StandingCharge, now);
                }

                break;

            case Phase.StandingCharge:
                if (elapsed >= ChargeSeconds)
                {
                    Release(now, Phase.StandingTakeOff);
                }

                break;

            case Phase.StandingTakeOff:
            case Phase.RunningTakeOff:
                TakeOff(now, elapsed, _phase == Phase.StandingTakeOff ? Phase.StandingAir : Phase.RunningAir, _phase == Phase.StandingTakeOff ? "standing" : "running");
                break;

            case Phase.StandingAir:
            case Phase.RunningAir:
                Land(now, elapsed, _phase == Phase.StandingAir ? Phase.StandingRest : Phase.EndRest, _phase == Phase.StandingAir ? "standing" : "running");
                break;

            case Phase.StandingRest:
                if (_intents.OnGround && elapsed >= RestSeconds)
                {
                    SetForward(true);
                    Go(Phase.RunUp, now);
                }

                break;

            case Phase.RunUp:
                if (elapsed >= RunUpSeconds)
                {
                    _intents.CommenceJump(now);
                    Go(Phase.RunningCharge, now);
                }

                break;

            case Phase.RunningCharge:
                if (elapsed >= ChargeSeconds)
                {
                    Release(now, Phase.RunningTakeOff);
                }

                break;

            case Phase.EndRest:
                if (_intents.OnGround && elapsed >= RestSeconds)
                {
                    Go(Phase.Done, now);
                }

                break;
        }
    }

    private void Release(double now, Phase next)
    {
        _intents.DoJump(now);
        Jumps++;
        Go(next, now);
    }

    private void TakeOff(double now, double elapsed, Phase next, string which)
    {
        if (!_intents.OnGround)
        {
            Go(next, now);
            return;
        }

        if (elapsed >= TakeOffSeconds)
        {
            Fail(now, $"the {which} jump did not leave the ground within {TakeOffSeconds:0.#} s");
        }
    }

    private void Land(double now, double elapsed, Phase next, string which)
    {
        if (_intents.OnGround)
        {
            Landed++;
            if (next == Phase.EndRest)
            {
                SetForward(false);
            }

            Go(next, now);
            return;
        }

        if (elapsed >= AirSeconds)
        {
            Fail(now, $"the {which} jump did not come down within {AirSeconds:0.#} s");
        }
    }

    private bool _forward;

    private void SetForward(bool forward)
    {
        if (forward == _forward)
        {
            return;
        }

        _forward = forward;
        if (forward)
        {
            _intents.HandleCommand(MotionCommands.WalkForward, true);
            _intents.SetHoldRun(!_runByDefault);
        }
        else
        {
            _intents.SetHoldRun(false);
            _intents.HandleCommand(MotionCommands.WalkForward, false);
        }
    }

    private void Fail(double now, string why)
    {
        Failure = $"{why} ({Describe()})";
        SetForward(false);
        Go(Phase.Done, now);
    }

    private void Go(Phase phase, double now)
    {
        _phase = phase;
        _phaseStart = now;
    }
}

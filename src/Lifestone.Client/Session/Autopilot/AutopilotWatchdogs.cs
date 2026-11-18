using System.Globalization;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The autopilot's per-state watchdogs (M5 item 4 a2, D57; automation-vision section 1.4 K). Each state has a bound, in seconds of the session clock since the state was entered (the fight's: since its last kill, since a fight that
/// is working never ends); a state over its bound is a trip, which the autopilot logs by name (<c>watchdog: &lt;state&gt; over &lt;n&gt; s</c>) and recovers by the recall home and the known state, at most
/// <see cref="MaxRecoveries"/> times a cycle, and then stops by name (<c>stop=watchdog:&lt;state&gt;</c>, ok=no). The bounds are Lifestone's own (no retail rule): the stations' own limits, with room, and the scale
/// (<c>--autopilot-watchdog-scale</c>) is the one knob a person changes. Pure: no log, no session.
/// </summary>
public sealed class AutopilotWatchdogs
{
    /// <summary>The recoveries a cycle may make before the next trip stops the run (Lifestone's own; <c>--autopilot-watchdog-recoveries</c>).</summary>
    public const int DefaultMaxRecoveries = 2;

    /// <summary>
    /// The margin added to the ground's longest respawn (delay plus tick) for the fight's no-kill bound (Lifestone's own, M5 4 F1, 4-R7): the fight may wait a whole respawn for the next creature, and then the mana waits (the stamina-to-mana
    /// dance takes up to 300 s) and the walk across the storeys, before it counts as stuck.
    /// </summary>
    public const double FightMarginSeconds = 600;

    private AutopilotState _state = AutopilotState.KnownState;
    private double _since = double.NaN;
    private int _recoveries;
    private readonly double _fightBase;
    private readonly double _passBase;

    /// <summary>Watchdogs with the bounds scaled by <paramref name="scale"/>.</summary>
    /// <param name="scale">The factor on every base bound (greater than 0; 1 is the default).</param>
    /// <param name="maxRecoveries">The recoveries a cycle may make (0 or more).</param>
    /// <param name="fightBaseSeconds">The fight's base bound without a kill (default <see cref="BaseSeconds"/>'s 900; the scenario passes <see cref="FightBaseSeconds"/> of its ground).</param>
    /// <param name="passBaseSeconds">The base bound of a buff state on a climber's run (<see cref="ClimberPassSeconds"/>); 0 keeps <see cref="BaseSeconds"/>.</param>
    public AutopilotWatchdogs(double scale = 1, int maxRecoveries = DefaultMaxRecoveries, double fightBaseSeconds = 0, double passBaseSeconds = 0)
    {
        if (!(scale > 0) || !double.IsFinite(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "the watchdog scale must be a positive number");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maxRecoveries);
        if (!(fightBaseSeconds >= 0) || !double.IsFinite(fightBaseSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(fightBaseSeconds), fightBaseSeconds, "the fight's base bound must be 0 (the default) or a positive number");
        }

        if (!(passBaseSeconds >= 0) || !double.IsFinite(passBaseSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(passBaseSeconds), passBaseSeconds, "the pass's base bound must be 0 (the default) or a positive number");
        }

        _fightBase = fightBaseSeconds > 0 ? fightBaseSeconds : BaseSeconds(AutopilotState.Fight);
        _passBase = passBaseSeconds;
        Scale = scale;
        MaxRecoveries = maxRecoveries;
    }

    /// <summary>The factor on every base bound.</summary>
    public double Scale { get; }

    /// <summary>The recoveries a cycle may make.</summary>
    public int MaxRecoveries { get; }

    /// <summary>The recoveries made in the cycle in progress.</summary>
    public int Recoveries => _recoveries;

    /// <summary>
    /// The base bound of <paramref name="state"/>, in seconds (Lifestone's own, about twice the stations' own limits): the known state 90 (it waits 60 s for the player to be present), the buff 300 (the station's limit is 200), the travel 150
    /// (60 and 30), the fight 900 without a kill (the default for a ground that lists no generator; the scenario passes <see cref="FightBaseSeconds"/> of its ground instead, M5 4 F1), the upkeep 300, the trip 900 without progress (its stations' limits are 2,100 s and 3,000 s: the body moving 10 m is the trip's progress, M6 4 F1, 4-R7), the death 900 (280 and the rearm's shopping); the stopped state has none (0).
    /// </summary>
    /// <param name="state">The state.</param>
    public static double BaseSeconds(AutopilotState state) => state switch
    {
        AutopilotState.KnownState => 90,
        AutopilotState.Buff => 300,
        AutopilotState.Travel => 150,
        AutopilotState.Fight => 900,
        AutopilotState.Upkeep => 300,
        AutopilotState.Trip => 900,
        AutopilotState.Death => 900,
        _ => 0,
    };

    /// <summary>
    /// The fight's base bound without a kill for <paramref name="ground"/> (M5 4 F1, 4-R7; D46: read from the profile, never a den constant): the longest respawn of its generators (a slot's delay after a death plus the generator's tick) plus
    /// <see cref="FightMarginSeconds"/>. A ground that lists no generator keeps the fixed <see cref="BaseSeconds"/> for the fight.
    /// </summary>
    /// <param name="ground">The ground.</param>
    public static double FightBaseSeconds(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var longest = 0.0;
        foreach (var generator in ground.Respawn.Generators)
        {
            longest = Math.Max(longest, generator.DelaySeconds + generator.TickSeconds);
        }

        return longest > 0 ? longest + FightMarginSeconds : BaseSeconds(AutopilotState.Fight);
    }

    /// <summary>The bound of <paramref name="state"/> with the scale applied (0: none).</summary>
    /// <param name="state">The state.</param>
    public double BoundSeconds(AutopilotState state) => (state == AutopilotState.Fight ? _fightBase : _passBase > 0 && state is AutopilotState.Buff or AutopilotState.Upkeep ? _passBase : BaseSeconds(state)) * Scale;

    /// <summary>
    /// M6 7 AO: a climber's pass waits for mana up to the ladder's named limit (<see cref="LadderVitals.BuffPassWaitSeconds"/>, 900 s) and then ends done (M6 7 AN), so the buff state's bound is that limit plus its own base: 300 s alone would cut the first whole pass (about 650 mana on a pool of 165, the AK note) every time.
    /// </summary>
    /// <param name="vitals">The ladder's vitals data.</param>
    public static double ClimberPassSeconds(LadderVitals vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        return vitals.BuffPassWaitSeconds + BaseSeconds(AutopilotState.Buff);
    }

    /// <summary>
    /// A state was entered but its station has not started (the keeper's vitals pass and the peace come first, each with its own bound: 420 s and 15 s): the state's clock does not run until <see cref="Enter"/> is called again when the
    /// station starts (4-R7).
    /// </summary>
    /// <param name="state">The state.</param>
    public void Hold(AutopilotState state)
    {
        _state = state;
        _since = double.NaN;
    }

    /// <summary>A new state was entered at <paramref name="now"/>, or its station started: its clock starts.</summary>
    /// <param name="state">The state entered.</param>
    /// <param name="now">The session clock.</param>
    public void Enter(AutopilotState state, double now)
    {
        _state = state;
        _since = now;
    }

    /// <summary>The state made progress (a kill): its clock starts again.</summary>
    /// <param name="now">The session clock.</param>
    public void Progress(double now) => _since = now;

    /// <summary>A cycle ended: the recoveries count starts again.</summary>
    public void NewCycle() => _recoveries = 0;

    /// <summary>Takes the cycle's recoveries from the scenario a re-seat replaced.</summary>
    /// <param name="recoveries">The recoveries so far this cycle.</param>
    public void RestoreRecoveries(int recoveries) => _recoveries = recoveries;

    /// <summary>The state is held (a reconnect is under way): its clock starts again, the recoveries are kept.</summary>
    /// <param name="now">The session clock.</param>
    public void Pause(double now) => _since = now;

    /// <summary>The seconds the state has been over its bound at <paramref name="now"/>, or null when it is within (or has none).</summary>
    /// <param name="now">The session clock.</param>
    public double? Over(double now)
    {
        var bound = BoundSeconds(_state);
        if (bound <= 0 || double.IsNaN(_since))
        {
            return null;
        }

        var held = now - _since;
        return held > bound ? held : null;
    }

    /// <summary>
    /// A trip was recovered: counts it. True when it may be recovered (this is not past <see cref="MaxRecoveries"/>), false when the run must stop by name.
    /// </summary>
    public bool TryRecover()
    {
        if (_recoveries >= MaxRecoveries)
        {
            return false;
        }

        _recoveries++;
        return true;
    }

    /// <summary>The state's trip line, <c>watchdog: &lt;state&gt; over &lt;n&gt; s</c> (n: the whole seconds held).</summary>
    /// <param name="state">The state.</param>
    /// <param name="seconds">The seconds held.</param>
    public static string Line(AutopilotState state, double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"watchdog: {AutopilotScenario.StateName(state)} over {seconds:0} s");
}

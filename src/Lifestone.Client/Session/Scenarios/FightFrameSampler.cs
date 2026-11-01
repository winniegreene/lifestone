using Lifestone.Client.Avatars;
using Lifestone.Client.Interiors;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The fight leg's frame measures (<c>--login-check=bsd-fight</c>, M3 3.1 fd; plan section 5, "The fight's frames"): one call per frame, no allocation, no clock of its own (the engine's delta, which is the
/// previous frame's whole time, is the frame time). The three fields are report-only in 3.1 (a new judge is report-only in its first gate).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><c>cast_frame_max_ms</c></b>: the slowest frame while the cast window is open (<c>AvatarSet.CastWindowOpen</c>), the buff top-up's casts included.</item>
/// <item><b><c>fight_frame_max_ms</c></b>: the slowest frame of the fight's stage while the cast window or the projectile window (<c>AvatarSet.ProjectileWindowOpen</c>) is open or the loop has a target engaged.</item>
/// <item><b><c>bsd_min_fps</c></b>: the lowest frames per second of any one-second window of frames spent in the den (a window closes when its frames add up to a second; the window then in progress when the leg
/// ends, and any window a frame flagged as skipped fell in, is not counted). Frames outside the den are not in a window, and a frame that leaves the den drops the window it was in.</item>
/// </list>
/// A frame flagged <c>skip</c> (the one after a review frame was saved: the read-back stalls it) is left out of all three, as the buff leg leaves it out of <c>cast_frame_max_ms</c>.
/// <para><b>The flags are the previous call's.</b> The delta is the previous frame's whole time, and the flags are read after this frame's events were applied (the runners step after play mode), so each delta
/// is judged against the flags the call before it brought (package 2 review, finding 11): the frame that applied a projectile's F747 or opened the cast window counts in that window, the frame before it does not,
/// and the first call's delta (the frame before the first sample) is judged against nothing open, outside the fight and the den. <c>skip</c> names this call's delta and is read as given.</para>
/// </remarks>
public sealed class FightFrameSampler
{
    /// <summary>The length of one fps window, in seconds.</summary>
    public const double WindowSeconds = 1.0;

    private bool _castWindowOpen;
    private bool _projectileWindowOpen;
    private bool _targetEngaged;
    private bool _fighting;
    private bool _inDen;
    private double _castMaxMs = double.NaN;
    private double _fightMaxMs = double.NaN;
    private double _minFps = double.NaN;
    private double _windowSeconds;
    private int _windowFrames;
    private long _frames;
    private long _denFrames;
    private long _windows;

    /// <summary>The slowest frame while a cast window was open, in milliseconds, or null when none was.</summary>
    public double? CastFrameMaxMs => double.IsNaN(_castMaxMs) ? null : _castMaxMs;

    /// <summary>The slowest frame while a cast or projectile window was open or a target was engaged, in milliseconds, or null when none was.</summary>
    public double? FightFrameMaxMs => double.IsNaN(_fightMaxMs) ? null : _fightMaxMs;

    /// <summary>The lowest frames per second of a whole one-second window in the den, or null when no window completed.</summary>
    public double? BsdMinFps => double.IsNaN(_minFps) ? null : _minFps;

    /// <summary>The frames sampled (not counting the skipped ones).</summary>
    public long Frames => _frames;

    /// <summary>The sampled frames spent in the den.</summary>
    public long DenFrames => _denFrames;

    /// <summary>The fps windows that completed.</summary>
    public long Windows => _windows;

    /// <summary>Takes one frame. The five flags are read as they stand after this frame's events; they judge the next call's delta (the remarks).</summary>
    /// <param name="deltaSeconds">The engine's delta, in seconds: the previous frame's whole time.</param>
    /// <param name="castWindowOpen">The cast window is open.</param>
    /// <param name="projectileWindowOpen">A projectile is alive (the projectile window is open).</param>
    /// <param name="targetEngaged">The loop has a target (or is waiting on a bolt).</param>
    /// <param name="fighting">The scenario is in its fight stage (the top-up's casts and the walk to the den are not the fight's frames).</param>
    /// <param name="inDen">The player is present in the Black Spawn Den.</param>
    /// <param name="skip">True for a frame to leave out (the one after a saved review frame): this call's delta.</param>
    public void Step(double deltaSeconds, bool castWindowOpen, bool projectileWindowOpen, bool targetEngaged, bool fighting, bool inDen, bool skip)
    {
        // The delta is the previous frame's: it is judged against the flags of the call before this one, and this call's flags are kept for the next (value tuples: no allocation).
        var (castOpen, projectileOpen, engaged, inFight, den) = (_castWindowOpen, _projectileWindowOpen, _targetEngaged, _fighting, _inDen);
        (_castWindowOpen, _projectileWindowOpen, _targetEngaged, _fighting, _inDen) = (castWindowOpen, projectileWindowOpen, targetEngaged, fighting, inDen);
        if (skip || deltaSeconds <= 0 || double.IsNaN(deltaSeconds))
        {
            _windowSeconds = 0;
            _windowFrames = 0; // a stalled frame spoils the window it fell in
            return;
        }

        _frames++;
        var ms = deltaSeconds * 1000.0;
        if (castOpen && (double.IsNaN(_castMaxMs) || ms > _castMaxMs))
        {
            _castMaxMs = ms;
        }

        if (inFight && (castOpen || projectileOpen || engaged) && (double.IsNaN(_fightMaxMs) || ms > _fightMaxMs))
        {
            _fightMaxMs = ms;
        }

        if (!den)
        {
            _windowSeconds = 0;
            _windowFrames = 0;
            return;
        }

        _denFrames++;
        _windowSeconds += deltaSeconds;
        _windowFrames++;
        if (_windowSeconds >= WindowSeconds)
        {
            var fps = _windowFrames / _windowSeconds;
            _windows++;
            if (double.IsNaN(_minFps) || fps < _minFps)
            {
                _minFps = fps;
            }

            _windowSeconds = 0;
            _windowFrames = 0;
        }
    }
}

/// <summary>
/// The fight leg's three review frames (<c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c>; M3 3.1 fd, triggers reworked in fix B after the live run's first frames caught a bolt hitting a wall):
/// <c>bolt</c> (the first projectile created AFTER the first damage line, when the target is in sight and a cast lands, plus <see cref="DelaysSeconds"/>[0]), <c>hit</c> (the Explode of a projectile whose damage line
/// comes within <see cref="PairSeconds"/> of it, either order, plus 0.1 s from that Explode: an Explode on a wall or a miss has no damage line, so it never triggers the frame) and <c>kill</c> (the 0x01AD kill notice plus
/// 0.5 s, the chat line visible). The hit is paired by time, not by guid: the plan sees counters, not projectiles. Each slot is triggered once; a slot whose trigger never came is reported by name in
/// <see cref="Note"/> and never faked. The plan is pure (the caller reads the counters and saves the frames), allocates nothing per frame, and says whether the scenario is to be held (<see cref="Owed"/>: at most
/// <see cref="HoldSeconds"/> after a trigger).
/// </summary>
public sealed class FightShotPlan
{
    /// <summary>The slots' names, in the order of the files <c>&lt;stem&gt;-bolt.png</c>, <c>-hit.png</c> and <c>-kill.png</c>.</summary>
    public static readonly string[] Names = ["bolt", "hit", "kill", "viewer", "first"];

    /// <summary>The seconds after each trigger at which the frame is taken (the bolt in flight, the hit, the kill line).</summary>
    public static readonly double[] DelaysSeconds = [0.15, 0.1, 0.5, 0.0, 0.15];

    /// <summary>How long, in seconds, no projectile must be alive (in the den) before the <c>viewer</c> frame is due (M3 4.1 lane c: the viewer light on its own, no bolt to outshine it).</summary>
    public const double QuietSeconds = 1.0;

    /// <summary>How long, in seconds, the player must also have been still (M3 4.1 fix c: the frame is the mage standing, not walking).</summary>
    public const double StillSeconds = 0.5;

    /// <summary>What triggers each slot, for the log (same order as <see cref="Names"/>).</summary>
    public static readonly string[] Triggers = ["first projectile created after the first damage line", "projectile Explode paired with a damage line", "0x01AD kill notice", "the walk begun, no projectile alive for 1 s and the player still for 0.5 s in the den", "the first projectile created (M3 4.2 P4: the first bolt, whatever it hits, with the target in view)"];

    /// <summary>The longest, in seconds, the scenario is held for a frame after its trigger; past it the frame is given up.</summary>
    public const double HoldSeconds = 3.0;

    /// <summary>The longest, in seconds, between a projectile's Explode and a damage line for the two to count as one hit (either order).</summary>
    public const double PairSeconds = 1.0;

    /// <summary>The frames the developer HUD stays hidden before a frame is saved (so the first hidden frame is not the saved one).</summary>
    public const int HiddenFramesBeforeShot = 2;

    private enum State
    {
        Waiting,
        Pending,
        Taken,
        GivenUp,
    }

    private readonly State[] _state = new State[Names.Length];
    private readonly double[] _triggeredAt = new double[Names.Length];
    private readonly int[] _frames = new int[Names.Length];
    private readonly string?[] _why = new string?[Names.Length];
    private long _createdBase;
    private long _explodesBase;
    private int _damageBase;
    private int _killsBase;
    private bool _begun;
    private bool _damageSeen;
    private long _createdMark;
    private double _lastExplodeAt = double.NegativeInfinity;
    private double _lastDamageAt = double.NegativeInfinity;
    private double _quietSince = double.NaN;
    private double _stillSince = double.NaN;

    /// <summary>
    /// F3: with <c>--fight-storeys-min</c> 2 or more the slots <c>first</c>, <c>bolt</c>, <c>hit</c> and <c>kill</c> wait for their trigger on a storey other than the arrival's (the first kill is on the arrival floor, so the owner would have no frame of a lower storey);
    /// <see cref="Step"/> then reads its <c>offArrivalStorey</c> argument, and the counters are re-based on every frame the player stands on the arrival's storey, so a trigger is an event of the other storey. The <c>viewer</c> slot is unchanged. Default false: nothing changes.
    /// </summary>
    public bool WaitsForOtherStorey { get; set; }

    /// <summary>Starts watching: the counters now are the bases a rise above is measured from.</summary>
    /// <param name="projectilesCreated">The avatar set's <c>ProjectilesCreated</c> now.</param>
    /// <param name="projectileExplodes">The avatar set's <c>ProjectileExplodes</c> now.</param>
    /// <param name="damageLines">The tally's <c>DamageLinesDealt</c> now.</param>
    /// <param name="kills">The tally's kills now.</param>
    public void Begin(long projectilesCreated, long projectileExplodes, int damageLines, int kills)
    {
        (_createdBase, _explodesBase, _damageBase, _killsBase) = (projectilesCreated, projectileExplodes, damageLines, kills);
        _createdMark = projectilesCreated;
        _begun = true;
    }

    /// <summary>True from the first trigger to the last frame taken or given up: the scenario waits (and the developer HUD stays hidden).</summary>
    public bool Owed
    {
        get
        {
            for (var i = 0; i < _state.Length; i++)
            {
                if (_state[i] == State.Pending)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The frames taken.</summary>
    public int Taken
    {
        get
        {
            var n = 0;
            for (var i = 0; i < _state.Length; i++)
            {
                n += _state[i] == State.Taken ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>One frame: notes the triggers the counters show and gives the slot to save now.</summary>
    /// <param name="now">The leg's clock, in seconds.</param>
    /// <param name="projectilesCreated">The avatar set's <c>ProjectilesCreated</c>.</param>
    /// <param name="projectileExplodes">The avatar set's <c>ProjectileExplodes</c>.</param>
    /// <param name="damageLines">The tally's <c>DamageLinesDealt</c>.</param>
    /// <param name="kills">The tally's kills.</param>
    /// <param name="projectileAlive">A projectile is alive (the avatar set's projectile window is open); default false.</param>
    /// <param name="inDen">The player is present in the Black Spawn Den; default false, which never triggers the <c>viewer</c> slot.</param>
    /// <param name="walked">The fight scenario has started its walk on the den's floor (not the arrival's first frames); default false, which never triggers the <c>viewer</c> slot.</param>
    /// <param name="moving">The player moved this frame (it stood still for <see cref="StillSeconds"/> before the <c>viewer</c> frame is due).</param>
    /// <param name="offArrivalStorey">The player stands on a storey other than the arrival's; read only when <see cref="WaitsForOtherStorey"/> is set; default true.</param>
    /// <returns>The slot (0 bolt, 1 hit, 2 kill, 3 viewer, 4 first) whose frame is due now, or -1. At most one a frame (a save stalls the frame after it).</returns>
    public int Step(double now, long projectilesCreated, long projectileExplodes, int damageLines, int kills, bool projectileAlive = false, bool inDen = false, bool walked = false, bool moving = false, bool offArrivalStorey = true)
    {
        if (!_begun)
        {
            return -1;
        }

        if (WaitsForOtherStorey && !offArrivalStorey)
        {
            // Still on the arrival's storey: what happens here triggers no frame of these four slots (the counters are the bases a rise on the other storey is measured from).
            (_createdBase, _createdMark, _explodesBase, _damageBase, _killsBase) = (projectilesCreated, projectilesCreated, projectileExplodes, damageLines, kills);
            (_damageSeen, _lastExplodeAt, _lastDamageAt) = (false, double.NegativeInfinity, double.NegativeInfinity);
        }

        // The first damage line says a cast landed with the target in sight: a projectile created after it is the bolt to show.
        var damaged = damageLines > _damageBase;
        if (damaged && !_damageSeen)
        {
            _damageSeen = true;
            _createdMark = projectilesCreated;
        }

        Trigger(0, _damageSeen && projectilesCreated > _createdMark, now);

        // The hit: an Explode and a damage line within PairSeconds of each other, either first. The Explode's time is the trigger.
        var explodedNow = projectileExplodes > _explodesBase;
        if (explodedNow)
        {
            _explodesBase = projectileExplodes;
            _lastExplodeAt = now;
            if (now - _lastDamageAt <= PairSeconds)
            {
                Trigger(1, true, now);
            }
        }

        if (damaged && damageLines > _damageBase)
        {
            _damageBase = damageLines;
            _lastDamageAt = now;
            if (now - _lastExplodeAt <= PairSeconds)
            {
                Trigger(1, true, _lastExplodeAt);
            }
        }

        Trigger(2, kills > _killsBase, now);
        Trigger(4, projectilesCreated > _createdBase, now);

        // The viewer frame: the player in the den with no projectile alive for QuietSeconds (the moment the quiet began is not the trigger; the frame is due when the quiet has lasted that long).
        if (!inDen || !walked || projectileAlive)
        {
            _quietSince = double.NaN;
        }
        else if (double.IsNaN(_quietSince))
        {
            _quietSince = now;
        }

        if (moving || !inDen || !walked)
        {
            _stillSince = double.NaN;
        }
        else if (double.IsNaN(_stillSince))
        {
            _stillSince = now;
        }

        Trigger(3, !double.IsNaN(_quietSince) && now - _quietSince >= QuietSeconds && !double.IsNaN(_stillSince) && now - _stillSince >= StillSeconds, now);
        var due = -1;
        for (var i = 0; i < _state.Length; i++)
        {
            if (_state[i] != State.Pending)
            {
                continue;
            }

            _frames[i]++;
            if (now - _triggeredAt[i] > HoldSeconds)
            {
                _state[i] = State.GivenUp;
                _why[i] = "not taken within the hold after its trigger";
                continue;
            }

            if (due < 0 && now >= _triggeredAt[i] + DelaysSeconds[i] && _frames[i] >= HiddenFramesBeforeShot)
            {
                due = i;
            }
        }

        return due;
    }

    /// <summary>The frame of <paramref name="slot"/> was saved.</summary>
    /// <param name="slot">The slot.</param>
    public void MarkTaken(int slot) => _state[slot] = State.Taken;

    /// <summary>The frame of <paramref name="slot"/> could not be saved.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="why">Why.</param>
    public void MarkFailed(int slot, string why)
    {
        _state[slot] = State.GivenUp;
        _why[slot] = why;
    }

    /// <summary>
    /// What the plan ended with, one phrase per slot (<c>bolt=taken hit=taken kill=trigger never came</c>): a slot never triggered is named as such, never faked.
    /// </summary>
    public string Note()
    {
        var parts = new string[_state.Length];
        for (var i = 0; i < _state.Length; i++)
        {
            var phrase = _state[i] switch
            {
                State.Taken => "taken",
                State.Waiting => WaitsForOtherStorey ? "trigger never came (waited for a storey other than the arrival's)" : "trigger never came",
                State.Pending => "trigger came, frame not taken",
                _ => "not taken (" + _why[i] + ")",
            };
            parts[i] = Names[i] + "=" + phrase;
        }

        return string.Join(' ', parts);
    }

    /// <summary>The path of the frame of <paramref name="slot"/>: the asked path's stem, <c>-</c> and the slot's name, and the <c>.png</c> extension.</summary>
    /// <param name="path">The asked path (<c>--play-screenshot</c>).</param>
    /// <param name="slot">The slot.</param>
    public static string ShotPath(string path, int slot)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = System.IO.Path.GetDirectoryName(path);
        var name = System.IO.Path.GetFileNameWithoutExtension(path) + "-" + Names[slot] + ".png";
        return string.IsNullOrEmpty(directory) ? name : System.IO.Path.Combine(directory, name);
    }

    private void Trigger(int slot, bool seen, double now)
    {
        if (seen && _state[slot] == State.Waiting)
        {
            _state[slot] = State.Pending;
            _triggeredAt[slot] = now;
        }
    }
}

/// <summary>
/// The fight leg's light measures (<c>--login-check=bsd-fight</c>, M3 4.1 lane c; report-only in the live leg's first gate, judged offline where the stand-in's fight is deterministic): read from the frame the
/// avatar set filled (<see cref="DynamicLightFrame"/>), never assumed. One call per frame, after <c>AvatarSet.Advance</c>; no allocation (fixed slots, index loops).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><c>bolt_light</c></b>: <c>yes</c> when every projectile that flew long enough to judge (<see cref="MinFlightFrames"/> frames in the avatar set) had its own entry (owner = its guid) in the frame for at least
/// <see cref="LitPercent"/> percent of the frames it was alive (integer maths: 89 of 100 fails, 90 of 100 holds). The flight is the frames the projectile is in the avatar set from its creation until its <c>LightingOn</c> state falls after having been set (the Explode clears it) or it is deleted. A projectile whose state never had <c>LightingOn</c> is counted in <see cref="StateOffFlights"/> and not judged. A flight shorter than <see cref="MinFlightFrames"/> is counted in <see cref="ShortFlights"/> and not judged (TODO(m3-bolt-light-short-flight): two frames cannot show a percentage). No projectile judged reads <c>no</c>.</item>
/// <item><b><c>dyn_lights_max</c></b>: the most lights in one frame.</item>
/// <item><b><c>viewer_light</c></b>: <c>on</c> when the last frame sampled held the viewer light (owner 0).</item>
/// </list>
/// </remarks>
public sealed class LightFrameSampler
{
    /// <summary>The share of a projectile's frames its light must be in the frame, in percent (4.1: at least 90%).</summary>
    public const int LitPercent = 90;

    /// <summary>The fewest frames a flight must last to be judged.</summary>
    public const int MinFlightFrames = 3;

    /// <summary>The projectiles tracked at once (a bolt per cast; a fight has a handful alive).</summary>
    public const int Slots = 16;

    private readonly uint[] _guid = new uint[Slots];
    private readonly int[] _alive = new int[Slots];
    private readonly int[] _lit = new int[Slots];
    private readonly bool[] _seen = new bool[Slots];
    private readonly bool[] _used = new bool[Slots];
    private readonly bool[] _wasOn = new bool[Slots];
    private readonly bool[] _ended = new bool[Slots];
    private readonly int[] _stateOn = new int[Slots];
    private long _frames;
    private int _maxLights;
    private bool _viewerLast;
    private bool _frameOpen;
    private int _judged;
    private int _passed;

    /// <summary>The frames sampled.</summary>
    public long Frames => _frames;

    /// <summary>The most dynamic lights in one frame.</summary>
    public int DynamicLightsMax => _maxLights;

    /// <summary>The viewer light was in the last frame sampled.</summary>
    public bool ViewerLight => _viewerLast;

    /// <summary>Flights judged (finished, or closed by <see cref="Finish"/>).</summary>
    public int FlightsJudged => _judged;

    /// <summary>Judged flights whose light was in the frame for at least <see cref="LitPercent"/> percent of their frames.</summary>
    public int FlightsLit => _passed;

    /// <summary>Flights too short to judge.</summary>
    public int ShortFlights { get; private set; }

    /// <summary>Projectiles seen whose state never had <c>LightingOn</c> (not judged: the light is off by the object's state; the offline fight's first avatar is the previous check's spent bolt).</summary>
    public int StateOffFlights { get; private set; }

    /// <summary>Projectiles that did not fit the <see cref="Slots"/> (not judged; loud in the leg's detail line).</summary>
    public int Untracked { get; private set; }

    /// <summary><c>bolt_light</c>: at least one flight judged and every judged flight lit enough. Call <see cref="Finish"/> first to close the flights still alive.</summary>
    public bool BoltLight => _judged > 0 && _passed == _judged;

    /// <summary>Opens a frame: the lights in it. Then call <see cref="Projectile"/> for every projectile alive and <see cref="EndFrame"/>.</summary>
    /// <param name="frame">The avatar set's dynamic-light frame, after this frame's <c>Advance</c>.</param>
    public void BeginFrame(DynamicLightFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames++;
        _frameOpen = true;
        _maxLights = Math.Max(_maxLights, frame.Count);
        _viewerLast = false;
        _frame = frame;
        for (var i = 0; i < frame.Count; i++)
        {
            if (frame[i].OwnerId == 0)
            {
                _viewerLast = true;
            }
        }
    }

    private DynamicLightFrame? _frame;

    /// <summary>One projectile alive this frame.</summary>
    /// <param name="guid">Its guid.</param>
    /// <param name="lightsOn">Its state has <c>LightingOn</c> (<c>AvatarBody.LightsOn</c>); the flight ends when that falls after it was set (<c>ProjectileImpact</c> clears it), because the avatar lingers in the set for seconds after its Explode.</param>
    public void Projectile(uint guid, bool lightsOn = true)
    {
        if (!_frameOpen || _frame is not { } frame)
        {
            return;
        }

        var slot = -1;
        var free = -1;
        for (var i = 0; i < Slots; i++)
        {
            if (_used[i] && _guid[i] == guid)
            {
                slot = i;
                break;
            }

            if (!_used[i] && free < 0)
            {
                free = i;
            }
        }

        if (slot < 0)
        {
            if (free < 0)
            {
                Untracked++;
                return;
            }

            (slot, _used[free], _guid[free], _alive[free], _lit[free]) = (free, true, guid, 0, 0);
            (_wasOn[slot], _ended[slot], _stateOn[slot]) = (false, false, 0);
        }

        _seen[slot] = true;
        if (_ended[slot])
        {
            return;
        }

        if (lightsOn)
        {
            _wasOn[slot] = true;
            _stateOn[slot]++;
        }
        else if (_wasOn[slot])
        {
            _ended[slot] = true; // the Explode cleared the light: the flight is over
            return;
        }

        _alive[slot]++;
        for (var i = 0; i < frame.Count; i++)
        {
            if (frame[i].OwnerId == guid)
            {
                _lit[slot]++;
                break;
            }
        }
    }

    /// <summary>Closes the frame: a projectile not named this frame is gone, and its flight is judged.</summary>
    public void EndFrame()
    {
        for (var i = 0; i < Slots; i++)
        {
            if (!_used[i])
            {
                continue;
            }

            if (_seen[i])
            {
                _seen[i] = false;
            }
            else
            {
                Close(i);
            }
        }

        _frameOpen = false;
        _frame = null;
    }

    /// <summary>One frame from the avatar set (after <c>Advance</c>): the frame's lights and every projectile in the set.</summary>
    /// <param name="avatars">The avatar set.</param>
    public void Step(AvatarSet avatars)
    {
        ArgumentNullException.ThrowIfNull(avatars);
        BeginFrame(avatars.DynamicLightFrame);
        var all = avatars.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Body.IsProjectile)
            {
                Projectile(all[i].Guid, all[i].Body.LightsOn);
            }
        }

        EndFrame();
    }

    /// <summary>Ends the run: the flights still alive are judged as they stand.</summary>
    public void Finish()
    {
        for (var i = 0; i < Slots; i++)
        {
            if (_used[i])
            {
                Close(i);
            }
        }
    }

    private readonly int[] _history = new int[3 * HistoryFlights];
    private int _historyCount;

    /// <summary>The flights whose (frames in flight, frames lit, frames the state said lit) are kept for the leg's detail line.</summary>
    public const int HistoryFlights = 12;

    /// <summary>The first <see cref="HistoryFlights"/> flights as <c>flight/lit/state</c> triples (a diagnostic, built once at the end of the run).</summary>
    public string FlightsText()
    {
        var parts = new string[_historyCount];
        for (var i = 0; i < _historyCount; i++)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            parts[i] = _history[3 * i].ToString(inv) + "/" + _history[(3 * i) + 1].ToString(inv) + "/" + _history[(3 * i) + 2].ToString(inv);
        }

        return string.Join(' ', parts);
    }

    private void Close(int slot)
    {
        if (_historyCount < HistoryFlights)
        {
            _history[3 * _historyCount] = _alive[slot];
            _history[(3 * _historyCount) + 1] = _lit[slot];
            _history[(3 * _historyCount) + 2] = _stateOn[slot];
            _historyCount++;
        }

        if (_stateOn[slot] == 0)
        {
            StateOffFlights++; // never had LightingOn: a leftover whose Explode already cleared it, or a projectile ACE did not light; its light is off by design, not by the pipeline
        }
        else if (_alive[slot] < MinFlightFrames)
        {
            // TODO(m3-bolt-light-short-flight): a flight under MinFlightFrames is counted, not judged (two frames cannot show a percentage); bolt_light does not read it.
            ShortFlights++;
        }
        else
        {
            _judged++;
            if (_lit[slot] * 100 >= _alive[slot] * LitPercent)
            {
                _passed++;
            }
        }

        _used[slot] = false;
    }
}

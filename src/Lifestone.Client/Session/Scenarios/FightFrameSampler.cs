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
/// </remarks>
public sealed class FightFrameSampler
{
    /// <summary>The length of one fps window, in seconds.</summary>
    public const double WindowSeconds = 1.0;

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

    /// <summary>Takes one frame.</summary>
    /// <param name="deltaSeconds">The engine's delta, in seconds: the previous frame's whole time.</param>
    /// <param name="castWindowOpen">The cast window is open.</param>
    /// <param name="projectileWindowOpen">A projectile is alive (the projectile window is open).</param>
    /// <param name="targetEngaged">The loop has a target (or is waiting on a bolt).</param>
    /// <param name="fighting">The scenario is in its fight stage (the top-up's casts and the walk to the den are not the fight's frames).</param>
    /// <param name="inDen">The player is present in the Black Spawn Den.</param>
    /// <param name="skip">True for a frame to leave out (the one after a saved review frame).</param>
    public void Step(double deltaSeconds, bool castWindowOpen, bool projectileWindowOpen, bool targetEngaged, bool fighting, bool inDen, bool skip)
    {
        if (skip || deltaSeconds <= 0 || double.IsNaN(deltaSeconds))
        {
            _windowSeconds = 0;
            _windowFrames = 0; // a stalled frame spoils the window it fell in
            return;
        }

        _frames++;
        var ms = deltaSeconds * 1000.0;
        if (castWindowOpen && (double.IsNaN(_castMaxMs) || ms > _castMaxMs))
        {
            _castMaxMs = ms;
        }

        if (fighting && (castWindowOpen || projectileWindowOpen || targetEngaged) && (double.IsNaN(_fightMaxMs) || ms > _fightMaxMs))
        {
            _fightMaxMs = ms;
        }

        if (!inDen)
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
    public static readonly string[] Names = ["bolt", "hit", "kill"];

    /// <summary>The seconds after each trigger at which the frame is taken (the bolt in flight, the hit, the kill line).</summary>
    public static readonly double[] DelaysSeconds = [0.15, 0.1, 0.5];

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

    private readonly State[] _state = new State[3];
    private readonly double[] _triggeredAt = new double[3];
    private readonly int[] _frames = new int[3];
    private readonly string?[] _why = new string?[3];
    private long _createdBase;
    private long _explodesBase;
    private int _damageBase;
    private int _killsBase;
    private bool _begun;
    private bool _damageSeen;
    private long _createdMark;
    private double _lastExplodeAt = double.NegativeInfinity;
    private double _lastDamageAt = double.NegativeInfinity;

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
    /// <returns>The slot (0 bolt, 1 hit, 2 kill) whose frame is due now, or -1. At most one a frame (a save stalls the frame after it).</returns>
    public int Step(double now, long projectilesCreated, long projectileExplodes, int damageLines, int kills)
    {
        if (!_begun)
        {
            return -1;
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
                State.Waiting => "trigger never came",
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

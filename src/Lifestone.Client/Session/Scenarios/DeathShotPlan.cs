namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The death leg's four review frames (<c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c>; M3 3.2 dd, the style of <see cref="FightShotPlan"/>): <c>hit</c> (a 0x01B2 with the health under 100%, plus
/// <see cref="DelaysSeconds"/>[0]: the vitals meters down), <c>dead</c> (the player's Dead motion plus 1.5 s: the death line in the chat), <c>corpse</c> (the corpse's F745 plus 0.3 s, when it comes before portal space) and
/// <c>lifestone</c> (the death's arrival plus 3.5 s: the lifestone and the 75% vitals). Each slot is triggered once; a slot whose trigger never came is reported by name in <see cref="Note"/> and never faked.
/// </summary>
/// <remarks>
/// <para><b>Held.</b> The caller holds the scenario (<see cref="IsOwed"/>) from a slot's trigger for at most <see cref="HoldSeconds"/> while its frame is owed, so the state to show is still on screen. The lifestone frame is due
/// 3.5 s after the arrival, past the hold: the scenario is held for 3 s of it and the frame is taken when due.</para>
/// <para><b>The world must be on screen.</b> A frame is taken only when the player is not in portal space (<c>inPortalSpace</c> false: the tunnel is not a review frame of the world). The corpse slot is the one that can fail on
/// it: ACE creates the corpse and sends the teleport in one tick (<c>Player_Death.cs</c> 247-292), so the corpse's frame is given up, by name, when portal space is already up 0.3 s after the corpse (and when the corpse came after the
/// F751). The other slots wait for the world, up to their give-up time (<see cref="GiveUpSeconds"/>).</para>
/// <para>The plan is pure (the caller reads the counters and saves the frames), allocates nothing per frame, and returns at most one slot a frame (a save stalls the frame after it).</para>
/// </remarks>
public sealed class DeathShotPlan
{
    /// <summary>The slots' names, in the order of the files <c>&lt;stem&gt;-hit.png</c>, <c>-dead.png</c>, <c>-corpse.png</c> and <c>-lifestone.png</c>.</summary>
    public static readonly string[] Names = ["hit", "dead", "corpse", "lifestone"];

    /// <summary>The seconds after each trigger at which the frame is taken.</summary>
    public static readonly double[] DelaysSeconds = [0.2, 1.5, 0.3, 3.5];

    /// <summary>How long after its trigger each slot may still be taken before it is given up, in seconds.</summary>
    public static readonly double[] GiveUpSeconds = [3.0, 4.5, 3.3, 13.5];

    /// <summary>The longest, in seconds, the scenario is held for a frame after its trigger.</summary>
    public const double HoldSeconds = 3.0;

    /// <summary>The frames the developer HUD stays hidden before a frame is saved (so the first hidden frame is not the saved one).</summary>
    public const int HiddenFramesBeforeShot = 2;

    /// <summary>The slot of the corpse.</summary>
    public const int CorpseSlot = 2;

    private enum State
    {
        Waiting,
        Pending,
        Taken,
        GivenUp,
    }

    private readonly State[] _state = new State[4];
    private readonly double[] _triggeredAt = new double[4];
    private readonly int[] _frames = new int[4];
    private readonly string?[] _why = new string?[4];
    private int _hitsBase;
    private int _deadBase;
    private int _corpsesBase;
    private int _arrivalsBase;
    private bool _begun;

    /// <summary>Starts watching: the counters now are the bases a rise above is measured from.</summary>
    /// <param name="hitsTaken">The tally's <c>HitsTaken</c> now.</param>
    /// <param name="deadMotions">The sampler's <c>DeadMotions</c> now.</param>
    /// <param name="corpses">The sampler's <c>Corpses</c> now.</param>
    /// <param name="arrivals">The sampler's <c>Arrivals</c> now.</param>
    public void Begin(int hitsTaken, int deadMotions, int corpses, int arrivals)
    {
        (_hitsBase, _deadBase, _corpsesBase, _arrivalsBase) = (hitsTaken, deadMotions, corpses, arrivals);
        _begun = true;
    }

    /// <summary>True when a trigger came and its hold has not run out: the scenario waits (and the developer HUD stays hidden).</summary>
    /// <param name="now">The leg's clock, in seconds.</param>
    public bool IsOwed(double now)
    {
        for (var i = 0; i < _state.Length; i++)
        {
            if (_state[i] == State.Pending && now - _triggeredAt[i] <= HoldSeconds)
            {
                return true;
            }
        }

        return false;
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
    /// <param name="hitsTaken">The tally's <c>HitsTaken</c> (the 0x01B2 notices).</param>
    /// <param name="healthUnderFull">The player's health is under its maximum.</param>
    /// <param name="deadMotions">The sampler's <c>DeadMotions</c>.</param>
    /// <param name="corpses">The sampler's <c>Corpses</c>.</param>
    /// <param name="corpseBeforePortal">The sampler's <c>CorpseBeforePortal</c>: the last corpse came before its death's F751.</param>
    /// <param name="arrivals">The sampler's <c>Arrivals</c>.</param>
    /// <param name="inPortalSpace">The player is not present in the world (portal space is up).</param>
    /// <returns>The slot (0 hit, 1 dead, 2 corpse, 3 lifestone) whose frame is due now, or -1. At most one a frame.</returns>
    public int Step(double now, int hitsTaken, bool healthUnderFull, int deadMotions, int corpses, bool corpseBeforePortal, int arrivals, bool inPortalSpace)
    {
        if (!_begun)
        {
            return -1;
        }

        Trigger(0, hitsTaken > _hitsBase && healthUnderFull, now);
        Trigger(1, deadMotions > _deadBase, now);
        if (corpses > _corpsesBase && _state[CorpseSlot] == State.Waiting)
        {
            if (corpseBeforePortal)
            {
                Trigger(CorpseSlot, true, now);
            }
            else
            {
                GiveUp(CorpseSlot, "the corpse's F745 came after the death's F751, so portal space was already up");
            }
        }

        Trigger(3, arrivals > _arrivalsBase, now);
        var due = -1;
        for (var i = 0; i < _state.Length; i++)
        {
            if (_state[i] != State.Pending)
            {
                continue;
            }

            _frames[i]++;
            var since = now - _triggeredAt[i];
            if (since < DelaysSeconds[i] || _frames[i] < HiddenFramesBeforeShot)
            {
                continue;
            }

            if (inPortalSpace)
            {
                if (i == CorpseSlot)
                {
                    GiveUp(i, "portal space was already up " + DelaysSeconds[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s after the corpse's F745");
                }
                else if (since > GiveUpSeconds[i])
                {
                    GiveUp(i, "portal space was up until the give-up time");
                }

                continue;
            }

            if (due < 0)
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
    public void MarkFailed(int slot, string why) => GiveUp(slot, why);

    /// <summary>
    /// What the plan ended with, one phrase per slot (<c>hit=taken dead=taken corpse=not taken (...) lifestone=trigger never came</c>): a slot never triggered is named as such, never faked.
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

    private void GiveUp(int slot, string why)
    {
        if (_state[slot] is State.Taken or State.GivenUp)
        {
            return;
        }

        _state[slot] = State.GivenUp;
        _why[slot] = why;
    }
}

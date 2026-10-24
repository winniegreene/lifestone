using System.Globalization;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The melee hunt leg's retail-UI review frames (M4 1.3 s2, the corpse frame 2.1, the heal and potion frames 3.1 p; <c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c>, taken beside <see cref="HuntShotPlan"/>'s swing frame): <c>c1-combat</c>
/// (the combat window shown in Melee mode during a run), <c>c1-powerbar</c> (mid-charge: the combat window's meter above 0 and below the run's power) and <c>meter</c> (the toolbar's target health meter after the first hit,
/// lower than at selection), <c>corpse</c> (the kill's corpse open) and, since 3.1 p, <c>heal</c> and <c>potion</c> (1 s after a kit use's roll was won and after a potion restored points: the vitals and the chat line after a recovery). Each slot has its own trigger and moment; a trigger whose condition fails at the frame's moment is dropped and waits for the next (the swing frame's rule), and a frame is owed for at most
/// <see cref="HoldSeconds"/>. The leg frames the camera for them (<see cref="FramedScale"/>: the chase camera's distance and height times that, so the player, the weapon and the target are in view, restored at the end;
/// <c>m4-hunt-frame-camera</c>: 1.2's frames hid the player in the belt's trees or behind a creature 0.5 m away) and drops a frame whose camera is blocked. The plan is pure (the caller reads the session and the shell
/// and saves the frame) and allocates nothing per frame.
/// </summary>
public sealed class HuntUiShotPlan
{
    /// <summary>The slots' names, in the order of the files <c>&lt;stem&gt;-c1-combat.png</c>, <c>-c1-powerbar.png</c>, <c>-meter.png</c>, <c>-corpse.png</c>, <c>-heal.png</c>, <c>-potion.png</c> and <c>-vendor.png</c>.</summary>
    public static readonly string[] Names = ["c1-combat", "c1-powerbar", "meter", "corpse", "heal", "potion", "vendor"];

    /// <summary>The slot of the <c>corpse</c> frame (M4 2.1 F1): the kill's corpse open and in view beside the player.</summary>
    public const int Corpse = 3;

    /// <summary>The slot of the <c>heal</c> frame (M4 3.1 p): 1 s after a healing kit's use confirmed with a Healed result (<see cref="HealResultKind.Healed"/>), so the vitals bar and the chat line show the heal. Taken once.</summary>
    public const int Heal = 4;

    /// <summary>The slot of the <c>potion</c> frame (M4 3.1 p): 1 s after a drink confirmed with a Drank result (<see cref="HealResultKind.Drank"/>: points restored). Taken once.</summary>
    public const int Potion = 5;

    /// <summary>
    /// The slot of the <c>vendor</c> frame (M4 3.2 s4a): 1 s after Renald the Elder's 0062 while the vendor panel shows. The frame is taken whatever shows (no target, no framed camera: the shop's interior and the panel are the subject) and its log line says
    /// whether a vendor panel was visible. Taken once; the scenario is held for it at most <see cref="HoldSeconds"/>.
    /// </summary>
    public const int Vendor = 6;

    /// <summary>The seconds after the vendor's 0062 at which the <c>vendor</c> frame is taken (the panel has filled and drawn).</summary>
    public const double VendorDelaySeconds = 1.0;

    /// <summary>The seconds after the heal's or the potion's confirmation at which their frames are taken (the vitals have moved and the chat line is in).</summary>
    public const double HealDelaySeconds = 1.0;

    /// <summary>
    /// The longest, in seconds, the scenario is held for the <c>corpse</c> frame, counted from its first trigger (the close is delayed that long at most, 2.1 F1). M4 2.2 s4: a dropped trigger (the container window not yet drawn, the corpse
    /// hidden from the camera) no longer lets the scenario step: the slot stays owed and is checked again after its delay, so the loot (which would shorten the window's list) has not started when the frame is taken; the hold ends the
    /// frame by name, never the loot's start.
    /// </summary>
    public const double CorpseHoldSeconds = 2.0;

    /// <summary>The seconds after each trigger at which the frame is taken.</summary>
    public static readonly double[] DelaysSeconds = [0.3, 0.0, 0.1, 0.3, HealDelaySeconds, HealDelaySeconds, VendorDelaySeconds];

    /// <summary>What triggers each slot, for the log.</summary>
    public static readonly string[] Triggers =
    [
        "the first own swing of an attack sequence with the combat window shown in Melee mode and the target within 6 m",
        "the combat window's meter between half the run's power and the power less a margin (mid-charge) with the target within 6 m",
        "the toolbar's health meter of the selected target lower than at its selection after a swing, the target within 6 m",
        "the kill's corpse open (the open Confirmed, the close not yet sent) with the corpse within 6 m",
        "a healing kit's use confirmed with a Healed result (the kit's roll won), the camera framed",
        "a potion's drink confirmed with a Drank result (points restored), the camera framed",
        "Renald the Elder's 0062 read (the vendor open in the read model), taken whatever shows",
    ];

    /// <summary>How near the target must be for the frames, in metres.</summary>
    public const double TargetRangeMetres = 6.0;

    /// <summary>The longest, in seconds, the scenario is held for a frame after its trigger.</summary>
    public const double HoldSeconds = 3.0;

    /// <summary>The frames the developer HUD stays hidden before a frame is saved.</summary>
    public const int HiddenFramesBeforeShot = 2;

    /// <summary>
    /// The scale of the chase camera's default offset for the frames: (0, -2.5, 0.75) times this is the viewer's offset from the pivot, 4.5 m behind and 1.35 m above it, about 4.7 m from the pivot (retail's is 2.5 m and 0.75 m).
    /// </summary>
    public const float FramedScale = 1.8f;

    /// <summary>
    /// The heading offset, in degrees, the missile hunt's framed camera starts at (M4 polish pass P2, <c>m4-hunt-frame-camera</c>): the viewer turned this far about the player (negative: to the player's left, the side of the bow arm), so the bow is seen
    /// from its side and not edge-on from straight behind (the lead's live missile frame showed it as a thin stick). The target, straight ahead of the player and some metres off, stays in the view: at 4.5 m and this turn its bearing from the
    /// camera's axis is under 35 degrees, inside the half-width of retail's field of view (<see cref="Lifestone.Client.Camera.RetailLens"/>, 44 degrees at 4:3).
    /// </summary>
    public const float MissileSideDegrees = -40f;

    /// <summary>
    /// How far down the window the player's box may reach for the frame to count as showing the player and the held weapon above the combat window: the box's middle must be above the window's top edge and its top on screen
    /// (the feet may be behind the window or the toolbar: the head, the shoulders and the hands are what the frame is for).
    /// </summary>
    /// <param name="player">The player's projected box.</param>
    /// <param name="playerKnown">False when the player's box could not be projected: the answer is false.</param>
    /// <param name="windowTop">The combat window's top edge, in the same pixels.</param>
    public static bool PlayerFramed(in ScreenRect player, bool playerKnown, float windowTop) =>
        playerKnown && player.Y >= 0f && player.Y + (player.Height * 0.5f) < windowTop;

    /// <summary>
    /// The scale of the default offset for the missile hunt's frames (M4 polish pass P2): 1.2, so the viewer is 3.1 m from the pivot (3 m behind and 0.9 m above) and the archer and his bow are half as large again as at <see cref="FramedScale"/>
    /// while the target straight ahead stays in view. The offset's height is over 0.75, so the camera looks at the pivot (<c>CameraSet::SetTargetForOffset</c>) and the player's head and hands sit around the middle of the window, above the combat window.
    /// </summary>
    public const float MissileFramedScale = 1.2f;

    /// <summary>How far, in metres, the viewer's distance from the pivot may be from the framed one at a frame's moment (the camera's smoothing has settled).</summary>
    public const float SettledMetres = 0.3f;

    /// <summary>The share of the run's power the meter must have reached to count as mid-charge (1.3-R2).</summary>
    public const float MidChargeFraction = 0.5f;

    /// <summary>How far the meter's level must stay short of the run's power to count as mid-charge.</summary>
    public const float MidChargeMargin = 0.05f;

    /// <summary>The most of the target's projected box the player's box may cover for a frame (1.3-L1: the framed camera put the target behind the player).</summary>
    public const float MaxOverlap = 0.30f;

    /// <summary>The heading offset, in degrees, the framing camera turns by to the side so the player and the target separate on screen (then the other side).</summary>
    public const float SideOffsetDegrees = 35f;

    /// <summary>The seconds the camera is given to settle after a heading offset is set, before the view is judged again.</summary>
    public const double SideSettleSeconds = 0.8;

    /// <summary>How much lower, as a fraction of the bar, the health meter must be than at selection to count as hit.</summary>
    public const float MeterDrop = 0.005f;

    /// <summary>The framed camera's distance from the pivot: |(0, -2.5, 0.75)| times <see cref="FramedScale"/>.</summary>
    public static readonly float FramedMetres = FramedMetresOf(FramedScale);

    /// <summary>The distance from the pivot of a viewer at the default offset (0, -2.5, 0.75) times <paramref name="scale"/>.</summary>
    /// <param name="scale">The camera model's scale.</param>
    public static float FramedMetresOf(float scale) => MathF.Sqrt((2.5f * 2.5f) + (0.75f * 0.75f)) * scale;

    private enum SlotState
    {
        Waiting,
        Pending,
        Taken,
        GivenUp,
    }

    private readonly SlotState[] _state = new SlotState[Names.Length];
    private readonly double[] _triggeredAt = new double[Names.Length];
    private double _corpseFirstAt = double.NaN; // the leg's clock at the corpse slot's first trigger: the hold of CorpseHoldSeconds is counted from it, across dropped triggers
    private double _now; // the latest clock a call gave (Trigger, Expire, Due): a dropped corpse trigger is re-armed at it
    private HealSnapshot _healSeen; // the heal counters at the last ObserveHeals (a fresh tally is all zeros)
    private readonly int[] _dropped = new int[Names.Length];
    private readonly string?[] _why = new string?[Names.Length];

    /// <summary>True while any slot has a trigger and is not yet taken, dropped or given up: the scenario is held.</summary>
    public bool Owed
    {
        get
        {
            for (var i = 0; i < _state.Length; i++)
            {
                if (_state[i] == SlotState.Pending)
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
                n += _state[i] == SlotState.Taken ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>True while a trigger may still be accepted for <paramref name="slot"/>.</summary>
    /// <param name="slot">The slot (index into <see cref="Names"/>).</param>
    public bool Waiting(int slot) => _state[slot] == SlotState.Waiting;

    /// <summary>True when the frame of <paramref name="slot"/> was taken.</summary>
    /// <param name="slot">The slot.</param>
    public bool IsTaken(int slot) => _state[slot] == SlotState.Taken;

    /// <summary>True when <paramref name="slot"/> was given up (owed for longer than its hold, or its frame could not be saved).</summary>
    /// <param name="slot">The slot.</param>
    public bool IsGivenUp(int slot) => _state[slot] == SlotState.GivenUp;

    /// <summary>A trigger of <paramref name="slot"/> came at <paramref name="now"/>; ignored unless the slot is waiting.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="now">The leg's clock, in seconds.</param>
    public void Trigger(int slot, double now)
    {
        _now = now;
        if (_state[slot] == SlotState.Waiting)
        {
            _state[slot] = SlotState.Pending;
            _triggeredAt[slot] = now;
            if (slot == Corpse && double.IsNaN(_corpseFirstAt))
            {
                _corpseFirstAt = now;
            }
        }
    }

    /// <summary>Gives up the slots owed for more than <see cref="HoldSeconds"/> (a pending frame whose moment never held).</summary>
    /// <param name="now">The leg's clock, in seconds.</param>
    public void Expire(double now)
    {
        _now = now;
        for (var i = 0; i < _state.Length; i++)
        {
            if (_state[i] == SlotState.Pending && (i == Corpse ? now - _corpseFirstAt > CorpseHoldSeconds : now - _triggeredAt[i] > HoldSeconds))
            {
                _state[i] = SlotState.GivenUp;
                _why[i] = i == Corpse ? "held longer than 2 s" : "held longer than 3 s";
            }
        }
    }

    /// <summary>The slot whose frame is due now (its delay has passed since its trigger and the HUD has been hidden for <see cref="HiddenFramesBeforeShot"/> frames), or -1. At most one a frame (a save stalls the frame after it).</summary>
    /// <param name="now">The leg's clock, in seconds.</param>
    /// <param name="hiddenFrames">The frames the developer HUD has been hidden.</param>
    public int Due(double now, int hiddenFrames)
    {
        _now = now;
        if (hiddenFrames < HiddenFramesBeforeShot)
        {
            return -1;
        }

        for (var i = 0; i < _state.Length; i++)
        {
            if (_state[i] == SlotState.Pending && now - _triggeredAt[i] >= DelaysSeconds[i])
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The frame of <paramref name="slot"/> was saved.</summary>
    /// <param name="slot">The slot.</param>
    public void MarkTaken(int slot) => _state[slot] = SlotState.Taken;

    /// <summary>
    /// The slot's condition failed at the frame's moment (the target out of 6 m, the camera blocked, the meter no longer mid-charge): drop the trigger and wait for the next. The <c>corpse</c> slot is the exception (M4 2.2 s4): it stays
    /// owed and is armed again at the clock of the latest call, so it is checked after its delay and the scenario stays held until <see cref="CorpseHoldSeconds"/> from its first trigger.
    /// </summary>
    /// <param name="slot">The slot.</param>
    public void Drop(int slot)
    {
        if (_state[slot] != SlotState.Pending)
        {
            return;
        }

        _dropped[slot]++;
        if (slot == Corpse)
        {
            _triggeredAt[Corpse] = _now; // M4 2.2 s4: stays owed (the scenario stays held) and is checked again after its delay, until the frame is taken or the hold from its first trigger ends
            return;
        }

        _state[slot] = SlotState.Waiting;
    }

    /// <summary>
    /// M4 2.2 fix2 R15: the hunt's retreat rule or its seconds abandoned the corpse sequence while the <c>corpse</c> frame was owed: the window it would show is closing, so the frame is given up and the hold ends. Nothing when that
    /// slot is not owed.
    /// </summary>
    /// <param name="why">Why (named in <see cref="Note"/>).</param>
    public void ReleaseCorpse(string why)
    {
        if (_state[Corpse] == SlotState.Pending)
        {
            MarkFailed(Corpse, why);
        }
    }

    /// <summary>The frame of <paramref name="slot"/> could not be saved: given up.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="why">Why.</param>
    public void MarkFailed(int slot, string why)
    {
        _state[slot] = SlotState.GivenUp;
        _why[slot] = why;
    }

    /// <summary>True for the slots whose frame shows the UI after a recovery (<see cref="Heal"/>, <see cref="Potion"/>): they need the framed camera, but no target (the hunt heals between kills) and no projection of one.</summary>
    /// <param name="slot">The slot.</param>
    public static bool IsHealSlot(int slot) => slot is Heal or Potion;

    /// <summary>
    /// Reads the hunt's heal counters (the session-fed <c>HealTally</c> the hunt's driver exposes: a finished heal handle is recorded there) and triggers <see cref="Heal"/> when a kit use whose roll won has been counted since the last call, and
    /// <see cref="Potion"/> when a drink that restored points has (a drink was counted, no wasted one, the points rose). Call it every frame; a counter that did not rise triggers nothing, and a slot that is not waiting ignores a trigger (each is taken once).
    /// </summary>
    /// <param name="now">The counters now.</param>
    /// <param name="clock">The leg's clock, in seconds.</param>
    public void ObserveHeals(in HealSnapshot now, double clock)
    {
        if (now.Heals > _healSeen.Heals)
        {
            Trigger(Heal, clock);
        }

        if (now.Potions > _healSeen.Potions && now.Wasted == _healSeen.Wasted && now.Points > _healSeen.Points)
        {
            Trigger(Potion, clock);
        }

        _healSeen = now;
    }

    /// <summary>True when <paramref name="level"/> is mid-charge for a run of <paramref name="power"/>: at least <see cref="MidChargeFraction"/> of the power and short of it by <see cref="MidChargeMargin"/> (1.3-R2: the first frame above 0 read as an empty meter).</summary>
    /// <param name="level">The combat window's meter position (0..1).</param>
    /// <param name="power">The power the run was sent with (0..1).</param>
    public static bool MidCharge(float level, float power) => level >= MidChargeFraction * power && level <= power - MidChargeMargin;

    /// <summary>The share (0..1) of <paramref name="target"/>'s area that <paramref name="player"/> covers; 0 for an empty target or no overlap.</summary>
    /// <param name="target">The target's projected box.</param>
    /// <param name="player">The player's projected box.</param>
    public static float Overlap(in ScreenRect target, in ScreenRect player)
    {
        var area = target.Width * target.Height;
        if (area <= 0f)
        {
            return 0f;
        }

        var w = MathF.Min(target.Right, player.Right) - MathF.Max(target.X, player.X);
        var h = MathF.Min(target.Bottom, player.Bottom) - MathF.Max(target.Y, player.Y);
        return w <= 0f || h <= 0f ? 0f : w * h / area;
    }

    /// <summary>
    /// True when the target is fit for a frame: its projected box is on screen, its middle above the combat window's top edge and the player covers at most <see cref="MaxOverlap"/> of it.
    /// </summary>
    /// <param name="target">The target's projected box.</param>
    /// <param name="onScreen">The projector's on-screen flag for the target.</param>
    /// <param name="player">The player's projected box (ignored when <paramref name="playerKnown"/> is false).</param>
    /// <param name="playerKnown">False when the player's box could not be projected: no overlap is judged and the log says so.</param>
    /// <param name="windowTop">The combat window's top edge, in the same pixels.</param>
    /// <param name="overlap">The overlap share.</param>
    public static bool TargetVisible(in ScreenRect target, bool onScreen, in ScreenRect player, bool playerKnown, float windowTop, out float overlap)
    {
        overlap = playerKnown ? Overlap(in target, in player) : 0f;
        return onScreen && target.Y + (target.Height * 0.5f) < windowTop && overlap <= MaxOverlap;
    }

    /// <summary>
    /// The corpse's ObjDesc facts as one log phrase (M4 2.2 s4, <c>q6-corpse-objdesc-live</c>): how many animation-part changes, palette sets and texture changes the CreateObject carried, so the lead can set them beside what the frame draws
    /// (the den's old corpses drew skin on the arms and legs). <paramref name="model"/> is the object's CreateObject model; <paramref name="updated"/> says a later ObjDescEvent changed what the object shows now.
    /// </summary>
    /// <param name="model">The CreateObject's model data.</param>
    /// <param name="updated">True when the object's current model is not the CreateObject's.</param>
    public static string ObjDescFacts(ModelData model, bool updated)
    {
        ArgumentNullException.ThrowIfNull(model);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"objdesc_parts={model.AnimPartChanges.Length} objdesc_palettes={model.SubPalettes.Length} objdesc_textures={model.TextureChanges.Length} objdesc_base_palette={(model.BasePaletteId is { } palette ? "0x" + palette.ToString("X8", CultureInfo.InvariantCulture) : "none")} objdesc_updated={(updated ? "yes" : "no")}");
    }

    /// <summary>The next heading offset to try after <paramref name="current"/> failed: 0 then +35, then -35, then back to 0 (the trigger is dropped each time).</summary>
    /// <param name="current">The offset now, in degrees.</param>
    public static float NextSideOffset(float current) => current == 0f ? SideOffsetDegrees : current > 0f ? -SideOffsetDegrees : 0f;

    /// <summary>True when the health meter <paramref name="now"/> is lower than <paramref name="atSelection"/> by <see cref="MeterDrop"/>; false when either is not a number.</summary>
    /// <param name="atSelection">The meter's fraction when the target was selected.</param>
    /// <param name="now">The meter's fraction now.</param>
    public static bool MeterLowered(float atSelection, float now) => !float.IsNaN(atSelection) && !float.IsNaN(now) && now < atSelection - MeterDrop;

    /// <summary>True when the viewer's distance from the pivot has settled on the framed camera (<see cref="FramedScale"/>) and the sweep did not shorten it.</summary>
    /// <param name="pivotDistance">The measured distance in metres.</param>
    /// <param name="blocked">The camera's sweep stopped it short.</param>
    public static bool CameraFramed(float pivotDistance, bool blocked) => CameraFramed(pivotDistance, blocked, FramedScale);

    /// <summary>True when the viewer's distance from the pivot has settled on the framed camera of <paramref name="scale"/> and the sweep did not shorten it.</summary>
    /// <param name="pivotDistance">The measured distance in metres.</param>
    /// <param name="blocked">The camera's sweep stopped it short.</param>
    /// <param name="scale">The scale the camera was framed with (<see cref="FramedScale"/> or <see cref="MissileFramedScale"/>).</param>
    public static bool CameraFramed(float pivotDistance, bool blocked, float scale) => !blocked && Math.Abs(pivotDistance - FramedMetresOf(scale)) <= SettledMetres;

    /// <summary>The phrase of one slot for the log (what <see cref="Note"/> prints for it).</summary>
    /// <param name="slot">The slot.</param>
    public string NoteOf(int slot)
    {
        var phrase = _state[slot] switch
        {
            SlotState.Taken => "taken",
            SlotState.Pending => "trigger came, frame not taken",
            SlotState.GivenUp => "given up (" + _why[slot] + ")",
            _ => "not taken (trigger '" + Triggers[slot] + "' never held)",
        };
        return Names[slot] + ": " + phrase + (_dropped[slot] > 0 ? ", " + _dropped[slot].ToString(CultureInfo.InvariantCulture) + " trigger(s) dropped" : string.Empty);
    }

    /// <summary>One phrase per slot for the log (<c>c1-combat: taken; c1-powerbar: not taken (trigger '...' never held); meter: given up (...)</c>), never faked.</summary>
    public string Note()
    {
        var parts = new string[_state.Length];
        for (var i = 0; i < _state.Length; i++)
        {
            var phrase = _state[i] switch
            {
                SlotState.Taken => "taken",
                SlotState.Pending => "trigger came, frame not taken",
                SlotState.GivenUp => "given up (" + _why[i] + ")",
                _ => "not taken (trigger '" + Triggers[i] + "' never held)",
            };
            parts[i] = Names[i] + ": " + phrase + (_dropped[i] > 0 ? ", " + _dropped[i].ToString(CultureInfo.InvariantCulture) + " trigger(s) dropped" : string.Empty);
        }

        return string.Join("; ", parts);
    }

    /// <summary>The path of the frame of <paramref name="slot"/>: <c>&lt;stem&gt;-&lt;name&gt;.png</c> (the stem is the path without its extension).</summary>
    /// <param name="path">The asked path (<c>--play-screenshot</c>).</param>
    /// <param name="slot">The slot.</param>
    public static string ShotPath(string path, int slot) => Path.ChangeExtension(path, null) + "-" + Names[slot] + ".png";
}

/// <summary>The hunt's heal counters as the plan reads them (<c>HealTally.Heals</c>, <c>Potions</c>, <c>Wasted</c>, <c>Points</c>): plain numbers, so the plan stays pure.</summary>
/// <param name="Heals">Kit uses whose roll won.</param>
/// <param name="Potions">Drinks confirmed.</param>
/// <param name="Wasted">Drinks that restored nothing.</param>
/// <param name="Points">The points restored by every heal and drink.</param>
public readonly record struct HealSnapshot(int Heals, int Potions, int Wasted, int Points);

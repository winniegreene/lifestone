using System.Globalization;
using System.Text;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;

namespace Lifestone.Client.Play;

/// <summary>The kinds of held item the look capture walks through (one step each, M4 4.1 lane v0, D27).</summary>
public enum LookKind
{
    /// <summary>A melee weapon (a mace, a sword, a two-handed weapon).</summary>
    Melee,

    /// <summary>A launcher (a bow, a crossbow; its ammunition is wielded with it).</summary>
    Launcher,

    /// <summary>A caster (a wand, a staff or an orb: the Healer's Heart is an orb).</summary>
    Caster,
}

/// <summary>The views the look capture takes its frames from (<c>--play-look-view</c>).</summary>
public enum LookView
{
    /// <summary>The close view of the upper body and hands (<see cref="LookShotsPlan.CameraScale"/>, <see cref="LookShotsPlan.CameraHeadingDegrees"/>): the default.</summary>
    Close,

    /// <summary>The view the hunt's review frames use (<c>HuntUiShotPlan.FramedScale</c>, the missile hunt's side turn for a launcher, straight behind otherwise), so the framing can be judged offline with the real weapons in hand.</summary>
    Hunt,
}

/// <summary>Where the look capture puts the camera: the scale of retail's default viewer offset, the turn about the player and the pivot's height.</summary>
/// <param name="Scale">The scale of the default viewer offset (0, -2.5, 0.75).</param>
/// <param name="HeadingDegrees">The turn of the viewer about the pivot (<c>ChaseCameraModel.SetHeadingOffset</c>), before the body's own turn is added.</param>
/// <param name="PivotZ">The pivot's height above the player's origin, in metres.</param>
public readonly record struct LookCamera(float Scale, float HeadingDegrees, float PivotZ);

/// <summary>An item the look capture wields, stows or puts back.</summary>
/// <param name="Guid">The item's object id.</param>
/// <param name="Wcid">The item's weenie class id (0 when the object table does not hold it).</param>
/// <param name="Name">The item's name (a question mark when the object table does not hold it).</param>
/// <param name="Slot">The <see cref="EquipMask"/> bits it is wielded at (the slot it was in at the start, else the slot its kind uses).</param>
/// <param name="HomeContainer">The pack the item was in when the plan was made (0 for the main pack, else the side pack's guid; 0 too for an item that was in the hands): the stow puts it back there (M4 4.1 fix2b, review R10).</param>
/// <param name="HomePlacement">The item's slot in <paramref name="HomeContainer"/> when the plan was made (0 for an item that was in the hands).</param>
public readonly record struct LookItem(uint Guid, uint Wcid, string Name, uint Slot, uint HomeContainer = 0, uint HomePlacement = 0);

/// <summary>One step of the look capture: an item wielded in turn, with its ammunition when it is a launcher.</summary>
/// <param name="Name">The step's name, the first part of its frame files (<c>melee</c>, <c>missile</c>, <c>caster</c>).</param>
/// <param name="Kind">What kind of item it is.</param>
/// <param name="Item">The item.</param>
/// <param name="Ammo">The launcher's ammunition, or null (a melee weapon, a caster, or a launcher the character carries no ammunition for).</param>
public sealed record LookStep(string Name, LookKind Kind, LookItem Item, LookItem? Ammo);

/// <summary>What one operation of the look capture does.</summary>
public enum LookOpKind
{
    /// <summary>Go to peace (<see cref="CombatMode.NonCombat"/>) through <see cref="IActionApi.SetCombatMode"/>; nothing is sent when the character is in peace already.</summary>
    Peace,

    /// <summary>Put <see cref="LookOp.Item"/> in its own pack and slot from the plan (<see cref="LookItem.HomeContainer"/>, <see cref="LookItem.HomePlacement"/>; the main pack slot 0 for an item that was in the hands) (<see cref="IActionApi.MoveToPack"/>); nothing is sent when it is not wielded.</summary>
    Stow,

    /// <summary>Wield <see cref="LookOp.Item"/> at its slot (<see cref="IActionApi.Wield"/>).</summary>
    Wield,

    /// <summary>Take frame <see cref="LookOp.Frame"/> of the step (A in peace after the wield, B in the stance after it settles, C after the stow).</summary>
    Frame,

    /// <summary>Enter the combat mode <see cref="CombatModeRules.DefaultMode(GameState, out string?)"/> gives for what is wielded now; a refusal is logged as data.</summary>
    EnterDefaultMode,

    /// <summary>Put the combat mode of the start back (only when the run began in a combat mode).</summary>
    RestoreMode,
}

/// <summary>One operation of <see cref="LookShotsPlan.Ops"/>.</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Step">The index of its step in <see cref="LookShotsPlan.Steps"/>, or -1 for the put-back at the end.</param>
/// <param name="Item">The item it moves, for a stow or a wield.</param>
/// <param name="Frame">The frame letter (<c>a</c>, <c>b</c> or <c>c</c>), for a frame.</param>
public readonly record struct LookOp(LookOpKind Kind, int Step, LookItem? Item = null, char Frame = ' ');

/// <summary>A hint for an item whose weenie description carries no kind (the offline stand-in's placeholder items, <c>TODO(m3-standin-weenies)</c>).</summary>
/// <param name="Kind">The step the item belongs to.</param>
/// <param name="IsAmmo">True when the item is the ammunition of a launcher, not a step of its own.</param>
public readonly record struct LookKindHint(LookKind Kind, bool IsAmmo = false);

/// <summary>What one look line says (<see cref="LookShotsPlan.Line"/>); a field the code could not read is null and prints <c>unknown</c>.</summary>
/// <param name="ItemName">The item's name.</param>
/// <param name="Wcid">The item's weenie class id.</param>
/// <param name="Frame">The frame letter.</param>
/// <param name="Parent">The item's parent result: <c>ok</c>, <c>failed(&lt;location&gt;)</c> or <c>none</c>.</param>
/// <param name="Drawn">True when the item's avatar was drawn in the scene.</param>
/// <param name="Light">The item's Setup light: <c>on</c>, <c>off</c> or <c>none</c> (the Setup has no light).</param>
/// <param name="Particles">The live emitters on the item's avatar.</param>
/// <param name="Stance">The motion style the server last sent for the player.</param>
/// <param name="Mode">The player's combat mode.</param>
/// <param name="Ammo">The launcher's ammunition and its facts, for a launcher's lines.</param>
public readonly record struct LookFacts(string ItemName, uint Wcid, char Frame, string? Parent, bool? Drawn, string? Light, int? Particles, string? Stance, string? Mode, string? Ammo = null);

/// <summary>
/// The look capture (M4 4.1 lane v0, D27; <c>--play-look-shots=DIR</c>): the instrument that reproduces what the owner sees in the window (a wielded bow not drawn, a stray glow from the Healer's Heart, no glow when it comes out,
/// a wand or an orb in a melee stance) and records it, frame by frame, for batch 2's fixes. The plan is pure: from what the character carries and wields (<see cref="SelfState.Inventory"/>) it makes one <see cref="LookStep"/> for each
/// held-item kind present (a melee weapon, a launcher with its ammunition, a caster), and the flat list of operations the Godot half performs through the Action API:
/// <list type="number">
/// <item>peace; everything in the hands put in the pack; the item (and a launcher's ammunition) wielded; <b>frame A</b> (peace, the item in hand);</item>
/// <item>the mode <see cref="CombatModeRules"/> gives (retail <c>GetDefaultCombatMode</c>, 11.6096 0x0056C050) entered, a refusal logged as data; <b>frame B</b> (that stance, after it settles);</item>
/// <item>peace; the item stowed; <b>frame C</b> (nothing may be left where it was: no glow, no stray model);</item>
/// <item>at the end, the items the character held at the start wielded again (and its combat mode put back).</item>
/// </list>
/// The frames are close third-person frames of the upper body and hands (<see cref="CameraScale"/>, <see cref="CameraHeadingDegrees"/>), 800x600, the developer HUD hidden, named <c>&lt;step&gt;-&lt;a|b|c&gt;.png</c>; each has one
/// <see cref="Line"/>. The plan decides nothing about the avatar code: it records what is there.
/// </summary>
public sealed class LookShotsPlan
{
    /// <summary>The slots a hand holds: the melee, missile, two-handed, held and ammunition slots (a shield and armour stay on).</summary>
    public const uint HandSlots = (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon | EquipMask.TwoHanded | EquipMask.Held | EquipMask.MissileAmmo);

    /// <summary>The chase camera's scale for the look frames (retail's viewer offset is (0, -2.5, 0.75) times it): about 1.9 m from the pivot instead of 2.6 m, so the upper body and the hands fill the frame.</summary>
    public const float CameraScale = 0.8f;

    /// <summary>The turn of the camera about the player, in degrees (<c>ChaseCameraModel.SetHeadingOffset</c>): 130 degrees swings the camera round to the front of the player, so the hand that holds the item and the item are in view (the retail UI still covers the lowest part of the frame).</summary>
    public const float CameraHeadingDegrees = 130f;

    /// <summary>
    /// The pivot's height for the close view, in metres (retail's is 1.5, the head: <c>ChaseCameraModel.DefaultPivotZ</c>). The camera looks at the pivot, so at the head the hands (about 0.9 m up) fall behind the combat window or the spell
    /// window (M4 polish pass P3: the magic stance's frame showed a head and shoulders, the orb hidden); at 1.15 the hands are above the window's top edge at 800x600 and the head is still in frame. A capture aid, not retail's camera.
    /// </summary>
    public const float ClosePivotZ = 1.15f;

    /// <summary>How far, in degrees, the aim may drift before the camera is aimed again.</summary>
    public const float AimToleranceDegrees = 2f;

    /// <summary>The camera of <paramref name="view"/> for a step of <paramref name="kind"/>.</summary>
    /// <param name="view">The view.</param>
    /// <param name="kind">The kind of held item the step wields (a launcher is seen from the side in the hunt view).</param>
    public static LookCamera CameraFor(LookView view, LookKind kind) => view == LookView.Hunt
        ? (kind == LookKind.Launcher
            ? new LookCamera(Lifestone.Client.Session.Scenarios.HuntUiShotPlan.MissileFramedScale, Lifestone.Client.Session.Scenarios.HuntUiShotPlan.MissileSideDegrees, Lifestone.Client.Camera.ChaseCameraModel.DefaultPivotZ)
            : new LookCamera(Lifestone.Client.Session.Scenarios.HuntUiShotPlan.FramedScale, 0f, Lifestone.Client.Camera.ChaseCameraModel.DefaultPivotZ))
        : new LookCamera(CameraScale, CameraHeadingDegrees, ClosePivotZ);

    /// <summary>
    /// The turn the camera is given for a frame: the view's own heading plus how far the player's body is turned from the frame the camera follows (<c>AvatarFacing.TurnDegrees</c> of the drawn frame from the camera's, plus the body's own turn in the stance against the same step's peace frame: 0 when neither moved): the camera is placed from the player's physics frame, so a stance that turns the drawn body (the frame faced the back in the mage's magic stance, 4.1 section 8) must turn the camera by the same angle to keep the body's front in view.
    /// </summary>
    /// <param name="headingDegrees">The view's heading.</param>
    /// <param name="bodyTurnDegrees">The body's turn from the camera's frame, in degrees (clockwise positive).</param>
    public static float AimDegrees(float headingDegrees, float bodyTurnDegrees) => Lifestone.Client.Camera.AvatarFacing.Wrap(headingDegrees + bodyTurnDegrees);

    /// <summary>
    /// True when the camera follows how the stance turns the drawn body (the close view: every frame faces the body's front three-quarter). The hunt view does not: it shows the camera the hunt uses, which is placed from the player's heading
    /// whatever the body does.
    /// </summary>
    /// <param name="view">The view.</param>
    public static bool FollowsBody(LookView view) => view == LookView.Close;

    /// <summary>The view named <paramref name="name"/> (<c>close</c> or <c>hunt</c>, case-insensitive), or null.</summary>
    /// <param name="name">The option's value.</param>
    public static LookView? ParseView(string name) => name.ToLowerInvariant() switch
    {
        "close" => LookView.Close,
        "hunt" => LookView.Hunt,
        _ => null,
    };

    /// <summary>The seconds a frame waits after its operation finished before the shot is taken (the parent event, the stance's animation and the first drawn frame).</summary>
    public const double SettleSeconds = 1.5;

    /// <summary>The seconds frame B waits after the combat mode is confirmed, so the stance's transition has played.</summary>
    public const double StanceSettleSeconds = 2.5;

    /// <summary>The seconds one operation may take before the run notes it failed and goes on.</summary>
    public const double OperationSeconds = 20.0;

    /// <summary>The weenie class id of ACE's Mace (M4 4.1 fix2b: the real wcid, so the stand-in draws its real Setup from <c>StandInWeenieArt</c>; the stand-in still has no weenie database, <c>TODO(m3-standin-weenies)</c>, so its kind is a hint).</summary>
    public const uint StandInMaceWcid = 331;

    /// <summary>The weenie class id of ACE's Shortbow (the real wcid, as the mace).</summary>
    public const uint StandInBowWcid = 307;

    /// <summary>The weenie class id of ACE's Arrow (the real wcid, as the mace).</summary>
    public const uint StandInArrowWcid = 300;

    /// <summary>The stack of stand-in arrows the offline run asks for.</summary>
    public const int StandInArrowStack = 5;

    /// <summary>The kinds of the stand-in's placeholder items, which carry no kind in their description (the offline run's hints for <see cref="Build"/>).</summary>
    public static IReadOnlyDictionary<uint, LookKindHint> StandInHints { get; } = new Dictionary<uint, LookKindHint>
    {
        [StandInMaceWcid] = new(LookKind.Melee),
        [StandInBowWcid] = new(LookKind.Launcher),
        [StandInArrowWcid] = new(LookKind.Launcher, IsAmmo: true),
    };

    private const uint TypeMelee = 0x1;
    private const uint TypeMissile = 0x100;

    private LookShotsPlan(IReadOnlyList<LookStep> steps, IReadOnlyList<LookItem> startHands, uint startMode, IReadOnlyList<LookOp> ops, IReadOnlyList<string> notes)
    {
        Steps = steps;
        StartHands = startHands;
        StartMode = startMode;
        Ops = ops;
        Notes = notes;
    }

    /// <summary>One step for each held-item kind the character has, in the order melee, launcher, caster.</summary>
    public IReadOnlyList<LookStep> Steps { get; }

    /// <summary>What was in the hands at the start, in the order it is put back (weapons and casters first, the ammunition last).</summary>
    public IReadOnlyList<LookItem> StartHands { get; }

    /// <summary>The combat mode (<see cref="CombatModes"/>) the run began in; 0 when the session had not said.</summary>
    public uint StartMode { get; }

    /// <summary>The operations, in order: every step's, then the put-back.</summary>
    public IReadOnlyList<LookOp> Ops { get; }

    /// <summary>What the plan could not do, one line each (a launcher with no ammunition, an item whose kind the description does not say); the run logs them.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>The file name of a frame: <c>&lt;step&gt;-&lt;a|b|c&gt;.png</c>.</summary>
    public static string FileName(string step, char frame) => string.Create(CultureInfo.InvariantCulture, $"{step}-{frame}.png");

    /// <summary>The log phrase for a run with nothing to wield.</summary>
    public const string NothingText = "look shots: the character carries no melee weapon, launcher or caster; nothing to capture";

    /// <summary>The wording of the run's verdict line: <c>look shots done frames=N failures=F steps=S</c>.</summary>
    public const string VerdictText = "look shots done";

    /// <summary>The verdict line of a run.</summary>
    public static string Verdict(int frames, int failures, int steps) => string.Create(CultureInfo.InvariantCulture, $"{VerdictText} frames={frames} failures={failures} steps={steps}");

    /// <summary>
    /// Makes the plan from the session's state: one step for each kind of held item the character carries (in the pack, a side pack or the hands; the one in the hands first), and the operations. The kind of an item is
    /// what its description says (<c>CombatUse</c>, <c>ValidLocations</c>, the <c>ItemType</c> bits: retail's <c>GetDefaultCombatMode</c> reads the same), else <paramref name="hints"/> names it, else the item is not a step and is noted.
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="hints">Kinds for items whose description carries none (the stand-in's placeholders), keyed by weenie class id; null for none.</param>
    public static LookShotsPlan Build(GameState state, IReadOnlyDictionary<uint, LookKindHint>? hints = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var notes = new List<string>();
        var startHands = new List<LookItem>();
        for (var i = 0; i < inventory.Equipped.Count; i++)
        {
            var worn = inventory.Equipped[i];
            if ((worn.Location & HandSlots) != 0)
            {
                startHands.Add(ItemOf(state, worn.Guid, worn.Location));
            }
        }

        // The hands go back weapons and casters first, the ammunition last (a launcher takes its ammunition after it).
        startHands.Sort((a, b) => ((a.Slot & (uint)EquipMask.MissileAmmo) != 0 ? 1 : 0).CompareTo((b.Slot & (uint)EquipMask.MissileAmmo) != 0 ? 1 : 0));

        // The candidates in the order the character has them: the hands first, then the pack, then the side packs.
        var order = new List<uint>();
        for (var i = 0; i < inventory.Equipped.Count; i++)
        {
            order.Add(inventory.Equipped[i].Guid);
        }

        for (var i = 0; i < inventory.Pack.Count; i++)
        {
            order.Add(inventory.Pack[i].Guid);
        }

        for (var p = 0; p < inventory.SidePacks.Count; p++)
        {
            var inside = inventory.ItemsIn(inventory.SidePacks[p].Guid);
            for (var i = 0; i < inside.Count; i++)
            {
                order.Add(inside[i].Guid);
            }
        }

        LookItem? melee = null;
        LookItem? launcher = null;
        LookItem? caster = null;
        LookItem? ammo = null;
        foreach (var guid in order)
        {
            if (!state.Objects.TryGet(guid, out var obj))
            {
                continue;
            }

            var kind = Classify(obj, hints, out var slot, out var isAmmo, out var note);
            if (note is not null && !notes.Contains(note))
            {
                notes.Add(note);
            }

            if (kind is null)
            {
                continue;
            }

            var worn = inventory.TryGetEquipped(guid, out var entry) && (entry.Location & HandSlots) != 0 ? entry.Location : slot;
            var home = inventory.Find(guid);
            var inPack = home.Kind is InventoryPlaceKind.MainPack or InventoryPlaceKind.SidePack or InventoryPlaceKind.InSidePack;
            var item = new LookItem(guid, obj.Weenie.WeenieClassId, obj.Name, worn, home.Kind == InventoryPlaceKind.InSidePack ? home.Container : 0, inPack ? (uint)Math.Max(0, home.Placement) : 0);
            if (isAmmo)
            {
                ammo ??= item;
            }
            else
            {
                switch (kind)
                {
                    case LookKind.Melee:
                        melee ??= item;
                        break;
                    case LookKind.Launcher:
                        launcher ??= item;
                        break;
                    default:
                        caster ??= item;
                        break;
                }
            }
        }

        var steps = new List<LookStep>();
        if (melee is { } m)
        {
            steps.Add(new LookStep("melee", LookKind.Melee, m, null));
        }

        if (launcher is { } l)
        {
            if (ammo is null)
            {
                notes.Add($"launcher {l.Name} (wcid {l.Wcid}) has no ammunition in the pack: its step is taken without arrows");
            }

            steps.Add(new LookStep("missile", LookKind.Launcher, l, ammo));
        }

        if (caster is { } c)
        {
            steps.Add(new LookStep("caster", LookKind.Caster, c, null));
        }

        var startMode = state.Self.Qualities.CombatMode;
        return new LookShotsPlan(steps, startHands, (uint)Math.Max(0, startMode), Operations(steps, startHands, (uint)Math.Max(0, startMode)), notes);
    }

    private static IReadOnlyList<LookOp> Operations(List<LookStep> steps, List<LookItem> startHands, uint startMode)
    {
        var ops = new List<LookOp>();
        var hands = new List<LookItem>(startHands);
        for (var s = 0; s < steps.Count; s++)
        {
            var step = steps[s];
            ops.Add(new LookOp(LookOpKind.Peace, s));
            foreach (var held in hands)
            {
                ops.Add(new LookOp(LookOpKind.Stow, s, held)); // everything in the hands, the step's own item included, so the wield below is a real wield
            }

            hands.Clear();
            ops.Add(new LookOp(LookOpKind.Wield, s, step.Item));
            if (step.Ammo is { } ammo)
            {
                ops.Add(new LookOp(LookOpKind.Wield, s, ammo));
            }

            ops.Add(new LookOp(LookOpKind.Frame, s, null, 'a'));
            ops.Add(new LookOp(LookOpKind.EnterDefaultMode, s));
            ops.Add(new LookOp(LookOpKind.Frame, s, null, 'b'));
            ops.Add(new LookOp(LookOpKind.Peace, s));
            if (step.Ammo is { } spent)
            {
                ops.Add(new LookOp(LookOpKind.Stow, s, spent));
            }

            ops.Add(new LookOp(LookOpKind.Stow, s, step.Item));
            ops.Add(new LookOp(LookOpKind.Frame, s, null, 'c'));
        }

        if (steps.Count > 0)
        {
            ops.Add(new LookOp(LookOpKind.Peace, -1));
            foreach (var held in startHands)
            {
                ops.Add(new LookOp(LookOpKind.Wield, -1, held)); // the start state put back
            }

            if (startMode is not (0 or (uint)CombatModes.NonCombat))
            {
                ops.Add(new LookOp(LookOpKind.RestoreMode, -1));
            }
        }

        return ops;
    }

    private static LookItem ItemOf(GameState state, uint guid, uint slot) =>
        state.Objects.TryGet(guid, out var obj) ? new LookItem(guid, obj.Weenie.WeenieClassId, obj.Name, slot) : new LookItem(guid, 0, "?", slot);

    /// <summary>
    /// The kind of a held item and the slot it is wielded at, or null for an item that is not a weapon or a caster. Ammunition comes back with <paramref name="isAmmo"/> set. An item whose description says nothing
    /// (no CombatUse, no ValidLocations, an ItemType that does not name a weapon) is not a guess: <paramref name="note"/> names it, unless the hints do.
    /// </summary>
    internal static LookKind? Classify(WorldObject obj, IReadOnlyDictionary<uint, LookKindHint>? hints, out uint slot, out bool isAmmo, out string? note)
    {
        note = null;
        isAmmo = false;
        slot = 0;
        if (hints is not null && hints.TryGetValue(obj.Weenie.WeenieClassId, out var hint))
        {
            isAmmo = hint.IsAmmo;
            slot = isAmmo ? (uint)EquipMask.MissileAmmo : hint.Kind switch
            {
                LookKind.Melee => (uint)EquipMask.MeleeWeapon,
                LookKind.Launcher => (uint)EquipMask.MissileWeapon,
                _ => (uint)EquipMask.Held,
            };
            return hint.Kind;
        }

        var valid = obj.Weenie.ValidLocations ?? 0;
        var use = obj.CombatUse;
        var type = obj.ItemType;
        if (use == 3 || (valid != 0 && (valid & ~(uint)EquipMask.MissileAmmo) == 0))
        {
            isAmmo = true;
            slot = (uint)EquipMask.MissileAmmo;
            return LookKind.Launcher;
        }

        if (use == 2 || (valid & (uint)EquipMask.MissileWeapon) != 0)
        {
            slot = (uint)EquipMask.MissileWeapon;
            return LookKind.Launcher;
        }

        if ((type & CombatModeRules.ItemTypeCaster) != 0)
        {
            slot = (uint)EquipMask.Held;
            return LookKind.Caster;
        }

        if (use is 1 or 5 || (valid & (uint)(EquipMask.MeleeWeapon | EquipMask.TwoHanded)) != 0)
        {
            slot = (valid & (uint)EquipMask.MeleeWeapon) == 0 && (valid & (uint)EquipMask.TwoHanded) != 0 ? (uint)EquipMask.TwoHanded : (uint)EquipMask.MeleeWeapon;
            return LookKind.Melee;
        }

        if (use is null && valid == 0 && (type & (TypeMelee | TypeMissile)) != 0)
        {
            note = $"{obj.Name} (wcid {obj.Weenie.WeenieClassId}) has ItemType 0x{type:X} and no CombatUse or ValidLocations: its kind is not read, no step (TODO(m4-look-kind-unknown))";
        }

        return null;
    }

    /// <summary>The combat mode's name for the look line.</summary>
    public static string ModeName(int mode) => mode switch
    {
        0 or CombatModes.NonCombat => "NonCombat",
        CombatModes.Melee => "Melee",
        CombatModes.Missile => "Missile",
        CombatModes.Magic => "Magic",
        _ => "0x" + mode.ToString("X", CultureInfo.InvariantCulture),
    };

    /// <summary>The stance (the low word of a motion command the server sent) as the look line shows it: its retail name and the word, or just the word when the table does not name it.</summary>
    public static string StanceName(ushort lowWord) =>
        MotionCommandTable.TryGetName(lowWord, out var name)
            ? string.Create(CultureInfo.InvariantCulture, $"{name}(0x{lowWord:X4})")
            : string.Create(CultureInfo.InvariantCulture, $"0x{lowWord:X4}");

    /// <summary>
    /// The look line of a frame: <c>look item=&lt;name&gt; wcid=&lt;n&gt; frame=&lt;a|b|c&gt; parent=ok|failed(&lt;location&gt;)|none drawn=yes|no light=on|off|none particles=&lt;n&gt;
    /// stance=&lt;the motion style last sent by the server for the player&gt; mode=&lt;combat mode&gt;</c>; a field the code could not read prints <c>unknown</c>. A launcher's line ends with <c>ammo=...</c>.
    /// </summary>
    public static string Line(in LookFacts f)
    {
        var text = new StringBuilder(160);
        text.Append(CultureInfo.InvariantCulture, $"look item={f.ItemName} wcid={f.Wcid} frame={f.Frame} parent={f.Parent ?? "unknown"} drawn={(f.Drawn is null ? "unknown" : f.Drawn.Value ? "yes" : "no")} light={f.Light ?? "unknown"} particles={(f.Particles is { } n ? n.ToString(CultureInfo.InvariantCulture) : "unknown")} stance={f.Stance ?? "unknown"} mode={f.Mode ?? "unknown"}");
        if (f.Ammo is not null)
        {
            text.Append(" ammo=").Append(f.Ammo);
        }

        return text.ToString();
    }

    /// <summary>The parent phrase of a look line from the avatar's state: <c>ok</c> when the item is held, <c>failed(&lt;location&gt;)</c> when the avatar set refused it (its <c>ParentFailed</c> notice), else <c>none</c>.</summary>
    /// <param name="held">True when the item's body has a parent.</param>
    /// <param name="failureDetail">The detail of the item's <c>ParentFailed</c> notice since its wield, or null.</param>
    public static string ParentPhrase(bool held, string? failureDetail)
    {
        if (held)
        {
            return "ok";
        }

        if (failureDetail is null)
        {
            return "none";
        }

        const string marker = "holding location ";
        var at = failureDetail.LastIndexOf(marker, StringComparison.Ordinal);
        var location = at < 0 ? string.Empty : failureDetail[(at + marker.Length)..].Trim();
        return "failed(" + (location.Length == 0 ? "?" : location) + ")";
    }
}

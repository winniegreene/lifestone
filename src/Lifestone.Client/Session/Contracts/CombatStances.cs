// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Creature_Combat.cs (GetCombatStance 265-285, GetWeaponStance 291-340, AddShieldStance 346-358) and Player_Inventory.cs (TryShuffleStance 252-290, RequiresStanceSwap 461-480) (AGPL-3.0)
// [with changes: it reads the client's read model (GameState) instead of the server's weenie properties, so a launcher is taken for a bow and a one-handed melee-slot weapon for a sword (TODO(m4-standin-combat-style)); the swap is returned as a mode, not run].
namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The motion stance a wielded setup stands in, and the combat mode a weapon swap moves to (M4 4.1 lane v3). Pure over the read model (<see cref="GameState"/>: the equipped list and the object table), so the offline stand-in
/// and the tests share the rule that ACE's server applies and the client only follows (the stance arrives in the player's UpdateMotion, the mode in the private int update 0x02CD for property 40).
/// </summary>
/// <remarks>
/// <para><b>The stance</b> is ACE's <c>Creature.GetCombatStance</c> (<c>Creature_Combat.cs</c> 265-285 with <c>GetWeaponStance</c> 291-340 and <c>AddShieldStance</c> 346-358, ACE 95ef426d): a wielded caster (the held slot) gives Magic whatever the mode
/// asked for; else the main-hand weapon's <c>DefaultCombatStyle</c> gives its stance (one-handed SwordCombat, two-handed TwoHandedSwordCombat, a bow BowCombat, a crossbow CrossbowCombat, an atlatl AtlatlCombat, thrown weapons ThrownWeaponCombat),
/// an off-hand weapon that is not a shield gives DualWieldCombat, a shield turns SwordCombat into SwordShieldCombat and ThrownWeaponCombat into ThrownShieldCombat; HandCombat only with nothing in the hands. Only the peace mode is
/// NonCombat. The rule reads the weapon, not the mode: a melee mode asked for with a wand wielded still gets Magic from ACE (the mismatch <see cref="CombatModeGuard"/> keeps the client from creating).</para>
/// <para><b>What the client knows.</b> ACE's <c>DefaultCombatStyle</c> is an appraisal property; the CreateObject carries the weenie description only (ItemType, CombatUse, the slot it is wielded in). Lifestone reads the style from those: a
/// weapon in the melee slot is one-handed, one in the two-handed slot (or with CombatUse 5) two-handed, one in the missile slot a bow, or thrown weapons when the description says it stacks; a crossbow, an atlatl and a sling cannot be told from a bow
/// that way, so the stand-in's launcher is a bow and the rule says so (<see cref="StyleTodo"/>, <c>assumed</c>). The same gap names the unarmed weapon: ACE gives HandCombat to a weapon whose <c>DefaultCombatStyle</c> is Unarmed (<c>GetWeaponStance</c>, <c>Creature_Combat.cs</c> 335-337), which the description cannot say, so a one-handed melee-slot weapon is always SwordCombat here (<c>TODO(m4-standin-combat-style)</c>, silent in the log because <c>assumed</c> is only the launcher's). Live, ACE sends the real stance and this class is only the tests' and the stand-in's.</para>
/// <para><b>The swap.</b> ACE moves a player in a combat mode to the new weapon's mode when a weapon is wielded (<c>TryShuffleStance</c>, <c>Player_Inventory.cs</c> 252-290, called after the wield event at 311 and 1835) and, when a weapon is
/// unwielded, to melee (hand combat with empty hands) or peace for the arrows of a missile stance (<c>TryDequipObjectWithNetworking</c> 438-452, <c>RequiresStanceSwap</c> 461-480); the change then runs through
/// <c>HandleActionChangeCombatMode</c> (<c>Player_Combat.cs</c> 737-886), which puts the player in peace when the weapons do not fit the mode (<see cref="Settle"/>). Retail's own client never decides a swap: its
/// <c>ClientCombatSystem::GetDefaultCombatMode</c> (11.6096 0x0056C050) is called by <c>ToggleCombatMode</c> (0x0056D610) and <c>OnAction</c> (0x0056E640) only, and <c>OnQualityChanged</c> (0x0056CF00) follows the server's property 40. What retail's
/// server does on a wield is not read (the retail server is not in the decompile): <c>TODO(m4-retail-wield-stance)</c>, ACE's rule stands in.</para>
/// </remarks>
public static class CombatStances
{
    /// <summary>The TODO named when the stance had to assume a launcher is a bow (a crossbow, an atlatl or a sling has the same description).</summary>
    public const string StyleTodo = "TODO(m4-standin-combat-style)";

    /// <summary>MotionStance.HandCombat (unarmed).</summary>
    public const uint HandCombat = 0x8000003C;

    /// <summary>MotionStance.NonCombat (peace).</summary>
    public const uint NonCombat = 0x8000003D;

    /// <summary>MotionStance.SwordCombat (a one-handed weapon).</summary>
    public const uint SwordCombat = 0x8000003E;

    /// <summary>MotionStance.BowCombat.</summary>
    public const uint BowCombat = 0x8000003F;

    /// <summary>MotionStance.SwordShieldCombat.</summary>
    public const uint SwordShieldCombat = 0x80000040;

    /// <summary>MotionStance.TwoHandedSwordCombat (ACE gives every two-handed weapon this stance).</summary>
    public const uint TwoHandedSwordCombat = 0x80000044;

    /// <summary>MotionStance.DualWieldCombat.</summary>
    public const uint DualWieldCombat = 0x80000046;

    /// <summary>MotionStance.ThrownWeaponCombat.</summary>
    public const uint ThrownWeaponCombat = 0x80000047;

    /// <summary>MotionStance.Magic.</summary>
    public const uint Magic = 0x80000049;

    /// <summary>MotionStance.ThrownShieldCombat.</summary>
    public const uint ThrownShieldCombat = 0x8000013C;

    private const uint MeleeSlot = (uint)EquipMask.MeleeWeapon;
    private const uint MissileSlot = (uint)EquipMask.MissileWeapon;
    private const uint TwoHandedSlot = (uint)EquipMask.TwoHanded;
    private const uint HeldSlot = (uint)EquipMask.Held;
    private const uint ShieldSlot = (uint)EquipMask.Shield;
    private const uint AmmoSlot = (uint)EquipMask.MissileAmmo;
    private const uint SelectableSlots = MeleeSlot | MissileSlot | TwoHandedSlot | HeldSlot | ShieldSlot;
    private const uint ArmourType = 0x2;
    private const sbyte CombatUseShield = 4;
    private const sbyte CombatUseTwoHanded = 5;

    /// <summary>The ACE name of a stance (<c>MotionStance</c>), for logs and test failures.</summary>
    public static string Name(uint stance) => stance switch
    {
        HandCombat => "HandCombat",
        NonCombat => "NonCombat",
        SwordCombat => "SwordCombat",
        BowCombat => "BowCombat",
        SwordShieldCombat => "SwordShieldCombat",
        TwoHandedSwordCombat => "TwoHandedSwordCombat",
        DualWieldCombat => "DualWieldCombat",
        ThrownWeaponCombat => "ThrownWeaponCombat",
        Magic => "Magic",
        ThrownShieldCombat => "ThrownShieldCombat",
        _ => "0x" + stance.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// <c>GetCombatStance</c> for what the player wields in <paramref name="state"/>: Magic for a caster, else the main-hand weapon's stance (a shield or a second weapon changing it), HandCombat with nothing in the hands. Never NonCombat:
    /// the peace mode is the caller's. <paramref name="assumed"/> is true when a launcher was taken for a bow (<see cref="StyleTodo"/>).
    /// </summary>
    public static uint Of(GameState state, out bool assumed)
    {
        ArgumentNullException.ThrowIfNull(state);
        assumed = false;
        var equipped = state.Self.Inventory.Equipped;
        EquippedItem? melee = null;
        EquippedItem? missile = null;
        EquippedItem? offHand = null;
        for (var i = 0; i < equipped.Count; i++)
        {
            var entry = equipped[i];
            if ((entry.Location & HeldSlot) != 0)
            {
                return Magic; // GetEquippedWand: anything in the held slot
            }

            if ((entry.Location & (MeleeSlot | TwoHandedSlot)) != 0)
            {
                melee ??= entry;
            }
            else if ((entry.Location & MissileSlot) != 0)
            {
                missile ??= entry;
            }
            else if ((entry.Location & ShieldSlot) != 0)
            {
                offHand ??= entry;
            }
        }

        var stance = HandCombat;
        if (melee is { } main)
        {
            var twoHanded = (main.Location & TwoHandedSlot) != 0 || (state.Objects.TryGet(main.Guid, out var weapon) && weapon.CombatUse == CombatUseTwoHanded);
            stance = twoHanded ? TwoHandedSwordCombat : SwordCombat;
        }
        else if (missile is { } launcher)
        {
            if (state.Objects.TryGet(launcher.Guid, out var item) && item.Weenie.MaxStackSize is > 1)
            {
                stance = ThrownWeaponCombat; // thrown weapons stack in the missile slot (GetEquippedMissileWeapon: "stackable thrown weapons directly in the main hand slot")
            }
            else
            {
                stance = BowCombat;
                assumed = true;
            }
        }

        if (offHand is { } second)
        {
            if (!IsShield(state, second))
            {
                return DualWieldCombat;
            }

            stance = stance switch { SwordCombat => SwordShieldCombat, ThrownWeaponCombat => ThrownShieldCombat, _ => stance };
        }

        return stance;
    }

    /// <summary><see cref="Of(GameState, out bool)"/> without the assumption flag.</summary>
    public static uint Of(GameState state) => Of(state, out _);

    /// <summary>The stance the player's avatar shows for <paramref name="mode"/> (<see cref="CombatModes"/>): NonCombat in peace, else <see cref="Of(GameState, out bool)"/>.</summary>
    public static uint ForMode(int mode, GameState state, out bool assumed)
    {
        assumed = false;
        return mode is 0 or CombatModes.NonCombat ? NonCombat : Of(state, out assumed);
    }

    /// <summary>
    /// <c>HandleActionChangeCombatMode</c>'s revert (<c>Player_Combat.cs</c> 745-760 and the inner switch 812-882): the mode the server ends in when <paramref name="requested"/> is asked of a player who wields what <paramref name="state"/> says. A melee
    /// mode with a caster or launcher, a missile mode without a launcher and a magic mode without a caster end in peace (<see cref="CombatModeGuard"/> is the same table); anything else is as asked.
    /// </summary>
    public static CombatMode Settle(CombatMode requested, GameState state) => CombatModeGuard.Check(requested, state) is null ? requested : CombatMode.NonCombat;

    /// <summary>
    /// <c>TryShuffleStance</c> (<c>Player_Inventory.cs</c> 252-290): the mode ACE asks for after <paramref name="location"/> was wielded while the player is in <paramref name="currentMode"/>, or null for none (peace, or a slot that is not a
    /// weapon slot). The missile slot gives missile mode, the held slot magic, the off hand missile for a thrown weapon and melee otherwise, every other weapon slot melee.
    /// </summary>
    public static CombatMode? ModeAfterWield(uint location, int currentMode, GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if ((location & SelectableSlots) == 0 || currentMode is 0 or CombatModes.NonCombat)
        {
            return null;
        }

        return location switch
        {
            MissileSlot => CombatMode.Missile,
            HeldSlot => CombatMode.Magic,
            ShieldSlot => Of(state) is ThrownWeaponCombat or ThrownShieldCombat ? CombatMode.Missile : CombatMode.Melee,
            _ => CombatMode.Melee,
        };
    }

    /// <summary>
    /// <c>TryDequipObjectWithNetworking</c>'s stance swap (<c>Player_Inventory.cs</c> 436-452, <c>RequiresStanceSwap</c> 461-480): the mode ACE asks for after <paramref name="location"/> was unwielded while the player is in
    /// <paramref name="currentMode"/>, with <paramref name="state"/> read after the item left. Melee (empty hands give HandCombat), peace for the arrows of a missile mode, missile when a thrown weapon is still wielded; null when no swap is due.
    /// </summary>
    public static CombatMode? ModeAfterUnwield(uint location, int currentMode, GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (currentMode is 0 or CombatModes.NonCombat)
        {
            return null;
        }

        if (currentMode == CombatModes.Missile && (location & AmmoSlot) != 0)
        {
            return CombatMode.NonCombat;
        }

        if ((location & SelectableSlots) == 0)
        {
            return null;
        }

        return Of(state) is ThrownWeaponCombat or ThrownShieldCombat ? CombatMode.Missile : CombatMode.Melee;
    }

    private static bool IsShield(GameState state, EquippedItem entry) =>
        state.Objects.TryGet(entry.Guid, out var item) && (item.CombatUse == CombatUseShield || (item.CombatUse is null && (item.ItemType & ArmourType) != 0));
}

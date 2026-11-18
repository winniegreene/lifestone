using System.Globalization;

namespace Lifestone.Client.Session.Contracts;

/// <summary>Why <see cref="CombatModeGuard"/> refuses a combat mode (M4 3.1 g).</summary>
public enum CombatModeProblem
{
    /// <summary>The mode fits what the player wields.</summary>
    None,

    /// <summary>The value is none of the four <see cref="CombatMode"/> values.</summary>
    NotAMode,

    /// <summary>A wielded weapon's object is not in the table, so its kind is unknown: the answer is a refusal, never a silent allow.</summary>
    CannotCheck,

    /// <summary>A melee mode with a caster (a wand, a staff, an orb) wielded.</summary>
    MeleeWithCaster,

    /// <summary>A melee mode with a missile launcher (a bow, a crossbow, an atlatl, a thrown weapon) wielded.</summary>
    MeleeWithLauncher,

    /// <summary>A magic mode with no caster wielded.</summary>
    MagicWithoutCaster,

    /// <summary>A missile mode with no missile launcher wielded.</summary>
    MissileWithoutLauncher,
}

/// <summary>What the player wields in the weapon slots, sorted by kind (<see cref="CombatModeGuard.Read"/>): the first caster, the first missile launcher, the first other weapon, and the first wielded item the object table lacks.</summary>
/// <param name="Caster">A wielded wand, staff or orb, or null.</param>
/// <param name="Launcher">A wielded bow, crossbow, atlatl or thrown weapon, or null.</param>
/// <param name="Other">A wielded weapon that is neither (a mace, a sword, a two-handed weapon), or null.</param>
/// <param name="Unknown">The guid of a wielded weapon-slot item whose object the table lacks (its kind cannot be read), or 0.</param>
public readonly record struct WieldedWeapons(WorldObject? Caster, WorldObject? Launcher, WorldObject? Other, uint Unknown);

/// <summary>
/// Lifestone's combat-mode guard (M4 3.1 g; an owner request: a character never goes into melee mode while it wields a wand or an orb, and never into magic or missile mode without one). It answers whether a combat mode fits
/// what the player wields, from the read model alone (<see cref="GameState"/>: the equipped list and the object table), pure and allocation-free on the allowed path (the refusal text is built only for a refusal).
/// </summary>
/// <remarks>
/// <para><b>The rules</b>, from ACE and retail. ACE's <c>Player.HandleActionChangeCombatMode_Inner</c> (<c>Player_Combat.cs</c> 778-884, ACE 95ef426d) puts the player back in peace when a melee mode is asked for while a missile
/// weapon or a caster is wielded (812-822), when a missile mode is asked for without a missile weapon (824-835) and when a magic mode is asked for without a caster (872-882); <c>HandleActionChangeCombatMode</c> (737-760) first runs
/// <c>CheckWeaponCollision</c> (<c>Player_Inventory.cs:1878-2056</c> at ACE 95ef426d, the combat-mode branch from 2009) and answers an illegal pair of wielded weapons with the WeenieError ActionCancelled. ACE finds the caster as the item in the held slot
/// (<c>Creature_Equipment.cs</c> 141-144, <c>GetEquippedWand</c>) and the missile weapon as the item in the missile slot (150-153, <c>GetEquippedMissileWeapon</c>, a launcher or a stack of thrown weapons). Retail's own picker,
/// <c>ClientCombatSystem::GetDefaultCombatMode</c> (11.6096 0x0056C050), never asks for such a mode: it reads the weapon's CombatUse (2 gives missile, else melee) and the held item's caster bit (ItemType 0x8000). So the toolbar
/// cannot reach the refusal; a script or a test can, and the client would draw the wrong stance for a moment before ACE's revert.</para>
/// <para><b>The table.</b> Peace is always allowed. Melee is refused when a caster or a launcher is wielded; with nothing wielded it is allowed (fists). Magic is refused unless a caster is wielded. Missile is refused unless a launcher is
/// wielded (arrows alone are no launcher). A <b>caster</b> is a wielded item whose ItemType has Caster 0x8000 or MagicWieldable 0x200000, or any item in the held slot (0x1000000: ACE's rule). A <b>launcher</b> is an item in the missile
/// slot (0x400000: ACE's rule) or a wielded item of ItemType MissileWeapon 0x100 with CombatUse 2. Only the weapon slots (melee 0x100000, missile 0x400000, held 0x1000000, two-handed 0x2000000) are read: armour, shields and
/// ammunition cannot change the answer. A weapon-slot item whose object the table lacks has no kind to read: the answer is "cannot check: ..." (<see cref="CombatModeProblem.CannotCheck"/>), a loud refusal, never a guess.</para>
/// </remarks>
public static class CombatModeGuard
{
    /// <summary>The ItemType bit of a caster (wand, staff, orb).</summary>
    public const uint ItemTypeCaster = 0x8000;

    /// <summary>The ItemType bit ACE sets on items a magic user wields.</summary>
    public const uint ItemTypeMagicWieldable = 0x200000;

    /// <summary>The ItemType bit of a missile weapon (a launcher, a thrown weapon, and ammunition, which CombatUse 3 tells apart).</summary>
    public const uint ItemTypeMissileWeapon = 0x100;

    /// <summary>The CombatUse of a missile weapon (ACE: 1 Melee, 2 Missile, 3 Ammo, 4 Shield, 5 TwoHanded).</summary>
    public const sbyte CombatUseMissile = 2;

    private const uint WeaponSlots = (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon | EquipMask.Held | EquipMask.TwoHanded);

    /// <summary>The refusal text, or null when <paramref name="mode"/> fits what the player wields in <paramref name="state"/>.</summary>
    /// <param name="mode">The mode asked for.</param>
    /// <param name="state">The read model.</param>
    /// <returns>Null when the mode is allowed, else a text that names the item.</returns>
    public static string? Check(CombatMode mode, GameState state) => Check((int)mode, state);

    /// <summary>The same for a mode held as the property-40 int (<see cref="CombatModes"/>).</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="state">The read model.</param>
    /// <returns>Null when the mode is allowed, else a text that names the item.</returns>
    public static string? Check(int mode, GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (mode == CombatModes.NonCombat)
        {
            return null; // peace always fits, and needs no read
        }

        var wielded = Read(state);
        var problem = Judge(mode, in wielded);
        return problem == CombatModeProblem.None ? null : Describe(problem, mode, in wielded);
    }

    /// <summary>Sorts what the player wields in the weapon slots by kind. Allocation-free.</summary>
    /// <param name="state">The read model.</param>
    /// <returns>The wielded weapons.</returns>
    public static WieldedWeapons Read(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        WorldObject? caster = null;
        WorldObject? launcher = null;
        WorldObject? other = null;
        uint unknown = 0;
        var equipped = state.Self.Inventory.Equipped;
        for (var i = 0; i < equipped.Count; i++)
        {
            var entry = equipped[i];
            if ((entry.Location & WeaponSlots) == 0)
            {
                continue;
            }

            if (!state.Objects.TryGet(entry.Guid, out var item))
            {
                unknown = unknown == 0 ? entry.Guid : unknown;
                continue;
            }

            if ((entry.Location & (uint)EquipMask.Held) != 0 || (item.ItemType & (ItemTypeCaster | ItemTypeMagicWieldable)) != 0)
            {
                caster ??= item;
            }
            else if ((entry.Location & (uint)EquipMask.MissileWeapon) != 0 || ((item.ItemType & ItemTypeMissileWeapon) != 0 && item.CombatUse == CombatUseMissile))
            {
                launcher ??= item;
            }
            else
            {
                other ??= item;
            }
        }

        return new WieldedWeapons(caster, launcher, other, unknown);
    }

    /// <summary>Whether <paramref name="mode"/> fits <paramref name="wielded"/>. Allocation-free.</summary>
    /// <param name="mode">The mode (<see cref="CombatModes"/>).</param>
    /// <param name="wielded">What <see cref="Read"/> found.</param>
    /// <returns><see cref="CombatModeProblem.None"/> or why not.</returns>
    public static CombatModeProblem Judge(int mode, in WieldedWeapons wielded)
    {
        if (mode == CombatModes.NonCombat)
        {
            return CombatModeProblem.None;
        }

        if (mode is not (CombatModes.Melee or CombatModes.Missile or CombatModes.Magic))
        {
            return CombatModeProblem.NotAMode;
        }

        if (wielded.Unknown != 0)
        {
            return CombatModeProblem.CannotCheck;
        }

        return mode switch
        {
            CombatModes.Melee when wielded.Caster is not null => CombatModeProblem.MeleeWithCaster,
            CombatModes.Melee when wielded.Launcher is not null => CombatModeProblem.MeleeWithLauncher,
            CombatModes.Magic when wielded.Caster is null => CombatModeProblem.MagicWithoutCaster,
            CombatModes.Missile when wielded.Launcher is null => CombatModeProblem.MissileWithoutLauncher,
            _ => CombatModeProblem.None,
        };
    }

    /// <summary>The item the problem is about: the caster or launcher a melee mode collides with, else what the player wields instead, or null for nothing (or for a problem that names none).</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="wielded">What <see cref="Read"/> found.</param>
    /// <returns>The item, or null.</returns>
    public static WorldObject? Culprit(CombatModeProblem problem, in WieldedWeapons wielded) => problem switch
    {
        CombatModeProblem.MeleeWithCaster => wielded.Caster,
        CombatModeProblem.MeleeWithLauncher => wielded.Launcher,
        CombatModeProblem.MagicWithoutCaster or CombatModeProblem.MissileWithoutLauncher => wielded.Other ?? wielded.Launcher ?? wielded.Caster,
        _ => null,
    };

    /// <summary>The text of a refusal: names the mode and the item, or what is missing.</summary>
    /// <param name="problem">The problem (not <see cref="CombatModeProblem.None"/>).</param>
    /// <param name="mode">The mode asked for (<see cref="CombatModes"/>).</param>
    /// <param name="wielded">What <see cref="Read"/> found.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentException"><paramref name="problem"/> is <see cref="CombatModeProblem.None"/> (no refusal) or a value with no arm: a named, loud failure (<c>TODO(combat-mode-guard-problem)</c>), never a silent default text.</exception>
    public static string Describe(CombatModeProblem problem, int mode, in WieldedWeapons wielded)
    {
        var item = Culprit(problem, in wielded);
        var name = item is null ? "nothing" : "the " + item.Name;
        return problem switch
        {
            CombatModeProblem.NotAMode => "combat mode " + mode.ToString(CultureInfo.InvariantCulture) + " is not one of 1, 2, 4 or 8",
            CombatModeProblem.CannotCheck => "cannot check the combat mode: the wielded item 0x" + wielded.Unknown.ToString("X8", CultureInfo.InvariantCulture) + " has no object in the table, so its kind is unknown (its CreateObject has not arrived)",
            CombatModeProblem.MeleeWithCaster => "the melee combat mode is refused: the player wields " + name + ", a caster (ACE would answer ActionCancelled and put the player back in peace)",
            CombatModeProblem.MeleeWithLauncher => "the melee combat mode is refused: the player wields " + name + ", a missile launcher (ACE would answer ActionCancelled and put the player back in peace)",
            CombatModeProblem.MagicWithoutCaster => "the magic combat mode is refused: the player wields " + name + ", not a caster (a wand, a staff or an orb)",
            CombatModeProblem.MissileWithoutLauncher => "the missile combat mode is refused: the player wields " + name + ", not a missile launcher (a bow, a crossbow or an atlatl)",
            CombatModeProblem.None => throw new ArgumentException("TODO(combat-mode-guard-problem): Describe was asked for the text of CombatModeProblem.None, which is no refusal", nameof(problem)),
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "TODO(combat-mode-guard-problem): CombatModeProblem " + problem + " has no refusal text; add its arm to CombatModeGuard.Describe"),
        };
    }
}

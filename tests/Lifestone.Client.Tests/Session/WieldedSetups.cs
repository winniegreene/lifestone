using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>One wielded item of a <see cref="WieldedSetups"/> case: the weenie's ItemType and CombatUse as ACE sends them, and the slot it is wielded in.</summary>
/// <param name="Guid">The item's id.</param>
/// <param name="Name">Its name.</param>
/// <param name="ItemType">The weenie description's ItemType.</param>
/// <param name="CombatUse">The weenie description's CombatUse (ACE: 1 Melee, 2 Missile, 3 Ammo, 4 Shield, 5 TwoHanded), or null when none is sent.</param>
/// <param name="Slot">The slot it is wielded in.</param>
/// <param name="Present">False when the object table never gets the item's CreateObject (its kind is unknown).</param>
internal readonly record struct Piece(uint Guid, string Name, uint ItemType, sbyte? CombatUse, EquipMask Slot, bool Present = true);

/// <summary>
/// One wielded setup and what is true of it (M4 3.1 g): whether melee, magic and missile fit it by the owner's rule (<see cref="CombatModeGuard"/>), and the mode the toolbar's
/// <c>GetDefaultCombatMode</c> picks from peace.
/// </summary>
internal sealed record WieldedCase(string Name, Piece[] Pieces, bool Melee, bool Magic, bool Missile, CombatMode Pick)
{
    public override string ToString() => Name;
}

/// <summary>The wielded setups of the combat-mode guard's tests: nothing, a mace, a sword, a bow with arrows, a wand, an orb, a mace with a shield, two-handed, a caster with a shield, and the odd ones.</summary>
internal static class WieldedSetups
{
    private const uint MeleeWeaponType = 0x1;
    private const uint ArmorType = 0x2;
    private const uint MissileType = 0x100;
    private const uint CasterType = 0x8000;

    public static Piece Mace => new(0x80000A01, "Mace", MeleeWeaponType, 1, EquipMask.MeleeWeapon);

    public static Piece Sword => new(0x80000A02, "Sword", MeleeWeaponType, 1, EquipMask.MeleeWeapon);

    public static Piece Bow => new(0x80000A03, "Shortbow", MissileType, 2, EquipMask.MissileWeapon);

    public static Piece Arrows => new(0x80000A04, "Arrow", MissileType, 3, EquipMask.MissileAmmo);

    public static Piece Darts => new(0x80000A05, "Dart", MissileType, 2, EquipMask.MissileWeapon);

    public static Piece Wand => new(0x80000A06, "Wand of Flame", CasterType, null, EquipMask.Held);

    public static Piece Orb => new(0x80000A07, "Crystal Orb", CasterType, null, EquipMask.Held);

    public static Piece Shield => new(0x80000A08, "Kite Shield", ArmorType, 4, EquipMask.Shield);

    public static Piece TwoHanded => new(0x80000A09, "Greatsword", MeleeWeaponType, 5, EquipMask.TwoHanded);

    public static Piece UnknownMace => new(0x80000777, "Mace", MeleeWeaponType, 1, EquipMask.MeleeWeapon, Present: false);

    public static Piece UnknownHelm => new(0x80000778, "Helm", ArmorType, null, EquipMask.HeadWear, Present: false);

    /// <summary>Every case, in the order the tests read them.</summary>
    public static IReadOnlyList<WieldedCase> All { get; } =
    [
        new("nothing", [], true, false, false, CombatMode.Melee),
        new("a mace", [Mace], true, false, false, CombatMode.Melee),
        new("a sword", [Sword], true, false, false, CombatMode.Melee),
        new("a bow with arrows", [Bow, Arrows], false, false, true, CombatMode.Missile),
        new("arrows alone", [Arrows], true, false, false, CombatMode.Melee),
        new("thrown darts", [Darts], false, false, true, CombatMode.Missile),
        new("a wand", [Wand], false, true, false, CombatMode.Magic),
        new("an orb", [Orb], false, true, false, CombatMode.Magic),
        new("a mace with a shield", [Mace, Shield], true, false, false, CombatMode.Melee),
        new("a two-handed weapon", [TwoHanded], true, false, false, CombatMode.Melee),
        new("a wand with a shield", [Wand, Shield], false, true, false, CombatMode.Magic), // ACE refuses to wield the pair; the guard still reads the wand
        new("an unknown mace", [UnknownMace], false, false, false, CombatMode.NonCombat),
        new("an unknown helm", [UnknownHelm], true, false, false, CombatMode.Melee), // armour cannot change the answer
    ];

    /// <summary>The case names, as xUnit theory data.</summary>
    public static IEnumerable<object[]> Names() => All.Select(c => new object[] { c.Name });

    /// <summary>The case named <paramref name="name"/>.</summary>
    public static WieldedCase Find(string name) => All.Single(c => c.Name == name);

    /// <summary>Gives the player the pieces: a PlayerDescription with the equipped list, then the CreateObject of each piece the table should hold.</summary>
    public static void Apply(ReducerHarness harness, params Piece[] pieces)
    {
        ArgumentNullException.ThrowIfNull(harness);
        harness.Reducer.OnPlayerDescription(
            harness.Context,
            new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Equipped = [.. pieces.Select(p => new EquippedDesc(p.Guid, (uint)p.Slot, 0))] });
        foreach (var piece in pieces.Where(p => p.Present))
        {
            Create(harness, piece);
        }
    }

    /// <summary>The piece's CreateObject, as ACE describes a wielded item.</summary>
    public static void Create(ReducerHarness harness, Piece piece)
    {
        var basis = ReducerHarness.Create(piece.Guid, ReducerHarness.Stamps(), null, name: piece.Name, itemType: piece.ItemType);
        var weenie = piece.CombatUse is { } use ? basis.Weenie with { CombatUse = use, Header = basis.Weenie.Header | WeenieHeaderFlags.CombatUse } : basis.Weenie;
        harness.Reducer.OnCreateObject(harness.Context, basis with { Weenie = weenie }, isUpdate: false);
    }

    /// <summary>
    /// Puts <paramref name="piece"/> in the player's hand of an offline session through its reducer, as ACE's CreateObject and GameEvent 0x0023 would (the read model only: the stand-in's own world is not asked). For the
    /// rows of the stand-in suites that enter the magic mode and need the caster the combat-mode guard wants (M4 3.1 g); a piece already wielded is left alone.
    /// </summary>
    public static void Hold(GameSession session, Piece piece)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State.Self.Inventory.EquippedAt((uint)piece.Slot) is not null)
        {
            return;
        }

        var context = new InboundContext(0, MessageQueue.SmartBox, 0);
        var basis = ReducerHarness.Create(piece.Guid, ReducerHarness.Stamps(), null, name: piece.Name, itemType: piece.ItemType);
        var weenie = piece.CombatUse is { } use ? basis.Weenie with { CombatUse = use, Header = basis.Weenie.Header | WeenieHeaderFlags.CombatUse } : basis.Weenie;
        session.Reducer.OnCreateObject(context, basis with { Weenie = weenie }, isUpdate: false);
        session.Reducer.OnWieldObject(context, new GameEventHeader(session.State.Self.Guid, 0, GameEventTypes.WieldObject), new WieldObjectEvent(piece.Guid, (uint)piece.Slot));
    }

    /// <summary>A world with the player in it and the pieces wielded.</summary>
    public static ReducerHarness World(params Piece[] pieces)
    {
        var harness = new ReducerHarness(track: false);
        harness.EnterWorld();
        Apply(harness, pieces);
        return harness;
    }
}

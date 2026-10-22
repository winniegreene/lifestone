using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// <see cref="FighterItems"/> (M4 3.1 fix2): a lookup by weenie class prefers an equipped instance over a pack copy that the held list names first (<c>GearInventory.Collect</c> lists the pack before the equipped items),
/// chooses among pack instances by the preferred guid, and reads the wielded launcher and ammunition by the read model's equipped list. The live round 4 swap waited for ever on the first-listed copy.
/// </summary>
public sealed class FighterItemsTests
{
    private const uint PackBow = 0x80000B01;
    private const uint PackBowTwo = 0x80000B02;
    private const uint WieldedBow = 0x80000B03;
    private const uint PackArrows = 0x80000B04;
    private const uint WieldedArrows = 0x80000B05;
    private const uint PackMace = 0x80000B06;
    private const uint WieldedMace = 0x80000B07;

    private static ReducerHarness World((uint Guid, uint Wcid)[] pack, (uint Guid, uint Wcid, EquipMask Slot)[] worn)
    {
        var harness = new ReducerHarness(track: false);
        harness.EnterWorld();
        harness.Reducer.OnPlayerDescription(
            harness.Context,
            new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg
            {
                Inventory = [.. pack.Select(p => new ContentDesc(p.Guid, 0))],
                Equipped = [.. worn.Select(w => new EquippedDesc(w.Guid, (uint)w.Slot, 0))],
            });
        foreach (var (guid, wcid) in pack.Concat(worn.Select(w => (w.Guid, w.Wcid))))
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Kit " + wcid);
            harness.Reducer.OnCreateObject(harness.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = wcid } }, isUpdate: false);
        }

        return harness;
    }

    [Fact]
    public void An_equipped_instance_wins_over_a_pack_copy_listed_before_it()
    {
        var world = World(
            [(PackBow, FighterKit.ShortbowWcid), (PackBowTwo, FighterKit.ShortbowWcid)],
            [(WieldedBow, FighterKit.ShortbowWcid, EquipMask.MissileWeapon)]);
        var scratch = new List<uint>();

        Assert.True(FighterItems.Find(world.State, scratch, FighterKit.ShortbowWcid, out var guid, out var equipped));

        Assert.Equal(WieldedBow, guid);
        Assert.True(equipped);
        Assert.Equal([PackBow, PackBowTwo, WieldedBow], scratch); // the held list does name the pack copies first: the old lookup returned PackBow
    }

    [Fact]
    public void With_nothing_equipped_a_pack_instance_is_chosen_the_preferred_one_when_it_is_held_else_the_first()
    {
        var world = World([(PackBow, FighterKit.ShortbowWcid), (PackBowTwo, FighterKit.ShortbowWcid), (PackMace, FighterKit.MaceWcid)], []);
        var scratch = new List<uint>();

        Assert.True(FighterItems.Find(world.State, scratch, FighterKit.ShortbowWcid, out var first, out var firstEquipped));
        Assert.Equal((PackBow, false), (first, firstEquipped));

        Assert.True(FighterItems.Find(world.State, scratch, FighterKit.ShortbowWcid, out var preferred, out var preferredEquipped, prefer: PackBowTwo));
        Assert.Equal((PackBowTwo, false), (preferred, preferredEquipped));

        Assert.True(FighterItems.Find(world.State, scratch, FighterKit.ShortbowWcid, out var other, out _, prefer: PackMace)); // a guid of another class is not a pack instance of this one
        Assert.Equal(PackBow, other);

        Assert.True(FighterItems.Find(world.State, scratch, FighterKit.ShortbowWcid, out var unheld, out _, prefer: 0x80000FFF));
        Assert.Equal(PackBow, unheld);

        Assert.False(FighterItems.Find(world.State, scratch, FighterKit.ArrowWcid, out var none, out var noneEquipped));
        Assert.Equal((0u, false), (none, noneEquipped));
    }

    [Fact]
    public void The_equipped_one_wins_even_over_the_preferred_pack_instance()
    {
        var world = World([(PackMace, FighterKit.MaceWcid)], [(WieldedMace, FighterKit.MaceWcid, EquipMask.MeleeWeapon)]);

        Assert.True(FighterItems.Find(world.State, [], FighterKit.MaceWcid, out var guid, out var equipped, prefer: PackMace));

        Assert.Equal((WieldedMace, true), (guid, equipped));
    }

    [Fact]
    public void The_wielded_launcher_and_ammunition_are_read_from_the_equipped_list_and_a_pack_copy_is_neither()
    {
        var packOnly = World([(PackBow, FighterKit.ShortbowWcid), (PackArrows, FighterKit.ArrowWcid)], [(WieldedMace, FighterKit.MaceWcid, EquipMask.MeleeWeapon)]);
        var scratch = new List<uint>();
        Assert.Null(FighterItems.WieldedLauncher(packOnly.State, scratch));
        Assert.Equal(0u, FighterItems.WieldedAmmunition(packOnly.State));

        var wielding = World(
            [(PackBow, FighterKit.ShortbowWcid), (PackArrows, FighterKit.ArrowWcid)],
            [(WieldedBow, FighterKit.ShortbowWcid, EquipMask.MissileWeapon), (WieldedArrows, FighterKit.ArrowWcid, EquipMask.MissileAmmo)]);
        Assert.Equal(WieldedBow, FighterItems.WieldedLauncher(wielding.State, scratch)?.Guid);
        Assert.Equal(WieldedArrows, FighterItems.WieldedAmmunition(wielding.State));
        Assert.Equal(WieldedArrows, FighterItems.WieldedAmmunition(wielding.State, prefer: WieldedArrows));
        Assert.Equal(WieldedArrows, FighterItems.WieldedAmmunition(wielding.State, prefer: PackArrows)); // a pack stack is no wielded one: the wielded stack is still found
    }
}

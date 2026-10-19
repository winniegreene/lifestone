using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Appearance;
using Lifestone.Net.Types;
using AnimPartChange = Lifestone.Data.Appearance.AnimPartChange;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 4.2 p6 follow-up: the stand-in knows BSD Mage's kit (D43: The Healer's Heart 8552, the Olthoi Helm, the Amuli coat and leggings, the platemail gauntlets and sollerets), carries the palette and shade of an <c>item</c>
/// request into the piece, and sends the player's ObjDescEvent (F625) when a worn piece comes on or off, as ACE does (<c>Creature_Equipment.cs</c> 365, 438).
/// </summary>
public sealed class StandInKitLookTests
{
    private sealed class FakeDresser : IStandInDresser
    {
        public List<StandInWorn> Items { get; } = [];

        public List<IReadOnlyList<StandInWorn>> Players { get; } = [];

        public ObjDesc Player(IReadOnlyList<StandInWorn> worn)
        {
            Players.Add([.. worn]);
            return ObjDesc.FromWire(0x04000001, [new WireSubPalette(0x04000002, 8, 8)], [], [new AnimPartChange((byte)(worn.Count + 1), 0x01000100)]);
        }

        public ObjDesc Item(StandInWorn piece)
        {
            Items.Add(piece);
            return ObjDesc.Empty;
        }
    }

    [Fact]
    public void The_stand_in_knows_the_six_kit_pieces_by_setup_and_clothing_base()
    {
        var weenies = StandInWorld.KitWeenies;
        (uint Wcid, uint Setup, uint Base)[] kit =
        [
            (8552, 0x020009AD, 0x10000249), (3715, 0x020000DA, 0x1000002C), (6600, 0x02000126, 0x100001A1),
            (6606, 0x02000128, 0x100001A0), (57, 0x020000D8, 0x10000011), (107, 0x020000DE, 0x10000054),
        ];
        foreach (var (wcid, setup, clothing) in kit)
        {
            Assert.True(weenies.TryGetValue(wcid, out var weenie), wcid.ToString());
            Assert.Equal(setup, weenie.SetupId);
            Assert.Equal(clothing, weenie.ClothingBase);
        }

        Assert.Equal(13, weenies[3715].Palette);
        Assert.Equal(0.66, weenies[3715].Shade);
        Assert.Equal(21, weenies[6600].Palette);
        Assert.Equal(18, weenies[6606].Palette);
        Assert.Equal(StandInWorld.WandSetup, weenies[GearKit.Wand.Wcid].SetupId);
    }

    [Fact]
    public void A_requested_palette_and_shade_reach_the_piece_and_a_wield_or_unwield_sends_the_players_ObjDescEvent()
    {
        var dresser = new FakeDresser();
        using var rig = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Dresser = dresser });
        rig.Begin();
        rig.Run(2.0);
        var descs = new List<ObjectDescChanged>();
        using var watch = rig.Session.Events.Subscribe((in ObjectDescChanged e) => descs.Add(e));

        var gauntlets = rig.Session.Tools.SpawnItem(57, null, ActionSource.Host, palette: 21, shade: 0.5);
        var coat = rig.Session.Tools.SpawnItem(6600, null, ActionSource.Host);
        rig.Run(2.0);
        Assert.True(gauntlets.IsCompleted && coat.IsCompleted);
        Assert.Contains(new StandInWorn(0x10000011, 0x020000D8, 21, 0.5), dresser.Items); // requested Gold at shade 0.5
        Assert.Contains(new StandInWorn(0x100001A1, 0x02000126, 21, 0.3), dresser.Items); // the weenie's own template and shade

        var guid = gauntlets.Result!.Guid;
        rig.Session.Actions.Wield(guid, EquipMask.HandWear, ActionSource.Host);
        rig.Run(2.0);
        var worn = Assert.Single(dresser.Players);
        Assert.Equal(new StandInWorn(0x10000011, 0x020000D8, 21, 0.5), Assert.Single(worn));
        var wield = Assert.Single(descs);
        Assert.Equal(rig.Session.State.Self.Guid, wield.Guid);
        Assert.Single(wield.Model.AnimPartChanges);
        Assert.Equal((byte)2, wield.Model.AnimPartChanges[0].PartIndex);

        rig.Session.Actions.MoveToPack(guid, rig.Session.State.Self.Guid, 0);
        rig.Run(2.0);
        Assert.Equal(2, dresser.Players.Count);
        Assert.Empty(dresser.Players[1]);
        Assert.Equal(2, descs.Count);
    }

    [Fact]
    public void A_held_caster_is_not_worn_so_it_sends_no_ObjDescEvent()
    {
        var dresser = new FakeDresser();
        using var rig = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Dresser = dresser });
        rig.Begin();
        rig.Run(2.0);
        var wand = rig.Session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
        rig.Run(2.0);
        rig.Session.Actions.Wield(wand.Result!.Guid, GearKit.Wand.ValidLocations, ActionSource.Host);
        rig.Run(2.0);
        Assert.Empty(dresser.Players);
    }

    [Fact]
    public void No_created_item_wears_a_human_or_creature_Setup()
    {
        Assert.NotEqual(StandInVisit.HumanSetup, StandInWorld.PlaceholderItemSetup);
        Assert.NotEqual(0x0200004Eu, StandInWorld.PlaceholderItemSetup);
        foreach (var weenie in StandInWorld.KitWeenies.Values)
        {
            Assert.NotEqual(StandInVisit.HumanSetup, weenie.SetupId);
            Assert.NotEqual(0x0200004Eu, weenie.SetupId);
        }

        using var rig = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 });
        rig.Begin();
        rig.Run(2.0);
        var unknown = rig.Session.Tools.SpawnItem(273, null, ActionSource.Host); // Pyreal: a weenie the stand-in knows no Setup for
        var wand = rig.Session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
        rig.Run(2.0);
        Assert.True(unknown.IsCompleted && wand.IsCompleted);
        Assert.True(rig.Session.State.Objects.TryGet(unknown.Result!.Guid, out var coin));
        Assert.Equal(StandInWorld.PlaceholderItemSetup, coin.Physics.SetupId);
        Assert.True(rig.Session.State.Objects.TryGet(wand.Result!.Guid, out var caster));
        Assert.Equal(0x020009ADu, caster.Physics.SetupId); // the Healer's Heart's own
    }

    [Fact]
    public void A_face_alone_is_not_clothing_but_a_retextured_body_part_is()
    {
        var face = new ModelData(0x04000001, [new SubPaletteSwap(0x04000002, 24, 8)], [new TextureChange(StandInHumanLook.HeadPart, 0x05000010, 0x05000011)], [new Lifestone.Net.Types.AnimPartChange(StandInHumanLook.HeadPart, 0x01000020)]);
        var shirt = new ModelData(0x04000001, [], [new TextureChange(StandInHumanLook.HeadPart, 0x05000010, 0x05000011), new TextureChange(0, 0x05000012, 0x05000013)], []);
        Assert.False(StandInHumanLook.IsDressed(ModelData.Empty));
        Assert.False(StandInHumanLook.IsDressed(face));
        Assert.True(StandInHumanLook.IsDressed(shirt));
    }
}

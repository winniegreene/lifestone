using System.Diagnostics;
using Lifestone.Client.Avatars;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The paperdoll (M3 2.2 slice b2): <see cref="GamePaperDoll"/> over the real layout and a scripted inventory. The slot table is <c>gmPaperDollUI::GetLocationInfoFromElementID</c>'s; BSD Mage's wand and five leather
/// pieces land in the slots retail puts them in (a coat in four); empty slots keep the layout's art; the page follows the inventory, and only while shown; a double click on a worn item takes it off through the Action API.
/// </summary>
public sealed class GamePaperDollTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint InventoryButton = 0x100001B1;
    private const uint Player = 0x50000001;
    private const uint Wand = 0x80000201;
    private const uint Cap = 0x80000202;
    private const uint Coat = 0x80000203;
    private const uint Leggings = 0x80000204;
    private const uint Gauntlets = 0x80000205;
    private const uint Boots = 0x80000206;
    private const uint Held = 0x01000000;
    private const uint HeadWear = 0x1;
    private const uint CoatLocations = 0x200 | 0x400 | 0x800 | 0x1000; // chest, abdomen, upper arm, lower arm armour
    private const uint LegLocations = 0x2000 | 0x4000; // upper and lower leg armour
    private const uint HandWear = 0x20;
    private const uint FootWear = 0x100;
    private const uint RingA = 0x80000207;
    private const uint RingB = 0x80000208;
    private const uint RingC = 0x80000209;
    private const uint Rings = 0x40000 | 0x80000; // FingerWearLeft | FingerWearRight
    private const uint LeftRing = 0x40000;
    private const uint RightRing = 0x80000;
    private const uint LeftRingSlot = 0x100001DC;
    private const uint RightRingSlot = 0x100001DE;
    private const uint WeaponSlot = 0x100001DF;
    private const uint ShieldSlot = 0x100001E1;
    private const uint HeadSlot = 0x100005AB;
    private const uint ChestSlot = 0x100005AC;

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler, RecordingViewHost views) : IDisposable
    {
        private uint _event;
        private ushort _descStamp = 2;

        public RecordingViewHost Views => views;

        /// <summary>The server's ObjDescEvent for the player: a part of the body changed (<paramref name="gfxObj"/> for part <paramref name="part"/>).</summary>
        public void ChangePlayerDesc(byte part, uint gfxObj) =>
            handler.OnObjDescEvent(Context, new ObjDescEventMsg(Player, new ModelData(null, [], [], [new Lifestone.Net.Types.AnimPartChange(part, gfxObj)]), 1, ++_descStamp));

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public IInboundMessageHandler Handler => handler;

        public GamePaperDoll Doll => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GamePaperDoll>());

        public GamePanelHost Host => screen.PanelHost!;

        public PlayerInventory Inventory => rig.Session.State.Self.Inventory;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public UiButton Button => Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton));

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void Wield(uint item, uint location) =>
            handler.OnWieldObject(Context, new GameEventHeader(Player, ++_event, 0x0023), new WieldObjectEvent(item, location));

        public void Unwield(uint item) =>
            handler.OnInventoryPutObjInContainer(Context, new GameEventHeader(Player, ++_event, 0x0022), new InventoryPutObjInContainerEvent(item, Player, 0, 0));

        public void Open()
        {
            Assert.True(rig.Click(Button));
            Frames(14);
            Assert.Equal(7u, Host.CurrentPanel);
        }

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void Dispose() => rig.Dispose();
    }

    private static CreateObjectMsg Item(uint guid, string name, uint icon, uint itemType, uint valid)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { IconId = icon, ItemType = itemType, ContainerId = Player, ValidLocations = valid } };
    }

    private Game Enter(bool equip, bool figure = false)
    {
        IInboundMessageHandler? handler = null;
        var views = new RecordingViewHost();
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true, view3D: figure, viewHost: figure ? views : null);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        var game = new Game(rig, screen, handler!, views);
        game.Handler.OnPlayerCreate(game.Context, new PlayerCreateMsg(Player));
        game.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        game.Create(Item(Wand, "Wand", 0x060011CF, 0x01000000, Held));
        game.Create(Item(Cap, "Leather Cap", 0x060011D1, 0x2, HeadWear));
        game.Create(Item(Coat, "Leather Coat", 0x060011D2, 0x2, CoatLocations));
        game.Create(Item(Leggings, "Leather Leggings", 0x060011D3, 0x2, LegLocations));
        game.Create(Item(Gauntlets, "Leather Gauntlets", 0x060011D4, 0x2, HandWear));
        game.Create(Item(Boots, "Leather Boots", 0x060011D0, 0x2, FootWear));
        if (equip)
        {
            Dress(game);
        }

        game.Frames(2);
        return game;
    }

    /// <summary>BSD Mage's kit: the wand held and the five leather pieces worn where they cover.</summary>
    private static void Dress(Game g)
    {
        g.Wield(Wand, Held);
        g.Wield(Cap, HeadWear);
        g.Wield(Coat, CoatLocations);
        g.Wield(Leggings, LegLocations);
        g.Wield(Gauntlets, HandWear);
        g.Wield(Boots, FootWear);
    }

    private static void DoubleClick(Game g, UiItem cell)
    {
        var box = cell.ScreenBox;
        g.Rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
        g.Rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
    }

    // ------------------------------------------------------------------ the slot table

    [Fact]
    public void The_slot_table_is_gmPaperDollUIs_element_to_location_table()
    {
        Assert.Equal(24, GamePaperDoll.SlotTable.Count);
        Assert.Equal(24, GamePaperDoll.SlotTable.Select(static s => s.ElementId).Distinct().Count());
        Assert.Equal(9, GamePaperDoll.SlotTable.Count(static s => s.Armour));

        void Expect(uint element, uint mask, int side)
        {
            Assert.True(GamePaperDoll.GetLocationInfoFromElementId(element, out var location, out var s));
            Assert.Equal((mask, side), (location, s));
        }

        Expect(0x100001DA, 0x8000, 0); // neck
        Expect(0x100001DB, 0x10000, 1); // left wrist
        Expect(0x100001DC, 0x40000, 1); // left ring
        Expect(0x100001DD, 0x20000, 2); // right wrist
        Expect(0x100001DE, 0x80000, 2); // right ring
        Expect(0x100001DF, 0x3500000, 0); // the weapon slot takes melee, missile, held and two-handed
        Expect(0x100001E0, 0x800000, 0); // ammunition
        Expect(0x100001E1, 0x200000, 0); // shield
        Expect(0x100001E2, 0x2, 0); // shirt
        Expect(0x100001E3, 0x40, 0); // pants
        Expect(0x1000058E, 0x4000000, 0); // trinket
        Expect(0x100005E9, 0x8000000, 0); // cloak
        Expect(0x10000595, 0x10000000, 0);
        Expect(0x10000596, 0x20000000, 0);
        Expect(0x10000597, 0x40000000, 0);
        Expect(0x100005AB, 0x1, 0); // head
        Expect(0x100005AC, 0x200, 0); // chest armour
        Expect(0x100005AD, 0x400, 0);
        Expect(0x100005AE, 0x800, 0);
        Expect(0x100005AF, 0x1000, 0);
        Expect(0x100005B0, 0x20, 0); // hand
        Expect(0x100005B1, 0x2000, 0);
        Expect(0x100005B2, 0x4000, 0);
        Expect(0x100005B3, 0x100, 0); // foot
        Assert.False(GamePaperDoll.GetLocationInfoFromElementId(0x100001D5, out var none, out var noSide));
        Assert.Equal((0u, 0), (none, noSide));
    }

    // ------------------------------------------------------------------ the page

    [RequiresLocalDatsFact]
    public void Every_slot_of_the_table_is_a_one_cell_item_list_of_the_layout_and_empty_slots_keep_the_layouts_art()
    {
        using var g = Enter(equip: false);
        var doll = g.Doll;
        g.Open();
        foreach (var slot in GamePaperDoll.SlotTable)
        {
            var list = doll.SlotList(slot.ElementId);
            Assert.NotNull(list);
            Assert.Equal(1, list.FixedSlots);
            Assert.Equal(1, list.CellCount);
            var cell = list.CellAt(0)!;
            Assert.True(cell.IsEmpty, slot.Name);
            Assert.Null(cell.ComposedIcon); // nothing is composed over the layout's own empty-slot art
            Assert.False(cell.IconPending);
            Assert.Equal(0u, doll.ItemIn(slot.ElementId));
        }
    }

    [RequiresLocalDatsFact]
    public void The_wand_and_the_five_leather_pieces_are_in_the_slots_retail_puts_them_in_and_the_rest_stay_empty()
    {
        using var g = Enter(equip: true);
        var doll = g.Doll;
        Assert.Equal(6, g.Inventory.Equipped.Count);
        g.Open();

        var expected = new Dictionary<uint, uint>
        {
            [0x100001DF] = Wand, // the weapon slot, for a held item
            [0x100005AB] = Cap, // head
            [0x100005AC] = Coat, // the coat covers chest, abdomen, upper arm and lower arm armour
            [0x100005AD] = Coat,
            [0x100005AE] = Coat,
            [0x100005AF] = Coat,
            [0x100005B1] = Leggings, // upper and lower leg
            [0x100005B2] = Leggings,
            [0x100005B0] = Gauntlets, // hand
            [0x100005B3] = Boots, // foot
        };
        foreach (var slot in GamePaperDoll.SlotTable)
        {
            Assert.Equal(expected.GetValueOrDefault(slot.ElementId), doll.ItemIn(slot.ElementId));
            var cell = doll.SlotList(slot.ElementId)!.CellAt(0)!;
            Assert.Equal(expected.ContainsKey(slot.ElementId), !cell.IsEmpty);
        }

        // The cells are the item cells of b1: the picture is composed from the object's icon.
        var wand = doll.SlotList(0x100001DF)!.CellAt(0)!;
        g.Frames(4);
        Assert.False(wand.IconPending);
        Assert.NotNull(wand.ComposedIcon);
    }

    // The owner's question (M5 item 7): a worn robe shows on the figure and in no slot. gmPaperDollUI::SetUIItemIntoLocation
    // (11.6096 0x004A6300) tests the placement's mask against every slot, so a robe is in each armour slot it covers; PostInit
    // (0x004A56D0) hides those nine slots on the figure view, so there it shows on the figure only, as retail. The hooded
    // robe's mask is the world database's Faran Robe with Hood (weenie 5851, ValidLocations 0x7F01).
    [RequiresLocalDatsFact]
    public void A_robe_is_in_every_armour_slot_it_covers_on_the_slots_view_and_on_the_figure_only_on_the_figure_view()
    {
        const uint Robe = 0x80000210;
        const uint HoodedRobe = 0x80000211;
        const uint RobeLocations = 0x200 | 0x400 | 0x800 | 0x1000 | 0x2000 | 0x4000 | 0x100; // chest to lower leg armour, and the feet
        const uint HoodedRobeLocations = RobeLocations | HeadWear; // 0x7F01
        Assert.Equal(0x7F01u, HoodedRobeLocations);
        uint[] robeSlots = [0x100005AC, 0x100005AD, 0x100005AE, 0x100005AF, 0x100005B1, 0x100005B2, 0x100005B3];

        using var g = Enter(equip: false);
        g.Create(Item(Robe, "Faran Robe", 0x060011D2, 0x4, RobeLocations));
        g.Create(Item(HoodedRobe, "Faran Robe with Hood", 0x060011D2, 0x4, HoodedRobeLocations));
        g.Create(Item(RingA, "Ring", 0x060011D1, 0x8, Rings));
        g.Create(Item(0x80000212, "Foci of Strife", 0x060011D0, 0x80, 0)); // the live kit's pack: a focus, a scarab stack and a trade note are in no slot
        g.Create(Item(0x80000213, "Platinum Scarab", 0x060011D0, 0x1000, 0));
        g.Create(Item(0x80000214, "Trade Note (250,000)", 0x060011D0, 0x40, 0));
        g.Wield(Robe, RobeLocations);
        g.Wield(Gauntlets, HandWear); // a single-slot armour piece beside it
        g.Wield(RingA, RightRing); // a single-slot item outside the armour slots
        var doll = g.Doll;
        g.Open();
        Assert.True(doll.SlotsViewShown); // the rig has no figure view: the page opens on the slots

        void Expect(uint robe, uint head)
        {
            foreach (var slot in GamePaperDoll.SlotTable)
            {
                var want = slot.ElementId switch
                {
                    0x100005AB => head,
                    0x100005B0 => Gauntlets,
                    RightRingSlot => RingA,
                    _ when robeSlots.Contains(slot.ElementId) => robe,
                    _ => 0u, // the shirt and pants slots are clothing (0x2, 0x40): a robe is not in them
                };
                Assert.True(want == doll.ItemIn(slot.ElementId), $"{slot.Name}: 0x{doll.ItemIn(slot.ElementId):X8}, expected 0x{want:X8}");
            }
        }

        Expect(Robe, 0);

        g.Unwield(Robe);
        g.Wield(HoodedRobe, HoodedRobeLocations);
        g.Frames(2);
        Expect(HoodedRobe, HoodedRobe); // the hood puts it in the head slot too

        // The figure view: the nine armour slots hide (they keep the robe), the ring's slot stays.
        Assert.True(g.Rig.Click(doll.Checkbox!));
        g.Frames(2);
        Assert.False(doll.SlotsViewShown);
        Assert.All(robeSlots, s => Assert.False(doll.SlotList(s)!.Visible));
        Assert.Equal(HoodedRobe, doll.ItemIn(0x100005AC));
        Assert.True(doll.SlotList(RightRingSlot)!.Visible);
    }

    // The owner's three round icons (blue, yellow, red) at the top of the slots were the empty Aetheria sigil slots, which
    // showed always. gmPaperDollUI::UpdateAetheria (11.6096 0x004A41C0) shows each only by its bit of the player's int 322.
    [RequiresLocalDatsFact]
    public void The_sigil_slots_show_only_for_the_aetheria_slots_the_player_has_unlocked()
    {
        uint[] sigils = [0x10000595, 0x10000596, 0x10000597]; // blue, yellow, red
        using var g = Enter(equip: true);
        var doll = g.Doll;
        g.Open();
        Assert.All(sigils, s => Assert.False(doll.SlotList(s)!.Visible)); // no int 322: none
        Assert.True(doll.SlotList(WeaponSlot)!.Visible);

        byte sequence = 0;
        void Unlock(int bits)
        {
            g.Handler.OnPublicUpdatePropertyInt(g.Context, new PublicUpdatePropertyIntMsg(++sequence, Player, GamePaperDoll.AetheriaBitfieldProperty, bits));
            g.Frames(1);
        }

        Unlock(1);
        Assert.Equal([true, false, false], sigils.Select(s => doll.SlotList(s)!.Visible));
        Unlock(1 | 4);
        Assert.Equal([true, false, true], sigils.Select(s => doll.SlotList(s)!.Visible));
        Unlock(7);
        Assert.All(sigils, s => Assert.True(doll.SlotList(s)!.Visible));
        Assert.All(sigils, s => Assert.Equal(0u, doll.ItemIn(s))); // shown empty: nothing worn there
    }

    // M5 7 D122: the live mage's worn set as the database had it on 2026-10-29 (BSD Mage: the Faran Robe with Hood at 32513, Leather Gauntlets at 32, a Shirt at 30, Wide
    // Breeches at 68 and the Wand held at 16777216), arriving as a login does: the PlayerDescription's equipped list, then each item's CreateObject with its wielder and
    // location. Each slot shows what retail's SetUIItemIntoLocation (0x004A6300) puts there; the figure view hides the nine armour slots and keeps the rest.
    [RequiresLocalDatsFact]
    public void The_live_mages_worn_set_from_a_login_shows_in_retails_slots_on_both_views()
    {
        const uint Robe = 0x80000230, Mitts = 0x80000231, Shirt = 0x80000232, Breeches = 0x80000233, LiveWand = 0x80000234;
        (uint Guid, string Name, uint ItemType, uint Location)[] worn =
        [
            (Robe, "Faran Robe with Hood", 0x4, 32513),
            (Mitts, "Leather Gauntlets", 0x2, 32),
            (Shirt, "Shirt", 0x4, 30),
            (Breeches, "Wide Breeches", 0x4, 68),
            (LiveWand, "Wand", 0x8000, 16777216),
        ];
        using var g = Enter(equip: false);
        g.Handler.OnPlayerDescription(g.Context, new GameEventHeader(Player, 1, 0x0013), new PlayerDescriptionMsg { Equipped = [.. worn.Select(w => new EquippedDesc(w.Guid, w.Location, 0))] });
        foreach (var w in worn)
        {
            var basis = Item(w.Guid, w.Name, 0x060011D2, w.ItemType, w.Location);
            g.Create(basis with { Weenie = basis.Weenie with { ContainerId = null, WielderId = Player, CurrentlyWieldedLocation = w.Location } });
        }

        g.Frames(2);
        var doll = g.Doll;
        g.Open();
        Assert.Equal(5, g.Inventory.Equipped.Count);

        var expected = new Dictionary<uint, uint>
        {
            [WeaponSlot] = LiveWand,
            [0x100001E2] = Shirt, // the shirt slot (chest wear 0x2)
            [0x100001E3] = Breeches, // the pants slot (upper leg wear 0x40)
            [0x100005B0] = Mitts, // hand
        };
        foreach (var slot in GamePaperDoll.SlotTable.Where(static s => s.Armour && s.ElementId != 0x100005B0))
        {
            expected[slot.ElementId] = Robe; // head, chest, abdomen, both arms, both legs and feet: the hooded robe covers them all
        }

        foreach (var slot in GamePaperDoll.SlotTable)
        {
            Assert.True(expected.GetValueOrDefault(slot.ElementId) == doll.ItemIn(slot.ElementId), $"{slot.Name}: 0x{doll.ItemIn(slot.ElementId):X8}");
        }

        // The views: on the slots view every filled slot shows; on the figure view the armour slots hide and the wand, shirt and breeches stay in sight.
        doll.ShowSlotsView(true);
        Assert.All(expected.Keys, s => Assert.True(doll.SlotList(s)!.Visible));
        doll.ShowSlotsView(false);
        Assert.Equal([WeaponSlot, 0x100001E2u, 0x100001E3u], expected.Keys.Where(s => doll.SlotList(s)!.Visible).Order());
    }

    // M5 7 D122 (closes TODO(m3-paperdoll-tooltips)): gmPaperDollUI::UpdateItemSlotTooltip (11.6096 0x004A5200) names what an empty slot takes, and a filled
    // slot's item, worn or wielded, with how to take it off.
    [RequiresLocalDatsFact]
    public void Each_slot_s_tooltip_names_what_it_takes_when_empty_and_its_item_worn_or_wielded_when_filled()
    {
        using var g = Enter(equip: true);
        var doll = g.Doll;
        g.Open();

        string? Tip(uint slot) => doll.SlotList(slot)!.CellAt(0)!.TooltipText?.Literal;
        Assert.Equal("Wand (wielded)\nDouble-click to unwield", Tip(WeaponSlot));
        Assert.Equal("Leather Cap (worn)\nDouble-click to take off", Tip(HeadSlot));
        Assert.Equal("Leather Coat (worn)\nDouble-click to take off", Tip(ChestSlot));
        Assert.Equal("Drag rings here to wear them", Tip(LeftRingSlot));
        Assert.Equal("Drag shields here to wield them", Tip(ShieldSlot));
        Assert.Equal("Drag a Red Aetheria sigil here to activate it", Tip(0x10000597));
        Assert.Equal("Drag foot coverings here to wear them", GamePaperDoll.SlotTooltip(GamePaperDoll.SlotTable.Count - 1, null));

        g.Unwield(Wand);
        g.Frames(2);
        Assert.Equal("Drag weapons here to wield them", Tip(WeaponSlot));
    }

    // M5 7 D122 (closes TODO(m4-paperdoll-figure-select)): the left button over the figure selects the worn item under the mouse, or the player where nothing covers the part
    // (gmPaperDollUI::ListenToElementMessage 0x004A5FA0, message 28 with parameter 7; GetPaperDollItemUnderMouse).
    [RequiresLocalDatsFact]
    public void A_left_click_on_the_figure_selects_the_worn_item_under_it_or_the_player()
    {
        using var g = Enter(equip: true);
        var doll = g.Doll;
        g.Open();
        var selected = new List<uint>();
        doll.SelectRequester = selected.Add;

        Assert.True(doll.SelectMask(0x200 | 0x2)); // the chest: the coat
        Assert.True(doll.SelectMask(0x20)); // the hands: the gauntlets
        g.Unwield(Cap);
        g.Frames(2);
        Assert.True(doll.SelectMask(0x1)); // the head with nothing on it: the player
        Assert.False(doll.SelectMask(0)); // no part
        Assert.Equal([Coat, Gauntlets, Player], selected);
    }

    [RequiresLocalDatsFact]
    public void The_paperdoll_follows_the_inventory_redrawing_once_when_shown_and_nothing_while_hidden()
    {
        using var g = Enter(equip: false);
        var doll = g.Doll;
        g.Open();
        Assert.Equal(0u, doll.ItemIn(0x100005AB));
        var drawn = doll.Refreshes;

        g.Wield(Cap, HeadWear);
        g.Frames(2);
        Assert.Equal(Cap, doll.ItemIn(0x100005AB));
        Assert.Equal(drawn + 1, doll.Refreshes); // one redraw for the change
        g.Frames(5);
        Assert.Equal(drawn + 1, doll.Refreshes); // idle frames draw nothing

        g.Unwield(Cap);
        g.Frames(2);
        Assert.Equal(0u, doll.ItemIn(0x100005AB)); // the slot is empty again
        Assert.True(doll.SlotList(0x100005AB)!.CellAt(0)!.IsEmpty);

        // Closed: a change draws nothing until the page shows again.
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(4);
        Assert.False(g.Host.IsOpen);
        var hidden = doll.Refreshes;
        g.Wield(Boots, FootWear);
        g.Frames(6);
        Assert.Equal(hidden, doll.Refreshes);
        g.Open();
        Assert.Equal(hidden + 1, doll.Refreshes);
        Assert.Equal(Boots, doll.ItemIn(0x100005B3));
    }

    [RequiresLocalDatsFact]
    public void A_double_click_on_a_worn_item_asks_the_action_api_to_take_it_off_and_an_empty_slot_does_nothing()
    {
        using var g = Enter(equip: true);
        g.Open();
        var session = g.Rig.Session;
        var before = session.ActionLog.Total;
        var weapon = g.Doll.SlotList(0x100001DF)!.CellAt(0)!;
        var shield = g.Doll.SlotList(0x100001E1)!.CellAt(0)!;
        Assert.Equal(Wand, weapon.ItemId);

        DoubleClick(g, shield); // empty
        g.Frames(2);
        Assert.Equal(before, session.ActionLog.Total);

        DoubleClick(g, weapon);
        g.Frames(2);
        var moves = Enumerable.Range(0, session.ActionLog.Count).Select(i => session.ActionLog[i]).Where(static e => e.Kind == ActionKind.MoveToPack).ToArray();
        Assert.Single(moves);
        Assert.Equal(ActionSource.Ui, moves[0].Source);
        Assert.Contains($"item=0x{Wand:X8} container=0x00000000", moves[0].Summary, StringComparison.OrdinalIgnoreCase);

        // The server's answer (GameEvent 0x0022) puts the wand in the pack: the slot empties.
        g.Unwield(Wand);
        g.Frames(2);
        Assert.Equal(0u, g.Doll.ItemIn(0x100001DF));
        Assert.Contains(g.Inventory.Pack, e => e.Guid == Wand);
    }

    [RequiresLocalDatsFact]
    public void The_slots_checkbox_swaps_the_armour_slots_and_the_figure_and_a_host_without_a_figure_view_opens_the_page_on_the_slots()
    {
        using var g = Enter(equip: true);
        var doll = g.Doll;
        g.Open();
        Assert.True(GamePaperDoll.FigureBuilt);
        Assert.False(doll.FigureServed); // the rig has no view data or view host: the slots view, loud (TODO(m3-paperdoll-figure))
        Assert.True(doll.SlotsViewShown);
        Assert.True(doll.Checkbox!.Highlighted);
        Assert.True(doll.SlotList(0x100005AB)!.Visible);
        Assert.False(doll.Viewport!.Visible);
        Assert.False(doll.ClickMask!.Visible);
        Assert.True(doll.SlotList(0x100001DF)!.Visible); // the weapon slot is always there

        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.False(doll.SlotsViewShown);
        Assert.False(doll.Checkbox.Highlighted);
        Assert.False(doll.SlotList(0x100005AB)!.Visible); // PostInit's hidden armour slots
        Assert.True(doll.Viewport.Visible);
        Assert.True(doll.ClickMask.Visible);
        Assert.Equal(Cap, doll.ItemIn(0x100005AB)); // hidden, but they hold the worn items

        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.True(doll.SlotsViewShown);
        Assert.True(doll.SlotList(0x100005AB)!.Visible);
    }

    // M5 7 D129: the live review frames tick "Slots" through the doll's own handler: the box toggles and the views swap as a click does.
    [RequiresLocalDatsFact]
    public void Clicking_the_slots_checkbox_from_code_toggles_the_box_and_swaps_the_views_as_a_click_does()
    {
        using var g = Enter(equip: true);
        var doll = g.Doll;
        g.Open();
        Assert.True(doll.SlotsViewShown); // the rig has no figure view: the page opens on the slots

        Assert.True(doll.ClickSlotsCheckbox());
        Assert.False(doll.SlotsViewShown);
        Assert.False(doll.Checkbox!.Highlighted);
        Assert.False(doll.SlotList(HeadSlot)!.Visible);
        Assert.True(doll.Viewport!.Visible);

        Assert.True(doll.ClickSlotsCheckbox());
        Assert.True(doll.SlotsViewShown);
        Assert.True(doll.Checkbox.Highlighted);
        Assert.True(doll.SlotList(HeadSlot)!.Visible);
        Assert.Equal(Cap, doll.ItemIn(HeadSlot));
    }

    // M5 7 D129 fix: the live run of 2026-10-29 15:19 UTC took both review frames in the login's portal space (the page open by the saved layout, nothing of it drawn).
    // A frame waits for the player placed, the tunnel gone, the shell built and painted, the inventory page shown and the doll drawn.
    [RequiresLocalDatsFact]
    public void The_review_frames_wait_out_portal_space_and_an_unpainted_or_closed_page()
    {
        using var g = Enter(equip: true);
        var shell = g.Screen;
        g.Open();

        Assert.Equal("the player to be placed in the world", GamePaperDoll.ReviewFrameWait(shell, playerPlaced: false, tunnelVisible: true));
        Assert.Equal("the portal tunnel to end", GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: true)); // portal space: the page may be open already
        Assert.Equal("the shell to be built and painted", GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false)); // the host has not painted it yet
        Assert.Equal("the shell to be built and painted", GamePaperDoll.ReviewFrameWait(null, playerPlaced: true, tunnelVisible: false));

        shell.MarkPainted(3, 50.0, timedOut: false);
        Assert.Null(GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false)); // arrived, painted, the page and the doll drawn

        // The 15:34 UTC run took both frames 0.4 s into the arrival, the player still hidden in the Hidden script's pink bubbles (retail's look before LoginComplete).
        Assert.StartsWith("the player to materialise", GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false, playerMaterialised: false));
        Assert.Null(GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false, playerMaterialised: true));

        Assert.True(g.Rig.Click(g.Button)); // the page closed
        g.Frames(4);
        Assert.Equal("the inventory page to show", GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false));
        g.Open();
        Assert.Null(GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false));
    }

    [RequiresLocalDatsFact]
    public void The_last_portal_notice_expires_five_seconds_after_it_is_posted_and_the_review_frames_wait_it_out()
    {
        // M5 7: the stale "In Portal Space - Please Wait..." by the vitals in early town frames is the tunnel's last notice, alive for its row media's 5 s pause (gmSpewBoxUI's row state 0x10000002, then the message 0x10000003
        // ListenToElementMessage 0x004D6420 deletes it on); retail posts it at each rotation segment (gmSmartBoxUI::UseTime 0x004D7A90) and clears nothing at the arrival. Kept as retail; the frames wait.
        using var g = Enter(equip: true);
        var shell = g.Screen;
        g.Open();
        shell.MarkPainted(3, 50.0, timedOut: false);
        var spew = Assert.IsType<Lifestone.Client.RetailUi.Chat.GameSpewBox>(shell.Chat?.Spew);
        Assert.Null(GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false));

        g.Rig.Screens.Notices.DisplayStringInfo(Lifestone.Client.RetailUi.Flow.UiNoticeRouter.SpewBoxTextType, Lifestone.Data.Ui.UiStringRef.FromLiteral("In Portal Space - Please Wait..."));
        g.Frames(1);
        Assert.Equal(1, spew.RowCount);
        Assert.StartsWith("the spew box's last notice to expire", GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false));
        Assert.StartsWith("the spew box's last notice to expire", GamePaperDoll.WorldReviewWait(shell, playerPlaced: true, tunnelVisible: false));

        g.Rig.Run(4.5);
        Assert.Equal(1, spew.RowCount); // still inside its 5 s
        g.Rig.Run(1.0);
        Assert.Equal(0, spew.RowCount); // gone by its own media, as retail's
        Assert.Null(GamePaperDoll.ReviewFrameWait(shell, playerPlaced: true, tunnelVisible: false));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Timing")] // m4-timing-tier-candidates: an 8 ms wall-clock bound
    public void The_controllers_construction_is_one_small_unit_under_eight_milliseconds()
    {
        using var g = Enter(equip: false);
        var panels = g.Screen.PanelControllers!;
        GamePaperDoll.Create(panels.Context, panels.Host)!.Dispose(); // warm
        var watch = Stopwatch.StartNew();
        var doll = GamePaperDoll.Create(panels.Context, panels.Host);
        watch.Stop();
        Assert.NotNull(doll);
        doll.Dispose();
        Assert.True(watch.Elapsed.TotalMilliseconds < 8.0, $"construction took {watch.Elapsed.TotalMilliseconds:0.00} ms");
    }

    // ------------------------------------------------------------------ the figure (b4)

    [Fact]
    public void The_figure_camera_is_UpdateForRaces_per_heritage_and_PostInits_for_the_others()
    {
        Assert.Equal(new System.Numerics.Vector3(0.12f, -2.4f, 0.88f), PaperDollFigure.DefaultCamera); // PostInit 0x004A56D0
        Assert.Equal(PaperDollFigure.DefaultCamera, PaperDollFigure.CameraFor(1)); // Aluvian (BSD Mage): UpdateForRace has no case
        foreach (var heritage in new[] { 2, 3, 4, 5, 10, 11, 0 })
        {
            Assert.Equal(PaperDollFigure.DefaultCamera, PaperDollFigure.CameraFor(heritage));
        }

        Assert.Equal(new System.Numerics.Vector3(0.12f, -3.0f, 0.88f), PaperDollFigure.CameraFor(6));
        Assert.Equal(new System.Numerics.Vector3(0.12f, -3.0f, 0.88f), PaperDollFigure.CameraFor(7));
        Assert.Equal(new System.Numerics.Vector3(0.12f, -3.4f, 1.0f), PaperDollFigure.CameraFor(8));
        foreach (var heritage in new[] { 9, 12, 13 })
        {
            Assert.Equal(new System.Numerics.Vector3(0.12f, -3.4f, 0.88f), PaperDollFigure.CameraFor(heritage));
        }

        Assert.Equal(191.3679f, PaperDollFigure.FigureHeading);
    }

    [RequiresLocalDatsFact]
    public void The_page_opens_on_the_figure_with_the_nine_armour_slots_hidden_and_the_figure_is_the_players_body_at_retails_heading_and_camera()
    {
        using var g = Enter(equip: true, figure: true);
        var doll = g.Doll;
        Assert.Empty(g.Views.AttachedFigures); // nothing is made before the page shows
        g.Open();

        Assert.True(doll.FigureServed);
        var (viewport, served) = Assert.Single(g.Views.AttachedFigures);
        Assert.Equal(GamePaperDoll.ViewportId, viewport);
        var figure = Assert.IsType<PaperDollFigure>(served);
        Assert.Same(doll.Figure, figure);

        // The view: retail's PostInit state (the nine armour slots hidden, the viewport and the click mask shown, the checkbox unchecked).
        Assert.False(doll.SlotsViewShown);
        Assert.True(doll.Viewport!.Visible);
        Assert.True(doll.ClickMask!.Visible);
        Assert.False(doll.Checkbox!.Highlighted);
        Assert.False(doll.SlotList(0x100005AB)!.Visible);
        Assert.True(doll.SlotList(0x100001DF)!.Visible); // the weapon slot is always there
        Assert.Equal(Cap, doll.ItemIn(0x100005AB)); // hidden, but they hold the worn items

        // The figure: the player's Setup, heading 191.3679, the idle pose, the camera (no heritage in the stand-in's qualities: PostInit's).
        var preview = Assert.IsType<CreaturePreview>(figure.Preview);
        Assert.Equal(0x02000001u, preview.Setup.Id);
        Assert.Equal(191.3679f, preview.Heading);
        Assert.Equal(PaperDollFigure.DefaultCamera, figure.CameraPosition);
        Assert.Equal(System.Numerics.Vector3.Zero, figure.CameraDirection);
        Assert.Equal(0u, figure.BackdropSetupId);
        Assert.True(preview.Draws.Length > 0);
        Assert.Contains(preview.Draws.ToArray(), static d => d.GfxObjId != 0);
        Assert.Equal(1, figure.Builds);

        // The checkbox swaps the views both ways, as before.
        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.True(doll.SlotsViewShown);
        Assert.False(doll.Viewport.Visible);
        Assert.True(doll.SlotList(0x100005AB)!.Visible);
        Assert.True(g.Rig.Click(doll.Checkbox));
        g.Frames(2);
        Assert.False(doll.SlotsViewShown);
        Assert.True(doll.Viewport.Visible);
        Assert.Equal(1, figure.Builds); // toggling the views does not rebuild
    }

    [RequiresLocalDatsFact]
    public void The_figure_is_rebuilt_when_the_players_appearance_changes_and_not_on_an_unrelated_event_nor_while_the_page_is_hidden()
    {
        using var g = Enter(equip: false, figure: true);
        g.Open();
        var figure = g.Doll.Figure!;
        var preview = figure.Preview!;
        Assert.Equal(1, figure.Builds);
        var draws = preview.Draws.ToArray();
        var version = figure.Version;

        // Idle frames and events that are not about the player's appearance: nothing is rebuilt.
        g.Frames(6);
        g.Create(Item(0x80000299, "Pebble", 0x060011D0, 0x80, 0)); // a new item in the pack
        g.Frames(2);
        Assert.Equal(1, figure.Builds);
        Assert.Equal(version, figure.Version);

        // The server's ObjDescEvent for the player: part 1 takes the gfx object of another part; the figure is dressed again, on the same preview.
        var other = draws.First(d => d.PartIndex != 1 && d.GfxObjId != 0 && d.GfxObjId != draws[1].GfxObjId).GfxObjId;
        g.ChangePlayerDesc(1, other);
        g.Frames(2);
        Assert.Equal(2, figure.Builds);
        Assert.Same(preview, figure.Preview);
        Assert.Equal(other, figure.Preview!.Draws[1].GfxObjId);
        Assert.NotEqual(version, figure.Version);
        Assert.Single(g.Views.AttachedFigures); // the same figure; not attached again

        // Putting a piece on re-dresses it too (the server follows with the ObjDescEvent; the same appearance draws as itself).
        g.Wield(Cap, HeadWear);
        g.Frames(2);
        Assert.Equal(3, figure.Builds);
        g.Frames(6);
        Assert.Equal(3, figure.Builds);

        // Hidden: a change rebuilds nothing until the page shows again.
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(4);
        Assert.False(g.Host.IsOpen);
        g.ChangePlayerDesc(1, draws[1].GfxObjId);
        g.Frames(6);
        Assert.Equal(3, figure.Builds);
        g.Open();
        Assert.Equal(4, figure.Builds);
        Assert.Equal(draws[1].GfxObjId, figure.Preview!.Draws[1].GfxObjId);
    }

    // ------------------------------------------------------------------ the click map and the drops (M4 2.2 s3)

    [Fact]
    public void The_click_map_gives_each_slot_its_equip_mask_side_and_the_drop_it_accepts()
    {
        var map = GamePaperDoll.ClickMap;
        Assert.Equal(GamePaperDoll.SlotTable.Count, map.Count);
        for (var i = 0; i < map.Count; i++)
        {
            var slot = GamePaperDoll.SlotTable[i];
            Assert.Equal((slot.ElementId, slot.Name, (EquipMask)slot.Mask, slot.Side, slot.Armour), (map[i].ElementId, map[i].Name, map[i].Mask, map[i].Side, map[i].Armour));
        }

        // The rings and the bracelets ask for the free side; every other slot takes the item's own locations.
        Assert.Equal([0x10000u, 0x20000u, 0x40000u, 0x80000u], map.Where(static e => e.Rule == PaperDollDropRule.FreeSide).Select(static e => (uint)e.Mask).Order());
        Assert.All(map.Where(static e => e.Rule == PaperDollDropRule.SlotLocations), static e => Assert.NotEqual(EquipMask.None, e.Mask));
        Assert.True(GamePaperDoll.TryGetClickEntry(WeaponSlot, out var weapon));
        Assert.Equal((EquipMask)GamePaperDoll.WeaponLocations, weapon.Mask);
        Assert.True(GamePaperDoll.TryGetClickEntry(LeftRingSlot, out var left));
        Assert.Equal((EquipMask.FingerWearLeft, 1, PaperDollDropRule.FreeSide), (left.Mask, left.Side, left.Rule));
        Assert.False(GamePaperDoll.TryGetClickEntry(0x10000001, out _)); // not a slot
    }

    [RequiresLocalDatsFact]
    public void A_ring_or_bracelet_dropped_on_a_slot_asks_for_that_side_when_it_is_free_else_the_free_side_else_the_slot_dropped_on()
    {
        using var g = Enter(equip: false);
        foreach (var ring in new[] { RingA, RingB, RingC })
        {
            g.Create(Item(ring, "Ring", 0x060011D0, 0x8, Rings));
            g.Unwield(ring); // a 0022 into the player: the ring is in the main pack
        }

        g.Open();
        var left = g.Doll.DropOf(LeftRingSlot)!;
        var right = g.Doll.DropOf(RightRingSlot)!;
        Assert.Equal(DropVerdict.Ok, left.Check(RingA, out var mask));
        Assert.Equal(LeftRing, mask); // its own side, free
        Assert.Equal(DropVerdict.Ok, right.Check(RingA, out mask));
        Assert.Equal(RightRing, mask);

        g.Wield(RingB, LeftRing);
        g.Frames(2);
        Assert.Equal(DropVerdict.Ok, left.Check(RingA, out mask));
        Assert.Equal(RightRing, mask); // the left is worn: the free side (WieldSlotChoice)

        g.Wield(RingC, RightRing);
        g.Frames(2);
        Assert.Equal(DropVerdict.Ok, left.Check(RingA, out mask));
        Assert.Equal(LeftRing, mask); // both worn: the slot dropped on, which the server swaps
        Assert.Equal(DropVerdict.DoesNotFit, left.Check(RingB, out _)); // worn there already
    }

    [RequiresLocalDatsFact]
    public void A_wearable_dropped_on_a_slot_it_fits_wields_its_own_locations_and_one_that_does_not_fit_or_is_not_carried_is_refused()
    {
        using var g = Enter(equip: false);
        foreach (var item in new[] { Wand, Cap, Coat })
        {
            g.Unwield(item);
        }

        g.Open();
        var doll = g.Doll;
        Assert.Equal(DropVerdict.Ok, doll.DropOf(HeadSlot)!.Check(Cap, out var mask));
        Assert.Equal(HeadWear, mask);
        Assert.Equal(DropVerdict.Ok, doll.DropOf(ChestSlot)!.Check(Coat, out mask));
        Assert.Equal(CoatLocations, mask); // a coat covers several slots: its whole mask goes, as the double click's wield does
        Assert.Equal(DropVerdict.Ok, doll.DropOf(WeaponSlot)!.Check(Wand, out mask));
        Assert.Equal(Held, mask);
        Assert.Equal(DropVerdict.DoesNotFit, doll.DropOf(ChestSlot)!.Check(Cap, out mask)); // a cap is no chest armour
        Assert.Equal(0u, mask);
        Assert.Equal(DropVerdict.DoesNotFit, doll.DropOf(ShieldSlot)!.Check(Wand, out _));
        Assert.Equal(DropVerdict.NotHeld, doll.DropOf(HeadSlot)!.Check(0x7FFFFFFF, out _)); // an object the player does not carry
        Assert.Equal(DropVerdict.NotHeld, doll.DropOf(HeadSlot)!.Check(0, out _));
        Assert.Equal(DropVerdict.NotHeld, doll.DropOf(HeadSlot)!.Check(Leggings, out _)); // created but never put in the pack: not carried
    }

    // M5 7 D122: gmPaperDollUI::AcceptDragObject (0x004A3E80) takes a melee weapon on the shield slot (the off hand), and HandleDropRelease (0x004A50F0) over the figure
    // wears a carried wearable (AcceptPaperDollDragObject 0x004A4DE0) and refuses anything else with "You can't put that item there".
    [RequiresLocalDatsFact]
    public void A_melee_weapon_fits_the_shield_slot_and_a_wearable_dropped_on_the_figure_is_worn_while_anything_else_is_refused_aloud()
    {
        const uint Dagger = 0x80000220;
        using var g = Enter(equip: false);
        g.Create(Item(Dagger, "Dagger", 0x060011CF, 0x1, 0x100000));
        g.Create(Item(RingA, "Ring", 0x060011D1, 0x8, Rings));
        foreach (var item in new[] { Wand, Cap, Coat, Dagger, RingA })
        {
            g.Unwield(item);
        }

        g.Wield(Gauntlets, HandWear);
        g.Open();
        var doll = g.Doll;
        Assert.Equal(DropVerdict.Ok, doll.DropOf(ShieldSlot)!.Check(Dagger, out var mask));
        Assert.Equal(0x200000u, mask); // the off hand
        Assert.Equal(DropVerdict.DoesNotFit, doll.DropOf(ShieldSlot)!.Check(Wand, out _)); // a held caster is no off-hand weapon

        var figure = doll.FigureDrop;
        var notices = new List<string>();
        figure.Notice = notices.Add;
        Assert.Equal(DropVerdict.Ok, figure.Check(Coat, out mask));
        Assert.Equal(CoatLocations, mask);
        Assert.Equal(DropVerdict.Ok, figure.Check(Cap, out mask));
        Assert.Equal(HeadWear, mask);
        Assert.Equal(DropVerdict.AlreadyThere, figure.Check(Gauntlets, out _)); // worn already: stays, no word
        Assert.Equal(DropVerdict.DoesNotFit, figure.Check(Wand, out _)); // a weapon goes in a slot, not on the body
        Assert.Equal(DropVerdict.NotHeld, figure.Check(Leggings, out _));

        var source = doll.SlotList(LeftRingSlot)!.CellAt(0)!; // any cell stands for the drag's source
        Assert.NotNull(figure.Refusal(new UiDragPayload(Wand, source, null, false), null));
        Assert.NotNull(figure.Refusal(new UiDragPayload(Gauntlets, source, null, false), null));
        Assert.Equal([GamePaperDollFigureDrop.CannotPutThere], notices); // the wand's refusal alone, aloud

        var before = g.Rig.Session.ActionLog.Total;
        figure.Drop(new UiDragPayload(Coat, source, null, false), null);
        Assert.Equal(before + 1, g.Rig.Session.ActionLog.Total);
        Assert.Contains($"item=0x{Coat:X8} location=0x{CoatLocations:X8}", figure.LastAction!.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [RequiresLocalDatsFact]
    public void An_item_dragged_from_the_pack_to_a_paperdoll_slot_is_wielded_through_the_action_api_and_a_refused_drop_sends_nothing()
    {
        using var g = Enter(equip: false);
        g.Create(Item(RingA, "Ring", 0x060011D0, 0x8, Rings));
        g.Unwield(RingA);
        g.Unwield(Cap);
        g.Open();
        g.Frames(4);
        var page = Assert.Single(g.Screen.PanelControllers!.ControllersOf(7).OfType<GameInventoryPage>());
        var actions = Assert.IsType<ActionApi>(g.Rig.Session.Actions);
        var ring = page.PackList.FindItem(RingA)!;
        var rightSlot = g.Doll.SlotList(RightRingSlot)!.CellAt(0)!;
        var headSlot = g.Doll.SlotList(HeadSlot)!.CellAt(0)!;

        var mark = actions.NextId();
        DragCell(g, ring, rightSlot);
        Assert.Equal(1, actions.NextId() - mark - 1);
        var wield = Assert.IsType<ActionHandle>(g.Doll.DropOf(RightRingSlot)!.LastAction);
        Assert.Equal((ActionKind.Wield, ActionSource.Ui), (wield.Kind, wield.Source));
        Assert.Equal($"Wield item=0x{RingA:X8} location=0x{RightRing:X8}", wield.Summary, ignoreCase: true);

        // A ring on the head slot does not fit: the release is refused and nothing is sent.
        var mark2 = actions.NextId();
        DragCell(g, ring, headSlot);
        Assert.Equal(0, actions.NextId() - mark2 - 1);
        Assert.Equal(Lifestone.Client.RetailUi.UiDragEnd.Refused, g.Rig.Manager.Input.Drag.LastEnd);
    }

    private static void DragCell(Game g, UiElement source, UiElement target)
    {
        var input = g.Rig.Manager.Input;
        var s = source.ScreenBox;
        var t = target.ScreenBox;
        var (sx, sy) = ((s.X0 + s.X1) / 2, (s.Y0 + s.Y1) / 2);
        input.MouseMove(sx, sy);
        input.MouseButton(UiMouseButton.Left, down: true);
        input.MouseMove(sx + 12, sy + 3);
        input.MouseMove((t.X0 + t.X1) / 2, (t.Y0 + t.Y1) / 2);
        input.MouseButton(UiMouseButton.Left, down: false);
        g.Frames(1);
    }
}

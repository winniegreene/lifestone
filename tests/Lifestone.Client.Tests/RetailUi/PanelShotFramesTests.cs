using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.Repository;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi;

/// <summary>
/// The extra frames of <c>--play-panel-shots</c> (M4 2.2 s4, <see cref="PanelFramesPlan"/>): the container window over a container object within 10 m (a "Chest", a corpse), named after the stand-in's play-check frame, and the engine-free
/// picking of the container. The Godot half (open, wait for the entries, save, close) is <c>PlayMode.PanelShots.cs</c>'s.
/// </summary>
public sealed class PanelShotFramesTests
{
    private const uint Cell = 0x860201AD;
    private const uint ContainerBit = 0x200;
    private const uint CreatureBit = 0x10;
    private const uint DoorFlag = 0x1000;
    private const uint PlayerFlag = 0x8;

    [Fact]
    public void The_panel_shots_add_the_container_window_after_the_pages_and_windows_under_a_name_no_other_frame_has()
    {
        Assert.Equal(["container-window"], PanelFramesPlan.PanelShotExtraFrames);
        Assert.Equal(PanelFramesPlan.ContainerWindowFrame, PanelFramesPlan.PanelShotExtraFrames[0]); // the stand-in's play-check saves the same name in its extra folder
        Assert.DoesNotContain(PanelFramesPlan.PanelShotExtraFrames, name => PanelFramesPlan.AllWindowFrames.Any(window => window.Name == name));
        Assert.DoesNotContain(PanelFramesPlan.PanelShotExtraFrames, name => GamePanelHost.Pages.Select(PanelFramesPlan.FileName).Contains(name + ".png"));
        Assert.Equal(10.0, PanelFramesPlan.ContainerFrameMetres);
        Assert.Equal("container-window: n/a (no container within 10 m)", PanelFramesPlan.NoContainerText);
    }

    [Fact]
    public void The_panel_shots_add_the_examine_frame_after_the_container_window_under_a_name_no_other_frame_has_M4_3_2_s4c()
    {
        Assert.Equal(["examine-kit"], PanelFramesPlan.ExamineShotFrames);
        Assert.Equal("examine-kit: n/a (no healing kit in the pack)", PanelFramesPlan.NoKitText);
        Assert.DoesNotContain(PanelFramesPlan.ExamineShotFrames, name => PanelFramesPlan.PanelShotExtraFrames.Contains(name));
        Assert.DoesNotContain(PanelFramesPlan.ExamineShotFrames, name => PanelFramesPlan.AllWindowFrames.Any(window => window.Name == name));
        Assert.DoesNotContain(PanelFramesPlan.ExamineShotFrames, name => GamePanelHost.Pages.Select(PanelFramesPlan.FileName).Contains(name + ".png"));
    }

    [Fact]
    public void The_examine_shot_finds_a_healing_kit_in_the_main_pack_and_nothing_when_there_is_none_M4_3_2_s4c()
    {
        var h = new ReducerHarness();
        Assert.Equal(0u, PanelFramesPlan.KitInPack(h.State));
        Assert.Throws<ArgumentNullException>(() => PanelFramesPlan.KitInPack(null!));
    }

    [Fact]
    public void The_inventory_page_has_a_main_pack_shot_beside_its_own_named_distinctly_and_no_other_page_has_one_M4_3_1_p()
    {
        // m4-panelshot-mainpack: the hunter's page showed an empty Sack, so no frame listed the looted items; the panel shots take the page twice.
        var frames = PanelFramesPlan.FramesFor(GamePanelHost.Pages.Select(static p => p.PanelId));
        var inventory = Assert.Single(frames, PanelFramesPlan.HasMainPackShot);
        Assert.Equal(GameInventoryPage.Panel, inventory.Page.PanelId);
        Assert.Equal("7-inventory-backpack-and-paperdoll.png", PanelFramesPlan.FileName(inventory)); // the existing shot is unchanged
        Assert.Equal("7-inventory-backpack-and-paperdoll-mainpack.png", PanelFramesPlan.MainPackFileName(inventory));
        Assert.Equal("-mainpack", PanelFramesPlan.MainPackSuffix);

        // Every file name the panel shots save is distinct: each page's own, the inventory's main-pack shot, the windows' and the extra frames'.
        var files = frames.Select(PanelFramesPlan.FileName).Append(PanelFramesPlan.MainPackFileName(inventory))
            .Concat(PanelFramesPlan.AllWindowFrames.Select(PanelFramesPlan.FileName))
            .Concat(PanelFramesPlan.PanelShotExtraFrames.Select(static n => n + ".png")).ToList();
        Assert.Equal(files.Count, files.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(frames.Count - 1, frames.Count(static f => !PanelFramesPlan.HasMainPackShot(f)));
        Assert.Throws<ArgumentNullException>(() => PanelFramesPlan.HasMainPackShot(null!));
    }

    [Fact]
    public void The_panel_shots_take_the_main_pack_shot_after_the_first_inventory_shot_and_before_the_page_closes_through_the_plans_names()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings("src/Lifestone.Game/scripts/Session/PlayMode.PanelShots.cs");
        var first = code.IndexOf("_panelShotMainPack ? PanelFramesPlan.MainPackFileName(frame) : PanelFramesPlan.FileName(frame)", StringComparison.Ordinal);
        var select = code.IndexOf("packPage.ShowContents(PanelFramesPlan.MainPackOwner(packSession.State));", StringComparison.Ordinal);
        var wait = code.IndexOf("PanelFramesPlan.MainPackListed(mainPage, state)", StringComparison.Ordinal);
        var close = code.LastIndexOf("host.Toggle(page.PanelId);", StringComparison.Ordinal); // the page closes at the end of the capture case
        Assert.True(first >= 0 && select > first && wait >= 0 && close > select, "the shot is named by the plan, the main pack is selected after it, listed, and the page closes after both");
        Assert.Contains("PanelShotPhase.MainPack", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_nearest_container_object_within_10_metres_is_chosen_and_creatures_doors_players_far_containers_and_held_items_are_not()
    {
        var h = new ReducerHarness();
        h.EnterWorld(Cell); // the player is at (10, 20, 30)
        Assert.Equal(0u, PanelFramesPlan.ContainerWithin(h.State, 10.0)); // nothing yet: no frame, and the run logs the n/a line

        Add(h, 0x80000001, "Mosswart", 11, 20, itemType: CreatureBit); // 1 m: a creature
        Add(h, 0x80000002, "Wooden Door", 12, 20, itemType: 0, flags: 0x1 | DoorFlag); // 2 m: openable, but a door
        Add(h, 0x80000003, "Another Player", 12, 21, itemType: CreatureBit | ContainerBit, flags: PlayerFlag); // a player is never a container
        Add(h, 0x80000004, "Corpse of Tusker", 40, 20, itemType: ContainerBit, flags: WorldObjectTable.CorpseFlag); // 30 m: too far
        Assert.Equal(0u, PanelFramesPlan.ContainerWithin(h.State, 10.0));

        Add(h, 0x80000005, "Corpse of Mosswart", 16, 20, itemType: ContainerBit, flags: WorldObjectTable.CorpseFlag); // 6 m
        Assert.Equal(0x80000005u, PanelFramesPlan.ContainerWithin(h.State, 10.0));

        Add(h, 0x80000006, "Chest", 13, 20, itemType: ContainerBit); // 3 m: nearer
        Assert.Equal(0x80000006u, PanelFramesPlan.ContainerWithin(h.State, 10.0));

        Add(h, 0x80000007, "Chest (stand-in placeholder)", 11, 20, itemType: 0, flags: StandInChest.OpenableFlag); // 1 m: the stand-in's placeholder chest announces only the openable bit
        Assert.Equal(0x80000007u, PanelFramesPlan.ContainerWithin(h.State, 10.0));
        Assert.Equal(0u, PanelFramesPlan.ContainerWithin(h.State, 0.5)); // nothing within half a metre

        // A pack the player holds (it has a holder and no position) is not a container in the world.
        var held = ReducerHarness.Create(0x80000008, ReducerHarness.Stamps(), null, name: "Sack");
        held = held with { Weenie = held.Weenie with { ItemType = ContainerBit, Header = held.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = ReducerHarness.PlayerGuid } };
        h.Reducer.OnCreateObject(h.Context, held, isUpdate: false);
        Assert.Equal(0x80000007u, PanelFramesPlan.ContainerWithin(h.State, 10.0));
        Assert.Throws<ArgumentNullException>(() => PanelFramesPlan.ContainerWithin(null!, 10.0));
    }

    private static void Add(ReducerHarness h, uint guid, string name, float x, float y, uint itemType, uint flags = 0)
    {
        var created = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, x, y, 30), name: name);
        created = created with { Weenie = created.Weenie with { ItemType = itemType, ObjectDescriptionFlags = flags } };
        h.Reducer.OnCreateObject(h.Context, created, isUpdate: false);
    }
}

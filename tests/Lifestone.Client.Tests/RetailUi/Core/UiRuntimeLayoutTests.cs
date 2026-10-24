using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// Live anchoring (UIElement::UpdateForParentSizeChange over UiAnchor): roots from their layout's design size to the
/// display, children from their parent's original box to its current one, Keep edges keeping the current coordinate.
/// </summary>
public sealed class UiRuntimeLayoutTests
{
    private const uint LayoutId = 0x21000001;
    private static readonly UiEdges All1 = new(UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide);

    [Theory]
    [InlineData(800, 600)]
    [InlineData(1920, 1080)]
    public void A_root_following_the_display_fills_it_and_its_children_follow_their_edges(int width, int height)
    {
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, edges: All1, children:
        [
            // Top-right corner (2112), bottom-left (1221), centred (3333).
            UiDescs.Element(2, UiClassIds.Field, 680, 0, 120, 140, edges: new UiEdges(UiEdge.OppositeSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OppositeSide)),
            UiDescs.Element(3, UiClassIds.Field, 0, 500, 410, 100, edges: new UiEdges(UiEdge.OwnSide, UiEdge.OppositeSide, UiEdge.OppositeSide, UiEdge.OwnSide)),
            UiDescs.Element(4, UiClassIds.Field, 0, 0, 416, 400, edges: new UiEdges(UiEdge.Centre, UiEdge.Centre, UiEdge.Centre, UiEdge.Centre)),
        ]);
        var manager = Manager(UiScalePolicy.Choose(width, height), desc);

        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;

        Assert.Equal(new UiBox(0, 0, width - 1, height - 1), root.Box);
        Assert.Equal(new UiBox(width - 120, 0, width - 1, 139), root.GetChild(2)!.Box);
        Assert.Equal(new UiBox(0, height - 100, 409, height - 1), root.GetChild(3)!.Box);
        Assert.Equal(new UiBox((width / 2) - 208, (height / 2) - 200, (width / 2) + 207, (height / 2) + 199), root.GetChild(4)!.Box);
    }

    [Fact]
    public void A_display_change_re_anchors_from_the_original_boxes_so_resizes_never_accumulate()
    {
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, edges: All1, children:
        [
            UiDescs.Element(2, UiClassIds.Field, 100, 100, 100, 100, edges: new UiEdges(UiEdge.Proportional, UiEdge.Keep, UiEdge.Proportional, UiEdge.Keep)),
        ]);
        var manager = Manager(UiScalePolicy.Choose(1024, 768), desc);
        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;

        // The float width ratio puts 100 at 127 for 800 -> 1024 (the data track's pin).
        Assert.Equal(127, root.GetChild(2)!.Box.X0);

        manager.SetScale(UiScalePolicy.Choose(1600, 600));
        manager.SetScale(UiScalePolicy.Choose(1024, 768));

        Assert.Equal(127, root.GetChild(2)!.Box.X0);
        Assert.Equal(new UiBox(0, 0, 1023, 767), root.Box);
    }

    [Fact]
    public void A_keep_edge_keeps_the_current_coordinate_of_a_moved_element()
    {
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, edges: All1, children:
        [
            UiDescs.Element(2, UiClassIds.Field, 10, 20, 100, 50, edges: UiEdges.None),
        ]);
        var manager = Manager(UiScalePolicy.Choose(800, 600), desc);
        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;
        var child = root.GetChild(2)!;

        child.MoveTo(300, 200);
        manager.SetScale(UiScalePolicy.Choose(1600, 1200, 1));

        Assert.Equal(new UiBox(300, 200, 399, 249), child.Box);
    }

    [Fact]
    public void A_login_root_keeps_its_800_by_600_box_and_a_forced_display_centres_it()
    {
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, edges: UiEdges.None);
        var manager = Manager(UiScalePolicy.Choose(1920, 1080), desc);
        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;
        Assert.Equal(new UiBox(0, 0, 799, 599), root.ScreenBox);

        manager.ForceDisplayResolution(true);

        Assert.Equal(new UiBox(0, 0, 799, 599), root.Box);
        Assert.Equal(new UiBox(560, 240, 1359, 839), root.ScreenBox);
    }

    private static UiElementManager Manager(UiScale scale, params UiElementDesc[] elements) =>
        new(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, elements)]) }, scale);
}

/// <summary>Live anchoring of retail roots from the user's dats (R10: the character-management root at four sizes; the gameplay shell).</summary>
public sealed class UiRuntimeLayoutDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsTheory]
    [InlineData(800, 600, 0, 0)]
    [InlineData(1280, 720, 240, 60)]
    [InlineData(1920, 1080, 560, 240)]
    [InlineData(2560, 1440, 240, 60)]
    public void The_character_management_root_stays_800_by_600_centred_in_the_forced_display(int width, int height, int originX, int originY)
    {
        var layouts = new UiLayouts(fixture.Dats);
        var manager = new UiElementManager(new UiServices { Descs = UiDescSource.From(layouts) }, UiScalePolicy.Choose(width, height));
        manager.ForceDisplayResolution(true);

        var root = manager.CreateRootElement(UiLayoutEnums.CharacterManagementRoot.LayoutEnum, UiLayoutEnums.CharacterManagementRoot.ElementId)!;

        Assert.Equal(new UiBox(0, 0, 799, 599), root.Box);
        Assert.Equal(new UiBox(originX, originY, originX + 799, originY + 599), root.ScreenBox);
        Assert.Equal(8, root.Children.Count);

        // Nothing moved: every element sits where its full desc puts it.
        void Check(UiElement element)
        {
            Assert.Equal(element.Desc.BaseState.Box, element.Box);
            foreach (var child in element.Children)
            {
                Check(child);
            }
        }

        foreach (var child in root.Children)
        {
            Check(child);
        }
    }

    [RequiresLocalDatsTheory]
    [InlineData(1280, 720, 1160, 620, 560, 970, 432, 160)]
    [InlineData(1920, 1080, 1800, 980, 880, 1610, 752, 340)]
    [InlineData(2560, 1440, 2440, 1340, 1200, 2250, 1072, 520)]
    public void The_gameplay_shell_anchors_live_as_the_data_track_worked_out_by_hand(
        int width, int height, int radarX0, int chatY0, int vitalsX0, int toolbarX0, int adminX0, int adminY0)
    {
        var layouts = new UiLayouts(fixture.Dats);
        var manager = new UiElementManager(new UiServices { Descs = UiDescSource.From(layouts) }, UiScalePolicy.Choose(width, height, 1));

        var root = manager.CreateRootElement(UiLayoutEnums.GameplayRoot.LayoutEnum, UiLayoutEnums.GameplayRoot.ElementId)!;
        UiBox Box(uint id) => root.GetChild(id)!.Box;

        Assert.Equal(new UiBox(0, 0, width - 1, height - 1), root.Box);
        Assert.Equal(new UiBox(0, 0, width - 1, height - 1), Box(UiLayoutEnums.Gameplay.SmartBox));
        Assert.Equal(new UiBox(radarX0, 0, width - 1, 139), Box(UiLayoutEnums.Gameplay.Radar));
        Assert.Equal(new UiBox(0, chatY0, 409, height - 1), Box(0x10000601u));
        Assert.Equal(new UiBox(vitalsX0, 0, vitalsX0 + 159, 57), Box(UiLayoutEnums.Gameplay.StackedVitals));
        Assert.Equal(new UiBox(toolbarX0, chatY0, width - 1, height - 1), Box(0x10000603u));
        Assert.Equal(new UiBox(adminX0, adminY0, adminX0 + 415, adminY0 + 399), Box(0x100004D1u));
    }
}

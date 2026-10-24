using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// The anchoring port (<c>StateDesc::UpdateSizeAndPosition</c>, <c>ElementDesc::UpdateSizeAndPosition</c>) against cases
/// worked out by hand (m2-log-in.md §5.5): every edge mode on every side, from an 800×600 parent (and an 800×500 one) to
/// 1280×720, 1920×1080 and 2560×1440.
/// </summary>
/// <remarks>
/// The child is (100,50)-(299,149), 200×100. Hand working, for a new parent W×H from 800×600: dW = W - 800, dH = H - 600.
/// Keep leaves the box; OwnSide moves the right edge by dW and the bottom by dH; OppositeSide moves the left edge by dW
/// and the top by dH (the box turns inside out when the parent grows past it, as in retail); Centre gives
/// (W/2 - 100, H/2 - 50)-(W/2 + 99, H/2 + 49); Proportional multiplies by W/800 rounded to a float and by H/600 as a double:
/// 1280×720: 1.6000000238f and 1.2 give 100→160, 299→478.4→478, 50→60, 149→178.8→178; 1920×1080: 2.4000000954f and 1.8
/// give 240, 717.6→717, 90, 268.2→268; 2560×1440: 3.2000000477f and 2.4 give 320, 956.8→956, 120, 357.6→357.
/// </remarks>
public sealed class UiAnchorTests
{
    private static readonly UiBox Child = new(100, 50, 299, 149);
    private static readonly UiBox Screen800X600 = new(0, 0, 799, 599);

    [Theory]
    // Keep: nothing moves, whatever the parent does.
    [InlineData(1280, 720, UiEdge.Keep, 100, 50, 299, 149)]
    // OwnSide: left and top keep; right + dW, bottom + dH.
    [InlineData(1280, 720, UiEdge.OwnSide, 100, 50, 779, 269)]
    [InlineData(1920, 1080, UiEdge.OwnSide, 100, 50, 1419, 629)]
    // OppositeSide: left + dW, top + dH; right and bottom keep.
    [InlineData(1280, 720, UiEdge.OppositeSide, 580, 170, 299, 149)]
    // Centre: W/2 - 100 .. W/2 + 99, H/2 - 50 .. H/2 + 49.
    [InlineData(1280, 720, UiEdge.Centre, 540, 310, 739, 409)]
    // Proportional: float width ratio, double height ratio, truncated.
    [InlineData(1280, 720, UiEdge.Proportional, 160, 60, 478, 178)]
    [InlineData(1920, 1080, UiEdge.Proportional, 240, 90, 717, 268)]
    [InlineData(2560, 1440, UiEdge.Proportional, 320, 120, 956, 357)]
    public void Every_mode_on_every_side_from_800x600(int width, int height, UiEdge mode, int x0, int y0, int x1, int y1)
    {
        var edges = new UiEdges(mode, mode, mode, mode);

        var box = UiAnchor.UpdateSizeAndPosition(Child, Screen800X600, new UiBox(0, 0, width - 1, height - 1), edges);

        Assert.Equal(new UiBox(x0, y0, x1, y1), box);
    }

    [Theory]
    // From an 800×500 layout (the journal books) to 1920×1080: dW = 1120, dH = 580, height ratio 1080/500 = 2.16.
    [InlineData(UiEdge.OwnSide, 100, 50, 1419, 729)]
    [InlineData(UiEdge.OppositeSide, 1220, 630, 299, 149)]
    [InlineData(UiEdge.Centre, 860, 490, 1059, 589)]
    [InlineData(UiEdge.Proportional, 240, 108, 717, 321)] // 50 × 2.16 = 108; 149 × 2.16 = 321.84
    public void Every_mode_from_an_800x500_layout(UiEdge mode, int x0, int y0, int x1, int y1)
    {
        var box = UiAnchor.UpdateSizeAndPosition(Child, new UiBox(0, 0, 799, 499), new UiBox(0, 0, 1919, 1079), new UiEdges(mode, mode, mode, mode));

        Assert.Equal(new UiBox(x0, y0, x1, y1), box);
    }

    [Fact]
    public void Mixed_edges_anchor_each_side_independently()
    {
        // A window pinned to the bottom-right corner (left and top follow the far side, right and bottom their own).
        var corner = new UiEdges(UiEdge.OppositeSide, UiEdge.OppositeSide, UiEdge.OwnSide, UiEdge.OwnSide);
        Assert.Equal(new UiBox(1220, 530, 1419, 629), UiAnchor.UpdateSizeAndPosition(Child, Screen800X600, new UiBox(0, 0, 1919, 1079), corner));

        // A bar that stretches across the top: left keeps, right follows its side, top and bottom keep.
        var bar = new UiEdges(UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OppositeSide);
        Assert.Equal(new UiBox(100, 50, 1419, 149), UiAnchor.UpdateSizeAndPosition(Child, Screen800X600, new UiBox(0, 0, 1919, 1079), bar));
    }

    [Fact]
    public void The_width_ratio_is_float_rounded_and_the_height_ratio_is_not()
    {
        // 800×600 to 1024×768: both ratios are 1.28. As a float it is 1.27999997f, so a Proportional left edge at 100 lands
        // on 127.999997 → 127; the height ratio stays the double 1.28000000000000003 and 100 lands on 128. A port with a
        // double width ratio would put the left edge on 128.
        var box = new UiBox(100, 100, 199, 199);
        var edges = new UiEdges(UiEdge.Proportional, UiEdge.Proportional, UiEdge.Keep, UiEdge.Keep);

        var anchored = UiAnchor.UpdateSizeAndPosition(box, Screen800X600, new UiBox(0, 0, 1023, 767), edges);

        Assert.Equal(new UiBox(127, 128, 199, 199), anchored);
        Assert.Equal(128, (int)(100 * (1024.0 / 800.0))); // what the double ratio would have given
    }

    [Fact]
    public void An_empty_old_parent_gives_a_zero_ratio()
    {
        // Old parent 0 wide and 0 high (x1 = x0 - 1): both ratios are 0, so Proportional edges go to 0; dW and dH are the
        // new size.
        var empty = new UiBox(0, 0, -1, -1);
        var all = new UiEdges(UiEdge.Proportional, UiEdge.Proportional, UiEdge.Proportional, UiEdge.Proportional);
        Assert.Equal(new UiBox(0, 0, 0, 0), UiAnchor.UpdateSizeAndPosition(Child, empty, Screen800X600, all));

        var own = new UiEdges(UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide);
        Assert.Equal(new UiBox(100, 50, 1099, 749), UiAnchor.UpdateSizeAndPosition(Child, empty, Screen800X600, own));
    }

    [Fact]
    public void Centre_uses_C_integer_division_and_loses_a_pixel_on_odd_sizes()
    {
        var centre = new UiEdges(UiEdge.Centre, UiEdge.Centre, UiEdge.Centre, UiEdge.Centre);

        // A 201×101 child in an 801×601 parent: 801/2 = 400, 201/2 = 100 → x 300..499 (200 wide); 601/2 = 300, 101/2 = 50
        // → y 250..349 (100 high).
        var odd = UiAnchor.UpdateSizeAndPosition(new UiBox(0, 0, 200, 100), Screen800X600, new UiBox(0, 0, 800, 600), centre);
        Assert.Equal(new UiBox(300, 250, 499, 349), odd);
        Assert.Equal((200, 100), (odd.Width, odd.Height));

        // A child wider than its parent: 101/2 - 301/2 = 50 - 150 = -100 (truncation toward zero, no clamp).
        var wide = UiAnchor.UpdateSizeAndPosition(new UiBox(0, 0, 300, 10), Screen800X600, new UiBox(0, 0, 100, 10), centre);
        Assert.Equal(new UiBox(-100, 0, 199, 9), wide);
    }

    [Fact]
    public void Only_the_parents_sizes_matter()
    {
        var edges = new UiEdges(UiEdge.OppositeSide, UiEdge.Centre, UiEdge.OwnSide, UiEdge.Proportional);
        var atOrigin = UiAnchor.UpdateSizeAndPosition(Child, Screen800X600, new UiBox(0, 0, 1279, 719), edges);
        var elsewhere = UiAnchor.UpdateSizeAndPosition(Child, new UiBox(40, 30, 839, 629), new UiBox(-7, 9, 1272, 728), edges);

        Assert.Equal(atOrigin, elsewhere);
    }

    [Fact]
    public void An_element_re_anchors_its_children_by_their_own_edges_but_not_its_other_states()
    {
        // A 200×100 panel pinned top-left; its child is pinned to the panel's bottom-right corner.
        var child = new UiElementDesc
        {
            ElementId = 2,
            Edges = new UiEdges(UiEdge.OppositeSide, UiEdge.OppositeSide, UiEdge.OwnSide, UiEdge.OwnSide),
            BaseState = new UiStateDesc { X = 150, Y = 80, Width = 40, Height = 15 },
        };
        var state = new UiStateDesc { StateId = 6 };
        var panel = new UiElementDesc
        {
            ElementId = 1,
            Edges = new UiEdges(UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide),
            BaseState = new UiStateDesc { X = 100, Y = 50, Width = 200, Height = 100 },
            States = UiOrdered.Dictionary([(6u, state)], k => new InvalidOperationException()),
            Children = UiOrdered.Dictionary([(2u, child)], k => new InvalidOperationException()),
        };

        // The panel grows by dW = 480, dH = 120 to (100,50)-(779,269), 680×220; its child moves by the panel's own growth.
        var anchored = UiAnchor.UpdateSizeAndPosition(panel, Screen800X600, new UiBox(0, 0, 1279, 719));

        Assert.Equal(new UiBox(100, 50, 779, 269), anchored.Box);
        Assert.Equal(new UiBox(630, 200, 669, 214), anchored.Children[2].Box); // 150 + 480, 80 + 120, 189 + 480, 94 + 120
        Assert.Same(state, anchored.States[6]); // ElementDesc::UpdateSizeAndPosition leaves the state table alone
        Assert.Equal(new UiBox(150, 80, 189, 94), panel.Children[2].Box); // the input is unchanged (immutable)

        // The state overload re-anchors a state's own box (retail does this for inherited states in Incorporate).
        var moved = UiAnchor.UpdateSizeAndPosition(new UiStateDesc { X = 10, Y = 10, Width = 5, Height = 5 }, Screen800X600, new UiBox(0, 0, 1279, 719), panel.Edges);
        Assert.Equal(new UiBox(10, 10, 494, 134), moved.Box);
    }
}

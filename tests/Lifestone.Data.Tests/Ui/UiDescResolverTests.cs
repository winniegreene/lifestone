using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// Template inheritance on synthetic layouts (m2-plan.md R3): <c>LayoutDesc::InqFullDesc</c>, <c>ElementDesc::Incorporate</c>
/// and <c>StateDesc::Incorporate</c>, rule by rule.
/// </summary>
public sealed class UiDescResolverTests
{
    private const uint TemplateLayout = 0x21000040u;
    private const uint ScreenLayout = 0x21000004u;

    [Fact]
    public void A_desc_with_a_class_is_used_as_authored()
    {
        var authored = Element(1, type: 3, layout: ScreenLayout, box: (0, 0, 10, 10));
        var layouts = Layouts(Layout(ScreenLayout, authored));

        var result = layouts.InqFullDesc(authored);

        Assert.True(result.Succeeded);
        Assert.Same(authored, result.Full);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Flagged_fields_override_and_flags_are_ORed()
    {
        var template = Element(0x100002D1, type: 1, layout: TemplateLayout, box: (5, 6, 170, 99)) with
        {
            BaseState = new UiStateDesc
            {
                Incorporation = UiIncorporation.X | UiIncorporation.Y | UiIncorporation.Width | UiIncorporation.Height | UiIncorporation.ZLevel,
                X = 5, Y = 6, Width = 170, Height = 99, ZLevel = 3, PassToChildren = true,
            },
            DefaultState = 1,
            Edges = Edges(1, 1, 1, 1),
            ReadOrder = 9,
        };

        // The local desc moves the element (X, Y) and clears pass-to-children; its width, height and z level are not
        // flagged, so its zeros do not override.
        var local = Partial(0x1000039E, ScreenLayout, TemplateLayout, 0x100002D1) with
        {
            BaseState = new UiStateDesc { Incorporation = UiIncorporation.PassToChildren | UiIncorporation.X | UiIncorporation.Y, X = 36, Y = 522 },
            DefaultState = 0,
            Edges = Edges(1, 1, 2, 2),
            ReadOrder = 3,
        };
        var layouts = Layouts(Layout(TemplateLayout, template), Layout(ScreenLayout, local));

        var full = layouts.RequireFullDesc(local);

        Assert.Equal(new UiBox(36, 522, 205, 620), full.Box);
        Assert.Equal(3u, full.BaseState.ZLevel);
        Assert.False(full.BaseState.PassToChildren);
        Assert.Equal((UiIncorporation)0x3F, full.BaseState.Incorporation);

        // Identity, default state, edges and read order are the local desc's; the class and base links the template's.
        Assert.Equal((0x1000039Eu, 0u, 3u, Edges(1, 1, 2, 2)), (full.ElementId, full.DefaultState, full.ReadOrder, full.Edges));
        Assert.Equal((1u, 0u, 0u), (full.Type, full.BaseLayout, full.BaseElement));
        Assert.Equal(ScreenLayout, full.SourceLayout);
    }

    [Fact]
    public void Media_concatenate_and_properties_override_or_join()
    {
        var baseImage = new UiImageMedia(0x06000001u, UiDrawMode.Normal);
        var localImage = new UiImageMedia(0x06000002u, UiDrawMode.AlphaBlend);
        var template = Element(1, type: 1, layout: TemplateLayout, box: (0, 0, 10, 10)) with
        {
            BaseState = new UiStateDesc
            {
                Media = [baseImage],
                Properties = Props(new UiBoolProperty(UiPropertyIds.ElementHide, true), new UiIntegerProperty(UiPropertyIds.TextLeftMargin, 4)),
            },
        };
        var local = Partial(2, ScreenLayout, TemplateLayout, 1) with
        {
            BaseState = new UiStateDesc
            {
                Media = [localImage],
                Properties = Props(new UiBoolProperty(UiPropertyIds.ElementHide, false), new UiEnumProperty(UiPropertyIds.TextHorizontalJustification, 2)),
            },
        };

        var full = Layouts(Layout(TemplateLayout, template), Layout(ScreenLayout, local)).RequireFullDesc(local);

        Assert.Equal([baseImage, localImage], full.Media);
        Assert.Equal([UiPropertyIds.ElementHide, UiPropertyIds.TextLeftMargin, UiPropertyIds.TextHorizontalJustification], full.Properties.Keys);
        Assert.False(full.Properties.GetBool(UiPropertyIds.ElementHide));
        Assert.Equal(4, full.Properties.GetInteger(UiPropertyIds.TextLeftMargin));
        Assert.Equal(2u, full.Properties.GetEnum(UiPropertyIds.TextHorizontalJustification));
    }

    [Fact]
    public void States_merge_by_id_and_keep_the_base_state_id()
    {
        var template = Element(1, type: 1, layout: TemplateLayout, box: (0, 0, 10, 10)) with
        {
            States = States(
                new UiStateDesc { StateId = 1, Media = [new UiImageMedia(0x06000001u, UiDrawMode.Normal)] },
                new UiStateDesc { StateId = 2, Properties = Props(new UiBoolProperty(UiPropertyIds.ElementHide, true)) }),
        };
        var local = Partial(2, ScreenLayout, TemplateLayout, 1) with
        {
            States = States(
                new UiStateDesc { StateId = 2, Incorporation = UiIncorporation.PassToChildren, PassToChildren = true },
                new UiStateDesc { StateId = 6, Media = [new UiImageMedia(0x06000006u, UiDrawMode.Normal)] }),
        };

        var full = Layouts(Layout(TemplateLayout, template), Layout(ScreenLayout, local)).RequireFullDesc(local);

        Assert.Equal([1u, 2u, 6u], full.States.Keys);
        Assert.Same(template.States[1], full.States[1]); // inherited only, size unchanged: as it was
        Assert.True(full.States[2].PassToChildren);
        Assert.True(full.States[2].Properties.GetBool(UiPropertyIds.ElementHide));
        Assert.Equal(UiIncorporation.PassToChildren, full.States[2].Incorporation);
        Assert.Same(local.States[6], full.States[6]);
    }

    [Fact]
    public void Children_merge_by_id_and_new_ones_follow_the_inherited_ones_in_read_order()
    {
        var template = Element(1, type: 3, layout: TemplateLayout, box: (0, 0, 99, 99)) with
        {
            Children = Children(
                Element(0x10, type: 1, layout: TemplateLayout, box: (0, 0, 10, 10)) with { ReadOrder = 1 },
                Element(0x11, type: 12, layout: TemplateLayout, box: (20, 0, 10, 10)) with { ReadOrder = 2 }),
        };

        // The local desc overrides child 0x11 (moves it) and adds 0x12. One base child (0x10) is not mentioned, so the new
        // child's read order 1 becomes 2.
        var local = Partial(2, ScreenLayout, TemplateLayout, 1) with
        {
            Children = Children(
                new UiElementDesc { ElementId = 0x11, ReadOrder = 7, SourceLayout = ScreenLayout, BaseState = new UiStateDesc { Incorporation = UiIncorporation.X, X = 40 } },
                Element(0x12, type: 5, layout: ScreenLayout, box: (60, 0, 10, 10)) with { ReadOrder = 1 }),
        };

        var full = Layouts(Layout(TemplateLayout, template), Layout(ScreenLayout, local)).RequireFullDesc(local);

        Assert.Equal([0x10u, 0x11u, 0x12u], full.Children.Keys);
        Assert.Same(template.Children[0x10], full.Children[0x10]);
        Assert.Equal((12u, 7u, 40, 10), (full.Children[0x11].Type, full.Children[0x11].ReadOrder, full.Children[0x11].Box.X0, full.Children[0x11].Box.Width));
        Assert.Equal((5u, 2u), (full.Children[0x12].Type, full.Children[0x12].ReadOrder));
    }

    [Fact]
    public void Inherited_states_and_children_re_anchor_when_the_size_changes()
    {
        // A 100×50 template with a child pinned to its right edge and a state of its own (StateDesc boxes are 0 unless set).
        var template = Element(1, type: 3, layout: TemplateLayout, box: (0, 0, 100, 50)) with
        {
            States = States(new UiStateDesc { StateId = 1 }),
            Children = Children(Element(0x10, type: 1, layout: TemplateLayout, box: (80, 10, 10, 10)) with { Edges = Edges(2, 1, 1, 1) }),
        };

        // The local desc widens it to 150 (dW = 50, dH = 0) with OwnSide edges.
        var local = Partial(2, ScreenLayout, TemplateLayout, 1) with
        {
            Edges = Edges(1, 1, 1, 1),
            BaseState = new UiStateDesc { Incorporation = UiIncorporation.Width, Width = 150 },
        };

        var full = Layouts(Layout(TemplateLayout, template), Layout(ScreenLayout, local)).RequireFullDesc(local);

        Assert.Equal(new UiBox(0, 0, 149, 49), full.Box);
        Assert.Equal(new UiBox(130, 10, 139, 19), full.Children[0x10].Box); // (80,10)-(89,19), left and right + 50

        // The inherited state's box (0,0)-(-1,-1) re-anchors by the element's edges: its right edge + 50 → 50 wide.
        Assert.Equal(new UiBox(0, 0, 49, -1), full.States[1].Box);

        // Same size: nothing inherited is re-anchored, even a centred child.
        var centred = template with
        {
            Children = Children(Element(0x10, type: 1, layout: TemplateLayout, box: (3, 3, 10, 10)) with { Edges = Edges(3, 3, 3, 3) }),
        };
        var same = Partial(2, ScreenLayout, TemplateLayout, 1) with { BaseState = new UiStateDesc { Incorporation = UiIncorporation.X, X = 7 } };
        var unchanged = Layouts(Layout(TemplateLayout, centred), Layout(ScreenLayout, same)).RequireFullDesc(same);
        Assert.Same(centred.Children[0x10], unchanged.Children[0x10]);
        Assert.Same(centred.States[1], unchanged.States[1]);
    }

    [Fact]
    public void Chains_resolve_through_every_level_and_the_bases_are_cached()
    {
        // C (screen) → B (a partial template) → A (a full template).
        const uint middleLayout = 0x21000041u;
        var a = Element(1, type: 1, layout: TemplateLayout, box: (0, 0, 50, 20)) with
        {
            BaseState = new UiStateDesc { Width = 50, Height = 20, Properties = Props(new UiIntegerProperty(UiPropertyIds.TextLeftMargin, 1)) },
        };
        var b = Partial(2, middleLayout, TemplateLayout, 1) with
        {
            BaseState = new UiStateDesc { Properties = Props(new UiIntegerProperty(UiPropertyIds.TextRightMargin, 2)) },
        };
        var c = Partial(3, ScreenLayout, middleLayout, 2) with
        {
            BaseState = new UiStateDesc { Properties = Props(new UiIntegerProperty(UiPropertyIds.TextTopMargin, 3)) },
        };
        var reads = new List<uint>();
        var byId = new[] { Layout(TemplateLayout, a), Layout(middleLayout, b), Layout(ScreenLayout, c) }.ToDictionary(l => l.Id);
        var layouts = new UiLayouts(id => { reads.Add(id); return byId.GetValueOrDefault(id); }, _ => null, byId.Keys.ToArray());

        var first = layouts.RequireFullDesc(c);
        var second = layouts.RequireFullDesc(c);

        Assert.Equal((1u, 3u), (first.Type, first.ElementId));
        Assert.Equal([UiPropertyIds.TextLeftMargin, UiPropertyIds.TextRightMargin, UiPropertyIds.TextTopMargin], first.Properties.Keys);
        Assert.Equal(first.Properties.Values, second.Properties.Values);
        Assert.Equal([middleLayout, TemplateLayout], reads); // each layout read once; the second call hits the base cache
    }

    [Fact]
    public void A_missing_base_layout_or_element_fails_naming_the_chain()
    {
        var local = Partial(0x1000039E, ScreenLayout, TemplateLayout, 0x100002D1);
        var nested = Element(0x100002D1, type: 1, layout: TemplateLayout, box: (0, 0, 1, 1));
        var container = Element(0x100002D0, type: 3, layout: TemplateLayout, box: (0, 0, 9, 9)) with { Children = Children(nested) };

        var noLayout = Layouts(Layout(ScreenLayout, local)).InqFullDesc(local);
        Assert.False(noLayout.Succeeded);
        Assert.Null(noLayout.Full);
        Assert.Equal(
            "element 0x1000039E of layout 0x21000004 does not resolve: base 0x21000040/0x100002D1: layout 0x21000040 is not in client_local_English.dat",
            noLayout.Failure);

        // The base must be top-level in its layout (LayoutDesc::AccessElementDesc); a nested element of that id does not count.
        var layouts = Layouts(Layout(TemplateLayout, container), Layout(ScreenLayout, local));
        Assert.Equal(
            "element 0x1000039E of layout 0x21000004 does not resolve: base 0x21000040/0x100002D1: layout 0x21000040 has no top-level element 0x100002D1",
            layouts.InqFullDesc(local).Failure);
        var error = Assert.Throws<InvalidDataException>(() => layouts.RequireFullDesc(local));
        Assert.StartsWith("TODO(retailui-data): element 0x1000039E of layout 0x21000004 does not resolve", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inheritance_cycle_fails_instead_of_recursing()
    {
        const uint otherLayout = 0x21000041u;
        var one = Partial(1, TemplateLayout, otherLayout, 2);
        var two = Partial(2, otherLayout, TemplateLayout, 1);
        var screen = Partial(3, ScreenLayout, TemplateLayout, 1);
        var layouts = Layouts(Layout(TemplateLayout, one), Layout(otherLayout, two), Layout(ScreenLayout, screen));

        var result = layouts.InqFullDesc(screen);

        Assert.False(result.Succeeded);
        Assert.EndsWith(
            "inheritance cycle (0x21000040/0x00000001 -> 0x21000041/0x00000002 -> 0x21000040/0x00000001)",
            result.Failure,
            StringComparison.Ordinal);
    }

    private static UiElementDesc Element(uint id, uint type, uint layout, (int X, int Y, int W, int H) box) => new()
    {
        ElementId = id,
        Type = type,
        SourceLayout = layout,
        BaseState = new UiStateDesc
        {
            Incorporation = UiIncorporation.X | UiIncorporation.Y | UiIncorporation.Width | UiIncorporation.Height,
            X = box.X,
            Y = box.Y,
            Width = box.W,
            Height = box.H,
        },
    };

    private static UiElementDesc Partial(uint id, uint layout, uint baseLayout, uint baseElement) => new()
    {
        ElementId = id,
        SourceLayout = layout,
        BaseLayout = baseLayout,
        BaseElement = baseElement,
    };

    private static UiEdges Edges(uint left, uint top, uint right, uint bottom) => new((UiEdge)left, (UiEdge)top, (UiEdge)right, (UiEdge)bottom);

    private static IReadOnlyDictionary<uint, UiProperty> Props(params UiProperty[] properties) =>
        UiOrdered.Dictionary(properties.Select(p => (p.Id, p)), k => new InvalidOperationException());

    private static IReadOnlyDictionary<uint, UiStateDesc> States(params UiStateDesc[] states) =>
        UiOrdered.Dictionary(states.Select(s => (s.StateId, s)), k => new InvalidOperationException());

    private static IReadOnlyDictionary<uint, UiElementDesc> Children(params UiElementDesc[] children) =>
        UiOrdered.Dictionary(children.Select(c => (c.ElementId, c)), k => new InvalidOperationException());

    private static UiLayout Layout(uint id, params UiElementDesc[] elements) => new(id, 800, 600, Children(elements));

    private static UiLayouts Layouts(params UiLayout[] layouts) => UiLayouts.FromLayouts(layouts);
}

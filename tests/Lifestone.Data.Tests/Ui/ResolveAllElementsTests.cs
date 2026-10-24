using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// Template inheritance and anchoring on the user's layouts (m2-plan.md R3): every element resolves, every base link
/// names a top-level element, and the gameplay shell anchors to wide screens as worked out by hand.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class ResolveAllElementsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly UiBox Screen800X600 = new(0, 0, 799, 599);

    private DatSet Dats => fixture.Dats;

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_element_of_every_layout_resolves_as_its_screen_would_create_it_but_one_orphan()
    {
        var layouts = new UiLayouts(Dats);
        var created = 0;
        var classes = new HashSet<uint>();
        var failures = new List<string>();

        // As the runtime creates a tree: resolve the desc, then each child of the full desc (inherited ones included).
        void Create(UiElementDesc desc, int depth)
        {
            Assert.True(depth < 32, $"{desc.Describe()}: the tree is suspiciously deep");
            var result = layouts.InqFullDesc(desc);
            if (!result.Succeeded)
            {
                failures.Add(result.Failure!);
                return;
            }

            var full = result.Full!;
            Assert.NotEqual(0u, full.Type);
            Assert.Equal(desc.ElementId, full.ElementId);
            created++;
            classes.Add(full.Type);
            foreach (var child in full.Children.Values)
            {
                Create(child, depth + 1);
            }
        }

        foreach (var id in layouts.LayoutIds)
        {
            foreach (var top in layouts.GetLayout(id).Elements.Values)
            {
                Create(top, 0);
            }
        }

        // One top-level template of the shared layout 0x21000040 names a base element that no layout holds. Nothing
        // inherits from it and no property names it, so retail never creates it; if it did, LayoutDesc::InqFullDesc would
        // fail as this does.
        Assert.Equal(
            ["element 0x100002C2 of layout 0x21000040 does not resolve: base 0x21000040/0x10000480: layout 0x21000040 has no top-level element 0x10000480"],
            failures);
        Assert.Equal(9525, created); // the 2,161 authored descs that resolve plus the children they inherit, at every level
        Assert.Equal(100, classes.Count);
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_base_link_names_a_top_level_element_of_its_base_layout()
    {
        var layouts = new UiLayouts(Dats);
        var partial = layouts.LayoutIds.SelectMany(id => layouts.GetLayout(id).AllElements()).Where(e => e.Type == 0).ToArray();

        Assert.Equal(1088, partial.Length);
        var orphan = Assert.Single(partial, e => layouts.AccessElementDesc(e.BaseLayout, e.BaseElement) is null);
        Assert.Equal((0x100002C2u, 0x21000040u, 0x21000040u, 0x10000480u), (orphan.ElementId, orphan.SourceLayout, orphan.BaseLayout, orphan.BaseElement));
        Assert.DoesNotContain(layouts.LayoutIds.SelectMany(id => layouts.GetLayout(id).AllElements()), e => e.ElementId == orphan.BaseElement);
        Assert.DoesNotContain(partial, e => (e.BaseLayout, e.BaseElement) == (orphan.SourceLayout, orphan.ElementId));
        partial = partial.Where(e => e != orphan).ToArray();

        // How deep the chains go: up to four bases; most partial descs name a full template directly.
        int Depth(UiElementDesc e) => e.Type != 0 ? 0 : 1 + Depth(layouts.AccessElementDesc(e.BaseLayout, e.BaseElement)!);
        Assert.Equal(
            "1:768 2:183 3:119 4:17",
            string.Join(" ", partial.GroupBy(Depth).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
        Assert.Equal(196, partial.Select(e => (e.BaseLayout, e.BaseElement)).Distinct().Count());
    }

    [RequiresLocalDatsFact]
    public void Proportional_edges_only_meet_non_negative_coordinates()
    {
        // UiAnchor truncates toward zero; only a negative coordinate under a Proportional edge would test that reading.
        var layouts = new UiLayouts(Dats);
        var proportional = layouts.LayoutIds.SelectMany(id => layouts.GetLayout(id).AllElements())
            .Select(e => layouts.InqFullDesc(e).Full)
            .OfType<UiElementDesc>()
            .Where(e => e.Edges.Left == UiEdge.Proportional || e.Edges.Right == UiEdge.Proportional)
            .ToArray();

        Assert.Equal(11, proportional.Length); // 8 left edges and 8 right edges, on 11 elements
        Assert.All(proportional, e => Assert.True(e.Box.X0 >= 0 && e.Box.X1 >= 0, e.Describe()));
    }

    [RequiresLocalDatsTheory]
    // Hand-worked from the 800×600 boxes and edges of the gameplay root's windows (dW = W - 800, dH = H - 600):
    // the root and the SmartBox (0,0)-(799,599), edges 1111, fill the screen;
    // the radar (680,0)-(799,139), edges 2112, keeps to the top-right corner;
    // the main chat (0,500)-(409,599), edges 1221, keeps to the bottom-left corner;
    // the stacked vitals (0,0)-(159,57), edges 3132, centre at the top: W/2 - 80 .. W/2 + 79;
    // the toolbar (490,500)-(799,599), edges 2211, keeps to the bottom-right corner;
    // the admin window (0,0)-(415,399), edges 3333, centres: W/2 - 208 .. W/2 + 207, H/2 - 200 .. H/2 + 199.
    [InlineData(1280, 720, 1160, 620, 560, 970, 432, 160)]
    [InlineData(1920, 1080, 1800, 980, 880, 1610, 752, 340)]
    [InlineData(2560, 1440, 2440, 1340, 1200, 2250, 1072, 520)]
    public void The_gameplay_shell_anchors_to_wide_screens_as_worked_out_by_hand(
        int width, int height, int radarX0, int chatY0, int vitalsX0, int toolbarX0, int adminX0, int adminY0)
    {
        var layouts = new UiLayouts(Dats);
        var root = layouts.RequireFullDesc(layouts.GetRootDesc(UiLayoutEnums.GameplayRoot));
        var screen = new UiBox(0, 0, width - 1, height - 1);

        var anchored = UiAnchor.UpdateSizeAndPosition(root, Screen800X600, screen);
        UiBox Box(uint id) => anchored.Children[id].Box;

        Assert.Equal(screen, anchored.Box);
        Assert.Equal(screen, Box(UiLayoutEnums.Gameplay.SmartBox));
        Assert.Equal(new UiBox(radarX0, 0, width - 1, 139), Box(UiLayoutEnums.Gameplay.Radar));
        Assert.Equal(new UiBox(0, chatY0, 409, height - 1), Box(0x10000601u));
        Assert.Equal(new UiBox(vitalsX0, 0, vitalsX0 + 159, 57), Box(UiLayoutEnums.Gameplay.StackedVitals));
        Assert.Equal(new UiBox(toolbarX0, chatY0, width - 1, height - 1), Box(0x10000603u));
        Assert.Equal(new UiBox(adminX0, adminY0, adminX0 + 415, adminY0 + 399), Box(0x100004D1u));
    }

    [RequiresLocalDatsFact]
    public void The_gameplay_shell_reaches_these_templates()
    {
        // Input to D36: the layouts the gameplay root's tree inherits from, at any depth.
        var layouts = new UiLayouts(Dats);
        var reached = new SortedSet<string>(StringComparer.Ordinal);

        void Walk(UiElementDesc desc)
        {
            if (desc.Type == 0)
            {
                reached.Add(Name(desc.BaseLayout));
                var baseDesc = layouts.AccessElementDesc(desc.BaseLayout, desc.BaseElement)!;
                if (baseDesc.Type == 0)
                {
                    Walk(baseDesc);
                }
            }

            foreach (var child in layouts.RequireFullDesc(desc).Children.Values)
            {
                Walk(child);
            }
        }

        Walk(layouts.GetRootDesc(UiLayoutEnums.GameplayRoot));

        // The windows are floating ones, but what they hold reuses templates of the classic layouts (classic_chat,
        // classic_toolbar, classic_inventory, classic_spellbook and many more). The classic radar, vitals, panel,
        // indicators, environment panel and status bar layouts are not reached at all.
        Assert.Equal(13, reached.Count(n => n.StartsWith("0x", StringComparison.Ordinal))); // unnamed template layouts
        Assert.Equal(11, reached.Count(n => n.StartsWith("classic_floaty", StringComparison.Ordinal)));
        Assert.Equal(75, reached.Count);
        Assert.Contains("classic_smartbox", reached);
        Assert.Contains("classic_chat", reached);
        Assert.Contains("classic_toolbar", reached);
        Assert.Contains("classic_characterinfo", reached);
        Assert.Contains("classic_spewbox", reached);
        Assert.Contains("classic_powerbar", reached);
        Assert.DoesNotContain("classic_radar", reached);
        Assert.DoesNotContain("classic_vitals", reached);
        Assert.DoesNotContain("classic_panel", reached);
        Assert.DoesNotContain("classic_indicators", reached);
        Assert.DoesNotContain("classic_envpanel", reached);
        Assert.DoesNotContain("classic_statusbar", reached);
    }

    private string Name(uint layoutId) =>
        Dats.EnumMaps.Entries(EnumGroup.UiLayout).Where(kv => kv.Value == layoutId)
            .Select(kv => Dats.EnumMaps.NameOf(EnumGroup.UiLayout, kv.Key)).SingleOrDefault() ?? $"0x{layoutId:X8}";
}

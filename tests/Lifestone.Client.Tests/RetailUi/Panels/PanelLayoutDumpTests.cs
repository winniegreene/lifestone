using System.Globalization;
using System.Text;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Data.Checkout;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The layout of the panel pages M3 2.2's slices build (s0, docs/milestones/m3-plan.md section 4.6): for each of the pages 11 (skills and attributes), 4 (beneficial effects), 5 (harmful effects), 15 (vitae),
/// 7 (inventory) and 13 (spellbook) the page element's whole subtree is written to <c>cache/m3/2-2/layout-&lt;panelId&gt;.txt</c> (gitignored; derived from the dats, never committed): per element its id, its
/// class (id and <see cref="UiClassRegistry.NameOf"/>), its template (base element and layout), position and size, the properties with raw ids and values (a string is a table id and a token, never text), the
/// media (image ids) per state and the states. The classes each page holds are pinned, so a slice learns at once if its page uses <c>UIElement_ItemList</c> (0x10000031) or <c>UIElement_UIItem</c> (0x10000032).
/// </summary>
public sealed class PanelLayoutDumpTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The pages a section 4.6 slice builds.</summary>
    private static readonly uint[] Pages = [11, 4, 5, 15, 7, 13];

    /// <summary>The retail classes each page's subtree holds (the page itself included), by panel id: pinned from the dump.</summary>
    private static readonly Dictionary<uint, uint[]> PinnedClasses = new()
    {
        [11] = [0x01, 0x03, 0x05, 0x07, 0x08, 0x0B, 0x0C, 0x1000002A, 0x1000002B, 0x10000046], // gmAttributeUI and gmSkillUI tabs; no item list
        [4] = [0x01, 0x03, 0x05, 0x0B, 0x0C, 0x1000001B], // gmEffectsUI; no item list
        [5] = [0x01, 0x03, 0x05, 0x0B, 0x0C, 0x1000001B], // gmEffectsUI again for the harmful page
        [15] = [0x01, 0x03, 0x0C, 0x10000020], // gmVitaeUI; no item list
        [7] = [0x01, 0x03, 0x07, 0x0B, 0x0C, 0x0D, 0x10000021, 0x10000022, 0x10000023, 0x10000024, 0x10000031], // the inventory page holds UIElement_ItemList (0x10000031); UIElement_UIItem (0x10000032) is made by the item list at run time
        [13] = [0x01, 0x03, 0x05, 0x08, 0x0B, 0x0C, 0x1000002E, 0x1000002F, 0x10000031], // the spell page holds UIElement_ItemList (0x10000031)
    };

    private static string CacheFolder()
    {
        var root = CheckoutLocator.EnclosingCheckouts(AppContext.BaseDirectory).FirstOrDefault()
            ?? throw new InvalidOperationException("TODO(panel-layout-dump): the test is not running inside a checkout");
        return Path.Combine(root, "cache", "m3", "2-2");
    }

    private (UiLayouts Layouts, UiElementDesc Window) FindHost()
    {
        var layouts = new UiLayouts(fixture.Dats);
        foreach (var layoutId in layouts.LayoutIds)
        {
            var layout = layouts.GetLayout(layoutId);
            if (layout.AllElements().FirstOrDefault(static e => e.ElementId == GamePanelHost.WindowId) is { } window)
            {
                return (layouts, layouts.RequireFullDesc(window));
            }
        }

        throw new InvalidDataException($"the panel host window 0x{GamePanelHost.WindowId:X8} is in no layout");
    }

    private static UiElementDesc? Find(UiLayouts layouts, UiElementDesc local, uint elementId)
    {
        var full = layouts.RequireFullDesc(local);
        if (full.ElementId == elementId)
        {
            return full;
        }

        foreach (var child in full.Children.Values)
        {
            if (Find(layouts, child, elementId) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void Classes(UiLayouts layouts, UiElementDesc local, SortedSet<uint> into)
    {
        var full = layouts.RequireFullDesc(local);
        into.Add(full.Type);
        foreach (var child in full.Children.Values)
        {
            Classes(layouts, child, into);
        }
    }

    [RequiresLocalDatsFact]
    public void Each_panel_page_the_slices_build_is_dumped_and_the_classes_it_holds_are_pinned()
    {
        var (layouts, window) = FindHost();
        var folder = CacheFolder();
        Directory.CreateDirectory(folder);
        var differences = new List<string>();
        foreach (var panelId in Pages)
        {
            var page = GamePanelHost.Pages.Single(p => p.PanelId == panelId);
            var desc = Find(layouts, window, page.ElementId) ?? throw new InvalidDataException($"the page 0x{page.ElementId:X8} ({page.Name}) is not in the host's layout");
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"# panel {panelId} page 0x{page.ElementId:X8} ({page.Name}): the layout subtree, resolved (derived from the retail dats; never committed)\n");
            text.Append("# element id | class | template (base element @ base layout) | source layout | box x,y wxh | z | default state | edges | properties id=value | media | states\n");
            Dump(layouts, desc, 0, text);
            File.WriteAllText(Path.Combine(folder, $"layout-{panelId}.txt"), text.ToString());

            var classes = new SortedSet<uint>();
            Classes(layouts, desc, classes);
            if (!PinnedClasses.TryGetValue(panelId, out var pinned) || !pinned.Order().SequenceEqual(classes))
            {
                differences.Add($"page {panelId} holds [{string.Join(", ", classes.Select(c => $"0x{c:X8}"))}], pinned [{string.Join(", ", (pinned ?? []).Order().Select(c => $"0x{c:X8}"))}]");
            }
        }

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    /// <summary>
    /// The floaty windows M3 3.2's panel slices build (s0, docs/milestones/m3-plan.md section 4.8): the combat panel (<c>gmCombatUI</c>), the power bar (<c>gmPowerbarUI</c>) and the spellcasting panel (<c>gmSpellcastingUI</c>), by
    /// retail class, with the file each is dumped to (<c>cache/m3/3-2/</c>) and the retail classes its subtree holds (the window itself included): pinned from the dump. They are windows of the gameplay layout
    /// (<see cref="UiLayoutEnums.GameplayRoot"/>), not host pages.
    /// </summary>
    private static readonly (string Name, uint ClassId, string File, uint[] PinnedClasses)[] Windows =
    [
        ("gmCombatUI", 0x1000000C, "layout-combat.txt", [0x01, 0x03, 0x07, 0x0B, 0x0C, 0x11, 0x1000000C, 0x10000035]), // window 0x1000005C, in gmFloatyCombatPanelUI 0x100006B5
        ("gmPowerbarUI", 0x1000000F, "layout-powerbar.txt", [0x03, 0x07, 0x0C, 0x1000000F]), // window 0x10000044, in the SmartBox frame 0x1000049A: a meter and its text
        ("gmSpellcastingUI", 0x10000015, "layout-spellcasting.txt", [0x01, 0x03, 0x08, 0x0B, 0x0C, 0x10000015, 0x10000031]), // window 0x10000061, in gmFloatyCombatPanelUI 0x100006B5; holds UIElement_ItemList (0x10000031)
    ];

    private static string CombatCacheFolder()
    {
        var root = CheckoutLocator.EnclosingCheckouts(AppContext.BaseDirectory).FirstOrDefault()
            ?? throw new InvalidOperationException("TODO(panel-layout-dump): the test is not running inside a checkout");
        return Path.Combine(root, "cache", "m3", "3-2");
    }

    private static bool FindWithPath(UiLayouts layouts, UiElementDesc local, uint classId, List<UiElementDesc> path)
    {
        var full = layouts.RequireFullDesc(local);
        path.Add(full);
        if (full.Type == classId)
        {
            return true;
        }

        foreach (var child in full.Children.Values)
        {
            if (FindWithPath(layouts, child, classId, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    [RequiresLocalDatsTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_floaty_window_of_the_gameplay_layout_is_dumped_and_the_classes_it_holds_are_pinned(int row)
    {
        var (name, classId, file, pinned) = Windows[row];
        var layouts = new UiLayouts(fixture.Dats);
        var root = layouts.GetRootDesc(UiLayoutEnums.GameplayRoot);
        var folder = CombatCacheFolder();
        Directory.CreateDirectory(folder);
        var path = new List<UiElementDesc>();
        Assert.True(FindWithPath(layouts, root, classId, path), $"{name} (0x{classId:X8}) is in no window of the gameplay layout");
        var window = path[^1];
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# {name} window 0x{window.ElementId:X8}: the layout subtree, resolved (derived from the retail dats; never committed)\n");
        text.Append("# path from the gameplay root: ").Append(string.Join(" > ", path.Select(static e => $"0x{e.ElementId:X8} {UiClassRegistry.NameOf(e.Type)}"))).Append('\n');
        text.Append("# element id | class | template (base element @ base layout) | source layout | box x,y wxh | z | default state | edges | properties id=value | media | states\n");
        Dump(layouts, window, 0, text);
        File.WriteAllText(Path.Combine(folder, file), text.ToString());

        var classes = new SortedSet<uint>();
        Classes(layouts, window, classes);
        Assert.True(
            pinned.Order().SequenceEqual(classes),
            $"{name} window 0x{window.ElementId:X8} (under {string.Join(" > ", path.Take(path.Count - 1).Select(static e => $"0x{e.ElementId:X8}"))}) holds [{string.Join(", ", classes.Select(c => $"0x{c:X8}"))}], pinned [{string.Join(", ", pinned.Order().Select(c => $"0x{c:X8}"))}]");
    }

    /// <summary>
    /// M4 2.2 s2: where a corpse's or chest's contents window comes from. <c>gm3DItemsUI</c> (0x10000021) is the pack list and its title inside the inventory page (page 7, element 0x100001CF, instanced from the base element
    /// 0x100001C4 of layout 0x21000021: title 0x100001C5, item list 0x100001C6, scroll bar 0x100001C7; no art of its own: the page's frame art is the page's), so it is not a window over the world view. The window is
    /// <c>gmExternalContainerUI</c> (0x1000000D, element 0x1000005D, panel id 0x12), one of the five panel windows of the environment floaty <c>gmFloatyEnvPanelUI</c> (0x1000004E, element 0x100005FD, 610 by 120 at 0,400) of the
    /// gameplay layout (docs/retail-ui.md section 23.9). Dumped to <c>cache/m4/2-2/</c> (gitignored; derived from the dats, never committed).
    /// </summary>
    [RequiresLocalDatsFact]
    public void The_container_window_is_gmExternalContainerUI_in_the_environment_floaty_and_gm3DItemsUI_is_the_inventory_pages_pack_list()
    {
        var layouts = new UiLayouts(fixture.Dats);
        var root = CheckoutLocator.EnclosingCheckouts(AppContext.BaseDirectory).FirstOrDefault()
            ?? throw new InvalidOperationException("TODO(panel-layout-dump): the test is not running inside a checkout");
        var folder = Path.Combine(root, "cache", "m4", "2-2");
        Directory.CreateDirectory(folder);
        var gameplay = layouts.GetRootDesc(UiLayoutEnums.GameplayRoot);

        // gm3DItemsUI: inside the inventory page, and the base element of layout 0x21000021 holds the title, the item list and its scroll bar.
        var itemsPath = new List<UiElementDesc>();
        Assert.True(FindWithPath(layouts, gameplay, 0x10000021, itemsPath));
        Assert.Contains(itemsPath, static e => e.ElementId == 0x1000018B); // page 7 (the inventory page)
        var baseDesc = layouts.AccessElementDesc(0x21000021, 0x100001C4) ?? throw new InvalidDataException("layout 0x21000021 has no element 0x100001C4");
        var text = new StringBuilder();
        text.Append("# layout 0x21000021 element 0x100001C4 (the base of gm3DItemsUI), resolved (derived from the dats; never committed)\n");
        Dump(layouts, baseDesc, 0, text);
        File.WriteAllText(Path.Combine(folder, "layout-21000021.txt"), text.ToString());
        var held = new SortedSet<uint>();
        Classes(layouts, baseDesc, held);
        Assert.Equal([0x01u, 0x03u, 0x0Bu, 0x0Cu, 0x10000021u, 0x10000031u], held.ToArray()); // the title is a UIElement_Text (0x0C): the container window's title is made from it

        // gmExternalContainerUI: a window of gmFloatyEnvPanelUI, with its own container cell, item rows, scroll bars and close button.
        var windowPath = new List<UiElementDesc>();
        Assert.True(FindWithPath(layouts, gameplay, 0x1000000D, windowPath));
        Assert.Equal([0x10000495u, 0x100005FDu, 0x1000005Du], windowPath.Select(static e => e.ElementId).ToArray()); // root, gmFloatyEnvPanelUI, gmExternalContainerUI
        Assert.Equal(0x1000004Eu, windowPath[1].Type);
        var envText = new StringBuilder();
        envText.Append("# gmFloatyEnvPanelUI window 0x100005FD: the layout subtree, resolved (derived from the dats; never committed)\n");
        Dump(layouts, windowPath[1], 0, envText);
        File.WriteAllText(Path.Combine(folder, "layout-envpanel.txt"), envText.ToString());
        var windowClasses = new SortedSet<uint>();
        Classes(layouts, windowPath[2], windowClasses);
        Assert.Equal([0x01u, 0x03u, 0x0Bu, 0x1000000Du, 0x10000031u], windowClasses.ToArray()); // buttons, fields, scroll bars and item lists: no text (the title is made)
        var window = layouts.RequireFullDesc(windowPath[2]);
        Assert.Equal([0x10000064u, 0x10000065u, 0x10000068u, 0x10000069u], window.Children.Values.Select(static c => c.ElementId).Order().ToArray()); // the container cell, the sub-container row, the close button, the item row's field
        Assert.Contains(window.Children.Values, c => c.ElementId == 0x10000068 && layouts.RequireFullDesc(c).Type == 0x01); // the close button (a UIElement_Button; the local desc takes its class from its base)
        Assert.True(window.BaseState.Properties.TryGetValue(UiPropertyIds.ElementPanelId, out var panelId)); // the panel id the floaty follows
        Assert.Equal(0x12u, Assert.IsType<UiEnumProperty>(panelId).Value);
    }

    /// <summary>
    /// M4 3.2 s4c: where the examine panel comes from. <c>gmExaminationUI</c> (0x1000001C) is a window of the gameplay layout; its subtree is dumped to <c>cache/m4/3-2/layout-examine.txt</c> (gitignored; derived from the dats, never committed).
    /// </summary>
    [RequiresLocalDatsFact]
    public void The_examination_window_is_dumped()
    {
        var layouts = new UiLayouts(fixture.Dats);
        var root = CheckoutLocator.EnclosingCheckouts(AppContext.BaseDirectory).FirstOrDefault()
            ?? throw new InvalidOperationException("TODO(panel-layout-dump): the test is not running inside a checkout");
        var folder = Path.Combine(root, "cache", "m4", "3-2");
        Directory.CreateDirectory(folder);
        var path = new List<UiElementDesc>();
        var where = new StringBuilder();
        foreach (var layoutId in layouts.LayoutIds)
        {
            foreach (var element in layouts.GetLayout(layoutId).AllElements())
            {
                UiElementDesc full;
                try
                {
                    full = layouts.RequireFullDesc(element);
                }
                catch (InvalidDataException)
                {
                    continue; // an element whose base is in a layout that has no such top-level element
                }

                if (full.Type is 0x1000001C or 0x1000004C)
                {
                    where.Append(CultureInfo.InvariantCulture, $"layout 0x{layoutId:X8} element 0x{full.ElementId:X8} class 0x{full.Type:X8}\n");
                    if (path.Count == 0 && full.Type == 0x1000001C)
                    {
                        path.Add(full);
                    }
                }
            }
        }

        WriteIfChanged(Path.Combine(folder, "layout-examine-where.txt"), where.ToString());
        Assert.True(path.Count > 0, where.ToString());
        var floatyPath = new List<UiElementDesc>();
        Assert.True(FindWithPath(layouts, layouts.GetRootDesc(UiLayoutEnums.GameplayRoot), 0x1000004C, floatyPath));
        var floaty = new StringBuilder();
        floaty.Append("# path: ").Append(string.Join(" > ", floatyPath.Select(static e => $"0x{e.ElementId:X8} {UiClassRegistry.NameOf(e.Type)}"))).Append((char)10);
        Dump(layouts, floatyPath[^1], 0, floaty);
        WriteIfChanged(Path.Combine(folder, "layout-examine-floaty.txt"), floaty.ToString());
        var text = new StringBuilder();
        text.Append("# gmExaminationUI window: the layout subtree, resolved (derived from the dats; never committed)\n");
        text.Append("# path from the gameplay root: ").Append(string.Join(" > ", path.Select(static e => $"0x{e.ElementId:X8} {UiClassRegistry.NameOf(e.Type)}"))).Append('\n');
        Dump(layouts, path[^1], 0, text);
        WriteIfChanged(Path.Combine(folder, "layout-examine.txt"), text.ToString());
    }

    // 3.2-R30: the research dump touches its cache files only when they are missing or differ, so a default run with the dats rewrites nothing.
    private static void WriteIfChanged(string path, string text)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            File.WriteAllText(path, text);
        }
    }

    private static void Outline(UiLayouts layouts, UiElementDesc local, int depth, int maxDepth, StringBuilder into)
    {
        var d = layouts.RequireFullDesc(local);
        var b = d.BaseState;
        into.Append(' ', depth * 2);
        into.Append(CultureInfo.InvariantCulture, $"0x{d.ElementId:X8} | 0x{d.Type:X8} {UiClassRegistry.NameOf(d.Type)} | {b.X},{b.Y} {b.Width}x{b.Height} | z {b.ZLevel} | {d.Children.Count} children\n");
        if (depth >= maxDepth)
        {
            return;
        }

        foreach (var child in d.Children.Values)
        {
            Outline(layouts, child, depth + 1, maxDepth, into);
        }
    }

    private static void Dump(UiLayouts layouts, UiElementDesc local, int depth, StringBuilder into)
    {
        var d = layouts.RequireFullDesc(local);
        var b = d.BaseState;
        into.Append(' ', depth * 2);
        into.Append(CultureInfo.InvariantCulture, $"0x{d.ElementId:X8} | 0x{d.Type:X8} {UiClassRegistry.NameOf(d.Type)} | base 0x{local.BaseElement:X8}@0x{local.BaseLayout:X8} | src 0x{d.SourceLayout:X8} | {b.X},{b.Y} {b.Width}x{b.Height} | z {b.ZLevel} | default 0x{d.DefaultState:X8} | edges {d.Edges}");
        into.Append(" | props [").Append(Props(b.Properties)).Append("] | media [").Append(Media(b.Media)).Append("] | states [");
        var first = true;
        foreach (var (id, state) in d.States)
        {
            into.Append(first ? "" : "; ");
            first = false;
            into.Append(CultureInfo.InvariantCulture, $"0x{id:X8}{{props [{Props(state.Properties)}] media [{Media(state.Media)}]}}");
        }

        into.Append("]\n");
        foreach (var child in d.Children.Values)
        {
            Dump(layouts, child, depth + 1, into);
        }
    }

    private static string Props(IReadOnlyDictionary<uint, UiProperty> properties) =>
        string.Join(", ", properties.Values.Select(static p => $"0x{p.Id:X8}={Value(p)}"));

    private static string Value(UiProperty p) => p switch
    {
        UiBoolProperty v => v.Value ? "true" : "false",
        UiIntegerProperty v => v.Value.ToString(CultureInfo.InvariantCulture),
        UiFloatProperty v => v.Value.ToString("0.###", CultureInfo.InvariantCulture),
        UiEnumProperty v => $"enum 0x{v.Value:X8}",
        UiDataIdProperty v => $"did 0x{v.Value:X8}",
        UiInstanceIdProperty v => $"iid 0x{v.Value:X8}",
        UiColorProperty v => $"argb 0x{v.Argb:X8}",
        UiBitfield32Property v => $"bits 0x{v.Value:X8}",
        UiBitfield64Property v => $"bits 0x{v.Value:X16}",
        UiStringInfoProperty v => v.Value.IsLiteral ? "string literal" : $"string {v.Value}", // the ids only, never the retail text
        UiVectorProperty v => $"vec {v.Value.X:0.###},{v.Value.Y:0.###},{v.Value.Z:0.###}",
        UiArrayProperty v => $"array({v.Items.Count})[{string.Join(", ", v.Items.Select(Value))}]",
        UiStructProperty v => $"struct{{{string.Join(", ", v.Members.Select(m => $"0x{m.Key:X8}={Value(m.Value)}"))}}}",
        _ => p.Type.ToString(),
    };

    private static string Media(IReadOnlyList<UiMedia> media) =>
        string.Join(", ", media.Select(static m => m switch
        {
            UiImageMedia i => $"image 0x{i.File:X8} {i.DrawMode}",
            UiAlphaMedia a => $"alpha 0x{a.File:X8}",
            UiAnimationMedia a => $"animation {a.Frames.Count} frames [{string.Join(" ", a.Frames.Select(f => $"0x{f:X8}"))}]",
            UiStateMedia s => $"state 0x{s.StateId:X8}",
            UiMessageMedia s => $"message 0x{s.MessageId:X8}",
            _ => m.Type.ToString(),
        }));
}

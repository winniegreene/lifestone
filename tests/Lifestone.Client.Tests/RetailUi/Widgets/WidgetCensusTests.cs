using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Widgets;

/// <summary>
/// The facts about the retail layouts the widgets lean on (R13): where Lifestone re-expresses retail for the data as it
/// is, this census pins the data. Nothing dat-derived is written anywhere.
/// </summary>
public sealed class WidgetCensusTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void No_element_has_both_scroll_bars()
    {
        // UiScrollable keeps the other axis where retail resets it to 0; with one bar per element nothing differs.
        var both = FullDescs().Where(d => Has(d, UiPropertyIds.ScrollableHorizontalScrollbar) && Has(d, UiPropertyIds.ScrollableVerticalScrollbar)).ToArray();

        Assert.Empty(both.Select(d => d.Describe()));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_buttons_the_layouts_ghost_default_to_normal_so_creation_unghosts_them()
    {
        // UIElement::Initialize sets the default state before it applies the desc's properties, through the button's own
        // SetState, which clears the ghosting for any state but 13 (UIElement::InqProperty already sees the desc's base
        // value). Every button a layout ghosts defaults to Normal, so retail creates them all unghosted and the screens
        // ghost what they need; Lifestone does the same (TODO(retailui-runtime): confirm the virtual slot
        // UIElement::Initialize calls is SetState in the 11.6096 vtable).
        var buttons = FullDescs().Where(d => d.Type is UiClassIds.Button or UiClassIds.Menu or UiClassIds.Scrollbar).ToArray();
        var ghosted = buttons.Where(d => d.BaseState.Properties.TryGetValue(UiPropertyIds.ButtonGhosted, out var g) && g is UiBoolProperty { Value: true }).ToArray();
        output.WriteLine($"{buttons.Length} buttons, {ghosted.Length} ghosted by their desc");

        Assert.Equal(23, ghosted.Length);
        Assert.All(ghosted, d => Assert.Equal(UiStateIds.Normal, d.DefaultState));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_engine_widgets_the_layouts_use()
    {
        var counts = FullDescs().Where(d => d.Type is > 0 and < 0x20).GroupBy(d => d.Type).OrderBy(g => g.Key)
            .Select(g => $"{UiClassRegistry.NameOf(g.Key)}:{g.Count()}").ToArray();
        output.WriteLine(string.Join(" ", counts));

        // Every engine class the layouts use has a runtime; no layout uses the browser.
        var registry = UiClassRegistry.CreateDefault();
        Assert.All(FullDescs().Where(d => d.Type is > 0 and < 0x20), d => Assert.True(registry.IsImplemented(d.Type), d.Describe()));
        Assert.DoesNotContain(FullDescs(), d => d.Type == UiClassIds.Browser);
    }

    private static bool Has(UiElementDesc desc, uint id) =>
        desc.BaseState.Properties.ContainsKey(id) || desc.States.Values.Any(s => s.Properties.ContainsKey(id));

    private IEnumerable<UiElementDesc> FullDescs()
    {
        var layouts = new UiLayouts(fixture.Dats);
        return layouts.LayoutIds
            .SelectMany(id => layouts.GetLayout(id).AllElements())
            .Select(local => layouts.InqFullDesc(local))
            .Where(r => r.Succeeded)
            .Select(r => r.Full!);
    }
}

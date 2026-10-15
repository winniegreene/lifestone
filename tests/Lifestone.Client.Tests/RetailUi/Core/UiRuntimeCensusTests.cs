using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// The facts about the retail data the runtime leans on (R10-R12, docs/retail-ui.md §8): where Lifestone re-expresses
/// retail for the data as it is, rather than for every value the format allows, this census pins the data, so a change
/// of dats that breaks an assumption fails here first. Nothing dat-derived is written anywhere.
/// </summary>
public sealed class UiRuntimeCensusTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_draw_after_children_and_erase_background_value_is_true()
    {
        var properties = AllStates().SelectMany(s => s.Properties.Values).ToArray();

        // UIElement::OnSetAttribute sets the draw-after-children flag whatever the value; the data never says false.
        var drawAfter = properties.Where(p => p.Id == UiPropertyIds.ElementDrawAfterChildren).ToArray();
        Assert.Equal(8, drawAfter.Length);
        Assert.All(drawAfter, p => Assert.True(Assert.IsType<UiBoolProperty>(p).Value));

        var erase = properties.Where(p => p.Id == UiPropertyIds.ElementShouldEraseBackground).ToArray();
        Assert.Equal(28, erase.Length);
        Assert.All(erase, p => Assert.True(Assert.IsType<UiBoolProperty>(p).Value));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_layouts_images_are_32_and_24_bit_or_jpeg()
    {
        var loader = new TextureLoader(fixture.Dats);
        var media = AllStates().SelectMany(s => s.Media).ToArray();
        var images = media.OfType<UiImageMedia>().Select(m => m.File).Distinct().ToArray();

        string Format(uint did) => did == 0 ? "none" : loader.ReadRenderSurface(did, out _)?.Format.ToString() ?? "missing";

        // DID 0 is an image entry that clears the image. No 16-bit, paletted or DXT image: SurfaceWindow::LegacyBlit is
        // not needed (DatUiImageSource).
        Assert.Equal(
            "PFID_A8R8G8B8:325 PFID_CUSTOM_RAW_JPEG:6 PFID_R8G8B8:292 none:1",
            string.Join(" ", images.GroupBy(Format).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Count()}")));
        Assert.Equal(
            [0x06004D26u, 0x06004D5Fu, 0x06005D3Fu, 0x0600610Fu, 0x06006EBCu, 0x06007576u],
            images.Where(d => Format(d) == nameof(PixelFormat.PFID_CUSTOM_RAW_JPEG)).Order());

        var frames = media.OfType<UiAnimationMedia>().SelectMany(a => a.Frames).Distinct().ToArray();
        Assert.Equal(30, frames.Length);
        Assert.All(frames, d => Assert.Equal(nameof(PixelFormat.PFID_R8G8B8), Format(d)));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Fonts_with_an_outline_atlas_have_equal_borders()
    {
        var dats = fixture.Dats;
        var fonts = dats.GetAllPortalIds<Font>().Select(did => (did, font: dats.TryGetPortal<Font>(did, out var f) ? f : null)).ToArray();
        Assert.All(fonts, f => Assert.NotNull(f.font));

        // TextRasterizer keeps retail's slip (the outline's destination grows downward by the horizontal border): with
        // equal borders it is invisible. Fonts with no outline atlas have no border.
        Assert.All(fonts, f => Assert.Equal(f.font!.NumHorizontalBorderPixels, f.font.NumVerticalBorderPixels));
        Assert.All(fonts.Where(f => f.font!.BackgroundSurfaceDataId == 0), f => Assert.Equal(0u, f.font!.NumHorizontalBorderPixels));

        // UiGlyphDesc's offsets are signed because many retail glyphs have negative ones.
        var glyphs = fonts.SelectMany(f => f.font!.CharDescs).ToArray();
        var before = glyphs.Count(c => c.HorizontalOffsetBefore < 0);
        var after = glyphs.Count(c => c.HorizontalOffsetAfter < 0);
        var vertical = glyphs.Count(c => c.VerticalOffsetBefore < 0);
        output.WriteLine(
            $"{fonts.Length} fonts, {fonts.Count(f => f.font!.BackgroundSurfaceDataId != 0)} with an outline atlas; {glyphs.Length} glyphs, "
            + $"negative offsets: {before} before, {after} after, {vertical} vertical");
        Assert.Equal((49, 37, 153749), (fonts.Length, fonts.Count(f => f.font!.BackgroundSurfaceDataId != 0), glyphs.Length));
        Assert.Equal((8738, 7048, 0), (before, after, vertical));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Default_states_override_few_base_properties()
    {
        var layouts = new UiLayouts(fixture.Dats);
        var differing = new SortedSet<string>(StringComparer.Ordinal);
        var shared = new HashSet<string>(StringComparer.Ordinal);

        void Visit(UiElementDesc desc)
        {
            var result = layouts.InqFullDesc(desc);
            if (!result.Succeeded)
            {
                return;
            }

            var full = result.Full!;
            if (full.States.TryGetValue(full.DefaultState, out var state))
            {
                foreach (var (id, value) in state.Properties)
                {
                    if (full.Properties.TryGetValue(id, out var baseValue))
                    {
                        var key = $"{full.Describe()} property 0x{id:X}";
                        shared.Add(key);
                        if (!Same(value, baseValue))
                        {
                            differing.Add(key);
                        }
                    }
                }
            }

            foreach (var child in full.Children.Values)
            {
                Visit(child);
            }
        }

        foreach (var id in layouts.LayoutIds)
        {
            foreach (var top in layouts.GetLayout(id).Elements.Values)
            {
                Visit(top);
            }
        }

        // UIElement::Initialize enters the default state, then applies the desc's own properties, so where the two
        // disagree the desc's value is what retail shows (docs/retail-ui.md §3).
        output.WriteLine($"{shared.Count} properties in both an element's desc and its default state; they differ for:");
        foreach (var key in differing)
        {
            output.WriteLine("  " + key);
        }

        Assert.Equal(27, shared.Count);
        Assert.Equal(
            [
                "element 0x1000004A of layout 0x21000011 property 0x1B",
                "element 0x1000004A of layout 0x21000011 property 0x23",
                "element 0x1000043A of layout 0x2100004D property 0x1B",
            ],
            differing);
    }

    private static bool Same(UiProperty a, UiProperty b) => (a, b) switch
    {
        (UiArrayProperty x, UiArrayProperty y) => x.Items.Count == y.Items.Count && x.Items.Zip(y.Items).All(p => Same(p.First, p.Second)),
        (UiStructProperty x, UiStructProperty y) => x.Members.Count == y.Members.Count
            && x.Members.All(m => y.Members.TryGetValue(m.Key, out var other) && Same(m.Value, other)),
        _ => Equals(a, b),
    };

    private IEnumerable<UiStateDesc> AllStates()
    {
        var layouts = new UiLayouts(fixture.Dats);
        return layouts.LayoutIds
            .SelectMany(id => layouts.GetLayout(id).AllElements())
            .SelectMany(e => e.States.Values.Prepend(e.BaseState));
    }
}

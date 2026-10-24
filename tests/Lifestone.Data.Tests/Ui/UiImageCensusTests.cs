using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// Every image the 101 retail layouts name (m2-plan.md R7), by the kind of media entry that names it (an image, an alpha
/// mask, an animation frame or a cursor): how it resolves, its pixel format and size, and which of them
/// <c>client_highres.dat</c> also holds (TextureLoader reads high-res first).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class UiImageCensusTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The media entry an image DID comes from.</summary>
    private enum Kind
    {
        /// <summary><see cref="UiImageMedia.File"/>.</summary>
        Image,

        /// <summary><see cref="UiAlphaMedia.File"/> (the compositor's alpha image).</summary>
        Alpha,

        /// <summary>One of <see cref="UiAnimationMedia.Frames"/>.</summary>
        AnimationFrame,

        /// <summary><see cref="UiCursorMedia.File"/> (an element's own cursor).</summary>
        Cursor,
    }

    private DatSet Dats => fixture.Dats;

    [RequiresLocalDatsFact]
    public void The_layouts_name_624_images_and_623_resolve()
    {
        var references = References().Where(r => r.Kind == Kind.Image).ToArray();
        var distinct = references.Select(r => r.File).Distinct().ToArray();
        var textures = new TextureLoader(Dats);

        Assert.Equal(1310, references.Length);
        Assert.Equal(624, distinct.Length);
        var missing = distinct.Where(id => !textures.RenderSurfaceExists(id, out _)).ToArray();

        // The one that does not resolve is DID 0, retail's INVALID_DID: an image entry that names no image. Its 43 uses, in
        // 17 states of 11 layouts (the shared dialog frame's corner pieces in 0x21000042, the chat windows' buttons, the
        // vitals' and the toolbar's empty states), clear the element's image; the compositor (R11, UIRegion::SetImageByDID)
        // must draw nothing for it rather than fail.
        Assert.Equal(0u, Assert.Single(missing));
        var uses = references.Where(r => r.File == 0).ToArray();
        output.WriteLine($"DID 0 is named {uses.Length} times: {string.Join("; ", uses.Select(r => r.Where).Distinct())}");
        Assert.Equal(43, uses.Length);
        Assert.Equal(17, uses.Select(r => r.Where).Distinct().Count());
    }

    [RequiresLocalDatsFact]
    public void Alpha_masks_animation_frames_and_layout_cursors_all_resolve_and_decode()
    {
        var references = References();
        var textures = new TextureLoader(Dats);
        var counts = new List<string>();
        var formats = new List<string>();
        foreach (var kind in new[] { Kind.Alpha, Kind.AnimationFrame, Kind.Cursor })
        {
            var ofKind = references.Where(r => r.Kind == kind).ToArray();
            var distinct = ofKind.Select(r => r.File).Distinct().Order().ToArray();
            counts.Add($"{kind}:{ofKind.Length}/{distinct.Length}");
            foreach (var id in distinct)
            {
                var surface = textures.ReadRenderSurface(id, out var source)
                    ?? throw new InvalidDataException($"{kind} image 0x{id:X8} does not resolve");
                Assert.Equal(TextureDat.Portal, source);
                formats.Add($"{kind}:{surface.Format}");
                var decoded = textures.DecodeRenderSurface(id, TextureDecodeMode.Rgba8);
                Assert.Equal((DecodedTextureFormat.Rgba8, surface.Width, surface.Height), (decoded.Format, decoded.Width, decoded.Height));
                output.WriteLine($"{kind} 0x{id:X8} {surface.Width}x{surface.Height} {surface.Format}, named {ofKind.Count(r => r.File == id)} times");
                if (kind == Kind.Cursor)
                {
                    Assert.Equal((32, 32), (surface.Width, surface.Height)); // Godot's custom-cursor limit is 256x256
                }
            }
        }

        // 3 alpha masks, each named once; 2 animations of 15 frames, no frame shared; 101 cursor entries naming 5 images.
        Assert.Equal(new[] { "Alpha:3/3", "AnimationFrame:30/30", "Cursor:101/5" }, counts);
        Assert.Equal(
            "Alpha:PFID_A8R8G8B8 x3, AnimationFrame:PFID_R8G8B8 x30, Cursor:PFID_A8R8G8B8 x5",
            string.Join(", ", formats.GroupBy(f => f).Select(g => $"{g.Key} x{g.Count()}")));

        // No DID is named by two kinds, and none of the 5 layout cursors is one of the 41 UICURSOR images.
        Assert.Equal(662, references.Select(r => r.File).Distinct().Count());
        Assert.Equal(624 + 3 + 30 + 5, references.Select(r => (r.Kind, r.File)).Distinct().Count());
        var uiCursorImages = new UiCursors(Dats).All.Select(c => c.ImageId).ToHashSet();
        Assert.DoesNotContain(references.Where(r => r.Kind == Kind.Cursor), r => uiCursorImages.Contains(r.File));
    }

    [RequiresLocalDatsFact]
    public void Formats_and_sizes_and_the_high_res_copies()
    {
        var references = References();
        var formats = new Dictionary<PixelFormat, int>();
        var highRes = new List<string>();
        var resolved = 0;
        foreach (var id in references.Select(r => r.File).Distinct().Order())
        {
            if (!Dats.TryGetPortal<RenderSurface>(id, out var portal))
            {
                continue;
            }

            resolved++;
            if (references.Any(r => r.File == id && r.Kind == Kind.Image))
            {
                formats[portal.Format] = formats.GetValueOrDefault(portal.Format) + 1;
            }

            if (Dats.TryGetHighRes<RenderSurface>(id, out var high))
            {
                highRes.Add($"0x{id:X8} {portal.Width}x{portal.Height} {portal.Format} / {high.Width}x{high.Height} {high.Format}");
                Assert.True((portal.Width, portal.Height) == (high.Width, high.Height), $"UI image 0x{id:X8} differs in size between the portal and client_highres.dat");
            }
        }

        foreach (var (format, count) in formats.OrderByDescending(kv => kv.Value))
        {
            output.WriteLine($"{format}: {count}");
        }

        // No UI image of any kind is in client_highres.dat, so TextureLoader's high-res-first rule never changes one.
        Assert.Empty(highRes);
        Assert.Equal(661, resolved);
        Assert.Equal(623, formats.Values.Sum());
        Assert.Equal((325, 292, 6), (formats[PixelFormat.PFID_A8R8G8B8], formats[PixelFormat.PFID_R8G8B8], formats[PixelFormat.PFID_CUSTOM_RAW_JPEG]));
    }

    /// <summary>Every image DID a media entry of a retail layout names, with the entry's kind and where it is.</summary>
    private List<(Kind Kind, uint File, string Where)> References()
    {
        var layouts = new UiLayouts(Dats);
        var references = new List<(Kind, uint, string)>();
        foreach (var layout in layouts.LayoutIds.Select(layouts.GetLayout))
        {
            foreach (var element in layout.AllElements())
            {
                foreach (var state in element.States.Values.Prepend(element.BaseState))
                {
                    var where = $"{element.Describe()} state 0x{state.StateId:X}";
                    foreach (var media in state.Media)
                    {
                        switch (media)
                        {
                            case UiImageMedia image:
                                references.Add((Kind.Image, image.File, where));
                                break;
                            case UiAlphaMedia alpha:
                                references.Add((Kind.Alpha, alpha.File, where));
                                break;
                            case UiAnimationMedia animation:
                                references.AddRange(animation.Frames.Select(frame => (Kind.AnimationFrame, frame, where)));
                                break;
                            case UiCursorMedia cursor:
                                references.Add((Kind.Cursor, cursor.File, where));
                                break;
                        }
                    }
                }
            }
        }

        return references;
    }
}

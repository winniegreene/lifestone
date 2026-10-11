using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Textures;

/// <summary>SurfaceTexture (0x05) to RenderSurface (0x06) resolution over the whole portal.</summary>
public sealed class SurfaceTextureResolutionTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresHighResDatsFact]
    public void Every_surface_texture_resolves_with_highres_first()
    {
        // docs/research/local-scout.md: 7,221 SurfaceTextures, 10,245 references, 4,197 single and
        // 3,024 two-level lists; the 2,284 first references missing from the portal live in client_highres.dat.
        var loader = new TextureLoader(Dats);
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<SurfaceTexture>().ToArray());
        Assert.Equal(7221, ids.Length);

        Assert.True(loader.HighResInstalled);
        int refs = 0, single = 0, twoLevel = 0, viaHighRes = 0, highResRefs = 0;
        var missing = new List<string>();
        var absentRefs = new List<string>();
        foreach (var id in ids)
        {
            var r = loader.ResolveSurfaceTexture(id);
            Assert.NotNull(r);
            refs += r.RenderSurfaceIds.Count;
            single += r.RenderSurfaceIds.Count == 1 ? 1 : 0;
            twoLevel += r.RenderSurfaceIds.Count == 2 ? 1 : 0;
            missing.AddRange(r.MissingIds.Select(m => $"0x{id:X8}->0x{m:X8}"));
            Assert.True(r.IsResolved, r.ToString());
            Assert.False(r.HighDetailDropped);
            Assert.Equal(0, r.SelectedLevel); // with highres present, level 0 always exists
            viaHighRes += r.Source == TextureDat.HighRes ? 1 : 0;
            foreach (var x in r.RenderSurfaceIds)
            {
                // Every listed reference, not only the selected one, exists in one of the two dats.
                if (!loader.RenderSurfaceExists(x, out var dat))
                {
                    absentRefs.Add($"0x{id:X8}->0x{x:X8}");
                }
                else if (dat == TextureDat.HighRes)
                {
                    highResRefs++;
                }
            }
        }

        output.WriteLine($"{ids.Length} SurfaceTextures, {refs} refs, {viaHighRes} resolved via highres, {missing.Count} missing");
        Assert.Equal(10245, refs);
        Assert.Equal(4197, single);
        Assert.Equal(3024, twoLevel);
        Assert.Equal(2284, viaHighRes);
        Assert.Equal(2284, highResRefs);
        Assert.Empty(missing);
        Assert.Empty(absentRefs);
    }

    [RequiresDatsFact]
    public void Without_highres_two_level_lists_take_level_1_and_report_nothing_missing()
    {
        // Retail Render::ShouldDropHighDetail is true when "HiFi" is not on disk, so ImgTex::GetSurfaceDID takes level
        // 1 and never asks for level 0. Simulated with a portal-only lookup.
        var lists = Dats.Read(d => d.Portal.GetAllIdsOfType<SurfaceTexture>().ToArray())
            .Select(id => (Id: id, Levels: TextureTestSupport.ReadUncached<SurfaceTexture>(Dats, id)!.Textures.Select(t => t.DataId).ToArray()))
            .ToArray();
        int twoLevel = 0, dropped = 0;
        var missing = new List<string>();
        foreach (var (id, levels) in lists)
        {
            var r = SurfaceTextureResolution.Resolve(id, levels, TextureDetail.High, highResInstalled: false, PortalOnly);
            Assert.True(r.IsResolved, r.ToString());
            Assert.Equal(TextureDat.Portal, r.Source);
            missing.AddRange(r.MissingIds.Select(m => $"0x{id:X8}->0x{m:X8}"));
            dropped += r.HighDetailDropped ? 1 : 0;
            if (levels.Length == 2)
            {
                Assert.Equal(1, r.SelectedLevel);
                twoLevel++;
            }
        }

        Assert.Equal(3024, twoLevel);
        Assert.Equal(3024, dropped);
        Assert.Empty(missing); // the 2,284 highres-only level-0 ids are not false alarms

        // The same lookup while claiming highres is installed walks level 0 first, so the highres-only ids are real
        // fallbacks and are reported.
        var fallbacks = lists.Sum(x =>
            SurfaceTextureResolution.Resolve(x.Id, x.Levels, TextureDetail.High, highResInstalled: true, PortalOnly).MissingIds.Count);
        Assert.Equal(2284, fallbacks);
    }

    [Fact]
    public void Resolve_walks_the_preferred_order_and_reports_only_ids_it_tried()
    {
        const uint hi = 0x06000001u, lo = 0x06000002u;
        static TextureDat? OnlyLow(uint id) => id == lo ? TextureDat.Portal : null;
        static TextureDat? Both(uint id) => id == hi ? TextureDat.HighRes : TextureDat.Portal;
        static TextureDat? None(uint id) => null;

        var r = SurfaceTextureResolution.Resolve(0x05000001u, [hi, lo], TextureDetail.High, highResInstalled: true, Both);
        Assert.Equal((hi, TextureDat.HighRes, 0, false), (r.RenderSurfaceId, r.Source, r.SelectedLevel, r.HighDetailDropped));
        Assert.Empty(r.MissingIds);

        r = SurfaceTextureResolution.Resolve(0x05000001u, [hi, lo], TextureDetail.High, highResInstalled: true, OnlyLow);
        Assert.Equal((lo, 1), (r.RenderSurfaceId, r.SelectedLevel));
        Assert.Equal(new[] { hi }, r.MissingIds); // a real fallback

        r = SurfaceTextureResolution.Resolve(0x05000001u, [hi, lo], TextureDetail.High, highResInstalled: false, OnlyLow);
        Assert.Equal((lo, 1, true), (r.RenderSurfaceId, r.SelectedLevel, r.HighDetailDropped));
        Assert.Empty(r.MissingIds); // retail never asks for level 0 without highres

        r = SurfaceTextureResolution.Resolve(0x05000001u, [hi, lo], TextureDetail.Low, highResInstalled: true, Both);
        Assert.Equal((lo, TextureDat.Portal, 1, true), (r.RenderSurfaceId, r.Source, r.SelectedLevel, r.HighDetailDropped));

        r = SurfaceTextureResolution.Resolve(0x05000001u, [hi], TextureDetail.Low, highResInstalled: false, Both);
        Assert.Equal((hi, 0, false), (r.RenderSurfaceId, r.SelectedLevel, r.HighDetailDropped)); // one level: always level 0

        r = SurfaceTextureResolution.Resolve(0x05000001u, [hi, lo], TextureDetail.Low, highResInstalled: true, None);
        Assert.False(r.IsResolved);
        Assert.Equal((0u, -1), (r.RenderSurfaceId, r.SelectedLevel));
        Assert.Equal(new[] { lo, hi }, r.MissingIds); // walk order
    }

    [RequiresDatsFact]
    public void Low_detail_prefers_the_second_level_which_is_always_in_the_portal()
    {
        var loader = new TextureLoader(Dats);
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<SurfaceTexture>().ToArray());
        var twoLevel = 0;
        foreach (var id in ids)
        {
            var r = loader.ResolveSurfaceTexture(id, TextureDetail.Low)!;
            Assert.True(r.IsResolved, r.ToString());
            Assert.Equal(TextureDat.Portal, r.Source);
            Assert.Empty(r.MissingIds);
            Assert.Equal(r.RenderSurfaceIds.Count == 2, r.HighDetailDropped);
            if (r.RenderSurfaceIds.Count == 2)
            {
                Assert.Equal(1, r.SelectedLevel);
                twoLevel++;
            }
        }

        Assert.Equal(3024, twoLevel);
    }

    [RequiresHighResDatsFact]
    public void Landscape_alpha_map_resolves_to_its_highres_level()
    {
        // rendering.md: 0x05001371 -> [0x060073A7 (highres), 0x06006D60 (portal, 512x512 LSCAPE_ALPHA)].
        var loader = new TextureLoader(Dats);
        var r = loader.ResolveSurfaceTexture(0x05001371u)!;
        Assert.Equal(new[] { 0x060073A7u, 0x06006D60u }, r.RenderSurfaceIds);
        Assert.Equal((0x060073A7u, TextureDat.HighRes, 0), (r.RenderSurfaceId, r.Source, r.SelectedLevel));
        var low = loader.ResolveSurfaceTexture(0x05001371u, TextureDetail.Low)!;
        Assert.Equal((0x06006D60u, TextureDat.Portal, 1), (low.RenderSurfaceId, low.Source, low.SelectedLevel));
        Assert.Null(loader.ResolveSurfaceTexture(0x05FFFFFFu));
    }

    [RequiresDatsFact]
    public void Loader_reads_do_not_fill_the_datreaderwriter_file_cache()
    {
        var loader = new TextureLoader(Dats);
        var a = loader.ReadRenderSurface(0x06004304u, out var src);
        var b = loader.ReadRenderSurface(0x06004304u, out _);
        Assert.NotNull(a);
        Assert.Equal(TextureDat.Portal, src);
        Assert.NotSame(a, b); // a fresh object each time: nothing was cached
        Assert.Null(loader.ReadRenderSurface(0x06FFFFFFu, out _));
    }

    private TextureDat? PortalOnly(uint renderSurfaceId) =>
        Dats.Read(d => d.Portal.Tree.HasFile(renderSurfaceId)) ? TextureDat.Portal : null;
}

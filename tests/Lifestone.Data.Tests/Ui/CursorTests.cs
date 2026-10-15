using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>The UICURSOR cursors (m2-plan.md R7) and the default pointer <c>gmUIFlow::gmUIFlow</c> sets.</summary>
public sealed class CursorTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void UICURSOR_has_41_cursors_and_enum_1_is_the_default_pointer_at_0_0()
    {
        var cursors = new UiCursors(Dats);

        Assert.Equal("UICURSOR", Dats.EnumMaps.GroupName(EnumGroup.UiCursor));
        Assert.Equal(0x2500000Fu, Dats.EnumMaps.GroupMapId(EnumGroup.UiCursor));
        Assert.Equal(41, cursors.All.Count);
        Assert.Equal(Enumerable.Range(1, 41).Select(i => (uint)i), cursors.All.Select(c => c.Enum));
        Assert.Equal(new UiCursor(UiCursors.DefaultEnum, "Default", 0x06004D68u, 0, 0), cursors.Default);
        Assert.Same(cursors.Default, cursors.Get(1));
        Assert.Equal("Combat_Advanced_AttackHigh", cursors.Get(7).Name);
        var error = Assert.Throws<InvalidDataException>(() => cursors.Get(0x42));
        Assert.Equal("TODO(retailui-data): UICURSOR enum 0x42 does not resolve to a cursor image.", error.Message);
    }

    [RequiresDatsFact]
    public void Every_cursor_is_an_A8R8G8B8_image_of_at_most_32_by_32_that_decodes()
    {
        var cursors = new UiCursors(Dats);
        var textures = new TextureLoader(Dats);
        foreach (var cursor in cursors.All)
        {
            var surface = textures.ReadRenderSurface(cursor.ImageId, out var source);
            Assert.NotNull(surface);
            Assert.Equal(TextureDat.Portal, source);
            Assert.Equal(PixelFormat.PFID_A8R8G8B8, surface.Format);
            Assert.InRange(surface.Width, 1, 32); // Godot's custom-cursor limit is 256x256
            Assert.InRange(surface.Height, 1, 32);
            var decoded = UiCursors.Decode(textures, cursor);
            Assert.Equal((DecodedTextureFormat.Rgba8, surface.Width, surface.Height), (decoded.Format, decoded.Width, decoded.Height));
        }

        // Four pairs of enums share an image (an object-hover or quick-turn variant drawn like the plain one).
        Assert.Equal(37, cursors.All.Select(c => c.ImageId).Distinct().Count());
    }

    [Fact]
    public void A_cursor_media_entry_keeps_its_hot_spot()
    {
        var cursor = UiCursor.FromMedia(new UiCursorMedia(0x06004D6Eu, 3, 5));
        Assert.Equal(new UiCursor(0, null, 0x06004D6Eu, 3, 5), cursor);
        Assert.Throws<InvalidDataException>(() => new UiCursors([new UiCursor(2, null, 1, 0, 0)]));
    }
}

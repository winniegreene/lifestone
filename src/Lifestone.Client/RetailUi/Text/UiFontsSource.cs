using System.Collections.Concurrent;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;
using DataUiFonts = Lifestone.Data.Ui.UiFonts;

namespace Lifestone.Client.RetailUi.Text;

/// <summary>
/// The retail fonts (R5's <see cref="Lifestone.Data.Ui.UiFonts"/>) as the element runtime asks for them: the production
/// <see cref="IUiFontSource"/>. Each font is converted once, by DID, with its atlases decoded once into
/// <see cref="UiImage"/>s; a FONT enum resolves to the same font object as its DID. Safe from any thread;
/// allocation-free once a font was asked for.
/// </summary>
/// <remarks>
/// <para>
/// Glyph lookup is R5's (<see cref="UiFontAdapter"/>): retail's <c>Font::GetCharDesc</c> over the character map of
/// <c>Font::GenerateCharacterMap</c>, so a character inside the font's range that it lacks draws as glyph 0 and only a
/// character outside the range falls back to '?'; <c>Font::ContainsChar</c> is the range test. FONT enum 18
/// (<c>FixedSys_15</c>) names a font the end-of-retail portal dat does not hold, so it answers null, as retail's
/// <c>DBObj::Get</c> does.
/// </para>
/// <para>
/// A font that exists but does not convert, and an atlas that does not decode or is in a format the rasterizer does not
/// take, are logged as errors (category <c>retailui</c>) once each: the font is then missing (its text draws nothing),
/// or draws without that atlas. Every retail font atlas is A8 (FontTests), decoded as
/// <see cref="DecodedTextureFormat.Alpha8"/> and kept as an <see cref="UiPixelFormat.A8"/> image (white, the coverage in
/// alpha) so the text can be tinted.
/// </para>
/// </remarks>
public sealed class UiFontsSource : IUiFontSource
{
    private const string Category = Core.UiElementManager.Category;

    private readonly DataUiFonts _fonts;
    private readonly TextureLoader _textures;
    private readonly ILogSink _log;
    private readonly ConcurrentDictionary<uint, UiFontAdapter?> _byDid = new();
    private readonly ConcurrentDictionary<uint, UiFontAdapter?> _byEnum = new();
    private readonly Func<uint, UiFontAdapter?> _loadByDid;
    private readonly Func<uint, UiFontAdapter?> _loadByEnum;

    /// <summary>The fonts of <paramref name="dats"/> (its portal dat), with their atlases from its image dats.</summary>
    /// <param name="dats">The opened dats (not owned).</param>
    /// <param name="log">Where conversion and decoding errors go; none by default.</param>
    public UiFontsSource(DatSet dats, ILogSink? log = null)
        : this(new DataUiFonts(dats), new TextureLoader(dats), log)
    {
    }

    /// <summary>The fonts of <paramref name="fonts"/>, with their atlases decoded through <paramref name="textures"/>.</summary>
    /// <param name="fonts">R5's fonts.</param>
    /// <param name="textures">The RenderSurface decoder for the atlases.</param>
    /// <param name="log">Where conversion and decoding errors go; none by default.</param>
    public UiFontsSource(DataUiFonts fonts, TextureLoader textures, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(textures);
        _fonts = fonts;
        _textures = textures;
        _log = log ?? NullLogSink.Instance;
        _loadByDid = LoadByDid;
        _loadByEnum = LoadByEnum;
    }

    /// <summary>R5's fonts underneath.</summary>
    public DataUiFonts Fonts => _fonts;

    /// <summary>The font <paramref name="did"/> (<c>FontLocal::GetGlobalOrLocalFont</c>), or null when the portal dat does not hold it.</summary>
    public IUiFont? GetFont(uint did) => GetAdapter(did);

    /// <summary>
    /// The font FONT enum <paramref name="fontEnum"/> names (<c>DBObj::GetByEnum(enum, 9, Font)</c>,
    /// <see cref="Lifestone.Data.Ui.UiFonts.TryGetFontByEnum"/>), or null when the enum does not resolve or its font is
    /// absent.
    /// </summary>
    public IUiFont? GetFontByEnum(uint fontEnum) =>
        _byEnum.TryGetValue(fontEnum, out var font) ? font : _byEnum.GetOrAdd(fontEnum, _loadByEnum);

    /// <summary>The adapter of font <paramref name="did"/>, or null (<see cref="GetFont"/>).</summary>
    public UiFontAdapter? GetAdapter(uint did) =>
        _byDid.TryGetValue(did, out var font) ? font : _byDid.GetOrAdd(did, _loadByDid);

    private UiFontAdapter? LoadByEnum(uint fontEnum) => _fonts.TryResolveFontEnum(fontEnum, out var did) ? GetAdapter(did) : null;

    private UiFontAdapter? LoadByDid(uint did)
    {
        UiFont? font;
        try
        {
            if (!_fonts.TryGetFont(did, out font))
            {
                return null;
            }
        }
        catch (InvalidDataException ex)
        {
            _log.Error(Category, $"font 0x{did:X8} does not convert: {ex.Message}; its text draws nothing");
            return null;
        }

        return new UiFontAdapter(font, Atlas(font, foreground: true), Atlas(font, foreground: false));
    }

    private UiImage? Atlas(UiFont font, bool foreground)
    {
        var did = foreground ? font.ForegroundSurfaceId : font.BackgroundSurfaceId;
        if (did == 0)
        {
            return null;
        }

        DecodedTexture? texture;
        try
        {
            texture = foreground ? font.DecodeForeground(_textures) : font.DecodeBackground(_textures);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            _log.Error(Category, $"{font}: its {(foreground ? "foreground" : "background")} atlas 0x{did:X8} does not decode ({ex.Message}); the font draws without it");
            return null;
        }

        return texture is null ? null : ToImage(font, did, texture);
    }

    private UiImage? ToImage(UiFont font, uint did, DecodedTexture texture)
    {
        switch (texture.Format)
        {
            case DecodedTextureFormat.Alpha8:
            {
                // An alpha mask: white, the coverage in alpha (as the rasterizer and the blitter read A8 images).
                var count = texture.Width * texture.Height;
                var rgba = new byte[count * 4];
                for (var i = 0; i < count; i++)
                {
                    var d = i * 4;
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = 255;
                    rgba[d + 3] = texture.Data[i];
                }

                return new UiImage(did, texture.Width, texture.Height, UiPixelFormat.A8, rgba);
            }

            case DecodedTextureFormat.Rgba8:
                return new UiImage(did, texture.Width, texture.Height, texture.SourceFormat switch
                {
                    PixelFormat.PFID_R8G8B8 => UiPixelFormat.Rgb,
                    PixelFormat.PFID_X8R8G8B8 => UiPixelFormat.Xrgb,
                    _ => UiPixelFormat.Argb,
                }, texture.Data);

            default:
                _log.Error(Category, $"TODO(retailui-runtime): {font}'s atlas 0x{did:X8} decodes as {texture.Format}, which the text rasterizer does not take (every retail font atlas is A8); the font draws without it");
                return null;
        }
    }
}

/// <summary>
/// One retail font (R5's <see cref="UiFont"/>) as an <see cref="IUiFont"/>, with its decoded atlases. Glyph lookup
/// follows <see cref="UiFont.GetCharDescIndex"/>; immutable, allocation-free, safe from any thread.
/// </summary>
public sealed class UiFontAdapter : IUiFont
{
    private readonly UiGlyphDesc[] _descs;

    /// <summary>The adapter of <paramref name="font"/> with its decoded atlases.</summary>
    /// <param name="font">R5's font.</param>
    /// <param name="foreground">The foreground atlas, or null.</param>
    /// <param name="background">The background (outline) atlas, or null.</param>
    public UiFontAdapter(UiFont font, UiImage? foreground, UiImage? background)
    {
        ArgumentNullException.ThrowIfNull(font);
        Font = font;
        Foreground = foreground;
        Background = background;
        _descs = new UiGlyphDesc[font.Glyphs.Count];
        for (var i = 0; i < _descs.Length; i++)
        {
            var g = font.Glyphs[i];
            _descs[i] = new UiGlyphDesc(g.Unicode, g.OffsetX, g.OffsetY, g.Width, g.Height, g.HorizontalOffsetBefore, g.HorizontalOffsetAfter, g.VerticalOffsetBefore);
        }
    }

    /// <summary>R5's font.</summary>
    public UiFont Font { get; }

    /// <inheritdoc/>
    public uint Did => Font.Id;

    /// <inheritdoc/>
    public int MaxCharHeight => Font.MaxCharHeight;

    /// <inheritdoc/>
    public int MaxCharWidth => Font.MaxCharWidth;

    /// <inheritdoc/>
    public int BaselineOffset => Font.BaselineOffset;

    /// <inheritdoc/>
    public int NumHorizontalBorderPixels => Font.HorizontalBorderPixels;

    /// <inheritdoc/>
    public int NumVerticalBorderPixels => Font.VerticalBorderPixels;

    /// <inheritdoc/>
    public UiImage? Foreground { get; }

    /// <inheritdoc/>
    public UiImage? Background { get; }

    /// <inheritdoc/>
    public bool ContainsChar(char c) => Font.ContainsChar(c);

    /// <inheritdoc/>
    public bool TryGetCharDesc(char c, out UiGlyphDesc desc)
    {
        var index = Font.GetCharDescIndex(c);
        desc = index < 0 ? default : _descs[index];
        return index >= 0;
    }

    /// <summary>For logs: the font's.</summary>
    public override string ToString() => Font.ToString();
}

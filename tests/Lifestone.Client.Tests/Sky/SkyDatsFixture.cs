using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Tests.Sky;

/// <summary>The user's dats, the Region sky and a texture loader, opened once per test class.</summary>
public sealed class SkyDatsFixture : IDisposable
{
    private readonly Lazy<DatSet> _dats = new(() => DatSet.Open());
    private readonly Lazy<DerethSky> _sky;
    private readonly Lazy<TextureLoader> _textures;

    public SkyDatsFixture()
    {
        _sky = new Lazy<DerethSky>(() => DerethSky.FromDats(_dats.Value));
        _textures = new Lazy<TextureLoader>(() => new TextureLoader(_dats.Value));
    }

    public DatSet Dats => _dats.Value;

    public DerethSky Sky => _sky.Value;

    public TextureLoader Textures => _textures.Value;

    /// <summary>A Surface record (throws when missing).</summary>
    public SurfaceMaterial Surface(uint id) => Textures.GetSurface(id) ?? throw new InvalidDataException($"Surface 0x{id:X8} missing");

    public void Dispose()
    {
        if (_dats.IsValueCreated)
        {
            _dats.Value.Dispose();
        }
    }
}

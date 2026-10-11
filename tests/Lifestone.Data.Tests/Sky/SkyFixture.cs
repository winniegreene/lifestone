using Lifestone.Data.Dat;
using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

/// <summary>Opens the user's dats once per test class and builds the Region sky + calendar from them.</summary>
public sealed class SkyFixture : IDisposable
{
    private readonly Lazy<DatSet> _dats = new(() => DatSet.Open());
    private readonly Lazy<DerethSky> _sky;

    public SkyFixture()
    {
        _sky = new Lazy<DerethSky>(() => DerethSky.FromDats(_dats.Value));
    }

    public DatSet Dats => _dats.Value;

    public DerethSky Sky => _sky.Value;

    public DerethCalendar Calendar => Sky.Calendar;

    public void Dispose()
    {
        if (_dats.IsValueCreated)
        {
            _dats.Value.Dispose();
        }
    }
}

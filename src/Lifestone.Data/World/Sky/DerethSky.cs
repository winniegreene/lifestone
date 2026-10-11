using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.World.Sky;

/// <summary>
/// Region sky + calendar: game time in, retail sky/light/fog state out. Immutable and thread-safe.
/// </summary>
/// <remarks>
/// Retail per frame (<c>LScape::UseTime</c> -> <c>GameSky::UseTime</c>): <c>GameTime::UseTime</c> updates
/// <c>present_time_of_day</c>, <c>CRegionDesc::CalcDayGroup</c> re-hashes the current day into a day group, then
/// <c>GetSky</c>/<c>GetLighting</c>/<c>GetWorldFog</c> are evaluated at that float time of day. Retail re-reads fog only
/// every <see cref="SkyDescription.TickSize"/> seconds and re-lights terrain/objects only every
/// <see cref="SkyDescription.LightTickSize"/> seconds; sky objects move every frame. Callers that want that cadence
/// should sample <see cref="Evaluate(double)"/> on the same schedule.
/// </remarks>
public sealed class DerethSky
{
    public DerethSky(SkyDescription sky, DerethCalendar calendar)
    {
        Sky = sky ?? throw new ArgumentNullException(nameof(sky));
        Calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    public SkyDescription Sky { get; }

    public DerethCalendar Calendar { get; }

    public static DerethSky FromRegion(Region region) =>
        new(SkyDescription.FromRegion(region), DerethCalendar.FromRegion(region));

    /// <summary>Builds from the Region (0x13000000) of the user's dats.</summary>
    public static DerethSky FromDats(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return FromRegion(dats.Region);
    }

    /// <summary>The retail day group for the Dereth day containing <paramref name="gameTime"/>
    /// (<see cref="SkyDescription.CalcPresentDayGroup(int,int,int,int)"/>).</summary>
    public int DayGroupIndexAt(double gameTime) => DayGroupIndexFor(Calendar.ToDate(gameTime));

    /// <summary>The retail day group for a calendar date.</summary>
    public int DayGroupIndexFor(DerethDate date)
    {
        ArgumentNullException.ThrowIfNull(date);
        return SkyDescription.CalcPresentDayGroup(date.DayOfYear, Calendar.DaysPerYear, date.Year, Sky.DayGroups.Count);
    }

    /// <summary>Sky state at a game time with the retail day group for that day.</summary>
    public SkyState Evaluate(double gameTime)
    {
        var date = Calendar.ToDate(gameTime);
        return SkyEvaluator.Evaluate(Sky.DayGroups[DayGroupIndexFor(date)], date.DayFraction, date);
    }

    /// <summary>Sky state at a game time with a forced day group (weather override / debug).</summary>
    public SkyState Evaluate(double gameTime, int dayGroupIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayGroupIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dayGroupIndex, Sky.DayGroups.Count);
        var date = Calendar.ToDate(gameTime);
        return SkyEvaluator.Evaluate(Sky.DayGroups[dayGroupIndex], date.DayFraction, date);
    }

    /// <summary>Sky state for a day group at a day fraction, with no calendar date (a time-of-day slider).</summary>
    public SkyState EvaluateAtTimeOfDay(int dayGroupIndex, float timeOfDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayGroupIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dayGroupIndex, Sky.DayGroups.Count);
        return SkyEvaluator.Evaluate(Sky.DayGroups[dayGroupIndex], timeOfDay);
    }
}

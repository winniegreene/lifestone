using System.Numerics;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Sky;

/// <summary>
/// What one <see cref="SkyDirector.Update"/> produced. The director re-uses one instance for every frame (the
/// per-frame path allocates nothing), so the values are valid until the next update; copy what must outlive it.
/// </summary>
public sealed class SkyFrame
{
    private readonly DerethCalendar _calendar;
    private readonly SkyObjectEvaluator _objects;
    private DerethDate? _date;

    internal SkyFrame(DerethCalendar calendar, SkyObjectEvaluator objects, SkyDayGroup firstGroup)
    {
        _calendar = calendar;
        _objects = objects;
        DayGroup = firstGroup;
    }

    /// <summary>The game time (seconds) this frame was evaluated at.</summary>
    public double GameTime { get; private set; }

    /// <summary>Retail <c>GameTime::present_time_of_day</c>: the float day fraction everything was evaluated at.</summary>
    public float TimeOfDay { get; private set; }

    /// <summary>The day group used (retail's for the day, or the forced one).</summary>
    public SkyDayGroup DayGroup { get; private set; }

    /// <summary>The light the world is lit with now (refreshed on the retail light cadence).</summary>
    public SkyLighting Lighting { get; private set; }

    /// <summary>The fog in force now (refreshed on the retail fog cadence; a disabled reading keeps the last one).</summary>
    public SkyFog Fog { get; private set; }

    /// <summary>The light was refreshed this frame.</summary>
    public bool LightingChanged { get; private set; }

    /// <summary>The fog was refreshed this frame.</summary>
    public bool FogChanged { get; private set; }

    /// <summary>A sky object was created or deleted this frame.</summary>
    public bool ObjectsChanged { get; private set; }

    /// <summary>Nothing moved since the previous frame (a paused clock): every value is the previous frame's.</summary>
    public bool Unchanged { get; private set; }

    /// <summary>This frame's sky objects (<c>SkyDesc::GetSky</c> + <c>GameSky::UseTime</c>), in dat (draw) order.</summary>
    public ReadOnlySpan<SkyObjectFrame> Objects => _objects.Objects;

    /// <summary>
    /// The calendar date of <see cref="GameTime"/>, built on first use each frame. It formats retail's date and time
    /// strings, so it allocates: for the UI and diagnostics, not for the per-frame path.
    /// </summary>
    public DerethDate Date => _date ??= _calendar.ToDate(GameTime);

    internal void Set(double gameTime, float timeOfDay, SkyDayGroup dayGroup, SkyLighting lighting, SkyFog fog, bool lightingChanged, bool fogChanged, bool objectsChanged)
    {
        if (gameTime != GameTime)
        {
            _date = null;
        }

        GameTime = gameTime;
        TimeOfDay = timeOfDay;
        DayGroup = dayGroup;
        Lighting = lighting;
        Fog = fog;
        LightingChanged = lightingChanged;
        FogChanged = fogChanged;
        ObjectsChanged = objectsChanged;
        Unchanged = false;
    }

    internal void SetUnchanged()
    {
        LightingChanged = false;
        FogChanged = false;
        ObjectsChanged = false;
        Unchanged = true;
    }
}

/// <summary>
/// Retail's per-frame sky driver, engine-agnostic: evaluates the Region sky at the game time, keeps the sky objects
/// (<see cref="SkyObjectTracker"/>) and their texture scroll (<see cref="SkyTextureScroll"/>), and refreshes the world
/// light and fog on retail's cadence (<see cref="SkyUpdateCadence"/>).
/// </summary>
/// <remarks>
/// <para>Retail order per frame (<c>LScape::UseTime</c>, acclient.c:307221, 11.4186): <c>GameSky::UseTime</c> (day
/// group, <c>GetSky</c>, sky object frames and lighting), then, every <c>tick_size</c> (0.8 s) of
/// <c>Timer::cur_time</c>, <c>GetWorldFog</c> -&gt; <c>SetFFFogProperties</c>, and every <c>light_tick_size</c> (15 s)
/// <c>GetLighting</c> -&gt; ambient clamp -&gt; <c>set_landscape_lighting</c>. <c>GetWorldFog</c> returning 0 (fog off in
/// either bracketing keyframe) leaves the previous fog in force; every keyframe of the user's Region has fog on.</para>
/// <para>The cadence clock is the controller's <see cref="DerethTimeController.ClientClock"/>. A time jump or a forced
/// day group calls <see cref="SkyUpdateCadence.Reset"/>, the effect of retail's <c>LScape::SetDay</c> (acclient.c:306897),
/// so the light follows a slider at once.</para>
/// <para><b>No garbage per frame.</b> The update evaluates exactly what <see cref="DerethSky.Evaluate(double)"/> does
/// (the day's group, <c>present_time_of_day</c>, <c>GetSky</c>; light and fog only on their ticks) through
/// allocation-free paths: the day group is cached for the Dereth day it belongs to, the objects go through
/// <see cref="SkyObjectEvaluator"/>, and the frame object is re-used. Only a change of day (one calendar lookup), a
/// change of the drawn object set (the scroll registry's sync) and <see cref="SkyFrame.Date"/> allocate.
/// <c>SkyDirectorTests</c> pins both the equality with <see cref="DerethSky.Evaluate(double)"/> and the zero
/// allocation.</para>
/// </remarks>
public sealed class SkyDirector
{
    /// <summary>Game seconds around a day boundary in which the day group is looked up afresh instead of cached.</summary>
    private const double DayEdgeMargin = 1e-3;

    private readonly SkyUpdateCadence _cadence;
    private readonly SkyObjectEvaluator _evaluator = new();
    private readonly SkyFrame _frame;
    private int? _forcedDayGroup;
    private bool _refresh = true;
    private bool _started;
    private double _lastGameTime = double.NaN;
    private double _lastClientClock = double.NaN;
    private double _cachedDayBegin = double.NaN;
    private double _cachedDayEnd = double.NaN;
    private int _cachedDayGroup;

    public SkyDirector(DerethSky sky)
    {
        Sky = sky ?? throw new ArgumentNullException(nameof(sky));
        _cadence = new SkyUpdateCadence(sky.Sky);
        _frame = new SkyFrame(sky.Calendar, _evaluator, sky.Sky.DayGroups[0]);
    }

    public DerethSky Sky { get; }

    /// <summary>The sky objects.</summary>
    public SkyObjectTracker Objects { get; } = new();

    /// <summary>The cloud and rain texture scroll (retail <c>CPhysics</c>'s GfxObj velocity list).</summary>
    public SkyTextureScroll Scroll { get; } = new();

    /// <summary>
    /// A forced day group (debug; retail always uses <c>SkyDesc::CalcPresentDayGroup</c>), or null for the retail
    /// choice. Changing it refreshes the light at once.
    /// </summary>
    public int? ForcedDayGroup
    {
        get => _forcedDayGroup;
        set
        {
            if (value is { } v)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(v);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(v, Sky.Sky.DayGroups.Count);
            }

            if (_forcedDayGroup != value)
            {
                _forcedDayGroup = value;
                _refresh = true;
            }
        }
    }

    /// <summary>The frame of the last update (the re-used instance), or null before the first one.</summary>
    public SkyFrame? Last => _started ? _frame : null;

    /// <summary>Resolves a drawn Setup (0x02) id to its part GfxObj ids for the texture scroll; null = GfxObjs only.</summary>
    public Func<uint, IEnumerable<uint>>? PartResolver { get; set; }

    /// <summary>Forces the next update to refresh light and fog (retail <c>LScape::SetDay</c>).</summary>
    public void RefreshNow() => _refresh = true;

    /// <summary>The day group used at <paramref name="gameTime"/>: the forced one or retail's.</summary>
    public int DayGroupAt(double gameTime) => _forcedDayGroup ?? RetailDayGroup(gameTime);

    /// <summary>Runs one frame.</summary>
    /// <param name="gameTime">Game time (seconds).</param>
    /// <param name="clientClock">Monotonic client clock (retail <c>Timer::cur_time</c>; seconds).</param>
    /// <param name="discontinuity">The game time jumped since the last frame (refresh light and fog now).</param>
    /// <returns>The frame (the same instance every time; valid until the next update).</returns>
    /// <remarks>A refresh (first frame, a jump, a forced day group) applies light and fog at once and restarts the cadence
    /// from <paramref name="clientClock"/>, as <c>LScape::SetDay</c> zeroing both deadlines does in retail, where
    /// <c>Timer::cur_time</c> is always past them. (The cadence alone would not fire the light at a paused clock of 0.)</remarks>
    public SkyFrame Update(double gameTime, double clientClock, bool discontinuity = false)
    {
        var refresh = _refresh || discontinuity;
        _refresh = false;
        if (!refresh && _started && gameTime == _lastGameTime && clientClock == _lastClientClock)
        {
            // Nothing moved (a paused clock): the sky, its cadence and the scroll are exactly as last frame.
            _frame.SetUnchanged();
            return _frame;
        }

        _lastGameTime = gameTime;
        _lastClientClock = clientClock;

        // DerethSky.Evaluate: the day's group (CRegionDesc::CalcDayGroup) and present_time_of_day.
        var group = Sky.Sky.DayGroups[DayGroupAt(gameTime)];
        var timeOfDay = Sky.Calendar.DayFractionAt(gameTime);
        if (refresh)
        {
            _cadence.Reset();
        }

        var updates = _cadence.Tick(clientClock);
        if (refresh)
        {
            updates |= SkyUpdates.Lighting | SkyUpdates.Fog;
        }

        var lighting = _frame.Lighting;
        var fog = _frame.Fog;
        var lightingChanged = false;
        var fogChanged = false;
        if ((updates & SkyUpdates.Lighting) != 0)
        {
            lighting = SkyEvaluator.GetLighting(group, timeOfDay);
            lightingChanged = true;
        }

        if ((updates & SkyUpdates.Fog) != 0)
        {
            var reading = SkyEvaluator.GetWorldFog(group, timeOfDay);
            if (reading.Enabled || !_started)
            {
                fog = reading;
                fogChanged = true;
            }
        }

        var objects = _evaluator.Evaluate(group, timeOfDay);
        var objectsChanged = Objects.Update(objects, fog.Enabled);

        // The velocity registry only changes when objects are created or deleted (GameSky::MakeObject registers,
        // CPhysics::UpdateTexVelocity drops what is no longer used). That is rare, so the allocating sync is fine here.
        if (objectsChanged || refresh)
        {
            Scroll.Sync(SkyEvaluator.Evaluate(group, timeOfDay), PartResolver);
        }

        Scroll.Update(clientClock);
        _frame.Set(gameTime, timeOfDay, group, lighting, fog, lightingChanged, fogChanged, objectsChanged);
        _started = true;
        return _frame;
    }

    /// <summary>Every GfxObj or Setup id a day group can draw (defaults and keyframe replacements), for preloading.</summary>
    public static IReadOnlySet<uint> ModelIds(SkyDayGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var ids = new HashSet<uint>();
        foreach (var o in group.SkyObjects)
        {
            if (o.DefaultGfxObjId != 0)
            {
                ids.Add(o.DefaultGfxObjId);
            }
        }

        foreach (var k in group.Keyframes)
        {
            foreach (var r in k.Replacements)
            {
                if (r.GfxObjId != 0)
                {
                    ids.Add(r.GfxObjId);
                }
            }
        }

        return ids;
    }

    /// <summary>Every (model id, pass) the Region's sky can draw, over all day groups (for warming shaders up).</summary>
    public static IReadOnlySet<(uint Id, SkyPass Pass)> ModelUses(SkyDescription sky)
    {
        ArgumentNullException.ThrowIfNull(sky);
        var uses = new HashSet<(uint, SkyPass)>();
        foreach (var group in sky.DayGroups)
        {
            foreach (var o in group.SkyObjects)
            {
                var pass = (o.Properties & SkyObjectProperties.AfterPass) != 0 ? SkyPass.After : SkyPass.Before;
                if (o.DefaultGfxObjId != 0)
                {
                    uses.Add((o.DefaultGfxObjId, pass));
                }

                foreach (var k in group.Keyframes)
                {
                    foreach (var r in k.Replacements)
                    {
                        if (r.ObjectIndex == (uint)o.Index && r.GfxObjId != 0)
                        {
                            uses.Add((r.GfxObjId, pass));
                        }
                    }
                }
            }
        }

        return uses;
    }

    /// <summary>The scroll offset for a GfxObj, wrapped into [0, 1) where that is invisible (WRAP addressing).</summary>
    /// <remarks>Retail's offset only wraps when it reaches +1 and runs unbounded for negative velocities
    /// (<c>CPhysics::UpdateTexVelocity</c>); with WRAP addressing a whole-number shift samples the same texels, so the
    /// wrap only protects float precision over long (or time-lapse) sessions.</remarks>
    public Vector2 ScrollOffset(uint gfxObjId, bool wrapAddressing)
    {
        var offset = Scroll.GetOffset(gfxObjId);
        return wrapAddressing ? new Vector2(offset.X - MathF.Floor(offset.X), offset.Y - MathF.Floor(offset.Y)) : offset;
    }

    /// <summary>
    /// Retail's day group for the Dereth day of <paramref name="gameTime"/> (<see cref="DerethSky.DayGroupIndexAt"/>),
    /// cached for that day. The calendar lookup formats date strings, so it runs once per day, and again within
    /// <see cref="DayEdgeMargin"/> of a day boundary, where the cached bounds could round differently from the
    /// calendar's own arithmetic.
    /// </summary>
    private int RetailDayGroup(double gameTime)
    {
        if (gameTime >= _cachedDayBegin + DayEdgeMargin && gameTime < _cachedDayEnd - DayEdgeMargin)
        {
            return _cachedDayGroup;
        }

        var date = Sky.Calendar.ToDate(gameTime);
        _cachedDayBegin = date.DayBeginTime;
        _cachedDayEnd = date.DayBeginTime + Sky.Calendar.DayLength;
        _cachedDayGroup = Sky.DayGroupIndexFor(date);
        return _cachedDayGroup;
    }
}

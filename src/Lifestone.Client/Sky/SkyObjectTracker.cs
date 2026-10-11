using System.Numerics;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Sky;

/// <summary>Which of retail's two sky draws an object belongs to.</summary>
public enum SkyPass
{
    /// <summary><c>GameSky::Draw(0)</c>: before the landscape, from <c>before_sky_cell</c>'s object list.</summary>
    Before,

    /// <summary><c>GameSky::Draw(1)</c>: after the landscape, the "weather cell" (<c>after_sky_cell</c>), only while
    /// <c>LScape::weather_enabled</c>.</summary>
    After,
}

/// <summary>One sky object slot (an index into the day group's objects) as retail's <c>GameSky</c> holds it.</summary>
public sealed class SkySlot
{
    internal SkySlot(int index) => Index = index;

    /// <summary>Index in <c>sky_obj</c> (the day group's object order, which is also the draw order).</summary>
    public int Index { get; }

    /// <summary>The GfxObj (0x01) or Setup (0x02) of the live physics object; 0 when the slot holds none.</summary>
    public uint GfxObjId { get; internal set; }

    /// <summary><c>property_array[i]</c>: the properties the object was created with.</summary>
    public SkyObjectProperties Properties { get; internal set; }

    /// <summary>True while retail would hold a <c>CPhysicsObj</c> here (<c>GameSky::MakeObject</c> succeeded).</summary>
    public bool Exists => GfxObjId != 0;

    /// <summary>Changes whenever the slot's physics object is deleted or created: a new object starts with
    /// <see cref="SkyPartMaterial.Default"/>.</summary>
    public int Generation { get; internal set; }

    /// <summary>The parts' lighting state (sticky between frames, as retail's physics object keeps it).</summary>
    public SkyPartMaterial Material { get; internal set; } = SkyPartMaterial.Default;

    /// <summary>Object-to-landblock rotation (AC frame) from <c>GameSky::CalcFrame</c>.</summary>
    public Quaternion Orientation { get; internal set; } = Quaternion.Identity;

    /// <summary>UV scroll per second (retail registers it with <c>CPhysics::AddGfxVelocity</c>).</summary>
    public Vector2 TexVelocity { get; internal set; }

    /// <summary>The pass that draws it.</summary>
    public SkyPass Pass => (Properties & SkyObjectProperties.AfterPass) != 0 ? SkyPass.After : SkyPass.Before;

    /// <summary>A weather object: pinned to the viewer's x, y (<c>GameSky::UpdatePosition</c>).</summary>
    public bool IsWeather => (Properties & SkyObjectProperties.Weather) != 0;

    /// <summary>True when this frame's <c>GameSky::Draw</c> draws it (set by <see cref="SkyObjectTracker.Update(ReadOnlySpan{SkyObjectFrame}, bool)"/>).</summary>
    public bool Drawn { get; internal set; }

    public override string ToString() =>
        $"sky[{Index}] 0x{GfxObjId:X8} {Properties} gen {Generation} {(Drawn ? "drawn" : "not drawn")} {Material}";
}

/// <summary>
/// Retail's sky object bookkeeping, engine-agnostic: which physics object each slot holds, when it is deleted and
/// re-created, the lighting state it keeps between frames, where it sits and whether this frame draws it.
/// </summary>
/// <remarks>
/// <para>Re-expressed from the retail decompile (Mag-nus/acclient-AI-RE 11.4186, PORTAL/lscape/Sky.c; nothing copied):</para>
/// <list type="bullet">
/// <item><c>GameSky::CreateDeletePhysicsObjects</c> (acclient.c:308687): slots beyond the new object count are deleted;
/// a slot keeps its object while the GfxObj id and the properties are unchanged (and, for weather objects, the weather
/// option did not change); otherwise the object is deleted and <c>GameSky::MakeObject</c> (308427) makes a new one,
/// which yields nothing for id 0 or for a weather object while weather is off. Kept objects keep their lighting
/// state, also across a change of day group.</item>
/// <item><c>GameSky::UseTime</c> (308826): every frame each live object gets its <c>GameSky::CalcFrame</c> rotation at
/// origin (0, 0, 0), then <c>SetLuminosity(l x 0.01)</c> if the keyframe sets l &gt; 0, <c>SetDiffusion(m x 0.01)</c>
/// if m &gt; 0, <c>SetTranslucency(t x 0.01)</c> if t &gt;= 0; values not set this frame stay as they were.</item>
/// <item><c>GameSky::UpdatePosition</c> (308367): non-weather objects keep origin (0, 0, 0) (an interpolation with
/// t = 0) in the <b>viewer's cell</b>, i.e. the south-west corner of the viewer's landblock at height 0; weather
/// objects take the viewer's x, y and z, with z forced to -120 m unless <see cref="SkyObjectProperties.WeatherKeepsPlayerZ"/>.
/// See <see cref="LocalOrigin"/>.</item>
/// <item><c>GameSky::Draw</c> (308475): the before pass draws, in slot order, every live object without
/// <see cref="SkyObjectProperties.AfterPass"/>, skipping weather objects while weather is off and
/// <see cref="SkyObjectProperties.HiddenUnderFogOverride"/> objects while the server fog override is on with fog
/// enabled; the after pass draws the weather cell (the <see cref="SkyObjectProperties.AfterPass"/> objects) while
/// weather is on. A part hidden by translucency 1.0 is skipped by <c>CPhysicsPart::Draw</c>.</item>
/// </list>
/// </remarks>
public sealed class SkyObjectTracker
{
    /// <summary>Weather objects without <see cref="SkyObjectProperties.WeatherKeepsPlayerZ"/> sit at this height
    /// (<c>GameSky::UpdatePosition</c> stores 0xC2F00000 = -120.0f, acclient.c:308415).</summary>
    public const float WeatherHeight = -120f;

    private readonly List<SkySlot> _slots = [];
    private bool? _lastWeatherEnabled;

    /// <summary>Retail <c>LScape::weather_enabled</c> (default true, acclient.c:45179).</summary>
    public bool WeatherEnabled { get; set; } = true;

    /// <summary>Retail <c>LScape::m_override_enabled</c>: the server's environment fog override (M2+, off in M1).</summary>
    public bool FogOverrideEnabled { get; set; }

    /// <summary>The slots, in draw order.</summary>
    public IReadOnlyList<SkySlot> Slots => _slots;

    /// <summary>Total objects created so far (diagnostics).</summary>
    public long CreatedCount { get; private set; }

    /// <summary>Total objects deleted so far (diagnostics).</summary>
    public long DeletedCount { get; private set; }

    /// <summary>
    /// Applies one frame's <c>SkyDesc::GetSky</c> result: creates and deletes objects, applies rotations and lighting,
    /// and decides what <c>GameSky::Draw</c> draws.
    /// </summary>
    /// <param name="state">This frame's sky state.</param>
    /// <param name="fogEnabled">Whether world fog is on (only matters with the fog override).</param>
    /// <returns>True when any slot's object was created or deleted.</returns>
    /// <remarks>Copies the objects (allocates); the per-frame path passes <see cref="SkyObjectEvaluator.Objects"/>.</remarks>
    public bool Update(SkyState state, bool fogEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Update(SkyObjectFrame.From(state), fogEnabled);
    }

    /// <summary>The same for one frame's objects; allocates nothing once the slots exist.</summary>
    /// <param name="objects">This frame's objects in dat order (<see cref="SkyObjectEvaluator.Objects"/>).</param>
    /// <param name="fogEnabled">Whether world fog is on (only matters with the fog override).</param>
    /// <returns>True when any slot's object was created or deleted.</returns>
    public bool Update(ReadOnlySpan<SkyObjectFrame> objects, bool fogEnabled = true)
    {
        var weatherOptionChanged = _lastWeatherEnabled is { } last && last != WeatherEnabled;
        _lastWeatherEnabled = WeatherEnabled;
        var changed = false;

        while (_slots.Count > objects.Length)
        {
            var removed = _slots[^1];
            if (removed.Exists)
            {
                DeletedCount++;
                changed = true;
            }

            _slots.RemoveAt(_slots.Count - 1);
        }

        while (_slots.Count < objects.Length)
        {
            _slots.Add(new SkySlot(_slots.Count));
        }

        for (var i = 0; i < objects.Length; i++)
        {
            var o = objects[i];
            var slot = _slots[i];
            if (slot.Exists)
            {
                var keep = slot.GfxObjId == o.GfxObjId
                           && slot.Properties == o.Properties
                           && !(weatherOptionChanged && (slot.Properties & SkyObjectProperties.Weather) != 0);
                if (keep)
                {
                    continue;
                }

                slot.GfxObjId = 0;
                slot.Generation++;
                DeletedCount++;
                changed = true;
            }

            slot.Properties = o.Properties;
            var weather = (o.Properties & SkyObjectProperties.Weather) != 0;
            if (o.GfxObjId != 0 && (!weather || WeatherEnabled))
            {
                slot.GfxObjId = o.GfxObjId;
                slot.Generation++;
                slot.Material = SkyPartMaterial.Default;
                CreatedCount++;
                changed = true;
            }
        }

        for (var i = 0; i < objects.Length; i++)
        {
            var o = objects[i];
            var slot = _slots[i];
            slot.TexVelocity = o.TexVelocity;
            if (!slot.Exists)
            {
                slot.Drawn = false;
                continue;
            }

            slot.Orientation = o.Orientation;
            var material = slot.Material;
            if (o.LuminosityScale is { } luminosity)
            {
                material = material.WithLuminosity(luminosity);
            }

            if (o.DiffusionScale is { } diffusion)
            {
                material = material.WithDiffusion(diffusion);
            }

            if (o.Translucency is { } translucency)
            {
                material = material.WithTranslucency(translucency);
            }

            slot.Material = material;
            slot.Drawn = IsDrawn(slot, fogEnabled);
        }

        return changed;
    }

    /// <summary>
    /// Where <c>GameSky::UpdatePosition</c> puts a slot's object, in the frame of the viewer's landblock (metres from its
    /// south-west corner at height 0; the viewer's cell is the anchor).
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <param name="viewerLocal">The viewer's position in the same landblock frame.</param>
    public static Vector3 LocalOrigin(SkySlot slot, Vector3 viewerLocal)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (!slot.IsWeather)
        {
            return Vector3.Zero;
        }

        var keepZ = (slot.Properties & SkyObjectProperties.WeatherKeepsPlayerZ) != 0;
        return new Vector3(viewerLocal.X, viewerLocal.Y, keepZ ? viewerLocal.Z : WeatherHeight);
    }

    private bool IsDrawn(SkySlot slot, bool fogEnabled)
    {
        if (slot.Material.Hidden)
        {
            return false;
        }

        var p = slot.Properties;
        if ((p & SkyObjectProperties.AfterPass) != 0)
        {
            // GameSky::Draw(1) draws the after (weather) cell only while weather is enabled.
            return WeatherEnabled;
        }

        return (WeatherEnabled || (p & SkyObjectProperties.Weather) == 0)
               && !(FogOverrideEnabled && fogEnabled && (p & SkyObjectProperties.HiddenUnderFogOverride) != 0);
    }
}

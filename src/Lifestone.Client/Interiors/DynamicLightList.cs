using System.Numerics;
using Lifestone.Data.World;

namespace Lifestone.Client.Interiors;

/// <summary>One light of the dynamic list, as <see cref="DynamicLightList"/> keeps it after sorting.</summary>
/// <param name="OwnerId">The owning object's guid; <see cref="DynamicLightList.ViewerOwner"/> (0) for the viewer light.</param>
/// <param name="CellId">The cell the light is in.</param>
/// <param name="Position">The light's position in the landblock-local space of <paramref name="CellId"/>'s landblock (AC axes, metres).</param>
/// <param name="Color">Colour, 0-1 RGB.</param>
/// <param name="Intensity">Retail's LIGHTINFO intensity.</param>
/// <param name="Falloff">Retail's LIGHTINFO falloff (metres); the hardware light reaches <c>falloff x 1.5</c>.</param>
/// <param name="DistanceSq">Squared distance from the viewer, the sort key (retail <c>RenderLight.distancesq</c>).</param>
public readonly record struct DynamicLightEntry(uint OwnerId, uint CellId, Vector3 Position, Vector3 Color, float Intensity, float Falloff, float DistanceSq)
{
    /// <summary>True for the viewer light.</summary>
    public bool IsViewer => OwnerId == DynamicLightList.ViewerOwner;
}

/// <summary>
/// Retail's dynamic light list (<c>Render::world_lights</c>'s dynamic half): at most seven point lights, nearest the viewer first
/// (<c>Render::add_dynamic_light</c> 11.6096 0x0054E030 over <c>Render::insert_light</c> 0x0054DDC0 and the list's removal,
/// <c>Render::remove_object_light</c> 0x0054CDC0), with the viewer light always holding a slot.
/// </summary>
/// <remarks>
/// <para><b>How retail keeps it.</b> <c>SmartBox::set_viewer</c> (0x00452C80) runs from <c>SmartBox::update_viewer</c> once per drawn frame
/// (via <c>SmartBox::DrawNoBlit</c>): it empties the list, adds the viewer light, and <c>CObjCell::add_dynamic_lights</c> adds every object light
/// again. <c>insert_light</c> puts a light at the first place whose squared distance from the viewer is larger (ties go behind), drops a light that would
/// be last in a full list, and otherwise pushes the farthest out. This class does the same on every <see cref="Update"/>, from a registry of
/// sources (<see cref="Upsert"/>, <see cref="Remove"/>, <see cref="SetOn"/>) so that a light pushed out of the seven comes back when it is nearer.
/// Deviation (the slice brief's rule, named): the viewer light is never the one pushed out; retail's could be by seven nearer lights.</para>
/// <para><b>Viewer light</b> (<c>SmartBox::set_viewer</c>; the values are the registry entries <c>SmartBox.ViewerLightIntensity</c> and
/// <c>.ViewerLightFalloff</c> of <c>SmartBox::SmartBox</c> 0x00454640): white, intensity 2.25, falloff 10, 2 m above the player (z only); at the viewer
/// itself, with no height, when there is no player.</para>
/// <para>Fixed arrays throughout: <see cref="Update"/> and the source calls allocate nothing.</para>
/// </remarks>
public sealed class DynamicLightList
{
    /// <summary>Most lights in the list (<c>Render::max_dynamic_lights</c>).</summary>
    public const int MaxLights = 7;

    /// <summary>Most registered sources (objects with lights); more are refused and counted in <see cref="SourcesRefused"/>.</summary>
    public const int MaxSources = 64;

    /// <summary>The owner id of the viewer light.</summary>
    public const uint ViewerOwner = 0;

    /// <summary>The viewer light's intensity (<c>SmartBox.ViewerLightIntensity</c>).</summary>
    public const float ViewerIntensity = 2.25f;

    /// <summary>The viewer light's falloff in metres (<c>SmartBox.ViewerLightFalloff</c>).</summary>
    public const float ViewerFalloff = 10f;

    /// <summary>The viewer light's height above the player (<c>SmartBox::set_viewer</c>: z of the light's offset).</summary>
    public const float ViewerHeight = 2f;

    /// <summary>The viewer light's colour, 0xFFFFFFFF.</summary>
    public static readonly Vector3 ViewerColor = Vector3.One;

    private struct Source
    {
        public uint OwnerId;
        public uint CellId;
        public Vector3 Position;
        public Vector3 Color;
        public float Intensity;
        public float Falloff;
        public bool On;
    }

    private readonly Source[] _sources = new Source[MaxSources];
    private readonly DynamicLightEntry[] _entries = new DynamicLightEntry[MaxLights];
    private int _sourceCount;
    private int _count;

    /// <summary>The viewer light takes part (<c>--viewer-light=on|off</c>, default on).</summary>
    public bool ViewerLightEnabled { get; set; } = true;

    /// <summary>Lights in the list now (after the last <see cref="Update"/>).</summary>
    public int Count => _count;

    /// <summary>Registered sources, on or off.</summary>
    public int SourceCount => _sourceCount;

    /// <summary>Sources refused because <see cref="MaxSources"/> were registered.</summary>
    public long SourcesRefused { get; private set; }

    /// <summary>The <paramref name="index"/>th light, nearest first.</summary>
    public ref readonly DynamicLightEntry this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _entries[index];
        }
    }

    /// <summary>True when <paramref name="ownerId"/> has a registered source.</summary>
    public bool Contains(uint ownerId) => IndexOf(ownerId) >= 0;

    /// <summary>Adds the owner's light, or replaces it. False when the registry is full.</summary>
    public bool Upsert(uint ownerId, uint cellId, Vector3 position, Vector3 color, float intensity, float falloff, bool on)
    {
        var i = IndexOf(ownerId);
        if (i < 0)
        {
            if (_sourceCount == MaxSources)
            {
                SourcesRefused++;
                return false;
            }

            i = _sourceCount++;
        }

        _sources[i] = new Source { OwnerId = ownerId, CellId = cellId, Position = position, Color = color, Intensity = intensity, Falloff = falloff, On = on };
        return true;
    }

    /// <summary>Moves the owner's light (<c>LIGHTLIST::set_frame</c>); false when it has none.</summary>
    public bool Move(uint ownerId, uint cellId, Vector3 position)
    {
        var i = IndexOf(ownerId);
        if (i < 0)
        {
            return false;
        }

        _sources[i].CellId = cellId;
        _sources[i].Position = position;
        return true;
    }

    /// <summary>Switches the owner's light on or off (<c>CPhysicsObj::set_lights</c> 0x005107C0); false when it has none.</summary>
    public bool SetOn(uint ownerId, bool on)
    {
        var i = IndexOf(ownerId);
        if (i < 0)
        {
            return false;
        }

        _sources[i].On = on;
        return true;
    }

    /// <summary>Removes the owner's light (the object left the world); false when it had none.</summary>
    public bool Remove(uint ownerId)
    {
        var i = IndexOf(ownerId);
        if (i < 0)
        {
            return false;
        }

        _sources[i] = _sources[--_sourceCount];
        return true;
    }

    /// <summary>Forgets every source and light.</summary>
    public void Clear()
    {
        _sourceCount = 0;
        _count = 0;
    }

    /// <summary>
    /// Rebuilds the list for this frame (<c>SmartBox::set_viewer</c>): the viewer light first, then every light that is on, each inserted by its squared distance from
    /// <paramref name="viewer"/>.
    /// </summary>
    /// <param name="viewer">The viewer (camera) position: the distances are measured from it.</param>
    /// <param name="player">The player's position, or null with no player (the viewer light then sits at the viewer, with no height).</param>
    public void Update(in CellFrame viewer, in CellFrame? player)
    {
        _count = 0;
        if (ViewerLightEnabled)
        {
            var at = player ?? viewer;
            var height = player.HasValue ? ViewerHeight : 0f;
            Insert(viewer, ViewerOwner, at.CellId, at.Origin + new Vector3(0f, 0f, height), ViewerColor, ViewerIntensity, ViewerFalloff);
        }

        for (var i = 0; i < _sourceCount; i++)
        {
            ref var s = ref _sources[i];
            if (s.On)
            {
                Insert(viewer, s.OwnerId, s.CellId, s.Position, s.Color, s.Intensity, s.Falloff);
            }
        }
    }

    private int IndexOf(uint ownerId)
    {
        for (var i = 0; i < _sourceCount; i++)
        {
            if (_sources[i].OwnerId == ownerId)
            {
                return i;
            }
        }

        return -1;
    }

    private void Insert(in CellFrame viewer, uint ownerId, uint cellId, Vector3 position, Vector3 color, float intensity, float falloff)
    {
        var distanceSq = 0f;
        if (viewer.CellId != 0 && cellId != 0)
        {
            distanceSq = Vector3.DistanceSquared(position + LandDefs.GetBlockOffset(viewer.CellId, cellId), viewer.Origin);
        }

        var at = 0;
        while (at < _count && !(distanceSq < _entries[at].DistanceSq))
        {
            at++;
        }

        if (_count == MaxLights)
        {
            if (at == _count)
            {
                return; // the farthest of a full list: dropped (insert_light)
            }

            // Push the farthest light out, never the viewer's; a light no nearer than that one is dropped itself.
            var pushed = _count - 1;
            while (pushed > 0 && _entries[pushed].IsViewer)
            {
                pushed--;
            }

            if (!(distanceSq < _entries[pushed].DistanceSq))
            {
                return;
            }

            for (var k = pushed; k < _count - 1; k++)
            {
                _entries[k] = _entries[k + 1];
            }

            _count--;
            if (pushed < at)
            {
                at--;
            }
        }

        for (var k = _count; k > at; k--)
        {
            _entries[k] = _entries[k - 1];
        }

        _entries[at] = new DynamicLightEntry(ownerId, cellId, position, color, intensity, falloff, distanceSq);
        _count++;
    }
}

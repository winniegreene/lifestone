using System.Numerics;

namespace Lifestone.Data.World.Sky;

/// <summary>
/// Retail UV scrolling for sky objects (clouds, rain): per-GfxObj accumulated texture offsets.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from the retail decompile (11.4186): <c>GameSky::MakeObject</c> (acclient.c:308427) calls
/// <c>CPhysicsObj::SetTextureVelocity(du, dv)</c> when the sky object's TexVelocity is non-zero; through
/// <c>CPartArray::SetTextureVelocityInternal</c> that registers every part's GfxObj DID with
/// <c>CPhysics::AddGfxVelocity</c> (acclient.c:311467): an existing entry only gets the new velocity (or is removed when
/// the velocity is 0), a new entry starts at offset (0, 0). <c>CPhysics::UseTime</c> (acclient.c:311335) computes
/// <c>quantum = Timer::cur_time - last_update</c> and, once <c>quantum &gt;= MIN_QUANTUM</c> (1/30 s), calls
/// <c>CPhysics::UpdateTexVelocity((float)quantum)</c> (acclient.c:311248), which for each entry whose GfxObj is still in
/// use does (all floats):
/// </para>
/// <code>
/// du = dt * velU;  dv = dt * velV;
/// if (u >= 1) du -= 1;   if (v >= 1) dv -= 1;
/// u += du;  v += dv;           // then CGfxObj::TexVelocity: mesh m_vUVDelta = (u, v)
/// </code>
/// <para>
/// The offset is therefore driven by real (client) time since the GfxObj was first registered, not by game time, and
/// only positive offsets are wrapped (negative velocities run unbounded). It is added to the mesh UVs
/// (<c>MeshBuffer::m_vUVDelta</c>, <c>m_bUseUVAnimation</c>).
/// </para>
/// </remarks>
public sealed class SkyTextureScroll
{
    /// <summary>Retail <c>MIN_QUANTUM</c> for <c>CPhysics::UseTime</c> (1.0 / 30.0, acclient.c:783717).</summary>
    public const double MinQuantum = 1.0 / 30.0;

    private readonly Dictionary<uint, Entry> _entries = [];
    private double? _lastUpdate;

    /// <summary>Number of registered GfxObjs.</summary>
    public int Count => _entries.Count;

    /// <summary><c>CPhysics::AddGfxVelocity</c> for one part GfxObj.</summary>
    public void AddGfxVelocity(uint gfxObjId, Vector2 velocity)
    {
        if (_entries.TryGetValue(gfxObjId, out var entry))
        {
            if (velocity.X != 0f || velocity.Y != 0f)
            {
                entry.Velocity = velocity;
            }
            else
            {
                _entries.Remove(gfxObjId);
            }

            return;
        }

        _entries[gfxObjId] = new Entry { Velocity = velocity };
    }

    /// <summary>Drops an entry, as <c>UpdateTexVelocity</c> does once the GfxObj is no longer in use.</summary>
    public bool Remove(uint gfxObjId) => _entries.Remove(gfxObjId);

    /// <summary>
    /// Keeps the registry in step with a sky state the way retail's object lifetime does: every drawn object with a
    /// non-zero TexVelocity registers its part GfxObjs (<c>GameSky::MakeObject</c> -> <c>SetTextureVelocity</c>), and
    /// entries whose GfxObj is no longer drawn by any sky object are dropped (<c>UpdateTexVelocity</c>'s
    /// <c>GetIfUsing</c> check). Call it whenever the drawn set may change (once per sky update is fine).
    /// </summary>
    /// <param name="state">The current sky state.</param>
    /// <param name="partGfxObjIds">Resolves a drawn id to the GfxObj DIDs of its parts. Default: a GfxObj (0x01) is its
    /// own single part; a Setup (0x02) with a non-zero TexVelocity throws, because its parts come from the Setup
    /// record (models track). Every scrolling sky object in the user's dat is a GfxObj.</param>
    public void Sync(SkyState state, Func<uint, IEnumerable<uint>>? partGfxObjIds = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        partGfxObjIds ??= DefaultParts;
        var inUse = new HashSet<uint>();
        foreach (var o in state.Objects)
        {
            if (!o.IsDrawn || (o.TexVelocity.X == 0f && o.TexVelocity.Y == 0f))
            {
                continue;
            }

            foreach (var part in partGfxObjIds(o.GfxObjId))
            {
                inUse.Add(part);
                if (!_entries.ContainsKey(part))
                {
                    AddGfxVelocity(part, o.TexVelocity);
                }
            }
        }

        foreach (var id in _entries.Keys.Where(id => !inUse.Contains(id)).ToArray())
        {
            _entries.Remove(id);
        }
    }

    private static IEnumerable<uint> DefaultParts(uint id) =>
        (id & 0xFF000000u) == 0x01000000u
            ? [id]
            : throw new NotSupportedException(
                $"TODO(sky): texture scroll for non-GfxObj sky object 0x{id:X8} needs its Setup part GfxObjs; pass partGfxObjIds.");

    /// <summary>Current offset (<c>m_vUVDelta</c>) for a GfxObj, or (0, 0) when it is not registered.</summary>
    public Vector2 GetOffset(uint gfxObjId) => _entries.TryGetValue(gfxObjId, out var e) ? new Vector2(e.U, e.V) : Vector2.Zero;

    /// <summary>
    /// <c>CPhysics::UseTime</c>'s texture part: call once per frame with the client clock in seconds. Steps the
    /// offsets by the elapsed time once at least <see cref="MinQuantum"/> has passed since the last step; a clock that
    /// goes backwards only resets the reference, as retail does.
    /// </summary>
    /// <returns>True when the offsets were stepped.</returns>
    public bool Update(double clockSeconds)
    {
        if (_lastUpdate is not { } last)
        {
            _lastUpdate = clockSeconds;
            return false;
        }

        var quantum = clockSeconds - last;
        if (!(quantum >= 0.0))
        {
            _lastUpdate = clockSeconds;
            return false;
        }

        if (quantum < MinQuantum)
        {
            return false;
        }

        _lastUpdate = clockSeconds;
        Step((float)quantum);
        return true;
    }

    /// <summary><c>CPhysics::UpdateTexVelocity(time_delta)</c> for every entry.</summary>
    public void Step(float timeDelta)
    {
        foreach (var e in _entries.Values)
        {
            double du = timeDelta * e.Velocity.X;
            double dv = timeDelta * e.Velocity.Y;
            if (e.U >= 1.0f)
            {
                du -= 1.0;
            }

            if (e.V >= 1.0f)
            {
                dv -= 1.0;
            }

            e.U = (float)(du + e.U);
            e.V = (float)(dv + e.V);
        }
    }

    private sealed class Entry
    {
        public Vector2 Velocity;
        public float U;
        public float V;
    }
}

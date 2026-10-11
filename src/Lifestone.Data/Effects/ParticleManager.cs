using Lifestone.Data.Animation;

namespace Lifestone.Data.Effects;

/// <summary>
/// The emitters of one object (retail <c>ParticleManager</c>, acclient 11.4186 PORTAL/cphysobj/CParticleManager.c):
/// creation with explicit or allocated ids, blocking creation, stop, destroy, and the per-update sweep that removes
/// finished emitters.
/// </summary>
/// <remarks>
/// Retail keeps emitters in a <c>LongNIHash</c> and updates them in hash-bucket order; we update in creation order.
/// This only changes which emitter consumes random numbers first within one update.
/// </remarks>
public sealed class ParticleManager
{
    private readonly Dictionary<uint, ParticleEmitter> _byId = [];
    private readonly List<ParticleEmitter> _order = [];

    /// <summary>Retail <c>ParticleManager::ParticleManager</c> starts allocated ids at 0xFFFF0000 (−65536).</summary>
    private uint _nextEmitterId = 0xFFFF0000;

    /// <summary>Tests: the next allocated id (to reach the wrap-around without 65,536 creations).</summary>
    internal uint NextEmitterId
    {
        get => _nextEmitterId;
        set => _nextEmitterId = value;
    }

    public IReadOnlyList<ParticleEmitter> Emitters => _order;

    public int Count => _order.Count;

    public bool TryGet(uint emitterId, out ParticleEmitter? emitter) => _byId.TryGetValue(emitterId, out emitter);

    /// <summary>
    /// Retail <c>CreateParticleEmitter</c>: a non-zero <paramref name="emitterId"/> first destroys the emitter with that
    /// id; the new emitter emits its initial particles immediately. Returns the id, or 0 on failure (unknown info,
    /// or an info without a hardware GfxObj — retail <c>SetInfo</c> fails then).
    /// </summary>
    public uint CreateParticleEmitter(
        ParticleEmitterInfoData? info, int partIndex, AcFrame offset, uint emitterId, double now, IEffectFrames parent, IEffectRandom random, List<string>? warnings)
    {
        if (emitterId != 0)
        {
            Remove(emitterId);
        }

        if (info is null || info.HwGfxObjId == 0)
        {
            return 0;
        }

        var id = emitterId != 0 ? emitterId : _nextEmitterId++;
        var emitter = new ParticleEmitter(id, info, partIndex, offset);
        emitter.InitEnd(now, parent, random, warnings);
        _byId[id] = emitter;
        _order.Add(emitter);
        return id;
    }

    /// <summary>Retail <c>CreateBlockingParticleEmitter</c>: does nothing while an emitter with the same non-zero id exists.</summary>
    public uint CreateBlockingParticleEmitter(
        ParticleEmitterInfoData? info, int partIndex, AcFrame offset, uint emitterId, double now, IEffectFrames parent, IEffectRandom random, List<string>? warnings)
    {
        if (emitterId != 0 && _byId.ContainsKey(emitterId))
        {
            return 0;
        }

        return CreateParticleEmitter(info, partIndex, offset, emitterId, now, parent, random, warnings);
    }

    /// <summary>Retail <c>StopParticleEmitter</c>: the emitter stops emitting; live particles finish their lives.</summary>
    public bool StopParticleEmitter(uint emitterId)
    {
        if (emitterId == 0 || !_byId.TryGetValue(emitterId, out var emitter))
        {
            return false;
        }

        emitter.Stopped = true;
        return true;
    }

    /// <summary>Retail <c>DestroyParticleEmitter</c>: removes the emitter and its particles at once.</summary>
    public bool DestroyParticleEmitter(uint emitterId) => emitterId != 0 && Remove(emitterId);

    /// <summary>Retail <c>ParticleManager::UpdateParticles</c>.</summary>
    public void UpdateParticles(double now, IEffectFrames parent, IEffectRandom random, bool shouldDraw, List<string>? warnings)
    {
        foreach (var emitter in _order.ToArray())
        {
            if (!emitter.Update(now, parent, random, shouldDraw, warnings))
            {
                _order.Remove(emitter);
                ForgetId(emitter);
            }
        }
    }

    /// <summary>
    /// Retail <c>ParticleManager::UpdateParticles</c> with retail's per-emitter draw test: each emitter has its own particle
    /// object and degrade distance (<c>ParticleEmitter::UpdateParticles</c> → <c>CPhysicsObj::ShouldDrawParticles</c>),
    /// which <paramref name="shouldDraw"/> answers (objects track, living statics).
    /// </summary>
    public void UpdateParticles(double now, IEffectFrames parent, IEffectRandom random, Func<ParticleEmitter, bool> shouldDraw, List<string>? warnings)
    {
        ArgumentNullException.ThrowIfNull(shouldDraw);

        // Same order as the overload above, without copying the list (statics update for as long as they exist):
        // an emitter's update never creates or destroys another one, so only the finished emitter leaves the list. It is
        // removed by index, so the loop always advances, even when its id no longer maps to it (below).
        for (var i = 0; i < _order.Count;)
        {
            var emitter = _order[i];
            if (emitter.Update(now, parent, random, shouldDraw(emitter), warnings))
            {
                i++;
            }
            else
            {
                _order.RemoveAt(i);
                ForgetId(emitter);
            }
        }
    }

    private bool Remove(uint emitterId)
    {
        if (!_byId.TryGetValue(emitterId, out var emitter))
        {
            return false;
        }

        _order.Remove(emitter);
        ForgetId(emitter);
        return true;
    }

    /// <summary>
    /// Drops a finished emitter's id, only if the id still maps to it: once the allocated ids wrap around (65,536
    /// allocations in one object's life) an allocated id can equal a live emitter's, and <c>_byId</c> then names the newer
    /// one while both stay in <c>_order</c> and keep updating. TODO(effects): what retail's <c>LongNIHash</c> does with
    /// such a duplicate id is not established; this only keeps the two collections consistent.
    /// </summary>
    private void ForgetId(ParticleEmitter emitter)
    {
        if (!_byId.TryGetValue(emitter.Id, out var mapped) || !ReferenceEquals(mapped, emitter))
        {
            return;
        }

        _byId.Remove(emitter.Id);
        for (var i = 0; i < _order.Count; i++)
        {
            // A live emitter that shares the id (wrapped allocation) takes it back, so it can still be stopped or destroyed.
            if (_order[i].Id == emitter.Id)
            {
                _byId[emitter.Id] = _order[i];
                break;
            }
        }
    }
}

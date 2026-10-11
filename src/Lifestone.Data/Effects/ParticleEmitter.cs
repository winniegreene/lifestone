using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Effects;

/// <summary>World frames of the object an emitter is attached to (its <c>CPhysicsObj</c> frame and part frames).</summary>
public interface IEffectFrames
{
    /// <summary>The object frame (retail <c>m_position.frame</c>).</summary>
    AcFrame ObjectFrame { get; }

    int PartCount { get; }

    /// <summary>World frame of part <paramref name="index"/> (retail <c>part_array-&gt;parts[i]-&gt;pos.frame</c>).</summary>
    AcFrame GetPartFrame(int index);
}

/// <summary>
/// A particle emitter attached to an object (retail <c>ParticleEmitter</c>, acclient 11.4186
/// PORTAL/cphysobj/CParticleEmitter.c: <c>SetInfo</c>, <c>SetParenting</c>, <c>InitEnd</c>, <c>EmitParticle</c>,
/// <c>UpdateParticles</c>, <c>KillParticle</c>, <c>StopEmitter</c>, <c>ShouldEmitParticle</c>,
/// <c>RecordParticleEmission</c>) together with <c>ParticleEmitterInfo</c>'s random/emission rules
/// (CParticleEmitterInfo.c).
/// </summary>
/// <remarks>
/// <c>ParticleEmitterInfo::ShouldEmitParticle</c>'s per-metre test was lost by the decompiler; it was read from the
/// user's 11.6096 <c>acclient.exe</c> (function 0x517F50): emit when <c>|Δ|² &gt; birthrate²</c>, Δ = emitter position
/// minus the position at the last emission (OpenAC <c>ParticleSystem</c> agrees; ACE compares the wrong quantities).
/// The emitter position is <c>Frame::combine(parent part or object frame, parent offset)</c>
/// (<c>CPhysicsObj::UpdateChild</c>); before the first emission it is the new particle object's origin, (0,0,0).
/// </remarks>
public sealed class ParticleEmitter
{
    private readonly Particle?[] _slots;
    private readonly Particle[] _storage;

    internal ParticleEmitter(uint id, ParticleEmitterInfoData info, int partIndex, AcFrame parentOffset)
    {
        Id = id;
        Info = info;
        PartIndex = partIndex;
        ParentOffset = parentOffset;
        _slots = new Particle?[Math.Max(0, info.MaxParticles)];
        _storage = new Particle[_slots.Length];
        for (var i = 0; i < _storage.Length; i++)
        {
            _storage[i] = new Particle();
        }
    }

    public uint Id { get; internal set; }

    public ParticleEmitterInfoData Info { get; }

    /// <summary>Part the emitter is attached to; −1 = the object frame.</summary>
    public int PartIndex { get; }

    public AcFrame ParentOffset { get; }

    public double CreationTime { get; private set; }

    public int NumParticles { get; private set; }

    public int TotalEmitted { get; private set; }

    public double LastEmitTime { get; private set; }

    public Vector3 LastEmitOffset { get; private set; }

    public double LastUpdateTime { get; private set; }

    public bool Stopped { get; internal set; }

    /// <summary>Retail <c>degraded_out</c>: the emitter is beyond its degrade distance and its particle object is hidden.</summary>
    public bool DegradedOut { get; private set; }

    /// <summary>Live particles by slot (null = free slot), in slot order.</summary>
    public IReadOnlyList<Particle?> Slots => _slots;

    /// <summary>Retail <c>ParticleEmitter::InitEnd</c>: records the creation time and emits the initial particles.</summary>
    internal void InitEnd(double now, IEffectFrames parent, IEffectRandom random, List<string>? warnings)
    {
        LastUpdateTime = now;
        LastEmitTime = now;
        LastEmitOffset = Vector3.Zero; // SetInfo: the fresh particle object's origin
        CreationTime = now;
        for (var i = 0; i < Info.InitialParticles; i++)
        {
            EmitParticle(now, parent, random, warnings);
        }
    }

    /// <summary>
    /// Retail <c>ParticleEmitter::UpdateParticles</c>. Returns false when the emitter is finished and should be removed.
    /// <paramref name="shouldDraw"/> stands for <c>CPhysicsObj::ShouldDrawParticles(degrade_distance)</c>, i.e. the
    /// viewer is within the particle GfxObj's maximum degrade distance.
    /// </summary>
    internal bool Update(double now, IEffectFrames parent, IEffectRandom random, bool shouldDraw, List<string>? warnings)
    {
        if (!shouldDraw)
        {
            DegradedOut = true;
            LastUpdateTime = now;
            if (Info.TotalParticles != 0 || Info.TotalSeconds != 0.0)
            {
                for (var i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i] is { } particle)
                    {
                        particle.Lifetime = now - particle.Birthtime;
                        KillParticle(i);
                    }
                }

                if (Stopped)
                {
                    return NumParticles != 0;
                }

                if (ShouldEmitParticle(now, parent, warnings))
                {
                    RecordParticleEmission(now, parent, warnings);
                }

                StopEmitter(now);
                return true;
            }

            foreach (var particle in _storage)
            {
                particle.ResetBirthtime(now);
            }

            return true;
        }

        DegradedOut = false;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is not { } particle)
            {
                continue;
            }

            var frame = Info.IsParentLocal ? ParentFrame(parent, warnings) : particle.StartFrame;
            particle.Update(Info.ParticleType, Info.IsPersistent, frame, now);
            KillParticle(i);
        }

        var alive = true;
        if (Stopped)
        {
            alive = NumParticles != 0;
        }
        else
        {
            if (ShouldEmitParticle(now, parent, warnings))
            {
                EmitParticle(now, parent, random, warnings);
            }

            StopEmitter(now);
        }

        LastUpdateTime = now;
        return alive;
    }

    /// <summary>Retail <c>EmitParticle</c>: first free slot; random draws in retail argument-evaluation order.</summary>
    private void EmitParticle(double now, IEffectFrames parent, IEffectRandom random, List<string>? warnings)
    {
        var slot = Array.IndexOf(_slots, null);
        if (slot < 0)
        {
            return;
        }

        var particle = _storage[slot];
        _slots[slot] = particle;
        var lifespan = GetRandomLifespan(random);
        var finalTrans = GetRandomFinalTrans(random);
        var startTrans = GetRandomStartTrans(random);
        var finalScale = GetRandomFinalScale(random);
        var startScale = GetRandomStartScale(random);
        var c = GetRandomScaled(Info.C, Info.MinC, Info.MaxC, random);
        var b = GetRandomScaled(Info.B, Info.MinB, Info.MaxB, random);
        var a = GetRandomScaled(Info.A, Info.MinA, Info.MaxA, random);
        var offset = GetRandomOffset(random);
        particle.Init(
            ParentFrame(parent, warnings),
            ParentOffset,
            offset,
            Info.ParticleType,
            Info.IsPersistent,
            a,
            b,
            c,
            startScale,
            finalScale,
            startTrans,
            finalTrans,
            lifespan,
            now,
            random);
        NumParticles++;
        TotalEmitted++;
        LastEmitOffset = EmitterPosition(parent, warnings);
        LastEmitTime = now;
    }

    /// <summary>Retail <c>RecordParticleEmission</c> (used on the degraded path: counts an emission without a particle).</summary>
    private void RecordParticleEmission(double now, IEffectFrames parent, List<string>? warnings)
    {
        NumParticles++;
        TotalEmitted++;
        LastEmitOffset = EmitterPosition(parent, warnings);
        LastEmitTime = now;
    }

    /// <summary>Retail <c>KillParticle</c>.</summary>
    private void KillParticle(int i)
    {
        if (_slots[i] is { } particle && particle.Lifetime >= particle.Lifespan)
        {
            _slots[i] = null;
            NumParticles--;
        }
    }

    /// <summary>Retail <c>StopEmitter</c>.</summary>
    private void StopEmitter(double now)
    {
        if (Stopped)
        {
            return;
        }

        if (Info.TotalSeconds > 0.0 && CreationTime + Info.TotalSeconds < now)
        {
            Stopped = true;
        }

        if (Info.TotalParticles != 0 && TotalEmitted >= Info.TotalParticles)
        {
            Stopped = true;
        }
    }

    /// <summary>Retail <c>ParticleEmitter::ShouldEmitParticle</c> + <c>ParticleEmitterInfo::ShouldEmitParticle</c>.</summary>
    private bool ShouldEmitParticle(double now, IEffectFrames parent, List<string>? warnings)
    {
        if ((Info.TotalParticles > 0 && TotalEmitted >= Info.TotalParticles) || NumParticles >= Info.MaxParticles)
        {
            return false;
        }

        var type = (int)Info.EmitterType;
        if ((type & 1) != 0)
        {
            return now - LastEmitTime > Info.Birthrate;
        }

        if ((type & 2) != 0)
        {
            var delta = EmitterPosition(parent, warnings) - LastEmitOffset;
            return (double)delta.LengthSquared() > Info.Birthrate * Info.Birthrate;
        }

        return false;
    }

    private Vector3 EmitterPosition(IEffectFrames parent, List<string>? warnings) =>
        AcFrame.Combine(ParentFrame(parent, warnings), ParentOffset).Origin;

    /// <summary>
    /// The frame the emitter hangs from. Retail indexes the part array without a range check when the index is not −1
    /// (<c>Particle::Init</c>); an out-of-range index is a data/Setup mismatch, so we use the object frame like
    /// <c>CPhysicsObj::UpdateChild</c> does and record a warning.
    /// </summary>
    private AcFrame ParentFrame(IEffectFrames parent, List<string>? warnings)
    {
        if (PartIndex == -1)
        {
            return parent.ObjectFrame;
        }

        if (PartIndex >= 0 && PartIndex < parent.PartCount)
        {
            return parent.GetPartFrame(PartIndex);
        }

        warnings?.Add($"Emitter {Id} (info 0x{Info.Id:X8}) is attached to part {PartIndex}, but the object has {parent.PartCount} parts; using the object frame.");
        return parent.ObjectFrame;
    }

    // ParticleEmitterInfo::GetRandom* (CParticleEmitterInfo.c 0x5170D0–0x5176D0).
    private double GetRandomLifespan(IEffectRandom random) => Math.Max(0.0, random.RollDice(-1f, 1f) * Info.LifespanRand + Info.Lifespan);

    private float GetRandomStartScale(IEffectRandom random) => ClampScale(random.RollDice(-1f, 1f) * Info.ScaleRand + Info.StartScale);

    private float GetRandomFinalScale(IEffectRandom random) => ClampScale(random.RollDice(-1f, 1f) * Info.ScaleRand + Info.FinalScale);

    private float GetRandomStartTrans(IEffectRandom random) => ClampTrans(random.RollDice(-1f, 1f) * Info.TransRand + Info.StartTrans);

    private float GetRandomFinalTrans(IEffectRandom random) => ClampTrans(random.RollDice(-1f, 1f) * Info.TransRand + Info.FinalTrans);

    private static float ClampScale(double v) => (float)(v >= 0.1 ? Math.Min(v, 10.0) : 0.1);

    private static float ClampTrans(double v) => (float)(v >= 0.0 ? Math.Min(v, 1.0) : 0.0);

    /// <summary>Retail <c>GetRandomA/B/C</c>: the base vector times a magnitude uniform in [min, max].</summary>
    private static Vector3 GetRandomScaled(Vector3 v, float min, float max, IEffectRandom random)
    {
        var magnitude = (max - min) * random.RollDice(0f, 1f) + min;
        return new Vector3((float)(magnitude * v.X), (float)(magnitude * v.Y), (float)(magnitude * v.Z));
    }

    /// <summary>
    /// Retail <c>GetRandomOffset</c>: a random direction with its component along <c>OffsetDir</c> removed, normalised,
    /// times a length uniform in [MinOffset, MaxOffset]. Draw order: z, y, x, then the length.
    /// </summary>
    private Vector3 GetRandomOffset(IEffectRandom random)
    {
        var z = random.RollDice(-1f, 1f);
        var y = random.RollDice(-1f, 1f);
        var x = random.RollDice(-1f, 1f);
        var dir = Info.OffsetDir;
        var dot = y * dir.Y + z * dir.Z + x * dir.X;
        var v = new Vector3((float)(x - dot * dir.X), (float)(y - dot * dir.Y), (float)(z - dot * dir.Z));
        if (Particle.NormalizeCheckSmall(v, out var n))
        {
            return Vector3.Zero;
        }

        var length = random.RollDice(0f, 1f) * (Info.MaxOffset - Info.MinOffset) + Info.MinOffset;
        return new Vector3((float)(n.X * length), (float)(n.Y * length), (float)(n.Z * length));
    }
}

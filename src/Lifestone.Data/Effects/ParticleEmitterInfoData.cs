using System.Numerics;
using DatEmitter = DatReaderWriter.DBObjs.ParticleEmitter;

namespace Lifestone.Data.Effects;

/// <summary>
/// Retail <c>ParticleType</c> (motion kernel of <c>Particle::Update</c>). Values match the dat and DatReaderWriter's
/// <c>ParticleType</c> enum; the names spell out which vectors are local (L, rotated by the parent frame at birth) or
/// global (G): V = velocity A, A = acceleration B, R = rotation C.
/// </summary>
public enum ParticleKind
{
    Unknown = 0,
    Still = 1,
    LocalVelocity = 2,
    ParabolicLVGA = 3,
    ParabolicLVGAGR = 4,
    Swarm = 5,
    Explode = 6,
    Implode = 7,
    ParabolicLVLA = 8,
    ParabolicLVLALR = 9,
    ParabolicGVGA = 10,
    ParabolicGVGAGR = 11,
    GlobalVelocity = 12,
}

/// <summary>Retail <c>EmitterType</c> bits tested by <c>ParticleEmitterInfo::ShouldEmitParticle</c>.</summary>
public enum EmitterKind
{
    Unknown = 0,
    BirthratePerSec = 1,
    BirthratePerMeter = 2,
}

/// <summary>
/// A ParticleEmitterInfo (0x32) — retail <c>ParticleEmitterInfo</c>. Decoded through DatReaderWriter's
/// <c>ParticleEmitter</c> type (176-byte layout, same as ACE <c>ACE.DatLoader/FileTypes/ParticleEmitterInfo.cs</c>).
/// </summary>
public sealed class ParticleEmitterInfoData
{
    public uint Id { get; init; }

    public EmitterKind EmitterType { get; init; }

    public ParticleKind ParticleType { get; init; }

    /// <summary>Software GfxObj; unused by the D3D client.</summary>
    public uint GfxObjId { get; init; }

    /// <summary>Hardware GfxObj — the mesh drawn for each particle (<c>ParticleEmitter::SetInfo</c> uses only this one; 0 = no emitter).</summary>
    public uint HwGfxObjId { get; init; }

    /// <summary>Seconds between emissions (per-second emitters) or metres between emissions (per-metre emitters).</summary>
    public double Birthrate { get; init; }

    public int MaxParticles { get; init; }

    public int InitialParticles { get; init; }

    /// <summary>Stop after this many particles (0 = unlimited).</summary>
    public int TotalParticles { get; init; }

    /// <summary>Stop emitting after this many seconds (0 = unlimited).</summary>
    public double TotalSeconds { get; init; }

    public double Lifespan { get; init; }

    public double LifespanRand { get; init; }

    public Vector3 OffsetDir { get; init; }

    public float MinOffset { get; init; }

    public float MaxOffset { get; init; }

    public Vector3 A { get; init; }

    public float MinA { get; init; }

    public float MaxA { get; init; }

    public Vector3 B { get; init; }

    public float MinB { get; init; }

    public float MaxB { get; init; }

    public Vector3 C { get; init; }

    public float MinC { get; init; }

    public float MaxC { get; init; }

    public float StartScale { get; init; }

    public float FinalScale { get; init; }

    public float ScaleRand { get; init; }

    public float StartTrans { get; init; }

    public float FinalTrans { get; init; }

    public float TransRand { get; init; }

    /// <summary>Particles follow the parent's <em>current</em> frame instead of the frame at their birth.</summary>
    public bool IsParentLocal { get; init; }

    /// <summary>
    /// Retail <c>ParticleEmitterInfo::InitEnd</c>: the sorting-sphere radius is <c>max(MaxOffset, MaxA · Lifespan)</c>,
    /// centred on the emitter.
    /// </summary>
    public float SortingSphereRadius => (float)Math.Max(MaxOffset, MaxA * Lifespan);

    /// <summary>Retail "persistent" particles: <c>!total_particles &amp;&amp; total_seconds == 0</c> (emitters that never stop).</summary>
    public bool IsPersistent => TotalParticles == 0 && TotalSeconds == 0.0;

    public static ParticleEmitterInfoData FromDat(DatEmitter e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new ParticleEmitterInfoData
        {
            Id = e.Id,
            EmitterType = (EmitterKind)(int)e.EmitterType,
            ParticleType = (ParticleKind)(int)e.ParticleType,
            GfxObjId = e.GfxObjId.DataId,
            HwGfxObjId = e.HwGfxObjId.DataId,
            Birthrate = e.Birthrate,
            MaxParticles = e.MaxParticles,
            InitialParticles = e.InitialParticles,
            TotalParticles = e.TotalParticles,
            TotalSeconds = e.TotalSeconds,
            Lifespan = e.Lifespan,
            LifespanRand = e.LifespanRand,
            OffsetDir = e.OffsetDir,
            MinOffset = e.MinOffset,
            MaxOffset = e.MaxOffset,
            A = e.A,
            MinA = e.MinA,
            MaxA = e.MaxA,
            B = e.B,
            MinB = e.MinB,
            MaxB = e.MaxB,
            C = e.C,
            MinC = e.MinC,
            MaxC = e.MaxC,
            StartScale = e.StartScale,
            FinalScale = e.FinalScale,
            ScaleRand = e.ScaleRand,
            StartTrans = e.StartTrans,
            FinalTrans = e.FinalTrans,
            TransRand = e.TransRand,
            IsParentLocal = e.IsParentLocal,
        };
    }
}

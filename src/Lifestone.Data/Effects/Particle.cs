using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Effects;

/// <summary>
/// One particle (retail <c>Particle</c> plus the state of its <c>CPhysicsPart</c>: frame, uniform scale,
/// translucency). Re-expresses retail <c>Particle::Init</c> and <c>Particle::Update</c> (acclient 11.4186
/// PORTAL/cphysobj/CParticleEmitter.c, 0x51C290 / 0x51C930).
/// </summary>
/// <remarks>
/// <para>
/// Retail mixes x87 extended precision with float temporaries; we compute in double and store floats, so positions
/// agree with retail to float rounding, not bit-exactly.
/// </para>
/// <para>
/// ACE <c>Physics/Particles/Particle.cs</c> (@95ef426d) differs from retail in several kernels; we follow retail:
/// parabolic types use <c>parent + offset + t·A + ½·B·t²</c> (ACE accumulates with <c>+=</c> and drops the parent and
/// the ½); <c>ParabolicLVGA</c>/<c>LVGAGR</c> also take A (local) and B/C (global) at birth (ACE keeps only B or C);
/// <c>Swarm</c> multiplies the sinusoids by C (ACE adds C); <c>Explode</c> adds <c>A.z·t</c> on z and its birth
/// direction's z term has no <c>cos(elevation)</c> factor (ACE has both differences).
/// </para>
/// </remarks>
public sealed class Particle
{
    /// <summary>Retail <c>birthtime</c> (a union with <c>last_update_time</c>: persistent particles reset it every update).</summary>
    public double Birthtime { get; private set; }

    public double Lifespan { get; private set; }

    public double Lifetime { get; internal set; }

    /// <summary>Parent frame (object or part) at birth; non-parent-local particles move relative to it.</summary>
    public AcFrame StartFrame { get; private set; }

    /// <summary>Birth offset, rotated into the parent space (Implode also scales it by C).</summary>
    public Vector3 Offset { get; private set; }

    public Vector3 A { get; private set; }

    public Vector3 B { get; private set; }

    public Vector3 C { get; private set; }

    public float StartScale { get; private set; }

    public float FinalScale { get; private set; }

    public float StartTrans { get; private set; }

    public float FinalTrans { get; private set; }

    /// <summary>World frame of the particle's part. Only the GR (rotating) kernels write the orientation.</summary>
    public AcFrame Frame { get; private set; } = AcFrame.Identity;

    /// <summary>Uniform <c>gfxobj_scale</c> of the particle's part.</summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>Part translucency (0 = opaque, 1 = invisible; retail <c>CPhysicsPart::SetTranslucency</c>).</summary>
    public float Translucency { get; private set; }

    /// <summary>Restores the birth time (retail degraded path of <c>ParticleEmitter::UpdateParticles</c>).</summary>
    internal void ResetBirthtime(double now) => Birthtime = now;

    /// <summary>Retail <c>Particle::Init</c>.</summary>
    internal void Init(
        AcFrame startFrame,
        AcFrame parentOffset,
        Vector3 randomOffset,
        ParticleKind type,
        bool persistent,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float startScale,
        float finalScale,
        float startTrans,
        float finalTrans,
        double lifespan,
        double now,
        IEffectRandom random)
    {
        Lifetime = 0.0;
        Birthtime = now;
        Lifespan = lifespan;
        StartFrame = startFrame;
        Offset = startFrame.LocalToGlobalVec(parentOffset.Origin + randomOffset);
        A = Vector3.Zero;
        B = Vector3.Zero;
        C = Vector3.Zero;

        switch (type)
        {
            case ParticleKind.ParabolicLVGAGR:
                C = c;
                B = b;
                A = startFrame.LocalToGlobalVec(a);
                break;
            case ParticleKind.ParabolicLVGA:
                B = b;
                A = startFrame.LocalToGlobalVec(a);
                break;
            case ParticleKind.LocalVelocity:
                A = startFrame.LocalToGlobalVec(a);
                break;
            case ParticleKind.ParabolicLVLALR:
                C = startFrame.LocalToGlobalVec(c);
                B = startFrame.LocalToGlobalVec(b);
                A = startFrame.LocalToGlobalVec(a);
                break;
            case ParticleKind.ParabolicLVLA:
                B = startFrame.LocalToGlobalVec(b);
                A = startFrame.LocalToGlobalVec(a);
                break;
            case ParticleKind.ParabolicGVGAGR:
                C = c;
                B = b;
                A = a;
                break;
            case ParticleKind.ParabolicGVGA:
                B = b;
                A = a;
                break;
            case ParticleKind.GlobalVelocity:
                A = a;
                break;
            case ParticleKind.Swarm:
                A = startFrame.LocalToGlobalVec(a);
                B = b;
                C = c;
                break;
            case ParticleKind.Implode:
                A = a;
                B = b;
                Offset *= c;
                C = Offset;
                break;
            case ParticleKind.Explode:
            {
                A = a;
                B = b;
                var azimuth = random.RollDice(-3.1415927f, 3.1415927f);
                var elevation = random.RollDice(-3.1415927f, 3.1415927f);
                var cosElevation = Math.Cos(elevation);
                var dir = new Vector3(
                    (float)(Math.Cos(azimuth) * c.X * cosElevation),
                    (float)(Math.Sin(azimuth) * c.Y * cosElevation),
                    (float)(Math.Sin(elevation) * c.Z));
                C = NormalizeCheckSmall(dir, out var normalized) ? Vector3.Zero : normalized;
                break;
            }

            case ParticleKind.Still:
                break;
            default:
                A = a;
                B = b;
                C = c;
                break;
        }

        StartScale = startScale;
        FinalScale = finalScale;
        StartTrans = startTrans;
        FinalTrans = finalTrans;
        Scale = startScale;
        Translucency = startTrans;
        Update(type, persistent, startFrame, now);
    }

    /// <summary>Retail <c>Particle::Update</c>: positions the part relative to <paramref name="parentFrame"/>.</summary>
    internal void Update(ParticleKind type, bool persistent, AcFrame parentFrame, double now)
    {
        var elapsed = now - Birthtime;
        if (persistent)
        {
            Lifetime += elapsed;
            Birthtime = now;
        }
        else
        {
            Lifetime = elapsed;
        }

        var t = Lifetime;
        var p = parentFrame.Origin;
        switch (type)
        {
            case ParticleKind.Still:
                Frame = Frame with { Origin = p + Offset };
                break;
            case ParticleKind.LocalVelocity:
            case ParticleKind.GlobalVelocity:
                Frame = Frame with { Origin = Vec(p.X + Offset.X + t * A.X, p.Y + Offset.Y + t * A.Y, p.Z + Offset.Z + t * A.Z) };
                break;
            case ParticleKind.ParabolicLVGA:
            case ParticleKind.ParabolicLVLA:
            case ParticleKind.ParabolicGVGA:
                Frame = Frame with { Origin = p + Parabola(t) };
                break;
            case ParticleKind.ParabolicLVGAGR:
            case ParticleKind.ParabolicLVLALR:
            case ParticleKind.ParabolicGVGAGR:
            {
                var frame = parentFrame with { Origin = parentFrame.Origin + Parabola(t) };
                Frame = frame.Rotate(new Vector3((float)(t * C.X), (float)(t * C.Y), (float)(t * C.Z)));
                break;
            }

            case ParticleKind.Swarm:
                Frame = Frame with
                {
                    Origin = Vec(
                        Math.Cos(B.X * t) * C.X + t * A.X + Offset.X + p.X,
                        Math.Sin(B.Y * t) * C.Y + t * A.Y + Offset.Y + p.Y,
                        Math.Cos(B.Z * t) * C.Z + t * A.Z + Offset.Z + p.Z),
                };
                break;
            case ParticleKind.Implode:
            {
                var k = Math.Cos(A.X * t);
                var t2 = t * t;
                Frame = Frame with
                {
                    Origin = Vec(
                        k * C.X + t2 * B.X + Offset.X + p.X,
                        k * C.Y + t2 * B.Y + Offset.Y + p.Y,
                        k * C.Z + t2 * B.Z + Offset.Z + p.Z),
                };
                break;
            }

            case ParticleKind.Explode:
                Frame = Frame with
                {
                    Origin = Vec(
                        (t * B.X + C.X * A.X) * t + Offset.X + p.X,
                        (t * B.Y + C.Y * A.X) * t + Offset.Y + p.Y,
                        (t * B.Z + C.Z * A.X + A.Z) * t + Offset.Z + p.Z),
                };
                break;
            default:
                // Retail Particle::Update has no case for other values: the part does not move.
                break;
        }

        var ratio = Lifetime < Lifespan ? Lifetime / Lifespan : 1.0;
        Scale = (float)((FinalScale - StartScale) * ratio + StartScale);
        Translucency = (float)((FinalTrans - StartTrans) * ratio + StartTrans);
    }

    /// <summary><c>offset + t·A + ½·B·t²</c> as written in the parabolic branches.</summary>
    private Vector3 Parabola(double t) => Vec(
        Offset.X + t * A.X + 0.5 * B.X * t * t,
        Offset.Y + t * A.Y + 0.5 * B.Y * t * t,
        Offset.Z + t * A.Z + 0.5 * B.Z * t * t);

    private static Vector3 Vec(double x, double y, double z) => new((float)x, (float)y, (float)z);

    /// <summary>Retail <c>AC1Legacy::Vector3::normalize_check_small</c>: true (and no result) when |v| &lt; ε.</summary>
    internal static bool NormalizeCheckSmall(Vector3 v, out Vector3 normalized)
    {
        var length = v.Length();
        if (length < AcFrame.Epsilon)
        {
            normalized = v;
            return true;
        }

        normalized = v / length;
        return false;
    }
}

// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Particles/Particle.cs (AGPL-3.0)
// [with changes: plain vectors instead of PhysicsPart/AFrame; and the retail corrections listed below, each one taken
// from acclient 11.4186 Particle::Init (0x51C930) / Particle::Update (0x51C290). With Corrected = false the oracle
// returns ACE's original formulas, which the tests use to show where ACE and retail differ].

using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Data.Tests.Effects;

/// <summary>Independent particle kernel: ACE's Particle.Init/Update with optional retail corrections.</summary>
internal static class AceParticleOracle
{
    internal sealed record Born(Vector3 Offset, Vector3 A, Vector3 B, Vector3 C);

    /// <summary>ACE <c>Particle.Init</c> (vector setup), with explode angles passed in instead of drawn.</summary>
    public static Born Init(ParticleKind type, AcFrame start, Vector3 parentOffsetOrigin, Vector3 randomOffset, Vector3 a, Vector3 b, Vector3 c,
        double azimuth, double elevation, bool corrected)
    {
        var offset = start.LocalToGlobalVec(parentOffsetOrigin + randomOffset);
        Vector3 A = Vector3.Zero, B = Vector3.Zero, C = Vector3.Zero;
        switch (type)
        {
            case ParticleKind.Still:
                break;
            case ParticleKind.LocalVelocity:
                A = start.LocalToGlobalVec(a);
                break;
            case ParticleKind.ParabolicLVGA:
                if (corrected)
                {
                    A = start.LocalToGlobalVec(a); // retail also sets A (local) ...
                    B = b;                          // ... and keeps B global (ACE rotates B)
                }
                else
                {
                    B = start.LocalToGlobalVec(b);
                }

                break;
            case ParticleKind.ParabolicLVGAGR:
                if (corrected)
                {
                    A = start.LocalToGlobalVec(a); // retail: A local, B and C global
                    B = b;
                    C = c;
                }
                else
                {
                    C = start.LocalToGlobalVec(c);
                }

                break;
            case ParticleKind.Swarm:
                A = start.LocalToGlobalVec(a);
                B = b;
                C = c;
                break;
            case ParticleKind.Explode:
            {
                A = a;
                B = b;
                var rb = Math.Cos(elevation);
                C = new Vector3(
                    (float)(Math.Cos(azimuth) * c.X * rb),
                    (float)(Math.Sin(azimuth) * c.Y * rb),
                    (float)(Math.Sin(elevation) * c.Z * (corrected ? 1.0 : rb))); // retail has no cos(elevation) on z
                var len = C.Length();
                C = len < 0.00019999999f ? Vector3.Zero : C / len;
                break;
            }

            case ParticleKind.Implode:
                A = a;
                B = b;
                offset *= c;
                C = offset;
                break;
            case ParticleKind.ParabolicLVLA:
                A = start.LocalToGlobalVec(a);
                B = start.LocalToGlobalVec(b);
                break;
            case ParticleKind.ParabolicLVLALR:
                if (corrected)
                {
                    A = start.LocalToGlobalVec(a); // retail rotates all three
                    B = start.LocalToGlobalVec(b);
                }

                C = start.LocalToGlobalVec(c);
                break;
            case ParticleKind.ParabolicGVGA:
                if (corrected)
                {
                    A = a; // retail also keeps A
                }

                B = b;
                break;
            case ParticleKind.ParabolicGVGAGR:
                if (corrected)
                {
                    A = a; // retail also keeps A and B
                    B = b;
                }

                C = c;
                break;
            case ParticleKind.GlobalVelocity:
                A = a;
                break;
        }

        return new Born(offset, A, B, C);
    }

    /// <summary>ACE <c>Particle.Update</c> position for lifetime <paramref name="t"/> relative to <paramref name="parent"/>.</summary>
    public static AcFrame Position(ParticleKind type, Born p, AcFrame parent, AcFrame previous, float t, bool corrected)
    {
        switch (type)
        {
            case ParticleKind.Still:
                return previous with { Origin = parent.Origin + p.Offset };
            case ParticleKind.LocalVelocity:
            case ParticleKind.GlobalVelocity:
                return previous with { Origin = t * p.A + parent.Origin + p.Offset };
            case ParticleKind.ParabolicLVGA:
            case ParticleKind.ParabolicLVLA:
            case ParticleKind.ParabolicGVGA:
                return corrected
                    ? previous with { Origin = parent.Origin + p.Offset + t * p.A + t * t * p.B / 2f } // retail: assigned, from the parent
                    : previous with { Origin = previous.Origin + t * t * p.B / 2f + t * p.A + p.Offset }; // ACE: accumulates
            case ParticleKind.ParabolicLVGAGR:
            case ParticleKind.ParabolicLVLALR:
            case ParticleKind.ParabolicGVGAGR:
            {
                // ACE: frame = parent; origin += ...; frame.Rotate(t·C). ACE's AFrame.GRotate treats the vector as
                // yaw/pitch/roll; retail Frame::rotate is an axis-angle rotation (local vector rotated to global, then
                // q' = q_w ⊗ q). The oracle uses the retail meaning, built from System.Numerics primitives.
                var origin = parent.Origin + t * t * p.B / 2f + t * p.A + p.Offset;
                var w = Vector3.Transform(t * p.C, parent.Orientation);
                var angle = w.Length();
                var q = angle < 0.00019999999f
                    ? parent.Orientation
                    : Quaternion.Normalize(Quaternion.CreateFromAxisAngle(w / angle, angle) * parent.Orientation);
                return new AcFrame(origin, q);
            }

            case ParticleKind.Swarm:
            {
                if (corrected)
                {
                    // retail: cos/sin terms scaled by C
                    var basePos = t * p.A + parent.Origin + p.Offset;
                    return previous with
                    {
                        Origin = new Vector3(
                            MathF.Cos(t * p.B.X) * p.C.X + basePos.X,
                            MathF.Sin(t * p.B.Y) * p.C.Y + basePos.Y,
                            MathF.Cos(t * p.B.Z) * p.C.Z + basePos.Z),
                    };
                }

                var swarm = t * p.A + p.C + parent.Origin + p.Offset;
                return previous with
                {
                    Origin = new Vector3(MathF.Cos(t * p.B.X) + swarm.X, MathF.Sin(t * p.B.Y) + swarm.Y, MathF.Cos(t * p.B.Z) + swarm.Z),
                };
            }

            case ParticleKind.Explode:
            {
                var o = (t * p.B + p.C * p.A.X) * t + p.Offset + parent.Origin;
                if (corrected)
                {
                    o.Z += p.A.Z * t; // retail z adds A.z·t
                }

                return previous with { Origin = o };
            }

            case ParticleKind.Implode:
                return previous with { Origin = MathF.Cos(p.A.X * t) * p.C + t * t * p.B + parent.Origin + p.Offset };
            default:
                return previous;
        }
    }
}

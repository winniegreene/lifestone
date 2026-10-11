using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Data.Tests.Effects;

/// <summary>Each <see cref="ParticleKind"/> kernel against the ACE-ported <see cref="AceParticleOracle"/> at sample times.</summary>
public sealed class ParticleKernelTests
{
    /// <summary>Returns queued values from RollDice (for Explode's two angle draws).</summary>
    internal sealed class QueueRandom(params double[] values) : IEffectRandom
    {
        private readonly Queue<double> _values = new(values);

        public double RollDice(float lower, float upper) => _values.Count > 0 ? _values.Dequeue() : lower;
    }

    private static readonly AcFrame Parent = new(new Vector3(10, 20, 5), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
    private static readonly Vector3 ParentOffsetOrigin = new(0, 0.4f, 1.2f);
    private static readonly Vector3 RandomOffset = new(0.1f, -0.2f, 0.3f);
    private static readonly Vector3 A = new(0.5f, 1.5f, 2.0f);
    private static readonly Vector3 B = new(-0.3f, 0.2f, -9.8f);
    private static readonly Vector3 C = new(0.7f, -1.1f, 1.3f);
    private const double Azimuth = 0.8;
    private const double Elevation = -0.4;

    public static TheoryData<ParticleKind> Kinds => new()
    {
        ParticleKind.Still, ParticleKind.LocalVelocity, ParticleKind.ParabolicLVGA, ParticleKind.ParabolicLVGAGR, ParticleKind.Swarm,
        ParticleKind.Explode, ParticleKind.Implode, ParticleKind.ParabolicLVLA, ParticleKind.ParabolicLVLALR, ParticleKind.ParabolicGVGA,
        ParticleKind.ParabolicGVGAGR, ParticleKind.GlobalVelocity,
    };

    private static Particle Born(ParticleKind kind)
    {
        var p = new Particle();
        p.Init(Parent, new AcFrame(ParentOffsetOrigin, Quaternion.Identity), RandomOffset, kind, persistent: false, A, B, C,
            startScale: 0.5f, finalScale: 2.5f, startTrans: 0.2f, finalTrans: 1.0f, lifespan: 2.0, now: 100.0, new QueueRandom(Azimuth, Elevation));
        return p;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Kernel_matches_the_ACE_oracle_with_retail_corrections(ParticleKind kind)
    {
        var particle = Born(kind);
        var born = AceParticleOracle.Init(kind, Parent, ParentOffsetOrigin, RandomOffset, A, B, C, Azimuth, Elevation, corrected: true);
        Assert.True(Vector3.Distance(born.Offset, particle.Offset) < 1e-5f);
        Assert.True(Vector3.Distance(born.A, particle.A) < 1e-5f, $"A {born.A} vs {particle.A}");
        Assert.True(Vector3.Distance(born.B, particle.B) < 1e-5f, $"B {born.B} vs {particle.B}");
        Assert.True(Vector3.Distance(born.C, particle.C) < 1e-5f, $"C {born.C} vs {particle.C}");

        foreach (var t in new[] { 0.0f, 0.1f, 0.75f, 1.9f })
        {
            particle.Update(kind, persistent: false, particle.StartFrame, 100.0 + t);
            var expected = AceParticleOracle.Position(kind, born, Parent, particle.Frame, t, corrected: true);
            Assert.True(Vector3.Distance(expected.Origin, particle.Frame.Origin) < 2e-4f, $"{kind} t={t}: {expected.Origin} vs {particle.Frame.Origin}");
            Assert.True(Math.Abs(Quaternion.Dot(expected.Orientation, particle.Frame.Orientation)) > 0.99999f, $"{kind} t={t}: orientation");
            Assert.Equal(0.5f + (2.5f - 0.5f) * t / 2f, particle.Scale, 4);
            Assert.Equal(0.2f + (1.0f - 0.2f) * t / 2f, particle.Translucency, 4);
        }

        particle.Update(kind, persistent: false, particle.StartFrame, 103.0); // past the lifespan: final values
        Assert.Equal(2.5f, particle.Scale);
        Assert.Equal(1.0f, particle.Translucency);
    }

    /// <summary>Kinds whose ACE port differs from retail (documented in <see cref="Particle"/> and docs/spell-visuals.md).</summary>
    public static TheoryData<ParticleKind, bool> AceDivergence => new()
    {
        { ParticleKind.Still, false },
        { ParticleKind.LocalVelocity, false },
        { ParticleKind.GlobalVelocity, false },
        { ParticleKind.Implode, false },
        { ParticleKind.ParabolicLVLA, true },     // update accumulates instead of assigning
        { ParticleKind.ParabolicLVGA, true },     // B rotated, A dropped, accumulation
        { ParticleKind.ParabolicGVGA, true },     // A dropped, accumulation
        { ParticleKind.ParabolicLVGAGR, true },   // A and B dropped, C rotated
        { ParticleKind.ParabolicLVLALR, true },   // A and B dropped
        { ParticleKind.ParabolicGVGAGR, true },   // A and B dropped
        { ParticleKind.Swarm, true },             // C added instead of scaling the sinusoids
        { ParticleKind.Explode, true },           // z direction and z drift
    };

    [Theory]
    [MemberData(nameof(AceDivergence))]
    public void ACE_uncorrected_kernels_differ_exactly_where_documented(ParticleKind kind, bool differs)
    {
        var particle = Born(kind);
        particle.Update(kind, persistent: false, particle.StartFrame, 100.75);
        var ace = AceParticleOracle.Init(kind, Parent, ParentOffsetOrigin, RandomOffset, A, B, C, Azimuth, Elevation, corrected: false);
        var acePos = AceParticleOracle.Position(kind, ace, Parent, AcFrame.Identity, 0.75f, corrected: false);
        Assert.Equal(differs, Vector3.Distance(acePos.Origin, particle.Frame.Origin) > 1e-3f);
    }

    [Fact]
    public void Persistent_particles_accumulate_lifetime_across_updates()
    {
        var p = new Particle();
        p.Init(Parent, AcFrame.Identity, Vector3.Zero, ParticleKind.GlobalVelocity, persistent: true, Vector3.UnitX, Vector3.Zero, Vector3.Zero,
            1f, 1f, 0f, 0f, lifespan: 5.0, now: 0.0, new QueueRandom());
        p.Update(ParticleKind.GlobalVelocity, persistent: true, Parent, 0.25);
        p.Update(ParticleKind.GlobalVelocity, persistent: true, Parent, 1.0);
        Assert.Equal(1.0, p.Lifetime, 9);
        Assert.Equal(1.0, p.Birthtime, 9); // birthtime doubles as last_update_time for persistent particles
        Assert.Equal(Parent.Origin.X + 1f, p.Frame.Origin.X, 5);
    }
}

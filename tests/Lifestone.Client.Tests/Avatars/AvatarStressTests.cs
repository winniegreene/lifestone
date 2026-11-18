using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Motion;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>The stress crowd (AV11): seeded scatter, walkers sent around by retail's MoveTo, and a timing report.</summary>
public sealed class AvatarStressTests(ITestOutputHelper output)
{
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void Generate_is_seeded_and_scatters_within_the_radius()
    {
        var center = AvatarRig.At(new Vector3(96, 96, 10));
        var a = AvatarStress.Generate(50, seed: 9, center, radius: 30f, [AvatarRig.HumanAt(0, default), AvatarStress.Object(AvatarRig.Lamp)]);
        var b = AvatarStress.Generate(50, seed: 9, center, radius: 30f, [AvatarRig.HumanAt(0, default), AvatarStress.Object(AvatarRig.Lamp)]);
        Assert.Equal(a.Select(c => c.Position), b.Select(c => c.Position));
        Assert.Equal(Enumerable.Range(0, 50).Select(i => AvatarStress.FirstGuid + (uint)i), a.Select(c => c.Guid));
        Assert.Equal(25, a.Count(c => c.SetupId == AvatarRig.Lamp));
        Assert.All(a, c => Assert.True(PositionFrames.Distance(center, c.Position!.Value) <= 30.01f));
    }

    [Fact]
    public void The_driver_sends_walkers_around_and_moveto_brings_them_to_their_points()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        var center = AvatarRig.At(new Vector3(96, 96, 0));
        var crowd = AvatarStress.Generate(10, seed: 3, center, radius: 20f, [AvatarRig.HumanAt(0, default)]);
        foreach (var create in crowd)
        {
            set.Create(create, now: 0);
        }

        var driver = new AvatarStressDriver(set, crowd.Select(c => c.Guid).ToArray(), center, 20f, seed: 4);
        var start = set.All.Select(a => a.Body.Position).ToArray();
        driver.Tick(0);
        Assert.Equal(10, driver.MovesSent);
        Assert.All(set.All, a => Assert.True(a.Body.Movement!.IsMovingTo));
        for (var i = 1; i <= 60 * 8; i++)
        {
            set.Advance(i / 60.0, Viewer, long.MaxValue);
        }

        var moved = set.All.Select((a, i) => PositionFrames.Distance(start[i], a.Body.Position)).ToArray();
        Assert.True(moved.Count(d => d > 1f) >= 8, string.Join(", ", moved));
        Assert.True(set.All.Count(a => !a.Body.Movement!.IsMovingTo) >= 8, "the walkers should have arrived");
        Assert.All(set.All, a => Assert.True(PositionFrames.Distance(center, a.Body.Position) <= 22f));
    }

    [Fact]
    [Trait("Tier", "Timing")] // m4-timing-tier-candidates: a per-frame wall-clock bound over 150 avatars
    public void Timing_of_150_walking_avatars()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        var center = AvatarRig.At(new Vector3(96, 96, 0));
        var crowd = AvatarStress.Generate(150, seed: 5, center, radius: 50f, [AvatarRig.HumanAt(0, default)]);
        foreach (var create in crowd)
        {
            set.Create(create, now: 0);
        }

        var driver = new AvatarStressDriver(set, crowd.Select(c => c.Guid).ToArray(), center, 50f, seed: 6);
        var ticks = 0L;
        for (var i = 1; i <= 600; i++)
        {
            var now = i / 60.0;
            driver.Tick(now);
            var start = Stopwatch.GetTimestamp();
            set.Advance(now, Viewer, long.MaxValue);
            ticks += Stopwatch.GetTimestamp() - start;
        }

        var ms = ticks * 1000.0 / Stopwatch.Frequency / 600;
        output.WriteLine($"150 avatars: {ms:0.000} ms per advance (draw output included)");
        Assert.True(ms < 20.0, $"{ms:0.000} ms per advance");
    }
}

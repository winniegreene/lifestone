using Lifestone.Client.Cinematic;
using Lifestone.Client.Navigation;

namespace Lifestone.Client.Tests.Cinematic;

public sealed class ShotListTests
{
    /// <summary>The committed hero shot list (tools\hero-video.ps1's default).</summary>
    private static readonly string HeroPath = RepositoryFiles.PathOf("src", "Lifestone.Game", "cinematics", "hero.json");

    private const string Minimal = """
        {
          "title": "t",
          "shots": [
            { "id": "a", "start": "holtburg", "duration": 5, "time": 0.3, "timeEnd": 0.35,
              "keys": [ { "pos": [0, -100, 50], "look": [0, 0, 0] }, { "pos": [50, -80, 40], "look": [0, 0, 0] } ] },
            { "id": "b", "scene": "spell", "spell": 85, "duration": 3, "castAt": 0.2, "ease": "ramp:0.1",
              "keys": [ { "t": 0, "yaw": 330, "pitch": -4, "distance": 3.5, "focus": "hands" },
                        { "t": 3, "yaw": 10, "pitch": -6, "distance": 6, "focus": "target", "offset": [0, 0, 0.2] } ] }
          ]
        }
        """;

    [Fact]
    public void A_minimal_list_parses_with_its_defaults()
    {
        var errors = new List<string>();
        var list = ShotList.Parse(Minimal, errors);
        Assert.Empty(errors);
        Assert.NotNull(list);
        Assert.Equal(60, list.Fps);
        Assert.Equal((1920, 1080), (list.Width, list.Height));
        Assert.Equal(4, list.Msaa);
        var a = list.Find("A")!;
        Assert.Equal(ShotScene.World, a.Scene);
        Assert.Equal(300, a.FrameCount(60));
        Assert.Equal(0.05 * 7620 / 5, a.EffectiveTimeSpeed(7620), 9);
        var b = list.Find("b")!;
        Assert.Equal(ShotScene.Spell, b.Scene);
        Assert.Equal(85u, b.Spell);
        Assert.Equal("hands", b.Keys[0].Focus);
        Assert.Equal(new Easing(EaseKind.Ramp, 0.1, 0.1), b.Ease);
        Assert.Equal(1.0, list.Shots[1].EffectiveTimeSpeed(7620));
    }

    [Theory]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "keys": [ { "pos": [0,0,0], "yaw": 0 } ], "colour": 1 } ] }""", "unknown property")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "keys": [ { "pos": [0,0,0], "yaw": 0 } ] }, { "id": "A", "start": "x", "duration": 1, "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "used 2 times")]
    [InlineData("""{ "shots": [ { "id": "a", "duration": 1, "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "needs a start")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "keys": [ { "pos": [0,0], "yaw": 0 } ] } ] }""", "three numbers")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "keys": [ { "pos": [0,0,0] } ] } ] }""", "yaw")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "ease": "wobble", "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "easing")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "keys": [ { "t": 0, "pos": [0,0,0], "yaw": 0 }, { "pos": [1,0,0], "yaw": 0 } ] } ] }""", "every key a time")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "keys": [ { "t": 0, "pos": [0,0,0], "yaw": 0 }, { "t": 2, "pos": [1,0,0], "yaw": 0 } ] } ] }""", "after the end")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "time": 0.2, "timeEnd": 0.3, "timeSpeed": 5, "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "not both")]
    [InlineData("""{ "shots": [ { "id": "a", "scene": "spell", "duration": 1, "keys": [ { "yaw": 0, "pitch": 0, "distance": 3 } ] } ] }""", "spell id")]
    [InlineData("""{ "shots": [ { "id": "a", "scene": "spell", "spell": 2, "duration": 1, "keys": [ { "yaw": 0, "pitch": 0, "distance": 3, "focus": "moon" } ] } ] }""", "focus")]
    [InlineData("""{ "shots": [ { "id": "a b", "start": "x", "duration": 1, "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "file-safe")]
    [InlineData("""{ "render": { "msaa": 3 }, "shots": [ { "id": "a", "start": "x", "duration": 1, "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "msaa")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "transition": "wipe", "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "transition")]
    [InlineData("""{ "shots": [ { "id": "a", "scene": "spell", "spell": 85, "duration": 1, "castAt": -40, "keys": [ { "yaw": 0, "pitch": 0, "distance": 3 } ] } ] }""", "castAt")]
    [InlineData("""{ "shots": [ { "id": "a", "start": "x", "duration": 1, "captionPosition": "middle", "keys": [ { "pos": [0,0,0], "yaw": 0 } ] } ] }""", "captionPosition")]
    [InlineData("""{ "shots": [] }""", "non-empty")]
    [InlineData("""not json""", "not valid JSON")]
    public void Problems_are_reported_with_their_place_and_nothing_is_returned(string json, string expected)
    {
        var errors = new List<string>();
        Assert.Null(ShotList.Parse(json, errors));
        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var errors = new List<string>();
        var list = ShotList.Parse("""
            { // a film
              "shots": [ { "id": "a", "start": "holtburg", "duration": 1, "keys": [ { "pos": [0, 0, 10], "yaw": 0, }, ], }, ],
            }
            """, errors);
        Assert.Empty(errors);
        Assert.Single(list!.Shots);
    }

    [Fact]
    public void The_hero_shot_list_is_valid_and_every_world_shot_starts_somewhere_real()
    {
        var errors = new List<string>();
        var list = ShotList.Parse(File.ReadAllText(HeroPath), errors);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.NotNull(list);
        Assert.Equal((1920, 1080, 60), (list.Width, list.Height, list.Fps));
        var catalog = TeleportCatalog.Default();
        foreach (var shot in list.Shots.Where(s => s.Scene == ShotScene.World))
        {
            Assert.True(GoldenViewpoints.TryResolveTarget(shot.Start!, catalog, out _, out var error), $"{shot.Id}: {error}");
            var path = ShotPaths.Build(shot, new Vec3d(0, 0, 0), (_, _) => null); // builds: keys consistent
            Assert.Equal(shot.Duration, path.Duration);
        }

        foreach (var shot in list.Shots.Where(s => s.Scene == ShotScene.Spell))
        {
            Assert.NotNull(ShotPaths.BuildOrbit(shot));
        }

        // The brief: about 70-90 s at 1080p60 once crossfades overlap the shots (tools\hero-video.ps1 adds the cards).
        Assert.Contains(list.Shots, s => s.Scene == ShotScene.Spell && s.Spell == 85); // Flame Bolt VI
        Assert.Contains(list.Shots, s => s.Scene == ShotScene.Spell && s.Spell == 1785); // Cassius' Ring of Fire
        Assert.Contains(list.Shots, s => s.Scene == ShotScene.Spell && s.Spell == 2645); // Portal Recall
        var shots = list.Shots.Sum(s => s.Duration);
        Assert.InRange(shots, 60, 100);
    }

    [Fact]
    public void An_orbit_track_hits_its_keys_blends_its_focus_and_follows_smoothly()
    {
        var keys = new[]
        {
            new OrbitKey(0, 350, -5, 4, "a", Vec3d.Zero),
            new OrbitKey(2, 10, -10, 8, "b", new Vec3d(0, 0, 1)),
        };
        var track = new OrbitTrack(keys, 2, Easing.Linear, 50, smoothSeconds: 0.2);
        Vec3d Points(string name) => name == "a" ? new Vec3d(0, 0, 0) : new Vec3d(10, 0, 0);
        var start = track.Sample(0, Points);
        Assert.Equal((350.0, -5.0, 4.0, 50.0), (start.Yaw, start.Pitch, start.Distance, start.Fov));
        Assert.Equal(new Vec3d(0, 0, 0), start.Focus);
        var end = track.Sample(2, Points);
        Assert.Equal((10.0, -10.0, 8.0), (end.Yaw, end.Pitch, end.Distance));
        Assert.Equal(new Vec3d(10, 0, 1), end.Focus);
        var middle = track.Sample(1, Points);
        Assert.Equal(0.0, middle.Yaw, 6); // the short way round
        Assert.Equal(new Vec3d(5, 0, 0.5), middle.Focus);

        // The follow filter cuts on its first step, then lags behind a jump and converges on it.
        var first = track.Step(0, 1 / 60.0, Points);
        Assert.Equal(start, first);
        var lagging = track.Step(2, 1 / 60.0, Points);
        Assert.True(lagging.Distance is > 4 and < 5, $"distance {lagging.Distance}");
        OrbitSample settled = lagging;
        for (var i = 0; i < 240; i++)
        {
            settled = track.Step(2, 1 / 60.0, Points);
        }

        Assert.Equal(8.0, settled.Distance, 3);
        Assert.Equal(10.0, settled.Yaw, 3);

        // The eye sits opposite the look direction: looking north and level, 8 m back is 8 m south.
        var eye = OrbitTrack.EyeOf(new OrbitSample(0, 0, 8, new Vec3d(1, 2, 3), 60));
        Assert.True(Vec3d.Distance(eye, new Vec3d(1, -6, 3)) < 1e-9);
    }
}

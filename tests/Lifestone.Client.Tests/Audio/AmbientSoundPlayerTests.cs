using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Client.Audio;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Audio;

/// <summary>A sink that records every ambient sound it is handed.</summary>
internal sealed class RecordingAmbientSink : ISoundSink
{
    public List<(uint Table, uint Type, SoundOrigin Origin, float Volume)> Tables { get; } = [];

    public List<(uint Table, uint Type)> Centred { get; } = [];

    public int Count => Tables.Count + Centred.Count;

    public void PlayFromCenter(uint soundTableId, uint soundType) => Centred.Add((soundTableId, soundType));

    public void PlayWave(uint waveId, float priority, float probability, float volume, SoundOrigin origin)
    {
    }

    public void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin, float volume) => Tables.Add((tableId, soundType, origin, volume));
}

/// <summary>A terrain source with one landblock (the home one) in which only the chosen vertices carry a scene slot.</summary>
internal sealed class FakeAmbientTerrain : IAmbientTerrain
{
    private readonly LandblockCoord _home;
    private readonly LandblockTerrain _terrain;

    /// <param name="home">The one landblock that exists.</param>
    /// <param name="vertices">The (x, y) vertices that name terrain type 0 slot 0; every other vertex names slot 1, which the test region does not have.</param>
    public FakeAmbientTerrain(LandblockCoord home, params (int X, int Y)[] vertices)
    {
        _home = home;
        var words = new ushort[TerrainConstants.VertexCount];
        Array.Fill(words, (ushort)(1 << 11));
        foreach (var (x, y) in vertices)
        {
            words[TerrainConstants.VertexIndex(x, y)] = 0;
        }

        _terrain = new LandblockTerrain(home, words, new byte[TerrainConstants.VertexCount]);
    }

    public LandblockTerrain? Get(LandblockCoord coord) => coord == _home ? _terrain : null;
}

/// <summary>
/// M3 1.3 s2b: <see cref="AmbientSoundPlayer"/>, retail's outdoor ambient rule (<c>LScape::add_ambient_sounds</c> 11.6096 0x00506270,
/// <c>Ambient::CalcWeight</c> 0x005519F0, <c>Ambient::CalcDir</c> 0x00551A60, <c>IntermitSound::PlayNow</c> 0x00551BC0 and the rest of
/// the <c>Ambient</c> family) re-expressed, on a region built in code.
/// </summary>
public sealed class AmbientSoundPlayerTests
{
    internal const uint TableId = 0x20000017;
    internal static readonly LandblockCoord Home = new(0xA9, 0xB4);
    private static readonly uint HomeCell = ((uint)Home.X << 24) | ((uint)Home.Y << 16) | 0x0021u;

    internal static RegionSoundData RegionOf(params AmbientSoundDesc[] sounds) =>
        RegionSoundData.FromRegion(new Region
        {
            SoundInfo = new SoundDesc { STBDesc = [new AmbientSTBDesc { STBId = TableId, AmbientSounds = [.. sounds] }] },
            SceneInfo = new SceneDesc { SceneTypes = [new SceneType { StbIndex = 0 }] },
            TerrainInfo = new TerrainDesc { TerrainTypes = [new TerrainType { SceneTypes = [0] }] },
        });

    internal static AmbientSoundDesc Sound(uint type, float volume, float chance, float min, float max) =>
        new() { SType = (DatReaderWriter.Enums.Sound)type, Volume = volume, BaseChance = chance, MinRate = min, MaxRate = max };

    internal static CellFrame At(float x, float y, float z = 0f) => new(HomeCell, new AcFrame(new System.Numerics.Vector3(x, y, z), System.Numerics.Quaternion.Identity));

    internal static AmbientSoundPlayer Player(RegionSoundData region, IAmbientTerrain terrain, ISoundSink sink, int seed = 7) =>
        new(region, [0f, 1f], terrain, sink, new RetailRandom(seed), uiSoundTableId: 0x2000004B);

    /// <summary>Runs 60 frames a second for <paramref name="seconds"/> at <paramref name="listener"/>.</summary>
    internal static void Run(AmbientSoundPlayer player, in CellFrame listener, bool outdoors, double seconds, double from = 0.0)
    {
        var frames = (int)(seconds * 60);
        for (var frame = 0; frame < frames; frame++)
        {
            player.Update(listener, outdoors, from + (frame / 60.0));
        }
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1f)]
    [InlineData(19.9f, 0f, 0f, 1f)]
    [InlineData(20f, 0f, 0f, 1f)]
    [InlineData(40f, 0f, 0f, 0.25f)]
    [InlineData(0f, 0f, 40f, 0.25f)]
    [InlineData(120.1f, 0f, 0f, 0f)]
    [InlineData(100f, 100f, 0f, 0f)]
    public void The_weight_is_one_inside_twenty_metres_then_four_hundred_over_the_squared_distance_and_zero_past_one_hundred_and_twenty(float x, float y, float z, float expected)
    {
        Assert.Equal(expected, AmbientSoundPlayer.CalcWeight(x, y, z), 5);
        Assert.Equal(400f / 14400f, AmbientSoundPlayer.CalcWeight(120f, 0f, 0f), 5);
    }

    [Theory]
    [InlineData(0f, 5f, 0)]
    [InlineData(9f, 9f, 0)]
    [InlineData(10f, 10f, 7)]
    [InlineData(0f, 15f, 1)]
    [InlineData(0f, -15f, 2)]
    [InlineData(15f, 0f, 3)]
    [InlineData(-15f, 0f, 4)]
    [InlineData(-15f, 15f, 5)]
    [InlineData(-15f, -15f, 6)]
    [InlineData(15f, 15f, 7)]
    [InlineData(15f, -15f, 8)]
    [InlineData(30f, 10f, 3)]
    [InlineData(10f, 30f, 1)]
    [InlineData(-10f, -30f, 2)]
    public void The_direction_is_none_inside_fourteen_metres_else_one_of_eight_sectors_split_at_two_to_one(float x, float y, int expected) =>
        Assert.Equal(expected, AmbientSoundPlayer.CalcDirection(x, y));

    [Fact]
    public void The_sector_headings_are_retails_compass_in_radians()
    {
        Assert.Equal(0f, AmbientSoundPlayer.SectorHeading(1));
        Assert.Equal(MathF.PI, AmbientSoundPlayer.SectorHeading(2), 5);
        Assert.Equal(MathF.PI / 2, AmbientSoundPlayer.SectorHeading(3), 5);
        Assert.Equal(3 * MathF.PI / 2, AmbientSoundPlayer.SectorHeading(4), 5);
        Assert.Equal(7 * MathF.PI / 4, AmbientSoundPlayer.SectorHeading(5), 5);
        Assert.Equal(5 * MathF.PI / 4, AmbientSoundPlayer.SectorHeading(6), 5);
        Assert.Equal(MathF.PI / 4, AmbientSoundPlayer.SectorHeading(7), 5);
        Assert.Equal(3 * MathF.PI / 4, AmbientSoundPlayer.SectorHeading(8), 5);
    }

    [Fact]
    public void An_intermittent_sound_with_chance_one_plays_each_time_it_comes_due_from_the_sector_it_was_met_in()
    {
        // One vertex at (3, 2) = (72, 48); the listener 40 m north of it at (72, 88): the sound is south of the listener, weight 400/1600.
        var sink = new RecordingAmbientSink();
        var player = Player(RegionOf(Sound(0x46, 0.6f, 1f, 2f, 2f)), new FakeAmbientTerrain(Home, (3, 2)), sink);

        Run(player, At(72f, 88f), outdoors: true, seconds: 10);

        Assert.InRange(player.Played, 4, 6); // at 0 s, then every 2 s (the next frame after each is due)
        Assert.Equal(player.Draws, player.Played);
        Assert.Equal(0, player.Skipped);
        Assert.Equal(player.Played, sink.Tables.Count);
        Assert.Equal(0.25f, player.TotalWeight, 5);
        Assert.Equal(1, player.ActiveSounds);
        foreach (var (table, type, origin, volume) in sink.Tables)
        {
            Assert.Equal(TableId, table);
            Assert.Equal(0x46u, type);
            Assert.Equal(0.6f, volume); // an intermittent sound plays at its own volume, unweighted
            Assert.InRange(origin.Distance, 30f, 50f); // the sector's range: 40 m from the listener, plus and minus 10
            var bearing = ((((origin.BearingDegrees + 180f) % 360f) + 360f) % 360f) - 180f; // wrapped to [-180, 180)
            Assert.InRange(bearing, -11.3f, 11.3f); // the source is south: the source-to-listener heading is north, within a sixteenth of a turn
        }
    }

    [Fact]
    public void A_vertex_in_the_listeners_own_square_adds_every_sector_with_a_near_and_far_distance_of_four_and_ten()
    {
        var sink = new RecordingAmbientSink();
        var player = Player(RegionOf(Sound(0x46, 0.6f, 1f, 0.5f, 0.5f)), new FakeAmbientTerrain(Home, (0, 0)), sink);

        Run(player, At(0f, 0f), outdoors: true, seconds: 60);

        Assert.True(player.Played > 50);
        Assert.All(sink.Tables, t => Assert.InRange(t.Origin.Distance, 3.9f, 10.1f));
        var sectors = sink.Tables.Select(t => (int)Math.Round((((t.Origin.BearingDegrees % 360f) + 360f) % 360f) / 45f) % 8).Distinct().Count();
        Assert.True(sectors >= 6, $"only {sectors} of 8 compass directions were used over {player.Played} sounds");
    }

    [Fact]
    public void A_chance_below_one_skips_some_draws_and_the_counters_add_up()
    {
        var sink = new RecordingAmbientSink();
        var player = Player(RegionOf(Sound(0x47, 0.5f, 0.5f, 0.5f, 0.5f)), new FakeAmbientTerrain(Home, (3, 2)), sink);

        Run(player, At(72f, 88f), outdoors: true, seconds: 60);

        Assert.True(player.Draws >= 80, $"{player.Draws} draws in a minute at one every half second");
        Assert.Equal(player.Draws, player.Played + player.Skipped);
        Assert.InRange(player.Played, 1, player.Draws - 1);
        Assert.Equal(player.Played, sink.Count);
    }

    [Fact]
    public void Indoors_nothing_is_drawn_and_a_queued_sound_that_comes_due_there_is_dropped_unplayed()
    {
        var sink = new RecordingAmbientSink();
        var player = Player(RegionOf(Sound(0x46, 0.6f, 1f, 2f, 2f)), new FakeAmbientTerrain(Home, (3, 2)), sink);

        Run(player, At(72f, 88f), outdoors: false, seconds: 5);
        Assert.Equal(0, player.Draws);
        Assert.Equal(0, player.OutdoorFrames);
        Assert.Equal(0, sink.Count);

        Run(player, At(72f, 88f), outdoors: true, seconds: 1, from: 5.0); // heard at once, queued for 2 s later
        var drawn = player.Draws;
        Assert.Equal(1, drawn);

        Run(player, At(72f, 88f), outdoors: false, seconds: 5, from: 6.0); // due while indoors
        Assert.Equal(drawn, player.Draws);
        Assert.Equal(drawn, sink.Count);

        Run(player, At(72f, 88f), outdoors: true, seconds: 1, from: 11.0); // back outside: heard again at once
        Assert.Equal(drawn + 1, player.Draws);
    }

    [Fact]
    public void A_constant_sound_plays_from_the_centre_at_its_weighted_volume_and_repeats_every_min_rate()
    {
        var sink = new RecordingAmbientSink();
        var player = Player(RegionOf(Sound(0x4D, 0.15f, 0f, 3f, 3f)), new FakeAmbientTerrain(Home, (3, 2)), sink);

        Run(player, At(72f, 88f), outdoors: true, seconds: 10);

        Assert.InRange(player.Played, 3, 5);
        Assert.Equal(player.Played, sink.Tables.Count);
        Assert.All(sink.Tables, t =>
        {
            Assert.Equal(SoundOrigin.Center, t.Origin);
            Assert.Equal(0.15f, t.Volume, 3); // volume / total weight * the sound's weight, and the table's one vertex is the whole weight
            Assert.Equal(0x4Du, t.Type);
        });
    }

    [Fact]
    public void A_constant_sound_weaker_than_three_hundredths_is_not_heard()
    {
        var sink = new RecordingAmbientSink();
        var player = Player(RegionOf(Sound(0x4D, 0.02f, 0f, 3f, 3f)), new FakeAmbientTerrain(Home, (3, 2)), sink);

        Run(player, At(72f, 88f), outdoors: true, seconds: 10);

        Assert.Equal(0, player.Draws);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void The_same_seed_plays_the_same_sounds_and_another_seed_does_not()
    {
        List<(uint Type, float Distance, float Bearing)> Record(int seed)
        {
            var sink = new RecordingAmbientSink();
            var player = Player(RegionOf(Sound(0x46, 0.6f, 0.7f, 0.5f, 3f), Sound(0x47, 0.4f, 0.9f, 0.4f, 2f)), new FakeAmbientTerrain(Home, (0, 0), (5, 5)), sink, seed);
            Run(player, At(10f, 10f), outdoors: true, seconds: 30);
            return [.. sink.Tables.Select(t => (t.Type, t.Origin.Distance, t.Origin.BearingDegrees))];
        }

        var first = Record(3);
        Assert.NotEmpty(first);
        Assert.Equal(first, Record(3));
        Assert.NotEqual(first, Record(4));
    }

    [Fact]
    public void A_disabled_player_does_nothing_and_the_environ_sound_plays_from_the_ui_table_centre()
    {
        var sink = new RecordingAmbientSink();
        var player = Player(RegionOf(Sound(0x46, 0.6f, 1f, 2f, 2f)), new FakeAmbientTerrain(Home, (3, 2)), sink);

        player.Enabled = false;
        Run(player, At(72f, 88f), outdoors: true, seconds: 3);
        Assert.Equal(0, player.Frames);
        Assert.Equal(0, sink.Count);

        Assert.True(player.ApplyEnviron(AdminEnvirons.Classify(118)));
        Assert.Equal((0x2000004Bu, 0x85u), Assert.Single(sink.Centred));
        Assert.Equal(1, player.EnvironSounds);

        // The fog options and the clear are counted and not drawn; an ignored option does nothing.
        Assert.False(player.ApplyEnviron(AdminEnvirons.Classify(3)));
        Assert.False(player.ApplyEnviron(AdminEnvirons.Classify(0)));
        Assert.False(player.ApplyEnviron(AdminEnvirons.Classify(115)));
        Assert.Equal(2, player.EnvironFogIgnored);
        Assert.Single(sink.Centred);

        // Without a UI sound table (the host could not resolve it) the sound is dropped, not played from table 0.
        player.UiSoundTableId = 0;
        Assert.False(player.ApplyEnviron(AdminEnvirons.Classify(120)));
        Assert.Single(sink.Centred);
    }

    [Theory]
    [InlineData(0u, EnvironKind.Clear, 0u)]
    [InlineData(1u, EnvironKind.Fog, 0u)]
    [InlineData(6u, EnvironKind.Fog, 0u)]
    [InlineData(9999u, EnvironKind.Fog, 0u)]
    [InlineData(7u, EnvironKind.Ignored, 0u)]
    [InlineData(100u, EnvironKind.Ignored, 0u)]
    [InlineData(101u, EnvironKind.Sound, 0x76u)] // Roar
    [InlineData(102u, EnvironKind.Sound, 0x77u)] // Bell
    [InlineData(114u, EnvironKind.Sound, 0x83u)] // LostSouls
    [InlineData(115u, EnvironKind.Ignored, 0u)]
    [InlineData(116u, EnvironKind.Ignored, 0u)]
    [InlineData(117u, EnvironKind.Sound, 0x84u)] // Squeal
    [InlineData(118u, EnvironKind.Sound, 0x85u)] // Thunder1
    [InlineData(123u, EnvironKind.Sound, 0x8Au)] // Thunder6
    [InlineData(124u, EnvironKind.Ignored, 0u)]
    [InlineData(125u, EnvironKind.Ignored, 0u)]
    [InlineData(9998u, EnvironKind.Ignored, 0u)]
    public void The_environ_options_are_classified_as_retails_handler_reads_them(uint option, EnvironKind kind, uint soundType) =>
        Assert.Equal(new EnvironEffect(kind, soundType), AdminEnvirons.Classify(option));
}

/// <summary>M3 1.3 s2b: a steady ambient frame allocates nothing (the player runs every frame).</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class AmbientSoundAllocationTests
{
    [Fact]
    public void Steady_ambient_frames_allocate_nothing()
    {
        var sink = new CountingSink();
        var region = AmbientSoundPlayerTests.RegionOf(
            AmbientSoundPlayerTests.Sound(0x46, 0.6f, 0.8f, 0.2f, 1f),
            AmbientSoundPlayerTests.Sound(0x47, 0.4f, 1f, 0.2f, 0.5f),
            AmbientSoundPlayerTests.Sound(0x4D, 0.15f, 0f, 0.25f, 0.25f));
        var terrain = new FakeAmbientTerrain(AmbientSoundPlayerTests.Home, (0, 0), (3, 2), (7, 7));
        var player = AmbientSoundPlayerTests.Player(region, terrain, sink);
        var listener = AmbientSoundPlayerTests.At(10f, 10f);
        var now = 0.0;

        void Frame()
        {
            player.Update(listener, outdoors: true, now);
            now += 1.0 / 60.0;
        }

        for (var warm = 0; warm < 300; warm++)
        {
            Frame(); // warm up (JIT, first sightings)
        }

        var allocated = 0L;
        var gcFrames = 0;
        for (var frame = 0; frame < 2000; frame++)
        {
            var window = AllocationWindow.Open();
            Frame();
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allocated += bytes;
        }

        Assert.True(sink.Plays > 100, $"only {sink.Plays} sounds played; the measurement would prove little");
        Assert.True(gcFrames <= 20, $"{gcFrames} of 2000 frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"steady ambient frames allocated {allocated} B over 2000 frames");
    }

    /// <summary>Counts and keeps nothing.</summary>
    private sealed class CountingSink : ISoundSink
    {
        public int Plays { get; private set; }

        public void PlayFromCenter(uint soundTableId, uint soundType) => Plays++;

        public void PlayWave(uint waveId, float priority, float probability, float volume, SoundOrigin origin) => Plays++;

        public void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin, float volume) => Plays++;
    }
}

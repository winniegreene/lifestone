using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// Where streaming looks while the player exists (<see cref="PlayerStreamingFocus"/>): the player's own position outdoors and in a
/// building cell that sees outside, and the dungeon rule in an EnvCell that does not (docs/interiors.md §5).
/// </summary>
public sealed class PlayerStreamingFocusTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>Holtburg's drop point, an outdoor cell of landblock 0xA9B4.</summary>
    private const uint HoltburgCell = 0xA9B40019;

    private const uint DungeonCell = 0x00070143;
    private const uint BuildingCell = 0xA9B40100;

    private static CellFrame At(uint cell, float x, float y, float z) => new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private static Func<uint, bool?> Classifier(Dictionary<uint, bool?> cells, List<uint>? asked = null) => id =>
    {
        asked?.Add(id);
        return cells.TryGetValue(id, out var sees) ? sees : null;
    };

    [Fact]
    public void Outdoors_the_focus_is_the_players_position()
    {
        var focus = new PlayerStreamingFocus(Classifier([]));
        var result = focus.Of(At(HoltburgCell, 84f, 7.1f, 94f));
        Assert.Equal(StreamingFocusKind.Outdoor, result.Kind);
        Assert.Equal(new GlobalPosition((0xA9 * 192.0) + 84.0, (0xB4 * 192.0) + 7.1f, 94.0), result.Position);
    }

    [Fact]
    public void In_a_building_that_sees_outside_the_landscape_stays_loaded_around_the_player()
    {
        var focus = new PlayerStreamingFocus(Classifier(new() { [BuildingCell] = true }));
        var result = focus.Of(At(BuildingCell, 61f, 45f, 94f));
        Assert.Equal(StreamingFocusKind.Outdoor, result.Kind);
        Assert.Equal(new GlobalPosition((0xA9 * 192.0) + 61.0, (0xB4 * 192.0) + 45.0, 94.0), result.Position);
        Assert.False(focus.IsDungeonCell(BuildingCell));
    }

    [Fact]
    public void In_a_dungeon_cell_the_focus_is_indoor_at_the_centre_of_the_cells_landblock()
    {
        var focus = new PlayerStreamingFocus(Classifier(new() { [DungeonCell] = false }));
        var result = focus.Of(At(DungeonCell, 70f, -60f, 0.005f));
        Assert.Equal(StreamingFocusKind.Indoor, result.Kind);
        Assert.Equal(96.0, result.Position.X);
        Assert.Equal((7 * 192.0) + 96.0, result.Position.Y);
        Assert.Equal(0.005f, (float)result.Position.Z);
        Assert.True(focus.IsDungeonCell(DungeonCell));
    }

    [Fact]
    public void A_dungeon_cell_far_outside_its_landblocks_square_still_names_its_own_landblock()
    {
        // Holtburg Redoubt's cells reach y = -85 and a town network's further; the position would name the wrong landblock.
        var focus = new PlayerStreamingFocus(Classifier(new() { [DungeonCell] = false }));
        var result = focus.Of(At(DungeonCell, 300f, -610f, -20f));
        Assert.Equal(StreamingFocusKind.Indoor, result.Kind);
        Assert.Equal(LandblockCoord.FromId(DungeonCell), AcCoordinates.LandblockAt(result.Position));
        Assert.Equal(96.0, result.Position.X);
        Assert.Equal((7 * 192.0) + 96.0, result.Position.Y);
    }

    [Fact]
    public void Leaving_the_dungeon_for_an_outdoor_cell_restores_the_outdoor_focus()
    {
        var focus = new PlayerStreamingFocus(Classifier(new() { [DungeonCell] = false }));
        Assert.Equal(StreamingFocusKind.Indoor, focus.Of(At(DungeonCell, 70f, -60f, 0f)).Kind);
        Assert.Equal(StreamingFocusKind.Outdoor, focus.Of(At(HoltburgCell, 84f, 7.1f, 94f)).Kind);
    }

    [Fact]
    public void Each_cell_is_classified_once()
    {
        var asked = new List<uint>();
        var focus = new PlayerStreamingFocus(Classifier(new() { [DungeonCell] = false, [BuildingCell] = true }, asked));
        for (var i = 0; i < 5; i++)
        {
            focus.Of(At(DungeonCell, 70f, -60f, 0f));
            focus.Of(At(BuildingCell, 61f, 45f, 94f));
            focus.Of(At(HoltburgCell, 84f, 7.1f, 94f));
        }

        Assert.Equal([DungeonCell, BuildingCell], asked);
    }

    [Fact]
    public void A_cell_missing_from_the_dats_is_loud_once_and_counts_as_outdoors()
    {
        var log = new RecordingLogSink();
        var focus = new PlayerStreamingFocus(Classifier([]), log);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(StreamingFocusKind.Outdoor, focus.Of(At(DungeonCell, 70f, -60f, 0f)).Kind);
        }

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, entry.Severity);
        Assert.Contains("0x00070143", entry.Line, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_dats_classify_the_town_network_as_a_dungeon_and_a_holtburg_house_as_seeing_outside()
    {
        var dats = fixture.Dats;
        var focus = new PlayerStreamingFocus(id => EnvCellData.Read(dats, id)?.SeenOutside);
        Assert.True(focus.IsDungeonCell(DungeonCell));
        Assert.False(focus.IsDungeonCell(BuildingCell));
        Assert.False(focus.IsDungeonCell(HoltburgCell));
        Assert.Equal(StreamingFocusKind.Indoor, focus.Of(At(DungeonCell, 70f, -60f, 0.005f)).Kind);
        Assert.Equal(StreamingFocusKind.Outdoor, focus.Of(At(BuildingCell, 61f, 45f, 94f)).Kind);
    }
}

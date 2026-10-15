using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Streaming;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The headless host's world (step ST15): what portal space's readiness waits for is the dat data the destination needs,
/// decoded when the host focuses on the player's cell; objects and collision are disabled layers; a missing or malformed
/// record is a settled failure with a WARNING, never a hang.
/// </summary>
public sealed class DataOnlyAttachmentsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Academy = 0x860201AD;

    [Fact]
    public void Nothing_is_ready_before_the_focus_and_queries_decode_nothing()
    {
        var source = new TableSource();
        var world = new DataOnlyAttachments(source, new RecordingLogSink());
        Assert.Equal(AttachState.Pending, world.Terrain(LandblockCoord.FromId(Academy)));
        Assert.Equal(AttachState.Pending, world.Cell(Academy));
        Assert.Null(world.VisibleCells(Academy));
        Assert.False(world.SeenOutside(Academy));
        Assert.Equal(AttachState.Disabled, world.Objects(LandblockCoord.FromId(Academy)));
        Assert.Equal(AttachState.Disabled, world.Collision(LandblockCoord.FromId(Academy)));
        Assert.Equal(0, source.Reads);
        Assert.False(DestinationReadiness.Evaluate(Academy, world, ReadinessPolicy.Default).IsReady);
    }

    [Fact]
    public void An_outdoor_cell_decodes_the_3x3_terrain_once()
    {
        var source = new TableSource();
        var world = new DataOnlyAttachments(source, new RecordingLogSink());
        const uint outdoor = 0xA9B40019;
        world.Focus(outdoor);
        world.Focus(outdoor);
        Assert.Equal(9, source.Reads);
        Assert.Equal(AttachState.Attached, world.Terrain(LandblockCoord.FromId(outdoor)));
        Assert.True(DestinationReadiness.Evaluate(outdoor, world, ReadinessPolicy.Default).IsReady);
    }

    [Fact]
    public void An_envcell_decodes_itself_and_what_it_sees_and_the_terrain_only_when_it_sees_outside()
    {
        var source = new TableSource();
        source.Cells[Academy] = new EnvCellInfo([0x860201AE, 0x860201AF], SeenOutside: false);
        source.Cells[0x860201AE] = new EnvCellInfo([Academy], SeenOutside: false);
        source.Cells[0x860201AF] = null;
        var log = new RecordingLogSink();
        var world = new DataOnlyAttachments(source, log);

        world.Focus(Academy);

        Assert.Equal(AttachState.Attached, world.Cell(Academy));
        Assert.Equal(AttachState.Attached, world.Cell(0x860201AE));
        Assert.Equal(AttachState.Failed, world.Cell(0x860201AF));
        Assert.Equal([], world.VisibleCells(0x860201AF));
        Assert.Equal(AttachState.Pending, world.Terrain(LandblockCoord.FromId(Academy)));
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.Contains("0x860201AF", warning.Message, StringComparison.Ordinal);

        var readiness = DestinationReadiness.Evaluate(Academy, world, ReadinessPolicy.Default);
        Assert.True(readiness.IsReady, readiness.Describe());
        Assert.Equal(1, readiness.Failed);

        source.Cells[0x860202AD] = new EnvCellInfo([], SeenOutside: true);
        world.Focus(0x860202AD);
        Assert.Equal(AttachState.Attached, world.Terrain(LandblockCoord.FromId(Academy)));
    }

    [Fact]
    public void A_malformed_record_is_a_settled_failure()
    {
        var source = new TableSource { Malformed = true };
        var log = new RecordingLogSink();
        var world = new DataOnlyAttachments(source, log);
        world.Focus(0xA9B40019);
        Assert.Equal(AttachState.Failed, world.Terrain(LandblockCoord.FromId(0xA9B40019)));
        Assert.True(DestinationReadiness.Evaluate(0xA9B40019, world, ReadinessPolicy.Default).IsReady);
        Assert.All(log.Entries, e => Assert.Contains("malformed", e.Message, StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void The_training_academy_is_ready_from_the_dats()
    {
        var log = new RecordingLogSink();
        var world = new DataOnlyAttachments(fixture.Dats, log);
        world.Focus(Academy);
        var readiness = DestinationReadiness.Evaluate(Academy, world, ReadinessPolicy.Default);
        Assert.True(readiness.IsReady, readiness.Describe());
        Assert.Equal(0, readiness.Failed);
        Assert.NotEmpty(world.VisibleCells(Academy)!);
        Assert.Empty(log.Entries);
    }

    /// <summary>A cell source from a table; every landblock exists unless <see cref="Malformed"/>.</summary>
    private sealed class TableSource : ICellSource
    {
        public Dictionary<uint, EnvCellInfo?> Cells { get; } = [];

        public bool Malformed { get; init; }

        public int Reads { get; private set; }

        public bool HasLandblock(LandblockCoord landblock)
        {
            Reads++;
            return Malformed ? throw new InvalidDataException("a malformed landblock") : true;
        }

        public EnvCellInfo? EnvCell(uint cellId)
        {
            Reads++;
            return Cells.TryGetValue(cellId, out var info) ? info : null;
        }
    }
}

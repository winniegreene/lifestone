using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Play;

/// <summary>D26 (the character drawn between the 30 Hz steps), the dry-run movement sink and the walking options of the offline player.</summary>
public sealed class DrawInterpolationTests
{
    private const uint Cell = 0x40400001;

    private static CellFrame At(float x, float y, float heading = 0f, uint cell = Cell) =>
        new(cell, new AcFrame(new Vector3(x, y, 10f), PositionFrames.WithHeading(Quaternion.Identity, heading)));

    [Fact]
    public void Between_two_steps_the_character_is_drawn_part_of_the_way()
    {
        // Steps at 1.000 and 1.034 s; 17 ms after the last one the character is half way from the earlier position.
        var drawn = DrawInterpolation.Interpolate(At(0f, 0f), At(0.6f, 0f), 1.0, 1.034, 1.034 + 0.017, stepping: false);

        Assert.Equal(0.3f, drawn.Origin.X, 2);
        Assert.Equal(10f, drawn.Origin.Z, 3);
        Assert.Equal(Cell, drawn.CellId);
    }

    [Fact]
    public void It_is_continuous_at_a_step_and_never_runs_ahead_of_the_last_one()
    {
        var previous = At(0f, 0f);
        var current = At(0.6f, 0f);

        Assert.Equal(0f, DrawInterpolation.Interpolate(previous, current, 1.0, 1.034, 1.034, stepping: false).Origin.X, 4); // the moment of the step: the position before it
        Assert.Equal(0.6f, DrawInterpolation.Interpolate(previous, current, 1.0, 1.034, 1.034 + 0.034, stepping: false).Origin.X, 4); // a step later: the position
        Assert.Equal(0.6f, DrawInterpolation.Interpolate(previous, current, 1.0, 1.034, 5.0, stepping: false).Origin.X, 4); // a stall does not extrapolate
        Assert.Equal(0f, DrawInterpolation.Interpolate(previous, current, 1.0, 1.034, 0.5, stepping: false).Origin.X, 4); // nor go back
    }

    [Fact]
    public void Retail_stepping_and_missing_data_draw_the_step_position()
    {
        var previous = At(0f, 0f);
        var current = At(0.6f, 0f);

        Assert.Equal(current, DrawInterpolation.Interpolate(previous, current, 1.0, 1.034, 1.04, stepping: true));
        Assert.Equal(current, DrawInterpolation.Interpolate(previous, current, double.NaN, 1.034, 1.04, stepping: false));
        Assert.Equal(current, DrawInterpolation.Interpolate(previous, current, 1.0, double.NaN, 1.04, stepping: false));
        Assert.Equal(current, DrawInterpolation.Interpolate(default, current, 1.0, 1.034, 1.04, stepping: false));
        Assert.Equal(current, DrawInterpolation.Interpolate(previous, current, 1.0, 1.0, 1.04, stepping: false));
    }

    [Fact]
    public void A_jump_in_position_is_not_smoothed()
    {
        var drawn = DrawInterpolation.Interpolate(At(0f, 0f), At(50f, 0f), 1.0, 1.034, 1.05, stepping: false);

        Assert.Equal(50f, drawn.Origin.X);
    }

    [Fact]
    public void The_turn_is_interpolated_along_the_short_way()
    {
        var drawn = DrawInterpolation.Interpolate(At(0f, 0f, 350f), At(0f, 0f, 10f), 1.0, 1.034, 1.051, stepping: false);

        Assert.Equal(0f, PositionFrames.Heading(drawn.Orientation) is var h && h > 180f ? h - 360f : h, 0);
    }

    [Fact]
    public void A_step_into_the_next_landblock_is_interpolated_across_the_border()
    {
        var east = Cell + 0x01000000; // landblock 0x41, 0x40
        var drawn = DrawInterpolation.Interpolate(At(191.8f, 5f), At(0.2f, 5f, cell: east), 1.0, 1.034, 1.051, stepping: false);

        Assert.Equal(east, drawn.CellId);
        Assert.Equal(-0.0f, drawn.Origin.X, 1); // half way over the 0.4 m: at the border, in the space of the new landblock
    }

    [Fact]
    public void The_offset_moves_a_part_with_the_body_and_turns_it_about_the_bodys_origin()
    {
        var stepped = At(100f, 100f, heading: 0f);
        var drawn = At(100.5f, 100f, heading: 90f);
        var offset = DrawInterpolation.Offset.Between(stepped, drawn);
        var bodyOrigin = stepped.Origin;

        // A part 1 m in front of the body (north) ends up 1 m east of the drawn body, which stands 0.5 m east.
        var part = new AcFrame(bodyOrigin + new Vector3(0f, 1f, 0f), Quaternion.Identity);
        var moved = offset.Apply(part, bodyOrigin);

        Assert.Equal(101.5f, moved.Origin.X, 3);
        Assert.Equal(100f, moved.Origin.Y, 3);
        Assert.Equal(90f, PositionFrames.Heading(moved.Orientation), 1);
        Assert.True(DrawInterpolation.Offset.Between(stepped, stepped).IsNone);
        Assert.Equal(part, DrawInterpolation.Offset.None.Apply(part, bodyOrigin));
    }

    [Fact]
    public void The_dry_run_sink_counts_and_logs_what_would_be_sent()
    {
        var log = new Lines();
        var sink = new DryRunMovementSink(log);
        var position = At(1f, 2f);

        sink.MoveToState(new RawMotionState(), position, contact: true, standingLongJump: false);
        sink.AutonomousPosition(position, contact: true);
        sink.AutonomousPosition(position, contact: true);
        sink.Jump(0.5f, new Vector3(0f, 1f, 2f), position);

        Assert.Equal((1L, 2L, 1L), (sink.MoveToStateCount, sink.AutonomousPositionCount, sink.JumpCount));
        Assert.Equal(4, log.Entries.Count);
        Assert.All(log.Entries, e => Assert.StartsWith("movement: dry run: ", e, StringComparison.Ordinal));
        Assert.Contains("MoveToState (not sent)", log.Entries[0]);
        Assert.Contains("AutonomousPosition (not sent)", log.Entries[1]);
        Assert.Contains("Jump (not sent)", log.Entries[3]);
        Assert.StartsWith("Jump extent 0.5", sink.Last);
    }

    [Fact]
    public void The_walking_options_default_off_and_parse_when_given()
    {
        var defaults = AvatarsOptions.Parse(["--offline-player"]);
        Assert.Equal((PlayerCameraMode.Chase, false, false, false), (defaults.Camera, defaults.PhysicsOverlay, defaults.RetailStepping, defaults.RetailRunRateCap));

        var all = AvatarsOptions.Parse(["--offline-player", "--player-camera=first", "--physics-overlay", "--motion-stepping=retail", "--retail-run-rate-cap"]);
        Assert.Equal((PlayerCameraMode.First, true, true, true), (all.Camera, all.PhysicsOverlay, all.RetailStepping, all.RetailRunRateCap));
        Assert.Empty(all.Errors);

        // The check suite and the frames run imply the player; the frames folder defaults to the gitignored review folder.
        var checks = AvatarsOptions.Parse(["--offline-player-checks"]);
        Assert.True(checks.OfflinePlayer);
        Assert.True(checks.WalkChecks);
        Assert.Null(checks.FramesDirectory);
        var frames = AvatarsOptions.Parse(["--offline-player-frames"]);
        Assert.True(frames.OfflinePlayer);
        Assert.Equal(AvatarsOptions.DefaultFramesDirectory, frames.FramesDirectory);
        Assert.Equal("somewhere", AvatarsOptions.Parse(["--offline-player-frames=somewhere"]).FramesDirectory);
        Assert.False(AvatarsOptions.Parse(["--offline-player"]).WalkChecks);

        var bad = AvatarsOptions.Parse(["--player-camera=sideways", "--motion-stepping=smooth"]);
        Assert.Equal(2, bad.Errors.Count);
        Assert.Equal(PlayerCameraMode.Chase, bad.Camera);
        Assert.False(bad.RetailStepping);
    }

    [Fact]
    public void The_walking_options_are_registered_in_the_catalog_once()
    {
        var names = OptionCatalog.Avatars().Options.Select(o => o.Name).ToArray();

        foreach (var name in new[] { "--offline-player", "--player-camera", "--physics-overlay", "--motion-stepping", "--retail-run-rate-cap", "--offline-player-frames" })
        {
            Assert.Single(names, name);
        }

        Assert.DoesNotContain("--camera", names);
    }

    private sealed class Lines : ILogSink
    {
        public List<string> Entries { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Entries.Add($"{category}: {message}{(fields.Length > 0 ? " " + fields[0].Value : string.Empty)}");
    }
}

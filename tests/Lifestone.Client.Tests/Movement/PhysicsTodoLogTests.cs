using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement;

/// <summary>The named physics TODOs the controller logs once (wave 3 lane C item 1; docs/physics.md section 15).</summary>
public sealed class PhysicsTodoLogTests
{
    [Fact]
    public void The_first_landing_logs_the_landing_velocity_todo_once()
    {
        var world = WorldOf(Flat(Block));
        var player = Player(world, At(Block, 96f, 96f, 0f), new TestPlayerWeenie { JumpVelocity = 4f });
        var log = new RecordingLogSink();
        var controller = new PlayerMovementController(player, new RecordingMovementSink(), MovementOptions.Default, log);
        var now = Run(controller, 0.5);
        Assert.DoesNotContain(log.Lines, line => line.Contains("physics-landing-velocity", StringComparison.Ordinal));

        for (var jumps = 0; jumps < 2; jumps++)
        {
            controller.CommenceJump(now);
            now = Run(controller, 0.3);
            controller.DoJump(now);
            Assert.False(controller.OnGround);
            now = Run(controller, 2.0);
            Assert.True(controller.OnGround, "the jump should have landed");
        }

        Assert.Single(log.Lines, line => line.Contains("TODO(physics-landing-velocity)", StringComparison.Ordinal));
    }
}

using Lifestone.Net.Session;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The silence watchdog (owner decision D18; retail <c>ClientNet::ProcessConnection</c>): a server that goes quiet for
/// <see cref="SessionTimeouts.Silence"/> (20 s, retail 140 s) fails the session, but only while the session is being
/// pumped, so a stalled game thread never looks like a dead server.
/// </summary>
public sealed class WatchdogTests
{
    [Fact]
    public void A_killed_server_fails_the_session_twenty_seconds_after_its_last_packet()
    {
        using var rig = new SessionRig();
        var lastArrival = TimeSpan.Zero;
        var received = 0L;
        rig.Harness.AddParticipant(() =>
        {
            if (rig.Session.Link.PacketsReceived != received)
            {
                received = rig.Session.Link.PacketsReceived;
                lastArrival = rig.Harness.Time.Elapsed;
            }
        });
        rig.ToCharacterSelect();
        rig.RunFor(TimeSpan.FromSeconds(3));

        rig.Harness.ServerAlive = false;
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(25)));

        Assert.Equal(SessionFailureKind.Silence, rig.Session.Failure!.Kind);
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Failure.Phase);
        Assert.InRange(rig.Harness.Time.Elapsed - lastArrival, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20.02));
    }

    [Fact]
    public void Retail_timeouts_wait_140_seconds()
    {
        using var rig = new SessionRig(configure: o => o with { Timeouts = SessionTimeouts.Retail });
        rig.ToCharacterSelect();
        var killedAt = rig.Harness.Time.Elapsed;
        rig.Harness.ServerAlive = false;

        rig.RunFor(TimeSpan.FromSeconds(130));
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(15)));
        Assert.Equal(SessionFailureKind.Silence, rig.Session.Failure!.Kind);
        Assert.InRange(rig.Harness.Time.Elapsed - killedAt, TimeSpan.FromSeconds(138), TimeSpan.FromSeconds(140.1));
    }

    [Fact]
    public void A_thirty_second_stall_with_server_packets_queued_does_not_fire()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var before = rig.Session.Link.PacketsReceived;

        rig.Pumping = false;
        rig.RunFor(TimeSpan.FromSeconds(30));
        rig.Pumping = true;
        rig.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
        Assert.True(rig.Session.Link.PacketsReceived - before >= 14, $"only {rig.Session.Link.PacketsReceived - before} packets came in");
        Assert.Null(rig.ServerSession.TerminationReason);
    }

    [Fact]
    public void A_server_that_died_during_a_stall_is_noticed_on_the_pump_after_the_stall()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Pumping = false;
        rig.Harness.ServerAlive = false;
        rig.RunFor(TimeSpan.FromSeconds(30));

        // The first pump after the stall does not judge the silence (it did not run within the window); the next does.
        rig.Pumping = true;
        rig.Harness.Step();
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
        rig.Harness.Step();
        Assert.Equal(SessionPhase.Failed, rig.Session.Phase);
        Assert.Equal(SessionFailureKind.Silence, rig.Session.Failure!.Kind);
    }
}

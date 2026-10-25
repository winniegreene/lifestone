using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 P5b fix FA, P5b-R1: the journey's headless body (<see cref="HeadlessJourneyRunnerHost"/>'s <see cref="HeadlessBody"/>, which the hunt sees through the host's follower) follows the server's
/// MoveToObject as the hunt's own <see cref="HuntFollow"/> does: ACE's approach (and its walk-to-use) moves the client's body, and the body's positions tell ACE where it is.
/// </summary>
public sealed partial class HuntScenarioTests
{
    [RequiresDatsFact]
    public void The_journeys_headless_body_follows_the_servers_MoveToObject_to_a_target_thirty_metres_away_FA()
    {
        var dats = datsFixture.Dats;
        ClientToScript? sink = null;
        HeadlessJourneyRunnerHost? host = null;
        using var rig = HuntScriptRig.Create(log =>
        {
            host = new HeadlessJourneyRunnerHost(dats, log, _ => sink!);
            return new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Melee, Kills = 1, Seconds = 600 }, log, cell => HuntGround.TerrainHeight(dats, cell), endSession: false, follow: new FaSettlingFollow(host, host.HuntFollow(null!)));
        });
        using var disposeHost = host;
        sink = new ClientToScript(rig);
        rig.ApproachSendsPositions = false;
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 30);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(host!.Body.ServerMovesFollowed >= 1, "the body was given the MoveToObject: " + host.Body.ServerMovesFollowed + rig.Tail());
        Assert.True(rig.Logged("the server moves the player: the journey's body follows"), rig.Tail());
        Assert.True(sink.Positions > 10, "the body reported its positions: " + sink.Positions);
        Assert.False(rig.Logged("did not close in"), "no target excluded for a walk that did not close");
    }

    /// <summary>
    /// The host's follower with the headless streamer's workers waited for in real time while the body waits for the collision round the player (the rig's clock is simulated, the workers are not:
    /// as <c>J4JourneyHostTests</c>' runs sleep while the host is busy); nothing else of the follower changes.
    /// </summary>
    private sealed class FaSettlingFollow(HeadlessJourneyRunnerHost host, IHuntFollow inner) : IHuntFollow
    {
        public string? Failure => inner.Failure;

        public CellFrame? Position => inner.Position;

        public void Prepare(IGameSessionView session, double now) => inner.Prepare(session, now);

        public void Tick(IGameSessionView session, double now)
        {
            inner.Tick(session, now);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (host.PlaceBlocker?.StartsWith("collision-not-settled", StringComparison.Ordinal) == true && session.State.Self.Position is { } at && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
                host.Body.Streamer!.Pump();
                if (host.Body.Settled(at))
                {
                    break;
                }
            }
        }

        public void Dispose() => inner.Dispose();
    }
}

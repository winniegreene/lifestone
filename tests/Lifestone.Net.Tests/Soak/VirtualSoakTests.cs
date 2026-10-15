using System.Numerics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Soak;

/// <summary>
/// The lossy soak in virtual time (m2-plan §3.3 N8): a session in the fake server's world walks for two minutes, sending
/// an AutonomousPosition every 200 ms and a MoveToState whenever its motion changes, over a link that drops 5% and swaps
/// 2% of the datagrams each way (<see cref="LossyDatagramSocket"/>, armed after the handshake). Afterwards:
/// <list type="bullet">
/// <item>the session never failed and the fake never terminated it;</item>
/// <item>the fake handled every client message, in the order the client sent them (ACE handles messages strictly in
/// fragment order, so one lost for good would stall every later one);</item>
/// <item>the client counted no checksum failure and skipped no gap;</item>
/// <item>the fake's CRC error count equals the duplicate encrypted deliveries the lossy link made (the oracle N12 uses
/// against the real ACE).</item>
/// </list>
/// Five seeds by default; ten minutes over 100 seeds at <c>Tier=Soak</c>.
/// </summary>
public sealed class VirtualSoakTests
{
    private static readonly TimeSpan PositionInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MotionInterval = TimeSpan.FromMilliseconds(1_700);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    public static TheoryData<int> DefaultSeeds => [1, 2, 3, 4, 5];

    [Theory]
    [MemberData(nameof(DefaultSeeds))]
    public void Two_minutes_at_five_percent_loss_and_two_percent_reordering(int seed) =>
        Soak(seed, TimeSpan.FromMinutes(2));

    [Fact]
    [Trait("Tier", "Soak")]
    public void Ten_minutes_over_a_hundred_seeds()
    {
        for (var seed = 100; seed < 200; seed++)
        {
            Soak(seed, TimeSpan.FromMinutes(10));
        }
    }

    [Fact]
    public void A_drop_only_link_makes_no_duplicate_deliveries_unless_a_resend_crosses_its_original()
    {
        // With drops alone a packet is resent only when the server NAKed it or the probe found it unacked, both after it
        // was lost, so ACE sees no duplicate and counts no CRC error.
        var run = Soak(7, TimeSpan.FromMinutes(1), swapPercent: 0);
        Assert.Equal(0, run.DuplicateDeliveries);
        Assert.Equal(0, run.CrcErrors);
    }

    /// <summary>Runs one soak and checks the four outcomes; returns the oracle's two counts.</summary>
    private static SoakRun Soak(int seed, TimeSpan duration, double dropPercent = 5, double swapPercent = 2)
    {
        var clientToServer = new LossModel(seed, dropPercent, swapPercent, duplicatePercent: 0);
        var serverToClient = new LossModel(seed ^ 0x5A5A5A, dropPercent, swapPercent, duplicatePercent: 0);
        LossyDatagramSocket? lossy = null;
        using var rig = new SessionRig(configure: options =>
        {
            var inner = options.SocketFactory!;
            return options with
            {
                SocketFactory = server => lossy = new LossyDatagramSocket(inner(server), options.Time, clientToServer, serverToClient),
            };
        });
        rig.Harness.Network.RecordSent = false;

        // The link is lossy from the first encrypted send (the data check) on, so the way in can take a few probes.
        rig.Login();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(60)),
            $"seed {seed}: stuck in {rig.Session.Phase} ({rig.Session.Failure}) before character select");
        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(60)),
            $"seed {seed}: stuck in {rig.Session.Phase} ({rig.Session.Failure}) entering the world");
        rig.Session.SendLoginComplete();

        var time = rig.Harness.Time;
        var start = time.Elapsed;
        var nextPosition = start;
        var nextMotion = start + MotionInterval;
        var running = false;
        var x = 84f;
        ushort instance = 1;
        while (time.Elapsed - start < duration && rig.Session.Phase == SessionPhase.InWorld)
        {
            rig.Harness.Step();
            if (time.Elapsed >= nextMotion)
            {
                running = !running;
                var motion = running
                    ? RawMotionStateWire.Default with { CurrentHoldKey = 2, ForwardCommand = 0x45000005, ForwardHoldKey = 2 }
                    : RawMotionStateWire.Default;
                rig.Session.SendMoveToState(motion, Spot(x), new ObjSeqs(instance, 0, 0, 0), contact: true, standingLongJump: false);
                nextMotion += MotionInterval;
            }

            if (time.Elapsed >= nextPosition)
            {
                x = running ? (x + 0.8f) % 190f : x;
                rig.Session.SendAutonomousPosition(Spot(x), new ObjSeqs(instance, 0, 0, 0), contact: true);
                nextPosition += PositionInterval;
            }
        }

        // Let the last packets through: acks, NAKs and the tail probe deliver what the link lost at the end.
        rig.RunFor(Settle);

        var context = $"seed {seed}: phase {rig.Session.Phase}, failure {rig.Session.Failure}, link {rig.Session.Link}, "
            + $"c2s {Describe(clientToServer)}, s2c {Describe(serverToClient)}, fake {Describe(rig)}";
        Assert.True(rig.Session.Phase == SessionPhase.InWorld && rig.Session.Failure is null, context);
        Assert.True(rig.Server.History.Count == 1 && rig.ServerSession.TerminationReason is null, context);

        var sent = rig.Tap.Messages.Where(m => m.Direction == MessageDirection.Outbound).Select(m => m.Body).ToList();
        var handled = rig.ServerSession.HandledMessages;
        Assert.True(sent.Count == handled.Count, $"{context}: sent {sent.Count} messages, the fake handled {handled.Count}");
        for (var i = 0; i < sent.Count; i++)
        {
            Assert.True(sent[i].AsSpan().SequenceEqual(handled[i]), $"{context}: message {i} differs");
        }

        Assert.True(rig.Session.Link.ChecksumFailures == 0, $"{context}: client checksum failures");
        Assert.True(rig.Session.Link.GapSkips == 0, $"{context}: client gap skips");
        Assert.True(rig.Server.Counters.C2SCrcErrors == clientToServer.DuplicateEncryptedDeliveries,
            $"{context}: the fake counted {rig.Server.Counters.C2SCrcErrors} CRC errors, the link made {clientToServer.DuplicateEncryptedDeliveries} duplicate deliveries");
        Assert.True(lossy is { Armed: true }, context);
        Assert.True(clientToServer.Dropped > 0 && serverToClient.Dropped > 0, context);
        return new SoakRun(clientToServer.DuplicateEncryptedDeliveries, rig.Server.Counters.C2SCrcErrors);
    }

    private static AcPosition Spot(float x) =>
        new(0xA9B40019, new Vector3(x, 7.1f, 94f), new Quaternion(0f, 0f, -0.0784591f, 0.996917f));

    private static string Describe(LossModel model) =>
        $"{model.Submitted} submitted, {model.Dropped} dropped, {model.Swapped} swapped, {model.DuplicateEncryptedDeliveries} duplicate deliveries";

    private static string Describe(SessionRig rig)
    {
        var c = rig.Server.Counters;
        return $"{c.C2SPackets} packets, {c.C2SCrcErrors} CRC errors, {c.S2CRequestsForRetransmit} NAKs sent, "
            + $"{c.C2SRequestsForRetransmit} NAKs received, {c.S2CRetransmits} resends, {c.S2CRejects} rejects, "
            + $"{c.MessagesHandled} handled, last packet {rig.ServerSession.LastReceivedPacketSequence}, "
            + $"last message {rig.ServerSession.LastReceivedFragmentSequence}, sessions {rig.Server.History.Count}, "
            + $"termination {rig.ServerSession.TerminationReason?.ToString() ?? "none"}, slot taken {c.OutOfOrderSlotTaken}, "
            + $"data lost behind an ack-only {c.DataLostBehindAckOnly}";
    }

    private sealed record SoakRun(long DuplicateDeliveries, long CrcErrors);
}

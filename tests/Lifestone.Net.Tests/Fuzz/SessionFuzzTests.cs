using System.Net;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Session;

namespace Lifestone.Net.Tests.Fuzz;

/// <summary>
/// The session under fuzz (m2-plan §3.3 N8): as it first reaches each phase from login to the return to character
/// select, 10,000 datagrams arrive from the server's two ports: random bytes, truncations, single-bit flips and exact
/// replays of the server's own earlier datagrams. <see cref="IAcSession.Pump"/> never throws, and the session takes no
/// notice: it completes the whole flow without a failure, every garbage datagram ending as a parse refusal, a checksum
/// failure or a dropped duplicate (N8's <c>InboundSequenceTracker.Reject</c> keeps a corrupt sequence from moving the
/// window).
/// </summary>
/// <remarks>
/// Forged cleartext packets with a valid checksum (an ack, a NAK, a reject) are out of scope: ACE's protocol does not
/// authenticate them, retail trusts them too, and the socket already drops anything that is not from the server's
/// address and ports.
/// </remarks>
public sealed class SessionFuzzTests
{
    private const int PerPhase = 10_000;

    private static readonly SessionPhase[] FuzzedPhases =
    [
        SessionPhase.Connecting, SessionPhase.Handshaking, SessionPhase.DataCheck, SessionPhase.CharacterSelect,
        SessionPhase.EnteringWorld, SessionPhase.InWorld, SessionPhase.LoggingOff,
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Garbage_in_every_phase_changes_nothing(int seed)
    {
        using var rig = new SessionRig();
        var network = rig.Harness.Network;
        network.RecordSent = false;
        var random = new Random(seed);

        // The server's real datagrams to this client, to truncate, flip and replay.
        var injecting = false;
        var corpus = new List<(IPEndPoint From, byte[] Datagram)>();
        network.Drop = (from, to, datagram) =>
        {
            if (!injecting && to.Port != rig.Harness.ServerEndPoint.Port)
            {
                corpus.Add((from, datagram));
            }

            return false;
        };

        var fuzzed = new HashSet<SessionPhase>();
        var injected = 0;
        void Inject()
        {
            var phase = rig.Session.Phase;
            if (!FuzzedPhases.Contains(phase) || rig.Machine.Connection is not { } connection || !fuzzed.Add(phase))
            {
                return;
            }

            injecting = true;
            var client = connection.Socket.LocalEndPoint;
            for (var i = 0; i < PerPhase; i++)
            {
                var (from, garbage) = Garbage(random, corpus, rig.Harness.ServerEndPoint);
                network.Send(from, client, garbage);
                injected++;
            }

            injecting = false;
        }

        // After every pump; and once right after Login, since the fake answers within the first step.
        rig.Harness.AddParticipant(Inject);
        rig.Login();
        Inject();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(15)),
            $"stuck in {rig.Session.Phase} ({rig.Session.Failure})");
        rig.Session.EnterWorld(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(15)),
            $"stuck in {rig.Session.Phase} ({rig.Session.Failure})");
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromSeconds(3));
        rig.Session.SendTalk("still here");
        rig.RunFor(TimeSpan.FromSeconds(2));
        rig.Session.LogOff();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(15)),
            $"stuck in {rig.Session.Phase} ({rig.Session.Failure})");

        Assert.True(fuzzed.SetEquals(FuzzedPhases), "fuzzed " + string.Join(", ", fuzzed));
        Assert.Equal(FuzzedPhases.Length * PerPhase, injected);
        Assert.Null(rig.Session.Failure);
        Assert.Contains("still here", rig.Sink.Speech.Select(s => s.Text));
        Assert.False(rig.ServerSession.Teleporting, "LoginComplete never reached the fake");
        Assert.True(rig.Session.Link.ChecksumFailures > 0, "no garbage got as far as the checksum");
        Assert.Equal(0, rig.Session.Link.GapSkips);
    }

    /// <summary>One garbage datagram from the login or world port: random, or a truncated, flipped or replayed real one.</summary>
    private static (IPEndPoint From, byte[] Datagram) Garbage(Random random, List<(IPEndPoint From, byte[] Datagram)> corpus, IPEndPoint login)
    {
        var port = random.Next(2) == 0 ? login : new IPEndPoint(login.Address, login.Port + 1);
        var kind = corpus.Count == 0 ? 0 : random.Next(4);
        if (kind == 0)
        {
            var bytes = new byte[random.Next(600)];
            random.NextBytes(bytes);
            return (port, bytes);
        }

        var (from, original) = corpus[random.Next(corpus.Count)];
        switch (kind)
        {
            case 1:
                return (from, original[..random.Next(original.Length)]);
            case 2:
                var flipped = (byte[])original.Clone();
                var bit = random.Next(flipped.Length * 8);
                flipped[bit >> 3] ^= (byte)(1 << (bit & 7));
                return (from, flipped);
            default:
                return (from, original);
        }
    }
}

using System.Buffers.Binary;
using Lifestone.Net.Crypto;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Transport;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// The vendored OpenAC transport and the fake server, written independently, agree on the wire: every server packet
/// verifies with the transport's own key tracking, multi-fragment messages cross both ways (including ACE's tail-first
/// order), and the transport's packets and acks keep the fake's session alive. N5 builds the session on this.
/// </summary>
public sealed class FakeAceInteropTests
{
    private const string Account = "tester";
    private const string Password = "test-password-1";

    [Fact]
    public void The_transport_reads_every_fake_packet_and_the_fake_reads_the_transports()
    {
        var script = new FakeAceScript();
        var account = script.AddAccount(Account, Password);
        for (var i = 0; i < 30; i++)
        {
            // A character list larger than one fragment (448 bytes), so the server splits it.
            account.Characters.Add(new FakeCharacter(0x50000001u + (uint)i, "Character With A Long Name " + i) { LastLogin = DateTimeOffset.UnixEpoch.AddDays(i) });
        }

        var harness = new SessionHarness(script);
        using var raw = new RawAceClient(harness);
        raw.SendLoginRequest(Account, Password);
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        raw.Drain();
        var request = raw.ConnectRequestPacket!;
        raw.SendConnectResponse();

        using var link = new TransportLink(harness, raw, request);
        harness.AddParticipant(link.Pump);

        Assert.True(harness.RunUntil(() => link.Opcodes.Contains(MessageOpcodes.CharacterList), TimeSpan.FromSeconds(2)));
        Assert.True(CharacterListCodec.TryParse(link.Messages.Single(m => Opcode(m) == MessageOpcodes.CharacterList), out var list));
        Assert.Equal(30, list.Characters.Length);
        Assert.Equal("Character With A Long Name 29", list.Characters[0].Name);  // most recent login first, as ACE sorts

        var writer = new PacketWriter();
        DddCodec.WriteInterrogationResponse(writer, new DddIterations(2072, 982, 994, null), serverAllowsHighRes: false);
        link.Transport.Outbound.SendGameMessage(writer.AsSpan(), MessageQueue.Database);
        Assert.True(harness.RunUntil(() => link.Opcodes.Contains(MessageOpcodes.DddEnd), TimeSpan.FromSeconds(2)));

        // A delete: the small acknowledgement and the large list share one UI bundle, so ACE sends the list's tail
        // fragment in the first packet, before the fragments it follows.
        writer = new PacketWriter();
        CharacterDeleteCodec.Write(writer, Account, 0);
        link.Transport.Outbound.SendGameMessage(writer.AsSpan(), MessageQueue.Ui);
        Assert.True(harness.RunUntil(() => link.Opcodes.Count(o => o == MessageOpcodes.CharacterList) == 2, TimeSpan.FromSeconds(2)));
        Assert.Equal(MessageOpcodes.CharacterDelete, link.Opcodes[^2]);
        var listSequence = link.Arrivals.Last(a => a.Count > 1).Sequence;
        var order = link.Arrivals.Where(a => a.Sequence == listSequence).Select(a => a.Index).ToList();
        Assert.Equal(order.Max(), order[0]);

        // A client message of three fragments.
        var big = new byte[1000];
        BinaryPrimitives.WriteUInt32LittleEndian(big, 0x1234);
        link.Transport.Outbound.SendGameMessage(big, MessageQueue.Ui);
        harness.RunFor(TimeSpan.FromMilliseconds(50));

        var session = harness.Server.Sessions.Single();
        Assert.Equal(3u, session.LastReceivedFragmentSequence);
        Assert.Equal(1, harness.Server.Counters.UnhandledMessages);  // 0x1234 has no handler, but it was reassembled whole
        Assert.Equal(0, harness.Server.Counters.C2SCrcErrors);

        // A minute of idling: the transport's acks keep the session alive, and the fake's acks and time syncs verify.
        harness.RunFor(TimeSpan.FromSeconds(65));
        Assert.Null(session.TerminationReason);
        Assert.Equal(0, link.ChecksumFailures);
        Assert.True(link.Verified > 10, $"only {link.Verified} server packets verified");
        Assert.Equal(0, harness.Server.Counters.C2SCrcErrors);
    }

    private static uint Opcode(byte[] message) => BinaryPrimitives.ReadUInt32LittleEndian(message);

    /// <summary>The product transport on the raw client's socket, with a receive loop like the session's.</summary>
    private sealed class TransportLink : IDisposable
    {
        private readonly RawAceClient _raw;
        private readonly FragmentAssembler _assembler;
        private readonly byte[] _buffer = new byte[2048];

        public TransportLink(SessionHarness harness, RawAceClient raw, ServerDatagram request)
        {
            _raw = raw;
            _assembler = new FragmentAssembler(harness.Time);
            Transport = new ReliableTransport(
                new IsaacRandom(BitConverter.GetBytes(request.ConnectRequestClientSeed)),
                new IsaacRandom(BitConverter.GetBytes(request.ConnectRequestServerSeed)),
                raw.ClientId,
                sessionIteration: 1,
                d => raw.Socket.Send(raw.Socket.Server.Port, d),
                new TransportClock(harness.Time),
                assembler: _assembler);
        }

        public ReliableTransport Transport { get; }

        public List<byte[]> Messages { get; } = [];

        public List<uint> Opcodes { get; } = [];

        /// <summary>Every server fragment in arrival order: its message sequence, index and count.</summary>
        public List<(uint Sequence, ushort Index, ushort Count)> Arrivals { get; } = [];

        public int Verified { get; private set; }

        public int ChecksumFailures { get; private set; }

        public void Pump()
        {
            while (_raw.Socket.TryReceive(_buffer, out var length, out _))
            {
                var datagram = _buffer.AsSpan(0, length).ToArray();
                Assert.True(PacketCodec.TryParseBorrowed(datagram, out var packet, out var headerHash, out var payloadHash, out var error), error.ToString());
                if (packet.Header.HasFlag(PacketHeaderFlags.ConnectRequest))
                {
                    continue;
                }

                var encrypted = packet.Header.HasFlag(PacketHeaderFlags.EncryptedChecksum);
                var admission = Transport.Inbound.Admit(packet.Header.Sequence, encrypted);
                if (admission.Drop)
                {
                    continue;
                }

                if (!PacketCodec.VerifyChecksum(packet.Header, headerHash, payloadHash, admission.VerifyKey))
                {
                    ChecksumFailures++;
                    continue;
                }

                Verified++;
                if (packet.Header.HasFlag(PacketHeaderFlags.AckSequence))
                {
                    Transport.Outbound.OnAckSequence(packet.Optional.AckSequence);
                }

                foreach (var fragment in packet.Fragments)
                {
                    Arrivals.Add((fragment.Header.Sequence, fragment.Header.Index, fragment.Header.Count));
                    if (_assembler.TryIngest(fragment, out var message, out _))
                    {
                        Messages.Add(message.ToArray());
                        Opcodes.Add(BinaryPrimitives.ReadUInt32LittleEndian(message.Span));
                    }
                }
            }

            Transport.Sweep();
        }

        public void Dispose() => Transport.Dispose();
    }
}

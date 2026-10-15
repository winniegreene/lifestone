using System.Numerics;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Fuzz;

/// <summary>
/// The borrowed datagram parser under fuzz (m2-plan §3.3 N8): random bytes, every truncation of real datagrams of both
/// directions (a corpus recorded from a whole session on the fake: login, character select, the world, logoff), and
/// seeded bit flips of them. <see cref="PacketCodec.TryParseBorrowed"/> never throws; a truncation never parses; a
/// datagram with one bit flipped never verifies with its own key (or none, for a cleartext one); random bytes never
/// verify. 100,000 iterations by default, 5,000,000 at <c>Tier=Soak</c>.
/// </summary>
/// <remarks>
/// Two or more flips can cancel in Hash32, which is a sum of words (the same bit set in one word and cleared in another),
/// so only single flips are required to fail the checksum; multi-bit flips are checked for exceptions only. On loopback
/// UDP's own checksum stands behind it.
/// </remarks>
public sealed class DatagramFuzzTests
{
    private const int DefaultIterations = 100_000;

    private const int SoakIterations = 5_000_000;

    /// <summary>Every datagram of a whole session, both directions, with the key each was sealed with (null for cleartext).</summary>
    private static readonly Lazy<IReadOnlyList<(byte[] Datagram, uint? Key)>> Corpus = new(RecordCorpus);

    [Fact]
    public void The_corpus_covers_both_directions_and_every_kind_of_packet()
    {
        var corpus = Corpus.Value;
        Assert.True(corpus.Count > 50, $"{corpus.Count} datagrams");
        var flags = corpus.Select(c => PacketHeader.Unpack(c.Datagram).Flags).ToList();
        Assert.Contains(flags, f => f.HasFlag(PacketHeaderFlags.LoginRequest));
        Assert.Contains(flags, f => f.HasFlag(PacketHeaderFlags.ConnectRequest));
        Assert.Contains(flags, f => f.HasFlag(PacketHeaderFlags.ConnectResponse));
        Assert.Contains(flags, f => f.HasFlag(PacketHeaderFlags.EncryptedChecksum) && f.HasFlag(PacketHeaderFlags.BlobFragments));
        Assert.Contains(flags, f => f == PacketHeaderFlags.AckSequence);
        Assert.Contains(flags, f => f.HasFlag(PacketHeaderFlags.TimeSync));
        Assert.Contains(corpus, c => c.Key is not null);
    }

    [Fact]
    public void Every_truncation_of_every_real_datagram_is_refused()
    {
        foreach (var (datagram, _) in Corpus.Value)
        {
            Assert.True(PacketCodec.TryParseBorrowed(datagram, out _, out _, out _, out _), "a corpus datagram did not parse");
            for (var length = 0; length < datagram.Length; length++)
            {
                Assert.False(PacketCodec.TryParseBorrowed(datagram.AsMemory(0, length), out _, out _, out _, out _),
                    $"a {length}-byte prefix of a {datagram.Length}-byte datagram parsed");
            }
        }
    }

    [Fact]
    public void Random_bytes_never_throw_and_never_verify() => RandomBytes(DefaultIterations, seed: 0x0D8A);

    [Fact]
    [Trait("Tier", "Soak")]
    public void Five_million_random_datagrams_never_throw_and_never_verify() => RandomBytes(SoakIterations, seed: 0x0D8A_50AC);

    [Fact]
    public void Seeded_bit_flips_never_throw_and_one_flip_never_verifies() => Flips(DefaultIterations, seed: 0x0F11);

    [Fact]
    [Trait("Tier", "Soak")]
    public void Five_million_bit_flips_never_throw_and_one_flip_never_verifies() => Flips(SoakIterations, seed: 0x0F11_50AC);

    private static void RandomBytes(int iterations, int seed)
    {
        var random = new Random(seed);
        var buffer = new byte[600];
        var parsed = 0;
        for (var i = 0; i < iterations; i++)
        {
            var length = random.Next(buffer.Length + 1);
            random.NextBytes(buffer.AsSpan(0, length));
            if (i % 2 == 1 && length >= PacketHeader.Size)
            {
                // Half of them get a body size that fits, so the optional and fragment parsers see the rest.
                BitConverter.TryWriteBytes(buffer.AsSpan(16), (ushort)random.Next(length - PacketHeader.Size + 1));
            }

            var memory = buffer.AsMemory(0, length);
            if (Parse(memory, i) is not { } p)
            {
                continue;
            }

            parsed++;
            if (PacketCodec.VerifyChecksum(p.Header, p.HeaderHash, p.PayloadHash, isaacKey: null))
            {
                Assert.Fail($"iteration {i}: random bytes verified: {Convert.ToHexString(memory.Span)}");
            }
        }

        Assert.True(parsed > 0, "no random datagram even parsed: the fuzz did not reach the optional and fragment parsers");
    }

    private static void Flips(int iterations, int seed)
    {
        var random = new Random(seed);
        var corpus = Corpus.Value;
        var parsedSingles = 0;
        for (var i = 0; i < iterations; i++)
        {
            var (original, key) = corpus[random.Next(corpus.Count)];
            var datagram = (byte[])original.Clone();
            var flips = i % 2 == 0 ? 1 : 2 + random.Next(3);
            for (var f = 0; f < flips; f++)
            {
                var bit = random.Next(datagram.Length * 8);
                datagram[bit >> 3] ^= (byte)(1 << (bit & 7));
            }

            if (Parse(datagram, i) is not { } p || flips != 1)
            {
                continue;
            }

            parsedSingles++;
            if (PacketCodec.VerifyChecksum(p.Header, p.HeaderHash, p.PayloadHash, key))
            {
                Assert.Fail($"iteration {i}: one flipped bit still verified: {Convert.ToHexString(datagram)}");
            }
        }

        Assert.True(parsedSingles > iterations / 10, $"only {parsedSingles} single flips parsed");
    }

    private static (PacketHeader Header, uint HeaderHash, uint PayloadHash)? Parse(ReadOnlyMemory<byte> datagram, int iteration)
    {
        try
        {
            return PacketCodec.TryParseBorrowed(datagram, out var packet, out var headerHash, out var payloadHash, out _)
                ? (packet.Header, headerHash, payloadHash)
                : null;
        }
        catch (Exception e)
        {
            Assert.Fail($"iteration {iteration}: {Convert.ToHexString(datagram.Span)} threw {e.GetType().Name}: {e.Message}");
            throw;
        }
    }

    /// <summary>A whole session on the fake, recorded off the in-memory network, each datagram with its sealing key.</summary>
    private static IReadOnlyList<(byte[] Datagram, uint? Key)> RecordCorpus()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        var spot = new AcPosition(0xA9B40019, new Vector3(84f, 7.1f, 94f), Quaternion.Identity);
        for (var i = 0; i < 5; i++)
        {
            rig.Session.SendAutonomousPosition(spot with { Origin = spot.Origin + new Vector3(i, 0, 0) }, new ObjSeqs(1, 0, 0, 0), contact: true);
            rig.Session.SendTalk("corpus " + i);
            rig.RunFor(TimeSpan.FromMilliseconds(500));
        }

        rig.RunFor(TimeSpan.FromSeconds(21));  // acks, a time sync
        rig.Session.LogOff();
        rig.RunFor(TimeSpan.FromSeconds(8));

        return [.. rig.Harness.Network.Sent.Select(s => (s.Datagram, KeyOf(s.Datagram)))];
    }

    /// <summary>The key an encrypted datagram was sealed with: (checksum - header hash) ^ payload hash, as ACE finds it.</summary>
    private static uint? KeyOf(byte[] datagram)
    {
        Assert.True(PacketCodec.TryParseBorrowed(datagram, out var packet, out var headerHash, out var payloadHash, out _));
        return packet.Header.HasFlag(PacketHeaderFlags.EncryptedChecksum)
            ? (packet.Header.Checksum - headerHash) ^ payloadHash
            : null;
    }
}

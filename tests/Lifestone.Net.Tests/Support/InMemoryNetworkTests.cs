using System.Buffers.Binary;
using System.Net;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// The test network the fake server runs on: client sockets that behave like <c>UdpLoopbackSocket</c> (two server
/// ports only, a filter on everything else, waits on the given clock), and a seeded loss model that replays exactly,
/// swaps by one place at most and counts duplicate encrypted deliveries.
/// </summary>
public sealed class InMemoryNetworkTests
{
    private static readonly IPEndPoint Server = new(IPAddress.Loopback, 9000);

    [Fact]
    public void A_client_socket_sends_to_the_two_server_ports_only_and_hears_only_them()
    {
        var time = new Lifestone.Tests.Shared.ManualTimeProvider();
        var network = new InMemoryNetwork(time);
        var login = network.BindServer(Server);
        var world = network.BindServer(new IPEndPoint(IPAddress.Loopback, 9001));
        var stranger = network.BindServer(new IPEndPoint(IPAddress.Loopback, 9005));
        using var socket = network.OpenClientSocket(Server);

        Assert.Equal(IPAddress.Loopback, socket.LocalEndPoint.Address);
        Assert.Equal(InMemoryNetwork.FirstEphemeralPort, socket.LocalEndPoint.Port);
        socket.Send(9000, [1]);
        socket.Send(9001, [2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => socket.Send(9002, [3]));
        Assert.True(login.TryDequeue(out var from, out var datagram));
        Assert.Equal(socket.LocalEndPoint, from);
        Assert.Equal(new byte[] { 1 }, datagram);
        Assert.True(world.TryDequeue(out _, out _));

        world.Send(socket.LocalEndPoint, [4, 5]);
        stranger.Send(socket.LocalEndPoint, [6]);
        Span<byte> buffer = stackalloc byte[8];
        Assert.True(socket.TryReceive(buffer, out var length, out var port));
        Assert.Equal((2, 9001), (length, port));
        Assert.False(socket.TryReceive(buffer, out _, out _));
        Assert.Equal(1, socket.DatagramsFiltered);

        socket.Dispose();
        Assert.Throws<ObjectDisposedException>(() => socket.Send(9000, [1]));
        login.Send(socket.LocalEndPoint, [7]);
        Assert.Equal(1, network.Undeliverable);
    }

    [Fact]
    public async Task Waiting_completes_on_arrival_or_times_out_on_the_virtual_clock()
    {
        var time = new Lifestone.Tests.Shared.ManualTimeProvider();
        var network = new InMemoryNetwork(time);
        var login = network.BindServer(Server);
        network.BindServer(new IPEndPoint(IPAddress.Loopback, 9001));
        using var socket = network.OpenClientSocket(Server);

        var waiting = socket.WaitForDatagramAsync(TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        Assert.False(waiting.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(await waiting);

        var arriving = socket.WaitForDatagramAsync(TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        login.Send(socket.LocalEndPoint, [1]);
        Assert.True(await arriving);
        Assert.True(await socket.WaitForDatagramAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public void The_loss_model_replays_exactly_from_its_seed()
    {
        static List<int> Run(int seed)
        {
            var time = new Lifestone.Tests.Shared.ManualTimeProvider();
            var model = new LossModel(seed, dropPercent: 10, swapPercent: 10, duplicatePercent: 10);
            var delivered = new List<int>();
            for (var i = 0; i < 500; i++)
            {
                model.Submit([(byte)i, (byte)(i >> 8)], d => delivered.Add(d[0] | (d[1] << 8)), time);
            }

            model.ReleaseHeld();
            return delivered;
        }

        Assert.Equal(Run(7), Run(7));
        Assert.NotEqual(Run(7), Run(8));
    }

    [Fact]
    public void A_swap_moves_a_datagram_by_one_place_and_a_lone_hold_is_released_after_20_ms()
    {
        var time = new Lifestone.Tests.Shared.ManualTimeProvider();
        var model = new LossModel(1, dropPercent: 0, swapPercent: 100, duplicatePercent: 0);
        var delivered = new List<byte>();

        model.Submit([1], d => delivered.Add(d[0]), time);  // held
        model.Submit([2], d => delivered.Add(d[0]), time);  // its successor: forwarded, then 1 follows
        Assert.Equal(new byte[] { 2, 1 }, delivered);

        model.Submit([3], d => delivered.Add(d[0]), time);  // held again; nothing follows
        time.Advance(TimeSpan.FromMilliseconds(19));
        Assert.Equal(2, delivered.Count);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(new byte[] { 2, 1, 3 }, delivered);
        Assert.Equal(2, model.Swapped);
    }

    [Fact]
    public void Drops_and_duplicates_follow_their_percentages()
    {
        var time = new Lifestone.Tests.Shared.ManualTimeProvider();
        var model = new LossModel(42, dropPercent: 5, swapPercent: 0, duplicatePercent: 2);
        var delivered = 0;
        for (var i = 0; i < 20_000; i++)
        {
            model.Submit([0], _ => delivered++, time);
        }

        Assert.InRange(model.Dropped, 800, 1200);
        Assert.InRange(model.Duplicated, 300, 460);
        Assert.Equal(20_000 - model.Dropped + model.Duplicated, delivered);
    }

    [Fact]
    public void Duplicate_deliveries_of_encrypted_client_packets_are_counted_by_sequence()
    {
        var time = new Lifestone.Tests.Shared.ManualTimeProvider();
        var model = new LossModel(3, dropPercent: 0, swapPercent: 0, duplicatePercent: 0);
        static byte[] Packet(uint sequence, uint flags)
        {
            var datagram = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(datagram, sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(4), flags);
            return datagram;
        }

        model.Submit(Packet(2, 0x6), _ => { }, time);
        model.Submit(Packet(3, 0x6), _ => { }, time);
        model.Submit(Packet(2, 0x7), _ => { }, time);     // a retransmission of 2 that also got through: ACE's CRC error
        model.Submit(Packet(3, 0x4000), _ => { }, time);  // cleartext ack-only at 3: not encrypted, not counted
        Assert.Equal(1, model.DuplicateEncryptedDeliveries);
    }
}

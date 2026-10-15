using System.Net;
using System.Net.Sockets;
using Lifestone.Net.Sockets;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Sockets;

/// <summary>
/// <see cref="UdpLoopbackSocket"/> (m2-plan §3.3 N2) against sockets the test opens itself on 127.0.0.1, standing in for
/// ACE's two ports; nothing else is contacted. It binds the loopback address (never every interface), sends only to the
/// server's Port and Port + 1, and keeps only datagrams from those two ports, counting the rest.
/// </summary>
public sealed class UdpLoopbackSocketTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void It_binds_the_loopback_address_on_a_port_the_system_assigns()
    {
        using var server = FakeServer.Open();
        using var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);

        Assert.Equal(IPAddress.Loopback, socket.LocalEndPoint.Address);
        Assert.NotEqual(0, socket.LocalEndPoint.Port);
        Assert.Equal(server.Login, socket.Server);
        Assert.Equal(0, socket.DatagramsFiltered);
        Assert.Null(socket.Fault);
    }

    [Fact]
    public async Task It_sends_to_both_server_ports_and_receives_from_both()
    {
        using var server = FakeServer.Open();
        using var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);

        socket.Send(server.Login.Port, "login"u8);
        socket.Send(server.World.Port, "world"u8);
        Assert.Equal("login", server.ReceiveOnLogin(out var fromClient));
        Assert.Equal("world", server.ReceiveOnWorld(out _));
        Assert.Equal(socket.LocalEndPoint, fromClient);

        server.SendFromLogin(socket.LocalEndPoint, "connect request"u8);
        server.SendFromWorld(socket.LocalEndPoint, "first bundle"u8);

        var received = new Dictionary<int, string>();
        var buffer = new byte[UdpLoopbackSocket.ReceiveBufferSize];
        while (received.Count < 2)
        {
            Assert.True(await socket.WaitForDatagramAsync(Patience, CancellationToken.None), "a datagram from the server should arrive");
            while (socket.TryReceive(buffer, out var length, out var port))
            {
                received.Add(port, System.Text.Encoding.ASCII.GetString(buffer, 0, length));
            }
        }

        Assert.Equal("connect request", received[server.Login.Port]);
        Assert.Equal("first bundle", received[server.World.Port]);
        Assert.Equal(0, socket.DatagramsFiltered);
    }

    [Fact]
    public async Task A_datagram_from_any_other_port_is_dropped_and_counted()
    {
        using var server = FakeServer.Open();
        using var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);
        using var stranger = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        stranger.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        stranger.SendTo("intruder"u8, socket.LocalEndPoint);
        await Eventually(() => socket.DatagramsFiltered == 1);
        server.SendFromLogin(socket.LocalEndPoint, "server"u8);

        Assert.True(await socket.WaitForDatagramAsync(Patience, CancellationToken.None));
        var buffer = new byte[64];
        Assert.True(socket.TryReceive(buffer, out var length, out var port));
        Assert.Equal("server", System.Text.Encoding.ASCII.GetString(buffer, 0, length));
        Assert.Equal(server.Login.Port, port);
        Assert.False(socket.TryReceive(buffer, out _, out _));
        Assert.Equal(1, socket.DatagramsFiltered);
    }

    [Fact]
    public async Task A_datagram_longer_than_the_callers_buffer_is_dropped_and_counted()
    {
        using var server = FakeServer.Open();
        using var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);

        server.SendFromLogin(socket.LocalEndPoint, new byte[100]);
        Assert.True(await socket.WaitForDatagramAsync(Patience, CancellationToken.None));

        Assert.False(socket.TryReceive(new byte[50], out _, out _));
        Assert.Equal(1, socket.DatagramsFiltered);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(-9000)]
    public void Only_the_two_server_ports_can_be_sent_to(int offset)
    {
        using var server = FakeServer.Open();
        using var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);

        Assert.Throws<ArgumentOutOfRangeException>(() => socket.Send(server.Login.Port + offset, "x"u8));
    }

    [Fact]
    public void A_server_that_is_not_loopback_is_refused_before_a_socket_opens()
    {
        Assert.Throws<LoopbackRequiredException>(() => new UdpLoopbackSocket(new IPEndPoint(IPAddress.Parse("10.0.0.5"), 9000), TimeProvider.System));
        Assert.Throws<LoopbackRequiredException>(() => new UdpLoopbackSocket(new IPEndPoint(new IPAddress(0), 9000), TimeProvider.System));
        Assert.Throws<LoopbackRequiredException>(() => new UdpLoopbackSocket(null!, TimeProvider.System));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    public void A_server_port_without_a_world_port_after_it_is_refused(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpLoopbackSocket(new IPEndPoint(IPAddress.Loopback, port), TimeProvider.System));
    }

    [Fact]
    public async Task Waiting_ends_after_the_timeout_measured_on_the_sessions_clock()
    {
        using var server = FakeServer.Open();
        var time = new ManualTimeProvider();
        using var socket = new UdpLoopbackSocket(server.Login, time);

        var wait = socket.WaitForDatagramAsync(TimeSpan.FromSeconds(30), CancellationToken.None).AsTask();
        Assert.False(wait.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(30));

        Assert.False(await wait.WaitAsync(Patience));
    }

    [Fact]
    public async Task Waiting_ends_on_cancellation()
    {
        using var server = FakeServer.Open();
        using var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);
        using var cancel = new CancellationTokenSource();

        var wait = socket.WaitForDatagramAsync(Timeout.InfiniteTimeSpan, cancel.Token).AsTask();
        await cancel.CancelAsync();

        Assert.False(await wait.WaitAsync(Patience));
    }

    /// <summary>
    /// Disposing before the receive loop has run its first line still ends the loop cleanly (N3: the loop used to read
    /// the cancellation token from the already disposed source, fault, and never complete the channel, so a wait started
    /// before the dispose hung; the N2 dispose test hit it under load).
    /// </summary>
    [Fact]
    public async Task Disposing_at_once_after_construction_ends_the_loop_cleanly()
    {
        using var server = FakeServer.Open();
        for (var i = 0; i < 200; i++)
        {
            var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);
            socket.Dispose();

            await socket.ReceiveLoop.WaitAsync(Patience);
            Assert.Null(socket.Fault);
        }
    }

    [Fact]
    public async Task Disposing_is_idempotent_and_ends_a_wait_and_any_later_use()
    {
        using var server = FakeServer.Open();
        var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);
        var wait = socket.WaitForDatagramAsync(Timeout.InfiniteTimeSpan, CancellationToken.None).AsTask();

        socket.Dispose();
        socket.Dispose();

        Assert.False(await wait.WaitAsync(Patience));
        await socket.ReceiveLoop.WaitAsync(Patience);
        Assert.Throws<ObjectDisposedException>(() => socket.Send(server.Login.Port, "x"u8));
        Assert.Throws<ObjectDisposedException>(() => socket.TryReceive(new byte[8], out _, out _));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await socket.WaitForDatagramAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.Null(socket.Fault);
    }

    /// <summary>
    /// A receive loop that ends on a socket error keeps the error and completes the channel, so every later wait says at
    /// once that nothing can arrive; the session sleeps out its own wait instead (WaitForInboundTests).
    /// </summary>
    [Fact]
    public async Task A_fault_ends_the_loop_and_later_waits_complete_with_false_at_once()
    {
        using var server = FakeServer.Open();
        using var socket = new UdpLoopbackSocket(server.Login, TimeProvider.System);

        socket.BreakForTests();
        await socket.ReceiveLoop.WaitAsync(Patience);

        Assert.NotNull(socket.Fault);
        Assert.False(await socket.WaitForDatagramAsync(Timeout.InfiniteTimeSpan, CancellationToken.None).AsTask().WaitAsync(Patience));
        Assert.False(socket.TryReceive(new byte[8], out _, out _));
    }

    [Fact]
    public void An_ipv6_server_gets_a_socket_on_the_ipv6_loopback()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return; // nothing to check on a machine without IPv6
        }

        using var socket = new UdpLoopbackSocket(new IPEndPoint(IPAddress.IPv6Loopback, 9000), TimeProvider.System);
        Assert.Equal(IPAddress.IPv6Loopback, socket.LocalEndPoint.Address);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the condition did not come true in time");
            await Task.Delay(10);
        }
    }

    /// <summary>Two UDP sockets on 127.0.0.1, on consecutive ports, standing in for ACE's login and world ports.</summary>
    internal sealed class FakeServer : IDisposable
    {
        private readonly Socket _login;
        private readonly Socket _world;

        private FakeServer(Socket login, Socket world)
        {
            _login = login;
            _world = world;
            Login = (IPEndPoint)login.LocalEndPoint!;
            World = (IPEndPoint)world.LocalEndPoint!;
        }

        public IPEndPoint Login { get; }

        public IPEndPoint World { get; }

        public static FakeServer Open()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var login = Bound(0);
                var port = ((IPEndPoint)login.LocalEndPoint!).Port;
                if (port < IPEndPoint.MaxPort)
                {
                    try
                    {
                        return new FakeServer(login, Bound(port + 1));
                    }
                    catch (SocketException)
                    {
                        // Port + 1 is taken: try another pair.
                    }
                }

                login.Dispose();
            }

            throw new InvalidOperationException("no two consecutive free UDP ports on 127.0.0.1");
        }

        public string ReceiveOnLogin(out EndPoint from) => Receive(_login, out from);

        public string ReceiveOnWorld(out EndPoint from) => Receive(_world, out from);

        public void SendFromLogin(EndPoint to, ReadOnlySpan<byte> datagram) => _login.SendTo(datagram, to);

        public void SendFromWorld(EndPoint to, ReadOnlySpan<byte> datagram) => _world.SendTo(datagram, to);

        public void Dispose()
        {
            _login.Dispose();
            _world.Dispose();
        }

        private static Socket Bound(int port)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                socket.ReceiveTimeout = (int)Patience.TotalMilliseconds;
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static string Receive(Socket socket, out EndPoint from)
        {
            var buffer = new byte[256];
            from = new IPEndPoint(IPAddress.Loopback, 0);
            var length = socket.ReceiveFrom(buffer, ref from);
            return System.Text.Encoding.ASCII.GetString(buffer, 0, length);
        }
    }
}

using System.Net;
using Lifestone.Net.Sockets;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// A virtual loopback network for the fake server and the client under test (m2-plan §3.3 N4): datagrams travel
/// between endpoints on 127.0.0.1 by copy, synchronously, through an optional seeded <see cref="LossModel"/> per
/// direction. Nothing touches a real socket. Client sockets implement <see cref="IDatagramSocket"/>, so the session's
/// <c>SocketFactory</c> can open them.
/// </summary>
/// <remarks>
/// Server endpoints are bound with <see cref="BindServer"/>; traffic from any other endpoint to a server endpoint is
/// client-to-server, the rest server-to-client. Delivery to an endpoint nobody bound (a closed client socket) is
/// counted in <see cref="Undeliverable"/>. Thread-safe enough for a test that waits on a socket from another thread.
/// </remarks>
internal sealed class InMemoryNetwork
{
    /// <summary>The first ephemeral port a client socket gets.</summary>
    public const int FirstEphemeralPort = 50_000;

    private readonly object _gate = new();
    private readonly Dictionary<IPEndPoint, Endpoint> _endpoints = [];
    private readonly HashSet<IPEndPoint> _serverEndpoints = [];
    private int _nextPort = FirstEphemeralPort;

    /// <summary>A network on <paramref name="time"/>, with optional loss in each direction.</summary>
    public InMemoryNetwork(TimeProvider time, LossModel? clientToServer = null, LossModel? serverToClient = null)
    {
        Time = time ?? throw new ArgumentNullException(nameof(time));
        ClientToServer = clientToServer;
        ServerToClient = serverToClient;
    }

    /// <summary>The clock the loss models time their holds on.</summary>
    public TimeProvider Time { get; }

    /// <summary>The loss model for client-to-server traffic, if any.</summary>
    public LossModel? ClientToServer { get; }

    /// <summary>The loss model for server-to-client traffic, if any.</summary>
    public LossModel? ServerToClient { get; }

    /// <summary>Datagrams sent to an endpoint nobody had bound.</summary>
    public long Undeliverable { get; private set; }

    /// <summary>Every datagram sent, in order, before any loss (for tests that inspect the raw traffic), while
    /// <see cref="RecordSent"/> is on.</summary>
    public List<(IPEndPoint From, IPEndPoint To, byte[] Datagram)> Sent { get; } = [];

    /// <summary>Whether <see cref="Sent"/> records traffic (on by default; a long soak turns it off).</summary>
    public bool RecordSent { get; set; } = true;

    /// <summary>
    /// A test's targeted loss: called with the sender, the receiver and the datagram before any loss model; true drops
    /// the datagram (it is still recorded in <see cref="Sent"/> and counted in <see cref="Filtered"/>).
    /// </summary>
    public Func<IPEndPoint, IPEndPoint, byte[], bool>? Drop { get; set; }

    /// <summary>Datagrams <see cref="Drop"/> dropped.</summary>
    public long Filtered { get; private set; }

    /// <summary>Binds a server endpoint (127.0.0.1:9000 and :9001 for the fake ACE).</summary>
    public ServerEndpoint BindServer(IPEndPoint local)
    {
        ArgumentNullException.ThrowIfNull(local);
        lock (_gate)
        {
            if (_endpoints.ContainsKey(local))
            {
                throw new InvalidOperationException($"{local} is already bound");
            }

            var endpoint = new ServerEndpoint(this, local);
            _endpoints.Add(local, endpoint);
            _serverEndpoints.Add(local);
            return endpoint;
        }
    }

    /// <summary>Opens a client socket on an ephemeral 127.0.0.1 port that talks to <paramref name="server"/> (Port and Port + 1).</summary>
    public InMemoryClientSocket OpenClientSocket(IPEndPoint server)
    {
        ArgumentNullException.ThrowIfNull(server);
        lock (_gate)
        {
            var local = new IPEndPoint(IPAddress.Loopback, _nextPort++);
            var socket = new InMemoryClientSocket(this, local, server);
            _endpoints.Add(local, socket);
            return socket;
        }
    }

    /// <summary>Sends <paramref name="datagram"/> (copied) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal void Send(IPEndPoint from, IPEndPoint to, ReadOnlySpan<byte> datagram)
    {
        var copy = datagram.ToArray();
        LossModel? model;
        lock (_gate)
        {
            if (RecordSent)
            {
                Sent.Add((from, to, copy));
            }

            model = _serverEndpoints.Contains(to) ? ClientToServer : ServerToClient;
            if (Drop is { } drop && drop(from, to, copy))
            {
                Filtered++;
                return;
            }
        }

        if (model is null)
        {
            Deliver(from, to, copy);
        }
        else
        {
            model.Submit(copy, d => Deliver(from, to, d), Time);
        }
    }

    /// <summary>Forgets a closed endpoint.</summary>
    internal void Unbind(IPEndPoint local)
    {
        lock (_gate)
        {
            _endpoints.Remove(local);
            _serverEndpoints.Remove(local);
        }
    }

    private void Deliver(IPEndPoint from, IPEndPoint to, byte[] datagram)
    {
        Endpoint? target;
        lock (_gate)
        {
            if (!_endpoints.TryGetValue(to, out target))
            {
                Undeliverable++;
                return;
            }
        }

        target.Enqueue(from, datagram);
    }

    /// <summary>A bound endpoint's receive queue.</summary>
    internal abstract class Endpoint(InMemoryNetwork network, IPEndPoint local)
    {
        private readonly Queue<(IPEndPoint From, byte[] Datagram)> _queue = new();
        private TaskCompletionSource? _arrival;

        /// <summary>The network.</summary>
        protected InMemoryNetwork Network { get; } = network;

        /// <summary>The local endpoint.</summary>
        public IPEndPoint LocalEndPoint { get; } = local;

        /// <summary>Datagrams waiting.</summary>
        public int Pending
        {
            get
            {
                lock (_queue)
                {
                    return _queue.Count;
                }
            }
        }

        /// <summary>Takes the next datagram, if any.</summary>
        public bool TryDequeue(out IPEndPoint from, out byte[] datagram)
        {
            lock (_queue)
            {
                if (_queue.TryDequeue(out var item))
                {
                    (from, datagram) = item;
                    return true;
                }
            }

            from = null!;
            datagram = [];
            return false;
        }

        /// <summary>Completes when a datagram is waiting.</summary>
        protected Task ArrivalAsync()
        {
            lock (_queue)
            {
                if (_queue.Count > 0)
                {
                    return Task.CompletedTask;
                }

                _arrival ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _arrival.Task;
            }
        }

        internal virtual void Enqueue(IPEndPoint from, byte[] datagram)
        {
            TaskCompletionSource? arrival;
            lock (_queue)
            {
                _queue.Enqueue((from, datagram));
                arrival = _arrival;
                _arrival = null;
            }

            arrival?.TrySetResult();
        }
    }

    /// <summary>A server endpoint: the fake ACE's 9000 or 9001.</summary>
    internal sealed class ServerEndpoint(InMemoryNetwork network, IPEndPoint local) : Endpoint(network, local)
    {
        /// <summary>Sends a datagram from this endpoint.</summary>
        public void Send(IPEndPoint to, ReadOnlySpan<byte> datagram) => Network.Send(LocalEndPoint, to, datagram);
    }

    /// <summary>
    /// A client socket: <see cref="IDatagramSocket"/> over the network, bound to 127.0.0.1 on an ephemeral port, keeping
    /// only datagrams from the server's address on Port or Port + 1, as <c>UdpLoopbackSocket</c> does.
    /// </summary>
    internal sealed class InMemoryClientSocket : Endpoint, IDatagramSocket
    {
        private bool _disposed;

        public InMemoryClientSocket(InMemoryNetwork network, IPEndPoint local, IPEndPoint server)
            : base(network, local)
        {
            Server = server;
        }

        public IPEndPoint Server { get; }

        public long DatagramsFiltered { get; private set; }

        /// <summary>Datagrams this socket sent.</summary>
        public long DatagramsSent { get; private set; }

        /// <summary>True once disposed.</summary>
        public bool IsDisposed => _disposed;

        public void Send(int port, ReadOnlySpan<byte> datagram)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (port != Server.Port && port != Server.Port + 1)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, $"only {Server.Port} and {Server.Port + 1} are reachable");
            }

            DatagramsSent++;
            Network.Send(LocalEndPoint, new IPEndPoint(Server.Address, port), datagram);
        }

        public bool TryReceive(Span<byte> destination, out int length, out int sourcePort)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (TryDequeue(out var from, out var datagram))
            {
                if (datagram.Length > destination.Length)
                {
                    DatagramsFiltered++;
                    continue;
                }

                datagram.CopyTo(destination);
                length = datagram.Length;
                sourcePort = from.Port;
                return true;
            }

            length = 0;
            sourcePort = 0;
            return false;
        }

        public async ValueTask<bool> WaitForDatagramAsync(TimeSpan max, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var arrival = ArrivalAsync();
            if (arrival.IsCompleted)
            {
                return true;
            }

            using var timeout = new CancellationTokenSource(max, Network.Time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            try
            {
                await arrival.WaitAsync(linked.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Network.Unbind(LocalEndPoint);
        }

        internal override void Enqueue(IPEndPoint from, byte[] datagram)
        {
            if (!from.Address.Equals(Server.Address) || (from.Port != Server.Port && from.Port != Server.Port + 1))
            {
                DatagramsFiltered++;
                return;
            }

            base.Enqueue(from, datagram);
        }
    }
}

// Partly ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/NetClient.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: only the socket setup is OpenAC's (an
// OS-assigned local port, SIO_UDP_CONNRESET switched off, SendTo and ReceiveFromAsync). The bind is the loopback address
// of the server's family instead of every interface; the source filter, the background receive loop into a bounded
// channel of pooled buffers and the IDatagramSocket surface are Lifestone's. N3: the loop's cancellation token is taken
// before the loop starts.

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Lifestone.Net.Diagnostics;

namespace Lifestone.Net.Sockets;

/// <summary>
/// The session's real socket (m2-plan §3.3 N2): one UDP socket bound to the loopback address of the server's family on
/// an OS-assigned port (127.0.0.1:0, or [::1]:0 for an IPv6 server), never every interface. It sends only to the
/// server's two ports and keeps only what comes back from them.
/// </summary>
/// <remarks>
/// <para><b>Loopback only.</b> The constructor runs <see cref="LoopbackPolicy.Require"/> on the server endpoint itself,
/// so even a caller that skipped <c>AcSession.Create</c>'s gate cannot point it elsewhere, and <see cref="Send"/> takes a
/// port, not an endpoint.</para>
/// <para><b>Receiving.</b> One background loop calls <c>ReceiveFromAsync</c> into buffers rented from
/// <see cref="ArrayPool{T}.Shared"/>. A datagram that does not come from the server's address on Port or Port + 1 is
/// returned to the pool and counted in <see cref="DatagramsFiltered"/>. The rest go into a bounded channel of
/// <see cref="ChannelCapacity"/> datagrams; when it is full the newest is dropped and counted in
/// <see cref="DatagramsOverflowed"/>. The loop never parses. <see cref="TryReceive"/> drains the channel on the
/// caller's thread without blocking.</para>
/// <para><b>Faults.</b> A socket error other than shutdown stops the loop and is kept in <see cref="Fault"/>; the next
/// <see cref="TryReceive"/> on the game thread logs it once, as an error, and <see cref="WaitForDatagramAsync"/>
/// completes with false at once from then on (nothing more can arrive). The session's silence watchdog then ends the
/// session; nothing is retried here.</para>
/// </remarks>
public sealed class UdpLoopbackSocket : IDatagramSocket
{
    /// <summary>Datagrams the channel holds before it drops the newest.</summary>
    public const int ChannelCapacity = 8192;

    /// <summary>
    /// The receive buffer size. ACE's datagrams are at most 484 bytes (docs/protocol.md §1), so anything near this is
    /// not ACE's and is dropped as filtered.
    /// </summary>
    internal const int ReceiveBufferSize = 2048;

    private const int SioUdpConnReset = -1744830452;

    /// <summary>The value of <c>_fault</c> while the loop has not faulted.</summary>
    private const int NoFault = int.MinValue;

    private readonly Socket _socket;
    private readonly IPEndPoint _server;
    private readonly IPEndPoint _local;
    private readonly SocketAddress _loginAddress;
    private readonly SocketAddress _worldAddress;
    private readonly TimeProvider _time;
    private readonly INetLog _log;
    private readonly Channel<ReceivedDatagram> _channel;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _receiveLoop;
    private long _filtered;
    private long _overflowed;
    private volatile int _fault = NoFault;
    private bool _faultLogged;
    private volatile bool _disposed;

    /// <summary>
    /// Opens the socket for <paramref name="server"/> (its login port; the world port is Port + 1) and starts receiving.
    /// </summary>
    /// <param name="server">ACE's endpoint, which must pass <see cref="LoopbackPolicy.Require"/>.</param>
    /// <param name="time">The session's clock, for <see cref="WaitForDatagramAsync"/>'s timeout.</param>
    /// <param name="log">Where a receive fault is reported, on the thread that calls <see cref="TryReceive"/>.</param>
    /// <exception cref="LoopbackRequiredException"><paramref name="server"/> is not a loopback IP endpoint.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The server port is 0 or 65535 (Port + 1 must be a port).</exception>
    public UdpLoopbackSocket(IPEndPoint server, TimeProvider time, INetLog? log = null)
    {
        var allowed = LoopbackPolicy.Require(server);
        ArgumentNullException.ThrowIfNull(time);
        if (allowed.Port is < IPEndPoint.MinPort + 1 or > IPEndPoint.MaxPort - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(server), allowed.Port, "the server port must be 1..65534, so that Port + 1 is a port");
        }

        _server = allowed;
        _time = time;
        _log = log ?? NullNetLog.Instance;
        _loginAddress = new IPEndPoint(allowed.Address, allowed.Port).Serialize();
        _worldAddress = new IPEndPoint(allowed.Address, allowed.Port + 1).Serialize();

        var bindAddress = allowed.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        _socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Without this, an ICMP port-unreachable for an earlier send (ACE not listening yet) makes the next
                // receive fail with ConnectionReset on Windows (OpenAC NetClient).
                _socket.IOControl((IOControlCode)SioUdpConnReset, [0, 0, 0, 0], null);
            }

            _socket.Bind(new IPEndPoint(bindAddress, 0));
            _local = (IPEndPoint)_socket.LocalEndPoint!;
        }
        catch
        {
            _socket.Dispose();
            throw;
        }

        _channel = Channel.CreateBounded<ReceivedDatagram>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full: the loop drops the newest and counts it
            SingleWriter = true,
            SingleReader = false, // the loop drains what is left when it ends, beside Dispose
        });

        // The token is taken here, not in the loop: Dispose may cancel and dispose the source before the loop's first
        // line runs, and reading Token from a disposed source would throw before the loop could complete the channel,
        // leaving every waiter hanging (N3: a race the N2 tests hit under load).
        var shutdown = _shutdown.Token;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(shutdown));
    }

    /// <inheritdoc />
    public IPEndPoint Server => new(_server.Address, _server.Port);

    /// <inheritdoc />
    public IPEndPoint LocalEndPoint => new(_local.Address, _local.Port);

    /// <inheritdoc />
    public long DatagramsFiltered => Interlocked.Read(ref _filtered);

    /// <summary>Datagrams from the server dropped because <see cref="ChannelCapacity"/> were already waiting.</summary>
    public long DatagramsOverflowed => Interlocked.Read(ref _overflowed);

    /// <summary>The socket error that stopped the receive loop, or null while it runs (or after a clean shutdown).</summary>
    public SocketError? Fault => _fault == NoFault ? null : (SocketError)_fault;

    /// <summary>The background receive loop; it completes after <see cref="Dispose"/> or a fault (for tests).</summary>
    internal Task ReceiveLoop => _receiveLoop;

    /// <summary>
    /// Closes the operating-system socket under the receive loop without disposing this object (for tests): the pending
    /// receive fails and the loop ends with a <see cref="Fault"/>, as after a real socket error.
    /// </summary>
    internal void BreakForTests() => _socket.Dispose();

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is neither the server's Port nor Port + 1.</exception>
    /// <exception cref="ObjectDisposedException">The socket was disposed.</exception>
    /// <exception cref="SocketException">The operating system refused the send (the transport counts it as lost).</exception>
    public void Send(int port, ReadOnlySpan<byte> datagram)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = port == _server.Port ? _loginAddress
            : port == _server.Port + 1 ? _worldAddress
            : throw new ArgumentOutOfRangeException(nameof(port), port, $"only the server's ports {_server.Port} and {_server.Port + 1} can be sent to");
        _socket.SendTo(datagram, SocketFlags.None, target);
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The socket was disposed.</exception>
    public bool TryReceive(Span<byte> destination, out int length, out int sourcePort)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (_channel.Reader.TryRead(out var datagram))
        {
            try
            {
                if (datagram.Length > destination.Length)
                {
                    Interlocked.Increment(ref _filtered);
                    continue;
                }

                datagram.Bytes.CopyTo(destination);
                length = datagram.Length;
                sourcePort = datagram.SourcePort;
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(datagram.Buffer);
            }
        }

        ReportFault();
        length = 0;
        sourcePort = 0;
        return false;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The socket was disposed before the call.</exception>
    public async ValueTask<bool> WaitForDatagramAsync(TimeSpan max, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (max < TimeSpan.Zero && max != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "must be non-negative or Timeout.InfiniteTimeSpan");
        }

        if (_channel.Reader.TryPeek(out _))
        {
            return true;
        }

        using var timeout = new CancellationTokenSource(max, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            // False when the loop has ended (fault or dispose) and nothing is left.
            return await _channel.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Closes the socket, stops the receive loop and returns every buffered datagram to the pool. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _socket.Dispose();
        _shutdown.Dispose(); // already cancelled, so the loop's token stays usable
        Drain();
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        EndPoint anySender = new IPEndPoint(_local.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
                SocketReceiveFromResult result;
                try
                {
                    result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, anySender, token).ConfigureAwait(false);
                }
                catch (SocketException e) when (e.SocketErrorCode is SocketError.MessageSize or SocketError.ConnectionReset && !_disposed)
                {
                    // An oversized datagram (not ACE's) or a stray ICMP report: drop it and keep listening.
                    ArrayPool<byte>.Shared.Return(buffer);
                    Interlocked.Increment(ref _filtered);
                    continue;
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }

                if (!IsFromServer(result.RemoteEndPoint, out var sourcePort))
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    Interlocked.Increment(ref _filtered);
                    continue;
                }

                if (!_channel.Writer.TryWrite(new ReceivedDatagram(buffer, result.ReceivedBytes, sourcePort)))
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    Interlocked.Increment(ref _overflowed);
                }
            }
        }
        catch (Exception) when (_disposed || token.IsCancellationRequested)
        {
            // Shutdown: the socket was closed under the pending receive.
        }
        catch (SocketException e)
        {
            _fault = (int)e.SocketErrorCode;
        }
        catch (Exception)
        {
            // Not a socket error and not shutdown: still a fault that ends receiving, reported on the game thread.
            _fault = (int)SocketError.SocketError;
        }
        finally
        {
            _channel.Writer.TryComplete();
            if (_disposed)
            {
                Drain();
            }
        }
    }

    private bool IsFromServer(EndPoint sender, out int sourcePort)
    {
        if (sender is IPEndPoint ip
            && ip.Address.Equals(_server.Address)
            && (ip.Port == _server.Port || ip.Port == _server.Port + 1))
        {
            sourcePort = ip.Port;
            return true;
        }

        sourcePort = 0;
        return false;
    }

    private void Drain()
    {
        while (_channel.Reader.TryRead(out var datagram))
        {
            ArrayPool<byte>.Shared.Return(datagram.Buffer);
        }
    }

    private void ReportFault()
    {
        if (_faultLogged || Fault is not { } fault)
        {
            return;
        }

        _faultLogged = true;
        _log.Log(NetLogLevel.Error, $"the UDP socket on {_local} stopped receiving: {fault}; no more datagrams will arrive from {_server}");
    }

    /// <summary>One received datagram in a pooled buffer.</summary>
    private readonly record struct ReceivedDatagram(byte[] Buffer, int Length, int SourcePort)
    {
        public ReadOnlySpan<byte> Bytes => Buffer.AsSpan(0, Length);
    }
}

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using Lifestone.Net.Sockets;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// A seeded lossy decorator over any <see cref="IDatagramSocket"/> (m2-plan §3.3 N8): what the session sends passes
/// through one <see cref="LossModel"/>, what it receives through another, so the same loss, adjacent reordering and
/// duplication hit the in-memory network of the virtual soak and, in N12, the real loopback socket to ACE.
/// </summary>
/// <remarks>
/// <para><b>Armed on the first encrypted send.</b> Until the session sends its first encrypted packet (its first message,
/// after the handshake), every datagram passes untouched both ways: the soak tests the reliable transport, not the
/// handshake's retries, which have their own tests. Each model keeps N4's release rule: a held datagram goes right after
/// its successor, or after 20 ms.</para>
/// <para><b>The CRC oracle.</b> <see cref="LossModel.DuplicateEncryptedDeliveries"/> of <see cref="ClientToServer"/>
/// counts the encrypted client packets forwarded a second time; ACE counts exactly those as CRC errors, because each
/// spends a key its <c>CryptoSystem</c> already consumed.</para>
/// <para>Its parameters come from the constructor only (no environment, no console). Datagrams a hold timer releases
/// wait in a queue that the next <see cref="Send"/> or <see cref="TryReceive"/> flushes, so the inner socket is only used
/// from the session's thread even when the timers run on a real clock.</para>
/// </remarks>
internal sealed class LossyDatagramSocket : IDatagramSocket
{
    private const int ReceiveBufferSize = 2048;

    private readonly IDatagramSocket _inner;
    private readonly TimeProvider _time;
    private readonly ConcurrentQueue<(int Port, byte[] Datagram)> _outbox = new();
    private readonly ConcurrentQueue<(int Port, byte[] Datagram)> _inbox = new();
    private readonly byte[] _receive = new byte[ReceiveBufferSize];

    /// <summary>A decorator over <paramref name="inner"/>, with a model per direction.</summary>
    public LossyDatagramSocket(IDatagramSocket inner, TimeProvider time, LossModel clientToServer, LossModel serverToClient)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ClientToServer = clientToServer ?? throw new ArgumentNullException(nameof(clientToServer));
        ServerToClient = serverToClient ?? throw new ArgumentNullException(nameof(serverToClient));
    }

    /// <summary>The model the session's sends pass through.</summary>
    public LossModel ClientToServer { get; }

    /// <summary>The model the server's datagrams pass through.</summary>
    public LossModel ServerToClient { get; }

    /// <summary>True from the session's first encrypted send on.</summary>
    public bool Armed { get; private set; }

    /// <summary>The decorated socket.</summary>
    public IDatagramSocket Inner => _inner;

    public IPEndPoint Server => _inner.Server;

    public IPEndPoint LocalEndPoint => _inner.LocalEndPoint;

    public long DatagramsFiltered => _inner.DatagramsFiltered;

    public void Send(int port, ReadOnlySpan<byte> datagram)
    {
        if (!Armed && IsEncrypted(datagram))
        {
            Armed = true;
        }

        if (!Armed)
        {
            FlushOutbox();
            _inner.Send(port, datagram);
            return;
        }

        ClientToServer.Submit(datagram.ToArray(), d => _outbox.Enqueue((port, d)), _time);
        FlushOutbox();
    }

    public bool TryReceive(Span<byte> destination, out int length, out int sourcePort)
    {
        FlushOutbox();
        while (_inner.TryReceive(_receive, out var received, out var port))
        {
            var copy = _receive.AsSpan(0, received).ToArray();
            if (Armed)
            {
                ServerToClient.Submit(copy, d => _inbox.Enqueue((port, d)), _time);
            }
            else
            {
                _inbox.Enqueue((port, copy));
            }
        }

        while (_inbox.TryDequeue(out var item))
        {
            if (item.Datagram.Length > destination.Length)
            {
                continue;  // longer than the caller's buffer: dropped, as the in-memory socket drops it
            }

            item.Datagram.CopyTo(destination);
            length = item.Datagram.Length;
            sourcePort = item.Port;
            return true;
        }

        length = 0;
        sourcePort = 0;
        return false;
    }

    public ValueTask<bool> WaitForDatagramAsync(TimeSpan max, CancellationToken cancellationToken) =>
        _inbox.IsEmpty ? _inner.WaitForDatagramAsync(max, cancellationToken) : ValueTask.FromResult(true);

    public void Dispose() => _inner.Dispose();

    /// <summary>True for a datagram whose header has EncryptedChecksum (0x2) in its flags (at offset 4).</summary>
    private static bool IsEncrypted(ReadOnlySpan<byte> datagram) =>
        datagram.Length >= 20 && (BinaryPrimitives.ReadUInt32LittleEndian(datagram[4..]) & 0x2) != 0;

    private void FlushOutbox()
    {
        while (_outbox.TryDequeue(out var item))
        {
            _inner.Send(item.Port, item.Datagram);
        }
    }
}

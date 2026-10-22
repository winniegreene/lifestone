using System.Net.Sockets;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Packets;
using Lifestone.Net.Sockets;

namespace Lifestone.Net.Session;

/// <summary>
/// Login attempts, the handshake, the retry policy and the timeouts (m2-plan §3.3 N5; docs/protocol.md §4.1).
/// </summary>
/// <remarks>
/// <para><b>Connecting.</b> <see cref="Login"/> opens a socket and sends LoginRequest to the login port. When no
/// ConnectRequest arrives within <see cref="SessionTimeouts.ConnectRequest"/>, the attempt counts as lost: the socket is
/// closed and, under the retry policy, another attempt starts from a fresh socket (ACE keys sessions by endpoint, and a
/// second LoginRequest from the same endpoint would hit its "bad handshake" rule).</para>
/// <para><b>Handshaking.</b> ConnectRequest seeds the transport. The ConnectResponse goes to the world port
/// <see cref="SessionTimeouts.ConnectResponseDelay"/> later (ACE ignores one that arrives before its own database write,
/// the handshake race), then every <see cref="SessionTimeouts.ConnectResponseInterval"/> until the first encrypted
/// server packet, within <see cref="SessionTimeouts.Handshake"/>. Retail resends it every third of a second in
/// <c>ClientNet::ProcessConnection</c> (11.4186 0x00545450, 11.6096 0x00546010) through
/// <c>ClientNet::SendConnectAck</c> (11.4186 0x005440F0, 11.6096 0x00544CB0).</para>
/// <para><b>Silence.</b> The session fails when nothing arrived for <see cref="SessionTimeouts.Silence"/> while the
/// previous pump ran within that window. Retail's check is the same shape: <c>ClientNet::ProcessConnection</c> drops
/// the connection only when the last <c>SharedNet::UseTime</c> (11.4186 0x00542450, 11.6096 0x00543030) ran less than
/// 140 s ago and no data came for 140 s, so a stalled main thread never counts as a silent server. Lifestone reads every
/// waiting datagram before it checks, so datagrams that queued up during a stall count as arrivals.</para>
/// </remarks>
internal sealed partial class SessionMachine
{
    private string? _account;
    private char[]? _password;
    private int _attempt;
    private long? _retryDueAt;
    private long _retryDueShadow = long.MinValue;
    private long _lastArrival;
    private long? _previousPumpAt;

    public void Login(AcCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        EnterGameThread(capture: false);
        if (Phase != SessionPhase.Idle)
        {
            throw new InvalidOperationException($"Login needs an idle session; this one is {Phase}.");
        }

        // Validates the strings (ASCII, lengths) before anything is opened; the messages never hold the password.
        _ = LoginRequest.GetSize(_options.ClientVersion, credentials.Account, credentials.Password);
        _account = credentials.Account;
        _password = credentials.Password.ToArray();
        _attempt = 0;
        SetPhase(SessionPhase.Connecting);
        StartAttempt();
    }

    /// <summary>Opens a fresh socket and sends this attempt's LoginRequest.</summary>
    private void StartAttempt()
    {
        RetryDueAt = null;
        CloseConnection();
        _attempt++;
        if (_password is null || _account is null)
        {
            Fail(SessionFailureKind.ConnectTimeout, "the login was abandoned: the credentials are gone");
            return;
        }

        IDatagramSocket socket;
        try
        {
            socket = _socketFactory(_options.Server) ?? throw new InvalidOperationException("the socket factory returned null");
        }
        catch (SocketException e)
        {
            Fail(SessionFailureKind.SocketError, $"could not open a socket for {_options.Server}: {e.SocketErrorCode}");
            return;
        }

        var connection = new SessionConnection(socket, _time, _attempt);
        _connection = connection;
        ResetLoginState();
        var timestamp = (uint)_time.GetUtcNow().ToUnixTimeSeconds();
        connection.SendLoginRequest(_options.ClientVersion, _account, _password, timestamp);
        Log(NetLogLevel.Info,
            $"login: LoginRequest sent to {_options.Server} from {connection.Socket.LocalEndPoint} (attempt {_attempt} of {_options.Retry.Attempts})");
    }

    /// <summary>A verified cleartext ConnectRequest arrived on a connection that had none.</summary>
    private void AcceptConnectRequest(SessionConnection connection, in BorrowedPacket packet)
    {
        connection.AcceptConnectRequest(packet.Optional, packet.Header.Iteration, _options.Order, _options.TailProbe);
        connection.NextConnectResponseAt = _time.GetTimestamp() + Ticks(_options.Timeouts.ConnectResponseDelay);
        SetPhase(SessionPhase.Handshaking);
        Raise(SessionEvent.ConnectRequestReceived(_time.GetTimestamp(), Phase, packet.Optional.ConnectRequestServerTime));
        Log(NetLogLevel.Info, $"login: ConnectRequest received; client id {connection.ClientId}");
    }

    /// <summary>The first encrypted server packet: the server took the ConnectResponse and the data check begins.</summary>
    private void ConfirmHandshake(SessionConnection connection)
    {
        connection.HandshakeConfirmed = true;
        if (Phase == SessionPhase.Handshaking)
        {
            _dataCheckStartedAt = _time.GetTimestamp();
            SetPhase(SessionPhase.DataCheck);
            Log(NetLogLevel.Info, $"login: handshake complete after {connection.ConnectResponsesSent} ConnectResponse(s)");
        }
    }

    /// <summary>The handshake's timers: ConnectRequest lost, ConnectResponse due, handshake cap.</summary>
    private void RunHandshakeTimers(SessionConnection connection)
    {
        var now = _time.GetTimestamp();
        if (connection.Transport is null)
        {
            if (Phase == SessionPhase.Connecting && _time.GetElapsedTime(connection.LoginRequestSentAt, now) >= _options.Timeouts.ConnectRequest)
            {
                const string Reason = "no ConnectRequest arrived";
                if (!_options.Retry.RetryLostConnectRequest || !TryScheduleRetry(0, Reason))
                {
                    Fail(SessionFailureKind.ConnectTimeout,
                        $"{Reason} within {_options.Timeouts.ConnectRequest.TotalSeconds:0.#} s of the LoginRequest ({_attempt} attempt(s)); is the server running at {_options.Server}?");
                }
            }

            return;
        }

        if (connection.HandshakeConfirmed || connection.ConnectRequestAt is not { } connectRequestAt)
        {
            return;
        }

        if (_time.GetElapsedTime(connectRequestAt, now) >= _options.Timeouts.Handshake)
        {
            Fail(SessionFailureKind.HandshakeTimeout,
                $"the server took none of {connection.ConnectResponsesSent} ConnectResponse(s) within {_options.Timeouts.Handshake.TotalSeconds:0.#} s");
            return;
        }

        if (now >= connection.NextConnectResponseAt)
        {
            connection.SendConnectResponse();
            connection.NextConnectResponseAt = now + Ticks(_options.Timeouts.ConnectResponseInterval);
        }
    }

    /// <summary>
    /// Schedules another login attempt under the retry policy (D18): the connection closes now, the next attempt starts
    /// from a fresh socket <see cref="LoginRetryPolicy.Delay"/> later. False when no attempt is left.
    /// </summary>
    private bool TryScheduleRetry(uint code, string reason)
    {
        if (_attempt >= _options.Retry.Attempts || _password is null)
        {
            return false;
        }

        CloseConnection();
        var delay = _options.Retry.Delay;
        RetryDueAt = _time.GetTimestamp() + Ticks(delay);
        SetPhase(SessionPhase.Connecting);
        Raise(SessionEvent.RetryScheduled(_time.GetTimestamp(), Phase, _attempt + 1, delay, code, reason));
        Log(NetLogLevel.Warning,
            $"login: {reason}{(code != 0 ? $" (CharacterError 0x{code:X2})" : string.Empty)}; attempt {_attempt + 1} of {_options.Retry.Attempts} in {delay.TotalSeconds:0.#} s from a fresh socket");
        return true;
    }

    /// <summary>The data check's cap and the silence watchdog.</summary>
    private void RunTimeouts(long? previousPump)
    {
        var now = _time.GetTimestamp();
        if (Phase == SessionPhase.DataCheck && _time.GetElapsedTime(_dataCheckStartedAt, now) >= _options.Timeouts.DataCheck)
        {
            Fail(SessionFailureKind.DataCheckTimeout,
                $"the data check did not finish within {_options.Timeouts.DataCheck.TotalSeconds:0.#} s (F7EA {(_dataCheckComplete ? "received" : "missing")}, F658 {(_listThisConnection ? "received" : "missing")})");
            return;
        }

        if (_connection is not { ConnectRequestAt: not null } || previousPump is not { } previous)
        {
            return;
        }

        var silence = _options.Timeouts.Silence;
        if (_time.GetElapsedTime(previous, now) < silence && _time.GetElapsedTime(_lastArrival, now) >= silence)
        {
            Fail(SessionFailureKind.Silence, $"nothing arrived from the server for {silence.TotalSeconds:0.#} s");
        }
    }

    /// <summary>When the scheduled retry is due; also published for <see cref="WaitForInboundAsync"/>, which may run on another thread.</summary>
    private long? RetryDueAt
    {
        get => _retryDueAt;
        set
        {
            _retryDueAt = value;
            Volatile.Write(ref _retryDueShadow, value ?? long.MinValue);
        }
    }

    private long Ticks(TimeSpan span) => (long)(span.TotalSeconds * _time.TimestampFrequency);
}

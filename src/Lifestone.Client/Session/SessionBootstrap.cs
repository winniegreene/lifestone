using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Recording;
using Lifestone.Data.Dat;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;
using Lifestone.Net.Taps;

namespace Lifestone.Client.Session;

/// <summary>
/// Creates live sessions for both hosts (the Godot session host and the headless host): the one product caller of the
/// network session's factory (ArchitectureTests pin this file), so the gates below cannot be skipped by either host.
/// </summary>
public static class SessionBootstrap
{
    /// <summary>The runner name the gates log.</summary>
    internal const string Runner = "SessionBootstrap.CreateLive";

    /// <summary>What needs the language dat, for <see cref="DatSet.RequireLocal"/>'s message.</summary>
    internal const string LanguageDatFeature = "Logging in (the data check reports its iteration)";

    /// <summary>
    /// Creates a live session against the bundled ACE, ready to log in (<see cref="GameSession.Actions"/>'s
    /// <c>Login</c>): it returns in phase <c>Idle</c>, with no socket open yet.
    /// </summary>
    /// <remarks>
    /// <para>Checks, in this order, before anything touches the network; each refusal logs one WARNING naming this
    /// runner and the reason, then throws:</para>
    /// <list type="number">
    /// <item><b>Loopback.</b> <paramref name="options"/>' server must be a loopback IP literal (<see cref="LoopbackGate"/>
    /// over net's <c>LoopbackPolicy</c>). A refusal throws <see cref="LoopbackRequiredException"/> (an
    /// <see cref="InvalidOperationException"/>).</item>
    /// <item><b>Mod ready.</b> <paramref name="pidsJson"/> (server/runtime/pids.json, as bytes) must say
    /// <c>ace.modReady</c>: ACE's first account would otherwise be promoted to Admin (owner decision D11). A refusal
    /// throws <see cref="BackendNotReadyException"/> (an <see cref="InvalidOperationException"/>) naming the fix.</item>
    /// <item><b>Language dat.</b> <paramref name="dats"/> must have client_local_English.dat (the data check reports its
    /// iteration; <see cref="DatSet.RequireLocal"/> throws <see cref="InvalidOperationException"/>).</item>
    /// <item><b>Account.</b> <paramref name="secretsJson"/> (server/runtime/secrets.json, as bytes, schema 1 or 2) must
    /// hold the account <see cref="SessionOptions.Account"/> names, or its default account when null
    /// (<see cref="PlayAccounts"/>; the headless host passes <c>test</c> when no <c>--account</c> was given). The password
    /// is copied into the session's credentials and never becomes a string; a refusal throws
    /// <see cref="PlayAccountException"/> (an <see cref="InvalidOperationException"/>) naming keys and usernames only.</item>
    /// </list>
    /// <para>Then the network session is created (net's <c>AcSession.Create</c>: no socket until the login) with the
    /// data-check iterations from <c>dats.Iterations</c> (the language dat's is the Local one), never hard-coded; the
    /// session clock's time source; net's log adapted to <paramref name="log"/> under the category <c>net</c>; and, with
    /// <see cref="SessionOptions.RetailNet"/>, retail's timeouts, a single login attempt, no tail probe and arrival order
    /// (owner decision D18). The session keeps the credentials (a zeroable <see cref="char"/> array) until it is disposed,
    /// so <see cref="GameSession.Reconnect"/> needs no secret; the host should zero its own copy of
    /// <paramref name="secretsJson"/> after the call.</para>
    /// </remarks>
    /// <param name="options">The Session option group's values.</param>
    /// <param name="secretsJson">The bytes of server/runtime/secrets.json, which the host read.</param>
    /// <param name="pidsJson">The bytes of server/runtime/pids.json, which the host read.</param>
    /// <param name="dats">The opened dats.</param>
    /// <param name="time">The session clock's time source (<see cref="TimeProvider.System"/> in the hosts).</param>
    /// <param name="log">Where the session (and, under the category <c>net</c>, the network session) logs.</param>
    /// <param name="recording">Where to write the session recording (.lsrec), or null for none. The host opens and
    /// disposes it, after disposing the session.</param>
    /// <param name="recordingHeader">The recording's header (who records, the scenario, the ACE commit), or null for a
    /// default one (runner <c>session</c>); its iterations are filled in from the dats when absent.</param>
    /// <returns>The session.</returns>
    public static GameSession CreateLive(
        SessionOptions options,
        ReadOnlySpan<byte> secretsJson,
        ReadOnlySpan<byte> pidsJson,
        DatSet dats,
        TimeProvider time,
        ILogSink log,
        Stream? recording,
        RecordingHeader? recordingHeader = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        return CreateLiveWith(options, secretsJson, pidsJson, () => LanguageIterations(dats), time, log, recording, socketFactory: null, recordingHeader);
    }

    /// <summary>
    /// <see cref="CreateLive"/> with the dat check as a function, the network session's socket factory (null: the
    /// loopback UDP socket) and, for tests, the network session itself: the S8 test passes a socket-factory spy that must
    /// never be invoked for a refused endpoint, and the session tests pass a scripted network session.
    /// </summary>
    /// <param name="options">The Session option group's values.</param>
    /// <param name="secretsJson">The bytes of secrets.json.</param>
    /// <param name="pidsJson">The bytes of pids.json.</param>
    /// <param name="iterations">Checks the language dat and returns the data check's iterations (gate 3).</param>
    /// <param name="time">The session clock's time source.</param>
    /// <param name="log">Where the session logs.</param>
    /// <param name="recording">Where to write the recording, or null.</param>
    /// <param name="socketFactory">The network session's socket factory, or null for the default.</param>
    /// <param name="recordingHeader">The recording's header, or null for the default.</param>
    /// <param name="networkFactory">Creates the network session from its options, sink and tap; null for net's
    /// <c>AcSession.Create</c>.</param>
    /// <param name="credentialsObserver">Tests only: sees the credentials the session will hold, as soon as the account
    /// gate made them, so a test can check they are zeroed when the creation fails. Null in the hosts.</param>
    internal static GameSession CreateLiveWith(
        SessionOptions options,
        ReadOnlySpan<byte> secretsJson,
        ReadOnlySpan<byte> pidsJson,
        Func<DddIterations> iterations,
        TimeProvider time,
        ILogSink log,
        Stream? recording,
        Func<IPEndPoint, IDatagramSocket>? socketFactory,
        RecordingHeader? recordingHeader = null,
        Func<AcSessionOptions, IAcSessionSink, IRawMessageTap, IAcSession>? networkFactory = null,
        Action<AcCredentials>? credentialsObserver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(iterations);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);

        // 1. Loopback, before anything else; nothing below runs for a refused endpoint (S8).
        var server = LoopbackGate.Require(options.Server, Runner, log);

        // 2. The Lifestone server mod is ready (D11).
        var backend = BackendReadiness.Require(pidsJson, Runner, log);

        // 3. The language dat, whose iteration the data check reports.
        DddIterations ddd;
        try
        {
            ddd = iterations();
        }
        catch (InvalidOperationException e)
        {
            log.Warn(SessionLog.Category, $"language dat: {Runner} refused: {e.Message}", new LogField("runner", Runner));
            throw;
        }

        // 4. The account.
        AcCredentials credentials;
        try
        {
            credentials = PlayAccounts.Parse(secretsJson, AccountChoice.FromOption(options.Account, AccountChoice.Default));
        }
        catch (PlayAccountException e)
        {
            log.Warn(SessionLog.Category, $"account: {Runner} refused: {e.Message}", new LogField("runner", Runner));
            throw;
        }

        try
        {
            credentialsObserver?.Invoke(credentials);
            log.Info(
                SessionLog.Category,
                "live session gates passed",
                new LogField("server", server),
                new LogField("account", credentials.Account),
                new LogField("mod", backend.ModVersion),
                new LogField("dats", $"p{ddd.Portal}-c{ddd.Cell}-l{ddd.Language}"),
                new LogField("recording", recording is not null),
                new LogField("sockets", socketFactory is null ? "loopback-udp" : "injected"));

            var netOptions = NetworkOptions(options, server, ddd, time, log, socketFactory);
            var create = networkFactory ?? CreateNetworkSession;
            var header = recordingHeader ?? new RecordingHeader { Runner = GameSession.DefaultRunner, CreatedUtc = time.GetUtcNow() };
            if (header.Iterations is null)
            {
                header = header with { Iterations = new DatIterations(ddd.Portal, ddd.Cell, ddd.Language, ddd.HighRes) };
            }

            var session = GameSession.CreateLive(
                new LiveSessionSetup(
                    server,
                    credentials,
                    (sink, tap) => create(netOptions, sink, tap),
                    TimeSpan.FromMilliseconds(options.BudgetMs),
                    recording,
                    header),
                time,
                log);
            log.Info(
                SessionLog.Category,
                "live session created: idle, no socket until the login",
                new LogField("retailNet", options.RetailNet),
                new LogField("budgetMs", options.BudgetMs));
            return session;
        }
        catch
        {
            credentials.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The network session's options for <paramref name="options"/>: the gated server, the data check's iterations, the
    /// session's time source, net's log adapted to <paramref name="log"/>, and Lifestone's defaults, or retail's with
    /// <see cref="SessionOptions.RetailNet"/> (<see cref="SessionTimeouts.Retail"/>, <see cref="LoginRetryPolicy.Retail"/>,
    /// no tail probe, <see cref="InboundOrder.Arrival"/>; D18).
    /// </summary>
    /// <param name="options">The Session option group's values.</param>
    /// <param name="server">The gated server.</param>
    /// <param name="iterations">The data check's iterations.</param>
    /// <param name="time">The session's time source.</param>
    /// <param name="log">Where net logs (category <c>net</c>).</param>
    /// <param name="socketFactory">The socket factory, or null for the default.</param>
    internal static AcSessionOptions NetworkOptions(
        SessionOptions options,
        IPEndPoint server,
        DddIterations iterations,
        TimeProvider time,
        ILogSink log,
        Func<IPEndPoint, IDatagramSocket>? socketFactory)
    {
        var retail = options.RetailNet;
        return new AcSessionOptions
        {
            Server = server,
            Iterations = iterations,
            Time = time,
            Timeouts = retail ? SessionTimeouts.Retail : SessionTimeouts.Default,
            Retry = retail ? LoginRetryPolicy.Retail : LoginRetryPolicy.Default,
            TailProbe = !retail,
            Order = retail ? InboundOrder.Arrival : InboundOrder.FragmentSequence,
            SocketFactory = socketFactory,
            Log = new NetLogAdapter(log),
        };
    }

    /// <summary>
    /// Gate 3: the language dat must be open (<see cref="DatSet.RequireLocal"/>), and the data check reports the dats'
    /// iterations (<see cref="ToDddIterations"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The language dat is absent.</exception>
    internal static DddIterations LanguageIterations(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        dats.RequireLocal(LanguageDatFeature);
        return ToDddIterations(dats.Iterations);
    }

    /// <summary>
    /// The data check's iterations from the dats' (<see cref="DatIterations.Local"/> is the language dat's), never
    /// hard-coded (m2-plan §1.6 item 24).
    /// </summary>
    /// <exception cref="InvalidOperationException">The language dat's iteration is unknown (the dat is absent).</exception>
    internal static DddIterations ToDddIterations(DatIterations iterations) =>
        iterations.Local is { } language
            ? new DddIterations(iterations.Portal, iterations.Cell, language, iterations.HighRes)
            : throw new InvalidOperationException(
                $"{DatSet.LocalFileName} is required to log in: the data check reports its iteration (end of retail: 994)");

    /// <summary>The product's network session: net's factory (this file is its one product caller).</summary>
    private static IAcSession CreateNetworkSession(AcSessionOptions options, IAcSessionSink sink, IRawMessageTap tap) =>
        AcSession.Create(options, sink, tap);
}

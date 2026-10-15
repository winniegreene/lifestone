using System.Reflection;
using System.Runtime.CompilerServices;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session;

/// <summary>
/// Compiles the session's message path on a worker thread before the first server message arrives, so the world-entry burst does not pay for the just-in-time compiler on the game
/// thread (DoD 15, <c>TODO(login-burst-frame)</c>; docs/session.md section 27). The first PlayerDescription of a login (the graduated test character's: 138 enchantments, hundreds of
/// spells) measured 51 ms live in one frame: offline, the same message decoded in 21 ms and reduced in 16 ms the first time and 0.23 ms in all the second, so the cost was compilation of
/// the decoder's and the reducer's code (the immutable tables and lists over a dozen value types, the log line's LINQ), not the message's size.
/// </summary>
/// <remarks>
/// <para>Two steps, both off the game thread and at below-normal priority. (1) <b>Run once</b>: a small well-formed PlayerDescription (<see cref="WarmUpMessages.PlayerDescription"/>, one entry in
/// every table) is decoded by the real decoder and reduced by a throwaway reducer over a state, bus and clock of its own, which compiles every generic instantiation the message uses.
/// Nothing is shared with the live session but static code (<see cref="EventTypeId{T}"/> hands out its indices atomically), and the throwaway log is silent. (2)
/// <b>Prepare</b>: <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/> over every method of the message codecs, the dispatcher, the reducer, the event bus and the contract types, which
/// covers the other messages of the burst (the first CreateObjects and updates). No code is executed by step 2. A method the game thread needs while the worker is compiling it waits for that
/// method, as it would compile it itself.</para>
/// </remarks>
public static class SessionJitWarmUp
{
    private static readonly string[] Namespaces =
    [
        "Lifestone.Net.Messages", "Lifestone.Net.Dispatch", "Lifestone.Net.Wire",
        "Lifestone.Client.Session.State", "Lifestone.Client.Session.Bus", "Lifestone.Client.Session.Contracts",
    ];

    private static int _started;
    private static volatile Result? _result;

    /// <summary>What the worker did, or null while it runs (or before <see cref="Start"/>).</summary>
    public static Result? Done => _result;

    /// <summary>The worker's outcome.</summary>
    /// <param name="DescriptionDecoded">The sample PlayerDescription was decoded and reduced (false means the sample no longer matches the decoder: <c>TODO(net)</c> fix the sample).</param>
    /// <param name="Methods">Methods prepared.</param>
    /// <param name="Skipped">Methods that could not be prepared.</param>
    /// <param name="RunMilliseconds">Wall-clock milliseconds of step 1.</param>
    /// <param name="Milliseconds">Wall-clock milliseconds of both steps.</param>
    public sealed record Result(bool DescriptionDecoded, int Methods, int Skipped, double RunMilliseconds, double Milliseconds);

    /// <summary>Starts the worker once per process.</summary>
    /// <param name="time">The clock that times the worker (the host's; the session layer reads no ambient time).</param>
    public static void Start(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var thread = new Thread(() => Work(time)) { IsBackground = true, Name = "SessionJitWarmUp", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    /// <summary>Runs the warm-up on the calling thread (tests and tools); returns what it did.</summary>
    /// <param name="time">The clock that times the run.</param>
    public static Result RunHere(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        Work(time);
        return _result!;
    }

    private static void Work(TimeProvider time)
    {
        var started = time.GetTimestamp();
        var decoded = false;
        try
        {
            decoded = RunSample(time);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or TypeLoadException)
        {
            // best effort: the live session compiles what this did not
        }

        var run = time.GetElapsedTime(started).TotalMilliseconds;
        var methods = 0;
        var skipped = 0;
        try
        {
            foreach (var type in Candidates())
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                             .Cast<MethodBase>()
                             .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                    {
                        continue;
                    }

                    try
                    {
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                        methods++;
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidProgramException or TypeLoadException or FileNotFoundException)
                    {
                        skipped++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or TypeLoadException or FileNotFoundException)
        {
            skipped++;
        }

        _result = new Result(decoded, methods, skipped, run, time.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>Step 1: decode the sample with the real decoder and reduce it in a reducer nothing else uses.</summary>
    private static bool RunSample(TimeProvider time)
    {
        var state = new GameState();
        var bus = new EventBus(NullLogSink.Instance);
        var reducer = new GameStateReducer(state, bus, new SessionClock(time), NullLogSink.Instance);
        var body = WarmUpMessages.PlayerDescription(0x50000001);
        var result = InboundDispatcher.Dispatch(new InboundContext(0, MessageQueue.Ui, 0), body, reducer);
        return result.Outcome == DispatchOutcome.Decoded && state.Self.Qualities.IsKnown;
    }

    private static IEnumerable<Type> Candidates()
    {
        var assemblies = new[] { typeof(WarmUpMessages).Assembly, typeof(SessionJitWarmUp).Assembly };
        var queue = new Queue<Type>(assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace is { } ns && Namespaces.Any(n => ns.StartsWith(n, StringComparison.Ordinal)) && !t.ContainsGenericParameters));
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            yield return type;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!nested.ContainsGenericParameters)
                {
                    queue.Enqueue(nested);
                }
            }
        }
    }
}

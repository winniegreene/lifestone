using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using Lifestone.Game.Core;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// Compiles the retail UI's code on a worker thread at start-up, so the frame that makes the in-game shell does not pay for the just-in-time compiler (DoD 15, no frame over 50 ms between
/// Enter and walking; docs/session.md section 27). The shell's construction cost about 31 ms the first time and 7 ms the second, the difference being first-use compilation of the panels'
/// classes (chat, vitals, the options and character information pages, the toolbar and the selection display); nothing else of the shell is first-use but its images, which
/// <see cref="ShellWarmUp"/> decodes.
/// </summary>
/// <remarks>
/// It only compiles (<see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>); no static constructor runs and no code of the UI executes, so it is safe beside the main thread. A method the
/// main thread needs while the worker is compiling it waits for that one method, as it would compile it itself. The worker is a low-priority background thread.
/// </remarks>
public static class ShellJitWarmUp
{
    private const string Namespace = "Lifestone.Client.RetailUi";
    private static int _started;
    private static volatile Result? _result;

    /// <summary>What the worker did, or null while it runs (or before <see cref="Start"/>).</summary>
    public static Result? Done => _result;

    /// <summary>The worker's outcome.</summary>
    /// <param name="Methods">Methods compiled.</param>
    /// <param name="Skipped">Methods that could not be prepared (generic definitions, native or abstract ones are not counted: they are not candidates).</param>
    /// <param name="Milliseconds">Wall-clock milliseconds the worker took.</param>
    public sealed record Result(int Methods, int Skipped, double Milliseconds);

    /// <summary>Starts the worker once per process.</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var thread = new Thread(Work) { IsBackground = true, Name = "ShellJitWarmUp", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    /// <summary>Logs the outcome once, from the main thread, when the worker has finished (call from a frame callback).</summary>
    public static void LogWhenDone(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        void Poll()
        {
            if (_result is not { } result)
            {
                return;
            }

            tree.ProcessFrame -= Poll;
            Log.Info("play", "retail UI code compiled in the background", ("methods", result.Methods), ("skipped", result.Skipped), ("ms", result.Milliseconds));
        }

        tree.ProcessFrame += Poll;
    }

    private static void Work()
    {
        var clock = Stopwatch.StartNew();
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
                    if (method.IsAbstract || method.ContainsGenericParameters || (method.GetMethodBody() is null))
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
            skipped++; // the warm-up is best effort: a type that does not load costs the shell the compile it would have had anyway
        }

        _result = new Result(methods, skipped, clock.Elapsed.TotalMilliseconds);
    }

    private static IEnumerable<Type> Candidates()
    {
        var queue = new Queue<Type>(typeof(Lifestone.Client.RetailUi.Flow.UiFlow).Assembly.GetTypes()
            .Where(t => t.Namespace is { } ns && ns.StartsWith(Namespace, StringComparison.Ordinal) && !t.ContainsGenericParameters));
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

using Godot;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// Counts the errors Godot itself logs, in process (M5 item 0 c3, <c>m5-godot-errors-uncounted</c>): Godot 4's <see cref="Logger"/>
/// registered with <see cref="OS.AddLogger"/> sees every error, script error and shader error the engine prints, including a C# exception no
/// code caught. Warnings are not counted, and neither are Lifestone's own <c>Log.Error</c> lines, which reach Godot through <c>GD.PushError</c>:
/// <c>Log</c> brackets that echo with <see cref="GodotErrorTally.Echo"/> and the tally skips it (M5 0 fix 0-R2), so <c>godot_errors</c> means
/// Godot's own errors here and in the smoke. The <c>[soak]</c> line's <c>godot_errors</c> reads <see cref="Count"/>.
/// </summary>
/// <remarks>
/// <see cref="Logger._LogError"/> runs on any thread, so the count is an interlocked long (<see cref="GodotErrorTally"/>). The logger must not call back into Godot's
/// logging (the engine documents the recursion), so it only counts.
/// </remarks>
public partial class GodotErrorLogger : Logger
{
    private static GodotErrorLogger? _instance;

    /// <summary>Errors counted since <see cref="Install"/> (error, script and shader errors; not warnings).</summary>
    public static long Count => GodotErrorTally.Count;

    /// <summary>True once the logger is registered with the engine; false when <see cref="Install"/> failed (the soak line then reads <c>godot_errors=n/a</c>, <c>TODO(m5-soak-godot-errors)</c>).</summary>
    public static bool Installed => _instance is not null;

    /// <summary>Registers the logger once. Main thread, early (<c>Log.Initialize</c>), so start-up errors count. A failure is reported once, as a warning, and leaves <see cref="Installed"/> false.</summary>
    public static void Install()
    {
        if (_instance is not null)
        {
            return;
        }

        try
        {
            var logger = new GodotErrorLogger();
            OS.AddLogger(logger);
            _instance = logger;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[log] cannot register the Godot error counter (TODO(m5-soak-godot-errors)): {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Removes the logger (at shutdown, so the engine does not call into a runtime that is going away).</summary>
    public static void Uninstall()
    {
        if (_instance is not { } logger)
        {
            return;
        }

        _instance = null;
        try
        {
            OS.RemoveLogger(logger);
        }
        catch (Exception)
        {
            // Shutting down: nothing to report to.
        }
    }

    /// <inheritdoc />
    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        GodotErrorTally.Record(errorType == (int)ErrorType.Warning);
    }
}

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// Where the game's log lines are copied (<c>--log-copies</c>, M5 item 0 c3, <c>m5-log-triple</c>). Every Lifestone line goes to
/// <c>lifestone.log</c> in every mode; the copies are the console and Godot's own <c>godot.log</c>.
/// </summary>
public enum LogCopies
{
    /// <summary>The default: <c>lifestone.log</c> and the console (every check reads the console). Godot's <c>godot.log</c> is off.</summary>
    Console,

    /// <summary><c>lifestone.log</c> only (the soak): no console copy of Debug, Info and Warning lines. Errors still reach the console, so none passes unseen.</summary>
    File,

    /// <summary>The three copies, as before M5: <c>lifestone.log</c>, the console and <c>godot.log</c> (tools\godot.ps1 passes Godot's <c>--log-file</c>).</summary>
    All,
}

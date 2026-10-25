using System.Runtime.CompilerServices;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// The count of the errors Godot itself logs (M5 item 0 fix 0-R2): the engine-free half of the game's <c>GodotErrorLogger</c>. Lifestone's own
/// <c>Log.Error</c> lines reach Godot through <c>GD.PushError</c>, and Godot hands every such push back to the logger on the pushing thread,
/// so the echo is bracketed with <see cref="Echo"/> and <see cref="Record"/> skips it: <see cref="Count"/> then means Godot's own errors
/// (the same meaning as the smoke's <c>godot_errors</c>, which excludes the echoed <c>ERROR: &lt;utc&gt;Z ERROR [</c> lines).
/// </summary>
/// <remarks>
/// <see cref="Record"/> may run on any thread (an interlocked long); the echo flag is thread-static, so another thread's real error is still
/// counted while one thread echoes. Nothing here allocates: <see cref="Echo"/> returns a struct.
/// </remarks>
public static class GodotErrorTally
{
    [ThreadStatic]
    private static int _echoDepth;

    private static long _count;

    /// <summary>Godot's own errors recorded (error, script and shader errors; not warnings, not Lifestone's echoes).</summary>
    public static long Count => Interlocked.Read(ref _count);

    /// <summary>True while this thread is inside an <see cref="Echo"/> scope.</summary>
    public static bool Echoing => _echoDepth > 0;

    /// <summary>Brackets a <c>GD.PushError</c> that echoes a Lifestone log line: <c>using (GodotErrorTally.Echo()) { GD.PushError(line); }</c>.</summary>
    /// <returns>The scope (a struct, so no allocation).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EchoScope Echo()
    {
        _echoDepth++;
        return default;
    }

    /// <summary>Records one logger callback: counted unless it is a warning or this thread is echoing a Lifestone line.</summary>
    /// <param name="isWarning">True for Godot's warning type.</param>
    /// <returns>True when it was counted.</returns>
    public static bool Record(bool isWarning)
    {
        if (isWarning || _echoDepth > 0)
        {
            return false;
        }

        Interlocked.Increment(ref _count);
        return true;
    }

    /// <summary>The echo scope; disposing it leaves the echo.</summary>
    public readonly struct EchoScope : IDisposable
    {
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_echoDepth > 0)
            {
                _echoDepth--;
            }
        }
    }
}

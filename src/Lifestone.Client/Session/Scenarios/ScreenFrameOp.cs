using System.Globalization;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// A screen operation written as a frame-stepped coroutine (the game's <c>ThrowawayScreens</c>, <see cref="ClimberChargenDriver"/>): the body yields an <see cref="int"/> to wait that many frames, or an
/// <see cref="Until"/> to wait for a condition (with a limit on the session clock and what it waited for), and ends the operation with <see cref="Succeed"/> or <see cref="Abort"/>.
/// Moved from the game project (M6 onscreen start) so the engine-free drivers and their tests share it; the behaviour is unchanged.
/// </summary>
public sealed class ScreenFrameOp : ScreenOp
{
    private readonly IEnumerator<object?> _steps;
    private int _frames;
    private Until? _until;
    private double _untilSince = double.NaN;

    /// <summary>A condition to wait for.</summary>
    /// <param name="Condition">True when the wait is over.</param>
    /// <param name="Seconds">The most it may take, in session-clock seconds.</param>
    /// <param name="What">What the operation waited for (the failure's words).</param>
    public readonly record struct Until(Func<bool> Condition, double Seconds, string What);

    /// <summary>An operation whose steps are <paramref name="body"/>.</summary>
    /// <param name="body">The coroutine; it gets the operation to end it.</param>
    public ScreenFrameOp(Func<ScreenFrameOp, IEnumerable<object?>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _steps = body(this).GetEnumerator();
    }

    /// <summary>Ends the operation well.</summary>
    /// <param name="text">What it found, or null.</param>
    /// <param name="number">What it measured, or NaN.</param>
    public void Succeed(string? text = null, double number = double.NaN) => Complete(text, number);

    /// <summary>Ends the operation with a failure.</summary>
    /// <param name="why">The reason.</param>
    public void Abort(string why) => Fail(why);

    /// <inheritdoc/>
    public override void Advance(double now)
    {
        if (Done)
        {
            return;
        }

        if (_until is { } wait)
        {
            if (double.IsNaN(_untilSince))
            {
                _untilSince = now;
            }

            bool met;
            try
            {
                met = wait.Condition();
            }
            catch (Exception ex)
            {
                Fail($"waiting for {wait.What}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (!met)
            {
                if (now - _untilSince > wait.Seconds)
                {
                    Fail(string.Create(CultureInfo.InvariantCulture, $"timed out after {wait.Seconds:0} s waiting for {wait.What}"));
                }

                return;
            }

            _until = null;
            _untilSince = double.NaN;
        }

        if (_frames > 0)
        {
            _frames--;
            return;
        }

        try
        {
            if (!_steps.MoveNext())
            {
                Complete();
                return;
            }
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        switch (_steps.Current)
        {
            case int frames:
                _frames = Math.Max(0, frames - 1);
                break;
            case Until until:
                _until = until;
                break;
        }
    }
}

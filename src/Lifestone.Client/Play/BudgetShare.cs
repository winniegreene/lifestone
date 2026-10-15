using System.Globalization;
using System.Text;

namespace Lifestone.Client.Play;

/// <summary>
/// The frame-budget table's arithmetic (docs/game-core.md §1.2): the main-thread queue's budget less the shares other
/// systems take from it, down to a floor, so attach work never starves while the portal tunnel shows or the session pump
/// runs. Engine-agnostic; the Godot <c>FrameBudgetShare</c> node owns one and sets the queue's budget from
/// <see cref="EffectiveMilliseconds"/> once per frame.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> <c>effective = min(base, max(floor, base - shares))</c>: shares are subtracted from the
/// configured budget (<c>--budget-ms</c>, 4 ms by default) and the result is never below the 1 ms floor
/// (<see cref="FloorMilliseconds"/>), but a base the owner set below the floor is left alone (the floor never raises it).
/// </para>
/// <para><b>Shares.</b> A share is a named amount of milliseconds, set by whoever takes the time and removed (set to 0) when
/// it stops: the portal tunnel while it shows, the session pump with <c>--session-budget-ms</c>. Setting a name again
/// replaces its amount. The retail UI's composite is not a share: it has its own 1 ms slice and is not subtracted.</para>
/// <para>Frame thread only; allocates only when a share is first added.</para>
/// </remarks>
public sealed class BudgetShare
{
    /// <summary>The least the queue is ever left with, in milliseconds (the plan's 1 ms floor).</summary>
    public const double FloorMilliseconds = 1.0;

    private readonly List<(string Name, double Milliseconds)> _shares = [];
    private double _baseMilliseconds;

    /// <summary>Creates the arbiter for a configured budget.</summary>
    /// <param name="baseMilliseconds">The queue's configured budget (<c>--budget-ms</c>).</param>
    /// <exception cref="ArgumentOutOfRangeException">The budget is negative or not a number.</exception>
    public BudgetShare(double baseMilliseconds)
    {
        BaseMilliseconds = baseMilliseconds;
    }

    /// <summary>The queue's configured budget, in milliseconds.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a number.</exception>
    public double BaseMilliseconds
    {
        get => _baseMilliseconds;
        set => _baseMilliseconds = Check(value, nameof(BaseMilliseconds));
    }

    /// <summary>The shares taken, in the order they were first set; every amount is above 0.</summary>
    public IReadOnlyList<(string Name, double Milliseconds)> Shares => _shares;

    /// <summary>The sum of the shares, in milliseconds.</summary>
    public double SharesMilliseconds
    {
        get
        {
            var sum = 0.0;
            for (var i = 0; i < _shares.Count; i++)
            {
                sum += _shares[i].Milliseconds;
            }

            return sum;
        }
    }

    /// <summary>The budget the queue may use this frame, in milliseconds (see the remarks).</summary>
    public double EffectiveMilliseconds => Math.Min(_baseMilliseconds, Math.Max(FloorMilliseconds, _baseMilliseconds - SharesMilliseconds));

    /// <summary><see cref="EffectiveMilliseconds"/> as a time span.</summary>
    public TimeSpan Effective => TimeSpan.FromMilliseconds(EffectiveMilliseconds);

    /// <summary>True when shares cut the budget below its configured value this frame.</summary>
    public bool IsReduced => EffectiveMilliseconds < _baseMilliseconds;

    /// <summary>The amount a share takes now, or 0 when it takes none.</summary>
    public double Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        for (var i = 0; i < _shares.Count; i++)
        {
            if (string.Equals(_shares[i].Name, name, StringComparison.Ordinal))
            {
                return _shares[i].Milliseconds;
            }
        }

        return 0.0;
    }

    /// <summary>Sets what share <paramref name="name"/> takes; 0 releases it. A name set again replaces its amount.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The amount is negative or not a number.</exception>
    public void Set(string name, double milliseconds)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Check(milliseconds, nameof(milliseconds));
        for (var i = 0; i < _shares.Count; i++)
        {
            if (!string.Equals(_shares[i].Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (milliseconds == 0.0)
            {
                _shares.RemoveAt(i);
            }
            else
            {
                _shares[i] = (name, milliseconds);
            }

            return;
        }

        if (milliseconds > 0.0)
        {
            _shares.Add((name, milliseconds));
        }
    }

    /// <summary>One line for the F3 overlay and the log: <c>queue 1.0 ms of 4.0 (tunnel 1.0, session 2.0)</c>.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"queue {EffectiveMilliseconds:0.0} ms of {_baseMilliseconds:0.0}");
        if (_shares.Count > 0)
        {
            text.Append(" (");
            for (var i = 0; i < _shares.Count; i++)
            {
                text.Append(i == 0 ? string.Empty : ", ").Append(_shares[i].Name).Append(CultureInfo.InvariantCulture, $" {_shares[i].Milliseconds:0.0}");
            }

            text.Append(')');
        }

        return text.ToString();
    }

    private static double Check(double value, string parameter) =>
        double.IsFinite(value) && value >= 0.0
            ? value
            : throw new ArgumentOutOfRangeException(parameter, value, "A budget or a share is a finite number of milliseconds, 0 or more.");
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Lifestone.Client.Session.Contracts;

/// <summary>What a heal did (<see cref="HealResult.Kind"/>).</summary>
public enum HealResultKind
{
    /// <summary>A healing kit's roll won: the vital rose by <see cref="HealResult.Amount"/> (<c>You [expertly ]heal yourself for N Health points. Your Handy Healing Kit has M uses left.</c>).</summary>
    Healed,

    /// <summary>A healing kit's roll lost: a use was spent and nothing healed (<c>You fail to heal yourself. Your Handy Healing Kit has M uses left.</c>). ACE spends the use first, win or lose.</summary>
    FailedRoll,

    /// <summary>A potion or food restored <see cref="HealResult.Amount"/> points (<c>The Potion of Healing restores N points of your Health.</c>).</summary>
    Drank,

    /// <summary>A potion or food restored nothing: it was drunk at a full vital and spent (<c>The Potion of Healing restores 0 points of your Health.</c>; capture.md C3).</summary>
    Wasted,
}

/// <summary>
/// What a heal's server lines said, read from the F7E0 chat line (<see cref="TryParse"/>), plus the stack or the uses the model saw (the trackers fill <see cref="Left"/> when the line has none). The three line shapes are ACE's
/// (<c>Healer.DoHealing</c>, <c>Food.ApplyBoost</c>, ACE 95ef426d), confirmed live in M4 3.1's capture. A heal of another player ("You heal Bob for ...") is not read (<c>TODO(m4-heal-other-target)</c>: the hunt heals itself).
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Amount">The points restored (0 for <see cref="HealResultKind.FailedRoll"/> and <see cref="HealResultKind.Wasted"/>).</param>
/// <param name="Vital">The vital's name (<c>Health</c>, <c>Stamina</c>, <c>Mana</c>); empty for a failed roll, whose line names none.</param>
/// <param name="Left">The uses left on a kit or the stack left of a potion; <see cref="Unknown"/> when neither the line nor the model said.</param>
/// <param name="Critical">True for ACE's critical heal ("expertly").</param>
public sealed record HealResult(HealResultKind Kind, int Amount, string Vital, int Left = HealResult.Unknown, bool Critical = false)
{
    /// <summary><see cref="Left"/> when it is not known.</summary>
    public const int Unknown = -1;

    /// <summary>True when the heal spent a use or a potion and changed the vital (<see cref="HealResultKind.Healed"/>, <see cref="HealResultKind.Drank"/>).</summary>
    public bool Restored => Kind is HealResultKind.Healed or HealResultKind.Drank;

    /// <summary>
    /// Reads one chat line. True for the kit's two lines and the potion's one; false for any other text. Nothing is allocated but the result (the three vitals' names are literals).
    /// </summary>
    /// <param name="text">The F7E0 line.</param>
    /// <param name="result">The result, or null.</param>
    public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out HealResult? result)
    {
        result = null;
        if (text.StartsWith("You ", StringComparison.Ordinal))
        {
            return TryParseKit(text[4..], out result);
        }

        return text.StartsWith("The ", StringComparison.Ordinal) && TryParsePotion(text[4..], out result);
    }

    private static bool TryParseKit(ReadOnlySpan<char> rest, out HealResult? result)
    {
        result = null;
        const string Fail = "fail to heal yourself.";
        if (rest.StartsWith(Fail, StringComparison.Ordinal))
        {
            result = new HealResult(HealResultKind.FailedRoll, 0, string.Empty, ReadUsesLeft(rest[Fail.Length..]));
            return true;
        }

        const string Expertly = "expertly ";
        var critical = rest.StartsWith(Expertly, StringComparison.Ordinal);
        if (critical)
        {
            rest = rest[Expertly.Length..];
        }

        const string Heal = "heal yourself for ";
        if (!rest.StartsWith(Heal, StringComparison.Ordinal))
        {
            return false;
        }

        rest = rest[Heal.Length..];
        if (!ReadNumber(ref rest, out var amount) || !rest.StartsWith(" ", StringComparison.Ordinal))
        {
            return false;
        }

        rest = rest[1..];
        var space = rest.IndexOf(' ');
        if (space <= 0)
        {
            return false;
        }

        var vital = rest[..space];
        rest = rest[space..];
        const string Points = " points.";
        if (!rest.StartsWith(Points, StringComparison.Ordinal))
        {
            return false;
        }

        result = new HealResult(HealResultKind.Healed, amount, VitalName(vital), ReadUsesLeft(rest[Points.Length..]), critical);
        return true;
    }

    private static bool TryParsePotion(ReadOnlySpan<char> rest, out HealResult? result)
    {
        result = null;
        const string Verb = " restores ";
        var at = rest.IndexOf(Verb, StringComparison.Ordinal);
        if (at <= 0)
        {
            return false;
        }

        rest = rest[(at + Verb.Length)..];
        const string Middle = " points of your ";
        if (!ReadNumber(ref rest, out var amount) || !rest.StartsWith(Middle, StringComparison.Ordinal))
        {
            return false;
        }

        rest = rest[Middle.Length..];
        if (rest.Length < 2 || rest[^1] != '.')
        {
            return false;
        }

        result = new HealResult(amount > 0 ? HealResultKind.Drank : HealResultKind.Wasted, amount, VitalName(rest[..^1]));
        return true;
    }

    /// <summary>The kit line's tail: <c> Your X has N uses left.</c> (N), <c> Your X has 1 use left.</c> (1), <c> Your X is used up.</c> (0), nothing (<see cref="Unknown"/>).</summary>
    private static int ReadUsesLeft(ReadOnlySpan<char> tail)
    {
        tail = tail.Trim();
        if (tail.IsEmpty)
        {
            return Unknown;
        }

        if (tail.EndsWith(" is used up.", StringComparison.Ordinal))
        {
            return 0;
        }

        var has = tail.LastIndexOf(" has ", StringComparison.Ordinal);
        if (has < 0)
        {
            return Unknown;
        }

        var number = tail[(has + 5)..];
        return ReadNumber(ref number, out var left) && number.StartsWith(" use", StringComparison.Ordinal) ? left : Unknown;
    }

    /// <summary>Reads the digits at the start of <paramref name="text"/> and moves past them.</summary>
    private static bool ReadNumber(ref ReadOnlySpan<char> text, out int value)
    {
        var digits = 0;
        while (digits < text.Length && text[digits] is >= '0' and <= '9')
        {
            digits++;
        }

        value = 0;
        if (digits == 0 || !int.TryParse(text[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        text = text[digits..];
        return true;
    }

    private static string VitalName(ReadOnlySpan<char> name) =>
        name.SequenceEqual("Health") ? "Health" : name.SequenceEqual("Stamina") ? "Stamina" : name.SequenceEqual("Mana") ? "Mana" : name.ToString();
}

/// <summary>
/// The handle of <see cref="IActionApi.UseWithTarget"/> and <see cref="IActionApi.Drink"/>: an <see cref="ActionHandle"/> plus what the heal's lines said. <see cref="Result"/> is set when the use
/// was confirmed and its line was read, before <see cref="ActionHandle.Completion"/> completes; it stays null for every other outcome.
/// </summary>
public sealed class HealHandle : ActionHandle
{
    /// <summary>A queued heal handle. Only the session creates handles.</summary>
    internal HealHandle(long id, ActionKind kind, ActionSource source, string summary, double queuedAt)
        : base(id, kind, source, summary, queuedAt)
    {
    }

    /// <summary>What the heal's line said, or null (not confirmed, or a confirmed use whose line was not seen).</summary>
    public HealResult? Result { get; private set; }

    /// <summary>Completes the handle with <paramref name="outcome"/>, setting <see cref="Result"/> first, unless it already completed (see <see cref="ActionHandle.TryComplete"/>).</summary>
    internal bool TryComplete(HealResult? result, ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError)
    {
        if (IsCompleted)
        {
            return false;
        }

        Result = result;
        return TryComplete(outcome, time, onHandlerError);
    }
}

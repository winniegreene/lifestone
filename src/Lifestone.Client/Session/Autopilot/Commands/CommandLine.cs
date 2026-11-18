namespace Lifestone.Client.Session.Autopilot.Commands;

/// <summary>What she does when an order's work has ended.</summary>
public enum CommandThen
{
    /// <summary>The run goes on with what it was doing, its counts kept (the default).</summary>
    Resume,

    /// <summary>She recalls home and holds (<c>then idle</c>): the run ends cleanly and the game stays open.</summary>
    Idle,
}

/// <summary>One parsed command line.</summary>
/// <param name="Name">The command's name as typed, lower case, without the slash.</param>
/// <param name="Args">Its arguments, as typed.</param>
/// <param name="Then">What she does after (a trailing <c>then idle</c> or <c>then resume</c>).</param>
/// <param name="Text">The whole line, trimmed, without the slash.</param>
public sealed record CommandRequest(string Name, IReadOnlyList<string> Args, CommandThen Then, string Text)
{
    /// <summary>The arguments joined by single spaces.</summary>
    public string ArgText => string.Join(' ', Args);
}

/// <summary>
/// The one parser both ways in use (the chat's <c>/name args</c> and the steer desk's <c>command</c> order): a leading slash is dropped, the line is split on spaces and tabs, the first word is the command (case-blind), and a trailing
/// <c>then idle</c> or <c>then resume</c> says what she does after. Anything else is a named error.
/// </summary>
public static class CommandLine
{
    /// <summary>The most characters a command line may have.</summary>
    public const int MaxLength = 200;

    /// <summary>Reads one line.</summary>
    /// <param name="text">The line.</param>
    /// <param name="request">The request, or null when refused.</param>
    /// <param name="error">The named error (<c>empty</c>, <c>too-long</c>, <c>bad-then</c>), or null.</param>
    public static bool TryParse(string? text, out CommandRequest? request, out string? error)
    {
        request = null;
        var t = (text ?? string.Empty).Trim();
        if (t.StartsWith('/'))
        {
            t = t[1..].TrimStart();
        }

        if (t.Length == 0)
        {
            error = "empty";
            return false;
        }

        if (t.Length > MaxLength)
        {
            error = "too-long";
            return false;
        }

        if (t.Any(c => char.IsControl(c) && c != '	'))
        {
            error = "bad-characters";
            return false;
        }

        var words = t.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
        var then = CommandThen.Resume;
        var thenAt = words.FindIndex(w => w.Equals("then", StringComparison.OrdinalIgnoreCase));
        if (thenAt > 0)
        {
            if (thenAt != words.Count - 2)
            {
                error = "bad-then";
                return false;
            }

            switch (words[^1].ToLowerInvariant())
            {
                case "idle":
                    then = CommandThen.Idle;
                    break;
                case "resume":
                    break;
                default:
                    error = "bad-then";
                    return false;
            }

            words.RemoveRange(thenAt, 2);
        }

        request = new CommandRequest(words[0].ToLowerInvariant(), words.Skip(1).ToArray(), then, t);
        error = null;
        return true;
    }
}

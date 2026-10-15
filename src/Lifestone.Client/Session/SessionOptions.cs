using System.Net;
using Lifestone.Client.Options;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;

namespace Lifestone.Client.Session;

/// <summary>
/// The Session option group's values (docs/milestones/m2-plan.md §5.4; registered with their help in
/// <c>OptionCatalog.Session</c>), as the game's options and the headless host parse them (<see cref="Parse(IEnumerable{string})"/>)
/// and <see cref="SessionBootstrap.CreateLive"/> takes them.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--play</term><description>Play mode: log in instead of exploring (owner decision D9; tools\play.ps1 passes it; the headless host always logs in).</description></item>
/// <item><term>--account=NAME</term><description>A key of secrets.json's <c>accounts</c>, else a username (D10). A name only.</description></item>
/// <item><term>--character=NAME</term><description>Enter the world with this character instead of stopping at the list.</description></item>
/// <item><term>--server=IP:PORT</term><description>ACE's login endpoint: a loopback IP literal (default 127.0.0.1:9000).</description></item>
/// <item><term>--record-session[=DIR]</term><description>Record the session (D48), to DIR or cache/sessions.</description></item>
/// <item><term>--session-budget-ms=X</term><description>Per-tick dispatch budget in milliseconds (default 2).</description></item>
/// <item><term>--retail-net</term><description>Retail's network behaviour where Lifestone deviates (D18).</description></item>
/// </list>
/// <para>No option takes a secret: <see cref="Account"/> names an entry of server\runtime\secrets.json, which the host
/// reads as bytes. The parser never repeats an <c>--account</c> or <c>--character</c> value in an error (a mistyped
/// option could have been given a password).</para>
/// </remarks>
public sealed record SessionOptions
{
    /// <summary>The default login endpoint of the bundled ACE: 127.0.0.1:9000 (server/versions.json network).</summary>
    public const int DefaultPort = 9000;

    /// <summary>The default per-tick dispatch budget, in milliseconds (the frame-budget table's session share).</summary>
    public const double DefaultBudgetMs = 2.0;

    /// <summary>The largest per-tick budget accepted, in milliseconds.</summary>
    public const double MaxBudgetMs = 100.0;

    /// <summary>The longest <c>--character</c> name accepted.</summary>
    public const int MaxCharacterNameLength = 64;

    /// <summary>The default folder for recordings, relative to the repository root (gitignored).</summary>
    public const string DefaultRecordDirectory = "cache/sessions";

    /// <summary>The longest account name ACE accepts (net's <see cref="AcCredentials.MaxAccountLength"/>).</summary>
    private const int AcCredentialsLimit = AcCredentials.MaxAccountLength;

    /// <summary>The play switch: log in instead of exploring (owner decision D9).</summary>
    public bool Play { get; init; }

    /// <summary>The account to log in with: a key of secrets.json's <c>accounts</c>, else a username; null for its
    /// <c>defaultAccount</c> (owner decision D10). A name only, never a password.</summary>
    public string? Account { get; init; }

    /// <summary>The character to enter automatically, by name; null to stop at the character list.</summary>
    public string? Character { get; init; }

    /// <summary>
    /// ACE's login endpoint, an IP literal on loopback (the world port is Port + 1). The session gate refuses anything
    /// else before a socket exists. A fresh copy per options instance.
    /// </summary>
    public IPEndPoint Server { get; init; } = new(IPAddress.Loopback, DefaultPort);

    /// <summary>True to record the session (owner decision D48: only on request; the headless host always records).</summary>
    public bool RecordSession { get; init; }

    /// <summary>Where the host writes recordings; null for its default (<see cref="DefaultRecordDirectory"/>).</summary>
    public string? RecordDirectory { get; init; }

    /// <summary>How long each tick may spend dispatching server messages, in milliseconds (the transport sweeps always run).</summary>
    public double BudgetMs { get; init; } = DefaultBudgetMs;

    /// <summary>
    /// True restores retail's network behaviour where Lifestone deviates (owner decision D18): the 140 s silence
    /// timeout, a single login attempt, no tail probe, arrival-order delivery.
    /// </summary>
    public bool RetailNet { get; init; }

    /// <summary>The problems <see cref="Parse(IEnumerable{string})"/> found, one line each; the option keeps its default.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Parses the Session group from the user arguments with the shared tokenizer (<see cref="CommandLineArgument"/>);
    /// other arguments are ignored here (the registry reports unknown ones). A value that does not parse is added to
    /// <see cref="Errors"/> and the option keeps its default.
    /// </summary>
    /// <param name="args">The user arguments (after <c>--</c>).</param>
    public static SessionOptions Parse(IEnumerable<string> args) => FromArguments(CommandLineArgument.ParseAll(args));

    /// <summary>Parses the Session group from arguments already split (see <see cref="Parse(IEnumerable{string})"/>).</summary>
    /// <param name="args">The user arguments.</param>
    public static SessionOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new SessionOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            switch (argument.Name)
            {
                case "--play":
                    o = o with { Play = OptionValues.Switch(argument, errors, o.Play) };
                    break;
                case "--account":
                    o = o with { Account = Name(argument, errors, AcCredentialsLimit, "an account key or username", allowSpaces: false) ?? o.Account };
                    break;
                case "--character":
                    o = o with { Character = Name(argument, errors, MaxCharacterNameLength, "a character name", allowSpaces: true) ?? o.Character };
                    break;
                case "--server":
                    o = o with { Server = ServerEndPoint(argument, errors) ?? o.Server };
                    break;
                case "--record-session":
                    if (argument.HasValue && string.IsNullOrWhiteSpace(argument.Value))
                    {
                        errors.Add($"{argument.Name}: the folder is empty (give --record-session alone for {DefaultRecordDirectory})");
                    }
                    else
                    {
                        o = o with { RecordSession = true, RecordDirectory = argument.HasValue ? argument.Value : o.RecordDirectory };
                    }

                    break;
                case "--session-budget-ms":
                    var known = errors.Count;
                    var budget = OptionValues.Double(argument, errors, o.BudgetMs);
                    if (errors.Count == known && budget is > 0 and <= MaxBudgetMs)
                    {
                        o = o with { BudgetMs = budget };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not more than 0 and at most {MaxBudgetMs} ms");
                    }

                    break;
                case "--retail-net":
                    o = o with { RetailNet = OptionValues.Switch(argument, errors, o.RetailNet) };
                    break;
            }
        }

        return o with { Errors = errors };
    }

    /// <summary>
    /// A name option's value: printable ASCII, 1 to <paramref name="maxLength"/> characters. On failure the error never
    /// repeats the value.
    /// </summary>
    private static string? Name(CommandLineArgument argument, List<string> errors, int maxLength, string what, bool allowSpaces)
    {
        var value = argument.Value.Trim();
        if (value.Length == 0)
        {
            errors.Add($"{argument.Name}: needs {what} (--name=VALUE)");
            return null;
        }

        if (value.Length > maxLength || !value.All(c => c is > ' ' and <= '~' || (allowSpaces && c == ' ')))
        {
            errors.Add($"{argument.Name}: the value is not {what} (1 to {maxLength} printable ASCII characters{(allowSpaces ? string.Empty : ", no spaces")}); it is not shown");
            return null;
        }

        return value;
    }

    /// <summary><c>--server</c>: a loopback IP literal and a port (net's <see cref="LoopbackPolicy.TryParse"/>), not the last port.</summary>
    private static IPEndPoint? ServerEndPoint(CommandLineArgument argument, List<string> errors)
    {
        if (!LoopbackPolicy.TryParse(argument.Value, out var endPoint, out var error))
        {
            errors.Add($"{argument.Name}: {error}");
            return null;
        }

        if (endPoint.Port >= IPEndPoint.MaxPort)
        {
            errors.Add($"{argument.Name}: the port must be at most {IPEndPoint.MaxPort - 1} (ACE's world port is the login port + 1)");
            return null;
        }

        return endPoint;
    }
}

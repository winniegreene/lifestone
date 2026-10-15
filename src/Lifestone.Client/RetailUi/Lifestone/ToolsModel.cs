using System.Globalization;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.RetailUi.LifestonePanels;

/// <summary>What the tools can do right now, as the panel decides whether to open (<see cref="ToolsModel.Gate"/>).</summary>
public enum ToolsGate
{
    /// <summary>Usable (the mod answered, or has not been asked yet).</summary>
    Open,

    /// <summary>The server is not on loopback: the tools never work against it.</summary>
    NotLoopback,

    /// <summary>The server does not have the Lifestone mod loaded.</summary>
    ModMissing,

    /// <summary>The player is not in the world, so there is no character to run a tool for.</summary>
    NotInWorld,
}

/// <summary>
/// What the tools panel does, without any element in sight (docs/retail-ui.md section 22.1): the Places search and its pages, and the three tools (teleport, Where am I, Add XP) as
/// calls on the session's <see cref="PlayerTools"/> with <see cref="ActionSource.Ui"/>, so every use goes through the Action API and its rate limit, is written to the action log, and
/// writes its request line to the chat (<c>PlayerTools</c> adds it). The kill switch and dry run gate automation (<see cref="ActionSource.Automation"/>) only: a player's own click in this
/// panel is not automation and is not stopped by them (docs/retail-ui.md section 22.1). This class adds what the panel shows after a use: the result text, and a chat line for what the server's own reply line
/// does not say (the agreement of the server's position with the client's, a refusal or a silence). Game thread only.
/// </summary>
/// <remarks>The retail UI reaches the session only through <c>Session.Contracts</c> (<c>RetailUiArchitectureTests</c>): the position comparison Where am I needs comes across as data
/// (<see cref="WhereResult.Compare"/>). The panel (<see cref="ToolsPanel"/>) is the only thing that knows an element.</remarks>
public sealed class ToolsModel
{
    /// <summary>The rows the Places list shows at once.</summary>
    public const int PageSize = 8;

    /// <summary>The most experience one Add XP asks for (the mod's limit).</summary>
    public const long MaxXp = 999_999_999_999;

    private readonly PlayerTools _tools;
    private TeleportCatalog? _catalog;
    private readonly Action<string> _chat;
    private IReadOnlyList<TeleportDestination>? _results;
    private ToolHandle<ToolStatus>? _probe;
    private readonly Action<string>? _logError;
    private bool _detached;
    private bool _noBodyLogged;

    /// <summary>The model of <paramref name="tools"/>.</summary>
    /// <param name="tools">The session's tools (<see cref="IGameSessionView.Tools"/>).</param>
    /// <param name="clientPosition">The client's physics body position, for Where am I's agreement (null: the host has no body; a function that returns null: the body has no position yet).</param>
    /// <param name="chat">Shows a line in the chat (a local line).</param>
    /// <param name="catalog">The places (default: <see cref="TeleportCatalog.Default"/>: ACE's points of interest and the curated dungeons and interiors).</param>
    /// <param name="logError">Logs an error line (a named TODO); null for none.</param>
    public ToolsModel(PlayerTools tools, Func<CellFrame?>? clientPosition, Action<string> chat, TeleportCatalog? catalog = null, Action<string>? logError = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(chat);
        _tools = tools;
        ClientPosition = clientPosition;
        _chat = chat;
        _logError = logError;
        _catalog = catalog; // the default catalogue is built on first use (it costs a few milliseconds, which the frame that builds the shell must not pay)
    }

    /// <summary>The places (<see cref="TeleportCatalog.Default"/> unless one was given).</summary>
    public TeleportCatalog Catalog => _catalog ??= TeleportCatalog.Default();

    /// <summary>
    /// The client's own position, which Where am I compares with the server's: the physics body's (play mode sets the player's walker body through the panel; the session state's position is the
    /// server's own and would compare the server with itself). Null: the host has no body (the offline shell without a play session), which Where am I says on screen, in the chat and in the log
    /// (<c>TODO(g7-where-client-body)</c>). A function that returns null: the body is not placed yet.
    /// </summary>
    public Func<CellFrame?>? ClientPosition { get; set; }

    /// <summary>The search text.</summary>
    public string Query { get; private set; } = string.Empty;

    /// <summary>Every place the query matches, best first (all of them for an empty query).</summary>
    public IReadOnlyList<TeleportDestination> Results => _results ??= Catalog.Search(Query);

    /// <summary>The index of the first row shown.</summary>
    public int Top { get; private set; }

    /// <summary>The rows shown: up to <see cref="PageSize"/> results from <see cref="Top"/>.</summary>
    public IReadOnlyList<TeleportDestination> Page => [.. Results.Skip(Top).Take(PageSize)];

    /// <summary>The result of the last Where am I, as the panel shows it; empty before one.</summary>
    public string WhereText { get; private set; } = string.Empty;

    /// <summary>The line under the tools: what the last use did, or why it did not.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>The last teleport asked for, or null.</summary>
    public ToolHandle<PlaceResult>? LastPlace { get; private set; }

    /// <summary>The last Where am I asked for, or null.</summary>
    public ToolHandle<WhereResult>? LastWhere { get; private set; }

    /// <summary>The last Add XP asked for, or null.</summary>
    public ToolHandle<XpResult>? LastXp { get; private set; }

    /// <summary>The server's position and the client's, as the last confirmed Where am I found them, or null.</summary>
    public (CellFrame Server, CellFrame? Client, double? Metres, bool Agrees)? LastAgreement { get; private set; }

    /// <summary>Raised when the rows, the status or the where text changed.</summary>
    public event Action? Changed;

    /// <summary>What the tools can do now (<see cref="PlayerTools.Availability"/>).</summary>
    public ToolsGate Gate => _tools.Availability switch
    {
        ToolAvailability.NotLoopback => ToolsGate.NotLoopback,
        ToolAvailability.ModMissing => ToolsGate.ModMissing,
        ToolAvailability.NotInWorld => ToolsGate.NotInWorld,
        _ => ToolsGate.Open,
    };

    /// <summary>
    /// The status probe (<see cref="PlayerTools.Status"/>, silent): asked when the panel opens and the mod has not told its status yet, so a server without the mod is found out now
    /// (<see cref="ToolAvailability.ModMissing"/>). The session probes once on entering the world; this covers a probe lost or never sent.
    /// </summary>
    public void ProbeIfNeeded()
    {
        if (_tools.LastStatus is null && Gate == ToolsGate.Open && _probe is not { IsCompleted: false })
        {
            _probe = _tools.Status(ActionSource.Ui);
        }
    }

    /// <summary>Searches the places (<see cref="TeleportCatalog.Search"/>) and shows the first page.</summary>
    public void SetQuery(string? query)
    {
        var text = query ?? string.Empty;
        if (text == Query)
        {
            return;
        }

        Query = text;
        _results = Catalog.Search(text);
        Top = 0;
        Changed?.Invoke();
    }

    /// <summary>Shows the next page (<paramref name="down"/>) or the previous one. False when there is no such page.</summary>
    public bool Scroll(bool down)
    {
        var top = down ? Math.Min(Top + PageSize, Math.Max(0, Results.Count - PageSize)) : Math.Max(0, Top - PageSize);
        if (top == Top)
        {
            return false;
        }

        Top = top;
        Changed?.Invoke();
        return true;
    }

    /// <summary>"1-8 of 123" (or "no places match").</summary>
    public string Counter => Results.Count == 0
        ? "no places match"
        : string.Create(CultureInfo.InvariantCulture, $"{Top + 1}-{Math.Min(Top + PageSize, Results.Count)} of {Results.Count}");

    /// <summary>
    /// Teleports to <paramref name="destination"/> (<see cref="PlayerTools.Place"/>: an ACE point of interest by its name, a curated place by its location). Portal space follows the server's
    /// teleport as for any other.
    /// </summary>
    public ToolHandle<PlaceResult> Teleport(TeleportDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var handle = _tools.Place(destination, ActionSource.Ui);
        LastPlace = handle;
        SetStatus("Teleporting to " + destination.Label + "...");
        Watch(handle, "teleport to " + destination.Label, done => SetStatus(done.Result is { } r ? "Teleport started: " + destination.Label + " (cell 0x" + r.Destination.CellId.ToString("X8", CultureInfo.InvariantCulture) + ")" : "Teleport started."));
        return handle;
    }

    /// <summary>Asks the server where the player is (<see cref="PlayerTools.WhereAmI"/>) and compares it with the client's own position (<see cref="WhereResult.Compare"/>).</summary>
    public ToolHandle<WhereResult> WhereAmI()
    {
        var handle = _tools.WhereAmI(ActionSource.Ui);
        LastWhere = handle;
        WhereText = "asking the server...";
        Changed?.Invoke();
        Watch(handle, "ask where you are", OnWhere);
        return handle;
    }

    /// <summary>Parses an amount typed in the field: digits, with commas and spaces ignored, from 1 to <see cref="MaxXp"/>.</summary>
    public static bool TryParseAmount(string? text, out long amount)
    {
        amount = 0;
        var digits = new string((text ?? string.Empty).Where(c => c is not (',' or ' ' or '_')).ToArray());
        if (digits.Length > 0 && digits.All(char.IsAsciiDigit) && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= MaxXp)
        {
            amount = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Adds the experience typed in the field (<see cref="PlayerTools.AddXp"/>); null with a status line when the text is not an amount.</summary>
    public ToolHandle<XpResult>? AddXp(string? amountText)
    {
        if (!TryParseAmount(amountText, out var amount))
        {
            SetStatus("Type an amount of experience from 1 to " + MaxXp.ToString("N0", CultureInfo.InvariantCulture) + ".");
            return null;
        }

        var handle = _tools.AddXp(amount, ActionSource.Ui);
        LastXp = handle;
        SetStatus("Asking for " + amount.ToString("N0", CultureInfo.InvariantCulture) + " experience...");
        Watch(handle, "add experience", done => SetStatus(done.Result is { } r
            ? string.Create(CultureInfo.InvariantCulture, $"Added {r.Added:N0} experience; total {r.Total:N0}, level {r.Level}.")
            : "Experience added."));
        return handle;
    }

    /// <summary>Sets the status line (a panel's own message, for example that a dialog was shown).</summary>
    public void SetStatus(string text)
    {
        Status = text;
        Changed?.Invoke();
    }

    private void Watch<T>(ToolHandle<T> handle, string what, Action<ToolHandle<T>> succeeded)
        where T : class
    {
        void Done(ActionHandle completed)
        {
            if (_detached)
            {
                return;
            }

            var outcome = handle.Outcome ?? new ActionOutcome(ActionOutcomeKind.Refused, 0, "no outcome");
            if (outcome.Succeeded)
            {
                succeeded(handle);
                return;
            }

            var why = outcome.Detail ?? outcome.Kind.ToString();
            SetStatus("Could not " + what + ": " + why);
            if (outcome.Kind is ActionOutcomeKind.Refused or ActionOutcomeKind.TimedOut or ActionOutcomeKind.Cancelled
                || (outcome.Kind == ActionOutcomeKind.ServerError && why.StartsWith("failed:", StringComparison.Ordinal)))
            {
                // No server reply line exists for these (a cancelled request, and a reply whose data the client could not read: "failed: ..."); an error reply from the mod is already in the chat.
                _chat("Lifestone: could not " + what + ": " + why);
            }
        }

        if (handle.IsCompleted)
        {
            Done(handle);
        }
        else
        {
            handle.Completed += Done;
        }
    }

    private void OnWhere(ToolHandle<WhereResult> handle)
    {
        var result = handle.Result!;
        var server = result.Position;
        var origin = server.Frame.Origin;
        var place = string.Create(CultureInfo.InvariantCulture, $"0x{server.CellId:X8} [{origin.X:0.0} {origin.Y:0.0} {origin.Z:0.0}]");
        if (ClientPosition is null)
        {
            LastAgreement = (server, null, null, false);
            WhereText = "server: " + place + "\nno client body in this host (TODO(g7-where-client-body))";
            _chat("Lifestone: server " + result.Compare(null).ServerLoc + "; this host has no client physics body to compare (TODO(g7-where-client-body))");
            if (!_noBodyLogged)
            {
                _noBodyLogged = true;
                _logError?.Invoke("Where am I: this host gave the tools panel no client physics body, so the server's position is not compared with the client's (TODO(g7-where-client-body))");
            }
        }
        else if (ClientPosition() is not { } mine)
        {
            LastAgreement = (server, null, null, false);
            WhereText = "server: " + place + "\nthe client body has no position yet";
            _chat("Lifestone: server " + result.Compare(null).ServerLoc + "; the client body has no position to compare yet");
        }
        else
        {
            var comparison = result.Compare(mine);
            LastAgreement = (server, mine, comparison.Metres, comparison.Agrees);
            WhereText = string.Create(CultureInfo.InvariantCulture, $"server: {place}\nclient body {comparison.Metres:0.00} m away ({(comparison.Agrees ? "agrees" : "does not agree")})");
            _chat("Lifestone: " + comparison.Line);
        }

        SetStatus("Where am I answered.");
    }

    /// <summary>
    /// Stops the model reacting to tool replies that arrive after the panel is gone (a request still pending when the player logs off): the callbacks of its handles become no-ops, so nothing
    /// writes to a torn-down chat or status line.
    /// </summary>
    public void Detach() => _detached = true;
}

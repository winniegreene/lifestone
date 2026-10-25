using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A model of the server mod's <c>char purge</c> for the fresh-character builds' tests (M4 fresh B1, owner decision D45), installed over a <see cref="ScriptedAceSession"/> in front of the character model's own tool hook. The rule it plays is the D45 one: a purge of the
/// template's name removes one old row of that name per ask, a pending-deletion row in the character list first, then an expired row (invisible in the list, <see cref="ExpiredRows"/>), and answers <c>unknown-character</c> when none is left. A test can make the mod
/// refuse every ask with a code (<see cref="Refuse"/>: <c>graduated</c> is the mod without the D45 rule, <c>unknown-verb</c> the mod without the verb), or never run out (<see cref="Endless"/>, for the cap). It also journals every tool request with its index among the messages the
/// session sent, so a test can place the mod's verbs between the character list's messages (delete, create, enter, log off). A model of what the scenario expects of the mod, not of the mod (the rule itself is <c>CharPurgeRulesTests</c>' and the live gate's).
/// </summary>
internal sealed class ScriptedPurge
{
    private readonly ScriptedAceSession _server;
    private ToolError? _refusal;

    /// <summary>Installs the model over <paramref name="server"/>: its tool hook answers a <c>char purge</c> first and passes every other request to the hook already there (call after the character model's <c>Attach</c>).</summary>
    public ScriptedPurge(ScriptedAceSession server)
    {
        _server = server;
        var inner = server.ToolHook;
        server.ToolHook = request =>
        {
            Journal.Add((request.GetType().Name, server.Sent.Count - 1));
            return request is CharPurgeRequest purge ? Answer(purge) : inner?.Invoke(request);
        };
    }

    /// <summary>Rows of the name that have expired: the list no longer shows them, and each ask purges one.</summary>
    public int ExpiredRows { get; set; }

    /// <summary>When true every ask removes something, whatever is left (the cap's test).</summary>
    public bool Endless { get; set; }

    /// <summary>When true the mod finds no old row of the name whatever the list shows and answers <c>unknown-character</c> at once (a mod that purged nothing though the retail delete left the old copy pending).</summary>
    public bool Gone { get; set; }

    /// <summary>The names asked, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>How many asks removed an old row.</summary>
    public int Removed { get; private set; }

    /// <summary>Every tool request the model's hook saw (not the status), in order: the request type's name and its own index in <see cref="ScriptedAceSession.Sent"/>.</summary>
    public List<(string Request, int SentIndex)> Journal { get; } = [];

    /// <summary>Makes the mod refuse every ask with <paramref name="code"/> (a <see cref="ToolErrors"/> word) and its sentence.</summary>
    public void Refuse(string code, string message) => _refusal = new ToolError(code, message);

    private string Answer(CharPurgeRequest request)
    {
        Asked.Add(request.Name);
        if (_refusal is { } refusal)
        {
            return ToolsWire.Reply(ToolReply.Failure(request.Id, ToolVerbs.Char, refusal), refusal.Message);
        }

        var pending = Gone ? -1 : _server.Characters.FindIndex(c => string.Equals(c.Name, request.Name, StringComparison.OrdinalIgnoreCase) && c.DeleteField != 0);
        if (pending >= 0)
        {
            _server.Characters.RemoveAt(pending);
        }
        else if (ExpiredRows > 0 && !Gone)
        {
            ExpiredRows--;
        }
        else if (!Endless || Gone)
        {
            var unknown = new ToolError(ToolErrors.UnknownCharacter, "no character of that name is on this account");
            return ToolsWire.Reply(ToolReply.Failure(request.Id, ToolVerbs.Char, unknown), unknown.Message);
        }

        Removed++;
        var data = new CharPurgeData(request.Name, 1, 1, 14);
        return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Char, data), data.Describe());
    }
}

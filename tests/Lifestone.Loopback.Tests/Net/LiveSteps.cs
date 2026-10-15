using System.Globalization;
using System.Numerics;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// The steps the live net tests share (phases A and B): login, world entry, the log off, and a question to the Lifestone server mod
/// (<c>@lifestone</c>, answered by one F7E0 whose second line is JSON). Each asserts, so a failure names the step; the client's own
/// <see cref="LiveNetClient.Describe"/> is appended where the session's events help.
/// </summary>
internal static class LiveSteps
{
    /// <summary>How long a login may take to reach the character list (over a lossy link a few probes are needed).</summary>
    public static readonly TimeSpan ListWait = TimeSpan.FromSeconds(60);

    /// <summary>How long the world entry may take to bring the player's own object.</summary>
    public static readonly TimeSpan EnterWait = TimeSpan.FromSeconds(60);

    /// <summary>How long the mod may take to answer, and the server to send a message it was asked for.</summary>
    public static readonly TimeSpan ReplyWait = TimeSpan.FromSeconds(15);

    /// <summary>How long the log off may take (about 6 s on a clean link).</summary>
    public static readonly TimeSpan LogOffWait = TimeSpan.FromSeconds(45);

    private static int _requestCounter;

    /// <summary>A fresh correlation id for a mod request (<c>q1</c>, <c>q2</c>, ...; the grammar allows 1 to 16 of letters, digits and hyphen).</summary>
    public static string NextId() => "q" + Interlocked.Increment(ref _requestCounter).ToString(CultureInfo.InvariantCulture);

    /// <summary>Logs in as the test account and waits for the character list.</summary>
    public static void Login(LiveNetClient client, bool waitOutAceLinger)
    {
        if (waitOutAceLinger)
        {
            LiveNetClient.WaitOutAceLinger();
        }

        client.Login();
        Assert.True(
            client.PumpUntil(() => client.Session.Phase == SessionPhase.CharacterSelect && client.Session.CharacterList is not null, ListWait),
            "the login did not reach the character list. " + client.Describe());
    }

    /// <summary>Enters the world with the client's character and waits for the player's own object.</summary>
    public static void Enter(LiveNetClient client)
    {
        var entry = client.FindCharacter() ?? throw new InvalidOperationException("the account has no character of the test's name");
        client.Session.EnterWorld(entry.Guid);
        Assert.True(
            client.PumpUntil(() => client.Session.Phase == SessionPhase.InWorld && client.Sink.SelfCreate is not null, EnterWait),
            "the world entry did not bring the player's own object. " + client.Describe());
    }

    /// <summary>Logs in (waiting out ACE's 2 s linger), creates the character when missing and enters the world.</summary>
    public static void EnterTheWorld(LiveNetClient client)
    {
        Login(client, waitOutAceLinger: true);
        client.CreateCharacterIfMissing();
        Enter(client);
    }

    /// <summary>Sends LoginComplete and waits for the server to clear Hidden on the player.</summary>
    public static void Unhide(LiveNetClient client)
    {
        client.Session.SendLoginComplete();
        Assert.True(client.PumpUntil(() => client.Sink.SelfUnhidden, ReplyWait), "LoginComplete did not make the server clear Hidden. " + client.Describe());
    }

    /// <summary>Asks the mod for its status; throws when the server answers 'Unknown command' (no mod).</summary>
    public static StatusData AskStatus(LiveNetClient client) => AskData<StatusData>(client, new StatusRequest("a1"));

    /// <summary>
    /// Sends <paramref name="request"/> as Talk and waits for the reply with its verb and id (the request needs an id: <see cref="NextId"/>). A reply with
    /// <c>ok:false</c> is returned as it is.
    /// </summary>
    public static ToolReply Ask(LiveNetClient client, ToolRequest request, TimeSpan? wait = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        var id = request.Id ?? throw new ArgumentException("the request has no id, so its reply cannot be matched", nameof(request));
        return AskText(client, ToolsWire.Request(request), request.Verb, id, wait).Reply!;
    }

    /// <summary>
    /// Sends <paramref name="text"/> (the whole chat line, <c>@lifestone ...</c> with a trailing <c># id</c> form the mod reads as the correlation id) as Talk and
    /// waits for the reply carrying <paramref name="verb"/> and <paramref name="id"/>. For requests the typed records cannot make (a bad argument, an unknown verb).
    /// The result holds the reply and the chat line the player sees.
    /// </summary>
    public static ToolReplyParse AskText(LiveNetClient client, string text, string verb, string id, TimeSpan? wait = null) =>
        AskTexts(client, [(text, verb, id)], wait)[0];

    /// <summary>
    /// Sends every request in <paramref name="requests"/> back to back (no pump in between, so ACE can read them in the same world tick) and waits for each reply;
    /// the results are in request order.
    /// </summary>
    public static IReadOnlyList<ToolReplyParse> AskTexts(LiveNetClient client, IReadOnlyList<(string Text, string Verb, string Id)> requests, TimeSpan? wait = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requests);
        foreach (var (text, _, _) in requests)
        {
            client.Session.SendTalk(text);
        }

        var parses = new ToolReplyParse?[requests.Count];
        var answered = client.PumpUntil(
            () =>
            {
                foreach (var m in client.Sink.ServerMessages)
                {
                    if (ToolsWire.IsModMissing(m.Text))
                    {
                        throw new InvalidOperationException("the server answered 'Unknown command': the Lifestone server mod is not loaded");
                    }

                    var parsed = ToolsWire.ParseReply(m.Text);
                    if (parsed is not { Kind: ToolReplyKind.Reply, Reply: { } r })
                    {
                        continue;
                    }

                    for (var i = 0; i < requests.Count; i++)
                    {
                        if (parses[i] is null && r.Verb == requests[i].Verb && r.Id == requests[i].Id)
                        {
                            parses[i] = parsed;
                        }
                    }
                }

                return parses.All(p => p is not null);
            },
            wait ?? ReplyWait);
        Assert.True(answered, $"the mod did not answer every request ({string.Join(", ", requests.Select(r => r.Verb + " " + r.Id))}). " + client.Describe());
        return [.. parses.Select(p => p!)];
    }

    /// <summary>Asks the mod and returns the reply's data; a refusal (<c>ok:false</c>) fails with the mod's error code and message.</summary>
    public static TData AskData<TData>(LiveNetClient client, ToolRequest request, TimeSpan? wait = null)
        where TData : class
    {
        var reply = Ask(client, request, wait);
        Assert.True(reply.Ok, $"the mod refused '@lifestone {request.Verb}': {reply.Error}: {reply.Message}");
        return reply.DataAs<TData>() ?? throw new InvalidOperationException($"the {request.Verb} reply had no data");
    }

    /// <summary>The distance in metres between a <c>where</c> reply and a position in the same landblock (the mod's frame is the landblock's, as the wire's).</summary>
    public static float Distance(WhereData where, AcPosition position) =>
        Vector3.Distance(new Vector3(where.X, where.Y, where.Z), position.Origin);

    /// <summary>The landblock half (the high 16 bits) of a cell the mod printed (<c>0xAAAABBBB</c>).</summary>
    public static uint LandblockOf(string cell) =>
        ToolsWire.TryParseCell(cell, out var id) ? id >> 16 : throw new InvalidOperationException($"'{cell}' is not a cell id");

    /// <summary>Logs the character off to the character list, then quits.</summary>
    public static void LogOffAndQuit(LiveNetClient client)
    {
        client.Session.LogOff();
        Assert.True(
            client.PumpUntil(() => client.Session.Phase == SessionPhase.CharacterSelect, LogOffWait),
            "the log off did not bring the character list back. " + client.Describe());
        Assert.Contains(client.Sink.Events, e => e.Kind == SessionEventKind.LoggedOff);
        client.Quit();
        client.PumpUntil(() => client.Session.Phase == SessionPhase.Closed, TimeSpan.FromSeconds(10));
        Assert.Equal(SessionPhase.Closed, client.Session.Phase);
    }

    /// <summary>Leaves the world and quits whatever state the test ended in, so a failed assertion does not leave the character logged in.</summary>
    public static void LeaveTheWorld(LiveNetClient client)
    {
        try
        {
            if (client.Session.Phase == SessionPhase.InWorld)
            {
                client.Session.LogOff();
                client.PumpUntil(() => client.Session.Phase == SessionPhase.CharacterSelect, LogOffWait);
            }

            if (client.Session.Phase is not (SessionPhase.Closed or SessionPhase.Failed))
            {
                client.Quit();
                client.PumpUntil(() => client.Session.Phase == SessionPhase.Closed, TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException)
        {
            // The session is past saving; Dispose sends the disconnect.
        }
    }
}

using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>What a request needs before its verb can run (<see cref="ToolRouting.ScopeOf"/>).</summary>
internal enum ToolScope
{
    /// <summary>Nothing: answered at ACE's console too (<c>status</c>, <c>net</c>).</summary>
    Console,

    /// <summary>A player's session, but not the player in the world (<c>places</c>).</summary>
    Session,

    /// <summary>The player in a landblock (<c>where</c>, <c>place</c>, <c>xp</c>, <c>quest</c>, <c>char</c>).</summary>
    Player,
}

/// <summary>
/// The verbs as <see cref="ToolRouting.Route"/> calls them: exactly one method per request, refusals included.
/// <typeparamref name="TPlayer"/> is ACE's Player in the mod (LifestoneCommand) and a stand-in in the tests.
/// </summary>
/// <typeparam name="TPlayer">The player type.</typeparam>
internal interface IToolVerbs<in TPlayer>
{
    /// <summary>A guard refused the request: answer with <paramref name="error"/>.</summary>
    void Refuse(ToolError error);

    /// <summary>Runs <c>status</c>.</summary>
    void Status(StatusRequest request);

    /// <summary>Runs <c>net</c>.</summary>
    void Net(NetRequest request);

    /// <summary>Runs <c>places</c>.</summary>
    void Places(PlacesRequest request);

    /// <summary>Runs <c>where</c> for a player in the world.</summary>
    void Where(TPlayer player, WhereRequest request);

    /// <summary>Runs <c>place poi</c> for a player in the world.</summary>
    void PlacePoi(TPlayer player, PlacePoiRequest request);

    /// <summary>Runs <c>place loc</c> for a player in the world.</summary>
    void PlaceLoc(TPlayer player, PlaceLocRequest request);

    /// <summary>Runs <c>xp</c> for a player in the world.</summary>
    void Xp(TPlayer player, XpRequest request);

    /// <summary>Runs <c>quest stamp</c>, <c>quest erase</c> or <c>quest has</c> for a player in the world.</summary>
    void Quest(TPlayer player, QuestRequest request);

    /// <summary>Runs <c>char purge</c> for a player in the world (the caller is the player who asks, never the character purged).</summary>
    void CharPurge(TPlayer player, CharPurgeRequest request);

    /// <summary>A request type the parser makes but no verb here handles: a mod bug, answered as failed.</summary>
    void Unhandled(ToolRequest request);
}

/// <summary>
/// The <c>@lifestone</c> command's guards and dispatch (m2-plan §4.12), with nothing from ACE in them, so
/// Lifestone.ServerMod.Tests runs them (ToolRoutingTests). LifestoneCommand feeds them ACE's session and player.
/// </summary>
/// <remarks>
/// The guards, in order: the session must come from a loopback address (<see cref="ToolErrors.NotLoopback"/>), the
/// request must parse (its own error), a console request must be one that needs no player
/// (<see cref="ToolErrors.NeedsPlayer"/>), and a verb that acts on the player needs the player in a landblock
/// (<see cref="ToolErrors.NotInWorld"/>). ACE answers nothing at all for a command it refuses for access or for
/// RequiresWorld (GameActionTalk.cs:83-95), which is why the command is registered without either and checks the world
/// here, where it always answers.
/// </remarks>
internal static class ToolRouting
{
    /// <summary>The refusal sentence for a session from another computer.</summary>
    public const string NotLoopbackMessage = "Lifestone's tools answer sessions from this computer (127.0.0.1) only";

    /// <summary>The refusal sentence for a player who is not in a landblock.</summary>
    public const string NotInWorldMessage = "your character is not in the world yet";

    /// <summary>The verbs this build carries out (<see cref="StatusData.Verbs"/>): all of them.</summary>
    public static IReadOnlyList<string> Implemented { get; } = ToolsWire.Verbs;

    /// <summary>What <paramref name="request"/> needs before its verb runs, or null for a request type no verb handles.</summary>
    public static ToolScope? ScopeOf(ToolRequest request) => request switch
    {
        StatusRequest or NetRequest => ToolScope.Console,
        PlacesRequest => ToolScope.Session,
        WhereRequest or PlacePoiRequest or PlaceLocRequest or XpRequest or QuestRequest or CharPurgeRequest => ToolScope.Player,
        _ => null,
    };

    /// <summary>
    /// Runs the guards in order, then calls exactly one method of <paramref name="verbs"/>: the verb's, or
    /// <see cref="IToolVerbs{TPlayer}.Refuse"/> with the first guard that failed.
    /// </summary>
    /// <param name="verbs">The verbs.</param>
    /// <param name="hasSession">False for a command typed at ACE's console (session null).</param>
    /// <param name="isLoopback">Whether the session comes from a loopback address; not read at the console.</param>
    /// <param name="parse">What the parser made of the request.</param>
    /// <param name="playerInWorld">
    /// The player when it is in a landblock, else null; called only for a verb that acts on the player, after the
    /// other guards.
    /// </param>
    public static void Route<TPlayer>(IToolVerbs<TPlayer> verbs, bool hasSession, bool isLoopback, ToolRequestParse parse, Func<TPlayer?> playerInWorld)
        where TPlayer : class
    {
        ArgumentNullException.ThrowIfNull(verbs);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(playerInWorld);
        if (hasSession && !isLoopback)
        {
            verbs.Refuse(new ToolError(ToolErrors.NotLoopback, NotLoopbackMessage));
            return;
        }

        if (!parse.Ok)
        {
            verbs.Refuse(parse.Error);
            return;
        }

        var request = parse.Request;
        var scope = ScopeOf(request);
        if (scope is null)
        {
            verbs.Unhandled(request);
            return;
        }

        if (!hasSession && scope != ToolScope.Console)
        {
            verbs.Refuse(new ToolError(ToolErrors.NeedsPlayer, request.Verb + " needs a player; at ACE's console only status and net work"));
            return;
        }

        if (scope != ToolScope.Player)
        {
            switch (request)
            {
                case StatusRequest status:
                    verbs.Status(status);
                    return;
                case NetRequest net:
                    verbs.Net(net);
                    return;
                case PlacesRequest places:
                    verbs.Places(places);
                    return;
                default:
                    verbs.Unhandled(request);
                    return;
            }
        }

        if (playerInWorld() is not { } player)
        {
            verbs.Refuse(new ToolError(ToolErrors.NotInWorld, NotInWorldMessage));
            return;
        }

        switch (request)
        {
            case WhereRequest where:
                verbs.Where(player, where);
                return;
            case PlacePoiRequest poi:
                verbs.PlacePoi(player, poi);
                return;
            case PlaceLocRequest loc:
                verbs.PlaceLoc(player, loc);
                return;
            case XpRequest xp:
                verbs.Xp(player, xp);
                return;
            case QuestRequest quest:
                verbs.Quest(player, quest);
                return;
            case CharPurgeRequest purge:
                verbs.CharPurge(player, purge);
                return;
            default:
                verbs.Unhandled(request);
                return;
        }
    }
}

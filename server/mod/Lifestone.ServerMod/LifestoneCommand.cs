using ACE.Entity.Enum;
using ACE.Server.Command;
using ACE.Server.Mods;
using ACE.Server.Network;
using ACE.Server.WorldObjects;
using Lifestone.Net.Sockets;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// The <c>@lifestone</c> command (m2-plan §4.12; the grammar is <see cref="ToolRequestParser"/>'s). Every request gets
/// exactly one reply (<see cref="ToolCall"/>, <see cref="Reply"/>) and one <see cref="ModLog.Tool"/> line in ACE's log.
/// </summary>
/// <remarks>
/// <para>Registered at Player level with no flags. ACE gives no answer at all to a command it refuses for access or for
/// RequiresWorld (GameActionTalk.cs:83-95), so the mod checks the world itself and always answers. It takes the raw
/// text (includeRaw), because ACE's own parsing deletes quotes (CommandManager.cs:193-238). A player's command runs on
/// ACE's world thread, like ACE's admin commands; one typed at ACE's console (session null, CommandManager.cs:158-167)
/// runs on the console thread and may use only <c>status</c> and <c>net</c>, answered into the log.</para>
/// <para>The guards and the dispatch are <see cref="ToolRouting.Route"/>'s, which needs nothing from ACE and is unit
/// tested; this class gives it the session's address and the player, and <see cref="Verbs"/> runs the verb it picks.</para>
/// </remarks>
public static class LifestoneCommand
{
    /// <summary>ACE's entry point for <c>@lifestone ...</c> (and <c>lifestone ...</c> at its console).</summary>
    /// <param name="session">The player's session, or null at ACE's console.</param>
    /// <param name="parameters">ACE's parameters; with includeRaw the first is the raw text after the command word.</param>
    [CommandHandler("lifestone", AccessLevel.Player, CommandHandlerFlag.None, true, -1, "Lifestone player tools (loopback only)", ToolsWire.Usage)]
    public static void Handle(Session? session, params string[] parameters)
    {
        var raw = parameters is { Length: > 0 } ? parameters[0] ?? string.Empty : string.Empty;
        var call = new ToolCall(session, ToolRequestParser.Parse(raw), raw);
        try
        {
            AutoPromotion.Recheck("a @" + ToolsWire.Command + " request arrived");
            ToolRouting.Route(
                new Verbs(call),
                session is not null,
                session is null || LoopbackPolicy.IsLoopback(session.EndPointC2S?.Address),
                call.Parse,
                () => session?.Player is { CurrentLandblock: not null } player ? player : null);
        }
        catch (Exception e)
        {
            call.Crashed(e);
        }
    }

    /// <summary>The verbs <see cref="ToolRouting.Route"/> picks from, each answering the request's <see cref="ToolCall"/>.</summary>
    private sealed class Verbs(ToolCall call) : IToolVerbs<Player>
    {
        public void Refuse(ToolError error) => call.Fail(error);

        public void Status(StatusRequest request) => StatusVerb.Run(call, request);

        public void Net(NetRequest request) => NetVerb.Run(call, request);

        public void Places(PlacesRequest request) => PlacesVerb.Run(call, request);

        public void Where(Player player, WhereRequest request) => WhereVerb.Run(call, player, request);

        public void PlacePoi(Player player, PlacePoiRequest request) => PlaceVerb.Poi(call, player, request);

        public void PlaceLoc(Player player, PlaceLocRequest request) => PlaceVerb.Loc(call, player, request);

        public void Xp(Player player, XpRequest request) => XpVerb.Run(call, player, request);

        public void Quest(Player player, QuestRequest request) => QuestVerb.Run(call, player, request);

        public void CharPurge(Player player, CharPurgeRequest request) => CharVerb.Purge(call, player, request);

        public void SpellLearn(Player player, SpellLearnRequest request) => SpellVerb.Learn(call, player, request);

        public void Item(Player player, ItemRequest request) => ItemVerb.Run(call, player, request);

        /// <summary>A request type the parser accepts but no verb here handles: a loud bug, answered as failed.</summary>
        public void Unhandled(ToolRequest request)
        {
            ModManager.Log("Lifestone mod bug: no verb handles " + request.GetType().Name + " (verb '" + request.Verb + "')", ModManager.LogLevel.Error);
            call.Fail(ToolErrors.Failed, "this version of the Lifestone server mod cannot carry out '" + request.Verb + "'");
        }
    }
}

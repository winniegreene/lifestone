using Lifestone.ServerTools;

namespace Lifestone.Client.Session.State;

/// <summary>
/// One accepted Lifestone tool reply, as the reducer hands it to the player tools (<see cref="GameStateReducer.ToolReplyRouted"/>):
/// the mod's reply, parsed by its own wire contract (<see cref="ToolsWire.ParseReply"/>), and the human line the chat shows.
/// </summary>
/// <param name="Reply">The reply (its id, verb, ok flag, error and data).</param>
/// <param name="ChatLine">The human line, <c>Lifestone: ...</c>.</param>
internal sealed record ToolReplyLine(ToolReply Reply, string ChatLine);

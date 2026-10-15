using ACE.Entity.Enum;
using ACE.Server.Mods;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>Sends a request's one reply (<see cref="ToolsWire.Reply"/>).</summary>
internal static class Reply
{
    /// <summary>
    /// Sends <paramref name="reply"/> to <paramref name="session"/> as one F7E0 ServerMessage of type Broadcast, or writes
    /// it to ACE's log (<see cref="ModLog.ConsoleReply"/>) for a console command. It goes through the session's own
    /// queue, not Player.SendMessage, whose squelch filter could drop it (Player.cs:1079-1085).
    /// </summary>
    public static void Send(Session? session, ToolReply reply, string sentence)
    {
        string text;
        try
        {
            text = ToolsWire.Reply(reply, sentence);
        }
        catch (ArgumentException e)
        {
            ModManager.Log("Lifestone tool reply could not be written: " + e.Message, ModManager.LogLevel.Error);
            var error = new ToolError(ToolErrors.Failed, "the reply could not be written; see ACE_Log.txt");
            text = ToolsWire.Reply(ToolReply.Failure(reply.Id, reply.Verb, error), error.Message);
        }

        if (session is null)
        {
            ModManager.Log(ModLog.ConsoleReply + text.Replace("\n", " | ", StringComparison.Ordinal));
            return;
        }

        session.Network.EnqueueSend(new GameMessageSystemChat(text, (ChatMessageType)ToolsWire.ReplyChatType));
    }
}

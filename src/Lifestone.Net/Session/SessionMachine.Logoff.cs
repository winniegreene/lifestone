using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Net.Session;

/// <summary>
/// Leaving the world (m2-plan §3.3 N6; docs/protocol.md §2.10 and §4.3 row 18): logoff back to character select, and
/// quit.
/// </summary>
/// <remarks>
/// <para><b>LogOff</b> sends F653 with the character's GUID (<c>Proto_UI::LogOffCharacter</c>, 11.4186 0x00546A20,
/// 11.6096 0x005475E0) and stays in <see cref="SessionPhase.LoggingOff"/> until the server's F653 and then its F658
/// arrive (ACE sends them, with F7E1, after its 6 s logoff animation; <c>Session.LogOffPlayer</c> and
/// <c>SendFinalLogOffMessages</c>). Then the session is back in character select and raises
/// <see cref="SessionEventKind.LoggedOff"/>. No answer within <see cref="SessionTimeouts.Logoff"/> fails it with
/// <see cref="SessionFailureKind.LogoffTimeout"/>. A server F653 the client did not ask for (an operator's logout) is
/// followed the same way, with a warning.</para>
/// <para><b>Quit</b> in the world sends F653, then a cleartext Disconnect with the client id, so ACE drops the session
/// at once (<c>NetworkSession.ProcessPacket</c> terminates on the Disconnect flag before anything else) instead of
/// after its 60 s timeout; in any other connected phase it sends the Disconnect alone. Retail sends the same
/// Disconnect header to each of its connections in <c>ClientNet::LogOffServer</c> (11.4186 0x00543EF0, 11.6096
/// 0x00544AB0). The session ends <see cref="SessionPhase.Closed"/>.</para>
/// </remarks>
internal sealed partial class SessionMachine
{
    private long _logOffStartedAt;
    private bool _logOffConfirmed;

    public void LogOff()
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "LogOff");
        SendLogOff();
        _logOffStartedAt = _time.GetTimestamp();
        _logOffConfirmed = false;
        SetPhase(SessionPhase.LoggingOff);
        Log(NetLogLevel.Info, $"log off: F653 sent for 0x{PlayerGuid:X8}");
    }

    public void Quit()
    {
        EnterGameThread(capture: false);
        if (Phase is SessionPhase.Closed or SessionPhase.Failed)
        {
            return;
        }

        var inWorld = Phase == SessionPhase.InWorld;
        SetPhase(SessionPhase.Closing);
        if (_connection is { Transport: not null } connection)
        {
            if (inWorld)
            {
                SendLogOff();
            }

            connection.SendDisconnect();
            Log(NetLogLevel.Info, inWorld ? "quit: F653 and Disconnect sent" : "quit: Disconnect sent");
        }

        CloseConnection();
        ZeroPassword();
        RetryDueAt = null;
        ResetCharacterState();
        PlayerGuid = null;
        SetPhase(SessionPhase.Closed);
    }

    private void SendLogOff()
    {
        var writer = BeginMessage();
        CharacterLogOffCodec.Write(writer, PlayerGuid ?? 0);
        SendMessage(writer, MessageQueue.Login);
    }

    private void OnServerLogOffMessage()
    {
        switch (Phase)
        {
            case SessionPhase.LoggingOff:
                _logOffConfirmed = true;
                break;
            case SessionPhase.InWorld:
                Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.UnexpectedMessage, "the server logged the character off"));
                Log(NetLogLevel.Warning, "log off: the server logged the character off without a request");
                _logOffStartedAt = _time.GetTimestamp();
                _logOffConfirmed = true;
                SetPhase(SessionPhase.LoggingOff);
                break;
            default:
                Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.UnexpectedMessage, $"F653 CharacterLogOff in {Phase}"));
                break;
        }
    }

    /// <summary>F658 while logging off: after the server's F653 it completes the logoff.</summary>
    private void OnLogOffList()
    {
        if (!_logOffConfirmed)
        {
            return;
        }

        _logOffConfirmed = false;
        PlayerGuid = null;
        _loginCompleteArmed = false;
        if (_connection is { } connection)
        {
            connection.NextActionSequence = GameActionEnvelope.FirstSequence;  // CPlayerSystem::ExecuteLogOff
        }

        SetPhase(SessionPhase.CharacterSelect);
        Raise(SessionEvent.LoggedOff(_time.GetTimestamp(), Phase));
        Log(NetLogLevel.Info, $"logged off: back at character select with {CharacterList!.Characters.Length} character(s)");
    }

    private void RunLogOffTimer(long now)
    {
        if (Phase == SessionPhase.LoggingOff && _time.GetElapsedTime(_logOffStartedAt, now) >= _options.Timeouts.Logoff)
        {
            Fail(SessionFailureKind.LogoffTimeout,
                $"the server did not finish the logoff within {_options.Timeouts.Logoff.TotalSeconds:0.#} s (its F653 {(_logOffConfirmed ? "came" : "never came")})");
        }
    }
}

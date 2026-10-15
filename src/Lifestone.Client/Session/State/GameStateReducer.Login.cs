using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.State;

/// <summary>The login phase (ST7): data check, character list, create, delete, restore, errors, boots, PlayerCreate, logoff.</summary>
internal sealed partial class GameStateReducer
{
    /// <summary>
    /// How long after a NameInUse answer a second, unrequested NameInUse counts as its duplicate and is folded into it
    /// (ACE can answer one create twice; the net track folds the same window, m2-plan N6).
    /// </summary>
    internal const double DuplicateNameInUseSeconds = 2.0;

    private int _createsAwaiting;
    private uint _restoreAwaiting;
    private bool _deleteAwaitingList;
    private CharGenResult _lastCreateAnswer;
    private double _lastCreateAnswerAt = double.NegativeInfinity;

    /// <summary>True between an F655 delete acknowledgement and the F658 list that follows it.</summary>
    internal bool DeleteAwaitingList => _deleteAwaitingList;

    /// <summary>
    /// F658 CharacterList: the list is replaced, in the server's order (a delete names a character by its index there).
    /// Each entry's deletion field is kept raw; any non-zero value means pending deletion (ACE writes at least 1, D52). A
    /// selection the new list no longer holds is cleared.
    /// </summary>
    public void OnCharacterList(in InboundContext context, CharacterListMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var list = _state.Characters;
        list.Mutable.Clear();
        foreach (var entry in message.Characters)
        {
            list.Mutable.Add(new CharacterSlot(entry.Guid, entry.Name, entry.DeleteTimeout));
        }

        list.SlotCount = message.SlotCount;
        var selectionLost = list.SelectedGuid != 0 && list.IndexOf(list.SelectedGuid) < 0;
        if (selectionLost)
        {
            list.SelectedGuid = 0;
        }

        list.Version++;
        _deleteAwaitingList = false;
        Changed();
        Publish(new CharacterListChanged(list.Version, CharacterListReason.Received));
        if (selectionLost)
        {
            Publish(new CharacterSelected(0));
        }
    }

    /// <summary>F7E1 ServerName: the world's name (<see cref="GameState.WorldName"/>; no event of its own: F658 follows).</summary>
    public void OnServerName(in InboundContext context, in ServerNameMsg message)
    {
        _state.WorldName = message.WorldName;
        Changed();
    }

    /// <summary>F7E5 DDD_Interrogation: the data check started (the network session answers it).</summary>
    public void OnDddInterrogation(in InboundContext context, DddInterrogationMsg message) => StartDataCheck();

    /// <summary>
    /// F7E7 DDD_BeginDDD: the server's dats differ from the client's. Lifestone never patches; the network session fails
    /// with <c>DataCheckMismatch</c>, which reaches the state as the failure.
    /// </summary>
    public void OnDddBegin(in InboundContext context, in DddBeginMsg message)
    {
        _state.DataCheck = DataCheckState.Mismatch;
        Changed();
        _log.Warn(
            SessionLog.Category,
            "the server's dats differ from the client's (DDD_BeginDDD); Lifestone does not patch, so the login fails",
            new LogField("missingIterations", message.MissingIterations),
            new LogField("bytes", message.TotalFileSize));
    }

    /// <summary>F7EA DDD_EndDDD: the data check is complete.</summary>
    public void OnDddEnd(in InboundContext context) => CompleteDataCheck();

    /// <summary>
    /// F643 CharacterCreateResponse, which answers a create and a restore alike. An <c>Ok</c> for a create appends the
    /// character locally (ACE does not resend F658, protocol.md §4); an <c>Ok</c> for a restore clears its pending
    /// deletion. A NameInUse within <see cref="DuplicateNameInUseSeconds"/> of another, with no create sent in between,
    /// is folded into the first.
    /// </summary>
    public void OnCharGenResponse(in InboundContext context, in CharGenResponseMsg message)
    {
        var list = _state.Characters;
        var ok = message.Result == CharGenResult.Ok;
        var guid = message.Guid;
        bool restore;
        if (_restoreAwaiting != 0)
        {
            restore = true;
            if (!ok)
            {
                guid = _restoreAwaiting;
            }

            _restoreAwaiting = 0;
        }
        else
        {
            // Without the restore notice (stand-in data, an older recording), an Ok for a character already listed can
            // only answer a restore: a create never returns an existing guid.
            restore = ok && list.IndexOf(guid) >= 0;
        }

        if (!restore)
        {
            var now = _clock.Now;
            if (_createsAwaiting > 0)
            {
                _createsAwaiting--;
            }
            else if (message.Result == CharGenResult.NameInUse && _lastCreateAnswer == CharGenResult.NameInUse &&
                now - _lastCreateAnswerAt <= DuplicateNameInUseSeconds)
            {
                _log.Debug(SessionLog.Category, "a duplicate NameInUse answer was folded into the first");
                return;
            }

            _lastCreateAnswer = message.Result;
            _lastCreateAnswerAt = now;
        }

        var reason = (CharacterListReason?)null;
        string? name = message.Name;
        if (ok)
        {
            var index = list.IndexOf(guid);
            if (restore)
            {
                if (index >= 0)
                {
                    var slot = list.Mutable[index];
                    name ??= slot.Name;
                    list.Mutable[index] = slot with { DeleteTimeout = 0 };
                    reason = CharacterListReason.Restored;
                }
            }
            else
            {
                list.Mutable.Add(new CharacterSlot(guid, name ?? string.Empty, 0));
                reason = CharacterListReason.Created;
            }

            if (reason is not null)
            {
                list.Version++;
                Changed();
            }
        }

        if (reason is { } changed)
        {
            Publish(new CharacterListChanged(list.Version, changed));
        }

        Publish(new CharacterCreateResult(message.Result, guid, ok ? name : null, restore));
    }

    /// <summary>F655 acknowledgement of a delete: the next F658 shows the character pending deletion.</summary>
    public void OnCharacterDeleteAck(in InboundContext context)
    {
        var list = _state.Characters;
        _deleteAwaitingList = true;
        list.Version++;
        Changed();
        Publish(new CharacterListChanged(list.Version, CharacterListReason.DeleteAcknowledged));
    }

    /// <summary>F659 CharacterError (paired with the network session's own event for it: one <see cref="CharacterErrorReceived"/> each).</summary>
    public void OnCharacterError(in InboundContext context, in CharacterErrorMsg message) => ReportCharacterError(message.Code, fromMessage: true);

    /// <summary>F7DC AccountBoot, in any phase (paired with the network session's own event for it: one <see cref="Booted"/> each).</summary>
    public void OnAccountBoot(in InboundContext context, in AccountBootMsg message) => ReportBoot(message.Reason, fromMessage: true);

    /// <summary>F7C1 AccountBanned.</summary>
    public void OnAccountBanned(in InboundContext context, in AccountBannedMsg message)
    {
        _log.Warn(SessionLog.Category, "the server says the account is banned", new LogField("secondsRemaining", message.SecondsRemaining));
        Publish(new Booted(message.Reason, Banned: true));
    }

    /// <summary>F7DF CharacterEnterWorldServerReady: the network session answers it with F657; nothing in the state changes.</summary>
    public void OnEnterWorldServerReady(in InboundContext context)
    {
    }

    /// <summary>
    /// F746 PlayerCreate: the player's object id. Re-expresses <c>SmartBox::HandleCreatePlayer</c> (11.4186 0x00455110,
    /// 11.6096 0x004551B0): a second one is ignored; otherwise the SmartBox messages held until now are replayed in
    /// arrival order (<c>SmartBox::ProcessNetBlobs</c>, 0x00454A70 / 0x00454B10).
    /// </summary>
    public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message)
    {
        var self = _state.Self;
        if (self.Guid != 0 || message.Guid == 0)
        {
            _log.Warn(
                SessionLog.Category,
                "a PlayerCreate was ignored: the player is already known, or the id is 0 (SmartBox::HandleCreatePlayer)",
                new LogField("guid", message.Guid),
                new LogField("player", self.Guid));
            return;
        }

        self.Guid = message.Guid;
        Changed();
        Publish(new PlayerCreated(message.Guid));
        ReplayParked(PendingObjectMessages.WaitingForPlayer);
    }

    /// <summary>F653 CharacterLogOff from the server: the logoff completed; the world is left (the phase follows from the network session).</summary>
    public void OnCharacterLogOff(in InboundContext context) => LeaveWorld();

    private void StartDataCheck()
    {
        if (_state.DataCheck != DataCheckState.NotStarted)
        {
            return;
        }

        _state.DataCheck = DataCheckState.Checking;
        Changed();
        Publish(new ConnectionProgress(ConnectionStage.DataCheckStarted, _attempt, TimeSpan.Zero, 0, null));
    }

    private void CompleteDataCheck()
    {
        if (_state.DataCheck is DataCheckState.Complete or DataCheckState.Mismatch)
        {
            return;
        }

        _state.DataCheck = DataCheckState.Complete;
        Changed();
        Publish(new ConnectionProgress(ConnectionStage.DataCheckComplete, _attempt, TimeSpan.Zero, 0, null));
    }

    /// <summary>
    /// One <see cref="Booted"/> per boot: net reports a boot both as the F7DC message and as its own session event, in an
    /// order its contract leaves open, so the two are paired (the first publishes, its partner is absorbed).
    /// </summary>
    private void ReportBoot(string? reason, bool fromMessage)
    {
        if (Absorb(ref _bootBalance, fromMessage))
        {
            return;
        }

        _log.Warn(SessionLog.Category, "the server ended the session (AccountBoot)" + (string.IsNullOrWhiteSpace(reason) ? string.Empty : ": " + reason.TrimStart()));
        Publish(new Booted(reason, Banned: false));
    }

    /// <summary>One <see cref="CharacterErrorReceived"/> per F659, paired with net's session event as <see cref="ReportBoot"/> is.</summary>
    private void ReportCharacterError(uint code, bool fromMessage)
    {
        if (Absorb(ref _characterErrorBalance, fromMessage))
        {
            return;
        }

        _log.Info(SessionLog.Category, "the server sent a character error", new LogField("code", code));
        Publish(new CharacterErrorReceived(code));
    }

    /// <summary>
    /// Pairs the two reports of one occurrence: <paramref name="balance"/> counts messages minus session events. A report
    /// whose partner already came is absorbed (true); otherwise it publishes (false) and waits for its partner.
    /// </summary>
    private static bool Absorb(ref int balance, bool fromMessage)
    {
        if (fromMessage)
        {
            return balance++ < 0;
        }

        return balance-- > 0;
    }

    private void Select(uint guid)
    {
        var list = _state.Characters;
        if (guid != 0 && list.IndexOf(guid) < 0)
        {
            _log.Warn(SessionLog.Category, "a selection of a character not in the list was ignored", new LogField("guid", guid));
            return;
        }

        if (list.SelectedGuid == guid)
        {
            return;
        }

        list.SelectedGuid = guid;
        Changed();
        Publish(new CharacterSelected(guid));
    }
}

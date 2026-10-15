using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Net.Session;

/// <summary>
/// The character operations of character select (m2-plan §3.3 N6; docs/protocol.md §4.2): create (F656 → F643),
/// delete (F655 → F655 acknowledgement and a fresh F658) and restore (F7D9 → F643). One operation waits for its answer
/// at a time, as retail's character screens do; another call meanwhile throws.
/// </summary>
/// <remarks>
/// <para><b>Create.</b> The answer F643 is matched with the waiting create. On Ok the new character is appended to
/// <see cref="CharacterList"/>, because ACE does not resend F658 after a create (docs/protocol.md §4.2 row 10a). ACE
/// answers a taken name with NameInUse twice: its first availability check does not stop the create. The next
/// NameInUse within <see cref="DuplicateAnswerWindow"/> of the first is folded into it. This holds even when a new
/// create or restore already waits, because ACE's serial database queue sends the duplicate before any NameInUse
/// answer to a later request. The sink does not see the duplicate, and a
/// <see cref="SessionWarning.DuplicateAnswerFolded"/> warning says so.</para>
/// <para><b>Delete</b> takes an index into <see cref="CharacterList"/>, range-checked here, because ACE indexes its list
/// with it unchecked (<c>CharacterHandler.CharacterDelete</c>); it completes when the fresh F658 arrives.</para>
/// <para><b>Restore</b> sends F7D9 as retail's character screen does: <c>CPlayerSystem::RestoreCharacter</c> (11.4186
/// 0x0055D760, 11.6096 0x0055E480) calls <c>Proto_UI::SendAdminRestoreCharacter</c> (11.4186 0x00546CF0, 11.6096
/// 0x005478B0) with the GUID and two empty strings for the name and the account. ACE reads the GUID alone, answers
/// F643, and does not resend F658 either, so an Ok clears the entry's greyed-out word locally.</para>
/// <para>An answer that does not come within <see cref="SessionTimeouts.CharacterOperation"/> ends the wait with a
/// <see cref="SessionWarning.CharacterOperationTimedOut"/> warning; the session stays in character select. An Ok that
/// arrives after that still updates <see cref="CharacterList"/>, with an <see cref="SessionWarning.UnexpectedMessage"/>
/// warning. Each request echoes the account string of the latest F658 verbatim (ACE ignores, or refuses, any
/// other).</para>
/// </remarks>
internal sealed partial class SessionMachine
{
    /// <summary>How long after a create's NameInUse a second NameInUse counts as ACE's duplicate.</summary>
    internal static readonly TimeSpan DuplicateAnswerWindow = TimeSpan.FromSeconds(2);

    private CharacterOperation? _operation;
    private long? _foldNameInUseUntil;

    public OutboundTicket CreateCharacter(in CharacterCreateRequest request)
    {
        EnterGameThread(capture: false);
        request.Validate();
        RequirePhase(SessionPhase.CharacterSelect, "CreateCharacter");
        RequireNoOperation("CreateCharacter");
        var list = CharacterList!;
        var writer = BeginMessage();
        CharacterCreateCodec.Write(writer, list.Account, request);
        var ticket = SendMessage(writer, MessageQueue.Login);
        _operation = new CharacterOperation(CharacterOperationKind.Create, ticket.Timestamp, 0, request.Name);
        Log(NetLogLevel.Info, "character select: CharacterCreate sent");
        return ticket;
    }

    public OutboundTicket DeleteCharacter(int listIndex)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.CharacterSelect, "DeleteCharacter");
        var list = CharacterList!;
        if ((uint)listIndex >= (uint)list.Characters.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(listIndex), listIndex,
                $"the character list has {list.Characters.Length} character(s); ACE would index its own list with this unchecked");
        }

        RequireNoOperation("DeleteCharacter");
        var entry = list.Characters[listIndex];
        var writer = BeginMessage();
        CharacterDeleteCodec.Write(writer, list.Account, listIndex);
        var ticket = SendMessage(writer, MessageQueue.Login);
        _operation = new CharacterOperation(CharacterOperationKind.Delete, ticket.Timestamp, entry.Guid, entry.Name);
        Log(NetLogLevel.Info, $"character select: CharacterDelete sent for list index {listIndex} (0x{entry.Guid:X8})");
        return ticket;
    }

    public OutboundTicket RestoreCharacter(uint guid)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.CharacterSelect, "RestoreCharacter");
        var list = CharacterList!;
        var index = IndexOf(list, guid);
        if (index < 0)
        {
            throw new ArgumentException($"character 0x{guid:X8} is not in the character list", nameof(guid));
        }

        RequireNoOperation("RestoreCharacter");
        var entry = list.Characters[index];
        var writer = BeginMessage();
        CharacterRestoreCodec.Write(writer, guid, ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty);
        var ticket = SendMessage(writer, MessageQueue.Control);
        _operation = new CharacterOperation(CharacterOperationKind.Restore, ticket.Timestamp, guid, entry.Name);
        Log(NetLogLevel.Info, $"character select: CharacterRestore sent for 0x{guid:X8}");
        return ticket;
    }

    /// <summary>
    /// F643: true when the sink should see it (false for a folded duplicate).
    /// </summary>
    /// <remarks>
    /// The duplicate is folded before the waiting operation is looked at. ACE runs both of a create's name checks on its
    /// serial database queue, so the second NameInUse always comes before any NameInUse answer to a later request. A
    /// create or restore sent from the first NameInUse's callback must not be answered by it: that request would then
    /// take the wrong result, and its own answer would arrive with nothing waiting. Any Ok also updates
    /// <see cref="CharacterList"/>, even an Ok that comes after its wait gave up. ACE has added (or restored) the
    /// character in its own list either way, and later delete indexes count from that list.
    /// </remarks>
    private bool OnCharGenResponseMessage(in CharGenResponseMsg response)
    {
        var now = _time.GetTimestamp();
        if (response.Result == CharGenResult.NameInUse && _foldNameInUseUntil is { } until && now <= until)
        {
            _foldNameInUseUntil = null;
            Raise(SessionEvent.Warned(now, Phase, SessionWarning.DuplicateAnswerFolded, "a second NameInUse for the same create (ACE sends two)"));
            Log(NetLogLevel.Debug, "character select: folded ACE's second NameInUse into the first");
            return false;
        }

        if (_operation is { Kind: CharacterOperationKind.Create or CharacterOperationKind.Restore } operation)
        {
            _operation = null;
            if (response.Result == CharGenResult.Ok)
            {
                ApplyOk(response, operation.Name);
            }

            _foldNameInUseUntil = operation.Kind == CharacterOperationKind.Create && response.Result == CharGenResult.NameInUse
                ? now + Ticks(DuplicateAnswerWindow)
                : null;
            Log(NetLogLevel.Info, $"character select: {operation.Kind} answered {response.Result}");
            return true;
        }

        if (response.Result == CharGenResult.Ok)
        {
            ApplyOk(response, fallbackName: string.Empty);
        }

        Raise(SessionEvent.Warned(now, Phase, SessionWarning.UnexpectedMessage, $"F643 {response.Result} with no create or restore waiting"));
        Log(NetLogLevel.Warning, $"character select: F643 {response.Result} arrived with no create or restore waiting");
        return true;
    }

    /// <summary>
    /// An Ok F643 updates the local copy of the list as ACE has updated its own, since it sends no new F658. A GUID already
    /// listed was restored, so its greyed-out word is cleared. A new GUID was created, so it is appended, as ACE appends
    /// it to the list that delete indexes count from.
    /// </summary>
    private void ApplyOk(in CharGenResponseMsg response, string fallbackName)
    {
        if (CharacterList is not { } list)
        {
            return;
        }

        CharacterList = IndexOf(list, response.Guid) >= 0
            ? Restored(list, response.Guid)
            : list with { Characters = list.Characters.Add(new CharacterListEntry(response.Guid, response.Name ?? fallbackName, response.Trailing)) };
    }

    private void OnCharacterDeleteAckMessage()
    {
        if (_operation is { Kind: CharacterOperationKind.Delete } operation)
        {
            _operation = operation with { Acknowledged = true };
        }
    }

    /// <summary>A fresh F658 in character select completes a waiting delete.</summary>
    private void OnCharacterSelectList()
    {
        if (_operation is { Kind: CharacterOperationKind.Delete } operation)
        {
            _operation = null;
            Log(NetLogLevel.Info, $"character select: 0x{operation.Guid:X8} is pending deletion{(operation.Acknowledged ? string.Empty : " (no acknowledgement came first)")}");
        }
    }

    /// <summary>A CharacterError in character select answers the waiting operation, if any.</summary>
    private void CompleteCharacterOperationWithError(uint code)
    {
        if (Phase == SessionPhase.CharacterSelect && _operation is { } operation)
        {
            _operation = null;
            Log(NetLogLevel.Info, $"character select: {operation.Kind} answered CharacterError 0x{code:X2}");
        }
    }

    /// <summary>Ends a wait that took longer than <see cref="SessionTimeouts.CharacterOperation"/>.</summary>
    private void RunCharacterOperationTimer()
    {
        if (_operation is { } operation && Phase == SessionPhase.CharacterSelect
            && _time.GetElapsedTime(operation.StartedAt) >= _options.Timeouts.CharacterOperation)
        {
            _operation = null;
            var text = $"{operation.Kind} got no answer within {_options.Timeouts.CharacterOperation.TotalSeconds:0.#} s";
            Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.CharacterOperationTimedOut, text));
            Log(NetLogLevel.Warning, "character select: " + text);
        }
    }

    private void ResetCharacterState()
    {
        _operation = null;
        _foldNameInUseUntil = null;
        _enterWorld = null;
    }

    private void RequireNoOperation(string operation)
    {
        if (_operation is { } waiting)
        {
            throw new InvalidOperationException($"{operation} must wait: a {waiting.Kind} is still waiting for its answer.");
        }
    }

    private static int IndexOf(CharacterListMsg list, uint guid)
    {
        for (var i = 0; i < list.Characters.Length; i++)
        {
            if (list.Characters[i].Guid == guid)
            {
                return i;
            }
        }

        return -1;
    }

    private static CharacterListMsg Restored(CharacterListMsg list, uint guid)
    {
        var index = IndexOf(list, guid);
        return index < 0 ? list : list with { Characters = list.Characters.SetItem(index, list.Characters[index] with { DeleteTimeout = 0 }) };
    }

    private enum CharacterOperationKind : byte
    {
        Create,
        Delete,
        Restore,
    }

    /// <summary>The operation waiting for its answer.</summary>
    private sealed record CharacterOperation(CharacterOperationKind Kind, long StartedAt, uint Guid, string Name)
    {
        /// <summary>For a delete: the F655 acknowledgement came.</summary>
        public bool Acknowledged { get; init; }
    }
}

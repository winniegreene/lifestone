using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Options;

/// <summary>
/// The character's options (docs/retail-ui.md section 21.8; M5 6 u1, D32): reads the two option words of <see cref="PlayerQualities"/> and, when the player applies a change in the options
/// panel, sends both words to the server (<see cref="IActionApi.SetCharacterOptions"/>, GameAction 0x01A1: retail's <c>UIOption_Checkbox::Apply</c>, 11.6096 0x00487020, wrote
/// <c>PlayerModule::SetOption</c>, 0x005D6410, and so <c>GameActionSetCharacterOptions</c>). ACE stores the words and returns them in the next PlayerDescription, so an option set in one login is
/// set in the next. M2 D42's session-local override is retired here: nothing in this class calls <c>IActionApi.SetSessionOption</c> any more.
/// </summary>
/// <remarks>
/// The action is queued and the read model applies the words when the tick sends it, so the class keeps the words it asked for (<c>_pending</c>) and answers <see cref="Get"/> from them until the
/// model reports the change (<see cref="QualitiesChanged"/>), or the send was refused: every reader (the vitals, the vivid indicator, the panel) sees the player's value at once and in any order.
/// An option whose client-side effect Lifestone does not build (<see cref="PlayerOptionDef.Honoured"/> false) is still set and sent; changing it logs <c>TODO(g6-option-effects)</c> once per option.
/// </remarks>
public sealed class GameOptions : IDisposable
{
    private readonly IGameSessionView _session;
    private readonly ILogSink _log;
    private readonly IDisposable _subscription;
    private readonly HashSet<string> _effectTodoLogged = [];
    private (uint One, uint Two)? _pending;
    private (uint One, uint Two) _known;

    /// <summary>The options over <paramref name="session"/>.</summary>
    public GameOptions(IGameSessionView session, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);
        _session = session;
        _log = log;
        _known = (session.State.Self.Qualities.CharacterOptions1, session.State.Self.Qualities.CharacterOptions2);
        _subscription = session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
    }

    /// <summary>Told after an option changed (retail's <c>RecvNotice_PlayerOptionChanged</c>); carries the option.</summary>
    public event Action<PlayerOptionDef>? Changed;

    /// <summary>How many options the player changed.</summary>
    public int ChangeCount { get; private set; }

    /// <summary>How many times both words were sent to the server (one per Apply or combat-window click).</summary>
    public int SendCount { get; private set; }

    /// <summary>True when the player's options ask for the side-by-side vitals.</summary>
    public bool SideBySideVitals => Get(PlayerOptionTable.SideBySideVitals);

    /// <summary>True when the vivid targeting indicator is on (the bit the indicator reads).</summary>
    public bool VividTargetingIndicator => Get(PlayerOptionTable.VividTargetingIndicator);

    /// <summary>The option's value now (<c>PlayerModule::GetOption</c>): the words the player asked for while they are on their way, else the model's.</summary>
    public bool Get(PlayerOptionDef option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return (Word(option.Word) & option.Mask) != 0;
    }

    /// <summary>Sets one option and sends the words (<see cref="Apply"/> for one change).</summary>
    public bool TrySet(PlayerOptionDef option, bool value)
    {
        ArgumentNullException.ThrowIfNull(option);
        return Apply([(option, value)]);
    }

    /// <summary>
    /// Retail's Apply: sets every change and sends both words once (<c>UIOption_Checkbox::Apply</c> per changed box, 0x00487020, then <c>PlayerModule::SetOption</c>, 0x005D6410; one packet here).
    /// False, with nothing changed, when no description has arrived or the send was refused (not in the world); an option already at its value is not a change.
    /// </summary>
    public bool Apply(IReadOnlyList<(PlayerOptionDef Option, bool Value)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var one = Word(1);
        var two = Word(2);
        var changed = new List<PlayerOptionDef>();
        foreach (var (option, value) in changes)
        {
            ref var word = ref (option.Word == 1 ? ref one : ref two);
            var now = (word & option.Mask) != 0;
            if (now == value)
            {
                continue;
            }

            word = value ? word | option.Mask : word & ~option.Mask;
            changed.Add(option);
        }

        if (changed.Count == 0)
        {
            return true;
        }

        var handle = _session.Actions.SetCharacterOptions(one, two, ActionSource.Ui);
        if (handle.IsCompleted && Refusal(handle) is { } refusal)
        {
            _log.Warn("options", $"the options were not sent: {refusal}");
            return false;
        }

        _pending = (one, two);
        if (!handle.IsCompleted)
        {
            handle.Completed += h => OnSendCompleted(h, changed); // a refusal comes with the tick (not in the world, no description yet)
        }

        SendCount++; // _known stays the model's words: when the tick applies them, OnQualitiesChanged tells every option it changed (the vitals window follows then; M5 6 k1 found it did not)
        foreach (var option in changed)
        {
            ChangeCount++;
            var on = (Word(option.Word) & option.Mask) != 0;
            _log.Info("options", $"option {option.Name} is {(on ? "on" : "off")}; both words go to the server (GameAction 0x01A1)");
            if (!option.Honoured && _effectTodoLogged.Add(option.Name))
            {
                _log.Warn("options", $"TODO(g6-option-effects): the option {option.Name} is set and sent, but Lifestone builds no client-side effect for it ({option.Effect})");
            }

            Changed?.Invoke(option);
        }

        return true;
    }

    /// <inheritdoc/>
    public void Dispose() => _subscription.Dispose();

    private static string? Refusal(ActionHandle handle) => handle.Outcome is { Kind: ActionOutcomeKind.Refused } o ? o.Detail : null;

    // A send the tick refused: the words never left, so the overlay ends and the readers read the model's words again.
    private void OnSendCompleted(ActionHandle handle, List<PlayerOptionDef> changed)
    {
        if (Refusal(handle) is not { } refusal)
        {
            return;
        }

        _pending = null;
        _log.Warn("options", $"the options were not sent: {refusal}");
        foreach (var option in changed)
        {
            Changed?.Invoke(option);
        }
    }

    // The model took the words (the tick sent them, or a description arrived): it is the truth again, and every option whose bit differs from the words last seen is told as changed (the server's
    // description after a login, the stand-in's answer): the vitals follow without anyone calling Apply.
    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change is not (QualityChange.Options or QualityChange.Description))
        {
            return;
        }

        _pending = null;
        var q = _session.State.Self.Qualities;
        var now = (One: q.CharacterOptions1, Two: q.CharacterOptions2);
        var before = _known;
        _known = now;
        if (now == before)
        {
            return;
        }

        foreach (var option in PlayerOptionTable.All)
        {
            var was = ((option.Word == 1 ? before.One : before.Two) & option.Mask) != 0;
            var isNow = ((option.Word == 1 ? now.One : now.Two) & option.Mask) != 0;
            if (was != isNow)
            {
                Changed?.Invoke(option);
            }
        }
    }

    private uint Word(int word)
    {
        if (_pending is { } pending)
        {
            return word == 1 ? pending.One : pending.Two;
        }

        var q = _session.State.Self.Qualities;
        return word == 1 ? q.CharacterOptions1 : q.CharacterOptions2;
    }
}

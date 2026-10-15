using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Options;

/// <summary>
/// The character's options for the session (D42; docs/retail-ui.md section 21.8): reads the two option words of <see cref="PlayerQualities"/> and, when the player changes one in the options panel, asks the
/// session to apply it (<see cref="IActionApi.SetSessionOption"/>): the reducer owns the session-local override and merges it over the PlayerDescription's words, so every reader (the vitals, the vivid
/// target indicator, the login check) sees the player's value whatever order it runs in, a description that arrives later keeps the change, and nothing here writes the session's state.
/// <b>Nothing is ever sent to the server</b> (retail sent <c>GameActionSetCharacterOptions</c> through <c>PlayerModule::SetOption</c>; Lifestone does not, <c>TODO(g6-options-save)</c>).
/// The override lasts until the character leaves the world (or the session resets): a new world entry starts from the server's words.
/// </summary>
public sealed class GameOptions
{
    private readonly IGameSessionView _session;
    private readonly ILogSink _log;

    /// <summary>The options over <paramref name="session"/>.</summary>
    public GameOptions(IGameSessionView session, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);
        _session = session;
        _log = log;
    }

    /// <summary>Told after an option changed (retail's <c>RecvNotice_PlayerOptionChanged</c>); carries the option.</summary>
    public event Action<PlayerOptionDef>? Changed;

    /// <summary>How many changes the player made.</summary>
    public int ChangeCount { get; private set; }

    /// <summary>True when the player's options ask for the side-by-side vitals.</summary>
    public bool SideBySideVitals => Get(PlayerOptionTable.SideBySideVitals);

    /// <summary>True when the vivid targeting indicator is on (the bit the indicator reads).</summary>
    public bool VividTargetingIndicator => Get(PlayerOptionTable.VividTargetingIndicator);

    /// <summary>The option's value now (<c>PlayerModule::GetOption</c>).</summary>
    public bool Get(PlayerOptionDef option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return (Word(option.Word) & option.Mask) != 0;
    }

    /// <summary>
    /// Sets <paramref name="option"/> for the session. A ghosted option cannot change: it returns false and logs, whoever asks. Nothing goes to the server.
    /// </summary>
    public bool TrySet(PlayerOptionDef option, bool value)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!option.Honoured)
        {
            _log.Warn("options", $"the option {option.Name} is ghosted in Lifestone M2 and cannot change ({option.Effect})");
            return false;
        }

        if (Get(option) == value)
        {
            return true;
        }

        _session.Actions.SetSessionOption(option.Word, option.Mask, value);
        ChangeCount++;
        _log.Info("options", $"option {option.Name} is {(value ? "on" : "off")} for this session ({option.Effect}); nothing is sent to the server (D42, TODO(g6-options-save))");
        Changed?.Invoke(option);
        return true;
    }

    private uint Word(int word)
    {
        var q = _session.State.Self.Qualities;
        return word == 1 ? q.CharacterOptions1 : q.CharacterOptions2;
    }
}

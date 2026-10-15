using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Flow;

/// <summary>
/// A UI mode's screen (retail's <c>UIMainFramework</c> subclasses: <c>gmDataPatchUI</c>, <c>gmCharacterManagementUI</c>,
/// <c>gmGamePlayUI</c>, ...): created when its mode starts and disposed when the next one does. The wave-2 screen tracks
/// (G2-G4) implement it.
/// </summary>
public interface IUiScreen : IDisposable
{
    /// <summary>Shows an error the mode was queued with (<c>UIFlow::QueueUIModeWithError</c>), e.g. a character error on character management.</summary>
    void DisplayError(in UiStringRef text);

    /// <summary>One frame of the screen (<c>UIFlow::Update</c> calls the current screen's).</summary>
    void Update();

    /// <summary>Shows or hides the screen: shown once made (and after its error), hidden just before it is disposed.</summary>
    void Show(bool visible);
}

/// <summary>The UI mode changed (a Lifestone notice for hosts and tests).</summary>
/// <param name="Previous">The mode before (0 at the start).</param>
/// <param name="Mode">The mode now.</param>
/// <param name="Error">The error the mode was queued with, or null.</param>
public readonly record struct UiModeChanged(uint Previous, uint Mode, UiStringRef? Error);

/// <summary>
/// The UI modes (retail's <c>UIFlow</c>): one screen at a time; a mode queued now (<see cref="QueueUIMode"/>) starts at
/// the next frame, the old screen disposed first. Modes are registered with their screen's factory
/// (<see cref="RegisterFrameworkClass"/>); <see cref="GmUiFlow"/> registers retail's.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>UIFlow::UIFlow</c> (11.4186 0x004795C0, 11.6096 0x004799C0: it hears every frame),
/// <c>RegisterFrameworkClass</c> (11.4186 0x00479850, 11.6096 0x00479C50: the first registration of a mode stays),
/// <c>QueueUIMode</c> (11.4186 0x004793C0, 11.6096 0x004797C0), <c>QueueUIModeWithError</c> (11.4186 0x004793F0, 11.6096
/// 0x004797F0), <c>UseNewMode</c> (11.4186 0x004796A0, 11.6096 0x00479AA0), <c>ListenToGlobalMessage</c> (11.4186
/// 0x004797A0, 11.6096 0x00479BA0) and <c>Update</c> (11.4186 0x00479430, 11.6096 0x00479830).
/// </para>
/// <para>
/// A queued mode replaces any mode queued before it (and its error). A mode with no registered screen does nothing and
/// stays queued, as retail. When the mode changes, the old screen is hidden and disposed, then every dialog closes
/// (<see cref="Dialogs.DialogFactory.Reset"/>: each callback hears its data and <see cref="Dialogs.DialogFactory.DialogClosed"/>
/// reports it), as retail's every mode screen is a <c>UIMainFramework</c> whose destructor
/// (<c>UIMainFramework::~UIMainFramework</c>, 11.4186 0x006A0CC0, 11.6096 0x006A1AE0, and its scalar-deleting destructor,
/// 11.4186 0x006A0D00, 11.6096 0x006A1B20) calls <c>DialogFactory::Reset</c>. Then the new screen is made, shown its error
/// (when the error is a valid string) and shown. A dialog a close callback opens during that reset survives it and shows
/// over the new screen.
/// </para>
/// <para>
/// Lifestone takes the queued mode and its error before the old screen goes, so a mode queued while it goes (by its
/// <see cref="IDisposable.Dispose"/> or a dialog's close callback) stays queued for the next frame. Retail reads
/// <c>_nextMode</c> again after deleting the old screen, so such a mode would become <c>_curMode</c> while the screen made
/// is the one queued first; nothing in retail's own flows queues a mode there.
/// </para>
/// </remarks>
public sealed class UiFlow : IUiGlobalListener, IDisposable
{
    private readonly UiElementManager _manager;
    private readonly Dictionary<uint, Func<IUiScreen?>> _modes = [];
    private long _frames; // the manager's frames this flow has heard
    private long _notBeforeFrame; // the queued mode starts at this frame at the earliest (QueueUIModeAfterAFrame)

    /// <summary>A flow over <paramref name="manager"/>; it hears the manager's frames.</summary>
    public UiFlow(UiElementManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
    }

    /// <summary>The mode showing (<c>_curMode</c>), or 0.</summary>
    public uint CurrentMode { get; private set; }

    /// <summary>The mode queued (<c>_nextMode</c>), or 0.</summary>
    public uint NextMode { get; private set; }

    /// <summary>The error queued with the next mode (<c>_nextText</c>), or null.</summary>
    public UiStringRef? NextError { get; private set; }

    /// <summary>The screen showing (<c>_curUI</c>), or null.</summary>
    public IUiScreen? CurrentScreen { get; private set; }

    /// <summary>The modes registered.</summary>
    public IEnumerable<uint> RegisteredModes => _modes.Keys;

    /// <summary>Raised after the mode changed.</summary>
    public event Action<UiModeChanged>? ModeChanged;

    /// <summary><c>UIFlow::RegisterFrameworkClass</c>: mode <paramref name="mode"/> shows the screen <paramref name="create"/> makes (null makes none). The first registration stays.</summary>
    public bool RegisterFrameworkClass(uint mode, Func<IUiScreen?> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        return _modes.TryAdd(mode, create);
    }

    /// <summary><c>UIFlow::QueueUIMode</c>: <paramref name="mode"/> starts at the next frame, with no error.</summary>
    public void QueueUIMode(uint mode)
    {
        NextMode = mode;
        NextError = null;
        _notBeforeFrame = 0;
    }

    /// <summary>
    /// <see cref="QueueUIMode"/> for a mode whose screen is expensive to make: it starts at the first frame after the next one, not at the next one. The in-game shell is queued this way when the
    /// player is created, so the screen's construction (about 30 ms the first time) is a frame of its own and not part of the arrival's frame, which already holds the world's start and the
    /// tunnel's (DoD 15, no frame over 50 ms between Enter and walking; docs/session.md section 27). A deviation from retail's literal "next frame", not of its behaviour.
    /// </summary>
    public void QueueUIModeAfterAFrame(uint mode)
    {
        QueueUIMode(mode);
        _notBeforeFrame = _frames + 1;
    }

    /// <summary><c>UIFlow::QueueUIModeWithError</c>: <paramref name="mode"/> starts at the next frame and its screen shows <paramref name="error"/>.</summary>
    public void QueueUIModeWithError(uint mode, in UiStringRef error)
    {
        NextMode = mode;
        NextError = error;
        _notBeforeFrame = 0;
    }

    /// <summary><c>UIFlow::Update</c>: the current screen's frame (the host calls it once a frame, after the manager's update).</summary>
    public void Update() => CurrentScreen?.Update();

    /// <inheritdoc/>
    void IUiGlobalListener.ListenToGlobalMessage(uint messageId, int data)
    {
        if (messageId == UiGlobalMessageIds.Frame)
        {
            if (_frames >= _notBeforeFrame)
            {
                UseNewMode();
            }

            _frames++;
        }
    }

    /// <summary>
    /// <c>UIFlow::UseNewMode</c>: a queued mode with a registered screen starts: the old screen is hidden and disposed, every
    /// dialog closes (<c>DialogFactory::Reset</c>, from the old screen's <c>UIMainFramework</c> destructor; only when there
    /// was an old screen), and the new screen is made, shown the queued error and shown.
    /// </summary>
    public void UseNewMode()
    {
        if (NextMode == 0 || !_modes.TryGetValue(NextMode, out var create))
        {
            return;
        }

        var previous = CurrentMode;
        var mode = NextMode;
        var error = NextError;
        NextMode = 0;
        NextError = null;
        var old = CurrentScreen;
        old?.Show(false);
        CurrentScreen = null;
        if (old is not null)
        {
            old.Dispose();
            _manager.Dialogs.Reset();
        }

        CurrentMode = mode;
        CurrentScreen = create();
        if (CurrentScreen is not null)
        {
            if (error is { } text && IsValid(text))
            {
                CurrentScreen.DisplayError(text);
            }

            CurrentScreen.Show(true);
        }

        _manager.LogOnce($"flow:{CurrentMode:X8}:{CurrentScreen is null}", LogSeverity.Info, $"UI mode 0x{CurrentMode:X8} ({UiModes.NameOf(CurrentMode)}) started{(CurrentScreen is null ? " with no screen" : string.Empty)}");
        ModeChanged?.Invoke(new UiModeChanged(previous, CurrentMode, error));
    }

    /// <summary><c>StringInfo::IsValid</c> as <c>UseNewMode</c> asks it: a literal, or a string id in a table.</summary>
    private static bool IsValid(in UiStringRef text) => text.IsLiteral || (text.StringId != 0 && text.TableId != 0);

    /// <summary>
    /// Stops hearing frames and disposes the current screen, closing every dialog after it as a mode change does
    /// (<c>UIMainFramework::~UIMainFramework</c> calls <c>DialogFactory::Reset</c>).
    /// </summary>
    public void Dispose()
    {
        _manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        var screen = CurrentScreen;
        CurrentScreen = null;
        if (screen is not null)
        {
            screen.Dispose();
            _manager.Dialogs.Reset();
        }
    }
}

/// <summary>The UI modes <c>gmUIFlow::gmUIFlow</c> registers, by their retail ids.</summary>
public static class UiModes
{
    /// <summary>0x10000001: the intro movies (<c>gmIntroUI</c>; skipped in M2, D38).</summary>
    public const uint Intro = 0x10000001;

    /// <summary>0x10000002: the disconnected screen (<c>gmDisconnectedUI</c>).</summary>
    public const uint Disconnected = 0x10000002;

    /// <summary>0x10000003: the data check (<c>gmDataPatchUI</c>), the first mode.</summary>
    public const uint DataPatch = 0x10000003;

    /// <summary>0x10000005: the credits (<c>gmCreditsUI</c>; "not in M2", D38).</summary>
    public const uint Credits = 0x10000005;

    /// <summary>0x10000008: the game (<c>gmGamePlayUI</c>).</summary>
    public const uint GamePlay = 0x10000008;

    /// <summary>0x10000009: the epilogue (<c>gmEpilogueUI</c>).</summary>
    public const uint Epilogue = 0x10000009;

    /// <summary>0x1000000A: character management (<c>gmCharacterManagementUI</c>).</summary>
    public const uint CharacterManagement = 0x1000000A;

    /// <summary>0x1000000B: character creation (<c>gmCharGenMainUI</c>).</summary>
    public const uint CharGen = 0x1000000B;

    /// <summary>The modes in the order <c>gmUIFlow::gmUIFlow</c> registers them.</summary>
    public static IReadOnlyList<uint> RetailOrder { get; } = [DataPatch, Intro, CharacterManagement, GamePlay, Epilogue, Disconnected, CharGen, Credits];

    /// <summary>The retail screen class of <paramref name="mode"/>, or its id in hex.</summary>
    public static string NameOf(uint mode) => mode switch
    {
        Intro => "gmIntroUI",
        Disconnected => "gmDisconnectedUI",
        DataPatch => "gmDataPatchUI",
        Credits => "gmCreditsUI",
        GamePlay => "gmGamePlayUI",
        Epilogue => "gmEpilogueUI",
        CharacterManagement => "gmCharacterManagementUI",
        CharGen => "gmCharGenMainUI",
        _ => $"mode 0x{mode:X8}",
    };
}

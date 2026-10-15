using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Flow;

/// <summary>
/// Where the game's text notices go (retail's <c>DisplayStringInfo</c> notices): a text type and a string. The portal
/// track's presenter sends "In Portal Space - Please Wait..." as type 26, which only the spew box shows.
/// </summary>
public interface IUiNoticeSink
{
    /// <summary>Shows <paramref name="text"/> as text type <paramref name="textType"/>.</summary>
    void DisplayStringInfo(int textType, in UiStringRef text);
}

/// <summary>A notice no window took (<see cref="UiNoticeRouter.Unheard"/>).</summary>
/// <param name="TextType">The text type.</param>
/// <param name="Text">The string.</param>
public readonly record struct UiNotice(int TextType, UiStringRef Text);

/// <summary>
/// The retail UI's <see cref="IUiNoticeSink"/>: type 26 goes to the spew box, every other type to the chat windows,
/// each a sink the game-play screen registers (G4). Before a window exists, a notice is logged as a placeholder and
/// raised as <see cref="Unheard"/>, so the host can show its labelled placeholder line (D32).
/// </summary>
/// <remarks>
/// <para>
/// Retail sends the notice through <c>ECM_UI::SendNotice_DisplayStringInfo</c> (11.4186 0x006925B0, 11.6096 0x00693500)
/// to <c>ClientCommunicationSystem::RecvNotice_DisplayStringInfo</c> (11.4186 0x0056E890, 11.6096 0x0056F640), which
/// passes the final string to every window; <c>gmSpewBoxUI::RecvNotice_DisplayFinalStringInfo</c> (11.4186 0x004D60A0,
/// 11.6096 0x004D6D00) keeps type 26 (0x1A) and nothing else. The chat windows' own type filters are G4's.
/// </para>
/// </remarks>
public sealed class UiNoticeRouter : IUiNoticeSink
{
    /// <summary>The text type the spew box shows (and nothing else does).</summary>
    public const int SpewBoxTextType = 26;

    private readonly ILogSink _log;

    /// <summary>A router logging to <paramref name="log"/>.</summary>
    public UiNoticeRouter(ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>The most notices held for windows that are still being made.</summary>
    public const int HeldLimit = 16;

    private readonly List<UiNotice> _held = [];
    private IUiNoticeSink? _spewBox;
    private IUiNoticeSink? _chat;
    private bool _expectWindows;

    /// <summary>The spew box (<c>gmSpewBoxUI</c>), or null before the game-play screen exists; a notice held for it is shown when it is set.</summary>
    public IUiNoticeSink? SpewBox
    {
        get => _spewBox;
        set
        {
            _spewBox = value;
            Deliver(forSpewBox: true);
        }
    }

    /// <summary>The chat windows, or null before the game-play screen exists; a notice held for them is shown when they are set.</summary>
    public IUiNoticeSink? Chat
    {
        get => _chat;
        set
        {
            _chat = value;
            Deliver(forSpewBox: false);
        }
    }

    /// <summary>
    /// True while the game screen is making its windows over several frames: a notice with no window to take it is held (up to <see cref="HeldLimit"/>) and shown when its window is registered,
    /// instead of being raised as <see cref="Unheard"/> for the host's labelled line. Setting it false hands what is still held to <see cref="Unheard"/>.
    /// </summary>
    public bool ExpectWindows
    {
        get => _expectWindows;
        set
        {
            _expectWindows = value;
            if (!value && _held.Count > 0)
            {
                var left = _held.ToArray();
                _held.Clear();
                foreach (var notice in left)
                {
                    Unheard?.Invoke(notice);
                }
            }
        }
    }

    /// <summary>The notices held for windows that are not registered yet.</summary>
    public int HeldCount => _held.Count;

    /// <summary>Raised for a notice no window took.</summary>
    public event Action<UiNotice>? Unheard;

    /// <summary>True when the spew box shows text type <paramref name="textType"/>.</summary>
    public static bool IsForSpewBox(int textType) => textType == SpewBoxTextType;

    /// <inheritdoc/>
    public void DisplayStringInfo(int textType, in UiStringRef text)
    {
        var sink = IsForSpewBox(textType) ? SpewBox : Chat;
        if (sink is not null)
        {
            sink.DisplayStringInfo(textType, text);
            return;
        }

        if (_expectWindows)
        {
            if (_held.Count >= HeldLimit)
            {
                _held.RemoveAt(0);
            }

            _held.Add(new UiNotice(textType, text));
            _log.Debug(UiElementManager.Category, $"notice type {textType} ({text}) is held for the {(IsForSpewBox(textType) ? "spew box" : "chat")}, which the game screen is still making");
            return;
        }

        _log.Info(UiElementManager.Category, $"placeholder: notice type {textType} ({text}) has no window yet ({(IsForSpewBox(textType) ? "spew box" : "chat")}); the host shows a labelled line");
        Unheard?.Invoke(new UiNotice(textType, text));
    }

    private void Deliver(bool forSpewBox)
    {
        var sink = forSpewBox ? _spewBox : _chat;
        if (sink is null || _held.Count == 0)
        {
            return;
        }

        var mine = _held.Where(n => IsForSpewBox(n.TextType) == forSpewBox).ToArray();
        _held.RemoveAll(n => IsForSpewBox(n.TextType) == forSpewBox);
        foreach (var notice in mine)
        {
            sink.DisplayStringInfo(notice.TextType, notice.Text);
        }
    }
}

using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// The dice the UI rolls: retail's <c>Random::RollDice</c>, which the media machine uses for pause lengths and for the
/// probability of jumps, messages and state changes (<see cref="UiMediaMachine"/>). Injected so tests are deterministic.
/// </summary>
public interface IUiRandom
{
    /// <summary>A value in [<paramref name="min"/>, <paramref name="max"/>].</summary>
    double RollDice(double min, double max);
}

/// <summary>An <see cref="IUiRandom"/> over a seeded <see cref="Random"/> (the default; same seed, same rolls).</summary>
/// <param name="seed">The seed.</param>
public sealed class UiSeededRandom(int seed) : IUiRandom
{
    private readonly Random _random = new(seed);

    /// <inheritdoc/>
    public double RollDice(double min, double max) => min + (_random.NextDouble() * (max - min));
}

/// <summary>
/// Where UI sounds go: the media machine's sound entries (<c>MediaMachine::Update_Sound</c>, 11.4186 0x004658B0,
/// 11.6096 0x004659E0) and, later, the widgets' clicks. A narrow seam the host adapts to the portal track's
/// <c>ISoundSink</c> (m2-plan.md §4.8) once that lands; the runtime never plays audio itself.
/// </summary>
public interface IUiSoundSink
{
    /// <summary>Plays sound type <paramref name="soundType"/> of SoundTable <paramref name="soundTableId"/> from the listener's position (<c>SoundManager::PlaySoundFromCenter</c>).</summary>
    void PlayFromCenter(uint soundTableId, uint soundType);

    /// <summary>Plays Wave <paramref name="waveId"/> from the listener's position at <paramref name="volume"/> (a sound entry with no sound type).</summary>
    void PlayWaveFromCenter(uint waveId, float volume);
}

/// <summary>An <see cref="IUiSoundSink"/> that plays nothing (the default where no host sink is wired).</summary>
public sealed class NullUiSoundSink : IUiSoundSink
{
    /// <summary>The one instance.</summary>
    public static readonly NullUiSoundSink Instance = new();

    private NullUiSoundSink()
    {
    }

    /// <inheritdoc/>
    public void PlayFromCenter(uint soundTableId, uint soundType)
    {
    }

    /// <inheritdoc/>
    public void PlayWaveFromCenter(uint waveId, float volume)
    {
    }
}

/// <summary>
/// The retail UI glitches Lifestone fixes by default, each of which the host can restore (the fidelity rule of
/// docs/milestones/m1-explore-dereth.md: a glitch fix is the documented default and a flag restores retail;
/// docs/retail-ui.md §14).
/// </summary>
[Flags]
public enum UiRetailGlitches
{
    /// <summary>Every fix on (the default).</summary>
    None = 0,

    /// <summary>
    /// Retail's dialog queueing (<see cref="Dialogs.DialogFactory.RetailQueueing"/>): a dialog a topmost one displaced is
    /// dropped, a closed waiting dialog opens later anyway, and the dialogs waiting behind one that is not created keep
    /// waiting.
    /// </summary>
    DialogQueueing = 1,

    /// <summary>
    /// Retail's hit test through an A8 mask (<see cref="UiImage.PointTestOverlay(int, int, bool)"/>): the pixel's dword
    /// with its alpha byte masked off, so the next three alpha bytes decide.
    /// </summary>
    A8PointTest = 2,

    /// <summary>
    /// Retail's release of the pressed keys when the window loses the focus (<see cref="Input.UiInputRouter.RetailFocusLoss"/>):
    /// the end it fires for each held action names input map 0, which the action map does not have, so the action counts
    /// as an impulse and does not end. A held auto-repeating or continuous action keeps repeating, and every held action
    /// stays in progress, until its key is pressed and released again.
    /// </summary>
    FocusLossActions = 4,

    /// <summary>
    /// Retail's heritage tabs (<c>gmCharGenMainUI::ListenToElementMessage</c>, 11.4186 0x004E9450, 11.6096 0x004EA0E0): the profession, skills and
    /// town tabs come back when a heritage is chosen after an Olthoi heritage hid them, except for the Lugian button, whose id the handler
    /// does not list. Retail leaves the tabs hidden after Olthoi then Lugian until the page changes; Lifestone shows them at once.
    /// </summary>
    CharGenHeritageTabs = 8,

    /// <summary>
    /// Retail's summary page names only heritages 1 to 4 (<c>gmCGSummaryPage::SetSummaryText</c>, 11.4186 0x0047B1D0, 11.6096 0x0047B590): the "Heritage:" line is
    /// empty for the Shadowbound, Gear Knight, Tumerok, Lugian, Empyrean, Penumbraen, Undead and Olthoi characters, whose heritages the page predates.
    /// Lifestone names every heritage.
    /// </summary>
    CharGenSummaryHeritage = 16,

    /// <summary>
    /// Retail's camera on the appearance page (<c>gmCGAppearancePage::Update</c>, 11.4186 0x0047E8F0, 11.6096 0x0047ECA0): every update, which runs when the page is
    /// shown again, on Random and on a gender button, puts the camera at the face whatever the half showing and leaves the zoom flag untouched, so the clothes half
    /// shows over a close-up of the head and the zoom-out button does nothing until zoom-in has been pressed. Lifestone keeps the camera where the zoom state says.
    /// </summary>
    CharGenAppearanceCamera = 32,

    /// <summary>
    /// Retail's empty name (<c>gmCGSummaryPage::ListenToElementMessage</c>, 11.4186 0x0047BF40, 11.6096 0x0047C300; <c>gmCharGenMainUI::DoFinish</c>, 0x004E9170, 0x004E9E00): an edit that leaves the name field
    /// empty does not reach the state, so the state keeps the last non-empty text ("Bob", or "B" after backspacing) and Finish creates a character under a name the
    /// field does not show. Lifestone clears the state's name, so Finish says there is no name.
    /// </summary>
    CharGenEmptyName = 64,

    /// <summary>
    /// Retail's held headgear (<c>gmCGAppearancePage::SetChoice</c>, 11.4186 0x0047D4B0, 11.6096 0x0047D860): the face half holds the headgear style and takes the hat
    /// off, and only the clothes half gives it back, so a headgear chosen and then left in the face half (Next, a tab, Finish), or rolled by Random on the summary page
    /// while the appearance page is in the face half, is dropped from the created character. Lifestone gives it back when the page is left and holds the state's
    /// headgear again when the face half shows.
    /// </summary>
    CharGenHeldHeadgear = 128,

    /// <summary>
    /// Retail's stale effects info box (<c>gmEffectsUI::RebuildList</c>, 11.6096 0x004B8F70): when the registry has nothing in effect at all, the function returns before
    /// <c>UpdateSelection</c> (0x004B8BB0), so the last spell to end leaves its name and description in the info box and its selection standing. Lifestone clears the selection and the info box when the list empties.
    /// </summary>
    EffectsStaleInfo = 256,

    /// <summary>Every glitch restored.</summary>
    All = DialogQueueing | A8PointTest | FocusLossActions | CharGenHeritageTabs | CharGenSummaryHeritage | CharGenAppearanceCamera | CharGenEmptyName | CharGenHeldHeadgear | EffectsStaleInfo,
}

/// <summary>
/// Everything the element runtime needs from its host, in one place: layouts, logging, dice, sound, fonts, strings and
/// the clipboard. Only <see cref="Descs"/> is required; the rest default to inert stand-ins, and an element that needs a
/// missing service says so loudly.
/// </summary>
public sealed class UiServices
{
    /// <summary>The layouts (retail's language dat, or synthetic ones).</summary>
    public required IUiDescSource Descs { get; init; }

    /// <summary>Where the runtime logs (category <c>retailui</c>).</summary>
    public ILogSink Log { get; init; } = NullLogSink.Instance;

    /// <summary>The dice (<c>Random::RollDice</c>).</summary>
    public IUiRandom Random { get; init; } = new UiSeededRandom(0);

    /// <summary>UI sounds.</summary>
    public IUiSoundSink Sound { get; init; } = NullUiSoundSink.Instance;

    /// <summary>Retail fonts (<see cref="UiFontsSource"/> over R5's <c>UiFonts</c>); null draws no text, loudly.</summary>
    public IUiFontSource? Fonts { get; init; }

    /// <summary>Retail strings (<see cref="UiStringsSource"/> over R6's <c>UiStrings</c>); null shows table references as nothing, loudly.</summary>
    public IUiStringSource? Strings { get; init; }

    /// <summary>The retail glitches to restore (none by default: Lifestone's fixes; docs/retail-ui.md §14).</summary>
    public UiRetailGlitches RetailGlitches { get; init; }

    /// <summary>
    /// True to show the examine window's item icon (<c>--examine-icon on</c>, M4 4.1 fix, review R8): an addition to retail, whose merged floaty layout has no icon element (<c>ItemExamineUI::SetAppraiseInfo</c> 0x004B7ED0 skips it), so
    /// the default is false and the panel is drawn as retail draws it.
    /// </summary>
    public bool ExamineIcon { get; init; }

    /// <summary>The clipboard for copy, cut and paste in text fields (D39); null disables them.</summary>
    public IUiClipboard? Clipboard { get; init; }

    /// <summary>
    /// The caret's blink half-period in seconds. Retail asks Windows (<c>GetCaretBlinkTime</c>, 530 ms by default); a host
    /// may pass the user's setting.
    /// </summary>
    public double CaretBlinkSeconds { get; init; } = 0.53;

    /// <summary>The images the elements draw (decoded RenderSurfaces); used by the element runtime only for image sizes.</summary>
    public IUiImageSource? Images { get; init; }

    /// <summary>The dats' enum-to-DID maps (<c>DBObj::GetByEnum</c>), for the shell pieces that name an image by enum (the vivid target indicator's group 0x10000009). Null in a rig without dats.</summary>
    public Lifestone.Data.Dat.EnumIdMaps? EnumMaps { get; init; }

    /// <summary>Where the viewports' holes go (the host's 3D views); null leaves them empty, loudly.</summary>
    public IUiViewportHost? ViewportHost { get; init; }
}

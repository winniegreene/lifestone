using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The start town page (retail's <c>gmCGTownPage</c>, element 0x100003D5, class 0x1000003D): four town buttons (Sanamar 0x1000040B, Holtburg
/// 0x1000040D, Yaraq 0x1000040E and Shoushi 0x1000040F) on a map, and a text (0x10000409) with the chosen town's description and the page's how-to line.
/// A click picks the town's CharGen start area (<see cref="CharacterCreation.CharGenState.SetStartArea"/>); Sanamar needs the Throne of Destiny and says
/// so when the account lacks it.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCGTownPage::InitializePage</c> (11.4186 0x0047C6D0, 11.6096 0x0047CA80), <c>Update</c> (0x0047C460, 0x0047C810),
/// <c>SetTown</c> (0x0047C360, 0x0047C710), <c>SetTownString</c> (0x0047C1F0, 0x0047C5A0) and <c>ListenToElementMessage</c> (0x0047C480, 0x0047C830).
/// The page shows the state's start area (<c>Update</c> adds one to get retail's town number): the town's button highlighted, the others normal, the
/// page itself in the town's state (which shows its name and map), and the text <c>ID_CharGen_*Text</c> + a blank line + <c>ID_CharGen_TownHowTo</c> + a
/// line feed. A start area the page has no town for (the Olthoi's, which never reach the page) leaves every button normal and the text to the how-to line.
/// </para>
/// <para>
/// <b>Which town the character starts in.</b> The page sends the start area in the create request. The bundled ACE server places a new character at the
/// first location of start area 0, the Holtburg Training Academy (interiors.md §6), whichever town the page chose; the choice is retail's, kept and sent.
/// </para>
/// </remarks>
public sealed class CharGenTownPage : CharGenPage
{
    /// <summary>The description text.</summary>
    public const uint TextId = 0x10000409;

    private readonly Dictionary<int, UiButton> _buttons = [];
    private readonly UiTextElement? _text;

    /// <summary>The town page over <paramref name="root"/>: its elements found, and the page shown for the screen's start area.</summary>
    public CharGenTownPage(CharGenMainScreen main, UiElement root)
        : base(main, root, "gmCGTownPage")
    {
        foreach (var entry in CharGenRules.Towns)
        {
            if (Find<UiButton>(entry.ButtonId) is { } button)
            {
                _buttons[entry.Town] = button;
            }
        }

        _text = Find<UiTextElement>(TextId);
        Update();
    }

    /// <summary>The text now shown.</summary>
    public string Text => _text?.Text ?? string.Empty;

    /// <summary>The state of town <paramref name="town"/>'s button (1 Holtburg, 2 Shoushi, 3 Yaraq, 4 Sanamar), or 0.</summary>
    public uint ButtonState(int town) => _buttons.TryGetValue(town, out var button) ? button.State : 0;

    /// <summary>The state the page itself is in.</summary>
    public uint PageState => Root.State;

    /// <summary><c>gmCGTownPage::Update</c>: the page shows the state's start area (town number = start area + 1).</summary>
    public override void Update() => SetTown(Main.State.StartArea + 1);

    /// <summary>
    /// <c>gmCGTownPage::SetTown</c>: every button normal and the chosen town's highlighted, the page in the town's state, the start area set (the town
    /// number less one) and the text built; <paramref name="town"/> 0 or outside 1 to 4 selects nothing.
    /// </summary>
    public void SetTown(int town)
    {
        foreach (var button in _buttons.Values)
        {
            button.SetState(UiStateIds.Normal);
        }

        var entry = CharGenRules.Towns.FirstOrDefault(t => t.Town == town);
        if (entry is not null)
        {
            if (_buttons.TryGetValue(entry.Town, out var selected))
            {
                selected.SetState(UiStateIds.Highlight);
            }

            Root.SetState(entry.PageState);
        }

        Main.State.SetStartArea(town - 1);
        SetTownString(entry);
    }

    /// <summary><c>gmCGTownPage::SetTownString</c>: the town's description, a blank line, the how-to line and a line feed.</summary>
    private void SetTownString(CharGenTownEntry? entry)
    {
        if (_text is null)
        {
            return;
        }

        if (entry is not null)
        {
            _text.SetStringInfoWithFont(Pregame(entry.TextToken), 0, 0);
        }
        else
        {
            _text.SetText(string.Empty);
        }

        _text.AppendText("\n\n");
        _text.AppendStringInfoWithFont(Pregame("ID_CharGen_TownHowTo"), 0, 0);
        _text.AppendText("\n");
    }

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick || CharGenRules.TownOfButton(message.ElementId) is not { } entry)
        {
            return UiListenResult.Default;
        }

        if (entry.Town == 4 && !Main.HasThroneOfDestiny)
        {
            Main.MakeToDWarningDialog();
            Update();
        }
        else
        {
            SetTown(entry.Town);
        }

        return UiListenResult.Default;
    }
}

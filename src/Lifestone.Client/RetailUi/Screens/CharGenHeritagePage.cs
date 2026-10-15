using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The heritage page (retail's <c>gmCGHeritagePage</c>, element 0x100003D1, class 0x10000039): thirteen heritage buttons, the heritage's picture
/// (0x100003BE, one state for each) and a text (0x100003C4) with the skills every heritage starts with, the ones this heritage trains, and its
/// description. A click on a heritage calls <see cref="CharacterCreation.CharGenState.SetHeritageGroup"/>; Viamontian asks the account for the
/// Throne of Destiny first and says so when it lacks it.
/// </summary>
/// <remarks>
/// Re-expressed from <c>gmCGHeritagePage::InitializePage</c> (11.4186 0x00483A10, 11.6096 0x00483DD0), <c>Update</c> (0x00483210, 0x004835D0) and
/// <c>ListenToElementMessage</c> (0x00483860, 0x00483C20). The text is built as retail builds it: the header in font colour 1, then the body in colour 0
/// (<c>UIElement_Text::SetStringInfoWithFont</c> and <c>AppendStringInfoWithFont</c>). The Penumbraen get the Shadowbound strings, as retail's
/// <c>Update</c> gives them (a case shared with heritage 5).
/// </remarks>
public sealed class CharGenHeritagePage : CharGenPage
{
    /// <summary>The text.</summary>
    public const uint TextId = 0x100003C4;

    /// <summary>The picture field (its state shows the heritage).</summary>
    public const uint BackgroundId = 0x100003BE;

    private readonly Dictionary<uint, UiElement> _buttons = [];
    private readonly UiTextElement? _text;
    private readonly UiElement? _background;

    /// <summary>The heritage page over <paramref name="root"/>: its elements found, and the page shown for the screen's character.</summary>
    public CharGenHeritagePage(CharGenMainScreen main, UiElement root)
        : base(main, root, "gmCGHeritagePage")
    {
        foreach (var entry in CharGenRules.Heritages)
        {
            if (Find<UiButton>(entry.ButtonId) is { } button)
            {
                _buttons[entry.Heritage] = button;
            }
        }

        _text = Find<UiTextElement>(TextId);
        _background = Find<UiElement>(BackgroundId);
        Update();
    }

    /// <summary>The text now shown.</summary>
    public string Text => _text?.Text ?? string.Empty;

    /// <summary>The state of heritage <paramref name="heritage"/>'s button, or 0.</summary>
    public uint ButtonState(uint heritage) => _buttons.TryGetValue(heritage, out var button) ? button.State : 0;

    /// <summary>The state the picture is in.</summary>
    public uint BackgroundState => _background?.State ?? 0;

    /// <summary><c>gmCGHeritagePage::Update</c>: the buttons unselected but the character's heritage's, its picture, and the text.</summary>
    public override void Update()
    {
        foreach (var button in _buttons.Values)
        {
            button.SetState(CharGenRules.UnselectedState);
        }

        var entry = CharGenRules.HeritageEntry(Main.State.Heritage);
        if (_text is not null)
        {
            _text.SetStringInfoWithFont(Pregame("ID_CharGen_Heritage_StartingSkills_Header"), 0, 1);
            _text.AppendStringInfoWithFont(Pregame("ID_CharGen_Heritage_StartingSkills"), 0, 0);
            _text.AppendStringInfoWithFont(Pregame("ID_CharGen_Heritage_BonusSkills_Trained_Header"), 0, 1);
            if (entry is not null)
            {
                _text.AppendStringInfoWithFont(Pregame(entry.BonusSkillsToken), 0, 0);
            }

            _text.AppendText("\n");
            if (entry is not null)
            {
                _text.AppendStringInfoWithFont(Pregame(entry.DescriptionToken), 0, 0);
            }
        }

        if (entry is not null)
        {
            if (_buttons.TryGetValue(entry.Heritage, out var button))
            {
                button.SetState(CharGenRules.SelectedState);
            }

            _background?.SetState(entry.BackgroundState);
        }
    }

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick || CharGenRules.HeritageOfButton(message.ElementId) is not { } entry)
        {
            return UiListenResult.Default;
        }

        if (entry.Heritage == CharGenMainScreen.ViamontianHeritage && !Main.HasThroneOfDestiny)
        {
            Main.MakeToDWarningDialog();
        }
        else
        {
            Main.State.SetHeritageGroup(entry.Heritage);
        }

        Update();
        return UiListenResult.Default;
    }
}

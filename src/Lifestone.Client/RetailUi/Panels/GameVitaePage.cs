using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The vitae page (M3 2.2 a3; page 15, <c>gmVitaeUI</c>, class 0x10000020; 11.6096 0x004A75C0 to 0x004A7B80). Its text box (0x100001C3) says what the death penalty is and what clears it. A controller builds
/// nothing until the page shows.
/// </summary>
/// <remarks>
/// <para><b>The text.</b> <c>Update</c> (0x004A7780) shows nothing for a hidden page. With a penalty (100 less the vitae multiplier as a percentage, rounded half up, above 0) it sets the text to
/// <c>ID_Vitae_Text_Vitae</c> and appends <c>ID_Vitae_Text_Skills</c> (both with the percentage) and <c>ID_Vitae_Text_Experience</c> with the experience still owed: the pool threshold of the multiplier and the level
/// (<c>VitaeSystem::VitaeCPPoolThreshold</c>, 11.6096 0x005C9FB0) less the vitae experience pool (integer property 0x81; the level is property 0x8B, else 0x19). With no penalty it sets
/// <c>ID_Vitae_Text_Full</c>, the "no vitae" state (<see cref="EffectRules"/>).</para>
/// <para><b>When it redraws.</b> The description arriving, the vitae changing (<c>RecvNotice_VitaeChanged</c> 0x004A7AA0) and the pool property (0x81) changing mark it dirty; it is drawn once when the page shows
/// (the visibility message) and, while shown, in <see cref="Step"/>. <b>A worse penalty opens the page:</b> when the vitae multiplier falls while the page is hidden, retail sends the panel-visibility notice for
/// page 15 (<c>CM_UI::SendNotice_SetPanelVisibility</c>), so the page comes up after a death; a milder or equal one only updates what the page will show.</para>
/// </remarks>
public sealed class GameVitaePage : IGamePanel, IDisposable
{
    /// <summary>The panel id of the vitae page.</summary>
    public const uint Page = 15;

    /// <summary>The class id of <c>gmVitaeUI</c>.</summary>
    public const uint ClassId = 0x10000020;

    /// <summary>The page's text box.</summary>
    public const uint TextId = 0x100001C3;

    /// <summary>The string table enum of the <c>ID_Vitae_*</c> strings.</summary>
    public const uint StringTableEnum = 0x10000001;

    private static readonly uint PercentageVariable = StringHash.Tokenize("PERCENTAGE");
    private static readonly uint ExperienceVariable = StringHash.Tokenize("EXPERIENCE");

    private readonly UiLoginContext _context;
    private readonly GamePanelHost _host;
    private readonly UiTextElement? _text;
    private readonly UiStringVariables _variables = new();
    private readonly IDisposable _enchantments;
    private readonly IDisposable _qualities;
    private float _vitae = 1f;
    private bool _shown;
    private bool _dirty = true;

    /// <summary>The controller of the vitae page of <paramref name="host"/>.</summary>
    public GameVitaePage(UiLoginContext context, GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        _context = context;
        _host = host;
        _text = host.ElementFor(Page)?.GetChildRecursive(TextId) as UiTextElement;
        if (_text is null)
        {
            context.Manager.Services.Log.Error(UiElementManager.Category, "gmVitaeUI: the vitae page lacks its text box 0x100001C3; it shows its art only (TODO(m3-vitae-page-missing))");
        }

        // A page built after the login description has heard no event yet: it starts from the vitae the qualities hold (fix B's live-shaped check found it saying "no vitae" with one).
        _vitae = EffectRules.VitaeValue(context.Session.State.Self.Qualities);
        _enchantments = context.Session.Events.Subscribe<EnchantmentChanged>(OnEnchantmentChanged);
        _qualities = context.Session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
    }

    /// <inheritdoc/>
    public uint PanelId => Page;

    /// <summary>The page's text now (empty until the page showed with a known description).</summary>
    public string Report => _text?.Text ?? string.Empty;

    /// <summary>How many times the text was written.</summary>
    public int Updates { get; private set; }

    /// <summary><c>--video-hide-floaties</c> (video capture only, off by default): a worse vitae does not open the page by itself (the toolbar still opens it). Set once at start-up by the main scene.</summary>
    public static bool HideForVideo { get; set; }

    /// <summary>The vitae multiplier the page last read (1 for none).</summary>
    public float Vitae => _vitae;

    /// <inheritdoc/>
    public void OnShown()
    {
        _shown = true;
        Refresh(); // gmVitaeUI::ListenToElementMessage: a visibility message with a shown flag runs Update
    }

    /// <inheritdoc/>
    public void OnHidden() => _shown = false;

    /// <inheritdoc/>
    public void Step(double now)
    {
        if (_dirty && _shown)
        {
            Refresh();
        }
    }

    /// <summary>Draws the page now if it shows (the visibility message does; <see cref="Step"/> does for a change while shown).</summary>
    public void Refresh()
    {
        var q = _context.Session.State.Self.Qualities;
        if (!_shown || _text is null || !q.IsKnown)
        {
            return;
        }

        _dirty = false;
        Update(q);
    }

    private void OnEnchantmentChanged(in EnchantmentChanged change)
    {
        if (!change.VitaeChanged)
        {
            return;
        }

        // RecvNotice_VitaeChanged (0x004A7AA0): a shown page, or a vitae that is not worse, takes the value and updates; a worse one while hidden takes it and opens the page.
        var value = EffectRules.VitaeValue(_context.Session.State.Self.Qualities);
        if (_shown || value >= _vitae)
        {
            _vitae = value;
            _dirty = true;
        }
        else
        {
            _vitae = value;
            _dirty = true;
            if (!HideForVideo)
            {
                _host.SetPanelVisibility(Page, true);
            }
        }
    }

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change == QualityChange.Description)
        {
            _vitae = EffectRules.VitaeValue(_context.Session.State.Self.Qualities); // RecvNotice_PlayerDescReceived
            _dirty = true;
        }
        else if (change.Change == QualityChange.Int && change.Key == EffectRules.VitaeCpPoolProperty)
        {
            _dirty = true; // OnQualityChanged: the pool property the page registered for
        }
    }

    /// <summary><c>gmVitaeUI::Update</c> (0x004A7780).</summary>
    private void Update(PlayerQualities q)
    {
        Updates++;
        var percentage = EffectRules.VitaePercentage(_vitae);
        if (percentage > 0)
        {
            var owed = EffectRules.VitaeExperienceOwed(q, _vitae);
            _text!.SetText(Render("ID_Vitae_Text_Vitae", PercentageVariable, percentage, $"You have suffered a {percentage}% vitae penalty."));
            _text.AppendText(Render("ID_Vitae_Text_Skills", PercentageVariable, percentage, $"Your skills and attributes are reduced by {percentage}%."));
            _text.AppendText(Render("ID_Vitae_Text_Experience", ExperienceVariable, owed, $"You must earn {owed} more experience to clear it."));
        }
        else
        {
            _text!.SetText(Render("ID_Vitae_Text_Full", 0, 0, "You have no vitae penalty."));
        }
    }

    private string Render(string token, uint variable, long value, string fallback)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings is not null && strings.TryReference(StringTableEnum, token, out var reference))
        {
            if (variable != 0)
            {
                if (strings.Language is { } language)
                {
                    _variables.SetInteger(variable, value, language);
                }
                else
                {
                    _variables.SetText(variable, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            if (strings.TryGetString(reference, _variables, out var text))
            {
                return text;
            }
        }

        _context.Manager.LogOnce("vitae:" + token, LogSeverity.Warning, $"gmVitaeUI: the string {token} does not render in the string tables; it shows a placeholder (TODO(m3-vitae-strings))");
        return fallback;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _enchantments.Dispose();
        _qualities.Dispose();
    }
}

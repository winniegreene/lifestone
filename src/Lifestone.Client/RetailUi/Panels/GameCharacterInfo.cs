using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Motion;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmCharacterInfoUI::Update 0x004BA790, ::ListenToElementMessage 0x004BAA00,
// ::UpdatePlayerBirthAgeDeaths 0x004B8CB0, ::UpdateEnduranceInfo 0x004B8EB0, ::UpdateInnateAttributeInfo 0x004B87E0, ::UpdateFakeSkills 0x004B8930,
// ::UpdateAugmentations 0x004B9000, ::UpdateLoad 0x004B8A20.
/// <summary>
/// The character information panel (<c>gmCharacterInfoUI</c>, page 3 of the panel host, 0x10000183; docs/retail-ui.md section 21.8). Retail's page is a read-only text report in the text element 0x1000011D,
/// built by <c>Update</c> (11.6096 0x004BB420) from the player's description each time the page shows or its qualities change: <c>UpdatePlayerBirthAgeDeaths</c> (0x004B98D0: birth date, time played, deaths),
/// <c>UpdateEnduranceInfo</c> (0x004B9B40: the two words from Strength and Endurance), <c>UpdateInnateAttributeInfo</c> (0x004B9400: the six starting attributes), <c>UpdateFakeSkills</c> (0x004B9550: chess
/// rank, fishing skill), <c>UpdateAugmentations</c> (0x004B9C90) and <c>UpdateLoad</c> (0x004B9640), a blank line between them. The strings are <c>ID_CharacterInfo_*</c> of table 0x10000001. It is
/// not a list of skills and vitals: those are the Skills page and the vitals window.
/// </summary>
/// <remarks>
/// A line whose quality the description lacks is left out, as retail's <c>InqInt</c> calls leave it; nothing is invented. Not built: <c>UpdateAugmentations</c> and the carrying-augmentation line of <c>UpdateLoad</c> (a new character has none; the
/// <c>TODO(g6-character-info-augmentations)</c> line is logged once), the played-time format (<c>TimeUtils::QueryDuration</c> is a table-driven format Lifestone has not read: a labelled compact form,
/// <c>TODO(g6-duration-format)</c>) and the variable names of the dat strings that did not render (each logs once).
/// </remarks>
public sealed class GameCharacterInfo : IDisposable
{
    /// <summary>The panel id of the page.</summary>
    public const int PanelId = 3;

    /// <summary>The page's report text (0x1000011D).</summary>
    public const uint TextId = 0x1000011D;

    /// <summary>The string table enum (0x10000001) of the <c>ID_CharacterInfo_*</c> strings.</summary>
    public const uint TableEnum = 0x10000001;

    private readonly UiLoginContext _context;
    private readonly GamePanelHost _host;
    private readonly UiTextElement? _text;
    private readonly IDisposable _subscription;
    private bool _augmentationsTodoLogged;

    /// <summary>The page in <paramref name="host"/>; registers itself as the handler of page 3.</summary>
    public GameCharacterInfo(UiLoginContext context, GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        _context = context;
        _host = host;
        _text = host.ElementFor(PanelId)?.GetChildRecursive(TextId) as UiTextElement;
        host.SetPageHandler((uint)PanelId, Open);
        _subscription = context.Session.Events.Subscribe<Lifestone.Client.Session.Contracts.QualitiesChanged>(OnQualitiesChanged);
    }

    /// <summary>How many times the report was built.</summary>
    public int Updates { get; private set; }

    /// <summary>The report's text now (empty until the page showed with a known description).</summary>
    public string Report => _text?.Text ?? string.Empty;

    /// <summary>Shows the page (the handler the host calls) and builds the report (<c>ListenToElementMessage</c> on a visibility message, 11.6096 0x004BB5C0 family).</summary>
    public void Open()
    {
        _host.SetPanelVisibility((uint)PanelId, true);
        Update();
    }

    private void OnQualitiesChanged(in Lifestone.Client.Session.Contracts.QualitiesChanged change)
    {
        if (_host.CurrentPanel == PanelId)
        {
            Update(); // OnQualityChanged / RecvNotice_PlayerDescReceived rebuild the report
        }
    }

    /// <summary><c>gmCharacterInfoUI::Update</c>: replaces the report.</summary>
    public void Update()
    {
        var q = _context.Session.State.Self.Qualities;
        if (_text is null || !q.IsKnown)
        {
            return;
        }

        Updates++;
        _text.SetText(string.Empty);
        BirthAgeDeaths(q);
        Blank();
        Endurance(q);
        Blank();
        Innates(q);
        Blank();
        FakeSkills(q);
        Blank();
        if (!_augmentationsTodoLogged)
        {
            _augmentationsTodoLogged = true;
            _context.Manager.Services.Log.Warn("retailui", "TODO(g6-character-info-augmentations): gmCharacterInfoUI::UpdateAugmentations (11.6096 0x004B9C90, masteries, luminance and augmentation lines) is not built; the report omits them");
        }

        Blank();
        Load(q);
    }

    private void Blank() => _text!.AppendText("\n");

    private void Append(string token, UiStringVariables? variables = null)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings is null || !strings.TryReference(TableEnum, token, out var reference) || !strings.TryGetString(reference, variables, out var text))
        {
            _context.Manager.LogOnce($"charinfo:{token}", LogSeverity.Warning, $"gmCharacterInfoUI: the string {token} does not render in the string tables; its line is left out (TODO(g6-character-info-strings))");
            return;
        }

        _text!.AppendText(text);
    }

    private UiStringVariables Integers(params (string Name, long Value)[] values)
    {
        var variables = new UiStringVariables();
        var language = _context.Manager.Services.Strings?.Language;
        foreach (var (name, value) in values)
        {
            var id = StringHash.Tokenize(name);
            if (language is not null)
            {
                variables.SetInteger(id, value, language);
            }
            else
            {
                variables.SetText(id, value.ToString(CultureInfo.InvariantCulture));
            }
        }

        return variables;
    }

    private void BirthAgeDeaths(Lifestone.Client.Session.Contracts.PlayerQualities q)
    {
        if (q.TryGetInt(0x62, out var birth)) // CreationTimestamp
        {
            // C's %c in the "C" locale (MSVC): month/day/year hours:minutes:seconds, in local time.
            var local = DateTimeOffset.FromUnixTimeSeconds((uint)birth).LocalDateTime;
            Append("ID_CharacterInfo_Birth", new UiStringVariables().SetText("DATE", local.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        if (q.TryGetInt(0x7D, out var age)) // Age (seconds played)
        {
            Append("ID_CharacterInfo_Played", new UiStringVariables().SetText("DURATION", Duration(age)));
        }

        q.TryGetInt(0x2B, out var deaths); // DeathCount
        switch (deaths)
        {
            case 0:
                Append("ID_CharacterInfo_Deaths_None");
                break;
            case 1:
                Append("ID_CharacterInfo_Deaths_One");
                break;
            case 2:
                Append("ID_CharacterInfo_Deaths_Two");
                break;
            default:
                Append("ID_CharacterInfo_Deaths_Many", Integers(("DEATHS", deaths)));
                break;
        }
    }

    /// <summary>TODO(g6-duration-format): <c>TimeUtils::QueryDuration</c> builds a string-table text; this compact form is a labelled placeholder.</summary>
    internal static string Duration(int seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m {span.Seconds}s" : $"{span.Hours}h {span.Minutes}m {span.Seconds}s";
    }

    private static uint Raw(Lifestone.Client.Session.Contracts.PlayerQualities q, uint id) => q.TryGetAttributeLevels(id, out _, out var raw) ? raw : 0;

    /// <summary>The word <c>UpdateEnduranceInfo</c> (11.6096 0x004B9B40) picks for a sum of attributes against its five thresholds.</summary>
    internal static string Word(uint sum, uint poor, uint mediocre, uint hardy, uint resilient, uint indomitable) =>
        sum > indomitable ? "Indomitable" : sum > resilient ? "Resilient" : sum > hardy ? "Hardy" : sum > mediocre ? "Mediocre" : sum > poor ? "Poor" : "None";

    private void Endurance(Lifestone.Client.Session.Contracts.PlayerQualities q)
    {
        var strength = Raw(q, 1);
        var endurance = Raw(q, 2);
        var resists = Word(strength + endurance, 200, 260, 320, 380, 440); // 0xC8, 0x104, 0x140, 0x17C, 0x1B8
        var regeneration = Word(strength + (2 * endurance), 200, 346, 470, 580, 690); // 0xC8, 0x15A, 0x1D6, 0x244, 690
        Append("ID_CharacterInfo_Resists", new UiStringVariables().SetText("RESIST", resists).SetText("REGEN", regeneration));
    }

    private void Innates(Lifestone.Client.Session.Contracts.PlayerQualities q)
    {
        uint Start(uint id) => q.TryGetAttributeLevels(id, out var start, out _) ? start : 0;
        Append("ID_CharacterInfo_Innates", Integers(("STRENGTH", Start(1)), ("ENDURANCE", Start(2)), ("COORDINATION", Start(4)), ("QUICKNESS", Start(3)), ("FOCUS", Start(5)), ("SELF", Start(6))));
    }

    private void FakeSkills(Lifestone.Client.Session.Contracts.PlayerQualities q)
    {
        var chess = q.TryGetInt(0xB5, out var rank) ? rank : 1400; // retail's default rank
        Append("ID_CharacterInfo_Chess", Integers(("CHESS", chess)));
        q.TryGetInt(0xC0, out var fishing);
        Append("ID_CharacterInfo_Fishing", Integers(("FISHING", fishing)));
    }

    /// <summary>
    /// <c>CACQualities::InqLoad</c> (11.6096 0x0058FF60): the burden (integer property 5) over the capacity, which is the Strength with the registry applied (10 when unknown; <c>InqAttribute</c> with <c>raw</c> false)
    /// and the carrying augmentations (property 0xE6). The burden indicator reads it too (<c>gmUIElement_BurdenIndicator::Update</c>).
    /// </summary>
    public static float LoadOf(Lifestone.Client.Session.Contracts.PlayerQualities q, out int capacity, out int burden)
    {
        q.TryGetInt(5, out burden); // EncumbranceVal
        q.TryGetInt(0xE6, out var augmentations); // AugmentationIncreasedCarrying
        var strength = new Lifestone.Client.Session.Contracts.StatValues(q).TryAttribute(1, out _, out var current) ? current : 10u;
        capacity = EncumbranceSystem.EncumbranceCapacity((int)strength, augmentations);
        return (float)EncumbranceSystem.Load(capacity, burden);
    }

    private void Load(Lifestone.Client.Session.Contracts.PlayerQualities q)
    {
        var load = LoadOf(q, out var capacity, out var burden);
        if (load >= 1f)
        {
            var penalty = 10 - (long)(EncumbranceSystem.LoadMod(load) * 10.0);
            Append("ID_CharacterInfo_Load_Burdened", Integers(("BURDEN", burden - capacity), ("PENALTY", 10 * penalty)));
        }
        else
        {
            Append("ID_CharacterInfo_Load_None");
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _subscription.Dispose();
}

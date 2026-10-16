using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The header the attributes tab and the skills tab share (<c>gmStatManagementUI</c>, which both pages derive from): the name, the level, the player-kill status, the total experience and the experience to the next level
/// with its meter. <c>gmStatManagementUI::UpdateCharacterInfo</c> (11.6096 0x004F1400) writes the first three and <c>UpdateExperience</c> (0x004F1700) the rest. Each tab owns its copy of the header elements (the same ids
/// under its page element), so each tab makes one <see cref="StatHeader"/> over its own element and calls <see cref="Update"/> when it redraws (review 2.2-R3).
/// </summary>
/// <remarks>
/// Placeholders, logged once each: the gender and heritage line is empty (<c>TODO(m3-attribute-heritage)</c>), the kill status always reads non-player killer (<c>TODO(m3-pk-status)</c>), a character below level 200
/// shows no luminance line. The writes happen only when a tab redraws, never on an idle frame.
/// </remarks>
internal sealed class StatHeader
{
    /// <summary>The string table enum (0x10000001) of the <c>ID_StatManagement_*</c> strings.</summary>
    internal const uint StringTableEnum = 0x10000001;

    private const uint NameId = 0x10000231;
    private const uint HeritageId = 0x10000232;
    private const uint PkStatusId = 0x10000233;
    private const uint LevelId = 0x1000023B;
    private const uint TotalXpId = 0x10000235;
    private const uint XpToLevelId = 0x10000238;
    private const uint XpMeterId = 0x10000236;
    private const uint LuminanceLabelId = 0x100005C5;
    private const uint LuminanceId = 0x100005C6;

    private static readonly uint TotalExperienceVariable = StringHash.Tokenize("TOTAL_EXPERIENCE");
    private static readonly uint ExperienceToLevelVariable = StringHash.Tokenize("EXPERIENCE_TO_LEVEL");

    private readonly UiLoginContext _context;
    private readonly UiTextElement? _name;
    private readonly UiTextElement? _heritage;
    private readonly UiTextElement? _pk;
    private readonly UiTextElement? _level;
    private readonly UiTextElement? _totalXp;
    private readonly UiTextElement? _xpToLevel;
    private readonly UiMeter? _meter;
    private readonly UiTextElement? _luminanceLabel;
    private readonly UiTextElement? _luminance;
    private readonly UiStringVariables _variables = new();

    /// <summary>The header under <paramref name="tab"/> (the tab's page element); an element the layout lacks is skipped.</summary>
    internal StatHeader(UiLoginContext context, UiElement tab)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tab);
        _context = context;
        _name = tab.GetChildRecursive(NameId) as UiTextElement;
        _heritage = tab.GetChildRecursive(HeritageId) as UiTextElement;
        _pk = tab.GetChildRecursive(PkStatusId) as UiTextElement;
        _level = tab.GetChildRecursive(LevelId) as UiTextElement;
        _totalXp = tab.GetChildRecursive(TotalXpId) as UiTextElement;
        _xpToLevel = tab.GetChildRecursive(XpToLevelId) as UiTextElement;
        _meter = tab.GetChildRecursive(XpMeterId) as UiMeter;
        _luminanceLabel = tab.GetChildRecursive(LuminanceLabelId) as UiTextElement;
        _luminance = tab.GetChildRecursive(LuminanceId) as UiTextElement;
    }

    internal string NameText => _name?.Text ?? string.Empty;

    internal string LevelText => _level?.Text ?? string.Empty;

    internal string TotalExperienceText => _totalXp?.Text ?? string.Empty;

    internal string ExperienceToLevelText => _xpToLevel?.Text ?? string.Empty;

    internal double MeterPosition => _meter?.Position ?? 0;

    /// <summary>Writes the header from <paramref name="q"/>.</summary>
    internal void Update(PlayerQualities q)
    {
        // gmStatManagementUI::UpdateCharacterInfo (0x004F1400): the name, the level; the heritage line and the kill status are placeholders.
        _name?.SetText(_context.Session.State.Self.Name ?? string.Empty);
        _level?.SetText(q.TryGetInt(PlayerQualities.LevelProperty, out var level) ? level.ToString(CultureInfo.InvariantCulture) : "???");
        _heritage?.ClearAllText();
        var strings = _context.Manager.Services.Strings;
        if (_pk is not null)
        {
            _pk.SetText(strings is not null && strings.TryReference(StringTableEnum, "ID_StatManagement_Header_PKStatus_NPK", out var npk) && strings.TryGetString(npk, null, out var text) ? text : "Non-Player Killer");
        }

        _context.Manager.LogOnce("attributes:heritage", LogSeverity.Warning, "gmStatManagementUI::UpdateCharacterInfo: the gender and heritage line is not read (AppraisalSystem::InqGenderHeritageDisplay); it shows empty (TODO(m3-attribute-heritage))");
        _context.Manager.LogOnce("attributes:pk", LogSeverity.Warning, "gmStatManagementUI::UpdatePKStatus: the kill status always reads non-player killer (TODO(m3-pk-status))");

        // UpdateExperience (0x004F1700).
        var total = q.TotalExperience;
        var throne = _context.CharGen?.HasThroneOfDestiny ?? true;
        if (!throne && total > uint.MaxValue)
        {
            total = uint.MaxValue;
        }

        var table = _context.Session.XpTable;
        ulong toLevel = 0;
        var levelNow = q.Level;
        double meter = 0;
        if (table is not null && levelNow >= 0 && levelNow + 1 < table.CharacterLevelXpList.Count)
        {
            var from = table.XpForLevel(levelNow);
            var next = table.XpForLevel(levelNow + 1);
            toLevel = next > (ulong)Math.Max(total, 0) ? next - (ulong)Math.Max(total, 0) : 0;
            meter = next > from ? Math.Clamp(((double)Math.Max(total, 0) - from) / (next - from), 0.0, 1.0) : 0;
            if (!throne && levelNow == 126)
            {
                toLevel = 0;
            }
        }
        else
        {
            _context.Manager.LogOnce("attributes:level-xp", LogSeverity.Warning, "gmStatManagementUI::UpdateExperience: the XpTable is not loaded or the level is unknown; the experience to the next level shows as infinite (TODO(m3-stat-tables))");
        }

        _totalXp?.SetText(Render("ID_StatManagement_Header_TotalXPValue", TotalExperienceVariable, total, $"{total}"));
        _xpToLevel?.SetText(toLevel != 0
            ? Render("ID_StatManagement_Header_XPToLevelMeterValue", ExperienceToLevelVariable, (long)toLevel, $"{toLevel}")
            : Render("ID_StatManagement_Header_XPToLevelMeterInfinity", 0, 0, "Infinity!"));
        _meter?.SetFloatProperty(UiPropertyIds.MeterPosition, (float)meter);
        _luminanceLabel?.ClearAllText(); // a character below level 200 shows no luminance line
        _luminance?.ClearAllText();
    }

    private string Render(string token, uint variable, long value, string fallback)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings is not null && strings.TryReference(StringTableEnum, token, out var reference))
        {
            if (strings.Language is { } language)
            {
                _variables.SetInteger(variable, value, language);
            }
            else
            {
                _variables.SetText(variable, value.ToString(CultureInfo.InvariantCulture));
            }

            if (strings.TryGetString(reference, _variables, out var text))
            {
                return text;
            }
        }

        _context.Manager.LogOnce("attributes:" + token, LogSeverity.Warning, $"gmStatManagementUI: the string {token} does not render in the string tables; it shows '{fallback}' (TODO(m3-attributes-strings))");
        return fallback;
    }
}

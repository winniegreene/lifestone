namespace Lifestone.Client.RetailUi.Options;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmCharacterSettingsUI::InitOptions 0x004A02F0; PlayerModule::SideBySideVitals 0x005D3070.
/// <summary>One character option the options panel lists (a retail <c>PlayerOption</c> toggle).</summary>
/// <param name="Name">Retail's name; its label is the string <c>ID_PlayerOption_&lt;Name&gt;</c> and its tooltip <c>..._Help</c> (table enum 0x10000003).</param>
/// <param name="Word">1 for the first option word of the PlayerDescription, 2 for the second.</param>
/// <param name="Mask">The option's bit in that word (the wire values, ACE's <c>CharacterOptions1</c> and <c>CharacterOptions2</c> enums).</param>
/// <param name="Honoured">
/// True when Lifestone builds a client-side effect for the option (the vitals window, the vivid indicator). Every option is settable and goes to the server on Apply
/// (M5 6 u1, D32); a false here only means the client does nothing more with it, and the panel logs <c>TODO(g6-option-effects)</c> once when the player changes it.
/// </param>
/// <param name="Effect">What the client-side effect is, or the named TODO when there is none.</param>
public sealed record PlayerOptionDef(string Name, int Word, uint Mask, bool Honoured, string Effect)
{
    /// <summary>The retail default (<c>PlayerModule::GetDefaultOptionValue</c>): the bit in the default words ACE and the PlayerDescription codec document (0x50C4A54A and 0x00948700).</summary>
    public bool Default => ((Word == 1 ? PlayerOptionTable.DefaultWord1 : PlayerOptionTable.DefaultWord2) & Mask) != 0;

    /// <summary>The label string token.</summary>
    public string LabelToken => "ID_PlayerOption_" + Name;

    /// <summary>The tooltip string token.</summary>
    public string HelpToken => LabelToken + "_Help";
}

/// <summary>A section of the character options page (<c>gmCharacterSettingsUI::InitOptions</c>, 11.6096 0x004A05B0): its header string and the options under it, in retail's order.</summary>
/// <param name="HeaderToken">The header string token (table enum 0x10000003).</param>
/// <param name="Options">The section's options.</param>
public sealed record PlayerOptionSection(string HeaderToken, IReadOnlyList<PlayerOptionDef> Options);

/// <summary>
/// The character options page's content (docs/retail-ui.md section 21.8): the sections and options in the order <c>gmCharacterSettingsUI::InitOptions</c> adds them (11.6096 0x004A05B0; the order and
/// the names are read from the decompile). Each option's word and bit are the wire values of the PlayerDescription's two option words (ACE's <c>CharacterOptions1</c> and <c>CharacterOptions2</c>,
/// which agree with retail: bit 21 of word 1, the side-by-side vitals, is what <c>PlayerModule::SideBySideVitals</c> tests, 11.6096 0x005D4070).
/// </summary>
public static class PlayerOptionTable
{
    /// <summary>The first default option word (retail's, as ACE gives a new character: 0x50C4A54A).</summary>
    public const uint DefaultWord1 = 0x50C4A54A;

    /// <summary>The second default option word (retail's default when the flags omit it: 0x948700).</summary>
    public const uint DefaultWord2 = 0x00948700;

    private const string NotM2 = "sent to the server (ACE keeps it and returns it with the next description; it acts on those it reads itself); no client-side effect in Lifestone yet (TODO(g6-option-effects))";

    private static PlayerOptionDef Off(string name, int word, uint mask) => new(name, word, mask, false, NotM2);

    /// <summary>Side by side vitals: swaps the vitals window.</summary>
    public static PlayerOptionDef SideBySideVitals { get; } = new("SideBySideVitals", 1, 0x200000, true, "chooses the side-by-side vitals window (0x100006D5) over the stacked one (0x100005FA) at once");

    /// <summary>The vivid targeting indicator: the bit the vivid target indicator reads from the session's option word.</summary>
    public static PlayerOptionDef VividTargetingIndicator { get; } = new("VividTargetingIndicator", 1, 0x8000, true, "flips the bit in the session's option word, which the vivid target indicator reads");

    /// <summary>The sections, in retail's order.</summary>
    public static IReadOnlyList<PlayerOptionSection> Sections { get; } =
    [
        new("ID_CharacterOption_UIBehavior_Section",
        [
            Off("ViewCombatTarget", 1, 0x80), Off("SalvageMultiple", 2, 0x80), Off("MainPackPreferred", 2, 0x4000),
        ]),
        new("ID_CharacterOption_UIDisplay_Section",
        [
            VividTargetingIndicator, Off("ShowTooltips", 1, 0x100), Off("CoordinatesOnRadar", 1, 0x400000), SideBySideVitals, Off("SpellDuration", 1, 0x800000),
            Off("DisableMostWeatherEffects", 1, 0x10000), Off("DisableDistanceFog", 2, 0x200000), Off("PersistentAtDay", 2, 1), Off("DisableHouseRestrictionEffects", 1, 0x2000000),
            Off("UseCraftSuccessDialog", 1, 0x80000000), Off("ConfirmVolatileRareUse", 2, 0x40000), Off("DisplayTimeStamps", 2, 0x40), Off("FilterLanguage", 2, 0x20000),
            Off("ShowHelm", 2, 0x100000), Off("ShowCloak", 2, 0x800000),
        ]),
        new("ID_CharacterOption_Grouping_Section",
        [
            Off("IgnoreAllegianceRequests", 1, 4), Off("IgnoreFellowshipRequests", 1, 8), Off("DisplayAllegianceLogonNotifications", 1, 0x8000000), Off("FellowshipShareXP", 1, 0x40000),
            Off("FellowshipShareLoot", 1, 0x100000), Off("FellowshipAutoAcceptRequests", 1, 0x20000000),
        ]),
        new("ID_CharacterOption_OtherPlayers_Section",
        [
            Off("AcceptLootPermits", 1, 0x80000), Off("UseDeception", 1, 0x200), Off("AllowGive", 1, 0x40), Off("IgnoreTradeRequests", 1, 0x20000), Off("DragItemOnPlayerOpensSecureTrade", 1, 0x4000000),
            Off("DisplayDateOfBirth", 2, 2), Off("DisplayAge", 2, 0x20), Off("DisplayChessRank", 2, 4), Off("DisplayFishingSkill", 2, 8), Off("DisplayNumberDeaths", 2, 0x10),
            Off("DisplayNumberCharacterTitles", 2, 0x2000),
        ]),
        new("ID_CharacterOption_CharacterBehavior_Section",
        [
            Off("ToggleRun", 1, 0x400), Off("AdvancedCombatUI", 1, 0x1000), Off("AutoTarget", 1, 0x2000), Off("AutoRepeatAttack", 1, 2), Off("UseChargeAttack", 1, 0x10000000),
            Off("LeadMissileTargets", 2, 0x8000), Off("UseFastMissiles", 2, 0x10000),
        ]),
        new("ID_CharacterOption_Chat_Section",
        [
            Off("StayInChatMode", 1, 0x800), Off("HearAllegianceChat", 1, 0x40000000), Off("HearGeneralChat", 2, 0x100), Off("HearTradeChat", 2, 0x200), Off("HearLFGChat", 2, 0x400),
            Off("HearRoleplayChat", 2, 0x800), Off("HearSocietyChat", 2, 0x80000),
        ]),
    ];

    /// <summary>Every option, in page order.</summary>
    public static IEnumerable<PlayerOptionDef> All => Sections.SelectMany(static s => s.Options);

    /// <summary>The options with a client-side effect in Lifestone (the others are still settable and sent).</summary>
    public static IEnumerable<PlayerOptionDef> Honoured => All.Where(static o => o.Honoured);
}

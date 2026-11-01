using System.Collections.Immutable;
using Lifestone.Net.Packets;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The game actions that cast a spell or change the combat mode (M3 1.2 s4a): each is F7B1 {sequence, type} then 32-bit
/// little-endian fields, nothing else. Lifestone's own, from the retail senders and ACE's handlers (ACE v1.78.4816,
/// <c>Source/ACE.Server/Network/GameAction/Actions/</c>).
/// <list type="bullet">
/// <item>0x0048 CastUntargetedSpell {u32 spell}: retail <c>CM_Magic::Event_CastUntargetedSpell</c> (11.4186 0x006A3150,
/// 11.6096 0x006A3F70); ACE <c>GameActionMagicCastUnTargetedSpell</c> reads one <c>ReadUInt32</c> (the spell id).</item>
/// <item>0x004A CastTargetedSpell {u32 target, u32 spell}: retail <c>CM_Magic::Event_CastTargetedSpell</c> (11.4186 0x006A3040,
/// 11.6096 0x006A3E60), prototype (target, spell id); ACE <c>GameActionMagicCastTargetedSpell</c> reads the target guid
/// <b>first</b>, then the spell id.</item>
/// <item>0x0053 ChangeCombatMode {u32 mode}: retail <c>CM_Combat::Event_ChangeCombatMode</c> (11.4186 0x006A9A70,
/// 11.6096 0x006AA9D0); ACE <c>GameActionChangeCombatMode</c> reads one <c>ReadUInt32</c> (1 non-combat, 2 melee, 4 missile, 8 magic).</item>
/// <item>0x0279 Suicide (no payload; M3 3.2 s0): retail <c>CM_Character::Event_Suicide</c> (11.4186 0x006A1B00, 11.6096 0x006A2920);
/// ACE <c>GameActionDie</c> (<c>GameActionType.Suicide</c>) reads nothing and calls <c>HandleActionDie</c>.</item>
/// <item>0x01E3 AddSpellFavorite {u32 spell, u32 index, u32 bar} (M3 3.2 s0): retail <c>CM_Character::Event_AddSpellFavorite</c> (11.4186 0x006A0F70,
/// 11.6096 0x006A1D90), prototype (spell, index, list); ACE <c>GameActionAddSpellFavorite</c> reads the spell, the position on the bar, then the bar.</item>
/// <item>0x01E4 RemoveSpellFavorite {u32 spell, u32 bar} (M3 4.2 p1): retail <c>CM_Character::Event_RemoveSpellFavorite</c> (11.4186 0x006A1890,
/// 11.6096 0x006A26B0); ACE <c>GameActionRemoveSpellFavorite</c> reads the spell, then the bar (no index).</item>
/// </list>
/// </summary>
internal static class MagicActionCodecs
{
    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0048, <paramref name="spellId"/>}.</summary>
    public static void WriteCastUntargetedSpell(PacketWriter writer, uint sequence, uint spellId)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.CastUntargetedSpell);
        writer.WriteUInt32(spellId);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x004A, <paramref name="target"/>, <paramref name="spellId"/>}: the target first, as ACE reads it.</summary>
    public static void WriteCastTargetedSpell(PacketWriter writer, uint sequence, uint target, uint spellId)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.CastTargetedSpell);
        writer.WriteUInt32(target);
        writer.WriteUInt32(spellId);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0053, <paramref name="mode"/>}.</summary>
    public static void WriteChangeCombatMode(PacketWriter writer, uint sequence, uint mode)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.ChangeCombatMode);
        writer.WriteUInt32(mode);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0279}: the suicide request carries no payload.</summary>
    public static void WriteSuicide(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.Suicide);

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x01E3, <paramref name="spellId"/>, <paramref name="index"/>, <paramref name="bar"/>}: the spell, its position on the bar, then the bar, as ACE reads them.</summary>
    public static void WriteAddSpellFavorite(PacketWriter writer, uint sequence, uint spellId, uint index, uint bar)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.AddSpellFavorite);
        writer.WriteUInt32(spellId);
        writer.WriteUInt32(index);
        writer.WriteUInt32(bar);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x01E4, <paramref name="spellId"/>, <paramref name="bar"/>}: the spell, then the bar, as ACE reads them.</summary>
    public static void WriteRemoveSpellFavorite(PacketWriter writer, uint sequence, uint spellId, uint bar)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.RemoveSpellFavorite);
        writer.WriteUInt32(spellId);
        writer.WriteUInt32(bar);
    }

    // ---- M5 6 w1: the character actions (the confirmation answer, the options and the two recalls) ----

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x0275, type, context, accepted}: ACE reads <c>ReadInt32</c> (the
    /// <c>ConfirmationType</c>), <c>ReadUInt32</c> (the context) and <c>Convert.ToBoolean(ReadInt32())</c>
    /// (<c>GameActionConfirmationResponse.cs:11-13</c>, @95ef426d); retail's sender is <c>CM_Character::Event_ConfirmationResponse</c>
    /// (11.6096 0x006A2030). The answer is 1 for yes, 0 for no.
    /// </summary>
    public static void WriteConfirmationResponse(PacketWriter writer, uint sequence, uint type, uint context, bool accepted)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.ConfirmationResponse);
        writer.WriteUInt32(type);
        writer.WriteUInt32(context);
        writer.WriteUInt32(accepted ? 1u : 0u);
    }

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x0005, option, value}: ACE reads <c>(CharacterOption)ReadUInt32</c> and
    /// <c>ReadUInt32() != 0</c> (<c>GameActionSetSingleCharacterOption.cs:11-12</c>, @95ef426d); retail's sender is
    /// <c>CM_Character::Event_PlayerOptionChangedEvent</c> (11.6096 0x006A2330). The option is ACE's <c>CharacterOption</c> value
    /// (only the ones that enum gives a number).
    /// </summary>
    public static void WriteSetSingleCharacterOption(PacketWriter writer, uint sequence, uint option, bool value)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.SetSingleCharacterOption);
        writer.WriteUInt32(option);
        writer.WriteUInt32(value ? 1u : 0u);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0063}: TeleToLifestone carries no payload (<c>GameActionTeleToLifestone.cs</c>; retail <c>CM_Character::Event_TeleToLifestone</c>, 11.6096 0x006A29B0).</summary>
    public static void WriteTeleToLifestone(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.TeleToLifestone);

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x028D}: TeleToMarketPlace carries no payload (<c>GameActionTeleToMarketPlace.cs</c>; retail <c>CM_Character::Event_TeleToMarketplace</c>, 11.6096 0x006A2A40).</summary>
    public static void WriteTeleToMarketPlace(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.TeleToMarketPlace);

    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x01A1, the player module block}, in the order ACE's <c>GameActionSetCharacterOptions</c>
    /// reads it (@95ef426d, <c>GameActionSetCharacterOptions.cs:45-190</c>; retail's sender is
    /// <c>CM_Character::Event_CharacterOptionsEvent</c>, 11.6096 0x006A1EE0): the flags word, the first options word, the shortcuts (flag
    /// 0x1: a count, then index, object and layered spell, 12 bytes each), the first favourite-spell bar (always: a count and the spells),
    /// four more bars (0x4), or six more (0x10), or seven more (0x400), the desired components (0x8: a u16 count and a u16 bucket count,
    /// 256 as ACE's writer uses from retail pcaps, then id and amount pairs), the spellbook filters (0x20), the second options word
    /// (0x40), the timestamp format (0x80, a String16L), and the gameplay options (0x200) as raw bytes to the end. A section is present
    /// when the block has its data; the filters and the second options word are always sent, as ACE's own description always sends them.
    /// </summary>
    /// <exception cref="ArgumentException">The block has a number of spell bars the flags cannot say (1, 5, 7 or 8), or a timestamp
    /// format that is not ASCII (ACE reads a client string by UTF-8 characters, and a field follows it); nothing is written.</exception>
    public static void WriteSetCharacterOptions(PacketWriter writer, uint sequence, CharacterOptionsBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        ImmutableArray<ImmutableArray<uint>> bars = block.SpellBars.IsDefaultOrEmpty ? [[]] : block.SpellBars;
        var flags = CharacterOptionsBlock.OptionCharacterOptions2 | CharacterOptionsBlock.OptionSpellbookFilters;
        switch (bars.Length)
        {
            case 1:
                break;
            case 5:
                flags |= CharacterOptionsBlock.OptionMultiSpellList;
                break;
            case 7:
                flags |= CharacterOptionsBlock.OptionExtendedMultiSpellLists;
                break;
            case 8:
                flags |= CharacterOptionsBlock.OptionSpellLists8;
                break;
            default:
                throw new ArgumentException($"the options block has {bars.Length} spell bars; the flags can say 1, 5, 7 or 8.", nameof(block));
        }

        if (block.TimestampFormat is { } timestamp)
        {
            AsciiGuard.Require(timestamp, "options timestamp format");
            _ = PacketWriter.String16LSize(timestamp, "options timestamp format");
            flags |= CharacterOptionsBlock.OptionTimestampFormat;
        }

        ImmutableArray<ShortcutDesc> shortcuts = block.Shortcuts.IsDefaultOrEmpty ? [] : block.Shortcuts;
        ImmutableArray<DesiredComponentDesc> components = block.DesiredComponents.IsDefaultOrEmpty ? [] : block.DesiredComponents;
        ImmutableArray<byte> gameplay = block.GameplayOptions.IsDefaultOrEmpty ? [] : block.GameplayOptions;
        flags |= shortcuts.Length > 0 ? CharacterOptionsBlock.OptionShortcut : 0;
        flags |= components.Length > 0 ? CharacterOptionsBlock.OptionDesiredComps : 0;
        flags |= gameplay.Length > 0 ? CharacterOptionsBlock.OptionGameplayOptions : 0;

        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.SetCharacterOptions);
        writer.WriteUInt32(flags);
        writer.WriteUInt32(block.Options1);
        if (shortcuts.Length > 0)
        {
            writer.WriteUInt32((uint)shortcuts.Length);
            foreach (var shortcut in shortcuts)
            {
                writer.WriteUInt32(shortcut.Index);
                writer.WriteUInt32(shortcut.ObjectId);
                writer.WriteUInt32(shortcut.SpellId);
            }
        }

        foreach (var bar in bars)
        {
            ImmutableArray<uint> spells = bar.IsDefaultOrEmpty ? [] : bar;
            writer.WriteUInt32((uint)spells.Length);
            foreach (var spell in spells)
            {
                writer.WriteUInt32(spell);
            }
        }

        if (components.Length > 0)
        {
            const int buckets = 256;
            writer.WriteUInt16((ushort)components.Length);
            writer.WriteUInt16(buckets);
            foreach (var component in components.OrderBy(c => c.ComponentId % buckets))
            {
                writer.WriteUInt32(component.ComponentId);
                writer.WriteInt32(component.Amount);
            }
        }

        writer.WriteUInt32(block.SpellbookFilters);
        writer.WriteUInt32(block.Options2);
        if (block.TimestampFormat is { } format)
        {
            writer.WriteAsciiString16L(format, "options timestamp format");
        }

        if (gameplay.Length > 0)
        {
            writer.WriteBytes([.. gameplay]);
        }
    }
}

/// <summary>
/// What a SetCharacterOptions (GA 0x01A1) carries: the player module's options, as the read model keeps them from the
/// PlayerDescription (<see cref="PlayerDescriptionMsg"/>: <c>Options1</c>, <c>Options2</c>, <c>SpellbookFilters</c>, <c>Shortcuts</c>,
/// <c>SpellBars</c>, <c>DesiredComponents</c>, <c>TimestampFormat</c>) plus the gameplay options, which a caller supplies (M5 6 w1).
/// </summary>
/// <param name="Options1">The first character options word (retail <c>PlayerModule.options_</c>).</param>
/// <param name="Options2">The second character options word (the default 0x948700 when the description omitted it).</param>
/// <param name="SpellbookFilters">The spellbook filters (the default 0x3FFF when the description omitted them).</param>
/// <param name="Shortcuts">The shortcut bar entries.</param>
/// <param name="SpellBars">The favourite-spell bars: the first, then none, four, six or seven more (1, 5, 7 or 8 bars).</param>
/// <param name="DesiredComponents">The desired components to rebuy.</param>
/// <param name="TimestampFormat">The timestamp format, or null for none (ACE never sends one).</param>
/// <param name="GameplayOptions">
/// The gameplay options archive (flag 0x200: the window layout, opacity and so on), raw, to the end of the message; empty for none, and
/// then ACE keeps the bytes it stored. TODO(m5-options-block-gameplayoptions): the description's own <c>GameplayOptions</c> cannot be
/// echoed (when present it also holds the inventory tail that follows, <see cref="PlayerDescriptionMsg.InventoryUndecoded"/>), so
/// <see cref="FromDescription"/> leaves this empty and a caller that edits the archive supplies it.
/// </param>
/// <remarks>
/// TODO(m5-options-block-genericqualities): the generic qualities section (flag 0x100) is never written: ACE never sends one and ACE's
/// reader reads its float table as strings (<c>GameActionSetCharacterOptions.cs:163-172</c>, a source bug), so a correct block would be
/// misread. TODO(m5-options-block-flags): which sections retail's own client sends is not captured; the live step records a retail
/// client's block and this writer's flags are then made to match.
/// </remarks>
public sealed record CharacterOptionsBlock(
    uint Options1,
    uint Options2,
    uint SpellbookFilters,
    ImmutableArray<ShortcutDesc> Shortcuts,
    ImmutableArray<ImmutableArray<uint>> SpellBars,
    ImmutableArray<DesiredComponentDesc> DesiredComponents,
    string? TimestampFormat = null,
    ImmutableArray<byte> GameplayOptions = default)
{
    /// <summary>ACE's <c>CharacterOptionDataFlag.Shortcut</c>.</summary>
    internal const uint OptionShortcut = 0x1;

    /// <summary>ACE's <c>CharacterOptionDataFlag.MultiSpellList</c>: four more bars.</summary>
    internal const uint OptionMultiSpellList = 0x4;

    /// <summary>ACE's <c>CharacterOptionDataFlag.DesiredComps</c>.</summary>
    internal const uint OptionDesiredComps = 0x8;

    /// <summary>ACE's <c>CharacterOptionDataFlag.ExtendedMultiSpellLists</c>: six more bars.</summary>
    internal const uint OptionExtendedMultiSpellLists = 0x10;

    /// <summary>ACE's <c>CharacterOptionDataFlag.SpellbookFilters</c>.</summary>
    internal const uint OptionSpellbookFilters = 0x20;

    /// <summary>ACE's <c>CharacterOptionDataFlag.CharacterOptions2</c>.</summary>
    internal const uint OptionCharacterOptions2 = 0x40;

    /// <summary>ACE's <c>CharacterOptionDataFlag.TimestampFormat</c>.</summary>
    internal const uint OptionTimestampFormat = 0x80;

    /// <summary>ACE's <c>CharacterOptionDataFlag.GameplayOptions</c>.</summary>
    internal const uint OptionGameplayOptions = 0x200;

    /// <summary>ACE's <c>CharacterOptionDataFlag.SpellLists8</c>: seven more bars.</summary>
    internal const uint OptionSpellLists8 = 0x400;

    /// <summary>The block a PlayerDescription already describes, with the options words changed to the given ones.</summary>
    public static CharacterOptionsBlock FromDescription(PlayerDescriptionMsg description, uint options1, uint options2)
    {
        ArgumentNullException.ThrowIfNull(description);
        return new CharacterOptionsBlock(
            options1,
            options2,
            description.SpellbookFilters,
            description.Shortcuts,
            description.SpellBars,
            description.DesiredComponents,
            description.TimestampFormat);
    }
}

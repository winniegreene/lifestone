using Lifestone.Net.Packets;

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
}

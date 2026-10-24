namespace Lifestone.Tests.Shared.Net;

/// <summary>
/// The M3 server-to-client messages of item 1.2 (inventory, wield, public updates, combat notifications, death, magic and
/// admin) as the bundled ACE writes them, for tests in every project (M3 1.2 s0). Each returns the whole message, opcode
/// first, built with a plain <see cref="BinaryWriter"/> from the cited ACE writer (ACEmulator/ACE, read, not copied),
/// independently of Lifestone.Net's codecs. Strings follow ACE's <c>WriteString16L</c> (<see cref="AceWireWriters.WriteString16L"/>).
/// </summary>
public static class AceM3Writers
{
    // GameMessages (ACE.Server/Network/GameMessages/Messages/).

    /// <summary>GM 0x0024 <c>GameMessageInventoryRemoveObject</c>: the GUID.</summary>
    public static byte[] InventoryRemoveObject(uint guid) => AceWireWriters.Message(0x0024, w => w.Write(guid));

    /// <summary>GM 0x0197 <c>GameMessageSetStackSize</c>: u8 sequence, GUID, stack size, value.</summary>
    public static byte[] SetStackSize(byte sequence, uint guid, uint stackSize, uint value) =>
        AceWireWriters.Message(0x0197, w =>
        {
            w.Write(sequence);
            w.Write(guid);
            w.Write(stackSize);
            w.Write(value);
        });

    /// <summary>GM 0x019E <c>GameMessagePlayerKilled</c>: String16L, victim, killer.</summary>
    public static byte[] PlayerKilled(string text, uint victim, uint killer) =>
        AceWireWriters.Message(0x019E, w =>
        {
            AceWireWriters.WriteString16L(w, text);
            w.Write(victim);
            w.Write(killer);
        });

    /// <summary>GM 0x02CE <c>GameMessagePublicUpdatePropertyInt</c>: u8 sequence, GUID, property, i32 value.</summary>
    public static byte[] PublicUpdatePropertyInt(byte sequence, uint guid, uint property, int value) =>
        AceWireWriters.Message(0x02CE, w =>
        {
            w.Write(sequence);
            w.Write(guid);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>GM 0x02DA <c>GameMessagePublicUpdateInstanceID</c>: u8 sequence, GUID, property, instance GUID.</summary>
    public static byte[] PublicUpdateInstanceId(byte sequence, uint guid, uint property, uint value) =>
        AceWireWriters.Message(0x02DA, w =>
        {
            w.Write(sequence);
            w.Write(guid);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>GM 0xEA60 <c>GameMessageAdminEnvirons</c>: the u32 environ id.</summary>
    public static byte[] AdminEnvirons(uint environ) => AceWireWriters.Message(0xEA60, w => w.Write(environ));

    // GameEvents (ACE.Server/Network/GameEvent/Events/), inside F7B0.

    /// <summary>GE 0x0022 <c>GameEventItemServerSaysContainId</c>: item, container, placement, container type.</summary>
    public static byte[] PutObjInContainer(uint player, uint sequence, uint item, uint container, uint placement, uint containerType) =>
        Event(player, sequence, 0x0022, w =>
        {
            w.Write(item);
            w.Write(container);
            w.Write(placement);
            w.Write(containerType);
        });

    /// <summary>GE 0x0023 <c>GameEventWieldItem</c>: item, equip mask.</summary>
    public static byte[] WieldObject(uint player, uint sequence, uint item, int equipMask) =>
        Event(player, sequence, 0x0023, w =>
        {
            w.Write(item);
            w.Write(equipMask);
        });

    /// <summary>GE 0x0196 <c>GameEventViewContents</c>: the container, then a count and (guid, ContainerType) per entry (M4 P5a fix r4: a side pack's contents at the login).</summary>
    public static byte[] ViewContents(uint player, uint sequence, uint container, IReadOnlyList<(uint Guid, uint ContainerType)> entries) =>
        Event(player, sequence, 0x0196, w =>
        {
            w.Write(container);
            w.Write(entries.Count);
            foreach (var (guid, type) in entries)
            {
                w.Write(guid);
                w.Write(type);
            }
        });

    /// <summary>GE 0x019A <c>GameEventItemServerSaysMoveItem</c>: the item.</summary>
    public static byte[] PutObjectIn3D(uint player, uint sequence, uint item) => Event(player, sequence, 0x019A, w => w.Write(item));

    /// <summary>GE 0x00A0 <c>GameEventInventoryServerSaveFailed</c>: item, WeenieError.</summary>
    public static byte[] InventoryServerSaysFailed(uint player, uint sequence, uint item, uint error) =>
        Event(player, sequence, 0x00A0, w =>
        {
            w.Write(item);
            w.Write(error);
        });

    /// <summary>GE 0x01AC <c>GameEventVictimNotification</c>: String16L.</summary>
    public static byte[] VictimNotification(uint player, uint sequence, string text) => Event(player, sequence, 0x01AC, w => AceWireWriters.WriteString16L(w, text));

    /// <summary>GE 0x01AD <c>GameEventKillerNotification</c>: String16L.</summary>
    public static byte[] KillerNotification(uint player, uint sequence, string text) => Event(player, sequence, 0x01AD, w => AceWireWriters.WriteString16L(w, text));

    /// <summary>GE 0x01B1 <c>GameEventAttackerNotification</c>: String16L, damage type, f64 percent, damage, critical (u32), attack conditions (u64).</summary>
    public static byte[] AttackerNotification(uint player, uint sequence, string defender, uint damageType, double percent, uint damage, bool critical, ulong conditions) =>
        Event(player, sequence, 0x01B1, w =>
        {
            AceWireWriters.WriteString16L(w, defender);
            w.Write(damageType);
            w.Write(percent);
            w.Write(damage);
            w.Write(critical ? 1u : 0u);
            w.Write(conditions);
        });

    /// <summary>GE 0x01B2 <c>GameEventDefenderNotification</c>: String16L, damage type, f64 percent, damage, location, critical, conditions, align.</summary>
    public static byte[] DefenderNotification(uint player, uint sequence, string attacker, uint damageType, double percent, uint damage, uint location, bool critical, ulong conditions) =>
        Event(player, sequence, 0x01B2, w =>
        {
            AceWireWriters.WriteString16L(w, attacker);
            w.Write(damageType);
            w.Write(percent);
            w.Write(damage);
            w.Write(location);
            w.Write(critical ? 1u : 0u);
            w.Write(conditions);
        });

    /// <summary>GE 0x01B3 <c>GameEventEvasionAttackerNotification</c>: String16L.</summary>
    public static byte[] EvasionAttackerNotification(uint player, uint sequence, string defender) =>
        Event(player, sequence, 0x01B3, w => AceWireWriters.WriteString16L(w, defender));

    /// <summary>GE 0x01B4 <c>GameEventEvasionDefenderNotification</c>: String16L.</summary>
    public static byte[] EvasionDefenderNotification(uint player, uint sequence, string attacker) =>
        Event(player, sequence, 0x01B4, w => AceWireWriters.WriteString16L(w, attacker));

    /// <summary>GE 0x02C1 <c>GameEventMagicUpdateSpell</c>: u16 spell, u16 layer.</summary>
    public static byte[] MagicUpdateSpell(uint player, uint sequence, ushort spell, ushort layer = 0) => Pair(player, sequence, 0x02C1, spell, layer);

    /// <summary>GE 0x02C2 <c>GameEventMagicUpdateEnchantment</c>: one enchantment (<c>EnchantmentExtensions.Write</c>).</summary>
    public static byte[] MagicUpdateEnchantment(uint player, uint sequence, AceEnchantment enchantment) =>
        Event(player, sequence, 0x02C2, w => WriteEnchantment(w, enchantment));

    /// <summary>GE 0x02C3 <c>GameEventMagicRemoveEnchantment</c>: u16 spell, u16 layer.</summary>
    public static byte[] MagicRemoveEnchantment(uint player, uint sequence, ushort spell, ushort layer) => Pair(player, sequence, 0x02C3, spell, layer);

    /// <summary>GE 0x02C4 <c>GameEventMagicUpdateMultipleEnchantments</c>: count, then the enchantments (<c>EnchantmentExtensions.Write(List)</c>).</summary>
    public static byte[] MagicUpdateMultipleEnchantments(uint player, uint sequence, IReadOnlyList<AceEnchantment> enchantments) =>
        Event(player, sequence, 0x02C4, w =>
        {
            w.Write(enchantments.Count);
            foreach (var enchantment in enchantments)
            {
                WriteEnchantment(w, enchantment);
            }
        });

    /// <summary>GE 0x02C5 <c>GameEventMagicRemoveMultipleEnchantments</c>: count, then u16 spell and u16 layer each.</summary>
    public static byte[] MagicRemoveMultipleEnchantments(uint player, uint sequence, IReadOnlyList<(ushort Spell, ushort Layer)> spells) =>
        SpellList(player, sequence, 0x02C5, spells);

    /// <summary>GE 0x02C6 <c>GameEventMagicPurgeEnchantments</c>: no payload.</summary>
    public static byte[] MagicPurgeEnchantments(uint player, uint sequence) => Event(player, sequence, 0x02C6, static _ => { });

    /// <summary>GE 0x02C7 <c>GameEventMagicDispelEnchantment</c>: u16 spell, u16 layer.</summary>
    public static byte[] MagicDispelEnchantment(uint player, uint sequence, ushort spell, ushort layer) => Pair(player, sequence, 0x02C7, spell, layer);

    /// <summary>GE 0x02C8 <c>GameEventMagicDispelMultipleEnchantments</c>: count, then u16 spell and u16 layer each.</summary>
    public static byte[] MagicDispelMultipleEnchantments(uint player, uint sequence, IReadOnlyList<(ushort Spell, ushort Layer)> spells) =>
        SpellList(player, sequence, 0x02C8, spells);

    /// <summary>GE 0x0312 <c>GameEventMagicPurgeBadEnchantments</c>: no payload.</summary>
    public static byte[] MagicPurgeBadEnchantments(uint player, uint sequence) => Event(player, sequence, 0x0312, static _ => { });

    private static byte[] Pair(uint player, uint sequence, uint eventType, ushort spell, ushort layer) =>
        Event(player, sequence, eventType, w =>
        {
            w.Write(spell);
            w.Write(layer);
        });

    private static byte[] SpellList(uint player, uint sequence, uint eventType, IReadOnlyList<(ushort Spell, ushort Layer)> spells) =>
        Event(player, sequence, eventType, w =>
        {
            w.Write(spells.Count);
            foreach (var (spell, layer) in spells)
            {
                w.Write(spell);
                w.Write(layer);
            }
        });

    private static byte[] Event(uint player, uint sequence, uint eventType, Action<BinaryWriter> payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            payload(writer);
        }

        return AceWireWriters.GameEvent(player, sequence, eventType, stream.ToArray());
    }

    /// <summary>ACE's <c>EnchantmentExtensions.Write</c>: 60 bytes, then the spell set id when HasSpellSetId is not 0.</summary>
    private static void WriteEnchantment(BinaryWriter w, AceEnchantment e)
    {
        w.Write(e.SpellId);
        w.Write(e.Layer);
        w.Write(e.SpellCategory);
        w.Write(e.HasSpellSetId);
        w.Write(e.PowerLevel);
        w.Write(e.StartTime);
        w.Write(e.Duration);
        w.Write(e.CasterGuid);
        w.Write(e.DegradeModifier);
        w.Write(e.DegradeLimit);
        w.Write(e.LastTimeDegraded);
        w.Write(e.StatModType);
        w.Write(e.StatModKey);
        w.Write(e.StatModValue);
        if (e.HasSpellSetId != 0)
        {
            w.Write(e.SpellSetId);
        }
    }
}

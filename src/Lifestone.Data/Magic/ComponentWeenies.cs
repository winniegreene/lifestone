using System.Collections.Frozen;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Magic;

/// <summary>One spell component and the weenie ACE gives it: the SpellComponentTable's id, the weenie class id, the name both share, and the weenie's maximum stack.</summary>
/// <param name="ComponentId">The id spell formulas use (the SpellComponentTable's key and the DualDidMapper's <c>ClientEnumToID</c> key).</param>
/// <param name="Wcid">The weenie class id of the item that stands for it (ACE's <c>Spell.GetComponentWCID</c>); the item a cast burns.</param>
/// <param name="Name">The component's name: the dat's SpellComponentTable name, equal to the weenie's name.</param>
/// <param name="MaxStack">The most one stack of the weenie holds (<c>PropertyInt.MaxStackSize</c>).</param>
/// <param name="Cdm">The component destruction modifier of the SpellComponentTable (<see cref="SpellComponentInfo.Cdm"/>): a cast burns a slot with the chance <c>ComponentLoss × Cdm × min(1, Power / skill)</c>.</param>
public readonly record struct ComponentWeenie(uint ComponentId, uint Wcid, string Name, int MaxStack, float Cdm);

/// <summary>
/// The component map (M3 1.4 s0): for every spell component the BSD mage's kit can need, the SpellComponentTable id and the world database's
/// weenie (name, class id, maximum stack), pinned by a read-only SELECT of ACE's world database (the weenies of WeenieType SpellComponent, 2026-10-16) and
/// checked against the portal dat by <c>ComponentWeenieTests</c>: the name equals the dat's SpellComponentTable name for the id, and the wcid equals the
/// DualDidMapper 0x27000002 (<see cref="DecodeClientEnumToId"/>) that ACE's <c>Spell.GetComponentWCID</c> reads. The kit's components are the scarabs, herbs, powders,
/// potions, talismans and the twelve coloured tapers (ids 63 to 74, <see cref="SpellFormulaRules.LowestTaperId"/>), with the Prismatic Taper (188) and the three
/// scarabs of the high spells.
/// </summary>
/// <remarks>
/// <para>Not in the map: the "Pea" weenies (8284-8357, 20963, 25729; the dat's components 113 to 191 except 187, 188 and 190), a separate family that no player formula uses, and the Dark Scarab (37117, maximum stack 1,
/// the void school's). An id outside the map is a loud failure of the kit (<see cref="TryGetByComponent"/> is false), never a guess.</para>
/// </remarks>
public static class ComponentWeenies
{
    /// <summary>Every pinned component, by component id.</summary>
    public static IReadOnlyList<ComponentWeenie> All { get; } =
    [
        new(1, 691, "Lead Scarab", 100, 0.1f),
        new(2, 689, "Iron Scarab", 100, 0.1f),
        new(3, 686, "Copper Scarab", 100, 0.1f),
        new(4, 688, "Silver Scarab", 100, 0.1f),
        new(5, 687, "Gold Scarab", 100, 0.1f),
        new(6, 690, "Pyreal Scarab", 100, 0.1f),
        new(7, 774, "Hyssop", 100, 1f),
        new(8, 775, "Mandrake", 100, 1f),
        new(9, 778, "Saffron", 100, 1f),
        new(10, 768, "Damiana", 100, 1f),
        new(11, 776, "Mugwort", 100, 1f),
        new(12, 766, "Bistort", 100, 1f),
        new(13, 780, "Wormwood", 100, 1f),
        new(14, 625, "Ginseng", 100, 1f),
        new(15, 772, "Hawthorn", 100, 1f),
        new(16, 765, "Amaranth", 100, 1f),
        new(17, 779, "Vervain", 100, 1f),
        new(18, 777, "Myrrh", 100, 1f),
        new(19, 770, "Eyebright", 100, 1f),
        new(20, 771, "Frankincense", 100, 1f),
        new(21, 769, "Dragonsblood", 100, 1f),
        new(22, 773, "Henbane", 100, 1f),
        new(23, 767, "Comfrey", 100, 1f),
        new(24, 781, "Yarrow", 100, 1f),
        new(25, 782, "Powdered Agate", 100, 1f),
        new(26, 783, "Powdered Amber", 100, 1f),
        new(27, 784, "Powdered Azurite", 100, 1f),
        new(28, 785, "Powdered Bloodstone", 100, 1f),
        new(29, 786, "Powdered Carnelian", 100, 1f),
        new(30, 626, "Powdered Hematite", 100, 1f),
        new(31, 787, "Powdered Lapis Lazuli", 100, 1f),
        new(32, 788, "Powdered Malachite", 100, 1f),
        new(33, 789, "Powdered Moonstone", 100, 1f),
        new(34, 790, "Powdered Onyx", 100, 1f),
        new(35, 791, "Powdered Quartz", 100, 1f),
        new(36, 792, "Powdered Turquoise", 100, 1f),
        new(37, 753, "Brimstone", 100, 1f),
        new(38, 754, "Cadmia", 100, 1f),
        new(39, 755, "Cinnabar", 100, 1f),
        new(40, 756, "Cobalt", 100, 1f),
        new(41, 757, "Colcothar", 100, 1f),
        new(42, 758, "Gypsum", 100, 1f),
        new(43, 759, "Quicksilver", 100, 1f),
        new(44, 760, "Realgar", 100, 1f),
        new(45, 761, "Stibnite", 100, 1f),
        new(46, 762, "Turpeth", 100, 1f),
        new(47, 763, "Verdigris", 100, 1f),
        new(48, 764, "Vitriol", 100, 1f),
        new(49, 749, "Poplar Talisman", 100, 0.1f),
        new(50, 742, "Blackthorn Talisman", 100, 0.1f),
        new(51, 752, "Yew Talisman", 100, 0.1f),
        new(52, 747, "Hemlock Talisman", 100, 0.1f),
        new(53, 627, "Alder Talisman", 100, 0.1f),
        new(54, 744, "Ebony Talisman", 100, 0.1f),
        new(55, 741, "Birch Talisman", 100, 0.1f),
        new(56, 748, "Oak Talisman", 100, 0.1f),
        new(57, 740, "Ashwood Talisman", 100, 0.1f),
        new(58, 745, "Elder Talisman", 100, 0.1f),
        new(59, 746, "Hazel Talisman", 100, 0.1f),
        new(60, 750, "Rowan Talisman", 100, 0.1f),
        new(61, 751, "Willow Talisman", 100, 0.1f),
        new(62, 743, "Cedar Talisman", 100, 0.1f),
        new(63, 1650, "Red Taper", 100, 1f),
        new(64, 1649, "Pink Taper", 100, 1f),
        new(65, 1648, "Orange Taper", 100, 1f),
        new(66, 1653, "Yellow Taper", 100, 1f),
        new(67, 1645, "Green Taper", 100, 1f),
        new(68, 1654, "Turquoise Taper", 100, 1f),
        new(69, 1643, "Blue Taper", 100, 1f),
        new(70, 1647, "Indigo Taper", 100, 1f),
        new(71, 1651, "Violet Taper", 100, 1f),
        new(72, 1644, "Brown Taper", 100, 1f),
        new(73, 1652, "White Taper", 100, 1f),
        new(74, 1646, "Grey Taper", 100, 1f),
        new(110, 7299, "Diamond Scarab", 100, 0.01f),
        new(111, 7581, "Chorizite", 100, 1f),
        new(112, 8897, "Platinum Scarab", 100, 0.03f),
        new(187, 9148, "Moo Juice", 100, 0.001f),
        new(188, 20631, "Prismatic Taper", 1000, 1f),
        new(190, 25730, "Banyan Talisman", 100, 0.1f),
        new(193, 37155, "Mana Scarab", 100, 0.03f),
    ];

    private static readonly FrozenDictionary<uint, ComponentWeenie> ById = All.ToFrozenDictionary(c => c.ComponentId);

    private static readonly FrozenDictionary<uint, ComponentWeenie> ByWcidMap = All.ToFrozenDictionary(c => c.Wcid);

    /// <summary>The pinned component with id <paramref name="componentId"/>.</summary>
    /// <param name="componentId">The id a spell formula holds.</param>
    /// <param name="component">The component, or default.</param>
    /// <returns>True when the map holds it.</returns>
    public static bool TryGetByComponent(uint componentId, out ComponentWeenie component) => ById.TryGetValue(componentId, out component);

    /// <summary>The pinned component whose weenie is <paramref name="wcid"/>.</summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="component">The component, or default.</param>
    /// <returns>True when the map holds it.</returns>
    public static bool TryGetByWcid(uint wcid, out ComponentWeenie component) => ByWcidMap.TryGetValue(wcid, out component);

    /// <summary>True for the twelve coloured tapers (ids 63 to 74): the ones <c>SpellFormula::RandomizeForName</c> writes into a formula.</summary>
    public static bool IsColouredTaper(uint componentId) => componentId is >= SpellFormulaRules.LowestTaperId and < SpellFormulaRules.LowestTaperId + 12;

    /// <summary>The portal dat's DualDidMapper of the spell components, which ACE's <c>Spell.GetComponentWCID</c> reads.</summary>
    public const uint DualDidMapperFileId = 0x27000002;

    /// <summary>
    /// The dat's component id to weenie class id (<c>ClientEnumToID</c>), from the raw record. The package of the dat reader has no type for the DualDidMapper, so the first table of the record is read here. The layout is the
    /// retail one: <c>u32 id</c> (the <c>DBObj</c> header), then the table as <c>SerializeIntrusiveHashTable&lt;..., IDClass&lt;_tagDataID,32,0&gt; ..., SB_Default&gt;</c> writes it (11.4186 0x00415C60, 11.6096 0x00415F00, called by
    /// <c>EnumIDMap::Serialize</c>, 11.4186 0x004165F0, 11.6096 0x00416890): a <c>u8</c> index into the bucket-size table, the element count in the dat's packed dword format (<see cref="ReadCompressed"/>), then <c>count x (u32
    /// component id, u32 weenie class id)</c>, little endian. The name tables after it are not read. ACE's <c>DualDidMapper</c> and <c>ReadCompressedUInt32</c> (ACE.DatLoader) read the same bytes and are a cross-check only: the
    /// tests compare the result with the SELECT-pinned map.
    /// </summary>
    /// <param name="raw">The record's bytes.</param>
    /// <returns>The map.</returns>
    /// <exception cref="InvalidDataException">The record is shorter than its count says.</exception>
    public static IReadOnlyDictionary<uint, uint> DecodeClientEnumToId(ReadOnlySpan<byte> raw)
    {
        var at = 5;
        var count = ReadCompressed(raw, ref at);
        if (raw.Length < at + (long)count * 8)
        {
            throw new InvalidDataException($"DualDidMapper 0x{DualDidMapperFileId:X8}: {count} pairs do not fit in {raw.Length} bytes.");
        }

        var map = new Dictionary<uint, uint>((int)count);
        for (var i = 0; i < count; i++)
        {
            map[BitConverter.ToUInt32(raw.Slice(at, 4))] = BitConverter.ToUInt32(raw.Slice(at + 4, 4));
            at += 8;
        }

        return map;
    }

    /// <summary>The dat's component id to weenie class id, from the user's portal dat.</summary>
    /// <param name="dats">The dats.</param>
    /// <returns>The map.</returns>
    public static IReadOnlyDictionary<uint, uint> LoadClientEnumToId(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return DecodeClientEnumToId(dats.TryGetPortalBytes(DualDidMapperFileId) ?? throw new InvalidDataException($"DualDidMapper 0x{DualDidMapperFileId:X8} not found."));
    }

    /// <summary>
    /// Reads one dword in the dat's packed format, the form retail's <c>SB_As32Bit_Compressed::Serialize</c> (11.4186 0x005BE430, 11.6096 0x00401A80 in the symbol table) reads from an archive: a first byte below 0x80 is the
    /// value; with the top bit set, the value is 15 bits, the low 7 bits of the first byte then the second byte; with the top two bits set, it is 30 bits, the low 6 bits of the first byte, the second byte, then a 16-bit little-endian
    /// word of the low bits. The branches are the format's own (the writer's thresholds 0x7F and 0x3FFF, same function), not copied from another reader.
    /// </summary>
    private static uint ReadCompressed(ReadOnlySpan<byte> raw, ref int at)
    {
        var b0 = raw[at++];
        if ((b0 & 0x80) == 0)
        {
            return b0;
        }

        var b1 = raw[at++];
        if ((b0 & 0x40) == 0)
        {
            return (uint)(((b0 & 0x7F) << 8) | b1);
        }

        var low = BitConverter.ToUInt16(raw.Slice(at, 2));
        at += 2;
        return ((uint)(((b0 & 0x3F) << 8) | b1) << 16) | low;
    }
}

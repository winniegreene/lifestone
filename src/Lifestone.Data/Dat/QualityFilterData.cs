using System.Buffers.Binary;
using System.Collections.Frozen;

namespace Lifestone.Data.Dat;

/// <summary>
/// The portal's quality filter that says which qualities a client keeps (<c>ACQualityFilter</c>, a portal file 0x0E01xxxx), of which the client asks one question here: may this secondary attribute (a vital) be
/// enchanted (<c>ACQualityFilter::QueryAttribute2nd</c>, 11.6096 0x005CCBC0; <c>CEnchantmentRegistry::EnchantAttribute2nd</c> reaches it with <c>DBObj::GetByEnum</c> of the enum 0x10000002 of enum group 3 and the object type 0x1000000C).
/// Immutable and safe from any thread.
/// </summary>
/// <remarks>
/// <para>
/// The layout is re-expressed from <c>QualityFilter::UnPack</c> (11.6096 0x006B3490) and <c>ACQualityFilter::UnPack</c>: the file id, the eight counts of the base filter (ints, int64s, bools, floats, DIDs, IIDs,
/// strings, positions), those eight lists of u32 stat ids back to back, then the three counts of the attribute filter (attributes, secondary attributes, skills) and their three lists of u32 ids. A stat id in a list
/// is a stat the filter keeps (<c>SetAttribute2nd</c> stores 1); one not in the list answers 0. The decoder fails loudly unless the lists consume the file exactly.
/// </para>
/// </remarks>
public sealed class QualityFilterData
{
    /// <summary>The enum <c>CEnchantmentRegistry::EnchantAttribute2nd</c> asks the enum group for (<c>DBObj::GetByEnum</c> of 0x10000002, group 3, the object type 0x1000000C).</summary>
    public const uint EnchantableEnum = 0x10000002u;

    /// <summary>The enum group of the quality filters in the master map (the 3 retail passes to <c>DBObj::GetByEnum</c>).</summary>
    public static readonly EnumGroup QualityFilterGroup = new(3);

    private readonly FrozenSet<uint> _attributes;
    private readonly FrozenSet<uint> _attribute2nds;
    private readonly FrozenSet<uint> _skills;

    private QualityFilterData(uint dataId, uint[] attributes, uint[] attribute2nds, uint[] skills)
    {
        DataId = dataId;
        _attributes = attributes.ToFrozenSet();
        _attribute2nds = attribute2nds.ToFrozenSet();
        _skills = skills.ToFrozenSet();
        Attribute2ndIds = attribute2nds;
        AttributeIds = attributes;
        SkillIds = skills;
    }

    /// <summary>The file's id.</summary>
    public uint DataId { get; }

    /// <summary>The secondary attribute ids the filter keeps, in file order.</summary>
    public IReadOnlyList<uint> Attribute2ndIds { get; }

    /// <summary>The primary attribute ids the filter keeps, in file order.</summary>
    public IReadOnlyList<uint> AttributeIds { get; }

    /// <summary>The skill ids the filter keeps, in file order.</summary>
    public IReadOnlyList<uint> SkillIds { get; }

    /// <summary><c>ACQualityFilter::QueryAttribute2nd</c>: true when the filter keeps the secondary attribute <paramref name="id"/> (a vital's id), false otherwise.</summary>
    public bool QueryAttribute2nd(uint id) => _attribute2nds.Contains(id);

    /// <summary><c>ACQualityFilter::QueryAttribute</c>: true when the filter keeps the primary attribute <paramref name="id"/>.</summary>
    public bool QueryAttribute(uint id) => _attributes.Contains(id);

    /// <summary>True when the filter keeps the skill <paramref name="id"/>.</summary>
    public bool QuerySkill(uint id) => _skills.Contains(id);

    /// <summary>Reads the filter retail asks for the enchantable attribute question: the enum <see cref="EnchantableEnum"/> of group 3 in the master map, then the file in the portal dat.</summary>
    /// <exception cref="InvalidDataException">The enum does not resolve, the file is absent or it does not match the layout.</exception>
    public static QualityFilterData LoadEnchantable(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var did = dats.EnumMaps.Resolve(QualityFilterGroup, EnchantableEnum);
        var raw = dats.TryGetPortalBytes(did)
            ?? throw new InvalidDataException($"TODO(g4-quality-filter): the quality filter 0x{did:X8} (enum 0x{EnchantableEnum:X8}) is not in {DatSet.PortalFileName}.");
        return Decode(raw, did);
    }

    /// <summary>Decodes the file bytes (the file id first); <paramref name="expectedId"/> is checked against it.</summary>
    /// <exception cref="InvalidDataException">The bytes do not match the layout exactly.</exception>
    public static QualityFilterData Decode(ReadOnlySpan<byte> raw, uint expectedId)
    {
        var at = 0;
        var id = U32(raw, ref at);
        if (id != expectedId)
        {
            throw new InvalidDataException($"TODO(g4-quality-filter): the file id is 0x{id:X8}, not 0x{expectedId:X8}.");
        }

        long baseIds = 0;
        for (var i = 0; i < 8; i++)
        {
            baseIds += U32(raw, ref at);
        }

        for (long i = 0; i < baseIds; i++)
        {
            U32(raw, ref at); // the base filter's int, bool, float, ... stat ids: not needed
        }

        var attributeCount = U32(raw, ref at);
        var attribute2ndCount = U32(raw, ref at);
        var skillCount = U32(raw, ref at);
        var attributes = List(raw, ref at, attributeCount);
        var attribute2nds = List(raw, ref at, attribute2ndCount);
        var skills = List(raw, ref at, skillCount);
        return at == raw.Length
            ? new QualityFilterData(id, attributes, attribute2nds, skills)
            : throw new InvalidDataException($"TODO(g4-quality-filter): {raw.Length - at} bytes follow the filter's lists.");
    }

    private static uint[] List(ReadOnlySpan<byte> raw, ref int at, uint count)
    {
        if (count > (uint)((raw.Length - at) / 4))
        {
            throw new InvalidDataException($"TODO(g4-quality-filter): a list of {count} ids does not fit the {raw.Length - at} bytes left.");
        }

        var list = new uint[count];
        for (var i = 0; i < list.Length; i++)
        {
            list[i] = U32(raw, ref at);
        }

        return list;
    }

    private static uint U32(ReadOnlySpan<byte> raw, ref int at)
    {
        if (raw.Length - at < 4)
        {
            throw new InvalidDataException($"TODO(g4-quality-filter): the file ends at byte {at}, mid-field.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(raw[at..]);
        at += 4;
        return value;
    }
}

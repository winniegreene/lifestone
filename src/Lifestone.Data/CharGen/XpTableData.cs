using System.Buffers.Binary;
using Lifestone.Data.Dat;

namespace Lifestone.Data.CharacterCreation;

/// <summary>
/// The XpTable (0x0E000018 in <c>client_portal.dat</c>): the cumulative experience each rank of an attribute, a vital and
/// a skill costs, the experience each character level needs and the skill credits each level grants. Immutable and safe
/// from any thread.
/// </summary>
/// <remarks>
/// <para>
/// Layout (a data format, re-expressed here; the reference is the field order of <c>XpTable.Unpack</c> in
/// ACEmulator/ACE <c>Source/ACE.DatLoader/FileTypes/XpTable.cs</c> lines 25-56, AGPL-3.0): the file id (u32), then the
/// counts <c>attribute</c>, <c>vital</c>, <c>trained skill</c> and <c>specialised skill</c> (four i32) and the level
/// count (u32), then six lists back to back. Each of the first four lists holds <c>count + 1</c> u32 entries, the level
/// list holds <c>levelCount + 1</c> u64 entries and the credit list <c>levelCount + 1</c> u32 entries. A count is
/// therefore the highest index, not the length, and entry 0 is the origin (rank 0, level 0).
/// </para>
/// <para>
/// Every list is cumulative: <c>list[n]</c> is the total experience to reach rank (or level) <c>n</c>. The experience to
/// move from rank <c>a</c> to rank <c>b</c> is <c>list[b] - list[a]</c> (<see cref="XpToRaise"/>). For attributes, vitals
/// and skills a rank counts from the value the character was created with (ACE's <c>InitLevel</c>), so an attribute
/// created at 10 and raised to 30 has 20 ranks. The retail client's reader is not decompiled here; <c>XpTableTests</c>
/// pins what the file holds, and the decoder fails loudly unless the six lists consume the file exactly.
/// </para>
/// </remarks>
public sealed class XpTableData
{
    /// <summary>The XpTable's DID.</summary>
    public const uint DataId = 0x0E000018u;

    private XpTableData(
        uint[] attribute, uint[] vital, uint[] trainedSkill, uint[] specializedSkill, ulong[] characterLevelXp, uint[] characterLevelSkillCredits)
    {
        AttributeXpList = attribute;
        VitalXpList = vital;
        TrainedSkillXpList = trainedSkill;
        SpecializedSkillXpList = specializedSkill;
        CharacterLevelXpList = characterLevelXp;
        CharacterLevelSkillCreditList = characterLevelSkillCredits;
    }

    /// <summary>Total XP to reach attribute rank <c>n</c> (index n; index 0 is 0).</summary>
    public IReadOnlyList<uint> AttributeXpList { get; }

    /// <summary>Total XP to reach vital rank <c>n</c>.</summary>
    public IReadOnlyList<uint> VitalXpList { get; }

    /// <summary>Total XP to reach rank <c>n</c> of a trained skill.</summary>
    public IReadOnlyList<uint> TrainedSkillXpList { get; }

    /// <summary>Total XP to reach rank <c>n</c> of a specialised skill.</summary>
    public IReadOnlyList<uint> SpecializedSkillXpList { get; }

    /// <summary>Total XP a character needs to be level <c>n</c> (index n).</summary>
    public IReadOnlyList<ulong> CharacterLevelXpList { get; }

    /// <summary>Skill credits the character is granted on reaching level <c>n</c> (index n).</summary>
    public IReadOnlyList<uint> CharacterLevelSkillCreditList { get; }

    /// <summary>The highest character level the table has an entry for (the level list's highest index).</summary>
    public int LevelCap => CharacterLevelXpList.Count - 1;

    /// <summary>Reads the XpTable from the portal dat.</summary>
    /// <exception cref="InvalidDataException">The file is absent or does not match the layout.</exception>
    public static XpTableData Load(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var raw = dats.TryGetPortalBytes(DataId)
            ?? throw new InvalidDataException($"TODO(m3-xp-table): XpTable 0x{DataId:X8} is not in {DatSet.PortalFileName}.");
        return Decode(raw);
    }

    /// <summary>Decodes the XpTable file bytes (the file id first).</summary>
    /// <exception cref="InvalidDataException">The bytes do not match the layout exactly.</exception>
    public static XpTableData Decode(ReadOnlySpan<byte> raw)
    {
        var at = 0;
        var id = U32(raw, ref at);
        if (id != DataId)
        {
            throw new InvalidDataException($"TODO(m3-xp-table): the file id is 0x{id:X8}, not 0x{DataId:X8}.");
        }

        var attributeCount = Count(raw, ref at);
        var vitalCount = Count(raw, ref at);
        var trainedCount = Count(raw, ref at);
        var specializedCount = Count(raw, ref at);
        var levelCount = Count(raw, ref at);

        var attribute = U32List(raw, ref at, attributeCount + 1);
        var vital = U32List(raw, ref at, vitalCount + 1);
        var trained = U32List(raw, ref at, trainedCount + 1);
        var specialized = U32List(raw, ref at, specializedCount + 1);
        var levelXp = new ulong[levelCount + 1];
        for (var i = 0; i < levelXp.Length; i++)
        {
            levelXp[i] = U64(raw, ref at);
        }

        var credits = U32List(raw, ref at, levelCount + 1);
        return at == raw.Length
            ? new XpTableData(attribute, vital, trained, specialized, levelXp, credits)
            : throw new InvalidDataException($"TODO(m3-xp-table): {raw.Length - at} bytes follow the six lists.");
    }

    /// <summary>
    /// The XP to move from rank <paramref name="fromRank"/> to rank <paramref name="toRank"/> in a cumulative
    /// <paramref name="list"/>: <c>list[toRank] - list[fromRank]</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A rank is outside the list, or <paramref name="toRank"/> is below <paramref name="fromRank"/>.</exception>
    public static ulong XpToRaise(IReadOnlyList<uint> list, int fromRank, int toRank)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentOutOfRangeException.ThrowIfNegative(fromRank);
        ArgumentOutOfRangeException.ThrowIfLessThan(toRank, fromRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toRank, list.Count);
        return list[toRank] - (ulong)list[fromRank];
    }

    /// <summary>XP to raise an attribute from rank <paramref name="fromRank"/> to <paramref name="toRank"/> (ranks above its creation value).</summary>
    public ulong XpToRaiseAttribute(int fromRank, int toRank) => XpToRaise(AttributeXpList, fromRank, toRank);

    /// <summary>XP to raise a vital from rank <paramref name="fromRank"/> to <paramref name="toRank"/>.</summary>
    public ulong XpToRaiseVital(int fromRank, int toRank) => XpToRaise(VitalXpList, fromRank, toRank);

    /// <summary>XP to raise a trained skill from rank <paramref name="fromRank"/> to <paramref name="toRank"/>.</summary>
    public ulong XpToRaiseTrainedSkill(int fromRank, int toRank) => XpToRaise(TrainedSkillXpList, fromRank, toRank);

    /// <summary>XP to raise a specialised skill from rank <paramref name="fromRank"/> to <paramref name="toRank"/>.</summary>
    public ulong XpToRaiseSpecializedSkill(int fromRank, int toRank) => XpToRaise(SpecializedSkillXpList, fromRank, toRank);

    /// <summary>The total XP to be level <paramref name="level"/> (<see cref="CharacterLevelXpList"/>[level]).</summary>
    public ulong XpForLevel(int level) =>
        (uint)level < (uint)CharacterLevelXpList.Count
            ? CharacterLevelXpList[level]
            : throw new ArgumentOutOfRangeException(nameof(level), level, $"TODO(m3-xp-table): the table ends at level {LevelCap}.");

    /// <summary>
    /// The skill credits granted by the levels <paramref name="fromLevel"/> + 1 through <paramref name="level"/>: the sum of
    /// <see cref="CharacterLevelSkillCreditList"/> over that range (entries 0 through <paramref name="fromLevel"/> are not
    /// counted; pass 1 to leave level 1's entry out, 0 to include it).
    /// </summary>
    public uint SkillCreditsThroughLevel(int level, int fromLevel = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromLevel);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, fromLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, CharacterLevelSkillCreditList.Count);
        uint sum = 0;
        for (var n = fromLevel + 1; n <= level; n++)
        {
            sum += CharacterLevelSkillCreditList[n];
        }

        return sum;
    }

    private static int Count(ReadOnlySpan<byte> raw, ref int at)
    {
        var value = U32(raw, ref at);
        return value <= 100_000 ? (int)value : throw new InvalidDataException($"TODO(m3-xp-table): implausible list count {value}.");
    }

    private static uint[] U32List(ReadOnlySpan<byte> raw, ref int at, int length)
    {
        var list = new uint[length];
        for (var i = 0; i < length; i++)
        {
            list[i] = U32(raw, ref at);
        }

        return list;
    }

    private static uint U32(ReadOnlySpan<byte> raw, ref int at)
    {
        if (raw.Length - at < 4)
        {
            throw new InvalidDataException("TODO(m3-xp-table): the file ends inside a list.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(raw[at..]);
        at += 4;
        return value;
    }

    private static ulong U64(ReadOnlySpan<byte> raw, ref int at)
    {
        if (raw.Length - at < 8)
        {
            throw new InvalidDataException("TODO(m3-xp-table): the file ends inside the level list.");
        }

        var value = BinaryPrimitives.ReadUInt64LittleEndian(raw[at..]);
        at += 8;
        return value;
    }
}

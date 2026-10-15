// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/PlayerDescriptionParser.cs and
// EnchantmentWireReader.cs (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: decoupled from
// AcDream.Core.Items (PropertyBundle, ShortcutEntry), so the result is the N0 record PlayerDescriptionMsg, extended
// additively; one internal static PlayerDescriptionCodec over N2's WireReader that never throws and accepts the event
// only when every byte was read (OpenAC caught FormatException, returned partial results and searched for the inventory
// heuristically); every section follows retail's reader: a spellbook entry is SpellBookPage::UnPack's variable form
// (OpenAC read one f32), the timestamp format and the generic options data are read (OpenAC skipped them), the player
// module ends with retail's align, a vector flag whose section is not decoded is refused by name, an enchantment's values
// stay raw (OpenAC refused non-finite ones), and the gameplay options are kept raw with the tail after them instead of
// being delimited by a guess (see the remarks).

using System.Collections.Immutable;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// GameEvent 0x0013 PlayerDescription (m2-plan §3.3 N10; owner decision D16), as ACE's
/// <c>GameEventPlayerDescription.WriteEventBody</c> writes it and retail's <c>CPlayerSystem::Handle_PlayerDescription</c>
/// (11.4186 0x005636A0, 11.6096 0x00564440, a predicted row of the symbol table) reads it.
/// </summary>
/// <remarks>
/// <para><b>The qualities</b> (<c>CACQualities::UnPack</c>, 11.4186 0x00593420, 11.6096 0x005941D0, after
/// <c>CBaseQualities::UnPack</c>, 11.4186 0x00595F10, 11.6096 0x00596E10): the property flags and the weenie type, then
/// the property tables in retail's order (int 0x1, int64 0x80, bool 0x2, float 0x4, string 0x10, data id 0x8, instance id
/// 0x40, position 0x20), each a packable hash table (u16 count, u16 buckets, then key and value). Then the vector flags,
/// the has-health word, and the vector sections in retail's order: the attribute cache 0x1 (<c>AttributeCache::UnPack</c>,
/// 11.4186 0x005CCFB0, 11.6096 0x005CDF00: a flags word, then 12 bytes per primary attribute and 16 per vital,
/// <c>Attribute::UnPack</c> and <c>SecondaryAttribute::UnPack</c>, 11.4186 0x005CABF0 and 0x005CAC90, 11.6096 0x005CBB20
/// and 0x005CBBC0), the skills 0x2 (<c>Skill::UnPack</c>, 11.4186 0x005CAAC0, 11.6096 0x005CBA30: 28 bytes after the key),
/// the spellbook 0x100 (<c>CSpellBook::UnPack</c>, 11.4186 0x00595BB0, 11.6096 0x00596AB0, with
/// <c>SpellBookPage::UnPack</c>, 11.4186 0x005CC260, 11.6096 0x005CD1B0) and the enchantment registry 0x200
/// (<c>CEnchantmentRegistry::UnPack</c>, 11.4186 0x00593FF0, 11.6096 0x00594E60: multiplicative, additive, cooldown, then
/// the single vitae; <c>Enchantment::UnPack</c>, 11.4186 0x005CB040, 11.6096 0x005CBF70). ACE sends no other vector
/// section; retail's Body 0x4, EventFilter 0x8, EmoteTable 0x10, CreationProfile 0x20, PageDataList 0x40, GeneratorTable
/// 0x80, GeneratorRegistry 0x400 and GeneratorQueue 0x800 are not decoded, and a description that carries one is
/// malformed with the section named (TODO(net): only if a server ever sends one).</para>
/// <para><b>The player module</b> (<c>PlayerModule::UnPack</c>, 11.4186 0x005D49D0, 11.6096 0x005D5B20, a predicted row):
/// the option flags and the first options word; the shortcuts 0x1 (<c>ShortCutManager::UnPack</c>, 11.4186 0x005D5820,
/// 11.6096 0x005D6930: a count, then 12 bytes each); one favourite-spell list, then four more with 0x4, else six with
/// 0x10, else seven with 0x400; the desired components 0x8; the spellbook filters 0x20 (default 0x3FFF); the second
/// options word 0x40 (default 0x948700); the timestamp format 0x80; the generic options data 0x100
/// (<c>GenericQualitiesData::UnPack</c>, 11.4186 0x006B79B0, 11.6096 0x006B8880); the gameplay options 0x200; then an
/// align. <b>The tail:</b> the main pack's contents (a count, then GUID and container type) and the equipped objects (a
/// count, then GUID, location and priority).</para>
/// <para><b>The gameplay options (0x200).</b> Retail reads them as a property collection through a versioned archive
/// (<c>PackUsingSerialize&lt;PackObj&gt;::UnPack</c>, <c>PackObjPropertyCollection::Serialize</c>, 11.4186 0x005D4F80,
/// 11.6096 0x005D60D0), which Lifestone does not decode; ACE stores and returns the bytes the client last sent, with no
/// length in front. Their end cannot be found from the tail either: a suffix of a content list whose first word reads as
/// a small count (the last entry a non-container, or the one before it a container) is itself a valid tail, so a search
/// would be a guess. So the options and the tail after them stay undecoded together:
/// <see cref="PlayerDescriptionMsg.GameplayOptions"/> holds those bytes and
/// <see cref="PlayerDescriptionMsg.InventoryUndecoded"/> is true (TODO(net): decode the archive when an options screen or
/// the inventory needs it). Everything before them, vitals included, is decoded. ACE sets the flag only for a character
/// a client sent options for (<c>GameActionSetCharacterOptions</c>); Lifestone sends none in M2.</para>
/// </remarks>
internal static class PlayerDescriptionCodec
{
    /// <summary>The prefix of every malformed reason.</summary>
    private const string Prefix = "F7B0 GameEvent 0x0013 PlayerDescription: ";

    private const string Truncated = Prefix + "truncated, trailing bytes or a count larger than the message";

    // CBaseQualities pack header.
    private const uint PropertyInt = 0x1;
    private const uint PropertyBool = 0x2;
    private const uint PropertyFloat = 0x4;
    private const uint PropertyDataId = 0x8;
    private const uint PropertyString = 0x10;
    private const uint PropertyPosition = 0x20;
    private const uint PropertyInstanceId = 0x40;
    private const uint PropertyInt64 = 0x80;

    // CACQualities pack header.
    private const uint VectorAttribute = 0x1;
    private const uint VectorSkill = 0x2;
    private const uint VectorSpell = 0x100;
    private const uint VectorEnchantment = 0x200;

    // PlayerModule pack header (ACE's CharacterOptionDataFlag names).
    private const uint OptionShortcut = 0x1;
    private const uint OptionMultiSpellList = 0x4;
    private const uint OptionDesiredComps = 0x8;
    private const uint OptionExtendedMultiSpellLists = 0x10;
    private const uint OptionSpellbookFilters = 0x20;
    private const uint OptionCharacterOptions2 = 0x40;
    private const uint OptionTimestampFormat = 0x80;
    private const uint OptionGenericQualitiesData = 0x100;
    private const uint OptionGameplayOptions = 0x200;
    private const uint OptionSpellLists8 = 0x400;

    /// <summary>Retail's spellbook filters when the flags omit them (<c>PlayerModule::UnPack</c>).</summary>
    public const uint DefaultSpellbookFilters = 0x3FFF;

    /// <summary>Retail's second options word when the flags omit it (<c>PlayerModule::UnPack</c>: 9733888).</summary>
    public const uint DefaultOptions2 = 0x948700;

    /// <summary>The vector sections retail reads and Lifestone does not, in retail's reading order, with their names.</summary>
    private static readonly (uint Flag, string Name)[] UndecodedVectorSections =
    [
        (0x4, "Body"), (0x8, "EventFilter"), (0x10, "EmoteTable"), (0x20, "CreationProfile list"), (0x40, "PageDataList"),
        (0x80, "GeneratorTable"), (0x400, "GeneratorRegistry"), (0x800, "GeneratorQueue"),
    ];

    /// <summary>
    /// Decodes <paramref name="message"/>, the whole F7B0 message; false with a reason naming the problem when it is not
    /// a whole, well-formed PlayerDescription.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out PlayerDescriptionMsg description, out string reason)
    {
        description = null!;
        reason = Truncated;
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            return false;
        }

        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);

        // The qualities.
        if (!reader.TryReadU32(out var propertyFlags) || !reader.TryReadU32(out var weenieType))
        {
            return false;
        }

        var ints = ImmutableDictionary<uint, int>.Empty;
        var int64s = ImmutableDictionary<uint, long>.Empty;
        var bools = ImmutableDictionary<uint, bool>.Empty;
        var floats = ImmutableDictionary<uint, double>.Empty;
        var strings = ImmutableDictionary<uint, string>.Empty;
        var dataIds = ImmutableDictionary<uint, uint>.Empty;
        var instanceIds = ImmutableDictionary<uint, uint>.Empty;
        var positions = ImmutableDictionary<uint, AcPosition>.Empty;
        if (((propertyFlags & PropertyInt) != 0 && !TryReadInts(ref reader, out ints))
            || ((propertyFlags & PropertyInt64) != 0 && !TryReadInt64s(ref reader, out int64s))
            || ((propertyFlags & PropertyBool) != 0 && !TryReadBools(ref reader, out bools))
            || ((propertyFlags & PropertyFloat) != 0 && !TryReadFloats(ref reader, out floats))
            || ((propertyFlags & PropertyString) != 0 && !TryReadStrings(ref reader, out strings))
            || ((propertyFlags & PropertyDataId) != 0 && !TryReadWords(ref reader, out dataIds))
            || ((propertyFlags & PropertyInstanceId) != 0 && !TryReadWords(ref reader, out instanceIds))
            || ((propertyFlags & PropertyPosition) != 0 && !TryReadPositions(ref reader, out positions))
            || !reader.TryReadU32(out var vectorFlags)
            || !reader.TryReadU32(out var hasHealth))
        {
            return false;
        }

        foreach (var (flag, name) in UndecodedVectorSections)
        {
            if ((vectorFlags & flag) != 0)
            {
                reason = $"{Prefix}vector flag 0x{flag:X} ({name}) is set, and Lifestone does not decode that section (TODO(net): ACE never sends it)";
                return false;
            }
        }

        uint attributeFlags = 0;
        ImmutableArray<AttributeDesc> attributes = [];
        ImmutableArray<VitalDesc> vitals = [];
        ImmutableArray<SkillDesc> skills = [];
        var spells = ImmutableDictionary<uint, float>.Empty;
        uint enchantmentMask = 0;
        ImmutableArray<EnchantmentDesc> enchantments = [];
        if (((vectorFlags & VectorAttribute) != 0 && !TryReadAttributeCache(ref reader, out attributeFlags, out attributes, out vitals))
            || ((vectorFlags & VectorSkill) != 0 && !TryReadSkills(ref reader, out skills))
            || ((vectorFlags & VectorSpell) != 0 && !TryReadSpellbook(ref reader, out spells))
            || ((vectorFlags & VectorEnchantment) != 0 && !TryReadEnchantmentRegistry(ref reader, out enchantmentMask, out enchantments)))
        {
            return false;
        }

        // The player module.
        if (!reader.TryReadU32(out var optionFlags) || !reader.TryReadU32(out var options1))
        {
            return false;
        }

        ImmutableArray<ShortcutDesc> shortcuts = [];
        if ((optionFlags & OptionShortcut) != 0 && !TryReadShortcuts(ref reader, out shortcuts))
        {
            return false;
        }

        var extraBars = (optionFlags & OptionMultiSpellList) != 0 ? 4
            : (optionFlags & OptionExtendedMultiSpellLists) != 0 ? 6
            : (optionFlags & OptionSpellLists8) != 0 ? 7
            : 0;
        var bars = ImmutableArray.CreateBuilder<ImmutableArray<uint>>(1 + extraBars);
        for (var i = 0; i <= extraBars; i++)
        {
            if (!TryReadWordList(ref reader, out var bar))
            {
                return false;
            }

            bars.Add(bar);
        }

        ImmutableArray<DesiredComponentDesc> components = [];
        var spellbookFilters = DefaultSpellbookFilters;
        var options2 = DefaultOptions2;
        string? timestampFormat = null;
        GenericQualitiesDesc? optionsData = null;
        if (((optionFlags & OptionDesiredComps) != 0 && !TryReadDesiredComponents(ref reader, out components))
            || ((optionFlags & OptionSpellbookFilters) != 0 && !reader.TryReadU32(out spellbookFilters))
            || ((optionFlags & OptionCharacterOptions2) != 0 && !reader.TryReadU32(out options2))
            || ((optionFlags & OptionTimestampFormat) != 0 && !TryReadString(ref reader, out timestampFormat))
            || ((optionFlags & OptionGenericQualitiesData) != 0 && !TryReadGenericQualities(ref reader, out optionsData)))
        {
            return false;
        }

        ImmutableArray<byte> gameplayOptions = [];
        ImmutableArray<ContentDesc> inventory = [];
        ImmutableArray<EquippedDesc> equipped = [];
        var inventoryUndecoded = (optionFlags & OptionGameplayOptions) != 0;
        if (inventoryUndecoded)
        {
            // The options, and the tail after them, stay raw (see the remarks).
            gameplayOptions = [.. reader.RemainingBytes];
        }
        else if (!reader.TryAlign()
            || !TryReadContents(ref reader, out inventory)
            || !TryReadEquipped(ref reader, out equipped)
            || !reader.IsAtEnd)
        {
            return false;
        }

        description = new PlayerDescriptionMsg
        {
            WeenieType = weenieType,
            IntProperties = ints,
            Int64Properties = int64s,
            BoolProperties = bools,
            FloatProperties = floats,
            StringProperties = strings,
            DataIdProperties = dataIds,
            InstanceIdProperties = instanceIds,
            Attributes = attributes,
            Vitals = vitals,
            Skills = skills,
            Spells = spells,
            PropertyFlags = propertyFlags,
            PositionProperties = positions,
            VectorFlags = vectorFlags,
            HasHealth = hasHealth != 0,
            AttributeFlags = attributeFlags,
            Enchantments = enchantments,
            EnchantmentMask = enchantmentMask,
            OptionFlags = optionFlags,
            Options1 = options1,
            Options2 = options2,
            SpellbookFilters = spellbookFilters,
            Shortcuts = shortcuts,
            SpellBars = bars.MoveToImmutable(),
            DesiredComponents = components,
            TimestampFormat = timestampFormat,
            OptionsData = optionsData,
            GameplayOptions = gameplayOptions,
            InventoryUndecoded = inventoryUndecoded,
            Inventory = inventory,
            Equipped = equipped,
        };
        reason = string.Empty;
        return true;
    }

    // ---- The property tables (retail PackableHashTable<K, V>::UnPack: a u32 whose low half is the count) ----

    /// <summary>Reads a packable hash table's header: the u16 count and the u16 bucket count, the count checked to fit.</summary>
    private static bool TryReadTableHeader(ref WireReader reader, int minimumEntrySize, out int count)
    {
        count = 0;
        if (!reader.TryReadU16(out var raw) || !reader.TryReadU16(out _) || raw > reader.Remaining / minimumEntrySize)
        {
            return false;
        }

        count = raw;
        return true;
    }

    private static bool TryReadInts(ref WireReader reader, out ImmutableDictionary<uint, int> table)
    {
        table = ImmutableDictionary<uint, int>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, int>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadI32(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadInt64s(ref WireReader reader, out ImmutableDictionary<uint, long> table)
    {
        table = ImmutableDictionary<uint, long>.Empty;
        if (!TryReadTableHeader(ref reader, 12, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, long>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadI64(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadBools(ref WireReader reader, out ImmutableDictionary<uint, bool> table)
    {
        table = ImmutableDictionary<uint, bool>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, bool>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadU32(out var value))
            {
                return false;
            }

            builder[key] = value != 0;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadFloats(ref WireReader reader, out ImmutableDictionary<uint, double> table)
    {
        table = ImmutableDictionary<uint, double>.Empty;
        if (!TryReadTableHeader(ref reader, 12, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, double>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadF64(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadStrings(ref WireReader reader, out ImmutableDictionary<uint, string> table)
    {
        table = ImmutableDictionary<uint, string>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, string>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadString16L(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    /// <summary>A table of u32 values: the data id and instance id tables.</summary>
    private static bool TryReadWords(ref WireReader reader, out ImmutableDictionary<uint, uint> table)
    {
        table = ImmutableDictionary<uint, uint>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, uint>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadU32(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadPositions(ref WireReader reader, out ImmutableDictionary<uint, AcPosition> table)
    {
        table = ImmutableDictionary<uint, AcPosition>.Empty;
        if (!TryReadTableHeader(ref reader, 4 + PositionCodec.Size, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, AcPosition>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !PositionCodec.TryRead(ref reader, out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    // ---- The vector sections ----

    /// <summary>The attribute cache: a flags word, then the primary attributes (bits 0-5) and the vitals (bits 6-8).</summary>
    private static bool TryReadAttributeCache(ref WireReader reader, out uint flags, out ImmutableArray<AttributeDesc> attributes, out ImmutableArray<VitalDesc> vitals)
    {
        attributes = [];
        vitals = [];
        if (!reader.TryReadU32(out flags))
        {
            return false;
        }

        var primary = ImmutableArray.CreateBuilder<AttributeDesc>();
        for (uint id = 1; id <= 6; id++)
        {
            if ((flags & (1u << (int)(id - 1))) == 0)
            {
                continue;
            }

            if (!reader.TryReadU32(out var ranks) || !reader.TryReadU32(out var start) || !reader.TryReadU32(out var experience))
            {
                return false;
            }

            primary.Add(new AttributeDesc(id, ranks, start, experience));
        }

        var secondary = ImmutableArray.CreateBuilder<VitalDesc>();
        for (var entry = 7; entry <= 9; entry++)
        {
            if ((flags & (1u << (entry - 1))) == 0)
            {
                continue;
            }

            if (!reader.TryReadU32(out var ranks) || !reader.TryReadU32(out var start) || !reader.TryReadU32(out var experience)
                || !reader.TryReadU32(out var current))
            {
                return false;
            }

            // Cache entries 7, 8 and 9 are health, stamina and mana: the vitals PrivateUpdateVital names 1, 3 and 5.
            secondary.Add(new VitalDesc((uint)(2 * (entry - 7)) + 1, ranks, start, experience, current));
        }

        attributes = primary.ToImmutable();
        vitals = secondary.ToImmutable();
        return true;
    }

    /// <summary>The skills table: key, then u16 ranks, u16 adjust, class, experience, init, resistance and an f64.</summary>
    private static bool TryReadSkills(ref WireReader reader, out ImmutableArray<SkillDesc> skills)
    {
        skills = [];
        if (!TryReadTableHeader(ref reader, 4 + SkillSize, out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<SkillDesc>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var id) || !TryReadSkill(ref reader, id, out var skill))
            {
                return false;
            }

            builder.Add(skill);
        }

        skills = builder.MoveToImmutable();
        return true;
    }

    /// <summary>The size of a skill after its id (<c>Skill::UnPack</c>).</summary>
    public const int SkillSize = 28;

    /// <summary>Reads one skill after its id: the PlayerDescription table and PrivateUpdateSkill share it.</summary>
    public static bool TryReadSkill(ref WireReader reader, uint id, out SkillDesc skill)
    {
        skill = default;
        if (!reader.TryReadU16(out var ranks)
            || !reader.TryReadU16(out var adjust)
            || !reader.TryReadU32(out var advancementClass)
            || !reader.TryReadU32(out var experience)
            || !reader.TryReadU32(out var init)
            || !reader.TryReadU32(out var resistance)
            || !reader.TryReadF64(out var lastUsed))
        {
            return false;
        }

        skill = new SkillDesc(id, ranks, adjust, advancementClass, experience, init, resistance, lastUsed);
        return true;
    }

    /// <summary>
    /// The spellbook: key, then <c>SpellBookPage::UnPack</c>'s entry. An f32 of 2 or more is the casting likelihood plus
    /// 2 (the form ACE writes, with 2.0); a smaller one opens the old form, a skipped word and the likelihood.
    /// </summary>
    private static bool TryReadSpellbook(ref WireReader reader, out ImmutableDictionary<uint, float> spells)
    {
        spells = ImmutableDictionary<uint, float>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, float>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var spell) || !reader.TryReadF32(out var first))
            {
                return false;
            }

            float likelihood;
            if (first < 2.0)
            {
                if (!reader.TrySkip(sizeof(uint)) || !reader.TryReadF32(out likelihood))
                {
                    return false;
                }
            }
            else
            {
                likelihood = (float)(first - 2.0);
            }

            builder[spell] = likelihood;
        }

        spells = builder.ToImmutable();
        return true;
    }

    /// <summary>The enchantment registry: a mask, then the lists it names in retail's order, then the vitae.</summary>
    private static bool TryReadEnchantmentRegistry(ref WireReader reader, out uint mask, out ImmutableArray<EnchantmentDesc> enchantments)
    {
        enchantments = [];
        if (!reader.TryReadU32(out mask))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<EnchantmentDesc>();
        if (((mask & (uint)EnchantmentBucket.Multiplicative) != 0 && !TryReadEnchantmentList(ref reader, EnchantmentBucket.Multiplicative, builder))
            || ((mask & (uint)EnchantmentBucket.Additive) != 0 && !TryReadEnchantmentList(ref reader, EnchantmentBucket.Additive, builder))
            || ((mask & (uint)EnchantmentBucket.Cooldown) != 0 && !TryReadEnchantmentList(ref reader, EnchantmentBucket.Cooldown, builder)))
        {
            return false;
        }

        if ((mask & (uint)EnchantmentBucket.Vitae) != 0)
        {
            if (!TryReadEnchantment(ref reader, EnchantmentBucket.Vitae, out var vitae))
            {
                return false;
            }

            builder.Add(vitae);
        }

        enchantments = builder.ToImmutable();
        return true;
    }

    /// <summary>The size of an enchantment without its spell set id.</summary>
    private const int EnchantmentSize = 60;

    private static bool TryReadEnchantmentList(ref WireReader reader, EnchantmentBucket bucket, ImmutableArray<EnchantmentDesc>.Builder builder)
    {
        if (!reader.TryReadCount(EnchantmentSize, out var count))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!TryReadEnchantment(ref reader, bucket, out var enchantment))
            {
                return false;
            }

            builder.Add(enchantment);
        }

        return true;
    }

    private static bool TryReadEnchantment(ref WireReader reader, EnchantmentBucket bucket, out EnchantmentDesc enchantment)
    {
        enchantment = default;
        if (!reader.TryReadU16(out var spellId)
            || !reader.TryReadU16(out var layer)
            || !reader.TryReadU16(out var category)
            || !reader.TryReadU16(out var hasSpellSetId)
            || !reader.TryReadU32(out var powerLevel)
            || !reader.TryReadF64(out var startTime)
            || !reader.TryReadF64(out var duration)
            || !reader.TryReadU32(out var caster)
            || !reader.TryReadF32(out var degradeModifier)
            || !reader.TryReadF32(out var degradeLimit)
            || !reader.TryReadF64(out var lastTimeDegraded)
            || !reader.TryReadU32(out var statModType)
            || !reader.TryReadU32(out var statModKey)
            || !reader.TryReadF32(out var statModValue))
        {
            return false;
        }

        uint spellSetId = 0;
        if (hasSpellSetId != 0 && !reader.TryReadU32(out spellSetId))
        {
            return false;
        }

        enchantment = new EnchantmentDesc(spellId, layer, category, hasSpellSetId, powerLevel, startTime, duration, caster,
            degradeModifier, degradeLimit, lastTimeDegraded, statModType, statModKey, statModValue, spellSetId, bucket);
        return true;
    }

    // ---- The player module ----

    private static bool TryReadShortcuts(ref WireReader reader, out ImmutableArray<ShortcutDesc> shortcuts)
    {
        shortcuts = [];
        if (!reader.TryReadCount(12, out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<ShortcutDesc>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var index) || !reader.TryReadU32(out var objectId) || !reader.TryReadU32(out var spellId))
            {
                return false;
            }

            builder.Add(new ShortcutDesc(index, objectId, spellId));
        }

        shortcuts = builder.MoveToImmutable();
        return true;
    }

    /// <summary>A packable list of u32 (a favourite-spell bar): the count, then the words.</summary>
    private static bool TryReadWordList(ref WireReader reader, out ImmutableArray<uint> words)
    {
        words = [];
        if (!reader.TryReadCount(sizeof(uint), out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<uint>(count);
        for (var i = 0; i < count; i++)
        {
            reader.TryReadU32(out var word);  // the count was checked to fit
            builder.Add(word);
        }

        words = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadDesiredComponents(ref WireReader reader, out ImmutableArray<DesiredComponentDesc> components)
    {
        components = [];
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<DesiredComponentDesc>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var component) || !reader.TryReadI32(out var amount))
            {
                return false;
            }

            builder.Add(new DesiredComponentDesc(component, amount));
        }

        components = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadString(ref WireReader reader, out string? value)
    {
        var ok = reader.TryReadString16L(out var read);
        value = read;
        return ok;
    }

    /// <summary>The generic options data: a flags word, then int 0x1, bool 0x2, float 0x4 and string 0x8 tables.</summary>
    private static bool TryReadGenericQualities(ref WireReader reader, out GenericQualitiesDesc? data)
    {
        data = null;
        var ints = ImmutableDictionary<uint, int>.Empty;
        var bools = ImmutableDictionary<uint, bool>.Empty;
        var floats = ImmutableDictionary<uint, double>.Empty;
        var strings = ImmutableDictionary<uint, string>.Empty;
        if (!reader.TryReadU32(out var flags)
            || ((flags & 0x1) != 0 && !TryReadInts(ref reader, out ints))
            || ((flags & 0x2) != 0 && !TryReadBools(ref reader, out bools))
            || ((flags & 0x4) != 0 && !TryReadFloats(ref reader, out floats))
            || ((flags & 0x8) != 0 && !TryReadStrings(ref reader, out strings)))
        {
            return false;
        }

        data = new GenericQualitiesDesc(ints, bools, floats, strings);
        return true;
    }

    // ---- The tail ----

    private static bool TryReadContents(ref WireReader reader, out ImmutableArray<ContentDesc> contents)
    {
        contents = [];
        if (!reader.TryReadCount(8, out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<ContentDesc>(count);
        for (var i = 0; i < count; i++)
        {
            reader.TryReadU32(out var guid);  // the count was checked to fit
            reader.TryReadU32(out var containerType);
            builder.Add(new ContentDesc(guid, containerType));
        }

        contents = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadEquipped(ref WireReader reader, out ImmutableArray<EquippedDesc> equipped)
    {
        equipped = [];
        if (!reader.TryReadCount(12, out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<EquippedDesc>(count);
        for (var i = 0; i < count; i++)
        {
            reader.TryReadU32(out var guid);  // the count was checked to fit
            reader.TryReadU32(out var location);
            reader.TryReadU32(out var priority);
            builder.Add(new EquippedDesc(guid, location, priority));
        }

        equipped = builder.MoveToImmutable();
        return true;
    }
}

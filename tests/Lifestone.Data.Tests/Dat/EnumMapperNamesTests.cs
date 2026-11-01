using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Dat;

/// <summary>
/// Retail's enum-to-name maps (M5 7, D137): <see cref="EnumMapperNames"/> over synthetic mappers (the base-map fallback, a miss, a DID that is not an EnumMapper) and over the user's dats (the four maps an appraisal names: genders,
/// heritages, creature types and character title tokens, and a title's text in the language dat). Only a few short labels are asserted.
/// </summary>
public sealed class EnumMapperNamesTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static EnumMapper Mapper(uint baseMap, params (uint Key, string Name)[] names)
    {
        var m = new EnumMapper { BaseEnumMap = baseMap };
        foreach (var (key, name) in names)
        {
            m.IdToStringMap[key] = name;
        }

        return m;
    }

    [Fact]
    public void A_value_is_found_in_the_map_then_in_its_base_map()
    {
        var mappers = new Dictionary<uint, EnumMapper> { [0x22000010] = Mapper(0x22000011, (1, "One")), [0x22000011] = Mapper(0, (2, "Two")) };
        var names = new EnumMapperNames(e => e == 0x10000009 ? 0x22000010u : null, did => mappers.GetValueOrDefault(did));

        Assert.Equal("One", names.GetString(0x10000009, 1));
        Assert.Equal("Two", names.GetString(0x10000009, 2));
        Assert.Null(names.GetString(0x10000009, 3));
        Assert.Null(names.GetString(0x10000008, 1));
    }

    [Fact]
    public void A_base_map_cycle_ends_the_search()
    {
        var mappers = new Dictionary<uint, EnumMapper> { [0x22000010] = Mapper(0x22000011), [0x22000011] = Mapper(0x22000010) };
        var names = new EnumMapperNames(_ => 0x22000010u, did => mappers.GetValueOrDefault(did));
        Assert.False(names.TryGetString(1, 5, out _));
    }

    [Fact]
    public void A_map_that_is_not_an_enum_mapper_fails_loudly()
    {
        var names = new EnumMapperNames(_ => 0x25000004u, _ => null);
        var e = Assert.Throws<InvalidDataException>(() => names.GetString(1, 1));
        Assert.Contains("TODO(m5-enum-mapper-idmap)", e.Message, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_appraisal_maps_are_enum_mappers_holding_the_gender_heritage_creature_and_title_names()
    {
        var names = new EnumMapperNames(fixture.Dats);
        Assert.Equal("Male", names.GetString(EnumMapperNames.GenderMap, 1));
        Assert.Equal("Female", names.GetString(EnumMapperNames.GenderMap, 2));
        Assert.Equal("Aluvian", names.GetString(EnumMapperNames.HeritageMap, 1));
        Assert.Equal("Drudge", names.GetString(EnumMapperNames.CreatureTypeMap, 3));
        Assert.Equal("Phyntos_Wasp", names.GetString(EnumMapperNames.CreatureTypeMap, 9));
        Assert.Equal("ID_CharacterTitle_Adventurer", names.GetString(EnumMapperNames.CharacterTitleMap, 1));
        Assert.Null(names.GetString(EnumMapperNames.GenderMap, 99));
    }

    [RequiresLocalDatsFact]
    public void A_character_title_token_renders_from_the_title_string_table()
    {
        var names = new EnumMapperNames(fixture.Dats);
        var strings = new UiStrings(new UiStringTables(fixture.Dats));
        var token = names.GetString(EnumMapperNames.CharacterTitleMap, 1)!;
        Assert.Equal("Adventurer", strings.GetString(strings.Tables.Reference(0x10000007, token)));
    }
}

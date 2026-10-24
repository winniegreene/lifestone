using System.Numerics;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests.CharacterCreation;

/// <summary>The CharGen and SkillTable projections (m2-plan.md R8).</summary>
public sealed class CharGenDataTests(DatSetFixture fixture, Xunit.Abstractions.ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void CharGen_and_SkillTable_resolve_through_UNIQUEDB()
    {
        Assert.Equal("UNIQUEDB", Dats.EnumMaps.GroupName(CharGenData.UniqueDb));
        Assert.Equal(CharGenData.DataId, Dats.EnumMaps.Resolve(CharGenData.UniqueDb, CharGenData.UniqueDbEnum));
        Assert.Equal("CharGen_CharacterData", Dats.EnumMaps.NameOf(CharGenData.UniqueDb, CharGenData.UniqueDbEnum));
        Assert.Equal(SkillTableData.DataId, Dats.EnumMaps.Resolve(CharGenData.UniqueDb, SkillTableData.UniqueDbEnum));
        Assert.Equal("Weenie_SkillTable", Dats.EnumMaps.NameOf(CharGenData.UniqueDb, SkillTableData.UniqueDbEnum));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Thirteen_heritages_with_their_credits()
    {
        var data = CharGenData.Load(Dats);

        Assert.Equal(Enumerable.Range(1, 13).Select(i => (uint)i), data.Heritages.Select(h => h.Id));
        Assert.Equal(
            ["Aluvian", "Gharu'ndim", "Sho", "Viamontian", "Umbraen", "Gear", "Tumerok", "Lugian", "Empyrean", "Penumbraen", "Undead", "Olthoi", "OlthoiAcid"],
            data.Heritages.Select(h => h.Name));
        Assert.All(data.Heritages.Where(h => h.Id != 12), h => Assert.Equal((330u, 52u), (h.AttributeCredits, h.SkillCredits)));
        Assert.Equal((60u, 68u), (data.GetHeritage(12).AttributeCredits, data.GetHeritage(12).SkillCredits));
        Assert.All(data.Heritages, h => Assert.Equal([1u, 2u], h.Genders.Keys.Order()));
        Assert.All(data.Heritages.Where(h => h.Id < 12), h => Assert.Equal(7, h.Templates.Count));
        Assert.All(data.Heritages.Where(h => h.Id >= 12), h => Assert.Single(h.Templates));
        Assert.All(data.Heritages, h => Assert.Equal([new CharGenSkill(14, 0, 2)], h.Skills)); // Arcane Lore: free to train
        var error = Assert.Throws<InvalidDataException>(() => data.GetHeritage(14));
        Assert.Equal("TODO(retailui-data): CharGen 0x0E000002 has no heritage 14.", error.Message);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Five_start_areas_and_the_Academy_location_matches_protocol_md()
    {
        var data = CharGenData.Load(Dats);

        Assert.Equal(["Holtburg", "Shoushi", "Yaraq", "Sanamar", "OlthoiLair"], data.StartAreas.Select(a => a.Name));
        Assert.Equal([0, 1, 2, 3, 4], data.StartAreas.Select(a => a.Index));
        Assert.Equal([5, 5, 5, 5, 1], data.StartAreas.Select(a => a.Locations.Count));

        // protocol.md §10: every Academy entry is at (12.32, -28.482, 0.005) in a 0x01AD cell; Holtburg's first is 0x860201AD.
        var academy = data.StartAreas[0].Locations[0];
        Assert.Equal(0x860201ADu, academy.CellId);
        Assert.True(Vector3.Distance(new Vector3(12.32f, -28.482f, 0.005f), academy.Origin) < 0.001f);
        Assert.All(data.StartAreas.Take(4).SelectMany(a => a.Locations), l =>
        {
            Assert.Equal(0x01ADu, l.CellId & 0xFFFF);
            Assert.True(Vector3.Distance(academy.Origin, l.Origin) < 1e-5f);
        });

        // Every heritage's start areas are valid indices; the Olthoi start in the lair only.
        Assert.All(data.Heritages, h => Assert.All(h.PrimaryStartAreas.Concat(h.SecondaryStartAreas), i => Assert.InRange(i, 0, 4)));
        Assert.Equal([4], data.GetHeritage(12).PrimaryStartAreas);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_SkillTable_has_38_skills_and_55_advancement_classes()
    {
        var skills = SkillTableData.Load(Dats);

        Assert.Equal(38, skills.Skills.Count);
        Assert.Equal(55, skills.TotalNumSkills);
        Assert.Equal(54u, skills.Skills[^1].Id);
        Assert.True(skills.TryGetSkill(6, out var melee));
        Assert.Equal(("Melee Defense", 10, 20, 1u), (melee.Name, melee.TrainedCost, melee.SpecializedCost, melee.MinLevel));
        Assert.False(skills.TryGetSkill(1, out _)); // a retired skill
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_skill_CharGen_names_exists_and_every_chargen_skill_is_below_55()
    {
        var data = CharGenData.Load(Dats);
        var skills = SkillTableData.Load(Dats);

        // ACE requires exactly 55 SACs (ACE.Server/Factories/PlayerFactory.cs:166, ACEmulator/ACE@95ef426d).
        Assert.All(skills.Skills.Where(s => s.ChargenUse), s => Assert.InRange(s.Id, 0u, 54u));
        Assert.All(data.Heritages.SelectMany(h => h.Skills), s => Assert.True(skills.TryGetSkill(s.SkillId, out _), $"heritage skill {s.SkillId}"));
        Assert.All(
            data.Heritages.SelectMany(h => h.Templates).SelectMany(t => t.NormalSkills.Concat(t.PrimarySkills)),
            id => Assert.True(skills.TryGetSkill(id, out var s) && s.ChargenUse, $"template skill {id}"));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Skill_costs_take_the_heritages_then_the_SkillTables()
    {
        var data = CharGenData.Load(Dats);
        var skills = SkillTableData.Load(Dats);

        Assert.Equal((0, 2), (data.GetSkillTrainedCost(14, 1, skills), data.GetSkillSpecializedCost(14, 1, skills))); // heritage
        Assert.Equal((4, 6), (data.GetSkillTrainedCost(14, 0, skills), data.GetSkillSpecializedCost(14, 0, skills))); // no heritage
        Assert.Equal((10, 20), (data.GetSkillTrainedCost(6, 1, skills), data.GetSkillSpecializedCost(6, 1, skills)));
        Assert.Equal((-1, -1), (data.GetSkillTrainedCost(1, 1, skills), data.GetSkillSpecializedCost(1, 1, skills))); // not in the table
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Aluvian_mage_skill_costs_and_the_52_credit_plan_of_D14()
    {
        var data = CharGenData.Load(Dats);
        var skills = SkillTableData.Load(Dats);
        var aluvian = data.Heritages.Single(h => h.Name == "Aluvian").Id;
        Assert.Equal(1u, aluvian);

        // (skill id, name, trained cost, specialised cost) as ACCharGenData::GetSkillTrainedCost/GetSkillSpecializedCost give them.
        (uint Id, string Name, int Trained, int Specialised)[] expected =
        [
            (6, "Melee Defense", 10, 20),
            (14, "Arcane Lore", 0, 2), // the heritage's own entry; the SkillTable says 4/6
            (16, "Mana Conversion", 6, 12),
            (31, "Creature Enchantment", 8, 16),
            (32, "Item Enchantment", 8, 16),
            (33, "Life Magic", 12, 20),
            (34, "War Magic", 16, 28),
        ];
        foreach (var (id, name, trained, specialised) in expected)
        {
            output.WriteLine($"skill {id} {name}: trained {data.GetSkillTrainedCost(id, aluvian, skills)}, specialised {data.GetSkillSpecializedCost(id, aluvian, skills)}");
            Assert.True(skills.TryGetSkill(id, out var skill));
            Assert.Equal(name, skill.Name);
            Assert.Equal((trained, specialised), (data.GetSkillTrainedCost(id, aluvian, skills), data.GetSkillSpecializedCost(id, aluvian, skills)));
        }

        // D14 (spec 5.3) expected War Magic + Melee Defense specialised = 32 + 20 = 52 credits. The dat says 28 + 20 = 48:
        // it does NOT equal the heritage's 52 credits (4 are left over), so the four magic skills cannot be trained from
        // the remaining credits at creation either way; s3 trains them with the level credits.
        var specialisedBoth = data.GetSkillSpecializedCost(34, aluvian, skills) + data.GetSkillSpecializedCost(6, aluvian, skills);
        output.WriteLine($"War Magic + Melee Defense specialised = {specialisedBoth}; heritage skill credits = {data.GetHeritage(aluvian).SkillCredits}");
        Assert.Equal(48, specialisedBoth);
        Assert.NotEqual((int)data.GetHeritage(aluvian).SkillCredits, specialisedBoth);
        Assert.Equal(52u, data.GetHeritage(aluvian).SkillCredits);

        // The four later trainings (Creature, Item, Life Magic and Mana Conversion) cost 8 + 8 + 12 + 6.
        Assert.Equal(34, new uint[] { 31, 32, 33, 16 }.Sum(id => data.GetSkillTrainedCost(id, aluvian, skills)));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_gear_offers_colours_from_its_ClothingTable()
    {
        var data = CharGenData.Load(Dats);
        foreach (var heritage in data.Heritages)
        {
            foreach (var gender in heritage.Genders.Values)
            {
                Assert.NotEmpty(gender.Shirts);
                Assert.NotEmpty(gender.Pants);
                Assert.NotEmpty(gender.Footwear);
                foreach (var gear in gender.Headgears.Concat(gender.Shirts).Concat(gender.Pants).Concat(gender.Footwear))
                {
                    var colors = data.GetClothingColors(gear.ClothingTableId, gender);
                    Assert.True(colors.Count > 0, $"heritage {heritage.Id} gender {gender.Id} gear '{gear.Name}' 0x{gear.ClothingTableId:X8}");
                    Assert.All(colors, c => Assert.True(data.HasPaletteTemplate(gear.ClothingTableId, c.PaletteTemplate)));
                    Assert.All(colors, c => Assert.Contains(c.PaletteTemplate, gender.ClothingColors));
                    Assert.All(colors, c => Assert.NotEqual(0u, c.PalSetId));
                }
            }
        }

        Assert.Empty(data.GetClothingColors(0x10FFFFFFu, data.GetHeritage(1).Genders[1]));
        Assert.False(data.HasPaletteTemplate(0x10FFFFFFu, 1));
    }

    [Fact]
    public void Retail_walks_a_PackableHashTable_bucket_by_bucket_newest_first()
    {
        // Keys in dat order with table size 4: buckets 0: [8, 4], 1: [1, 5], 2: [6]; each bucket newest first.
        uint[] keys = [1, 8, 6, 5, 4];
        Assert.Equal([4u, 8u, 5u, 1u, 6u], CharGenData.PackableHashOrder(keys, k => k, 4));
        Assert.Empty(CharGenData.PackableHashOrder(keys, k => k, 0));
    }

    [Fact]
    public void SkillFormula_Calculate_rounds_half_up_and_needs_a_divisor()
    {
        var formula = new SkillTableFormula(0, 1, 1, 3, 4, 6); // (Coordination + Self) / 3, e.g. Melee Defense's shape
        Assert.True(formula.TryCalculate(10, 11, out var value));
        Assert.Equal(7u, value); // 21 / 3
        Assert.True(formula.TryCalculate(10, 12, out value));
        Assert.Equal(7u, value); // 22 / 3 = 7.33
        Assert.True(formula.TryCalculate(10, 13, out value));
        Assert.Equal(8u, value); // 23 / 3 = 7.67
        Assert.False(new SkillTableFormula(0, 1, 1, 0, 1, 2).TryCalculate(10, 10, out _));
    }
}

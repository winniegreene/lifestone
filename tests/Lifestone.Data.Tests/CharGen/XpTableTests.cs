using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.CharacterCreation;

/// <summary>
/// The XpTable (0x0E000018) pins M3 1.1 s3 plans the mage build from: the level cap, the experience to level 126, the
/// credits levels grant and the cumulative cost of a rank in each of the four rank lists.
/// </summary>
public sealed class XpTableTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    // Levels whose credit entry is 1 up to level 126 (all others are 0); level 1's entry is 0, so it makes no difference
    // whether level 1 counts. Beyond 126 the table also grants at 130, 140, 150, 160, 180, 200, 225, 250 and 275.
    private static readonly int[] CreditLevelsThrough126 =
    [
        2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20, 23, 26, 29, 32, 35,
        40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 105, 110, 115, 120, 125,
    ];

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Six_lists_with_their_lengths_and_the_level_cap()
    {
        var xp = XpTableData.Load(fixture.Dats);

        output.WriteLine(
            $"XpTable lengths: attribute={xp.AttributeXpList.Count} vital={xp.VitalXpList.Count} trained={xp.TrainedSkillXpList.Count} " +
            $"specialised={xp.SpecializedSkillXpList.Count} levelXp={xp.CharacterLevelXpList.Count} levelCredits={xp.CharacterLevelSkillCreditList.Count}");
        Assert.Equal(191, xp.AttributeXpList.Count);
        Assert.Equal(197, xp.VitalXpList.Count);
        Assert.Equal(209, xp.TrainedSkillXpList.Count);
        Assert.Equal(227, xp.SpecializedSkillXpList.Count);
        Assert.Equal(276, xp.CharacterLevelXpList.Count);
        Assert.Equal(276, xp.CharacterLevelSkillCreditList.Count);

        // The level cap is the highest index: 275 (index 0 is the origin, so there are 276 entries).
        output.WriteLine($"level cap = {xp.LevelCap}");
        Assert.Equal(275, xp.LevelCap);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_list_is_cumulative_from_zero()
    {
        var xp = XpTableData.Load(fixture.Dats);

        Assert.All(new[] { xp.AttributeXpList, xp.VitalXpList, xp.TrainedSkillXpList, xp.SpecializedSkillXpList }, list =>
        {
            Assert.Equal(0u, list[0]);
            for (var i = 1; i < list.Count; i++)
            {
                Assert.True(list[i] > list[i - 1], $"rank {i}");
            }
        });
        Assert.Equal(0ul, xp.CharacterLevelXpList[0]);
        Assert.Equal(0ul, xp.CharacterLevelXpList[1]); // level 1 costs nothing: index n is level n
        for (var i = 2; i < xp.CharacterLevelXpList.Count; i++)
        {
            Assert.True(xp.CharacterLevelXpList[i] > xp.CharacterLevelXpList[i - 1], $"level {i}");
        }

        Assert.Equal([0u, 110, 277, 501, 784, 1125], xp.AttributeXpList.Take(6));
        Assert.Equal([0u, 73, 183, 331, 517, 743], xp.VitalXpList.Take(6));
        Assert.Equal([0u, 58, 138, 243, 372, 526], xp.TrainedSkillXpList.Take(6));
        Assert.Equal([0u, 23, 56, 97, 149, 211], xp.SpecializedSkillXpList.Take(6));
        Assert.Equal([0ul, 0, 1000, 2777, 5697, 10248], xp.CharacterLevelXpList.Take(6));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Experience_to_level_126_and_the_credits_by_then()
    {
        var xp = XpTableData.Load(fixture.Dats);

        output.WriteLine($"XP to level 126 = {xp.XpForLevel(126)}; XP at the cap (275) = {xp.XpForLevel(275)}");
        Assert.Equal(4_286_609_098ul, xp.XpForLevel(126));
        Assert.Equal(4_125_476_914ul, xp.XpForLevel(125));
        Assert.Equal(191_226_310_247ul, xp.XpForLevel(275));
        Assert.Throws<ArgumentOutOfRangeException>(() => xp.XpForLevel(276));

        // Skill credits: the sum of the credit list through level 126 is 37 whether or not level 1's entry counts (it is 0).
        // The formula: one credit on each of levels 2-10, 12, 14, 16, 18, 20, 23, 26, 29, 32, 35, then every fifth level 40-125.
        Assert.Equal(0u, xp.CharacterLevelSkillCreditList[0]);
        Assert.Equal(0u, xp.CharacterLevelSkillCreditList[1]);
        var granting = Enumerable.Range(0, 127).Where(n => xp.CharacterLevelSkillCreditList[n] != 0).ToArray();
        Assert.Equal(CreditLevelsThrough126, granting);
        Assert.All(granting, n => Assert.Equal(1u, xp.CharacterLevelSkillCreditList[n]));
        output.WriteLine($"credits by level 126: counting level 1's entry = {xp.SkillCreditsThroughLevel(126)}, leaving it out = {xp.SkillCreditsThroughLevel(126, 1)}");
        Assert.Equal(37u, xp.SkillCreditsThroughLevel(126));
        Assert.Equal(37u, xp.SkillCreditsThroughLevel(126, 1));
        Assert.Equal(37u, (uint)CreditLevelsThrough126.Length);
        Assert.Equal(1u, xp.SkillCreditsThroughLevel(2, 1));
        Assert.Equal(0u, xp.SkillCreditsThroughLevel(1));
        Assert.Equal(9u, xp.SkillCreditsThroughLevel(10)); // levels 2-10
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Xp_to_raise_helpers_read_the_cumulative_lists()
    {
        var xp = XpTableData.Load(fixture.Dats);

        // A rank counts from the creation value, so an attribute created at 10 and raised to 100 has 90 ranks.
        output.WriteLine(
            $"attribute 10->100 = {xp.XpToRaiseAttribute(0, 90)}; 10->30 = {xp.XpToRaiseAttribute(0, 20)}; 100->190 = {xp.XpToRaiseAttribute(90, 180)}; " +
            $"vital +100 ranks = {xp.XpToRaiseVital(0, 100)}; trained skill +100 ranks = {xp.XpToRaiseTrainedSkill(0, 100)}; " +
            $"specialised skill +100 ranks = {xp.XpToRaiseSpecializedSkill(0, 100)}");
        Assert.Equal(110ul, xp.XpToRaiseAttribute(0, 1));
        Assert.Equal(14_015ul, xp.XpToRaiseAttribute(0, 20));
        Assert.Equal(1_556_972ul, xp.XpToRaiseAttribute(0, 90));
        Assert.Equal(xp.XpToRaiseAttribute(0, 180), xp.XpToRaiseAttribute(0, 90) + xp.XpToRaiseAttribute(90, 180));
        Assert.Equal(2_154_256ul, xp.XpToRaiseVital(0, 100));
        Assert.Equal(1_269_177ul, xp.XpToRaiseTrainedSkill(0, 100));
        Assert.Equal(507_671ul, xp.XpToRaiseSpecializedSkill(0, 100));
        Assert.True(xp.XpToRaiseSpecializedSkill(0, 100) < xp.XpToRaiseTrainedSkill(0, 100)); // specialised ranks are cheaper
        Assert.Equal(0ul, xp.XpToRaiseTrainedSkill(5, 5));

        // The last rank of each list, and the loud failures past it.
        Assert.Equal(4_203_819_496ul, xp.XpToRaiseTrainedSkill(0, 208));
        Assert.Equal(4_100_490_438ul, xp.XpToRaiseSpecializedSkill(0, 226));
        Assert.Throws<ArgumentOutOfRangeException>(() => xp.XpToRaiseTrainedSkill(0, 209));
        Assert.Throws<ArgumentOutOfRangeException>(() => xp.XpToRaiseAttribute(10, 9));
    }

    [Fact]
    public void Decode_refuses_a_wrong_id_and_a_trailing_byte()
    {
        var bytes = new byte[4 + 20 + 4 * 4 + 12 + 8 + 4 + 8 + 4 + 4];
        Assert.Throws<InvalidDataException>(() => XpTableData.Decode(bytes)); // id 0
        BitConverter.GetBytes(XpTableData.DataId).CopyTo(bytes, 0);
        var exact = bytes[..(4 + 20 + 4 + 4 + 4 + 4 + 8 + 4)];
        var table = XpTableData.Decode(exact);
        Assert.Equal(0, table.LevelCap);
        Assert.Throws<InvalidDataException>(() => XpTableData.Decode(bytes[..(exact.Length + 1)]));
        Assert.Throws<InvalidDataException>(() => XpTableData.Decode(bytes[..(exact.Length - 1)]));
    }
}

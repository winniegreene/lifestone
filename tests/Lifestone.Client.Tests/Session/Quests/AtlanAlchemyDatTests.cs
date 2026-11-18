using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 14 the Atlan wand (D136): the one roll of the chain, recipe 789 (two Pyreal Bars to the Quality Pyreal Ingot, Alchemy 38 against difficulty 150), on the dats. What these prove: Alchemy's formula is the SkillTable's
/// (Coordination and Focus over 3), the chance at each skill is ACE's logistic (<see cref="RecipeChance"/>), and the experience to raise a trained Alchemy to a chance is the XpTable's; the numbers are printed for the notes.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class AtlanAlchemyDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Difficulty = 150; // the recipe row 789's difficulty (the atlan-wand file's step and its source)

    [RequiresDatsFact]
    public void Alchemy_is_coordination_and_focus_over_three_and_the_chance_to_raise_it_is_the_xp_tables()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        Assert.True(skills.TryGetSkill(MageTemplate.Alchemy, out var alchemy));
        var f = alchemy!.Formula;
        Assert.Equal((4u, 5u, 3), (Math.Min(f.Attribute1, f.Attribute2), Math.Max(f.Attribute1, f.Attribute2), f.Divisor)); // Coordination 4, Focus 5
        output.WriteLine($"Alchemy: trained cost {alchemy.TrainedCost}, formula ({f.AdditiveBonus} + attr{f.Attribute1} x {f.Attribute1Multiplier} + attr{f.Attribute2} x {f.Attribute2Multiplier}) / {f.Divisor}");

        var table = XpTableData.Load(fixture.Dats);
        foreach (var (what, coordination, focus) in new[] { ("base (Coordination 105, Focus 233: fight-sim her, 2026-11-16)", 105u, 233u), ("buffed (Coordination 145, Focus 288)", 145u, 288u) })
        {
            Assert.True(f.TryCalculate(f.Attribute1 == 4 ? coordination : focus, f.Attribute1 == 4 ? focus : coordination, out var trained));
            output.WriteLine($"{what}: trained Alchemy {trained}, chance {RecipeChance.Of(trained, Difficulty):0.0000} against {Difficulty}");
            foreach (var want in new[] { 0.5, 0.75, 0.9, 0.95 })
            {
                var skill = RecipeChance.SkillFor(Difficulty, want);
                var ranks = (int)Math.Max(0, (int)skill - (int)trained);
                var xp = ranks < table.TrainedSkillXpList.Count ? table.XpToRaiseTrainedSkill(0, ranks).ToString("N0") : "past the table";
                output.WriteLine($"  chance {want:0.00}: skill {skill}, {ranks} ranks over trained, {xp} experience");
            }
        }

        output.WriteLine($"level 78 -> 79: {table.XpForLevel(79) - table.XpForLevel(78):N0} experience; 79 -> 80: {table.XpForLevel(80) - table.XpForLevel(79):N0}");
        Assert.Equal(0.5, RecipeChance.Of(Difficulty, Difficulty), 6);
    }
}

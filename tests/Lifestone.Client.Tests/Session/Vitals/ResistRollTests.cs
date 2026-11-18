using DatReaderWriter.Enums;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 16 E1e (docs/automation-engine.md stage E1; m6-plan.md D139): one function says whether a creature lets a spell of hers through (<see cref="ResistRoll"/>), and it is ACE's roll: <c>MagicDefenseCheck</c>,
/// WorldObject_Magic.cs:104-113 @95ef426d, <c>SkillCheck.GetSkillChance</c> at 0.03 with no case for a Magic Defense of 0. Before, the drain pick read 0 as unknown (an even 0.5) and the bolt's rate read it as untrained
/// (exactly 1). The numbers pinned are the simulator's (<c>tools/fight-sim.ps1 matchup</c>, 2026-11-17, a character with War and Life Magic 298): ACE's own objects.
/// </summary>
public sealed class ResistRollTests
{
    private const uint StrawDrudge = 2442; // no Magic Defense row, Focus 1 and Self 1: Magic Defense 0 (fight-sim creature 2442)
    private const uint GuardianGolem = 43247; // no row, Focus 850 and Self 890: Magic Defense 249
    private const uint GuardianSpirit = 44473; // no row, Focus 1000 and Self 1000: Magic Defense 286

    [Theory]
    [InlineData(StrawDrudge, 0, "100.0")] // "her WarMagic 298: a resistable spell of that school lands 100.0 % of the time"
    [InlineData(GuardianGolem, 249, "81.3")]
    [InlineData(GuardianSpirit, 286, "58.9")]
    public void A_spell_lands_on_a_kind_as_the_simulator_says_whether_its_weenie_states_a_Magic_Defense_row_or_not(uint wcid, int magicDefence, string percent)
    {
        var kind = Bestiary.Find(wcid);
        Assert.NotNull(kind);
        Assert.Equal(magicDefence, kind.Defence.MagicDefence); // a weenie with no row is untrained and usable: the formula's share alone (CreatureSkill.cs:48-63, 154-195)

        var lands = ResistRoll.LandChance(298, (uint)magicDefence);

        Assert.Equal(percent, (lands * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1.0 - (1.0 / (1.0 + Math.Exp(0.03f * (298 - magicDefence)))), lands, 12); // SkillCheck.cs:7-12, the float factor
    }

    [Fact]
    public void A_Magic_Defense_of_0_is_rolled_on_the_curve_never_an_even_half_and_never_a_plain_1()
    {
        Assert.Equal(0.99987, ResistRoll.LandChance(298, 0), 5);
        Assert.Equal(0.8176, ResistRoll.LandChance(50, 0), 4); // a weak caster's spell is resisted by a creature of Magic Defense 0 nearly one time in five
        Assert.Equal(0.5, ResistRoll.LandChance(0, 0), 12); // the even chance is the curve's, where the skill equals the defence
        Assert.InRange(ResistRoll.LandChance(362, 0), 0.9999, 0.99999);
    }

    [Fact]
    public void The_drain_pick_and_the_bolt_s_rate_ask_the_one_function()
    {
        foreach (var skill in new uint[] { 0, 1, 50, 150, 273, 298, 362, 430 })
        {
            foreach (var defence in new uint[] { 0, 1, 16, 94, 249, 286, 302, 362, 500 })
            {
                var one = ResistRoll.LandChance(skill, defence);
                Assert.Equal(one, DrainPicker.PassChance(skill, defence));
                Assert.Equal(one, KillRate.LandChance(skill, defence));
                Assert.Equal(one, ManaConversionModel.SkillChance(skill, defence)); // what the fight's bolt line and the approach cost ask already
                Assert.Equal(GearCasts.Chance(skill, 200) * one, LifeThroughRule.LandChance(skill, 200, defence));
            }
        }
    }

    [Fact]
    public void A_drain_at_a_target_of_Magic_Defense_0_is_priced_at_its_cast_chance_not_at_half_of_it()
    {
        // health 100 of 300, Life Magic 298; a target of 400 health and Magic Defense 0 at 3 m; Drain Health Other IV (1240: a quarter of its health, cap 100, no loss)
        var info = WorldActionRig.Spell(1240, SpellType.Transfer, SpellFlags.Resistable | SpellFlags.Reversed) with { Name = "Drain Health Other IV", School = MagicSchool.LifeMagic, Power = 150, BaseMana = 40, BaseRangeConstant = 33f, BaseRangeMod = 0f };
        var level = new DrainLevel(info, DrainPicker.RowOf(1240)!.Value, 1.0);
        var situation = new DrainSituation(100, 300, 400, 298, 298, 400, 0, 3);

        var choice = DrainPicker.Pick([level], [0u], in situation);

        Assert.Equal(0, choice.Index);
        Assert.Equal(100u, choice.HealIfLanded);
        Assert.Equal(GearCasts.Chance(298, 150) * ResistRoll.LandChance(298, 0), choice.Chance, 12);
        Assert.True(choice.Chance > 0.99, $"the chance was {choice.Chance}: before this slice a Magic Defense of 0 halved it");
        Assert.Equal(choice.Chance * 100, choice.ExpectedHeal, 9);
    }
}

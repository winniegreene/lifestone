using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AX: the Cave has a gate and fight data. A Crimsonback hits 1.833 times a Male Tusker (mean unarmoured 65, largest 104, critical 208), so the kit that makes it bearable is Armor Self V and Bludgeoning Protection V with the Lodge's lines (the numbers:
/// war-mage.json sources rung_gate, cache/m6/7/ax/out.txt). Proved: the gate's numbers, that a shut gate holds her on the Lodge past 20 (M6 14, D135: the Cave's band is 21-24), that the gate can open (the plan names both V scrolls on the rung that holds her, a gate that never opens would end the climb), and the fight data on both rungs.
/// </summary>
public sealed class CaveGateTests
{
    private static Ladder L => LadderLoader.WarMage;

    private static RungGate Gate => L.GateOf("cave")!;

    [Fact]
    public void The_Caves_gate_is_the_Lodges_lines_with_Armor_and_Protection_at_V_and_no_health_condition()
    {
        var lodge = L.GateOf("lodge")!;

        Assert.Equal(lodge.Skills, Gate.Skills);
        Assert.Equal(lodge.Lines.Select(l => l.Line), Gate.Lines.Select(l => l.Line));
        foreach (var line in new[] { "Armor Self", "Bludgeon Protection Self" })
        {
            var at = Gate.Lines.Single(l => l.Line == line);
            Assert.Equal((5, true), (at.Level, at.Highest));
        }

        Assert.Equal(3, Gate.Lines.Single(l => l.Line == "Impenetrability").Level);
        Assert.Equal(3, Gate.Lines.Single(l => l.Line == "Bludgeon Bane").Level);
        foreach (var line in Gate.Lines)
        {
            Assert.True(RungGateRule.LineOf(BuffProfiles.Climber, line.Line).Spells.Count >= line.Level, line.Line); // the climber's profile casts that level
        }
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(21, 20)] // shut: the Lodge, past its band
    [InlineData(23, 20)]
    [InlineData(24, 20)] // M6 14 (D135): the Cave's band runs to 24, so its gate holds her to there; past it the Abode's gate holds her in the Cave (AbodeGateTests)
    public void A_shut_Cave_gate_holds_her_on_the_Lodge_past_20(int level, int held)
    {
        var read = RungGateRule.Walk(L, level, gate => gate.Rung == "cave" ? [new GateCondition("Armor Self V up", false, "level 4 of 5")] : []);

        Assert.Equal(held, read);
        Assert.Equal("lodge", L.RungFor(read).Key);
    }

    [Fact]
    public void An_open_Cave_gate_lets_her_on()
    {
        Assert.Equal(21, RungGateRule.Walk(L, 21, _ => []));
        Assert.Equal("cave", L.RungFor(21).Key);
    }

    [Fact]
    public void The_gate_can_open_the_plan_on_the_rung_that_holds_her_names_both_V_scrolls_once_the_skill_reaches_them()
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 31, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 30, 100, 0);
        q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 172, 1, 2u, 0, 0u, 0, 0); // (131 + 130) / 4 + 172 ranks: over the 216 the 0.75 bar asks of power 200
        q.Skills[MageTemplate.CreatureEnchantment] = new SkillDesc(MageTemplate.CreatureEnchantment, 172, 1, 2u, 0, 0u, 0, 0);
        var book = new Spellbook { IsKnown = true };
        foreach (var name in new[] { "Armor Self", "Bludgeoning Protection Self" })
        {
            book.Learn(ScrivenerCatalog.Default.LineSpells(name).Single(s => s.Level == 4).SpellId);
        }

        var ground = GroundCatalog.Get("tusker-lodge");
        var needs = ScrivenerLearnPlan.Needs(ground, L.Rungs.Single(r => r.Key == "lodge"), L.PlanCastChance, q, book, ScrivenerCatalog.Default, L);

        foreach (var name in new[] { "Armor Self", "Bludgeoning Protection Self" })
        {
            var need = needs.Single(n => n.Line.Name == name);
            Assert.True(need.Target >= 5, $"{name}: the plan's target is {need.Target}: {need.Why}");
        }
    }

    [Theory]
    [InlineData("tusker-cave", 40, 80, 24)]
    [InlineData("tusker-temple-20-40", 40, 85, 25)]
    public void The_Cave_and_the_Temple_have_fight_data(string key, int retreat, int mana, int aggro)
    {
        var ground = GroundCatalog.Get(key);

        Assert.Equal(retreat, ground.FightRetreatHealthPercent);
        var rest = ground.PullRest!;
        Assert.Equal(mana, rest.ManaPercent);
        Assert.Equal(100, rest.FullPercent);
        Assert.Equal(70, rest.HealthPercent);
        Assert.Equal(90, rest.UntilPercent);
        Assert.Equal(RestPosture.Lie, rest.Posture);
        Assert.Equal(aggro, rest.AggroMetres);
    }
}

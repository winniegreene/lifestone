using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (the owner, 2026-11-13: the Temple 20-40 leaves the ladder): the Abode has a gate of the Cave's shape, so a climber whose lines are down is held in the Cave (gates chain, <see cref="RungGateRule.Walk"/>). D135 (the owner,
/// 2026-11-13: the study's bands): the Cave 21-24, the Abode 25-41, the Quarters 42-64 behind the Abode's gate, the Holding from 65 behind the buff bot's level VII Armor and Protection. Proved: the gate is the Cave's lines at the
/// Cave's levels; a shut Abode gate holds her at 24, on the Cave, at every level of the Abode's band, and a shut Cave gate under it drops her on to the Lodge unless she stands in the Cave; an open gate lets her on at 25; the gate can
/// open (the plan on the rung that holds her names both V scrolls); and the rungs past the Abode have their gates (closes <c>m6-gate-past-the-cave</c>).
/// </summary>
public sealed class AbodeGateTests
{
    private static Ladder L => LadderLoader.WarMage;

    private static RungGate Gate => L.GateOf("abode")!;

    private static readonly GateCondition Down = new("Armor Self V up", false, "level 4 of 5");

    [Fact]
    public void The_Abodes_gate_is_the_Caves_lines_at_the_Caves_levels()
    {
        var cave = L.GateOf("cave")!;

        Assert.Equal(cave.Skills, Gate.Skills);
        Assert.Equal(cave.Lines.Select(l => (l.Line, l.Level, l.Highest)), Gate.Lines.Select(l => (l.Line, l.Level, l.Highest)));
        Assert.Equal(["burrow", "lodge", "cave", "abode", "quarters", "holding"], L.Gates.Select(g => g.Rung)); // climbing order
        foreach (var line in Gate.Lines)
        {
            Assert.True(RungGateRule.LineOf(BuffProfiles.Climber, line.Line).Spells.Count >= line.Level, line.Line); // the climber's profile casts that level
        }

        Assert.Contains("the Abode has a gate of the Cave's shape", L.Sources["rung_gate"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(32)]
    [InlineData(41)]
    public void A_shut_Abode_gate_holds_her_on_the_Cave_at_24(int level)
    {
        var read = RungGateRule.Walk(L, level, gate => gate.Rung == "abode" ? [Down] : []);

        Assert.Equal(24, read);
        Assert.Equal("cave", L.RungFor(read).Key);
    }

    [Fact]
    public void Shut_Abode_and_Cave_gates_hold_her_on_the_Lodge_unless_she_stands_in_the_Cave()
    {
        Assert.Equal(20, RungGateRule.Walk(L, 32, gate => gate.Rung is "abode" or "cave" ? [Down] : []));
        Assert.Equal(24, RungGateRule.Walk(L, 32, gate => gate.Rung switch { "abode" => [Down], "cave" => null, _ => [] })); // on a gate's own ground that gate is not read again
    }

    [Fact]
    public void An_open_Abode_gate_lets_her_on_at_25()
    {
        Assert.Equal(25, RungGateRule.Walk(L, 25, _ => []));
        Assert.Equal("abode", L.RungFor(25).Key);
        Assert.Same(Gate, RungGateRule.GateAt(L, 25));
        Assert.Same(L.GateOf("cave"), RungGateRule.GateAt(L, 24));
    }

    [Theory]
    [InlineData(42, "quarters", 41)] // the Quarters' gate shut: held in the Abode
    [InlineData(64, "quarters", 41)]
    [InlineData(65, "holding", 64)] // the Holding's shut: held in the Quarters
    [InlineData(126, "holding", 64)]
    public void Past_the_Abode_each_rung_has_its_gate_and_a_shut_one_holds_her_on_the_rung_before_it(int level, string rung, int held)
    {
        Assert.Equal(rung, L.RungFor(level).Key);
        Assert.Same(L.GateOf(rung), RungGateRule.GateAt(L, level));
        Assert.Equal(held, RungGateRule.Walk(L, level, gate => gate.Rung == rung ? [Down] : []));
        Assert.Equal(level, RungGateRule.Walk(L, level, _ => []));
        Assert.DoesNotContain("does not hold her past 59: TODO(m6-gate-past-the-cave)", L.Sources["rung_gate"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_Quarters_gate_is_the_Abodes_and_the_Holdings_asks_the_buff_bots_level_VII_armour_lines()
    {
        var quarters = L.GateOf("quarters")!;
        var holding = L.GateOf("holding")!;

        Assert.Equal(Gate.Lines.Select(l => (l.Line, l.Level, l.Highest)), quarters.Lines.Select(l => (l.Line, l.Level, l.Highest)));
        Assert.Equal(Gate.Skills, holding.Skills);
        Assert.Equal(Gate.Lines.Select(l => l.Line), holding.Lines.Select(l => l.Line));
        Assert.All(holding.Lines.Where(l => l.Line is "Armor Self" or "Bludgeon Protection Self"), l => Assert.Equal((7, true), (l.Level, l.Highest)));
        Assert.All(holding.Lines.Where(l => l.Line is not ("Armor Self" or "Bludgeon Protection Self")), l => Assert.Equal(Gate.Lines.Single(a => a.Line == l.Line), l));

        // her own VI (the most she casts at 65: power 250 at the 0.75 bar asks Life 266) holds her; the bot's VII (Executor's Boon over Armor Self, Boon of the Mace Turner over the protection) opens it
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        Func<string, int, bool> book = (line, level) => level <= 6;
        var own = RungGateRule.Check(holding, q, book, _ => 6, _ => 6);
        Assert.Equal(["Armor Self VII learned", "Bludgeon Protection Self VII learned", "Armor Self up", "Bludgeon Protection Self up"], own.Where(c => !c.Met).Select(c => c.Name));
        Func<string, int, string?> bot = (line, level) => line is "Armor Self" or "Bludgeon Protection Self" && level <= 7 ? "stands by another's spell at level VII" : null;
        Assert.All(RungGateRule.Check(holding, q, book, name => name is "Armor Self" or "Bludgeon Protection Self" ? 7 : 6, _ => 6, bot), c => Assert.True(c.Met, c.Name));
        Assert.True(RungGateRule.Check(quarters, q, book, _ => 6, _ => 6).All(c => c.Met), "her own VI opens the Quarters' gate");
    }

    [Fact]
    public void The_gate_can_open_the_plan_on_the_Cave_names_both_V_scrolls_once_the_skill_reaches_them()
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

        var needs = ScrivenerLearnPlan.Needs(GroundCatalog.Get("tusker-cave"), L.Rungs.Single(r => r.Key == "cave"), L.PlanCastChance, q, book, ScrivenerCatalog.Default, L);

        foreach (var name in new[] { "Armor Self", "Bludgeoning Protection Self" })
        {
            var need = needs.Single(n => n.Line.Name == name);
            Assert.True(need.Target >= 5, $"{name}: the plan's target is {need.Target}: {need.Why}");
        }
    }
}

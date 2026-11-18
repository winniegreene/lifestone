using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 E3.0 (docs/automation-engine.md E3 and section 3, "shadow first"; m6-plan.md D119): the survival layer asked inside the fight, report-only (<c>FightScenario.SurvivalShadow.cs</c>). Proved over the scripted
/// caster of the other fight tests: with the ask set the fight casts what it casts without it, cast for cast and line for line; a heal both would make is an agreement and writes nothing; a move the layer would not make
/// (the bolt that kills the only attacker before a heal, the leave at the way out's line) is one line when it appears; and the fight's end writes the counts. That an ask allocates nothing is
/// <c>FightSurvivalShadowAllocationTests</c>'.
/// </summary>
public sealed partial class FightScenarioTests
{
    private static readonly LadderSurvival LayerLines = new(50, 80, 25, 60, 15, 40, 50, 15, 80, 50, 0); // the ladder's survival lines of 2026-11-17

    private static SurvivalAsk LayerAsk => static (in SurvivalInput asked) => SurvivalRule.Decide(in asked, LayerLines);

    private static List<string> SurvivalLines(FightManaTests.Rig r) => [.. r.Log.Lines.Where(l => l.Contains(FightScenario.SurvivalShadowToken, StringComparison.Ordinal))];

    private static bool IsShadowLine(string line) =>
        line.Contains(FightScenario.SurvivalShadowToken, StringComparison.Ordinal) || line.Contains(ThreatShadowLane.Token, StringComparison.Ordinal) || line.Contains(HealthShadow.Token, StringComparison.Ordinal);

    /// <summary>One scripted fight that passes every deciding site: bolts, a hit that takes her under the emergency line (the heal), then the mana at the way out's line (the refusal), the same with and without the ask.</summary>
    private static (List<uint> Spells, List<string> Lines, string Verdict, FightManaTests.Rig Rig, FightScenario Scenario) EverySite(bool asked)
    {
        var (r, scenario, spells, t) = Engaged(knowsDance: true, attackable: true);
        scenario.Survival = asked ? LayerAsk : null;
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.Run(scenario, 3, 0.1);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 1f);
        r.Caster.SetHealth(MaxHealthOf(r) * 40 / 100);
        Hit(r, scenario, 3);
        r.Run(scenario, 6, 0.1);
        r.Caster.SetHealth(MaxHealthOf(r));
        r.Caster.SetVitals(150, 200); // the mana loop's ground: the bolt is paid, the mana is missing, the stamina is there
        Hit(r, scenario, 3);
        r.Run(scenario, 6, 0.1);
        scenario.ArmWayOut(WayOut);
        r.Caster.SetVitals(70, 0); // at the way out's line: the bolt is refused
        Hit(r, scenario, 3);
        r.Run(scenario, 4, 0.1);
        var verdict = scenario.VerdictFields();
        scenario.Dispose();
        return (spells, [.. r.Log.Lines.Where(l => !IsShadowLine(l))], verdict, r, scenario);
    }

    [Fact]
    public void M6_16_E3_0_with_the_layer_asked_the_fight_casts_and_says_exactly_what_it_does_without_it()
    {
        var without = EverySite(asked: false);
        var with = EverySite(asked: true);

        Assert.Equal(without.Spells, with.Spells); // every cast, in order: the emergency's, the drain rule's, the mana loop's, the bolts
        Assert.Equal(without.Lines, with.Lines); // every line of the fight but the shadows' own
        Assert.Equal(without.Verdict, with.Verdict);
        Assert.DoesNotContain("survival_shadow", with.Verdict, StringComparison.Ordinal); // the verdict line keeps its pinned shape
        Assert.Empty(SurvivalLines(without.Rig)); // no ask, no line
        Assert.Equal((0L, 0L), (without.Scenario.SurvivalShadowAgree, without.Scenario.SurvivalShadowDiffer));
        Assert.True(with.Scenario.SurvivalShadowAgree + with.Scenario.SurvivalShadowDiffer > 0);
        foreach (var site in new[] { "emergency", "drain", "mana-loop", "way-out" })
        {
            var (agree, differ) = with.Scenario.SurvivalShadowOf(site);
            Assert.True(agree + differ > 0, $"the {site} site was asked: {string.Join(" | ", SurvivalLines(with.Rig))}");
        }
    }

    [Fact]
    public void M6_16_E3_0_a_heal_the_fight_and_the_layer_both_make_is_an_agreement_and_the_fights_end_writes_the_counts()
    {
        var (r, scenario, spells, t) = Engaged(attackable: true);
        scenario.Survival = LayerAsk;
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 1f);
        r.Caster.SetHealth(MaxHealthOf(r) * 40 / 100); // under the fight's emergency line (60%) and the ladder's line (50%)
        var from = spells.Count;
        Hit(r, scenario, 3);

        var first = FirstAfter(r, scenario, spells, from);

        Assert.Contains(first, DrainIds.Concat(Lifestone.Client.Session.Vitals.VitalsMeans.HealSelf)); // the fight's own emergency healed, as ever
        var (read, layer, input) = scenario.SurvivalShadowLast;
        Assert.True(read);
        Assert.True(input.InFight && input.Attackers.Count == 1 && input.Attackers[0].Guid == t[0], $"the board's one attacker, the tusker heard hitting her: {input.Attackers.Count} read ({string.Join(", ", input.Attackers.Select(a => $"{a.Name} 0x{a.Guid:X8} {a.Metres:0.#} m"))}); tusker 0x{t[0]:X8}; {string.Join(" | ", SurvivalLines(r))}");
        Assert.Equal(SurvivalVerdict.Heal, layer.Verdict);
        var (agree, differ) = scenario.SurvivalShadowOf("emergency");
        Assert.True(agree > 0, $"agree {agree} differ {differ}: {string.Join(" | ", SurvivalLines(r))}");
        Assert.Contains(SurvivalLines(r), l => l.Contains("survival-shadow: fight: the survival layer is asked beside the fight's emergency", StringComparison.Ordinal) && l.Contains("emergency floor 60%, safe 85%, margin 10%; drain under 50%, until 75%; way out not armed", StringComparison.Ordinal));
        Assert.DoesNotContain(SurvivalLines(r), l => l.Contains(": its asks: ", StringComparison.Ordinal)); // nothing before the end

        scenario.Dispose();

        var end = Assert.Single(SurvivalLines(r), l => l.Contains("survival-shadow: fight: its asks: ", StringComparison.Ordinal));
        var sites = string.Join(", ", new[] { "emergency", "drain", "mana-loop", "way-out" }.Select(s => $"{s} {scenario.SurvivalShadowOf(s).Agree}/{scenario.SurvivalShadowOf(s).Differ}"));
        Assert.Equal($"survival-shadow: fight: its asks: survival_shadow_agree={scenario.SurvivalShadowAgree} survival_shadow_differ={scenario.SurvivalShadowDiffer} (agreed/differed by site: {sites})", end[end.IndexOf("survival-shadow: ", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void M6_16_E3_0_the_bolt_that_kills_the_only_attacker_before_a_heal_is_a_difference_written_once()
    {
        var (r, scenario, spells, t) = Engaged(attackable: true);
        scenario.Survival = LayerAsk;
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.02f); // well under one bolt
        r.Caster.SetHealth(MaxHealthOf(r) * 40 / 100);
        var from = spells.Count;
        Hit(r, scenario, 3, 0);

        var first = FirstAfter(r, scenario, spells, from);

        Assert.True(FightManaTests.Rig.IsBolt(first), $"the fight's own choice stands: the bolt, not spell {first}");
        Assert.Equal(1, scenario.EmergencyFinishes);
        var line = Assert.Single(SurvivalLines(r), l => l.Contains("survival-shadow: emergency: the fight goes on (the bolt that kills the only attacker, Tusker Slave 0x", StringComparison.Ordinal));
        Assert.Contains(", the layer says heal ", line, StringComparison.Ordinal);
        Assert.Contains("about to die: under the ladder's line and the fight's emergency line", line, StringComparison.Ordinal);
        Assert.Contains("health 40%", line, StringComparison.Ordinal);
        Assert.True(scenario.SurvivalShadowOf("emergency").Differ > 0);
    }

    [Fact]
    public void M6_16_E3_0_the_leave_at_the_way_outs_line_is_a_difference_the_layer_does_not_make_written_once_while_it_stands()
    {
        var (r, scenario, _, t) = AtTheLine(mana: 70, stamina: 0, attackable: true); // the way out's line is 60 (Lifestone Recall 50 + Drain Health Other I 10): a 70 mana bolt would leave her under it
        scenario.Survival = LayerAsk;

        r.Run(scenario, 4, 0.1, () => scenario.WayOutRefused);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 1f);
        r.Run(scenario, 3, 0.1);

        Assert.True(scenario.WayOutRefused); // the fight's own gate, as ever
        var line = Assert.Single(SurvivalLines(r), l => l.Contains("survival-shadow: way-out: the fight asks to leave (its spend is refused for the way out), the layer says go (", StringComparison.Ordinal));
        Assert.True(line.Contains("mana 16% (70)", StringComparison.Ordinal) && line.Contains("the mana that finishes them ", StringComparison.Ordinal), line); // her mana is over the ladder's leave line of 15%, and the layer counts the bolt at its expected mana where the way out charges its most: the pairs the slice's report lists
        Assert.True(scenario.SurvivalShadowOf("way-out").Differ > 1, "asked each decision, written once");
        Assert.Contains(SurvivalLines(r), l => l.Contains("way out line=60 (Lifestone Recall 50 + Drain Health Other I 10)", StringComparison.Ordinal));
    }
    [Fact]
    public void M6_16_E3_0_an_ask_that_throws_is_named_once_and_taken_away_and_the_fight_goes_on_as_with_no_ask()
    {
        var (r, scenario, spells, t) = Engaged(attackable: true);
        var asks = 0;
        scenario.Survival = (in SurvivalInput _) => ++asks > 3 ? throw new InvalidOperationException("the test's fault") : default;
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 1f);
        r.Caster.SetHealth(MaxHealthOf(r) * 40 / 100);
        var from = spells.Count;
        Hit(r, scenario, 3);

        var first = FirstAfter(r, scenario, spells, from);
        r.Run(scenario, 3, 0.1);

        Assert.Contains(first, DrainIds.Concat(Lifestone.Client.Session.Vitals.VitalsMeans.HealSelf)); // the fight's own emergency healed all the same
        Assert.Null(scenario.Survival);
        Assert.Equal(4, asks); // asked no more after the fault
        var line = Assert.Single(r.Log.Lines, l => l.Contains("TODO(survival-shadow-failed)", StringComparison.Ordinal));
        Assert.Contains("InvalidOperationException: the test's fault", line, StringComparison.Ordinal);
        Assert.Contains("the fight decides as ever", line, StringComparison.Ordinal);
    }
}

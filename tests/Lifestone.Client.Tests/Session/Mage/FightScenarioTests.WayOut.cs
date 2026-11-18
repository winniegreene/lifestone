using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 BE: the fight's half of the way out, over the scripted caster: the bolt, the emergency's heal and the buff line refused for the line, the kill of the last attacker and the press that go anyway, and a fight with no line armed as it always was.
/// </summary>
public sealed partial class FightScenarioTests
{
    private static readonly WayOutLine WayOut = ManaWayOut.LineOf(50, 10);

    private static uint[] DrainIds => [.. ScriptedCaster.DrainLine.Spells.Select(s => s.Id)];

    /// <summary>
    /// The fight under way, an attacker on her at full health (the bolts modelled against it are put right: the rig's bolt is a level VII at 70 mana, so the rule's bolt is refused under 130), the mana and the stamina as the case wants them and nothing
    /// coming back by itself.
    /// </summary>
    private static (FightManaTests.Rig Rig, FightScenario Scenario, List<uint> Spells, uint[] Tuskers) AtTheLine(uint mana, uint stamina, bool armed = true, bool knowsDance = false, uint manaPotions = 0, bool attackable = false)
    {
        var (r, scenario, spells, t) = Engaged(knowsDance: knowsDance, manaPotions: manaPotions, attackable: attackable);
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.Run(scenario, 3, 0.1); // the opening bolt is out of the way
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 1f);
        r.Caster.SetVitals(mana, stamina);
        scenario.ArmWayOut(armed ? WayOut : null);
        return (r, scenario, spells, t);
    }

    [Fact]
    public void A_bolt_that_would_take_the_mana_under_the_line_is_refused_with_an_attacker_on_her_and_says_she_leaves_M6_7_BE()
    {
        var (r, scenario, spells, t) = AtTheLine(mana: 70, stamina: 0);
        var from = spells.Count;

        r.Run(scenario, 4, 0.1, () => scenario.WayOutRefused);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 1f);
        r.Run(scenario, 3, 0.1);

        Assert.True(scenario.WayOutRefused);
        Assert.DoesNotContain(spells.Skip(from), FightManaTests.Rig.IsBolt);
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the bolt is refused: mana 70 of ", StringComparison.Ordinal) && l.Contains("line=60 (Lifestone Recall 50 + Drain Health Other I 10)", StringComparison.Ordinal));
    }

    [Fact]
    public void With_the_way_out_not_armed_the_same_bolt_goes_as_it_always_did_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 129, stamina: 275, armed: false);
        var from = spells.Count;

        Assert.True(FightManaTests.Rig.IsBolt(FirstAfter(r, scenario, spells, from)));
        Assert.False(scenario.WayOutRefused);
    }

    [Fact]
    public void With_no_attacker_on_her_the_line_has_no_effect_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 129, stamina: 200);
        var from = spells.Count;
        r.Run(scenario, 4, 0.1, () => scenario.WayOutRefused);
        Assert.True(scenario.WayOutRefused);
        Assert.DoesNotContain(spells.Skip(from), FightManaTests.Rig.IsBolt);

        r.Run(scenario, RebuffDriver.FightQuietSeconds + 8, 0.1, () => spells.Skip(from).Any(FightManaTests.Rig.IsBolt)); // the attack's seconds run out

        Assert.Contains(spells.Skip(from), FightManaTests.Rig.IsBolt);
        Assert.False(scenario.WayOutRefused);
    }

    [Fact]
    public void The_bolt_that_kills_the_last_attacker_goes_under_the_line_M6_7_BE()
    {
        var (r, scenario, spells, t) = AtTheLine(mana: 70, stamina: 275);
        TuskerHealth(r, t[0], 0.02f);
        Assert.True(scenario.WayOutKillPending(r.View, r.Now));
        var from = spells.Count;

        Assert.True(FightManaTests.Rig.IsBolt(FirstAfter(r, scenario, spells, from)));
        Assert.False(scenario.WayOutRefused);
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the bolt goes: mana 70 of ", StringComparison.Ordinal) && l.Contains("kill-of-last-attacker", StringComparison.Ordinal));
    }

    [Fact]
    public void With_potions_held_even_the_bolt_that_kills_the_last_attacker_waits_for_the_reserve_M6_8()
    {
        // The climb's sixth death (2026-11-11 21:07 UTC): in a stream of tuskers nearly every bolt is the one that kills the last attacker; each went under the line, the mana sat at 1 to 43, and the recall could not be paid.
        var (r, scenario, spells, t) = AtTheLine(mana: 70, stamina: 0, manaPotions: 10);
        TuskerHealth(r, t[0], 0.02f);
        Assert.True(scenario.WayOutKillPending(r.View, r.Now));
        var from = spells.Count;
        var drunkBefore = r.Caster.PotionsDrunk;

        for (var i = 0; i < 70 && !spells.Skip(from).Any(FightManaTests.Rig.IsBolt); i++)
        {
            Hit(r, scenario, 1);
            TuskerHealth(r, t[0], 0.02f);
        }

        Assert.Contains(spells.Skip(from), FightManaTests.Rig.IsBolt); // the kill comes, a few seconds later
        Assert.InRange(r.Caster.PotionsDrunk - drunkBefore, 2, 3); // 70 to 130 first
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the reserve: mana 70 of ", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("way out: the bolt goes: mana 70 of ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_retreat_whose_recall_is_not_paid_for_waits_while_the_potions_pay_it_M6_8()
    {
        // The sixth death: the retreat was taken at mana 1 with 104 potions held; the way home's need failed and she was killed standing. The rig's recall costs 50 (the line 60).
        var (r, scenario, spells, _) = AtTheLine(mana: 5, stamina: 0, manaPotions: 10);
        var drunkBefore = r.Caster.PotionsDrunk;

        Assert.True(scenario.DrinkForRecall(r.View)); // the autopilot asks before it stops the fight for the retreat
        var from = spells.Count;
        for (var i = 0; i < 70 && scenario.DrinkForRecall(r.View); i++)
        {
            Hit(r, scenario, 1);
        }

        Assert.False(scenario.DrinkForRecall(r.View)); // paid: the retreat is taken now
        Assert.True(r.Mana >= 50, $"mana {r.Mana}");
        Assert.InRange(r.Caster.PotionsDrunk - drunkBefore, 2, 3); // 5 to 50 at 25 a potion, and nothing else spent it
        Assert.DoesNotContain(spells.Skip(from), FightManaTests.Rig.IsBolt); // no bolt came before the recall was paid
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the retreat waits for the recall's mana: mana 5 of ", StringComparison.Ordinal) && l.Contains("(m6-recall-drinks)", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the reserve: mana 5 of ", StringComparison.Ordinal) && l.Contains("the recall, before the retreat", StringComparison.Ordinal));

        var (bare, none, _, _) = AtTheLine(mana: 5, stamina: 0);
        Assert.False(none.DrinkForRecall(bare.View)); // no potion held: the retreat is taken as before
    }

    [Fact]
    public void Under_the_reserve_a_press_that_nets_its_worth_comes_before_the_potion_M6_8()
    {
        // The owner, 2026-11-11: the Stamina to Mana press is the best return there is; it is pressed (until it lands) before a potion is drunk.
        var (r, scenario, spells, _) = AtTheLine(mana: 90, stamina: 275, knowsDance: true, manaPotions: 10);
        var from = spells.Count;
        var drunkBefore = r.Caster.PotionsDrunk;

        for (var i = 0; i < 70 && !r.Log.Lines.Any(l => l.Contains("way out: the reserve: mana 90 of ", StringComparison.Ordinal)); i++)
        {
            Hit(r, scenario, 1);
        }

        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the reserve: mana 90 of ", StringComparison.Ordinal) && l.Contains("the press first: stamina 275 of ", StringComparison.Ordinal) && l.Contains("the potions wait (m6-mana-reserve)", StringComparison.Ordinal));
        Assert.Equal(drunkBefore, r.Caster.PotionsDrunk); // no potion before the press
        Assert.DoesNotContain(spells.Skip(from), FightManaTests.Rig.IsBolt); // and no bolt under the reserve
    }

    [Fact]
    public void With_an_attacker_on_her_and_nothing_started_for_two_seconds_the_fight_says_what_it_waits_for_M6_8()
    {
        // Live 2026-11-11 19:37:34 UTC: eight seconds with a tusker on her and not one line of the log. No line armed, no mana, no potion, no stamina: the mana order waits, which is the stillness the line names.
        var (r, scenario, _, _) = AtTheLine(mana: 0, stamina: 0, armed: false);
        for (var i = 0; i < 12; i++)
        {
            Hit(r, scenario, 1, 0.5); // six seconds of attacks, nothing she can do
        }

        Assert.True(scenario.IdleLines >= 1, $"idle lines {scenario.IdleLines}");
        var said = r.Log.Lines.First(l => l.Contains("(m6-fight-idle)", StringComparison.Ordinal));
        Assert.Contains("idle: an attacker is on her and nothing has started for ", said, StringComparison.Ordinal);
        Assert.Contains(" phase=Mana mana_phase=Waiting ", said, StringComparison.Ordinal);
        Assert.Contains(" mana 0/", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_creature_beside_her_is_no_last_attacker_and_the_bolt_stays_refused_M6_7_BE()
    {
        var (r, scenario, spells, t) = AtTheLine(mana: 70, stamina: 0);
        r.AddTusker(22); // 2 m from her
        TuskerHealth(r, t[0], 0.02f);
        var from = spells.Count;

        r.Run(scenario, 4, 0.1, () => scenario.WayOutRefused);

        Assert.True(scenario.WayOutRefused);
        Assert.DoesNotContain(spells.Skip(from), FightManaTests.Rig.IsBolt);
    }

    [Fact]
    public void A_press_that_nets_its_worth_and_leaves_the_recall_paid_goes_in_the_bolts_place_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 125, stamina: 200, knowsDance: true);
        var from = spells.Count;

        Assert.True(scenario.WayOutPress(r.View, r.Now));
        var first = FirstAfter(r, scenario, spells, from);

        Assert.Contains(first, VitalsMeans.StaminaToManaSelf);
        Assert.False(scenario.WayOutRefused);
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: a press before the line: mana 125 of ", StringComparison.Ordinal) && l.Contains("leaves the recall paid", StringComparison.Ordinal));
    }

    [Fact]
    public void No_press_is_sent_that_would_leave_the_recall_unpaid_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 52, stamina: 200, knowsDance: true);
        var from = spells.Count;

        Assert.False(scenario.WayOutPress(r.View, r.Now));
        r.Run(scenario, 3, 0.1, () => scenario.WayOutRefused);

        Assert.True(scenario.WayOutRefused);
        Assert.Empty(spells.Skip(from));
    }

    [Fact]
    public void The_emergency_heal_is_refused_when_it_would_leave_the_recall_unpaid_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 52, stamina: 0);
        var max = MaxHealthOf(r);
        r.Caster.SetHealth(max * 40 / 100); // under the emergency line, far from the next hit
        var from = spells.Count;

        Hit(r, scenario, 3);
        r.Run(scenario, 3, 0.1, () => scenario.WayOutRefused);

        Assert.True(scenario.WayOutRefused);
        Assert.Empty(spells.Skip(from));
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the heal is refused: mana 52", StringComparison.Ordinal));
    }

    [Fact]
    public void The_emergency_heal_goes_into_the_line_while_the_recall_stays_paid_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 65, stamina: 0);
        var max = MaxHealthOf(r);
        r.Caster.SetHealth(max * 40 / 100);
        var from = spells.Count;
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, Slave, 4, 0.1, 3, 4, false, 0));

        var first = FirstAfter(r, scenario, spells, from);

        Assert.Contains(first, DrainIds.Concat(VitalsMeans.HealSelf));
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the heal goes: mana 65, ", StringComparison.Ordinal) && l.Contains("drain-recall-paid", StringComparison.Ordinal));
    }

    [Fact]
    public void The_emergency_heal_goes_past_the_recall_when_the_next_hit_kills_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 52, stamina: 0);
        var health = Math.Max(4, MaxHealthOf(r) / 20);
        Hit(r, scenario, health * 2);
        r.Caster.SetHealth(health);
        var from = spells.Count;
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, Slave, 4, 0.1, 1, 4, false, 0));

        var first = FirstAfter(r, scenario, spells, from);

        Assert.Contains(first, DrainIds.Concat(VitalsMeans.HealSelf));
        Assert.Contains(r.Log.Lines, l => l.Contains("drain-next-hit-kills", StringComparison.Ordinal));
    }

    [Fact]
    public void A_revitalize_between_the_bolts_keeps_the_line_M6_7_BE()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 135, stamina: 200); // the bolt is paid and stays over the line; a Revitalize Self on top of it would not
        var from = spells.Count;

        Assert.True(FightManaTests.Rig.IsBolt(FirstAfter(r, scenario, spells, from)));
    }

    [Fact]
    public void A_buff_line_under_the_hard_floor_waits_when_it_would_take_the_mana_under_the_line_with_a_hostile_near_M6_7_BE()
    {
        var r = new FightManaTests.Rig(mana: 90, stamina: 0, knowsDrain: true, knowsRevitalize: false, knowsDance: false) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 0;
        r.AddTusker(12); // within the refresh's reach
        FightF24Tests.Prime(r, _ => 10); // every line under the 30 s hard floor
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");
        scenario.ArmWayOut(WayOut);

        r.Run(scenario, 2, 0.1);

        Assert.DoesNotContain(r.Caster.Answered, a => ArenaProfile.Lines.Any(l => l.IsBuff && l.Spells.Any(s => s.Id == a.Spell)));
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: ", StringComparison.Ordinal) && l.Contains(" waits: mana 90, up to ", StringComparison.Ordinal) && l.Contains("(buff-refused)", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_14_upkeep_mana_a_recast_held_for_the_way_out_says_why_once_with_its_numbers_and_its_lapse_names_the_hold()
    {
        // Live 2026-11-14 01:52 to 02:15 UTC: every recast was refused for the way out's mana and only a field was set; the lapse's error line then named the cast 45 minutes before ("the pass cast it").
        var r = new FightManaTests.Rig(mana: 90, stamina: 0, knowsDrain: true, knowsRevitalize: false, knowsDance: false) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 0;
        r.AddTusker(12);
        FightF24Tests.Prime(r, l => l.Name == "Quickness Self" ? 6 : 1500); // under the hard floor, and it runs out in 6 s
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");
        scenario.ArmWayOut(WayOut);

        r.Run(scenario, 12, 0.1);

        Assert.DoesNotContain(r.Caster.Answered, a => ArenaProfile.Lines.Any(l => l.IsBuff && l.Spells.Any(s => s.Id == a.Spell)));
        var held = Assert.Single(r.Log.Lines, l => l.Contains("buff refresh: Quickness Self waits for mana: 90 of the ", StringComparison.Ordinal));
        Assert.Contains(" the way out needs (cast up to ", held, StringComparison.Ordinal);
        Assert.Contains(" + reserve 60)", held, StringComparison.Ordinal);
        Assert.Contains("the vitals policy is not hers", held, StringComparison.Ordinal); // this rig's mage is not the climber: nothing fills it, and the line says so
        Assert.Equal(1, scenario.BuffsExpired); // counted although the fight was in its mana wait when it ran out (the bolts took the mana to 3): the expiry is read in every phase
        var expired = Assert.Single(r.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Error && e.Line.Contains("buff refresh: Quickness Self EXPIRED under fire: ", StringComparison.Ordinal)).Line;
        Assert.Contains("its recast was held, last ", expired, StringComparison.Ordinal);
        Assert.Contains("Quickness Self waits for mana: ", expired, StringComparison.Ordinal); // the hold's latest numbers
        Assert.Contains(" the way out needs (cast up to ", expired, StringComparison.Ordinal);
        Assert.DoesNotContain("the pass cast it", expired, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_buff_line_goes_with_the_way_out_not_armed_M6_7_BE()
    {
        var r = new FightManaTests.Rig(mana: 90, stamina: 0, knowsDrain: true, knowsRevitalize: false, knowsDance: false) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 0;
        r.AddTusker(12);
        FightF24Tests.Prime(r, _ => 10);
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");

        r.Run(scenario, 2, 0.1);

        Assert.Contains(r.Caster.Answered, a => ArenaProfile.Lines.Any(l => l.IsBuff && l.Spells.Any(s => s.Id == a.Spell)));
    }

    [Fact]
    public void With_Mana_Potions_held_she_drinks_in_the_refused_bolt_s_place_and_does_not_leave_M6_8()
    {
        // Live 2026-11-11 15:13 UTC: mana 78 at the line 60 with two tuskers on her and dozens of potions in the pack: she left for home. The rig's bolt costs 70 and the line is 60: the bolt goes at 130, so from 70 the potions
        // are drunk until it does. (For three hours of 2026-11-11 the bolt went under the line while potions were held; the fourth death, 19:38 UTC, came of a mana of 4 to 36 with no heal and no recall to pay.)
        var (r, scenario, spells, t) = AtTheLine(mana: 70, stamina: 0, manaPotions: 10);
        var from = spells.Count;
        var drunkBefore = r.Caster.PotionsDrunk;

        for (var i = 0; i < 70 && !spells.Skip(from).Any(FightManaTests.Rig.IsBolt); i++)
        {
            Hit(r, scenario, 1); // the attacker stays on her
        }

        Assert.Contains(spells.Skip(from), FightManaTests.Rig.IsBolt); // the bolt came once the potions covered it over the line
        Assert.False(scenario.WayOutRefused);
        Assert.InRange(r.Caster.PotionsDrunk - drunkBefore, 2, 3); // 70 to 130 at 25 a potion (the rig's own regeneration may save the third)
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the reserve: mana 70 of ", StringComparison.Ordinal) && l.Contains("is under 130 (the line and one bolt", StringComparison.Ordinal) && l.Contains("(m6-mana-reserve)", StringComparison.Ordinal));
        Assert.True(scenario.ReservePotions >= 2, $"reserve potions {scenario.ReservePotions}"); // each drink asked (a busy one is asked again)
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("way out: the bolt goes under the line", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("way out: the bolt is refused", StringComparison.Ordinal));
        Assert.True(scenario.WayOutPotion(r.View));
        _ = t;
    }

    [Fact]
    public void A_busy_drink_at_the_line_is_tried_again_and_the_potions_stay_in_use_M6_8()
    {
        var (r, scenario, spells, _) = AtTheLine(mana: 70, stamina: 0, manaPotions: 10);
        r.Caster.BusyDrinks = 2;
        var from = spells.Count;

        for (var i = 0; i < 90 && !spells.Skip(from).Any(FightManaTests.Rig.IsBolt); i++)
        {
            Hit(r, scenario, 1);
        }

        Assert.Contains(spells.Skip(from), FightManaTests.Rig.IsBolt);
        Assert.Equal(2, r.Log.Lines.Count(l => l.Contains("mana: the server was too busy for the Mana Potion", StringComparison.Ordinal)));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("Mana Potions are set aside", StringComparison.Ordinal));
        Assert.False(scenario.WayOutRefused);
    }

    [Fact]
    public void With_no_potion_held_the_refusal_and_the_leaving_stand_M6_8()
    {
        var (r, scenario, _, _) = AtTheLine(mana: 70, stamina: 0);
        r.Run(scenario, 4, 0.1, () => scenario.WayOutRefused);

        Assert.True(scenario.WayOutRefused);
        Assert.False(scenario.WayOutPotion(r.View));
        Assert.Contains(r.Log.Lines, l => l.Contains("no press that pays and no Mana Potion in use; she leaves", StringComparison.Ordinal));
    }

    [Fact]
    public void With_potions_held_a_bolt_that_would_take_the_mana_under_the_line_waits_for_a_potion_M6_8()
    {
        // The climb's fourth death (2026-11-11 19:38 UTC): the bolt went under the line while potions were held, and she fought at mana 4 to 36 with no heal and no recall to pay. The rig's bolt costs 70 and the line is 60: at 90
        // the bolt would leave 20, so a potion is drunk first; every bolt's mana comes from a potion either way, and the reserve costs none.
        var (r, scenario, spells, _) = AtTheLine(mana: 90, stamina: 0, manaPotions: 10);
        var from = spells.Count;
        var drunkBefore = r.Caster.PotionsDrunk;

        for (var i = 0; i < 70 && !spells.Skip(from).Any(FightManaTests.Rig.IsBolt); i++)
        {
            Hit(r, scenario, 1); // the attacker stays on her
        }

        Assert.Contains(spells.Skip(from), FightManaTests.Rig.IsBolt);
        Assert.InRange(r.Caster.PotionsDrunk - drunkBefore, 1, 3); // 90 to 130, and what the emergency spends while the attacker hits
        Assert.False(scenario.WayOutRefused);
        Assert.Contains(r.Log.Lines, l => l.Contains("way out: the reserve: mana 90 of ", StringComparison.Ordinal));
        Assert.True(scenario.ReservePotions >= 1);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("way out: the bolt goes under the line", StringComparison.Ordinal));
    }
}

using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Vitals;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// The vitals keeper's policy on the scripted rig (M4 P5a lane C2, <see cref="C2VitalsRig"/>): each branch (kit, Heal Self or potion by the character's means; the rest to its level and its cap; the posture sent and
/// released; Stamina to Mana before a cast; the attack refusal; the in-fight potion) and the verdict line's shape. Every number below is the rig's: maxima 200, Healing 166, Life Magic 200.
/// </summary>
public sealed partial class VitalsKeeperTests
{
    private const uint HealSelfIII = 1158;
    private const uint RevitalizeIII = 1179;
    private const uint StaminaToManaIII = 1678;

    private static readonly uint[] HealSelfToIII = [6, 1157, HealSelfIII];

    [GeneratedRegex(@"^vitals_min=(\d+|-)/(\d+|-)/(\d+|-) heals=\d+ kits=\d+ potions=\d+ rests=\d+ rest_s=\d+ transfers=\d+ mana_potions=\d+ stamina_potions=\d+( mana_tries=\d+ mana_refusals=\d+ mana_landed=\d+ busy=\d+ resists=\d+ proved=\d+)?( mana_presses=\d+ dances=\d+ dances_landed=\d+)?$")] // M5 1 K5: the cast policy's fields once the keeper cast, the dance's where one ran
    private static partial Regex VerdictShape();

    [Fact]
    public void Needs_already_met_are_Ready_at_once_and_send_nothing()
    {
        var rig = new C2VitalsRig(new C2Character { KitUses = 5, Potions = 5 });

        Assert.True(rig.Keeper.NeedsFor(new VitalsNeeds()));
        Assert.Equal(VitalsKeeperState.Ready, rig.Frame());
        Assert.Empty(rig.Used);
        Assert.Equal("vitals_min=100/100/100 heals=0 kits=0 potions=0 rests=0 rest_s=0 transfers=0 mana_potions=0 stamina_potions=0", rig.Keeper.VerdictFields());
    }

    [Fact]
    public void Healing_trained_heals_with_the_kit_until_the_until_share_and_keeps_the_potions()
    {
        var rig = new C2VitalsRig(new C2Character { KitUses = 10, Potions = 5, Spells = HealSelfToIII, Health = 100 });

        Assert.False(rig.Keeper.NeedsFor(new VitalsNeeds()));
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["kit", "kit"], rig.Used); // 100 -> 160 (80%, under the 90% until) -> 200
        Assert.Equal(2, rig.Keeper.Kits);
        Assert.Equal(0, rig.Keeper.Potions);
        Assert.Equal(5u, rig.Potions);
    }

    [Fact]
    public void Healing_untrained_casts_Heal_Self_at_the_highest_level_the_chance_allows_in_the_magic_mode_and_returns_to_peace()
    {
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, KitUses = 10, Potions = 5, Spells = HealSelfToIII, Health = 100 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["mode:8", "cast:1158", "cast:1158", "mode:1"], rig.Used); // no kit (0x04FC), no potion: the spell is cheaper
        Assert.All(rig.CastModes, m => Assert.Equal(CombatModes.Magic, m));
        Assert.Equal(2, rig.Keeper.HealSelfCasts);
        Assert.Equal(2, rig.Keeper.Heals);
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void No_kit_and_no_spell_drinks_potions_to_the_until_share()
    {
        var rig = new C2VitalsRig(new C2Character { Potions = 5, Health = 100 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["potion", "potion", "potion", "potion"], rig.Used); // 100, 125, 150, 175 (under 180) -> 200
        Assert.Equal("vitals_min=50/100/100 heals=4 kits=0 potions=4 rests=0 rest_s=0 transfers=0 mana_potions=0 stamina_potions=0", rig.Keeper.VerdictFields());
    }

    [Fact]
    public void A_refused_magic_mode_sets_the_spells_aside_loudly_and_the_potion_heals()
    {
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Caster = false, Potions = 5, Spells = HealSelfToIII, Health = 100 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.DoesNotContain(rig.Used, u => u.StartsWith("cast:", StringComparison.Ordinal) || u.StartsWith("mode:", StringComparison.Ordinal));
        Assert.Equal(4, rig.Keeper.Potions);
        Assert.NotNull(rig.Keeper.CastOff);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("the keeper casts nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fighter_with_the_caster_in_the_pack_casts_nothing_and_reports_cast_needs_swap_while_the_potion_heals_P5a_C1b()
    {
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Caster = false, CasterInPack = true, Potions = 5, Spells = HealSelfToIII, Health = 100 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.DoesNotContain(rig.Used, u => u.StartsWith("cast:", StringComparison.Ordinal) || u.StartsWith("mode:", StringComparison.Ordinal));
        Assert.Equal(4, rig.Keeper.Potions);
        Assert.Equal(VitalsKeeper.NeedsSwap, rig.Keeper.CastOff);
        Assert.EndsWith(" cast=needs-swap", rig.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("cast=needs-swap", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_lost_kit_rolls_set_the_kit_aside_at_that_health_and_the_rule_turns_to_the_potion()
    {
        var rig = new C2VitalsRig(new C2Character { KitUses = 10, Potions = 5, Health = 100 });
        rig.KitRolls(false, false);

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["kit", "kit", "potion", "kit"], rig.Used); // the potion moves the health (125), the lost rolls clear, the kit wins (185)
    }

    [Fact]
    public void Three_fizzles_in_a_row_set_Heal_Self_aside_and_the_potion_follows()
    {
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Potions = 5, Spells = HealSelfToIII, Health = 100 });
        rig.FizzleNext(HealSelfIII, 3);

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(3, rig.Keeper.Fizzles);
        Assert.Equal(0, rig.Keeper.HealSelfCasts);
        Assert.Equal(["mode:8", "cast:1158", "cast:1158", "cast:1158", "potion", "potion", "potion", "potion", "mode:1"], rig.Used);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("Heal Self is set aside", StringComparison.Ordinal));
    }

    [Fact]
    public void With_nothing_to_heal_with_the_keeper_rests_in_peace_to_the_recovery_rule_s_start_trigger()
    {
        var rig = new C2VitalsRig(new C2Character { Health = 130, Mode = CombatModes.Melee });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["mode:1"], rig.Used); // peace first: a combat mode halves regeneration
        Assert.Equal(1, rig.Keeper.Rests);
        Assert.InRange(rig.Keeper.RestSeconds, 280, 292); // 130 -> 150 (75%): 20 points at ACE's 0.3 a 5 s tick times the attribute factor 1.1667 (0.07 a second) is 286 s: the rule's tail ends a recovery with no means at its start trigger
        Assert.True(rig.Health >= 150);
    }

    [Fact]
    public void A_rest_over_its_cap_ends_Blocked_with_the_vital_s_name()
    {
        var rig = new C2VitalsRig(new C2Character { Health = 130 }, new VitalsOptions { RestCapSeconds = 30 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Blocked, rig.RunToEnd());

        Assert.Equal("rest-cap:health", rig.Keeper.Why);
        Assert.InRange(rig.Keeper.RestSeconds, 30, 31);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("Blocked why=rest-cap:health", StringComparison.Ordinal));
    }

    [Fact]
    public void Low_stamina_casts_Revitalize_Self_on_to_the_named_level()
    {
        var rig = new C2VitalsRig(new C2Character { Spells = [1177, 1178, RevitalizeIII], Stamina = 40 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(3, rig.Keeper.Revitalizes); // 40 -> 85 -> 130 -> 175 (80% is 160)
        Assert.All(rig.Used.Where(u => u.StartsWith("cast:", StringComparison.Ordinal)), u => Assert.Equal("cast:1179", u));
        Assert.Equal(0, rig.Keeper.Rests);
    }

    [Fact]
    public void Low_stamina_without_Revitalize_rests_standing_by_default()
    {
        var rig = new C2VitalsRig(new C2Character { Stamina = 40 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.InRange(rig.Keeper.RestSeconds, 195, 205); // 40 -> 160: 120 points at ACE's 3 a 5 s tick (0.6 a second) is 200 s
        Assert.Empty(rig.Spy.Calls);
    }

    [Fact]
    public void Lying_down_sends_the_emote_key_rests_three_times_faster_and_stands_up_with_the_stop_key()
    {
        var rig = new C2VitalsRig(new C2Character { Stamina = 40 }, new VitalsOptions { Posture = RestPosture.Lie });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["0x41000014 down", "0x41000014 up", "0x41000003 down"], rig.Spy.Calls);
        Assert.InRange(rig.Keeper.RestSeconds, 64, 70); // 120 points at 0.6 x 3.0 a second (ACE's sleep factor 3.0): 66.7 s
        Assert.False(rig.Keeper.PostureDown);
        Assert.Equal(0u, rig.Spy.Posture);
    }

    [Fact]
    public void Sitting_is_the_other_posture()
    {
        var rig = new C2VitalsRig(new C2Character { Stamina = 40 }, new VitalsOptions { Posture = RestPosture.Sit });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["0x41000013 down", "0x41000013 up", "0x41000003 down"], rig.Spy.Calls);
        Assert.InRange(rig.Keeper.RestSeconds, 78, 84); // 0.6 x 2.5 a second: 80 s
    }

    [Fact]
    public void The_keeper_stands_up_before_any_other_action()
    {
        // Lying down for health with no means; once the mana covers Heal Self it stands first, then enters the magic mode and casts.
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100, Mana = 0 }, new VitalsOptions { Posture = RestPosture.Lie });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Contains("cast:1158", rig.Used);
        Assert.All(rig.PosturesAtUse, p => Assert.Equal(0u, p));
        Assert.Equal("0x41000003 down", rig.Spy.Calls[2]);
    }

    [Fact]
    public void Unbound_movement_rests_standing_and_says_why()
    {
        var rig = new C2VitalsRig(new C2Character { Stamina = 40 }, new VitalsOptions { Posture = RestPosture.Lie }, bindController: false);

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.False(rig.Keeper.PostureDown);
        Assert.InRange(rig.Keeper.RestSeconds, 195, 205); // standing: 200 s at ACE's rate
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("the rest posture lie was not taken", StringComparison.Ordinal));
    }

    [Fact]
    public void Stamina_to_Mana_Self_comes_before_a_cast_that_needs_more_mana()
    {
        var rig = new C2VitalsRig(new C2Character { Spells = [1676, 1677, StaminaToManaIII], Mana = 50 });

        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["mode:8", "cast:1678", "cast:1678"], rig.Used.Take(3)); // 50 -> 130 -> 155 (the rig's cost 30, ACE's transfer)
        Assert.Equal(2, rig.Keeper.Transfers);
        Assert.True(rig.Mana >= 150);
    }

    [Fact]
    public void A_mana_shortfall_without_a_transfer_rests_or_ends_Blocked_where_no_rest_is_allowed()
    {
        var rests = new C2VitalsRig(new C2Character { Mana = 50 });
        rests.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 100));
        Assert.Equal(VitalsKeeperState.Ready, rests.RunToEnd());
        Assert.InRange(rests.Keeper.RestSeconds, 245, 255); // 50 mana at ACE's 1 a 5 s tick (0.2 a second): 250 s

        var blocked = new C2VitalsRig(new C2Character { Mana = 50 });
        blocked.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 100, NoManaRest: true));
        Assert.Equal(VitalsKeeperState.Blocked, blocked.RunToEnd());
        Assert.Equal("mana:50/100", blocked.Keeper.Why);

        var over = new C2VitalsRig(new C2Character());
        over.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 250));
        Assert.Equal(VitalsKeeperState.Blocked, over.RunToEnd());
        Assert.Equal("mana-max:250/200", over.Keeper.Why);
    }

    [Fact]
    public void At_ACE_s_rates_a_deep_health_rest_and_a_big_mana_rest_hit_the_600_s_cap_and_a_transfer_decides_the_mana_P5a_R5()
    {
        // Health: 100 -> 150 is 50 points at 0.07 a second, 714 s, over the default rest cap of 600 s: the cap decides, named.
        var health = new C2VitalsRig(new C2Character { Health = 100 });
        health.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Blocked, health.RunToEnd(maxSeconds: 1000));
        Assert.Equal("rest-cap:health", health.Keeper.Why);
        Assert.InRange(health.Keeper.RestSeconds, 600, 601);

        // Mana: 0 -> 150 is 750 s at 0.2 a second, over the cap: the rest alone cannot do it...
        var rest = new C2VitalsRig(new C2Character { Mana = 0 });
        rest.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        Assert.Equal(VitalsKeeperState.Blocked, rest.RunToEnd(maxSeconds: 1000));
        Assert.Equal("rest-cap:mana", rest.Keeper.Why);

        // ...and with Stamina to Mana Self known and the mana to cast it, the transfer decides it in seconds, not minutes.
        var transfer = new C2VitalsRig(new C2Character { Spells = [1676, 1677, StaminaToManaIII], Mana = 40 });
        transfer.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        Assert.Equal(VitalsKeeperState.Ready, transfer.RunToEnd(maxSeconds: 1000));
        Assert.True(transfer.Keeper.Transfers >= 1);
        Assert.True(transfer.Now < 300, "the transfer decided (the stamina rest it causes included), far under the mana rest's 750 s: " + transfer.Now);
    }

    [Fact]
    public void A_station_s_minimum_raises_the_health_rule()
    {
        var rig = new C2VitalsRig(new C2Character { Potions = 5, Health = 160 }); // 80%: over the 75% trigger

        Assert.True(new C2VitalsRig(new C2Character { Potions = 5, Health = 160 }).Keeper.NeedsFor(new VitalsNeeds()));
        Assert.False(rig.Keeper.NeedsFor(new VitalsNeeds(MinHealthPercent: 95)));
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());
        Assert.Equal(["potion", "potion"], rig.Used); // 160 -> 185 (92.5%, under 95) -> 200
    }

    [Fact]
    public void An_attack_is_refused_under_the_attack_stamina()
    {
        var low = new C2VitalsRig(new C2Character { Stamina = 20 });
        Assert.False(low.Keeper.MayAttack(out var why));
        Assert.Contains("10% is under --vitals-attack-stamina 15%", why, StringComparison.Ordinal);

        var enough = new C2VitalsRig(new C2Character { Stamina = 40 });
        Assert.True(enough.Keeper.MayAttack(out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void The_in_fight_potion_is_off_by_default()
    {
        var rig = new C2VitalsRig(new C2Character { Potions = 5, Health = 60 });

        Assert.False(rig.Keeper.TryInFightPotion(targetInReach: false));
        rig.Frame();
        Assert.Empty(rig.Used);
    }

    [Fact]
    public void The_in_fight_potion_is_drunk_only_under_its_share_with_the_target_out_of_reach()
    {
        var rig = new C2VitalsRig(new C2Character { Potions = 5, Health = 60, Mode = CombatModes.Melee }, new VitalsOptions { InFightPotion = true });

        Assert.False(rig.Keeper.TryInFightPotion(targetInReach: true));
        Assert.True(rig.Keeper.TryInFightPotion(targetInReach: false));
        Assert.False(rig.Keeper.TryInFightPotion(targetInReach: false)); // one in flight
        rig.Frame();
        rig.Frame();

        Assert.Equal(["potion"], rig.Used);
        Assert.Equal(1, rig.Keeper.InFightPotions);
        Assert.Equal(1, rig.Keeper.Potions);
        Assert.Equal(VitalsKeeperState.Idle, rig.Keeper.State); // no station asked: the keeper only settled it

        rig.SetHealth(100); // 50%: over the 40% share
        rig.Frame();
        Assert.False(rig.Keeper.TryInFightPotion(targetInReach: false));
    }

    [Fact]
    public void The_verdict_line_has_its_golden_shape_and_reads_the_minimum_shares()
    {
        var fresh = new C2VitalsRig(new C2Character());
        Assert.Equal("vitals_min=-/-/- heals=0 kits=0 potions=0 rests=0 rest_s=0 transfers=0 mana_potions=0 stamina_potions=0", fresh.Keeper.VerdictFields());

        var rig = new C2VitalsRig(new C2Character { KitUses = 10, Spells = [1676, 1677, StaminaToManaIII], Health = 100, Stamina = 50, Mana = 60 });
        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 100));
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        var line = rig.Keeper.VerdictFields();
        Assert.Matches(VerdictShape(), line);
        Assert.StartsWith("vitals_min=50/", line, StringComparison.Ordinal);
        Assert.Contains("kits=2", line, StringComparison.Ordinal);
        Assert.Contains(" rests=", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Vitae_is_reported_never_acted_on()
    {
        var rig = new C2VitalsRig(new C2Character());
        Assert.Equal(0, rig.Keeper.VitaePercent);
        rig.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(666, 1, 666, 0, 0, 0, -1, p, 0f, 0f, 0.0, 0x800000 | 0x4000 | 0x2000 | 0x2, 0, 0.95f, 0)));

        Assert.Equal(5, rig.Keeper.VitaePercent);
        Assert.True(rig.Keeper.NeedsFor(new VitalsNeeds()));
        rig.Frame();
        Assert.Empty(rig.Used);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("vitae 5% (reported, not acted on)", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ M5 1 K5: the mana order (D15, D27)

    [Fact]
    public void The_mana_order_is_todays_order_for_a_caster_without_Mana_Conversion_and_cast_mana_tries_0_reproduces_it_K5()
    {
        // The rig's character has no Mana Conversion: every try floor is the ceiling and a press lands only at its base, so the dance is today's single transfer. Both settings send the same actions.
        var ceiling = new C2VitalsRig(new C2Character { Spells = [1676, 1677, StaminaToManaIII], Mana = 50 }, VitalsOptions.Default with { Mana = ManaOrderOptions.Ceiling });
        ceiling.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        Assert.Equal(VitalsKeeperState.Ready, ceiling.RunToEnd());

        var order = new C2VitalsRig(new C2Character { Spells = [1676, 1677, StaminaToManaIII], Mana = 50 });
        order.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        Assert.Equal(VitalsKeeperState.Ready, order.RunToEnd());

        Assert.Equal(ceiling.Used, order.Used);
        Assert.Equal(ceiling.Keeper.Transfers, order.Keeper.Transfers);
        Assert.Equal(0, ceiling.Keeper.Dances);
        Assert.Equal(order.Keeper.Transfers, order.Keeper.DancesLanded); // each transfer was a dance that landed on its first press
        Assert.Equal(order.Keeper.Dances, order.Keeper.DancePresses);
        Assert.Equal(order.Keeper.DancesLanded, order.Keeper.Tally.Proved); // M5 1 fix B (G5): a landed press is proved by ACE's transfer line (Magic chat), so it ends Landed and counts as proved
        Assert.Contains($" proved={order.Keeper.Tally.Proved}", order.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, ceiling.Keeper.Tally.Proved); // today's order asks no proof
        Assert.Contains($" mana_presses={order.Keeper.DancePresses} dances={order.Keeper.Dances} dances_landed={order.Keeper.DancesLanded}", order.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" mana_tries=", order.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain("mana_presses=", ceiling.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(VerdictShape(), order.Keeper.VerdictFields());
    }

    [Fact]
    public void With_the_tries_on_a_station_need_is_met_at_its_try_floor_and_tries_0_waits_for_its_ceiling_K5()
    {
        var floor = new C2VitalsRig(new C2Character { Spells = [1676, 1677, StaminaToManaIII], Mana = 50 });
        Assert.True(floor.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150, NextCastFloor: 40))); // the station's own attempt tries the real spell from 40
        Assert.Empty(floor.Used);
        Assert.Contains(floor.R.H.Log.Lines, l => l.Contains("150 for the next cast (its try floor 40)", StringComparison.Ordinal));

        var ceiling = new C2VitalsRig(new C2Character { Spells = [1676, 1677, StaminaToManaIII], Mana = 50 }, VitalsOptions.Default with { Mana = ManaOrderOptions.Ceiling });
        Assert.False(ceiling.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150, NextCastFloor: 40)));
        Assert.Equal(VitalsKeeperState.Ready, ceiling.RunToEnd());
        Assert.True(ceiling.Keeper.Transfers >= 1);

        // Under the floor the order runs: the dance funds it.
        var under = new C2VitalsRig(new C2Character { Spells = [1676, 1677, StaminaToManaIII], Mana = 35 });
        Assert.False(under.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150, NextCastFloor: 40)));
        Assert.Equal(VitalsKeeperState.Ready, under.RunToEnd());
        Assert.Equal(1, under.Keeper.DancesLanded);
    }

    [Fact]
    public void The_keepers_cast_mana_options_parse_and_a_bad_one_is_a_named_error_K5()
    {
        var o = VitalsOptions.FromArguments(CommandLineArgument.ParseAll(["--cast-mana-tries=0", "--cast-mana-try-spacing-s=0.5"]));
        Assert.Empty(o.Errors);
        Assert.False(o.Mana.On);
        Assert.Equal(0.5, o.Mana.TrySpacingSeconds);
        var bad = VitalsOptions.FromArguments(CommandLineArgument.ParseAll(["--cast-mana-try-chance=2"]));
        Assert.Equal("bad cast option: --cast-mana-try-chance: '2' is not more than 0 and at most 1", Assert.Single(bad.Errors));
        Assert.Equal(ManaOrderOptions.DefaultTryChance, bad.Mana.TryChance);
    }

    // ------------------------------------------------------------------------------------------------ M5 3 m4: the keeper sees Mana Conversion (m5-keeper-tries-rig) and its tries are cut to the pass's cap (m5-keeper-tries-cap)

    [Fact]
    public void With_Mana_Conversion_a_heal_is_cast_from_its_try_floor_and_a_try_the_roll_lands_costs_the_rolled_mana_M5_3_m4()
    {
        // 20 mana is under Heal Self III's ceiling (30) and over its try floor at Mana Conversion 133; every roll takes the whole discount, so the cost is small and the try lands.
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100, Mana = 20, ManaConversionRanks = 100 });
        for (var i = 0; i < 30; i++)
        {
            rig.ManaRolls.Enqueue(0.0);
        }

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["mode:8", "cast:1158", "cast:1158", "mode:1"], rig.Used);
        Assert.Equal(0, rig.ManaRefused);
        Assert.Equal(200u, rig.Health);
        Assert.True(rig.Mana is > 0 and < 20, $"the rolled costs were charged (mana {rig.Mana})");
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("from its try floor", StringComparison.Ordinal));
        Assert.Contains(" mana_refusals=0 ", rig.Keeper.VerdictFields() + " ", StringComparison.Ordinal);
    }

    [Fact]
    public void With_Mana_Conversion_a_try_the_roll_refuses_is_a_0x0401_with_nothing_charged_and_the_next_try_lands_M5_3_m4()
    {
        // The first try's rolls pay the whole base (30) with 20 held: ACE refuses it 0x0401 and charges nothing; the keeper's tries send it again after the spacing and the next rolls land it.
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100, Mana = 20, ManaConversionRanks = 100 });
        for (var i = 0; i < 3; i++)
        {
            rig.ManaRolls.Enqueue(1.0);
        }

        for (var i = 0; i < 30; i++)
        {
            rig.ManaRolls.Enqueue(0.0);
        }

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(1, rig.ManaRefused);
        Assert.Equal(["mode:8", "cast:1158", "cast:1158", "cast:1158", "mode:1"], rig.Used); // the refused try, then the two heals
        Assert.Equal(200u, rig.Health);
        Assert.Contains(" mana_refusals=1 ", rig.Keeper.VerdictFields() + " ", StringComparison.Ordinal);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("(0x0401", StringComparison.Ordinal) || l.Contains("mana try", StringComparison.Ordinal));
    }

    [Fact]
    public void The_real_spell_tries_are_cut_to_the_actions_the_pass_has_left_M5_3_m4()
    {
        // Every roll pays the whole base, so with 20 mana every try is refused. 40 Mana Conversion tries would send 40 casts after the mode change (an action too): 41 in a pass capped at 40. The attempt is cut to the 39 left.
        var options = VitalsOptions.Default with { Mana = new ManaOrderOptions { Tries = ManaOrderOptions.MaxTries, TrySpacingSeconds = 0 } };
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100, Mana = 20, ManaConversionRanks = 100 }, options);
        for (var i = 0; i < 300; i++)
        {
            rig.ManaRolls.Enqueue(1.0);
        }

        rig.Keeper.NeedsFor(new VitalsNeeds());
        rig.RunToEnd();

        var casts = rig.Used.Count(u => u.StartsWith("cast:", StringComparison.Ordinal));
        Assert.Equal(VitalsKeeper.MaxActionsPerPass - 1, casts); // the mode change took one of the 40
        Assert.Equal(casts, rig.ManaRefused);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("tries are spent", StringComparison.Ordinal) || l.Contains("Mana Conversion tries refused", StringComparison.Ordinal));
    }
}

using Lifestone.Client.Session;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The fighters' buff leg (M4 P5a C4, <see cref="BuffScenario"/> over a <see cref="FighterTemplate"/>) on <see cref="C1bSwapRig"/> with the fighter's profile: no gear step, the caster swap in once, the ordered pass over the
/// fighter's twelve lines with the mana transfer, the forced re-buff pass, the swap out once; a failure after the swap in puts the weapon back before the run ends; the wrong character in the world is refused and sent nothing;
/// the Hunter has no buff leg. The mage's own scenario is <c>BuffScenarioTests</c>.
/// </summary>
public sealed class FighterBuffScenarioTests
{
    private const string Heart = "The Healer's Heart";

    private static readonly System.Text.RegularExpressions.Regex Verdict = new(
        @"^gear=n/a worn=n/a buffs=12/12 levels=(\w+:(I|II|III|IV|V|VI|VII|VIII|-),){11}\w+:(I|II|III|IV|V|VI|VII|VIII|-) rebuffed=yes forced=yes min_remaining_s=\d+ transfers=\d+/\d+ revitalizes=\d+ fizzles=\d+ mana_min=(\d+|n/a) cycle_s=\d+ misses=none surpassed=\d+ swapped=yes swap_s=\d+\.\d$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private sealed class View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    /// <summary>The swap rig with the fighter's profile and the scenario over it; notes how many casts were answered when the magic mode and peace were asked.</summary>
    private sealed class Rig
    {
        public Rig(FighterTemplate template, bool refuseCaster = false, uint manaPotions = 0)
        {
            Template = template;
            Swap = new C1bSwapRig(template, refuseCaster, withSwap: false, fighterProfile: true, manaPotions: manaPotions);
            Swap.R.H.State.Self.Name = template.Name;

            // The real costs: level IV of a buff line is 50 mana (the scripted table's 10 a level makes it 40); Stamina to Mana IV stays 40 (W0, handoff C1).
            foreach (var line in FighterProfile.For(template)!.Lines.Where(l => l.IsBuff))
            {
                foreach (var spell in line.Spells.Where(x => x.Position == 4))
                {
                    Swap.Caster.Table[spell.Id] = Swap.Caster.Table[spell.Id] with { BaseMana = 50 };
                }
            }

            Swap.Caster.ManaRegenPerSecond = 0.2; // a fighter's live regeneration: 1 mana a 5 s tick
            Swap.Caster.StaminaRegenPerSecond = 0.3;
            Swap.Caster.CeilingCost = true; // the worst case: every cast costs the ceiling (the fighters now train Mana Conversion, M4 P5a C7, and pay less live)
            View = new View(Swap.R);
        }

        public FighterTemplate Template { get; }

        public C1bSwapRig Swap { get; }

        public IGameSessionView View { get; }

        public int AnsweredAtMagic { get; private set; } = -1;

        public int AnsweredAtSwapOut { get; private set; } = -1;

        /// <summary>What the server was asked about the equipment, in order, without the combat-mode changes (a mana wait hops out of the magic mode and back).</summary>
        public List<string> EquipmentOps => [.. Swap.Ops.Where(o => !o.StartsWith("mode:", StringComparison.Ordinal))];

        public BuffScenario Scenario(bool endSession = false) =>
            new(Template, Swap.Caster.Table, Swap.Options with { Auto = false, RebuffMarginSeconds = 300 }, Swap.R.H.Log, endSession: endSession);

        public ScenarioStatus Run(BuffScenario scenario, double maxSeconds = 6000, double dt = 0.25)
        {
            var end = Swap.Now + maxSeconds;
            var status = ScenarioStatus.Running;
            while (status == ScenarioStatus.Running && Swap.Now < end)
            {
                Swap.R.Tick(dt);
                Swap.Caster.Pump();
                Swap.Pump();
                Swap.R.Tick();
                status = scenario.Step(View, Swap.Now);
                if (AnsweredAtMagic < 0 && Swap.Ops.Contains("mode:8"))
                {
                    AnsweredAtMagic = Swap.Caster.Answered.Count;
                }

                if (AnsweredAtSwapOut < 0 && Swap.Ops.Contains("unwield:" + Heart))
                {
                    AnsweredAtSwapOut = Swap.Caster.Answered.Count;
                }
            }

            return status;
        }
    }

    [Fact]
    public void A_brawlers_buff_leg_swaps_in_once_casts_the_ordered_pass_with_a_transfer_and_the_forced_pass_then_swaps_out_with_the_Mace_in_hand_in_peace()
    {
        var t = new Rig(FighterTemplate.Melee, manaPotions: FighterKit.ManaPotionCount);
        var scenario = t.Scenario();
        var status = t.Run(scenario);
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + " | " + string.Join(" | ", t.Swap.R.H.Log.Lines.Where(l => l.Contains("[buff] mana", StringComparison.Ordinal)).Take(40)));
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);

        // The order of what the server was asked: the swap in, nothing else touched the equipment (no gear step), the swap out; each exactly once (the mana waits' hops between the magic mode and peace are modes only).
        Assert.Equal(["unwield:" + C6Names.BrawlerWeapon, "wield:" + Heart, "unwield:" + Heart, "wield:" + C6Names.BrawlerWeapon], t.EquipmentOps);
        Assert.Equal(["unwield:" + C6Names.BrawlerWeapon, "wield:" + Heart, "mode:8"], t.Swap.Ops.Take(3));
        Assert.Equal(["mode:1", "unwield:" + Heart, "wield:" + C6Names.BrawlerWeapon], t.Swap.Ops.TakeLast(3));
        Assert.Equal(0, t.AnsweredAtMagic); // no cast before the magic mode
        Assert.Equal(t.Swap.Caster.Answered.Count, t.AnsweredAtSwapOut); // no cast after the caster left the hand
        Assert.Equal((uint)EquipMask.MeleeWeapon, t.Swap.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0u, t.Swap.LocationOf(C1bSwapRig.WandGuid));
        Assert.Equal(1, t.Swap.CombatMode);
        Assert.Equal(0, scenario.Gear.GearSteps); // the inert gear scenario never stepped

        // The verdict keeps the mage's fields, counts the fighter's twelve lines and ends with the swap's.
        var verdict = scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.Contains(t.Swap.R.H.Log.Lines, l => l.Contains("verdict gear=n/a worn=n/a buffs=12/12", StringComparison.Ordinal));
        Assert.Equal(12, scenario.CycleResult!.Wanted);
        Assert.Equal(12, scenario.CycleResult.Active);
        Assert.Equal(12, scenario.CycleCast); // twelve lines at level IV (Creature Enchantment and Life Magic 183 to 196 reach no V)
        // M4 P5a C7 (D28): was InRange(TransfersFired, 3, 8) with the Revitalize that refilled the stamina. A pool of 225 pays four buffs and the rest is Mana Potions now: no transfer, no Revitalize.
        Assert.Equal((0, 0, 0), (scenario.CycleResult.TransfersFired, scenario.CycleResult.TransfersTried, scenario.CycleResult.Revitalizes));
        Assert.InRange(scenario.CycleResult.ManaPotions, 3, FighterKit.ManaPotionCount);
        Assert.True(t.Swap.Caster.PotionsDrunk >= scenario.CycleResult.ManaPotions);
        Assert.True(t.Swap.Now < 1500, "the whole leg fits the headless limit (3000 s) with room: " + t.Swap.Now);
        Assert.True(scenario.ForcedResult!.Forced);
        Assert.True(scenario.ForcedResult.Rebuffed);
        Assert.True(scenario.Swap!.Swapped);
        Assert.Equal(SwapStatus.Done, scenario.Swap.Status);
    }

    [Fact]
    public void An_archers_buff_leg_casts_its_own_twelve_lines_including_Missile_Weapon_Mastery_and_ends_with_the_Longbow_and_the_arrows_in_hand()
    {
        var t = new Rig(FighterTemplate.Archer, manaPotions: FighterKit.ArcherManaPotionCount); // M4 P5a C7: the fighters carry Mana Potions
        var scenario = t.Scenario();
        var status = t.Run(scenario);
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);

        Assert.Equal(
            ["unwield:Greater Frost Arrow", "unwield:" + C6Names.ArcherBow, "wield:" + Heart, "unwield:" + Heart, "wield:" + C6Names.ArcherBow, "wield:Greater Frost Arrow"],
            t.EquipmentOps);
        Assert.Equal(["mode:1", "unwield:" + Heart, "wield:" + C6Names.ArcherBow, "wield:Greater Frost Arrow"], t.Swap.Ops.TakeLast(4));
        Assert.Equal((uint)EquipMask.MissileWeapon, t.Swap.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal((uint)EquipMask.MissileAmmo, t.Swap.LocationOf(C1bSwapRig.AmmoGuid));
        Assert.Matches(Verdict, scenario.VerdictFields());

        // Every buff line of the Archer's profile was cast at least once, and the Brawler's Heavy Weapon Mastery never was.
        var profile = FighterProfile.Archer;
        var cast = t.Swap.Caster.Answered.Select(a => a.Spell).ToHashSet();
        foreach (var line in profile.Lines.Where(l => l.IsBuff))
        {
            Assert.Contains(line.Spells, s => cast.Contains(s.Id));
        }

        Assert.DoesNotContain(FighterProfile.Brawler.WeaponMastery.Spells, s => cast.Contains(s.Id));
    }

    [Fact]
    public void The_fighters_pass_drinks_a_mana_potion_where_it_once_cast_a_transfer_ahead_and_no_wait_runs_out_M4_P5a_C7()
    {
        // M4 P5a C7 (D28): was "the first transfer is cast ahead of the buff that would leave the pool under its cost". A fighter's pass no longer chains transfers: a Mana Potion is drunk when the next buff needs the mana.
        var t = new Rig(FighterTemplate.Melee, manaPotions: FighterKit.ManaPotionCount);
        var scenario = t.Scenario();
        var status = t.Run(scenario);
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        var lines = t.Swap.R.H.Log.Lines.ToList();
        var firstMana = lines.First(l => l.Contains("[buff] mana: ", StringComparison.Ordinal));
        Assert.Contains("Mana Potion 0x", firstMana, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("mana: Stamina to Mana", StringComparison.Ordinal) || l.Contains("mana: Revitalize", StringComparison.Ordinal));

        // The waits are the cycle's bounded last resort (BuffOptions.ManaWaitSeconds, 180 s each); none ran out.
        Assert.DoesNotContain(lines, l => l.Contains("the mana wait ran out", StringComparison.Ordinal));
    }

    [Fact]
    public void With_round_5s_Archer_numbers_and_a_stack_of_potions_the_buff_pass_needs_no_transfer_and_no_Revitalize_at_all_M4_P5a_C7()
    {
        // Round 5's Archer started the buff leg with 135 mana; the precondition (30% mana) and the pass drink potions, the keeper casts nothing and the cycle sends no transfer.
        var t = new Rig(FighterTemplate.Archer, manaPotions: FighterKit.ArcherManaPotionCount);
        t.Swap.Caster.SetVitals(135, t.Swap.Caster.Stamina);
        var scenario = t.Scenario();
        var status = t.Run(scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(0, scenario.PreconditionKeeper!.Transfers);
        Assert.Equal(0, scenario.CycleResult!.TransfersTried);
        Assert.Equal(0, scenario.CycleResult.Revitalizes);
        Assert.Equal(0, scenario.ForcedResult!.TransfersTried);
        Assert.True(t.Swap.Caster.PotionsDrunk > 0);
        Assert.True(t.Swap.Caster.PotionsDrunk < FighterKit.ArcherManaPotionCount, "the Archer's stack of 60 covers the pass: " + t.Swap.Caster.PotionsDrunk);
        t.Swap.R.H.Log.Lines.ToList().ForEach(l => Assert.DoesNotContain("mana-futile", l, StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_caster_fails_the_run_at_swap_in_with_the_Mace_back_in_hand_and_no_cast()
    {
        var t = new Rig(FighterTemplate.Melee, refuseCaster: true);
        var scenario = t.Scenario();
        var status = t.Run(scenario);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("swap-in: swap:wield-caster", scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(t.Swap.Caster.Answered);
        Assert.Equal((uint)EquipMask.MeleeWeapon, t.Swap.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(1, t.Swap.CombatMode);
        Assert.EndsWith("swap_why=swap:wield-caster", scenario.VerdictFields(), StringComparison.Ordinal); // the weapon was moved and restored: swapped=yes, with the reason
    }

    [Fact]
    public void A_cycle_failure_after_the_swap_in_puts_the_weapon_back_before_the_run_ends_and_the_verdict_says_swapped_yes()
    {
        var t = new Rig(FighterTemplate.Melee);
        t.Swap.Caster.FizzleNext(_ => true, 1000); // every cast fizzles: the cycle gives up
        var scenario = t.Scenario();
        var status = t.Run(scenario);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.NotNull(scenario.Failure);
        Assert.Equal(["unwield:" + C6Names.BrawlerWeapon, "wield:" + Heart, "unwield:" + Heart, "wield:" + C6Names.BrawlerWeapon], t.EquipmentOps);
        Assert.Equal(["mode:1", "unwield:" + Heart, "wield:" + C6Names.BrawlerWeapon], t.Swap.Ops.TakeLast(3));
        Assert.Equal((uint)EquipMask.MeleeWeapon, t.Swap.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(1, t.Swap.CombatMode);
        Assert.EndsWith(" swapped=yes swap_s=" + scenario.Swap!.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_in_the_world_as_another_character_fails_at_once_and_sends_nothing_and_the_Hunter_has_no_buff_leg()
    {
        var t = new Rig(FighterTemplate.Melee);
        t.Swap.R.H.State.Self.Name = FighterTemplate.ArcherName;
        var sends = t.Swap.R.H.Net.Sends.Count;
        var scenario = t.Scenario(endSession: true);
        Assert.Equal(ScenarioStatus.Failed, t.Run(scenario, maxSeconds: 30));
        Assert.Contains("not " + FighterTemplate.BrawlerName, scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(sends, t.Swap.R.H.Net.Sends.Count);

        Assert.Throws<ArgumentException>(() => new BuffScenario(FighterTemplate.Hunter, t.Swap.Caster.Table, new BuffOptions(), t.Swap.R.H.Log));
    }

    [Fact]
    public void A_drained_archer_rests_in_peace_to_the_precondition_before_the_swap_and_then_casts_the_whole_pass_P5a_r4()
    {
        // Round 4: FighterArcherBuffLiveTests ran after the heal leg left the Archer at 9 mana and a low stamina; the pass traded them back and forth for 64 casts.
        var t = new Rig(FighterTemplate.Archer);
        t.Swap.Caster.SetVitals(9, 40);
        t.Swap.Caster.ManaRegenPerSecond = 0.8; // round 4's rise between transfers: 0 -> 10 in 13.1 s
        t.Swap.Caster.StaminaRegenPerSecond = 1.0;
        var scenario = t.Scenario();
        var status = t.Run(scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        var lines = t.Swap.R.H.Log.Lines.ToList();
        var rest = lines.FindIndex(l => l.Contains("precondition: the vitals keeper restores stamina to 80% and mana to 30%", StringComparison.Ordinal));
        var met = lines.FindIndex(l => l.Contains("precondition met: ", StringComparison.Ordinal));
        Assert.True(rest >= 0 && met > rest, string.Join(" | ", lines.Take(40)));
        Assert.Equal("unwield:Greater Frost Arrow", t.EquipmentOps[0]); // the swap came after the rest
        Assert.True(lines.FindIndex(l => l.Contains("swap in", StringComparison.OrdinalIgnoreCase) || l.Contains("caster swap first", StringComparison.Ordinal)) > met);
        Assert.Matches(Verdict, scenario.VerdictFields());
        Assert.InRange(scenario.CycleResult!.TransfersTried, 0, 20);
    }

    [Fact]
    public void A_fighter_whose_stamina_cannot_come_back_fails_up_front_as_precondition_stamina_with_the_weapon_in_hand_and_no_cast_P5a_r4()
    {
        var t = new Rig(FighterTemplate.Archer);
        t.Swap.Caster.SetVitals(t.Swap.Caster.Mana, 20);
        t.Swap.Caster.StaminaRegenPerSecond = 0;
        var scenario = t.Scenario();
        var status = t.Run(scenario, maxSeconds: 900);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("precondition:stamina: ", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("rest-cap:stamina", scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(t.Swap.Caster.Answered);
        Assert.Empty(t.EquipmentOps);
    }

    [Fact]
    public void A_drained_archer_with_no_potion_casts_no_Revitalize_for_mana_and_at_most_the_capped_transfers_and_the_mana_wait_brings_the_buff_P5a_C7()
    {
        // M4 P5a C7 (D28): was "Mana casts that raise nothing stop after the futile window" (round 4's trade of transfers and Revitalizes, 64 casts). A fighter's pass now casts no Revitalize for the sake of mana and at most
        // FighterTransferCap transfers, and only at a high stamina, so that trade cannot start; the futile window stays as the safety net behind it. The cycle's own loop, without the precondition and without potions.
        var t = new Rig(FighterTemplate.Archer);
        t.Swap.Caster.SetVitals(9, 25);
        t.Swap.Caster.ManaRegenPerSecond = 0.8;
        t.Swap.Caster.StaminaRegenPerSecond = 0.1;
        var scenario = new BuffScenario(FighterTemplate.Archer, t.Swap.Caster.Table, t.Swap.Options with { Auto = false, RebuffMarginSeconds = 300 }, t.Swap.R.H.Log, endSession: false) { Precondition = null };
        var status = t.Run(scenario);

        var lines = t.Swap.R.H.Log.Lines.ToList();
        Assert.True(status == ScenarioStatus.Succeeded, lines.LastOrDefault(l => l.Contains("ERROR", StringComparison.Ordinal)));
        Assert.Equal(0, scenario.CycleResult!.Revitalizes);
        Assert.InRange(scenario.CycleResult.TransfersTried, 0, BuffCycle.FighterTransferCap);
        Assert.Contains(lines, l => l.Contains("fighter mana order: no Mana Potion to drink", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("mana-futile", StringComparison.Ordinal));
    }

    [Fact]
    public void The_fighter_profile_is_the_ones_mana_transfer_line_and_asks_for_transfers_ahead_the_mages_does_not()
    {
        foreach (var profile in FighterProfile.All)
        {
            Assert.Same(profile.StaminaToMana, ((IBuffProfile)profile).ManaTransferLine);
            Assert.Equal(BuffProfile.TransferLineName, profile.StaminaToMana.Name);
            Assert.True(((IBuffProfile)profile).TransfersAhead);
        }

        Assert.False(((IBuffProfile)ArenaProfile.Buffs).TransfersAhead);
    }
}

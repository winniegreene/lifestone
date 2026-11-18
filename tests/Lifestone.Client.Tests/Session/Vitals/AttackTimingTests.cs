using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 attack timing (the owner, 2026-11-15, after the second death on the Tusker Holding's approach: "YOU HAVE THE MOBS STATS. YOU HAVE THE MATH. STOP GUESSING."): each creature's seconds between attacks and hits a second, from
/// ACE's monster attack loop (@95ef426d: <c>Monster_Melee.MeleeAttack</c>'s <c>NextAttackTime = PrevAttackTime + animLength + U(0, PowerupTime)</c>, <c>GetCombatManeuver</c>'s height and maneuver at random, <c>DoSwingMotion</c>'s
/// <c>MotionTable.GetAnimationLength</c> at <c>GetAnimSpeed</c>, <c>GetAttackFrames</c>'s strikes, the 0.2 s <c>Monster_Tick</c>; <c>Monster_Missile.LaunchMissile</c>; <c>Monster_Magic.TryRollSpell</c> and <c>MagicAttack</c>), the
/// world database's facts (<c>creatures.json</c>'s timing columns) and the user's dats (the motion and combat tables, read at run time). The pinned numbers are the dats' frames and framerates for each maneuver (the dat tests check them).
/// </summary>
public sealed class AttackTimingTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>
    /// The Infected Assailer (27800) as ACE times it: Quickness 200, PowerupTime 2.5; motion 0x0900000C in HandCombat: AttackHigh1 60 frames, AttackMed1 40, AttackLow1 50 (the punch: combat table 0x3000000B has no kick at low), each at 30
    /// frames a second, one attack hook each.
    /// </summary>
    internal static AttackTiming InfectedAssailer => Melee(27800, "Infected Assailer", 200, 2.5, [60 / 30.0, 40 / 30.0, 50 / 30.0]);

    /// <summary>The Reaper (22747): Quickness 190, PowerupTime 1 (the weenie's float 34); motion 0x0900001A in HandCombat: AttackHigh1, AttackMed1 and AttackLow1 24 frames each at 30, one attack hook each.</summary>
    internal static AttackTiming Reaper => Melee(22747, "Reaper", 190, 1.0, [24 / 30.0, 24 / 30.0, 24 / 30.0]);

    /// <summary>The Tusker Guard (1629): Quickness 180, PowerupTime 2.6; the Assailers' motion 0x0900000C and combat table 0x3000000B.</summary>
    internal static AttackTiming TuskerGuard => Melee(1629, "Tusker Guard", 180, 2.6, [60 / 30.0, 40 / 30.0, 50 / 30.0]);

    /// <summary>A bare-handed melee creature's timing by ACE's terms: each of its three maneuvers (one height in three, <c>GetCombatManeuver</c>) at <c>GetAnimSpeed</c>, then <c>U(0, PowerupTime)</c>, started on the monster tick.</summary>
    private static AttackTiming Melee(uint wcid, string name, int quickness, double powerup, double[] raw)
    {
        var speed = AttackTimings.AnimSpeed(quickness);
        var seconds = raw.Average(r => AttackTimings.TickMean(r / speed, powerup));
        return new AttackTiming(wcid, name, seconds, 1, 1, [], string.Create(System.Globalization.CultureInfo.InvariantCulture, $"pinned: the dats' maneuvers {string.Join(", ", raw.Select(r => r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))} s at speed {speed:0.###}, PowerupTime {powerup}"))
        {
            MeleeAnimSeconds = raw.Average(),
            PowerupSeconds = powerup,
        };
    }

    // ------------------------------------------------------------------------------------------------ ACE's terms, without the dats

    [Fact]
    public void ACE_s_GetAnimSpeed_is_one_over_one_less_Quickness_over_300_plus_the_monster_weapon_speed_40_over_150_within_half_and_double()
    {
        Assert.Equal(1 / 0.6, AttackTimings.AnimSpeed(200), 9); // the Assailers: 1 - 200 / 300 + 40 / 150 = 0.6
        Assert.Equal(1 / (1 - (190 / 300.0) + (40 / 150.0)), AttackTimings.AnimSpeed(190), 9); // the Reaper 22747: 1.579
        Assert.Equal(1.5, AttackTimings.AnimSpeed(180), 9); // the Tusker Guard and the Reedshark Ravager
        Assert.Equal(2.0, AttackTimings.AnimSpeed(280), 9); // the Assailer 22582: 1 / 0.3 clamped to MaxAttackSpeed 2
        Assert.Equal(2.0, AttackTimings.AnimSpeed(450), 9); // the Reaper 27504: the divisor under 0 is MaxAttackSpeed
        Assert.Equal(1 / (1 + (40 / 150.0)), AttackTimings.AnimSpeed(0), 9); // no Quickness: 0.789
        Assert.Equal(40, AttackTimings.MonsterWeaponSpeed); // WorldObject_Weapon.GetWeaponSpeed reads the weapon of `wielder as Player`: none for a monster, ACE's defaultSpeed
    }

    [Fact]
    public void The_next_attack_starts_on_the_first_0_2_s_monster_tick_past_the_animation_and_a_uniform_0_to_PowerupTime_delay()
    {
        Assert.Equal(0.2, AttackTimings.MonsterTickSeconds); // Monster_Tick.monsterTickInterval
        Assert.Equal(1.0, AttackTimings.TickMean(1.0, 0), 9); // on a tick: no wait
        Assert.Equal(1.0, AttackTimings.TickMean(0.9, 0), 9); // past one: the next
        foreach (var (a, p) in new[] { (1.2, 2.5), (0.8, 2.5), (0.5067, 1.0), (1.3333, 2.6), (0.4, 1.0) })
        {
            const int n = 200_000;
            var sum = 0.0;
            for (var i = 0; i < n; i++)
            {
                sum += 0.2 * Math.Ceiling((a + (p * (i + 0.5) / n)) / 0.2);
            }

            Assert.Equal(sum / n, AttackTimings.TickMean(a, p), 4); // the closed form is the mean of the rounding over U(0, p)
            Assert.InRange(AttackTimings.TickMean(a, p) - (a + (p / 2)), 0.08, 0.12); // the animation, half the PowerupTime and about half a tick
        }
    }

    [Fact]
    public void The_Infected_Assailer_27800_attacks_every_2_352_s_its_animations_1_2_0_8_and_1_0_s_at_GetAnimSpeed_1_667_plus_U0_PowerupTime_2_5_on_the_tick_one_strike_each()
    {
        var t = InfectedAssailer;
        Assert.Equal((2.552 + 2.152 + 2.352) / 3, t.Seconds, 3); // AttackHigh1 2.0 s / 1.667 = 1.2 s + 1.25 + the tick's 0.1; AttackMed1 0.8 s; AttackLow1 1.0 s
        Assert.Equal(2.352, t.Seconds, 3);
        Assert.Equal(0.425, t.HitsPerSecond, 3); // one hit a 2.35 s: the live "about two a second" (17 hits in 11.9 s, two of them 1 ms apart) were 3 to 4 of them
        Assert.Equal(1.667, t.MeleeAnimSeconds, 3);
    }

    [Fact]
    public void The_Reaper_22747_attacks_every_1_107_s_its_animations_0_8_s_at_GetAnimSpeed_1_579_plus_U0_PowerupTime_1_on_the_tick_one_strike_each()
    {
        var t = Reaper;
        Assert.Equal(1.107, t.Seconds, 3); // 0.8 s / 1.579 = 0.507 s + 0.5 + the tick's 0.1
        Assert.Equal(0.904, t.HitsPerSecond, 3);
    }

    [Fact]
    public void The_Tusker_Guard_1629_attacks_every_2_511_s_its_animations_2_0_1_333_and_1_667_s_at_GetAnimSpeed_1_5_plus_U0_PowerupTime_2_6_on_the_tick_one_strike_each()
    {
        var t = TuskerGuard;
        Assert.Equal((2.7333 + 2.2889 + 2.5111) / 3, t.Seconds, 3); // 1.333, 0.889 and 1.111 s + 1.3 + the tick's 0.1
        Assert.Equal(2.511, t.Seconds, 3);
        Assert.Equal(0.398, t.HitsPerSecond, 3);
    }

    [Fact]
    public void The_creature_table_holds_ACE_s_timing_facts_and_the_spell_books()
    {
        var assailer = CreatureTable.Find(27800)!.Timing!;
        Assert.Equal((0x0900000Cu, 0x3000000Bu, (double?)2.5, (double?)null, 200), (assailer.MotionTable, assailer.CombatTable, assailer.PowerupTime, assailer.MagicDelay, assailer.Quickness));
        Assert.Empty(assailer.Spells);
        Assert.False(assailer.Thrown || assailer.Launcher || assailer.MeleeWeapon);
        Assert.Equal((double?)1.0, CreatureTable.Find(22747)!.Timing!.PowerupTime);
        Assert.Equal(180, CreatureTable.Find(1629)!.Timing!.Quickness);

        var gunner = CreatureTable.Find(22521)!.Timing!;
        Assert.True(gunner.Thrown); // its Coconut: a missile, ACE's IsRanged
        Assert.Equal(0x80, gunner.WeaponCombatStyle); // CombatStyle.ThrownWeapon: the ThrownWeaponCombat stance

        var wasp = CreatureTable.Find(7105)!.Timing!;
        Assert.Equal((double?)3.0, wasp.MagicDelay);
        Assert.Equal([(72u, "Frost Bolt IV", 2.2), (136u, "Frost Volley IV", 2.2)], wasp.Spells.Select(s => (s.Id, s.Name, s.Probability)).ToArray());
        Assert.All(wasp.Spells, s => Assert.Equal(0.2, s.Chance, 6)); // TryRollSpell: a probability over 2 is p - 2
        Assert.Equal((0x8u, 52, 53, 1), (wasp.Spells[0].Element, wasp.Spells[0].BaseIntensity, wasp.Spells[0].Variance, wasp.Spells[0].Projectiles));
        Assert.Equal(3, wasp.Spells[1].Projectiles);
    }

    [Fact]
    public void Without_the_dats_a_creature_is_the_named_2_75_s_fallback_never_a_silent_one()
    {
        var t = CreatureAttackTimes.None.Of(27800, "Infected Assailer");
        Assert.False(t.Derived);
        Assert.Equal(AttackTimings.UnderivedSwingSeconds, t.Seconds);
        Assert.StartsWith("TODO(f11-swing-seconds): 'Infected Assailer' (weenie 27800) is not derived (no dats at hand)", t.Todo, StringComparison.Ordinal);
        Assert.Contains("TODO(f11-swing-seconds)", t.Short(), StringComparison.Ordinal);
        Assert.StartsWith("TODO(f11-swing-seconds): 'creature' (weenie 999999) is not derived (no creature of the world database's table)", CreatureAttackTimes.None.Of(999_999, "creature").Todo, StringComparison.Ordinal);
        var pinned = CreatureAttackTimes.Fixed([InfectedAssailer]);
        Assert.Same(pinned.Of(27800, "Infected Assailer"), pinned.Of(27800, "Infected Assailer"));
        Assert.Equal(InfectedAssailer.Seconds, pinned.Of(27800, "x").Seconds, 9);
    }

    [Fact]
    public void The_line_from_the_attackers_counts_each_at_its_own_hits_a_second_and_a_caster_s_spells()
    {
        // two Infected Assailers on her through the live acid factor 0.0162 ("hit 439 x factor 0.0162"): the mean hit with its criticals 0.9 x 439 + 0.1 x 1404 = 535.5, at 0.425 hits a second each
        var fact = CreatureFacts.All.Find(27800)!;
        AttackerRead One(uint guid) => new(guid, 27800, "Infected Assailer", fact, 0x20, 0.0462, 0.35) { Timing = InfectedAssailer };
        var line = FightLines.DeriveAttackers(206, [One(1), One(2)], 0, 0);
        Assert.Equal(2 * 535.5 * 0.0462 * 0.35 * InfectedAssailer.HitsPerSecond, line.ModelPerSecond, 6);
        Assert.Contains("an attack every 2.35 s, 0.43 hits/s", line.Why(), StringComparison.Ordinal);
        Assert.Contains("their hits a second are", line.Why(), StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(f11-swing-seconds)", line.Why(), StringComparison.Ordinal);

        // a caster's priced spells join its hits: Frost Bolt IV at 52 to 105 (a critical adds half its top at 5%) on her cold resistance
        var bolt = new SpellAttack(72, "Frost Bolt IV", 0.2, 52 + 26.5 + (0.05 * 0.5 * 105), 105 * 1.5, 0x8, null);
        var wasp = new AttackTiming(7105, "White Phyntos Wasp", 2.0, 0.8, 1, [bolt], "test");
        Assert.Equal(0.2 * 81.125 * 0.35 / 2.0, wasp.SpellPerSecond(_ => 0.35), 9);
        Assert.Equal(157.5 * 0.35, wasp.LargestSpellHit(_ => 0.35), 9);
        Assert.Equal((10 * 0.5 * 0.4) + (0.2 * 81.125 * 0.35 / 2.0), wasp.PerSecond(10, 0.5, _ => 0.35), 9);
    }

    // ------------------------------------------------------------------------------------------------ the dats

    [RequiresDatsFact]
    public void The_dats_derive_the_Infected_Assailer_Reaper_and_Tusker_Guard_as_pinned_by_ACE_s_terms()
    {
        var times = CreatureAttackTimes.FromDats(new DatAnimationSource(fixture.Dats));
        foreach (var pinned in new[] { InfectedAssailer, Reaper, TuskerGuard })
        {
            var t = times.Of(pinned.Wcid, pinned.Name);
            output.WriteLine(t.Why);
            Assert.True(t.Derived, t.Why);
            Assert.Null(t.Todo);
            Assert.Equal(pinned.Seconds, t.Seconds, 9);
            Assert.Equal(pinned.HitsPerSecond, t.HitsPerSecond, 9);
            Assert.Equal(1, t.StrikesPerPhysical, 9); // one attack hook in each maneuver (GetAttackFrames)
            Assert.Equal(pinned.MeleeAnimSeconds, t.MeleeAnimSeconds, 9);
            Assert.Contains("3 maneuver(s) over 3 height(s)", t.Why, StringComparison.Ordinal);
        }

        var motion = new DatAnimationSource(fixture.Dats).GetCombatTable(0x3000000B)!;
        Assert.Equal([0x1u, 0x1u, 0x1u], motion.Rows.Select(r => r.AttackType).ToArray()); // the tuskers punch at every height: ACE swaps the low kick for the punch

        var src = new DatAnimationSource(fixture.Dats);
        foreach (var (mt, ct) in new[] { (0x09000008u, 0x30000004u), (0x09000009u, 0x30000005u), (0x09000195u, 0x30000000u) })
        {
            var styles = src.GetMotionTable(mt)!.Links.Keys.Select(k => k >> 16).Distinct().Select(k => $"0x{k:X}");
            var rows = src.GetCombatTable(ct)!.Rows.Select(r => $"0x{r.Style:X8}/h{r.AttackHeight}/t0x{r.AttackType:X}").Distinct();
            output.WriteLine($"motion 0x{mt:X8} link styles {string.Join(" ", styles)}; default 0x{src.GetMotionTable(mt)!.DefaultStyle:X8}; combat 0x{ct:X8}: {string.Join(" ", rows)}");
        }
    }

    /// <summary>The others the brief names, derived and printed: the Reaper 27504, the Assailers 22053 and 22582, the Tuskie Gunner 22521 (a thrower), the White Phyntos Wasp 7105 (a caster) and the Reedshark Ravager 22746.</summary>
    [RequiresDatsFact]
    public void The_dats_derive_the_approach_s_other_attackers()
    {
        var times = CreatureAttackTimes.FromDats(new DatAnimationSource(fixture.Dats));
        var want = new (uint Wcid, double Seconds, double Hits)[] { (27504, 1.0, 1.0), (22053, 2.352, 0.425), (22582, 2.184, 0.458), (22521, 3.2, 0.3125), (7105, 2.093, 0.306), (22746, 1.133, 0.882) };
        foreach (var (wcid, seconds, hits) in want)
        {
            var t = times.Of(wcid, "?");
            output.WriteLine($"{wcid} {t.Name}: {t.Seconds:0.###} s, {t.HitsPerSecond:0.###} hits/s; {t.Why}");
            Assert.True(t.Derived, t.Why);
            Assert.Equal(seconds, t.Seconds, 3);
            Assert.Equal(hits, t.HitsPerSecond, 3);
        }

        // the Gunner's coconut: the aim 0.867 s, no reload, the link back to Ready 1.2 s and MissileDelay 1 s: 3.067 s, on the tick 3.2 s
        Assert.Contains("a missile in stance 0x80000047", times.Of(22521, "?").Why, StringComparison.Ordinal);

        // the Wasp: Frost Bolt IV at 0.2 and Frost Volley IV at 0.8 x 0.2 of its decisions, cast no sooner than AiUseMagicDelay 3 s after the attack before
        var wasp = times.Of(7105, "?");
        Assert.Equal(0.64, wasp.PhysicalShare, 6);
        Assert.Equal([0.2, 0.16], wasp.Spells.Select(s => Math.Round(s.Share, 6)).ToArray());
        Assert.Equal(52 + 26.5 + (0.05 * 0.5 * 105), wasp.Spells[0].MeanDamage, 6);
        Assert.Equal(3 * (20 + 10 + (0.05 * 0.5 * 40)), wasp.Spells[1].MeanDamage, 6); // every projectile of the volley counted, the safe side
        Assert.Equal(0x8u, wasp.Spells[0].Element);
    }

    [RequiresDatsFact]
    public void Every_creature_of_the_table_with_a_motion_and_a_combat_table_derives()
    {
        var times = CreatureAttackTimes.FromDats(new DatAnimationSource(fixture.Dats));
        var (derived, underived, approximate) = (0, new List<string>(), 0);
        foreach (var row in CreatureTable.All)
        {
            var t = times.Of(row.Wcid, row.Name);
            if (!t.Derived)
            {
                underived.Add(t.Todo!);
                continue;
            }

            derived++;
            approximate += t.Todo is null ? 0 : 1;
            Assert.True(t.Seconds is >= 0.2 and <= 600, t.Why);
            Assert.InRange(t.StrikesPerPhysical, 0, 10);
        }

        output.WriteLine($"{derived} derived ({approximate} with a named approximation), {underived.Count} not: {string.Join(" | ", underived.Take(12))}");
        Assert.All(underived, u => Assert.StartsWith(AttackTimings.UnderivedTodo, u, StringComparison.Ordinal)); // each named, its reason in words
        var tabled = CreatureTable.All.Where(r => r.Timing is { MotionTable: > 0, CombatTable: > 0 }).ToList();
        var missed = tabled.Where(r => !times.Of(r.Wcid, r.Name).Derived).ToList();
        output.WriteLine($"{tabled.Count} with both tables, {missed.Count} of them not derived: {string.Join(", ", missed.Select(r => r.Name).Distinct(StringComparer.Ordinal))}");
        Assert.True(missed.Count * 20 < tabled.Count, $"{missed.Count} of {tabled.Count} with both tables are not derived"); // props and NPCs whose motion table has no combat stance (ACE's monster never melees: GetCombatManeuver returns null), each named; the rest have no combat table
    }

    // ------------------------------------------------------------------------------------------------ the exactness checks

    /// <summary>
    /// (a) ACE's Magic Defense of a creature is <c>CreatureSkill.Current</c>: init_Level + ranks + <c>AttributeFormula.GetFormula</c>'s round((Focus + Self) / 7) (the dats' SkillTable formula for skill 15); the table holds the Reaper's
    /// 325 + round(260 / 7) = 362 and the Infected Assailer's 282 + round(140 / 7) = 302. An untrained or absent Magic Defense is usable untrained (the dats' min level 1, <c>CreatureSkill.IsUsable</c>): its formula share counts, not 0.
    /// </summary>
    [Fact]
    public void A_creature_s_Magic_Defense_is_init_plus_ranks_plus_round_Focus_plus_Self_over_7_and_untrained_it_keeps_the_formula_s_share()
    {
        Assert.Equal(362, CreatureTable.Find(22747)!.MagicDefence); // 325 + round((140 + 120) / 7 = 37.1)
        Assert.Equal(302, CreatureTable.Find(27800)!.MagicDefence); // 282 + round((80 + 60) / 7 = 20)
        Assert.Equal(325 + (int)Math.Round(160 / 7.0, MidpointRounding.AwayFromZero), CreatureTable.Find(27504)!.MagicDefence); // 325 + round((80 + 80) / 7 = 22.9)
        Assert.Equal(12, CreatureTable.MagicDefenceOf(-1, -1, -1, 40, 44)); // no row: ACE's untrained skill, round(84 / 7)
        Assert.Equal(12 + 5, CreatureTable.MagicDefenceOf(5, 0, 1, 40, 44)); // untrained with an init_Level
        Assert.Equal(5, CreatureTable.MagicDefenceOf(5, 0, 0, 40, 44)); // inactive: unusable, its init_Level only
        Assert.Equal(1, CreatureTable.MagicDefenseMinLevel);
    }

    [RequiresDatsFact]
    public void The_dats_SkillTable_gives_Magic_Defense_Self_plus_Focus_over_7_usable_untrained()
    {
        var md = Lifestone.Data.CharacterCreation.SkillTableData.Load(fixture.Dats).Skills.Single(s => s.Id == 15);
        Assert.Equal(GroundDraft.MagicDefenseFormula, md.Formula);
        Assert.Equal(CreatureTable.MagicDefenseMinLevel, (int)md.MinLevel);
    }

    /// <summary>
    /// (b) Her skill in ACE's resist roll is <c>GetCreatureSkill(spell.School).Current</c> (<c>WorldObject_Magic.TryResistSpell</c>): War Magic for a bolt, Life Magic for a drain, Creature Enchantment for her debuffs; ACE's Current is
    /// (the formula on her current Focus and Self + init + ranks) x the skill's multiplicative enchantments x her vitae + its additive ones, rounded (<c>CreatureSkill.Current</c>). The client's read (<see cref="GearCasts.TryCurrentSkill"/>,
    /// which the guard's kill side and drains and the fight's picks read) is the same.
    /// </summary>
    [Fact]
    public void Her_skill_in_the_resist_roll_is_ACE_s_Current_with_her_vitae_after_the_multipliers_and_before_the_additives()
    {
        var state = new GameState();
        var q = state.Self.Qualities;
        q.IsKnown = true;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 90, 100, 0); // Focus 190
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 80, 100, 0); // Self 180
        foreach (var skill in new[] { MageTemplate.WarMagic, MageTemplate.LifeMagic, MageTemplate.CreatureEnchantment })
        {
            q.Skills[skill] = new SkillDesc(skill, 200, 0, SkillRules.TrainedClass, 0, 10, 0, 0.0);
        }

        const uint additive = EnchantmentRules.BeneficialKind | EnchantmentRules.Additive | 0x1000 | SkillRules.SkillKind;
        const uint multiplicative = EnchantmentRules.BeneficialKind | EnchantmentRules.Multiplicative | 0x1000 | SkillRules.SkillKind;
        var plus = new EnchantmentDesc(2215, 1, 50, 0, 300, 0.0, 1800.0, 1, 0f, 0f, 0.0, additive, MageTemplate.WarMagic, 40f, 0, EnchantmentBucket.Additive); // War Magic Mastery-like +40
        var times = new EnchantmentDesc(9999, 1, 51, 0, 300, 0.0, 1800.0, 1, 0f, 0f, 0.0, multiplicative, MageTemplate.WarMagic, 1.1f, 0, EnchantmentBucket.Multiplicative);
        var vitae = new EnchantmentDesc(666, 1, 0, 0, 0, 0.0, -1, 1, 0f, 0f, 0.0, 0x800000 | 0x4000 | 0x2000 | 0x2, 0, 0.9f, 0, EnchantmentBucket.Vitae);
        q.Enchantments = [plus, times, vitae];

        // ACE: round((round((190 + 180) / 4) + 10 + 200) x 1.1 x 0.9 + 40) = round(303 x 0.99 + 40) = 340
        Assert.True(GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var war, out _));
        Assert.Equal((uint)Math.Round(((Math.Round(370 / 4.0, MidpointRounding.AwayFromZero) + 210) * 1.1f * 0.9f) + 40, MidpointRounding.AwayFromZero), war);
        Assert.Equal(340u, war);

        // Life Magic and Creature Enchantment: the vitae alone, round(303 x 0.9) = 273
        Assert.True(GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out _));
        Assert.True(GearCasts.TryCurrentSkill(q, MageTemplate.CreatureEnchantment, out var creature, out _));
        Assert.Equal((273u, 273u), (life, creature));

        // and the roll she makes with it: ACE's SkillCheck.GetSkillChance (0.03) against the Reaper's 362 and the Infected Assailer's 302
        Assert.Equal(1 / (1 + Math.Exp(-0.03 * (340 - 362.0))), KillRate.LandChance(war, 362), 6);
        Assert.Equal(1 / (1 + Math.Exp(-0.03 * (273 - 302.0))), DrainPicker.PassChance(life, 302), 6);

        // with no vitae the same skill reads 303 x 1.1 + 40 = 373: the vitae is in the read
        q.Enchantments = [plus, times];
        Assert.True(GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var unhurt, out _));
        Assert.Equal(373u, unhurt);
    }
}

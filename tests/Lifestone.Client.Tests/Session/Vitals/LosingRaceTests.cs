using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Autopilot;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 the losing race (live 2026-11-15 04:12:20 to 04:13:28 UTC, <c>cache/m6/14/live/ladder25/step3-levelup.log</c>): the climber, level 69 and 206 health, fought on foot on the Tusker Holding's approach with an Assailer, three Tuskie Gunners,
/// a Plated Tusker and a Tusker Guard on her (hits of 2 to 15, criticals of 24 and 29: roughly 10 to 20 a second). From 04:13:09 (health 121, under the guard's line of 124) the travel vitals guard held the state and cast Drain Health Other I
/// eight times in a row, each landing 8 to 12, while her health fell to 0; its pick read the Assailer at "target health 571" in every pick (the server's 0.75 of 760, never refreshed) after her bolts had burnt it 192, so it predicted 11 to
/// 23 health a second and got about 6; the travel leg's line read the Tuskie Gunner (weenie 22521) as unknown, the general fallback. The three faults and their fixes, on the death's numbers.
/// </summary>
public sealed class LosingRaceTests
{
    private const uint HealthMax = 206; // the climber at level 69

    private const double DrainOneSeconds = 1.6; // Drain Health Other I, measured live 1.56 to 1.6 s (the log's "landed after 1 tries ... 1.57 s")

    // ------------------------------------------------------------------------------------------------ fault 1: the guard heals a losing race and never leaves

    [Fact]
    public void The_way_out_is_Lifestone_Recall_s_measured_cast_and_ACE_s_recall_delay_and_the_fight_lines_use_it()
    {
        Assert.Equal(2.45, RecallWayOut.CastSeconds); // the median of the 135 live casts
        Assert.Equal(2.0, RecallWayOut.AceDelaySeconds); // HandleCastSpell_PortalRecall's AddDelaySeconds(2.0f)
        Assert.Equal(4.45, RecallWayOut.Seconds, 6);
        Assert.Equal(RecallWayOut.Seconds, FightLines.LeaveSeconds);
        Assert.Equal(RecallWayOut.Seconds + AutopilotVitalsGuard.RetrySeconds, AutopilotVitalsGuard.HoldOffSeconds);
    }

    [Fact]
    public void The_0413_death_replay_hands_over_at_121_while_the_recall_is_covered()
    {
        // the death's numbers: about 15 a second coming in from six attackers all alive, the drains giving about 6 a second, a Tuskie Gunner critical of 29 measured, her health falling about 10 a second; the kill side: the Assailer read
        // at 216, three full Tuskie Gunners (200 each), the Tusker Guard at 208 after her 192 and the Plated Tusker, about 1,100 left, at her bolts' 37 a second (192, 189, 192 and 163 over six casts of 3.3 s)
        var at121 = new LosingRaceInput(121, 15, 29, 6, DrainOneSeconds, RecallWayOut.Seconds) { TrendPerSecond = -10, IncomingAllPerSecond = 15, Attackers = 6, AttackersHealth = 1100, KillPerSecond = 736.0 / (6 * 3.3) };
        var first = LosingRace.Decide(in at121);
        Assert.True(first.Losing); // falling, and 6 a second is under 15: every heal only slows the loss
        Assert.False(first.KillsFirst); // 1,100 at 37 a second is 29.6 s, against the (121 - 95.75) / 15 = 1.7 s her health gives before the way out's need
        Assert.Equal(29.6, first.KillSeconds, 1);
        Assert.Equal(1.68, first.LiveSeconds, 2);
        Assert.True(first.Lost); // both sides fail: she leaves now, no more heals (a heal under the incoming gains nothing while she cannot kill)
        Assert.Equal(95.75, first.Need, 2); // 15 x 4.45 + 29
        var words = LosingRace.Words(in at121, in first);
        Assert.StartsWith("lost: her health 121 falls 10/s over the last 6 s; the heal she can sustain, 6/s, is under the incoming 15/s", words, StringComparison.Ordinal);
        Assert.Contains("6 attacker(s) with 1100 health left at her 37.2 damage a second: killed in 29.6 s, against the 1.7 s her health gives at the full incoming before the way out's need of 96", words, StringComparison.Ordinal);
        Assert.Contains("the way out (4.45 s (Lifestone Recall's cast 2.45 s, the live median, and ACE's recall delay 2 s)) need", words, StringComparison.Ordinal);

        // in time: the recall's 4.45 s at 15 a second and the largest hit leave her 25 (live, the guard drained on to 0 at 04:13:27)
        Assert.True(at121.Health - (15 * RecallWayOut.Seconds) - 29 > 0, "the recall lands before the health runs out");
    }

    /// <summary>
    /// The lead, 2026-11-15 (live ladder27 06:17:00 to 06:17:15 UTC, the second death): an Infected Assailer (760 health, acid hits of 3 to 19 about two a second, first heard 06:17:03) and a Reaper (517, Magic Defense 362; 2 to 3 a hit) on her;
    /// health 167 (06:17:01), 122 (06:17:07), 76 (06:17:10.9), 50 (06:17:11.9), dead at 06:17:15; her bolt at the Reaper resisted, her Drain Health Other I at the Assailer resisted twice; the guard never logged losing.
    /// </summary>
    [Fact]
    public void The_0617_death_replay_hands_over_at_122_with_the_resists_in_both_sides()
    {
        // the heal: Drain Health Other I at the Infected Assailer, its cast's chance times ACE's resist roll of her Life Magic 361 against its Magic Defense 302 (282 + (80 + 60) / 7), 60 a landing, the session's 2.6 s a cast and its
        // 10 mana at the dance's 0.09 s a point; Heal Self VI is under it (87.5 a cast, 70 mana)
        var drainLand = 0.9 * KillRate.LandChance(361, 302);
        Assert.Equal(0.854, KillRate.LandChance(361, 302), 3);
        var drain = HealPick.Sustained(new HealCandidate("Drain Health Other I", 0x8000E0B8, 60, 30, drainLand, 10, 2.6), Lifestone.Client.Session.Mage.FightRates.SeedSecondsPerMana);
        var healSelf = HealPick.Sustained(new HealCandidate("Heal Self VI", 0, 84, 0, 0.9, 70, 3.1), Lifestone.Client.Session.Mage.FightRates.SeedSecondsPerMana);
        Assert.InRange(drain, 13, 13.5);
        Assert.True(healSelf < drain);

        // the kill side: her bolts' 180 a landing over 3.4 s a cast; the Reaper's 362 lets a half of them through (resisted at 06:17:07), the Assailer's 302 lets 0.86
        Assert.Equal(0.5, KillRate.LandChance(362, 362), 6);
        var reaper = 517 / (180 * KillRate.LandChance(362, 362) / 3.4);
        var assailer = 760 / (180 * KillRate.LandChance(362, 302) / 3.4);
        var at122 = new LosingRaceInput(122, (2 * 8.8) + (2.5 / 1.3), 19, drain, 2.6, RecallWayOut.Seconds)
        {
            TrendPerSecond = (122 - 167) / 5.7,
            IncomingAllPerSecond = 19.5,
            Attackers = 2,
            AttackersHealth = 517 + 760,
            KillPerSecond = (517 + 760) / (reaper + assailer),
        };
        var answer = LosingRace.Decide(in at122);
        Assert.True(answer.Falling);
        Assert.False(answer.HealHolds); // 13.2 a second against the Assailer's two hits a second of 8.8 and the Reaper's
        Assert.False(answer.KillsFirst); // 36 s of bolts against the 0.8 s her health gives before the way out's need
        Assert.True(answer.Lost); // she leaves at 122
        Assert.True(at122.Health - (at122.IncomingPerSecond * RecallWayOut.Seconds) - 19 > 0, "the recall lands with her alive: 122 less 4.45 s at 19.5 and a critical of 19");

        // the measured resists then take over: two resisted drains at the Assailer leave its landing share at a third of the formula's
        var heals = new MeasuredHeals();
        heals.NoteTry("Drain Health Other I", "Infected Assailer", landed: false);
        heals.NoteTry("Drain Health Other I", "Infected Assailer", landed: false);
        Assert.Equal(drainLand / 3, heals.LandShare("Drain Health Other I", "Infected Assailer", drainLand), 6);
        Assert.Equal(drainLand, heals.LandShare("Drain Health Other I", "White Phyntos Wasp", drainLand), 6); // another kind keeps the formula
    }

    /// <summary>
    /// M6 14 attack timing (the owner, 2026-11-15: every fight number computed, never guessed): the 06:17 death replayed hit by hit from the log (<c>cache/m6/14/live/ladder27/step3-levelup.log</c>, 06:17:01.630 to 06:17:14.379: two Reapers' nicks of 2 to 3 and the
    /// Infected Assailers' blisters of 3 to 19), the guard reading each kind at its derived rate: ACE's attack loop over the dats, an Infected Assailer one hit every 2.35 s (<see cref="AttackTimingTests.InfectedAssailer"/>), a Reaper every
    /// 1.11 s (<see cref="AttackTimingTests.Reaper"/>), on her live factors (acid 0.0162, pierce 0.016: her armour's 0.046 and the bot's protections' 0.35). One Assailer lands 0.43 hits a second, so the log's 17 hits in 11.9 s (two of them
    /// 1 ms apart) were several: <paramref name="assailers"/> of the approach's camp of 4 on her. The guard first read the race at 06:17:07.285, under its line of 124 (live: "at health 122/206 (line 124"), against the live drain's 13.2 a
    /// second (Drain Health Other I at the Assailer, as the death's first replay prices it). Live it read the Assailers' measured 9.3 a second (their new read over at 4.2 s and five hits) and the Reapers' 1.4, held the race and drained on to
    /// the death; at the derived rates of the ones on her it hands the race over at once, and the logged hits of the recall's 4.45 s leave her alive.
    /// </summary>
    /// <param name="assailers">The Infected Assailers on her (the log proves 2; the approach's camp is 4).</param>
    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    public void The_0617_replay_at_the_derived_attack_timing_hands_over_in_time_for_the_recall(int assailers)
    {
        (double At, string Who, uint Damage)[] log =
        [
            (1.630, "Reaper", 2), (3.096, "Infected Assailer", 8), (3.729, "Infected Assailer", 9), (4.363, "Reaper", 2), (5.846, "Reaper", 2), (6.046, "Infected Assailer", 7), (6.463, "Infected Assailer", 8),
            (7.279, "Infected Assailer", 7), (7.280, "Reaper", 2), (8.746, "Reaper", 2), (8.946, "Infected Assailer", 12), (9.796, "Infected Assailer", 19), (9.996, "Reaper", 2), (10.196, "Infected Assailer", 3),
            (10.396, "Infected Assailer", 6), (10.612, "Reaper", 2), (11.029, "Infected Assailer", 7), (11.030, "Infected Assailer", 9), (11.246, "Reaper", 3), (11.679, "Infected Assailer", 7), (12.079, "Reaper", 3),
            (12.512, "Infected Assailer", 6), (12.712, "Reaper", 3), (13.746, "Infected Assailer", 17), (13.962, "Infected Assailer", 5), (13.963, "Reaper", 2), (14.379, "Infected Assailer", 12),
        ];
        const uint GuardLine = 124; // the guard's line live ("line 124")
        var heal = HealPick.Sustained(new HealCandidate("Drain Health Other I", 0x8000E0B8, 60, 30, 0.9 * KillRate.LandChance(361, 302), 10, 2.6), Lifestone.Client.Session.Mage.FightRates.SeedSecondsPerMana);
        var (handed, at, left, read) = Replay(log, assailers, AttackTimingTests.InfectedAssailer, AttackTimingTests.Reaper, GuardLine, heal);
        Assert.True(handed, $"the guard never called the race lost: at 06:17:{at:00.000} with {left}: {LosingRace.Words(read, LosingRace.Decide(read))}");
        Assert.Equal(7.280, at, 3); // its first read under the line: 122, where live it drained
        Assert.Equal(122.0, left);
        Assert.True(read.IncomingWhy?.Contains("on her while measured under 10 s (" + assailers.ToString(System.Globalization.CultureInfo.InvariantCulture) + " x an attack every 2.35 s, 0.43 hits/s)", StringComparison.Ordinal) == true, read.IncomingWhy);
        Assert.Contains("'Reaper' 5.1/s", read.IncomingWhy, StringComparison.Ordinal); // two Reapers at 0.9 hits a second each, not the 2.75 s's 0.36
        var duringRecall = log.Where(h => h.At > at && h.At <= at + RecallWayOut.Seconds).Sum(h => h.Damage);
        Assert.True(left - duringRecall > 0, $"handed over at 06:17:{at:00.000} with {left}; the logged hits of the recall's {RecallWayOut.Seconds} s take {duringRecall}");
        Assert.Equal(72, duringRecall); // 06:17:07.28 to 11.73: she lands at the lifestone with 50
        Assert.True(left > (read.IncomingPerSecond * RecallWayOut.Seconds) + read.BiggestHit, LosingRace.Words(read, LosingRace.Decide(read))); // the race's own need is under her health too

        // the 2.75 s fallback read the same moment: one Assailer on her and the Reapers at a hit every 2.75 s stay under the drain (the live verdict)
        var (oldHanded, _, _, oldRead) = Replay(log, 1, null, null, GuardLine, heal, untilSeconds: 7.3);
        Assert.False(oldHanded, LosingRace.Words(oldRead, LosingRace.Decide(oldRead)));
    }

    /// <summary>The replay's rig: the guard over the log's hits, her health falling by them, the race read at each hit once her health is under <paramref name="line"/>, her heal the live one; the first lost race and its read.</summary>
    private static (bool Handed, double At, double Health, LosingRaceInput Read) Replay((double At, string Who, uint Damage)[] log, int assailers, AttackTiming? assailer, AttackTiming? reaper, uint line, double heal, double untilSeconds = 20)
    {
        var (rig, watch) = Shape(health: 169); // 167 after the Reaper's 2 at 06:17:01.630 ("health 167/206" at .651)
        using (watch)
        {
            var here = rig.R.H.State.Self.Position!.Value;
            rig.R.H.State.Self.Qualities.Enchantments = [Ench(2101, 901, BodyArmourType, 0, 1376f), Ench(2100, 902, ResistType, 69, 0.35f), Ench(2102, 903, ResistType, 65, 0.35f)]; // body armour 1376: ACE's 66.67 / (1376 + 66.67) = 0.0462
            Spawn(rig.R.H, 0x8000E1AE, 22747, "Reaper", here.Origin + new Vector3(2.8f, 0, 0));
            Spawn(rig.R.H, 0x8000E1AD, 22747, "Reaper", here.Origin + new Vector3(-2.8f, 0, 0));
            for (var i = 0; i < assailers; i++)
            {
                Spawn(rig.R.H, 0x8000E0B8 + (uint)i, 27800, "Infected Assailer", here.Origin + new Vector3(0, 3.1f + i, 0));
            }

            var times = assailer is null || reaper is null ? CreatureAttackTimes.None : CreatureAttackTimes.Fixed([assailer, reaper]);
            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { Times = times };
            var (t, health, next) = (1.5, 169.0, 0);
            LosingRaceInput last = default;
            while (next < log.Length && log[next].At <= untilSeconds)
            {
                rig.R.Tick(0.05);
                t += 0.05;
                while (next < log.Length && log[next].At <= t)
                {
                    var hit = log[next++];
                    Hit(rig, hit.Who, hit.Damage);
                    health -= hit.Damage;
                    rig.Caster.SetHealth((uint)health);
                    rig.R.Tick();
                    if (health < line && guard.Race(rig.View, rig.Now, out var input, out _))
                    {
                        last = input with { HealPerSecond = heal, NextCastSeconds = 2.6 }; // the live drain, not the rig's caster
                        if (LosingRace.Decide(in last).Lost)
                        {
                            return (true, hit.At, health, last);
                        }
                    }
                }

                guard.Sample(rig.View, rig.Now);
            }

            return (false, t, health, last);
        }
    }

    private const uint ResistType = 0x2000000 | EnchantmentRules.Multiplicative | 0x1000 | FightLines.FloatKind;
    private const uint BodyArmourType = 0x2000000 | EnchantmentRules.Additive | 0x2000 | FightLines.BodyArmourKind;

    private static EnchantmentDesc Ench(ushort spell, ushort category, uint type, uint key, float value) =>
        new(spell, 1, category, 0, 300, 0, 240, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, (type & EnchantmentRules.Additive) != 0 ? EnchantmentBucket.Additive : EnchantmentBucket.Multiplicative);

    [Fact]
    public void A_new_attacker_is_read_at_once_at_its_derived_rate_not_diluted_in_the_ten_seconds()
    {
        var (rig, watch) = Shape(health: 167);
        using (watch)
        {
            var here = rig.R.H.State.Self.Position!.Value;
            Spawn(rig.R.H, 0x8000E1AE, 22747, "Reaper", here.Origin + new Vector3(2.8f, 0, 0));
            Spawn(rig.R.H, 0x8000E0B8, 27800, "Infected Assailer", here.Origin + new Vector3(0, 3.1f, 0));
            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log);
            for (var i = 0; i < 4; i++)
            {
                Hit(rig, "Reaper", 2); // 06:17:01 to 06:17:05: the Reaper's nicks
                rig.R.Tick(1.3);
            }

            Hit(rig, "Infected Assailer", 8); // 06:17:03: its first hit
            Assert.True(guard.Race(rig.View, rig.Now, out var input, out _));
            Assert.Contains("'Infected Assailer' ", input.IncomingWhy, StringComparison.Ordinal);
            Assert.Contains(", derived ", input.IncomingWhy, StringComparison.Ordinal); // one hit: its derived rate until enough are measured
            Assert.True(input.IncomingPerSecond > watch.Read(rig.Now).PerSecond, $"{input.IncomingPerSecond} against the ten seconds' read {watch.Read(rig.Now).PerSecond}");
            Assert.True(watch.KindRead("Infected Assailer", rig.Now, out var damage, out var hits, out var since, out var living, out var held));
            Assert.Equal((8.0, 1, 1, true), (damage, hits, living, held));
            Assert.Equal(0, since, 6);
        }
    }

    [Fact]
    public void Her_bolts_land_by_ACE_s_resist_roll_and_her_measured_resists_take_over()
    {
        var rate = new KillRate(new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>());
        Assert.Equal(0.5, rate.LandShare("Reaper", KillRate.LandChance(362, 362)), 6);
        rate.NoteResist("Reaper"); // 06:17:07.162 "Reaper resists your spell"
        Assert.Equal(0.25, rate.LandShare("Reaper", 0.5), 6); // (0 + 0.5) / (1 + 1)
        rate.NoteHit(10, 157, "Reaper");
        Assert.Equal((1, 1), rate.Of("Reaper"));
        Assert.Equal(1.5 / 3, rate.LandShare("Reaper", 0.5), 6);
        Assert.Equal(0.99998, KillRate.LandChance(362, 0), 5); // M6 16 E1e: a Magic Defense of 0 is rolled as ACE rolls it (ResistRoll), the curve of her skill alone; it read exactly 1 before
        Assert.Equal("White Phyntos Wasp", KillRate.TargetOf("You incinerate White Phyntos Wasp for 157 points with Ilservian's Flame."));
        Assert.Null(KillRate.TargetOf("White Phyntos Wasp chills you for 23 points with Frost Bolt IV."));
    }

    /// <summary>
    /// The lead, 2026-11-15 (live ladder27 06:03:47 to 06:04:10 UTC): two White Phyntos Wasps cast Frost Bolt IV at her (23 to 36 a hit, a Frost Volley of 14, melee 0 to 2); health 206 to 111 by 06:03:57, the first of three wasps dead at
    /// 06:03:56; the guard's drains took her 111 to 133 to 166 while her bolt had burnt the second wasp 157; at 166 and rising the old race read 15.7 heal against 16.2 incoming and recalled her from a fight she was winning.
    /// </summary>
    [Fact]
    public void The_wasp_replay_does_not_leave_at_111_falling_nor_at_166_rising()
    {
        // 06:03:57, health 111 and falling about 13 a second from 176 at 06:03:52; the window's hits were 58 while three wasps lived and 30 after one died: the incoming of the living is 16.4 x (58 x 2/3 + 30) / 88 = 12.8
        var share = ((58 * 2.0 / 3) + 30) / 88;
        var at111 = new LosingRaceInput(111, 16.37 * share, 35, 0.9 * 60 / DrainOneSeconds, DrainOneSeconds, RecallWayOut.Seconds)
        {
            TrendPerSecond = -13,
            IncomingAllPerSecond = 16.37,
            Attackers = 2,
            AttackersHealth = 81 + 238, // the second wasp 238 less her 157, the third full: the creature table's 238 less her lines
            KillPerSecond = 144 / (3.68 + 3.08), // her one damage line over her two casts' seconds (the killing blow wrote no damage line: the safe side)
        };
        var falling = LosingRace.Decide(in at111);
        Assert.True(falling.Falling);
        Assert.True(falling.HealHolds); // Drain Health Other I on a full wasp, 60 a cast, holds the two wasps left
        Assert.False(falling.Losing || falling.Lost);
        Assert.Equal("holding", LosingRace.Verdict(in falling));
        Assert.Contains("(16.4/s with the dead attackers' hits)", LosingRace.Words(in at111, in falling), StringComparison.Ordinal);

        // 06:04:07, health 166 and rising 11.7 a second from 96 at 06:04:01; the hits of the window all after the death (the share is 1), 16.2 a second; the heal measured at 15.7 a second; the wasps left read 81 and 156
        var at166 = new LosingRaceInput(166, 16.2, 36, 15.7, 3.9, RecallWayOut.Seconds) { TrendPerSecond = (166 - 96) / 6.0, IncomingAllPerSecond = 16.2, Attackers = 2, AttackersHealth = 81 + 156, KillPerSecond = (144 + 157) / (3.68 + 3.08 + 4.37) };
        var rising = LosingRace.Decide(in at166);
        Assert.False(rising.Losing || rising.Lost); // rising is not losing, whatever the two rates say
        Assert.Equal("winning", LosingRace.Verdict(in rising));
        Assert.StartsWith("winning: her health 166 rises 11.7/s over the last 6 s; the heal she can sustain, 15.7/s, is under the incoming 16.2/s", LosingRace.Words(in at166, in rising), StringComparison.Ordinal);
        Assert.True(LosingRace.Decide(at166 with { TrendPerSecond = null }).Lost); // the live read, on the two rates alone: (166 - 36) / 16.2 = 8.0 s under the 8.4 s, and she recalled
    }

    [Fact]
    public void Losing_the_heal_race_but_killing_first_she_fights_on()
    {
        var input = new LosingRaceInput(120, 10, 20, 5, 1.6, RecallWayOut.Seconds) { TrendPerSecond = -6, IncomingAllPerSecond = 10, Attackers = 1, AttackersHealth = 60, KillPerSecond = 30 };
        var answer = LosingRace.Decide(in input);
        Assert.True(answer.Losing);
        Assert.True(answer.KillsFirst); // 60 at 30 a second is 2 s, against (120 - 64.5) / 10 = 5.6 s
        Assert.True(answer.FightsOn);
        Assert.False(answer.Lost);
        Assert.Equal("fights-on", LosingRace.Verdict(in answer));

        Assert.False(LosingRace.Decide(input with { KillPerSecond = null }).KillsFirst); // her damage not measured: no kill to count on
        Assert.False(LosingRace.Decide(input with { AttackersHealth = null }).KillsFirst); // an attacker whose health is known nowhere
        Assert.False(LosingRace.Decide(input with { Attackers = 0 }).KillsFirst); // no attacker read on her
    }

    [Fact]
    public void Her_health_s_trend_is_the_slope_of_the_last_six_seconds()
    {
        var trend = new HealthTrend();
        Assert.False(trend.TryRead(0, out _, out _));
        for (var t = 0.0; t <= 10; t += 0.1)
        {
            trend.Note(t, t < 4 ? 206 - (10 * t) : 166 + (11.7 * (t - 4))); // falling to 166, then her drains lift her
        }

        Assert.True(trend.TryRead(10, out var slope, out var span));
        Assert.Equal(11.7, slope, 1); // the last six seconds only
        Assert.InRange(span, HealthTrend.WindowSeconds - 1, HealthTrend.WindowSeconds); // the samples of the window, one each 0.2 s
        var fresh = new HealthTrend();
        fresh.Note(0, 100);
        fresh.Note(1, 90);
        Assert.False(fresh.TryRead(1, out _, out _)); // a second of samples is one hit, not a trend
    }

    [Fact]
    public void Her_damage_a_second_is_her_damage_lines_over_her_war_casts_seconds()
    {
        var spells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo> { [2128] = WorldActionRig.Spell(2128, DatReaderWriter.Enums.SpellType.Projectile) with { Name = "Ilservian's Flame", School = DatReaderWriter.Enums.MagicSchool.WarMagic }, [1237] = WorldActionRig.Spell(1237, DatReaderWriter.Enums.SpellType.Transfer) with { Name = "Drain Health Other I", School = DatReaderWriter.Enums.MagicSchool.LifeMagic } };
        var rate = new KillRate(spells);
        var log = new ActionLog();
        var chat = new ChatLog();
        rate.Read(log, chat); // the first read marks where it starts
        log.Add(new ActionLogEntry(401, ActionKind.CastSpell, ActionSource.Automation, "Cast spell=2128 target=0x8000B23B", new ActionOutcome(ActionOutcomeKind.Confirmed), 100, 103.68));
        chat.Add(new ChatLine(103.7, ChatKind.System, FightTally.MagicChatType, null, 0, "You incinerate White Phyntos Wasp for 144 points with Ilservian's Flame."));
        log.Add(new ActionLogEntry(402, ActionKind.CastSpell, ActionSource.Automation, "Cast spell=2128 target=0x8000B23B", new ActionOutcome(ActionOutcomeKind.Confirmed), 103.7, 106.78)); // the killing blow: a death line, no damage line
        log.Add(new ActionLogEntry(403, ActionKind.CastSpell, ActionSource.Automation, "Cast spell=1237 target=0x8000B292", new ActionOutcome(ActionOutcomeKind.Sent), 107, 110.9)); // a drain: Life Magic, not counted
        rate.Read(log, chat);
        Assert.True(rate.TryBolt(107, out var mean, out var seconds, out var hits, out var casts));
        Assert.Equal((2, 1, 144.0), (casts, hits, mean));
        Assert.Equal(3.38, seconds, 2); // the two war casts' mean; the drain is Life Magic, not counted
        Assert.True(rate.TryAgainst(107, "White Phyntos Wasp", KillRate.LandChance(362, 263), out var perSecond));
        Assert.Equal(144 * rate.LandShare("White Phyntos Wasp", KillRate.LandChance(362, 263)) / 3.38, perSecond, 3);
        Assert.False(rate.TryBolt(107 + KillRate.WindowSeconds + 5, out _, out _, out _, out _)); // nothing cast in the window: not measured
        Assert.Equal(2128u, KillRate.SpellOf("Cast spell=2128 target=0x8000B23B"));
        Assert.Equal(1635u, KillRate.SpellOf("Cast spell=1635"));
        Assert.Null(KillRate.SpellOf("Use item=0x80001234"));
    }

    [Fact]
    public void A_dead_attacker_s_hits_drop_out_of_the_incoming()
    {
        var (rig, watch) = Shape(health: 150);
        using (watch)
        {
            uint[] wasps = [0x8000B292, 0x8000B1E5, 0x8000B23B];
            foreach (var w in wasps)
            {
                Spawn(rig.R.H, w, 7105, "White Phyntos Wasp");
            }

            Hit(rig, "White Phyntos Wasp", 23);
            rig.R.Tick(1);
            Hit(rig, "White Phyntos Wasp", 35);
            rig.R.Tick(1);
            Assert.Equal(3, watch.Living("White Phyntos Wasp"));
            Assert.Equal(1.0, watch.LivingShare(rig.Now), 6);
            rig.R.H.R.Reducer.OnUpdateHealth(rig.R.H.R.Context, rig.R.H.Header, new UpdateHealthEvent(0x8000B23B, 0f)); // 06:03:56: reduced to cinders
            Hit(rig, "White Phyntos Wasp", 30); // from one of the two alive
            Assert.Equal(2, watch.Living("White Phyntos Wasp"));
            Assert.Equal(((58 * 2.0 / 3) + 30) / 88, watch.LivingShare(rig.Now), 6);
            Assert.Equal(watch.IncomingPerSecond(rig.Now) * watch.LivingShare(rig.Now), watch.IncomingOfLiving(rig.Now), 6);
            Hit(rig, "Nobody's Shadow", 10); // a kind the table never held: counted whole
            Assert.Equal(((58 * 2.0 / 3) + 30 + 10) / 98, watch.LivingShare(rig.Now), 6);
        }
    }

    [Fact]
    public void A_race_she_wins_or_no_incoming_never_leaves()
    {
        Assert.False(LosingRace.Decide(new LosingRaceInput(40, 15, 29, 30, DrainOneSeconds, RecallWayOut.Seconds)).Losing); // a full Tuskie Gunner's drain, 60 a cast, holds 15 a second
        var quiet = LosingRace.Decide(new LosingRaceInput(10, 0, 0, 0, DrainOneSeconds, RecallWayOut.Seconds));
        Assert.False(quiet.Losing || quiet.Lost);
        Assert.True(double.IsPositiveInfinity(quiet.SecondsLeft));
    }

    [Fact]
    public void The_survival_layer_leaves_a_lost_race_before_any_hold_and_only_where_it_may()
    {
        var lines = LadderLoader.WarMage.Survival;
        var lost = new SurvivalInput { HealthPercent = 51, ManaPercent = 91, StaminaPercent = 90, HostileNear = true, Attacked = true, HealPayable = true, GuardRaceLost = true, RaceWhy = "the numbers", MayLeaveRace = true, MayWait = true, Attackers = [] };
        var leave = SurvivalRule.Decide(in lost, lines);
        Assert.Equal((SurvivalVerdict.Leave, SurvivalReason.LosingRace), (leave.Verdict, leave.Reason)); // mana 91%, a heal payable: the no-mana leave would never fire
        Assert.EndsWith("the race is lost: the numbers", leave.Why, StringComparison.Ordinal);

        var home = SurvivalRule.Decide(lost with { MayLeaveRace = false }, lines); // on her way home: no leave here, a hostile near: the activity goes on
        Assert.Equal(SurvivalVerdict.Go, home.Verdict);
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(lost with { GuardRaceLost = false }, lines).Verdict);
    }

    [Fact]
    public void The_race_s_road_is_the_survival_layer_s_or_the_retreat_rule_s_and_only_while_the_recall_is_paid()
    {
        var paid = (true, "mana 357 of 79 needed, the components held");
        Assert.Equal((true, "the survival layer's leave by Lifestone Recall (mana 357 of 79 needed, the components held)"), AutopilotScenario.RaceRoadOf("travel", survival: true, ground: false, paid));
        Assert.Equal((true, "the retreat rule's by Lifestone Recall (mana 357 of 79 needed, the components held)"), AutopilotScenario.RaceRoadOf("known", survival: false, ground: true, paid));
        Assert.Equal((false, "Lifestone Recall is not payable: mana 40 under 79"), AutopilotScenario.RaceRoadOf("travel", true, false, (false, "mana 40 under 79")));
        Assert.StartsWith("the travel state takes no leave for it here", AutopilotScenario.RaceRoadOf("travel", false, false, paid).Why, StringComparison.Ordinal);

        Assert.True(AutopilotScenario.RaceLeaveStateOf(AutopilotState.Travel, homeward: false)); // the rung change's way out: the death's state
        Assert.False(AutopilotScenario.RaceLeaveStateOf(AutopilotState.Travel, homeward: true)); // the recall home is the way out already
        Assert.True(AutopilotScenario.RaceLeaveStateOf(AutopilotState.Trip, homeward: false));
        Assert.False(AutopilotScenario.RaceLeaveStateOf(AutopilotState.Fight, homeward: false)); // the fight's is the retreat rule's losing-race arm
        Assert.Equal(RetreatRule.LosingToken, AutopilotScenario.RaceToken);
    }

    [Fact]
    public void The_attack_s_largest_hit_outlasts_the_ten_second_window_until_the_attack_goes_quiet()
    {
        var (rig, watch) = Shape(health: 150);
        using (watch)
        {
            Hit(rig, "Tuskie Gunner", 29); // 04:12:49: the critical
            var end = rig.Now + 12;
            while (rig.Now < end)
            {
                rig.R.Tick(1);
                Hit(rig, "Assailer", 7); // the attack goes on, 7 a hit
            }

            Assert.True(watch.Read(rig.Now).BiggestHit < 29); // out of the ten seconds' window
            Assert.Equal(29, watch.LargestOfAttack(rig.Now)); // still this attack's: it can come again in the way out
            rig.R.Tick(HostileWatch.AttackQuietSeconds + 1);
            Assert.Equal(0, watch.LargestOfAttack(rig.Now)); // quiet: the attack is over
            Hit(rig, "Assailer", 5);
            Assert.Equal(5, watch.LargestOfAttack(rig.Now)); // a new attack counts from its own hits
        }
    }

    [Fact]
    public void The_guard_measures_its_drain_s_gain_and_tells_the_target_the_take()
    {
        var (rig, watch) = Shape(health: 80);
        using (watch)
        {
            var here = rig.R.H.State.Self.Position!.Value;
            Spawn(rig.R.H, Assailer, 22053, "Assailer", here.Origin + new Vector3(1.2f, 0, 0));
            Hit(rig, "Assailer", 7);
            var walk = new KnownTargetHealth(Assailer, 22053, "Assailer", 760, 290, "tusker-holding:approach max_health", rig.R.H.State.Chat.Total);
            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { WalkTarget = () => walk };
            Run(rig, guard, until: _ => rig.Log.Lines.Any(l => l.Contains("guard: travel: measured: Drain Health Other", StringComparison.Ordinal)), seconds: 20, hits: false);
            var measured = rig.Log.Lines.FirstOrDefault(l => l.Contains("guard: travel: measured: Drain Health Other", StringComparison.Ordinal));
            Assert.True(measured is not null, string.Join(" | ", rig.Log.Lines.Where(l => l.Contains("guard", StringComparison.Ordinal)).TakeLast(6)));
            Assert.Contains("on Assailer 0x800069E9 gave ", measured, StringComparison.Ordinal);
            Assert.True(walk.Dealt > 0); // the take is the target's: its health falls with her drains
            Assert.True(walk.Health(rig.R.H.State) < 760);
        }
    }

    [Fact]
    public void The_retreat_rule_takes_a_lost_race_at_once_over_its_line_but_waits_for_a_finishing_bolt()
    {
        var rule = new RetreatRule();
        var input = new RetreatInput(55, 15, 6, false, false, 20, 100) { Losing = true }; // health 55% over the 20% line
        var now = rule.Decide(in input);
        Assert.Equal((true, RetreatRule.LosingToken), (now.Retreat, now.Why)); // no hold of RetreatHoldSeconds: a hold at 15 a second is the way out's health
        Assert.Contains(" losing-race -> retreat", RetreatRule.Line(in input, in now), StringComparison.Ordinal);
        Assert.False(new RetreatRule().Decide(input with { Finishing = true }).Retreat); // the bolt that kills the only attacker stops the incoming
        Assert.False(new RetreatRule().Decide(input with { RetreatPercent = 0 }).Retreat); // a ground that never retreats keeps its order
        Assert.False(new RetreatRule().Decide(input with { Losing = false }).Retreat); // over the line, not losing: the old order
    }

    [Fact]
    public void The_sustained_heal_is_what_she_gains_and_the_landing_share_is_measured()
    {
        var drain = new HealCandidate("Drain Health Other I", 1, 12, 6, 0.8, 10, 1.6);
        Assert.Equal(0.8 * 12 / 1.6, HealPick.Sustained(in drain), 6); // no damage credit, no mana time
        Assert.True(HealPick.PerSecond(in drain, 0, HealPick.DefaultDamageWeight) > HealPick.Sustained(in drain));

        var heals = new MeasuredHeals();
        Assert.Equal(0.8, heals.LandShare("Drain Health Other I", 0.8)); // nothing measured: the model's chance
        heals.NoteTry("Drain Health Other I", landed: false); // the first cast resisted
        Assert.Equal(0.4, heals.LandShare("Drain Health Other I", 0.8), 6); // (0 + 0.8) / 2
        foreach (var landed in new[] { true, true, false, true, true, true, false, true, true, true, true, false, true, true, true })
        {
            heals.NoteTry("Drain Health Other I", landed);
        }

        Assert.Equal((16, 12), heals.TriesOf("Drain Health Other I")); // the death's sixteen drains at the Assailer, four resisted
        Assert.Equal((12 + 0.8) / 17, heals.LandShare("Drain Health Other I", 0.8), 6);
    }

    [Fact]
    public void The_guard_hands_a_lost_race_to_the_leave_and_holds_off_its_heals_while_the_recall_is_cast()
    {
        var (rig, watch) = Shape(health: 100);
        using (watch)
        {
            var asked = 0;
            Warm(rig);
            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { LeaveRoad = () => (++asked > 0, "the survival layer's leave by Lifestone Recall (test)") };
            var handed = Run(rig, guard, until: g => g.RaceLost, seconds: 20, hits: true, fallPerHit: 2); // 40 a second coming in, her health falling 4 a second
            Assert.True(handed, string.Join(" | ", rig.Log.Lines.Where(l => l.Contains("guard", StringComparison.Ordinal)).TakeLast(8)));
            Assert.False(guard.Holding); // the state is let go: the leave runs
            Assert.True(asked > 0);
            Assert.Contains(rig.Log.Lines, l => l.Contains("guard: travel: the race is lost at health ", StringComparison.Ordinal) && l.Contains(" falls ", StringComparison.Ordinal) && l.Contains("the hold is released and the leave is asked: the survival layer's leave by Lifestone Recall (test)", StringComparison.Ordinal));
            Assert.DoesNotContain(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell)); // no heal was started for a race it had lost

            // the autopilot takes the leave: the flag is down and no heal starts while the recall is cast
            guard.RaceTaken(rig.Now);
            Assert.False(guard.RaceLost);
            Assert.True(guard.HoldingOff(rig.Now));
            Run(rig, guard, until: _ => false, seconds: RecallWayOut.Seconds, hits: true);
            Assert.DoesNotContain(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
            Assert.Contains(rig.Log.Lines, l => l.Contains("guard: travel: holds off", StringComparison.Ordinal) || l.Contains("no heal starts for", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_guard_keeps_healing_a_race_her_rising_health_says_she_is_winning()
    {
        var (rig, watch) = Shape(health: 100);
        using (watch)
        {
            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { LeaveRoad = () => (true, "the survival layer's leave by Lifestone Recall (test)") };
            var health = 70.0;
            var end = rig.Now + 3;
            var first = true;
            while (rig.Now < end)
            {
                Hit(rig, "Assailer", first ? 29u : 20u); // 40 a second coming in, as the losing tests; her drains outrun it
                first = false;
                for (var i = 0; i < 5; i++)
                {
                    health += 1.5; // 15 a second up: the live 111 to 166 of 06:03:57 to 06:04:07
                    rig.Caster.SetHealth((uint)health);
                    rig.R.Tick(0.1);
                    guard.Sample(rig.View, rig.Now);
                }
            }

            Run(rig, guard, until: g => g.RaceLost || rig.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell)), seconds: 20, hits: true);
            Assert.False(guard.RaceLost); // rising is not losing, whatever the two rates say
            Assert.Contains(rig.Log.Lines, l => l.Contains("guard: travel: race at health ", StringComparison.Ordinal) && l.Contains(": winning: her health ", StringComparison.Ordinal) && l.Contains(" rises ", StringComparison.Ordinal));
            Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("the race is lost", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void With_no_leave_open_the_guard_says_so_and_heals_on()
    {
        var (rig, watch) = Shape(health: 100);
        using (watch)
        {
            Warm(rig);
            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { LeaveRoad = () => (false, "Lifestone Recall is not payable: test") };
            Run(rig, guard, until: _ => rig.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell)), seconds: 20, hits: true, fallPerHit: 2);
            Assert.False(guard.RaceLost);
            Assert.Contains(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell)); // it slows the loss
            Assert.Single(rig.Log.Lines, l => l.Contains("but no leave from here (Lifestone Recall is not payable: test): she heals on", StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------------------------------------------ fault 2: the drain's estimate read a stale target health

    private const uint Assailer = 0x800069E9;

    [Fact]
    public void The_Assailer_after_a_192_bolt_reads_379_not_the_stale_571_and_a_fresh_report_re_bases_it()
    {
        var h = new ActionHarness();
        h.R.EnterWorld();
        Spawn(h, Assailer, 22053, "Assailer");
        var known = new KnownTargetHealth(Assailer, 22053, "Assailer", 760, 290, "tusker-holding:approach max_health", h.State.Chat.Total);
        Assert.Equal(760u, known.Health(h.State)); // no report, nothing dealt: its facts' maximum

        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(Assailer, 571f / 760)); // 04:12:43: the server's 0.75 after her 189 bolt
        Assert.Equal(571u, known.Health(h.State));
        h.ServerMessage("You burn Assailer for 192 points with Ilservian's Flame.", (int)FightTally.MagicChatType); // 04:13:04; the server sent no new fraction
        Assert.Equal(379u, known.Health(h.State)); // the report's 571 less the 192 since it, not the stale 571 of the live picks
        Assert.Contains("the server's report of 571 less the 192 she dealt it since", known.Words(h.State), StringComparison.Ordinal);

        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(Assailer, 0.5f)); // a fresh report holds what she dealt before it
        Assert.Equal(380u, known.Health(h.State));
        h.ServerMessage("You scorch Assailer for 163 points with Ilservian's Flame.", (int)FightTally.MagicChatType);
        Assert.Equal(217u, known.Health(h.State));

        known.NoteTake(AutopilotVitalsGuard.DrainTake(10, DrainPicker.RowOf(1237)!.Value)); // "You gain 10 points of health due to casting Drain Health Other I on Assailer": it took 5
        Assert.Equal(212u, known.Health(h.State));
    }

    [Fact]
    public void A_drain_s_take_is_its_gain_over_one_minus_its_loss_cut_to_its_cap()
    {
        Assert.Equal(30, AutopilotVitalsGuard.DrainTake(60, DrainPicker.RowOf(1237)!.Value), 6); // Drain Health Other I: LossPercent -1, the caster gains twice the take
        Assert.Equal(100 / 1.33, AutopilotVitalsGuard.DrainTake(100, DrainPicker.RowOf(1239)!.Value), 4); // III: -0.33
        Assert.Equal(100, AutopilotVitalsGuard.DrainTake(100, DrainPicker.RowOf(1240)!.Value), 6); // IV: no loss
    }

    [Fact]
    public void A_tracking_that_is_not_the_fight_back_target_counts_only_its_own_takes_and_carries_into_the_next_target()
    {
        var h = new ActionHarness();
        h.R.EnterWorld();
        Spawn(h, 0x800071BA, 22521, "Tuskie Gunner");
        var tracked = new KnownTargetHealth(0x800071BA, 22521, "Tuskie Gunner", 200, 255, "the world database's creature table", h.State.Chat.Total) { CountsLines = false };
        h.ServerMessage("You burn Tuskie Gunner for 192 points with Ilservian's Flame.", (int)FightTally.MagicChatType); // a bolt at another Tuskie Gunner
        Assert.Equal(200u, tracked.Health(h.State));
        tracked.NoteTake(30); // the guard's own drain at it
        Assert.Equal(170u, tracked.Health(h.State));

        var target = new KnownTargetHealth(0x800071BA, 22521, "Tuskie Gunner", 200, 255, "the world database's creature table", h.State.Chat.Total); // the fight-back takes it for its target
        target.CarryFrom(tracked);
        Assert.Equal(170u, target.Health(h.State));
        h.ServerMessage("You burn Tuskie Gunner for 100 points with Ilservian's Flame.", (int)FightTally.MagicChatType);
        Assert.Equal(70u, target.Health(h.State));
    }

    [Fact]
    public void A_carry_on_the_same_report_keeps_what_was_dealt_since_it_and_a_newer_report_holds_it()
    {
        var h = new ActionHarness();
        h.R.EnterWorld();
        Spawn(h, Assailer, 22053, "Assailer");
        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(Assailer, 571f / 760));
        var tracked = new KnownTargetHealth(Assailer, 22053, "Assailer", 760, 290, "the guard's tracking", h.State.Chat.Total) { CountsLines = false };
        Assert.Equal(571u, tracked.Health(h.State));
        tracked.NoteTake(30); // 04:12:42: Drain Health Other I gave 60
        var target = new KnownTargetHealth(Assailer, 22053, "Assailer", 760, 290, "the fight-back's target", h.State.Chat.Total);
        Assert.Equal(571u, target.Health(h.State)); // the leg's words read the same report
        target.CarryFrom(tracked);
        Assert.Equal(541u, target.Health(h.State)); // the report less the take since it

        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(Assailer, 0.5f));
        var fresh = new KnownTargetHealth(Assailer, 22053, "Assailer", 760, 290, "a later fight-back", h.State.Chat.Total);
        Assert.Equal(380u, fresh.Health(h.State)); // a newer report
        fresh.CarryFrom(target);
        Assert.Equal(380u, fresh.Health(h.State)); // it holds what was dealt before it: nothing is carried twice
    }

    [Fact]
    public void The_guard_drains_a_full_Tuskie_Gunner_heard_attacking_her_over_the_Assailer_drained_to_a_few_points()
    {
        var (rig, watch) = Shape(health: 80);
        using (watch)
        {
            var here = rig.R.H.State.Self.Position!.Value;
            Spawn(rig.R.H, Assailer, 22053, "Assailer", here.Origin + new Vector3(1.2f, 0, 0));
            Spawn(rig.R.H, 0x800071BA, 22521, "Tuskie Gunner", here.Origin + new Vector3(0, 2f, 0));
            rig.R.H.R.Reducer.OnUpdateHealth(rig.R.H.R.Context, rig.R.H.Header, new UpdateHealthEvent(Assailer, 30f / 760)); // the Assailer at the end of the live fight: its drains gave 8 to 12
            rig.R.H.R.Reducer.OnUpdateHealth(rig.R.H.R.Context, rig.R.H.Header, new UpdateHealthEvent(0x800071BA, 1f));
            Hit(rig, "Tuskie Gunner", 5); // heard attacking her: a drain candidate though no ground of hers lists it
            Hit(rig, "Assailer", 7);
            var walk = new KnownTargetHealth(Assailer, 22053, "Assailer", 760, 290, "tusker-holding:approach max_health", rig.R.H.State.Chat.Total);
            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { WalkTarget = () => walk };
            Run(rig, guard, until: _ => rig.Log.Lines.Any(l => l.Contains("guard: travel: heal pick", StringComparison.Ordinal)), seconds: 20, hits: false);
            var pick = rig.Log.Lines.First(l => l.Contains("guard: travel: heal pick", StringComparison.Ordinal));
            Assert.Contains("on 0x800071BA", pick, StringComparison.Ordinal); // the drain offered is on the full Gunner (its table maximum, 105 + 190 / 2 = 200)
            Assert.DoesNotContain("0x800069E9", pick, StringComparison.Ordinal); // not on the Assailer the live picks drained to a few points
        }
    }

    // ------------------------------------------------------------------------------------------------ fault 3: the retreat line had an unknown creature and the way out's seconds were hand-sized

    [Fact]
    public void The_Tuskie_Gunner_is_derived_from_the_world_database_its_coconut_and_its_critical()
    {
        Assert.Null(CreatureFacts.All.Recorded(22521)); // in no ground profile
        var row = CreatureTable.Find(22521)!;
        Assert.Equal(("Tuskie Gunner", 80, 200), (row.Name, row.Level, row.MaxHealth));
        var attack = row.Attack!;
        Assert.Equal("its thrown weapon 22546", attack.Kind); // the Coconut it wields (ACE GetMissileDamage: the missile itself), harder than its body part's 55
        Assert.Equal((120, 0.5, 250, 0x4u), (attack.Hitter.DVal, attack.Hitter.DVar, attack.Hitter.Strength, attack.DamageType));
        Assert.Equal(284, attack.Hitter.Hit); // 120 x 0.75 x (1 + 195 x 0.011)
        Assert.Equal((0.1, 3.0), (attack.Hitter.CriticalChance, attack.Hitter.CriticalFactor)); // the Coconut's CriticalMultiplier 2: a critical is three times its largest hit
        Assert.Equal(1132.2, FightLines.CriticalHit(attack.Hitter), 1);

        var fact = CreatureFacts.All.Find(22521)!;
        Assert.Equal("the world database's creature table, its thrown weapon 22546", fact.Where);
        Assert.Equal(attack.Hitter, fact.Hitter);
        Assert.Null(CreatureFacts.All.Find(999_999)); // only a weenie the world database does not hold is unknown
        Assert.StartsWith("the world database's creature table: no melee or missile attack recorded", CreatureFacts.TableFact(CreatureTable.All.First(r => r.Attack is null).Wcid)!.Where, StringComparison.Ordinal);

        // the launcher's shot is on Coordination at ACE's bow factor
        var archer = CreatureTable.All.First(r => r.Attack is { Kind: var k } && k.StartsWith("its launcher", StringComparison.Ordinal)).Attack!;
        Assert.Equal(LadderHitter.BowAttributeFactor, archer.Hitter.AttributeFactor);
    }

    [Fact]
    public void The_death_s_attackers_give_a_derived_line_on_the_measured_rate_over_the_way_out_with_no_fallback()
    {
        const double factor = 0.044 * 0.35; // the leg's line: "damage 0x4 x armour 0.044 x resistance 0.35 = 0.0156"
        uint[] wcids = [22053, 1629, 22521, 22521, 22521];
        var present = wcids.Select((w, i) => new AttackerRead(0x80006900u + (uint)i, w, CreatureFacts.All.Find(w)!.Name, CreatureFacts.All.Find(w), 0x4, 0.044, 0.35)).ToArray();
        var live = FightLines.DeriveAttackers(HealthMax, present, 4.5, 20); // the leg's measure at 04:12:37: 4.5 a second, a largest of 20
        Assert.Equal(0, live.Unknown);
        Assert.False(live.Fallback); // the log's "the general fallback 50%" is gone
        Assert.True(live.ModelPerSecond > 4.5, live.Why()); // the three Gunners' coconuts carry the model over the early measure
        Assert.Contains("'Tuskie Gunner' 0x80006902 (the world database's creature table, its thrown weapon 22546: hit 284, critical 10% at x3, damage 0x4", live.Why(), StringComparison.Ordinal);

        var model = live.ModelPerSecond;
        Assert.Equal(((0.9 * 403) + (0.1 * 220 * 2.925 * 2) + (0.9 * 102) + (0.1 * 60 * 2.705 * 2) + (3 * ((0.9 * 284) + (0.1 * 120 * 3.145 * 3)))) * factor / AttackTimings.UnderivedSwingSeconds, model, 1); // no timing read (no dats in the line): the named 2.75 s fallback

        // at 04:13:09 the hits came at about 15 a second and a Tuskie Gunner's critical of 29 was measured: the line is the way out's loss
        var late = FightLines.DeriveAttackers(HealthMax, present, 15, 29);
        Assert.Equal(15, late.MeasuredPerSecond);
        Assert.Equal(29, late.Critical);
        Assert.Equal((15 * RecallWayOut.Seconds) + 29, late.Loss, 6); // 95.75 of 206
        Assert.Equal(50, late.Percent); // 46.5%, up to the step: she leaves at 103, over the live 66 she still had at 04:13:15
    }

    // ------------------------------------------------------------------------------------------------ the Zefirs (live ladder45, 2026-11-16, the Crater Pathway)

    private const uint ShockWaveVI = 69;
    private const uint KilifWcid = 2610, ZofritWcid = 2611, WispWcid = 1988;
    private const double LiveHeal = 14.4; // the heal she could sustain, the live line's "the heal she can sustain, 14.4/s"
    private const double LiveHealCast = 1.6; // the live line's "the next heal (1.6 s)"

    /// <summary>
    /// The first false recall (<c>cache/m6/14/live/ladder45/step3-levelup.log</c>, 23:07:00 to 23:07:43.661 UTC): the climber, level 79 and 218 health, fought back on a travel leg in the Crater Pathway; each of her five Shock Wave VI casts
    /// killed its Zefir outright (no damage line, only ACE's death line to the killer), the Dark Wisps' spells mostly resisted, the Zefirs' melee 0 points. The guard read 16.7 a second coming in (the burst read of 50 in 2 s), the Wisps
    /// "derived while new" after half a minute of their spells, and her bolts "not measured", and recalled her at 129. Every line of the log's window, as the guard heard it.
    /// </summary>
    private static readonly string[] At2307 =
    [
        "11.233|7|Dark Wisp cast Fester Other II on you", "11.449|7|You resist the spell cast by Dark Wisp", "12.083|7|You resist the spell cast by Dark Wisp", "14.416|7|You resist the spell cast by Dark Wisp",
        "14.616|7|You resist the spell cast by Dark Wisp", "15.232|7|You lose 0 points of stamina due to Dark Wisp casting Drain Stamina Other II on you", "17.532|7|You resist the spell cast by Dark Wisp",
        "17.749|7|You resist the spell cast by Dark Wisp", "20.066|21|Critical hit! Zofrit Zefir scratches your upper leg for 1 point of slashing damage!", "20.182|21|Kilif Zefir scratches your lower arm for 0 points of slashing damage!",
        "21.249|21|Kilif Zefir scratches your chest for 0 points of slashing damage!", "21.332|21|You evaded Zofrit Zefir!", "21.949|21|Zofrit Zefir scratches your foot for 0 points of slashing damage!", "22.066|21|You evaded Kilif Zefir!",
        "22.399|7|You resist the spell cast by Dark Wisp", "22.732|21|You evaded Kilif Zefir!", "23.866|7|You lose 0 points of health due to Dark Wisp casting Drain Health Other II on you",
        "24.066|21|Zofrit Zefir scratches your lower leg for 0 points of slashing damage!", "24.749|cast|0x800029C3|3628.4", "24.799|0|You beat Zofrit Zefir to a lifeless pulp!",
        "24.916|7|You lose 0 points of health due to Dark Wisp casting Drain Health Other II on you", "25.182|7|You resist the spell cast by Kilif Zefir", "25.549|7|You lose 0 points of health due to Dark Wisp casting Drain Health Other II on you",
        "25.899|7|You resist the spell cast by Kilif Zefir", "25.932|7|You resist the spell cast by Kilif Zefir", "26.082|21|You evaded Kilif Zefir!", "27.315|21|You evaded Kilif Zefir!", "27.632|7|Dark Wisp sparks you for 10 points with Lightning Bolt II.",
        "28.065|7|You resist the spell cast by Dark Wisp", "28.182|21|You evaded Kilif Zefir!", "28.399|21|You evaded Kilif Zefir!", "29.232|7|Kilif Zefir scratches you for 17 points with Whirling Blade II.", "29.532|cast|0x800029BE|4710.3",
        "29.599|0|You beat Kilif Zefir to a lifeless pulp!", "29.615|21|You evaded Kilif Zefir!", "30.098|7|You resist the spell cast by Dark Wisp", "30.415|21|Critical hit! Kilif Zefir scratches your abdomen for 0 points of slashing damage!",
        "31.049|21|Kilif Zefir scratches your chest for 0 points of slashing damage!", "31.132|7|You resist the spell cast by Dark Wisp", "31.465|21|Kilif Zefir scratches your upper arm for 0 points of slashing damage!",
        "31.982|21|Zofrit Zefir scratches your lower leg for 0 points of slashing damage!", "32.032|7|Dark Wisp sparks you for 12 points with Lightning Bolt II.", "32.315|21|You evaded Kilif Zefir!", "32.549|21|You evaded Kilif Zefir!",
        "33.265|21|Zofrit Zefir scratches your lower leg for 0 points of slashing damage!", "33.915|7|You resist the spell cast by Dark Wisp", "33.998|21|You evaded Kilif Zefir!", "34.283|cast|0x800029C0|4679.9",
        "34.298|7|You resist the spell cast by Dark Wisp", "34.365|0|The thunder of crushing Kilif Zefir is followed by the deafening silence of death!", "34.615|7|You resist the spell cast by Kilif Zefir",
        "35.132|21|Zofrit Zefir scratches your head for 0 points of slashing damage!", "35.148|7|You resist the spell cast by Dark Wisp", "35.248|21|You evaded Kilif Zefir!", "35.765|21|Zofrit Zefir scratches your lower leg for 0 points of slashing damage!",
        "36.298|21|Kilif Zefir scratches your lower leg for 0 points of slashing damage!", "36.415|7|Dark Wisp casts Enfeeble Other II and drains 15 points of your stamina.", "36.532|21|You evaded Kilif Zefir!", "36.532|21|You evaded Kilif Zefir!",
        "37.448|7|You resist the spell cast by Dark Wisp", "37.565|21|You evaded Kilif Zefir!", "37.566|21|You evaded Kilif Zefir!", "37.566|21|You evaded Kilif Zefir!", "38.348|7|You resist the spell cast by Dark Wisp",
        "38.598|21|You evaded Kilif Zefir!", "38.798|21|Kilif Zefir scratches your upper arm for 0 points of slashing damage!", "38.932|cast|0x800029BA|4561.9", "38.948|7|You resist the spell cast by Zofrit Zefir",
        "38.998|0|Kilif Zefir is shattered by your assault!", "39.531|7|You resist the spell cast by Dark Wisp", "39.865|21|You evaded Kilif Zefir!", "40.731|7|Kilif Zefir scratches you for 13 points with Whirling Blade II.",
        "40.932|21|You evaded Kilif Zefir!", "40.932|21|You evaded Kilif Zefir!", "41.015|21|Zofrit Zefir scratches your head for 0 points of slashing damage!", "41.448|7|Critical hit! Dark Wisp numbs you for 21 points with Frost Bolt II.",
        "41.715|7|Dark Wisp sparks you for 16 points with Lightning Bolt II.", "41.765|21|You evaded Kilif Zefir!", "41.848|21|Zofrit Zefir scratches your head for 0 points of slashing damage!", "42.565|21|You evaded Kilif Zefir!",
        "42.765|21|Kilif Zefir scratches your chest for 0 points of slashing damage!", "42.998|21|You evaded Kilif Zefir!", "43.648|cast|0x800029BB|4645.5",
    ];

    /// <summary>The second false recall (the same log, 23:14:42 to 23:15:02.351 UTC): 18.8 a second read for the living against 9.7 for all the hits, the Wisps "derived while new" again; recalled at 116.</summary>
    private static readonly string[] At2315 =
    [
        "42.464|7|You resist the spell cast by Dark Wisp", "42.466|7|You resist the spell cast by Dark Wisp", "42.681|7|You resist the spell cast by Dark Wisp", "45.998|7|Dark Wisp cast Mana Depletion Other II on you",
        "46.414|7|Dark Wisp cast Fester Other II on you", "46.415|7|You resist the spell cast by Dark Wisp", "49.564|7|You resist the spell cast by Dark Wisp", "49.565|7|You lose 0 points of health due to Dark Wisp casting Drain Health Other II on you",
        "49.898|21|You evaded Kilif Zefir!", "51.164|21|You evaded Kilif Zefir!", "51.247|21|Zofrit Zefir scratches your abdomen for 0 points of slashing damage!", "51.447|21|Zofrit Zefir scratches your chest for 0 points of slashing damage!",
        "51.781|21|Kilif Zefir scratches your head for 0 points of slashing damage!", "52.631|21|You evaded Kilif Zefir!", "52.731|7|You resist the spell cast by Dark Wisp", "52.731|7|You resist the spell cast by Dark Wisp",
        "52.848|21|You evaded Kilif Zefir!", "53.131|7|You resist the spell cast by Kilif Zefir", "53.347|7|You resist the spell cast by Dark Wisp", "53.397|cast|0x80002EEF|2448.4", "53.431|0|Kilif Zefir is shattered by your assault!",
        "53.881|21|You evaded Kilif Zefir!", "54.347|21|You evaded Kilif Zefir!", "54.564|7|Zofrit Zefir scratches you for 18 points with Whirling Blade II.", "54.781|7|Zofrit Zefir grazes you for 10 points with Shock Wave II.",
        "55.164|21|You evaded Kilif Zefir!", "55.381|21|You evaded Kilif Zefir!", "55.664|21|Zofrit Zefir scratches your chest for 0 points of slashing damage!", "55.880|7|You resist the spell cast by Dark Wisp",
        "56.080|7|You resist the spell cast by Dark Wisp", "56.414|21|You evaded Kilif Zefir!", "56.497|7|You lose 0 points of stamina due to Dark Wisp casting Drain Stamina Other II on you",
        "56.697|21|Zofrit Zefir scratches your head for 0 points of slashing damage!", "56.831|21|You evaded Kilif Zefir!", "57.447|7|You resist the spell cast by Kilif Zefir", "57.797|cast|0x80002EFB|4347.4",
        "57.830|0|You flatten Kilif Zefir's body with the force of your assault!", "57.864|21|You evaded Kilif Zefir!", "58.114|7|Zofrit Zefir singes you for 18 points with Flame Bolt II.", "58.914|21|You evaded Kilif Zefir!",
        "59.547|21|Kilif Zefir scratches your lower leg for 0 points of slashing damage!", "59.630|7|You resist the spell cast by Dark Wisp", "59.964|7|Zofrit Zefir nicks you for 12 points with Force Bolt II.",
        "59.964|7|Dark Wisp numbs you for 13 points with Frost Bolt II.", "60.180|21|You evaded Kilif Zefir!", "60.597|21|Kilif Zefir scratches your abdomen for 0 points of slashing damage!",
        "61.097|21|Critical hit! Zofrit Zefir scratches your lower leg for 0 points of slashing damage!", "61.430|21|Kilif Zefir scratches your lower leg for 0 points of slashing damage!", "61.464|7|Zofrit Zefir nicks you for 9 points with Force Bolt II.",
        "61.830|21|You evaded Kilif Zefir!", "62.114|7|Dark Wisp casts Harm Other II and drains 11 points of your health.", "62.348|cast|0x80002EF5|4513.7",
    ];

    [Fact]
    public void The_2307_Zefir_fight_reads_her_killing_bolts_and_the_measured_incoming_and_holds_at_129()
    {
        // the creatures as the log names them: seven Kilif Zefirs (three killed in the window, 0x800029BB about to be), two Zofrit Zefirs (0x800029C3 killed at 23:07:24.8), three Dark Wisps
        (uint Guid, uint Wcid, string Name, float Metres)[] creatures =
        [
            (0x800029CA, KilifWcid, "Kilif Zefir", 1.8f), (0x800029BB, KilifWcid, "Kilif Zefir", 1.8f), (0x800029BA, KilifWcid, "Kilif Zefir", 1.8f), (0x800029C4, KilifWcid, "Kilif Zefir", 2.4f), (0x800029CB, KilifWcid, "Kilif Zefir", 2.6f),
            (0x800029C0, KilifWcid, "Kilif Zefir", 1.8f), (0x800029BE, KilifWcid, "Kilif Zefir", 1.8f), (0x800029C3, ZofritWcid, "Zofrit Zefir", 1.8f), (0x800029BD, ZofritWcid, "Zofrit Zefir", 2.2f),
            (0x800029C2, WispWcid, "Dark Wisp", 9f), (0x800029C1, WispWcid, "Dark Wisp", 11f), (0x800029BC, WispWcid, "Dark Wisp", 13f),
        ];
        var (input, answer, rig) = ReplayZefirs(At2307, start: 11.0, at: 43.661, health: 129, creatures);

        // her side: the hits of the last 10 s are 13 (a Kilif's Whirling Blade II), 21 and 16 (a Dark Wisp's bolts): 5.0 a second, every kind heard over the whole window
        Assert.Equal(5.0, input.IncomingAllPerSecond, 6);
        Assert.Equal(5.0, input.IncomingPerSecond, 6); // not the 16.7 the burst read gave
        Assert.True(input.IncomingPerSecond <= input.IncomingAllPerSecond + 1e-9, input.IncomingWhy); // the living never read over the measured total
        Assert.Contains("'Dark Wisp' 3.7/s (2 hit(s) for 37 over 10 s, measured over the whole 10 s", input.IncomingWhy, StringComparison.Ordinal); // not "derived while new": its spells were heard from 23:07:11
        Assert.Contains("'Kilif Zefir' 1.3/s (", input.IncomingWhy, StringComparison.Ordinal);
        Assert.Contains("'Zofrit Zefir' 0/s (", input.IncomingWhy, StringComparison.Ordinal);
        Assert.DoesNotContain("while new", input.IncomingWhy, StringComparison.Ordinal);
        Assert.True(input.TrendPerSecond < -9, $"{input.TrendPerSecond}"); // the burst is in her trend: 179 to 129 in 1 s, then level

        // the kill side: her four killing blows in the window are landed bolts of the health each had (70, 50, 50, 50)
        Assert.Null(input.KillUnknown);
        Assert.True(input.KillPerSecond > 0, LosingRace.Words(in input, in answer));
        Assert.Contains(rig.Log.Lines, l => l.Contains("killing blow \"Kilif Zefir is shattered by your assault!\" by her Shock Wave VI at 'Kilif Zefir' 0x800029BA: counted as a landed bolt of at least 50", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Lines, l => l.Contains("killing blow \"You beat Zofrit Zefir to a lifeless pulp!\" by her Shock Wave VI at 'Zofrit Zefir' 0x800029C3: counted as a landed bolt of at least 70", StringComparison.Ordinal));

        // the verdict: the heal she can sustain holds the incoming: she heals and the quest goes on (live: "lost", recalled)
        Assert.True(answer.HealHolds);
        Assert.False(answer.Losing || answer.Lost);
        Assert.Equal("holding", LosingRace.Verdict(in answer));

        // the live read, for the record: 16.7 coming in and the kill side unread is lost
        Assert.True(LosingRace.Decide(input with { IncomingPerSecond = 16.7, KillPerSecond = null, AttackersHealth = null, KillUnknown = "not measured" }).Lost);
    }

    [Fact]
    public void The_2315_Zefir_fight_never_reads_the_living_over_the_measured_total_and_holds_at_116()
    {
        (uint Guid, uint Wcid, string Name, float Metres)[] creatures =
        [
            (0x80002EEF, KilifWcid, "Kilif Zefir", 3.9f), (0x80002EFB, KilifWcid, "Kilif Zefir", 1.8f), (0x80002EF5, KilifWcid, "Kilif Zefir", 1.8f), (0x80002EF0, KilifWcid, "Kilif Zefir", 2.2f), (0x80002EF1, KilifWcid, "Kilif Zefir", 2.4f),
            (0x80002EF2, KilifWcid, "Kilif Zefir", 2.6f), (0x80002EF3, KilifWcid, "Kilif Zefir", 2.8f), (0x80002EE1, ZofritWcid, "Zofrit Zefir", 2f), (0x80002EE2, ZofritWcid, "Zofrit Zefir", 2.2f),
            (0x80002EE3, WispWcid, "Dark Wisp", 9f), (0x80002EE4, WispWcid, "Dark Wisp", 11f), (0x80002EE5, WispWcid, "Dark Wisp", 13f),
        ];
        var (input, answer, _) = ReplayZefirs(At2315, start: 42.0, at: 62.351, health: 116, creatures);

        // the hits of the last 10 s: the Zofrits' 18, 10, 18, 12 and 9, a Wisp's 13 and its Harm Other II's 11 (the drain that took health, now a hit): 9.1 a second
        Assert.Equal(9.1, input.IncomingAllPerSecond, 6);
        Assert.True(input.IncomingPerSecond <= input.IncomingAllPerSecond + 1e-9, $"{input.IncomingPerSecond} over the measured {input.IncomingAllPerSecond}: {input.IncomingWhy}"); // live 18.8 against 9.7
        Assert.Equal(9.1, input.IncomingPerSecond, 6);
        Assert.Contains("'Dark Wisp' 2.4/s (2 hit(s) for 24 over 10 s, measured over the whole 10 s", input.IncomingWhy, StringComparison.Ordinal);
        Assert.Contains("'Zofrit Zefir' 6.7/s (", input.IncomingWhy, StringComparison.Ordinal);
        Assert.Contains("'Kilif Zefir' 0/s (", input.IncomingWhy, StringComparison.Ordinal); // every Kilif swing 0 points or evaded: not the derived 5.2
        Assert.Null(input.KillUnknown); // two killing blows in the window
        Assert.Equal("holding", LosingRace.Verdict(in answer));
        Assert.False(answer.Lost);
    }

    [Fact]
    public void A_death_line_after_her_bolt_is_a_landed_bolt_of_the_target_s_health_and_after_a_drain_it_is_not()
    {
        var spells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo> { [ShockWaveVI] = ZefirSpell(), [1237] = WorldActionRig.Spell(1237, DatReaderWriter.Enums.SpellType.Transfer) with { Name = "Drain Health Other I", School = DatReaderWriter.Enums.MagicSchool.LifeMagic } };
        var rate = new KillRate(spells);
        var log = new ActionLog();
        var chat = new ChatLog();
        rate.Read(log, chat);
        log.Add(new ActionLogEntry(1062, ActionKind.CastSpell, ActionSource.Automation, "Cast spell=69 target=0x800029BA", new ActionOutcome(ActionOutcomeKind.Confirmed), 34.37, 38.932));
        rate.Read(log, chat);
        Assert.False(rate.TryBolt(39, out _, out _, out _, out _)); // a cast, no damage line: not measured
        Assert.Equal("her bolts are not measured yet: 1 War Magic cast(s), 0 damage line(s) and 0 killing blow(s) in the last 30 s", rate.Unmeasured(39));
        Assert.True(rate.TryKillingBolt(38.998, out var spell, out var target));
        Assert.Equal((ShockWaveVI, 0x800029BAu), (spell, target));
        Assert.Equal(1, rate.CastsAt(38.998, 0x800029BA));
        rate.NoteKill(38.998, 50, "Kilif Zefir");
        Assert.True(rate.TryBolt(39, out var mean, out var seconds, out var hits, out var casts));
        Assert.Equal((50.0, 1, 1), (mean, hits, casts));
        Assert.Equal(4.562, seconds, 3);
        Assert.Equal((1, 0), rate.Of("Kilif Zefir")); // a landing on the kind
        Assert.Equal(1, rate.KillsIn(39));

        log.Add(new ActionLogEntry(1063, ActionKind.CastSpell, ActionSource.Automation, "Cast spell=1237 target=0x800029BB", new ActionOutcome(ActionOutcomeKind.Confirmed), 40, 41.6)); // a drain: Life Magic
        rate.Read(log, chat);
        Assert.False(rate.TryKillingBolt(41.7, out _, out _)); // a death line after it is not her bolt's
        Assert.True(rate.TryKillingBolt(39.5, out _, out _)); // before the drain, the bolt's
        Assert.Equal(0x800029BBu, KillRate.TargetOfCast("Cast spell=1237 target=0x800029BB"));
        Assert.Equal(0u, KillRate.TargetOfCast("Cast spell=1635"));
    }

    [Fact]
    public void The_watch_hears_misses_as_attacks_and_a_health_drain_as_a_hit()
    {
        Assert.True(HostileWatch.TryHealthLoss("You lose 7 points of health due to Dark Wisp casting Drain Health Other II on you", out var source, out var amount));
        Assert.Equal(("Dark Wisp", 7L), (source, amount));
        Assert.False(HostileWatch.TryHealthLoss("You lose 12 points of mana due to Kilif Zefir casting Drain Mana Other II on you", out _, out _));

        var (rig, watch) = Shape(health: 150);
        using (watch)
        {
            Spawn(rig.R.H, 0x80002EE3, WispWcid, "Dark Wisp");
            rig.R.H.ServerMessage("You resist the spell cast by Dark Wisp", (int)FightTally.MagicChatType);
            rig.R.Tick(3);
            rig.R.H.ServerMessage("Dark Wisp casts Harm Other II and drains 11 points of your health.", (int)FightTally.MagicChatType);
            rig.R.Tick(1);
            rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseEvaded, "Kilif Zefir", 0, 0, 0, false, 0, false, 0, "You evaded Kilif Zefir!"));
            Assert.True(watch.AttacksOf("Dark Wisp", rig.Now, out var since, out var attacks));
            Assert.Equal((4.0, 2), (Math.Round(since, 6), attacks)); // the resist began its attack
            Assert.True(watch.KindRead("Dark Wisp", rig.Now, out var damage, out var hits, out _, out _, out _));
            Assert.Equal((11.0, 1), (damage, hits)); // the Harm Other's health is a hit
            Assert.True(watch.AttacksOf("Kilif Zefir", rig.Now, out _, out var evaded));
            Assert.Equal(1, evaded);
            Assert.False(watch.KindRead("Kilif Zefir", rig.Now, out _, out _, out _, out _, out _) && watch.HeardWithin(rig.Now, 10).Contains("Kilif Zefir")); // an evade is no hit

            rig.R.H.R.Bus.Publish(new PlayerKilledSomething("You beat Dark Wisp to a lifeless pulp!"));
            Assert.Equal(1, watch.KillNotices);
            Assert.True(watch.KillNotice(0, out _, out var text));
            Assert.Equal("You beat Dark Wisp to a lifeless pulp!", text);
            Assert.Equal(1, watch.Living("Dark Wisp"));
            watch.NoteKilled(0x80002EE3);
            Assert.Equal(0, watch.Living("Dark Wisp")); // dead from her death line, before its dead motion
        }
    }

    private static Lifestone.Data.Magic.SpellInfo ZefirSpell() => WorldActionRig.Spell(ShockWaveVI, DatReaderWriter.Enums.SpellType.Projectile) with { Name = "Shock Wave VI", School = DatReaderWriter.Enums.MagicSchool.WarMagic };

    /// <summary>
    /// Replays one of the Zefir windows into the guard: the melee lines as the 0x01B2/0x01B4 notices, the Magic lines as chat, her casts as the Action API's entries (queued the logged ms before), each death line as ACE's 0x01AD (its
    /// creature's health reported 0 0.6 s later, its corpse), the fight-back's target the cast's, her health falling by the hits to <paramref name="health"/> at <paramref name="at"/>; then the race the guard reads there, on the live heal.
    /// </summary>
    private static (LosingRaceInput Input, LosingRaceAnswer Answer, FightManaTests.Rig Rig) ReplayZefirs(string[] events, double start, double at, uint health, (uint Guid, uint Wcid, string Name, float Metres)[] creatures)
    {
        var damageAll = 0.0;
        foreach (var e in events)
        {
            damageAll += HitOf(e.Split('|')).Damage;
        }

        var hp = health + damageAll;
        var (rig, watch) = Shape(health: (uint)hp);
        var here = rig.R.H.State.Self.Position!.Value;
        rig.Caster.Table[ShockWaveVI] = ZefirSpell();
        for (var i = 0; i < creatures.Length; i++)
        {
            var c = creatures[i];
            var angle = i * 0.7f;
            Spawn(rig.R.H, c.Guid, c.Wcid, c.Name, here.Origin + new Vector3(MathF.Cos(angle) * c.Metres, MathF.Sin(angle) * c.Metres, 0));
        }

        KnownTargetHealth? walk = null;
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { WalkTarget = () => walk };
        var zero = rig.Now - start;
        var (next, id) = (0, 1000L);
        var corpses = new List<(double At, uint Guid)>();
        guard.Sample(rig.View, rig.Now);
        while (rig.Now - zero < at)
        {
            rig.R.Tick(0.02);
            var t = rig.Now - zero;
            while (next < events.Length && double.Parse(events[next].Split('|')[0], System.Globalization.CultureInfo.InvariantCulture) <= t)
            {
                var f = events[next++].Split('|');
                switch (f[1])
                {
                    case "cast":
                        var target = uint.Parse(f[2][2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                        var c = creatures.First(x => x.Guid == target);
                        var max = CreatureFacts.All.MaxHealthOf(c.Wcid, out var source)!.Value;
                        walk = new KnownTargetHealth(c.Guid, c.Wcid, c.Name, max, CreatureTable.Find(c.Wcid)?.MagicDefence ?? 0, source, rig.R.H.State.Chat.Total);
                        var ms = double.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture) / 1000;
                        rig.R.H.ActionLog.Add(new ActionLogEntry(id++, ActionKind.CastSpell, ActionSource.Automation, $"Cast spell={ShockWaveVI} target=0x{target:X8}", new ActionOutcome(ActionOutcomeKind.Confirmed), rig.Now - ms, rig.Now));
                        break;
                    case "0":
                        rig.R.H.ServerMessage(f[2], 0);
                        rig.R.H.R.Bus.Publish(new PlayerKilledSomething(f[2]));
                        corpses.Add((t + 0.6, walk!.Guid));
                        break;
                    case "21" when f[2].StartsWith("You evaded ", StringComparison.Ordinal):
                        var who = f[2]["You evaded ".Length..^1];
                        rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseEvaded, who, 0, 0, 0, false, 0, false, 0, f[2]));
                        break;
                    case "21":
                        var melee = HitOf(f);
                        Hit(rig, melee.Who, (uint)melee.Damage);
                        break;
                    default:
                        rig.R.H.ServerMessage(f[2], (int)FightTally.MagicChatType);
                        break;
                }

                hp -= HitOf(f).Damage;
                rig.Caster.SetHealth((uint)Math.Round(hp));
            }

            foreach (var (when, guid) in corpses.Where(x => x.At <= t).ToList())
            {
                rig.R.H.R.Reducer.OnUpdateHealth(rig.R.H.R.Context, rig.R.H.Header, new UpdateHealthEvent(guid, 0f));
                corpses.Remove((when, guid));
            }

            rig.R.Tick();
            guard.Sample(rig.View, rig.Now);
        }

        Assert.Equal(health, rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var now, out _) ? now : 0u);
        Assert.True(guard.Race(rig.View, rig.Now, out var input, out _));
        input = input with { HealPerSecond = LiveHeal, NextCastSeconds = LiveHealCast };
        watch.Dispose();
        return (input, LosingRace.Decide(in input), rig);
    }

    /// <summary>A log event's damage to her health and its source: a melee notice's points, a spell's damage line, a drain of her health; 0 for the rest.</summary>
    private static (string Who, double Damage) HitOf(string[] f)
    {
        if (f[1] == "21" && !f[2].StartsWith("You evaded ", StringComparison.Ordinal))
        {
            var text = f[2].StartsWith("Critical hit! ", StringComparison.Ordinal) ? f[2]["Critical hit! ".Length..] : f[2];
            var your = text.IndexOf(" your ", StringComparison.Ordinal);
            var who = text[..text.LastIndexOf(' ', your - 1)];
            var forAt = text.IndexOf(" for ", StringComparison.Ordinal) + 5;
            return (who, double.Parse(text[forAt..text.IndexOf(" point", forAt, StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture));
        }

        if (f[1] == "7" && (FightTally.Classify(f[2], out var amount, out _) == MagicLineKind.DamageTaken || (FightTally.Classify(f[2], out amount, out _) == MagicLineKind.DrainTaken && f[2].EndsWith(" points of your health.", StringComparison.Ordinal))))
        {
            return (string.Empty, amount);
        }

        return (string.Empty, 0);
    }

    // ------------------------------------------------------------------------------------------------ the Nubilous Golems (live ladder52, 2026-11-17, the Crater Caves)

    /// <summary>
    /// The false recall of 04:54:27.874 UTC (<c>cache/m6/14/live/ladder52/step3-levelup.log</c>): six Nubilous Golems on her in the mote hunt, their hits 4 (04:54:07.387), 30 (Lightning Bolt IV, 10.420) and 4 (13.620), then 14.2 s quiet
    /// and a Drain Health Other IV that took 42 (27.869); her health level at 137 of 199; the heal she could sustain 16.1 a second (its next cast 2.4 s). The guard read the golems' new attack as its 42 over a span of 0 s, 42 a second, and
    /// she recalled.
    /// </summary>
    [Fact]
    public void The_0454_golem_s_first_hit_is_its_hits_over_the_window_and_the_derived_rate_for_the_rest_and_she_stays()
    {
        const uint GolemWcid = 23082;
        var (rig, watch) = Shape(health: 137);
        using (watch)
        {
            var here = rig.R.H.State.Self.Position!.Value;
            for (var i = 0u; i < 6; i++)
            {
                Spawn(rig.R.H, 0x800011D0 + i, GolemWcid, "Nubilous Golem", here.Origin + new Vector3(2f + i, 1f, 0));
            }

            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log);
            var zero = rig.Now - 7.0;
            (double At, string Line, uint Melee)[] log =
            [
                (7.387, "Nubilous Golem grazes your foot for 4 points of bludgeoning damage!", 4), (10.420, "Nubilous Golem shocks you for 30 points with Lightning Bolt IV.", 0),
                (13.620, "Nubilous Golem grazes your lower leg for 4 points of bludgeoning damage!", 4), (27.869, "You lose 42 points of health due to Nubilous Golem casting Drain Health Other IV on you", 0),
            ];
            foreach (var (at, line, melee) in log)
            {
                while (rig.Now - zero < at)
                {
                    rig.R.Tick(0.1);
                    guard.Sample(rig.View, rig.Now); // her health level at 137: the live trend
                }

                if (melee > 0)
                {
                    Hit(rig, "Nubilous Golem", melee);
                }
                else
                {
                    rig.R.H.ServerMessage(line, (int)FightTally.MagicChatType);
                }
            }

            rig.R.Tick(0.005);
            Assert.True(guard.Race(rig.View, rig.Now, out var input, out _));
            input = input with { HealPerSecond = 16.1, NextCastSeconds = 2.4 };
            var answer = LosingRace.Decide(in input);
            Assert.True(watch.AttacksOf("Nubilous Golem", rig.Now, out var since, out var attacks));
            Assert.Equal((0.0, 1), (Math.Round(since, 1), attacks)); // a new attack: 14.2 s since the last
            Assert.Equal(4.2, input.IncomingAllPerSecond, 6); // the window's one hit
            Assert.DoesNotContain("'Nubilous Golem' 42/s", input.IncomingWhy, StringComparison.Ordinal);
            Assert.Contains("its 42 over the 10 s and the derived rate for the 10 s it was not heard give ", input.IncomingWhy, StringComparison.Ordinal);
            var derived = double.Parse(System.Text.RegularExpressions.Regex.Match(input.IncomingWhy!, @"derived ([0-9.]+)/s while new").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var expected = (derived * (1 - (since / RecentHits.AverageSeconds))) + 4.2; // the derived rate for the part of the window not heard (all of it but 5 ms) and its 42 over the window: never 42 over 0 s
            Assert.InRange(input.IncomingPerSecond, expected - 0.1, expected + 0.1); // (the rig wears no armour: its derived rate is the unarmoured one)
            Assert.False(answer.Falling); // level
            Assert.False(answer.Lost, LosingRace.Words(in input, in answer));
        }

        // the live numbers: the golems' derived 9.9 a second (six at an attack every 2.87 s) and the one 42 over the window, 14.1, under the heal's 16.1; her health level (the live "falls 0/s" was a slope under a point over 6 s)
        var live = new LosingRaceInput(137, 9.9 + 4.2, 42, 16.1, 2.4, RecallWayOut.Seconds) { TrendPerSecond = -0.04, IncomingAllPerSecond = 4.2, Attackers = 6, AttackersHealth = 1491, KillPerSecond = 67.1 };
        var verdict = LosingRace.Decide(in live);
        Assert.True(verdict.HealHolds);
        Assert.False(verdict.Falling || verdict.Losing || verdict.Lost);
        Assert.Equal("winning", LosingRace.Verdict(in verdict));
        var words = LosingRace.Words(in live, in verdict);
        Assert.StartsWith("winning: her health 137 holds over the last 6 s (-0.04/s, under a point over the window); the heal she can sustain, 16.1/s, holds the incoming 14.1/s", words, StringComparison.Ordinal);
        Assert.True(LosingRace.Decide(live with { TrendPerSecond = -1.0 / HealthTrend.WindowSeconds }).Falling); // a whole point over the window is a fall
        Assert.True(LosingRace.Decide(live with { IncomingPerSecond = 42 }).Lost == false); // level health is never losing, whatever the rates say
    }

    /// <summary>
    /// The false recall of 07:51:27.657 UTC (<c>cache/m6/14/live/ladder55/step3-levelup.log</c>, the Crater Caves mote hunt, 2 of 16 motes): five Nubilous Golems alive, two of them on her (the kill side's "2 attacker(s) with 363 health
    /// left"); a golem's Lightning Bolt IV for 45 at 07:51:26.966, a new attack 0.7 s old; her health 167 of 219 falling 4.4 a second; the heal she could sustain 8.6 a second. The guard priced the new attack on all five alive (8.6 a
    /// second derived), read 12.5 against the heal, and she recalled.
    /// </summary>
    [Fact]
    public void The_0751_golems_derived_rate_counts_the_two_on_her_not_the_five_alive_and_she_stays()
    {
        const uint GolemWcid = 23082;
        var (rig, watch) = Shape(health: 167);
        using (watch)
        {
            var here = rig.R.H.State.Self.Position!.Value;
            Spawn(rig.R.H, 0x80001201, GolemWcid, "Nubilous Golem", here.Origin + new Vector3(2.5f, 0, 0)); // on her
            Spawn(rig.R.H, 0x80001202, GolemWcid, "Nubilous Golem", here.Origin + new Vector3(0, 6f, 0)); // on her
            for (var i = 0u; i < 3; i++)
            {
                Spawn(rig.R.H, 0x80001203 + i, GolemWcid, "Nubilous Golem", here.Origin + new Vector3(60f + (10 * i), 0, 0)); // alive in the cave, past the 40 m reach
            }

            var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log);
            rig.R.H.ServerMessage("Nubilous Golem shocks you for 45 points with Lightning Bolt IV.", (int)FightTally.MagicChatType); // 07:51:26.966
            rig.R.Tick(0.691); // the guard's read at 07:51:27.657
            Assert.True(guard.Race(rig.View, rig.Now, out var input, out _));
            Assert.Equal(5, watch.Living("Nubilous Golem"));
            Assert.Contains("derived ", input.IncomingWhy, StringComparison.Ordinal);
            Assert.Contains("/s while new (2 x an attack every ", input.IncomingWhy, StringComparison.Ordinal); // the two on her, not the five alive
            var derived = double.Parse(System.Text.RegularExpressions.Regex.Match(input.IncomingWhy!, @"derived ([0-9.]+)/s while new").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(watch.AttacksOf("Nubilous Golem", rig.Now, out var since, out _));
            var expected = Math.Max(derived, 4.5 + (derived * (1 - (since / RecentHits.AverageSeconds)))); // its 45 over the window and the two's derived rate for the 9.3 s not heard (the rig wears no armour)
            Assert.InRange(input.IncomingPerSecond, expected - 0.1, expected + 0.1);
            Assert.Equal(4.5, input.IncomingAllPerSecond, 6);
        }

        // the live numbers: the derived 8.6 was five golems' (8.6 / 5 = 1.72 each); the two on her are 3.44, of which the 9.3 s not heard give 3.2, and the 45 over the window 4.5: 7.7 a second, under her heal's 8.6
        var perGolem = 8.6 / 5;
        var incoming = Math.Max(2 * perGolem, 4.5 + (2 * perGolem * (1 - (0.691 / RecentHits.AverageSeconds))));
        Assert.Equal(7.70, incoming, 2);
        var live = new LosingRaceInput(167, incoming, 45, 8.6, 2.4, RecallWayOut.Seconds) { TrendPerSecond = -4.4, IncomingAllPerSecond = 4.5, Attackers = 2, AttackersHealth = 363, KillPerSecond = 62.3 };
        var answer = LosingRace.Decide(in live);
        Assert.True(answer.Falling); // 4.4 a second down
        Assert.True(answer.HealHolds); // 8.6 holds 7.7
        Assert.False(answer.Losing || answer.Lost);
        Assert.Equal("holding", LosingRace.Verdict(in answer)); // she heals and stays in the hunt (live: "lost" on 12.5)
        Assert.True(LosingRace.Decide(live with { IncomingPerSecond = 12.5, KillPerSecond = null, AttackersHealth = null, KillUnknown = "not read" }).Losing); // the live read: the five alive were under the heal's losing side
    }

    // ------------------------------------------------------------------------------------------------ the rig

    private static (FightManaTests.Rig Rig, HostileWatch Watch) Shape(uint health)
    {
        var rig = new FightManaTests.Rig(mana: 300, stamina: 200, knowsDrain: true);
        rig.Caster.ManaRegenPerSecond = 0;
        AutopilotF30Tests.Settle(rig);
        rig.Caster.SetHealth(health);
        rig.R.Tick();
        return (rig, new HostileWatch(rig.View, rig.Fight.GroundProfile, 20));
    }

    private static void Spawn(ActionHarness h, uint guid, uint wcid, string name, Vector3? at = null)
    {
        var here = h.State.Self.Position;
        var position = here is { } p ? new AcPosition(p.CellId, at ?? p.Origin + new Vector3(2.5f, 0, 0), Quaternion.Identity) : ReducerHarness.Position(0x860201AD);
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), position, name: name);
        h.R.Reducer.OnCreateObject(h.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid, ItemType = HostileReach.CreatureItemType, ObjectDescriptionFlags = HostileReach.AttackableFlag } }, isUpdate: false);
    }

    /// <summary>Two seconds of the attack before the guard first looks: 20 every half second after a critical of 29 (40 a second).</summary>
    private static void Warm(FightManaTests.Rig rig)
    {
        var end = rig.Now + 2;
        var first = true;
        while (rig.Now < end)
        {
            Hit(rig, "Assailer", first ? 29u : 20u);
            first = false;
            rig.R.Tick(0.5);
        }
    }

    private static void Hit(FightManaTests.Rig rig, string who, uint damage) =>
        rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, who, 4, damage, 0.05, false, 4, false, 0, $"{who} grazes your lower leg for {damage} points of bludgeoning damage!"));

    /// <summary>Steps the guard for up to <paramref name="seconds"/>, the attackers hitting her for 20 every half second (40 a second, a critical of 29 first) when <paramref name="hits"/>; true when <paramref name="until"/> held.</summary>
    private static bool Run(FightManaTests.Rig rig, AutopilotVitalsGuard guard, Func<AutopilotVitalsGuard, bool> until, double seconds, bool hits, uint fallPerHit = 0)
    {
        var end = rig.Now + seconds;
        var next = rig.Now;
        var first = true;
        while (rig.Now < end)
        {
            if (hits && rig.Now >= next)
            {
                Hit(rig, "Assailer", first ? 29u : 20u);
                (first, next) = (false, rig.Now + 0.5);
                if (fallPerHit > 0 && rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health > fallPerHit)
                {
                    rig.Caster.SetHealth(health - fallPerHit); // M6 14 the losing race: her health falls with the hits (a level health is not losing)
                }
            }

            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            guard.Step(rig.View, rig.Now, "travel");
            if (until(guard))
            {
                return true;
            }
        }

        return false;
    }
}

using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Casting;

/// <summary>
/// The cast policy (M5 item 1 lane K1, the P5b design's §2.2 and §2.3) over the real Action API and ACE's own answers through the real decoder (<see cref="WorldActionRig"/>): every outcome row with its
/// bound and its count, every preset against the rule of the caller it reproduces (a scripted sequence of answers each), the owner's fizzle rule (D14), the stamina-to-mana dance (D27), the Mana
/// Conversion tries (D15), the kill switch while a try waits and while one is in flight, dry run, the timeout never resent, and the chat-line proof.
/// </summary>
public sealed class CastAttemptTests
{
    internal const uint BuffI = 301, BuffII = 302, BuffIII = 303;
    internal const uint BoltI = 401, BoltII = 402, BoltIII = 403;
    internal const uint OtherBuff = 501;
    internal const uint Transfer = 601;
    internal const uint Monster = 0x80000123;
    internal const string MonsterName = "Drudge Slinker";
    internal static readonly uint[] Buffs = [BuffI, BuffII, BuffIII];
    internal static readonly uint[] Bolts = [BoltI, BoltII, BoltIII];
    internal static readonly uint[] Ceilings = [10, 20, 30];

    [Fact]
    public void A_self_buff_lands_on_its_enchantment_in_one_try_as_automation()
    {
        var t = new Rig();
        Assert.Equal(CastEnd.Running, t.Start(CastRules.BuffLine, Buffs, 2));
        Assert.Equal(1, t.Sends);
        Assert.Equal(ActionSource.Automation, t.A.Handle!.Source);
        Assert.Equal(CastEnd.Landed, t.Land(BuffIII));
        var report = t.A.Report;
        Assert.Equal((1, 2, BuffIII, 0), (report.Tries, report.LevelIndex, report.SpellId, report.Fizzles));
        Assert.Contains("landed after 1 tries", t.LastLog, StringComparison.Ordinal);
    }

    [Fact]
    public void The_source_is_the_request_s_when_it_names_one()
    {
        var t = new Rig();
        t.A.Start(new CastRequest { Levels = Buffs, Index = 0, Rules = CastRules.Once, Source = ActionSource.Ui }, t.Now);
        t.Pump();
        Assert.Equal(ActionSource.Ui, t.A.Handle!.Source);
    }

    [Fact]
    public void A_bad_request_throws()
    {
        var t = new Rig();
        Assert.Throws<ArgumentException>(() => t.A.Start(new CastRequest { Levels = Buffs, Index = 3, Rules = CastRules.Once }, t.Now));
        Assert.Throws<ArgumentException>(() => t.A.Start(new CastRequest { Levels = [0u], Rules = CastRules.Once }, t.Now));
    }

    // ------------------------------------------------------------------------------------------------ the presets against today's callers

    [Fact]
    public void BuffLine_reproduces_the_buff_cycle_one_level_down_once_then_a_named_miss()
    {
        var t = new Rig();
        t.Start(CastRules.BuffLine, Buffs, 2);
        Assert.Equal(CastEnd.Running, t.Fizzle());
        Assert.Equal(2, t.Sends);
        Assert.Equal(BuffII, t.LastSpell); // BuffCycle.CastOutcome: _castIndex-- on the first fizzle
        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        var report = t.A.Report;
        Assert.Equal((2, 2, 1, 1), (report.Tries, report.Fizzles, report.LevelIndex, report.LevelsDownSpent));
        Assert.Equal(2, t.Sends); // "no third try"
    }

    [Fact]
    public void BuffLine_lands_one_level_down_after_a_fizzle()
    {
        var t = new Rig();
        t.Start(CastRules.BuffLine, Buffs, 2);
        t.Fizzle();
        Assert.Equal(CastEnd.Landed, t.Land(BuffII));
        Assert.Equal((2, 1, BuffII), (t.A.Report.Tries, t.A.Report.LevelIndex, t.A.Report.SpellId));
    }

    [Fact]
    public void BuffLine_at_the_lowest_level_misses_with_no_level_below()
    {
        var t = new Rig();
        t.Start(CastRules.BuffLine, Buffs, 0);
        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Contains("no level below", t.A.Report.Why, StringComparison.Ordinal);
        Assert.Equal(1, t.Sends);
    }

    [Fact]
    public void BuffLine_names_a_level_below_the_SpellTable_lacks()
    {
        var t = new Rig();
        uint[] line = [999, BuffII];
        t.Start(CastRules.BuffLine, line, 1);
        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Contains("the level below is not in the SpellTable", t.A.Report.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Once_reproduces_the_upgrade_pass_and_the_checks_no_retry()
    {
        var t = new Rig();
        t.Start(CastRules.Once, Buffs, 2);
        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Equal((1, 1), (t.Sends, t.A.Report.Fizzles));
    }

    [Theory]
    [InlineData(8)] // the mana loop's TransferTries: the 8th fizzle in a row fails the loop
    [InlineData(3)] // the vitals keeper's MaxFizzlesInRow: the 3rd sets the line aside
    [InlineData(4)] // the pre-trip heal's PreTripMaxHeals
    public void Utility_n_reproduces_at_most_n_casts_while_they_fizzle(int n)
    {
        var t = new Rig();
        t.Start(CastRules.Utility(n), [Transfer], 0);
        for (var i = 1; i < n; i++)
        {
            Assert.Equal(CastEnd.Running, t.Fizzle());
        }

        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Equal((n, n), (t.Sends, t.A.Report.Fizzles));
    }

    [Fact]
    public void Utility_counts_a_streak_the_caller_carries_and_lands_inside_its_bound()
    {
        var t = new Rig();
        t.A.Start(new CastRequest { Levels = new[] { Transfer }, Rules = CastRules.Utility(3), FizzlesSpent = 2 }, t.Now); // the keeper's row already holds 2
        t.Pump();
        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Equal(1, t.Sends);

        var u = new Rig();
        u.Start(CastRules.Utility(3), [Transfer], 0);
        u.Fizzle();
        u.Fizzle();
        Assert.Equal(CastEnd.Unproven, u.Sent());
        Assert.Equal(3, u.A.Report.Tries);
    }

    [Fact]
    public void Bolt_reproduces_the_fight_one_level_down_to_the_next_usable_level_once_per_scenario()
    {
        var t = new Rig();
        Func<uint, bool> knows = id => id != BoltII; // the spellbook lacks Bolt II
        t.A.Start(new CastRequest { Levels = Bolts, Index = 2, Target = Monster, Rules = CastRules.Bolt, Usable = knows }, t.Now);
        t.Pump();
        Assert.Equal(CastEnd.Running, t.Fizzle());
        Assert.Equal(BoltI, t.LastSpell);
        Assert.Equal(Monster, t.LastTarget);
        Assert.Equal(CastEnd.Landed, t.Projectile());
        Assert.Equal((0, 1), (t.A.Report.LevelIndex, t.A.Report.LevelsDownSpent));

        var again = new Rig(); // the scenario dropped once already: a fizzle ends the attempt
        again.A.Start(new CastRequest { Levels = Bolts, Index = 1, Target = Monster, Rules = CastRules.Bolt, LevelsDownSpent = 1 }, again.Now);
        again.Pump();
        Assert.Equal(CastEnd.Fizzled, again.Fizzle());
        Assert.Equal(1, again.Sends);
    }

    [Theory]
    [InlineData(5)] // RecallHome.MaxFizzles
    [InlineData(3)] // the script legs' MaxFizzles
    public void Travel_n_reproduces_n_same_level_retries_sent_with_no_mana_check(int n)
    {
        var t = new Rig(mana: 1);
        t.A.Start(new CastRequest { Levels = new[] { BuffII }, Rules = CastRules.Travel(n), ManaCeilings = new uint[] { 20 } }, t.Now);
        t.Pump();
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(CastEnd.Running, t.Fizzle());
        }

        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Equal(n + 1, t.Sends);

        var u = new Rig();
        u.Start(CastRules.Travel(n), [BuffII], 0);
        for (var i = 0; i < n; i++)
        {
            u.Fizzle();
        }

        Assert.Equal(CastEnd.Landed, u.Land(BuffII));
        Assert.Equal(n + 1, u.A.Report.Tries);
    }

    [Fact]
    public void Timed_reproduces_the_harm_heal_cap_fizzles_retried_until_the_time_cap()
    {
        var t = new Rig();
        t.Start(CastRules.Timed(60), [BuffI], 0);
        for (var i = 0; i < 6; i++)
        {
            t.R.Tick(9); // each cast's answer 9 s later (inside the 10 s timeout)
            Assert.Equal(CastEnd.Running, t.Fizzle());
        }

        t.R.Tick(9); // 63 s
        Assert.Equal(CastEnd.TimeCap, t.Fizzle());
        Assert.Equal(7, t.Sends); // no try after the cap
        Assert.Equal(7, t.A.Report.Fizzles);
    }

    // ------------------------------------------------------------------------------------------------ the owner's fizzle rule (D14)

    [Fact]
    public void The_D14_rule_is_the_same_level_twice_more_then_one_level_down_once_then_a_named_miss()
    {
        var t = new Rig();
        t.Start(CastRules.BuffLineD14, Buffs, 2);
        var levels = new List<uint> { t.LastSpell };
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(CastEnd.Running, t.Fizzle());
            levels.Add(t.LastSpell);
        }

        Assert.Equal([BuffIII, BuffIII, BuffIII, BuffII], levels);
        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Equal((4, 4, 2, 1), (t.Sends, t.A.Report.Fizzles, t.A.Report.FizzleRetriesSpent, t.A.Report.LevelsDownSpent));
        Assert.Equal("Bolt+D14", CastRules.BoltD14.Name);
        Assert.Equal((2, 1), (CastRules.BoltD14.MaxFizzles, CastRules.BoltD14.LevelsDown));
    }

    // ------------------------------------------------------------------------------------------------ the outcome rows

    [Fact]
    public void A_0x0401_outside_the_mana_tries_ends_ManaShort_today_s_ceiling_rule()
    {
        var t = new Rig();
        t.Start(CastRules.BuffLine, Buffs, 2);
        Assert.Equal(CastEnd.ManaShort, t.Answer(0x0401));
        Assert.Equal((1, 1, 0x0401u), (t.A.Report.Tries, t.A.Report.ManaRefusals, t.A.Report.Code));
    }

    [Fact]
    public void The_mana_tries_resend_after_the_spacing_and_end_ManaShort_outside_them()
    {
        var t = new Rig(mana: 12);
        var rules = CastRules.Once.WithManaTries(3, 0.25);
        t.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = rules, ManaCeilings = Ceilings }, t.Now);
        t.Pump();
        Assert.Equal(CastEnd.Running, t.Answer(0x0401));
        Assert.Equal(1, t.Sends);
        t.R.Tick(0.2);
        Assert.Equal(CastEnd.Running, t.Step());
        Assert.Equal(1, t.Sends); // the spacing is not over
        t.R.Tick(0.05);
        t.Step();
        t.Pump();
        Assert.Equal(2, t.Sends);
        Assert.Equal(CastEnd.Running, t.Answer(0x0401));
        t.R.Tick(0.25);
        t.Step();
        t.Pump();
        Assert.Equal(CastEnd.ManaShort, t.Answer(0x0401));
        Assert.Equal((3, 3, 3, 0), (t.A.Report.Tries, t.A.Report.ManaRefusals, t.A.Report.ManaTries, t.A.Report.ManaLanded));
    }

    [Fact]
    public void A_mana_try_that_lands_is_counted_against_the_ceiling()
    {
        var t = new Rig(mana: 25);
        t.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.BuffLine.WithManaTries(), ManaCeilings = Ceilings }, t.Now);
        t.Pump();
        t.Answer(0x0401);
        t.R.Tick(0.25);
        t.Step();
        t.Pump();
        Assert.Equal(CastEnd.Landed, t.Land(BuffIII));
        Assert.Equal((2, 1, 2, 1), (t.A.Report.Tries, t.A.Report.ManaRefusals, t.A.Report.ManaTries, t.A.Report.ManaLanded));

        var full = new Rig(mana: 40); // at or over the ceiling: not a mana try
        full.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.BuffLine, ManaCeilings = Ceilings }, full.Now);
        full.Pump();
        full.Land(BuffIII);
        Assert.Equal(0, full.A.Report.ManaTries);
    }

    [Fact]
    public void A_fizzle_among_the_mana_tries_counts_against_the_fizzle_bound()
    {
        var t = new Rig(mana: 5);
        t.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.Once.WithManaTries(8), ManaCeilings = Ceilings }, t.Now);
        t.Pump();
        t.Answer(0x0401);
        t.R.Tick(0.25);
        t.Step();
        t.Pump();
        Assert.Equal(CastEnd.Fizzled, t.Fizzle());
        Assert.Equal((2, 1, 1), (t.A.Report.Tries, t.A.Report.Fizzles, t.A.Report.ManaRefusals));
    }

    [Fact]
    public void Under_the_try_floor_a_refusal_ends_ManaShort()
    {
        var t = new Rig(mana: 3);
        t.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.Once.WithManaTries(8), ManaCeilings = Ceilings, ManaTryFloor = 5 }, t.Now);
        t.Pump();
        Assert.Equal(CastEnd.ManaShort, t.Answer(0x0401));
        Assert.Contains("try floor 5", t.A.Report.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_a_try_is_not_sent_when_the_conversion_model_cannot_raise_the_mana_to_the_cost_and_is_when_it_can()
    {
        // The model gate (M5 7, D15 gated on ManaConversionModel): 12 mana held of up to 30. An untrained caster pays the base, so no try can land: ManaShort at once, nothing sent, no blind retries.
        var untrained = new Rig(mana: 12);
        untrained.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.Once.WithManaTries(8), ManaCeilings = Ceilings, ManaConversion = 0 }, untrained.Now);
        untrained.Pump();
        Assert.Equal(CastEnd.ManaShort, untrained.A.End);
        Assert.Equal(0, untrained.Sends);
        Assert.Equal((0, 0), (untrained.A.Report.Tries, untrained.A.Report.ManaRefusals));
        Assert.Contains("Mana Conversion model", untrained.A.Report.Why, StringComparison.Ordinal);

        // A skilled caster whose model gives the try its chance sends it (and a refusal inside the tries goes on to the next try as before).
        var spell = untrained.Spells[BuffIII];
        Assert.True(ManaConversionModel.LandingChance(30, spell.Power, 400, 12) >= ManaConversionModel.DefaultTryChance);
        var skilled = new Rig(mana: 12);
        skilled.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.Once.WithManaTries(8), ManaCeilings = Ceilings, ManaConversion = 400 }, skilled.Now);
        skilled.Pump();
        Assert.Equal(1, skilled.Sends);
        Assert.Equal(CastEnd.Running, skilled.Answer(0x0401));

        // No conversion given: today's behaviour, the try is sent whatever the model says.
        var ungated = new Rig(mana: 12);
        ungated.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.Once.WithManaTries(8), ManaCeilings = Ceilings }, ungated.Now);
        ungated.Pump();
        Assert.Equal(1, ungated.Sends);

        // At or over the ceiling the gate has nothing to say, whatever the skill.
        var full = new Rig(mana: 30);
        full.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.Once.WithManaTries(8), ManaCeilings = Ceilings, ManaConversion = 0 }, full.Now);
        full.Pump();
        Assert.Equal(1, full.Sends);
    }

    [Fact]
    public void A_retry_the_rules_check_against_the_ceiling_ends_ManaShort_with_nothing_sent_and_resumes()
    {
        var t = new Rig(mana: 15);
        var request = new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.BuffLine, ManaCeilings = Ceilings };
        t.A.Start(request, t.Now); // the first try is sent with 15 of 30: the policy never checks the mana before it
        t.Pump();
        Assert.Equal(1, t.Sends);
        Assert.Equal(CastEnd.ManaShort, t.Fizzle());
        var report = t.A.Report;
        Assert.Equal((1, 1, 1), (t.Sends, report.NextLevelIndex, report.LevelsDownSpent));

        t.SetMana(25); // the caller's mana phase
        t.A.Start(request.Resume(report), t.Now);
        t.Pump();
        Assert.Equal(BuffII, t.LastSpell);
        Assert.Equal(CastEnd.Fizzled, t.Fizzle()); // the line's one level down was spent before the resume
    }

    private static CastRules FamilyRules(string family) => family switch
    {
        "BuffLine" => CastRules.BuffLine,
        "BuffLine+D14" => CastRules.BuffLineD14,
        "Once" => CastRules.Once,
        "Bolt" => CastRules.Bolt,
        "Bolt+D14" => CastRules.BoltD14,
        "Utility(3)" => CastRules.Utility(3),
        "Travel(3)" => CastRules.Travel(3),
        "Timed(60)" => CastRules.Timed(60),
        "ManaDance(40)" => CastRules.ManaDance(40),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "no such preset"),
    };

    // M5 1 fix A, 1-R6 (the owner's DoD 2, busy waits and resends): every preset waits out a busy refusal (0x001D) for one second and sends the same cast again, three times; the fourth ends Busy. Each busy answer is
    // counted (busy=). Before the fix every preset ended Busy on the first.
    [Theory]
    [InlineData("BuffLine")]
    [InlineData("BuffLine+D14")]
    [InlineData("Once")]
    [InlineData("Bolt")]
    [InlineData("Bolt+D14")]
    [InlineData("Utility(3)")]
    [InlineData("Travel(3)")]
    [InlineData("Timed(60)")]
    [InlineData("ManaDance(40)")]
    public void Every_preset_waits_out_a_busy_refusal_one_second_and_sends_again_three_times_then_ends_Busy_counted(string family)
    {
        var rules = FamilyRules(family);
        Assert.Equal((CastRules.DefaultBusyRetries, CastRules.DefaultBusyWaitSeconds), (rules.MaxBusy, rules.BusyWaitSeconds));
        var t = new Rig();
        t.Start(rules, [Transfer], 0);
        var spell = t.LastSpell;
        for (var i = 1; i <= CastRules.DefaultBusyRetries; i++)
        {
            Assert.Equal(CastEnd.Running, t.Answer(0x001D)); // the wait, not an end
            t.R.Tick(0.9);
            t.Step();
            t.Pump();
            Assert.Equal(i, t.Sends); // the wait is 1 s: not resent at 0.9
            t.R.Tick(0.1);
            t.Step();
            t.Pump();
            Assert.Equal(i + 1, t.Sends); // the resend, the same cast
            Assert.Equal(spell, t.LastSpell);
            Assert.Equal(i, t.A.Report.Busy); // each busy answer counted
        }

        Assert.Equal(CastEnd.Busy, t.Answer(0x001D)); // the bound
        var report = t.A.Report;
        Assert.Equal((CastRules.DefaultBusyRetries + 1, CastRules.DefaultBusyRetries + 1), (report.Tries, report.Busy));
        Assert.Equal(CastRules.DefaultBusyRetries + 1, t.Sends); // nothing sent after the end
        var tally = new CastTally();
        tally.Add(report);
        Assert.Contains($" busy={CastRules.DefaultBusyRetries + 1} ", tally.Fields + " ", StringComparison.Ordinal);
    }

    [Fact]
    public void A_busy_resend_that_lands_ends_Landed_and_the_form_sets_its_own_bound()
    {
        var t = new Rig();
        t.Start(CastRules.BuffLine, Buffs, 2);
        Assert.Equal(CastEnd.Running, t.Answer(0x001D));
        t.R.Tick(1.0);
        t.Step();
        t.Pump();
        Assert.Equal(CastEnd.Landed, t.Land(BuffIII));
        Assert.Equal((2, 1), (t.A.Report.Tries, t.A.Report.Busy));

        var none = new Rig();
        none.Start(CastRules.Utility(3).WithBusyRetries(0), [Transfer], 0); // the form restores the old ending
        Assert.Equal(CastEnd.Busy, none.Answer(0x001D));
        Assert.Equal((1, 1), (none.Sends, none.A.Report.Busy));

        var two = new Rig();
        two.Start(CastRules.Once.WithBusyRetries(2, 0.5), [BuffI], 0);
        Assert.Equal(CastEnd.Running, two.Answer(0x001D));
        two.R.Tick(0.5);
        two.Step();
        two.Pump();
        Assert.Equal(2, two.Sends);
        Assert.Equal(CastEnd.Running, two.Answer(0x001D));
        two.R.Tick(0.5);
        two.Step();
        two.Pump();
        Assert.Equal(CastEnd.Busy, two.Answer(0x001D));
        Assert.Equal((3, 3), (two.Sends, two.A.Report.Busy));
    }

    [Fact]
    public void A_busy_wait_does_not_outlast_the_attempts_time_cap()
    {
        var t = new Rig();
        t.Start(CastRules.Once.WithMaxSeconds(1.5), [BuffI], 0);
        Assert.Equal(CastEnd.Running, t.Answer(0x001D));
        t.R.Tick(1.0);
        t.Step();
        t.Pump();
        Assert.Equal(2, t.Sends); // the resend at 1 s, inside the cap
        Assert.Equal(CastEnd.Running, t.Answer(0x001D));
        t.R.Tick(1.0);
        Assert.Equal(CastEnd.TimeCap, t.Step()); // the next resend would be at 2 s: the cap of 1.5 s ends it first
        Assert.Equal(2, t.Sends);
    }

    [Fact]
    public void Timed_checks_the_mana_before_a_retry_and_ends_ManaShort_with_nothing_sent_and_resumes_M5_1_fix_A_1_R1()
    {
        // Timed's doc says "the mana checked again before each retry"; before the fix it set no CeilingBeforeRetry, so a fizzle was resent at once with whatever mana was left (and the server refused it 0x0401).
        Assert.True(CastRules.Timed(60).CeilingBeforeRetry);
        var t = new Rig(mana: 25);
        var request = new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.Timed(60), ManaCeilings = Ceilings };
        t.A.Start(request, t.Now); // the first try is never checked: 25 of 30
        t.Pump();
        Assert.Equal(1, t.Sends);
        Assert.Equal(CastEnd.ManaShort, t.Fizzle());
        var report = t.A.Report;
        Assert.Equal((1, 2, 0x0402u, 0), (t.Sends, report.NextLevelIndex, report.Code, report.ManaRefusals)); // nothing sent, the same level to resume at, no refusal (so a caller re-decides, as HarmHealScript does)
        Assert.Contains("nothing sent", report.Why, StringComparison.Ordinal);

        t.SetMana(35); // the caller's top-up
        t.A.Start(request.Resume(report), t.Now);
        t.Pump();
        Assert.Equal(2, t.Sends);
        Assert.Equal(CastEnd.Landed, t.Land(BuffIII));
    }

    [Fact]
    public void An_unproven_Sent_mana_try_is_not_counted_as_landed_M5_1_fix_A_1_R16()
    {
        var t = new Rig(mana: 25);
        t.A.Start(new CastRequest { Levels = new[] { OtherBuff }, Target = Monster, Rules = CastRules.Once.WithManaTries(), ManaCeilings = new uint[] { 30 } }, t.Now); // a buff on another target (no proof on the wire), 25 of 30: a mana try
        t.Pump();
        Assert.Equal(CastEnd.Unproven, t.Sent());
        var report = t.A.Report;
        Assert.Equal((1, 0, 0), (report.ManaTries, report.ManaLanded, report.Proved)); // sent, nothing proved: not a landing
        var tally = new CastTally();
        tally.Add(report);
        Assert.Equal((1, 0, 1), (tally.Unproven, tally.ManaLanded, tally.Attempts)); // the Unproven end is still the tally's own count
    }

    [Theory]
    [InlineData(0x0400u, CastEnd.NoComponents)]
    [InlineData(0x0550u, CastEnd.OutOfRange)]
    [InlineData(0x042Cu, CastEnd.TargetDied)] // M5 7 D124: never out of range
    [InlineData(0x04CCu, CastEnd.ServerError)]
    public void The_codes_that_are_never_retried_end_at_once(uint code, CastEnd end)
    {
        var t = new Rig();
        t.Start(CastRules.Travel(5), [BuffI], 0);
        Assert.Equal(end, t.Answer(code));
        Assert.Equal((1, code), (t.Sends, t.A.Report.Code));
    }

    [Fact]
    public void A_0x042C_for_a_target_the_caller_says_is_dead_ends_TargetDied_with_nothing_resent()
    {
        var t = new Rig();
        t.A.Start(new CastRequest { Levels = Bolts, Index = 2, Target = Monster, Rules = CastRules.Travel(5), Purpose = "test", TargetGone = guid => guid == Monster }, t.Now); // M5 7 F28: the kill landed during the wind-up
        t.Pump();
        Assert.Equal(CastEnd.TargetDied, t.Answer(CastAttempt.NotAcquiredCode));
        Assert.Equal((1, CastAttempt.NotAcquiredCode), (t.Sends, t.A.Report.Code));
        Assert.Equal("target-died", t.A.Report.End.Name());
        Assert.False(t.A.Report.End.Succeeded());
    }

    [Fact]
    public void A_cast_the_tracker_knows_ACE_would_refuse_for_mana_ends_as_ACE_s_0x0401_would_with_nothing_sent_M5_7_D141()
    {
        var t = new Rig(mana: 0); // no mana at all: the cost is at least 1, whatever the Mana Conversion
        t.Start(CastRules.BuffLine.WithManaTries(8), Buffs, 2);
        Assert.Equal(CastEnd.ManaShort, t.Step()); // the callers' dance reads ManaShort with a 0x0401 refusal, as before; no try is spent on a certain refusal
        Assert.Equal((0, 1, 0x0401u), (t.Sends, t.A.Report.ManaRefusals, t.A.Report.Code));
        Assert.Contains("known before the send", t.A.Report.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cast_whose_target_left_the_table_before_its_turn_ends_TargetDied_as_ACE_s_0x042C_would_M5_7_D141()
    {
        var t = new Rig();
        t.A.Start(new CastRequest { Levels = Bolts, Index = 2, Target = Monster, Rules = CastRules.Travel(5), Purpose = "test" }, t.Now);
        t.R.H.R.Reducer.OnDeleteObject(t.R.H.R.Context, new DeleteObjectMsg(Monster, 1)); // the corpse rotted before the Magic lane's turn
        t.Pump();
        Assert.Equal(CastEnd.TargetDied, t.Step());
        Assert.Equal((0, CastAttempt.NotAcquiredCode), (t.Sends, t.A.Report.Code));
    }

    [Fact]
    public void A_0x042C_is_never_OutOfRange_even_for_a_target_still_shown_here_and_0x0550_stays_OutOfRange()
    {
        // M5 7 D124: ACE sends 0x042C only when the target is gone from its landblock (Player_Magic.cs:137-140, 173-179, 197-203, 751-757 @95ef426d); round 12's target stood 2.4 m away and had died.
        var t = new Rig();
        t.A.Start(new CastRequest { Levels = Bolts, Index = 2, Target = Monster, Rules = CastRules.Travel(5), Purpose = "test", TargetGone = _ => false }, t.Now);
        t.Pump();
        Assert.Equal(CastEnd.TargetDied, t.Answer(CastAttempt.NotAcquiredCode));
        Assert.Contains("not acquired at the release", t.A.Report.Why, StringComparison.Ordinal);
        Assert.Equal(1, t.Sends);
        var unasked = new Rig();
        unasked.A.Start(new CastRequest { Levels = Bolts, Index = 2, Target = Monster, Rules = CastRules.Travel(5), Purpose = "test" }, unasked.Now); // no TargetGone test (the drain's request)
        unasked.Pump();
        Assert.Equal(CastEnd.TargetDied, unasked.Answer(CastAttempt.NotAcquiredCode));
        var far = new Rig();
        far.A.Start(new CastRequest { Levels = Bolts, Index = 2, Target = Monster, Rules = CastRules.Travel(5), Purpose = "test", TargetGone = _ => true }, far.Now);
        far.Pump();
        Assert.Equal(CastEnd.OutOfRange, far.Answer(CastAttempt.OutOfRangeCode)); // only 0x0550 means out of range
    }

    [Fact]
    public void The_releases_0x042C_WeenieError_before_UseDone_0_ends_TargetDied_not_NoEffect()
    {
        // M5 7 D124: at the release ACE's DoCastSpell sends WeenieError 0x042C (Player_Magic.cs:751-757 @95ef426d) and FinishCast's UseDone 0 follows (935-985): round 12's 31 "ended the cast without a projectile".
        var t = new Rig();
        t.A.Start(new CastRequest { Levels = Bolts, Index = 2, Target = Monster, Rules = CastRules.Travel(5), Purpose = "test", TargetGone = guid => guid == Monster }, t.Now);
        t.Pump();
        Assert.Equal(CastEnd.TargetDied, t.NotAcquiredAtRelease());
        Assert.Equal((1, CastAttempt.NotAcquiredCode), (t.Sends, t.A.Report.Code));
        Assert.Contains("died or left during the wind-up", t.A.Report.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void UseDone_0_without_the_enchantment_is_NoEffect()
    {
        var t = new Rig();
        t.Start(CastRules.Travel(5), [BuffI], 0);
        Assert.Equal(CastEnd.NoEffect, t.Answer(0));
        Assert.Equal(1, t.Sends);
    }

    [Fact]
    public void A_refusal_ends_Refused_and_is_never_retried()
    {
        var t = new Rig();
        t.R.CombatModeIs(CombatModes.NonCombat); // the stance guard refuses a cast outside the magic mode
        t.Start(CastRules.Travel(5), [BuffI], 0); // the guard refuses in the Action API's update
        Assert.Equal(CastEnd.Refused, t.Step());
        t.Step();
        t.Pump();
        Assert.Equal((0, 1), (t.Sends, t.A.Report.Tries));
    }

    [Fact]
    public void Dry_run_ends_DryRun_with_nothing_sent()
    {
        var t = new Rig();
        t.R.Api.DryRun = true;
        Assert.Equal(CastEnd.DryRun, t.Start(CastRules.Travel(5), [BuffI], 0));
        t.Pump();
        Assert.Equal(0, t.Sends);
    }

    [Fact]
    public void The_timeout_ends_Timeout_and_is_never_resent()
    {
        var t = new Rig();
        t.Start(CastRules.Travel(5), [BuffI], 0);
        t.R.Tick(10.5);
        Assert.Equal(CastEnd.Timeout, t.Step());
        t.R.Tick(5);
        t.Step();
        t.Pump();
        Assert.Equal(1, t.Sends);
    }

    [Fact]
    public void The_kill_switch_before_the_start_sends_nothing()
    {
        var t = new Rig();
        t.R.Api.KillSwitch = true;
        Assert.Equal(CastEnd.Cancelled, t.Start(CastRules.Travel(5), [BuffI], 0));
        Assert.Equal((0, 0), (t.Sends, t.A.Report.Tries));
    }

    [Fact]
    public void The_kill_switch_while_a_try_waits_its_spacing_ends_Cancelled_with_nothing_more_sent()
    {
        var t = new Rig(mana: 5);
        t.A.Start(new CastRequest { Levels = Buffs, Index = 2, Rules = CastRules.ManaDance(40), ManaCeilings = Ceilings }, t.Now);
        t.Pump();
        t.Answer(0x0401);
        t.R.Api.KillSwitch = true;
        Assert.Equal(CastEnd.Cancelled, t.Step());
        t.R.Tick(1);
        t.Step();
        t.Pump();
        Assert.Equal(1, t.Sends);
    }

    [Fact]
    public void The_kill_switch_while_a_try_is_in_flight_lets_it_finish_then_ends_Cancelled_with_no_resend()
    {
        var t = new Rig();
        t.Start(CastRules.Travel(5), [BuffI], 0);
        t.R.Api.KillSwitch = true; // the try was sent: it cannot be unsent
        Assert.Equal(CastEnd.Running, t.Step());
        Assert.Equal(CastEnd.Cancelled, t.Fizzle());
        Assert.Equal((1, 1), (t.Sends, t.A.Report.Fizzles));
    }

    // ------------------------------------------------------------------------------------------------ the dance (D27)

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void The_dance_lands_on_press_k_with_every_press_counted(int k)
    {
        var t = new Rig(mana: 5);
        t.A.Start(new CastRequest { Levels = new[] { Transfer }, Rules = CastRules.ManaDance(40, 0.25), ManaCeilings = new uint[] { 70 }, Proof = t.Proof }, t.Now);
        t.Pump();
        for (var press = 1; press < k; press++)
        {
            Assert.Equal(CastEnd.Running, t.Answer(0x0401));
            t.R.Tick(0.24);
            t.Step();
            t.Pump();
            Assert.Equal(press, t.Sends); // the spacing is honoured on the fake clock
            t.R.Tick(0.01);
            t.Step();
            t.Pump();
            Assert.Equal(press + 1, t.Sends);
        }

        t.Chat("You cast Spell 601 on yourself and lose 50 points of stamina and also gain 45 points of mana");
        Assert.Equal(CastEnd.Landed, t.Sent());
        var report = t.A.Report;
        Assert.Equal((k, k - 1, k, 1, 1), (report.Tries, report.ManaRefusals, report.ManaTries, report.ManaLanded, report.Proved));
    }

    [Fact]
    public void Forty_refusals_end_the_dance_ManaShort_with_forty_presses_counted()
    {
        var t = new Rig(mana: 1);
        t.A.Start(new CastRequest { Levels = new[] { Transfer }, Rules = CastRules.ManaDance(40, 0.25), ManaCeilings = new uint[] { 70 } }, t.Now);
        t.Pump();
        var end = CastEnd.Running;
        for (var press = 1; press <= 40 && end == CastEnd.Running; press++)
        {
            end = t.Answer(0x0401);
            t.R.Tick(0.25);
            t.Step();
            t.Pump();
        }

        Assert.Equal(CastEnd.ManaShort, end);
        Assert.Equal((40, 40, 40, 40), (t.Sends, t.A.Report.Tries, t.A.Report.ManaRefusals, t.A.Report.ManaTries));
    }

    [Fact]
    public void A_fizzle_mid_dance_is_counted_and_waits_the_spacing_too()
    {
        var t = new Rig(mana: 5);
        t.A.Start(new CastRequest { Levels = new[] { Transfer }, Rules = CastRules.ManaDance(40, 0.25), ManaCeilings = new uint[] { 70 } }, t.Now);
        t.Pump();
        t.Answer(0x0401);
        t.R.Tick(0.25);
        t.Step();
        t.Pump();
        Assert.Equal(CastEnd.Running, t.Fizzle());
        Assert.Equal(2, t.Sends); // the spacing after a fizzle
        t.R.Tick(0.25);
        t.Step();
        t.Pump();
        Assert.Equal(3, t.Sends);
        Assert.Equal(CastEnd.Unproven, t.Sent()); // no proof asked of this one
        Assert.Equal((3, 1, 1), (t.A.Report.Tries, t.A.Report.Fizzles, t.A.Report.ManaRefusals));
    }

    // ------------------------------------------------------------------------------------------------ the proof

    [Fact]
    public void A_buff_on_another_target_is_proved_by_the_caster_s_chat_line()
    {
        var t = new Rig();
        t.StartAt(OtherBuff, Monster, CastRules.Once);
        t.Chat("You cast Spell 501 on Drudge Slinker, surpassing Spell 500");
        Assert.Equal(CastEnd.Landed, t.Sent());
        Assert.Equal(1, t.A.Report.Proved);
    }

    [Fact]
    public void A_buff_on_another_target_without_its_line_or_with_another_target_s_line_stays_unproven()
    {
        var t = new Rig();
        t.StartAt(OtherBuff, Monster, CastRules.Once);
        t.Chat("You cast Spell 501 on Mosswart Worshipper");
        t.Chat("You cast Spell 501 on Drudge Slinker", chatType: 0x11); // not a Magic line
        Assert.Equal(CastEnd.Unproven, t.Sent());
        Assert.Contains("TODO(m3-cast-other-target-proof)", t.A.Report.Why, StringComparison.Ordinal);
        Assert.Equal(0, t.A.Report.Proved);
    }

    [Fact]
    public void A_line_from_before_the_send_proves_nothing()
    {
        var t = new Rig();
        t.Chat("You cast Spell 501 on Drudge Slinker");
        t.StartAt(OtherBuff, Monster, CastRules.Once);
        Assert.Equal(CastEnd.Unproven, t.Sent());
    }

    [Fact]
    public void A_resist_is_retried_at_the_same_level_under_the_resist_rule_then_ends_Resisted()
    {
        var t = new Rig();
        t.StartAt(OtherBuff, Monster, CastRules.Once.WithResistRetries(1));
        t.Chat("Drudge Slinker resists your spell");
        Assert.Equal(CastEnd.Running, t.Sent());
        Assert.Equal(2, t.Sends);
        t.Chat("Drudge Slinker resists your spell");
        Assert.Equal(CastEnd.Resisted, t.Sent());
        Assert.Equal((2, 2), (t.A.Report.Tries, t.A.Report.Resists));

        var once = new Rig();
        once.StartAt(OtherBuff, Monster, CastRules.Once);
        once.Chat("Drudge Slinker resists your spell");
        Assert.Equal(CastEnd.Resisted, once.Sent());
    }

    [Theory]
    [InlineData("You cast Ice Armor on Drudge Slinker", "Drudge Slinker", CastProofResult.Proved)]
    [InlineData("You cast Ice Armor on Drudge Slinker, refreshing Ice Armor", "Drudge Slinker", CastProofResult.Proved)]
    [InlineData("You cast Ice Armor on Drudge Slinker, but it is surpassed by Ice Armor II", "Drudge Slinker", CastProofResult.Proved)]
    [InlineData("You cast Ice Armor on Drudge Slinker and dispel: Weakness.", "Drudge Slinker", CastProofResult.Proved)]
    [InlineData("You cast Ice Armor on Drudge Slinkers", "Drudge Slinker", CastProofResult.None)]
    [InlineData("You cast Ice Armor II on Drudge Slinker", "Drudge Slinker", CastProofResult.None)]
    [InlineData("You cast Ice Armor on yourself", null, CastProofResult.Proved)]
    [InlineData("You cast Ice Armor on yourself", "Drudge Slinker", CastProofResult.None)]
    [InlineData("You cast Ice Armor and restore 40 points of your Health.", null, CastProofResult.Proved)]
    [InlineData("With Ice Armor you restore 40 points of Health to Drudge Slinker.", "Drudge Slinker", CastProofResult.Proved)]
    [InlineData("With Ice Armor you drain 12 points of Health from Drudge Slinker.", "Drudge Slinker", CastProofResult.Proved)]
    [InlineData("With Ice Armor you restore 40 points of Health to Mosswart.", "Drudge Slinker", CastProofResult.None)]
    [InlineData("You lose 30 points of Stamina due to casting Ice Armor on Drudge Slinker", "Drudge Slinker", CastProofResult.Proved)]
    [InlineData("Drudge Slinker resists your spell", "Drudge Slinker", CastProofResult.Resisted)]
    [InlineData("Mosswart resists your spell", "Drudge Slinker", CastProofResult.None)]
    public void The_chat_proof_reads_ACE_s_caster_lines(string text, string? target, CastProofResult expected) =>
        Assert.Equal(expected, CastChatProof.Classify(text, "Ice Armor", target));

    // ------------------------------------------------------------------------------------------------ the tally

    [Fact]
    public void The_tally_sums_the_reports_into_the_fields()
    {
        var tally = new CastTally();
        tally.Add(new CastReport { End = CastEnd.Landed, Tries = 3, Fizzles = 1, ManaTries = 2, ManaRefusals = 1, ManaLanded = 1, Proved = 1, Seconds = 2 });
        tally.Add(new CastReport { End = CastEnd.Busy, Tries = 2, Busy = 2, Resists = 1 });
        tally.Add(new CastReport { End = CastEnd.Running, Tries = 9 }); // not ended: not counted
        Assert.Equal("mana_tries=2 mana_refusals=1 mana_landed=1 busy=2 resists=1 proved=1", tally.Fields);
        Assert.Equal((2, 1, 1, 5, 1), (tally.Attempts, tally.Landed, tally.Failed, tally.Tries, tally.Fizzles));
        tally.Clear();
        Assert.Equal("mana_tries=0 mana_refusals=0 mana_landed=0 busy=0 resists=0 proved=0", tally.Fields);
    }

    /// <summary>The Action API, a described player in the magic mode with a mana value, the test spells and an attempt with the chat proof.</summary>
    internal sealed class Rig
    {
        private byte _manaSeq;

        public Rig(uint mana = 100)
        {
            R = new WorldActionRig();
            var spells = new[]
            {
                WorldActionRig.Spell(BuffI, SpellType.Enchantment, SpellFlags.Beneficial | SpellFlags.SelfTargeted),
                WorldActionRig.Spell(BuffII, SpellType.Enchantment, SpellFlags.Beneficial | SpellFlags.SelfTargeted),
                WorldActionRig.Spell(BuffIII, SpellType.Enchantment, SpellFlags.Beneficial | SpellFlags.SelfTargeted),
                WorldActionRig.Spell(BoltI, SpellType.Projectile, SpellFlags.Projectile),
                WorldActionRig.Spell(BoltII, SpellType.Projectile, SpellFlags.Projectile),
                WorldActionRig.Spell(BoltIII, SpellType.Projectile, SpellFlags.Projectile),
                WorldActionRig.Spell(OtherBuff, SpellType.Enchantment, SpellFlags.Beneficial),
                WorldActionRig.Spell(Transfer, SpellType.Transfer, SpellFlags.Beneficial),
            };
            R.Spells(spells);
            Spells = spells.ToImmutableDictionary(s => s.Id);
            R.H.Describe(new PlayerDescriptionMsg { Vitals = [new VitalDesc(5, 100, 0, 0, mana)], Spells = spells.ToImmutableDictionary(s => s.Id, _ => 2f) }); // M6 15: the character knows the spells it casts (the player's rule puts each on a spell bar before its cast)
            R.CombatModeIs(CombatModes.Magic);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, ReducerHarness.Create(Monster, ReducerHarness.Stamps(), null, name: MonsterName), isUpdate: false);
            Proof = new CastChatProof(R.H.State, Spells);
            A = new CastAttempt(R.Api, R.H.State, R.H.Log, Spells);
        }

        public WorldActionRig R { get; }

        public ImmutableDictionary<uint, SpellInfo> Spells { get; }

        public CastChatProof Proof { get; }

        public CastAttempt A { get; }

        public double Now => R.H.R.Clock.Now;

        public int Sends => R.H.Net.Of(FakeSendKind.CastUntargetedSpell).Count + R.H.Net.Of(FakeSendKind.CastTargetedSpell).Count;

        public uint LastSpell => R.H.Net.Sends.Last(s => s.Kind is FakeSendKind.CastUntargetedSpell or FakeSendKind.CastTargetedSpell) is var s && s.Kind == FakeSendKind.CastTargetedSpell ? (uint)s.Index : (uint)s.Guid;

        public uint LastTarget => (uint)R.H.Net.Sends.Last(s => s.Kind == FakeSendKind.CastTargetedSpell).Guid;

        public string LastLog => R.H.Log.Entries.Last(e => e.Category == CastAttempt.LogCategory).Message;

        public CastEnd Start(CastRules rules, uint[] levels, int index)
        {
            var end = A.Start(new CastRequest { Levels = levels, Index = index, Rules = rules, Purpose = "test" }, Now);
            Pump();
            return end;
        }

        public void StartAt(uint spell, uint target, CastRules rules)
        {
            A.Start(new CastRequest { Levels = new[] { spell }, Target = target, Rules = rules, Proof = Proof }, Now);
            Pump();
        }

        /// <summary>Runs the Action API's update (the queued try goes out).</summary>
        public void Pump() => R.Tick();

        public CastEnd Step()
        {
            var end = A.Step(Now);
            return end;
        }

        /// <summary>ACE's answer to the try in flight, then the update and one step (which may send the next try), then the update again.</summary>
        private CastEnd After(Action answer)
        {
            answer();
            R.Tick();
            var end = A.Step(Now);
            R.Tick();
            return end;
        }

        public CastEnd Land(uint spell) => After(() =>
        {
            R.Enchant((ushort)spell);
            R.UseDone();
        });

        public CastEnd Projectile() => After(() => R.Create(0x80000777, 12));

        public CastEnd Fizzle() => After(() =>
        {
            R.WeenieError(0x0402);
            R.UseDone();
        });

        public CastEnd Answer(uint code) => After(() => R.UseDone(code));

        /// <summary>M5 7 D124: the target gone at the release: WeenieError 0x042C, then FinishCast's UseDone 0.</summary>
        public CastEnd NotAcquiredAtRelease() => After(() =>
        {
            R.WeenieError(CastAttempt.NotAcquiredCode);
            R.UseDone();
        });

        public CastEnd Sent() => Answer(0);

        public void Chat(string text, int chatType = (int)CastChatProof.MagicChatType) => R.Raw(AceWireWriters.ServerMessage(text, chatType));

        public void SetMana(uint mana) => R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_manaSeq, 6, mana));
    }
}

/// <summary>The cast policy's allocation checks (in the allocation collection: nothing else runs beside them).</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class CastAttemptAllocationTests
{
    [Fact]
    public void Step_allocates_nothing_while_a_try_is_in_flight_or_waits_over_a_whole_attempt_with_refusals_and_a_landing()
    {
        Run(out _); // warm the paths
        Run(out var bytes);
        Assert.Equal(0L, bytes);
    }

    [Fact]
    public void The_chat_proof_allocates_nothing()
    {
        var t = new CastAttemptTests.Rig();
        var mark = t.R.H.State.Chat.Total;
        t.Chat("Some other line");
        t.Chat("You cast Spell 501 on Drudge Slinker, surpassing Spell 500");
        var query = new CastProofQuery(CastAttemptTests.OtherBuff, CastAttemptTests.Monster, mark);
        var result = CastProofResult.None;
        AllocationAssert.Nothing(() => result = t.Proof.Check(in query));
        Assert.Equal(CastProofResult.Proved, result);
    }

    // One dance: three refusals, a landing proved by the chat; every idle step (the try in flight, the spacing) measured.
    private static void Run(out long bytes)
    {
        bytes = 0;
        var t = new CastAttemptTests.Rig(mana: 5);
        var request = new CastRequest { Levels = new[] { CastAttemptTests.Transfer }, Rules = CastRules.ManaDance(10, 0.25), ManaCeilings = new uint[] { 70 }, Proof = t.Proof };
        t.A.Start(request, t.Now);
        t.Pump();
        for (var press = 1; press <= 4; press++)
        {
            bytes += Idle(t, 3); // in flight
            if (press < 4)
            {
                t.R.UseDone(0x0401);
                t.R.Tick();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var end = t.A.Step(t.Now); // the refusal: a decision and no log line (the wait is silent)
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(CastEnd.Running, end);
                bytes += Idle(t, 3); // the spacing
                t.R.Tick(0.25);
                t.A.Step(t.Now); // the send: the Action API's handle and the try's log line
                t.Pump();
            }
        }

        t.Chat("You cast Spell 601 on yourself and lose 50 points of stamina and also gain 45 points of mana");
        t.R.UseDone();
        t.R.Tick();
        Assert.Equal(CastEnd.Landed, t.A.Step(t.Now)); // the end's log line
        bytes += Idle(t, 3); // after the end
    }

    private static long Idle(CastAttemptTests.Rig t, int frames)
    {
        var bytes = 0L;
        for (var i = 0; i < frames; i++)
        {
            var now = t.Now;
            var before = GC.GetAllocatedBytesForCurrentThread();
            t.A.Step(now);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return bytes;
    }
}

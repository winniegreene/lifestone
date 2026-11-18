using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The player's weenie read from the session's qualities (step ST16): the load, the Run and Jump skills, the run rate,
/// the jump, the stamina, and that it follows the qualities as they change.
/// </summary>
public sealed class QualitiesWeenieTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Endurance = 2;
    private const uint Quickness = 3;
    private const uint Coordination = 4;
    private const uint Untrained = 1;
    private const uint Trained = 2;
    private const uint Specialized = 3;

    /// <summary>Run: (Endurance + Quickness) / 3, usable untrained (the shape of the retail table's rule, with stand-in numbers).</summary>
    private static readonly SkillRule RunRule = new(new SkillTableFormula(0, 1, 1, 3, Endurance, Quickness), MinLevel: 1);

    /// <summary>Jump: (Strength + Coordination) / 2, usable only trained.</summary>
    private static readonly SkillRule JumpRule = new(new SkillTableFormula(0, 1, 1, 2, QualitiesWeenie.StrengthAttributeId, Coordination), MinLevel: 2);

    private static PlayerQualities Known(uint strength = 50, uint endurance = 60, uint quickness = 90, uint coordination = 40)
    {
        var q = new PlayerQualities();
        q.Attributes[QualitiesWeenie.StrengthAttributeId] = new AttributeDesc(QualitiesWeenie.StrengthAttributeId, Ranks: 5, Start: strength - 5, Experience: 0);
        q.Attributes[Endurance] = new AttributeDesc(Endurance, 0, endurance, 0);
        q.Attributes[Quickness] = new AttributeDesc(Quickness, 0, quickness, 0);
        q.Attributes[Coordination] = new AttributeDesc(Coordination, 0, coordination, 0);
        q.Skills[QualitiesWeenie.RunSkillId] = Skill(QualitiesWeenie.RunSkillId, Trained, ranks: 10, init: 2);
        q.Skills[QualitiesWeenie.JumpSkillId] = Skill(QualitiesWeenie.JumpSkillId, Trained, ranks: 0, init: 0);
        q.Vitals[QualitiesWeenie.StaminaVitalId] = new VitalDesc(QualitiesWeenie.StaminaVitalId, 0, 100, 0, Current: 100);
        q.IsKnown = true;
        return q;
    }

    private static SkillDesc Skill(uint id, uint advancement, ushort ranks, uint init) =>
        new(id, ranks, 1, advancement, 0, init, 0, 0);

    private static QualitiesWeenie Weenie(PlayerQualities q, ILogSink? log = null) => new(q, RunRule, JumpRule, log);

    [Fact]
    public void Before_the_description_it_cannot_tell_and_the_interpreter_keeps_its_own_rate()
    {
        var weenie = Weenie(new PlayerQualities());
        Assert.False(weenie.TryGetRunRate(out _));
        Assert.False(weenie.TryGetJumpVelocity(1f, out _));
        Assert.True(weenie.CanJump(1f));
        Assert.True(weenie.TryGetJumpStaminaCost(1f, out var cost));
        Assert.Equal(0, cost);
        Assert.Equal(0, weenie.RunSkill);
    }

    [Fact]
    public void It_is_the_clients_own_player_and_a_plain_one()
    {
        var weenie = Weenie(Known());
        Assert.True(weenie.IsThePlayer);
        Assert.True(weenie.IsCreature);
        Assert.True(weenie.IsPlayer);
        Assert.False(weenie.IsPK);
        Assert.False(weenie.IsPKLite);
        Assert.False(weenie.IsImpenetrable);
    }

    [Fact]
    public void The_run_skill_is_the_formula_over_the_attributes_plus_the_start_level_and_the_ranks()
    {
        var weenie = Weenie(Known());

        // (60 + 90) / 3 = 50, + 10 ranks + 2 start (init) - CACQualities::InqSkill adds nothing for the class.
        Assert.Equal(62, weenie.RunSkill);
        Assert.True(weenie.TryGetRunRate(out var rate));
        Assert.Equal((float)MovementSystem.GetRunRate(0f, 62, 1f), rate, 5);
        Assert.InRange(rate, 1.65f, 1.67f); // (62 / 262 x 11 + 4) / 4
    }

    [Theory]
    [InlineData(Untrained, 62)]
    [InlineData(Trained, 62)]
    [InlineData(Specialized, 62)]
    public void The_advancement_class_adds_no_bonus_of_its_own_because_the_creation_bonus_is_in_init(uint advancement, int expected)
    {
        var q = Known();
        q.Skills[QualitiesWeenie.RunSkillId] = Skill(QualitiesWeenie.RunSkillId, advancement, ranks: 10, init: 2);
        Assert.Equal(expected, Weenie(q).RunSkill);
    }

    [Theory]
    [InlineData(Trained, 5, 0)]      // what the live ACE sends for a Run trained at creation: ranks 5, init 0 (recorded in the loopback run)
    [InlineData(Trained, 0, 5)]      // the creation bonus in init instead: the same level
    [InlineData(Specialized, 10, 0)] // and 10 for a specialised skill
    public void A_created_characters_skill_counts_its_creation_bonus_once(uint advancement, ushort ranks, uint init)
    {
        var q = Known(endurance: 55, quickness: 55);
        q.Skills[QualitiesWeenie.RunSkillId] = Skill(QualitiesWeenie.RunSkillId, advancement, ranks, init);

        // The formula over 55 and 55, plus the 5 or 10 the record holds once: not plus the class again.
        Assert.True(RunRule.Formula.TryCalculate(55, 55, out var formula));
        Assert.Equal((int)formula + (advancement == Specialized ? 10 : 5), Weenie(q).RunSkill);
    }

    [Fact]
    public void A_skill_below_its_minimum_class_or_missing_counts_as_zero_and_runs_at_the_base_rate()
    {
        var q = Known();
        q.Skills[QualitiesWeenie.JumpSkillId] = Skill(QualitiesWeenie.JumpSkillId, Untrained, ranks: 20, init: 0);
        var weenie = Weenie(q);
        Assert.Equal(0, weenie.JumpSkill); // Jump needs training in this table

        q.Skills.Remove(QualitiesWeenie.RunSkillId);
        Assert.Equal(0, weenie.RunSkill);
        Assert.True(weenie.TryGetRunRate(out var rate));
        Assert.Equal(1f, rate, 5);
    }

    [Fact]
    public void The_jump_speed_follows_the_jump_height_formula()
    {
        var q = Known();
        var weenie = Weenie(q);

        // (50 + 40) / 2 = 45 (init 0, no ranks).
        Assert.Equal(45, weenie.JumpSkill);
        Assert.True(weenie.TryGetJumpVelocity(1f, out var full));
        Assert.Equal((float)Math.Sqrt(MovementSystem.GetJumpHeight(0f, 45, 1f, 1f) * 19.6), full, 5);
        Assert.True(weenie.TryGetJumpVelocity(0.5f, out var half));
        Assert.True(half < full);
        Assert.True(weenie.TryGetJumpVelocity(0f, out var none));
        Assert.Equal((float)Math.Sqrt(0.35 * 19.6), none, 5); // GetJumpHeight is at least 0.35
    }

    [Theory]
    [InlineData(0, 0f, 1.0)]
    [InlineData(8250, 1f, 1.0)]       // capacity is 150 x 55 = 8250 at Strength 55
    [InlineData(12375, 1.5f, 0.5)]
    [InlineData(16500, 2f, 0.0)]
    public void The_burden_over_the_capacity_is_the_load_that_slows_running(int burden, float load, double loadMod)
    {
        var q = Known(strength: 55);
        q.Ints[QualitiesWeenie.EncumbranceValueProperty] = burden;
        var weenie = Weenie(q);
        Assert.Equal(load, weenie.Load, 4);
        Assert.True(weenie.TryGetRunRate(out var rate));
        Assert.Equal((float)(((loadMod * (weenie.RunSkill / (weenie.RunSkill + 200.0) * 11.0)) + 4.0) / 4.0), rate, 4);
        Assert.Equal(load < 2f, weenie.CanJump(1f));
    }

    [Fact]
    public void A_character_with_no_strength_is_overloaded()
    {
        var q = Known();
        q.Attributes.Remove(QualitiesWeenie.StrengthAttributeId);
        var weenie = Weenie(q);
        Assert.Equal(3f, weenie.Load); // EncumbranceSystem.Load: no capacity is a load of 3
        Assert.False(weenie.CanJump(1f));
    }

    [Fact]
    public void The_stamina_cost_is_payable_while_the_current_stamina_covers_it()
    {
        var q = Known();
        var weenie = Weenie(q);

        Assert.True(weenie.TryGetJumpStaminaCost(1f, out var cost));
        Assert.Equal((int)MovementSystem.JumpStaminaCost(1f, 0f, playerKiller: false), cost);
        Assert.Equal(6, cost); // ceil((0 + 0.5) x 1 x 8 + 2)

        q.Vitals[QualitiesWeenie.StaminaVitalId] = q.Vitals[QualitiesWeenie.StaminaVitalId] with { Current = 5 };
        Assert.False(weenie.TryGetJumpStaminaCost(1f, out cost));
        Assert.Equal(6, cost);

        q.Vitals[QualitiesWeenie.StaminaVitalId] = q.Vitals[QualitiesWeenie.StaminaVitalId] with { Current = 6 };
        Assert.True(weenie.TryGetJumpStaminaCost(1f, out _));
    }

    [Fact]
    public void It_reads_the_qualities_afresh_so_a_private_update_changes_the_next_answer()
    {
        var q = Known();
        var weenie = Weenie(q);
        Assert.True(weenie.TryGetRunRate(out var before));

        q.Skills[QualitiesWeenie.RunSkillId] = Skill(QualitiesWeenie.RunSkillId, Trained, ranks: 100, init: 2);
        Assert.True(weenie.TryGetRunRate(out var after));
        Assert.True(after > before, $"{after} vs {before}");

        q.Clear(); // a new description is coming: it cannot tell again
        Assert.False(weenie.TryGetRunRate(out _));
    }

    /// <summary>
    /// M6 14 pits: the run rate and the jump read the buffs, as retail's <c>InqSkill</c> with <c>raw</c> false does (Sprint Self and Quickness Self raise the run, Jumping Mastery Self and Strength Self the jump; the load's
    /// Strength too); the base stays the bare rule. Stand-in buffs: Quickness +30 (an attribute's additive 0x9001) and Run +40 and Jump +20 (a skill's additive 0x9010).
    /// </summary>
    [Fact]
    public void The_buffs_raise_the_run_and_the_jump_and_the_base_stays_bare()
    {
        var q = Known();
        var weenie = Weenie(q);
        Assert.True(weenie.TryGetRunRate(out var bareRate));
        Assert.True(weenie.TryGetJumpVelocity(1f, out var bareJump));
        Assert.Equal((62, 62), (weenie.RunSkill, weenie.RunSkillBase));
        Assert.Equal((45, 45), (weenie.JumpSkill, weenie.JumpSkillBase)); // (50 + 40) / 2, no ranks

        q.Enchantments = [Buff(1, 0x9001, Quickness, 30f), Buff(2, 0x9010, QualitiesWeenie.RunSkillId, 40f), Buff(3, 0x9010, QualitiesWeenie.JumpSkillId, 20f)];
        Assert.Equal(62, weenie.RunSkillBase);
        Assert.Equal(112, weenie.RunSkill); // (60 + 120) / 3 + 12, + 40
        Assert.Equal(65, weenie.JumpSkill);
        Assert.True(weenie.TryGetRunRate(out var rate));
        Assert.True(weenie.TryGetJumpVelocity(1f, out var jump));
        Assert.Equal((float)MovementSystem.GetRunRate(0f, 112, 1f), rate, 4);
        Assert.True(rate > bareRate && jump > bareJump, $"{rate} {jump} vs {bareRate} {bareJump}");
    }

    private static EnchantmentDesc Buff(ushort spell, uint type, uint key, float value) =>
        new(spell, 1, spell, 0, 300, 0.0, 3600.0, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, EnchantmentBucket.Additive);

    [Fact]
    public void The_unread_qualities_are_logged_once_as_named_TODOs()
    {
        var log = new RecordingLog();
        var weenie = Weenie(Known(), log);
        Assert.Empty(log.Lines);
        Assert.True(weenie.TryGetRunRate(out _));
        Assert.True(weenie.TryGetJumpVelocity(1f, out _));
        var line = Assert.Single(log.Lines);
        Assert.All(["state-carry-augmentation", "state-pk-status"], todo => Assert.Contains($"TODO({todo})", line));
        Assert.All(["state-enchantments", "state-enlightenment"], todo => Assert.DoesNotContain($"TODO({todo})", line)); // M6 14 pits: the buffs and Enlightenment count (SkillRules.TryCurrent)
    }

    [Fact]
    public void A_weenie_needs_its_qualities()
    {
        Assert.Throws<ArgumentNullException>(() => new QualitiesWeenie(null!, RunRule, JumpRule));
    }

    [RequiresDatsFact]
    public void The_dats_SkillTable_gives_Run_and_Jump_their_rules_and_a_new_character_runs_at_retails_rate()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        Assert.True(skills.TryGetSkill(QualitiesWeenie.RunSkillId, out var run));
        Assert.True(skills.TryGetSkill(QualitiesWeenie.JumpSkillId, out var jump));
        var q = new PlayerQualities();
        foreach (var attribute in new uint[] { 1, 2, 3, 4, 5, 6 })
        {
            q.Attributes[attribute] = new AttributeDesc(attribute, 0, 55, 0);
        }

        q.Skills[QualitiesWeenie.RunSkillId] = Skill(QualitiesWeenie.RunSkillId, Trained, 0, 5); // ACE's record for a skill trained at creation: the 5 is in ranks or init, never both and never the class on top
        q.Skills[QualitiesWeenie.JumpSkillId] = Skill(QualitiesWeenie.JumpSkillId, Trained, 0, 5);
        q.IsKnown = true;
        var weenie = QualitiesWeenie.FromSkillTable(q, skills);

        Assert.True(run.Formula.TryCalculate(55, 55, out var runBase));
        Assert.True(jump.Formula.TryCalculate(55, 55, out var jumpBase));
        Assert.Equal((int)runBase + 5, weenie.RunSkill);
        Assert.Equal((int)jumpBase + 5, weenie.JumpSkill);
        Assert.True(weenie.TryGetRunRate(out var rate));
        Assert.InRange(rate, 1.5f, 2.2f); // a new character with 55s runs a little faster than 1.0, not at an expert's 3
        Assert.True(weenie.TryGetJumpVelocity(1f, out var vz));
        Assert.InRange(vz, 3f, 8f);
    }

    private sealed class RecordingLog : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }
}

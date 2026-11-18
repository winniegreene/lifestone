using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Objects;

namespace Lifestone.Client.Session;

/// <summary>What <see cref="QualitiesWeenie"/> needs of one SkillTable entry to compute the skill's level.</summary>
/// <param name="Formula">The skill's base-value formula (<see cref="SkillTableEntry.Formula"/>).</param>
/// <param name="MinLevel">The lowest advancement class at which the skill is usable (<see cref="SkillTableEntry.MinLevel"/>).</param>
public readonly record struct SkillRule(SkillTableFormula Formula, uint MinLevel)
{
    /// <summary>The rule of <paramref name="entry"/>.</summary>
    public static SkillRule Of(SkillTableEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new SkillRule(entry.Formula, entry.MinLevel);
    }
}

/// <summary>
/// The logged-in character's weenie for physics (step ST16; owner decision D16; ST13's <c>QualitiesWeenie</c>, moved
/// here): <see cref="IPhysicsWeenie"/> answered from the session's <see cref="PlayerQualities"/> (<c>Self.Qualities</c>),
/// read afresh at every question, so a PrivateUpdate that raises the Run skill, the burden or the stamina is felt by the
/// next movement. The player's body (<c>PhysicsObj</c>, via <c>OfflineWalker.Create</c>) is given it in place of
/// <see cref="Movement.OfflinePlayerWeenie"/>'s placeholder skills.
/// </summary>
/// <remarks>
/// <para><b>What it answers</b>, re-expressing <c>CACQualities::InqRunRate</c> and <c>InqJumpVelocity</c> (11.4186
/// 0x00592800 and 0x00592980; 11.6096: not in the symbol table, TODO(physics) in retail-addresses-m2.md), <c>CanJump</c>
/// (0x00591B50 / 0x00592850) and <c>JumpStaminaCost</c> (0x00591B90 / 0x00592890) with the formulas of
/// <see cref="MovementSystem"/> and <see cref="EncumbranceSystem"/>:</para>
/// <list type="bullet">
/// <item>The <b>load</b> is the burden (PropertyInt <see cref="EncumbranceValueProperty"/>) over the capacity
/// <see cref="EncumbranceSystem.EncumbranceCapacity"/> of the current Strength (start plus ranks), with no carrying-capacity
/// augmentations (TODO(state-carry-augmentation)).</item>
/// <item>The <b>run rate</b> is <see cref="MovementSystem.GetRunRate"/> of the load and the Run skill (24); the <b>jump
/// speed</b> is <c>sqrt(</c><see cref="MovementSystem.GetJumpHeight"/> of the load, the Jump skill (22) and the extent
/// <c>× 19.6)</c>; <c>CanJump</c> holds below a load of 2; the stamina cost is <see cref="MovementSystem.JumpStaminaCost"/>
/// and is payable while the current stamina (vital 3's current value) covers it.</item>
/// <item>A <b>skill's level</b> is what <c>CACQualities::InqSkill</c> (11.4186 0x00591DF0, 11.6096 0x00592AF0) adds up: the
/// skill's formula over the current attributes (start plus ranks, enchanted: see Buffs below), plus the skill's start level (<c>_init_level</c>, the
/// PlayerDescription's <c>init</c> field) and the levels from its ranks (<c>_level_from_pp</c>). It adds <b>no class
/// bonus</b>: the 5 (trained) or 10 (specialised) that <c>CharGenState::GetSkillScore</c> (11.4186 0x005C4B50, 11.6096
/// 0x005C5B30) shows on the character-creation screen is the creation-time value the server already sends in the skill's
/// record (live, a character created with Run trained arrives with ranks 5, init 0, class 2: the 5 is in the record), so
/// adding the class again counted it twice (Run 65 and a run rate of 1.675 where retail has Run 60 and 1.635). A skill below its
/// minimum advancement class, or missing from the description, is 0 (a run rate of 1.0).</item>
/// <item>Before the PlayerDescription arrives (<see cref="PlayerQualities.IsKnown"/> is false) the run rate and jump speed
/// are unknown, so the interpreter runs at <see cref="MotionInterpreter.MyRunRate"/> and the stock jump speed.</item>
/// </list>
/// <para><b>Buffs</b> (M6 14 pits: <c>TODO(state-enchantments)</c> and <c>TODO(state-enlightenment)</c> are closed for the weenie). Retail's <c>InqRunRate</c> and <c>InqJumpVelocity</c> ask
/// <c>CACQualities::InqSkill</c> with <c>raw</c> false, so the enchantment registry counts: Sprint Self and Quickness Self raise the run rate, Jumping Mastery Self, Strength Self and
/// Coordination Self the jump (the skill is <see cref="SkillRules.TryCurrent"/>, the one rule the skills page and the cast planner read: the attributes enchanted, the skill's multiplicative and
/// additive enchantments, the vitae, the augmentation and Enlightenment bonuses). The load's Strength is enchanted too (<see cref="EnchantmentRules.EnchantAttribute"/>; ACE's
/// <c>HandleActionJump</c> reads <c>Strength.Current</c>, Player.cs:870 @95ef426d). Before this the climber ran and jumped on her bare skills: Qwinnie at level 47 (Quickness 15, Run and
/// Jump 8 ranks each) ran at Run 23, 5.1 m/s, and jumped at Jump 98, where the buff bot's level VII set makes them Run 103, 7.7 m/s, and Jump 178.</para>
/// <para><b>Not read yet</b> (each a named TODO, logged once when the first answer is given): the carrying-capacity augmentations (TODO(state-carry-augmentation)); and the player-killer status that
/// <see cref="IsPK"/> and <see cref="IsPKLite"/> need (TODO(state-pk-status): both are false, so the player passes through other players as a non-player-killer does).</para>
/// <para>Game thread only, like the qualities.</para>
/// </remarks>
public sealed class QualitiesWeenie : IPhysicsWeenie
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "weenie";

    /// <summary>The Run skill's id.</summary>
    public const uint RunSkillId = 24;

    /// <summary>The Jump skill's id.</summary>
    public const uint JumpSkillId = 22;

    /// <summary>The Strength attribute's id.</summary>
    public const uint StrengthAttributeId = 1;

    /// <summary>The Max Stamina vital's id (the description keys a vital by its maximum; its record carries the current value).</summary>
    public const uint StaminaVitalId = 3;

    /// <summary>PropertyInt <c>EncumbranceVal</c>: the burden carried.</summary>
    public const uint EncumbranceValueProperty = 5;

    private readonly PlayerQualities _qualities;
    private readonly SkillRule _run;
    private readonly SkillRule _jump;
    private readonly ILogSink _log;
    private bool _todosLogged;

    /// <summary>A weenie over <paramref name="qualities"/> with the rules of the Run and Jump skills.</summary>
    /// <param name="qualities">The session's qualities (<c>Self.Qualities</c>).</param>
    /// <param name="run">The Run skill's rule.</param>
    /// <param name="jump">The Jump skill's rule.</param>
    /// <param name="log">Where the unread-quality TODOs are logged once, or null.</param>
    public QualitiesWeenie(PlayerQualities qualities, SkillRule run, SkillRule jump, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        _qualities = qualities;
        _run = run;
        _jump = jump;
        _log = log ?? NullLogSink.Instance;
    }

    /// <summary>A weenie over <paramref name="qualities"/> with the Run and Jump rules of the dats' SkillTable.</summary>
    /// <exception cref="InvalidDataException">The SkillTable has no Run or no Jump skill.</exception>
    public static QualitiesWeenie FromSkillTable(PlayerQualities qualities, SkillTableData skills, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        return new QualitiesWeenie(qualities, RuleOf(skills, RunSkillId, "Run"), RuleOf(skills, JumpSkillId, "Jump"), log);
    }

    /// <inheritdoc/>
    public bool IsCreature => true;

    /// <inheritdoc/>
    public bool IsThePlayer => true;

    /// <inheritdoc/>
    public bool IsPlayer => true;

    /// <inheritdoc/>
    /// <remarks>TODO(state-pk-status): always false; see the class remarks.</remarks>
    public bool IsPK => false;

    /// <inheritdoc/>
    /// <remarks>TODO(state-pk-status): always false; see the class remarks.</remarks>
    public bool IsPKLite => false;

    /// <inheritdoc/>
    public bool IsImpenetrable => false;

    /// <summary>The Run skill's level now, buffs counted (0 before the description), as the run rate uses it.</summary>
    public int RunSkill => SkillLevel(_run, RunSkillId);

    /// <summary>The Jump skill's level now, buffs counted (0 before the description), as the jump speed uses it.</summary>
    public int JumpSkill => SkillLevel(_jump, JumpSkillId);

    /// <summary>The Run skill's base level (no buffs: <c>InqSkill</c> with <c>raw</c> true): the formula over the bare attributes, the start level and the ranks.</summary>
    public int RunSkillBase => BaseLevel(_run, RunSkillId);

    /// <summary>The Jump skill's base level (no buffs).</summary>
    public int JumpSkillBase => BaseLevel(_jump, JumpSkillId);

    /// <summary>The load (burden over capacity) now: 0 before the description.</summary>
    public float Load
    {
        get
        {
            if (!_qualities.IsKnown)
            {
                return 0f;
            }

            _ = _qualities.TryGetInt(EncumbranceValueProperty, out var burden);
            var strength = (int)EnchantmentRules.EnchantAttribute(_qualities.Enchantments, StrengthAttributeId, (uint)Attribute(StrengthAttributeId));
            return (float)EncumbranceSystem.Load(EncumbranceSystem.EncumbranceCapacity(strength, augmentations: 0), burden);
        }
    }

    /// <inheritdoc/>
    public bool TryGetRunRate(out float rate)
    {
        if (!Known())
        {
            rate = 1f;
            return false;
        }

        rate = (float)MovementSystem.GetRunRate(Load, RunSkill, 1f);
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetJumpVelocity(float extent, out float velocityZ)
    {
        if (!Known())
        {
            velocityZ = 0f;
            return false;
        }

        velocityZ = (float)Math.Sqrt(MovementSystem.GetJumpHeight(Load, JumpSkill, extent, 1f) * 19.6);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Retail allows a jump below a load of 2 (<c>CACQualities::CanJump</c>); before the description it allows it.</remarks>
    public bool CanJump(float extent) => !_qualities.IsKnown || Load < 2f;

    /// <inheritdoc/>
    public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
    {
        if (!Known())
        {
            staminaCost = 0;
            return true;
        }

        staminaCost = (int)MovementSystem.JumpStaminaCost(extent, Load, playerKiller: false);
        return _qualities.TryGetVital(StaminaVitalId, out var stamina) && stamina.Current >= staminaCost;
    }

    private static SkillRule RuleOf(SkillTableData skills, uint id, string name) =>
        skills.TryGetSkill(id, out var entry)
            ? SkillRule.Of(entry)
            : throw new InvalidDataException($"the SkillTable has no {name} skill ({id}); the player's weenie cannot be made");

    /// <summary>True once the description arrived; logs the unread qualities' TODOs on the first true.</summary>
    private bool Known()
    {
        if (!_qualities.IsKnown)
        {
            return false;
        }

        if (!_todosLogged)
        {
            _todosLogged = true;
            _log.Warn(
                LogCategory,
                "TODO(state-carry-augmentation): the carrying-capacity augmentations are not counted; TODO(state-pk-status): IsPK and IsPKLite are false");
        }

        return true;
    }

    private int Attribute(uint id) => _qualities.TryGetAttribute(id, out var a) ? checked((int)(a.Start + a.Ranks)) : 0;

    /// <summary>
    /// <c>CACQualities::InqSkill</c> with <c>raw</c> false: <see cref="SkillRules.TryCurrent"/> (the formula over the enchanted attributes, the start level and the ranks, the skill's enchantments, the
    /// vitae and the bonuses); the class is only the usability gate. A description that lacks an attribute of the formula falls back to the bare sum with that attribute 0.
    /// </summary>
    private int SkillLevel(SkillRule rule, uint id)
    {
        if (!_qualities.IsKnown || !_qualities.TryGetSkill(id, out var skill) || skill.AdvancementClass < rule.MinLevel)
        {
            return 0;
        }

        return SkillRules.TryCurrent(_qualities, id, rule.Formula, rule.MinLevel, out var current) ? (int)Math.Min(current, int.MaxValue) : BaseLevel(rule, id);
    }

    /// <summary><c>CACQualities::InqSkill</c> with <c>raw</c> true: the formula over the bare attributes (start plus ranks) plus the start level and the ranks; the class is only the usability gate.</summary>
    private int BaseLevel(SkillRule rule, uint id)
    {
        if (!_qualities.IsKnown || !_qualities.TryGetSkill(id, out var skill) || skill.AdvancementClass < rule.MinLevel)
        {
            return 0;
        }

        long level = (long)skill.Init + skill.Ranks;
        if (rule.Formula.TryCalculate(
                (uint)Attribute(rule.Formula.Attribute1), (uint)Attribute(rule.Formula.Attribute2), out var formula))
        {
            level += formula;
        }

        return (int)Math.Min(level, int.MaxValue);
    }
}

using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>What a piece's great cantrip useful to her does for her on her body (<see cref="GreatOnBody"/>).</summary>
public enum GreatOnBodyKind
{
    /// <summary>The piece casts no great cantrip useful to her (or the judge reads none: a judge of no session): nothing to count.</summary>
    None,

    /// <summary>Its spells are on her now: her registry holds one cast by the piece (<see cref="WearScore.LitOn"/>, <see cref="GearLight"/>).</summary>
    Lit,

    /// <summary>ACE would activate it at a wield now: its item mana, her Arcane Lore and every limit are met (<see cref="WearScore.SpellsOff"/> names nothing).</summary>
    Lights,

    /// <summary>
    /// It cannot light yet, for want of something that can come: her Arcane Lore (the experience spend raises it toward such a piece, <see cref="LoreSpend"/>), her level, the level of a skill she holds or her plan trains, an
    /// attribute, a vital, or the piece's mana (the worn items' refill). Kept worn, or wanted, as before the two questions were told apart.
    /// </summary>
    NotYet,

    /// <summary>It can never light for her as she is built (<see cref="GreatOnBody.NeverActivates"/>): a limit on a skill she does not hold trained and her plan does not train, or another heritage's piece.</summary>
    Never,
}

/// <summary>
/// M6 16 E5e5 (the owner's decision of 2026-11-18: a piece with a useful great cantrip she cannot activate is kept, never sold and never dropped, but it holds no worn slot against a piece whose magic works for her: she
/// wears what works). Two questions that were one, each asked by name:
/// <list type="bullet">
/// <item><b>Is this a keeper</b> (never sold, never dropped, kept through the make-room rule, the sale, the junk drop, the pack sorter and the villa's store list): a great cantrip useful to her, whether or not she can
/// activate the piece. Asked of <see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/>, unchanged; a judge carries that read as
/// <see cref="SelfEquipJudge.UsefulGreat"/>.</item>
/// <item><b>Does its great cantrip count for her on her body</b> (this record, <see cref="HoldsSlot"/>): a keeper whose spells are on her, or that ACE would light at a wield now, or that cannot light yet. The two guards of
/// E5e2 ask it (a worn piece that holds its slot is never taken off for a piece that does not), and so does the wear pick's choice between two sides worth the same.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>"Cannot yet" against "can never".</b> ACE checks a piece's activation requirements at the wield, each against her numbers of that moment (<c>CheckUseRequirements</c>, WorldObject_Use.cs:191-324 @95ef426d). "Never" is
/// said only of what her build does not move (<see cref="NeverActivates"/>): a requirement that names a skill she neither holds trained or specialised nor has in her plan (<see cref="CantripPlan"/>), or a heritage
/// that is not hers. Loot armour's own limit is such a one for a mage: ACE rolls it on Melee Defense at the piece's spellcraft plus 20, or on Missile Defense at 0.7 of that
/// (<c>TryMutate_ItemSkillLimit</c>, LootGenerationFactory_Magic.cs:177-203). Everything else that keeps its spells off is "not yet": her Arcane Lore above all (trained, raised by her experience), her level, a held or
/// planned skill, an attribute, a vital, the item's mana.</para>
/// <para><b>The verdict is read at each ask, never remembered.</b> An untrained defence still has a value (ACE's formula on Quickness and Coordination, her buffs in, <see cref="QualitiesSkills"/>): the day it meets the
/// limit, <see cref="SelfEquipJudge.ActivationUnmet"/> names nothing and the piece is <see cref="GreatOnBodyKind.Lights"/> again. A skill whose value this code cannot read is never called "never".</para>
/// </remarks>
/// <param name="Kind">The answer.</param>
/// <param name="Why">The reason with its names and numbers, for the log.</param>
public readonly record struct GreatOnBody(GreatOnBodyKind Kind, string Why)
{
    /// <summary>True when the piece's great cantrip counts for her on her body: lit, or ACE would light it now, or it cannot light yet (the lore-spend case, kept as before). False for <see cref="GreatOnBodyKind.Never"/> and for a piece with none.</summary>
    public bool HoldsSlot => Kind is GreatOnBodyKind.Lit or GreatOnBodyKind.Lights or GreatOnBodyKind.NotYet;

    /// <summary>The guards' words for a candidate that does not hold a slot: it casts no great cantrip useful to her, or its own can never light.</summary>
    /// <param name="candidate">The candidate's answer.</param>
    public static string CastsNone(GreatOnBody candidate) =>
        candidate.Kind == GreatOnBodyKind.Never ? "this piece's own great cantrip can never light for her (" + candidate.Why + ")" : "this piece casts none";

    /// <summary>The line a verdict carries when a worn keeper's great cantrip can never light: why it holds no slot, and that it stays hers.</summary>
    /// <param name="piece">The worn piece.</param>
    /// <param name="on">Its answer (<see cref="GreatOnBodyKind.Never"/>).</param>
    public static string NoSlot(WornPiece piece, GreatOnBody on)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"the worn '{piece.Name}' 0x{piece.Guid:X8} casts a great cantrip useful to her that can never light for her ({on.Why}): it holds no slot against a piece that works and is judged on what it gives; taken off it is kept (never sold, never dropped)");
    }

    /// <summary>The answer for one piece.</summary>
    /// <param name="keeper">The first question's answer: it casts a great cantrip useful to her (<see cref="SelfEquipJudge.UsefulGreat"/>).</param>
    /// <param name="lit">True when her registry holds a spell cast by the piece (<see cref="GearContext.LitBy"/>).</param>
    /// <param name="appraisal">The piece's appraisal, or null.</param>
    /// <param name="skills">Her numbers.</param>
    /// <param name="plan">The skills her plan trains; null: <see cref="CantripPlan.Mage"/> (the keeper question's own default).</param>
    public static GreatOnBody Of(bool keeper, bool lit, Appraisal? appraisal, IGearSkills skills, CantripPlan? plan = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (!keeper)
        {
            return new(GreatOnBodyKind.None, "it casts no great cantrip useful to her");
        }

        if (lit)
        {
            return new(GreatOnBodyKind.Lit, "its spells are on her");
        }

        if (appraisal is not { IsNotFound: false } a)
        {
            return new(GreatOnBodyKind.NotYet, "no appraisal of it is held: what keeps its spells off is not read");
        }

        if (NeverActivates(a, skills, plan) is { } never)
        {
            return new(GreatOnBodyKind.Never, never);
        }

        return WearScore.SpellsOff(a, skills) is { } off
            ? new(GreatOnBodyKind.NotYet, "not yet: " + off)
            : new(GreatOnBodyKind.Lights, "ACE would activate it at a wield now");
    }

    /// <summary>
    /// Why ACE can never activate the piece for her as she is built, or null when nothing says so (see the remarks): the first of its skill limit (ItemSkillLevelLimit on the appraisal's skill, WorldObject_Use.cs:214-229
    /// @95ef426d) read under the limit on a skill she neither holds trained nor plans; a skill it asks trained (UseRequiresSkill, :231-241) or specialised (:254-286) that she neither holds trained nor plans; a heritage
    /// that is not hers (:314-324). Her Arcane Lore is never a reason here: it rises.
    /// </summary>
    /// <param name="appraisal">The piece's appraisal.</param>
    /// <param name="skills">Her numbers.</param>
    /// <param name="plan">The skills her plan trains; null: <see cref="CantripPlan.Mage"/>.</param>
    public static string? NeverActivates(Appraisal appraisal, IGearSkills skills, CantripPlan? plan = null)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(skills);
        var trains = plan ?? CantripPlan.Mage;
        bool Outside(uint skill) => skills.AdvancementClass(skill) < SkillRules.TrainedClass && !trains.Skills.Contains(skill);
        string NotHers(uint skill) => string.Create(CultureInfo.InvariantCulture, $"she does not hold {SelfEquipJudge.SkillName(skill)} trained and her plan does not train it ({trains.Source})");

        if (appraisal.Int(SelfEquipJudge.ItemSkillLevelLimitInt) is { } limit and > 0 && SelfEquipJudge.ItemSkillLimitOf(appraisal) is { } limitSkill && skills.Current(limitSkill) is { } value && value < limit && Outside(limitSkill))
        {
            return string.Create(CultureInfo.InvariantCulture, $"its magic asks {SelfEquipJudge.SkillName(limitSkill)} {limit} and hers is {value}: {NotHers(limitSkill)}");
        }

        if (appraisal.Int(SelfEquipJudge.UseRequiresSkillInt) is { } use and > 0 && Outside((uint)use))
        {
            return string.Create(CultureInfo.InvariantCulture, $"its magic asks {SelfEquipJudge.SkillName((uint)use)} trained: {NotHers((uint)use)}");
        }

        var specInt = appraisal.Int(SelfEquipJudge.UseRequiresSkillSpecInt) is { } s and > 0 ? (uint)s : 0u;
        var specDid = appraisal.DataId(SelfEquipJudge.ItemSpecializedOnlyDid) ?? 0u;
        if ((specInt != 0 && Outside(specInt) ? specInt : specDid != 0 && Outside(specDid) ? specDid : 0u) is var spec and not 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"its magic asks {SelfEquipJudge.SkillName(spec)} specialised: {NotHers(spec)}");
        }

        if (appraisal.Int(SelfEquipJudge.HeritageGroupInt) is { } heritage and > 0 && skills.Heritage != 0 && heritage != skills.Heritage)
        {
            return string.Create(CultureInfo.InvariantCulture, $"its magic is for heritage {heritage} and hers is {skills.Heritage}");
        }

        return null;
    }
}

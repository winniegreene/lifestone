using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Tests.RetailUi.CharacterCreation;

/// <summary>
/// What ACE accepts in a character create, restated as rules from reading ACEmulator/ACE@95ef426d
/// Source/ACE.Server/Factories/PlayerFactory.cs lines 135-209 (the template, attribute and skill steps of <c>Create</c>)
/// and 606-631 (<c>ValidateAttributeCredits</c>), with <c>Player.TrainSkill</c> and <c>Player.SpecializeSkill</c>
/// (Source/ACE.Server/WorldObjects/Player_Skills.cs) for the credit checks. No ACE code is reproduced; the outcomes are
/// named after ACE's <c>CreateResult</c> values.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>
///     The template option is 0 to the heritage's template count - 1: line 135 indexes the heritage's templates with it,
///     so -1 (which retail's <c>VerifyCharacterGenerationResult</c> allows) or an index past the list throws there,
///     before any <c>CreateResult</c>. ACE skips that step for the Olthoi heritages (line 103).
///   </item>
///   <item>The start area is one of CharGen's: line 358 indexes the start areas with it.</item>
///   <item>Every attribute is 10-100, and their sum is at most the heritage's attribute credits.</item>
///   <item>Exactly 55 skill advancement classes.</item>
///   <item>
///     Skills are walked by id with the heritage's skill credits available. An inactive skill is skipped; any other must
///     be in the SkillTable. Training costs the SkillTable's trained cost and specialising then costs the difference to
///     its specialised cost, unless the heritage lists the skill, whose normal cost then trains it and whose primary cost
///     then specialises it. A step that costs more than the credits left fails. An untrained skill costs nothing.
///   </item>
/// </list>
/// ACE creates the player from a weenie whose skills it assumes start untrained here; the restatement makes the same
/// assumption. ACE runs the attribute and skill steps only for a non-Olthoi player too (line 103); the restatement applies
/// them to every heritage, which is stricter.
/// </remarks>
internal static class AceCreateRules
{
    /// <summary>The ACE outcome for <paramref name="intent"/>, or null when ACE would create the character.</summary>
    public static string? Check(CharacterCreateIntent intent, CharGenData data, SkillTableData skills)
    {
        if (!data.TryGetHeritage(intent.Heritage, out var heritage))
        {
            return "InvalidHeritage";
        }

        var olthoi = intent.Heritage is CharGenCatalog.OlthoiHeritage or CharGenCatalog.OlthoiAcidHeritage;
        if (!olthoi && (intent.TemplateOption < 0 || intent.TemplateOption >= heritage.Templates.Count))
        {
            return "TemplateOutOfRange (ACE throws: PlayerFactory.cs:135)";
        }

        if (intent.StartArea >= (uint)data.StartAreas.Count)
        {
            return "StartAreaOutOfRange (ACE throws: PlayerFactory.cs:358)";
        }

        uint[] attributes = [intent.Strength, intent.Endurance, intent.Coordination, intent.Quickness, intent.Focus, intent.Self];
        if (attributes.Any(a => a is < 10 or > 100))
        {
            return "InvalidSkillRequested";
        }

        if (attributes.Sum(a => (long)a) > heritage.AttributeCredits)
        {
            return "TooManySkillCreditsUsed";
        }

        if (intent.SkillAdvancementClasses.Count != 55)
        {
            return "ClientServerSkillsMismatch";
        }

        long available = heritage.SkillCredits;
        for (var i = 0; i < intent.SkillAdvancementClasses.Count; i++)
        {
            var sac = intent.SkillAdvancementClasses[i];
            if (sac == 0)
            {
                continue;
            }

            if (!skills.TryGetSkill((uint)i, out var skill))
            {
                return "InvalidSkillRequested";
            }

            long trainedCost = skill.TrainedCost;
            long specializedStep = skill.SpecializedCost - skill.TrainedCost;
            foreach (var own in heritage.Skills)
            {
                if (own.SkillId == (uint)i)
                {
                    trainedCost = own.NormalCost;
                    specializedStep = own.PrimaryCost;
                    break;
                }
            }

            if (sac is 2 or 3)
            {
                if (trainedCost > available)
                {
                    return "FailedToTrainSkill";
                }

                available -= trainedCost;
            }

            if (sac == 3)
            {
                if (specializedStep > available)
                {
                    return "FailedToSpecializeSkill";
                }

                available -= specializedStep;
            }
        }

        return null;
    }
}

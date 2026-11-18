using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 the Atlan wand (D132, D136; closes <c>TODO(m6-credit-need)</c> and <c>TODO(m7-atlan-alchemy-by-play)</c>): a recipe with a roll (<see cref="QuestStep.RecipeSkill"/>, <see cref="QuestStep.RecipeDifficulty"/>) gets its skill ready
/// before the first use, check, act, check, each one line:
/// <list type="bullet">
/// <item>untrained: trained from her credits at the SkillTable's trained cost (ACE's <c>Player.TrainSkill(Skill)</c>, Player_Skills.cs:155-197 @95ef426d; the ladder's level-up spend trains it first from the credit order's need, this is the
/// station's own check when it did not), or the named stop <c>&lt;kind&gt;:untrained</c> when the credits do not cover it (ACE would consume nothing: <c>YouAreNotTrainedInThatTradeSkill</c>);</item>
/// <item>then raised with her unassigned experience one rank at a time (<see cref="IActionApi.RaiseSkill"/>, the XpTable's trained or specialised list) until ACE's chance on her current skill (<see cref="RecipeChance"/>) reaches the
/// ladder's <c>plan_cast_chance</c> (the chance the climb casts at), or the next rank costs more than she has; each raise's after-check is the skill's ranks risen, a raise not seen is the stop <c>&lt;kind&gt;:raise-unconfirmed</c>.</item>
/// </list>
/// The chance the roll is made at is logged before it; the roll goes at whatever chance the experience reached (a lost roll keeps the errand callable, the autopilot's <c>RecipeRollLost</c>). Each move is <see cref="RecipeSkillPlan.Next"/>'s.
/// A session with no SkillTable or XpTable (a rig with no dats) cannot price the skill: one loud line, <c>TODO(m6-recipe-skill-tables)</c>, and the roll goes as the skill stands (the server's answer decides).
/// The experience is the errand's own: the level-up spend runs only in the Fight state (<c>LadderMidRunSpend</c>), so the hunt's experience is still unassigned at the roll.
/// </summary>
public sealed partial class QuestRun
{
    private IEnumerable<ActionHandle?> RecipeSkillReady(QuestStep step, string word)
    {
        var skillId = step.RecipeSkill!.Value;
        var difficulty = step.RecipeDifficulty!.Value;
        var name = MageTemplate.SkillName(skillId);
        if (S.SkillTable is not { } skills || !skills.TryGetSkill(skillId, out var entry) || S.XpTable is not { } table)
        {
            Warn(Stage, $"{word}: TODO(m6-recipe-skill-tables): the session has no SkillTable or XpTable to price {name} (skill {skillId}): the roll goes as the skill stands, unjudged (the server's answer decides)");
            yield break;
        }

        var wanted = LadderLoader.WarMage.PlanCastChance;
        var (raises, spent) = (0, 0L);
        for (var moves = 0; moves <= table.TrainedSkillXpList.Count + 1; moves++)
        {
            var q = S.State.Self.Qualities;
            var trained = q.IsKnown && q.TryGetSkill(skillId, out var held) && held.AdvancementClass >= SkillRules.TrainedClass;
            q.TryGetSkill(skillId, out held);
            var current = trained && SkillRules.TryCurrent(q, skillId, entry!.Formula, entry.MinLevel, out var value) ? value : 0u;
            var list = held.AdvancementClass >= SkillRules.SpecializedClass ? table.SpecializedSkillXpList : table.TrainedSkillXpList;
            var move = RecipeSkillPlan.Next(trained, q.AvailableSkillCredits, entry!.TrainedCost, current, held.Ranks, list, q.AvailableExperience, difficulty, wanted);
            switch (move.Kind)
            {
                case RecipeSkillMoveKind.Untrained:
                    Stop($"{word}:untrained", $"{name} {move.Why}; nothing is used (ACE: YouAreNotTrainedInThatTradeSkill)");
                    yield break;
                case RecipeSkillMoveKind.Train:
                    {
                        Log($"{word}: check before: {name}: {move.Why} (the roll needs it trained, D136)");
                        var train = S.Actions.TrainSkill(skillId, (int)move.Price, ActionSource.Host);
                        yield return train;
                        foreach (var h in Until(() => S.State.Self.Qualities.TryGetSkill(skillId, out var now) && now.AdvancementClass >= SkillRules.TrainedClass, AfterSeconds)) { yield return h; }

                        var done = S.State.Self.Qualities.TryGetSkill(skillId, out var after) && after.AdvancementClass >= SkillRules.TrainedClass;
                        Log(string.Create(CultureInfo.InvariantCulture, $"{word}: act: train {name}: {Describe(train)}; check after: trained={Yes(done)} credits {S.State.Self.Qualities.AvailableSkillCredits}"));
                        Require(Verify(done, $"{word}: {name} is not trained {AfterSeconds:0} s after the train"), $"{word}: train unconfirmed");
                        break;
                    }

                case RecipeSkillMoveKind.Raise:
                    {
                        var ranks = held.Ranks;
                        var raise = S.Actions.RaiseSkill(skillId, (uint)move.Price, ActionSource.Host);
                        yield return raise;
                        foreach (var h in Until(() => S.State.Self.Qualities.TryGetSkill(skillId, out var now) && now.Ranks > ranks, AfterSeconds)) { yield return h; }

                        if (!(S.State.Self.Qualities.TryGetSkill(skillId, out var risen) && risen.Ranks > ranks))
                        {
                            Stop($"{word}:raise-unconfirmed", $"{name}: {move.Why}: {Describe(raise)}; the ranks did not rise in {AfterSeconds:0} s");
                            yield break;
                        }

                        (raises, spent) = (raises + 1, spent + move.Price);
                        break;
                    }

                default:
                    Log(string.Create(CultureInfo.InvariantCulture, $"{word}: check before the roll: {name}: {move.Why} (ACE RecipeManager.GetRecipeChance); raised {raises} rank(s) for {spent:N0} experience"));
                    yield break;
            }
        }
    }
}

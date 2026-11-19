using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 the Atlan wand (the owner, 2026-11-16): what the jobs she is about to take need, read at each cycle's start of the level up job (<see cref="LadderJobNeeds"/>). The jobs are the ladder errands of her band (D118: a quest file's
/// <c>ladder</c> band holds her level, it was never recorded done, the run's quest selection runs it and it has not failed this run). Two needs, both from the quest files' facts:
/// <list type="bullet">
/// <item>the skill a roll needs (a recipe step's <see cref="QuestStep.RecipeSkill"/>: the Atlan wand's Alchemy): the credit order's need (D136, <see cref="SkillCreditPlan.OrderOf"/>), so the level-up spend trains it from her banked credits
/// (closes <c>TODO(m6-credit-need)</c>);</item>
/// <item>the bolt line a hunt needs (<see cref="JobBolt.Needs"/>: the line the hunted creatures take best, when it is not fire): the learn plan's order lines (<see cref="LogisticsCounters.OrderLines"/>, the trip's
/// <c>LearnTravelLines</c>), so a trip buys and reads it by the plan's chance rule; and the errand waits for one trip's learn when the book lacks the line's planned level (<see cref="ErrandWaitsForLearn"/>).</item>
/// </list>
/// Each change of either is one log line.
/// </summary>
public sealed partial class AutopilotScenario
{
    private IReadOnlyList<uint> _jobSkills = [];
    private IReadOnlyList<JobBoltNeed> _jobBolts = [];
    private string _jobNeedsText = string.Empty;
    private readonly HashSet<string> _jobLearnWaited = new(StringComparer.Ordinal);

    /// <summary>The quests whose ladder band holds <paramref name="level"/>, never recorded done, run by the selection and not failed this run.</summary>
    private IReadOnlyList<Quest> LadderJobs(int level) =>
        [.. QuestCatalog.All.Where(q => q.Ladder is { } band && level >= band.FromLevel && level < band.BeforeLevel && !LadderQuestDone(q.Key) && _options.Quests.Allows(q.Key) && !_questFailed.ContainsKey(q.Key))];

    /// <summary>
    /// M6 14 (the Atlan wand's ingot): true when a quest station's <c>fail=</c> is a lost roll (<c>combine:failed-roll</c> or <c>apply:failed-roll</c>, <see cref="RecipeOutcomes.FailedRoll"/>): the roll's sources are gone and ACE's fail result is in
    /// the pack, so the errand is not a failure of the run: it stays callable and the next way home starts it again from what she holds (the hunt for fresh motes).
    /// </summary>
    /// <param name="fail">The station's <c>fail=</c> token, or null.</param>
    internal static bool RecipeRollLost(string? fail) => fail is not null && fail.EndsWith(":" + RecipeOutcomes.FailedRoll, StringComparison.Ordinal);

    /// <summary>
    /// M6 14 (the lead's ruling after live ladder42 20:20:15 UTC: the Atlan errand's first walk retreated by recall and the errand was failed for the run): the walk steps of a ladder errand that may fail in one run before the errand is
    /// failed for the run (Lifestone's own bound: three tries, as the fight's misses before it walks); until then a failed walk leaves it callable at the next way home.
    /// </summary>
    public const int LadderErrandWalkTries = 3;

    private readonly Dictionary<string, int> _errandWalkFails = new(StringComparer.Ordinal);

    /// <summary>M6 14: true when a quest station's <c>fail=</c> names a walk step (a go: <c>go:...</c>, the travel, the hop, the retreat).</summary>
    /// <param name="fail">The station's <c>fail=</c> token, or null.</param>
    internal static bool ErrandWalkFailed(string? fail) => fail is not null && fail.StartsWith("go:", StringComparison.Ordinal);

    /// <summary>Counts a failed walk of <paramref name="key"/>'s errand; true while the count is within <see cref="LadderErrandWalkTries"/>.</summary>
    private bool ErrandWalkTry(string key) => (_errandWalkFails[key] = _errandWalkFails.GetValueOrDefault(key) + 1) <= LadderErrandWalkTries;

    private int _errandHuntHeld;
    private readonly List<uint> _huntScratch = [];

    /// <summary>M6 14: how many of the items <paramref name="key"/>'s hunt-until steps hunt she holds now (0 with no session or no such step).</summary>
    private int HuntHeld(string key) =>
        _session is { } session && (TrophyQuests ?? QuestCatalog.All).FirstOrDefault(q => q.Key == key) is { } quest
            ? quest.Steps.Where(s => s.Kind == QuestStepKind.HuntUntil && s.Item?.Wcid is not null).Sum(s => QuestCombineChain.HuntHeld(quest, s, w => GearInventory.CountOf(session.State, _huntScratch, w))) // M6 14 combine3: the equivalents along the no-roll chain
            : 0;

    /// <summary>M6 audit (fix 10): true when a quest station's <c>fail=</c> is a roll held under <see cref="RecipeChance.RollFloor"/> (<c>&lt;kind&gt;:roll-held</c>): nothing was used, the errand stays callable.</summary>
    /// <param name="fail">The station's <c>fail=</c> token, or null.</param>
    internal static bool RecipeRollHeld(string? fail) => fail is not null && fail.EndsWith(":roll-held", StringComparison.Ordinal);

    /// <summary>M6 audit (fix 10): the first roll step of the ladder errands of her band (its skill and difficulty, the quest's key), or null.</summary>
    private (uint Skill, uint Difficulty, string Quest)? JobRoll()
    {
        if (_session is not { } session || !_options.LadderQuests)
        {
            return null;
        }

        foreach (var quest in LadderJobs(session.State.Self.Qualities.Level))
        {
            if (quest.Steps.FirstOrDefault(s => s.RecipeSkill is not null && s.RecipeDifficulty is not null) is { } step)
            {
                return (step.RecipeSkill!.Value, step.RecipeDifficulty!.Value, quest.Key);
            }
        }

        return null;
    }

    /// <summary>M6 audit (fix 10): her current value of <paramref name="skill"/> (the SkillTable's formula, her buffs in), or null when untrained or unread.</summary>
    private uint? CurrentSkill(IGameSessionView session, uint skill)
    {
        var q = session.State.Self.Qualities;
        return session.SkillTable is { } table && table.TryGetSkill(skill, out var entry) && q.TryGetSkill(skill, out var held) && held.AdvancementClass >= SkillRules.TrainedClass && SkillRules.TryCurrent(q, skill, entry!.Formula, entry.MinLevel, out var value)
            ? value
            : null;
    }

    /// <summary>M6 audit (fix 10): the level-up spend's read of the roll a ladder errand of her band makes (<see cref="RecipeSpend"/>), or null.</summary>
    private RecipeSpendFacts? LadderRecipeFacts() =>
        JobRoll() is { } roll && _session is { } session && CurrentSkill(session, roll.Skill) is { } current ? new RecipeSpendFacts(roll.Skill, current, roll.Difficulty, roll.Quest) : null;

    private readonly HashSet<string> _rollWaitNoted = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 audit (fix 10, the owner's decision of 2026-11-19: only start the errand once she could reasonably get the skill to at least an 80% chance): a ladder errand with a roll waits while her current skill's chance is under
    /// <see cref="RecipeChance.RollFloor"/> (the level-up spend buys toward it, <see cref="RecipeSpend"/>, in the caster rule's reach); said once a level.
    /// </summary>
    private bool ErrandWaitsForRoll(string key)
    {
        if (_session is not { } session || QuestCatalog.All.FirstOrDefault(q => q.Key == key) is not { } quest || quest.Steps.FirstOrDefault(s => s.RecipeSkill is not null && s.RecipeDifficulty is not null) is not { } step)
        {
            return false;
        }

        var skill = step.RecipeSkill!.Value;
        var difficulty = step.RecipeDifficulty!.Value;
        var current = CurrentSkill(session, skill);
        var chance = current is { } v ? RecipeChance.Of(v, difficulty) : 0;
        if (chance >= RecipeChance.RollFloor)
        {
            return false;
        }

        var level = session.State.Self.Qualities.Level;
        if (_rollWaitNoted.Add(key + ":" + level.ToString(CultureInfo.InvariantCulture)))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={key}: the errand waits for its roll (m6-audit-roll-floor): {MageTemplate.SkillName(skill)} {(current is { } c ? c.ToString(CultureInfo.InvariantCulture) : "untrained")} against {difficulty} is a chance of {chance:0.000}, under {RecipeChance.RollFloor:0.00} (skill {RecipeChance.SkillFor(difficulty, RecipeChance.RollFloor)}); the level-up spend buys toward it"));
        }

        return true;
    }

    /// <summary>True when a job she will take needs <paramref name="skill"/> trained (the credit order's need, D136).</summary>
    private bool JobNeedsSkill(uint skill) => _jobSkills.Contains(skill);

    /// <summary>The cycle's read of the jobs' needs (see the class remarks); the order lines are the den run's (when ordered) and then the jobs' bolt lines.</summary>
    private void LadderJobNeeds(int level)
    {
        if (_options.Job != AutopilotJob.LevelUp || level <= 0 || !_options.LadderQuests)
        {
            return;
        }

        var jobs = LadderJobs(level);
        _jobSkills = JobBolt.Skills(jobs);
        _jobBolts = JobBolt.Needs(jobs);
        var den = _options.Task.Kind == AutopilotTaskKind.DenRun ? ScrivenerLearnPlan.DenRunLines : [];
        IReadOnlyList<string> lines = [.. den, .. _jobBolts.Select(b => b.Line)];
        _counters.OrderLines = lines;
        _context.Facts.LearnTravelLines = lines;
        var text = string.Join("; ", _jobBolts.Select(b => $"{b.Quest} needs {b.Line} ({b.Why})").Concat(_jobSkills.Select(s => $"a roll needs {MageTemplate.SkillName(s)} trained")));
        if (text != _jobNeedsText)
        {
            _jobNeedsText = text;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: job needs at level {level}: {(text.Length == 0 ? "none" : text)} (jobs: {(jobs.Count == 0 ? "none" : string.Join(", ", jobs.Select(j => j.Key)))}; M6 14: the credit order's need, the learn plan's order lines)"));
        }
    }

    /// <summary>
    /// The ladder errand <paramref name="key"/> waits for one trip's learn (M6 14 the element line): the book lacks the planned level of a bolt line the quest's hunt needs (<see cref="JobBolt.Needs"/>, the learn plan's answer Due or off
    /// home). At a way home the fight stopped for a trip the trip goes first and its end calls the errand again (<see cref="TripEnded"/>); elsewhere the errand is asked at the next way home. Once a key a run: after one trip the errand runs
    /// with what she has, named (the fight casts the best line her book holds, <c>FightScenario.Element.cs</c>; a scroll the trip could not buy is never a stall).
    /// </summary>
    private bool ErrandWaitsForLearn(string key, bool atTrip)
    {
        if (_session is not { } session || (_ladder ?? LadderLoader.WarMage) is not { } ladder || QuestCatalog.All.FirstOrDefault(q => q.Key == key) is not { } quest || JobBolt.Needs([quest]) is not { Count: > 0 } bolts)
        {
            return false;
        }

        var state = session.State;
        if (!state.Self.Spellbook.IsKnown || !state.Self.Qualities.IsKnown || ScrivenerLearnPlan.RungOf(ladder, _ground.Key, state.Self.Qualities.Level) is not { } rung)
        {
            return false;
        }

        var lacking = ScrivenerLearnPlan.Needs(_ground, rung, ladder.PlanCastChance, state.Self.Qualities, state.Self.Spellbook, ScrivenerCatalog.Default, ladder, null, [.. bolts.Select(b => b.Line)])
            .Where(n => bolts.Any(b => b.Line == n.Line.Name) && n.Kind is LearnNeedKind.Due or LearnNeedKind.OffHome)
            .ToList();
        if (lacking.Count == 0)
        {
            return false;
        }

        var what = string.Join(", ", lacking.Select(n => $"{n.Spell?.Name ?? n.Line.Name} ({(n.Kind == LearnNeedKind.Due ? "sold at home" : "sold at " + string.Join(", ", n.Towns))})"));
        if (_jobLearnWaited.Contains(key))
        {
            _log.Warn(LogCategory, $"autopilot quest: key={key}: TODO(m6-job-bolt-unbought): the book still lacks {what} after a trip: the errand runs with the lines she holds (the fight casts the hardest of them at each target)");
            return false;
        }

        if (atTrip)
        {
            _jobLearnWaited.Add(key); // one trip a key a run
            _errandAfterTrip = true;
        }

        _log.Info(LogCategory, $"autopilot quest: key={key}: check before: the job's bolt line is not in the book: {what}: {(atTrip ? "the trip first (its learn buys and reads it), the errand from where the trip ends" : "the errand waits for the next trip's learn")} (M6 14 the element line; once a run)");
        return true;
    }
}

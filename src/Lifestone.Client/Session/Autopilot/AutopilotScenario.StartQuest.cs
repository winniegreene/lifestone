using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 the start quest (the owner's ruling, 2026-11-16: start the quest on request (a way to trigger the autopilot to start the quest at once); a forced trip after one kill was refused as a fake step): the level up job runs one quest's errand from home at the run's
/// start (<see cref="AutopilotOptions.StartQuest"/>, <c>--autopilot-start-quest</c>; the chat's <c>/auto level-up quest &lt;key&gt;</c> sets it through the task), with no trip from kills. Where: the first time she leaves home for the ground
/// after the run's start (its first audit, trip and buff pass have run: <see cref="BeginTravelOut"/>), or before her first fight when the run starts on the ground. Check before, each a refusal by name (<c>autopilot quest: start-quest:
/// refused: ...</c>): the key in the catalog, the level gate (a ladder band, a level range), a once-quest recorded done, its timer, the run's quest selection. Act: the errand's own start (<see cref="StartErrand"/>, from home, a ladder errand
/// when the file has a band, with its own checks: a stop due, the buffs, the job's bolt line); a wait there is named and the ordinary ways home ask the errand again. After: the errand's end goes back to the known state, so the ladder
/// resumes, and the errand's retry rules stand (a lost roll, a hunt that gained, a walk within its tries). Asked once a run.
/// </summary>
public sealed partial class AutopilotScenario
{
    private bool _startQuestAsked;

    /// <summary>The quest the run starts with: <see cref="AutopilotOptions.StartQuest"/>, else the level up task's quest (the chat's <c>/auto level-up quest &lt;key&gt;</c>), else null.</summary>
    internal string? StartQuestKey => _options.StartQuest ?? (_options.Task is { Kind: AutopilotTaskKind.Job, QuestKey: { } key } ? key : null);

    /// <summary>
    /// The start quest's errand now, or null (none named, asked already, refused by name, or the errand's own check holds it: the caller's way goes on). <paramref name="why"/> is the caller's moment.
    /// </summary>
    private ScenarioStatus? StartQuestNow(string why)
    {
        if (SubFightQuestAgain(why) is { } again)
        {
            return again; // M6 16 E3.1: a quest whose fight was left by a staying-alive ask is called again here, from home (AutopilotScenario.SubFightWatch.cs)
        }

        if (_startQuestAsked || _options.Job != AutopilotJob.LevelUp || StartQuestKey is not { } key || _session is not { } session || _questTripActive)
        {
            return null;
        }

        _startQuestAsked = true;
        var level = session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var read) ? read : 0;
        if (StartQuestRefusal(key, level, session.Clock.Time.GetUtcNow()) is { } refused)
        {
            _log.Warn(LogCategory, $"autopilot quest: start-quest: refused: key={key} level={level}: {refused}; the ladder goes on");
            return null;
        }

        var quest = (TrophyQuests ?? QuestCatalog.All).First(q => q.Key == key);
        var reason = string.Create(CultureInfo.InvariantCulture, $"{why}: the start quest {key} (--autopilot-start-quest, the owner 2026-11-16): its errand from home now, at level {level}, no trip from kills; the ladder after it");
        _log.Info(LogCategory, $"autopilot quest: start-quest: key={key} level={level}: {reason}");
        var started = StartErrand(QuestThen.Home, forced: false, key, JourneyPlans.FromHome, ladder: quest.Ladder is not null, reason);
        if (started is null)
        {
            _log.Info(LogCategory, $"autopilot quest: start-quest: key={key}: the errand's own check holds it (the line above names it); the ordinary ways home ask it again");
        }

        return started;
    }

    /// <summary>Why the start quest <paramref name="key"/> is refused at <paramref name="level"/>, or null.</summary>
    private string? StartQuestRefusal(string key, int level, DateTimeOffset now)
    {
        if ((TrophyQuests ?? QuestCatalog.All).FirstOrDefault(q => q.Key == key) is not { } quest)
        {
            return "no quest of the catalog has the key";
        }

        if (quest.Ladder is { } band && (level < band.FromLevel || level >= band.BeforeLevel))
        {
            return string.Create(CultureInfo.InvariantCulture, $"level-out-of-band: its ladder band is {band.FromLevel} to before {band.BeforeLevel}");
        }

        if (quest.Level is { } range && !range.Fits(level))
        {
            return $"level-out-of-range: its level gate is {range}";
        }

        if (quest.Ladder is { Once: true } && LadderQuestDone(key))
        {
            return "done-once: it is recorded done and the file runs it once";
        }

        if (!_context.QuestTimersInUse.IsDue(quest, now))
        {
            return "not-due: " + _context.QuestTimersInUse.Line(quest, now);
        }

        if (!_options.Quests.Allows(key))
        {
            return $"not-selected: --autopilot-ladder-quests={_options.Quests.Text} leaves it out";
        }

        return _questFailed.TryGetValue(key, out var failed) ? "failed-this-run: " + failed : null;
    }
}

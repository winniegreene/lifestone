using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>How a <see cref="SkillTrainStep"/> ended.</summary>
public enum SkillTrainEnd
{
    /// <summary>Not ended (or not begun).</summary>
    None,

    /// <summary>The after-check holds: the skill reads trained and the credits fell by the cost.</summary>
    Trained,

    /// <summary>The before-check failed (the description unread, the skill missing or already trained, the credits short): nothing was sent.</summary>
    NotDue,

    /// <summary>The Action API or ACE refused the train, or the action timed out (ACE drops a wrong cost without a word): named, never retried.</summary>
    Refused,

    /// <summary>The action completed but the after-check did not hold within <see cref="SkillTrainStep.ConfirmSeconds"/>: named, never retried.</summary>
    Unconfirmed,

    /// <summary>Dry run: the train was logged and not sent.</summary>
    DryRun,
}

/// <summary>
/// M6 item 1 b: one skill trained from the credits, check, act, check (M5 D141). Before: the character's description is read, the skill is in it and untrained, and the server's <see cref="PlayerQualities.AvailableSkillCredits"/>
/// (PropertyInt 24) covers the cost. The act: <see cref="IActionApi.TrainSkill"/> (GameAction 0x0047) for exactly the cost, <see cref="ActionSource.Automation"/> (the kill switch cancels it, dry run logs it). After: the skill's
/// advancement class reads trained or better and the credits read the count before less the cost, within <see cref="ConfirmSeconds"/> of the action's completion. A refusal, a timeout or an after-check that does not hold
/// ends the step with <see cref="Failure"/> naming it; the step is never retried (the caller stops by name).
/// </summary>
/// <remarks>ACE's rule (@95ef426d <c>WorldObjects/Player_Skills.cs</c> <c>HandleActionTrainSkill</c>): the credits sent must equal the SkillTable's trained cost and the character must hold them, else the request is dropped
/// without a word (the action times out); an already-trained skill is answered "Failed to train X!"; a trained skill gets the private skill update, the credits' update and "X trained.".</remarks>
public sealed class SkillTrainStep
{
    /// <summary>The seconds, on the session clock, the after-check waits once the action has completed (Lifestone's own bound: ACE sends both updates with the train).</summary>
    public const double ConfirmSeconds = 5;

    private readonly ILogSink? _log;
    private IGameSessionView? _view;
    private ActionHandle? _pending;
    private double _completedAt = double.NaN;
    private int _creditsBefore;

    /// <summary>A step that logs to <paramref name="log"/>.</summary>
    /// <param name="log">The log, or null.</param>
    public SkillTrainStep(ILogSink? log = null) => _log = log;

    /// <summary>The skill trained.</summary>
    public uint Skill { get; private set; }

    /// <summary>The credits it costs.</summary>
    public int Cost { get; private set; }

    /// <summary>True once the step has ended (<see cref="End"/>).</summary>
    public bool Done { get; private set; }

    /// <summary>How it ended.</summary>
    public SkillTrainEnd End { get; private set; }

    /// <summary>The named reason a step did not train, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>Begins the step: the before-check, then the act.</summary>
    /// <param name="view">The session view.</param>
    /// <param name="skill">The skill to train.</param>
    /// <param name="cost">Its trained cost (the SkillTable's).</param>
    public void Begin(IGameSessionView view, uint skill, int cost)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        Skill = skill;
        Cost = cost;
        Done = false;
        End = SkillTrainEnd.None;
        Failure = null;
        _pending = null;
        _completedAt = double.NaN;
        var q = view.State.Self.Qualities;
        var name = MageTemplate.SkillName(skill);
        if (!q.IsKnown)
        {
            Finish(SkillTrainEnd.NotDue, $"train {name}: check before: the character's description has not arrived");
            return;
        }

        if (!q.TryGetSkill(skill, out var desc))
        {
            Finish(SkillTrainEnd.NotDue, $"train {name}: check before: the skill is not in the character's description");
            return;
        }

        if (desc.AdvancementClass >= SkillRules.TrainedClass)
        {
            Finish(SkillTrainEnd.NotDue, string.Create(CultureInfo.InvariantCulture, $"train {name}: check before: the skill is already trained (class {desc.AdvancementClass})"));
            return;
        }

        _creditsBefore = q.AvailableSkillCredits;
        if (_creditsBefore < cost)
        {
            Finish(SkillTrainEnd.NotDue, string.Create(CultureInfo.InvariantCulture, $"train {name}: check before: {_creditsBefore} credits do not cover its cost of {cost}"));
            return;
        }

        _log?.Info(XpSpendRule.LogCategory, string.Create(CultureInfo.InvariantCulture, $"credits: train {name}: check before: untrained (class {desc.AdvancementClass}), {_creditsBefore} credits cover its cost of {cost}; act: TrainSkill {skill} for {cost}"));
        _pending = view.Actions.TrainSkill(skill, cost, ActionSource.Automation);
    }

    /// <summary>Advances the step; true once it is <see cref="Done"/>.</summary>
    public bool Tick()
    {
        if (Done || _view is null || _pending is not { } handle)
        {
            return Done;
        }

        var name = MageTemplate.SkillName(Skill);
        if (!handle.IsCompleted)
        {
            return false;
        }

        var outcome = handle.Outcome!.Value;
        if (outcome.Kind == ActionOutcomeKind.DryRun)
        {
            return Finish(SkillTrainEnd.DryRun, null);
        }

        if (!outcome.Succeeded)
        {
            return Finish(SkillTrainEnd.Refused, string.Create(CultureInfo.InvariantCulture, $"train {name} for {Cost} credits ended {outcome.Kind}{(outcome.Code != 0 ? $" (0x{outcome.Code:X4})" : string.Empty)}: {outcome.Detail}"));
        }

        var now = _view.Clock.Now;
        if (double.IsNaN(_completedAt))
        {
            _completedAt = now;
        }

        var q = _view.State.Self.Qualities;
        var cls = q.TryGetSkill(Skill, out var desc) ? desc.AdvancementClass : 0u;
        var credits = q.AvailableSkillCredits;
        if (cls >= SkillRules.TrainedClass && credits == _creditsBefore - Cost)
        {
            return Finish(SkillTrainEnd.Trained, null);
        }

        return now - _completedAt > ConfirmSeconds
            ? Finish(SkillTrainEnd.Unconfirmed, string.Create(CultureInfo.InvariantCulture, $"train {name}: check after: the action was confirmed but within {ConfirmSeconds:0} s the skill reads class {cls} and the credits {credits} (wanted trained and {_creditsBefore - Cost})"))
            : false;
    }

    private bool Finish(SkillTrainEnd end, string? failure)
    {
        End = end;
        Done = true;
        Failure = failure;
        var name = MageTemplate.SkillName(Skill);
        var text = end == SkillTrainEnd.Trained
            ? string.Create(CultureInfo.InvariantCulture, $"credits: train {name}: check after: trained, credits {_creditsBefore} -> {_creditsBefore - Cost}")
            : $"credits: train {name}: ended {end}{(failure is null ? string.Empty : ": " + failure)}";
        if (end is SkillTrainEnd.Trained or SkillTrainEnd.DryRun)
        {
            _log?.Info(XpSpendRule.LogCategory, text);
        }
        else
        {
            _log?.Warn(XpSpendRule.LogCategory, text);
        }

        return true;
    }
}

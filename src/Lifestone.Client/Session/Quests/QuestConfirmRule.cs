using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Quests;

/// <summary>What <see cref="QuestConfirmRule"/> decides for one open server question.</summary>
public enum ConfirmDecisionKind
{
    /// <summary>Answer Yes (only a confirm step's own question whose answer is yes).</summary>
    Yes = 0,

    /// <summary>Answer No.</summary>
    No = 1,

    /// <summary>Do not answer: the question is left open (a swear allegiance or a fellowship invite, or a type ACE does not keep), logged.</summary>
    Leave = 2,
}

/// <summary>One decision of <see cref="QuestConfirmRule.Decide"/>: the question, the decision and why.</summary>
/// <param name="Question">The open question (its type and context go back in the answer, <see cref="IActionApi.RespondToConfirmation"/>).</param>
/// <param name="Decision">Yes, No or leave it.</param>
/// <param name="Why">Why, in a few words.</param>
public sealed record ConfirmDecision(PendingConfirmation Question, ConfirmDecisionKind Decision, string Why)
{
    /// <summary>True when the decision is an answer (Yes or No) to send.</summary>
    public bool Answers => Decision != ConfirmDecisionKind.Leave;

    /// <summary>The answer to send (true for Yes); only meaningful when <see cref="Answers"/>.</summary>
    public bool Accepted => Decision == ConfirmDecisionKind.Yes;

    /// <summary>The decision as one log line: <c>confirm: type=7 text="..." decision=no why=...</c>.</summary>
    public string Line => string.Create(
        CultureInfo.InvariantCulture,
        $"confirm: type={Question.Type} text=\"{Question.Text}\" decision={Decision.ToString().ToLowerInvariant()} why={Why}");
}

/// <summary>
/// M6 item 5 (Q1): the headless Yes/No rule, the rule half of <c>TODO(m5-6-headless-confirm-reply)</c> (the headless host answered no server question; the
/// wiring is the quest station's confirm step, Q3, and the autopilot's tick, Q5). Pure: given the open questions (<see cref="GameState.PendingConfirmations"/>,
/// GameEvent 0x0274) and the active confirm step (or none), it decides for each question, oldest first:
/// <list type="bullet">
/// <item>Types 1 (swear allegiance) and 4 (fellowship) are never answered by the automation, even when a step names their text: they are left open, logged
/// (ACE removes them itself at its timeout: @95ef426d <c>WorldObjects/Managers/ConfirmationManager.cs:57-84</c>).</item>
/// <item>The active confirm step's question (its <see cref="QuestExpect.Reply"/> a case-sensitive substring of the question's text) gets the step's
/// <see cref="QuestStep.Answer"/>.</item>
/// <item>Any other question of a type ACE keeps open until the client answers (<see cref="GameState.ServerKeepsSlotAfterDone"/>: 2, 3, 5, 6, 7) is unasked:
/// it gets No, so its slot does not block the next question of its type ("confirmation in progress").</item>
/// <item>Any other type (0, or a value ACE does not name) is left open, logged.</item>
/// </list>
/// Each decision is one line (<see cref="ConfirmDecision.Line"/>); the caller logs it and sends the answer (check, act, check: the question gone after).
/// </summary>
public static class QuestConfirmRule
{
    /// <summary>The decision for each open question, oldest first.</summary>
    /// <param name="open">The open questions (<see cref="GameState.PendingConfirmations"/>).</param>
    /// <param name="step">The active confirm step, or null when no confirm step runs.</param>
    /// <exception cref="ArgumentException"><paramref name="step"/> is not a confirm step.</exception>
    public static IReadOnlyList<ConfirmDecision> Decide(IReadOnlyList<PendingConfirmation> open, QuestStep? step)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (step is not null && step.Kind != QuestStepKind.Confirm)
        {
            throw new ArgumentException($"QuestConfirmRule takes a confirm step or none, not {step}", nameof(step));
        }

        var decisions = new List<ConfirmDecision>(open.Count);
        foreach (var question in open)
        {
            decisions.Add(DecideOne(question, step));
        }

        return decisions;
    }

    /// <summary>The decision for one open question.</summary>
    /// <param name="question">The question.</param>
    /// <param name="step">The active confirm step, or null.</param>
    public static ConfirmDecision DecideOne(PendingConfirmation question, QuestStep? step)
    {
        ArgumentNullException.ThrowIfNull(question);
        var asked = step?.Expect.Reply is { } text && question.Text.Contains(text, StringComparison.Ordinal);
        if (question.Type is 1 or 4)
        {
            var what = question.Type == 1 ? "swear allegiance" : "fellowship";
            return new(question, ConfirmDecisionKind.Leave, $"type {question.Type} ({what}) is never answered by the automation{(asked ? ", even for the step's question" : string.Empty)}; left open");
        }

        if (asked)
        {
            var answer = step!.Answer ?? throw new InvalidOperationException($"the confirm step has no answer (the loader requires one): {step}");
            return new(question, answer ? ConfirmDecisionKind.Yes : ConfirmDecisionKind.No, string.Create(CultureInfo.InvariantCulture, $"the step {step.Index}'s question (\"{step.Expect.Reply}\")"));
        }

        if (GameState.ServerKeepsSlotAfterDone(question.Type))
        {
            return new(question, ConfirmDecisionKind.No, step is null ? "unasked: no confirm step runs, and ACE keeps the slot until an answer" : string.Create(CultureInfo.InvariantCulture, $"unasked: not the step {step.Index}'s question (\"{step.Expect.Reply}\"), and ACE keeps the slot until an answer"));
        }

        return new(question, ConfirmDecisionKind.Leave, $"type {question.Type} is not one ACE keeps open (2, 3, 5, 6, 7); left open");
    }
}

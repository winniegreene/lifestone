using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 item 5 (Q1): <see cref="QuestConfirmRule"/>, the rule half of <c>TODO(m5-6-headless-confirm-reply)</c>: a confirm step's own question gets the step's
/// answer; an unasked question of a type ACE keeps open (2, 3, 5, 6, 7) gets No; types 1 (swear allegiance) and 4 (fellowship) are never answered; any other
/// type is left open; each decision is one <c>confirm:</c> line.
/// </summary>
public sealed class QuestConfirmRuleTests
{
    private static PendingConfirmation Question(uint type, string text = "Are you sure you want to do that?", uint context = 0x1234) => new(type, context, text, 12.5);

    private static QuestStep ConfirmStep(string reply, bool answer) => new()
    {
        Index = 3,
        Kind = QuestStepKind.Confirm,
        What = "answer the question",
        Place = null,
        Target = null,
        Item = null,
        Expect = new QuestExpect(reply, null, null),
        Source = "QuestConfirmRuleTests",
        Todo = null,
        Answer = answer,
    };

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(7u)]
    public void With_no_step_an_unasked_question_ACE_keeps_open_gets_no(uint type)
    {
        var d = QuestConfirmRule.DecideOne(Question(type), null);
        Assert.Equal(ConfirmDecisionKind.No, d.Decision);
        Assert.True(d.Answers);
        Assert.False(d.Accepted);
        Assert.Contains("unasked", d.Why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1u, "swear allegiance")]
    [InlineData(4u, "fellowship")]
    public void Swear_allegiance_and_fellowship_are_never_answered(uint type, string what)
    {
        var d = QuestConfirmRule.DecideOne(Question(type, "Do you wish to join the fellowship?"), null);
        Assert.Equal(ConfirmDecisionKind.Leave, d.Decision);
        Assert.False(d.Answers);
        Assert.Contains(what, d.Why, StringComparison.Ordinal);

        var even = QuestConfirmRule.DecideOne(Question(type, "Do you wish to join the fellowship?"), ConfirmStep("join the fellowship", answer: true));
        Assert.Equal(ConfirmDecisionKind.Leave, even.Decision); // not even for a step that names its text
        Assert.Contains("even for the step's question", even.Why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(8u)]
    [InlineData(0xFFu)]
    public void A_type_ACE_does_not_keep_open_is_left(uint type) =>
        Assert.Equal(ConfirmDecisionKind.Leave, QuestConfirmRule.DecideOne(Question(type), null).Decision);

    [Fact]
    public void The_steps_own_question_gets_the_steps_answer()
    {
        var yes = QuestConfirmRule.DecideOne(Question(7, "Do you wish to hand over the scroll?"), ConfirmStep("hand over the scroll", answer: true));
        Assert.Equal((ConfirmDecisionKind.Yes, true, true), (yes.Decision, yes.Answers, yes.Accepted));
        Assert.Equal("confirm: type=7 text=\"Do you wish to hand over the scroll?\" decision=yes why=the step 3's question (\"hand over the scroll\")", yes.Line);

        var no = QuestConfirmRule.DecideOne(Question(7, "Do you wish to hand over the scroll?"), ConfirmStep("hand over the scroll", answer: false));
        Assert.Equal(ConfirmDecisionKind.No, no.Decision);
        Assert.Contains("the step 3's question", no.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_question_while_a_step_runs_is_unasked_and_the_match_is_case_sensitive()
    {
        var step = ConfirmStep("hand over the scroll", answer: true);
        var other = QuestConfirmRule.DecideOne(Question(7, "Do you want to drink the potion?"), step);
        Assert.Equal(ConfirmDecisionKind.No, other.Decision);
        Assert.Contains("not the step 3's question", other.Why, StringComparison.Ordinal);
        Assert.Equal(ConfirmDecisionKind.No, QuestConfirmRule.DecideOne(Question(7, "Do you wish to HAND OVER THE SCROLL?"), step).Decision);
    }

    [Fact]
    public void Each_open_question_gets_one_decision_in_order_and_one_line()
    {
        var open = new[] { Question(2, "Raise your skill?"), Question(4, "Join Ann's fellowship?"), Question(7, "Do you wish to hand over the scroll?") };
        var decisions = QuestConfirmRule.Decide(open, ConfirmStep("hand over the scroll", answer: true));
        Assert.Equal([ConfirmDecisionKind.No, ConfirmDecisionKind.Leave, ConfirmDecisionKind.Yes], decisions.Select(d => d.Decision));
        Assert.Equal(open, decisions.Select(d => d.Question));
        Assert.All(decisions, d => Assert.StartsWith($"confirm: type={d.Question.Type} text=\"{d.Question.Text}\" decision=", d.Line, StringComparison.Ordinal));
        Assert.Equal(0x1234u, decisions[2].Question.Context); // the context goes back in the answer unchanged
        Assert.Empty(QuestConfirmRule.Decide([], null));
    }

    [Fact]
    public void A_step_that_is_not_a_confirm_is_refused()
    {
        var talk = ConfirmStep("x", answer: true) with { Kind = QuestStepKind.Talk };
        Assert.Throws<ArgumentException>(() => QuestConfirmRule.Decide([Question(7)], talk));
    }
}

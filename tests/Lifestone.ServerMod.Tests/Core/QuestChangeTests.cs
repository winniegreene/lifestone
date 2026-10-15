using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The decisions of one <c>quest</c> request that need nothing from ACE (server/mod/Lifestone.ServerMod/Core/QuestChange.cs,
/// compiled here by link): when it is refused, what the reply's data holds for the registry row before and after, and the
/// loud failure when ACE's registry did not end as the action promises. The ACE calls themselves are checked by
/// ModSourceTests (their shape) and by the live loopback tests (slice C).
/// </summary>
public sealed class QuestChangeTests
{
    private const string Name = "SentryTaskComplete";

    private static QuestRequest Request(QuestAction action, string? id = null) => new(action, Name, id);

    [Theory]
    [InlineData(QuestAction.Stamp, true)]
    [InlineData(QuestAction.Erase, true)]
    [InlineData(QuestAction.Has, false)]
    public void A_player_who_is_logging_out_may_only_read_a_quest(QuestAction action, bool refused)
    {
        var refusal = QuestChange.Refusal(Request(action), isLoggingOut: true);
        if (refused)
        {
            Assert.Equal(new ToolError(ToolErrors.Busy, "you are logging out"), refusal);
            Assert.Equal(QuestChange.LoggingOutMessage, refusal!.Message);
        }
        else
        {
            Assert.Null(refusal);
        }

        Assert.Null(QuestChange.Refusal(Request(action), isLoggingOut: false));
    }

    [Fact]
    public void A_stamp_of_a_new_quest_reports_no_row_before_and_one_completion_after()
    {
        var request = Request(QuestAction.Stamp, "q1");
        var data = QuestChange.Build(request, before: null, after: 1);
        Assert.Equal(new QuestData("stamp", Name, false, 0, true, 1), data);
        Assert.True(data.Changed());
        Assert.Null(QuestChange.Check(request, data));
    }

    [Fact]
    public void A_stamp_of_a_held_quest_adds_a_completion_or_leaves_it_at_its_maximum()
    {
        var request = Request(QuestAction.Stamp);
        var added = QuestChange.Build(request, before: 1, after: 2);
        Assert.Equal(new QuestData("stamp", Name, true, 1, true, 2), added);
        Assert.Null(QuestChange.Check(request, added));

        // ACE leaves a quest at its maximum solves alone (QuestManager.IsMaxSolves): not an error, and the reply says so.
        var atMax = QuestChange.Build(request, before: 1, after: 1);
        Assert.False(atMax.Changed());
        Assert.Null(QuestChange.Check(request, atMax));
        Assert.Contains("already at its maximum", atMax.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_stamp_that_leaves_no_row_or_lowers_the_count_is_a_loud_failure()
    {
        var request = Request(QuestAction.Stamp);
        var none = QuestChange.Check(request, QuestChange.Build(request, before: null, after: null));
        Assert.Equal(ToolErrors.Failed, none!.Code);
        Assert.Contains("did not record the stamp", none.Message, StringComparison.Ordinal);
        Assert.Contains(Name, none.Message, StringComparison.Ordinal);

        var lowered = QuestChange.Check(request, QuestChange.Build(request, before: 3, after: 2));
        Assert.Equal(ToolErrors.Failed, lowered!.Code);
        Assert.Contains("lowered", lowered.Message, StringComparison.Ordinal);

        // A row that exists with no completions is still a row: a stamp that leaves it at 0 is a recorded stamp, so not this failure.
        Assert.Null(QuestChange.Check(request, QuestChange.Build(request, before: 0, after: 0)));
    }

    [Fact]
    public void An_erase_removes_the_row_and_an_erase_of_a_missing_one_is_an_answer()
    {
        var request = Request(QuestAction.Erase);
        var erased = QuestChange.Build(request, before: 1, after: null);
        Assert.Equal(new QuestData("erase", Name, true, 1, false, 0), erased);
        Assert.True(erased.Changed());
        Assert.Null(QuestChange.Check(request, erased));

        var absent = QuestChange.Build(request, before: null, after: null);
        Assert.Equal(new QuestData("erase", Name, false, 0, false, 0), absent);
        Assert.False(absent.Changed());
        Assert.Null(QuestChange.Check(request, absent));

        var stillThere = QuestChange.Check(request, QuestChange.Build(request, before: 1, after: 1));
        Assert.Equal(ToolErrors.Failed, stillThere!.Code);
        Assert.Contains("did not erase", stillThere.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_has_reports_the_row_and_must_change_nothing()
    {
        var request = Request(QuestAction.Has);
        var held = QuestChange.Build(request, before: 2, after: 2);
        Assert.Equal(new QuestData("has", Name, true, 2, true, 2), held);
        Assert.Null(QuestChange.Check(request, held));

        var missing = QuestChange.Build(request, before: null, after: null);
        Assert.Equal(new QuestData("has", Name, false, 0, false, 0), missing);
        Assert.Null(QuestChange.Check(request, missing));

        foreach (var (before, after) in new (int?, int?)[] { (null, 1), (1, null), (1, 2) })
        {
            var changed = QuestChange.Check(request, QuestChange.Build(request, before, after));
            Assert.Equal(ToolErrors.Failed, changed!.Code);
            Assert.Contains("only being read", changed.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_action_builds_data_with_its_wire_word_and_the_requested_name()
    {
        foreach (var action in Enum.GetValues<QuestAction>())
        {
            var data = QuestChange.Build(Request(action), before: null, after: null);
            Assert.Equal(ToolRequestParser.QuestActionWord(action), data.Action);
            Assert.Equal(Name, data.Name);
        }
    }

    [Fact]
    public void The_failure_message_is_printable_ascii_and_fits_a_reply()
    {
        var request = new QuestRequest(QuestAction.Stamp, new string('q', ToolRequestParser.MaxQuestNameLength), "x");
        var error = QuestChange.Check(request, QuestChange.Build(request, before: null, after: null))!;
        var text = ToolsWire.Reply(ToolReply.Failure("x", ToolVerbs.Quest, error), error.Message);
        ToolsWireTests.AssertPrintableAscii(text.Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.True(ToolsWire.TryParseReply(text, out var reply, out _));
        Assert.Equal(error, reply.AsError());
    }
}

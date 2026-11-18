using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 item 8: <see cref="RecipeOutcomes.Read"/>, each outcome from what the pack did and what the server said, with the recipe rows that decide its shape: 790 (two motes, no roll), 789 (two lines that differ only in "of high quality", and a different fail result), 6027 (one result on both branches, two lines) and
/// 6068 (one result, one text, the source kept). Every refusal text and code is ACE's (@95ef426d <c>Managers/RecipeManager.cs</c>).
/// </summary>
public sealed class RecipeOutcomeTests
{
    private const uint MoteA = 0x80000A01;
    private const uint MoteB = 0x80000A02;
    private const uint Result = 0x80000A03;
    private const uint Mote = 6353;
    private const uint Sliver = 6355;
    private const string SliverLine = "You combine the two motes into a larger sliver.";
    private const string BarWin = "You combine the two bars into an ingot of high quality.";
    private const string BarLose = "You combine the two bars into an ingot.";
    private const uint Quality = 6331;
    private const uint Plain = 6330;
    private const string GemLine = "The gem slips into the weapon altering its properties.";
    private const uint Stone = 6320;
    private const uint Wand = 46122;
    private const uint StonedWand = 46128;

    private static readonly RecipeExpectation Motes = new(Sliver, SliverLine, null, null);
    private static readonly RecipeExpectation Bars = new(Quality, BarWin, Plain, BarLose);
    private static readonly RecipeExpectation Gem = new(StonedWand, GemLine, StonedWand, GemLine);

    private static RecipeHeldItem Held(uint guid, uint wcid, string name = "Thing", int stack = 1) => new(guid, wcid, name, stack);

    private static ChatLine System(string text) => new(1, ChatKind.System, 0x18, null, 0, text);

    private static ChatLine Transient(string text) => new(1, ChatKind.Transient, 0, null, 0, text);

    private static ChatLine Error(uint code, string text = "") => new(1, ChatKind.WeenieError, code, null, 0, text);

    private static RecipeHeldItem[] TwoMotes => [Held(MoteA, Mote, "Pyreal Mote"), Held(MoteB, Mote, "Pyreal Mote")];

    private static RecipeEvidence Seen(RecipeHeldItem[] before, RecipeHeldItem[] after, uint? code = 0, bool ended = true, params ChatLine[] lines) => new(before, after, lines, code, ended);

    [Fact]
    public void Both_sources_gone_and_the_result_arrived_is_a_success_that_lists_them()
    {
        var o = RecipeOutcomes.Read(Motes, Seen(TwoMotes, [Held(Result, Sliver, "Pyreal Sliver")], lines: System(SliverLine)));

        Assert.Equal(RecipeVerdict.Success, o.Verdict);
        Assert.Equal(RecipeOutcomes.Success, o.Name);
        Assert.Equal([MoteA, MoteB], o.Consumed.Select(c => c.Guid));
        Assert.Equal(Result, Assert.Single(o.Created).Guid);
        Assert.Equal(SliverLine, o.Line);
        Assert.Equal("1 Pyreal Mote (6353), 1 Pyreal Mote (6353)", o.ConsumedText);
        Assert.Equal("1 Pyreal Sliver (6355)", o.CreatedText);
    }

    [Fact]
    public void The_result_arriving_settles_it_before_the_line_and_before_the_use_ends()
    {
        var o = RecipeOutcomes.Read(Motes, Seen(TwoMotes, [Held(Result, Sliver)], code: null, ended: false));

        Assert.Equal(RecipeVerdict.Success, o.Verdict);
        Assert.Null(o.Line);
    }

    [Fact]
    public void A_kept_source_with_one_text_for_both_branches_is_read_from_the_result_recipe_6068()
    {
        RecipeHeldItem[] before = [Held(MoteA, Stone, "Minor Smoldering Stone"), Held(MoteB, Wand, "Atlan Wand")];
        var o = RecipeOutcomes.Read(Gem, Seen(before, [Held(MoteA, Stone, "Minor Smoldering Stone"), Held(Result, StonedWand, "Minor Smoldering Atlan Wand")], lines: System(GemLine)));

        Assert.Equal(RecipeVerdict.Success, o.Verdict);
        Assert.Equal(Wand, Assert.Single(o.Consumed).Wcid);
    }

    [Fact]
    public void One_text_with_nothing_arrived_is_no_result_not_a_success_recipe_6068()
    {
        RecipeHeldItem[] before = [Held(MoteA, Stone), Held(MoteB, Wand)];

        var o = RecipeOutcomes.Read(Gem, Seen(before, before, lines: System(GemLine)));

        Assert.Equal(RecipeVerdict.Refused, o.Verdict);
        Assert.Equal(RecipeOutcomes.NoResult, o.Name);
    }

    [Fact]
    public void The_fail_result_arriving_is_a_failed_roll_and_says_what_went_recipe_789()
    {
        RecipeHeldItem[] before = [Held(MoteA, 6329, "Pyreal Bar"), Held(MoteB, 6329, "Pyreal Bar")];
        var o = RecipeOutcomes.Read(Bars, Seen(before, [Held(Result, Plain, "Pyreal Ingot")], lines: System(BarLose)));

        Assert.Equal(RecipeVerdict.FailedRoll, o.Verdict);
        Assert.Equal(RecipeOutcomes.FailedRoll, o.Name);
        Assert.Equal(2, o.Consumed.Count);
        Assert.Equal(Plain, Assert.Single(o.Created).Wcid);
    }

    [Fact]
    public void The_two_lines_that_differ_only_at_the_end_are_compared_whole_recipe_789()
    {
        // "You combine the two bars into an ingot." is not a part-match of the win line: only the whole text counts, in both directions.
        RecipeHeldItem[] before = [Held(MoteA, 6329), Held(MoteB, 6329)];
        Assert.Equal(RecipeVerdict.Success, RecipeOutcomes.Read(Bars, Seen(before, [Held(Result, Quality)], lines: System(BarWin))).Verdict);

        var loseLine = RecipeOutcomes.Read(Bars, Seen(before, before, lines: System(BarLose)));
        Assert.Equal(RecipeOutcomes.FailedRoll, loseLine.Name);
        Assert.Equal(BarLose, loseLine.Line);

        var winLineOnly = RecipeOutcomes.Read(Bars, Seen(before, before, lines: System(BarWin)));
        Assert.Equal(RecipeOutcomes.NoResult, winLineOnly.Name);
        Assert.Equal(BarWin, winLineOnly.Line);
    }

    [Fact]
    public void One_result_on_both_branches_is_told_apart_by_its_own_fail_line_recipe_6027()
    {
        var expect = new RecipeExpectation(46141, "You strongly imbue the pyreal with the essence of war magic.", 46141, "You strongly imbue the pyreal with the essence of a wand.");
        RecipeHeldItem[] before = [Held(MoteA, 19410), Held(MoteB, Quality)];
        RecipeHeldItem[] after = [Held(Result, 46141)];

        Assert.Equal(RecipeVerdict.FailedRoll, RecipeOutcomes.Read(expect, Seen(before, after, lines: System(expect.FailLine!))).Verdict);
        Assert.Equal(RecipeVerdict.Success, RecipeOutcomes.Read(expect, Seen(before, after, lines: System(expect.SuccessLine))).Verdict);
    }

    [Fact]
    public void A_fail_line_with_no_fail_result_waits_for_the_end_then_is_a_failed_roll()
    {
        var expect = new RecipeExpectation(Sliver, SliverLine, null, "You fail.");

        Assert.False(RecipeOutcomes.Read(expect, Seen(TwoMotes, [], code: null, ended: false, System("You fail."))).Settled);

        var o = RecipeOutcomes.Read(expect, Seen(TwoMotes, [], ended: true, lines: System("You fail.")));
        Assert.Equal(RecipeVerdict.FailedRoll, o.Verdict);
        Assert.Equal(2, o.Consumed.Count);
        Assert.Empty(o.Created);
    }

    [Fact]
    public void A_stack_that_falls_by_one_is_a_part_consumed_and_a_stack_that_rises_a_part_created()
    {
        var o = RecipeOutcomes.Read(Motes, Seen([Held(MoteA, Mote, "Mote", 5), Held(Result, Sliver, "Sliver", 2)], [Held(MoteA, Mote, "Mote", 4), Held(Result, Sliver, "Sliver", 3)]));

        Assert.Equal(RecipeVerdict.Success, o.Verdict);
        Assert.Equal(1, Assert.Single(o.Consumed).Amount);
        Assert.Equal(MoteA, o.Consumed[0].Guid);
        Assert.Equal(1, Assert.Single(o.Created).Amount);
    }

    [Theory]
    [InlineData(0x043Bu, RecipeOutcomes.Untrained)]
    [InlineData(0x043Au, RecipeOutcomes.CombatMode)]
    [InlineData(0x001Du, RecipeOutcomes.Busy)]
    [InlineData(0x048Fu, RecipeOutcomes.Busy)]
    [InlineData(0x0437u, RecipeOutcomes.Requirements)]
    [InlineData(0x0435u, RecipeOutcomes.CraftError)]
    [InlineData(0x0036u, "refused-0x0036")]
    public void A_server_code_on_the_use_or_as_an_error_line_names_the_refusal(uint code, string name)
    {
        var onUse = RecipeOutcomes.Read(Motes, Seen(TwoMotes, TwoMotes, code));
        var asLine = RecipeOutcomes.Read(Motes, Seen(TwoMotes, TwoMotes, 0, true, Error(code)));

        Assert.Equal(RecipeVerdict.Refused, onUse.Verdict);
        Assert.Equal(name, onUse.Name);
        Assert.Equal(name, asLine.Name);
        Assert.Empty(onUse.Consumed);
    }

    [Theory]
    [InlineData("The Pyreal Mote cannot be combined with itself.", RecipeOutcomes.SameItem)]
    [InlineData("The Pyreal Mote cannot be used on the Atlan Wand.", RecipeOutcomes.NoRecipe)]
    [InlineData("You are not trained in Item Tinkering.", RecipeOutcomes.Untrained)]
    public void A_refusal_the_server_words_on_the_chat_names_it_before_the_use_ends(string text, string name)
    {
        var o = RecipeOutcomes.Read(Motes, Seen(TwoMotes, TwoMotes, null, false, System(text)));

        Assert.Equal(RecipeVerdict.Refused, o.Verdict);
        Assert.Equal(name, o.Name);
    }

    [Theory]
    [InlineData("You can't use the Pyreal Mote on itself.", RecipeOutcomes.SameItem)]
    [InlineData("Cannot use the Pyreal Mote with the Pack", RecipeOutcomes.NotUsable)]
    public void A_refusal_on_the_transient_channel_names_it(string text, string name) =>
        Assert.Equal(name, RecipeOutcomes.Read(Motes, Seen(TwoMotes, TwoMotes, 0, true, Transient(text))).Name);

    [Fact]
    public void Speech_that_quotes_a_refusal_or_a_success_line_is_not_evidence()
    {
        var speech = new ChatLine(1, ChatKind.Speech, 0, "Someone", 1, SliverLine);
        var o = RecipeOutcomes.Read(Motes, Seen(TwoMotes, TwoMotes, 0, true, speech, new ChatLine(1, ChatKind.Speech, 0, "Someone", 1, "The Mote cannot be used on the Wand.")));

        Assert.Equal(RecipeOutcomes.NoAnswer, o.Name);
        Assert.Null(o.Line);
    }

    [Fact]
    public void An_arrived_result_beats_a_stray_error_line()
    {
        var o = RecipeOutcomes.Read(Motes, Seen(TwoMotes, [Held(Result, Sliver)], 0, true, Error(0x001D)));

        Assert.Equal(RecipeVerdict.Success, o.Verdict);
    }

    [Fact]
    public void Nothing_moved_and_nothing_said_is_pending_until_the_end_then_no_answer()
    {
        Assert.Equal(RecipeVerdict.Pending, RecipeOutcomes.Read(Motes, Seen(TwoMotes, TwoMotes, null, false)).Verdict);

        var o = RecipeOutcomes.Read(Motes, Seen(TwoMotes, TwoMotes));
        Assert.Equal(RecipeVerdict.Refused, o.Verdict);
        Assert.Equal(RecipeOutcomes.NoAnswer, o.Name);
    }

    [Fact]
    public void Items_gone_with_no_result_and_no_line_is_no_answer_and_says_what_left()
    {
        var o = RecipeOutcomes.Read(Motes, Seen(TwoMotes, []));

        Assert.Equal(RecipeOutcomes.NoAnswer, o.Name);
        Assert.Equal(2, o.Consumed.Count);
        Assert.Contains("gone", o.Why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("You determine that you have a 38 percent chance to succeed.", 38.0)]
    [InlineData("You determine that you have a 100 percent chance to succeed.\n5 percent is due to your augmentation.", 100.0)]
    [InlineData("Do you wish to join?", null)]
    public void The_dialogs_percent_is_read(string text, double? percent) => Assert.Equal(percent, RecipeOutcomes.ChancePercent(text));
}

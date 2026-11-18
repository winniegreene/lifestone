using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// D138: the stop at the paid bot. From level 60 she asks the bot for her credit count, gives one Platinum Scarab by the player's give when she holds none, reads her count, then says the word as before; every
/// failure by name (<c>no-scarab</c>, <c>give-refused</c>, <c>no-credit-after-give</c>, <c>credit-unread</c>, <c>no-credit</c>), and the bot away at the shop is waited for as a named fallback. Under 60 the round is free
/// and nothing is asked. The scripted bot answers as the mod's <c>BuffBotDesk</c> does.
/// </summary>
public sealed partial class BuffBotStationTests
{
    private const uint ScarabStack = 0x80008897;

    private static string Fee(Match f) => f.Groups["fee"].Value;

    private static string CreditsRead(Match f) => f.Groups["credits"].Value;

    [Fact]
    public void Under_level_60_the_round_is_free_no_credit_is_asked_and_no_scarab_is_given()
    {
        var bot = new Bot();
        bot.Level(59);
        bot.Scarabs(ScarabStack, 3);
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(("free", "-", 0), (Fee(f), CreditsRead(f), Int(f, "gave")));
        Assert.Equal(["tusker"], bot.Heard.Select(h => h.Text)); // the word alone: no credit word
        Assert.Empty(bot.Given);
        Assert.Single(bot.Log, l => l.Contains("level 59: a round is free (under level 60)", StringComparison.Ordinal));
    }

    [Fact]
    public void From_level_60_with_a_credit_held_she_asks_her_count_gives_nothing_and_the_round_takes_the_credit()
    {
        var bot = new Bot { Credits = 2 };
        bot.Level(64);
        bot.Scarabs(ScarabStack, 3);
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(("paid", "2", 0), (Fee(f), CreditsRead(f), Int(f, "gave")));
        Assert.Equal([BuffBotWire.CreditWord, "tusker"], bot.Heard.Select(h => h.Text)); // the count first, then the word
        Assert.Empty(bot.Given);
        Assert.Equal(1, bot.Credits); // the round took one
        Assert.Equal((N, N), (Int(f, "lines"), Int(f, "landed")));
        Assert.Contains(bot.Log, l => l.Contains("credit: the bot holds 2 credit(s) of hers: no give", StringComparison.Ordinal));
    }

    [Fact]
    public void From_level_60_with_no_credit_she_gives_one_scarab_reads_one_credit_and_then_says_the_word()
    {
        var bot = new Bot();
        bot.Level(64);
        bot.Scarabs(ScarabStack);
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(("paid", "1", 1), (Fee(f), CreditsRead(f), Int(f, "gave")));
        var (givenAt, item) = Assert.Single(bot.Given);
        Assert.Equal(ScarabStack, item); // the scarab, by the player's give (GameAction 0x00CD), one of it
        var give = Assert.Single(bot.T.R.H.Net.Of(FakeSendKind.GiveObject));
        Assert.Equal((BuffBotWire.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), 1u), (give.Target, give.Amount));
        Assert.True(givenAt < bot.Heard.Single(h => h.Text == "tusker").At); // the give before the word
        Assert.Equal(0, bot.Credits); // given and spent
        Assert.Contains(bot.Log, l => l.Contains("check after: the give took one Platinum Scarab (1 -> 0 in the pack); the bot tells 1 credit(s)", StringComparison.Ordinal));
    }

    [Fact]
    public void From_level_60_with_no_credit_and_no_scarab_the_stop_fails_no_scarab_and_no_word_is_told()
    {
        var bot = new Bot();
        bot.Level(64);
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("buff-bot: no-scarab: level 64 pays a credit a round from 60, the bot holds none of hers and the pack holds no Platinum Scarab (8897)", run.Failure, StringComparison.Ordinal);
        Assert.Equal(["credits"], bot.Heard.Select(h => h.Text));
        Assert.Empty(bot.Given);
        Assert.Equal(("paid", "0", 0, "no"), (Fee(f), CreditsRead(f), Int(f, "gave"), f.Groups["ok"].Value));
    }

    [Fact]
    public void A_give_the_bot_refuses_fails_give_refused_by_ACEs_line()
    {
        var bot = new Bot { RefuseGive = true };
        bot.Level(64);
        bot.Scarabs(ScarabStack);
        var (run, status, _) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("buff-bot: give-refused: Koji the Enchanter did not take the Platinum Scarab: Examined", run.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(bot.Heard, h => h.Text == "tusker");
    }

    [Fact]
    public void A_give_taken_with_no_credit_told_after_it_fails_no_credit_after_give()
    {
        var bot = new Bot { SilentAfterGive = true };
        bot.Level(64);
        bot.Scarabs(ScarabStack);
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("buff-bot: no-credit-after-give: the Platinum Scarab was given (You give Koji the Enchanter Platinum Scarab.) and the bot told no count within 10 s", run.Failure, StringComparison.Ordinal);
        Assert.Equal(1, Int(f, "gave"));
        Assert.DoesNotContain(bot.Heard, h => h.Text == "tusker");
    }

    [Fact]
    public void A_bot_that_tells_no_count_fails_credit_unread_and_a_bot_that_says_she_needs_a_credit_fails_no_credit()
    {
        var mute = new Bot { MuteCredits = true };
        mute.Level(64);
        mute.Scarabs(ScarabStack);
        var (run, status, _) = mute.Run();
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("buff-bot: credit-unread: the bot told no count within 10 s of 'credits'", run.Failure, StringComparison.Ordinal);
        Assert.Empty(mute.Given);

        var needs = new Bot { Credits = 1, AlwaysNeedsCredit = true };
        needs.Level(64);
        var (run2, status2, f2) = needs.Run();
        Assert.Equal(ScenarioStatus.Failed, status2);
        Assert.Contains("buff-bot: no-credit: the bot answered the word 'tusker' with \"You need a credit: hand me a Platinum Scarab.\" and casts nothing (level 64; the count read before the word was 1)", run2.Failure, StringComparison.Ordinal);
        Assert.Equal(1, Int(f2, "told")); // never told again
    }

    [Fact]
    public void Her_stop_ends_at_once_with_every_line_up_well_within_the_ten_seconds_the_bot_waits_after_its_done_line()
    {
        // The owner, 2026-11-14: the bot's ten seconds after a round give the customer time to finish; she is done and on her way within them, so she never sees it leave.
        var bot = new Bot { Credits = 1 };
        bot.Level(64);
        var (run, status, _) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        var ended = bot.Now;
        var lastLanded = bot.LandedOverTime[^1].At;
        Assert.InRange(ended - lastLanded, 0, 0.5); // done the frame the last line lands
        Assert.NotNull(bot.DoneDue); // the bot's done line comes a second after its last cast
        Assert.True(ended <= bot.DoneDue!.Value, $"the stop ended at {ended:0.0} s, the done line is due at {bot.DoneDue:0.0} s");
        Assert.True(ended - bot.DoneDue.Value < BuffBotRule.LeaveSeconds, "within the bot's ten seconds after its done line");
        Assert.Equal(10.0, BuffBotRule.LeaveSeconds);
        Assert.Equal(2, bot.Heard.Count); // the count and the word: nothing more is told after the round
    }

    [Fact]
    public void A_done_line_with_a_line_missing_on_a_paid_round_is_never_told_again_and_the_stop_ends_within_ten_seconds()
    {
        var bot = new Bot { Credits = 1, Casts = _ => Tusker.Lines.Take(N - 1) };
        bot.Level(64);
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains($"buff-bot: lines-missing: {N - 1} of {N} lines landed after 1 tell(s)", run.Failure, StringComparison.Ordinal);
        Assert.Equal(1, Int(f, "told")); // a second word would take a second credit
        Assert.Contains(bot.Log, l => l.Contains("no second tell on a paid round", StringComparison.Ordinal));
        Assert.NotNull(bot.DoneAt);
        Assert.InRange(bot.Now - bot.DoneAt!.Value, 0, BuffBotRule.LeaveSeconds - 1); // the one more look is a cast's 8 s, within the bot's ten
    }

    [Fact]
    public void A_rest_the_bot_tells_for_its_mana_lengthens_the_answers_time_and_every_line_lands()
    {
        var rest = (int)BuffBotRule.AnswerSeconds(Tusker) + 60; // longer than the whole answer's time
        var bot = new Bot { Rest = rest };
        var (run, status, f) = bot.Run(maxSeconds: 1200);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 1, 0), (Int(f, "lines"), Int(f, "landed"), Int(f, "told"), Int(f, "recoveries")));
        Assert.Contains(bot.Log, l => l.Contains($"the bot rests for its mana: {rest} s told in all", StringComparison.Ordinal));
    }

    [Fact]
    public void The_bot_away_at_the_shop_answers_away_and_she_waits_for_its_return_when_it_answers_the_word_it_kept()
    {
        // The fallback (D138): she comes while it is out; in normal play she is gone before it leaves.
        var bot = new Bot { Away = true };
        bot.After(40, bot.ComeBack);
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 1, 1), (Int(f, "lines"), Int(f, "landed"), Int(f, "told"), Int(f, "recoveries")));
        Assert.Contains(bot.Log, l => l.Contains("recovery=wait-away: the bot answered the word with \"I am away for tapers: I will answer you when I am back.\"", StringComparison.Ordinal));
        Assert.Contains(bot.Log, l => l.Contains("the bot is back at its post after", StringComparison.Ordinal));
        Assert.Single(bot.Heard); // the word kept, not told again
    }

    [Fact]
    public void The_bot_away_past_the_wait_fails_bot_away()
    {
        var bot = new Bot { Away = true };
        var (run, status, _) = bot.Run(maxSeconds: 900);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("buff-bot: bot-away: the bot answered the word with", run.Failure, StringComparison.Ordinal);
        Assert.Contains("it is not back at its post within 360 s", run.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reads_of_the_bots_lines_are_its_own_count_rest_and_away_lines_by_id_or_name()
    {
        var bot = new Bot();
        var chat = bot.T.R.H.State.Chat;
        var mark = chat.Total;
        bot.Tell(BuffBotWire.Credits(3));
        bot.Tell(BuffBotWire.Rest(40));
        bot.Tell(BuffBotWire.Rest(25));
        Assert.Equal(3, BuffBotRule.CreditsSince(chat, mark));
        Assert.Equal(65, BuffBotRule.RestSince(chat, mark));
        Assert.False(BuffBotRule.AwaySince(chat, mark));
        bot.Say(BuffBotWire.AwayAnnouncement);
        Assert.True(BuffBotRule.AwaySince(chat, mark));
        bot.Say(BuffBotWire.BackAnnouncement);
        Assert.False(BuffBotRule.AwaySince(chat, mark));
        bot.T.R.H.ServerMessage("You have 9 credits."); // a system line is not the bot's
        Assert.Equal(3, BuffBotRule.CreditsSince(chat, mark));
        Assert.Null(BuffBotRule.CreditsSince(chat, chat.Total));

        Assert.True(BuffBotRule.Pays(60));
        Assert.False(BuffBotRule.Pays(59));
        var post = new Lifestone.Data.World.CellFrame(BuffBotWire.Cell, new Lifestone.Data.Animation.AcFrame(new System.Numerics.Vector3(BuffBotWire.X + 2, BuffBotWire.Y, 94), System.Numerics.Quaternion.Identity));
        Assert.True(BuffBotRule.AtPost(post));
        Assert.False(BuffBotRule.Away(post));
        var road = post with { Frame = post.Frame with { Origin = new System.Numerics.Vector3(106.5f, 58.5f, 88) } };
        Assert.True(BuffBotRule.Away(road));
        Assert.False(BuffBotRule.AtPost(road));
        Assert.False(BuffBotRule.Away(new Lifestone.Data.World.CellFrame(J3bRig.AcademyCell, road.Frame))); // another landblock: judged by its words alone
    }
}

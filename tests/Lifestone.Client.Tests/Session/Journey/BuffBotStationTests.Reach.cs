using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 bot reach (live 2026-11-17 04:55:00, ladder52: the bot stopped the round at its twelfth line, "You are out of my reach", and the Armor Self and Bludgeon Protection lines never landed): a round the bot stops
/// out of reach is walked back to and told again, check, act, check, at most <see cref="BuffBotStation.ReachTries"/> times by name; a paid round reads the credit the stop returned and spends no second scarab.
/// </summary>
public sealed partial class BuffBotStationTests
{
    [Fact]
    public void A_round_stopped_out_of_reach_is_walked_back_to_and_told_again_and_every_line_lands()
    {
        var bot = new Bot();
        bot.OutOfReachAfter[1] = 11; // the live round: 11 lines, then out of reach
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 2, 1, "yes"), Counts(f)); // two tells, one recovery
        Assert.Equal(["tusker", "tusker"], bot.Heard.Select(h => h.Text));
        Assert.Equal(2, bot.T.Travel.Requests.Count); // the walk, then the walk back
        Assert.All(bot.T.Travel.Requests, r => Assert.Equal((BuffBotWire.Guid, BuffBotWire.ReachMetres), (r.ObjectGuid, r.Range)));

        var lines = bot.Log.ToList();
        var before = lines.FindIndex(l => l.Contains("recovery=walk-back 1 of 2: check before: the bot stopped the round with \"" + BuffBotWire.OutOfReach + "\" after 11 of " + N + " lines; her client reads her ", StringComparison.Ordinal));
        var after = lines.FindIndex(l => l.Contains("recovery=walk-back 1 of 2: check after: her client reads her ", StringComparison.Ordinal));
        var second = lines.FindLastIndex(l => l.Contains("told 'tusker': the server echoed it", StringComparison.Ordinal));
        Assert.True(before >= 0 && after > before && second > after, "check before, the walk back, check after, then the word again");
        Assert.DoesNotContain(lines, l => l.Contains("recovery=tell-again", StringComparison.Ordinal)); // the walk back's tell is its own
    }

    [Fact]
    public void A_bot_that_stops_out_of_reach_every_time_fails_out_of_reach_by_name_after_the_named_number_of_walks()
    {
        var bot = new Bot();
        for (var talk = 1; talk <= BuffBotStation.ReachTries + 1; talk++)
        {
            bot.OutOfReachAfter[talk] = 3;
        }

        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(BuffBotStation.ReachTries + 1, Int(f, "told"));
        Assert.Equal(BuffBotStation.ReachTries, Int(f, "recoveries"));
        Assert.Contains("buff-bot: out-of-reach: the bot stopped the round short", run.Failure, StringComparison.Ordinal);
        Assert.Contains($"{BuffBotStation.ReachTries} walk(s) back to it, the most is {BuffBotStation.ReachTries}: BuffBotStation.ReachTries", run.Failure, StringComparison.Ordinal);
        Assert.Equal(BuffBotStation.ReachTries + 1, bot.T.Travel.Requests.Count);
    }

    [Fact]
    public void A_paid_round_stopped_out_of_reach_reads_the_returned_credit_and_gives_no_second_scarab()
    {
        var bot = new Bot();
        bot.Level(79);
        bot.Scarabs(ScarabStack, 3);
        bot.OutOfReachAfter[2] = 11; // the second talk is the word (the first is the credit count)
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(("paid", 1), (Fee(f), Int(f, "gave"))); // one scarab in all: the stopped round gave its credit back
        Assert.Single(bot.Given);
        Assert.Equal([BuffBotWire.CreditWord, "tusker", BuffBotWire.CreditWord, "tusker"], bot.Heard.Select(h => h.Text));
        Assert.Equal(0, bot.Credits);
        Assert.Equal((N, N, 1), (Int(f, "lines"), Int(f, "landed"), Int(f, "recoveries")));
        Assert.Contains(bot.Log, l => l.Contains("recovery=walk-back 1 of 2: check before:", StringComparison.Ordinal) && l.Contains("her credit read again, the word told again", StringComparison.Ordinal));
    }
}

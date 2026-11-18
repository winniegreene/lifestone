using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 item 5 (Q1): <see cref="QuestReads"/> reads ACE's exact strings (@95ef426d): the reward line <c>You've earned {amount:N0} experience.</c>
/// (<c>WorldObjects/Player_Xp.cs:114</c>), the kill counter (<c>Managers/QuestManager.cs:619-626</c>) and the give lines
/// (<c>WorldObjects/Player_Inventory.cs:3400-3475</c>), each since a chat mark; a line before the mark is never read.
/// </summary>
public sealed class QuestReadsTests
{
    private const string Brighteyes = "Brighteyes, the Tailor";

    private static ChatLine System(string text) => new(0, ChatKind.System, 1, null, 0, text);

    private static ChatLine Tell(string text) => new(0, ChatKind.Tell, 3, "San Ming", 0x7DA5504B, text);

    private static ChatLine Error(uint code, string text) => new(0, ChatKind.WeenieError, code, null, 0, text);

    private static ChatLog Chat(params ChatLine[] lines)
    {
        var chat = new ChatLog();
        foreach (var line in lines)
        {
            chat.Add(line);
        }

        return chat;
    }

    [Theory]
    [InlineData("You've earned 950 experience.", 950L)]
    [InlineData("You've earned 0 experience.", 0L)]
    [InlineData("You've earned 120,000 experience.", 120_000L)] // San Ming's cap (facts.md section 3)
    [InlineData("You've earned 20,000,000 experience.", 20_000_000L)] // Shoichi's award (facts.md section 4)
    [InlineData("You've earned 22,000,000 experience.", 22_000_000L)] // the Devastator Tusk (facts.md section 1)
    [InlineData("You've earned 1.234.567 experience.", 1_234_567L)] // N0 in a culture that groups with '.'
    public void The_reward_line_is_read_with_its_thousands_separators(string text, long amount) =>
        Assert.Equal(amount, QuestReads.ParseReward(text));

    [Theory]
    [InlineData("You've earned 1,20 experience.")]
    [InlineData("You've earned 1200,000 experience.")]
    [InlineData("You've earned experience.")]
    [InlineData("You have earned 100 experience.")]
    [InlineData("You've earned 100 experience")]
    [InlineData("You've earned 100 luminance.")]
    [InlineData(" You've earned 100 experience.")]
    [InlineData(null)]
    public void Any_other_text_is_no_reward_line(string? text) => Assert.Null(QuestReads.ParseReward(text));

    [Fact]
    public void The_reward_since_a_mark_sums_the_system_lines_after_it_and_ignores_the_rest()
    {
        var chat = Chat(System("You've earned 5,000 experience."));
        var mark = chat.Total;
        Assert.Null(QuestReads.Reward(chat, mark)); // the line before the mark is not read
        chat.Add(Tell("You've earned 1,000,000 experience.")); // an NPC's tell is not the server's line
        chat.Add(System("You've earned 100,000 experience."));
        chat.Add(System("Brighteyes, the Tailor gives you Aphus Lassel Portal Gem."));
        chat.Add(System("You've earned 120,000 experience."));
        Assert.Equal(new QuestReward(220_000, 2), QuestReads.Reward(chat, mark));
        Assert.Equal(new QuestReward(225_000, 3), QuestReads.Reward(chat, 0));
    }

    [Fact]
    public void The_kill_counter_reads_the_not_complete_line()
    {
        var c = QuestReads.ParseKillCounter("You have killed 3 Blood Shreths! You must kill 10 to complete your task.");
        Assert.Equal(new QuestKillCounter(3, "Blood Shreths", false, 10, "You have killed 3 Blood Shreths! You must kill 10 to complete your task."), c);
    }

    [Fact]
    public void The_kill_counter_reads_the_complete_line_and_a_plural_with_spaces()
    {
        var done = QuestReads.ParseKillCounter("You have killed 10 Blood Shreths! Your task is complete!")!;
        Assert.Equal((10, "Blood Shreths", true, (int?)null), (done.Killed, done.Plural, done.Complete, done.MustKill));
        Assert.Contains(QuestReads.TaskComplete, done.Text, StringComparison.Ordinal);

        var guards = QuestReads.ParseKillCounter("You have killed 500 Tusker Guards! Your task is complete!")!;
        Assert.Equal((500, "Tusker Guards", true), (guards.Killed, guards.Plural, guards.Complete));

        var spaced = QuestReads.ParseKillCounter("You have killed 41 Mattekar of the Snowy Wastes! You must kill 500 to complete your task.")!;
        Assert.Equal((41, "Mattekar of the Snowy Wastes", false, (int?)500), (spaced.Killed, spaced.Plural, spaced.Complete, spaced.MustKill));
    }

    [Theory]
    [InlineData("You have killed 3 Blood Shreths!")]
    [InlineData("You have killed Blood Shreths! Your task is complete!")]
    [InlineData("You have killed 3 Blood Shreths! You must kill to complete your task.")]
    [InlineData("You have killed 3 Blood Shreths! Your task is complete")]
    [InlineData("You killed 3 Blood Shreths! Your task is complete!")]
    [InlineData(null)]
    public void Any_other_text_is_no_kill_counter(string? text) => Assert.Null(QuestReads.ParseKillCounter(text));

    [Fact]
    public void The_kill_counters_since_a_mark_are_the_system_lines_after_it_by_plural()
    {
        var chat = Chat(System("You have killed 2 Blood Shreths! You must kill 10 to complete your task."));
        var mark = chat.Total;
        chat.Add(System("You have killed 3 Blood Shreths! You must kill 10 to complete your task."));
        chat.Add(System("You have killed 7 Tusker Guards! You must kill 500 to complete your task."));
        chat.Add(Tell("You have killed 9 Blood Shreths! You must kill 10 to complete your task."));
        chat.Add(System("You have killed 10 Blood Shreths! Your task is complete!"));

        var shreths = QuestReads.KillCounters(chat, mark, "Blood Shreths");
        Assert.Equal([3, 10], shreths.Select(c => c.Killed));
        Assert.True(shreths[^1].Complete);
        Assert.Equal(3, QuestReads.KillCounters(chat, mark).Count);
        Assert.Empty(QuestReads.KillCounters(chat, chat.Total));
    }

    [Fact]
    public void The_give_lines_are_read_with_the_npc_name_that_holds_a_comma()
    {
        var given = QuestReads.ParseGive(System("You give Brighteyes, the Tailor Female Tusker Tusk."), Brighteyes)!;
        Assert.Equal((QuestGiveOutcome.Given, "Female Tusker Tusk", true), (given.Outcome, given.Item, given.Accepted));

        var examined = QuestReads.ParseGive(System("You allow Brighteyes, the Tailor to examine your Sharp Tusker Slave Tusk."), Brighteyes)!;
        Assert.Equal((QuestGiveOutcome.Examined, "Sharp Tusker Slave Tusk", false), (examined.Outcome, examined.Item, examined.Accepted));

        Assert.Equal(QuestGiveOutcome.NotWanted, QuestReads.ParseGive(Error(0x046A, Brighteyes), Brighteyes)!.Outcome);
        Assert.Equal(QuestGiveOutcome.Busy, QuestReads.ParseGive(Error(0x04CE, Brighteyes), Brighteyes)!.Outcome);
        Assert.Equal(QuestGiveOutcome.NotAccepting, QuestReads.ParseGive(Error(0x03EF, Brighteyes), Brighteyes)!.Outcome);
        Assert.Equal(QuestGiveOutcome.Cowers, QuestReads.ParseGive(Error(0x058A, Brighteyes), Brighteyes)!.Outcome);
        Assert.Null(QuestReads.ParseGive(Error(0x046A, Brighteyes), Brighteyes)!.Item);

        Assert.Null(QuestReads.ParseGive(System("You give Pathwarden Thorolf Pathwarden Token."), Brighteyes)); // another NPC
        Assert.Null(QuestReads.ParseGive(Error(0x046A, "Pathwarden Thorolf"), Brighteyes));
        Assert.Null(QuestReads.ParseGive(Error(0x001D, Brighteyes), Brighteyes)); // not a give refusal
        Assert.Null(QuestReads.ParseGive(Tell("You give Brighteyes, the Tailor Female Tusker Tusk."), Brighteyes));
        Assert.Null(QuestReads.ParseGive(System("Brighteyes, the Tailor gives you Aphus Lassel Portal Gem."), Brighteyes));
    }

    [Fact]
    public void The_give_since_a_mark_is_the_first_give_line_after_it()
    {
        var chat = Chat(System("You give Brighteyes, the Tailor Male Tusker Tusk."));
        var mark = chat.Total;
        Assert.Null(QuestReads.Give(chat, mark, Brighteyes)); // the earlier give is before the mark
        chat.Add(Error(0x04CE, Brighteyes));
        chat.Add(System("You give Brighteyes, the Tailor Female Tusker Tusk."));
        Assert.Equal(QuestGiveOutcome.Busy, QuestReads.Give(chat, mark, Brighteyes)!.Outcome);
        Assert.Equal("Female Tusker Tusk", QuestReads.Give(chat, mark + 1, Brighteyes)!.Item);
    }

    [Fact]
    public void The_reply_and_what_was_said_since_a_mark_are_the_Academys_reads()
    {
        var chat = Chat(Tell("So, you wish to prove your hunting prowess?"));
        var mark = chat.Total;
        Assert.True(QuestReads.Replied(chat, mark, null)); // a step with no reply
        Assert.False(QuestReads.Replied(chat, mark, "hunting prowess")); // before the mark
        Assert.Equal("; said: nothing", QuestReads.Said(chat, mark));

        chat.Add(Tell("Excellent work, friend! You have slain many of the terrible beasts!"));
        chat.Add(System("You've earned 120,000 experience."));
        Assert.True(QuestReads.Replied(chat, mark, "Excellent work, friend!"));
        Assert.False(QuestReads.Replied(chat, mark, "excellent work")); // case sensitive
        Assert.Equal("; said: Tell \"Excellent work, friend! You have slain many of the terrible beasts!\" | System \"You've earned 120,000 experience.\"", QuestReads.Said(chat, mark));
        Assert.Empty(QuestReads.Since(chat, chat.Total + 5)); // a mark past the end reads nothing
        Assert.Equal(3, QuestReads.Since(chat, -10).Count);
    }

    [Fact]
    public void A_mark_older_than_the_kept_lines_reads_every_kept_line()
    {
        var chat = new ChatLog();
        for (var i = 0; i < ChatLog.Capacity + 20; i++)
        {
            chat.Add(System($"line {i}"));
        }

        var since = QuestReads.Since(chat, 10);
        Assert.Equal(ChatLog.Capacity, since.Count);
        Assert.Equal("line 20", since[0].Text);
        Assert.Equal($"line {ChatLog.Capacity + 19}", since[^1].Text);
    }
}

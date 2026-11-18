using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Quests;

/// <summary>The quest reward lines read since a mark (<see cref="QuestReads.Reward"/>): the experience they name, summed, and how many there were.</summary>
/// <param name="Experience">The experience the lines name, summed (ACE prints the amount after its modifiers).</param>
/// <param name="Lines">How many reward lines there were (at least 1).</param>
public sealed record QuestReward(long Experience, int Lines);

/// <summary>One kill counter line (<see cref="QuestReads.ParseKillCounter"/>): ACE's <c>You have killed N plural! ...</c>.</summary>
/// <param name="Killed">The kills the task has counted (the registry's completions; it stops at the task's max).</param>
/// <param name="Plural">The creature's plural name as ACE prints it (<c>Blood Shreths</c>, <c>Tusker Guards</c>).</param>
/// <param name="Complete">True for <c>Your task is complete!</c>.</param>
/// <param name="MustKill">The task's max from <c>You must kill M to complete your task.</c>, or null on a complete line.</param>
/// <param name="Text">The line as sent.</param>
public sealed record QuestKillCounter(int Killed, string Plural, bool Complete, int? MustKill, string Text);

/// <summary>What the server said of a give to an NPC (<see cref="QuestReads.ParseGive"/>).</summary>
public enum QuestGiveOutcome
{
    /// <summary><c>You give &lt;npc&gt; &lt;item&gt;.</c>: the NPC took it (its Give emote runs next).</summary>
    Given = 0,

    /// <summary><c>You allow &lt;npc&gt; to examine your &lt;item&gt;.</c>: a Refuse emote; the item stays in the pack.</summary>
    Examined = 1,

    /// <summary>WeenieError 0x046A <c>TradeAiDoesntWant</c>: the NPC has no Give or Refuse emote for the item.</summary>
    NotWanted = 2,

    /// <summary>WeenieErrorWithString 0x04CE <c>AiRefuseItemDuringEmote</c>: the NPC's emote is still running; give again when it ends.</summary>
    Busy = 3,

    /// <summary>WeenieErrorWithString 0x03EF <c>_IsNotAcceptingGiftsRightNow</c>: a Give emote exists but the NPC does not allow gives.</summary>
    NotAccepting = 4,

    /// <summary>WeenieErrorWithString 0x058A <c>_CowersFromYou</c>: the NPC will not take anything from this character.</summary>
    Cowers = 5,
}

/// <summary>One give line (<see cref="QuestReads.ParseGive"/>).</summary>
/// <param name="Outcome">What the line says.</param>
/// <param name="Npc">The NPC's name as the caller gave it.</param>
/// <param name="Item">The item's name as the line prints it (a stack's count first, <c>3 Tusks</c>), or null on a refusal that names none.</param>
/// <param name="Text">The line as sent (a WeenieError line's string: the NPC's name).</param>
public sealed record QuestGive(QuestGiveOutcome Outcome, string Npc, string? Item, string Text)
{
    /// <summary>True when the NPC took the item.</summary>
    public bool Accepted => Outcome == QuestGiveOutcome.Given;
}

/// <summary>
/// M6 item 5 (Q1): what a quest step reads from the chat since a mark (a mark is <see cref="ChatLog.Total"/> taken before the act): the NPC's reply, the
/// quest reward line, the kill counter and the give lines, each to a small record. Pure: no session state beyond the <see cref="ChatLog"/> and the mark,
/// and it logs nothing (the caller logs what it read). The server's own strings, cited @95ef426d (<c>vendor-eval/ACE/Source/ACE.Server/</c>):
/// <list type="bullet">
/// <item>The reward: <c>You've earned {amount:N0} experience.</c>, system chat (Broadcast), sent for every quest-source grant (AwardXP, AwardNoShareXP,
/// AwardLevelProportionalXP): <c>WorldObjects/Player_Xp.cs:113-114</c>. The amount carries thousands separators in the server's culture; any one separator
/// between groups of three digits is read.</item>
/// <item>The kill counter: <c>You have killed {n} {plural}!</c> then <c> Your task is complete!</c> at the task's max, else <c> You must kill {max} to complete
/// your task.</c>, system chat (Broadcast), sent only when the task is armed (<c>Managers/QuestManager.cs:605-626</c>).</item>
/// <item>The give: <c>You give {npc} {count }{item}.</c> (system chat, <c>WorldObjects/Player_Inventory.cs:3432-3435</c>), the Refuse emote's
/// <c>You allow {npc} to examine your {item}.</c> (:3446), and the WeenieErrorWithString refusals carrying the NPC's name: 0x04CE busy (:3407), 0x03EF not
/// accepting (:3454), 0x046A doesn't want (:3475), 0x058A cowers (:3400).</item>
/// </list>
/// </summary>
public static partial class QuestReads
{
    /// <summary>ACE <c>WeenieError.TradeAiDoesntWant</c> (@95ef426d <c>ACE.Entity/Enum/WeenieError.cs:536</c>).</summary>
    public const uint NotWantedCode = 0x046A;

    /// <summary>ACE <c>WeenieErrorWithString.AiRefuseItemDuringEmote</c> (@95ef426d <c>ACE.Entity/Enum/WeenieErrorWithString.cs:134</c>).</summary>
    public const uint BusyCode = 0x04CE;

    /// <summary>ACE <c>WeenieErrorWithString._IsNotAcceptingGiftsRightNow</c> (@95ef426d <c>ACE.Entity/Enum/WeenieErrorWithString.cs:64</c>).</summary>
    public const uint NotAcceptingCode = 0x03EF;

    /// <summary>ACE <c>WeenieErrorWithString._CowersFromYou</c> (@95ef426d <c>ACE.Entity/Enum/WeenieErrorWithString.cs:579</c>).</summary>
    public const uint CowersCode = 0x058A;

    /// <summary>The kill counter's complete text (ACE <c>Managers/QuestManager.cs:622</c>), the check a kill step's <c>expect.reply</c> names.</summary>
    public const string TaskComplete = "Your task is complete!";

    /// <summary>The chat lines added since <paramref name="mark"/> that the log still keeps, oldest first.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark"><see cref="ChatLog.Total"/> before the act.</param>
    public static IReadOnlyList<ChatLine> Since(ChatLog chat, long mark)
    {
        ArgumentNullException.ThrowIfNull(chat);
        var fresh = (int)Math.Max(0, Math.Min(chat.Count, chat.Total - mark));
        var lines = new ChatLine[fresh];
        for (var i = 0; i < fresh; i++)
        {
            lines[i] = chat[chat.Count - fresh + i];
        }

        return lines;
    }

    /// <summary>True when <paramref name="reply"/> (a case-sensitive substring, <see cref="QuestExpect.Reply"/>) is in a chat line since <paramref name="mark"/>; true when there is no reply to read.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark"><see cref="ChatLog.Total"/> before the act.</param>
    /// <param name="reply">The reply, or null.</param>
    public static bool Replied(ChatLog chat, long mark, string? reply) =>
        reply is null || Since(chat, mark).Any(l => l.Text is { } text && text.Contains(reply, StringComparison.Ordinal));

    /// <summary>What the server said since <paramref name="mark"/>: every chat line, as <c>; said: Kind "text" | ...</c>, or <c>; said: nothing</c>.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark"><see cref="ChatLog.Total"/> before the act.</param>
    public static string Said(ChatLog chat, long mark)
    {
        var lines = Since(chat, mark).Select(l => $"{l.Kind} \"{l.Text}\"").ToList();
        return lines.Count == 0 ? "; said: nothing" : "; said: " + string.Join(" | ", lines);
    }

    /// <summary>The experience a reward line names (<c>You've earned 120,000 experience.</c> is 120000), or null for any other text.</summary>
    /// <param name="text">The line's text.</param>
    public static long? ParseReward(string? text)
    {
        if (text is null || RewardPattern().Match(text) is not { Success: true } m)
        {
            return null;
        }

        var digits = new string(m.Groups[1].Value.Where(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    /// <summary>The system reward lines since <paramref name="mark"/>, summed, or null when there was none.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark"><see cref="ChatLog.Total"/> before the act.</param>
    public static QuestReward? Reward(ChatLog chat, long mark)
    {
        long sum = 0;
        var lines = 0;
        foreach (var line in Since(chat, mark))
        {
            if (line.Kind == ChatKind.System && ParseReward(line.Text) is { } amount)
            {
                sum += amount;
                lines++;
            }
        }

        return lines == 0 ? null : new QuestReward(sum, lines);
    }

    /// <summary>The kill counter a line holds, or null for any other text.</summary>
    /// <param name="text">The line's text.</param>
    public static QuestKillCounter? ParseKillCounter(string? text)
    {
        if (text is null || KillCounterPattern().Match(text) is not { Success: true } m
            || !int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var killed))
        {
            return null;
        }

        if (m.Groups["done"].Success)
        {
            return new QuestKillCounter(killed, m.Groups["plural"].Value, true, null, text);
        }

        return int.TryParse(m.Groups["max"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var max)
            ? new QuestKillCounter(killed, m.Groups["plural"].Value, false, max, text)
            : null;
    }

    /// <summary>Every system kill counter line since <paramref name="mark"/>, oldest first; only those of <paramref name="plural"/> when it is given.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark"><see cref="ChatLog.Total"/> before the act.</param>
    /// <param name="plural">The creature's plural name (<c>Blood Shreths</c>), or null for every task's.</param>
    public static IReadOnlyList<QuestKillCounter> KillCounters(ChatLog chat, long mark, string? plural = null) =>
        Since(chat, mark)
            .Where(l => l.Kind == ChatKind.System)
            .Select(l => ParseKillCounter(l.Text))
            .OfType<QuestKillCounter>()
            .Where(c => plural is null || string.Equals(c.Plural, plural, StringComparison.Ordinal))
            .ToList();

    /// <summary>What a chat line says of a give to <paramref name="npc"/>, or null when it is not a give line naming that NPC.</summary>
    /// <param name="line">The line.</param>
    /// <param name="npc">The NPC's name as the server sends it (<c>Brighteyes, the Tailor</c>): it splits the line, since both names may hold spaces.</param>
    public static QuestGive? ParseGive(ChatLine line, string npc)
    {
        ArgumentException.ThrowIfNullOrEmpty(npc);
        var text = line.Text ?? string.Empty;
        if (line.Kind == ChatKind.System)
        {
            var given = "You give " + npc + " ";
            if (text.Length > given.Length + 1 && text.StartsWith(given, StringComparison.Ordinal) && text.EndsWith('.'))
            {
                return new QuestGive(QuestGiveOutcome.Given, npc, text[given.Length..^1], text);
            }

            var examine = "You allow " + npc + " to examine your ";
            if (text.Length > examine.Length + 1 && text.StartsWith(examine, StringComparison.Ordinal) && text.EndsWith('.'))
            {
                return new QuestGive(QuestGiveOutcome.Examined, npc, text[examine.Length..^1], text);
            }

            return null;
        }

        if (line.Kind != ChatKind.WeenieError || !string.Equals(text, npc, StringComparison.Ordinal))
        {
            return null;
        }

        QuestGiveOutcome? refusal = line.ChatType switch
        {
            NotWantedCode => QuestGiveOutcome.NotWanted,
            BusyCode => QuestGiveOutcome.Busy,
            NotAcceptingCode => QuestGiveOutcome.NotAccepting,
            CowersCode => QuestGiveOutcome.Cowers,
            _ => null,
        };
        return refusal is { } r ? new QuestGive(r, npc, null, text) : null;
    }

    /// <summary>The first give line naming <paramref name="npc"/> since <paramref name="mark"/> (the give's outcome), or null when the server has said none yet.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark"><see cref="ChatLog.Total"/> before the give.</param>
    /// <param name="npc">The NPC's name as the server sends it.</param>
    public static QuestGive? Give(ChatLog chat, long mark, string npc)
    {
        foreach (var line in Since(chat, mark))
        {
            if (ParseGive(line, npc) is { } give)
            {
                return give;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^You've earned (\d{1,3}(?:[,.  ' ]\d{3})*) experience\.$", RegexOptions.CultureInvariant)]
    private static partial Regex RewardPattern();

    [GeneratedRegex(@"^You have killed (?<n>\d+) (?<plural>.+?)! (?:(?<done>Your task is complete!)|You must kill (?<max>\d+) to complete your task\.)$", RegexOptions.CultureInvariant)]
    private static partial Regex KillCounterPattern();
}

using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 errand lapse (live ladder57, 2026-11-17 UTC: the buff bot cast the 28 lines of <c>all</c> at 09:40:39, each for an hour; the Atlan wand's hunt-until in the Crater Caves began at 09:52:39 and ran past the hour; nothing
/// renewed the bot's lines, and from 10:40:43 every boon ran out: War Magic 378 to 326, the kills twice as long, the incoming about 2.45 times, the race lost at 10:42:05). The owner, 2026-11-17: she relies on her own buffs; she
/// does not need the bot. Which of the bot's lines on her she covers with her own: a bot line covers one of her own profile's lines (<see cref="Lifestone.ServerTools.BuffBotLine.Line"/>, the same stat or skill family), and her
/// book's best castable level of that line covers it (<see cref="BookCasts"/>); the fight's refresh casts it under the bot's, and it takes over when the bot's ends. A line with no such level in her book has nothing to cover
/// it: it is warned by name (<see cref="WarnToken"/>). Pure: the reads are the caller's (<see cref="BuffBotRule.Read"/>), the book's answer is a function.
/// </summary>
public static class BuffLapseRule
{
    /// <summary>The token of the warning when a bot line she has nothing to cover with is about to lapse.</summary>
    public const string WarnToken = "m6-line-lapse-uncovered";

    /// <summary>The bot's lines split by whether her book covers them.</summary>
    /// <param name="LeastOwn">The fewest seconds left of a line her book covers (infinite when none).</param>
    /// <param name="OwnLine">That line's name, or null.</param>
    /// <param name="LeastBot">The fewest seconds left of a line nothing of hers covers (0 for one not up; infinite when none).</param>
    /// <param name="BotLine">That line's name, or null.</param>
    /// <param name="Own">How many her book covers.</param>
    /// <param name="BotOnly">How many nothing of hers covers.</param>
    public readonly record struct Split(double LeastOwn, string? OwnLine, double LeastBot, string? BotLine, int Own, int BotOnly)
    {
        /// <summary>The split's words: <c>covered by her own 2 (least Armor Self 418 s), uncovered 1 (least Sprint Self 412 s)</c>.</summary>
        public string Text => string.Create(CultureInfo.InvariantCulture, $"covered by her own {Own}{(OwnLine is null ? string.Empty : $" (least {OwnLine} {LeastOwn:0} s)")}, uncovered {BotOnly}{(BotLine is null ? string.Empty : $" (least {BotLine} {LeastBot:0} s)")}");
    }

    /// <summary>Splits <paramref name="reads"/> by <paramref name="covers"/> (a line's name: her book holds a castable level of it).</summary>
    /// <param name="reads">The bot's lines as read on her.</param>
    /// <param name="covers">Whether her book covers the line named.</param>
    public static Split Of(IReadOnlyList<BuffBotLineRead> reads, Func<string, bool> covers)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(covers);
        var (leastOwn, leastBot) = (double.PositiveInfinity, double.PositiveInfinity);
        string? ownLine = null;
        string? botLine = null;
        var (own, bot) = (0, 0);
        foreach (var read in reads)
        {
            var left = read.Up ? read.RemainingSeconds : 0;
            if (covers(read.Line.Line))
            {
                own++;
                if (left < leastOwn)
                {
                    (leastOwn, ownLine) = (left, read.Line.Line);
                }
            }
            else
            {
                bot++;
                if (left < leastBot)
                {
                    (leastBot, botLine) = (left, read.Line.Line);
                }
            }
        }

        return new Split(leastOwn, ownLine, leastBot, botLine, own, bot);
    }

    /// <summary>
    /// True when the long fight should keep her lines from its start: a line of the bot's stands on her and the least of them has under <paramref name="expectedSeconds"/> (the fight's expected length) left, so the bot's hour
    /// ends inside the fight and her own lines must take over line by line, each as it nears its lapse (the refresh's batching spreads them).
    /// </summary>
    /// <param name="reads">The bot's lines as read on her.</param>
    /// <param name="expectedSeconds">The fight's expected seconds.</param>
    public static bool KeepFromStart(IReadOnlyList<BuffBotLineRead> reads, double expectedSeconds)
    {
        ArgumentNullException.ThrowIfNull(reads);
        var least = double.PositiveInfinity;
        foreach (var read in reads)
        {
            if (read.Up)
            {
                least = Math.Min(least, read.RemainingSeconds);
            }
        }

        return least < expectedSeconds;
    }

    /// <summary>
    /// True when her book holds a castable level of the line named <paramref name="line"/> (the bot's <see cref="Lifestone.ServerTools.BuffBotLine.Line"/>, her own profile's name): the climber's profile has the line, the
    /// spellbook is read and holds one of its levels, and the line's school is trained. A line her profile does not hold (Sprint, Jumping Mastery) has nothing of hers to cover it.
    /// </summary>
    /// <param name="self">Her state.</param>
    /// <param name="line">The line's name.</param>
    public static bool BookCasts(SelfState self, string line)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (BuffProfiles.Climber.LineNamed(line) is not { } profile || !self.Spellbook.IsKnown)
        {
            return false;
        }

        var held = false;
        foreach (var spell in profile.Spells)
        {
            held |= self.Spellbook.Knows(spell.Id);
        }

        return held && self.Qualities.TryGetSkill(GearCasts.SkillOf(profile.School), out var skill) && skill.AdvancementClass >= HeadlessCharacter.TrainedSkill;
    }
}

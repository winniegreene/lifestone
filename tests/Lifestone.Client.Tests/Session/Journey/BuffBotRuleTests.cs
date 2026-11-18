using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 S2c: the rules of the stop at the buff bot, with no session in them (<see cref="BuffBotRule"/>). A line of the profile is the bot's when the bot's own spell (<see cref="BuffBotWire"/>: the Other
/// spell of the line at level VII) is in her enchantments, read with the seconds it has left on the session clock; the counts, the least seconds, the 45-minute rule at home and the time one answer may take.
/// </summary>
public sealed class BuffBotRuleTests
{
    private static BuffBotProfile Tusker => BuffBotWire.Tusker;

    private static uint Spell(string line) => Tusker.Lines.Single(l => l.Line == line).SpellId;

    /// <summary>One entry of the registry: <paramref name="spell"/> with <paramref name="duration"/> seconds, arrived at <paramref name="received"/> on the session clock.</summary>
    private static ActiveEnchantment Entry(uint spell, double received, double duration, ushort layer = 1) =>
        new(new EnchantmentDesc((ushort)spell, layer, 1, 0, 300, 0.0, duration, BuffBotWire.Guid, 0f, 0f, 0.0, 0x9001, 1, 0f, 0, EnchantmentBucket.Additive), received);

    private static PlayerQualities Qualities(params ActiveEnchantment[] entries)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.SetActiveEnchantments([.. entries]);
        return q;
    }

    /// <summary>Every line of the profile up, arrived at 0 with <paramref name="duration"/> seconds.</summary>
    private static PlayerQualities Whole(double duration = 3600) => Qualities([.. Tusker.Lines.Select(l => Entry(l.SpellId, 0, duration))]);

    private static List<BuffBotLineRead> Read(PlayerQualities q, double now, BuffBotProfile? profile = null)
    {
        var reads = new List<BuffBotLineRead> { default }; // never empty at the call: Read clears it first
        BuffBotRule.Read(profile ?? Tusker, q, now, reads);
        return reads;
    }

    [Fact]
    public void The_profile_and_the_rules_numbers_are_the_plans()
    {
        Assert.NotEmpty(Tusker.Lines); // the list itself, by spell and name, is pinned in the mod's BuffBotRulesTests
        Assert.Equal(Tusker.Lines.Count, Tusker.Lines.Select(l => l.SpellId).Distinct().Count());
        Assert.Equal(3300, BuffBotRule.FreshSeconds);
        Assert.Equal(2700, BuffBotRule.RenewUnderSeconds); // 45 minutes of the spell's hour
        Assert.True(BuffBotRule.RenewUnderSeconds < BuffBotRule.FreshSeconds); // a stop that just ended is never due again at once
    }

    [Fact]
    public void Read_gives_every_line_in_the_profiles_order_with_the_bots_spell_and_its_seconds_left()
    {
        const double now = 400;
        var q = Qualities(
            Entry(Spell("Focus Self"), 100, 3600),                 // 3,300 s left at 400
            Entry(Spell("Willpower Self"), 0, 900, layer: 1),      // two entries of one spell: the longer is read
            Entry(Spell("Willpower Self"), 300, 2100, layer: 2),
            Entry(Spell("Strength Self"), 0, 100),                 // kept past its end (no timer removes an entry): up, with nothing left
            Entry(Spell("Armor Self"), 0, -1),                     // no end
            Entry(2067, 0, 3600),                                  // her own Focus Self VII: not the bot's spell
            Entry(60001, 390, 3600));                              // a spell of no line

        var reads = Read(q, now);

        Assert.Equal(Tusker.Lines, reads.Select(r => r.Line));
        BuffBotLineRead Of(string line) => reads.Single(r => r.Line.Line == line);
        Assert.Equal((true, 3300.0), (Of("Focus Self").Up, Of("Focus Self").RemainingSeconds));
        Assert.Equal((true, 2000.0), (Of("Willpower Self").Up, Of("Willpower Self").RemainingSeconds));
        Assert.Equal((true, 0.0), (Of("Strength Self").Up, Of("Strength Self").RemainingSeconds));
        Assert.True(Of("Armor Self").Up);
        Assert.True(double.IsPositiveInfinity(Of("Armor Self").RemainingSeconds));
        Assert.Equal(4, reads.Count(r => r.Up));
        Assert.All(reads.Where(r => !r.Up), r => Assert.Equal(0.0, r.RemainingSeconds)); // a line that is not up has no seconds

        // The same registry read later: the clock is the caller's.
        Assert.Equal(1300.0, Read(q, 2400).Single(r => r.Line.Line == "Focus Self").RemainingSeconds);
        Assert.Equal(0.0, Read(q, 9000).Single(r => r.Line.Line == "Focus Self").RemainingSeconds);
    }

    [Fact]
    public void Read_is_empty_handed_on_an_empty_registry_and_refuses_nulls()
    {
        var reads = Read(Qualities(), 0);
        Assert.Equal(Tusker.Lines.Count, reads.Count);
        Assert.DoesNotContain(reads, r => r.Up);

        Assert.Empty(Read(Whole(), 0, new BuffBotProfile("none", ["none"], "nothing", [])));
        Assert.Throws<ArgumentNullException>(() => BuffBotRule.Read(null!, Qualities(), 0, []));
        Assert.Throws<ArgumentNullException>(() => BuffBotRule.Read(Tusker, null!, 0, []));
        Assert.Throws<ArgumentNullException>(() => BuffBotRule.Read(Tusker, Qualities(), 0, null!));
    }

    [Fact]
    public void CountWith_counts_the_lines_up_with_at_least_the_seconds()
    {
        var q = Qualities(
            Entry(Spell("Focus Self"), 0, 3600),
            Entry(Spell("Willpower Self"), 0, 3300),
            Entry(Spell("Strength Self"), 0, 2700),
            Entry(Spell("Armor Self"), 0, 100));
        var reads = Read(q, 0);

        Assert.Equal(4, BuffBotRule.CountWith(reads, 1));
        Assert.Equal(3, BuffBotRule.CountWith(reads, 2700)); // the edge counts
        Assert.Equal(2, BuffBotRule.CountWith(reads, 2700.5));
        Assert.Equal(2, BuffBotRule.CountWith(reads, BuffBotRule.FreshSeconds));
        Assert.Equal(1, BuffBotRule.CountWith(reads, 3600));
        Assert.Equal(0, BuffBotRule.CountWith(reads, 3601));
        Assert.Equal(4, BuffBotRule.CountWith(reads, 0)); // a line that is not up is never counted, even at 0 seconds
        Assert.Equal(0, BuffBotRule.CountWith([], 0));

        // Past its end an entry is still in the registry: it counts at 0 and at no second more.
        var late = Read(q, 200);
        Assert.Equal(4, BuffBotRule.CountWith(late, 0));
        Assert.Equal(3, BuffBotRule.CountWith(late, 1));
    }

    [Fact]
    public void MinRemaining_is_the_least_seconds_of_any_line_and_0_when_a_line_is_down()
    {
        Assert.Equal(0, BuffBotRule.MinRemaining([]));
        Assert.Equal(3600, BuffBotRule.MinRemaining(Read(Whole(), 0)));
        Assert.Equal(3100, BuffBotRule.MinRemaining(Read(Whole(), 500)));

        // One line shorter than the rest is the least.
        var shorter = Qualities([.. Tusker.Lines.Select(l => Entry(l.SpellId, 0, l.Line == "Mana Renewal Self" ? 1200 : 3600))]);
        Assert.Equal(1200, BuffBotRule.MinRemaining(Read(shorter, 0)));

        // One line down: 0, whatever the others have.
        var oneDown = Qualities([.. Tusker.Lines.Where(l => l.Line != "Armor Self").Select(l => Entry(l.SpellId, 0, 3600))]);
        var reads = Read(oneDown, 0);
        Assert.Equal(Tusker.Lines.Count - 1, BuffBotRule.CountWith(reads, 1));
        Assert.Equal(0, BuffBotRule.MinRemaining(reads));
        Assert.Equal(0, BuffBotRule.MinRemaining(Read(Qualities(), 0)));
    }

    [Theory]
    [InlineData(3600, 0, false)]     // just cast
    [InlineData(3600, 899.5, false)] // 2,700.5 s left
    [InlineData(3600, 900, false)]   // 2,700 s left: the edge is not due
    [InlineData(3600, 900.5, true)]  // 2,699.5 s left
    [InlineData(3600, 3599, true)]
    [InlineData(3600, 4000, true)]   // past its end
    public void Due_at_home_is_a_line_under_45_minutes(double duration, double now, bool due)
    {
        Assert.Equal(due, BuffBotRule.Due(Read(Whole(duration), now)));
    }

    [Fact]
    public void Due_reads_one_line_down_or_short_and_nothing_from_no_lines()
    {
        var oneDown = Qualities([.. Tusker.Lines.Skip(1).Select(l => Entry(l.SpellId, 0, 3600))]);
        var oneShort = Qualities([.. Tusker.Lines.Select((l, i) => Entry(l.SpellId, 0, i == Tusker.Lines.Count - 1 ? 2699 : 3600))]);

        Assert.True(BuffBotRule.Due(Read(oneDown, 0)));
        Assert.True(BuffBotRule.Due(Read(oneShort, 0)));
        Assert.True(BuffBotRule.Due(Read(Qualities(), 0))); // nothing up: the first visit
        Assert.False(BuffBotRule.Due(Read(Whole(), 0)));
        Assert.False(BuffBotRule.Due([])); // a profile of no lines asks for no stop
    }

    [Fact]
    public void The_rules_judge_by_the_profile_read_the_tuskers_lines_alone_are_whole_for_tusker_and_nine_short_for_all()
    {
        // The owner, 2026-11-12: she says the word all. Every line of the tuskers' profile on her, fresh: whole by the tuskers' profile, and by 'all' the nine lines it adds are down.
        var q = Whole();
        var tusker = Read(q, 0, Tusker);
        var all = Read(q, 0, BuffBotWire.All);
        Assert.Equal((19, 28), (tusker.Count, all.Count));
        Assert.Equal(BuffBotWire.All.Lines, all.Select(r => r.Line));
        Assert.Equal((19, 19), (BuffBotRule.CountWith(tusker, BuffBotRule.FreshSeconds), BuffBotRule.CountWith(all, BuffBotRule.FreshSeconds)));
        Assert.Equal((3600.0, 0.0), (BuffBotRule.MinRemaining(tusker), BuffBotRule.MinRemaining(all)));
        Assert.False(BuffBotRule.Due(tusker));
        Assert.True(BuffBotRule.Due(all)); // the stop is due until every line of the chosen profile is up
        Assert.Equal(["Blade Protection Self", "Piercing Protection Self", "Fire Protection Self", "Cold Protection Self", "Acid Protection Self", "Lightning Protection Self", "Magic Resistance Self", "Impregnability Self", "Arcane Enlightenment Self"], all.Where(r => !r.Up).Select(r => r.Line.Line));

        // Every line of 'all' on her: whole by both, and the least seconds are those of the profile read: a protection of 'all' alone about to lapse is the least of 'all' and is not read by the tuskers' profile.
        var whole = Qualities([.. BuffBotWire.All.Lines.Select(l => Entry(l.SpellId, 0, l.Line == "Piercing Protection Self" ? 300 : 3600))]);
        Assert.Equal((3600.0, 300.0), (BuffBotRule.MinRemaining(Read(whole, 0, Tusker)), BuffBotRule.MinRemaining(Read(whole, 0, BuffBotWire.All))));
        Assert.True(BuffBotRule.MinRemaining(Read(whole, 0, BuffBotWire.All)) < BuffBotRule.TripUnderSeconds); // what the trip home keys on
        Assert.False(BuffBotRule.Due(Read(Qualities([.. BuffBotWire.All.Lines.Select(l => Entry(l.SpellId, 0, 3600))]), 0, BuffBotWire.All)));
    }

    [Fact]
    public void AnswerSeconds_is_each_casts_time_and_the_margin()
    {
        Assert.Equal(8, BuffBotStation.CastSeconds);
        Assert.Equal(30, BuffBotStation.MarginSeconds);
        Assert.Equal((Tusker.Lines.Count * 8) + 30, BuffBotRule.AnswerSeconds(Tusker));
        Assert.Equal(46, BuffBotRule.AnswerSeconds(new BuffBotProfile("two", ["two"], "two lines", [.. Tusker.Lines.Take(2)])));
        Assert.Equal(30, BuffBotRule.AnswerSeconds(new BuffBotProfile("none", ["none"], "nothing", [])));
        Assert.Equal(30, BuffBotRule.AnswerSeconds(null!));

        // One answer's time is well inside what is left of the hour when the last line is read: a line cast first is still fresh (FreshSeconds) when the last one lands. It holds for every profile: the 28 lines of 'all' too.
        Assert.True(3600 - BuffBotRule.AnswerSeconds(Tusker) >= BuffBotRule.FreshSeconds);
        Assert.Equal((28 * 8) + 30, BuffBotRule.AnswerSeconds(BuffBotWire.All));
        Assert.All(BuffBotWire.Profiles, p => Assert.True(3600 - BuffBotRule.AnswerSeconds(p) >= BuffBotRule.FreshSeconds, p.Key));
    }
}

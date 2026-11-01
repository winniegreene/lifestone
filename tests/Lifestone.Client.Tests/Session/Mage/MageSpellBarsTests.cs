using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The BSD mage's spell bars (M3 4.2 p1): the plan over <see cref="Spellbook.SpellBars"/> as a pure function (empty bars, the earlier plan's bars, the exact bars, a spell the book lacks), the plan played against ACE's bar rules
/// (AddSpellToBar: a spell already on the bar stays where it is; TryRemoveSpellFromBar), and the read model applying a sent add or remove at once with a later description winning.
/// </summary>
public sealed class MageSpellBarsTests
{
    private static Spellbook BookWith(IEnumerable<uint> known, ImmutableArray<ImmutableArray<uint>> bars) => Book(new ReducerHarness(), known, bars);

    private static Spellbook Book(ReducerHarness h, IEnumerable<uint> known, ImmutableArray<ImmutableArray<uint>> bars, bool enter = true)
    {
        if (enter)
        {
            h.EnterWorld();
        }

        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Spells = known.ToImmutableDictionary(id => id, _ => 2f), SpellBars = bars });
        return h.State.Self.Spellbook;
    }

    private static IEnumerable<uint> Everything() => ArenaProfile.SpellsToLearn;

    private static ImmutableArray<ImmutableArray<uint>> Bars(uint[] one, uint[] two, uint[]? three = null)
    {
        var bars = ImmutableArray.CreateBuilder<ImmutableArray<uint>>();
        for (var b = 0; b < 8; b++)
        {
            bars.Add(b == 0 ? [.. one] : b == 1 ? [.. two] : b == 2 && three is not null ? [.. three] : []);
        }

        return bars.ToImmutable();
    }

    /// <summary>The bars as ACE keeps them after the steps: an add inserts at the index clamped to the bar's length unless the spell is on it; a remove takes it off.</summary>
    private static List<List<uint>> PlayAce(ImmutableArray<ImmutableArray<uint>> start, IEnumerable<BarStep> plan)
    {
        var bars = start.Select(b => b.IsDefault ? [] : b.ToList()).ToList();
        foreach (var step in plan)
        {
            var list = bars[step.Bar];
            if (step.Remove)
            {
                list.Remove(step.Spell);
            }
            else if (!list.Contains(step.Spell))
            {
                list.Insert(Math.Min(step.Index, list.Count), step.Spell);
            }
        }

        return bars;
    }

    [Fact]
    public void The_two_tabs_are_the_owners_lists()
    {
        // Tab I: Focus, Willpower, Creature Enchantment Mastery at VI, then the 24 buff lines at VII in the buff cycle's order (Focus, Willpower, Creature Enchantment Mastery among them).
        var expected = new List<uint> { 1426, 1450, 562 };
        expected.AddRange(BuffCycle.OrderedLines.Where(l => !l.ByPlay).Select(l => l.Spells[6].Id)); // M5 7 P7j: Spirit Drinker, learned by play, is not on the bars
        Assert.Equal(expected, MageSpellBars.BuffTab);
        Assert.Equal(27, MageSpellBars.BuffTab.Count);
        Assert.Equal(27, MageSpellBars.BuffTab.Distinct().Count());
        Assert.Equal<uint>([2067, 2091, 2215], MageSpellBars.BuffTab.Skip(3).Take(3)); // Inner Calm, Mind Blossom, Adja's Blessing: the three VIIs
        Assert.Equal<uint>([2170, 2128, 2083, 2345, 2073], MageSpellBars.FightTab);
        Assert.All(ArenaProfile.FightExtras, id => Assert.Contains(id, ArenaProfile.SpellsToLearn));
    }

    [Fact]
    public void Empty_bars_plan_only_adds_at_each_index_in_order_and_leave_the_other_tabs_alone()
    {
        var plan = MageSpellBars.Plan(BookWith(Everything(), Bars([], [])));

        Assert.DoesNotContain(plan, s => s.Remove);
        Assert.Equal(MageSpellBars.BuffTab, plan.Where(s => s.Bar == 0).Select(s => s.Spell));
        Assert.Equal(Enumerable.Range(0, 27), plan.Where(s => s.Bar == 0).Select(s => s.Index));
        Assert.Equal(MageSpellBars.FightTab, plan.Where(s => s.Bar == 1).Select(s => s.Spell));
        Assert.DoesNotContain(plan, s => s.Bar > 1);
    }

    [Fact]
    public void Todays_bars_the_profile_on_bar_1_and_the_bolts_on_bar_2_are_cleared_from_tabs_I_and_II_and_tab_III_stays()
    {
        var profile = ArenaProfile.Lines.Where(l => !l.ByPlay).Select(l => l.Spells[6].Id).ToArray(); // the earlier plan's bar 1 (a line per spell, spec order)
        var bolts = new uint[] { 27, 81, 82, 83, 84, 85, 2128 };
        var start = Bars([], profile, bolts);
        var plan = MageSpellBars.Plan(BookWith(Everything(), start));

        Assert.DoesNotContain(plan, s => s.Bar > 1); // the bolts on tab III are not touched
        var played = PlayAce(start, plan);
        Assert.Equal(MageSpellBars.BuffTab, played[0]);
        Assert.Equal(MageSpellBars.FightTab, played[1]);
        Assert.Equal(bolts, played[2]);
        var tabTwo = plan.Where(s => s.Bar == 1).ToList();
        Assert.Equal(profile.Length, tabTwo.TakeWhile(s => s.Remove).Count()); // the removals come first
        Assert.All(tabTwo.Where(s => s.Remove), s => Assert.Contains(s.Spell, profile));
    }

    [Fact]
    public void The_exact_bars_plan_nothing_and_a_run_after_a_run_sends_nothing()
    {
        var exact = Bars([.. MageSpellBars.BuffTab], [.. MageSpellBars.FightTab], [27, 81]);
        Assert.Empty(MageSpellBars.Plan(BookWith(Everything(), exact)));

        var start = Bars([2067, 1426], [2128, 2170]);
        var played = PlayAce(start, MageSpellBars.Plan(BookWith(Everything(), start)));
        Assert.Empty(MageSpellBars.Plan(BookWith(Everything(), Bars([.. played[0]], [.. played[1]]))));
    }

    [Fact]
    public void A_bar_that_matches_only_in_part_keeps_the_prefix_and_removes_the_rest()
    {
        var tab = MageSpellBars.BuffTab;
        var start = Bars([tab[0], tab[1], 999, tab[2]], [MageSpellBars.FireVulnerabilityOtherVii, 5, MageSpellBars.FlameBoltVii]);
        var plan = MageSpellBars.Plan(BookWith(Everything(), start));

        Assert.Equal(new BarStep(true, tab[2], 0, 0), plan[0]); // last first
        Assert.Equal(new BarStep(true, 999u, 0, 0), plan[1]);
        Assert.Equal(new BarStep(false, tab[2], 2, 0), plan[2]); // the prefix of two stays
        Assert.DoesNotContain(plan, s => s.Spell == tab[0] || s.Spell == tab[1]);
        Assert.DoesNotContain(plan, s => s.Spell == MageSpellBars.FireVulnerabilityOtherVii);
        var played = PlayAce(start, plan);
        Assert.Equal(tab, played[0]);
        Assert.Equal(MageSpellBars.FightTab, played[1]);
    }

    [Fact]
    public void A_spell_the_book_lacks_is_left_off_and_logged_by_name_and_the_rest_keep_their_places()
    {
        var h = new ReducerHarness();
        var known = Everything().Where(id => id != MageSpellBars.HealSelfVii && id != MageSpellBars.FireVulnerabilityOtherVii).ToArray();
        var log = new RecordingLogSink();
        var plan = MageSpellBars.Plan(Book(h, known, Bars([], [])), log);

        Assert.Equal<uint>([2128, 2083, 2345], plan.Where(s => s.Bar == 1).Select(s => s.Spell));
        Assert.Equal([0, 1, 2], plan.Where(s => s.Bar == 1).Select(s => s.Index));
        Assert.True(log.AnyLineContains("Fire Vulnerability Other VII is not in the spellbook"));
        Assert.True(log.AnyLineContains("Heal Self VII is not in the spellbook"));
        Assert.Empty(MageSpellBars.Plan(BookWith([], Bars([], []))));
    }

    [Fact]
    public void The_read_model_applies_a_sent_add_and_remove_at_once_and_a_later_description_wins()
    {
        var h = new ReducerHarness();
        var book = Book(h, [27, 81, 82], Bars([27], [81]));
        h.ClearEvents();

        h.Reducer.ApplyFavoriteAdd(82, 0, 0); // inserted first
        h.Reducer.ApplyFavoriteAdd(81, 9, 0); // past the end: appended
        h.Reducer.ApplyFavoriteAdd(27, 0, 1);
        h.Reducer.ApplyFavoriteAdd(81, 0, 1); // already on the bar: stays
        h.Reducer.ApplyFavoriteAdd(999, 0, 1); // not known: ignored
        h.Reducer.ApplyFavoriteRemove(81, 1);
        h.Reducer.ApplyFavoriteRemove(555, 1); // not on the bar: nothing
        Assert.Equal<uint>([82, 27, 81], book.Bar(0));
        Assert.Equal<uint>([27], book.Bar(1));
        Assert.Equal(4, h.Of<SpellbookChanged>().Count(c => c.Change == SpellbookChange.Favorites)); // one event per change

        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(ReducerHarness.PlayerGuid, 2, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Spells = new[] { 27u, 81u }.ToImmutableDictionary(id => id, _ => 2f), SpellBars = Bars([81], []) });
        Assert.Equal<uint>([81], book.Bar(0));
        Assert.Empty(book.Bar(1));
    }

    [Fact]
    public void The_action_api_sends_the_remove_and_the_read_model_follows_the_send()
    {
        var r = new WorldActionRig();
        Book(r.H.R, [27, 81], Bars([27, 81], []), enter: false);

        var remove = r.Api.RemoveSpellFavorite(27, 0, ActionSource.Host);
        var add = r.Api.AddSpellFavorite(27, 1, 0, ActionSource.Host);
        r.H.Tick();
        r.H.Tick();

        Assert.Equal(ActionOutcomeKind.Sent, remove.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Sent, add.Outcome!.Value.Kind);
        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.RemoveSpellFavorite));
        Assert.Equal((27u, 0), (sent.Guid, sent.Index));
        Assert.Equal<uint>([81, 27], r.H.R.State.Self.Spellbook.Bar(0));
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.RemoveSpellFavorite(0, 0, ActionSource.Host).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.RemoveSpellFavorite(27, 8, ActionSource.Host).Outcome!.Value.Kind);
    }
}

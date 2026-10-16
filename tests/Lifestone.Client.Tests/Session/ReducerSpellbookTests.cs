using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The spellbook read model (M3 1.2 lane s1; <see cref="Spellbook"/>): filled from the PlayerDescription (the known spells, the eight favourite bars in order, the filters, the desired components) and
/// grown by GameEvent 0x02C1 (<c>ClientMagicSystem::Handle_Magic__UpdateSpell</c>). ACE sends "You learn the X spell." as its own chat message, so the reducer composes no line for 0x02C1.
/// </summary>
public sealed class ReducerSpellbookTests
{
    private const uint Player = ReducerHarness.PlayerGuid;

    /// <summary>A harness that also records <see cref="SpellbookChanged"/> (the shared harness tracks a fixed list of events).</summary>
    private static ReducerHarness Rig()
    {
        var h = new ReducerHarness();
        return h;
    }

    private static ReducerHarness LiveDescription()
    {
        var h = Rig();
        h.EnterWorld();
        InboundDispatcher.Dispatch(h.Context, PlayerDescriptionCostTests.LiveShaped(Player), h.Reducer);
        return h;
    }

    [Fact]
    public void Nothing_is_known_until_the_description_arrives()
    {
        var book = new ReducerHarness().State.Self.Spellbook;

        Assert.False(book.IsKnown);
        Assert.Equal(0, book.Count);
        Assert.False(book.Knows(1));
        Assert.Empty(book.SpellBars);
        Assert.Empty(book.Bar(0));
        Assert.Empty(book.DesiredComponents);
    }

    [Fact]
    public void The_description_fills_the_spells_the_eight_bars_in_order_and_the_desired_components()
    {
        var h = LiveDescription();
        var book = h.State.Self.Spellbook;

        Assert.True(book.IsKnown);
        Assert.Equal(400, book.Count);
        Assert.True(book.Knows(1));
        Assert.True(book.Knows(400));
        Assert.False(book.Knows(401));
        Assert.True(book.TryGetValue(7, out _));
        Assert.Equal(8, book.SpellBars.Length);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (uint)i), book.Bar(0).ToArray());
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (uint)(7 * 20 + i)), book.Bar(7).ToArray());
        Assert.Empty(book.Bar(8));
        Assert.Empty(book.Bar(-1));
        Assert.Equal(30, book.DesiredComponents.Length);
        Assert.Equal(new DesiredComponentDesc(1, 10), book.DesiredComponents[0]);
        Assert.Equal(new DesiredComponentDesc(30, 300), book.DesiredComponents[29]);
        var changed = Assert.Single(h.Of<SpellbookChanged>());
        Assert.Equal(new SpellbookChanged(SpellbookChange.Description, 0, 400, true), changed);
    }

    [Fact]
    public void The_description_carries_the_filters_and_the_bars_exactly_as_decoded()
    {
        var h = Rig();
        h.EnterWorld();
        ImmutableArray<ImmutableArray<uint>> bars = [[5u, 6u], [], [9u]];

        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { SpellbookFilters = 0x1234, SpellBars = bars, Spells = ImmutableDictionary<uint, float>.Empty.Add(5, 1.5f) });

        var book = h.State.Self.Spellbook;
        Assert.Equal(0x1234u, book.Filters);
        Assert.Equal([5u, 6u], book.Bar(0).ToArray());
        Assert.Empty(book.Bar(1));
        Assert.Equal([9u], book.Bar(2).ToArray());
        Assert.True(book.TryGetValue(5, out var value));
        Assert.Equal(1.5f, value);
    }

    [Fact]
    public void A_learned_spell_joins_the_book_with_the_value_zero_and_announces_it()
    {
        var h = LiveDescription();
        var book = h.State.Self.Spellbook;
        h.ClearEvents();
        var chatBefore = h.State.Chat.Count;

        var result = InboundDispatcher.Dispatch(h.Context, AceM3Writers.MagicUpdateSpell(Player, 31, 0x0C8B), h.Reducer);

        Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
        Assert.Equal(401, book.Count);
        Assert.True(book.Knows(0x0C8B));
        Assert.True(book.TryGetValue(0x0C8B, out var value));
        Assert.Equal(0f, value);
        Assert.Equal(new SpellbookChanged(SpellbookChange.Learned, 0x0C8B, 401, true), Assert.Single(h.Of<SpellbookChanged>()));
        Assert.Equal(chatBefore, h.State.Chat.Count); // ACE's own chat message says "You learn the X spell."
        Assert.Equal(0, h.Reducer.Counters.DecodedNotApplied);
    }

    [Fact]
    public void A_spell_the_book_already_holds_keeps_its_value_and_is_reported_as_not_new()
    {
        var h = Rig();
        h.EnterWorld();
        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Spells = ImmutableDictionary<uint, float>.Empty.Add(0x0C8B, 1.25f) });
        h.ClearEvents();

        InboundDispatcher.Dispatch(h.Context, AceM3Writers.MagicUpdateSpell(Player, 31, 0x0C8B), h.Reducer);

        var book = h.State.Self.Spellbook;
        Assert.Equal(1, book.Count);
        Assert.True(book.TryGetValue(0x0C8B, out var value));
        Assert.Equal(1.25f, value);
        Assert.False(Assert.Single(h.Of<SpellbookChanged>()).IsNew);
    }

    [Fact]
    public void A_new_description_replaces_the_book()
    {
        var h = LiveDescription();
        var book = h.State.Self.Spellbook;
        InboundDispatcher.Dispatch(h.Context, AceM3Writers.MagicUpdateSpell(Player, 31, 0x0C8B), h.Reducer);

        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(Player, 2, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg());

        Assert.True(book.IsKnown);
        Assert.Equal(0, book.Count);
        Assert.Empty(book.SpellBars);
        Assert.Empty(book.DesiredComponents);
    }

    [Fact]
    public void Leaving_the_world_clears_the_book_with_the_qualities()
    {
        var h = LiveDescription();
        var book = h.State.Self.Spellbook;
        Assert.Equal(400, book.Count);

        h.Reducer.OnCharacterLogOff(h.Context);

        Assert.False(book.IsKnown);
        Assert.Equal(0, book.Count);
        Assert.Empty(book.SpellBars);
        Assert.False(h.State.Self.Qualities.IsKnown);
    }
}

using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Xunit;

namespace Lifestone.Client.Tests.Session.Creatures;

/// <summary>
/// M6 16 E1e (docs/automation-engine.md stage E1; m6-plan.md D139): a kind's maximum health and Magic Defense with their words (<see cref="KindNumbers"/>), the part of slice E1b's read the fight now shares with the
/// legs, and the legs' named read (<see cref="LegKindRead"/>): a number the bestiary does not hold is null, never assumed, and what a leg takes instead is said once a kind a leg. The fight's own lines are pinned where
/// they were (<c>FightScenarioTests</c>, E1b's); the legs' runs are <c>RungChangeTests</c>'. Nothing here needs the dats or a server.
/// </summary>
public sealed class KindNumbersTests
{
    private const uint DrudgeLurker = 1608; // the world database's creature table: health 117, Magic Defense 113
    private const uint NoWeenie = 999_999_999;

    /// <summary>A record the bestiary could hold with no maximum health and no row of the table: a profile's kind the table does not hold (none today).</summary>
    private static CreatureCapabilities Bare(uint wcid) =>
        Bestiary.Of(new CreatureFact(wcid, "Profile Only", 0, 0, new LadderHitter("Profile Only", wcid, 10, 0.5, 60), 0, 1, "test-ground:targets"), null, $"TODO(m6-creature-health): weenie {wcid}", null, null, [], 1.0,
            CreatureAwareness.DefaultMetres, awarenessPinned: false);

    [Fact]
    public void A_kind_the_bestiary_holds_reads_both_numbers_with_their_source_and_allocates_nothing()
    {
        var kind = KindNumbers.Of(DrudgeLurker);

        Assert.Equal((117, 113), (kind.MaxHealth, kind.MagicDefence));
        Assert.True(kind.Complete);
        Assert.Same(Bestiary.Find(DrudgeLurker), kind.Kind);
        Assert.Equal("maximum health 117 (the world database's creature table)", kind.HealthWords("unused"));
        Assert.Equal("Magic Defense 113", kind.DefenceWords);
        AllocationAssert.Nothing(() => _ = KindNumbers.Of(DrudgeLurker).Complete);
    }

    [Fact]
    public void A_number_the_bestiary_does_not_hold_is_null_and_its_words_say_what_the_caller_takes()
    {
        var bare = KindNumbers.For(8, Bare(8));
        Assert.Null(bare.MaxHealth);
        Assert.Null(bare.MagicDefence);
        Assert.False(bare.Complete);
        Assert.Equal("no maximum health above 0 (TODO(m6-creature-health): weenie 8; TODO(m6-creature-health): the caller's fallback)", bare.HealthWords("the caller's fallback"));
        Assert.Equal("no Magic Defense (the world database's creature table does not hold the kind)", bare.DefenceWords);

        var none = KindNumbers.Of(NoWeenie);
        Assert.Null(none.Kind);
        Assert.Null(none.MaxHealth);
        Assert.Null(none.MagicDefence);
        Assert.False(none.Complete);
        Assert.Equal("an Odd Thing (class 999999999) is no kind of the bestiary (no creature weenie of the pinned world database's table)", none.NotHeldWords("an Odd Thing"));
    }

    [Fact]
    public void A_leg_names_a_class_the_bestiary_does_not_hold_once_and_says_what_it_takes()
    {
        var lines = new List<string>();
        var kinds = new LegKindRead("fight-back", lines.Add);

        Assert.True(kinds.Of(DrudgeLurker, "Drudge Lurker").Complete); // both numbers: no line
        Assert.Empty(lines);

        for (var i = 0; i < 3; i++)
        {
            var kind = kinds.Of(NoWeenie, "Odd Thing");
            Assert.Null(kind.MaxHealth);
            Assert.Null(kind.MagicDefence);
        }

        Assert.False(kinds.Of(0, "?").Complete); // class 0: its object is gone; no kind and no line
        var line = Assert.Single(lines);
        Assert.Equal(
            "fight-back: TODO(engine-bestiary-unknown-kind): 'Odd Thing' (class 999999999) is no kind of the bestiary (no creature weenie of the pinned world database's table): a life cast at it takes a large target, "
            + $"{FightScenario.LifeThroughUnknownHealth} health, so the spell's own take decides, and its Magic Defense is taken as 0, rolled as ACE rolls a Magic Defense of 0 (the 0.03 curve of her skill alone); named once on this leg",
            line);
        Assert.Equal([NoWeenie], kinds.Named);
    }

    [Fact]
    public void A_leg_names_a_kind_the_bestiary_holds_with_no_number_once_in_the_fights_words()
    {
        var lines = new List<string>();
        var kinds = new LegKindRead("kill", lines.Add, wcid => KindNumbers.For(wcid, Bare(wcid)));

        _ = kinds.Of(8, "Profile Only");
        _ = kinds.Of(8, "Profile Only");

        var line = Assert.Single(lines);
        Assert.Equal(
            "kill: target facts: Profile Only (class 8): no maximum health above 0 (TODO(m6-creature-health): weenie 8; TODO(m6-creature-health): a life cast at it takes a large target, 10000 health, so the spell's own take decides), "
            + "no Magic Defense (the world database's creature table does not hold the kind) (its Magic Defense is taken as 0, rolled as ACE rolls a Magic Defense of 0 (the 0.03 curve of her skill alone)), from the bestiary; named once on this leg",
            line);
    }
}

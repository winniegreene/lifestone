using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The decisions of <c>@lifestone item</c> and <c>spell learn</c> that need nothing from ACE (server/mod/Lifestone.ServerMod/Core/ItemCreateRules.cs and
/// Shared SpellOutcomes, compiled here by link): which weenies are refused with which code, how the stack settles the way ACE's
/// <c>@ci</c> does (clamped to MaxStackSize, ignored without one), and how a spell id is classified.
/// </summary>
public sealed class ItemCreateRulesTests
{
    [Fact]
    public void A_weenie_the_database_does_not_have_is_unknown_and_is_checked_first()
    {
        var refusal = ItemCreateRules.Refusal(99_999_999, found: false, typeAllowed: false, typeName: string.Empty, stuck: true);
        Assert.Equal(ToolErrors.UnknownWcid, refusal!.Code);
        Assert.Equal("no weenie has class id 99999999", refusal.Message);
    }

    [Fact]
    public void A_type_ACE_never_spawns_and_a_stuck_weenie_are_not_creatable()
    {
        var type = ItemCreateRules.Refusal(5, found: true, typeAllowed: false, typeName: "Portal", stuck: false);
        Assert.Equal(ToolErrors.NotCreatable, type!.Code);
        Assert.Equal("weenie 5 is a Portal, which ACE never spawns", type.Message);

        var stuck = ItemCreateRules.Refusal(6, found: true, typeAllowed: true, typeName: "Generic", stuck: true);
        Assert.Equal(ToolErrors.NotCreatable, stuck!.Code);
        Assert.Equal("weenie 6 cannot be picked up, so it cannot go in your pack", stuck.Message);
    }

    [Fact]
    public void A_plain_weenie_is_not_refused()
    {
        Assert.Null(ItemCreateRules.Refusal(1628, found: true, typeAllowed: true, typeName: "Generic", stuck: false));
    }

    [Theory]
    [InlineData(null, 100, 1, 1, false)] // no stack asked: the object keeps its own
    [InlineData(null, null, 1, 1, false)]
    [InlineData(null, 100, 7, 7, false)]
    [InlineData(1, 100, 1, 1, false)]
    [InlineData(25, 100, 1, 25, false)] // within the maximum
    [InlineData(100, 100, 1, 100, false)] // exactly the maximum
    [InlineData(500, 100, 1, 100, true)] // above it: clamped, as HandleCI does
    [InlineData(65535, 250, 1, 250, true)]
    [InlineData(5, null, 1, 1, true)] // an item that does not stack ignores the stack: one is created
    [InlineData(1, null, 1, 1, false)] // ... which is not a clamp when one was asked
    public void The_stack_is_clamped_or_ignored_as_HandleCI_does(int? requested, int? maxStack, int weenieStack, int expectedStack, bool expectedClamped)
    {
        Assert.Equal((expectedStack, expectedClamped), ItemCreateRules.ResolveStack(requested, maxStack, weenieStack));
    }

    [Fact]
    public void The_new_error_codes_are_distinct_and_listed()
    {
        var codes = new[] { ToolErrors.UnknownWcid, ToolErrors.NotCreatable, ToolErrors.NoRoom };
        Assert.Equal(3, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Contains(c, ToolErrors.All));
    }

    [Theory]
    [InlineData(false, false, SpellOutcomes.Unknown)]
    [InlineData(false, true, SpellOutcomes.Unknown)] // a spellbook cannot hold an id the table lacks, but the table decides first
    [InlineData(true, false, SpellOutcomes.Learned)]
    [InlineData(true, true, SpellOutcomes.Known)]
    public void A_spell_is_classified_by_the_table_then_the_spellbook(bool inTable, bool known, string expected)
    {
        Assert.Equal(expected, SpellOutcomes.Classify(inTable, known));
    }

    [Fact]
    public void The_counts_follow_the_outcomes_and_a_ready_spellbook_is_all_known()
    {
        var ready = SpellLearnData.Of([new SpellOutcome(1, SpellOutcomes.Known, "A"), new SpellOutcome(2, SpellOutcomes.Known, "B")]);
        Assert.Equal((0, 2, 0), (ready.Learned, ready.Known, ready.Unknown));

        var mixed = SpellLearnData.Of([new SpellOutcome(1, SpellOutcomes.Learned, "A"), new SpellOutcome(99999, SpellOutcomes.Unknown), new SpellOutcome(1, SpellOutcomes.Known, "A")]);
        Assert.Equal((1, 1, 1), (mixed.Learned, mixed.Known, mixed.Unknown));
        Assert.Equal(3, mixed.Spells.Count);
        Assert.Equal(SpellLearnData.Of(mixed.Spells.ToArray()), mixed);
    }
}

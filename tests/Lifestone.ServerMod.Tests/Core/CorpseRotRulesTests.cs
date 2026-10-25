using System.Globalization;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The rules of <c>@lifestone corpses rot</c> (server/mod/Lifestone.ServerMod/Core/CorpseRotRules.cs, compiled here by link): only a player corpse whose
/// name is exactly "Corpse of " and the caller's own name is matched, and only a matched corpse with more than ACE's empty-decay time left (or none set, or
/// never rotting) is set. The ACE calls around them (the landblock's queue, the one write) are checked by ModSourceTests and the live legs.
/// </summary>
public sealed class CorpseRotRulesTests
{
    private const string Caller = "BSD Mage";
    private const double EmptyDecayTime = 15.0;

    /// <summary>A player's corpse as ACE keeps it: a Level, not a monster's, an hour to rot.</summary>
    private static CorpseCandidate Mine(string name = "Corpse of BSD Mage", double? timeToRot = 3600) => new(name, HasLevel: true, IsMonster: false, timeToRot);

    [Fact]
    public void A_player_corpse_named_for_the_caller_matches()
    {
        Assert.True(CorpseRotRules.Matches("Corpse of BSD Mage", isPlayerCorpse: true, Caller));
        Assert.True(CorpseRotRules.Matches(CorpseRotRules.NamePrefix + Caller, isPlayerCorpse: true, Caller));
        Assert.Equal("Corpse of ", CorpseRotRules.NamePrefix);
    }

    [Theory]
    [InlineData("Corpse of Holtburg Hunter")]
    [InlineData("Corpse of BSD Mage 2")]
    [InlineData("Corpse of BSD")]
    [InlineData("Corpse of BSD Mage ")]
    [InlineData(" Corpse of BSD Mage")]
    [InlineData("corpse of BSD Mage")]
    [InlineData("Corpse of bsd mage")]
    [InlineData("CORPSE OF BSD MAGE")]
    [InlineData("Treasure of BSD Mage")]
    [InlineData("Corpse of Corpse of BSD Mage")]
    [InlineData("BSD Mage")]
    [InlineData("Corpse of ")]
    [InlineData("")]
    [InlineData(null)]
    public void Any_other_name_never_matches_even_for_a_player_corpse(string? name)
    {
        Assert.False(CorpseRotRules.Matches(name, isPlayerCorpse: true, Caller));
    }

    [Fact]
    public void A_corpse_that_is_not_a_player_corpse_never_matches_even_with_the_callers_name()
    {
        // A creature's corpse carries no Level, and one made this session is flagged IsMonster; a name alone is not enough.
        Assert.False(CorpseRotRules.Matches("Corpse of BSD Mage", isPlayerCorpse: false, Caller));
        Assert.False(new CorpseCandidate("Corpse of BSD Mage", HasLevel: false, IsMonster: false, 3600).IsPlayerCorpse);
        Assert.False(new CorpseCandidate("Corpse of BSD Mage", HasLevel: true, IsMonster: true, 3600).IsPlayerCorpse);
        Assert.False(new CorpseCandidate("Corpse of BSD Mage", HasLevel: false, IsMonster: true, 3600).IsPlayerCorpse);
        Assert.True(new CorpseCandidate("Corpse of BSD Mage", HasLevel: true, IsMonster: false, 3600).IsPlayerCorpse);
    }

    [Fact]
    public void A_caller_without_a_name_matches_nothing()
    {
        Assert.False(CorpseRotRules.Matches("Corpse of ", isPlayerCorpse: true, string.Empty));
        Assert.False(CorpseRotRules.Matches("Corpse of ", isPlayerCorpse: true, null));
        Assert.False(CorpseRotRules.Matches(null, isPlayerCorpse: true, null));
        Assert.Equal(0, CorpseRotRules.Plan([Mine("Corpse of ")], string.Empty, EmptyDecayTime).Found);
    }

    [Fact]
    public void The_name_is_compared_ordinally_whatever_the_servers_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // Turkish casing would make I and i the same letter across a dotless/dotted pair; an ordinal match never does.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.False(CorpseRotRules.Matches("Corpse of BSD MAGE", isPlayerCorpse: true, "BSD Mage"));
            Assert.False(CorpseRotRules.Matches("Corpse of Isaac", isPlayerCorpse: true, "İsaac"));
            Assert.True(CorpseRotRules.Matches("Corpse of Isaac", isPlayerCorpse: true, "Isaac"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(3600.0, true)]
    [InlineData(18000.0, true)]
    [InlineData(15.001, true)]
    [InlineData(null, true)]
    [InlineData(-1.0, true)]
    [InlineData(15.0, false)]
    [InlineData(14.9, false)]
    [InlineData(5.0, false)]
    [InlineData(0.0, false)]
    [InlineData(-2.0, false)]
    [InlineData(-0.5, false)]
    public void A_corpse_is_set_only_when_that_shortens_its_life(double? timeToRot, bool set)
    {
        Assert.Equal(set, CorpseRotRules.NeedsShortening(timeToRot, EmptyDecayTime));
        Assert.Equal(-1, CorpseRotRules.NeverRots);
    }

    [Fact]
    public void The_plan_counts_the_callers_corpses_and_picks_the_ones_to_set()
    {
        CorpseCandidate[] landblock =
        [
            Mine(timeToRot: 3600),                                                                    // 0: set
            new("Corpse of Holtburg Hunter", true, false, 3600),                                       // 1: another character
            Mine(timeToRot: 7200),                                                                    // 2: set
            new("Corpse of BSD Mage", false, false, 3600),                                             // 3: no Level: not a player corpse
            Mine(timeToRot: 15),                                                                      // 4: found, already at the empty-decay time
            new("Corpse of BSD Mage", true, true, 3600),                                               // 5: a monster's
            Mine(timeToRot: -2),                                                                      // 6: found, already decayed
            Mine(timeToRot: null),                                                                    // 7: set (unset)
            Mine(timeToRot: -1),                                                                      // 8: set (never rots)
            new("Corpse of Pyreal Thief", false, false, 900),                                          // 9: a creature
        ];

        var plan = CorpseRotRules.Plan(landblock, Caller, EmptyDecayTime);

        Assert.Equal(6, plan.Found);
        Assert.Equal([0, 2, 7, 8], plan.SetIndexes);
        Assert.Equal(4, plan.Rotted);
    }

    [Fact]
    public void Sixteen_old_corpses_of_a_recreated_character_are_all_found_and_set()
    {
        // The case the verb exists for: 16 corpses of earlier generations of BSD Mage (every generation shares the name), an hour or more left on each.
        var corpses = Enumerable.Range(0, 16).Select(i => Mine(timeToRot: 3600 + (i * 300))).Append(new CorpseCandidate("Corpse of Mosswart Scrapper", false, false, 900)).ToArray();

        var plan = CorpseRotRules.Plan(corpses, Caller, EmptyDecayTime);

        Assert.Equal((16, 16), (plan.Found, plan.Rotted));
        Assert.Equal(Enumerable.Range(0, 16), plan.SetIndexes);
    }

    [Fact]
    public void An_empty_landblock_or_one_without_the_callers_corpses_finds_nothing()
    {
        Assert.Equal((0, 0), Counts(CorpseRotRules.Plan([], Caller, EmptyDecayTime)));
        Assert.Equal((0, 0), Counts(CorpseRotRules.Plan([new CorpseCandidate("Corpse of Holtburg Hunter", true, false, 3600)], Caller, EmptyDecayTime)));
        Assert.Throws<ArgumentNullException>(() => CorpseRotRules.Plan(null!, Caller, EmptyDecayTime));
    }

    [Fact]
    public void Another_characters_corpses_are_never_set_whoever_asks()
    {
        CorpseCandidate[] landblock = [Mine("Corpse of Holtburg Hunter"), Mine("Corpse of Holtburg Brawler"), Mine("Corpse of BSD Mage")];

        Assert.Equal([2], CorpseRotRules.Plan(landblock, "BSD Mage", EmptyDecayTime).SetIndexes);
        Assert.Equal([0], CorpseRotRules.Plan(landblock, "Holtburg Hunter", EmptyDecayTime).SetIndexes);
        Assert.Equal([1], CorpseRotRules.Plan(landblock, "Holtburg Brawler", EmptyDecayTime).SetIndexes);
        Assert.Empty(CorpseRotRules.Plan(landblock, "Holtburg Archer", EmptyDecayTime).SetIndexes);
    }

    private static (int Found, int Rotted) Counts(CorpseRotPlan plan) => (plan.Found, plan.Rotted);
}

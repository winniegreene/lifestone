using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.PlayerRules;
using Lifestone.Data.Magic;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.PlayerRules;

/// <summary>
/// The player's rule for spells as a pure function (M6 item 15): the tab a spell belongs on, a spell on any bar is never added again, a spell on none is appended to its tab, a higher level takes its line's lower level's
/// place, a lower level goes after the higher one, and what cannot be added (a spell the book lacks, a book that has not arrived).
/// </summary>
public sealed class SpellBarRuleTests
{
    private const uint BoltI = 27, BoltII = 81, BoltIII = 82, StreakI = 1796, ArcI = 2739, HealI = 6, RecallId = 1635, PortalRecallId = 48, StrengthI = 2, ArmorI = 24, BaneI = 1486, VulnI = 21;

    private static readonly ImmutableDictionary<uint, SpellInfo> Table = new[]
    {
        Info(BoltI, "Flame Bolt I", MagicSchool.WarMagic, SpellType.Projectile, 121, 1, SpellFlags.Resistable | SpellFlags.Projectile),
        Info(BoltII, "Flame Bolt II", MagicSchool.WarMagic, SpellType.Projectile, 121, 50, SpellFlags.Resistable | SpellFlags.Projectile),
        Info(BoltIII, "Flame Bolt III", MagicSchool.WarMagic, SpellType.Projectile, 121, 100, SpellFlags.Resistable | SpellFlags.Projectile),
        Info(StreakI, "Flame Streak I", MagicSchool.WarMagic, SpellType.Projectile, 121, 1, SpellFlags.Resistable | SpellFlags.Projectile | SpellFlags.FastCast),
        Info(ArcI, "Flame Arc I", MagicSchool.WarMagic, SpellType.Projectile, 121, 1, SpellFlags.Resistable | SpellFlags.Projectile | SpellFlags.NonTrackingProjectile),
        Info(HealI, "Heal Self I", MagicSchool.LifeMagic, SpellType.Boost, 67, 1, SpellFlags.SelfTargeted | SpellFlags.Beneficial),
        Info(5, "Heal Other I", MagicSchool.LifeMagic, SpellType.Boost, 67, 1, SpellFlags.Beneficial),
        Info(1237, "Drain Health Other I", MagicSchool.LifeMagic, SpellType.Transfer, 70, 38, SpellFlags.Resistable),
        Info(RecallId, "Lifestone Recall", MagicSchool.ItemEnchantment, SpellType.PortalRecall, 201, 100, SpellFlags.SelfTargeted | SpellFlags.Beneficial),
        Info(PortalRecallId, "Primary Portal Recall", MagicSchool.ItemEnchantment, SpellType.PortalRecall, 201, 150, SpellFlags.SelfTargeted | SpellFlags.Beneficial), // the table's category is the Lifestone Recall's
        Info(StrengthI, "Strength Self I", MagicSchool.CreatureEnchantment, SpellType.Enchantment, 1, 1, SpellFlags.SelfTargeted | SpellFlags.Beneficial),
        Info(1, "Strength Other I", MagicSchool.CreatureEnchantment, SpellType.Enchantment, 1, 1, SpellFlags.Beneficial),
        Info(3, "Weakness Other I", MagicSchool.CreatureEnchantment, SpellType.Enchantment, 2, 1, SpellFlags.Resistable),
        Info(ArmorI, "Armor Self I", MagicSchool.LifeMagic, SpellType.Enchantment, 115, 1, SpellFlags.SelfTargeted | SpellFlags.Beneficial),
        Info(BaneI, "Blade Bane I", MagicSchool.ItemEnchantment, SpellType.Enchantment, 160, 1, SpellFlags.Beneficial),
        Info(VulnI, "Fire Vulnerability Other I", MagicSchool.LifeMagic, SpellType.Enchantment, 110, 25, SpellFlags.Resistable),
        Info(5356, "Nether Bolt I", MagicSchool.VoidMagic, SpellType.Projectile, 600, 1, SpellFlags.Resistable | SpellFlags.Projectile),
    }.ToImmutableDictionary(s => s.Id);

    private static SpellInfo Info(uint id, string name, MagicSchool school, SpellType meta, uint category, uint power, SpellFlags flags) =>
        WorldActionRig.Spell(id, meta, flags) with { Name = name, School = school, Category = category, Power = power };

    private static SpellInfo? Lookup(uint id) => Table.GetValueOrDefault(id);

    private static Spellbook Book(IEnumerable<uint> known, params uint[][] bars)
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        var all = ImmutableArray.CreateBuilder<ImmutableArray<uint>>();
        for (var b = 0; b < 8; b++)
        {
            all.Add(b < bars.Length ? [.. bars[b]] : []);
        }

        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Spells = known.ToImmutableDictionary(id => id, _ => 2f), SpellBars = all.ToImmutable() });
        return h.State.Self.Spellbook;
    }

    [Fact]
    public void M6_14_own_buffs_a_Focus_Self_VI_she_has_learned_goes_on_the_creature_tab_before_its_first_cast()
    {
        // The every-spell rule buys her own Focus Self while the bot casts it (the owner, 2026-11-17); the autopilot casts only from a bar, so the learned level must find its place: by the profile with no SpellTable.
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var (iv, vi) = (focus.Spells[3].Id, focus.Spells[5].Id);

        var first = SpellBarRule.Place(Book([vi]), vi, null);
        Assert.Equal((SpellBarVerdict.Add, SpellBarRule.CreatureTab, 0, 0u), (first.Verdict, first.Bar, first.Index, first.Replaces)); // no level on a bar: appended to tab I

        var over = SpellBarRule.Place(Book([iv, vi], new uint[] { StrengthI, iv }), vi, null);
        Assert.Equal((SpellBarVerdict.Add, SpellBarRule.CreatureTab, 1, iv), (over.Verdict, over.Bar, over.Index, over.Replaces)); // her lower level's place

        Assert.Equal(SpellBarVerdict.OnBar, SpellBarRule.Place(Book([vi], new uint[] { vi }), vi, null).Verdict); // once there, nothing more
        Assert.Equal(SpellBarVerdict.NotKnown, SpellBarRule.Place(Book([iv], new uint[] { iv }), vi, null).Verdict); // not learned yet: no place
    }

    [Theory]
    [InlineData(StrengthI, SpellBarRule.CreatureTab)] // her own creature buff
    [InlineData(1u, SpellBarRule.CreatureTab)] // a creature buff for another: with the school's buffs
    [InlineData(3u, SpellBarRule.FightTab)] // a creature debuff: cast on the enemy
    [InlineData(BoltI, SpellBarRule.FightTab)]
    [InlineData(5356u, SpellBarRule.FightTab)] // void
    [InlineData(VulnI, SpellBarRule.FightTab)] // the opener is a life enchantment that is not beneficial
    [InlineData(HealI, SpellBarRule.VitalsTab)]
    [InlineData(1237u, SpellBarRule.VitalsTab)] // a drain is a Transfer
    [InlineData(ArmorI, SpellBarRule.LifeTab)]
    [InlineData(BaneI, SpellBarRule.ItemTab)]
    [InlineData(RecallId, SpellBarRule.UtilityTab)] // a recall is Item Enchantment by school, but no enchantment
    public void A_spell_of_the_table_goes_on_a_tidy_players_tab(uint spell, int tab)
    {
        Assert.Equal(tab, SpellBarRule.TabOf(Table[spell]));
        Assert.Equal(tab, SpellBarRule.Describe(spell, Table[spell]).Tab);
    }

    [Fact]
    public void Without_a_table_the_mages_profile_gives_the_tab_and_the_line_and_an_unknown_spell_goes_on_the_utility_tab_by_number()
    {
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var one = SpellBarRule.Describe(focus.Spells[0].Id, null);
        var six = SpellBarRule.Describe(focus.Spells[5].Id, null);
        Assert.Equal((SpellBarRule.CreatureTab, true, focus.Spells[0].Name), (one.Tab, one.Described, one.Name));
        Assert.Equal(one.Line, six.Line);
        Assert.True(six.Power > one.Power);

        Assert.Equal(SpellBarRule.FightTab, SpellBarRule.Describe(MageSpellBarsIds.FlameBoltVii, null).Tab);
        Assert.Equal(SpellBarRule.FightTab, SpellBarRule.Describe(MageSpellBarsIds.FireVulnerabilityOtherVii, null).Tab);
        Assert.Equal(SpellBarRule.VitalsTab, SpellBarRule.Describe(MageSpellBarsIds.HealSelfVii, null).Tab);
        Assert.Equal(SpellBarRule.VitalsTab, SpellBarRule.Describe(MageSpellBarsIds.StaminaToManaSelfVii, null).Tab);
        Assert.Equal(SpellBarRule.UtilityTab, SpellBarRule.Describe(RecallId, null).Tab);
        Assert.Equal(SpellBarRule.LifeTab, SpellBarRule.Describe(ArenaProfile.LineNamed("Armor Self")!.Spells[0].Id, null).Tab);
        Assert.Equal(SpellBarRule.ItemTab, SpellBarRule.Describe(ArenaProfile.LinesOf(ProfileRole.ItemBuffArmor).First().Spells[0].Id, null).Tab);

        var unknown = SpellBarRule.Describe(60001, null);
        Assert.Equal((SpellBarRule.UtilityTab, false, "spell 60001", null), (unknown.Tab, unknown.Described, unknown.Name, unknown.Line));
    }

    [Fact]
    public void A_spell_on_no_bar_is_appended_to_its_tab()
    {
        var book = Book([BoltI, HealI, StrengthI], [], [VulnI], []);
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.FightTab, 1, 0, 0), SpellBarRule.Place(book, BoltI, Lookup)); // after the one spell tab II holds
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.VitalsTab, 0, 0, 0), SpellBarRule.Place(book, HealI, Lookup));
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.CreatureTab, 0, 0, 0), SpellBarRule.Place(book, StrengthI, Lookup));
    }

    [Fact]
    public void A_spell_on_any_bar_stays_where_it_is_and_is_never_added_again()
    {
        // Heal Self on tab II (the fight tab of the earlier bars) and a bolt on tab VIII (the person's own): both are on a bar, whatever the tab the rule would have chosen.
        var book = Book([BoltI, HealI], [], [HealI], [], [], [], [], [], [BoltI]);
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.OnBar, 1, 0, 0, 0), SpellBarRule.Place(book, HealI, Lookup));
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.OnBar, 7, 0, 0, 0), SpellBarRule.Place(book, BoltI, Lookup));
        Assert.True(SpellBarRule.TryFind(book, BoltI, out var bar, out var index));
        Assert.Equal((7, 0), (bar, index));
    }

    [Fact]
    public void A_higher_level_takes_its_lines_lower_levels_place()
    {
        var book = Book([BoltI, BoltII, BoltIII, StreakI, VulnI], [], [VulnI, BoltI, StreakI]);
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.FightTab, 1, BoltI, 0), SpellBarRule.Place(book, BoltII, Lookup));
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.FightTab, 1, BoltI, 0), SpellBarRule.Place(book, BoltIII, Lookup));
    }

    [Fact]
    public void A_lower_level_goes_right_after_the_lines_higher_level_which_stays()
    {
        var book = Book([BoltI, BoltIII, VulnI], [], [BoltIII, VulnI]);
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.FightTab, 1, 0, BoltIII), SpellBarRule.Place(book, BoltI, Lookup));
    }

    [Fact]
    public void A_line_is_replaced_where_it_lives_even_on_another_tab()
    {
        // The bolt's level I sits on tab III (someone put it there): level II replaces it there, not on tab II.
        var book = Book([BoltI, BoltII], [], [], [HealI, BoltI]);
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, 2, 1, BoltI, 0), SpellBarRule.Place(book, BoltII, Lookup));
    }

    [Fact]
    public void A_streak_an_arc_and_a_spell_for_another_target_are_not_the_bolts_or_the_self_spells_line()
    {
        var book = Book([BoltII, StreakI, ArcI, HealI, 5], [], [StreakI, ArcI], [5]);
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.FightTab, 2, 0, 0), SpellBarRule.Place(book, BoltII, Lookup)); // appended: neither the streak nor the arc is replaced
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.VitalsTab, 1, 0, 0), SpellBarRule.Place(book, HealI, Lookup)); // Heal Other is another line
        Assert.NotEqual(SpellBarRule.Describe(BoltI, Table[BoltI]).Line, SpellBarRule.Describe(StreakI, Table[StreakI]).Line);
        Assert.NotEqual(SpellBarRule.Describe(BoltI, Table[BoltI]).Line, SpellBarRule.Describe(ArcI, Table[ArcI]).Line);
        Assert.Equal(SpellBarRule.Describe(BoltI, Table[BoltI]).Line, SpellBarRule.Describe(BoltIII, Table[BoltIII]).Line);
    }

    [Fact]
    public void A_recall_never_takes_another_recalls_place_though_they_share_the_tables_category()
    {
        Assert.Null(SpellBarRule.Describe(RecallId, Table[RecallId]).Line); // a recall, a tie, a portal spell and a dispel have no line
        Assert.Null(SpellBarRule.Describe(PortalRecallId, Table[PortalRecallId]).Line);
        var book = Book([RecallId, PortalRecallId], [], [], [], [], [], [RecallId]);
        Assert.Equal(new SpellBarPlacement(SpellBarVerdict.Add, SpellBarRule.UtilityTab, 1, 0, 0), SpellBarRule.Place(book, PortalRecallId, Lookup)); // appended beside it, whatever its power
    }

    [Fact]
    public void A_spell_the_book_lacks_cannot_be_added_and_a_book_that_has_not_arrived_is_not_judged()
    {
        var book = Book([BoltI]);
        Assert.Equal(SpellBarVerdict.NotKnown, SpellBarRule.Place(book, BoltII, Lookup).Verdict);
        Assert.Equal(SpellBarVerdict.NotKnown, SpellBarRule.Place(book, 0, Lookup).Verdict);

        var unread = new ReducerHarness().State.Self.Spellbook;
        Assert.Equal(SpellBarVerdict.BookUnread, SpellBarRule.Place(unread, BoltI, Lookup).Verdict);
    }

    [Fact]
    public void The_layout_is_the_same_whatever_the_order_of_the_casts_and_the_rule_never_adds_to_the_last_two_tabs()
    {
        // Every spell of the table, added one at a time by the rule as ACE keeps a bar (an add inserts at the index unless the spell is there; a remove closes up), in two different orders of casting.
        static List<List<uint>> Play(IEnumerable<uint> order)
        {
            var bars = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToList();
            foreach (var spell in order)
            {
                var book = Book(Table.Keys, [.. bars.Select(b => b.ToArray())]);
                var place = SpellBarRule.Place(book, spell, Lookup);
                Assert.Equal(SpellBarVerdict.Add, place.Verdict);
                if (place.Replaces != 0)
                {
                    Assert.True(bars[place.Bar].Remove(place.Replaces));
                }

                bars[place.Bar].Insert(Math.Min(place.Index, bars[place.Bar].Count), spell);
                Assert.Equal(SpellBarVerdict.OnBar, SpellBarRule.Place(Book(Table.Keys, [.. bars.Select(b => b.ToArray())]), spell, Lookup).Verdict);
            }

            return bars;
        }

        var rising = Play(Table.Keys.Order());
        var falling = Play(Table.Keys.OrderDescending());
        uint[] bolts = [BoltI, BoltII, BoltIII];
        for (var b = 0; b < 8; b++)
        {
            Assert.Equal(rising[b].Except(bolts).Order(), falling[b].Except(bolts).Order()); // the same spells on the same tabs; only the bolt line's levels differ (below)
        }

        Assert.Empty(rising[6]);
        Assert.Empty(rising[7]);
        Assert.Equal<uint>([BoltIII], rising[SpellBarRule.FightTab].Intersect([BoltI, BoltII, BoltIII])); // rising: each level took the one before's place
        Assert.Equal<uint>([BoltIII, BoltII, BoltI], falling[SpellBarRule.FightTab].Where(id => id is BoltI or BoltII or BoltIII)); // falling: each lower level went after the higher
        Assert.All(rising, bar => Assert.Equal(bar.Count, bar.Distinct().Count()));
        Assert.All(falling, bar => Assert.Equal(bar.Count, bar.Distinct().Count()));
    }

    [Fact]
    public void The_tabs_are_named_for_the_log()
    {
        Assert.Equal("tab I (creature buffs)", SpellBarRule.TabLabel(0));
        Assert.Equal("tab II (the fight)", SpellBarRule.TabLabel(SpellBarRule.FightTab));
        Assert.Equal("tab VI (travel and utility)", SpellBarRule.TabLabel(SpellBarRule.UtilityTab));
        Assert.Equal("tab VIII", SpellBarRule.TabLabel(7));
    }

    private static class MageSpellBarsIds
    {
        public const uint FlameBoltVii = Lifestone.Client.Session.Scenarios.MageSpellBars.FlameBoltVii;
        public const uint FireVulnerabilityOtherVii = Lifestone.Client.Session.Scenarios.MageSpellBars.FireVulnerabilityOtherVii;
        public const uint HealSelfVii = Lifestone.Client.Session.Scenarios.MageSpellBars.HealSelfVii;
        public const uint StaminaToManaSelfVii = Lifestone.Client.Session.Scenarios.MageSpellBars.StaminaToManaSelfVii;
    }
}

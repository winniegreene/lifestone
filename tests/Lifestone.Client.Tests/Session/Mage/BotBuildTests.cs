using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14, slice S3: the build that leans on the buff bot. What these prove: the climber's skill order is the option's (the bot on: Life Magic, Item Enchantment, Mana Conversion, Creature Enchantment; off: the order of before), in the
/// creation's plan and in the credit order, with the levels each skill is trained at (the synthetic table here, the retail XpTable with the dats); the lines the bot covers go by both their names; while the bot covers them the learn plan
/// buys none (each answers <see cref="LearnNeedKind.BotCovers"/>), saves for none of the first scrolls and reads her item lines on the skill the bot's lines give; the keep rule keeps no scroll of a covered line; the experience push, which asks
/// nothing of a book without a bootstrap, pushes Item Enchantment with the bot's lines in the bootstrap's place and never Creature Enchantment; the foci plan does not wait for a scroll the bot casts. With the cover off every answer is the one of
/// before. That experience is never spent on a skill held untrained is <c>XpSpendRuleTests</c>' (the climber at level 10: "untrained: never raised").
/// </summary>
public sealed class BotBuildTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Life = MageTemplate.LifeMagic, Creature = MageTemplate.CreatureEnchantment, Item = MageTemplate.ItemEnchantment, Conversion = MageTemplate.ManaConversion, Alchemy = MageTemplate.Alchemy;
    private const string Burrow = "tusker-burrow";
    private const string Outskirts = "holtburg-outskirts";

    private static readonly Dictionary<uint, int> TrainedCost = new()
    {
        [MageTemplate.WarMagic] = 14, [Life] = 12, [Creature] = 8, [Item] = 8, [Conversion] = 6, [Alchemy] = 6, // M6 14: Alchemy, the order's last line
    };

    private static readonly Dictionary<uint, int> SpecializedCost = new()
    {
        [MageTemplate.WarMagic] = 28, [Life] = 20, [Creature] = 16, [Item] = 16, [Conversion] = 12,
    };

    private static Ladder L => LadderLoader.WarMage;

    private static ScrivenerCatalog C => ScrivenerCatalog.Default;

    private static MageTemplate.ClimberCreationPlan Plan(bool buffBot, int credits = 52) => MageTemplate.PlanClimberCreation(credits, id => TrainedCost[id], id => SpecializedCost[id], buffBot);

    /// <summary>The climber the bot's build gives: Focus and Self 100, War Magic and Life Magic specialised, Item Enchantment trained with <paramref name="itemRanks"/> ranks (or not at all), Creature Enchantment as <paramref name="creature"/> says.</summary>
    private static PlayerQualities Winnie(int level, bool item = true, bool creature = false, int itemRanks = 0)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 0, 1, 3u, 0, 10u, 0, 0);
        q.Skills[Life] = new SkillDesc(Life, 0, 1, 3u, 0, 10u, 0, 0);
        q.Skills[Creature] = new SkillDesc(Creature, 0, 1, creature ? 2u : 1u, 0, 0u, 0, 0);
        q.Skills[Item] = new SkillDesc(Item, (ushort)itemRanks, 1, item ? 2u : 1u, 0, 0u, 0, 0);
        return q;
    }

    /// <summary>The profile these tests' cover is: the tuskers' (the plans take the profile that covers, or null for none; <c>all</c>, the default, has its own tests below).</summary>
    private static BuffBotProfile Tusker => BuffBotWire.Tusker;

    private static IReadOnlyList<LearnNeed> Needs(PlayerQualities q, bool botCovers, string ground = Burrow, Spellbook? book = null, BuffBotProfile? profile = null) =>
        ScrivenerLearnPlan.Needs(GroundCatalog.Get(ground), ScrivenerLearnPlan.RungOf(L, ground, q.Level)!, L.PlanCastChance, q, book ?? new Spellbook { IsKnown = true }, C, L, botCovers ? profile ?? Tusker : null);

    // ------------------------------------------------------------------------------------------------ the order

    [Fact]
    public void The_order_is_the_options_Item_and_Mana_Conversion_before_Creature_with_the_bot_and_the_order_of_before_without()
    {
        Assert.Equal([Life, Item, Conversion, Creature], MageTemplate.ClimberOrder(true));
        Assert.Equal([Life, Creature, Item, Conversion], MageTemplate.ClimberOrder(false));
        Assert.Same(MageTemplate.ClimberCreationOrder, MageTemplate.ClimberOrder(false));

        // The Aluvian 52 credits pay War Magic and Life Magic specialised in both orders and leave 4: the creation is the same character; what differs is what the levels' credits train next.
        var (on, off) = (Plan(true), Plan(false));
        Assert.Equal(off.Paid.Select(p => (p.Skill, p.Class, p.Cost)), on.Paid.Select(p => (p.Skill, p.Class, p.Cost)));
        Assert.Equal([(MageTemplate.WarMagic, HeadlessCharacter.SpecializedSkill, 28), (Life, HeadlessCharacter.SpecializedSkill, 20)], on.Paid.Select(p => (p.Skill, p.Class, p.Cost)));
        Assert.Equal((48, 4), (on.Spent, on.Left));
        Assert.Equal([Item, Conversion, Creature], on.Unpaid.Select(u => u.Skill));
        Assert.Equal([Creature, Item, Conversion], off.Unpaid.Select(u => u.Skill));
        Assert.Equal([new CreditLine(Item, 8), new CreditLine(Conversion, 6), new CreditLine(Creature, 8)], SkillCreditPlan.OrderOf(on)); // M6 14 D136: Alchemy only on a need
        Assert.Equal([new CreditLine(Creature, 8), new CreditLine(Item, 8), new CreditLine(Conversion, 6)], SkillCreditPlan.OrderOf(off));
        Assert.Equal([new CreditLine(Item, 8), new CreditLine(Conversion, 6), new CreditLine(Creature, 8), new CreditLine(Alchemy, 6)], SkillCreditPlan.OrderOf(on, id => id == Alchemy)); // a need: last
        Assert.Equal(Plan(false).Unpaid, MageTemplate.PlanClimberCreation(52, id => TrainedCost[id], id => SpecializedCost[id]).Unpaid); // the default is the order of before

        // A heritage with 62 credits has 14 after the two specialisations: the bot's order buys Item Enchantment and Mana Conversion at creation, the other Creature Enchantment and Mana Conversion.
        var (richOn, richOff) = (Plan(true, 62), Plan(false, 62));
        Assert.Equal((HeadlessCharacter.TrainedSkill, HeadlessCharacter.TrainedSkill, (uint?)null), (richOn.ClassOf(Item), richOn.ClassOf(Conversion), richOn.ClassOf(Creature)));
        Assert.Equal(((uint?)null, HeadlessCharacter.TrainedSkill, HeadlessCharacter.TrainedSkill), (richOff.ClassOf(Item), richOff.ClassOf(Conversion), richOff.ClassOf(Creature)));
        Assert.Equal([new CreditLine(Creature, 8)], SkillCreditPlan.OrderOf(richOn));
    }

    [Fact]
    public void Over_one_credit_a_level_the_bots_order_trains_Item_at_5_Mana_Conversion_at_11_and_Creature_at_19_and_a_trained_skill_is_skipped()
    {
        var table = ScriptedMage.SyntheticTable(); // one credit a level from level 2
        var order = SkillCreditPlan.OrderOf(Plan(true));
        Assert.Equal([(Item, 5, 0), (Conversion, 11, 0), (Creature, 19, 0)], SkillCreditPlan.Schedule(table, order, 4, 30)); // M6 14 D136: the credits bank after the order
        Assert.Equal([(Creature, 5, 0), (Item, 13, 0), (Conversion, 19, 0)], SkillCreditPlan.Schedule(table, SkillCreditPlan.OrderOf(Plan(false)), 4, 30));

        // The order is kept: 6 credits would pay Mana Conversion, but Item Enchantment comes first.
        var wait = SkillCreditPlan.Next(order, 6, _ => false);
        Assert.Equal((CreditStepKind.Wait, (uint?)Item, 2), (wait.Kind, wait.Skill, wait.Short));

        // A character keeps what she trained: a climber of the old order (Creature Enchantment trained) played with the bot on trains Item Enchantment next, then Mana Conversion, and is then done.
        Assert.Equal((CreditStepKind.Train, (uint?)Item), (SkillCreditPlan.Next(order, 8, id => id == Creature).Kind, SkillCreditPlan.Next(order, 8, id => id == Creature).Skill));
        Assert.Equal((CreditStepKind.Train, (uint?)Conversion), (SkillCreditPlan.Next(order, 6, id => id is Creature or Item).Kind, SkillCreditPlan.Next(order, 6, id => id is Creature or Item).Skill));
        Assert.Equal(CreditStepKind.Done, SkillCreditPlan.Next(order, 9, _ => true).Kind);
    }

    [Fact]
    public void The_one_line_says_which_order_is_in_force_and_why()
    {
        var on = MageTemplate.ClimberOrderLine(true);
        Assert.StartsWith("skill order: bot: War Magic, Life Magic, Item Enchantment, Mana Conversion, Creature Enchantment, then the credits bank (Alchemy on a need only): --autopilot-buff-bot is on (the default): ", on, StringComparison.Ordinal);
        Assert.Contains("Creature Enchantment, which comes after them; ", on, StringComparison.Ordinal);
        Assert.EndsWith("; Alchemy is trained from the banked credits only when a job needs it (M6 14 D136, amending D132; the creation trains none)", on, StringComparison.Ordinal);

        var off = MageTemplate.ClimberOrderLine(false);
        Assert.StartsWith("skill order: own: War Magic, Life Magic, Creature Enchantment, Item Enchantment, Mana Conversion, then the credits bank (Alchemy on a need only): --autopilot-buff-bot=off: ", off, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void With_the_dats_the_bots_order_trains_Item_Enchantment_at_5_Mana_Conversion_at_12_and_Creature_Enchantment_at_32()
    {
        var table = XpTableData.Load(fixture.Dats);
        var on = ClimberCreateScenario.PlanFromDats(fixture.Dats, buffBot: true);
        var off = ClimberCreateScenario.PlanFromDats(fixture.Dats, buffBot: false);
        string Text(MageTemplate.ClimberCreationPlan plan) =>
            $"created: {string.Join(", ", plan.Paid.Select(p => $"{MageTemplate.SkillName(p.Skill)} {(p.Class == HeadlessCharacter.SpecializedSkill ? "specialised" : "trained")} {p.Cost}"))}; {plan.Left} of {plan.Credits} left; then "
            + string.Join(", ", SkillCreditPlan.Schedule(table, SkillCreditPlan.OrderOf(plan), plan.Left, 60).Select(s => $"{MageTemplate.SkillName(s.Skill)} at level {s.Level}"));
        output.WriteLine("bot on:  " + Text(on));
        output.WriteLine("bot off: " + Text(off));

        // The creation is the same in both: War Magic 28 and Life Magic 20 specialised, 4 of 52 left; the SkillTable's costs 8, 6, 8.
        Assert.Equal(off.Paid.Select(p => (p.Skill, p.Class, p.Cost)), on.Paid.Select(p => (p.Skill, p.Class, p.Cost)));
        Assert.Equal((48, 4), (on.Spent, on.Left));
        Assert.Equal([new CreditLine(Item, 8), new CreditLine(Conversion, 6), new CreditLine(Creature, 8)], SkillCreditPlan.OrderOf(on)); // M6 14 D136: Alchemy only on a need

        // The XpTable grants one credit at each level 2 to 10, then at 12, 14, 16, 18, 20, 23, 26, 29, 32 (SkillCreditTests): 8 at level 5, 6 more at 12, 8 more at 32.
        Assert.Equal([(Item, 5, 0), (Conversion, 12, 0), (Creature, 32, 0)], SkillCreditPlan.Schedule(table, SkillCreditPlan.OrderOf(on), on.Left, 60)); // M6 14 D136: 35 to 60's credits bank
        Assert.Equal([(Item, 5, 0), (Conversion, 12, 0), (Creature, 32, 0), (Alchemy, 60, 0)], SkillCreditPlan.Schedule(table, SkillCreditPlan.OrderOf(on, id => id == Alchemy), on.Left, 60)); // a need from 32: 35 to 60 pay its 6
        Assert.Equal([(Creature, 5, 0), (Item, 16, 0), (Conversion, 32, 0)], SkillCreditPlan.Schedule(table, SkillCreditPlan.OrderOf(off), off.Left, 60));
    }

    // ------------------------------------------------------------------------------------------------ what the bot covers

    [Fact]
    public void The_cover_names_each_of_the_bots_lines_by_the_profiles_name_and_the_catalogs_and_no_line_of_her_own()
    {
        Assert.All(BuffBotWire.Tusker.Lines, l => Assert.True(BuffBotCover.Covers(Tusker, l.Line), l.Line));

        // Every line but Run and Jump is one of her own profile's, so the catalog's name is found. She holds no Sprint or Jumping Mastery line and the catalog sells none: each is covered by its one name and adds nothing to buy.
        string[] notHers = ["Sprint Self", "Jumping Mastery Self"];
        Assert.Equal(notHers, BuffBotWire.Tusker.Lines.Where(l => BuffProfiles.Climber.LineNamed(l.Line) is null).Select(l => l.Line));
        Assert.All(notHers, n => Assert.True(BuffBotCover.Covers(Tusker, n) && C.Line(n) is null && !L.FirstScrolls.Any(g => g.Line == n), n));
        Assert.True(BuffBotCover.Covers(Tusker, "Bludgeon Protection Self") && BuffBotCover.Covers(Tusker, "Bludgeoning Protection Self")); // the profile's name and the catalog's
        Assert.True(BuffBotCover.Covers(Tusker, "Mana Mastery Self") && BuffBotCover.Covers(Tusker, "Mana Conversion Mastery Self"));
        Assert.Equal(BuffBotWire.Tusker.Lines.Count + 2, BuffBotCover.LinesOf(Tusker).Count); // a name a line, two of them with a second name

        // Every buff line the catalog sells that is not an item or a travel line is the bot's; her armour lines, her recall, her bolts and what she fights with are her own.
        var hers = C.Lines.Where(l => !BuffBotCover.Covers(Tusker, l.Name)).Select(l => l.Kind).Distinct().ToList();
        Assert.DoesNotContain(ScrivenerLineKind.SelfBuff, hers);
        Assert.DoesNotContain(ScrivenerLineKind.Armor, hers);
        Assert.DoesNotContain(ScrivenerLineKind.Protection, hers);
        Assert.DoesNotContain(ScrivenerLineKind.Recovery, hers);
        Assert.All(new[] { "Impenetrability", "Bludgeon Bane", "Lifestone Recall", "Lifestone Tie", "Flame Bolt", "Stamina to Mana Self", "Heal Self", "Imperil Other" }, n => Assert.False(BuffBotCover.Covers(Tusker, n), n));

        // The boost the plans count for a level VII line is the spell table's: the Self spell of a line is one id over the bot's Other spell.
        foreach (var name in new[] { "Focus Self", "Willpower Self", "Item Enchantment Mastery Self", "Creature Enchantment Mastery Self" })
        {
            var self = BuffBotWire.Tusker.Lines.Single(l => l.Line == name).SpellId + 1;
            Assert.Equal(BuffBotCover.Boost, BuffStatMods.BySpell[self].Value);
        }
    }

    [Fact]
    public void The_cover_follows_the_profile_all_covers_its_nine_more_lines_and_the_tuskers_leaves_them_hers()
    {
        // The owner, 2026-11-12: she says the word all. The cover is the lines of the profile asked for: every line of the tuskers' in both, the nine more in 'all' alone.
        var all = BuffBotWire.All;
        Assert.All(Tusker.Lines, l => Assert.True(BuffBotCover.Covers(all, l.Line), l.Line));
        string[] more = ["Blade Protection Self", "Piercing Protection Self", "Fire Protection Self", "Cold Protection Self", "Acid Protection Self", "Lightning Protection Self", "Magic Resistance Self", "Impregnability Self", "Arcane Enlightenment Self"];
        Assert.Equal(more, all.Lines.Skip(Tusker.Lines.Count).Select(l => l.Line));
        Assert.All(more, n => Assert.True(BuffBotCover.Covers(all, n) && !BuffBotCover.Covers(Tusker, n), n));
        Assert.Equal(BuffBotCover.LinesOf(Tusker).Count + more.Length, BuffBotCover.LinesOf(all).Count); // a name a line: the dat's root of each of the nine is its name
        Assert.True(BuffBotCover.LinesOf(Tusker).IsSubsetOf(BuffBotCover.LinesOf(all)));
        Assert.Same(BuffBotCover.LinesOf(all), BuffBotCover.LinesOf(all)); // made once a profile
        Assert.All(new[] { "Impenetrability", "Bludgeon Bane", "Lifestone Recall", "Flame Bolt", "Deception Mastery Self" }, n => Assert.False(BuffBotCover.Covers(all, n), n)); // her armour lines stay her own in every profile

        // What she learns and buys does not change with the word: the catalog sells none of the nine (one protection line, the tuskers'), and her plan's lines are the same under both covers.
        Assert.All(more, n => Assert.True(C.Line(n) is null && !L.FirstScrolls.Any(g => g.Line == n) && !L.Gates.SelectMany(g => g.Lines).Any(g => g.Line == n), n));
        var q = Winnie(16);
        Assert.Equal(Needs(q, botCovers: true).Select(n => (n.Line.Name, n.Kind, n.Target)), Needs(q, botCovers: true, profile: all).Select(n => (n.Line.Name, n.Kind, n.Target)));
        Assert.All(Needs(q, botCovers: true, profile: all).Where(n => n.Kind == LearnNeedKind.BotCovers), n => Assert.Contains("the profile 'all'", n.Why, StringComparison.Ordinal));
        Assert.All(Needs(q, botCovers: true).Where(n => n.Kind == LearnNeedKind.BotCovers), n => Assert.Contains("the profile 'tusker'", n.Why, StringComparison.Ordinal));

        // The three skill lines of 'all' are lines of her own profile: under its cover her standing boost of one is the bot's, under the tuskers' it is her own book's (none here).
        static bool None(string line, int level) => false;
        Assert.All(new[] { "Magic Resistance Self", "Impregnability Self", "Arcane Enlightenment Self" }, n => Assert.NotNull(BuffProfiles.Climber.LineNamed(n)));
        Assert.Equal((BuffBotCover.Boost, 0, 0), (BuffReach.StandingBoost(null, L.PlanBuffChance, "Magic Resistance Self", None, all), BuffReach.StandingBoost(null, L.PlanBuffChance, "Magic Resistance Self", None, Tusker), BuffReach.StandingBoost(null, L.PlanBuffChance, "Magic Resistance Self", None)));
        Assert.Equal((BuffBotCover.Boost, BuffBotCover.Boost), (BuffReach.StandingBoost(null, L.PlanBuffChance, "Endurance Self", None, all), BuffReach.StandingBoost(null, L.PlanBuffChance, "Endurance Self", None, Tusker)));

        // A session's cover carries the profile the run asks for: the default until the autopilot says another, and a change of profile alone is a change.
        var cover = BuffBotCover.For(new GameState());
        Assert.Same(BuffBotWire.Default, cover.Profile);
        Assert.Null(cover.Covering); // off: the plans are asked with no cover
        Assert.True(cover.Set(true, "applies"));
        Assert.Same(all, cover.Covering);
        Assert.False(cover.Set(true, "applies", all));
        Assert.True(cover.Set(true, "applies", Tusker));
        Assert.Same(Tusker, cover.Covering);
        Assert.True(cover.Set(false, "off", Tusker));
        Assert.Null(cover.Covering);
        Assert.Same(Tusker, cover.Profile);
    }

    [Fact]
    public void A_sessions_cover_is_off_until_it_is_set_says_each_change_once_and_names_a_line_once_until_it_changes()
    {
        var cover = BuffBotCover.For(new GameState());
        Assert.False(cover.On);
        Assert.False(cover.IsSet);
        Assert.True(cover.Set(false, "no rung")); // the first word is a change, even to off
        Assert.False(cover.Set(false, "no rung"));
        Assert.True(cover.Set(true, "applies"));
        Assert.Equal((true, "applies"), (cover.On, cover.Why));
        Assert.True(cover.FirstNote("Armor Self"));
        Assert.False(cover.FirstNote("Armor Self"));
        Assert.False(cover.Set(true, "applies still"));
        Assert.False(cover.FirstNote("Armor Self"));
        Assert.True(cover.Set(false, "the bot has not answered"));
        Assert.True(cover.FirstNote("Armor Self")); // named again after a change
        Assert.NotSame(cover, BuffBotCover.For(new GameState())); // one a session
    }

    // ------------------------------------------------------------------------------------------------ the learn plan

    [Fact]
    public void While_the_bot_covers_them_no_scroll_of_its_lines_is_due_and_her_item_lines_are_read_on_the_skill_its_lines_give()
    {
        var q = Winnie(16);
        var on = Needs(q, botCovers: true);
        var off = Needs(q, botCovers: false);

        // The Burrow's plan holds every line the bot casts that is one of her own (its targets deal bludgeon damage, so the protection line too; Run and Jump are no lines of hers, so the plan has none to name): each is named, none is due, none has a scroll.
        var covered = on.Where(n => n.Kind == LearnNeedKind.BotCovers).ToList();
        Assert.Equal(17, BuffBotWire.Tusker.Lines.Count(l => BuffProfiles.Climber.LineNamed(l.Line) is not null));
        Assert.Equal(17, covered.Count);
        Assert.DoesNotContain(on.Concat(off), n => n.Line.Name is "Sprint Self" or "Jumping Mastery Self");
        Assert.All(covered, n => Assert.True(BuffBotCover.Covers(Tusker, n.Line.Name) && n.Spell is null && n.Target == 0));
        Assert.All(covered, n => Assert.Contains("the buff bot casts it at level VII", n.Why, StringComparison.Ordinal));
        Assert.DoesNotContain(on, n => n.Kind != LearnNeedKind.BotCovers && BuffBotCover.Covers(Tusker, n.Line.Name));
        Assert.DoesNotContain(on, n => n.Kind == LearnNeedKind.Saving); // nothing is saved for a first scroll that is never bought
        Assert.DoesNotContain(off, n => n.Kind == LearnNeedKind.BotCovers); // off: the plan of before

        // Off, the same character (Creature Enchantment untrained) is named untrained for the creature lines and still asked for her own Armor Self.
        Assert.Equal(LearnNeedKind.Untrained, off.First(n => n.Line.Name == "Focus Self").Kind);
        Assert.NotNull(off.First(n => n.Line.Name == "Armor Self").Spell);

        // Her own lines stay hers. Impenetrability on Item Enchantment 50: level I alone; with the bot's Focus, Willpower and mastery up it is 70 + 40 = 110, and II (power 50, the bar at 66) is the target.
        Assert.Equal(1, off.Single(n => n.Line.Name == "Impenetrability").Target);
        Assert.Equal(2, on.Single(n => n.Line.Name == "Impenetrability").Target);
        Assert.Equal(2, on.Single(n => n.Line.Name == "Bludgeon Bane").Target);
        Assert.Contains(on, n => n.Line.Kind == ScrivenerLineKind.Bolt);
        Assert.Contains(on, n => n.Line.Name == "Lifestone Recall");

        // The creature debuffs are not the bot's: with Creature Enchantment untrained they answer untrained, by name, in both.
        Assert.All(new[] { on, off }, list => Assert.Equal(LearnNeedKind.Untrained, list.Single(n => n.Line.Name == "Magic Yield Other").Kind));
    }

    [Fact]
    public void A_climber_of_the_old_order_saves_for_the_first_scrolls_until_the_bot_covers_them()
    {
        // BuffBootstrapPlanTests' climber of level 16 (Creature Enchantment 115, Life Magic 140, Item Enchantment 150) on the outskirts: every travel line waits for the first scrolls.
        var q = Winnie(16, creature: true, itemRanks: 100);
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 31, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 30, 100, 0);
        q.Skills[Creature] = new SkillDesc(Creature, 50, 1, 2u, 0, 0u, 0, 0);
        q.Skills[Life] = new SkillDesc(Life, 75, 1, 2u, 0, 0u, 0, 0);
        q.Skills[Item] = new SkillDesc(Item, 85, 1, 2u, 0, 0u, 0, 0);

        var off = Needs(q, botCovers: false, Outskirts);
        Assert.All(off.Where(n => n.Line.Kind == ScrivenerLineKind.Travel), n => Assert.Equal(LearnNeedKind.Saving, n.Kind));

        var on = Needs(q, botCovers: true, Outskirts);
        Assert.All(on.Where(n => n.Line.Kind == ScrivenerLineKind.Travel), n => Assert.Equal(LearnNeedKind.Due, n.Kind));
        Assert.DoesNotContain(on, n => n.Kind == LearnNeedKind.Saving);
        Assert.DoesNotContain(on, n => n.Kind == LearnNeedKind.Due && BuffBotCover.Covers(Tusker, n.Line.Name));
    }

    // ------------------------------------------------------------------------------------------------ the reach and the push

    [Fact]
    public void With_the_bot_the_reach_counts_its_lines_and_the_push_raises_Item_Enchantment_alone()
    {
        var q = Winnie(8);
        static bool None(string line, int level) => false;

        // Her own bootstrap: with Creature Enchantment untrained there is none, so the reach is her bare skills.
        var bare = BuffReach.Walk(q, L.PlanBuffChance, static (_, _) => true)!;
        Assert.Empty(bare.Steps);
        Assert.Equal((0, 0, 0, 50u), (bare.Focus, bare.Willpower, bare.CreatureMastery, bare.Item));

        // The bot's: Focus and Self 140 give 70, the mastery 40 more: Item Enchantment 110, Life Magic and War Magic 120 (specialised: 10 more).
        var bot = BuffReach.Walk(q, L.PlanBuffChance, None, bot: true)!;
        Assert.Empty(bot.Steps);
        Assert.Equal((BuffReach.BotLevel, BuffReach.BotLevel, BuffReach.BotLevel), (bot.Focus, bot.Willpower, bot.CreatureMastery));
        Assert.Equal((110u, 120u, 120u), (bot.Item, bot.Life, bot.War));

        // The push of before asks nothing of a book with no bootstrap (the silent no-op this slice found): her item lines would never be pushed.
        Assert.Empty(BuffReach.Push(q, L.PlanBuffChance, None));

        // With the bot: Impenetrability III (power 100) is under the bar 0.75 at Item Enchantment 116, IV (150) at 166: base 56 and 106, so 6 and 56 ranks over her 50. Creature Enchantment is never pushed.
        var pushes = BuffReach.Push(q, L.PlanBuffChance, None, bot: true);
        Assert.Equal([(Item, 6), (Item, 56)], pushes.Select(p => (p.Skill, p.Ranks)));
        Assert.Contains("Item Enchantment base 56 brings level III under the bar 0.75 with the buff bot's level VII lines up", pushes[0].Why, StringComparison.Ordinal);
        Assert.Contains("base 106 brings level IV", pushes[1].Why, StringComparison.Ordinal);
        Assert.Equal([(Item, 56)], BuffReach.Push(Winnie(8, itemRanks: 6), L.PlanBuffChance, None, bot: true).Select(p => (p.Skill, p.Ranks))); // III is reached: IV alone
        Assert.Empty(BuffReach.Push(Winnie(8, itemRanks: 56), L.PlanBuffChance, None, bot: true));
        Assert.DoesNotContain(BuffReach.Push(Winnie(40, creature: true), L.PlanBuffChance, None, bot: true), p => p.Skill == Creature); // trained at 32: still the bot's lines, never pushed
        Assert.Empty(BuffReach.Push(Winnie(4, item: false), L.PlanBuffChance, None, bot: true)); // Item Enchantment untrained (levels 1 to 4): nothing is asked, as before
    }

    // ------------------------------------------------------------------------------------------------ the keep rule and the foci

    [Fact]
    public void A_found_scroll_of_a_line_the_bot_covers_is_not_kept_and_is_kept_as_before_when_it_does_not()
    {
        var rung = ScrivenerLearnPlan.RungOf(L, Burrow, 16)!;
        var q = Winnie(16);
        var book = new Spellbook { IsKnown = true };
        var armor = C.LineSpells("Armor Self").Single(s => s.Level == ScrivenerLearnPlan.TargetLevel(60, L.PlanCastChance, [.. C.LineSpells("Armor Self").Select(s => s.Power)], 0));
        var scroll = ScrollKeepRuleTests.Scroll(1, "Scroll of " + armor.Name, armor.SpellId);

        var kept = ScrollKeepRule.Evaluate(scroll, ScrollKeepRule.PlanLines(rung, null, C), book, q, L.PlanCastChance);
        Assert.Equal(ScrollKeepKind.Keep, kept.Kind); // off: the plan's next level of her own Armor Self is kept to be read

        var covered = ScrollKeepRule.Evaluate(scroll, ScrollKeepRule.PlanLines(rung, null, C, botCovers: Tusker), book, q, L.PlanCastChance);
        Assert.Equal(ScrollKeepKind.BotCovers, covered.Kind);
        Assert.False(covered.Keep);
        Assert.Contains("the plan's Armor Self line, which the buff bot casts at level VII while its stop applies: not kept, not read", covered.Reason, StringComparison.Ordinal);

        // The hook reads the cover at each call: the same scroll is kept, then left, then kept again.
        var on = false;
        var hook = ScrollKeepRule.Hook(L, Burrow, null, () => book, () => q, C, () => on ? Tusker : null);
        Assert.NotNull(hook(scroll));
        on = true;
        Assert.Null(hook(scroll));
        on = false;
        Assert.NotNull(hook(scroll));

        // A line of her own is kept in both: the plan's next Impenetrability.
        var lines = ScrollKeepRule.PlanLines(rung, null, C, botCovers: Tusker);
        Assert.All(lines, l => Assert.Equal(BuffBotCover.Covers(Tusker, l.Name), l.Covered));
        Assert.False(lines.Single(l => l.Name == "Impenetrability").Covered);
    }

    [Fact]
    public void The_foci_do_not_wait_for_a_gate_scroll_the_bot_casts()
    {
        var ground = GroundCatalog.Get(Outskirts);
        var q = Winnie(16);
        static bool HerOwn(string line, int level) => line is "Impenetrability" or "Bludgeon Bane"; // the book of the bot's build: her item lines, none of the bot's

        // Before: the Burrow gate's Focus Self II, Willpower Self II, Life Magic Mastery III, Armor Self IV and Bludgeon Protection IV are never learned, so no focus was ever bought.
        Assert.Null(FociPlan.Next(ground, L, C, q, HerOwn, _ => 0, new HashSet<uint>(), out var waits));
        Assert.Contains("Focus Self II learned", waits, StringComparison.Ordinal);

        // With the cover: only her own conditions are waited for; Creature Enchantment is untrained, so the first focus is Item Enchantment's.
        var need = FociPlan.Next(ground, L, C, q, HerOwn, _ => 0, new HashSet<uint>(), out var why, botCovers: Tusker);
        Assert.NotNull(need);
        Assert.Equal(DatReaderWriter.Enums.MagicSchool.ItemEnchantment, need.Focus.School);
        Assert.Contains("the burrow gate's conditions are met", why, StringComparison.Ordinal);

        // Her own lines are still waited for.
        Assert.Null(FociPlan.Next(ground, L, C, q, static (_, _) => false, _ => 0, new HashSet<uint>(), out var own, botCovers: Tusker));
        Assert.Contains("Impenetrability III learned", own, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus Self", own, StringComparison.Ordinal);
    }
}

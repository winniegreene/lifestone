using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 0 a3a (D5, D7) and F1 (D34): the climber's creation template. What these prove: War Magic is specialised first (no Alchemy, D34 amends D6), then Life Magic (specialised when the credits allow, else trained) and the other creation skills as far as the credits go; Melee Defense is never bought;
/// a skill the credits cannot pay for is named in a loud line (<c>TODO(m6-climber-creation-credits)</c>); the intent is a level-1 traditional mage's (attributes 10, 10, 10, 100, 100, 100); the template check reads level 1, the classes and Melee Defense untrained;
/// and, with the retail dats, the SkillTable's real costs pay War Magic and Life Magic specialised (28 + 20) inside the Aluvian heritage's 52 credits. The Winnie plumbing (the account list, the journey plan, the station kind and the quest file) is pinned at the end, with the Pathwarden wear plan (F1).
/// </summary>
public sealed class FreshClimberTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly Dictionary<uint, int> Trained = new()
    {
        [MageTemplate.WarMagic] = 14, [MageTemplate.Alchemy] = 6, [MageTemplate.LifeMagic] = 12, [MageTemplate.CreatureEnchantment] = 8, [MageTemplate.ItemEnchantment] = 8, [MageTemplate.ManaConversion] = 6,
    };

    private static readonly Dictionary<uint, int> Specialized = new()
    {
        [MageTemplate.WarMagic] = 28, [MageTemplate.LifeMagic] = 20, [MageTemplate.Alchemy] = 12, [MageTemplate.CreatureEnchantment] = 16, [MageTemplate.ItemEnchantment] = 16, [MageTemplate.ManaConversion] = 12,
    };

    private static MageTemplate.ClimberCreationPlan Plan(int credits) => MageTemplate.PlanClimberCreation(credits, id => Trained[id], id => Specialized[id]);

    private static uint[] StandInClasses()
    {
        var classes = new uint[CharacterCreateIntent.RequiredSkillCount];
        Array.Fill(classes, HeadlessCharacter.UntrainedSkill);
        classes[MageTemplate.ArcaneLore] = HeadlessCharacter.TrainedSkill;
        classes[MageTemplate.Jump] = HeadlessCharacter.TrainedSkill;
        classes[MageTemplate.Run] = HeadlessCharacter.TrainedSkill;
        return classes;
    }

    [Fact]
    public void Fifty_two_credits_pay_War_Magic_and_Life_Magic_specialised_no_Alchemy_and_name_what_they_cannot_pay()
    {
        var plan = Plan(52);
        Assert.Equal(
            [(MageTemplate.WarMagic, HeadlessCharacter.SpecializedSkill, 28), (MageTemplate.LifeMagic, HeadlessCharacter.SpecializedSkill, 20)],
            plan.Paid.Select(p => (p.Skill, p.Class, p.Cost)));
        Assert.Equal((48, 4), (plan.Spent, plan.Left));
        Assert.Equal([MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.ManaConversion], plan.Unpaid.Select(u => u.Skill));
        Assert.DoesNotContain(plan.Paid, p => p.Skill == MageTemplate.MeleeDefense);
        Assert.DoesNotContain(plan.Paid, p => p.Skill == MageTemplate.Alchemy); // D34
        Assert.DoesNotContain(plan.Unpaid, u => u.Skill == MageTemplate.Alchemy);
        Assert.Equal([new CreditLine(MageTemplate.Alchemy, 6)], plan.Last); // M6 14: not bought at creation, its trained cost read for the credit order's last line (no loud line: it is not a creation shortfall)

        var lines = plan.LoudLines;
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.StartsWith("TODO(m6-climber-creation-credits):", l, StringComparison.Ordinal));
        Assert.Contains("Creature Enchantment trained", lines[0], StringComparison.Ordinal);
        Assert.Contains("cannot pay 8", lines[0], StringComparison.Ordinal);
        Assert.Contains("(48 spent at creation, 4 left)", lines[0], StringComparison.Ordinal);
        Assert.Contains("Item Enchantment trained", lines[1], StringComparison.Ordinal); // M6 D43: Item Enchantment before Mana Conversion
        Assert.Contains("Mana Conversion trained", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_heritage_with_enough_credits_specialises_Life_Magic_and_names_nothing()
    {
        var plan = Plan(90);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, plan.ClassOf(MageTemplate.LifeMagic));
        Assert.Null(plan.ClassOf(MageTemplate.Alchemy)); // D34: the creation trains no Alchemy however many credits there are
        Assert.Empty(plan.Unpaid);
        Assert.Empty(plan.LoudLines);
        Assert.Equal(90 - plan.Spent, plan.Left);
    }

    [Fact]
    public void Credits_that_cannot_pay_Life_Magic_name_it_and_a_missing_cost_is_a_loud_dat_gap()
    {
        var plan = Plan(30); // War Magic 28 leaves 2: nothing else fits
        Assert.Equal([MageTemplate.WarMagic], plan.Paid.Select(p => p.Skill));
        Assert.Contains(plan.Unpaid, u => u.Skill == MageTemplate.LifeMagic);
        Assert.Contains(plan.LoudLines, l => l.Contains("Life Magic specialised", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Unpaid, u => u.Skill == MageTemplate.Alchemy);
        var gap = Assert.Throws<InvalidDataException>(() => MageTemplate.PlanClimberCreation(52, _ => -1, _ => -1));
        Assert.StartsWith("TODO(m6-climber-creation-cost)", gap.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_intent_is_a_level_one_traditional_mage_with_no_Alchemy_and_no_Melee_Defense()
    {
        var plan = Plan(52);
        var intent = MageTemplate.ClimberCreationIntent(MageTemplate.ClimberName, StandInClasses(), plan);
        Assert.Equal("Winnie", intent.Name);
        Assert.Equal((10u, 10u, 10u, 100u, 100u, 100u), (intent.Strength, intent.Endurance, intent.Quickness, intent.Coordination, intent.Focus, intent.Self));
        Assert.Equal(HeadlessCharacter.SpecializedSkill, intent.SkillAdvancementClasses[(int)MageTemplate.WarMagic]);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, intent.SkillAdvancementClasses[(int)MageTemplate.LifeMagic]);
        Assert.Equal(HeadlessCharacter.UntrainedSkill, intent.SkillAdvancementClasses[(int)MageTemplate.Alchemy]); // D34
        Assert.Equal(HeadlessCharacter.UntrainedSkill, intent.SkillAdvancementClasses[(int)MageTemplate.MeleeDefense]);
        Assert.Equal(HeadlessCharacter.TrainedSkill, intent.SkillAdvancementClasses[(int)MageTemplate.ArcaneLore]); // the heritage's free skills stay
        Assert.Equal(HeadlessCharacter.UntrainedSkill, intent.SkillAdvancementClasses[(int)MageTemplate.CreatureEnchantment]); // unpaid: left to item 1
        Assert.Equal(CharacterCreateIntent.RequiredSkillCount, intent.SkillAdvancementClasses.Count);
    }

    [Fact]
    public void The_model_climber_is_created_with_Endurance_100_and_Coordination_10_and_Winnie_keeps_the_traditional_mage()
    {
        var plan = Plan(52);
        var intent = MageTemplate.ClimberCreationIntent(MageTemplate.ModelClimberName, StandInClasses(), plan); // the owner's ruling of 2026-11-18, for the sprint
        Assert.Equal((10u, 100u, 10u, 10u, 100u, 100u), (intent.Strength, intent.Endurance, intent.Quickness, intent.Coordination, intent.Focus, intent.Self));
        Assert.Equal(330u, intent.Strength + intent.Endurance + intent.Quickness + intent.Coordination + intent.Focus + intent.Self); // the heritage's 330 (CharGenScreenTests)
        var winnie = MageTemplate.ClimberCreationIntent(MageTemplate.ClimberName, StandInClasses(), plan);
        Assert.Equal((10u, 100u), (winnie.Endurance, winnie.Coordination));
        Assert.Equal(100u, MageTemplate.CreationValue(MageTemplate.Coordination)); // BSD Mage and the other mages keep the traditional template

        Assert.True(MageTemplate.ClimberCheck(MageTemplate.ModelClimberName, Created(plan, MageTemplate.ModelClimberName), plan).AllMet);
        var shortOnes = MageTemplate.ClimberCheck(MageTemplate.ModelClimberName, Created(plan), plan).Short.Select(c => c.Label).ToArray(); // a traditional copy under her name is short by the two swapped attributes
        Assert.Equal(["attribute 2 start", "attribute 4 start"], shortOnes);
        Assert.False(MageTemplate.ClimberCheck(MageTemplate.ClimberName, Created(plan, MageTemplate.ModelClimberName), plan).AllMet);
    }

    private static PlayerQualities Created(MageTemplate.ClimberCreationPlan plan, string name = MageTemplate.ClimberName)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = 1;
        for (uint id = 1; id <= 6; id++)
        {
            q.Attributes[id] = new AttributeDesc(id, 0, MageTemplate.ClimberCreationValue(name, id), 0);
        }

        foreach (var pick in plan.Paid)
        {
            q.Skills[pick.Skill] = new SkillDesc(pick.Skill, 0, 1, pick.Class, 1, pick.Class == HeadlessCharacter.SpecializedSkill ? 10u : 0u, 0, 0);
        }

        return q;
    }

    [Fact]
    public void The_template_check_is_met_at_level_one_and_short_by_name_for_a_level_a_class_or_a_trained_Melee_Defense()
    {
        var plan = Plan(52);
        var check = MageTemplate.ClimberCheck(MageTemplate.ClimberName, Created(plan), plan);
        Assert.True(check.AllMet, check.Summary);
        Assert.Equal("ok", check.Summary);
        Assert.Equal(1 + 6 + plan.Paid.Count + 1, check.Checks.Count);

        var q = Created(plan);
        q.Ints[PlayerQualities.LevelProperty] = 2; // an XP grant would show here: the climber is created at level 1 and gains only what it earns
        q.Skills[MageTemplate.LifeMagic] = q.Skills[MageTemplate.LifeMagic] with { AdvancementClass = HeadlessCharacter.UntrainedSkill };
        q.Skills[MageTemplate.MeleeDefense] = new SkillDesc(MageTemplate.MeleeDefense, 0, 1, HeadlessCharacter.TrainedSkill, 1, 0, 0, 0);
        Assert.Equal(["level", "Life Magic class", "Melee Defense class (untrained)"], MageTemplate.ClimberCheck(MageTemplate.ClimberName, q, plan).Short.Select(c => c.Label));
        Assert.False(MageTemplate.ClimberCheck(MageTemplate.ClimberName, new PlayerQualities(), plan).AllMet);
    }

    [RequiresDatsFact]
    public void The_retail_skill_table_prices_War_Magic_and_Life_Magic_specialised_inside_the_Aluvian_heritages_credits_with_no_Alchemy()
    {
        var plan = ClimberCreateScenario.PlanFromDats(fixture.Dats, buffBot: false); // the creation pays the same in the bot's order (BotBuildTests)
        output.WriteLine($"climber creation: {plan.Credits} credits, {plan.Spent} spent, {plan.Left} left; paid {string.Join(", ", plan.Paid.Select(p => $"{MageTemplate.SkillName(p.Skill)} {(p.Class == HeadlessCharacter.SpecializedSkill ? "spec" : "trained")} {p.Cost}"))}");
        foreach (var line in plan.LoudLines)
        {
            output.WriteLine(line);
        }

        Assert.Equal(MageTemplate.HeritageSkillCredits, plan.Credits);
        Assert.InRange(plan.Spent, 1, plan.Credits);
        Assert.Contains(plan.Paid, p => p is { Skill: MageTemplate.WarMagic, Class: HeadlessCharacter.SpecializedSkill, Cost: 28 });
        Assert.Contains(plan.Paid, p => p is { Skill: MageTemplate.LifeMagic, Class: HeadlessCharacter.SpecializedSkill, Cost: 20 }); // the traditional mage's second creation specialisation
        Assert.Equal(48, plan.Spent);
        Assert.DoesNotContain(plan.Paid, p => p.Skill == MageTemplate.Alchemy); // D34 (amends D6)
        Assert.DoesNotContain(plan.Paid, p => p.Skill == MageTemplate.MeleeDefense); // D5
        Assert.Equal(plan.Unpaid.Count, plan.LoudLines.Count);
    }

    [Fact]
    public void Winnie_is_a_test_account_character_and_the_climber_journey_is_the_academy_then_the_pathwarden_outside_the_gates_three_plans()
    {
        Assert.Equal("Winnie", MageTemplate.ClimberName);
        Assert.Equal("test", TestAccounts.AccountOf("Winnie"));
        var plan = JourneyPlans.ForName("climber");
        Assert.NotNull(plan);
        Assert.Equal(("Winnie", "test", 0), (plan.Character, plan.Account, plan.TeleportBudget));
        Assert.Equal([StationKinds.Academy, StationKinds.Pathwarden], plan.Stations.Select(s => s.Kind)); // M6 0 a3b: the Academy by play first
        Assert.DoesNotContain(JourneyPlans.All, p => p.Name == "climber");
        Assert.Contains(StationKinds.Pathwarden, StationKinds.All);

        var quest = QuestCatalog.Get("pathwarden");
        Assert.Equal(new System.Numerics.Vector3(97, 27, 94), quest.Steps[0].Place!.Point);
        Assert.Equal(0xA9B40022u, quest.Steps[0].Place!.Cell);
        Assert.NotNull(quest.Steps.First(s => s.Kind == QuestStepKind.Use).Place);
    }

    // ------------------------------------------------------------------------------------------------ the Pathwarden wear plan (F1)

    /// <summary>The pinned world SQL's rows for the four pieces (ClothingPriority 4 and ValidLocations 9 of weenie_properties_int, W0 SELECT, ace_world on this server, 2026-11-01), as the dump writes them.</summary>
    private const string PieceRows = """
        CREATE TABLE `weenie_properties_int` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `value` int(11) NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_int` VALUES (353113,33605,4,65536),(353115,33605,9,256),(353128,33606,4,32768),(353130,33606,9,32),(365360,40439,4,98048),(365363,40439,9,32513),(494715,41513,9,67108864);
        """;

    private const uint RobeWcid = 40439;
    private const uint GauntletsWcid = 33606;
    private const uint SolleretsWcid = 33605;
    private const uint TrinketWcid = 41513;
    private static readonly HashSet<uint> QuestWcids = [RobeWcid, GauntletsWcid, SolleretsWcid, TrinketWcid];

    // A new character's starter outfit (PlayerFactory.cs:109-130): a shirt and pants (underwear coverage) and shoes (feet); the shirt and pants priorities here are illustrative underwear masks (0x2 to 0x40), the feet are CoverageMask.Feet.
    private static readonly PathwardenWear.WornPiece Shirt = new(0xA1, 100, "Shirt", 0x1A, 0x68);
    private static readonly PathwardenWear.WornPiece Pants = new(0xA2, 101, "Pants", 0xC4, 0x06);
    private static readonly PathwardenWear.WornPiece Shoes = new(0xA3, 102, "Shoes", 0x100, 0x10000);

    private static uint Int(IEnumerable<SqlRow> rows, uint wcid, long type) => (uint)rows.Single(r => r.L("object_Id") == wcid && r.L("type") == type).L("value");

    [Fact]
    public void The_pinned_rows_give_the_robe_the_feet_and_the_head_so_the_starter_shoes_and_the_sollerets_are_in_its_way()
    {
        using var reader = new StringReader(PieceRows);
        var rows = WorldSqlDump.Read(reader, new Dictionary<string, Func<SqlRow, bool>> { ["weenie_properties_int"] = _ => true })["weenie_properties_int"].View().ToList();
        Assert.Equal(0x17F00u, Int(rows, RobeWcid, 4));
        Assert.Equal(0x7F01u, Int(rows, RobeWcid, 9));
        Assert.Equal(0x8000u, Int(rows, GauntletsWcid, 4));
        Assert.Equal(0x10000u, Int(rows, SolleretsWcid, 4));
        Assert.Equal(0x04000000u, Int(rows, TrinketWcid, 9)); // TrinketOne, no ClothingPriority row

        var robe = Int(rows, RobeWcid, 4);
        var plan = PathwardenWear.Plan(0xB1, [Shirt, Pants, Shoes], robe, Int(rows, RobeWcid, 9), QuestWcids);
        Assert.Equal(["Shoes"], plan.TakeOff.Select(w => w.Name)); // the shirt and pants are underwear: not in the way, and not taken off
        Assert.Null(plan.Blocker);

        var gauntlets = PathwardenWear.Plan(0xB2, [Shirt, Pants, Shoes], Int(rows, GauntletsWcid, 4), 32, QuestWcids);
        Assert.Empty(gauntlets.TakeOff);
        Assert.Null(gauntlets.Blocker);

        var onceTheRobeIsOn = new PathwardenWear.WornPiece(0xB1, RobeWcid, "Pathwarden Robe", 0x7F01, robe);
        var sollerets = PathwardenWear.Plan(0xB3, [Shirt, Pants, onceTheRobeIsOn], Int(rows, SolleretsWcid, 4), 256, QuestWcids);
        Assert.Empty(sollerets.TakeOff);
        Assert.Equal("Pathwarden Robe", sollerets.Blocker?.Name); // the quest's own robe covers the feet: the sollerets stay in the pack, named, never forced
        Assert.Equal(0x10000u, sollerets.Blocker!.Coverage & sollerets.Coverage);
    }

    [Fact]
    public void A_starter_hat_comes_off_for_the_robe_a_worn_quest_piece_is_never_taken_off_and_the_trinket_goes_by_its_location()
    {
        var hat = new PathwardenWear.WornPiece(0xA4, 103, "Hat", 0x1, 0x4000);
        var robe = PathwardenWear.Plan(0xB1, [hat, Shoes], 0x17F00, 0x7F01, QuestWcids);
        Assert.Equal(["Hat", "Shoes"], robe.TakeOff.Select(w => w.Name));

        var worn = new PathwardenWear.WornPiece(0xB3, SolleretsWcid, "Pathwarden Sollerets", 0x100, 0x10000);
        var robeAfterSollerets = PathwardenWear.Plan(0xB1, [worn], 0x17F00, 0x7F01, QuestWcids);
        Assert.Empty(robeAfterSollerets.TakeOff);
        Assert.Equal(SolleretsWcid, robeAfterSollerets.Blocker?.Wcid);

        Assert.Empty(PathwardenWear.Plan(0xB4, [Shirt, Pants, Shoes], 0, 0x04000000, QuestWcids).TakeOff); // nothing holds TrinketOne
        var otherTrinket = new PathwardenWear.WornPiece(0xA5, 104, "Old Trinket", 0x04000000, 0);
        Assert.Equal(["Old Trinket"], PathwardenWear.Plan(0xB4, [Shirt, otherTrinket], 0, 0x04000000, QuestWcids).TakeOff.Select(w => w.Name));
        Assert.Empty(PathwardenWear.Plan(0xB1, [new PathwardenWear.WornPiece(0xB1, RobeWcid, "Robe", 0x7F01, 0x17F00)], 0x17F00, 0x7F01, QuestWcids).TakeOff); // a piece never blocks itself
    }

    [Fact]
    public void The_quest_file_lists_the_robe_before_the_sollerets_so_the_robe_wins_the_feet_and_the_verdict_names_what_was_left_off()
    {
        var wcids = QuestCatalog.Get("pathwarden").Steps.Where(s => s.Kind == QuestStepKind.PickUp).Select(s => s.Item!.Wcid!.Value).ToList();
        Assert.Equal([RobeWcid, GauntletsWcid, SolleretsWcid, TrinketWcid], wcids);
        Assert.Equal(TrinketWcid, QuestCatalog.Get("pathwarden").Done.Item!.Wcid);
    }
}

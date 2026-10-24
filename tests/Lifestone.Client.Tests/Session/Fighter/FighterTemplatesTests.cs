using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>The three fighter templates as data (M4 P4, plan D31), no dats and no backend: the lookups, the kits, the hunt defaults, the Hunter unchanged and the template-driven scenarios.</summary>
public sealed class FighterTemplatesTests
{
    [Theory]
    [InlineData(null, "hunter")]
    [InlineData("", "hunter")]
    [InlineData("  ", "hunter")]
    [InlineData("test", "hunter")]
    [InlineData("TEST", "hunter")]
    [InlineData("test-melee", "melee")]
    [InlineData("Test-Melee", "melee")]
    [InlineData("test-archer", "archer")]
    public void The_template_is_found_by_the_account_key(string? account, string expectedKey)
    {
        Assert.Equal(expectedKey, FighterTemplate.ForAccount(account)!.Key);
    }

    [Theory]
    [InlineData("test-mage")]
    [InlineData("play")]
    [InlineData("test-melee2")]
    public void An_account_with_no_fighter_is_not_found_and_never_falls_back_to_the_hunter(string account)
    {
        Assert.Null(FighterTemplate.ForAccount(account));
    }

    [Theory]
    [InlineData("Holtburg Hunter", "hunter")]
    [InlineData("holtburg brawler", "melee")]
    [InlineData("HOLTBURG ARCHER", "archer")]
    public void The_template_is_found_by_the_character_name(string name, string expectedKey)
    {
        Assert.Equal(expectedKey, FighterTemplate.ForName(name)!.Key);
        Assert.Null(FighterTemplate.ForName("BSD Mage"));
        Assert.Null(FighterTemplate.ForName(null));
    }

    [Fact]
    public void The_keys_names_and_accounts_are_distinct_and_the_hunter_lives_on_the_default_test_account()
    {
        Assert.Equal(["hunter", "melee", "archer"], FighterTemplate.All.Select(t => t.Key));
        Assert.Equal(3, FighterTemplate.All.Select(t => t.Name).Distinct().Count());
        Assert.Equal(3, FighterTemplate.All.Select(t => t.AccountKey).Distinct().Count());
        Assert.Equal(("Holtburg Hunter", "test"), (FighterTemplate.Hunter.Name, FighterTemplate.Hunter.AccountKey));
        Assert.Equal(("Holtburg Brawler", "test-melee"), (FighterTemplate.Melee.Name, FighterTemplate.Melee.AccountKey));
        Assert.Equal(("Holtburg Archer", "test-archer"), (FighterTemplate.Archer.Name, FighterTemplate.Archer.AccountKey));
        Assert.Null(FighterTemplate.ForKey("mage"));
        Assert.Same(FighterTemplate.Archer, FighterTemplate.ForKey("Archer"));
    }

    [Fact]
    public void The_hunter_is_unchanged()
    {
        var hunter = FighterTemplate.Hunter;
        Assert.Equal([FighterTemplate.HeavyWeapons], hunter.SpecializedAtCreation);
        Assert.Equal([FighterTemplate.MeleeDefense, FighterTemplate.MissileWeapons, FighterTemplate.Healing], hunter.TrainedAtCreation);
        Assert.Equal(34, hunter.CreationSkillCredits);
        Assert.Equal((20_487_694UL, 14_567UL), (hunter.XpSpent, hunter.XpRemainder));
        Assert.Equal([145, 112, 107, 97, 94, 93, 94, 91, 90], hunter.Targets.Select(t => t.Ranks));
        Assert.Equal((HuntWeapon.Melee, HuntGroundChoice.Belt), (hunter.DefaultWeapon, hunter.DefaultGround));
        Assert.Equal(FighterKit.MaceWcid, hunter.WieldedWeaponWcid);
        Assert.Equal(10, hunter.Kit.Items.Count);
        Assert.Equal(FighterKit.Burden, hunter.Kit.TotalBurden);
        Assert.Equal(FighterKit.ArrowCount, hunter.Kit.ItemOf(FighterKit.ArrowWcid)!.Item.Stack);
        Assert.True(hunter.CanHuntWith(HuntWeapon.Melee) && hunter.CanHuntWith(HuntWeapon.Missile));
    }

    [Fact]
    public void The_brawler_has_the_mace_and_no_bow_and_no_arrows_and_no_missile_weapons_skill()
    {
        var kit = FighterTemplate.Melee.Kit;
        Assert.NotNull(kit.ItemOf(kit.WeaponWcid)); // M4 P5a C6: the gear's melee weapon, not the plain Mace
        Assert.Null(kit.ItemOf(FighterKit.MaceWcid));
        Assert.Null(kit.ItemOf(FighterKit.ShortbowWcid));
        Assert.Null(kit.ItemOf(FighterKit.ArrowWcid));
        Assert.False(kit.HasBow);
        Assert.True(kit.HasMelee);
        Assert.Equal(kit.WeaponWcid, FighterTemplate.Melee.WieldedWeaponWcid);
        Assert.NotEqual(FighterKit.MaceWcid, kit.WeaponWcid);
        Assert.Equal([FighterTemplate.HeavyWeapons], FighterTemplate.Melee.SpecializedAtCreation);
        Assert.Equal([FighterTemplate.MeleeDefense, FighterTemplate.Healing], FighterTemplate.Melee.TrainedAtCreation);
        Assert.DoesNotContain(FighterTemplate.Melee.Targets, t => t.Id == FighterTemplate.MissileWeapons);
        Assert.Equal(7, kit.Worn.Count(i => i.Role == FighterRole.Armour)); // M4 P5a C6: seven mismatched magic pieces cover the nine armour slots
        Assert.Equal(5, kit.Worn.Count(i => i.Role == FighterRole.Jewelry));
        Assert.Contains(kit.Pack, i => i.Role == FighterRole.HealingKit);
    }

    [Fact]
    public void The_archer_has_the_Longbow_and_the_Greater_Frost_Arrows_and_no_mace_and_no_heavy_weapons_skill_P4_arch()
    {
        var kit = FighterTemplate.Archer.Kit;
        Assert.Null(kit.ItemOf(FighterKit.MaceWcid));
        Assert.Null(kit.ItemOf(FighterKit.ShortbowWcid));
        Assert.Null(kit.ItemOf(FighterKit.ArrowWcid));
        Assert.Equal(FighterGearSet.Archer.Weapon.Wcid, kit.LauncherWcid); // M4 P5a C6: the gear's bow, not the plain Longbow 31092
        Assert.Null(kit.ItemOf(FighterKit.LongbowWcid));
        Assert.Equal(5307u, kit.AmmoWcid);
        Assert.Equal(FighterRole.Bow, kit.ItemOf(kit.LauncherWcid)!.Role);
        Assert.Equal(FighterRole.Ammo, kit.ItemOf(5307)!.Role);
        Assert.Equal(FighterKit.ArcherArrowCount, kit.ItemOf(FighterKit.GreaterFrostArrowWcid)!.Item.Stack);
        Assert.Equal(250, FighterKit.ArcherArrowCount);
        Assert.True(kit.ItemOf(FighterKit.GreaterFrostArrowWcid)!.Item.Stack <= kit.ItemOf(FighterKit.GreaterFrostArrowWcid)!.Item.MaxStack);
        Assert.True(kit.TotalBurden < FighterKit.Capacity((int)FighterTemplate.Archer.AttributeAtTarget(FighterTemplate.Strength))); // M4 P5a C5: the kit is granted after the raises, at the Strength the plan buys
        // The Hunter keeps its Shortbow and Arrows, the Brawler has no launcher.
        Assert.Equal(307u, FighterTemplate.Hunter.Kit.LauncherWcid);
        Assert.Equal(300u, FighterTemplate.Hunter.Kit.AmmoWcid);
        Assert.Equal(FighterKit.ArrowCount, FighterTemplate.Hunter.Kit.ItemOf(300)!.Item.Stack);
        Assert.Equal(0u, FighterTemplate.Melee.Kit.LauncherWcid);
        Assert.Equal(0u, FighterTemplate.Melee.Kit.AmmoWcid);
        Assert.True(kit.HasBow);
        Assert.False(kit.HasMelee);
        Assert.Equal(0u, FighterTemplate.Archer.WieldedWeaponWcid);
        Assert.Equal([FighterTemplate.MissileWeapons], FighterTemplate.Archer.SpecializedAtCreation);
        Assert.Equal([FighterTemplate.MeleeDefense, FighterTemplate.Healing], FighterTemplate.Archer.TrainedAtCreation);
        Assert.DoesNotContain(FighterTemplate.Archer.Targets, t => t.Id == FighterTemplate.HeavyWeapons);
        Assert.True(FighterTemplate.Archer.Targets.Single(t => t.Id == FighterTemplate.MissileWeapons).Specialized);
        Assert.Equal(7, kit.Worn.Count(i => i.Role == FighterRole.Armour));
        Assert.Equal(5, kit.Worn.Count(i => i.Role == FighterRole.Jewelry));
        Assert.DoesNotContain(kit.Worn, i => i.Role == FighterRole.Weapon);
    }

    [Fact]
    public void Every_kit_fits_the_strength_the_plan_buys_and_the_burdens_are_pinned()
    {
        // M4 P5a C1b: the Brawler's and the Archer's kits hold the caster (burden 50) in the pack; the Hunter has no magic and no caster.
        // M4 P5a fix r4 (D28): the template kits carry one side pack (FighterKit.SidePacksFor(0)); the build's kit sizes them from the component lines (both fighters: 3, burden 15 each), so 15 less here.
        // M4 P5a C7 (D28): the kits hold 100 Mana Potions and 50 Stamina Potions (15 burden each, 2,250) and the base kit's two side packs (the two potion lines and the spare): 4,465 + 2,250 + 15 = 6,730 (100 + 50 potions) and 5,710 + 1,050 + 15 = 6,775 (the Archer's 60 + 10: its capacity leaves about 1,100).
        Assert.Equal((4_860, 6_730, 6_775), (FighterTemplate.Hunter.Kit.TotalBurden, FighterTemplate.Melee.Kit.TotalBurden, FighterTemplate.Archer.Kit.TotalBurden));
        Assert.All(FighterTemplate.All, t => Assert.True(t.Kit.TotalBurden <= FighterKit.Capacity((int)t.AttributeAtTarget(FighterTemplate.Strength)), t.Key)); // M4 P5a C5: the Archer is created with Strength 10 (capacity 1,500); the build grants the kit after the raises (FighterBuildScenario step d), so the plan's Strength is what carries it
    }

    [Fact]
    public void The_brawler_and_the_archer_keep_the_healers_heart_in_the_pack_and_the_hunter_has_none_P5a_C1b()
    {
        foreach (var template in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            var caster = template.Kit.Items.Single(i => i.Role == FighterRole.Caster);
            Assert.Equal((8552u, "The Healer's Heart"), (caster.Wcid, caster.Item.Name));
            Assert.False(caster.Wield, "the build never wields it: the swap does");
            Assert.Contains(caster, template.Kit.Pack);
            Assert.DoesNotContain(caster, template.Kit.Worn);
            Assert.Equal((35u, 0x8000u, EquipMask.Held, 50), (caster.Item.WeenieType, caster.Item.ItemType, caster.Item.ValidLocations, caster.Item.Burden));
            Assert.True(template.Kit.HasCaster);
            Assert.Equal(8552u, template.Kit.CasterWcid);
        }

        Assert.False(FighterTemplate.Hunter.Kit.HasCaster);
        Assert.Equal(0u, FighterTemplate.Hunter.Kit.CasterWcid);
        Assert.DoesNotContain(FighterTemplate.Hunter.Kit.Items, i => i.Role == FighterRole.Caster);
    }

    [Fact]
    public void The_hunt_defaults_follow_the_template_and_the_command_line_still_overrides_them()
    {
        var bare = HuntOptions.FromArguments(CommandLineArgument.ParseAll([]));
        Assert.Empty(bare.Errors);
        var hunter = bare.ForTemplate(FighterTemplate.Hunter);
        var melee = bare.ForTemplate(FighterTemplate.Melee);
        var archer = bare.ForTemplate(FighterTemplate.Archer);
        Assert.Equal((HuntWeapon.Melee, HuntGroundChoice.Belt), (hunter.Weapon, hunter.Ground));
        Assert.Equal((HuntWeapon.Melee, HuntGroundChoice.Belt), (melee.Weapon, melee.Ground));
        Assert.Equal((HuntWeapon.Missile, HuntGroundChoice.Forest), (archer.Weapon, archer.Ground));
        Assert.Same(HuntGround.LowB, melee.GroundRow);
        Assert.Same(HuntGround.LowNorthForestMix, archer.GroundRow);
        Assert.Contains(HuntGround.LowNorthForestTable, c => c.Level is >= 30 and <= 40);
        Assert.Empty(hunter.Errors.Concat(melee.Errors).Concat(archer.Errors));

        var given = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-weapon=missile", "--hunt-ground=belt"]));
        var hunterGiven = given.ForTemplate(FighterTemplate.Hunter);
        Assert.Equal((HuntWeapon.Missile, HuntGroundChoice.Belt), (hunterGiven.Weapon, hunterGiven.Ground));
        var archerGround = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-ground=belt"])).ForTemplate(FighterTemplate.Archer);
        Assert.Equal((HuntWeapon.Missile, HuntGroundChoice.Belt), (archerGround.Weapon, archerGround.Ground));
        var archerMelee = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-weapon=melee"])).ForTemplate(FighterTemplate.Archer);
        Assert.Equal(HuntWeapon.Melee, archerMelee.Weapon);
    }

    [Fact]
    public void A_missile_hunt_with_no_bow_or_a_melee_hunt_with_no_melee_weapon_is_a_loud_option_error_that_shows_no_value()
    {
        var missileBrawler = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-weapon=missile"])).ForTemplate(FighterTemplate.Melee);
        var error = Assert.Single(missileBrawler.Errors);
        Assert.StartsWith("--hunt-weapon:", error, StringComparison.Ordinal);
        Assert.Contains("melee fighter", error, StringComparison.Ordinal);
        Assert.Contains("no bow", error, StringComparison.Ordinal);

        var meleeArcher = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-weapon=melee"])).ForTemplate(FighterTemplate.Archer);
        Assert.Contains("no melee weapon", Assert.Single(meleeArcher.Errors), StringComparison.Ordinal);

        Assert.Empty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-weapon=missile"])).ForTemplate(FighterTemplate.Hunter).Errors);
    }

    [Fact]
    public void The_hunt_scenario_refuses_a_weapon_the_template_has_none_of_when_built_directly()
    {
        var log = Lifestone.Client.Diagnostics.NullLogSink.Instance;
        Assert.Throws<ArgumentException>(() => new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Missile }, log, _ => 0f, template: FighterTemplate.Melee));
        Assert.Throws<ArgumentException>(() => new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Melee }, log, _ => 0f, template: FighterTemplate.Archer));
        _ = new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Missile }, log, _ => 0f, template: FighterTemplate.Archer);
        _ = new HuntScenario(new HuntOptions(), log, _ => 0f);
    }

    [Fact]
    public void The_name_check_and_the_login_check_field_follow_the_template()
    {
        Assert.True(FighterBuildScenario.NameCheck("holtburg archer", FighterTemplate.Archer));
        Assert.False(FighterBuildScenario.NameCheck("Holtburg Hunter", FighterTemplate.Archer));
        Assert.True(FighterBuildScenario.NameCheck("Holtburg Hunter"));
        Assert.True(HuntScenario.NameCheck("Holtburg Brawler", FighterTemplate.Melee));
        Assert.False(HuntScenario.NameCheck("Holtburg Archer", FighterTemplate.Melee));
        var list = new[] { new Lifestone.Client.Session.Contracts.CharacterSlot(1, "Holtburg Brawler", 0) };
        Assert.Equal("present", LoginCheckScenario.FighterOf(list, FighterTemplate.Melee));
        Assert.Equal("absent", LoginCheckScenario.FighterOf(list));
        Assert.Equal("absent", LoginCheckScenario.FighterOf(list, FighterTemplate.Archer));
    }

    [Fact]
    public void The_creation_intent_of_each_template_names_its_character_and_skill_classes()
    {
        var retail = Enumerable.Repeat(1u, 55).ToArray();
        foreach (var t in FighterTemplate.All)
        {
            var intent = t.CreationIntent(t.Name, retail);
            Assert.Equal(t.Name, intent.Name);
            Assert.Equal((t.Creation.Strength, t.Creation.Quickness), (intent.Strength, intent.Quickness));
            foreach (var skill in t.SpecializedAtCreation)
            {
                Assert.Equal(HeadlessCharacter.SpecializedSkill, intent.SkillAdvancementClasses[(int)skill]);
            }

            foreach (var skill in t.TrainedAtCreation)
            {
                Assert.Equal(HeadlessCharacter.TrainedSkill, intent.SkillAdvancementClasses[(int)skill]);
            }
        }

        Assert.NotEqual(HeadlessCharacter.TrainedSkill, FighterTemplate.Melee.CreationIntent("x", retail).SkillAdvancementClasses[(int)FighterTemplate.MissileWeapons]);
        Assert.NotEqual(HeadlessCharacter.SpecializedSkill, FighterTemplate.Archer.CreationIntent("x", retail).SkillAdvancementClasses[(int)FighterTemplate.HeavyWeapons]);
    }

    [Fact]
    public void The_shares_of_every_template_sum_to_100_and_name_only_stats_the_template_trains()
    {
        foreach (var t in FighterTemplate.All)
        {
            Assert.Equal(100, t.Shares.Sum(s => s.Percent));
            var skills = t.SpecializedAtCreation.Concat(t.TrainedAtCreation).ToHashSet();
            Assert.All(t.Shares.Where(s => s.Kind == MageStatKind.Skill), s => Assert.Contains(s.Id, skills));
            // M4 P5a: the targets are the base shares' stats, then the stats the extension (the spread) adds for the templates that train magic; a stat in both appears once, in its base place.
            var expectedStats = t.Shares.Concat(t.FloorStats).Concat(t.MagicShares).Select(s => (s.Kind, s.Id)).Distinct().ToList(); // M4 P5a C5: then the floor rule's stats
            Assert.Equal(expectedStats, t.Targets.Select(x => (x.Kind, x.Id)));
            Assert.Equal(t.Targets.Count, t.Targets.Select(x => (x.Kind, x.Id)).Distinct().Count());
            if (t.HasMagic)
            {
                Assert.Equal(100, t.MagicShares.Sum(s => s.Percent));
                Assert.Equal(new[] { FighterTemplate.CreatureEnchantment, FighterTemplate.ItemEnchantment, FighterTemplate.LifeMagic, FighterTemplate.ManaConversion }, t.TrainedLater);
                // The extension buys on skills the template has (created, specialised or trained later) and on Run, the heritage's free trained skill.
                Assert.All(t.MagicShares.Where(s => s.Kind == MageStatKind.Skill), s => Assert.True(skills.Contains(s.Id) || t.TrainedLater.Contains(s.Id) || s.Id == FighterTemplate.Run, $"skill {s.Id}"));
            }
            else
            {
                Assert.Empty(t.TrainedLater);
                Assert.Empty(t.MagicShares);
            }
        }
    }

    [Fact]
    public void Only_the_Brawler_and_the_Archer_train_magic_and_build_to_level_55_the_Hunter_stays_at_40_M4_P5a_D33_D38()
    {
        Assert.False(FighterTemplate.Hunter.HasMagic);
        Assert.Equal(FighterTemplate.TargetLevel, FighterTemplate.Hunter.Level);
        Assert.Null(FighterProfile.For(FighterTemplate.Hunter));
        Assert.Empty(FighterTemplate.Hunter.MagicShares);
        foreach (var t in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            Assert.True(t.HasMagic);
            Assert.Equal(FighterTemplate.MagicLevel, t.Level);
            Assert.Equal(55, t.Level);
            Assert.True(t.Level >= FighterTemplate.TargetLevel); // D19's level>=40 holds
            Assert.NotNull(FighterProfile.For(t));

            // The extension spreads the experience: the weapon, defence and Healing, the attributes, Quickness and Run, the vitals with Max Mana, and the three schools with Focus and Self (D38). The Archer has no Strength share.
            var stats = t.MagicShares.Select(s => (s.Kind, s.Id)).ToHashSet();
            Assert.Contains((MageStatKind.Attribute, FighterTemplate.Quickness), stats);
            Assert.Contains((MageStatKind.Skill, FighterTemplate.Run), stats);
            Assert.Contains((MageStatKind.Vital, FighterTemplate.MaxMana), stats);
            Assert.Superset(
                new HashSet<(MageStatKind, uint)>
                {
                    (MageStatKind.Attribute, FighterTemplate.Focus), (MageStatKind.Attribute, FighterTemplate.Self), (MageStatKind.Skill, FighterTemplate.CreatureEnchantment),
                    (MageStatKind.Skill, FighterTemplate.ItemEnchantment), (MageStatKind.Skill, FighterTemplate.LifeMagic),
                },
                stats);
            Assert.Equal(t.Key == "melee", stats.Contains((MageStatKind.Attribute, FighterTemplate.Strength)));

            // The schools reach their skills from the pinned ranks: level IV (182) for Creature Enchantment and Life Magic, 132 for Item Enchantment (Lifestone Recall, power 100).
            Assert.True(FighterSpendingPlanner.MagicSkill(t.Targets, FighterTemplate.CreatureEnchantment, t.Creation) >= FighterTemplate.IVSkill);
            Assert.True(FighterSpendingPlanner.MagicSkill(t.Targets, FighterTemplate.LifeMagic, t.Creation) >= FighterTemplate.IVSkill);
            Assert.True(FighterSpendingPlanner.MagicSkill(t.Targets, FighterTemplate.ItemEnchantment, t.Creation) >= FighterTemplate.IIISkill);
        }
    }

    [Fact]
    public void The_chance_rule_needs_182_for_level_IV_and_132_for_level_III_M4_P5a()
    {
        Assert.Equal(FighterTemplate.IVSkill, FighterSpendingPlanner.SkillForChance(150));
        Assert.Equal(FighterTemplate.IIISkill, FighterSpendingPlanner.SkillForChance(100));
        Assert.True(Lifestone.Client.Session.Scenarios.GearCasts.Chance(182, 150) >= 0.9);
        Assert.True(Lifestone.Client.Session.Scenarios.GearCasts.Chance(181, 150) < 0.9);
    }

    [Fact]
    public void Only_the_archer_has_its_own_band_and_the_hunt_takes_it_on_the_archers_own_ground_P4()
    {
        Assert.Equal((20, 30), FighterTemplate.Archer.TargetBand);
        Assert.Null(FighterTemplate.Hunter.TargetBand);
        Assert.Null(FighterTemplate.Melee.TargetBand);

        var log = new Lifestone.Client.Tests.RecordingLogSink();
        _ = new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Missile }, log, _ => 0f, HuntGround.LowNorthForestMix, template: FighterTemplate.Archer);
        Assert.Contains(log.Entries, e => e.Message.Contains("takes the band 20-30 of", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.Contains("never picks 5 creature(s)", StringComparison.Ordinal));
        Assert.Empty(FighterTemplate.Hunter.AvoidedTargets);
        Assert.Empty(FighterTemplate.Melee.AvoidedTargets);
        Assert.Contains(1758u, FighterTemplate.Archer.AvoidedTargets); // the Shadow: ResistPierce 0.5

        var belt = new Lifestone.Client.Tests.RecordingLogSink();
        _ = new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Missile }, belt, _ => 0f, HuntGround.LowB, template: FighterTemplate.Archer);
        Assert.DoesNotContain(belt.Entries, e => e.Message.Contains("takes the band", StringComparison.Ordinal)); // --hunt-ground=belt keeps the belt's own band
    }

    [Fact]
    public void Each_template_creates_its_owners_attribute_split_330_credits_each_10_to_100_M4_P5a_C5_D40()
    {
        // Strength, Endurance, Quickness, Coordination, Focus, Self. The Hunter's is unchanged; the Brawler is high (100) in Strength, Coordination and Focus, the Archer in Coordination, Quickness and Focus.
        Assert.Equal((100u, 100u, 10u, 100u, 10u, 10u), Split(FighterTemplate.Hunter));
        Assert.Equal((100u, 10u, 10u, 100u, 100u, 10u), Split(FighterTemplate.Melee));
        Assert.Equal((10u, 10u, 100u, 100u, 100u, 10u), Split(FighterTemplate.Archer));
        var retail = Enumerable.Repeat(1u, 55).ToArray();
        foreach (var t in FighterTemplate.All)
        {
            Assert.Equal(330u, t.Creation.Total);
            for (uint id = FighterTemplate.Strength; id <= FighterTemplate.Self; id++)
            {
                Assert.InRange(t.Creation[id], 10u, 100u);
                Assert.Equal(t.Creation[id], t.CreationValue(id));
            }

            var intent = t.CreationIntent(t.Name, retail);
            Assert.Equal(Split(t), (intent.Strength, intent.Endurance, intent.Quickness, intent.Coordination, intent.Focus, intent.Self));
            Assert.Equal(t.CreationValue(FighterTemplate.Strength) + (uint)(t.TargetOf(MageStatKind.Attribute, FighterTemplate.Strength)?.Ranks ?? 0), t.AttributeAtTarget(FighterTemplate.Strength));
        }

        static (uint, uint, uint, uint, uint, uint) Split(FighterTemplate t) => (t.Creation.Strength, t.Creation.Endurance, t.Creation.Quickness, t.Creation.Coordination, t.Creation.Focus, t.Creation.Self);
    }

    [Fact]
    public void Both_fighters_and_the_hunter_train_Melee_Defense_at_creation_and_spend_on_it_M4_P5a_C5_D40()
    {
        foreach (var t in FighterTemplate.All)
        {
            Assert.Contains(FighterTemplate.MeleeDefense, t.TrainedAtCreation);
            Assert.False(t.TargetOf(MageStatKind.Skill, FighterTemplate.MeleeDefense)!.Specialized);
            Assert.True(t.TargetOf(MageStatKind.Skill, FighterTemplate.MeleeDefense)!.Ranks > 0);
        }
    }

    [Fact]
    public void The_floor_covers_every_attribute_vital_and_trained_skill_of_the_two_fighters_and_none_of_the_hunters_M4_P5a_C5_D40()
    {
        Assert.Empty(FighterTemplate.Hunter.FloorStats);
        foreach (var t in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            var floor = t.FloorStats.Select(f => (f.Kind, f.Id)).ToHashSet();
            for (uint id = FighterTemplate.Strength; id <= FighterTemplate.Self; id++)
            {
                Assert.Contains((MageStatKind.Attribute, id), floor);
            }

            Assert.Contains((MageStatKind.Vital, FighterTemplate.MaxHealth), floor);
            Assert.Contains((MageStatKind.Vital, FighterTemplate.MaxStamina), floor);
            Assert.Contains((MageStatKind.Vital, FighterTemplate.MaxMana), floor);
            foreach (var skill in t.SpecializedAtCreation.Concat(t.TrainedAtCreation).Concat(t.TrainedLater).Concat(FighterTemplate.FreeTrainedSkills))
            {
                Assert.Contains((MageStatKind.Skill, skill), floor);
            }

            Assert.Equal(floor.Count, t.FloorStats.Count);
            Assert.All(t.FloorStats, f => Assert.Equal(0, f.Percent));
            Assert.All(t.FloorStats, f => Assert.NotNull(t.TargetOf(f.Kind, f.Id)));
        }
    }
}

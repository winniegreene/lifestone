using DatReaderWriter.Enums;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The spell components the fighters' kit carries (M4 P5a fix F-abil, live round 1: Harm Self refused with 0x0400): <see cref="FighterComponents"/> on a synthetic SpellTable (every spell burns one slot each of a scarab, a herb, a powder,
/// a potion and a talisman, no account customisation), and the build granting and topping them up through the scripted ACE. The dats' own formulas are <c>FighterComponentsDatTests</c>.
/// </summary>
public sealed class FighterComponentsTests
{
    private const uint LeadScarab = 691;
    private const uint Hyssop = 774;
    private const string Account = "test-melee";

    /// <summary>A table of every spell either profile holds, each with the same one-slot-each formula and component loss 0.5 (formula version 0: no taper customisation).</summary>
    internal static Dictionary<uint, SpellInfo> SyntheticSpells()
    {
        var table = new Dictionary<uint, SpellInfo>();
        foreach (var profile in FighterProfile.All)
        {
            foreach (var line in profile.Lines)
            {
                foreach (var spell in line.Spells)
                {
                    table[spell.Id] = WorldActionRig.Spell(spell.Id, SpellType.Boost, SpellFlags.SelfTargeted) with
                    {
                        Name = spell.Name,
                        School = line.School,
                        Power = spell.Power,
                        FormulaVersion = 0,
                        ComponentLoss = 0.5f,
                        Formula = [1, 7, 25, 37, 49],
                    };
                }
            }
        }

        return table;
    }

    [Fact]
    public void The_kit_covers_levels_I_to_IV_of_the_buffs_and_the_utilities_Harm_Self_I_to_III_and_Lifestone_Recall_at_the_0_9_chance_rule()
    {
        var profile = FighterProfile.Brawler;
        foreach (var line in profile.Lines)
        {
            var levels = FighterComponents.AllowedSpells(line).Select(s => s.Power).ToArray();
            if (line.Role == ProfileRole.HarmSelf)
            {
                Assert.Equal<uint>([15, 75, 125], levels); // Harm Self IV is power 175: a chance of 0.88 at Life Magic 183 + 20, under 0.9
            }
            else if (line.Role == ProfileRole.Travel)
            {
                Assert.Equal<uint>([100], levels); // Lifestone Recall: Item Enchantment 161 + 20 reaches power 100 only
            }
            else
            {
                Assert.Equal<uint>([1, 50, 100, 150], levels.Take(4).ToArray()); // I to IV: IV (power 150) needs the 182 the fighters are built to
                Assert.DoesNotContain(200u, levels); // V (power 200) is a chance of 0.55
                Assert.All(levels, p => Assert.True(GearCasts.Chance(FighterComponents.PlannedSkill(line.School) + FighterComponents.SkillAllowance, p) >= FighterComponents.CastChance));
            }
        }
    }

    [Fact]
    public void The_utility_lines_are_covered_at_every_learned_level_and_the_buff_lines_at_the_plans_P5a_r3()
    {
        // Round 3: the Brawler's live skill (gear and buffs) chose Harm Self V and Stamina to Mana Self VI, which the plan-level kit did not cover (0x0400).
        foreach (var profile in new[] { FighterProfile.Brawler, FighterProfile.Archer })
        {
            var known = new HashSet<uint>(profile.SpellsToLearn);
            foreach (var line in profile.UtilityLines.Where(l => l.Role is ProfileRole.HarmSelf or ProfileRole.HealSelf || l.IsManaLine))
            {
                var covered = FighterComponents.CoveredSpells(line, known);
                Assert.Equal(line.Spells.Where(s => known.Contains(s.Id)).Select(s => s.Id), covered.Select(s => s.Id));
                Assert.Superset(FighterComponents.AllowedSpells(line).Where(s => known.Contains(s.Id)).Select(s => s.Id).ToHashSet(), covered.Select(s => s.Id).ToHashSet());
            }

            Assert.Contains(FighterComponents.CoveredSpells(profile.HarmSelf, known), s => s.Power == 225); // Harm Self V
            Assert.Contains(FighterComponents.CoveredSpells(profile.StaminaToMana, known), s => s.Id == 1681); // Stamina to Mana Self VI
        }
    }

    [Fact]
    public void The_stack_of_a_component_is_the_gates_expected_burn_times_the_safety_up_to_the_weenies_maximum_and_never_under_the_minimum()
    {
        var lines = FighterComponents.For(FighterProfile.Brawler, SyntheticSpells(), Account);

        // Five components, one slot each: a scarab (CDM 0.1), a herb, a powder, a potion (CDM 1) and a talisman (CDM 0.1).
        Assert.Equal(5, lines.Count);
        Assert.All(lines, l => Assert.Equal(FighterRole.Component, l.Role));
        Assert.All(lines, l => Assert.Equal(GearKit.WeenieTypeSpellComponent, l.Item.WeenieType));

        // Per cast a slot burns 0.5 x CDM. Casts per gate: the 12 buff lines 3 cycles each, the 2 mana lines (3 + 3 legs) x 8 casts each, Harm Self 3 legs x 12, Heal Self 3 x 6, Lifestone Recall 6.
        var casts = 12 * FighterComponents.BuffCycles + 2 * (FighterComponents.BuffCycles + FighterComponents.HarmHealLegs) * FighterComponents.ManaCastsPerLine + (FighterComponents.HarmHealLegs * FighterComponents.HarmCastsPerLeg)
            + (FighterComponents.HarmHealLegs * FighterComponents.HealCastsPerLeg) + FighterComponents.RecallCasts;
        Assert.Equal(12 * 3 + 2 * 6 * 8 + 36 + 18 + 6, casts); // 192
        var scarab = lines.Single(l => l.Wcid == LeadScarab);
        Assert.Equal((int)Math.Ceiling(FighterComponents.Safety * casts * 0.5 * 0.1), scarab.Item.Stack); // 29
        Assert.Equal(29, scarab.Item.Stack);

        // A herb burns ten times as fast: 288 would pass the weenie's maximum stack of 100, so 100.
        var herb = lines.Single(l => l.Wcid == Hyssop);
        Assert.Equal(100, herb.Item.Stack);
        Assert.All(lines, l => Assert.True(l.Item.Stack <= l.Item.MaxStack && l.Item.Stack >= FighterComponents.MinimumStack));
        Assert.Equal(lines.Select(l => l.Wcid).Distinct().Count(), lines.Count);
        Assert.All(lines, l => Assert.Equal(GearKit.ComponentBurden, l.Item.UnitBurden));
    }

    [Fact]
    public void A_profile_spell_the_table_lacks_and_a_component_the_map_lacks_fail_by_name()
    {
        var table = SyntheticSpells();
        table.Remove(1168); // Harm Self III: a spell the kit covers (IV, power 175, is outside the chance rule and is not asked for)
        var missing = Assert.Throws<KeyNotFoundException>(() => FighterComponents.For(FighterProfile.Archer, table, Account));
        Assert.Contains("1168", missing.Message, StringComparison.Ordinal);

        var odd = SyntheticSpells();
        odd[2] = odd[2] with { Formula = [1, 150] }; // 150 is one of the Pea family the map does not hold
        var unmapped = Assert.Throws<InvalidOperationException>(() => FighterComponents.For(FighterProfile.Brawler, odd, Account));
        Assert.Contains("component map", unmapped.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => FighterComponents.For(FighterProfile.Brawler, SyntheticSpells(), ""));
    }

    [Fact]
    public void WithComponents_adds_the_lines_after_the_pack_replaces_an_earlier_set_and_leaves_the_template_kits_as_they_were()
    {
        var one = FighterComponents.For(FighterProfile.Brawler, SyntheticSpells(), Account);
        var kit = FighterKit.Melee.WithComponents(one);

        Assert.Empty(FighterKit.Melee.Components);
        Assert.Equal(FighterKit.Melee.Items.Count + one.Count, kit.Items.Count);
        // M4 P5a fix r4 (D28): the side-pack line's count is sized from the lines (FighterKit.SidePacksFor); every other line of the template kit is unchanged.
        Assert.Equal(FighterKit.Melee.Items.Where(i => i.Role != FighterRole.SidePack), kit.Items.Take(FighterKit.Melee.Items.Count).Where(i => i.Role != FighterRole.SidePack));
        Assert.Equal(FighterKit.SidePacksFor(one.Count), kit.SidePacks);
        Assert.Equal(one.Count, kit.Components.Count());
        Assert.Equal(FighterKit.Melee.TotalBurden + one.Sum(l => l.Item.Burden) + (kit.SidePacks - FighterKit.Melee.SidePacks) * 15, kit.TotalBurden); // fix r4: the added packs weigh 15 each
        Assert.Equal(kit.Items, kit.WithComponents(one).Items); // idempotent
        Assert.Equal(FighterKit.Melee.Items.Count + 1, kit.WithComponents([one[0]]).Items.Count); // a second set replaces the first
        Assert.Equal(FighterKit.Melee.Worn.Select(i => i.Wcid), kit.Worn.Select(i => i.Wcid)); // nothing about what is worn or wielded changed
        Assert.True(kit.HasCaster);
    }

    // ------------------------------------------------------------------------------------------------ the build

    private static Dictionary<uint, SpellInfo> Table => SyntheticSpells();

    /// <summary>A Run of the fighter build over the scripted ACE with the components: the fighter is built to level 55 and holds <paramref name="held"/> of every component stack at the start.</summary>
    private sealed class BuildRun : IDisposable
    {
        private readonly IReadOnlyList<FighterItem> _components;

        public BuildRun(FighterTemplate template, Func<int, int> held, Action<ScriptedFighter, IReadOnlyList<FighterItem>>? arrange = null)
        {
            _components = FighterComponents.For(FighterProfile.For(template)!, Table, Account);
            var table = ScriptedMage.SyntheticTable();
            Scenario = new FighterBuildScenario(table, HeadlessCharacterTests.StandInClasses(), Log, template: template, spells: Table, account: Account);
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 900 },
                Scenario,
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => World,
                Time,
                Log,
                Recording,
                Sockets.Open,
                (_, sink, tap) =>
                {
                    Server = new ScriptedAceSession(sink, tap, Time);
                    Server.Characters.Insert(0, new Lifestone.Tests.Shared.Net.AceCharacter(Guid, template.Name));
                    Fighter = ScriptedFighter.Ready(table, Server, template.Name).WithComponents(_components, held: false);
                    for (var i = 0; i < _components.Count; i++)
                    {
                        var count = held(i);
                        if (count > 0)
                        {
                            Fighter.AddOther(_components[i].Item with { Stack = count });
                        }
                    }

                    arrange?.Invoke(Fighter, _components);
                    Fighter.Attach();
                    return Server;
                });
        }

        public const uint Guid = 0x50000031;

        public IReadOnlyList<FighterItem> Components => _components;

        public FighterBuildScenario Scenario { get; }

        public RecordingLogSink Log { get; } = new();

        public ManualTimeProvider Time { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public ScriptedFighter Fighter { get; private set; } = null!;

        public HeadlessRunResult Finish(double maxSeconds = 900)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
                if (Server.PlayerGuid is not null)
                {
                    Fighter.Pump();
                }
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage}, steps {string.Join(" | ", Scenario.SentSteps)})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_with_no_components_is_granted_every_line_once_and_a_rerun_asks_for_nothing(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        using var run = new BuildRun(template, _ => 0);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var steps = run.Scenario.SentSteps.Where(s => s.StartsWith("item ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(run.Components.Count, steps.Length);
        foreach (var line in run.Components)
        {
            Assert.Contains(steps, s => s.Contains($"(weenie {line.Wcid}) x {line.Item.Stack}, held 0 of {line.Item.Stack}", StringComparison.Ordinal));
            Assert.Equal(line.Item.Stack, run.Fighter.HeldCount(line.Wcid));
        }

        Assert.Contains(" kit=ok ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_rerun_after_casts_burned_some_tops_each_stack_up_by_the_missing_count_only_and_a_full_kit_asks_for_nothing()
    {
        var components = FighterComponents.For(FighterProfile.Brawler, Table, Account);

        // A built fighter that has cast: the first component line is gone, the second is 3 short, the rest are full.
        using var again = new BuildRun(FighterTemplate.Melee, i => i == 0 ? 0 : i == 1 ? components[1].Item.Stack - 3 : components[i].Item.Stack);
        var result = again.Finish();

        Assert.True(result.Ok, result.Failure + " / " + again.Scenario.Failure);
        var steps = again.Scenario.SentSteps.Where(s => s.StartsWith("item ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, steps.Length); // only the two short lines
        Assert.Contains($"(weenie {components[0].Wcid}) x {components[0].Item.Stack}, held 0 of {components[0].Item.Stack}", steps[0], StringComparison.Ordinal);
        Assert.Contains($"(weenie {components[1].Wcid}) x 3, held {components[1].Item.Stack - 3} of {components[1].Item.Stack}", steps[1], StringComparison.Ordinal);
        Assert.StartsWith("fighter=built ", again.Scenario.VerdictFields(), StringComparison.Ordinal);

        using var full = new BuildRun(FighterTemplate.Melee, i => components[i].Item.Stack);
        var fullResult = full.Finish();
        Assert.True(fullResult.Ok, fullResult.Failure + " / " + full.Scenario.Failure);
        Assert.DoesNotContain(full.Scenario.SentSteps, s => s.StartsWith("item ", StringComparison.Ordinal));
        // M4 P5a C6: the rig holds the component stacks in the main pack, so the full kit still moves them into the side packs (a build step, not a grant): built, with no grant.
        Assert.StartsWith("fighter=built ", full.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.All(full.Scenario.SentSteps.Where(s => !s.StartsWith("put the ", StringComparison.Ordinal) && !s.StartsWith("item ", StringComparison.Ordinal)), s => Assert.DoesNotContain("component", s, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_without_potions_is_granted_100_mana_and_50_stamina_potions_once_into_a_side_pack_and_a_full_stock_asks_for_nothing_M4_P5a_C7(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var components = FighterComponents.For(FighterProfile.For(template)!, Table, Account);
        using var bare = new BuildRun(template, i => components[i].Item.Stack, (f, _) => { f.DropItems(FighterKit.ManaPotionWcid); f.DropItems(FighterKit.StaminaPotionWcid); });
        var result = bare.Finish();

        Assert.True(result.Ok, result.Failure + " / " + bare.Scenario.Failure);
        var items = bare.Scenario.SentSteps.Where(s => s.StartsWith("item ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, items.Length); // only the two potion lines
        var (manaCount, staminaCount) = key == "melee" ? (FighterKit.ManaPotionCount, FighterKit.StaminaPotionCount) : (FighterKit.ArcherManaPotionCount, FighterKit.ArcherStaminaPotionCount);
        Assert.Contains($"(weenie {FighterKit.ManaPotionWcid}) x {manaCount}, held 0 of {manaCount}", items[0], StringComparison.Ordinal);
        Assert.Contains($"(weenie {FighterKit.StaminaPotionWcid}) x {staminaCount}, held 0 of {staminaCount}", items[1], StringComparison.Ordinal);
        Assert.Equal(manaCount, bare.Fighter.HeldCount(FighterKit.ManaPotionWcid));
        Assert.Equal(staminaCount, bare.Fighter.HeldCount(FighterKit.StaminaPotionWcid));
        Assert.Contains(bare.Scenario.SentSteps, s => s.StartsWith("put the Mana Potion into the side pack", StringComparison.Ordinal)); // never in the main pack (the headroom rule)
        Assert.Contains(bare.Scenario.SentSteps, s => s.StartsWith("put the Stamina Potion into the side pack", StringComparison.Ordinal));

        using var full = new BuildRun(template, i => components[i].Item.Stack);
        var fullResult = full.Finish();
        Assert.True(fullResult.Ok, fullResult.Failure + " / " + full.Scenario.Failure);
        Assert.DoesNotContain(full.Scenario.SentSteps, s => s.StartsWith("item ", StringComparison.Ordinal));
        Assert.Contains(" kit=ok ", full.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_rerun_after_potions_were_drunk_tops_the_stack_up_by_the_missing_count_and_merges_it_into_the_side_pack_stack_M4_P5a_C7()
    {
        var template = FighterTemplate.Melee;
        var components = FighterComponents.For(FighterProfile.Brawler, Table, Account);
        var mana = FighterKit.Melee.VitalPotions.Single(l => l.Wcid == FighterKit.ManaPotionWcid);
        using var run = new BuildRun(template, i => components[i].Item.Stack, (f, _) => { f.DropItems(FighterKit.ManaPotionWcid); f.AddInSidePack(mana.Item, 60, 0); });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var items = run.Scenario.SentSteps.Where(s => s.StartsWith("item ", StringComparison.Ordinal)).ToArray();
        Assert.Single(items);
        Assert.Contains($"(weenie {FighterKit.ManaPotionWcid}) x 40, held 60 of {FighterKit.ManaPotionCount}", items[0], StringComparison.Ordinal);
        Assert.Contains(run.Scenario.SentSteps, s => s.StartsWith("merge the Mana Potion x 40 into its stack", StringComparison.Ordinal));
        Assert.Equal(FighterKit.ManaPotionCount, run.Fighter.HeldCount(FighterKit.ManaPotionWcid));
    }

    [Fact]
    public void The_potions_take_no_main_pack_slot_weigh_2250_and_1050_and_the_sale_rule_never_sells_them_M4_P5a_C7()
    {
        foreach (var kit in new[] { FighterKit.Melee, FighterKit.Archer })
        {
            Assert.Equal(2, kit.VitalPotions.Count());
            Assert.Equal(kit == FighterKit.Melee ? 2_250 : 1_050, kit.VitalPotions.Sum(l => l.Item.Burden)); // 15 each: 100 + 50 for the Brawler, 60 + 10 for the Archer (its capacity)
            Assert.DoesNotContain(kit.Items.Where(i => i.Role == FighterRole.VitalPotion), l => kit.MainPackSlots == 0 && l.Wcid == 0);
            Assert.All(kit.VitalPotions, l => Assert.Contains(l.Wcid, SellRules.KeptWeenies));
        }
    }

    [Fact]
    public void The_side_packs_are_sized_from_the_component_lines_with_one_spare_and_both_fighters_carry_three_P5a_r4()
    {
        Assert.Equal(1, FighterKit.SidePacksFor(0));
        Assert.Equal(2, FighterKit.SidePacksFor(24));
        Assert.Equal(3, FighterKit.SidePacksFor(44)); // the Brawler's lines after r3 (FighterComponentsDatTests prints the live count)
        Assert.Equal(3, FighterKit.SidePacksFor(45)); // the Archer's
        Assert.Equal(4, FighterKit.SidePacksFor(49));
        var one = FighterComponents.For(FighterProfile.Brawler, SyntheticSpells(), Account);
        Assert.Equal(FighterKit.SidePacksFor(one.Count), FighterKit.Melee.WithComponents(one).SidePacks);
    }

    [Fact]
    public void A_rerun_with_full_side_packs_of_unmerged_top_ups_merges_every_line_into_one_stack_instead_of_failing_pack_full_P5a_r4()
    {
        // Round 4 (FighterBrawlerBuildLoopbackTests): every side pack full of the lines and earlier reruns' top-up stacks (the item tool creates a new stack), the lines one short:
        // the top-ups landed in the main pack and the build failed "pack-full: no side pack has room for the Gold Scarab (3 side packs of 24 slots hold the components)".
        var components = FighterComponents.For(FighterProfile.Brawler, Table, Account);
        var big = components.Select((l, i) => (l, i)).MaxBy(p => p.l.Item.Stack).i;
        using var run = new BuildRun(FighterTemplate.Melee, _ => 0, (f, lines) =>
        {
            var packs = f.Items.Where(y => y.Kind.Wcid == FighterKit.SidePackWcid).Select(y => y.Guid).ToArray();
            var fragments = packs.Length * FighterKit.SidePackSlots - lines.Count;
            Assert.True(lines[big].Item.Stack - 1 - fragments >= 1, "the rig's biggest line holds the fragments");
            for (var i = 0; i < lines.Count; i++)
            {
                f.AddInSidePack(lines[i].Item, lines[i].Item.Stack - 1 - (i == big ? fragments : 0), i * packs.Length / lines.Count);
            }

            // The fragments of the biggest line fill every slot left, pack by pack.
            var left = fragments;
            for (var p = 0; p < packs.Length && left > 0; p++)
            {
                var here = Math.Min(FighterKit.SidePackSlots - f.Items.Count(x => x.Container == packs[p]), left);
                f.AddInSidePack(lines[big].Item, 1, p, here);
                left -= here;
            }

            Assert.All(packs, g => Assert.Equal(FighterKit.SidePackSlots, f.Items.Count(x => x.Container == g)));
        });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Equal(components.Count, run.Scenario.SentSteps.Count(s => s.StartsWith("item ", StringComparison.Ordinal))); // one top-up of 1 per line
        Assert.True(run.Fighter.MergesSeen >= components.Count, $"merges {run.Fighter.MergesSeen}");
        Assert.Contains(run.Scenario.SentSteps, s => s.StartsWith("merge the ", StringComparison.Ordinal));
        foreach (var line in components)
        {
            var stacks = run.Fighter.Items.Where(i => i.Kind.Wcid == line.Wcid).ToArray();
            Assert.Equal(line.Item.Stack, stacks.Sum(x => x.Stack));
            Assert.Single(stacks); // one stack per line again: the fragments are merged
            Assert.NotEqual(0u, stacks[0].Container); // in a side pack
        }

        Assert.Equal(FighterKit.SidePacksFor(components.Count), run.Fighter.Items.Count(i => i.Kind.Wcid == FighterKit.SidePackWcid));
    }

    [Fact]
    public void A_fighter_missing_its_side_packs_is_granted_them_and_the_lines_go_into_them_P5a_r4()
    {
        using var run = new BuildRun(FighterTemplate.Archer, _ => 0, (f, _) => f.DropItems(FighterKit.SidePackWcid));
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var packs = FighterKit.SidePacksFor(run.Components.Count);
        Assert.Equal(packs, run.Scenario.SentSteps.Count(s => s.StartsWith("item Pack (weenie 136)", StringComparison.Ordinal)));
        Assert.Equal(packs, run.Fighter.Items.Count(i => i.Kind.Wcid == FighterKit.SidePackWcid));
        Assert.All(run.Components, line => Assert.NotEqual(0u, run.Fighter.Items.Single(i => i.Kind.Wcid == line.Wcid).Container));
    }

    [Fact]
    public void A_short_component_is_named_in_the_kit_field_as_name_held_over_needed()
    {
        // The server grants nothing for the component the test refuses: the build fails by name and the verdict names the short line.
        using var run = new BuildRun(FighterTemplate.Melee, i => 0);
        var first = run.Components[0];
        run.Fighter.UnknownWcids.Add(first.Wcid);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains(first.Item.Name, run.Scenario.Failure ?? result.Failure ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains($"{first.Item.Name.Replace(' ', '_')}:0/{first.Item.Stack}", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_build_given_the_table_without_the_account_or_the_account_without_the_table_is_refused_and_one_given_neither_logs_the_TODO()
    {
        Assert.Throws<ArgumentException>(() => new FighterBuildScenario(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses(), new RecordingLogSink(), template: FighterTemplate.Melee, spells: Table));
        Assert.Throws<ArgumentException>(() => new FighterBuildScenario(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses(), new RecordingLogSink(), template: FighterTemplate.Melee, account: Account));

        var log = new RecordingLogSink();
        var plain = new FighterBuildScenario(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses(), log, template: FighterTemplate.Archer);
        Assert.Contains(log.Lines, l => l.Contains("TODO(m4-fighter-components)", StringComparison.Ordinal));
        Assert.Empty(plain.Kit.Components);

        var hunterLog = new RecordingLogSink();
        _ = new FighterBuildScenario(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses(), hunterLog);
        Assert.DoesNotContain(hunterLog.Lines, l => l.Contains("TODO(m4-fighter-components)", StringComparison.Ordinal)); // the Hunter has no spells and needs no components
    }
}

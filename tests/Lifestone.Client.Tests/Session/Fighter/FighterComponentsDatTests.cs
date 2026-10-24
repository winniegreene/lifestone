using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The fighters' spell components against the dats (M4 P5a fix F-abil): the real formulas of the covered spells, as the two test accounts cast them, are all in the kit, in stacks that cover one cast of each and stay inside the weenie's maximum;
/// the kit's burden and slots are printed against the fighters' capacity at Strength 100; and the planned skills the allowed levels rest on equal the planner's.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class FighterComponentsDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The burden a fighter must have free over its kit with components, for loot (M4 P5a C5).</summary>
    private const int LootRoom = 2_000;

    private static readonly (FighterProfile Profile, string Account)[] Fighters = [(FighterProfile.Brawler, FighterTemplate.MeleeAccount), (FighterProfile.Archer, FighterTemplate.ArcherAccount)];

    [RequiresDatsFact]
    public void The_planned_skills_the_allowed_levels_rest_on_are_the_planners_for_both_fighters()
    {
        var xp = XpTableData.Load(fixture.Dats);
        // M4 P5a C5: the fighters' creation attributes differ (Strength or Quickness 100, Focus 100), so their plans' schools do too (Brawler 184/181/184, Archer 183/179/183); the constants are the lower of the two, which both can cast.
        uint ce = uint.MaxValue, ie = uint.MaxValue, lm = uint.MaxValue;
        foreach (var t in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            var plan = FighterSpendingPlanner.ComputeLevel(xp, t.Level, t);
            ce = Math.Min(ce, FighterSpendingPlanner.MagicSkill(plan.Targets, FighterTemplate.CreatureEnchantment, t.Creation));
            ie = Math.Min(ie, FighterSpendingPlanner.MagicSkill(plan.Targets, FighterTemplate.ItemEnchantment, t.Creation));
            lm = Math.Min(lm, FighterSpendingPlanner.MagicSkill(plan.Targets, FighterTemplate.LifeMagic, t.Creation));
        }

        Assert.Equal((FighterComponents.CreatureEnchantmentSkill, FighterComponents.ItemEnchantmentSkill, FighterComponents.LifeMagicSkill), (ce, ie, lm));
    }

    [RequiresDatsFact]
    public void Every_component_of_every_covered_spell_in_the_account_s_formula_is_in_the_kit_in_a_stack_that_covers_one_cast_of_each_and_the_numbers_are_printed()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var (profile, account) in Fighters)
        {
            var lines = FighterComponents.For(profile, spells, account);
            var byWcid = lines.ToDictionary(l => l.Wcid);
            var known = new HashSet<uint>(profile.SpellsToLearn);
            foreach (var line in profile.Lines)
            {
                // P5a r3: the utility lines are covered at every learned level (their pickers choose by the live skill); the buff lines at the plan's.
                foreach (var allowed in line.IsBuff ? FighterComponents.AllowedSpells(line) : FighterComponents.CoveredSpells(line, known))
                {
                    var info = spells[allowed.Id];
                    var slots = new Dictionary<uint, int>();
                    foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account).Where(c => c != 0))
                    {
                        slots[id] = slots.GetValueOrDefault(id) + 1;
                    }

                    foreach (var (id, n) in slots)
                    {
                        Assert.True(ComponentWeenies.TryGetByComponent(id, out var weenie), $"{info.Name}: component {id} is not in the map");
                        Assert.True(byWcid.TryGetValue(weenie.Wcid, out var item), $"{profile.CharacterName}: {info.Name} needs {weenie.Name} and the kit has none");
                        Assert.True(item!.Item.Stack >= n, $"{info.Name} needs {n} {weenie.Name} for one cast and the kit holds {item.Item.Stack}");
                    }
                }
            }

            Assert.All(lines, l => Assert.True(l.Item.Stack <= l.Item.MaxStack && l.Item.Stack >= FighterComponents.MinimumStack));
            var template = profile.Template;
            var kit = template.Kit.WithComponents(lines);
            // M4 P5a C5: the kit is granted after the raises, so it is carried at the plan's Strength: at level 40 (the base plan with its floor) and at the template's level; each leaves room for loot.
            var xpTable = XpTableData.Load(fixture.Dats);
            var strength40 = template.CreationValue(FighterTemplate.Strength) + (uint)FighterSpendingPlanner.Compute(xpTable, FighterTemplate.Level40Xp, template.Shares, template.FloorStats).Targets.Single(x => x.Kind == MageStatKind.Attribute && x.Id == FighterTemplate.Strength).Ranks;
            var capacity40 = FighterKit.Capacity((int)strength40);
            var capacity = FighterKit.Capacity((int)template.AttributeAtTarget(FighterTemplate.Strength));
            output.WriteLine($"{profile.CharacterName} ({account}): {FighterComponents.Describe(lines)}; kit slots {kit.Items.Count}/{GearKit.PackCapacity}, burden {template.Kit.TotalBurden} + {lines.Sum(l => l.Item.Burden)} = {kit.TotalBurden}; capacity {capacity40} at level 40 (Strength {strength40}), {capacity} at level {template.Level} (Strength {template.AttributeAtTarget(FighterTemplate.Strength)})");
            // P5a r3: both fighters are built at their template's level (55, D40's re-creation), where the kit is granted after the raises; the level-40 capacity is printed, no longer judged (the utility lines' every-level
            // components put the Archer's kit over its level-40 Strength, a level it is never built at).
            Assert.Equal(FighterTemplate.MagicLevel, template.Level);
            output.WriteLine(kit.TotalBurden + LootRoom <= capacity40 ? $"level 40: fits ({kit.TotalBurden} + {LootRoom} of {capacity40})" : $"level 40: over ({kit.TotalBurden} + {LootRoom} of {capacity40}; never built there)");
            Assert.True(kit.TotalBurden + LootRoom <= capacity, $"level {template.Level}: {kit.TotalBurden} + {LootRoom} of {capacity}");
            foreach (var l in lines)
            {
                output.WriteLine($"  {l.Item.Name} ({l.Wcid}) x {l.Item.Stack}");
            }

            // M4 P5a C6 (D41): the live round 2 filled the main pack with the component stacks (102 slots, Container.cs:527). The components now go into the kit's side packs; the main pack keeps its own lines and at least the headroom free.
            Assert.True(kit.ComponentSlots <= (kit.SidePacks - FighterKit.SidePackSpare) * FighterKit.SidePackSlots, $"{kit.ComponentSlots} component stacks in {kit.SidePacks} side packs of {FighterKit.SidePackSlots} (one spare)");
            Assert.True(kit.MainPackSlots + FighterKit.MainPackHeadroom <= GearKit.PackCapacity, $"main pack {kit.MainPackSlots} + {FighterKit.MainPackHeadroom} free of {GearKit.PackCapacity}");
            output.WriteLine($"  slots: main pack {kit.MainPackSlots}/{GearKit.PackCapacity}, side packs {kit.ComponentSlots} of {kit.SidePacks} x {FighterKit.SidePackSlots}");
            Assert.True(kit.TotalBurden < capacity, $"{kit.TotalBurden} of {capacity}");
        }
    }

    [RequiresDatsFact]
    public void The_account_name_is_lower_cased_as_ACE_stores_it_and_the_covered_levels_match_the_real_powers()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equivalent(FighterComponents.For(FighterProfile.Brawler, spells, "test-melee"), FighterComponents.For(FighterProfile.Brawler, spells, "TEST-Melee"));

        // The profile's powers equal the dat's (FighterProfileDatTests pins the table); the allowed levels read the dat's own power.
        foreach (var line in FighterProfile.Archer.Lines.Where(l => FighterComponents.CastsPerGate(l) > 0))
        {
            foreach (var spell in line.Spells)
            {
                Assert.Equal(spell.Power, spells[spell.Id].Power);
            }
        }

        // Level IV of every buff line is covered, level V is not: the 0.9 chance rule at skill 183 + 20.
        foreach (var line in FighterProfile.Brawler.Lines.Where(l => l.IsBuff))
        {
            var allowed = FighterComponents.AllowedSpells(line);
            Assert.Contains(allowed, s => s.Power == 150);
            Assert.DoesNotContain(allowed, s => s.Power == 200);
        }
    }
}

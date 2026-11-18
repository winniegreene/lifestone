using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.Quartermaster.QuartersState;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5e3 (a live defect, ladder67, 2026-11-17 23:55 to 2026-11-18 01:02 UTC: the climber at level 82 on the Tusker Quarters cast the fire line holding a 'Piercing Sceptre' while the 'Minor Smoldering Atlan
/// Wand' she forged sat in her pack): the wear judge reads the element the fight casts on the ground (<see cref="CastElement"/>, <see cref="Wardrobe.ElementOn"/>), where it read a rung's label and a plan's family that are
/// both empty past a ground's own rows.
/// <para>The state and what it assumes: <see cref="QuartersState"/>.</para>
/// </summary>
public sealed class WardrobeElementTests
{
    [Fact]
    public void At_level_82_on_the_Quarters_the_judge_reads_fire_from_the_fights_own_choice_where_the_rung_and_the_plan_named_none()
    {
        var quarters = GroundCatalog.Get("tusker-quarters");
        var rig = MageOf(82, 379, Book(6, fireSeven: true));
        var spells = Spells();

        // Before: the rung of level 82 is the Holding's (the Quarters are its alternate) and the Quarters' plan ends at level 74: no element, so no caster's bonus on her bolts counted (the live log's line).
        Assert.Equal(0u, WornRestorer.ElementFor(quarters, 82, out var before));
        Assert.Contains("TODO(m5-self-equip-element)", before, StringComparison.Ordinal);
        Assert.Contains("no ladder rung of level 82 names tusker-quarters", before, StringComparison.Ordinal);

        // Now: the fight's own choice. Both Tusker Slaves take the fire line's VII hardest from her book (their resistance to fire and to pierce is the same; the fire line has the higher level).
        var wardrobe = Wardrobe.For(rig.State);
        Assert.Equal(Fire, wardrobe.ElementOn(quarters, spells, out var source));
        Assert.Contains("the fight's own choice on tusker-quarters: fire deals 100% of her damage there", source, StringComparison.Ordinal);
        Assert.Contains("Tusker Slave (1628): Flame Bolt VII", source, StringComparison.Ordinal);
        wardrobe.JudgeOn(quarters, spells, out var read);
        Assert.Equal(Fire, read.Element);
        Assert.NotNull(read.Caster.Bolt); // the bolt is read with the element: the mana term runs the mana model, where it stood in for it

        // One function: the fight's pick for each target of the ground is of that element.
        foreach (var target in quarters.Targets.Where(t => t.Role == GroundRole.Target))
        {
            Assert.Equal("fire", CastElement.BestOn(rig.State, target.Wcid, id => spells.TryGetValue(id, out var s) ? s : null, wardrobe.KeepCastChance, FightScenario.Levels)!.Element);
        }

        // The gear keeps and the rearm read the same element.
        wardrobe.StateGround(quarters, spells);
        Assert.Equal(Fire, wardrobe.RearmJudge().Element);
    }

    [Fact]
    public void At_level_82_on_the_Quarters_the_pick_takes_the_Atlan_wand_over_the_piercing_sceptre_she_held()
    {
        var quarters = GroundCatalog.Get("tusker-quarters");
        var rig = MageOf(82, 379, Book(6, fireSeven: true));
        var spells = Spells();
        var sceptre = Sceptre(rig);
        var wand = AtlanWand(rig);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var worn = SelfEquipJudge.Worn(rig.State);

        // Before: the judge of no element (what every site was given at level 82 on the Quarters) kept the sceptre: its Mana Conversion and its melee defence outscore the wand's, whose fire bonus counted nothing.
        var blind = new SelfEquipJudge([], 0, id => spells.TryGetValue(id, out var s) ? s.Name : null, null, null, CasterTerms.OfLadder(rig.State, spells, 0, out _));
        Assert.Null(blind.BestCaster(PackCasters(rig), worn, skills, out var kept));
        Assert.Contains("keep 'Piercing Sceptre'", kept, StringComparison.Ordinal);
        Assert.Contains("on the plan's element 0x0", kept, StringComparison.Ordinal);

        // Now: the wand, by the judge every site asks.
        var judge = Wardrobe.For(rig.State).JudgeOn(quarters, spells);
        var pick = judge.BestCaster(PackCasters(rig), worn, skills, out var why);
        Assert.NotNull(pick);
        Assert.Equal(wand, pick.Guid);
        Assert.Equal(sceptre, pick.Replaces);
        Assert.Contains("'Minor Smoldering Atlan Wand'", why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_element_the_judge_reads_is_the_fights_on_every_ground_and_every_level_of_the_ladder()
    {
        var spells = Spells();
        SpellInfo? Spell(uint id) => spells.TryGetValue(id, out var s) ? s : null;
        var ladder = LadderLoader.WarMage;
        var checkedGrounds = 0;

        // Two books: the climber late (every line to VI, the fire line's VII, War Magic 379) and early (every line to II, War Magic 120).
        foreach (var (war, levels, seven) in new[] { (379, 6, true), (120, 2, false) })
        {
            for (var level = 1; level <= ladder.TopLevel; level++)
            {
                var rig = MageOf(level, war, Book(levels, seven));
                var wardrobe = Wardrobe.For(rig.State);
                foreach (var ground in GroundCatalog.All)
                {
                    var targets = ground.Targets.Where(t => t.Role == GroundRole.Target).Select(t => t.Wcid).Concat(ground.Outdoor?.Creatures.Where(c => c.SpawnMass > 0).Select(c => c.Wcid) ?? []).Distinct().ToList();
                    if (targets.Count == 0)
                    {
                        continue;
                    }

                    checkedGrounds++;
                    var element = wardrobe.ElementOn(ground, spells, out var source);
                    Assert.True(element != 0, $"{ground.Key} at level {level}: no element ({source})"); // the defect: no ground she fights on reads none, at any level
                    Assert.StartsWith("the fight's own choice on " + ground.Key, source, StringComparison.Ordinal);
                    var name = CasterTerms.ElementName(element)!;
                    var first = BoltElement.ElementOf(ground.PlanFor(level)?.Family ?? string.Empty) ?? "fire";
                    var picks = targets.Select(w => CastElement.BestOn(rig.State, w, Spell, wardrobe.KeepCastChance, FightScenario.Levels, null, first)!).Where(p => p.Expected > 0).ToList();
                    Assert.Contains(picks, p => p.Element == name); // the judge's element is a line the fight casts there
                    if (picks.Select(p => p.Element).Distinct().Count() == 1)
                    {
                        Assert.Equal(picks[0].Element, name); // and the only one, where every creature takes the same line hardest
                    }
                    else
                    {
                        // more than one line on the ground: the judge's is the one that deals most of her damage, by each kind's maximum health (and an outdoor ground's spawn mass)
                        var shares = Shares(ground, rig.State, Spell, wardrobe.KeepCastChance, first);
                        Assert.Equal(shares.Values.Max(), shares[name], 6);
                    }
                }
            }
        }

        Assert.True(checkedGrounds > 0);
    }

    private static Dictionary<string, double> Shares(HuntingGround ground, GameState state, Func<uint, SpellInfo?> spell, double chance, string first)
    {
        var shares = new Dictionary<string, double>();
        var seen = new HashSet<uint>();
        void Add(uint wcid, int health, double mass)
        {
            if (seen.Add(wcid) && CastElement.BestOn(state, wcid, spell, chance, FightScenario.Levels, null, first) is { Expected: > 0 } pick)
            {
                var h = Bestiary.Find(wcid)?.Vitals.MaxHealth is { } b and > 0 ? b : health;
                shares[pick.Element] = shares.GetValueOrDefault(pick.Element) + ((h > 0 ? h : 1) * mass);
            }
        }

        foreach (var t in ground.Targets.Where(t => t.Role == GroundRole.Target))
        {
            Add(t.Wcid, t.MaxHealth, 1.0);
        }

        foreach (var c in ground.Outdoor?.Creatures.Where(c => c.SpawnMass > 0) ?? [])
        {
            Add(c.Wcid, c.MaxHealth, c.SpawnMass);
        }

        return shares;
    }

    [Fact]
    public void A_ground_whose_creatures_take_different_lines_reads_the_line_that_deals_most_of_her_damage()
    {
        // The Crater Caves: the Nubilous Golem takes lightning hardest and the Magma Golem, the Ember and the Cursed Wisp other lines (BoltElement's own check against the simulator); the judge reads one of them, by health.
        var caves = GroundCatalog.Get("crater-caves");
        var rig = MageOf(80, 379, Book(6, fireSeven: true));
        var spells = Spells();
        var element = Wardrobe.For(rig.State).ElementOn(caves, spells, out var source);
        Assert.NotEqual(0u, element);
        Assert.Contains("the other lines:", source, StringComparison.Ordinal);
        Assert.Contains("TODO(m6-ground-kind-counts)", source, StringComparison.Ordinal); // an indoor profile holds no count by kind: each kind counts once, said
        var shares = Shares(caves, rig.State, id => spells.TryGetValue(id, out var s) ? s : null, Wardrobe.For(rig.State).KeepCastChance, "cold");
        Assert.True(shares.Count > 1);
        Assert.Equal(shares.Values.Max(), shares[CasterTerms.ElementName(element)!], 6);
    }

    [Fact]
    public void Where_the_fights_choice_is_not_known_the_rung_and_the_plan_answer_as_before_and_the_source_says_both()
    {
        // No bolt in her book: the fight has no line, so the rule of before stands (the outskirts' rung of level 6 is fire).
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        var rig = MageOf(6, 120, ImmutableDictionary<uint, float>.Empty.Add(HermeticLinkVI, 2f));
        Assert.Equal(Fire, Wardrobe.For(rig.State).ElementOn(outskirts, Spells(), out var source));
        Assert.Contains("the rung's element fire 0x10", source, StringComparison.Ordinal);
        Assert.Contains("the fight's own choice is not known: her book holds no bolt line castable", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fights_own_terms_govern_once_it_has_stated_them()
    {
        // A fight that caps its bolts at VI (a command line's cap) does not cast the fire line's VII: the Slaves then take the fire and the force line's VI alike, and the tie goes to the fight's first line.
        var quarters = GroundCatalog.Get("tusker-quarters");
        var rig = MageOf(82, 379, Book(6, fireSeven: true));
        var spells = Spells();
        var cast = CastElement.For(rig.State);
        Assert.False(cast.FoughtOn(quarters.Key));
        Assert.Contains("no fight has stated its cap there yet", cast.OnGround(quarters, spells, 0.95).Source, StringComparison.Ordinal);
        cast.StateFight(quarters.Key, 0.95, 6);
        Assert.True(cast.FoughtOn(quarters.Key));
        var stated = cast.OnGround(quarters, spells, 0.5);
        Assert.Equal("fire", stated.Element);
        Assert.Contains("Flame Bolt VI ", stated.Source, StringComparison.Ordinal);
        Assert.Contains("chance 0.95, levels up to VI: the terms the fight stated there this session", stated.Source, StringComparison.Ordinal);
    }
}

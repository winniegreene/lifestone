using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using static Lifestone.Client.Tests.Session.Quartermaster.QuartersState;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 16, slice Q4 (closes <c>TODO(m6-bolt-element-caster-mod)</c>): the fight's line for a target counts the caster in her hands (<see cref="CasterWar"/>, <see cref="WarFactor"/>), the one factor the caster score's war term
/// reads too. The numbers are the simulator's: <c>tools/fight-sim.ps1 matchup</c> on Qwinnie, level 82, War Magic 394, 2026-11-18, with the Minor Smoldering Atlan Wand in her hands and <c>--wield</c> her plain Orb
/// (no element, masterable), mean plain hits:
/// <list type="bullet">
/// <item>Stinging Chittick 4244 (fire 0.65, acid and pierce 1, the Holding): with the wand Ilservian's Flame 249.4, Flame Bolt VI 211.8, Acid Stream VI 138.0, Force Bolt VI 137.9; with the Orb Acid Stream VI 145.1, Force
/// Bolt VI 145.1, Ilservian's Flame 111.1, Flame Bolt VI 94.1. Fire resisted more, and still the hardest line with the wand.</item>
/// <item>Gotrok Lithos 24942 (fire 0.25, electric 1, the north forest): with the wand Lightning Bolt VI 138.0, Acid Stream VI 124.4, Ilservian's Flame 96.2; with the Orb Lightning Bolt VI 145.2, Ilservian's Flame 42.8.
/// The lightning line wins either way.</item>
/// </list>
/// The wand's Flame Bolt VI is x2.25 of the Orb's on both (211.8 / 94.1; 81.4 / 36.3) and every other line x1/1.05 (the wand is tough to master): the factor this rig reads is x1.12 x 2 / 1.05 = 2.133 (her Spirit Drinker is
/// not on her in the rig; live it adds 0.06: 1.18 x 2 / 1.05 = 2.248, the simulator's 2.25).
/// </summary>
public sealed class CasterWarTests
{
    private const uint Chittick = 4244, Gotrok = 24942;
    private const int WarMagic = 394;

    private static BoltPick Pick(VendorRig rig, uint wcid)
    {
        var spells = Spells();
        return CastElement.BestOn(rig.State, wcid, id => spells.TryGetValue(id, out var s) ? s : null, 0.95, FightScenario.Levels)!;
    }

    [Fact]
    public void The_wand_in_her_hands_is_the_factor_aces_bolt_takes_and_a_plain_orb_is_x1_on_every_line()
    {
        var withWand = MageOf(82, WarMagic, Book(6, fireSeven: true));
        AtlanWand(withWand, wielded: true);
        var wand = CasterWar.Held(withWand.State);
        Assert.True(wand.Read);
        Assert.False(wand.Plain);
        Assert.Equal(1.12 * 2.0 / GearScore.MasteryBonus, wand.On("fire"), 9); // elemental x1.12, cleaving x2, tough to master
        foreach (var other in new[] { "slash", "pierce", "bludgeon", "cold", "acid", "electric" })
        {
            Assert.Equal(1.0 / GearScore.MasteryBonus, wand.On(other), 9);
        }

        Assert.Contains("fire x2.133", wand.Note, StringComparison.Ordinal);

        var withOrb = MageOf(82, WarMagic, Book(6, fireSeven: true));
        Orb(withOrb);
        var orb = CasterWar.Held(withOrb.State);
        Assert.True(orb.Read);
        Assert.True(orb.Plain);
        Assert.False(CasterWar.Held(MageOf(82, WarMagic, Book(6, fireSeven: true)).State).Read); // no caster in her hands: x1, as before
    }

    [Fact]
    public void On_the_stinging_chittick_the_wand_keeps_her_on_the_fire_line_though_it_resists_fire_more()
    {
        // Before (and with the Orb): acid and pierce at 1 over fire at 0.65, so the fight left the fire line for another the wand does not double.
        var plain = MageOf(82, WarMagic, Book(6, fireSeven: true));
        Orb(plain);
        var before = Pick(plain, Chittick);
        Assert.NotEqual("fire", before.Element);
        Assert.Equal(1.0, before.Resist);
        Assert.Equal(1.0, before.Caster);

        // Now: the wand's x2.133 on fire makes Ilservian's Flame (the fire line's VII) the hardest, as the simulator's 249.4 over 138.0.
        var held = MageOf(82, WarMagic, Book(6, fireSeven: true));
        AtlanWand(held, wielded: true);
        var now = Pick(held, Chittick);
        Assert.Equal("fire", now.Element);
        Assert.Equal(6, now.Index);
        Assert.Equal(0.65, now.Resist);
        Assert.Equal(1.12 * 2.0 / GearScore.MasteryBonus, now.Caster, 9);
        Assert.Contains("caster x2.133", now.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void On_the_gotrok_lithos_the_lightning_line_wins_with_the_wand_in_hand_too()
    {
        // Fire 0.25: x2.133 does not lift the fire line's VII over the lightning line's VI at 1 (the simulator's 96.2 under 138.0 with the wand).
        foreach (var wand in new[] { false, true })
        {
            var rig = MageOf(82, WarMagic, Book(6, fireSeven: true));
            if (wand)
            {
                AtlanWand(rig, wielded: true);
            }
            else
            {
                Orb(rig);
            }

            var pick = Pick(rig, Gotrok);
            Assert.Equal("electric", pick.Element);
            Assert.Equal(wand ? 1.0 / GearScore.MasteryBonus : 1.0, pick.Caster, 9);
        }
    }

    [Fact]
    public void With_a_plain_caster_in_hand_every_pick_on_every_ground_of_the_ladder_is_the_pick_of_before()
    {
        var spells = Spells();
        SpellInfo? Spell(uint id) => spells.TryGetValue(id, out var s) ? s : null;
        var compared = 0;
        foreach (var (war, levels, seven) in new[] { (379, 6, true), (120, 2, false) })
        {
            var rig = MageOf(82, war, Book(levels, seven));
            Orb(rig);
            Assert.True(CasterWar.Held(rig.State).Plain);
            foreach (var ground in GroundCatalog.All)
            {
                var wcids = ground.Targets.Select(t => t.Wcid).Concat(ground.Outdoor?.Creatures.Select(c => c.Wcid) ?? []).Distinct();
                foreach (var wcid in wcids)
                {
                    var now = CastElement.BestOn(rig.State, wcid, Spell, 0.95, FightScenario.Levels);
                    var before = CastElement.BestOn(rig.State, wcid, Spell, 0.95, FightScenario.Levels, caster: CasterWar.None);
                    Assert.Equal(before, now); // the record's every field: element, level, spell, resistance, the hit to the bit, the factor 1
                    compared++;
                }
            }
        }

        Assert.True(compared > 100);
    }

    [Fact]
    public void With_the_wand_in_hand_the_wardrobes_element_is_a_line_the_fight_casts_on_every_ground()
    {
        var spells = Spells();
        SpellInfo? Spell(uint id) => spells.TryGetValue(id, out var s) ? s : null;
        var rig = MageOf(82, WarMagic, Book(6, fireSeven: true));
        AtlanWand(rig, wielded: true);
        var wardrobe = Wardrobe.For(rig.State);
        var checkedGrounds = 0;
        foreach (var ground in GroundCatalog.All)
        {
            var targets = ground.Targets.Where(t => t.Role == GroundRole.Target).Select(t => t.Wcid).Concat(ground.Outdoor?.Creatures.Where(c => c.SpawnMass > 0).Select(c => c.Wcid) ?? []).Distinct().ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            checkedGrounds++;
            var element = wardrobe.ElementOn(ground, spells, out var source);
            Assert.StartsWith("the fight's own choice on " + ground.Key, source, StringComparison.Ordinal);
            Assert.Contains("the caster in her hands: 'Minor Smoldering Atlan Wand'", source, StringComparison.Ordinal);
            var first = BoltElement.ElementOf(ground.PlanFor(82)?.Family ?? string.Empty) ?? "fire";
            var picks = targets.Select(w => CastElement.BestOn(rig.State, w, Spell, wardrobe.KeepCastChance, FightScenario.Levels, null, first)!).Where(p => p.Expected > 0).Select(p => p.Element).ToHashSet();
            Assert.Contains(CasterTerms.ElementName(element)!, picks);
        }

        Assert.Equal("fire", CasterTerms.ElementName(wardrobe.ElementOn(GroundCatalog.Get("tusker-holding"), spells, out _))); // the Holding: the Chittick and the tuskers on the fire line with the wand
        Assert.True(checkedGrounds > 0);
    }
}

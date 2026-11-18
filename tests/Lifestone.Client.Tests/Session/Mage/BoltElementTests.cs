using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 the element line (the owner, 2026-11-16, the Atlan wand; closes <c>TODO(m6-fight-element-line)</c>): the creature resistances are the world database's (<see cref="CreatureResists"/>), the line that hits a creature hardest is chosen
/// from her book by ACE's damage rule (<see cref="BoltElement"/>), a ground's hunted slots choose the line a job buys (<see cref="JobBolt"/>), and the learn plan takes a job's bolt line as an order line. The numbers are the simulator's
/// (<c>tools/fight-sim.ps1 matchup</c> on Qwinnie, level 78, War Magic 381, 2026-11-16): on the Nubilous Golem 23082 Lightning Bolt VI 143.7, Frost Bolt VI 122.5, Acid Stream VI 115.3 a plain hit, fire 0; on the Cursed Wisp 7126 Whirling Blade VI
/// 143.8, frost 0; on the Gout 21164 Frost Bolt VI first, fire 0. The simulator's hits carry a common factor of about 1.05 over <see cref="BoltElement.MeanHit"/> (her ratings), the same for every line, so the order is the same.
/// </summary>
public sealed class BoltElementTests
{
    private const uint Nubilous = 23082, Wisp = 7126, Gout = 21164, Magma = 199, Ember = 7607;
    private const uint NoRow = 999_999_999; // no creature weenie of the world database
    private const uint War = 381; // her buffed War Magic (fight-sim her, 2026-11-16)

    private static readonly uint[] Powers = [1, 50, 100, 150, 200, 250, 300, 400];

    private static SpellInfo? Spell(uint id)
    {
        foreach (var (element, ids) in WarSpellData.BoltLineIds)
        {
            var i = ids.ToList().IndexOf(id);
            if (i >= 0)
            {
                return WorldActionRig.Spell(id, SpellType.Projectile) with { Name = $"{SpellPlan.BoltLineNames[element]} {LevelFloor.Roman[Math.Min(i, 6)]}", Power = Powers[i], School = MagicSchool.WarMagic };
            }
        }

        return null;
    }

    /// <summary>Her book at level 78 (fight-sim her): level I of every line, Flame Bolt VI and VII, and the levels <paramref name="more"/> names.</summary>
    private static Func<uint, bool> Book(params uint[] more)
    {
        var known = new HashSet<uint>(WarSpellData.BoltLineIds.Values.Select(l => l[0])) { WarSpellData.BoltLineIds["fire"][5], WarSpellData.BoltLineIds["fire"][6] };
        known.UnionWith(more);
        return known.Contains;
    }

    private static uint Level(string element, int level) => WarSpellData.BoltLineIds[element][level - 1];

    [Fact]
    public void The_resistances_are_the_world_databases_and_a_creature_that_records_none_takes_every_element_whole()
    {
        Assert.Equal(6468, CreatureResists.Count); // 6,451 of weenie.type 10 and, from M6 16 E1e, the 17 cattle of weenie.type 15 (bestiary --check, 2026-11-17)
        Assert.Equal((0.0, 0.85, 1.0, 0.8), (CreatureResists.Of(Nubilous, "fire"), CreatureResists.Of(Nubilous, "cold"), CreatureResists.Of(Nubilous, "electric"), CreatureResists.Of(Nubilous, "acid")));
        Assert.Equal((0.0, 1.0, 0.9), (CreatureResists.Of(Wisp, "cold"), CreatureResists.Of(Wisp, "slash"), CreatureResists.Of(Wisp, "electric")));
        Assert.Equal((0.0, 0.65, 0.3), (CreatureResists.Of(Gout, "fire"), CreatureResists.Of(Gout, "cold"), CreatureResists.Of(Gout, "electric")));
        Assert.Equal((0.0, 1.0), (CreatureResists.Of(Magma, "fire"), CreatureResists.Of(Magma, "cold")));
        Assert.False(CreatureResists.Records(NoRow));
        Assert.Equal(1.0, CreatureResists.Of(NoRow, "fire"));
        Assert.Throws<ArgumentException>(() => CreatureResists.Of(Nubilous, "nether"));
    }

    [Fact]
    public void The_mean_hit_is_aces_mean_roll_and_skill_bonus()
    {
        Assert.True(WarSpellData.TryGet(Level("electric", 6), out var row));
        Assert.Equal(84 + 42 + (84 * 131 / 1000.0), BoltElement.MeanHit(row, War, 250), 6); // SpellProjectile.cs:527-537: MinDamage x (skill - power) / 1000
        Assert.Equal(84 + 42, BoltElement.MeanHit(row, 200, 250), 6); // no bonus under the power
    }

    [Fact]
    public void On_the_nubilous_golem_the_order_is_the_simulators_and_fire_does_nothing()
    {
        var lines = BoltElement.Lines(Nubilous, Spell, Book(Level("electric", 6), Level("cold", 6), Level("acid", 6)), War, 0.95, 7);
        Assert.Equal(["electric", "cold", "acid"], lines.Take(3).Select(l => l.Element));
        Assert.Equal(5, lines[0].Index); // VI
        Assert.Equal(0.0, lines.Single(l => l.Element == "fire").Expected);

        // her book today: level I of the six others and fire VI and VII: Lightning Bolt I (fight-sim: 31.1 a plain hit, the best she holds)
        var today = BoltElement.Best(Nubilous, Spell, Book(), War, 0.95, 7)!;
        Assert.Equal(("electric", 0), (today.Element, today.Index));
    }

    [Fact]
    public void A_wisp_takes_the_blade_and_a_gout_the_frost_and_the_tie_keeps_the_line_in_hand()
    {
        Assert.Equal("slash", BoltElement.Best(Wisp, Spell, Book(Level("slash", 6), Level("pierce", 6), Level("cold", 6)), War, 0.95, 7)!.Element);
        Assert.Equal("cold", BoltElement.Best(Gout, Spell, Book(Level("cold", 6), Level("electric", 6)), War, 0.95, 7)!.Element);
        Assert.Equal("cold", BoltElement.Best(Gout, Spell, Book(), War, 0.95, 7)!.Element); // Frost Bolt I, the one she holds: fight-sim 10.1 s a kill
        Assert.Equal("cold", BoltElement.Best(Ember, Spell, Book(Level("cold", 6)), War, 0.95, 7)!.Element);

        // a creature that records nothing takes every line whole: the fire VII in hand stays, and a tie keeps the line in hand
        Assert.Equal("fire", BoltElement.Best(NoRow, Spell, Book(), War, 0.95, 7, current: "fire")!.Element);
        Assert.Equal("cold", BoltElement.Best(NoRow, Spell, Book(Level("cold", 7), Level("fire", 7)), War, 0.95, 7, current: "cold")!.Element);
    }

    [Fact]
    public void The_cap_and_the_chance_are_the_fights()
    {
        var capped = BoltElement.Best(Gout, Spell, Book(Level("cold", 6), Level("cold", 7)), War, 0.95, 6)!;
        Assert.Equal(5, capped.Index); // the cap VI: never VII
        Assert.Null(BoltElement.Best(Gout, Spell, _ => false, War, 0.95, 7)); // nothing in the book
        Assert.Equal(0, BoltElement.Best(Gout, Spell, Book(Level("cold", 6)), 200, 0.95, 7)!.Index); // War 200 does not reach VI's power 250 at 0.95: Frost Bolt I
    }

    [Fact]
    public void The_crater_caves_hunted_slots_want_lightning_and_the_atlan_wand_needs_lightning_and_alchemy()
    {
        var element = JobBolt.ElementFor(GroundCatalog.Get("crater-caves"), out var why);
        Assert.Equal("electric", element);
        Assert.Contains("28 hunted slots", why, StringComparison.Ordinal);
        Assert.Contains("electric 27.6", why, StringComparison.Ordinal);
        Assert.Contains("cold 20.4", why, StringComparison.Ordinal);

        var atlan = QuestCatalog.Get("atlan-wand");
        var need = Assert.Single(JobBolt.Needs([atlan]));
        Assert.Equal(("atlan-wand", "crater-caves", "Lightning Bolt"), (need.Quest, need.Ground, need.Line));
        Assert.Equal([MageTemplate.Alchemy], JobBolt.Skills([atlan]));
        Assert.Empty(JobBolt.Needs([QuestCatalog.Get("minor-smoldering-stone")])); // no hunt: its Gouts are fought back with the best line she holds
    }

    [Fact]
    public void A_fire_ground_needs_no_job_line()
    {
        Assert.Equal("fire", JobBolt.ElementFor(GroundCatalog.Get("tusker-holding"), out _));
        Assert.Empty(JobBolt.Needs(QuestCatalog.All.Where(q => q.Steps.Any(s => s.Ground == "tusker-holding"))));
    }

    [Fact]
    public void The_learn_plan_takes_a_jobs_bolt_line_first_and_names_it_once()
    {
        var rung = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, "tusker-holding", 78)!;
        var lines = ScrivenerLearnPlan.LinesFor(rung, ScrivenerCatalog.Default, orderLines: ["Lightning Bolt"]);
        Assert.Equal("Lightning Bolt", lines[0].Name);
        Assert.Single(lines, l => l.Name == "Lightning Bolt");
        Assert.Single(ScrivenerLearnPlan.LinesFor(rung, ScrivenerCatalog.Default, orderLines: ["Flame Bolt"]), l => l.Name == "Flame Bolt"); // the rung's own line, named once
    }
}

using System.Text.Json.Nodes;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 drain resistance (live 2026-11-14 19:42 UTC, the Tusker Armory: the climber at level 65, several Armored Tuskers hitting about 8 health a second; the emergency picked Drain Health Other I, III and IV on three of them, each gave
/// 0, and she died 12 s later where Heal Self VI was worth about 9.4 health a second). ACE takes <c>round(current health x Proportion x drainMod)</c> before the cap (<c>WorldObject_Magic.cs</c> 719-726 @95ef426d), the mod the
/// target's <c>ResistHealthDrain</c> (float property 125): 0 for both Armored Tuskers (11540, 22611), read-only from the world copy. The fact is the profiles', read by kind (<see cref="DrainResist"/>); every heal pick reads it.
/// </summary>
public sealed class DrainResistTests
{
    private const uint ArmoredTusker = 11540;
    private const uint TuskHolder = 22611;
    private const uint HalfResistTusker = 22610; // the world copy's ResistHealthDrain 0.5 (weenie_properties_float row 322263); no profile lists it
    private const double SecondsPerMana = FightRates.SeedSecondsPerMana;
    private const double Weight = HealPick.DefaultDamageWeight;

    private static SpellInfo Info(uint id)
    {
        var spell = ScriptedCaster.DrainLine.Spells.First(s => s.Id == id);
        return WorldActionRig.Spell(id, SpellType.Transfer, SpellFlags.Resistable | SpellFlags.Reversed) with
        {
            Name = spell.Name,
            School = MagicSchool.LifeMagic,
            Power = spell.Power,
            BaseMana = spell.Position <= 6 ? 10u * (uint)spell.Position : 60u,
            BaseRangeConstant = 33f,
            BaseRangeMod = 0f,
        };
    }

    private static DrainLevel Level(uint id) => new(Info(id), DrainPicker.RowOf(id)!.Value, 1.0);

    private static HealCandidate HealSelfVI(uint missing) => new("Heal Self VI", 0, Math.Min(missing, MeasuredHeals.SelfSeed(6)), 0, 0.9, 45, MeasuredHeals.DrainSeed(6));

    private static int Pick(double resist, out HealCandidate[] drains)
    {
        // health 75 of 200 (125 missing), Life Magic 300 at level 65; the Armored Tusker at its full 435 with Magic Defense 275 (tusker-armory.json); Drain Health Other I, III and IV offered, as live
        var s = new DrainSituation(75, 200, 400, 300, 300, 435, 275, 3, resist);
        var heals = new MeasuredHeals();
        drains = [.. new uint[] { 1237, 1239, 1240 }.Select(id => Level(id)).Select(l => FightHealPick.DrainCandidate(in l, in s, 0x80006905, heals, out _))];
        var self = HealSelfVI(125);
        return HealPick.Choose(in self, true, drains, SecondsPerMana, Weight, out _, out _, out _);
    }

    [Fact]
    public void An_Armored_Tusker_at_435_gives_Drain_I_III_and_IV_nothing_so_the_heal_pick_takes_Heal_Self_VI()
    {
        Assert.Equal(0.0, DrainResist.Of(ArmoredTusker)); // the profiles' fact, the world copy's ResistHealthDrain

        var choice = Pick(DrainResist.Of(ArmoredTusker), out var drains);

        Assert.All(drains, d => Assert.Equal((0.0, 0.0), (d.Heal, d.Damage)));
        Assert.Equal(HealPick.HealSelf, choice);

        // the live mistake, reproduced: read at a resistance of 1 (TODO(m5-drain-resistance-mods) as it was), a drain outscored Heal Self VI
        var asBefore = Pick(1.0, out var before);
        Assert.True(asBefore >= 0, "at a resistance of 1 a drain wins the pick, as it did live");
        Assert.Equal(60.0, before[0].Heal); // level I on 435: 109 taken, cut to its cap 60, doubled to 120, cut to 60 (her 125 missing, the cap)
    }

    [Fact]
    public void Wcid_22610_at_half_resistance_halves_a_drains_take_before_the_cap()
    {
        // a profile that lists the half-resistant Armored Tusker (the Temple 40+'s, with a target added): the lookup reads it by kind
        var profile = (JsonObject)JsonNode.Parse(GroundCatalog.ResourceText("tusker-temple-fort-tethana.json")!)!;
        ((JsonArray)profile["targets"]!).Add(JsonNode.Parse($$"""{ "wcid": {{HalfResistTusker}}, "name": "Armored Tusker", "level": 100, "role": "target", "max_health": 435, "resist_health_drain": 0.5 }"""));
        var ground = GroundCatalog.Parse("tusker-temple-fort-tethana.json", profile.ToJsonString());
        var resist = DrainResist.Recorded([ground]);
        Assert.Equal(0.5, resist[HalfResistTusker]);
        Assert.False(resist.ContainsKey(1628)); // a kind at 1 is not written: the default

        // Drain Health Other V (0.3, cap 120, no loss), the mage missing 200: 300 health gives 90, at half 45; 400 gives the cap 120, at half round(400 x 0.3 x 0.5) = 60, under the cap: halved before it, as ACE does
        var five = Level(1241);
        var full = new DrainSituation(100, 300, 400, 300, 300, 300, 200, 3);
        var half = full with { TargetDrainResist = resist[HalfResistTusker] };
        Assert.Equal((90u, 45u), (DrainPicker.HealOf(in five, in full), DrainPicker.HealOf(in five, in half)));
        Assert.Equal((120u, 60u), (DrainPicker.HealOf(in five, full with { TargetHealth = 400 }), DrainPicker.HealOf(in five, half with { TargetHealth = 400 })));
        Assert.Equal(DrainPicker.HealOf(in five, full with { TargetHealth = 150 }), DrainPicker.HealOf(in five, in half)); // the same as a target of half the health

        // the drain picker and Harm Other read it the same: a pick on the half-resistant kind expects half; Harm Other IV's mean 24.5 is halved; at 0 nothing is cast through an obstacle
        var levels = new[] { Level(1237), Level(1240), five };
        uint[] floors = [.. levels.Select(l => GearCasts.ManaCeiling(l.Info, 0))];
        var pickFull = DrainPicker.Pick(levels, floors, in full);
        var pickHalf = DrainPicker.Pick(levels, floors, in half);
        Assert.True(pickHalf.ExpectedHeal < pickFull.ExpectedHeal, $"{pickHalf.ExpectedHeal} against {pickFull.ExpectedHeal}");
        var harm = HarmOtherRows.RowOf(1174)!.Value;
        var none = new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.NothingWorthACast);
        Assert.Equal(24.5 * 0.5, LifeThroughRule.Choose(in none, 0, harm, 1.0, 435, 0.5).HarmTakes, 9);
        Assert.Equal((LifeThroughSpell.None, LifeThroughRule.Resisted), (LifeThroughRule.Choose(in none, 0, harm, 1.0, 435, 0.0).Spell, LifeThroughRule.Choose(in none, 0, harm, 1.0, 435, 0.0).Why));
    }

    [Fact]
    public void The_Armory_profile_loads_its_Armored_Tuskers_at_0_and_every_listed_kind_its_world_value()
    {
        var armory = GroundCatalog.Get("tusker-armory");
        Assert.Equal((0.0, 0.0), (armory.Targets.Single(t => t.Wcid == ArmoredTusker).ResistHealthDrain, armory.Targets.Single(t => t.Wcid == TuskHolder).ResistHealthDrain));
        Assert.Equal(0.0, armory.Approach!.Hostiles.Single(h => h.Wcid == ArmoredTusker).ResistHealthDrain);
        Assert.All(armory.Approach.Hostiles.Where(h => h.Wcid != ArmoredTusker), h => Assert.Equal(1.0, h.ResistHealthDrain)); // the Silver Tusker, the Tusker Guard, the Stinging Chittick, the Reedshark Ravager

        var holding = GroundCatalog.Get("tusker-holding").Approach!.Hostiles.ToDictionary(h => h.Wcid, h => h.ResistHealthDrain);
        Assert.Equal((0.75, 0.75, 0.0, 0.0, 1.0), (holding[27800], holding[22053], holding[ArmoredTusker], holding[11541], holding[10810])); // Infected Assailer, Assailer, Armored and Plated Tusker, Rampager
        Assert.Equal(0.0, GroundCatalog.Get("tusker-barracks").Approach!.Hostiles.Single(h => h.Wcid == ArmoredTusker).ResistHealthDrain);
        var forest = GroundCatalog.Get("low-north-forest-mix");
        Assert.Equal((0.85, 0.85), (forest.Outdoor!.Creatures.Single(c => c.Wcid == 7978).ResistHealthDrain, forest.Outdoor.Creatures.Single(c => c.Wcid == 7979).ResistHealthDrain)); // the Grievvers
        Assert.Equal(0.85, FightTargets.Of(forest with { LevelBand = new LevelBand(30, 60) }).Single(t => t.Wcid == 7978).ResistHealthDrain); // an outdoor ground's fight targets carry it (the band widened to the Grievvers' 50)

        // the lookup across every profile: the values other than 1, a kind no profile names at 1
        Assert.Equal((0.0, 0.0, 0.0, 0.75, 0.75, 0.85, 0.85), (DrainResist.RecordedOf(ArmoredTusker), DrainResist.RecordedOf(TuskHolder), DrainResist.RecordedOf(11541), DrainResist.RecordedOf(27800), DrainResist.RecordedOf(22053), DrainResist.RecordedOf(7978), DrainResist.RecordedOf(7979)));
        Assert.Equal(7, DrainResist.Recorded(GroundCatalog.All).Count);
        Assert.Equal((1.0, 1.0, 0.5), (DrainResist.RecordedOf(1629), DrainResist.RecordedOf(1628), DrainResist.RecordedOf(HalfResistTusker))); // M6 14 walk drains: a kind no profile names reads the world database's table (the server's database holds every creature)

        // a bad value is refused by its path
        var bad = (JsonObject)JsonNode.Parse(GroundCatalog.ResourceText("tusker-armory.json")!)!;
        ((JsonObject)bad["targets"]![0]!).Remove("from_bestiary"); // the Armory names its targets' drain resistance by reference (M6 16 Q12): stated here, it is read as strictly as ever
        ((JsonObject)bad["targets"]![0]!)["resist_health_drain"] = -1;
        Assert.Equal("$.targets[0].resist_health_drain", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse("tusker-armory.json", bad.ToJsonString())).JsonPath);
    }
}

/// <summary>The drain resistance's measured override is process-wide: its tests run alone (after the parallel collections), so no other fight test reads a kind they turned off.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DrainResistCollection
{
    public const string Name = "Drain resistance's measured override (not parallel)";
}

/// <summary>
/// M6 14 drain resistance, the measured override: a landed drain that gave 0 where the model expected a real heal is the creature kind's resistance (an Armored Tusker gave 0 three times in a row live, each on another creature,
/// because the drain-off memory is per creature): the kind is taken as 0 for the rest of the process, named once.
/// </summary>
[Collection(DrainResistCollection.Name)]
public sealed class DrainResistMeasuredTests
{
    private const uint TuskerSlave = 1628; // FightManaTests.Rig's tusker

    [Fact]
    public void A_measured_zero_turns_a_kind_off_once_and_Clear_forgets_it()
    {
        const uint kind = 0x7FFF0001; // no profile lists it: 1 by default
        DrainResist.Clear();
        try
        {
            Assert.Equal(1.0, DrainResist.Of(kind));
            Assert.True(DrainResist.NoteMeasuredZero(kind, "Somebody"));
            Assert.False(DrainResist.NoteMeasuredZero(kind, "Somebody")); // named once
            Assert.Equal((0.0, 1.0, true), (DrainResist.Of(kind), DrainResist.RecordedOf(kind), DrainResist.IsMeasuredZero(kind)));

            // its drains now expect nothing: Drain Health Other I on a full 400-health creature, the mage missing 60
            var one = new DrainLevel(WorldActionRig.Spell(1237, SpellType.Transfer, SpellFlags.Resistable) with { Name = "Drain Health Other I" }, DrainPicker.RowOf(1237)!.Value, 1.0);
            var s = new DrainSituation(140, 200, 400, 300, 300, 400, 200, 3);
            Assert.Equal((60u, 0u), (DrainPicker.HealOf(in one, in s), DrainPicker.HealOf(in one, s with { TargetDrainResist = DrainResist.Of(kind) })));
        }
        finally
        {
            DrainResist.Clear();
        }

        Assert.Equal(1.0, DrainResist.Of(kind));
    }

    [Fact]
    public void In_the_fight_a_drain_that_gave_0_turns_its_kind_off_and_the_next_emergency_heal_is_Heal_Self()
    {
        DrainResist.Clear();
        try
        {
            // the ladder's character (m6-assume-health: no ask first), under the emergency line, one full Tusker Slave in reach; ACE's answer to every drain is "You gain 0 points of health ..."
            var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400 };
            r.R.H.State.Self.Name = MageTemplate.ClimberName;
            r.Caster.ManaRegenPerSecond = 20;
            r.Caster.HealthPerCast = 0;
            r.AddTusker(40);
            r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
            r.Caster.SetHealth(max * 55 / 100);
            var scenario = r.Scenario();

            r.Run(scenario, 15, 0.1, () => r.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell)));

            var drains = r.Caster.Answered.Where(a => DrainPicker.Ids.Contains(a.Spell)).ToList();
            Assert.Single(drains); // one drain, then none on its kind
            Assert.Contains(r.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
            Assert.True(DrainResist.IsMeasuredZero(TuskerSlave));
            Assert.Single(r.Log.Lines, l => l.Contains("health: drain resistance measured: Drain Health Other", StringComparison.Ordinal) && l.Contains("on Tusker Slave", StringComparison.Ordinal) && l.Contains($"(wcid {TuskerSlave})", StringComparison.Ordinal));
            Assert.Contains(r.Log.Lines, l => l.Contains("emergency: no tusker in reach that a drain takes from", StringComparison.Ordinal));
            var name = r.Caster.Table[drains[0].Spell].Name;
            Assert.Equal(60, scenario.MeasuredHeals.ExpectedFromRatio(name, 60)); // the 0 was the kind's, not the spell's: its measured ratio is untouched
        }
        finally
        {
            DrainResist.Clear();
        }
    }
}

using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 the element line in the fight (closes <c>TODO(m6-fight-element-line)</c>): over the scripted caster, a target that takes no fire (the Nubilous Golem 23082, ResistFire 0, ResistCold 0.85) gets the frost line of her book, one
/// line in the log, and the bolts sent are frost; a den tusker (fire 1, the largest of its seven) keeps the Flame Bolt, no line changed. What this does not prove: the live server's damage lines (the simulator's numbers are in
/// <see cref="BoltElementTests"/>).
/// </summary>
public sealed class FightElementTests(ITestOutputHelper output)
{
    private const uint Nubilous = 23082;

    private static readonly uint[] Powers = [1, 50, 100, 150, 200, 250, 300, 400];

    private static ProfileLine Frost { get; } = new("Frost Bolt", "Frost Bolt", MagicSchool.WarMagic, ProfileRole.WarOption,
        [.. WarSpellData.BoltLineIds["cold"].Take(FightScenario.Levels).Select((id, i) => new ProfileSpell(i + 1, id, i < 6 ? "Frost Bolt " + LevelFloor.Roman[i] : "Icy Torment", Powers[i], 0))]);

    private static FightManaTests.Rig Rig(uint wcid)
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        var ground = den with { Targets = [.. den.Targets, new GroundTarget(Nubilous, "Nubilous Golem", 80, GroundRole.Target)] };
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, fight: new FightOptions { Kills = 1, StaminaFullPercent = 0, GroundForTest = ground }, lines: [.. ArenaProfile.Lines, Frost], alsoKnown: [.. Frost.Spells.Select(s => s.Id)])
        {
            TuskerWcid = wcid,
        };
        foreach (var spell in Frost.Spells)
        {
            r.Caster.Table[spell.Id] = r.Caster.Table[spell.Id] with { BaseRangeConstant = 30f, BaseRangeMod = 0f };
        }

        r.Caster.ManaRegenPerSecond = 40;
        r.AddTusker(12);
        return r;
    }

    private void Dump(FightManaTests.Rig r)
    {
        foreach (var line in r.Log.Lines.Where(l => l.Contains("element", StringComparison.Ordinal) || l.Contains("the fight begins", StringComparison.Ordinal)).Take(20))
        {
            output.WriteLine(line);
        }
    }

    [Fact]
    public void A_creature_that_takes_no_fire_gets_the_frost_line_of_her_book()
    {
        var r = Rig(Nubilous);
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25, () => r.Caster.Answered.Any(a => Frost.Spells.Any(s => s.Id == a.Spell) || FightManaTests.Rig.IsBolt(a.Spell)));
        Dump(r);

        Assert.Equal("cold", scenario.BoltElementName);
        Assert.Equal("Frost Bolt", scenario.BoltLineName);
        Assert.Contains(r.Log.Lines, l => l.Contains("element: Tusker Slave (23082):", StringComparison.Ordinal) && l.Contains("the line that hits it hardest", StringComparison.Ordinal));
        Assert.DoesNotContain(r.BoltAnswers, a => true); // no Flame Bolt was cast at it
        Assert.Contains(r.Caster.Answered, a => Frost.Spells.Any(s => s.Id == a.Spell));
    }

    [Fact]
    public void A_den_tusker_keeps_the_flame_bolt()
    {
        var r = Rig(1628);
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25, () => r.BoltAnswers.Any());
        Dump(r);

        Assert.Equal("fire", scenario.BoltElementName);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("element: Tusker Slave", StringComparison.Ordinal));
        Assert.NotEmpty(r.BoltAnswers);
    }
}

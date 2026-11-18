using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 cap (live 2026-11-17 05:35:45, <c>cache/m6/14/live/ladder53/step3-levelup.log</c>: "fight end: failed: fight: m5-carried-stock: the Platinum Scarab stack is 2, under its stop line 3; Holtburg sells none (D8)" in the climber's
/// mote hunt): a carried line a whole spell level burns, under its stop line, leaves out the War Magic spells that burn it; the fight casts the highest level it pays for over the lines, named once, and ends by name only when no level is left.
/// The rig's bolts burn the level's scarab on the foci formula as the dats' do (VI the Pyreal Scarab, VII the Platinum Scarab: <c>MageKitTests</c> pins the dats'); over the scripted ACE, no dats, no server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint PyrealScarabComponent = 6;
    private const uint PlatinumScarabComponent = 112;

    /// <summary>The mote rig with the bolt lines' formulas: levels I to VI burn components 1 to 6 (the Lead to the Pyreal Scarab), VII the Platinum Scarab (112), as the dats' foci formulas do.</summary>
    private static (Rig Rig, FakeWalk Walk) CapRig(int platinum, int? pyreal = null, Func<IEnumerable<uint>, IEnumerable<uint>>? known = null)
    {
        var (r, walk) = MoteRig(diamonds: 6, platinum: platinum, pyreal: pyreal, known: known);
        var flame = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(s => s.Id).Take(FightScenario.Levels).ToArray();
        var frost = WarSpellData.BoltLineIds["cold"].Take(FightScenario.Levels).ToArray();
        foreach (var line in new[] { flame, frost })
        {
            for (var i = 0; i < line.Length; i++)
            {
                r.Table[line[i]] = r.Table[line[i]] with { Formula = [i < 6 ? (uint)(i + 1) : PlatinumScarabComponent, 0, 0, 0, 0, 0, 0, 0] };
            }
        }

        Assert.True(ComponentWeenies.TryGetByComponent(PyrealScarabComponent, out var pyrealWeenie) && pyrealWeenie.Wcid == PyrealScarab);
        Assert.True(ComponentWeenies.TryGetByComponent(PlatinumScarabComponent, out var platinumWeenie) && platinumWeenie.Wcid == PlatinumScarab);
        return (r, walk);
    }

    private static uint FlameAt(int level) => ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(s => s.Id).ElementAt(level - 1);

    [Fact]
    public void Two_Platinum_Scarabs_under_their_stop_line_of_three_leave_out_the_VII_line_and_the_mote_hunt_fights_at_VI_and_goes_on()
    {
        var (r, walk) = CapRig(platinum: 2);
        var scenario = r.Scenario(walk);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 200 && status == ScenarioStatus.Running; i++)
        {
            status = r.Frame(scenario); // 50 s
        }

        Assert.Equal(ScenarioStatus.Running, status); // live: failed at the start, m5-carried-stock
        Assert.Equal("fight", scenario.Stage);
        Assert.Null(scenario.Failure);
        Assert.Equal(0, LinesWith(r, "m5-carried-stock"));
        Assert.Equal(1, LinesWith(r, "stock: the VII line is short: Platinum Scarab 2 under 3 (carried, no home vendor sells it); the fight casts VI ("));
        Assert.Equal(1, LinesWith(r, "the fight begins: " + r.Table[FlameAt(6)].Name + " (" + FlameAt(6) + ")"));
        Assert.Contains(FlameAt(7), scenario.ShortStockSpells);
        Assert.DoesNotContain(FlameAt(6), scenario.ShortStockSpells);
    }

    [Fact]
    public void A_book_of_VI_and_VII_with_both_scarabs_under_their_stop_lines_pays_for_no_level_and_the_fight_ends_by_name()
    {
        var levels = new HashSet<uint> { FlameAt(6), FlameAt(7), WarSpellData.BoltLineIds["cold"][5], WarSpellData.BoltLineIds["cold"][6] };
        var (r, walk) = CapRig(platinum: 2, pyreal: 2, known: k => k.Where(id => levels.Contains(id) || !FightScenarioTests.IsBoltLevel(id)));
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 400 && scenario.Failure is null; i++)
        {
            r.Frame(scenario);
        }

        Assert.NotNull(scenario.Failure); // the run then leaves the ground and ends Failed, as every failed fight does
        Assert.StartsWith(FightScenario.NoCastableFailure + ": the VI line is short: Pyreal Scarab 2 under 3; the VII line is short: Platinum Scarab 2 under 3; no level of the ", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(1, LinesWith(r, "fight end: failed: " + FightScenario.NoCastableFailure + ": the VI line is short: Pyreal Scarab 2 under 3"));
        Assert.Equal(0, LinesWith(r, "m5-carried-stock"));
        Assert.Empty(r.Answered); // nothing was cast
    }

    /// <summary>True for a level of a bolt line (Flame or any element's).</summary>
    private static bool IsBoltLevel(uint id) =>
        ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Any(s => s.Id == id) || WarSpellData.BoltLineIds.Values.Any(line => line.Contains(id));
}

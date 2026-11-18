using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 run past (D137): a creature's speed is its motion's run times the run rate of its Run skill times its scale (ACE's <c>Monster_Navigation.GetMovementSpeed</c>), hers the run rate of her Run skill times 4 m/s; the profiles' approach
/// hostiles carry the pace facts (the Run skill, the motion table and the scale) and the dats give each motion's run.
/// </summary>
public sealed class CreaturePaceTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The arithmetic: ACE's rate of a Run skill, the speed at a motion's run and a scale, and ACE's 2.5 m/s for a table that gives no run.</summary>
    [Fact]
    public void A_creatures_speed_is_its_motions_run_times_its_run_rate_times_its_scale()
    {
        Assert.Equal(1.0, CreaturePace.RunRate(0), 6);
        Assert.Equal(((290.0 / 490.0 * 11.0) + 4.0) / 4.0, CreaturePace.RunRate(290), 6);
        Assert.Equal(3.0 * CreaturePace.RunRate(290) * 1.5, CreaturePace.Speed(3.0, CreaturePace.RunRate(290), 1.5), 6);
        Assert.Equal(CreaturePace.AceFallbackAnimMetresPerSecond * 2, CreaturePace.Speed(0, 2, 1), 6);

        var assailer = GroundCatalog.Get("tusker-holding").Approach!.Hostiles.Single(h => h.Wcid == 22053);
        Assert.Equal((270, 0x0900000Cu, 1.5), (assailer.RunSkill, assailer.MotionTable, assailer.Scale));
        var pace = CreaturePace.Of(assailer, _ => 3.0)!.Value;
        Assert.Equal(3.0 * CreaturePace.RunRate(270) * 1.5, pace.MetresPerSecond, 6);
        Assert.Contains("Run 270", pace.Why, StringComparison.Ordinal);
        Assert.Null(CreaturePace.Of(assailer, _ => null)); // no dats: not known
        var spines = GroundCatalog.Get("tusker-holding").Approach!.Hostiles.Single(h => h.Wcid == 22506).Ranged!;
        Assert.Equal((90, 0x40u, 40.8), (spines.DVal, spines.DamageTypes, spines.RangeMetres));
        Assert.Equal(22506u, CreatureFacts.All.Find(22506)!.Wcid);
        Assert.NotNull(CreatureFacts.All.Find(22506)!.Ranged);
        Assert.Equal(270, CreatureFacts.All.Find(22053)!.RunSkill);
    }

    /// <summary>
    /// The dats' Run is Quickness over 1 (so the profiles' <c>run_skill</c> facts are init_Level + Quickness); the dats give every approach hostile's motion a run, and each creature's speed is printed beside hers at a Run of 102 (the live
    /// climber's 7.7 m/s).
    /// </summary>
    [RequiresDatsFact]
    public void The_dats_give_every_approach_hostiles_run_and_its_speed()
    {
        var run = Lifestone.Data.CharacterCreation.SkillTableData.Load(fixture.Dats).Skills.Single(s => s.Id == Lifestone.Client.Session.Mage.MageTemplate.Run);
        output.WriteLine($"the dats' Run formula: {run.Formula}, min level {run.MinLevel}");
        Assert.Equal(CreaturePace.RunFormula, run.Formula); // the creatures' run_skill facts are init_Level + Quickness / 1
        var source = new DatAnimationSource(fixture.Dats);
        var paces = CreaturePaces.FromDats(source);
        var her = CreaturePace.RunRate(102) * Lifestone.Data.Motion.MotionInterpreter.RunAnimSpeed;
        output.WriteLine($"her speed at Run 102: {her:0.00} m/s");
        Assert.InRange(her, 7.6, 7.8);
        foreach (var ground in new[] { "tusker-holding", "tusker-barracks" })
        {
            foreach (var h in GroundCatalog.Get(ground).Approach!.Hostiles)
            {
                var anim = paces.AnimOf(h.MotionTable!.Value);
                Assert.NotNull(anim);
                var pace = CreaturePace.Of(h, paces.AnimOf)!.Value;
                output.WriteLine($"{ground}: {h.Name} ({h.Wcid}): motion run {anim:0.00} m/s; {pace.MetresPerSecond:0.00} m/s ({pace.Why})");
                Assert.InRange(pace.MetresPerSecond, 1, 40);
            }
        }

        foreach (var table in new uint[] { 0x09000001, 0x09000081, 0x0900001C, 0x09000007, 0x0900009A, 0x09000006, 0x0900008F, 0x09000002, 0x0900000A, 0x090000A3, 0x09000027, 0x0900012B, 0x090001A8, 0x09000090, 0x09000167, 0x09000119 })
        {
            output.WriteLine($"motion 0x{table:X8}: run {paces.AnimOf(table)?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "none"} m/s");
        }
    }
}

using DatReaderWriter.Enums;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 2.1 s2a: the stand-in's cast over the retail dats' own plans. <see cref="StandInCastPlanner"/> is <see cref="CastGesturePlanner"/> over the SpellComponentTable and the player's motion table, and the stand-in's messages
/// arrive at the plan's own times: the words at the start, a motion per scarab and the talisman's gesture, the effect at the plan's EffectTime, UseDone at its EndTime. A spell with the most wind-ups, a FastCast spell (none) and a
/// level VIII that fizzles are cast through a whole offline session. Skipped without the dats.
/// </summary>
public sealed class StandInCastPlannerTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Fizzler = 4325; // Incantation of Strength Self, power 400: above the stand-in's skill plus 50

    private static int Windups(SpellInfo s) => s.Components.Count(CastGesturePlanner.IsAceScarab);

    private static bool SelfBuff(SpellInfo s) =>
        s.MetaSpellType == SpellType.Enchantment && (s.Flags & SpellFlags.SelfTargeted) != 0 && s.Power <= StandInWorld.PlaceholderMagicSkill + 50 && (s.CasterEffect != PlayScript.Invalid || s.TargetEffect != PlayScript.Invalid);

    [RequiresDatsFact]
    public void The_stand_ins_planner_is_the_cast_gesture_planner_over_the_dats()
    {
        var table = SpellTableData.Load(fixture.Dats);
        var animations = new DatAnimationSource(fixture.Dats);
        var components = SpellComponentTableData.Load(fixture.Dats);
        var human = animations.GetMotionTable(StandInVisit.HumanMotionTable)!;
        var planner = StandInCastPlanner.FromDats(fixture.Dats);
        foreach (var id in new uint[] { 2, 27, Fizzler })
        {
            var direct = CastGesturePlanner.Plan(table.Spells[id], components, human, animations, StandInCastPlanner.AccountName);
            var planned = planner.Plan(table.Spells[id]);
            Assert.Equal(direct.Words, planned.Words);
            Assert.Equal(direct.Steps, planned.Steps);
            Assert.Equal((direct.EffectTime, direct.EndTime, direct.EffectIntensity), (planned.EffectTime, planned.EndTime, planned.EffectIntensity));
            Assert.Same(planned, planner.Plan(table.Spells[id])); // kept: a second cast plans nothing
        }
    }

    private CastRig Rig(SpellTableData table, StandInCastPlanner planner)
    {
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Stats = new StandInPlayerStats(), SpellTable = table, CastPlanner = planner.Plan };
        var rig = new CastRig(visit, null, fixture.Dats);
        return rig;
    }

    private void CastAndCompare(uint id, bool fizzle)
    {
        var table = SpellTableData.Load(fixture.Dats);
        var planner = StandInCastPlanner.FromDats(fixture.Dats);
        var spell = table.Spells[id];
        var plan = planner.Plan(spell);
        using var rig = Rig(table, planner);
        rig.Learn(id);
        rig.Mode(CombatMode.Magic);
        rig.Begin();
        var cast = rig.Session.Actions.Cast(id, ActionSource.Host);
        rig.Run(plan.EndTime + 1.5);

        Assert.True(cast.IsCompleted, cast.Summary);
        Assert.True(rig.Tags.Contains("02BB"), $"the words were not sent: tags=[{string.Join(',', rig.Tags)}] spell={spell.Id} '{spell.Name}' words='{plan.Words}' outcome={cast.Outcome?.Kind}/{cast.Outcome?.Detail}");
        var start = rig.First("02BB");
        var motionTimes = rig.Timeline.Where(e => e.Tag == "F74C").Select(e => e.At - start).ToArray();
        var expected = plan.Steps.Where(s => s.Kind != CastStepKind.SpellEffects).ToArray();

        // A motion per step, at the step's time (within the 10 ms tick), the commands the plan names, in order.
        Assert.Equal(expected.Length, motionTimes.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(motionTimes[i], expected[i].StartTime - 0.011, expected[i].StartTime + 0.021);
            Assert.Equal(MotionCommandTable.ToWire(expected[i].Motion), rig.Motions[i].Motion.Forward);
        }

        Assert.Equal((spell.Flags & SpellFlags.FastCast) != 0 ? 0 : Windups(spell), plan.Steps.Count(s => s.Kind == CastStepKind.Windup)); // a wind-up per scarab of the base formula, none for FastCast
        Assert.Equal(plan.EffectTime, rig.First(fizzle ? "028A" : "02C2") - start, 0.03);
        Assert.Equal(plan.EndTime, rig.First("01C7") - start, 0.03);
        Assert.Equal(plan.Words, rig.Chat.First(l => l.ChatType == StandInWorld.SpellcastingChat).Text);
        Assert.Equal(fizzle ? ActionOutcomeKind.ServerError : ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal(
            (spell.CasterEffect != PlayScript.Invalid && !fizzle ? 1 : 0) + (spell.TargetEffect != PlayScript.Invalid && !fizzle ? 1 : 0) + (fizzle ? 1 : 0),
            rig.Scripts.Count);
    }

}

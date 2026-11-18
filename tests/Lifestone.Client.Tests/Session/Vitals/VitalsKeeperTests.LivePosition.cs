using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 16 E2b (the one world picture; docs/automation-engine.md E2): the keeper's "is a creature awake near her" (<c>CreatureAwakeNear</c>, 24 m) takes a creature where the session draws it
/// (<see cref="IGameSessionView.Live"/>), not where the server last reported it. A monster in melee that follows her sends no position, so its report stays where it stood before her own steps. Each test states the one
/// state that shows the change: a creature whose drawn body and reported place differ. The answer by the report alone (what the keeper read before) is the same rig with no bodies drawn.
/// </summary>
public sealed partial class VitalsKeeperTests
{
    private const uint Tusker = 0x7A9B301B;

    /// <summary>
    /// A rig whose player stands in the town (a land cell: retail's blip distance, the bound inside which a body answers, is 100 m there) with low stamina and the lying posture asked, and a creature that can attack her last
    /// reported <paramref name="reportMetres"/> east of her; with <paramref name="bodyMetres"/> given, the session draws bodies and the creature's stands that far east of her.
    /// </summary>
    private static C2VitalsRig WithCreature(float reportMetres, float? bodyMetres, bool drawNoBodyForIt = false)
    {
        var rig = WithNeighbour(WorldObjectTable.AttackableFlag);
        var here = rig.R.H.State.Self.Position!.Value;
        Assert.True(rig.R.H.State.Objects.TryGet(Tusker, out var creature));
        creature.Position = ScriptedBodies.East(here, reportMetres);
        if (bodyMetres is { } metres)
        {
            rig.Draw(new ScriptedBodies().StandEast(Tusker, here, metres));
        }
        else if (drawNoBodyForIt)
        {
            rig.Draw(new ScriptedBodies());
        }

        return rig;
    }

    private static bool LayDown(C2VitalsRig rig)
    {
        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());
        var stood = rig.R.H.Log.Lines.Any(l => l.Contains("0x7A9B301B is within 24 m: she rests standing", StringComparison.Ordinal));
        var lay = rig.Spy.Calls.Contains("0x41000014 down");
        Assert.NotEqual(stood, lay); // one or the other, never both
        return lay;
    }

    [Fact]
    public void A_creature_drawn_2_m_from_her_and_last_reported_30_m_back_keeps_her_on_her_feet()
    {
        // before (the report alone: 30 m is past the 24 m): she lay down beside it
        Assert.True(LayDown(WithCreature(reportMetres: 30, bodyMetres: null)));

        // after: its body stands 2 m from her, so she rests on her feet and the line names it
        var rig = WithCreature(reportMetres: 30, bodyMetres: 2);
        Assert.False(LayDown(rig));
        Assert.Empty(rig.Spy.Calls);
        Assert.InRange(rig.Keeper.RestSeconds, 195, 205); // the standing rate, as the keeper's other standing rests
    }

    [Fact]
    public void A_creature_last_reported_10_m_from_her_and_drawn_40_m_off_no_longer_keeps_her_on_her_feet()
    {
        // before (the report alone: 10 m is inside the 24 m): she rested standing
        Assert.False(LayDown(WithCreature(reportMetres: 10, bodyMetres: null)));

        // after: its body stands 40 m off, so nothing is awake within 24 m and she lies down
        var rig = WithCreature(reportMetres: 10, bodyMetres: 40);
        Assert.True(LayDown(rig));
        Assert.InRange(rig.Keeper.RestSeconds, 64, 70); // the lying rate
    }

    [Fact]
    public void Where_no_body_answers_the_servers_report_does_as_before()
    {
        // the session draws bodies and keeps none for the creature (its create waits in the adapter's queue): the report answers
        Assert.False(LayDown(WithCreature(reportMetres: 10, bodyMetres: null, drawNoBodyForIt: true)));
        Assert.True(LayDown(WithCreature(reportMetres: 30, bodyMetres: null, drawNoBodyForIt: true)));

        // a body farther from the report than retail walks a body to one (100 m outdoors) is not trusted: the report answers
        Assert.True(LayDown(WithCreature(reportMetres: 150, bodyMetres: 2)));
    }

    /// <summary>M6 16 Q1: the awake read asks the session's threat board; the rest decides as it did and writes no shadow line (the lane of the old read is gone).</summary>
    [Theory]
    [InlineData(10f, false)] // a creature within the 24 m: she rests standing, asked every frame of the rest
    [InlineData(30f, true)] // none within it: she lies down, asked until she is down
    public void The_awake_read_asks_the_threat_board_and_a_rest_writes_no_shadow_line(float metres, bool lay)
    {
        var rig = WithCreature(reportMetres: metres, bodyMetres: null);
        Assert.Equal(lay, LayDown(rig));
        Assert.DoesNotContain(rig.R.H.Log.Lines, l => l.Contains(Lifestone.Client.Session.Threat.ThreatShadowLane.Token, StringComparison.Ordinal));
        Assert.Equal(!lay, rig.R.H.Log.Lines.Any(l => l.Contains("she rests standing", StringComparison.Ordinal))); // named once, as before
    }

    [Fact]
    public void The_rest_blockers_rule_reads_the_drawn_body_when_it_is_given_the_live_positions()
    {
        const uint den = 0x01050303; // an EnvCell: the bound is 20 m there
        var here = new CellFrame(den, new AcFrame(new Vector3(0, 0, 0), Quaternion.Identity));
        var rig = WithNeighbour(WorldObjectTable.AttackableFlag);
        var table = rig.R.H.State.Objects;
        Assert.True(table.TryGet(Tusker, out var creature));
        creature.Position = ScriptedBodies.East(here, 30);
        var bodies = new ScriptedBodies();
        var live = bodies.Over(rig.R.H.R.Clock);

        // reported 30 m off, drawn 17 m off (within the fight's 20 m): before, nothing blocked the lie-down; after, it is shot first
        bodies.StandEast(Tusker, here, 17);
        Assert.Equal(RestBlock.None, RestBlockerRule.Read(table, here, 0, VitalsKeeper.QuietMetres, 20f, out _));
        Assert.Equal(RestBlock.ShootFirst, RestBlockerRule.Read(table, here, 0, VitalsKeeper.QuietMetres, 20f, out var guid, live));
        Assert.Equal(Tusker, guid);

        // drawn 22 m off (inside the quiet 24 m, past the reach): the wait
        bodies.StandEast(Tusker, here, 22);
        Assert.Equal(RestBlock.Wait, RestBlockerRule.Read(table, here, 0, VitalsKeeper.QuietMetres, 20f, out _, live));

        // reported 8 m off, drawn 26 m off: before, shot first; after, nothing blocks
        creature.Position = ScriptedBodies.East(here, 8);
        bodies.StandEast(Tusker, here, 26);
        Assert.Equal(RestBlock.ShootFirst, RestBlockerRule.Read(table, here, 0, VitalsKeeper.QuietMetres, 20f, out _));
        Assert.Equal(RestBlock.None, RestBlockerRule.Read(table, here, 0, VitalsKeeper.QuietMetres, 20f, out _, live));

        // a session that draws no bodies answers from the report, with or without the parameter
        Assert.Equal(RestBlock.ShootFirst, RestBlockerRule.Read(table, here, 0, VitalsKeeper.QuietMetres, 20f, out _, LivePositions.ReportsOnly));
    }
}

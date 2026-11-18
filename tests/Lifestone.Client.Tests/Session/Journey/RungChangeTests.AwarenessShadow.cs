using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 E1f (docs/automation-engine.md E1; m6-plan.md D139, D137): the travel leg's part of the walk-awareness shadow (<c>TravelLegAwarenessShadow.cs</c>): a guarded walk is one leg of the session's <see cref="WalkAwareness"/>; the
/// attackers the run past already reads are handed to it; the leg's line is written at its end, also when the leg fails. Report-only: the runs here end as they do without it. The rig's travel seam moves the body itself, so the
/// walker's own creature feed (<see cref="HostileCreatureFeed"/>, over the session's object table, as every host builds it) is filled here at each step of the run, as a runner's refresh fills it. The creature is an Armored
/// Tusker (weenie 11540): it sees 25 m by the world database and is read at ACE's default of 18 m (<c>CreatureBehaviourTests</c>); with no Setup read its two discs are 19.48 m and 26.48 m.
/// </summary>
public sealed partial class RungChangeTests
{
    private const uint ArmoredTusker = 11540;

    /// <summary>The run past rig with the walker's creature feed filled at each step of the run to the entry's front; <paramref name="atStart"/> sets the world when that run begins (the body's place and the run's direction along x).</summary>
    private static (RungRig Rig, JourneyContext Context) ShadowRig(Action<RungRig, Lifestone.Data.World.CellFrame, float> atStart)
    {
        var (t, context) = RunPastRig(anim: 1.0, seconds: 12);
        t.R.H.State.Objects.WalkAwareness.Mode = WalkAwarenessMode.Pinned; // these lines are the pinned mode's (the product's default is Own since M6 16 Q3; the mode's words are WalkAwarenessTests')
        var feed = new HostileCreatureFeed(t.R.H.State.Objects, WorldActionRig.Player, t.R.H.Log);
        var layer = new List<NavCreature>();
        t.Travel.OnRun = (_, ran, _) =>
        {
            var here = t.R.H.State.Self.Position!.Value;
            if (ran == 0)
            {
                atStart(t, here, here.Origin.X > 96 ? -1f : 1f);
            }

            feed.Fill(NavMap.FromCellFrame(here).Global, NavRunner.CreatureLookMetres, layer);
        };
        return (t, context);
    }

    /// <summary>
    /// The run to the entry's front passes an Armored Tusker that never attacks, 22 m aside of her line: outside the 19.48 m the walk keeps, inside the 26.48 m of its own range. The kind is named once, the leg's line counts the pass
    /// with no attacker, the shadow says once that it watches, and the run arrives as it does without the shadow: no stop, no fight-back.
    /// </summary>
    [Fact]
    public void A_travel_leg_names_the_creature_it_passed_inside_its_own_range_and_outside_the_pinned_radius()
    {
        var (t, context) = ShadowRig((rig, here, along) =>
        {
            rig.Bystander(Runner, "Armored Tusker", 3f, wcid: ArmoredTusker);
            rig.MoveObject(Runner, here.CellId, here.Origin + new Vector3(along * 20f, 22f, 0f));
        });

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("awareness-shadow: watching (--walk-awareness=pinned): the walks keep the pinned ranges off a creature;")); // once a session, whatever the legs
        Assert.Single(t.Lines("awareness-shadow: Armored Tusker (class 11540): the walk keeps 18 m, its own range gives 25 m"));
        var leg = Assert.Single(t.Lines("awareness-shadow: the run leg to entry-front: "));
        Assert.Contains(
            "1 creature(s) passed inside their own range and outside the pinned radius, 0 of them among her attackers; closest 'Armored Tusker' (class 11540) 0x80001960 at 22.0 m (pinned radius 19.5 m, own 26.5 m); mode=pinned passed=1 attackers=0 closest_m=22.0 session_legs=",
            leg,
            StringComparison.Ordinal);
        Assert.EndsWith(" session_passed=1 session_attackers=0", leg, StringComparison.Ordinal);
        Assert.Single(t.Lines("awareness-shadow: the ")); // the legs that passed no such creature write no line
        Assert.Empty(t.Lines("fight-back 1 on the run leg"));
        Assert.Single(t.Travel.Requests, r => r.Destination.Name == "entry-front"); // the walk was never stopped or planned again
    }

    /// <summary>
    /// The same pass by an Armored Tusker that has attacked her: the run past reads it among the attackers present (heard within 10 s, within 40 m), and the leg's line counts it as one of them. The run past's own verdict and the
    /// run's end are the rule's, unchanged.
    /// </summary>
    [Fact]
    public void A_travel_leg_says_when_a_creature_it_passed_that_way_was_then_among_her_attackers()
    {
        var (t, context) = ShadowRig((rig, here, along) =>
        {
            rig.Attacker(Runner, "Armored Tusker", 3f, wcid: ArmoredTusker);
            rig.MoveObject(Runner, here.CellId, here.Origin + new Vector3(along * 20f, 22f, 0f));
        });

        var (_, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        var leg = Assert.Single(t.Lines("awareness-shadow: the run leg to entry-front: "));
        Assert.Contains("1 creature(s) passed inside their own range and outside the pinned radius, 1 of them among her attackers; closest 'Armored Tusker' (class 11540) 0x80001960 at 22.0 m", leg, StringComparison.Ordinal);
        Assert.EndsWith(" session_passed=1 session_attackers=1", leg, StringComparison.Ordinal);
        Assert.NotEmpty(t.Lines("run past: ")); // the run past judged it by its own rule
        Assert.Equal(1, t.R.H.State.Objects.WalkAwareness.Attackers);
    }

    /// <summary>A leg that fails still writes its line: the walk to the entry's front is refused by the seam after the layer saw the creature between its two discs, and the leg's end closes the shadow's leg.</summary>
    [Fact]
    public void A_travel_leg_that_fails_still_writes_the_shadows_line()
    {
        var t = HopRig();
        t.R.H.State.Objects.WalkAwareness.Mode = WalkAwarenessMode.Pinned; // the pinned mode's line (see ShadowRig)
        var context = t.Context(Burrow);
        var feed = new HostileCreatureFeed(t.R.H.State.Objects, WorldActionRig.Player, t.R.H.Log);
        t.Travel.Refuse = r =>
        {
            if (r.Destination.Name != "entry-front")
            {
                return null;
            }

            var here = t.R.H.State.Self.Position!.Value;
            t.Bystander(Runner, "Armored Tusker", 22f, wcid: ArmoredTusker);
            feed.Fill(NavMap.FromCellFrame(here).Global, NavRunner.CreatureLookMetres, []);
            return "travel:run:no-path: the test refuses the walk";
        };

        var (_, status) = t.RunStation(new RungChangeStation(), context);

        Assert.Equal(ScenarioStatus.Failed, status);
        var leg = Assert.Single(t.Lines("awareness-shadow: the run leg to entry-front: "));
        Assert.Contains("mode=pinned passed=1 attackers=0 closest_m=22.0 ", leg, StringComparison.Ordinal);
        Assert.False(t.R.H.State.Objects.WalkAwareness.LegOpen);
    }
}

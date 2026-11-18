using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 14 holding plates: the pressure plates' walls (<see cref="PlateKeepOff"/>) and the standing jumps' arithmetic (<see cref="PlateJumps"/>), without the dats: a wall is the plate's shape, her body and her stop at her live speed; a step
/// that passes within one meets it and one that only leaves it does not; a plan's line is judged on its walked legs, not over a jump; the arc's apex, low ends and partial-bar flight come from her flight and speed; plates whose walls
/// leave no body between them are one group; and the witness knows a plate's chat line.
/// </summary>
public sealed class PlateKeepOffTests
{
    private static readonly KeptPlate Plate = new("the plate", 0x5B48026B, new Vector3(70f, -60f, 0.005f), 0.24f, 0.18f);

    [Fact]
    public void The_wall_is_the_plate_s_shape_her_body_and_her_stop_at_her_live_speed()
    {
        Assert.Equal(0.40f, PlateKeepOff.StopAt(0f), 3); // a body at rest still coasts the runner's least brake
        Assert.Equal((7.74f / 30f) + 0.774f, PlateKeepOff.StopAt(7.74f), 3); // one physics step and a tenth of a second of her run
        Assert.True(PlateKeepOff.StopAt(9f) > PlateKeepOff.StopAt(7.74f), "a faster body stops longer");
        var walls = new PlateKeepOff([Plate], PlateKeepOff.StopAt(7.74f));
        Assert.Equal(0.24f + 0.48f, PlateKeepOff.TouchOf(Plate), 4);
        Assert.Equal(0.72f + walls.StopMetres, walls.ReachOf(Plate), 4);
        Assert.Same(walls, walls.WithStop(walls.StopMetres));
        Assert.NotSame(walls, walls.WithStop(0.5f));
        Assert.Equal(0.72f + 0.5f, walls.WithStop(0.5f).ReachOf(Plate), 4);
        Assert.Equal(walls.ReachOf(Plate), walls.MaxReach, 4);
    }

    [Fact]
    public void A_step_that_passes_within_a_wall_meets_it_and_one_that_only_leaves_it_does_not()
    {
        var walls = new PlateKeepOff([Plate], 1f); // a wall of 1.72 m
        var floor = 0.005f;
        Assert.Equal(Plate, walls.FirstMeeting(new Vector3(70f, -65f, floor), new Vector3(70f, -55f, floor))); // straight over it
        Assert.Equal(Plate, walls.FirstMeeting(new Vector3(68.5f, -65f, floor), new Vector3(68.5f, -55f, floor))); // 1.5 m beside it
        Assert.Null(walls.FirstMeeting(new Vector3(68.2f, -65f, floor), new Vector3(68.2f, -55f, floor))); // 1.8 m beside it
        Assert.Null(walls.FirstMeeting(new Vector3(70f, -59f, floor), new Vector3(70f, -55f, floor))); // from inside, outward
        Assert.Equal(Plate, walls.FirstMeeting(new Vector3(70f, -59f, floor), new Vector3(70f, -65f, floor))); // from inside, across it
        Assert.Null(walls.FirstMeeting(new Vector3(70f, -65f, -12f), new Vector3(70f, -55f, -12f))); // a storey down
        Assert.Equal(Plate, walls.Holding(new Vector3(71f, -60f, floor)));
        Assert.Null(walls.Holding(new Vector3(72f, -60f, floor)));
        Assert.Equal(2f, walls.Nearest(new Vector3(72f, -60f, floor))!.Value.Metres, 4);
    }

    [Fact]
    public void A_plan_s_line_is_judged_on_its_walked_legs_and_not_over_a_jump()
    {
        var walls = new PlateKeepOff([Plate], 1f);
        const uint block = 0x5B480000;
        NavWaypoint At(float y, DirectedJump? jump = null) => new(new CellFrame(0x5B48026B, new AcFrame(new Vector3(70f, y, 0.005f), Quaternion.Identity)), Doorway: false) { Jump = jump };
        var side = new PitJumpSide(0x5B48026B, new Vector3(70f, -62.5f, 0.005f), new Vector3(70f, -62.5f, 0.005f), new Vector3(70f, -62.5f, 0.005f));
        var far = new PitJumpSide(0x5B48026B, new Vector3(70f, -58f, 0.005f), new Vector3(70f, -54f, 0.005f), new Vector3(70f, -54f, 0.005f));
        var jump = new DirectedJump(new PitJump("plates: the plate north", 0, 4.5f, 4.8f, side, far) { Standing = true, Bar = 0.6f }, side, far);
        Assert.Null(walls.FirstMeeting([At(-65f), At(-62.5f, jump), At(-54f), At(-50f)], block)); // the flight passes over it
        var walked = walls.FirstMeeting([At(-65f), At(-62.5f), At(-54f)], block);
        Assert.Equal((Plate, 2), walked!.Value);
        Assert.Single(jump.Crossing.Directions); // a standing jump is one way
        Assert.Equal(0.6f, jump.Crossing.Bar);
    }

    [Fact]
    public void The_arc_s_apex_low_ends_and_partial_bar_flight_come_from_her_flight_and_speed()
    {
        var apex = PlateJumps.ApexMetres(11.54f, 7.74f);
        Assert.Equal(2.72f, apex, 2); // Jump 178's GetJumpHeight at a full bar
        Assert.Equal(0.19f, PlateJumps.LowEndMetres(11.54f, apex, 0.18f), 2); // her feet clear the plate's top 0.19 m after the take-off
        Assert.True(float.IsPositiveInfinity(PlateJumps.LowEndMetres(11.54f, apex, 3f)));
        Assert.Equal(apex * 0.5f, PlateJumps.ApexAt(apex, 0.5f), 4); // the height is linear in the bar
        Assert.Equal(0.35f, PlateJumps.ApexAt(apex, 0.05f), 4); // and at least 0.35 m
        Assert.Equal(11.54f * MathF.Sqrt(0.5f), PlateJumps.FlightAt(11.54f, apex, 0.5f), 3); // the flight goes with the root of the height
        Assert.Equal(11.54f, PlateJumps.FlightAt(11.54f, apex, 1f), 3);
    }

    [Fact]
    public void Plates_whose_walls_leave_no_body_between_them_are_one_group()
    {
        KeptPlate At(float x, float y, float z = 0.005f) => new($"the plate at {x}", 0x5B480373, new Vector3(x, y, z), 0.24f, 0.18f);
        var walls = new PlateKeepOff([At(296.1f, -50f), At(298.9f, -50f), At(301.4f, -50f), At(303.9f, -50f), At(340f, -70.6f), At(296.1f, -50f, -12f)], 1.03f);
        var groups = PlateJumps.Groups(walls);
        Assert.Equal(3, groups.Count);
        Assert.Equal(4, groups[0].Count); // the row across 0x5B480373: walls of 1.75 m, 2.5 to 2.8 m apart
        Assert.Equal("the 4 plates in 0x5B480373", PlateJumps.GroupName(groups[0]));
        Assert.Equal("the plate at 340", PlateJumps.GroupName(groups[1]));
        Assert.Single(groups[2]); // a storey down is its own
    }

    [Fact]
    public void The_witness_knows_a_plate_s_chat_line_by_its_name_or_its_spells()
    {
        var plates = GroundCatalog.Get("tusker-holding").Plates!;
        Assert.True(PlateSpellWitness.NamesPlate(plates, "Tusker's Friend casts Tusker's Gift on you"));
        Assert.True(PlateSpellWitness.NamesPlate(plates, "You resist the spell cast by Tusker's Friend"));
        Assert.True(PlateSpellWitness.NamesPlate(plates, "Tusker's Friend casts Nullify All Magic Other on you"));
        Assert.False(PlateSpellWitness.NamesPlate(plates, "Tusker Rampager hits you for 40 points of bludgeoning damage!"));
        Assert.Equal(new uint[] { 1878, 2166 }, plates.Spells.Select(s => s.Id));
    }
}

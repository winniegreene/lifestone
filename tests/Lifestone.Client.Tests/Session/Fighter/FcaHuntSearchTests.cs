using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 P5b fix FC-a3 (gate round 1 B, the serial archer: "acquire: no creature of the table in the band 20-30 within 35 m of the player after 4 cell(s)" with about 92 of its 300 s left, the forest emptied by gate A's
/// archer): a hunt on foot (a journey's travel seam) searches on, up to <see cref="HuntScenario.OnFootMaxCells"/> cells and its own time, the seek running at each cell as before; a search that still finds nothing fails by
/// the acquire's own name, also when the time ends it. Over <see cref="PlaceTravel"/> the bound stays <see cref="HuntScenario.MaxCells"/>. The scripted rig holds no creature at all (D23: nothing is spawned anywhere).
/// </summary>
public sealed class FcaHuntSearchTests
{
    private static HuntOptions Base => new() { Kills = 1, Seconds = 3000, Sell = false, BuyKits = 0 };

    private static HuntScriptRig FcaRig(HuntOptions options, J2aRecordingTravel travel)
    {
        var rig = HuntScriptRig.Create(log => new HuntScenario(options with { Travel = travel }, log, _ => 40f, endSession: false) { SeekHeight = (_, _, _) => 40f });
        travel.Rig = rig;
        return rig;
    }

    [Fact]
    public void On_foot_with_nothing_near_the_hunt_searches_eight_cells_then_fails_by_the_acquires_name()
    {
        var travel = new J2aRecordingTravel();
        using var rig = FcaRig(Base, travel);

        var status = rig.Run(3000);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(8, HuntScenario.OnFootMaxCells);
        Assert.Equal(HuntScenario.OnFootMaxCells, rig.Scenario.CellLimit);
        Assert.Equal(HuntScenario.OnFootMaxCells, rig.Scenario.CellsVisited);
        Assert.Equal(HuntScenario.OnFootMaxCells, travel.Requests.Count(r => r.Purpose == TravelPurpose.Cell));
        Assert.StartsWith("acquire: no creature of the table in the band 8-20 within 60 m of the player after 8 cell(s)", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.True(rig.Logged("(cell 8 of at most 8)"), rig.Tail());
        Assert.Equal(0, rig.J2aPlaces());
    }

    [Fact]
    public void On_foot_a_search_the_hunts_time_ends_fails_by_the_acquires_name_not_the_times()
    {
        var travel = new J2aRecordingTravel();
        using var rig = FcaRig(Base with { Seconds = 1 }, travel);

        var status = rig.Run(600);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("acquire: no creature of the table in the band 8-20", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the hunt's 1 s ran out while it searched", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.True(rig.Scenario.CellsVisited < HuntScenario.OnFootMaxCells, rig.Scenario.CellsVisited.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Over_place_travel_the_bound_stays_four_cells()
    {
        using var rig = HuntScriptRig.Create(log => new HuntScenario(Base, log, _ => 40f, endSession: false) { SeekHeight = (_, _, _) => 40f }); // the default travel: today's place

        var status = rig.Run(3000);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(HuntScenario.MaxCells, rig.Scenario.CellLimit);
        Assert.Equal(HuntScenario.MaxCells, rig.Scenario.CellsVisited);
        Assert.StartsWith("acquire: no creature of the table in the band 8-20 within 60 m of the player after 4 cell(s)", rig.Scenario.Failure, StringComparison.Ordinal);
    }
}

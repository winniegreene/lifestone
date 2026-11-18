using System.Text.Json.Nodes;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 entry recharge: a ground's approach facts (the profile's optional <c>approach</c>): the Tusker Barracks' records the hostiles between the Facility Hub's drop 0xF07E0040 and the entry front 0xF07F0039, with their source and no fight
/// number; the loader is strict about the section. And the guard's line for a state it takes no retreat from names the way out as the way out (live 2026-11-13 19:29:08 UTC: it said "on its way home" of the way out).
/// </summary>
public sealed class GroundApproachTests
{
    [Fact]
    public void The_Barracks_records_its_approachs_hostiles_as_facts_and_a_drop_there_is_among_them()
    {
        var barracks = GroundCatalog.Get("tusker-barracks");
        var approach = barracks.Approach!;
        Assert.Equal(new ushort[] { 0xF07E, 0xF07F }, approach.Landblocks);
        Assert.Equal(new[] { (7184u, "Silver Tusker"), (11540u, "Armored Tusker"), (1629u, "Tusker Guard") }, approach.Hostiles.Select(h => (h.Wcid, h.Name)));
        Assert.Contains("weenie_properties_generator", approach.Source, StringComparison.Ordinal);
        Assert.Same(approach, barracks.ApproachAt(0xF07E0040)); // the Facility Hub portal's drop
        Assert.Same(approach, barracks.ApproachAt(0xF07F0039)); // the entry's front
        Assert.Null(barracks.ApproachAt(barracks.Arrival.Cell)); // inside the ground
        Assert.Null(barracks.ApproachAt(0xF07E0140)); // an indoor cell of the landblock
        Assert.Null(barracks.ApproachAt(0xF6820033)); // the Refuge's drop: another landblock
        Assert.Null(GroundCatalog.Get("tusker-burrow").ApproachAt(0xF07E0040)); // no other ground records it
    }

    [Fact]
    public void The_approach_section_is_strict()
    {
        static JsonObject Copy() => (JsonObject)JsonNode.Parse(GroundCatalog.ResourceText("tusker-barracks.json")!)!;
        const string File = "tusker-barracks.json";

        var none = Copy();
        ((JsonObject)none["approach"]!)["hostiles"] = new JsonArray();
        Assert.Equal("$.approach.hostiles", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(File, none.ToJsonString())).JsonPath);

        var nowhere = Copy();
        ((JsonObject)nowhere["approach"]!)["landblocks"] = new JsonArray();
        Assert.Equal("$.approach.landblocks", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(File, nowhere.ToJsonString())).JsonPath);

        var unknown = Copy();
        ((JsonObject)unknown["approach"]!)["hit"] = 12; // never a fight number: an unknown field fails
        Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(File, unknown.ToJsonString()));

        var absent = Copy();
        absent.Remove("approach");
        Assert.Null(GroundCatalog.Parse(File, absent.ToJsonString()).Approach); // optional
    }

    [Fact]
    public void The_guards_no_retreat_line_names_the_way_out_as_the_way_out()
    {
        var outbound = AutopilotScenario.GuardNoRetreatWhy(AutopilotState.Travel, eligible: false, AutopilotScenario.RungOutSpec.Name, homeward: false);
        Assert.StartsWith("she travels out to the ground, not home: the rung change's legs keep the ground's retreat line", outbound, StringComparison.Ordinal);
        Assert.Equal("she is on her way home", AutopilotScenario.GuardNoRetreatWhy(AutopilotState.Travel, false, AutopilotScenario.RungHomeSpec.Name, homeward: true));
        Assert.Equal("the state is the death's", AutopilotScenario.GuardNoRetreatWhy(AutopilotState.Death, false, null, false));
        Assert.Equal("not on the ground", AutopilotScenario.GuardNoRetreatWhy(AutopilotState.Upkeep, eligible: true, null, false));
    }
}

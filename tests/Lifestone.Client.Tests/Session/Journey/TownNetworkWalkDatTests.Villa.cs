using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 villa, on the retail dats: the walks the villa's steps make, each planned and confirmed as the journey's leg plans it (<see cref="Walk"/>): the three minor-stone runs from their portal's drop to the stone
/// (the Smoldering one is <see cref="The_run_from_the_yinar_drop_reaches_the_minor_smoldering_stones_cave"/>), Holtburg's lifestone to the Agent of the Arcanum and to the Crude Lockpick's Merchant, Holtburg's
/// lifestone to the villa's guardian, the guardian (the house recall's arrival) to the house portal, and the basement's arrival to its two storage chests. A walk no plan reaches is green only while it is pinned in
/// <see cref="VillaKnown"/> with its TODO; a pin fails once the walk reaches, so a fix removes its row.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>
    /// The villa walks no plan reaches on this base, each with its TODO (the cause is in the test's output). <c>minor-shivering-stone</c>: the climb from the Holtburg Wilderness Settlement drop reaches the igloo's
    /// outside (<see cref="Approach"/>), but no doorway lane leads into the igloo's room 0xBDB40102. <c>minor-stinging-stone</c>: the run from the Blackmire Temple drop reaches the swamp cave's mouth, but no doorway lane leads
    /// into the cave's rooms (the stone's room 0xBA600100 lies 6 m under the mouth's 0xBA600103). <c>recall-to-house-portal</c>: from the guardian (where the house recall lands) the plan finds no path into the villa's portal room
    /// 0x9DAF0118, a floor 4.8 m under the guardian's.
    /// </summary>
    private static readonly Dictionary<string, string> VillaKnown = new(StringComparer.Ordinal)
    {
        ["minor-shivering-stone"] = "TODO(villa-shivering-igloo-walk)",
        ["minor-stinging-stone"] = "TODO(villa-stinging-cave-walk)",
        ["recall-to-house-portal"] = "TODO(villa-house-portal-walk)",
    };

    /// <summary>The outdoor approach of a cave whose last steps no plan reaches: a point outside its mouth (the guards' outdoor spots, writ-caves.md), which the run must reach, so the pin names only the way in.</summary>
    private static readonly Dictionary<string, (uint Cell, Vector3 At)> Approach = new(StringComparer.Ordinal)
    {
        ["minor-shivering-stone"] = (0xBDB40000, new Vector3(128.8f, 140.0f, 310.0f)), // 24 m south of the stone, outside the golems' ring
        ["minor-stinging-stone"] = (0xBA600000, new Vector3(131.7f, 149.0f, 6.0f)), // 2.4 m south of a Mosswart Soul Trapper's spot (guid 0x7BA60021)
    };

    /// <summary>The run from each stone's drop (its travel edge's destination) to the stone (<see cref="WritFacts.Caves"/>), planned and confirmed outdoors and into the cave's room.</summary>
    /// <param name="key">The stone's fetch key.</param>
    [RequiresDatsTheory]
    [InlineData("minor-stinging-stone")]
    [InlineData("minor-shivering-stone")]
    [InlineData("minor-sparking-stone")]
    public void The_run_from_the_drop_reaches_the_minor_stone(string key)
    {
        var cave = WritFacts.Caves.Single(c => c.QuestKey == key);
        var drop = Routes.Edge(cave.DropEdge);
        var from = new CellFrame(drop.DestCell, new AcFrame(drop.DestOrigin, Quaternion.Identity));
        using var rig = Rig(from);
        var walk = Walk(rig, from, rig.PointGoal(cave.StoneCell, cave.StonePoint));
        Pinned(key, string.Create(CultureInfo.InvariantCulture, $"quest-walk name={key} from=0x{drop.DestCell:X8} to=0x{cave.StoneCell:X8} metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")}"), walk);
        if (walk.Ok)
        {
            Assert.Equal(cave.StoneCell, walk.End.CellId);
            return;
        }

        if (Approach.TryGetValue(key, out var approach))
        {
            var outside = Walk(rig, from, rig.PointGoal(PortalCell(approach.Cell, approach.At), approach.At));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name={key}-approach to=({approach.At.X}, {approach.At.Y}) metres={outside.Metres:0.0} pass={(outside.Ok ? "yes" : "no")} {outside.Why}"));
            Assert.True(outside.Ok, $"{key}: the outdoor approach failed too: {outside.Why}");
        }
    }

    /// <summary>From Holtburg's lifestone to an NPC or an object the villa's steps use, within the use reach: the Agent of the Arcanum, the Crude Lockpick's Merchant, and the villa's guardian.</summary>
    /// <param name="name">The walk's name.</param>
    [RequiresDatsTheory]
    [InlineData("agent-of-the-arcanum")]
    [InlineData("lockpick-merchant")]
    [InlineData("villa-guardian")]
    public void Holtburgs_lifestone_reaches_the_villa_steps_npc(string name)
    {
        var (guid, cell, at) = name switch
        {
            "agent-of-the-arcanum" => (WritFacts.HoltburgAgent.Guid, WritFacts.HoltburgAgent.Cell, WritFacts.HoltburgAgent.Point),
            "lockpick-merchant" => (LockpickFacts.Merchant.Guid, LockpickFacts.Merchant.Cell, LockpickFacts.Merchant.Point),
            _ => (VillaFacts.Block1401.Houses[0].Guardian.Guid, VillaFacts.Block1401.Houses[0].Guardian.Cell, VillaFacts.Block1401.Houses[0].Guardian.Point),
        };
        var start = new CellFrame(PortalCell(Routes.StartLifestone.Cell, Routes.StartLifestone.Origin), new AcFrame(Routes.StartLifestone.Origin, Quaternion.Identity));
        using var rig = Rig(start);
        var target = PortalCell(cell, at);
        var walk = Walk(rig, start, rig.ObjectGoal(guid, target, at, PathwardenRun.UseRange));
        Pinned(name, string.Create(CultureInfo.InvariantCulture, $"villa-walk name={name} from=0x{start.CellId:X8} to=0x{target:X8} metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")}"), walk);
        if (walk.Ok)
        {
            Assert.True(Vector2.Distance(new Vector2(walk.End.Origin.X, walk.End.Origin.Y), new Vector2(at.X, at.Y)) <= PathwardenRun.UseRange + 0.5, $"the walk ended at {walk.End}");
        }
    }

    /// <summary>The storage trip's two walks: from the guardian (where the house recall lands, ACE <c>HandleActionTeleToHouse</c>: the SlumLord's location) to the house portal, and from the basement's arrival to each storage chest.</summary>
    /// <param name="name">The walk's name.</param>
    [RequiresDatsTheory]
    [InlineData("recall-to-house-portal")]
    [InlineData("basement-to-chest-0")]
    [InlineData("basement-to-chest-1")]
    public void The_storage_trip_walks_reach_the_house_portal_and_the_chests(string name)
    {
        var house = VillaFacts.Block1401.Houses[0];
        var (fromCell, fromAt, guid, cell, at, range) = name switch
        {
            "recall-to-house-portal" => (house.Guardian.Cell, house.Guardian.Point, house.HousePortal.Guid, house.HousePortal.Cell, house.HousePortal.Point, DenTravelStation.PortalRangeMetres),
            "basement-to-chest-0" => (house.BasementArrival.Cell, house.BasementArrival.Point, house.Chests[0].Guid, house.Chests[0].Cell, house.Chests[0].Point, PathwardenRun.UseRange),
            _ => (house.BasementArrival.Cell, house.BasementArrival.Point, house.Chests[1].Guid, house.Chests[1].Cell, house.Chests[1].Point, PathwardenRun.UseRange),
        };
        var start = new CellFrame(PortalCell(fromCell, fromAt), new AcFrame(fromAt, Quaternion.Identity));
        using var rig = Rig(start);
        start = rig.RealRoom(start);
        var target = PortalCell(cell, at);
        var clock = Stopwatch.StartNew();
        var walk = Walk(rig, start, rig.ObjectGoal(guid, target, at, range));
        Pinned(name, string.Create(CultureInfo.InvariantCulture, $"villa-walk name={name} from=0x{start.CellId:X8} to=0x{target:X8} metres={walk.Metres:0.0} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(walk.Ok ? "yes" : "no")}"), walk);
        if (walk.Ok)
        {
            Assert.True(Vector2.Distance(new Vector2(walk.End.Origin.X, walk.End.Origin.Y), new Vector2(at.X, at.Y)) <= range + 0.5, $"the walk ended at {walk.End}");
        }
    }

    /// <summary>One line a walk; a failed walk passes only while <see cref="VillaKnown"/> pins it, and a pin fails once the walk reaches.</summary>
    private void Pinned(string name, string line, WalkResult walk)
    {
        output.WriteLine(line);
        if (!walk.Ok)
        {
            output.WriteLine($"the walk failed: {walk.Why}");
            Assert.True(VillaKnown.TryGetValue(name, out var todo), line + " " + walk.Why);
            output.WriteLine($"pinned: {todo}");
            return;
        }

        Assert.False(VillaKnown.ContainsKey(name), $"{line}: reached, so the pin {VillaKnown.GetValueOrDefault(name)} is stale: remove it from VillaKnown");
    }
}

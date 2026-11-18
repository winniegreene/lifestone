using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.Versioning;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.WorldWalk;

/// <summary>A thing the world places: a portal, lifestone, vendor or NPC, as the pinned world data has it.</summary>
internal sealed record WorldPlace(string Kind, uint Guid, uint Wcid, string Name, uint Cell, Vector3 Origin, Quaternion Rotation, uint Setup, uint State, float Scale, bool IsCreature);

/// <summary>Where portals put a body, once per distinct place.</summary>
internal sealed record WorldDrop(uint Cell, Vector3 Origin, int Portals);

/// <summary>Every place and drop of the pinned world data, by landblock.</summary>
internal sealed record PlaceBook(Dictionary<ushort, List<WorldPlace>> Places, Dictionary<ushort, List<WorldDrop>> Drops, int PortalsWithNoDestination, int NoSetup)
{
    /// <summary>Every landblock with something to walk to, in id order.</summary>
    public IReadOnlyList<LandblockCoord> Landblocks() =>
        [.. Places.Keys.Union(Drops.Keys).Order().Select(id => new LandblockCoord((byte)(id >> 8), (byte)(id & 0xFF)))];
}

/// <summary>Reads the pinned world data's places: two streaming passes over the zip, no database (the weenie table comes after the instances in the dump, so the instances need a second pass, as <see cref="GroundDraft"/>'s do).</summary>
internal static class WorldPlaces
{
    /// <summary>WeenieType Portal (ACE.Entity/Enum/WeenieType.cs @95ef426d).</summary>
    private const int PortalType = 7;

    /// <summary>PropertyBool Attackable (ACE.Entity/Enum/Properties/PropertyBool.cs:33 @95ef426d): a creature without it is an NPC.</summary>
    private const int AttackableProperty = 19;

    /// <summary>PropertyInt PhysicsState (ACE.Entity/Enum/Properties/PropertyInt.cs:137 @95ef426d).</summary>
    private const int PhysicsStateProperty = 93;

    /// <summary>PropertyFloat DefaultScale (ACE.Entity/Enum/Properties/PropertyFloat.cs:52 @95ef426d).</summary>
    private const int DefaultScaleProperty = 39;

    /// <summary>PropertyDataId Setup (ACE.Entity/Enum/Properties/PropertyDataId.cs:11 @95ef426d).</summary>
    private const int SetupProperty = 1;

    /// <summary>A bool row's value: the dump writes the bit column as a one-character string, a raw 0x01 for true and an escaped zero for false.</summary>
    private static bool IsTrue(SqlRow row) => row.Raw("value") switch
    {
        long l => l != 0,
        string s => s.Length > 0 && s[0] != '\0' && s != "\\0",
        _ => false,
    };

    public static PlaceBook Read(string zipPath)
    {
        using var zip = File.OpenRead(zipPath);
        var first = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>(StringComparer.Ordinal)
        {
            ["weenie"] = _ => true,
            ["weenie_properties_d_i_d"] = r => r.L("type") == SetupProperty,
            ["weenie_properties_int"] = r => r.L("type") == PhysicsStateProperty,
            ["weenie_properties_float"] = r => r.L("type") == DefaultScaleProperty,
            ["weenie_properties_bool"] = r => r.L("type") == AttackableProperty,
            ["weenie_properties_string"] = r => r.L("type") == GroundDraft.NameProperty,
            ["weenie_properties_position"] = r => r.L("position_Type") == GroundDraft.DestinationPosition,
        });

        IEnumerable<SqlRow> Rows(string table) => first.TryGetValue(table, out var t) ? t.View() : [];
        var attackable = Rows("weenie_properties_bool").Where(IsTrue).Select(r => (uint)r.L("object_Id")).ToHashSet();
        var kinds = new Dictionary<uint, string>();
        foreach (var r in Rows("weenie"))
        {
            var wcid = (uint)r.L("class_Id");
            var kind = r.L("type") switch
            {
                PortalType => "portal",
                GroundDraft.LifestoneType => "lifestone",
                GroundDraft.VendorType => "vendor",
                GroundDraft.CreatureType when !attackable.Contains(wcid) => "npc",
                _ => null,
            };

            if (kind is not null)
            {
                kinds[wcid] = kind;
            }
        }

        var setups = Rows("weenie_properties_d_i_d").GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => (uint)g.First().L("value"));
        var states = Rows("weenie_properties_int").GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => (uint)g.First().L("value"));
        var scales = Rows("weenie_properties_float").GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => (float)g.First().F("value"));
        var names = Rows("weenie_properties_string").GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.First().S("value") ?? "(no name)");
        var destinations = Rows("weenie_properties_position").GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.First());

        zip.Position = 0;
        var second = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>(StringComparer.Ordinal)
        {
            ["landblock_instance"] = r => kinds.ContainsKey((uint)r.L("weenie_Class_Id")),
        });

        var places = new Dictionary<ushort, List<WorldPlace>>();
        var drops = new Dictionary<(uint Cell, int X, int Y, int Z), (uint Cell, Vector3 Origin, int Portals)>();
        var noDestination = 0;
        var noSetup = 0;
        foreach (var r in second["landblock_instance"].View().OrderBy(r => r.L("guid")))
        {
            var wcid = (uint)r.L("weenie_Class_Id");
            var kind = kinds[wcid];
            var origin = new Vector3((float)r.F("origin_X"), (float)r.F("origin_Y"), (float)r.F("origin_Z"));
            var cell = GroundDraft.OutdoorCell((uint)r.L("obj_Cell_Id"), origin);
            var setup = setups.GetValueOrDefault(wcid);
            noSetup += setup == 0 ? 1 : 0;
            var rotation = new Quaternion((float)r.F("angles_X"), (float)r.F("angles_Y"), (float)r.F("angles_Z"), (float)r.F("angles_W", 1));
            var block = (ushort)(cell >> 16);
            if (!places.TryGetValue(block, out var list))
            {
                places[block] = list = [];
            }

            list.Add(new WorldPlace(kind, (uint)r.L("guid"), wcid, names.GetValueOrDefault(wcid, "(no name)"), cell, origin, rotation, setup, states.GetValueOrDefault(wcid), scales.GetValueOrDefault(wcid, 1f), kind is "npc" or "vendor"));
            if (kind == "portal")
            {
                if (!destinations.TryGetValue(wcid, out var d) || d.L("obj_Cell_Id") == 0)
                {
                    noDestination++;
                    continue;
                }

                var at = new Vector3((float)d.F("origin_X"), (float)d.F("origin_Y"), (float)d.F("origin_Z"));
                var to = GroundDraft.OutdoorCell((uint)d.L("obj_Cell_Id"), at);
                var key = (to, (int)MathF.Round(at.X * 10), (int)MathF.Round(at.Y * 10), (int)MathF.Round(at.Z * 10));
                drops[key] = drops.TryGetValue(key, out var seen) ? (seen.Cell, seen.Origin, seen.Portals + 1) : (to, at, 1);
            }
        }

        var byBlock = drops.Values.OrderBy(d => d.Cell).ThenBy(d => d.Origin.X).ThenBy(d => d.Origin.Y).ThenBy(d => d.Origin.Z)
            .GroupBy(d => (ushort)(d.Cell >> 16)).ToDictionary(g => g.Key, g => g.Select(d => new WorldDrop(d.Cell, d.Origin, d.Portals)).ToList());
        return new PlaceBook(places, byBlock, noDestination, noSetup);
    }
}

/// <summary>
/// W6, the places: every portal, lifestone, vendor and NPC the world places directly, loaded as a real solid object and walked to from a short way off, the way the journeys walk to them; and every place a portal drops a body,
/// which must hold a body and let it walk away. Creatures made by generators are not here (they move), and doors are not loaded (W9's: a door's state is the server's).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PlaceWalks
{
    /// <summary>How near an object a walk ends: the portal exit's stop (<c>PortalExit.DefaultStopWithinMetres</c>), the range the journeys' object goals use.</summary>
    private const float ObjectRangeMetres = 4f;

    /// <summary>A walk's start is this far from the place at most, in straight metres, and this far at least when there is one. Not retail: the walk's own.</summary>
    private const float StartMostMetres = 70f, StartLeastMetres = 30f, StartFallbackMetres = 10f;

    /// <summary>How many doorways away an indoor start may be. Not retail: the walk's own.</summary>
    private const int MostHops = 4;

    /// <summary>How many rooms are tried for a start before the place is recorded as having none.</summary>
    private const int RoomStartTries = 5;

    /// <summary>How far round a place the nearest walkable node may be, in nodes (metres).</summary>
    private const int NearestNodeSearch = 8;

    /// <summary>A walk's limit in simulated seconds. Not retail: the walk's own.</summary>
    private const double WalkSeconds = 120;

    /// <summary>The 5 cm a body is raised when it is refused exactly on a floor (docs/world-walk.md, the room walks' first trial).</summary>
    private const float RaiseMetres = 0.05f;

    public static object Run(WalkContext context, OfflineWalkRig rig, PlaceBook book, LandblockCoord coord)
    {
        var id = Census.IdOf(coord);
        var block = (ushort)((coord.X << 8) | coord.Y);
        var places = book.Places.GetValueOrDefault(block) ?? [];
        var drops = book.Drops.GetValueOrDefault(block) ?? [];
        var cells = LandblockCellSet.Load(context.Structures, coord);
        var graph = cells.Count > 0 ? new RoomGraph(cells, RoomGraph.BuildingDoorwayLean) : null;
        var tile = context.Tile(coord, out _);
        var objects = ObjectsAround(book, coord);
        var run = new Tally(rig, tile, cells, graph, objects);
        var dropCells = drops.Select(d => d.Cell).Where(IsRoom).ToHashSet();
        var cutOff = new List<object>();
        foreach (var place in places)
        {
            if (IsRoom(place.Cell))
            {
                run.WalkToIndoor(place, dropCells, cutOff);
            }
            else
            {
                run.WalkToOutdoor(place);
            }
        }

        foreach (var drop in drops)
        {
            run.WalkOffDrop(drop);
        }

        return new { id, counts = run.Counts, objects = objects.Count, drop_cannot_reach = cutOff, bad = run.Bad };
    }

    private static bool IsRoom(uint cell) => (cell & 0xFFFF) >= 0x100;

    private static string Short(uint cell) => (cell & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture);

    private static List<OfflineObject> ObjectsAround(PlaceBook book, LandblockCoord coord)
    {
        var objects = new List<OfflineObject>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (coord.Offset(dx, dy) is { } near && book.Places.TryGetValue((ushort)((near.X << 8) | near.Y), out var list))
                {
                    objects.AddRange(list.Select(p => new OfflineObject(new ServerObjectDescription(p.Guid, p.Setup, p.Scale, p.State, p.IsCreature, IsSelf: false, DescriptionFlags: null), new CellFrame(p.Cell, new AcFrame(p.Origin, p.Rotation)))));
                }
            }
        }

        return objects;
    }

    /// <summary>The walks of one landblock and what came of them.</summary>
    private sealed class Tally(OfflineWalkRig rig, NavTile tile, LandblockCellSet cells, RoomGraph? graph, List<OfflineObject> objects)
    {
        private readonly Lazy<Dictionary<uint, List<uint>>> _into = new(() => Links(cells, graph, reverse: true));
        private readonly Lazy<Dictionary<uint, List<uint>>> _outOf = new(() => Links(cells, graph, reverse: false));

        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        public List<object> Bad { get; } = [];

        private void Count(string name, int by = 1) => Counts[name] = Counts.GetValueOrDefault(name) + by;

        private static Dictionary<uint, List<uint>> Links(LandblockCellSet cells, RoomGraph? graph, bool reverse)
        {
            var links = new Dictionary<uint, List<uint>>();
            if (graph is null)
            {
                return links;
            }

            foreach (var cellId in cells.Ids)
            {
                var cell = cells.GetCell(cellId)!;
                foreach (var portal in cell.Portals)
                {
                    if (!portal.LeadsOutside && cells.GetCell(portal.OtherCellId) is not null && graph.IsPassable(cell, portal))
                    {
                        var (from, to) = reverse ? (portal.OtherCellId, cellId) : (cellId, portal.OtherCellId);
                        if (!links.TryGetValue(from, out var list))
                        {
                            links[from] = list = [];
                        }

                        list.Add(to);
                    }
                }
            }

            return links;
        }

        private static Dictionary<uint, int> Hops(Dictionary<uint, List<uint>> links, uint from)
        {
            var hops = new Dictionary<uint, int> { [from] = 0 };
            var queue = new Queue<uint>();
            queue.Enqueue(from);
            while (queue.TryDequeue(out var cell))
            {
                if (links.TryGetValue(cell, out var next))
                {
                    foreach (var other in next.Where(o => !hops.ContainsKey(o)))
                    {
                        hops[other] = hops[cell] + 1;
                        queue.Enqueue(other);
                    }
                }
            }

            return hops;
        }

        private OfflineWalkResult Walk(string name, CellFrame start, NavGoal goal, bool withObjects = true) =>
            rig.Walk(new OfflineWalkRequest(name, start, goal, WalkSeconds)
            {
                Objects = withObjects ? objects : null,
                PathSource = cells.Count > 0 ? map => new IndoorOutdoorPathSource(new PathPlanner(map), cells) : null,
            });

        /// <summary>The control for a walk that went wrong: the same walk with no server objects in the world, so a failure the objects caused is told from one the ground caused.</summary>
        private string Control(string name, CellFrame start, NavGoal goal)
        {
            var walk = Walk(name + "-bare", start, goal, withObjects: false);
            return walk.Clean ? "clean" : walk.State?.ToString() ?? "over-time";
        }

        private static NavGoal GoalOf(WorldPlace place) =>
            NavGoal.ToObject(place.Guid, new NavPoint(place.Cell & 0xFFFF0000u, place.Origin), ObjectRangeMetres, needSight: false, IsRoom(place.Cell) ? place.Cell : null);

        private void Record(string kind, string what, string from, OfflineWalkResult walk, CellFrame start, NavGoal goal, ref bool reached)
        {
            Count(kind + "_walks");
            Count(kind + "_clean", walk.Clean ? 1 : 0);
            reached |= walk.State == NavRunState.Arrived;
            if (!walk.Clean)
            {
                Bad.Add(new { kind, what, from, without_objects = Control(walk.Name, start, goal), state = walk.State?.ToString(), reason = walk.Reason, rungs = walk.Rungs, refused = walk.ConfirmFailures, why = walk.RefusedWhy, rungs_at = walk.RungsAt, left_m = Math.Round(walk.LeftMetres, 2), route_m = Math.Round(walk.RouteMetres, 1) });
            }
        }

        private void Named(string kind, string what, string state)
        {
            Count(kind + "_" + state);
            Bad.Add(new { kind, what, state });
        }

        private static string What(WorldPlace p) => string.Create(CultureInfo.InvariantCulture, $"{p.Name} wcid {p.Wcid} guid 0x{p.Guid:X8} cell {Short(p.Cell)} ({p.Origin.X:F1},{p.Origin.Y:F1},{p.Origin.Z:F1})");

        public void WalkToOutdoor(WorldPlace place)
        {
            Count(place.Kind + "_places");
            var what = What(place);
            if (Nearest(new Vector2(place.Origin.X, place.Origin.Y)) is not { } node)
            {
                Named(place.Kind, what, "no-ground-near");
                return;
            }

            var starts = Starts(node);
            if (starts.Count == 0)
            {
                Named(place.Kind, what, "no-start");
                return;
            }

            var reached = false;
            var goal = GoalOf(place);
            for (var i = 0; i < starts.Count; i++)
            {
                var from = NavMap.ToCellFrame(WalkContext.PointOf(tile, starts[i].X, starts[i].Y));
                Record(place.Kind, what, "ground", Walk($"{place.Guid:X8}-g{i}", from, goal), from, goal, ref reached);
            }

            Count(place.Kind + "_reached", reached ? 1 : 0);
        }

        public void WalkToIndoor(WorldPlace place, HashSet<uint> dropCells, List<object> cutOff)
        {
            Count(place.Kind + "_places");
            var what = What(place);
            if (graph is null)
            {
                Named(place.Kind, what, "cell-not-found");
                return;
            }

            var hops = Hops(_into.Value, place.Cell);
            var goal = GoalOf(place);
            var reached = false;
            var walked = false;
            var exit = graph.ExitsToOutside.Where(e => hops.ContainsKey(e.CellId)).OrderBy(e => hops[e.CellId]).ThenBy(e => e.CellId).Select(e => (RoomExit?)e).FirstOrDefault();
            if (exit is { } door && RoomWalks.OutsidePoint(tile, door) is { } outside)
            {
                walked = true;
                var from = NavMap.ToCellFrame(outside);
                Record(place.Kind, what, "outside", Walk($"{place.Guid:X8}-out", from, goal), from, goal, ref reached);
            }
            else if (exit is null && dropCells.Count > 0 && !dropCells.Overlaps(hops.Keys))
            {
                // Map only: nothing a portal drops a body in joins this place, and it has no way outside.
                cutOff.Add(new { kind = place.Kind, what, rooms_joined = hops.Count });
                Count(place.Kind + "_drop_cannot_reach");
            }

            var tries = 0;
            foreach (var (room, _) in hops.Where(h => h.Key != place.Cell && h.Value <= MostHops).OrderByDescending(h => h.Value).ThenBy(h => h.Key))
            {
                if (tries == RoomStartTries || graph.RoomFrame(room) is not { } frame)
                {
                    if (tries == RoomStartTries)
                    {
                        break;
                    }

                    continue;
                }

                tries++;
                var start = new CellFrame(room, new AcFrame(frame.Origin + new Vector3(0, 0, RaiseMetres), Quaternion.Identity));
                var walk = Walk($"{place.Guid:X8}-r{room & 0xFFFF:X4}", start, goal);
                if (walk.Reason == OfflineWalkRig.StartRefused)
                {
                    continue;
                }

                walked = true;
                Record(place.Kind, what, "room " + Short(room), walk, start, goal, ref reached);
                break;
            }

            if (!walked)
            {
                Named(place.Kind, what, "no-start");
                return;
            }

            Count(place.Kind + "_reached", reached ? 1 : 0);
        }

        public void WalkOffDrop(WorldDrop drop)
        {
            Count("drop_places");
            var what = string.Create(CultureInfo.InvariantCulture, $"drop cell {Short(drop.Cell)} ({drop.Origin.X:F1},{drop.Origin.Y:F1},{drop.Origin.Z:F1}) from {drop.Portals} portals");
            if (Away(drop) is not { } goal)
            {
                Named("drop", what, "nowhere-to-walk");
                return;
            }

            var start = new CellFrame(drop.Cell, new AcFrame(drop.Origin, Quaternion.Identity));
            var walk = Walk($"drop-{drop.Cell:X8}", start, goal);
            if (walk.Reason == OfflineWalkRig.StartRefused)
            {
                Count("drop_raised");
                start = new CellFrame(drop.Cell, new AcFrame(drop.Origin + new Vector3(0, 0, RaiseMetres), Quaternion.Identity));
                walk = Walk($"drop-{drop.Cell:X8}-r", start, goal);
            }

            if (walk.Reason == OfflineWalkRig.StartRefused)
            {
                Named("drop", what, "no-stand");
                return;
            }

            var reached = false;
            Record("drop", what, "drop", walk, start, goal, ref reached);
            Count("drop_reached", reached ? 1 : 0);
        }

        /// <summary>A goal a short walk from the drop, indoors the room furthest within a few doorways, outdoors a node a short way off; null when there is none.</summary>
        private NavGoal? Away(WorldDrop drop)
        {
            if (!IsRoom(drop.Cell))
            {
                if (Nearest(new Vector2(drop.Origin.X, drop.Origin.Y)) is not { } node || Starts(node) is not { Count: > 0 } far)
                {
                    return null;
                }

                return NavGoal.ToPoint(WalkContext.PointOf(tile, far[0].X, far[0].Y));
            }

            if (graph is null)
            {
                return null;
            }

            var hops = Hops(_outOf.Value, drop.Cell);
            foreach (var (room, _) in hops.Where(h => h.Key != drop.Cell && h.Value <= MostHops).OrderByDescending(h => h.Value).ThenBy(h => h.Key))
            {
                if (graph.RoomFrame(room) is { } frame)
                {
                    return NavGoal.ToPoint(new NavPoint(room & 0xFFFF0000u, frame.Origin), room);
                }
            }

            var exit = graph.ExitsToOutside.Where(e => hops.ContainsKey(e.CellId)).OrderBy(e => hops[e.CellId]).ThenBy(e => e.CellId).Select(e => (RoomExit?)e).FirstOrDefault();
            return exit is { } door && RoomWalks.OutsidePoint(tile, door) is { } outside ? NavGoal.ToPoint(outside) : null;
        }

        private (int X, int Y)? Nearest(Vector2 local)
        {
            var (cx, cy) = NavTile.NodeAt(local);
            for (var r = 0; r <= NearestNodeSearch; r++)
            {
                for (var y = cy - r; y <= cy + r; y++)
                {
                    for (var x = cx - r; x <= cx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) == r && NavTile.InBounds(x, y) && tile.IsWalkable(x, y) && (tile.Flags[NavTile.Index(x, y)] & NavFlags.Water) == 0)
                        {
                            return (x, y);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>Up to two starts joined to the node on foot inside the tile: the furthest in range, and the one furthest from that, so a place is come at from two sides.</summary>
        private List<(int X, int Y)> Starts((int X, int Y) node)
        {
            var seen = new bool[NavTile.Size * NavTile.Size];
            var queue = new Queue<(int X, int Y)>();
            seen[NavTile.Index(node.X, node.Y)] = true;
            queue.Enqueue(node);
            var near = new List<(int X, int Y, float D)>();
            while (queue.TryDequeue(out var at))
            {
                var d = MathF.Sqrt(((at.X - node.X) * (at.X - node.X)) + ((at.Y - node.Y) * (at.Y - node.Y)));
                if (d <= StartMostMetres && d >= StartFallbackMetres && tile.Clearance[NavTile.Index(at.X, at.Y)] >= 2)
                {
                    near.Add((at.X, at.Y, d));
                }

                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var (x, y) = (at.X + dx, at.Y + dy);
                    if (NavTile.InBounds(x, y) && !seen[NavTile.Index(x, y)] && tile.IsWalkable(x, y) && (tile.Flags[NavTile.Index(x, y)] & NavFlags.Water) == 0)
                    {
                        seen[NavTile.Index(x, y)] = true;
                        queue.Enqueue((x, y));
                    }
                }
            }

            if (near.Count == 0)
            {
                return [];
            }

            var pool = near.Any(n => n.D >= StartLeastMetres) ? near.Where(n => n.D >= StartLeastMetres).ToList() : near;
            var first = pool.OrderByDescending(n => n.D).ThenBy(n => NavTile.Index(n.X, n.Y)).First();
            var second = pool.OrderByDescending(n => ((n.X - first.X) * (n.X - first.X)) + ((n.Y - first.Y) * (n.Y - first.Y))).ThenBy(n => NavTile.Index(n.X, n.Y)).First();
            return second.X == first.X && second.Y == first.Y ? [(first.X, first.Y)] : [(first.X, first.Y), (second.X, second.Y)];
        }
    }
}

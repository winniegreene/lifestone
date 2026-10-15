using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What a <see cref="RoutePlay"/> that ends at a portal needs (the Academy exit, ST16 part 2): the rooms of the dungeon
/// to plan the walk through, and how far from the portal the walk stops. The portal itself is a world-database object the
/// server creates, so the route is planned once the player stands at its start and the portal's CreateObject is in the
/// session's objects.
/// </summary>
/// <param name="Cells">The dungeon's cells (<see cref="LandblockCellSet.Coord"/> is "inside": a teleport to any other
/// landblock is the exit).</param>
/// <param name="PortalName">The name of the portal to leave through (a case-insensitive part of the object name). ACE names the
/// Academy portals for where they lead (world database v0.9.296, landblock 0x8602): "Exit to Holtburg" is the only one with a
/// destination (Holtburg) and needs the Sentry's quest (<paramref name="QuestName"/>: a character without it is told it may not
/// use it, WeenieError 0x0474); "Central Courtyard" and "Outer Courtyard" have no destination at all.</param>
/// <param name="StopWithinMetres">The distance from the portal at which the walk stops and the player uses it, so that the
/// server has to bring the player the rest of the way, as it does for a person who clicks a portal from across the room
/// (ACE <c>Player.CreateMoveToChain</c> sends a MoveToObject, which the client's <c>MoveToManager</c> carries out).</param>
/// <param name="PortalCell">The cell the world database puts the exit portal in, or null when it is not known. The Academy's rooms
/// are not one connected set (the start's 176 rooms and the exit's wing share no doorway, measured on the dats), so a walk that
/// starts at the Academy start cannot reach the portal: with a cell given, <see cref="WingStart"/> picks a room of the portal's wing
/// for the scenario to be sent to (<c>place loc</c>) and walk from.</param>
/// <param name="Home">Where the character is sent back to when the walk is over (the Academy start, where a new character
/// begins), or null for the route's start.</param>
/// <param name="QuestName">The quest the portal needs held (ACE's <c>Portal</c> checks the character's quest registry before it
/// teleports): the Sentry's <see cref="DefaultQuestName"/> for "Exit to Holtburg". See <see cref="ExitQuestMode"/>.</param>
public sealed record PortalExit(
    LandblockCellSet Cells,
    string PortalName = PortalExit.DefaultPortalName,
    float StopWithinMetres = PortalExit.DefaultStopWithinMetres,
    uint? PortalCell = null,
    CellFrame? Home = null,
    string QuestName = PortalExit.DefaultQuestName)
{
    /// <summary>The exit proper: ACE's "Exit to Holtburg" (weenie 29338, cell 0x86020169).</summary>
    public const string DefaultPortalName = "Exit to Holtburg";

    /// <summary>The quest "Exit to Holtburg" needs the character to hold (world database v0.9.296, quest 4554: one solve, no delay; ACE's Portal.cs:246-255 refuses a character that lacks it or could solve it again).</summary>
    public const string DefaultQuestName = "SentryTaskComplete";

    /// <summary>The cell the world database v0.9.296 (<c>landblock_instance</c>) puts "Exit to Holtburg" in, at (158.64, -149.52, -6.06).</summary>
    public const uint ExitToHoltburgCell = 0x86020169;

    /// <summary>How many doorways from the portal's cell the wing start is at most (far enough to cross rooms, near enough to see the portal soon).</summary>
    public const int WingDoorways = 4;

    /// <summary>The default stop distance, in metres: outside the portal's own reach, so the Use has a MoveTo to make.</summary>
    public const float DefaultStopWithinMetres = 4f;

    /// <summary>The most search legs (walks to a room not seen yet) before the scenario gives up looking for the portal.</summary>
    public const int MaxSearchLegs = 8;

    /// <summary>How near the search target the search walk stops, in metres.</summary>
    public const float SearchStopWithinMetres = 1f;

    /// <summary>
    /// Where to look for a portal that is not in the session objects yet (the server tells the client only of the objects
    /// the player cell can see): the room most doorways from <paramref name="from"/> that is not in
    /// <paramref name="exclude"/>, at its largest floor centre.
    /// </summary>
    /// <returns>The frame, or null when no other room can be walked to.</returns>
    public CellFrame? FarthestRoom(uint from, IReadOnlySet<uint>? exclude = null)
    {
        var planner = new CellRoutePlanner(Cells);
        var best = (Cell: from, Depth: 0);
        foreach (var id in Cells.Ids.OrderBy(i => i))
        {
            if (exclude?.Contains(id) != true && planner.FindPath(from, id) is { } path && path.Count - 1 > best.Depth)
            {
                best = (id, path.Count - 1);
            }
        }

        return best.Depth == 0 ? null : RoomFrame(best.Cell);
    }

    /// <summary>
    /// A room of the wing the portal is in (<see cref="PortalCell"/>), for the scenario to start in: the room farthest from the portal
    /// cell by doorways that is no more than <paramref name="maxDoorways"/> from it and from which a walk to it through walkable
    /// doorways exists, at its largest floor centre; the portal cell itself when it has no such neighbour.
    /// </summary>
    /// <returns>The frame, or null when no portal cell is known or it is not one of the dungeon's rooms.</returns>
    public CellFrame? WingStart(int maxDoorways = WingDoorways)
    {
        if (PortalCell is not { } portalCell || Cells.GetCell(portalCell) is null)
        {
            return null;
        }

        var planner = new CellRoutePlanner(Cells);
        var best = (Cell: portalCell, Depth: 0);
        foreach (var id in Cells.Ids.OrderBy(i => i))
        {
            if (planner.FindPath(id, portalCell) is { } path && path.Count - 1 is var depth && depth <= maxDoorways && depth > best.Depth)
            {
                best = (id, depth);
            }
        }

        return RoomFrame(best.Cell);
    }

    private CellFrame? RoomFrame(uint cellId)
    {
        if (Cells.GetCell(cellId) is not { } cell)
        {
            return null;
        }

        var floor = Cells.GetStructure(cell).Floors.OrderByDescending(f => f.Area).First();
        return new CellFrame(cell.Id, new AcFrame(cell.Frame.LocalToParent(floor.Centroid), Quaternion.Identity));
    }
}

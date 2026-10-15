using Lifestone.Client.Diagnostics;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Play;

/// <summary>
/// Where streaming looks while the player exists: the player's own position, and the dungeon rule. In an EnvCell that cannot
/// see outside the landscape is released and only the cell's own landblock is kept (<see cref="StreamingFocusKind.Indoor"/>);
/// anywhere else (outdoors, or in a building cell that sees outside) the ring around the player's position loads.
/// </summary>
/// <remarks>
/// <para><b>Retail.</b> <c>CellManager::ChangePosition</c> (11.4186 acclient.c:146646; docs/interiors.md §5, docs/objects.md §2.6)
/// releases the landscape (<c>LScape::release_all</c>) when the viewer's cell is an EnvCell that is not <c>seen_outside</c>, and
/// keeps no outdoor blocks around it. M1's explore camera took this rule from the interiors' cell tracker
/// (<c>InteriorController.AdjustFocus</c>); the player's focus comes from the player's cell instead, so it stays correct when the
/// camera is somewhere else (a chase camera orbiting outside a dungeon wall, a free camera).</para>
/// <para><b>The centre.</b> A dungeon's cells can lie hundreds of metres outside the 192 m square of the landblock they belong
/// to, so the indoor focus is the centre of the cell's landblock, not the player's position (the position would pick a landblock
/// that is not the one the cells are in). Its height is the player's.</para>
/// <para><b>Cells whose data is missing.</b> <see cref="Of"/> asks the classifier once per cell and remembers the answer. A cell
/// the classifier cannot answer for (null: the dats have no such EnvCell) is logged once as a problem and treated as outdoors,
/// the focus that always loads something to look at.</para>
/// </remarks>
public sealed class PlayerStreamingFocus
{
    private const string LogCategory = "play";

    private readonly Func<uint, bool?> _seesOutside;
    private readonly ILogSink _log;
    private readonly Dictionary<uint, bool> _dungeonCells = [];

    /// <param name="seesOutside">Whether an EnvCell can see outside (its <c>seen_outside</c> flag; the dat read), or null when the cell cannot be read.</param>
    /// <param name="log">Where problems are reported.</param>
    public PlayerStreamingFocus(Func<uint, bool?> seesOutside, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(seesOutside);
        _seesOutside = seesOutside;
        _log = log ?? NullLogSink.Instance;
    }

    /// <summary>The streaming focus for a player at <paramref name="position"/>.</summary>
    public StreamingFocus Of(in CellFrame position)
    {
        if (IsDungeonCell(position.CellId))
        {
            var origin = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(position.CellId));
            var half = AcCoordinates.LandblockLength / 2;
            return new StreamingFocus(new GlobalPosition(origin.X + half, origin.Y + half, position.Origin.Z), StreamingFocusKind.Indoor);
        }

        return new StreamingFocus(GlobalOf(position), StreamingFocusKind.Outdoor);
    }

    /// <summary>True when <paramref name="cellId"/> is an EnvCell that cannot see outside (the cell of a dungeon).</summary>
    public bool IsDungeonCell(uint cellId)
    {
        if (!AcCoordinates.IsEnvCell(cellId))
        {
            return false;
        }

        if (_dungeonCells.TryGetValue(cellId, out var dungeon))
        {
            return dungeon;
        }

        var sees = _seesOutside(cellId);
        if (sees is null)
        {
            _log.Warn(LogCategory, "the player's cell does not exist in the dats; its streaming focus is outdoors", new LogField("cell", $"0x{cellId:X8}"));
        }

        dungeon = sees == false;
        _dungeonCells[cellId] = dungeon;
        return dungeon;
    }

    /// <summary>A player position in global metres (the cell's landblock corner plus the position in it).</summary>
    public static GlobalPosition GlobalOf(in CellFrame position) =>
        AcCoordinates.ToGlobal(new CellPosition(position.CellId, position.Origin));
}

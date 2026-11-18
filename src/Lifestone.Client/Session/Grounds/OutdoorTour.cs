using System.Globalization;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// M6 item 0 a5 (D33, the mage's outdoor fight): the <see cref="IGroundTour"/> of an outdoor ground: the profile's <c>outdoor.cells</c> (<see cref="OutdoorGround.Cells"/>) in the file's order, round and round, skipping the cell the
/// player stands in. Each offer's purpose is <see cref="TourPurpose.Order"/> (D47: the walk goes to the next cell of the profile to look for targets there); the walk (<c>OutdoorFightWalk</c>) makes each cell a point goal at
/// its spot through the travel seam. Nothing here is one ground's: the cells, their spots and their order are the profile's (D26).
/// </summary>
/// <remarks>Game thread only; one instance serves one walk.</remarks>
public sealed class OutdoorTour : IGroundTour
{
    private readonly IReadOnlyList<OutdoorCell> _cells;
    private int _next;
    private int _offered = -1;

    /// <summary>A tour over <paramref name="ground"/>'s outdoor cells, from the first.</summary>
    /// <param name="ground">An outdoor ground with cells (<see cref="FightTargets.IsOutdoorFight"/>).</param>
    /// <exception cref="ArgumentException">The ground has no outdoor cells.</exception>
    public OutdoorTour(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        _cells = ground.Outdoor is { Cells.Count: > 0 } outdoor
            ? outdoor.Cells
            : throw new ArgumentException($"'{ground.Key}' has no outdoor cells: the outdoor tour walks the profile's outdoor.cells", nameof(ground));
    }

    /// <inheritdoc/>
    public int Waypoints => _cells.Count;

    /// <summary>The index in the profile's cells of the last offer, or -1 before the first.</summary>
    public int Offered => _offered;

    /// <inheritdoc/>
    public TourPurpose Purpose => TourPurpose.Order;

    /// <inheritdoc/>
    public string PurposeNote => _offered < 0
        ? "the profile's outdoor cells in order"
        : string.Create(CultureInfo.InvariantCulture, $"the profile's outdoor cell {_offered + 1} of {_cells.Count}: look for targets there");

    /// <inheritdoc/>
    public uint? Next(uint from, double now)
    {
        for (var tries = 0; tries < _cells.Count; tries++)
        {
            var index = _next++ % _cells.Count;
            if (_cells[index].Cell != from)
            {
                _offered = index;
                return _cells[index].Cell;
            }
        }

        return null;
    }

    /// <summary>The profile's cell of id <paramref name="cell"/>, or null when the tour has none.</summary>
    /// <param name="cell">The outdoor cell id.</param>
    public OutdoorCell? CellOf(uint cell)
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            if (_cells[i].Cell == cell)
            {
                return _cells[i];
            }
        }

        return null;
    }
}

/// <summary>
/// M6 item 0 a5: the creatures a fight hunts on a ground and the cells they count in, for a dungeon and an outdoor ground alike. A dungeon's are its profile's <c>targets</c> on its walkable cells (exactly
/// <see cref="GroundRules.TargetWcids"/> and <see cref="HuntingGround.IsWalkable"/>); an outdoor ground with no listed targets hunts its <c>outdoor.creatures</c> within its <c>level_band</c>, on the outdoor cells of its
/// landblocks that are not no-go. The character's own band (<see cref="Band"/>) lowers the band's top to the character's level + the rung's margin (<c>target_margin</c> of the ladder file, review 0-R5).
/// </summary>
public static class FightTargets
{
    /// <summary>The low word of a cell id from which the cells are indoor (an EnvCell); an outdoor ground's targets count on its outdoor (land) cells only.</summary>
    public const uint FirstIndoorCell = 0x0100;

    /// <summary>True when <paramref name="ground"/> is an outdoor ground the fight can walk: not a dungeon, with outdoor cells in its profile.</summary>
    /// <param name="ground">The ground.</param>
    public static bool IsOutdoorFight(this HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Kind != GroundKind.Dungeon && ground.Outdoor is { Cells.Count: > 0 };
    }

    /// <summary>
    /// The ground's target creatures: a dungeon's (or any ground's that lists targets) its <c>targets</c> of role target, in the profile's order; an outdoor ground's with none listed, its <c>outdoor.creatures</c> whose level is
    /// in its <c>level_band</c>, once per weenie class, in the table's order. Built per call: the callers keep the list.
    /// </summary>
    /// <param name="ground">The ground.</param>
    public static IReadOnlyList<GroundTarget> Of(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var listed = ground.Targets.Where(t => t.Role == GroundRole.Target).ToArray();
        if (listed.Length > 0 || !ground.IsOutdoorFight())
        {
            return listed;
        }

        var band = ground.LevelBand;
        var seen = new HashSet<uint>();
        return ground.Outdoor!.Creatures
            .Where(c => c.Level >= band.Min && c.Level <= band.Max && ground.RoleOf(c.Wcid) != GroundRole.Avoid && seen.Add(c.Wcid))
            .Select(c => new GroundTarget(c.Wcid, c.Name, c.Level, GroundRole.Target, c.MaxHealth) { ResistHealthDrain = c.ResistHealthDrain })
            .ToArray();
    }

    /// <summary>The weenie class ids of <see cref="Of"/>, in its order.</summary>
    /// <param name="ground">The ground.</param>
    public static IReadOnlyList<uint> Wcids(HuntingGround ground) => Of(ground).Select(t => t.Wcid).ToArray();

    /// <summary>The cells a target counts in: a dungeon's walkable cells (<see cref="HuntingGround.IsWalkable"/>); an outdoor ground's outdoor cells of its landblocks that are not no-go.</summary>
    /// <param name="ground">The ground.</param>
    public static Func<uint, bool> CellFilter(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.IsOutdoorFight() ? cell => (cell & 0xFFFF) < FirstIndoorCell && ground.InLandblocks(cell) && !ground.IsNoGo(cell) : ground.IsWalkable;
    }

    /// <summary>
    /// The character's band over <paramref name="targets"/>: the weenie class ids whose level is no higher than <paramref name="level"/> + <paramref name="margin"/>, and the targets above it (skipped by the pick; an attacker of
    /// theirs is still fought back, M5 D88).
    /// </summary>
    /// <param name="targets">The ground's targets (<see cref="Of"/>).</param>
    /// <param name="level">The character's level.</param>
    /// <param name="margin">The levels above the character's own that the pick still takes: the rung's <c>target_margin</c> (M6 0 F5, 0-R5; D26: a rung's fact, not a constant).</param>
    public static (IReadOnlyList<uint> Kept, IReadOnlyList<GroundTarget> Skipped) Band(IReadOnlyList<GroundTarget> targets, int level, int margin)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var kept = new List<uint>();
        var skipped = new List<GroundTarget>();
        foreach (var t in targets)
        {
            if (t.Level > level + margin)
            {
                skipped.Add(t);
            }
            else
            {
                kept.Add(t.Wcid);
            }
        }

        return (kept, skipped);
    }
}

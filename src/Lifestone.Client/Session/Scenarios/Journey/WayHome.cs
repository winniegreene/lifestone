using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>What a character with no Lifestone Recall in its spellbook does to get home (M6 item 0 a6).</summary>
public enum WayHomeKind
{
    /// <summary>Cast Lifestone Recall (the spellbook holds it, or has not been read yet: the recall is tried, as before).</summary>
    Recall,

    /// <summary>Walk to the home's lifestone point through the travel seam (a point goal): the ground is outdoors and hunted from home on foot.</summary>
    Walk,

    /// <summary>No way: a loud stop by name (the first tie's walk and every other case are item 4's, <c>TODO(m6-first-tie-walk)</c>).</summary>
    Stop,
}

/// <summary>The way home the rule chose and why (the journey logs it as the purpose of the walk, M5 D47).</summary>
/// <param name="Kind">The way.</param>
/// <param name="Why">The reason, in a sentence for the log and the stop.</param>
public sealed record WayHomeChoice(WayHomeKind Kind, string Why);

/// <summary>
/// The way home and the way to an outdoor ground, as pure rules (M6 item 0 a6; the stations read the spellbook and the ground and act on the answer). A character whose spellbook holds Lifestone Recall casts it (BSD Mage, always: its build teaches
/// it); a climber that has not learned it (Lifestone Recall is a level 3 Item spell) <b>walks</b> home from an outdoor ground that the profile says is hunted from home on foot (no portal, cells listed: the belt, the forest), and stops by name
/// anywhere else. The arrival at such a ground is the same walk the other way: to its first cell's point, never a portal or a place.
/// </summary>
public static class WayHomeRule
{
    /// <summary>
    /// The way home from <paramref name="ground"/> for a character with <paramref name="book"/>. A spellbook that has not been read yet is not a reason to walk (the recall is tried and fails by its own name, as before); one that is read and
    /// lacks <paramref name="recallSpell"/> walks from an outdoor ground reachable on foot and stops anywhere else.
    /// </summary>
    /// <param name="ground">The journey's ground (its home is the lifestone to reach).</param>
    /// <param name="book">The character's spellbook in the read model.</param>
    /// <param name="recallSpell">The recall spell (<see cref="RecallHome.LifestoneRecallSpell"/>).</param>
    public static WayHomeChoice Choose(HuntingGround ground, Spellbook book, uint recallSpell = RecallHome.LifestoneRecallSpell)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(book);
        if (!book.IsKnown)
        {
            return new(WayHomeKind.Recall, "the spellbook is not read yet; the recall is tried (it fails by name if the book lacks it)");
        }

        if (book.Knows(recallSpell))
        {
            return new(WayHomeKind.Recall, $"the spellbook holds {RecallHome.SpellName(recallSpell)}");
        }

        return HomeOnFoot(ground)
            ? new(WayHomeKind.Walk, $"the spellbook lacks {RecallHome.SpellName(recallSpell)} and {ground.Key} is an outdoor ground hunted from {ground.Home.Town} on foot (no portal): the way home is a walk to {ground.Home.Town}'s lifestone point")
            : new(WayHomeKind.Stop, string.Create(CultureInfo.InvariantCulture, $"the spellbook lacks {RecallHome.SpellName(recallSpell)} and {ground.Key} is not an outdoor ground reachable on foot from {ground.Home.Town} (portal {(ground.Portal is null ? "none" : "yes")}, kind {ground.Kind}); the first tie's walk is item 4's (TODO(m6-first-tie-walk))"));
    }

    /// <summary>True when the ground is outdoors, has no portal and lists cells: it is hunted from its home on foot, so home is reachable on foot from it (the profile's own facts, never a constant).</summary>
    /// <param name="ground">The ground.</param>
    public static bool HomeOnFoot(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Kind == GroundKind.Outdoor && ground.Portal is null && ground.Outdoor is { Cells.Count: > 0 };
    }

    /// <summary>The cell an outdoor ground is entered at on foot (its first listed cell, <c>outdoor.cells[0]</c>), or null for a dungeon or a ground that lists none.</summary>
    /// <param name="ground">The ground.</param>
    public static OutdoorCell? FirstCell(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Kind == GroundKind.Outdoor && ground.Outdoor is { Cells.Count: > 0 } outdoor ? outdoor.Cells[0] : null;
    }

    /// <summary>
    /// The hunt cell for <paramref name="cell"/> (its stop point is the profile's spot), so the terrain height and the standing point are read the way the hunt reads them (<see cref="HuntGround.TerrainHeight"/>, <see cref="HuntGround.StopPoint"/>).
    /// </summary>
    /// <param name="cell">The outdoor cell.</param>
    public static HuntCell HuntCellOf(OutdoorCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return new HuntCell(cell.Cell, cell.North, cell.East, 0, cell.Terrain switch { GroundTerrain.Water => HuntTerrain.Water, GroundTerrain.Steep => HuntTerrain.Steep, GroundTerrain.Dry => HuntTerrain.Dry, _ => HuntTerrain.Unread }) { Spot = new System.Numerics.Vector2(cell.Spot.X, cell.Spot.Y) };
    }
}

using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 AH (the owner's direction: on a corpse run get another robe and gloves, buff, head out): the death handler's <c>replace</c> step, between the rewear and the rebuff. Pure over the state and the ground's profile: which of the
/// floor's pieces a respawned mage is missing (a robe, gauntlets and, when the body holds no caster at all, one caster), the floor's plan for them (<see cref="GearFloor.Plan"/>, the lines only, never the kit and never a mana charge), and
/// which corpse entries the replacement already stands for (<see cref="LeftOnCorpse"/>, the take-back's named leave).
/// </summary>
/// <remarks>
/// <para><b>When it fires.</b> ACE drops nothing worn or held below level 35 and never a bonded piece (<c>Player_Death.cs</c> <c>canDropWielded</c>; the facts are <see cref="DeathDropModel"/>'s), so for a real death the step is a logged <c>replace=none</c>
/// until level 35 and then only for an unbonded pair of gauntlets; a staged death that undresses the mage (M6 7 AE) puts the pieces in the pack, which the rewear wore. A mage that lost its only caster (<c>rearm=corpse</c>) is the one that buys a Wand here.</para>
/// <para><b>One caster, not two.</b> The floor wants <see cref="GearFloor.CastersWanted"/> casters held; the replace step buys the missing <i>one</i>, so it asks the floor with the spare counted (a second Wand is a loot trip's, not the death's).</para>
/// </remarks>
public static class DeathReplace
{
    /// <summary><c>replace=none</c>: nothing the floor wants was missing.</summary>
    public const string None = "none";

    /// <summary><c>replace=short</c>: a piece was missing and could not be bought (the purse, no vendor, a vendor not reached): the handling goes on.</summary>
    public const string Short = "short";

    /// <summary><c>replace=failed</c>: the trip or the wear failed (a failed walk or buy, the time, a piece bought and not worn): the handling goes on.</summary>
    public const string Failed = "failed";

    /// <summary>The floor's kinds the step replaces (the mana charge is the loot trip's).</summary>
    public static readonly IReadOnlyList<GearFloorKind> Kinds = [GearFloorKind.Caster, GearFloorKind.Robe, GearFloorKind.Gauntlets];

    /// <summary>The kinds the corpse leaves a piece for once the replacement is worn (a caster is never left: the original is the spare, <see cref="LeftOnCorpse"/>).</summary>
    public static readonly IReadOnlyList<GearFloorKind> LeftKinds = [GearFloorKind.Robe, GearFloorKind.Gauntlets];

    /// <summary>The short name of a kind for the log and the verdict (<c>caster</c>, <c>robe</c>, <c>gauntlets</c>).</summary>
    /// <param name="kind">The floor kind.</param>
    public static string Name(GearFloorKind kind) => kind switch
    {
        GearFloorKind.Caster => "caster",
        GearFloorKind.Robe => "robe",
        GearFloorKind.Gauntlets => "gauntlets",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>The kinds <paramref name="state"/> is missing from <paramref name="floorLevel"/> up: a robe (no chest piece worn), gauntlets (no worn piece covers the hands) and a caster (none wielded or held). Empty under the floor level.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="floorLevel">The floor level (<see cref="GearFloor.FloorLevel"/>).</param>
    public static IReadOnlyList<GearFloorKind> Missing(GameState state, int floorLevel)
    {
        ArgumentNullException.ThrowIfNull(state);
        var missing = new List<GearFloorKind>();
        if (state.Self.Qualities.Level < floorLevel)
        {
            return missing;
        }

        var floor = GearFloor.StateOf(state, 0);
        if (floor.Casters == 0)
        {
            missing.Add(GearFloorKind.Caster);
        }

        if (!floor.ChestCovered)
        {
            missing.Add(GearFloorKind.Robe);
        }

        if (!floor.HandsCovered)
        {
            missing.Add(GearFloorKind.Gauntlets);
        }

        return missing;
    }

    /// <summary>The floor's plan for the missing pieces of <paramref name="state"/> on <paramref name="ground"/>: the floor's lines for a robe, gauntlets and one caster, never a mana charge (see the class remarks).</summary>
    /// <param name="ground">The ground: its vendors and their prices.</param>
    /// <param name="state">The read model.</param>
    /// <param name="purse">The pyreals the trip can spend.</param>
    /// <param name="floorLevel">The floor level.</param>
    public static GearFloorPlan Plan(HuntingGround ground, GameState state, long purse, int floorLevel)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(state);
        var floor = GearFloor.StateOf(state, purse);
        var asked = floor with { Casters = floor.Casters == 0 ? GearFloor.CastersWanted - 1 : GearFloor.CastersWanted };
        return GearFloor.Plan(ground, asked, floorLevel, null, SelfEquipJudge.Worn(state));
    }

    /// <summary>
    /// True when <paramref name="item"/> (a corpse entry) is a piece whose slot a replacement of <paramref name="replaced"/> already fills, so the take-back leaves it on the corpse (<see cref="LeftKinds"/>): a robe while a chest piece is worn,
    /// a pair of gauntlets while a worn piece covers the hands. A caster is never left (the original is the spare).
    /// </summary>
    /// <param name="item">The corpse entry.</param>
    /// <param name="replaced">The kinds the replace step bought and wore.</param>
    /// <param name="state">The read model (what is worn now).</param>
    public static bool LeftOnCorpse(WorldObject item, IReadOnlyCollection<GearFloorKind> replaced, GameState state)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(replaced);
        ArgumentNullException.ThrowIfNull(state);
        if (replaced.Count == 0 || (item.ItemType & (GearKit.ItemTypeClothing | GearKit.ItemTypeArmor)) == 0)
        {
            return false;
        }

        var locations = item.Weenie.ValidLocations ?? 0;
        var coverage = item.Weenie.Priority ?? 0;
        var worn = SelfEquipJudge.Worn(state);
        if (replaced.Contains(GearFloorKind.Robe) && ((locations & GearFloor.ChestArmor) != 0 || (coverage & GearFloor.OuterwearChest) != 0) && GearFloor.ChestPiece(worn) is not null)
        {
            return true;
        }

        return replaced.Contains(GearFloorKind.Gauntlets) && (locations & GearFloor.HandWear) != 0 && (locations & GearFloor.ChestArmor) == 0 && GearFloor.HandsCoveredBy(worn);
    }

    /// <summary>The kinds as one word for a verdict value: <c>caster+robe+gauntlets</c> in that order.</summary>
    /// <param name="kinds">The kinds.</param>
    public static string Join(IEnumerable<GearFloorKind> kinds) => string.Join('+', Kinds.Where(k => kinds.Contains(k)).Select(Name));

    /// <summary>The count and names for a log line.</summary>
    /// <param name="kinds">The kinds.</param>
    public static string Describe(IReadOnlyCollection<GearFloorKind> kinds) => string.Create(CultureInfo.InvariantCulture, $"{kinds.Count} ({(kinds.Count == 0 ? "none" : Join(kinds))})");
}

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 AF (the owner's direction: the robe leaves the hands as the only part of the body uncovered): the body's coverage as a mask, and the one question the burden gate and the gear floor both ask: does this piece close a part of the body
/// the worn set leaves bare? The masks are ACE's <c>CoverageMask</c> (the pieces' <c>ClothingPriority</c>, the pinned world SQL PropertyInt 4): the outerwear upper and lower legs 0x100 and 0x200, chest 0x400, abdomen 0x800, upper and lower arms
/// 0x1000 and 0x2000, head 0x4000, hands 0x8000, feet 0x10000. ACE judges a hit's armour by the pieces whose priority overlaps the struck part (<c>Monster_Melee.GetArmorLayers</c> with <c>BodyParts.GetCoverageMask</c>, @95ef426d
/// <c>WorldObjects/Monster_Melee.cs:409-419</c>, <c>Entity/BodyPart.cs:115-140</c>) and refuses a wield whose priority overlaps a worn piece's (<c>Creature_Equipment.cs:50-58, 93-101</c>). No constant is a rung's or a ground's (M6 D26).
/// </summary>
public static class GearCoverage
{
    /// <summary>The outerwear body: legs, chest, abdomen, arms, head, hands and feet (bits 0x100 to 0x10000 of <c>CoverageMask</c>).</summary>
    public const uint Body = 0x1FF00;

    /// <summary>The hands' coverage bit (<c>CoverageMask.Hands</c>).</summary>
    public const uint Hands = 0x8000;

    /// <summary>
    /// M6 item 14: the outerwear parts a piece worn at <paramref name="location"/> covers, for a worn piece whose ClothingPriority is not read (the header carried none): ACE's <c>EquipMask</c> armour and wear bits to the <c>CoverageMask</c>
    /// part of the same name (HeadWear 0x1 the head, HandWear 0x20 the hands, FootWear 0x100 the feet, ChestArmor 0x200 to LowerLegArmor 0x4000 the chest, abdomen, arms and legs). 0 for a location that is no part of the body (a held or
    /// jewellery slot, underwear).
    /// </summary>
    /// <param name="location">The EquipMask the piece is worn at.</param>
    public static uint OfLocation(uint location) =>
        ((location & 0x1) != 0 ? 0x4000u : 0) | ((location & 0x20) != 0 ? Hands : 0) | ((location & 0x100) != 0 ? 0x10000u : 0) | ((location & 0x200) != 0 ? 0x400u : 0) | ((location & 0x400) != 0 ? 0x800u : 0)
        | ((location & 0x800) != 0 ? 0x1000u : 0) | ((location & 0x1000) != 0 ? 0x2000u : 0) | ((location & 0x2000) != 0 ? 0x100u : 0) | ((location & 0x4000) != 0 ? 0x200u : 0);

    /// <summary>The coverage of a worn piece: its ClothingPriority, or, with none read, the parts of its location (<see cref="OfLocation"/>).</summary>
    /// <param name="coverage">The piece's ClothingPriority, 0 when not read.</param>
    /// <param name="location">The EquipMask it is worn at.</param>
    public static uint OfWorn(uint coverage, uint location) => coverage != 0 ? coverage : OfLocation(location);

    /// <summary>The parts of the body that no piece of <paramref name="coverages"/> covers.</summary>
    /// <param name="coverages">The worn (or planned) pieces' ClothingPriority values.</param>
    public static uint Gaps(IEnumerable<uint> coverages)
    {
        ArgumentNullException.ThrowIfNull(coverages);
        var covered = 0u;
        foreach (var c in coverages)
        {
            covered |= c;
        }

        return Body & ~covered;
    }

    /// <summary>
    /// True when a piece of <paramref name="coverage"/> and <paramref name="armourLevel"/> closes a bare part of the body of the set <paramref name="planned"/> and can be worn with it: it covers a gap, overlaps no piece of the set (ACE would
    /// refuse the wield), and has armour (a level above 0: the value is the reason to carry it). <paramref name="why"/> says which, with the masks.
    /// </summary>
    /// <param name="planned">The coverage of the pieces that will be worn.</param>
    /// <param name="coverage">The piece's ClothingPriority (0: not a covering piece).</param>
    /// <param name="armourLevel">The piece's ArmorLevel (PropertyInt 28), or null when not read.</param>
    /// <param name="why">The verdict with the numbers.</param>
    public static bool ClosesGap(IReadOnlyCollection<uint> planned, uint coverage, int? armourLevel, out string why)
    {
        ArgumentNullException.ThrowIfNull(planned);
        var covered = planned.Aggregate(0u, (a, c) => a | c);
        var gap = Body & ~covered;
        if (coverage == 0)
        {
            why = "it covers no part of the body (no ClothingPriority read)";
            return false;
        }

        if ((coverage & covered) != 0)
        {
            why = $"its coverage 0x{coverage:X} overlaps the worn set's 0x{covered:X} (0x{coverage & covered:X}): ACE refuses it with that on";
            return false;
        }

        if ((coverage & gap) == 0)
        {
            why = $"its coverage 0x{coverage:X} closes none of the bare parts 0x{gap:X}";
            return false;
        }

        if (armourLevel is not > 0)
        {
            why = armourLevel is null ? "its armour level is not read, so its value is not known (the gate does not guess)" : "it has no armour level";
            return false;
        }

        why = $"its coverage 0x{coverage:X} closes the bare parts 0x{coverage & gap:X} of 0x{gap:X}, armour level {armourLevel}";
        return true;
    }
}

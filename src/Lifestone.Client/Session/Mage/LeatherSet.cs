using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// One piece of a quest file's <c>leather</c> block (M6 item 14, the owner's order of 2026-11-12: leather in place of the plate). The numbers are the world database's rows for the weenie (read-only SELECT, cited in the block's
/// <c>sources</c>); the price is the vendor's (ACE's <c>GetSellCost</c>).
/// </summary>
/// <param name="Wcid">The ware the vendor sells for this part of the body.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Coverage">Its ClothingPriority (ACE's <c>CoverageMask</c>): the part of the body it covers.</param>
/// <param name="Locations">Its ValidLocations (the EquipMask it is worn at), for a piece whose header carries none.</param>
/// <param name="ArmourLevel">Its ArmorLevel.</param>
/// <param name="Burden">Its burden.</param>
/// <param name="Price">Its price at the vendor, in pyreals.</param>
/// <param name="Also">Other weenie classes that fill this part when she already holds one (the Leather Gauntlets the gear floor bought, the Leather Boots a new character wears): held, the ware is not bought.</param>
public sealed record LeatherPiece(uint Wcid, string Name, uint Coverage, uint Locations, int ArmourLevel, int Burden, int Price, IReadOnlyList<uint> Also)
{
    /// <summary>True when an item of <paramref name="wcid"/> fills this part: the ware itself or one of <see cref="Also"/>.</summary>
    /// <param name="wcid">The held item's weenie class.</param>
    public bool FilledBy(uint wcid) => wcid == Wcid || Also.Contains(wcid);
}

/// <summary>
/// The <c>leather</c> block of a quest file (M6 item 14): the armour set bought at a vendor of the quest giver's town and worn in place of the pieces named by <see cref="InsteadOf"/> (the Pathwarden Robe), as one swap. Data read
/// here, as <see cref="GearReturnTable"/> reads <c>returns</c>; the quest loader skips the block.
/// </summary>
/// <param name="Quest">The quest file's key.</param>
/// <param name="Vendor">The vendor who sells every piece (his <see cref="GroundVendor.Sells"/> lists them at the block's prices); a ground's profile need not list him.</param>
/// <param name="Pieces">The pieces, one a part of the body.</param>
/// <param name="InsteadOf">The worn pieces the set replaces: while she wears one, the swap is hers.</param>
public sealed record LeatherSet(string Quest, GroundVendor Vendor, IReadOnlyList<LeatherPiece> Pieces, IReadOnlyList<uint> InsteadOf)
{
    private static readonly Lazy<LeatherSet?> Loaded = new(() => Of("pathwarden"));

    /// <summary>The one set there is today: the Pathwarden quest's (Holtburg's blacksmith). A set by town is <c>TODO(m6-leather-towns)</c>.</summary>
    public static LeatherSet? Default => Loaded.Value;

    /// <summary>The parts of the body the set covers.</summary>
    public uint Coverage => Pieces.Aggregate(0u, (a, p) => a | p.Coverage);

    /// <summary>The piece an item of <paramref name="wcid"/> fills, or null.</summary>
    /// <param name="wcid">The item's weenie class.</param>
    public LeatherPiece? PieceFilledBy(uint wcid) => Pieces.FirstOrDefault(p => p.FilledBy(wcid));

    /// <summary>The set of the quest <paramref name="key"/>, or null when its file has no <c>leather</c> block.</summary>
    /// <param name="key">The quest's key.</param>
    public static LeatherSet? Of(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return QuestCatalog.ResourceText(key + ".json") is { } text ? Parse(text) : null;
    }

    /// <summary>Reads the <c>leather</c> block of a quest file's text, or null when the file has none; a malformed block throws, naming the field.</summary>
    /// <param name="questJson">The quest file's text.</param>
    public static LeatherSet? Parse(string questJson)
    {
        ArgumentNullException.ThrowIfNull(questJson);
        using var doc = JsonDocument.Parse(questJson);
        if (!doc.RootElement.TryGetProperty("leather", out var block))
        {
            return null;
        }

        var key = doc.RootElement.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : throw Fail("$.key", "is not a text");
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("pieces", out var piecesElement) || piecesElement.ValueKind != JsonValueKind.Array || piecesElement.GetArrayLength() == 0)
        {
            throw Fail("$.leather.pieces", "is not a non-empty array");
        }

        var pieces = new List<LeatherPiece>();
        var covered = 0u;
        var i = 0;
        foreach (var p in piecesElement.EnumerateArray())
        {
            var path = $"$.leather.pieces[{i++}]";
            var also = new List<uint>();
            if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("also", out var alsoElement))
            {
                if (alsoElement.ValueKind != JsonValueKind.Array)
                {
                    throw Fail(path + ".also", "is not a list of wcids");
                }

                foreach (var w in alsoElement.EnumerateArray())
                {
                    also.Add(w.ValueKind == JsonValueKind.Number && w.TryGetUInt32(out var wcid) && wcid != 0 ? wcid : throw Fail(path + ".also", "holds an entry that is not a wcid"));
                }
            }

            var piece = new LeatherPiece((uint)Whole(p, "wcid", path, 1), Text(p, "name", path), (uint)Whole(p, "coverage", path, 1), (uint)Whole(p, "locations", path, 1), Whole(p, "armour_level", path, 1), Whole(p, "burden", path, 0), Whole(p, "price", path, 1), also);
            if ((piece.Coverage & ~GearCoverage.Body) != 0)
            {
                throw Fail(path + ".coverage", $"0x{piece.Coverage:X} holds a bit outside the outerwear body 0x{GearCoverage.Body:X}");
            }

            if ((piece.Coverage & covered) != 0)
            {
                throw Fail(path + ".coverage", $"0x{piece.Coverage:X} overlaps an earlier piece's (0x{piece.Coverage & covered:X}): ACE would refuse the second (Creature_Equipment.cs:50-58, 93-101 @95ef426d)");
            }

            if (pieces.Any(q => q.FilledBy(piece.Wcid) || piece.Also.Any(q.FilledBy)))
            {
                throw Fail(path + ".wcid", "names a weenie class an earlier piece names");
            }

            covered |= piece.Coverage;
            pieces.Add(piece);
        }

        if (covered != GearCoverage.Body)
        {
            throw Fail("$.leather.pieces", $"cover 0x{covered:X}, not the whole body 0x{GearCoverage.Body:X}: the set is worn in the robe's place, so a part it leaves bare is a part bared by the swap");
        }

        var insteadOf = new List<uint>();
        if (!block.TryGetProperty("instead_of", out var instead) || instead.ValueKind != JsonValueKind.Array || instead.GetArrayLength() == 0)
        {
            throw Fail("$.leather.instead_of", "is not a non-empty list of wcids");
        }

        foreach (var w in instead.EnumerateArray())
        {
            insteadOf.Add(w.ValueKind == JsonValueKind.Number && w.TryGetUInt32(out var wcid) && wcid != 0 ? wcid : throw Fail("$.leather.instead_of", "holds an entry that is not a wcid"));
        }

        if (!block.TryGetProperty("vendor", out var v) || v.ValueKind != JsonValueKind.Object)
        {
            throw Fail("$.leather.vendor", "is not an object");
        }

        const string vendorPath = "$.leather.vendor";
        if (!v.TryGetProperty("point", out var point) || point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 3 || point.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.Number))
        {
            throw Fail(vendorPath + ".point", "is not three numbers");
        }

        var at = new Vector3(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle());
        var cell = Hex(v, "cell", vendorPath);
        var vendor = new GroundVendor(
            Text(v, "name", vendorPath),
            (uint)Whole(v, "wcid", vendorPath, 1),
            Hex(v, "guid", vendorPath),
            cell,
            at,
            cell, // the walk ends in his own cell, at his own point (as the healer's and the tailor's rows): the station stops short of his body and opens him from there
            null,
            Hex(v, "buys_item_types", vendorPath),
            [.. pieces.Select(p => new VendorWare(p.Wcid, p.Name, p.Price))])
        {
            BuyPrice = v.TryGetProperty("buy_price", out var rate) && rate.ValueKind == JsonValueKind.Number && rate.GetDouble() is > 0 and <= 1 ? rate.GetDouble() : throw Fail(vendorPath + ".buy_price", "is not a share of the Value above 0 and at most 1"),
        };
        return new LeatherSet(key, vendor, pieces, insteadOf);
    }

    private static int Whole(JsonElement e, string name, string path, int min) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= min ? n : throw Fail($"{path}.{name}", $"is not a whole number of {min} or more");

    private static string Text(JsonElement e, string name, string path) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : throw Fail($"{path}.{name}", "is not a text");

    private static uint Hex(JsonElement e, string name, string path) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) && n != 0
            ? n
            : throw Fail($"{path}.{name}", "is not a hex number written 0x...");

    private static FormatException Fail(string path, string what) => new($"TODO(m6-leather-file): {path} {what}");
}

/// <summary>What <see cref="LeatherSwap.Decide"/> says of a supply trip.</summary>
public enum LeatherSwapKind
{
    /// <summary>She wears none of the pieces the set replaces: the swap is not this character's (or it is done).</summary>
    NotHers,

    /// <summary>Every piece of the set is held: it goes on in the robe's place on this trip, nothing to buy.</summary>
    Wear,

    /// <summary>The purse pays for every missing piece over the trip's other buys: they are bought on this trip, and the set goes on once all are held.</summary>
    Buy,

    /// <summary>The purse does not pay for all of the missing pieces yet: none is bought, the robe stays on.</summary>
    HeldBack,
}

/// <summary>The rule's answer, with the numbers.</summary>
/// <param name="Kind">What the trip does.</param>
/// <param name="Missing">The pieces she does not hold.</param>
/// <param name="Need">What the missing pieces cost.</param>
/// <param name="Budget">What the trip may spend on them.</param>
/// <param name="Token">The answer as a token for the log (<c>not-hers</c>, <c>wear</c>, <c>due</c>, <c>purse</c>).</param>
/// <param name="Why">The answer in words with every number.</param>
public sealed record LeatherSwapPlan(LeatherSwapKind Kind, IReadOnlyList<LeatherPiece> Missing, long Need, long Budget, string Token, string Why);

/// <summary>
/// M6 item 14 (the owner's order of 2026-11-12: leather in place of the plate): when the leather set is bought and worn in the robe's place. Pure.
/// </summary>
/// <remarks>
/// <para><b>Staged, never part-bare.</b> The chest gives the Pathwarden Robe as before (free, armour level 50 with its own Impenetrability II, every part but the hands); the gear floor buys gauntlets on the first trip. The robe covers
/// the head, the legs and the feet too (ClothingPriority 0x17F00), so no piece of the set can be worn beside it (ACE Creature_Equipment.cs:50-58, 93-101 @95ef426d): the set goes on as one swap, the robe off and every piece on, and only
/// once every piece is held. A piece she already holds fills its part (<see cref="LeatherPiece.Also"/>): the gauntlets the floor bought, the boots a new character wears.</para>
/// <para><b>Never coin-starved.</b> The missing pieces are bought, all of them on one trip, only when the purse pays for them over the trip's other needs: the trip's share (the ground's <c>purse.trip_spend_percent</c>, the rule the
/// scrolls keep, M6 7 V and X) of what the purse holds after the stock, the floor's lines and the scrolls this trip buys, and after the scrolls this trip put off (her Impenetrability and Bludgeon Bane come first: the leather is worth
/// no more than the robe until she casts them). Held back, nothing of the set is bought; the caller says so once, and once when it can pay.</para>
/// </remarks>
public static class LeatherSwap
{
    /// <summary>True while the swap is this character's: she wears a piece the set replaces (the Pathwarden Robe).</summary>
    /// <param name="set">The set.</param>
    /// <param name="wornWcids">The weenie classes she wears.</param>
    public static bool Applies(LeatherSet set, IEnumerable<uint> wornWcids)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(wornWcids);
        return wornWcids.Any(set.InsteadOf.Contains);
    }

    /// <summary>
    /// The pieces of <paramref name="set"/> she holds nothing for, in the set's order. A part another worn piece covers whole is not missing either (<paramref name="coveredByOthers"/>): live 2026-11-12 02:10 UTC the gear
    /// pass put a looted pair of Long Leather Gauntlets (armour level 180) on in place of the Leather Gauntlets, and ACE refused the set's pair over them ("You must remove your Long Leather Gauntlets to wield Leather
    /// Gauntlets"): the hands were covered, by something better than the set's piece.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="heldWcids">The weenie classes she wears or carries.</param>
    /// <param name="coveredByOthers">The parts of the body covered by worn pieces that are neither of the set nor replaced by it (<see cref="CoveredByOthers"/>), 0 for none.</param>
    public static List<LeatherPiece> Missing(LeatherSet set, IReadOnlyCollection<uint> heldWcids, uint coveredByOthers = 0)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(heldWcids);
        return [.. set.Pieces.Where(p => !heldWcids.Any(p.FilledBy) && !Covers(coveredByOthers, p))];
    }

    /// <summary>True when <paramref name="covered"/> holds every part of <paramref name="piece"/>: the part needs no piece of the set.</summary>
    /// <param name="covered">The parts covered by other worn pieces.</param>
    /// <param name="piece">The set's piece.</param>
    public static bool Covers(uint covered, LeatherPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return (covered & piece.Coverage) == piece.Coverage;
    }

    /// <summary>
    /// The parts of the body covered by worn pieces that are neither of the set (no piece's weenie class or its <see cref="LeatherPiece.Also"/>) nor replaced by it (<see cref="LeatherSet.InsteadOf"/>, the robe): a looted
    /// piece the gear pass wore. A piece whose ClothingPriority is not read counts by its location (<see cref="GearCoverage.OfWorn"/>).
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="worn">What she wears: each piece's weenie class, ClothingPriority and location.</param>
    public static uint CoveredByOthers(LeatherSet set, IEnumerable<(uint Wcid, uint Coverage, uint Location)> worn)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(worn);
        var covered = 0u;
        foreach (var (wcid, coverage, location) in worn)
        {
            if (!set.InsteadOf.Contains(wcid) && set.PieceFilledBy(wcid) is null)
            {
                covered |= GearCoverage.OfWorn(coverage, location) & GearCoverage.Body;
            }
        }

        return covered;
    }

    /// <summary>The trip's answer (see the class remarks).</summary>
    /// <param name="set">The set.</param>
    /// <param name="wornWcids">The weenie classes she wears.</param>
    /// <param name="heldWcids">The weenie classes she wears or carries.</param>
    /// <param name="purse">The pyreals she can pay (the coin and the face of the notes), after the trip's sale.</param>
    /// <param name="planned">What this trip's other buys cost (the stock, the floor's lines, the scrolls and their components).</param>
    /// <param name="waiting">What the scrolls this trip put off cost: they are bought before the set.</param>
    /// <param name="tripSpendPercent">The ground's <c>purse.trip_spend_percent</c>.</param>
    /// <param name="coveredByOthers">The parts covered by worn pieces that are neither of the set nor replaced by it (<see cref="CoveredByOthers"/>): such a part needs no piece of the set.</param>
    public static LeatherSwapPlan Decide(LeatherSet set, IReadOnlyCollection<uint> wornWcids, IReadOnlyCollection<uint> heldWcids, long purse, long planned, long waiting, int tripSpendPercent, uint coveredByOthers = 0)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tripSpendPercent);
        if (!Applies(set, wornWcids))
        {
            return new LeatherSwapPlan(LeatherSwapKind.NotHers, [], 0, 0, "not-hers", "leather: none of the pieces the set replaces is worn: the swap is not this character's, or it is done");
        }

        var missing = Missing(set, heldWcids, coveredByOthers);
        if (missing.Count == 0)
        {
            return new LeatherSwapPlan(LeatherSwapKind.Wear, missing, 0, 0, "wear", string.Create(CultureInfo.InvariantCulture, $"leather: every one of the {set.Pieces.Count} parts is held or covered{(coveredByOthers == 0 ? string.Empty : $" (another worn piece covers 0x{coveredByOthers:X})")}: nothing to buy; the set goes on in the robe's place on this trip"));
        }

        var need = missing.Sum(p => (long)p.Price);
        var left = Math.Max(0, purse - planned - waiting);
        var budget = left * tripSpendPercent / 100;
        var names = string.Join(", ", missing.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} {p.Price}")));
        var sums = string.Create(CultureInfo.InvariantCulture, $"the purse {purse} less this trip's other buys {planned}{(waiting > 0 ? $" and the scrolls it put off {waiting}" : string.Empty)} leaves {left}, and the trip's share of it ({tripSpendPercent}%) is {budget}");
        if (need <= budget)
        {
            return new LeatherSwapPlan(LeatherSwapKind.Buy, missing, need, budget, "due", string.Create(CultureInfo.InvariantCulture,
                $"leather: due: the {missing.Count} missing piece(s) of {set.Pieces.Count} ({names}) cost {need} at {set.Vendor.Name}; {sums}: bought on this trip, all of them, and the set goes on in the robe's place as one swap"));
        }

        var dueAt = planned + waiting + ((need * 100 + tripSpendPercent - 1) / tripSpendPercent);
        return new LeatherSwapPlan(LeatherSwapKind.HeldBack, missing, need, budget, "purse", string.Create(CultureInfo.InvariantCulture,
            $"leather: held back: the {missing.Count} missing piece(s) of {set.Pieces.Count} ({names}) cost {need} at {set.Vendor.Name}; {sums}, short by {need - budget}: none is bought and the robe stays on (a part of the set is never worn alone); it is due when a trip's purse reaches {dueAt} with these buys"));
    }
}

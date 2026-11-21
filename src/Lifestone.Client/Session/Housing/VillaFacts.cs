using System.Numerics;

namespace Lifestone.Client.Session.Housing;

/// <summary>A placed world object a villa step walks to: its landblock_instance guid, its weenie, its name, its stored cell and its landblock-local point.</summary>
/// <param name="Guid">The landblock_instance guid.</param>
/// <param name="Wcid">The weenie class.</param>
/// <param name="Name">The name the server sends.</param>
/// <param name="Cell">The stored cell.</param>
/// <param name="Point">The landblock-local point.</param>
public readonly record struct PlacedThing(uint Guid, uint Wcid, string Name, uint Cell, Vector3 Point);

/// <summary>A point a step arrives at (a portal's destination row).</summary>
/// <param name="Cell">The cell.</param>
/// <param name="Point">The landblock-local point.</param>
public readonly record struct HousePoint(uint Cell, Vector3 Point);

/// <summary>
/// One dwelling of a block (M6 villa): the house object, its guardian (the slumlord, where a player buys and pays and where <c>/house recall</c> lands: ACE <c>HandleActionTeleToHouse</c> teleports to
/// <c>house.SlumLord.Location</c>), the house portal inside it, the basement's arrival (the <c>house_portal</c> row of the house) and the basement's storage chests.
/// </summary>
/// <param name="HouseId">The house id (the <c>house_portal</c> rows' <c>house_Id</c>).</param>
/// <param name="House">The house object in the outdoor landblock.</param>
/// <param name="Guardian">The guardian (slumlord).</param>
/// <param name="HousePortal">The house portal inside the dwelling (into the basement).</param>
/// <param name="BasementArrival">Where the house portal lands (the house's <c>house_portal</c> row in the basement's landblock).</param>
/// <param name="Chests">The basement's storage chests (wcid 9687), the basement house object's linked children.</param>
public sealed record VillaHouse(uint HouseId, PlacedThing House, PlacedThing Guardian, PlacedThing HousePortal, HousePoint BasementArrival, IReadOnlyList<PlacedThing> Chests);

/// <summary>A dwelling's price: the guardian's buy rows (create-list destination 16).</summary>
/// <param name="Pyreals">The money line (pyreals; trade notes pay it too).</param>
/// <param name="Writs">The Writs of Refuge.</param>
/// <param name="Lockpicks">The Crude Lockpicks.</param>
public readonly record struct HousePrice(long Pyreals, int Writs, int Lockpicks);

/// <summary>A block of dwellings that share one guardian weenie (and so one kind, price and level), in the order a purchase tries them (the nearest to Holtburg first).</summary>
/// <param name="Key">The block's key (the guardian weenie's class name).</param>
/// <param name="GuardianWcid">The guardian weenie.</param>
/// <param name="Type">The dwellings' kind (an apartment is exempt from the 15-day rule).</param>
/// <param name="Price">The purchase price.</param>
/// <param name="MinLevel">The guardian's MinLevel (PropertyInt 86).</param>
/// <param name="Houses">The dwellings this file knows, in order.</param>
public sealed record VillaBlock(string Key, uint GuardianWcid, Lifestone.Net.Messages.HouseType Type, HousePrice Price, int MinLevel, IReadOnlyList<VillaHouse> Houses);

/// <summary>
/// The villa's facts (M6 villa; cache/m6/villa/villa-facts.md and writ-caves.md, read-only SELECTs on ace_world and ACE's source @95ef426d), each checked against the pinned world SQL by <c>VillaFactsSqlTests</c>.
/// Facts only: no fight or kit number lives here (the owner's general rules, 2026-11-13).
/// </summary>
public static class VillaFacts
{
    /// <summary>Pyreal (the money line of a price; ACE's <c>HousePayment</c> takes trade notes for it too).</summary>
    public const uint PyrealWcid = 273;

    /// <summary>Writ of Refuge (stack 100, bonded).</summary>
    public const uint WritWcid = 11710;

    /// <summary>Crude Lockpick.</summary>
    public const uint LockpickWcid = 511;

    /// <summary>The villa's storage chest ("Storage").</summary>
    public const uint StorageWcid = 9687;

    /// <summary>The villa block's guardian, slumlordvilla1401-1440: its create list's buy rows (destination 16) are 2,000,000 pyreals, 5 writs and 1 Crude Lockpick, its rent rows (32) 100,000 pyreals and 2 writs; MinLevel 35.</summary>
    public const uint VillaGuardianWcid = 13080;

    /// <summary>The purchase price in pyreals (create-list row of 13080, destination 16, wcid 273).</summary>
    public const long PricePyreals = 2_000_000;

    /// <summary>The writs the purchase takes (destination 16, wcid 11710).</summary>
    public const int PriceWrits = 5;

    /// <summary>The lockpicks the purchase takes (destination 16, wcid 511).</summary>
    public const int PriceLockpicks = 1;

    /// <summary>The rent in pyreals (destination 32, wcid 273).</summary>
    public const long RentPyreals = 100_000;

    /// <summary>The writs the rent takes (destination 32, wcid 11710).</summary>
    public const int RentWrits = 2;

    /// <summary>The villa's MinLevel (PropertyInt 86 of 13080).</summary>
    public const int MinLevel = 35;

    /// <summary>
    /// The first instant the climber's account may buy anything but an apartment: at once. The server mod sets ACE's <c>house_15day_account</c> false at every start (the owner, 2026-11-16: the account age
    /// makes no sense here; server/mod/README.md, "A house needs no account age"), so no account waits. With <c>LIFESTONE_ACE_HOUSE_ACCOUNT_AGE=ace</c> on the server the 15 days return: pass
    /// <see cref="ClimberAccountOpensVillasRetail"/> instead (ACE's refusal line is also read by the buy, <c>HousePaymentTracker.AccountAgeLinePrefix</c>).
    /// </summary>
    public static readonly DateTimeOffset ClimberAccountOpensVillas = DateTimeOffset.MinValue;

    /// <summary>
    /// The climber's opening under retail's rule (the server's <c>LIFESTONE_ACE_HOUSE_ACCOUNT_AGE=ace</c>): ACE's <c>house_15day_account</c> (Player_House.cs:94) refuses an account younger than 15 days, and the
    /// climber's account was created 2026-11-09 22:51:38 UTC (ace_auth, read 2026-11-15), so 2026-11-24 22:51:38 UTC. Another account's date is the caller's to pass.
    /// </summary>
    public static readonly DateTimeOffset ClimberAccountOpensVillasRetail = new(2026, 11, 24, 22, 51, 38, TimeSpan.Zero);

    /// <summary>A storage chest's item slots (PropertyInt 6 ItemsCapacity of 9687).</summary>
    public const int ChestItems = 26;

    /// <summary>A storage chest's pack slots (PropertyInt 7 ContainersCapacity of 9687).</summary>
    public const int ChestPacks = 1;

    /// <summary>
    /// The block slumlordvilla1401-1440 (40 villas), the two houses of its nearest landblock 0x9DAF (about 2.5 km WSW of Holtburg): housevilla1425 (house id 1425) and housevilla1426 (1426), each with its basement
    /// (0x038B and 0x038C) and two storage chests. The other 38 are not listed yet (<c>TODO(villa-more-houses)</c>: a purchase that finds both owned stops by that name).
    /// </summary>
    public static VillaBlock Block1401 { get; } = new(
        "slumlordvilla1401-1440",
        VillaGuardianWcid,
        Lifestone.Net.Messages.HouseType.Villa,
        new HousePrice(PricePyreals, PriceWrits, PriceLockpicks),
        MinLevel,
        [
            new VillaHouse(
                1425,
                new PlacedThing(0x79DAF038, 13049, "Villa", 0x9DAF0112, new Vector3(115.626f, 138.524f, 144.005f)),
                new PlacedThing(0x79DAF037, VillaGuardianWcid, "Villa", 0x9DAF0000, new Vector3(97.4746f, 135.875f, 144.005f)),
                new PlacedThing(0x79DAF039, 11730, "House Portal", 0x9DAF0118, new Vector3(105.399f, 134.327f, 139.205f)),
                new HousePoint(0x038B011F, new Vector3(20f, -50f, -5.995f)),
                [
                    new PlacedThing(0x7038B000, StorageWcid, "Storage", 0x038B0100, new Vector3(0.023f, -43.404f, -5.995f)),
                    new PlacedThing(0x7038B001, StorageWcid, "Storage", 0x038B0100, new Vector3(-3.608f, -39.98f, -5.995f)),
                ]),
            new VillaHouse(
                1426,
                new PlacedThing(0x79DAF03B, 13050, "Villa", 0x9DAF0127, new Vector3(133.117f, 32.6741f, 138.005f)),
                new PlacedThing(0x79DAF03A, VillaGuardianWcid, "Villa", 0x9DAF0000, new Vector3(138.04f, 24.5413f, 138.005f)),
                new PlacedThing(0x79DAF03C, 11730, "House Portal", 0x9DAF0137, new Vector3(141.923f, 36.4428f, 133.205f)),
                new HousePoint(0x038C011F, new Vector3(20f, -50f, -5.995f)),
                [
                    new PlacedThing(0x7038C000, StorageWcid, "Storage", 0x038C0100, new Vector3(0.023f, -43.404f, -5.995f)),
                    new PlacedThing(0x7038C001, StorageWcid, "Storage", 0x038C0100, new Vector3(-3.608f, -39.98f, -5.995f)),
                ]),
        ]);

    /// <summary>The house of <paramref name="block"/> whose guardian is <paramref name="guardian"/>, or null.</summary>
    /// <param name="block">The block.</param>
    /// <param name="guardian">The guardian's guid.</param>
    public static VillaHouse? HouseOf(VillaBlock block, uint guardian) => (block ?? throw new ArgumentNullException(nameof(block))).Houses.FirstOrDefault(h => h.Guardian.Guid == guardian);

    /// <summary>The house of any known block whose guardian or house object is at <paramref name="landblock"/>, or null (the owned house, read from its HouseData position).</summary>
    /// <param name="landblock">The landblock (cell id high 16 bits).</param>
    public static VillaHouse? HouseAt(uint landblock) => Block1401.Houses.FirstOrDefault(h => h.Guardian.Cell >> 16 == landblock || h.House.Cell >> 16 == landblock || h.BasementArrival.Cell >> 16 == landblock);
}

/// <summary>The shop the Crude Lockpick is bought at (M6 villa; read-only SELECTs, writ-caves.md section 5).</summary>
public static class LockpickFacts
{
    /// <summary>
    /// The Merchant (weenie 1387, landblock_instance guid 0x7ABB3003, cell 0xABB30102, a cottage about 390 m east-south-east of Holtburg's lifestone): of the ten shop weenies that list the lockpick (create-list destination 4),
    /// the only placement within a walk of a town on the travel file; its row 68527 lists 511 unlimited. Holtburg itself has none.
    /// </summary>
    public static PlacedThing Merchant { get; } = new(0x7ABB3003, 1387, "Merchant", 0xABB30102, new Vector3(81.2478f, 107.194f, 65.205f));

    /// <summary>The Merchant's create-list row for the lockpick.</summary>
    public const long CreateListRow = 68527;

    /// <summary>The lockpick's Value (PropertyInt 19 of 511).</summary>
    public const int LockpickValue = 150;

    /// <summary>The Merchant's SellPrice (PropertyFloat 38 of 1387).</summary>
    public const double SellRate = 1.55;

    /// <summary>The catalog's price of one: ACE's <c>ceil(rate x Value - 0.1)</c> (as the scarab sellers' <c>PriceOf</c>): 233. The wire price wins when the shop says another.</summary>
    public static int CatalogPrice => (int)Math.Ceiling((SellRate * LockpickValue) - 0.1);
}

/// <summary>One minor elemental stone's cave (M6 villa, writ-caves.md): the fetch's quest file, the stone, its pick-up timer row and the travel edge whose drop the run starts from.</summary>
/// <param name="QuestKey">The fetch's quest file key.</param>
/// <param name="StoneWcid">The stone's weenie.</param>
/// <param name="StoneName">The stone's name.</param>
/// <param name="QuestRow">The stone's Quest string (the pick-up timer: one hour, max solves -1).</param>
/// <param name="StoneGuid">The stone's landblock_instance guid (a 25 s item generator's child).</param>
/// <param name="StoneCell">The stone's cell.</param>
/// <param name="StonePoint">The stone's point.</param>
/// <param name="DropEdge">The travel edge (travel-routes.json) whose destination the run to the stone starts from.</param>
/// <param name="RouteTodo">The named TODO of a route no walk establishes yet (<c>TownNetworkWalkDatTests</c>: the hop or the run into the cave), or null when every walk of it is planned and confirmed on the dats.</param>
public sealed record StoneCave(string QuestKey, uint StoneWcid, string StoneName, string QuestRow, uint StoneGuid, uint StoneCell, Vector3 StonePoint, string DropEdge, string? RouteTodo = null);

/// <summary>The writ run's facts (M6 villa; writ-caves.md): the four minor stones, the Agent of the Arcanum's give flow and Holtburg's Agent.</summary>
public static class WritFacts
{
    /// <summary>The Agent of the Arcanum (weenie 12050; 34 placed: landblock_instance, read-only SELECT on the world copy, 2026-11-21).</summary>
    public const uint AgentWcid = 12050;

    /// <summary>The stones one writ takes.</summary>
    public const int StonesPerWrit = 2;

    /// <summary>
    /// The Agent's Tell on the first stone of a pair: the stone's Give emote (category 6) runs UpdateQuest <c>MinorStoneWrit@n</c>; ACE strips the comment after '@' (<c>QuestManager.cs:496</c>), so all four stones share the
    /// quest <c>MinorStoneWrit</c> (max solves 1): without it the update stamps it and the QuestSuccess set (category 12) Tells this line (<c>EmoteManager.cs:1462-1487</c>).
    /// </summary>
    public const string FirstStoneLine = "Hand me another minor crystal like this one and I'll be able to reward you.";

    /// <summary>The Agent's Tell on the second stone: the quest cannot be solved again, so the QuestFailure set (category 13) gives one Writ of Refuge, Tells this line and erases <c>MinorStoneWrit</c>.</summary>
    public const string WritLine = "Here, take this as a reward.";

    /// <summary>Holtburg's Agent of the Arcanum (landblock_instance guid 0x7A9B4038, cell 0xA9B40172, 82.0 m east of the lifestone 0x7A9B404F at (81.330, 11.797), bearing 94 degrees, the same landblock).</summary>
    public static PlacedThing HoltburgAgent { get; } = new(0x7A9B4038, AgentWcid, "Agent of the Arcanum", 0xA9B40172, new Vector3(163.152f, 5.80392f, 94.005f));

    /// <summary>
    /// The four caves, the nearest run first from Holtburg's lifestone (the Shivering Stone's portal stands in Holtburg's place; the Stinging Stone's is the Facility Hub's; Smoldering and Sparking are two hops past the Network).
    /// Two carry a <see cref="StoneCave.RouteTodo"/> (the dat walks, writ-caves.md section 6): <see cref="WritRule.NextFetch"/> passes them by name until the walk is established.
    /// </summary>
    public static IReadOnlyList<StoneCave> Caves { get; } =
    [
        new("minor-shivering-stone", 6321, "Minor Shivering Stone", "shiveringstoneminor", 0x7BDB4001, 0xBDB40102, new Vector3(128.824f, 164.167f, 310.017f), "holtburg-to-wilderness", "TODO(villa-shivering-igloo-walk): the climb from the drop reaches 24 m south of the igloo, but no doorway lane leads into its room 0xBDB40102"),
        new("minor-stinging-stone", 6319, "Minor Stinging Stone", "stingingstoneminor", 0x7BA60011, 0xBA600100, new Vector3(132.208f, 176.637f, -0.328499f), "facility-hub-to-blackmire", "TODO(villa-blackmire-portal-ground): the Facility Hub's Blackmire Temple portal stands where the confirmer finds no walkable ground; TODO(villa-stinging-cave-walk): the run from the drop reaches the cave's mouth, but no doorway lane leads into the stone's room 0xBA600100"),
        new("minor-smoldering-stone", 6320, "Minor Smoldering Stone", "smolderingstoneminor", 0x7A66C000, 0xA66C0100, new Vector3(83.5479f, 141.65f, 16.8029f), "samsur-to-yinar"),
        new("minor-sparking-stone", 6318, "Minor Sparking Stone", "sparkingstoneminor", 0x7DD34002, 0xDD340100, new Vector3(156.236f, 102.922f, 134.816f), "mayoi-to-west-villas"),
    ];

    /// <summary>The four stones' weenies.</summary>
    public static IReadOnlySet<uint> StoneWcids { get; } = Caves.Select(c => c.StoneWcid).ToHashSet();
}

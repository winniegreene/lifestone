using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Housing;

/// <summary>
/// The storage trip (M6 villa): <c>/house recall</c>, the walk into the villa and through its house portal to the basement, a storage chest opened, the kept finds the caller names moved into it, the chest closed, and
/// Lifestone Recall home. Check, act, check:
/// <list type="number">
/// <item>Before: an owned house this file knows (<see cref="VillaFacts.HouseAt"/>; a HouseQuery when the status is unread); each item appraised for Attuned (<see cref="StorageRule.Plan"/> keeps attuned, worn and unheld items
/// out, ACE <c>Player_Inventory.cs:2326</c>); nothing to move is <c>reason=not-due:nothing-to-store</c>, ok.</item>
/// <item>The house recall (one named retry, <see cref="HouseLeg.RecallToHouse"/>); the walk to the house portal (<c>TODO(villa-house-portal-walk)</c>: no plan reaches the portal's room on this base, so this fails
/// <c>storage:house-portal-walk</c> by name until the walk is established) and its Use, checked in the basement's landblock; the walk to the chest and its open (the view names it).</item>
/// <item>Each move: before, the item held and not worn, the chest open with room; the act, <see cref="IActionApi.MoveToPack"/> into the chest; after, the item gone from the packs and listed in the chest's view. A refused move
/// leaves the item in the packs and goes on (<c>recovery=move-refused</c>); a full chest moves on to the house's next chest (<c>recovery=next-chest</c>); both full is named on the line (<c>left=</c>).</item>
/// <item>The chest closed (the view gone), then Lifestone Recall home (one named retry, <see cref="HouseLeg.GoHome"/>) unless <c>back</c> is false.</item>
/// </list>
/// The line: <c>villa_storage: house= asked= moved= refused= left= chests= ok= reason=</c>.
/// </summary>
/// <param name="items">The kept finds to put away (guids the player holds), in order.</param>
/// <param name="back">False to end the trip in the basement.</param>
public sealed class StorageTripStation(IReadOnlyList<uint> items, bool back = true) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "villa-storage";

    /// <summary>The line's prefix.</summary>
    public const string LinePrefix = "villa_storage:";

    /// <summary>
    /// False until the walk from the guardian (where <c>/house recall</c> lands) to the house portal is established on the dats (<c>TODO(villa-house-portal-walk)</c>, <c>TownNetworkWalkDatTests.Villa</c>'s known row
    /// <c>recall-to-house-portal</c>): the villa schedule holds the trip by that name instead of walking a plan that stops at <c>storage:house-portal-walk</c>. Set it true with the fix that removes the known row.
    /// </summary>
    public const bool HousePortalWalkEstablished = false;

    /// <summary>The use range asked of the walk to a chest (as the chests station's, <see cref="ChestsRun.ChestRange"/>).</summary>
    public const double ChestRange = ChestsRun.ChestRange;

    /// <summary>The items.</summary>
    public IReadOnlyList<uint> Items { get; } = items ?? throw new ArgumentNullException(nameof(items));

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    /// <remarks>In peace (the recall puts the player in peace anyway), with the mana for the recall home.</remarks>
    public StationNeeds Needs { get; } = new(Peace: true, MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, Items, back);
    }

    private sealed class Run(JourneyContext context, IReadOnlyList<uint> items, bool back) : HouseLeg(StationName, context)
    {
        private readonly List<string> _refused = [];
        private VillaHouse? _house;
        private int _moved, _chests;
        private List<KeepItem> _left = [];
        private bool _lined;

        protected override string Prefix => "storage";

        protected override string Fields() => string.Create(
            CultureInfo.InvariantCulture,
            $"house={_house?.HouseId ?? 0} asked={items.Count} moved={_moved} refused={_refused.Count} left={_left.Count} chests={_chests} ok={Yes(Failure is null)} reason={Reason}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            try
            {
                Stage = "status";
                if (S.State.HouseStatus == HouseOwnership.Unknown)
                {
                    foreach (var h in QueryHouse()) { yield return h; }

                    if (Failure is not null)
                    {
                        yield break;
                    }
                }

                if (S.State.HouseStatus != HouseOwnership.Owned || S.State.House is not { } owned)
                {
                    Fail("no-house", $"the house status is {S.State.HouseStatus}: no house to store in");
                    yield break;
                }

                if (VillaFacts.HouseAt(owned.Position.CellId >> 16) is not { } house)
                {
                    Fail("house-unknown", string.Create(CultureInfo.InvariantCulture, $"the owned house at 0x{owned.Position.CellId:X8} is in no block this file knows (TODO(villa-more-houses))"));
                    yield break;
                }

                _house = house;
                Stage = "appraise";
                var keep = new List<KeepItem>();
                foreach (var guid in items)
                {
                    if (S.State.Self.Inventory.Contains(guid) && !S.TryGetAppraisal(guid, out _))
                    {
                        var appraise = S.Actions.Identify(guid, ActionSource.Automation);
                        yield return appraise;
                    }

                    keep.Add(ItemOf(guid));
                }

                var (planned, refusedEarly) = StorageRule.Plan(keep, 0, 0, int.MaxValue, int.MaxValue);
                foreach (var (item, token) in refusedEarly)
                {
                    _refused.Add(token);
                    Log($"storage: '{item.Name}' 0x{item.Guid:X8} stays in the packs: {token}");
                }

                Log($"storage: check before: {planned.Count} of {items.Count} items may go into a chest of house {house.HouseId}; {refusedEarly.Count} stay");
                if (planned.Count == 0)
                {
                    _left = [];
                    NotNow("nothing-to-store", $"none of the {items.Count} items may go into a chest");
                    yield break;
                }

                Stage = "recall";
                foreach (var h in RecallToHouse(house.Guardian.Cell >> 16)) { yield return h; }

                if (Failure is not null)
                {
                    yield break;
                }

                Stage = "house-portal";
                var reached = false;
                foreach (var h in Meet(house.HousePortal, TravelPurpose.Seek, DenTravelStation.PortalRangeMetres, DenTravelStation.PortalRangeMetres + 1, ok => reached = ok)) { yield return h; }

                if (!reached)
                {
                    Fail("house-portal-walk", $"the house portal 0x{house.HousePortal.Guid:X8} was not reached: {LastTravelFailure ?? "not seen in reach"} (TODO(villa-house-portal-walk): no plan reaches the portal's room on this base)");
                    yield break;
                }

                var mark = S.ActionLog.Total;
                var use = S.Actions.Use(house.HousePortal.Guid, ActionSource.Automation);
                yield return use;
                var basement = house.BasementArrival.Cell >> 16;
                foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && LoginCompleteConfirmedSince(mark) && InLandblock(basement), TeleportSeconds)) { yield return h; }

                if (!InLandblock(basement))
                {
                    Fail("house-portal", string.Create(CultureInfo.InvariantCulture, $"after the house portal's Use the body stands at {Where()}, not in the basement 0x{basement:X4}: {Describe(use)}"));
                    yield break;
                }

                Log($"storage: check after the house portal: in the basement at {Where()}");
                var rest = planned.ToList();
                foreach (var chest in house.Chests)
                {
                    if (rest.Count == 0 || Failure is not null)
                    {
                        break;
                    }

                    if (_chests > 0)
                    {
                        Log($"storage: recovery=next-chest: {rest.Count} items left; to the next chest 0x{chest.Guid:X8}");
                    }

                    _chests++;
                    foreach (var h in IntoChest(chest, rest)) { yield return h; }
                }

                _left = rest;
                if (Failure is not null)
                {
                    yield break;
                }

                if (rest.Count > 0)
                {
                    Log($"storage: the house's chests are full: {rest.Count} items stay in the packs ({string.Join(", ", rest.Select(r => r.Name))})");
                }

                Reason = rest.Count == 0 ? "stored" : "chests-full";
                if (back)
                {
                    Stage = "back";
                    foreach (var h in GoHome()) { yield return h; }
                }
            }
            finally
            {
                Emit();
            }
        }

        /// <summary>One chest: the walk, the open, the moves its room allows (removing the moved and the refused from <paramref name="rest"/>), the close.</summary>
        private IEnumerable<ActionHandle?> IntoChest(PlacedThing chest, List<KeepItem> rest)
        {
            Stage = "chest";
            var reached = false;
            foreach (var h in Meet(chest, TravelPurpose.Cell, ChestRange, QuestStepLeg.UseReachMetres, ok => reached = ok)) { yield return h; }

            if (!reached)
            {
                Fail("chest-unreached", $"the chest 0x{chest.Guid:X8} was not met in reach: {LastTravelFailure ?? "not seen in reach"}");
                yield break;
            }

            var open = S.Actions.OpenContainer(chest.Guid, ActionSource.Automation);
            yield return open;
            if (open.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || S.State.Containers.Viewed is not { } view || view.Guid != chest.Guid)
            {
                Fail("chest-closed", $"the chest 0x{chest.Guid:X8} did not open: {Describe(open)}");
                yield break;
            }

            var inItems = view.Entries.Count(e => e.ContainerType == 0);
            var inPacks = view.Entries.Count - inItems;
            var (moves, refused) = StorageRule.Plan(rest, inItems, inPacks);
            Log(string.Create(CultureInfo.InvariantCulture, $"storage: the chest 0x{chest.Guid:X8} holds {inItems} of {VillaFacts.ChestItems} items and {inPacks} of {VillaFacts.ChestPacks} packs: {moves.Count} moves, {refused.Count} wait"));
            Stage = "move";
            foreach (var item in moves)
            {
                rest.Remove(item);
                if (!S.State.Self.Inventory.Contains(item.Guid) || S.State.Self.Inventory.TryGetEquipped(item.Guid, out _))
                {
                    _refused.Add("not-held");
                    Log($"storage: recovery=move-refused: '{item.Name}' 0x{item.Guid:X8} is no longer a pack item");
                    continue;
                }

                var move = S.Actions.MoveToPack(item.Guid, chest.Guid, 0, ActionSource.Automation);
                yield return move;
                foreach (var h in Until(() => !S.State.Self.Inventory.Contains(item.Guid) && S.State.Containers.Viewed is { } v && v.Entries.Any(e => e.Guid == item.Guid), 3)) { yield return h; }

                var landed = !S.State.Self.Inventory.Contains(item.Guid) && S.State.Containers.Viewed is { } after && after.Entries.Any(e => e.Guid == item.Guid);
                if (move.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || !landed)
                {
                    _refused.Add("move-refused");
                    Log($"storage: recovery=move-refused: '{item.Name}' 0x{item.Guid:X8} did not land in the chest ({Describe(move)}; held {Yes(S.State.Self.Inventory.Contains(item.Guid))}); it stays in the packs");
                    continue;
                }

                _moved++;
                Log($"storage: check after the move: '{item.Name}' 0x{item.Guid:X8} is in the chest 0x{chest.Guid:X8}");
            }

            foreach (var (item, token) in refused.Where(r => r.Token is not ("chest-full" or "pack-slot-full")))
            {
                rest.Remove(item);
                _refused.Add(token);
            }

            var close = S.Actions.CloseContainer(chest.Guid, ActionSource.Automation);
            yield return close;
            foreach (var h in Until(() => S.State.Containers.Viewed is null, 2)) { yield return h; }

            if (S.State.Containers.Viewed is not null)
            {
                Check(false, $"storage: the chest 0x{chest.Guid:X8} view is still open after the close: {Describe(close)}");
            }
        }

        /// <summary>The rule's view of an item: held, worn, a pack, attuned (from the appraisal; null when unread).</summary>
        private KeepItem ItemOf(uint guid)
        {
            var held = S.State.Self.Inventory.Contains(guid);
            var worn = S.State.Self.Inventory.TryGetEquipped(guid, out _);
            var known = S.State.Objects.TryGet(guid, out var o);
            bool? attuned = S.TryGetAppraisal(guid, out var appraisal) ? GearScore.Attuned(appraisal) : null;
            var isPack = known && (o!.ItemType & ContainerItemType) != 0;
            return new KeepItem(guid, known ? o!.Name : "an unknown item", isPack, attuned, held, worn);
        }

        /// <summary>The ItemType bit of a container (ACE <c>ItemType.Container</c>).</summary>
        private const uint ContainerItemType = 0x00000200;

        private void Emit()
        {
            if (_lined)
            {
                return;
            }

            _lined = true;
            Log(LinePrefix + " " + Fields());
        }
    }
}

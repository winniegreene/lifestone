using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 keep every great cantrip (the owner's ruling, 2026-11-15: asked whether she was looting and saving all major cantrips then a ruling that these must be kept). Live ladder27-29: from 01:46 UTC she saw 14 pieces with a Major cantrip she wants and took 2;
/// the other 12 were left on the corpse over the load line (10:08:39: Studded Leather Leggings, Major Life Magic Aptitude, burden 726, left at 17,664 of 17,850 while the same plan kept a Katar), because the line and the slots refused them
/// against the ordinary sell loot earlier corpses had filled the pack with, and the full-pack trip came only after the piece was lost with its corpse.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> A great-cantrip piece (<see cref="MageLootItem.GreatCantrip"/>) outranks every piece of sell loot. When the load line or the slots would refuse it, the plan (<see cref="MageLootRules.Plan"/>) picks the sell loot she
/// carries with the lowest value per burden first (<see cref="CantripRoom.SellLoot"/>) until the piece fits; this stage drops it on the same corpse visit, before the take.</para>
/// <para><b>Check, act, check (D141).</b> Before each drop: the item is still in her main pack and not kept aside since the plan; act: a player's drop from the pack (<see cref="IActionApi.Drop"/>, the pack rules' junk drop); after: the item
/// is gone from her inventory, and the burden and the free slots are read again, each drop logged with its numbers. A drop answered "You're too busy!" (0x001D: ACE refuses a drop while the player is busy,
/// Player_Inventory.cs:1373-1377 @95ef426d) is sent again after the busy wait (<see cref="Casting.CastRules.DefaultBusyWaitSeconds"/>), at most <see cref="LootBusyRetries"/> times; any other refusal ends the drops for the corpse. After the drops the plan's keeps are fitted again to the
/// burden and the slots read now (<see cref="MageLootPlan.Refit"/>): a piece that still does not fit is left with a loud named line, what fills the pack, and the full-pack trip due at once (<see cref="PackRoomMemory.NoteKeeperLeft"/>).
/// After the take, every great-cantrip piece the plan kept is looked for in her pack.</para>
/// <para><b>The fight goes first.</b> An attacker while room is made: a drop in flight is read to its end first (at most the loot step's <c>ActionSeconds</c>), then the corpse is put off as at any other stage; the room already made stays made, and
/// the retry plans again from what she carries then. The seconds spent on the drops do not count against the corpse's own <c>CorpseSeconds</c>.</para>
/// </remarks>
public sealed partial class FightScenario
{
    private sealed partial class LootStep
    {
        /// <summary>The seconds after the last confirmed drop the check waits for the burden the server reports to settle before it calls the room short.</summary>
        public const double RoomSettleSeconds = 1.5;

        /// <summary>The ACE error "You're too busy!" (WeenieError 0x001D).</summary>
        private const uint BusyCode = 0x001D;

        private readonly Queue<RoomDrop> _roomQueue = new();
        private readonly HashSet<uint> _roomRefused = []; // drops ACE refused this fight: never planned again (no loop)
        private readonly Dictionary<string, int> _census = new(StringComparer.Ordinal);
        private string? _casterLine; // M6 14: the casters' verdicts last logged (a line on each change) // M6 14 (the slots): the main pack by why each item is or is not sell loot, for the swap's log
        private RoomDrop _roomDrop;
        private int _roomBusy;
        private double _roomRetryAt = double.NaN;
        private int _roomBurdenBefore;
        private int _roomSlotsBefore;
        private int _roomBurdenAtStart;
        private double _roomStart;
        private double _roomLastDrop = double.NaN;
        private double _roomSpent;
        private bool _roomFailed;
        private string _roomFailWhy = string.Empty;
        private bool _roomAttackLogged;

        /// <summary>M6 14 keep every great cantrip: the sell loot dropped to make room for great-cantrip pieces over the fight (each confirmed gone).</summary>
        public int RoomDrops { get; private set; }

        /// <summary>M6 14 keep every great cantrip: the Value the room's drops left on the ground over the fight.</summary>
        public long RoomDroppedValue { get; private set; }

        /// <summary>M6 14 keep every great cantrip: the great-cantrip pieces taken into her pack over the fight (checked held after the take).</summary>
        public int GreatTaken { get; private set; }

        /// <summary>M6 14 keep every great cantrip: the great-cantrip pieces left on a corpse over the fight (no room could be made, or the check after the drops or the take found it missing): each called the full-pack trip.</summary>
        public int GreatLeft { get; private set; }

        /// <summary>M6 14 hour load (the owner, 2026-11-16): the finds of sell loot taken by a swap over the fight (room made by dropping sell loot of a lower value per burden, checked after the drops).</summary>
        public int SwapsMade { get; private set; }

        /// <summary>M6 14 hour load: the swaps whose room the check after the drops found not made (the find is left; no trip: it is sell loot).</summary>
        public int SwapsShort { get; private set; }

        /// <summary>The seconds the corpse in hand spent making room (they do not count against <see cref="CorpseSeconds"/>).</summary>
        private double RoomSeconds(double clock) => _stage == Stage.MakingRoom ? clock - _roomStart : _roomSpent;

        /// <summary>
        /// The sell loot the plan may drop for the corpse's great-cantrip pieces and, M6 14 hour load, for the swaps (<see cref="CantripRoom.SellLoot"/>), or null when the corpse holds nothing she does not hold that is not money (nothing is read
        /// then). A drop ACE refused earlier in the fight is left out.
        /// </summary>
        private List<CarriedLoot>? RoomCandidates(GameState state, List<MageLootItem> items)
        {
            if (!items.Exists(i => i.GreatCantrip || (!i.Held && !i.IsMoney)))
            {
                return null;
            }

            _census.Clear();
            var keeps = CantripRoom.GearKeeps(state, owner._ground, owner._spells, Quartermaster.Wardrobe.For(state).KeepCastChanceIn(owner._buffOptions.CastChance)); // M6 14 (the owner, 2026-11-16): only the casters her gear rules need, the stones the kit keeps
            var casterLine = CasterKeep.Line(keeps.Verdicts);
            if (!string.Equals(casterLine, _casterLine, StringComparison.Ordinal))
            {
                _casterLine = casterLine;
                log.Info(LogCategory, "loot: " + casterLine);
            }

            var loot = CantripRoom.SellLoot(state, CantripRoom.FieldBook(state, owner._ground, owner._spells, rules, keeps), _census); // M6 16 E5c2: the rule book's verdict, with the field's own keeps named in one place (CantripRoom.FieldKeep)
            loot.RemoveAll(c => _roomRefused.Contains(c.Guid));
            return loot;
        }

        /// <summary>The plan's room lines: a make-room line per piece (info), a loud line per piece no room could be made for (warn) with what fills the pack.</summary>
        private void LogRoom(GameState state, List<CarriedLoot>? sellLoot)
        {
            foreach (var line in _plan!.RoomLines())
            {
                if (line.StartsWith("loot: NO ROOM for the wanted item", StringComparison.Ordinal))
                {
                    log.Error(LogCategory, line); // M6 14 want room: the quest's item left is an error
                }
                else if (line.StartsWith("loot: NO ROOM", StringComparison.Ordinal))
                {
                    log.Warn(LogCategory, line);
                }
                else
                {
                    log.Info(LogCategory, line);
                }
            }

            if (_plan.SwapShorts.Count > 0 && _census.Count > 0)
            {
                // M6 14 (the slots): why the swap had nothing to drop: every main-pack item by why it is or is not sell loot
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the main pack for the swap: {_census.Values.Sum()} item(s): {CantripRoom.CensusLine(_census)}"));
            }

            if (_plan.Shorts.Count > 0)
            {
                GreatLeft += _plan.Shorts.Count;
                log.Warn(LogCategory, $"loot: the pack holds: {CantripRoom.PackFill(state, sellLoot ?? [])}; the full-pack trip is due (the plan noted {_plan.Shorts.Count} great-cantrip piece(s) left: m6-pack-room)");
            }
        }

        /// <summary>The pick-ups begin: the shared looter over the plan.</summary>
        private bool StartLooting(IGameSessionView session)
        {
            _looter = new CorpseLooter(log, LogCategory, _corpse, _plan);
            _lootChatMark = owner._hookSession?.State.Chat.Total ?? 0;
            _stage = Stage.Looting;
            return true;
        }

        /// <summary>The make-room stage begins: the plan's drops queued in its order (the lowest value per burden first, piece by piece).</summary>
        private bool BeginRoom(IGameSessionView session, double clock)
        {
            _roomQueue.Clear();
            foreach (var drop in _plan!.Drops)
            {
                _roomQueue.Enqueue(drop);
            }

            (_roomStart, _roomLastDrop, _roomRetryAt, _roomFailed, _roomFailWhy, _roomAttackLogged) = (clock, double.NaN, double.NaN, false, string.Empty, false);
            _roomBurdenAtStart = LootContext.Of(session.State).Burden;
            _pending = null;
            _stage = Stage.MakingRoom;
            _stageStart = clock;
            return MakingRoom(session, clock);
        }

        /// <summary>One frame of the make-room stage: a drop in flight read to its end and checked, a busy retry waited out, the next drop sent, or, with none left, the check of the room (<see cref="EndRoom"/>).</summary>
        private bool MakingRoom(IGameSessionView session, double clock)
        {
            if (_pending is not null)
            {
                if (_pending.Outcome is null && clock - _stageStart <= ActionSeconds)
                {
                    return true;
                }

                SettleDrop(session, clock);
                _pending = null;
                if (_roomFailed)
                {
                    _roomQueue.Clear();
                    return EndRoom(session, clock);
                }
            }

            if (!double.IsNaN(_roomRetryAt))
            {
                if (clock < _roomRetryAt)
                {
                    return true;
                }

                _roomRetryAt = double.NaN;
                return SendDrop(session, clock, again: true);
            }

            if (_roomQueue.Count > 0)
            {
                _roomDrop = _roomQueue.Dequeue();
                _roomBusy = 0;
                return SendDrop(session, clock, again: false);
            }

            return EndRoom(session, clock);
        }

        /// <summary>Check before, then act: the drop in hand is sent when the item is still in her main pack and not kept aside since the plan; otherwise it is named and skipped (the check after the drops reads the room).</summary>
        private bool SendDrop(IGameSessionView session, double clock, bool again)
        {
            var state = session.State;
            var loot = _roomDrop.Loot;
            var place = state.Self.Inventory.Find(loot.Guid);
            if (place.Kind != InventoryPlaceKind.MainPack || SaleMemory.Shared.KeptAside.Contains(loot.Guid))
            {
                log.Info(LogCategory, $"loot: room: '{loot.Name}' 0x{loot.Guid:X8} is {(place.Kind == InventoryPlaceKind.MainPack ? "kept aside since the plan" : "no longer in her main pack (" + place.Kind + ")")}: not dropped; the check after the drops reads the room");
                return true;
            }

            _roomBurdenBefore = LootContext.Of(state).Burden;
            _roomSlotsBefore = Quartermaster.PackView.MainPackFree(state);
            if (!again)
            {
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: room: dropping {_roomDrop.Reason}: burden {_roomBurdenBefore} of {LootContext.Of(state).Capacity}, {_roomSlotsBefore} free slot(s)"));
            }

            _pending = session.Actions.Drop(loot.Guid, ActionSource.Automation);
            _stageStart = clock;
            return true;
        }

        /// <summary>
        /// The check after a drop: gone from her inventory (counted, the burden and the free slots read again and logged), a busy answer (sent again after <see cref="BusyWaitSeconds"/>, at most <see cref="BusyRetries"/> times), or anything else
        /// (still held: named, never planned again this fight, and the drops for the corpse end).
        /// </summary>
        private void SettleDrop(IGameSessionView session, double clock)
        {
            var state = session.State;
            var loot = _roomDrop.Loot;
            var outcome = _pending?.Outcome is { } o ? Describe(o) : string.Create(CultureInfo.InvariantCulture, $"unanswered after {ActionSeconds:0} s");
            var load = LootContext.Of(state);
            var free = Quartermaster.PackView.MainPackFree(state);
            if (!state.Self.Inventory.Contains(loot.Guid))
            {
                RoomDrops++;
                RoomDroppedValue += loot.Value;
                KeptGuids.Remove(loot.Guid);
                _roomLastDrop = clock;
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: room: dropped {_roomDrop.Reason}: {outcome}; check after: gone from her pack, burden {_roomBurdenBefore} -> {load.Burden} of {load.Capacity}, free slots {_roomSlotsBefore} -> {free}"));
                return;
            }

            if (_pending?.Outcome is { Code: BusyCode } && _roomBusy < BusyRetries)
            {
                _roomBusy++;
                BusyWaits++;
                _roomRetryAt = clock + BusyWaitSeconds;
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: room: the drop of '{loot.Name}' 0x{loot.Guid:X8} was answered busy (0x001D \"You're too busy!\": ACE refuses a drop while she is busy, Player_Inventory.cs:1373-1377 @95ef426d); refusal {_roomBusy}, sent again in {BusyWaitSeconds:0.#} s (at most {BusyRetries})"));
                return;
            }

            _roomRefused.Add(loot.Guid);
            _roomFailed = true;
            _roomFailWhy = string.Create(CultureInfo.InvariantCulture, $"the drop of '{loot.Name}' 0x{loot.Guid:X8} ended {outcome} and it is still held");
            log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: room: {_roomFailWhy} (check after: burden {load.Burden} of {load.Capacity}, {free} free slot(s)); it is not planned again this fight and no more is dropped for this corpse"));
        }

        /// <summary>
        /// The check of the room after the drops: the plan's keeps fitted again to the burden and the free slots read now (<see cref="MageLootPlan.Refit"/>), after waiting up to <see cref="RoomSettleSeconds"/> past the last drop for the
        /// burden to settle. Each great-cantrip piece is named made (then the pick-ups begin) or not made (left, loud, with what fills the pack, and the full-pack trip due at once).
        /// </summary>
        private bool EndRoom(IGameSessionView session, double clock)
        {
            var state = session.State;
            var load = LootContext.Of(state);
            var free = Quartermaster.PackView.MainPackFree(state);
            var plan = _plan!;
            var pieces = plan.RoomMadeFor.ToList();
            if (!_roomFailed && !double.IsNaN(_roomLastDrop) && clock - _roomLastDrop < RoomSettleSeconds && plan.Refit(load.Burden, load.Capacity, free, apply: false).Exists(pieces.Contains))
            {
                return true; // the burden the server reports may lag the drop's confirmation by a message: read again
            }

            _roomSpent = clock - _roomStart;
            var off = plan.Refit(load.Burden, load.Capacity, free);
            var line = plan.LoadLinePercent;
            foreach (var guid in pieces)
            {
                var piece = plan.Items.First(i => i.Guid == guid);
                if (!piece.GreatCantrip)
                {
                    // M6 14 hour load (the owner, 2026-11-16): a swap's find is sell loot: made or not, no trip is called for it
                    if (off.Contains(guid))
                    {
                        SwapsShort++;
                        log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: swap NOT made for '{piece.Name}' 0x{guid:X8} (value {piece.Value ?? 0}, burden {piece.Burden}, v/b {CantripRoom.Ratio(piece.ValuePerBurden)}): {plan.WithdrawnWhy(guid)}{(_roomFailed ? "; " + _roomFailWhy : string.Empty)}; burden {load.Burden} of {load.Capacity} (load line {(line <= 0 ? "none" : line + "%")}), {free} free slot(s): it is left (m6-hour-load)"));
                    }
                    else
                    {
                        SwapsMade++;
                        log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: swap made for '{piece.Name}' 0x{guid:X8} (value {piece.Value ?? 0}, burden {piece.Burden}, v/b {CantripRoom.Ratio(piece.ValuePerBurden)}): burden {_roomBurdenAtStart} -> {load.Burden} of {load.Capacity} (load line {(line <= 0 ? "none" : line + "%")}), {free} free slot(s), {clock - _roomStart:0.#} s: it is taken now (m6-hour-load)"));
                    }

                    continue;
                }

                if (off.Contains(guid))
                {
                    GreatLeft++;
                    PackRoomMemory.For(state).NoteKeeperLeft();
                    log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: room NOT made for the great cantrip '{piece.Name}' 0x{guid:X8} ({piece.KeepAside}; burden {piece.Burden}): {plan.WithdrawnWhy(guid)}{(_roomFailed ? "; " + _roomFailWhy : string.Empty)}; burden {load.Burden} of {load.Capacity} (load line {(line <= 0 ? "none" : line + "%")}), {free} free slot(s); it is left on the corpse and the full-pack trip is due at once (m6-pack-room); the pack holds: {CantripRoom.PackFill(state, CantripRoom.SellLoot(state, CantripRoom.FieldBook(state, owner._ground, owner._spells, rules)))}"));
                }
                else
                {
                    log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: room made for the great cantrip '{piece.Name}' 0x{guid:X8} ({piece.KeepAside}; value {piece.Value ?? 0}, burden {piece.Burden}): burden {_roomBurdenAtStart} -> {load.Burden} of {load.Capacity} (load line {(line <= 0 ? "none" : line + "%")}), {free} free slot(s), {_roomSpent:0.#} s: it is taken now"));
                }
            }

            foreach (var guid in off.Where(g => !pieces.Contains(g)))
            {
                var item = plan.Items.First(i => i.Guid == guid);
                log.Info(LogCategory, $"loot: '{item.Name}' 0x{guid:X8} is taken off the plan after the drops: {plan.WithdrawnWhy(guid)}");
            }

            if (plan.Kept.Count == 0)
            {
                Looted++;
                Close(session);
                return true;
            }

            return StartLooting(session);
        }

        /// <summary>
        /// The check after the take: each great-cantrip piece the plan kept is looked for in her pack: held (counted, logged with the burden) or not (loud: the full-pack trip is due at once, <see cref="PackRoomMemory.NoteKeeperLeft"/>).
        /// </summary>
        private void CheckGreatTaken(IGameSessionView session, CorpseLooter looter)
        {
            var state = session.State;
            foreach (var guid in _plan!.Kept)
            {
                var piece = _plan.Items.FirstOrDefault(i => i.Guid == guid);
                if (!piece.GreatCantrip || _plan.VerdictOf(guid) != MageLootVerdict.KeepAside)
                {
                    continue;
                }

                var load = LootContext.Of(state);
                if (state.Self.Inventory.Find(guid).Held)
                {
                    GreatTaken++;
                    log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the great cantrip '{piece.Name}' 0x{guid:X8} ({piece.KeepAside}) is in her pack (check after the take): burden {load.Burden} of {load.Capacity}, {Quartermaster.PackView.MainPackFree(state)} free slot(s)"));
                }
                else
                {
                    GreatLeft++;
                    PackRoomMemory.For(state).NoteKeeperLeft();
                    log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the great cantrip '{piece.Name}' 0x{guid:X8} ({piece.KeepAside}) is NOT in her pack after the take (the loop: {looter.Summary}{(looter.StoppedBy.Length == 0 ? string.Empty : ", stopped on " + looter.StoppedBy)}); burden {load.Burden} of {load.Capacity}; the full-pack trip is due at once (m6-pack-room)"));
                }
            }
        }

        /// <summary>
        /// An attacker while room is made: a drop in flight is read to its end first (true: the frame waits, at most <see cref="ActionSeconds"/>), then the drops stop and the corpse is put off (false: the caller's put-off); the room already
        /// made stays made and the retry plans again.
        /// </summary>
        private bool RoomAttacked(IGameSessionView session, double clock, string who)
        {
            if (_pending is { Outcome: null } && clock - _stageStart <= ActionSeconds)
            {
                if (!_roomAttackLogged)
                {
                    _roomAttackLogged = true;
                    log.Info(LogCategory, $"loot: {who} while a drop (0x{_roomDrop.Loot.Guid:X8}) was in flight to make room for a great cantrip: its outcome is read first (up to {ActionSeconds:0} s), then the fight goes first");
                }

                return true;
            }

            if (_pending is not null)
            {
                SettleDrop(session, clock);
                _pending = null;
            }

            _roomSpent = clock - _roomStart;
            log.Warn(LogCategory, $"loot: {who} while room was made for {(_plan?.Swaps.Any() == true ? "a swap or " : string.Empty)}a great cantrip of the corpse 0x{_corpse:X8}: {_plan?.Drops.Count - _roomQueue.Count ?? 0} of {_plan?.Drops.Count ?? 0} drop(s) sent; the rest are not sent, the corpse is put off and its retry plans again from what she carries then");
            _roomQueue.Clear();
            _roomRetryAt = double.NaN;
            return false;
        }
    }
}

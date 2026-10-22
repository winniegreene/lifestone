// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Inventory.cs (HandleActionPutItemInContainer, HandleActionDropItem, HandleActionStackableSplitToContainer / To3D / ToWield,
// HandleActionGiveObjectRequest and their queued pick-up and drop actions) and Source/ACE.Server/Entity/Landblock.cs (the pick-up action, 940-983) (AGPL-3.0)
// [with changes: the order and the spacing are the ones the live capture records (cache/m4/2-1/capture.md C2 and C4); the stand-in keeps the items itself and models no range or weight].
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

// M4 2.1 s3: the item moves the stand-in answers (TODO(m4-standin-item-moves), closed): the pick-up from the ground and from an open container, the drop, the three splits and the give.
internal sealed partial class StandInWorld
{
    /// <summary>The pick-up motion's length from the ground and from the 3D split (capture.md C4: 0.74 s; ACE picks the motion by the item's height).</summary>
    internal const double PickupGroundSeconds = 0.74;

    /// <summary>The pick-up motion's length from a corpse (capture.md C2: 1.35 s).</summary>
    internal const double PickupContainerSeconds = 1.35;

    /// <summary>The drop's motion (capture.md C4: 0.73 s) and the 3D split's (0.74 s, <see cref="PickupGroundSeconds"/>).</summary>
    internal const double DropSeconds = 0.73;

    /// <summary>The turn before a give is accepted (capture.md C4 run 4: 0.70 s).</summary>
    internal const double GiveSeconds = 0.70;

    /// <summary>How long after a refused give the 028B and 00A0 come (capture.md C4: 10 to 430 ms).</summary>
    internal const double RefuseLagSeconds = 0.1;

    /// <summary>How long after a split to a container the burden update follows (capture.md C4: 0.56 s).</summary>
    internal const double BurdenLagSeconds = 0.56;

    /// <summary>How long after the item's update the pick-up sound and the return to the stance come (capture.md C2: 31 ms).</summary>
    internal const double ReturnStanceLagSeconds = 0.031;

    /// <summary>How long after the return stance a ground item's pickup event follows (capture.md C4: 15 ms).</summary>
    internal const double PickupEventLagSeconds = 0.015;

    /// <summary>The error ACE answers a give with when the target has no emote for the item (0x046A; capture.md C4: not 0x03EF).</summary>
    internal const uint GiveRefused = 0x046A;

    /// <summary>The pick-up sound (0x8F), the drop sound (0x90), the ammunition wield sound (0x8C) and the give sound (0x8E) of the capture.</summary>
    private const uint SoundPickUp = 0x8F;
    private const uint SoundDropItem = 0x90;
    private const uint SoundWieldAmmo = 0x8C;
    private const uint SoundGiveItem = 0x8E;

    private readonly Dictionary<uint, (StandInPackEntry Item, AcPosition At)> _ground = [];
    private readonly Dictionary<uint, StandInPackEntry> _stacks = [];
    private readonly Dictionary<uint, HashSet<uint>> _giveAccepts = [];
    private double _busyUntil;

    /// <summary>The items lying on the ground that the stand-in dropped or split off (guid to the item; tests).</summary>
    internal IReadOnlyCollection<uint> GroundItems => _ground.Keys;

    /// <summary>The moves the stand-in refused as busy (tests).</summary>
    internal int BusyRefusals { get; private set; }

    /// <summary>Makes the NPC <paramref name="npc"/> accept a give of the weenies <paramref name="wcids"/> (any other item or NPC draws the 028B refusal): the stand-in's NPCs have no emotes (<c>TODO(m4-standin-give-emotes)</c>).</summary>
    internal void AcceptGive(uint npc, params uint[] wcids)
    {
        if (!_giveAccepts.TryGetValue(npc, out var set))
        {
            set = _giveAccepts[npc] = [];
        }

        set.UnionWith(wcids);
    }

    /// <summary>Puts an item on the ground at <paramref name="at"/> for the player to pick up (the tests' and the checks' ground item); the client is told with its CreateObject.</summary>
    internal void DropOnGround(StandInPackEntry item, AcPosition at)
    {
        _ground[item.Guid] = (item, at);
        Later(sink =>
        {
            AnswerLog.Add("F745");
            sink.OnCreateObject(Context(sink), ItemObject(item, null, at), isUpdate: false);
        });
    }

    private bool Busy => _reducer.Clock.Now < _busyUntil;

    /// <summary>A move with an animation starts a chain: its motion, then as long again for the return to the stance (capture.md C2: the next move sent earlier is refused).</summary>
    private void BeginChain(double motion) => _busyUntil = _reducer.Clock.Now + DelaySeconds + (2 * motion);

    private OutboundTicket Refused(uint item, bool busy)
    {
        if (busy)
        {
            BusyRefusals++;
        }

        Later(sink =>
        {
            if (busy)
            {
                AnswerLog.Add("028A");
                sink.OnWeenieError(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x028A), new WeenieErrorEvent(YoureTooBusy, null));
            }

            Failed(sink, item);
        });
        return Ticket();
    }

    private GameEventHeader Header(uint type) => new(_reducer.State.Self.Guid, 0, type);

    /// <summary>The item the player carries as the stand-in knows it: the stand-in's pack, then what a move changed, then the world object (a recorded item).</summary>
    private bool TryEntry(uint guid, out StandInPackEntry entry)
    {
        var at = _pack.FindIndex(e => e.Guid == guid);
        if (at >= 0)
        {
            entry = _pack[at];
            return true;
        }

        if (_stacks.TryGetValue(guid, out entry))
        {
            return true;
        }

        if (_reducer.State.Objects.TryGet(guid, out var o))
        {
            entry = new StandInPackEntry(guid, o.Weenie.WeenieClassId, o.Name, o.Weenie.StackSize ?? 1);
            return true;
        }

        entry = default;
        return false;
    }

    private void SetEntry(StandInPackEntry entry)
    {
        var at = _pack.FindIndex(e => e.Guid == entry.Guid);
        if (at >= 0)
        {
            _pack[at] = entry;
        }
        else
        {
            _stacks[entry.Guid] = entry;
        }
    }

    private void RemoveEntry(uint guid)
    {
        _pack.RemoveAll(e => e.Guid == guid);
        _stacks.Remove(guid);
    }

    /// <summary>True when the player holds <paramref name="item"/> in a pack (not worn or wielded).</summary>
    private bool InPack(uint item, out StandInPackEntry entry)
    {
        entry = default;
        return _reducer.State.Self.Inventory.Find(item) is { Held: true, Kind: not InventoryPlaceKind.Equipped } && TryEntry(item, out entry);
    }

    private bool IsStack(uint item) => _reducer.State.Objects.TryGet(item, out var o) && o.Weenie.MaxStackSize is not null;

    private int UnitBurden(uint wcid) => KitWeenies.TryGetValue(wcid, out var kit) ? kit.UnitBurden : 0;

    /// <summary>The burden update (02CD) after a move: the stand-in has no weights (<c>TODO(m4-standin-item-burden)</c>), so it repeats the burden the model has, moved by the gear kit's burden when the item is one of its.</summary>
    private void BurdenNow(IAcSessionSink sink, int delta)
    {
        Placeholder("m4-standin-item-burden", "the stand-in has no weights: a move's 02CD repeats the burden the model has, moved by the gear kit's burden for a kit item");
        var burden = Math.Max(0, (_reducer.State.Self.Inventory.Burden ?? 0) + delta);
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PlayerInventory.EncumbranceProperty, burden);
    }

    private void PickupMotion(IAcSessionSink sink)
    {
        Placeholder("m4-standin-pickup-motion", "the pick-up and drop motions are the stance with no forward command: the stand-in has no motion table for the player's pick-up animation");
        SendPlayerMotion(sink, StanceNow(), MotionCommands.Ready);
    }

    private void ReturnStance(IAcSessionSink sink, uint sound)
    {
        PlaySound(sink, _reducer.State.Self.Guid, sound, 1f);
        SendPlayerMotion(sink, StanceNow(), MotionCommands.Ready);
    }

    // --- the pick-up --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The place an item the player can pick up lies in: the ground, or an open container (<paramref name="container"/> names it).</summary>
    private bool TryPickupSource(uint item, out StandInPackEntry entry, out StandInContainer? container)
    {
        if (_ground.TryGetValue(item, out var lying))
        {
            entry = lying.Item;
            container = null;
            return true;
        }

        if (_open != 0 && _containers.TryGetValue(_open, out var open))
        {
            var at = open.Items.FindIndex(i => i.Guid == item);
            if (at >= 0)
            {
                entry = open.Items[at];
                container = open;
                return true;
            }
        }

        entry = default;
        container = null;
        return false;
    }

    /// <summary>
    /// A PutItemInContainer (0x0019) of an item the player does not hold: a pick-up from the ground or from the open container (capture.md C2, C4). Own F748 and F74C, then after the pick-up motion (0.74 s from the ground,
    /// 1.35 s from a corpse) 02DA {Container}, 0022, 02CD; 31 ms later F750 0x8F and the return F74C; a ground item's F74A 15 ms after that.
    /// </summary>
    private OutboundTicket PickUp(uint item, uint container, uint placement)
    {
        if (Busy)
        {
            return Refused(item, busy: true);
        }

        var self = _reducer.State.Self;
        if (!TryPickupSource(item, out _, out var source) || (container != self.Guid && !self.Inventory.IsKnownContainer(container)))
        {
            return Refused(item, busy: false);
        }

        Placeholder("m4-standin-move-range", "the stand-in checks no reach for a pick-up: ACE walks the player to an item out of reach, and refuses one that is too far");
        var motion = source is null ? PickupGroundSeconds : PickupContainerSeconds;
        BeginChain(motion);
        var start = _reducer.Clock.Now + DelaySeconds;
        _reducer.StandIn!.At(start, sink =>
        {
            PlayerPositionNow(sink);
            PickupMotion(sink);
        });
        _reducer.StandIn!.At(start + motion, sink => PickedUp(sink, item, container, placement, fromGround: source is null));
        return Ticket();
    }

    private void PickedUp(IAcSessionSink sink, uint item, uint container, uint placement, bool fromGround)
    {
        if (!TryPickupSource(item, out var entry, out var source))
        {
            Failed(sink, item);
            return;
        }

        if (source is null)
        {
            _ground.Remove(item);
        }
        else
        {
            source.Items.RemoveAll(i => i.Guid == item);
        }

        _pack.Add(entry);
        var self = _reducer.State.Self;
        var slot = (uint)Math.Min(placement, (uint)self.Inventory.ItemsIn(container).Count);
        Instance(sink, item, ContainerProperty, container);
        AnswerLog.Add("0022");
        sink.OnInventoryPutObjInContainer(Context(sink), Header(0x0022), new InventoryPutObjInContainerEvent(item, container, slot, 0));
        BurdenNow(sink, UnitBurden(entry.Wcid) * entry.Stack);
        _reducer.StandIn!.At(_reducer.Clock.Now + ReturnStanceLagSeconds, later =>
        {
            ReturnStance(later, SoundPickUp);
            if (fromGround)
            {
                _reducer.StandIn!.At(_reducer.Clock.Now + PickupEventLagSeconds, last =>
                {
                    AnswerLog.Add("F74A");
                    last.OnPickupEvent(Context(last), PickupOf(item));
                });
            }
        });
    }

    // --- the drop -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A DropItem (0x001B; capture.md C4): own F748 and F74C, then after <see cref="DropSeconds"/> 02DA {Container 0}, 02CD, 02DA again, 019A, the item's F745 (container none), its F748, F74B and F748, F750 0x90 and the return F74C.
    /// </summary>
    internal OutboundTicket DropItem(uint item)
    {
        if (Busy)
        {
            return Refused(item, busy: true);
        }

        if (!InPack(item, out _))
        {
            return Refused(item, busy: false);
        }

        BeginChain(DropSeconds);
        var start = _reducer.Clock.Now + DelaySeconds;
        _reducer.StandIn!.At(start, sink =>
        {
            PlayerPositionNow(sink);
            PickupMotion(sink);
        });
        _reducer.StandIn!.At(start + DropSeconds, sink =>
        {
            if (!InPack(item, out var entry))
            {
                Failed(sink, item);
                return;
            }

            RemoveEntry(item);
            Instance(sink, item, ContainerProperty, 0);
            BurdenNow(sink, -UnitBurden(entry.Wcid) * entry.Stack);
            Instance(sink, item, ContainerProperty, 0);
            AnswerLog.Add("019A");
            sink.OnInventoryPutObjectIn3D(Context(sink), Header(0x019A), new InventoryPutObjectIn3DEvent(item));
            Land(sink, entry);
            ItemSettles(sink, entry.Guid, twoPositions: true);
            ReturnStance(sink, SoundDropItem);
        });
        return Ticket();
    }

    /// <summary>The item lands where the player stands: its F745 (container none).</summary>
    private void Land(IAcSessionSink sink, StandInPackEntry item)
    {
        var place = PlayerPlace() ?? new AcPosition(_visit.Cell, _visit.Origin, System.Numerics.Quaternion.Identity);
        _ground[item.Guid] = (item, place);
        AnswerLog.Add("F745");
        sink.OnCreateObject(Context(sink), ItemObject(item, null, place), isUpdate: false);
    }

    /// <summary>The item's own settling messages after it lands: F748, F74B (its state), and, for a drop, a second F748 (capture.md C4).</summary>
    private void ItemSettles(IAcSessionSink sink, uint item, bool twoPositions)
    {
        var place = _ground.TryGetValue(item, out var lying) ? lying.At : default;
        var stamps = _reducer.State.Objects.TryGet(item, out var body) ? body.Stamps : new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1);
        AnswerLog.Add("F748");
        sink.OnUpdatePosition(Context(sink), new UpdatePositionMsg(item, PositionFlags.Grounded, place, null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 1)), stamps.Teleport, stamps.ForcePosition));
        AnswerLog.Add("F74B");
        sink.OnSetState(Context(sink), new SetStateMsg(item, PhysicsState.Gravity | PhysicsState.ReportCollisions, stamps.Instance, unchecked((ushort)(stamps.State + 1))));
        if (twoPositions)
        {
            AnswerLog.Add("F748");
            sink.OnUpdatePosition(Context(sink), new UpdatePositionMsg(item, PositionFlags.Grounded, place, null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 2)), stamps.Teleport, stamps.ForcePosition));
        }
    }

    // --- the splits ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private bool ValidSplit(uint stack, int amount, out StandInPackEntry entry) =>
        InPack(stack, out entry) && IsStack(stack) && amount > 0 && amount < entry.Stack;

    /// <summary>
    /// A StackableSplitToContainer (0x0055; capture.md C4): 0022 {new guid, container, placement} first, then 0197 {old stack}, then the new stack's F745 (container = the container); no animation; 02CD
    /// <see cref="BurdenLagSeconds"/> later.
    /// </summary>
    internal OutboundTicket SplitToContainer(uint stack, uint container, int placement, int amount)
    {
        var self = _reducer.State.Self;
        if (Busy)
        {
            return Refused(stack, busy: true);
        }

        if (!ValidSplit(stack, amount, out _) || (container != self.Guid && !self.Inventory.IsKnownContainer(container)))
        {
            return Refused(stack, busy: false);
        }

        Later(sink =>
        {
            if (!ValidSplit(stack, amount, out var entry))
            {
                Failed(sink, stack);
                return;
            }

            var created = entry with { Guid = _nextItemGuid++, Stack = amount };
            SetEntry(entry with { Stack = entry.Stack - amount });
            _pack.Add(created);
            var slot = (uint)Math.Clamp(placement, 0, self.Inventory.ItemsIn(container).Count);
            AnswerLog.Add("0022");
            sink.OnInventoryPutObjInContainer(Context(sink), Header(0x0022), new InventoryPutObjInContainerEvent(created.Guid, container, slot, 0));
            StackSize(sink, stack, entry.Stack - amount);
            AnswerLog.Add("F745");
            sink.OnCreateObject(Context(sink), ItemObject(created, container, null), isUpdate: false);
            _reducer.StandIn!.At(_reducer.Clock.Now + BurdenLagSeconds, later => BurdenNow(later, 0));
        });
        return Ticket();
    }

    /// <summary>
    /// A StackableSplitTo3D (0x0056; capture.md C4): own F748 and F74C, then after <see cref="PickupGroundSeconds"/> 0197 {old stack}, 02CD, the new stack's F745 (container none), its F748 and F74B, F750 0x90 and the return F74C.
    /// </summary>
    internal OutboundTicket SplitTo3D(uint stack, int amount)
    {
        if (Busy)
        {
            return Refused(stack, busy: true);
        }

        if (!ValidSplit(stack, amount, out _))
        {
            return Refused(stack, busy: false);
        }

        BeginChain(PickupGroundSeconds);
        var start = _reducer.Clock.Now + DelaySeconds;
        _reducer.StandIn!.At(start, sink =>
        {
            PlayerPositionNow(sink);
            PickupMotion(sink);
        });
        _reducer.StandIn!.At(start + PickupGroundSeconds, sink =>
        {
            if (!ValidSplit(stack, amount, out var entry))
            {
                Failed(sink, stack);
                return;
            }

            var created = entry with { Guid = _nextItemGuid++, Stack = amount };
            SetEntry(entry with { Stack = entry.Stack - amount });
            StackSize(sink, stack, entry.Stack - amount);
            BurdenNow(sink, -UnitBurden(entry.Wcid) * amount);
            Land(sink, created);
            ItemSettles(sink, created.Guid, twoPositions: false);
            ReturnStance(sink, SoundDropItem);
        });
        return Ticket();
    }

    /// <summary>
    /// A StackableSplitToWield (0x019B; capture.md C4): 0197 {old stack}, 02DA {new, Wielder}, 02CE, 0023 {new, mask}, F625, F750 0x8C, then the new stack's F745; no animation. A slot already holding ammunition refuses: 0197 {old stack}, 02EB
    /// "You must remove your &lt;ammo&gt; to wield &lt;ammo&gt;", 00A0 {new guid}, 02CD and the old stack's 0197 restored (run 2).
    /// </summary>
    internal OutboundTicket SplitToWield(uint stack, uint mask, int amount)
    {
        if (Busy)
        {
            return Refused(stack, busy: true);
        }

        if (!ValidSplit(stack, amount, out _))
        {
            return Refused(stack, busy: false);
        }

        Later(sink =>
        {
            if (!ValidSplit(stack, amount, out var entry))
            {
                Failed(sink, stack);
                return;
            }

            var self = _reducer.State.Self;
            var created = entry with { Guid = _nextItemGuid++, Stack = amount };
            StackSize(sink, stack, entry.Stack - amount);
            if ((mask & (uint)EquipMask.MissileAmmo) != 0 && HasWielded((uint)EquipMask.MissileAmmo, out _, out var ammo))
            {
                AnswerLog.Add("02EB");
                sink.OnTransientString(Context(sink), Header(0x02EB), new TransientStringEvent("You must remove your " + ammo.Name + " to wield " + entry.Name));
                Failed(sink, created.Guid);
                BurdenNow(sink, 0);
                StackSize(sink, stack, entry.Stack);
                return;
            }

            SetEntry(entry with { Stack = entry.Stack - amount });
            _wielded[created.Guid] = created;
            _wieldMasks[created.Guid] = mask;
            Instance(sink, created.Guid, WielderProperty, self.Guid);
            AnswerLog.Add("02CE");
            sink.OnPublicUpdatePropertyInt(Context(sink), new PublicUpdatePropertyIntMsg(NextSequence(created.Guid, CurrentWieldedLocationProperty), created.Guid, CurrentWieldedLocationProperty, unchecked((int)mask)));
            AnswerLog.Add("0023");
            sink.OnWieldObject(Context(sink), Header(0x0023), new WieldObjectEvent(created.Guid, mask));
            if (_reducer.State.Objects.TryGet(self.Guid, out var player))
            {
                AnswerLog.Add("F625");
                sink.OnObjDescEvent(Context(sink), new ObjDescEventMsg(self.Guid, player.Model, player.Stamps.Instance, unchecked((ushort)(player.Stamps.ObjDesc + 1))));
            }

            PlaySound(sink, self.Guid, SoundWieldAmmo, 1f);
            AnswerLog.Add("F745");
            sink.OnCreateObject(Context(sink), ItemObject(created, null, null, self.Guid, mask), isUpdate: false);
        });
        return Ticket();
    }

    // --- the give -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A GiveObjectRequest (0x00CD; capture.md C4 runs 2 to 4). An NPC that accepts the item (<see cref="AcceptGive"/>): the player's turn (F74C), then after <see cref="GiveSeconds"/> 0024 {item}, 02CD, 0022 {item, NPC}, a chat line and F750
    /// {NPC, 0x8E}. Any other item or NPC: the turn, then 028B {0x046A, the NPC's name} and 00A0 {item, 0}. Always the whole stack (<c>TODO(m4-standin-give-amount)</c>).
    /// </summary>
    internal OutboundTicket Give(uint target, uint item, int amount)
    {
        if (Busy)
        {
            return Refused(item, busy: true);
        }

        if (!InPack(item, out var held) || amount <= 0)
        {
            return Refused(item, busy: false);
        }

        if (amount < held.Stack)
        {
            Placeholder("m4-standin-give-amount", "a give of part of a stack gives the whole stack: the stand-in does not split a stack for a give");
        }

        var accepts = _giveAccepts.TryGetValue(target, out var wcids) && wcids.Contains(held.Wcid);
        BeginChain(GiveSeconds);
        var start = _reducer.Clock.Now + DelaySeconds;
        _reducer.StandIn!.At(start, sink => SendPlayerMotion(sink, StanceNow(), MotionCommands.Ready));
        if (!accepts)
        {
            _reducer.StandIn!.At(start + RefuseLagSeconds, sink =>
            {
                Placeholder("m4-standin-give-emotes", "the stand-in's NPCs have no emotes: only an NPC given AcceptGive takes an item, every other give draws ACE's 'no emote for this item' refusal (0x046A)");
                var name = _reducer.State.Objects.TryGet(target, out var npc) ? npc.Name : string.Empty;
                AnswerLog.Add("028B");
                sink.OnWeenieError(Context(sink), Header(0x028B), new WeenieErrorEvent(GiveRefused, name));
                Failed(sink, item);
            });
            return Ticket();
        }

        _reducer.StandIn!.At(start + GiveSeconds, sink =>
        {
            if (!InPack(item, out var entry))
            {
                Failed(sink, item);
                return;
            }

            RemoveEntry(item);
            AnswerLog.Add("0024");
            sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(item));
            BurdenNow(sink, -UnitBurden(entry.Wcid) * entry.Stack);
            AnswerLog.Add("0022");
            sink.OnInventoryPutObjInContainer(Context(sink), Header(0x0022), new InventoryPutObjInContainerEvent(item, target, 0, 0));
            Placeholder("m4-standin-give-line", "the give's chat line is 'You give <npc> <item>.' as ACE words it in Player_Inventory.cs (not verified against the capture's text)");
            var who = _reducer.State.Objects.TryGet(target, out var npc) ? npc.Name : "it";
            AnswerLog.Add("F7E0");
            sink.OnServerMessage(Context(sink), new ServerMessageMsg("You give " + who + " " + entry.Name + ".", BroadcastChat));
            PlaySound(sink, target, SoundGiveItem, 1f);
        });
        return Ticket();
    }
}

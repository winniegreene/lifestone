// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Container.cs (Open, Close, the chest's ActOnUse/Reset, 853-875: the close animation's half-length delay),
// Source/ACE.Server/Network/GameEvent/Events/GameEventViewContents.cs and GameEventCloseGroundContainer.cs (AGPL-3.0)
// [with changes: the order is the one the live capture records (cache/m4/2-1/capture.md C2 and C3); the stand-in keeps the containers' contents itself].
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The stand-in's chest (M4 2.1 s3, <c>TODO(m4-standin-chest)</c>): an object the player can open. The stand-in has no weenie database, so it is weenie 143's own Setup and icon with a name that says it is a placeholder; its
/// weenie class id (143, the "Chest" the live capture opened) is what marks it as a container. A host places one with <see cref="StandInServer.PlaceChest"/>.
/// </summary>
public static class StandInChest
{
    /// <summary>The weenie class id of the chest the live capture opened (C3: "Chest" 143).</summary>
    public const uint Wcid = 143;

    /// <summary>The chest's name (it says it is a placeholder).</summary>
    public const string Name = "Chest (stand-in placeholder)";

    /// <summary>The object description bit of an openable object (ACE <c>ObjectDescriptionFlags.Openable</c>).</summary>
    public const uint OpenableFlag = 0x1;

    /// <summary>A chest for a placement or a visit.</summary>
    /// <param name="guid">The object id.</param>
    public static StandInObject Make(uint guid) => new(guid, Name, default, StandInWeenieArt.SetupOr(Wcid, StandInVisit.HumanSetup), 0, DescriptionFlags: OpenableFlag, WeenieClassId: Wcid); // M4 4.1 v4: weenie 143 "chest"'s own Setup (the class id the live capture opened), not a human
}

// M4 2.1 s3: the containers the player opens (a creature's corpse, a chest) and what they hold, in the order ACE sends (capture.md C2, C3).
internal sealed partial class StandInWorld
{
    /// <summary>How long after the turn a chest's contents are announced (capture.md C3: the open animation, 1.24 s).</summary>
    internal const double ChestOpenSeconds = 1.24;

    /// <summary>How long after a chest's Off motion its 0052 follows (half the close animation, <c>W/Container.cs</c> 853-875; capture.md C3).</summary>
    internal const double ChestCloseSeconds = 0.42;

    /// <summary>How long after the 0196 and the UseDone the contents' CreateObjects follow (capture.md C2: 16 ms).</summary>
    internal const double ContentsLagSeconds = 0.016;

    /// <summary>The weenie class id of the bowl a corpse holds (a placeholder item; <c>TODO(m4-standin-corpse-contents)</c>).</summary>
    internal const uint BowlWcid = 141;

    /// <summary>The weenie class id of the smock a corpse holds (a placeholder item).</summary>
    internal const uint SmockWcid = 2589;

    /// <summary>The Value the stand-in gives the corpse's Bowl (a placeholder, not ACE's weenie value: <c>TODO(m4-standin-corpse-contents)</c>).</summary>
    internal const uint BowlValue = 10;

    /// <summary>The Value the stand-in gives the corpse's Smock (a placeholder).</summary>
    internal const uint SmockValue = 25;

    /// <summary>
    /// The description the stand-in gives the two items a corpse holds (M4 2.2 s0, <c>TODO(m4-standin-corpse-contents)</c>): a Value above 0 and an ItemType the vendor mask covers (the Bowl Misc 0x80, the Smock Clothing 0x4), so the
    /// offline hunt loots them under the default rule. The values and burdens are <b>placeholders</b>: the stand-in has no weenie database, and ACE's own Bowl and Smock have their own.
    /// </summary>
    private static bool TryCorpsePlaceholder(uint wcid, out uint itemType, out uint value, out ushort burden)
    {
        switch (wcid)
        {
            case BowlWcid:
                (itemType, value, burden) = (0x80u, BowlValue, 15);
                return true;
            case SmockWcid:
                (itemType, value, burden) = (0x4u, SmockValue, 30);
                return true;
            default:
                (itemType, value, burden) = (0u, 0u, 0);
                return false;
        }
    }

    /// <summary>The first guid the stand-in gives a container's items.</summary>
    internal const uint FirstContentGuid = 0x80060000;

    private const uint ViewContentsType = 0x0196;
    private const uint CloseGroundType = 0x0052;

    private uint _nextContentGuid = FirstContentGuid;
    private readonly Dictionary<uint, StandInContainer> _containers = [];
    private uint _open;

    /// <summary>A container of the stand-in: a chest and the items it holds (M4 4.1 fix2b: the creature corpse is gone with the creature, D23).</summary>
    private sealed class StandInContainer(uint guid, string name)
    {
        public uint Guid { get; } = guid;

        public string Name { get; } = name;

        public bool On { get; set; }

        public List<StandInPackEntry> Items { get; } = [];
    }

    /// <summary>Test lever (2.1-R5): a Use of a closed container is answered by a UseDone alone (no 0196, no turn, nothing opens), as a refused open reads.</summary>
    internal bool AnswerOpenWithUseDoneOnly { get; set; }

    /// <summary>Test lever (2.1-R5): the player's health becomes <paramref name="value"/> <paramref name="after"/> seconds from now (a PrivateUpdateVital, vital current 2).</summary>
    internal void SetPlayerHealthLater(double after, uint value) =>
        _reducer.StandIn!.At(_reducer.Clock.Now + after, sink => sink.OnPrivateUpdate(Context(sink), new PrivateUpdateMsg(PrivateUpdateKind.VitalCurrent, 200, 2, value, default, default, default)));

    /// <summary>The container the player has open now (a chest), or 0.</summary>
    internal uint OpenContainerGuid => _open;

    /// <summary>The guids of the items the container <paramref name="container"/> holds (tests); empty for none.</summary>
    internal IReadOnlyList<uint> ContentsOf(uint container) => _containers.TryGetValue(container, out var c) ? [.. c.Items.Select(i => i.Guid)] : [];

    /// <summary>Puts <paramref name="items"/> into the container <paramref name="container"/> (a chest placed or in the visit); the tests' and the checks' way of stocking a chest.</summary>
    internal void Stock(uint container, params StandInPackEntry[] items)
    {
        if (!TryContainer(container, out var c))
        {
            throw new ArgumentException("the stand-in holds no container with that id", nameof(container));
        }

        c.Items.AddRange(items);
    }

    /// <summary>Stocks the chest <paramref name="chest"/> with items given as name, weenie class id and stack.</summary>
    internal void StockChest(uint chest, (string Name, uint Wcid, int Stack)[] items)
    {
        // The chest object reaches the stand-in's tables with its CreateObject (PlaceCreature records it first).
        Stock(chest, [.. items.Select(i => new StandInPackEntry(_nextContentGuid++, i.Wcid, i.Name, i.Stack))]);
    }

    /// <summary>A new guid for a stocked item (the stand-in's own range).</summary>
    internal uint NewContentGuid() => _nextContentGuid++;

    private bool TryContainer(uint guid, out StandInContainer container)
    {
        if (_containers.TryGetValue(guid, out container!))
        {
            return true;
        }

        var kind = _placed.TryGetValue(guid, out var placed) ? placed : _visit.Objects.FirstOrDefault(o => o.Guid == guid);
        if (kind.Guid == guid && guid != 0 && kind.WeenieClassId == StandInChest.Wcid)
        {
            container = _containers[guid] = new StandInContainer(guid, kind.Name);
            return true;
        }

        container = null!;
        return false;
    }

    /// <summary>
    /// A Use of a chest (capture.md C3; the corpse path of C2 went with the creature, M4 4.1 fix2b). Closed: the player's turn (F74C), then 0196 {container, entries}, 01C7 UseDone 0 and the F745 of every item (container = the container) 16 ms later; a
    /// chest opens <see cref="ChestOpenSeconds"/> after the turn and its On motion follows the UseDone. Open: it closes (its Off motion, UseDone, then 0052 <see cref="ChestCloseSeconds"/>
    /// later). Opening a second container closes the first (its 0052 first).
    /// </summary>
    private OutboundTicket UseContainer(StandInContainer container)
    {
        Later(sink =>
        {
            if (_open == container.Guid)
            {
                CloseByUse(sink, container);
                return;
            }

            if (AnswerOpenWithUseDoneOnly)
            {
                UseDone(sink);
                return;
            }

            if (_open != 0 && _containers.TryGetValue(_open, out var previous))
            {
                CloseNow(sink, previous);
            }

            TurnToContainer(sink, container);
            Placeholder("m4-standin-chest-motion", "a chest's On and Off are UpdateMotions with a placeholder command (Stop for On, Ready for Off): the stand-in has no motion table for the chest");
            _reducer.StandIn!.At(_reducer.Clock.Now + ChestOpenSeconds, later => Reveal(later, container));
        });
        return Ticket();
    }

    /// <summary>A NoLongerViewingContents (0x0195): the open container closes with its 0052 (a chest: its Off motion first, the 0052 <see cref="ChestCloseSeconds"/> later), and no UseDone. Any other container is ignored.</summary>
    internal OutboundTicket StopViewing(uint container)
    {
        Later(sink =>
        {
            if (_open != container || !_containers.TryGetValue(container, out var open))
            {
                return;
            }

            _open = 0;
            ChestMotion(sink, open, on: false);
            _reducer.StandIn!.At(_reducer.Clock.Now + ChestCloseSeconds, later => Closed(later, open));
        });
        return Ticket();
    }

    private void Reveal(IAcSessionSink sink, StandInContainer container)
    {
        _open = container.Guid;
        var self = _reducer.State.Self.Guid;
        var entries = new ViewContentsEntry[container.Items.Count];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new ViewContentsEntry(container.Items[i].Guid, 0);
        }

        AnswerLog.Add("0196");
        sink.OnViewContents(Context(sink), new GameEventHeader(self, 0, ViewContentsType), new ViewContentsEvent(container.Guid, entries));
        UseDone(sink);
        ChestMotion(sink, container, on: true);

        var items = container.Items.ToArray();
        _reducer.StandIn!.At(_reducer.Clock.Now + ContentsLagSeconds, later =>
        {
            if (_open != container.Guid)
            {
                return; // closed before the contents were sent
            }

            foreach (var item in items)
            {
                if (container.Items.Any(i => i.Guid == item.Guid))
                {
                    AnswerLog.Add("F745");
                    later.OnCreateObject(Context(later), ItemObject(item, container.Guid, null), isUpdate: false);
                }
            }
        });
    }

    private void CloseByUse(IAcSessionSink sink, StandInContainer container)
    {
        _open = 0;
        ChestMotion(sink, container, on: false);
        UseDone(sink);
        _reducer.StandIn!.At(_reducer.Clock.Now + ChestCloseSeconds, later => Closed(later, container));
    }

    /// <summary>Closes the open container <paramref name="container"/> at once (another is being opened): a chest's Off motion, then the 0052.</summary>
    private void CloseNow(IAcSessionSink sink, StandInContainer container)
    {
        _open = 0;
        ChestMotion(sink, container, on: false);
        Closed(sink, container);
    }

    private void Closed(IAcSessionSink sink, StandInContainer container)
    {
        AnswerLog.Add("0052");
        sink.OnCloseGroundContainer(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, CloseGroundType), new CloseGroundContainerEvent(container.Guid));
    }

    private void TurnToContainer(IAcSessionSink sink, StandInContainer container)
    {
        SendPlayerMotion(sink, StanceNow(), MotionCommands.Ready); // the turn toward the chest (a placeholder motion: the stand-in sends no MoveTo for it; capture.md C3: a chest's turn carries no F748, a corpse's did)
    }

    private void ChestMotion(IAcSessionSink sink, StandInContainer container, bool on)
    {
        container.On = on;
        var stamps = _reducer.State.Objects.TryGet(container.Guid, out var body) ? body.Stamps : default;
        var wire = MotionCommandTable.ToWire(MotionCommands.NonCombat);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Forward = on ? MotionCommandTable.ToWire(MotionCommands.Stop) : null,
        };
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(container.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    /// <summary>The stance word of the player's combat mode now (the stand-in's own motions use it so a turn or a pick-up does not drop the stance).</summary>
    private uint StanceNow() => CombatStances.ForMode(_reducer.State.Self.Qualities.CombatMode, _reducer.State, out _); // by the wielded weapon, as ACE's GetCombatStance does (M4 4.1 v3)

    /// <summary>The player's position as the stand-in last knew it (F748 sends it as the player's own update), or null.</summary>
    private AcPosition? PlayerPlace() =>
        _reducer.State.Self.Position is { } p ? new AcPosition(p.CellId, p.Frame.Origin, p.Frame.Orientation) : null;

    private void PlayerPositionNow(IAcSessionSink sink)
    {
        if (PlayerPlace() is not { } place)
        {
            return;
        }

        var self = _reducer.State.Self;
        var stamps = self.Stamps;
        AnswerLog.Add("F748");
        sink.OnUpdatePosition(Context(sink), new UpdatePositionMsg(self.Guid, PositionFlags.Grounded, place, null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 1)), stamps.Teleport, stamps.ForcePosition));
    }

    /// <summary>The stack limit of an item of weenie <paramref name="wcid"/> as the stand-in knows it (the visit's items, then the gear kit), or null for an item that does not stack.</summary>
    private int? MaxStackOf(uint wcid) =>
        (_visit.Items ?? StandInItemKind.Default).TryGetValue(wcid, out var kind) ? kind.MaxStack : KitWeenies.TryGetValue(wcid, out var kit) ? kit.Kind.MaxStack : null;

    /// <summary>
    /// The CreateObject of an item the container / the ground / a wielder shows: the weenie's own Setup and icons from <see cref="StandInWeenieArt"/> (<c>TODO(m3-standin-weenies)</c>; <c>TODO(m4-standin-art-missing)</c> for a class id the table lacks), the item's stack and limit, and,
    /// when <paramref name="container"/> is set, that container; on the ground, <paramref name="at"/>.
    /// </summary>
    private CreateObjectMsg ItemObject(StandInPackEntry item, uint? container, AcPosition? at, uint wielder = 0, uint location = 0)
    {
        var kit = KitWeenies.GetValueOrDefault(item.Wcid);
        var max = _reducer.State.Objects.TryGet(item.Guid, out var known) && known.Weenie.MaxStackSize is { } m ? m : MaxStackOf(item.Wcid);
        var header = WeenieHeaderFlags.None;
        var corpseItem = TryCorpsePlaceholder(item.Wcid, out var corpseType, out var corpseValue, out var corpseBurden);
        if (corpseItem)
        {
            header |= WeenieHeaderFlags.Value | WeenieHeaderFlags.Burden;
        }

        if (container is not null)
        {
            header |= WeenieHeaderFlags.Container;
        }

        if (wielder != 0)
        {
            header |= WeenieHeaderFlags.Wielder | WeenieHeaderFlags.CurrentlyWieldedLocation;
        }

        if (max is not null)
        {
            header |= WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize;
        }

        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | (at is not null ? PhysicsDescFlags.Position : PhysicsDescFlags.None),
            State = (at is not null ? PhysicsState.Gravity | PhysicsState.ReportCollisions : PhysicsState.IgnoreCollisions | PhysicsState.Ethereal) | LightingOf(item.Wcid),
            Position = at ?? default,
            SetupId = SetupFor(item.Wcid, kit),
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = header,
            Name = item.Name,
            WeenieClassId = item.Wcid,
            IconId = StandInWeenieArt.PlaceholderIcon,
            ItemType = corpseItem ? corpseType : kit?.ItemType ?? (item.Wcid == StandInItemKind.PyrealWcid ? 0x40u : 0x80u),
            Value = corpseItem ? corpseValue : null,
            Burden = corpseItem ? corpseBurden : null,
            ObjectDescriptionFlags = 0,
            ContainerId = container,
            WielderId = wielder != 0 ? wielder : null,
            CurrentlyWieldedLocation = wielder != 0 ? location : null,
            StackSize = max is not null ? (ushort)Math.Min(item.Stack, ushort.MaxValue) : null,
            MaxStackSize = max is { } limit ? (ushort)Math.Min(limit, ushort.MaxValue) : null,
        };
        return new CreateObjectMsg(item.Guid, ModelData.Empty, physics, StandInWeenieArt.Apply(weenie, Log));
    }
}

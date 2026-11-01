using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The world phase (ST8): the one owner of retail's SmartBox semantics (m2-plan §1.4), re-expressed from the 11.4186
/// decompile (<c>PORTAL/smartbox/SmartBox.c</c>, <c>AC/acsmartbox/ACSmartBox.c</c>, <c>PORTAL/cphysics/CPhysics.c</c>;
/// functions cited with both addresses, nothing copied). The reducer classifies and records; physics and the avatars act
/// on its events.
/// </summary>
/// <remarks>
/// <para><b>Before the player.</b> Until F746 PlayerCreate names the player, every SmartBox message is held and replayed
/// in arrival order once it does (<c>ACSmartBox::IsReadyToDispatchEvent</c>, 11.4186 0x00559590, 11.6096 0x0055A1D0;
/// <c>SmartBox::QueueNetBlob</c> 0x00453980 / 0x00453A20; <c>SmartBox::ProcessNetBlobs</c> 0x00454A70 / 0x00454B10).</para>
/// <para><b>The instance gate</b> (<see cref="ObjectMessageGate"/>) heads every per-object handler: an unknown object or
/// a newer instance parks the message under the object (<c>CObjectMaint::QueueBlobForObject</c>, 0x005092D0 /
/// 0x00509DA0), an equal one applies it, an older one drops it. Parked messages replay in arrival order right after the
/// object's creation (<c>SmartBox::ProcessObjectNetBlobs</c>, 0x00454B20 / 0x00454BC0), through the same handlers (a
/// message that still does not apply parks again). The bounds (32 per object, 4,096 in all, 60 s) are Lifestone's
/// (<see cref="PendingObjectMessages"/>).</para>
/// <para><b>Per-field sequences.</b> An applied message changes a field only when its own stamp is newer
/// (<c>CPhysicsObj::newer_event</c>, 0x00451B10 / 0x00451B50).</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    private double? _replayParkedAt;
    private readonly List<uint> _culled = new(64);
    private bool _cullTodoLogged;

    /// <summary>The client-side cull (<see cref="ObjectCull"/>, M5 item 0 c1): ticked by <see cref="CullObjects"/>.</summary>
    internal ObjectCull Cull { get; } = new();

    /// <summary>
    /// M5 7 P7d: keep orphaned objects as the client did before D139 row f (no contents dropped with a deleted object, no doom for an object that left
    /// the player's holdings). Off (the rule) by default; the replayer turns it on for a recording whose header predates the rule
    /// (<see cref="Recording.RecordingHeader.ClientRules"/>), so the recording replays to its own trailer digest.
    /// </summary>
    internal bool KeepsOrphans { get; set; }

    /// <summary>
    /// F745 ObjectCreate and F7DB UpdateObject. Re-expresses <c>SmartBox::HandleCreateObject</c> (11.4186 0x00454C80,
    /// 11.6096 0x00454D20) and <c>SmartBox::HandleUpdateObject</c> (0x00455580 / 0x00455620), which is the same call with
    /// a forced re-create:
    /// <list type="number">
    /// <item>a parent the server names but the client does not know parks the message under the parent;</item>
    /// <item>an unknown object, a forced re-create, or a newer instance creates the object anew (<see cref="ObjectCreated"/>,
    /// <c>Recreated</c> when it replaced one), then replays what was parked for it;</item>
    /// <item>an older instance drops the message;</item>
    /// <item>the same instance refreshes it in retail's order: the visual description (on its own sequence), then the
    /// parent, or the position (the full position classification, with contact), or a pickup when it has neither; then
    /// the movement, the state and the vectors, each on its own sequence.</item>
    /// </list>
    /// An object with neither position nor parent is kept out of the world (inventory items).
    /// </summary>
    public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (HoldUntilPlayer(ParkedKind.Create, context, message, message.Guid, isUpdate))
        {
            return;
        }

        var physics = message.Physics;
        WorldObject? parent = null;
        if (physics.Parent is { Guid: not 0 } link && !_state.Objects.TryGet(link.Guid, out parent))
        {
            Park(link.Guid, message.Guid, ParkedKind.Create, context, message, isUpdate);
            return;
        }

        _state.Objects.TryGet(message.Guid, out var existing);
        var instance = physics.Timestamps.Instance;
        if (existing is null || isUpdate || Sequence16.IsNewer(existing.Stamps.Instance, instance))
        {
            CreateNew(message, parent, recreated: existing is not null);
            Cull.Seen(message.Guid);
            Cull.Restore(message.Guid); // M5 7 P7d: a create re-announces the object, so a doom (0024, 0022 away) is lifted (RemoveObjectToBeDestroyed)
            NoteObjectArrived(message.Guid);
            return;
        }

        if (existing.Stamps.Instance != instance)
        {
            ReportDrop(isUpdate ? MessageOpcodes.UpdateObject : MessageOpcodes.ObjectCreate, message.Guid, DropReason.OlderInstance);
            return;
        }

        UpdateExisting(existing, message, parent);
        Cull.Seen(message.Guid);
        Cull.Restore(message.Guid);
        NoteObjectArrived(message.Guid);
    }

    /// <summary>
    /// F747 ObjectDelete. Re-expresses <c>SmartBox::HandleDeleteObject</c> (11.4186 0x00451EA0, 11.6096 0x00451EE0): a
    /// delete of the player is ignored; otherwise the instance gate decides.
    /// </summary>
    public void OnDeleteObject(in InboundContext context, in DeleteObjectMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Delete, context, message, message.Guid))
        {
            return;
        }

        _state.Objects.TryGet(message.Guid, out var target);
        switch (ObjectMessageGate.CheckDelete(message.Guid, _state.Self.Guid, target, message.InstanceSeq))
        {
            case ObjectGateVerdict.IgnoredSelf:
                ReportDrop(MessageOpcodes.ObjectDelete, message.Guid, DropReason.SelfDelete);
                return;
            case ObjectGateVerdict.Park:
                Park(message.Guid, message.Guid, ParkedKind.Delete, context, message);
                return;
            case ObjectGateVerdict.Drop:
                ReportDrop(MessageOpcodes.ObjectDelete, message.Guid, DropReason.OlderInstance);
                return;
        }

        RemoveObject(message.Guid);
        if (!KeepsOrphans)
        {
            DropContentsOf(message.Guid);
        }
    }

    /// <summary>
    /// M5 7 P7d (D139 row f): a deleted object's contents, and what it wielded or carried, go with it, at any depth, except what the player holds.
    /// ACE deletes a rotted corpse (or a chest, or a creature) with one F747 and sends nothing for what was inside it, which it showed this client
    /// only when the player opened it; the round-13 soak kept about 110 such orphans a den stretch. Retail never keeps them either: closing the
    /// ground container queues its contents for destruction (<c>ClientUISystem::SetGroundObject</c>, 11.6096 0x005652B0, then
    /// <c>ACCWeenieObject::AddContentsToDestructionQueue</c>, 0x0058D7D0), and <c>CObjectMaint::DeleteObject</c> (0x00508F30) unparents
    /// a deleted object's children; with the holder gone nothing can reach them, so they leave at once, through the F747 path, one line per
    /// delete that took any (category <c>objects</c>).
    /// </summary>
    private void DropContentsOf(uint holder)
    {
        _culled.Clear();
        _culled.Add(holder);
        ObjectCull.AddFollowers(_state, _culled, 0, 0);
        var count = _culled.Count - 1;
        if (count == 0)
        {
            return;
        }

        for (var i = 1; i < _culled.Count; i++)
        {
            if (_state.Objects.Contains(_culled[i]))
            {
                RemoveObject(_culled[i]);
            }
        }

        Cull.NoteContentsDropped(count);
        _log.Info(
            ObjectsLogCategory,
            "a deleted object's contents left with it",
            new LogField("holder", holder.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)),
            new LogField("count", count),
            new LogField("first", _culled[1].ToString("X8", System.Globalization.CultureInfo.InvariantCulture)),
            new LogField("orphans_total", Cull.OrphansDropped),
            new LogField("objects", _state.Objects.Count));
    }

    /// <summary>
    /// Removes an object the way an applied F747 does, for the F747 itself and for the client-side cull: out of the table,
    /// the inventory, the container views, the vendor, the cull's queues, then <see cref="ObjectDeleted"/>, which every
    /// consumer follows (the avatar set, the light table, the fight's tables, the wayfinder's creature feed).
    /// </summary>
    private void RemoveObject(uint guid)
    {
        _state.Objects.Remove(guid);
        Cull.Gone(guid);
        ReleaseDeletedHeld(guid);
        ForgetInViews(guid);
        ForgetVendor(guid);
        Changed();
        Publish(new ObjectDeleted(guid));
    }

    /// <summary>
    /// The client-side cull's tick (<see cref="ObjectCull"/>; retail's <c>CObjectMaint::UseTime</c>, 11.6096 0x00509480): the
    /// session calls it once per tick, after the parked messages expire. Each due object leaves through
    /// <see cref="RemoveObject"/>, holders before what they hold; one INFO line per batch (category <c>objects</c>).
    /// Allocation-free when nothing is culled.
    /// </summary>
    internal void CullObjects()
    {
        _culled.Clear();
        var count = Cull.Collect(_state, _clock.Now, _culled);
        if (!_cullTodoLogged && Cull.CellsMissingSeen)
        {
            _cullTodoLogged = true;
            _log.Info(ObjectsLogCategory, "TODO(m5-object-cull-cells): this host gives the cull no cell visibility (ObjectCull.Cells), so inside an interior cell the player's landblock and its eight neighbours count as visible, not a dungeon cell's visible cells");
        }

        if (Cull.LastDoomedKept != 0)
        {
            // M5 7 P7d: a doomed object whose time came but which is back with the player, in the open ground container, or in the world.
            _log.Info(ObjectsLogCategory, "doomed objects kept: back with the player, in the open ground container or placed in the world", new LogField("kept", Cull.LastDoomedKept), new LogField("doom_pending", Cull.DoomPending));
        }

        if (count == 0)
        {
            return;
        }

        var first = _culled[0];
        for (var i = 0; i < _culled.Count; i++)
        {
            var guid = _culled[i];
            if (_state.Objects.Contains(guid))
            {
                RemoveObject(guid);
            }
        }

        _log.Info(
            ObjectsLogCategory,
            "culled objects the server stopped sending",
            new LogField("count", count),
            new LogField("first", first.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)),
            new LogField("orphans", Cull.LastOrphans), // M5 7 P7d: the doomed contained objects and what they held, within count
            new LogField("pending", Cull.Pending),
            new LogField("doom_pending", Cull.DoomPending),
            new LogField("culled_total", Cull.Culled),
            new LogField("orphans_total", Cull.OrphansDropped),
            new LogField("objects", _state.Objects.Count));
    }

    /// <summary>The log category of the cull's lines.</summary>
    internal const string ObjectsLogCategory = "objects";

    /// <summary>
    /// F748 UpdatePosition. Re-expresses <c>SmartBox::UnpackPositionEvent</c> (11.4186 0x004542C0, 11.6096 0x00454360:
    /// the instance gate) and <c>SmartBox::HandleReceivedPosition</c> (0x00453FD0 / 0x00454070: the verdict; see
    /// <see cref="ReceivePosition"/>).
    /// </summary>
    public void OnUpdatePosition(in InboundContext context, in UpdatePositionMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Position, context, message, message.Guid))
        {
            return;
        }

        _state.Objects.TryGet(message.Guid, out var target);
        if (!Admit(ObjectMessageGate.Check(target, message.InstanceSeq), ParkedKind.Position, message.Guid, context, message))
        {
            return;
        }

        var p = message.Position;
        var applied = ReceivePosition(
            target!,
            new CellFrame(p.CellId, new AcFrame(p.Origin, p.Rotation)),
            message.Placement,
            message.Grounded,
            message.Velocity,
            message.PositionSeq,
            message.TeleportSeq,
            message.ForcePositionSeq,
            out var moved,
            out var reason);
        SyncSelf(target!);
        if (!applied)
        {
            ReportDrop(MessageOpcodes.UpdatePosition, message.Guid, reason);
            return;
        }

        var travel = moved.Verdict == PositionVerdict.SelfTeleport ? Travel(SelfTravel.TeleportArrived) : null;
        Changed();
        Publish(in moved);
        PublishTravel(travel);
        if (moved.IsSelf)
        {
            CheckVendorRange(); // M4 3.2 s1
        }
    }

    /// <summary>
    /// F74C UpdateMotion. The instance gate is <c>ACSmartBox::DispatchSmartBoxEvent</c>'s own (11.4186 0x005595D0,
    /// 11.6096 0x0055A210); the sequences are <c>CPhysics::SetObjectMovement</c>'s (0x00509690 / 0x0050A160; see
    /// <see cref="SetMovement"/>). The player's own motion is stored like any other, flagged; consumers follow retail in
    /// not replaying an autonomous echo of the player's input.
    /// </summary>
    public void OnUpdateMotion(in InboundContext context, UpdateMotionMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (HoldUntilPlayer(ParkedKind.Motion, context, message, message.Guid))
        {
            return;
        }

        _state.Objects.TryGet(message.Guid, out var target);
        if (!Admit(ObjectMessageGate.Check(target, message.InstanceSeq), ParkedKind.Motion, message.Guid, context, message))
        {
            return;
        }

        if (!SetMovement(target!, message.Motion, message.MovementSeq, message.ServerControlSeq, message.IsAutonomous))
        {
            SyncSelf(target!);
            ReportDrop(MessageOpcodes.UpdateMotion, message.Guid, DropReason.Stale);
            return;
        }

        SyncSelf(target!);
        Changed();
        Publish(new ObjectMotionChanged(message.Guid, target!.IsSelf, message.IsAutonomous, message.Motion));
    }

    /// <summary>
    /// F74E VectorUpdate: <c>SmartBox::HandleVectorUpdate</c> (11.4186 0x00453480, 11.6096 0x004534C0) and
    /// <c>SmartBox::DoVectorUpdate</c> (0x004521C0 / 0x00452200). Retail applies the player's own velocity only when its
    /// command interpreter allows it; the reducer records it and physics decides.
    /// </summary>
    public void OnVectorUpdate(in InboundContext context, in VectorUpdateMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Vector, context, message, message.Guid))
        {
            return;
        }

        _state.Objects.TryGet(message.Guid, out var target);
        if (!Admit(ObjectMessageGate.Check(target, message.InstanceSeq), ParkedKind.Vector, message.Guid, context, message))
        {
            return;
        }

        if (!DoVector(target!, message.Velocity, message.Omega, message.VectorSeq))
        {
            ReportDrop(MessageOpcodes.VectorUpdate, message.Guid, DropReason.Stale);
            return;
        }

        SyncSelf(target!);
        Changed();
        Publish(new ObjectVectorChanged(message.Guid, message.Velocity, message.Omega));
    }

    /// <summary>
    /// F74B SetState: <c>SmartBox::HandleSetState</c> (11.4186 0x004533E0, 11.6096 0x00453420) and
    /// <c>SmartBox::DoSetState</c> (0x004520D0 / 0x00452110). A state without Hidden ends the player's arrival
    /// (<see cref="SelfTravel.Present"/>), as it clears retail's <c>waiting_for_teleport</c>.
    /// </summary>
    public void OnSetState(in InboundContext context, in SetStateMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.State, context, message, message.Guid))
        {
            return;
        }

        _state.Objects.TryGet(message.Guid, out var target);
        if (!Admit(ObjectMessageGate.Check(target, message.InstanceSeq), ParkedKind.State, message.Guid, context, message))
        {
            return;
        }

        if (!DoSetState(target!, message.State, message.StateSeq, out var previous))
        {
            ReportDrop(MessageOpcodes.SetState, message.Guid, DropReason.Stale);
            return;
        }

        SyncSelf(target!);
        var travel = StateTravel(target!);
        Changed();
        if (previous != message.State)
        {
            Publish(new ObjectStateChanged(message.Guid, target!.IsSelf, previous, message.State));
        }

        PublishTravel(travel);
    }

    /// <summary>
    /// F751 PlayerTeleport: <c>SmartBox::HandlePlayerTeleport</c> (11.4186 0x00452150, 11.6096 0x00452190). Accepted when
    /// its sequence is not older than the player's teleport sequence, which it does not store (the position that follows
    /// advances it). Without a player object yet it does nothing, as in retail.
    /// </summary>
    public void OnPlayerTeleport(in InboundContext context, in PlayerTeleportMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Teleport, context, message, 0))
        {
            return;
        }

        if (!_state.Objects.TryGet(_state.Self.Guid, out var player))
        {
            _log.Debug(SessionLog.Category, "a PlayerTeleport before the player's CreateObject does nothing (SmartBox::HandlePlayerTeleport)");
            return;
        }

        if (!Sequence16.NotOlder(player.Stamps.Teleport, message.TeleportSeq))
        {
            ReportDrop(MessageOpcodes.PlayerTeleport, player.Guid, DropReason.Stale);
            return;
        }

        var travel = Travel(SelfTravel.TeleportAnnounced);
        Changed();
        Publish(new PlayerTeleportAnnounced(message.TeleportSeq));
        PublishTravel(travel);
    }

    /// <summary>
    /// F749 ParentEvent: <c>SmartBox::HandleParentEvent</c> (11.4186 0x004535D0, 11.6096 0x00453610;
    /// <see cref="ObjectMessageGate.CheckParent"/>) and <c>SmartBox::DoParentEvent</c> (0x00452290 / 0x004522D0): the
    /// child's position sequence decides; the child then has a parent and no position.
    /// </summary>
    public void OnParentEvent(in InboundContext context, in ParentEventMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Parent, context, message, message.ChildGuid))
        {
            return;
        }

        _state.Objects.TryGet(message.ParentGuid, out var parent);
        _state.Objects.TryGet(message.ChildGuid, out var child);
        switch (ObjectMessageGate.CheckParent(message.ParentGuid, parent, message.ChildGuid, child, message.ParentInstanceSeq, out var parkUnder))
        {
            case ObjectGateVerdict.Park:
                Park(parkUnder, message.ChildGuid, ParkedKind.Parent, context, message);
                return;
            case ObjectGateVerdict.Drop:
                ReportDrop(MessageOpcodes.ParentEvent, message.ChildGuid, DropReason.OlderInstance);
                return;
        }

        if (!DoParent(child!, parent!, message.ParentLocation, message.Placement, message.ChildPositionSeq))
        {
            ReportDrop(MessageOpcodes.ParentEvent, message.ChildGuid, DropReason.Stale);
            return;
        }

        SyncSelf(child!);
        Changed();
        Publish(new ObjectParented(message.ChildGuid, message.ParentGuid, message.ParentLocation, message.Placement));
    }

    /// <summary>
    /// F74A PickupEvent: <c>SmartBox::HandlePickupEvent</c> (11.4186 0x00453530, 11.6096 0x00453570) and
    /// <c>SmartBox::DoPickupEvent</c> (0x00452240 / 0x00452280): on a newer position sequence the object loses its parent
    /// and its position and leaves the world.
    /// </summary>
    public void OnPickupEvent(in InboundContext context, in PickupEventMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Pickup, context, message, message.Guid))
        {
            return;
        }

        _state.Objects.TryGet(message.Guid, out var target);
        if (!Admit(ObjectMessageGate.Check(target, message.InstanceSeq), ParkedKind.Pickup, message.Guid, context, message))
        {
            return;
        }

        if (!DoPickup(target!, message.PositionSeq))
        {
            ReportDrop(MessageOpcodes.PickupEvent, message.Guid, DropReason.Stale);
            return;
        }

        SyncSelf(target!);
        Changed();
        Publish(new ObjectPickedUp(message.Guid));
    }

    /// <summary>
    /// F625 ObjDescEvent: <c>SmartBox::HandleObjDescEvent</c> (11.4186 0x00453340, 11.6096 0x00453380) and
    /// <c>SmartBox::UpdateVisualDesc</c> (0x00451F40 / 0x00451F80).
    /// </summary>
    public void OnObjDescEvent(in InboundContext context, ObjDescEventMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (HoldUntilPlayer(ParkedKind.ObjDesc, context, message, message.Guid))
        {
            return;
        }

        _state.Objects.TryGet(message.Guid, out var target);
        if (!Admit(ObjectMessageGate.Check(target, message.InstanceSeq), ParkedKind.ObjDesc, message.Guid, context, message))
        {
            return;
        }

        var stamps = target!.Stamps;
        if (!ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.ObjDesc, message.ObjDescSeq))
        {
            ReportDrop(MessageOpcodes.ObjDescEvent, message.Guid, DropReason.Stale);
            return;
        }

        target.Stamps = stamps;
        target.Model = message.Model;
        SyncSelf(target);
        Changed();
        Publish(new ObjectDescChanged(message.Guid, message.Model));
    }

    /// <summary>Environ options already logged (an option logs once).</summary>
    private readonly HashSet<uint> _environsLogged = [];

    /// <summary>
    /// GM 0xEA60 AdminEnvirons: <c>CM_Admin::DispatchUI_Environs</c> (11.6096 0x006AE870; 11.4186 0x006AD910) then
    /// <c>CPlayerSystem::Handle_Admin__Environs</c> (11.6096 0x0055EB40; 11.4186 0x0055DE20), classified by
    /// <see cref="AdminEnvirons.Classify"/>. The sound options publish <see cref="AdminEnvironChanged"/> for the host to play
    /// from the UI sound table, only while the player's object exists (retail checks <c>GetPhysicsObject(playerID)</c> first). The
    /// fog options (0 to 6, 9999) publish it too; applying the fog is out of M3's scope (<c>TODO(m3-environs-fog)</c>, logged
    /// once per option). An option retail ignores is logged once and publishes nothing. No state changes.
    /// </summary>
    public void OnAdminEnvirons(in InboundContext context, in AdminEnvironsMsg message)
    {
        var option = message.Environ;
        var effect = AdminEnvirons.Classify(option);
        if (effect.Kind == EnvironKind.Ignored)
        {
            LogEnvironOnce(option, "GM 0xEA60 AdminEnvirons: option " + option + " is one retail's handler ignores; nothing happens");
            return;
        }

        if (effect.Kind == EnvironKind.Sound && !_state.Objects.Contains(_state.Self.Guid))
        {
            // Retail: no physics object for the player yet, so the handler returns before the sound.
            LogEnvironOnce(option, "GM 0xEA60 AdminEnvirons: sound option " + option + " arrived before the player's object exists; retail plays nothing");
            return;
        }

        if (effect.Kind is EnvironKind.Fog or EnvironKind.Clear)
        {
            LogEnvironOnce(option, "GM 0xEA60 AdminEnvirons: option " + option + " sets the landscape fog and ambient override (TODO(m3-environs-fog): not drawn; the event is published)");
        }

        Publish(new AdminEnvironChanged(option, effect.Kind, effect.SoundType));
    }

    private void LogEnvironOnce(uint option, string what)
    {
        if (_environsLogged.Add(option))
        {
            _log.Info(SessionLog.Category, what, new LogField("environ", option));
        }
    }

    /// <summary>
    /// F750 Sound: <c>SmartBox::HandleSoundEvent</c> (11.4186 0x00451FC0, 11.6096 0x00452000): parked for an unknown object,
    /// else <see cref="ObjectSoundPlayed"/>.
    /// </summary>
    public void OnSound(in InboundContext context, in SoundMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Sound, context, message, message.Guid))
        {
            return;
        }

        if (!_state.Objects.Contains(message.Guid))
        {
            Park(message.Guid, message.Guid, ParkedKind.Sound, context, message);
            return;
        }

        Publish(new ObjectSoundPlayed(message.Guid, message.SoundId, message.Volume));
    }

    /// <summary>
    /// F755 PlayEffect: <c>SmartBox::HandlePlayScriptType</c> (11.4186 0x00452070, 11.6096 0x004520B0): parked for an
    /// unknown object, else <see cref="ObjectScriptPlayed"/>.
    /// </summary>
    public void OnPlayEffect(in InboundContext context, in PlayEffectMsg message)
    {
        if (HoldUntilPlayer(ParkedKind.Script, context, message, message.Guid))
        {
            return;
        }

        if (!_state.Objects.Contains(message.Guid))
        {
            Park(message.Guid, message.Guid, ParkedKind.Script, context, message);
            return;
        }

        Publish(new ObjectScriptPlayed(message.Guid, message.ScriptId, message.Speed));
    }

    /// <summary>
    /// <c>SmartBox::HandleReceivedPosition</c> (11.4186 0x00453FD0, 11.6096 0x00454070), with <c>CPhysicsObj::MoveOrTeleport</c>'s
    /// teleport test for other objects (0x00516330 / 0x00516E30). For the player, a newer force-position sequence (which is
    /// stored at once) with a teleport sequence that is not older is a <see cref="PositionVerdict.ForceBlip"/>: the position
    /// sequence is set to the update's outright. Otherwise a newer position sequence (stored) is needed: with an older
    /// teleport sequence the position sequence is put back and the update dropped (<see cref="DropReason.StaleTeleport"/>);
    /// else the object loses its parent and takes the position, and the teleport sequence's <c>newer_event</c> decides
    /// between <see cref="PositionVerdict.SelfTeleport"/> and <see cref="PositionVerdict.SelfEcho"/> for the player, and
    /// gives <see cref="ObjectMoved.TeleportAdvanced"/> for <see cref="PositionVerdict.Remote"/>. A position sequence that
    /// is not newer drops the update (<see cref="DropReason.Stale"/>), and with a newer teleport sequence raises retail's
    /// error count.
    /// </summary>
    /// <returns>True when the update applied (<paramref name="moved"/> set); false when dropped (<paramref name="reason"/> set).</returns>
    private bool ReceivePosition(
        WorldObject target,
        in CellFrame position,
        uint? placement,
        bool hasContact,
        Vector3? velocity,
        ushort positionStamp,
        ushort teleportStamp,
        ushort forcePositionStamp,
        out ObjectMoved moved,
        out DropReason reason)
    {
        var stamps = target.Stamps;
        var isPlayer = target.IsSelf;
        if (isPlayer &&
            ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.ForcePosition, forcePositionStamp) &&
            Sequence16.NotOlder(stamps.Teleport, teleportStamp))
        {
            target.Stamps = stamps with { Position = positionStamp };
            target.Position = position;
            Counters.ForceBlips++;
            moved = new ObjectMoved(target.Guid, position, PositionVerdict.ForceBlip, false, hasContact, velocity, placement);
            reason = default;
            return true;
        }

        var previousPositionStamp = stamps.Position;
        if (!ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Position, positionStamp))
        {
            target.Stamps = stamps;
            if (Sequence16.IsNewer(stamps.Teleport, teleportStamp))
            {
                Counters.RetailErrorCount++;
            }

            moved = default;
            reason = DropReason.Stale;
            return false;
        }

        if (Sequence16.IsNewer(teleportStamp, stamps.Teleport))
        {
            target.Stamps = stamps with { Position = previousPositionStamp };
            moved = default;
            reason = DropReason.StaleTeleport;
            return false;
        }

        target.Parent = null;
        target.Position = position;
        var teleportAdvanced = ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Teleport, teleportStamp);
        target.Stamps = stamps;
        var verdict = isPlayer
            ? teleportAdvanced ? PositionVerdict.SelfTeleport : PositionVerdict.SelfEcho
            : PositionVerdict.Remote;
        moved = new ObjectMoved(target.Guid, position, verdict, teleportAdvanced, hasContact, velocity, placement);
        reason = default;
        return true;
    }

    /// <summary>
    /// <c>CPhysics::SetObjectMovement</c> (11.4186 0x00509690, 11.6096 0x0050A160): a newer movement sequence is stored;
    /// then an older server-control sequence leaves the motion as it was (the movement sequence stays advanced), else the
    /// server-control sequence is stored and the motion taken.
    /// </summary>
    /// <returns>True when the motion was taken.</returns>
    private static bool SetMovement(WorldObject target, ServerMotionState motion, ushort movementStamp, ushort serverControlStamp, bool autonomous)
    {
        var stamps = target.Stamps;
        if (!ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Movement, movementStamp))
        {
            return false;
        }

        if (Sequence16.IsNewer(serverControlStamp, stamps.ServerControl))
        {
            target.Stamps = stamps;
            return false;
        }

        target.Stamps = stamps with { ServerControl = serverControlStamp };
        target.Motion = motion;
        target.MotionAutonomous = autonomous;
        return true;
    }

    /// <summary><c>SmartBox::DoVectorUpdate</c> (11.4186 0x004521C0, 11.6096 0x00452200).</summary>
    private static bool DoVector(WorldObject target, Vector3 velocity, Vector3 omega, ushort vectorStamp)
    {
        var stamps = target.Stamps;
        if (!ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Vector, vectorStamp))
        {
            return false;
        }

        target.Stamps = stamps;
        target.Velocity = velocity;
        target.Omega = omega;
        return true;
    }

    /// <summary><c>SmartBox::DoSetState</c> (11.4186 0x004520D0, 11.6096 0x00452110).</summary>
    private static bool DoSetState(WorldObject target, PhysicsState state, ushort stateStamp, out PhysicsState previous)
    {
        previous = target.State;
        var stamps = target.Stamps;
        if (!ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.State, stateStamp))
        {
            return false;
        }

        target.Stamps = stamps;
        target.State = state;
        return true;
    }

    /// <summary><c>SmartBox::DoPickupEvent</c> (11.4186 0x00452240, 11.6096 0x00452280).</summary>
    private static bool DoPickup(WorldObject target, ushort positionStamp)
    {
        var stamps = target.Stamps;
        if (!ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Position, positionStamp))
        {
            return false;
        }

        target.Stamps = stamps;
        target.Parent = null;
        target.Position = null;
        return true;
    }

    /// <summary><c>SmartBox::DoParentEvent</c> (11.4186 0x00452290, 11.6096 0x004522D0): the child's position sequence decides.</summary>
    private static bool DoParent(WorldObject child, WorldObject parent, int location, int placement, ushort childPositionStamp)
    {
        var stamps = child.Stamps;
        if (!ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Position, childPositionStamp))
        {
            return false;
        }

        child.Stamps = stamps;
        child.Parent = new ObjectAttachment(parent.Guid, location, placement);
        child.Position = null;
        return true;
    }

    /// <summary>A new object, or a re-created one (the create half of <c>SmartBox::HandleCreateObject</c>).</summary>
    private void CreateNew(CreateObjectMsg message, WorldObject? parent, bool recreated)
    {
        var physics = message.Physics;
        var isSelf = message.Guid == _state.Self.Guid;
        var created = new WorldObject(message, isSelf, _clock.Now)
        {
            State = physics.State,
            Stamps = physics.Timestamps,
            Velocity = physics.Velocity ?? Vector3.Zero,
            Omega = physics.Omega ?? Vector3.Zero,
            Motion = physics.Movement,
            MotionAutonomous = physics.MovementIsAutonomous,
        };
        if (parent is not null)
        {
            created.Parent = new ObjectAttachment(parent.Guid, (int)physics.Parent!.Value.Location, (int)(physics.AnimationFrame ?? 0));
        }
        else if (physics.Position is { CellId: not 0 } p)
        {
            created.Position = ToFrame(p);
        }

        _state.Objects.Set(created);
        SelfTravelChanged? travel = null;
        if (isSelf)
        {
            _state.Self.Name = message.Weenie.Name;
            SyncSelf(created);

            // Entering the world: the player arrives hidden and waits for LoginComplete. A re-created player that the
            // server shows (an F7DB in the world) keeps its travel state.
            var keep = recreated && (physics.State & PhysicsState.Hidden) == 0 && _state.Self.Travel == SelfTravel.Present;
            if (created.Position is not null && !keep)
            {
                travel = Travel(SelfTravel.Arriving);
            }
        }

        Changed();
        Publish(new ObjectCreated(message.Guid, isSelf, created.InWorld, recreated));
        // Side-pack contents at login: the 0196 ViewContents before the F745s names them (M4 2.1 s1, GameStateReducer.Containers.cs); this adoption is the tolerance for an item no 0196 named (M3 1.2 s2). ACE sends no 0022 for them, only each item's CreateObject. Only during the arrival
        // (before the player is Present): a live grant (a pick-up, a loot, a gear kit into a full main pack's side pack) sends the CreateObject and then the item's own 0022, which the model applies and
        // publishes once as Added at the real slot; adopting at the CreateObject would publish Added and then Moved (M3 1.2 fix R3). A main-pack item is held already by the description. The wielded arm
        // (the item's WielderId is the player: no container) is reachable from here for the same arrival objects.
        if (_state.Self.Travel != SelfTravel.Present && ((created.ContainerId is { } holder && holder != _state.Self.Guid) || (created.WielderId is { } wielder && wielder == _state.Self.Guid)))
        {
            AdoptHeldObject(created);
        }
        if (created.Motion is { } motion)
        {
            Publish(new ObjectMotionChanged(message.Guid, isSelf, created.MotionAutonomous, motion));
        }

        PublishTravel(travel);
        ReplayParked(message.Guid);
    }

    /// <summary>The same instance again: the refresh half of <c>SmartBox::HandleCreateObject</c>, in retail's order.</summary>
    private void UpdateExisting(WorldObject target, CreateObjectMsg message, WorldObject? parent)
    {
        var physics = message.Physics;
        var ts = physics.Timestamps;
        var guid = message.Guid;
        var isSelf = target.IsSelf;

        // Visual description (SmartBox::UpdateVisualDesc), then the descriptions the message carries.
        var changes = ObjectChanges.None;
        var stamps = target.Stamps;
        if (ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.ObjDesc, ts.ObjDesc))
        {
            target.Stamps = stamps;
            target.Model = message.Model;
            changes |= ObjectChanges.Model;
        }

        if (DescribedDifferently(target.Created.Physics, physics))
        {
            changes |= ObjectChanges.Physics;
        }

        if (!Equals(target.Created.Weenie, message.Weenie))
        {
            changes |= ObjectChanges.Weenie;
        }

        target.Created = message;

        // Parent, position or pickup.
        var parented = false;
        var moved = default(ObjectMoved);
        var hasMoved = false;
        var pickedUp = false;
        var placement = (int)(physics.AnimationFrame ?? 0);
        if (parent is not null)
        {
            parented = DoParent(target, parent, (int)physics.Parent!.Value.Location, placement, ts.Position);
        }
        else if (physics.Position is { CellId: not 0 } p)
        {
            hasMoved = ReceivePosition(target, ToFrame(p), physics.AnimationFrame, true, physics.Velocity, ts.Position, ts.Teleport, ts.ForcePosition, out moved, out _);
        }
        else
        {
            pickedUp = DoPickup(target, ts.Position);
        }

        var positionTravel = hasMoved && moved.Verdict == PositionVerdict.SelfTeleport ? Travel(SelfTravel.TeleportArrived) : null;

        // Movement, state, vectors.
        var motionTaken = physics.Movement is { } motion && SetMovement(target, motion, ts.Movement, ts.ServerControl, physics.MovementIsAutonomous);
        var stateApplied = DoSetState(target, physics.State, ts.State, out var previousState);
        var stateTravel = stateApplied ? StateTravel(target) : null;
        var vectorApplied = DoVector(target, physics.Velocity ?? Vector3.Zero, physics.Omega ?? Vector3.Zero, ts.Vector);
        SyncSelf(target);
        Changed();

        Publish(new ObjectUpdated(guid, changes));
        if (parented)
        {
            Publish(new ObjectParented(guid, parent!.Guid, (int)physics.Parent!.Value.Location, placement));
        }
        else if (hasMoved)
        {
            Publish(in moved);
        }
        else if (pickedUp)
        {
            Publish(new ObjectPickedUp(guid));
        }

        PublishTravel(positionTravel);
        if (motionTaken)
        {
            Publish(new ObjectMotionChanged(guid, isSelf, target.MotionAutonomous, target.Motion!));
        }

        if (stateApplied && previousState != target.State)
        {
            Publish(new ObjectStateChanged(guid, isSelf, previousState, target.State));
        }

        PublishTravel(stateTravel);
        if (vectorApplied)
        {
            Publish(new ObjectVectorChanged(guid, target.Velocity, target.Omega));
        }
    }

    /// <summary>LoginComplete was sent (a local notice): an arrival becomes <see cref="SelfTravel.Materialising"/>.</summary>
    private void LoginCompleteSent()
    {
        var travel = _state.Self.Travel;
        if (travel is not (SelfTravel.Arriving or SelfTravel.TeleportArrived))
        {
            _log.Warn(
                SessionLog.Category,
                "LoginComplete was sent while the player was not arriving; the travel state is unchanged",
                new LogField("travel", travel));
            return;
        }

        var change = Travel(SelfTravel.Materialising);
        Changed();
        PublishTravel(change);
    }

    /// <summary>
    /// The world is gone (the server's F653 or the network session's logoff event, or a reset): every object, the
    /// player's own included, gets <see cref="ObjectDeleted"/> (the table is cleared with it, and a consumer that keeps the
    /// player's object, the avatar set, would otherwise keep a dead player beside the next character's), the player
    /// <see cref="SelfTravel.NotInWorld"/>, and the player's identity, stamps and qualities are forgotten.
    /// </summary>
    private void LeaveWorld()
    {
        CloseConfirmationsOnWorldExit(); // M5 6 w2: no server question outlives the character (GameStateReducer.Confirmations.cs)
        Cull.Clear(); // CObjectMaint::DestroyObjects (11.6096 0x00509700): the log off clears the cull's queue with the table
        if (_attack.State != AttackSequenceState.Idle || _attack.Number != 0)
        {
            // A sequence left Active by a log off (or a lost final 01A7 before it) must not carry into the next character (review 1.2-R4).
            _attack.Reset();
            Publish(_attack.Snapshot);
        }

        var self = _state.Self;
        var objects = _state.Objects;
        if (self.Guid == 0 && objects.Count == 0 && self.Travel == SelfTravel.NotInWorld && _parked.Count == 0)
        {
            return;
        }

        _guidScratch.Clear();
        foreach (var o in objects.All)
        {
            _guidScratch.Add(o.Guid);
        }

        var deleted = _guidScratch.ToArray();
        objects.Clear();
        _parked.Clear();
        var travel = Travel(SelfTravel.NotInWorld);
        self.Guid = 0;
        self.Name = null;
        self.Position = null;
        self.Stamps = default;
        self.PhysicsState = PhysicsState.None;
        self.Qualities.Clear();
        self.Spellbook.Clear();
        self.Inventory.Clear();
        _dequipZeroed.Clear(); // M5 7 D139 (l)
        self.Shortcuts.Clear();
        var closedGround = _state.Containers.ViewedGuid;
        var closedVendor = DropVendorForLeave();
        _state.Containers.Clear();
        self.Qualities.ClearSessionOptions();
        _qualityStamps.Clear();
        Changed();
        foreach (var guid in deleted)
        {
            Publish(new ObjectDeleted(guid));
        }

        if (closedGround != 0)
        {
            Publish(new ContainerClosed(closedGround));
        }

        if (closedVendor != 0)
        {
            Publish(new VendorClosed(closedVendor));
        }

        PublishTravel(travel);
    }

    /// <summary>
    /// <c>ACSmartBox::IsReadyToDispatchEvent</c>: until F746 names the player, SmartBox messages wait. True when the message
    /// was held (boxed only then).
    /// </summary>
    private bool HoldUntilPlayer<T>(ParkedKind kind, in InboundContext context, in T message, uint objectGuid, bool isUpdate = false)
        where T : notnull
    {
        if (_state.Self.Guid != 0)
        {
            return false;
        }

        Park(PendingObjectMessages.WaitingForPlayer, objectGuid, kind, context, message, isUpdate);
        return true;
    }

    /// <summary>Acts on the instance gate's verdict: true for apply; a park or a drop is carried out here.</summary>
    private bool Admit<T>(ObjectGateVerdict verdict, ParkedKind kind, uint guid, in InboundContext context, in T message)
        where T : notnull
    {
        switch (verdict)
        {
            case ObjectGateVerdict.Apply:
                Cull.Seen(guid); // any applied message for a pending object cancels its cull (RemoveObjectToBeDestroyed)
                return true;
            case ObjectGateVerdict.Park:
                Park(guid, guid, kind, context, message);
                return false;
            default:
                ReportDrop(ParkedMessage.OpcodeOf(kind, isUpdate: false), guid, DropReason.OlderInstance);
                return false;
        }
    }

    /// <summary>Parks a message under <paramref name="key"/> (boxing a struct message: parking is the rare path).</summary>
    private void Park<T>(uint key, uint objectGuid, ParkedKind kind, in InboundContext context, in T message, bool isUpdate = false)
        where T : notnull
    {
        ExpireParked();
        var entry = new ParkedMessage(kind, key, objectGuid, _replayParkedAt ?? _clock.Now, context, message, isUpdate);
        if (_parked.TryPark(entry) == ParkOutcome.Parked)
        {
            Counters.Parked++;
            if (_log.IsEnabled(LogSeverity.Debug))
            {
                _log.Debug(SessionLog.Category, "message parked", new LogField("opcode", entry.Opcode), new LogField("guid", objectGuid), new LogField("under", key));
            }

            return;
        }

        Counters.ParkingFull++;
        ReportDrop(entry.Opcode, objectGuid, DropReason.ParkingFull);
    }

    /// <summary>
    /// <c>SmartBox::ProcessObjectNetBlobs</c> (and <c>ProcessNetBlobs</c> for the messages held for the player): takes every
    /// message parked under <paramref name="key"/> and replays it, in arrival order, through its handler.
    /// </summary>
    private void ReplayParked(uint key)
    {
        var count = _parked.CountFor(key);
        if (count == 0)
        {
            return;
        }

        var batch = new List<ParkedMessage>(count);
        _parked.TakeFor(key, batch);
        var outer = _replayParkedAt;
        try
        {
            foreach (var entry in batch)
            {
                Counters.Replayed++;
                _replayParkedAt = entry.ParkedAt;
                Replay(entry);
            }
        }
        finally
        {
            _replayParkedAt = outer;
        }
    }

    private void Replay(in ParkedMessage entry)
    {
        var context = entry.Context;
        switch (entry.Kind)
        {
            case ParkedKind.Create:
                OnCreateObject(context, (CreateObjectMsg)entry.Message, entry.IsUpdate);
                break;
            case ParkedKind.Delete:
                OnDeleteObject(context, (DeleteObjectMsg)entry.Message);
                break;
            case ParkedKind.Position:
                OnUpdatePosition(context, (UpdatePositionMsg)entry.Message);
                break;
            case ParkedKind.Motion:
                OnUpdateMotion(context, (UpdateMotionMsg)entry.Message);
                break;
            case ParkedKind.Vector:
                OnVectorUpdate(context, (VectorUpdateMsg)entry.Message);
                break;
            case ParkedKind.State:
                OnSetState(context, (SetStateMsg)entry.Message);
                break;
            case ParkedKind.Parent:
                OnParentEvent(context, (ParentEventMsg)entry.Message);
                break;
            case ParkedKind.Pickup:
                OnPickupEvent(context, (PickupEventMsg)entry.Message);
                break;
            case ParkedKind.ObjDesc:
                OnObjDescEvent(context, (ObjDescEventMsg)entry.Message);
                break;
            case ParkedKind.Sound:
                OnSound(context, (SoundMsg)entry.Message);
                break;
            case ParkedKind.Script:
                OnPlayEffect(context, (PlayEffectMsg)entry.Message);
                break;
            case ParkedKind.Teleport:
                OnPlayerTeleport(context, (PlayerTeleportMsg)entry.Message);
                break;
            default:
                throw new InvalidOperationException("TODO(state): a parked message of kind " + entry.Kind + " has no replay");
        }
    }

    /// <summary>A SetState that left the player without Hidden ends an arrival or a teleport (<see cref="SelfTravel.Present"/>).</summary>
    private SelfTravelChanged? StateTravel(WorldObject target)
    {
        if (!target.IsSelf || (target.State & PhysicsState.Hidden) != 0)
        {
            return null;
        }

        return _state.Self.Travel is SelfTravel.Arriving or SelfTravel.TeleportAnnounced or SelfTravel.TeleportArrived or SelfTravel.Materialising
            ? Travel(SelfTravel.Present)
            : null;
    }

    /// <summary>Moves the player's travel state; the change to publish, or null when it was already there.</summary>
    private SelfTravelChanged? Travel(SelfTravel to)
    {
        var self = _state.Self;
        var from = self.Travel;
        if (from == to)
        {
            return null;
        }

        self.Travel = to;
        return new SelfTravelChanged(from, to);
    }

    private void PublishTravel(SelfTravelChanged? change)
    {
        if (change is { } travel)
        {
            Publish(in travel);
        }
    }

    /// <summary>Mirrors the player's object into <see cref="SelfState"/> (its stamps, state and last server position).</summary>
    private void SyncSelf(WorldObject target)
    {
        if (!target.IsSelf)
        {
            return;
        }

        var self = _state.Self;
        self.Stamps = target.Stamps;
        self.PhysicsState = target.State;
        if (target.Position is { } position)
        {
            self.Position = position;
        }
    }

    private static CellFrame ToFrame(in AcPosition p) => new(p.CellId, new AcFrame(p.Origin, p.Rotation));

    /// <summary>True when the physics descriptions differ in what <see cref="ObjectChanges.Physics"/> stands for.</summary>
    private static bool DescribedDifferently(PhysicsDesc a, PhysicsDesc b) =>
        a.SetupId != b.SetupId || a.MotionTableId != b.MotionTableId || a.SoundTableId != b.SoundTableId ||
        a.PhysicsEffectTableId != b.PhysicsEffectTableId || a.Scale != b.Scale || a.Translucency != b.Translucency ||
        a.Friction != b.Friction || a.Elasticity != b.Elasticity || a.DefaultScript != b.DefaultScript ||
        a.DefaultScriptIntensity != b.DefaultScriptIntensity;
}

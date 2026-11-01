using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Appearance;
using Lifestone.Data.Motion;
using Lifestone.Net.Types;
using DataAnimPartChange = Lifestone.Data.Appearance.AnimPartChange;
using DatPlayScript = DatReaderWriter.Enums.PlayScript;

namespace Lifestone.Client.Avatars;

/// <summary>
/// Maps the session's wire records (the net track's types, as the state track's events and <see cref="WorldObject"/>
/// carry them) to the avatars' Lifestone.Data types, field for field. The G5 session adapter (AV13) and the avatar set
/// use it.
/// </summary>
public static class AvatarWire
{
    /// <summary>The weenie item type of creatures (ItemType 0x10).</summary>
    public const uint CreatureItemType = 0x10;

    /// <summary>
    /// Retail <c>ObjDesc::UnPack</c> of a decoded ModelData (<see cref="ObjDesc.FromWire"/>): the base palette (kept only
    /// with sub-palettes), the sub-palette, texture and part swaps in wire order.
    /// </summary>
    public static ObjDesc ToObjDesc(ModelData model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.SubPalettes.IsEmpty && model.TextureChanges.IsEmpty && model.AnimPartChanges.IsEmpty)
        {
            return ObjDesc.Empty;
        }

        Span<WireSubPalette> subs = model.SubPalettes.Length <= 64 ? stackalloc WireSubPalette[model.SubPalettes.Length] : new WireSubPalette[model.SubPalettes.Length];
        for (var i = 0; i < subs.Length; i++)
        {
            var s = model.SubPalettes[i];
            subs[i] = new WireSubPalette(s.PaletteId, s.Offset, s.Length);
        }

        var textures = new TextureMapChange[model.TextureChanges.Length];
        for (var i = 0; i < textures.Length; i++)
        {
            var t = model.TextureChanges[i];
            textures[i] = new TextureMapChange(t.PartIndex, t.OldTexture, t.NewTexture);
        }

        var parts = new DataAnimPartChange[model.AnimPartChanges.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var p = model.AnimPartChanges[i];
            parts[i] = new DataAnimPartChange(p.PartIndex, p.GfxObjId);
        }

        return ObjDesc.FromWire(model.BasePaletteId ?? 0, subs, textures, parts);
    }

    /// <summary>
    /// A server MovementData as <see cref="WireMovement"/>: the type, the stance, the two flags and the body of the
    /// type. The queued actions are copied separately (<see cref="CopyActions"/>), so the per-message path allocates
    /// nothing.
    /// </summary>
    public static WireMovement ToMovement(ServerMotionState motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        WireMoveTo? moveTo = motion.MoveTo is { } m
            ? new WireMoveTo(
                m.TargetGuid ?? 0,
                WireMovementPositions.Origin(m.OriginCellId, m.Origin),
                MovementParameters.FromMoveToWire(
                    m.Parameters.Flags,
                    m.Parameters.DistanceToObject,
                    m.Parameters.MinDistance,
                    m.Parameters.FailDistance,
                    m.Parameters.Speed,
                    m.Parameters.WalkRunThreshold,
                    m.Parameters.DesiredHeading),
                m.RunRate)
            : null;
        WireTurnTo? turnTo = motion.TurnTo is { } t
            ? new WireTurnTo(
                t.TargetGuid ?? 0,
                t.ObjectHeading ?? 0f,
                MovementParameters.FromTurnToWire(t.Parameters.Flags, t.Parameters.Speed, t.Parameters.DesiredHeading))
            : null;
        return new WireMovement(
            (WireMovementType)(byte)motion.MovementType,
            motion.Stance,
            (motion.MotionFlags & MotionFlags.StickToObject) != 0,
            (motion.MotionFlags & MotionFlags.StandingLongJump) != 0,
            new WireInterpretedMotion(
                motion.Style,
                motion.Forward,
                motion.Sidestep,
                motion.Turn,
                motion.ForwardSpeed,
                motion.SidestepSpeed,
                motion.TurnSpeed),
            motion.StickyGuid ?? 0,
            moveTo,
            turnTo);
    }

    /// <summary>
    /// Copies the queued actions of <paramref name="motion"/> into <paramref name="into"/>; returns how many there are.
    /// When <paramref name="into"/> is too short nothing is copied and the needed length is returned.
    /// </summary>
    public static int CopyActions(ServerMotionState motion, Span<WireMotionItem> into)
    {
        ArgumentNullException.ThrowIfNull(motion);
        var commands = motion.Commands;
        if (commands.Length > into.Length)
        {
            return commands.Length;
        }

        for (var i = 0; i < commands.Length; i++)
        {
            var c = commands[i];
            into[i] = new WireMotionItem(c.Command, c.Stamp, c.Autonomous, c.Speed);
        }

        return commands.Length;
    }

    /// <summary>
    /// The avatar description of a session object: its CreateObject's physics description with its current visual
    /// description, state, position (with the last UpdatePosition's contact flag unknown here, so grounded), parent,
    /// vectors and motion.
    /// </summary>
    public static AvatarCreate FromWorldObject(WorldObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var physics = obj.Physics;
        WireMovement? movement = null;
        WireMotionItem[] actions = [];
        if (obj.Motion is { } motion)
        {
            movement = ToMovement(motion);
            actions = new WireMotionItem[motion.Commands.Length];
            CopyActions(motion, actions);
        }

        var children = new (uint, int)[physics.Children.Length];
        for (var i = 0; i < children.Length; i++)
        {
            children[i] = (physics.Children[i].Guid, (int)physics.Children[i].Location);
        }

        return new AvatarCreate
        {
            Guid = obj.Guid,
            Name = obj.Name,
            IsSelf = obj.IsSelf,
            IsCreature = (obj.Weenie.ItemType & CreatureItemType) != 0,
            DescriptionFlags = obj.DescriptionFlags,
            SetupId = physics.SetupId ?? 0,
            MotionTableId = physics.MotionTableId ?? 0,
            SoundTableId = physics.SoundTableId ?? 0,
            PhysicsScriptTableId = physics.PhysicsEffectTableId ?? 0,
            ObjDesc = ToObjDesc(obj.Model),
            PhysicsState = (uint)obj.State,
            Position = obj.Position,
            ParentGuid = obj.Parent?.ParentGuid ?? 0,
            ParentLocation = obj.Parent?.Location ?? 0,
            Placement = obj.Parent is { } parent ? (uint)parent.Placement : physics.AnimationFrame ?? 0,
            Children = children,
            Movement = movement,
            MovementActions = actions,
            MovementAutonomous = obj.MotionAutonomous,
            Scale = physics.Scale ?? 1f,
            Translucency = physics.Translucency ?? 0f,
            Velocity = obj.Velocity,
            Acceleration = physics.Acceleration ?? default,
            Omega = obj.Omega,
            DefaultScript = (DatPlayScript)(physics.DefaultScript ?? 0),
            DefaultScriptIntensity = physics.DefaultScriptIntensity ?? 0f,
        };
    }
}

using Lifestone.Client.Avatars;
using Lifestone.Data.Appearance;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// What a figure of a creature is made from (M3 2.2 b4, the paperdoll): the CreateObject's body and tables and the object's current appearance, as plain values the retail UI may take
/// (it may not name <c>Lifestone.Net</c> types).
/// </summary>
/// <param name="SetupId">The object's Setup (the physics description's CSetup), or 0 when the description sent none.</param>
/// <param name="MotionTableId">The object's motion table, or 0.</param>
/// <param name="ScriptTableId">The object's physics-effect (script) table, or 0.</param>
/// <param name="ObjDesc">The current visual description as an <see cref="ObjDesc"/> (the create's, or the last accepted ObjDescEvent's).</param>
/// <param name="Scale">The physics description's ObjScale, or 1 when it sent none.</param>
public readonly record struct FigureFields(uint SetupId, uint MotionTableId, uint ScriptTableId, ObjDesc ObjDesc, float Scale = 1f);

/// <summary>The figure fields of a <see cref="WorldObject"/> (additive; <see cref="WorldObject"/> itself is unchanged).</summary>
public static class WorldObjectFigure
{
    /// <summary>
    /// The Setup, tables and current appearance of <paramref name="obj"/> (<c>SmartBox::get_player_visualdesc</c>, 11.6096, is what the paperdoll's <c>RedressCreature</c> 0x004A3F30 reads). Converts the
    /// appearance on every call, so a caller asks when it changed, not per frame.
    /// </summary>
    public static FigureFields FigureFields(this WorldObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var physics = obj.Physics;
        return new FigureFields(physics.SetupId ?? 0, physics.MotionTableId ?? 0, physics.PhysicsEffectTableId ?? 0, AvatarWire.ToObjDesc(obj.Model), physics.Scale ?? 1f);
    }
}

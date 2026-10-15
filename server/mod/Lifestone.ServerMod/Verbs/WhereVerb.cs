// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/DeveloperCommands.cs HandleMyLoc (AGPL-3.0)
// with changes: the call sequence only (what @myloc reads: the landblock, Location and the physics position), at Player
// level for the requesting player, with the teleporting flag and the force-position and teleport sequences added, and
// one machine-readable reply formatted invariantly instead of three chat lines.
using ACE.Server.Network.Sequence;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// <c>@lifestone where</c>: where the server thinks the player is (<see cref="WhereData"/>), the check behind DoD 8 (the
/// server keeps accepting the client's moves: the position agrees and ForcePositionSeq does not change).
/// </summary>
/// <remarks>
/// It reads what ACE's <c>@myloc</c> reads (DeveloperCommands.cs:1767-1773): the player's Location (Position.cs:18-70:
/// the cell, the landblock-frame origin and the rotation), the current landblock's id and the physics object's cell
/// (Physics/Common/Position.cs:14). It adds WorldObject.Teleporting (WorldObject.cs:263) and the ObjectForcePosition
/// and ObjectTeleport sequences (SequenceType.cs:9-11) through <see cref="AceFacts.CurrentSequence"/>.
/// </remarks>
internal static class WhereVerb
{
    /// <summary>Answers a where request for <paramref name="player"/>, who is in a landblock.</summary>
    public static void Run(ToolCall call, Player player, WhereRequest request)
    {
        var location = player.Location ?? throw new InvalidOperationException("the player has no Location");
        var landblock = player.CurrentLandblock ?? throw new InvalidOperationException("the player has no landblock");
        var physics = player.PhysicsObj?.Position ?? throw new InvalidOperationException("the player has no physics position");
        var data = new WhereData(
            ToolsWire.FormatCell(location.Cell),
            location.PositionX,
            location.PositionY,
            location.PositionZ,
            location.RotationW,
            location.RotationX,
            location.RotationY,
            location.RotationZ,
            ToolsWire.FormatCell(PlaceRules.LandblockOf(landblock.Id.Raw)),
            ToolsWire.FormatCell(physics.ObjCellID),
            player.Teleporting,
            AceFacts.CurrentSequence(player, SequenceType.ObjectForcePosition),
            AceFacts.CurrentSequence(player, SequenceType.ObjectTeleport));
        call.Succeed(request, data, data.Describe());
    }
}

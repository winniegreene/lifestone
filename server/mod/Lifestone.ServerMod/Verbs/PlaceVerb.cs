// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/AdminCommands.cs HandleTeleportPoi,
// HandleTeleportLOC (AGPL-3.0)
// with changes: the call sequences only, at Player level for the requesting player; the point of interest is matched by
// its own name ignoring case (ordinal) instead of a culture lower-cased cache key; a missing portal weenie or
// destination answers 'failed' (@telepoi has no such guard); the location comes from the shared parser (invariant
// numbers, a validated cell, x and y and a unit rotation) instead of float.TryParse in the server's culture, and its
// cell must be in ACE's cell dat index; teleporting, logging out and dead players are refused as busy; one
// machine-readable reply.
using System.Globalization;
using ACE.Database;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;
using Position = ACE.Entity.Position;

namespace Lifestone.ServerMod;

/// <summary>
/// <c>@lifestone place poi "&lt;name&gt;"</c> and <c>@lifestone place loc &lt;cell&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; [&lt;qw&gt; &lt;qx&gt;
/// &lt;qy&gt; &lt;qz&gt;]</c>: teleport the player (<see cref="PlaceData"/>). A success means the teleport started; the client
/// sees it complete as ACE's teleport (F751) and position (F748) messages.
/// </summary>
/// <remarks>
/// <para><c>poi</c> follows ACE's <c>@telepoi</c> (AdminCommands.cs:875-909): the point of interest's portal weenie
/// (WorldDatabaseWithEntityCache.GetCachedWeenie), its Destination position (WeenieExtensions.cs:104-113), adjusted for
/// dungeons by WorldObject.AdjustDungeon (WorldObject.cs:738-742; it loads the destination landblock, as the teleport
/// would), then Player.Teleport (Player_Location.cs:654). The reply carries that adjusted destination; Teleport itself
/// lifts z by 0.005 times the player's scale (:658).</para>
/// <para><c>loc</c> follows ACE's <c>@teleloc</c> (AdminCommands.cs:912-962) with the exact location asked for and no
/// dungeon adjustment. The cell must be in ACE's cell dat (<see cref="AceFacts.CellExists"/>), else
/// <see cref="ToolErrors.InvalidCell"/>. The Position constructor takes the rotation as x, y, z, w (Position.cs:220).</para>
/// <para>Player.Teleport may wait a second and re-enter through WorldManager.ThreadSafeTeleport while it clears fog
/// (Player_Location.cs:666-676); the reply goes out at once either way.</para>
/// </remarks>
internal static class PlaceVerb
{
    /// <summary>Teleports <paramref name="player"/> to a point of interest.</summary>
    public static void Poi(ToolCall call, Player player, PlacePoiRequest request)
    {
        if (AceFacts.TeleportBusy(player) is { } busy)
        {
            call.Fail(ToolErrors.Busy, busy);
            return;
        }

        var poi = AceFacts.FindPointOfInterest(request.Name);
        if (poi is null)
        {
            call.Fail(ToolErrors.NotFound, "no place is called \"" + request.Name + "\"; @" + ToolsWire.Command + " " + ToolVerbs.Places + " lists them");
            return;
        }

        var weenie = DatabaseManager.World.GetCachedWeenie(poi.WeenieClassId);
        var destination = weenie?.GetPosition(PositionType.Destination);
        if (destination is null)
        {
            call.Fail(
                ToolErrors.Failed,
                "ACE's place \"" + poi.Name + "\" has no destination (portal weenie " + poi.WeenieClassId.ToString(CultureInfo.InvariantCulture)
                + (weenie is null ? " is missing" : " has no Destination position") + ")");
            return;
        }

        var target = new Position(destination);
        WorldObject.AdjustDungeon(target);
        player.Teleport(target);
        Started(call, request, ToolVerbs.PlacePoi, poi.Name, target);
    }

    /// <summary>Teleports <paramref name="player"/> to an exact location.</summary>
    public static void Loc(ToolCall call, Player player, PlaceLocRequest request)
    {
        if (AceFacts.TeleportBusy(player) is { } busy)
        {
            call.Fail(ToolErrors.Busy, busy);
            return;
        }

        var at = request.At;
        if (!AceFacts.CellExists(at.Cell))
        {
            call.Fail(ToolErrors.InvalidCell, "cell " + ToolsWire.FormatCell(at.Cell) + " is not in ACE's cell dat");
            return;
        }

        var target = new Position(at.Cell, at.X, at.Y, at.Z, at.Qx, at.Qy, at.Qz, at.Qw);
        player.Teleport(target);
        Started(call, request, ToolVerbs.PlaceLoc, null, target);
    }

    private static void Started(ToolCall call, ToolRequest request, string kind, string? name, Position target)
    {
        var data = new PlaceData(
            kind,
            name,
            ToolsWire.FormatCell(target.Cell),
            target.PositionX,
            target.PositionY,
            target.PositionZ,
            target.RotationW,
            target.RotationX,
            target.RotationY,
            target.RotationZ);
        call.Succeed(request, data, data.Describe());
    }
}

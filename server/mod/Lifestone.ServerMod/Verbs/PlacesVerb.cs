using System.Globalization;
using ACE.Database;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Mods;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// <c>@lifestone places</c>: ACE's points of interest, one entry per portal weenie with all its names and its raw
/// Destination position (<see cref="PlacesData"/>). The loopback tests compare it with the client's embedded extract
/// (Lifestone.Data AcePointsOfInterest: 56 destinations, 62 names); the tools panel lists the client's own copy.
/// </summary>
/// <remarks>
/// <para>It runs on a background task, not on ACE's world thread: with WorldDatabasePrecaching off (Lifestone's
/// Config.js), each of the 56 portal weenies may be a database query (WorldDatabaseWithEntityCache.GetCachedWeenie),
/// and the world must not stall for them. ACE's caches are concurrent, and the session's send queue takes messages from
/// any thread (NetworkSession.EnqueueSend locks per message group).</para>
/// <para>The destinations are raw, as the weenie stores them (WeenieExtensions.cs:104-113): no dungeon adjustment, which
/// would load 56 landblocks. The grouping, the order and the leaving out of a name whose weenie or Destination is
/// missing are <see cref="PlacesBuilder"/>'s (unit tested); each one left out is logged here as a warning and named in
/// the reply's message.</para>
/// </remarks>
internal static class PlacesVerb
{
    /// <summary>Starts building the list and answers from the background task.</summary>
    public static void Run(ToolCall call, PlacesRequest request) =>
        _ = Task.Run(() =>
        {
            try
            {
                Answer(call, request);
            }
            catch (Exception e)
            {
                call.Crashed(e);
            }
        });

    private static void Answer(ToolCall call, PlacesRequest request)
    {
        var build = PlacesBuilder.Build(
            AceFacts.PointsOfInterest().Select(p => new PoiRow(p.Name, p.WeenieClassId)),
            Destination);
        foreach (var skip in build.Skipped)
        {
            ModManager.Log(
                "Lifestone places: no Destination for portal weenie " + skip.Wcid.ToString(CultureInfo.InvariantCulture) + " (" + string.Join(", ", skip.Names) + "); left out",
                ModManager.LogLevel.Warn);
        }

        call.Succeed(request, build.Data, build.Sentence, build.Note);
    }

    /// <summary>The portal weenie's raw Destination position (WeenieExtensions.cs:104-113), or null when ACE has no weenie or no Destination.</summary>
    private static ToolLocation? Destination(uint wcid) =>
        DatabaseManager.World.GetCachedWeenie(wcid)?.GetPosition(PositionType.Destination) is { } d
            ? new ToolLocation(d.Cell, d.PositionX, d.PositionY, d.PositionZ, d.RotationW, d.RotationX, d.RotationY, d.RotationZ)
            : null;
}

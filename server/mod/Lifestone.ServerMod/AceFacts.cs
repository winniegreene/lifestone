using System.Buffers.Binary;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.DatLoader;
using ACE.Server.Network.Sequence;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// What the verbs read from ACE (ACEmulator/ACE 95ef426d, AGPL-3.0, read for reference): its points of interest, its
/// cell dat index, a player's sequences and whether a player can be moved now. Every place the mod depends on one of
/// ACE's internals is here, cited by file and line, so a new ACE pin has one file to re-check.
/// </summary>
internal static class AceFacts
{
    private static readonly System.Threading.Lock PoiCacheLock = new();
    private static bool poiCacheFilled;

    /// <summary>
    /// ACE's points of interest (the world database's <c>points_of_interest</c> rows, which ACE's own <c>@telepoi</c>
    /// reads), in a stable order: by name, ordinal.
    /// </summary>
    /// <remarks>
    /// The first call fills ACE's cache from the database (WorldDatabaseWithEntityCache.CacheAllPointsOfInterest,
    /// WorldDatabaseWithEntityCache.cs:659-669); later calls read the cache. The cache can also hold null values, which
    /// ACE stores for a name its own <c>@telepoi</c> did not find (:687-702); they are skipped. Its keys are lower-cased
    /// in the thread's culture, so the mod never looks names up by key: <see cref="FindPointOfInterest"/> compares the
    /// rows' own names.
    /// </remarks>
    public static IReadOnlyList<PointsOfInterest> PointsOfInterest()
    {
        lock (PoiCacheLock)
        {
            if (!poiCacheFilled)
            {
                DatabaseManager.World.CacheAllPointsOfInterest();
                poiCacheFilled = true;
            }
        }

        return
        [
            .. DatabaseManager.World.GetPointsOfInterestCache().Values
                .Where(p => p is not null && !string.IsNullOrEmpty(p.Name))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Id),
        ];
    }

    /// <summary>
    /// The point of interest named <paramref name="name"/>, ignoring case (ordinal, so the server's culture cannot change
    /// the match), or null. ACE's own lookup lower-cases the name in the thread's culture and remembers misses as nulls
    /// (WorldDatabaseWithEntityCache.cs:687-702), which the mod avoids.
    /// </summary>
    public static PointsOfInterest? FindPointOfInterest(string name) =>
        PointsOfInterest().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when ACE's cell dat has the cell: for an outdoor cell, its landblock's file (<c>0xXXYYFFFF</c>; outdoor cells
    /// are not files of their own); for an EnvCell, the cell's own file. Reads the dat's file index only
    /// (DatDatabase.AllFiles, DatDatabase.cs:30, filled once when ACE opens the dat, DatManager.cs:35), so nothing is
    /// loaded: ACE's own cell lookup would load the landblock in server mode (LScape.get_landblock, LScape.cs:54-63).
    /// </summary>
    public static bool CellExists(uint cellId)
    {
        var index = DatManager.CellDat?.AllFiles ?? throw new InvalidOperationException("ACE's cell dat is not open (DatManager.CellDat is null)");
        return PlaceRules.IsOutdoorCell(cellId)
            ? index.ContainsKey(PlaceRules.LandblockOf(cellId))
            : PlaceRules.IsEnvCell(cellId) && index.ContainsKey(cellId);
    }

    /// <summary>
    /// The player's current sequence of <paramref name="type"/> (a u16, ObjectForcePosition or ObjectTeleport), read
    /// from the bytes ACE writes into its messages: SequenceManager.GetCurrentSequence gives
    /// <c>BitConverter.GetBytes</c> of a UShortSequence (SequenceManager.cs:12-15, 151-183; UShortSequence.cs), and the
    /// client reads them little-endian.
    /// </summary>
    public static ushort CurrentSequence(Player player, SequenceType type)
    {
        var bytes = player.Sequences.GetCurrentSequence(type);
        return bytes is { Length: 2 }
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : throw new InvalidOperationException(type + " is not a 2-byte sequence in this ACE (got " + (bytes?.Length ?? 0) + " bytes)");
    }

    /// <summary>
    /// Why the player cannot be teleported now, or null: already teleporting (WorldObject.Teleporting,
    /// WorldObject.cs:263; cleared by Player.OnTeleportComplete), logging out (Player.IsLoggingOut, Player.cs:493) or dead
    /// (Creature.IsDead, Monster_Combat.cs:72).
    /// </summary>
    public static string? TeleportBusy(Player player)
    {
        if (player.Teleporting)
        {
            return "you are already teleporting; wait until you arrive";
        }

        if (player.IsLoggingOut)
        {
            return "you are logging out";
        }

        return player.IsDead ? "you are dead" : null;
    }
}

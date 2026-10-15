using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Lifestone.ServerTools;

/// <summary>
/// The error codes a reply's <c>error</c> field carries (m2-plan §4.12). A client should treat an unknown code as
/// <see cref="Failed"/>.
/// </summary>
public static class ToolErrors
{
    /// <summary>The request did not parse, or an argument is out of range.</summary>
    public const string BadArgs = "bad-args";

    /// <summary>The first word is not a verb (<see cref="ToolVerbs"/>).</summary>
    public const string UnknownVerb = "unknown-verb";

    /// <summary>No point of interest has that name.</summary>
    public const string NotFound = "not-found";

    /// <summary>The cell is not in ACE's cell dat.</summary>
    public const string InvalidCell = "invalid-cell";

    /// <summary>The player cannot do it now: teleporting, logging out or dead.</summary>
    public const string Busy = "busy";

    /// <summary>The player is not in the world (no landblock).</summary>
    public const string NotInWorld = "not-in-world";

    /// <summary>The session did not come from a loopback address.</summary>
    public const string NotLoopback = "not-loopback";

    /// <summary>The verb needs a player, and it came from ACE's console.</summary>
    public const string NeedsPlayer = "needs-player";

    /// <summary>Anything else: the mod caught an exception or ACE refused (the message says which).</summary>
    public const string Failed = "failed";

    /// <summary><c>char purge</c>: the name is one of the graduated test characters, which stay as the baseline (owner decision D59).</summary>
    public const string Graduated = "graduated";

    /// <summary><c>char purge</c>: the name is the character the caller is playing.</summary>
    public const string OwnCharacter = "own-character";

    /// <summary><c>char purge</c>: no character of that name is on the caller's account (another account's are never revealed).</summary>
    public const string UnknownCharacter = "unknown-character";

    /// <summary><c>char purge</c>: the character is online.</summary>
    public const string CharacterOnline = "character-online";

    /// <summary><c>char purge</c>: the character is not marked for deletion (delete it on the character screen first).</summary>
    public const string NotPendingDeletion = "not-pending-deletion";

    /// <summary>Every code above.</summary>
    public static IReadOnlyList<string> All { get; } =
        [BadArgs, UnknownVerb, NotFound, InvalidCell, Busy, NotInWorld, NotLoopback, NeedsPlayer, Failed, Graduated, OwnCharacter, UnknownCharacter, CharacterOnline, NotPendingDeletion];
}

/// <summary>Why a request failed: a code from <see cref="ToolErrors"/> and one human sentence.</summary>
/// <param name="Code">The error code (<see cref="ToolErrors"/>).</param>
/// <param name="Message">One sentence for the player, printable ASCII.</param>
public sealed record ToolError(string Code, string Message);

/// <summary>
/// The machine-readable half of a reply: the JSON after <see cref="ToolsWire.ReplyPrefix"/> (camelCase, ASCII only).
/// <see cref="ToolsWire.Reply"/> writes it, <see cref="ToolsWire.TryParseReply"/> reads it.
/// </summary>
/// <param name="V">The wire version, always <see cref="ToolsWire.Version"/> in a reply this code accepts.</param>
/// <param name="Id">The request's correlation id, echoed (null when the request had none or it could not be read).</param>
/// <param name="Verb">The request's verb (<see cref="ToolVerbs"/>); for an unknown verb its lower-cased word when that is short
/// ASCII, else empty.</param>
/// <param name="Ok">True when the request was carried out (for <c>place</c>: the teleport started).</param>
/// <param name="Error">The error code when <paramref name="Ok"/> is false (<see cref="ToolErrors"/>).</param>
/// <param name="Message">The error's sentence, or an optional note on success.</param>
/// <param name="Data">The verb's data (<see cref="DataAs{T}"/>), or null (always null on failure).</param>
public sealed record ToolReply(int V, string? Id, string Verb, bool Ok, string? Error, string? Message, JsonElement? Data)
{
    /// <summary>
    /// The data as <typeparamref name="T"/> (one of <see cref="StatusData"/>, <see cref="WhereData"/>,
    /// <see cref="PlaceData"/>, <see cref="XpData"/>, <see cref="QuestData"/>, <see cref="CharPurgeData"/>, <see cref="NetData"/>,
    /// <see cref="PlacesData"/>), or null when the
    /// reply has none. Throws <see cref="JsonException"/> when the data does not have that shape.
    /// </summary>
    public T? DataAs<T>()
        where T : class =>
        Data is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } data ? data.Deserialize(TypeInfo<T>()) : null;

    /// <summary>The error as a <see cref="ToolError"/>, or null on success.</summary>
    public ToolError? AsError() => Ok ? null : new ToolError(Error ?? ToolErrors.Failed, Message ?? string.Empty);

    /// <summary>A successful reply to <paramref name="request"/> carrying <paramref name="data"/>.</summary>
    public static ToolReply Success<T>(ToolRequest request, T data, string? message = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        return Success(request.Id, request.Verb, data, message);
    }

    /// <summary>A successful reply for <paramref name="verb"/> carrying <paramref name="data"/>.</summary>
    public static ToolReply Success<T>(string? id, string verb, T data, string? message = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(verb);
        ArgumentNullException.ThrowIfNull(data);
        return new ToolReply(ToolsWire.Version, id, verb, true, null, message, JsonSerializer.SerializeToElement(data, TypeInfo<T>()));
    }

    /// <summary>A failed reply for <paramref name="verb"/>.</summary>
    public static ToolReply Failure(string? id, string verb, ToolError error)
    {
        ArgumentNullException.ThrowIfNull(verb);
        ArgumentNullException.ThrowIfNull(error);
        return new ToolReply(ToolsWire.Version, id, verb, false, error.Code, error.Message, null);
    }

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        ToolsWireJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new NotSupportedException(typeof(T).Name + " is not a tools-wire data type; add it to ToolsWireJsonContext");
}

/// <summary>How <see cref="ToolsWire.ParseReply"/> classified a server message.</summary>
public enum ToolReplyKind
{
    /// <summary>Not a tool reply: ordinary chat (show it).</summary>
    NotAReply,

    /// <summary>A version-1 tool reply (<see cref="ToolReplyParse.Reply"/> is set).</summary>
    Reply,

    /// <summary>A tool reply of another wire version: the mod and the client disagree, a setup problem to report.</summary>
    UnsupportedVersion,

    /// <summary>It claims to be a version-1 tool reply but does not parse: a bug to report.</summary>
    Malformed,
}

/// <summary>The result of <see cref="ToolsWire.ParseReply"/>.</summary>
/// <param name="Kind">What the message is.</param>
/// <param name="Reply">The reply, for <see cref="ToolReplyKind.Reply"/>.</param>
/// <param name="ChatLine">The human line (<c>Lifestone: ...</c>) for every kind but <see cref="ToolReplyKind.NotAReply"/>.</param>
/// <param name="Problem">Why it was not accepted, for <see cref="ToolReplyKind.UnsupportedVersion"/> and <see cref="ToolReplyKind.Malformed"/>.</param>
public sealed record ToolReplyParse(ToolReplyKind Kind, ToolReply? Reply, string? ChatLine, string? Problem);

/// <summary>The data of a <c>status</c> reply.</summary>
/// <param name="Wire">The mod's wire version (<see cref="ToolsWire.Version"/>).</param>
/// <param name="Mod">The mod's version (Meta.json).</param>
/// <param name="AceCommit">The ACE commit the mod was built against (40 hex digits).</param>
/// <param name="AceServerDllSha256">SHA-256 of the ACE.Server.dll the mod was built against and checked at load.</param>
/// <param name="Host">ACE's configured host (always 127.0.0.1, or the mod refuses to load).</param>
/// <param name="Port">ACE's configured port.</param>
/// <param name="AutoPromotion">ACE's AutoPromoteNextAccountToAdmin flag (false once the mod is ready).</param>
/// <param name="Access">The session's access level (ACE's AccessLevel name), or <c>Console</c>.</param>
/// <param name="Account">The session's account name, or null from the console.</param>
/// <param name="Character">The player's name, or null.</param>
/// <param name="World">ACE's world status (<c>Open</c> or <c>Closed</c>).</param>
/// <param name="Verbs">The verbs this mod build carries out, in <see cref="ToolsWire.Verbs"/> spelling.</param>
public sealed record StatusData(
    int Wire,
    string Mod,
    string AceCommit,
    string AceServerDllSha256,
    string Host,
    int Port,
    bool AutoPromotion,
    string Access,
    string? Account,
    string? Character,
    string World,
    IReadOnlyList<string> Verbs)
{
    /// <summary>The reply's human sentence.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"mod {Mod} (wire {Wire}) on ACE {ToolsWire.ShortCommit(AceCommit)} at {Host}:{Port}; world {World}; access {Access}; auto-promotion {(AutoPromotion ? "ON" : "off")}; verbs: {string.Join(", ", Verbs)}");
}

/// <summary>The data of a <c>where</c> reply: the server's view of the player's position.</summary>
/// <param name="Cell">The player's cell (<see cref="ToolsWire.FormatCell"/>).</param>
/// <param name="X">Origin x in the landblock frame.</param>
/// <param name="Y">Origin y.</param>
/// <param name="Z">Origin z.</param>
/// <param name="Qw">Rotation w.</param>
/// <param name="Qx">Rotation x.</param>
/// <param name="Qy">Rotation y.</param>
/// <param name="Qz">Rotation z.</param>
/// <param name="Landblock">The landblock the player is in (<c>0xXXYYFFFF</c>).</param>
/// <param name="PhysicsCell">The cell of the server's physics object.</param>
/// <param name="Teleporting">True while the server considers the player teleporting.</param>
/// <param name="ForcePositionSeq">The player's force-position sequence (unchanged while the server accepts the client's moves).</param>
/// <param name="TeleportSeq">The player's teleport sequence.</param>
public sealed record WhereData(
    string Cell,
    float X,
    float Y,
    float Z,
    float Qw,
    float Qx,
    float Qy,
    float Qz,
    string Landblock,
    string PhysicsCell,
    bool Teleporting,
    ushort ForcePositionSeq,
    ushort TeleportSeq)
{
    /// <summary>The reply's human sentence.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"you are in cell {Cell} at ({X:0.00}, {Y:0.00}, {Z:0.00}){(Teleporting ? ", teleporting" : string.Empty)}");
}

/// <summary>The data of a <c>place</c> reply: where the teleport that just started goes.</summary>
/// <param name="Kind"><c>poi</c> or <c>loc</c> (<see cref="ToolVerbs.PlacePoi"/>, <see cref="ToolVerbs.PlaceLoc"/>).</param>
/// <param name="Name">The point of interest's name as ACE spells it, or null for <c>loc</c>.</param>
/// <param name="Cell">The destination cell (<see cref="ToolsWire.FormatCell"/>).</param>
/// <param name="X">Origin x.</param>
/// <param name="Y">Origin y.</param>
/// <param name="Z">Origin z.</param>
/// <param name="Qw">Rotation w.</param>
/// <param name="Qx">Rotation x.</param>
/// <param name="Qy">Rotation y.</param>
/// <param name="Qz">Rotation z.</param>
public sealed record PlaceData(string Kind, string? Name, string Cell, float X, float Y, float Z, float Qw, float Qx, float Qy, float Qz)
{
    /// <summary>The reply's human sentence.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"teleporting to {(Name is null ? string.Empty : Name + ", ")}cell {Cell} at ({X:0.00}, {Y:0.00}, {Z:0.00})");
}

/// <summary>The data of an <c>xp</c> reply.</summary>
/// <param name="Requested">The amount asked for.</param>
/// <param name="Added">What ACE actually added for this request alone, even with other experience queued around it in the
/// same world tick (less at the level cap; 0 for an Olthoi player).</param>
/// <param name="Total">Total experience afterwards.</param>
/// <param name="Available">Unassigned experience afterwards.</param>
/// <param name="Level">The level afterwards.</param>
public sealed record XpData(long Requested, long Added, long Total, long Available, int Level)
{
    /// <summary>The reply's human sentence.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"added {Added:N0} experience (asked {Requested:N0}); total {Total:N0}, unassigned {Available:N0}, level {Level}");
}

/// <summary>
/// The data of a <c>quest</c> reply: the player's own quest registry row for one quest, before and after the request. A
/// completion count is the registry's NumTimesCompleted, 0 when the character has no row. A quest can be held with a count of
/// 0 (a row ACE or a script wrote that way), so <see cref="Held"/> and <see cref="HeldBefore"/> say whether the row exists.
/// </summary>
/// <param name="Action">What was asked: <c>stamp</c>, <c>erase</c> or <c>has</c> (<see cref="ToolVerbs.QuestStamp"/> and the others).</param>
/// <param name="Name">The quest name as the request spelled it.</param>
/// <param name="HeldBefore">Whether the character had the quest before the request.</param>
/// <param name="Before">The completions before the request (0 without a row).</param>
/// <param name="Held">Whether the character has the quest now: always true after a stamp, false after an erase.</param>
/// <param name="After">The completions now (0 without a row). A stamp adds one, unless the quest is already at the maximum its
/// world definition allows (ACE then leaves it as it is, so <paramref name="After"/> equals <paramref name="Before"/>).</param>
public sealed record QuestData(string Action, string Name, bool HeldBefore, int Before, bool Held, int After)
{
    /// <summary>True when the request changed the registry: a row added or removed, or its count changed.</summary>
    public bool Changed() => HeldBefore != Held || Before != After;

    /// <summary>The reply's human sentence.</summary>
    public string Describe() => Action switch
    {
        ToolVerbs.QuestStamp when !HeldBefore => string.Create(CultureInfo.InvariantCulture, $"quest {Name} stamped: added, completions {After}"),
        ToolVerbs.QuestStamp when Changed() => string.Create(CultureInfo.InvariantCulture, $"quest {Name} stamped: completions {Before} to {After}"),
        ToolVerbs.QuestStamp => string.Create(CultureInfo.InvariantCulture, $"quest {Name} is already at its maximum ({After} completions); unchanged"),
        ToolVerbs.QuestErase when HeldBefore => string.Create(CultureInfo.InvariantCulture, $"quest {Name} erased (it had {Before} completions)"),
        ToolVerbs.QuestErase => string.Create(CultureInfo.InvariantCulture, $"quest {Name} was not held; nothing erased"),
        _ when Held => string.Create(CultureInfo.InvariantCulture, $"you have quest {Name}: {After} completions"),
        _ => string.Create(CultureInfo.InvariantCulture, $"you do not have quest {Name}"),
    };
}

/// <summary>
/// The data of a <c>char purge</c> reply: what ACE's <c>ShardDatabaseOfflineTools.PurgeCharacter</c> reported removing, as its
/// three counts. A purge that removed nothing (every count 0) is a failure, not this reply.
/// </summary>
/// <param name="Name">The character's name as ACE spells it.</param>
/// <param name="Characters">Character records removed (1).</param>
/// <param name="PlayerBiotas">Player biotas removed (1).</param>
/// <param name="Possessions">Inventory and wielded item biotas removed with the character.</param>
public sealed record CharPurgeData(string Name, int Characters, int PlayerBiotas, int Possessions)
{
    /// <summary>True when the purge removed something: at least a character record or a player biota.</summary>
    public bool Removed => Characters > 0 || PlayerBiotas > 0;

    /// <summary>The reply's human sentence.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"purged {Name}: {Characters} character record{(Characters == 1 ? string.Empty : "s")}, {PlayerBiotas} player biota{(PlayerBiotas == 1 ? string.Empty : "s")}, {Possessions} possession{(Possessions == 1 ? string.Empty : "s")}");
}

/// <summary>
/// The data of a <c>net</c> reply: ACE's server-wide packet counters since it booted (NetworkStatistics). Take the
/// difference of two replies to measure a session.
/// </summary>
/// <param name="C2SPackets">Client-to-server packets received.</param>
/// <param name="S2CPackets">Server-to-client packets sent.</param>
/// <param name="C2SCrcErrors">Client-to-server packets that failed their checksum.</param>
/// <param name="C2SRetransmitRequests">Retransmit requests (NAKs) clients sent to the server.</param>
/// <param name="S2CRetransmitRequests">Retransmit requests the server sent to clients.</param>
public sealed record NetData(long C2SPackets, long S2CPackets, long C2SCrcErrors, long C2SRetransmitRequests, long S2CRetransmitRequests)
{
    /// <summary>The reply's human sentence.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"since ACE started: {C2SPackets:N0} packets in ({C2SCrcErrors:N0} bad checksums, {C2SRetransmitRequests:N0} resend requests), {S2CPackets:N0} out ({S2CRetransmitRequests:N0} resend requests)");
}

/// <summary>The data of a <c>places</c> reply: ACE's points of interest, one entry per destination.</summary>
/// <param name="Count">The number of destinations.</param>
/// <param name="Names">The number of names (a destination may have several).</param>
/// <param name="Places">The destinations.</param>
public sealed record PlacesData(int Count, int Names, IReadOnlyList<PoiEntry> Places)
{
    /// <summary>The reply's human sentence.</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture, $"{Count} places ({Names} names)");
}

/// <summary>One destination of ACE's points of interest: the portal weenie and its raw Destination position.</summary>
/// <param name="Names">ACE's names for it (<c>place poi</c> accepts any of them).</param>
/// <param name="Wcid">The portal weenie class id.</param>
/// <param name="Cell">The Destination cell (<see cref="ToolsWire.FormatCell"/>).</param>
/// <param name="X">Origin x.</param>
/// <param name="Y">Origin y.</param>
/// <param name="Z">Origin z.</param>
/// <param name="Qw">Rotation w.</param>
/// <param name="Qx">Rotation x.</param>
/// <param name="Qy">Rotation y.</param>
/// <param name="Qz">Rotation z.</param>
public sealed record PoiEntry(IReadOnlyList<string> Names, uint Wcid, string Cell, float X, float Y, float Z, float Qw, float Qx, float Qy, float Qz);

namespace Lifestone.ServerTools;

/// <summary>
/// The verbs of the <c>@lifestone</c> command (m2-plan §4.12, owner decision D51), as they appear on the wire and in a
/// reply's <c>verb</c> field. Requests match them ignoring case (ACE's command table is OrdinalIgnoreCase,
/// CommandManager.cs:22); replies always carry these lower-case spellings.
/// </summary>
public static class ToolVerbs
{
    /// <summary>The mod's state: versions, host, access level, auto-promotion, world status (<see cref="StatusData"/>).</summary>
    public const string Status = "status";

    /// <summary>Where the server thinks the player is (<see cref="WhereData"/>).</summary>
    public const string Where = "where";

    /// <summary>ACE's server-wide packet counters since boot (<see cref="NetData"/>).</summary>
    public const string Net = "net";

    /// <summary>ACE's points of interest and their destinations (<see cref="PlacesData"/>).</summary>
    public const string Places = "places";

    /// <summary>Adds experience to the player (<see cref="XpData"/>).</summary>
    public const string Xp = "xp";

    /// <summary>Teleports the player to a point of interest or an exact location (<see cref="PlaceData"/>).</summary>
    public const string Place = "place";

    /// <summary>The <c>place</c> form that names a point of interest.</summary>
    public const string PlacePoi = "poi";

    /// <summary>The <c>place</c> form that gives a cell and a frame.</summary>
    public const string PlaceLoc = "loc";

    /// <summary>Stamps, erases or checks one of the player's own quests (<see cref="QuestData"/>).</summary>
    public const string Quest = "quest";

    /// <summary>The <c>quest</c> form that stamps a quest, as ACE's quest scripts do (<see cref="QuestAction.Stamp"/>).</summary>
    public const string QuestStamp = "stamp";

    /// <summary>The <c>quest</c> form that erases a quest from the registry (<see cref="QuestAction.Erase"/>).</summary>
    public const string QuestErase = "erase";

    /// <summary>The <c>quest</c> form that reads a quest from the registry (<see cref="QuestAction.Has"/>).</summary>
    public const string QuestHas = "has";

    /// <summary>Acts on one of the caller's account's characters (<see cref="CharPurgeData"/>).</summary>
    public const string Char = "char";

    /// <summary>The <c>char</c> form that removes a character already marked for deletion from the server's database.</summary>
    public const string CharPurge = "purge";
}

/// <summary>
/// One <c>@lifestone</c> request. <see cref="ToolsWire.Request"/> writes it as chat text; the mod reads it back with
/// <see cref="ToolsWire.TryParseRequest"/>. Every request may carry an <see cref="Id"/>, which the reply echoes so a
/// client can match the two.
/// </summary>
/// <param name="Id">The correlation id: 1 to 16 of <c>A-Z a-z 0-9 -</c>, or null for none.</param>
public abstract record ToolRequest(string? Id)
{
    /// <summary>The request's verb (<see cref="ToolVerbs"/>), as the reply's <c>verb</c> field carries it.</summary>
    public abstract string Verb { get; }
}

/// <summary><c>@lifestone status</c>.</summary>
/// <param name="Id">The correlation id, or null.</param>
public sealed record StatusRequest(string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Status;
}

/// <summary><c>@lifestone where</c>.</summary>
/// <param name="Id">The correlation id, or null.</param>
public sealed record WhereRequest(string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Where;
}

/// <summary><c>@lifestone net</c>.</summary>
/// <param name="Id">The correlation id, or null.</param>
public sealed record NetRequest(string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Net;
}

/// <summary><c>@lifestone places</c>.</summary>
/// <param name="Id">The correlation id, or null.</param>
public sealed record PlacesRequest(string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Places;
}

/// <summary><c>@lifestone xp &lt;amount&gt;</c>.</summary>
/// <param name="Amount">Experience to add: 1 to <see cref="ToolRequestParser.MaxXp"/> (ACE's own grantxp limit).</param>
/// <param name="Id">The correlation id, or null.</param>
public sealed record XpRequest(long Amount, string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Xp;
}

/// <summary><c>@lifestone place poi "&lt;name&gt;"</c>: teleport to one of ACE's points of interest.</summary>
/// <param name="Name">
/// The point of interest, as ACE's <c>points_of_interest</c> table names it (matched ignoring case): 1 to
/// <see cref="ToolRequestParser.MaxNameLength"/> of <c>A-Z a-z 0-9</c>, space, apostrophe and hyphen.
/// </param>
/// <param name="Id">The correlation id, or null.</param>
public sealed record PlacePoiRequest(string Name, string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Place;
}

/// <summary><c>@lifestone place loc &lt;cell&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; [&lt;qw&gt; &lt;qx&gt; &lt;qy&gt; &lt;qz&gt;]</c>: teleport to an exact location.</summary>
/// <param name="At">The destination.</param>
/// <param name="Id">The correlation id, or null.</param>
public sealed record PlaceLocRequest(ToolLocation At, string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Place;
}

/// <summary>What a <see cref="QuestRequest"/> does to the quest.</summary>
public enum QuestAction
{
    /// <summary>Stamps the quest (ACE's <c>QuestManager.Stamp</c>, what a quest script's StampQuest emote does).</summary>
    Stamp,

    /// <summary>Erases the quest from the registry (<c>QuestManager.Erase</c>).</summary>
    Erase,

    /// <summary>Only reads whether the registry holds the quest.</summary>
    Has,
}

/// <summary>
/// <c>@lifestone quest stamp|erase|has &lt;name&gt;</c>: one of the calling player's own quests. A quest is a named row in
/// the character's quest registry; a portal, a door or a conversation tests it. The mod acts on the requesting
/// player's character only, never on another.
/// </summary>
/// <param name="Action">What to do with the quest.</param>
/// <param name="Name">
/// The quest's name as ACE spells it (matched ignoring case): 1 to <see cref="ToolRequestParser.MaxQuestNameLength"/>
/// of <c>A-Z a-z 0-9</c>, <c>_</c>, <c>-</c>, <c>(</c> and <c>)</c>. Never <c>@</c>: ACE reads what follows it as a comment.
/// </param>
/// <param name="Id">The correlation id, or null.</param>
public sealed record QuestRequest(QuestAction Action, string Name, string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Quest;
}

/// <summary>
/// <c>@lifestone char purge "&lt;name&gt;"</c>: removes a character that is already marked for deletion from the server's
/// database, for good (owner decision D59). Only a character on the caller's own account that was deleted through the
/// retail character screens is purged; the graduated test characters, the caller's own character, a character that is
/// online and any other account's characters are refused (<see cref="ToolErrors.Graduated"/> and its neighbours).
/// </summary>
/// <param name="Name">
/// The character's name (matched ignoring case): 1 to <see cref="ToolRequestParser.MaxCharacterNameLength"/> of <c>A-Z a-z
/// 0-9</c>, space, apostrophe and hyphen, not starting or ending with a space.
/// </param>
/// <param name="Id">The correlation id, or null.</param>
public sealed record CharPurgeRequest(string Name, string? Id = null) : ToolRequest(Id)
{
    /// <inheritdoc/>
    public override string Verb => ToolVerbs.Char;
}

/// <summary>
/// A location in AC terms: a cell id and a frame in its landblock (metres; outdoor x and y in [0, 192)), with the
/// rotation as a quaternion in wire order W, X, Y, Z. The constructor's defaults give the identity rotation; note that
/// <c>default(ToolLocation)</c> has an all-zero quaternion, which no request accepts.
/// </summary>
/// <param name="Cell">The cell id: an outdoor cell (low word 0x0001-0x0040) or an EnvCell (0x0100-0xFFFD).</param>
/// <param name="X">Origin x in the landblock frame.</param>
/// <param name="Y">Origin y in the landblock frame.</param>
/// <param name="Z">Origin z.</param>
/// <param name="Qw">Rotation w.</param>
/// <param name="Qx">Rotation x.</param>
/// <param name="Qy">Rotation y.</param>
/// <param name="Qz">Rotation z.</param>
public readonly record struct ToolLocation(uint Cell, float X, float Y, float Z, float Qw = 1, float Qx = 0, float Qy = 0, float Qz = 0)
{
    /// <summary>True unless the rotation is exactly the identity (1, 0, 0, 0), so a request must spell it out.</summary>
    public bool HasRotation => Qw != 1 || Qx != 0 || Qy != 0 || Qz != 0;
}

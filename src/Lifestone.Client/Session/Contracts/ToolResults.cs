using Lifestone.Client.Session.Tools;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Contracts;

/// <summary>Whether the player tools can be used (<see cref="PlayerTools.Availability"/>).</summary>
public enum ToolAvailability
{
    /// <summary>Not known yet: the session has not probed the mod (it does so on entering the world).</summary>
    Unknown,

    /// <summary>The mod answered; tools work.</summary>
    Ready,

    /// <summary>The server answered <c>Unknown command: lifestone</c>: the Lifestone server mod is not loaded.</summary>
    ModMissing,

    /// <summary>The server is not on loopback: the tools refuse to work (DoD 10, A0's R1).</summary>
    NotLoopback,

    /// <summary>The player is not in the world, so there is no character to run a tool for.</summary>
    NotInWorld,
}

/// <summary>The mod's <c>status</c> reply (<see cref="PlayerTools.Status"/>). An access level above Player, or ACE's
/// first-account auto-promotion on, is logged as an ERROR and stops automation (owner decision D11).</summary>
/// <param name="Wire">The mod's wire version (1).</param>
/// <param name="ModVersion">The mod's version.</param>
/// <param name="Access">The account's access level as ACE names it (<c>Player</c> for every Lifestone account).</param>
/// <param name="AutoPromotion">ACE's first-account Admin auto-promotion flag (false once the mod is ready).</param>
/// <param name="Account">The account name.</param>
/// <param name="Character">The character's name, or null.</param>
/// <param name="World">The world's name.</param>
/// <param name="Verbs">The verbs the mod serves.</param>
public sealed record ToolStatus(
    int Wire,
    string ModVersion,
    string Access,
    bool AutoPromotion,
    string Account,
    string? Character,
    string World,
    IReadOnlyList<string> Verbs);

/// <summary>The mod's <c>where</c> reply (<see cref="PlayerTools.WhereAmI"/>): the server's view of the player (DoD 8's 0.5 m check).</summary>
/// <param name="Position">The player's position on the server.</param>
/// <param name="Teleporting">True while the server considers the player teleporting.</param>
/// <param name="ForcePositionSequence">The player's force-position sequence on the server (unchanged by a walk ACE accepts).</param>
/// <param name="TeleportSequence">The player's teleport sequence on the server.</param>
public sealed record WhereResult(CellFrame Position, bool Teleporting, ushort ForcePositionSequence, ushort TeleportSequence)
{
    /// <summary>
    /// How this server position compares with the client's (<paramref name="client"/>: the physics body, null for none): the distance, whether it agrees within
    /// <see cref="PositionAgreement.ToleranceMetres"/> (DoD 8) and the lines the panel and the chat show. Pure data, so the retail UI needs nothing of <c>Session.Tools</c>.
    /// </summary>
    public PositionComparison Compare(CellFrame? client)
    {
        var position = Position;
        var serverLoc = AceLocText.Format(in position);
        if (client is not { } mine)
        {
            return new PositionComparison(serverLoc, null, null, false);
        }

        var agrees = PositionAgreement.Agrees(in position, in mine, out var metres);
        return new PositionComparison(serverLoc, PositionAgreement.Describe(in position, in mine), metres, agrees);
    }
}

/// <summary>A server position compared with the client's (<see cref="WhereResult.Compare"/>).</summary>
/// <param name="ServerLoc">The server's position in ACE's text form.</param>
/// <param name="Line">The full chat line (both positions, the distance and the verdict), or null when there is no client position.</param>
/// <param name="Metres">The distance in metres, or null when there is no client position.</param>
/// <param name="Agrees">True when the distance is within the tolerance.</param>
public sealed record PositionComparison(string ServerLoc, string? Line, double? Metres, bool Agrees);

/// <summary>
/// The mod's <c>place</c> reply (<see cref="PlayerTools.Place"/>): the teleport started. Its completion is the usual
/// F751 and position update, which portal space follows.
/// </summary>
/// <param name="Kind"><c>poi</c> (ACE's point of interest, by name) or <c>loc</c> (an explicit location).</param>
/// <param name="Name">The point of interest's name, for <c>poi</c>.</param>
/// <param name="Destination">Where the server is sending the player.</param>
public sealed record PlaceResult(string Kind, string? Name, CellFrame Destination);

/// <summary>The mod's <c>xp</c> reply (<see cref="PlayerTools.AddXp"/>), after ACE applied it.</summary>
/// <param name="Requested">The amount asked for.</param>
/// <param name="Added">The amount ACE added for this request alone, even with other experience granted in the same world
/// tick (less at the level cap; 0 for an Olthoi player; the mod's <c>XpData.Added</c>).</param>
/// <param name="Total">The character's total experience now.</param>
/// <param name="Available">The unassigned experience now.</param>
/// <param name="Level">The character's level now.</param>
public sealed record XpResult(long Requested, long Added, long Total, long Available, int Level);

/// <summary>
/// The mod's <c>quest</c> reply (<see cref="PlayerTools.StampQuest"/>, <see cref="PlayerTools.EraseQuest"/>,
/// <see cref="PlayerTools.HasQuest"/>): the character's own quest registry row for one quest, before and after the request. A
/// completion count is 0 when the character has no row; a row can also hold 0, which is why the held flags exist.
/// </summary>
/// <param name="Action">What was asked: <c>stamp</c>, <c>erase</c> or <c>has</c>.</param>
/// <param name="Name">The quest name as requested.</param>
/// <param name="HeldBefore">Whether the character had the quest before the request.</param>
/// <param name="CompletionsBefore">The completions before the request.</param>
/// <param name="Held">Whether the character has the quest now (always true after a stamp, false after an erase).</param>
/// <param name="Completions">The completions now. A stamp adds one, unless the quest is already at the most its world definition
/// allows, when ACE leaves it as it is.</param>
public sealed record QuestResult(string Action, string Name, bool HeldBefore, int CompletionsBefore, bool Held, int Completions)
{
    /// <summary>True when the request changed the registry: a row added or removed, or its count changed.</summary>
    public bool Changed => HeldBefore != Held || CompletionsBefore != Completions;
}

/// <summary>
/// The mod's <c>char purge</c> reply (<see cref="PlayerTools.PurgeCharacter"/>): a character that was marked for deletion is gone
/// from the server's database. The counts are the ones ACE's purge reports; every count is at least 0 and a purge that removed
/// nothing is a failure, not this result.
/// </summary>
/// <param name="Name">The character's name as the server spells it.</param>
/// <param name="Characters">Character records removed (1).</param>
/// <param name="PlayerBiotas">Player biotas removed (1).</param>
/// <param name="Possessions">Inventory and wielded item biotas removed with the character.</param>
public sealed record CharPurgeResult(string Name, int Characters, int PlayerBiotas, int Possessions);

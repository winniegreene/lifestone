using Lifestone.Client.Session.Tools;
using Lifestone.Data.World;
using Lifestone.ServerTools;

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

/// <summary>What the mod did with one spell of a <c>spell learn</c> request (<see cref="SpellLearnEntry.Outcome"/>).</summary>
public enum SpellOutcomeKind
{
    /// <summary>The character did not know the spell and now does.</summary>
    Learned,

    /// <summary>The character already knew the spell; nothing changed.</summary>
    Known,

    /// <summary>The id is not in the server's spell table; nothing was learned. A request holding one is a failure (<see cref="SpellLearnResult.Ok"/> is false).</summary>
    Unknown,
}

/// <summary>One spell of a <see cref="SpellLearnResult"/>, in request order.</summary>
/// <param name="Id">The spell id as requested.</param>
/// <param name="Outcome">What happened to it.</param>
/// <param name="Name">The spell's name in the spell table, or null for an unknown id.</param>
public sealed record SpellLearnEntry(uint Id, SpellOutcomeKind Outcome, string? Name);

/// <summary>
/// The mod's <c>spell learn</c> reply (<see cref="PlayerTools.LearnSpells"/>): one entry per requested id, in order. The mod carries out every
/// id it can, so an unknown id does not stop the others; <see cref="Ok"/> is true only when none was unknown, and the handle then ends as a
/// server error naming the ids (<see cref="PlayerTools.UnknownSpellCode"/>) with this result still set.
/// </summary>
/// <param name="Spells">The outcomes, one per requested id, in order.</param>
/// <param name="Learned">How many were learned just now.</param>
/// <param name="Known">How many the character already knew.</param>
/// <param name="Unknown">How many are not in the spell table.</param>
public sealed record SpellLearnResult(IReadOnlyList<SpellLearnEntry> Spells, int Learned, int Known, int Unknown)
{
    /// <summary>True when no requested id was unknown.</summary>
    public bool Ok => Unknown == 0;

    /// <summary>True when every requested id was already known (a ready spellbook is verified by this).</summary>
    public bool AllAlreadyKnown => Spells.Count > 0 && Known == Spells.Count;

    /// <summary>The requested ids the spell table does not hold, in request order.</summary>
    public IReadOnlyList<uint> UnknownIds => Spells.Where(s => s.Outcome == SpellOutcomeKind.Unknown).Select(s => s.Id).ToArray();

    /// <summary>Equal when the entries are equal in order (a record's default would compare the list by reference).</summary>
    public bool Equals(SpellLearnResult? other) => other is not null && Learned == other.Learned && Known == other.Known && Unknown == other.Unknown && Spells.SequenceEqual(other.Spells);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Spells.Count, Learned, Known, Unknown);
}

/// <summary>
/// The mod's <c>item</c> reply (<see cref="PlayerTools.SpawnItem"/>): the item ACE created in the player's pack. A stack above the item's maximum is
/// clamped to it, and a stack asked for an item that does not stack is ignored (one is made); <see cref="Clamped"/> says so and is never an error.
/// </summary>
/// <param name="Guid">The new object's id.</param>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Stack">The stack size it was created with (1 for an item that does not stack).</param>
/// <param name="Requested">The stack size asked for, or null when none was.</param>
/// <param name="MaxStack">The item's maximum stack size, or null when it does not stack.</param>
/// <param name="Clamped">True when <paramref name="Stack"/> is below <paramref name="Requested"/>.</param>
public sealed record ItemResult(uint Guid, uint Wcid, string Name, int Stack, int? Requested, int? MaxStack, bool Clamped);

/// <summary>How a <see cref="PlayerTools.SpawnItem"/> request ended (<see cref="ItemOutcomes.Of"/>).</summary>
public enum ItemOutcome
{
    /// <summary>Not finished yet.</summary>
    Pending,

    /// <summary>The item was created (<see cref="ToolHandle{T}.Result"/> is set; read <see cref="ItemResult.Clamped"/>).</summary>
    Created,

    /// <summary>The server has no weenie of that class id (<c>unknown-wcid</c>).</summary>
    UnknownWcid,

    /// <summary>The weenie is of a type the server never spawns (<c>not-creatable</c>).</summary>
    NotCreatable,

    /// <summary>The pack had no room; nothing was created (<c>no-room</c>).</summary>
    NoRoom,

    /// <summary>Anything else that is not a creation: refused before sending (a stack outside 1 to 65,535, the tools unavailable), a different server error, no answer in time.</summary>
    Failed,
}

/// <summary>Reads how a tool request for an item ended.</summary>
public static class ItemOutcomes
{
    /// <summary>The typed ending of <paramref name="handle"/>, from its outcome and the mod's error code.</summary>
    /// <param name="handle">A handle from <see cref="PlayerTools.SpawnItem"/>.</param>
    /// <returns>The ending.</returns>
    public static ItemOutcome Of(ToolHandle<ItemResult> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.Outcome is not { } outcome)
        {
            return ItemOutcome.Pending;
        }

        if (outcome.Kind == ActionOutcomeKind.Confirmed)
        {
            return handle.Result is null ? ItemOutcome.Failed : ItemOutcome.Created;
        }

        if (outcome.Kind != ActionOutcomeKind.ServerError)
        {
            return ItemOutcome.Failed;
        }

        var detail = outcome.Detail ?? string.Empty;
        if (detail.StartsWith(ToolErrors.UnknownWcid + ":", StringComparison.Ordinal))
        {
            return ItemOutcome.UnknownWcid;
        }

        if (detail.StartsWith(ToolErrors.NotCreatable + ":", StringComparison.Ordinal))
        {
            return ItemOutcome.NotCreatable;
        }

        return detail.StartsWith(ToolErrors.NoRoom + ":", StringComparison.Ordinal) ? ItemOutcome.NoRoom : ItemOutcome.Failed;
    }
}

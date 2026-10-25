using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// One character of the caller's account as ACE's shard database holds it (the fields of ACE's Character row the purge's
/// rules read), plus whether it is online. CharVerb makes these from ACE's rows; the tests make them by hand.
/// </summary>
/// <param name="Guid">The character's object id.</param>
/// <param name="Name">The name.</param>
/// <param name="IsDeleted">ACE's <c>IsDeleted</c>: the deletion delay has run out and ACE has processed it.</param>
/// <param name="DeleteTime">ACE's <c>DeleteTime</c>: the Unix time the character is deleted at; 0 for a character that was never marked.</param>
/// <param name="IsOnline">True when a player of that character is logged in.</param>
internal readonly record struct AccountCharacter(uint Guid, string Name, bool IsDeleted, ulong DeleteTime, bool IsOnline)
{
    /// <summary>
    /// True when the character was marked for deletion on the character screen (ACE's CharacterDelete sets
    /// <c>DeleteTime</c> and leaves <c>IsDeleted</c> false, CharacterHandler.cs:315-318) or its delay has run out
    /// (<c>IsDeleted</c>, Session.CheckCharactersForDeletion, Session.cs:195-210).
    /// </summary>
    public bool PendingDeletion => IsDeleted || DeleteTime > 0;

    /// <summary>
    /// True for the character the retail character screen still lists as pending: marked, with its delay still running
    /// (<c>!IsDeleted &amp;&amp; DeleteTime &gt; 0</c>). A row whose delay has run out (<c>IsDeleted</c>) is no longer in the list.
    /// </summary>
    public bool VisiblePending => !IsDeleted && DeleteTime > 0;

    /// <summary>
    /// The order in which <see cref="CharPurgeRules.Check"/> prefers same-named rows: 0 the visible pending one (the one the caller just
    /// deleted), 1 an expired <c>IsDeleted</c> row (invisible in the list), 2 any other (a live character).
    /// </summary>
    public int PurgeRank => VisiblePending ? 0 : IsDeleted ? 1 : 2;
}

/// <summary>What <see cref="CharPurgeRules.Check"/> decided: a refusal, or the character to purge.</summary>
/// <param name="Refusal">Why the request is refused, or null.</param>
/// <param name="Target">The character to purge, when there is no refusal.</param>
internal sealed record CharPurgeDecision(ToolError? Refusal, AccountCharacter? Target)
{
    /// <summary>True when the character may be purged.</summary>
    public bool Allowed => Refusal is null && Target is not null;
}

/// <summary>
/// The rules of <c>@lifestone char purge</c> (owner decision D59), with nothing from ACE in them, so
/// Lifestone.ServerMod.Tests runs them (CharPurgeRulesTests). Only a character on the caller's own account that was already
/// marked for deletion through the retail character screens is purged.
/// </summary>
/// <remarks>
/// The refusals, in order, each with its own code and message: a graduated test character
/// (<see cref="ToolErrors.Graduated"/>), the caller's own character (<see cref="ToolErrors.OwnCharacter"/>), a name with no
/// character on the caller's account (<see cref="ToolErrors.UnknownCharacter"/>; another account's characters are never
/// revealed, so a name on another account looks the same as no name), an online character
/// (<see cref="ToolErrors.CharacterOnline"/>) and a character not marked for deletion
/// (<see cref="ToolErrors.NotPendingDeletion"/>). Names are compared ignoring case, ordinally (the server's culture must
/// not change the match). ACE frees a name as soon as its character is pending deletion
/// (ShardDatabase.IsCharacterNameAvailable, ShardDatabase.cs:583-595), so an account can hold a pending character and a
/// live one of the same name; the visible pending one is the one purged, then the latest-deleted of the expired ones (the verb is
/// repeated until it answers unknown-character to clear stale rows).
/// <para><b>The journey characters (m4-plan D44, D45).</b> The live gate deletes <see cref="Recreated"/> through the retail screens and
/// creates them again, and the new copy purges the old ones. For those three names only the guards change: no refusal by name, but a
/// graduated refusal while the account holds no live copy of the name (only a replaced copy goes, never the last one), then only the
/// account's pending copies of that name are candidates (none left answers unknown-character); the caller's own row is never one, since a
/// pending character is never in the world. The other graduated names are still refused by name, even a pending copy.</para>
/// </remarks>
internal static class CharPurgeRules
{
    /// <summary>The graduated test characters, which stay as the baseline and are never purged (m2-log-in §7 decision 13; "BSD Mage", the M3 mage, from m3-plan D11; "Holtburg Hunter", the M4 fighter, from m4-plan D1; "Holtburg Brawler" and "Holtburg Archer", the M4 melee and archer lanes' fighters, from m4-plan D31).</summary>
    public static IReadOnlyList<string> Graduated { get; } = ["Lifestone Smoke", "Lifestone Loopback", "BSD Mage", "Holtburg Hunter", "Holtburg Brawler", "Holtburg Archer", "Lifestone Tools"];

    /// <summary>The graduated journey characters the live gate re-creates (m4-plan D44: BSD Mage on test, Holtburg Brawler on test-melee, Holtburg Archer on test-archer): an old copy of one is purged once its new copy is on the account (D45).</summary>
    public static IReadOnlyList<string> Recreated { get; } = ["BSD Mage", "Holtburg Brawler", "Holtburg Archer"];

    /// <summary>The refusals that need no database read: a graduated name, then the caller's own character; null when neither applies, and for a <see cref="Recreated"/> name (D45: the account's rows decide).</summary>
    public static ToolError? EarlyRefusal(CharPurgeRequest request, string callerName)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var graduated in Graduated)
        {
            if (Same(graduated, request.Name))
            {
                // D45: the new copy names its own name to purge the old one; the guid check in Check keeps a character from purging itself.
                return IsRecreated(request.Name) ? null : GraduatedRefusal(graduated);
            }
        }

        return Same(callerName, request.Name)
            ? new ToolError(ToolErrors.OwnCharacter, "you are playing " + callerName + "; log in as another character to purge it")
            : null;
    }

    /// <summary>
    /// Applies every rule, in order, to the characters of the caller's account.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="callerName">The name of the character the caller is playing.</param>
    /// <param name="callerGuid">That character's object id.</param>
    /// <param name="account">The characters of the caller's account, deleted ones included (and no other account's).</param>
    public static CharPurgeDecision Check(CharPurgeRequest request, string callerName, uint callerGuid, IEnumerable<AccountCharacter> account)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(account);
        if (EarlyRefusal(request, callerName) is { } early)
        {
            return new CharPurgeDecision(early, null);
        }

        if (IsRecreated(request.Name))
        {
            return CheckReplacedCopy(request, callerGuid, account);
        }

        // The visible pending character first (the one the caller just deleted: the latest DeleteTime), then the expired IsDeleted rows
        // (invisible in the list; the same order), then the rest by id, so neither a live character nor an old expired row of the same
        // name shields or replaces the pending one. Keep StandInCharPurge.Decide equal (CharPurgeRulesTests runs one table through both).
        var matches = account
            .Where(c => Same(c.Name, request.Name))
            .OrderBy(c => c.PurgeRank)
            .ThenByDescending(c => c.DeleteTime)
            .ThenBy(c => c.Guid)
            .ToList();
        if (matches.Count == 0)
        {
            return Refuse(ToolErrors.UnknownCharacter, "no character named \"" + request.Name + "\" is on your account");
        }

        var target = matches[0];
        if (target.Guid == callerGuid)
        {
            return Refuse(ToolErrors.OwnCharacter, "you are playing " + callerName + "; log in as another character to purge it");
        }

        if (target.IsOnline)
        {
            return Refuse(ToolErrors.CharacterOnline, target.Name + " is online; only a character that is offline can be purged");
        }

        return target.PendingDeletion
            ? new CharPurgeDecision(null, target)
            : Refuse(ToolErrors.NotPendingDeletion, target.Name + " is not marked for deletion; delete it on the character screen first");
    }

    /// <summary>
    /// The live copies of a <see cref="Recreated"/> name on the account (not pending deletion), which the purge's second check needs beside the
    /// fresh row (D45: a replaced copy goes only while a live copy is on the account); empty for any other name.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="account">The characters of the caller's account.</param>
    public static AccountCharacter[] LiveCopies(CharPurgeRequest request, IEnumerable<AccountCharacter> account)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(account);
        return IsRecreated(request.Name) ? [.. account.Where(c => Same(c.Name, request.Name) && !c.PendingDeletion)] : [];
    }

    /// <summary>
    /// The reply for a purge ACE has run, or the failure when it did not do what it was asked.
    /// </summary>
    /// <param name="name">The character's name.</param>
    /// <param name="characters">ACE's count of character records removed.</param>
    /// <param name="playerBiotas">ACE's count of player biotas removed.</param>
    /// <param name="possessions">ACE's count of possessions removed.</param>
    /// <param name="characterRemains">True when a read after the purge still found the character's record: ACE's PurgeCharacter
    /// logs a failed save and carries on (ShardDatabaseOfflineTools.cs:136-142), after counting what it meant to remove.</param>
    public static (CharPurgeData? Data, ToolError? Error) Outcome(string name, int characters, int playerBiotas, int possessions, bool characterRemains)
    {
        if (characterRemains)
        {
            return (null, new ToolError(ToolErrors.Failed, "ACE's purge did not remove " + name + "; see ACE_Log.txt"));
        }

        var data = new CharPurgeData(name, characters, playerBiotas, possessions);
        return data.Removed
            ? (data, null)
            : (null, new ToolError(ToolErrors.Failed, "nothing was left to purge for " + name + "; it was already gone"));
    }

    /// <summary>
    /// Said in the reply and the log when the purge failed or was refused AFTER ACE's offline-player, name and account caches had dropped the character
    /// (<c>PlayerManager.ProcessDeletedPlayer</c>): its rows may still be there, but ACE no longer knows it until it restarts.
    /// </summary>
    public const string RestartNote = " (ACE has already dropped this character from its caches: it needs a restart for that character to be known again)";

    /// <summary>The error with <see cref="RestartNote"/> added to its sentence (the code stays).</summary>
    public static ToolError WithRestartNote(ToolError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error with { Message = error.Message + RestartNote };
    }

    /// <summary>
    /// D45's guards for a <see cref="Recreated"/> name: graduated while the account holds no live copy of it, then the pending copies only
    /// (the visible one first, then the expired ones latest first, as <see cref="Check"/> orders them); unknown-character when none is left.
    /// </summary>
    private static CharPurgeDecision CheckReplacedCopy(CharPurgeRequest request, uint callerGuid, IEnumerable<AccountCharacter> account)
    {
        var copies = account.Where(c => Same(c.Name, request.Name)).ToList();
        if (!copies.Any(c => !c.PendingDeletion))
        {
            return new CharPurgeDecision(GraduatedRefusal(Graduated.First(g => Same(g, request.Name))), null);
        }

        var pending = copies
            .Where(c => c.PendingDeletion)
            .OrderBy(c => c.PurgeRank)
            .ThenByDescending(c => c.DeleteTime)
            .ThenBy(c => c.Guid)
            .ToList();
        if (pending.Count == 0)
        {
            return Refuse(ToolErrors.UnknownCharacter, "no old copy of \"" + request.Name + "\" is pending deletion on your account");
        }

        var target = pending[0];
        if (target.Guid == callerGuid)
        {
            return Refuse(ToolErrors.OwnCharacter, "you are playing " + target.Name + "; log in as another character to purge it");
        }

        return target.IsOnline
            ? Refuse(ToolErrors.CharacterOnline, target.Name + " is online; only a character that is offline can be purged")
            : new CharPurgeDecision(null, target);
    }

    private static bool IsRecreated(string name) => Recreated.Any(r => Same(r, name));

    private static ToolError GraduatedRefusal(string graduated) => new(ToolErrors.Graduated, graduated + " is a graduated test character and stays as it is");

    private static CharPurgeDecision Refuse(string code, string message) => new(new ToolError(code, message), null);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

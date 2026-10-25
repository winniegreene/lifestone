using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>How a <see cref="FreshPurge"/> stands after a poll.</summary>
internal enum FreshPurgeProgress
{
    /// <summary>A purge request is in flight.</summary>
    Asking,

    /// <summary>The verb answered <c>unknown-character</c>: no old copy of the name is left.</summary>
    Done,

    /// <summary>The loop failed; <see cref="FreshPurge.Poll"/> named why.</summary>
    Failed,
}

/// <summary>
/// The purge stage of the fresh-character builds (M4 fresh B1, owner decisions D44 and D45), shared by <see cref="MageBuildScenario"/> and <see cref="FighterBuildScenario"/>: after the NEW copy of the template's character is built, it asks the server mod
/// to purge the old copies of the same name (<see cref="PlayerTools.PurgeCharacter"/>: the retail delete left the old live copy pending, and earlier gates' copies may still be pending or expired) until the verb answers <c>unknown-character</c>.
/// The loop is <see cref="ThrowawayLegScenario"/>'s: each Confirmed reply that removed something counts one in <see cref="Purged"/>, <c>unknown-character</c> ends it (also when nothing was purged: the count is then 0), <c>unknown-verb</c> names the mod install, and any other
/// reply fails by its code and sentence, <c>graduated</c> meaning the installed mod lacks the D45 rule (a pending row named like a build character may be purged when a live copy of that name is on the account). It asks only for the one name it was given.
/// </summary>
internal sealed class FreshPurge(string name, ILogSink log, string category)
{
    /// <summary>The mod verb the purge stage uses, which the status stage requires the mod to report in a fresh-character run (a stale mod fails before anything is created, not at the purge).</summary>
    public static readonly string RequiredVerb = ToolVerbs.Char + " " + ToolVerbs.CharPurge;

    /// <summary>The most purges one name may take before the loop fails: the verb must have answered <c>unknown-character</c> by then.</summary>
    public const int MaxPurges = 10;

    private ToolHandle<CharPurgeResult>? _handle;

    /// <summary>How many old copies of the name were purged (each Confirmed reply that removed a character record or a player biota).</summary>
    public int Purged { get; private set; }

    /// <summary>The name this loop purges (the template's character, never another).</summary>
    public string Name => name;

    /// <summary>
    /// Asks for the first purge, or polls the one in flight. Asking again after a purge is done here. <paramref name="failure"/> is the reason when the result is <see cref="FreshPurgeProgress.Failed"/>, else null.
    /// </summary>
    public FreshPurgeProgress Poll(IGameSessionView session, out string? failure)
    {
        failure = null;
        if (_handle is null)
        {
            _handle = session.Tools.PurgeCharacter(name, ActionSource.Host);
            return FreshPurgeProgress.Asking;
        }

        if (_handle.Outcome is not { } outcome)
        {
            return FreshPurgeProgress.Asking;
        }

        var handle = _handle;
        _handle = null;
        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith(ToolErrors.UnknownVerb, StringComparison.Ordinal) == true)
        {
            failure = "purge: the installed server mod has no char purge verb (install it with setup-server.ps1 -ModOnly, with the owner's go-ahead): " + Describe(outcome);
            return FreshPurgeProgress.Failed;
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith(ToolErrors.UnknownCharacter, StringComparison.Ordinal) == true)
        {
            log.Info(category, string.Create(CultureInfo.InvariantCulture, $"purge: no old copy of the template's character is left; the verb answered unknown-character after {Purged} purge(s)"));
            return FreshPurgeProgress.Done;
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith(ToolErrors.Graduated, StringComparison.Ordinal) == true)
        {
            failure = "purge: the server mod refused the name as graduated, so the installed mod lacks the D45 rule (a pending row named like a build character may be purged when a live copy of that name is on the account): update it with setup-server.ps1 -ModOnly, with the owner's go-ahead: " + Describe(outcome);
            return FreshPurgeProgress.Failed;
        }

        if (outcome.Kind != ActionOutcomeKind.Confirmed || handle.Result is not { } result || (result.Characters <= 0 && result.PlayerBiotas <= 0))
        {
            failure = "purge: " + Describe(outcome) + (handle.Result is { } r ? string.Create(CultureInfo.InvariantCulture, $" (removed {r.Characters} character records, {r.PlayerBiotas} player biotas)") : string.Empty);
            return FreshPurgeProgress.Failed;
        }

        Purged++;
        log.Info(category, string.Create(CultureInfo.InvariantCulture, $"purged an old copy: {result.Characters} character record(s), {result.PlayerBiotas} player biota(s), {result.Possessions} possession(s)"));
        if (Purged >= MaxPurges)
        {
            failure = string.Create(CultureInfo.InvariantCulture, $"purge: the old copies were purged {MaxPurges} times and the verb still does not answer unknown-character");
            return FreshPurgeProgress.Failed;
        }

        // Ask again: an older pending copy, or an expired row (invisible in the list), is purged next, until the verb says none is left.
        _handle = session.Tools.PurgeCharacter(name, ActionSource.Host);
        return FreshPurgeProgress.Asking;
    }

    private static string Describe(in ActionOutcome o) =>
        o.Kind + (o.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{o.Code:X})")) + (o.Detail is null ? string.Empty : ": " + o.Detail);
}

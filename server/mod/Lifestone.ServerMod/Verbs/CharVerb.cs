// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/CharacterCommands.cs HandleCharacterForcedDelete (@deletecharacter),
// Source/ACE.Database/ShardDatabaseOfflineTools.cs PurgeCharacter and Source/ACE.Server/Managers/PlayerManager.cs ProcessDeletedPlayer (AGPL-3.0)
// with changes: the call sequence only (the shard queue's read, PlayerManager.ProcessDeletedPlayer on the world's action queue, then
// ShardDatabaseOfflineTools.PurgeCharacter); Player level, and only a character on the requesting player's own account that is already
// marked for deletion, never an online one, the requesting player's own or a graduated test character (CharPurgeRules); the purge
// runs on the shard database's own worker, after a second read of the row; ACE's force-delete (it marks the character deleted itself and
// boots it) is not used; one machine-readable reply with PurgeCharacter's counts.
using ACE.Database;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Mods;
using ACE.Server.Network;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;
using ShardCharacter = ACE.Database.Models.Shard.Character;

namespace Lifestone.ServerMod;

/// <summary>
/// <c>@lifestone char purge "&lt;name&gt;"</c>: removes a character that is already marked for deletion from the shard
/// database (<see cref="CharPurgeData"/>), so the test account is left as it was. It is the one verb that writes the
/// database; the rules are <see cref="CharPurgeRules"/>'s (unit tested).
/// </summary>
/// <remarks>
/// <para><b>The steps, and why each is safe while the server runs.</b></para>
/// <list type="number">
/// <item><description><b>Early refusals</b> (<see cref="CharPurgeRules.EarlyRefusal"/>): a graduated name or the caller's own
/// character is refused at once, on the world thread, before the database is touched.</description></item>
/// <item><description><b>The lookup</b> is <c>SerializedShardDatabase.GetCharacters(accountId, includeDeleted: true)</c>
/// (SerializedShardDatabase.cs:205-212), which ACE runs on its one shard-database worker, in queue order (:42-78). Every save
/// queued before it has therefore landed: the throwaway's own logoff saves and the delete's SaveCharacter
/// (CharacterHandler.cs:315-324) are earlier entries in the same queue. The query is by the caller's own account id, so
/// another account's characters are never read. Whether a character is online is PlayerManager.GetOnlinePlayer
/// (PlayerManager.cs:339-352), a read under the manager's lock; an account has one session, and the caller's is in the world,
/// so no other character of the account can be online anyway.</description></item>
/// <item><description><b>The caches</b>: when the rules allow it, the callback queues <c>PlayerManager.ProcessDeletedPlayer</c>
/// on <c>WorldManager.ActionQueue</c>, as ACE's own @deletecharacter does (CharacterCommands.cs:96-100), so the world thread is
/// the one that takes the character out of ACE's offline-player, name and account caches (PlayerManager.cs:484-502; the
/// method locks them itself). It returns false when the character was never in them (a deletion ACE already processed), which
/// is logged and not an error. <c>HandlePlayerDelete</c> (allegiance and house bookkeeping, PlayerManager.cs:473-478) is not
/// repeated: ACE ran it when the character was marked for deletion (CharacterHandler.cs:324). A restore, the only way a
/// character comes back, needs the account's one session on the character screen, and the caller's is in the world. The same step
/// drops the character from the caller's session's own character list (<c>Session.Characters</c>), as ACE does for a character whose
/// deletion has run out (Session.cs:196-210), so the list sent at the caller's next log off is the account's real one. ACE's own
/// admin commands also change that list from the world thread (AdminCommands.cs:2149, 3559).</description></item>
/// <item><description><b>The purge</b> runs on the shard database's worker too: the world queue asks for a second read of the
/// row (<c>GetCharacter</c>, SerializedShardDatabase.cs:214-221; for a character whose context is live it returns the cached
/// <c>Character</c> instance from <c>CharacterContexts</c>, not a fresh database read, which is still right because a restore
/// mutates that same object on this same worker), and its callback checks the rules once more against that
/// row and only then calls <c>ShardDatabaseOfflineTools.PurgeCharacter</c> (ShardDatabaseOfflineTools.cs:76-144). Running
/// there, never on the world thread, keeps a database round trip from stalling the world, and keeps the purge in the same order as
/// every other shard write, so nothing queued for this character can land after it and put its rows back. PurgeCharacter opens
/// a context of its own and removes the inventory and wielded biotas, the player biota and the character record in one
/// <c>SaveChanges</c>. It logs a failed save and returns its counts anyway (:136-142), so the verb reads the row once more
/// afterwards and fails if it is still there (<see cref="CharPurgeRules.Outcome"/>). The caches were emptied before this step, so a
/// refusal here, or a purge that left the row, says in its reply and in the mod's log that ACE needs a restart for that character
/// to be known again (<see cref="CharPurgeRules.WithRestartNote"/>); the order (caches, then purge) is kept on purpose.</description></item>
/// <item><description><b>The reply</b> is one answer from that worker thread, as <c>places</c> answers from its task: the
/// session's send queue takes messages from any thread. An exception in any step answers once through
/// <see cref="ToolCall.Crashed"/>.</description></item>
/// </list>
/// <para>Nothing here deletes the character: it must already be marked for deletion through the retail screens, and
/// <see cref="CharPurgeRules"/> refuses everything else. The purge is logged by ACE (<c>[DATABASE][PURGE]</c>) and by the mod's
/// <c>Lifestone tool:</c> line.</para>
/// </remarks>
internal static class CharVerb
{
    /// <summary>Starts the purge of the character named by <paramref name="request"/>; answers once, later.</summary>
    public static void Purge(ToolCall call, Player player, CharPurgeRequest request)
    {
        var caller = new Caller(player.Name, player.Guid.Full, player.Character.AccountId, player.Session);
        if (CharPurgeRules.EarlyRefusal(request, caller.Name) is { } early)
        {
            call.Fail(early);
            return;
        }

        DatabaseManager.Shard.GetCharacters(caller.AccountId, true, rows => AfterLookup(call, request, caller, rows));
    }

    /// <summary>Runs on the shard database's worker: applies the rules to the account's characters.</summary>
    private static void AfterLookup(ToolCall call, CharPurgeRequest request, Caller caller, List<ShardCharacter> rows)
    {
        try
        {
            var decision = CharPurgeRules.Check(request, caller.Name, caller.Guid, rows.Select(Describe));
            if (decision.Target is not { } target || !decision.Allowed)
            {
                call.Fail(decision.Refusal ?? new ToolError(ToolErrors.Failed, "the purge rules gave neither a refusal nor a character"));
                return;
            }

            WorldManager.ActionQueue.EnqueueAction(new ActionEventDelegate(() => OnWorldThread(call, request, caller, target)));
        }
        catch (Exception e)
        {
            call.Crashed(e);
        }
    }

    /// <summary>Runs on the world's action queue: ACE's own deletion bookkeeping, then asks the database worker to purge.</summary>
    private static void OnWorldThread(ToolCall call, CharPurgeRequest request, Caller caller, AccountCharacter target)
    {
        try
        {
            var cached = PlayerManager.ProcessDeletedPlayer(target.Guid);
            if (!cached)
            {
                ModManager.Log("Lifestone char purge: " + target.Name + " was not in ACE's offline-player caches (ProcessDeletedPlayer returned false); purging its rows", ModManager.LogLevel.Info);
            }

            // The caller's session keeps the account's characters it read at login and sends them again when the caller logs off
            // (Session.cs:260-273). ACE drops a character whose deletion has run out from that list the same way, right after
            // ProcessDeletedPlayer (Session.CheckCharactersForDeletion, :196-210). Without this the list at the next log off would
            // still show the purged character, and a stale entry whose delay had run out would be saved again by that very method.
            caller.Session.Characters.RemoveAll(c => c.Id == target.Guid);

            DatabaseManager.Shard.GetCharacter(target.Guid, row => PurgeOnShardWorker(call, request, caller, target, row));
        }
        catch (Exception e)
        {
            call.Crashed(e);
        }
    }

    /// <summary>Runs on the shard database's worker: checks the fresh row, purges, verifies, answers.</summary>
    private static void PurgeOnShardWorker(ToolCall call, CharPurgeRequest request, Caller caller, AccountCharacter target, ShardCharacter? row)
    {
        try
        {
            // The same rules against the fresh row alone: a row that is gone or on another account is not on the account.
            AccountCharacter[] fresh = row is not null && row.AccountId == caller.AccountId ? [Describe(row)] : [];
            var again = CharPurgeRules.Check(request, caller.Name, caller.Guid, fresh);
            if (!again.Allowed)
            {
                // The caches were emptied of this character on the world thread already (OnWorldThread): say so.
                var refusal = CharPurgeRules.WithRestartNote(again.Refusal ?? new ToolError(ToolErrors.Failed, "the purge rules gave neither a refusal nor a character"));
                ModManager.Log("Lifestone char purge: " + target.Name + " was refused after ACE's caches dropped it (" + refusal.Code + "); ACE needs a restart for that character to be known again", ModManager.LogLevel.Warn);
                call.Fail(refusal);
                return;
            }

            ShardDatabaseOfflineTools.PurgeCharacter(target.Guid, out var characters, out var playerBiotas, out var possessions, "@" + ToolsWire.Command + " " + ToolVerbs.Char + " " + ToolVerbs.CharPurge);
            var remains = DatabaseManager.Shard.BaseDatabase.GetCharacter(target.Guid) is not null;
            var (data, error) = CharPurgeRules.Outcome(target.Name, characters, playerBiotas, possessions, remains);
            if (data is null)
            {
                var failure = error ?? new ToolError(ToolErrors.Failed, "the purge gave neither a result nor an error");
                if (remains)
                {
                    // The rows are still there but the caches no longer hold the character: ACE does not know it until it restarts.
                    failure = CharPurgeRules.WithRestartNote(failure);
                    ModManager.Log("Lifestone char purge: " + target.Name + " is still in the database after the purge; ACE needs a restart for that character to be known again", ModManager.LogLevel.Warn);
                }

                call.Fail(failure);
                return;
            }

            call.Succeed(request, data, data.Describe());
        }
        catch (Exception e)
        {
            call.Crashed(e);
        }
    }

    private static AccountCharacter Describe(ShardCharacter row) =>
        new(row.Id, row.Name, row.IsDeleted, row.DeleteTime, PlayerManager.GetOnlinePlayer(row.Id) is not null);

    private sealed record Caller(string Name, uint Guid, uint AccountId, Session Session);
}

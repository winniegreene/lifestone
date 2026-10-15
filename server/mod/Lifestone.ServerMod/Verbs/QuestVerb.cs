// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/AdminCommands.cs Handleqst (AGPL-3.0)
// with changes: the call sequences only (QuestManager.Stamp, QuestManager.Erase and the registry's own row), at Player
// level for the requesting player's own character, never a targeted one; the name comes from the shared parser (a
// bounded character set, no '@' comment, no '*' erase-all); a stamp is QuestManager.Stamp, the call a quest script's
// StampQuest emote makes, and never the admin command's forced completion count; an erase of a quest the character does
// not hold is an answer, not an error; a player who is logging out is refused as busy; one machine-readable reply with the
// completions before and after.
using ACE.Server.Managers;
using ACE.Server.Mods;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// <c>@lifestone quest stamp|erase|has &lt;name&gt;</c>: one of the calling player's own quests (<see cref="QuestData"/>), so
/// a test or the tools panel can mark a quest done and clear it again without database access or admin rights.
/// </summary>
/// <remarks>
/// <para>ACE's admin <c>@qst</c> (AdminCommands.cs:3571-3730) acts on the targeted creature with QuestManager.Update,
/// SetQuestCompletions and Erase. This verb acts on the requesting player's own QuestManager (Creature.cs:29) only, and
/// <c>stamp</c> calls QuestManager.Stamp (QuestManager.cs:484-488), which is what ACE's quest scripts call for a StampQuest
/// emote (EmoteManager.cs:1265-1280): Update adds the row with one completion, or adds one completion to the row it finds
/// unless the quest's world definition has reached its maximum solves (IsMaxSolves, :281-292), when it leaves the row alone.
/// <c>erase</c> is QuestManager.Erase (:407-433), which removes the row; <c>has</c> reads it. Update and Erase mark the
/// character changed (CharacterChangesDetected), so ACE saves the registry on its own schedule; the mod writes nothing.</para>
/// <para>The command runs on ACE's world thread, as <c>@qst</c> does. The registry is guarded by the player's
/// CharacterDatabaseLock (CharacterExtensions.cs:347-415), and every call here runs to completion before the reply, so the
/// counts are this request's alone unless another thread changes the same quest between two of the calls.</para>
/// <para>A gate that tests a quest, such as a portal's QuestRestriction, does more than look for the row: ACE's portal needs
/// the quest held and not solvable again (Portal.cs:246-255, QuestManager.GetNextSolveTime :334-364). SentryTaskComplete
/// allows one solve, so one stamp opens the Academy's "Exit to Holtburg" portal.</para>
/// </remarks>
internal static class QuestVerb
{
    /// <summary>Runs <paramref name="request"/> on <paramref name="player"/>'s own quest registry and answers once with the counts.</summary>
    public static void Run(ToolCall call, Player player, QuestRequest request)
    {
        if (QuestChange.Refusal(request, player.IsLoggingOut) is { } refusal)
        {
            call.Fail(refusal);
            return;
        }

        var quests = player.QuestManager;
        var before = Completions(quests, request.Name);
        switch (request.Action)
        {
            case QuestAction.Stamp:
                quests.Stamp(request.Name);
                break;
            case QuestAction.Erase:
                quests.Erase(request.Name);
                break;
            case QuestAction.Has:
                break;
            default:
                throw new InvalidOperationException("quest: no handling for the action " + request.Action);
        }

        var data = QuestChange.Build(request, before, Completions(quests, request.Name));
        if (QuestChange.Check(request, data) is { } error)
        {
            ModManager.Log("Lifestone tool failed: verb=quest: " + error.Message, ModManager.LogLevel.Error);
            call.Fail(error);
            return;
        }

        call.Succeed(request, data, data.Describe());
    }

    /// <summary>The completions of the player's registry row for <paramref name="name"/> (matched ignoring case), or null without one.</summary>
    private static int? Completions(QuestManager quests, string name) => quests.GetQuest(name)?.NumTimesCompleted;
}

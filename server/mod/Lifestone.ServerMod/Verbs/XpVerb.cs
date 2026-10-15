// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/DeveloperCommands.cs HandleGrantXp (AGPL-3.0)
// with changes: the call sequence only (Player.GrantXP with XpType.Admin and ShareType.None), at Player level for the
// requesting player only; the amount comes from the shared parser (digits, 1 to 999,999,999,999, @grantxp's own limit);
// the experience before is read and the reply sent on the player's action queue, around ACE's own update, so the reply
// reports what ACE added for this request; no audit-channel broadcast; one machine-readable reply.
using ACE.Entity.Enum;
using ACE.Server.Entity.Actions;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary><c>@lifestone xp &lt;amount&gt;</c>: adds experience to the player (<see cref="XpData"/>).</summary>
/// <remarks>
/// <para>ACE's <c>@grantxp</c> (DeveloperCommands.cs:787-827) calls Player.GrantXP (Player_Xp.cs:53-82), which does not
/// add the experience itself: it queues UpdateXpAndLevel on the player's action queue (:72). That adds at most what
/// is left to the maximum level (:99-101), sends the TotalExperience and AvailableExperience updates and levels up
/// (CheckForLevelup, :276).</para>
/// <para>The mod reads the experience before on that same queue, just ahead of ACE's update, and queues its reply right
/// behind it. The queue runs its actions one at a time, in order, on one thread (ActionQueue.cs RunActions), and the
/// three are queued together on the world thread, so nothing runs between them: <c>added</c> is exactly what this
/// request's UpdateXpAndLevel added, even when other experience (a second <c>xp</c> in the same world tick, a kill's
/// UpdateXpAndLevel) is queued before or after it. Reading the experience when the command runs would count those
/// too. The count is <see cref="XpGrant"/>'s (unit tested). <c>added</c> is less than <c>requested</c> at the level
/// cap, and 0 for an Olthoi player, whom GrantXP skips (:55-61).</para>
/// <para>ShareType.None keeps it from the fellowship and allegiance shares; XpType.Admin is @grantxp's type (no item
/// experience, :79-81). A player who is logging out is refused as busy: ACE may never run the queued reply.</para>
/// </remarks>
internal static class XpVerb
{
    /// <summary>Adds the requested experience to <paramref name="player"/> and answers once ACE has.</summary>
    public static void Run(ToolCall call, Player player, XpRequest request)
    {
        if (player.IsLoggingOut)
        {
            call.Fail(ToolErrors.Busy, "you are logging out");
            return;
        }

        var grant = new XpGrant(request);
        player.EnqueueAction(new ActionEventDelegate(() => grant.Before(player.TotalExperience ?? 0)));
        player.GrantXP(request.Amount, XpType.Admin, ShareType.None);
        player.EnqueueAction(new ActionEventDelegate(() =>
        {
            try
            {
                var data = grant.After(player.TotalExperience ?? 0, player.AvailableExperience ?? 0, player.Level ?? 1);
                call.Succeed(request, data, data.Describe());
            }
            catch (Exception e)
            {
                call.Crashed(e);
            }
        }));
    }
}

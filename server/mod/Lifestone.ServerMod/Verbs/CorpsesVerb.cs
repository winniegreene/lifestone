// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Entity/Landblock.cs AddWorldObjectInternal, the corpse_spam_limit block (:905-919) (AGPL-3.0)
// with changes: the call (setting a corpse's TimeToRot to Corpse.EmptyDecayTime so the landblock's own decay removes it) only; run at Player level for
// every player corpse in the requesting player's current landblock named "Corpse of " and the caller's own name (Core/CorpseRotRules.cs), not for the
// oldest one over a limit; it runs as an action on that landblock's queue, as ACE's own limit runs on it; one machine-readable reply with the counts.
using System.Globalization;
using ACE.Server.Entity.Actions;
using ACE.Server.Mods;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;
using AceLandblock = ACE.Server.Entity.Landblock;

namespace Lifestone.ServerMod;

/// <summary>
/// <c>@lifestone corpses rot</c>: sets the caller's own character's corpses in the landblock the caller is in to rot, so ACE's decay removes them
/// (<see cref="CorpsesData"/>). It exists to clear the corpses of earlier generations of a re-created test character, which the current character
/// cannot loot and the database must not be edited for. It changes nothing but those corpses' time to rot.
/// </summary>
/// <remarks>
/// <para><b>What ACE does with a corpse whose time to rot is low.</b> A landblock's heartbeat, every 5 seconds (Landblock.cs:99, :516-529), calls
/// <c>Decay</c> on each decayable object in it (WorldObject_Decay.cs:27-46, :49). A corpse counts its time to rot down by the time since the last
/// heartbeat (:84-92), but only once its inventory has loaded (:72-73). At zero or below it is done, unless a player has it open (:101-105): ACE
/// then puts a player's corpse's contents on the ground at the corpse's place (:111-134; each is then an ordinary object on the landblock with
/// the 5 minutes ACE gives an object with no time to rot, :18, :58-65, after which ACE destroys it, removing its database record, WorldObject.cs:837-893) and
/// fades the corpse out and destroys it a second later (:136-147). The corpse is destroyed by ACE, not by the mod, and its rows leave the database through ACE's own
/// destroy. An open corpse, or one whose inventory has not loaded, waits.</para>
/// <para><b>What the mod does.</b> It sets <c>TimeToRot</c> to <c>Corpse.EmptyDecayTime</c> (Corpse.cs:27, 15 seconds), which is exactly what ACE does to the
/// oldest corpse of a player who leaves more than <c>corpse_spam_limit</c> of them in a landblock (Landblock.cs:905-919; the property's default is 15,
/// PropertyManager.cs:622). That is the only thing it changes: it never calls Destroy, never touches the database and never moves or opens a corpse.</para>
/// <para><b>Which corpses (Core/CorpseRotRules.cs).</b> Player corpses only, told from a creature's the way ACE's own player-corpse code does, by
/// <c>Level.HasValue</c> (a Level is set only for a player's corpse, Corpse.cs:115), and not <c>Corpse.IsMonster</c> (a field set when ACE makes a creature's
/// corpse, Creature_Death.cs:584; false again for a corpse loaded from the database, so Level is what matters there); whose name is exactly "Corpse of " and the
/// caller's own name (Creature_Death.cs:495-496 makes the name "{prefix} of {Name}"). Every generation of a re-created character has the same name, so those
/// of the generations the live gate deleted match; a corpse of any other name, or of a creature, never does. Only the landblock the caller is in is searched:
/// corpses in other landblocks are not looked at, and they do not count down while their landblock is unloaded.</para>
/// <para><b>On which thread.</b> The command runs on ACE's world thread, in the inbound-message phase (WorldManager.cs:363), which is not concurrent with the
/// landblocks' ticks (:376), but a landblock's objects belong to its own queue: ACE changes a corpse's time to rot from an action on it
/// (AddWorldObjectInternal, which holds the spam-limit block, runs on the landblock's thread: a corpse loaded from the database is added from
/// the landblock's action queue, Landblock.cs:258-262; the queue is run first in the landblock's tick, :445, ahead of the heartbeat's decay, :516-529). So the mod queues one action on the landblock (<c>EnqueueAction</c>, Landblock.cs:796-799), as <see cref="XpVerb"/> queues
/// on the player's, reads the landblock's objects there (<c>GetWorldObjectsForPhysicsHandling</c>, :1019-1026, which first settles the landblock's pending
/// additions and removals; copied to a list before anything changes) and answers from it. The player may leave before the action runs; the landblock the
/// command found them in is the one searched. If the landblock unloads first, ACE drops the action (Landblock.cs:1151, :1176) and, as for <c>xp</c>, the client's own timeout reports it.</para>
/// <para>The reply says how many corpses matched and how many were set; a matching corpse that is already at or under 15 seconds, or already decayed, is found and left alone.</para>
/// </remarks>
internal static class CorpsesVerb
{
    /// <summary>Queues the search of <paramref name="player"/>'s current landblock; answers from the landblock's action queue.</summary>
    public static void Rot(ToolCall call, Player player, CorpsesRotRequest request)
    {
        var landblock = player.CurrentLandblock ?? throw new InvalidOperationException("the player has no landblock");
        var callerName = player.Name;
        var landblockId = ToolsWire.FormatCell(PlaceRules.LandblockOf(landblock.Id.Raw));
        landblock.EnqueueAction(new ActionEventDelegate(() => OnLandblock(call, request, landblock, callerName, landblockId)));
    }

    /// <summary>Runs on the landblock's action queue: the rules over its corpses, then the one change.</summary>
    private static void OnLandblock(ToolCall call, CorpsesRotRequest request, AceLandblock landblock, string callerName, string landblockId)
    {
        try
        {
            var corpses = landblock.GetWorldObjectsForPhysicsHandling().OfType<Corpse>().ToList();
            var plan = CorpseRotRules.Plan(
                corpses.Select(c => new CorpseCandidate(c.Name, c.Level.HasValue, c.IsMonster, c.TimeToRot)),
                callerName,
                Corpse.EmptyDecayTime);
            foreach (var index in plan.SetIndexes)
            {
                var corpse = corpses[index];
                var before = corpse.TimeToRot;
                corpse.TimeToRot = Corpse.EmptyDecayTime;
                var was = before is { } seconds ? seconds.ToString("0.###", CultureInfo.InvariantCulture) : "unset";
                ModManager.Log(
                    "Lifestone corpses rot: " + corpse.Name + " (0x" + corpse.Guid.Full.ToString("X8", CultureInfo.InvariantCulture) + ") in landblock " + landblockId
                        + ": TimeToRot " + was + " -> " + Corpse.EmptyDecayTime.ToString("0.###", CultureInfo.InvariantCulture),
                    ModManager.LogLevel.Info);
            }

            var data = new CorpsesData(plan.Found, plan.Rotted, landblockId);
            call.Succeed(request, data, data.Describe());
        }
        catch (Exception e)
        {
            call.Crashed(e);
        }
    }
}

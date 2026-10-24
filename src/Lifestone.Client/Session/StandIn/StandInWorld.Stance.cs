// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Creature_Combat.cs (SetCombatMode 60-105, HandleSwitchToPeaceMode 107-125, HandleSwitchToMeleeCombatMode 161-183, GetCombatStance 265-285), Player_Combat.cs (HandleActionChangeCombatMode 737-776) and
// Player_Inventory.cs (TryShuffleStance 252-290, the dequip swap 436-452) (AGPL-3.0) [with changes: the stance rule is Lifestone.Client.Session.Contracts.CombatStances over the read model; ACE's animation delays, the missile reload and the
// "Action cancelled" flash are not played: the stand-in sends the stance motion, then the mode's private update, at once].
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.StandIn;

internal sealed partial class StandInWorld
{
    /// <summary>
    /// <c>HandleActionChangeCombatMode</c> then <c>SetCombatMode</c>: the mode ACE ends in (<see cref="CombatStances.Settle"/>: a mode the wielded weapons do not fit ends in peace), the player's stance motion for it
    /// (<see cref="CombatStances.ForMode"/>: by weapon, HandCombat only with nothing in the hands), then the private int update 0x02CD for property 40. The motion comes first, as ACE's <c>ExecuteMotionPersist</c> precedes its <c>EnqueueSend</c>.
    /// </summary>
    private void EnterMode(IAcSessionSink sink, CombatMode requested)
    {
        var state = _reducer.State;
        var mode = CombatStances.Settle(requested, state);
        if (mode != requested)
        {
            Placeholder("m4-standin-mode-revert", "ACE answers ActionCancelled and plays the asked mode's stance for the animation's length before it puts the player in peace (Player_Combat.cs 745-760); the stand-in goes to peace at once");
        }

        var stance = CombatStances.ForMode((int)mode, state, out var assumed);
        if (assumed)
        {
            Placeholder("m4-standin-combat-style", "the stand-in tells a bow from a crossbow, an atlatl or a sling by nothing (the weenie description has no DefaultCombatStyle): a launcher in the missile slot stands in BowCombat");
        }

        SendPlayerMotion(sink, stance, MotionCommands.Ready);

        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, (uint)mode);
    }

    /// <summary><c>TryShuffleStance</c> after a wield: a player in a combat mode moves to the new weapon's mode (the read model already holds the item: the wield event came first).</summary>
    private void ShuffleAfterWield(IAcSessionSink sink, uint location)
    {
        var state = _reducer.State;
        if (CombatStances.ModeAfterWield(location, state.Self.Qualities.CombatMode, state) is { } mode)
        {
            EnterMode(sink, mode);
        }
    }

    /// <summary>The dequip swap after an unwield: a player in a combat mode goes to melee (hand combat with empty hands), or to peace when the arrows of a missile mode left; <paramref name="location"/> is where the item was.</summary>
    private void ShuffleAfterUnwield(IAcSessionSink sink, uint location)
    {
        var state = _reducer.State;
        if (CombatStances.ModeAfterUnwield(location, state.Self.Qualities.CombatMode, state) is { } mode)
        {
            EnterMode(sink, mode);
        }
    }
}

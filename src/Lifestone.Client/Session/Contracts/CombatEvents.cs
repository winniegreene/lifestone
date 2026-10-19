namespace Lifestone.Client.Session.Contracts;

// Combat events (M3 1.2 lane s3): the damage, evasion, kill and death notices, and the combat-mode change. Published by GameStateReducer.Combat.cs.

/// <summary>The combat modes (<c>PropertyInt</c> 40, ACE's <c>CombatMode</c>; retail's <c>COMBAT_MODE</c>), as <see cref="PlayerQualities.CombatMode"/> holds them.</summary>
public static class CombatModes
{
    /// <summary>The quality the combat mode lives in (<c>PropertyInt</c> 40); <c>0x02CD PrivateUpdatePropertyInt</c> keeps it current.</summary>
    public const uint Property = 40;

    /// <summary>Not in combat (also the value before the server has said anything).</summary>
    public const int NonCombat = 1;

    /// <summary>Melee.</summary>
    public const int Melee = 2;

    /// <summary>Missile.</summary>
    public const int Missile = 4;

    /// <summary>Magic.</summary>
    public const int Magic = 8;
}

/// <summary>
/// The chat types retail's combat handlers pass to <c>ClientSystem::AddTextToScroll</c> (ACE's <c>ChatMessageType</c>): the lines the combat events add to the chat
/// carry them in <see cref="ChatLine.ChatType"/> (their <see cref="ChatLine.Kind"/> is <see cref="ChatKind.System"/>).
/// </summary>
public static class CombatChatTypes
{
    /// <summary>Broadcast (0): the death notices (<c>HandleVictimNotificationEvent</c>, <c>HandlePlayerDeathEvent</c>).</summary>
    public const uint Broadcast = 0x00;

    /// <summary>CombatEnemy (0x15): what hit the player, or what the player evaded (<c>HandleDefenderNotificationEvent</c>, <c>HandleEvasionDefenderNotificationEvent</c>).</summary>
    public const uint CombatEnemy = 0x15;

    /// <summary>CombatSelf (0x16): the player's own hit or evaded attack (<c>HandleAttackerNotificationEvent</c>, <c>HandleEvasionAttackerNotificationEvent</c>).</summary>
    public const uint CombatSelf = 0x16;
}

/// <summary>Which of the four combat notices a <see cref="CombatEvent"/> is.</summary>
public enum CombatEventKind
{
    /// <summary>GameEvent 0x01B1 AttackerNotification: the player's attack landed.</summary>
    AttackLanded,

    /// <summary>GameEvent 0x01B2 DefenderNotification: an attack landed on the player.</summary>
    DefenseHit,

    /// <summary>GameEvent 0x01B3 EvasionAttackerNotification: the player's attack was evaded.</summary>
    AttackEvaded,

    /// <summary>GameEvent 0x01B4 EvasionDefenderNotification: the player evaded an attack.</summary>
    DefenseEvaded,
}

/// <summary>
/// One of the combat notices (GameEvents 0x01B1 to 0x01B4), with the chat line retail composes for it (already in the chat when this is published, unless the line is
/// empty: see <see cref="Text"/>). Every field a panel or a tracker needs is here; nothing is re-read from the state.
/// </summary>
/// <param name="Kind">Which notice.</param>
/// <param name="Who">The other party: the defender for <see cref="CombatEventKind.AttackLanded"/> and <see cref="CombatEventKind.AttackEvaded"/>, the attacker for the other two.</param>
/// <param name="DamageType">ACE's <c>DamageType</c> flags; 0 for the evasion notices (they carry none).</param>
/// <param name="Damage">The damage dealt or taken; 0 for the evasion notices.</param>
/// <param name="Percent">The damage as a fraction of the one hurt's health (0 for the evasion notices).</param>
/// <param name="Critical">True for a critical hit.</param>
/// <param name="BodyLocation">The body part hit (retail's <c>BodyPart</c>, ACE's <c>CombatBodyPart</c>) for <see cref="CombatEventKind.DefenseHit"/>; -1 (UNDEFINED) for the others.</param>
/// <param name="Evaded">True for the two evasion notices.</param>
/// <param name="AttackConditions">ACE's <c>AttackConditions</c> flags: 1 critical protection augmentation, 2 recklessness, 4 sneak attack (0 for the evasion notices).</param>
/// <param name="Text">The chat line composed (without retail's trailing newline: each chat line is a line already).</param>
public readonly record struct CombatEvent(
    CombatEventKind Kind,
    string Who,
    uint DamageType,
    uint Damage,
    double Percent,
    bool Critical,
    int BodyLocation,
    bool Evaded,
    ulong AttackConditions,
    string Text) : IGameEvent;

/// <summary>The player's combat mode changed (<see cref="PlayerQualities.CombatMode"/> already holds the new value). Raised by the 0x02CD for property 40 when it changes the stored value.</summary>
/// <param name="Previous">The mode before, or 0 when none was stored (the description did not carry one).</param>
/// <param name="Mode">The mode now (<see cref="CombatModes"/>).</param>
public readonly record struct CombatModeChanged(int Previous, int Mode) : IGameEvent;

/// <summary>Which message raised a <see cref="PlayerDied"/>.</summary>
public enum DeathNoticeSource
{
    /// <summary>GameEvent 0x01AC VictimNotification (sent to the victim alone; wins, see <see cref="PlayerDied"/>).</summary>
    VictimNotification,

    /// <summary>GameMessage 0x019E PlayerKilled, the broadcast, with the player as the victim (used only when no 0x01AC came for the same death).</summary>
    PlayerKilledBroadcast,
}

/// <summary>
/// The player died: raised once per death. ACE sends the victim GameEvent 0x01AC first and then the 0x019E broadcast, which names the victim too; retail shows the 0x01AC text
/// and shows nothing for a 0x019E that names the player (<c>HandlePlayerDeathEvent</c>), so 0x01AC wins: whichever of the two arrives first raises this event and the other, within
/// <see cref="DeathPairing.PairWindowSeconds"/> seconds, is taken as the same death and raises nothing.
/// </summary>
/// <param name="Text">The server's death message ("You have been killed by X").</param>
/// <param name="Killer">The killer's object id when the 0x019E named one (0 for none or when only the 0x01AC came).</param>
/// <param name="Source">The message that raised it.</param>
public readonly record struct PlayerDied(string Text, uint Killer, DeathNoticeSource Source) : IGameEvent;

/// <summary>GameEvent 0x01AD KillerNotification: the player killed something (a player: ACE sends it for player kills only). Raised after its text is in the chat.</summary>
/// <param name="Text">The server's message ("You have killed X").</param>
public readonly record struct PlayerKilledSomething(string Text) : IGameEvent;

/// <summary>The constants of the death pairing (<see cref="PlayerDied"/>).</summary>
public static class DeathPairing
{
    /// <summary>How long after one death message the other (0x01AC or 0x019E) is still taken as the same death (session-clock seconds).</summary>
    public const double PairWindowSeconds = 5.0;
}

/// <summary>
/// The player was attacked (0x01B2 or 0x01B4) in melee or missile combat mode with the AutoTarget option on:
/// <c>ClientCombatSystem::AutoTarget</c> (11.6096 0x0056C9D0) wants the attacker selected when nothing is. The selection listens
/// (<c>ObjectSelection.Attach</c>); the reducer does not know the selection.
/// </summary>
/// <param name="Attacker">The attacker's object id, or 0 when no object of the notice's name is known (retail then selects the closest compass item: <c>TODO(m3-autotarget-selectnext)</c>).</param>
public readonly record struct AutoTargetRequested(uint Attacker) : IGameEvent;

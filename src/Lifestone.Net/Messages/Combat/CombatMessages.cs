namespace Lifestone.Net.Messages;

// The combat and death notifications (M3 1.2 s0). CombatCodecs holds the parsers and the layouts. The texts are decoded as
// fields only; lane s3 composes the chat lines from them.

/// <summary>GameEvent 0x01AC VictimNotification: the player died; the text is the server's death message ("You have been killed by X").</summary>
/// <param name="Text">The death message.</param>
public readonly record struct VictimNotificationEvent(string Text);

/// <summary>GameEvent 0x01AD KillerNotification: the player killed something; the text is the server's death message.</summary>
/// <param name="Text">The death message.</param>
public readonly record struct KillerNotificationEvent(string Text);

/// <summary>GameEvent 0x01B1 AttackerNotification: the player's own hit landed.</summary>
/// <param name="DefenderName">The name of what was hit.</param>
/// <param name="DamageType">ACE's <c>DamageType</c> flags.</param>
/// <param name="Percent">The damage as a fraction of the defender's health (a double on the wire).</param>
/// <param name="Damage">The damage dealt.</param>
/// <param name="CriticalHit">True for a critical hit.</param>
/// <param name="AttackConditions">ACE's <c>AttackConditions</c> flags (a u64 on the wire).</param>
public readonly record struct AttackerNotificationEvent(string DefenderName, uint DamageType, double Percent, uint Damage, bool CriticalHit, ulong AttackConditions);

/// <summary>GameEvent 0x01B2 DefenderNotification: a hit landed on the player.</summary>
/// <param name="AttackerName">The attacker's name.</param>
/// <param name="DamageType">ACE's <c>DamageType</c> flags.</param>
/// <param name="Percent">The damage as a fraction of the player's health (a double on the wire).</param>
/// <param name="Damage">The damage taken.</param>
/// <param name="DamageLocation">ACE's <c>DamageLocation</c> (the body part).</param>
/// <param name="CriticalHit">True for a critical hit.</param>
/// <param name="AttackConditions">ACE's <c>AttackConditions</c> flags (a u64 on the wire).</param>
public readonly record struct DefenderNotificationEvent(
    string AttackerName,
    uint DamageType,
    double Percent,
    uint Damage,
    uint DamageLocation,
    bool CriticalHit,
    ulong AttackConditions);

/// <summary>GameEvent 0x01B3 EvasionAttackerNotification: the player's attack was evaded.</summary>
/// <param name="DefenderName">The name of what evaded it.</param>
public readonly record struct EvasionAttackerNotificationEvent(string DefenderName);

/// <summary>GameEvent 0x01B4 EvasionDefenderNotification: the player evaded an attack.</summary>
/// <param name="AttackerName">The attacker's name.</param>
public readonly record struct EvasionDefenderNotificationEvent(string AttackerName);

/// <summary>GM 0x019E PlayerKilled: a player died (broadcast, the victim included).</summary>
/// <param name="Text">The death message.</param>
/// <param name="Victim">The victim's GUID.</param>
/// <param name="Killer">The killer's GUID.</param>
public readonly record struct PlayerKilledMsg(string Text, uint Victim, uint Killer);

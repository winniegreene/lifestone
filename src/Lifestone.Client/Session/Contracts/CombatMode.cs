namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The combat modes <see cref="IActionApi.SetCombatMode"/> asks for (ACE's <c>CombatMode</c>, retail's <c>COMBAT_MODE</c>, the argument of
/// <c>CM_Combat::Event_ChangeCombatMode</c>): the values are the wire values, one bit each. The server keeps the mode in
/// <c>PropertyInt</c> 40 (<see cref="CombatModes"/>, <see cref="PlayerQualities.CombatMode"/>), where the same numbers read back as <see langword="int"/>s.
/// </summary>
public enum CombatMode : uint
{
    /// <summary>Not in combat (peace mode).</summary>
    NonCombat = 1,

    /// <summary>Melee.</summary>
    Melee = 2,

    /// <summary>Missile.</summary>
    Missile = 4,

    /// <summary>Magic.</summary>
    Magic = 8,
}

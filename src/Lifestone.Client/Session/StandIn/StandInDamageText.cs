// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/Entity/Strings.cs (GetAttackVerb, GetDeathMessage, the death-message lists) (AGPL-3.0)
// [with changes: only the killer's sentence and the single verb are kept (the stand-in's player is always the attacker); the damage type is read from the spell's name].
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.StandIn;

/// <summary>ACE's <c>DamageType</c> for the elements the stand-in's war spells deal (<c>ACE.Entity.Enum.DamageType</c>).</summary>
internal enum StandInDamageType
{
    /// <summary>ACE's <c>Undef</c>: the default verb "hit" and the general death message.</summary>
    Undef,
    Slash,
    Pierce,
    Bludgeon,
    Cold,
    Fire,
    Acid,
    Electric,
    Nether,
}

/// <summary>
/// The words of a spell hit and of a kill, as ACE writes them (M3 3.1 fb): the attack verb (<c>Strings.GetAttackVerb</c>: the damage type and the share of the target's maximum health the blow took) and the
/// killer's sentence (<c>Strings.GetDeathMessage</c>: one of the damage type's messages, chosen at random). The damage type of a spell is ACE's world database's, which the dat does not carry:
/// <see cref="TypeOf"/> reads it from the spell's name (<c>TODO(m3-standin-spell-damage)</c>).
/// </summary>
internal static class StandInDamageText
{
    /// <summary>The damage type of a war spell, read from its name (a placeholder: "Flame" is fire, "Frost" cold, "Lightning" electric, "Acid", "Force" and "Shock Wave" bludgeon, "Blade" slash, "Nether" nether); <see cref="StandInDamageType.Undef"/> for any other.</summary>
    internal static StandInDamageType TypeOf(SpellInfo info)
    {
        var name = info.Name;
        if (name.Contains("Flame", StringComparison.Ordinal) || name.Contains("Fire", StringComparison.Ordinal))
        {
            return StandInDamageType.Fire;
        }

        if (name.Contains("Frost", StringComparison.Ordinal) || name.Contains("Ice", StringComparison.Ordinal) || name.Contains("Cold", StringComparison.Ordinal))
        {
            return StandInDamageType.Cold;
        }

        if (name.Contains("Lightning", StringComparison.Ordinal) || name.Contains("Electric", StringComparison.Ordinal))
        {
            return StandInDamageType.Electric;
        }

        if (name.Contains("Acid", StringComparison.Ordinal))
        {
            return StandInDamageType.Acid;
        }

        if (name.Contains("Force", StringComparison.Ordinal) || name.Contains("Shock Wave", StringComparison.Ordinal))
        {
            return StandInDamageType.Bludgeon;
        }

        if (name.Contains("Blade", StringComparison.Ordinal))
        {
            return StandInDamageType.Slash;
        }

        return name.Contains("Nether", StringComparison.Ordinal) ? StandInDamageType.Nether : StandInDamageType.Undef;
    }

    /// <summary>ACE's single attack verb for a blow of <paramref name="percent"/> (the damage over the target's maximum health) of <paramref name="type"/>.</summary>
    internal static string Verb(StandInDamageType type, float percent)
    {
        var tier = percent > 0.5f ? 0 : percent > 0.25f ? 1 : percent > 0.1f ? 2 : 3;
        return type switch
        {
            StandInDamageType.Slash => Pick(tier, "mangle", "slash", "cut", "scratch"),
            StandInDamageType.Pierce => Pick(tier, "gore", "impale", "stab", "nick"),
            StandInDamageType.Bludgeon => Pick(tier, "crush", "smash", "bash", "graze"),
            StandInDamageType.Fire => Pick(tier, "incinerate", "burn", "scorch", "singe"),
            StandInDamageType.Cold => Pick(tier, "freeze", "frost", "chill", "numb"),
            StandInDamageType.Acid => Pick(tier, "dissolve", "corrode", "sear", "blister"),
            StandInDamageType.Electric => Pick(tier, "blast", "jolt", "shock", "spark"),
            StandInDamageType.Nether => Pick(tier, "eradicate", "wither", "twist", "scar"),
            _ => "hit", // ACE's default case
        };
    }

    private static string Pick(int tier, string a, string b, string c, string d) => tier switch { 0 => a, 1 => b, 2 => c, _ => d };

    /// <summary>The killer's sentences of <paramref name="type"/> (<c>DeathMessage.Killer</c>; <c>{0}</c> is the victim's name).</summary>
    internal static IReadOnlyList<string> KillerMessages(StandInDamageType type) => type switch
    {
        StandInDamageType.Slash => Slashing,
        StandInDamageType.Pierce => Piercing,
        StandInDamageType.Bludgeon => Bludgeoning,
        StandInDamageType.Fire => Fire,
        StandInDamageType.Cold => Ice,
        StandInDamageType.Acid => Acid,
        StandInDamageType.Electric => Lightning,
        StandInDamageType.Nether => Void,
        _ => General,
    };

    private static readonly string[] Slashing =
    [
        "You split {0} apart!",
        "You cleave {0} in twain!",
        "{0} is torn to ribbons by your assault!",
        "Your killing blow nearly turns {0} inside-out!",
    ];

    private static readonly string[] Piercing =
    [
        "You run {0} through!",
        "{0} is fatally punctured!",
        "{0}'s perforated corpse falls before you!",
        "{0}'s death is preceded by a sharp, stabbing pain!",
    ];

    private static readonly string[] Bludgeoning =
    [
        "You beat {0} to a lifeless pulp!",
        "{0} is shattered by your assault!",
        "You flatten {0}'s body with the force of your assault!",
        "The thunder of crushing {0} is followed by the deafening silence of death!",
    ];

    private static readonly string[] Fire =
    [
        "You bring {0} to a fiery end!",
        "{0} is reduced to cinders!",
        "{0} is incinerated by your assault!",
        "{0}'s seared corpse smolders before you!",
    ];

    private static readonly string[] Ice =
    [
        "Your attack stops {0} cold!",
        "Your assault sends {0} to an icy death!",
        "{0} suffers a frozen fate!",
    ];

    private static readonly string[] Acid =
    [
        "{0} is liquified by your attack!",
        "{0}'s last strength dissolves before you!",
        "You reduce {0} to a sizzling, oozing mass!",
    ];

    private static readonly string[] Lightning =
    [
        "Blistered by lightning, {0} falls!",
        "Electricity tears {0} apart!",
        "Your lightning coruscates over {0}'s mortal remains!",
    ];

    private static readonly string[] Void =
    [
        "{0} is dessicated by your attack!",
        "{0}'s last strength withers before you!",
        "You reduce {0} to a drained, twisted corpse!",
    ];

    private static readonly string[] General =
    [
        "You killed {0}!",
        "{0} died!",
    ];
}

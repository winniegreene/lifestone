using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Fighter;

namespace Lifestone.Client.Session.Vitals;

/// <summary>The posture the keeper rests in (<c>--vitals-rest-posture</c>).</summary>
public enum RestPosture
{
    /// <summary>Standing in peace (the default): ACE's stance factor 1.0.</summary>
    Stand,

    /// <summary>Sitting (MotionCommand Sitting 0x41000013): ACE multiplies health and stamina regeneration by 2.5 (<c>Creature_Vitals.cs</c> 210-236 at ACE 95ef426d).</summary>
    Sit,

    /// <summary>Lying down (MotionCommand Sleeping 0x41000014): ACE multiplies health and stamina regeneration by 3.0 (the same lines).</summary>
    Lie,
}

/// <summary>
/// The vitals keeper's options (M4 P5a lane C2; <see cref="VitalsKeeper"/>): the stamina and mana rules, the rest caps, the rest posture, the in-fight potion and the cast chance, parsed from the command line as
/// <see cref="RecoveryOptions"/> is. The health thresholds are the recovery rule's own (<c>--heal-below</c>, <c>--heal-until</c>, <c>--heal-chance</c>, <c>--heal-floor-chance</c>), read by
/// <see cref="RecoveryOptions.Apply"/> into <see cref="Recovery"/>, so the hunt and the keeper never disagree. A value that does not parse is reported in <see cref="Errors"/> and the option keeps its default.
/// </summary>
public sealed record VitalsOptions
{
    /// <summary>The default of <c>--vitals-stamina-below</c>: the stamina share, in percent, under which the keeper restores stamina before a run or an attack.</summary>
    public const int DefaultStaminaBelowPercent = 30;

    /// <summary>The default of <c>--vitals-stamina-until</c>: the named level a stamina rest or Revitalize goes on to.</summary>
    public const int DefaultStaminaUntilPercent = 80;

    /// <summary>The default of <c>--vitals-attack-stamina</c>: under this stamina share the keeper refuses an attack (<see cref="VitalsKeeper.MayAttack"/>).</summary>
    public const int DefaultAttackStaminaPercent = 15;

    /// <summary>
    /// The default of <c>--vitals-rest-cap-s</c>: the longest one rest lasts, in seconds of the session clock. Not retail: Lifestone's own bound. At ACE's 5 s tick a fighter standing in peace regains about 0.45 health
    /// and 3 stamina a tick (cache research, section 6), so 15% of 188 health is about 300 s standing and 100 s lying down; 600 s covers the standing worst case twice.
    /// </summary>
    public const double DefaultRestCapSeconds = 600;

    /// <summary>The default of <c>--vitals-infight-potion-below</c>: the health share under which the in-fight potion is drunk while the target is out of reach (the design's 40%).</summary>
    public const int DefaultInFightPotionBelowPercent = 40;

    /// <summary>The default of <c>--vitals-cast-chance</c>: the chance a Heal Self, Revitalize Self or Stamina to Mana Self level must reach (the buff cycle's 0.9, <c>BuffOptions.DefaultCastChance</c>).</summary>
    public const double DefaultCastChance = Mage.BuffOptions.DefaultCastChance;

    /// <summary>The default options.</summary>
    public static VitalsOptions Default { get; } = new();

    /// <summary>The health rule's options (the recovery rule's: below 75%, until 90%, the kit's chances).</summary>
    public RecoveryOptions Recovery { get; init; } = RecoveryOptions.Default;

    /// <summary>The stamina share, in percent, under which the keeper restores stamina (<c>--vitals-stamina-below</c>, 0 to 100).</summary>
    public int StaminaBelowPercent { get; init; } = DefaultStaminaBelowPercent;

    /// <summary>The stamina share, in percent, a started stamina restore goes on to (<c>--vitals-stamina-until</c>, 1 to 100, not under <see cref="StaminaBelowPercent"/>).</summary>
    public int StaminaUntilPercent { get; init; } = DefaultStaminaUntilPercent;

    /// <summary>The stamina share, in percent, under which an attack is refused (<c>--vitals-attack-stamina</c>, 0 to 100).</summary>
    public int AttackStaminaPercent { get; init; } = DefaultAttackStaminaPercent;

    /// <summary>The longest one rest lasts, in seconds (<c>--vitals-rest-cap-s</c>, above 0); over it the keeper ends <c>Blocked why=rest-cap:&lt;vital&gt;</c>.</summary>
    public double RestCapSeconds { get; init; } = DefaultRestCapSeconds;

    /// <summary>The posture of a rest (<c>--vitals-rest-posture stand|sit|lie</c>, default stand); sit and lie only in peace.</summary>
    public RestPosture Posture { get; init; } = RestPosture.Stand;

    /// <summary>True when <see cref="VitalsKeeper.TryInFightPotion"/> may drink (<c>--vitals-infight-potion on|off</c>, default off until P5b wires it; closes <c>m4-hunt-infight-heal</c> as the owner's call).</summary>
    public bool InFightPotion { get; init; }

    /// <summary>The health share, in percent, under which the in-fight potion is drunk (<c>--vitals-infight-potion-below</c>, 1 to 100).</summary>
    public int InFightPotionBelowPercent { get; init; } = DefaultInFightPotionBelowPercent;

    /// <summary>The chance a utility spell level must reach to be cast (<c>--vitals-cast-chance</c>, above 0 and at most 1).</summary>
    public double CastChance { get; init; } = DefaultCastChance;

    /// <summary>
    /// True for the fighters (M4 P5a C7): the Brawler and the Archer cast Revitalize Self and Stamina to Mana Self at a loss (Revitalize costs mana itself and the transfer gives back less than the pair took: round 5's Archer lost 130 mana to three Revitalizes between two transfers),
    /// so their mana order is a Mana Potion, then Stamina to Mana Self only at a high stamina (<see cref="StaminaUntilPercent"/>) with no potion left and at most <see cref="VitalsKeeper.FighterTransferCap"/> casts a pass, then a rest in peace; Revitalize Self serves stamina alone.
    /// False (the default) is the mage's order, unchanged: the transfer, then the rest. Lifestone's own; set by the host from the character (the buff scenario for a fighter, the live harm-heal leg for a caster swap).
    /// </summary>
    public bool FighterOrder { get; init; }

    /// <summary>These options in the fighters' order (<see cref="FighterOrder"/>).</summary>
    public VitalsOptions ForFighter() => this with { FighterOrder = true };

    /// <summary>Every problem found, one line each.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Reads the keeper's options and the recovery rule's out of the parsed arguments (the others are ignored).</summary>
    public static VitalsOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new VitalsOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            Apply(argument, errors, ref o);
        }

        if (o.StaminaUntilPercent < o.StaminaBelowPercent)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"--vitals-stamina-until: {o.StaminaUntilPercent}% is under --vitals-stamina-below {o.StaminaBelowPercent}%; the until level is raised to it"));
            o = o with { StaminaUntilPercent = o.StaminaBelowPercent };
        }

        var recovery = RecoveryOptions.FromArguments(args); // the --heal-* options, parsed and checked by their own parser
        errors.AddRange(recovery.Errors);
        return o with { Recovery = recovery with { Errors = [] }, Errors = errors };
    }

    /// <summary>Applies <paramref name="argument"/> when it is one of the keeper's own options; a bad value adds a line to <paramref name="errors"/> and leaves the option as it was.</summary>
    /// <returns>True when the argument is one of the keeper's options (good or bad).</returns>
    public static bool Apply(CommandLineArgument argument, List<string> errors, ref VitalsOptions options)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(options);
        var known = errors.Count;
        switch (argument.Name)
        {
            case "--vitals-stamina-below":
                options = options with { StaminaBelowPercent = Percent(argument, errors, known, options.StaminaBelowPercent, 0) };
                return true;
            case "--vitals-stamina-until":
                options = options with { StaminaUntilPercent = Percent(argument, errors, known, options.StaminaUntilPercent, 1) };
                return true;
            case "--vitals-attack-stamina":
                options = options with { AttackStaminaPercent = Percent(argument, errors, known, options.AttackStaminaPercent, 0) };
                return true;
            case "--vitals-infight-potion-below":
                options = options with { InFightPotionBelowPercent = Percent(argument, errors, known, options.InFightPotionBelowPercent, 1) };
                return true;
            case "--vitals-rest-cap-s":
                var cap = OptionValues.Double(argument, errors, options.RestCapSeconds);
                if (errors.Count == known && cap > 0 && double.IsFinite(cap))
                {
                    options = options with { RestCapSeconds = cap };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not a number of seconds above 0");
                }

                return true;
            case "--vitals-cast-chance":
                var chance = OptionValues.Double(argument, errors, options.CastChance);
                if (errors.Count == known && chance is > 0 and <= 1)
                {
                    options = options with { CastChance = chance };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not more than 0 and at most 1");
                }

                return true;
            case "--vitals-rest-posture":
                options = options with { Posture = OptionValues.Choice(argument, errors, options.Posture, ("stand", RestPosture.Stand), ("sit", RestPosture.Sit), ("lie", RestPosture.Lie)) };
                return true;
            case "--vitals-infight-potion":
                options = options with { InFightPotion = OptionValues.Switch(argument, errors, options.InFightPotion) };
                return true;
            default:
                return false;
        }
    }

    private static int Percent(CommandLineArgument argument, List<string> errors, int known, int fallback, int least)
    {
        var value = OptionValues.Int(argument, errors, fallback);
        if (errors.Count == known && value >= least && value <= 100)
        {
            return value;
        }

        if (errors.Count == known)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not {least} to 100 percent"));
        }

        return fallback;
    }
}

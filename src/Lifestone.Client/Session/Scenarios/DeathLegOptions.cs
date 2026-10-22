using Lifestone.Client.Options;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// Everything the game's death leg (<c>--login-check=bsd-death</c>, <c>DeathLegRunner</c>) reads from the command line for <see cref="DeathScenario"/> (M4 3.1 d2, D20), built without Godot so a test can prove the pass-through: the death loop's own options
/// (<see cref="DeathOptions"/>: <c>--death-decoys</c>, <c>--death-rebuff</c>, <c>--death-fight</c>, <c>--death-fight-kills</c>, <c>--death-fight-seconds</c> and the older ones), the buff cycle's (<see cref="BuffOptions"/>, the <c>--buff-*</c> options: the
/// rebuff stage is a buff cycle) and the fight's (<see cref="FightOptions"/>, built from the death options' <c>FightKills</c> and <c>FightSeconds</c>, as the buff and fight legs build theirs; the fight leg's own <c>--fight-*</c> options are not the death loop's).
/// A bad value in any of them fails the leg at its first frame, as in every other leg (<see cref="Error"/>).
/// </summary>
/// <param name="Death">The death loop's options.</param>
/// <param name="Buff">The buff cycle's options for the rebuff stage.</param>
/// <param name="Fight">The fight's options for the fight stage.</param>
public sealed record DeathLegOptions(DeathOptions Death, BuffOptions Buff, FightOptions Fight)
{
    /// <summary>Every problem found while parsing the death loop's and the buff cycle's options, one line each.</summary>
    public IReadOnlyList<string> Errors => [.. Death.Errors, .. Buff.Errors];

    /// <summary>The leg's option error (<c>bad death option: ...</c>), or null when every option parsed.</summary>
    public string? Error => Errors.Count == 0 ? null : "bad death option: " + string.Join("; ", Errors);

    /// <summary>Parses the three option sets from the user arguments.</summary>
    /// <param name="arguments">The user arguments, already split.</param>
    public static DeathLegOptions FromArguments(IReadOnlyList<CommandLineArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var death = DeathOptions.FromArguments(arguments);
        return new DeathLegOptions(death, BuffOptions.FromArguments(arguments), new FightOptions { Kills = death.FightKills, Seconds = death.FightSeconds });
    }
}

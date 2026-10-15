using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The login check's shell fields (m2-plan G4 slice g7; docs/session.md section 27): what the verdict line says about the in-game shell, additive to the line of
/// section 4.14: <c>shell=ok|missing vitals=&lt;h&gt;/&lt;s&gt;/&lt;m&gt;|empty spew_portal=yes|no vitals_window=stacked|side</c>. The facts that need no engine come from the session's state
/// (the PlayerDescription's vitals and character options, here); the facts only the engine sees (the shell is up, the spew box heard portal space, the window shown) come from
/// <c>ShellWatch</c> in the Godot project, and <see cref="Fields"/> puts them in one place so both halves, and the tests, use one wording.
/// </summary>
public static class LoginCheckShell
{
    /// <summary>The text of <c>vitals</c> when the description left a vital unknown or without a maximum (a judged failure).</summary>
    public const string Empty = "empty";

    /// <summary>The text of <c>vitals_window</c> when the run did not get as far as the description.</summary>
    public const string NotApplicable = "n/a";

    /// <summary>
    /// The current health, stamina and mana as <c>h/s/m</c> from the PlayerDescription (<see cref="PlayerQualities.TryGetVitalLevels"/>), or <see cref="Empty"/> when the
    /// description has not arrived or a vital is missing or has no maximum (an unfilled shell would show empty meters).
    /// </summary>
    public static string Vitals(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        if (!qualities.TryGetVitalLevels(1, out var health, out var healthMax) || !qualities.TryGetVitalLevels(3, out var stamina, out var staminaMax) || !qualities.TryGetVitalLevels(5, out var mana, out var manaMax)
            || healthMax == 0 || staminaMax == 0 || manaMax == 0)
        {
            return Empty;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{health}/{stamina}/{mana}");
    }

    /// <summary>
    /// The maximum health, stamina and mana as <c>h/s/m</c> (<see cref="PlayerQualities.TryGetVitalLevels"/>), or <see cref="Empty"/>: logged beside the currents (<c>vitals</c> is the three
    /// <i>current</i> values, never current over maximum), so a current above its maximum shows in the log.
    /// </summary>
    public static string VitalMaximums(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        if (!qualities.TryGetVitalLevels(1, out _, out var health) || !qualities.TryGetVitalLevels(3, out _, out var stamina) || !qualities.TryGetVitalLevels(5, out _, out var mana))
        {
            return Empty;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{health}/{stamina}/{mana}");
    }

    /// <summary>True when <paramref name="vitals"/> is a filled <c>h/s/m</c> (not <see cref="Empty"/>).</summary>
    public static bool VitalsFilled(string vitals) => vitals != Empty && vitals.Split('/').Length == 3;

    /// <summary>The window the character's option chooses (<c>PlayerModule::SideBySideVitals</c>, option bit 21): <c>side</c> or <c>stacked</c>.</summary>
    public static string VitalsWindow(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        return qualities.SideBySideVitals ? "side" : "stacked";
    }

    /// <summary>The fields, as they end the verdict line (a leading space each).</summary>
    /// <param name="shellUp">The game screen was up after Enter with no frame of the bare world.</param>
    /// <param name="vitals">See <see cref="Vitals"/>.</param>
    /// <param name="spewPortal">The spew box heard portal space's notice.</param>
    /// <param name="vitalsWindow">The window the shell showed (<c>stacked</c> or <c>side</c>), or <see cref="NotApplicable"/>.</param>
    public static string Fields(bool shellUp, string vitals, bool spewPortal, string vitalsWindow) =>
        string.Create(CultureInfo.InvariantCulture, $" shell={(shellUp ? "ok" : "missing")} vitals={vitals} spew_portal={(spewPortal ? "yes" : "no")} vitals_window={vitalsWindow}");
}

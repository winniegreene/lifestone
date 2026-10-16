namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The headless host's own options (M2 state track; parsed by src/Lifestone.Headless/HeadlessOptions.cs). The headless
    /// host also reads the Core options marked <see cref="OptionScenes.Headless"/> and the <see cref="Session"/> group.
    /// </summary>
    public static OptionGroup Headless() => new("Headless host", "dotnet Lifestone.Headless.dll",
    [
        new("--scenario", "login|holtburg-laps|academy-exit|mage-build|gear|buff", "What the run does (default login: log in, enter the world, check the server mod's status, log off and quit). holtburg-laps walks laps around the Holtburg lifestone and academy-exit walks out of the Training Academy, both with zero server corrections required. mage-build finds or creates the test account's level-126 combat mage (the character name is fixed; --character and --create-character are ignored with a warning, and any --account but the default test account is refused), spends its experience and credits on the template, learns the arena profile's spells, then logs off; it is idempotent (a ready mage is only verified) and logs '[mage] verdict mage=<ready|built|failed> level=<n> template=<ok|short> spells=<n>/<m> built_steps=<n>'. gear enters that mage (run mage-build first; the same character and account rules), has the server mod grant its kit (a wand, a leather set and the spell components of the arena profile, as the account casts them; a top-up), wields the wand and wears the five armour pieces, then logs off; it is idempotent (a ready mage sends no gear step: gear_steps counts the wand, the armour and the wields, topup counts the component requests, and the cast, when it is on, still burns components and mana) and logs '[gear] verdict gear=<wielded|failed> worn=<n> kit=<held>/<lines> gear_steps=<n> topup=<n> cast=<ok|fizzled|failed|n/a>' (cast is report-only: after the gear it enters the magic combat mode, casts one Creature Enchantment self buff, waits for the mana it needs and returns to non-combat). buff is the gear scenario without its cast, then the magic combat mode, the buff cycle of the profile's 24 lines (the --buff-* options; a regeneration wait is spent in non-combat), a forced re-buff pass that recasts the line closest to expiry while it is still active, non-combat and the log off (the same character and account rules; idempotent: a second run casts no self line) and logs '[buff] verdict gear=<wielded|failed> worn=<n> buffs=<active>/<wanted> levels=<line:level,..> rebuffed=<yes|no> forced=<yes|no> min_remaining_s=<s> transfers=<fired>/<tried> revitalizes=<n> fizzles=<n> mana_min=<n> cycle_s=<s> misses=<names|none>' (its default time limit is 3000 s)."),
        new("--laps", "N", "Laps of the Holtburg route for --scenario=holtburg-laps (default 3)."),
        new("--create-character", "NAME", "Use this character, creating it first on the account when it does not exist."),
        new("--summary", "PATH", "Also write the run's summary as JSON to PATH (relative to the repository root, never under src/ or tests/); it holds no secrets."),
    ], OptionScenes.Headless);
}

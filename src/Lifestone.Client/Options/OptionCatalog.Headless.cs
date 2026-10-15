namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The headless host's own options (M2 state track; parsed by src/Lifestone.Headless/HeadlessOptions.cs). The headless
    /// host also reads the Core options marked <see cref="OptionScenes.Headless"/> and the <see cref="Session"/> group.
    /// </summary>
    public static OptionGroup Headless() => new("Headless host", "dotnet Lifestone.Headless.dll",
    [
        new("--scenario", "login|holtburg-laps|academy-exit", "What the run does (default login: log in, enter the world, check the server mod's status, log off and quit). holtburg-laps walks laps around the Holtburg lifestone and academy-exit walks out of the Training Academy, both with zero server corrections required."),
        new("--laps", "N", "Laps of the Holtburg route for --scenario=holtburg-laps (default 3)."),
        new("--create-character", "NAME", "Use this character, creating it first on the account when it does not exist."),
        new("--summary", "PATH", "Also write the run's summary as JSON to PATH (relative to the repository root, never under src/ or tests/); it holds no secrets."),
    ], OptionScenes.Headless);
}

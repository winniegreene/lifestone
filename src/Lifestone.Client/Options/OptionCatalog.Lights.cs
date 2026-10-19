namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// Dynamic lights (M3 4.1 s0; parsed by <see cref="Lifestone.Client.Interiors.LightOptions"/>).
    /// </summary>
    public static OptionGroup Lights() => new("Lights", "main scene",
    [
        new("--viewer-light", "on|off", "The viewer light, a white point light 2 m above the player (intensity 2.25, falloff 10) that retail adds to the dynamic lights every frame (default on); off is for a review frame without it."),
    ], OptionScenes.Main);
}

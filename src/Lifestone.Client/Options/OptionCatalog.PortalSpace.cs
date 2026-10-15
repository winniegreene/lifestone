namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// Portal space in the main scene (M2 wave-2 track G1, the portal view; parsed by
    /// <see cref="Lifestone.Client.PortalSpace.PortalSpaceOptions"/>). The check suite <c>--portal-space-checks</c> belongs to
    /// the Checks group (lead, W2-pre). <c>--retail-logoff-tunnel</c> is also read by the headless host, whose state
    /// machine ends the logoff tunnel by the same policy (owner decision D28).
    /// </summary>
    public static OptionGroup PortalSpace() => new(
        "Portal space",
        "main scene",
        [
            new("--portal-transition", "on|off", "Play portal space when the teleport panel jumps (default on). Check suites, golden viewpoints and tours never play it."),
            new("--portal-space", null, "Hold the portal tunnel (state 3) until quit, for review."),
            new(
                "--portal-space-frames",
                "DIR",
                "Play a scripted portal-space sequence at a fixed step and seed, capture four review frames (default docs/screenshots/m2/portal-space) and quit.",
                OptionRole.EndsRun,
                ValueOptional: true),
            new(
                "--retail-logoff-tunnel",
                null,
                "Retail's logoff tunnel: the tunnel ends and the world shows again before the character list arrives (default: hold the tunnel until it does, D28).",
                AlsoIn: OptionScenes.Headless),
        ],
        OptionScenes.Main);
}

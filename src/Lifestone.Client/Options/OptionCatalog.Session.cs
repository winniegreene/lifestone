namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The live session (M2 state track), read by the main scene and the headless host; parsed by
    /// src/Lifestone.Client/Session/SessionOptions.cs (<c>SessionOptions.Parse</c>), which both hosts call. None takes a
    /// password: <c>--account</c> names an entry of server\runtime\secrets.json, which the host reads as bytes.
    /// </summary>
    public static OptionGroup Session() => new("Session", "main scene and headless host",
    [
        new("--play", null, "Play mode: log in to the bundled ACE on 127.0.0.1 instead of exploring (tools\\play.ps1 passes it; with no arguments the game stays in explore mode; the headless host always logs in)."),
        new("--account", "NAME", "The account to log in with: a key of server\\runtime\\secrets.json's accounts (play, test, or one added by setup-server.ps1 -AddAccount), else a username. Default: the file's defaultAccount (the play account); the headless host (always an automated run) accepts only the test account. A name only: no option takes a password."),
        new("--character", "NAME", "Enter the world with this character (case-insensitive) instead of stopping at the character list; refused when it is pending deletion."),
        new("--server", "IP:PORT", "ACE's login endpoint, an IP literal on loopback (default 127.0.0.1:9000, from server\\versions.json); host names and every address off loopback are refused before anything is sent."),
        new("--record-session", "DIR", "Record the session's messages as a .lsrec file in DIR (default cache/sessions; relative to the repository root, never under src/ or tests/). The headless host always records.", ValueOptional: true),
        new("--session-budget-ms", "X", "Main-thread milliseconds per frame for applying server messages (default 2; the transport's acks and resends always run)."),
        new("--retail-net", null, "Retail's network behaviour where Lifestone deviates: the 140 s silence timeout, one login attempt, no tail probe, arrival-order delivery (default: Lifestone's, docs/protocol.md)."),
    ], OptionScenes.Main | OptionScenes.Headless);
}

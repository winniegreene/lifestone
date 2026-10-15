namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// Who issued an action. Every action carries one, the action log records it, and the safety rules key on it: the kill
/// switch and dry run apply to <see cref="Automation"/> only (A0's R6), and automation runs only against a loopback
/// server (R1).
/// </summary>
public enum ActionSource
{
    /// <summary>The host program itself: the Godot session host (quitting on window close), the headless host's login steps.</summary>
    Host,

    /// <summary>A person, through the retail UI or the Lifestone panels.</summary>
    Ui,

    /// <summary>A bot: an automation engine, a scripted scenario, a route walker. Subject to the kill switch and dry run.</summary>
    Automation,

    /// <summary>The client's own retail behaviour, not a choice anyone made: LoginComplete when portal space ends.</summary>
    Client,

    /// <summary>A test driving the session directly.</summary>
    Test,
}

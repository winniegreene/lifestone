namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// One thing a screen driver is doing for <see cref="ThrowawayLegScenario"/> (clicking through character creation, pressing a tools-panel button, deleting a character
/// on the character screen): started by a call on <see cref="IThrowawayScreens"/>, advanced once per frame by the scenario (<see cref="Advance"/>), done when
/// <see cref="Done"/> is true. A driver that needs frames subclasses it; a test seam uses <see cref="Completed"/> and <see cref="Failed"/>.
/// </summary>
public class ScreenOp
{
    /// <summary>True once the operation ended, well or not.</summary>
    public bool Done { get; private set; }

    /// <summary>Why the operation failed (the screen was not where it should be, a button was missing, an expected reply never came), or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>What the operation found: the error dialog's text for a Finish that was refused, the tools panel's status for a button; null when it found nothing to say.</summary>
    public string? Text { get; private set; }

    /// <summary>A number the operation measured (Where am I: the metres between the server's position and the client's; Add XP: the experience added), or NaN.</summary>
    public double Number { get; private set; } = double.NaN;

    /// <summary>True when the operation ended without a failure.</summary>
    public bool Succeeded => Done && Failure is null;

    /// <summary>A finished operation.</summary>
    /// <param name="text">What it found, or null.</param>
    /// <param name="number">What it measured, or NaN.</param>
    public static ScreenOp Completed(string? text = null, double number = double.NaN)
    {
        var op = new ScreenOp();
        op.Complete(text, number);
        return op;
    }

    /// <summary>An operation that failed at once.</summary>
    /// <param name="why">The reason.</param>
    public static ScreenOp Failed(string why)
    {
        var op = new ScreenOp();
        op.Fail(why);
        return op;
    }

    /// <summary>Runs one frame of the operation (the scenario calls it before it looks at <see cref="Done"/>); an operation that is done or has nothing to run does nothing.</summary>
    /// <param name="now">The session clock's time.</param>
    public virtual void Advance(double now)
    {
    }

    /// <summary>Ends the operation successfully.</summary>
    /// <param name="text">What it found, or null.</param>
    /// <param name="number">What it measured, or NaN.</param>
    protected void Complete(string? text = null, double number = double.NaN)
    {
        if (Done)
        {
            return;
        }

        Done = true;
        Text = text;
        Number = number;
    }

    /// <summary>Ends the operation with a failure.</summary>
    /// <param name="why">The reason.</param>
    protected void Fail(string why)
    {
        if (Done)
        {
            return;
        }

        Done = true;
        Failure = why;
    }
}

/// <summary>
/// The screen-action seam of <see cref="ThrowawayLegScenario"/> (<c>--login-check=throwaway</c>): everything the leg does through the retail screens rather than
/// the Action API. The game's driver clicks the real screens (<c>ThrowawayScreens</c> in the game project); a test supplies a fake. Every member only starts
/// the operation and returns at once; the scenario advances it each frame and looks at it (<see cref="ScreenOp.Done"/>). The screen the member expects is named in
/// each summary: an operation started anywhere else fails.
/// </summary>
public interface IThrowawayScreens
{
    /// <summary>On the character list: Create, then Next through the heritage, profession, skills, appearance and town pages to the summary page with its name field focused.</summary>
    ScreenOp OpenCharGenSummary();

    /// <summary>
    /// On the summary page: types <paramref name="name"/> and presses Finish (answering the credit warning with Yes). Done when the server's answer is on screen: the
    /// result's <see cref="ScreenOp.Text"/> is the error dialog's text for a refused name, or null when the server accepted the character (character creation closed).
    /// </summary>
    /// <param name="name">The character's name.</param>
    ScreenOp Finish(string name);

    /// <summary>On character creation with an error dialog showing: presses its OK button. Done when the dialog is gone (the summary page is under it).</summary>
    ScreenOp DismissErrorDialog();

    /// <summary>In the world: opens the tools panel with its hotkey (F7). Done when it is open.</summary>
    ScreenOp OpenToolsPanel();

    /// <summary>In the world: closes the tools panel with its hotkey. Done when it is closed.</summary>
    ScreenOp CloseToolsPanel();

    /// <summary>With the panel open: presses Where am I. Done when the server answered; the result's <see cref="ScreenOp.Number"/> is the metres between the server's position and the client body's.</summary>
    ScreenOp PressWhere();

    /// <summary>
    /// With the panel open: types the amount and presses Add XP. The driver refuses (a failed operation, nothing pressed) unless the in-world character is
    /// <paramref name="expectedCharacter"/>. Done when the chat shows the server's reply; the result's <see cref="ScreenOp.Number"/> is the experience added.
    /// </summary>
    /// <param name="expectedCharacter">The character that must be in the world (the throwaway; owner decision D61).</param>
    /// <param name="amount">The experience to add.</param>
    ScreenOp PressAddXp(string expectedCharacter, long amount);

    /// <summary>With the panel open: types <paramref name="query"/> into the search field and clicks the first place. Done when the click asked the server to teleport.</summary>
    /// <param name="query">The search text (for example <c>holtburg</c>).</param>
    ScreenOp PressPlace(string query);

    /// <summary>On the character list: clicks the row named <paramref name="name"/>. Done when it is the selected character.</summary>
    /// <param name="name">The character's name.</param>
    ScreenOp SelectCharacter(string name);

    /// <summary>On the character list, with the selected character ready: Delete, the typed word <c>delete</c>, Done, and the Please Wait dialog. Done when the list shows the character pending deletion.</summary>
    ScreenOp DeleteSelected();

    /// <summary>On the character list, with the selected character pending deletion: Restore and the Please Wait dialog. Done when the list shows the character ready again.</summary>
    ScreenOp RestoreSelected();
}

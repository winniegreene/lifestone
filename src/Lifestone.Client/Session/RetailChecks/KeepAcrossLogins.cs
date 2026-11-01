using System.Globalization;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.RetailChecks;

/// <summary>One floating window's place as the shell's layout keeper writes and reads it (<c>ScreenLayoutFile</c>: retail's tag and the box in the root).</summary>
/// <param name="Tag">Retail's tag, with the angle brackets (<c>&lt;VITS&gt;</c>).</param>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct KeepWindow(string Tag, int X, int Y, int Width, int Height)
{
    /// <summary>The same place and size, whatever the tag.</summary>
    public bool SamePlace(KeepWindow other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
}

/// <summary>What <see cref="IKeepShell.MoveOneWindow"/> did: the window and where it was and is.</summary>
/// <param name="Before">The window before the move.</param>
/// <param name="After">The window after it (same tag).</param>
public readonly record struct KeepMove(KeepWindow Before, KeepWindow After);

/// <summary>What the present game screen read from the layout store: which screen it is (an id that differs between two entries), whether the file existed, how many windows it applied, and how many reads it made.</summary>
public readonly record struct KeepLayoutRead(int ScreenId, bool Found, int WindowsApplied, int Reads);

/// <summary>
/// The in-game shell as the keep check sees it (M5 6 k2, D33): the Godot host's runner implements it over the retail UI's game screen and its <c>ScreenLayoutKeeper</c>, a test over a fake.
/// No automation click moves a window: the move is the keeper's own seam (<c>UiElement.MoveTo</c>, the call the layout's load and a player's drag end in; M3 D29).
/// </summary>
public interface IKeepShell
{
    /// <summary>True when the in-game shell of the present login is built and its saved layout was read and applied (or found missing): the windows are where this login's layout puts them.</summary>
    bool ShellReady { get; }

    /// <summary>
    /// Moves one floating window to the fixed place <see cref="KeepAcrossLogins.MovePlaceX"/>, <see cref="KeepAcrossLogins.MovePlaceY"/> (inside the root; <see cref="KeepAcrossLogins.MovePlaceAlternate"/> when it is already there) and returns its place before and
    /// after, or null when no window could be moved (the shell has none, or the move changed nothing). A fixed place does not drift from run to run (M5 6 fix, 6-R6); the layout's own default differs from it.
    /// </summary>
    KeepMove? MoveOneWindow();

    /// <summary>Moves the window tagged <paramref name="original"/>'s tag back to the place it started at, so the next run starts from the same layout (M5 6 fix, 6-R6).</summary>
    void PutBack(KeepWindow original);

    /// <summary>What the last read of a layout file by the present game screen found (M5 6 fix, 6-R6: the proof that the file was read at the second entry).</summary>
    KeepLayoutRead LayoutRead { get; }

    /// <summary>The place of the window tagged <paramref name="tag"/> now, or null when the shell has no such window.</summary>
    KeepWindow? Read(string tag);
}

/// <summary>
/// <c>--login-check=keep</c> (M5 6 k2, D32 and D33; DoD 11: "one login shows the options and the layout kept from the one before"): one process, two logins of Lifestone Throwaway.
/// Login 1 flips one innocuous character option through <see cref="IActionApi.SetCharacterOptions"/> (GameAction 0x01A1) and moves one window through <see cref="IKeepShell"/>, then logs off
/// to the character list (which writes the layout, <c>GamePlayScreen</c>'s log-off save); login 2 enters again, waits for the new PlayerDescription and the applied layout, reads the option bit and
/// the window's place, puts the option back, and logs off. The verdict line is <c>keep options_kept=&lt;yes|no&gt; layout_kept=&lt;yes|no&gt; option=&lt;name&gt; window=&lt;tag&gt;</c>, report-only (a new judge).
/// <para>
/// <b>The option</b> is <see cref="OptionName"/> (<c>DisplayTimeStamps</c>, word 2, ACE <c>CharacterOptions2</c> 0x40): a chat-display preference that ACE only stores and that no Lifestone code reads, so
/// flipping it changes nothing in the world. The bit is flipped from whatever login 1 reads (a leftover of a run that died between the flip and the put-back is read as the starting value and flipped again),
/// and login 2 always sends the starting value back before it logs off. The character is created when absent (the way the loopback tests do) and is left on the list.
/// </para>
/// <para>
/// <b>Why login 2's read is not stale.</b> <see cref="IActionApi.SetCharacterOptions"/> applies the words to the read model at once, so the model alone would read "kept" without a server. The check therefore
/// counts the PlayerDescriptions (<see cref="QualityChange.Description"/>) and reads the option only after a description that arrived since login 2 began: that description is ACE's stored words.
/// </para>
/// </summary>
public sealed class KeepAcrossLogins : ScriptLeg
{
    /// <summary>The scenario's name in the log.</summary>
    public const string ScenarioName = "keep-across-logins";

    /// <summary>The option the check flips (retail's name; see <see cref="PlayerOptionTable"/>).</summary>
    public const string OptionName = "DisplayTimeStamps";

    /// <summary>How long the arrival of one login may take, in session-clock seconds.</summary>
    public const double ArrivalSeconds = 90;

    /// <summary>The fixed place the moved window goes to (UI pixels from the root's top left); chosen to differ from every default of the layout's floating windows (6-R6).</summary>
    public const int MovePlaceX = 120;

    /// <summary>See <see cref="MovePlaceX"/>.</summary>
    public const int MovePlaceY = 120;

    /// <summary>The place used when the window is already at <see cref="MovePlaceX"/>, <see cref="MovePlaceY"/> (a layout left there by an earlier run), so that the move always changes something.</summary>
    public const int MovePlaceAlternate = 200;

    /// <summary>How long the shell may take to be built with its layout applied after the arrival, in session-clock seconds.</summary>
    public const double ShellSeconds = 30;

    private readonly IKeepShell _shell;
    private readonly string _character;
    private readonly Func<CharacterCreateIntent>? _createIntent;
    private int _descriptions;
    private bool? _optionsKept;
    private bool? _layoutKept;
    private KeepLayoutRead _firstRead;
    private bool? _fileRead;
    private string _window = "n/a";

    /// <summary>Makes the check.</summary>
    /// <param name="shell">The shell's window seam.</param>
    /// <param name="log">Where the leg's lines go.</param>
    /// <param name="character">The character's name (Lifestone Throwaway; a test uses its own).</param>
    /// <param name="createIntent">The intent to create the character with when the list lacks it, or null to fail instead.</param>
    public KeepAcrossLogins(IKeepShell shell, Action<string> log, string character, Func<CharacterCreateIntent>? createIntent = null)
        : base(ScenarioName, log)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _createIntent = createIntent;
    }

    /// <summary>The option's definition.</summary>
    public static PlayerOptionDef Option { get; } = PlayerOptionTable.All.First(static o => o.Name == OptionName);

    /// <summary>True once login 1's changes were made and the character was logged off the first time.</summary>
    public bool FirstLoginDone { get; private set; }

    /// <summary>True once login 2 read both.</summary>
    public bool SecondLoginRead { get; private set; }

    /// <inheritdoc />
    public override bool Ok => base.Ok && SecondLoginRead && _optionsKept == true && _layoutKept == true;

    /// <summary>The option words after <paramref name="option"/>'s bit is set to <paramref name="on"/>.</summary>
    public static (uint Options1, uint Options2) WithBit(PlayerOptionDef option, uint options1, uint options2, bool on)
    {
        ArgumentNullException.ThrowIfNull(option);
        var mask = option.Mask;
        return option.Word == 1
            ? (on ? options1 | mask : options1 & ~mask, options2)
            : (options1, on ? options2 | mask : options2 & ~mask);
    }

    /// <summary>True when <paramref name="option"/>'s bit is set in the words.</summary>
    public static bool BitOf(PlayerOptionDef option, uint options1, uint options2)
    {
        ArgumentNullException.ThrowIfNull(option);
        return ((option.Word == 1 ? options1 : options2) & option.Mask) != 0;
    }

    /// <summary>The window kept: the same place and size login 1 left it at, and not the place it started at (a window that never moved proves nothing).</summary>
    public static bool LayoutKept(KeepMove moved, KeepWindow? readBack) =>
        readBack is { } now && now.Tag == moved.After.Tag && now.SamePlace(moved.After) && !now.SamePlace(moved.Before);

    /// <summary>
    /// True when the second entry read the layout file: another game screen than login 1's, whose read found the file and applied at least one window to it. Without this the window could be
    /// "kept" by a shell that survived the log off and never touched the store (6-R6).
    /// </summary>
    public static bool FileWasRead(KeepLayoutRead first, KeepLayoutRead second) => second.ScreenId != first.ScreenId && second.Found && second.WindowsApplied >= 1 && second.Reads >= 1;

    /// <summary>The verdict line: <c>keep options_kept=&lt;yes|no&gt; layout_kept=&lt;yes|no&gt; option=&lt;name&gt; window=&lt;tag&gt;</c>.</summary>
    public static string VerdictLine(bool optionsKept, bool layoutKept, string option, string window) =>
        string.Create(CultureInfo.InvariantCulture, $"keep options_kept={(optionsKept ? "yes" : "no")} layout_kept={(layoutKept ? "yes" : "no")} option={option} window={window}");

    /// <inheritdoc />
    public override string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"problems={Problems.Count}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <summary>The check's judged line (<see cref="VerdictLine"/>) from what it has read so far; a field not read is "no".</summary>
    public string Verdict => VerdictLine(_optionsKept == true, _layoutKept == true, OptionName, _window);

    /// <inheritdoc />
    protected override bool LogOffOnFailure => true;

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        using var descriptions = S.Events.Subscribe((in QualitiesChanged e) =>
        {
            if (e.Change == QualityChange.Description)
            {
                _descriptions++;
            }
        });

        // ---- login 1 ----
        foreach (var h in EnterTheCharacter(first: true))
        {
            yield return h;
        }

        foreach (var h in WaitForShell("login 1"))
        {
            yield return h;
        }

        _firstRead = _shell.LayoutRead;
        var q = S.State.Self.Qualities;
        var start = BitOf(Option, q.CharacterOptions1, q.CharacterOptions2);
        Stage = "flip-option";
        var (flipped1, flipped2) = WithBit(Option, q.CharacterOptions1, q.CharacterOptions2, !start);
        var send = S.Actions.SetCharacterOptions(flipped1, flipped2, ActionSource.Host);
        yield return send;
        Sent(send, $"SetCharacterOptions ({OptionName} {(start ? "on" : "off")} to {(start ? "off" : "on")})");
        Check(BitOf(Option, S.State.Self.Qualities.CharacterOptions1, S.State.Self.Qualities.CharacterOptions2) == !start, "the read model did not take the flipped option at once");

        Stage = "move-window";
        var move = _shell.MoveOneWindow();
        if (!Check(move is not null, "the shell moved no window"))
        {
            yield break;
        }

        _window = move!.Value.After.Tag;
        Log($"moved {move.Value.After.Tag} from ({move.Value.Before.X}, {move.Value.Before.Y}) to ({move.Value.After.X}, {move.Value.After.Y})");
        foreach (var h in Pause(1.5))
        {
            yield return h;
        }

        Stage = "logging-off-1";
        var off = S.Actions.LogOff(ActionSource.Host); // GamePlayScreen writes the layout on LogOffRequested; the list comes back with the handle
        yield return off;
        Confirmed(off, "log off (login 1)");
        FirstLoginDone = true;
        foreach (var h in Pause(2))
        {
            yield return h;
        }

        // ---- login 2 ----
        var before = _descriptions;
        foreach (var h in EnterTheCharacter(first: false))
        {
            yield return h;
        }

        Stage = "description-2";
        foreach (var h in Until(() => _descriptions > before, ArrivalSeconds))
        {
            yield return h;
        }

        Require(_descriptions > before, "login 2: no PlayerDescription arrived, so the option bit would be read from the model alone");
        foreach (var h in WaitForShell("login 2"))
        {
            yield return h;
        }

        Stage = "read";
        var q2 = S.State.Self.Qualities;
        var now = BitOf(Option, q2.CharacterOptions1, q2.CharacterOptions2);
        _optionsKept = now == !start;
        var secondRead = _shell.LayoutRead;
        _fileRead = FileWasRead(_firstRead, secondRead);
        _layoutKept = LayoutKept(move.Value, _shell.Read(move.Value.After.Tag)) && _fileRead == true;
        SecondLoginRead = true;
        Log($"login 2: the layout file was {(_fileRead == true ? "read" : "NOT read")} (screen {(secondRead.ScreenId != _firstRead.ScreenId ? "is new" : "is login 1's")}, found={secondRead.Found}, windows applied={secondRead.WindowsApplied}, reads={secondRead.Reads})");
        Log($"login 2: {OptionName} is {(now ? "on" : "off")} (login 1 started with it {(start ? "on" : "off")}); {_window} is {DescribeWindow(_shell.Read(_window))}");
        Check(_optionsKept == true, $"the option was not kept: login 1 started {(start ? "on" : "off")}, flipped it, and login 2 reads it {(now ? "on" : "off")}");
        Check(_fileRead == true, "the layout file was not read at the second entry (the screen is not a new one, or its read found no file or applied no window)");
        Check(_layoutKept == true, $"the window was not kept: login 1 left {_window} at ({move.Value.After.X}, {move.Value.After.Y}), login 2 reads {DescribeWindow(_shell.Read(_window))}");

        Stage = "put-back";
        _shell.PutBack(move.Value.Before); // the window goes back where it started, and the log off below writes that place (6-R6: no drift from run to run)
        Check(_shell.Read(move.Value.Before.Tag) is { } placed && placed.SamePlace(move.Value.Before), "the window was not put back to its starting place");
        var (back1, back2) = WithBit(Option, q2.CharacterOptions1, q2.CharacterOptions2, start);
        var restore = S.Actions.SetCharacterOptions(back1, back2, ActionSource.Host);
        yield return restore;
        Sent(restore, $"SetCharacterOptions (put {OptionName} back {(start ? "on" : "off")})");
        foreach (var h in Pause(1.5))
        {
            yield return h;
        }

        Stage = "logging-off-2";
        var off2 = S.Actions.LogOff(ActionSource.Host);
        yield return off2;
        Confirmed(off2, "log off (login 2)");
        if (!LiveStays)
        {
            Stage = "quitting";
            var quit = S.Actions.Quit(ActionSource.Host);
            yield return quit;
            Log("quit: " + Describe(quit));
        }
    }

    /// <summary>True while <paramref name="session"/> is in the world (the Godot host's runner asks it without naming the network layer).</summary>
    public static bool InWorld(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.State.Phase == SessionPhase.InWorld;
    }

    /// <summary>True in the Godot play host, which logs in by itself: the leg waits for the character list instead of asking for a login (the live run's first refusal, M5 6 live step).</summary>
    public bool HostLogsIn { get; init; }

    /// <summary>True in a test that keeps the session after the last log off (no quit).</summary>
    public bool LiveStays { get; init; }

    /// <summary>The handle settled as Sent (ACE answers nothing to GameAction 0x01A1) or Confirmed.</summary>
    private bool Sent(ActionHandle handle, string what)
    {
        Log(what + ": " + Describe(handle));
        return Check(handle.Outcome is { Succeeded: true }, what + ": " + Describe(handle));
    }

    private static string DescribeWindow(KeepWindow? w) => w is { } x ? $"({x.X}, {x.Y}) {x.Width}x{x.Height}" : "not found";

    private IEnumerable<ActionHandle?> WaitForShell(string login)
    {
        Stage = "shell";
        foreach (var h in Until(() => _shell.ShellReady, ShellSeconds))
        {
            yield return h;
        }

        Require(_shell.ShellReady, $"{login}: the in-game shell was not built with its layout applied within {ShellSeconds:0} s");
    }

    /// <summary>Logs in when the session is idle (the first time), selects the character (created when absent), enters the world and waits for the arrival.</summary>
    private IEnumerable<ActionHandle?> EnterTheCharacter(bool first)
    {
        if (HostLogsIn && first)
        {
            // The Godot play host logs in by itself when it starts (LoginCheckScenario.HostLoginSeconds); a second Login is refused, so the leg waits for the list as the throwaway leg does.
            Stage = "login";
            foreach (var h in Until(() => S.State.Phase is SessionPhase.CharacterSelect or SessionPhase.Failed, ArrivalSeconds))
            {
                yield return h;
            }

            Require(S.State.Phase == SessionPhase.CharacterSelect, $"login: the host's login did not reach the character list within {ArrivalSeconds:0} s (phase {S.State.Phase})");
        }
        else if (S.State.Phase == SessionPhase.Idle)
        {
            Stage = "login";
            var login = S.Actions.Login(ActionSource.Host);
            yield return login;
            Require(login.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "login: " + Describe(login));
        }

        Stage = first ? "select-1" : "select-2";
        var slot = Find();
        Require(slot is not { PendingDeletion: true }, $"'{_character}' is pending deletion from an earlier run: purge it first");
        if (slot is null)
        {
            Require(first && _createIntent is not null, $"'{_character}' is not on the character list and cannot be created here");
            var create = S.Actions.CreateCharacter(_createIntent!(), ActionSource.Host);
            yield return create;
            Require(create.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "creating the character: " + Describe(create));
            slot = Find();
            Require(slot is { PendingDeletion: false }, "creating the character: the list does not show it");
        }

        Stage = first ? "entering-1" : "entering-2";
        var enter = S.Actions.EnterWorld(slot!.Value.Guid, ActionSource.Host);
        yield return enter;
        Require(enter.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "entering the world: " + Describe(enter));
        Stage = first ? "arriving-1" : "arriving-2";
        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && S.State.Self.Qualities.IsKnown, ArrivalSeconds))
        {
            yield return h;
        }

        Require(S.State.Self.Travel == SelfTravel.Present && S.State.Self.Qualities.IsKnown, $"arriving: the player was not unhidden with its qualities within {ArrivalSeconds:0} s");
        Log("arrived");
    }

    private CharacterSlot? Find() =>
        S.State.Characters.Entries.OrderBy(static c => c.PendingDeletion).ThenBy(static c => c.Guid).Cast<CharacterSlot?>()
            .FirstOrDefault(c => string.Equals(c!.Value.Name, _character, StringComparison.OrdinalIgnoreCase));
}

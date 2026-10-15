using System.Globalization;
using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Streaming;
using Lifestone.Data.Effects;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// Runs portal space in one fixed frame order, the same for the Godot presenter, the headless host and the tests: the
/// state machine, then the destination's readiness and the teleport flags, then the tunnel, then the cues. A presenter
/// calls <see cref="Step"/> once per frame and applies the returned <see cref="PortalFrame"/>; it never orders these
/// pieces itself.
/// </summary>
/// <remarks>
/// <para><b>Frame order</b> (step PS11):</para>
/// <list type="number">
/// <item><see cref="PortalSpaceMachine.Update"/> (<c>gmSmartBoxUI::UseTime</c>, 11.4186 0x004D6E30, 11.6096 0x004D7A90)
/// reads <see cref="TeleportProgress.InProgress"/> before</item>
/// <item><see cref="DestinationReadiness.Evaluate"/> and <see cref="TeleportProgress.Tick"/> (<c>SmartBox::UseTime</c>,
/// 11.4186 0x00455410, 11.6096 0x004554B0). Retail's UI runs before the SmartBox in a frame, so the machine sees the
/// teleport flags as the last frame left them: a destination that is ready at once still plays the whole sequence (the
/// tunnel expands for 1 s, holds at least 2 s, collapses for 1 s, the world expands for 1 s).</item>
/// <item>While the tunnel shows, the tunnel advances (<c>CreatureMode::Render</c>, 11.4186 0x004529D0, 11.6096
/// 0x00452A10, steps creature-mode objects while it draws them) and its hum goes to the sound sink, heard from the
/// tunnel camera (<see cref="PortalSpaceScene.TunnelHumOrigin"/>). The tunnel's events are drained every such frame,
/// with or without a sink.</item>
/// <item>The cues go out: <see cref="PortalCues.PlayEnterPortal"/> and <see cref="PortalCues.PlayExitPortal"/> as
/// <c>UI_EnterPortal</c> (0x6A) and <c>UI_ExitPortal</c> (0x6B) on the UIASSET UI sound table
/// (<c>ClientUISystem::GetUISoundTable</c>, 11.4186 0x00563FB0, 11.6096 0x00564D50; played with
/// <c>SoundManager::PlaySoundFromCenter</c>); <see cref="PortalCues.PostPortalNotice"/> as the literal "In Portal Space -
/// Please Wait..." with text type 0x1A; <see cref="PortalCues.SendLoginComplete"/> through
/// <see cref="LoginComplete"/>.</item>
/// <item>Lifestone's loud warnings (D30): <see cref="PortalCues.ReadinessStalled"/> every
/// <see cref="StallWarningSeconds"/> while state 3 waits for an update that is not held, with the reason (the
/// readiness report, or the server's position still awaited); <see cref="PortalCues.ArrivedWithFailures"/> once, on the
/// frame the update completes with failed pieces. There is no automatic exit: a destination that never settles keeps the
/// tunnel up, as in retail.</item>
/// </list>
/// <para>The session layer's event handlers, pumped earlier in the frame, call the <see cref="Progress"/> methods
/// directly (<see cref="PortalSessionBridge"/>). The cues returned are the machine's plus the two warnings; the director
/// has already acted on the sound, notice and LoginComplete cues.</para>
/// <para><b>LoginComplete</b> goes to <see cref="LoginComplete"/>, at most once per completed update (the machine's
/// guarantee), on the cue itself: retail's wait for the player's inventory (<c>sendLoginCompletePending</c>) is not
/// re-expressed (the TODO on <see cref="PortalSessionBridge.SendLoginComplete"/>). While the sink is null (offline), or
/// while the update was a local jump (<see cref="TeleportProgress.IsLocal"/>, <see cref="ExploreTransition"/>), the send
/// is suppressed, logged and counted in <see cref="LoginCompletesSuppressed"/>.</para>
/// <para><b>Sound.</b> When given an <see cref="ISoundSink"/>, the director plays the enter and exit whooshes and the hum
/// itself; a presenter that passes one must not play <see cref="PortalCues.PlayEnterPortal"/> or
/// <see cref="PortalCues.PlayExitPortal"/> again. Without one (the headless host) portal space is silent.</para>
/// <para>Allocation-free per frame, apart from a warning's text when the log is enabled. Deterministic for a given clock,
/// random source, tunnel, world and event sequence. Frame thread only.</para>
/// </remarks>
public sealed class PortalSpaceDirector
{
    /// <summary>How often a stalled tunnel is reported, in seconds (D30).</summary>
    public const double StallWarningSeconds = 10.0;

    /// <summary>The log category.</summary>
    public const string LogCategory = "portal";

    /// <summary>The enter whoosh: <c>UI_EnterPortal</c> (DatReaderWriter's <c>Sound.UI_EnterPortal</c>).</summary>
    public const uint EnterPortalSoundType = 0x6A;

    /// <summary>The exit whoosh: <c>UI_ExitPortal</c> (DatReaderWriter's <c>Sound.UI_ExitPortal</c>).</summary>
    public const uint ExitPortalSoundType = 0x6B;

    private readonly List<EffectEvent> _events = [];
    private double _stallStart = double.NaN;
    private int _stallWarnings;
    private double _holdStart = double.NaN;
    private int _holdWarnings;

    /// <summary>Creates a director over a machine and the teleport flags it reads.</summary>
    /// <param name="machine">The state machine (and, through it, the tunnel).</param>
    /// <param name="progress">The teleport flags the session layer feeds.</param>
    /// <param name="assets">The UIASSET records (the UI sound table for the whooshes).</param>
    /// <param name="policy">The readiness rule's options.</param>
    /// <param name="sound">Plays the whooshes and the hum; null for silence.</param>
    /// <param name="notices">Shows the portal notice; null to drop it.</param>
    /// <param name="log">Receives the readiness warnings and the LoginComplete trail; null for none. It also becomes the
    /// machine's log.</param>
    public PortalSpaceDirector(
        PortalSpaceMachine machine,
        TeleportProgress progress,
        PortalSpaceAssets assets,
        ReadinessPolicy policy,
        ISoundSink? sound = null,
        IPortalNoticeSink? notices = null,
        ILogSink? log = null)
    {
        Machine = machine ?? throw new ArgumentNullException(nameof(machine));
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        Assets = assets ?? throw new ArgumentNullException(nameof(assets));
        Policy = policy;
        Sound = sound;
        Notices = notices;
        Log = log ?? NullLogSink.Instance;
        if (log is not null)
        {
            Machine.Log = log;
        }
    }

    /// <summary>The state machine.</summary>
    public PortalSpaceMachine Machine { get; }

    /// <summary>The teleport flags.</summary>
    public TeleportProgress Progress { get; }

    /// <summary>The UIASSET records.</summary>
    public PortalSpaceAssets Assets { get; }

    /// <summary>The readiness rule's options.</summary>
    public ReadinessPolicy Policy { get; }

    /// <summary>The sound sink, or null.</summary>
    public ISoundSink? Sound { get; }

    /// <summary>The notice sink, or null.</summary>
    public IPortalNoticeSink? Notices { get; }

    /// <summary>The log.</summary>
    public ILogSink Log { get; }

    /// <summary>
    /// Sends LoginComplete when portal space ends. The session bridge sets it; while it is null (offline explore mode) the
    /// director logs the send as suppressed and counts it in <see cref="LoginCompletesSuppressed"/>.
    /// </summary>
    public ILoginCompleteSink? LoginComplete { get; set; }

    /// <summary>The readiness evaluated in the last <see cref="Step"/>.</summary>
    public ReadinessReport LastReadiness { get; private set; }

    /// <summary>LoginCompletes handed to <see cref="LoginComplete"/>.</summary>
    public int LoginCompletesSent { get; private set; }

    /// <summary>LoginCompletes due while <see cref="LoginComplete"/> was null, or after a local jump.</summary>
    public int LoginCompletesSuppressed { get; private set; }

    /// <summary>Stall warnings given since the director was created.</summary>
    public int StallWarnings { get; private set; }

    /// <summary>Warnings given while a logoff's tunnel waited for the character list (D28's hold, <c>TODO(portal-logoff-hold)</c>).</summary>
    public int LogOffHoldWarnings { get; private set; }

    /// <summary>Arrivals with failed pieces since the director was created.</summary>
    public int ArrivalsWithFailures { get; private set; }

    /// <summary>Tunnel hook events drained (and, with a sink, routed) since the director was created.</summary>
    public int TunnelEvents { get; private set; }

    /// <summary>Runs one frame at <paramref name="now"/> with the player's cell and the world's attach state.</summary>
    /// <param name="now">Monotonic real seconds, advanced once per frame.</param>
    /// <param name="playerCellId">The destination: the player's cell (offline, the viewer's cell after the jump).</param>
    /// <param name="world">What the world has attached so far.</param>
    public PortalFrame Step(double now, uint playerCellId, IWorldAttachments world)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 1. gmSmartBoxUI::UseTime, on the flags as the last frame left them.
        var frame = Machine.Update(now, Progress);

        // 2. SmartBox::UseTime: the destination's readiness completes the update.
        var readiness = DestinationReadiness.Evaluate(playerCellId, world, Policy);
        LastReadiness = readiness;
        var wasInProgress = Progress.InProgress;
        Progress.Tick(readiness.IsReady);
        var extra = PortalCues.None;
        if (wasInProgress && !Progress.InProgress && readiness.Failed > 0)
        {
            extra |= ArrivedWithFailures(now, readiness);
        }

        // 3. CreatureMode::Render: the tunnel steps while it shows, and its hum is heard from the tunnel camera.
        if (frame.TunnelVisible)
        {
            Machine.Tunnel.AdvanceTo(now);
            DrainTunnel();
        }

        // 4. The cues.
        var cues = frame.Cues;
        if ((cues & PortalCues.PlayEnterPortal) != 0)
        {
            Sound?.PlayFromCenter(Assets.UiSoundTableId, EnterPortalSoundType);
        }

        if ((cues & PortalCues.PostPortalNotice) != 0)
        {
            Notices?.PostNotice(PortalSpaceMachine.PortalNotice, PortalSpaceMachine.PortalNoticeTextType);
        }

        if ((cues & PortalCues.PlayExitPortal) != 0)
        {
            Sound?.PlayFromCenter(Assets.UiSoundTableId, ExitPortalSoundType);
        }

        if ((cues & PortalCues.SendLoginComplete) != 0)
        {
            SendLoginComplete(now);
        }

        // 5. A stall in state 3 (D30).
        extra |= Stall(now, frame.State);
        HoldStall(now);

        return extra == PortalCues.None ? frame : frame with { Cues = cues | extra };
    }

    /// <summary>
    /// Back to the world with nothing pending (the UI mode changed, e.g. the character list arrived): resets the machine
    /// (silently; see <see cref="PortalSpaceMachine.Reset"/>) and the stall warnings. The teleport flags are the session's
    /// and are left alone.
    /// </summary>
    public void Reset()
    {
        Machine.Reset();
        _stallStart = double.NaN;
        _stallWarnings = 0;
        _holdStart = double.NaN;
        _holdWarnings = 0;
        _events.Clear();
    }

    private void DrainTunnel()
    {
        _events.Clear();
        var count = Machine.Tunnel.DrainEvents(_events);
        TunnelEvents += count;
        if (Sound is null)
        {
            return;
        }

        var origin = PortalSpaceScene.TunnelHumOrigin;
        for (var i = 0; i < count; i++)
        {
            SoundHookRouter.Route(_events[i], origin, Sound, soundTableId: 0);
        }
    }

    private void SendLoginComplete(double now)
    {
        var sink = LoginComplete;
        if (sink is not null && !Progress.IsLocal)
        {
            LoginCompletesSent++;
            if (Log.IsEnabled(LogSeverity.Info))
            {
                Log.Info(LogCategory, "portal space ended: LoginComplete sent", new LogField("t", now));
            }

            sink.SendLoginComplete();
            return;
        }

        LoginCompletesSuppressed++;
        if (Log.IsEnabled(LogSeverity.Info))
        {
            Log.Info(
                LogCategory,
                Progress.IsLocal
                    ? "portal space ended after a local jump: LoginComplete not sent (offline explore mode)"
                    : "portal space ended: LoginComplete not sent (no session)",
                new LogField("t", now));
        }
    }

    private PortalCues ArrivedWithFailures(double now, in ReadinessReport readiness)
    {
        ArrivalsWithFailures++;
        if (Log.IsEnabled(LogSeverity.Warning))
        {
            Log.Warn(
                LogCategory,
                "the destination settled with failed pieces; portal space ends without them: " + readiness.Describe(),
                new LogField("t", now));
        }

        return PortalCues.ArrivedWithFailures;
    }

    /// <summary>D30: a loud line every 10 s while state 3 waits for an update that is not held.</summary>
    private PortalCues Stall(double now, TeleportAnimState state)
    {
        if (state != TeleportAnimState.InTunnel || !Progress.InProgress || Progress.IsHeld)
        {
            _stallStart = double.NaN;
            _stallWarnings = 0;
            return PortalCues.None;
        }

        if (double.IsNaN(_stallStart))
        {
            _stallStart = now;
            return PortalCues.None;
        }

        var waited = now - _stallStart;
        if (waited < StallWarningSeconds * (_stallWarnings + 1))
        {
            return PortalCues.None;
        }

        _stallWarnings++;
        StallWarnings++;
        if (Log.IsEnabled(LogSeverity.Warning))
        {
            Log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"portal space has waited {waited:F1} s in the tunnel: {StallReason()}"));
        }

        return PortalCues.ReadinessStalled;
    }

    /// <summary>
    /// A loud line every 10 s (D30's cadence) while Lifestone's logoff fix (D28) holds the tunnel up for the character list: a character
    /// list that never arrives would otherwise leave the tunnel on screen with nothing said.
    /// </summary>
    private void HoldStall(double now)
    {
        if (!Machine.IsHoldingLogOff)
        {
            _holdStart = double.NaN;
            _holdWarnings = 0;
            return;
        }

        if (double.IsNaN(_holdStart))
        {
            _holdStart = now;
            return;
        }

        var waited = now - _holdStart;
        if (waited < StallWarningSeconds * (_holdWarnings + 1))
        {
            return;
        }

        _holdWarnings++;
        LogOffHoldWarnings++;
        if (Log.IsEnabled(LogSeverity.Warning))
        {
            Log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"TODO(portal-logoff-hold): the logoff tunnel has been held {waited:F1} s waiting for the character list (D28's hold; the character list has not arrived)"));
        }
    }

    private string StallReason()
    {
        if (!Progress.HasPlayer)
        {
            return "there is no player";
        }

        if (Progress.WaitingForTeleport)
        {
            return "the server's new position for the player has not arrived";
        }

        return LastReadiness.IsReady ? "the update has not completed" : LastReadiness.Describe();
    }
}

using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M6 6 MH (closes <c>m5-autopilot-ui-reconnect</c>, map-menu.md 5 and 6.7): the autopilot's own reconnect back to the game screen, offline. The dial runs over a ready-made logic (idle stations, as <see cref="AutopilotDialAsync"/>'s) whose
/// scenario has the host's reconnect (<see cref="OwnTryCountingSession"/> over a stand-in login); the stand-in boots the session; the autopilot's own try (never the disconnected screen's button) brings the player back; then the game screen
/// is current again, a NEW game screen is wired to the dial (its chat, its tools button and its inspector: what the shell's "are wired to the dial" line reports, re-logged once for the new screen), the dial is still running and the scenario
/// counts <c>reconnects=1</c>. The stand-in's own login: the offline session refuses Login and EnterWorld (no server), and a re-run of the stand-in plays the visit's login itself (the person's button path proves it), so this check's
/// <see cref="IAutopilotSession"/> calls the same <see cref="GameSession.Reconnect"/> and waits for the player to be in the world; the live login half is <see cref="GameSessionAutopilotSession"/>'s (its loopback and gate rounds).
/// M6 6 FXU: the check's world is a mage's: the player holds a caster (the kit's wand, as the magic rows' <see cref="HoldCasterAsync"/>) before the dial starts, and again after the stand-in's re-login, whose world is made anew and
/// would lose it where a server keeps what he wields; so the audit's caster check passes on both arrivals instead of logging its no-caster ERROR.
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>The reconnect's waits for this check (the first try 2 s after the drop; the defaults' 5 s would only slow the run).</summary>
    private static readonly double[] ReconnectCheckWaits = [2, 5];

    private async Task AutopilotReconnectChecksAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        if (session is not GameSession game || _host.StandIn is not { } server || ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen before)
        {
            Check("the-autopilots-own-reconnect-is-checked-on-the-stand-in", false, "no stand-in session or no game screen");
            return;
        }

        var log = GodotLogSink.Instance;
        var options = new AutopilotOptions { ReconnectWaits = ReconnectCheckWaits };
        var own = 0;
        var caster = await HoldCasterAsync(session); // a mage's world: the audit's caster check finds one in his hands (0: one was held already)
        var login = new StandInRelogin(game, session, () => _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.InWorld);
        var reconnects = new OwnTryCountingSession(login, () => game.Reconnects, n => own += n);
        var logic = new AutopilotHostLogic(
            carry =>
            {
                var scenario = new AutopilotScenario(carry.Remaining(options), new IdleStations(), new JourneyContext { Character = "BSD Mage", Account = "test", Log = log, Travel = new PlaceTravel(), Spells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>() }, checkpoints: null, host: reconnects);
                carry.Apply(scenario);
                return scenario;
            },
            log,
            options)
        {
            ForeignReconnects = () => game.Reconnects - own, // as the real host's (AutopilotKit.ForeignReconnects): the person's presses only
        };
        var host = AutopilotHost.InstallWith(_main, _host, ui, logic, new AutopilotHostOptions());
        await Frames(3);
        var typed = AutopilotHost.TypeInChat(ui, "/auto");
        var started = await WaitFor(() => logic.Mode == AutopilotMode.Running && logic.Scenario is not null, 5.0);
        var heldBefore = session.State.Self.Inventory.EquippedAt((uint)EquipMask.Held) is not null;
        Check("the-dial-runs-before-the-drop", typed && started && before.Inspector?.Autopilot == logic, $"typed={typed} mode={logic.Mode} stage={logic.Scenario?.Stage} caster_held={heldBefore}");

        var sessionReconnects = game.Reconnects;
        server.Boot(0.2, "stand-in boot (the autopilot's own reconnect)");
        var dropped = await WaitFor(() => logic.Scenario is { Stage: "reconnecting" }, 15.0);
        Check("a-boot-under-the-dial-starts-the-autopilots-reconnect", dropped && logic.Mode == AutopilotMode.Running, $"stage={logic.Scenario?.Stage} mode={logic.Mode} mode_ui={(ui.Flow is { } bf ? UiModes.NameOf(bf.Flow.CurrentMode) : "none")}");

        var back = await WaitFor(() => logic.Scenario is { CycleTally.Reconnects: 1 } && ui.Flow?.Flow.CurrentMode == UiModes.GamePlay && ui.Screens?.Flow.CurrentScreen is GamePlayScreen, 90.0);
        await Frames(5); // the host wires the new screen on its next frame
        var screen = ui.Screens?.Flow.CurrentScreen as GamePlayScreen;
        Check(
            "the-autopilots-own-try-reconnects-not-the-button",
            back && login.Tries == 1 && own == 1 && game.Reconnects == sessionReconnects + 1,
            $"tries={login.Tries} own={own} session_reconnects={game.Reconnects - sessionReconnects} caster_rewielded=0x{login.Caster:X8} failure={login.LoginFailure ?? "none"}");
        Check("the-reconnect-returns-to-the-game-screen", back && screen is not null && !ReferenceEquals(screen, before), $"mode={(ui.Flow is { } gf ? UiModes.NameOf(gf.Flow.CurrentMode) : "none")} new_screen={screen is not null && !ReferenceEquals(screen, before)}");
        Check(
            "the-new-game-screen-is-wired-to-the-dial-again",
            screen is { Chat.Autopilot: not null, Hotkeys.AutopilotKeyHint: not null, ToolsUi.ToggleAutopilot: not null } && ReferenceEquals(screen.ToolsUi?.Autopilot, logic) && ReferenceEquals(screen.Inspector?.Autopilot, logic),
            $"chat={screen?.Chat?.Autopilot is not null} num_lock_hint={screen?.Hotkeys?.AutopilotKeyHint is not null} button={screen?.ToolsUi?.ToggleAutopilot is not null} panel={ReferenceEquals(screen?.ToolsUi?.Autopilot, logic)} inspector={ReferenceEquals(screen?.Inspector?.Autopilot, logic)}");
        var tally = logic.Scenario?.CycleTally.Reconnects ?? -1;
        Check("the-dial-is-still-running-with-reconnects-1", logic.Mode == AutopilotMode.Running && logic.Interrupted == 0 && tally == 1, $"mode={logic.Mode} interrupted={logic.Interrupted} reconnects={tally} stage={logic.Scenario?.Stage}");

        // Back to the host's own state for the checks after this one (the dial's disposal unlocks the keys, D145), as AutopilotDialAsync leaves it.
        host.Logic.Dispose();
        if (screen is { Chat: { } chat, Hotkeys: { } hotkeys, ToolsUi: { } panel, Inspector: { } inspector })
        {
            chat.Autopilot = null;
            hotkeys.AutopilotKeyHint = null;
            panel.ToggleAutopilot = null;
            inspector.Autopilot = null;
        }

        _main.RemoveChild(host);
        host.Free();
        await WaitFor(() => _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.InWorld, 60.0);
        await StowAsync(session, login.Caster != 0 ? login.Caster : caster); // the wand the re-login wielded (the first went with the old world), put away as the magic rows put theirs
        Check("the-reconnect-check-leaves-the-movement-keys-unlocked", !session.Actions.Move.KeyLock.Locked, $"keys_locked={session.Actions.Move.KeyLock.Locked}");
    }

    /// <summary>
    /// The reconnect's login on the stand-in: <see cref="GameSession.Reconnect"/> (the call the disconnected screen's button makes; the stand-in re-runs and plays the visit's login and entry itself), then the player in the world (the play flow's stage), present.
    /// A placeholder of the live <see cref="GameSessionAutopilotSession"/>, whose <see cref="LoginScenario"/> needs a server that answers Login and EnterWorld. M6 6 FXU: the stand-in's world is made anew on the re-login, so the wand
    /// the player held is gone (a server keeps it); before the success the re-login puts the kit's wand in his hands again, checked before (none held), acted (created, wielded) and checked after (the wield confirmed and the held
    /// slot filled); a refusal fails the login by name (<see cref="LoginFailure"/>), never a silent retry.
    /// </summary>
    private sealed class StandInRelogin(GameSession game, IGameSessionView view, Func<bool> playInWorld) : IAutopilotSession
    {
        private ToolHandle<ItemResult>? _spawn;
        private ActionHandle? _wield;

        public int Tries { get; private set; }

        public string? LoginFailure { get; private set; }

        /// <summary>The wand the re-login wielded (0 before, or when one was held already).</summary>
        public uint Caster { get; private set; }

        public void Reconnect()
        {
            Tries++;
            LoginFailure = null;
            (_spawn, _wield, Caster) = (null, null, 0);
            game.Reconnect();
        }

        public ScenarioStatus StepLogin(double now)
        {
            if (Tries == 0)
            {
                LoginFailure = "StepLogin was called before Reconnect";
                return ScenarioStatus.Failed;
            }

            var state = game.State;
            if (!(playInWorld() && state.Self.Guid != 0 && state.Self.Travel == SelfTravel.Present)) // the play flow's in-world stage (the Game names no network-layer type: ArchitectureTests); the autopilot checks the session's phase itself after the success
            {
                return ScenarioStatus.Running;
            }

            if (_spawn is null)
            {
                if (state.Self.Inventory.EquippedAt((uint)EquipMask.Held) is not null)
                {
                    return ScenarioStatus.Succeeded;
                }

                _spawn = view.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
                Log.Info(LogCategory, "reconnect check: the stand-in's new world holds no caster: the re-login creates the kit's wand and wields it (a server keeps what he wields)");
                return ScenarioStatus.Running;
            }

            if (!_spawn.IsCompleted)
            {
                return ScenarioStatus.Running;
            }

            if (_wield is null)
            {
                if (_spawn.Result?.Guid is not (> 0 and var guid))
                {
                    return Fail($"the stand-in did not create the wand ({_spawn.Outcome?.Kind.ToString() ?? "pending"})");
                }

                Caster = guid;
                _wield = view.Actions.Wield(guid, EquipMask.Held, ActionSource.Host);
                return ScenarioStatus.Running;
            }

            if (!_wield.IsCompleted)
            {
                return ScenarioStatus.Running;
            }

            if (_wield.Outcome?.Kind != ActionOutcomeKind.Confirmed || state.Self.Inventory.EquippedAt((uint)EquipMask.Held) != Caster)
            {
                return Fail($"the wand 0x{Caster:X8} was not wielded ({_wield.Outcome?.Kind.ToString() ?? "pending"}: {_wield.Outcome?.Detail ?? "no detail"})");
            }

            Log.Info(LogCategory, $"reconnect check: the re-login wielded the kit's wand 0x{Caster:X8} (checked after: the held slot holds it)");
            return ScenarioStatus.Succeeded;
        }

        private static ScenarioStatus Fail(StandInRelogin self, string why)
        {
            self.LoginFailure = why;
            Log.Warn(LogCategory, "reconnect check: the re-login failed: " + why);
            return ScenarioStatus.Failed;
        }

        private ScenarioStatus Fail(string why) => Fail(this, why);
    }
}

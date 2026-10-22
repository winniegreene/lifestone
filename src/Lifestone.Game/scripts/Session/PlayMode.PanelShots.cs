using Godot;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;


namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--play-panel-shots=DIR</c> (M3 2.2 s0, <see cref="PanelFramesPlan"/>): in a plain <c>--play</c> run, once the player is placed and the arrival has settled, each registered panel page is opened in turn
/// through its toolbar action, saved as <c>DIR/&lt;panelId&gt;-&lt;name&gt;.png</c> with the developer HUD hidden (as <c>--play-screenshot</c> does), and closed; then the run quits like <c>--play-screenshot</c>
/// (0, or 1 when a frame could not be saved). The inventory page is taken twice (M4 3.1 p, <c>m4-panelshot-mainpack</c>): with the first side pack selected (its own file, unchanged) and then with the main pack selected and listed,
/// so the hunter's looted items show (<see cref="PanelFramesPlan.MainPackFileName"/>, <c>...-mainpack.png</c>). With no registered page it logs <see cref="PanelFramesPlan.NoPanelText"/> and quits. With <c>--play-checks</c> (the stand-in) it only saves and the checks go on.
/// </summary>
public sealed partial class PlayMode
{
    /// <summary>Frames a page gets to draw after it opens, before its frame is taken.</summary>
    private const int PanelShotSettleFrames = 40; // 8 was too few for the spellbook's first open on BSD Mage (247 spells filled 8 a frame): its shot showed the page before (M3 2.2 live gate)

    private enum PanelShotPhase
    {
        Open,
        Settle,
        Capture,
        AttackSetup,
        AttackCharge,
        SidePack,
        MainPack,
        AttackStow,
    }

    private enum ContainerShotPhase
    {
        Find,
        Opening,
        Drawing,
        Capture,
        Closing,
        Done,
    }

    private float _panelShotRestorePower;

    private double _panelShotWait;
    private int _panelShotWindowIndex;
    private int _panelShotRestoreMode;
    private int _panelShotRestoreTab; // the spellcasting window's open tab before its frame (M3 3.2 fix B)
    private PanelShotPhase _panelShotWindowPhase;
    private bool _panelShotsDone;
    private bool _panelShotTabOpen;
    private IReadOnlyList<PanelFrame>? _panelShotPages;
    private int _panelShotIndex;
    private int _panelShotFrames;
    private int _panelShotCaptured;
    private int _panelShotFailures;
    private PanelShotPhase _panelShotPhase;
    private bool _panelShotSidePackTried; // M4 2.2 s4: the inventory page's frame selects the first side pack once
    private uint _panelShotSidePack;
    private ActionHandle? _panelShotStow; // M4 3.1 p: the caster put away before the Melee mode of the c1-powerbar frame (the combat-mode guard refuses melee with one in hand), and its guid for the put-back
    private uint _panelShotStowed;
    private bool _panelShotMainPack; // M4 3.1 p: the inventory page's second shot, the main pack selected (PanelFramesPlan.MainPackFileName), is the one being taken
    private ContainerShotPhase _containerShotPhase; // M4 2.2 s4: the container window's frame (open a container within 10 m, wait for its entries, save, close)
    private ActionHandle? _containerShotHandle;
    private uint _containerShotGuid;
    private double _containerShotWait;
    private int _containerShotFrames;

    private void StepPanelShots(double delta, string directory)
    {
        if (!IsPlayerPresent() || _rig is not { } rig || rig.Controller.Placing || rig.Controller.Position.CellId == 0)
        {
            _panelShotWait = 0;
            return;
        }

        _panelShotWait += delta;
        if (_panelShotWait < _services.Options.Play.ScreenshotAfterSeconds
            || _ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { IsBuilt: true, PanelHost: { } host, PanelControllers: { } panels } shell)
        {
            return;
        }

        _panelShotPages ??= PanelFramesPlan.FramesFor(panels.RegisteredPages);
        if (_panelShotPages.Count == 0)
        {
            Log.Info(LogCategory, PanelFramesPlan.NoPanelText);
            FinishPanelShots(0);
            return;
        }

        if (_panelShotIndex >= _panelShotPages.Count)
        {
            if (StepWindowShots(shell, directory))
            {
                return; // the combat window's and the power bar's frames (M3 3.2 pa) follow the pages'
            }

            if (StepContainerShot(shell, directory, delta))
            {
                return; // M4 2.2 s4: the container window's frame follows the windows'
            }

            if (StepHotbarShot(shell, directory))
            {
                return; // M4 3.2 h2: the hotbar's shot follows the container window's
            }

            if (StepExamineShot(shell, directory, delta)) // M4 3.2 s4c: the examine panel's frame follows the container window's (PlayMode.PanelShots.Examine.cs)
            {
                return;
            }

            Log.Info(LogCategory, PanelFramesPlan.Verdict(_panelShotCaptured, _panelShotFailures));
            FinishPanelShots(_panelShotFailures == 0 ? 0 : 1);
            return;
        }

        var frame = _panelShotPages[_panelShotIndex];
        var page = frame.Page;
        switch (_panelShotPhase)
        {
            case PanelShotPhase.Open:
                if (host.CurrentPanel != page.PanelId)
                {
                    host.Toggle(page.PanelId); // what the page's toolbar button does
                }

                _panelShotTabOpen = host.CurrentPanel == page.PanelId && PanelFramesPlan.OpenTab(frame, host.ElementFor(page.PanelId)); // a click on the tab button

                _panelShotFrames = 0;
                _panelShotPhase = PanelShotPhase.Settle;
                break;
            case PanelShotPhase.Settle:
                if (++_panelShotFrames >= PanelShotSettleFrames)
                {
                    if (page.PanelId == GameInventoryPage.Panel && !_panelShotSidePackTried && panels.ControllersOf(page.PanelId).OfType<GameInventoryPage>().FirstOrDefault() is { } inventoryPage)
                    {
                        // M4 2.2 s4: the inventory page's frame (the pack list, the backpack column with its side packs, the paperdoll) is taken with the first side pack selected when the character has one.
                        _panelShotSidePackTried = true;
                        var sidePack = PanelFramesPlan.FirstSidePack(inventoryPage);
                        Log.Info(LogCategory, sidePack == 0 ? "inventory page: the character has no side pack; the frame shows the backpack" : "inventory page: the first side pack is selected for the frame", ("pack", $"0x{sidePack:X8}"));
                        if (sidePack != 0)
                        {
                            _panelShotSidePack = sidePack;
                            inventoryPage.ShowContents(sidePack); // what a click on the pack's cell does
                            _panelShotFrames = 0;
                            _panelShotPhase = PanelShotPhase.SidePack;
                            break;
                        }
                    }

                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _panelShotFrames = 0;
                    _panelShotPhase = PanelShotPhase.Capture;
                }

                break;
            case PanelShotPhase.SidePack:
            {
                // The page lists the selected pack's contents (its pack list holds one cell for each of the pack's items that has landed) or, after a second, the frame is taken as it is, said by name.
                var objects = _host.Session?.State.Objects;
                var items = _host.Session?.State.Self.Inventory.ItemsIn(_panelShotSidePack);
                var sidePage = panels.ControllersOf(page.PanelId).OfType<GameInventoryPage>().FirstOrDefault();
                var listed = sidePage is not null && objects is not null && items is not null && sidePage.OpenContainer == _panelShotSidePack && sidePage.PackList.OccupiedCount == items.Count(e => objects.Contains(e.Guid));
                if (listed || ++_panelShotFrames > 60)
                {
                    Log.Info(LogCategory, "inventory page: the selected side pack " + (listed ? "is listed" : "did not finish listing within 60 frames; the frame is taken as it is"), ("pack", $"0x{_panelShotSidePack:X8}"), ("contents_title", sidePage?.ContentsTitle), ("held", items?.Count), ("listed", sidePage?.PackList.OccupiedCount));
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _panelShotFrames = 0;
                    _panelShotPhase = PanelShotPhase.Capture;
                }

                break;
            }

            case PanelShotPhase.MainPack:
            {
                // M4 3.1 p (m4-panelshot-mainpack): the page lists the main pack (one cell for each held item whose object has landed), or after a second the frame is taken as it is, said by name.
                var mainPage = panels.ControllersOf(page.PanelId).OfType<GameInventoryPage>().FirstOrDefault();
                var state = _host.Session?.State;
                var mainListed = mainPage is not null && state is not null && PanelFramesPlan.MainPackListed(mainPage, state);
                if (mainListed || ++_panelShotFrames > 60)
                {
                    Log.Info(LogCategory, "inventory page: the main pack " + (mainListed ? "is listed" : "did not finish listing within 60 frames; the frame is taken as it is"), ("contents_title", mainPage?.ContentsTitle), ("held", state?.Self.Inventory.Pack.Count), ("listed", mainPage?.PackList.OccupiedCount));
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _panelShotFrames = 0;
                    _panelShotPhase = PanelShotPhase.Capture;
                }

                break;
            }

            default:
                if (++_panelShotFrames < PlayShotHiddenFrames)
                {
                    break;
                }

                var shown = _panelShotTabOpen && host.CurrentPanel == page.PanelId && host.ElementFor(page.PanelId)?.Visible == true && PanelFramesPlan.OpenTab(frame, host.ElementFor(page.PanelId));
                var path = System.IO.Path.Combine(directory, _panelShotMainPack ? PanelFramesPlan.MainPackFileName(frame) : PanelFramesPlan.FileName(frame));
                var saved = shown && ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-panel-shots"), ("panel", page.PanelId), ("developer_hud", "hidden"), ("main_pack", _panelShotMainPack));
                RestoreDeveloperHud();
                if (saved)
                {
                    _panelShotCaptured++;
                    Log.Info(LogCategory, "panel shot saved", ("panel", page.PanelId), ("name", page.Name), ("tab", frame.TabName), ("main_pack", _panelShotMainPack), ("path", path));
                }
                else
                {
                    _panelShotFailures++;
                    Log.Error(LogCategory, "panel shot failed", ("panel", page.PanelId), ("name", page.Name), ("tab", frame.TabName), ("main_pack", _panelShotMainPack), ("shown", shown));
                }

                if (PanelFramesPlan.HasMainPackShot(frame) && !_panelShotMainPack
                    && panels.ControllersOf(page.PanelId).OfType<GameInventoryPage>().FirstOrDefault() is { } packPage && _host.Session is { } packSession)
                {
                    // M4 3.1 p: the main-pack shot follows the first (the side pack's, or the backpack's when the character has no side pack): the page is put on the player's own pack and listed again.
                    _panelShotMainPack = true;
                    _panelShotSidePack = 0;
                    packPage.ShowContents(PanelFramesPlan.MainPackOwner(packSession.State)); // what a click on the player's own cell does
                    _panelShotFrames = 0;
                    _panelShotPhase = PanelShotPhase.MainPack;
                    break;
                }

                _panelShotMainPack = false;
                if (_panelShotSidePack != 0)
                {
                    // Put back: the page shows the backpack again (the character's own selection, not the frame's).
                    var self = _host.Session?.State.Self;
                    if (self is not null)
                    {
                        panels.ControllersOf(page.PanelId).OfType<GameInventoryPage>().FirstOrDefault()?.ShowContents(self.Inventory.Owner != 0 ? self.Inventory.Owner : self.Guid);
                    }

                    _panelShotSidePack = 0;
                }

                if (host.CurrentPanel == page.PanelId)
                {
                    host.Toggle(page.PanelId); // closes the page
                }

                _panelShotIndex++;
                _panelShotPhase = PanelShotPhase.Open;
                break;
        }
    }

    private void FinishPanelShots(int exitCode)
    {
        _panelShotsDone = true;
        if (_services.Options.Checks != GameCheckSuite.Play)
        {
            GetTree().Quit(exitCode); // as --play-screenshot: with --play-checks it only saves and the checks go on
        }
    }

    // M3 3.2 pa and pb, M4 1.3 s1: after the pages, the floaty windows that are not host pages (PanelFramesPlan.AllWindowFrames: c1-combat, c1-powerbar, c2-spellcasting). Each is shown by the UI's own mode notice (never a session action: a live
    // run's character keeps its mode), the shot taken, the notice put back. c1-powerbar is taken during a REAL attack (WindowFrame.ByAttack): the stand-in's tusker placed, the player put in melee mode, the tusker selected, the Medium button
    // clicked through the UI's own element messages, the shot taken while the power charges, then the run cancelled and the mode and selection put back. Nothing is begun by hand. A run with no stand-in (a live server has no tusker) skips
    // that frame, loudly (TODO(m4-powerbar-frame-live): lane s2's live hunt leg takes its frames). True while frames remain.
    private bool StepWindowShots(GamePlayScreen shell, string directory)
    {
        var frames = PanelFramesPlan.AllWindowFrames; // pa's two, then pb's c2-spellcasting
        if (_panelShotWindowIndex >= frames.Count)
        {
            return false;
        }

        var frame = frames[_panelShotWindowIndex];
        var combat = shell.Root?.GetChildRecursive(GameCombatPanel.WindowId) as GameCombatPanel;
        var bar = shell.Root?.GetChildRecursive(GamePowerBar.WindowId) as GamePowerBar;
        var spells = shell.Root?.GetChildRecursive(GameSpellcastingPanel.WindowId) as GameSpellcastingPanel;
        if (combat is null || bar is null || spells is null)
        {
            Log.Error(LogCategory, "window shot failed: the shell has no combat window, power bar or spellcasting window", ("window", frame.Name));
            _panelShotFailures++;
            _panelShotWindowIndex++;
            return true;
        }

        switch (_panelShotWindowPhase)
        {
            case PanelShotPhase.Open:
                if (frame.ByAttack)
                {
                    if (_host.StandIn is not { } server || _host.Session is not { } attackSession)
                    {
                        Log.Warn(LogCategory, "TODO(m4-powerbar-frame-live): the c1-powerbar frame is taken during an attack on the stand-in's tusker; this run has no stand-in, so the frame is skipped", ("window", frame.Name));
                        _panelShotWindowIndex++;
                        break;
                    }

                    _panelShotRestoreMode = combat.Mode;
                    _panelShotRestorePower = combat.RequestedPower;
                    server.PlayerHitRate = 1.0;

                    // M4 3.1 p (lead's note, lane g's combat-mode guard): SetCombatMode(Melee) is Refused while a caster is in hand, so what is held is put away first through the Action API (what PlayChecks.StowHeldAsync does for
                    // the rows), and wielded again after the shot; a caster that cannot be put away skips the frame, said by name, never a request for a refused mode.
                    if (attackSession.State.Self.Inventory.EquippedAt((uint)EquipMask.Held) is { } held)
                    {
                        _panelShotStowed = held;
                        _panelShotStow = attackSession.Actions.Unwield(held, Lifestone.Client.Session.Contracts.ActionSource.Host);
                        _panelShotFrames = 0;
                        _panelShotWindowPhase = PanelShotPhase.AttackStow;
                        break;
                    }

                    BeginAttackFrame(attackSession, server);
                    break;
                }

                _panelShotRestoreMode = combat.Mode;
                combat.SetCombatMode(frame.CombatMode);
                spells.SetCombatMode(frame.CombatMode); // M3 3.2 pb: the spellcasting window shows in Magic mode only (the UI's notice, never a session action)
                if (frame.WindowId == GameSpellcastingPanel.WindowId)
                {
                    // M3 3.2 fix B (3.2-R7): the first tab whose bar holds spells (live, BSD Mage's sit on bars 1 and 2), opened as a tab click; put back after the shot.
                    _panelShotRestoreTab = spells.OpenTab;
                    var choice = PanelFramesPlan.OpenSpellcastingFrameTab(spells);
                    Log.Info(LogCategory, "spellcasting shot tab", ("window", frame.Name), ("tab", choice.Tab), ("holds_spells", choice.HoldsSpells), ("opened", choice.Opened));
                }

                _panelShotFrames = 0;
                _panelShotWindowPhase = PanelShotPhase.Settle;
                break;
            case PanelShotPhase.AttackStow:
            {
                if (_host.StandIn is not { } stowServer || _host.Session is not { } stowSession)
                {
                    _panelShotWindowIndex++;
                    _panelShotWindowPhase = PanelShotPhase.Open;
                    break;
                }

                if (_panelShotStow?.Outcome is { } stowed)
                {
                    if (stowed.Kind == Lifestone.Client.Session.Contracts.ActionOutcomeKind.Confirmed)
                    {
                        BeginAttackFrame(stowSession, stowServer);
                        break;
                    }

                    Log.Error(LogCategory, "panel shot failed: the caster in hand was not put away, and the guard refuses the Melee mode with one (the frame is skipped)", ("window", frame.Name), ("outcome", stowed.Kind.ToString()), ("detail", stowed.Detail));
                    _panelShotFailures++;
                    _panelShotStowed = 0;
                    _panelShotWindowIndex++;
                    _panelShotWindowPhase = PanelShotPhase.Open;
                }
                else if (++_panelShotFrames > 900)
                {
                    Log.Error(LogCategory, "panel shot failed: putting the caster away got no answer within 900 frames (the frame is skipped)", ("window", frame.Name));
                    _panelShotFailures++;
                    _panelShotStowed = 0;
                    _panelShotWindowIndex++;
                    _panelShotWindowPhase = PanelShotPhase.Open;
                }

                break;
            }

            case PanelShotPhase.AttackSetup:
            {
                // The mode is melee and the tusker is in the object table: select it, ask for a full bar, press and click the Medium button (the mouse down starts the request, the click ends it).
                var attackSession = _host.Session!;
                var ready = attackSession.State.Self.Qualities.CombatMode == Lifestone.Client.Session.Contracts.CombatModes.Melee && attackSession.State.Objects.Contains(PanelFramesPlan.AttackFrameTusker);
                if (!ready)
                {
                    if (++_panelShotFrames > 900)
                    {
                        Log.Error(LogCategory, "panel shot failed: the player did not enter melee mode or the tusker did not arrive", ("window", frame.Name));
                        _panelShotFailures++;
                        RestoreStowedCaster(attackSession);
                        _panelShotWindowIndex++;
                        _panelShotWindowPhase = PanelShotPhase.Open;
                    }

                    break;
                }

                combat.SetCombatMode(frame.CombatMode);
                Selection.Select(PanelFramesPlan.AttackFrameTusker);
                (combat.GetChildRecursive(GameCombatPanel.SliderId) as Lifestone.Client.RetailUi.Widgets.UiScrollbar)?.SetScrollbarPosition(PanelFramesPlan.AttackFramePower);
                var medium = combat.HeightButton(2);
                medium?.BroadcastElementMessage(Lifestone.Client.RetailUi.Core.UiMessageIds.MouseDown);
                medium?.BroadcastElementMessage(Lifestone.Client.RetailUi.Core.UiMessageIds.ButtonClick);
                _panelShotFrames = 0;
                _panelShotWindowPhase = PanelShotPhase.AttackCharge;
                break;
            }

            case PanelShotPhase.AttackCharge:
            {
                var meter = combat.GetChildRecursive(GameCombatPanel.MeterId) as Lifestone.Client.RetailUi.Widgets.UiMeter;
                if (meter is not null && combat.Attacks is { Building: true } && meter.Position >= PanelFramesPlan.AttackFrameLevel)
                {
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _panelShotFrames = 0;
                    _panelShotWindowPhase = PanelShotPhase.Capture;
                }
                else if (++_panelShotFrames > 900)
                {
                    Log.Error(LogCategory, "panel shot failed: the meter did not charge after the Medium click", ("window", frame.Name), ("level", meter?.Position));
                    _panelShotFailures++;
                    _panelShotWindowPhase = PanelShotPhase.Capture;
                    _panelShotFrames = PlayShotHiddenFrames;
                }

                break;
            }
            case PanelShotPhase.Settle:
                if (++_panelShotFrames >= PanelShotSettleFrames)
                {
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _panelShotFrames = 0;
                    _panelShotWindowPhase = PanelShotPhase.Capture;
                }

                break;
            default:
                if (++_panelShotFrames < PlayShotHiddenFrames)
                {
                    break;
                }

                var shown = shell.Root?.GetChildRecursive(frame.WindowId)?.IsShown == true;
                var path = System.IO.Path.Combine(directory, PanelFramesPlan.FileName(frame));
                var saved = shown && ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-panel-shots"), ("window", frame.Name), ("developer_hud", "hidden"), ("meter_level", frame.ByAttack ? (combat.GetChildRecursive(GameCombatPanel.MeterId) as Lifestone.Client.RetailUi.Widgets.UiMeter)?.Position : null));
                RestoreDeveloperHud();
                if (saved)
                {
                    _panelShotCaptured++;
                    Log.Info(LogCategory, "panel shot saved", ("window", frame.Name), ("path", path));
                }
                else
                {
                    _panelShotFailures++;
                    Log.Error(LogCategory, "panel shot failed", ("window", frame.Name), ("shown", shown), ("combat_shown", combat.IsShown));
                }

                if (frame.ByAttack && _host.Session is { } doneSession)
                {
                    // Put back: the run cancelled, the peace mode, nothing selected, the slider where it was.
                    doneSession.Actions.CancelAttack(Lifestone.Client.Session.Contracts.ActionSource.Host);
                    doneSession.Actions.SetCombatMode(Lifestone.Client.Session.Contracts.CombatMode.NonCombat, Lifestone.Client.Session.Contracts.ActionSource.Host);
                    RestoreStowedCaster(doneSession);
                    Selection.Clear();
                    (combat.GetChildRecursive(GameCombatPanel.SliderId) as Lifestone.Client.RetailUi.Widgets.UiScrollbar)?.SetScrollbarPosition(_panelShotRestorePower);
                }

                if (frame.WindowId == GameSpellcastingPanel.WindowId)
                {
                    spells.OpenTabByClick(_panelShotRestoreTab);
                }

                combat.SetCombatMode(_panelShotRestoreMode);
                spells.SetCombatMode(_panelShotRestoreMode);
                _panelShotWindowIndex++;
                _panelShotWindowPhase = PanelShotPhase.Open;
                break;
        }

        return true;
    }

    /// <summary>The Melee mode, the tusker and the next phase of the c1-powerbar frame (after the held caster, if any, is away).</summary>
    private void BeginAttackFrame(IGameSessionView session, Lifestone.Client.Session.StandIn.StandInServer server)
    {
        session.Actions.SetCombatMode(Lifestone.Client.Session.Contracts.CombatMode.Melee, Lifestone.Client.Session.Contracts.ActionSource.Host);
        PanelFramesPlan.PlaceAttackFrameTusker(session, server);
        _panelShotFrames = 0;
        _panelShotWindowPhase = PanelShotPhase.AttackSetup;
    }

    /// <summary>Wields again the caster the c1-powerbar frame put away (M4 3.1 p), so the character is as the frame found it; nothing when none was put away.</summary>
    private void RestoreStowedCaster(IGameSessionView session)
    {
        if (_panelShotStowed == 0)
        {
            return;
        }

        session.Actions.Wield(_panelShotStowed, EquipMask.Held, Lifestone.Client.Session.Contracts.ActionSource.Host);
        _panelShotStowed = 0;
        _panelShotStow = null;
    }

    // M4 2.2 s4: the container window's frame (PanelFramesPlan.PanelShotExtraFrames). When a container object (a "Chest", a corpse) is within 10 m, it is opened through IActionApi.OpenContainer (ActionSource.Host), the frame is taken once the window
    // shows its entries (GameContainerPanel.DrawState: shown, every entry listed, every icon drawn) and the container is closed again; when none is in range the run logs PanelFramesPlan.NoContainerText (not a failure). An open the server refuses
    // is logged by name and no frame is taken (not counted as a failure: a locked chest is the world's answer, not a client fault); a window that does not draw within 5 s is a failure. True while the frame is in work.
    private bool StepContainerShot(GamePlayScreen shell, string directory, double delta)
    {
        if (_containerShotPhase == ContainerShotPhase.Done)
        {
            return false;
        }

        var session = _host.Session;
        var window = shell.Root?.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel;
        if (session is null || window is null)
        {
            Log.Error(LogCategory, "container window shot failed: the shell has no session or no container window", ("session", session is not null), ("window", window is not null));
            _panelShotFailures++;
            _containerShotPhase = ContainerShotPhase.Done;
            return false;
        }

        _containerShotWait += delta;
        switch (_containerShotPhase)
        {
            case ContainerShotPhase.Find:
            {
                _containerShotGuid = PanelFramesPlan.ContainerWithin(session.State, PanelFramesPlan.ContainerFrameMetres);
                if (_containerShotGuid == 0)
                {
                    Log.Info(LogCategory, PanelFramesPlan.NoContainerText);
                    _containerShotPhase = ContainerShotPhase.Done;
                    return false;
                }

                var name = session.State.Objects.TryGet(_containerShotGuid, out var container) ? container.Name : "?";
                Log.Info(LogCategory, "container window shot: opening the nearest container", ("container", $"0x{_containerShotGuid:X8}"), ("name", name));
                _containerShotHandle = session.Actions.OpenContainer(_containerShotGuid, ActionSource.Host);
                _containerShotWait = 0;
                _containerShotPhase = ContainerShotPhase.Opening;
                return true;
            }

            case ContainerShotPhase.Opening:
                if (_containerShotHandle?.Outcome is { } opened)
                {
                    if (opened.Kind == ActionOutcomeKind.Confirmed)
                    {
                        _containerShotWait = 0;
                        _containerShotPhase = ContainerShotPhase.Drawing;
                    }
                    else
                    {
                        Log.Warn(LogCategory, "container window shot: the open was not confirmed; no frame (the world's answer, not counted as a failure)", ("container", $"0x{_containerShotGuid:X8}"), ("outcome", opened.Kind.ToString()), ("detail", opened.Detail));
                        _containerShotPhase = ContainerShotPhase.Done;
                        return false;
                    }
                }
                else if (_containerShotWait > 30.0)
                {
                    Log.Error(LogCategory, "container window shot failed: the open got no answer within 30 s", ("container", $"0x{_containerShotGuid:X8}"));
                    _panelShotFailures++;
                    _containerShotPhase = ContainerShotPhase.Done;
                    return false;
                }

                return true;
            case ContainerShotPhase.Drawing:
                if (window.DrawState == ContainerWindowDraw.Drawn)
                {
                    HideDeveloperHud();
                    _playShotHiddenFrames = 0;
                    _containerShotFrames = 0;
                    _containerShotPhase = ContainerShotPhase.Capture;
                }
                else if (_containerShotWait > 5.0)
                {
                    Log.Error(LogCategory, "container window shot failed: the window did not draw its entries within 5 s", ("container", $"0x{_containerShotGuid:X8}"), ("window_draw", window.DrawState.ToString()), ("cells", window.ItemRow?.CellCount ?? -1));
                    _panelShotFailures++;
                    CloseContainerShot(session);
                }

                return true;
            case ContainerShotPhase.Capture:
            {
                if (++_containerShotFrames < PlayShotHiddenFrames)
                {
                    return true;
                }

                var path = System.IO.Path.Combine(directory, PanelFramesPlan.ContainerWindowFrame + ".png");
                var saved = window.Visible && ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-panel-shots"), ("window", PanelFramesPlan.ContainerWindowFrame), ("container", $"0x{_containerShotGuid:X8}"), ("title", window.Title), ("cells", window.ItemRow?.CellCount ?? -1), ("developer_hud", "hidden"));
                RestoreDeveloperHud();
                if (saved)
                {
                    _panelShotCaptured++;
                    Log.Info(LogCategory, "panel shot saved", ("window", PanelFramesPlan.ContainerWindowFrame), ("title", window.Title), ("cells", window.ItemRow?.CellCount ?? -1), ("path", path));
                }
                else
                {
                    _panelShotFailures++;
                    Log.Error(LogCategory, "panel shot failed", ("window", PanelFramesPlan.ContainerWindowFrame), ("shown", window.Visible));
                }

                CloseContainerShot(session);
                return true;
            }

            default: // Closing
                if (_containerShotHandle?.Outcome is not null || _containerShotHandle is null || _containerShotWait > 15.0)
                {
                    _containerShotPhase = ContainerShotPhase.Done;
                    return false;
                }

                return true;
        }
    }

    // M4 3.2 h2: the hotbar's shot (PanelFramesPlan.HotbarShotFile). The toolbar shows the character's own shortcut bar, so the shot needs no setup: the developer HUD is hidden, the bar settles for PlayShotHiddenFrames frames, the window is saved.
    // A character with an empty bar gets the empty slots with their numbers (said in the log line). True while the shot is in work.
    private int _hotbarShotFrames;
    private bool _hotbarShotDone;

    private bool StepHotbarShot(GamePlayScreen shell, string directory)
    {
        if (_hotbarShotDone)
        {
            return false;
        }

        if (shell.ToolbarUi?.Hotbar is not { } hotbar)
        {
            Log.Error(LogCategory, "hotbar shot failed: the shell has no hotbar");
            _panelShotFailures++;
            _hotbarShotDone = true;
            return false;
        }

        if (_hotbarShotFrames++ == 0)
        {
            HideDeveloperHud();
            _playShotHiddenFrames = 0;
            return true;
        }

        if (_hotbarShotFrames < PlayShotHiddenFrames)
        {
            return true;
        }

        var path = System.IO.Path.Combine(directory, PanelFramesPlan.HotbarShotFile);
        var filled = Enumerable.Range(0, Lifestone.Client.RetailUi.Toolbar.GameHotbar.VisibleSlots).Count(slot => hotbar.CellOf(slot) is { IsEmpty: false });
        var saved = shell.Toolbar?.Visible == true && ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-panel-shots"), ("window", PanelFramesPlan.HotbarFrame), ("filled_slots", filled), ("developer_hud", "hidden"));
        RestoreDeveloperHud();
        if (saved)
        {
            _panelShotCaptured++;
            Log.Info(LogCategory, "panel shot saved", ("window", PanelFramesPlan.HotbarFrame), ("filled_slots", filled), ("empty_bar", filled == 0 ? "the character's bar holds nothing; the frame shows the empty slots" : "no"), ("path", path));
        }
        else
        {
            _panelShotFailures++;
            Log.Error(LogCategory, "panel shot failed", ("window", PanelFramesPlan.HotbarFrame), ("shown", shell.Toolbar?.Visible == true));
        }

        _hotbarShotDone = true;
        return false;
    }

    private void CloseContainerShot(IGameSessionView session)
    {
        _containerShotHandle = session.State.Containers.ViewedGuid != 0 ? session.Actions.CloseContainer(session.State.Containers.ViewedGuid, ActionSource.Host) : null;
        _containerShotWait = 0;
        _containerShotPhase = ContainerShotPhase.Closing;
    }
}

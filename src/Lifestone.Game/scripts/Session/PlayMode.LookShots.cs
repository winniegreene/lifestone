using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Player;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--play-look-shots=DIR</c> (M4 4.1 lane v0, D27; <see cref="LookShotsPlan"/>): the look capture, the instrument that reproduces the rendering quirks the owner sees in the window (a wielded bow not drawn, a stray glow from the
/// Healer's Heart and none when it comes out, a wand or an orb in a melee stance) and records what the avatar code holds about each item at each frame. In a play run, once the player is placed, the shell is built and the arrival has
/// settled, each kind of held item the character carries is wielded in turn through the Action API (the hands stowed first), framed close in peace (frame A), in the stance the toolbar's rule gives (frame B) and after it is
/// stowed (frame C), one <see cref="LookShotsPlan.Line"/> per frame; then the start state is put back and the run quits (0, or 1 when a frame could not be saved or the start state could not be put back; also under
/// <c>--play-checks</c>, which it cuts short). Offline (the stand-in, which carries no weapons) it is first given the real Mace 331, Shortbow 307, Arrow 300 and the Healer's Heart 8552 (their real models; the stand-in still lacks the weenie database). It reads the avatar set and the scene
/// (<see cref="AvatarBody.Parent"/>, <see cref="ServerObjectsLayer.IsDrawn"/>, the dynamic light list, the body's emitters) and changes nothing in the avatar code.
/// </summary>
public sealed partial class PlayMode
{
    private enum LookPhase
    {
        Waiting,
        Grant,
        Run,
        Done,
    }

    private LookPhase _lookPhase;
    private bool _lookShotsDone;
    private double _lookWait;
    private double _lookWaitLogged;
    private long _lookFrameNo;
    private LookShotsPlan? _lookPlan;
    private int _lookIndex;
    private bool _lookOpBegun;
    private double _lookOpElapsed;
    private ActionHandle? _lookHandle;
    private int _lookShotStage;
    private int _lookShotFrames;
    private int _lookCaptured;
    private int _lookFailures;
    private bool _lookCameraFramed;
    private float _lookCameraScaleBefore;
    private System.Numerics.Vector3 _lookCameraOffset;
    private LookCamera _lookCamera;
    private float _lookAim = float.NaN;
    private float _lookBodyBaseline = float.NaN;
    private double _lookCameraExtraSettle;
    private readonly List<AvatarNotice> _lookNotices = [];
    private readonly Dictionary<uint, string> _lookParentFail = [];
    private readonly Dictionary<uint, long> _lookDrawnFrame = [];
    private int _lookGrantIndex;
    private ActionHandle? _lookGrantHandle;
    private double _lookGrantElapsed;
    private uint _lookGrantGuid;
    private bool _lookGrantItemsRegistered;

    /// <summary>The stand-in items the offline run is given when the character carries none of that kind: the Mace 331, the Shortbow 307 and the Arrow 300 (ACE's real class ids, so their real models show; M4 4.1 fix2b) and the Healer's Heart (the kit's wand, which has its own Setup).</summary>
    private static readonly (uint Wcid, int? Stack)[] LookStandInGrants =
    [
        (LookShotsPlan.StandInMaceWcid, null),
        (LookShotsPlan.StandInBowWcid, null),
        (LookShotsPlan.StandInArrowWcid, LookShotsPlan.StandInArrowStack),
        (GearKit.Wand.Wcid, null),
    ];

    private void StepLookShots(double delta, string directory)
    {
        var session = _host.Session;
        var waitingFor = LookWaitingFor(session);
        if (waitingFor is not null || session is null || _rig is not { } rig)
        {
            _lookWait = 0;
            if (_lookPhase == LookPhase.Waiting && waitingFor is not null && (_lookWaitLogged += delta) >= 10.0)
            {
                _lookWaitLogged = 0;
                Log.Info(LogCategory, "look shots: waiting", ("for", waitingFor));
            }

            return;
        }

        _lookFrameNo++;
        _lookWait += delta;
        if (_lookPhase == LookPhase.Waiting)
        {
            if (_lookWait < _services.Options.Play.ScreenshotAfterSeconds)
            {
                return;
            }

            _lookPhase = _host.StandIn is not null ? LookPhase.Grant : LookPhase.Run;
            Log.Info(LogCategory, "look shots: begin", ("stand_in", _host.StandIn is not null), ("directory", directory));
            return;
        }

        if (_lookPhase == LookPhase.Grant)
        {
            StepLookGrant(session, delta);
            return;
        }

        if (_lookPhase != LookPhase.Run)
        {
            return;
        }

        if (_lookPlan is null)
        {
            BeginLookRun(session, rig);
            return;
        }

        DrainLookNotices();
        SampleLookDrawn();
        _lookOpElapsed += delta;
        if (_lookIndex >= _lookPlan.Ops.Count)
        {
            FinishLookShots(_lookFailures == 0 ? 0 : 1);
            return;
        }

        StepLookOp(session, rig, _lookPlan.Ops[_lookIndex], directory);
    }

    // What the capture is still waiting for before it may begin or go on, or null: the session, the placed player, the built shell, the world shown (no teleport tunnel), the player's avatar unhidden (the stand-in creates it hidden).
    private string? LookWaitingFor(GameSession? session)
    {
        if (session is null)
        {
            return "a session";
        }

        if (session.State.Self.Travel is not (SelfTravel.Present or SelfTravel.TeleportArrived) || _rig is not { } rig || rig.Controller.Placing || rig.Controller.Position.CellId == 0)
        {
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"the player to be present and placed (travel={session.State.Self.Travel} rig={_rig is not null} placing={_rig?.Controller.Placing} cell=0x{_rig?.Controller.Position.CellId:X8})");
        }

        if (_ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { IsBuilt: true })
        {
            return "the shell to be built";
        }

        if (_portal?.Director.Machine.TunnelVisible == true)
        {
            return "the portal tunnel to end"; // a teleport's tunnel (the stand-in visit's, 4 s after the unhide) restarts the wait
        }

        if (_avatars is null || !_avatars.TryGet(session.State.Self.Guid, out var self) || self.Body.IsHidden)
        {
            return "the player's avatar to be unhidden";
        }

        return null;
    }

    // Offline only: the stand-in carries no weapons, so the run is given the real Mace, Shortbow and Arrows and the kit's Healer's Heart (items are fine, D23), one at a time through the item verb, each only when no item of
    // that weenie class is held already. The stand-in's visit must know the kinds (it answers unknown-wcid otherwise).
    private void StepLookGrant(GameSession session, double delta)
    {
        if (!_lookGrantItemsRegistered)
        {
            _lookGrantItemsRegistered = true;
            var items = new Dictionary<uint, Lifestone.Client.Session.StandIn.StandInItemKind>(PlayChecks.Visit.Items ?? Lifestone.Client.Session.StandIn.StandInItemKind.Default)
            {
                [LookShotsPlan.StandInMaceWcid] = new("Mace", null),
                [LookShotsPlan.StandInBowWcid] = new("Shortbow", null),
                [LookShotsPlan.StandInArrowWcid] = new("Arrow", 250),
            };
            PlayChecks.Visit.Items = items;
        }

        if (_lookGrantHandle is { } pending)
        {
            _lookGrantElapsed += delta;
            if (!pending.IsCompleted && _lookGrantElapsed < LookShotsPlan.OperationSeconds)
            {
                return;
            }

            var created = (pending as ToolHandle<ItemResult>)?.Result;
            var landed = created is not null && session.State.Self.Inventory.Contains(created.Guid) && session.State.Objects.Contains(created.Guid);
            if (!landed && _lookGrantElapsed < LookShotsPlan.OperationSeconds)
            {
                return; // the item's CreateObject and its inventory entry follow the reply by a frame or two
            }

            Log.Info(LogCategory, "look shots: stand-in item granted", ("wcid", LookStandInGrants[_lookGrantIndex].Wcid), ("outcome", pending.Outcome?.Kind.ToString() ?? "pending"), ("guid", created is null ? "none" : $"0x{created.Guid:X8}"), ("landed", landed));
            _lookGrantHandle = null;
            _lookGrantIndex++;
        }

        while (_lookGrantIndex < LookStandInGrants.Length)
        {
            var (wcid, stack) = LookStandInGrants[_lookGrantIndex];
            if (HeldWcid(session.State, wcid))
            {
                _lookGrantIndex++;
                continue;
            }

            _lookGrantElapsed = 0;
            _lookGrantHandle = session.Tools.SpawnItem(wcid, stack, ActionSource.Host);
            return;
        }

        _lookPhase = LookPhase.Run;
    }

    private static bool HeldWcid(GameState state, uint wcid)
    {
        var inventory = state.Self.Inventory;
        for (var i = 0; i < inventory.Equipped.Count; i++)
        {
            if (state.Objects.TryGet(inventory.Equipped[i].Guid, out var worn) && worn.Weenie.WeenieClassId == wcid)
            {
                return true;
            }
        }

        for (var i = 0; i < inventory.Pack.Count; i++)
        {
            if (state.Objects.TryGet(inventory.Pack[i].Guid, out var carried) && carried.Weenie.WeenieClassId == wcid)
            {
                return true;
            }
        }

        return false;
    }

    private void BeginLookRun(GameSession session, PlayerRig rig)
    {
        var plan = LookShotsPlan.Build(session.State, _host.StandIn is not null ? LookShotsPlan.StandInHints : null);
        _lookPlan = plan;
        foreach (var note in plan.Notes)
        {
            Log.Warn(LogCategory, "look shots: " + note);
        }

        if (plan.Steps.Count == 0)
        {
            Log.Info(LogCategory, LookShotsPlan.NothingText);
            Log.Info(LogCategory, LookShotsPlan.Verdict(0, 0, 0));
            FinishLookShots(0);
            return;
        }

        foreach (var step in plan.Steps)
        {
            Log.Info(LogCategory, "look shots: step", ("step", step.Name), ("item", step.Item.Name), ("wcid", step.Item.Wcid), ("guid", $"0x{step.Item.Guid:X8}"), ("slot", $"0x{step.Item.Slot:X8}"), ("ammo", step.Ammo?.Name ?? "none"));
        }

        Log.Info(LogCategory, "look shots: start state", ("hands", string.Join(", ", plan.StartHands.Select(h => $"{h.Name} (wcid {h.Wcid}, slot 0x{h.Slot:X8})"))), ("mode", LookShotsPlan.ModeName((int)plan.StartMode)), ("ops", plan.Ops.Count));

        // The close third-person view (or the hunt's, --play-look-view): the chase camera pulled in and swung in front of the shoulder, the same for every frame of a step (put back by FinishLookShots).
        _lookCameraFramed = true;
        _lookCameraScaleBefore = rig.Chase.Model.Scale;
        _lookAim = float.NaN;
        _lookCameraExtraSettle = 0;
        AimLookCamera(rig, plan.Steps[0], 0f, "first set");
        Log.Info(LogCategory, "look shots: the camera is set", ("view", _services.Options.Play.LookShotsView.ToString()), ("scale_before", _lookCameraScaleBefore), ("scale", _lookCamera.Scale), ("heading_degrees", _lookCamera.HeadingDegrees), ("pivot_z", _lookCamera.PivotZ));
        _lookIndex = 0;
        _lookOpBegun = false;
        _lookOpElapsed = 0;
        _lookNotices.Clear();
        _ = _avatars?.DrainNotices(_lookNotices);
        _lookNotices.Clear();
    }

    private void DrainLookNotices()
    {
        if (_avatars is null)
        {
            return;
        }

        _lookNotices.Clear();
        _avatars.DrainNotices(_lookNotices);
        foreach (var notice in _lookNotices)
        {
            if (notice.Kind == AvatarNoticeKind.ParentFailed)
            {
                _lookParentFail[notice.Guid] = notice.Detail ?? "failed";
            }
        }
    }

    // The layer's IsDrawn answers for the layer's current frame only, and the order of this node's _Process against the layer's is not fixed, so the last frame each watched item was seen drawn is kept.
    private void SampleLookDrawn()
    {
        if (_layer is null || _lookPlan is null)
        {
            return;
        }

        foreach (var step in _lookPlan.Steps)
        {
            if (_layer.IsDrawn(step.Item.Guid))
            {
                _lookDrawnFrame[step.Item.Guid] = _lookFrameNo;
            }

            if (step.Ammo is { } ammo && _layer.IsDrawn(ammo.Guid))
            {
                _lookDrawnFrame[ammo.Guid] = _lookFrameNo;
            }
        }
    }

    private void StepLookOp(GameSession session, PlayerRig rig, LookOp op, string directory)
    {
        var state = session.State;
        switch (op.Kind)
        {
            case LookOpKind.Peace:
                if (!_lookOpBegun)
                {
                    if (state.Self.Qualities.CombatMode is 0 or CombatModes.NonCombat)
                    {
                        NextLookOp();
                        return;
                    }

                    BeginLookAction(session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host), "peace");
                    return;
                }

                FinishLookAction(op, "peace", fatal: false);
                break;
            case LookOpKind.Stow:
                if (!_lookOpBegun)
                {
                    if (op.Item is not { } held || !state.Self.Inventory.TryGetEquipped(held.Guid, out _))
                    {
                        NextLookOp();
                        return;
                    }

                    _lookParentFail.Remove(held.Guid);
                    BeginLookAction(session.Actions.MoveToPack(held.Guid, held.HomeContainer, held.HomePlacement, ActionSource.Host), "stow " + held.Name);
                    return;
                }

                FinishLookAction(op, "stow " + op.Item?.Name, fatal: false);
                break;
            case LookOpKind.Wield:
                if (!_lookOpBegun)
                {
                    var item = op.Item!.Value;
                    _lookParentFail.Remove(item.Guid);
                    BeginLookAction(session.Actions.Wield(item.Guid, (EquipMask)item.Slot, ActionSource.Host), "wield " + item.Name);
                    return;
                }

                FinishLookAction(op, "wield " + op.Item?.Name, fatal: op.Step < 0);
                break;
            case LookOpKind.EnterDefaultMode:
                if (!_lookOpBegun)
                {
                    var mode = CombatModeRules.DefaultMode(state, out var blockedBy);
                    if (mode == CombatMode.NonCombat)
                    {
                        Log.Info(LogCategory, "look refusal: the toolbar's rule gives no combat mode for what is wielded; the frame is taken in peace", ("step", _lookPlan!.Steps[op.Step].Name), ("blocked_by", blockedBy ?? "none"));
                        NextLookOp();
                        return;
                    }

                    var guard = CombatModeGuard.Check(mode, state);
                    if (guard is not null)
                    {
                        Log.Info(LogCategory, "look refusal: the combat-mode guard refuses the default mode", ("step", _lookPlan!.Steps[op.Step].Name), ("mode", mode), ("why", guard));
                    }

                    BeginLookAction(session.Actions.SetCombatMode(mode, ActionSource.Host), "enter " + mode);
                    return;
                }

                FinishLookAction(op, "enter the default mode", fatal: false);
                break;
            case LookOpKind.RestoreMode:
                if (!_lookOpBegun)
                {
                    BeginLookAction(session.Actions.SetCombatMode((CombatMode)_lookPlan!.StartMode, ActionSource.Host), "restore mode " + LookShotsPlan.ModeName((int)_lookPlan.StartMode));
                    return;
                }

                FinishLookAction(op, "restore the combat mode", fatal: false);
                break;
            case LookOpKind.Frame:
                StepLookFrame(session, rig, op, directory);
                break;
        }
    }

    private void BeginLookAction(ActionHandle handle, string what)
    {
        _lookHandle = handle;
        _lookOpBegun = true;
        _lookOpElapsed = 0;
        Log.Info(LogCategory, "look op: sent", ("op", what), ("action", handle.Summary));
    }

    // The action ends (its tracker's confirmation, a refusal or its own timeout) or the run's own limit passes; a refusal or a failure is logged as data and the run goes on (a put-back that fails is counted).
    private void FinishLookAction(LookOp op, string what, bool fatal)
    {
        if (_lookHandle is not { } handle)
        {
            NextLookOp();
            return;
        }

        if (!handle.IsCompleted && _lookOpElapsed < LookShotsPlan.OperationSeconds)
        {
            return;
        }

        var outcome = handle.Outcome;
        var ok = handle.IsCompleted && outcome is { Succeeded: true };
        Log.Info(LogCategory, ok ? "look op: done" : "look refusal: the action did not succeed", ("op", what), ("outcome", outcome?.Kind.ToString() ?? "pending"), ("detail", outcome?.Detail), ("code", outcome?.Code), ("seconds", _lookOpElapsed));
        if (!ok && fatal)
        {
            _lookFailures++;
        }

        NextLookOp();
    }

    private void NextLookOp()
    {
        _lookIndex++;
        _lookOpBegun = false;
        _lookHandle = null;
        _lookOpElapsed = 0;
        _lookShotStage = 0;
        _lookShotFrames = 0;
        _lookCameraExtraSettle = 0;
    }

    // M4 4.1 v1 and P3: the same front three-quarter view for every frame. Round 1's mage frames b and c were taken from behind and the v1 re-aim (a camera moved off its offset is aimed again) did not hold in the magic stance (4.1 section 8).
    // The camera follows the player's physics frame; the body is drawn from the avatars' frame, so before a frame is judged the camera's aim is the view's heading plus how far the drawn frame is turned from the camera's (AvatarFacing, 0 when
    // they agree, as offline), and a camera that was moved off its offset or direction, or whose aim changed, is aimed again (logged with what it was) and given a longer settle.
    private bool KeepLookCamera(PlayerRig rig, LookStep step)
    {
        var model = rig.Chase.Model;
        var moved = System.Numerics.Vector3.Distance(model.ViewerOffset, _lookCameraOffset) > 1e-3f || System.Numerics.Vector3.Distance(model.Direction, System.Numerics.Vector3.UnitY) > 1e-3f;
        var turn = DrawTurnFromCamera(rig) + BodyTurnInStance();
        var wanted = LookShotsPlan.CameraFor(_services.Options.Play.LookShotsView, step.Kind);
        var aim = LookShotsPlan.AimDegrees(wanted.HeadingDegrees, turn);
        var changed = wanted != _lookCamera || float.IsNaN(_lookAim) || MathF.Abs(Lifestone.Client.Camera.AvatarFacing.Wrap(aim - _lookAim)) > LookShotsPlan.AimToleranceDegrees;
        if (!moved && !changed)
        {
            return false;
        }

        Log.Warn(LogCategory, "look camera: it had been moved off the run's view or its aim changed; aimed again", ("offset", model.ViewerOffset), ("wanted", _lookCameraOffset), ("direction", model.Direction), ("aim_before", _lookAim), ("aim", aim), ("body_turn_deg", turn), ("step", step.Name));
        AimLookCamera(rig, step, turn, moved ? "moved" : "aim changed");
        _lookCameraExtraSettle = LookShotsPlan.SettleSeconds;
        return true;
    }

    private void AimLookCamera(PlayerRig rig, LookStep step, float turn, string why)
    {
        var camera = LookShotsPlan.CameraFor(_services.Options.Play.LookShotsView, step.Kind);
        var aim = LookShotsPlan.AimDegrees(camera.HeadingDegrees, turn);
        var model = rig.Chase.Model;
        model.Scale = camera.Scale;
        model.SetHeadingOffset(aim, camera.PivotZ);
        _lookCamera = camera;
        _lookAim = aim;
        _lookCameraOffset = model.ViewerOffset;
        Log.Debug(LogCategory, "look camera: aimed", ("why", why), ("step", step.Name), ("scale", camera.Scale), ("aim_degrees", aim), ("pivot_z", camera.PivotZ));
    }

    // P3: how far the stance turns the drawn body from where the peace frame of the step has it (the first part's turn from the object's frame, against the baseline read at frame A): the humans' bow and melee stances turn the root
    // part about 85 degrees, so a camera fixed to the object's frame sees the archer from the side or the back. 0 for a view that does not follow the body, before the baseline is read, and while the player is not in the draw.
    private float BodyTurnInStance()
    {
        if (!LookShotsPlan.FollowsBody(_services.Options.Play.LookShotsView) || _avatars is null || float.IsNaN(_lookBodyBaseline))
        {
            return 0f;
        }

        return PlayerBodyTurn() is { } now ? Lifestone.Client.Camera.AvatarFacing.Wrap(now - _lookBodyBaseline) : 0f;
    }

    private float? PlayerBodyTurn()
    {
        if (_avatars is null)
        {
            return null;
        }

        var frame = _avatars.Frame;
        foreach (var draw in frame.Avatars)
        {
            if (draw.IsPlayer && draw.PartCount > 0)
            {
                return Lifestone.Client.Camera.AvatarFacing.TurnDegrees(draw.Frame.Orientation, frame.Parts[draw.PartStart].Frame.Orientation);
            }
        }

        return null;
    }

    // P3: how far the player's drawn frame (the avatars' draw of this frame) is turned from the frame the camera follows, in degrees clockwise; 0 when the player is not in the draw.
    private float DrawTurnFromCamera(PlayerRig rig)
    {
        if (_avatars is null)
        {
            return 0f;
        }

        foreach (var draw in _avatars.Frame.Avatars)
        {
            if (draw.IsPlayer)
            {
                return Lifestone.Client.Camera.AvatarFacing.TurnDegrees(rig.Chase.Pose.Pivot.Frame.Orientation, draw.Frame.Orientation);
            }
        }

        return 0f;
    }

    // One frame: wait for it to settle (A: the parent event and the first drawn frames; B: the stance's transition; C: the stow), hide the developer HUD, take the shot two frames later, log its look line.
    private void StepLookFrame(GameSession session, PlayerRig rig, LookOp op, string directory)
    {
        var plan = _lookPlan!;
        var step = plan.Steps[op.Step];
        switch (_lookShotStage)
        {
            case 0:
            {
                if (KeepLookCamera(rig, step))
                {
                    _lookOpElapsed = 0; // the settle starts again from the re-aimed camera
                }

                var need = (op.Frame == 'b' ? LookShotsPlan.StanceSettleSeconds : LookShotsPlan.SettleSeconds) + _lookCameraExtraSettle;
                var parentReady = op.Frame != 'a' || (_avatars is not null && _avatars.TryGet(step.Item.Guid, out var held) && held.Body.Parent is not null);
                if (_lookOpElapsed < need || (!parentReady && _lookOpElapsed < need + 3.0))
                {
                    return;
                }

                HideDeveloperHud();
                _playShotHiddenFrames = 0;
                _lookShotFrames = 0;
                _lookShotStage = 1;
                return;
            }

            default:
                if (++_lookShotFrames < PlayShotHiddenFrames)
                {
                    return;
                }

                break;
        }

        if (KeepLookCamera(rig, step))
        {
            _lookShotStage = 0; // moved while the HUD was hidden: aim again and settle again before the shot
            _lookOpElapsed = 0;
            return;
        }

        if (op.Frame == 'a')
        {
            _lookBodyBaseline = PlayerBodyTurn() ?? float.NaN; // the peace pose is the step's reference for the body's turn in its stance
        }

        var path = System.IO.Path.Combine(directory, LookShotsPlan.FileName(step.Name, op.Frame));
        var facts = LookFactsOf(session, step, op.Frame);
        var camera = rig.Chase.Pose;
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-look-shots"), ("step", step.Name), ("which", op.Frame), ("developer_hud", "hidden"), ("camera_blocked", camera.Blocked), ("camera_offset", rig.Chase.Model.ViewerOffset), ("body_turn_deg", DrawTurnFromCamera(rig) + BodyTurnInStance()), ("body_baseline_deg", _lookBodyBaseline), ("aim_degrees", _lookAim));
        RestoreDeveloperHud();
        Log.Info(LogCategory, LookShotsPlan.Line(facts));
        if (saved)
        {
            _lookCaptured++;
        }
        else
        {
            _lookFailures++;
            Log.Error(LogCategory, "look shot failed", ("step", step.Name), ("which", op.Frame), ("path", path));
        }

        NextLookOp();
    }

    private LookFacts LookFactsOf(GameSession session, LookStep step, char frame)
    {
        var state = session.State;
        var (parent, drawn, light, particles) = AvatarFactsOf(step.Item.Guid);
        string? ammo = null;
        if (step.Ammo is { } arrows)
        {
            var (ammoParent, ammoDrawn, _, _) = AvatarFactsOf(arrows.Guid);
            ammo = $"{arrows.Name}(parent={ammoParent},drawn={(ammoDrawn is null ? "unknown" : ammoDrawn.Value ? "yes" : "no")})";
        }

        // The motion style the server last sent for the player: the player object's motion state (the stance word, else the interpreted style).
        string? stance = null;
        if (state.Objects.TryGet(state.Self.Guid, out var player) && player.Motion is { } motion)
        {
            stance = LookShotsPlan.StanceName(motion.Stance != 0 ? motion.Stance : motion.Style ?? 0);
        }

        return new LookFacts(step.Item.Name, step.Item.Wcid, frame, parent, drawn, light, particles, stance, LookShotsPlan.ModeName(state.Self.Qualities.CombatMode), ammo);
    }

    // What the avatar set and the scene hold about one item now. parent: the body's parent (ok) or a ParentFailed notice since its wield (failed(<location>)), else none. drawn: the layer drew it in the last three frames. light: the Setup's
    // light is in this frame's lit list (on), registered but not lit (off), or there is none registered (none). particles: the live emitters on the body. An item with no avatar (stowed items have none) is none/no/none/0.
    private (string Parent, bool? Drawn, string Light, int? Particles) AvatarFactsOf(uint guid)
    {
        _lookParentFail.TryGetValue(guid, out var failure);
        var drawn = _layer is null ? (bool?)null : _lookDrawnFrame.TryGetValue(guid, out var seen) && _lookFrameNo - seen <= 3;
        if (_avatars is null || !_avatars.TryGet(guid, out var avatar))
        {
            return (LookShotsPlan.ParentPhrase(false, failure), drawn, "none", 0);
        }

        var body = avatar.Body;
        string light;
        if (body.Setup.Lights.Count == 0 || !_avatars.DynamicLights.Contains(guid))
        {
            light = "none";
        }
        else
        {
            // off-state: the server's description lacks LightingOn (retail set_lights 0x005107C0: the state bit is what lights a Setup's lights), so no holding rule could light it; off: registered and the state allows it, but not lit now.
            light = body.LightsOn ? "off" : "off-state";
            for (var i = 0; i < _avatars.DynamicLights.Count; i++)
            {
                if (_avatars.DynamicLights[i].OwnerId == guid)
                {
                    light = "on";
                    break;
                }
            }
        }

        return (LookShotsPlan.ParentPhrase(body.Parent is not null, failure), drawn, light, body.Effects.Particles.Count);
    }

    private void FinishLookShots(int exitCode)
    {
        _lookShotsDone = true;
        _lookPhase = LookPhase.Done;
        RestoreDeveloperHud();
        if (_lookCameraFramed && _rig is { } rig && GodotObject.IsInstanceValid(rig.Chase))
        {
            rig.Chase.Model.Scale = _lookCameraScaleBefore;
            rig.Chase.Model.SetDefaultOffsets();
            _lookCameraFramed = false;
        }

        var steps = _lookPlan?.Steps.Count ?? 0;
        if (steps > 0)
        {
            Log.Info(LogCategory, LookShotsPlan.Verdict(_lookCaptured, _lookFailures, steps));
        }

        Log.FlushToGodot();
        GetTree().Quit(exitCode);
    }
}

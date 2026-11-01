using Lifestone.Client.Diagnostics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Movement;

/// <summary>The keymap's intents: retail's command lists, hold keys, autorun, control and the jump key.</summary>
public sealed partial class PlayerMovementController
{
    /// <summary>The run key's command, HoldRun 0x85000001.</summary>
    public const uint HoldRunCommand = 0x85000001;

    /// <summary>The sidestep modifier's command, HoldSidestep 0x85000002.</summary>
    public const uint HoldSidestepCommand = 0x85000002;

    /// <summary>The autorun key's command, AutoRun 0x090000C7.</summary>
    public const uint AutoRunCommand = 0x090000C7;

    /// <summary>The jump key's command, Jump 0x2500003B (the jump itself goes through <see cref="CommenceJump"/> and <see cref="DoJump"/>).</summary>
    public const uint JumpCommand = 0x2500003B;

    /// <summary>Retail's UI-command bit, which <c>HandleKeyboardCommand</c> ignores.</summary>
    private const uint UiCommandBit = 0x08000000;

    /// <summary>A substate (forward-command) bit.</summary>
    private const uint SubstateBit = 0x40000000;

    /// <summary>The bit of the substates that are locomotion (walk, run, walk backwards): they go in the substate list.</summary>
    private const uint LocomotionBit = 0x04000000;

    private readonly CommandList _substates = new();
    private readonly CommandList _turns = new();
    private readonly CommandList _sidesteps = new();
    private bool _transientState;
    private float _autoRunSpeed = 1f;
    private uint _actionStamp = 1;
    private bool _loggedDeadPlayer;

    /// <summary>
    /// Retail <c>CommandInterpreter::HandleKeyboardCommand</c> (11.4186 0x006B3690, 11.6096 0x006B45D0) for a key the
    /// keymap maps to a motion command:
    /// <list type="number">
    /// <item>nothing while the interpreter is not active; AutoRun toggles autorun (<see cref="ToggleAutoRun"/>); a UI
    /// command (bit 0x08000000) is not movement;</item>
    /// <item>a key released while the server controls the player is only forgotten; otherwise control comes back
    /// (<c>TakeControlFromServer</c>);</item>
    /// <item>HoldRun and HoldSidestep set the modifiers (<c>CommandInterpreter::SetHoldRun</c> and <c>SetHoldSidestep</c>)
    /// and send a MoveToState unless the player stands still (the keymap's run key does not come this way:
    /// <see cref="SetHoldRun"/>);</item>
    /// <item>any other command is booked in its list (<c>BookkeepCommandAndModifyIfNecessary</c>: pressed, it becomes the
    /// list's head; released, the key pressed before it takes over), the sidestep modifier turns a turn into a sidestep
    /// (<c>ApplyHoldKeysToCommand</c>), the player moves (<c>MovePlayer</c>), and a MoveToState is sent.</item>
    /// </list>
    /// </summary>
    /// <param name="command">The retail motion command (e.g. <see cref="MotionCommands.WalkForward"/>).</param>
    /// <param name="start">True on press, false on release.</param>
    /// <param name="speed">The speed the keymap attaches (1 for keys).</param>
    public void HandleCommand(uint command, bool start, float speed = 1f)
    {
        if (!IsActive)
        {
            return;
        }

        if (command == AutoRunCommand)
        {
            _autoRunSpeed = speed;
            ToggleAutoRunCore();
            SendMovementEvent();
            return;
        }

        if ((command & UiCommandBit) != 0)
        {
            return;
        }

        if (ControlledByServer && !start)
        {
            NukeCommand(ref command, ref start, ref speed);
            return;
        }

        TakeControlFromServer();
        if (command == HoldRunCommand)
        {
            SetHoldRunCore(start);
            SendUnlessStandingStill();
            return;
        }

        if (command == HoldSidestepCommand)
        {
            SetHoldSidestepCore(start);
            SendUnlessStandingStill();
            return;
        }

        if (!BookkeepCommand(ref command, ref start, ref speed))
        {
            SendMovementEvent();
            return;
        }

        ApplyHoldKeysToCommand(ref command);
        MovePlayer(command, start, speed, mouse: false, holdRun: false);
        if (command != JumpCommand)
        {
            SendMovementEvent();
        }
    }

    /// <summary>
    /// The run key, as retail's keymap delivers it: <c>ACCmdInterp::OnAction</c> (11.4186 0x0058B370, 11.6096 0x0058C1A0)
    /// handles input action 0x32 by calling <c>CommandInterpreter::SetHoldRun</c> directly while the interpreter is active.
    /// Unlike the command path (<see cref="HandleCommand"/> with <see cref="HoldRunCommand"/>), it does not take control
    /// from the server, is not dropped while the server controls the player, and sends no MoveToState: the server learns
    /// of the run key with the next movement the player sends.
    /// </summary>
    public void SetHoldRun(bool on)
    {
        if (IsActive)
        {
            SetHoldRunCore(on);
        }
    }

    /// <summary>
    /// The sidestep modifier: <see cref="HandleCommand"/> with HoldSidestep, retail's command path (no input action of
    /// <c>ACCmdInterp::OnAction</c> sends it; see <see cref="IPlayerMovementIntents.SetHoldSidestep"/>).
    /// </summary>
    public void SetHoldSidestep(bool on) => HandleCommand(HoldSidestepCommand, on);

    /// <summary>The autorun key: <see cref="HandleCommand"/> with AutoRun at speed 1.</summary>
    public void ToggleAutoRun() => HandleCommand(AutoRunCommand, start: true);

    /// <summary>
    /// The jump key went down: retail <c>ACCmdInterp::CommenceJump</c> (11.4186 0x0058B1A0, 11.6096 0x0058BFD0) and
    /// <c>ClientCombatSystem::CommenceJump</c> (0x0056AF90 / 0x0056BCD0). Unless a jump is already pending, the motion
    /// interpreter is asked to charge (<c>CMotionInterp::charge_jump</c>: a refusal is a <see cref="MovementNoticeKind.JumpRefused"/>
    /// notice); allowed, the jump bar starts filling at <paramref name="now"/> and a MoveToState goes out (it may carry the
    /// standing long jump the charge started).
    /// </summary>
    public void CommenceJump(double now)
    {
        Advance(now);
        if (!IsActive || Jump.Pending)
        {
            return;
        }

        var refusal = _motion.ChargeJump();
        if (refusal != WeenieError.None)
        {
            Raise(MovementNoticeKind.JumpRefused, ChargeRefusal(refusal));
            return;
        }

        Jump.Start(_now);
        SendMovementEvent();
    }

    /// <summary>
    /// The jump key came up: retail <c>ACCmdInterp::DoJump</c> (11.4186 0x0058B1C0, 11.6096 0x0058BFF0) and
    /// <c>ClientCombatSystem::DoJump</c> (0x0056B110 / 0x0056BE50) for an autonomous jump. With a jump pending: the extent
    /// is the bar's level at <paramref name="now"/> (at least <see cref="JumpCharge.MinJumpExtent"/>), the bar finishes
    /// (<c>FinishJump</c>, which also ends the standing long jump), the motion interpreter jumps
    /// (<c>CMotionInterp::jump</c>), and an allowed jump sends Jump with the extent, the player's local velocity
    /// (<c>CPhysicsObj::get_local_physics_velocity</c>, 11.4186 0x00512140, 11.6096 0x00512C10) and its position. A refusal
    /// for the load, the position or being in the air is a <see cref="MovementNoticeKind.JumpRefused"/> notice; retail is
    /// silent about any other.
    /// </summary>
    public void DoJump(double now)
    {
        Advance(now);
        if (!IsActive || !Jump.Pending)
        {
            return;
        }

        var extent = JumpCharge.ToExtent(Jump.GetPowerBarLevel(_now, _motion.Interpreted.CurrentStyle));
        FinishJump();
        var result = _player.Jump(extent, out _);
        var velocity = _player.GetLocalPhysicsVelocity();
        switch (result)
        {
            case WeenieError.None:
                _sink.Jump(extent, velocity, _player.Position);
                Stats.JumpSent++;
                break;
            case WeenieError.CantJumpLoadedDown:
            case WeenieError.YouCantJumpFromThisPosition:
            case WeenieError.YouCantJumpWhileInTheAir:
                Raise(MovementNoticeKind.JumpRefused, result);
                break;
        }
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::TakeControlFromServer</c> (11.4186 0x006B32D0, 11.6096 0x006B4210), after
    /// <c>ACCmdInterp::TakeControlFromServer</c> (0x0058B160 / 0x0058BF90; its automatic-attack abort is combat's): while
    /// the server controls a living player, the player's last move becomes autonomous, it stops completely and stops
    /// interpolating, the run key is applied again and the held keys move it.
    /// </summary>
    public void TakeControlFromServer()
    {
        if (!ControlledByServer || PlayerIsDead)
        {
            return;
        }

        ControlledByServer = false;
        _player.LastMoveWasAutonomous = true;
        _player.StopCompletely();
        _player.StopInterpolating();
        SetHoldRunCore(HoldRun);
        ApplyCurrentMovement();
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::LoseControlToServer</c> (11.4186 0x006B3170, 11.6096 0x006B40B0): the server moves
    /// the player now (the host calls this for a non-autonomous UpdateMotion of the player, as
    /// <c>ACSmartBox::DispatchSmartBoxEvent</c> does): autorun goes off without moving, and a pending jump is dropped
    /// (<c>ACCmdInterp::FinishJump</c>, 0x0058B1D0 / 0x0058C000).
    /// </summary>
    public void LoseControlToServer()
    {
        ControlledByServer = true;
        BeginServerMoveToWatch();
        SetAutoRun(false, applyMovement: false);
        FinishJump();
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::StopCompletely</c> (11.4186 0x006B3B50, 11.6096 0x006B4A90): every held command is
    /// dropped, autorun goes off without moving, the player stops completely, and a MoveToState is sent.
    /// </summary>
    public void StopCompletely()
    {
        ClearAllCommands();
        SetAutoRun(false, applyMovement: false);
        _player.StopCompletely();
        SendMovementEvent();
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::Disable</c> (11.4186 0x006B3250, 11.6096 0x006B4190): every held command is dropped
    /// (the decompile's first call, vtable slot 10, read as <c>ClearAllCommands</c>), the run key and the sidestep
    /// modifier are released, a player the client controls stops moving and a MoveToState says so, and the interpreter
    /// takes no more keys and sends no positions until <see cref="Enable"/>.
    /// </summary>
    public void Disable()
    {
        ClearAllCommands();
        SetHoldRunCore(false);
        HoldSidestep = false;
        if (!ControlledByServer)
        {
            ApplyCurrentMovement();
            SendMovementEvent();
        }

        Enabled = false;
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::Enable</c> (11.4186 0x006B3290, 11.6096 0x006B41D0): the interpreter takes keys again,
    /// and the run key's state is applied to the motion interpreter.
    /// </summary>
    public void Enable()
    {
        Enabled = true;
        SetHoldRunCore(HoldRun);
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::LoseKeyboardFocus</c> (11.4186 0x006B31F0, 11.6096 0x006B4130), when another window
    /// (the chat entry) takes the keyboard: the held keys are dropped, the run key and the sidestep modifier released, a
    /// pending jump finishes (<c>ACCmdInterp::FinishJump</c>), and a player the client controls stops moving and a
    /// MoveToState says so.
    /// </summary>
    public void LoseKeyboardFocus()
    {
        ClearAllCommands();
        SetHoldRunCore(false);
        HoldSidestep = false;
        FinishJump();
        if (!ControlledByServer)
        {
            ApplyCurrentMovement();
            SendMovementEvent();
        }
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::PlayerIsDead</c> (11.4186 0x006B3D70, 11.6096 0x006B4CB0): the interpreted forward
    /// command is Dead.
    /// </summary>
    private bool PlayerIsDead => _motion.Interpreted.ForwardCommand == MotionCommands.Dead;

    /// <summary>
    /// Retail <c>CommandInterpreter::MovePlayer</c> (11.4186 0x006B3F40, 11.6096 0x006B4E80) at autonomy level 2 without
    /// mouse turning: a pressed command is <c>CPhysicsObj::DoMotion</c> with the action stamp, the speed and the
    /// Autonomous flag (an action first stops the player completely, and on success advances the stamp); a released one
    /// is <c>CPhysicsObj::StopMotion</c>. Jump is neither (the jump key has its own path). A mouse command (autorun is
    /// one) carries its own run key and does not set the interpreter's.
    /// </summary>
    private void MovePlayer(uint command, bool start, float speed, bool mouse, bool holdRun)
    {
        if (PlayerIsDead)
        {
            if (!_loggedDeadPlayer)
            {
                _loggedDeadPlayer = true;
                _log.Error(
                    "movement",
                    "TODO(physics): CommandInterpreter::MovePlayer for a dead player calls a command-interpreter entry point (vtable slot 28) the decompile does not name; the keys are dropped and autorun goes off");
            }

            ClearAllCommands();
            SetAutoRun(false, applyMovement: false);
            return;
        }

        if (command == JumpCommand)
        {
            return;
        }

        var holdKey = mouse ? holdRun ? HoldKey.Run : HoldKey.None : HoldKey.Invalid;
        var parameters = new MovementParameters() with { HoldKeyToApply = holdKey };
        if (mouse)
        {
            parameters = parameters.WithFlag(MovementParameterFlags.SetHoldKey, false);
        }

        if (!start)
        {
            _player.StopMotion(command, parameters);
            return;
        }

        parameters = parameters.WithFlag(MovementParameterFlags.Autonomous, true) with
        {
            ActionStamp = unchecked((ushort)_actionStamp),
            Speed = speed,
        };
        var isAction = (command & MotionCommands.ActionMask) != 0;
        if (isAction)
        {
            StopCompletely();
        }

        var result = _player.DoMotion(command, parameters);
        switch (result)
        {
            case WeenieError.None:
                if (isAction)
                {
                    _actionStamp++;
                }

                break;
            case WeenieError.CantCrouchInCombat:
            case WeenieError.CantSitInCombat:
            case WeenieError.CantLieDownInCombat:
            case WeenieError.CantChatEmoteInCombat:
            case (WeenieError)0x3E:
            case (WeenieError)0x44:
                Raise(MovementNoticeKind.MotionRefused, result);
                break;
        }
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::ApplyCurrentMovement</c> (11.4186 0x006B3430, 11.6096 0x006B4370): moves the player by
    /// what is held. Forward: autorun (WalkForward at the autorun speed as a mouse command with the run key), else the
    /// substate list's head, else Ready unless a transient substate (an emote state) holds. Turning: the turn list's head,
    /// else sidestep and turn stopped. Sidestepping: the sidestep list's head, else sidestep stopped.
    /// </summary>
    private void ApplyCurrentMovement()
    {
        if (AutoRun)
        {
            MovePlayer(MotionCommands.WalkForward, start: true, _autoRunSpeed, mouse: true, holdRun: true);
        }
        else if (_substates.HasHead)
        {
            ApplyListHeadMovement(_substates, start: true);
        }
        else if (!_transientState)
        {
            MovePlayer(MotionCommands.Ready, start: true, 1f, mouse: false, holdRun: false);
        }

        if (_turns.HasHead)
        {
            ApplyListHeadMovement(_turns, start: true);
        }
        else
        {
            MovePlayer(MotionCommands.SideStepRight, start: false, 1f, mouse: false, holdRun: false);
            MovePlayer(MotionCommands.TurnRight, start: false, 1f, mouse: false, holdRun: false);
        }

        if (_sidesteps.HasHead)
        {
            ApplyListHeadMovement(_sidesteps, start: true);
        }
        else
        {
            MovePlayer(MotionCommands.SideStepRight, start: false, 1f, mouse: false, holdRun: false);
        }
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::ApplyListHeadMovement</c> (11.4186 0x006B3510, 11.6096 0x006B4450) and
    /// <c>StopListHeadMovement</c> (0x006B35A0 / 0x006B44E0): starts or stops a list's head, a keyboard command after the
    /// sidestep modifier.
    /// </summary>
    private void ApplyListHeadMovement(CommandList list, bool start)
    {
        if (!list.HasHead)
        {
            return;
        }

        var head = list.Head;
        var command = head.Command;
        ApplyHoldKeysToCommand(ref command);
        MovePlayer(command, start, head.Speed, mouse: false, holdRun: false);
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::ApplyHoldKeysToCommand</c> (11.4186 0x006B3340, 11.6096 0x006B4280): with the
    /// sidestep modifier held, TurnRight becomes SideStepRight and TurnLeft SideStepLeft.
    /// </summary>
    private void ApplyHoldKeysToCommand(ref uint command)
    {
        if (!HoldSidestep)
        {
            return;
        }

        if (command == MotionCommands.TurnRight)
        {
            command = MotionCommands.SideStepRight;
        }
        else if (command == MotionCommands.TurnLeft)
        {
            command = MotionCommands.SideStepLeft;
        }
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::SetHoldRun</c> (11.4186 0x006B3370, 11.6096 0x006B42B0) at autonomy level 2: the run
    /// key's state, and the motion interpreter's run key set to it XOR <see cref="MovementOptions.RunByDefault"/>
    /// (<c>CMotionInterp::set_hold_run</c>, 0x00528B70 / 0x00529780, cancelling a MoveTo).
    /// </summary>
    private void SetHoldRunCore(bool on)
    {
        HoldRun = on;
        _motion.SetHoldRun(HoldRun != Options.RunByDefault, cancelMoveTo: true);
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::SetHoldSidestep</c> (11.4186 0x006B3410, 11.6096 0x006B4350): the turn list's head
    /// stops, the modifier is set, and the held keys move the player again (a held turn becomes a sidestep or back).
    /// </summary>
    private void SetHoldSidestepCore(bool on)
    {
        ApplyListHeadMovement(_turns, start: false);
        HoldSidestep = on;
        ApplyCurrentMovement();
    }

    /// <summary>
    /// The end of <c>HandleKeyboardCommand</c>'s modifier branches: a MoveToState, unless the player stands still
    /// (<c>CMotionInterp::is_standing_still</c>, 11.4186 0x00527FA0, 11.6096 0x00528BB0).
    /// </summary>
    private void SendUnlessStandingStill()
    {
        if (!_motion.IsStandingStill())
        {
            SendMovementEvent();
        }
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::SetAutoRun</c> (11.4186 0x006B4850, 11.6096 0x006B5790): on a change, the transient
    /// substate ends; switched on, control comes back from the server; "AutoRun ON" or "AutoRun OFF" is shown; with
    /// <paramref name="applyMovement"/> the held keys move the player.
    /// </summary>
    private void SetAutoRun(bool on, bool applyMovement)
    {
        if (on == AutoRun)
        {
            return;
        }

        AutoRun = on;
        _transientState = false;
        if (on)
        {
            TakeControlFromServer();
            Raise(MovementNoticeKind.AutoRunOn, text: "AutoRun ON");
        }
        else
        {
            Raise(MovementNoticeKind.AutoRunOff, text: "AutoRun OFF");
        }

        if (applyMovement)
        {
            ApplyCurrentMovement();
        }
    }

    /// <summary>Retail <c>CommandInterpreter::ToggleAutoRun</c> (11.4186 0x006B3CC0, 11.6096 0x006B4C00).</summary>
    private void ToggleAutoRunCore() => SetAutoRun(!AutoRun, applyMovement: true);

    /// <summary>
    /// Retail <c>ACCmdInterp::HandleNewForwardMovement</c> (11.4186 0x0058B1F0, 11.6096 0x0058C020; its automatic-attack
    /// abort is combat's) and <c>CommandInterpreter::HandleNewForwardMovement</c> (0x006B3D60 / 0x006B4CA0): a new forward
    /// command ends autorun.
    /// </summary>
    private void HandleNewForwardMovement() => SetAutoRun(false, applyMovement: true);

    /// <summary>
    /// Retail <c>CommandInterpreter::BookkeepCommandAndModifyIfNecessary</c> (11.4186 0x006B3AD0, 11.6096 0x006B4A10): Jump
    /// passes; a press is added (<c>AddCommand</c>); a release is removed (<c>NukeCommand</c>), which may turn it into the
    /// press of the key held before.
    /// </summary>
    /// <returns>False when the release changes nothing (the player keeps moving as it was).</returns>
    private bool BookkeepCommand(ref uint command, ref bool start, ref float speed)
    {
        if (command == JumpCommand)
        {
            return true;
        }

        if (start)
        {
            AddCommand(command, speed);
            return true;
        }

        return NukeCommand(ref command, ref start, ref speed);
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::AddCommand</c> (11.4186 0x006B39A0, 11.6096 0x006B48E0): a command with a list
    /// (<c>WhichList</c>) becomes its head; a new walk, run or walk-backwards ends autorun and any transient substate. A
    /// substate without a list (Ready, an emote state) ends autorun too, and any but Ready becomes the transient substate.
    /// </summary>
    private void AddCommand(uint command, float speed)
    {
        var list = WhichList(command);
        if (list is not null)
        {
            list.Add(command, speed, holdRun: false);
            if ((command & SubstateBit) != 0)
            {
                if (ReferenceEquals(list, _substates))
                {
                    HandleNewForwardMovement();
                }

                _transientState = false;
            }
        }
        else if ((command & SubstateBit) != 0 && (command & LocomotionBit) == 0)
        {
            HandleNewForwardMovement();
            if (command != MotionCommands.Ready)
            {
                _transientState = true;
            }
        }
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::NukeCommand</c> (11.4186 0x006B3A20, 11.6096 0x006B4960): a released command leaves its
    /// list. Only when it was the list's head, no transient substate holds and autorun does not own the forward list does
    /// the motion change: to the new head, pressed, or to the release itself when the list is now empty.
    /// </summary>
    private bool NukeCommand(ref uint command, ref bool start, ref float speed)
    {
        var list = WhichList(command);
        if (list is null)
        {
            return false;
        }

        if (!list.Remove(command) || _transientState || (AutoRun && ReferenceEquals(list, _substates)))
        {
            return false;
        }

        if (list.HasHead)
        {
            var head = list.Head;
            command = head.Command;
            speed = head.Speed;
            start = true;
        }

        return true;
    }

    /// <summary>
    /// Retail <c>CommandInterpreter::WhichList</c> (11.4186 0x006B3950, 11.6096 0x006B4890): turns, sidesteps, or the
    /// locomotion substates (walk, run, walk backwards); null for anything else.
    /// </summary>
    private CommandList? WhichList(uint command) => command switch
    {
        MotionCommands.TurnRight or MotionCommands.TurnLeft => _turns,
        MotionCommands.SideStepRight or MotionCommands.SideStepLeft => _sidesteps,
        _ when (command & SubstateBit) != 0 && (command & LocomotionBit) != 0 => _substates,
        _ => null,
    };

    /// <inheritdoc/>
    public bool? IsHeld(uint command) => WhichList(command)?.Contains(command);

    /// <summary>
    /// Retail <c>CommandInterpreter::ClearAllCommands</c> (11.4186 0x006B31A0, 11.6096 0x006B40E0): every list emptied.
    /// </summary>
    private void ClearAllCommands()
    {
        _substates.Clear();
        _turns.Clear();
        _sidesteps.Clear();
    }

    /// <summary>
    /// The jump part of retail <c>ClientCombatSystem::FinishJump</c> (11.4186 0x0056A9B0, 11.6096 0x0056B6F0): the bar
    /// finishes and the player's standing long jump ends.
    /// </summary>
    private void FinishJump()
    {
        Jump.Finish();
        _motion.StandingLongJump = false;
    }

    /// <summary>
    /// The notice <c>ClientCombatSystem::CommenceJump</c> shows for a refused charge: 0x48 the position, 0x49 the load, and
    /// anything else "in the air".
    /// </summary>
    private static WeenieError ChargeRefusal(WeenieError refusal) =>
        refusal is WeenieError.YouCantJumpFromThisPosition or WeenieError.CantJumpLoadedDown
            ? refusal
            : WeenieError.YouCantJumpWhileInTheAir;
}

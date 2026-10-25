using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// D37, dying on purpose (M4 P5b): before the wait among the tuskers the mage, in non-combat, takes its armour off to the pack (the undress, a part of the <c>death-mode</c> stage; the bonded pieces never drop, D22) and, once its walk to the tuskers has
/// stopped, lies down (MotionCommand Sleeping 0x41000014 through <see cref="IActionApi.Move"/>, the controller's own command path, retail <c>CommandInterpreter::HandleKeyboardCommand</c>, 11.6096 0x006B45D0). ACE then
/// takes a NonCombat defence skill factor of 0.2 (<c>Player_Combat.cs</c> 261-287 @95ef426d), so undressed and lying the tuskers finish it in one death: the old unbuff suicide is gone from every path (D37, the owner's
/// decision; <see cref="DeathStages.All"/> no longer holds <see cref="DeathStages.Unbuff"/>).
/// <para><b>Why W0's lie-down never reached ACE</b> (research section 4): the server's echo of the two MoveToStates carried style 0x0049, which ACE copies from the client's own raw motion state
/// (<c>MovementData.cs</c> 94-95 @95ef426d): the client's controller was still in the Magic stance (its walker had followed a cast's TurnTo, and the scenario walkers apply only MoveTo and TurnTo, never the server's
/// peace motion), so retail <c>CMotionInterp::DoMotion</c> (11.6096 0x00529930) refused Sleeping outside NonCombat (CantLieDownInCombat) and <c>HandleKeyboardCommand</c> sent the MoveToState anyway with the raw
/// forward command unchanged (Ready). No protocol or movement-core change is needed: the lie-down goes out from a controller in NonCombat (the death loop's own walker is made fresh at the den, in NonCombat, and never
/// casts). The verdict reports it: <c>undressed=n lie_down=sent|refused:&lt;why&gt; lie_echo=yes|no</c> (report-only in their first gate); a <c>lie_echo=no</c> logs the echo's style, which names a controller that refused.</para>
/// </summary>
public sealed partial class DeathScenario
{
    /// <summary>MotionCommand Sleeping, the lie-down emote (client <c>MotionInterpreter</c>; ACE <c>MotionCommand.cs</c> 25-27 @95ef426d).</summary>
    public const uint LieDownCommand = 0x41000014;

    /// <summary>The low word of <see cref="LieDownCommand"/>, as the server's motion carries it in <c>ServerMotionState.Forward</c>.</summary>
    public const ushort LieDownWire = 0x0014;

    /// <summary>The body locations the undress empties: the head, the body's wear and the armour (<see cref="EquipMask.HeadWear"/> to <see cref="EquipMask.LowerLegArmor"/>); weapons, jewellery and the cloak stay.</summary>
    public const uint UndressMask = 0x00007FFF;

    /// <summary>The limit on one piece's move to the pack, in seconds (the move's own tracker answers sooner).</summary>
    public const double UndressPieceSeconds = 10;

    private readonly Queue<uint> _undressQueue = new();
    private bool _undressing;
    private ActionHandle? _undressPending;
    private double _undressPieceAt;
    private int? _undressed;
    private int _undressFailed;
    private ActionHandle? _liePress;
    private bool _lying;
    private bool _lieBlocked;
    private int _lieSends;
    private double _lieAt = double.NaN;
    private string _lieDown = DeathLegVerdict.NotApplicable;
    private bool _lieEchoed;
    private string? _lieEchoLast;
    private IDisposable? _motionSubscription;

    /// <summary><c>undressed=</c>: the pieces the undress moved to the pack, or null when it did not run.</summary>
    public int? Undressed => _undressed;

    /// <summary><c>lie_down=</c>: <c>sent</c> (the controller took the Sleeping command), <c>refused:&lt;why&gt;</c>, or <c>n/a</c> (the wait never got that far).</summary>
    public string LieDown => _lieDown;

    /// <summary><c>lie_echo=</c>: <c>yes</c> when the server's motion for the player showed Sleeping after a lie-down was sent, <c>no</c> when one was sent and none did, <c>n/a</c> when none was sent.</summary>
    public string LieEcho => _lieSends == 0 ? DeathLegVerdict.NotApplicable : _lieEchoed ? "yes" : "no";

    // ------------------------------------------------------------------------------------------------ the undress

    /// <summary>After the non-combat mode, inside the <c>death-mode</c> stage: every worn piece in <see cref="UndressMask"/> to the pack, one at a time; then the wait among the tuskers.</summary>
    private ScenarioStatus BeginUndress(IGameSessionView session, double clock)
    {
        _undressQueue.Clear();
        foreach (var item in session.State.Self.Inventory.Equipped)
        {
            if ((item.Location & UndressMask) != 0)
            {
                _undressQueue.Enqueue(item.Guid);
            }
        }

        _undressed = 0;
        _undressFailed = 0;
        _undressing = true; // a part of the death-mode stage (the stage list stays the leg runners' and the gate's)
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"undress (D37): {_undressQueue.Count} worn piece(s) of armour and clothing go to the pack before the wait (the bonded pieces never drop, D22)"));
        return UndressStep(session, clock);
    }

    private ScenarioStatus UndressStep(IGameSessionView session, double clock)
    {
        if (_undressPending is { } pending)
        {
            if (pending.Outcome is { } moved)
            {
                _undressPending = null;
                if (moved.Kind == ActionOutcomeKind.Confirmed)
                {
                    _undressed++;
                }
                else
                {
                    _undressFailed++;
                    _log.Warn(LogCategory, "undress: a piece did not go to the pack: " + Describe(moved) + "; the wait goes on with it worn (undressed= is report-only)");
                }
            }
            else if (clock - _undressPieceAt > UndressPieceSeconds)
            {
                _undressPending = null;
                _undressFailed++;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"undress: a piece's move to the pack was not answered within {UndressPieceSeconds:0} s; the wait goes on"));
            }
            else
            {
                return ScenarioStatus.Running;
            }
        }

        while (_undressQueue.Count > 0)
        {
            var guid = _undressQueue.Dequeue();
            if (!session.State.Self.Inventory.TryGetEquipped(guid, out _))
            {
                continue; // no longer worn
            }

            _undressPieceAt = clock;
            _undressPending = session.Actions.MoveToPack(guid, 0, 0, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        _undressing = false;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"undress: {_undressed} piece(s) in the pack{(_undressFailed == 0 ? string.Empty : $", {_undressFailed} not moved")}"));
        return BeginDeathWait(session, clock);
    }

    // ------------------------------------------------------------------------------------------------ the lie-down

    /// <summary>
    /// One frame of the lie-down inside the wait: once the walk is not walking and the player does not lie, the Sleeping command is pressed and released through <see cref="IActionApi.Move"/> (the keeper's rest does the same,
    /// <c>VitalsKeeper</c>), in non-combat only (D23, retail <c>CMotionInterp::DoMotion</c> and ACE). A walk leg (an idle "moving on") stands the player up; the next stop lies down again.
    /// </summary>
    private void LieStep(IGameSessionView session, double clock, FightWalkStatus walkStatus)
    {
        if (_liePress is { } press)
        {
            if (press.Outcome is not { } outcome)
            {
                return;
            }

            _liePress = null;
            if (outcome.Kind is ActionOutcomeKind.Local or ActionOutcomeKind.Confirmed or ActionOutcomeKind.Sent)
            {
                if (_lieDown == DeathLegVerdict.NotApplicable)
                {
                    _lieDown = "sent";
                }

                _lying = true;
                _ = session.Actions.Move.Command(LieDownCommand, false, ActionSource.Host); // the key's release: the transient substate stays (NukeCommand changes nothing for a command without a list)
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: lying down (0x{LieDownCommand:X8} taken by the controller: {outcome.Kind}); the server's echo decides lie_echo"));
                return;
            }

            _lieBlocked = true;
            _lieDown = "refused:" + outcome.Kind.ToString().ToLowerInvariant();
            _log.Warn(LogCategory, "death: the lie-down was not taken (" + Describe(outcome) + "); the mage waits standing, undressed");
            return;
        }

        if (_lying || _lieBlocked || walkStatus == FightWalkStatus.Walking)
        {
            return;
        }

        if (session.State.Self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            _lieBlocked = true;
            _lieDown = "refused:combat-mode";
            _log.Warn(LogCategory, $"death: no lie-down outside non-combat (D23; retail refuses it, CantLieDownInCombat): the mode is {session.State.Self.Qualities.CombatMode}");
            return;
        }

        if (!session.Actions.Move.IsBound)
        {
            _lieBlocked = true;
            _lieDown = "refused:no-controller";
            _log.Warn(LogCategory, "death: no movement controller is bound to IActionApi.Move, so the lie-down cannot be sent (TODO(m4-lie-down-controller): the host binds the player's controller); the mage waits standing, undressed");
            return;
        }

        _lieSends++;
        _lieAt = clock;
        _liePress = session.Actions.Move.Command(LieDownCommand, true, ActionSource.Host);
    }

    /// <summary>A walk leg begins: the player stands up with it (the controller's forward command replaces Sleeping), so the next stop lies down again.</summary>
    private void StoodUp() => _lying = false;

    private void SubscribeMotion(IGameSessionView session) =>
        _motionSubscription ??= session.Events.Subscribe<ObjectMotionChanged>(OnMotion);

    private void OnMotion(in ObjectMotionChanged e)
    {
        if (!e.IsSelf || _lieSends == 0 || _lieEchoed)
        {
            return;
        }

        var m = e.Motion;
        if (m.Forward == LieDownWire)
        {
            _lieEchoed = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: the server's motion for the player shows Sleeping {_lastClock - _lieAt:0.0} s after the lie-down (lie_echo=yes)"));
            return;
        }

        _lieEchoLast = string.Create(CultureInfo.InvariantCulture, $"fwd={(m.Forward is { } f ? "0x" + f.ToString("X4", CultureInfo.InvariantCulture) : "default")} style={(m.Style is { } s ? "0x" + s.ToString("X4", CultureInfo.InvariantCulture) : "default")}");
    }

    /// <summary>The wait's end: a lie-down sent with no Sleeping echo is named with the last echo (its style is the client's own raw style, ACE <c>MovementData.cs</c> 94-95: anything but NonCombat 0x003D names a controller that refused).</summary>
    private void ReportLieEcho()
    {
        if (_lieSends > 0 && !_lieEchoed)
        {
            _log.Warn(LogCategory, $"death: lie_echo=no: no server motion of the player showed Sleeping after {_lieSends} lie-down(s); the last echo read {_lieEchoLast ?? "nothing"} (style 0x0049 is the Magic stance: the client's controller refused, research section 4)");
        }
    }

    private string LieFields() => string.Create(CultureInfo.InvariantCulture, $"undressed={(_undressed is { } n ? n.ToString(CultureInfo.InvariantCulture) : DeathLegVerdict.NotApplicable)} lie_down={_lieDown} lie_echo={LieEcho}");
}

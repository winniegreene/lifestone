using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 (live 2026-11-12, the idle watch's lines of 06:42 to 07:53 UTC: five times she stood 8 to 16 s under a tusker's hits with full mana before her bolt went): a corpse's walk no longer holds her cast.
/// </summary>
/// <remarks>
/// <para><b>The cause.</b> A Use of a corpse beyond reach lets ACE walk her to it, and the Use is answered only when that walk ends (the corpse's contents, or a UseDone with no effect). The Action API never has a Use and a
/// cast out together, because ACE's UseDone names no request (<c>ActionApi.UseDoneAmbiguous</c>, M3 1.2 R1). So when a tusker attacked during the walk, the loot step put the corpse off and the fight asked its bolt at once,
/// but the bolt waited in the Action API until the walk ended on its own: 7 to 8 s of walking for a corpse 18 to 22 m away, or ACE's own 15 s limit when the tuskers round her stopped the walk
/// (<c>Player_Move.cs:16,119-124</c> @95ef426d).</para>
/// <para><b>The cut (check, act, check, D141).</b> When the loot step lets go of a corpse whose walk is still under way (an attacker, the step's own time limits, the fight's end), the walk is ended as a person at the keys
/// ends one, with a movement key (<see cref="MoveIntents.EndServerMove"/>). Before: the Use is unanswered. After: the Use's answer is read; the fight casts nothing until it has come, for at most
/// <see cref="LootWalkCutSeconds"/>. A key that was not taken, or an answer that does not come in that time, is named and the fight casts anyway.</para>
/// <para><b>The walk that does not start.</b> ACE's walk to a corpse is not begun while a living target stands within the loot's aggro reach of her, or, for a walk longer than that reach, while the fight has a target it
/// could cast at from where she stands (<see cref="TargetInSight"/>). The corpse is put off like any other and retried when the fight has no target.</para>
/// <para><b>The cast that waits.</b> A cast with no answer for <see cref="CastWaitSeconds"/> while an attacker is on her writes what the client believes she is doing (<see cref="CastWaitWatch"/>, report-only).</para>
/// </remarks>
public sealed partial class FightScenario
{
    /// <summary>
    /// The seconds the fight waits, after the key that ends ACE's walk to a corpse, for the Use's answer before it casts anyway. ACE's walk-to chain looks every 0.1 s (<c>Player_Move.cs:140-145</c> @95ef426d) and the
    /// answer needs one round trip more; a second is several times that. Lifestone's own.
    /// </summary>
    public const double LootWalkCutSeconds = 1;

    /// <summary>
    /// The seconds a cast may go without an answer, with an attacker on her, before the fight says what it waits behind (and again each <see cref="StillAgainSeconds"/>). The live bolts of 2026-11-12 took 2.9 s at the median and
    /// 5.3 s at the 99th percentile from the ask to the projectile. Lifestone's own.
    /// </summary>
    public const double CastWaitSeconds = 5;

    private readonly List<uint> _lootSight = [];
    private double _lootSightAt = double.NaN;
    private uint _lootSightGuid;
    private double _castWaitSaidAt;
    private long _castWaitId;

    /// <summary>The walks to a corpse the loot step ended with a key (m6-loot-walk-cut), counted when the key's two intents were asked.</summary>
    public int LootWalkCuts => _loot?.WalkCuts ?? 0;

    /// <summary>The walks to a corpse that could not be ended, or whose end was not seen within <see cref="LootWalkCutSeconds"/> (m6-loot-walk-cut-refused, m6-loot-walk-cut-late).</summary>
    public int LootWalkCutsUnseen => _loot?.WalkCutsUnseen ?? 0;

    /// <summary>The times a corpse was put off because ACE's walk to it would have begun with a hostile in reach (m6-loot-walk-waits).</summary>
    public int LootWalkWaits => _loot?.WalkWaits ?? 0;

    /// <summary>The lines the cast-wait watch wrote in this fight (m6-cast-wait).</summary>
    public int CastWaitLines { get; private set; }

    /// <summary>
    /// The pick's own test, with no side effect: the nearest living target of the ground in range of <paramref name="here"/> with a clear line (the band, the cells and the range of <c>Choose</c>'s pick), or 0. The targets the
    /// shot rule or the wall rule set aside are left out, as the pick leaves them out; the targets a bolt in flight is about to kill are counted (the pick's own line names those). Its own scratch list: the pick's is in use
    /// when the loot retry asks.
    /// </summary>
    private uint TargetInSight(IGameSessionView session, double clock, in CellFrame here)
    {
        if (clock == _lootSightAt)
        {
            return _lootSightGuid; // asked once a frame, however many put-off corpses the retry looks at
        }

        (_lootSightAt, _lootSightGuid) = (clock, SightNow(session, clock, in here));
        return _lootSightGuid;
    }

    private uint SightNow(IGameSessionView session, double clock, in CellFrame here)
    {
        _lootSight.Clear();
        _lootSight.AddRange(_killed);
        Shots.AddDroppedTo(_lootSight, clock);
        foreach (var (blocked, until) in _wallBlockedUntil)
        {
            if (clock < until)
            {
                _lootSight.Add(blocked);
            }
        }

        for (var tries = 0; tries < MaxLinePicks; tries++)
        {
            if (!_killFirstPick.Nearest(session.State.Objects, PickWcids(session, in here), in here, _range, _huntFilter, _lootSight, out var guid, session.Live))
            {
                return 0;
            }

            if (_options.IgnoreLine || LineIsClear(session, in here, guid))
            {
                return guid;
            }

            _lootSight.Add(guid);
        }

        return 0;
    }

    /// <summary>The cast the fight has asked and not had answered: the try in flight of whichever attempt is running (the bolt, the opener, the mana cast, the stamina drain, the heal), or null.</summary>
    private ActionHandle? CastUnanswered()
    {
        return Of(_cast) ?? Of(_openerCast) ?? Of(_manaCast) ?? Of(_staminaDrainCast) ?? Of(_drain.Cast);

        static ActionHandle? Of(CastAttempt? attempt) => attempt is { IsRunning: true, Handle: { Outcome: null } handle } ? handle : null;
    }

    /// <summary>
    /// M6 14 (m6-cast-wait), report-only: a cast asked <see cref="CastWaitSeconds"/> ago with no answer, while an attacker is on her, is named once (and again each <see cref="StillAgainSeconds"/>) with what the client
    /// believes: whether the cast was sent at all, every action the Action API still has out by number and kind (<see cref="IActionApi.PendingText"/>), its busy reason, whether the server is walking her, how its last walk
    /// ended and the movement stack's own line. The five stalls of 2026-11-12 would each have read "not sent ... #N OpenContainer sent, unanswered"; the next cause that is not a corpse's walk reads as itself.
    /// </summary>
    private void CastWaitWatch(IGameSessionView session, double clock)
    {
        if (CastUnanswered() is not { } cast)
        {
            return;
        }

        if (cast.Id != _castWaitId)
        {
            (_castWaitId, _castWaitSaidAt) = (cast.Id, 0);
        }

        var waited = clock - cast.QueuedAt;
        if (waited < CastWaitSeconds || (_castWaitSaidAt > 0 && clock - _castWaitSaidAt < StillAgainSeconds) || !AttackerOn(session, clock))
        {
            return;
        }

        _castWaitSaidAt = clock;
        CastWaitLines++;
        var actions = session.Actions;
        var move = actions.Move;
        var sent = cast.SentAt is { } at ? string.Create(CultureInfo.InvariantCulture, $"sent {clock - at:0.0} s ago, no answer from the server") : "not sent yet: it waits its turn in the Action API";
        var moving = move.ServerMoving switch
        {
            true => "yes",
            false => "no",
            _ => "unknown",
        };
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"cast wait: an attacker is on her and the cast #{cast.Id} ({cast.Summary}) has had no answer for {waited:0.0} s: {sent}; out: {actions.PendingText}; busy: {actions.BusyReason ?? "no"}; server walks her: {moving}; its last walk ended: {move.LastServerMoveToEnd ?? "unknown"}; motion: {move.MotionText ?? "unknown (no movement controller says)"}; phase={_phase} target=0x{_target:X8} (m6-cast-wait)"));
    }

    private sealed partial class LootStep
    {
        private ActionHandle? _cutOpen; // the Use whose walk was let go of; read until it is answered
        private ActionHandle? _cutPress;
        private ActionHandle? _cutRelease;
        private uint _cutCorpse;
        private string _cutName = string.Empty;
        private double _cutAt;
        private bool _cutWaits; // the fight casts nothing while this is set (at most LootWalkCutSeconds)

        /// <summary>See <see cref="FightScenario.LootWalkCuts"/>.</summary>
        public int WalkCuts { get; private set; }

        /// <summary>See <see cref="FightScenario.LootWalkCutsUnseen"/>.</summary>
        public int WalkCutsUnseen { get; private set; }

        /// <summary>See <see cref="FightScenario.LootWalkWaits"/>.</summary>
        public int WalkWaits { get; private set; }

        /// <summary>
        /// The step lets go of the corpse in hand (<paramref name="why"/>). When its open is ACE's walk and still unanswered, the walk is ended with a movement key (check before: the Use has no outcome; act: the key's press
        /// and release; check after: <see cref="WalkCutStep"/>). With <paramref name="wait"/> the fight holds its next cast for the Use's answer. A key that cannot be pressed (no movement controller bound, the kill switch,
        /// a dry run) is named: the Use stays out and is still read to its end.
        /// </summary>
        private void CutWalk(IGameSessionView session, double clock, string why, bool wait)
        {
            if (_stage != Stage.Opening || !_aceWalk || _pending is not { Outcome: null } open || ReferenceEquals(open, _cutOpen))
            {
                return; // no walk under way, or this Use's walk was already cut
            }

            var move = session.Actions.Move;
            var useOut = string.Create(CultureInfo.InvariantCulture, $"the Use #{open.Id} has been out {clock - _stageStart:0.0} s");
            (_cutOpen, _cutCorpse, _cutName, _cutAt, _cutPress, _cutRelease, _cutWaits) = (open, _corpse, _kill.Name, clock, null, null, false);
            if (!move.IsBound)
            {
                WalkCutsUnseen++;
                log.Warn(LogCategory, $"loot: ACE's walk to the corpse 0x{_cutCorpse:X8} of '{_cutName}' is let go of ({why}) and can not be ended: no movement controller is bound, so no key can be pressed; {useOut} and stays out until ACE answers it: a cast waits in the Action API behind it (m6-loot-walk-cut-refused)");
                return;
            }

            var before = Moving(move);
            (_cutPress, _cutRelease) = move.EndServerMove(ActionSource.Automation);
            if (Refused(_cutPress) is { } refused)
            {
                WalkCutsUnseen++;
                log.Warn(LogCategory, $"loot: ACE's walk to the corpse 0x{_cutCorpse:X8} of '{_cutName}' is let go of ({why}) and can not be ended: the key was not taken ({refused}); {useOut} and stays out until ACE answers it: a cast waits in the Action API behind it (m6-loot-walk-cut-refused)");
                return;
            }

            WalkCuts++;
            _cutWaits = wait;
            log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: ACE's walk to the corpse 0x{_cutCorpse:X8} of '{_cutName}' is ended as a key ends it ({why}): a turn key is pressed and released (actions #{_cutPress.Id} and #{_cutRelease.Id}); the press takes control back and its MoveToState ends ACE's walk-to chain (GameActionMoveToState.cs:22-26, Player_Move.cs:99-107 @95ef426d); {useOut}; server walks her: {before}; {(wait ? string.Create(CultureInfo.InvariantCulture, $"the fight casts when the Use is answered, or after {LootWalkCutSeconds:0.#} s") : "the fight is ending: it does not wait")} (m6-loot-walk-cut)"));
        }

        /// <summary>
        /// Once a frame: the after-check of <see cref="CutWalk"/>. The Use answered: one line with how long after the key, the answer, how the body's own MoveTo ended and whether the server still walks her; the fight goes on.
        /// Not answered within <see cref="LootWalkCutSeconds"/>, or the key refused meanwhile: named (m6-loot-walk-cut-late), the fight casts anyway, and the Use is still read so its end is logged when it comes.
        /// True while the fight is to cast nothing.
        /// </summary>
        public bool WalkCutStep(IGameSessionView session, double clock)
        {
            if (_cutOpen is not { } open)
            {
                return false;
            }

            var move = session.Actions.Move;
            if (open.Outcome is { } answered)
            {
                var arrived = answered.Kind == ActionOutcomeKind.Confirmed ? " (the walk had arrived: the corpse is open, its view stays for the retry)" : string.Empty;
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: ACE's walk to the corpse 0x{_cutCorpse:X8} of '{_cutName}' ended: the Use #{open.Id} was answered {clock - _cutAt:0.00} s after it was let go of: {Describe(answered)}{arrived}; the body's own MoveTo ended: {move.LastServerMoveToEnd ?? "unknown"}; server walks her: {Moving(move)}; {(_cutWaits ? "the fight casts now" : "a cast is free to go")} (m6-loot-walk-cut)"));
                (_cutOpen, _cutPress, _cutRelease, _cutWaits) = (null, null, null, false);
                return false;
            }

            if (!_cutWaits)
            {
                return false;
            }

            var refused = Refused(_cutPress) ?? Refused(_cutRelease);
            if (refused is null && clock - _cutAt < LootWalkCutSeconds)
            {
                return true;
            }

            _cutWaits = false;
            WalkCutsUnseen++;
            var what = refused is null ? string.Create(CultureInfo.InvariantCulture, $"was not seen to end within {LootWalkCutSeconds:0.#} s of the key") : $"was not ended: the key was not taken ({refused})";
            log.Warn(LogCategory, $"loot: ACE's walk to the corpse 0x{_cutCorpse:X8} of '{_cutName}' {what}: the Use #{open.Id} is still unanswered; server walks her: {Moving(move)}; motion: {move.MotionText ?? "unknown"}; the fight casts anyway, and its cast waits in the Action API behind the Use until ACE answers it (m6-loot-walk-cut-late)");
            return false;
        }

        private static string Moving(MoveIntents move) => move.ServerMoving switch
        {
            true => "yes",
            false => "no",
            _ => "unknown",
        };

        /// <summary>Why a key's intent was not taken by the controller (refused, cancelled by the kill switch, a dry run, no controller), or null while it waits its turn or was taken.</summary>
        private static string? Refused(ActionHandle? intent) => intent is { Outcome: { Kind: not ActionOutcomeKind.Local } outcome } ? Describe(outcome) : null;

        /// <summary>
        /// M6 14 (m6-loot-walk-waits): asked before a Use that would let ACE walk her <paramref name="away"/> metres to a corpse, and of each put-off corpse before it is retried. True (and why) when a living target stands
        /// within the loot's aggro reach (<c>--fight-loot-aggro-m</c>) of her, or when the walk is longer than that reach and the fight has a target it could cast at from here (<see cref="FightScenario.TargetInSight"/>):
        /// ACE's walk to a corpse that far is long (the walks of 2026-11-12: 18 to 22 m in 7 to 8 s), and every one of them was begun in the frame a tusker came into range and line. A corpse within reach of her hand
        /// is not asked (no walk). The reach at 0 turns the rule off with the rest of the aggro rule.
        /// </summary>
        private bool WalkHeld(IGameSessionView session, double clock, in CellFrame own, double away, uint except, out string why)
        {
            why = string.Empty;
            var reach = owner._options.LootAggroMetres;
            if (reach <= 0 || away <= HuntScenario.CorpseReachMetres)
            {
                return false;
            }

            var near = owner.HostileBeside(session, own.ToGlobal(), reach, except, out var at);
            if (near != 0)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"{NameOf(session, near)} 0x{near:X8} stands {at:0.#} m from the mage (aggro reach {reach:0.#} m, --fight-loot-aggro-m) and the corpse is {away:0.#} m away: ACE's walk to it does not start with a hostile in reach (m6-loot-walk-waits)");
                return true;
            }

            if (away <= reach || owner.TargetInSight(session, clock, in own) is not (> 0 and var seen))
            {
                return false;
            }

            why = string.Create(CultureInfo.InvariantCulture, $"{NameOf(session, seen)} 0x{seen:X8} is in range of the mage with a clear line and the corpse is {away:0.#} m away, a walk longer than the aggro reach ({reach:0.#} m, --fight-loot-aggro-m): the fight has a target, so ACE's walk does not start (m6-loot-walk-waits)");
            return true;
        }

        private static string NameOf(IGameSessionView session, uint guid) => session.State.Objects.TryGet(guid, out var o) ? o.Name ?? "creature" : "creature";
    }
}

using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.World;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 item 14, the waste at every start of the autopilot (the owner's order; the long round's watcher measured 71.4 s per in-session restart and a bind of 13 s to 87 s at every relaunch). Five things, each checked before and after:
/// <list type="bullet">
/// <item><b>The bind is proven once and stays proven</b> (<see cref="BindProof"/>): kept in the checkpoint by the character's guid and in the journey's facts, read at each start, checked against the ground's home lifestone before the audit
/// skips the bind, and dropped loudly when a death's respawn lands away from it.</item>
/// <item><b>The hand-over</b> (<see cref="AutopilotHandOver"/>): the run the dial starts after a rung move gets what the ended run proved seconds ago, and uses each part only while its own check says it still stands.</item>
/// <item><b>The minutes budget</b> (<see cref="MinutesBudgetRule"/>): a resumed checkpoint whose minutes are already used fails at once by name, with both numbers.</item>
/// <item><b>A pending stop starts nothing new</b>: once a stop request is seen no trip, travel leg or fight is begun again; a station at home is cut at the first frame with nothing of hers in flight; away from home what is in flight
/// ends at its own end, then the way home and the stop. A fight with attackers on her still waits for its lull (<c>StopWaitsForLull</c>).</item>
/// </list>
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The stop name of a start whose minutes budget the resumed checkpoint has already used (<see cref="MinutesBudgetRule"/>).</summary>
    public const string MinutesBudgetStop = MinutesBudgetRule.Stop;

    /// <summary>The prefix of the bind proof's own log lines (kept, carried, dropped).</summary>
    public const string BindLinePrefix = "autopilot bind: ";

    /// <summary>The prefix of the hand-over's log lines (received, each part's use).</summary>
    public const string HandOverLinePrefix = "autopilot hand-over: ";

    /// <summary>The prefix of the pending stop's log lines (a station cut, held, or not begun).</summary>
    public const string PendingStopLinePrefix = "autopilot stop: pending";

    private const string ThisRun = "this run";

    private readonly AutopilotHandOver? _handOverGiven; // what the host passed; accepted (or refused, named) at the start
    private AutopilotHandOver? _handOver;
    private bool _handOverPassAsked; // the handed-over pass is judged once, at the run's first buff arrow
    private BindProof? _bindProof;
    private string _bindProofFrom = "nothing";
    private string _bindWhyNot = "no start has read a proof yet";
    private bool _tieFromProof; // the facts' LifestoneTied was set from the carried proof, not confirmed this session
    private bool _rebindOwed; // a respawn proved the bind wrong: she binds again when next home
    private (int Teleports, uint Cell, Vector3 At) _respawnJudged;
    private double _passEndedAt = double.NaN; // the session clock when the last whole pass ended (the hand-over's)
    private int? _passBookAny; // the book that pass stood on, with or without a rung
    private string? _minutesRefusal;
    private BindProof? _checkpointBind; // the checkpoint's proof, read before the load (which may set another character's or a finished run's file aside)
    private string? _checkpointBindWhy;
    private StationSpec? _stopHeldFor; // the station whose hold under a pending stop was logged
    private bool _stopTripLogged;

    /// <summary>The bind's proof as the run holds it now, or null (the tests and the hosts read it).</summary>
    public BindProof? Bind => _bindProof;

    /// <summary>
    /// What this run hands to the run the dial starts next in the same session (the move-on's restart): the bind's proof, the last whole buff pass and the last audit's kit finding, stamped with the character's guid and the session clock.
    /// The next run checks each part against the world before it uses it.
    /// </summary>
    public AutopilotHandOver HandOver => new(_characterGuid, _ground.Key, StopName ?? "running", _clock, _bindProof, _passBookAny, _passEndedAt, _lastKit, MoveOnGateStands(), _startQuestAsked, _questFailed.Count == 0 ? null : new Dictionary<string, string>(_questFailed, StringComparer.Ordinal)); // M6 14 motes: the start quest and the quests failed this run go with it

    /// <summary>
    /// The lines of the gate of the rung this run's stop names (<c>rung-moved:&lt;rung&gt;</c>) that it read met by another's standing spell and not by the book, as <see cref="AskRung"/> read them at the stop; null for any other stop.
    /// The dial reads that gate again when it chooses the next run's ground, and it has no run to read a standing spell with: this is that read, handed over.
    /// </summary>
    private IReadOnlyList<GateStanding>? MoveOnGateStands()
    {
        var ladder = _ladder ?? Grounds.LadderLoader.WarMage;
        if (_session is not { } session || RungOwner.NamedRung(ladder, StopName) is not { } key || ladder.GateOf(key) is not { } gate || GateStandsNow() is not { } stands)
        {
            return null;
        }

        var knows = GateKnows(session);
        var list = new List<GateStanding>();
        foreach (var line in gate.Lines)
        {
            if (!knows(line.Line, line.Level) && stands(line.Line, line.Level) is { } read)
            {
                list.Add(new GateStanding(line.Line, line.Level, read));
            }
        }

        return list.Count == 0 ? null : list;
    }

    private string? _lastKit;

    private static bool IsRequest(string stop) => stop.StartsWith("request:", StringComparison.Ordinal);

    // ------------------------------------------------------------------------------------------------ the start: the hand-over, the proof, the budget

    /// <summary>The start's reads for the restart's waste, after the checkpoint's: the hand-over is accepted or refused by name, then the bind's proof is carried from it or from the checkpoint.</summary>
    private void BeginRestartReads()
    {
        ReceiveHandOver();
        RecallBindProof();
    }

    /// <summary>The hand-over the host passed is this character's and this session's, or it is refused by name; one line either way.</summary>
    private void ReceiveHandOver()
    {
        if (_handOverGiven is not { } given)
        {
            return;
        }

        if (given.CharacterGuid == 0 || given.CharacterGuid != _characterGuid)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{HandOverLinePrefix}refused: it is character 0x{given.CharacterGuid:X8}'s and this run plays 0x{_characterGuid:X8}: a cold start"));
            return;
        }

        if (!double.IsFinite(given.EndedAt) || given.EndedAt > _clock)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{HandOverLinePrefix}refused: it was taken at {given.EndedAt:0.#} on the session clock and the clock reads {_clock:0.#} now (another session's): a cold start"));
            return;
        }

        _handOver = given;
        foreach (var (key, stop) in given.QuestsFailed ?? new Dictionary<string, string>())
        {
            if (_questFailed.TryAdd(key, stop))
            {
                _log.Info(LogCategory, $"autopilot quest: key={key}: failed earlier in this process run ({stop}), handed over: not called again this run");
            }
        }

        if (given.StartQuestAsked && StartQuestKey is { } startKey)
        {
            _startQuestAsked = true; // M6 14 motes: asked once a process run; the errand's own retry rules (the ways home) still stand
            _log.Info(LogCategory, $"autopilot quest: start-quest: key={startKey}: not asked again: a run before this one in the same chain of restarts asked it (the hand-over); the ladder's own ways call its errand by their rules");
        }

        _log.Info(LogCategory, $"{HandOverLinePrefix}received {given.Line(_clock)}; each part is checked against the world before it is used (the bind against the ground's home lifestone, the buffs line by line, the stock from the pack)");
    }

    /// <summary>
    /// The bind's proof at the start: the hand-over's, else the checkpoint's by the character's guid (<see cref="AutopilotCheckpoint.ReadBindProof"/>); put in the journey's facts (<c>BoundAt</c>, so a death's respawn is judged against it, and
    /// <c>LifestoneTied</c> when the tie is in it). One line says what was carried and from where, or why nothing was.
    /// </summary>
    private void RecallBindProof()
    {
        BindProof? proof = null;
        string from;
        if (_handOver?.Bind is { } handed)
        {
            proof = handed;
            from = string.Create(CultureInfo.InvariantCulture, $"the hand-over of the run on {_handOver.Ground} that ended {Math.Max(0, _clock - _handOver.EndedAt):0.#} s ago");
        }
        else if (_checkpoints is not null)
        {
            (proof, from) = (_checkpointBind, _checkpointBindWhy ?? "the checkpoint was not read");
        }
        else
        {
            from = "no checkpoint is kept and no run handed one over";
        }

        if (proof is null)
        {
            _bindWhyNot = from;
            _log.Info(LogCategory, $"{BindLinePrefix}no proof is carried: {from}");
            return;
        }

        _bindProof = proof;
        _bindProofFrom = from;
        var facts = _context.Facts;
        facts.Bound = true;
        facts.BoundAt ??= proof.At;
        if (proof.Tied && facts.LifestoneTied is null)
        {
            facts.LifestoneTied = true;
            _tieFromProof = true;
        }

        _log.Info(LogCategory, $"{BindLinePrefix}proof carried from {from}: {proof.Text}; put in the journey's facts; the audit checks it against this ground's home lifestone before it skips the bind");
    }

    /// <summary>The checkpoint's bind proof, read before <see cref="AutopilotCheckpoint.Load"/> decides of the run (as the tie memory is): the bind is the character's, and a file the load sets aside is still the one that says why nothing is carried.</summary>
    private void ReadCheckpointBind() => _checkpointBind = AutopilotCheckpoint.ReadBindProof(_checkpoints!, _context.Character, _characterGuid, out _checkpointBindWhy);

    /// <summary>The resumed checkpoint's minutes against the option's (<see cref="MinutesBudgetRule"/>): read once, when the checkpoint is loaded; the start stops by name before anything else runs.</summary>
    private void NoteMinutesBudget(AutopilotCheckpoint checkpoint) => _minutesRefusal = MinutesBudgetRule.Refusal(_options.Minutes, checkpoint.MinutesUsed);

    // ------------------------------------------------------------------------------------------------ the bind's proof while the run goes on

    /// <summary>The audit's reading of the kept proof for this ground (<see cref="BindProofRule.Decide"/>): proven, or why not.</summary>
    private BindDecision BindProofNow()
    {
        var book = _session?.State.Self.Spellbook;
        bool? knows = book is null || !(book.IsKnown || book.Count > 0) ? null : book.Knows(Fighter.RecallHome.LifestoneRecallSpell); // as the bind station reads it: a book not read yet cannot say
        return BindProofRule.Decide(_bindProof, _ground.Home, knows, _bindWhyNot);
    }

    /// <summary>
    /// The bind station ended well: the proof is made from what the server confirmed (the facts' <c>BoundAt</c>, set at "attuned your spirit"; the tie from the station's <c>tied=yes</c> or the facts). A bind at another lifestone replaces
    /// the old proof, named. A station that says <c>bound=yes</c> and recorded no point leaves this run's own word only (<c>_bindHow</c>), named.
    /// </summary>
    private void NoteBindStation(string fields)
    {
        var bound = Token(fields, "bound") == "yes";
        var facts = _context.Facts;
        if (!bound || facts.BoundAt is not { } at)
        {
            _log.Warn(LogCategory, $"{BindLinePrefix}the bind station ended ok and no proof is kept ({(bound ? "it recorded no lifestone point" : "its fields do not say bound=yes")}): this run takes its word, the next start binds again");
            return;
        }

        var tied = Token(fields, "tied") == "yes" || (facts.LifestoneTied == true && !_tieFromProof);
        var old = _bindProof;
        _bindProof = BindProof.Of(at, tied, "the bind station (the server's \"attuned your spirit\"" + (tied ? " and \"linked with the life stone\")" : ")"));
        _bindProofFrom = ThisRun;
        _tieFromProof = false;
        _rebindOwed = false;
        var replaced = old is not null && !BindProofRule.RespawnAgrees(old, at, out _) ? string.Create(CultureInfo.InvariantCulture, $"; it replaces the proof at cell 0x{old.LifestoneCell:X8}: she bound at another lifestone") : string.Empty;
        _log.Info(LogCategory, $"{BindLinePrefix}proof kept: {_bindProof.Text}{replaced}; it is written to the checkpoint for character 0x{_characterGuid.ToString("X8", CultureInfo.InvariantCulture)}, and a later start does not walk to the lifestone again");
    }

    /// <summary>
    /// Each frame, two cheap reads keep the proof honest: a death's respawn is judged against the proven lifestone the moment it arrives (<see cref="BindRespawnWatch"/>), and the tie follows what the server said since (a Lifestone Tie or a
    /// recall that landed confirms it; a 0x049E refusal takes it back).
    /// </summary>
    private void BindWatch()
    {
        BindRespawnWatch();
        if (_bindProof is not { } proof)
        {
            return;
        }

        var tiedNow = _context.Facts.LifestoneTied;
        if (!proof.Tied && tiedNow == true && !_tieFromProof)
        {
            _bindProof = proof with { Tied = true, How = proof.How + ", then a Lifestone Tie or a Lifestone Recall the server confirmed" };
            _log.Info(LogCategory, $"{BindLinePrefix}the tie is confirmed now (a Lifestone Tie linked or a Lifestone Recall landed): {_bindProof.Text}");
        }
        else if (proof.Tied && tiedNow == false)
        {
            _bindProof = proof with { Tied = false };
            _tieFromProof = false;
            _log.Warn(LogCategory, $"{BindLinePrefix}the tie is NOT proven any more: the server refused a Lifestone Recall with {NoLifestoneRefusal} (no linked lifestone): {_bindProof.Text}; the bind at {_ground.Home.Town} ties again (F14)");
        }
    }

    /// <summary>
    /// A death's respawn against the proven lifestone: the autopilot's own death tally holds where the death's teleport arrived (ACE sends her to her Sanctuary, the lifestone she used). An arrival away from the proven lifestone is the bind
    /// proving itself wrong: the proof is dropped, loudly, the facts cleared, and she binds again when she is next home.
    /// </summary>
    private void BindRespawnWatch()
    {
        if (_deathTally is not { HasArrival: true } tally)
        {
            return;
        }

        var seen = (tally.DeathTeleports, tally.ArrivalCell, tally.ArrivalFrame.Origin);
        if (seen == _respawnJudged)
        {
            return;
        }

        _respawnJudged = seen;
        if (_bindProof is not { } proof)
        {
            return;
        }

        var arrival = new CellFrame(tally.ArrivalCell, tally.ArrivalFrame);
        if (BindProofRule.RespawnAgrees(proof, arrival, out var metres))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{BindLinePrefix}the respawn agrees with the proof: it arrived in cell 0x{tally.ArrivalCell:X8}, {metres:0.#} m from the proven lifestone in cell 0x{proof.LifestoneCell:X8}"));
            return;
        }

        _log.Error(LogCategory, string.Create(CultureInfo.InvariantCulture,
            $"{BindLinePrefix}PROVEN WRONG ({BindProofRule.RespawnElsewhere}): the death's respawn arrived in cell 0x{tally.ArrivalCell:X8}, {metres:0} m from the proven lifestone in cell 0x{proof.LifestoneCell:X8} (over {DeathLegVerdict.LifestoneRadiusMetres:0} m): she is bound somewhere else. The proof is dropped ({proof.Text}, from {_bindProofFrom}); she binds again at {_ground.Home.Town} when she is next home"));
        _bindProof = null;
        _bindHow = null;
        _bindProofFrom = "nothing";
        _bindWhyNot = string.Create(CultureInfo.InvariantCulture, $"{BindProofRule.RespawnElsewhere}: a death's respawn arrived in cell 0x{tally.ArrivalCell:X8}, away from the lifestone the proof named");
        var facts = _context.Facts;
        facts.Bound = false;
        facts.BoundAt = null;
        if (_tieFromProof)
        {
            facts.LifestoneTied = null;
            _tieFromProof = false;
        }

        _rebindOwed = true;
        Notice($"Autopilot: the last death did not send her to the {_ground.Home.Town} lifestone, so its bind is not proven any more: it binds again when it is next home.");
    }

    /// <summary>At home with a bind a respawn proved wrong: the bind station, once (its proof replaces the dropped one); null otherwise.</summary>
    private ScenarioStatus? RebindOwed()
    {
        if (!_rebindOwed)
        {
            return null;
        }

        _rebindOwed = false;
        _boundOnce = true;
        _log.Warn(LogCategory, $"{BindLinePrefix}a respawn proved the bind wrong: she is home now and binds again at the {_ground.Home.Town} lifestone before anything else");
        return BeginRun(AutopilotState.Travel, BindSpec, "the bind proved wrong at the last respawn: binding again");
    }

    // ------------------------------------------------------------------------------------------------ the hand-over's parts

    /// <summary>
    /// The handed-over buff pass at the run's first buff arrow (<see cref="HandOverPassRule"/>): the line says what was read; when the pass stands her own pass is not run again, and the book it stood on is this run's too (so the fight
    /// does not end for a pass it thinks is owed). Judged once a run: after it the run's own passes stand for themselves. Null: her own pass runs as ever.
    /// </summary>
    private string? HandOverPassStands(IGameSessionView session)
    {
        if (_handOver is not { } handOver || _handOverPassAsked)
        {
            return null;
        }

        _handOverPassAsked = true;
        _buffOptions ??= _options.Buff ?? new BuffOptions();
        BuffReads(session);
        var seedPending = _buffCycle.SeedPending;
        IReadOnlyList<string> due = [];
        _auditLines.Clear();
        if (!seedPending)
        {
            _buffCycle.ReadLines(_clock, _auditLines);
            due = _buffCycle.RebuffDue(_clock, _buffOptions.RebuffMarginSeconds);
        }

        var book = session.State.Self.Spellbook.Count;
        var (stands, why) = HandOverPassRule.Decide(handOver, _clock, book, seedPending, _auditLines, due, _buffOptions.RebuffMarginSeconds);
        if (!stands)
        {
            _log.Info(LogCategory, $"{HandOverLinePrefix}buffs: check before: {why}: her own pass runs");
            return null;
        }

        _passBook = book;
        _passBookAny = book;
        _passEndedAt = handOver.PassEndedAt;
        _log.Info(LogCategory, $"{HandOverLinePrefix}buffs: check before: {why}: her own pass is not run again (the fight keeps the lines up between kills as ever)");
        return "the handed-over pass stands";
    }

    /// <summary>The audit's kit step under a hand-over: one line says the pack was read again and what it showed (the stock is never carried by trust: the pack is the check).</summary>
    private void HandOverKitLine(string finding)
    {
        if (_handOver is { } handOver)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{HandOverLinePrefix}stock: the ended run's last audit read kit={handOver.Kit ?? "none"} {Math.Max(0, _clock - handOver.EndedAt):0.#} s ago; read again from the pack now: {finding}"));
        }
    }

    // ------------------------------------------------------------------------------------------------ a pending stop starts nothing new

    /// <summary>
    /// What the logistics trip reads at its safe point at home (the errand done, before the way back; <c>JourneyFacts.MinutesStopDue</c>): the minutes rule, as since M6 8 E, and now a pending stop request too, so a trip in flight when the
    /// stop is asked ends at home and no way back is begun. The trip's own line names the minutes rule; this one names the request.
    /// </summary>
    private bool TripSafePointStop()
    {
        if (MinutesDue())
        {
            return true;
        }

        if (DenRunOrdered)
        {
            if (!_stopTripLogged)
            {
                _stopTripLogged = true;
                _log.Info(LogCategory, "den run: the trip is at its safe point at home with the errand done: no way back is begun (the run starts from home)");
            }

            return true; // M6 item 14: the den run's trip ends at home
        }

        if (_stopAsked is not { } asked || !IsRequest(asked))
        {
            return false;
        }

        if (!_stopTripLogged)
        {
            _stopTripLogged = true;
            _log.Info(LogCategory, $"{PendingStopLinePrefix} ({asked}): the trip is at its safe point at home with the errand done: no way back is begun (the trip's own line names the minutes rule; it is this request)");
        }

        return true;
    }

    /// <summary>True when a resumed station is not begun again under <paramref name="stop"/>: a stop request is pending, the station is not the death's (a corpse owed is recovered first) and not the way home before the stop itself.</summary>
    private bool PendingStopHoldsResume(string stop, AutopilotState state) => IsRequest(stop) && state != AutopilotState.Death && _finalStop is null;

    /// <summary>
    /// A stop request is pending and a station that is neither the fight (it has its own stop, which waits for the lull) nor the death (a corpse is owed) is running or about to: at home, with her present and nothing of hers sent and
    /// unanswered, it is cut here: nothing more is bought, cast or walked, and the run stops where it stands, home. Away from home it is not cut: what is in flight (a walk, a portal hop, a pass) ends at its own end, then the stop takes
    /// the way home. Checked each frame; each hold is logged once a station.
    /// </summary>
    private ScenarioStatus? PendingStopCut(IGameSessionView session)
    {
        if (_stopAsked is not { } asked || !IsRequest(asked) || _finalStop is not null || _deathSeen || _deathPending || _healOnly || _spec is not { } spec
            || _state is not (AutopilotState.Buff or AutopilotState.Travel or AutopilotState.Trip or AutopilotState.Upkeep))
        {
            return null;
        }

        var self = session.State.Self;
        if (ReferenceEquals(spec, DenRunSpec) && self.Travel == SelfTravel.Present)
        {
            // M6 item 14 (live 2026-11-13: a stop asked 106 s before was not acted on while the den run's station ran away from home; a /stop typed in the game ended it): a stop request calls the den run off at once, where she
            // stands, whatever is on her or in flight; the stop's own rule then recalls her home. In portal space nothing can be sent: the cut waits for the arrival (the next frame she is present).
            _log.Warn(LogCategory, $"{PendingStopLinePrefix} ({asked}): the den run is called off: the {spec.Name} station is cut where she stands ({Cell(session)}) after {_clock - _phaseStart:0.0} s, whatever is on her; the stop's rule recalls her home");
            _context.Facts.RunOnly = null;
            _denRunEnd = DenRunPrefix + "called-off";
            var cut = spec.Name;
            var running = _run is not null;
            FoldInFlight(clearFinal: false);
            _phase = RunPhase.Done;
            if (running)
            {
                _context.Observer.StationEnded(cut, false, _clock);
            }

            return Stop(asked, ok: true, "the stop request called the den run off");
        }

        string? holds = null;
        if (ReferenceEquals(spec, BindSpec))
        {
            holds = "the bind at the lifestone is under way: it ends by itself at home within the minute, and its proof saves the next start's walk";
        }
        else if (ReferenceEquals(spec, RecallHomeSpec) || ReferenceEquals(spec, RungHomeSpec))
        {
            holds = "the way home is under way: it ends at home by itself";
        }
        else if (self.Travel != SelfTravel.Present)
        {
            holds = "a portal hop is under way";
        }
        else if (Classify(session.State, _ground) != AutopilotPlace.Home)
        {
            holds = "she is away from home: what is in flight ends at its own end, then the way home and the stop";
        }
        else if (session.Actions is Lifestone.Client.Session.Actions.ActionApi api && api.PendingOf(ActionSource.Automation).InFlight is > 0 and var inFlight)
        {
            holds = string.Create(CultureInfo.InvariantCulture, $"{inFlight} action(s) of hers are sent and not answered yet");
        }
        else if (_hostiles is { } watch && watch.InReach(_clock, out var hostile))
        {
            holds = $"a hostile is on her or in reach ({hostile}): the fight-back goes first";
        }

        if (holds is not null)
        {
            if (!ReferenceEquals(_stopHeldFor, spec))
            {
                _stopHeldFor = spec;
                _log.Info(LogCategory, $"{PendingStopLinePrefix} ({asked}): the {spec.Name} station of the {StateName(_state)} state is not cut: {holds}");
            }

            return null;
        }

        var began = _run is not null;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture,
            $"{PendingStopLinePrefix} ({asked}): check before: the {spec.Name} station of the {StateName(_state)} state {(began ? "is running" : "has not begun (its vitals pass or peace)")}, she is home ({Cell(session)}), present, nothing of hers is in flight and no hostile is in reach: it is cut here after {_clock - _phaseStart:0.#} s: nothing new is bought, cast or walked"));
        var name = spec.Name;
        FoldInFlight(clearFinal: false); // a trip's sales and purchases so far count; the run is disposed
        _phase = RunPhase.Done;
        if (began)
        {
            _context.Observer.StationEnded(name, false, _clock);
        }

        return Stop(asked, ok: true, $"the stop request cut the {name} station at home, at its safe point");
    }
}

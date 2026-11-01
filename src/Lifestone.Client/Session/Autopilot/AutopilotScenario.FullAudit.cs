using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M5 7 P7n (D145 point 4, the owner: /auto makes the bot evaluate <i>every</i> state necessary for combat, then resume as best it can; D141: check before, act, check after): the start-up audit widened into a full state
/// audit. Every start (a start, a reconnect, a resume by <c>/auto</c>) reads, from the live state and never from what the autopilot last did: the actions the stopped run left, alive or dead, portal space, the place (and how far from
/// the den's arrival), the hostiles and an attacker, an open vendor, corpse or trade, the kit and the trip due, the packs, the worn set, the caster held and whether it is the gear plan's pick, the stance, each buff line and its time left
/// with the Spirit Drinker aura, the vitals, the load and the loot queue. Each gap gets its named fix, checked after; one line per check (<c>autopilot audit &lt;check&gt;: &lt;finding&gt;; fix=&lt;fix&gt;</c>), the summary line, and one plain line
/// for the chat window (<see cref="ChatNotice"/>). With a hostile on the mage the slow fixes wait and the fight fights back first (D88).
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The prefix of the per-hit line (M5 7 P7n, D145 point 6): <c>autopilot hit: spell='&lt;spell&gt;' damage=&lt;n&gt; ... caster='&lt;name&gt;' wcid=&lt;wcid&gt; ... spirit_drinker=&lt;up|down|unlearned&gt; ...</c>.</summary>
    public const string HitLinePrefix = "autopilot hit: ";

    /// <summary>The prefix of a chat notice's log line (every notice is logged as well as sent).</summary>
    public const string ChatLinePrefix = "autopilot chat: ";

    private readonly List<BuffCycle.LineState> _auditLines = [];
    private RecoveryFixRun? _auditFix; // the caster's wield of the audit in progress (the recovery table's fix)
    private ActionHandle? _auditAction; // the close of a corpse or the magic mode the audit asked, in flight
    private uint _auditCloseGuid;
    private int _auditModeBefore;
    private StartupAudit? _finished; // the audit that finished this frame, for the known state's arrow and the chat summary

    /// <summary>
    /// D145: the line for the chat window, in plain words (P7m wires it to the chat window in the Godot host): the audit's summary at every start (<see cref="StartupAudit.ChatSummary"/>), a notice when the audit sends a station first (a
    /// bind, a trip, the death path, a recall from a place with no route), and the stop with its reason when the autopilot stops (<see cref="Stopped"/>). Each line is logged too (<see cref="ChatLinePrefix"/>).
    /// </summary>
    public event Action<string>? ChatNotice;

    /// <summary>
    /// D145: raised once when the run stops (its <see cref="StopName"/>: a stop rule, a request, a failure, a give-up), after the done line; the host unlocks the movement keys on it (a stopped bot never locks the person out). An
    /// abandon (<see cref="Abandon"/>: the person or the kill switch took the run) does not raise it: the host that abandoned it knows.
    /// </summary>
    public event Action<string>? Stopped;

    /// <summary>
    /// D145: what the start is, for the audit's <see cref="StartupAudit.Why"/> and the chat summary: <c>start</c> (the default), or <see cref="StartupAudit.ResumeWhy"/> for a start by <c>/auto</c> or a hand-back. Set by the host before the
    /// first step; a reconnect's audit is always <c>reconnect</c>.
    /// </summary>
    public string StartReason { get; set; } = "start";

    /// <summary>Sends <paramref name="line"/> to the chat window (<see cref="ChatNotice"/>) and logs it.</summary>
    private void Notice(string line)
    {
        _log.Info(LogCategory, ChatLinePrefix + line);
        ChatNotice?.Invoke(line);
    }

    /// <summary>An audit's notice while it runs (a station it sends first): <c>Autopilot: &lt;what&gt;.</c></summary>
    private void Notice(StartupAudit audit, string what)
    {
        _ = audit;
        Notice("Autopilot: " + what + ".");
    }

    /// <summary>The audit that finished this frame gets its chat summary with what comes <paramref name="next"/>; nothing when none did (a known state after a station).</summary>
    private void Announce(StartupAudit? finished, string next)
    {
        if (finished is not null)
        {
            Notice(finished.ChatSummary(next));
        }
    }

    /// <summary>The stop's chat line (D145: a stop the bot makes on its own says why; the keys unlock on <see cref="Stopped"/>).</summary>
    private void NoticeStop(string stop, string why)
    {
        var plain = why.Length > 240 ? why[..240] + "..." : why;
        // Logged only: the host's unlock line is the one stop line in the chat window (P7m, D145), so the person never reads two.
        _log.Info(LogCategory, ChatLinePrefix + "(log only) " + (stop.StartsWith("request:", StringComparison.Ordinal)
            ? $"Autopilot stopped ({stop["request:".Length..]}): {plain}. The controls are yours."
            : $"Autopilot stopped by itself ({stop}): {plain}. The controls are yours."));
    }

    // ------------------------------------------------------------------------------------------------ the checks

    /// <summary>
    /// 0 (D145, without stale state): the actions the session still has queued or in flight (the stopped run's, or the person's) end before the audit reads anything, at most <see cref="StartupAudit.ActionsSettleSeconds"/>; a movement
    /// key an automation still holds is released. One line.
    /// </summary>
    private ScenarioStatus? SettleActions(IGameSessionView session, StartupAudit audit)
    {
        var (queued, inFlight) = session.Actions is Lifestone.Client.Session.Actions.ActionApi api ? api.PendingOf(ActionSource.Automation) : (0, 0); // the automation's own: a person's actions are theirs
        if (queued + inFlight > 0 && _clock - audit.StartedAt < StartupAudit.ActionsSettleSeconds)
        {
            if (audit.BeginWait(StartupAudit.WaitActions, _clock))
            {
                _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitActions, string.Create(CultureInfo.InvariantCulture, $"{queued} automation action(s) queued and {inFlight} in flight (a stopped run's): the audit reads the state once they end, at most {StartupAudit.ActionsSettleSeconds:0} s")));
            }

            return ScenarioStatus.Running;
        }

        var waited = audit.WaitingOn == StartupAudit.WaitActions ? _clock - audit.WaitStartedAt : 0;
        audit.EndWait();
        audit.ActionsSettled = true;
        var released = session.Actions.Move.ReleaseAutomation();
        var finding = queued + inFlight == 0
            ? string.Create(CultureInfo.InvariantCulture, $"no automation action queued or in flight{(waited > 0 ? $" (waited {waited:0.#} s)" : string.Empty)}")
            : string.Create(CultureInfo.InvariantCulture, $"{queued} queued and {inFlight} in flight after {StartupAudit.ActionsSettleSeconds:0} s: TODO(m6-chat-controls-robust): they end by their own timeouts");
        var fix = released > 0 ? string.Create(CultureInfo.InvariantCulture, $"released {released} movement key(s) an automation still held") : "none: no movement key held by an automation";
        if (queued + inFlight > 0)
        {
            _log.Warn(LogCategory, StartupAudit.Line("actions", finding, fix));
        }
        else
        {
            _log.Info(LogCategory, StartupAudit.Line("actions", finding, fix));
        }

        if (released > 0)
        {
            audit.Note("held movement released");
        }

        return null;
    }

    /// <summary>The metres from the mage to the ground's arrival, for the place line (<c>, 34.2 m from the den's arrival</c>), or empty when either is unknown.</summary>
    private string ArrivalDistance(GameState state)
    {
        if (state.Self.Position is not { } at)
        {
            return string.Empty;
        }

        var arrival = _ground.Arrival;
        var metres = AcCoordinates.ToGlobal(new CellPosition(arrival.Cell, arrival.Origin)).DistanceTo(at.ToGlobal());
        return string.Create(CultureInfo.InvariantCulture, $", {metres:0.#} m from {_ground.Name}'s arrival");
    }

    /// <summary>
    /// 4a (D145): an open vendor is closed (the client's own close: ACE has no close message), an open corpse or chest is closed (GameAction 0x0195), each checked closed after (D141); trade is named. Lines <c>vendor</c>,
    /// <c>container</c>, <c>trade</c>.
    /// </summary>
    private ScenarioStatus? WindowsStep(IGameSessionView session, StartupAudit audit)
    {
        var state = session.State;
        if (_auditAction is null)
        {
            audit.Windows = "none";
            if (state.Vendor is { } vendor)
            {
                var who = state.Objects.TryGet(vendor.Vendor, out var v) && v.Name is { Length: > 0 } name ? $"'{name}' 0x{vendor.Vendor:X8}" : $"0x{vendor.Vendor:X8}";
                session.CloseVendorWindow();
                if (state.Vendor is null)
                {
                    audit.Windows = "vendor-closed";
                    audit.Note("vendor window closed");
                    _log.Info(LogCategory, StartupAudit.Line("vendor", $"{who} open", "closed (the client's own close: ACE has no close message, gmVendorUI::RecvNotice_CloseVendor 0x004C0F40); after: no vendor open"));
                }
                else
                {
                    audit.Windows = "gap";
                    _log.Warn(LogCategory, StartupAudit.Line("vendor", $"{who} open", "close asked; after: still open: gap: the next vendor's stock replaces it (another vendor's 0062 closes it) and walking away closes it"));
                }
            }
            else
            {
                _log.Info(LogCategory, StartupAudit.Line("vendor", "none open", "none"));
            }

            _log.Info(LogCategory, StartupAudit.Line("trade", "the client keeps no trade window (single-player: no other player to trade with)", "none: TODO(m6-trade-window)"));
            if (state.Containers.Viewed is { IsGround: true } open)
            {
                _auditCloseGuid = open.Guid;
                _auditAction = session.Actions.CloseContainer(open.Guid, ActionSource.Automation);
                if (audit.BeginWait(StartupAudit.WaitWindows, _clock))
                {
                    _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitWindows, string.Create(CultureInfo.InvariantCulture, $"the close of the open container 0x{open.Guid:X8} is in flight")));
                }

                return ScenarioStatus.Running;
            }

            _log.Info(LogCategory, StartupAudit.Line("container", "no corpse or chest open", "none"));
            audit.WindowsDone = true;
            return null;
        }

        if (_auditAction.Outcome is not { } outcome && _clock - audit.WaitStartedAt <= StartupAudit.FixActionSeconds)
        {
            return ScenarioStatus.Running;
        }

        var answer = _auditAction.Outcome is { } o ? o.Kind + (o.Detail is null ? string.Empty : " (" + o.Detail + ")") : string.Create(CultureInfo.InvariantCulture, $"no answer within {StartupAudit.FixActionSeconds:0} s");
        var closed = state.Containers.ViewedGuid != _auditCloseGuid;
        var what = string.Create(CultureInfo.InvariantCulture, $"corpse or chest 0x{_auditCloseGuid:X8} open (the stopped run's loot)");
        if (closed)
        {
            audit.Windows = audit.Windows == "none" ? "corpse-closed" : audit.Windows + "+corpse-closed";
            audit.Note("open corpse closed");
            _log.Info(LogCategory, StartupAudit.Line("container", what, $"closed (NoLongerViewingContents): {answer}; after: no container open"));
        }
        else
        {
            audit.Windows = "gap";
            _log.Warn(LogCategory, StartupAudit.Line("container", what, $"close asked: {answer}; after: still open: gap: the fight's next corpse open replaces the view"));
        }

        _auditAction = null;
        audit.EndWait();
        audit.WindowsDone = true;
        return null;
    }

    /// <summary>
    /// 5d (D145): the caster held (its name and weenie) and whether it is the gear plan's pick (<see cref="SelfEquipJudge.BestCaster"/>, the worn-set restore's judge); none held with casters in the packs: the recovery table's
    /// <see cref="RecoveryFix.WieldCaster"/> (<see cref="HeldCaster.Rearm"/>), checked after (a caster in the hands); a fix that cannot be made stops the run safely by name. No caster anywhere is a named gap: the first magic action's
    /// refusal (NoCasterHeld) then goes to the recovery table, which stops the run safely.
    /// </summary>
    private ScenarioStatus? CasterStep(IGameSessionView session, StartupAudit audit)
    {
        var state = session.State;
        if (_auditFix is null)
        {
            var casters = PackCasters(state);
            if (CombatModeGuard.Read(state).Caster is { } held)
            {
                var other = CasterPick(state, casters, out var pickWhy);
                audit.Caster = other is null ? "pick" : "held";
                _log.Info(LogCategory, StartupAudit.Line("caster", string.Create(CultureInfo.InvariantCulture, $"held '{held.Name}' 0x{held.Guid:X8} wcid {held.Weenie.WeenieClassId}; {casters.Count} caster(s) in the packs; {pickWhy}"), other is null
                    ? "none: it is the gear plan's pick"
                    : string.Create(CultureInfo.InvariantCulture, $"none: '{other.Name}' 0x{other.Guid:X8} beats it and the worn-set restore did not swap it (no pack with room, a refusal, or deferred with a hostile on him); the next trip's gear check swaps it")));
                audit.CasterDone = true;
                return null;
            }

            if (casters.Count == 0)
            {
                audit.Caster = "none";
                audit.Note("no caster in his hands or packs");
                _log.Error(LogCategory, StartupAudit.Line("caster", "the hands and the packs hold no caster", "none: gap: the first magic action is refused (NoCasterHeld) and the recovery table's wield-caster fix stops the run safely with that reason"));
                audit.CasterDone = true;
                return null;
            }

            _auditFix = new RecoveryFixRun(RecoveryFix.WieldCaster, _context.Keeper);
            if (audit.BeginWait(StartupAudit.WaitCaster, _clock))
            {
                _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitCaster, string.Create(CultureInfo.InvariantCulture, $"the hands hold no caster and the packs hold {casters.Count}: the recovery table's wield-caster fix runs (checked after: a caster in the hands)")));
            }
        }

        var status = _auditFix.Step(session, _clock);
        if (status == RecoveryFixStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        var why = _auditFix.Why;
        _auditFix = null;
        audit.EndWait();
        audit.CasterDone = true;
        if (status == RecoveryFixStatus.Failed)
        {
            audit.Caster = "gap";
            _log.Error(LogCategory, StartupAudit.Line("caster", "the hands hold no caster", "wield-caster failed: " + why + ": the run stops safely"));
            return AuditStop("failed:audit:caster", "the hands hold no caster and the wield-caster fix could not be made: " + why);
        }

        audit.Caster = "rewielded";
        var now = CombatModeGuard.Read(state).Caster;
        audit.Note(now is null ? "caster re-wielded" : $"caster re-wielded ('{now.Name}')");
        _log.Info(LogCategory, StartupAudit.Line("caster", "the hands held no caster", string.Create(CultureInfo.InvariantCulture, $"wield-caster: {why}{(now is null ? string.Empty : $" ('{now.Name}' 0x{now.Guid:X8} wcid {now.Weenie.WeenieClassId})")}")));
        return null;
    }

    /// <summary>The casters held in the packs (the wielded one is not in them).</summary>
    private static List<GearCandidate> PackCasters(GameState state)
    {
        var casters = new List<GearCandidate>();
        foreach (var item in PackLayout.Items(state))
        {
            if ((item.ItemType & SelfEquipJudge.CasterType) != 0 && state.Objects.TryGet(item.Guid, out var o))
            {
                casters.Add(SelfEquipJudge.CandidateOf(state, o));
            }
        }

        return casters;
    }

    /// <summary>The caster in the packs that beats the held one by the gear plan (the worn-set restore's judge: the ground's element at the character's level), or null when the held one is the pick; <paramref name="why"/> says so.</summary>
    private GearCandidate? CasterPick(GameState state, List<GearCandidate> casters, out string why)
    {
        var judge = new SelfEquipJudge(_ground.Loot?.KeepAside?.SpellNames() ?? [], SelfEquipJudge.ElementOf(_ground.PlanFor(state.Self.Qualities.Level)?.Family), id => _context.Spells.TryGetValue(id, out var s) ? s.Name : null);
        var choice = judge.BestCaster(casters, SelfEquipJudge.Worn(state), new QualitiesSkills(state.Self.Qualities), out var pickWhy);
        why = pickWhy.Replace("gear: ", string.Empty, StringComparison.Ordinal);
        return choice is null ? null : casters.FirstOrDefault(c => c.Guid == choice.Guid);
    }

    /// <summary>
    /// 5e (D145): the combat stance. On the ground with a caster held the fight's magic mode is asked and checked after (the mode reads magic, D141); a contrary answer is a named gap (the fight's own mode step asks again and its typed
    /// failure goes to the recovery table). Elsewhere the next station sets its own mode.
    /// </summary>
    private ScenarioStatus? StanceStep(IGameSessionView session, StartupAudit audit)
    {
        var state = session.State;
        if (_auditAction is null)
        {
            var mode = state.Self.Qualities.CombatMode;
            if (audit.Place != "ground" || !HeldCaster.Held(state) || mode == CombatModes.Magic)
            {
                audit.Stance = mode == CombatModes.Magic ? "magic" : "left";
                var fix = mode == CombatModes.Magic ? "none"
                    : audit.Place != "ground" ? "none: not on the ground: the next station sets its own mode (the buff and the travels enter magic, a vendor peace)"
                    : "none: no caster is held, and the magic mode needs one (the caster check names it)";
                _log.Info(LogCategory, StartupAudit.Line("stance", ModeName(mode), fix));
                audit.StanceDone = true;
                return null;
            }

            _auditModeBefore = mode;
            _auditAction = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            if (audit.BeginWait(StartupAudit.WaitStance, _clock))
            {
                _log.Info(LogCategory, StartupAudit.WaitLine(StartupAudit.WaitStance, $"the magic mode is asked (the stance was {ModeName(mode)})"));
            }

            return ScenarioStatus.Running;
        }

        if (_auditAction.Outcome is null && _clock - audit.WaitStartedAt <= StartupAudit.FixActionSeconds)
        {
            return ScenarioStatus.Running; // the act's own end first (its tracker fails fast on a contrary answer, D141), then the check after
        }

        var after = state.Self.Qualities.CombatMode;

        var answer = _auditAction.Outcome is { } o ? o.Kind + (o.Detail is null ? string.Empty : " (" + o.Detail + ")") : string.Create(CultureInfo.InvariantCulture, $"no answer within {StartupAudit.FixActionSeconds:0} s");
        if (after == CombatModes.Magic)
        {
            audit.Stance = "magic-set";
            audit.Note("magic stance set");
            _log.Info(LogCategory, StartupAudit.Line("stance", ModeName(_auditModeBefore), $"the magic mode asked: {answer}; after: magic"));
        }
        else
        {
            audit.Stance = "gap";
            _log.Warn(LogCategory, StartupAudit.Line("stance", ModeName(_auditModeBefore), $"the magic mode asked: {answer}; after: {ModeName(after)}: gap: the fight's own mode step asks again, and its typed failure goes to the recovery table"));
        }

        _auditAction = null;
        audit.EndWait();
        audit.StanceDone = true;
        return null;
    }

    private static string ModeName(int mode) => mode switch
    {
        CombatModes.NonCombat => "peace",
        CombatModes.Melee => "melee",
        CombatModes.Missile => "missile",
        CombatModes.Magic => "magic",
        0 => "unknown (the server has not said)",
        _ => mode.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// 6 (D145): the vitae, every wanted buff line and its time left (<see cref="BuffCycle.ReadLines"/>), the lines under the re-buff margin (<see cref="BuffCycle.RebuffDue"/>), and the Spirit Drinker aura. Lines due (or none up): the
    /// buff pass before the fight (<c>buffs=buff</c>); with a hostile on him the fight fights back first and refreshes them between kills (<c>buffs=fight-first</c>).
    /// </summary>
    private void BuffsCheck(IGameSessionView session, StartupAudit audit)
    {
        var state = session.State;
        var enchantments = state.Self.Qualities.Enchantments;
        var vitae = _deathTally?.VitaePercent ?? 0;
        var up = enchantments.IsDefaultOrEmpty ? "no enchantment is up" : string.Create(CultureInfo.InvariantCulture, $"{enchantments.Length} enchantment(s) up");
        _buffOptions ??= _options.Buff ?? new BuffOptions();
        _buffCycle ??= new BuffCycle(session, _context.Spells, _buffOptions, _log, ArmourBuffLedger.For(state));
        string lines;
        IReadOnlyList<string> due = [];
        if (_buffCycle.SeedPending)
        {
            lines = "the lines are not judged one by one: the armour memory is not seeded yet (the buff pass seeds it)";
        }
        else
        {
            _buffCycle.ReadLines(_clock, _auditLines);
            lines = _auditLines.Count == 0 ? "no wanted line" : string.Join(", ", _auditLines.Select(l => l.Active ? string.Create(CultureInfo.InvariantCulture, $"{l.Line.Name} {l.Remaining:0} s") : l.Line.Name + " down"));
            due = _buffCycle.RebuffDue(_clock, _buffOptions.RebuffMarginSeconds);
        }

        audit.BuffsDue = due.Count;
        var wanted = enchantments.IsDefaultOrEmpty || due.Count > 0;
        audit.Buffs = !wanted ? "pass" : audit.Attacker is not null ? "fight-first" : "buff";
        var dueText = due.Count == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"; due under the {_buffOptions.RebuffMarginSeconds:0} s margin: {string.Join(", ", due)}");
        var fix = audit.Buffs switch
        {
            "buff" => "the buff pass before the first fight" + (due.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $" (renews {due.Count} line(s))") : string.Empty),
            "fight-first" => "deferred: a hostile is on him: the fight fights back first (D88) and refreshes the due lines between kills (F23)",
            _ => "none: the fight refreshes the due lines between kills (F23)",
        };
        _log.Info(LogCategory, StartupAudit.Line("buffs", string.Create(CultureInfo.InvariantCulture, $"{up}; vitae {vitae}% (logged only: it clears by earned experience); lines: {lines}{dueText}"), fix));
        if (due.Count > 0)
        {
            audit.Note(audit.Buffs == "buff" ? string.Create(CultureInfo.InvariantCulture, $"{due.Count} buff line(s) due: renewed first") : string.Create(CultureInfo.InvariantCulture, $"{due.Count} buff line(s) due: renewed between kills"));
        }

        // the Spirit Drinker aura (D143: learned by play, a line of the buff profile once learned; its 30-minute recast)
        var learned = SpiritDrinker.LearnedLevel(state);
        if (learned == 0)
        {
            audit.Spirit = "unlearned";
            _log.Info(LogCategory, StartupAudit.Line("spirit_drinker", "not learned (no Aura of Spirit Drinker Self I-VI in the spellbook)", "none: learned by play (the Scrivener of Item Magic's scroll on a trip, D143)"));
        }
        else if (SpiritDrinker.TryAura(state, _clock, out var level, out var left))
        {
            audit.Spirit = "up:" + SpiritDrinker.Roman(level);
            _log.Info(LogCategory, StartupAudit.Line("spirit_drinker", string.Create(CultureInfo.InvariantCulture, $"learned to {SpiritDrinker.Roman(learned)}; up: level {SpiritDrinker.Roman(level)}, {left:0} s left"), left < _buffOptions.RebuffMarginSeconds ? "the buff line's recast (it is under the margin)" : "none"));
        }
        else
        {
            audit.Spirit = "down";
            _log.Info(LogCategory, StartupAudit.Line("spirit_drinker", $"learned to {SpiritDrinker.Roman(learned)}; down", audit.Attacker is null ? "the buff pass casts it (a line of the buff profile)" : "the fight's refresh casts it between kills (F23)"));
        }
    }

    /// <summary>7 (D145): health, stamina and mana. On the ground with no hostile and the health under the ground's floor (<see cref="HealthFloorFor"/>): the heal in place first; else the next station's vitals pass.</summary>
    private void VitalsCheck(IGameSessionView session, StartupAudit audit)
    {
        _ = session;
        if (!TryVitalShares(out var health, out var stamina, out var mana))
        {
            audit.Vitals = "unread";
            _log.Info(LogCategory, StartupAudit.Line("vitals", "not read yet (no vital levels)", "none: the next station's vitals pass reads them"));
            return;
        }

        var floor = HealthFloorFor(_ground, _options);
        audit.HealFirst = audit.Place == "ground" && audit.Attacker is null && floor > 0 && health < floor;
        audit.Vitals = audit.HealFirst ? "heal" : "ok";
        var fix = audit.HealFirst ? string.Create(CultureInfo.InvariantCulture, $"the heal in place first (the keeper's pass to {JourneyPlans.FightHealthPercent}% health; the floor is {floor:0}%)")
            : audit.Attacker is not null ? "none: the fight's guard and drains keep them while it fights back (D88)"
            : "none: the next station's vitals pass brings them to its needs (the keeper: the stamina-to-mana dance fills the mana, D88)";
        _log.Info(LogCategory, StartupAudit.Line("vitals", string.Create(CultureInfo.InvariantCulture, $"health {health:0}%, stamina {stamina:0}%, mana {mana:0}%"), fix));
        if (audit.HealFirst)
        {
            audit.Note(string.Create(CultureInfo.InvariantCulture, $"health {health:0}%: healing first"));
        }
    }

    /// <summary>7 (D145): the main pack's free slots and the burden. The counters' trip (the kit check above) covers both.</summary>
    private void LoadCheck(StartupAudit audit)
    {
        if (!TryLoadShares(out var free, out var burden))
        {
            _log.Info(LogCategory, StartupAudit.Line("load", "not read yet (the pack's list or the burden capacity)", "none: the counters judge it once it reads"));
            return;
        }

        _log.Info(LogCategory, StartupAudit.Line("load", string.Create(CultureInfo.InvariantCulture, $"{free} free slot(s) in the main pack; burden {burden:0}% of the capacity"), audit.Kit is "trip" or "trip-deferred" ? "the trip the kit check named (the counters' trip covers the slots and the burden)" : "none: the counters call a trip at their slot and burden lines"));
    }

    /// <summary>
    /// 7 (D145): the loot queue. The stopped fight's queue (its kill notices paired with their corpses) is not carried into the resumed fight; the creature corpses in view are read and named. TODO(m6-resume-loot-queue).
    /// </summary>
    private void LootQueueCheck(GameState state, StartupAudit audit)
    {
        var mine = "Corpse of " + state.Self.Name;
        var count = 0;
        var nearest = double.MaxValue;
        var nearestName = string.Empty;
        var self = state.Self.Position is { } p ? p.ToGlobal() : (GlobalPosition?)null;
        foreach (var o in state.Objects.All)
        {
            if ((o.DescriptionFlags & WorldObjectTable.CorpseFlag) == 0 || o.Name is not { } name || !name.StartsWith("Corpse of ", StringComparison.Ordinal) || string.Equals(name, mine, StringComparison.Ordinal))
            {
                continue;
            }

            count++;
            if (self is { } s && o.Position is { } at && at.ToGlobal().DistanceTo(s) is var d && d < nearest)
            {
                (nearest, nearestName) = (d, string.Create(CultureInfo.InvariantCulture, $"'{name}' 0x{o.Guid:X8} at {d:0.#} m"));
            }
        }

        audit.CorpsesInView = count;
        _log.Info(LogCategory, count == 0
            ? StartupAudit.Line("loot_queue", "no creature corpse in view; the resumed fight starts with an empty queue", "none")
            : StartupAudit.Line("loot_queue", string.Create(CultureInfo.InvariantCulture, $"{count} creature corpse(s) in view{(nearestName.Length > 0 ? ", the nearest " + nearestName : string.Empty)}; the stopped fight's queue is not carried over"), "none: the resumed fight queues the kills it makes from now (its kill notices); these are left to rot: TODO(m6-resume-loot-queue)"));
    }

    // ------------------------------------------------------------------------------------------------ the Flame Bolt split (D145 point 6)

    /// <summary>
    /// D145 point 6: one line per landed damage spell of the mage's (the server's "You &lt;verb&gt; &lt;target&gt; for &lt;n&gt; points with &lt;spell&gt;."), with the caster held then (name, weenie, guid) and the Spirit Drinker aura read from the
    /// enchantments (up with its level and time left, down, or not learned), so a Flame Bolt mean splits by caster and by aura.
    /// </summary>
    private void NoteHit(in ChatReceived e)
    {
        if (e.Line.ChatType != FightTally.MagicChatType || e.Line.Kind != ChatKind.System || _session is not { } session)
        {
            return;
        }

        var text = e.Line.Text;
        if (FightTally.Classify(text, out var amount, out var critical) != MagicLineKind.DamageDealt)
        {
            return;
        }

        _log.Info(LogCategory, HitLine(session.State, text, amount, critical, _clock));
    }

    /// <summary>The per-hit line (see <see cref="NoteHit"/>): <c>autopilot hit: spell='Flame Bolt VI' damage=123 critical=no target='Tusker Guard' caster='Fire Sceptre' wcid=1234 guid=0x80001234 spirit_drinker=up level=4 left_s=1620</c>.</summary>
    /// <param name="state">The state (the caster held, the enchantments).</param>
    /// <param name="text">The server's damage line.</param>
    /// <param name="amount">Its damage.</param>
    /// <param name="critical">True for a critical hit.</param>
    /// <param name="now">The session clock.</param>
    public static string HitLine(GameState state, string text, long amount, bool critical, double now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(text);
        var withAt = text.LastIndexOf(" with ", StringComparison.Ordinal);
        var spell = withAt < 0 ? "?" : text[(withAt + " with ".Length)..].TrimEnd('.', '!', ' ');
        var you = text.IndexOf("You ", StringComparison.Ordinal); // past a "Critical hit! " or another prefix
        var target = FightScenario.DealtTarget(you > 0 ? text[you..] : text);
        var caster = CombatModeGuard.Read(state).Caster;
        var held = caster is null ? "caster=none wcid=0 guid=0x00000000" : string.Create(CultureInfo.InvariantCulture, $"caster='{caster.Name}' wcid={caster.Weenie.WeenieClassId} guid=0x{caster.Guid:X8}");
        var aura = SpiritDrinker.TryAura(state, now, out var level, out var left)
            ? string.Create(CultureInfo.InvariantCulture, $"spirit_drinker=up level={level} left_s={left:0}")
            : SpiritDrinker.LearnedLevel(state) > 0 ? "spirit_drinker=down" : "spirit_drinker=unlearned";
        return string.Create(CultureInfo.InvariantCulture, $"{HitLinePrefix}spell='{spell}' damage={amount} critical={(critical ? "yes" : "no")} target='{target}' {held} {aura}");
    }
}

/// <summary>
/// M5 7 P7n (D145 point 6, D143): the Aura of Spirit Drinker Self I-VI (spells 3253-3258, Item Enchantment auras on the player; stat key 170 WeaponAuraElemental, +0.01 a level, <see cref="BuffStatMods"/>): learned from the spellbook and
/// up from the player's enchantments, never from what the autopilot last cast.
/// </summary>
public static class SpiritDrinker
{
    /// <summary>The spell id of level I (Aura of Spirit Drinker Self I).</summary>
    public const uint FirstSpellId = 3253;

    /// <summary>The levels (I to VI; no VII).</summary>
    public const int Levels = 6;

    /// <summary>The highest level the spellbook holds, or 0.</summary>
    /// <param name="state">The state.</param>
    public static int LearnedLevel(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (var level = Levels; level >= 1; level--)
        {
            if (state.Self.Spellbook.Knows(FirstSpellId + (uint)level - 1))
            {
                return level;
            }
        }

        return 0;
    }

    /// <summary>True when an aura of Spirit Drinker is up on the player at <paramref name="now"/>: its <paramref name="level"/> (the highest up) and the seconds left on it.</summary>
    /// <param name="state">The state.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="level">The level (1-6), or 0.</param>
    /// <param name="left">The seconds left, or 0.</param>
    public static bool TryAura(GameState state, double now, out int level, out double left)
    {
        ArgumentNullException.ThrowIfNull(state);
        (level, left) = (0, 0);
        var active = state.Self.Qualities.ActiveEnchantments;
        if (active.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var e in active)
        {
            var at = (int)e.SpellId - (int)FirstSpellId + 1;
            if (at is < 1 or > Levels || at < level || e.RemainingSeconds(now) <= 0)
            {
                continue;
            }

            (level, left) = (at, e.RemainingSeconds(now));
        }

        return level > 0;
    }

    /// <summary>The level as the spell names write it (I to VI).</summary>
    /// <param name="level">The level.</param>
    public static string Roman(int level) => level switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        _ => level.ToString(CultureInfo.InvariantCulture),
    };
}

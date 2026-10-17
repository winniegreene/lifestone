using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M3 2.1's buff scenario (<c>--scenario=buff</c>, plan section 4.5 s1; BSD Mage and nobody else): enter the mage and have the gear on (<see cref="GearScenario"/> with <c>endSession: false, cast: false</c>), enter the magic
/// combat mode, run the buff cycle (<see cref="BuffCycle.Start"/>: the ordered pass and its upgrade pass), run the forced re-buff pass (decision D25 as narrowed: the one line closest to expiry is recast while still
/// active), return to non-combat, log off and quit. With <c>endSession: false</c> (the tests, and the game's buff leg of s3, which owns the session) it ends at <c>done</c> in the world.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>): <c>start</c>, <c>gear</c> (login, entering and the gear are the inner scenario's), <c>mode-magic</c>, <c>cycle</c>, <c>rebuff</c>, <c>mode-peace</c>, <c>logging-off</c>,
/// <c>quitting</c>, <c>done</c>.</para>
/// <para><b>Success</b> means the gear is on, the cycle and the forced pass ended without a failure, every wanted line is active, no line was missed and the forced pass recast a line while it was still active. Anything
/// else fails the run: a failure at any stage, in the world, logs off before it is reported, as the gear scenario does (M3 1.4 fix A2; the headless host's <c>headless-failed-logoff</c>), so ACE does not keep the character in the
/// world for the next run.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, logged as <c>[buff] verdict ...</c>, printed by the headless host as <c>buff ...</c>): <c>gear=wielded|failed worn=&lt;n&gt; buffs=&lt;active&gt;/&lt;wanted&gt;
/// levels=&lt;line:level,..&gt; rebuffed=yes|no forced=yes|no min_remaining_s=&lt;s&gt; transfers=&lt;fired&gt;/&lt;tried&gt; revitalizes=&lt;n&gt; fizzles=&lt;n&gt; mana_min=&lt;n&gt; cycle_s=&lt;s&gt; misses=&lt;names|none&gt;</c>.
/// <c>buffs</c>, <c>levels</c> and <c>min_remaining_s</c> are what the read model holds when the last pass ended; the counters are the cycle's, cumulative; <c>forced=yes</c> means the forced pass landed a recast;
/// <c>cycle_s</c> is the ordered pass's session seconds; <c>misses</c> are the lines either pass missed. No account or character name is in it.</para>
/// <para><b>A second run.</b> <see cref="CycleCast"/> is what the ordered pass cast (the forced pass always casts one line, so it is counted apart). A cycle object that already ran (passed in as <c>cycle</c>) casts nothing
/// the second time; a new object does not know an earlier object's armour casts, so it recasts the two armour lines (<see cref="CycleArmourCast"/>; <c>TODO(m3-buff-armour-memory)</c>) and nothing else.</para>
/// </remarks>
public sealed class BuffScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=buff</c>).</summary>
    public const string ScenarioName = "buff";

    /// <summary>The log category of the scenario's lines (<c>[buff] ...</c>, the cycle's category too).</summary>
    public const string LogCategory = BuffCycle.LogCategory;

    private readonly GearScenario _gear;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly BuffOptions _options;
    private readonly bool _endSession;
    private readonly ILogSink _log;
    private readonly int _wanted;
    private BuffCycle? _cycle;
    private ActionHandle? _pending;
    private BuffResult? _first;
    private BuffResult? _final;
    private bool _forcedLanded;
    private bool _failLogOff;
    private string _gearFields = "gear=failed worn=0";

    /// <summary>The buff scenario.</summary>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): the kit's components, the casts' costs, forms and durations.</param>
    /// <param name="account">The account name the kit is built for (hashed by the kit, never stored, logged or printed).</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the scenario and the cycle log.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True (the headless host's): log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="cycle">A cycle object that already ran on this session (a second run casts nothing), or null for a new one.</param>
    public BuffScenario(IReadOnlyDictionary<uint, SpellInfo> spells, string account, BuffOptions options, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, BuffCycle? cycle = null)
        : this(GearKit.Build(spells ?? throw new ArgumentNullException(nameof(spells)), account), spells, options, log, arrivalSeconds, endSession, cycle)
    {
    }

    /// <summary>The buff scenario over a kit already built (the tests').</summary>
    /// <param name="kit">The kit.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the scenario and the cycle log.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="cycle">A cycle object that already ran on this session, or null for a new one.</param>
    public BuffScenario(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions options, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, BuffCycle? cycle = null)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _spells = spells;
        _options = options;
        _log = log;
        _endSession = endSession;
        _cycle = cycle;
        _gear = new GearScenario(kit, log, arrivalSeconds, endSession: false, cast: false, spells);
        foreach (var line in ArenaProfile.Lines)
        {
            if (line.IsBuff)
            {
                _wanted++;
            }
        }
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario sends no movement.</remarks>
    public long Unmatched => 0;

    /// <summary>The inner gear scenario (the tests read its steps).</summary>
    public GearScenario Gear => _gear;

    /// <summary>The cycle object (null before the gear is done).</summary>
    public BuffCycle? Cycle => _cycle;

    /// <summary>The buff lines the ordered pass cast and landed (its upgrade casts included), or -1 before it ended; the forced pass is not counted.</summary>
    public int CycleCast => _first?.Cast ?? -1;

    /// <summary>Of <see cref="CycleCast"/>, the armour lines.</summary>
    public int CycleArmourCast => _first?.ArmourCast ?? 0;

    /// <summary>What the ordered pass left (null before it ended).</summary>
    public BuffResult? CycleResult => _first;

    /// <summary>What the forced pass left (null before it ended).</summary>
    public BuffResult? ForcedResult => _final;

    /// <summary>The verdict fields (see the class remarks); a run that never reached the cycle reports zeros and <c>n/a</c>.</summary>
    public string VerdictFields()
    {
        var best = _final ?? _first ?? _cycle?.Result;
        var misses = new List<string>();
        if (_first is not null)
        {
            misses.AddRange(_first.Misses);
        }

        if (best is not null && !ReferenceEquals(best, _first))
        {
            foreach (var miss in best.Misses)
            {
                if (!misses.Contains(miss))
                {
                    misses.Add(miss);
                }
            }
        }

        var gear = _gear.VerdictFields();
        var min = best is null || double.IsNaN(best.MinRemainingSeconds) ? "n/a" : Math.Floor(best.MinRemainingSeconds).ToString("0", CultureInfo.InvariantCulture);
        var mana = best?.ManaMin is { } m ? m.ToString(CultureInfo.InvariantCulture) : "n/a";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Token(gear, "gear")} {Token(gear, "worn")} buffs={best?.Active ?? 0}/{best?.Wanted ?? _wanted} levels={(best is null ? "n/a" : best.Levels)} rebuffed={(best?.Rebuffed == true ? "yes" : "no")} forced={(_forcedLanded ? "yes" : "no")} min_remaining_s={min} transfers={best?.TransfersFired ?? 0}/{best?.TransfersTried ?? 0} revitalizes={best?.Revitalizes ?? 0} fizzles={best?.Fizzles ?? 0} mana_min={mana} cycle_s={Math.Floor(_first?.CycleSeconds ?? 0).ToString("0", CultureInfo.InvariantCulture)} misses={(misses.Count == 0 ? "none" : string.Join(';', misses))} surpassed={_first?.Surpassed ?? 0}");
    }

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Failure is not null)
        {
            // A failed run in the world logs off first (see Fail), so ACE does not keep the character in the world for the next run.
            if (_failLogOff && _pending is { Outcome: { } left })
            {
                _pending = null;
                _failLogOff = false;
                _log.Info(LogCategory, "the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            }

            return _failLogOff ? ScenarioStatus.Running : ScenarioStatus.Failed;
        }

        if (Stage == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        var actions = session.Actions;
        var clock = session.Clock.Now;
        switch (Stage)
        {
            case "start":
                Stage = "gear";
                break;

            case "gear":
                switch (_gear.Step(session, now))
                {
                    case ScenarioStatus.Failed:
                        return Fail(session, "gear: " + _gear.Failure);
                    case ScenarioStatus.Succeeded:
                        _gearFields = _gear.VerdictFields();
                        _log.Info(LogCategory, "gear done (" + _gearFields + "): magic combat mode next");
                        _pending = actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
                        Stage = "mode-magic";
                        break;
                }

                break;

            case "mode-magic":
                if (_pending is { Outcome: { } magic })
                {
                    _pending = null;
                    if (!magic.Succeeded)
                    {
                        return Fail(session, "mode-magic: the magic combat mode was not entered: " + Describe(magic));
                    }

                    _cycle ??= new BuffCycle(session, _spells, _options, _log);
                    if (!_cycle.Start(clock))
                    {
                        return Fail(session, "cycle: it did not start: " + (_cycle.Result?.Failure ?? "a pass is already running"));
                    }

                    Stage = "cycle";
                }

                break;

            case "cycle":
                switch (_cycle!.Step(clock))
                {
                    case BuffStatus.Failed:
                        _first = _cycle.Result;
                        return Fail(session, "cycle: " + _cycle.Result?.Failure);
                    case BuffStatus.Done:
                        _first = _cycle.Result;
                        _log.Info(LogCategory, "the ordered cycle is done (" + CycleCast + " casts); the forced re-buff pass next (D25)");
                        if (!_cycle.StartRebuff(clock, forced: true))
                        {
                            return Fail(session, "rebuff: it did not start");
                        }

                        Stage = "rebuff";
                        break;
                }

                break;

            case "rebuff":
                switch (_cycle!.Step(clock))
                {
                    case BuffStatus.Failed:
                        _final = _cycle.Result;
                        return Fail(session, "rebuff: " + _cycle.Result?.Failure);
                    case BuffStatus.Done:
                        _final = _cycle.Result;
                        _forcedLanded = _final is { Forced: true, Cast: >= 1, RecastWhileActive: >= 1 };
                        var judged = Judge();
                        if (judged is not null)
                        {
                            return Fail(session, judged);
                        }

                        _pending = actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
                        Stage = "mode-peace";
                        break;
                }

                break;

            case "mode-peace":
                if (_pending is { Outcome: { } peace })
                {
                    _pending = null;
                    if (!peace.Succeeded)
                    {
                        _log.Warn(LogCategory, "the player was left in the magic combat mode: " + Describe(peace)); // report-only, as the gear scenario's
                    }

                    return Conclude(session);
                }

                break;

            case "logging-off":
                if (_pending is { Outcome: { } loggedOff })
                {
                    _pending = null;
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "logging off: " + Describe(loggedOff));
                    }

                    _pending = actions.Quit(ActionSource.Host);
                    Stage = "quitting";
                }

                break;

            case "quitting":
                if (_pending is { Outcome: { } quit })
                {
                    _pending = null;
                    if (quit.Kind != ActionOutcomeKind.Sent)
                    {
                        return Fail(session, "quitting: " + Describe(quit));
                    }

                    _log.Info(LogCategory, "verdict " + VerdictFields());
                    Stage = "done";
                    return ScenarioStatus.Succeeded;
                }

                break;

            default:
                return Fail(session, $"TODO(state): unknown buff stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    /// <summary>The run's judgement after the forced pass: null when it holds, else the first thing that does not.</summary>
    private string? Judge()
    {
        var best = _final!;
        var missed = new List<string>();
        if (_first is not null)
        {
            missed.AddRange(_first.Misses);
        }

        missed.AddRange(best.Misses);
        if (missed.Count > 0)
        {
            return "buffs: lines were missed (" + string.Join(';', missed) + ")";
        }

        if (best.Active < best.Wanted)
        {
            return $"buffs: {best.Active} of {best.Wanted} lines are active ({best.Levels})";
        }

        return _forcedLanded ? null : "rebuff: the forced pass recast no line while it was still active";
    }

    private ScenarioStatus Conclude(IGameSessionView session)
    {
        if (!_endSession)
        {
            _log.Info(LogCategory, "verdict " + VerdictFields());
            Stage = "done";
            return ScenarioStatus.Succeeded;
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        Stage = "logging-off";
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Failure = why;
        _log.Error(LogCategory, why);
        _log.Info(LogCategory, "verdict " + VerdictFields());
        if (_endSession && session.State.Phase == SessionPhase.InWorld && Stage is not ("logging-off" or "quitting"))
        {
            // As the gear scenario's failure (M3 1.4 fix A2): leave the world before the run ends as failed, so the next login is not refused as CharacterInWorld; the verdict above stands whatever the log off does.
            _log.Info(LogCategory, "logging off before the run ends as failed");
            _pending = session.Actions.LogOff(ActionSource.Host);
            _failLogOff = true;
            return ScenarioStatus.Running;
        }

        return ScenarioStatus.Failed;
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);

    /// <summary>The <c>name=value</c> token of a verdict line (the gear scenario's), or <c>name=?</c>.</summary>
    private static string Token(string fields, string name)
    {
        var key = name + "=";
        var at = fields.StartsWith(key, StringComparison.Ordinal) ? 0 : fields.IndexOf(" " + key, StringComparison.Ordinal);
        if (at < 0)
        {
            return key + "?";
        }

        if (at > 0)
        {
            at++;
        }

        var end = fields.IndexOf(' ', at);
        return end < 0 ? fields[at..] : fields[at..end];
    }
}

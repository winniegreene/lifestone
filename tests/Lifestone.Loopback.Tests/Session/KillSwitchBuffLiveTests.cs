using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M5 item 1 lane K3, the kill switch over a live buff pass (D13). WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD ONLY, with the backend up and the Lifestone server mod ready. On <c>BSD Mage</c> only (the test account; it must
/// exist), over the shared session, the leg has the gear on, enters the magic combat mode, and starts a buff pass that is forced to cast every line (a re-buff margin so large that every line is due), then arms the Action API's kill
/// switch the moment the second cast of the pass is confirmed by ACE, before the pass can send the third.
/// </summary>
/// <remarks>
/// <para><b>What it proves.</b> (a) The pass casts as <see cref="ActionSource.Automation"/>: the first two casts are Automation entries in the action log and both were Confirmed by ACE (GameEvent 0x02C2). (b) With the kill
/// switch on, the pass stops: the cycle ends <see cref="BuffStatus.Failed"/> naming the kill switch ("the cast was not carried out"), and nothing was queued after the switch: the action log holds no Automation cast queued at or after the moment the
/// switch was armed (the pass reads the switch before every send, so the cast it tried next is never queued). The number of casts that went out is reported (<c>casts_sent=</c>), not judged: a fizzle retry, a refused Mana Conversion try or a mana-loop
/// press before the second confirmed cast all count there (review 1-R2). (c) The kill switch is released afterwards (the Action API accepts Automation again: the leg's combat mode back to peace is an
/// Automation action and is Confirmed). (d) The character is left in the world in non-combat, and logs off only when the session is not the shared one.</para>
/// <para>The pass spends mana and a component or two for its two casts; the leg waits at most 180 s of session time for the mana of each cast it needs (a named precondition wait, not a retry). Skipped, with the probe's reason, unless the
/// backend runs with the Lifestone server mod ready; on another character it fails at once and sends nothing.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[Trait("Lane", "solo")]
[LiveSharedSession]
public sealed class KillSwitchBuffLiveTests(ITestOutputHelper output)
{
    /// <summary>The headless limit of the run, in seconds: the gear, two casts with their mana, the stop and the log off.</summary>
    private const double QuitAfterSeconds = 600;

    [LoopbackFact]
    public void The_kill_switch_armed_after_the_second_confirmed_cast_stops_the_buff_pass_and_nothing_is_queued_after_it()
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var log = new RecordingLogSink();
        var leg = new KillSwitchLeg(GearKit.Build(spells, LiveMage.KitAccount()), spells, log, output.WriteLine);
        var result = LiveMage.Run(output, leg, "killswitch-buff", QuitAfterSeconds).Result;

        foreach (var line in log.Lines.Where(l => l.Contains("[gear]", StringComparison.Ordinal) || l.Contains("[buff]", StringComparison.Ordinal) || l.Contains("[cast]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        if (leg.Summary is { } summary)
        {
            output.WriteLine(summary);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Null(leg.Failure);
        Assert.Equal("done", leg.Stage);
        Assert.NotNull(leg.Summary);
    }

    /// <summary>The leg: gear, magic mode, the forced pass, the kill switch after the second confirmed cast, the assertions, the switch released, non-combat.</summary>
    internal sealed class KillSwitchLeg : IScenario
    {
        private const double ManaWaitSeconds = 180;
        private const double BailSeconds = 30;

        private readonly GearScenario _gear;
        private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
        private readonly ILogSink _log;
        private readonly Action<string> _write;
        private BuffCycle? _cycle;
        private ActionHandle? _pending;
        private double _stageStart;
        private int _confirmed;
        private bool _armed;
        private double _armedAt;

        public KillSwitchLeg(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, Action<string> write)
        {
            _gear = new GearScenario(kit, log, endSession: false, cast: false, spells: spells) { RequireCharacter = true };
            _spells = spells;
            _log = log;
            _write = write;
        }

        public string Name => "killswitch-buff-leg";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        /// <summary>The <c>killswitch_live ...</c> line, once every assertion of the leg held.</summary>
        public string? Summary { get; private set; }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            var clock = session.Clock.Now;
            if (Stage == "start" && GearScenario.WrongCharacter(session) is { } wrong)
            {
                Failure = wrong; // nothing was sent
                _write("FAILED: " + wrong);
                Stage = "failed";
                return ScenarioStatus.Failed;
            }

            switch (Stage)
            {
                case "done":
                    return ScenarioStatus.Succeeded;
                case "failed":
                    return ScenarioStatus.Failed;
                case "bailing":
                    if (_pending is { IsCompleted: true } || session.State.Phase != SessionPhase.InWorld || clock - _stageStart > BailSeconds)
                    {
                        Stage = "failed";
                        return ScenarioStatus.Failed;
                    }

                    return ScenarioStatus.Running;
            }

            try
            {
                return Advance(session, clock, now);
            }
            catch (InvalidOperationException e)
            {
                Failure ??= Stage + ": " + e.Message;
                _write("FAILED: " + Failure);
                session.Actions.KillSwitch = false; // never leave the switch on for the next test
                if (session.State.Phase == SessionPhase.InWorld)
                {
                    _stageStart = clock;
                    _pending = session.Actions.LogOff(ActionSource.Host);
                    Stage = "bailing";
                    return ScenarioStatus.Running;
                }

                Stage = "failed";
                return ScenarioStatus.Failed;
            }
        }

        private static void Require(bool ok, string what)
        {
            if (!ok)
            {
                throw new InvalidOperationException(what);
            }
        }

        private ScenarioStatus Advance(IGameSessionView session, double clock, double now)
        {
            switch (Stage)
            {
                case "start":
                    Stage = "gear";
                    break;

                case "gear":
                    var gear = _gear.Step(session, now);
                    Require(gear != ScenarioStatus.Failed, "the gear scenario failed: " + _gear.Failure);
                    if (gear == ScenarioStatus.Succeeded)
                    {
                        _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
                        Stage = "mode";
                    }

                    break;

                case "mode":
                    if (_pending is { Outcome: { } mode })
                    {
                        Require(mode.Succeeded, "the magic combat mode was not entered: " + mode.Kind + (mode.Detail is null ? string.Empty : ": " + mode.Detail));
                        _pending = null;
                        // A margin no buff can reach: every line is due, so the pass casts them in the owner's order (the mage is usually buffed already, and a plain pass would cast nothing).
                        _cycle = new BuffCycle(session, _spells, new BuffOptions { RebuffMarginSeconds = 1_000_000 }, _log, ArmourBuffLedger.For(session.State));
                        Require(_cycle.StartRebuff(clock, forced: false), "the buff pass did not start");
                        _stageStart = clock;
                        Stage = "pass";
                    }

                    break;

                case "pass":
                    CountConfirmedCasts(session);
                    if (!_armed && _confirmed >= 2)
                    {
                        // Before the pass is stepped again: it has not sent the third cast yet, so the third never leaves the client.
                        session.Actions.KillSwitch = true;
                        _armed = true;
                        _armedAt = clock; // the line the log is judged against: no Automation cast may be queued at or after it
                        _write($"the kill switch is on after {_confirmed} confirmed casts");
                    }

                    var status = _cycle!.Step(clock);
                    if (status == BuffStatus.Running)
                    {
                        Require(_armed || clock - _stageStart <= ManaWaitSeconds * 3, "the pass did not confirm two casts in time (mana waits are bounded by the pass itself)");
                        break;
                    }

                    CountConfirmedCasts(session);
                    Require(_armed, "the pass ended before its second cast was confirmed: " + _cycle.Result?.Failure);
                    Require(status == BuffStatus.Failed, "the pass was not stopped by the kill switch: it ended " + status);
                    var failure = _cycle.Result?.Failure ?? string.Empty;
                    Require(failure.Contains("kill switch", StringComparison.OrdinalIgnoreCase), "the pass failed but not naming the kill switch: " + failure);
                    var sent = 0;
                    var refused = 0;
                    var queuedAfter = 0;
                    for (var i = 0; i < session.ActionLog.Count; i++)
                    {
                        var entry = session.ActionLog[i];
                        if (entry.Kind != ActionKind.CastSpell || entry.Source != ActionSource.Automation || entry.QueuedAt < _stageStart)
                        {
                            continue;
                        }

                        if (entry.QueuedAt >= _armedAt)
                        {
                            queuedAfter++;
                        }

                        if (entry.Outcome.Kind is ActionOutcomeKind.Refused or ActionOutcomeKind.Cancelled)
                        {
                            refused++;
                        }
                        else
                        {
                            sent++;
                        }
                    }

                    // What the switch must prevent is a send after it, not a count of the sends before it (a fizzle retry, a Mana Conversion try or a mana-loop press can come first): sent= is reported, never judged.
                    Require(queuedAfter == 0, $"{queuedAfter} Automation cast(s) were queued after the kill switch was armed (it was armed at {_armedAt.ToString("F3", CultureInfo.InvariantCulture)} s)");
                    Summary = string.Create(CultureInfo.InvariantCulture, $"killswitch_live casts_sent={sent} casts_refused={refused} queued_after_switch={queuedAfter} pass={_cycle.Result!.Pass} failure=\"{failure}\"");
                    _write(Summary);
                    session.Actions.KillSwitch = false;
                    _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation); // the released switch: an Automation action is accepted again
                    Stage = "peace";
                    break;

                case "peace":
                    if (_pending is { Outcome: { } peace })
                    {
                        Require(peace.Succeeded, "the kill switch was released but the combat mode change (Automation) was not carried out: " + peace.Kind + (peace.Detail is null ? string.Empty : ": " + peace.Detail));
                        if (LiveRun.KeepSession)
                        {
                            _write("the shared session stays logged in (no log off at the end of the leg)");
                            Stage = "done";
                            return ScenarioStatus.Succeeded;
                        }

                        _pending = session.Actions.LogOff(ActionSource.Host);
                        Stage = "logging-off";
                    }

                    break;

                case "logging-off":
                    if (_pending is { Outcome: { } off })
                    {
                        Require(off.Kind == ActionOutcomeKind.Confirmed, "logging off: " + off.Kind);
                        _pending = session.Actions.Quit(ActionSource.Host);
                        Stage = "quitting";
                    }

                    break;

                case "quitting":
                    if (_pending is { Outcome: { } quit })
                    {
                        Require(quit.Kind == ActionOutcomeKind.Sent, "quitting: " + quit.Kind);
                        Stage = "done";
                        return ScenarioStatus.Succeeded;
                    }

                    break;

                default:
                    throw new InvalidOperationException("unknown stage " + Stage);
            }

            return ScenarioStatus.Running;
        }

        /// <summary>Counts the pass's Automation casts that ACE confirmed, from the action log (entries since the pass began).</summary>
        private void CountConfirmedCasts(IGameSessionView session)
        {
            var count = 0;
            for (var i = 0; i < session.ActionLog.Count; i++)
            {
                var entry = session.ActionLog[i];
                if (entry.QueuedAt >= _stageStart && entry.Kind == ActionKind.CastSpell && entry.Source == ActionSource.Automation && entry.Outcome.Kind == ActionOutcomeKind.Confirmed)
                {
                    count++;
                }
            }

            _confirmed = Math.Max(_confirmed, count);
        }
    }
}

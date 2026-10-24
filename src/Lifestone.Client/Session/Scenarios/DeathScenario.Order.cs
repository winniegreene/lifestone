using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The composed inner scenario of the death loop's rebuff and fight stages (M4 3.1 d1): what <see cref="DeathScenario"/> needs from a <see cref="FightScenario"/>, so the tests substitute a scripted one (<see cref="FightScenario"/> is sealed and its
/// buff note and cycle are private). <see cref="FightLeg"/> is the real one.
/// </summary>
public interface IFightLeg : IScenario, IDisposable
{
    /// <summary>The kills the fight has counted so far (the server's 0x01AD notices).</summary>
    int Kills { get; }

    /// <summary>The buff stage's note, as <see cref="FightScenario.VerdictFields"/> prints it after <c>buffs=</c>: <c>n/a</c> before the stage, <c>skipped</c> (every profile line already had enough time left), <c>topped-up:&lt;casts&gt;</c>.</summary>
    string BuffsNote { get; }

    /// <summary>True when the buff stage ended with every profile line up: skipped because every line was up, or topped up with the cycle's active lines equal to its wanted lines.</summary>
    bool BuffsAllUp { get; }

    /// <summary>The inner scenario's verdict fields (<see cref="FightScenario.VerdictFields"/>).</summary>
    string VerdictFields();
}

/// <summary>Makes the inner scenario of the rebuff and fight stages (<see cref="DeathScenario.DefaultFightLeg"/> is the real one).</summary>
/// <param name="kit">The kit (the plain one: the inner scenario never runs the gear stage).</param>
/// <param name="spells">The SpellTable's spells.</param>
/// <param name="buff">The buff cycle's options.</param>
/// <param name="fight">The fight's options.</param>
/// <param name="walk">The walk along the den's floor, shared with the death loop's own (the inner scenario disposes it when it ends).</param>
/// <param name="log">Where the inner scenario logs.</param>
/// <param name="arrivalSeconds">The limit on the arrival.</param>
/// <param name="endSession">Always false: the death loop owns the session.</param>
/// <param name="stages">The stages before the fight: <see cref="FightStages.Buffs"/> for the rebuff, <see cref="FightStages.Place"/> and <see cref="FightStages.Mode"/> for the fight.</param>
public delegate IFightLeg FightLegFactory(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions buff, FightOptions fight, IFightWalk? walk, ILogSink log, double arrivalSeconds, bool endSession, FightStages stages);

/// <summary>The real <see cref="IFightLeg"/>: a <see cref="FightScenario"/> and nothing else (M4 3.1 d1).</summary>
/// <param name="inner">The fight scenario.</param>
public sealed class FightLeg(FightScenario inner) : IFightLeg
{
    /// <inheritdoc/>
    public string Name => inner.Name;

    /// <inheritdoc/>
    public string Stage => inner.Stage;

    /// <inheritdoc/>
    public string? Failure => inner.Failure;

    /// <inheritdoc/>
    public double? WhereMaxMetres => inner.WhereMaxMetres;

    /// <inheritdoc/>
    public long Unmatched => inner.Unmatched;

    /// <inheritdoc/>
    public int Kills => inner.Tally.Kills;

    /// <inheritdoc/>
    public string BuffsNote => Field(inner.VerdictFields(), "buffs");

    /// <inheritdoc/>
    public bool BuffsAllUp =>
        BuffsNote switch
        {
            "skipped" => true,
            var note when note.StartsWith("topped-up:", StringComparison.Ordinal) => inner.Cycle?.Result is { Wanted: > 0 } result && result.Active >= result.Wanted,
            _ => false,
        };

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now) => inner.Step(session, now);

    /// <inheritdoc/>
    public string VerdictFields() => inner.VerdictFields();

    /// <inheritdoc/>
    public void Dispose() => inner.Dispose();

    private static string Field(string fields, string name)
    {
        var key = name + "=";
        var at = fields.StartsWith(key, StringComparison.Ordinal) ? 0 : fields.IndexOf(" " + key, StringComparison.Ordinal) + 1;
        if (at < 0 || (at == 0 && !fields.StartsWith(key, StringComparison.Ordinal)))
        {
            return "n/a";
        }

        var start = at + key.Length;
        var end = fields.IndexOf(' ', start);
        return end < 0 ? fields[start..] : fields[start..end];
    }
}

/// <summary>
/// The death loop's order after the respawn (M4 3.1 d1, D20 and D22), the decoys and the measures' freeze. The order, by what is on (<see cref="DeathStages"/> and <see cref="DeathOptions"/>):
/// <list type="bullet">
/// <item><b>Fight on (the default):</b> <c>respawn-vitals</c> (the measures freeze here), <c>regear</c> (what the death destroyed, with the PLAIN kit: the decoys on the corpse are not spawned a second time), <c>rebuff</c> (the buff cycle at Holtburg: the death purged
/// every enchantment), <c>fight</c> (the den, the magic mode and the kill loop until <see cref="DeathOptions.FightKills"/> kills or <see cref="DeathOptions.FightSeconds"/>), <c>pretrip-heal</c> (only when the health share is the one precondition failing: Heal Self VII and regeneration, <c>DeathScenario.Heal.cs</c>), <c>corpse-*</c> (the 2.2 trip, unchanged inside, with the tuskers dead), the return to Holtburg, <c>done</c>.</item>
/// <item><b>Fight off:</b> M4 2.2's order, <c>respawn-vitals</c>, <c>corpse-*</c>, <c>regear</c>, then <c>rebuff</c> (alone, when it is on), <c>done</c>; with the rebuff off too it is exactly the loop that merged in 2.2.</item>
/// </list>
/// Rebuff and fight are ONE inner scenario (<see cref="IFightLeg"/>, by default <see cref="FightLeg"/> over <see cref="FightScenario"/>, from <see cref="FightLegFactory"/>) stepped like the regear's <see cref="GearScenario"/>: the rebuff is its
/// <see cref="FightStages.Buffs"/> stage (the scenario's own stage <c>place</c> marks its end), the fight its <see cref="FightStages.Place"/> and <see cref="FightStages.Mode"/> stages and its kill loop; with only the rebuff on the inner scenario is dropped at <c>place</c> and the player goes back to
/// non-combat (<c>rebuff-peace</c>). <b>The judged measures freeze at the end of <c>respawn-vitals</c></b> (<c>_atRespawn</c>): the regear, the rebuff, the fight and the trip never move <c>died</c>, <c>vitae</c>, <c>vitals_pct</c>, <c>hits_taken</c>, <c>health_min_pct</c> and the rest; a death
/// DURING the rebuff or the fight fails the leg, naming the stage (a new death is never <c>died=1</c>'s second count).
/// </summary>
public sealed partial class DeathScenario
{
    private static readonly DeathStages[] OldOrder = [DeathStages.Corpse, DeathStages.Regear, DeathStages.Rebuff];
    private static readonly DeathStages[] NewOrder = [DeathStages.Regear, DeathStages.Rebuff, DeathStages.Fight, DeathStages.Corpse];

    private readonly GearKit _regearKit;
    private readonly BuffOptions _buffOptions;
    private readonly FightOptions _fightOptions;
    private readonly FightLegFactory _fightFactory;
    private IFightLeg? _inner;
    private int _postIndex = -1;
    private string _rebuffed = DeathLegVerdict.RebuffedSkipped;
    private int _denKills;
    private int _afterNotices;
    private int _afterDeadMotions;
    private int _afterDeaths;
    private DecoyCheck? _decoyCheck;
    private int? _decoysAtDeath;
    private ListedEntry[]? _listing;
    private int? _decoysDropped;
    private int? _gearDropped;
    private int? _gearLeft;
    private int? _listingEntries;
    private int? _listingCoins;
    private int? _decoysRecovered;

    private struct ListedEntry(uint guid)
    {
        public uint Guid = guid;
        public uint Wcid;
    }

    /// <summary>The real inner scenario: a <see cref="FightScenario"/> over the kit (<see cref="FightLeg"/>).</summary>
    /// <param name="kit">The kit.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="buff">The buff cycle's options.</param>
    /// <param name="fight">The fight's options.</param>
    /// <param name="walk">The walk along the den's floor.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True: log off and quit when done (never here).</param>
    /// <param name="stages">The stages to run before the fight.</param>
    public static IFightLeg DefaultFightLeg(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions buff, FightOptions fight, IFightWalk? walk, ILogSink log, double arrivalSeconds, bool endSession, FightStages stages) =>
        new FightLeg(new FightScenario(kit, spells, buff, fight, walk, log, arrivalSeconds, endSession, stages));

    private bool RegearOn => (_stages & DeathStages.Regear) != 0 && !_stopped && !_diedEarly;

    private bool RebuffOn => (_stages & DeathStages.Rebuff) != 0 && _options.Rebuff;

    private bool FightOn => (_stages & DeathStages.Fight) != 0 && _options.Fight;

    /// <summary>The kit the regear spawns from: the kit without its decoys (they are on the corpse; M4 3.1 d1).</summary>
    public GearKit RegearKit => _regearKit;

    /// <summary><c>rebuffed=</c>: <c>yes</c>, <c>no</c> or <c>skipped</c> (<see cref="DeathLegVerdict.RebuffedYes"/>).</summary>
    public string Rebuffed => _rebuffed;

    /// <summary><c>den_kills=</c>: the kills the fight stage's inner scenario counted.</summary>
    public int DenKills => _denKills;

    /// <summary><c>decoys=</c>: the decoys the pack held when the wait among the tuskers began, or null (<c>--death-decoys off</c>, or the wait never began).</summary>
    public int? DecoysCarried => _options.Decoys ? _decoysAtDeath : null;

    /// <summary>The gear stage's decoy check (<c>decoy_safe=</c> is its <see cref="DecoyCheck.Field"/>), or null when it did not run.</summary>
    public DecoyCheck? DecoyCheckResult => _decoyCheck;

    /// <summary><c>decoy_safe=</c>: the gear stage's check, or <c>n/a</c> (<c>--death-decoys off</c>, or no gear stage ran).</summary>
    public string DecoySafeField => _options.Decoys && _decoyCheck is { } check ? check.Field : DeathLegVerdict.NotApplicable;

    /// <summary><c>decoys_dropped=</c>: the decoys in the corpse's own listing, or null (decoys off, or no listing was read).</summary>
    public int? DecoysDropped => _decoysDropped;

    /// <summary><c>gear_dropped=</c>: the listed items <see cref="DeathDropModel.IsNeeded"/> says the loops need, or null.</summary>
    public int? GearDropped => _gearDropped;

    /// <summary>The entries of the corpse's own listing (every kind, pyreals included), or null when no listing was read (M4 3.1 fix, review R20).</summary>
    public int? ListingEntries => _listingEntries;

    /// <summary>The listed entries that are pyreal stacks (<see cref="DeathDropModel.CoinWcid"/>): ACE puts half the pyreals on the corpse, so a mage that carries coins lists them beside its decoys; they are neither decoys nor gear. Null when no listing was read.</summary>
    public int? ListingCoins => _listingCoins;

    /// <summary><c>gear_left=</c> (report-only; M4 3.1 fix, review R2): the listed items the loops need that were not taken from the corpse, or null (no listing was read). In the order whose regear runs before the trip each is a duplicate on the corpse.</summary>
    public int? GearLeft => _gearLeft;

    /// <summary><c>decoys_recovered=</c>: the listed decoys that are in the pack after the trip, or null.</summary>
    public int? DecoysRecovered => _decoysRecovered;

    // ------------------------------------------------------------------------------------------------ the order

    /// <summary>
    /// The end of the respawn's vitals wait (the respawn judged lifestone): the judged measures freeze here, once (<c>_atRespawn</c>), and the stages after the respawn begin in their order. The marks are the tally's counts of victim notices, Dead motions and deaths: a
    /// rise in any of them during the rebuff or the fight is a new death.
    /// </summary>
    private ScenarioStatus BeginPost(IGameSessionView session, double clock)
    {
        _atRespawn = TallyMeasures(session);
        (_afterNotices, _afterDeadMotions, _afterDeaths) = (_tally.VictimNotices, _tally.DeadMotions, _tally.Deaths);
        _postIndex = -1;
        _log.Info(LogCategory, "the stages after the respawn: " + string.Join(", ", EnabledPost()) + " (the judged measures are frozen here)");
        return NextPost(session, clock);
    }

    private IEnumerable<string> EnabledPost()
    {
        foreach (var stage in FightOn ? NewOrder : OldOrder)
        {
            var on = stage switch
            {
                DeathStages.Corpse => (_stages & DeathStages.Corpse) != 0 && _options.RecoverCorpse,
                DeathStages.Regear => RegearOn,
                DeathStages.Rebuff => RebuffOn,
                _ => FightOn,
            };
            if (on)
            {
                yield return stage.ToString().ToLowerInvariant();
            }
        }
    }

    /// <summary>Starts the next enabled stage after the one that just ended (or the first, after <see cref="BeginPost"/>); past the last one the run concludes.</summary>
    private ScenarioStatus NextPost(IGameSessionView session, double clock)
    {
        var order = FightOn ? NewOrder : OldOrder;
        while (++_postIndex < order.Length)
        {
            switch (order[_postIndex])
            {
                case DeathStages.Corpse:
                    if (TryBeginCorpse(session, clock))
                    {
                        return ScenarioStatus.Running;
                    }

                    break;
                case DeathStages.Regear:
                    if (RegearOn)
                    {
                        StartRegear(clock);
                        return ScenarioStatus.Running;
                    }

                    break;
                case DeathStages.Rebuff:
                    if (RebuffOn)
                    {
                        EnsureInner();
                        Enter("rebuff", clock);
                        return ScenarioStatus.Running;
                    }

                    break;
                case DeathStages.Fight:
                    if (FightOn)
                    {
                        EnsureInner();
                        Enter("fight", clock);
                        return ScenarioStatus.Running;
                    }

                    break;
            }
        }

        return Conclude(session, clock);
    }

    private void StartRegear(double clock)
    {
        _postIndex = Array.IndexOf(FightOn ? NewOrder : OldOrder, DeathStages.Regear); // entered by Begin (a run of the regear alone) or by NextPost: the order goes on from here
        _regear = new GearScenario(_regearKit, _log, _arrivalSeconds, endSession: false, cast: false, _spells);
        Enter("regear-settle", clock);
    }

    // ------------------------------------------------------------------------------------------------ the rebuff and the fight

    private void EnsureInner()
    {
        if (_inner is not null)
        {
            return;
        }

        var stages = (RebuffOn ? FightStages.Buffs : FightStages.None) | (FightOn ? FightStages.Place | FightStages.Mode : FightStages.None);
        _inner = _fightFactory(_regearKit, _spells, _buffOptions, _fightOptions, _walk, _log, _arrivalSeconds, false, stages);
        _log.Info(LogCategory, $"the composed fight scenario: stages {stages.ToString().ToLowerInvariant()}; {_fightOptions}");
    }

    /// <summary>A death after the respawn's vitals wait, during the rebuff or the fight, fails the leg naming the stage. Null when none came.</summary>
    private ScenarioStatus? OrderGuard(IGameSessionView session, string stage)
    {
        if (_tally.VictimNotices <= _afterNotices && _tally.DeadMotions <= _afterDeadMotions && _tally.Deaths <= _afterDeaths)
        {
            return null;
        }

        if (stage == "rebuff")
        {
            _rebuffed = DeathLegVerdict.RebuffedNo; // the stage ran and the leg failed in it
        }

        return Fail(
            session,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{stage}: the mage died during the {stage} (since the respawn's vitals wait: victim notices {_tally.VictimNotices - _afterNotices}, Dead motions {_tally.DeadMotions - _afterDeadMotions}, deaths {_tally.Deaths - _afterDeaths}); died= keeps the tuskers' death, a second death is never counted in it"));
    }

    private static bool IsBuffing(string stage) => stage is "start" or "gear" or "buffs" or "buffs-mode" or "buffs-cycle";

    private ScenarioStatus RebuffStep(IGameSessionView session, double now)
    {
        if (OrderGuard(session, "rebuff") is { } died)
        {
            return died;
        }

        var inner = _inner!;
        switch (inner.Step(session, now))
        {
            case ScenarioStatus.Failed:
                _rebuffed = DeathLegVerdict.RebuffedNo;
                return Fail(session, "rebuff: " + inner.Failure);
            case ScenarioStatus.Succeeded:
                return RebuffDone(session);
            default:
                return IsBuffing(inner.Stage) ? ScenarioStatus.Running : RebuffDone(session);
        }
    }

    private ScenarioStatus RebuffDone(IGameSessionView session)
    {
        var clock = session.Clock.Now;
        var inner = _inner!;
        var up = inner.BuffsAllUp;
        _rebuffed = up ? DeathLegVerdict.RebuffedYes : DeathLegVerdict.RebuffedNo;
        if (up)
        {
            _log.Info(LogCategory, $"rebuff: done (buffs={inner.BuffsNote}, every profile line up); rebuffed=yes");
        }
        else
        {
            _log.Warn(LogCategory, $"rebuff: the buff stage ended with a profile line down (buffs={inner.BuffsNote}); rebuffed=no (report-only)");
        }

        if (FightOn)
        {
            return NextPost(session, clock); // the same inner scenario goes on to the den: it is already past its buff stage
        }

        // The rebuff alone: the inner scenario would go on to its fight, so it is dropped here and the mage leaves the magic mode the cycle entered.
        inner.Dispose();
        _inner = null;
        if (session.State.Self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            Enter("rebuff-peace", clock);
            return ScenarioStatus.Running;
        }

        return NextPost(session, clock);
    }

    private ScenarioStatus RebuffPeaceStep(IGameSessionView session, double clock)
    {
        if (OrderGuard(session, "rebuff") is { } died)
        {
            return died;
        }

        if (_pending is not { Outcome: { } peace })
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (!peace.Succeeded)
        {
            _log.Warn(LogCategory, "rebuff: the mage was left in the magic combat mode: " + Describe(peace)); // report-only, as the fight scenario's mode-peace
        }

        return NextPost(session, clock);
    }

    private ScenarioStatus FightStep(IGameSessionView session, double now)
    {
        if (OrderGuard(session, "fight") is { } died)
        {
            return died;
        }

        if (FightManaWaiting(session))
        {
            return ScenarioStatus.Running;
        }

        var inner = _inner!;
        var status = inner.Step(session, now);
        _denKills = inner.Kills;
        switch (status)
        {
            case ScenarioStatus.Failed:
                return Fail(session, "fight: " + inner.Failure);
            case ScenarioStatus.Succeeded:
                _log.Info(LogCategory, $"fight: done; den_kills={_denKills}");
                inner.Dispose();
                _inner = null;
                return NextPost(session, session.Clock.Now);
            default:
                return ScenarioStatus.Running;
        }
    }

    /// <summary>
    /// M4 P4 g3fix: the bounded precondition wait before the den (see <see cref="DeathOptions.FightManaWaitSeconds"/>): true while the mana is under the floor and the wait has time left. Logged once at its start and once at its end with the numbers;
    /// when the bound passes with the mana short the fight goes on (it waits for mana itself, as before) and a WARN names the numbers, so the cause stays on the log.
    /// </summary>
    private bool FightManaWaiting(IGameSessionView session)
    {
        if (_options.FightManaWaitSeconds <= 0 || _fightManaDone)
        {
            return false;
        }

        var now = session.Clock.Now;
        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var current, out var max) || max == 0)
        {
            _fightManaDone = true;
            _log.Warn(LogCategory, "fight: the read model has no mana vital; no mana wait before the den");
            return false;
        }

        var floor = Math.Ceiling(max * _options.FightManaFloorPercent / 100.0);
        if (double.IsNaN(_fightManaStart))
        {
            _fightManaStart = now;
            if (current >= floor)
            {
                _fightManaDone = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: mana {current} of {max} is at the {floor:0} floor ({_options.FightManaFloorPercent:0.#}%): no wait before the den"));
                return false;
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: mana {current} of {max}, the floor is {floor:0} ({_options.FightManaFloorPercent:0.#}%): waiting for regeneration before the den, at most {_options.FightManaWaitSeconds:0} s (a precondition wait, nothing has been cast)"));
            return true;
        }

        if (current >= floor)
        {
            _fightManaDone = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: mana {current} reached the {floor:0} floor after {now - _fightManaStart:0.#} s"));
            return false;
        }

        if (now - _fightManaStart > _options.FightManaWaitSeconds)
        {
            _fightManaDone = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight: mana {current} of {max} did not reach the {floor:0} floor within {_options.FightManaWaitSeconds:0} s; the fight goes on and waits for mana itself"));
            return false;
        }

        return true;
    }

    private double _fightManaStart = double.NaN;
    private bool _fightManaDone;

    // ------------------------------------------------------------------------------------------------ the decoys

    /// <summary>The gear stage's check, once, after the gear steps: the text is logged, <c>decoy_safe=</c> is its field. With <c>--death-decoys off</c> there is none.</summary>
    private void RunDecoyCheck(IGameSessionView session)
    {
        if (!_options.Decoys)
        {
            _log.Info(LogCategory, "decoys: --death-decoys is off: no decoy check, every decoy field reads n/a");
            return;
        }

        var check = DecoyKit.Check(session.State, session.State.Self.Qualities.Level);
        _decoyCheck = check;
        if (check.Made && check.Verdict.Survives)
        {
            _log.Info(LogCategory, check.Text);
        }
        else
        {
            _log.Warn(LogCategory, check.Text + " (decoy_safe=no)");
        }

        // M4 3.1 fix, review R7: the live load share once after the gear steps, a warning over 95% (no behaviour change: the decoy kit and the regear's duplicates are the mage's burden, and nothing here trims them).
        var load = DecoyKit.LoadOf(session.State);
        if (load.Over)
        {
            _log.Warn(LogCategory, DecoyKit.LoadText(in load));
        }
        else
        {
            _log.Info(LogCategory, DecoyKit.LoadText(in load));
        }
    }

    /// <summary>
    /// When the wait among the tuskers begins: the decoys the pack holds (<c>decoys=</c>), by weenie class, and the check again over the live inventory, because the unbuff suicide is a death too and drops items by the same rule (D33): a run whose suicide
    /// dropped decoys reads fewer here than the kit has, and the line says it is not safe when that leaves the wand under the drops. Report-only.
    /// </summary>
    private void CountDecoysAtDeath(IGameSessionView session)
    {
        if (!_options.Decoys)
        {
            return;
        }

        _scratch.Clear();
        GearInventory.Collect(session.State, _scratch);
        var held = 0;
        foreach (var guid in _scratch)
        {
            if (session.State.Objects.TryGet(guid, out var item) && DecoyKit.IsDecoy(item.Weenie.WeenieClassId))
            {
                held++;
            }
        }

        _decoysAtDeath = held;
        var check = DecoyKit.Check(session.State, session.State.Self.Qualities.Level);
        if (check.Made && check.Verdict.Survives)
        {
            _log.Info(LogCategory, $"decoys: {held} in the pack as the wait among the tuskers begins; {check.Text}");
        }
        else
        {
            _log.Warn(LogCategory, $"decoys: {held} in the pack as the wait among the tuskers begins; {check.Text} (not safe: the unbuff suicide, a death too, drops items by the same rule)");
        }
    }

    /// <summary>The corpse's own listing at its open: the entries' guids (the weenie classes are read as the F745s arrive, <see cref="ResolveListing"/>). Nothing with <c>--death-decoys off</c>.</summary>
    private void CaptureListing(ContainerView view)
    {
        if (!_options.Decoys)
        {
            return;
        }

        var entries = new ListedEntry[view.Entries.Count];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new ListedEntry(view.Entries[i].Guid);
        }

        _listing = entries;
    }

    /// <summary>Reads each listed entry's weenie class from the object table while it is there (the table forgets the corpse's contents when the mage leaves). No allocation.</summary>
    private void ResolveListing(IGameSessionView session)
    {
        if (_listing is not { } entries)
        {
            return;
        }

        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Wcid == 0 && session.State.Objects.TryGet(entries[i].Guid, out var item))
            {
                entries[i].Wcid = item.Weenie.WeenieClassId;
            }
        }
    }

    /// <summary>
    /// At the end of the trip: the listing classified by weenie class (<c>decoys_dropped=</c> the <see cref="DecoyKit.IsDecoy"/> entries, <c>gear_dropped=</c> the <see cref="DeathDropModel.IsNeeded"/> ones), and the entries now in the pack counted
    /// (<c>decoys_recovered=</c>: a taken entry keeps its guid). A needed item that was listed and is not in the pack is counted (<c>gear_left=</c>, report-only in its first gate; M4 3.1 fix, review R2) and warned about, never a failure: in the order whose regear ran
    /// BEFORE the trip (the fight is on) the regear already replaced it, so what is left behind is a duplicate on the corpse, not a missing item; in M4 2.2's order the regear after the trip spawns it.
    /// </summary>
    private void ClassifyListing(IGameSessionView session)
    {
        ResolveListing(session);
        if (_listing is not { } entries)
        {
            return;
        }

        int decoys = 0, gear = 0, recovered = 0, unresolved = 0, coins = 0;
        List<uint>? missed = null;
        foreach (var entry in entries)
        {
            var taken = session.State.Self.Inventory.Contains(entry.Guid);
            if (entry.Wcid == 0)
            {
                unresolved++;
            }
            else if (DecoyKit.IsDecoy(entry.Wcid))
            {
                decoys++;
                if (taken)
                {
                    recovered++;
                }
            }
            else if (DeathDropModel.IsNeeded(entry.Wcid))
            {
                gear++;
                if (!taken)
                {
                    (missed ??= []).Add(entry.Wcid);
                }
            }
            else if (entry.Wcid == DeathDropModel.CoinWcid)
            {
                coins++; // review R20: ACE puts half the pyreals on the corpse (Player_Death.cs), a stack that is neither a decoy nor gear
            }
        }

        (_decoysDropped, _gearDropped, _decoysRecovered, _gearLeft) = (decoys, gear, recovered, missed?.Count ?? 0);
        (_listingEntries, _listingCoins) = (entries.Length, coins);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: the listing held {entries.Length} entries: decoys_dropped={decoys} gear_dropped={gear} decoys_recovered={recovered}{(unresolved == 0 ? string.Empty : $"; {unresolved} never resolved to a weenie class, not classified")}{(coins == 0 ? string.Empty : $"; {coins} of them pyreal stack(s) (wcid {DeathDropModel.CoinWcid}: ACE puts half the pyreals on the corpse), neither a decoy nor gear")}"));
        if (missed is null)
        {
            return;
        }

        var left = string.Create(CultureInfo.InvariantCulture, $"corpse: gear_left={missed.Count}: {missed.Count} item(s) the loops need were on the corpse and were not taken (weenie classes {string.Join(", ", missed)})");
        _log.Warn(
            LogCategory,
            FightOn
                ? left + "; the regear before the trip already replaced them, so what is left behind is a duplicate on the corpse, not a missing item (report-only in its first gate, review R2)"
                : left + "; the regear after the trip spawns them (not a failure in M4 2.2's order)");
    }
}

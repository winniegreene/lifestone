using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;
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

    /// <summary>M5 3 d1: the lines the buff stage missed, as the buff cycle names them (empty before the stage or when none missed): the death handler names the item lines it re-casts from them.</summary>
    IReadOnlyList<string> BuffMisses => [];
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
    public IReadOnlyList<string> BuffMisses => inner.Cycle?.Result?.Misses ?? [];

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
/// The death loop's order after the respawn (M5 3 d2, D51: one order for every option set) and the measures' freeze. After <c>respawn-vitals</c> the order is the death handler's (<see cref="DeathHandler"/>): the respawn, the rebuff of what is held,
/// the pre-trip heal, the corpse trip by its route (Primary Portal Recall or the runner, never a place), the items back (worn and wielded again), the item lines re-cast, the fight. <c>--death-fight off</c> keeps the handler and skips its return
/// to the fight; <c>--death-rebuff off</c> and <c>--death-recover-corpse off</c> skip their steps. No mod verb is sent from the respawn on: no item, experience or place verb (the regear by the item verb, the vitae grant and the decoy top-up are removed).
/// The handler's steps are shown as the stages <c>handler-&lt;step&gt;</c> (<see cref="DeathScenario.HandlerStagePrefix"/>), logged once on entry. <b>The judged measures freeze at the end of <c>respawn-vitals</c></b> (<c>_atRespawn</c>): the handler never moves
/// <c>died</c>, <c>vitae</c>, <c>vitals_pct</c>, <c>hits_taken</c>, <c>health_min_pct</c> and the rest; a death during the handler restarts it for the newer corpse (<c>deaths=</c> of the handler's fields counts it).
/// </summary>
public sealed partial class DeathScenario
{
    private readonly BuffOptions _buffOptions;
    private readonly FightOptions _fightOptions;
    private readonly FightLegFactory _fightFactory;
    private IFightLeg? _inner;
    private string _rebuffed = DeathLegVerdict.RebuffedSkipped;
    private int _denKills;
    private ListedEntry[]? _listing;
    private int? _gearLeft;
    private int? _listingEntries;
    private int? _listingCoins;

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

    // ------------------------------------------------------------------------------------------------ the death handler (M5 3 d1, D51)

    private DeathHandler? _handler;
    private string? _handlerStep;

    /// <summary>The death handler that runs after the respawn with the fight on (the default), or null (before the respawn, or a stage list with none of the rebuff, corpse and fight stages).</summary>
    public DeathHandler? Handler => _handler;

    /// <inheritdoc />
    public void NoteHeld(double seconds) => _handler?.NoteHeld(seconds);

    /// <summary>
    /// The handler's verdict fields (additive at the end of the death's line, before the optional <c>frame_over_50=yes</c>: <c>deaths= corpse_recovered= route= items_back= vitae_grants= rebuffed= relines=</c>), or
    /// <see cref="DeathHandler.UnmeasuredFields"/> when no handler ran. <see cref="VerdictFields"/> appends them (closes <c>m5-death-handler-fields</c>).
    /// </summary>
    public string HandlerFields => _handler?.Fields ?? DeathHandler.UnmeasuredFields;

    /// <summary>The prefix of the stages that are the handler's steps (<c>handler-respawn</c>, <c>handler-corpse-loot</c>, ...).</summary>
    public const string HandlerStagePrefix = "handler-";

    /// <summary>
    /// M5 3 d2 (D51, DoD 7): the order after <c>respawn-vitals</c> for every option set. The stage list picks what the handler may do (a stage out of <see cref="DeathStages"/> turns its option off for this run); the options pick the rest.
    /// </summary>
    private ScenarioStatus BeginHandler(IGameSessionView session, double clock)
    {
        _log.Info(LogCategory, "the stages after the respawn: the death handler (D51: rebuff, the corpse trip, the items back, the item lines, the fight; no regear, no vitae grant, no place) (the judged measures are frozen here)");
        var effective = _options with
        {
            Rebuff = _options.Rebuff && (_stages & DeathStages.Rebuff) != 0,
            RecoverCorpse = _options.RecoverCorpse && (_stages & DeathStages.Corpse) != 0,
            Fight = _options.Fight && (_stages & DeathStages.Fight) != 0,
        };
        _handler = new DeathHandler(_ground, _tally, _seam.Travel, _fightFactory, _walk, _kit, _spells, _buffOptions, _fightOptions, effective, _log, _arrivalSeconds)
        {
            HoldBeforeFight = FightManaWaiting,
            BeforeRebuff = BuffBotBeforeRebuff,
        };
        _handler.Begin(session, clock, DeathFacts.From(_tally), BoundLifestone, respawned: true, respawnRead: _enteredAgain);
        return HandlerStep(session, clock);
    }

    /// <summary>One frame of the handler, with the scenario's stage following the handler's step and the measures it owns (<c>rebuffed=</c>, <c>den_kills=</c>, <c>gear_restored=</c>, <c>worn=</c>) read from it.</summary>
    private ScenarioStatus HandlerStep(IGameSessionView session, double now)
    {
        var handler = _handler!;
        var status = handler.Step(session, session.Clock.Now);
        _inner = handler.Inner; // the scenario's Dispose disposes the inner fight the handler runs
        (_rebuffed, _denKills) = (handler.RebuffedPretrip, handler.Kills); // stage_rebuffed= stays the rebuff's own verdict before the trip (3-R2); rebuffed= is the handler's, after the item lines (D56)
        if (handler.CurrentStep is "relines" or "fight" or "done")
        {
            (_gearRestored, _regearFields) = (handler.GearBack, handler.WornFields);
        }

        // The corpse's own listing (M4 3.1 d1) is read from the handler's open view, so gear_left= keeps DeathDropModel's prediction against what the corpse held (D51; the decoys that once filled the listing are retired, D51).
        if (handler.CurrentStep is "corpse-loot" && _listing is null && session.State.Containers.Viewed is { } view && view.Guid == handler.Facts.CorpseGuid)
        {
            CaptureListing(view);
        }

        if (handler.CurrentStep is "corpse-loot" or "corpse-close")
        {
            ResolveListing(session);
        }
        else if (handler.CurrentStep is "wear" && _listing is not null && _listingEntries is null)
        {
            ClassifyListing(session);
        }

        var clock = session.Clock.Now;
        switch (status)
        {
            case ScenarioStatus.Failed:
                _inner = null;
                return Fail(session, handler.Failure ?? "handler: failed without saying why");
            case ScenarioStatus.Succeeded:
                _inner = null;
                return Conclude(session, clock);
        }

        if (!ReferenceEquals(_handlerStep, handler.CurrentStep))
        {
            _handlerStep = handler.CurrentStep;
            Enter(HandlerStagePrefix + _handlerStep, clock); // one allocation per step change
        }

        return ScenarioStatus.Running;
    }

    /// <summary><c>stage_rebuffed=</c> on the verdict line (3-R2): <c>yes</c>, <c>no</c> or <c>skipped</c> (<see cref="DeathLegVerdict.RebuffedYes"/>).</summary>
    public string Rebuffed => _rebuffed;

    /// <summary><c>den_kills=</c>: the kills the fight stage's inner scenario counted.</summary>
    public int DenKills => _denKills;

    /// <summary>The entries of the corpse's own listing (every kind, pyreals included), or null when no listing was read (M4 3.1 fix, review R20).</summary>
    public int? ListingEntries => _listingEntries;

    /// <summary>The listed entries that are pyreal stacks (<see cref="DeathDropModel.CoinWcid"/>): ACE destroys the coin a death drops (D49), so a stack here is a legacy listing; it is not gear. Null when no listing was read.</summary>
    public int? ListingCoins => _listingCoins;

    /// <summary><c>gear_left=</c> (report-only; M4 3.1 fix, review R2): the listed items the loops need that were not taken from the corpse, or null (no listing was read). Under D51 the corpse trip is the only way the item comes back, so each is a loss until it is bought or found.</summary>
    public int? GearLeft => _gearLeft;

    // ------------------------------------------------------------------------------------------------ the order

    /// <summary>
    /// The end of the respawn's vitals wait (the respawn judged lifestone): the judged measures freeze here, once (<c>_atRespawn</c>), and the death handler begins. A stage list with none of the rebuff, corpse and fight stages concludes at once.
    /// </summary>
    private ScenarioStatus BeginPost(IGameSessionView session, double clock)
    {
        _atRespawn = TallyMeasures(session);
        if ((_stages & (DeathStages.Rebuff | DeathStages.Corpse | DeathStages.Fight)) == 0)
        {
            _log.Info(LogCategory, "the stages after the respawn: none in this run's stages (the judged measures are frozen here)");
            return Conclude(session, clock);
        }

        return BeginHandler(session, clock);
    }

    /// <summary>
    /// M4 P4 g3fix: the bounded precondition wait before the den (see <see cref="DeathOptions.FightManaWaitSeconds"/>): true while the mana is under the floor and the wait has time left. Logged once at its start and once at its end with the numbers;
    /// when the bound passes with the mana short the fight goes on (it waits for mana itself, as before) and a WARN names the numbers, so the cause stays on the log.
    /// </summary>
    private IStationRun? _botRun;
    private bool _botDone;

    /// <summary>
    /// M6 item 14 (the climb's third death, 2026-11-11 18:52 UTC): the stop at the buff bot before the death's rebuff. She respawns at the lifestone, ten metres from the bot; her own pass would cast her own levels and the
    /// corpse trip would walk back into the ground on them. When the stop applies to the run (<see cref="JourneyFacts.BuffBotOn"/>) and a line is due (<see cref="BuffBotRule.Due"/>), the station runs first; her own pass
    /// follows it either way and casts what the bot does not cover. A stop that fails is logged by name and never fails the death's order. True while the stop runs.
    /// </summary>
    internal bool BuffBotBeforeRebuff(IGameSessionView session, double now)
    {
        if (_botDone)
        {
            return false;
        }

        if (_botRun is null)
        {
            if (_options.Journey is not { } journey || journey.Facts.BuffBotOn?.Invoke() != true)
            {
                _botDone = true;
                return false;
            }

            var reads = new List<BuffBotLineRead>();
            var profile = journey.Facts.BuffBotProfile; // the run's profile (the option's word, 'all' by default): the stop is due by its lines and tells its key
            BuffBotRule.Read(profile, session.State.Self.Qualities, session.Clock.Now, reads);
            var least = BuffBotRule.MinRemaining(reads);
            if (!BuffBotRule.Due(reads))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: the buff bot is not due before the rebuff: every line of '{profile.Key}' has at least {BuffBotRule.RenewUnderSeconds:0} s left (the least {least:0})"));
                _botDone = true;
                return false;
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: the buff bot before the rebuff: {BuffBotRule.CountWith(reads, 1)} of {reads.Count} lines of '{profile.Key}' are up, the least {least:0} s left (under {BuffBotRule.RenewUnderSeconds:0})"));
            _botRun = new BuffBotStation().Start(journey);
        }

        var status = _botRun.Step(session, session.Clock.Now);
        if (status == ScenarioStatus.Running)
        {
            return true;
        }

        if (status == ScenarioStatus.Succeeded && _botRun.Ok)
        {
            _log.Info(LogCategory, $"death: the buff bot's stop ended {status}: {_botRun.VerdictFields()}: her own pass for what the bot does not cover");
        }
        else
        {
            _log.Warn(LogCategory, $"death: the buff bot's stop failed ({_botRun.VerdictFields()}): the order goes on with her own whole pass (m6-buffbot-fallback)");
        }

        (_botRun, _botDone) = (null, true);
        return false;
    }

    internal bool FightManaWaiting(IGameSessionView session)
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

    // ------------------------------------------------------------------------------------------------ the corpse's listing

    /// <summary>The corpse's own listing at its open: the entries' guids (the weenie classes are read as the F745s arrive, <see cref="ResolveListing"/>).</summary>
    private void CaptureListing(ContainerView view)
    {
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
    /// At the end of the trip: the listing classified by weenie class (the <see cref="DeathDropModel.IsNeeded(GearKit, uint)"/> entries are the lines of the mage's own kit, M5 3 d4). A needed item that was listed and is not in the pack is counted (<c>gear_left=</c>,
    /// report-only in its first gate; M4 3.1 fix, review R2) and warned about, never a failure: under D51 the handler takes what the corpse gives and spawns nothing in its place, so what is left behind is missing until it is bought or found.
    /// </summary>
    private void ClassifyListing(IGameSessionView session)
    {
        ResolveListing(session);
        if (_listing is not { } entries)
        {
            return;
        }

        int gear = 0, unresolved = 0, coins = 0;
        List<uint>? missed = null;
        foreach (var entry in entries)
        {
            var taken = session.State.Self.Inventory.Contains(entry.Guid);
            if (entry.Wcid == 0)
            {
                unresolved++;
            }
            else if (DeathDropModel.IsNeeded(_kit, entry.Wcid))
            {
                gear++;
                if (!taken)
                {
                    (missed ??= []).Add(entry.Wcid);
                }
            }
            else if (entry.Wcid == DeathDropModel.CoinWcid)
            {
                coins++; // review R20: a pyreal stack on the corpse is not gear
            }
        }

        _gearLeft = missed?.Count ?? 0;
        (_listingEntries, _listingCoins) = (entries.Length, coins);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: the listing held {entries.Length} entries: gear_listed={gear}{(unresolved == 0 ? string.Empty : $"; {unresolved} never resolved to a weenie class, not classified")}{(coins == 0 ? string.Empty : $"; {coins} of them pyreal stack(s) (wcid {DeathDropModel.CoinWcid}: not gear")}"));
        if (missed is null)
        {
            return;
        }

        var left = string.Create(CultureInfo.InvariantCulture, $"corpse: gear_left={missed.Count}: {missed.Count} item(s) the loops need were on the corpse and were not taken (weenie classes {string.Join(", ", missed)})");
        _log.Warn(
            LogCategory,
            left + "; the death handler took what the corpse gave and spawns nothing in its place (D51), so the item is missing until it is bought or found (report-only in its first gate, review R2)");
    }
}

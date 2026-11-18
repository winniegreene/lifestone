using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 F20 (the owner: never drain a target by a stale health): the fight's one live health model per creature (<see cref="TargetHealthModel"/>), fed by the network traffic. ACE makes the creature of a QueryHealth the player's
/// selected target (Player.cs 393-413 @95ef426d) and from then on pushes UpdateHealth for it on every change (Creature.OnHealthUpdate, Creature.cs 369-378) and once per player heartbeat (Player.HandleTargetVitals,
/// Player_Vitals.cs 164-174); the client stores them (WorldObjectTable.cs 350) and tells the model. So: the fight selects (asks) its current target whenever the target changes; for the other creatures the model keeps the last
/// pushed value reduced by what the mage's own spells took (a drain's source change, a war spell's damage line). Every provider reads it: the picker's snapshot, the opener's under-half rule, F9's war picks, D88's drain floor
/// and the emergency. A drain is chosen only on a fresh health (<see cref="TargetHealthModel.Fresh"/>), and a drain that gave little sets drains on that creature off.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>A push or answer this many seconds before a damage line is taken as the same blow (ACE pushes the new health, then writes the chat line); Lifestone's own.</summary>
    public const double PushSameEventSeconds = 0.3;

    private readonly TargetHealthModel _live = new();
    private IDisposable? _liveHealthSubscription;
    private uint _yieldGuid;
    private string _yieldSpell = string.Empty;
    private double _yieldExpected;
    private double _yieldMissing;
    private double _yieldSince;
    private float _yieldLoss;
    private int _yieldCap;
    private bool _healthWaitNoted;
    private string _drainOffNoted = string.Empty;
    private uint _yieldWcid;
    private string _yieldName = "creature";
    private double _yieldPicked;
    private readonly HashSet<uint> _drainResistNoted = [];

    /// <summary>
    /// M6 14 drain resistance: the health-drain resistance of weenie class <paramref name="wcid"/> (<see cref="DrainResist.Of"/>: the profiles' <c>ResistHealthDrain</c>, or 0 once a drain measured it so), named once a fight for a kind
    /// that is not 1. Allocates only the first time a kind is named.
    /// </summary>
    private double DrainResistOf(uint wcid, string? name)
    {
        var resist = DrainResist.Of(wcid);
        if (resist != 1.0 && _drainResistNoted.Add(wcid))
        {
            var from = DrainResist.IsMeasuredZero(wcid) ? "measured: a landed drain on its kind gave 0" : "its ResistHealthDrain, weenie_properties_float 125, as the ground profiles record it";
            var then = resist <= 0 ? ": no drain and no Harm Other is cast at it, and it is no drain target of the emergency; Heal Self heals" : string.Empty;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: drain resistance: {name ?? "creature"} (wcid {wcid}) lets {resist:0.##} of a drain's take through ({from}){then}"));
        }

        return resist;
    }

    /// <summary>The fight's live health model (a test reads and seeds it).</summary>
    internal TargetHealthModel LiveHealth => _live;

    private void OnLiveHealth(in HealthUpdated e)
    {
        _live.Updated(e.Guid, e.Health, _lastClock);
        _drainedSinceAnswer.Remove(e.Guid); // the server's value holds what the drains took
    }

    /// <summary>The live health of <paramref name="guid"/>: the fraction, where it came from and its age. False when unknown (nothing told, nothing dealt).</summary>
    private bool ReadHealth(WorldObjectTable objects, uint guid, double clock, out float fraction, out HealthSource source, out double age, Threat.HealthReader reader)
    {
        var max = 0;
        if (objects.TryGet(guid, out var o))
        {
            max = TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth;
            if (o.Health is { } held)
            {
                _live.Seed(guid, held); // a value the table held before the fight listened
            }
        }

        age = _live.AgeOf(guid, clock);
        var known = _live.TryGet(guid, max, out fraction, out source);
        ShadowHealth(reader, guid, known, fraction, max, source, age, clock); // M6 16 E2d: the one picture is asked the same beside it (report-only)
        return known;
    }

    /// <summary>Asks the server for <paramref name="guid"/>'s health and so selects it, so that ACE pushes every change of it from now on (QueryHealth, GameAction 0x01BF).</summary>
    private void AskHealth(IGameSessionView session, uint guid, double clock, string why)
    {
        _live.Asked(guid, clock);
        session.Actions.QueryHealth(guid, ActionSource.Host);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: the health of 0x{guid:X8} is asked of the server ({why}); ACE selects it and pushes each change"));
    }

    /// <summary>The fight's target changed: select it, so its health is pushed from the first decision.</summary>
    private void SelectTarget(IGameSessionView session, uint guid, double clock)
    {
        if (_live.Selected != guid && !_live.AskedRecently(guid, clock))
        {
            AskHealth(session, guid, clock, "the new target is selected");
        }
    }

    private bool _assumeNoted;

    /// <summary>
    /// The owner's rule of 2026-11-11 for the ladder's character (bound by name, as <see cref="CharacterPolicy.ManaBySum"/>): a creature is taken as full until she is told otherwise, her own damage is counted off it, and what
    /// the server sends corrects the count: a blow's push for the selected target, and a drain that returns less than the count expected (<see cref="NoteYield"/>). Its health is never asked of the server before a heal or a
    /// drain, and no choice waits for an answer. The den's mage keeps M5's D92 (asked first, an unknown never taken as full) until its own tests are revisited: <c>TODO(m6-assume-health-den)</c>.
    /// </summary>
    private bool AssumesHealth(IGameSessionView session)
    {
        if (!CharacterPolicy.For(session.State.Self.Name).ManaBySum)
        {
            return false;
        }

        if (!_assumeNoted)
        {
            _assumeNoted = true;
            _log.Info(LogCategory, "health: a creature's health is assumed full and tracked by her own damage; it is never asked of the server before a heal or a drain, and a drain's return corrects the count (m6-assume-health)");
        }

        return true;
    }

    /// <summary>
    /// Before a drain is chosen (as an attack, or as the floor's heal) the target's health is fresh: set within <see cref="TargetHealthModel.FreshSeconds"/> by the server or by our own last cast on it. Otherwise it is asked, and the choice
    /// waits for the answer up to <see cref="TargetHealthModel.QueryWaitSeconds"/> (true while it waits); one unanswered goes on with what the model holds (an unknown health allows no drain).
    /// </summary>
    private bool HealthWait(IGameSessionView session, double clock, uint guid)
    {
        if (AssumesHealth(session))
        {
            return false; // the owner, 2026-11-11: no ask and no wait; the model's count decides and a drain's own return corrects it (NoteYield)
        }

        if (_live.Fresh(guid, clock) || _live.DrainOff(guid, out _))
        {
            _healthWaitNoted = false;
            return false;
        }

        if (_live.Waiting(guid, clock))
        {
            return true;
        }

        if (_live.AskedRecently(guid, clock))
        {
            if (!_healthWaitNoted)
            {
                _healthWaitNoted = true;
                _log.Warn(LogCategory, $"health: the server has not answered for 0x{guid:X8} within {TargetHealthModel.QueryWaitSeconds:0.#} s; the choice goes on the model");
            }

            return false;
        }

        AskHealth(session, guid, clock, "a drain would be chosen and its health is not fresh");
        return true;
    }

    /// <summary>
    /// A drain begins: the yield it is measured against is the heal the model's health for the target gives (ACE's formula at the kind's drain resistance, cut to the mage's missing health; <see cref="DrainPicker.HealOf"/>), and the cap
    /// and loss that turn the gain back into what the target lost. M6 14 drain resistance: beside it, the heal the pick read (<c>_yieldPicked</c>): a creature the model was never told of and never hit is taken as full there (the
    /// emergency's <see cref="HealthEstimate"/>, the owner's m6-assume-health), so a 0 against that names the kind (live 2026-11-14 19:42 UTC: "last gave 0 of the 0 the model expected" three times, at a full Armored Tusker each).
    /// </summary>
    private void BeginYield(IGameSessionView session, int levelIndex, uint guid, double clock)
    {
        _yieldGuid = 0;
        var q = session.State.Self.Qualities;
        var levels = _drain.Levels;
        if (levelIndex < 0 || levelIndex >= levels.Length || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) || !session.State.Objects.TryGet(guid, out var target))
        {
            return;
        }

        var wcid = target.Weenie.WeenieClassId;
        var max = TargetStatsOf(wcid).MaxHealth;
        var fraction = 1f;
        var known = max > 0 && ReadHealth(session.State.Objects, guid, clock, out fraction, out _, out _, Threat.HealthReader.FightYield);
        var targetHealth = known ? (uint)Math.Round(max * Math.Clamp(fraction, 0f, 1f)) : 0u;
        var situation = new DrainSituation(health, healthMax, 0, 0, 0, targetHealth, 0, 0, DrainResistOf(wcid, target.Name)); // M6 14 drain resistance: the kind's share, so the yield is judged against what ACE lets through
        var row = levels[levelIndex].Row;
        _yieldExpected = targetHealth > 0 ? DrainPicker.HealOf(in levels[levelIndex], in situation) : 0; // no health to expect from: only the owner's 20 points judge
        _yieldPicked = known ? _yieldExpected : max > 0 ? DrainPicker.HealOf(in levels[levelIndex], situation with { TargetHealth = (uint)max }) : 0; // never told, never hit: full, as the pick took it
        (_yieldGuid, _yieldSpell, _yieldMissing, _yieldSince, _yieldLoss, _yieldCap) = (guid, levels[levelIndex].Info.Name, healthMax > health ? healthMax - health : 0, clock, row.LossPercent, row.TransferCap);
        (_yieldWcid, _yieldName) = (wcid, target.Name ?? "creature");
    }

    /// <summary>
    /// ACE's "You gain N points of health due to casting &lt;spell&gt; on &lt;target&gt;" for the drain in flight: the target lost N / (1 - loss), cut to the row's cap; its live health falls by that, and the yield is
    /// measured. A yield under 20 (or under half of the model's expectation) sets drains on that creature off until a fresher health says otherwise. M6 14 drain resistance (live 2026-11-14 19:42 UTC: three Armored Tuskers gave 0 in
    /// turn, the drain-off being per creature): a yield of 0 where the pick expected at least <see cref="TargetHealthModel.MinDrainYield"/> (<c>_yieldPicked</c>, cut to her missing health, so she had room for it) is the creature
    /// kind's resistance: its kind is taken as resisting wholly for the rest of the process (<see cref="DrainResist.NoteMeasuredZero"/>), logged once by name. True then: the 0 says nothing of the spell's own ratio.
    /// </summary>
    private bool NoteYield(double gain, string spell)
    {
        if (_yieldGuid == 0 || !string.Equals(spell, _yieldSpell, StringComparison.Ordinal))
        {
            return false;
        }

        var guid = _yieldGuid;
        _yieldGuid = 0;
        var resisted = gain <= 0 && _yieldPicked >= TargetHealthModel.MinDrainYield;
        if (resisted && DrainResist.NoteMeasuredZero(_yieldWcid, _yieldName))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: drain resistance measured: {spell} on {_yieldName} 0x{guid:X8} (wcid {_yieldWcid}) gave 0 of the {_yieldPicked:0.#} the pick expected, with {_yieldMissing:0} health missing: its kind is taken to resist drains wholly for the rest of the process (no drain and no Harm Other at it; Heal Self heals; the ground profiles record {DrainResist.RecordedOf(_yieldWcid):0.##}: TODO(m6-drain-resist-profile), record its ResistHealthDrain)"));
        }

        var lost = gain / Math.Max(0.25, 1.0 - _yieldLoss);
        if (_yieldCap > 0)
        {
            lost = Math.Min(lost, _yieldCap);
        }

        var max = _view is { } v && v.State.Objects.TryGet(guid, out var o) ? TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth : 0;
        _live.Took(guid, lost, max, _lastClock, _yieldSince);
        HealthPictureTook(guid, lost); // M6 16 E2d: the one picture is told the drain's take where the model is (report-only)
        if (_live.NoteYield(guid, gain, _yieldExpected, _yieldMissing))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: drain off on 0x{guid:X8}: last gave {gain:0.#} of the {_yieldExpected:0.#} the model expected (under {TargetHealthModel.MinDrainYield:0} or half of it); drains on it stay off until a fresher health says otherwise"));
        }

        return resisted;
    }

    /// <summary>Forgets the creatures that left the object table, one a frame (no allocation).</summary>
    private void PruneLiveHealth(WorldObjectTable objects)
    {
        foreach (var guid in _live.Guids)
        {
            if (!objects.TryGet(guid, out _))
            {
                _live.Forget(guid);
                return;
            }
        }
    }
}

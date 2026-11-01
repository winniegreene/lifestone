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

    /// <summary>The fight's live health model (a test reads and seeds it).</summary>
    internal TargetHealthModel LiveHealth => _live;

    private void OnLiveHealth(in HealthUpdated e)
    {
        _live.Updated(e.Guid, e.Health, _lastClock);
        _drainedSinceAnswer.Remove(e.Guid); // the server's value holds what the drains took
    }

    /// <summary>The live health of <paramref name="guid"/>: the fraction, where it came from and its age. False when unknown (nothing told, nothing dealt).</summary>
    private bool ReadHealth(WorldObjectTable objects, uint guid, double clock, out float fraction, out HealthSource source, out double age)
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
        return _live.TryGet(guid, max, out fraction, out source);
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

    /// <summary>
    /// Before a drain is chosen (as an attack, or as the floor's heal) the target's health is fresh: set within <see cref="TargetHealthModel.FreshSeconds"/> by the server or by our own last cast on it. Otherwise it is asked, and the choice
    /// waits for the answer up to <see cref="TargetHealthModel.QueryWaitSeconds"/> (true while it waits); one unanswered goes on with what the model holds (an unknown health allows no drain).
    /// </summary>
    private bool HealthWait(IGameSessionView session, double clock, uint guid)
    {
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
    /// A drain begins: the yield it is measured against is the heal the model's health for the target gives (ACE's formula, cut to the mage's missing health; <see cref="DrainPicker.HealOf"/>), and the cap and loss that turn the
    /// gain back into what the target lost.
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

        var max = TargetStatsOf(target.Weenie.WeenieClassId).MaxHealth;
        var targetHealth = max > 0 && ReadHealth(session.State.Objects, guid, clock, out var fraction, out _, out _) ? (uint)Math.Round(max * Math.Clamp(fraction, 0f, 1f)) : 0u;
        var situation = new DrainSituation(health, healthMax, 0, 0, 0, targetHealth, 0, 0);
        var row = levels[levelIndex].Row;
        _yieldExpected = targetHealth > 0 ? DrainPicker.HealOf(in levels[levelIndex], in situation) : 0; // no health to expect from: only the owner's 20 points judge
        (_yieldGuid, _yieldSpell, _yieldMissing, _yieldSince, _yieldLoss, _yieldCap) = (guid, levels[levelIndex].Info.Name, healthMax > health ? healthMax - health : 0, clock, row.LossPercent, row.TransferCap);
    }

    /// <summary>
    /// ACE's "You gain N points of health due to casting &lt;spell&gt; on &lt;target&gt;" for the drain in flight: the target lost N / (1 - loss), cut to the row's cap; its live health falls by that, and the yield is
    /// measured. A yield under 20 (or under half of the model's expectation) sets drains on that creature off until a fresher health says otherwise.
    /// </summary>
    private void NoteYield(double gain, string spell)
    {
        if (_yieldGuid == 0 || !string.Equals(spell, _yieldSpell, StringComparison.Ordinal))
        {
            return;
        }

        var guid = _yieldGuid;
        _yieldGuid = 0;
        var lost = gain / Math.Max(0.25, 1.0 - _yieldLoss);
        if (_yieldCap > 0)
        {
            lost = Math.Min(lost, _yieldCap);
        }

        var max = _view is { } v && v.State.Objects.TryGet(guid, out var o) ? TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth : 0;
        _live.Took(guid, lost, max, _lastClock, _yieldSince);
        if (_live.NoteYield(guid, gain, _yieldExpected, _yieldMissing))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: drain off on 0x{guid:X8}: last gave {gain:0.#} of the {_yieldExpected:0.#} the model expected (under {TargetHealthModel.MinDrainYield:0} or half of it); drains on it stay off until a fresher health says otherwise"));
        }
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

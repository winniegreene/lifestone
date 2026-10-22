using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The decoy top-up after the unbuff suicide (M4 3.1 d1 follow-up F2, D20). The new order ends every run buffed (the rebuff), so every next run takes the unbuff suicide first (D33), and the suicide is a death too: ACE drops items by the same rule, so
/// the mage would enter the real death with fewer decoys than the kit has. Stage <c>unbuff-decoys</c>, after <c>unbuff-vitals</c> and before the den stages run again, spawns the decoys the pack now lacks with the mod's <c>item</c> verb (<c>PlayerTools.SpawnGear</c>
/// over a kit of the decoys alone: a decoy is counted by weenie class, so only the missing ones are asked for; nothing is wielded, nothing is cast), waits for them to arrive as the gear stage does, then runs <see cref="DecoyKit.Check"/> again, so
/// <c>decoy_safe=</c> is read after the top-up and <c>decoys=</c> (counted when the wait among the tuskers begins) after it too.
/// </summary>
/// <remarks>
/// <para><b>The suicide's own corpse</b> stays in the den holding the old decoys and is not gone back for: the corpse trip targets the death's own corpse by <see cref="DeathTally"/>'s window (<see cref="DeathTally.LastCorpseGuid"/> is the last corpse of
/// the window of a death, the tuskers'), as before; the stage logs one line saying so. The stage runs only after a suicide (the unbuff stage runs and the mage was buffed), only with <c>--death-decoys on</c>, and only when the kit has decoys; the tuskers'
/// kill during the suicide's countdown (<c>unbuff=killed</c>) is the run's death itself, with the decoys the gear stage spawned.</para>
/// </remarks>
public sealed partial class DeathScenario
{
    private GearKit? _topUpKit;
    private GearSpawn? _topUp;
    private bool _topUpAsked;
    private double _topUpLastAsk;
    private ReasonTimer? _topUpStartWait;
    private ReasonTimer? _topUpDoneWait;
    private int _topUpGrants;

    /// <summary>The decoys the top-up after the unbuff suicide asked the server for (0 when it did not run, or nothing was missing).</summary>
    public int DecoyTopUpGrants => _topUpGrants;

    /// <summary>After the suicide's vitals: the top-up stage, or (decoys off, no decoy in the kit) straight to the den stages.</summary>
    private ScenarioStatus BeginDecoyTopUp(IGameSessionView session, double clock)
    {
        if (!_options.Decoys || !_kit.HasDecoys)
        {
            return Begin(DeathStages.Den, session, clock);
        }

        _topUpKit = GearKit.Of([.. _kit.DecoyItems]);
        _topUpStartWait = new ReasonTimer(GearScenario.SettleSeconds);
        _topUpDoneWait = new ReasonTimer(GearScenario.SpawnBaseSeconds + _topUpKit.Items.Count);
        Enter("unbuff-decoys", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus UnbuffDecoysStep(IGameSessionView session, double clock)
    {
        if (_topUp is null)
        {
            if (_topUpAsked && clock - _topUpLastAsk < GearScenario.SpawnAskSeconds)
            {
                return ScenarioStatus.Running;
            }

            _topUpAsked = true;
            _topUpLastAsk = clock;
            var spawn = session.Tools.SpawnGear(_topUpKit!, ActionSource.Host);
            if (spawn.NotStarted is { } why)
            {
                // Nothing was requested; the inventory cannot be counted yet (a dropped item's removal or a new item's object in flight). Asked again after it settles, as the gear stage does.
                return _topUpStartWait!.Expired(clock, why)
                    ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"unbuff: the decoy top-up could not start within {GearScenario.SettleSeconds:0.#} s: {why}"))
                    : ScenarioStatus.Running;
            }

            _topUp = spawn;
            _topUpDoneWait!.Reset();
            _log.Info(
                LogCategory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"unbuff: the suicide is a death too and dropped items by the same rule: topping up the decoys the pack lacks ({spawn.Grants.Count} missing, {spawn.AlreadyHeld} held); the suicide's own corpse stays in the den holding the old ones and is not gone back for (the trip targets the death's own corpse by the tally's window)"));
            return ScenarioStatus.Running;
        }

        if (!_topUp.IsDone)
        {
            return _topUpDoneWait!.Expired(clock, "the decoy requests")
                ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"unbuff: the decoy top-up was not complete within {GearScenario.SpawnBaseSeconds + _topUpKit!.Items.Count:0.#} s: {_topUp.Describe()}"))
                : ScenarioStatus.Running;
        }

        if (!_topUp.Succeeded)
        {
            return Fail(session, "unbuff: the decoy top-up: " + _topUp.Describe());
        }

        _topUpGrants = _topUp.Grants.Count;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"unbuff: the decoy top-up is done ({_topUpGrants} granted); the decoy check runs again"));
        RunDecoyCheck(session); // decoy_safe= is this check's field: the mage enters the real death with the pack as it is now
        return Begin(DeathStages.Den, session, clock);
    }
}

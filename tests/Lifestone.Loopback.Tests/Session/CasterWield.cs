using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Types;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>What <see cref="CasterWield.Decide"/> says to do this step.</summary>
public enum WieldDecision
{
    /// <summary>A caster is in hand: nothing to wield, the journey goes on.</summary>
    Done,

    /// <summary>No caster in hand, one in the pack, none asked for yet: send the wield.</summary>
    Issue,

    /// <summary>Wait for the pack to arrive or the wield to settle.</summary>
    Wait,

    /// <summary>The limit passed with no caster in hand: a loud failure.</summary>
    Fail,
}

/// <summary>
/// M6 7 B (<c>TODO(m6-quest-loopbacks-wield)</c>, item 5's round 3 and item 6's): the pack's caster wielded into the Held slot before a stand-in journey's first station, shared by the live tests whose stand-in journeys start at the bind with the Wand in the pack
/// (<c>LowRungDeathLiveTests</c>, <c>KillTaskLiveTests</c>, <c>TrophyLiveTests</c>), so the bind's tie (and every later magic mode) finds a caster in hand. <see cref="PortalTieLeg"/>'s way: <c>GearInventory.PackCaster</c>, <see cref="IActionApi.Wield"/>, <see cref="CombatModeGuard.Read"/> before
/// and after (check, act, check). The decision is the pure <see cref="Decide"/>, tested offline (<c>CasterWieldTests</c>); no caster after <see cref="WieldSeconds"/> is a loud failure, never a guess. Call <see cref="Step"/> each tick until it returns null.
/// </summary>
public sealed class CasterWield(ILogSink log, string category)
{
    /// <summary>Seconds the pack's caster is looked for and its wield awaited (PortalTieLeg.ModeSeconds' value, plus the inventory's arrival).</summary>
    public const double WieldSeconds = 20;

    private readonly List<uint> _scratch = [];
    private ActionHandle? _wield;
    private double _from = double.NaN;

    /// <summary>True once a caster is in hand (checked).</summary>
    public bool Done { get; private set; }

    /// <summary>Why the wield failed, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The pure decision: <paramref name="inHand"/> a caster is held; <paramref name="packCaster"/> a caster is in the pack; <paramref name="issued"/> the wield was already sent; <paramref name="elapsed"/> seconds since the first look.</summary>
    public static WieldDecision Decide(bool inHand, bool packCaster, bool issued, double elapsed)
    {
        if (inHand)
        {
            return WieldDecision.Done;
        }

        if (!issued && packCaster)
        {
            return WieldDecision.Issue;
        }

        return elapsed > WieldSeconds ? WieldDecision.Fail : WieldDecision.Wait;
    }

    /// <summary>One step. Null when a caster is in hand (the journey goes on); else the status to return (Running, or Failed with <see cref="Failure"/>). Not to be called before the player is present.</summary>
    public ScenarioStatus? Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Done)
        {
            return null;
        }

        if (double.IsNaN(_from))
        {
            _from = now;
        }

        var held = CombatModeGuard.Read(session.State).Caster;
        var pack = held is null && _wield is null ? GearInventory.PackCaster(session.State, _scratch) : 0u;
        switch (Decide(held is not null, pack != 0, _wield is not null, now - _from))
        {
            case WieldDecision.Done:
                log.Info(category, _wield is null ? $"wield: a caster 0x{held!.Guid:X8} is already in hand; nothing to wield" : $"wield: checked after, the caster 0x{held!.Guid:X8} is in hand ({Describe(_wield)})");
                Done = true;
                return null;
            case WieldDecision.Issue:
                log.Info(category, $"wield: no caster in hand; wielding the pack's caster 0x{pack:X8} into the held slot before the bind's tie (TODO(m6-quest-loopbacks-wield))");
                _wield = session.Actions.Wield(pack, EquipMask.Held, ActionSource.Host);
                return ScenarioStatus.Running;
            case WieldDecision.Fail:
                Failure = _wield is null
                    ? $"wield: no caster (a wand, a staff or an orb) in hand or in the pack within {WieldSeconds:0} s of the player's arrival"
                    : $"wield: the pack's caster was not in hand within {WieldSeconds:0} s ({Describe(_wield)})";
                log.Error(category, Failure);
                return ScenarioStatus.Failed;
            default:
                return ScenarioStatus.Running;
        }
    }

    private static string Describe(ActionHandle handle) =>
        handle.Outcome is { } o ? o.Kind + (o.Code == 0 ? string.Empty : $" (code 0x{o.Code:X})") + (o.Detail is null ? string.Empty : ": " + o.Detail) : "not settled";
}

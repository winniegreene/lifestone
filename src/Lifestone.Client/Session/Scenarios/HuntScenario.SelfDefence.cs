using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 P7u (the owner's decision under D148; D88: always fight back an attacker): the hunt does not end while a creature attacks the player. At the hunt's end (its kills done, from the kill's wait, the next send or the
/// next pick) it looks for an attacker first: a creature heard attacking the player within the last <see cref="DefendHeardSeconds"/> (<see cref="AttackWatch"/>), alive, attackable and within <see cref="DefendReachMetres"/>
/// (<see cref="HostileReach"/>, the harm-heal station's own rule and reach). With one, the hunt takes it as its target, with its own attack and the combat mode of its weapon (query, mode, fight-send: every attack checked
/// before and after as any other), and kills it; then it looks again. With none left, it ends, logged (<c>hunt: fought back N attacker(s) before ending</c>). The hunt's own time limit (<see cref="HuntOptions.Seconds"/>)
/// bounds it, failing by name (<c>time: ... while fighting back ...</c>). Between kills, the next pick takes an attacker in reach before the ground's targets (whatever the ground's table and band say of it).
/// </summary>
/// <remarks>
/// The harm-heal station's step-away (<c>HealStations.cs</c>, <c>TODO(m5-station-defend)</c>) stays the fallback for what the hunt could not fight (a refused or blocked attacker is skipped here, logged). The attack's notices name
/// the attacker, never give its guid (<c>FightScenario.SelfDefence.cs</c>): the nearest living creature of that name stands for it. The hunt does not switch targets inside a fight (minimal: the end check is the ask).
/// </remarks>
public sealed partial class HuntScenario
{
    /// <summary>M5 7 P7u: how recent an attack must be for its creature to hold the hunt's end, in session seconds (Lifestone's own: the gate's wasp and drudge were heard every 1 to 2 s; a few swings' worth).</summary>
    public const double DefendHeardSeconds = 10;

    /// <summary>M5 7 P7u: the reach, centre to centre, inside which an attacker holds the hunt's end (the harm-heal station's <see cref="HostileReach.DefaultMetres"/>: what the hunt leaves standing is what the station would refuse).</summary>
    public const double DefendReachMetres = HostileReach.DefaultMetres;

    private readonly List<(uint Guid, string Name)> _foughtBack = [];
    private AttackWatch? _attackWatch;
    private Func<uint, bool>? _defendSkip;
    private Func<uint, bool>? _notOnly;
    private uint _onlyGuid;
    private bool _defending;
    private uint _defendGuid;
    private int _defendKillsFrom;
    private int _defendedBetween;

    /// <summary>M5 7 P7u: the attackers the hunt fought back at its end before it ended (a test reads it).</summary>
    public int FoughtBack => _foughtBack.Count;

    /// <summary>M5 7 P7u: the attackers the hunt's pick took between kills, before the ground's targets (a test reads it).</summary>
    public int DefendedBetweenKills => _defendedBetween;

    /// <summary>Starts hearing the attacks on the player (once, with the hunt's other subscriptions).</summary>
    private void StartAttackWatch(IGameSessionView session)
    {
        _attackWatch = new AttackWatch(session, line => _log.Info(LogCategory, "self-defence: " + line));
        _attackWatch.Start();
        _subscriptions.Add(_attackWatch);
        _defendSkip = guid => _excluded.Contains(guid) || _blockedHere.Contains(guid);
        _notOnly = guid => guid != _onlyGuid;
    }

    /// <summary>The nearest attacker in reach (heard within <see cref="DefendHeardSeconds"/>, alive, attackable, within <see cref="DefendReachMetres"/>), the hunt's refused and blocked targets skipped.</summary>
    private bool AttackerInReach(IGameSessionView session, out uint guid, out string name, out double metres)
    {
        guid = 0;
        name = string.Empty;
        metres = double.NaN;
        return _attackWatch is { } watch && HostileReach.TryNearest(session.State, DefendReachMetres, watch.HeardWithin(DefendHeardSeconds), out guid, out name, out metres, _defendSkip);
    }

    /// <summary>How long ago <paramref name="name"/> was last heard attacking, for the log.</summary>
    private string HeardAgo(string name) =>
        _attackWatch is { } watch && watch.LastHeard(name, out var at) ? string.Create(CultureInfo.InvariantCulture, $"{_clock - at:0.0} s ago") : "at an unknown time";

    /// <summary>
    /// The send's check with the kills done: true while the target is the attacker being fought back and is still one (alive, heard within <see cref="DefendHeardSeconds"/>, in reach), so the next attack goes at it.
    /// </summary>
    private bool StillFightingBack(IGameSessionView session)
    {
        if (!_defending || _target == 0 || _target != _defendGuid || !TargetAlive(session) || _attackWatch is not { } watch)
        {
            return false;
        }

        _onlyGuid = _target;
        return HostileReach.TryNearest(session.State, DefendReachMetres, watch.HeardWithin(DefendHeardSeconds), out _, out _, out _, _notOnly);
    }

    /// <summary>
    /// The hunt's end (D148, D88): with an attacker in reach the hunt does not end; it takes the attacker as its target (the hunt's own attack and mode) and the end is checked again after it. With none, it ends, logged.
    /// The hunt's time limit bounds the fight-back: past it, with an attacker still in reach, the hunt fails by name.
    /// </summary>
    private ScenarioStatus Finish(IGameSessionView session)
    {
        if (!AttackerInReach(session, out var guid, out var name, out var metres))
        {
            if (_defending)
            {
                var list = string.Join(", ", _foughtBack.Select(a => $"'{a.Name}' 0x{a.Guid:X8}"));
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hunt: fought back {_foughtBack.Count} attacker(s) before ending ({list}); {_tally.Kills - _defendKillsFrom} kill(s) in the fight-back, {_tally.Kills} of {_options.Kills} in all; no creature heard attacking in the last {DefendHeardSeconds:0} s stands within {DefendReachMetres:0} m"));
            }
            else
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hunt: the end check: {_tally.Kills} of {_options.Kills} kill(s) and no creature heard attacking the player in the last {DefendHeardSeconds:0} s stands within {DefendReachMetres:0} m; the hunt ends"));
            }

            return Succeed(session);
        }

        if (!double.IsNaN(_huntStart) && _clock - _huntStart > _options.Seconds)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"time: {_options.Seconds:0.#} s of hunting passed while fighting back '{name}' 0x{guid:X8} ({metres:0.0} m, last heard {HeardAgo(name)}; self-defence, D88) with {_tally.Kills} of {_options.Kills} kill(s) and {_foughtBack.Count} attacker(s) fought back"));
        }

        if (!_defending)
        {
            _defending = true;
            _defendKillsFrom = _tally.Kills;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hunt: the kills are done ({_tally.Kills} of {_options.Kills}) but '{name}' 0x{guid:X8} attacked the player {HeardAgo(name)} and stands {metres:0.0} m away (within {DefendReachMetres:0} m): the hunt does not end; fighting it back (D88)"));
        }
        else
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hunt: the fight-back goes on: '{name}' 0x{guid:X8} attacked the player {HeardAgo(name)} and stands {metres:0.0} m away (attacker {_foughtBack.Count + 1}; D88)"));
        }

        if (!_foughtBack.Exists(a => a.Guid == guid))
        {
            _foughtBack.Add((guid, name));
        }

        _defendGuid = guid;
        return DefendTarget(session, guid);
    }

    /// <summary>
    /// The pick's self-defence between kills: an attacker in reach is the next target before the ground's (the hunt is not in a fight here). Null when there is none.
    /// </summary>
    private ScenarioStatus? DefendBetweenKills(IGameSessionView session)
    {
        if (!AttackerInReach(session, out var guid, out var name, out var metres))
        {
            return null;
        }

        _defendedBetween++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"acquire: '{name}' 0x{guid:X8} attacked the player {HeardAgo(name)} and stands {metres:0.0} m away (within {DefendReachMetres:0} m): fought back before the ground's targets (D88; {_tally.Kills} of {_options.Kills} kill(s))"));
        return DefendTarget(session, guid);
    }

    /// <summary>The attacker becomes the target by the pick's own path: the bow on foot goes to a clear line first (<see cref="SightGoal"/>), else the query, the weapon's mode and the attack follow (<see cref="Chosen"/>).</summary>
    private ScenarioStatus DefendTarget(IGameSessionView session, uint guid) =>
        _options.Weapon == HuntWeapon.Missile && OnFoot ? SightGoal(session, guid) : Chosen(session, guid);
}

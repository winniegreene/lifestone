using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 Q8 (docs/automation-engine.md section 2, "one combat entry"; the one fight-back, part 2): the harm-heal station's pre-check is a host of the skirmish (<see cref="Skirmish"/>). The skirmish fights the one attacker the pre-check
/// found; whatever ends the fight short of its death (the skirmish leaving it, a way out it sets due, a named failure of its own) is the station's refusal, named, and the pre-check's step away follows, as before. The station keeps no
/// way-out reserve and no retreat line (its way out is the step away); its bolt pick judges the pack on the station's account (<see cref="ISkirmishHost.AccountName"/>), as the station's own pick did.
/// </summary>
public sealed partial class HarmHealRun : ISkirmishHost
{
    private Skirmish? _skirmish;
    private LegKindRead? _kinds;
    private AttackWatch? _fightBackWatch;
    private uint _fightBackGuid;
    private string _fightBackName = string.Empty;
    private string? _fightBackFailed;
    private int _fightBackCasts;

    /// <summary>The station's skirmish, made at its first use; the bolts it refused for a component are the station's for its run.</summary>
    private Skirmish Skirmish => _skirmish ??= new Skirmish(this);

    IGameSessionView ISkirmishHost.Session => S;

    JourneyContext ISkirmishHost.Context => Context;

    AttackWatch? ISkirmishHost.Watch => _fightBackWatch;

    LegKindRead ISkirmishHost.Kinds => _kinds ??= new LegKindRead("fight-back", text => Context.Log.Warn("station", "[live harm-heal] " + text));

    string? ISkirmishHost.AccountName => _accountName;

    bool ISkirmishHost.OnTheRun => false;

    bool ISkirmishHost.RetreatIsDue => _fightBackRefusal is not null;

    void ISkirmishHost.Log(string text) => Log(text);

    IEnumerable<ActionHandle?> ISkirmishHost.Until(Func<bool> done, double max) => Until(done, max);

    IEnumerable<ActionHandle?> ISkirmishHost.Cast(uint spell, uint target, TravelLeg.CastResult result) => SkirmishCast(spell, target, result);

    uint ISkirmishHost.RefusalSince(int mark) => ChatRefusalSince(mark);

    IEnumerable<ActionHandle?> ISkirmishHost.MagicNow() => MagicNow();

    IEnumerable<ActionHandle?> ISkirmishHost.PeaceNow() => []; // the fight-back goes to peace after the skirmish, however it ended (FightBack)

    void ISkirmishHost.Fail(string name, string detail)
    {
        // the skirmish's named failure (no-war-spell, no-magic-mode) is the station's refusal: the step away follows
        _fightBackFailed = name[(name.LastIndexOf(':') + 1)..] + ": " + detail;
        throw new InvalidOperationException(name + ": " + detail);
    }

    CellFrame? ISkirmishHost.WhereSheStands() => S.State.Self.Position;

    bool ISkirmishHost.NextAttacker(out uint guid, out string name, out double metres)
    {
        guid = _fightBackGuid != 0 && Alive(_fightBackGuid) ? _fightBackGuid : 0;
        name = guid == 0 ? string.Empty : _fightBackName;
        metres = guid != 0 && S.State.Objects.TryGet(guid, out var o) && S.Live.At(o) is { } there && S.State.Self.Position is { } here ? there.ToGlobal().DistanceTo(here.ToGlobal()) : double.NaN; // where its body is drawn (the one world picture)
        return guid != 0;
    }

    void ISkirmishHost.Leave(uint guid, string name, bool forRange, string why, double reach)
    {
        _fightBackRefusal ??= string.Create(CultureInfo.InvariantCulture, $"{(forRange ? "out-of-range" : "given-up")}: '{name}' 0x{guid:X8}: {why}");
        _fightBackGuid = 0;
    }

    string ISkirmishHost.NewTarget(uint guid, string name) => "M5 D88: always fight back an attacker";

    string ISkirmishHost.LastAttackWord() =>
        _fightBackWatch is { } watch && watch.LastAttack(out var who, out var at) ? string.Create(CultureInfo.InvariantCulture, $"the last attack heard on her: '{who}' {S.Clock.Now - at:0.0} s ago") : "no attack heard on her in the pre-check";

    string ISkirmishHost.EndWhy(bool onTheRun) => _fightBackRefusal is { } refused ? "the attacker is not fought on: " + refused : "the attacker is dead";

    string? ISkirmishHost.RunPastAgain() => null;

    void ISkirmishHost.RetreatDue(string why, string detail) => _fightBackRefusal ??= why + ": " + detail;

    string? ISkirmishHost.HealthUnderLine(out string health)
    {
        health = "health " + HealthText(); // the station keeps no retreat line: its way out is the step away
        return null;
    }

    void ISkirmishHost.Counted(SkirmishTally what, double netted)
    {
        if (what == SkirmishTally.Cast)
        {
            _fightBackCasts++;
        }
        else if (what == SkirmishTally.Kill)
        {
            FightBackKills++;
        }
    }

    string? ISkirmishHost.GivesUp() => null;

    /// <summary>The skirmish at the one attacker <paramref name="guid"/>: a named failure of its own is caught as the station's refusal (the step away follows), never the station's end.</summary>
    private IEnumerable<ActionHandle?> SkirmishFightBack(uint guid, string name)
    {
        (_fightBackGuid, _fightBackName, _fightBackFailed) = (guid, name, null);
        using var run = Skirmish.Run("harm-heal", retreating: false, reserve: null, "none: the station's way out is the step away").GetEnumerator();
        while (true)
        {
            bool more;
            try
            {
                more = run.MoveNext();
            }
            catch (InvalidOperationException) when (_fightBackFailed is not null)
            {
                _fightBackRefusal ??= _fightBackFailed;
                break;
            }

            if (!more)
            {
                break;
            }

            yield return run.Current;
        }

        _fightBackGuid = 0;
    }

    /// <summary>The magic mode for the skirmish (the station's own: asked once, five seconds for it); not reached is the skirmish's check before each cast to name.</summary>
    private IEnumerable<ActionHandle?> MagicNow()
    {
        if (S.State.Self.Qualities.CombatMode == CombatModes.Magic)
        {
            yield break;
        }

        var mode = S.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        foreach (var h in Until(() => mode.IsCompleted && S.State.Self.Qualities.CombatMode == CombatModes.Magic, 5)) { yield return h; }
    }

    /// <summary>The skirmish's cast: a targeted cast as automation (the station's own), the chat mark before it.</summary>
    private IEnumerable<ActionHandle?> SkirmishCast(uint spell, uint target, TravelLeg.CastResult result)
    {
        result.Mark = (int)Math.Min(S.State.Chat.Total, int.MaxValue);
        var cast = S.Actions.Cast(spell, target, ActionSource.Automation);
        result.Handle = cast;
        yield return cast;
    }
}

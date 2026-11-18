using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 Q8 (docs/automation-engine.md section 2, "one combat entry"; the one fight-back, part 2): the quest steps are a host of the skirmish (<see cref="Skirmish"/>, the travel leg's fight-back moved out of it in Q7). The kill of a step
/// and the defence on a quest walk keep their own seek-and-walk; the casting and the staying alive are the skirmish's:
/// <list type="bullet">
/// <item><b>What the step keeps</b> (<see cref="KillBySkirmish"/>): the step's creature found near the go step's point (or the respawn waited for), the mana first (the stamina dance, <see cref="DanceFor"/>), the walk into
/// <see cref="CastRange"/>, the shot check before the creature is handed over and its one walk for a clear line, the victim's last place for the loot, and the kill's own bound (<see cref="KillSeconds"/>, the survival layer's holds
/// excluded).</item>
/// <item><b>What the skirmish decides</b>: the bolt for each target by its element, the reach of each spell (Life Magic beyond the bolt's, else a run past), bolts that come to nothing given way to Harm Other, the way out's reserve
/// (<see cref="TravelLeg.ReserveLineFor"/>: Lifestone Recall and a heal) with the dance under fire, the components refused, the magic mode before each cast, the wait after a bolt ended by its line, and its own bound.</item>
/// <item><b>Its way out on a quest step</b>: the step has no walk back, so a way out the skirmish sets due (no mana, the mana line, no components, its timeout) ends the step at once by name (<c>kill: no mana</c>, <c>kill: mana-line</c>,
/// <c>kill: no components</c>, <c>kill: timeout</c>; on a walk, <c>go: walk-fight: ...</c>, the errand's walk rule); the old loop's wait of up to <see cref="RestSeconds"/> for the mana is gone. Whether she may stand and rest is the
/// survival layer's, above the step.</item>
/// </list>
/// The old loop stays behind <see cref="JourneyFacts.QuestOwnKillLoop"/> (<c>--autopilot-quest-own-kill-loop on</c>) for the skirmish's first live gate on the errands.
/// <c>TODO(engine-quest-skirmish-watch)</c>: the quest steps run no attack watch of their own (a new watch is a new detector, E2's ratchet), so the dance under fire reads no hits coming in (it never judges her losing) and the fight-back's
/// tally is not kept; the threat board's incoming read takes its place when the detectors move onto it.
/// </summary>
public abstract partial class QuestStepLeg : ISkirmishHost
{
    /// <summary>The quest steps' word in the skirmish's lines and in its failure names (<c>fight-back:quest:no-war-spell</c>).</summary>
    public const string SkirmishLeg = "quest";

    private readonly Dictionary<uint, LeftByFight> _left = [];
    private readonly List<uint> _released = [];
    private Skirmish? _skirmish;
    private uint _armed; // the step's creature, handed to the skirmish after the step's walk (0: none, a defence)
    private uint _killWcid; // the step's kind while a kill runs (0: a defence)
    private Victim? _victim;
    private string? _retreatWhy;
    private string _retreatDetail = string.Empty;
    private string _fightTarget = string.Empty;

    /// <summary>A creature the skirmish left: for good, or (<c>ForRange</c>) until it attacks her again from within <c>Reach</c>.</summary>
    private readonly record struct LeftByFight(string Name, string Why, bool ForRange, double At, double Reach);

    /// <summary>The leg's skirmish, made at its first use; what it learns (the bolts refused, the targets whose bolts came to nothing) is the leg's.</summary>
    private Skirmish Skirmish => _skirmish ??= new Skirmish(this);

    IGameSessionView ISkirmishHost.Session => S;

    JourneyContext ISkirmishHost.Context => Context;

    AttackWatch? ISkirmishHost.Watch => null; // TODO(engine-quest-skirmish-watch): see the class

    LegKindRead ISkirmishHost.Kinds => Kinds;

    bool ISkirmishHost.ZeroPointsComeToNothing => true; // M6 14 the quest kill: a bolt of 0 points did not hurt it (NoteHurt's rule), so its bolts give way to Life Magic, then the leaving

    bool ISkirmishHost.OnTheRun => false;

    bool ISkirmishHost.RetreatIsDue => _retreatWhy is not null;

    void ISkirmishHost.Log(string text) => Log(text);

    IEnumerable<ActionHandle?> ISkirmishHost.Until(Func<bool> done, double max) => Until(done, max);

    IEnumerable<ActionHandle?> ISkirmishHost.Cast(uint spell, uint target, TravelLeg.CastResult result) => SkirmishCast(spell, target, result);

    uint ISkirmishHost.RefusalSince(int mark) => ChatRefusalSince(mark);

    IEnumerable<ActionHandle?> ISkirmishHost.MagicNow() => ReadyMagic();

    IEnumerable<ActionHandle?> ISkirmishHost.PeaceNow() => []; // the kill goes to peace after its loot (KillAndLoot) and the defence after its fight (DefendIfAttacked), as before

    void ISkirmishHost.Fail(string name, string detail) => Require(Verify(false, name + ": " + detail), name);

    CellFrame? ISkirmishHost.WhereSheStands() => S.State.Self.Position; // the steps' own reads measure her where the server last put her (Distance)

    bool ISkirmishHost.NextAttacker(out uint guid, out string name, out double metres) => NextToFight(out guid, out name, out metres);

    void ISkirmishHost.Leave(uint guid, string name, bool forRange, string why, double reach)
    {
        _left[guid] = new LeftByFight(name, why, forRange, S.Clock.Now, reach);
        if (guid == _armed)
        {
            _armed = 0;
        }
    }

    string ISkirmishHost.NewTarget(uint guid, string name)
    {
        _fightTarget = name;
        return string.Create(CultureInfo.InvariantCulture, $"health {KnownHealthOf(guid)}; {(StepsCreature(guid) ? "the step's creature" : "an attacker (M5 D88)")}");
    }

    string ISkirmishHost.LastAttackWord() => LastAttackWord();

    string ISkirmishHost.EndWhy(bool onTheRun) => "no attacker left in reach and no step's creature handed over (" + LastAttackWord() + ")";

    string? ISkirmishHost.RunPastAgain() => null;

    void ISkirmishHost.RetreatDue(string why, string detail)
    {
        _retreatWhy = why;
        _retreatDetail = detail;
        Log($"kill: the skirmish's way out is due: why={why}: {detail}");
    }

    string? ISkirmishHost.HealthUnderLine(out string health)
    {
        // the quest steps keep no retreat line of their own, as before: whether she may stand here is the survival layer's, above the step
        health = "health " + (S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var cur, out var max) ? string.Create(CultureInfo.InvariantCulture, $"{cur}/{max}") : "?");
        return null;
    }

    void ISkirmishHost.Counted(SkirmishTally what, double netted)
    {
        switch (what)
        {
            case SkirmishTally.Cast:
                Casts++;
                break;
            case SkirmishTally.Kill:
                Kills++;
                ForgetAttacker(_fightTarget); // M6 8 L: a dead attacker's name leaves the list, so another of that name is not taken for it
                break;
            case SkirmishTally.DanceCast:
                DanceCasts++;
                break;
        }
    }

    string? ISkirmishHost.GivesUp() => null;

    /// <summary>True for the step's creature: the one handed over, or (a kill running) any of the step's kind (an attacker of the step's own kind is the step's creature, as before).</summary>
    private bool StepsCreature(uint guid) => guid != 0 && (guid == _armed || (_killWcid != 0 && WcidOf(guid) == _killWcid));

    /// <summary>The last attack heard on her, in words (the steps' list: each name's last notice within <see cref="ForgetSeconds"/>).</summary>
    private string LastAttackWord()
    {
        var (who, at) = (string.Empty, double.NegativeInfinity);
        foreach (var (name, heard) in _attackers)
        {
            if (heard.When > at)
            {
                (who, at) = (name, heard.When);
            }
        }

        return double.IsNegativeInfinity(at) ? "no attack heard on her in the last " + ForgetSeconds.ToString("0", CultureInfo.InvariantCulture) + " s" : string.Create(CultureInfo.InvariantCulture, $"the last attack heard on her: '{who}' {S.Clock.Now - at:0.0} s ago");
    }

    /// <summary>
    /// The next creature the skirmish fights: an attacker first (M5 D88, the steps' own list: <see cref="LiveAttacker"/>, the ones left, run past and dropped set aside), else the step's creature once the step handed it over. The step's
    /// creature's last place is kept for the loot while it is fought.
    /// </summary>
    private bool NextToFight(out uint guid, out string name, out double metres)
    {
        ReleaseLeftThatAttackAgain();
        guid = LiveAttacker();
        if (guid == 0 && _armed != 0 && Alive(_armed) && !PassedBy(_armed) && !_shots.IsDropped(_armed, S.Clock.Now))
        {
            guid = _armed;
        }

        if (guid == 0)
        {
            (name, metres) = (string.Empty, double.NaN);
            return false;
        }

        name = NameOf(guid);
        metres = Distance(guid);
        if (_victim is { } victim && StepsCreature(guid) && S.State.Objects.TryGet(guid, out var o) && o.Position is { } at)
        {
            (victim.Guid, victim.Name, victim.LastSeen) = (guid, o.Name ?? name, at);
        }

        return true;
    }

    /// <summary>
    /// M6 14 caught on the run, the travel leg's rule: a creature left for its distance whose name is heard attacking her again after it was left, from within the reach it was left with (or from where she cannot read it), is no
    /// longer left; one line each. A creature given up stays left for the leg.
    /// </summary>
    private void ReleaseLeftThatAttackAgain()
    {
        if (_left.Count == 0)
        {
            return;
        }

        _released.Clear();
        foreach (var (guid, left) in _left)
        {
            if (left.ForRange && _attackers.TryGetValue(left.Name, out var heard) && heard.When > left.At && Skirmish.PlanarMetres(guid, double.NaN) is var d && (double.IsNaN(d) || d <= left.Reach))
            {
                _released.Add(guid);
            }
        }

        foreach (var guid in _released)
        {
            var left = _left[guid];
            _left.Remove(guid);
            Log(string.Create(CultureInfo.InvariantCulture, $"kill: '{left.Name}' 0x{guid:X8} was left {S.Clock.Now - left.At:0.0} s ago ({left.Why}) but attacked her again from within {left.Reach:0.#} m: it is no longer left (M6 14 caught on the run)"));
        }
    }

    /// <summary>The skirmish's cast: the steps' own (a targeted cast as automation, a busy answer waits a second), and the shot ledger's rule that a cast the server ended with no bolt counts toward the target's drop (M6 8 L).</summary>
    private IEnumerable<ActionHandle?> SkirmishCast(uint spell, uint target, TravelLeg.CastResult result)
    {
        var mark = S.State.Chat.Total;
        result.Mark = (int)Math.Min(mark, int.MaxValue);
        var cast = S.Actions.Cast(spell, target, ActionSource.Automation); // M6 15: the automation's cast, so the player's rule holds for it
        result.Handle = cast;
        yield return cast;
        if (cast.Outcome is { Code: CastAttempt.BusyCode })
        {
            foreach (var h in Pause(1)) { yield return h; }
        }

        var aimName = NameOf(target);
        if (cast.Outcome is { Kind: ActionOutcomeKind.ServerError, Reason: ActionReason.NoEffect } && Alive(target) && BoltLineSince(mark, aimName) == MagicLineKind.Unparsed)
        {
            _shots.NoteWasted(target, aimName, S.Clock.Now, "the cast ended with no bolt: " + Describe(cast)); // two drop it for the leg; the next pick skips it
        }
    }

    /// <summary>The step's failure for a way out the skirmish set due (see the class).</summary>
    private static string KillFailure(string why) => why switch
    {
        "no-mana" => "kill: no mana",
        "no-components" => "kill: no components",
        "timeout" => "kill: timeout",
        _ => "kill: " + why,
    };

    /// <summary>
    /// The kill of one creature of <paramref name="wcid"/> near the go step's point, or (wcid 0) the defence against the attackers only, with the skirmish casting (see the class). Each turn: an attacker on her is fought first; else
    /// the step's creature is found, the mana got first, walked to and its shot checked, then handed over; the skirmish fights until none is left to fight or its way out is due (the step's failure by name).
    /// </summary>
    private IEnumerable<ActionHandle?> KillBySkirmish(uint wcid, string name, uint cell, Vector3 near, Victim victim)
    {
        foreach (var h in ReadyMagic()) { yield return h; }

        var spell = Skirmish.BoltFor(wcid, out var bolt, out var lack);
        Require(Verify(lack != BoltLack.NoComponents, $"kill: no-components: {bolt}"), "kill: no components");
        Require(Verify(spell is not null, "kill: the spellbook holds no War Magic projectile (TODO(m6-academy-war-spell))"), "kill: no war spell");
        Log($"kill: the war spell is {spell!.Name} ({spell.Id}): {bolt}; the skirmish casts (M6 16 Q8; the bolt is chosen again for each target)");
        var reserve = TravelLeg.ReserveLineFor(S, Context, out var reserveWhy);
        var deadline = S.Clock.Now + KillSeconds;
        var heldAtStart = HeldSeconds; // M6 8 (m6-survival-kill-clock): a hold by the survival layer does not count against the kill's bound
        (_killWcid, _victim, _armed) = (wcid, wcid == 0 ? null : victim, 0);
        try
        {
            var turned = false;
            while (S.Clock.Now - (HeldSeconds - heldAtStart) < deadline)
            {
                if (turned)
                {
                    yield return null; // M6 8 L: every turn gives the frame back
                }

                turned = true;
                var attacker = LiveAttacker();
                var fight = attacker;
                if (attacker == 0)
                {
                    if (wcid == 0)
                    {
                        Log("defend: no living attacker left");
                        yield break;
                    }

                    var target = NearestLiving(wcid, cell, near);
                    if (target == 0)
                    {
                        Log($"kill: no living {name} ({wcid}) within {SearchMetres:0} m of the point; waiting up to {RespawnSeconds:0} s for one");
                        foreach (var h in Until(() => NearestLiving(wcid, cell, near) != 0 || LiveAttacker() != 0, RespawnSeconds)) { yield return h; }

                        if (NearestLiving(wcid, cell, near) == 0 && LiveAttacker() == 0)
                        {
                            Require(Verify(false, $"kill: no living {name} ({wcid}) near the point within {RespawnSeconds:0} s"), "kill: nothing to kill");
                        }

                        continue;
                    }

                    if (S.State.Objects.TryGet(target, out var seen) && seen.Position is { } pos)
                    {
                        (victim.Guid, victim.Name, victim.LastSeen) = (target, seen.Name ?? name, pos);
                    }

                    fight = target;
                }

                // M6 8 L: the mana comes before the walk (and before a defence): the stamina dance pays it; still short, the step ends now by name, no wait (M6 16 Q8)
                if (Skirmish.BoltFor(WcidOf(fight), out _, out _) is { } cost && !ManaFor(cost))
                {
                    Log($"kill: mana {Mana()} is below {cost.Name}'s {cost.BaseMana}: the stamina dance first (target 0x{fight:X8}, {Distance(fight):0.0} m)");
                    foreach (var h in DanceFor(cost)) { yield return h; }

                    Require(Verify(ManaFor(cost), $"kill: mana {Mana()} is still below {cost.Name}'s {cost.BaseMana} after the dance: the step ends now, with no wait for the mana (M6 16 Q8: the skirmish's way out; whether she rests is the survival layer's)"), "kill: no mana");
                }

                if (attacker == 0)
                {
                    // the step's own seek-and-walk: into the cast range, then its shot checked (one walk for a clear line), then it is handed over
                    if (Distance(fight) > CastRange)
                    {
                        foreach (var h in WalkTo(fight, NameOf(fight), CastRange - 3)) { yield return h; }

                        continue;
                    }

                    var aimName = NameOf(fight);
                    var shot = _shots.Before(S.Actions.Shots, S, Skirmish.BoltFor(wcid, out _, out _)?.Id ?? spell.Id, fight, aimName);
                    if (shot.Answer == ShotAnswer.Blocked)
                    {
                        if (_shotWalked.Add(fight))
                        {
                            var closer = Math.Max(Distance(fight) / 2, UseRange);
                            Log(string.Create(CultureInfo.InvariantCulture, $"kill: no shot at {aimName} (0x{fight:X8}) from here: one walk to within {closer:0.0} m for a clear line"));
                            foreach (var h in WalkTo(fight, aimName, closer)) { yield return h; }

                            continue;
                        }

                        _shots.Drop(fight, aimName, S.Clock.Now, "the shot is blocked: " + shot.Why);
                        continue;
                    }

                    _armed = fight;
                    Log($"kill: target {aimName} (0x{fight:X8}, wcid {WcidOf(fight)}) at {Distance(fight):0.0} m, health {KnownHealthOf(fight)}: handed to the skirmish (M6 16 Q8)");
                }

                (_retreatWhy, _retreatDetail) = (null, string.Empty);
                foreach (var h in Skirmish.Run(SkirmishLeg, retreating: false, reserve, reserveWhy)) { yield return h; }

                _armed = 0;
                if (_retreatWhy is { } why)
                {
                    Require(Verify(false, $"kill: the skirmish's way out is due (why={why}): {_retreatDetail}"), KillFailure(why));
                }

                if (wcid != 0 && victim.Guid != 0 && !Alive(victim.Guid))
                {
                    _shots.NoteDamage(victim.Guid);
                    Log($"kill: {victim.Name} (0x{victim.Guid:X8}) is dead after {Casts} cast(s) in all");
                    yield break;
                }
            }

            Require(Verify(false, $"kill: {name} not killed within {KillSeconds:0} s"), "kill: timeout");
        }
        finally
        {
            (_killWcid, _victim, _armed) = (0, null, 0);
        }
    }
}

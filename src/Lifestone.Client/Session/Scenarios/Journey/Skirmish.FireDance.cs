using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 fire dance (<see cref="FireDance"/>; the owner, 2026-11-15: dancing under fire is ordinary retail play; live ladder23 02:51:52 UTC: the Reedshark Ravagers gave her no lull and she recalled on the mana line at 151 of
/// 394): the fight-back dances under fire (M6 16 Q7: moved from the travel leg with the fight-back into the skirmish, unchanged). Check, act, check for each cast:
/// <list type="bullet">
/// <item><b>Check before</b>: the bolt would take the mana under the way out's line (<see cref="ManaWayOut.Spend"/>), and the dance's answer on her live state (<see cref="FireDance.Decide"/>: it covers her, she is not losing, it still
/// pays, the cast keeps the recall paid). A leaving answer is the walk's retreat (<c>why=mana-line</c>), its token and numbers named.</item>
/// <item><b>Act</b>: one cast, Revitalize Self or the press, through the cast policy as automation, in the magic mode.</item>
/// <item><b>Check after</b>: a Revitalize raised her stamina, a press her mana; a round (from its first cast to its press, or to a Revitalize that lands nothing) that netted no mana counts toward <see cref="FireDance.BarrenRounds"/>; the mana
/// it moved is counted for the crossing (<see cref="ISkirmishHost.Counted"/>). Then the fight-back's loop reads the attacker, the lines and the bolt again: she bolts on when the bolt clears the line.</item>
/// </list>
/// The dance's seconds do not count against the fight-back's bound (<see cref="FightBackSeconds"/>, the bound of an attacker the bolts do not kill).
/// </summary>
internal sealed partial class Skirmish
{
    private CastAttempt? _fireCast;
    private CastChatProof? _fireProof;
    private bool _fireAfterRevitalize;
    private bool _fireRoundOpen;
    private uint _fireRoundStart;
    private int _fireBarren;
    private int _fireRounds;
    private double _fireSeconds;
    private string? _fireDecided;

    /// <summary>The casts the dance under fire made for this skirmish's host (report-only).</summary>
    internal int FireDanceCasts { get; private set; }

    /// <summary>A fight-back begins: a round under fire starts again with it (the rounds in a row that netted nothing are the leg's, kept across its fight-backs).</summary>
    private void FireDanceBegin()
    {
        _fireAfterRevitalize = false;
        _fireRoundOpen = false;
        _fireSeconds = 0;
    }

    /// <summary>True when the last seconds' hits would kill her within <see cref="ManaWayOut.LosingSeconds"/> at her health (<see cref="ManaWayOut.IsLosing"/>, on the recent hits' read).</summary>
    private bool FireLosing() =>
        S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && ManaWayOut.IsLosing(health, host.Watch?.Incoming(S.Clock.Now).PerSecond ?? 0);

    /// <summary>The dance's answer under fire now (pure, <see cref="FireDance.Decide"/>), on her live state, the way out's <paramref name="line"/> and the bolt's <paramref name="boltCost"/>.</summary>
    private FireDanceStep FireDanceNow(in WayOutLine line, uint boltCost, out DanceLevel[] presses)
    {
        var round = StaminaDance.Round(S.State, Context.Spells, out var coverWhy);
        presses = StaminaPress.Levels(S.State, Context.Spells);
        var q = S.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _);
        var incoming = host.Watch?.Incoming(S.Clock.Now).PerSecond ?? 0;
        var input = new FireDanceInput(line, boltCost, mana, manaMax, stamina, staminaMax, health, incoming, _fireAfterRevitalize, _fireBarren);
        return FireDance.Decide(in input, round, coverWhy, presses);
    }

    /// <summary>
    /// The way out's leaving at the line (<see cref="ManaWayOut.Decide"/>) with the dance under fire in it: a round that can be sent holds the leaving while she is not losing. The decision's line is logged when its answer changes.
    /// True when she leaves; <paramref name="why"/> then names the dance's answer.
    /// </summary>
    internal bool LeavesAtTheLine(in WayOutLine line, uint mana, out string why)
    {
        var fire = FireDanceNow(in line, BoltMana(out _), out _);
        var input = new WayOutInput(true, mana, line, Attackers: true, KillPending: false, Press: false, Refused: false, Losing: FireLosing(), Dance: fire.Move != FireDanceMove.Leave);
        var d = ManaWayOut.Decide(in input);
        why = $"{fire.Token}: {fire.Why}";
        if (!string.Equals(_fireDecided, d.Why, StringComparison.Ordinal))
        {
            _fireDecided = d.Why;
            Log(ManaWayOut.DecideLine(in input, in d) + (d.Yes ? $": the dance under fire does not hold it ({why})" : $": the dance under fire holds it ({why})"));
        }

        return d.Yes;
    }

    /// <summary>One cast of the dance under fire (the class's list): <paramref name="step"/> at '<paramref name="name"/>' 0x<paramref name="guid"/>'s fight-back, checked after it lands.</summary>
    private IEnumerable<ActionHandle?> FireDanceCast(FireDanceStep step, DanceLevel[] presses, string name, uint guid)
    {
        var c = CultureInfo.InvariantCulture;
        var at = S.Clock.Now;
        foreach (var h in WarBoltPick.MagicBeforeCast(S, TravelLeg.ModeSeconds, Log))
        {
            yield return h;
        }

        var q = S.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        if (!_fireRoundOpen)
        {
            (_fireRoundOpen, _fireRoundStart) = (true, mana); // a round begins: with a Revitalize, or with a press from the stamina held
        }

        var revitalize = step.Move == FireDanceMove.Revitalize;
        var spellId = revitalize ? VitalsMeans.RevitalizeSelf[step.Index] : presses[step.Index].SpellId;
        var spellName = Context.Spells.TryGetValue(spellId, out var info) ? info.Name : spellId.ToString(c);
        _fireCast ??= new CastAttempt(S.Actions, S.State, Context.Log, Context.Spells);
        _fireProof ??= new CastChatProof(S.State, Context.Spells);
        var request = revitalize
            ? new CastRequest { Levels = VitalsMeans.RevitalizeSelf, Index = step.Index, Rules = CastRules.Utility(1), Purpose = "fire dance revitalize", Proof = _fireProof }
            : new CastRequest { Levels = VitalsMeans.StaminaToManaSelf, Index = IndexOf(VitalsMeans.StaminaToManaSelf, spellId), Rules = CastRules.ManaDance(1), Purpose = "fire dance press", Proof = _fireProof };
        _fireCast.Start(request, S.Clock.Now);
        var end = CastEnd.Running;
        while ((end = _fireCast.Step(S.Clock.Now)) == CastEnd.Running)
        {
            yield return null;
        }

        FireDanceCasts++;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var manaAfter, out _);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var staminaAfter, out _);
        host.Counted(SkirmishTally.DanceCast, (double)manaAfter - mana);
        string after;
        if (revitalize && staminaAfter > stamina)
        {
            _fireAfterRevitalize = true;
            after = "the stamina rose: the round's press next";
        }
        else
        {
            // the round ends: at its press, or at a Revitalize that landed nothing
            var landed = !revitalize && manaAfter > mana;
            var net = (double)manaAfter - _fireRoundStart;
            _fireRounds++;
            _fireBarren = net > 0 ? 0 : _fireBarren + 1;
            (_fireAfterRevitalize, _fireRoundOpen) = (false, false);
            after = string.Create(c, $"{(revitalize ? "check after: the stamina did not rise" : landed ? "the mana rose" : "check after: the mana did not rise")}; round {_fireRounds} netted {net:0} (the round's reading {step.Gives:0.#} {(revitalize ? "stamina" : "net")}); {_fireBarren} of {FireDance.BarrenRounds} rounds in a row netted nothing");
        }

        _fireSeconds += S.Clock.Now - at;
        Log(string.Create(c, $"fire dance: {(revitalize ? "revitalize" : "press")}: {spellName} ({end.Name()}) under '{name}' 0x{guid:X8}'s attack: stamina {stamina} -> {staminaAfter} of {staminaMax}, mana {mana} -> {manaAfter} of {manaMax}, health {VitalWord(MageTemplate.MaxHealth)}: {after}"));
    }

    private static int IndexOf(IReadOnlyList<uint> ids, uint id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return i;
            }
        }

        return 0;
    }
}

using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 AN: the mana loop in a fight, for a caster with no Mana Conversion, who pays a press's whole base (<see cref="ManaLoopRule"/>; the numbers are the ladder's <see cref="LadderVitals"/>): mana under the line and stamina at least half: press Stamina to Mana. Mana under the line and stamina under
/// half: Drain Stamina Other from the creature in reach that holds the most, where one cast gives at least the data's least, then press. The target's stamina is read live (ACE's creature profile, sent on a successful appraisal, <c>CreatureProfile.cs:47-66</c> @95ef426d; a creature's Deception is the
/// roll), never the ground's name: in the outskirts a creature holds about 80, the cast gives 16 and the rule passes it over. Each decision has a line; a stamina no appraisal told is a named TODO, and the rule passes over it.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>How long a target's stamina reading stands before it is asked again, in seconds (Lifestone's own: a creature's stamina moves with its swings, and an appraisal is a packet).</summary>
    public const double StaminaAskSeconds = 5;

    private readonly Dictionary<uint, double> _staminaAskAt = [];
    private bool _staminaUnreadNoted;

    // M6 14 (live 2026-11-14, the owner: out of stamina and not draining): an appraisal that fails its roll against the creature's Deception tells no stamina, and with Assess Creature untrained most of a level 80
    // Tusker Slave's do; every unread creature was passed over, so the stamina drain sat idle at 16 of 272 while Slaves holding 600 stood in reach. The kind's stamina as read (the most an appraisal of that weenie class told
    // this fight) stands for an unread creature of the kind, less what her own drains took from that creature since: what the client has read, never a profile's number.
    private readonly Dictionary<uint, uint> _kindStamina = []; // weenie class -> the most stamina an appraisal of the kind told
    private readonly Dictionary<uint, uint> _drainedFrom = []; // creature -> the stamina her drains took from it since its last reading
    private readonly HashSet<uint> _kindAssumedNoted = [];
    private string? _manaLoopNoted;

    private int _manaLoopBoltsOwed;
    private bool _manaLoopHoldsPress;
    private ManaLoopNote? _manaLoopNote;
    private uint? _manaEnd;
    private uint _manaEndMax;

    /// <summary>The fight's presses of the mana loop (<c>loop_presses=</c>, report-only).</summary>
    public int ManaLoopPresses { get; private set; }

    /// <summary>The mana loop's Drain Stamina Other casts sent (<c>loop_drains=</c>, report-only).</summary>
    public int ManaLoopDrains { get; private set; }

    /// <summary>
    /// One decision of the loop, before a bolt: true when it began a cast (the phase is the mana phase's). Check, act, check: the vitals are read now, the target's stamina is the appraisal's, the press's net is read before it is sent, and <see cref="StaminaDrainStep"/> reads what the drain gave.
    /// </summary>
    private bool ManaLoopStepOwn(IGameSessionView session, double clock, uint mana, uint manaMax, uint stamina, uint staminaMax) // M6 16 E3.0: the old ManaLoopStep, unchanged; the name is now its wrapper (FightScenario.SurvivalShadow.cs)
    {
        _manaLoopHoldsPress = false;
        if (CharacterPolicy.For(session.State.Self.Name).ManaBySum)
        {
            return PolicyOn(session) ? ManaLoopByPolicy(session, clock, mana, manaMax, stamina, staminaMax) : ManaLoopBySum(session, clock, mana, manaMax, stamina, staminaMax); // M6 14 (m6-vitals-policy): the climber's fills by the numbers (FightScenario.VitalsPolicy.cs); the ladder's vitals_policy.on off: M6 8 H's rule by the sum; every other character keeps the rule below (D95)
        }

        var rules = LadderLoader.WarMage.Vitals;
        if (_manaConversion != 0 || _options.ArenaHere || mana >= rules.ManaLoopUnder || session.State.Self.Position is not { } here)
        {
            return false;
        }

        var canPress = _danceCount > 0 && clock >= _danceAsideUntil;
        var best = 0u;
        var bestStamina = 0u;
        var give = 0;
        ProfileLine? line = null;
        var index = -1;
        DebuffSpell? row = null;
        var shortOfPress = ManaLoopRule.ShortOfPress(stamina, staminaMax, rules);
        if (shortOfPress && clock >= _staminaDrainAsideUntil)
        {
            line = ArenaProfile.ExtraNamed(StaminaDrainLine);
            if (line is not null && GearCasts.TryCurrentSkill(session.State.Self.Qualities, MageTemplate.LifeMagic, out var life, out _))
            {
                var held = LevelFloor.HeldForAccount(session.State, line.School, _buffOptions.AccountName);
                index = ChooseOpenerLevel(line, _spells, session.State.Self.Spellbook, life, _buffOptions.CastChance, LevelCapFor(session.State.Self.Qualities), held is null ? null : info => LevelFloor.ShortOf(held, info, out var need, _buffOptions.AccountName) ? need : null, out _);
                row = index >= 0 ? Picker.Rows.Find(DebuffKind.DrainStaminaOther, index + 1) : null;
            }

            if (row is { } spell && session.State.Self.Qualities.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
            {
                best = FullestStamina(session, in here, CastRange(_spells[spell.Id], lifeSkill.Init + lifeSkill.Ranks), clock, out bestStamina);
                give = ManaLoopRule.StaminaGive(bestStamina, in spell);
            }
        }

        var move = _options.SurvivalMana ? SurvivalLoopFixed(rules, mana, stamina, staminaMax, give, ref best) : ManaLoopRule.Decide(mana, stamina, staminaMax, rules, give); // M6 16 Q6: the layer decides when the option is on
        switch (move)
        {
            case ManaLoopMove.Press when canPress:
                var levels = _danceLevels.AsSpan(0, _danceCount);
                var level = ManaOrder.BestNetLevel(levels, stamina, mana, _manaConversion, _options.Mana.TryChance, out var net);
                if (level < 0 || net <= 0)
                {
                    NoteManaLoop(string.Create(CultureInfo.InvariantCulture, $"mana loop: no press: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax} land or net nothing (best net {net:0.#})"));
                    return false;
                }

                ManaLoopPresses++;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: press: mana {mana} of {manaMax} under {rules.ManaLoopUnder}, stamina {stamina} of {staminaMax} at least {rules.ManaLoopStaminaPercent}%, net {net:0.#}"));
                _preempting = true;
                _phase = Phase.Mana;
                StartDance(session, level, mana, manaMax, stamina, staminaMax, clock);
                return true;

            case ManaLoopMove.DrainStamina when best != 0 && row is { } drain && mana >= drain.BaseMana:
                ManaLoopDrains++;
                session.State.Objects.TryGet(best, out var target);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: Drain Stamina Other on {target?.Name ?? "creature"} 0x{best:X8}: it holds {bestStamina} stamina, one cast gives {give} (at least {rules.StaminaDrainMinGive}); mana {mana} of {manaMax}, stamina {stamina} of {staminaMax} under {rules.ManaLoopStaminaPercent}%"));
                _phase = Phase.Mana;
                StartStaminaDrain(session, clock, best, line!, index, _spells[drain.Id].Name, stamina, give);
                return true;

            default:
                if (shortOfPress)
                {
                    NoteManaLoop(string.Create(CultureInfo.InvariantCulture, $"mana loop: no drain pays: the best in reach holds {bestStamina} stamina, {give} a cast against {rules.StaminaDrainMinGive}; the order decides"));
                }

                return false;
        }
    }

    /// <summary>
    /// M6 8 H: the climber's mana loop (<see cref="ManaLoopRule.DecideBySum"/>; the sums are <c>cache/m6/8/drain-sums.md</c>): before each bolt, the press by the sum, or the drain from the creature that gives the most in reach and the press after it, regularly and not only at low mana, so her fights end near full where the bars allow it. Check, act, check: the vitals are read now, the target's stamina is the appraisal's and the two are read again after the cast (one line).
    /// M6 14 (the owner, 2026-11-13): the drain is read whenever her stamina is under the stamina line (<c>stamina_keep_percent</c>), her mana full or not, and a drain of that line owes the bolts between as a press does; the press before a bolt
    /// she can pay is the fill (<see cref="ManaLoopRule.FillOption"/>), and while the rule holds it ("the press would overflow") D27's pre-emptive transfer stands aside too.
    /// </summary>
    private bool ManaLoopBySum(IGameSessionView session, double clock, uint mana, uint manaMax, uint stamina, uint staminaMax)
    {
        var rules = LadderLoader.WarMage.Vitals;
        _manaLoopHoldsPress = false;
        if (_options.ArenaHere || session.State.Self.Position is not { } here)
        {
            return false;
        }

        var levels = _danceLevels.AsSpan(0, clock >= _danceAsideUntil ? _danceCount : 0);
        var pressNow = ManaLoopRule.PressOption(levels, stamina, mana, manaMax, _manaConversion, _options.Mana.TryChance);
        var pressFill = ManaLoopRule.FillOption(levels, stamina, mana, manaMax, _manaConversion, _options.Mana.TryChance); // M6 14: the press fills
        var bolt = _triesSpent ? _manaNeeded : _manaFloor;
        if (bolt == 0)
        {
            return false; // no bolt is chosen yet: the bars read its mana
        }

        var best = 0u;
        var bestStamina = 0u;
        var give = 0;
        var index = -1;
        ProfileLine? line = null;
        DebuffSpell? row = null;
        var pressAfter = ManaLoopPress.None;
        var dying = TargetDiesToNextBolt(session, clock, rules);
        if ((ManaLoopRule.UnderStaminaLine(stamina, staminaMax, rules) || ManaLoopRule.ShortOfPress(stamina, staminaMax, rules)) && clock >= _staminaDrainAsideUntil) // M6 14: the drain is read under the stamina line, not only under mana_loop_stamina_percent
        {
            line = ArenaProfile.ExtraNamed(StaminaDrainLine);
            if (line is not null && GearCasts.TryCurrentSkill(session.State.Self.Qualities, MageTemplate.LifeMagic, out var life, out _))
            {
                var held = LevelFloor.HeldForAccount(session.State, line.School, _buffOptions.AccountName);
                index = ChooseOpenerLevel(line, _spells, session.State.Self.Spellbook, life, _buffOptions.CastChance, LevelCapFor(session.State.Self.Qualities), held is null ? null : info => LevelFloor.ShortOf(held, info, out var need, _buffOptions.AccountName) ? need : null, out _);
                row = index >= 0 ? Picker.Rows.Find(DebuffKind.DrainStaminaOther, index + 1) : null;
            }

            if (row is { } spell && session.State.Self.Qualities.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
            {
                best = FullestStamina(session, in here, CastRange(_spells[spell.Id], lifeSkill.Init + lifeSkill.Ranks), clock, out bestStamina, dying ? _target : 0); // M6 14: a target dying to the next bolt is not the drain's: another is drained, or none
                give = best != 0 ? ManaLoopRule.StaminaGive(bestStamina, in spell, staminaMax - stamina) : 0;
                if (give > 0 && mana >= spell.BaseMana)
                {
                    pressAfter = ManaLoopRule.PressOption(levels, stamina + (uint)give, mana - (uint)spell.BaseMana, manaMax, _manaConversion, _options.Mana.TryChance);
                }
            }
        }

        var facts = new ManaLoopFacts
        {
            Mana = mana,
            ManaMax = manaMax,
            Stamina = stamina,
            StaminaMax = staminaMax,
            BoltMana = bolt,
            PressNow = pressNow,
            PressAfterDrain = pressAfter,
            DrainGive = give,
            DrainMana = row is { } priced ? (uint)priced.BaseMana : 0,
            TargetDying = dying,
            Attackers = AttackersNow,
            BoltsOwed = _manaLoopBoltsOwed,
            StaminaKeep = PullRestStaminaLine(session, staminaMax),
            PressFill = pressFill,
            DrainOnBoltTarget = best != 0 && best == _target,
        };
        var plan = _options.SurvivalMana ? SurvivalLoopBySum(in facts, rules, ref best) : ManaLoopRule.DecideBySum(in facts, rules); // M6 16 Q6: the layer decides when the option is on
        switch (plan.Move)
        {
            case ManaLoopMove.Press:
                ManaLoopPresses++;
                _manaLoopBoltsOwed = rules.ManaLoopBoltsBetween;
                _manaLoopNote = new ManaLoopNote(false, 0, null, 0, 0, plan.Net, mana, stamina);
                var pressed = mana >= bolt ? pressFill : pressNow; // the rule's own choice (the loop by the sum's: the fill while the bolt is paid)
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: press by the sum: {plan.Why}: mana {mana} of {manaMax} (missing {manaMax - mana}), stamina {stamina} of {staminaMax}, net {plan.Net:0.#}, give {pressed.Give} (fills at {rules.ManaPressFillPercent}%), bolt {bolt}, level index {plan.Level}"));
                _preempting = true;
                _phase = Phase.Mana;
                StartDance(session, plan.Level, mana, manaMax, stamina, staminaMax, clock);
                return true;

            case ManaLoopMove.DrainStamina when row is { } drain && plan.StaminaLine:
                ManaLoopDrains++;
                _manaLoopBoltsOwed = rules.ManaLoopBoltsBetween; // M6 14: the bolts between follow a drain of the stamina line, as a press (no stall of the kill)
                session.State.Objects.TryGet(best, out var marked);
                _manaLoopNote = new ManaLoopNote(true, best, marked?.Name ?? "creature", bestStamina, give, plan.Net, mana, stamina);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: drain by the stamina line: {_spells[drain.Id].Name} on {marked?.Name ?? "creature"} 0x{best:X8}: it holds {bestStamina} stamina, gives {give} (room {staminaMax - stamina}, at least {rules.StaminaDrainMinGive}); stamina {stamina} of {staminaMax} under {rules.StaminaKeepPercent}%; mana {mana} of {manaMax} pays the drain {drain.BaseMana} and a bolt {bolt}; attackers {AttackersNow} (at most {rules.StaminaDrainMaxAttackers}){(dying ? "; the bolt's target dies to the next bolt and is not this one" : string.Empty)}"));
                _phase = Phase.Mana;
                StartStaminaDrain(session, clock, best, line!, index, _spells[drain.Id].Name, stamina, give);
                return true;

            case ManaLoopMove.DrainStamina when row is { } drain:
                ManaLoopDrains++;
                session.State.Objects.TryGet(best, out var target);
                _manaLoopNote = new ManaLoopNote(true, best, target?.Name ?? "creature", bestStamina, give, plan.Net, mana, stamina);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: drain by the sum: {_spells[drain.Id].Name} on {target?.Name ?? "creature"} 0x{best:X8}: it holds {bestStamina} stamina, gives {give} (room {staminaMax - stamina}); mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}; the drain costs {drain.BaseMana}, the press after it nets {pressAfter.Net:0.#}, the pair {plan.Net:0.#}, bolt {bolt}"));
                _phase = Phase.Mana;
                StartStaminaDrain(session, clock, best, line!, index, _spells[drain.Id].Name, stamina, give);
                return true;

            default:
                _manaLoopHoldsPress = string.Equals(plan.Why, "the press would overflow", StringComparison.Ordinal);
                NoteManaLoop(plan.Why, string.Create(CultureInfo.InvariantCulture, $"mana loop: none: {plan.Why}: mana {mana} of {manaMax} (missing {manaMax - mana}), stamina {stamina} of {staminaMax} (line {rules.StaminaKeepPercent}%), press net {pressNow.Net:0.#}, fill give {pressFill.Give} at {rules.ManaPressFillPercent}%, drain give {give} (target {bestStamina}), bolt {bolt}, attackers {AttackersNow}, owed {_manaLoopBoltsOwed}"));
                return false;
        }
    }

    /// <summary>The target would die to its next bolt (<c>mana_loop_bolt_first_hits</c> mean bolts of the last snapshot's mean hit, from the live health): false while either is unknown.</summary>
    private bool TargetDiesToNextBolt(IGameSessionView session, double clock, LadderVitals rules)
    {
        if (_target == 0 || _snap.BoltMeanDamage <= 0 || !session.State.Objects.TryGet(_target, out var target) || !ReadHealth(session.State.Objects, _target, clock, out var fraction, out _, out _, Threat.HealthReader.FightManaLoop))
        {
            return false;
        }

        var maxHealth = TargetStatsOf(target.Weenie.WeenieClassId).MaxHealth;
        return maxHealth > 0 && fraction * maxHealth <= rules.ManaLoopBoltFirstHits * _snap.BoltMeanDamage;
    }

    /// <summary>One line after a mana-loop cast: what it was read to give, what really moved (the check after the act).</summary>
    private void NoteManaLoopAfter(IGameSessionView session, string what, bool landed)
    {
        if (_manaLoopNote is not { } note)
        {
            return;
        }

        _manaLoopNote = null;
        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _);
        if (note.IsDrain)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: {what} {(landed ? "landed" : "did not land")}: {note.Name} 0x{note.Guid:X8}: it held {note.TargetStamina}, read to give {note.Give}; stamina {note.Stamina} -> {stamina} ({(int)stamina - (int)note.Stamina:+0;-0;0}), mana {note.Mana} -> {mana} ({(int)mana - (int)note.Mana:+0;-0;0})"));
        }
        else
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: {what} {(landed ? "landed" : "did not land")}: read to net {note.Net:0.#}; stamina {note.Stamina} -> {stamina} ({(int)stamina - (int)note.Stamina:+0;-0;0}), mana {note.Mana} -> {mana} ({(int)mana - (int)note.Mana:+0;-0;0})"));
        }
    }

    /// <summary>A bolt was sent: one owed bolt is paid.</summary>
    private void NoteManaLoopBolt()
    {
        if (_manaLoopBoltsOwed > 0)
        {
            _manaLoopBoltsOwed--;
        }
    }

    /// <summary>The fight's mana-loop fields (M6 8 H; report-only): the presses and drains sent, and the mana at the fight's end.</summary>
    private string ManaLoopFields() => string.Create(CultureInfo.InvariantCulture, $"loop_presses={ManaLoopPresses} loop_drains={ManaLoopDrains} mana_end={(_manaEnd is { } m ? m.ToString(CultureInfo.InvariantCulture) : "n/a")} mana_end_pct={(_manaEnd is { } v && _manaEndMax > 0 ? (100 * v / _manaEndMax).ToString(CultureInfo.InvariantCulture) : "n/a")}");

    private readonly record struct ManaLoopNote(bool IsDrain, uint Guid, string? Name, uint TargetStamina, int Give, double Net, uint Mana, uint Stamina);

    /// <summary>The creature in reach, with a clear line, that holds the most stamina by its last appraisal (a stale or missing reading is asked, and read at the next decision); 0 when none is read. <paramref name="exclude"/> (M6 14: the bolt's target dying to the next bolt) is passed over.</summary>
    private uint FullestStamina(IGameSessionView session, in CellFrame here, float range, double clock, out uint stamina, uint exclude = 0)
    {
        stamina = 0;
        var best = 0u;
        var pick = new Threat.FullestPick(Threat.FullestRule.StaminaDrain); // M6 16 E2d: the one comparison, by this pick's rule (more than none, the first met among equals)
        var origin = here.ToGlobal();
        var appraisals = session.State.Appraisals;
        foreach (var o in session.State.Objects.All)
        {
            if ((exclude != 0 && o.Guid == exclude) || !IsEmergencyCandidate(session, o) || StandsAt(session, o) is not { } p || p.ToGlobal().DistanceTo(origin) > range || StaminaDrainSetAside(o.Guid))
            {
                continue;
            }

            if (!_staminaAskAt.TryGetValue(o.Guid, out var asked) || clock - asked >= StaminaAskSeconds)
            {
                _staminaAskAt[o.Guid] = clock;
                session.Actions.Identify(o.Guid, ActionSource.Automation);
                continue;
            }

            var kind = o.Weenie.WeenieClassId;
            uint held;
            if (appraisals.TryGet(o.Guid, out var answer) && answer.CreatureStamina is { } read)
            {
                held = read;
                if (kind != 0 && read > _kindStamina.GetValueOrDefault(kind))
                {
                    _kindStamina[kind] = read; // the kind's stamina as read, for its unread creatures
                }
            }
            else if (kind != 0 && _kindStamina.TryGetValue(kind, out var kindRead))
            {
                var drained = _drainedFrom.GetValueOrDefault(o.Guid);
                held = StaminaForPick(null, kindRead, drained) ?? 0;
                if (_kindAssumedNoted.Add(kind))
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana loop: {o.Name ?? "creature"} 0x{o.Guid:X8}: its appraisal told no stamina (a failed roll against its Deception); assumed {held}: its kind's stamina as read, {kindRead}, less the {drained} her drains took from it (logged once for its kind)"));
                }
            }
            else
            {
                if (!_staminaUnreadNoted && clock - asked >= 1)
                {
                    _staminaUnreadNoted = true;
                    _log.Warn(LogCategory, $"mana loop: TODO(m6-target-stamina-unread): {o.Name ?? "creature"} 0x{o.Guid:X8}: its appraisal told no stamina (a failed roll against its Deception sends none) and no creature of its kind was read yet; passed over");
                }

                continue;
            }

            if (pick.Beats(o.Guid, held) && (_options.IgnoreLine || LineIsClear(session, in here, o.Guid)))
            {
                pick.Take(-1, o.Guid, held);
                (best, stamina) = (o.Guid, held);
            }
        }

        return best;
    }

    /// <summary>
    /// M6 14 (live 2026-11-14): a creature's stamina for the stamina drain's pick: its own reading when an appraisal told one; else its kind's stamina as read this fight (<paramref name="kindRead"/>) less what her drains took
    /// from it since (<paramref name="drained"/>, never under 0); null when neither is known, and the creature is passed over by name.
    /// </summary>
    /// <param name="read">The stamina its last appraisal told, or null (a failed roll against its Deception tells none).</param>
    /// <param name="kindRead">The most stamina an appraisal of its weenie class told this fight, or null.</param>
    /// <param name="drained">The stamina her drains took from it since its last reading.</param>
    internal static uint? StaminaForPick(uint? read, uint? kindRead, uint drained) => read ?? (kindRead is { } k ? (k > drained ? k - drained : 0u) : null);

    private void NoteManaLoop(string text) => NoteManaLoop(text, text);

    private void NoteManaLoop(string key, string text)
    {
        if (!string.Equals(_manaLoopNoted, key, StringComparison.Ordinal))
        {
            _manaLoopNoted = key;
            _log.Info(LogCategory, text);
        }
    }
}

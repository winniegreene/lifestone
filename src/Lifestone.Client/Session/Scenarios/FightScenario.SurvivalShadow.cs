using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 16 E3.0 (docs/automation-engine.md E3 and section 3, "shadow first"; m6-plan.md D119, D139): the survival layer asked inside the fight, report-only. The fight's emergency, its drain rule and heal, its mana loop and
/// its way out decide exactly as before: each keeps its body under the name that ends in <c>Own</c>, and the old name is a wrapper here that calls it and then, beside it, asks the layer (<see cref="Survival"/>) the same
/// question and compares the two answers. Nothing here sends, casts, walks or changes a field the fight decides by.
/// <list type="bullet">
/// <item><c>emergency</c>: <see cref="EmergencyStep"/> (the emergency's heal, its finishing bolt, its refusal for the way out).</item>
/// <item><c>drain</c>: <see cref="DrainDecide"/> and <see cref="HealDecide"/> (D95's drain rule and its Heal Self).</item>
/// <item><c>mana-loop</c>: <see cref="ManaLoopStep"/> (the press, the stamina drain, the policy's fills).</item>
/// <item><c>way-out</c>: <see cref="WayOutHoldsCast"/> (the bolt's gate: the press or the potion in its place, or the refusal by which the fight asks to leave), only while a way out is armed.</item>
/// </list>
/// <para><b>The ask</b> (<see cref="SurvivalAskNow"/>), once a decision of the fight: her bars; who is attacking her from the session's threat board (<see cref="ThreatBoard.Gather"/>: the living creatures of a name heard
/// attacking her within <see cref="HostileWatch.AttackQuietSeconds"/> that stand within <see cref="HostileReach.DefaultMetres"/>, the guard's picture of "on her", the fight's killed set aside), each with its health from the
/// session's one health picture (<see cref="HealthPicture"/>) and its stamina from its appraisal; the fight's own lines; and the means, priced with the shared rules the fight's own deciders use (<see cref="FightHealPick"/>,
/// <see cref="HealPick.Sustained(in HealCandidate)"/>, <see cref="ManaOrder.BestNetLevel"/>, <see cref="BuffStatMods.Transfer"/>) on the LAYER's picks (<see cref="SurvivalRule.FullestHealth"/>,
/// <see cref="SurvivalRule.FullestStamina"/>): a pick whose kind lets no drain through, whose drain yield is set aside or whose line is blocked is left out and the next is asked, as the emergency tries the next best.</para>
/// <para><b>The comparison</b> is each site's own question: at the two heal sites the layer's heal (and on whom) or leave against the fight's; at the mana loop its take of mana (and from whom); at the way out its leave or
/// its take of mana. A difference is one line when it appears or changes and one when it ends, at most one line a site a second:
/// <c>survival-shadow: &lt;site&gt;: the fight &lt;move&gt;, the layer says &lt;verdict&gt; (&lt;why&gt;; the numbers)</c>. At the fight's end one line carries the counts, beside the threat shadow's:
/// <c>survival-shadow: fight: its asks: survival_shadow_agree=&lt;n&gt; survival_shadow_differ=&lt;n&gt; (...)</c>. The verdict line keeps its pinned shape.</para>
/// <para>Allocation-free while the two agree or a difference stands (a line allocates; the Drain Stamina Other level the layer's sum is priced at is read again only when the fight reads its own drain levels,
/// <see cref="FightDrain.Fills"/>, which allocates as that read does).</para>
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The first words of every line of this shadow.</summary>
    public const string SurvivalShadowToken = "survival-shadow: ";

    /// <summary>The least seconds between two lines of one site.</summary>
    public const double SurvivalShadowLineSeconds = 1.0;

    private static readonly string[] SurvivalSiteWords = ["emergency", "drain", "mana-loop", "way-out"];

    private readonly SurvivalLane[] _survivalLanes = new SurvivalLane[SurvivalSiteWords.Length];
    private readonly List<ThreatStander> _survivalStanders = [];
    private readonly List<SurvivalAttacker> _survivalAttackers = [];
    private ThreatBoard? _survivalBoard;
    private Func<uint, bool>? _survivalKilled;
    private bool _survivalTried;
    private bool _survivalLinesSaid;
    private double _survivalAskedAt = double.NaN;
    private bool _survivalHeldAsked;
    private bool _survivalRead;
    private SurvivalDecision _survivalDecision;
    private SurvivalInput _survivalInput;
    private int _survivalDepth;
    private int _survivalHealRefusals;
    private int _survivalStaminaFills = -1;
    private DebuffSpell? _survivalStaminaRow;
    private float _survivalStaminaRange;

    private enum SurvivalSite : byte
    {
        Emergency,
        Drain,
        ManaLoop,
        WayOut,
    }

    private enum SurvivalMove : byte
    {
        GoesOn,
        Finishes,
        HealsSelf,
        HealsDrain,
        HealPending,
        TakesPress,
        TakesStaminaDrain,
        TakesPotion,
        AsksToLeave,
    }

    private struct SurvivalLane
    {
        public long Agree;
        public long Differ;
        public bool Standing;
        public SurvivalMove Move;
        public uint MoveTarget;
        public SurvivalVerdict Verdict;
        public uint LayerTarget;
        public string? Why;
        public double LineAt;
    }

    /// <summary>
    /// The survival layer's ask (<see cref="SurvivalRule.Decide"/> under the ladder's lines), set where the den-fight station is made; null in every other construction: the fight then asks nothing and compares nothing.
    /// Report-only either way: the fight decides by its own deciders.
    /// </summary>
    public SurvivalAsk? Survival { get; set; }

    /// <summary>The asks where the fight's move and the layer's verdict agreed, all sites.</summary>
    public long SurvivalShadowAgree => _survivalLanes[0].Agree + _survivalLanes[1].Agree + _survivalLanes[2].Agree + _survivalLanes[3].Agree;

    /// <summary>The asks where they differed, all sites.</summary>
    public long SurvivalShadowDiffer => _survivalLanes[0].Differ + _survivalLanes[1].Differ + _survivalLanes[2].Differ + _survivalLanes[3].Differ;

    /// <summary>One site's counts (a test reads them): <c>emergency</c>, <c>drain</c>, <c>mana-loop</c>, <c>way-out</c>.</summary>
    internal (long Agree, long Differ) SurvivalShadowOf(string site)
    {
        var i = Array.IndexOf(SurvivalSiteWords, site);
        return i < 0 ? (0, 0) : (_survivalLanes[i].Agree, _survivalLanes[i].Differ);
    }

    /// <summary>The layer's last answer to this fight and whether it was read (a test reads it).</summary>
    internal (bool Read, SurvivalDecision Decision, SurvivalInput Input) SurvivalShadowLast => (_survivalRead, _survivalDecision, _survivalInput);

    /// <summary>One ask and its comparison at the emergency's site with no move of the fight's, for a test that measures what an ask allocates; true when the layer was read.</summary>
    internal bool SurvivalShadowProbe(IGameSessionView session, double clock)
    {
        if (session.State.Self.Position is not { } here)
        {
            return false;
        }

        SurvivalCompare(session, clock, in here, SurvivalSite.Emergency, SurvivalMove.GoesOn, 0);
        return _survivalRead;
    }

    /// <summary>The emergency (<see cref="EmergencyStepOwn"/> decides, as ever); the layer is asked beside it.</summary>
    private bool EmergencyStep(IGameSessionView session, double clock, in CellFrame here)
    {
        if (Survival is null)
        {
            return EmergencyStepOwn(session, clock, in here);
        }

        var refusals = _survivalHealRefusals;
        var began = EmergencyStepOwn(session, clock, in here);
        SurvivalCompare(session, clock, in here, SurvivalSite.Emergency, HealMoveOf(began, refusals, out var target), target);
        return began;
    }

    /// <summary>The drain rule (<see cref="DrainDecideOwn"/> decides, as ever); the layer is asked beside it.</summary>
    private bool DrainDecide(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        if (Survival is null)
        {
            return DrainDecideOwn(session, clock, in here, guid);
        }

        var refusals = _survivalHealRefusals;
        _survivalDepth++; // its Heal Self for a target that lets no drain through is this site's one move, not a second ask
        var began = DrainDecideOwn(session, clock, in here, guid);
        _survivalDepth--;
        SurvivalCompare(session, clock, in here, SurvivalSite.Drain, HealMoveOf(began, refusals, out var target), target);
        return began;
    }

    /// <summary>The drain rule's Heal Self (<see cref="HealDecideOwn"/> decides, as ever); the layer is asked beside it.</summary>
    private bool HealDecide(IGameSessionView session, double clock, string why = "with no target in reach")
    {
        if (Survival is null || _survivalDepth > 0 || session.State.Self.Position is not { } here)
        {
            return HealDecideOwn(session, clock, why);
        }

        var refusals = _survivalHealRefusals;
        var began = HealDecideOwn(session, clock, why);
        SurvivalCompare(session, clock, in here, SurvivalSite.Drain, HealMoveOf(began, refusals, out var target), target);
        return began;
    }

    /// <summary>The mana loop (<see cref="ManaLoopStepOwn"/> decides, as ever); the layer is asked beside it.</summary>
    private bool ManaLoopStep(IGameSessionView session, double clock, uint mana, uint manaMax, uint stamina, uint staminaMax)
    {
        if (Survival is null || session.State.Self.Position is not { } here)
        {
            return ManaLoopStepOwn(session, clock, mana, manaMax, stamina, staminaMax);
        }

        var (presses, drains) = (ManaLoopPresses, ManaLoopDrains);
        var began = ManaLoopStepOwn(session, clock, mana, manaMax, stamina, staminaMax);
        var (move, target) = !began ? (SurvivalMove.GoesOn, 0u)
            : ManaLoopPresses != presses ? (SurvivalMove.TakesPress, 0u)
            : ManaLoopDrains != drains ? (SurvivalMove.TakesStaminaDrain, _staminaDrainGuid)
            : _phase == Phase.Drain ? (SurvivalMove.HealsDrain, _drain.TargetGuid) // the policy's health drain
            : (SurvivalMove.TakesPotion, 0u); // the policy's mana drink
        SurvivalCompare(session, clock, in here, SurvivalSite.ManaLoop, move, target);
        return began;
    }

    /// <summary>The way out's gate of a cast in a bolt's place (<see cref="WayOutHoldsCastOwn"/> decides, as ever); the layer is asked beside it while a way out is armed.</summary>
    private bool WayOutHoldsCast(IGameSessionView session, double clock, uint cost, bool kill, string what)
    {
        if (Survival is null || _wayOut is null || session.State.Self.Position is not { } here)
        {
            return WayOutHoldsCastOwn(session, clock, cost, kill, what);
        }

        var held = WayOutHoldsCastOwn(session, clock, cost, kill, what);
        var move = !held ? SurvivalMove.GoesOn
            : _wayOutRefused ? SurvivalMove.AsksToLeave
            : _manaPhase == ManaPhase.Dancing ? SurvivalMove.TakesPress
            : SurvivalMove.TakesPotion;
        SurvivalCompare(session, clock, in here, SurvivalSite.WayOut, move, 0);
        return held;
    }

    /// <summary>The way out's refusal of a heal (<see cref="WayOutRefusesHealOwn"/> decides, as ever); a refusal is counted so the heal sites read "the fight asks to leave".</summary>
    private bool WayOutRefusesHeal(IGameSessionView session, double clock, uint mana, uint cost, bool nextHitKills)
    {
        var refused = WayOutRefusesHealOwn(session, clock, mana, cost, nextHitKills);
        _survivalHealRefusals += refused ? 1 : 0;
        return refused;
    }

    /// <summary>The emergency found no heal it may pay for the recall (<see cref="WayOutNoHealOwn"/>, as ever); counted as a refusal.</summary>
    private void WayOutNoHeal(uint mana)
    {
        _survivalHealRefusals++;
        WayOutNoHealOwn(mana);
    }

    /// <summary>What a heal site did: a cast under way (her own spell or a drain and on whom), a heal still coming (a health drink, a corpse closed first, its target's health asked first), the finishing bolt, a refusal for the way out, or nothing.</summary>
    private SurvivalMove HealMoveOf(bool began, int refusalsBefore, out uint target)
    {
        target = 0;
        if (began && _phase == Phase.Drain)
        {
            target = _drain.IsHeal ? 0 : _drain.TargetGuid;
            return _drain.IsHeal ? SurvivalMove.HealsSelf : SurvivalMove.HealsDrain;
        }

        if (began)
        {
            return SurvivalMove.HealPending;
        }

        if (_finishGuid != 0)
        {
            target = _finishGuid;
            return SurvivalMove.Finishes;
        }

        return _survivalHealRefusals != refusalsBefore ? SurvivalMove.AsksToLeave : SurvivalMove.GoesOn;
    }

    /// <summary>
    /// The layer's answer for this decision of the fight, asked once a clock value (and again when the fight's heal run began or ended in between): false when her bars or her place are not read, or the board could
    /// not be made (named once). Everything is read; nothing is sent.
    /// </summary>
    private bool SurvivalAskNow(IGameSessionView session, double clock, in CellFrame here, out SurvivalDecision decision)
    {
        var held = _emergency.Active || _drain.Active;
        if (clock == _survivalAskedAt && held == _survivalHeldAsked)
        {
            decision = _survivalDecision;
            return _survivalRead;
        }

        (_survivalAskedAt, _survivalHeldAsked, _survivalRead, decision) = (clock, held, false, default);
        var q = session.State.Self.Qualities;
        if (Survival is not { } ask || SurvivalBoardOf(session) is not { } board
            || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax)
            || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax) || healthMax == 0 || manaMax == 0 || staminaMax == 0 || health == 0)
        {
            return false;
        }

        // Who is attacking her: the board's list, each with the one picture's health and its appraisal's stamina.
        var query = ThreatQuery.OnHer(HostileReach.DefaultMetres, HostileWatch.AttackQuietSeconds) with { Skip = _survivalKilled };
        board.Gather(session, in query, clock, _survivalStanders);
        _survivalAttackers.Clear();
        var picture = board.Health;
        var appraisals = session.State.Appraisals;
        foreach (var s in _survivalStanders)
        {
            var seen = picture.Read(s.Guid, clock);
            var known = seen.Known && seen.Max > 0;
            _survivalAttackers.Add(new SurvivalAttacker(s.Guid, s.Name, s.Metres, known ? (float)(seen.Health / seen.Max) : null, appraisals.TryGet(s.Guid, out var told) ? told.CreatureStamina : null) { Health = known ? seen.Health : null });
        }

        // The heal's means, priced on the layer's pick; only while she is hurt (a full bar asks for no heal).
        var (selfPayable, selfRate, drainTarget, drainPayable, drainRate) = (false, 0.0, 0u, false, 0.0);
        if (health < healthMax && !_options.ArenaHere && q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            var initPlusRanks = lifeSkill.Init + lifeSkill.Ranks;
            var life = GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var current, out _) ? current : initPlusRanks;
            var missing = healthMax - health;
            if (HealSelfCandidate(missing, mana, life, out var self))
            {
                (selfPayable, selfRate) = (true, HealPick.Sustained(in self));
            }

            var levels = _drain.Levels;
            var reach = 0f;
            for (var i = 0; i < levels.Length; i++)
            {
                reach = Math.Max(reach, CastRange(levels[i].Info, initPlusRanks));
            }

            for (var tries = 0; levels.Length > 0 && tries < _survivalAttackers.Count; tries++)
            {
                var pick = SurvivalRule.FullestHealth(_survivalAttackers, reach);
                var at = IndexOfAttacker(pick);
                if (at < 0)
                {
                    break;
                }

                var a = _survivalAttackers[at];
                var wcid = _survivalStanders[at].Wcid;
                var resist = DrainResist.Of(wcid);
                if (resist <= 0 || _live.DrainOff(pick, out _) || !(_options.IgnoreLine || LineIsClear(session, in here, pick)))
                {
                    _survivalAttackers[at] = a with { HealthShare = null }; // left out of the pick: its kind lets no drain through, its yield is set aside, or its line is blocked; the next is asked
                    continue;
                }

                drainTarget = pick;
                var situation = new DrainSituation(health, healthMax, mana, life, initPlusRanks, (uint)Math.Round(a.Health ?? 0), (uint)Math.Max(0, Bestiary.Find(wcid)?.Defence.MagicDefence ?? 0), a.Metres, resist);
                for (var i = 0; i < levels.Length; i++)
                {
                    if (mana < GearCasts.ManaCeiling(levels[i].Info, 0) || a.Metres > CastRange(levels[i].Info, initPlusRanks))
                    {
                        continue;
                    }

                    var candidate = DrainCandidate(in levels[i], in situation, pick, out _);
                    var rate = HealPick.Sustained(in candidate);
                    if (!drainPayable || rate > drainRate)
                    {
                        (drainPayable, drainRate) = (true, rate);
                    }
                }

                break;
            }
        }

        // The mana's means by the sum (D114): the press now; the stamina drain on the layer's pick and the press after it.
        var dances = !_options.ArenaHere && clock >= _danceAsideUntil ? _danceLevels.AsSpan(0, _danceCount) : default;
        var pressNet = dances.Length > 0 && ManaOrder.BestNetLevel(dances, stamina, mana, _manaConversion, _options.Mana.TryChance, out var net) >= 0 ? net : 0;
        var (staminaTarget, pairNet) = (0u, 0.0);
        SurvivalStaminaRead(session);
        if (_survivalStaminaRow is { } row && dances.Length > 0 && clock >= _staminaDrainAsideUntil && mana >= (uint)row.BaseMana)
        {
            for (var tries = 0; tries < _survivalAttackers.Count; tries++)
            {
                var pick = SurvivalRule.FullestStamina(_survivalAttackers, _survivalStaminaRange);
                var at = IndexOfAttacker(pick);
                if (at < 0)
                {
                    break;
                }

                var a = _survivalAttackers[at];
                if (StaminaDrainSetAside(pick) || !(_options.IgnoreLine || LineIsClear(session, in here, pick)))
                {
                    _survivalAttackers[at] = a with { Stamina = null };
                    continue;
                }

                var transfer = new TransferRow(row.Id, BuffStatMods.Stamina, BuffStatMods.Stamina, (float)row.Proportion, (float)row.LossPercent, row.TransferCap);
                BuffStatMods.Transfer(in transfer, a.Stamina ?? 0, staminaMax - stamina, out _, out var give);
                var after = give > 0 && ManaOrder.BestNetLevel(dances, stamina + give, mana - (uint)row.BaseMana, _manaConversion, _options.Mana.TryChance, out var netAfter) >= 0 ? netAfter : 0;
                (staminaTarget, pairNet) = (pick, after - row.BaseMana);
                break;
            }
        }

        var input = new SurvivalInput
        {
            Held = held,
            HealthPercent = health * 100.0 / healthMax,
            ManaPercent = mana * 100.0 / manaMax,
            StaminaPercent = stamina * 100.0 / staminaMax,
            HostileNear = _survivalAttackers.Count > 0,
            Attacked = AttackerOn(session, clock),
            HealPayable = selfPayable || drainPayable,
            MayWait = false, // a fight keeps its own bars (SurvivalRule.KeepsItsOwnBars): the layer's rest is not for it
            MayLeave = _wayOut is not null, // a leave from a fight is the autopilot's retreat by Lifestone Recall, which the armed way out stands for
            InFight = true,
            Attackers = _survivalAttackers,
            Fight = new SurvivalFight
            {
                EmergencyLinePercent = _emergency.Enabled && !_options.ArenaHere ? _emergency.LineHealth * 100.0 / healthMax : 0,
                EmergencySafePercent = _emergency.Enabled && !_options.ArenaHere ? _emergency.SafePercent : 0,
                SelfHealPayable = selfPayable,
                SelfHealPerSecond = selfRate,
                DrainTarget = drainTarget,
                DrainPayable = drainPayable,
                DrainPerSecond = drainRate,
                Mana = mana,
                BoltMana = SurvivalBoltMana(),
                BoltDamage = _snap.BoltMeanDamage,
                PressNet = pressNet,
                StaminaTarget = staminaTarget,
                StaminaPairNet = pairNet,
            },
        };
        (_survivalInput, _survivalDecision, _survivalRead) = (input, decision = ask(in input), true);
        return true;
    }

    /// <summary>
    /// What the next bolt is expected to cost: the mean of ACE's rolled cost at her Mana Conversion (<see cref="ManaConversionModel.ExpectedCost"/>, the fight's own model) for the most it costs
    /// (<c>_manaNeeded</c>, the way out's number for a bolt) and the bolt's power as the picker last read it; that most itself before a pick read the power; 0 before a bolt is chosen. The last answer is kept by its three
    /// inputs, so a decision that changes none of them works nothing out.
    /// </summary>
    private double SurvivalBoltMana()
    {
        var key = (_manaNeeded, _snap.BoltPower, _manaConversion);
        if (key != _survivalBoltKey)
        {
            (_survivalBoltKey, _survivalBoltMana) = (key, key.Item2 == 0 ? key.Item1 : ManaConversionModel.ExpectedCost(key.Item1, key.Item2, key.Item3));
        }

        return _survivalBoltMana;
    }

    private (uint Needed, uint Power, uint Conversion) _survivalBoltKey = (uint.MaxValue, 0, 0);
    private double _survivalBoltMana;

    private int IndexOfAttacker(uint guid)
    {
        for (var i = 0; guid != 0 && i < _survivalAttackers.Count; i++)
        {
            if (_survivalAttackers[i].Guid == guid)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The session's board, made once; a board that cannot be made is named once and the fight asks nothing (it decides as before either way).</summary>
    private ThreatBoard? SurvivalBoardOf(IGameSessionView session)
    {
        if (_survivalTried)
        {
            return _survivalBoard;
        }

        _survivalTried = true;
        try
        {
            _survivalKilled = guid => _killed.Contains(guid);
            _survivalBoard = ThreatBoard.For(session);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _log.Error(LogCategory, $"{SurvivalShadowToken}fight: TODO(survival-shadow-failed): the session's threat board could not be made ({error.GetType().Name}: {error.Message}); the layer is not asked, and the fight decides as ever");
        }

        return _survivalBoard;
    }

    /// <summary>
    /// The Drain Stamina Other level the layer's sum is priced at: the level the fight's own loop would cast (the same choice: the book, the cast chance, the level cap, the pack's components), read when the fight reads
    /// its drain levels (<see cref="FightDrain.Fills"/>) and at the first ask. Allocates: never per ask.
    /// </summary>
    private void SurvivalStaminaRead(IGameSessionView session)
    {
        if (_survivalStaminaFills != _drain.Fills)
        {
            SurvivalStaminaReadNow(session); // apart: its lambda's closure is made at that method's entry, never on the path of an ask that reads nothing
        }
    }

    private void SurvivalStaminaReadNow(IGameSessionView session)
    {
        (_survivalStaminaFills, _survivalStaminaRow, _survivalStaminaRange) = (_drain.Fills, null, 0);
        var q = session.State.Self.Qualities;
        if (ArenaProfile.ExtraNamed(StaminaDrainLine) is not { } line || !GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out _) || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return;
        }

        var held = LevelFloor.HeldForAccount(session.State, line.School, _buffOptions.AccountName);
        var index = ChooseOpenerLevel(line, _spells, session.State.Self.Spellbook, life, _buffOptions.CastChance, LevelCapFor(q), held is null ? null : info => LevelFloor.ShortOf(held, info, out var need, _buffOptions.AccountName) ? need : null, out _);
        if (index >= 0 && Picker.Rows.Find(DebuffKind.DrainStaminaOther, index + 1) is { } row && _spells.TryGetValue(row.Id, out var spell))
        {
            (_survivalStaminaRow, _survivalStaminaRange) = (row, CastRange(spell, lifeSkill.Init + lifeSkill.Ranks));
        }
    }

    /// <summary>True when the fight's move and the layer's verdict are one answer to the site's question.</summary>
    private static bool SurvivalAgrees(SurvivalSite site, SurvivalMove move, uint target, in SurvivalDecision layer)
    {
        var verdict = layer.Verdict;
        if (move == SurvivalMove.HealsDrain || move == SurvivalMove.HealsSelf)
        {
            return verdict == SurvivalVerdict.Heal && layer.DrainTarget == target; // a heal wherever the fight made it (the policy's health drain is the mana loop's)
        }

        switch (site)
        {
            case SurvivalSite.Emergency or SurvivalSite.Drain:
                return verdict switch
                {
                    SurvivalVerdict.Heal => move == SurvivalMove.HealPending,
                    SurvivalVerdict.Leave => move == SurvivalMove.AsksToLeave,
                    _ => move == SurvivalMove.GoesOn, // the mana is the mana loop's question, asked at its own site
                };
            case SurvivalSite.ManaLoop:
                return verdict == SurvivalVerdict.TakeMana
                    ? (move == SurvivalMove.TakesStaminaDrain ? layer.DrainTarget == target : (move is SurvivalMove.TakesPress or SurvivalMove.TakesPotion) && layer.DrainTarget == 0)
                    : move == SurvivalMove.GoesOn; // a heal or a leave is the other sites' question
            default:
                return verdict switch
                {
                    SurvivalVerdict.Leave => move == SurvivalMove.AsksToLeave,
                    SurvivalVerdict.TakeMana => move is SurvivalMove.TakesPress or SurvivalMove.TakesPotion,
                    _ => move == SurvivalMove.GoesOn,
                };
        }
    }

    /// <summary>
    /// One comparison at <paramref name="site"/> (<see cref="SurvivalCompareNow"/>). The shadow never costs the fight its step: anything it throws is named once, the ask is taken away (<see cref="Survival"/> is
    /// null from then on, so every wrapper passes straight through) and the fight goes on as it does with no ask.
    /// </summary>
    private void SurvivalCompare(IGameSessionView session, double clock, in CellFrame here, SurvivalSite site, SurvivalMove move, uint target)
    {
        try
        {
            SurvivalCompareNow(session, clock, in here, site, move, target);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Survival = null;
            _survivalRead = false;
            _log.Error(LogCategory, $"{SurvivalShadowToken}fight: TODO(survival-shadow-failed): the ask of the survival layer threw at the {SurvivalSiteWords[(int)site]} site ({error.GetType().Name}: {error.Message}); the layer is asked no more in this fight, and the fight decides as ever");
        }
    }

    /// <summary>One comparison at <paramref name="site"/>: counted, and a difference written when it appears or changes and when it ends, at most one line a second. Allocates only for a line.</summary>
    private void SurvivalCompareNow(IGameSessionView session, double clock, in CellFrame here, SurvivalSite site, SurvivalMove move, uint target)
    {
        if (!SurvivalAskNow(session, clock, in here, out var layer))
        {
            return;
        }

        if (!_survivalLinesSaid)
        {
            _survivalLinesSaid = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{SurvivalShadowToken}fight: the survival layer is asked beside the fight's emergency, its drain rule and heal, its mana loop and its way out, and decides nothing (report-only): the fight's lines: emergency floor {_emergency.FloorPercent}%, safe {_emergency.SafePercent}%, margin {_emergency.MarginPercent}%; drain under {_drain.UnderPercent}%, until {_drain.UntilPercent}%; way out {(_wayOut is { } armed ? ManaWayOut.Named(in armed) : "not armed")}"));
        }

        ref var lane = ref _survivalLanes[(int)site];
        var agree = SurvivalAgrees(site, move, target, in layer);
        if (agree)
        {
            lane.Agree++;
        }
        else
        {
            lane.Differ++;
        }

        var same = lane.Standing == !agree && (agree || (lane.Move == move && lane.MoveTarget == target && lane.Verdict == layer.Verdict && lane.LayerTarget == layer.DrainTarget && ReferenceEquals(lane.Why, layer.Why)));
        if (same || clock - lane.LineAt < SurvivalShadowLineSeconds)
        {
            return; // nothing new to say, or said within the second: a change that still stands is written when the second has passed
        }

        (lane.Standing, lane.Move, lane.MoveTarget, lane.Verdict, lane.LayerTarget, lane.Why, lane.LineAt) = (!agree, move, target, layer.Verdict, layer.DrainTarget, layer.Why, clock);
        var i = _survivalInput;
        var f = i.Fight;
        var numbers = string.Create(CultureInfo.InvariantCulture, $"health {i.HealthPercent:0}%, mana {i.ManaPercent:0}% ({f.Mana}), stamina {i.StaminaPercent:0}%, {_survivalAttackers.Count} attacker(s) read, the fight's emergency line {f.EmergencyLinePercent:0}% (safe {f.EmergencySafePercent:0}%), a heal run {(i.Held ? "stands" : "does not stand")}; her own heal {(f.SelfHealPayable ? f.SelfHealPerSecond.ToString("0.#", CultureInfo.InvariantCulture) + " a second" : "not payable")}, a drain on the layer's pick {(f.DrainTarget == 0 ? "none" : SurvivalName(session, f.DrainTarget))} {(f.DrainPayable ? f.DrainPerSecond.ToString("0.#", CultureInfo.InvariantCulture) + " a second" : "not payable")}; bolt {f.BoltMana:0.#} mana for {f.BoltDamage:0.#}, the mana that finishes them {(SurvivalRule.ManaToFinish(_survivalAttackers, f.BoltDamage, f.BoltMana) is var need && !double.IsNaN(need) ? need.ToString("0", CultureInfo.InvariantCulture) : "not read")}, the press nets {f.PressNet:0.#}, the stamina drain and its press {(f.StaminaTarget == 0 ? "none" : f.StaminaPairNet.ToString("0.#", CultureInfo.InvariantCulture) + " on " + SurvivalName(session, f.StaminaTarget))}");
        _log.Info(LogCategory, agree
            ? $"{SurvivalShadowToken}{SurvivalSiteWords[(int)site]}: the two agree again: the fight {SurvivalMoveWords(session, move, target)}, the layer says {SurvivalVerdictWords(session, in layer)} ({layer.Why}; {numbers})"
            : $"{SurvivalShadowToken}{SurvivalSiteWords[(int)site]}: the fight {SurvivalMoveWords(session, move, target)}, the layer says {SurvivalVerdictWords(session, in layer)} ({layer.Why}; {numbers})");
    }

    private static string SurvivalName(IGameSessionView session, uint guid) =>
        string.Create(CultureInfo.InvariantCulture, $"{(session.State.Objects.TryGet(guid, out var o) ? o.Name ?? "creature" : "creature")} 0x{guid:X8}");

    private string SurvivalMoveWords(IGameSessionView session, SurvivalMove move, uint target) => move switch
    {
        SurvivalMove.Finishes => "goes on (the bolt that kills the only attacker, " + SurvivalName(session, target) + ", comes before a heal)",
        SurvivalMove.HealsSelf => "heals with " + _drain.HealName,
        SurvivalMove.HealsDrain => "drains " + SurvivalName(session, target) + " with " + _drain.LastName,
        SurvivalMove.HealPending => "heals (a health drink, a corpse closed first, or its target's health asked first)",
        SurvivalMove.TakesPress => "takes mana by a Stamina to Mana press",
        SurvivalMove.TakesStaminaDrain => "takes mana by Drain Stamina Other on " + SurvivalName(session, target),
        SurvivalMove.TakesPotion => "takes mana by a mana drink",
        SurvivalMove.AsksToLeave => "asks to leave (its spend is refused for the way out)",
        _ => "goes on",
    };

    private static string SurvivalVerdictWords(IGameSessionView session, in SurvivalDecision layer) => layer.Verdict switch
    {
        SurvivalVerdict.Heal => layer.DrainTarget == 0 ? "heal with her own spell" : "heal by a drain on " + SurvivalName(session, layer.DrainTarget),
        SurvivalVerdict.TakeMana => layer.DrainTarget == 0 ? "take mana by the press" : "take mana by a stamina drain on " + SurvivalName(session, layer.DrainTarget),
        SurvivalVerdict.Leave => "leave",
        SurvivalVerdict.Wait => "wait",
        _ => "go",
    };

    /// <summary>What the fight's own heal deciders hold now, in words (the autopilot's line of the vitals guard over a quest's sub-fight reads it). Allocates: asked once a sub-fight.</summary>
    public string HealDecidersWords() => string.Create(
        CultureInfo.InvariantCulture,
        $"its emergency line is {_emergency.LineHealth:0} health (floor {_emergency.FloorPercent}%, safe {_emergency.SafePercent}%; a run {(_emergency.Active ? "stands" : "does not stand")}, {_emergency.Landed} landed), its drain rule is under {_drain.UnderPercent}% until {_drain.UntilPercent}% (a run {(_drain.Active ? "stands" : "does not stand")}, {_drain.Drains} drain(s) and {_drain.Heals} heal(s) landed), {(_drain.Cast is { IsRunning: true } ? "its own " + _drain.LastName + " is in flight" + (_drain.IsHeal ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" at 0x{_drain.TargetGuid:X8}")) : "no heal of its own is in flight")}, its way out is {(_wayOut is { } line ? ManaWayOut.Named(in line) : "not armed")}");

    /// <summary>The fight ends: its line with its asks of the survival layer, when it asked at all.</summary>
    private void EndSurvivalShadow()
    {
        if (SurvivalShadowAgree + SurvivalShadowDiffer == 0)
        {
            return; // never asked (no ask was set, or it never read)
        }

        var sites = new System.Text.StringBuilder();
        for (var i = 0; i < SurvivalSiteWords.Length; i++)
        {
            sites.Append(i == 0 ? string.Empty : ", ").Append(CultureInfo.InvariantCulture, $"{SurvivalSiteWords[i]} {_survivalLanes[i].Agree}/{_survivalLanes[i].Differ}");
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{SurvivalShadowToken}fight: its asks: survival_shadow_agree={SurvivalShadowAgree} survival_shadow_differ={SurvivalShadowDiffer} (agreed/differed by site: {sites})"));
    }
}

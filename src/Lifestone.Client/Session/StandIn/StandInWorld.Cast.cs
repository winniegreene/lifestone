// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Magic.cs (CreatePlayerSpell, DoSpellWords, DoCastSpell_Inner's fizzle branch, FinishCast),
// WorldObject_Magic.cs (DoSpellEffects, HandleCastSpell_Boost, HandleCastSpell_Transfer), Creature_Vitals.cs and Player_Vitals.cs (UpdateVital) and
// WorldObject_Networking.cs (EnqueueMotionMagic) (AGPL-3.0) [with changes: the stand-in's player is the caster and the target of a self spell; the timing comes from
// CastGesturePlanner; the resistance modifiers are 1; the boost and transfer numbers are the ACE world database's spell rows (the table below, read-only SELECT results)].
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// Gives the stand-in's cast ACE's sequence (M3 2.1 s2a; <c>TODO(m3-standin-cast-gestures)</c> closed): <see cref="CastGesturePlanner"/> builds the plan from the spell's formula, the
/// SpellComponentTable and the player's motion table, and <see cref="StandInVisit.CastPlanner"/> hands it to the stand-in. Thread-safe: a host may plan the spells it is about to cast on a worker
/// (<see cref="Plan"/>), so the first cast does not decode animations on the game thread.
/// </summary>
public sealed class StandInCastPlanner
{
    /// <summary>The account name the stand-in's formulas are customised for (the stand-in's own account; it only chooses the taper colours, never a gesture or a word).</summary>
    public const string AccountName = "stand-in";

    /// <summary>The highest power of a spell the stand-in's character casts without a fizzle (its fixed magic skill plus ACE's margin of 50, <c>Player_Magic.cs</c> 534): a host plans only spells up to it ahead of time.</summary>
    public const uint CastablePower = StandInWorld.PlaceholderMagicSkill + 50;

    private readonly SpellComponentTableData _components;
    private readonly MotionTableData _table;
    private readonly IAnimationSource _animations;
    private readonly Dictionary<uint, CastPlan> _plans = [];
    private readonly object _gate = new();

    private StandInCastPlanner(SpellComponentTableData components, MotionTableData table, IAnimationSource animations)
    {
        _components = components;
        _table = table;
        _animations = animations;
    }

    /// <summary>A planner over the dats: the SpellComponentTable and the stand-in player's motion table (<see cref="StandInVisit.HumanMotionTable"/>, the body the stand-in creates).</summary>
    public static StandInCastPlanner FromDats(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var animations = new DatAnimationSource(dats);
        var table = animations.GetMotionTable(StandInVisit.HumanMotionTable)
            ?? throw new InvalidDataException($"MotionTable 0x{StandInVisit.HumanMotionTable:X8} (the stand-in player's) is not in the dats.");
        return new StandInCastPlanner(SpellComponentTableData.Load(dats), table, animations);
    }

    /// <summary>A planner over parts the caller built (tests).</summary>
    public static StandInCastPlanner Create(SpellComponentTableData components, MotionTableData table, IAnimationSource animations) =>
        new(components ?? throw new ArgumentNullException(nameof(components)), table ?? throw new ArgumentNullException(nameof(table)), animations ?? throw new ArgumentNullException(nameof(animations)));

    /// <summary>The plan of <paramref name="spell"/> (kept: the second cast of a spell plans nothing).</summary>
    public CastPlan Plan(SpellInfo spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        lock (_gate)
        {
            if (!_plans.TryGetValue(spell.Id, out var plan))
            {
                plan = CastGesturePlanner.Plan(spell, _components, _table, _animations, AccountName);
                _plans[spell.Id] = plan;
            }

            return plan;
        }
    }
}

/// <summary>
/// The stand-in's cast, as ACE plays it for a player who is in the magic combat mode and knows the spell (<c>Player_Magic.cs</c> <c>CreatePlayerSpell</c> 1000-1058): the words (<c>DoSpellWords</c> 589-598, a HearSpeech 0x02BB from the
/// player, chat type Spellcasting), one UpdateMotion 0xF74C per scarab of the base formula and then the talisman's cast gesture (<c>DoWindupGestures</c> 600-640, <c>DoCastGesture</c> 642-676, <c>EnqueueMotionMagic</c>
/// in <c>WorldObject_Networking.cs</c> 1078-1092: stance Magic, speed 2.0, turn speed 2.25), then the spell (<c>DoCastSpell_Inner</c>: the effect, then the caster's and the target's PlayEffect 0xF755 of
/// <c>DoSpellEffects</c>, <c>WorldObject_Magic.cs</c> 351-368) and <c>FinishCast</c> (the return to the Magic-stance Ready at once, UseDone 0x01C7 a second later, 940-990). A fizzle comes at the effect's
/// moment, after the words and the gestures (the pre-check status is read at the cast's end): PlayEffect Fizzle at 0.5 on the caster and WeenieError 0x028A 0x0402 (the <c>default</c> branch of the switch,
/// ~917). Every time is <see cref="CastPlan"/>'s, which is <see cref="CastGesturePlanner"/>'s, which reads the motion table. Without a planner (no dats) the cast is what it was: the effect and UseDone a second after
/// it, logged once as <c>TODO(m3-standin-cast-plan)</c>.
/// </summary>
internal sealed partial class StandInWorld
{
    /// <summary>ACE's <c>ChatMessageType.Spellcasting</c> (0x11), the type of the spell words.</summary>
    internal const uint SpellcastingChat = 0x11;

    /// <summary>ACE's <c>ChatMessageType.Magic</c> (0x07), the type of the lines a boost or a transfer answers with.</summary>
    internal const int MagicChat = 7;

    /// <summary>The turn speed ACE puts on a cast motion (<c>EnqueueMotionMagic</c>: "// ??").</summary>
    internal const float CastTurnSpeed = 2.25f;

    /// <summary>The intensity of the Fizzle script (<c>Player_Magic.cs</c> ~917: <c>PlayScript.Fizzle, 0.5f</c>).</summary>
    internal const float FizzleIntensity = 0.5f;

    /// <summary>The seed of the stand-in's generator for a boost's roll (ACE rolls with <c>ThreadSafeRandom</c>; the stand-in's is seeded, so a run repeats).</summary>
    internal const int VitalSeed = 0x5EED_0B05;

    private readonly Random _vitalRandom = new(VitalSeed);

    /// <summary>The words, motions and effects the cast sequence sent in total (tests and the play checks' lines).</summary>
    internal int CastMotionsSent { get; private set; }

    private CastPlan? PlanFor(SpellInfo info)
    {
        if (_visit.CastPlanner is not { } planner)
        {
            Placeholder("m3-standin-cast-plan", "the stand-in has no cast planner (no dats were given to its visit): a cast is its effect and ACE's UseDone, with no words, gestures or scripts");
            return null;
        }

        try
        {
            return planner(info);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidDataException)
        {
            Log?.Error(SessionLog.Category, "TODO(m3-standin-cast-plan): the cast of spell " + info.Id + " could not be planned, so it plays without gestures: " + e.Message);
            return null;
        }
    }

    /// <summary>Schedules the whole sequence of <paramref name="plan"/> now (the closures are made here, once; what runs each frame is the schedule's own list).</summary>
    private void PlayCast(IAcSessionSink sink, uint spellId, SpellInfo? info, uint target, CastPlan plan)
    {
        var self = _reducer.State.Self;
        var start = _reducer.Clock.Now;
        var schedule = _reducer.StandIn!;
        if (!string.IsNullOrWhiteSpace(plan.Words))
        {
            AnswerLog.Add("02BB");
            sink.OnSpeech(Context(sink), new SpeechMsg(plan.Words, self.Name ?? "You", self.Guid, SpellcastingChat, null));
        }

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            switch (step.Kind)
            {
                case CastStepKind.Windup:
                case CastStepKind.CastGesture:
                    schedule.At(start + step.StartTime, later => SendCastMotion(later, step.Motion, step.Speed, CastTurnSpeed));
                    break;
                case CastStepKind.SpellEffects:
                    schedule.At(start + step.StartTime, later => ResolveCast(later, spellId, info, target, plan));
                    break;
                default:
                    schedule.At(start + step.StartTime, later => SendCastMotion(later, MotionCommands.Ready, 1f, null)); // FinishCast: back to the Magic-stance Ready (Player_Magic.cs 975)
                    break;
            }
        }

        schedule.At(start + plan.EndTime, later => UseDone(later)); // the recoil (Player_Magic.cs 977-990)
    }

    /// <summary>One cast motion: ACE's <c>new Motion(MotionStance.Magic, motion, speed)</c> as an UpdateMotion of the player (the sequences continue the player's own).</summary>
    private void SendCastMotion(IAcSessionSink sink, uint motion, float speed, float? turnSpeed)
    {
        var guid = _reducer.State.Self.Guid;
        var stamps = _reducer.State.Objects.TryGet(guid, out var body) ? body.Stamps : default;
        var stance = MotionCommandTable.ToWire(MotionCommands.Magic);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = stance,
            Style = stance,
            Forward = MotionCommandTable.ToWire(motion),
            ForwardSpeed = speed != 1f ? speed : null,
            TurnSpeed = turnSpeed,
        };
        CastMotionsSent++;
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    private void PlayEffect(IAcSessionSink sink, uint guid, uint script, float intensity)
    {
        AnswerLog.Add("F755");
        sink.OnPlayEffect(Context(sink), new PlayEffectMsg(guid, script, intensity));
    }

    /// <summary>What happens when the cast's gesture ends: the fizzle, or the effect and the caster's and the target's scripts.</summary>
    private void ResolveCast(IAcSessionSink sink, uint spellId, SpellInfo? info, uint target, CastPlan? plan)
    {
        var self = _reducer.State.Self;
        if (info is not null && info.Power > PlaceholderMagicSkill + 50)
        {
            // ACE: the pre-check fails when the skill is below the power minus 50, and the cast fizzles (Player_Magic.cs 528-540, 917-918); the stand-in has no dice.
            Fizzles++;
            Placeholder("m3-standin-magic-skill", "the stand-in's character casts with a fixed magic skill of " + PlaceholderMagicSkill + " and never rolls: a spell of power above " + (PlaceholderMagicSkill + 50) + " fizzles");
            if (plan is not null)
            {
                PlayEffect(sink, self.Guid, (uint)PlayScript.Fizzle, FizzleIntensity);
            }

            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(FizzleError, null));
            return;
        }

        if (info is not null && IsArmourEnchantmentAtCaster(info, target, self.Guid))
        {
            // ACE answers it with a chat line per worn piece and no enchantment of the player's own (M3 2.1 s2b, D23). The cast tracker's Classify says SelfEnchantment for it until its own rule changes (s0b), so it is not asked here.
            CastOnWornClothing(sink, info);
            if (plan is not null)
            {
                CastEffects(sink, info, plan, target, projectile: false);
            }

            return;
        }

        var expect = info is null ? CastTracker.Expectation.SelfEnchantment : CastTracker.Classify(info, target, self.Guid);
        switch (expect)
        {
            case CastTracker.Expectation.SelfEnchantment:
                AddEnchantment(sink, spellId, info);
                break;
            case CastTracker.Expectation.Projectile:
                CreateProjectile(sink, info!, target);
                break;
            default:
                // Any other spell (a buff on another target, a portal) has no effect the stand-in can show: ACE's UseDone ends it, as the tracker expects (TODO(m3-cast-other-target-proof)).
                if (info is not null)
                {
                    CastVitalSpell(sink, info, target);
                }

                break;
        }

        if (plan is not null && info is not null)
        {
            CastEffects(sink, info, plan, target, expect == CastTracker.Expectation.Projectile);
        }
    }

    /// <summary>The <c>ItemType.Vestements</c> a spell's <c>NonComponentTargetType</c> names for the armour lines (Armor 0x2 | Clothing 0x4, ACE <c>ItemType.cs</c>).</summary>
    internal const uint VestementsTargetType = 0x6;

    /// <summary>
    /// An Item Enchantment whose <c>NonComponentTargetType</c> is Vestements (Impenetrability, Bludgeon Bane and the other armour lines), cast at the caster (no target, or the player): ACE's <c>Player.VerifyNonComponentTargetType</c>
    /// accepts a creature for Vestements (<c>Player_Magic.cs</c> 1391-1393) and <c>TryCastItemEnchantment_WithRedirects</c> (<c>WorldObject_Magic.cs</c> 2067-2121) puts the enchantment on every worn clothing piece and shield. Decided by the
    /// spell's school, its meta type, its flags (a SelfTargeted spell is the creature kind) and its target type; the cast tracker's <c>Classify</c> is not consulted.
    /// </summary>
    internal static bool IsArmourEnchantmentAtCaster(SpellInfo info, uint target, uint self) =>
        info.School == MagicSchool.ItemEnchantment
        && info.MetaSpellType == SpellType.Enchantment
        && info.NonComponentTargetType == VestementsTargetType
        && (info.Flags & SpellFlags.SelfTargeted) == 0
        && (target == 0 || target == self);

    /// <summary>The equipment locations ACE counts as clothing or a shield for the Vestements redirect: every Wear and Armor slot, and the shield (<c>EquipMask</c>: 0x1 to 0x4000 and 0x200000).</summary>
    internal const uint ClothingAndShieldMask = 0x00007FFF | 0x00200000;

    /// <summary>
    /// ACE's answer to an armour enchantment at the caster: for each equipped clothing piece and shield one Magic chat line, "You cast &lt;spell&gt; on &lt;item name&gt;" (<c>WorldObject_Magic.cs</c> 430-452), and nothing else: no 0x02C2 for
    /// the player (the enchantments are on the items, which the stand-in has no registry for: <c>TODO(m3-standin-item-enchantments)</c>), and the cast ends with UseDone 0 as every cast does.
    /// </summary>
    private void CastOnWornClothing(IAcSessionSink sink, SpellInfo info)
    {
        Placeholder("m3-standin-item-enchantments", "an armour enchantment is answered with ACE's chat line per worn piece and no more: the stand-in keeps no enchantment registry for items");
        var state = _reducer.State;
        var equipped = state.Self.Inventory.Equipped;
        for (var i = 0; i < equipped.Count; i++)
        {
            var piece = equipped[i];
            if ((piece.Location & ClothingAndShieldMask) == 0)
            {
                continue;
            }

            MagicLine(sink, "You cast " + info.Name + " on " + (state.Objects.TryGet(piece.Guid, out var item) ? item.Name : "an item"));
        }
    }

    /// <summary>ACE's <c>DoSpellEffects</c> (<c>WorldObject_Magic.cs</c> 351-368): the caster's effect, then the target's (a projectile's plays on its hit, which the stand-in has no model for: <c>TODO(m3-standin-projectile-model)</c>).</summary>
    private void CastEffects(IAcSessionSink sink, SpellInfo info, CastPlan plan, uint target, bool projectile)
    {
        var self = _reducer.State.Self.Guid;
        if (info.CasterEffect != PlayScript.Invalid)
        {
            PlayEffect(sink, self, (uint)info.CasterEffect, plan.EffectIntensity);
        }

        if (info.TargetEffect != PlayScript.Invalid && !projectile)
        {
            var at = target == 0 || (info.Flags & SpellFlags.SelfTargeted) != 0 ? self : target;
            if (at == self || _reducer.State.Objects.Contains(at))
            {
                PlayEffect(sink, at, (uint)info.TargetEffect, plan.EffectIntensity);
            }
        }
    }

    // --- a self Boost and a self Transfer (WorldObject_Magic.cs HandleCastSpell_Boost 471-560, HandleCastSpell_Transfer 694-840) ---------------------------------------------------------------------------------------

    private const uint Health = 2;
    private const uint Stamina = 4;
    private const uint Mana = 6;

    /// <summary>A self Boost row of ACE's spell table: the vital (by its current's id), the base boost and its variance (the maximum is the base plus the variance, <c>Spell.MaxBoost</c>).</summary>
    private readonly record struct VitalBoost(uint Vital, int Boost, int Variance);

    /// <summary>A self Transfer row: the source and the destination vital, the proportion, the loss and the cap (0: none).</summary>
    private readonly record struct VitalTransfer(uint Source, uint Destination, float Proportion, float Loss, int Cap);

    /// <summary>
    /// The self Boost spells the stand-in knows the numbers of: the ACE world database's <c>spell</c> rows (<c>boost</c>, <c>boost_Variance</c>, the vital from the spell's line) for Revitalize Self I to VI,
    /// Robustification, Incantation of Revitalize Self and Mana Boost Self VI. A Heal's rows were not selected (<c>TODO(m3-standin-vital-spell-table)</c>: a heal, like any Boost or Transfer not here, has no effect).
    /// </summary>
    private static readonly Dictionary<uint, VitalBoost> SelfBoosts = new()
    {
        [1177] = new(Stamina, 15, 20),
        [1178] = new(Stamina, 20, 25),
        [1179] = new(Stamina, 30, 30),
        [1180] = new(Stamina, 40, 40),
        [1181] = new(Stamina, 60, 60),
        [1182] = new(Stamina, 80, 80),
        [1206] = new(Mana, 51, 49),
        [2083] = new(Stamina, 100, 100),
        [4321] = new(Stamina, 125, 125),
    };

    private static readonly float[] TierLoss = [0.1f, 0f, -0.1f, -0.2f, -0.35f, -0.5f];
    private static readonly int[] TierCap = [50, 100, 150, 200, 0, 0];

    /// <summary>
    /// The self Transfer spells (the <c>spell</c> rows' proportion, <c>loss_Percent</c> and <c>transfer_Cap</c>; the proportion is 0.5 on every one): the six tiers of each "X to Y Self" line, and the Incantation of each.
    /// The Stamina to Mana Self rows (1676-1681, VII 2345 "Meditative Trance", VIII 4981) are not kept here: they are <c>BuffStatMods.StaminaToMana</c>, the one table the buff cycle predicts with (M3 2.1 fix A, 2.1-R3).
    /// The level VII spells of the other five lines (Self Sacrifice, Rushed Recovery and their kin) are still left out: the selection did not carry the transfer bitfield that says whether they are self lines
    /// (<c>TODO(m3-standin-vital-spell-table)</c> narrowed to those, to the Heal rows and to the other Boost lines).
    /// </summary>
    private static readonly Dictionary<uint, VitalTransfer> SelfTransfers = BuildTransfers();

    private static Dictionary<uint, VitalTransfer> BuildTransfers()
    {
        var rows = new Dictionary<uint, VitalTransfer>();
        void Line(uint source, uint destination, uint[] ids)
        {
            for (var tier = 0; tier < ids.Length; tier++)
            {
                rows[ids[tier]] = new VitalTransfer(source, destination, 0.5f, TierLoss[tier], TierCap[tier]);
            }
        }

        Line(Health, Stamina, [1272, 1273, 1274, 1275, 1276, 1277]);
        Line(Health, Mana, [1278, 1279, 1280, 1702, 1703, 1704]);
        Line(Mana, Health, [1290, 1291, 1292, 1293, 1294, 1295]);
        Line(Mana, Stamina, [1296, 1297, 1298, 1299, 1300, 1301]);
        Line(Stamina, Health, [1664, 1665, 1666, 1667, 1668, 1669]);

        // Stamina to Mana Self (all eight levels, Meditative Trance 2345 included) is the buff cycle's own table, not a second copy.
        foreach (var row in BuffStatMods.StaminaToMana)
        {
            rows[row.Id] = new VitalTransfer(row.Source, row.Destination, row.Proportion, row.LossPercent, row.TransferCap);
        }

        rows[4647] = new VitalTransfer(Health, Mana, 0.5f, -1f, 0);
        rows[4649] = new VitalTransfer(Health, Stamina, 0.5f, -1f, 0);
        rows[4654] = new VitalTransfer(Mana, Health, 0.5f, -1f, 0);
        rows[4656] = new VitalTransfer(Mana, Stamina, 0.5f, -1f, 0);
        rows[4658] = new VitalTransfer(Stamina, Health, 0.5f, -1f, 0);
        return rows;
    }

    private static string VitalName(uint vital) => vital switch { Health => "health", Stamina => "stamina", _ => "mana" };

    private void CastVitalSpell(IAcSessionSink sink, SpellInfo info, uint target)
    {
        var onSelf = target == 0 || target == _reducer.State.Self.Guid || (info.Flags & SpellFlags.SelfTargeted) != 0;
        if (onSelf && info.MetaSpellType == SpellType.Boost && SelfBoosts.TryGetValue(info.Id, out var boost))
        {
            Boost(sink, info, boost);
        }
        else if (onSelf && info.MetaSpellType == SpellType.Transfer && SelfTransfers.TryGetValue(info.Id, out var transfer))
        {
            Transfer(sink, info, transfer);
        }
        else if (info.MetaSpellType is SpellType.Boost or SpellType.Transfer or SpellType.FellowBoost)
        {
            Placeholder("m3-standin-vital-spell-table", "the stand-in knows the numbers of a few self Boost and Transfer spells (ACE's world database, not the dat); spell " + info.Id + " (" + info.Name + ") changes no vital");
        }
    }

    /// <summary>ACE's <c>UpdateVital</c> (<c>Creature_Vitals.cs</c> 50-56, <c>Player_Vitals.cs</c> 130-140): the vital is set clamped to 0 and its maximum; a change sends PrivateUpdateAttribute2ndLevel 0x02E9 with the current value.</summary>
    private int UpdateVitalDelta(IAcSessionSink sink, uint vital, int delta)
    {
        if (!_reducer.State.Self.Qualities.TryGetVitalLevels(vital - 1, out var current, out var max))
        {
            Placeholder("m3-standin-vital-spell-table", "the stand-in's character has no vital " + vital + " in its description, so a boost or transfer cannot change it");
            return 0;
        }

        var next = (uint)Math.Clamp((int)current + delta, 0, (int)max);
        var change = (int)next - (int)current;
        if (change != 0)
        {
            AnswerLog.Add("02E9");
            PrivateUpdate(sink, PrivateUpdateKind.VitalCurrent, vital, next);
        }

        return change;
    }

    private void Boost(IAcSessionSink sink, SpellInfo info, VitalBoost boost)
    {
        // ACE: a roll between the smaller and the larger of the base and the maximum, inclusive (ThreadSafeRandom.Next); the player's boost-resistance modifier is 1 (no such enchantments are modelled).
        var min = Math.Min(boost.Boost, boost.Boost + boost.Variance);
        var max = Math.Max(boost.Boost, boost.Boost + boost.Variance);
        var change = UpdateVitalDelta(sink, boost.Vital, _vitalRandom.Next(min, max + 1));
        var verb = (info.Flags & SpellFlags.Beneficial) != 0 || change >= 0 ? "restore" : "drain";
        MagicLine(sink, "You cast " + info.Name + " and " + verb + " " + Math.Abs(change) + " points of your " + VitalName(boost.Vital) + ".");
    }

    private void Transfer(IAcSessionSink sink, SpellInfo info, VitalTransfer row)
    {
        var q = _reducer.State.Self.Qualities;
        if (!q.TryGetVitalLevels(row.Source - 1, out var sourceNow, out _) || !q.TryGetVitalLevels(row.Destination - 1, out var destinationNow, out var destinationMax))
        {
            Placeholder("m3-standin-vital-spell-table", "the stand-in's character has no vital " + row.Source + " or " + row.Destination + " in its description, so a transfer cannot change them");
            return;
        }

        // ACE: the source's share, capped; the destination gets it less the loss, limited to what the destination misses (and the cap), the source's part scaled to match.
        var sourceChange = (uint)Math.Round(sourceNow * row.Proportion);
        if (row.Cap != 0 && sourceChange > row.Cap)
        {
            sourceChange = (uint)row.Cap;
        }

        var destinationChange = (uint)Math.Round(sourceChange * (1.0f - row.Loss));
        var missing = destinationMax > destinationNow ? destinationMax - destinationNow : 0u;
        var room = row.Cap != 0 && missing > row.Cap ? (uint)row.Cap : missing;
        if (destinationChange > room)
        {
            var scalar = (float)room / destinationChange;
            sourceChange = (uint)Math.Round(sourceChange * scalar);
            destinationChange = room;
        }

        var lost = (uint)-UpdateVitalDelta(sink, row.Source, -(int)sourceChange);
        var gained = (uint)UpdateVitalDelta(sink, row.Destination, (int)destinationChange);
        MagicLine(sink, "You cast " + info.Name + " on yourself and lose " + lost + " points of " + VitalName(row.Source) + " and also gain " + gained + " points of " + VitalName(row.Destination));
    }

    private void MagicLine(IAcSessionSink sink, string text)
    {
        AnswerLog.Add("F7E0");
        sink.OnServerMessage(Context(sink), new ServerMessageMsg(text, MagicChat));
    }
}

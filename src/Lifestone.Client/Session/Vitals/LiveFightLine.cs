using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Vitals;

/// <summary>A creature attacking her or in reach, as the line from the attackers present reads it (M6 14 retreat attackers).</summary>
/// <param name="Guid">Its guid.</param>
/// <param name="Wcid">Its weenie class id (its facts are looked up by it, <see cref="CreatureFacts"/>).</param>
/// <param name="Name">Its name.</param>
/// <param name="Metres">Its distance, centre to centre.</param>
public readonly record struct PresentAttacker(uint Guid, uint Wcid, string Name, double Metres);

/// <summary>
/// M6 14 holding safety (the owner, 2026-11-13: every number about how she fights is computed at run time from her live state and the creatures' facts): the derived fight line (<see cref="FightLines.Derive"/>) read on her live state, one
/// reader a run or a leg. The autopilot's retreat line and health floor and the travel legs' retreat ask it; a <see cref="DerivedLine.Derived"/> line wins over the profile's hand-sized <c>fight.retreat_health_percent</c> (the reader's caller
/// logs that the profile's value is ignored); a place whose targets carry no attack facts is not derived and the caller's own line stands, named.
/// <list type="bullet">
/// <item><b>The place:</b> its hardest hitter (<see cref="HuntingGround.HardestHitter"/>: a target's <c>d_val</c>, <c>d_var</c> and Strength, each with its source) and the pack the ladder's survey names for it (<c>health.pack</c> of the rung
/// whose ground it is, else 1, <c>TODO(m6-pack-past-the-cave)</c>).</item>
/// <item><b>Her armour:</b> the worn pieces' appraisals (<see cref="ArmorReport.Read(IReadOnlyList{WornPiece}, uint, out int)"/>, each piece's protection against the hit's own damage type; a piece not appraised is left out, the safe side, and counted),
/// the body armour of her registry (<see cref="FightLines.BodyArmour"/>), and the Impenetrability and that type's bane the session's <see cref="ArmourBuffLedger"/> holds on every worn piece (the lowest across them). Read once each
/// <see cref="ArmourReadSeconds"/> a type, and at once when her registry changes.</item>
/// <item><b>Her resistance:</b> her registry's multipliers against the hit's own damage type (<see cref="FightLines.ResistanceMod"/>), read again whenever the registry changes: a landed Tusker's Gift (ResistBludgeon x2.85, 240 s) raises the line
/// at the next read and its removal lowers it (<c>TODO(m6-holding-gift-live)</c>, answered).</item>
/// <item><b>The fight's measure:</b> the damage a second and the biggest recent hit the caller's <see cref="RecentHits"/> reads.</item>
/// </list>
/// M6 14 retreat attackers (live 2026-11-14 10:59:18 UTC, the climber's death on the Holding's approach: the run leg's line was the Holding's Rampager, bludgeon, under the bot's bludgeon protection, 10%, while two Infected Assailers, acid,
/// were on her): <see cref="ReadAttackers"/> derives the line from the creatures actually attacking or in reach, each by its own facts looked up by weenie class across every profile (<see cref="CreatureFacts"/>), its own damage type, her armour
/// and her live resistance to that type; a creature with no facts gives the general fallback (<see cref="FightLines.GeneralFallbackPercent"/>), named; the measured hits are its floor. <see cref="FactorFor"/> is the same read for one damage
/// type, the approach check's (<see cref="ApproachCheck"/>).
/// </summary>
public sealed class LiveFightLine
{
    /// <summary>Seconds between two reads of the worn pieces and the item lines (Lifestone's own: they change with an equip, an appraisal or a cast, never inside a second; the resistance and the health are read on every call).</summary>
    public const double ArmourReadSeconds = 1;

    /// <summary>The armour line names of the session's ledger (<see cref="ArmourBuffLedger"/>; the buff profile's names).</summary>
    private const string ImpenetrabilityLine = "Impenetrability";

    private const string BludgeonBaneLine = "Bludgeon Bane";

    private static readonly uint[] TypeBits = [0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40];

    private HuntingGround? _ground;
    private LadderHitter? _hitter;
    private uint _types;
    private int _pack = 1;
    private ImmutableArray<EnchantmentDesc> _enchantments;
    private double _body;
    private readonly double[] _armourAt = [.. TypeBits.Select(_ => double.NegativeInfinity)];
    private readonly ArmourRead[] _armour = [.. TypeBits.Select(_ => new ArmourRead(double.NaN, 0, 0, 0, 0, null))];
    private readonly Func<uint, double> _spellResistance;

    /// <summary>A reader with no timings until its owner gives them (<see cref="Times"/>).</summary>
    public LiveFightLine() => _spellResistance = bits => Resistance(_enchantments, bits);

    /// <summary>
    /// M6 14 attack timing: the creatures' attack timings the lines read (<see cref="CreatureAttackTimes.For"/> of the session's dats, set by the reader's owner); <see cref="CreatureAttackTimes.None"/> until then, each creature's line then
    /// the named 2.75 s fallback.
    /// </summary>
    public CreatureAttackTimes Times { get; set; } = CreatureAttackTimes.None;

    /// <summary>M6 14 attack timing: her resistance against a spell of the DamageType bits given (her registry's, as last read; a spell takes no armour).</summary>
    public Func<uint, double> SpellResistance => _spellResistance;

    /// <summary>The line now on <paramref name="ground"/> for the character of <paramref name="state"/>.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="ground">The ground (its targets' facts, its profile line).</param>
    /// <param name="now">The session clock.</param>
    /// <param name="incoming">The fight's measure of the hits (default: none measured).</param>
    public DerivedLine Read(GameState state, HuntingGround ground, double now, in IncomingRead incoming)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        if (!ReferenceEquals(ground, _ground))
        {
            _ground = ground;
            _hitter = ground.HardestHitter(out _types);
            _pack = PackOf(ground);
        }

        var q = state.Self.Qualities;
        var healthMax = q.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max) ? max : 0u;
        if (_hitter is null)
        {
            return FightLines.Derive(new FightLineInput(ground.Key, healthMax, null, _pack, double.NaN, 1, 0, 0, ground.FightRetreatHealthPercent));
        }

        Registry(q.Enchantments);
        var type = Worst(state, now, _types, out var armour, out var resistance);
        var timing = Times.Of(_hitter.Wcid, _hitter.Name);
        var input = new FightLineInput(ground.Key, healthMax, _hitter, _pack, armour.Factor, resistance, incoming.PerSecond, incoming.BiggestHit, ground.FightRetreatHealthPercent)
        {
            Timing = timing,
            SpellPerSecond = timing.SpellPerSecond(_spellResistance),
            LargestSpellHit = timing.LargestSpellHit(_spellResistance),
            ArmourPieces = armour.Pieces,
            ArmourUnread = armour.Unread,
            BodyArmour = _body,
            Impenetrability = armour.Impenetrability,
            Bane = armour.Bane,
            ArmourNote = type == 0x4 ? armour.Note : string.Create(CultureInfo.InvariantCulture, $"read against its damage 0x{type:X}{(armour.Note is null ? string.Empty : ": " + armour.Note)}"),
        };
        return FightLines.Derive(in input);
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 1): the line from the creatures attacking her or in reach, <paramref name="present"/>: each one's facts by its weenie class (<paramref name="facts"/>, every profile's targets and approaches), the worst of its
    /// damage types for her (her armour against the type times her live resistance to it), summed (<see cref="FightLines.DeriveAttackers"/>); an attacker with no facts lifts the line to the general fallback (rule 2); the measured hits are
    /// its floor (rule 3).
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="present">The attackers present.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="measuredPerSecond">The measured damage a second (the larger of the recent hits' rate and this fight-back's mean).</param>
    /// <param name="biggestHit">The largest hit measured.</param>
    /// <param name="facts">The facts to look the attackers up in (default: every profile of the catalog, <see cref="CreatureFacts.All"/>).</param>
    /// <param name="measure">
    /// M6 14 the walk's line: each kind's measured attacks by name (the walk's watch, <c>AttackWatch.Measure</c>), or null: none measured, the table prices every attacker whole. A kind's measured hits are split among the ones of it present
    /// (<see cref="FightLines.DeriveAttackers"/>).
    /// </param>
    public AttackersLine ReadAttackers(GameState state, IReadOnlyList<PresentAttacker> present, double now, double measuredPerSecond, double biggestHit, CreatureFacts? facts = null, Func<string, KindMeasure?>? measure = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(present);
        facts ??= CreatureFacts.All;
        var q = state.Self.Qualities;
        var healthMax = q.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max) ? max : 0u;
        Registry(q.Enchantments);
        var reads = new AttackerRead[present.Count];
        var her = DefenceRead.Of(q); // M6 14 the derived incoming: ACE's evade and resist rolls on her defences
        for (var i = 0; i < present.Count; i++)
        {
            var p = present[i];
            if (facts.Find(p.Wcid) is { } fact)
            {
                var type = Worst(state, now, fact.DamageTypes, out var armour, out var resistance);
                var timing = Times.Of(p.Wcid, p.Name);
                reads[i] = new AttackerRead(p.Guid, p.Wcid, p.Name, fact, type, double.IsNaN(armour.Factor) ? 1.0 : armour.Factor, resistance)
                {
                    Timing = timing,
                    SpellPerSecond = timing.SpellPerSecond(_spellResistance, her.Magic),
                    LargestSpellHit = timing.LargestSpellHit(_spellResistance),
                    StrikesLand = timing.StrikesLand(timing.Ranged ? her.Missile : her.Melee),
                    Measure = measure?.Invoke(p.Name),
                    KindPresent = KindCount(present, p.Name),
                };
            }
            else
            {
                reads[i] = new AttackerRead(p.Guid, p.Wcid, p.Name, null, 0, 1.0, 1.0);
            }
        }

        return FightLines.DeriveAttackers(healthMax, reads, measuredPerSecond, biggestHit);
    }

    private static int KindCount(IReadOnlyList<PresentAttacker> present, string name)
    {
        var count = 0;
        for (var i = 0; i < present.Count; i++)
        {
            count += string.Equals(present[i].Name, name, StringComparison.Ordinal) ? 1 : 0;
        }

        return Math.Max(1, count);
    }

    /// <summary>M6 14 attack timing: her resistance now against a spell of <paramref name="element"/> (her registry's multiplier, the worst of the bits; a spell takes no armour).</summary>
    /// <param name="state">The read model.</param>
    /// <param name="element">The spell's DamageType bits.</param>
    public double SpellFactorFor(GameState state, uint element)
    {
        ArgumentNullException.ThrowIfNull(state);
        Registry(state.Self.Qualities.Enchantments);
        return Resistance(_enchantments, element);
    }

    /// <summary>M6 14 retreat attackers: what reaches her of an unarmoured hit of <paramref name="damageTypes"/> now (the worst of its bits: her armour against it times her live resistance), and which bit that is.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="damageTypes">The DamageType bits (0: the worst of the seven).</param>
    /// <param name="now">The session clock.</param>
    /// <param name="type">The worst bit.</param>
    public double FactorFor(GameState state, uint damageTypes, double now, out uint type)
    {
        ArgumentNullException.ThrowIfNull(state);
        Registry(state.Self.Qualities.Enchantments);
        type = Worst(state, now, damageTypes, out var armour, out var resistance);
        return (double.IsNaN(armour.Factor) ? 1.0 : armour.Factor) * resistance;
    }

    /// <summary>
    /// Her resistance against <paramref name="types"/>: the largest multiplier over its bits (the hardest of the damages the hitter deals reaches her; <see cref="FightLines.ResistanceMod"/>); with no bit named (the profile gives no
    /// <c>damage_types</c>), the largest over the seven.
    /// </summary>
    /// <param name="enchantments">Her registry.</param>
    /// <param name="types">The hitter's damage type bits.</param>
    public static double Resistance(ImmutableArray<EnchantmentDesc> enchantments, uint types)
    {
        var mask = types == 0 ? 0x7Fu : types;
        var worst = 0.0;
        for (var bit = 1u; bit <= 0x40; bit <<= 1)
        {
            if ((mask & bit) != 0 && FightLines.ResistanceKey(bit) is var key and > 0)
            {
                worst = Math.Max(worst, FightLines.ResistanceMod(enchantments, key));
            }
        }

        return worst > 0 ? worst : 1.0;
    }

    /// <summary>The armour line of the ledger that cuts <paramref name="type"/>: Bludgeon Bane, or one of the six (<see cref="ArmourBanes"/>), or null.</summary>
    /// <param name="type">One DamageType bit.</param>
    public static string? BaneLineOf(uint type) => type == 0x4 ? BludgeonBaneLine : ArmourBanes.Names.FirstOrDefault(n => ArmourBanes.DamageBitOf(n) == type);

    /// <summary>A new registry: the body armour is read again and every type's armour with it (the resistance is read on each call).</summary>
    private void Registry(ImmutableArray<EnchantmentDesc> enchantments)
    {
        if (_enchantments.IsDefault || enchantments != _enchantments)
        {
            _enchantments = enchantments;
            _body = FightLines.BodyArmour(enchantments);
            Array.Fill(_armourAt, double.NegativeInfinity); // the body armour may have changed with it
        }
    }

    /// <summary>The bit of <paramref name="types"/> (0: the seven) whose hit reaches her most: its armour (read each <see cref="ArmourReadSeconds"/>) times her resistance to it.</summary>
    private uint Worst(GameState state, double now, uint types, out ArmourRead armour, out double resistance)
    {
        var mask = types == 0 ? 0x7Fu : types;
        var (best, bestFactor) = (0u, -1.0);
        (armour, resistance) = (new ArmourRead(double.NaN, 0, 0, 0, 0, null), 1.0);
        for (var i = 0; i < TypeBits.Length; i++)
        {
            var bit = TypeBits[i];
            if ((mask & bit) == 0)
            {
                continue;
            }

            if (now - _armourAt[i] >= ArmourReadSeconds)
            {
                _armourAt[i] = now;
                _armour[i] = ReadArmour(state, now, _body, bit);
            }

            var res = FightLines.ResistanceMod(_enchantments, FightLines.ResistanceKey(bit));
            var factor = (double.IsNaN(_armour[i].Factor) ? 1.0 : _armour[i].Factor) * res;
            if (factor > bestFactor)
            {
                (best, bestFactor, armour, resistance) = (bit, factor, _armour[i], res);
            }
        }

        return best;
    }

    /// <summary>The pack the ladder's survey names for the rung whose ground <paramref name="ground"/> is (<see cref="LadderHealth.Pack"/>), else 1.</summary>
    private static int PackOf(HuntingGround ground)
    {
        foreach (var rung in LadderLoader.WarMage.Rungs)
        {
            if (string.Equals(rung.GroundKey, ground.Key, StringComparison.Ordinal) && rung.Health is { } health)
            {
                return Math.Max(1, health.Pack);
            }
        }

        return 1;
    }

    /// <summary>The armour's factor against a hit of <paramref name="type"/> on what she wears now (see the class): the pieces' protection against the type, Impenetrability and that type's bane from the ledger.</summary>
    private static ArmourRead ReadArmour(GameState state, double now, double body, uint type)
    {
        var worn = SelfEquipJudge.Worn(state);
        var pieces = ArmorReport.Read(worn, type, out var unread);
        var ledger = ArmourBuffLedger.For(state);
        var baneLine = BaneLineOf(type);
        var (impen, bane, any) = (double.MaxValue, double.MaxValue, false);
        foreach (var w in worn)
        {
            if (!ArmorReport.Covers(w))
            {
                continue;
            }

            any = true;
            impen = Math.Min(impen, LineValue(ledger, w.Guid, ImpenetrabilityLine, now));
            bane = Math.Min(bane, baneLine is null ? 0 : LineValue(ledger, w.Guid, baneLine, now));
        }

        (impen, bane) = any ? (impen, bane) : (0, 0);
        return new ArmourRead(ArmorReport.ArmourFactor(pieces, body, impen, bane), pieces.Count, unread, impen, bane, null);
    }

    /// <summary>
    /// What the line adds on the piece while the ledger holds it (its spell's row, <see cref="GearScore.StatOf"/>; for one of the six banes the level's value, <see cref="ArmorReport.BaneAt"/>, the same for every bane line), 0 when it holds none or
    /// it ended.
    /// </summary>
    private static double LineValue(ArmourBuffLedger ledger, uint piece, string line, double now)
    {
        var left = ledger.Remaining(piece, line, now);
        if (!(left > 0) && !double.IsNaN(left))
        {
            return 0;
        }

        if (!ledger.TryGet(piece, line, out var entry))
        {
            return 0;
        }

        if (GearScore.StatOf(entry.SpellId) is { } row)
        {
            return row.Value;
        }

        var level = ArmourBanes.Lines.FirstOrDefault(l => string.Equals(l.Name, line, StringComparison.Ordinal))?.Spells.FirstOrDefault(s => s.Id == entry.SpellId)?.Position ?? 0;
        return level > 0 ? ArmorReport.BaneAt(level) : 0;
    }

    private readonly record struct ArmourRead(double Factor, int Pieces, int Unread, double Impenetrability, double Bane, string? Note);
}

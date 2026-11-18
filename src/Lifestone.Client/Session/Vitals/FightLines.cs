using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 next grounds (the owner, 2026-11-13: a ground profile holds facts; every number about how a character fights is computed from her live state and the creatures' facts; <c>docs/milestones/m6-derived-rules.md</c>): the fight's
/// retreat line, derived. A profile's <c>fight.retreat_health_percent</c> was sized for one character at one level (the Abode's 45 for a projected 136 health at level 40 and the Cave's live hits); this rule reads instead what she holds now
/// (her maximum health, the damage factor of her armour and protections) and the place's facts (the hardest hitter's DVal, DVar and Strength, the pack it sends). Pure. M6 14 holding safety wires it (<see cref="LiveFightLine"/>): the
/// autopilot's retreat line and health floor and the travel legs' retreat read it on her live state wherever the place's targets carry their attack's facts, and the derived line wins over a profile's hand-sized one.
/// </summary>
/// <remarks>
/// <para><b>The loss while she leaves</b>: the pack's hits a second (<c>pack x hit x factor x hits a second</c>, the hit ACE's mean uncritical hit, <see cref="LadderHitter.Hit"/>; M6 14 attack timing: the hits a second the creature's own, derived from
/// ACE's attack loop and the dats, <see cref="AttackTiming"/>, the 2.75 s fallback only where they cannot be read, named) over <see cref="LeaveSeconds"/>, plus one critical of the largest
/// hit (<see cref="CriticalFactor"/>). The line is that loss as a share of her maximum health, rounded up to <see cref="RoundToPercent"/>, at least <see cref="MinPercent"/> and at most <see cref="MaxPercent"/>.</para>
/// <para><b>The factor</b> is what reaches her of an unarmoured hit: <see cref="Mage.ArmorReport"/>'s on the pieces she wears and the levels standing, times any vulnerability on her (Tusker's Gift's x2.85 lifts the line to the top: she leaves).
/// Live, the Abode's Redeemers under the buff bot's 28 lines (factor 0.049) landed 1,060 hits of mean 2.04 (normal 1 to 3, criticals 4 to 7; <c>cache/m6/14/live/abode7</c>) against this model's 4.0 mean: the model is the safe side.</para>
/// </remarks>
public static class FightLines
{
    /// <summary>
    /// The seconds she takes to leave: Lifestone Recall's cast and ACE's recall delay (<see cref="RecallWayOut.Seconds"/>, M6 14 the losing race: the live casts' median and ACE's <c>HandleCastSpell_PortalRecall</c>; before it, the Cave's
    /// measured way out of about 4 s).
    /// </summary>
    public const double LeaveSeconds = RecallWayOut.Seconds;

    /// <summary>ACE's critical on a creature's hit with no weapon doubles the largest hit (@95ef426d <c>WorldObject_Weapon.cs:291,356</c>); a weapon's CriticalMultiplier sets its own (<see cref="LadderHitter.CriticalFactor"/>).</summary>
    public const double CriticalFactor = 2.0;

    /// <summary>The line's step (Lifestone's own: the profiles' lines were multiples of 5).</summary>
    public const int RoundToPercent = 5;

    /// <summary>The lowest line: a fight never runs her to nothing.</summary>
    public const int MinPercent = 5;

    /// <summary>The highest line: past it she does not fight at all (the loss while leaving is near all her health).</summary>
    public const int MaxPercent = 95;

    /// <summary>ACE's largest uncritical unarmoured hit of <paramref name="hitter"/>: its DVal times the attribute modifier (<see cref="LadderHitter.Hit"/>'s terms with no variance).</summary>
    /// <param name="hitter">The creature.</param>
    public static double MaxHit(LadderHitter hitter)
    {
        ArgumentNullException.ThrowIfNull(hitter);
        return hitter.DVal * hitter.AttributeMod;
    }

    /// <summary>ACE's critical of <paramref name="hitter"/>, unarmoured: its largest hit times its critical factor (<see cref="LadderHitter.CriticalFactor"/>: 2 with no weapon, 3 for a coconut's CriticalMultiplier of 2).</summary>
    /// <param name="hitter">The creature.</param>
    public static double CriticalHit(LadderHitter hitter) => MaxHit(hitter) * hitter.CriticalFactor;

    /// <summary>
    /// M6 14 the losing race: ACE's mean unarmoured hit with its criticals counted: the uncritical mean (<see cref="LadderHitter.Hit"/>) the share of the time it is not a critical, the critical (<see cref="CriticalHit"/>) the share it is
    /// (<see cref="LadderHitter.CriticalChance"/>, ACE's <c>GetWeaponCriticalChance</c>; her critical resistance rating is not read, the safe side).
    /// </summary>
    /// <param name="hitter">The creature.</param>
    public static double MeanHit(LadderHitter hitter)
    {
        ArgumentNullException.ThrowIfNull(hitter);
        var p = Math.Clamp(hitter.CriticalChance, 0, 1);
        return ((1 - p) * hitter.Hit) + (p * CriticalHit(hitter));
    }

    /// <summary>The health she loses while she leaves: the pack's hits over <see cref="LeaveSeconds"/> and one critical.</summary>
    /// <param name="hitter">The place's hardest hitter.</param>
    /// <param name="pack">The attackers at once (a fact of the place's spawns; at least 1).</param>
    /// <param name="damageFactor">What reaches her of an unarmoured hit (1: no armour).</param>
    /// <param name="timing">M6 14 attack timing: the hitter's attack timing (<see cref="AttackTiming"/>); null: the named 2.75 s fallback (<see cref="TimingOrFallback"/>).</param>
    /// <param name="spellPerSecond">M6 14 attack timing: the damage a second one of it deals her with its spells (<see cref="AttackTiming.SpellPerSecond(Func{uint, double}?)"/>), 0 when none is priced.</param>
    public static double LossWhileLeaving(LadderHitter hitter, int pack, double damageFactor, AttackTiming? timing = null, double spellPerSecond = 0)
    {
        ArgumentNullException.ThrowIfNull(hitter);
        var t = TimingOrFallback(hitter, timing);
        var perSecond = Math.Max(1, pack) * ((hitter.Hit * damageFactor * t.HitsPerSecond) + Math.Max(0, spellPerSecond));
        return (perSecond * LeaveSeconds) + (CriticalHit(hitter) * damageFactor);
    }

    /// <summary>
    /// M6 14 attack timing: <paramref name="timing"/>, or the hitter's named fallback when none is given (<see cref="AttackTiming.Underived"/>: one hit every <see cref="AttackTimings.UnderivedSwingSeconds"/>,
    /// <c>TODO(f11-swing-seconds)</c>, with the creature named).
    /// </summary>
    /// <param name="hitter">The hitter.</param>
    /// <param name="timing">Its timing, or null.</param>
    public static AttackTiming TimingOrFallback(LadderHitter hitter, AttackTiming? timing)
    {
        ArgumentNullException.ThrowIfNull(hitter);
        return timing ?? AttackTiming.Underived(hitter.Wcid, hitter.Name, "no timing read for this line");
    }

    /// <summary>The retreat line in percent of her maximum health, and why in words.</summary>
    /// <param name="healthMax">Her maximum health now.</param>
    /// <param name="hitter">The place's hardest hitter.</param>
    /// <param name="pack">The attackers at once.</param>
    /// <param name="damageFactor">What reaches her of an unarmoured hit.</param>
    /// <param name="why">The line's numbers.</param>
    /// <param name="timing">M6 14 attack timing: the hitter's attack timing, or null (the named fallback).</param>
    public static int RetreatPercent(uint healthMax, LadderHitter hitter, int pack, double damageFactor, out string why, AttackTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(hitter);
        var t = TimingOrFallback(hitter, timing);
        var loss = LossWhileLeaving(hitter, pack, damageFactor, t);
        var share = healthMax == 0 ? double.PositiveInfinity : 100.0 * loss / healthMax;
        var line = LineOf(share);
        why = string.Create(
            CultureInfo.InvariantCulture,
            $"retreat {line}%: {Math.Max(1, pack)} x {hitter.Name}'s hit {hitter.Hit} x factor {damageFactor:0.###} at {t.Short()} for {LeaveSeconds:0.##} s, and a critical of {CriticalHit(hitter) * damageFactor:0.#}: {loss:0.#} of {healthMax} health ({share:0.#}%)");
        return line;
    }

    /// <summary>The line of a loss that is <paramref name="share"/> percent of her maximum health: rounded up to <see cref="RoundToPercent"/>, inside <see cref="MinPercent"/> to <see cref="MaxPercent"/> (an unread health, infinity, is the top).</summary>
    internal static int LineOf(double share) => double.IsInfinity(share) || double.IsNaN(share) ? MaxPercent : (int)Math.Clamp(Math.Ceiling(share / RoundToPercent) * RoundToPercent, MinPercent, MaxPercent);

    // ------------------------------------------------------------------------------------------------ M6 14 retreat attackers: the line from the creatures present

    /// <summary>
    /// M6 14 retreat attackers: the line an attacker with no facts gives (rule 2): the conservative general fallback, the autopilot's general retreat default before the line was derived (<c>AutopilotScenario.HealthFloorPercent</c>, 50;
    /// the run leg read it until the Holding's merge, and the live leg would have left the Infected Assailers at about 98 health on it). M6 14 the losing race: every creature of the world database has its attack in the creature table
    /// (<see cref="Grounds.CreatureTable"/>), so only a weenie missing from it is unknown, named on its line (<see cref="UnknownTodo"/>).
    /// </summary>
    public const int GeneralFallbackPercent = 50;

    /// <summary>The named TODO of an attacker whose weenie is in no ground profile and not in the world database's creature table (a weenie the pinned world SQL does not hold).</summary>
    public const string UnknownTodo = "TODO(m6-creature-attack-unknown)";

    /// <summary>
    /// M6 14 retreat attackers (rule 3): the line the measured hits alone say: the damage a second this fight or fight-back has seen over the seconds she needs to leave (<see cref="LeaveSeconds"/>) plus the largest hit seen, as a share of her
    /// maximum health (<see cref="LineOf"/>); <see cref="MinPercent"/> with nothing measured. No line is ever under it.
    /// </summary>
    /// <param name="healthMax">Her maximum health (0: unread, the top).</param>
    /// <param name="perSecond">The measured damage a second.</param>
    /// <param name="biggestHit">The largest hit seen.</param>
    public static int MeasuredPercent(uint healthMax, double perSecond, double biggestHit)
    {
        var loss = (Math.Max(0, perSecond) * LeaveSeconds) + Math.Max(0, biggestHit);
        return loss <= 0 ? MinPercent : LineOf(healthMax == 0 ? double.PositiveInfinity : 100.0 * loss / healthMax);
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 1; live 2026-11-14 10:59:16 UTC: the run leg's line was 10%, the Holding's Rampager in bludgeon under the bot's protection, while two Infected Assailers took her from 163 to 15 in eleven seconds): the
    /// retreat line from the creatures attacking her now. Each known attacker (<see cref="AttackerRead.Fact"/>) hits for its own mean hit through its own factor (her armour and her live resistance against its own damage type) at its own
    /// hits a second (M6 14 attack timing: <see cref="AttackerRead.Timing"/>, ACE's attack loop over the dats; the named 2.75 s fallback without one), and its priced spells (<see cref="AttackerRead.SpellPerSecond"/>) join it: their sum a second is the model; the critical is the largest of theirs; the measured hits (<paramref name="measuredPerSecond"/>, <paramref name="biggestHit"/>) count in their place when larger
    /// (rule 3). The loss over <see cref="LeaveSeconds"/> is the line (<see cref="LineOf"/>). An attacker with no facts lifts it to at least <see cref="GeneralFallbackPercent"/> (rule 2), named; so does a fight with no attacker read.
    /// M6 14 the losing race: the mean hit counts the attacker's criticals at ACE's chance (<see cref="MeanHit"/>) and the critical is its weapon's (<see cref="CriticalHit"/>); the way out's seconds are the recall's
    /// (<see cref="RecallWayOut.Seconds"/>). Pure.
    /// </summary>
    /// <param name="healthMax">Her maximum health now (0: unread, the top).</param>
    /// <param name="attackers">The attackers read.</param>
    /// <param name="measuredPerSecond">The measured damage a second (the larger of the recent hits' rate and this fight-back's mean).</param>
    /// <param name="biggestHit">The largest hit measured.</param>
    public static AttackersLine DeriveAttackers(uint healthMax, IReadOnlyList<AttackerRead> attackers, double measuredPerSecond, double biggestHit)
    {
        ArgumentNullException.ThrowIfNull(attackers);
        var (model, critical, unknown) = (0.0, 0.0, 0);
        foreach (var a in attackers)
        {
            if (a.Fact is not { } fact)
            {
                unknown++;
                continue;
            }

            var (rate, largest) = Priced(a, fact);
            model += rate;
            critical = Math.Max(critical, largest);
        }

        var measured = Math.Max(0, measuredPerSecond);
        var biggest = Math.Max(0, biggestHit);
        var loss = (Math.Max(model, measured) * LeaveSeconds) + Math.Max(critical, biggest);
        var share = healthMax == 0 ? double.PositiveInfinity : 100.0 * loss / healthMax;
        var percent = LineOf(share);
        var fallback = unknown > 0 || attackers.Count == 0;
        return new AttackersLine(fallback ? Math.Max(percent, GeneralFallbackPercent) : percent, percent, loss, model, measured, Math.Max(critical, biggest), unknown, healthMax, attackers);
    }

    /// <summary>
    /// M6 14 the walk's line (live ladder51 2026-11-17 03:48:30.997 UTC: three Dark Wisps priced at the table's 6.6 a second each, every one of their spells landing, read the line to 95% against a measured 3.1 a second, and she left a fight
    /// her heal outpaced at 142 of 191; the same fault as the losing race's of 2026-11-16, <c>AutopilotVitalsGuard.IncomingNow</c>): one attacker's damage a second and its one largest hit. Its table rate (its mean hit with criticals
    /// through her armour and resistance at its own hits a second, and its priced spells) prices only the part of the hits' window (<see cref="RecentHits.AverageSeconds"/>) its kind was not yet heard in; the part it was heard in is its kind's
    /// measured hits there, split among the ones of it present (<see cref="AttackerRead.Measure"/>): a kind heard over the whole window is its measured hits alone, never the table's rate added on top. With no measure the table prices it
    /// whole (the earlier protections: the 2026-11-14 10:59:16 Infected Assailers arriving on the Holding's approach). The largest hit is one hit, the way out's margin: a kind heard over the whole window gives its largest measured hit; a kind
    /// still partly priced by the table keeps the table's largest (ACE's critical of its hit or its largest spell), as one newly arrived can land it in the way out.
    /// </summary>
    private static (double PerSecond, double Largest) Priced(in AttackerRead a, CreatureFact fact)
    {
        var timing = TimingOrFallback(fact.Hitter, a.Timing);
        var table = (MeanHit(fact.Hitter) * a.Factor * timing.HitsPerSecond * Math.Clamp(a.StrikesLand, 0, 1)) + Math.Max(0, a.SpellPerSecond); // M6 14 the derived incoming: the strikes that land (ACE's evade roll)
        var tableLargest = Math.Max(CriticalHit(fact.Hitter) * a.Factor, a.LargestSpellHit);
        if (a.Measure is not { } m)
        {
            return (table, tableLargest);
        }

        var heard = m.Heard;
        var measured = Math.Max(0, m.Damage) / Math.Max(1, a.KindPresent) / RecentHits.AverageSeconds;
        return (measured + (table * (1 - heard)), heard >= 1 ? Math.Max(0, m.Biggest) : Math.Max(tableLargest, m.Biggest));
    }

    /// <summary>M6 14 the walk's line: one attacker's damage a second and largest hit as the line prices it (<see cref="Priced"/>), for the words; (0, 0) with no facts.</summary>
    /// <param name="a">The attacker.</param>
    public static (double PerSecond, double Largest) PricedOf(in AttackerRead a) => a.Fact is { } fact ? Priced(a, fact) : (0, 0);

    // ------------------------------------------------------------------------------------------------ M6 14 holding safety: the line on her live state

    /// <summary>ACE's EnchantmentTypeFlags.Float (the enchantment changes a float property: a resistance, a bane).</summary>
    public const uint FloatKind = 0x8;

    /// <summary>ACE's EnchantmentTypeFlags.BodyArmorValue (the enchantment changes the body armour: Armor Self, Imperil).</summary>
    public const uint BodyArmourKind = 0x80;

    /// <summary>ACE's PropertyFloat ResistBludgeon, the key of Bludgeoning Protection and of Tusker's Gift (the dats' spell 2166: ResistBludgeon x2.85).</summary>
    public const uint ResistBludgeonKey = 66;

    /// <summary>
    /// ACE's PropertyFloat key of the resistance to one damage type (ACE.Entity/Enum/Properties/PropertyFloat.cs @95ef426d: ResistSlash 64, ResistPierce 65, ResistBludgeon 66, ResistFire 67, ResistCold 68, ResistAcid 69, ResistElectric 70; the
    /// DamageType bits slash 0x1, pierce 0x2, bludgeon 0x4, cold 0x8, fire 0x10, acid 0x20, electric 0x40), or 0 for another bit.
    /// </summary>
    /// <param name="damageType">One DamageType bit.</param>
    public static uint ResistanceKey(uint damageType) => damageType switch
    {
        0x1 => 64,
        0x2 => 65,
        0x4 => ResistBludgeonKey,
        0x8 => 68,
        0x10 => 67,
        0x20 => 69,
        0x40 => 70,
        _ => 0,
    };

    /// <summary>
    /// Her resistance multiplier against <paramref name="key"/> (a <see cref="ResistanceKey"/>) as her registry holds it, re-expressed from ACE's <c>Creature.GetResistanceMod</c> (Creature_Properties.cs:100-152 @95ef426d, which multiplies
    /// <c>EnchantmentManager.GetProtectionResistanceMod</c> by <c>GetVulnerabilityResistanceMod</c>, the latter EnchantmentManager.cs:925-939 as <c>Mage.FightDebuffs</c> cites it): every multiplicative float enchantment on the key, the strongest of each spell category standing (the registry's
    /// duel: the higher power, then the later start, then the first listed; <c>EnchantmentRules.Holds</c>), multiplied together. A protection is under 1 (Bludgeoning Protection VII x0.35), a vulnerability over 1 (Tusker's Gift x2.85), so a
    /// landed Gift raises it while it lasts and the server's removal of the entry lowers it again. 1 with none (an unread registry reads none: the safe side). Allocation-free.
    /// </summary>
    /// <param name="enchantments">Her registry (<c>PlayerQualities.Enchantments</c>).</param>
    /// <param name="key">The resistance's PropertyFloat key.</param>
    public static double ResistanceMod(ImmutableArray<EnchantmentDesc> enchantments, uint key)
    {
        if (enchantments.IsDefaultOrEmpty || key == 0)
        {
            return 1.0;
        }

        var mod = 1.0;
        for (var i = 0; i < enchantments.Length; i++)
        {
            if (OnKey(enchantments[i], key, FloatKind, EnchantmentRules.Multiplicative) && Stands(enchantments, i, key, FloatKind, EnchantmentRules.Multiplicative))
            {
                mod *= enchantments[i].StatModValue;
            }
        }

        return mod;
    }

    /// <summary>
    /// The body armour her registry adds (Armor Self's, less an Imperil's): every additive body-armour enchantment, the strongest of each spell category standing, summed: the body armour ACE adds to every part of the body
    /// (Monster_Melee.cs:428-452 @95ef426d, as <c>Mage.ArmorReport</c> reads it). Allocation-free.
    /// </summary>
    /// <param name="enchantments">Her registry.</param>
    public static double BodyArmour(ImmutableArray<EnchantmentDesc> enchantments)
    {
        if (enchantments.IsDefaultOrEmpty)
        {
            return 0;
        }

        var armour = 0.0;
        for (var i = 0; i < enchantments.Length; i++)
        {
            if (OnKey(enchantments[i], null, BodyArmourKind, EnchantmentRules.Additive) && Stands(enchantments, i, null, BodyArmourKind, EnchantmentRules.Additive))
            {
                armour += enchantments[i].StatModValue;
            }
        }

        return armour;
    }

    private static bool OnKey(in EnchantmentDesc e, uint? key, uint kind, uint how) =>
        (e.StatModType & kind) != 0 && (e.StatModType & how) != 0 && (key is null || e.StatModKey == key) && e.Bucket is EnchantmentBucket.Multiplicative or EnchantmentBucket.Additive;

    /// <summary>True when no other entry of the same spell category on the same stat holds against entry <paramref name="i"/> (the higher power; at equal power the later start; at both equal the first listed).</summary>
    private static bool Stands(ImmutableArray<EnchantmentDesc> list, int i, uint? key, uint kind, uint how)
    {
        var e = list[i];
        for (var j = 0; j < list.Length; j++)
        {
            if (j == i || list[j].SpellCategory != e.SpellCategory || !OnKey(list[j], key, kind, how))
            {
                continue;
            }

            var o = list[j];
            if (o.PowerLevel > e.PowerLevel || (o.PowerLevel == e.PowerLevel && (o.StartTime > e.StartTime || (o.StartTime == e.StartTime && j < i))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The retreat line from <paramref name="input"/>: the larger of the model's loss a second (the place's pack of its hardest hitter through her armour and resistance) and the fight's measured damage a second, over
    /// <see cref="LeaveSeconds"/>, plus one critical (the model's, or the biggest hit seen if larger), as a share of her maximum health (<see cref="LineOf"/>). Not derived (<see cref="DerivedLine.Derived"/> false) when the place names
    /// no creature's attack facts: the caller's own line then stands. Pure.
    /// </summary>
    /// <param name="input">Her state and the place's facts.</param>
    public static DerivedLine Derive(in FightLineInput input)
    {
        if (input.Hitter is not { } hitter)
        {
            return new DerivedLine(0, false, 0, 0, 0, 0, 0, input);
        }

        var armour = double.IsNaN(input.ArmourFactor) ? 1.0 : Math.Max(0, input.ArmourFactor);
        var factor = armour * Math.Max(0, input.Resistance);
        var timing = TimingOrFallback(hitter, input.Timing);
        var model = Math.Max(1, input.Pack) * ((hitter.Hit * factor * timing.HitsPerSecond) + Math.Max(0, input.SpellPerSecond));
        var measured = Math.Max(0, input.IncomingPerSecond);
        var critical = Math.Max(Math.Max(CriticalHit(hitter) * factor, input.LargestSpellHit), Math.Max(0, input.BiggestHit));
        var loss = (Math.Max(model, measured) * LeaveSeconds) + critical;
        var share = input.HealthMax == 0 ? double.PositiveInfinity : 100.0 * loss / input.HealthMax;
        return new DerivedLine(LineOf(share), true, loss, model, measured, critical, factor, input);
    }
}

/// <summary>What <see cref="FightLines.Derive"/> reads at one moment (M6 14 holding safety).</summary>
/// <param name="Ground">The ground's key, for the words.</param>
/// <param name="HealthMax">Her maximum health now (0: unread, the line is then the top).</param>
/// <param name="Hitter">The place's hardest hitter (<see cref="HuntingGround.HardestHitter"/>), or null when the place names no creature's attack facts.</param>
/// <param name="Pack">The attackers at once, a fact of the place (the ladder rung's surveyed <c>health.pack</c>, else 1: <c>TODO(m6-pack-past-the-cave)</c>); the measured damage a second covers a larger pack live.</param>
/// <param name="ArmourFactor">What her armour leaves of an unarmoured hit, before any resistance (<see cref="Mage.ArmorReport.ArmourFactor"/>); NaN when not read (the unarmoured hit, the safe side).</param>
/// <param name="Resistance">Her live resistance multiplier against the hitter's damage (<see cref="FightLines.ResistanceMod"/>): the protections under 1, a vulnerability such as Tusker's Gift over 1.</param>
/// <param name="IncomingPerSecond">The damage a second the fight measures (<see cref="RecentHits"/>), 0 when nothing is measured.</param>
/// <param name="BiggestHit">The biggest recent hit measured (0: none).</param>
/// <param name="ProfilePercent">The profile's hand-sized <c>fight.retreat_health_percent</c>, or null: a derived line ignores it, and the reader logs that it did.</param>
public readonly record struct FightLineInput(string Ground, uint HealthMax, LadderHitter? Hitter, int Pack, double ArmourFactor, double Resistance, double IncomingPerSecond, double BiggestHit, int? ProfilePercent)
{
    /// <summary>The worn pieces whose armour was read.</summary>
    public int ArmourPieces { get; init; }

    /// <summary>The covering pieces left out because their appraisal is not held (their armour is not counted: the safe side).</summary>
    public int ArmourUnread { get; init; }

    /// <summary>The body armour her registry adds (Armor Self's).</summary>
    public double BodyArmour { get; init; }

    /// <summary>The armour level Impenetrability adds on the worn pieces (the lowest the session's ledger holds across them; 0 when a piece lacks it).</summary>
    public double Impenetrability { get; init; }

    /// <summary>What the bludgeon bane adds on the worn pieces (the lowest across them).</summary>
    public double Bane { get; init; }

    /// <summary>A word on the armour's damage type when it is not bludgeon's (the armour is then read as none, the safe side), else null.</summary>
    public string? ArmourNote { get; init; }

    /// <summary>M6 14 attack timing: the hitter's attack timing (<see cref="AttackTiming"/>), or null: the named 2.75 s fallback.</summary>
    public AttackTiming? Timing { get; init; }

    /// <summary>M6 14 attack timing: the damage a second one hitter deals her with its priced spells (her resistance applied), 0 with none.</summary>
    public double SpellPerSecond { get; init; }

    /// <summary>M6 14 attack timing: the hitter's largest spell hit on her (0 with none).</summary>
    public double LargestSpellHit { get; init; }
}

/// <summary>The derived line and every number it was made of (M6 14 holding safety; <see cref="FightLines.Derive"/>).</summary>
/// <param name="Percent">The line in percent of her maximum health (0 when not derived).</param>
/// <param name="Derived">False when the place names no creature's attack facts: the caller's own line stands.</param>
/// <param name="Loss">The health she loses while she leaves.</param>
/// <param name="ModelPerSecond">The model's loss a second.</param>
/// <param name="MeasuredPerSecond">The measured damage a second.</param>
/// <param name="Critical">The one critical counted.</param>
/// <param name="Factor">What reaches her of an unarmoured hit: the armour's factor times the resistance.</param>
/// <param name="Input">What it read.</param>
public readonly record struct DerivedLine(int Percent, bool Derived, double Loss, double ModelPerSecond, double MeasuredPerSecond, double Critical, double Factor, FightLineInput Input)
{
    /// <summary>The line in words with every number (built on demand; the readers log it when the line changes).</summary>
    public string Why()
    {
        var i = Input;
        if (!Derived)
        {
            return string.Create(CultureInfo.InvariantCulture, $"not derived: {i.Ground} names no creature's attack facts (a target's d_val, d_var and Strength)");
        }

        var hitter = i.Hitter!;
        var share = i.HealthMax == 0 ? "unread" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * Loss / i.HealthMax:0.#}%");
        var armour = double.IsNaN(i.ArmourFactor) ? "unread (1)" : i.ArmourFactor.ToString("0.###", CultureInfo.InvariantCulture);
        var ignored = i.ProfilePercent is { } p ? string.Create(CultureInfo.InvariantCulture, $"; the profile's fight.retreat_health_percent {p} is ignored") : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"retreat {Percent}% derived on {i.Ground}: {Math.Max(1, i.Pack)} x {hitter.Name}'s hit {hitter.Hit} x factor {Factor:0.####} (armour {armour} on {i.ArmourPieces} piece(s), {i.ArmourUnread} unread, body armour {i.BodyArmour:0}, Impenetrability {i.Impenetrability:0}, bane {i.Bane:0.###}{(i.ArmourNote is null ? string.Empty : "; " + i.ArmourNote)}; resistance {i.Resistance:0.###}) at {FightLines.TimingOrFallback(hitter, i.Timing).Short()}{(i.SpellPerSecond > 0 ? string.Create(CultureInfo.InvariantCulture, $" and its spells' {i.SpellPerSecond:0.##}/s") : string.Empty)} is {ModelPerSecond:0.##}/s, measured {MeasuredPerSecond:0.##}/s; the larger for {FightLines.LeaveSeconds:0.##} s and a critical of {Critical:0.#}: {Loss:0.#} of {i.HealthMax} health ({share}){ignored}");
    }
}

/// <summary>One creature attacking her, as the line from the attackers present reads it (M6 14 retreat attackers; <see cref="FightLines.DeriveAttackers"/>).</summary>
/// <param name="Guid">Its guid.</param>
/// <param name="Wcid">Its weenie class id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Fact">Its attack facts by weenie class (<see cref="CreatureFacts"/>: a ground profile's, else the world database's creature table), or null: the weenie is in neither.</param>
/// <param name="DamageType">The DamageType bit its factor is read against: the worst of its attacks' bits for her (0 with no facts).</param>
/// <param name="Armour">What her armour leaves of its hit of that type (1: none read).</param>
/// <param name="Resistance">Her live resistance multiplier against that type (her registry's protections and vulnerabilities).</param>
public readonly record struct AttackerRead(uint Guid, uint Wcid, string Name, CreatureFact? Fact, uint DamageType, double Armour, double Resistance)
{
    /// <summary>What reaches her of its unarmoured hit: the armour's factor times the resistance.</summary>
    public double Factor => Math.Max(0, Armour) * Math.Max(0, Resistance);

    /// <summary>M6 14 attack timing: its attack timing (<see cref="AttackTiming"/>), or null: the named 2.75 s fallback.</summary>
    public AttackTiming? Timing { get; init; }

    /// <summary>M6 14 attack timing: the damage a second its priced spells deal her (her resistance to each element applied), 0 with none.</summary>
    public double SpellPerSecond { get; init; }

    /// <summary>M6 14 attack timing: its largest spell hit on her (0 with none).</summary>
    public double LargestSpellHit { get; init; }

    /// <summary>
    /// M6 14 the derived incoming: the share of its strikes that land on her, ACE's evade roll of its attack skill against her defence (<see cref="AttackTiming.StrikesLand"/>); 1 when not read. Its spells' landing share is in
    /// <see cref="SpellPerSecond"/> already.
    /// </summary>
    public double StrikesLand { get; init; } = 1;

    /// <summary>M6 14 the walk's line: its kind's measured attacks and hits (<see cref="KindMeasure"/>), or null: not measured, the table prices it whole.</summary>
    public KindMeasure? Measure { get; init; }

    /// <summary>M6 14 the walk's line: the ones of its kind present (its kind's measured hits are split among them); at least 1.</summary>
    public int KindPresent { get; init; } = 1;
}

/// <summary>
/// M6 14 the walk's line: one kind's attacks as the walk's watch heard them in this attack (<c>AttackWatch.Measure</c>).
/// </summary>
/// <param name="Since">The seconds since its first attack in this attack.</param>
/// <param name="Attacks">Its attacks heard (hits and attacks that took nothing alike).</param>
/// <param name="Damage">Its hits' damage in the last <see cref="RecentHits.AverageSeconds"/>.</param>
/// <param name="Biggest">Its largest hit in that window.</param>
public readonly record struct KindMeasure(double Since, int Attacks, double Damage, double Biggest)
{
    /// <summary>The share of the hits' window (<see cref="RecentHits.AverageSeconds"/>) the kind has been heard in: 1 once its attacks span the whole of it.</summary>
    public double Heard => Math.Clamp(Since / RecentHits.AverageSeconds, 0, 1);
}

/// <summary>The line from the attackers present and every number it was made of (M6 14 retreat attackers; <see cref="FightLines.DeriveAttackers"/>).</summary>
/// <param name="Percent">The line in percent of her maximum health: the loss's line, lifted to <see cref="FightLines.GeneralFallbackPercent"/> by an attacker with no facts or by no attacker read.</param>
/// <param name="LossPercent">The loss's own line, before the fallback.</param>
/// <param name="Loss">The health she loses while she leaves.</param>
/// <param name="ModelPerSecond">The known attackers' hits a second.</param>
/// <param name="MeasuredPerSecond">The measured damage a second.</param>
/// <param name="Critical">The one critical counted (the attackers' largest, or the biggest hit measured).</param>
/// <param name="Unknown">The attackers with no facts.</param>
/// <param name="HealthMax">Her maximum health.</param>
/// <param name="Attackers">The attackers read.</param>
public readonly record struct AttackersLine(int Percent, int LossPercent, double Loss, double ModelPerSecond, double MeasuredPerSecond, double Critical, int Unknown, uint HealthMax, IReadOnlyList<AttackerRead> Attackers)
{
    /// <summary>True when an attacker with no facts (or no attacker at all) set the line to the general fallback.</summary>
    public bool Fallback => Percent > LossPercent;

    /// <summary>The line in words with every number, each attacker by name (built on demand; the readers log it when the line changes).</summary>
    public string Why()
    {
        var c = CultureInfo.InvariantCulture;
        var parts = new List<string>(Attackers.Count);
        foreach (var a in Attackers)
        {
            parts.Add(a.Fact is { } f
                ? string.Create(c, $"'{a.Name}' 0x{a.Guid:X8} ({f.Where}: hit {f.Hitter.Hit}{CriticalWords(f.Hitter)}, damage 0x{a.DamageType:X} x armour {a.Armour:0.###} x resistance {a.Resistance:0.###} = {a.Factor:0.####}; {FightLines.TimingOrFallback(f.Hitter, a.Timing).Short()}{(a.SpellPerSecond > 0 ? string.Create(c, $", its spells {a.SpellPerSecond:0.##}/s") : string.Empty)}{MeasureWords(a)})")
                : string.Create(c, $"'{a.Name}' 0x{a.Guid:X8} (weenie {a.Wcid}: in no ground profile and not in the world database's creature table, {FightLines.UnknownTodo}: the general fallback {FightLines.GeneralFallbackPercent}%)"));
        }

        var who = Attackers.Count == 0 ? string.Create(c, $"no attacker read in reach: the general fallback {FightLines.GeneralFallbackPercent}%") : string.Join(", ", parts);
        var share = HealthMax == 0 ? "unread" : string.Create(c, $"{100.0 * Loss / HealthMax:0.#}%");
        var lifted = Fallback ? string.Create(c, $"; lifted from {LossPercent}% to the general fallback {FightLines.GeneralFallbackPercent}% (rule 2)") : string.Empty;
        return string.Create(c, $"retreat {Percent}% from the attackers present: {who}; their hits a second are {ModelPerSecond:0.##}/s, measured {MeasuredPerSecond:0.##}/s; the larger for {FightLines.LeaveSeconds:0.##} s and a critical of {Critical:0.#}: {Loss:0.#} of {HealthMax} health ({share}){lifted}");
    }

    /// <summary>M6 14 the walk's line: how the attacker is priced, in words (its kind's measure and what the table still prices), or empty when it is not measured.</summary>
    private static string MeasureWords(in AttackerRead a)
    {
        if (a.Measure is not { } m)
        {
            return string.Empty;
        }

        var c = CultureInfo.InvariantCulture;
        var (rate, largest) = FightLines.PricedOf(a);
        var share = a.KindPresent > 1 ? string.Create(c, $" shared by the {a.KindPresent} of it present") : string.Empty;
        return m.Heard >= 1
            ? string.Create(c, $"; measured over the whole {RecentHits.AverageSeconds:0} s: its kind's {m.Attacks} attack(s) since {m.Since:0.#} s, {m.Damage:0} in hits{share}, largest {m.Biggest:0}: priced {rate:0.##}/s, not the table's")
            : string.Create(c, $"; heard {m.Since:0.#} s of the {RecentHits.AverageSeconds:0}: its kind's {m.Attacks} attack(s), {m.Damage:0} in hits{share}, the table's rate for the other {(1 - m.Heard) * RecentHits.AverageSeconds:0.#} s: priced {rate:0.##}/s, largest {largest:0.#}");
    }

    /// <summary>A hitter's critical in words when it is not ACE's default (a weapon's chance or multiplier), else empty.</summary>
    private static string CriticalWords(LadderHitter h) =>
        h.CriticalChance == LadderHitter.DefaultCriticalChance && h.CriticalMultiplier == LadderHitter.DefaultCriticalMultiplier
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", critical {h.CriticalChance * 100:0.#}% at x{h.CriticalFactor:0.##}");
}

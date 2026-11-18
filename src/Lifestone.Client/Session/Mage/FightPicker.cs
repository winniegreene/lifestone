using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Mage;

/// <summary>What the fight's next move is (M5 7 F10, D90): the bolt, a war option of F9's (<see cref="WarSpellPicker"/>), a debuff (<see cref="FightDebuffs"/>) or Drain Health Other as an attack.</summary>
public enum FightMoveKind : byte
{
    /// <summary>The bolt in hand (the fallback; a tie keeps it).</summary>
    Bolt,

    /// <summary>A war option other than the bolt (F9: the ring, a streak, a volley, a blast).</summary>
    War,

    /// <summary>A debuff on the target (F8's opener, Fire Vulnerability, and the rest of <see cref="FightDebuffs"/>).</summary>
    Debuff,

    /// <summary>Drain Health Other as an attack: nearly a bolt of damage and a heal (F11).</summary>
    DrainHealth,
}

/// <summary>
/// The two live rates of D90, each seeded and then measured (an exponentially weighted mean, <see cref="Weight"/> a sample), and the incoming damage rate (a time-weighted mean over <see cref="IncomingSeconds"/>). Allocation-free.
/// </summary>
public sealed class FightRates
{
    /// <summary>The dance's seconds a mana point before any dance is measured: 0.09 at level 80 (the lead's D90 note, F9's <see cref="WarPickSettings.DanceSecondsPerMana"/>).</summary>
    public const double SeedSecondsPerMana = 0.09;

    /// <summary>The weight of a new sample in the exponentially weighted means (Lifestone's own: about the last five samples count).</summary>
    public const double Weight = 0.2;

    /// <summary>The time constant of the incoming damage rate, in seconds (Lifestone's own: about two tusker kills).</summary>
    public const double IncomingSeconds = 20;

    private double _incomingAt = double.NaN;
    private long _incomingTotal;

    /// <summary>The dance's seconds a mana point: <see cref="SeedSecondsPerMana"/>, then measured from landed dances (presses times their cast time, plus the Revitalize Self casts since the last dance, over the net mana).</summary>
    public double SecondsPerMana { get; private set; } = SeedSecondsPerMana;

    /// <summary>The heal's seconds a health point, or NaN until seeded (<see cref="SeedHeal"/>, from Drain Health's yield at the current skill) or measured (landed drains and heals: cast seconds plus their mana at the dance's rate, over the health gained).</summary>
    public double SecondsPerHealth { get; private set; } = double.NaN;

    /// <summary>The landed dances measured.</summary>
    public int ManaSamples { get; private set; }

    /// <summary>The landed drains and heals measured.</summary>
    public int HealthSamples { get; private set; }

    /// <summary>The damage the mage takes a second over the fight's long run (every attacker in the fight; <c>TODO(f10-incoming-per-attacker)</c>: ACE's notices name the attacker, the tally sums them): the picker's cost of the time a move takes. A danger read is <see cref="Read"/>.</summary>
    public double IncomingPerSecond { get; private set; }

    /// <summary>M6 7 Y: the fight's recent hits (fed by the fight's hit notices).</summary>
    public RecentHits Hits { get; } = new();

    /// <summary>M6 7 Y: what the recent hits say at <paramref name="clock"/>: the number the emergency reads, and the retreat rule on <c>HostileWatch</c>'s ring of the same hits.</summary>
    /// <param name="clock">The session clock.</param>
    public IncomingRead Read(double clock) => Hits.Read(clock);

    /// <summary>A landed dance: <paramref name="seconds"/> spent for <paramref name="netMana"/> gained. Ignored when either is not positive.</summary>
    /// <param name="seconds">The seconds of the presses and the refills since the last sample.</param>
    /// <param name="netMana">The mana gained net of the presses' cost.</param>
    public void NoteDance(double seconds, double netMana)
    {
        if (seconds > 0 && netMana > 0)
        {
            SecondsPerMana = Mean(SecondsPerMana, seconds / netMana, ManaSamples++);
        }
    }

    /// <summary>A landed drain or heal: <paramref name="seconds"/> all in (cast and mana) for <paramref name="health"/> gained. Ignored when either is not positive.</summary>
    /// <param name="seconds">The all-in seconds.</param>
    /// <param name="health">The health gained.</param>
    public void NoteHeal(double seconds, double health)
    {
        if (seconds > 0 && health > 0)
        {
            SecondsPerHealth = double.IsNaN(SecondsPerHealth) ? seconds / health : Mean(SecondsPerHealth, seconds / health, HealthSamples);
            HealthSamples++;
        }
    }

    /// <summary>Seeds the heal's rate while nothing is measured (the caller works it out from Drain Health's yield at the current skill).</summary>
    /// <param name="secondsPerPoint">The seed.</param>
    public void SeedHeal(double secondsPerPoint)
    {
        if (HealthSamples == 0 && secondsPerPoint > 0 && !double.IsInfinity(secondsPerPoint))
        {
            SecondsPerHealth = secondsPerPoint;
        }
    }

    /// <summary>The fight's damage taken so far at <paramref name="clock"/>: the incoming rate moves toward the damage since the last call over the time since.</summary>
    /// <param name="clock">The session clock.</param>
    /// <param name="totalTaken">The damage taken so far (the tally's).</param>
    public void NoteIncoming(double clock, long totalTaken)
    {
        if (double.IsNaN(_incomingAt) || totalTaken < _incomingTotal)
        {
            (_incomingAt, _incomingTotal) = (clock, totalTaken);
            return;
        }

        var dt = clock - _incomingAt;
        if (dt <= 0)
        {
            return;
        }

        var a = 1.0 - Math.Exp(-dt / IncomingSeconds);
        IncomingPerSecond += a * (((totalTaken - _incomingTotal) / dt) - IncomingPerSecond);
        (_incomingAt, _incomingTotal) = (clock, totalTaken);
    }

    private static double Mean(double mean, double sample, int before) => before == 0 ? sample : mean + (Weight * (sample - mean));
}

/// <summary>
/// The live snapshot the picker reads at each decision (D90): the mage's vitals and live skills, the payable levels, the target in reach with its live health, distance and line, the debuffs we landed on it, and the bolt and F9's best war
/// option as the fight has them. Filled by the fight each decision without allocating (the strings are the spells' own names).
/// </summary>
public struct FightSnapshot
{
    /// <summary>The mage's health, stamina and mana, and their maxima.</summary>
    public uint Health, HealthMax, Stamina, StaminaMax, Mana, ManaMax;

    /// <summary>The live skills (buffs up), as the cast policy reads them.</summary>
    public double WarSkill, LifeSkill, CreatureSkill, ManaConversion, MeleeDefense;

    /// <summary>The level the debuffs are scored at: the fight's plan cap for the bolt now, at most 7 (M6 1 f: 1 to 7; 0: none). The drains as an attack stay at VI and VII.</summary>
    public int DebuffLevel;

    /// <summary>The debuff kinds the target's profile entry allows (its <c>opener</c>), a bit per <see cref="DebuffKind"/>.</summary>
    public uint AllowedDebuffs;

    /// <summary>M6 1 f: true when <see cref="KnownDebuffs"/> was read from the spellbook (false: every kind counts as known, the unit tests' default).</summary>
    public bool BookRead;

    /// <summary>M6 1 f: a bit per <see cref="DebuffKind"/> whose spell at <see cref="DebuffLevel"/> the spellbook holds and whose school is trained; a kind without it is skipped, named once.</summary>
    public uint KnownDebuffs;

    /// <summary>True when Drain Health Other VI (or VII) is offered: known, paid for by the pack, the mana over its floor, the target in its range.</summary>
    public bool DrainVI, DrainVII;

    /// <summary>The target.</summary>
    public uint TargetGuid;

    /// <summary>The target's name.</summary>
    public string TargetName;

    /// <summary>The target's health as a share of its maximum (the last update).</summary>
    public double TargetHealthFraction;

    /// <summary>M5 7 F20: where <see cref="TargetHealthFraction"/> came from (<see cref="TargetHealthModel.Name"/>: pushed, queried, modelled; null reads as unknown) and its age in seconds (the decide line names both).</summary>
    public string? TargetHealthSource;

    /// <summary>The seconds since the target's health was last set by the server or by our own cast.</summary>
    public double TargetHealthAge;

    /// <summary>M5 7 F20: true when the target's health was never told and nothing was dealt to it: the drains are not allowed (an unknown health is never taken as full).</summary>
    public bool TargetHealthUnknown;

    /// <summary>M5 7 F20: when the target's last drain gave too little (<see cref="TargetHealthModel.NoteYield"/>), the reason ("drain off on 0x...: last gave 4"); drains on it are not allowed. Null: on.</summary>
    public string? DrainOffWhy;

    /// <summary>The target's maximum health and Magic Defense (the ground profile's; 0: unknown).</summary>
    public double TargetMaxHealth, TargetMagicDefense;

    /// <summary>
    /// M6 14 drain resistance: the target kind's health-drain resistance (<c>Vitals.DrainResist.Of</c>, its <c>ResistHealthDrain</c>), which scales a drain's take and a Harm Other's roll (<see cref="DebuffTarget.HealthDrainResistance"/>);
    /// null reads as 1.
    /// </summary>
    public double? TargetHealthDrainResistance;

    /// <summary>The target's stamina (0: unknown).</summary>
    public double TargetStamina;

    /// <summary>The target's attributes and attack skill, when a source gives them (0: unknown; the debuffs that read them are skipped, named).</summary>
    public double TargetStrength, TargetEndurance, TargetQuickness, TargetCoordination, TargetFocus, TargetSelf, TargetAttackSkill, TargetSwingSeconds, TargetPowerupSeconds;

    /// <summary>The distance to the target in metres, and whether the line is clear.</summary>
    public double Distance;

    /// <summary>True when the line to the target is clear.</summary>
    public bool LineClear;

    /// <summary>The bolt in hand: its name, its mean damage a landed cast (no vulnerability in it), its all-in cast seconds (gesture, overhead, flight), its base mana and power.</summary>
    public string BoltName;

    /// <summary>The bolt's mean damage a landed cast, before our vulnerability; 0: unknown.</summary>
    public double BoltMeanDamage, BoltCastSeconds;

    /// <summary>The bolt's dat base mana and power.</summary>
    public uint BoltBaseMana, BoltPower;

    /// <summary>The bolt's level and F9's war option's level (1 to 8; 0: unknown).</summary>
    public int BoltLevel, WarLevel;

    /// <summary>The cast seconds of a debuff or drain (the cast policy's gesture and overhead).</summary>
    public double DebuffCastSeconds;

    /// <summary>F9's best war option other than the bolt (null: none): its name, its seconds a kill and the bolt's (<see cref="WarSpellPicker.Choose"/>), its expected mana.</summary>
    public string? WarName;

    /// <summary>F9's seconds a kill of <see cref="WarName"/> and of the bolt.</summary>
    public double WarSeconds, WarBoltSeconds, WarMana;

    /// <summary>M5 7 F22: the ring's measured hit factor (<see cref="RingHitFactor"/>) while the ring is among the options, else 0; the decide line shows it.</summary>
    public double RingFactor;
}

/// <summary>The picker's choice.</summary>
/// <param name="Kind">The move.</param>
/// <param name="Debuff">The debuff or drain kind (for <see cref="FightMoveKind.Debuff"/> and <see cref="FightMoveKind.DrainHealth"/>).</param>
/// <param name="Level">The move's level (1 to 8; 0: unknown).</param>
/// <param name="Spell">The spell's name.</param>
/// <param name="AllIn">Its expected all-in seconds to finish the target.</param>
/// <param name="BoltAllIn">The bolt's.</param>
/// <param name="Hit">The chance it lands.</param>
/// <param name="Mana">Its expected mana.</param>
/// <param name="Why">A short reason (a constant).</param>
public readonly record struct FightPick(FightMoveKind Kind, DebuffKind Debuff, int Level, string Spell, double AllIn, double BoltAllIn, double Hit, double Mana, string Why);

/// <summary>The emergency's finishing bolt, read by <see cref="FightPicker.Finishing"/>.</summary>
/// <param name="Wins">True when the bolt beats the heal.</param>
/// <param name="Why">A short reason (a constant).</param>
/// <param name="Remaining">The target's health the bolt must take: its live health less the projectiles in flight.</param>
/// <param name="Expected">The bolt's expected damage: its mean hit, our vulnerability and the chance it lands.</param>
/// <param name="Landing">The chance it lands (the picker's war landing).</param>
/// <param name="Survives">True when the mage is expected to live through the cast.</param>
/// <param name="HealSaves">True when the best heal outruns the incoming and the mage lives through its cast.</param>
public readonly record struct FinishRead(bool Wins, string Why, double Remaining, double Expected, double Landing, bool Survives, bool HealSaves);

/// <summary>
/// The fight's one live picker (M5 7 F10, D90, the owner's option C): each decision it scores every move the mage may make now in expected all-in seconds to finish the target in reach, and the least wins; a tie keeps the bolt. Engine-free and
/// allocation-free per decision (reused arrays; the strings are built only when the pick changes and once per debuff for its note).
/// </summary>
/// <remarks>
/// <para><b>The score.</b> The bolt's all-in seconds are <see cref="FightDebuffs.FightSeconds"/> (the casts it needs at its landing chance, each its cast seconds, its mana at the dance's seconds a mana point, and the damage taken over it at
/// the heal's seconds a health point). A debuff or a drain is valued by <see cref="FightDebuffs.Evaluate"/>; its all-in seconds are the bolt's minus its net seconds (what it saves over the rest of the fight, less its own cast, mana and the blows
/// taken while casting). F9's war option is scaled by its seconds a kill against the bolt's (<see cref="WarSpellPicker.Choose"/>), so one scale holds every move.</para>
/// <para><b>Missing data</b> skips a move with one loud TODO line, never a guess: no bolt numbers or no target health means the bolt (the profile's opener stays the default, the caller's rule); a debuff that reads an attribute the source
/// does not give is skipped.</para>
/// <para><b>The floors</b> of D88 (stamina, mana, the drain-health floor) stay above the picker, in the fight; the picker chooses only when no floor forces a move. <see cref="StaminaDrainWins"/> is the stamina floor's own choice between
/// Revitalize Self and Drain Stamina Other, by the same seconds.</para>
/// </remarks>
public sealed class FightPicker
{
    /// <summary>The ledger's size: the debuffs we landed that are still remembered.</summary>
    public const int LedgerSize = 32;

    /// <summary>The spells the <c>picks=</c> field counts at most.</summary>
    public const int CountedMax = 24;

    private const double TieSeconds = 1e-9;
    private const string WhyBolt = "nothing beats the bolt";
    private const string WhyWar = "the war option kills faster here (F9)";
    private const string WhyVulnerability = "the vulnerability multiplies every bolt that follows";
    private const string WhyDebuff = "it saves more than it costs over the fight";
    private const string WhyDrain = "it takes nearly a bolt of health and heals the mage";
    private const string WhyFinishNoData = "not read: the bolt's numbers or the target's health are not known; the heal stands";
    private const string WhyFinishInFlight = "the projectiles in flight are expected to finish it; the heal stands";
    private const string WhyFinishShort = "one bolt is not expected to finish it; the heal stands";
    private const string WhyFinishSurvives = "the bolt kills the only attacker and the mage lives through the cast";
    private const string WhyFinishNoHeal = "no heal can save the mage: a dead attacker stops the incoming";
    private const string WhyFinishHeal = "the mage does not live through the bolt's cast and a heal does: the heal stands";
    private const string WhyNoData = "the bolt: its numbers or the target's maximum health are not known (named once for this kind of target, f10-picker-data)";

    private static readonly int Kinds = Enum.GetValues<DebuffKind>().Length;
    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII"];

    private readonly ILogSink _log;
    private readonly string _category;
    private readonly bool[] _noted = new bool[Enum.GetValues<DebuffKind>().Length];
    private readonly uint[] _ledgerGuid = new uint[LedgerSize];
    private readonly DebuffKind[] _ledgerKind = new DebuffKind[LedgerSize];
    private readonly double[] _ledgerMagnitude = new double[LedgerSize];
    private readonly double[] _ledgerUntil = new double[LedgerSize];
    private readonly string[] _countedName = new string[CountedMax];
    private readonly string[] _countedToken = new string[CountedMax];
    private readonly int[] _counted = new int[CountedMax];
    private int _countedN;
    private int _ledgerNext;
    private string _lastSpell = string.Empty;
    private uint _lastGuid;
    private readonly HashSet<string> _noDataKinds = new(StringComparer.Ordinal); // M6 1 f: the target kinds whose missing data was named
    private const int CandidateCap = 48;
    private readonly string[] _candName = new string[CandidateCap];
    private readonly double[] _candSeconds = new double[CandidateCap];
    private readonly bool[] _candAllowed = new bool[CandidateCap];
    private readonly System.Text.StringBuilder _decisionText = new();
    private int _candN;

    /// <summary>
    /// M5 7 F16 (the owner: log basically everything): true writes one <c>decide:</c> line at EVERY decision (never only when the pick changes), with every candidate's all-in seconds, the chosen move, why and the emergency line. The
    /// fight sets it; the line allocates its text, so the allocation-free <see cref="Decide"/> of the unit tests keeps it off.
    /// </summary>
    public bool LogEveryDecision { get; set; }

    /// <summary>The emergency line in health points, as the fight last worked it out (printed on the <c>decide:</c> line; 0 when off).</summary>
    public double EmergencyLine { get; set; }

    /// <summary>A picker logging to <paramref name="log"/>.</summary>
    /// <param name="log">The log.</param>
    /// <param name="category">The log category.</param>
    public FightPicker(ILogSink log, string category)
    {
        (_log, _category) = (log ?? throw new ArgumentNullException(nameof(log)), category);
    }

    /// <summary>The two live rates and the incoming damage.</summary>
    public FightRates Rates { get; } = new();

    /// <summary>M6 1 f: the debuff rows the picker scores (<see cref="DebuffRows.Standard"/>: VI and VII; the fight sets the SpellTable's I to VII, <see cref="DebuffRows.FromSpellTable"/>).</summary>
    public DebuffRows Rows { get; set; } = DebuffRows.Standard;

    /// <summary>The last pick.</summary>
    public FightPick Last { get; private set; }

    /// <summary>
    /// M6 1 f (<c>f10-picker-data</c>): the bolt's numbers from the bolt the fight will cast when no war option was built: its name, base mana and power are the SpellTable's, its cast seconds the bolt gesture and overhead
    /// (<see cref="WarSpellData.EffectSeconds"/>, the item 0 measure) plus the flight, its level and mean damage the projectile row's (<see cref="WarSpellPicker.MeanHit"/>). True when the mean damage was filled; false leaves it 0 (the
    /// spell has no projectile row: the picker names the gap once for the target kind).
    /// </summary>
    /// <param name="s">The snapshot.</param>
    /// <param name="picker">F9's picker (its estimates).</param>
    /// <param name="id">The bolt's spell id.</param>
    /// <param name="name">Its name.</param>
    /// <param name="baseMana">Its dat base mana.</param>
    /// <param name="power">Its dat power.</param>
    /// <param name="warSkill">The current War Magic skill.</param>
    /// <param name="distance">The target's distance in metres.</param>
    public static bool FillBolt(ref FightSnapshot s, WarSpellPicker picker, uint id, string name, uint baseMana, uint power, uint warSkill, double distance)
    {
        ArgumentNullException.ThrowIfNull(picker);
        (s.BoltName, s.BoltBaseMana, s.BoltPower) = (name, baseMana, power);
        s.BoltCastSeconds = WarSpellData.EffectSeconds(WarKind.Bolt) + picker.Settings.CastOverheadSeconds;
        if (!WarSpellData.TryGet(id, out var row))
        {
            return false;
        }

        var option = new WarOption(name, name, row, power, baseMana, false, 0, WarSpellData.EffectSeconds(row.Kind));
        s.BoltMeanDamage = picker.MeanHit(option, warSkill);
        s.BoltCastSeconds = option.EffectSeconds + picker.Settings.CastOverheadSeconds + (row.SpeedMetresPerSecond > 0 ? distance / row.SpeedMetresPerSecond : 0);
        s.BoltLevel = row.Level;
        return true;
    }

    /// <summary>The bit of <paramref name="kind"/> in <see cref="FightSnapshot.AllowedDebuffs"/>.</summary>
    /// <param name="kind">The kind.</param>
    public static uint Bit(DebuffKind kind) => 1u << (int)kind;

    /// <summary>A debuff we landed on <paramref name="guid"/>, until <paramref name="until"/> (the spell's duration from the landing).</summary>
    /// <param name="guid">The target.</param>
    /// <param name="kind">The debuff.</param>
    /// <param name="magnitude">Its magnitude (Fire Vulnerability: the multiplier).</param>
    /// <param name="until">When it lapses, on the session clock.</param>
    public void NoteLanded(uint guid, DebuffKind kind, double magnitude, double until)
    {
        for (var i = 0; i < LedgerSize; i++)
        {
            if (_ledgerGuid[i] == guid && _ledgerKind[i] == kind)
            {
                (_ledgerMagnitude[i], _ledgerUntil[i]) = (Math.Max(magnitude, _ledgerMagnitude[i]), Math.Max(until, _ledgerUntil[i]));
                return;
            }
        }

        var at = _ledgerNext;
        _ledgerNext = (_ledgerNext + 1) % LedgerSize;
        (_ledgerGuid[at], _ledgerKind[at], _ledgerMagnitude[at], _ledgerUntil[at]) = (guid, kind, magnitude, until);
    }

    /// <summary>The Fire Vulnerability multiplier we have on <paramref name="guid"/> at <paramref name="clock"/> (1: none).</summary>
    /// <param name="guid">The target.</param>
    /// <param name="clock">The session clock.</param>
    public double VulnerabilityOn(uint guid, double clock)
    {
        var m = 1.0;
        for (var i = 0; i < LedgerSize; i++)
        {
            if (_ledgerGuid[i] == guid && _ledgerKind[i] == DebuffKind.FireVulnerability && clock < _ledgerUntil[i])
            {
                m = Math.Max(m, _ledgerMagnitude[i]);
            }
        }

        return m;
    }

    /// <summary>The caster as <see cref="FightDebuffs"/> reads it, from the snapshot and the rates.</summary>
    /// <param name="s">The snapshot.</param>
    /// <param name="vulnerability">Our vulnerability on the target now.</param>
    public DebuffCaster CasterOf(in FightSnapshot s, double vulnerability) => new()
    {
        CreatureSkill = s.CreatureSkill,
        LifeSkill = s.LifeSkill,
        WarSkill = s.WarSkill,
        ManaConversion = s.ManaConversion,
        MeleeDefense = s.MeleeDefense,
        MissingHealth = s.HealthMax > s.Health ? s.HealthMax - s.Health : 0,
        MissingStamina = s.StaminaMax > s.Stamina ? s.StaminaMax - s.Stamina : 0,
        SecondsPerMana = Rates.SecondsPerMana,
        SecondsPerHealth = HealthRate,
        SecondsPerStamina = Rates.SecondsPerMana, // a stamina point is a mana point through Stamina to Mana, at the dance's rate
        DebuffCastSeconds = s.DebuffCastSeconds,
        WarMeanDamage = s.BoltMeanDamage * vulnerability,
        WarCastSeconds = s.BoltCastSeconds,
        WarBaseMana = s.BoltBaseMana,
        WarPower = s.BoltPower,
    };

    /// <summary>The target as <see cref="FightDebuffs"/> reads it.</summary>
    /// <param name="s">The snapshot.</param>
    /// <param name="vulnerability">Our vulnerability on it now.</param>
    public DebuffTarget TargetOf(in FightSnapshot s, double vulnerability) => new()
    {
        RemainingHealth = s.TargetMaxHealth * Math.Clamp(s.TargetHealthFraction, 0, 1),
        MaxHealth = s.TargetMaxHealth,
        Stamina = s.TargetStamina,
        Strength = s.TargetStrength,
        Endurance = s.TargetEndurance,
        Quickness = s.TargetQuickness,
        Coordination = s.TargetCoordination,
        Focus = s.TargetFocus,
        Self = s.TargetSelf,
        MagicDefense = s.TargetMagicDefense,
        AttackSkill = s.TargetAttackSkill,
        IncomingDamagePerSecond = Rates.IncomingPerSecond,
        SwingSeconds = s.TargetSwingSeconds,
        PowerupSeconds = s.TargetPowerupSeconds,
        FireVulnerabilityMultiplier = vulnerability,
        HealthDrainResistance = s.TargetHealthDrainResistance ?? 1.0, // M6 14 drain resistance
    };

    private double HealthRate => double.IsNaN(Rates.SecondsPerHealth) ? 0 : Rates.SecondsPerHealth;

    /// <summary>
    /// The decision: every move scored in all-in seconds, the least wins, a tie keeps the bolt. Logs one line when the pick changes and once per debuff why it does not win. Allocation-free once the notes are written.
    /// </summary>
    /// <param name="s">The snapshot, read now.</param>
    /// <param name="clock">The session clock (the ledger's).</param>
    public FightPick Decide(in FightSnapshot s, double clock)
    {
        if (s.BoltMeanDamage <= 0 || s.BoltCastSeconds <= 0 || s.TargetMaxHealth <= 0)
        {
            var kindName = s.TargetName ?? "creature";
            if (!_noDataKinds.Contains(kindName) && _noDataKinds.Add(kindName))
            {
                // M6 1 f: once per kind of target, naming what is missing (item 0's climb wrote it on every decision: 130 lines).
                _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"TODO(f10-picker-data): {kindName}: {(s.BoltMeanDamage <= 0 ? "the bolt's mean damage " : string.Empty)}{(s.BoltCastSeconds <= 0 ? "the bolt's cast seconds " : string.Empty)}{(s.TargetMaxHealth <= 0 ? "the target's maximum health (no ground row, no appraisal answer) " : string.Empty)}missing; the bolt, unscored, for this kind of target"));
            }

            return Settle(new FightPick(FightMoveKind.Bolt, default, s.BoltLevel, s.BoltName ?? "bolt", double.NaN, double.NaN, double.NaN, 0, WhyNoData), in s);
        }

        _candN = 0;
        var vulnerability = VulnerabilityOn(s.TargetGuid, clock);
        var caster = CasterOf(in s, vulnerability);
        var target = TargetOf(in s, vulnerability);
        var warLanding = FightDebuffs.Chance(s.WarSkill, s.TargetMagicDefense);
        var bolt = FightDebuffs.FightSeconds(in caster, in target, target.RemainingHealth, warLanding, 1.0, 1.0);
        var boltMana = ManaConversionModel.ExpectedCost(s.BoltBaseMana, s.BoltPower, (uint)Math.Max(0, Math.Round(s.ManaConversion)));
        var best = new FightPick(FightMoveKind.Bolt, default, s.BoltLevel, s.BoltName ?? "bolt", bolt, bolt, warLanding, boltMana, WhyBolt);
        Candidate(s.BoltName ?? "bolt", bolt, true);

        // F9's war option, on the same scale.
        if (s.WarName is { } war && s.WarBoltSeconds > 0 && s.WarSeconds > 0)
        {
            var allIn = bolt * (s.WarSeconds / s.WarBoltSeconds);
            Candidate(war, allIn, true);
            if (allIn < best.AllIn - TieSeconds)
            {
                best = new FightPick(FightMoveKind.War, default, s.WarLevel, war, allIn, bolt, warLanding, s.WarMana, WhyWar);
            }
        }

        // The debuffs and the drains through F11's value model, at the fight's cap (M6 1 f: every level I to VII the rows hold, the spellbook's).
        if (s.DebuffLevel >= 1)
        {
            var level = Math.Min(s.DebuffLevel, 7);
            for (var k = 0; k < Kinds; k++)
            {
                var kind = (DebuffKind)k;
                if (kind == DebuffKind.DrainStaminaOther)
                {
                    continue; // a refill only (F11: it does not pay as an attack): StaminaDrainWins
                }

                if (kind == DebuffKind.DrainHealthOther)
                {
                    var drainOn = s.DrainOffWhy is null && !s.TargetHealthUnknown; // M5 7 F20: a drain is never scored as castable on a stale yield or an unknown health
                    if (level >= 6)
                    {
                        Consider(ref best, in s, in caster, in target, bolt, kind, 6, s.DrainVI && drainOn); // the drain as an attack is offered at VI and VII only (FillDrainOffer)
                        Consider(ref best, in s, in caster, in target, bolt, kind, 7, s.DrainVII && level >= 7 && drainOn);
                    }

                    continue;
                }

                if (s.BookRead && (s.KnownDebuffs & Bit(kind)) == 0)
                {
                    if (!_noted[k])
                    {
                        Note(kind, string.Create(CultureInfo.InvariantCulture, $"pick: {Rows.Find(kind, level)?.Name ?? kind + " level " + Numeral(level)} is skipped: the spellbook does not hold it or its school is not trained (M6 1 f)"));
                    }

                    continue;
                }

                if (NeedsAttributes(kind) && s.TargetStrength <= 0)
                {
                    if (!_noted[k])
                    {
                        Note(kind, "pick: TODO(f10-target-attributes): " + kind + " reads the target's attributes, which the ground's profile gives for no such creature (its attributes section); skipped");
                    }

                    continue;
                }

                if (NeedsAttackSkill(kind) && s.TargetAttackSkill <= 0)
                {
                    if (!_noted[k])
                    {
                        Note(kind, "pick: TODO(m6-target-attack-skill): " + kind + " reads the target's attack skill, which the profile does not carry (its attributes are not the skill); skipped");
                    }

                    continue;
                }

                Consider(ref best, in s, in caster, in target, bolt, kind, level, (s.AllowedDebuffs & Bit(kind)) != 0);
            }
        }

        return Settle(best, in s);
    }

    /// <summary>
    /// M6 7 Y: the emergency's question, on the picker's own numbers (the target's live health as <see cref="TargetOf"/> reads it, the bolt's mean, our vulnerability, the war landing <see cref="Decide"/> scores with): is the target's health at or under
    /// the bolt's expected damage at the chance it lands? If so the bolt wins when the mage is expected to live through the cast, and also when no heal can save her anyway (<paramref name="healPerSecond"/>, the emergency's best score, does not outrun
    /// the incoming, or she does not live through the heal's own cast): a dead attacker stops the incoming. A health that is not known never finishes (an unknown is never taken as low). Engine-free; the caller says the target is the only attacker.
    /// </summary>
    /// <param name="s">The snapshot of the target.</param>
    /// <param name="read">The recent hits.</param>
    /// <param name="healPerSecond">The best heal's health a second (<see cref="HealPick"/>'s; 0: nothing payable).</param>
    /// <param name="healSeconds">That heal's cast seconds.</param>
    /// <param name="inFlight">The damage the mage's projectiles in flight are expected to deal the target.</param>
    /// <param name="clock">The session clock (the ledger's).</param>
    public FinishRead Finishing(in FightSnapshot s, in IncomingRead read, double healPerSecond, double healSeconds, double inFlight, double clock)
    {
        if (s.BoltMeanDamage <= 0 || s.BoltCastSeconds <= 0 || s.TargetMaxHealth <= 0 || s.TargetHealthUnknown)
        {
            return new FinishRead(false, WhyFinishNoData, 0, 0, 0, false, false);
        }

        var vulnerability = VulnerabilityOn(s.TargetGuid, clock);
        var target = TargetOf(in s, vulnerability);
        var remaining = Math.Max(0, target.RemainingHealth - Math.Max(0, inFlight));
        var landing = FightDebuffs.Chance(s.WarSkill, s.TargetMagicDefense);
        var expected = s.BoltMeanDamage * vulnerability * landing;
        var survives = read.Survives(s.Health, s.BoltCastSeconds);
        var healSaves = healPerSecond > read.PerSecond && read.Survives(s.Health, healSeconds);
        if (remaining <= 0)
        {
            return new FinishRead(false, WhyFinishInFlight, remaining, expected, landing, survives, healSaves);
        }

        if (remaining > expected)
        {
            return new FinishRead(false, WhyFinishShort, remaining, expected, landing, survives, healSaves);
        }

        return survives
            ? new FinishRead(true, WhyFinishSurvives, remaining, expected, landing, survives, healSaves)
            : !healSaves
                ? new FinishRead(true, WhyFinishNoHeal, remaining, expected, landing, survives, healSaves)
                : new FinishRead(false, WhyFinishHeal, remaining, expected, landing, survives, healSaves);
    }

    /// <summary>
    /// The stamina floor's choice (D88 with the owner's addition): true when Drain Stamina Other on the target yields more stamina an all-in second than Revitalize Self (cast seconds, mana at the dance's rate and the blows taken while
    /// casting, over the expected stamina). False when the drain's data is missing (named once by the caller) or the target holds no stamina to take.
    /// </summary>
    /// <param name="s">The snapshot (the target in reach).</param>
    /// <param name="drain">The Drain Stamina Other level the mage may cast.</param>
    /// <param name="revitalizeGain">The stamina a landed Revitalize Self gives (its boost, cut to the stamina missing).</param>
    /// <param name="revitalizeChance">Its cast chance.</param>
    /// <param name="revitalizeMana">Its expected mana.</param>
    /// <param name="drainPerSecond">The drain's stamina an all-in second.</param>
    /// <param name="revitalizePerSecond">Revitalize Self's.</param>
    public bool StaminaDrainWins(in FightSnapshot s, in DebuffSpell drain, double revitalizeGain, double revitalizeChance, double revitalizeMana, out double drainPerSecond, out double revitalizePerSecond) =>
        StaminaDrainWinsMeasured(in s, in drain, revitalizeGain, revitalizeChance, revitalizeMana, 1.0, out drainPerSecond, out revitalizePerSecond, out _);

    /// <summary>
    /// M5 7 F15 (7-F4): <see cref="StaminaDrainWins"/> with the yield this target has shown: <paramref name="yieldShare"/> is the share of the expected
    /// gain the last drain on it measured (1: none yet, or as expected), which scales the drain's stamina a second; <paramref name="gainIfLanded"/> is the stamina one landed cast is expected to give (the measure's yardstick).
    /// </summary>
    /// <param name="s">The snapshot (the target in reach).</param>
    /// <param name="drain">The Drain Stamina Other level the mage may cast.</param>
    /// <param name="revitalizeGain">The stamina a landed Revitalize Self gives (its boost, cut to the stamina missing).</param>
    /// <param name="revitalizeChance">Its cast chance.</param>
    /// <param name="revitalizeMana">Its expected mana.</param>
    /// <param name="yieldShare">The measured share of the expected gain (0 to 1; 1 when not measured).</param>
    /// <param name="drainPerSecond">The drain's stamina an all-in second, scaled by the measured share.</param>
    /// <param name="revitalizePerSecond">Revitalize Self's.</param>
    /// <param name="gainIfLanded">The stamina a landed drain is expected to give.</param>
    public bool StaminaDrainWinsMeasured(in FightSnapshot s, in DebuffSpell drain, double revitalizeGain, double revitalizeChance, double revitalizeMana, double yieldShare, out double drainPerSecond, out double revitalizePerSecond, out double gainIfLanded)
    {
        var caster = CasterOf(in s, 1.0);
        var target = TargetOf(in s, 1.0);
        var blows = s.DebuffCastSeconds * Rates.IncomingPerSecond * HealthRate;
        var revitalizeSeconds = s.DebuffCastSeconds + blows + (revitalizeMana * Rates.SecondsPerMana);
        revitalizePerSecond = revitalizeSeconds > 0 ? revitalizeChance * revitalizeGain / revitalizeSeconds : 0;
        var v = FightDebuffs.Evaluate(in drain, in target, in caster);
        drainPerSecond = v.HasValue && v.CostSeconds > 0 ? Math.Clamp(yieldShare, 0, 1) * v.ExpectedStamina / v.CostSeconds : 0;
        gainIfLanded = v.HasValue && v.Landing > 0 ? v.ExpectedStamina / v.Landing : 0;
        return drainPerSecond > revitalizePerSecond + TieSeconds;
    }

    /// <summary>Counts a cast of the move named <paramref name="spell"/> for <c>picks=</c> (allocates the field's token once per spell).</summary>
    /// <param name="spell">The spell's name.</param>
    public void Count(string spell)
    {
        for (var i = 0; i < _countedN; i++)
        {
            if (ReferenceEquals(_countedName[i], spell) || string.Equals(_countedName[i], spell, StringComparison.Ordinal))
            {
                _counted[i]++;
                return;
            }
        }

        if (_countedN < CountedMax)
        {
            (_countedName[_countedN], _countedToken[_countedN], _counted[_countedN]) = (spell, spell.Replace(' ', '_').Replace("'", string.Empty, StringComparison.Ordinal), 1);
            _countedN++;
        }
    }

    /// <summary>The verdict field: <c>picks=Flame_Bolt_VI:40,Fire_Vulnerability_Other_VI:12</c> (report-only), or <c>picks=none</c>.</summary>
    public string Field()
    {
        if (_countedN == 0)
        {
            return "picks=none";
        }

        var parts = new string[_countedN];
        for (var i = 0; i < _countedN; i++)
        {
            parts[i] = _countedToken[i] + ":" + _counted[i].ToString(CultureInfo.InvariantCulture);
        }

        return "picks=" + string.Join(',', parts);
    }

    /// <summary>The kinds whose value reads the target's attack skill (the hit chance before and after): the Light Weapons formula's (Strength + Coordination) / 3 is only the part a debuff moves.</summary>
    private static bool NeedsAttackSkill(DebuffKind kind) => kind is DebuffKind.Weakness or DebuffKind.Clumsiness or DebuffKind.LightWeaponsIneptitude;

    /// <summary>
    /// M6 3 a2 (closes the source half of <c>TODO(f10-target-attributes)</c>): fills the snapshot's target attributes and swing from the ground profile's row for the creature, or zeroes them when the profile has none (the
    /// debuffs that read them are then skipped, named once). Each attribute feeds one ACE rule: Strength the melee damage mod (<c>SkillFormula.GetAttributeMod</c>, SkillFormula.cs:18-22 @95ef426d); Endurance the maximum health
    /// (AttributeFormula.cs:55-71, CreatureVital.cs:104-131); Quickness the attack animation speed (<c>GetAnimSpeed</c>, Creature_Combat.cs:510-523); Coordination, with Strength, the Light Weapons skill and, with Quickness, the Melee Defense
    /// (CreatureSkill.cs:154-163); Focus and Self, divided by 7, the Magic Defense our spells roll against (CreatureSkill.cs:154-163). The swing seconds are the profile's <c>swing_s</c> (Monster_Melee.cs:151-156), 0 when it has none
    /// (<c>TODO(f11-swing-seconds)</c>: Slowness is then reported unscored). The attack skill and the PowerupTime are not profile fields and stay as they were.
    /// </summary>
    /// <param name="s">The snapshot.</param>
    /// <param name="target">The ground's row for the creature, or null.</param>
    public static void FillTargetStats(ref FightSnapshot s, GroundTarget? target)
    {
        var a = target?.Attributes;
        (s.TargetStrength, s.TargetEndurance, s.TargetQuickness, s.TargetCoordination, s.TargetFocus, s.TargetSelf) = a is null ? (0, 0, 0, 0, 0, 0) : (a.Strength, a.Endurance, a.Quickness, a.Coordination, a.Focus, a.Self);
        s.TargetSwingSeconds = target?.SwingSeconds is { } swing && swing > 0 ? swing : 0;
    }

    private static bool NeedsAttributes(DebuffKind kind) => kind is DebuffKind.Weakness or DebuffKind.Clumsiness or DebuffKind.LightWeaponsIneptitude or DebuffKind.Slowness or DebuffKind.Bafflement or DebuffKind.Feeblemind or DebuffKind.Frailty;

    private void Consider(ref FightPick best, in FightSnapshot s, in DebuffCaster caster, in DebuffTarget target, double bolt, DebuffKind kind, int level, bool castable)
    {
        if (Rows.Find(kind, level) is not { } spell)
        {
            if (!_noted[(int)kind])
            {
                Note(kind, string.Create(CultureInfo.InvariantCulture, $"pick: {kind} level {Numeral(level)} has no row (the SpellTable lacks it: DebuffRows.FromSpellTable named it); not scored"));
            }

            return;
        }

        var v = FightDebuffs.Evaluate(in spell, in target, in caster);
        if (!v.HasValue)
        {
            if (!_noted[(int)kind] && kind is not (DebuffKind.FireVulnerability or DebuffKind.DrainHealthOther))
            {
                // A structural no-value (Imperil, Exhaustion, the defence debuffs, a missing swing) once; Fire Vulnerability already on and a drain at full health are the moment's, not the move's.
                Note(kind, "pick: " + spell.Name + " never wins: " + v.Why);
            }

            return;
        }

        var allIn = bolt - v.NetSeconds;
        Candidate(spell.Name, allIn, castable);
        if (castable && allIn < best.AllIn - TieSeconds)
        {
            var why = kind == DebuffKind.DrainHealthOther ? WhyDrain : kind == DebuffKind.FireVulnerability ? WhyVulnerability : WhyDebuff;
            best = new FightPick(kind == DebuffKind.DrainHealthOther ? FightMoveKind.DrainHealth : FightMoveKind.Debuff, kind, level, spell.Name, allIn, bolt, v.Landing, v.ManaCost, why);
        }
        else if (!v.Pays && !_noted[(int)kind])
        {
            Note(kind, string.Create(CultureInfo.InvariantCulture, $"pick: {spell.Name} does not win here: it saves {v.SavedSeconds:0.0} s and costs {v.CostSeconds:0.0} s (net {v.NetSeconds:0.0} s) against the bolt's {bolt:0.0} s on {s.TargetName} at {target.RemainingHealth:0} health{(castable ? string.Empty : " (and it is not a move this target's profile entry allows)")}"));
        }
    }

    private void Note(DebuffKind kind, string text)
    {
        if (!_noted[(int)kind])
        {
            _noted[(int)kind] = true;
            _log.Info(_category, text);
        }
    }

    private void Candidate(string name, double seconds, bool allowed)
    {
        if (_candN < CandidateCap)
        {
            (_candName[_candN], _candSeconds[_candN], _candAllowed[_candN]) = (name, seconds, allowed);
            _candN++;
        }
    }

    private void LogDecision(in FightPick pick, in FightSnapshot s)
    {
        var sb = _decisionText.Clear();
        sb.Append(CultureInfo.InvariantCulture, $"decide: {pick.Spell} on {s.TargetName ?? "creature"} 0x{s.TargetGuid:X8} (health {s.TargetHealthFraction:P0} {(s.TargetHealthUnknown ? "unknown" : s.TargetHealthSource ?? "unread")}{(double.IsFinite(s.TargetHealthAge) && !s.TargetHealthUnknown ? string.Create(CultureInfo.InvariantCulture, $" {s.TargetHealthAge:0.0} s ago") : string.Empty)}, {s.Distance:0.#} m{(s.DrainOffWhy is { } off ? "; " + off : s.TargetHealthUnknown ? "; drain not allowed: the health is unknown" : string.Empty)}) | scores (all-in s):");
        for (var i = 0; i < _candN; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $" {_candName[i]}={_candSeconds[i]:0.0}{(_candAllowed[i] ? string.Empty : "(not allowed here)")};");
        }

        sb.Append(CultureInfo.InvariantCulture, $" | chosen {pick.Kind} {pick.Spell} {pick.AllIn:0.0} s vs bolt {pick.BoltAllIn:0.0} s | why {pick.Why} | mage health {s.Health}/{s.HealthMax}, mana {s.Mana}/{s.ManaMax}, stamina {s.Stamina}/{s.StaminaMax} | incoming {Rates.IncomingPerSecond:0.0} a second | emergency line {EmergencyLine:0}");
        if (s.RingFactor > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $" | ring factor {s.RingFactor:0.00}"); // M5 7 F22: the measured hit factor the ring is scaled by
        }

        _log.Info(_category, sb.ToString());
    }

    private FightPick Settle(FightPick pick, in FightSnapshot s)
    {
        Last = pick;
        if (LogEveryDecision)
        {
            LogDecision(in pick, in s);
        }

        if (string.Equals(pick.Spell, _lastSpell, StringComparison.Ordinal) && (pick.Kind is FightMoveKind.Bolt or FightMoveKind.War || s.TargetGuid == _lastGuid))
        {
            return pick;
        }

        (_lastSpell, _lastGuid) = (pick.Spell, s.TargetGuid);
        _log.Info(
            _category,
            string.Create(
                CultureInfo.InvariantCulture,
                $"pick: {LineOf(pick.Spell, pick.Level)} {Numeral(pick.Level)} on {s.TargetName ?? "creature"} 0x{s.TargetGuid:X8} | all-in {pick.AllIn:0.0} s vs bolt {pick.BoltAllIn:0.0} s | hit {pick.Hit * 100:0}% | mana {pick.Mana:0.#} | why {pick.Why}"));
        return pick;
    }

    /// <summary>The spell's name without its trailing numeral, so the log line reads "Flame Bolt VI", not "Flame Bolt VI VI".</summary>
    private static string LineOf(string spell, int level)
    {
        var numeral = Numeral(level);
        return spell.Length > numeral.Length + 1 && spell.EndsWith(" " + numeral, StringComparison.Ordinal) ? spell[..^(numeral.Length + 1)] : spell;
    }

    private static string Numeral(int level) => level is >= 1 and <= 8 ? Numerals[level - 1] : "-";

}

/// <summary>
/// M6 3 a2 (the reader of <c>GroundTarget.KillFirst</c>): the fight's pick among the creatures in reach. A kill-first creature of the ground (<see cref="HuntingGround.IsKillFirst"/>) in reach is picked before the
/// others, the nearest of them; with none in reach the pick is today's nearest. The filtered list is rebuilt only when the list it filters is another object, so a pick allocates nothing.
/// </summary>
public sealed class KillFirstPick
{
    private readonly HuntingGround _ground;
    private IReadOnlyList<uint>? _source;
    private uint[] _first = [];

    /// <summary>A pick for <paramref name="ground"/>.</summary>
    /// <param name="ground">The ground whose kill-first targets come first.</param>
    public KillFirstPick(HuntingGround ground) => _ground = ground ?? throw new ArgumentNullException(nameof(ground));

    /// <summary>The nearest creature of <paramref name="wcids"/> within <paramref name="maxMetres"/>, a kill-first one first (then <see cref="WorldObjectTable.NearestCreature(IReadOnlyList{uint}, in CellFrame, float, Func{uint, bool}, IReadOnlyList{uint}, out uint, LivePositions)"/> over all).</summary>
    /// <param name="objects">The table.</param>
    /// <param name="wcids">The classes the pick may take (a dungeon's targets, an outdoor ground's band).</param>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="maxMetres">The largest distance accepted.</param>
    /// <param name="cellFilter">Takes an object's full cell id; null accepts every cell.</param>
    /// <param name="excluded">Ids never returned, or null.</param>
    /// <param name="guid">The pick, or 0.</param>
    /// <param name="live">Where a creature stands now (M6 14, <c>m6-live-position</c>: the session's <see cref="IGameSessionView.Live"/>), or null for the server's last report.</param>
    public bool Nearest(WorldObjectTable objects, IReadOnlyList<uint> wcids, in CellFrame centre, float maxMetres, Func<uint, bool>? cellFilter, IReadOnlyList<uint>? excluded, out uint guid, LivePositions? live = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (!ReferenceEquals(_source, wcids))
        {
            var n = 0;
            for (var i = 0; i < wcids.Count; i++)
            {
                n += _ground.IsKillFirst(wcids[i]) ? 1 : 0;
            }

            var first = new uint[n];
            n = 0;
            for (var i = 0; i < wcids.Count; i++)
            {
                if (_ground.IsKillFirst(wcids[i]))
                {
                    first[n++] = wcids[i];
                }
            }

            (_first, _source) = (first, wcids);
        }

        return (_first.Length > 0 && objects.NearestCreature(_first, in centre, maxMetres, cellFilter, excluded, out guid, live)) || objects.NearestCreature(wcids, in centre, maxMetres, cellFilter, excluded, out guid, live);
    }
}

// Her in-fight policy (docs/fight-sim.md, "With her heals"): which cast she makes next in a simulated fight, chosen by the client's OWN rules, called and not copied (M6 item 16, D139, the owner's ruling of 2026-11-17: one owner per automation decision).
// Plain C# with no ACE type, so Lifestone.Client.Tests compiles it too (InFightRulesTests). InFight.cs plays each chosen cast through ACE's own code on the real objects.
//
// The order of a decision is the fight's own (Session/Scenarios/FightScenario.cs Choose):
//   1. the emergency (FightScenario.Emergency.cs EmergencyStepOwn): FightEmergency's live heal line and its hysteresis, at the fight's default lines (FightOptions: floor 60%, safe 85%, margin 10%); the candidates as FightHealPick
//      builds them (Heal Self at the highest level cast at BuffOptions.DefaultCastChance, VitalsMeans.Pick; each Drain Health Other level at the fullest creature a drain takes from, FightEmergency.Fullest, ACE's transfer arithmetic
//      BuffStatMods.Transfer at its drain resistance); a wound-up level only when it fits the seconds her health lasts (FightScenario.EmergencySecondsMargin); the pick HealPick.ChooseNow;
//   2. the climber's fills (FightScenario.VitalsPolicy.cs ManaLoopByPolicy): VitalsPolicy.Decide on the ladder's lines (LadderLoader.WarMage.VitalsPolicy), its facts filled as TryPolicyFacts fills them (the presses she may try at
//      the mana order's try chance, ManaConversionModel.LandingChance; the health drains her mana holds the base of; one Drain Stamina Other level at BuffOptions.DefaultCastChance; each level's cast chance and a drain's share past its
//      source's resist and the creatures on her, M6 heal spend);
//   3. the bolt, at the target the kill order keeps (TargetSwitch.Decide, folded over the creatures on her: the most damage a second removed for each second of casting).
// What the fight does that is not played here is named where it would bite (InFight.cs Gaps): the finishing bolt over the emergency's heal (FightScenario.Emergency.cs FinishStep), the drain rule behind a set-aside emergency
// (FightScenario.Drain.cs), potions, the way out's mana reserve, the buffs' upkeep, the components' level floor.

using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.FightSim;

/// <summary>What she casts next.</summary>
internal enum FightMoveKind
{
    /// <summary>Her bolt at the target.</summary>
    Bolt,

    /// <summary>Heal Self.</summary>
    HealSelf,

    /// <summary>Drain Health Other at a creature.</summary>
    DrainHealth,

    /// <summary>Stamina to Mana Self.</summary>
    Press,

    /// <summary>Drain Stamina Other at a creature.</summary>
    DrainStamina,

    /// <summary>She can cast nothing that kills or gives mana: no bolt and no press her mana lets her try.</summary>
    OutOfMana,
}

/// <summary>How the fight is played.</summary>
internal enum FightMode
{
    /// <summary>The client's rules: the emergency's heals, the vitals policy's fills, the bolt; mana and stamina counted.</summary>
    Heals,

    /// <summary>Bolts only, mana not counted: the race's own model (Commands.DeathChance), played by the same engine as a check of it.</summary>
    BoltsOnly,

    /// <summary>The emergency's heals and the bolt, mana counted, the vitals policy's fills left out (a press only when the bolt cannot be tried): what the policy's fills add or cost, measured by their absence.</summary>
    EmergencyOnly,
}

/// <summary>One level of a spell she may cast in the fight, as the client's rules read it.</summary>
/// <param name="Id">The spell id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Level">Its level (1 to 8).</param>
/// <param name="BaseMana">Its base mana: the most a cast costs (GearCasts.ManaCeiling for a spell that is not an item enchantment).</param>
/// <param name="Power">Its power (the cast chance's difficulty and Mana Conversion's).</param>
/// <param name="Seconds">One cast's seconds on the server, plus the client's loop overhead when given.</param>
/// <param name="CastChance">Her chance the cast does not fizzle.</param>
/// <param name="Row">ACE's transfer row for a transfer (default for a boost).</param>
internal sealed record FightSpell(uint Id, string Name, int Level, uint BaseMana, uint Power, double Seconds, double CastChance, TransferRow Row);

/// <summary>A kind of creature on her, as the rules read it (the figures ACE gave for her and it).</summary>
/// <param name="Name">Its name.</param>
/// <param name="HealthMax">Its maximum health.</param>
/// <param name="StaminaMax">Its maximum stamina.</param>
/// <param name="ThreatPerSecond">Its damage a second on her (the race's sampled mean).</param>
/// <param name="Bolt">Her bolt on it (her fastest killer).</param>
/// <param name="BoltMeanLanded">The bolt's mean damage a landed cast.</param>
/// <param name="BoltSpread">The width of the bolt's plain roll (its largest plain hit less its smallest).</param>
/// <param name="BoltLand">The chance a bolt lands: the cast's and its resist's.</param>
/// <param name="HerDamagePerSecond">Her bolt's mean damage a second on it, fizzles and resists counted.</param>
/// <param name="DrainLand">The chance a Life spell of hers is not resisted by it.</param>
/// <param name="DrainResist">Its health-drain resistance: the share of a drain's take ACE lets through (0: a drain gives nothing).</param>
/// <param name="StaminaDrainResist">M6 undrainable fills: its stamina-drain resistance (ACE <c>GetResistanceMod(StaminaDrain)</c>; 0: a Drain Stamina Other gives nothing, an Armored Tusker's).</param>
internal sealed record FoeKind(string Name, double HealthMax, double StaminaMax, double ThreatPerSecond, FightSpell Bolt, double BoltMeanLanded, double BoltSpread, double BoltLand, double HerDamagePerSecond, double DrainLand,
    double DrainResist, double StaminaDrainResist = 1.0);

/// <summary>One creature on her now.</summary>
internal sealed class Foe
{
    /// <summary>A creature of <paramref name="kind"/>, fresh.</summary>
    /// <param name="kind">Its kind.</param>
    public Foe(FoeKind kind)
    {
        Kind = kind;
        Health = kind.HealthMax;
        Stamina = kind.StaminaMax;
    }

    /// <summary>Its kind.</summary>
    public FoeKind Kind { get; }

    /// <summary>Its health now.</summary>
    public double Health { get; set; }

    /// <summary>Its stamina now.</summary>
    public double Stamina { get; set; }

    /// <summary>True while it lives.</summary>
    public bool Alive => Health > 0;
}

/// <summary>Everything a decision reads: her bars, her spells, the creatures, the fight's tally and the fight's own heal line.</summary>
internal sealed class FightView
{
    /// <summary>The fight's clock, seconds from its start.</summary>
    public double Clock { get; set; }

    /// <summary>Her health now.</summary>
    public uint Health { get; set; }

    /// <summary>Her maximum health.</summary>
    public uint HealthMax { get; init; }

    /// <summary>Her stamina now.</summary>
    public uint Stamina { get; set; }

    /// <summary>Her maximum stamina.</summary>
    public uint StaminaMax { get; init; }

    /// <summary>Her mana now.</summary>
    public uint Mana { get; set; }

    /// <summary>Her maximum mana.</summary>
    public uint ManaMax { get; init; }

    /// <summary>Her Mana Conversion now (0: untrained).</summary>
    public uint ManaConversion { get; init; }

    /// <summary>Her health regeneration a second (ACE's heartbeat on her in the fight's stance).</summary>
    public double HealthRegen { get; init; }

    /// <summary>Her stamina regeneration a second.</summary>
    public double StaminaRegen { get; init; }

    /// <summary>Her mana regeneration a second.</summary>
    public double ManaRegen { get; init; }

    /// <summary>The Heal Self level the fight casts, or null.</summary>
    public FightSpell? HealSelf { get; init; }

    /// <summary>The Drain Health Other levels she knows, lowest first.</summary>
    public IReadOnlyList<FightSpell> HealthDrains { get; init; } = [];

    /// <summary>The Stamina to Mana Self levels she knows, lowest first.</summary>
    public IReadOnlyList<FightSpell> Presses { get; init; } = [];

    /// <summary>The Drain Stamina Other level the fight casts, or null.</summary>
    public FightSpell? StaminaDrain { get; init; }

    /// <summary>M6 stamina dance: the Revitalize Self levels the live refill may cast, lowest first, with their boost rows (the policy prices the stamina a press takes by them; not cast here).</summary>
    public IReadOnlyList<(FightSpell Spell, BoostRow Row)> Revitalizes { get; init; } = [];

    /// <summary>The creatures on her (the dead stay in the list).</summary>
    public IReadOnlyList<Foe> Foes { get; init; } = [];

    /// <summary>The creature she bolts (-1: none chosen yet).</summary>
    public int Target { get; set; } = -1;

    /// <summary>The attacks thrown at her so far (hits and evades).</summary>
    public double Attacks { get; set; }

    /// <summary>The damage she has taken so far.</summary>
    public double Damage { get; set; }

    /// <summary>The biggest hit she has taken so far.</summary>
    public double BiggestHit { get; set; }

    /// <summary>The recent hits (the fight's <c>Picker.Rates.Hits</c>): the emergency's incoming rate and the policy's burst.</summary>
    public RecentHits Hits { get; } = new();

    /// <summary>The emergency at the fight's default lines (<see cref="InFightRules.NewEmergency"/>).</summary>
    public FightEmergency Emergency { get; } = InFightRules.NewEmergency();

    /// <summary>The mana order's try chance: a cast is tried when ACE's Mana Conversion lets it land on the mana held at least this often (<see cref="ManaOrderOptions.DefaultTryChance"/>, D15).</summary>
    public double TryChance { get; init; } = ManaOrderOptions.DefaultTryChance;
}

/// <summary>A decision: what she casts, at whom, why, and whether it is the emergency's.</summary>
/// <param name="Kind">The move.</param>
/// <param name="Spell">The spell (null for <see cref="FightMoveKind.OutOfMana"/>).</param>
/// <param name="Foe">The creature it is cast at (-1: herself or none).</param>
/// <param name="Why">The rule that chose it.</param>
/// <param name="Emergency">True for the emergency's heal.</param>
internal readonly record struct FightMove(FightMoveKind Kind, FightSpell? Spell, int Foe, string Why, bool Emergency = false);

/// <summary>The decision, by the client's rules.</summary>
internal static class InFightRules
{
    private static readonly WarPickSettings WarPick = new();

    /// <summary>
    /// The client's war pick's price of a kill (<see cref="WarSpellPicker"/>, D89: the least seconds a kill wins, each cast costing its gesture, the overhead and the dance's seconds for its mana), on ACE's sampled kill: its casting
    /// seconds, each cast's <see cref="WarPickSettings.CastOverheadSeconds"/> and its mana at <see cref="WarPickSettings.DanceSecondsPerMana"/> (the picker's registered estimates). The fights with her heals bolt with the spell of
    /// the least price, as the fight's picker would; the race keeps the fastest killer.
    /// </summary>
    /// <param name="seconds">The kill's casting seconds.</param>
    /// <param name="casts">The kill's casts.</param>
    /// <param name="mana">The kill's mana.</param>
    public static double KillPrice(double seconds, double casts, double mana) => seconds + (casts * WarPick.CastOverheadSeconds) + (mana * WarPick.DanceSecondsPerMana);

    /// <summary>The fight's emergency at its default lines (<c>--fight-emergency-floor</c>, <c>-safe</c>, <c>-margin</c>: <see cref="FightOptions"/>).</summary>
    public static FightEmergency NewEmergency() => new(FightOptions.DefaultEmergencyFloorPercent, FightOptions.DefaultEmergencySafePercent, FightOptions.DefaultEmergencyMarginPercent);

    /// <summary>True when ACE's Mana Conversion lets <paramref name="spell"/> land on the mana she holds at least at the try chance (the mana order's rule, D15: <see cref="ManaConversionModel.LandingChance"/>).</summary>
    /// <param name="v">The view.</param>
    /// <param name="spell">The spell.</param>
    public static bool CanTry(FightView v, FightSpell spell)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(spell);
        return ManaConversionModel.LandingChance(spell.BaseMana, spell.Power, v.ManaConversion, v.Mana) >= v.TryChance;
    }

    /// <summary>
    /// The target she bolts: the current one while it lives (the fight changes target only on a kill or a new attacker); else the order of <see cref="TargetSwitch.Decide"/> folded over the living creatures, the first kept on a tie:
    /// the one whose damage a second on her over the seconds her bolts need to kill it is the most. -1 when none lives.
    /// </summary>
    /// <param name="v">The view.</param>
    public static int ChooseTarget(FightView v)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (v.Target >= 0 && v.Target < v.Foes.Count && v.Foes[v.Target].Alive)
        {
            return v.Target;
        }

        List<SwitchSide> living = [];
        for (var i = 0; i < v.Foes.Count; i++)
        {
            if (v.Foes[i].Alive)
            {
                living.Add(Side(v.Foes[i], i));
            }
        }

        var best = TargetSwitch.KillOrder(living, out _); // the live fight's own fold (FightScenario.OneTarget.cs): one rule, so the two pick alike (docs/session.md section 124)
        return best < 0 ? -1 : (int)living[best].Guid;
    }

    private static SwitchSide Side(Foe foe, int index) => new((uint)index, foe.Kind.Name, foe.Health, foe.Kind.HerDamagePerSecond, foe.Kind.ThreatPerSecond, "the race's sampled mean");

    /// <summary>The next cast. Moves nothing but the emergency's hysteresis and the target.</summary>
    /// <param name="v">The view.</param>
    /// <param name="mode">How the fight is played.</param>
    public static FightMove Decide(FightView v, FightMode mode)
    {
        ArgumentNullException.ThrowIfNull(v);
        v.Target = ChooseTarget(v);
        var target = v.Target;
        if (target < 0)
        {
            return new FightMove(FightMoveKind.OutOfMana, null, -1, "no creature lives");
        }

        var bolt = v.Foes[target].Kind.Bolt;
        if (mode == FightMode.BoltsOnly)
        {
            return new FightMove(FightMoveKind.Bolt, bolt, target, "bolts only");
        }

        if (Emergency(v) is { } heal)
        {
            return heal;
        }

        List<FightSpell> presses = [];
        List<FightSpell> drains = [];
        var (fullestHealth, fullestStamina) = (-1, -1);
        var choice = new VitalsChoice(VitalsMove.Bolt, -1, 0, 0, "the fills are left out");
        if (mode != FightMode.EmergencyOnly)
        {
            choice = VitalsPolicy.Decide(Facts(v, out presses, out drains, out fullestHealth, out fullestStamina), LadderLoader.WarMage.VitalsPolicy).Choice;
        }

        switch (choice.Move)
        {
            case VitalsMove.Press when choice.Index >= 0 && choice.Index < presses.Count:
                return new FightMove(FightMoveKind.Press, presses[choice.Index], -1, "vitals policy: " + choice.Why);
            case VitalsMove.DrainHealth when choice.Index >= 0 && choice.Index < drains.Count && fullestHealth >= 0:
                return new FightMove(FightMoveKind.DrainHealth, drains[choice.Index], fullestHealth, "vitals policy: " + choice.Why);
            case VitalsMove.DrainStamina when v.StaminaDrain is { } drain && fullestStamina >= 0 && CanTry(v, drain):
                return new FightMove(FightMoveKind.DrainStamina, drain, fullestStamina, "vitals policy: " + choice.Why);
        }

        if (CanTry(v, bolt))
        {
            return new FightMove(FightMoveKind.Bolt, bolt, target, "vitals policy: " + choice.Why);
        }

        // The bolt cannot be tried: the press the mana lets her try that gives the most (she presses before she stands empty), else she is out of mana.
        FightSpell? press = null;
        foreach (var p in v.Presses)
        {
            if (CanTry(v, p) && v.Stamina > 0)
            {
                press = p;
            }
        }

        return press is null
            ? new FightMove(FightMoveKind.OutOfMana, null, -1, "out of mana: no bolt and no press her mana lets her try")
            : new FightMove(FightMoveKind.Press, press, -1, "the bolt cannot be tried: the press first");
    }

    /// <summary>
    /// The emergency (FightScenario.Emergency.cs EmergencyStepOwn): null while it does not hold or no heal is offered. Under the line (or inside its hysteresis up to the safe line), each Drain Health Other level her mana pays the
    /// ceiling of at the fullest creature a drain takes from, and Heal Self, are scored by the health a landed cast gives her a second now (<see cref="HealPick.ChooseNow"/>); a tie goes to Heal Self.
    /// </summary>
    /// <param name="v">The view.</param>
    public static FightMove? Emergency(FightView v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var read = v.Hits.Read(v.Clock);
        if (!v.Emergency.Wanted(v.Health, v.HealthMax, read.PerSecond, v.Clock))
        {
            return null;
        }

        var missing = v.HealthMax > v.Health ? v.HealthMax - v.Health : 0u;
        var fullest = FullestDrainable(v);
        var candidates = new HealCandidate[8];
        Span<int> levelOf = stackalloc int[8];
        var count = 0;
        if (fullest >= 0)
        {
            var foe = v.Foes[fullest];
            var secondsLeft = v.Health / Math.Max(read.PerSecond, FightEmergency.SeedIncomingPerSecond);
            for (var i = 0; i < v.HealthDrains.Count && count < candidates.Length; i++)
            {
                var level = v.HealthDrains[i];
                if (v.Mana < level.BaseMana)
                {
                    continue;
                }

                var row = level.Row;
                BuffStatMods.Transfer(in row, (uint)Math.Max(0, Math.Round(foe.Health)), missing, out var taken, out var given, (float)foe.Kind.DrainResist);
                var candidate = new HealCandidate(level.Name, (uint)fullest, Math.Min(missing, given), taken, level.CastChance * foe.Kind.DrainLand, level.BaseMana, level.Seconds);
                if (count > 0 && candidate.CastSeconds * FightScenario.EmergencySecondsMargin > secondsLeft)
                {
                    continue; // a wound-up level only when she has the seconds
                }

                levelOf[count] = i;
                candidates[count++] = candidate;
            }
        }

        var heal = v.HealSelf;
        var canSelf = heal is not null && v.Mana >= heal.BaseMana;
        var self = heal is null
            ? new HealCandidate("Heal Self", 0, 0, 0, 0, 0, 0)
            : new HealCandidate(heal.Name, 0, Math.Min(missing, MeasuredHeals.SelfSeed(heal.Level)), 0, heal.CastChance, heal.BaseMana, heal.Seconds);
        var choice = HealPick.ChooseNow(in self, canSelf, candidates.AsSpan(0, count), out _, out _, out _);
        return choice switch
        {
            HealPick.Nothing => null,
            HealPick.HealSelf => new FightMove(FightMoveKind.HealSelf, heal, -1, "emergency: Heal Self scores at least the best drain", Emergency: true),
            _ => new FightMove(FightMoveKind.DrainHealth, v.HealthDrains[levelOf[choice]], fullest, "emergency: the drain scores more than Heal Self", Emergency: true),
        };
    }

    /// <summary>The living creature a drain takes from with the most health left as a share of its maximum (<see cref="FightEmergency.Fullest"/>: the nearest among equals; every creature here stands at her), or -1.</summary>
    /// <param name="v">The view.</param>
    public static int FullestDrainable(FightView v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var n = Math.Min(v.Foes.Count, 64);
        Span<float> fractions = stackalloc float[n];
        Span<float> distances = stackalloc float[n];
        Span<bool> excluded = stackalloc bool[n];
        for (var i = 0; i < n; i++)
        {
            var foe = v.Foes[i];
            fractions[i] = foe.Kind.HealthMax > 0 ? (float)(foe.Health / foe.Kind.HealthMax) : 0f;
            excluded[i] = !foe.Alive || foe.Kind.DrainResist <= 0;
        }

        return FightEmergency.Fullest(fractions, distances, excluded, n);
    }

    /// <summary>The living creature with the most stamina, or -1.</summary>
    private static int FullestStamina(FightView v)
    {
        var best = -1;
        for (var i = 0; i < v.Foes.Count; i++)
        {
            if (v.Foes[i].Alive && v.Foes[i].Stamina > 0 && v.Foes[i].Kind.StaminaDrainResist > 0 && (best < 0 || v.Foes[i].Stamina > v.Foes[best].Stamina)) // M6 undrainable fills: FightScenario.ManaLoop.cs FullestStamina's kind rule
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// The vitals policy's facts as the fight fills them (FightScenario.VitalsPolicy.cs TryPolicyFacts): her bars and regeneration, the fight's tally, the bolt on the target's kind, the fullest sources, the presses she may try,
    /// the health drains whose base her mana holds, the stamina drain. Two differences, named in docs/fight-sim.md: the seconds a kill are left unmeasured (the policy's seed: a fresh pull), and every cast's seconds are the server's.
    /// </summary>
    /// <param name="v">The view.</param>
    /// <param name="presses">The presses offered, in the order of <see cref="FillLevel.Index"/>.</param>
    /// <param name="drains">The health drains offered, in the same order.</param>
    /// <param name="fullestHealth">The creature a health drain takes from, or -1.</param>
    /// <param name="fullestStamina">The creature a stamina drain takes from, or -1.</param>
    public static VitalsPolicyFacts Facts(FightView v, out List<FightSpell> presses, out List<FightSpell> drains, out int fullestHealth, out int fullestStamina)
    {
        ArgumentNullException.ThrowIfNull(v);
        var target = v.Foes[v.Target];
        var kind = target.Kind;
        fullestHealth = FullestDrainable(v);
        fullestStamina = FullestStamina(v);
        var healthLand = fullestHealth >= 0 ? v.Foes[fullestHealth].Kind.DrainLand : 1; // the share of a drain's casts that land past the source's resist (FightScenario.VitalsPolicy.cs TryPolicyFacts); the cast chance apart
        var staminaLand = fullestStamina >= 0 ? v.Foes[fullestStamina].Kind.DrainLand : 1;
        var healthResist = fullestHealth >= 0 ? v.Foes[fullestHealth].Kind.DrainResist : 1; // M6 undrainable fills: the source's share of the take (TryPolicyFacts: DrainResistOf, DrainResist.StaminaOf)
        var staminaResist = fullestStamina >= 0 ? v.Foes[fullestStamina].Kind.StaminaDrainResist : 1;
        presses = [];
        var pressLevels = new List<FillLevel>();
        foreach (var p in v.Presses)
        {
            if (ManaConversionModel.LandingChance(p.BaseMana, p.Power, v.ManaConversion, v.Mana) < v.TryChance)
            {
                continue;
            }

            pressLevels.Add(new FillLevel(presses.Count, p.Level, p.BaseMana, p.Power, p.Seconds, p.Row, p.CastChance));
            presses.Add(p);
        }

        drains = [];
        var drainLevels = new List<FillLevel>();
        foreach (var d in v.HealthDrains)
        {
            if (d.BaseMana > v.Mana)
            {
                continue;
            }

            drainLevels.Add(new FillLevel(drains.Count, d.Level, d.BaseMana, d.Power, d.Seconds, d.Row, d.CastChance, healthLand, healthResist));
            drains.Add(d);
        }

        FillLevel[] staminaLevels = v.StaminaDrain is { } s ? [new FillLevel(0, s.Level, s.BaseMana, s.Power, s.Seconds, s.Row, s.CastChance, staminaLand, staminaResist)] : [];
        var elapsed = v.Clock;
        var read = v.Hits.Read(v.Clock);
        return new VitalsPolicyFacts
        {
            Health = v.Health,
            HealthMax = v.HealthMax,
            Stamina = v.Stamina,
            StaminaMax = v.StaminaMax,
            Mana = v.Mana,
            ManaMax = v.ManaMax,
            ManaConversion = v.ManaConversion,
            HealthRegen = v.HealthRegen,
            StaminaRegen = v.StaminaRegen,
            ManaRegen = v.ManaRegen,
            SecondsPerKill = 0,
            AttacksPerSecond = elapsed > 1 ? v.Attacks / elapsed : 0,
            DamagePerSecond = elapsed > 1 ? v.Damage / elapsed : 0,
            BurstPerSecond = read.PerSecond,
            BiggestHit = Math.Max(read.BiggestHit, v.BiggestHit),
            BoltLevel = kind.Bolt.Level,
            BoltBaseMana = kind.Bolt.BaseMana,
            BoltPower = kind.Bolt.Power,
            BoltSeconds = kind.Bolt.Seconds,
            BoltMeanDamage = kind.BoltMeanLanded,
            BoltSpread = kind.BoltSpread,
            BoltLandChance = kind.BoltLand,
            KillHealth = kind.HealthMax,
            KillStamina = (uint)Math.Max(0, kind.StaminaMax),
            TargetHealth = target.Health,
            FullestHealth = fullestHealth >= 0 ? v.Foes[fullestHealth].Health : 0,
            FullestStamina = fullestStamina >= 0 ? (uint)Math.Max(0, v.Foes[fullestStamina].Stamina) : 0,
            EmergencyLine = v.Emergency.Enabled ? v.Emergency.LineHealth : 0,
            Attackers = v.Foes.Count(foe => foe.Alive), // the fight's attackers (FightScenario AttackersNow): every living creature here attacks her from the first second
            Presses = pressLevels.ToArray(),
            StaminaDrains = staminaLevels,
            HealthDrains = drainLevels.ToArray(),
            Revitalizes = v.Revitalizes.Select((r, i) => new BoostLevel(i, r.Spell.Level, r.Spell.BaseMana, r.Spell.Power, r.Spell.Seconds, r.Row, r.Spell.CastChance)).ToArray(), // M6 stamina dance: TryPolicyFacts' refill levels
        };
    }
}

/// <summary>How one simulated fight ended.</summary>
internal enum FightEnd
{
    /// <summary>Every creature fell and she lives.</summary>
    Won,

    /// <summary>Her health reached 0.</summary>
    Died,

    /// <summary>Creatures live and she can cast nothing that kills or gives mana: the moment she must flee or die.</summary>
    OutOfMana,

    /// <summary>Neither side won within the fight's longest time (<see cref="InFightRules"/>' engine's cap).</summary>
    Stalled,
}

/// <summary>One simulated fight's outcome.</summary>
/// <param name="End">How it ended.</param>
/// <param name="Seconds">When it ended.</param>
/// <param name="LowestHealth">Her lowest health over it (0 when she died).</param>
/// <param name="EndHealth">Her health at its end.</param>
/// <param name="ManaSpent">The mana her casts cost.</param>
/// <param name="ManaNet">Her mana at the start less her mana at its end.</param>
/// <param name="Heals">Her heal casts (Heal Self and Drain Health Other, landed or not).</param>
/// <param name="Bolts">Her bolt casts.</param>
/// <param name="Presses">Her Stamina to Mana casts.</param>
/// <param name="StaminaDrains">Her Drain Stamina Other casts.</param>
/// <param name="EmergencyHeals">Of <paramref name="Heals"/>, the emergency's.</param>
/// <param name="Kills">The creatures dead at its end.</param>
internal readonly record struct FightResult(FightEnd End, double Seconds, double LowestHealth, double EndHealth, double ManaSpent, double ManaNet, int Heals, int Bolts, int Presses, int StaminaDrains, int EmergencyHeals, int Kills = 0);

/// <summary>Many fights of one pack, summed up.</summary>
/// <param name="Fights">How many.</param>
/// <param name="Dies">The share she died in.</param>
/// <param name="OutOfMana">The share that ended with creatures alive and nothing she could cast.</param>
/// <param name="Stalled">The share that reached the cap.</param>
/// <param name="LowestMean">Her lowest health, the mean over every fight (a death counts 0).</param>
/// <param name="LowestP10">Her lowest health, the 10th percentile over every fight.</param>
/// <param name="HealsMean">Heal casts a fight.</param>
/// <param name="ManaSpentMean">Mana her casts cost a fight.</param>
/// <param name="ClearSeconds">Seconds to clear, the mean over the fights she won (NaN when none).</param>
/// <param name="EndHealthWon">Her health at the end of a won fight, the mean (NaN when none).</param>
/// <param name="ManaNetWon">Her mana at the start of a won fight less her mana at its end, the mean (NaN when none; under 0 when her presses gave more than her casts cost).</param>
/// <param name="BoltsMean">Bolt casts a fight.</param>
/// <param name="PressesMean">Stamina to Mana casts a fight.</param>
/// <param name="StaminaDrainsMean">Drain Stamina Other casts a fight.</param>
/// <param name="EmergencyShare">The share of the heal casts that were the emergency's.</param>
/// <param name="OutOfManaSeconds">When a fight that ran out of mana ended, the mean (NaN when none did).</param>
/// <param name="OutOfManaKills">The creatures dead when a fight ran out of mana, the mean (NaN when none did).</param>
internal sealed record PackVerdict(int Fights, double Dies, double OutOfMana, double Stalled, double LowestMean, double LowestP10, double HealsMean, double ManaSpentMean, double ClearSeconds, double EndHealthWon, double ManaNetWon,
    double BoltsMean, double PressesMean, double StaminaDrainsMean, double EmergencyShare, double OutOfManaSeconds = double.NaN, double OutOfManaKills = double.NaN)
{
    /// <summary>The share of fights that did not end in a win: dead, out of mana or stalled.</summary>
    public double NotWon => Dies + OutOfMana + Stalled;

    /// <summary>The fights summed up.</summary>
    /// <param name="results">The fights.</param>
    public static PackVerdict Of(IReadOnlyList<FightResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var n = results.Count;
        if (n == 0)
        {
            return new PackVerdict(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        var lowest = results.Select(r => r.LowestHealth).OrderBy(h => h).ToArray();
        var won = results.Where(r => r.End == FightEnd.Won).ToList();
        var dry = results.Where(r => r.End == FightEnd.OutOfMana).ToList();
        var heals = results.Sum(r => (double)r.Heals);
        return new PackVerdict(
            n,
            results.Count(r => r.End == FightEnd.Died) / (double)n,
            results.Count(r => r.End == FightEnd.OutOfMana) / (double)n,
            results.Count(r => r.End == FightEnd.Stalled) / (double)n,
            lowest.Average(),
            Percentile(lowest, 0.10),
            heals / n,
            results.Average(r => r.ManaSpent),
            won.Count == 0 ? double.NaN : won.Average(r => r.Seconds),
            won.Count == 0 ? double.NaN : won.Average(r => r.EndHealth),
            won.Count == 0 ? double.NaN : won.Average(r => r.ManaNet),
            results.Average(r => (double)r.Bolts),
            results.Average(r => (double)r.Presses),
            results.Average(r => (double)r.StaminaDrains),
            heals > 0 ? results.Sum(r => (double)r.EmergencyHeals) / heals : 0,
            dry.Count == 0 ? double.NaN : dry.Average(r => r.Seconds),
            dry.Count == 0 ? double.NaN : dry.Average(r => (double)r.Kills));
    }

    /// <summary>The value at <paramref name="share"/> of <paramref name="sorted"/> (ascending): the element at that share's rank, rounded down.</summary>
    /// <param name="sorted">The values, ascending.</param>
    /// <param name="share">0 to 1.</param>
    public static double Percentile(IReadOnlyList<double> sorted, double share)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        return sorted.Count == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Floor(share * sorted.Count), 0, sorted.Count - 1)];
    }

    /// <summary>
    /// The verdict at a pack of <paramref name="company"/> (a mean pack size) from the verdicts at its two whole sizes, taken in proportion as the race's <c>Verdict.DeathAt</c> takes them: <paramref name="low"/> at the size rounded
    /// down, <paramref name="high"/> at the next.
    /// </summary>
    /// <param name="low">The verdict at the size rounded down.</param>
    /// <param name="high">The verdict at the next size (the same as <paramref name="low"/> at a whole size).</param>
    /// <param name="company">The mean pack size.</param>
    public static (double Dies, double OutOfMana) Between(PackVerdict low, PackVerdict high, double company)
    {
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(high);
        var t = company - Math.Floor(company);
        return (low.Dies + ((high.Dies - low.Dies) * t), low.OutOfMana + ((high.OutOfMana - low.OutOfMana) * t));
    }
}

using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What <see cref="VitalsPolicy.Decide"/> casts next.</summary>
public enum VitalsMove : byte
{
    /// <summary>The bolt: no fill is due now.</summary>
    Bolt,

    /// <summary>Stamina to Mana Self at the level <see cref="VitalsChoice.Index"/> of <see cref="VitalsPolicyFacts.Presses"/>.</summary>
    Press,

    /// <summary>Drain Stamina Other at the creature in reach that holds the most stamina.</summary>
    DrainStamina,

    /// <summary>Drain Health Other at the creature in reach that holds the most health.</summary>
    DrainHealth,

    /// <summary>M6 14 upkeep mana: a mana drink from the hotbar (the biggest held that fits whole, <see cref="ManaDrinks.Fitting"/>), toward the upkeep's target when no press fits.</summary>
    DrinkMana,
}

/// <summary>
/// M6 14 upkeep mana: one kept buff line as the policy reads it, every number the fight's own: when it falls due under fire, what its recast can cost and is expected to cost, and the health standing for it needs.
/// </summary>
/// <param name="Name">The line's name (the log's).</param>
/// <param name="TimeToDue">The seconds until the line's time left falls under the time at which the fight recasts it with a hostile on her (the hard floor, the next kill's seconds and the recast's expected seconds: the between-kills
/// horizon); 0 or less: due now.</param>
/// <param name="Ceiling">The most its recast can cost: the number the way out's guard reads (ACE's base mana with the armour surcharge; Mana Conversion only lowers it).</param>
/// <param name="Expected">The recast's expected cost after Mana Conversion (<see cref="ManaConversionModel.ExpectedCost"/> at the level the pass casts).</param>
/// <param name="StandHealth">The health she needs to stand for the recast: the heal line and the damage she takes over its expected seconds (the between-kills safety, <c>FightScenario.KillGapPlan</c>).</param>
public readonly record struct UpkeepLine(string Name, double TimeToDue, uint Ceiling, double Expected, double StandHealth);

/// <summary>M6 14 upkeep mana: the upkeep the policy plans for (<see cref="VitalsPolicy.Upkeep"/>).</summary>
/// <param name="Target">The mana it fills toward before the soonest line falls due (0: no line inside the window).</param>
/// <param name="Wanted">The target before the cap at her maximum (over it, the guard cannot be met under fire: the line waits for a lull).</param>
/// <param name="Lines">The lines inside the window.</param>
/// <param name="First">The soonest line's name (the soonest outside the window when none is inside).</param>
/// <param name="DueSeconds">The soonest line's seconds to its due.</param>
/// <param name="LeadSeconds">The window: the seconds a kill and the fills' seconds to the target.</param>
/// <param name="StandHealth">The most health a line that is due now needs to stand for its recast (0: none is due).</param>
public readonly record struct VitalsUpkeep(uint Target, uint Wanted, int Lines, string First, double DueSeconds, double LeadSeconds, double StandHealth)
{
    /// <summary>No kept line.</summary>
    public static VitalsUpkeep None => new(0, 0, 0, string.Empty, double.PositiveInfinity, 0, 0);

    /// <summary>True when a line is inside the window: the target stands.</summary>
    public bool Active => Lines > 0;
}

/// <summary>One level of a fill she may cast now (in her book, at the cast chance, paid for by the pack).</summary>
/// <param name="Index">The caller's index of the level (the fight's own list).</param>
/// <param name="Level">The spell's level (1 to 8).</param>
/// <param name="BaseMana">The spell's base mana (the most it costs).</param>
/// <param name="Power">The spell's power (Mana Conversion's difficulty).</param>
/// <param name="Seconds">The cast's seconds (the ladder's measured table).</param>
/// <param name="Row">ACE's transfer row: the share taken, the loss, the cap.</param>
/// <param name="Chance">M6 heal spend: the chance the cast does not fizzle, on her live skill (1: taken as sure, not read). A fizzle gives nothing and costs <see cref="ManaConversionModel.FizzleMana"/> (ACE <c>Player_Magic.cs</c>
/// <c>CalculateManaUsage</c>, 566-572 @95ef426d), so the policy prices a level at its expected give and cost; 0 or less: not offered.</param>
/// <param name="Lands">M6 heal spend: of the casts that do not fizzle, the share that lands: for a drain, the source's failing its resist (1: a self cast, or not read). A resisted cast gives nothing and costs its whole mana.</param>
/// <param name="DrainMod">
/// M6 undrainable fills (live state 2026-11-20, the Tusker Armory at level 50: an Armored Tusker's ResistHealthDrain, ResistStaminaDrain and ResistManaDrain are 0, world DB weenie_properties_float 125, 72, 74 of 11540 and 22611):
/// the source's drain resistance, the share of the take ACE lets through (WorldObject_Magic.cs 719 @95ef426d, <c>drainMod</c>; <see cref="BuffStatMods.Transfer"/>'s <c>drainMod</c>). 1: a self cast, or a source that resists
/// nothing; 0: the drain takes and gives nothing, so the level is never offered. The emergency read it since M6 14 drain resistance (<see cref="DrainPicker"/>); the policy priced every drain as if it were 1.
/// </param>
public readonly record struct FillLevel(int Index, int Level, uint BaseMana, uint Power, double Seconds, TransferRow Row, double Chance = 1, double Lands = 1, double DrainMod = 1);

/// <summary>
/// M6 stamina dance (<c>docs/session.md</c> section 121): one level of Revitalize Self her fight's refill may cast (in her book, at the cast chance, paid for by the pack): what a point of stamina costs her to put back, so the policy prices the
/// stamina a press takes (<see cref="VitalsPolicy.RefillPrice"/>).
/// </summary>
/// <param name="Index">The caller's index of the level.</param>
/// <param name="Level">The spell's level (1 to 8).</param>
/// <param name="BaseMana">The spell's base mana (the most it costs).</param>
/// <param name="Power">The spell's power (Mana Conversion's difficulty).</param>
/// <param name="Seconds">The cast's seconds (the ladder's measured self table).</param>
/// <param name="Row">The spell's boost row: a uniform integer in [Boost, Boost + Variance] (ACE <c>HandleCastSpell_Boost</c>, <c>WorldObject_Magic.cs</c> 474 @95ef426d; <see cref="BuffStatMods.Revitalize"/>).</param>
/// <param name="Chance">The chance the cast does not fizzle, on her live skill (1: taken as sure); 0 or less: not offered.</param>
public readonly record struct BoostLevel(int Index, int Level, uint BaseMana, uint Power, double Seconds, BoostRow Row, double Chance = 1);

/// <summary>What <see cref="VitalsPolicy.Decide"/> reads: every number live from her character, the fight's own measurements and the target; nothing fixed but the ladder's <see cref="LadderVitalsPolicy"/>.</summary>
public readonly record struct VitalsPolicyFacts
{
    /// <summary>Her health now and its maximum.</summary>
    public uint Health { get; init; }

    /// <summary>Her maximum health.</summary>
    public uint HealthMax { get; init; }

    /// <summary>Her stamina now.</summary>
    public uint Stamina { get; init; }

    /// <summary>Her maximum stamina.</summary>
    public uint StaminaMax { get; init; }

    /// <summary>Her mana now.</summary>
    public uint Mana { get; init; }

    /// <summary>Her maximum mana.</summary>
    public uint ManaMax { get; init; }

    /// <summary>Her current Mana Conversion (0: untrained).</summary>
    public uint ManaConversion { get; init; }

    /// <summary>Her health regeneration a second in the fight's stance (<see cref="VitalsPolicy.Regen"/>).</summary>
    public double HealthRegen { get; init; }

    /// <summary>Her stamina regeneration a second in the fight's stance.</summary>
    public double StaminaRegen { get; init; }

    /// <summary>Her mana regeneration a second (no stance moves it).</summary>
    public double ManaRegen { get; init; }

    /// <summary>The fight's measured seconds a kill, every second of the fight counted (0 or less: not measured yet, the seed reads).</summary>
    public double SecondsPerKill { get; init; }

    /// <summary>The attacks thrown at her a second over the fight (hits and evades: each costs her a point of stamina in a combat mode, ACE <c>Player_Combat.cs:331, 518</c> @95ef426d).</summary>
    public double AttacksPerSecond { get; init; }

    /// <summary>The damage she takes a second over the fight.</summary>
    public double DamagePerSecond { get; init; }

    /// <summary>The damage she takes a second over the last few seconds (<see cref="RecentHits"/>).</summary>
    public double BurstPerSecond { get; init; }

    /// <summary>The biggest hit she has taken.</summary>
    public double BiggestHit { get; init; }

    /// <summary>The bolt's level (1 to 8).</summary>
    public int BoltLevel { get; init; }

    /// <summary>The bolt's base mana.</summary>
    public uint BoltBaseMana { get; init; }

    /// <summary>The bolt's power.</summary>
    public uint BoltPower { get; init; }

    /// <summary>The bolt's seconds (the ladder's measured table).</summary>
    public double BoltSeconds { get; init; }

    /// <summary>The bolt's mean damage a landed cast (the skill bonus and ACE's critical in it, <see cref="WarSpellPicker.MeanHit"/>).</summary>
    public double BoltMeanDamage { get; init; }

    /// <summary>The width of the bolt's damage roll (the spell's variance: ACE rolls a uniform between its minimum and maximum, <c>SpellProjectile.cs:537</c>).</summary>
    public double BoltSpread { get; init; }

    /// <summary>The chance a bolt lands: the cast's (Player_Magic.cs:530-536) times the target's failing its Magic Defense (WorldObject_Magic.cs:104-113).</summary>
    public double BoltLandChance { get; init; }

    /// <summary>The ground's target class's maximum health (a fresh target).</summary>
    public double KillHealth { get; init; }

    /// <summary>The ground's target class's stamina (a fresh target; 0: not told).</summary>
    public uint KillStamina { get; init; }

    /// <summary>The bolt's target's health now (negative: not read).</summary>
    public double TargetHealth { get; init; }

    /// <summary>The health of the creature in reach that holds the most (0: none in reach).</summary>
    public double FullestHealth { get; init; }

    /// <summary>The stamina of the creature in reach that holds the most, by its appraisal (0: none read).</summary>
    public uint FullestStamina { get; init; }

    /// <summary>The survival layer's emergency line in health points (<see cref="FightEmergency.LineHealth"/>; 0: none).</summary>
    public double EmergencyLine { get; init; }

    /// <summary>The Stamina to Mana Self levels she may cast now, lowest first.</summary>
    public ReadOnlyMemory<FillLevel> Presses { get; init; }

    /// <summary>The Drain Stamina Other levels she may cast now.</summary>
    public ReadOnlyMemory<FillLevel> StaminaDrains { get; init; }

    /// <summary>The Drain Health Other levels she may cast now.</summary>
    public ReadOnlyMemory<FillLevel> HealthDrains { get; init; }

    /// <summary>M6 stamina dance: the Revitalize Self levels her fight's refill may cast (empty: none, or the refill is off or set aside; the stamina is then priced as before).</summary>
    public ReadOnlyMemory<BoostLevel> Revitalizes { get; init; }

    /// <summary>M6 14 upkeep mana: the kept buff lines the fight recasts under fire (empty: none, or no way out armed: no guard to meet).</summary>
    public ReadOnlyMemory<UpkeepLine> Upkeep { get; init; }

    /// <summary>M6 14 upkeep mana: the mana the way out keeps after a recast (its line: the recall and a drain).</summary>
    public uint UpkeepReserve { get; init; }

    /// <summary>M6 14 upkeep mana: what the mana drink she would drink gives (the biggest held that fits whole in the missing mana; 0: none held, none fits, or the drinks are set aside).</summary>
    public uint ManaDrinkGive { get; init; }

    /// <summary>
    /// M6 heal spend: the creatures attacking her now (the fight's attackers, heard swinging and alive; 0: not read). Over one, the next kill takes its share out of the incoming while another stays on her as a drain's source: a top-up
    /// stands under less incoming after it (<see cref="VitalsPolicy.HealthTopUpWaits"/>).
    /// </summary>
    public int Attackers { get; init; }
}

/// <summary>One kill's budget of each vital (<see cref="VitalsPolicy.Budget"/>): what a kill costs her and what she regenerates over it.</summary>
/// <param name="BoltsAKill">The bolts a kill (renewal: health over the mean landed damage, plus the mean overshoot).</param>
/// <param name="SecondsAKill">The seconds a kill (measured, or the seed).</param>
/// <param name="Measured">True when <paramref name="SecondsAKill"/> is the fight's own.</param>
/// <param name="ManaAKill">The mana a kill: the bolts' and the health drains' expected costs after Mana Conversion.</param>
/// <param name="ManaRegenAKill">The mana regenerated over a kill.</param>
/// <param name="StaminaAKill">The stamina a kill: the attacks thrown at her and the presses that cover the mana short.</param>
/// <param name="StaminaRegenAKill">The stamina regenerated over a kill.</param>
/// <param name="HealthAKill">The damage taken over a kill.</param>
/// <param name="HealthRegenAKill">The health regenerated over a kill.</param>
public readonly record struct VitalsBudget(double BoltsAKill, double SecondsAKill, bool Measured, double ManaAKill, double ManaRegenAKill, double StaminaAKill, double StaminaRegenAKill, double HealthAKill, double HealthRegenAKill)
{
    /// <summary>The mana a kill that regeneration does not cover (over 0: presses are needed).</summary>
    public double ManaShort => ManaAKill - ManaRegenAKill;

    /// <summary>The stamina a kill that regeneration does not cover (over 0: stamina drains are needed).</summary>
    public double StaminaShort => StaminaAKill - StaminaRegenAKill;

    /// <summary>The health a kill that regeneration does not cover.</summary>
    public double HealthShort => HealthAKill - HealthRegenAKill;
}

/// <summary>The lines the policy works out (<see cref="VitalsPolicy.Lines"/>).</summary>
/// <param name="ManaFloor">Under it a press goes at once: a bolt, a health drain and a press at their dearest rolls.</param>
/// <param name="HealthFloor">Under it a health drain goes at once: the biggest hit and the burst over a bolt and a drain.</param>
/// <param name="StaminaReady">A press waits for the stamina to reach it: the top less one stamina drain's give (stamina short) or one kill's regeneration (stamina in surplus).</param>
/// <param name="PullMana">The mana a pull starts with at least: the floor and one kill's short.</param>
/// <param name="PullHealth">The health a pull starts with at least: within one health drain of full, never under the floor or the emergency line.</param>
public readonly record struct VitalsLines(uint ManaFloor, uint HealthFloor, uint StaminaReady, uint PullMana, uint PullHealth);

/// <summary>The seconds a point of each thing is worth (<see cref="VitalsPolicy.Prices"/>).</summary>
/// <param name="Damage">A point of damage by the bolt: its seconds over its expected damage.</param>
/// <param name="Stamina">A point of stamina while stamina runs short over a kill: the cheaper of the best stamina drain's seconds over its give and (M6 stamina dance) the best Revitalize Self level's seconds and mana over its expected restore
/// (<see cref="VitalsPolicy.RefillPrice"/>); else 0.</param>
/// <param name="Mana">A point of mana: the best press's seconds over its net from a full bar while mana runs short, else 0 (a drain's own mana is priced at it).</param>
/// <param name="HealMana">M6 heal spend: the mana a point of health costs her through the health drain the policy prices best at the fullest source (its expected cost over its whole give; 0: no drain offered).</param>
public readonly record struct VitalsPrices(double Damage, double Stamina, double Mana = 0, double HealMana = 0);

/// <summary>A choice: the move, the level's index in its list, what it gives, its price in seconds a point and the reason.</summary>
/// <param name="Move">The move.</param>
/// <param name="Index">The caller's index of the level (-1: the bolt).</param>
/// <param name="Give">What it gives (mana, stamina or health).</param>
/// <param name="Price">Its seconds a point given.</param>
/// <param name="Why">A fixed reason (the log's key).</param>
/// <param name="Upkeep">M6 14 upkeep mana: the move is one of the upkeep's fills (or its heal), toward a buff line's recast.</param>
public readonly record struct VitalsChoice(VitalsMove Move, int Index, double Give, double Price, string Why, bool Upkeep = false);

/// <summary>The plan: the choice and every number it read, for the log.</summary>
/// <param name="Choice">The choice.</param>
/// <param name="Budget">One kill's budget.</param>
/// <param name="Lines">The lines.</param>
/// <param name="Prices">The prices.</param>
/// <param name="NextBoltKills">The chance the next bolt kills the target (0: not read).</param>
/// <param name="Upkeep">M6 14 upkeep mana: the upkeep it planned for.</param>
public readonly record struct VitalsPlan(VitalsChoice Choice, VitalsBudget Budget, VitalsLines Lines, VitalsPrices Prices, double NextBoltKills, VitalsUpkeep Upkeep = default)
{
    /// <summary>M6 14 upkeep mana: the upkeep's part of the log line (empty with no kept line).</summary>
    public string UpkeepText()
    {
        var u = Upkeep;
        if (string.IsNullOrEmpty(u.First))
        {
            return string.Empty;
        }

        return u.Active
            ? string.Create(CultureInfo.InvariantCulture, $"; upkeep target {u.Target}{(u.Wanted > u.Target ? $" (wanted {u.Wanted}, over her maximum)" : string.Empty)}: {u.Lines} line(s) due within {u.LeadSeconds:0.#} s, {u.First} in {Math.Max(0, u.DueSeconds):0.#} s{(u.StandHealth > 0 ? $", standing for it needs health {u.StandHealth:0}" : string.Empty)}")
            : string.Create(CultureInfo.InvariantCulture, $"; upkeep: none due (the soonest, {u.First}, in {u.DueSeconds:0} s, past the {u.LeadSeconds:0.#} s window)");
    }

    /// <summary>The log line: the move and every input it read.</summary>
    /// <param name="f">The facts.</param>
    public string Describe(in VitalsPolicyFacts f)
    {
        var b = Budget;
        var l = Lines;
        return string.Create(CultureInfo.InvariantCulture, $"vitals policy: {Choice.Move.ToString().ToLowerInvariant()}{(Choice.Index >= 0 ? $" level {Choice.Index}" : string.Empty)} ({Choice.Why}; gives {Choice.Give:0.#} at {Choice.Price:0.###} s a point): health {f.Health}/{f.HealthMax} stamina {f.Stamina}/{f.StaminaMax} mana {f.Mana}/{f.ManaMax}; a kill {b.SecondsAKill:0.#} s{(b.Measured ? string.Empty : " (seed)")} {b.BoltsAKill:0.##} bolts; mana {b.ManaAKill:0.#} regen {b.ManaRegenAKill:0.#} short {b.ManaShort:0.#}; stamina {b.StaminaAKill:0.#} regen {b.StaminaRegenAKill:0.#} short {b.StaminaShort:0.#}; health {b.HealthAKill:0.#} regen {b.HealthRegenAKill:0.#}; floors mana {l.ManaFloor} health {l.HealthFloor}; stamina ready {l.StaminaReady}; pull mana {l.PullMana} health {l.PullHealth}; prices damage {Prices.Damage:0.####} stamina {Prices.Stamina:0.####} mana {Prices.Mana:0.####} heal mana a point {Prices.HealMana:0.###}; attackers {(f.Attackers > 0 ? f.Attackers.ToString(CultureInfo.InvariantCulture) : "unread")}; target {(f.TargetHealth < 0 ? "unread" : f.TargetHealth.ToString("0", CultureInfo.InvariantCulture))} next bolt kills {NextBoltKills:P0}; fullest health {f.FullestHealth:0} stamina {f.FullestStamina}; incoming {f.DamagePerSecond:0.##}/s burst {f.BurstPerSecond:0.##}/s biggest {f.BiggestHit:0} attacks {f.AttacksPerSecond:0.##}/s; regen a second health {f.HealthRegen:0.###} stamina {f.StaminaRegen:0.###} mana {f.ManaRegen:0.###}; Mana Conversion {f.ManaConversion}{UpkeepText()}");
    }
}

/// <summary>
/// M6 14 (<c>m6-vitals-policy</c>; the owner, 2026-11-13: given her stats and the creature she fights, the best level and moment of every mana cast; experience an hour first; fights end near full without top-ups): the climber's fills by the
/// numbers, pure. The model and its sums are <c>cache/m6/mana/model.md</c>; every formula is ACE's at 95ef426d.
/// <para><b>One currency, seconds.</b> Her experience an hour is the kills she makes, so every cast that is not a bolt is time not killing. A fill is worth casting only when the vital it fills runs short over a kill (what a kill costs her
/// against what she regenerates over it, <see cref="Budget"/>), and then at the moment it wastes nothing: when its whole give fits what she is missing (ACE cuts a transfer to the missing amount and charges the whole cost anyway,
/// <c>WorldObject_Magic.cs:737-746</c>), at the level that costs the fewest seconds a point (<see cref="Prices"/>: the cast's seconds, the stamina it takes at the stamina's price, and for a health drain the damage it deals counted
/// as bolt seconds saved). Waiting is free while she is over the floors; a floor fills at once.</para>
/// <para><b>Regeneration</b> (<c>Creature_Vitals.cs:102-236</c>): a vital gains its rate times the attribute factor (health only), the stance factor (health and stamina; 0.5 in a combat mode) and the enchantments each 5 s heartbeat;
/// mana takes no stance. So a mana point at rest costs 2.3 s and a stamina drain's point 0.05 s: she never rests for a vital a fill can give.</para>
/// <para><b>Stamina</b> is spent only by the attacks thrown at her (a point each in a combat mode) and by presses; a tusker's attacks spend none of its own (ACE's monster attacks read no stamina), so a stamina drain is worth
/// only the stamina it gives her.</para>
/// <para><b>Upkeep</b> (M6 14 upkeep mana; live 2026-11-14 01:29 to 02:15 UTC, level 58 in the Tusker Quarters: a hostile always in reach, the mana at 150 to 200 of 377, and every recast of the armour lines refused by the way out's guard until they
/// lapsed under fire). A buff line recast under fire must pass the way out's guard: its cost at the worst roll and the way out's line held (<see cref="ManaWayOut.Spend"/>). So the upkeep coming is a mana need of its own
/// (<see cref="Upkeep"/>): when a kept line falls due within a kill and the fills' own seconds, its worst-case cost plus the way out's line is a target the policy fills toward before the line is due, by the cheapest fill it prices
/// (the press from the stamina she holds when it reaches the target, else a stamina drain first, else the press toward it, else a mana drink that fits when no press does), and a line that is due waits for no heal it can be given
/// (its standing health first). The worst case stays the guard's number (the target must pass it); the expected cost (Mana Conversion's mean) is what each line before the last of a batch takes, so one fill covers the batch.</para>
/// </summary>
public static class VitalsPolicy
{
    /// <summary>ACE's chance of a War Magic critical with no weapon rating (<c>WorldObject_Weapon.cs:324</c> <c>defaultMagicCritFrequency</c>).</summary>
    public const double CritChance = 0.05;

    /// <summary>The human weenie's regeneration rows a 5 s heartbeat (ACE's player weenie: HealthRate 0.3, StaminaRate 3, ManaRate 1; <c>Creature_Vitals.cs:102-150</c>), a second at the stance factor 1.</summary>
    public const double HealthAtOne = 0.06, StaminaAtOne = 0.6, ManaAtOne = 0.2;

    /// <summary>The stance factor of health and stamina in a combat mode or running (<c>Creature_Vitals.cs:220-222</c>).</summary>
    public const double CombatStance = 0.5;

    /// <summary>
    /// Her regeneration a second (<c>Creature_Vitals.cs:102-236</c>): <paramref name="atOne"/> times the attribute factor (health: 1 + (Strength + 2 Endurance - 200) / 600 on the base attributes, 1 to 2.1), the stance factor
    /// (mana: none) and the enchantments' multiplier.
    /// </summary>
    /// <param name="atOne">The vital's rate at the factor 1 (<see cref="HealthAtOne"/>, <see cref="StaminaAtOne"/>, <see cref="ManaAtOne"/>).</param>
    /// <param name="stance">The stance factor (1 for mana).</param>
    /// <param name="enchantment">The product of her regeneration enchantments' multipliers for the vital.</param>
    /// <param name="strengthBase">For health: her base Strength (0 for the others).</param>
    /// <param name="enduranceBase">For health: her base Endurance (0 for the others).</param>
    public static double Regen(double atOne, double stance, double enchantment, uint strengthBase = 0, uint enduranceBase = 0)
    {
        var sum = strengthBase + (2 * enduranceBase);
        var attribute = sum <= 200 ? 1.0 : Math.Clamp(1.0 + ((sum - 200) / 600.0), 1.0, 2.1);
        return atOne * attribute * stance * enchantment;
    }

    private const ushort RateKeyHealth = 3, RateKeyStamina = 4, RateKeyMana = 5; // PropertyFloat HealthRate, StaminaRate, ManaRate: the regeneration enchantments' stat keys (EnchantmentManager.GetRegenerationMod)
    private const uint MultiplicativeFlag = 0x8; // EnchantmentTypeFlags.Multiplicative

    /// <summary>
    /// Her regeneration enchantments' multipliers for health, stamina and mana (ACE <c>EnchantmentManager.GetRegenerationMod</c>: the multiplicative enchantments on the vital's rate key). The highest of each key is taken: her own line and
    /// the buff bot's share a spell category, whose top layer is the highest (a second category on one key is not modelled: <c>TODO(m6-regen-categories)</c>). M6 14 entry recharge: the fight's and the journey's recharge read it here.
    /// </summary>
    /// <param name="q">Her qualities.</param>
    public static (double Health, double Stamina, double Mana) RegenMultipliers(Contracts.PlayerQualities q)
    {
        ArgumentNullException.ThrowIfNull(q);
        double health = 1, stamina = 1, mana = 1;
        foreach (var e in q.ActiveEnchantments)
        {
            var d = e.Enchantment;
            if ((d.StatModType & MultiplicativeFlag) == 0 || d.StatModValue <= 1)
            {
                continue;
            }

            switch (d.StatModKey)
            {
                case RateKeyHealth:
                    health = Math.Max(health, d.StatModValue);
                    break;
                case RateKeyStamina:
                    stamina = Math.Max(stamina, d.StatModValue);
                    break;
                case RateKeyMana:
                    mana = Math.Max(mana, d.StatModValue);
                    break;
            }
        }

        return (health, stamina, mana);
    }

    /// <summary>The bolts a kill of <paramref name="health"/> (renewal: health over the mean landed damage a cast, plus the mean overshoot E[X²] / 2E[X]², at least 1). The roll is ACE's uniform (<c>SpellProjectile.cs:537</c>).</summary>
    /// <param name="f">The facts (the bolt's mean, spread and land chance).</param>
    /// <param name="health">The health to take.</param>
    public static double BoltsToKill(in VitalsPolicyFacts f, double health)
    {
        var mean = f.BoltMeanDamage * f.BoltLandChance;
        if (mean <= 0 || health <= 0)
        {
            return health <= 0 ? 0 : double.PositiveInfinity;
        }

        var half = f.BoltSpread / 2;
        var second = f.BoltLandChance * ((f.BoltMeanDamage * f.BoltMeanDamage) + (half * half / 3)); // E[X²] of a uniform about the mean, times the land chance
        return Math.Max(1, (health / mean) + (second / (2 * mean * mean)));
    }

    /// <summary>The chance the next bolt takes <paramref name="health"/>: the land chance times the share of the uniform roll at or over it.</summary>
    /// <param name="f">The facts.</param>
    /// <param name="health">The target's health now.</param>
    public static double NextBoltKills(in VitalsPolicyFacts f, double health)
    {
        if (health < 0 || f.BoltMeanDamage <= 0)
        {
            return 0;
        }

        var low = f.BoltMeanDamage - (f.BoltSpread / 2);
        var high = f.BoltMeanDamage + (f.BoltSpread / 2);
        var share = high <= low ? (f.BoltMeanDamage >= health ? 1 : 0) : Math.Clamp((high - health) / (high - low), 0, 1);
        return f.BoltLandChance * share;
    }

    /// <summary>One kill's budget of each vital.</summary>
    /// <param name="f">The facts.</param>
    /// <param name="rules">The ladder's lines.</param>
    public static VitalsBudget Budget(in VitalsPolicyFacts f, LadderVitalsPolicy rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var bolts = BoltsToKill(in f, f.KillHealth);
        var measured = f.SecondsPerKill > 0;
        var seconds = measured ? f.SecondsPerKill : rules.SeedOverheadSeconds + (bolts * f.BoltSeconds);
        var boltCost = ManaConversionModel.ExpectedCost(f.BoltBaseMana, f.BoltPower, f.ManaConversion);
        var damagePrice = DamagePrice(in f);
        var fresh = BestTransfer(f.HealthDrains.Span, (uint)Math.Max(0, f.KillHealth), uint.MaxValue, damagePrice, 0, f.ManaConversion, health: true);
        var healthAKill = f.DamagePerSecond * seconds;
        var healthRegen = f.HealthRegen * seconds;
        var drains = fresh.ExpectedGive > 0 ? Math.Max(0, healthAKill - healthRegen) / fresh.ExpectedGive : 0;
        var manaAKill = (bolts * boltCost) + (drains * fresh.Cost);
        var manaRegen = f.ManaRegen * seconds;
        var press = BestTransfer(f.Presses.Span, f.StaminaMax, uint.MaxValue, 0, 0, f.ManaConversion, health: false, mana: true);
        var pressStamina = manaAKill > manaRegen && press.Give > 0 ? (manaAKill - manaRegen) * press.Taken / press.Give : 0;
        var staminaAKill = (f.AttacksPerSecond * seconds) + pressStamina;
        return new VitalsBudget(bolts, seconds, measured, manaAKill, manaRegen, staminaAKill, f.StaminaRegen * seconds, healthAKill, healthRegen);
    }

    /// <summary>The seconds a point of damage costs by the bolt (its seconds over its expected damage a cast).</summary>
    /// <param name="f">The facts.</param>
    public static double DamagePrice(in VitalsPolicyFacts f) => f.BoltMeanDamage * f.BoltLandChance > 0 ? f.BoltSeconds / (f.BoltMeanDamage * f.BoltLandChance) : 0;

    /// <summary>
    /// The prices: a point of damage by the bolt; a point of stamina while stamina runs short over a kill (else 0: it comes back by itself) by the cheaper of the best stamina drain and (M6 stamina dance, <c>docs/session.md</c> section 121)
    /// the refill's Revitalize Self (<see cref="RefillPrice"/>): a press's stamina is put back by one or the other, so a press that takes more stamina for the same mana is dearer by that much.
    /// </summary>
    /// <param name="f">The facts.</param>
    /// <param name="budget">The budget.</param>
    public static VitalsPrices Prices(in VitalsPolicyFacts f, in VitalsBudget budget)
    {
        var stamina = 0.0;
        if (budget.StaminaShort > 0)
        {
            var source = f.FullestStamina > 0 ? f.FullestStamina : f.KillStamina;
            var drain = BestTransfer(f.StaminaDrains.Span, source, uint.MaxValue, 0, 0, f.ManaConversion, health: false);
            stamina = drain.Give > 0 ? drain.Price : 0;
            var refill = RefillPrice(in f, in budget);
            if (refill > 0 && (stamina <= 0 || refill < stamina))
            {
                stamina = refill;
            }
        }

        var mana = 0.0;
        if (budget.ManaShort > 0)
        {
            var press = BestTransfer(f.Presses.Span, f.StaminaMax, uint.MaxValue, 0, stamina, f.ManaConversion, health: false, mana: true);
            mana = press.Index >= 0 ? press.Price : 0;
        }

        var heal = BestTransfer(f.HealthDrains.Span, (uint)Math.Max(0, f.FullestHealth > 0 ? f.FullestHealth : f.KillHealth), uint.MaxValue, DamagePrice(in f), 0, f.ManaConversion, health: true);
        return new VitalsPrices(DamagePrice(in f), stamina, mana, heal.Index >= 0 && heal.ExpectedGive > 0 ? heal.Cost / heal.ExpectedGive : 0);
    }

    /// <summary>
    /// The rounds of <see cref="RefillPrice"/>'s fixed point (Lifestone's own bound: each round shrinks the error by the share of a Revitalize's restore that its own mana costs in pressed stamina, well under one on her rows;
    /// <c>StaminaDanceWholeGivesTests</c> checks the fixed point to four places on run 9's numbers).
    /// </summary>
    public const int RefillPriceRounds = 4;

    /// <summary>
    /// M6 stamina dance (<c>docs/session.md</c> section 121; run 9, <c>cache/m6/sprint/run9/step3-levelup.log</c>: the policy priced stamina at 0 with no stamina drain's source in reach and picked Stamina to Mana III to V from a full bar, 1.1 to
    /// 1.35 mana a stamina point against VI's 1.5): the seconds a point of stamina costs her to put back by the refill's Revitalize Self, at the best level: its cast seconds and its mana at the mana's price over its expected restore.
    /// The restore is the boost row's mean (ACE's uniform roll, <c>HandleCastSpell_Boost</c>, <c>WorldObject_Magic.cs</c> 474 @95ef426d) at the cast chance; the mana its expected cost after Mana Conversion, a fizzle's 5 counted
    /// (<c>Player_Magic.cs</c> 566-572). Only a level whose top roll fits her bar is priced (the refill casts a level only when its whole roll fits, <see cref="ManaOrder.RevitalizeLevelOf(ReadOnlySpan{RevitalizeLevel}, uint, uint, uint, uint, uint, out RefillFit)"/>),
    /// unless none does. The mana's price is the press's (<see cref="Prices"/>), which carries the stamina's: the two are solved together (<see cref="RefillPriceRounds"/> rounds); while the mana does not run short over a kill its price
    /// is 0, as in <see cref="Prices"/>. 0: no level offered. Allocation-free.
    /// </summary>
    /// <param name="f">The facts.</param>
    /// <param name="budget">The budget.</param>
    public static double RefillPrice(in VitalsPolicyFacts f, in VitalsBudget budget)
    {
        var levels = f.Revitalizes.Span;
        var price = BestRefill(levels, f.StaminaMax, f.ManaConversion, 0);
        if (price <= 0 || budget.ManaShort <= 0)
        {
            return price;
        }

        for (var i = 0; i < RefillPriceRounds; i++)
        {
            var press = BestTransfer(f.Presses.Span, f.StaminaMax, uint.MaxValue, 0, price, f.ManaConversion, health: false, mana: true);
            if (press.Index < 0 || double.IsInfinity(press.Price))
            {
                break;
            }

            price = BestRefill(levels, f.StaminaMax, f.ManaConversion, press.Price);
        }

        return price;
    }

    /// <summary>The fewest seconds a point of stamina of <paramref name="levels"/> at <paramref name="manaPrice"/> (see <see cref="RefillPrice"/>); 0 when none is offered.</summary>
    private static double BestRefill(ReadOnlySpan<BoostLevel> levels, uint staminaMax, uint manaConversion, double manaPrice)
    {
        var any = false;
        foreach (var level in levels)
        {
            any |= level.Chance > 0 && level.Row.Max <= staminaMax;
        }

        var best = 0.0;
        foreach (var level in levels)
        {
            if (level.Chance <= 0 || (any && level.Row.Max > staminaMax))
            {
                continue;
            }

            var chance = Math.Min(1, level.Chance);
            var restore = chance * Math.Min(staminaMax, level.Row.Boost + (level.Row.Variance / 2.0));
            var cost = (chance * ManaConversionModel.ExpectedCost(level.BaseMana, level.Power, manaConversion)) + ((1 - chance) * ManaConversionModel.FizzleMana);
            if (restore <= 0)
            {
                continue;
            }

            var price = (level.Seconds + (manaPrice * cost)) / restore;
            if (best <= 0 || price < best)
            {
                best = price;
            }
        }

        return best;
    }

    /// <summary>
    /// M6 heal spend: the mana the health a fill's seconds cost her would take to heal: the incoming now (<see cref="VitalsPolicyFacts.BurstPerSecond"/>, the recent hits) over the fill's seconds, at the mana a health point of the heal the
    /// policy would cast (<see cref="VitalsPrices.HealMana"/>). Every second not killing stands under every creature on her; in a fight whose health is kept by drains, that damage is paid for in mana.
    /// </summary>
    /// <param name="f">The facts.</param>
    /// <param name="prices">The prices.</param>
    /// <param name="seconds">The fill's seconds.</param>
    public static double IncomingMana(in VitalsPolicyFacts f, in VitalsPrices prices, double seconds) => Math.Max(0, f.BurstPerSecond) * Math.Max(0, seconds) * prices.HealMana;

    /// <summary>M6 heal spend: true when a press pays: its give less its own expected cost and the mana its seconds' incoming costs to heal (<see cref="IncomingMana"/>) is over 0.</summary>
    /// <param name="f">The facts.</param>
    /// <param name="prices">The prices.</param>
    /// <param name="press">The press's pick.</param>
    internal static bool PressPays(in VitalsPolicyFacts f, in VitalsPrices prices, in TransferPick press) => press.Index >= 0 && press.ExpectedGive - press.Cost - IncomingMana(in f, in prices, press.Seconds) > 0;

    /// <summary>
    /// M6 heal spend: the mana a stamina drain of <paramref name="give"/> buys her through the presses it feeds, each paying (<see cref="PressPays"/>): the presses from her stamina with the drain's give in it, less the presses from her stamina as it
    /// is, less the drain's own expected <paramref name="cost"/> and the mana its seconds' incoming costs to heal. A press takes its share of the bar it is cast from (ACE's transfer arithmetic, <see cref="BuffStatMods.Transfer"/>: the
    /// proportion of the source's current), so each press after the first gives less for the same cost: the presses are walked down the bar, not priced at a full bar's rate (<see cref="StaminaDrainNets"/>, which the upkeep's fill still
    /// reads). Over 0: the drain pays. Allocation-free (at most <see cref="MaxPressWalk"/> presses a walk).
    /// </summary>
    /// <param name="f">The facts.</param>
    /// <param name="prices">The prices.</param>
    /// <param name="give">The drain's give (cut to the stamina she is missing).</param>
    /// <param name="cost">The drain's expected mana.</param>
    /// <param name="seconds">The drain's seconds.</param>
    public static double StaminaDrainNet(in VitalsPolicyFacts f, in VitalsPrices prices, double give, double cost, double seconds)
    {
        var with = PressedMana(in f, in prices, (uint)Math.Min(f.StaminaMax, f.Stamina + Math.Max(0, give)));
        var without = PressedMana(in f, in prices, f.Stamina);
        return with - without - cost - IncomingMana(in f, in prices, seconds);
    }

    /// <summary>The most presses a walk of <see cref="PressedMana"/> counts (a press takes at least a share of the bar, so a bar empties in a few).</summary>
    public const int MaxPressWalk = 16;

    /// <summary>The mana the presses that pay give her from a bar of <paramref name="stamina"/>, each at the policy's best-priced level from the bar it is cast from, until one would not pay.</summary>
    private static double PressedMana(in VitalsPolicyFacts f, in VitalsPrices prices, uint stamina)
    {
        var net = 0.0;
        var bar = stamina;
        for (var i = 0; i < MaxPressWalk && bar > 0; i++)
        {
            var press = BestTransfer(f.Presses.Span, bar, uint.MaxValue, 0, prices.Stamina, f.ManaConversion, health: false, mana: true);
            if (!PressPays(in f, in prices, in press) || press.ExpectedTaken <= 0)
            {
                break;
            }

            net += press.ExpectedGive - press.Cost - IncomingMana(in f, in prices, press.Seconds);
            bar = press.ExpectedTaken >= bar ? 0 : bar - (uint)Math.Ceiling(press.ExpectedTaken);
        }

        return net;
    }

    /// <summary>
    /// True when a stamina drain's give, pressed by the best press from a full bar, nets mana over the drain's own cost and its share of the press's (the drain sums of M6 8 H, <c>cache/m6/8/drain-sums.md</c>: with no Mana Conversion
    /// Drain Stamina Other I and Stamina to Mana I cost 10 each, so 16 from an outskirts creature loses mana and 56 from a tusker pays; with Mana Conversion both cost about 1.4 and any tusker pays). Stamina is only a press's fuel to her.
    /// </summary>
    /// <param name="f">The facts.</param>
    /// <param name="give">The drain's give.</param>
    /// <param name="cost">The drain's expected mana.</param>
    public static bool StaminaDrainNets(in VitalsPolicyFacts f, double give, double cost)
    {
        var press = BestTransfer(f.Presses.Span, f.StaminaMax, uint.MaxValue, 0, 0, f.ManaConversion, health: false, mana: true);
        return press.Index >= 0 && press.ExpectedTaken > 0 && (give * (press.ExpectedGive - press.Cost) / press.ExpectedTaken) - cost > 0;
    }

    /// <summary>The lines.</summary>
    /// <param name="f">The facts.</param>
    /// <param name="budget">The budget.</param>
    /// <param name="rules">The ladder's lines.</param>
    public static VitalsLines Lines(in VitalsPolicyFacts f, in VitalsBudget budget, LadderVitalsPolicy rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var presses = f.Presses.Span;
        var drains = f.HealthDrains.Span;
        var cheapestPress = uint.MaxValue;
        foreach (var p in presses)
        {
            cheapestPress = Math.Min(cheapestPress, p.BaseMana);
        }

        var dearestDrain = 0u;
        var drainSeconds = 0.0;
        foreach (var d in drains)
        {
            dearestDrain = Math.Max(dearestDrain, d.BaseMana);
            drainSeconds = Math.Max(drainSeconds, d.Seconds);
        }

        var manaFloor = f.BoltBaseMana + dearestDrain + (cheapestPress == uint.MaxValue ? 0 : cheapestPress);
        var burst = Math.Max(f.BurstPerSecond, rules.HealthBurstFactor * f.DamagePerSecond);
        var healthFloor = f.HealthMax == 0 ? 0 : Math.Min(f.HealthMax - 1, Math.Ceiling(f.BiggestHit + (burst * (f.BoltSeconds + drainSeconds))));
        var drainGive = budget.StaminaShort > 0 ? BestTransfer(f.StaminaDrains.Span, f.FullestStamina > 0 ? f.FullestStamina : f.KillStamina, uint.MaxValue, 0, 0, f.ManaConversion, health: false).Give : 0;
        var stamina = drainGive > 0 ? drainGive : f.StaminaRegen * budget.SecondsAKill; // no drain to refill it: within a kill's regeneration of full
        var ready = f.StaminaMax > stamina ? f.StaminaMax - (uint)Math.Ceiling(stamina) : 0;
        var pullMana = Math.Min(f.ManaMax, manaFloor + (uint)Math.Ceiling(Math.Max(0, budget.ManaShort)));
        var fresh = BestTransfer(drains, (uint)Math.Max(0, f.KillHealth), uint.MaxValue, DamagePrice(in f), 0, f.ManaConversion, health: true);
        var oneDrainOfFull = f.HealthMax > fresh.Give ? f.HealthMax - fresh.Give : 0;
        var pullHealth = Math.Min(f.HealthMax, Math.Max(Math.Max(healthFloor, Math.Ceiling(f.EmergencyLine)), oneDrainOfFull));
        return new VitalsLines(manaFloor, (uint)healthFloor, ready, pullMana, (uint)pullHealth);
    }

    /// <summary>
    /// The decision before a bolt, in this order: under the health floor, the best-priced health drain at once; under the mana floor, the best-priced press (or the stamina drain first when no press pays); the bolt when it more likely
    /// than not kills (<see cref="LadderVitalsPolicy.BoltFirstChance"/>); the best-priced health drain whose whole give fits what she is missing; while mana runs short over a kill and her stamina is ready, the best-priced press when its
    /// whole give fits; while stamina runs short over a kill, the stamina drain when its whole give fits; else the bolt.
    /// <para>M6 heal spend (the owner's ruling of 2026-11-19: her in-fight fills overspent; <c>docs/session.md</c> section 119, the simulator's evidence in <c>docs/fight-sim.md</c>, "With her heals"): every level is priced at its
    /// expected give and cost (<see cref="FillLevel.Chance"/>, <see cref="FillLevel.Lands"/>); a mana fill pays only when the mana it nets is more than the mana the health its seconds cost would take to heal (<see cref="PressPays"/>,
    /// <see cref="StaminaDrainNet"/>: the incoming now over its seconds, at the heal's mana a point; a stamina drain through the presses it feeds, walked down the bar); a health top-up waits for the best-priced level to fit; and with
    /// more than one on her, a health top-up waits for the next kill (<see cref="HealthTopUpWaits"/>, a drain whose damage pays for its seconds excepted). The floors fill at once as before, by the same test of what pays. A mana top-up
    /// that waited while her mana covered the kills on her was measured and dropped: it saved nothing the other rules did not, and in the Tusker Cave's five it ran her dry more often (docs/session.md section 119).</para>
    /// </summary>
    /// <param name="f">The facts.</param>
    /// <param name="rules">The ladder's lines.</param>
    public static VitalsPlan Decide(in VitalsPolicyFacts f, LadderVitalsPolicy rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var budget = Budget(in f, rules);
        var prices = Prices(in f, in budget);
        var lines = Lines(in f, in budget, rules);
        var nextKills = NextBoltKills(in f, f.TargetHealth);
        var upkeep = Upkeep(in f, in budget, in prices);
        var choice = Choose(in f, in budget, in prices, in lines, rules, nextKills, in upkeep);
        return new VitalsPlan(choice, budget, lines, prices, nextKills, upkeep);
    }

    /// <summary>
    /// M6 14 upkeep mana: the target the coming upkeep sets. The kept lines are read soonest due first; a line is inside the window when it falls due within the lead, the seconds a kill (the fight recasts at a moment between kills, and
    /// the fills go between the bolts of the kill before it) and the seconds the fills to the target take at the fill's own pace (the best press from a full bar, its stamina at the stamina's price; with no press, a drink's give a
    /// decision of a bolt's seconds). The target is the way out's line, the expected costs of the lines inside, and the most any one of them can cost over its expected cost (the guard checks each recast at its worst roll, the
    /// lines before it having taken about their expected costs, in whatever order the fight reaches them), capped at her maximum. A line due now also names the health standing for it needs. Allocation-free up to 64 lines.
    /// </summary>
    /// <param name="f">The facts.</param>
    /// <param name="budget">The budget.</param>
    /// <param name="prices">The prices.</param>
    public static VitalsUpkeep Upkeep(in VitalsPolicyFacts f, in VitalsBudget budget, in VitalsPrices prices)
    {
        var lines = f.Upkeep.Span;
        if (lines.IsEmpty)
        {
            return VitalsUpkeep.None;
        }

        var press = BestTransfer(f.Presses.Span, f.StaminaMax, uint.MaxValue, 0, prices.Stamina, f.ManaConversion, health: false, mana: true);
        var secondsAPoint = press.Index >= 0 && !double.IsInfinity(press.Price) ? press.Price : f.ManaDrinkGive > 0 ? f.BoltSeconds / f.ManaDrinkGive : 0;
        var kill = Math.Max(0, budget.SecondsAKill);
        Span<int> order = lines.Length <= 64 ? stackalloc int[lines.Length] : new int[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var at = i;
            while (at > 0 && lines[order[at - 1]].TimeToDue > lines[i].TimeToDue)
            {
                order[at] = order[at - 1];
                at--;
            }

            order[at] = i;
        }

        double expected = 0, margin = 0, wanted = 0, lead = kill, stand = 0;
        var taken = 0;
        for (var k = 0; k < order.Length; k++)
        {
            var line = lines[order[k]];
            var sum = expected + Math.Max(0, line.Expected);
            var over = Math.Max(margin, line.Ceiling - Math.Max(0, line.Expected));
            var want = f.UpkeepReserve + sum + over;
            var gap = Math.Max(0, Math.Min(want, f.ManaMax) - f.Mana);
            var leadNow = kill + (gap * secondsAPoint);
            if (line.TimeToDue > leadNow)
            {
                if (taken == 0)
                {
                    return new VitalsUpkeep(0, 0, 0, line.Name, line.TimeToDue, leadNow, 0);
                }

                break;
            }

            (expected, margin, wanted, lead, taken) = (sum, over, want, leadNow, taken + 1);
            if (line.TimeToDue <= 0)
            {
                stand = Math.Max(stand, line.StandHealth);
            }
        }

        var first = lines[order[0]];
        var wantedMana = (uint)Math.Ceiling(wanted);
        return new VitalsUpkeep(Math.Min(wantedMana, f.ManaMax), wantedMana, taken, first.Name, first.TimeToDue, lead, stand);
    }

    /// <summary>The reason the upkeep's press toward its target waits (<see cref="UpkeepChoice"/>), the log's key.</summary>
    public const string UpkeepPressWaitsWhy = "upkeep: the press toward the target waits: the best-priced level's whole give does not fit the missing mana";

    /// <summary>The reason of the upkeep's press toward its target, the log's key.</summary>
    public const string UpkeepPressTowardWhy = "upkeep: the press toward the target: its whole give fits";

    /// <summary>
    /// M6 14 upkeep mana: the move toward a line's recast, or null when none is due or none can be made (the rules after it decide). The heal first, when a line is due now and standing for it would leave her under the heal line; then, under the
    /// target, the cheapest fill: the press from the stamina she holds when it reaches the target, else a stamina drain whose whole give fits (the press after it gives more), else the press toward it while it gives at least what a
    /// drink would, else a mana drink that fits whole (the potions' own rule: <see cref="ManaDrinks.Fitting"/>, never while they are set aside).
    /// <para>M6 stamina dance (<c>docs/session.md</c> section 121; run 9, <c>cache/m6/sprint/run9/step3-levelup.log</c> 01:37:31 to 01:38:01 UTC: Stamina to Mana Self II pressed at 340, 363, 369 and 372 of 375 mana for 35, 12, 6 and 3,
    /// toward a target of 375 whose wanted 379 was over her maximum): the press toward the target waits as the policy's own press does (<see cref="LadderVitalsPolicy.PressWaitsForBest"/>): only the best-priced level at its whole give, and
    /// only when that give fits the missing mana, so a small gap is not topped up a cast at a time (ACE cuts the give to the missing mana and charges the whole cost, <c>WorldObject_Magic.cs:737-746</c>). While it waits
    /// (<paramref name="waited"/>) no drink stands in for it: a drink goes only when no press is offered or the press that fits gives less.</para>
    /// </summary>
    private static VitalsChoice? UpkeepChoice(in VitalsPolicyFacts f, in VitalsPrices prices, in VitalsUpkeep upkeep, uint healthMissing, uint manaMissing, uint staminaMissing, uint healthSource, uint staminaSource, out bool waited)
    {
        waited = false;
        if (upkeep.StandHealth > f.Health && healthSource > 0)
        {
            var heal = BestTransfer(f.HealthDrains.Span, healthSource, healthMissing, prices.Damage, 0, f.ManaConversion, health: true, fitsOnly: true, manaPrice: prices.Mana);
            if (heal.Index < 0)
            {
                heal = BestTransfer(f.HealthDrains.Span, healthSource, healthMissing, prices.Damage, 0, f.ManaConversion, health: true, manaPrice: prices.Mana); // none fits whole: the heal comes anyway (the recast waits on it)
            }

            if (heal.Index >= 0)
            {
                return new VitalsChoice(VitalsMove.DrainHealth, heal.Index, heal.Give, heal.Price, "upkeep: the heal first: standing for the recast needs the health", Upkeep: true);
            }
        }

        if (f.Mana >= upkeep.Target)
        {
            return null;
        }

        var gap = upkeep.Target - f.Mana;
        var reach = BestTransfer(f.Presses.Span, f.Stamina, manaMissing, 0, prices.Stamina, f.ManaConversion, health: false, mana: true, minGive: gap); // the best-priced level that reaches it (a give cut at the top costs its price)
        if (reach.Index >= 0)
        {
            return new VitalsChoice(VitalsMove.Press, reach.Index, reach.Give, reach.Price, "upkeep: the press reaches the target", Upkeep: true);
        }

        if (staminaSource > 0)
        {
            var drain = BestTransfer(f.StaminaDrains.Span, staminaSource, uint.MaxValue, 0, 0, f.ManaConversion, health: false, manaPrice: prices.Mana);
            if (drain.Index >= 0 && drain.Give <= staminaMissing && StaminaDrainNets(in f, drain.ExpectedGive, drain.Cost))
            {
                return new VitalsChoice(VitalsMove.DrainStamina, drain.Index, drain.Give, drain.Price, "upkeep: the stamina first, the press after it", Upkeep: true);
            }
        }

        var press = BestTransfer(f.Presses.Span, f.Stamina, uint.MaxValue, 0, prices.Stamina, f.ManaConversion, health: false, mana: true); // the best-priced level at its whole give (the policy's own press, PressWaitsForBest)
        var presses = press.Index >= 0;
        var fits = presses && press.Give <= manaMissing;
        var drink = f.ManaDrinkGive > 0 && f.ManaDrinkGive <= manaMissing ? f.ManaDrinkGive : 0u;
        if (fits && press.Give >= drink)
        {
            return new VitalsChoice(VitalsMove.Press, press.Index, press.Give, press.Price, UpkeepPressTowardWhy, Upkeep: true);
        }

        if (drink > 0 && (!presses || fits))
        {
            return new VitalsChoice(VitalsMove.DrinkMana, -1, drink, 0, presses ? "upkeep: the press from her stamina gives less than a drink: a drink toward the target" : "upkeep: no press fits: a drink toward the target", Upkeep: true);
        }

        waited = presses && !fits;
        return null;
    }

    /// <summary>The reason a health top-up waits (<see cref="HealthTopUpWaits"/>), the log's key.</summary>
    public const string HealthTopUpWaitsWhy = "a health top-up waits: the next kill lowers the incoming and another stays on her";

    /// <summary>
    /// M6 heal spend: true when a health top-up (a health drain above the floor whose whole give fits) waits for the next kill. Its seconds are the same now or after the next kill, but every second not killing stands under the incoming of
    /// every creature on her. With more than one on her (<see cref="VitalsPolicyFacts.Attackers"/>) the next kill takes its share out of the incoming and another stays as the drain's source, so the same heal after it costs less health; the
    /// health floor and the emergency stand guard in the meantime. With one on her (or none read) the moment is the hunt's own, as before; a drain whose damage pays for its seconds never waits (<see cref="TransferPick.KillSeconds"/>).
    /// </summary>
    /// <param name="f">The facts.</param>
    public static bool HealthTopUpWaits(in VitalsPolicyFacts f) => f.Attackers > 1;

    private static VitalsChoice Choose(in VitalsPolicyFacts f, in VitalsBudget budget, in VitalsPrices prices, in VitalsLines lines, LadderVitalsPolicy rules, double nextKills, in VitalsUpkeep upkeep)
    {
        var healthMissing = f.HealthMax > f.Health ? f.HealthMax - f.Health : 0;
        var manaMissing = f.ManaMax > f.Mana ? f.ManaMax - f.Mana : 0;
        var staminaMissing = f.StaminaMax > f.Stamina ? f.StaminaMax - f.Stamina : 0;
        var healthSource = (uint)Math.Max(0, f.FullestHealth);
        var staminaSource = f.FullestStamina;

        if (f.Health < lines.HealthFloor && healthSource > 0)
        {
            var heal = BestTransfer(f.HealthDrains.Span, healthSource, healthMissing, prices.Damage, 0, f.ManaConversion, health: true, manaPrice: prices.Mana);
            if (heal.Index >= 0)
            {
                return new VitalsChoice(VitalsMove.DrainHealth, heal.Index, heal.Give, heal.Price, "under the health floor");
            }
        }

        if (f.Mana < lines.ManaFloor)
        {
            var press = BestTransfer(f.Presses.Span, f.Stamina, manaMissing, 0, prices.Stamina, f.ManaConversion, health: false, mana: true);
            if (PressPays(in f, in prices, in press))
            {
                return new VitalsChoice(VitalsMove.Press, press.Index, press.Give, press.Price, "under the mana floor");
            }

            var drain = BestTransfer(f.StaminaDrains.Span, staminaSource, staminaMissing, 0, 0, f.ManaConversion, health: false, manaPrice: prices.Mana);
            if (drain.Index >= 0 && StaminaDrainNet(in f, in prices, drain.ExpectedGive, drain.Cost, drain.Seconds) > 0)
            {
                return new VitalsChoice(VitalsMove.DrainStamina, drain.Index, drain.Give, drain.Price, "under the mana floor: no press pays, the stamina first");
            }

            return new VitalsChoice(VitalsMove.Bolt, -1, 0, 0, "under the mana floor and no fill pays: the mana order decides");
        }

        if (nextKills >= rules.BoltFirstChance)
        {
            return new VitalsChoice(VitalsMove.Bolt, -1, 0, 0, "the next bolt likely kills");
        }

        var upkeepWaited = false;
        if (upkeep.Active && UpkeepChoice(in f, in prices, in upkeep, healthMissing, manaMissing, staminaMissing, healthSource, staminaSource, out upkeepWaited) is { } fill)
        {
            return fill; // M6 14 upkeep mana: a line due within the window: its fill before the optional fills and the bolt (the current kill's finishing bolt went above)
        }

        var healthWaited = false;
        if (budget.HealthShort > 0 && healthSource > 0)
        {
            // M6 heal spend: the best-priced level at its whole give, when that give fits (as a press waits for the best): a level that fits sooner only because it gives less is dearer a point, and one she seldom lands (a
            // fizzle still costs 5) dearer still; the best fits after a few more hits, and the floor and the emergency stand guard meanwhile.
            var heal = BestTransfer(f.HealthDrains.Span, healthSource, uint.MaxValue, prices.Damage, 0, f.ManaConversion, health: true, manaPrice: prices.Mana);
            heal = heal.Give <= healthMissing ? heal : TransferPick.None;
            healthWaited = heal.Index >= 0 && heal.KillSeconds > 0 && HealthTopUpWaits(in f);
            if (heal.Index >= 0 && !healthWaited)
            {
                return new VitalsChoice(VitalsMove.DrainHealth, heal.Index, heal.Give, heal.Price, "the health drain's whole give fits");
            }
        }

        if (budget.ManaShort > 0 && f.Stamina >= lines.StaminaReady)
        {
            var press = rules.PressWaitsForBest
                ? BestTransfer(f.Presses.Span, f.Stamina, uint.MaxValue, 0, prices.Stamina, f.ManaConversion, health: false, mana: true) // the cheapest level, when its whole give fits
                : BestTransfer(f.Presses.Span, f.Stamina, manaMissing, 0, prices.Stamina, f.ManaConversion, health: false, fitsOnly: true, mana: true); // the cheapest of the levels whose whole give fits now
            if (press.Index >= 0 && press.Give <= manaMissing && PressPays(in f, in prices, in press))
            {
                return new VitalsChoice(VitalsMove.Press, press.Index, press.Give, press.Price, "the press's whole give fits");
            }
        }

        if (budget.StaminaShort > 0 && staminaSource > 0)
        {
            var drain = BestTransfer(f.StaminaDrains.Span, staminaSource, uint.MaxValue, 0, 0, f.ManaConversion, health: false, manaPrice: prices.Mana);
            if (drain.Index >= 0 && drain.Give <= staminaMissing && StaminaDrainNet(in f, in prices, drain.ExpectedGive, drain.Cost, drain.Seconds) > 0)
            {
                return new VitalsChoice(VitalsMove.DrainStamina, drain.Index, drain.Give, drain.Price, "the stamina drain's whole give fits");
            }
        }

        return new VitalsChoice(VitalsMove.Bolt, -1, 0, 0, healthWaited ? HealthTopUpWaitsWhy : upkeepWaited ? UpkeepPressWaitsWhy : budget.ManaShort > 0 && f.Stamina < lines.StaminaReady ? "no fill is due: the stamina is not ready for a press" : "no fill is due");
    }

    /// <summary>
    /// The level of <paramref name="levels"/> with the fewest seconds a point given: (its seconds, plus the stamina it takes at <paramref name="staminaPrice"/>, less the damage it deals at <paramref name="damagePrice"/> for a health drain)
    /// over its give (ACE's transfer, <see cref="BuffStatMods.Transfer"/>, cut to <paramref name="missing"/>; the transfer cap binds both), for a press with <paramref name="mana"/> its give less its expected cost, for a drain its own mana at
    /// <paramref name="manaPrice"/>. With <paramref name="fitsOnly"/>, only a level whose whole give fits <paramref name="missing"/>; with <paramref name="minGive"/> (M6 14 upkeep mana), only a level whose net (its give, less its own expected cost for a press) is at least it.
    /// M6 heal spend: every give, take and cost is the expected one at the level's chances (<see cref="FillLevel.Chance"/>, <see cref="FillLevel.Lands"/>): a fizzle costs 5 and gives nothing, a resist costs the cast and gives nothing.
    /// </summary>
    internal static TransferPick BestTransfer(ReadOnlySpan<FillLevel> levels, uint source, uint missing, double damagePrice, double staminaPrice, uint manaConversion, bool health, bool fitsOnly = false, bool mana = false, double manaPrice = 0, uint minGive = 0)
    {
        var best = TransferPick.None;
        foreach (var level in levels)
        {
            var row = level.Row;
            var drainMod = (float)Math.Clamp(level.DrainMod, 0, 1); // M6 undrainable fills: the source's share, as ACE applies it before the cap
            BuffStatMods.Transfer(in row, source, uint.MaxValue, out var takenFull, out var giveFull, drainMod);
            if (giveFull == 0 || (fitsOnly && giveFull > missing))
            {
                continue;
            }

            BuffStatMods.Transfer(in row, source, missing, out var taken, out var give, drainMod);
            if (give == 0)
            {
                continue;
            }

            if (level.Chance <= 0)
            {
                continue;
            }

            // M6 heal spend: at the chance it lands (FillLevel.Chance, Lands): the give and take as expected; the cost a cast's expected cost (resisted or not) or a fizzle's 5 (Player_Magic.cs 566-572).
            var chance = Math.Min(1, level.Chance);
            var landed = chance * Math.Clamp(level.Lands, 0, 1);
            var cost = (chance * ManaConversionModel.ExpectedCost(level.BaseMana, level.Power, manaConversion)) + ((1 - chance) * ManaConversionModel.FizzleMana);
            var takenExpected = landed * taken;
            var seconds = level.Seconds + (health ? -damagePrice * takenExpected : staminaPrice * takenExpected) + (mana ? 0 : manaPrice * cost);
            var net = mana ? (landed * give) - cost : landed * give; // a press's own cost comes out of the mana it gives; a drain's mana is a few points of the mana a kill (Budget counts the health drain's)
            if (net <= 0 || net < minGive)
            {
                continue;
            }

            var price = seconds / net;
            if (best.Index < 0 || price < best.Price)
            {
                best = new TransferPick(level.Index, give, taken, cost, price, level.Seconds, level.Seconds - (health ? damagePrice * takenExpected : 0), landed);
            }
        }

        return best;
    }

    /// <summary>
    /// A level's pick: its index, its give and take when it lands, its expected mana (a fizzle's counted) and price; its cast's seconds; the kill seconds it costs (its seconds less, for a health drain, the damage it is expected to deal counted
    /// as bolt seconds); and the share of its casts that land (neither fizzled nor resisted).
    /// </summary>
    internal readonly record struct TransferPick(int Index, double Give, double Taken, double Cost, double Price, double Seconds = 0, double KillSeconds = 0, double Chance = 1)
    {
        public static TransferPick None => new(-1, 0, 0, 0, double.PositiveInfinity);

        /// <summary>The give as expected: the give when it lands, at the chance it lands.</summary>
        public double ExpectedGive => Chance * Give;

        /// <summary>The take as expected.</summary>
        public double ExpectedTaken => Chance * Taken;
    }
}

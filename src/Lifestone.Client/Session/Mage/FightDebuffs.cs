// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/{WorldObject_Magic.cs, Creature_Combat.cs, SkillFormula.cs, SkillCheck.cs, Monster_Melee.cs, WorldObject_Weapon.cs, Creature_Vitals.cs},
// Entity/{DamageEvent.cs, AttributeFormula.cs} and WorldObjects/Entity/{CreatureAttribute.cs, CreatureSkill.cs} (AGPL-3.0) [with changes: engine-free, allocation-free; ACE's random rolls are replaced by their means and
// landing chances; the fight is valued in all-in seconds, which ACE has no notion of].
using Lifestone.Client.Session.Casting;

namespace Lifestone.Client.Session.Mage;

/// <summary>What a debuff or drain does (M5 item 7 F11, D90). Bafflement is a Focus debuff and Feeblemind a Self debuff (ACE spell table, <c>stat_Mod_Key</c> 5 and 6).</summary>
public enum DebuffKind : byte
{
    /// <summary>Weakness Other: Strength down.</summary>
    Weakness,

    /// <summary>Clumsiness Other: Coordination down.</summary>
    Clumsiness,

    /// <summary>Slowness Other: Quickness down.</summary>
    Slowness,

    /// <summary>Bafflement Other: Focus down (feeds Magic Defense).</summary>
    Bafflement,

    /// <summary>Feeblemind Other: Self down (feeds Magic Defense).</summary>
    Feeblemind,

    /// <summary>Frailty Other: Endurance down (feeds the maximum health).</summary>
    Frailty,

    /// <summary>Magic Yield Other: the Magic Defense skill down.</summary>
    MagicYield,

    /// <summary>Light Weapon Ineptitude Other: the Light Weapons skill down (a tusker's attack skill).</summary>
    LightWeaponsIneptitude,

    /// <summary>Vulnerability Other: the Melee Defense skill down (the target's defence against OUR melee).</summary>
    Vulnerability,

    /// <summary>Defenselessness Other: the Missile Defense skill down (against OUR missiles).</summary>
    Defenselessness,

    /// <summary>Fire Vulnerability Other: the target's fire resistance multiplied (every fire bolt).</summary>
    FireVulnerability,

    /// <summary>Imperil Other: the target's armour level down.</summary>
    Imperil,

    /// <summary>Exhaustion Other: the target's stamina regeneration down.</summary>
    Exhaustion,

    /// <summary>Harm Other: Life damage (a Boost spell with a negative amount).</summary>
    HarmOther,

    /// <summary>Drain Health Other: a Transfer spell, health from the target to the caster.</summary>
    DrainHealthOther,

    /// <summary>Drain Stamina Other: a Transfer spell, stamina from the target to the caster.</summary>
    DrainStaminaOther,
}

/// <summary>The school a spell is cast and resisted with (the dat's <c>School</c>).</summary>
public enum DebuffSchool : byte
{
    /// <summary>Creature Enchantment: the attribute and skill debuffs.</summary>
    CreatureEnchantment,

    /// <summary>Life Magic: Fire Vulnerability, Imperil, Exhaustion, Harm and the drains.</summary>
    LifeMagic,
}

/// <summary>
/// One debuff or drain at one level, as data. <c>Id</c>, <c>Name</c>, <c>BaseMana</c>, <c>Power</c>, <c>DurationSeconds</c> and the school are the SpellTable's (pinned by <c>FightDebuffsDatTests</c>); the effect
/// numbers (<c>Magnitude</c>, <c>Boost</c>, <c>BoostVariance</c>, <c>Proportion</c>, <c>LossPercent</c>, <c>TransferCap</c>) are ACE's world database spell table, which the dat does not hold (the lead's read-only SELECT of
/// 2026-10-28, same method as <see cref="BuffStatMods"/>).
/// </summary>
/// <param name="Id">The spell id.</param>
/// <param name="Name">The dat's name (level VII is a title, e.g. "Senescence").</param>
/// <param name="Kind">What it does.</param>
/// <param name="Level">1 to 7 (VI and VII are <see cref="FightDebuffs.Spells"/>; I to V are resolved from the SpellTable by <see cref="DebuffRows.FromSpellTable"/>, M6 1 f).</param>
/// <param name="School">The casting and resisting school.</param>
/// <param name="BaseMana">The dat's base mana.</param>
/// <param name="Power">The dat's power.</param>
/// <param name="DurationSeconds">The dat's enchantment duration (0 for a Boost or Transfer).</param>
/// <param name="Magnitude">The enchantment's <c>stat_Mod_Val</c>: an additive amount (negative) for an attribute or skill, a multiplier for a resistance or a rate, an armour amount for Imperil.</param>
/// <param name="Boost">A Boost spell's <c>boost</c> (negative for Harm).</param>
/// <param name="BoostVariance">A Boost spell's <c>boost_Variance</c>: ACE rolls an integer between <c>Boost</c> and <c>Boost + BoostVariance</c> (<c>SpellProperties.MaxBoost</c>, WorldObject_Magic.cs:480-490).</param>
/// <param name="Proportion">A Transfer spell's share of the source's current vital.</param>
/// <param name="LossPercent">A Transfer spell's loss (the caster gets <c>1 - LossPercent</c> of what is drained).</param>
/// <param name="TransferCap">A Transfer spell's cap on both amounts (0 is none).</param>
public readonly record struct DebuffSpell(
    uint Id,
    string Name,
    DebuffKind Kind,
    int Level,
    DebuffSchool School,
    uint BaseMana,
    uint Power,
    double DurationSeconds,
    double Magnitude,
    int Boost = 0,
    int BoostVariance = 0,
    double Proportion = 0,
    double LossPercent = 0,
    int TransferCap = 0);

/// <summary>
/// The target as the picker reads it, once a decision. Attributes and skills are the CURRENT values (the debuffs already on it included); every default of 1.0 is "no modifier".
/// </summary>
public readonly struct DebuffTarget
{
    /// <summary>The target's health now.</summary>
    public double RemainingHealth { get; init; }

    /// <summary>The target's maximum health (Frailty shaves it, and ACE clamps the current health to it: Creature_Vitals.cs:107-111).</summary>
    public double MaxHealth { get; init; }

    /// <summary>The target's stamina now (what a Drain Stamina takes a share of).</summary>
    public double Stamina { get; init; }

    /// <summary>Current Strength (the melee damage's attribute modifier).</summary>
    public double Strength { get; init; }

    /// <summary>Current Endurance (the maximum health's attribute).</summary>
    public double Endurance { get; init; }

    /// <summary>Current Quickness (the attack animation's speed, and with Coordination the Melee Defense).</summary>
    public double Quickness { get; init; }

    /// <summary>Current Coordination (with Strength the Light Weapons skill).</summary>
    public double Coordination { get; init; }

    /// <summary>Current Focus (with Self the Magic Defense).</summary>
    public double Focus { get; init; }

    /// <summary>Current Self.</summary>
    public double Self { get; init; }

    /// <summary>The target's effective Magic Defense now (what our spells roll against).</summary>
    public double MagicDefense { get; init; }

    /// <summary>The target's current attack skill (a tusker: Light Weapons, because it wields nothing and has no Unarmed Combat).</summary>
    public double AttackSkill { get; init; }

    /// <summary>The target's damage to the mage, per second, measured (F4's attack notices); what a debuff scales.</summary>
    public double IncomingDamagePerSecond { get; init; }

    /// <summary>The target's attack animation at speed 1.0, in seconds (0: unknown, and Slowness is then reported unscored).</summary>
    public double SwingSeconds { get; init; }

    /// <summary>The target's <c>PowerupTime</c> (the property float): ACE waits a uniform 0 to this after a swing (Monster_Melee.cs:154-156).</summary>
    public double PowerupSeconds { get; init; }

    /// <summary>The Fire Vulnerability already on it (1.0: none).</summary>
    public double FireVulnerabilityMultiplier { get; init; }

    /// <summary>The target's <c>ResistHealthDrain</c> (1.0: none).</summary>
    public double HealthDrainResistance { get; init; }

    /// <summary>The target's <c>ResistStaminaDrain</c> (1.0: none).</summary>
    public double StaminaDrainResistance { get; init; }

    /// <summary>A target with the multipliers at 1.0.</summary>
    public DebuffTarget()
    {
        FireVulnerabilityMultiplier = 1.0;
        HealthDrainResistance = 1.0;
        StaminaDrainResistance = 1.0;
    }
}

/// <summary>The mage as the picker reads it, once a decision: the live skills, the two live rates (and the stamina's), the vitals it can still take, and the war attack the fight repeats.</summary>
public readonly struct DebuffCaster
{
    /// <summary>The live Creature Enchantment skill (buffs included).</summary>
    public double CreatureSkill { get; init; }

    /// <summary>The live Life Magic skill.</summary>
    public double LifeSkill { get; init; }

    /// <summary>The live War Magic skill.</summary>
    public double WarSkill { get; init; }

    /// <summary>The live Mana Conversion skill (the costs follow it: <see cref="ManaConversionModel.ExpectedCost"/>).</summary>
    public double ManaConversion { get; init; }

    /// <summary>The mage's effective Melee Defense (0 when exhausted: Creature_Combat.cs:497). D87: it has no points in it, only what its attributes give.</summary>
    public double MeleeDefense { get; init; }

    /// <summary>Health the mage can still take (ACE scales a drain to it: WorldObject_Magic.cs:735-746).</summary>
    public double MissingHealth { get; init; }

    /// <summary>Stamina the mage can still take.</summary>
    public double MissingStamina { get; init; }

    /// <summary>The dance's seconds a mana point (seed 0.09 at level 80, then measured).</summary>
    public double SecondsPerMana { get; init; }

    /// <summary>The heal's seconds a health point (seeded from Drain Health's yield, then measured).</summary>
    public double SecondsPerHealth { get; init; }

    /// <summary>The seconds a stamina point is worth: its mana through Stamina to Mana at <see cref="SecondsPerMana"/>, or its share of a Revitalize (the caller's reading).</summary>
    public double SecondsPerStamina { get; init; }

    /// <summary>The cast policy's seconds for one debuff or drain (windup plus recovery).</summary>
    public double DebuffCastSeconds { get; init; }

    /// <summary>The war attack the fight repeats: its mean damage when it lands (fire resistance and our own vulnerability already in it).</summary>
    public double WarMeanDamage { get; init; }

    /// <summary>The spread of one landed cast's damage as a share of its mean (a uniform 84 to 168 is 0.19); 0.2 unless set.</summary>
    public double WarDamageCv { get; init; }

    /// <summary>The war attack's cast seconds (windup, recovery and flight as the cast policy has them).</summary>
    public double WarCastSeconds { get; init; }

    /// <summary>The war attack's dat base mana.</summary>
    public uint WarBaseMana { get; init; }

    /// <summary>The war attack's dat power.</summary>
    public uint WarPower { get; init; }

    /// <summary>A caster whose damage spread is 0.2.</summary>
    public DebuffCaster()
    {
        WarDamageCv = 0.2;
    }
}

/// <summary>What one debuff or drain is worth against one target, in all-in seconds (D90).</summary>
/// <param name="HasValue">False when the spell does nothing that matters in this fight; <paramref name="Why"/> says why (one constant string, so the log can say it once).</param>
/// <param name="Why">The reason when there is no value (a constant, never built per call); empty otherwise.</param>
/// <param name="Landing">The chance the spell lands: <c>SkillCheck.GetSkillChance</c> of the school's skill against the target's Magic Defense, factor 0.03.</param>
/// <param name="SavedSeconds">Expected all-in seconds saved over the target's remaining fight (landing already in it): the debuff's effect, or the attack's damage, heal and stamina.</param>
/// <param name="CostSeconds">The cast's cost: its cast seconds (and the target's damage to the mage over them, at the heal's rate) plus its mana at the dance's rate.</param>
/// <param name="ManaCost">The expected mana of the cast at the live Mana Conversion.</param>
/// <param name="ExpectedDamage">An attack's expected damage to the target (landing in it); 0 for a debuff.</param>
/// <param name="ExpectedHeal">A drain's expected health to the mage (landing in it).</param>
/// <param name="ExpectedStamina">A drain's expected stamina to the mage (landing in it).</param>
public readonly record struct DebuffValue(bool HasValue, string Why, double Landing, double SavedSeconds, double CostSeconds, double ManaCost, double ExpectedDamage, double ExpectedHeal, double ExpectedStamina)
{
    /// <summary>Seconds saved minus seconds spent: positive means the spell pays against the war attack alone.</summary>
    public double NetSeconds => SavedSeconds - CostSeconds;

    /// <summary>True when it has value and pays.</summary>
    public bool Pays => HasValue && NetSeconds > 0;
}

/// <summary>
/// The value of the Creature and Life debuffs and the drains a level-80 mage has against a monster, in all-in expected seconds a kill (M5 item 7 F11, D90). The picker (F10) calls <see cref="Evaluate"/> for each
/// candidate and compares its <see cref="DebuffValue.NetSeconds"/> with the war attack's own.
/// <para><b>What ACE does with each debuff, in short</b> (the whole table is in the slice's doc notes). A monster's melee damage is its weapon-or-body-part damage times
/// <c>AttributeMod = 1 + 0.011 (Strength - 55)</c> (Creature_Combat.cs:412-420, SkillFormula.cs:18-22), so Weakness cuts it, and Strength and Coordination also feed the attack skill (Light Weapons = (Strength +
/// Coordination) / 3), which is rolled against the mage's Melee Defense (DamageEvent.cs:396-398); Clumsiness and Light Weapon Ineptitude cut only that roll. Slowness lengthens the swing through
/// <c>GetAnimSpeed</c> (Creature_Combat.cs:510-523). Our war spells roll the target's Magic Defense twice, to land and to hit (WorldObject_Magic.cs:104-113, SpellProjectile.cs:419-421), and Magic Defense =
/// skill + (Focus + Self) / 7 (AttributeFormula.cs), so Magic Yield, Bafflement and Feeblemind raise that landing chance. A war bolt's damage reads the target's resistance, not its armour
/// (SpellProjectile.cs:526-557), so Fire Vulnerability multiplies it (Creature_Properties.cs:100-152, EnchantmentManager.cs:925-939) and Imperil does nothing. Harm is a Boost with a negative amount and the drains
/// are Transfers scaled to what the mage can still take (WorldObject_Magic.cs:474-500, 697-760).</para>
/// <para><b>The model.</b> The war attack needs <c>k = max(1, health / mean + (1 + cv^2) / 2)</c> landed casts (the renewal estimate of the casts to pass a total: 3.29 for 400 health at 146 a cast, where a 60,000-kill
/// simulation gave 3.27), so <c>k / landing</c> casts; each costs its cast seconds and its mana at the dance's rate, and the target hits the
/// mage meanwhile (its damage rate times the cast seconds times the heal's seconds a point). A debuff changes one input (the damage per landed cast, the landing chance, the target's damage rate, the target's
/// health) and the fight is valued again; the saving is the landing chance times the difference. The cost is the debuff's own cast, with the target's damage over it, plus its mana. A debuff lasts 210 s or more, far
/// beyond a fight, so the model does not cap its effect by the duration. The numbers are expectations, not rolls.</para>
/// </summary>
public static class FightDebuffs
{
    /// <summary>The attribute floor ACE enforces on a debuffed attribute whose base is 10 or more (CreatureAttribute.cs:150-154).</summary>
    public const double AttributeFloor = 10;

    /// <summary>ACE's default weapon speed for a creature with no weapon or wand (<c>WorldObject_Weapon.cs:265</c>, "TODO: find default speed").</summary>
    public const double DefaultWeaponSpeed = 40;

    /// <summary>The attack animation speed ceiling and floor (<c>Creature_Combat.cs:508-509</c>).</summary>
    public const double MaxAnimSpeed = 2.0, MinAnimSpeed = 0.5;

    /// <summary>Strength's damage factor (SkillFormula.DefaultMod, SkillFormula.cs:10).</summary>
    public const double StrengthDamageFactor = 0.011;

    /// <summary>The divisors of the dat's formulas: Light Weapons (Strength + Coordination) / 3, Magic Defense (Focus + Self) / 7 (<c>FightDebuffsDatTests</c> pins them to the SkillTable).</summary>
    public const double LightWeaponsDivisor = 3, MagicDefenseDivisor = 7;

    /// <summary>
    /// Health a point of Endurance gives (ACE: Endurance / 2 rounded half up, AttributeFormula.cs:55-71 @95ef426d). M6 3 a2: the target's maximum health is the ground profile's (<c>max_health</c>, ACE's current value, CreatureVital.cs:104-131),
    /// which the picker reads; this slope is only what Frailty's Endurance drop takes off it. TODO(f11-maxhealth-formula) is narrowed to the slope: the dat's SecondaryAttributeTable is still not read for it.
    /// </summary>
    public const double HealthPerEndurance = 0.5;

    private const string NoBaseline = "TODO(f11-baseline): the war attack has no mean damage, cast seconds or health to value against";

    private const string ImperilWhy = "Imperil lowers armour level, which melee and missile damage read and a war bolt does not (SpellProjectile.cs:526-557); the mage is the one who wears armour";

    private const string ExhaustionWhy = "a monster spends no stamina on its attacks (Monster_Melee.cs), and an empty stamina changes only its Melee Defense (Creature_Combat.cs:497), which no spell of ours rolls against";

    private const string OwnSkillWhy = "it lowers a defence skill of the target against melee or missiles, which only OUR melee and missiles roll against (DamageEvent.cs:396-398); our spells roll Magic Defense";

    private const string SwingWhy = "TODO(f11-swing-seconds): Slowness needs the target's attack animation length (DebuffTarget.SwingSeconds); M6 14 attack timing derives it from the dats (Vitals.AttackTiming.MeleeAnimSeconds and PowerupSeconds), which the fight scenario's options do not hold";

    private const string AlreadyWhy = "a Fire Vulnerability at least this strong is already on the target (the products of vulnerabilities above 1 stack, but the same family keeps the stronger: EnchantmentManager.cs:925-939)";

    private const string FullHealthWhy = "ACE scales a drain to what the mage can still take (WorldObject_Magic.cs:735-746) and the mage has none missing";

    private const string NoDamageWhy = "the cast would do nothing: the target has none of the vital to drain";

    /// <summary>The debuffs and drains at levels VI and VII, in the order of the doc notes.</summary>
    public static IReadOnlyList<DebuffSpell> Spells { get; } = BuildSpells();

    /// <summary>
    /// M6 1 f: the effect numbers of levels I to V of every kind (ACE's world database spell table, the pinned world SQL v0.9.296 read offline; the dat holds the rest, <see cref="DebuffRows.FromSpellTable"/>). The attribute and
    /// skill debuffs are -10, -15, -20, -25 and -30 (VI is -35); Fire Vulnerability Other multiplies the fire resistance by 1.1, 1.25, 1.5, 1.75 and 2.0 (VI 2.5; ACE <c>GetVulnerabilityResistanceMod</c>, EnchantmentManager.cs:925-939,
    /// read by <c>Creature_Properties.GetResistanceMod</c>, :100-152, which a war bolt's damage multiplies by, SpellProjectile.cs:486-488); Imperil Other lowers the armour level by 20, 50, 75, 100 and 150.
    /// </summary>
    public static IReadOnlyList<DebuffEffect> LowerLevelEffects { get; } = BuildLowerEffects();

    /// <summary>The effect row of <paramref name="kind"/> at <paramref name="level"/> (1 to 5), or null.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="level">The level.</param>
    public static DebuffEffect? EffectOf(DebuffKind kind, int level)
    {
        var effects = LowerLevelEffects;
        for (var i = 0; i < effects.Count; i++)
        {
            if (effects[i].Kind == kind && effects[i].Level == level)
            {
                return effects[i];
            }
        }

        return null;
    }

    /// <summary>The spell of <paramref name="kind"/> at <paramref name="level"/> (6 or 7), or null.</summary>
    public static DebuffSpell? Find(DebuffKind kind, int level)
    {
        var spells = Spells;
        for (var i = 0; i < spells.Count; i++)
        {
            if (spells[i].Kind == kind && spells[i].Level == level)
            {
                return spells[i];
            }
        }

        return null;
    }

    /// <summary>ACE's <c>SkillFormula.GetAttributeMod</c> for a melee attack (SkillFormula.cs:18-22): <c>max(1 + 0.011 (strength - 55), 1)</c>.</summary>
    public static double AttributeMod(double strength) => Math.Max(1.0 + ((strength - 55.0) * StrengthDamageFactor), 1.0);

    /// <summary>The attack animation speed ACE gives a creature of this Quickness with the default weapon speed (<c>GetAnimSpeed</c>, Creature_Combat.cs:510-523).</summary>
    public static double AnimSpeed(double quickness)
    {
        var divisor = 1.0 - (quickness / 300.0) + (DefaultWeaponSpeed / 150.0);
        return divisor <= 0 ? MaxAnimSpeed : Math.Clamp(1.0 / divisor, MinAnimSpeed, MaxAnimSpeed);
    }

    /// <summary>The value of casting <paramref name="spell"/> now, in all-in seconds over the target's remaining fight; see the class remarks.</summary>
    public static DebuffValue Evaluate(in DebuffSpell spell, in DebuffTarget target, in DebuffCaster caster)
    {
        var mana = ManaConversionModel.ExpectedCost(spell.BaseMana, spell.Power, ToSkill(caster.ManaConversion));
        var cost = (caster.DebuffCastSeconds * (1.0 + (target.IncomingDamagePerSecond * caster.SecondsPerHealth))) + (mana * caster.SecondsPerMana);
        var skill = spell.School == DebuffSchool.LifeMagic ? caster.LifeSkill : caster.CreatureSkill;
        var landing = Chance(skill, target.MagicDefense);

        if (caster.WarMeanDamage <= 0 || caster.WarCastSeconds <= 0 || target.RemainingHealth <= 0)
        {
            return NoValue(NoBaseline, landing, cost, mana);
        }

        var warLanding = Chance(caster.WarSkill, target.MagicDefense);
        var baseline = FightSeconds(in caster, in target, target.RemainingHealth, warLanding, 1.0, 1.0);

        switch (spell.Kind)
        {
            case DebuffKind.Weakness:
                {
                    var strength = Debuffed(target.Strength, spell.Magnitude);
                    var attack = target.AttackSkill - Formula(target.Strength + target.Coordination, LightWeaponsDivisor) + Formula(strength + target.Coordination, LightWeaponsDivisor);
                    var factor = (AttributeMod(strength) / AttributeMod(target.Strength)) * HitRatio(attack, target.AttackSkill, caster.MeleeDefense);
                    return Debuff(in caster, in target, landing, cost, mana, baseline, target.RemainingHealth, warLanding, 1.0, factor);
                }

            case DebuffKind.Clumsiness:
                {
                    var coordination = Debuffed(target.Coordination, spell.Magnitude);
                    var attack = target.AttackSkill - Formula(target.Strength + target.Coordination, LightWeaponsDivisor) + Formula(target.Strength + coordination, LightWeaponsDivisor);
                    return Debuff(in caster, in target, landing, cost, mana, baseline, target.RemainingHealth, warLanding, 1.0, HitRatio(attack, target.AttackSkill, caster.MeleeDefense));
                }

            case DebuffKind.LightWeaponsIneptitude:
                {
                    var attack = Math.Max(0, target.AttackSkill + spell.Magnitude);
                    return Debuff(in caster, in target, landing, cost, mana, baseline, target.RemainingHealth, warLanding, 1.0, HitRatio(attack, target.AttackSkill, caster.MeleeDefense));
                }

            case DebuffKind.Slowness:
                {
                    if (target.SwingSeconds <= 0)
                    {
                        return NoValue(SwingWhy, landing, cost, mana);
                    }

                    var interval = (target.SwingSeconds / AnimSpeed(target.Quickness)) + (target.PowerupSeconds / 2.0);
                    var slowed = (target.SwingSeconds / AnimSpeed(Debuffed(target.Quickness, spell.Magnitude))) + (target.PowerupSeconds / 2.0);
                    return Debuff(in caster, in target, landing, cost, mana, baseline, target.RemainingHealth, warLanding, 1.0, interval / slowed);
                }

            case DebuffKind.MagicYield:
                {
                    var defence = Math.Max(0, target.MagicDefense + spell.Magnitude);
                    return Debuff(in caster, in target, landing, cost, mana, baseline, target.RemainingHealth, Chance(caster.WarSkill, defence), 1.0, 1.0);
                }

            case DebuffKind.Bafflement:
            case DebuffKind.Feeblemind:
                {
                    var focus = spell.Kind == DebuffKind.Bafflement ? Debuffed(target.Focus, spell.Magnitude) : target.Focus;
                    var self = spell.Kind == DebuffKind.Feeblemind ? Debuffed(target.Self, spell.Magnitude) : target.Self;
                    var defence = Math.Max(0, target.MagicDefense - Formula(target.Focus + target.Self, MagicDefenseDivisor) + Formula(focus + self, MagicDefenseDivisor));
                    return Debuff(in caster, in target, landing, cost, mana, baseline, target.RemainingHealth, Chance(caster.WarSkill, defence), 1.0, 1.0);
                }

            case DebuffKind.Frailty:
                {
                    var drop = (target.Endurance - Debuffed(target.Endurance, spell.Magnitude)) * HealthPerEndurance;
                    var health = Math.Min(target.RemainingHealth, Math.Max(0, target.MaxHealth - drop));
                    return Debuff(in caster, in target, landing, cost, mana, baseline, health, warLanding, 1.0, 1.0);
                }

            case DebuffKind.FireVulnerability:
                {
                    var current = Math.Max(1.0, target.FireVulnerabilityMultiplier);
                    if (spell.Magnitude <= current)
                    {
                        return NoValue(AlreadyWhy, landing, cost, mana);
                    }

                    return Debuff(in caster, in target, landing, cost, mana, baseline, target.RemainingHealth, warLanding, spell.Magnitude / current, 1.0);
                }

            case DebuffKind.Imperil:
                return NoValue(ImperilWhy, landing, cost, mana);
            case DebuffKind.Exhaustion:
                return NoValue(ExhaustionWhy, landing, cost, mana);
            case DebuffKind.Vulnerability:
            case DebuffKind.Defenselessness:
                return NoValue(OwnSkillWhy, landing, cost, mana);
            case DebuffKind.HarmOther:
                return Harm(in spell, in caster, in target, landing, cost, mana, baseline, warLanding);
            case DebuffKind.DrainHealthOther:
                return Drain(in spell, in caster, in target, landing, cost, mana, baseline, warLanding, health: true);
            case DebuffKind.DrainStaminaOther:
                return Drain(in spell, in caster, in target, landing, cost, mana, baseline, warLanding, health: false);
            default:
                // M6 1 f (f11-unknown-kind closed): every DebuffKind has its rule above (FightDebuffsTests pins it kind by kind); only a value outside the enum reaches here, a programming error that fails loudly.
                throw new ArgumentOutOfRangeException(nameof(spell), spell.Kind, "FightDebuffs.Evaluate: a debuff kind with no value rule");
        }
    }

    /// <summary>ACE's <c>SkillCheck.GetSkillChance</c> with its 0.03 factor (the resist roll): the chance the caster's skill beats the difficulty (<see cref="ManaConversionModel.SkillChance(int, int, float)"/>).</summary>
    public static double Chance(double skill, double difficulty) => ManaConversionModel.SkillChance((int)Math.Round(skill), (int)Math.Round(difficulty));

    /// <summary>
    /// The all-in seconds of the war attack against <paramref name="health"/>: <c>k = max(1, health / (mean x damageFactor) + (1 + cv^2) / 2)</c> landed casts, so <c>k / landing</c> casts, each its cast seconds plus its mana at the
    /// dance's rate, plus the target's damage to the mage over the cast seconds (its rate scaled by <paramref name="incomingFactor"/>) at the heal's rate.
    /// </summary>
    public static double FightSeconds(in DebuffCaster caster, in DebuffTarget target, double health, double landing, double damageFactor, double incomingFactor)
    {
        if (health <= 0)
        {
            return 0;
        }

        var mean = caster.WarMeanDamage * damageFactor;
        var landed = Math.Max(1.0, (health / mean) + (0.5 * (1.0 + (caster.WarDamageCv * caster.WarDamageCv))));
        var casts = landed / Math.Max(landing, 1e-6);
        var castSeconds = casts * caster.WarCastSeconds;
        var mana = casts * ManaConversionModel.ExpectedCost(caster.WarBaseMana, caster.WarPower, ToSkill(caster.ManaConversion));
        return castSeconds + (mana * caster.SecondsPerMana) + (target.IncomingDamagePerSecond * incomingFactor * castSeconds * caster.SecondsPerHealth);
    }

    private static DebuffValue Debuff(in DebuffCaster caster, in DebuffTarget target, double landing, double cost, double mana, double baseline, double health, double warLanding, double damageFactor, double incomingFactor)
    {
        var after = FightSeconds(in caster, in target, health, warLanding, damageFactor, incomingFactor);
        return new DebuffValue(true, string.Empty, landing, landing * (baseline - after), cost, mana, 0, 0, 0);
    }

    private static DebuffValue Harm(in DebuffSpell spell, in DebuffCaster caster, in DebuffTarget target, double landing, double cost, double mana, double baseline, double warLanding)
    {
        // WorldObject_Magic.cs:480-491: an integer in [min, max] of Boost and Boost + BoostVariance, times the target's health-drain resistance.
        var mean = Math.Abs((spell.Boost + (spell.Boost + spell.BoostVariance)) / 2.0) * target.HealthDrainResistance;
        var damage = Math.Min(mean, target.RemainingHealth);
        var after = FightSeconds(in caster, in target, target.RemainingHealth - damage, warLanding, 1.0, 1.0);
        return new DebuffValue(true, string.Empty, landing, landing * (baseline - after), cost, mana, landing * damage, 0, 0);
    }

    private static DebuffValue Drain(in DebuffSpell spell, in DebuffCaster caster, in DebuffTarget target, double landing, double cost, double mana, double baseline, double warLanding, bool health)
    {
        var source = health ? target.RemainingHealth : target.Stamina;
        var resistance = health ? target.HealthDrainResistance : target.StaminaDrainResistance;
        var missing = health ? caster.MissingHealth : caster.MissingStamina;

        // WorldObject_Magic.cs:721-746: drained = round(source x proportion x resistance), cut to the cap; given = round(drained x (1 - loss)); both scaled down to what the caster can still take.
        var drained = Math.Round(source * spell.Proportion * resistance);
        if (spell.TransferCap != 0 && drained > spell.TransferCap)
        {
            drained = spell.TransferCap;
        }

        var given = Math.Round(drained * (1.0 - spell.LossPercent));
        var room = missing;
        if (spell.TransferCap != 0 && room > spell.TransferCap)
        {
            room = spell.TransferCap;
        }

        if (given > room)
        {
            drained = given > 0 ? Math.Round(drained * (room / given)) : 0;
            given = room;
        }

        if (source <= 0)
        {
            return NoValue(NoDamageWhy, landing, cost, mana);
        }

        if (given <= 0)
        {
            return NoValue(FullHealthWhy, landing, cost, mana);
        }

        if (!health)
        {
            // The target's stamina does not matter to the fight; the mage's gain does.
            return new DebuffValue(true, string.Empty, landing, landing * given * caster.SecondsPerStamina, cost, mana, 0, 0, landing * given);
        }

        var after = FightSeconds(in caster, in target, target.RemainingHealth - drained, warLanding, 1.0, 1.0);
        var saved = baseline - after + (given * caster.SecondsPerHealth);
        return new DebuffValue(true, string.Empty, landing, landing * saved, cost, mana, landing * drained, landing * given, 0);
    }

    private static DebuffValue NoValue(string why, double landing, double cost, double mana) => new(false, why, landing, 0, cost, mana, 0, 0, 0);

    private static double HitRatio(double attackAfter, double attackBefore, double meleeDefense)
    {
        var before = Chance(attackBefore, meleeDefense);
        return before <= 0 ? 1.0 : Chance(attackAfter, meleeDefense) / before;
    }

    private static double Debuffed(double attribute, double magnitude) => Math.Max(AttributeFloor, attribute + magnitude);

    private static double Formula(double sum, double divisor) => Math.Round(sum / divisor, MidpointRounding.AwayFromZero);

    private static uint ToSkill(double skill) => skill <= 0 ? 0u : (uint)Math.Round(skill);

    private static DebuffEffect[] BuildLowerEffects()
    {
        var rows = new List<DebuffEffect>();
        double[] attribute = [-10, -15, -20, -25, -30];
        foreach (var kind in new[] { DebuffKind.Weakness, DebuffKind.Clumsiness, DebuffKind.Slowness, DebuffKind.Bafflement, DebuffKind.Feeblemind, DebuffKind.Frailty, DebuffKind.MagicYield, DebuffKind.LightWeaponsIneptitude, DebuffKind.Vulnerability, DebuffKind.Defenselessness })
        {
            for (var l = 1; l <= 5; l++)
            {
                rows.Add(new DebuffEffect(kind, l, attribute[l - 1]));
            }
        }

        double[] fire = [1.1, 1.25, 1.5, 1.75, 2.0];
        double[] imperil = [-20, -50, -75, -100, -150];
        double[] exhaustion = [0.91, 0.8, 0.71, 0.65, 0.59];
        (int Boost, int Variance)[] harm = [(-4, -2), (-7, -5), (-10, -9), (-16, -17), (-24, -23)];
        double[] loss = [-1, -0.6, -0.33, 0, 0];
        int[] cap = [60, 80, 100, 100, 120];
        double[] healthShare = [0.25, 0.25, 0.25, 0.25, 0.3];
        double[] staminaShare = [0.1, 0.15, 0.2, 0.25, 0.3];
        for (var l = 1; l <= 5; l++)
        {
            var i = l - 1;
            rows.Add(new DebuffEffect(DebuffKind.FireVulnerability, l, fire[i]));
            rows.Add(new DebuffEffect(DebuffKind.Imperil, l, imperil[i]));
            rows.Add(new DebuffEffect(DebuffKind.Exhaustion, l, exhaustion[i]));
            rows.Add(new DebuffEffect(DebuffKind.HarmOther, l, 0, Boost: harm[i].Boost, BoostVariance: harm[i].Variance));
            rows.Add(new DebuffEffect(DebuffKind.DrainHealthOther, l, 0, Proportion: healthShare[i], LossPercent: loss[i], TransferCap: cap[i]));
            rows.Add(new DebuffEffect(DebuffKind.DrainStaminaOther, l, 0, Proportion: staminaShare[i], LossPercent: loss[i], TransferCap: cap[i]));
        }

        return [.. rows];
    }

    private static DebuffSpell[] BuildSpells()
    {
        const DebuffSchool ce = DebuffSchool.CreatureEnchantment;
        const DebuffSchool life = DebuffSchool.LifeMagic;
        return
        [
            new(1343, "Weakness Other VI", DebuffKind.Weakness, 6, ce, 60, 275, 210, -35),
            new(2088, "Senescence", DebuffKind.Weakness, 7, ce, 70, 325, 240, -40),
            new(1396, "Clumsiness Other VI", DebuffKind.Clumsiness, 6, ce, 60, 275, 210, -35),
            new(2056, "Ataxia", DebuffKind.Clumsiness, 7, ce, 70, 325, 240, -40),
            new(1420, "Slowness Other VI", DebuffKind.Slowness, 6, ce, 60, 275, 210, -35),
            new(2084, "Belly of Lead", DebuffKind.Slowness, 7, ce, 70, 325, 240, -40),
            new(1444, "Bafflement Other VI", DebuffKind.Bafflement, 6, ce, 60, 275, 210, -35),
            new(2054, "Synaptic Misfire", DebuffKind.Bafflement, 7, ce, 70, 325, 240, -40),
            new(1468, "Feeblemind Other VI", DebuffKind.Feeblemind, 6, ce, 60, 275, 210, -35),
            new(2064, "Self Loathing", DebuffKind.Feeblemind, 7, ce, 70, 325, 240, -40),
            new(1372, "Frailty Other VI", DebuffKind.Frailty, 6, ce, 60, 275, 210, -35),
            new(2068, "Brittle Bones", DebuffKind.Frailty, 7, ce, 70, 325, 240, -40),
            new(285, "Magic Yield Other VI", DebuffKind.MagicYield, 6, ce, 60, 275, 210, -35),
            new(2282, "Futility", DebuffKind.MagicYield, 7, ce, 70, 325, 240, -40),
            new(309, "Light Weapon Ineptitude Other VI", DebuffKind.LightWeaponsIneptitude, 6, ce, 60, 275, 210, -35),
            new(2200, "Light Weapon Ineptitude Other VII", DebuffKind.LightWeaponsIneptitude, 7, ce, 70, 325, 240, -40),
            new(234, "Vulnerability Other VI", DebuffKind.Vulnerability, 6, ce, 60, 275, 210, -35),
            new(2318, "Gravity Well", DebuffKind.Vulnerability, 7, ce, 70, 325, 240, -40),
            new(267, "Defenselessness Other VI", DebuffKind.Defenselessness, 6, ce, 60, 275, 210, -35),
            new(2228, "Broadside of a Barn", DebuffKind.Defenselessness, 7, ce, 70, 325, 240, -40),
            new(1108, "Fire Vulnerability Other VI", DebuffKind.FireVulnerability, 6, life, 60, 275, 210, 2.5),
            new(2170, "Inferno's Gift", DebuffKind.FireVulnerability, 7, life, 70, 325, 240, 2.85),
            new(1327, "Imperil Other VI", DebuffKind.Imperil, 6, life, 60, 275, 210, -200),
            new(2074, "Gossamer Flesh", DebuffKind.Imperil, 7, life, 70, 325, 240, -225),
            new(199, "Exhaustion Other VI", DebuffKind.Exhaustion, 6, life, 60, 275, 210, 0.54),
            new(2176, "Enervation", DebuffKind.Exhaustion, 7, life, 70, 325, 240, 0.4),
            new(1176, "Harm Other VI", DebuffKind.HarmOther, 6, life, 60, 275, 0, 0, Boost: -33, BoostVariance: -30),
            new(2070, "Heart Rend", DebuffKind.HarmOther, 7, life, 70, 325, 0, 0, Boost: -42, BoostVariance: -37),
            new(1242, "Drain Health Other VI", DebuffKind.DrainHealthOther, 6, life, 60, 288, 0, 0, Proportion: 0.4, LossPercent: 0.25, TransferCap: 150),
            new(2328, "Vitality Siphon", DebuffKind.DrainHealthOther, 7, life, 60, 325, 0, 0, Proportion: 0.5, LossPercent: 0.5, TransferCap: 180),
            new(1254, "Drain Stamina Other VI", DebuffKind.DrainStaminaOther, 6, life, 60, 288, 0, 0, Proportion: 0.4, LossPercent: 0.25, TransferCap: 150),
            new(2330, "Vigor Siphon", DebuffKind.DrainStaminaOther, 7, life, 60, 325, 0, 0, Proportion: 0.5, LossPercent: 0.5, TransferCap: 180),
        ];
    }
}

/// <summary>
/// The effect numbers of one debuff or drain level that the dat does not hold (M6 1 f): ACE's world database spell table, read offline from the pinned world SQL v0.9.296 (<c>SELECT id, name, stat_Mod_Val, boost,
/// boost_Variance, proportion, loss_Percent, transfer_Cap FROM spell WHERE name = '&lt;line&gt; &lt;I..V&gt;'</c>; the rows are named in the slice's doc notes). The same meaning as <see cref="DebuffSpell"/>'s fields.
/// </summary>
/// <param name="Kind">The kind.</param>
/// <param name="Level">1 to 5.</param>
/// <param name="Magnitude">The enchantment's <c>stat_Mod_Val</c>.</param>
/// <param name="Boost">A Boost's <c>boost</c>.</param>
/// <param name="BoostVariance">A Boost's <c>boost_Variance</c>.</param>
/// <param name="Proportion">A Transfer's <c>proportion</c>.</param>
/// <param name="LossPercent">A Transfer's <c>loss_Percent</c> (negative at I to III: the caster gets more than is drained, ACE's <c>1 - LossPercent</c>, WorldObject_Magic.cs:721-746).</param>
/// <param name="TransferCap">A Transfer's <c>transfer_Cap</c>.</param>
public readonly record struct DebuffEffect(DebuffKind Kind, int Level, double Magnitude, int Boost = 0, int BoostVariance = 0, double Proportion = 0, double LossPercent = 0, int TransferCap = 0);

/// <summary>
/// The debuff and drain rows the fight's picker scores (M6 1 f, <c>f10-debuff-levels</c> closed): VI and VII are <see cref="FightDebuffs.Spells"/> (unchanged); I to V are built at run time from the client's SpellTable, the id, name,
/// school, base mana, power and duration of the spell named "&lt;the VI row's line&gt; &lt;numeral&gt;" in the VI row's school (the lowest id when the dat holds several, as for Light Weapon Ineptitude Other's quest copies), with the
/// effect numbers of <see cref="FightDebuffs.LowerLevelEffects"/>. A level the table lacks is left out and named. Lookups are allocation-free.
/// </summary>
public sealed class DebuffRows
{
    private const int LevelSlots = 8;
    private static readonly int Kinds = Enum.GetValues<DebuffKind>().Length;
    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V"];
    private readonly DebuffSpell?[] _rows = new DebuffSpell?[Kinds * LevelSlots];
    private readonly List<DebuffSpell> _all = [];

    private DebuffRows(IEnumerable<DebuffSpell> rows)
    {
        foreach (var row in rows)
        {
            _rows[((int)row.Kind * LevelSlots) + row.Level] = row;
            _all.Add(row);
        }
    }

    /// <summary>The VI and VII rows alone (the unit tests' and the arena's default).</summary>
    public static DebuffRows Standard { get; } = new(FightDebuffs.Spells);

    /// <summary>Every row held.</summary>
    public IReadOnlyList<DebuffSpell> All => _all;

    /// <summary>The row of <paramref name="kind"/> at <paramref name="level"/> (1 to 7), or null.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="level">The level.</param>
    public DebuffSpell? Find(DebuffKind kind, int level) => level is >= 1 and <= 7 && (int)kind < Kinds ? _rows[((int)kind * LevelSlots) + level] : null;

    /// <summary>The row whose spell is named <paramref name="name"/>, or null.</summary>
    /// <param name="name">The spell's name.</param>
    public DebuffSpell? Named(string name)
    {
        for (var i = 0; i < _all.Count; i++)
        {
            if (string.Equals(_all[i].Name, name, StringComparison.Ordinal))
            {
                return _all[i];
            }
        }

        return null;
    }

    /// <summary>
    /// VI and VII plus levels I to V resolved from <paramref name="spells"/> (the client's SpellTable): <paramref name="missing"/> names every level the table does not hold (that level is then not scored).
    /// </summary>
    /// <param name="spells">The SpellTable by id.</param>
    /// <param name="missing">The levels left out, one text each ("Weakness Other III: not in the SpellTable").</param>
    public static DebuffRows FromSpellTable(IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo> spells, out IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(spells);
        var wanted = new Dictionary<string, (DebuffKind Kind, int Level, DatReaderWriter.Enums.MagicSchool School)>(StringComparer.Ordinal);
        foreach (var six in FightDebuffs.Spells)
        {
            if (six.Level != 6 || !six.Name.EndsWith(" VI", StringComparison.Ordinal))
            {
                continue;
            }

            var line = six.Name[..^3];
            var school = six.School == DebuffSchool.LifeMagic ? DatReaderWriter.Enums.MagicSchool.LifeMagic : DatReaderWriter.Enums.MagicSchool.CreatureEnchantment;
            for (var level = 1; level <= 5; level++)
            {
                wanted[line + " " + Numerals[level - 1]] = (six.Kind, level, school);
            }
        }

        var found = new Dictionary<string, Lifestone.Data.Magic.SpellInfo>(StringComparer.Ordinal);
        foreach (var info in spells.Values)
        {
            if (wanted.TryGetValue(info.Name, out var w) && info.School == w.School && (!found.TryGetValue(info.Name, out var held) || info.Id < held.Id))
            {
                found[info.Name] = info;
            }
        }

        var rows = new List<DebuffSpell>(FightDebuffs.Spells);
        var gaps = new List<string>();
        foreach (var (name, w) in wanted)
        {
            if (!found.TryGetValue(name, out var info))
            {
                gaps.Add(name + ": not in the SpellTable");
                continue;
            }

            if (FightDebuffs.EffectOf(w.Kind, w.Level) is not { } e)
            {
                gaps.Add(name + ": no effect row in FightDebuffs.LowerLevelEffects");
                continue;
            }

            var school = w.School == DatReaderWriter.Enums.MagicSchool.LifeMagic ? DebuffSchool.LifeMagic : DebuffSchool.CreatureEnchantment;
            rows.Add(new DebuffSpell(info.Id, info.Name, w.Kind, w.Level, school, info.BaseMana, info.Power, info.Duration, e.Magnitude, e.Boost, e.BoostVariance, e.Proportion, e.LossPercent, e.TransferCap));
        }

        missing = gaps;
        return new DebuffRows(rows);
    }
}

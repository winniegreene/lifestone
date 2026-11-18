// Follows ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/SpellProjectile.cs (OnCollideObject, DamageTarget's ratings step) and Player_Magic.cs (GetCastingPreCheckStatus, DoWindupGestures, DoCastGesture,
// FinishCast) (AGPL-3.0): a projectile's strike and a cast's parts, re-expressed around calls into ACE's own functions. No ACE source is copied (docs/third-party/fight-sim.md).

using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Physics.Animation;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>One of her spells thrown at one creature many times through ACE's own code.</summary>
/// <param name="Spell">The spell.</param>
/// <param name="CastChance">Her chance the cast does not fizzle (Player_Magic.cs GetCastingPreCheckStatus: SkillCheck.GetMagicSkillChance, the 0.07 curve, her skill in the spell's school against the spell's power).</param>
/// <param name="LandChance">The chance a cast that leaves her hand is not resisted (WorldObject_Magic.cs MagicDefenseCheck: SkillCheck.GetSkillChance, the 0.03 curve, her skill in the spell's school against the creature's effective Magic Defense).</param>
/// <param name="Damage">The damage of each sampled cast that did not fizzle, as the creature's health would lose it (rounded as SpellProjectile.DamageTarget rounds); 0 where the creature resisted.</param>
/// <param name="CastSeconds">The server's time for one cast: the windup gestures and the cast gesture at the player's cast speed, then the return to the ready stance.</param>
/// <param name="ManaPerCast">The mean mana one successful cast costs her (Creature_Magic.cs CalculateManaUsage, sampled: Mana Conversion rolls).</param>
internal sealed record Strike(Spell Spell, double CastChance, double LandChance, int[] Damage, double CastSeconds, double ManaPerCast)
{
    /// <summary>Whether each sampled cast was a critical hit.</summary>
    public bool[] Critical { get; init; } = [];

    /// <summary>The share of landed casts that were critical.</summary>
    public double CriticalShare => Damage.Count(d => d > 0) == 0 ? 0 : Critical.Count(c => c) / (double)Damage.Count(d => d > 0);

    /// <summary>The landed casts that were not critical.</summary>
    public IEnumerable<int> Plain => Damage.Where((d, i) => d > 0 && !Critical[i]);

    /// <summary>The landed casts that were critical.</summary>
    public IEnumerable<int> Criticals => Damage.Where((d, i) => d > 0 && Critical[i]);

    /// <summary>The mean damage of a cast that left her hand (resists count as 0).</summary>
    public double MeanPerCast => Damage.Length == 0 ? 0 : Damage.Average();

    /// <summary>The mean damage of a cast that landed.</summary>
    public double MeanLanded => Damage.Where(d => d > 0).DefaultIfEmpty(0).Average();

    /// <summary>The largest sampled hit.</summary>
    public int MaxLanded => Damage.DefaultIfEmpty(0).Max();

    /// <summary>The smallest sampled hit that landed.</summary>
    public int MinLanded => Damage.Where(d => d > 0).DefaultIfEmpty(0).Min();

    /// <summary>The share of sampled casts that landed (to set beside <see cref="LandChance"/>).</summary>
    public double LandedShare => Damage.Length == 0 ? 0 : Damage.Count(d => d > 0) / (double)Damage.Length;

    /// <summary>The mean damage a second of casting, fizzles included.</summary>
    public double DamagePerSecond => CastSeconds <= 0 ? 0 : MeanPerCast * CastChance / CastSeconds;

    /// <summary>The mean mana an attempt costs: a fizzle costs 5 (Player_Magic.cs CalculateManaUsage).</summary>
    public double ManaPerAttempt => (ManaPerCast * CastChance) + (5 * (1 - CastChance));
}

/// <summary>Her spells on a creature.</summary>
internal static class Strikes
{
    /// <summary>The server's seconds for one cast of <paramref name="spell"/> by <paramref name="her"/>.</summary>
    /// <remarks>
    /// Player_Magic.cs: DoWindupGestures and DoCastGesture play the formula's windup gestures and its cast gesture at <c>Player.CastSpeed</c> (SpellFormula.GetCastTime sums them), the spell takes effect at the end of the cast
    /// gesture, and FinishCast plays the return to the ready stance at speed 1 before she is free again (<c>IsBusy</c>). The formula is her own (GetPlayerFormula: it depends on her account and on a held focus).
    /// </remarks>
    public static double CastSeconds(Player her, Spell spell)
    {
        spell.Formula.GetPlayerFormula(her);
        var cast = spell.Formula.GetCastTime(her.MotionTableId, Player.CastSpeed);
        var recoil = MotionTable.GetAnimationLength(her.MotionTableId, MotionStance.Magic, spell.Formula.CastGesture, MotionCommand.Ready);
        return cast + recoil;
    }

    /// <summary>The parts of one cast's time, as lines.</summary>
    public static string CastWorking(Player her, Spell spell)
    {
        spell.Formula.GetPlayerFormula(her);
        var text = new System.Text.StringBuilder();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        text.AppendLine(string.Create(culture, $"  formula: scarabs {string.Join(", ", spell.Formula.Scarabs)}; fast cast {spell.Flags.HasFlag(SpellFlags.FastCast)}"));
        foreach (var gesture in spell.Formula.WindupGestures)
        {
            text.AppendLine(string.Create(culture, $"    windup {gesture}: {MotionTable.GetAnimationLength(her.MotionTableId, MotionStance.Magic, gesture, Player.CastSpeed):0.000} s at speed {Player.CastSpeed}"));
        }

        text.AppendLine(string.Create(culture, $"    cast gesture {spell.Formula.CastGesture}: {MotionTable.GetAnimationLength(her.MotionTableId, MotionStance.Magic, spell.Formula.CastGesture, Player.CastSpeed):0.000} s at speed {Player.CastSpeed}"));
        text.AppendLine(string.Create(culture, $"    return to ready: {MotionTable.GetAnimationLength(her.MotionTableId, MotionStance.Magic, spell.Formula.CastGesture, MotionCommand.Ready):0.000} s"));
        text.AppendLine(string.Create(culture, $"  one cast: {CastSeconds(her, spell):0.00} s"));
        return text.ToString();
    }

    /// <summary>Her chance a cast of <paramref name="spell"/> does not fizzle (Player_Magic.cs GetCastingPreCheckStatus).</summary>
    public static double CastChance(Player her, Spell spell)
    {
        var skill = (int)her.GetCreatureSkill(spell.School).Current;
        var difficulty = (int)spell.Power;
        return skill > 0 && skill >= difficulty - 50 ? SkillCheck.GetMagicSkillChance(skill, difficulty) : 0.0;
    }

    /// <summary>The chance <paramref name="target"/> does not resist <paramref name="spell"/> cast by <paramref name="caster"/> (WorldObject_Magic.cs TryResistSpell and MagicDefenseCheck).</summary>
    public static double LandChance(Creature caster, Creature target, Spell spell)
    {
        if (!spell.IsResistable || spell.IsSelfTargeted)
        {
            return 1.0;
        }

        WorldObject.MagicDefenseCheck(caster.GetCreatureSkill(spell.School).Current, target.GetEffectiveMagicDefense(), out var resistChance);
        return 1.0 - resistChance;
    }

    /// <summary>
    /// Throws <paramref name="spell"/> (one projectile: a bolt, a streak or an arc) from <paramref name="caster"/> at <paramref name="target"/> <paramref name="casts"/> times through SpellProjectile.CalculateDamage, the function a
    /// projectile's strike calls (the resist roll, the critical roll, the skill bonus, the elemental and slayer modifiers of the caster's wand, the target's resistance), then the ratings step of
    /// SpellProjectile.DamageTarget, which applies the damage: the caster's damage rating, a player's heritage and sneak-attack modifiers, the target's damage-resistance rating, and on a critical both critical ratings.
    /// The result is the health the target would lose, rounded as DamageTarget rounds it. Nothing is applied to either object.
    /// </summary>
    /// <returns>The damage of each cast (0 where resisted) and whether it was critical.</returns>
    public static (int[] Damage, bool[] Critical) SampleProjectile(Creature caster, Creature target, Spell spell, int casts)
    {
        if (WorldObjectFactory.CreateNewWorldObject(spell.Wcid) is not SpellProjectile projectile)
        {
            throw new InvalidOperationException($"spell {spell.Id} '{spell.Name}' names weenie {spell.Wcid}, which is not a spell projectile");
        }

        projectile.Setup(spell, SpellProjectile.GetProjectileSpellType(spell.Id));
        projectile.ProjectileSource = caster;
        projectile.ProjectileTarget = target;
        projectile.ProjectileLauncher = caster.GetEquippedWand();
        var damage = new int[casts];
        var criticals = new bool[casts];
        for (var i = 0; i < casts; i++)
        {
            (damage[i], criticals[i]) = Strike(projectile, caster, target);
        }

        projectile.Destroy(false);
        return (damage, criticals);
    }

    /// <summary>One strike of <paramref name="projectile"/>: SpellProjectile.CalculateDamage, then DamageTarget's ratings step (SpellProjectile.cs DamageTarget, the health branch).</summary>
    public static (int Damage, bool Critical) Strike(SpellProjectile projectile, Creature caster, Creature target)
    {
        bool critical = false, criticalDefended = false, overpower = false;
        var hit = projectile.CalculateDamage(caster, target, ref critical, ref criticalDefended, ref overpower);
        if (hit is null)
        {
            return (0, false);
        }

        float heritage = 1.0f, sneak = 1.0f;
        if (caster is Player player)
        {
            sneak = player.GetSneakAttackMod(target);
            heritage = player.GetHeritageBonus(player.GetEquippedWand()) ? 1.05f : 1.0f;
        }

        var rating = Creature.AdditiveCombine(Creature.GetPositiveRatingMod(caster.GetDamageRating()), heritage, sneak);
        var resistRating = target.GetDamageResistRatingMod(CombatType.Magic);
        if (critical)
        {
            rating = Creature.AdditiveCombine(rating, Creature.GetPositiveRatingMod(caster.GetCritDamageRating()));
            resistRating = Creature.AdditiveCombine(resistRating, Creature.GetNegativeRatingMod(target.GetCritDamageResistRating()));
        }

        return ((int)Math.Round(hit.Value * rating * resistRating), critical);
    }

    /// <summary>One of her single-projectile spells against <paramref name="monster"/>.</summary>
    public static Strike Of(Player her, Creature monster, Spell spell, int casts)
    {
        var (damage, criticals) = SampleProjectile(her, monster, spell, casts);
        double mana = 0;
        const int ManaRolls = 2000;
        for (var i = 0; i < ManaRolls; i++)
        {
            mana += her.CalculateManaUsage(her, spell, monster);
        }

        return new Strike(spell, CastChance(her, spell), LandChance(her, monster, spell), damage, CastSeconds(her, spell), mana / ManaRolls) { Critical = criticals };
    }

    /// <summary>Her attack spells with one projectile, from her spell book: the highest level she knows of each name family (Flame Bolt, Frost Streak, ...).</summary>
    public static IReadOnlyList<Spell> BoltsOf(Player her)
    {
        var best = new Dictionary<string, Spell>(StringComparer.Ordinal);
        foreach (var id in her.Biota.PropertiesSpellBook?.Keys ?? [])
        {
            var spell = new Spell((uint)id);
            if (spell.NotFound || spell.School != MagicSchool.WarMagic || spell.NumProjectiles != 1 || spell.IsSelfTargeted)
            {
                continue;
            }

            var family = Family(spell.Name);
            if (!best.TryGetValue(family, out var had) || spell.Power > had.Power)
            {
                best[family] = spell;
            }
        }

        return [.. best.Values.OrderBy(s => s.Name, StringComparer.Ordinal)];
    }

    /// <summary>A spell's name without its level numeral ("Flame Bolt VI" gives "Flame Bolt").</summary>
    public static string Family(string name)
    {
        var cut = name.LastIndexOf(' ');
        return cut > 0 && name[(cut + 1)..].All(c => c is 'I' or 'V' or 'X') ? name[..cut] : name;
    }
}

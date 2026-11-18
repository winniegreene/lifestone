// Follows ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Monster_Tick.cs (Monster_Tick), Monster_Combat.cs (AttackReady, NextMagicAttackTime, Attack), Monster_Melee.cs (MeleeAttack, DoSwingMotion),
// Monster_Magic.cs (MagicAttack, UseMana) and Monster_Missile.cs (LaunchMissile) (AGPL-3.0): the order and timing of a monster's attacks, re-expressed around calls into ACE's own functions. No ACE source is copied
// (docs/third-party/fight-sim.md).

using System.Globalization;
using System.Reflection;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Physics.Animation;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>How a creature's attack reaches her.</summary>
internal enum BlowKind
{
    /// <summary>A melee swing (one strike frame of it).</summary>
    Melee,

    /// <summary>A missile from its launcher or its thrown weapon.</summary>
    Missile,

    /// <summary>A spell with projectiles: each row is one projectile.</summary>
    SpellProjectile,

    /// <summary>A spell that takes from her vitals at once (a drain, a harm).</summary>
    SpellDirect,

    /// <summary>A spell that lays an enchantment on her (a vulnerability, an imperil, a skill or attribute debuff).</summary>
    SpellDebuff,

    /// <summary>A spell the creature casts on itself or that harms nobody (a heal, a buff).</summary>
    SpellOther,
}

/// <summary>One attack of a creature on her, as ACE resolved it.</summary>
/// <param name="Fight">Which simulated fight it belongs to.</param>
/// <param name="Seconds">When it struck, in seconds from that fight's start.</param>
/// <param name="Kind">How it reaches her.</param>
/// <param name="What">The spell's name, or the damage type of a physical attack.</param>
/// <param name="Landed">Whether it was not evaded or resisted.</param>
/// <param name="Damage">The health she loses.</param>
internal sealed record Blow(int Fight, double Seconds, BlowKind Kind, string What, bool Landed, int Damage)
{
    /// <summary>Whether ACE rolled a critical hit.</summary>
    public bool Critical { get; init; }

    /// <summary>Where a physical attack struck her (ACE's BodyPart), empty for a spell.</summary>
    public string Part { get; init; } = string.Empty;

    /// <summary>ACE's own working for a physical attack (DamageEvent's fields), for the explain view.</summary>
    public string Working { get; init; } = string.Empty;
}

/// <summary>A creature's attacks on her over many simulated fights, each begun with the creature fresh (full health, stamina and mana, nothing of its laid on her).</summary>
/// <param name="FightSeconds">How long each fight ran.</param>
/// <param name="Fights">How many fights ran.</param>
/// <param name="Blows">Every attack.</param>
/// <param name="Notes">What was not simulated, each a named gap (never a silent guess).</param>
internal sealed record BlowReport(double FightSeconds, int Fights, IReadOnlyList<Blow> Blows, IReadOnlyList<string> Notes)
{
    private double[][]? _times;
    private double[][]? _prefix;

    /// <summary>All the simulated seconds.</summary>
    public double Seconds => FightSeconds * Fights;

    /// <summary>Her mean health loss a second over a whole fight.</summary>
    public double DamagePerSecond => Seconds <= 0 ? 0 : Blows.Sum(b => (double)b.Damage) / Seconds;

    /// <summary>Her mean health loss within the first <paramref name="seconds"/> of a fight (the creature's opening counts in full: a caster spends its mana first).</summary>
    public double MeanDamageWithin(double seconds) => Fights <= 0 ? 0 : Enumerable.Range(0, Fights).Average(f => DamageWithin(f, seconds));

    /// <summary>Her health loss within the first <paramref name="seconds"/> of fight <paramref name="fight"/>.</summary>
    public double DamageWithin(int fight, double seconds)
    {
        if (_times is null)
        {
            _times = new double[Fights][];
            _prefix = new double[Fights][];
            foreach (var group in Blows.Where(b => b.Damage > 0).GroupBy(b => b.Fight))
            {
                var ordered = group.OrderBy(b => b.Seconds).ToArray();
                _times[group.Key] = [.. ordered.Select(b => b.Seconds)];
                var prefix = new double[ordered.Length + 1];
                for (var i = 0; i < ordered.Length; i++)
                {
                    prefix[i + 1] = prefix[i] + ordered[i].Damage;
                }

                _prefix[group.Key] = prefix;
            }
        }

        var times = _times[fight];
        if (times is null)
        {
            return 0;
        }

        var index = Array.BinarySearch(times, seconds);
        index = index < 0 ? ~index : index + 1;
        return _prefix![fight][Math.Min(index, times.Length)];
    }
}

/// <summary>A creature's attacks on her, driven as ACE's monster loop drives them (Monster_Tick.cs, Monster_Combat.cs), each roll made by ACE's own functions on the real objects.</summary>
internal static class Blows
{
    private static readonly PropertyInfo CurrentSpellProperty = typeof(Creature).GetProperty("CurrentSpell", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Creature.CurrentSpell is not in this ACE build");

    /// <summary>ACE's default for a creature with no AiUseMagicDelay (Monster_Combat.cs NextMagicAttackTime).</summary>
    private const double DefaultMagicDelay = 3.0;

    /// <summary>ACE's default for a creature with no PowerupTime (Monster_Melee.cs MeleeAttack, Monster_Magic.cs MagicAttack).</summary>
    private const double DefaultPowerupTime = 1.0;

    /// <summary>ACE's fallback strike frame for a swing whose animation holds no attack hook (Monster_Melee.cs defaultAttackFrames).</summary>
    private const float DefaultStrikeFrame = 1.0f / 3.0f;

    /// <summary>ACE's monster tick (Monster_Tick.cs monsterTickInterval): an attack dropped for want of mana is rolled again a tick later.</summary>
    private const double MonsterTick = 0.2;

    /// <summary>
    /// ACE's vital heartbeat (Creature.HeartbeatInterval's default): the creature's mana and stamina regenerate on it. TODO(fight-sim-heartbeat): a creature's own HeartbeatInterval is not read here (139 creature
    /// weenies state another, M6 item 16 E1e; Commands.HeartbeatOf gives it and matchup says so for such a creature).
    /// </summary>
    private const double Heartbeat = 5.0;

    /// <summary>
    /// The creature attacks her in <paramref name="fights"/> separate fights of <paramref name="fightSeconds"/>, with her in reach from the first second. Each fight starts with the creature fresh (full vitals: a caster's
    /// mana is spent on its opening) and with nothing of its laid on her. The schedule is the monster loop's: after each attack the next kind is rolled (GetNextAttackType: each spell of its book by its probability,
    /// else its missile or melee attack); a spell waits for the last attack's start plus AiUseMagicDelay, a physical attack for NextAttackTime, and either for the running animation's end (AttackReady). A melee swing
    /// takes its animation's length and then a pause drawn evenly from 0 to PowerupTime; a spell its pre-cast and post-cast animations and the same pause. Her vitals are restored after every attack.
    /// </summary>
    /// <param name="monster">The creature.</param>
    /// <param name="her">The character.</param>
    /// <param name="fights">How many fights.</param>
    /// <param name="fightSeconds">How long each runs.</param>
    /// <param name="applyDebuffs">Whether a landed enchantment stays on her for the rest of that fight (true: the fight as it goes; false: every attack against her as she was saved).</param>
    public static BlowReport Sample(Creature monster, Player her, int fights, double fightSeconds, bool applyDebuffs)
    {
        var notes = new List<string>();
        var blows = new List<Blow>();
        if (monster.CombatTable == null)
        {
            monster.GetCombatTable();
        }

        monster.AttackTarget = her;
        var ranged = monster.IsRanged;
        monster.SetCombatMode(ranged ? CombatMode.Missile : CombatMode.Melee);
        var stance = monster.CurrentMotionState.Stance;
        var magicDelay = monster.GetProperty(PropertyFloat.AiUseMagicDelay) ?? DefaultMagicDelay;
        var powerup = (float)(monster.PowerupTime ?? DefaultPowerupTime);
        var usesMana = monster.GetProperty(PropertyBool.AiUsesMana) ?? true;
        var projectiles = new Dictionary<uint, SpellProjectile>();
        var (health, stamina, mana) = (her.Health.Current, her.Stamina.Current, her.Mana.Current);
        var enchantmentsBefore = her.Biota.PropertiesEnchantmentRegistry?.ToList() ?? [];
        var dropped = 0;
        var broken = false;

        for (var fight = 0; fight < fights && !broken; fight++)
        {
            monster.SetMaxVitals();
            RemoveLaid(her, enchantmentsBefore);
            double prevAttack = double.NegativeInfinity, nextAttack = 0, animationEnd = 0, now = 0, nextHeartbeat = Heartbeat;
            while (now < fightSeconds)
            {
                while (nextHeartbeat <= now)
                {
                    monster.VitalHeartBeat();
                    nextHeartbeat += Heartbeat;
                }

                var kind = monster.GetNextAttackType();
                if (kind == CombatType.Magic)
                {
                    var spell = (Spell)CurrentSpellProperty.GetValue(monster)!;
                    var ready = Math.Max(Math.Max(now, prevAttack + magicDelay), animationEnd);
                    if (usesMana)
                    {
                        var cost = monster.CalculateManaUsage(monster, spell, her);
                        if (cost > monster.Mana.Current)
                        {
                            // Monster_Magic.cs MagicAttack: UseMana fails, the attack is dropped with no timer moved, and the next tick rolls again.
                            dropped++;
                            now = Math.Max(now, animationEnd) + MonsterTick;
                            continue;
                        }

                        monster.UpdateVitalDelta(monster.Mana, -(int)cost);
                    }

                    var preCast = monster.PreCastMotion(her);
                    var postCast = monster.GetPostCastTime(spell);
                    var at = ready + preCast;
                    if (at <= fightSeconds)
                    {
                        CastOnHer(monster, her, spell, fight, at, projectiles, applyDebuffs, blows, notes);
                    }

                    prevAttack = at;
                    animationEnd = at + postCast;
                    nextAttack = at + postCast + ThreadSafeRandom.Next(0.0f, powerup);
                    now = at;
                }
                else if (kind == CombatType.Missile)
                {
                    var ready = Math.Max(Math.Max(now, nextAttack), animationEnd);
                    var weapon = monster.GetEquippedMissileWeapon();
                    var ammo = weapon is { IsAmmoLauncher: true } ? monster.GetEquippedAmmo() : weapon;
                    if (weapon == null || ammo == null)
                    {
                        notes.Add("TODO(fight-sim-missile-kit): the creature is ranged but holds no usable launcher and ammunition in this build; its missile attacks are not simulated");
                        broken = true;
                        break;
                    }

                    // Monster_Missile.cs LaunchMissile: the aim motion, the reload at the creature's animation speed, the link back to ready, then MissileDelay. The aim is taken level (the aim level follows the geometry).
                    var launch = MotionTable.GetAnimationLength(monster.MotionTableId, stance, MotionCommand.AimLevel);
                    var reload = MotionTable.GetAnimationLength(monster.MotionTableId, stance, MotionCommand.Reload, monster.GetAnimSpeed());
                    var link = MotionTable.GetAnimationLength(monster.MotionTableId, stance, reload > 0 ? MotionCommand.Reload : MotionCommand.AimLevel, MotionCommand.Ready);
                    monster.AttackHeight = monster.ChooseAttackHeight();
                    var missile = WorldObjectFactory.CreateNewWorldObject(ammo.WeenieClassId);
                    missile.ProjectileSource = monster;
                    missile.ProjectileTarget = her;
                    missile.ProjectileLauncher = monster.GetEquippedMissileLauncher();
                    missile.ProjectileAmmo = ammo;
                    monster.CurrentAttack = CombatType.Missile; // GetBaseDamage reads it at the strike; the loop has re-rolled it by then, and a creature that only shoots re-rolls Missile
                    var strike = DamageEvent.CalculateDamage(monster, her, missile);
                    missile.Destroy(false);
                    if (ready + launch <= fightSeconds)
                    {
                        blows.Add(Physical(fight, ready + launch, BlowKind.Missile, strike));
                    }

                    prevAttack = ready;
                    animationEnd = ready + launch + reload + link;
                    nextAttack = animationEnd + Creature.MissileDelay;
                    now = ready;
                }
                else
                {
                    var ready = Math.Max(Math.Max(now, nextAttack), animationEnd);
                    var motion = monster.GetCombatManeuver();
                    if (motion == null)
                    {
                        notes.Add("TODO(fight-sim-no-maneuver): ACE finds no combat maneuver for this creature (its combat table or motion table lacks one); its melee attacks are not simulated");
                        broken = true;
                        break;
                    }

                    var speed = monster.GetAnimSpeed() * (monster.IsDualWieldAttack ? 1.2f : 1.0f);
                    var length = MotionTable.GetAnimationLength(monster.MotionTableId, stance, motion.Value, speed);
                    var frames = MotionTable.GetAttackFrames(monster.MotionTableId, stance, motion.Value);
                    var weapon = monster.GetEquippedWeapon();
                    var hook = frames.Count > 0 ? frames[0].attackHook : null;
                    var strikes = frames.Count > 0 ? frames.Select(f => f.time).ToList() : [DefaultStrikeFrame];
                    foreach (var frame in strikes)
                    {
                        var strike = DamageEvent.CalculateDamage(monster, her, weapon, motion, hook);
                        if (ready + (frame * length) <= fightSeconds)
                        {
                            blows.Add(Physical(fight, ready + (frame * length), BlowKind.Melee, strike));
                        }
                    }

                    prevAttack = ready;
                    animationEnd = ready + length;
                    nextAttack = ready + length + ThreadSafeRandom.Next(0.0f, powerup);
                    now = ready;
                }

                RestoreVitals(her, health, stamina, mana);
            }
        }

        foreach (var projectile in projectiles.Values)
        {
            projectile.Destroy(false);
        }

        // Her enchantments go back to the saved registry: what the creature laid on her is taken off again.
        RemoveLaid(her, enchantmentsBefore);
        RestoreVitals(her, health, stamina, mana);
        monster.SetMaxVitals();
        if (dropped > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{dropped} spell attacks over the {fights} fights were dropped for want of mana (the creature's own pool and regeneration, Monster_Magic.cs UseMana): its spells come mostly at a fight's start"));
        }

        return new BlowReport(fightSeconds, fights, blows, notes);
    }

    private static void RemoveLaid(Player her, List<ACE.Entity.Models.PropertiesEnchantmentRegistry> before)
    {
        foreach (var added in (her.Biota.PropertiesEnchantmentRegistry?.ToList() ?? []).Where(e => !before.Contains(e)))
        {
            her.EnchantmentManager.Remove(added, false);
        }
    }

    /// <summary>One physical attack as DamageEvent resolved it; the health she loses is rounded as Player_Combat.cs TakeDamage rounds it.</summary>
    private static Blow Physical(int fight, double at, BlowKind kind, DamageEvent strike)
    {
        var landed = strike.HasDamage && !strike.GeneralFailure;
        var working = string.Create(
            CultureInfo.InvariantCulture,
            $"attack skill {strike.EffectiveAttackSkill} vs her defence {strike.EffectiveDefenseSkill}: evade chance {strike.EvasionChance:0.000}; base {strike.BaseDamageMod?.MinDamage:0.#}-{strike.BaseDamageMod?.MaxDamage:0.#} (rolled {strike.BaseDamage:0.#}) x attribute {strike.AttributeMod:0.###} x power {strike.PowerMod:0.###} x rating {strike.DamageRatingMod:0.###}{(strike.IsCritical ? string.Create(CultureInfo.InvariantCulture, $" CRITICAL x{strike.CriticalDamageMod:0.###} on the maximum") : string.Empty)} = {strike.DamageBeforeMitigation:0.##}; x armour {strike.ArmorMod:0.####} ({strike.BodyPart}: {string.Join("+", strike.Armor?.Select(a => a.Name) ?? [])}) x shield {strike.ShieldMod:0.###} x resistance {strike.ResistanceMod:0.###} x resist rating {strike.DamageResistanceRatingMod:0.###} = {strike.Damage:0.###}; critical chance {strike.CriticalChance:0.###}");
        return new Blow(fight, at, kind, strike.GeneralFailure ? "a swing ACE finds no body part for (it does nothing)" : strike.DamageType.ToString(), landed, landed ? (int)Math.Round(strike.Damage) : 0)
        {
            Critical = strike.IsCritical,
            Part = landed ? strike.BodyPart.ToString() : string.Empty,
            Working = working,
        };
    }

    private static void RestoreVitals(Player her, uint health, uint stamina, uint mana)
    {
        her.UpdateVital(her.Health, health);
        her.UpdateVital(her.Stamina, stamina);
        her.UpdateVital(her.Mana, mana);
    }

    private static void CastOnHer(Creature monster, Player her, Spell spell, int fight, double at, Dictionary<uint, SpellProjectile> projectiles, bool applyDebuffs, List<Blow> blows, List<string> notes)
    {
        if (spell.NotFound)
        {
            Note(notes, $"TODO(fight-sim-spell-missing): the creature's book names spell {spell.Id}, which the spell table does not hold; not simulated");
            return;
        }

        if (spell.NumProjectiles > 0)
        {
            if (!projectiles.TryGetValue(spell.Id, out var projectile))
            {
                if (WorldObjectFactory.CreateNewWorldObject(spell.Wcid) is not SpellProjectile made)
                {
                    Note(notes, $"TODO(fight-sim-spell-projectile): spell {spell.Id} '{spell.Name}' names weenie {spell.Wcid}, which is not a spell projectile; not simulated");
                    return;
                }

                made.Setup(spell, SpellProjectile.GetProjectileSpellType(spell.Id));
                made.ProjectileSource = monster;
                made.ProjectileTarget = her;
                projectiles[spell.Id] = projectile = made;
            }

            if (spell.NumProjectiles > 1)
            {
                Note(notes, $"'{spell.Name}' throws {spell.NumProjectiles} projectiles: each row is one projectile and every figure counts ONE striking her; how many strike depends on where she stands (TODO(fight-sim-volley-geometry))");
            }

            var (damage, critical) = Strikes.Strike(projectile, monster, her);
            blows.Add(new Blow(fight, at, BlowKind.SpellProjectile, spell.Name, damage > 0, damage) { Critical = critical });
            return;
        }

        if (spell.IsSelfTargeted || !spell.IsHarmful)
        {
            blows.Add(new Blow(fight, at, BlowKind.SpellOther, spell.Name, true, 0));
            return;
        }

        if (spell.MetaSpellType == SpellType.Enchantment && !applyDebuffs)
        {
            blows.Add(new Blow(fight, at, BlowKind.SpellDebuff, spell.Name, !monster.TryResistSpell(her, spell), 0));
            return;
        }

        // A drain, a harm, or (when the debuffs stay) an enchantment: ACE's own cast on the real objects (WorldObject_Magic.cs TryCastSpell), from full health so that the loss is the spell's and never her death.
        her.UpdateVital(her.Health, her.Health.MaxValue);
        var before = her.Health.Current;
        // TryCastSpell's own first step is this resist roll (WorldObject_Magic.cs:69); it is made here so that the outcome is known, and the cast then skips it.
        var landed = !monster.TryResistSpell(her, spell);
        if (landed)
        {
            monster.TryCastSpell(spell, her, tryResist: false);
        }

        var lost = (int)before - (int)her.Health.Current;
        blows.Add(spell.MetaSpellType == SpellType.Enchantment
            ? new Blow(fight, at, BlowKind.SpellDebuff, spell.Name, landed, 0)
            : new Blow(fight, at, BlowKind.SpellDirect, spell.Name, lost > 0, Math.Max(0, lost)));
    }

    private static void Note(List<string> notes, string note)
    {
        if (!notes.Contains(note))
        {
            notes.Add(note);
        }
    }
}

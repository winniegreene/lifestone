using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Animation;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One spell of a creature's attacks as <see cref="AttackTiming"/> prices it.</summary>
/// <param name="Id">The spell.</param>
/// <param name="Name">Its name.</param>
/// <param name="Share">Its share of the creature's attacks (ACE's <c>TryRollSpell</c> in book order, over the attacks that happen).</param>
/// <param name="MeanDamage">Its mean damage on an unresisted target a cast (every projectile counted, the safe side; a magic critical's add at ACE's chance), 0 for a spell that deals no health damage.</param>
/// <param name="LargestDamage">Its largest damage a projectile (the top of its range and a critical's add).</param>
/// <param name="Element">Its DamageType bits (0: none).</param>
/// <param name="Unpriced">Why its health damage is not priced (a named TODO), or null.</param>
public sealed record SpellAttack(uint Id, string Name, double Share, double MeanDamage, double LargestDamage, uint Element, string? Unpriced)
{
    /// <summary>
    /// M6 14 the derived incoming: the creature's skill in the spell's school (<see cref="CreatureSkills.Current"/>), which ACE's resist roll pits against her Magic Defense (<c>TryResistSpell</c>, <c>MagicDefenseCheck</c>,
    /// WorldObject_Magic.cs:104-168 @95ef426d); null when it is not read (no dats, or the spell's school unknown): the spell lands whole, the safe side.
    /// </summary>
    public uint? CasterSkill { get; init; }

    /// <summary>The share of its casts that land on her of Magic Defense <paramref name="magicDefense"/> (ACE's <c>GetSkillChance</c> at 0.03); 1 when the caster's skill or her defence is not read.</summary>
    /// <param name="magicDefense">Her Magic Defense, or null.</param>
    public double LandsOn(double? magicDefense) => CasterSkill is { } skill && magicDefense is { } defence ? Bestiary.SkillChance(skill, defence) : 1.0;
}

/// <summary>
/// M6 14 attack timing (the owner, 2026-11-15, after the second death on the Tusker Holding's approach: "YOU HAVE THE MOBS STATS. YOU HAVE THE MATH. STOP GUESSING."): how often one creature weenie attacks and hits, derived from its world database
/// facts (<see cref="CreatureTimingFacts"/>) and the user's dats by ACE's monster AI (<see cref="AttackTimings"/>), replacing the one swing every 2.75 s the lines assumed (<c>TODO(f11-swing-seconds)</c>, answered).
/// </summary>
/// <param name="Wcid">The weenie class.</param>
/// <param name="Name">Its name.</param>
/// <param name="Seconds">The mean seconds from one attack's start to the next's, every kind of its attacks mixed as ACE rolls them.</param>
/// <param name="PhysicalShare">The share of its attacks that are physical (a melee maneuver or a missile).</param>
/// <param name="StrikesPerPhysical">The mean damage rolls of a physical attack (a melee animation's attack frames, ACE's <c>GetAttackFrames</c>; 1 a missile).</param>
/// <param name="Spells">Its spells' shares and damage.</param>
/// <param name="Why">The derivation in words, every term named.</param>
public sealed record AttackTiming(uint Wcid, string Name, double Seconds, double PhysicalShare, double StrikesPerPhysical, ImmutableArray<SpellAttack> Spells, string Why)
{
    /// <summary>The named TODO when the timing is not derived (the 2.75 s fallback stands) or a part of it is approximate, else null.</summary>
    public string? Todo { get; init; }

    /// <summary>False when the timing is the 2.75 s fallback (<see cref="Underived"/>).</summary>
    public bool Derived { get; init; } = true;

    /// <summary>The mean melee animation at speed 1 (seconds), 0 when it does not melee: Slowness's term (<c>Mage.FightDebuffs</c>).</summary>
    public double MeleeAnimSeconds { get; init; }

    /// <summary>
    /// M6 14 the derived incoming: its physical attack's skill (<see cref="CreatureSkills.AttackSkillId"/> and <see cref="CreatureSkills.Current"/>), which ACE's evade roll pits against her Melee or Missile Defense
    /// (<c>DamageEvent.GetEvadeChance</c>, DamageEvent.cs:386-397 @95ef426d); null when it is not read: every strike lands, the safe side.
    /// </summary>
    public uint? AttackSkill { get; init; }

    /// <summary>M6 14 the derived incoming: its physical attack is a missile's (a launcher or a thrown weapon): her Missile Defense evades it, not her Melee Defense.</summary>
    public bool Ranged { get; init; }

    /// <summary>The share of its strikes that land on her of <paramref name="defence"/> (Melee Defense, or Missile Defense for a <see cref="Ranged"/> attack; ACE's <c>GetSkillChance</c> at 0.03); 1 when either is not read.</summary>
    /// <param name="defence">Her defence against it, or null.</param>
    public double StrikesLand(double? defence) => AttackSkill is { } skill && defence is { } d ? Bestiary.SkillChance(skill, d) : 1.0;

    /// <summary>
    /// M6 14 the derived incoming (live ladder57 2026-11-17 10:42:05 UTC: a Nubilous Golem read 4.3 a second, fight-sim's ACE 0.8 to 1.5): the damage a second it deals her of the defences <paramref name="her"/>: its strikes that land
    /// (<see cref="StrikesLand"/>) at <paramref name="meanHit"/> times <paramref name="factor"/>, and its priced spells that land (<see cref="SpellAttack.LandsOn"/>) through <paramref name="resistance"/>. With her defences unread, every
    /// strike and spell lands (<see cref="PerSecond(double, double, Func{uint, double}?)"/>).
    /// </summary>
    /// <param name="meanHit">Its mean unarmoured physical hit.</param>
    /// <param name="factor">What reaches her of it.</param>
    /// <param name="resistance">Her multiplier against a spell's element, or null.</param>
    /// <param name="her">Her defences, or null.</param>
    public double PerSecondOn(double meanHit, double factor, Func<uint, double>? resistance, DefenceRead? her) =>
        (Math.Max(0, meanHit) * Math.Max(0, factor) * HitsPerSecond * StrikesLand(her is { } h ? Ranged ? h.Missile : h.Melee : null)) + SpellPerSecond(resistance, her?.Magic);

    /// <summary>The damage a second its priced spells deal her, each landing at its share on her Magic Defense <paramref name="magicDefense"/> (<see cref="SpellAttack.LandsOn"/>); null: every spell lands.</summary>
    /// <param name="resistance">Her multiplier against the DamageType bits given, or null.</param>
    /// <param name="magicDefense">Her Magic Defense, or null.</param>
    public double SpellPerSecond(Func<uint, double>? resistance, double? magicDefense)
    {
        if (Seconds <= 0 || Spells.IsDefaultOrEmpty)
        {
            return 0;
        }

        var sum = 0.0;
        foreach (var s in Spells)
        {
            if (s.MeanDamage > 0)
            {
                sum += s.Share * s.MeanDamage * (resistance?.Invoke(s.Element) ?? 1.0) * s.LandsOn(magicDefense);
            }
        }

        return sum / Seconds;
    }

    /// <summary>Its PowerupTime (ACE's 1 s when the weenie records none): the most of the random delay after a physical attack.</summary>
    public double PowerupSeconds { get; init; } = AttackTimings.DefaultPowerupSeconds;

    /// <summary>Its physical hits a second (each strike counted a hit: her evades are not read, the safe side).</summary>
    public double HitsPerSecond => Seconds > 0 ? PhysicalShare * StrikesPerPhysical / Seconds : 0;

    /// <summary>The mean seconds between two of its physical hits (infinity when it lands none).</summary>
    public double SecondsPerHit => HitsPerSecond > 0 ? 1.0 / HitsPerSecond : double.PositiveInfinity;

    /// <summary>The damage a second its priced spells deal her: each spell's share of the attacks over <see cref="Seconds"/>, its mean damage, times what her resistance leaves of its element (<paramref name="resistance"/>; null: unresisted, the safe side).</summary>
    /// <param name="resistance">Her multiplier against the DamageType bits given, or null.</param>
    public double SpellPerSecond(Func<uint, double>? resistance) => SpellPerSecond(resistance, null);

    /// <summary>The largest single spell hit on her (a projectile's top and a critical's add, times her resistance), 0 with none priced.</summary>
    /// <param name="resistance">Her multiplier against the DamageType bits given, or null.</param>
    public double LargestSpellHit(Func<uint, double>? resistance)
    {
        var most = 0.0;
        foreach (var s in Spells.IsDefault ? [] : Spells)
        {
            if (s.LargestDamage > 0)
            {
                most = Math.Max(most, s.LargestDamage * (resistance?.Invoke(s.Element) ?? 1.0));
            }
        }

        return most;
    }

    /// <summary>The damage a second it deals her: its physical hits at <paramref name="meanHit"/> times <paramref name="factor"/>, and its spells through <paramref name="resistance"/>.</summary>
    /// <param name="meanHit">Its mean unarmoured physical hit.</param>
    /// <param name="factor">What reaches her of it (her armour times her resistance against its type).</param>
    /// <param name="resistance">Her multiplier against a spell's element, or null (unresisted).</param>
    public double PerSecond(double meanHit, double factor, Func<uint, double>? resistance) => (Math.Max(0, meanHit) * Math.Max(0, factor) * HitsPerSecond) + SpellPerSecond(resistance);

    /// <summary>The words a line prints for it: its attack interval and hits a second, and the TODO when there is one.</summary>
    public string Short() => string.Create(
        CultureInfo.InvariantCulture,
        $"an attack every {Seconds:0.##} s, {HitsPerSecond:0.##} hits/s{(Spells.IsDefaultOrEmpty || !Spells.Any(s => s.MeanDamage > 0) ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" and {Spells.Where(s => s.MeanDamage > 0).Sum(s => s.Share) / Seconds:0.##} damaging casts/s"))}{(Todo is null ? string.Empty : " (" + Todo + ")")}");

    /// <summary>
    /// The timing of a creature whose attacks cannot be derived (no dats at hand, no motion or combat table): one hit every <see cref="AttackTimings.UnderivedSwingSeconds"/>, the Lodge's measured gaps (M6 7 AY), named loud
    /// (<c>TODO(f11-swing-seconds)</c> with the creature and the reason).
    /// </summary>
    /// <param name="wcid">The weenie class.</param>
    /// <param name="name">Its name.</param>
    /// <param name="why">Why it is not derived.</param>
    public static AttackTiming Underived(uint wcid, string name, string why)
    {
        var todo = string.Create(CultureInfo.InvariantCulture, $"{AttackTimings.UnderivedTodo}: '{name}' (weenie {wcid}) is not derived ({why}): one hit every {AttackTimings.UnderivedSwingSeconds:0.##} s, the Lodge's measured gaps");
        return new AttackTiming(wcid, name, AttackTimings.UnderivedSwingSeconds, 1, 1, [], todo) { Todo = todo, Derived = false };
    }
}

/// <summary>
/// M6 14 attack timing: ACE's monster attack loop re-expressed (@95ef426d; the owner's rule that every fight number is computed, never guessed), per creature weenie, from its world database facts (<see cref="CreatureTimingFacts"/>) and the user's dats (its MotionTable, its
/// CombatTable and their Animations, read at run time and never recorded). Pure given the dat reads.
/// <list type="bullet">
/// <item><b>The decision</b> (<c>Monster_Combat.GetNextAttackType</c>, <c>Monster_Magic.TryRollSpell</c>): a caster rolls its book in order, each spell at its chance (a probability over 2 is <c>p - 2</c>, else <c>p / 100</c>); the first hit
/// is cast; else it attacks physically: a missile when it wields a launcher or a thrown weapon (<c>IsRanged</c>), else melee. The roll is made again after every attack, and on the monster tick after a melee pick that found no maneuver.</item>
/// <item><b>Melee</b> (<c>Monster_Melee.GetCombatManeuver</c>): an attack height at random, any of the three when the stance's maneuvers name three, else medium or low; a creature with no weapon punches high and medium and kicks low (a
/// missing punch or kick swaps for the other; a weapon's AttackType at ACE's power 0.5, <c>WorldObject_Weapon.GetAttackType</c>); a maneuver at random among the table's for it, reduced to its single strike or first attack when its motion
/// table lacks it. Its length (<c>DoSwingMotion</c>, <c>MotionTable.GetAnimationLength</c>: the link from the stance's default to the motion, frames over the framerate) is played at <c>GetAnimSpeed</c>
/// (<c>1 / (1 - Quickness / 300 + 40 / 150)</c> within 0.5 to 2: a monster's weapon speed is always ACE's default 40, <c>GetWeaponSpeed(wielder as Player)</c>); each of its attack hooks is a strike (<c>GetAttackFrames</c>; one when it has
/// none). The next attack is ready after the animation and a delay uniform in 0 to PowerupTime (1 s when none: <c>NextAttackTime = PrevAttackTime + animLength + meleeDelay</c>).</item>
/// <item><b>Missile</b> (<c>Monster_Missile.LaunchMissile</c>): the aim's link (AimLevel: she stands level with it), the reload at its animation speed and the link back to Ready, then <c>MissileDelay</c>'s 1 s; one projectile a launch.</item>
/// <item><b>Magic</b> (<c>Monster_Magic.MagicAttack</c>): the CastSpell motion at <c>PreCastSpeed</c> 2, the spell, the link back to Ready at 1 (1.66 for a creature with a missile weapon); the next physical attack after both and a delay
/// in 0 to PowerupTime; a cast is ready no sooner than <c>AiUseMagicDelay</c> (3 s when none) after the previous attack's <c>PrevAttackTime</c> and never while it animates (<c>AttackReady</c>, <c>NextMagicAttackTime</c>).</item>
/// <item><b>The tick</b> (<c>Monster_Tick.monsterTickInterval</c>): the AI looks every 0.2 s, so an attack starts at the first tick past its ready time (the mean of the rounding is taken exactly).</item>
/// </list>
/// The mean seconds between attacks mixes every pair of consecutive kinds at their shares (the roll is independent each time). A spell's damage is a war projectile's (<c>SpellProjectile.CalculateDamage</c>: uniform over its range,
/// a critical at ACE's 5% adding half its top); a life spell that harms or drains health is named unpriced (<see cref="LifeSpellTodo"/>).
/// </summary>
public static class AttackTimings
{
    /// <summary>ACE's monster AI tick (<c>Monster_Tick.monsterTickInterval</c>).</summary>
    public const double MonsterTickSeconds = 0.2;

    /// <summary>ACE's PowerupTime when the weenie records none (<c>Monster_Melee.MeleeAttack</c>: <c>PowerupTime ?? 1.0f</c>).</summary>
    public const double DefaultPowerupSeconds = 1.0;

    /// <summary>ACE's AiUseMagicDelay when the weenie records none (<c>Monster_Combat.NextMagicAttackTime</c>: <c>AiUseMagicDelay ?? 3.0f</c>).</summary>
    public const double DefaultMagicDelaySeconds = 3.0;

    /// <summary>ACE's delay after a missile (<c>Monster_Missile.MissileDelay</c>).</summary>
    public const double MissileDelaySeconds = 1.0;

    /// <summary>ACE's CastSpell speed (<c>Monster_Magic.PreCastSpeed</c>).</summary>
    public const double PreCastSpeed = 2.0;

    /// <summary>ACE's speed back to Ready after a cast (<c>Monster_Magic.PostCastSpeed</c>; <see cref="PostCastSpeedRanged"/> with a missile weapon).</summary>
    public const double PostCastSpeed = 1.0;

    /// <summary>ACE's <c>PostCastSpeed_Ranged</c>.</summary>
    public const double PostCastSpeedRanged = 1.66;

    /// <summary>ACE's weapon speed for every monster (<c>WorldObject_Weapon.defaultSpeed</c>: <c>GetWeaponSpeed</c> reads the weapon of <c>wielder as Player</c>, none for a monster).</summary>
    public const double MonsterWeaponSpeed = 40;

    /// <summary>ACE's <c>Creature_Combat.MinAttackSpeed</c> and <c>MaxAttackSpeed</c>.</summary>
    public const double MinAnimSpeed = 0.5, MaxAnimSpeed = 2.0;

    /// <summary>ACE's magic critical chance with no caster (<c>WorldObject_Weapon.defaultMagicCritFrequency</c>) and its add, half the spell's top (<c>SpellProjectile.CalculateDamage</c>, PvE).</summary>
    public const double MagicCriticalChance = 0.05, MagicCriticalAdd = 0.5;

    /// <summary>The fallback for a creature whose timing cannot be derived: the Lodge's measured gaps of 2.1 to 3.4 s (M6 7 AY), one hit every this many seconds, named (<see cref="UnderivedTodo"/>).</summary>
    public const double UnderivedSwingSeconds = 2.75;

    /// <summary>The named TODO of an underived timing.</summary>
    public const string UnderivedTodo = "TODO(f11-swing-seconds)";

    /// <summary>The named TODO of a creature's life spell that harms or drains her health, not priced here (the guard measures it).</summary>
    public const string LifeSpellTodo = "TODO(m6-creature-life-spells)";

    /// <summary>The named TODO of a part of the timing that is approximate (a dual wield, a rolled treasure weapon, the human casting gestures).</summary>
    public const string ApproximateTodo = "TODO(m6-attack-timing-approximate)";

    /// <summary>
    /// M6 14 the derived incoming: the creature's own mana is not counted. ACE drops a monster's cast it cannot pay (<c>Monster_Magic.UseMana</c>), so over a long fight its spells thin out (fight-sim, 2026-11-17: a Cursed Wisp 3.4 a second in a
    /// fight's opening, 1.43 over a minute); the rate here is the opening's, the loop with its mana never running out: the safe side for the race's seconds.
    /// </summary>
    public const string ManaTodo = "TODO(m6-creature-mana): its spells are timed as if its mana never ran out (a fight's opening rate)";

    // Motion ids, end-of-retail numbering (ACE.Entity/Enum/MotionCommand.cs and MotionStance.cs @95ef426d; the dats use the same).
    internal const uint Ready = 0x41000003, CastSpell = 0x400000D3, Reload = 0x40000016, AimLevel = 0x4000001E;
    internal const uint HandCombat = 0x8000003C, SwordCombat = 0x8000003E, BowCombat = 0x8000003F, SwordShieldCombat = 0x80000040, CrossbowCombat = 0x80000041, SlingCombat = 0x80000043, TwoHandedSwordCombat = 0x80000044;
    internal const uint DualWieldCombat = 0x80000046, ThrownWeaponCombat = 0x80000047, MagicStance = 0x80000049, AtlatlCombat = 0x8000013B, ThrownShieldCombat = 0x8000013C;

    // ACE AttackType bits and AttackHeight.
    private const uint Punch = 0x1, Thrust = 0x2, Slash = 0x4, Kick = 0x8, DoubleSlash = 0x20, TripleSlash = 0x40, DoubleThrust = 0x80, TripleThrust = 0x100, OffhandBits = 0x7E00, MultiStrikeBits = 0x79E0;
    private const uint Low = 3;

    /// <summary>ACE's <c>GetAnimSpeed</c> for a monster of <paramref name="quickness"/>: <c>1 / (1 - Q / 300 + 40 / 150)</c>, within 0.5 to 2.</summary>
    /// <param name="quickness">Its current Quickness.</param>
    public static double AnimSpeed(double quickness)
    {
        var divisor = 1.0 - (quickness / 300.0) + (MonsterWeaponSpeed / 150.0);
        return divisor <= 0 ? MaxAnimSpeed : Math.Clamp(1.0 / divisor, MinAnimSpeed, MaxAnimSpeed);
    }

    /// <summary>
    /// The mean of a ready time <c>a + U(0, p)</c> rounded up to the monster tick <paramref name="tick"/> (the attack starts at the first tick past it), exactly: <c>h x (F(a + p) - F(a)) / p</c> with <c>F</c> the integral of
    /// <c>ceil(x / h)</c>; <c>p = 0</c> is the rounding of <c>a</c> alone.
    /// </summary>
    /// <param name="a">The fixed part.</param>
    /// <param name="p">The width of the uniform delay.</param>
    /// <param name="tick">The tick.</param>
    public static double TickMean(double a, double p, double tick = MonsterTickSeconds)
    {
        a = Math.Max(0, a);
        if (tick <= 0)
        {
            return a + (Math.Max(0, p) / 2);
        }

        if (p <= 0)
        {
            return tick * Math.Ceiling((a / tick) - 1e-9);
        }

        return tick * (CeilIntegral(a + p, tick) - CeilIntegral(a, tick)) / p;
    }

    /// <summary>The integral of <c>ceil(t / h)</c> from 0 to <paramref name="x"/>.</summary>
    private static double CeilIntegral(double x, double h)
    {
        var n = Math.Floor(x / h);
        var r = x - (n * h);
        return (h * n * (n + 1) / 2) + ((n + 1) * r);
    }

    /// <summary>The DatAnimationSource reads <see cref="Derive"/> needs.</summary>
    /// <param name="Motion">A MotionTable by id, or null.</param>
    /// <param name="Animation">An Animation by id, or null.</param>
    /// <param name="Combat">A CombatTable by id, or null.</param>
    public sealed record Dats(Func<uint, MotionTableData?> Motion, Func<uint, AnimationData?> Animation, Func<uint, CombatTableData?> Combat)
    {
        /// <summary>M6 14 the derived incoming: the dats' SkillTable entry of a skill id (its formula and min level), or null: the creatures' skills are not read.</summary>
        public Func<uint, Lifestone.Data.CharacterCreation.SkillTableEntry?>? Skill { get; init; }

        /// <summary>M6 14 the derived incoming: the dats' SpellTable school of a spell id, or null: the spells' casters' skills are not read.</summary>
        public Func<uint, DatReaderWriter.Enums.MagicSchool?>? School { get; init; }
    }

    private readonly record struct Option(double P, uint Motion, double Seconds, double RawSeconds, int Strikes);

    /// <summary>The timing of weenie <paramref name="wcid"/> from its facts and the dats (see the class); <see cref="AttackTiming.Underived"/> when its tables are not at hand.</summary>
    /// <param name="wcid">The weenie class.</param>
    /// <param name="name">Its name.</param>
    /// <param name="f">Its facts.</param>
    /// <param name="dats">The dats' reads.</param>
    public static AttackTiming Derive(uint wcid, string name, CreatureTimingFacts f, Dats dats)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(dats);
        var c = CultureInfo.InvariantCulture;
        if (f.MotionTable == 0)
        {
            return AttackTiming.Underived(wcid, name, "the world database records no motion table for it");
        }

        if (dats.Motion(f.MotionTable) is not { } mt)
        {
            return AttackTiming.Underived(wcid, name, string.Create(c, $"its motion table 0x{f.MotionTable:X8} is not in the dats at hand"));
        }

        var todos = new List<string>();
        var speed = AnimSpeed(f.Quickness);
        var powerup = f.PowerupTime ?? DefaultPowerupSeconds;
        var magicDelay = f.MagicDelay ?? DefaultMagicDelaySeconds;
        var ranged = f.Launcher || f.Thrown;
        var stance = StanceOf(f, ranged, todos);
        if (f.WieldedTreasure != 0)
        {
            todos.Add(string.Create(c, $"its weapon is rolled from wielded treasure {f.WieldedTreasure} at spawn: timed with the weapon its create list holds, else bare-handed"));
        }

        // the physical attack's options, each with its chance a decision, its length at its speed and its strikes; fail: a melee pick that finds no maneuver (tried again on the next tick)
        var options = new List<Option>();
        var fail = 0.0;
        string physicalWhy;
        var physicalEnd = 0.0; // a missile's animations before MissileDelay
        if (ranged)
        {
            var launch = Length(mt, dats, stance, DefaultOf(mt, stance), AimLevel);
            var reload = Length(mt, dats, stance, DefaultOf(mt, stance), Reload) / speed;
            var link = Length(mt, dats, stance, reload > 0 ? Reload : AimLevel, Ready);
            physicalEnd = launch + reload + link;
            options.Add(new Option(1, AimLevel, physicalEnd + MissileDelaySeconds, physicalEnd, 1));
            physicalWhy = string.Create(c, $"a missile in stance 0x{stance:X8}: the aim {launch:0.###} s, the reload {reload:0.###} s at speed {speed:0.###} (Quickness {f.Quickness}), the link to Ready {link:0.###} s and MissileDelay {MissileDelaySeconds:0.#} s, one projectile");
        }
        else
        {
            physicalWhy = Melee(f, mt, dats, stance, speed, options, out fail, todos);
        }

        // the spells, in book order: each its chance, the first hit cast (ACE's TryRollSpell)
        var none = 1.0;
        var spellChances = new List<(CreatureSpell Spell, double P)>();
        foreach (var s in f.Spells)
        {
            var p = Math.Clamp(s.Chance, 0, 1);
            spellChances.Add((s, none * p));
            none *= 1 - p;
        }

        fail = fail > 1 - 1e-9 ? 1 : fail; // every pick fails (the shares' sum is 1 to the rounding)
        var magic = 1 - none; // a decision casts
        var physical = none * (1 - fail); // a decision attacks physically
        var failed = none * fail; // a decision finds no maneuver: tried again on the next tick
        if (magic + physical <= 1e-9)
        {
            return AttackTiming.Underived(wcid, name, string.Create(c, $"ACE's attack loop finds no attack it can make: {physicalWhy}"));
        }

        var pre = Length(mt, dats, stance, DefaultOf(mt, stance), CastSpell) / PreCastSpeed;
        var post = Length(mt, dats, stance, CastSpell, Ready) / (ranged ? PostCastSpeedRanged : PostCastSpeed);
        if (f.HumanMagic && magic > 0)
        {
            todos.Add("it casts with the spells' own gestures (AiUseHumanMagicAnimations): timed with the CastSpell motion");
        }

        // the mean seconds between consecutive attacks: every pair of kinds at their shares a decision that attacks
        var attack = magic + physical;
        var pm = magic / attack;
        var physicalOptions = options.Select(o => o with { P = o.P / Math.Max(1e-12, 1 - fail) }).ToList(); // the options' shares among the physical attacks that happen
        var seconds = 0.0;
        foreach (var a in physicalOptions)
        {
            seconds += (1 - pm) * a.P * (((1 - pm) * TickMean(a.Seconds, ranged ? 0 : powerup)) + (pm * TickMean(Math.Max(magicDelay, ranged ? physicalEnd : a.Seconds), 0)));
        }

        if (pm > 0)
        {
            seconds += pm * (((1 - pm) * TickMean(pre + post, powerup)) + (pm * TickMean(pre + Math.Max(magicDelay, post), 0)));
        }

        seconds += MonsterTickSeconds * failed / attack; // each pick that found no maneuver costs a tick

        var strikes = physicalOptions.Sum(o => o.P * o.Strikes);
        var meleeRaw = ranged ? 0 : physicalOptions.Sum(o => o.P * o.RawSeconds);
        var spells = spellChances.Select(x => Priced(x.Spell, x.P / attack, todos)).Select(sp => sp with { CasterSkill = CasterSkill(wcid, f, sp.Id, dats) }).ToImmutableArray();
        if (spells.Any(sp => sp.MeanDamage > 0))
        {
            todos.Add(ManaTodo); // M6 14 the derived incoming: fight-sim's wisps cast mostly in a fight's first seconds, then for want of mana; the rate here is that opening's, the safe side
        }

        var attackSkillId = Bestiary.AttackSkillId(wcid, f.AttackWeapon, ranged);
        var attackSkill = dats.Skill is { } skillOf ? Bestiary.SkillOf(wcid, attackSkillId, f.Attributes, skillOf(attackSkillId)) : null;
        var spellWhy = f.Spells.Count == 0
            ? "no spells"
            : string.Create(c, $"casts {pm:0.###} of its attacks ({string.Join(", ", spells.Select(s => string.Create(c, $"{s.Name} {s.Share:0.###}{(s.MeanDamage > 0 ? string.Create(c, $" at {s.MeanDamage:0.#}") : string.Empty)}")))}): CastSpell {pre:0.###} s at speed {PreCastSpeed:0}, back to Ready {post:0.###} s, AiUseMagicDelay {magicDelay:0.##} s{(f.MagicDelay is null ? " (ACE's default)" : string.Empty)}");
        var todo = todos.Count == 0 ? null : string.Create(c, $"{ApproximateTodo}: '{name}' (weenie {wcid}): {string.Join("; ", todos)}");
        var timing = new AttackTiming(wcid, name, seconds, 1 - pm, strikes, spells, string.Empty)
        {
            Todo = todo,
            MeleeAnimSeconds = meleeRaw,
            PowerupSeconds = powerup,
            AttackSkill = attackSkill,
            Ranged = ranged,
        };
        var why = string.Create(
            c,
            $"'{name}' (weenie {wcid}, motion 0x{f.MotionTable:X8}, combat 0x{f.CombatTable:X8}): {physicalWhy}; PowerupTime {powerup:0.##} s{(f.PowerupTime is null ? " (ACE's default)" : string.Empty)}; {spellWhy}; the {MonsterTickSeconds:0.#} s monster tick: an attack every {seconds:0.###} s, {timing.HitsPerSecond:0.###} hits a second{(todo is null ? string.Empty : "; " + todo)}");
        return timing with { Why = why };
    }

    /// <summary>The melee options (see the class), and their words.</summary>
    private static string Melee(CreatureTimingFacts f, MotionTableData mt, Dats dats, uint stance, double speed, List<Option> options, out double fail, List<string> todos)
    {
        var c = CultureInfo.InvariantCulture;
        fail = 1;
        if (f.CombatTable == 0 || dats.Combat(f.CombatTable) is not { } ct)
        {
            return f.CombatTable == 0 ? "no combat table: ACE picks no maneuver" : string.Create(c, $"its combat table 0x{f.CombatTable:X8} is not in the dats at hand");
        }

        if (!mt.Links.TryGetValue((stance << 16) | (Ready & 0xFFFFFF), out var ready))
        {
            return string.Create(c, $"its motion table has no links from Ready in stance 0x{stance:X8}: ACE picks no maneuver");
        }

        fail = 0;
        var heights = ct.Heights(stance);
        var first = heights.Count == 3 ? 1u : 2u;
        var share = 1.0 / (3 - first + 1);
        var weaponType = f.MeleeWeapon ? WeaponAttackType((uint)f.WeaponAttackType, stance) : (uint?)null;
        for (var h = first; h <= 3; h++)
        {
            var type = weaponType ?? (h != Low ? Punch : Kick);
            var list = ct.Maneuvers(stance, h, type);
            if (list.Count == 0 && ((type == Punch && h == Low) || type == Kick))
            {
                list = ct.Maneuvers(stance, h, type == Punch ? Kick : Punch);
            }
            else if (list.Count == 0 && (type & MultiStrikeBits) != 0)
            {
                list = ReduceType(type) is var reduced and > 0 ? ct.Maneuvers(stance, h, reduced) : [];
            }

            if (list.Count == 0)
            {
                fail += share;
                continue;
            }

            foreach (var m in list)
            {
                var p = share / list.Count;
                var motion = m;
                if (!ready.ContainsKey(m))
                {
                    var single = IsMultiStrikeMotion(m) ? ReduceMultiStrikeMotion(m) : IsSubsequentMotion(m) ? ReduceSubsequentMotion(m) : 0;
                    if (single == 0 || !ready.ContainsKey(single))
                    {
                        fail += p;
                        continue;
                    }

                    motion = single;
                }

                var raw = Length(mt, dats, stance, DefaultOf(mt, stance), motion);
                var strikes = AttackFrames(mt, dats, stance, motion);
                options.Add(new Option(p, motion, raw / speed, raw, strikes));
            }
        }

        if (f.MeleeWeapons >= 2)
        {
            todos.Add(string.Create(c, $"its create list holds {f.MeleeWeapons} melee weapons to wield (alternatives at spawn, or a dual wield): timed with the hardest one's stance and attack type"));
        }

        if (stance == DualWieldCombat)
        {
            todos.Add("it dual wields: the off hand's alternation and ACE's 1.2 dual-wield speed are not modelled");
        }

        var success = options.Sum(o => o.P);
        var mean = success > 0 ? options.Sum(o => o.P * o.Seconds) / success : 0;
        var strikesMean = success > 0 ? options.Sum(o => o.P * o.Strikes) / success : 0;
        var who = weaponType is { } w ? string.Create(c, $"its weapon's attack type 0x{w:X}") : "bare-handed (punches high and medium, kicks low)";
        return string.Create(
            c,
            $"melee in stance 0x{stance:X8}, {who}: {options.Count} maneuver(s) over {3 - first + 1} height(s) ({string.Join(", ", options.Select(o => string.Create(c, $"0x{o.Motion:X8} {o.Seconds:0.###} s x{o.Strikes}")))}){(fail > 0 ? string.Create(c, $", {fail:0.###} of the picks find none") : string.Empty)}: an animation of {mean:0.###} s on the mean at speed {speed:0.###} (Quickness {f.Quickness}), {strikesMean:0.##} strikes");
    }

    /// <summary>The stance ACE fights it in (<c>Creature_Combat.GetCombatStance</c>, <c>GetWeaponStance</c>, <c>AddShieldStance</c>).</summary>
    private static uint StanceOf(CreatureTimingFacts f, bool ranged, List<string> todos)
    {
        var style = ranged ? (f.Launcher ? f.LauncherCombatStyle : f.WeaponCombatStyle) : f.MeleeWeapon ? f.WeaponCombatStyle : 0;
        var stance = style switch
        {
            0x400 => AtlatlCombat,
            0x10 => BowCombat,
            0x20 => CrossbowCombat,
            0x100 => DualWieldCombat,
            0x200 => MagicStance,
            0x2 => SwordCombat,
            0x4 => SwordShieldCombat,
            0x40 => SlingCombat,
            0x800 => ThrownShieldCombat,
            0x80 => ThrownWeaponCombat,
            0x8 => TwoHandedSwordCombat,
            _ => HandCombat,
        };
        if (style != 0 && stance == HandCombat && style != 0x1)
        {
            todos.Add(string.Create(CultureInfo.InvariantCulture, $"its weapon's combat style 0x{style:X} is none ACE maps (HandCombat, as ACE does)"));
        }

        if (f.Shields > 0)
        {
            stance = stance == SwordCombat ? SwordShieldCombat : stance == ThrownWeaponCombat ? ThrownShieldCombat : stance;
        }

        return stance;
    }

    /// <summary>ACE's <c>WorldObject_Weapon.GetAttackType</c> for a monster's main hand at power 0.5 (over the thrust threshold 0.33).</summary>
    private static uint WeaponAttackType(uint attackType, uint stance)
    {
        attackType &= ~OffhandBits;
        if (stance == DualWieldCombat)
        {
            if ((attackType & (TripleThrust | TripleSlash)) == (TripleThrust | TripleSlash))
            {
                attackType = TripleSlash;
            }
            else if ((attackType & (DoubleThrust | DoubleSlash)) == (DoubleThrust | DoubleSlash))
            {
                attackType = DoubleSlash;
            }
            else if ((attackType & DoubleThrust) != 0)
            {
                attackType = DoubleThrust;
            }
            else if ((attackType & (Thrust | DoubleSlash)) == (Thrust | DoubleSlash))
            {
                attackType = DoubleSlash;
            }
            else if ((attackType & (Thrust | TripleSlash)) == (Thrust | TripleSlash))
            {
                attackType = TripleSlash;
            }
        }
        else if (stance == SwordShieldCombat)
        {
            if ((attackType & TripleThrust) != 0)
            {
                attackType = TripleThrust;
            }
            else if ((attackType & DoubleThrust) != 0)
            {
                attackType = DoubleThrust;
            }
            else if ((attackType & Thrust) != 0 && (attackType & (DoubleSlash | TripleSlash)) != 0)
            {
                attackType = Thrust;
            }
        }
        else if (stance == SwordCombat)
        {
            if ((attackType & TripleSlash) != 0)
            {
                attackType = TripleSlash;
            }
            else if ((attackType & DoubleSlash) != 0)
            {
                attackType = DoubleSlash;
            }
            else if ((attackType & DoubleThrust) != 0)
            {
                attackType = Thrust;
            }
        }

        if ((attackType & (Thrust | Slash)) == (Thrust | Slash))
        {
            attackType = Slash;
        }

        return attackType;
    }

    /// <summary>ACE's <c>AttackType.ReduceMultiStrike</c> (double and triple to single), 0 for another value.</summary>
    private static uint ReduceType(uint type) => type switch
    {
        DoubleThrust or TripleThrust => Thrust,
        DoubleSlash or TripleSlash => Slash,
        0x2000 or 0x4000 => 0x200,
        0x800 or 0x1000 => 0x400,
        _ => 0,
    };

    private static bool IsMultiStrikeMotion(uint m) => m is (>= 0x1000011F and <= 0x1000012A) or (>= 0x10000179 and <= 0x10000184);

    /// <summary>ACE's <c>MotionCommand.ReduceMultiStrike</c>: DoubleSlashLow and TripleSlashLow to SlashLow, and so on, the off hand's to the main hand's.</summary>
    private static uint ReduceMultiStrikeMotion(uint m)
    {
        // SlashLow 0x1000005D, SlashMed 0x1000005C, SlashHigh 0x1000005B, ThrustLow 0x10000059, ThrustMed 0x10000058, ThrustHigh 0x1000005A; each group of the multi-strikes runs Low, Med, High
        var i = m <= 0x1000012A ? m - 0x1000011F : m - 0x10000179; // 0..11: double slash, triple slash, double thrust, triple thrust, three heights each
        var slash = i < 6;
        return (i % 3) switch
        {
            0 => slash ? 0x1000005Du : 0x10000059u,
            1 => slash ? 0x1000005Cu : 0x10000058u,
            _ => slash ? 0x1000005Bu : 0x1000005Au,
        };
    }

    private static bool IsSubsequentMotion(uint m) => m is (>= 0x10000065 and <= 0x1000006A) or (>= 0x10000186 and <= 0x1000018E);

    /// <summary>ACE's <c>MotionCommand.ReduceSubsequent</c>: AttackHigh2 to 6 to AttackHigh1, and the medium and low the same.</summary>
    private static uint ReduceSubsequentMotion(uint m)
    {
        var i = m <= 0x1000006A ? m - 0x10000065 : m - 0x10000186; // each triple runs High, Med, Low
        return 0x10000062u + (i % 3);
    }

    /// <summary>The stance's default motion (<c>MotionTable.GetDefaultMotion</c>: its StyleDefaults, else 0).</summary>
    private static uint DefaultOf(MotionTableData mt, uint stance) => mt.StyleDefaults.TryGetValue(stance, out var d) ? d : 0;

    /// <summary>ACE's <c>MotionTable.GetAnimData</c>: the link from <paramref name="current"/> to <paramref name="motion"/> in <paramref name="stance"/>, else from the stance's empty key; none when neither holds it.</summary>
    private static IReadOnlyList<AnimDataRef> AnimData(MotionTableData mt, uint stance, uint current, uint motion)
    {
        if (!mt.Links.TryGetValue((stance << 16) | (current & 0xFFFFF), out var link))
        {
            return [];
        }

        if (link.TryGetValue(motion, out var data))
        {
            return data.Anims;
        }

        return mt.Links.TryGetValue(stance << 16, out var empty) && empty.TryGetValue(motion, out data) ? data.Anims : [];
    }

    /// <summary>ACE's <c>MotionTable.GetAnimationLength</c> at speed 1: each animation's frames (to its last when the high frame is -1 or past it) over its framerate, summed.</summary>
    private static double Length(MotionTableData mt, Dats dats, uint stance, uint current, uint motion)
    {
        var length = 0.0;
        foreach (var a in AnimData(mt, stance, current, motion))
        {
            var frames = dats.Animation(a.AnimId)?.NumFrames ?? 0;
            var high = a.HighFrame == -1 || a.HighFrame > frames ? frames : a.HighFrame;
            if (a.Framerate != 0)
            {
                length += (high - a.LowFrame) / Math.Abs((double)a.Framerate);
            }
        }

        return length;
    }

    /// <summary>ACE's <c>MotionTable.GetAttackFrames</c>: the attack hooks over every frame of the motion's animations; ACE's one default strike when there are none.</summary>
    private static int AttackFrames(MotionTableData mt, Dats dats, uint stance, uint motion)
    {
        var count = 0;
        foreach (var a in AnimData(mt, stance, DefaultOf(mt, stance), motion))
        {
            foreach (var frame in dats.Animation(a.AnimId)?.PartFrames ?? [])
            {
                foreach (var hook in frame.Hooks)
                {
                    if (hook is AttackHook)
                    {
                        count++;
                    }
                }
            }
        }

        return count == 0 ? 1 : count;
    }

    /// <summary>
    /// M6 14 the derived incoming: the creature's skill in the school of spell <paramref name="spell"/> (ACE's <c>Creature_Skills.GetCreatureSkill(MagicSchool)</c>: Creature Enchantment 31, Item 32, Life 33, War 34), or null when the
    /// dats' SkillTable or SpellTable is not at hand.
    /// </summary>
    private static uint? CasterSkill(uint wcid, CreatureTimingFacts f, uint spell, Dats dats)
    {
        if (dats.Skill is not { } skillOf || dats.School?.Invoke(spell) is not { } school)
        {
            return null;
        }

        uint? id = school switch
        {
            DatReaderWriter.Enums.MagicSchool.CreatureEnchantment => CreatureSkills.CreatureEnchantment,
            DatReaderWriter.Enums.MagicSchool.ItemEnchantment => CreatureSkills.ItemEnchantment,
            DatReaderWriter.Enums.MagicSchool.LifeMagic => CreatureSkills.LifeMagic,
            DatReaderWriter.Enums.MagicSchool.WarMagic => CreatureSkills.WarMagic,
            _ => null,
        };
        return id is { } skill ? Bestiary.SkillOf(wcid, skill, f.Attributes, skillOf(skill)) : null;
    }

    /// <summary>A spell's damage on her, unresisted (see the class).</summary>
    private static SpellAttack Priced(CreatureSpell s, double share, List<string> todos)
    {
        if (s.Projectiles > 0 && s.BaseIntensity >= 0 && s.Variance >= 0 && s.Element != 0)
        {
            var top = s.BaseIntensity + s.Variance;
            var mean = s.Projectiles * ((s.BaseIntensity + (s.Variance / 2.0)) + (MagicCriticalChance * MagicCriticalAdd * top));
            return new SpellAttack(s.Id, s.Name, share, mean, top * (1 + MagicCriticalAdd), s.Element, null);
        }

        if ((s.Boost < 0 && s.DamageType == 0x80) || s.TransferSource == 2)
        {
            var todo = string.Create(CultureInfo.InvariantCulture, $"{LifeSpellTodo}: {s.Name} harms or drains health, not priced (measured live)");
            todos.Add(todo);
            return new SpellAttack(s.Id, s.Name, share, 0, 0, 0, todo);
        }

        return new SpellAttack(s.Id, s.Name, share, 0, 0, 0, null); // a debuff, a buff or a heal: an attack turn with no health damage
    }
}

/// <summary>
/// M6 14 attack timing: the creatures' attack timings as the lines and the guard read them, one cache a dat set (<see cref="For"/>): each weenie's <see cref="AttackTiming"/> derived once from the world database's creature table
/// (<see cref="CreatureTable"/>) and the user's dats; without dats (<see cref="None"/>) every creature is the named fallback (<see cref="AttackTiming.Underived"/>).
/// </summary>
public sealed class CreatureAttackTimes
{
    private static readonly ConditionalWeakTable<Lifestone.Data.Dat.DatSet, CreatureAttackTimes> ByDats = [];
    private readonly AttackTimings.Dats? _dats;
    private readonly ConcurrentDictionary<uint, AttackTiming> _byWcid = new();

    private CreatureAttackTimes(AttackTimings.Dats? dats) => _dats = dats;

    /// <summary>No dats at hand: every creature's timing is the named fallback.</summary>
    public static CreatureAttackTimes None { get; } = new(null);

    /// <summary>The timings read through <paramref name="dats"/> (a test's or the dats').</summary>
    /// <param name="dats">The reads.</param>
    public static CreatureAttackTimes Using(AttackTimings.Dats dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return new CreatureAttackTimes(dats);
    }

    /// <summary>Timings given whole (a replay's, pinned from the dats' derivation), each creature not among them the named fallback.</summary>
    /// <param name="timings">The timings, by their weenie class.</param>
    public static CreatureAttackTimes Fixed(IEnumerable<AttackTiming> timings)
    {
        ArgumentNullException.ThrowIfNull(timings);
        var times = new CreatureAttackTimes(null);
        foreach (var t in timings)
        {
            times._byWcid[t.Wcid] = t;
        }

        return times;
    }

    /// <summary>The timings of <paramref name="source"/>'s dats.</summary>
    /// <param name="source">The dats' animation source.</param>
    public static CreatureAttackTimes FromDats(DatAnimationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CreatureAttackTimes(new AttackTimings.Dats(source.GetMotionTable, source.GetAnimation, source.GetCombatTable));
    }

    /// <summary>The timings of <paramref name="dats"/>, one cache a dat set; <see cref="None"/> without dats.</summary>
    /// <param name="dats">The user's dats, or null.</param>
    public static CreatureAttackTimes For(Lifestone.Data.Dat.DatSet? dats) => dats is null ? None : ByDats.GetValue(dats, d =>
    {
        var source = new DatAnimationSource(d);
        var skills = Lifestone.Data.CharacterCreation.SkillTableData.Load(d);
        var spells = Lifestone.Data.Magic.SpellTableData.Load(d).Spells;
        return new CreatureAttackTimes(new AttackTimings.Dats(source.GetMotionTable, source.GetAnimation, source.GetCombatTable)
        {
            Skill = id => skills.TryGetSkill(id, out var entry) ? entry : null, // M6 14 the derived incoming: the creatures' skills' formulas
            School = id => spells.TryGetValue(id, out var info) ? info.School : null, // and the schools their spells are resisted in
        });
    });

    /// <summary>True when the dats are at hand (the timings are derived).</summary>
    public bool HasDats => _dats is not null;

    /// <summary>The timing of weenie <paramref name="wcid"/>, cached by weenie for a creature of the table (one named <paramref name="name"/> that the table does not hold is the named fallback, never cached under another's name).</summary>
    /// <param name="wcid">The weenie class.</param>
    /// <param name="name">Its name.</param>
    public AttackTiming Of(uint wcid, string name)
    {
        if (_byWcid.TryGetValue(wcid, out var known))
        {
            return known;
        }

        if (Bestiary.Find(wcid) is not { Timing: { } facts } kind)
        {
            return AttackTiming.Underived(wcid, name, "no creature of the world database's table");
        }

        return _byWcid.GetOrAdd(wcid, w => _dats is null ? AttackTiming.Underived(w, kind.Identity.Name, "no dats at hand") : AttackTimings.Derive(w, kind.Identity.Name, facts, _dats));
    }
}

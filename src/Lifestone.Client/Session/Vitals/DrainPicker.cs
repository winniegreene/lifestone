using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One level of Drain Health Other the caster could press: the spell, ACE's transfer row for it, and the cast time.</summary>
/// <param name="Info">The SpellTable's spell (its power, base mana, school and range).</param>
/// <param name="Row">ACE's transfer row (<see cref="DrainPicker.RowOf"/>): the share of the target's current health taken, the loss, the cap.</param>
/// <param name="CastSeconds">How long the cast takes (the score divides by it; Lifestone's own, equal across the line until the cast times are measured, <c>TODO(m5-drain-cast-time)</c>).</param>
public readonly record struct DrainLevel(SpellInfo Info, TransferRow Row, double CastSeconds);

/// <summary>
/// What the picker reads at the moment of the choice (M5 7 F7, the owner's rule: every number live, never fixed at the build): the caster's health and mana, its Life Magic skill as the read model gives it now with the buffs up (and
/// its initial plus ranks, for the range), the target's health and Magic Defense, and the distance to it.
/// </summary>
/// <param name="Health">The caster's current health.</param>
/// <param name="HealthMax">The caster's maximum health.</param>
/// <param name="Mana">The caster's current mana.</param>
/// <param name="LifeMagic">The caster's current Life Magic (<see cref="GearCasts.TryCurrentSkill"/>).</param>
/// <param name="LifeInitPlusRanks">Life Magic's start level plus its ranks, which the range reads.</param>
/// <param name="TargetHealth">The target's current health in points.</param>
/// <param name="TargetMagicDefence">The target's Magic Defense skill as ACE computes it. 0 is a Magic Defense of 0, as ACE rolls it (<see cref="ResistRoll"/>, M6 16 E1e); a caller that passes 0 for a kind it does not know names that itself.</param>
/// <param name="Distance">The distance to the target in metres.</param>
/// <param name="TargetDrainResist">
/// M6 14 drain resistance: the target kind's health-drain resistance (<see cref="DrainResist.Of"/>, its <c>ResistHealthDrain</c>): the share of the take ACE lets through, applied before the cap. 1 takes the full share; 0 gives nothing, so
/// a heal pick expects nothing of a drain on it and takes Heal Self.
/// </param>
public readonly record struct DrainSituation(uint Health, uint HealthMax, uint Mana, uint LifeMagic, uint LifeInitPlusRanks, uint TargetHealth, uint TargetMagicDefence, double Distance, double TargetDrainResist = 1.0);

/// <summary>Why the pick came out as it did (<see cref="DrainPicker.Name(DrainWhy)"/> says it in words for the log).</summary>
public enum DrainWhy
{
    /// <summary>The level with the most expected heal per second of casting.</summary>
    Best,

    /// <summary>A cheaper level whose expected heal is within the tolerance of the best's.</summary>
    CheaperNearBest,

    /// <summary>No level helps enough: even the best heals less than the least worth a cast (a nearly dead target, or a level that cannot land); a bolt is better.</summary>
    NothingWorthACast,

    /// <summary>The mana pays no level (each level's floor is over the mana held).</summary>
    ManaShort,

    /// <summary>The target is out of every level's range.</summary>
    OutOfRange,

    /// <summary>No level is offered (none known, none paid for by the pack).</summary>
    NoLevels,

    /// <summary>The caster is at full health: nothing to heal.</summary>
    NothingMissing,
}

/// <summary>The pick: the level's index in the offered span (-1: no drain, a bolt), the heal it expects, the chance it lands, the mana it costs and why.</summary>
/// <param name="Index">The index into the levels offered, or -1.</param>
/// <param name="ExpectedHeal">The chance times the health a landing gives (0 when none).</param>
/// <param name="Chance">The chance the cast lands: the cast chance times the chance the target does not resist.</param>
/// <param name="Mana">The mana the level costs at most (its ceiling).</param>
/// <param name="HealIfLanded">The health a landing gives.</param>
/// <param name="Why">Why.</param>
public readonly record struct DrainChoice(int Index, double ExpectedHeal, double Chance, uint Mana, uint HealIfLanded, DrainWhy Why);

/// <summary>
/// The drain picker (M5 7 F7, the owner's rule: the drain's level is chosen at the moment of casting, not fixed at the highest one the pack pays for). Engine-free and allocation-free, so the harm-heal and the death handler can read it
/// later. Of the levels offered (the caller has already kept the level floor: only levels whose scarabs the pack holds, <c>LevelFloor</c>), it takes the one that does the most good in a cast.
/// </summary>
/// <remarks>
/// <para><b>What a cast does.</b> ACE's <c>HandleCastSpell_Transfer</c> (<c>WorldObject_Magic.cs</c> 697-760 @95ef426d, the same arithmetic as the Stamina to Mana rows: <see cref="BuffStatMods.Transfer"/>): the target loses
/// <c>round(current health x Proportion)</c>, cut to the row's <c>TransferCap</c> (721-727); the caster gains <c>round(taken x (1 - LossPercent))</c> (732), cut to its own missing health (cut to the cap, 737-749, the taken scaled
/// with it). The target's drain resistance scales the take before the cap (719-726: <c>round(current health x Proportion x drainMod)</c>, the mod its <c>GetResistanceMod(HealthDrain)</c>, its <c>ResistHealthDrain</c>, float property
/// 125): <see cref="DrainSituation.TargetDrainResist"/>, read by kind (<see cref="DrainResist"/>; M6 14 drain resistance closed <c>TODO(m5-drain-resistance-mods)</c> after an Armored Tusker, resistance 0, gave three drains of 0 and the
/// climber died). The caster's boost resistance (730) is 1 (a player with no healing rating). Level VII ("Vitality Siphon") takes more (0.5 of the health, cap 180) but loses half of it; level VI takes 0.4
/// (cap 150) and loses a quarter: on a big target VI heals more than VII, which is why the level is picked and not just the highest.</para>
/// <para><b>The chance a cast lands</b> is two rolls: the cast chance (<see cref="GearCasts.Chance"/>, the 0.07 factor, the fizzle: <c>Player_Magic.cs</c> @95ef426d) and the target's resist (<c>MagicDefenseCheck</c>, <c>WorldObject_Magic.cs</c>
/// 104-113: the 0.03 logistic of the caster's skill against the target's Magic Defense, an even chance when they are equal; <see cref="ResistRoll"/>, the one function the bolt's rate asks too). Their product is the chance.</para>
/// <para><b>The score</b> is the expected heal (chance times the heal a landing gives, cut to the health missing, so a level that overshoots earns nothing for the overshoot) per second of casting. Of the levels whose score is within
/// <see cref="NearBestShare"/> of the best, the cheapest in mana is taken (Lifestone's own tolerance: mana is what the soak runs short of). When even the best expected heal is under <see cref="MinWorthShare"/> of the caster's
/// maximum health the answer is no drain: a bolt that kills the nearly dead target is better than a drain that heals a few points (Lifestone's own share).</para>
/// </remarks>
public static class DrainPicker
{
    /// <summary>The levels whose score is within this share of the best's compete on mana (Lifestone's own number).</summary>
    public const double NearBestShare = 0.10;

    /// <summary>The least expected heal worth a cast, as a share of the caster's maximum health (Lifestone's own number).</summary>
    public const double MinWorthShare = 0.03;

    /// <summary>The slope of ACE's resist roll (<c>SkillCheck.GetSkillChance</c>, called by <c>MagicDefenseCheck</c>: "uses regular 0.03 factor, and not magic casting 0.07 factor", <c>WorldObject_Magic.cs</c> 106).</summary>
    public const double ResistSlope = 0.03;

    /// <summary>The ids of Drain Health Other I to VII (VII is 2328, "Vitality Siphon"); the incantation (4643) has no row in the pinned world SQL, so it is not offered (<c>TODO(m5-drain-viii-row)</c>).</summary>
    public static IReadOnlyList<uint> Ids { get; } = [1237, 1238, 1239, 1240, 1241, 1242, 2328];

    /// <summary>
    /// ACE's transfer rows of the line (the pinned world SQL v0.9.296, table <c>spell</c>, columns <c>source</c> 2 and <c>destination</c> 2 (health), <c>proportion</c>, <c>loss_Percent</c>, <c>transfer_Cap</c>; the bitfield is 6, a target source and a
    /// caster destination; read-only, M5 7 F7).
    /// </summary>
    public static IReadOnlyList<TransferRow> Rows { get; } =
    [
        new(1237, Health, Health, 0.25f, -1.0f, 60),
        new(1238, Health, Health, 0.25f, -0.6f, 80),
        new(1239, Health, Health, 0.25f, -0.33f, 100),
        new(1240, Health, Health, 0.25f, 0.0f, 100),
        new(1241, Health, Health, 0.3f, 0.0f, 120),
        new(1242, Health, Health, 0.4f, 0.25f, 150),
        new(2328, Health, Health, 0.5f, 0.5f, 180),
    ];

    private const uint Health = 2;

    /// <summary>The transfer row of spell <paramref name="id"/>, or null when it is not one of <see cref="Ids"/>.</summary>
    /// <param name="id">The spell id.</param>
    public static TransferRow? RowOf(uint id)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Id == id)
            {
                return Rows[i];
            }
        }

        return null;
    }

    /// <summary>
    /// The chance the target does not resist: ACE's resist roll, the 0.03 logistic of the caster's skill against the target's Magic Defense (<see cref="ResistRoll.LandChance"/>). M6 16 E1e: a Magic Defense of 0 is rolled as
    /// ACE rolls it (the curve of the skill alone: 0.99987 at 298); before, 0 was read as unknown and gave an even 0.5, where the bolt's rate (<see cref="KillRate.LandChance"/>) gave 1 for the same creature.
    /// </summary>
    /// <param name="skill">The caster's skill.</param>
    /// <param name="defence">The target's Magic Defense.</param>
    public static double PassChance(uint skill, uint defence) => ResistRoll.LandChance(skill, defence);

    /// <summary>The health a landed cast of <paramref name="level"/> gives the caster in <paramref name="s"/> (ACE's arithmetic, cut to the health missing).</summary>
    /// <param name="level">The level.</param>
    /// <param name="s">The situation.</param>
    public static uint HealOf(in DrainLevel level, in DrainSituation s)
    {
        var missing = s.HealthMax > s.Health ? s.HealthMax - s.Health : 0u;
        var row = level.Row;
        BuffStatMods.Transfer(in row, s.TargetHealth, missing, out _, out var given, (float)s.TargetDrainResist);
        return given;
    }

    /// <summary>
    /// Picks the level: see the class remarks. <paramref name="floors"/> is each level's mana floor at the moment of the pick (the caller reads the Mana Conversion live and adds the mana it keeps in reserve): a level is offered only when the
    /// mana held is at or over its floor and the target is within its range.
    /// </summary>
    /// <param name="levels">The levels the caller offers, lowest first (the pack pays for each).</param>
    /// <param name="floors">Each level's mana floor, the same length.</param>
    /// <param name="s">The situation, read now.</param>
    public static DrainChoice Pick(ReadOnlySpan<DrainLevel> levels, ReadOnlySpan<uint> floors, in DrainSituation s)
    {
        if (levels.IsEmpty)
        {
            return new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.NoLevels);
        }

        if (s.Health >= s.HealthMax)
        {
            return new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.NothingMissing);
        }

        var anyRange = false;
        var anyMana = false;
        var bestScore = 0.0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (!InRange(in levels[i], in s))
            {
                continue;
            }

            anyRange = true;
            if (s.Mana < floors[i])
            {
                continue;
            }

            anyMana = true;
            bestScore = Math.Max(bestScore, Score(in levels[i], in s, out _, out _));
        }

        if (!anyRange)
        {
            return new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.OutOfRange);
        }

        if (!anyMana)
        {
            return new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.ManaShort);
        }

        var pick = -1;
        var pickMana = uint.MaxValue;
        var pickScore = 0.0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (!InRange(in levels[i], in s) || s.Mana < floors[i])
            {
                continue;
            }

            var score = Score(in levels[i], in s, out _, out _);
            var mana = GearCasts.ManaCeiling(levels[i].Info, 0);
            if (score >= bestScore * (1.0 - NearBestShare) && (mana < pickMana || (mana == pickMana && score > pickScore)))
            {
                (pick, pickMana, pickScore) = (i, mana, score);
            }
        }

        if (pick < 0)
        {
            return new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.NothingWorthACast);
        }

        Score(in levels[pick], in s, out var chance, out var expected);
        var heal = HealOf(in levels[pick], in s);
        if (expected < MinWorthShare * s.HealthMax)
        {
            return new DrainChoice(-1, expected, chance, pickMana, heal, DrainWhy.NothingWorthACast);
        }

        return new DrainChoice(pick, expected, chance, pickMana, heal, pickScore >= bestScore ? DrainWhy.Best : DrainWhy.CheaperNearBest);
    }

    /// <summary>The words for the log.</summary>
    /// <param name="why">The reason.</param>
    public static string Name(DrainWhy why) => why switch
    {
        DrainWhy.Best => "the most expected heal a second",
        DrainWhy.CheaperNearBest => "a cheaper level within a tenth of the best expected heal",
        DrainWhy.NothingWorthACast => "no level heals enough to be worth a cast; a bolt instead",
        DrainWhy.ManaShort => "the mana pays no level; a bolt instead",
        DrainWhy.OutOfRange => "the target is out of range of every level; a bolt instead",
        DrainWhy.NoLevels => "no level is known and paid for by the pack",
        _ => "the health is full",
    };

    private static bool InRange(in DrainLevel level, in DrainSituation s) => s.Distance <= FightScenario.CastRange(level.Info, s.LifeInitPlusRanks);

    private static double Score(in DrainLevel level, in DrainSituation s, out double chance, out double expected)
    {
        chance = GearCasts.Chance(s.LifeMagic, level.Info.Power) * PassChance(s.LifeMagic, s.TargetMagicDefence);
        expected = chance * HealOf(in level, in s);
        return level.CastSeconds > 0 ? expected / level.CastSeconds : expected;
    }
}

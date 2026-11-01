using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;

namespace Lifestone.Client.Session.Mage;

/// <summary>The war spell shapes ACE tells apart (<c>SpellProjectile.GetProjectileSpellType</c>, SpellProjectile.cs 118-163 at 95ef426d).</summary>
public enum WarKind
{
    /// <summary>One tracking projectile (Flame Bolt).</summary>
    Bolt,

    /// <summary>One fast tracking projectile, no wind-up (FastCast; Flame Streak).</summary>
    Streak,

    /// <summary>One non-tracking lobbed projectile (Flame Arc).</summary>
    Arc,

    /// <summary>Three (five at VII) parallel projectiles at the target (Flame Volley).</summary>
    Volley,

    /// <summary>Three (five at VII) projectiles in a 90 degree fan (Flame Blast).</summary>
    Blast,

    /// <summary>Nine projectiles in a 360 degree ring around the caster (Cassius' Ring of Fire).</summary>
    Ring,
}

/// <summary>
/// One war spell's projectile data from ACE's world database (the pinned world SQL v0.9.296, table <c>spell</c>, read-only, 2026-10-28; <c>SpellProperties.cs</c> 262-270 at 95ef426d: a hit is uniform on
/// <c>BaseIntensity</c> to <c>BaseIntensity + Variance</c>), with the projectile speed of its weenie (<c>MaximumVelocity</c>, <c>WorldObject_Magic.GetProjectileSpeed</c> 1898-1921).
/// </summary>
/// <param name="Id">The spell id.</param>
/// <param name="Kind">The spell's shape.</param>
/// <param name="Level">The level, 1 to 8 (the Incantation); the ring is the VI-equivalent (power 200, the Searing Disc and Flame Ring's).</param>
/// <param name="BaseIntensity">The least damage of a projectile.</param>
/// <param name="Variance">The damage added at most.</param>
/// <param name="Projectiles">The projectiles one cast launches.</param>
/// <param name="SpeedMetresPerSecond">The projectile's speed.</param>
public sealed record WarSpellRow(uint Id, WarKind Kind, int Level, int BaseIntensity, int Variance, int Projectiles, double SpeedMetresPerSecond);

/// <summary>The war lines' projectile data (see <see cref="WarSpellRow"/>) and the cast seconds by shape.</summary>
public static class WarSpellData
{
    private const double BoltSpeed = 15;
    private const double StreakSpeed = 45;
    private const double ArcSpeed = 40;
    private const double RingSpeed = 2;

    /// <summary>The rows: bolt, streak, arc, volley, blast and the ring.</summary>
    public static IReadOnlyList<WarSpellRow> Rows { get; } =
    [
        new(27, WarKind.Bolt, 1, 16, 15, 1, BoltSpeed), new(81, WarKind.Bolt, 2, 26, 26, 1, BoltSpeed), new(82, WarKind.Bolt, 3, 42, 42, 1, BoltSpeed), new(83, WarKind.Bolt, 4, 52, 53, 1, BoltSpeed),
        new(84, WarKind.Bolt, 5, 68, 68, 1, BoltSpeed), new(85, WarKind.Bolt, 6, 84, 84, 1, BoltSpeed), new(2128, WarKind.Bolt, 7, 115, 74, 1, BoltSpeed), new(4439, WarKind.Bolt, 8, 142, 62, 1, BoltSpeed),
        new(1796, WarKind.Streak, 1, 16, 15, 1, StreakSpeed), new(1797, WarKind.Streak, 2, 18, 17, 1, StreakSpeed), new(1798, WarKind.Streak, 3, 21, 21, 1, StreakSpeed), new(1799, WarKind.Streak, 4, 25, 25, 1, StreakSpeed),
        new(1800, WarKind.Streak, 5, 29, 30, 1, StreakSpeed), new(1801, WarKind.Streak, 6, 36, 35, 1, StreakSpeed), new(2129, WarKind.Streak, 7, 42, 42, 1, StreakSpeed), new(4440, WarKind.Streak, 8, 47, 47, 1, StreakSpeed),
        new(2739, WarKind.Arc, 1, 16, 14, 1, ArcSpeed), new(2740, WarKind.Arc, 2, 26, 26, 1, ArcSpeed), new(2741, WarKind.Arc, 3, 42, 42, 1, ArcSpeed), new(2742, WarKind.Arc, 4, 52, 53, 1, ArcSpeed),
        new(2743, WarKind.Arc, 5, 68, 68, 1, ArcSpeed), new(2744, WarKind.Arc, 6, 84, 84, 1, ArcSpeed), new(2745, WarKind.Arc, 7, 115, 74, 1, ArcSpeed), new(4423, WarKind.Arc, 8, 142, 62, 1, ArcSpeed),
        new(143, WarKind.Volley, 3, 15, 16, 3, BoltSpeed), new(144, WarKind.Volley, 4, 20, 20, 3, BoltSpeed), new(145, WarKind.Volley, 5, 26, 26, 3, BoltSpeed), new(146, WarKind.Volley, 6, 34, 35, 3, BoltSpeed),
        new(2130, WarKind.Volley, 7, 42, 42, 5, BoltSpeed), new(4441, WarKind.Volley, 8, 47, 47, 5, BoltSpeed),
        new(3662, WarKind.Blast, 1, 11, 10, 3, BoltSpeed), new(115, WarKind.Blast, 3, 15, 16, 3, BoltSpeed), new(116, WarKind.Blast, 4, 20, 20, 3, BoltSpeed), new(117, WarKind.Blast, 5, 26, 26, 3, BoltSpeed),
        new(118, WarKind.Blast, 6, 34, 35, 3, BoltSpeed), new(2127, WarKind.Blast, 7, 42, 42, 5, BoltSpeed), new(4438, WarKind.Blast, 8, 47, 47, 5, BoltSpeed),
        new(1785, WarKind.Ring, 6, 42, 42, 9, RingSpeed),
    ];

    private static readonly Dictionary<uint, WarSpellRow> ById = Rows.ToDictionary(r => r.Id);

    /// <summary>The row of spell <paramref name="id"/>.</summary>
    /// <param name="id">The spell id.</param>
    /// <param name="row">The row, or null.</param>
    /// <returns>True when the spell has a row.</returns>
    public static bool TryGet(uint id, out WarSpellRow row)
    {
        var found = ById.TryGetValue(id, out var value);
        row = value!;
        return found;
    }

    /// <summary>
    /// The highest-level spell of <paramref name="ids"/> that has a row, is <paramref name="usable"/> (known, in the table, a cast chance paid) and is not above <paramref name="cap"/> (the plan's cap, <c>FightScenario.PlanCap</c>:
    /// the den's VI below level 100): a spell above the cap is never chosen, only noted in <paramref name="capped"/>.
    /// </summary>
    /// <param name="ids">The line's spell ids.</param>
    /// <param name="cap">The highest level allowed, 1 to 7.</param>
    /// <param name="usable">Whether the mage can cast the spell.</param>
    /// <param name="capped">True when a spell with a row was left out for being above the cap.</param>
    /// <returns>The row, or null.</returns>
    public static WarSpellRow? HighestUnderCap(IEnumerable<uint> ids, int cap, Func<uint, bool> usable, out bool capped)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(usable);
        capped = false;
        WarSpellRow? best = null;
        foreach (var id in ids)
        {
            if (!ById.TryGetValue(id, out var row))
            {
                continue;
            }

            if (row.Level > cap)
            {
                capped = true;
            }
            else if (usable(id) && (best is null || row.Level > best.Level))
            {
                best = row;
            }
        }

        return best;
    }

    /// <summary>
    /// The seconds from the cast request to the spell leaving the hand, by shape, at level VI: the player cast plan over the motion table (<c>CastGesturePlanner</c>; the Flame Bolt VI's 2.30 s is the
    /// plan's, item 0 measured 2.40 s on the wire). The Streak is FastCast (no wind-up gestures, <c>Player_Magic.cs</c> 607). Registered estimates, Lifestone's own, for the rehearsal to measure (D89); another level of
    /// the same shape differs by a few hundredths.
    /// </summary>
    /// <param name="kind">The shape.</param>
    public static double EffectSeconds(WarKind kind) => kind switch
    {
        WarKind.Streak => 0.52,
        WarKind.Volley or WarKind.Blast => 2.11,
        WarKind.Ring => 2.67,
        _ => 2.30,
    };
}

/// <summary>The picker's registered estimates (D89): every one is Lifestone's own and named in the log of the fight that uses it.</summary>
public sealed record WarPickSettings
{
    /// <summary>The stamina-to-mana dance's seconds a mana point at level 80 (the owner's D89 figure, a registered estimate the rehearsal measures).</summary>
    public double DanceSecondsPerMana { get; init; } = 0.09;

    /// <summary>The seconds a cast costs beyond its own gesture (the turn, the pick and the hit wait): 0.4, which with the Flame Bolt VI's 2.30 s and a 0.6 s mana share gives item 0's measured 2.9 s a bolt at 5 m.</summary>
    public double CastOverheadSeconds { get; init; } = 0.4;

    /// <summary>The war critical chance (<c>WorldObject_Weapon.cs</c> 324 at 95ef426d: 5 percent).</summary>
    public double CritChance { get; init; } = 0.05;

    /// <summary>The damage rating's multiplier (the heritage 1.05, <c>SpellProjectile.cs</c> 738-761; M5 7's vulnerability note).</summary>
    public double DamageRating { get; init; } = 1.05;

    /// <summary>The fire resistance of the target as a multiplier (the tuskers' ResistFire 1.0).</summary>
    public double ResistMod { get; init; } = 1.0;

    /// <summary>The radius of a target's body in metres, for the ring's hits: a registered estimate (<c>TODO(m5-war-pick-radius)</c>), 0.8.</summary>
    public double TargetRadiusMetres { get; init; } = 0.8;

    /// <summary>The share of a volley's parallel projectiles that hit one target (2 of 3: the side projectiles clip a narrow target).</summary>
    public double VolleyHitShare { get; init; } = 2.0 / 3.0;

    /// <summary>The most a ring's projectiles hit one body: two neighbours.</summary>
    public double MaxRingHitsPerTarget { get; init; } = 2.0;

    /// <summary>
    /// M5 7 F24 (soak round 3: Flame Streak VI was picked 198 times, 0.38 hits a cast, 26 a cast, mostly finishers): the streak is weighed only against a target whose live health (F20 model) is under this share of its full health, 0.15.
    /// A registered estimate, Lifestone own.
    /// </summary>
    public double StreakFinisherFraction { get; init; } = 0.15;

    /// <summary>The default for <see cref="RingMinTargets"/>: three (D89).</summary>
    public const int DefaultRingMinTargets = 3;

    /// <summary>
    /// The fewest targets within the ring's reach before it is weighed at all (M5 7 F22; the ground's <c>fight.ring_min_targets</c>, the default 3 is D89's "three or more tuskers"). The measured hit factor
    /// (<see cref="RingHitFactor"/>) raises the effective trigger above it; this floor guards the start.
    /// </summary>
    public int RingMinTargets { get; init; } = DefaultRingMinTargets;
}

/// <summary>
/// M5 7 F22: the ring's measured hit factor, the realised hits of a ring cast over the hits the picker predicted at the pick, an exponentially weighted mean (<see cref="FightRates.Weight"/> a sample) seeded from rehearsal 9
/// (55 ring casts, 153 hits from ACE's "with Cassius' Ring of Fire" lines: 2.78 hits a cast; the predicted average was not logged, so the seed is the registered estimate 0.6). The ring is slow (2 m/s) and the tuskers move,
/// so the model's count of bodies within reach overstates what lands. Allocation-free.
/// </summary>
public sealed class RingHitFactor(double seed = RingHitFactor.Seed)
{
    /// <summary>The seed: 0.6 (rehearsal 9's 2.78 hits a cast over a predicted average that was not logged; <c>TODO(m5-ring-seed)</c>: replace it by the measured ratio of the next run's <c>ring:</c> lines).</summary>
    public const double Seed = 0.6;

    /// <summary>The least the factor falls to: a factor near zero would keep the ring unchosen for good, and a ring never cast is never measured.</summary>
    public const double Floor = 0.2;

    /// <summary>The most the factor rises to (a ring cannot be expected to hit more than the model's count by much).</summary>
    public const double Ceiling = 1.5;

    /// <summary>The factor now.</summary>
    public double Factor { get; private set; } = seed;

    /// <summary>The ring casts measured.</summary>
    public int Samples { get; private set; }

    /// <summary>A ring cast ended: <paramref name="hits"/> landed of <paramref name="predicted"/> predicted. Ignored when nothing was predicted.</summary>
    /// <param name="hits">The hits counted from the chat lines.</param>
    /// <param name="predicted">The picker's hits at the pick, unscaled.</param>
    public void Note(int hits, double predicted)
    {
        if (predicted <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(hits / predicted, 0.0, 2.0);
        Factor = Math.Clamp(Factor + (FightRates.Weight * (ratio - Factor)), Floor, Ceiling);
        Samples++;
    }
}

/// <summary>
/// M5 7 F22: the ring casts in flight and their hits. A ring is slow (2 m/s over up to 4.5 m), so its last projectiles land after the next cast's request: a damage line belongs to the oldest ring cast that has left the hand, and a
/// cast closes when the next ring leaves the hand or <see cref="WindowSeconds"/> after its own release. Each close logs one <c>ring:</c> line and moves the factor. At most two casts are open.
/// </summary>
public sealed class RingCasts(RingHitFactor factor, ILogSink log, string category, string label = "ring")
{
    /// <summary>The seconds after a release a cast stays open for its hits: the farthest target (4.5 m) at 2 m/s is 2.25 s, and a margin.</summary>
    public const double WindowSeconds = 6.0;

    private Cast _first;
    private Cast _second;

    /// <summary>The ring casts open now.</summary>
    public int Open => (_first.Active ? 1 : 0) + (_second.Active ? 1 : 0);

    /// <summary>A ring cast is requested.</summary>
    /// <param name="predicted">The hits the picker predicted at the pick, unscaled by the factor.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="anchorHealth">M5 7 F28: the health share (0 to 1) of the creature the cast is aimed at, shown in the close line; NaN when unknown.</param>
    public void Begin(double predicted, double clock, double anchorHealth = double.NaN)
    {
        if (_second.Active)
        {
            Close(ref _first); // never reached in play: a cast ends before the next begins
            _first = _second;
            _second = default;
        }

        var cast = new Cast { Active = true, Predicted = predicted, Start = clock, AnchorHealth = anchorHealth };
        if (_first.Active)
        {
            _second = cast;
        }
        else
        {
            _first = cast;
        }
    }

    /// <summary>The newest ring cast ended: it left the hand when <paramref name="landed"/> (the projectiles exist), else it is dropped and named.</summary>
    /// <param name="landed">True when the cast was confirmed.</param>
    /// <param name="clock">The session clock.</param>
    public void Ended(bool landed, double clock)
    {
        var second = _second.Active;
        ref var newest = ref (second ? ref _second : ref _first);
        if (!newest.Active || newest.Released)
        {
            return;
        }

        if (!landed)
        {
            log.Info(category, label + ": the cast did not leave the hand; no hits counted");
            newest = default;
            return;
        }

        newest.Released = true;
        newest.ReleasedAt = clock;
        if (second && _first.Released)
        {
            Close(ref _first); // the older ring's projectiles have landed or are gone: the new ring has left the hand
            _first = _second;
            _second = default;
        }
    }

    /// <summary>A "with Cassius' Ring of Fire" damage line: a hit of the oldest released cast.</summary>
    /// <param name="damage">The damage in the line.</param>
    /// <returns>True when a cast took it.</returns>
    public bool NoteHit(long damage)
    {
        ref var cast = ref (_first.Active && _first.Released ? ref _first : ref _second);
        if (!cast.Active || !cast.Released)
        {
            return false;
        }

        cast.Hits++;
        cast.Damage += damage;
        return true;
    }

    /// <summary>Closes the casts whose window has passed.</summary>
    /// <param name="clock">The session clock.</param>
    public void Tick(double clock)
    {
        if (_first.Active && _first.Released && clock - _first.ReleasedAt >= WindowSeconds)
        {
            Close(ref _first);
            _first = _second;
            _second = default;
        }

        if (_second.Active && _second.Released && clock - _second.ReleasedAt >= WindowSeconds)
        {
            Close(ref _second);
        }
    }

    /// <summary>Closes every open cast (the fight ended).</summary>
    public void CloseAll()
    {
        Close(ref _first);
        Close(ref _second);
    }

    private void Close(ref Cast cast)
    {
        var c = cast;
        cast = default;
        if (!c.Active || !c.Released)
        {
            return;
        }

        factor.Note(c.Hits, c.Predicted);
        log.Info(category, string.Create(CultureInfo.InvariantCulture, $"{label}: hits {c.Hits} of {c.Predicted:0.0} predicted, damage {c.Damage}, {c.ReleasedAt - c.Start:0.0} s{(double.IsNaN(c.AnchorHealth) ? string.Empty : ", anchor health " + (c.AnchorHealth * 100).ToString("0", CultureInfo.InvariantCulture) + "%")}, factor now {factor.Factor:0.00}"));
    }

    private struct Cast
    {
        public bool Active;
        public bool Released;
        public double Predicted;
        public double Start;
        public double ReleasedAt;
        public double AnchorHealth;
        public int Hits;
        public long Damage;
    }
}

/// <summary>One war spell the mage could cast now: its data, its price and its reach. Option 0 of a picker's list is the bolt.</summary>
/// <param name="Family">The line's name.</param>
/// <param name="Name">The spell's name.</param>
/// <param name="Row">The projectile data.</param>
/// <param name="Power">The spell's power (the dat).</param>
/// <param name="BaseMana">The spell's base mana (the dat).</param>
/// <param name="IgnoresManaConversion">True when the spell ignores Mana Conversion.</param>
/// <param name="ReachMetres">The cast range (the dat: <c>BaseRangeConstant + BaseRangeMod x (init + ranks)</c>, <c>Player_Magic.VerifySpellRange</c> 481-505); the ring's is 4.5 m.</param>
/// <param name="EffectSeconds">The seconds from the request to the spell leaving the hand.</param>
public sealed record WarOption(string Family, string Name, WarSpellRow Row, uint Power, uint BaseMana, bool IgnoresManaConversion, double ReachMetres, double EffectSeconds)
{
    /// <summary>The levels array the cast request takes: this spell alone.</summary>
    public uint[] Levels { get; } = [Row.Id];
}

/// <summary>One creature the picker weighs.</summary>
/// <param name="Distance">Its distance from the mage in metres.</param>
/// <param name="HealthFraction">Its health, 0 to 1.</param>
public readonly record struct WarTarget(double Distance, double HealthFraction);

/// <summary>The mage's live numbers.</summary>
/// <param name="WarSkill">The current War Magic skill (the buffed value).</param>
/// <param name="ManaConversion">The current Mana Conversion skill.</param>
/// <param name="ManaHeld">The mana held now.</param>
/// <param name="TargetMaxHealth">What a target is taken to have at full health (0: unknown).</param>
public readonly record struct WarMage(uint WarSkill, uint ManaConversion, uint ManaHeld, double TargetMaxHealth);

/// <summary>
/// D89 (M5 item 7 F9): the fight's war spell by situation. For each option it works out the seconds all in to clear what the option hits, per kill: the casts the cast target needs at the option's mean hit (ACE's
/// <c>SpellProjectile.CalculateDamage</c>, SpellProjectile.cs 490-560 at 95ef426d: a uniform roll, plus the skill bonus <c>MinDamage x (skill - power) / 1000</c>, plus a critical's half of the maximum, times the rating
/// and the resistance), each cast costing its gesture, the overhead and the dance's seconds for its mana (<see cref="ManaConversionModel.ExpectedCost"/>, the cast policy's formula), plus the last projectile's flight; the
/// ring and the volley count the extra hits, and every target they hit counts the share of a kill the casts do (at most one). The least seconds a kill wins; a tie keeps the bolt (option 0).
/// </summary>
public sealed class WarSpellPicker(WarPickSettings settings)
{
    /// <summary>Why an option could not be evaluated.</summary>
    public enum Unusable
    {
        /// <summary>It can be.</summary>
        None,

        /// <summary>No target.</summary>
        NoTarget,

        /// <summary>The target's full health is unknown.</summary>
        HealthUnknown,

        /// <summary>The cast target is beyond the option's reach.</summary>
        OutOfReach,

        /// <summary>The mana held is under the option's base mana (the mana order is the bolt's).</summary>
        Mana,

        /// <summary>The ring has fewer targets within its reach than the ground's floor (<see cref="WarPickSettings.RingMinTargets"/>).</summary>
        TooFewTargets,

        /// <summary>The streak is picked only as a finisher (M5 7 F24): the cast target live health is over <see cref="WarPickSettings.StreakFinisherFraction"/>.</summary>
        NotFinisher,
    }

    private readonly WarPickSettings _s = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>The picker's estimates.</summary>
    public WarPickSettings Settings => _s;

    /// <summary>The ring's measured hit factor (M5 7 F22): the model's hits for the ring are scaled by it.</summary>
    public RingHitFactor RingFactor { get; } = new();

    /// <summary>
    /// The volley seed (M5 7 F24, soak round 3 live chat lines: 137 volley picks, 1.26 hits a cast of its three projectiles, 71 a hit; the model has 3 x 2/3 = 2.0 hits a cast): 1.3 hits a cast over the model 2.0 is 0.65.
    /// </summary>
    public const double VolleySeed = 1.3 / 2.0;

    /// <summary>The volley measured hit factor (M5 7 F24): the model hits for the volley are scaled by it, as the ring ones are (<see cref="RingFactor"/>).</summary>
    public RingHitFactor VolleyFactor { get; } = new(VolleySeed);

    /// <summary>The mean damage of one hit of <paramref name="option"/> at <paramref name="warSkill"/>.</summary>
    /// <param name="option">The option.</param>
    /// <param name="warSkill">The current War Magic skill.</param>
    public double MeanHit(WarOption option, uint warSkill)
    {
        ArgumentNullException.ThrowIfNull(option);
        var min = option.Row.BaseIntensity;
        var max = min + option.Row.Variance;
        var skillBonus = warSkill > option.Power ? min * ((warSkill - option.Power) / 1000.0) : 0.0;
        return (((min + max) / 2.0) + skillBonus + (_s.CritChance * 0.5 * max)) * _s.DamageRating * _s.ResistMod;
    }

    /// <summary>The expected projectiles of <paramref name="option"/> that hit the target at <paramref name="distance"/>; <paramref name="castTarget"/> is true for the target the cast is aimed at.</summary>
    /// <param name="option">The option.</param>
    /// <param name="distance">The target's distance in metres.</param>
    /// <param name="castTarget">True for the aimed target.</param>
    public double Hits(WarOption option, double distance, bool castTarget)
    {
        ArgumentNullException.ThrowIfNull(option);
        var raw = RawHits(option, distance, castTarget);
        return option.Row.Kind switch
        {
            WarKind.Ring => raw * RingFactor.Factor, // M5 7 F22: the ring lands what it landed, not what the bodies within reach promise
            WarKind.Volley => raw * VolleyFactor.Factor, // M5 7 F24: nor does the volley land all its projectiles
            _ => raw,
        };
    }

    /// <summary>The model's hits before the measured factor: what a log's "predicted" is.</summary>
    /// <param name="option">The option.</param>
    /// <param name="distance">The target's distance in metres.</param>
    /// <param name="castTarget">True for the aimed target.</param>
    public double RawHits(WarOption option, double distance, bool castTarget)
    {
        ArgumentNullException.ThrowIfNull(option);
        switch (option.Row.Kind)
        {
            case WarKind.Volley:
                return castTarget ? option.Row.Projectiles * _s.VolleyHitShare : 0.0;
            case WarKind.Ring:
                if (distance > option.ReachMetres)
                {
                    return 0.0;
                }

                var r = _s.TargetRadiusMetres;
                var half = Math.Asin(Math.Min(1.0, r / Math.Max(distance, r)));
                var arc = (2.0 * half / (2.0 * Math.PI)) * option.Row.Projectiles; // the projectiles whose lines cross the body: its angular width over the 40 degrees between two
                return Math.Min(_s.MaxRingHitsPerTarget, Math.Max(castTarget ? 1.0 : 0.0, arc));
            default:
                return castTarget ? 1.0 : 0.0;
        }
    }

    /// <summary>The hits <paramref name="option"/> is predicted to make on all of <paramref name="targets"/> before the measured factor (the first is the cast target).</summary>
    /// <param name="option">The option.</param>
    /// <param name="targets">The creatures; the first is the cast target.</param>
    public double PredictedHits(WarOption option, ReadOnlySpan<WarTarget> targets)
    {
        var sum = 0.0;
        for (var i = 0; i < targets.Length; i++)
        {
            sum += RawHits(option, targets[i].Distance, castTarget: i == 0);
        }

        return sum;
    }

    /// <summary>The seconds all in a kill of <paramref name="option"/> against <paramref name="targets"/> (the first is the cast target).</summary>
    /// <param name="option">The option.</param>
    /// <param name="targets">The creatures; the first is the cast target.</param>
    /// <param name="mage">The mage's numbers.</param>
    /// <param name="seconds">The seconds a kill, or NaN.</param>
    /// <param name="kills">The kills the casts make: each target hit counts the share of it cleared, at most one (the cast target included).</param>
    /// <param name="casts">The casts to clear the cast target.</param>
    /// <returns>Why it could not be worked out, or <see cref="Unusable.None"/>.</returns>
    public Unusable Evaluate(WarOption option, ReadOnlySpan<WarTarget> targets, in WarMage mage, out double seconds, out double kills, out int casts)
    {
        ArgumentNullException.ThrowIfNull(option);
        (seconds, kills, casts) = (double.NaN, 0.0, 0);
        if (targets.IsEmpty)
        {
            return Unusable.NoTarget;
        }

        if (mage.TargetMaxHealth <= 0)
        {
            return Unusable.HealthUnknown;
        }

        if (targets[0].Distance > option.ReachMetres)
        {
            return Unusable.OutOfReach;
        }

        if (option.Row.Kind != WarKind.Bolt && mage.ManaHeld < option.BaseMana)
        {
            return Unusable.Mana;
        }

        if (option.Row.Kind == WarKind.Ring)
        {
            var near = 0;
            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i].Distance <= option.ReachMetres)
                {
                    near++;
                }
            }

            if (near < _s.RingMinTargets)
            {
                return Unusable.TooFewTargets;
            }
        }

        if (option.Row.Kind == WarKind.Streak && targets[0].HealthFraction > _s.StreakFinisherFraction)
        {
            return Unusable.NotFinisher; // M5 7 F24: a streak is a finisher, not an attack
        }

        var hit = MeanHit(option, mage.WarSkill);
        var mana = ManaConversionModel.ExpectedCost(option.BaseMana, option.Power, mage.ManaConversion, option.IgnoresManaConversion);
        var perCast = option.EffectSeconds + _s.CastOverheadSeconds + (mana * _s.DanceSecondsPerMana);
        casts = CastsToClear(targets[0].HealthFraction * mage.TargetMaxHealth, hit * Hits(option, targets[0].Distance, castTarget: true));
        for (var i = 0; i < targets.Length; i++)
        {
            var perCastDamage = hit * Hits(option, targets[i].Distance, castTarget: i == 0);
            var health = targets[i].HealthFraction * mage.TargetMaxHealth;
            if (perCastDamage > 0)
            {
                kills += health <= 0 ? 1.0 : Math.Min(1.0, casts * perCastDamage / health); // a target the casts leave nearly dead counts nearly a kill: the next bolt finishes it
            }
        }

        var flight = option.Row.SpeedMetresPerSecond > 0 ? targets[0].Distance / option.Row.SpeedMetresPerSecond : 0.0;
        seconds = ((casts * perCast) + flight) / Math.Max(kills, 1.0);
        return Unusable.None;
    }

    /// <summary>
    /// Chooses among <paramref name="options"/> (option 0 is the bolt, the fallback). Allocation-free.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="targets">The creatures; the first is the cast target.</param>
    /// <param name="mage">The mage's numbers.</param>
    /// <param name="seconds">The chosen option's seconds a kill.</param>
    /// <param name="boltSeconds">The bolt's, or NaN when it could not be worked out.</param>
    /// <param name="missing">Why the bolt could not be worked out (the pick is then the bolt: the data is missing), else <see cref="Unusable.None"/>.</param>
    /// <returns>The chosen option's index (0 for the bolt).</returns>
    public int Choose(IReadOnlyList<WarOption> options, ReadOnlySpan<WarTarget> targets, in WarMage mage, out double seconds, out double boltSeconds, out Unusable missing)
    {
        ArgumentNullException.ThrowIfNull(options);
        seconds = boltSeconds = double.NaN;
        missing = Unusable.NoTarget;
        if (options.Count == 0)
        {
            return 0;
        }

        missing = Evaluate(options[0], targets, in mage, out boltSeconds, out _, out _);
        seconds = boltSeconds;
        if (missing != Unusable.None)
        {
            return 0;
        }

        var best = 0;
        for (var i = 1; i < options.Count; i++)
        {
            if (Evaluate(options[i], targets, in mage, out var s, out _, out _) == Unusable.None && s < seconds - 1e-9)
            {
                (best, seconds) = (i, s);
            }
        }

        return best;
    }

    private static int CastsToClear(double health, double damagePerCast) => damagePerCast <= 0 ? int.MaxValue : Math.Max(1, (int)Math.Ceiling((health / damagePerCast) - 1e-9));
}

/// <summary>
/// The fight's pick, kept: the options, the counts by shape, the last pick and the log lines (one when the pick changes, one for each option left out and why). The fight feeds it the situation at each cast.
/// </summary>
public sealed class WarPicks(WarSpellPicker picker, ILogSink log, string category)
{
    private readonly int[] _counts = new int[Enum.GetValues<WarKind>().Length];
    private readonly HashSet<string> _noted = [];
    private IReadOnlyList<WarOption> _options = [];
    private int _last = -1;
    private WarKind _lastKind = WarKind.Bolt;

    /// <summary>The picker.</summary>
    public WarSpellPicker Picker => picker;

    /// <summary>The options now (option 0 is the bolt).</summary>
    public IReadOnlyList<WarOption> Options => _options;

    /// <summary>The casts counted by shape.</summary>
    public IReadOnlyList<int> Counts => _counts;

    /// <summary>Takes the options; the last pick is forgotten so the next is logged.</summary>
    /// <param name="options">Option 0 is the bolt.</param>
    public void SetOptions(IReadOnlyList<WarOption> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _last = -1;
    }

    /// <summary>Logs <paramref name="line"/> once (an option left out, a stub: the same text is never logged twice).</summary>
    /// <param name="line">The text.</param>
    public void NoteOnce(string line)
    {
        if (_noted.Add(line))
        {
            log.Info(category, line);
        }
    }

    /// <summary>Picks the spell for the next cast, counts it, and logs it when it differs from the last.</summary>
    /// <param name="targets">The creatures; the first is the cast target.</param>
    /// <param name="mage">The mage's numbers.</param>
    /// <returns>The option to cast, or null for the bolt in hand (the data for a pick is missing, logged once).</returns>
    public WarOption? Pick(ReadOnlySpan<WarTarget> targets, in WarMage mage)
    {
        if (_options.Count == 0)
        {
            return null;
        }

        var index = picker.Choose(_options, targets, in mage, out var seconds, out var boltSeconds, out var missing);
        if (missing != WarSpellPicker.Unusable.None)
        {
            NoteOnce($"TODO(m5-war-pick-data): the bolt's seconds could not be worked out ({missing}); the bolt stays");
            Count(0);
            return null;
        }

        Count(index);
        if (index != _last)
        {
            _last = index;
            var chosen = _options[index];
            picker.Evaluate(chosen, targets, in mage, out _, out var kills, out var casts);
            log.Info(category, string.Create(CultureInfo.InvariantCulture, $"war pick: {chosen.Name} (all-in s={seconds:0.0} vs bolt {boltSeconds:0.0}; {casts} cast(s) are {kills:0.0} kill(s) of {targets.Length} in view, {chosen.Row.Kind.ToString().ToLowerInvariant()} at {targets[0].Distance:0.#} m)"));
        }

        return index == 0 ? null : _options[index];
    }

    /// <summary>The verdict field, <c>picks=bolt:40,ring:2</c> (only shapes cast), or <c>picks=none</c>.</summary>
    public string Field()
    {
        var parts = new List<string>();
        for (var i = 0; i < _counts.Length; i++)
        {
            if (_counts[i] > 0)
            {
                parts.Add(((WarKind)i).ToString().ToLowerInvariant() + ":" + _counts[i].ToString(CultureInfo.InvariantCulture));
            }
        }

        return "picks=" + (parts.Count == 0 ? "none" : string.Join(',', parts));
    }

    private void Count(int index)
    {
        _lastKind = index < _options.Count ? _options[index].Row.Kind : WarKind.Bolt;
        _counts[(int)_lastKind]++;
    }
}

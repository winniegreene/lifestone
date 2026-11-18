using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 7 AB: the ground's rest before the next pull (the profile's <c>fight</c> section; a ground without it has no rest rule: the den at level 80 is fed by the dance, D88, and keeps its numbers). Every field is data (D26).
/// </summary>
/// <param name="ManaPercent"><c>fight.pull_mana_percent</c>: under this mana share the fight rests before it picks a new target (0: mana never rests). One pull's cost: casts a kill times the bolt's mana plus a fight's heals, named in the profile's <c>source</c>.</param>
/// <param name="HealthPercent"><c>fight.pull_health_percent</c>: the same for the health share (0: health never rests).</param>
/// <param name="UntilPercent"><c>fight.pull_rest_until_percent</c>: the share of both vitals a rest goes on to (at least both floors).</param>
/// <param name="Posture"><c>fight.pull_rest_posture</c>: sit, lie or stand. Mana takes no stance factor; the posture buys health and stamina only, and it costs defence (<see cref="PullRestRule"/>).</param>
/// <param name="AggroMetres"><c>fight.pull_aggro_metres</c>: the reach of the ground's creatures' sight (ACE <c>VisualAwarenessRange</c>, PropertyFloat 31): a living target within it, with a clear line, refuses or ends a rest.</param>
/// <param name="StaminaPercent"><c>fight.pull_stamina_percent</c>: M6 7 AN: the same for the stamina share (0: stamina never rests).</param>
/// <param name="FullPercent"><c>fight.pull_full_percent</c>: M6 7 AN (the owner, 2026-11-06): the fullness bar: stamina and mana both at this share or she never enters and never pulls: no refusal, no aside lets a pull through, an attacker alone is fought (M5 D88). 0: no bar.</param>
/// <param name="Potions"><c>fight.pull_rest_potions</c> 1: M6 8 (the owner's decision of 2026-11-11: the autopilot sprints, and coin is no object before Mana Conversion at level 32): while Mana Potions are held, mana under the rest's target is drunk, not rested for (<c>FightScenario.PullRest.cs</c>, <c>m6-potion-before-rest</c>). Absent: the rest as before.</param>
/// <param name="PotionManaPercent"><c>fight.pull_potion_mana_percent</c>: M6 8 (the owner's rules of 2026-11-11): while Mana Potions are in use the mana's line: under it a potion is drunk, at it or over none is and nothing waits for mana (0: the potions drink to the rest's own target).</param>
/// <param name="PotionStaminaPercent"><c>fight.pull_potion_stamina_percent</c>: M6 8: while Mana Potions are in use the stamina's floor before a pull, in the bar's place: stamina that sits full regenerates nothing, so Stamina to Mana is pressed and she pulls on what it leaves.</param>
/// <param name="ToFloors">Set by <see cref="WithPotionsInUse"/> only: mana and stamina rest to their floors, not to the until share.</param>
public sealed record GroundPullRest(int ManaPercent, int HealthPercent, int UntilPercent, RestPosture Posture, double AggroMetres, int StaminaPercent = 0, int FullPercent = 0, bool Potions = false, int PotionManaPercent = 0, int PotionStaminaPercent = 0, bool ToFloors = false)
{
    /// <summary>
    /// M6 8 (the owner's rules of 2026-11-11): the rule as it stands while Mana Potions are held and in use. She does not need full mana: the mana's floor is <see cref="PotionManaPercent"/> and nothing rests past it; the fullness bar
    /// does not stand (it would send her to rest for the stamina a Stamina to Mana press spent), and the stamina's floor is <see cref="PotionStaminaPercent"/>. With none held, the potions set aside, or no line written, the rule itself.
    /// </summary>
    /// <param name="inUse">A Mana Potion is held and the potions are not set aside.</param>
    public GroundPullRest WithPotionsInUse(bool inUse) =>
        Potions && inUse && PotionManaPercent > 0 ? this with { FullPercent = 0, ManaPercent = PotionManaPercent, StaminaPercent = PotionStaminaPercent, ToFloors = true } : this;
}

/// <summary>What <see cref="PullRestRule.Decide"/> reads.</summary>
/// <param name="Mana">The mana now.</param>
/// <param name="ManaMax">The mana's maximum.</param>
/// <param name="Health">The health now.</param>
/// <param name="HealthMax">The health's maximum (vitae-reduced, as the server sends it).</param>
/// <param name="Rule">The ground's rule.</param>
/// <param name="ManaPerSecond">The mana a second: the fight's last measured rest, else <see cref="PullRestRule.SeedManaPerSecond"/>.</param>
/// <param name="Stamina">M6 7 AN: the stamina now.</param>
/// <param name="StaminaMax">M6 7 AN: the stamina's maximum.</param>
public readonly record struct PullRestInput(uint Mana, uint ManaMax, uint Health, uint HealthMax, GroundPullRest Rule, double ManaPerSecond, uint Stamina = 0, uint StaminaMax = 0);

/// <summary>What <see cref="PullRestRule.Decide"/> answers.</summary>
/// <param name="Wanted">True when a vital is under its floor.</param>
/// <param name="ManaTarget">The mana the rest goes on to (never under the mana now).</param>
/// <param name="HealthTarget">The health the rest goes on to (never under the health now).</param>
/// <param name="BoundSeconds">The longest this rest lasts: the regeneration's time for what is short, with <see cref="PullRestRule.BoundFactor"/> and <see cref="PullRestRule.BoundMarginSeconds"/>, at most <see cref="PullRestRule.CapSeconds"/>.</param>
/// <param name="Why">The vital under its floor, for the line (<c>mana 57/133 under 60%</c>).</param>
/// <param name="StaminaTarget">M6 7 AN: the stamina the rest goes on to (0: stamina does not hold it).</param>
public readonly record struct PullRestPlan(bool Wanted, uint ManaTarget, uint HealthTarget, double BoundSeconds, string Why, uint StaminaTarget = 0)
{
    /// <summary>
    /// M6 8 (live 2026-11-11 14:59 UTC): the mana target as it stands now, never above the maximum she has now. A buff that lapses while she rests or drinks lowers the maximum under a target planned before it
    /// (242 planned, 217 after): the old target can never be met, so a potion was drunk at the maximum, restored nothing, and the potions stood aside for the fight.
    /// </summary>
    /// <param name="manaMax">The maximum mana read now (0: not read, the plan's target stands).</param>
    public uint ManaTargetNow(uint manaMax) => manaMax == 0 ? ManaTarget : Math.Min(ManaTarget, manaMax);
}

/// <summary>What <see cref="PullRestRule.PressLevel"/> reads.</summary>
/// <param name="Mana">The mana now.</param>
/// <param name="ManaMax">The mana's maximum.</param>
/// <param name="ManaTarget">The mana the rest goes on to.</param>
/// <param name="Stamina">The stamina now.</param>
/// <param name="StaminaMax">The stamina's maximum.</param>
/// <param name="ManaConversion">The caster's Mana Conversion (0: untrained).</param>
/// <param name="TryChance">The least landing chance of a press (<see cref="ManaOrderOptions.TryChance"/>).</param>
public readonly record struct PullRestPressInput(uint Mana, uint ManaMax, uint ManaTarget, uint Stamina, uint StaminaMax, uint ManaConversion, double TryChance);

/// <summary>
/// M6 7 AB (r3d, level 10 in the Burrow: a kill cost 3 casts, the next tusker was walked to at 57 of 133 mana, the heals ran it dry and she died with the tusker at 91%): the rule that rests between pulls. Pure.
/// <para><b>Regeneration</b> (ACE @95ef426d <c>WorldObjects/Creature_Vitals.cs:102-150</c>: a vital gains rate x attribute x stance x enchantment each 5 s heartbeat; the human weenie's rows are HealthRate 0.3, StaminaRate 3, ManaRate 1). <c>GetStanceMod</c>
/// (<c>Creature_Vitals.cs:210-236</c>) returns 1 for mana at once: no posture and no combat mode moves mana. Health and stamina: combat mode or running 0.5, standing in peace 1, sitting 2.5, lying 3. So mana stands at 0.2 a second, 0.25 under Mana Renewal
/// Self II (1.25; live: 56 mana in 226 s, 53 in 213 s), and health sitting at 0.15 a second against 0.03 in the magic mode. The price of a posture is <c>Player_Combat.cs:261</c> <c>GetDefenseStanceMod</c>: sitting 0.3, lying 0.2 of the defence skill.</para>
/// <para><b>The dance</b> (M6 7 AD: it needs no Mana Conversion, <c>Creature_Magic.cs:41-44</c>): mana takes no stance factor but stamina does, 0.6 a second at the stance factor 1, so sitting (1.5 a second) feeds Stamina to Mana Self (<see cref="PressLevel"/>): at level 10,
/// stamina 50, each press nets +12.5.</para>
/// </summary>
public static class PullRestRule
{
    /// <summary>The mana a second before a rest was measured: ManaRate 1 a 5 s heartbeat x Mana Renewal Self II 1.25 (the climber's line at these rungs); live 0.25 (r3d 09:54:54 to 09:58:40, r3 08:15:39 to 08:19:12).</summary>
    public const double SeedManaPerSecond = 0.25;

    /// <summary>The health a second at the stance factor 1: HealthRate 0.3 a 5 s heartbeat (the human weenie's row; the attribute factor is 1 at Strength + 2 x Endurance up to 200).</summary>
    public const double HealthPerSecondAtOne = 0.06;

    /// <summary>M6 7 AN: the stamina a second at the stance factor 1: StaminaRate 3 a 5 s heartbeat (the human weenie's row, <c>Creature_Vitals.cs:102-150</c> @95ef426d): lying x3 is 1.8 a second, x1.4 Rejuvenation III 2.5.</summary>
    public const double StaminaPerSecondAtOne = 0.6;

    /// <summary>The bound's factor on the regeneration's time (Lifestone's own: a tick's rounding and a missed heartbeat fit in it).</summary>
    public const double BoundFactor = 1.5;

    /// <summary>The bound's margin, in seconds (Lifestone's own: six heartbeats).</summary>
    public const double BoundMarginSeconds = 30;

    /// <summary>The longest one rest lasts, in seconds (as <see cref="VitalsOptions.DefaultRestCapSeconds"/>). It stays under the autopilot's fight watchdog: a ground's longest respawn plus 600 s without a kill.</summary>
    public const double CapSeconds = VitalsOptions.DefaultRestCapSeconds;

    /// <summary>
    /// M6 7 AD: the stamina share at which a rest presses (<see cref="ManaOrder.PreemptStaminaShare"/>, the same line). A press is worth most from a full bar: from S it nets 0.45 S - 10 and leaves S / 2, so a second press from half nets about 1: one press a refill. 0.9 and not 1:
    /// stamina comes in 5 s ticks (7.5 sitting, StaminaRate 3 x 2.5), and the last tick to the top of a 50 bar buys 1 net mana for 5 s.
    /// </summary>
    public const double PressStaminaShare = ManaOrder.PreemptStaminaShare;

    /// <summary>
    /// M6 7 AD: the level a rest presses now (<see cref="ManaOrder.BestNetLevel"/>), or -1: the mana short of the rest's top, the stamina at <see cref="PressStaminaShare"/>, the press able to land at the mana held, and the mana it adds (the give less the cost, cut at what is missing) over
    /// 0 (M6 7 C: a press that nets no mana is not sent).
    /// </summary>
    /// <param name="levels">The Stamina to Mana Self levels she may cast, lowest first.</param>
    /// <param name="input">The vitals.</param>
    /// <param name="net">The press's net, 0 at -1.</param>
    public static int PressLevel(ReadOnlySpan<DanceLevel> levels, in PullRestPressInput input, out double net)
    {
        net = 0;
        if (levels.IsEmpty || input.Mana >= input.ManaTarget || input.StaminaMax == 0 || input.Stamina < PressStaminaShare * input.StaminaMax)
        {
            return -1;
        }

        var level = ManaOrder.BestNetLevel(levels, input.Stamina, input.Mana, input.ManaConversion, input.TryChance, out var yield);
        yield = Math.Min(yield, input.ManaMax > input.Mana ? input.ManaMax - input.Mana : 0);
        if (level < 0 || yield <= 0)
        {
            return -1;
        }

        net = yield;
        return level;
    }

    /// <summary>M6 8 (the owner, 2026-11-11): true when a potion of <paramref name="boost"/> fits whole in what the mana lacks: one that would overflow is not drunk.</summary>
    /// <param name="mana">The mana now.</param>
    /// <param name="manaMax">The mana's maximum.</param>
    /// <param name="boost">What the potion restores.</param>
    public static bool PotionFits(uint mana, uint manaMax, uint boost) => manaMax >= mana && manaMax - mana >= boost;

    /// <summary>M6 7 AN: true when the ground's fullness bar stands and <paramref name="mana"/> or <paramref name="stamina"/> is under it: she does not enter, she does not pull.</summary>
    /// <param name="rule">The ground's rule, or null.</param>
    /// <param name="mana">The mana now.</param>
    /// <param name="manaMax">The mana's maximum.</param>
    /// <param name="stamina">The stamina now.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    public static bool UnderFullBar(GroundPullRest? rule, uint mana, uint manaMax, uint stamina, uint staminaMax) =>
        rule is { FullPercent: > 0 } r && ((manaMax > 0 && mana * 100.0 < r.FullPercent * (double)manaMax) || (staminaMax > 0 && stamina * 100.0 < r.FullPercent * (double)staminaMax));

    /// <summary>The stance factor of <paramref name="posture"/> on health, in peace (ACE <c>GetStanceMod</c>).</summary>
    /// <param name="posture">The rest's posture.</param>
    public static double HealthStance(RestPosture posture) => posture switch
    {
        RestPosture.Sit => 2.5,
        RestPosture.Lie => 3.0,
        _ => 1.0,
    };

    /// <summary>The rule: under a floor, a rest to the until share of both vitals, bounded by the time the regeneration needs.</summary>
    /// <param name="input">The vitals, the ground's rule and the rate.</param>
    public static PullRestPlan Decide(in PullRestInput input)
    {
        var rule = input.Rule;
        var manaFloor = rule.FullPercent > 0 ? rule.FullPercent : rule.ManaPercent;
        var staminaFloor = rule.FullPercent > 0 ? rule.FullPercent : rule.StaminaPercent;
        var until = Math.Max(rule.UntilPercent, rule.FullPercent);
        var manaShort = manaFloor > 0 && input.ManaMax > 0 && input.Mana * 100.0 < manaFloor * (double)input.ManaMax;
        var staminaShort = staminaFloor > 0 && input.StaminaMax > 0 && input.Stamina * 100.0 < staminaFloor * (double)input.StaminaMax;
        var healthShort = rule.HealthPercent > 0 && input.HealthMax > 0 && input.Health * 100.0 < rule.HealthPercent * (double)input.HealthMax;
        if (!manaShort && !healthShort && !staminaShort)
        {
            return new PullRestPlan(false, input.Mana, input.Health, 0, string.Empty, input.Stamina);
        }

        var manaUntil = rule.ToFloors ? manaFloor : until; // M6 8: with potions in use the mana and the stamina go to their floors and no further
        var staminaUntil = rule.ToFloors ? staminaFloor : until;
        var manaTarget = manaFloor == 0 ? input.Mana : Math.Max(input.Mana, (uint)Math.Ceiling(input.ManaMax * (double)manaUntil / 100)); // a ground that rests for health alone waits for no mana
        var staminaTarget = staminaFloor == 0 ? input.Stamina : Math.Max(input.Stamina, (uint)Math.Ceiling(input.StaminaMax * (double)staminaUntil / 100));
        var healthTarget = rule.HealthPercent == 0 ? input.Health : Math.Max(input.Health, (uint)Math.Ceiling(input.HealthMax * (double)rule.UntilPercent / 100));
        var rate = input.ManaPerSecond > 0 ? input.ManaPerSecond : SeedManaPerSecond;
        var manaSeconds = (manaTarget - input.Mana) / rate;
        var healthSeconds = (healthTarget - input.Health) / (HealthPerSecondAtOne * HealthStance(rule.Posture));
        var staminaSeconds = (staminaTarget - input.Stamina) / (StaminaPerSecondAtOne * HealthStance(rule.Posture));
        var bound = Math.Min(CapSeconds, (Math.Max(Math.Max(manaSeconds, healthSeconds), staminaSeconds) * BoundFactor) + BoundMarginSeconds);
        var mana = string.Create(CultureInfo.InvariantCulture, $"mana {input.Mana}/{input.ManaMax} under {manaFloor}%");
        var health = string.Create(CultureInfo.InvariantCulture, $"health {input.Health}/{input.HealthMax} under {rule.HealthPercent}%");
        var stamina = string.Create(CultureInfo.InvariantCulture, $"stamina {input.Stamina}/{input.StaminaMax} under {staminaFloor}%");
        var why = string.Join(", ", new[] { manaShort ? mana : null, healthShort ? health : null, staminaShort ? stamina : null }.Where(s => s is not null));
        return new PullRestPlan(true, manaTarget, healthTarget, bound, why, staminaTarget);
    }
}

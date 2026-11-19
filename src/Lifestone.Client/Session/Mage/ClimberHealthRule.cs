using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>What the ladder asks of a level: the base health that takes <see cref="Hits"/> of the rung's hardest hit, and the share of the experience earned the floor may take.</summary>
/// <param name="Rung">The rung's key.</param>
/// <param name="Want">The base health wanted: <see cref="Hit"/> x <see cref="Hits"/>, rounded up.</param>
/// <param name="Hit">The hardest hit (<see cref="LadderHitter.Hit"/>).</param>
/// <param name="Hits">The hits of the whole pack in the window: <see cref="ClimberHealthRule.PackHits"/> (the ladder's <see cref="Ladder.HealthHits"/> when one attacker).</param>
/// <param name="Hitter">The creature that hits it.</param>
/// <param name="Road">True when the hardest hitter is the road's (the rung's island run), not the ground's.</param>
/// <param name="PayPercent">The ladder's <see cref="Ladder.HealthPayPercent"/>.</param>
/// <param name="Pack">The pack counted (<see cref="LadderHealth.Pack"/>; 1 for a road hitter, met alone).</param>
public sealed record HealthNeed(string Rung, int Want, int Hit, double Hits, string Hitter, bool Road, int PayPercent, int Pack = 1);

/// <summary>What the floor buys: the target ranks of Max Health and Endurance, the health they reach, the experience they cost, and the last comparison made.</summary>
/// <param name="HealthRanks">The Max Health ranks wanted (at or above the character's).</param>
/// <param name="EnduranceRanks">The Endurance ranks wanted (at or above the character's).</param>
/// <param name="Health">The base health those ranks reach.</param>
/// <param name="Xp">Their cumulative price (the XpTable's vital list at <see cref="HealthRanks"/> plus its attribute list at <see cref="EnduranceRanks"/>).</param>
/// <param name="Buyer">The stat the last comparison chose (<c>Max Health</c>, <c>Endurance</c>), or <c>none</c> when nothing was bought.</param>
/// <param name="HealthPrice">The price of one health by Max Health at the last comparison.</param>
/// <param name="EndurancePrice">The price of one health by Endurance at the last comparison.</param>
/// <param name="Limited">True when the pay limit (not the want) ended the buying.</param>
public sealed record HealthBuy(int HealthRanks, int EnduranceRanks, int Health, ulong Xp, string Buyer, ulong HealthPrice, ulong EndurancePrice, bool Limited)
{
    /// <summary>No floor.</summary>
    public static HealthBuy None { get; } = new(0, 0, 0, 0, "none", 0, 0, false);
}

/// <summary>
/// M6 7 R (the owner's correction, 2026-11-05: the climber dies easily because her experience spend neglects health): the health the climber buys before the shares, up to what the rung needs. Plan data, on the model of <see cref="StrengthFloor"/>: the rung's
/// need is <see cref="Ladder.HealthHits"/> of its hardest hit (<see cref="LadderHitter.Hit"/>: ACE's mean uncritical unarmoured hit of the ground's creature, or of the road's on a rung whose route runs the island), and each purchase goes to whichever of
/// Max Health and Endurance prices one health cheaper at that moment (the dats' XpTable), so the split is the rule's, never a weight. The floor takes at most <see cref="Ladder.HealthPayPercent"/> of the experience earned: past that the magic that kills the
/// creature is worth more than the health that outlasts it, and what is short is logged (<c>short=</c>).
/// M6 7 AY (the owner, 2026-11-06): the want counts the whole pack the rung sends (<see cref="PackHits"/>), not one attacker.
/// </summary>
/// <remarks>
/// <para><b>Health.</b> The base maximum is the vital's start and ranks plus half the base Endurance rounded up (retail's formula, <see cref="Contracts.PlayerQualities.TryGetVitalLevels"/>; ACE <c>AttributeFormula.GetFormula</c> @95ef426d
/// <c>Entity/AttributeFormula.cs:54-70</c>). Base, no buff, because a buff lapses, as <see cref="StrengthFloor"/>: Endurance's ranks at 10 give a health on each odd value. A rank of Endurance also gives a point of stamina; that is not priced.</para>
/// <para><b>Whom it binds.</b> Only the ladder's character (<see cref="MageTemplate.ClimberName"/>, <see cref="XpSpendRule"/>). <c>BSD Mage</c>, <see cref="MageTemplate.Targets"/> and M5's pins do not move: no plan they read carries a floor.</para>
/// </remarks>
public static class ClimberHealthRule
{
    /// <summary>The ladder's need at <paramref name="level"/>, or null on a rung with no <see cref="LadderRung.Health"/> (a quest rung, the Holding) or past the ladder's top.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    public static HealthNeed? NeedAt(Ladder ladder, int level)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        return level < 1 || level > ladder.TopLevel ? null : NeedFor(ladder, ladder.RungFor(level));
    }

    /// <summary>
    /// M6 audit (the owner's decision of 2026-11-19): the need of the rung she hunts, <paramref name="rung"/>, or null when it has no <see cref="LadderRung.Health"/>. The level's rung and the hunted one differ under a hold: from
    /// 65 the sprint holds the Holding, so she hunts the Quarters (cache/m6/14/live: "read_at=64 ... rung=quarters ... reason=approach-session:holding"), whose want is 204, while the level's rung, the Holding, has none on purpose.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="rung">The rung.</param>
    public static HealthNeed? NeedFor(Ladder ladder, LadderRung rung)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(rung);
        if (rung.Health is not { } health)
        {
            return null;
        }

        var hits = PackHits(ladder.HealthHits, health.Pack);
        var road = health.RoadHitter is { } r && r.Hit * ladder.HealthHits > health.Hitter.Hit * hits;
        var hardest = road ? health.RoadHitter! : health.Hitter;
        var window = road ? ladder.HealthHits : hits;
        return new HealthNeed(rung.Key, (int)Math.Ceiling(hardest.Hit * window), hardest.Hit, window, hardest.Name, road, ladder.HealthPayPercent, road ? 1 : health.Pack);
    }

    /// <summary>
    /// M6 7 AY, the pack formula: the hits that land in the window of <paramref name="hits"/> hits of one attacker, whole pack counted: <c>N + (pack - 1) x (N - 1)</c>. N hits of one attacker span N - 1 of its swing gaps, and each other attacker lands N - 1 hits in that time (their swings
    /// are not in step: the Lodge's gaps measured 2.1 to 3.4 s, ACE's swing interval is the animation plus 0 to 2.5 s, @95ef426d <c>WorldObjects/Monster_Melee.cs:152-156</c>). So a pack of 1 is the rule of before, and the want is the hit x this: at N = 2 the hardest hitter's two hits and one more from each other attacker.
    /// </summary>
    /// <param name="hits">N (<see cref="Ladder.HealthHits"/>).</param>
    /// <param name="pack">The attackers.</param>
    public static double PackHits(double hits, int pack) => hits + ((Math.Max(1, pack) - 1) * (hits - 1));

    /// <summary>The base maximum health of <paramref name="endurance"/> (its start and ranks) and <paramref name="healthRanks"/> Max Health ranks over the vital's start.</summary>
    /// <param name="endurance">The base Endurance.</param>
    /// <param name="healthRanks">The vital's start and ranks.</param>
    public static int BaseHealth(uint endurance, int healthRanks) => (int)((endurance + 1) / 2) + healthRanks;

    /// <summary>
    /// The ranks that bring the base health to <paramref name="want"/> from the character's ranks, one health at a time, each by whichever of Max Health and Endurance prices it cheaper now (a tie to Max Health, which is all health), while each stat's own cumulative price
    /// stays under <paramref name="budget"/>; a stat at its rank cap leaves the other. The path is the same however far it runs, so a later rung's want extends it.
    /// </summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="want">The base health wanted.</param>
    /// <param name="enduranceStart">The attribute's creation value.</param>
    /// <param name="enduranceRanks">The Endurance ranks now.</param>
    /// <param name="healthRanks">The Max Health ranks now.</param>
    /// <param name="budget">The most each of the two stats may have cost (M6 7 finish, the owner: no single stat over its share).</param>
    public static HealthBuy Buy(XpTableData table, int want, uint enduranceStart, int enduranceRanks, int healthRanks, ulong budget)
    {
        ArgumentNullException.ThrowIfNull(table);
        var vital = table.VitalXpList;
        var attribute = table.AttributeXpList;
        var (h, e) = (healthRanks, enduranceRanks);
        var buyer = "none";
        ulong healthPrice = 0, endurancePrice = 0;
        var limited = false;
        while (BaseHealth(enduranceStart + (uint)e, h) < want)
        {
            var value = enduranceStart + (uint)e;
            var step = value % 2 == 0 ? 1 : 2; // ceil(E / 2) rises on the odd value
            var healthOpen = h + 1 < vital.Count;
            var enduranceOpen = e + step < attribute.Count;
            if (!healthOpen && !enduranceOpen)
            {
                break;
            }

            var healthFits = healthOpen && vital[h + 1] <= budget; // M6 7 finish: the limit is each stat's own
            var enduranceFits = enduranceOpen && attribute[e + step] <= budget;
            healthPrice = healthFits ? (ulong)vital[h + 1] - vital[h] : ulong.MaxValue;
            endurancePrice = enduranceFits ? (ulong)attribute[e + step] - attribute[e] : ulong.MaxValue;
            if (!healthFits && !enduranceFits)
            {
                limited = true;
                break;
            }

            var byHealth = healthPrice <= endurancePrice;

            buyer = byHealth ? "Max Health" : "Endurance";
            if (byHealth)
            {
                h++;
            }
            else
            {
                e += step;
            }
        }

        return new HealthBuy(h, e, BaseHealth(enduranceStart + (uint)e, h), (ulong)vital[h] + attribute[e], buyer, healthPrice, endurancePrice, limited);
    }

    /// <summary>The most the floor may have cost at <paramref name="totalXp"/> (<see cref="HealthNeed.PayPercent"/> of the experience earned).</summary>
    /// <param name="totalXp">The experience earned.</param>
    /// <param name="payPercent">The ladder's limit.</param>
    public static ulong Budget(ulong totalXp, int payPercent) => (totalXp / 100 * (ulong)payPercent) + (totalXp % 100 * (ulong)payPercent / 100);

    /// <summary>
    /// The floor of a climber who bought it at each level on her way to <paramref name="level"/> (<see cref="SpellPlan.ProjectedWarBase"/>'s input, and the table of the slice's notes): a rung's need bought as far as the pay limit goes at each level, what was
    /// bought kept (ranks are never refunded, so a lower need on a later rung does not undo the road's floor).
    /// </summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The level.</param>
    public static HealthBuy ProjectedBuy(XpTableData table, Ladder ladder, int level)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(ladder);
        var buy = HealthBuy.None;
        for (var l = 1; l <= Math.Min(level, ladder.TopLevel); l++)
        {
            if (NeedAt(ladder, l) is { } need)
            {
                buy = Buy(table, need.Want, MageTemplate.LowAttribute, buy.EnduranceRanks, buy.HealthRanks, ladder.StatCap?.Aim(table.XpForLevel(l)) ?? Budget(table.XpForLevel(l), need.PayPercent));
            }
        }

        return buy;
    }

    /// <summary>The <c>xp: health:</c> line of one spend (<see cref="XpSpendRule"/>): the base health now, the want, the hit, N, the rung, the buyer and the prices compared.</summary>
    /// <param name="need">The need.</param>
    /// <param name="now">The base health now.</param>
    /// <param name="buy">What the floor buys.</param>
    public static string Line(HealthNeed need, int now, HealthBuy buy)
    {
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(buy);
        var c = CultureInfo.InvariantCulture;
        var who = need.Hitter.Replace(" ", "-", StringComparison.Ordinal);
        var verdict = buy.Health > now ? string.Create(c, $"the floor raises health {now} -> {buy.Health} before the shares") : now >= need.Want ? "held: no floor rank to buy" : "the pay limit leaves no floor rank to buy";
        return string.Create(
            c,
            $"health: now={now} want={need.Want} reach={Math.Max(now, buy.Health)} short={Math.Max(0, need.Want - Math.Max(now, buy.Health))} why={need.Hits:0.##}x{need.Hit}({who}{(need.Road ? ",road" : string.Empty)}{(need.Pack > 1 ? string.Create(c, $",pack={need.Pack}") : string.Empty)}) rung={need.Rung} max_health_ranks={buy.HealthRanks} endurance_ranks={buy.EnduranceRanks} buyer={buy.Buyer.Replace(" ", string.Empty, StringComparison.Ordinal)} price_health={Price(buy.HealthPrice)} price_endurance={Price(buy.EndurancePrice)} pay_limit={need.PayPercent}%{(buy.Limited ? " limited=yes" : string.Empty)}: {verdict}");

        static string Price(ulong price) => price == ulong.MaxValue ? "capped" : price.ToString(CultureInfo.InvariantCulture);
    }
}

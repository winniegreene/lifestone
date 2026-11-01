using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Session.Mage;

/// <summary>The kind of stat a plan target names (the keys of <c>PlayerQualities</c> are per kind: Self is attribute 6, Melee Defense is skill 6).</summary>
public enum MageStatKind
{
    /// <summary>A primary attribute (1 Strength ... 6 Self); the XpTable's attribute list.</summary>
    Attribute,

    /// <summary>A vital by its maximum's id (1 Max Health, 3 Max Stamina, 5 Max Mana); the XpTable's vital list.</summary>
    Vital,

    /// <summary>A skill by id; the XpTable's trained or specialised list.</summary>
    Skill,
}

/// <summary>One stat of the spending plan: the ranks to buy with experience.</summary>
/// <param name="Kind">Attribute, vital or skill.</param>
/// <param name="Id">The stat's id (see <see cref="MageStatKind"/>).</param>
/// <param name="Name">The stat's name.</param>
/// <param name="Specialized">For a skill: true when the skill is specialised (the specialised list prices its ranks), false when trained.</param>
/// <param name="Ranks">The target ranks above the creation value (ACE counts a stat's ranks from rank 0 at creation, whatever the start value).</param>
public sealed record StatTarget(MageStatKind Kind, uint Id, string Name, bool Specialized, int Ranks)
{
    /// <summary>The cumulative XpTable list that prices this stat's ranks.</summary>
    public IReadOnlyList<uint> XpList(XpTableData table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Kind switch
        {
            MageStatKind.Attribute => table.AttributeXpList,
            MageStatKind.Vital => table.VitalXpList,
            _ => Specialized ? table.SpecializedSkillXpList : table.TrainedSkillXpList,
        };
    }

    /// <summary>The most ranks the stat can have (ACE: <c>IsMaxRank</c> is <c>Ranks &gt;= list.Count - 1</c>).</summary>
    public int MaxRanks(XpTableData table) => XpList(table).Count - 1;

    /// <summary>
    /// The experience to go from <paramref name="currentRanks"/> to the target: <c>list[target] - list[current]</c>
    /// (<see cref="XpTableData.XpToRaise"/>); 0 when the stat is at or above the target. ACE derives a rank from the
    /// experience spent, so this is exact when the spent experience sits on a rank boundary, as a plan's raises leave it.
    /// </summary>
    public ulong XpToReach(XpTableData table, int currentRanks) =>
        currentRanks >= Ranks ? 0 : XpTableData.XpToRaise(XpList(table), currentRanks, Ranks);

    /// <summary>The experience still to spend given the stat's experience already spent (the private update's value): <c>list[target] - spent</c>, 0 when reached.</summary>
    public ulong XpRemaining(XpTableData table, uint experienceSpent)
    {
        var goal = XpList(table)[Ranks];
        return experienceSpent >= goal ? 0 : goal - experienceSpent;
    }
}

/// <summary>
/// M5 7 F3 (D87): the shares of a spending plan as data, named. <see cref="D19"/> is the owner's split of 2026-10-15 (level 126: 50/30/5/5 to magic, the rest in equal shares); <see cref="Dance"/> is the level-80 soak mage's, for a mage that
/// fights by the stamina-to-mana dance (Stamina to Mana Self moves 0.875 of the current stamina to mana at VII, so a stamina point is worth far more than a mana point, and a stamina point is also far cheaper in experience).
/// </summary>
/// <param name="Name">The set's name, logged with the plan.</param>
/// <param name="MagicPercents">The percent of the total for the four magic skills, Focus and Self, Mana Conversion and Max Mana, in that order.</param>
/// <param name="PoolWeights">The weight of a stat of the rest-of-the-stats pool by (kind, id); a stat not listed has weight 1. The pool is what the magic percents leave, split by weight. D126 (b): weight 0 leaves the stat out of the plan (never raised).</param>
/// <param name="RunCap">D126 (b): when set, the Run skill is raised only while its next rank pays for itself within the payback's horizon (<see cref="RunPayback.CapRanks"/>); null: no cap.</param>
public sealed record MageShares(string Name, IReadOnlyList<int> MagicPercents, IReadOnlyDictionary<(MageStatKind Kind, uint Id), int> PoolWeights, RunPayback? RunCap = null)
{
    /// <summary>The owner's D19 split (level 126): 50, 30, 5, 5 and the remaining 10% in equal shares.</summary>
    public static MageShares D19 { get; } = new("d19", [50, 30, 5, 5], new Dictionary<(MageStatKind, uint), int>());

    /// <summary>
    /// The soak mage's dance split (M5 7 F3, D87): 45, 27, 5, 5 to magic and the other 18% by weight, Endurance and Max Stamina four shares each. Mana Conversion keeps its 5% and Max Mana its 5%.
    /// <para>D126 (b), checked against ACE (docs/session.md section 56): Loyalty and Salvaging are left out (weight 0): Loyalty is read only by the allegiance's pass-up (<c>AllegianceManager.cs:265</c>, through
    /// <c>Creature_Skills.cs:72</c>), and a mage sworn to no one passes nothing up; Salvaging only by the salvage operation (<c>Player_Crafting.cs:220, 313-327</c>), which item 7 does not run (tinkering is later). Run is capped
    /// by its payback (<see cref="RunPayback.Soak"/>: 81 ranks), so the soak mage, at 121, raises it no more.</para>
    /// </summary>
    public static MageShares Dance { get; } = new(
        "dance",
        [45, 27, 5, 5],
        new Dictionary<(MageStatKind, uint), int>
        {
            [(MageStatKind.Attribute, MageTemplate.Endurance)] = 4,
            [(MageStatKind.Vital, MageTemplate.MaxStamina)] = 4,
            [(MageStatKind.Skill, MageTemplate.Loyalty)] = 0,
            [(MageStatKind.Skill, MageTemplate.Salvaging)] = 0,
        },
        RunPayback.Soak);

    /// <summary>The weight of a pool stat.</summary>
    public int WeightOf(MageStatKind kind, uint id) => PoolWeights.GetValueOrDefault((kind, id), 1);
}

/// <summary>
/// D126 (b): what one more Run point is worth to a mage that walks part of its hour, and the rank where it stops paying. ACE runs a player at <c>MovementSystem.GetRunRate(burden, Run, 1)</c>
/// (<c>Monster_Navigation.cs:346-368</c>, <c>MovementSystem.cs:20-28</c> at 95ef426d; the client's <see cref="MovementSystem.GetRunRate"/> is the same retail formula): <c>(loadMod × Run / (Run + 200) × 11 + 4) / 4</c>, the load
/// modifier 1 under a full burden (<c>EncumbranceSystem.cs:32-40</c>; the soak mage carries about half its capacity). One point more saves the walking time's share <c>1 - rate(S) / rate(S + 1)</c>, and the time saved is fought at the
/// hour's experience rate. A rank is bought while its price (the trained list's step) is earned back within <see cref="HorizonHours"/>.
/// </summary>
/// <param name="WalkShare">The share of an hour spent walking (the runner's legs and ACE's own MoveTo to a corpse).</param>
/// <param name="XpPerHour">The experience an hour of the soak earns.</param>
/// <param name="HorizonHours">The hours a rank must pay for itself in.</param>
/// <param name="SkillBeforeRanks">The current Run skill less its ranks (the Quickness part and the Quickness buff).</param>
public sealed record RunPayback(double WalkShare, double XpPerHour, double HorizonHours, int SkillBeforeRanks)
{
    /// <summary>
    /// Soak round 12's figures (2026-10-29, 06:53 to 11:28 UTC, 4.57 hours): the runner walked 3,888 s over 274 runs and ACE's MoveTo to a corpse took at most 608 s over 677 opens (an upper bound: from the Use to the open), 27% of the
    /// hours; the kills earned 7,053,567 in 73.6 minutes (cycle 3) and 5,695,297 in 59.7 minutes (cycle 4), about 5.7 M an hour; the horizon is one soak, the 12-hour window of D85; the base Run was 238 at 121 ranks ("Your base Run
    /// skill is now 238!"), so 117 before the ranks, plus Quickness Self VI's 35 (Run is Quickness alone).
    /// </summary>
    public static RunPayback Soak { get; } = new(0.27, 5_700_000, 12, 152);

    /// <summary>The experience an hour that one more point of Run brings at <paramref name="skill"/>.</summary>
    /// <param name="skill">The current Run skill.</param>
    public double XpPerHourOfAPoint(int skill) => WalkShare * XpPerHour * (1 - (MovementSystem.GetRunRate(0f, skill, 1f) / MovementSystem.GetRunRate(0f, skill + 1, 1f)));

    /// <summary>The ranks of Run worth buying: the first rank whose next step costs more than <see cref="HorizonHours"/> of what its point brings (a trained skill's list; Run is trained at creation).</summary>
    /// <param name="table">The XpTable.</param>
    public int CapRanks(XpTableData table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var list = table.TrainedSkillXpList;
        for (var r = 0; r + 1 < list.Count; r++)
        {
            var cost = (double)list[r + 1] - list[r];
            if (cost > XpPerHourOfAPoint(SkillBeforeRanks + r) * HorizonHours)
            {
                return r;
            }
        }

        return list.Count - 1;
    }
}

/// <summary>The result of one planning run.</summary>
/// <param name="Targets">The stats with their target ranks, in plan order.</param>
/// <param name="TotalXp">The experience that was split.</param>
/// <param name="Spent">The experience the targets cost.</param>
/// <param name="Remainder"><c>TotalXp - Spent</c>: what could not be spent.</param>
public sealed record SpendingPlan(IReadOnlyList<StatTarget> Targets, ulong TotalXp, ulong Spent, ulong Remainder);


/// <summary>
/// The rule that turns the experience of the mage's level into fixed target ranks (<see cref="MageTemplate"/> hard-codes the
/// result; <c>MageTemplateDatTests</c> recomputes it from the XpTable). It is the owner's spending split, decision D19
/// (2026-10-15): spread the experience around, because the character cannot survive on magic alone.
/// </summary>
/// <remarks>
/// <para><b>The split.</b> 90% goes to magic and 10% to everything else.
/// Magic: 50% to the four magic skills (War Magic specialised; Life Magic, Creature Enchantment and Item Enchantment trained),
/// 30% to Focus and Self, 5% to Mana Conversion (trained), 5% to Max Mana. Everything else: the remaining 10% (plus the rounding of
/// the percentages) in EQUAL experience shares, one per stat, in this order: the attributes Strength, Endurance, Coordination and
/// Quickness, the vitals Max Health and Max Stamina, then every other skill the mage has trained or specialised (Melee Defense, the
/// skills the heritage and <c>HeadlessCharacter.RetailSkillClasses</c> train at creation, then the skills trained later with
/// the spare credits, which are none now: Healing was dropped by the owner, 2026-10-15; see <see cref="MageTemplate.CreationTrained"/> and <see cref="ChooseSurvivalSkills"/>).</para>
/// <para><b>Buying.</b> A share with several stats (the four magic skills, Focus and Self) buys ranks cheapest next rank first
/// (a tie goes to the stat listed first), so equal stats stay level; a share with one stat buys its ranks until the next rank does
/// not fit. A stat never goes past its list (ACE's rank cap, <c>IsMaxRank</c>: list length - 1).</para>
/// <para><b>What this mage gains from (D126 b).</b> A share set may leave a pool stat out (weight 0: <see cref="MageShares.Dance"/> leaves out Loyalty and Salvaging) and cap Run by its payback (<see cref="RunPayback"/>); the capped stat
/// goes first in the pool so what it cannot spend rolls on. The D19 split (level 126) is unchanged.</para>
/// <para><b>Leftovers.</b> What a share cannot spend (a rank cap, or a next rank that does not fit) rolls into the following
/// shares in plan order, magic first and then the 10% pool, so the final remainder, which is recorded, is as small as the rank
/// granularity of the last stat allows. The percentages are floored; the pool takes what is left, and its integer-division remainder
/// goes to its last share.</para>
/// </remarks>
public static class MageSpendingPlanner
{
    /// <summary>The percentages of the magic shares: the four magic skills, Focus and Self, Mana Conversion, Max Mana (the rest is the pool).</summary>

    /// <summary>One share: the experience it starts with and the stats it buys.</summary>
    private sealed record Share(ulong Xp, StatTarget[] Stats);

    /// <summary>
    /// The skills to train with the spare credits (D19): the <paramref name="priority"/> list in order, each only if it is not already
    /// trained and its trained cost still fits what is left of <paramref name="creditsLeft"/>.
    /// </summary>
    /// <param name="priority">The candidates, most important first (<see cref="MageTemplate.SurvivalPriority"/>).</param>
    /// <param name="alreadyTrained">The skills already trained or specialised, which are skipped.</param>
    /// <param name="trainedCost">The heritage's trained cost of a skill (<c>CharGenData.GetSkillTrainedCost</c>).</param>
    /// <param name="creditsLeft">The credits left after the magic skills are trained.</param>
    public static IReadOnlyList<uint> ChooseSurvivalSkills(IReadOnlyList<uint> priority, IReadOnlyCollection<uint> alreadyTrained, Func<uint, int> trainedCost, int creditsLeft)
    {
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(alreadyTrained);
        ArgumentNullException.ThrowIfNull(trainedCost);
        var chosen = new List<uint>();
        foreach (var skill in priority)
        {
            var cost = trainedCost(skill);
            if (alreadyTrained.Contains(skill) || cost < 0 || cost > creditsLeft)
            {
                continue;
            }

            creditsLeft -= cost;
            chosen.Add(skill);
        }

        return chosen;
    }

    /// <summary>Runs the rule over <paramref name="table"/> with <paramref name="totalXp"/> experience to spend.</summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="totalXp">The experience to split.</param>
    /// <param name="untrained">M5 7 F3 (D87): skills the credits of a lower level could not train (<see cref="MageTemplate.PlanAt"/>); they get no ranks and their share rolls on to the next. Null or empty: every skill of the plan is bought (level 126).</param>
    /// <param name="shares">The shares (<see cref="MageShares"/>); null: <see cref="MageShares.D19"/>.</param>
    /// <param name="build">M5 7 F6 (D87, amended): the mage planned. <see cref="MageBuild.Gate126"/> (the default): War Magic and Melee Defense specialised. <see cref="MageBuild.Traditional"/>: no Melee Defense (the pool is shared over the stats that remain by the same rule) and Life Magic specialised (its ranks priced by the specialised list), War Magic as before.</param>
    public static SpendingPlan Compute(XpTableData table, ulong totalXp, IReadOnlyCollection<uint>? untrained = null, MageShares? shares = null, MageBuild build = MageBuild.Gate126)
    {
        ArgumentNullException.ThrowIfNull(table);
        var split = shares ?? MageShares.D19;
        var left = untrained ?? [];
        var magic = new StatTarget[][]
        {
            new[]
            {
                Skill(MageTemplate.WarMagic, specialized: true),
                Skill(MageTemplate.LifeMagic, specialized: build == MageBuild.Traditional),
                Skill(MageTemplate.CreatureEnchantment, specialized: false),
                Skill(MageTemplate.ItemEnchantment, specialized: false),
            }.Where(t => !left.Contains(t.Id)).ToArray(),
            [
                new(MageStatKind.Attribute, MageTemplate.Focus, "Focus", false, 0),
                new(MageStatKind.Attribute, MageTemplate.Self, "Self", false, 0),
            ],
            left.Contains(MageTemplate.ManaConversion) ? [] : [Skill(MageTemplate.ManaConversion, specialized: false)],
            [new(MageStatKind.Vital, MageTemplate.MaxMana, "Max Mana", false, 0)],
        };

        var pool = new List<StatTarget>
        {
            new(MageStatKind.Attribute, MageTemplate.Strength, "Strength", false, 0),
            new(MageStatKind.Attribute, MageTemplate.Endurance, "Endurance", false, 0),
            new(MageStatKind.Attribute, MageTemplate.Coordination, "Coordination", false, 0),
            new(MageStatKind.Attribute, MageTemplate.Quickness, "Quickness", false, 0),
            new(MageStatKind.Vital, MageTemplate.MaxHealth, "Max Health", false, 0),
            new(MageStatKind.Vital, MageTemplate.MaxStamina, "Max Stamina", false, 0),
        };
        if (build == MageBuild.Gate126)
        {
            pool.Add(Skill(MageTemplate.MeleeDefense, specialized: true)); // the traditional mage has none: the pool shares over the stats that remain
        }

        pool.AddRange(MageTemplate.CreationTrained.Select(id => Skill(id, specialized: false)));
        pool.AddRange(MageTemplate.TrainedLater.Where(id => !MageTemplate.IsMagicPlanSkill(id)).Select(id => Skill(id, specialized: false)));
        pool.RemoveAll(p => split.WeightOf(p.Kind, p.Id) == 0); // D126 (b): a stat this mage gains nothing from is left out of the plan, so it is never raised
        var runCap = split.RunCap?.CapRanks(table);
        if (runCap is not null && pool.FindIndex(IsRun) is var run and > 0)
        {
            var target = pool[run];
            pool.RemoveAt(run);
            pool.Insert(0, target); // D126 (b): the capped stat first in the pool, so what its cap leaves rolls on to the stats after it, not into the remainder
        }

        var shareList = new List<Share>();
        ulong magicXp = 0;
        for (var g = 0; g < magic.Length; g++)
        {
            var percent = (ulong)split.MagicPercents[g];
            var xp = (totalXp / 100 * percent) + (totalXp % 100 * percent / 100);
            magicXp += xp;
            shareList.Add(new Share(xp, magic[g]));
        }

        var poolXp = totalXp - magicXp;
        var weightSum = (ulong)pool.Sum(p => split.WeightOf(p.Kind, p.Id));
        ulong given = 0;
        for (var i = 0; i < pool.Count; i++)
        {
            var one = i == pool.Count - 1 ? poolXp - given : poolXp / weightSum * (ulong)split.WeightOf(pool[i].Kind, pool[i].Id);
            given += one;
            shareList.Add(new Share(one, [pool[i]]));
        }

        var targets = new List<StatTarget>();
        ulong carried = 0;
        ulong spent = 0;
        foreach (var share in shareList)
        {
            var budget = share.Xp + carried;
            var stats = share.Stats;
            var ranks = new int[stats.Length];
            while (true)
            {
                var best = -1;
                ulong bestCost = 0;
                for (var i = 0; i < stats.Length; i++)
                {
                    var list = stats[i].XpList(table);
                    if (ranks[i] + 1 >= list.Count || (runCap is { } cap && IsRun(stats[i]) && ranks[i] >= cap))
                    {
                        continue;
                    }

                    var cost = list[ranks[i] + 1] - (ulong)list[ranks[i]];
                    if (best < 0 || cost < bestCost)
                    {
                        best = i;
                        bestCost = cost;
                    }
                }

                if (best < 0 || bestCost > budget)
                {
                    break;
                }

                budget -= bestCost;
                spent += bestCost;
                ranks[best]++;
            }

            carried = budget;
            for (var i = 0; i < stats.Length; i++)
            {
                targets.Add(stats[i] with { Ranks = ranks[i] });
            }
        }

        return new SpendingPlan(targets, totalXp, spent, totalXp - spent);
    }

    private static StatTarget Skill(uint id, bool specialized) => new(MageStatKind.Skill, id, MageTemplate.SkillName(id), specialized, 0);

    private static bool IsRun(StatTarget t) => t.Kind == MageStatKind.Skill && t.Id == MageTemplate.Run;
}

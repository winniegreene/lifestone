using Lifestone.Data.CharacterCreation;

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
/// <para><b>Leftovers.</b> What a share cannot spend (a rank cap, or a next rank that does not fit) rolls into the following
/// shares in plan order, magic first and then the 10% pool, so the final remainder, which is recorded, is as small as the rank
/// granularity of the last stat allows. The percentages are floored; the pool takes what is left, and its integer-division remainder
/// goes to its last share.</para>
/// </remarks>
public static class MageSpendingPlanner
{
    /// <summary>The percentages of the magic shares: the four magic skills, Focus and Self, Mana Conversion, Max Mana (the rest is the pool).</summary>
    private static readonly int[] MagicPercents = [50, 30, 5, 5];

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
    public static SpendingPlan Compute(XpTableData table, ulong totalXp)
    {
        ArgumentNullException.ThrowIfNull(table);
        var magic = new StatTarget[][]
        {
            [
                Skill(MageTemplate.WarMagic, specialized: true),
                Skill(MageTemplate.LifeMagic, specialized: false),
                Skill(MageTemplate.CreatureEnchantment, specialized: false),
                Skill(MageTemplate.ItemEnchantment, specialized: false),
            ],
            [
                new(MageStatKind.Attribute, MageTemplate.Focus, "Focus", false, 0),
                new(MageStatKind.Attribute, MageTemplate.Self, "Self", false, 0),
            ],
            [Skill(MageTemplate.ManaConversion, specialized: false)],
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
            Skill(MageTemplate.MeleeDefense, specialized: true),
        };
        pool.AddRange(MageTemplate.CreationTrained.Select(id => Skill(id, specialized: false)));
        pool.AddRange(MageTemplate.TrainedLater.Where(id => !MageTemplate.IsMagicPlanSkill(id)).Select(id => Skill(id, specialized: false)));

        var shares = new List<Share>();
        ulong magicXp = 0;
        for (var g = 0; g < magic.Length; g++)
        {
            var xp = (totalXp / 100 * (ulong)MagicPercents[g]) + (totalXp % 100 * (ulong)MagicPercents[g] / 100);
            magicXp += xp;
            shares.Add(new Share(xp, magic[g]));
        }

        var poolXp = totalXp - magicXp;
        var each = poolXp / (ulong)pool.Count;
        for (var i = 0; i < pool.Count; i++)
        {
            shares.Add(new Share(i == pool.Count - 1 ? poolXp - (each * (ulong)(pool.Count - 1)) : each, [pool[i]]));
        }

        var targets = new List<StatTarget>();
        ulong carried = 0;
        ulong spent = 0;
        foreach (var share in shares)
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
                    if (ranks[i] + 1 >= list.Count)
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
}

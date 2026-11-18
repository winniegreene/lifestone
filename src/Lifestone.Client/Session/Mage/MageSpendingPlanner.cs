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
/// <param name="Floor">M6 1 b (<c>m6-climber-strength</c>): true for a floor's target (the Strength floor, M6 7 R the health floor's two), which the plan lists first and the spend raises before every share (<see cref="MageSpendingPlanner.Compute"/>'s <c>strengthFloorRanks</c>, <c>healthFloor</c>): a pool that does not cover its next rank waits, it is never passed over.</param>
public sealed record StatTarget(MageStatKind Kind, uint Id, string Name, bool Specialized, int Ranks, bool Floor = false)
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
/// M6 7 finish (the owner's decision of 2026-11-08, changing D72 and D101): no single attribute and not Max Health takes more than a small share of the experience. A floor (the Strength floor, the health floor) aims at <see cref="AimPercent"/> of the experience earned; nothing passes <see cref="MaxPercent"/>. The hard limit is at least <see cref="GraceXp"/>, because at level 2 a single attribute rank is more than 10% of the total; the aim has no grace.
/// </summary>
/// <param name="AimPercent">The floors' share of the experience earned, per stat.</param>
/// <param name="MaxPercent">The most any capped stat may have cost, of the experience earned.</param>
/// <param name="GraceXp">The hard limit is at least this much, so the first two ranks of an attribute are not stopped by the small total of level 2 (by level 3, 10% of the total covers them).</param>
public sealed record StatCap(int AimPercent, int MaxPercent, ulong GraceXp)
{
    /// <summary>The floors' limit at <paramref name="totalXp"/>.</summary>
    public ulong Aim(ulong totalXp) => Percent(totalXp, AimPercent);

    /// <summary>The hard limit at <paramref name="totalXp"/>.</summary>
    public ulong Max(ulong totalXp) => Math.Max(Percent(totalXp, MaxPercent), GraceXp);

    /// <summary>True for a stat the cap binds: every attribute, and Max Health. Max Mana, Max Stamina and the skills are not named by the owner's rule.</summary>
    public static bool Binds(StatTarget t) => t is not null && Binds(t.Kind, t.Id);

    /// <summary>True for a stat the cap binds, by kind and id (<see cref="Binds(StatTarget)"/>).</summary>
    public static bool Binds(MageStatKind kind, uint id) => kind == MageStatKind.Attribute || (kind == MageStatKind.Vital && id == MageTemplate.MaxHealth);

    /// <summary>True for the two health stats, Max Health and Endurance: the owner's standing rule gives each about <see cref="AimPercent"/> of the experience earned, so their limit is the aim, not the 10% line.</summary>
    public static bool IsHealth(MageStatKind kind, uint id) => (kind == MageStatKind.Vital && id == MageTemplate.MaxHealth) || (kind == MageStatKind.Attribute && id == MageTemplate.Endurance);

    /// <summary>
    /// M6 item 14 (the owner's standing rule, read against the allocation of 2026-11-11; serves D112): the percent of the experience earned a bound stat may have cost: <see cref="AimPercent"/> for Max Health and Endurance (about 5% each: the floor's share and the
    /// pool's share are one account, not two), <see cref="MaxPercent"/> for every other attribute. 100 for a stat the cap does not bind.
    /// </summary>
    public int LimitPercent(MageStatKind kind, uint id) => !Binds(kind, id) ? 100 : IsHealth(kind, id) ? AimPercent : MaxPercent;

    /// <summary>The most a stat may have cost at <paramref name="totalXp"/> (<see cref="LimitPercent"/>): the aim for the two health stats (no grace, as the health floor's budget), the hard limit with its grace for every other attribute, no limit for a stat the cap does not bind.</summary>
    public ulong Limit(MageStatKind kind, uint id, ulong totalXp) => !Binds(kind, id) ? ulong.MaxValue : IsHealth(kind, id) ? Aim(totalXp) : Max(totalXp);

    private static ulong Percent(ulong total, int percent) => (total / 100 * (ulong)percent) + (total % 100 * (ulong)percent / 100);
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

    /// <summary>
    /// M6 item 14 (the owner's standing rule for the climber: Health and Endurance about 5% of the experience earned each, no attribute over 10%, the rest to her magic skills; serves D112, read against the allocation of 2026-11-11 that gave Jump 18 ranks and Magic
    /// Defense 14): the ladder's character's split, used wherever her <see cref="StatCap"/> is. 55% to the magic skills she has trained (War Magic, Life Magic, Item Enchantment, Creature Enchantment once trained), 20% to Focus and Self (each bound by the 10% line), 5% to Mana
    /// Conversion once trained, 5% to Max Mana, and the other 15% in three equal shares: Endurance, Max Health (each bound by the 5% aim, floor and share together) and Max Stamina (the stamina-to-mana dance's supply, D88 and D114). What a share cannot spend goes to the
    /// magic skills and Max Mana (D112), never down the pool.
    /// <para><b>Left out, each by name</b> (weight 0: the plan never lists it, so it is never raised). Strength: bought only by its floor, when the measured burden asks (<see cref="StrengthFloor"/>). Coordination: she has no skill that reads it (no Melee or Missile
    /// Defense). Quickness and Run: the buff bot's level VII lines stand on both, and a run rank at her level buys little speed (<see cref="RunPayback"/>). Jump: no rule of hers jumps. Magic Defense: the tuskers do not cast. Arcane Lore: no share; M6 14
    /// (<c>m6-lore-spend</c>, the owner, 2026-11-14) buys it only toward a piece she holds whose spells her lore alone keeps off, when the piece's gain pays for the experience (<see cref="LoreSpend"/>, through <see cref="MageSpendingPlanner.Compute"/>'s <c>beforeShares</c>). Loyalty and Salvaging: as the dance split (D126 b).</para>
    /// </summary>
    public static MageShares Climber { get; } = new(
        "climber",
        [55, 20, 5, 5],
        new Dictionary<(MageStatKind, uint), int>
        {
            [(MageStatKind.Attribute, MageTemplate.Strength)] = 0,
            [(MageStatKind.Attribute, MageTemplate.Coordination)] = 0,
            [(MageStatKind.Attribute, MageTemplate.Quickness)] = 0,
            [(MageStatKind.Skill, MageTemplate.ArcaneLore)] = 0,
            [(MageStatKind.Skill, MageTemplate.MagicDefense)] = 0,
            [(MageStatKind.Skill, MageTemplate.Jump)] = 0,
            [(MageStatKind.Skill, MageTemplate.Run)] = 0,
            [(MageStatKind.Skill, MageTemplate.Loyalty)] = 0,
            [(MageStatKind.Skill, MageTemplate.Salvaging)] = 0,
        });

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
/// the spare credits, which are none now: Healing was dropped by an owner ruling of 2026-10-15; see <see cref="MageTemplate.CreationTrained"/> and <see cref="ChooseSurvivalSkills"/>).</para>
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
    /// <param name="strengthFloorRanks">M6 1 b (<c>m6-climber-strength</c>): the Strength ranks bought BEFORE the shares (<see cref="StrengthFloor.Ranks"/>: the ranks whose capacity carries the measured burden under the ground's line), listed first as a
    /// <see cref="StatTarget.Floor"/> target; the shares then split what the floor leaves, and Strength's own pool share buys on from the floor's rank. 0 (the default): no floor, the plan of before. A floor the total cannot pay is
    /// bought as far as it goes.</param>
    /// <param name="healthFloor">M6 7 R: the climber's health floor (<see cref="ClimberHealthRule"/>): the Max Health and Endurance ranks bought BEFORE the shares and after the Strength floor, listed after it as <see cref="StatTarget.Floor"/> targets; each stat's pool share buys on
    /// from the floor's rank. Null (the default): no floor. What the total left after the Strength floor cannot pay is bought as far as it goes.</param>
    /// <param name="push">M6 7 AK (the owner: a mage keeps pushing for stronger buffs): skills the magic skills' share raises first, in order, each to its ranks (<see cref="BuffReach.Push"/>), as far as the share pays, and listed first in it; the health floor and every other share stand. Null: the plan of before.</param>
    /// <param name="cap">M6 7 finish (the owner, 2026-11-08): the cap on each attribute and Max Health (<see cref="StatCap"/>); what a capped stat leaves goes to the four magic skills and Max Mana. Null (the default): no cap, the plan of before.
    /// M6 item 14: each stat's limit is <see cref="StatCap.Limit"/> (the 5% aim for Max Health and Endurance, the 10% line for every other attribute).</param>
    /// <param name="strengthCureRanks">M6 item 14: the Strength ranks the floor keeps whatever the cap says (<see cref="StrengthFloor.CureWant"/>: the least that brings a load she cannot run under back under the run line), as far as the total pays. The one case a floor
    /// passes the cap; the spend names it each time. 0 (the default): the cap wins everywhere.</param>
    /// <param name="beforeShares">M6 14 (<c>m6-lore-spend</c>): stats bought after the floors and before every share, each to its ranks as far as the total pays (the Arcane Lore rule's target, <see cref="LoreSpend"/>), listed after the floors and not
    /// as floors (the walk passes over one whose next rank the pool does not cover); their experience comes off the total the shares split, and a share holding the same stat buys on from its rank. Null (the default): the plan of before.</param>
    public static SpendingPlan Compute(XpTableData table, ulong totalXp, IReadOnlyCollection<uint>? untrained = null, MageShares? shares = null, MageBuild build = MageBuild.Gate126, int strengthFloorRanks = 0, HealthBuy? healthFloor = null, IReadOnlyList<SkillPush>? push = null, StatCap? cap = null, int strengthCureRanks = 0, IReadOnlyList<StatTarget>? beforeShares = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfNegative(strengthFloorRanks);
        ArgumentOutOfRangeException.ThrowIfNegative(strengthCureRanks);
        var earned = totalXp; // M6 7 finish: the caps are shares of all the experience earned, whatever the floors take off the top
        var capMax = cap?.Max(totalXp) ?? ulong.MaxValue; // the most Strength (as every attribute but Endurance) may have cost
        var attributes = table.AttributeXpList;
        var floorRanks = Math.Min(strengthFloorRanks, attributes.Count - 1);
        var cureRanks = Math.Min(strengthCureRanks, floorRanks);
        while (floorRanks > cureRanks && attributes[floorRanks] > capMax)
        {
            floorRanks--; // M6 item 14: the cap wins over the floor, down to the cure's ranks and no further
        }

        while (floorRanks > 0 && attributes[floorRanks] > totalXp)
        {
            floorRanks--; // the floor as far as the total pays
        }

        ulong floorXp = attributes[floorRanks];
        var floorTarget = floorRanks > 0 ? new StatTarget(MageStatKind.Attribute, MageTemplate.Strength, "Strength", false, floorRanks, Floor: true) : null;
        totalXp -= floorXp;
        var vitals = table.VitalXpList;
        var healthRanks = Math.Min(healthFloor?.HealthRanks ?? 0, vitals.Count - 1);
        while (healthRanks > 0 && vitals[healthRanks] > totalXp)
        {
            healthRanks--; // the floor as far as the total pays
        }

        ulong healthXp = vitals[healthRanks];
        var enduranceRanks = Math.Min(healthFloor?.EnduranceRanks ?? 0, attributes.Count - 1);
        while (enduranceRanks > 0 && attributes[enduranceRanks] > totalXp - healthXp)
        {
            enduranceRanks--;
        }

        healthXp += attributes[enduranceRanks];
        totalXp -= healthXp;
        var first = new List<StatTarget>();
        ulong firstXp = 0;
        foreach (var t in beforeShares ?? [])
        {
            var list = t.XpList(table);
            var r = Math.Min(t.Ranks, list.Count - 1);
            while (r > 0 && list[r] > totalXp - firstXp)
            {
                r--; // as far as the total pays
            }

            if (r > 0)
            {
                firstXp += list[r];
                first.Add(t with { Ranks = r, Floor = false });
            }
        }

        totalXp -= firstXp;
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
        if (floorTarget is not null)
        {
            targets.Add(floorTarget); // M6 1 b: the floor first, so the spend raises it before every share
        }

        if (healthRanks > 0)
        {
            targets.Add(new StatTarget(MageStatKind.Vital, MageTemplate.MaxHealth, "Max Health", false, healthRanks, Floor: true)); // M6 7 R
        }

        if (enduranceRanks > 0)
        {
            targets.Add(new StatTarget(MageStatKind.Attribute, MageTemplate.Endurance, "Endurance", false, enduranceRanks, Floor: true)); // M6 7 R
        }

        targets.AddRange(first); // M6 14 (m6-lore-spend): after the floors, before every share

        ulong carried = 0;
        ulong freed = 0; // M6 7 finish: under a cap, what each share leaves goes to the magic skills and Max Mana, not on down the shares
        ulong spent = floorXp + healthXp + firstXp;
        foreach (var share in shareList)
        {
            var budget = share.Xp + carried;
            var stats = share.Stats;
            var ranks = stats.Select(t => IsStrength(t) ? floorRanks : IsMaxHealth(t) ? healthRanks : IsEndurance(t) ? enduranceRanks : first.FirstOrDefault(f => f.Kind == t.Kind && f.Id == t.Id)?.Ranks ?? 0).ToArray(); // a floor's stat (and one bought before the shares): its share buys on from that rank
            var pushed = new List<int>();
            foreach (var p in push ?? [])
            {
                var at = Array.FindIndex(stats, t => t.Kind == MageStatKind.Skill && t.Id == p.Skill);
                if (at < 0)
                {
                    continue;
                }

                pushed.Add(at);
                var list = stats[at].XpList(table);
                while (ranks[at] < Math.Min(p.Ranks, list.Count - 1) && list[ranks[at] + 1] - (ulong)list[ranks[at]] <= budget)
                {
                    var cost = list[ranks[at] + 1] - (ulong)list[ranks[at]];
                    budget -= cost;
                    spent += cost;
                    ranks[at]++;
                }
            }

            while (true)
            {
                var best = -1;
                ulong bestCost = 0;
                for (var i = 0; i < stats.Length; i++)
                {
                    var list = stats[i].XpList(table);
                    if (ranks[i] + 1 >= list.Count || (runCap is { } runRanks && IsRun(stats[i]) && ranks[i] >= runRanks) || (cap is not null && list[ranks[i] + 1] > cap.Limit(stats[i].Kind, stats[i].Id, earned)))
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

            carried = cap is null ? budget : 0;
            freed += cap is null ? 0 : budget;
            foreach (var i in pushed.Distinct().Concat(Enumerable.Range(0, stats.Length).Where(i => !pushed.Contains(i))))
            {
                targets.Add(stats[i] with { Ranks = ranks[i] });
            }
        }

        if (cap is not null)
        {
            var sinks = Enumerable.Range(0, targets.Count).Where(i => !targets[i].Floor && (magic[0].Any(m => m.Id == targets[i].Id && targets[i].Kind == MageStatKind.Skill) || (targets[i].Kind == MageStatKind.Vital && targets[i].Id == MageTemplate.MaxMana))).ToArray();
            while (sinks.Length > 0)
            {
                var best = -1;
                ulong bestCost = 0;
                foreach (var i in sinks)
                {
                    var list = targets[i].XpList(table);
                    if (targets[i].Ranks + 1 < list.Count && (list[targets[i].Ranks + 1] - (ulong)list[targets[i].Ranks] is var c) && (best < 0 || c < bestCost))
                    {
                        (best, bestCost) = (i, c);
                    }
                }

                if (best < 0 || bestCost > freed)
                {
                    break;
                }

                freed -= bestCost;
                spent += bestCost;
                targets[best] = targets[best] with { Ranks = targets[best].Ranks + 1 };
            }
        }

        var total = totalXp + floorXp + healthXp + firstXp;
        return new SpendingPlan(targets, total, spent, total - spent);
    }

    private static bool IsStrength(StatTarget t) => t.Kind == MageStatKind.Attribute && t.Id == MageTemplate.Strength;

    private static bool IsEndurance(StatTarget t) => t.Kind == MageStatKind.Attribute && t.Id == MageTemplate.Endurance;

    private static bool IsMaxHealth(StatTarget t) => t.Kind == MageStatKind.Vital && t.Id == MageTemplate.MaxHealth;

    private static StatTarget Skill(uint id, bool specialized) => new(MageStatKind.Skill, id, MageTemplate.SkillName(id), specialized, 0);

    private static bool IsRun(StatTarget t) => t.Kind == MageStatKind.Skill && t.Id == MageTemplate.Run;
}

/// <summary>
/// M6 1 b (<c>m6-climber-strength</c>): the Strength the spend raises before the planner's shares. Item 0's climb walked 91% of its time at about 113% burden (Strength 10, capacity 1,500), and ACE slows a run over 100%: the load
/// modifier is 1 under a load of 1 and 2 - load from 1 to 2 (retail <c>EncumbranceSystem::LoadMod</c>, <see cref="EncumbranceSystem.LoadMod"/>; ACE <c>EncumbranceSystem.GetBurdenMod</c>). The capacity is
/// <see cref="EncumbranceSystem.EncumbranceCapacity"/> (150 x Strength, plus 30 x Strength a carrying augmentation; ACE <c>Player.GetEncumbranceCapacity</c>, @95ef426d <c>WorldObjects/Player_Inventory.cs:46-51</c>).
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> The floor is the smallest BASE Strength whose capacity carries the measured burden (the server's EncumbranceValue, read live) plus the ground's loot margin, all under 100%: the margin is the room the ground's
/// trip line leaves under 100% (<see cref="LinePercent"/>: the line is the profile's <c>burden_trip_percent</c>, at most 100), so the burden sits at or under the line and the loot the trip rule allows stays under 100%. The base
/// Strength (start plus ranks, no buff) is read because a buff lapses; the capacity ACE judges reads the buffed Strength, so the floor is conservative.</para>
/// </remarks>
public static class StrengthFloor
{
    /// <summary>The line the floor carries the burden under: the ground's <c>burden_trip_percent</c>, at most 100 (a line over 100%, the outskirts' 150, is a trip line, not a carrying one).</summary>
    /// <param name="burdenTripPercent">The profile's <c>burden_trip_percent</c>.</param>
    public static int LinePercent(int burdenTripPercent) => Math.Clamp(burdenTripPercent, 1, 100);

    /// <summary>The smallest base Strength whose capacity carries <paramref name="burden"/> at or under <paramref name="linePercent"/> of it.</summary>
    /// <param name="burden">The measured burden.</param>
    /// <param name="augmentations">The carrying augmentations (PropertyInt 0xE6).</param>
    /// <param name="linePercent">The line (<see cref="LinePercent"/>).</param>
    public static int Want(int burden, int augmentations, int linePercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(linePercent);
        var strength = 1;
        while ((long)burden * 100 > (long)EncumbranceSystem.EncumbranceCapacity(strength, augmentations) * linePercent)
        {
            strength++;
        }

        return strength;
    }

    /// <summary>
    /// M6 item 14: the load from which she cannot run, in percent of the capacity: retail's load modifier is 0 from a load of 2 (<see cref="EncumbranceSystem.LoadMod"/>; ACE <c>EncumbranceSystem.GetBurdenMod</c>), so the run rate is the walk's
    /// (<see cref="MovementSystem.GetRunRate"/>).
    /// </summary>
    public const int RunLinePercent = 200;

    /// <summary>M6 item 14: true when <paramref name="burden"/> is at or over the run line of <paramref name="capacity"/> (<see cref="RunLinePercent"/>): she cannot run.</summary>
    /// <param name="burden">The measured burden.</param>
    /// <param name="capacity">The capacity the server judges (the Strength with what stands on it).</param>
    public static bool CannotRun(int burden, int capacity) => (long)burden * 100 >= (long)capacity * RunLinePercent;

    /// <summary>
    /// M6 item 14, the one case a floor passes the owner's cap: the smallest Strength whose capacity puts <paramref name="burden"/> strictly under the run line (<see cref="RunLinePercent"/>), so she runs again. The least that cures it: the rest of the floor's want
    /// stays under the cap.
    /// </summary>
    /// <param name="burden">The measured burden.</param>
    /// <param name="augmentations">The carrying augmentations (PropertyInt 0xE6).</param>
    public static int CureWant(int burden, int augmentations)
    {
        var strength = 1;
        while (CannotRun(burden, EncumbranceSystem.EncumbranceCapacity(strength, augmentations)))
        {
            strength++;
        }

        return strength;
    }

    /// <summary>The floor's ranks over the attribute's start: <paramref name="want"/> less <paramref name="start"/>, at least 0.</summary>
    /// <param name="want">The Strength wanted (<see cref="Want"/>).</param>
    /// <param name="start">The attribute's creation value.</param>
    public static int Ranks(int want, uint start) => Math.Max(0, want - (int)start);
}

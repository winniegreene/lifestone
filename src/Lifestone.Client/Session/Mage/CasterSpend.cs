using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>M6 14 (<c>m6-caster-spend</c>): one skill-base wield requirement (ACE's RawSkill, type 2: the skill's <c>Base</c>, Player_Inventory.cs:2133-2146 @95ef426d) a held caster is short of.</summary>
/// <param name="Skill">The skill id.</param>
/// <param name="Base">Her base value now.</param>
/// <param name="Need">The requirement.</param>
public sealed record CasterSkillShort(uint Skill, int Base, int Need)
{
    /// <summary>The base points she is short: one rank of the skill is one base point (ACE's <c>CreatureSkill.Base</c> is the formula over the base attributes, plus InitLevel, plus the ranks).</summary>
    public int Points => Math.Max(0, Need - Base);

    /// <summary>The shortfall as the lines print it: <c>3 base points of War Magic (base 287 &lt; 290)</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Points} base point{(Points == 1 ? string.Empty : "s")} of {SelfEquipJudge.SkillName(Skill)} (base {Base} < {Need})");
}

/// <summary>
/// M6 14 (<c>m6-caster-spend</c>; the owner's ruling, 2026-11-14: she should definitely use the fire wand she has): a caster she holds whose wield requirement she does not meet, scored by the caster pick as if wielded (her skills with the points it is short
/// added, so its element and damage bonuses on the plan's element count as for a usable one: <see cref="SelfEquipJudge.ShortCasters"/>), against the caster she wields.
/// </summary>
/// <param name="Guid">The caster.</param>
/// <param name="Name">Its name.</param>
/// <param name="Skills">The skill-base requirements it is short of (empty when only another requirement is unmet).</param>
/// <param name="Other">Why no experience spend makes it wieldable, or null when its skill-base points alone would: another requirement unmet with those points bought (a level, a training, a current skill, an attribute), or the caster held back for the run.</param>
/// <param name="Score">Its caster score as if wielded, in percent of her hour.</param>
/// <param name="Held">The caster she wields, as the lines name it.</param>
/// <param name="HeldScore">That caster's score (0 when she wields none).</param>
/// <param name="Margin">The swap margin it must beat the held one by (the ladder's; 0 with none held).</param>
/// <param name="Beats">True when the score beats the held one's by the margin (<see cref="GearScore.CasterBeats"/>), as the pick would swap to it.</param>
public sealed record CasterShortfall(uint Guid, string Name, IReadOnlyList<CasterSkillShort> Skills, string? Other, double Score, string Held, double HeldScore, double Margin, bool Beats)
{
    /// <summary>The gain in percent of her hour: its score as if wielded over the held caster's.</summary>
    public double Gain => Score - HeldScore;

    /// <summary>The numbers as the lines print them.</summary>
    public string Numbers => string.Create(CultureInfo.InvariantCulture, $"as wielded {Score:0.##} against {Held} {HeldScore:0.##}: gain {Gain:+0.##;-0.##;0} of her hour, {(Beats ? "over" : "not over")} the swap margin {Margin:0.##}");
}

/// <summary>M6 14 (<c>m6-caster-spend</c>): what the experience spend reads of the casters she holds: the shortfalls (<see cref="SelfEquipJudge.ShortCasters"/>) and her measured rate.</summary>
/// <param name="Short">The held casters short of a wield requirement.</param>
/// <param name="XpPerHour">Her measured experience an hour, or null while none is measured.</param>
/// <param name="Rate">Where the rate comes from, or why there is none, for the line.</param>
public sealed record CasterSpendFacts(IReadOnlyList<CasterShortfall> Short, double? XpPerHour, string Rate);

/// <summary>M6 14 (<c>m6-caster-spend</c>): what the spend decided of the casters she holds.</summary>
/// <param name="Chosen">The caster whose points the spend buys first, or null.</param>
/// <param name="Pushes">The skills and ranks the spend buys first for it (<see cref="MageSpendingPlanner.Compute"/>'s push), empty with none chosen.</param>
/// <param name="Cost">The experience those ranks cost, 0 with none chosen.</param>
/// <param name="Line">The decision's line: the caster bought first with its numbers, or why none is.</param>
/// <param name="Others">The other casters' lines, each with its reason.</param>
public sealed record CasterSpendDecision(CasterShortfall? Chosen, IReadOnlyList<SkillPush> Pushes, long Cost, string Line, IReadOnlyList<string> Others);

/// <summary>
/// M6 14 (<c>m6-caster-spend</c>): the casters held back for the run: a swap whose wield did not land (ACE refused it, or the hands did not hold it after). The caster pick and the experience spend pass them over, each named, so a refusal is never
/// sent again at every visit. One a session (<see cref="Quartermaster.Wardrobe.HeldBack"/>, M6 16 E5e2): the swap lives in the resupply, the pick in the autopilot and the restore, the rearm in the recovery table, and every
/// judge the session's wardrobe builds carries the same one.
/// </summary>
public sealed class CasterHeldBack
{
    private readonly Dictionary<uint, string> _held = [];

    /// <summary>The casters held back.</summary>
    public int Count
    {
        get
        {
            lock (_held)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>Holds <paramref name="guid"/> back for the run.</summary>
    /// <param name="guid">The caster.</param>
    /// <param name="why">The failure, for the lines that pass it over.</param>
    public void Hold(uint guid, string why)
    {
        ArgumentNullException.ThrowIfNull(why);
        lock (_held)
        {
            _held[guid] = why;
        }
    }

    /// <summary>True when <paramref name="guid"/> is held back; <paramref name="why"/> names the failure.</summary>
    /// <param name="guid">The caster.</param>
    /// <param name="why">The failure, or empty.</param>
    public bool IsHeldBack(uint guid, out string why)
    {
        lock (_held)
        {
            if (_held.TryGetValue(guid, out var w))
            {
                why = w;
                return true;
            }
        }

        why = string.Empty;
        return false;
    }

    /// <summary>Forgets every caster (a test's fresh start).</summary>
    public void Reset()
    {
        lock (_held)
        {
            _held.Clear();
        }
    }
}

/// <summary>
/// M6 14 (<c>m6-caster-spend</c>; the owner, 2026-11-14: the Fire Sceptre in her pack wants base War Magic 290 and she has 287): the experience spend buys the points a held caster is short of first, when the caster is worth it and the points are near.
/// </summary>
/// <remarks>
/// <para><b>The gain.</b> The caster pick scores the caster as if wielded (<see cref="SelfEquipJudge.ShortCasters"/>: her skills with the points added, so its element, damage, Mana Conversion and defence bonuses count as for a usable one); the gain is that
/// score over her held caster's, in percent of her hour. Only a caster whose gain beats the swap margin is considered: the pick would not swap to any other.</para>
/// <para><b>The price.</b> The points are ranks of the skill (one rank, one base point), priced from the XpTable's list of the skill's class against the experience she has spent on it. The time is that experience at her measured rate, beside the time the
/// skill's own running share of her experience would take (the spend without this rule).</para>
/// <para><b>In reach.</b> The spend runs once a level (the level-up spend; a trip spends what the fights earned since). "The next cycle or two" is read as the allocations it will make: the unassigned pool now less what the floors take first, then the
/// experience of her next level-up and of the one after (the XpTable's level list from her total). The ranks are bought in order, each from one allocation, an allocation's leftover going to the shares (the whole allocation is spent, as before): in reach
/// when the last rank is bought by the third. Out of reach: logged with the numbers, and nothing changes.</para>
/// <para><b>The choice.</b> Of the casters in reach, the best gain for its experience (percent of her hour a million), then the larger gain, then the lower guid.</para>
/// <para><b>The buy.</b> The chosen caster's ranks go to the planner as a push of the magic skills' share (<see cref="MageSpendingPlanner.Compute"/>, the buff push's way): the floors stay first, the caps stand (a skill is bound by none), and the plan, made
/// for all the experience she has earned, lists them first in the share, so the spend buys them before every share's rank. The other magic skills' targets give the ranks back as the total grows.</para>
/// </remarks>
public static class CasterSpend
{
    /// <summary>The allocations after this one the points may wait for: the brief's "the next cycle or two", the spend running once a level.</summary>
    public const int NextAllocations = 2;

    /// <summary>The decision over the casters of <paramref name="facts"/> (see the class remarks).</summary>
    /// <param name="table">The XpTable (the skills' prices and the level list).</param>
    /// <param name="q">Her qualities now.</param>
    /// <param name="facts">The shortfalls and her rate.</param>
    /// <param name="pool">The unassigned experience now.</param>
    /// <param name="floorsOwed">The experience the floors take first from the pool.</param>
    /// <param name="raises">Whether the spend raises a stat for her (<see cref="MageTemplate.OnClimberList"/>).</param>
    public static CasterSpendDecision Decide(XpTableData table, PlayerQualities q, CasterSpendFacts facts, long pool, long floorsOwed, Func<MageStatKind, uint, bool> raises)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(raises);
        var c = CultureInfo.InvariantCulture;
        if (facts.Short.Count == 0)
        {
            return new CasterSpendDecision(null, [], 0, "no caster she holds is short of a wield requirement: nothing is bought first", []);
        }

        var allocations = Allocations(table, q, pool, floorsOwed, out var reach);
        var others = new List<string>();
        var fits = new List<(CasterShortfall Caster, List<SkillPush> Pushes, long Cost, int By, string Price)>();
        foreach (var s in facts.Short)
        {
            var head = string.Create(c, $"'{s.Name}' 0x{s.Guid:X8}");
            if (s.Other is { } other)
            {
                others.Add($"{head}: {other}: no points bought");
                continue;
            }

            if (s.Skills.Count == 0)
            {
                continue;
            }

            if (!s.Beats)
            {
                others.Add($"{head}: {s.Numbers}: no points bought");
                continue;
            }

            if (!Price(table, q, s, facts, raises, out var pushes, out var prices, out var cost, out var price, out var refused))
            {
                others.Add($"{head}: {s.Numbers}; {refused}: no points bought");
                continue;
            }

            var by = ReachedBy(prices, allocations);
            if (by < 0)
            {
                var biggest = prices.Count == 0 ? 0 : prices.Max();
                others.Add(string.Create(c, $"{head}: {s.Numbers}; {price}: out of reach of {reach} ({allocations.Sum():N0} in all{(biggest > allocations.Max() ? string.Create(c, $", and one rank costs {biggest:N0}, more than any one of them") : string.Empty)}): nothing changes"));
                continue;
            }

            fits.Add((s, pushes, cost, by, price));
        }

        if (fits.Count == 0)
        {
            return new CasterSpendDecision(null, [], 0, string.Create(c, $"none of the {facts.Short.Count} caster(s) she holds short of a wield requirement has its points bought first"), others);
        }

        static double PerMillion(CasterShortfall s, long cost) => cost > 0 ? s.Gain / (cost / 1_000_000.0) : double.MaxValue;
        var best = fits.OrderByDescending(f => PerMillion(f.Caster, f.Cost)).ThenByDescending(f => f.Caster.Gain).ThenBy(f => f.Caster.Guid).First();
        foreach (var f in fits.Where(f => f.Caster.Guid != best.Caster.Guid))
        {
            others.Add(string.Create(c, $"'{f.Caster.Name}' 0x{f.Caster.Guid:X8}: in reach ({f.Price}), but '{best.Caster.Name}' 0x{best.Caster.Guid:X8} gains more for its experience ({PerMillion(f.Caster, f.Cost):0.###} against {PerMillion(best.Caster, best.Cost):0.###} of her hour a million): not bought"));
        }

        var when = best.By switch { 0 => "this allocation", 1 => "the next level-up's", _ => "the level-up after it" };
        var line = string.Create(c, $"buy first: '{best.Caster.Name}' 0x{best.Caster.Guid:X8}: {best.Caster.Numbers}; {best.Price}; in reach: its last rank is bought by {when} of {reach}; {(fits.Count > 1 ? $"the best gain a million of the {fits.Count} in reach; " : string.Empty)}the spend buys them first, after the floors and before every share, and the pick wields it at the next home visit");
        return new CasterSpendDecision(best.Caster, best.Pushes, best.Cost, line, others);
    }

    /// <summary>
    /// The allocations a buy first may draw on (the class remarks' "In reach"): the unassigned pool now less what the floors take first, then the experience of her next level-up and of the one after (the XpTable's level list from her total),
    /// <see cref="NextAllocations"/> of them. <paramref name="reach"/> says them as the lines print them. Shared by the caster's points and the Arcane Lore rule (<see cref="LoreSpend"/>).
    /// </summary>
    /// <param name="table">The XpTable (the level list).</param>
    /// <param name="q">Her qualities now.</param>
    /// <param name="pool">The unassigned experience now.</param>
    /// <param name="floorsOwed">The experience the floors (and any buy before this one) take first from the pool.</param>
    /// <param name="reach">The allocations as the lines print them.</param>
    public static long[] Allocations(XpTableData table, PlayerQualities q, long pool, long floorsOwed, out string reach)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(q);
        var level = q.Level;
        var total = Math.Max(0, q.TotalExperience);
        var levels = table.CharacterLevelXpList;
        var allocations = new long[1 + NextAllocations];
        allocations[0] = Math.Max(0, pool - Math.Max(0, floorsOwed));
        for (var i = 1; i <= NextAllocations; i++)
        {
            var to = level + i;
            allocations[i] = level > 0 && to < levels.Count ? Math.Max(0, (long)levels[to] - Math.Max(total, (long)levels[to - 1])) : 0;
        }

        reach = string.Create(CultureInfo.InvariantCulture, $"this allocation {allocations[0]:N0} (the pool {pool:N0} less the floors' {Math.Max(0, floorsOwed):N0}), the next level-up's {allocations[1]:N0} (to level {level + 1}) and the one after {allocations[2]:N0} (to level {level + 2})");
        return allocations;
    }

    /// <summary>
    /// The index of the allocation by which the last of <paramref name="prices"/> is bought, each rank in order and from one allocation (an allocation's leftover goes to the shares), or -1 when they are not all bought by the last one.
    /// </summary>
    /// <param name="prices">The ranks' prices, in order.</param>
    /// <param name="allocations">The allocations, in order.</param>
    public static int ReachedBy(IReadOnlyList<long> prices, IReadOnlyList<long> allocations)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(allocations);
        if (allocations.Count == 0)
        {
            return prices.Count == 0 ? 0 : -1;
        }

        var k = 0;
        var left = allocations[0];
        foreach (var p in prices)
        {
            while (p > left)
            {
                if (++k >= allocations.Count)
                {
                    return -1;
                }

                left = allocations[k];
            }

            left -= p;
        }

        return k;
    }

    /// <summary>The ranks the shortfall asks, priced; false (with <paramref name="refused"/>) for a skill the spend does not raise for her, one not trained, or ranks past the list's cap.</summary>
    private static bool Price(XpTableData table, PlayerQualities q, CasterShortfall s, CasterSpendFacts facts, Func<MageStatKind, uint, bool> raises, out List<SkillPush> pushes, out List<long> prices, out long cost, out string price, out string refused)
    {
        var c = CultureInfo.InvariantCulture;
        (pushes, prices, cost, price, refused) = ([], [], 0, string.Empty, string.Empty);
        var parts = new List<string>();
        double shareHours = 0;
        var shareKnown = true;
        var total = Math.Max(0, q.TotalExperience);
        foreach (var k in s.Skills)
        {
            var name = SelfEquipJudge.SkillName(k.Skill);
            if (!raises(MageStatKind.Skill, k.Skill))
            {
                refused = $"{name} is not a skill her spend raises";
                return false;
            }

            if (!q.TryGetSkill(k.Skill, out var skill) || skill.AdvancementClass < SkillRules.TrainedClass)
            {
                refused = $"{name} is not trained (no rank of it can be bought)";
                return false;
            }

            var list = skill.AdvancementClass >= SkillRules.SpecializedClass ? table.SpecializedSkillXpList : table.TrainedSkillXpList;
            var from = (int)skill.Ranks;
            var to = from + k.Points;
            if (to >= list.Count)
            {
                refused = string.Create(c, $"{name} {from} -> {to} is past its rank cap {list.Count - 1}");
                return false;
            }

            var spent = (long)skill.Experience;
            for (var r = from; r < to; r++)
            {
                prices.Add(Math.Max(0, (long)list[r + 1] - Math.Max(spent, (long)list[r])));
            }

            var xp = Math.Max(0, (long)list[to] - spent);
            cost += xp;
            pushes.Add(new SkillPush(k.Skill, to, string.Create(c, $"{name} base {k.Need} lets her wield '{s.Name}' 0x{s.Guid:X8}")));
            var share = total > 0 ? (double)skill.Experience / total : 0;
            parts.Add(string.Create(c, $"{k}: {name} ranks {from} -> {to} for {xp:N0}"));
            if (share > 0 && facts.XpPerHour is > 0 and var rate)
            {
                shareHours += xp / (rate * share);
                parts[^1] += string.Create(c, $" ({name}'s running share of her experience {100 * share:0.0}%)");
            }
            else
            {
                shareKnown = false;
            }
        }

        var time = facts.XpPerHour is > 0 and var perHour
            ? string.Create(c, $"{cost / perHour:0.0} h of her play at {perHour:N0} an hour ({facts.Rate}){(shareKnown ? string.Create(c, $", against {shareHours:0.0} h at the skill's running share") : string.Empty)}")
            : $"the time is not priced: {facts.Rate}";
        price = string.Create(c, $"{string.Join(", ", parts)}: {cost:N0} experience in all, {time}");
        return true;
    }
}

/// <summary>M6 14 (<c>m6-caster-spend</c>): her numbers with the points a caster is short of added (<see cref="SelfEquipJudge.ShortCasters"/>'s "as if wielded"): each skill's base and current go up by its points, everything else as hers.</summary>
/// <param name="inner">Her numbers.</param>
/// <param name="shorts">The shortfalls.</param>
internal sealed class RaisedSkills(IGearSkills inner, IReadOnlyList<CasterSkillShort> shorts) : IGearSkills
{
    private int Lift(uint skill)
    {
        var lift = 0;
        foreach (var s in shorts)
        {
            if (s.Skill == skill)
            {
                lift = Math.Max(lift, s.Points);
            }
        }

        return lift;
    }

    /// <inheritdoc/>
    public int Level => inner.Level;

    /// <inheritdoc/>
    public int Heritage => inner.Heritage;

    /// <inheritdoc/>
    public int AdvancementClass(uint skill) => inner.AdvancementClass(skill);

    /// <inheritdoc/>
    public int? Current(uint skill) => inner.Current(skill) is { } v ? v + Lift(skill) : null;

    /// <inheritdoc/>
    public int? Base(uint skill) => inner.Base(skill) is { } v ? v + Lift(skill) : null;

    /// <inheritdoc/>
    public int? Attribute(uint attribute) => inner.Attribute(attribute);

    /// <inheritdoc/>
    public int? VitalMax(uint vital) => inner.VitalMax(vital);
}

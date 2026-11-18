using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 14 (the lead, 2026-11-16: the element casters the fight's element line actually uses): the bolt lines the fight put in hand this session (its start line and each line a target's hardest took, <c>FightScenario.LoadLine</c>), by
/// element. One per session (<see cref="For"/>); game thread only.
/// </summary>
public sealed class ElementsUsed
{
    private static readonly ConditionalWeakTable<GameState, ElementsUsed> PerSession = new();
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    /// <summary>The memory of a session, made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static ElementsUsed For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static _ => new ElementsUsed());
    }

    /// <summary>The elements used, in no order.</summary>
    public IReadOnlyCollection<string> Used => _used;

    /// <summary>Notes that the fight put <paramref name="element"/>'s bolt line in hand.</summary>
    /// <param name="element">The element (a key of <see cref="WarSpellData.BoltLineIds"/>).</param>
    public void Note(string element) => _used.Add(element);
}

/// <summary>What <see cref="CasterKeep.Choose"/> reads of one held caster.</summary>
/// <param name="Guid">The caster.</param>
/// <param name="Name">Its name.</param>
/// <param name="Wielded">True when she wields it.</param>
/// <param name="Read">True when its appraisal is read.</param>
/// <param name="Usable">True when she meets its wield requirements (read).</param>
/// <param name="UsefulGreat">True when it casts a great cantrip useful to her.</param>
/// <param name="Mana">Its mana (ItemCurMana; 0 when none or unread).</param>
/// <param name="MaxMana">Its mana capacity (ItemMaxMana; 0 when none).</param>
public readonly record struct HeldCasterFacts(uint Guid, string Name, bool Wielded, bool Read, bool Usable, bool UsefulGreat, int Mana, int MaxMana);

/// <summary>One held caster's verdict (<see cref="CasterKeep.Decide"/>).</summary>
/// <param name="Guid">The caster.</param>
/// <param name="Name">Its name.</param>
/// <param name="KeptFor">Why her gear rules need it, or null: it is sell loot.</param>
/// <param name="Line">The verdict as the log prints it, with its numbers.</param>
public readonly record struct CasterKeepVerdict(uint Guid, string Name, string? KeptFor, string Line);

/// <summary>
/// M6 14 (the owner's ruling, 2026-11-16: drop the loot casters too; her cycle line read casters=14): the casters she carries are only the ones her gear rules need, derived live each time it is asked; every other caster is sell loot, ranked like
/// any other in the swap and at the sale. Kept, in this order (the first reason named):
/// <list type="number">
/// <item>the caster she wields;</item>
/// <item>a caster not appraised yet (never sold blind);</item>
/// <item>a caster with a useful great cantrip (<see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/>);</item>
/// <item>for each element the fight's element line uses (the plan's element and every line the fight put in hand this session, <see cref="ElementsUsed"/>) that she can cast (a level of its bolt line in her book castable at the cast
/// chance, <see cref="WarSpellData.BoltLineIds"/>), the best caster for that element by the caster score whose wield requirements she meets (<see cref="GearScore.Caster"/>, the judge of that element);</item>
/// <item>the gear floor's "two casters always" (<see cref="GearFloor.CastersWanted"/>): while fewer usable casters are kept, the best other on the plan's element;</item>
/// <item>the mana rule's charged spare (<see cref="CasterManaRule"/>: the spare it swaps to is the one holding the most mana);</item>
/// <item>a caster short of a wield requirement whose score as wielded beats hers over the swap margin (<see cref="SelfEquipJudge.ShortCasters"/>): the experience spend works toward it (<see cref="CasterSpend"/>).</item>
/// </list>
/// Pure over the read model: allocates the verdicts.
/// </summary>
public static class CasterKeep
{
    /// <summary>The verdict of every caster she holds (wielded, the main pack, the side packs), read from the session's model and decided by <see cref="Choose"/>.</summary>
    /// <param name="state">The read model (her book, skills, inventory, appraisals).</param>
    /// <param name="spells">The SpellTable's spells (the bolt lines' powers).</param>
    /// <param name="judgeFor">The self-equip judge for an element's bits (the caster score's weights for that element).</param>
    /// <param name="planElement">The element bits of the ground's plan (the floor's spare is scored on it); 0 for none read.</param>
    /// <param name="castChance">The cast chance a bolt level must reach to count as cast (the fight's).</param>
    /// <param name="usefulGreat">True for a caster whose great cantrip is useful to her.</param>
    /// <param name="used">The elements the fight's element line used this session (<see cref="ElementsUsed"/>); the plan's element is added.</param>
    public static List<CasterKeepVerdict> Decide(GameState state, IReadOnlyDictionary<uint, SpellInfo> spells, Func<uint, SelfEquipJudge> judgeFor, uint planElement, double castChance, Func<uint, bool> usefulGreat, IReadOnlyCollection<string> used)
    {
        ArgumentNullException.ThrowIfNull(used);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(judgeFor);
        ArgumentNullException.ThrowIfNull(usefulGreat);
        var guids = new List<uint>();
        GearInventory.Collect(state, guids);
        var held = new List<(WorldObject Item, Appraisal? Appraisal)>();
        var skills = new QualitiesSkills(state.Self.Qualities);
        var facts = new List<HeldCasterFacts>();
        foreach (var g in guids)
        {
            if (state.Objects.TryGet(g, out var o) && (o.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                var a = state.Appraisals.TryGet(g, out var read) && !read.IsNotFound ? read : null;
                held.Add((o, a));
                facts.Add(new HeldCasterFacts(g, o.Name, state.Self.Inventory.Find(g).Kind == InventoryPlaceKind.Equipped || o.Weenie.WielderId is > 0, a is not null,
                    a is not null && SelfEquipJudge.WieldUnmet(a, skills) is null, a is not null && usefulGreat(g), a?.ItemMana ?? 0, a?.ItemMaxMana ?? 0));
            }
        }

        // the elements the fight's element line uses (the plan's and the ones it put in hand) that she can cast: a level of the line in her book castable at the cast chance
        var elements = new List<(string Name, uint Bits)>();
        if (GearCasts.TryCurrentSkill(state.Self.Qualities, MageTemplate.WarMagic, out var war, out _))
        {
            foreach (var (element, ids) in WarSpellData.BoltLineIds)
            {
                var bits = SelfEquipJudge.ElementBits[element];
                if ((used.Contains(element) || bits == planElement) && ids.Any(id => state.Self.Spellbook.Knows(id) && spells.TryGetValue(id, out var info) && GearCasts.Chance(war, info.Power) >= castChance))
                {
                    elements.Add((element, bits));
                }
            }
        }

        var floorElement = planElement != 0 ? planElement : elements.Count > 0 ? elements[0].Bits : 0;
        var contexts = new Dictionary<uint, GearContext>();
        double Score(uint element, uint guid)
        {
            if (!contexts.TryGetValue(element, out var context))
            {
                contexts[element] = context = judgeFor(element).Context(skills);
            }

            var x = held.First(h => h.Item.Guid == guid);
            return GearScore.Caster(guid, x.Item.Name, x.Appraisal!, context).CasterScore;
        }

        var candidates = held.Where(h => !facts.First(f => f.Guid == h.Item.Guid).Wielded).Select(h => SelfEquipJudge.CandidateOf(h.Item, h.Appraisal)).ToList();
        var spend = new Dictionary<uint, string>();
        foreach (var shortfall in judgeFor(floorElement).ShortCasters(candidates, SelfEquipJudge.Worn(state), skills))
        {
            if (shortfall.Beats && shortfall.Other is null)
            {
                spend[shortfall.Guid] = shortfall.Numbers;
            }
        }

        return Choose(facts, elements, floorElement, Score, spend);
    }

    /// <summary>
    /// The rule over the facts (see the class remarks): pure. <paramref name="score"/> gives a usable caster's caster score on an element's bits; <paramref name="spend"/> holds the casters short of a wield requirement the experience spend
    /// works toward, with its numbers.
    /// </summary>
    /// <param name="casters">The casters she holds.</param>
    /// <param name="elements">The elements the fight's line uses that she can cast, by name and bits.</param>
    /// <param name="floorElement">The element the floor's spare is scored on.</param>
    /// <param name="score">A usable caster's score on an element.</param>
    /// <param name="spend">The spend's targets.</param>
    public static List<CasterKeepVerdict> Choose(IReadOnlyList<HeldCasterFacts> casters, IReadOnlyList<(string Name, uint Bits)> elements, uint floorElement, Func<uint, uint, double> score, IReadOnlyDictionary<uint, string> spend)
    {
        ArgumentNullException.ThrowIfNull(casters);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(spend);
        var c = CultureInfo.InvariantCulture;
        var kept = new Dictionary<uint, string>();
        void Keep(uint guid, string why) => kept.TryAdd(guid, why);
        foreach (var x in casters)
        {
            if (x.Wielded)
            {
                Keep(x.Guid, "the caster she wields");
            }
            else if (!x.Read)
            {
                Keep(x.Guid, "not appraised: never sold blind");
            }
            else if (x.UsefulGreat)
            {
                Keep(x.Guid, "a useful great cantrip");
            }
        }

        uint? Best(uint element, Func<uint, bool> skip, out double best)
        {
            best = double.NegativeInfinity;
            uint? won = null;
            foreach (var x in casters)
            {
                if (skip(x.Guid) || !x.Usable)
                {
                    continue;
                }

                var s = score(element, x.Guid);
                if (won is not { } w || s > best + 1e-6 || (Math.Abs(s - best) <= 1e-6 && x.Guid < w))
                {
                    (won, best) = (x.Guid, s);
                }
            }

            return won;
        }

        foreach (var (name, bits) in elements)
        {
            if (Best(bits, _ => false, out var s) is { } g)
            {
                Keep(g, string.Create(c, $"the best caster for the {name} line the fight uses (score {s:0.##})"));
            }
        }

        // the floor counts as GearFloor.CountsAsCaster does: the wielded, a usable one, and one not read yet
        while (casters.Count(x => kept.ContainsKey(x.Guid) && (x.Wielded || x.Usable || !x.Read)) < GearFloor.CastersWanted && Best(floorElement, kept.ContainsKey, out var spareScore) is { } spare)
        {
            Keep(spare, string.Create(c, $"the gear floor's spare (two casters always; score {spareScore:0.##} on the element 0x{floorElement:X})"));
        }

        var charged = casters.Where(x => !x.Wielded && x.MaxMana > 0 && x.Mana > 0).OrderByDescending(x => x.Mana).ThenBy(x => x.Guid).ToList();
        if (charged.Count > 0)
        {
            Keep(charged[0].Guid, string.Create(c, $"the mana rule's charged spare ({charged[0].Mana}/{charged[0].MaxMana} mana, the most of the spares)"));
        }

        foreach (var (guid, numbers) in spend)
        {
            Keep(guid, "the experience spend works toward its wield requirement (" + numbers + ")");
        }

        var names = string.Join(", ", elements.Select(e => e.Name));
        var verdicts = new List<CasterKeepVerdict>(casters.Count);
        foreach (var x in casters)
        {
            var why = kept.GetValueOrDefault(x.Guid);
            var line = why is null
                ? string.Create(c, $"'{x.Name}' 0x{x.Guid:X8}: sell loot (not the wielded, no useful great cantrip, not the best for any of the {elements.Count} element(s) the fight's line uses ({names}), not the floor's spare nor the charged spare{(x.Usable ? string.Empty : ", short of a wield requirement the spend does not work toward")})")
                : string.Create(c, $"'{x.Name}' 0x{x.Guid:X8}: kept: {why}");
            verdicts.Add(new CasterKeepVerdict(x.Guid, x.Name, why, line));
        }

        return verdicts;
    }

    /// <summary>The verdicts as one log line: kept and sold counts, then each verdict.</summary>
    /// <param name="verdicts">The verdicts.</param>
    public static string Line(IReadOnlyList<CasterKeepVerdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        return string.Create(CultureInfo.InvariantCulture, $"casters: {verdicts.Count} held, {verdicts.Count(v => v.KeptFor is not null)} kept, {verdicts.Count(v => v.KeptFor is null)} sell loot (the owner, 2026-11-16: only the casters her gear rules need): {string.Join("; ", verdicts.Select(v => v.Line))}");
    }
}

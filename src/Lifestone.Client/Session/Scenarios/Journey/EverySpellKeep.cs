using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E5c2 (docs/automation-engine.md E5, <c>TODO(engine-sale-copies)</c>): the every-spell rule's keep of a scroll (M6 14, the owner's rule: every castable spell is bought and read), as one place the town trip and the session's
/// sale context both ask. Before this slice only a trip could answer (<c>ResupplyRun.EverySpellKeepReason</c> over the trip's own fields), so outside a trip the rule book read a scroll the rule wants as loot to sell, and the field's
/// make-room rule could drop it. The trip passes what is its own (the plan's needs it read, the scrolls it reads already, its latch of the inputs); the session's context passes the same things as they stand with no trip under way
/// (<see cref="Quartermaster.SaleContext.For"/>).
/// </summary>
internal static class EverySpellKeep
{
    private const uint WritableType = ResupplyRun.WritableType;

    /// <summary>
    /// The rule's inputs that no trip's purse or room decide, read live: the book, the SpellTable's facts, the standing skills (<see cref="ScrivenerLearnPlan.Standing"/>, the buff bot's lines while it applies), the ladder's two bars,
    /// the spell plan's lines, the bot's cover, the ground's incoming elements and home vendors, the session's refusals and the held scrolls.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="memory">The session's sale memory (the learn refusals, the every-spell rule's own memory).</param>
    /// <param name="planNeeds">The spell plan's needs on the ground and rung (<see cref="PlanNeeds"/>): the rule defers to its lines.</param>
    /// <param name="onTrip">The spells a trip reads already; empty outside a trip.</param>
    /// <param name="gearNeedsLore">True when her gear asks more Arcane Lore than her own (<see cref="GearNeedsLore"/>): Arcane Enlightenment joins her bootstrap lines.</param>
    public static EverySpellInput InputOf(GameState state, HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, SaleMemory memory, IReadOnlyList<LearnNeed> planNeeds, IReadOnlySet<uint> onTrip, bool gearNeedsLore)
    {
        var q = state.Self.Qualities;
        var book = state.Self.Spellbook;
        var ladder = LadderLoader.WarMage;
        var catalog = ScrivenerCatalog.Default;
        var cover = BuffBotCover.For(state).Covering;
        var standing = ScrivenerLearnPlan.Standing(q, book, catalog, ladder, cover is not null);
        var own = cover is null ? standing : ScrivenerLearnPlan.Standing(q, book, catalog, ladder); // M6 14 own buffs: her bootstrap with no bot about (she relies on her self-buffs)
        var memo = EverySpellMemory.For(memory);
        var refused = memory.LearnRefused;
        var held = new HashSet<uint>();
        var inventory = state.Self.Inventory;
        foreach (var entry in inventory.Pack)
        {
            if (entry.ContainerType == 0 && state.Objects.TryGet(entry.Guid, out var o) && (o.ItemType & WritableType) != 0)
            {
                held.Add(o.Weenie.WeenieClassId);
            }
        }

        foreach (var side in inventory.SidePacks)
        {
            foreach (var entry in inventory.ItemsIn(side.Guid))
            {
                if (entry.ContainerType == 0 && state.Objects.TryGet(entry.Guid, out var o) && (o.ItemType & WritableType) != 0)
                {
                    held.Add(o.Weenie.WeenieClassId);
                }
            }
        }

        var plan = new Dictionary<string, EverySpellPlanLine>(StringComparer.Ordinal);
        foreach (var line in planNeeds.GroupBy(n => EverySpellPlan.StockLine(catalog, n.Line.Name), StringComparer.Ordinal))
        {
            var top = line.OrderBy(n => ResupplyRun.NeedRank(n.Kind)).First();
            plan[line.Key] = new EverySpellPlanLine(top.Kind, top.Line.Kind, line.Max(n => n.Target));
        }

        return new EverySpellInput
        {
            Knows = book.Knows,
            Fact = id => spells.TryGetValue(id, out var info) ? new EverySpellFact(info.Name, info.School, info.Power, (info.Flags & SpellFlags.Beneficial) != 0) : null,
            Skill = school => GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(school), out var current, out _) ? standing?.SkillOf(school) ?? current : null,
            OwnSkill = school => GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(school), out var current, out _) ? own?.SkillOf(school) ?? current : null,
            BootstrapLines = EverySpellPlan.BootstrapLines(catalog, id => q.TryGetSkill(id, out var s) && s.AdvancementClass >= SkillRules.TrainedClass, gearNeedsLore), // M6 14 own buffs: right after the gates, covered or not; M6 14 own lore: Arcane Enlightenment when her gear needs it
            Catalog = catalog,
            AtHome = wcid => ground.VendorOf(wcid) is not null,
            CastChance = ladder.PlanCastChance,
            BuffChance = ladder.PlanBuffChance,
            PlanLines = plan,
            Covered = line => cover is not null && BuffBotCover.Covers(cover, line),
            GateLines = EverySpellPlan.GateArmourLines(catalog, ladder), // M6 14 gate lines: first, covered or not
            Incoming = ScrivenerLearnPlan.IncomingElements(ground),
            RefusedBefore = id => refused.ContainsKey(id) || memo.Unreadable.ContainsKey(id),
            Held = held.Contains,
            OnTrip = onTrip,
        };
    }

    /// <summary>
    /// M6 14 own lore: true when a piece she wears (the caster aside) or a held piece the gear rules keep for her Arcane Lore asks more lore than her unbuffed Arcane Lore (<see cref="EverySpellPlan.GearNeedsLore"/>);
    /// <paramref name="why"/> says it with its numbers, either way.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="wearJudge">The wear judge the trip published, or null: the held pieces are not asked then.</param>
    /// <param name="why">The reading, in words.</param>
    public static bool GearNeedsLore(GameState state, SelfEquipJudge? wearJudge, out string why)
    {
        var q = state.Self.Qualities;
        var needs = new List<(string, int)>();
        foreach (var w in SelfEquipJudge.Worn(state))
        {
            if ((w.ItemType & SelfEquipJudge.CasterType) == 0 && w.Appraisal?.Int(AppraisalIds.ArcaneLore) is { } d)
            {
                needs.Add((w.Name, d));
            }
        }

        if (wearJudge is { } judge)
        {
            var skills = new QualitiesSkills(q);
            foreach (var want in LoreSpend.Wants(judge, LoreSpend.HeldPieces(state), SelfEquipJudge.Worn(state), skills))
            {
                needs.Add((want.Name, want.Difficulty));
            }
        }

        return EverySpellPlan.GearNeedsLore(needs, new QualitiesSkills(q).Base(MageTemplate.ArcaneLore), out why);
    }

    /// <summary>The spell plan's needs on <paramref name="ground"/> and <paramref name="rung"/>, as the trip's learn plan reads them (<see cref="ScrivenerLearnPlan.Needs"/>).</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="rung">The rung of the ground at her level.</param>
    /// <param name="travelLines">The lines a job adds to the plan (<see cref="JourneyFacts.LearnTravelLines"/>).</param>
    public static IReadOnlyList<LearnNeed> PlanNeeds(GameState state, HuntingGround ground, LadderRung rung, IReadOnlyList<string> travelLines)
    {
        var casters = CasterGain.Of(state, rung.Element is { } rungElement && SelfEquipJudge.ElementBits.TryGetValue(rungElement, out var bits) ? bits : 0);
        return ScrivenerLearnPlan.Needs(ground, rung, LadderLoader.WarMage.PlanCastChance, state.Self.Qualities, state.Self.Spellbook, ScrivenerCatalog.Default, LadderLoader.WarMage, BuffBotCover.For(state).Covering, travelLines, casters);
    }

    /// <summary>
    /// The sale's keep reason for a scroll under the every-spell rule (after <see cref="ScrollKeepRule"/>'s), or null: a scroll whose read ACE refused this session (kept, never bought again), one the rule bought this session, or one
    /// of a line's target level she can cast and lacks (read on the trip, never sold and bought back). The caller answers whether the rule is on at all.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="memory">The session's sale memory.</param>
    /// <param name="item">The pack item.</param>
    /// <param name="input">The rule's inputs (<see cref="InputOf"/>), asked only for a scroll of a spell her book lacks that the rule did not buy: the caller makes them once.</param>
    public static string? Reason(GameState state, SaleMemory memory, WorldObject item, Func<EverySpellInput> input)
    {
        if ((item.ItemType & WritableType) == 0 || (item.Weenie.SpellId ?? ScrivenerCatalog.Default.Stock.ByScroll(item.Weenie.WeenieClassId)?.SpellId) is not { } id)
        {
            return null; // the spell from the wire's description, else the shop's row of the scroll's weenie
        }

        var memo = EverySpellMemory.For(memory);
        if (memo.Unreadable.TryGetValue(id, out var refused))
        {
            return string.Create(CultureInfo.InvariantCulture, $"the every-spell rule: ACE refused its read this session ({refused}): kept, never bought again");
        }

        var book = state.Self.Spellbook;
        if (!book.IsKnown || book.Knows(id) || ScrivenerCatalog.Default.Stock.Spell(id) is not { } spell)
        {
            return null;
        }

        if (memo.Bought.Contains(item.Weenie.WeenieClassId))
        {
            return "the every-spell rule bought it this session: read on the trip";
        }

        if (!state.Self.Qualities.IsKnown)
        {
            return null;
        }

        var pick = EverySpellPlan.Judge(input() with { Held = w => w == item.Weenie.WeenieClassId }, spell.Line);
        return pick.Spell?.SpellId == id && pick.Verdict is EverySpellVerdict.ReadHeld ? string.Create(CultureInfo.InvariantCulture, $"the every-spell rule: {spell.Name} is the target of its line ({pick.Why})") : null;
    }

    /// <summary>
    /// The rule's keep outside a trip, for the session's sale context: on while the job's switch is (<see cref="JourneyFacts.EverySpell"/>), over the plan's needs as they stand on the ground's rung and with nothing read on a
    /// trip. The inputs are made on the first scroll that needs them. Null when the job stated no facts or the switch is off: the rule keeps nothing then, as on a trip.
    /// <para>One named difference from a trip: whether her gear asks more Arcane Lore than her own is read from what she wears alone. The held pieces' wants need the wear judge a trip publishes on the journey's facts
    /// (<c>TODO(engine-wear-terms)</c>, a side channel no new reader is added to), so outside a trip a scroll of Arcane Enlightenment is the target of its line only when a worn piece asks for it.</para>
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="memory">The session's sale memory.</param>
    /// <param name="facts">The job's facts, or null.</param>
    public static Func<WorldObject, string?>? Hook(GameState state, HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, SaleMemory memory, JourneyFacts? facts)
    {
        if (facts is not { EverySpell: true })
        {
            return null;
        }

        var input = new Lazy<EverySpellInput>(() =>
        {
            IReadOnlyList<LearnNeed> needs = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, ground.Key, state.Self.Qualities.Level) is { } rung ? PlanNeeds(state, ground, rung, facts.LearnTravelLines) : [];
            return InputOf(state, ground, spells, memory, needs, new HashSet<uint>(), GearNeedsLore(state, null, out _));
        });
        return item => Reason(state, memory, item, () => input.Value);
    }
}

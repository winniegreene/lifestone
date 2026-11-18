using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>One cantrip's row: its tier and family (the spell's name is "&lt;tier&gt; &lt;family&gt;") and what it does (<see cref="BuffStatMod"/>).</summary>
/// <param name="Id">The spell id.</param>
/// <param name="Tier">Minor, Major, Epic or Legendary.</param>
/// <param name="Family">The family ("War Magic Aptitude").</param>
/// <param name="Mod">The spell table's row.</param>
public readonly record struct CantripRow(uint Id, string Tier, string Family, BuffStatMod Mod)
{
    /// <summary>The spell's name as the world names it.</summary>
    public string Name => Tier + " " + Family;
}

/// <summary>
/// M6 14 (<c>m6-armour</c>): the cantrips the armour and jewellery pick reads, by spell id, from a read-only SELECT of the world's spell table (2026-11-13: <c>SELECT id, name, stat_Mod_Type, stat_Mod_Key, stat_Mod_Val FROM spell</c>
/// for the Minor, Major, Epic and Legendary rows of each family below): never assumed from the name. A skill or attribute row adds (0x9010 skill, 0x9001 attribute: 5, 15, 25, 35); a regeneration row multiplies (0x5008, keys 3 health,
/// 4 stamina, 5 mana: 1.15, 1.3, 1.45, 1.6). The armour cantrips (Impenetrability, Armor) are <see cref="GearScore.StatOf"/>'s.
/// </summary>
public static class CantripRows
{
    private const uint Skill = 0x9010, Attribute = 0x9001, Regen = 0x5008;
    private static readonly string[] Tiers = ["Minor", "Major", "Epic", "Legendary"];

    /// <summary>The rows by spell id.</summary>
    public static IReadOnlyDictionary<uint, CantripRow> BySpell { get; } = Build();

    /// <summary>The row of <paramref name="spell"/>, or null when it is no cantrip the pick reads.</summary>
    /// <param name="spell">The spell id.</param>
    public static CantripRow? Of(uint spell) => BySpell.TryGetValue(spell, out var row) ? row : null;

    private static Dictionary<uint, CantripRow> Build()
    {
        var rows = new Dictionary<uint, CantripRow>();
        void Add(string family, uint type, uint key, uint[] ids, double[] values)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                rows[ids[i]] = new CantripRow(ids[i], Tiers[i], family, new BuffStatMod(ids[i], type, key, values[i]));
            }
        }

        double[] points = [5, 15, 25, 35];
        double[] rates = [1.15, 1.3, 1.45, 1.6];
        Add("War Magic Aptitude", Skill, MageTemplate.WarMagic, [2569, 2534, 4715, 6075], points);
        Add("Life Magic Aptitude", Skill, MageTemplate.LifeMagic, [2555, 2520, 4700, 6060], points);
        Add("Creature Enchantment Aptitude", Skill, MageTemplate.CreatureEnchantment, [2542, 2507, 4689, 6046], points);
        Add("Item Enchantment Aptitude", Skill, MageTemplate.ItemEnchantment, [2551, 2516, 4697, 6056], points);
        Add("Mana Conversion Prowess", Skill, MageTemplate.ManaConversion, [2560, 2525, 4705, 6064], points);
        Add("Arcane Prowess", Skill, MageTemplate.ArcaneLore, [2537, 2502, 4684, 6041], points);
        Add("Magic Resistance", Skill, MageTemplate.MagicDefense, [2559, 2524, 4704, 6063], points);
        Add("Invulnerability", Skill, MageTemplate.MeleeDefense, [2550, 2515, 4696, 6055], points);
        Add("Focus", Attribute, MageTemplate.Focus, [2581, 2574, 3964, 6105], points);
        Add("Willpower", Attribute, MageTemplate.Self, [2584, 2577, 4227, 6101], points);
        Add("Strength", Attribute, 1, [2583, 2576, 3965, 6107], points);
        Add("Endurance", Attribute, 2, [2580, 2573, 4226, 6104], points);
        Add("Mana Gain", Regen, 5, [2627, 2624, 4681, 6078], rates);
        Add("Health Gain", Regen, 3, [2626, 2623, 4680, 6077], rates);
        Add("Stamina Gain", Regen, 4, [2628, 2625, 4682, 6076], rates);
        return rows;
    }
}

/// <summary>M6 14 (<c>m6-armour</c>): the levels of her four armour lines the armour term reads (0: none): Armor Self and her protection on her, Impenetrability and the bane on every worn piece of armour.</summary>
/// <param name="ArmorSelf">Armor Self's level.</param>
/// <param name="Protection">The protection's level.</param>
/// <param name="Impenetrability">Impenetrability's level.</param>
/// <param name="Bane">The bane's level.</param>
public sealed record ArmourLines(int ArmorSelf, int Protection, int Impenetrability, int Bane)
{
    /// <summary>No line.</summary>
    public static ArmourLines None { get; } = new(0, 0, 0, 0);

    /// <summary>The lines a rung's gate holds her to (<see cref="RungGate"/>: Armor Self, Bludgeon Protection Self, Impenetrability, Bludgeon Bane), or <see cref="None"/>.</summary>
    /// <param name="gate">The gate, or null.</param>
    public static ArmourLines Of(RungGate? gate)
    {
        int At(string line) => gate?.Lines.FirstOrDefault(l => string.Equals(l.Line, line, StringComparison.Ordinal))?.Level ?? 0;
        return gate is null ? None : new ArmourLines(At("Armor Self"), At("Bludgeon Protection Self"), At("Impenetrability"), At("Bludgeon Bane"));
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Armor Self {ArmorSelf}, protection {Protection}, Impenetrability {Impenetrability}, bane {Bane}");
}

/// <summary>
/// M6 14 (<c>m6-armour</c>): what the armour and jewellery pick reads besides the pieces and her skills: the ladder's weights, the caster terms (the plan's bolt, the damage and mana weights), the ground's Magic Defense, the rung's hitter,
/// her armour lines, and her load (the capacity, the burden she keeps after a sale, whether the ground has jumps).
/// </summary>
/// <param name="Weights">The ladder's weights.</param>
/// <param name="Caster">The caster score's terms (its weights, its bolt).</param>
/// <param name="TargetMagicDefence">The ground's targets' Magic Defense (the resist roll's difficulty), 0 when not known.</param>
/// <param name="Hitter">The rung's hardest hitter, or null.</param>
/// <param name="Lines">Her armour lines.</param>
public sealed record WearTerms(LadderWearScore Weights, CasterTerms Caster, int TargetMagicDefence, LadderHitter? Hitter, ArmourLines Lines)
{
    /// <summary>Terms of no ground: the default weights, no bolt, no hitter, no line.</summary>
    public static WearTerms Default { get; } = new(LadderWearScore.Default, CasterTerms.Default, 0, null, ArmourLines.None);

    /// <summary>Her burden capacity now (150 x her current Strength), or null.</summary>
    public int? Capacity { get; init; }

    /// <summary>The burden she keeps after a sale: the worn set and the kept pack, no held gear and no loot; null when not read.</summary>
    public int? KeptBurden { get; init; }

    /// <summary>
    /// M6 16 Q10d (<c>TODO(engine-keeper-burden)</c> closed): true for a worn piece that stays in her pack once it is taken off, because the sale keeps it (a great-cantrip piece, a noted or review piece, rotation gear, a leather-set
    /// piece, a caster her gear rules need): its burden is not saved by the swap. Null: none stays (an attuned piece still stays, by its own rule).
    /// </summary>
    public Func<WornPiece, bool>? StaysInPack { get; init; }

    /// <summary>True on a ground whose walk takes a running jump (<see cref="HuntingGround.Jumps"/>): the hard stop of the load.</summary>
    public bool Jumps { get; init; }

    /// <summary>M6 14 capacity: the burden she carries now (her live load, the loot in), or null (the kept burden stands in).</summary>
    public int? Carried { get; init; }

    /// <summary>M6 14 capacity: the loot's load line in percent of the capacity (<see cref="MageLootThresholds.LoadLinePercent"/>).</summary>
    public int LoadLinePercent { get; init; } = MageLootThresholds.DefaultLoadLinePercent;

    /// <summary>M6 14 capacity: her load as the capacity term reads it (<see cref="GearScore.CapacityPart"/>), or null while the capacity or every burden is unread.</summary>
    public CapacityLoad? Load => Capacity is > 0 && (Carried ?? KeptBurden) is { } carried ? new CapacityLoad(Capacity.Value, carried, LoadLinePercent, Weights.Burden) : null;

    /// <summary>Where the hitter and the lines came from, for the log.</summary>
    public string Source { get; init; } = "no ground";

    /// <summary>The terms as the log prints them.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"weights {Weights}; target Magic Defense {TargetMagicDefence}; hitter {(Hitter is { } h ? $"{h.Name} (hit {h.Hit})" : "none")}; lines {Lines}; capacity {Capacity?.ToString(CultureInfo.InvariantCulture) ?? "unread"}, carried {Carried?.ToString(CultureInfo.InvariantCulture) ?? "unread"} (load line {LoadLinePercent}%), kept burden {KeptBurden?.ToString(CultureInfo.InvariantCulture) ?? "unread"}, jumps {(Jumps ? "yes" : "no")}; {Source}");
}

/// <summary>The pick's verdict on one piece: what to do, the pieces it displaces, the gain and its parts, and the two sides' own numbers for the log.</summary>
/// <param name="Kind">Wear (a free slot), Replace (displaces <see cref="Displaced"/>), Review (kept for the next visit) or None.</param>
/// <param name="Candidate">The piece.</param>
/// <param name="Location">Where it goes on.</param>
/// <param name="Displaced">The worn pieces it takes off.</param>
/// <param name="Gain">The set's score with it on, less the set's score now, in percent of the hour.</param>
/// <param name="Parts">The gain's terms.</param>
/// <param name="Why">The decision's reason with the numbers.</param>
public sealed record WearPick(SelfEquipKind Kind, GearCandidate Candidate, uint Location, IReadOnlyList<WornPiece> Displaced, double Gain, IReadOnlyList<GearScorePart> Parts, string Why)
{
    /// <summary>The gain of its cantrips alone (requirements aside: what it would add were its spells on).</summary>
    public double CantripsAsIf { get; init; }

    /// <summary>The candidate's own number: the set with it on, less the set with it off (its displaced pieces off too).</summary>
    public double Own { get; init; }

    /// <summary>The displaced pieces' own number: the set now, less the set with them off.</summary>
    public double DisplacedOwn { get; init; }

    /// <summary>
    /// M6 14 (<c>m6-lore-spend</c>): for a <see cref="SelfEquipKind.Review"/> whose spells stay off for her Arcane Lore alone: the piece's ItemDifficulty, the Arcane Lore that would light them; null otherwise. With it, <see cref="LoreKind"/>,
    /// <see cref="LoreGain"/> and <see cref="LoreMargin"/> are the pick's own verdict at that lore (the experience spend reads them: <see cref="LoreSpend"/>).
    /// </summary>
    public int? LoreNeed { get; init; }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): what the pick would do with her Arcane Lore at <see cref="LoreNeed"/> (Wear or Replace).</summary>
    public SelfEquipKind LoreKind { get; init; }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): the pick's gain at that lore, in percent of her hour.</summary>
    public double LoreGain { get; init; }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): the margin that gain beat (0 in a free slot, the swap margin over a displaced piece).</summary>
    public double LoreMargin { get; init; }

    /// <summary>The value of one term by name, or 0.</summary>
    /// <param name="name">The term's name.</param>
    public double Part(string name) => Parts.FirstOrDefault(p => p.Name == name).Value;

    /// <summary>As the self-equip's choice (the first displaced piece is <see cref="SelfEquipChoice.Replaces"/>, all of them <see cref="SelfEquipChoice.Displaced"/>).</summary>
    public SelfEquipChoice ToChoice() =>
        new(Kind, Candidate.Guid, Candidate.Name, Location, Displaced.Count == 0 ? 0 : Displaced[0].Guid, "wear pick: " + Why) { Displaced = [.. Displaced.Select(d => d.Guid)], LoreNeed = LoreNeed, LoreKind = LoreKind, LoreGain = LoreGain, LoreMargin = LoreMargin };
}

/// <summary>
/// M6 14 (<c>m6-armour</c>; the owner, 2026-11-13: she evaluates the armour she finds as possible upgrades for what she wears, especially with regard to minor and major cantrips): the armour and jewellery pick, in the caster pick's
/// shape and currency (percent of her experience an hour, <see cref="GearScore.Caster"/>). A piece is scored as the set she would wear with it on, against the set she wears: what it displaces comes off (retail's layering: a worn piece
/// whose ClothingPriority overlaps its own, Creature_Equipment.cs:50-58, 93-101 @95ef426d; a ring or bracelet takes a free side, a necklace or trinket its one slot), and the difference is the gain.
/// </summary>
/// <remarks>
/// <para><b>Which spells count.</b> ACE activates a worn piece's spells only when its item mana is above 0 (<c>TryEquipObjectWithNetworking</c>, Player_Inventory.cs:313-315), its activation requirements are met
/// (<c>CheckUseRequirements</c>: Arcane Lore against ItemDifficulty, the skill, level, attribute and heritage limits, WorldObject_Use.cs:191-324; <see cref="SelfEquipJudge.ActivationUnmet"/>) and its mana is not exactly 1 (ACE zeroes it
/// and casts nothing, :338-342); a piece out of mana loses them (ManaConsumersTick, Player_Tick.cs:681-699). Of the cantrips of one family only the strongest counts: ACE reads the top layer of a spell category
/// (<c>GetEnchantments_TopLayer</c>, EnchantmentManager.cs:123-129, the layers by <c>Add</c>, :134-175), and the cantrip families are their own categories, apart from the buffs (the bot's War Magic Mastery Other VII stacks with an
/// aptitude). A piece whose wield requirement is unmet is never worn (<c>CheckWieldRequirement</c>, Player_Inventory.cs:2126-2229).</para>
/// <para><b>The terms</b> (the ladder's <see cref="LadderWearScore"/>, each reason in <c>sources.wear_score</c>): War Magic on the landed share of the plan's bolts (the cast chance at its power, factor 0.07, times the resist roll against
/// the ground's Magic Defense, factor 0.03: <c>SkillCheck.GetSkillChance</c>, SkillCheck.cs:9-22) by the caster score's damage weight; Mana Conversion on the mana model's bolts a mana bar
/// (<see cref="ManaConversionModel.ExpectedCost"/>) by its mana weight; Life, Creature and Item Enchantment by a weight a point; Focus and Willpower through the formulas ((Focus + Self) / 4 to each magic skill, / 6 to Mana Conversion,
/// Self to the maximum mana); mana regeneration by the seconds of presses it saves; armour by the share of the hour her health costs on the rung, cut by the drop in her damage factor (<see cref="ArmorReport"/>'s ACE reading over the
/// ground's damage types); the burden as a cost on her capacity, and a hard stop where her kept load would pass the jump's line (<see cref="MageLootThresholds.JumpLoadLinePercent"/>) on a ground with jumps.</para>
/// </remarks>
public static class WearScore
{
    private const double Eps = 1e-6;
    private const uint MaxManaVital = 5;

    /// <summary>Judges <paramref name="candidate"/> against the worn set (see the class remarks).</summary>
    /// <param name="candidate">The piece, with its appraisal.</param>
    /// <param name="worn">The pieces worn now, with their appraisals.</param>
    /// <param name="context">Her numbers and the ground (<see cref="SelfEquipJudge.Context(IGearSkills)"/>).</param>
    /// <param name="terms">The pick's terms.</param>
    public static WearPick Judge(GearCandidate candidate, IReadOnlyList<WornPiece> worn, GearContext context, WearTerms terms)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terms);
        WearPick None(string why) => new(SelfEquipKind.None, candidate, 0, [], 0, [], why);
        if (candidate.Appraisal is not { IsNotFound: false } a)
        {
            return None("no appraisal of it is held: not judged on a guess");
        }

        var slot = GearScore.SlotOf(candidate.ItemType, candidate.Coverage, candidate.ValidLocations);
        if (slot is not (GearSlot.Armour or GearSlot.Jewellery))
        {
            return None("not armour, clothing or jewellery");
        }

        if (worn.FirstOrDefault(w => w.Appraisal is not { IsNotFound: false } && (w.ItemType & SelfEquipJudge.CasterType) == 0) is { } unread)
        {
            return None(string.Create(CultureInfo.InvariantCulture, $"the worn '{unread.Name}' 0x{unread.Guid:X8} is not appraised: the set is not read whole, nothing is swapped on a guess"));
        }

        var (location, displaced, slotWhy) = Displaces(candidate, worn, context, terms);
        if (location == 0)
        {
            return None(slotWhy);
        }

        var after = worn.Where(w => !displaced.Contains(w)).Append(new WornPiece(candidate.Guid, candidate.Name, candidate.ItemType, location, candidate.Coverage, a)).ToList();
        var wield = SelfEquipJudge.WieldUnmet(a, context.Skills);
        var spellsOff = SpellsOff(a, context.Skills);
        var parts = Delta(worn, after, context, terms, out var cantripNames);
        var asIf = Delta(worn, after, context, terms, out _, candidateAsIf: candidate.Guid);
        var cantripsAsIf = CantripTotal(asIf);
        var gain = parts.Sum(p => p.Name == "load" ? 0 : p.Value);
        var withoutCandidate = worn.Where(w => !displaced.Contains(w)).ToList();
        var own = Delta(withoutCandidate, after, context, terms, out _).Sum(p => p.Name == "load" ? 0 : p.Value);
        var displacedOwn = displaced.Count == 0 ? 0 : Delta(withoutCandidate, worn, context, terms, out _).Sum(p => p.Name == "load" ? 0 : p.Value);

        // M6 16 E5e5 (the owner's decision of 2026-11-18: an unusable great-cantrip piece is kept, and she wears what works): of the worn pieces this one would take off, the keepers whose great cantrip can never light
        // for her (GreatOnBody). They hold no slot by the guard below; the terms already score them on what they give (Tops and DamageFactor read a piece's spells only when they are on); every verdict names them
        var deadNote = string.Empty;
        if (context.UsefulGreat is not null)
        {
            foreach (var d in displaced)
            {
                if (context.GreatOn(d.Guid, d.Appraisal) is { Kind: GreatOnBodyKind.Never } never)
                {
                    deadNote += "; " + GreatOnBody.NoSlot(d, never);
                }
            }
        }

        var numbers = string.Create(
            CultureInfo.InvariantCulture,
            $"gain {gain:0.###}% of the hour = {string.Join(", ", parts)}; '{candidate.Name}' 0x{candidate.Guid:X8} own {own:0.###}{(cantripNames.Count == 0 ? string.Empty : " (" + string.Join(", ", cantripNames) + ")")} [armour level {a.ArmorLevel ?? 0}, burden {a.Burden ?? candidate.Burden}, coverage 0x{candidate.Coverage:X}, ItemDifficulty {a.Int(AppraisalIds.ArcaneLore)?.ToString(CultureInfo.InvariantCulture) ?? "none"} against Arcane Lore {context.Skills.Current(MageTemplate.ArcaneLore)?.ToString(CultureInfo.InvariantCulture) ?? "unread"}, item mana {a.ItemMana?.ToString(CultureInfo.InvariantCulture) ?? "none"}{(GearScore.Attuned(a) ? ", attuned" : string.Empty)}{(GearScore.Bonded(a) ? ", bonded" : string.Empty)}{(spellsOff is null ? string.Empty : ", spells off")}] against {(displaced.Count == 0 ? "a free slot" : string.Join(" + ", displaced.Select(d => $"'{d.Name}' 0x{d.Guid:X8}")) + string.Create(CultureInfo.InvariantCulture, $" own {displacedOwn:0.###}"))}{deadNote}");
        WearPick Pick(SelfEquipKind kind, string why) => new(kind, candidate, location, displaced, gain, parts, why + ": " + numbers) { CantripsAsIf = cantripsAsIf, Own = own, DisplacedOwn = displacedOwn };

        if (wield is not null)
        {
            return cantripsAsIf > terms.Weights.SwapMargin + Eps
                ? Pick(SelfEquipKind.Review, "its cantrips would be worth wearing but " + wield + ": kept for the review at the next visit")
                : Pick(SelfEquipKind.None, "not worn: " + wield);
        }

        if (parts.FirstOrDefault(p => p.Name == "load") is { Value: < 0, Note: { } stop })
        {
            return Pick(SelfEquipKind.None, "refused by the load: " + stop);
        }

        // M6 16 E5e2 (the owner's standing rule, asked of the pieces themselves): a worn piece that casts a great cantrip useful to her is never taken off for a piece that casts none. The terms below score her core cantrips
        // alone, so a worn piece whose great cantrip they do not score (a Major Invulnerability the keep-aside list names, a Major Endurance) is worth nothing to them and any gain would displace it; with every site
        // reading the keep-aside in force, the restore wears such pieces on every ground, and the next visit's pick must not take them off for plain armour
        // M6 16 E5e5: the guard asks the second question of both sides, "does its great cantrip count for her on her body": a worn keeper ACE can never light does not hold its slot (she wears what works), and a
        // candidate whose own great cantrip can never light is no reason to take a working one off
        if (displaced.Count > 0 && context.UsefulGreat is not null && context.GreatOn(candidate.Guid, a) is { HoldsSlot: false } itsOwn && displaced.FirstOrDefault(d => context.GreatOn(d.Guid, d.Appraisal).HoldsSlot) is { } keptOn)
        {
            return Pick(SelfEquipKind.None, string.Create(CultureInfo.InvariantCulture, $"the worn '{keptOn.Name}' 0x{keptOn.Guid:X8} casts a great cantrip useful to her and {GreatOnBody.CastsNone(itsOwn)}: never taken off for it"));
        }

        // M6 14 (the owner, 2026-11-15: the aim is a Minor or better cantrip for her core attributes and skills in every slot; every other built-in spell, and even the armour's own numbers, are secondary): cantrips
        // first. A piece that raises her core cantrips (the cantrip parts: War, Life, Creature and Item, Mana Conversion, and Focus and Willpower through the mana pool and regeneration) is worn whatever its armour; one that
        // lowers them is not; only when they tie do the armour, the burden and the margin decide
        var cantripGain = CantripTotal(parts);
        if (cantripGain > Eps)
        {
            return Pick(displaced.Count == 0 ? SelfEquipKind.Wear : SelfEquipKind.Replace, string.Create(CultureInfo.InvariantCulture, $"cantrips first: it raises her core cantrips by {cantripGain:0.###}% of the hour (the armour and the rest are secondary)"));
        }

        if (cantripGain < -Eps)
        {
            return Pick(SelfEquipKind.None, string.Create(CultureInfo.InvariantCulture, $"cantrips first: it would lower her core cantrips by {-cantripGain:0.###}% of the hour"));
        }

        // M6 14 (the owner, 2026-11-15: cantrips are buffs on top of the spells, and invaluable): the swap margin keeps her from trading one useful piece for another of about the same worth; a displaced piece that brings her
        // nothing (its own number at or under 0: its spells overridden by the active ones, only its burden left) is no worth to protect, so any gain replaces it
        var margin = displaced.Count == 0 || displacedOwn <= Eps ? 0 : terms.Weights.SwapMargin;
        if (gain > margin + Eps)
        {
            return Pick(displaced.Count == 0 ? SelfEquipKind.Wear : SelfEquipKind.Replace, displaced.Count == 0 ? "an upgrade in a free slot" : string.Create(CultureInfo.InvariantCulture, $"beats what it displaces by more than the swap margin {margin:0.##}"));
        }

        // M6 14 (m6-lore-spend; the owner, 2026-11-14: her experience spend raises Arcane Lore when gear she holds needs it): a piece whose spells stay off for her Arcane Lore alone is judged again as the pick would judge it with her lore at
        // its ItemDifficulty (every term, the armour, the burden and the load among them, not the cantrips alone); when the pick would then wear it, it is kept for the review with that verdict, which the experience spend reads (LoreSpend).
        if (spellsOff is not null && SelfEquipJudge.LoreShort(a, context.Skills, out var lore, out var difficulty) is not null)
        {
            var lifted = SelfEquipJudge.WithLore(context.Skills, lore, difficulty);
            if (SpellsOff(a, lifted) is null && Judge(candidate, worn, context with { Skills = lifted }, terms) is { Kind: SelfEquipKind.Wear or SelfEquipKind.Replace } at)
            {
                var atMargin = at.Displaced.Count == 0 ? 0 : terms.Weights.SwapMargin;
                return Pick(SelfEquipKind.Review, string.Create(CultureInfo.InvariantCulture, $"its spells would stay off ({spellsOff}); with Arcane Lore {difficulty} (its ItemDifficulty) the wear pick would {(at.Kind == SelfEquipKind.Wear ? "wear" : "replace with")} it, gain {at.Gain:0.###}% of the hour over the margin {atMargin:0.##}: kept for the review at the next visit while the experience spend raises Arcane Lore"))
                    with { LoreNeed = difficulty, LoreKind = at.Kind, LoreGain = at.Gain, LoreMargin = atMargin };
            }
        }

        if (spellsOff is not null && cantripsAsIf > terms.Weights.SwapMargin + Eps)
        {
            return Pick(SelfEquipKind.Review, "its spells would stay off (" + spellsOff + ") and they would be worth wearing: kept for the review at the next visit");
        }

        return Pick(SelfEquipKind.None, string.Create(CultureInfo.InvariantCulture, $"no gain over the margin {margin:0.##}{(spellsOff is null ? string.Empty : "; its spells would stay off (" + spellsOff + "): no credit for them")}"));
    }

    /// <summary>
    /// M6 14 own lore: what a worn piece whose spells are off for her Arcane Lore alone would bring her lit (re-wielded with her lore at <paramref name="difficulty"/>, its ItemDifficulty: ACE reads the lore only at the wield,
    /// Player_Inventory.cs:313-335 @95ef426d), in percent of the hour: the worn set's terms with her lore there less the set's terms now (the load stop aside). 0 when it brings nothing.
    /// </summary>
    /// <param name="worn">The pieces she wears.</param>
    /// <param name="context">Her numbers and the ground.</param>
    /// <param name="terms">The pick's terms.</param>
    /// <param name="lore">Her Arcane Lore now.</param>
    /// <param name="difficulty">The piece's ItemDifficulty.</param>
    public static double LitGain(IReadOnlyList<WornPiece> worn, GearContext context, WearTerms terms, int lore, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terms);
        var lifted = context with { Skills = SelfEquipJudge.WithLore(context.Skills, lore, difficulty) };
        double Value(GearContext c) => Delta([], worn, c, terms, out _).Sum(p => p.Name == "load" ? 0 : p.Value);
        return Value(lifted) - Value(context);
    }

    /// <summary>Why a piece's spells are off for her (no mana pool, mana 1 or under, an activation requirement unmet), or null when ACE would activate them.</summary>
    /// <param name="a">The piece's appraisal.</param>
    /// <param name="skills">Her numbers.</param>
    public static string? SpellsOff(Appraisal a, IGearSkills skills)
    {
        // (the worn pieces lit on her are read first by their callers: GearContext.LitBy)
        ArgumentNullException.ThrowIfNull(a);
        if (a.Spells.Length == 0)
        {
            return null;
        }

        if (a.ItemMana is not > 0)
        {
            return "no item mana: ACE activates a piece's spells only with mana above 0 (Player_Inventory.cs:313-315)";
        }

        if (a.ItemMana == 1)
        {
            return "item mana 1: ACE zeroes it and casts nothing (Player_Inventory.cs:338-342)";
        }

        return SelfEquipJudge.ActivationUnmet(a, skills);
    }

    /// <summary>
    /// M6 14: true when an enchantment in her registry (<see cref="PlayerQualities.ActiveEnchantments"/>, surpassed ones too) was cast by the piece <paramref name="guid"/>: its spells are on her now (ACE lit them when it was
    /// wielded and keeps them whatever her lore), so the wear pick counts them for a worn piece. Allocation-free.
    /// </summary>
    /// <param name="qualities">Her qualities.</param>
    /// <param name="guid">The worn piece.</param>
    public static bool LitOn(PlayerQualities qualities, uint guid)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        var active = qualities.ActiveEnchantments;
        if (guid == 0 || active.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var e in active)
        {
            if (e.Enchantment.Caster == guid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The worn pieces <paramref name="candidate"/> displaces and where it goes on; location 0 when it cannot go on (why says so).</summary>
    private static (uint Location, List<WornPiece> Displaced, string Why) Displaces(GearCandidate candidate, IReadOnlyList<WornPiece> worn, GearContext context, WearTerms terms)
    {
        if (candidate.Coverage != 0)
        {
            var inTheWay = worn.Where(w => (w.Coverage & candidate.Coverage) != 0).ToList();
            return (candidate.ValidLocations, inTheWay, string.Empty);
        }

        if (SelfEquipJudge.FreeLocation(candidate, worn, out var blocker) is { } free)
        {
            return (free, [], string.Empty);
        }

        var valid = candidate.ValidLocations;
        var holders = worn.Where(w => (w.Location & valid) != 0 && w.Coverage == 0).ToList();
        if (holders.Count == 0)
        {
            return (0, [], blocker);
        }

        // both sides of a ring or bracelet taken: the side whose piece is worth least to her comes off
        var weakest = holders.OrderBy(h => Delta(worn.Where(w => w != h).ToList(), worn, context, terms, out _).Sum(p => p.Name == "load" ? 0 : p.Value))
            .ThenBy(h => context.GreatOn(h.Guid, h.Appraisal).HoldsSlot ? 1 : 0) // M6 16 E5e2, E5e5: of two sides worth the same, the one without a great cantrip that counts for her on her body comes off
            .ThenBy(h => h.Guid).First();
        return (weakest.Location & valid, [weakest], string.Empty);
    }

    private static double CantripTotal(IReadOnlyList<GearScorePart> parts) =>
        parts.Where(p => p.Name is "war magic" or "mana conversion" or "life magic" or "creature" or "item" or "mana pool" or "mana regen" or "capacity").Sum(p => p.Value); // M6 14 capacity: Strength is a basic attribute (the owner's useful cantrips, 2026-11-16)

    /// <summary>
    /// The terms of wearing <paramref name="after"/> in place of <paramref name="before"/>, in percent of the hour. <paramref name="candidateAsIf"/>: that piece's spells count whatever its requirements (the review's question).
    /// </summary>
    private static List<GearScorePart> Delta(IReadOnlyList<WornPiece> before, IReadOnlyList<WornPiece> after, GearContext context, WearTerms terms, out List<string> cantrips, uint candidateAsIf = 0)
    {
        var skills = context.Skills;
        var w = terms.Weights;
        var top0 = Tops(before, skills, 0, context.LitBy);
        var top1 = Tops(after, skills, candidateAsIf, context.LitBy);
        cantrips = [.. top1.Where(t => !top0.TryGetValue(t.Key, out var b) || b.Mod.Value < t.Value.Mod.Value).Select(t => t.Value.Name)];
        double D(uint type, uint key) => Value(top1, type, key) - Value(top0, type, key);
        var focus = D(0x9001, MageTemplate.Focus);
        var self = D(0x9001, MageTemplate.Self);
        var school = (focus + self) / 4.0;
        var dWar = D(0x9010, MageTemplate.WarMagic) + school;
        var dMc = D(0x9010, MageTemplate.ManaConversion) + ((focus + self) / 6.0);
        var parts = new List<GearScorePart>
        {
            War(dWar, context, terms),
            Mana(dMc, context, terms),
            Points("life magic", D(0x9010, MageTemplate.LifeMagic) + school, w.LifeMagic),
            Points("creature", D(0x9010, MageTemplate.CreatureEnchantment) + school, w.CreatureEnchantment),
            Points("item", D(0x9010, MageTemplate.ItemEnchantment) + school, w.ItemEnchantment),
            Pool(self, skills, w),
            Regen(top0, top1, w),
            Armour(before, after, context, terms),
            GearScore.CapacityPart(D(0x9001, GearContext.Strength), terms.Load), // M6 14 capacity: a Strength cantrip's top layer, 150 capacity a point
            Burden(before, after, terms, out var load),
        };
        if (load is { } stop)
        {
            parts.Add(stop);
        }

        return parts;
    }

    /// <summary>The strongest active cantrip of each family on the set (ACE's top layer), by its stat's (type, key).</summary>
    private static Dictionary<(uint, uint), CantripRow> Tops(IReadOnlyList<WornPiece> set, IGearSkills skills, uint asIf, Func<uint, bool>? litBy = null)
    {
        var tops = new Dictionary<(uint, uint), CantripRow>();
        foreach (var piece in set)
        {
            if (piece.Appraisal is not { IsNotFound: false } a || (piece.Guid != asIf && litBy?.Invoke(piece.Guid) != true && SpellsOff(a, skills) is not null))
            {
                continue;
            }

            // M6 14 capacity: the held caster's cantrips are in both sets alike (the caster pick moves it, never this pick), so they add nothing here, but ACE keeps only the top layer of a family across every worn and wielded piece
            // (GetEnchantments_TopLayer, EnchantmentManager.cs:123-129 @95ef426d): a wielded sceptre's Major Strength leaves a pair of Major Strength shoes nothing to add
            if ((piece.ItemType & SelfEquipJudge.CasterType) != 0 && (piece.Location & SelfEquipJudge.Held) == 0)
            {
                continue;
            }

            foreach (var id in a.Spells)
            {
                if (CantripRows.Of(id) is { } row && (!tops.TryGetValue((row.Mod.Type, row.Mod.Key), out var held) || held.Mod.Value < row.Mod.Value))
                {
                    tops[(row.Mod.Type, row.Mod.Key)] = row;
                }
            }
        }

        return tops;
    }

    private static double Value(Dictionary<(uint, uint), CantripRow> tops, uint type, uint key) =>
        tops.TryGetValue((type, key), out var row) ? row.Mod.Value : (type == 0x5008 ? 1.0 : 0.0);

    /// <summary>ACE's <c>SkillCheck.GetSkillChance</c> (SkillCheck.cs:9-22 @95ef426d) on a fractional skill: 1 - 1 / (1 + e^(factor x (skill - difficulty))).</summary>
    private static double Chance(double skill, double difficulty, double factor) => 1.0 - (1.0 / (1.0 + Math.Exp(factor * (skill - difficulty))));

    private static GearScorePart War(double delta, GearContext context, WearTerms terms)
    {
        if (Math.Abs(delta) < Eps)
        {
            return new("war magic", 0);
        }

        if (terms.Caster.Bolt is not { } bolt || context.Skills.Current(MageTemplate.WarMagic) is not { } war)
        {
            const string Todo = "m6-wear-war-unread";
            context.Notes.Once(Todo, "gear: wear pick: TODO(" + Todo + "): no plan bolt or War Magic read: a War Magic cantrip counts nothing");
            return new("war magic", 0, string.Create(CultureInfo.InvariantCulture, $"{delta:+0.##;-0.##} points, TODO({Todo}): no bolt or War Magic read"));
        }

        double Landed(double s) => Chance(s, bolt.Power, 0.07) * Chance(s, terms.TargetMagicDefence, 0.03);
        var before = Landed(war);
        var after = Landed(war + delta);
        var percent = before > 0 ? ((after / before) - 1.0) * 100.0 : 0;
        return new("war magic", terms.Caster.Weights.Damage * percent, string.Create(CultureInfo.InvariantCulture, $"{delta:+0.##;-0.##} on War Magic {war}: {before:0.0000} to {after:0.0000} of {bolt.Name}'s casts land against Magic Defense {terms.TargetMagicDefence} ({percent:+0.##;-0.##}%) x {terms.Caster.Weights.Damage:0.###}"));
    }

    private static GearScorePart Mana(double delta, GearContext context, WearTerms terms)
    {
        if (Math.Abs(delta) < Eps)
        {
            return new("mana conversion", 0);
        }

        var skill = context.Skills.Current(MageTemplate.ManaConversion);
        if (context.Skills.AdvancementClass(MageTemplate.ManaConversion) is > 0 and < (int)SkillRules.TrainedClass)
        {
            return new("mana conversion", 0, "her Mana Conversion is untrained: ACE pays the base (Creature_Magic.cs:41-45)");
        }

        if (terms.Caster.Bolt is not { } bolt || skill is not > 0)
        {
            const string Todo = "m6-wear-mana-unread";
            context.Notes.Once(Todo, "gear: wear pick: TODO(" + Todo + "): no plan bolt or Mana Conversion read: a Mana Conversion cantrip counts nothing");
            return new("mana conversion", 0, string.Create(CultureInfo.InvariantCulture, $"{delta:+0.##;-0.##} points, TODO({Todo})"));
        }

        var before = ManaConversionModel.ExpectedCost(bolt.BaseMana, bolt.Power, (uint)skill.Value);
        var after = ManaConversionModel.ExpectedCost(bolt.BaseMana, bolt.Power, (uint)Math.Max(0, Math.Round(skill.Value + delta)));
        var percent = after > 0 ? ((before / after) - 1.0) * 100.0 : 0;
        return new("mana conversion", terms.Caster.Weights.Mana * percent, string.Create(CultureInfo.InvariantCulture, $"{delta:+0.##;-0.##} on Mana Conversion {skill}: {bolt.Name} {before:0.##} to {after:0.##} mana a cast ({percent:+0.##;-0.##}% bolts a bar) x {terms.Caster.Weights.Mana:0.###}"));
    }

    /// <summary>
    /// M6 14 (<c>m6-lore-spend</c>): what <paramref name="points"/> more of one of her magic skills brings her, in percent of the hour, by the pick's own terms: War Magic on the landed share of the plan's bolts, Mana Conversion on the bolts a
    /// bar, Life, Creature and Item Enchantment by their weights a point (the terms a cantrip of the skill is scored by). Any other skill: 0, named. The Arcane Lore rule weighs a piece's gain against it (<see cref="LoreSpend"/>).
    /// </summary>
    /// <param name="skill">The skill id.</param>
    /// <param name="points">The points added.</param>
    /// <param name="context">Her numbers and the ground.</param>
    /// <param name="terms">The pick's terms.</param>
    public static GearScorePart SkillPoints(uint skill, double points, GearContext context, WearTerms terms)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terms);
        var w = terms.Weights;
        return skill switch
        {
            MageTemplate.WarMagic => War(points, context, terms),
            MageTemplate.ManaConversion => Mana(points, context, terms),
            MageTemplate.LifeMagic => Points("life magic", points, w.LifeMagic),
            MageTemplate.CreatureEnchantment => Points("creature", points, w.CreatureEnchantment),
            MageTemplate.ItemEnchantment => Points("item", points, w.ItemEnchantment),
            _ => new(SelfEquipJudge.SkillName(skill), 0, "no term of the pick weighs it"),
        };
    }

    private static GearScorePart Points(string name, double delta, double weight) =>
        Math.Abs(delta) < Eps ? new(name, 0) : new(name, weight * delta, string.Create(CultureInfo.InvariantCulture, $"{delta:+0.##;-0.##} points x {weight:0.###}"));

    private static GearScorePart Pool(double self, IGearSkills skills, LadderWearScore w)
    {
        if (Math.Abs(self) < Eps)
        {
            return new("mana pool", 0);
        }

        var max = skills.VitalMax(MaxManaVital);
        if (max is not > 0)
        {
            return new("mana pool", 0, "her maximum mana is unread: not weighed");
        }

        var percent = self / max.Value * 100.0;
        return new("mana pool", w.ManaPool * percent, string.Create(CultureInfo.InvariantCulture, $"Self {self:+0.##;-0.##} on a bar of {max} ({percent:+0.##;-0.##}%) x {w.ManaPool:0.###}"));
    }

    private static GearScorePart Regen(Dictionary<(uint, uint), CantripRow> top0, Dictionary<(uint, uint), CantripRow> top1, LadderWearScore w)
    {
        var (r0, r1) = (Value(top0, 0x5008, 5), Value(top1, 0x5008, 5));
        if (Math.Abs(r1 - r0) < Eps)
        {
            return new("mana regen", 0);
        }

        var gain = (r1 / r0) - 1.0;
        return new("mana regen", 100.0 * w.ManaRegenASecond * gain * w.ManaPointSeconds, string.Create(CultureInfo.InvariantCulture, $"x{r0:0.##} to x{r1:0.##} on {w.ManaRegenASecond:0.##} mana a second at {w.ManaPointSeconds:0.###} s a point"));
    }

    /// <summary>
    /// Her damage factor on <paramref name="set"/>: each part of the body by its share of the hits (<see cref="ArmorReport.HitShares"/>), the mean over the ground's damage types (the seven when it gives none) of ACE's
    /// <c>66.67 / (66.67 + armour)</c>, the part's armour being Armor Self and the strongest body-armour cantrip plus, for each piece that covers it, <c>(ArmorLevel + Impenetrability) x clamp(protection + bane, -2, 2)</c>
    /// (Monster_Melee.cs:409-452, 490-530 @95ef426d); her lines' Impenetrability and bane on every piece of armour (her pass casts them on what she wears), a piece's own stronger Impenetrability in their place while its spells are on.
    /// </summary>
    /// <param name="set">The worn pieces.</param>
    /// <param name="context">Her numbers and the ground's damage types.</param>
    /// <param name="lines">Her armour lines.</param>
    public static double DamageFactor(IReadOnlyList<WornPiece> set, GearContext context, ArmourLines lines)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lines);
        var types = context.DamageTypes is { Count: > 0 } given ? given : GearScore.StubDamageTypes;
        var body = 0.0;
        var pieces = new List<(uint Coverage, int Level, double Impen, Appraisal A)>();
        foreach (var w in set)
        {
            if (w.Appraisal is not { IsNotFound: false } a || (w.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                continue;
            }

            var on = context.LitBy?.Invoke(w.Guid) == true || SpellsOff(a, context.Skills) is null; // M6 14: a worn piece lit on her stays lit whatever her lore now
            var own = 0.0;
            foreach (var id in a.Spells)
            {
                if (!on)
                {
                    break; // its spells are off: no Impenetrability or Armor of its own
                }

                if (GearScore.StatOf(id) is { } row && row.Type == 0x9004 && row.Key == 28)
                {
                    own = Math.Max(own, row.Value);
                }
                else if (GearScore.StatOf(id) is { Type: 0xA080 } bodyRow)
                {
                    body = Math.Max(body, bodyRow.Value);
                }
            }

            var level = a.ArmorLevel ?? 0;
            var coverage = GearCoverage.OfWorn(w.Coverage, w.Location);
            if (coverage != 0 && (level > 0 || own > 0))
            {
                pieces.Add((coverage, level, Math.Max(level > 0 ? ArmorReport.ImpenetrabilityAt(lines.Impenetrability) : 0, own), a));
            }
        }

        var factor = 0.0;
        foreach (var (_, coverage, share) in ArmorReport.HitShares)
        {
            var mean = 0.0;
            foreach (var type in types)
            {
                // M6 14 (the owner, 2026-11-15: an item's Armor Self V is overridden by the active spells): her Armor Self line and a worn item's Armor Self are one category, and ACE applies its top layer
                // (GetEnchantments_TopLayer, EnchantmentManager.cs:123-129 @95ef426d): the higher of the two, never their sum
                var armour = Math.Max(ArmorReport.ArmorSelfAt(lines.ArmorSelf), body);
                foreach (var p in pieces)
                {
                    if ((p.Coverage & coverage) != 0)
                    {
                        armour += (p.Level + p.Impen) * Math.Clamp(GearScore.ModVs(p.A, type, out _) + (p.Level > 0 ? ArmorReport.BaneAt(lines.Bane) : 0), -2.0, 2.0);
                    }
                }

                mean += armour > 0 ? ArmorReport.ArmorMod / (ArmorReport.ArmorMod + armour) : armour < 0 ? 1.0 - (armour / ArmorReport.ArmorMod) : 1.0;
            }

            factor += share * mean / types.Count;
        }

        return factor;
    }

    private static GearScorePart Armour(IReadOnlyList<WornPiece> before, IReadOnlyList<WornPiece> after, GearContext context, WearTerms terms)
    {
        var f0 = DamageFactor(before, context, terms.Lines);
        var f1 = DamageFactor(after, context, terms.Lines);
        if (Math.Abs(f1 - f0) < 1e-9)
        {
            return new("armour", 0);
        }

        if (terms.Hitter is not { } hitter)
        {
            return new("armour", 0, string.Create(CultureInfo.InvariantCulture, $"damage factor {f0:0.0000} to {f1:0.0000}, but the rung names no hitter: not weighed"));
        }

        var w = terms.Weights;
        var hit = hitter.Hit * f0 * ArmorReport.ProtectionAt(terms.Lines.Protection);
        var share = 100.0 * w.AttacksASecond * hit * w.HealthPointSeconds;
        var cut = 1.0 - (f1 / f0);
        return new("armour", share * cut, string.Create(CultureInfo.InvariantCulture, $"damage factor {f0:0.0000} to {f1:0.0000} ({cut * 100:+0.##;-0.##}% less) of a health share of {share:0.##}% ({hitter.Name}'s {hitter.Hit} to {hit:0.#} a hit, {w.AttacksASecond:0.##} a second, {w.HealthPointSeconds:0.###} s a point)"));
    }

    /// <summary>The burden's cost, and the load stop (<paramref name="stop"/>, a negative part named "load") where the kept load after the swap passes the jump's line on a ground with jumps.</summary>
    private static GearScorePart Burden(IReadOnlyList<WornPiece> before, IReadOnlyList<WornPiece> after, WearTerms terms, out GearScorePart? stop)
    {
        stop = null;
        static int BurdenOf(WornPiece p) => p.Appraisal?.Burden ?? 0;

        // a piece taken off goes to the pack rules and is sold; an attuned one cannot be sold or dropped (Player_Inventory.cs:1389-1393 @95ef426d), so its burden stays; so does that of a piece the sale keeps (terms.StaysInPack)
        var on = after.Where(p => !before.Contains(p)).Sum(BurdenOf);
        var off = before.Where(p => !after.Contains(p) && !GearScore.Attuned(p.Appraisal) && terms.StaysInPack?.Invoke(p) != true).Sum(BurdenOf);
        var delta = on - off;
        var capacity = terms.Capacity;
        if (capacity is not > 0)
        {
            return new("burden", 0, string.Create(CultureInfo.InvariantCulture, $"{delta:+0;-0} burden, capacity unread: not weighed"));
        }

        var percent = (double)delta / capacity.Value * 100.0;
        if (terms.Jumps && delta > 0 && terms.KeptBurden is { } kept)
        {
            var line = capacity.Value * MageLootThresholds.JumpLoadLinePercent / 100;
            var load = kept + delta;
            if (load > line)
            {
                stop = new("load", -1, string.Create(CultureInfo.InvariantCulture, $"her kept load {kept} {delta:+0;-0} = {load} passes the jump's line {line} ({MageLootThresholds.JumpLoadLinePercent}% of {capacity}): past it the flight falls under the pit's need"));
            }
        }

        return delta == 0 ? new("burden", 0) : new("burden", -terms.Weights.Burden * percent, string.Create(CultureInfo.InvariantCulture, $"{delta:+0;-0} of {capacity} ({percent:+0.##;-0.##}%) x {terms.Weights.Burden:0.###}"));
    }
}

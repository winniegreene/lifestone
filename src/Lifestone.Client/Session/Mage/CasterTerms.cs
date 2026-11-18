using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>M6 14 (<c>m6-caster</c>): the bolt the caster score runs through the mana model: the plan's bolt line at the level her live War Magic casts at the plan's chance, with its base mana and power (the SpellTable's).</summary>
/// <param name="Name">The spell's name.</param>
/// <param name="BaseMana">Its base mana.</param>
/// <param name="Power">Its power (Mana Conversion's difficulty, <see cref="Casting.ManaConversionModel"/>).</param>
public sealed record CasterBolt(string Name, uint BaseMana, uint Power);

/// <summary>
/// M6 14 (<c>m6-caster</c>): the three Item Enchantment auras she keeps on herself for her caster (the highest level of each the spellbook holds: the learn plan buys the level she casts at the buff bar), with what each adds by the
/// pinned world SQL's spell rows (<see cref="BuffStatMods"/>). On this server they are "Aura of ... Self" spells: cast on her, they land in her own enchantments (WorldObject_Magic.cs:2180-2184 @95ef426d, not redirected: Spell.cs:279-312), and ACE
/// reads them as the wielder's when it reads the wielded caster: Hermetic Link (WeaponAuraManaConv 171, x1.1 to x1.6) multiplies the caster's own ManaConversionMod, so a caster with none gains nothing (WorldObject_Weapon.cs:247-259);
/// Defender (WeaponAuraDefense 169, +0.03 to +0.15) adds to its melee defence modifier (:123-126); Spirit Drinker (WeaponAuraElemental 170, +0.01 a level) adds to its ElementalDamageMod only when the caster's element is the spell's (:384-395).
/// Each needs the caster to be enchantable (ResistMagic under 9999, :532).
/// </summary>
/// <param name="HermeticLink">The level of Aura of Hermetic Link Self held (0: none).</param>
/// <param name="Defender">The level of Aura of Defender Self held (0: none).</param>
/// <param name="SpiritDrinker">The level of Aura of Spirit Drinker Self held (0: none).</param>
public sealed record CasterAuras(int HermeticLink, int Defender, int SpiritDrinker)
{
    /// <summary>The profile lines' names (<see cref="ArenaProfile"/>), in the learn plan's order.</summary>
    public static IReadOnlyList<string> LineNames { get; } = ["Spirit Drinker", "Hermetic Link", "Defender"];

    /// <summary>No aura held.</summary>
    public static CasterAuras None { get; } = new(0, 0, 0);

    /// <summary>The multiplier Hermetic Link puts on the caster's ManaConversionMod (1 with none).</summary>
    public double ManaConversionFactor => Row("Hermetic Link", HermeticLink, 1.0);

    /// <summary>What Defender adds to the caster's melee defence modifier (0 with none).</summary>
    public double DefenceAdd => Row("Defender", Defender, 0.0);

    /// <summary>What Spirit Drinker adds to an on-element caster's ElementalDamageMod (0 with none).</summary>
    public double ElementalAdd => Row("Spirit Drinker", SpiritDrinker, 0.0);

    /// <summary>The level of <paramref name="line"/> held (0: none).</summary>
    /// <param name="line">One of <see cref="LineNames"/>.</param>
    public int LevelOf(string line) => line switch { "Hermetic Link" => HermeticLink, "Defender" => Defender, "Spirit Drinker" => SpiritDrinker, _ => 0 };

    /// <summary>The auras the spellbook holds: the highest level I to VI of each line.</summary>
    /// <param name="book">The spellbook.</param>
    public static CasterAuras Of(Spellbook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return new CasterAuras(Highest(book, "Hermetic Link"), Highest(book, "Defender"), Highest(book, "Spirit Drinker"));
    }

    /// <summary>The spell id of <paramref name="line"/> at <paramref name="level"/> (1 to 6), or 0.</summary>
    /// <param name="line">One of <see cref="LineNames"/>.</param>
    /// <param name="level">The level.</param>
    public static uint SpellOf(string line, int level) => ArenaProfile.LineNamed(line) is { } l && level is >= 1 and <= 6 && level <= l.Spells.Count ? l.Spells[level - 1].Id : 0;

    /// <summary>The auras as the log prints them.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Hermetic Link {Roman(HermeticLink)} (x{ManaConversionFactor:0.##}), Defender {Roman(Defender)} (+{DefenceAdd:0.###}), Spirit Drinker {Roman(SpiritDrinker)} (+{ElementalAdd:0.##})");

    private static string Roman(int level) => level == 0 ? "none" : ScrivenerLearnPlan.Numeral(level);

    private static int Highest(Spellbook book, string line)
    {
        for (var level = 6; level >= 1; level--)
        {
            if (SpellOf(line, level) is var id and not 0 && book.Knows(id))
            {
                return level;
            }
        }

        return 0;
    }

    private static double Row(string line, int level, double none) =>
        level > 0 && SpellOf(line, level) is var id and not 0 && BuffStatMods.BySpell.TryGetValue(id, out var row) ? row.Value : none;
}

/// <summary>
/// M6 14 (<c>m6-caster</c>): whether each caster aura adds anything to a caster she holds and can wield (wielded, or in her packs with its wield requirements met), so the learn plan buys only a line that pays: Hermetic Link
/// multiplies the caster's own ManaConversionMod, so it needs a caster with one (ACE: "enchantments are multiplicative, so they are only effective if there is a base mod", WorldObject_Weapon.cs:246-259 @95ef426d); Spirit Drinker adds
/// only on a caster of the spell's element (:384-395), so it needs a caster of the plan's element (the Fire Baton, on the day her base War Magic meets its 290: the pick then counts the aura, <see cref="GearScore.Caster"/>);
/// Defender adds to any caster's melee defence (:123-126), so it needs a caster. Read from the state, never assumed; a caster not yet appraised is not counted.
/// </summary>
/// <param name="Read">False when the casters were not read (the plan then buys none of the three, named).</param>
/// <param name="ManaConversion">The best ManaConversionMod of a caster she holds and can wield, 0 when none has one.</param>
/// <param name="ManaConversionCaster">That caster's name, or null.</param>
/// <param name="OnElementCaster">The name of a caster of the plan's element she holds and can wield, or null.</param>
/// <param name="Casters">How many casters she holds and can wield.</param>
public sealed record CasterGain(bool Read, double ManaConversion, string? ManaConversionCaster, string? OnElementCaster, int Casters)
{
    /// <summary>The casters were not read.</summary>
    public static CasterGain Unread { get; } = new(false, 0, null, null, 0);

    /// <summary>Why <paramref name="line"/> adds nothing to a caster she holds, or null when it adds something.</summary>
    /// <param name="line">The catalog line's name (<see cref="CasterAuras.LineNames"/>).</param>
    public string? NoGain(string line)
    {
        if (!Read)
        {
            return "the casters she holds are not read: no caster aura is bought on an assumption";
        }

        if (Casters == 0)
        {
            return "she holds no caster she can wield: the aura would add to nothing";
        }

        return line switch
        {
            "Hermetic Link" when ManaConversion <= 0 => "no caster she holds and can wield has a Mana Conversion bonus: Hermetic Link multiplies that bonus, so it adds nothing (WorldObject_Weapon.cs:246-259 @95ef426d)",
            "Spirit Drinker" when OnElementCaster is null => "no caster she holds and can wield is of the plan's element: Spirit Drinker adds only on the caster's own element (WorldObject_Weapon.cs:384-395 @95ef426d)",
            _ => null,
        };
    }

    /// <summary>What it gains on, for the plan's line (<paramref name="line"/>): the caster named.</summary>
    /// <param name="line">The catalog line's name.</param>
    public string On(string line) => line switch
    {
        "Hermetic Link" => string.Create(CultureInfo.InvariantCulture, $"'{ManaConversionCaster}' has a Mana Conversion bonus of {ManaConversion:0.###}"),
        "Spirit Drinker" => $"'{OnElementCaster}' is of the plan's element",
        _ => string.Create(CultureInfo.InvariantCulture, $"{Casters} caster(s) she can wield"),
    };

    /// <summary>The facts of the casters <paramref name="state"/> shows she holds (wielded and in the packs).</summary>
    /// <param name="state">The read model.</param>
    /// <param name="element">The plan's element (ACE DamageType bits), 0 when none.</param>
    public static CasterGain Of(GameState state, uint element)
    {
        ArgumentNullException.ThrowIfNull(state);
        var skills = new QualitiesSkills(state.Self.Qualities);
        var guids = state.Self.Inventory.Equipped.Select(e => e.Guid).Concat(PackLayout.Items(state).Select(i => i.Guid)).Distinct();
        var (best, bestName, onElement, count) = (0.0, (string?)null, (string?)null, 0);
        foreach (var guid in guids)
        {
            if (!state.Objects.TryGet(guid, out var o) || (o.ItemType & SelfEquipJudge.CasterType) == 0 || !state.Appraisals.TryGet(guid, out var a) || a.IsNotFound || SelfEquipJudge.WieldUnmet(a, skills) is not null)
            {
                continue;
            }

            count++;
            var mcm = SelfEquipJudge.ManaConversion(a);
            if (mcm > best)
            {
                (best, bestName) = (mcm, o.Name);
            }

            if (element != 0 && a.Int(SelfEquipJudge.DamageTypeInt) is { } type && (uint)type == element)
            {
                onElement ??= o.Name;
            }
        }

        return new CasterGain(true, best, bestName, onElement, count);
    }
}

/// <summary>
/// M6 14 (<c>m6-caster</c>): what the caster score (<see cref="GearScore.Caster"/>) reads besides the caster and her skills: the ladder's weights (<see cref="LadderCasterScore"/>), the plan's bolt for the mana model, and the auras she keeps.
/// </summary>
/// <param name="Weights">The weights.</param>
/// <param name="Bolt">The plan's bolt, or null when no SpellTable or rung gives one (the mana term then counts the skill's percent alone, <c>TODO(m6-caster-bolt-unread)</c>, noted once).</param>
/// <param name="Auras">The auras she keeps.</param>
public sealed record CasterTerms(LadderCasterScore Weights, CasterBolt? Bolt, CasterAuras Auras)
{
    /// <summary>
    /// M6 14 casterfair (the owner's ruling, 2026-11-17: the hermetic link is a spell she casts already): her live caster auras and the caster she wields, which ACE folds into that caster's appraisal (<see cref="CasterFold"/>); the score takes them back
    /// out so every caster is scored on its own numbers. <see cref="CasterFold.None"/> when not read.
    /// </summary>
    public CasterFold Fold { get; init; } = CasterFold.None;

    /// <summary>The terms with the default weights, no bolt and no aura (a judge that is given none: a test's, the rearm's).</summary>
    public static CasterTerms Default { get; } = new(LadderCasterScore.Default, null, CasterAuras.None);

    /// <summary>
    /// The terms of the character now: <paramref name="weights"/> (the ladder's), the bolt of <paramref name="element"/>'s line (<see cref="SpellPlan.BoltLine"/>) at the highest level the book holds whose chance on her current War Magic is at
    /// least <paramref name="chance"/> (the fight's own cap rule), its base mana from <paramref name="spells"/>, and the auras the book holds. <paramref name="why"/> names the bolt or why there is none.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="spells">The SpellTable, or null.</param>
    /// <param name="element">The judge's war element (ACE DamageType bits, <see cref="SelfEquipJudge.ElementOf(string?, string?, out string)"/>), 0 when none.</param>
    /// <param name="weights">The weights.</param>
    /// <param name="chance">The plan's cast chance.</param>
    /// <param name="why">The bolt's source.</param>
    public static CasterTerms Read(GameState state, IReadOnlyDictionary<uint, SpellInfo>? spells, uint element, LadderCasterScore weights, double chance, out string why)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(weights);
        return new CasterTerms(weights, BoltOf(state, spells, ElementName(element), chance, out why), CasterAuras.Of(state.Self.Spellbook)) { Fold = CasterFold.Of(state) };
    }

    /// <summary>
    /// The terms of the war mage's ladder for <paramref name="state"/>: its <see cref="Ladder.CasterScore"/> and <see cref="Ladder.PlanCastChance"/> (<see cref="Read"/>), logged by the caller with <paramref name="why"/>.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="spells">The SpellTable, or null.</param>
    /// <param name="element">The judge's war element bits.</param>
    /// <param name="why">The bolt's source.</param>
    public static CasterTerms OfLadder(GameState state, IReadOnlyDictionary<uint, SpellInfo>? spells, uint element, out string why)
    {
        var ladder = LadderLoader.WarMage;
        return Read(state, spells, element, ladder.CasterScore, ladder.PlanCastChance, out why);
    }

    /// <summary>The ladder's name of an element's DamageType bits ("fire" for 0x10), or null.</summary>
    /// <param name="element">The bits.</param>
    public static string? ElementName(uint element)
    {
        foreach (var (name, bits) in SelfEquipJudge.ElementBits)
        {
            if (bits == element)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>The plan's bolt (see <see cref="Read"/>), or null with the reason.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="spells">The SpellTable, or null.</param>
    /// <param name="rungElement">The rung's element, or null.</param>
    /// <param name="chance">The plan's cast chance.</param>
    /// <param name="why">The bolt's source, or why there is none.</param>
    public static CasterBolt? BoltOf(GameState state, IReadOnlyDictionary<uint, SpellInfo>? spells, string? rungElement, double chance, out string why)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (SpellPlan.BoltLine(rungElement, spells, out var lineWhy) is not { } line)
        {
            why = lineWhy;
            return null;
        }

        if (!GearCasts.TryCurrentSkill(state.Self.Qualities, MageTemplate.WarMagic, out var war, out var noWar))
        {
            why = "War Magic is not read: " + noWar;
            return null;
        }

        var cap = SpellPlan.LevelAt(war, chance, [.. line.Select(s => s.Power)]);
        for (var level = Math.Min(cap, line.Count); level >= 1; level--)
        {
            var spell = line[level - 1];
            if (!state.Self.Spellbook.Knows(spell.Id))
            {
                continue;
            }

            if (spells is null || !spells.TryGetValue(spell.Id, out var info))
            {
                why = string.Create(CultureInfo.InvariantCulture, $"{spell.Name} ({spell.Id}) is not in the SpellTable given: its base mana is unread");
                return null;
            }

            why = string.Create(CultureInfo.InvariantCulture, $"{spell.Name}: the highest level of the line the book holds at a chance of {chance:0.00} on War Magic {war}");
            return new CasterBolt(spell.Name, info.BaseMana, info.Power);
        }

        why = string.Create(CultureInfo.InvariantCulture, $"the book holds no level of the line at a chance of {chance:0.00} on War Magic {war}");
        return null;
    }

    /// <summary>The terms as the log prints them.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"weights {Weights}; bolt {(Bolt is { } b ? $"{b.Name} (base mana {b.BaseMana}, power {b.Power})" : "unread")}; auras {Auras}");
}

/// <summary>
/// M6 14 casterfair: what ACE folds into the appraisal of the caster she wields (<c>AppraiseInfo.AddPropertyEnchantments</c>, Network/Structure/AppraiseInfo.cs:415-453 @95ef426d): the wielder's item-enchantment auras on her
/// (<c>ResistMaskHelper.GetManaConversionMod</c> and <c>GetElementalDamageBonus</c>, Network/Enum/ResistMask.cs:127-145, the wielder's term; <c>Wielder.EnchantmentManager.GetDefenseMod</c> for WeaponDefense), which a caster in her pack does
/// not show (no wielder). Read from her registry: each stat's enchantments by spell category, the strongest of a category (ACE's top layer, EnchantmentManager.cs:123-129), the categories summed (Defender, Spirit Drinker: additive)
/// or multiplied (Hermetic Link).
/// </summary>
/// <param name="WieldedCaster">The caster she wields, 0 when none.</param>
/// <param name="Defence">Her auras' WeaponAuraDefense (key 169), added to the wielded caster's WeaponDefense.</param>
/// <param name="ManaFactor">Her auras' WeaponAuraManaConv (key 171), multiplying its ManaConversionMod (1 with none).</param>
/// <param name="Elemental">Her auras' WeaponAuraElemental (key 170), added to its ElementalDamageMod.</param>
public sealed record CasterFold(uint WieldedCaster, double Defence, double ManaFactor, double Elemental)
{
    /// <summary>ACE's stat keys of the caster auras and cantrips: WeaponAuraDefense 169, WeaponAuraElemental 170, WeaponAuraManaConv 171.</summary>
    public const uint DefenceKey = 169, ElementalKey = 170, ManaKey = 171;

    /// <summary>Nothing read: no caster wielded, no aura.</summary>
    public static CasterFold None { get; } = new(0, 0, 1, 0);

    /// <summary>Her fold now (see the class remarks).</summary>
    /// <param name="state">The read model.</param>
    public static CasterFold Of(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        uint wielded = 0;
        foreach (var e in state.Self.Inventory.Equipped)
        {
            if ((e.Location & SelfEquipJudge.Held) != 0 && state.Objects.TryGet(e.Guid, out var o) && (o.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                wielded = e.Guid;
            }
        }

        var all = state.Self.Qualities.Enchantments;
        return all.IsDefaultOrEmpty
            ? None with { WieldedCaster = wielded }
            : new CasterFold(wielded, Sum(all, DefenceKey), Product(all, ManaKey), Sum(all, ElementalKey));
    }

    private static IEnumerable<double> TopByCategory(System.Collections.Immutable.ImmutableArray<Lifestone.Net.Messages.EnchantmentDesc> all, uint key) =>
        all.Where(e => e.StatModKey == key).GroupBy(e => e.SpellCategory).Select(g => (double)g.Max(e => e.StatModValue));

    private static double Sum(System.Collections.Immutable.ImmutableArray<Lifestone.Net.Messages.EnchantmentDesc> all, uint key) => TopByCategory(all, key).Sum();

    private static double Product(System.Collections.Immutable.ImmutableArray<Lifestone.Net.Messages.EnchantmentDesc> all, uint key) => TopByCategory(all, key).Aggregate(1.0, (p, v) => p * v);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"wielded 0x{WieldedCaster:X8}, her auras on her: defence +{Defence:0.###}, mana conversion x{ManaFactor:0.###}, elemental +{Elemental:0.###}");
}

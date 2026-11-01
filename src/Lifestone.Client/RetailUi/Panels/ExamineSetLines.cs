using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The examine window's set line (M5 7 polish, D137): <c>ItemExamineUI::Appraisal_ShowSet</c> (11.6096 0x004AEC00) and, as a Lifestone addition behind the caller's option, the pieces of the set worn and the set's spells by pieces from the
/// SpellTable's sets (<see cref="SpellTableData.SpellSets"/>). Pure: the appraisal in, the lines out. <see cref="ExamineRatingLines.SetAndRatings"/> places them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail.</b> <c>Appraisal_ShowSet</c> asks for PropertyInt 265 (ACE <c>EquipmentSetId</c>, an <c>[AssessmentProperty]</c>: <c>ACE.Entity/Enum/Properties/PropertyInt.cs:384-385</c>, sent by <c>AppraiseInfo.cs:350-352</c>) and names the set
/// from its own table, a jump table at 0x004AF204 over the ids 4 to 130 whose cases each set a literal (0x007B0628 to 0x007B109C, read from the 11.6096 executable; <see cref="SetName"/>); it writes "Set: " + the name, single-spaced, and
/// answers true. Ids 34 and 42 to 48 and any id outside 4 to 130 fall to its default: no line (ACE's sets 131 to 140, the society and Paragon sets, are not in 11.6096's table). That is all the retail window says of a set: the client
/// unpacks the SpellTable's sets (<c>SpellSet::UnPack</c>) and never reads them in a window.
/// </para>
/// <para>
/// <b>The addition.</b> <see cref="WornLines"/> says how many pieces of the set the player wears and which spells the set casts at each count, marking the count in force, as the server chooses them: ACE takes the worn items of the same
/// set, counts them (or, for a set of levelled items, sums their item levels), caps the count at the highest tier and uses the highest tier at or below it (<c>ACE.Server/WorldObjects/WorldObject_Set.cs:149-182</c>, the gaps filled by
/// <c>ACE.DatLoader/Entity/SpellSet.cs:19-35</c>). It is not retail: the caller shows it only behind its own option, off by default.
/// </para>
/// </remarks>
public static class ExamineSetLines
{
    /// <summary>PropertyInt 265 (ACE <c>EquipmentSetId</c>): the set the item belongs to.</summary>
    public const uint EquipmentSetId = 265;

    /// <summary>PropertyInt 320 (ACE <c>ItemXpStyle</c>): above 0, the item levels, and its set's tiers are keyed by the worn pieces' summed item levels (<c>WorldObject_Set.cs:159-169</c>).</summary>
    public const uint ItemXpStyle = 320;

    /// <summary>The lowest set id retail's table names.</summary>
    public const int FirstNamedSet = 4;

    /// <summary>
    /// <c>Appraisal_ShowSet</c>'s names (11.6096 0x004AEC00), by set id from <see cref="FirstNamedSet"/>: null where its jump table (0x004AF204) goes to the default case (no line). Several cloak ids share a name, as in retail (53, 67, 74, 75
    /// and 79 are "Weave of Light Weapons"; 54, 57 and 77 "Weave of Missile Weapons").
    /// </summary>
    private static readonly string?[] Names =
    [
        "Carraida's Benediction", "Noble Relic", "Ancient Relic", "Alduressa Relic", "Shou-jen", "Empyrean Rings", "Arm, Mind, Heart", "Coat of Perfect Light", "Leggings of Perfect Light", // 4-12
        "Soldier's", "Adept's", "Archer's", "Defender's", "Tinker's", "Crafter's", "Hearty", "Dexterous", "Wise", "Swift", "Hardened", "Reinforced", "Interlocking", // 13-25
        "Flame Proof", "Acid Proof", "Cold Proof", "Lightning Proof", "Dedication", "Gladiatorial Clothing", "Ceremonial Clothing", "Protective Clothing", null, // 26-34
        "Sigil of Defense", "Sigil of Destruction", "Sigil of Fury", "Sigil of Growth", "Sigil of Vigor", "Heroic Protector", "Heroic Destroyer", // 35-41
        null, null, null, null, null, null, null, // 42-48
        "Weave of Alchemy", "Weave of Arcane Lore", "Weave of Armor Tinkering", "Weave of Assess Person", "Weave of Light Weapons", "Weave of Missile Weapons", "Weave of Cooking", // 49-55
        "Weave of Creature Enchantment", "Weave of Missile Weapons", "Weave of Finesse Weapons", "Weave of Deception", "Weave of Fletching", "Weave of Healing", "Weave of Item Enchantment", // 56-62
        "Weave of Item Tinkering", "Weave of Leadership", "Weave of Life Magic", "Weave of Loyalty", "Weave of Light Weapons", "Weave of Magic Defense", "Weave of Magic Item Tinkering", // 63-69
        "Weave of Mana Conversion", "Weave of Melee Defense", "Weave of Missile Defense", "Weave of Salvaging", "Weave of Light Weapons", "Weave of Light Weapons", "Weave of Heavy Weapons", // 70-76
        "Weave of Missile Weapons", "Weave of Two Handed Combat", "Weave of Light Weapons", "Weave of Void Magic", "Weave of War Magic", "Weave of Weapon Tinkering", "Weave of Assess Creature", // 77-83
        "Weave of Dirty Fighting", "Weave of Dual Wield", "Weave of Recklessness", "Weave of Shield", "Weave of Sneak Attack", "Shou-jen Shozoku", "Weave of Summoning", // 84-90
        "Shrouded Soul", "Darkened Mind", "Clouded Spirit", // 91-93
        "Minor Stinging Shrouded Soul", "Minor Sparking Shrouded Soul", "Minor Smoldering Shrouded Soul", "Minor Shivering Shrouded Soul", // 94-97
        "Minor Stinging Darkened Mind", "Minor Sparking Darkened Mind", "Minor Smoldering Darkened Mind", "Minor Shivering Darkened Mind", // 98-101
        "Minor Stinging Clouded Spirit", "Minor Sparking Clouded Spirit", "Minor Smoldering Clouded Spirit", "Minor Shivering Clouded Spirit", // 102-105
        "Major Stinging Shrouded Soul", "Major Sparking Shrouded Soul", "Major Smoldering Shrouded Soul", "Major Shivering Shrouded Soul", // 106-109
        "Major Stinging Darkened Mind", "Major Sparking Darkened Mind", "Major Smoldering Darkened Mind", "Major Shivering Darkened Mind", // 110-113
        "Major Stinging Clouded Spirit", "Major Sparking Clouded Spirit", "Major Smoldering Clouded Spirit", "Major Shivering Clouded Spirit", // 114-117
        "Blackfire Stinging Shrouded Soul", "Blackfire Sparking Shrouded Soul", "Blackfire Smoldering Shrouded Soul", "Blackfire Shivering Shrouded Soul", // 118-121
        "Blackfire Stinging Darkened Mind", "Blackfire Sparking Darkened Mind", "Blackfire Smoldering Darkened Mind", "Blackfire Shivering Darkened Mind", // 122-125
        "Blackfire Stinging Clouded Spirit", "Blackfire Sparking Clouded Spirit", "Blackfire Smoldering Clouded Spirit", "Blackfire Shivering Clouded Spirit", // 126-129
        "Shimmering Shadows", // 130
    ];

    /// <summary>The highest set id retail's table names.</summary>
    public static int LastNamedSet => FirstNamedSet + Names.Length - 1;

    /// <summary><c>Appraisal_ShowSet</c>'s name for <paramref name="setId"/>, or null where retail writes no line.</summary>
    public static string? SetName(int setId) => setId >= FirstNamedSet && setId <= LastNamedSet ? Names[setId - FirstNamedSet] : null;

    /// <summary>The item's set id (PropertyInt 265), or null.</summary>
    public static int? SetIdOf(Appraisal a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.Int(EquipmentSetId);
    }

    /// <summary><c>Appraisal_ShowSet</c> (11.6096 0x004AEC00): "Set: " and the set's name, or null (no set, or one retail's table does not name).</summary>
    /// <param name="a">The appraisal.</param>
    public static string? SetLine(Appraisal a) => SetIdOf(a) is { } id && SetName(id) is { } name ? "Set: " + name : null;

    /// <summary>
    /// How many of the worn items belong to <paramref name="setId"/> (the addition's count; ACE's is the same: the equipped items of that set, <c>Player_Spells.cs:131</c>). The client learns an item's set only from its appraisal, so
    /// a worn item not yet appraised is null in <paramref name="wornItemSetIds"/> and counted in <c>Unknown</c>, not guessed.
    /// </summary>
    /// <param name="setId">The examined item's set.</param>
    /// <param name="wornItemSetIds">One entry per worn item: its PropertyInt 265 from its appraisal (0 when the appraisal has none), or null when the item has not been appraised.</param>
    public static (int Worn, int Unknown) CountWorn(int setId, IEnumerable<int?> wornItemSetIds)
    {
        ArgumentNullException.ThrowIfNull(wornItemSetIds);
        int worn = 0, unknown = 0;
        foreach (var id in wornItemSetIds)
        {
            if (id is null)
            {
                unknown++;
            }
            else if (id == setId)
            {
                worn++;
            }
        }

        return (worn, unknown);
    }

    /// <summary>
    /// The tier in force for <paramref name="count"/> pieces (or summed item levels): the highest tier key at or below the count capped at the highest tier, or null below the lowest (<c>WorldObject_Set.cs:171-177</c> over
    /// <c>SpellSet.cs:27-34</c>).
    /// </summary>
    /// <param name="tiers">The set's tiers: key → the spells cast at that key.</param>
    /// <param name="count">The pieces worn (or the summed item levels).</param>
    public static uint? ActiveTier(IReadOnlyDictionary<uint, IReadOnlyList<uint>> tiers, int count)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        uint? best = null;
        foreach (var key in tiers.Keys)
        {
            if (count >= 0 && key <= (uint)count && (best is null || key > best))
            {
                best = key;
            }
        }

        return best;
    }

    /// <summary>
    /// The addition (not retail; shown only behind the caller's option): "Pieces worn: N" and, for each of the set's tiers in the SpellTable in order, "K pieces: spell, spell", the tier in force marked " (active)". A set of levelled items
    /// (<see cref="ItemXpStyle"/> above 0) is keyed by summed item levels the window cannot know yet: its tiers read "Level K: ..." with no count and no mark, and a named <c>TODO(m5-examine-set-levels)</c> is logged. Empty when the item has
    /// no set retail names; a set the SpellTable lacks gives the count alone and a named <c>TODO(m5-examine-set-table)</c>.
    /// </summary>
    /// <param name="a">The examined item's appraisal.</param>
    /// <param name="piecesWorn">The pieces of its set the player wears (<see cref="CountWorn"/>).</param>
    /// <param name="spellSets">The SpellTable's sets (<see cref="SpellTableData.SpellSets"/>).</param>
    /// <param name="spell">The SpellTable lookup that names the spells; a spell it lacks shows as "Spell N" with a logged <c>TODO(m5-examine-spell-name)</c>.</param>
    /// <param name="log">Where a named TODO goes.</param>
    /// <param name="unknownPieces">Worn items not yet appraised (<see cref="CountWorn"/>'s <c>Unknown</c>): with any, the count reads "N or more" and a <c>TODO(m5-examine-set-unappraised)</c> is logged.</param>
    public static IReadOnlyList<string> WornLines(
        Appraisal a,
        int piecesWorn,
        IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>> spellSets,
        Func<uint, SpellInfo?>? spell,
        ILogSink log,
        int unknownPieces = 0)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(spellSets);
        ArgumentNullException.ThrowIfNull(log);
        if (SetIdOf(a) is not { } setId || SetName(setId) is null)
        {
            return [];
        }

        var lines = new List<string>(6);
        var leveled = a.Int(ItemXpStyle) is > 0;
        if (leveled)
        {
            Todo(log, "m5-examine-set-levels", $"set {setId} is of levelled items: its tiers are keyed by the worn pieces' summed item levels, which the examine window does not compute; no tier is marked");
        }
        else
        {
            if (unknownPieces > 0)
            {
                Todo(log, "m5-examine-set-unappraised", $"{unknownPieces} worn item(s) have not been appraised, so their sets are unknown; the count of set {setId} is a floor");
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Pieces worn: {piecesWorn}{(unknownPieces > 0 ? " or more" : string.Empty)}"));
        }

        if (!spellSets.TryGetValue(unchecked((uint)setId), out var tiers))
        {
            Todo(log, "m5-examine-set-table", $"set {setId} is not among the SpellTable's sets; its spells are not shown");
            return lines;
        }

        var active = leveled || unknownPieces > 0 ? null : ActiveTier(tiers, piecesWorn);
        foreach (var key in tiers.Keys.Order())
        {
            var spells = tiers[key];
            if (spells.Count == 0)
            {
                continue;
            }

            var names = new List<string>(spells.Count);
            foreach (var id in spells)
            {
                names.Add(SpellName(id, spell, log));
            }

            var head = leveled
                ? string.Create(CultureInfo.InvariantCulture, $"Level {key}")
                : string.Create(CultureInfo.InvariantCulture, $"{key} piece{(key == 1 ? string.Empty : "s")}");
            lines.Add($"{head}: {string.Join(", ", names)}{(key == active ? " (active)" : string.Empty)}");
        }

        return lines;
    }

    private static string SpellName(uint id, Func<uint, SpellInfo?>? spell, ILogSink log)
    {
        if (spell?.Invoke(id) is { Name.Length: > 0 } info)
        {
            return info.Name;
        }

        Todo(log, "m5-examine-spell-name", $"spell {id} is not in the SpellTable (or the table has not loaded), so the examine panel shows its number");
        return "Spell " + id.ToString(CultureInfo.InvariantCulture);
    }

    private static void Todo(ILogSink log, string name, string text) => log.Warn("retailui", $"placeholder: TODO({name}): {text}");
}

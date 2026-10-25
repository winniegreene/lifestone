using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The Holtburg Brawler's and the Holtburg Archer's spell profile as data (M4 P5a lane C1, owner decision D33): the self-buffs they cast on themselves and the utility spells their vitals keeper and heal tests use. An <see cref="IBuffProfile"/>, so the buff cycle
/// and the re-buff driver cast it as they cast the mage's. Ids, names, powers, durations and schools are the SpellTable's (<c>FighterProfileDatTests</c> pins them as <c>ArenaProfileTests</c> pins the mage's). Immutable, no I/O. Holtburg Hunter has no profile.
/// </summary>
/// <remarks>
/// <para><b>The buff lines</b> (twelve, <see cref="ProfileLine.IsBuff"/>): the six attribute self-buffs (Strength, Endurance, Coordination, Quickness, Focus, Willpower Self), the weapon mastery (Heavy Weapon Mastery Self for the Brawler, Missile Weapon Mastery Self for the Archer),
/// Invulnerability, Impregnability, Armor, Regeneration and Rejuvenation Self. The cycle casts Focus and Willpower first (they raise the Creature Enchantment and Life Magic every later cast rolls against), then the mastery, then the rest in this order.
/// The lines the mage shares are the mage's own <see cref="ProfileLine"/> objects (<see cref="ArenaProfile.LineNamed"/>); the weapon masteries and the two utility lines are new here.</para>
/// <para><b>The utility lines</b> (not buffs, never in the cycle): <see cref="StaminaToMana"/> and <see cref="Revitalize"/> (the cycle's mana lines), <see cref="HarmSelf"/> and <see cref="HealSelf"/> (by <see cref="UtilityLine"/> or the properties).
/// Harm Self is a Life Magic Boost that takes health away (power 15, 75, 125, 175, 225, 275, 325 for I-VII, 400 for VIII), Heal Self gives it back (1, 50, 100, 150, 200, 250, 300, 400).</para>
/// <para><b>The level rule.</b> Nothing here picks a level: the cycle (and the keeper) pick the highest level whose cast chance (<c>GearCasts.Chance</c>) reaches 0.9. A Brawler or Archer at Creature Enchantment 183 and Life Magic 183 (level
/// <see cref="FighterTemplate.MagicLevel"/>, unbuffed) casts level IV (power 150); a lower skill falls back to III (power 100, skill 132) or lower.</para>
/// <para><b>The Missile Weapon Mastery Self line</b> has three level-VII rows in the dat (2207, 2219, 2313: one per old missile skill, all category 19, all power 300, all "Missile Weapon Mastery Self VII"); the profile pins 2207, the VII that
/// follows the VI of ids 467-472 in the dat's own order (the other two do the same thing to the same skill). Level VIII (the dat's "Incantation of ...", power 400) is listed for the new lines (4624, 4522, 4311, Harm Self 4309) and is out of reach at skill 185: the level rule never picks it.</para>
/// </remarks>
public sealed class FighterProfile : IBuffProfile
{
    /// <summary>The line names of the utility spells (the Boost and Transfer lines are <see cref="BuffProfile.BoostLineName"/> and <see cref="BuffProfile.TransferLineName"/>).</summary>
    public const string HarmSelfLineName = "Harm Self", HealSelfLineName = "Heal Self", LifestoneRecallLineName = "Lifestone Recall", LifestoneTieLineName = "Lifestone Tie";

    /// <summary>The line names of the two weapon masteries.</summary>
    public const string HeavyMasteryLineName = "Heavy Weapon Mastery Self", MissileMasteryLineName = "Missile Weapon Mastery Self";

    /// <summary>The buff lines both fighters share, in the profile's order: the six attributes, then (after the weapon mastery) the defences, armour and the two regenerations.</summary>
    private static readonly string[] AttributeLines = ["Strength Self", "Endurance Self", "Coordination Self", "Quickness Self", "Focus Self", "Willpower Self"];

    private static readonly string[] DefenceLines = ["Invulnerability Self", "Impregnability Self", "Armor Self", "Regeneration Self", "Rejuvenation Self"];

    private static ProfileLine HeavyMastery { get; } = Line(HeavyMasteryLineName, MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
        S(418, "Heavy Weapon Mastery Self I", 1, 1800),
        S(419, "Heavy Weapon Mastery Self II", 50, 1800),
        S(420, "Heavy Weapon Mastery Self III", 100, 1800),
        S(421, "Heavy Weapon Mastery Self IV", 150, 1800),
        S(422, "Heavy Weapon Mastery Self V", 200, 1800),
        S(423, "Heavy Weapon Mastery Self VI", 250, 2700),
        S(2309, "Heavy Weapon Mastery Self VII", 300, 3600),
        S(4624, "Incantation of Heavy Weapon Mastery Self", 400, 5400));

    private static ProfileLine MissileMastery { get; } = Line(MissileMasteryLineName, MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
        S(467, "Missile Weapon Mastery Self I", 1, 1800),
        S(468, "Missile Weapon Mastery Self II", 50, 1800),
        S(469, "Missile Weapon Mastery Self III", 100, 1800),
        S(470, "Missile Weapon Mastery Self IV", 150, 1800),
        S(471, "Missile Weapon Mastery Self V", 200, 1800),
        S(472, "Missile Weapon Mastery Self VI", 250, 2700),
        S(2207, "Missile Weapon Mastery Self VII", 300, 3600),
        S(4522, "Incantation of Missile Weapon Mastery Self", 400, 5400));

    private static ProfileLine HealLine { get; } = Line(HealSelfLineName, MagicSchool.LifeMagic, ProfileRole.HealSelf,
        S(6, "Heal Self I", 1, 0),
        S(1157, "Heal Self II", 50, 0),
        S(1158, "Heal Self III", 100, 0),
        S(1159, "Heal Self IV", 150, 0),
        S(1160, "Heal Self V", 200, 0),
        S(1161, "Heal Self VI", 250, 0),
        S(2073, "Adja's Intervention", 300, 0),
        S(4311, "Incantation of Heal Self", 400, 0));

    private readonly BuffProfile _profile;

    private FighterProfile(FighterTemplate template, ProfileLine mastery)
    {
        Template = template;
        WeaponMastery = mastery;
        List<ProfileLine> lines = [];
        lines.AddRange(AttributeLines.Select(Mage));
        lines.Add(mastery);
        lines.AddRange(DefenceLines.Select(Mage));
        lines.Add(Mage(BuffProfile.TransferLineName));
        lines.Add(Mage(BuffProfile.BoostLineName));
        HarmSelf = ArenaProfile.ExtraNamed(HarmSelfLineName) ?? throw new InvalidOperationException("the mage profile has no extra " + HarmSelfLineName);
        HealSelf = HealLine;
        LifestoneRecall = ArenaProfile.ExtraNamed(LifestoneRecallLineName) ?? throw new InvalidOperationException("the mage profile has no extra " + LifestoneRecallLineName);
        LifestoneTie = ArenaProfile.ExtraNamed(LifestoneTieLineName) ?? throw new InvalidOperationException("the mage profile has no extra " + LifestoneTieLineName);
        lines.Add(HarmSelf);
        lines.Add(HealSelf);
        lines.Add(LifestoneRecall);
        lines.Add(LifestoneTie);
        _profile = new BuffProfile(template.Name, lines, ["Focus Self", "Willpower Self"], []);
    }

    /// <summary>The Holtburg Brawler's profile (Heavy Weapon Mastery Self).</summary>
    public static FighterProfile Brawler { get; } = new(FighterTemplate.Melee, HeavyMastery);

    /// <summary>The Holtburg Archer's profile (Missile Weapon Mastery Self).</summary>
    public static FighterProfile Archer { get; } = new(FighterTemplate.Archer, MissileMastery);

    /// <summary>Both profiles, Brawler then Archer.</summary>
    public static IReadOnlyList<FighterProfile> All { get; } = [Brawler, Archer];

    /// <summary>The profile of the fighter template <paramref name="template"/>, or null (the Hunter has none).</summary>
    public static FighterProfile? For(FighterTemplate? template) => All.FirstOrDefault(p => p.Template == template);

    /// <summary>The profile of the character named <paramref name="characterName"/> (case-insensitive), or null for any other.</summary>
    public static FighterProfile? ForName(string? characterName) => All.FirstOrDefault(p => string.Equals(p.CharacterName, characterName?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The fighter template the profile is for.</summary>
    public FighterTemplate Template { get; }

    /// <summary>The weapon mastery line: Heavy Weapon Mastery Self (Brawler) or Missile Weapon Mastery Self (Archer).</summary>
    public ProfileLine WeaponMastery { get; }

    /// <summary>Harm Self, I-VIII (<see cref="ProfileRole.HarmSelf"/>): takes health away from the caster (the same line the mage learns, <see cref="ArenaProfile.Extras"/>).</summary>
    public ProfileLine HarmSelf { get; }

    /// <summary>Heal Self, I-VIII (<see cref="ProfileRole.HealSelf"/>): gives health back to the caster.</summary>
    public ProfileLine HealSelf { get; }

    /// <summary>Lifestone Recall (spell 1635, <see cref="ProfileRole.Travel"/>; an Item Enchantment spell of power 100, no duration, so a skill of 132 casts it at the 0.9 chance rule; M4 P5a C1c, owner decision D38): takes the caster to the lifestone it is bound to. The same line the mage learns (<see cref="ArenaProfile.Extras"/>); not a buff, never in the cycle.</summary>
    public ProfileLine LifestoneRecall { get; }

    /// <summary>Lifestone Tie (spell 2644, <see cref="ProfileRole.Travel"/>; M4 P5b J3b, W0): cast at Holtburg's lifestone it sets ACE's LinkedLifestone, the point Lifestone Recall reads (WorldObject_Magic.cs:966-976 and :1087-1093 at 95ef426d; using the lifestone only sets the Sanctuary). The same line the mage learns (<see cref="ArenaProfile.Extras"/>); a travel line, never in the buff cycle.</summary>
    public ProfileLine LifestoneTie { get; }

    /// <summary>Stamina to Mana Self, I-VIII (<see cref="ProfileRole.ManaTransfer"/>): the cycle's mana line.</summary>
    public ProfileLine StaminaToMana => _profile.ManaTransferLine;

    /// <inheritdoc/>
    public bool TransfersAhead => true;

    /// <summary>Revitalize Self, I-VIII (<see cref="ProfileRole.StaminaRestore"/>): the cycle's stamina line.</summary>
    public ProfileLine Revitalize => _profile.StaminaRestoreLine;

    /// <summary>The six utility lines: Stamina to Mana Self, Revitalize Self, Harm Self, Heal Self, Lifestone Recall, Lifestone Tie (the lines that are no buff).</summary>
    public IReadOnlyList<ProfileLine> UtilityLines => [StaminaToMana, Revitalize, HarmSelf, HealSelf, LifestoneRecall, LifestoneTie];

    /// <summary>The utility line named <paramref name="name"/> ("Stamina to Mana Self", "Revitalize Self", "Harm Self", "Heal Self", "Lifestone Recall" or "Lifestone Tie"), or null.</summary>
    public ProfileLine? UtilityLine(string name) => UtilityLines.FirstOrDefault(l => l.Name == name);

    /// <inheritdoc/>
    public string CharacterName => _profile.CharacterName;

    /// <inheritdoc/>
    public IReadOnlyList<ProfileLine> Lines => _profile.Lines;

    /// <inheritdoc/>
    public IReadOnlyList<ProfileLine> BuffOrder => _profile.BuffOrder;

    /// <inheritdoc/>
    public IReadOnlyList<uint> SpellsToLearn => _profile.SpellsToLearn;

    /// <inheritdoc/>
    public ProfileLine ManaTransferLine => _profile.ManaTransferLine;

    /// <inheritdoc/>
    public ProfileLine StaminaRestoreLine => _profile.StaminaRestoreLine;

    /// <inheritdoc/>
    public ProfileLine? LineNamed(string name) => _profile.LineNamed(name);

    /// <inheritdoc/>
    public ProfileSpell? Spell(uint id) => _profile.Spell(id);

    /// <inheritdoc/>
    public override string ToString() => $"{CharacterName}: {Lines.Count} lines, {Lines.Count(l => l.IsBuff)} buffs, {SpellsToLearn.Count} spells";

    private static ProfileLine Mage(string name) => ArenaProfile.LineNamed(name) ?? throw new InvalidOperationException("the mage profile has no line " + name);

    private static ProfileLine Line(string name, MagicSchool school, ProfileRole role, params ProfileSpell[] spells) =>
        new(name, name, school, role, [.. spells.Select((s, i) => s with { Position = i + 1 })]);

    private static ProfileSpell S(uint id, string name, uint power, double duration) => new(0, id, name, power, duration);
}

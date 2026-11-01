using System.Collections.Frozen;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// A character's buff profile (M4 P5a lane C1, owner decision D33): the spell lines the buff cycle casts on the character, in the order it casts them, and the lines it needs to keep casting. <see cref="ArenaProfile.Buffs"/> is the mage's;
/// <see cref="Fighter.FighterProfile"/> is the Brawler's and the Archer's. <see cref="BuffCycle"/>, <see cref="RebuffDriver"/> (through its cycle) and <see cref="BuffProfiles"/> read this and nothing mage-specific.
/// </summary>
public interface IBuffProfile
{
    /// <summary>The character the profile is for (the name the read model holds).</summary>
    string CharacterName { get; }

    /// <summary>Every line of the profile: the buffs (<see cref="ProfileLine.IsBuff"/>), the two mana lines and, for a fighter, the utility lines.</summary>
    IReadOnlyList<ProfileLine> Lines { get; }

    /// <summary>The buff lines in the order the cycle casts them: the profile's leading lines first, then the other "Mastery Self" lines, then the rest in the profile's order.</summary>
    IReadOnlyList<ProfileLine> BuffOrder { get; }

    /// <summary>Every spell id the build teaches the character with <c>spell learn</c>, in line order then list order (the lines first, then the extras); a line learned by play (<see cref="ProfileLine.ByPlay"/>) is not among them.</summary>
    IReadOnlyList<uint> SpellsToLearn { get; }

    /// <summary>The line named <paramref name="name"/> (the profile's name for it), or null.</summary>
    ProfileLine? LineNamed(string name);

    /// <summary>The spell with id <paramref name="id"/> in one of the lines, or null.</summary>
    ProfileSpell? Spell(uint id);

    /// <summary>The Life Magic Transfer line the cycle restores mana with (<see cref="BuffProfile.TransferLineName"/>).</summary>
    ProfileLine ManaTransferLine { get; }

    /// <summary>The Life Magic Boost line the cycle restores stamina with after a transfer (<see cref="BuffProfile.BoostLineName"/>).</summary>
    ProfileLine StaminaRestoreLine { get; }

    /// <summary>
    /// True (M4 P5a C4; a fighter's profile): the ordered pass casts the mana transfer <b>ahead</b> when the mana left after the next buff would not pay for another transfer, so a small mana pool (a fighter's Max Mana is about a
    /// quarter of the mage's) never runs dry below the transfer's own cost and into a regeneration wait. False (the default, the mage's): the transfer is cast only when the next buff needs more mana than is left.
    /// </summary>
    bool TransfersAhead => false;
}

/// <summary>
/// The plain data-backed <see cref="IBuffProfile"/> that both profiles are built on: a character name, its lines, the names of the lines the cycle casts first, and the extra spell ids the build teaches after the lines. Immutable, no I/O.
/// </summary>
public sealed class BuffProfile : IBuffProfile
{
    /// <summary>The line the cycle restores mana with (Life Magic, Transfer): the name every profile uses.</summary>
    public const string TransferLineName = "Stamina to Mana Self";

    /// <summary>The line the cycle restores stamina with after a transfer (Life Magic, Boost): the name every profile uses.</summary>
    public const string BoostLineName = "Revitalize Self";

    private readonly FrozenDictionary<string, ProfileLine> _byName;
    private readonly FrozenDictionary<uint, ProfileSpell> _spells;

    /// <summary>A profile over <paramref name="lines"/>.</summary>
    /// <param name="characterName">The character's name.</param>
    /// <param name="lines">The lines, in the profile's order (names unique).</param>
    /// <param name="leadingLines">The names of the buff lines the cycle casts first, in order (each must be a buff line of <paramref name="lines"/>): the ones that raise the skills the later casts roll against.</param>
    /// <param name="extraSpells">The ids the build teaches after the lines' (the fight and travel spells that are no line).</param>
    public BuffProfile(string characterName, IReadOnlyList<ProfileLine> lines, IReadOnlyList<string> leadingLines, IReadOnlyList<uint> extraSpells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(leadingLines);
        ArgumentNullException.ThrowIfNull(extraSpells);
        CharacterName = characterName;
        Lines = [.. lines];
        _byName = Lines.ToFrozenDictionary(l => l.Name);
        _spells = Lines.SelectMany(l => l.Spells).DistinctBy(s => s.Id).ToFrozenDictionary(s => s.Id);
        BuffOrder = Order(Lines, leadingLines);
        SpellsToLearn = [.. Lines.Where(l => !l.ByPlay).SelectMany(l => l.Spells).Select(s => s.Id), .. extraSpells]; // M5 7 P7j: a line learned by play is never granted
        ManaTransferLine = LineNamed(TransferLineName) ?? throw new InvalidOperationException("the profile of " + characterName + " has no line " + TransferLineName);
        StaminaRestoreLine = LineNamed(BoostLineName) ?? throw new InvalidOperationException("the profile of " + characterName + " has no line " + BoostLineName);
    }

    /// <inheritdoc/>
    public string CharacterName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ProfileLine> Lines { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ProfileLine> BuffOrder { get; }

    /// <inheritdoc/>
    public IReadOnlyList<uint> SpellsToLearn { get; }

    /// <inheritdoc/>
    public ProfileLine ManaTransferLine { get; }

    /// <inheritdoc/>
    public ProfileLine StaminaRestoreLine { get; }

    /// <inheritdoc/>
    public ProfileLine? LineNamed(string name) => _byName.GetValueOrDefault(name);

    /// <inheritdoc/>
    public ProfileSpell? Spell(uint id) => _spells.GetValueOrDefault(id);

    /// <summary>
    /// The cycle's order (the owner's dependency order, M3): <paramref name="leading"/> first, then every other buff line whose name ends in "Mastery Self", then the other buffs in the profile's order. The mage's order is
    /// Focus, Willpower, Creature Enchantment Mastery, the other masteries, then the rest.
    /// </summary>
    private static List<ProfileLine> Order(IReadOnlyList<ProfileLine> lines, IReadOnlyList<string> leading)
    {
        var order = new List<ProfileLine>();
        foreach (var name in leading)
        {
            var line = lines.FirstOrDefault(l => l.Name == name && l.IsBuff) ?? throw new InvalidOperationException("the profile has no buff line " + name);
            order.Add(line);
        }

        foreach (var line in lines)
        {
            if (line.IsBuff && line.Name.EndsWith("Mastery Self", StringComparison.Ordinal) && !order.Contains(line))
            {
                order.Add(line);
            }
        }

        foreach (var line in lines)
        {
            if (line.IsBuff && !order.Contains(line))
            {
                order.Add(line);
            }
        }

        return order;
    }
}

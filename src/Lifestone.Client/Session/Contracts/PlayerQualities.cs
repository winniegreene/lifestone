using System.Collections.Immutable;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The player's qualities (<see cref="SelfState.Qualities"/>; owner decision D16): the properties, attributes, vitals and
/// skills GameEvent 0x0013 PlayerDescription sets and the PrivateUpdate messages change. Keys are ACE's property, attribute,
/// vital and skill ids. Empty until the description arrives; <see cref="QualitiesChanged"/> follows every change.
/// </summary>
/// <remarks>Read by the vitals and character-info panels and by physics' motion weenie (run and jump skills).</remarks>
public sealed class PlayerQualities
{
    /// <summary>No qualities. Only the session creates one.</summary>
    internal PlayerQualities()
    {
    }

    /// <summary>True once a PlayerDescription arrived.</summary>
    public bool IsKnown { get; internal set; }

    private uint _serverOptions1;
    private uint _serverOptions2;
    private uint _overrideMask1;
    private uint _overrideMask2;
    private uint _overrideValue1;
    private uint _overrideValue2;

    /// <summary>
    /// The character's option bits (PlayerDescription's first options word, <c>PlayerModule::options_</c>; 0 until the description arrives) with the player's session-local changes
    /// applied (<see cref="SetSessionOption"/>). Always the merged word, whatever order a reader runs in.
    /// </summary>
    public uint CharacterOptions1 => (_serverOptions1 & ~_overrideMask1) | (_overrideValue1 & _overrideMask1);

    /// <summary>
    /// The character's second option word (the PlayerDescription's second options word; 0 until the description arrives) with the player's session-local changes applied. The options panel
    /// (docs/retail-ui.md section 21.8) changes both words for the session only (D42), through <c>IActionApi.SetSessionOption</c>.
    /// </summary>
    public uint CharacterOptions2 => (_serverOptions2 & ~_overrideMask2) | (_overrideValue2 & _overrideMask2);

    /// <summary>The reducer stores the PlayerDescription's option words (the server's values; the session-local changes stay applied on top).</summary>
    internal void SetServerOptions(uint options1, uint options2)
    {
        _serverOptions1 = options1;
        _serverOptions2 = options2;
    }

    /// <summary>The reducer applies a session-local change of one option bit (nothing goes to the server).</summary>
    /// <param name="word">1 or 2.</param>
    /// <param name="mask">The option's bit.</param>
    /// <param name="value">Whether the option is on.</param>
    internal void SetSessionOption(int word, uint mask, bool value)
    {
        if (word == 1)
        {
            _overrideMask1 |= mask;
            _overrideValue1 = value ? _overrideValue1 | mask : _overrideValue1 & ~mask;
        }
        else
        {
            _overrideMask2 |= mask;
            _overrideValue2 = value ? _overrideValue2 | mask : _overrideValue2 & ~mask;
        }
    }

    /// <summary>Forgets the session-local option changes (the character left the world, or the session was reset).</summary>
    internal void ClearSessionOptions()
    {
        _overrideMask1 = _overrideMask2 = _overrideValue1 = _overrideValue2 = 0;
    }

    /// <summary>The option bit <c>PlayerModule::SideBySideVitals</c> (11.4186 0x005D3070, 11.6096 0x005D4070) tests: bit 21.</summary>
    public const uint SideBySideVitalsOption = 0x200000;

    /// <summary>True when the character's options ask for the side-by-side vitals window (<see cref="SideBySideVitalsOption"/>); false for the stacked one.</summary>
    public bool SideBySideVitals => (CharacterOptions1 & SideBySideVitalsOption) != 0;

    /// <summary>
    /// The option bit <c>PlayerModule::VividTargetingIndicator</c> (11.4186 0x005D2EE0, 11.6096 0x005D3EE0) tests: bit 15. The 11.6096 body was read from the exe (D55; the bytes are
    /// <c>mov eax,[ecx+0x8C]; shr eax,15; and eax,1; ret</c>), docs/research/retail-addresses-m2.md section 1.8.
    /// </summary>
    public const uint VividTargetingIndicatorOption = 0x8000;

    /// <summary>True when the character's options ask for the vivid target indicator (<see cref="VividTargetingIndicatorOption"/>); false until a PlayerDescription arrived.</summary>
    public bool VividTargetingIndicator => (CharacterOptions1 & VividTargetingIndicatorOption) != 0;

    /// <summary>The attribute id of Endurance (the health and stamina formulas).</summary>
    public const uint EnduranceAttributeId = 2;

    /// <summary>The attribute id of Self (the mana formula).</summary>
    public const uint SelfAttributeId = 6;

    /// <summary>
    /// A vital's current and maximum values, as <c>gmVitalsUI::Update</c> (11.6096 0x004C0AB0) asks <c>CACQualities::InqAttribute2nd</c> for them (0x00592D20 / 0x005927F0). The vital is named by its
    /// maximum's id (1 health, 3 stamina, 5 mana: the key the description and PrivateUpdateVital use). The maximum is retail's: the vital's start and ranks plus the secondary
    /// attribute formula (health: half of Endurance rounded up, stamina: Endurance, mana: Self) over the attributes <b>with their enchantments applied</b>
    /// (<c>InqAttribute2ndBaseLevel</c>, 0x00592A20 / 0x00591D20, asks <c>InqAttribute</c> with <c>raw</c> false), plus, for health, the integer property 379 (<c>0x17B</c>, ACE's GearMaxHealth), and then the
    /// registry's modifiers for the vital itself (<see cref="EnchantmentRules.EnchantSecondaryAttribute"/>). Checked against a live ACE: a fresh level-1 character with Endurance and Self of 55 is sent
    /// current values 28, 55 and 55 (ranks and start 0), which are this maximum with no enchantments; a buffed one's 50, 100 and 100 over raw attributes of 55 are the formula over attributes enchanted to 100.
    /// The current value is not enchanted.
    /// </summary>
    /// <returns>False until the description arrived or when the vital is missing.</returns>
    public bool TryGetVitalLevels(uint maxId, out uint current, out uint max)
    {
        current = 0;
        max = 0;
        if (!IsKnown || !Vitals.TryGetValue(maxId, out var vital))
        {
            return false;
        }

        uint FromAttribute(uint id) => Attributes.TryGetValue(id, out var a) ? EnchantmentRules.EnchantAttribute(Enchantments, id, a.Start + a.Ranks) : 0;
        var bonus = maxId switch
        {
            1 => (FromAttribute(EnduranceAttributeId) + 1) / 2,
            3 => FromAttribute(EnduranceAttributeId),
            _ => FromAttribute(SelfAttributeId),
        };
        var gear = maxId == 1 && Ints.TryGetValue(GearMaxHealthProperty, out var g) ? g : 0; // CACQualities::InqAttribute2nd adds PropertyInt 0x17B to the health maximum
        current = vital.Current;
        max = EnchantmentRules.EnchantSecondaryAttribute(Enchantments, maxId, unchecked((uint)(vital.Start + vital.Ranks + bonus + gear)));
        return true;
    }

    /// <summary>PropertyInt 379 (<c>0x17B</c>), added to the health maximum by <c>CACQualities::InqAttribute2nd</c>.</summary>
    public const uint GearMaxHealthProperty = 379;

    /// <summary>
    /// The enchantments the PlayerDescription's registry held, in wire order (multiplicative, additive, cooldown, then the vitae); empty until the description arrives. The vitals' maximums apply them
    /// (<see cref="TryGetVitalLevels"/>). TODO(g4-enchantment-updates): the messages that change the registry after the description (UpdateEnchantment 0x02C2, UpdateMultipleEnchantments, RemoveEnchantment,
    /// RemoveMultipleEnchantments, DispelEnchantment, PurgeEnchantments, PurgeBadEnchantments: <c>ClientMagicSystem::Handle_Magic__*</c>, 11.6096 0x005686D0 to 0x005698C0) are not decoded by net yet,
    /// so a buff cast or ended in the world shows in the maximums at the next description only.
    /// </summary>
    public ImmutableArray<EnchantmentDesc> Enchantments { get; internal set; } = [];

    internal Dictionary<uint, int> Ints { get; } = [];

    internal Dictionary<uint, long> Int64s { get; } = [];

    internal Dictionary<uint, bool> Bools { get; } = [];

    internal Dictionary<uint, double> Floats { get; } = [];

    internal Dictionary<uint, string> Strings { get; } = [];

    internal Dictionary<uint, uint> DataIds { get; } = [];

    internal Dictionary<uint, uint> InstanceIds { get; } = [];

    internal Dictionary<uint, AttributeDesc> Attributes { get; } = [];

    internal Dictionary<uint, VitalDesc> Vitals { get; } = [];

    internal Dictionary<uint, SkillDesc> Skills { get; } = [];

    /// <summary>An integer property (PropertyInt).</summary>
    public bool TryGetInt(uint key, out int value) => Ints.TryGetValue(key, out value);

    /// <summary>A 64-bit integer property (PropertyInt64).</summary>
    public bool TryGetInt64(uint key, out long value) => Int64s.TryGetValue(key, out value);

    /// <summary>A boolean property (PropertyBool).</summary>
    public bool TryGetBool(uint key, out bool value) => Bools.TryGetValue(key, out value);

    /// <summary>A floating-point property (PropertyFloat).</summary>
    public bool TryGetFloat(uint key, out double value) => Floats.TryGetValue(key, out value);

    /// <summary>A string property (PropertyString).</summary>
    public bool TryGetString(uint key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) => Strings.TryGetValue(key, out value);

    /// <summary>A data-id property (PropertyDataId).</summary>
    public bool TryGetDataId(uint key, out uint value) => DataIds.TryGetValue(key, out value);

    /// <summary>An instance-id property (PropertyInstanceId).</summary>
    public bool TryGetInstanceId(uint key, out uint value) => InstanceIds.TryGetValue(key, out value);

    /// <summary>A primary attribute (strength 1 ... self 6).</summary>
    public bool TryGetAttribute(uint id, out AttributeDesc value) => Attributes.TryGetValue(id, out value);

    /// <summary>A primary attribute's starting value and its start plus ranks (the retail UI reads attributes through these, not through the wire type).</summary>
    public bool TryGetAttributeLevels(uint id, out uint start, out uint raw)
    {
        start = 0;
        raw = 0;
        if (!Attributes.TryGetValue(id, out var attribute))
        {
            return false;
        }

        start = attribute.Start;
        raw = attribute.Start + attribute.Ranks;
        return true;
    }

    /// <summary>A vital (its ranks, experience and current value).</summary>
    public bool TryGetVital(uint id, out VitalDesc value) => Vitals.TryGetValue(id, out value);

    /// <summary>A skill.</summary>
    public bool TryGetSkill(uint id, out SkillDesc value) => Skills.TryGetValue(id, out value);

    /// <summary>Forgets every quality (a new description, a logoff, a reconnect).</summary>
    internal void Clear()
    {
        IsKnown = false;
        _serverOptions1 = 0; // the session-local option changes stay: a new description replaces the server's words only
        _serverOptions2 = 0;
        Ints.Clear();
        Int64s.Clear();
        Bools.Clear();
        Floats.Clear();
        Strings.Clear();
        DataIds.Clear();
        InstanceIds.Clear();
        Attributes.Clear();
        Vitals.Clear();
        Skills.Clear();
        Enchantments = [];
    }
}

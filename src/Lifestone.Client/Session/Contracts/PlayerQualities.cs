using System.Collections.Immutable;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

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

    /// <summary>The option bit <c>PlayerModule::AutoTarget</c> (11.6096 0x005D3E90) tests: bit 13 of the options word (<c>PlayerOptionTable</c>'s "AutoTarget", 0x2000).</summary>
    public const uint AutoTargetOption = 0x2000;

    /// <summary>True when the character's options ask for the auto-target (<see cref="AutoTargetOption"/>); false until a PlayerDescription arrives.</summary>
    public bool AutoTarget => (CharacterOptions1 & AutoTargetOption) != 0;

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

        uint FromAttribute(uint id) => Attributes.TryGetValue(id, out var a) ? EnchantmentRules.EnchantAttribute(_effective, id, a.Start + a.Ranks) : 0;
        var bonus = maxId switch
        {
            1 => (FromAttribute(EnduranceAttributeId) + 1) / 2,
            3 => FromAttribute(EnduranceAttributeId),
            _ => FromAttribute(SelfAttributeId),
        };
        var gear = maxId == 1 && Ints.TryGetValue(GearMaxHealthProperty, out var g) ? g : 0; // CACQualities::InqAttribute2nd adds PropertyInt 0x17B to the health maximum
        current = vital.Current;
        max = EnchantmentRules.EnchantSecondaryAttribute(_effective, maxId, unchecked((uint)(vital.Start + vital.Ranks + bonus + gear)));
        return true;
    }

    /// <summary>PropertyInt 379 (<c>0x17B</c>), added to the health maximum by <c>CACQualities::InqAttribute2nd</c>.</summary>
    public const uint GearMaxHealthProperty = 379;

    private ImmutableArray<ActiveEnchantment> _active = [];
    private ImmutableArray<EnchantmentDesc> _enchantments = [];
    private ImmutableArray<EnchantmentDesc> _effective = [];

    /// <summary>
    /// The enchantments of the player's registry as the server sent them (the records of the PlayerDescription and of every 02C2 and 02C4 since), in the registry's order: multiplicative, additive,
    /// cooldown, then the vitae; empty until the description arrives. The vitals' maximums apply them (<see cref="TryGetVitalLevels"/>), and every message that changes the registry (GameEvent 0x02C1 to
    /// 0x02C8 and 0x0312, <c>ClientMagicSystem::Handle_Magic__*</c>, 11.6096 0x005686D0 to 0x005698C0) is applied by the reducer at once (<c>GameStateReducer.Enchantments.cs</c>, which publishes
    /// <see cref="EnchantmentChanged"/>). Each record's start time is as sent, relative to when its message arrived: <see cref="ActiveEnchantments"/> holds the arrival time and answers the remaining time.
    /// Setting it (the description) stamps every entry with arrival time 0 until <c>GameStateReducer</c> stamps the description's own time.
    /// </summary>
    public ImmutableArray<EnchantmentDesc> Enchantments
    {
        get => _enchantments;
        internal set => SetActiveEnchantments(value.IsDefaultOrEmpty ? [] : [.. value.Select(e => new ActiveEnchantment(e, 0.0))]);
    }

    /// <summary>
    /// The registry's enchantments with the time each arrived (<see cref="ActiveEnchantment"/>: the remaining seconds, the expiry on the session clock or the server clock, whether it is overdue), in
    /// the same order as <see cref="Enchantments"/>. No timer removes an entry: the server's 02C3 does (retail's client never times one out); an entry kept past its expiry is reported as
    /// <c>TODO(m3-enchant-overdue)</c> (<see cref="CountOverdueEnchantments"/>).
    /// </summary>
    public ImmutableArray<ActiveEnchantment> ActiveEnchantments => _active;

    /// <summary>The enchantment with this spell and layer (as retail's layered id names it).</summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="layer">The layer.</param>
    /// <param name="enchantment">The entry.</param>
    public bool TryGetEnchantment(ushort spellId, ushort layer, out ActiveEnchantment enchantment)
    {
        foreach (var candidate in _active)
        {
            if (candidate.SpellId == spellId && candidate.Layer == layer)
            {
                enchantment = candidate;
                return true;
            }
        }

        enchantment = default;
        return false;
    }

    /// <summary>The vitae (the death penalty's single enchantment), when the character has one.</summary>
    /// <param name="vitae">The entry.</param>
    public bool TryGetVitae(out ActiveEnchantment vitae)
    {
        foreach (var candidate in _active)
        {
            if (candidate.Enchantment.Bucket == EnchantmentBucket.Vitae)
            {
                vitae = candidate;
                return true;
            }
        }

        vitae = default;
        return false;
    }

    /// <summary>How many entries are past their expiry by more than <paramref name="graceSeconds"/> at <paramref name="nowSeconds"/> with no removal received (<c>TODO(m3-enchant-overdue)</c>; report-only).</summary>
    /// <param name="nowSeconds">Session-clock seconds.</param>
    /// <param name="graceSeconds">The allowance past the expiry.</param>
    public int CountOverdueEnchantments(double nowSeconds, double graceSeconds = EnchantmentRules.OverdueGraceSeconds)
    {
        var count = 0;
        foreach (var entry in _active)
        {
            if (entry.IsOverdue(nowSeconds, graceSeconds))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The reducer replaces the registry. The rule's input (<see cref="EnchantmentRules"/>) is the registry with every start time moved to the session clock, so the duel's "started later" compares enchantments
    /// that arrived at different times (retail adds its clock to the start when it unpacks an enchantment).
    /// </summary>
    internal void SetActiveEnchantments(ImmutableArray<ActiveEnchantment> active)
    {
        _active = active.IsDefaultOrEmpty ? [] : active;
        _enchantments = _active.IsEmpty ? [] : [.. _active.Select(e => e.Enchantment)];
        _effective = _active.IsEmpty ? [] : [.. _active.Select(e => e.Enchantment with { StartTime = e.StartSeconds })];
    }

    /// <summary>
    /// The description's registry in one pass (M3 1.2 s5; the arrival's frame allocated the registry's three arrays twice, once with arrival time 0 and again stamped): each entry stamped with
    /// <paramref name="receivedSeconds"/>, the arrays pre-sized, and the incoming array kept as <see cref="Enchantments"/> (its content is exactly what the setter rebuilt). The same state as setting
    /// <see cref="Enchantments"/> and then stamping every entry.
    /// </summary>
    internal void SetDescriptionEnchantments(ImmutableArray<EnchantmentDesc> registry, double receivedSeconds)
    {
        if (registry.IsDefaultOrEmpty)
        {
            SetActiveEnchantments([]);
            return;
        }

        var active = ImmutableArray.CreateBuilder<ActiveEnchantment>(registry.Length);
        var effective = ImmutableArray.CreateBuilder<EnchantmentDesc>(registry.Length);
        foreach (var entry in registry)
        {
            var stamped = new ActiveEnchantment(entry, receivedSeconds);
            active.Add(stamped);
            effective.Add(entry with { StartTime = stamped.StartSeconds });
        }

        _active = active.MoveToImmutable();
        _enchantments = registry;
        _effective = effective.MoveToImmutable();
    }

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

    /// <summary>The player's stored positions by ACE's <c>PositionType</c> (GameMessage 0x02DB PrivateUpdatePosition; M5 6 w2), e.g. LastOutsideDeath.</summary>
    internal Dictionary<uint, AcPosition> Positions { get; } = [];

    /// <summary>PropertyInt 25 (ACE's <c>PropertyInt.Level</c>).</summary>
    public const uint LevelProperty = 25;

    /// <summary>PropertyInt 24 (ACE's <c>PropertyInt.AvailableSkillCredits</c>).</summary>
    public const uint AvailableSkillCreditsProperty = 24;

    /// <summary>PropertyInt64 1 (ACE's <c>PropertyInt64.TotalExperience</c>).</summary>
    public const uint TotalExperienceProperty = 1;

    /// <summary>PropertyInt64 2 (ACE's <c>PropertyInt64.AvailableExperience</c>).</summary>
    public const uint AvailableExperienceProperty = 2;

    /// <summary>
    /// The character's level (PropertyInt 25): read from the PlayerDescription and kept current by PrivateUpdatePropertyInt 0x02CD. 0 until
    /// the description arrives, or when the server did not send it (M3 s1b; spec 5.3).
    /// </summary>
    public int Level => Ints.TryGetValue(LevelProperty, out var v) ? v : 0;

    /// <summary>The skill credits the character can still spend (PropertyInt 24); 0 until known. Kept current by 0x02CD.</summary>
    public int AvailableSkillCredits => Ints.TryGetValue(AvailableSkillCreditsProperty, out var v) ? v : 0;

    /// <summary>The experience the character can still spend (PropertyInt64 2); 0 until known. Kept current by PrivateUpdatePropertyInt64 0x02CF.</summary>
    public long AvailableExperience => Int64s.TryGetValue(AvailableExperienceProperty, out var v) ? v : 0;

    /// <summary>The experience the character has earned in all (PropertyInt64 1; a level 126 character has 4,286,609,098 or more, which an int cannot hold); 0 until known. Kept current by 0x02CF.</summary>
    public long TotalExperience => Int64s.TryGetValue(TotalExperienceProperty, out var v) ? v : 0;

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

    /// <summary>ACE's <c>PropertyBool.Afk</c>, set by GameMessage 0x02D1 after a SetAfkMode (M5 6 w2).</summary>
    public const uint AfkProperty = 110;

    /// <summary>
    /// Whether the server says the player is away from the keyboard: ACE's <c>PropertyBool.Afk</c> from the last GameMessage 0x02D1 (ACE's
    /// <c>HandleActionSetAFKMode</c> answers every 0x000F with it: @95ef426d <c>Player_Networking.cs:438-443</c>) or the description; false when neither said.
    /// </summary>
    public bool Afk => Bools.TryGetValue(AfkProperty, out var v) && v;

    /// <summary>One of the player's stored positions (ACE's <c>PositionType</c>, e.g. 0x0E LastOutsideDeath), from GameMessage 0x02DB; false when none came.</summary>
    public bool TryGetPosition(uint positionType, out AcPosition value) => Positions.TryGetValue(positionType, out value);

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
        Positions.Clear();
        Enchantments = [];
    }

    /// <summary>The player's combat mode (<c>PropertyInt</c> 40, <see cref="CombatModes"/>: 1 non-combat, 2 melee, 4 missile, 8 magic); 0 until the server has sent one. Kept current by 0x02CD, which also raises <see cref="CombatModeChanged"/>.</summary>
    public int CombatMode => Ints.TryGetValue(CombatModes.Property, out var v) ? v : 0;

    /// <summary>PropertyInt 20 (ACE's <c>PropertyInt.CoinValue</c>): the pyreals the player carries.</summary>
    public const uint CoinValueProperty = 20;

    /// <summary>
    /// The pyreals the player carries (PropertyInt 20, ACE's <c>CoinValue</c>, recomputed by <c>Player.UpdateCoinValue</c> whenever a coin stack is created, changed or removed); 0 until the description or a 0x02CD says. Kept current by
    /// 0x02CD, which also raises <see cref="CoinValueChanged"/> (M4 3.2 s1). The vendor trackers check a trade's price or payout against its change.
    /// </summary>
    public int CoinValue => Ints.TryGetValue(CoinValueProperty, out var v) ? v : 0;
}

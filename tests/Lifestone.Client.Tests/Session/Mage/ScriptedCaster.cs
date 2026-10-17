using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// A scripted ACE for the buff cycle's tests (M3 2.1 s0b): the BSD mage's PlayerDescription (the template's attributes, ranks and vitals, the wand and five armour pieces worn) on a <see cref="WorldActionRig"/>,
/// and a <see cref="Pump"/> that answers each cast the cycle sent as ACE does, through the real decoder:
/// <list type="bullet">
/// <item>a self buff: the mana is spent (private vital update 0x02E9), then GameEvent 0x02C2 with the enchantment and its StatMod from <see cref="BuffStatMods"/> (so the current skill rises), then UseDone 0;</item>
/// <item>an armour line cast at the caster: the mana plus the ManaMod of each worn clothing piece, one "You cast &lt;spell&gt; on &lt;item&gt;" Magic chat line per piece and no 0x02C2 (<c>WorldObject_Magic.cs</c>
/// 430-452 and 2067-2121), then UseDone 0;</item>
/// <item>Stamina to Mana Self: the cost, then the vitals by <see cref="BuffStatMods.Transfer"/> (ACE's <c>HandleCastSpell_Transfer</c>) and ACE's chat line; Revitalize Self: the mean of its roll; both end with UseDone 0;</item>
/// <item>a fizzle on demand (<see cref="FizzleNext"/>): WeenieError 0x028A 0x0402, 5 mana, UseDone 0 (<c>Player_Magic.cs</c> 566-572).</item>
/// </list>
/// The spell table is the profile's lines with the shape the real table has (powers and durations from <see cref="ArenaProfile"/>, base mana, the armour lines' ManaMod and Vestements target type, the wand's
/// Auras self-targeted). Mana and stamina regenerate at <see cref="ManaRegenPerSecond"/> and <see cref="StaminaRegenPerSecond"/> as the fake clock advances.
/// <para><b>The mana cost</b> (M3 2.1 fix B, review 2.1-R2): a cast costs what ACE charges, not <see cref="GearCasts.ManaCeiling"/>. This file re-expresses ACEmulator/ACE@95ef426d
/// <c>Source/ACE.Server/WorldObjects/Creature_Magic.cs</c> (<c>CalculateManaUsage</c> lines 14-56, <c>GetManaCost</c> 58-100; AGPL-3.0), <c>SkillCheck.cs</c> (<c>GetSkillChance</c> 9-14) and
/// <c>WorldObject_Weapon.cs</c> (<c>GetWeaponManaConversionModifier</c> 235-262) [with changes: the roll is a seeded <see cref="Random"/>; the mage's wand has no ManaConversionMod, so the weapon modifier is 1]. The
/// base is the spell's base mana plus, for the armour lines, <c>ManaMod</c> for each worn piece; the scripted mage's Mana Conversion skill (<see cref="ManaConversionSkill"/>: the template's ranks, the
/// Focus and Self formula and the strongest additive enchantment of each category, as the read model sees them) then rolls twice against the spell's power (difficulty / 2, then difficulty) and lowers the cost
/// by the success chance less the roll times a luck roll; the cost is at least 1. Set <see cref="CeilingCost"/> to charge the ceiling instead (the worst case some tests pin).</para>
/// </summary>
internal sealed class ScriptedCaster
{
    /// <summary>The Magic chat type (ACE <c>ChatMessageType.Magic</c>).</summary>
    public const int MagicChat = 17;

    /// <summary>The armour pieces the mage wears (names are the test's; item type Armor).</summary>
    public static readonly string[] PieceNames = ["Leather Cap", "Leather Vest", "Leather Gloves", "Leather Boots", "Leather Leggings"];

    private const uint WandGuid = 0x80000200;
    private const uint FirstPieceGuid = 0x80000201;
    private const uint Wand = 0x01000000;

    private readonly WorldActionRig _rig;
    private readonly Dictionary<uint, int> _layers = [];
    private readonly Dictionary<uint, byte> _sequence = [];
    private readonly List<(Func<SpellInfo, bool> Match, int Left)> _fizzles = [];
    private readonly HashSet<string> _silentPieces = [];
    private readonly HashSet<string> _surpassedPieces = [];
    private int _seen;
    private double _lastNow = double.NaN;
    private readonly Dictionary<uint, uint> _topOfCategory = [];
    private readonly Queue<(uint Spell, uint Target, double Due)> _pending = new();
    private readonly Random _manaRolls;
    private double _manaAcc;
    private double _staminaAcc;

    /// <summary>The divisor of Mana Conversion's skill formula, (Focus + Self) / 6 (the SkillTable's entry 16).</summary>
    public const int ManaConversionDivisor = 6;

    /// <summary>The seed of the Mana Conversion rolls (the same seed gives the same cycle).</summary>
    public const int DefaultManaSeed = 0x2A1;

    public ScriptedCaster(WorldActionRig rig, uint mana, uint stamina, int manaSeed = DefaultManaSeed)
    {
        _rig = rig;
        _manaRolls = new Random(manaSeed);
        Table = BuildTable();
        Mana = mana;
        Stamina = stamina;
        rig.Spells([.. Table.Values]);
        Describe();
        rig.CombatModeIs(CombatModes.Magic); // after the description, which replaces the player's integer properties
    }

    /// <summary>The spell table (the profile's lines, in the real table's shape).</summary>
    public Dictionary<uint, SpellInfo> Table { get; }

    /// <summary>The server's current mana.</summary>
    public uint Mana { get; set; }

    /// <summary>The server's current stamina.</summary>
    public uint Stamina { get; set; }

    /// <summary>Mana regenerated per second of the fake clock: 2.0 keeps a whole cycle inside the shortest buff (live ACE regenerates about 0.2 a second; the economy tests set that).</summary>
    public double ManaRegenPerSecond { get; set; } = 2.0;

    /// <summary>Stamina regenerated per second of the fake clock (1.0: a guess, TODO(m3-stamina-regen-rate): ACE's heartbeat rate of the BSD mage is not pinned).</summary>
    public double StaminaRegenPerSecond { get; set; } = 1.0;

    /// <summary>True: every cast costs <see cref="GearCasts.ManaCeiling"/> (the worst case, no Mana Conversion reduction); false (the default): ACE's cost with the scripted mage's Mana Conversion roll.</summary>
    public bool CeilingCost { get; set; }

    /// <summary>Seconds of the fake clock a cast takes before ACE answers it (the cast gesture's time; 0, the default, answers in the frame the cast arrives; the live buff cycle averaged about 1.9 s a cast, M3 2.1 fix B).</summary>
    public double CastSeconds { get; set; }

    /// <summary>The mana each landed cast was charged, in order (the spell id and the cost).</summary>
    public List<(uint Spell, uint Cost)> Charged { get; } = [];

    /// <summary>Every cast answered, in order: the spell id and the session-clock time of the answer.</summary>
    public List<(uint Spell, double At, bool Fizzled)> Answered { get; } = [];

    /// <summary>The combat modes asked for, in order (GameAction 0x0053), each answered at once.</summary>
    public List<int> ModesSent { get; } = [];

    /// <summary>The armour pieces' guids in <see cref="PieceNames"/> order.</summary>
    public IReadOnlyList<uint> PieceGuids { get; } = [.. Enumerable.Range(0, PieceNames.Length).Select(i => FirstPieceGuid + (uint)i)];

    /// <summary>The next <paramref name="count"/> casts of a spell that <paramref name="match"/> accepts fizzle.</summary>
    public void FizzleNext(Func<SpellInfo, bool> match, int count) => _fizzles.Add((match, count));

    /// <summary>The next <paramref name="count"/> casts of the spell with this id fizzle.</summary>
    public void FizzleNext(uint spellId, int count) => FizzleNext(s => s.Id == spellId, count);

    /// <summary>ACE leaves this piece out of the armour cast's chat lines (a piece that is not enchantable, or a server that does not name it).</summary>
    public void SilencePiece(string name) => _silentPieces.Add(name);

    /// <summary>The piece's armour line says it was surpassed (ACE, <c>WorldObject_Magic.cs</c> 419: ", but it is surpassed by &lt;spell&gt;"): the piece already holds a stronger version.</summary>
    public void SurpassPiece(string name) => _surpassedPieces.Add(name);

    /// <summary>The spell ids of the answered casts that landed, in order.</summary>
    public IEnumerable<uint> Landed => Answered.Where(a => !a.Fizzled).Select(a => a.Spell);

    /// <summary>Applies the regeneration since the last call and answers every cast the Action API sent since.</summary>
    public void Pump()
    {
        var now = _rig.H.R.Clock.Now;
        Regenerate(now);
        var sends = _rig.H.Net.Sends;
        while (_seen < sends.Count)
        {
            var send = sends[_seen++];
            switch (send.Kind)
            {
                case FakeSendKind.CastUntargetedSpell:
                    _pending.Enqueue(((uint)send.Guid, 0, now + CastSeconds));
                    break;
                case FakeSendKind.CastTargetedSpell:
                    _pending.Enqueue(((uint)send.Index, send.Guid, now + CastSeconds));
                    break;
                case FakeSendKind.ChangeCombatMode:
                    // ACE changes the mode after the stance animation and tells it with the private int update 0x02CD (Player_Combat.cs HandleActionChangeCombatMode).
                    ModesSent.Add((int)send.Amount);
                    _rig.CombatModeIs((int)send.Amount);
                    break;
            }
        }

        while (_pending.Count > 0 && _pending.Peek().Due <= now)
        {
            var cast = _pending.Dequeue();
            Answer(cast.Spell, cast.Target, now);
        }
    }

    private static Dictionary<uint, SpellInfo> BuildTable()
    {
        var table = new Dictionary<uint, SpellInfo>();
        var category = 200u;
        foreach (var line in ArenaProfile.Lines)
        {
            category++;
            foreach (var spell in line.Spells)
            {
                var armour = line.Role == ProfileRole.ItemBuffArmor;
                var meta = line.Role switch
                {
                    ProfileRole.ManaTransfer => SpellType.Transfer,
                    ProfileRole.StaminaRestore => SpellType.Boost,
                    ProfileRole.WarBolt or ProfileRole.WarOption => SpellType.Projectile,
                    _ => SpellType.Enchantment,
                };
                var flags = line.Role switch
                {
                    ProfileRole.ItemBuffArmor => SpellFlags.Beneficial,
                    ProfileRole.WarBolt or ProfileRole.WarOption => SpellFlags.Projectile,
                    _ => SpellFlags.Beneficial | SpellFlags.SelfTargeted,
                };
                var baseMana = line.IsManaLine
                    ? spell.Position switch { <= 5 => (uint)(10 * spell.Position), 6 => 60u, 7 => 70u, _ => 80u }
                    : spell.Position switch { <= 5 => (uint)(10 * spell.Position), 6 or 7 => 70u, _ => 100u };
                table[spell.Id] = WorldActionRig.Spell(spell.Id, meta, flags) with
                {
                    Name = spell.Name,
                    School = line.School,
                    Power = spell.Power,
                    Duration = spell.DurationSeconds,
                    BaseMana = baseMana,
                    Category = armour ? 159 + (line.Name == "Impenetrability" ? 1u : 2u) : category,
                    ManaMod = armour ? 14u : 0u,
                    NonComponentTargetType = armour ? 0x6u : line.Role == ProfileRole.ItemBuffWand ? 0x8000u : 0u,
                };
            }
        }

        return table;
    }

    private void Describe()
    {
        var attributes = ImmutableArray.CreateBuilder<AttributeDesc>();
        foreach (var id in new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self })
        {
            var ranks = MageTemplate.TargetOf(MageStatKind.Attribute, id)?.Ranks ?? 0;
            attributes.Add(new AttributeDesc(id, (uint)ranks, MageTemplate.CreationValue(id), 0));
        }

        var skills = ImmutableArray.CreateBuilder<SkillDesc>();
        foreach (var id in new[] { MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.LifeMagic, MageTemplate.WarMagic, MageTemplate.ManaConversion })
        {
            var t = MageTemplate.TargetOf(MageStatKind.Skill, id)!;
            skills.Add(new SkillDesc(id, (ushort)t.Ranks, 1, t.Specialized ? 3u : 2u, 0, t.Specialized ? 10u : 0u, 0, 0));
        }

        // Vitals: start 0, the ranks the template buys, so the maximum is the formula's (mana 159 + Self 267 = 426, stamina 135 + Endurance 140 = 275).
        var vitals = ImmutableArray.CreateBuilder<VitalDesc>();
        vitals.Add(new VitalDesc(MageTemplate.MaxHealth, 100, 0, 0, 200));
        vitals.Add(new VitalDesc(MageTemplate.MaxStamina, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxStamina)!.Ranks, 0, 0, Stamina));
        vitals.Add(new VitalDesc(MageTemplate.MaxMana, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxMana)!.Ranks, 0, 0, Mana));

        // The gear kit's own weenies (the wand, the leather set) in their locations, so a gear scenario sees a ready mage and sends no gear step.
        Wear(WandGuid, "Test Wand", 0x8000, GearKit.Wand.Wcid);
        var equipped = new List<EquippedDesc> { new(WandGuid, Wand, 0) };
        for (var i = 0; i < PieceNames.Length; i++)
        {
            Wear(FirstPieceGuid + (uint)i, PieceNames[i], 0x2, GearKit.Armour[i].Wcid);
            equipped.Add(new EquippedDesc(FirstPieceGuid + (uint)i, (uint)GearKit.Armour[i].ValidLocations, 0));
        }

        _rig.H.Describe(new PlayerDescriptionMsg { Attributes = attributes.ToImmutable(), Skills = skills.ToImmutable(), Vitals = vitals.ToImmutable(), Equipped = [.. equipped] });
    }

    private void Wear(uint guid, string name, uint itemType, uint wcid)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        _rig.H.R.Reducer.OnCreateObject(_rig.H.R.Context, basis with { Weenie = basis.Weenie with { ItemType = itemType, WeenieClassId = wcid } }, isUpdate: false);
    }

    private void Regenerate(double now)
    {
        if (!double.IsNaN(_lastNow) && now > _lastNow)
        {
            _manaAcc += (now - _lastNow) * ManaRegenPerSecond;
            _staminaAcc += (now - _lastNow) * StaminaRegenPerSecond;
            var q = _rig.H.State.Self.Qualities;
            if (_manaAcc >= 1 && q.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var maxMana))
            {
                var whole = (uint)_manaAcc;
                _manaAcc -= whole;
                if (Mana < maxMana)
                {
                    Mana = Math.Min(maxMana, Mana + whole);
                    SendVital(MageTemplate.MaxMana, Mana);
                }
            }

            if (_staminaAcc >= 1 && q.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var maxStamina))
            {
                var whole = (uint)_staminaAcc;
                _staminaAcc -= whole;
                if (Stamina < maxStamina)
                {
                    Stamina = Math.Min(maxStamina, Stamina + whole);
                    SendVital(MageTemplate.MaxStamina, Stamina);
                }
            }
        }

        _lastNow = now;
    }

    private void SendVital(uint maxId, uint current)
    {
        var key = maxId + 1; // the current's secondary attribute: 4 stamina, 6 mana (0x02E9 PrivateUpdateAttribute2ndLevel)
        _sequence[key] = (byte)(_sequence.GetValueOrDefault(key) + 1);
        _rig.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(_sequence[key], key, current));
    }

    private void Chat(string text) => _rig.Raw(AceWireWriters.ServerMessage(text, MagicChat));

    private void Answer(uint spellId, uint target, double now)
    {
        var info = Table[spellId];
        for (var i = 0; i < _fizzles.Count; i++)
        {
            if (_fizzles[i].Left > 0 && _fizzles[i].Match(info))
            {
                _fizzles[i] = (_fizzles[i].Match, _fizzles[i].Left - 1);
                Answered.Add((spellId, now, true));
                Mana = Mana >= GearCasts.FizzleMana ? Mana - GearCasts.FizzleMana : 0;
                SendVital(MageTemplate.MaxMana, Mana);
                _rig.WeenieError(0x0402);
                _rig.UseDone();
                return;
            }
        }

        Answered.Add((spellId, now, false));
        var worn = PieceNames.Length;
        var ceiling = GearCasts.ManaCeiling(info, worn); // ACE: the base mana plus the ManaMod of each worn clothing piece for the armour lines (Creature_Magic.cs 14-60)
        var cost = CeilingCost ? ceiling : CalculateManaUsage(info, ceiling);
        if (Mana < cost)
        {
            _rig.UseDone(0x0401);
            return;
        }

        Charged.Add((spellId, cost));
        Mana -= cost;
        SendVital(MageTemplate.MaxMana, Mana);
        switch (info.MetaSpellType)
        {
            case SpellType.Enchantment when info.School == MagicSchool.ItemEnchantment && (info.Flags & SpellFlags.SelfTargeted) == 0:
                Assert.Equal(WorldActionRig.Player, target); // the armour lines are cast at the caster (D23)
                for (var i = 0; i < PieceNames.Length; i++)
                {
                    if (!_silentPieces.Contains(PieceNames[i]))
                    {
                        Chat("You cast " + info.Name + " on " + PieceNames[i] + (_surpassedPieces.Contains(PieceNames[i]) ? ", but it is surpassed by " + info.Name + " VII" : string.Empty));
                    }
                }

                break;
            case SpellType.Enchantment:
                Enchant(info);
                break;
            case SpellType.Transfer:
                var row = BuffStatMods.TransferOf(info.Id)!.Value;
                _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var maxMana);
                BuffStatMods.Transfer(row, Stamina, maxMana - Mana, out var drained, out var given);
                Stamina -= drained;
                Mana += given;
                SendVital(MageTemplate.MaxStamina, Stamina);
                SendVital(MageTemplate.MaxMana, Mana);
                Chat($"You cast {info.Name} on yourself and lose {drained} points of stamina and also gain {given} points of mana");
                break;
            case SpellType.Boost:
                var boost = BuffStatMods.Revitalize.First(b => b.Id == info.Id);
                _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var maxStamina);
                var gained = (uint)(boost.Boost + (boost.Variance / 2));
                Stamina = Math.Min(maxStamina, Stamina + gained);
                SendVital(MageTemplate.MaxStamina, Stamina);
                Chat($"You cast {info.Name} on yourself and restore {gained} points of stamina");
                break;
            default:
                throw new InvalidOperationException("the scripted caster does not answer " + info.Name);
        }

        _rig.UseDone();
    }

    /// <summary>
    /// The scripted mage's current Mana Conversion skill: the read model's Focus and Self with their enchantments in the skill's own formula, (Focus + Self) / <see cref="ManaConversionDivisor"/> (the SkillTable's, pinned by <c>The_scripted_mages_mana_conversion_formula_is_the_dats</c>; the four schools' is / 4), plus the skill's start level and ranks
    /// and the strongest additive skill enchantment of each category (ACE <c>CreatureSkill.Current</c>); 0 when the read model does not hold the skill.
    /// </summary>
    public uint ManaConversionSkill
    {
        get
        {
            var q = _rig.H.State.Self.Qualities;
            if (!q.TryGetSkill(MageTemplate.ManaConversion, out var skill)
                || !q.TryGetAttributeLevels(MageTemplate.Focus, out _, out var focusRaw) || !q.TryGetAttributeLevels(MageTemplate.Self, out _, out var selfRaw))
            {
                return 0;
            }

            var focus = EnchantmentRules.EnchantAttribute(q.Enchantments, MageTemplate.Focus, focusRaw);
            var self = EnchantmentRules.EnchantAttribute(q.Enchantments, MageTemplate.Self, selfRaw);
            var additive = 0;
            foreach (var id in _topOfCategory.Values)
            {
                var mod = BuffStatMods.BySpell[id];
                if (mod.IsSkill && mod.IsAdditive && mod.Key == MageTemplate.ManaConversion)
                {
                    additive += (int)mod.Value;
                }
            }

            var formula = (uint)Math.Round((focus + self) / (double)ManaConversionDivisor, MidpointRounding.AwayFromZero);
            return (uint)Math.Max(0, (int)(formula + skill.Init + skill.Ranks) + additive);
        }
    }

    /// <summary>ACE <c>Creature.CalculateManaUsage</c> (<c>Creature_Magic.cs</c> 14-56) from the base cost <paramref name="baseCost"/> (the ceiling: base mana plus the armour surcharge).</summary>
    private uint CalculateManaUsage(SpellInfo info, uint baseCost)
    {
        if ((info.Flags & SpellFlags.IgnoresManaConversion) != 0)
        {
            return baseCost; // line 43; the trained check of the same line holds: Mana Conversion is trained later
        }

        var skill = (uint)Math.Round(ManaConversionSkill * 1.0); // line 48; the wand has no ManaConversionMod: GetWeaponManaConversionModifier is 1
        return GetManaCost(Math.Max(info.Power, 25u), baseCost, skill);
    }

    /// <summary>ACE <c>Creature.GetManaCost</c> (<c>Creature_Magic.cs</c> 58-100): two rolls against the spell's power (half of it, then all of it), a luck roll lowering the first.</summary>
    private uint GetManaCost(uint difficulty, uint manaCost, uint manaConv)
    {
        if (manaConv == 0)
        {
            return manaCost;
        }

        var successChance = SkillChance(manaConv, difficulty / 2);
        var roll = _manaRolls.NextSingle();
        var luck = _manaRolls.NextSingle();
        if (roll < successChance)
        {
            manaCost = (uint)Math.Round(manaCost * (1.0f - (successChance - (roll * luck))));
        }

        if (manaCost > 1)
        {
            successChance = SkillChance(manaConv, difficulty);
            roll = _manaRolls.NextSingle();
            if (roll < successChance)
            {
                manaCost = (uint)Math.Round(manaCost * (1.0f - (successChance - (roll * luck))));
            }
        }

        return Math.Max(manaCost, 1);
    }

    /// <summary>ACE <c>SkillCheck.GetSkillChance</c> (<c>SkillCheck.cs</c> 9-14, factor 0.03).</summary>
    private static double SkillChance(uint skill, uint difficulty) =>
        Math.Clamp(1.0 - (1.0 / (1.0 + Math.Exp(0.03f * ((int)skill - (int)difficulty)))), 0.0, 1.0);

    /// <summary>ACE's <c>EnchantmentManager.Add</c> for a player caster and target: a repeat of the spell refreshes its layer, another spell of the category takes the next layer; GameEvent 0x02C2.</summary>
    private void Enchant(SpellInfo info)
    {
        var mod = BuffStatMods.BySpell[info.Id];
        if (!_topOfCategory.TryGetValue(info.Category, out var top) || Table[top].Power <= info.Power)
        {
            _topOfCategory[info.Category] = info.Id; // the strongest of a category is the one that counts (EnchantmentManager.GetSkillMod_Additives)
        }

        if (!_layers.TryGetValue(info.Id, out var layer))
        {
            layer = 1;
            foreach (var other in _layers.Keys)
            {
                if (Table[other].Category == info.Category)
                {
                    layer = Math.Max(layer, _layers[other] + 1);
                }
            }

            _layers[info.Id] = layer;
        }

        _rig.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)info.Id, (ushort)layer, (ushort)info.Category, 0, info.Power, 0.0, info.Duration, WorldActionRig.Player, 0f, 0f, 0.0, mod.Type, mod.Key, (float)mod.Value, 0)));
    }
}

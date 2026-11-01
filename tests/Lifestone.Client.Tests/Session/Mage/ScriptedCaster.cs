using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
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
/// by the success chance less the roll times a luck roll; the cost is at least 1. Set <see cref="CeilingCost"/> to charge the ceiling instead (the worst case some tests pin).
/// M5 1 K2: the formula (<c>GetManaCost</c> and <c>GetSkillChance</c>) now lives in <see cref="ManaConversionModel"/>, the one source the stand-in and the keeper's try floor share; this file only draws from its own seeded
/// <see cref="Random"/> (floats, in ACE's order: the roll, the luck, then the second roll) and passes the draws to <see cref="ManaConversionModel.RollCost"/>.</para>
/// </summary>
internal sealed class ScriptedCaster
{
    /// <summary>
    /// The chat type of the caster's own "You cast &lt;spell&gt; on &lt;name&gt;" lines and the vital spells' lines: ACE sends them as <c>ChatMessageType.Magic</c> = 0x07 (<c>WorldObject_Magic.cs</c> 443 and 1420-1422
    /// @95ef426d; <c>ChatMessageType.cs</c> 98), the value of <see cref="FightTally.MagicChatType"/> and the stand-in's. M5 1 K2 fix: it was 17 (0x11, <c>Spellcasting</c>, the type of a caster's spoken words), which a
    /// cast proof that reads the Magic type (K1's) never saw; no test depended on 17 (<c>BuffCycle</c> matches these lines by their text).
    /// </summary>
    public const int MagicChat = (int)FightTally.MagicChatType;

    /// <summary>The armour pieces the mage wears (names are the test's; item type Armor).</summary>
    public static readonly string[] PieceNames = ["Leather Cap", "Leather Vest", "Leather Gloves", "Leather Boots", "Leather Leggings"];

    private const uint WandGuid = 0x80000200;

    /// <summary>The Mana Potion stack a fighter's rig may hold (M4 P5a C7).</summary>
    public const uint ManaPotionGuid = 0x80000260;

    /// <summary>The Stamina Potion stack a rig may hold (M5 7 F5: the refill's fallback when no Revitalize Self is payable).</summary>
    public const uint StaminaPotionGuid = 0x80000261;
    private const uint FirstPieceGuid = 0x80000201;
    private const uint Wand = 0x01000000;

    private readonly WorldActionRig _rig;
    private readonly Lifestone.Client.Session.Fighter.FighterTemplate? _fighter;
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
    private readonly Func<double> _nextManaRoll;
    private double _manaAcc;
    private double _staminaAcc;
    private byte _potionSeq;
    private byte _wieldSeq;

    /// <summary>True: a wield (0x001A) is answered as ACE answers it; false (the default): no answer.</summary>
    public bool AnswerWields { get; set; }

    /// <summary>
    /// Takes the wand off into the main pack as ACE's unwield does (Player_Inventory.cs 400-405 and 1359 @95ef426d: 02DA Wielder 0, 02CE CurrentWieldedLocation 0, 02DA Container, then 0022): a fresh build's mage,
    /// its kit granted and not wielded (M5 3 gate round 1). Returns the wand's guid.
    /// </summary>
    public uint WandToPack()
    {
        _rig.Raw(AceM3Writers.PublicUpdateInstanceId(++_wieldSeq, WandGuid, 3, 0));
        _rig.Raw(AceM3Writers.PublicUpdatePropertyInt(_wieldSeq, WandGuid, 10, 0));
        _rig.Raw(AceM3Writers.PublicUpdateInstanceId(_wieldSeq, WandGuid, 2, WorldActionRig.Player));
        _rig.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, WandGuid, WorldActionRig.Player, 0, 0));
        _rig.Tick();
        return WandGuid;
    }
    private readonly IReadOnlyCollection<uint>? _known;

    /// <summary>
    /// M5 1 F1: a projectile spell (the fight's bolt) is answered by this hook after its mana is charged (a dearer roll than the mana held is refused 0x0401 first, as every cast): the test creates the projectile and lands
    /// the hit; the UseDone follows. Null: a projectile is not answered (as before).
    /// </summary>
    public Action<uint, uint>? ProjectileHook { get; set; }

    /// <summary>The divisor of Mana Conversion's skill formula, (Focus + Self) / 6 (the SkillTable's entry 16).</summary>
    public const int ManaConversionDivisor = 6;

    /// <summary>The seed of the Mana Conversion rolls (the same seed gives the same cycle).</summary>
    public const int DefaultManaSeed = 0x2A1;

    /// <param name="lines">The lines the spell table holds: the mage's <see cref="ArenaProfile.Lines"/> by default; a fighter's rig (M4 P5a C4) passes the fighter profile's buff and mana lines.</param>
    /// <param name="fighter">A fighter template (M4 P5a C4): the attributes, the three magic skills and the vitals are the fighter's level-55 build (Creature Enchantment and Life Magic 183, Max Mana about 225), not the mage's.</param>
    /// <param name="known">M5 1 F1: the spells the PlayerDescription's spellbook holds (null, the default: none, as before).</param>
    public ScriptedCaster(WorldActionRig rig, uint mana, uint stamina, int manaSeed = DefaultManaSeed, IReadOnlyList<ProfileLine>? lines = null, Lifestone.Client.Session.Fighter.FighterTemplate? fighter = null, uint manaPotions = 0, IReadOnlyCollection<uint>? known = null, uint staminaPotions = 0, bool withDrain = false, bool withStaminaDrain = false, uint lifeMagicRanks = 0)
    {
        _lifeMagicRanks = lifeMagicRanks;
        _rig = rig;
        StaminaPotions = staminaPotions;
        _known = known;
        _fighter = fighter;
        _manaRolls = new Random(manaSeed);
        _nextManaRoll = () => _manaRolls.NextSingle(); // the draws are floats, as before the model took the formula over (ACE's are doubles in [0, 1))
        ManaPotions = manaPotions;
        Table = BuildTable([.. lines ?? ArenaProfile.Lines, .. (withDrain ? new[] { DrainLine, HealLine } : []), .. (withStaminaDrain ? new[] { StaminaDrainLine } : [])]);
        Mana = mana;
        Stamina = stamina;
        rig.Spells([.. Table.Values]);
        Describe();
        rig.CombatModeIs(CombatModes.Magic); // after the description, which replaces the player's integer properties
    }

    /// <summary>M5 7 F7: Drain Health Other I to VII, the line the fight's drain rule casts (the extras' line, the dat's ids).</summary>
    public static ProfileLine DrainLine { get; } = ArenaProfile.ExtraNamed("Drain Health Other")!;

    private readonly uint _lifeMagicRanks; // M5 7 F15: a Life Magic that reaches level VI's power (0: the template's ranks)

    /// <summary>M5 7 F15: Drain Stamina Other I to VII, the line the fight's stamina floor may cast at its target.</summary>
    public static ProfileLine StaminaDrainLine { get; } = ArenaProfile.ExtraNamed("Drain Stamina Other")!;

    /// <summary>M5 7 F15: the stamina a landed Drain Stamina Other of the scripted caster gives (cut to the stamina missing); 0 is a target with nothing left, where ACE still writes its landing line.</summary>
    public uint StaminaDrainYield { get; set; } = 40;

    /// <summary>M5 7 F7: Heal Self I to VII (6, 1157 to 1161, 2073; powers 1, 50, 100, 150, 200, 250 and 300: the dat's), the keeper's heal.</summary>
    public static ProfileLine HealLine { get; } = new("Heal Self", "Heal Self", MagicSchool.LifeMagic, ProfileRole.HealSelf, [.. VitalsMeans.HealSelf.Select((id, i) => new ProfileSpell(i + 1, id, i == 6 ? "Adja's Intervention" : "Heal Self " + LevelFloor.Roman[i], (uint)(i == 0 ? 1 : i * 50), 0))]);

    /// <summary>M5 7 F7: the health a landed Drain Health Other or Heal Self of the scripted caster gives (cut to the health missing).</summary>
    public uint HealthPerCast { get; set; } = 60;

    /// <summary>M5 7 F7: the caster's current health (the scripted description starts it at 200).</summary>
    public uint Health { get; private set; } = 200;

    /// <summary>M5 7 F7: sets the current health and tells the client.</summary>
    /// <param name="health">The health.</param>
    public void SetHealth(uint health)
    {
        Health = health;
        SendVital(MageTemplate.MaxHealth, Health);
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

    /// <summary>The Mana Potions held (M4 P5a C7); set by the constructor's <c>manaPotions</c>, each drink restores <see cref="Lifestone.Client.Session.Fighter.FighterKit.VitalPotionBoost"/> and the potion's use is answered in <see cref="Pump"/>.</summary>
    public uint ManaPotions { get; private set; }

    /// <summary>The Stamina Potions held (M5 7 F5); set by the constructor's <c>staminaPotions</c>, each drink restores <see cref="Lifestone.Client.Session.Fighter.FighterKit.VitalPotionBoost"/> stamina.</summary>
    public uint StaminaPotions { get; private set; }

    /// <summary>How many Stamina Potions were drunk (M5 7 F5).</summary>
    public int StaminaPotionsDrunk { get; private set; }

    /// <summary>How many Mana Potions were drunk (M4 P5a C7).</summary>
    public int PotionsDrunk { get; private set; }

    /// <summary>The mana each landed cast was charged, in order (the spell id and the cost).</summary>
    public List<(uint Spell, uint Cost)> Charged { get; } = [];

    /// <summary>Every cast answered, in order: the spell id and the session-clock time of the answer.</summary>
    public List<(uint Spell, double At, bool Fizzled)> Answered { get; } = [];

    /// <summary>A hook over the untargeted casts the Action API sent (M4 P5a C1c): returns true when the test answers the cast itself, so the scripted table never sees it.</summary>
    public Func<uint, bool>? CastHook { get; set; }

    /// <summary>M5 3 p1: as <see cref="CastHook"/>, for a targeted cast (the spell and its target): true when the test answers it itself.</summary>
    public Func<uint, uint, bool>? TargetedCastHook { get; set; }

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
    /// <summary>M4 P5a fix r4: sets the current mana and stamina and tells the session at once (a drained character after the login, as round 4's heal leg left the Archer).</summary>
    public void SetVitals(uint mana, uint stamina)
    {
        Mana = mana;
        Stamina = stamina;
        SendVital(MageTemplate.MaxMana, Mana);
        SendVital(MageTemplate.MaxStamina, Stamina);
    }

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
                    if (CastHook?.Invoke((uint)send.Guid) == true)
                    {
                        break; // M4 P5a C1c: a test answers this spell itself (Lifestone Recall is no spell of the scripted table)
                    }

                    _pending.Enqueue(((uint)send.Guid, 0, now + CastSeconds));
                    break;
                case FakeSendKind.CastTargetedSpell:
                    if (send.Guid == WorldActionRig.Player && CastHook?.Invoke((uint)send.Index) == true)
                    {
                        break; // a self spell goes out at the caster's own guid, as retail's ClientMagicSystem::CastSpell sends it: the untargeted hook answers it
                    }

                    if (TargetedCastHook?.Invoke((uint)send.Index, send.Guid) == true)
                    {
                        break; // M5 3 p1: a test answers this targeted spell itself (Primary Portal Tie is no spell of the scripted table)
                    }

                    _pending.Enqueue(((uint)send.Index, send.Guid, now + CastSeconds));
                    break;
                case FakeSendKind.Use when send.Guid == ManaPotionGuid:
                    DrinkManaPotion();
                    break;
                case FakeSendKind.Use when send.Guid == StaminaPotionGuid:
                    DrinkStaminaPotion();
                    break;
                case FakeSendKind.GetAndWieldItem when AnswerWields:
                    // ACE's wield (Player_Inventory.cs 297-306 @95ef426d): 02DA Wielder 0, 02CE CurrentWieldedLocation 0, then 0023.
                    var item = (uint)send.Guid;
                    var location = (int)send.Amount;
                    _rig.Raw(AceM3Writers.PublicUpdateInstanceId(++_wieldSeq, item, 3, 0));
                    _rig.Raw(AceM3Writers.PublicUpdatePropertyInt(_wieldSeq, item, 10, 0));
                    _rig.Server((p, s) => AceM3Writers.WieldObject(p, s, item, location));
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

    private static Dictionary<uint, SpellInfo> BuildTable(IReadOnlyList<ProfileLine> lines)
    {
        var table = new Dictionary<uint, SpellInfo>();
        var category = 200u;
        foreach (var line in lines)
        {
            category++;
            foreach (var spell in line.Spells)
            {
                var armour = line.Role == ProfileRole.ItemBuffArmor;
                var meta = line.Role switch
                {
                    ProfileRole.ManaTransfer or ProfileRole.LifeDrain => SpellType.Transfer,
                    ProfileRole.StaminaRestore or ProfileRole.HealSelf => SpellType.Boost,
                    ProfileRole.WarBolt or ProfileRole.WarOption => SpellType.Projectile,
                    _ => SpellType.Enchantment,
                };
                var flags = line.Role switch
                {
                    ProfileRole.ItemBuffArmor => SpellFlags.Beneficial,
                    ProfileRole.WarBolt or ProfileRole.WarOption => SpellFlags.Projectile,
                    ProfileRole.LifeDrain => SpellFlags.Resistable | SpellFlags.Reversed,
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
                    BaseRangeConstant = line.Role == ProfileRole.LifeDrain ? 33f : 0f,
                };
            }
        }

        return table;
    }

    /// <summary>A Stamina Potion (ACE <c>Food.BoostVital</c>): the potion's boost in stamina, the line, the stack, the use done (M5 7 F5).</summary>
    private void DrinkStaminaPotion()
    {
        var before = Stamina;
        _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var maxStamina);
        Stamina = Math.Min(maxStamina, Stamina + (uint)Lifestone.Client.Session.Fighter.FighterKit.VitalPotionBoost);
        StaminaPotionsDrunk++;
        SendVital(MageTemplate.MaxStamina, Stamina);
        Chat($"The Stamina Potion restores {Stamina - before} points of your Stamina.");
        StaminaPotions--;
        if (StaminaPotions == 0)
        {
            _rig.Raw(AceM3Writers.InventoryRemoveObject(StaminaPotionGuid));
        }
        else
        {
            _rig.Raw(AceM3Writers.SetStackSize(++_potionSeq, StaminaPotionGuid, StaminaPotions, StaminaPotions * 170));
        }

        _rig.UseDone();
    }

    /// <summary>A Mana Potion (ACE <c>Food.BoostVital</c>): +25 mana, the line, the stack, the use done (M4 P5a C7).</summary>
    private void DrinkManaPotion()
    {
        var before = Mana;
        Mana = Math.Min(MaxMana, Mana + (uint)Lifestone.Client.Session.Fighter.FighterKit.VitalPotionBoost);
        PotionsDrunk++;
        SendVital(MageTemplate.MaxMana, Mana);
        Chat($"The Mana Potion restores {Mana - before} points of your Mana.");
        ManaPotions--;
        if (ManaPotions == 0)
        {
            _rig.Raw(AceM3Writers.InventoryRemoveObject(ManaPotionGuid));
        }
        else
        {
            _rig.Raw(AceM3Writers.SetStackSize(++_potionSeq, ManaPotionGuid, ManaPotions, ManaPotions * 170));
        }

        _rig.UseDone();
    }

    private uint MaxMana => _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max) ? max : uint.MaxValue;

    private void Describe()
    {
        var attributes = ImmutableArray.CreateBuilder<AttributeDesc>();
        foreach (var id in new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self })
        {
            var ranks = _fighter is null ? MageTemplate.TargetOf(MageStatKind.Attribute, id)?.Ranks ?? 0 : _fighter.TargetOf(MageStatKind.Attribute, id)?.Ranks ?? 0;
            attributes.Add(new AttributeDesc(id, (uint)ranks, _fighter is null ? MageTemplate.CreationValue(id) : _fighter.CreationValue(id), 0));
        }

        var skills = ImmutableArray.CreateBuilder<SkillDesc>();
        foreach (var id in new[] { MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.LifeMagic, MageTemplate.WarMagic, MageTemplate.ManaConversion })
        {
            var t = _fighter is null ? MageTemplate.TargetOf(MageStatKind.Skill, id) : _fighter.TargetOf(MageStatKind.Skill, id);
            if (t is null)
            {
                continue; // a fighter trains no War Magic and no Mana Conversion
            }

            skills.Add(new SkillDesc(id, (ushort)(id == MageTemplate.LifeMagic && _lifeMagicRanks > 0 ? (int)_lifeMagicRanks : t.Ranks), 1, t.Specialized ? 3u : 2u, 0, t.Specialized ? 10u : 0u, 0, 0));
        }

        // Vitals: start 0, the ranks the template buys, so the maximum is the formula's (mana 159 + Self 267 = 426, stamina 135 + Endurance 140 = 275).
        var vitals = ImmutableArray.CreateBuilder<VitalDesc>();
        vitals.Add(new VitalDesc(MageTemplate.MaxHealth, 100, 0, 0, 200));
        vitals.Add(new VitalDesc(MageTemplate.MaxStamina, (uint)(_fighter is null ? MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxStamina)! : _fighter.TargetOf(MageStatKind.Vital, MageTemplate.MaxStamina)!).Ranks, 0, 0, Stamina));
        vitals.Add(new VitalDesc(MageTemplate.MaxMana, (uint)(_fighter is null ? MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxMana)! : _fighter.TargetOf(MageStatKind.Vital, MageTemplate.MaxMana)!).Ranks, 0, 0, Mana));

        // The gear kit's own weenies (the wand, the leather set) in their locations, so a gear scenario sees a ready mage and sends no gear step.
        Wear(WandGuid, "Test Wand", 0x8000, GearKit.Wand.Wcid);
        var equipped = new List<EquippedDesc> { new(WandGuid, Wand, 0) };
        for (var i = 0; i < PieceNames.Length; i++)
        {
            Wear(FirstPieceGuid + (uint)i, PieceNames[i], 0x2, GearKit.Armour[i].Wcid);
            equipped.Add(new EquippedDesc(FirstPieceGuid + (uint)i, (uint)GearKit.Armour[i].ValidLocations, 0));
        }

        var pack = new List<ContentDesc>();
        if (ManaPotions > 0)
        {
            var basis = ReducerHarness.Create(ManaPotionGuid, ReducerHarness.Stamps(), null, name: "Mana Potion");
            _rig.H.R.Reducer.OnCreateObject(_rig.H.R.Context, basis with { Weenie = basis.Weenie with { Header = Lifestone.Net.Types.WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, ItemType = 0x80, WeenieClassId = Lifestone.Client.Session.Fighter.FighterKit.ManaPotionWcid } }, isUpdate: false);
            pack.Add(new ContentDesc(ManaPotionGuid, 0));
        }

        if (StaminaPotions > 0)
        {
            var basis = ReducerHarness.Create(StaminaPotionGuid, ReducerHarness.Stamps(), null, name: "Stamina Potion");
            _rig.H.R.Reducer.OnCreateObject(_rig.H.R.Context, basis with { Weenie = basis.Weenie with { Header = Lifestone.Net.Types.WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, ItemType = 0x80, WeenieClassId = Lifestone.Client.Session.Fighter.FighterKit.StaminaPotionWcid } }, isUpdate: false);
            pack.Add(new ContentDesc(StaminaPotionGuid, 0));
        }

        var description = new PlayerDescriptionMsg { Attributes = attributes.ToImmutable(), Skills = skills.ToImmutable(), Vitals = vitals.ToImmutable(), Equipped = [.. equipped], Inventory = [.. pack] };
        _rig.H.Describe(_known is null ? description : description with { Spells = _known.ToImmutableDictionary(id => id, _ => 2f) });
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
                if (StaminaDrainLine.Spells.Any(x => x.Id == info.Id))
                {
                    _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var staminaCap);
                    var drainedStamina = Math.Min(StaminaDrainYield, staminaCap - Stamina);
                    Stamina += drainedStamina;
                    SendVital(MageTemplate.MaxStamina, Stamina);
                    Chat($"You gain {drainedStamina} points of stamina due to casting {info.Name} on {(_rig.H.State.Objects.TryGet(target, out var mark) ? mark.Name : "a creature")}");
                    break;
                }

                if (DrainPicker.Ids.Contains(info.Id))
                {
                    _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var maxHealth);
                    var taken = Math.Min(HealthPerCast, maxHealth - Health);
                    Health += taken;
                    SendVital(MageTemplate.MaxHealth, Health);
                    Chat($"You gain {taken} points of health due to casting {info.Name} on {(_rig.H.State.Objects.TryGet(target, out var victim) ? victim.Name : "a creature")}");
                    break;
                }

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
                if (VitalsMeans.HealSelf.Contains(info.Id))
                {
                    _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var healthMax);
                    var healed = Math.Min(HealthPerCast, healthMax - Health);
                    Health += healed;
                    SendVital(MageTemplate.MaxHealth, Health);
                    Chat($"You cast {info.Name} and restore {healed} points of your Health.");
                    break;
                }

                var boost = BuffStatMods.Revitalize.First(b => b.Id == info.Id);
                _rig.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var maxStamina);
                var gained = (uint)(boost.Boost + (boost.Variance / 2));
                Stamina = Math.Min(maxStamina, Stamina + gained);
                SendVital(MageTemplate.MaxStamina, Stamina);
                Chat($"You cast {info.Name} and restore {gained} points of your Stamina."); // ACE's self-boost line (the cast proof reads " and restore ")
                break;
            case SpellType.Projectile when ProjectileHook is not null:
                ProjectileHook(spellId, target);
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
                if (BuffStatMods.BySpell.TryGetValue(id, out var mod) && mod.IsSkill && mod.IsAdditive && mod.Key == MageTemplate.ManaConversion)
                {
                    additive += (int)mod.Value;
                }
            }

            var formula = (uint)Math.Round((focus + self) / (double)ManaConversionDivisor, MidpointRounding.AwayFromZero);
            return (uint)Math.Max(0, (int)(formula + skill.Init + skill.Ranks) + additive);
        }
    }

    /// <summary>ACE <c>Creature.CalculateManaUsage</c> (<c>Creature_Magic.cs</c> 14-56) from the base cost <paramref name="baseCost"/> (the ceiling: base mana plus the armour surcharge); the rolls are <see cref="ManaConversionModel.RollCost"/>'s.</summary>
    private uint CalculateManaUsage(SpellInfo info, uint baseCost)
    {
        // line 43: IgnoresManaConversion pays the base (the trained check of the same line holds: Mana Conversion is trained later; skill 0 pays the base too); line 48: the wand has no ManaConversionMod, so GetWeaponManaConversionModifier is 1
        var skill = (uint)Math.Round(ManaConversionSkill * 1.0);
        return ManaConversionModel.RollCost(baseCost, info.Power, skill, _nextManaRoll, (info.Flags & SpellFlags.IgnoresManaConversion) != 0);
    }

    /// <summary>ACE's <c>EnchantmentManager.Add</c> for a player caster and target: a repeat of the spell refreshes its layer, another spell of the category takes the next layer; GameEvent 0x02C2.</summary>
    private void Enchant(SpellInfo info)
    {
        // M4 P5a C4: the fighters' weapon-mastery lines have no BuffStatMods row (the mage has none): a stand-in additive row with value 0, so the enchantment arrives and changes no stat; the cycle judges a line by its spell being enchanted.
        var mod = BuffStatMods.BySpell.TryGetValue(info.Id, out var known) ? known : new BuffStatMod(info.Id, 0x9001, 1, 0.0);
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

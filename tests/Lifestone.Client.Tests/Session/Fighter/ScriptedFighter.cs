using System.Globalization;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.World.Data;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// A model of ACE answering the fighter build over <see cref="ScriptedAceSession"/> (M4 1.1 s2; the stand-in, which has no Login or EnterWorld, cannot run a build): the mod's <c>xp</c> (levels and
/// credits from the table), <c>item</c> (a pack item per grant: the 0x0022 line, the reply, then its CreateObject), <c>place poi</c> (the teleport, the position and the unhide), the four stat actions as
/// <see cref="ScriptedMage"/> answers them, and a wield or unwield (GameEvent 0x0023 or the move to the pack) read from the session's world actions by <see cref="Pump"/>, which the test's loop calls every
/// step. The character keeps its pack, equipment and position between runs, and a login describes them (the objects, the description's pack and equipment lists, the position in Holtburg when it stands there).
/// </summary>
internal sealed class ScriptedFighter
{
    private const uint TotalExperienceKey = 1;
    private const uint AvailableExperienceKey = 2;
    private const uint CreditsKey = 24;
    private const uint LevelKey = 25;

    /// <summary>ACE's trained costs for the template's skills (Aluvian SkillTable), independent of the scenario's copy.</summary>
    internal static readonly IReadOnlyDictionary<uint, int> AceTrainedCost = new Dictionary<uint, int> { [6] = 10, [21] = 6, [47] = 6, [31] = 8, [32] = 8, [33] = 12, [16] = 6 };

    internal const uint FirstItemGuid = 0x80020000;

    private readonly XpTableData _table;
    private readonly ScriptedAceSession _server;
    private readonly uint[] _attributeRanks = new uint[6];
    private readonly uint[] _attributeExperience = new uint[6];
    private readonly uint[] _vitalRanks = new uint[3];
    private readonly uint[] _vitalExperience = new uint[3];
    private readonly SortedDictionary<uint, SkillState> _skills = [];
    private readonly List<PackItem> _items = [];
    private readonly TeleportDestination _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
    private FighterCreation _creation;
    private uint _oldGuid;
    private byte _sequence;
    private uint _eventSequence = 1000;
    private uint _nextGuid = FirstItemGuid;
    private int _wieldsSeen;
    private int _itemsAnnounced;
    private bool _teleportDue;
    private ushort _teleportStamp = 5;
    private ushort _positionStamp = 30;

    private ScriptedFighter(XpTableData table, ScriptedAceSession server, string name)
    {
        Template = FighterTemplate.ForName(name) ?? FighterTemplate.Hunter; // M4 P4 arch: the Archer's kit is the Longbow and the Greater Frost Arrows, so the scripted fighter follows its name's template
        Gear = Template.Kit;
        _creation = Template.Creation;
        _table = table;
        _server = server;
        Name = name;
    }

    private sealed record SkillState(ushort Ranks, uint Class, uint Experience);

    /// <summary>An item the character holds: where it is is its equip location (0: in the pack).</summary>
    internal sealed record PackItem(uint Guid, GearItem Kind, int Stack, uint Location, uint Container = 0);

    public string Name { get; }

    /// <summary>The template of the character's name (the Hunter's for any other name): its kit and targets are what the fighter holds and what the item tool knows.</summary>
    public FighterTemplate Template { get; }

    /// <summary>The kit the item tool knows: the template's, or (after <see cref="WithComponents"/>) the template's with the spell components of an account (M4 P5a fix F-abil).</summary>
    public FighterKit Gear { get; private set; } = null!;

    /// <summary>
    /// The server grants the spell components of <paramref name="components"/> too (the item tool knows their weenies); with <paramref name="held"/> the fighter holds them in the pack already, one stack of the kit's count each (a built fighter).
    /// </summary>
    public ScriptedFighter WithComponents(IReadOnlyList<FighterItem> components, bool held)
    {
        Gear = Template.Kit.WithComponents(components);
        // M4 P5a fix r4: a built fighter holds the packs its kit carries (sized from the component lines: FighterKit.SidePacksFor).
        var pack = Gear.Items.Single(i => i.Role == FighterRole.SidePack);
        for (var have = _items.Count(i => i.Kind.Wcid == FighterKit.SidePackWcid); have < Gear.SidePacks; have++)
        {
            _items.Add(new PackItem(_nextGuid++, pack.Item with { Stack = 1 }, 1, 0));
        }

        if (held)
        {
            foreach (var line in components)
            {
                _items.Add(new PackItem(_nextGuid++, line.Item, line.Item.Stack, 0));
            }
        }

        return this;
    }

    /// <summary>A cast burned <paramref name="count"/> of the component stack: the held stack of <paramref name="wcid"/> goes down by that many (gone at 0).</summary>
    public void Burn(uint wcid, int count)
    {
        var index = _items.FindIndex(i => i.Kind.Wcid == wcid);
        var left = _items[index].Stack - count;
        if (left <= 0)
        {
            _items.RemoveAt(index);
        }
        else
        {
            _items[index] = _items[index] with { Stack = left };
        }
    }

    /// <summary>The count held of the weenie across its stacks.</summary>
    public int HeldCount(uint wcid) => _items.Where(i => i.Kind.Wcid == wcid).Sum(i => i.Stack);

    public long Total { get; set; }

    public long Available { get; set; }

    public int Level { get; set; } = 1;

    public int Credits { get; set; } = 18;

    public bool InHoltburg { get; set; }

    public List<long> XpAsked { get; } = [];

    public List<uint> ItemWcidsAsked { get; } = [];

    public List<string> Refusals { get; } = [];

    public IReadOnlyList<PackItem> Items => _items;

    /// <summary>The spell ids the character knows (its spellbook; the description lists them at a login).</summary>
    public HashSet<uint> Known { get; } = [];

    /// <summary>The number of ids in each <c>spell learn</c> request (M4 P5a).</summary>
    public List<int> LearnSizes { get; } = [];

    /// <summary>Wcids the mod refuses as unknown (a test's fault injection).</summary>
    public HashSet<uint> UnknownWcids { get; } = [];

    /// <summary>The StackableMerges the rig applied (M4 P5a fix r4).</summary>
    public int MergesSeen { get; private set; }

    private readonly Dictionary<uint, byte> _stackSequences = [];

    private bool _viewContentsDue;

    private byte NextStackSequence(uint guid)
    {
        var next = (byte)(_stackSequences.GetValueOrDefault(guid) + 1);
        _stackSequences[guid] = next;
        return next;
    }

    /// <summary>
    /// M4 P5a fix r4: <paramref name="count"/> stacks of <paramref name="size"/> of <paramref name="line"/> in the side pack at <paramref name="pack"/> (0-based among the held packs), as reruns' unmerged top-ups left them in round 4.
    /// </summary>
    public void AddInSidePack(GearItem line, int size, int pack, int count = 1)
    {
        var packs = _items.Where(i => i.Kind.Wcid == FighterKit.SidePackWcid).ToArray();
        for (var i = 0; i < count; i++)
        {
            _items.Add(new PackItem(_nextGuid++, line with { Stack = size }, size, 0, packs[pack].Guid));
        }
    }

    public static ScriptedFighter Fresh(XpTableData table, ScriptedAceSession server, string name)
    {
        var fighter = new ScriptedFighter(table, server, name);
        fighter.InitSkills();
        return fighter;
    }

    private void InitSkills()
    {
        foreach (var t in Template.Targets.Where(t => t.Kind == MageStatKind.Skill))
        {
            if (Template.TrainedLater.Contains(t.Id))
            {
                _skills[t.Id] = new SkillState(0, HeadlessCharacter.UntrainedSkill, 0); // trained after creation (M4 P5a): untrained until the build trains it
                continue;
            }

            // ACE's PlayerFactory gives a creation-trained skill 5 ranks and 526 XP (Player_Skills.cs:182-186).
            _skills[t.Id] = new SkillState(ScriptedMage.CreationBonusRanks, t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill, ScriptedMage.CreationBonusXp);
        }
    }

    /// <summary>
    /// A fighter that a character-creation of the OLD templates made (M4 P5a C5): the legacy level-40 state, created with the Hunter's split (Strength, Endurance and Coordination 100), which for the Brawler and the Archer is not their template's. When the server lists a new live character of its
    /// name (the re-creation after a retail delete), it becomes a fresh character with the template's creation attributes.
    /// </summary>
    public static ScriptedFighter OldSplit(XpTableData table, ScriptedAceSession server, string name)
    {
        var fighter = Built(table, server, name, BuildState.Legacy);
        fighter._creation = FighterTemplate.Hunter.Creation;
        fighter._oldGuid = server.Characters.First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) && c.DeleteField == 0).Guid;
        return fighter;
    }

    private void BecomeFresh()
    {
        Array.Clear(_attributeRanks);
        Array.Clear(_attributeExperience);
        Array.Clear(_vitalRanks);
        Array.Clear(_vitalExperience);
        _skills.Clear();
        _items.Clear();
        Known.Clear();
        Total = 0;
        Available = 0;
        Level = 1;
        Credits = 18;
        InHoltburg = false;
        _creation = Template.Creation;
        _oldGuid = 0;
        _wieldsSeen = 0;
        _itemsAnnounced = 0;
        InitSkills();
    }

    /// <summary>A built fighter: the template's level (40 for the Hunter, 55 for the Brawler and the Archer), the plan spent (the three magic schools trained and raised), its profile's spells known, the kit held, the Mace wielded, the leather worn, standing in Holtburg.</summary>
    public static ScriptedFighter Ready(XpTableData table, ScriptedAceSession server, string name) => Built(table, server, name, BuildState.Ready);

    /// <summary>A fighter as it was built before M4 P5a (owner decision D33): level 40, the base plan spent, the magic schools untrained, no spell known, the kit held and worn. The Hunter is the same either way.</summary>
    public static ScriptedFighter Legacy(XpTableData table, ScriptedAceSession server, string name) => Built(table, server, name, BuildState.Legacy);

    /// <summary>A fighter as lane C1 built it (M4 P5a, D33 alone): level 48, the base plan spent plus Focus, Self, Creature Enchantment and Life Magic at C1's ranks (CE 127, LM 126 or 127: above this plan's 125), those two schools trained, Item Enchantment untrained, every spell but Lifestone Recall known; the experience spent is exactly the plan's, the rest unspent (M4 P5a C1c).</summary>
    public static ScriptedFighter C1Level48(XpTableData table, ScriptedAceSession server, string name) => Built(table, server, name, BuildState.C1);

    /// <summary>The level-40 plan as the fighters were first built (the targets of the base shares, before any D33 or D38 extension).</summary>
    private static List<StatTarget> OldBasePlan(FighterTemplate template)
    {
        StatTarget Skill(uint id, bool specialized, int ranks) => new(MageStatKind.Skill, id, FighterTemplate.SkillName(id), specialized, ranks);
        StatTarget Attribute(uint id, string name, int ranks) => new(MageStatKind.Attribute, id, name, false, ranks);
        StatTarget Vital(uint id, string name, int ranks) => new(MageStatKind.Vital, id, name, false, ranks);
        return template.Key switch
        {
            "melee" =>
            [
                Skill(FighterTemplate.HeavyWeapons, true, 145), Skill(FighterTemplate.MeleeDefense, false, 112), Skill(FighterTemplate.Healing, false, 98),
                Attribute(FighterTemplate.Strength, "Strength", 99), Attribute(FighterTemplate.Endurance, "Endurance", 94), Attribute(FighterTemplate.Coordination, "Coordination", 99),
                Vital(FighterTemplate.MaxHealth, "Max Health", 91), Vital(FighterTemplate.MaxStamina, "Max Stamina", 90),
            ],
            "archer" =>
            [
                Skill(FighterTemplate.MissileWeapons, true, 145), Skill(FighterTemplate.MeleeDefense, false, 112), Skill(FighterTemplate.Healing, false, 98),
                Attribute(FighterTemplate.Endurance, "Endurance", 99), Attribute(FighterTemplate.Coordination, "Coordination", 103),
                Vital(FighterTemplate.MaxHealth, "Max Health", 99), Vital(FighterTemplate.MaxStamina, "Max Stamina", 91),
            ],
            _ => [.. template.Targets],
        };
    }

    private enum BuildState
    {
        Ready,
        Legacy,
        C1,
    }

    private static ScriptedFighter Built(XpTableData table, ScriptedAceSession server, string name, BuildState state)
    {
        var fighter = Fresh(table, server, name);
        var template = fighter.Template;
        var level = state switch { BuildState.Ready => template.Level, BuildState.C1 when template.HasMagic => 48, _ => FighterTemplate.TargetLevel };
        fighter.Total = (long)table.XpForLevel(level);
        fighter.Level = level;
        fighter.Available = 1_000;
        fighter.Credits = 30;
        if (state == BuildState.Ready && FighterProfile.For(template) is { } profile)
        {
            fighter.Known.UnionWith(profile.SpellsToLearn);
        }

        IEnumerable<StatTarget> targets = template.Targets;
        if (state != BuildState.Ready)
        {
            // The plans the characters were first built to, hard-coded (the stand-in XpTable is not the retail one, so the rule is not re-run): the base plan (level 40) and, for C1's build, its four stats on top.
            var basePlan = OldBasePlan(template);
            if (state == BuildState.C1 && template.HasMagic)
            {
                var lm = template.Key == "melee" ? 126 : 127;
                basePlan.Add(new StatTarget(MageStatKind.Attribute, FighterTemplate.Focus, "Focus", false, 96));
                basePlan.Add(new StatTarget(MageStatKind.Attribute, FighterTemplate.Self, "Self", false, 106));
                basePlan.Add(new StatTarget(MageStatKind.Skill, FighterTemplate.CreatureEnchantment, "Creature Enchantment", false, 127));
                basePlan.Add(new StatTarget(MageStatKind.Skill, FighterTemplate.LifeMagic, "Life Magic", false, lm));
                fighter.Known.UnionWith(FighterProfile.For(template)!.SpellsToLearn.Where(id => id != 1635));
                fighter.Credits = 30 - 20;
            }

            targets = basePlan;
        }

        ulong spent = 0;
        foreach (var t in targets)
        {
            var experience = t.XpList(table)[t.Ranks];
            spent += experience;
            switch (t.Kind)
            {
                case MageStatKind.Attribute:
                    fighter._attributeRanks[t.Id - 1] = (uint)t.Ranks;
                    fighter._attributeExperience[t.Id - 1] = experience;
                    break;
                case MageStatKind.Vital:
                    fighter._vitalRanks[VitalIndex(t.Id)] = (uint)t.Ranks;
                    fighter._vitalExperience[VitalIndex(t.Id)] = experience;
                    break;
                default:
                    fighter._skills[t.Id] = new SkillState((ushort)t.Ranks, t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill, experience);
                    break;
            }
        }

        if (state == BuildState.C1 && template.HasMagic)
        {
            fighter.Available = (long)((ulong)fighter.Total - spent);
        }

        foreach (var line in fighter.Template.Kit.Items)
        {
            for (var i = 0; i < (line.Item.MaxStack is null ? line.Item.Stack : 1); i++)
            {
                // M4 P5a C7: the Mana and Stamina Potions live in a side pack, as the build leaves them.
                var container = line.Role == FighterRole.VitalPotion ? fighter._items.First(x => x.Kind.Wcid == FighterKit.SidePackWcid).Guid : 0u;
                fighter._items.Add(new PackItem(fighter._nextGuid++, line.Item, line.Item.MaxStack is null ? 1 : line.Item.Stack, line.Wield ? (uint)line.Item.ValidLocations : 0, container));
            }
        }

        fighter.InHoltburg = true;
        return fighter;
    }

    /// <summary>Trains Arcane Lore (M4 P5a C6) with <paramref name="ranks"/> ranks, so the magic gear's difficulties are met.</summary>
    public void TrainArcaneLore(ushort ranks) => _skills[14] = new SkillState(ranks, HeadlessCharacter.TrainedSkill, 0);

    public void Untrain(uint skill) => _skills[skill] = new SkillState(0, HeadlessCharacter.UntrainedSkill, 0);

    public void DropItems(uint wcid) => _items.RemoveAll(i => i.Kind.Wcid == wcid);

    /// <summary>Wield guids the model refused as ACE's <c>CheckWeaponCollision</c> does: a melee weapon while a launcher is wielded, a launcher while a melee weapon is (M4 3.1 fix2; the live round's WeenieError 0).</summary>
    public List<uint> CollisionRefusals { get; } = [];

    /// <summary>A second item of a kit class in the pack, as the hunt's loot adds one (M4 3.1 fix2); the guid.</summary>
    public uint AddCopy(uint wcid)
    {
        var kind = Template.Kit.ItemOf(wcid)!.Item;
        var guid = _nextGuid++;
        _items.Add(new PackItem(guid, kind, kind.MaxStack is null ? 1 : kind.Stack, 0));
        return guid;
    }

    /// <summary>Leaves the first item of the class at <paramref name="location"/> without a message (the state a half-done weapon swap leaves, before the login describes it); the guid.</summary>
    public uint Hold(uint wcid, EquipMask location)
    {
        var index = _items.FindIndex(i => i.Kind.Wcid == wcid && i.Location == 0);
        _items[index] = _items[index] with { Location = (uint)location };
        return _items[index].Guid;
    }

    /// <summary>An item of any kind in the pack (a leftover of an older build, as the Archer's Shortbow and Arrows beside its Longbow and Greater Frost Arrows); the guid.</summary>
    public uint AddOther(GearItem kind)
    {
        var guid = _nextGuid++;
        _items.Add(new PackItem(guid, kind, kind.MaxStack is null ? 1 : kind.Stack, 0));
        return guid;
    }

    /// <summary>Puts the first wielded item of the class back in the pack without a message; the guid.</summary>
    public uint PutAway(uint wcid)
    {
        var index = _items.FindIndex(i => i.Kind.Wcid == wcid && i.Location != 0);
        _items[index] = _items[index] with { Location = 0 };
        return _items[index].Guid;
    }

    /// <summary>The items held at <paramref name="location"/> (any bit of it).</summary>
    public IReadOnlyList<PackItem> At(EquipMask location) => [.. _items.Where(i => (i.Location & (uint)location) != 0)];

    public (uint Ranks, uint Experience) AttributeOf(uint id) => (_attributeRanks[id - 1], _attributeExperience[id - 1]);

    public (ushort Ranks, uint Class, uint Experience) SkillOf(uint id) => (_skills[id].Ranks, _skills[id].Class, _skills[id].Experience);

    public void Attach()
    {
        var fallback = _server.Description;
        _server.Description = name => string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) ? Describe() : fallback(name);
        _server.StatHook = OnStat;
        _server.ToolHook = OnTool;
    }

    private static int VitalIndex(uint id) => id switch
    {
        FighterTemplate.MaxHealth => 0,
        FighterTemplate.MaxStamina => 1,
        5 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "not a vital's maximum id"),
    };

    private AcePlayerDescription Describe()
    {
        // The login's objects first (the pack and the equipment, then where the character stands); the description lists them.
        if (_oldGuid != 0 && _server.Characters.Any(c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase) && c.DeleteField == 0 && c.Guid != _oldGuid))
        {
            BecomeFresh(); // the old character was deleted and a new one of the name created
        }

        var player = _server.Characters.First(c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase) && c.DeleteField == 0).Guid;
        foreach (var item in _items)
        {
            Send(AceWireWriters.ObjectCreate(Created(item, player)));
        }

        _itemsAnnounced = _items.Count;
        _viewContentsDue = true; // the side packs' contents follow the description (ACE's 0196 per pack), at the next Pump
        if (InHoltburg)
        {
            var p = _holtburg.Location.Position;
            Send(AceWireWriters.UpdatePosition(player, new AcePosition(p.CellId, p.Local.X, p.Local.Y, p.Local.Z), true, new AceUpdatePositionSequences(1, ++_positionStamp, 1, 1)));
        }

        var baseline = AcePlayerDescription.NewCharacter(Name);
        return baseline with
        {
            Ints = [.. baseline.Ints.Where(p => p.Key is not (CreditsKey or LevelKey)), (CreditsKey, Credits), (LevelKey, Level)],
            Int64s = [(TotalExperienceKey, Total), (AvailableExperienceKey, Available)],
            Attributes = [.. Enumerable.Range(0, 6).Select(i => (_attributeRanks[i], _creation[(uint)i + 1], _attributeExperience[i]))],
            Vitals = [.. Enumerable.Range(0, 3).Select(i => (_vitalRanks[i], 0u, _vitalExperience[i], 10u))],
            Skills = [.. _skills.Select(s => new AceSkill(s.Key, s.Value.Ranks, s.Value.Class, s.Value.Experience, 0))],
            Spells = [.. Known.Select(id => (int)id)],
            Inventory = [.. _items.Where(i => i.Location == 0 && i.Container == 0).Select(i => (i.Guid, i.Kind.ItemType == FighterKit.ItemTypeContainer ? 1u : 0u))],
            Equipped = [.. _items.Where(i => i.Location != 0).Select(i => (i.Guid, i.Location, 0u))],
        };
    }

    private AceCreateObject Created(PackItem item, uint owner) => new()
    {
        Guid = item.Guid,
        PhysicsState = (uint)(PhysicsState.IgnoreCollisions | PhysicsState.Ethereal),
        Setup = 0x02000B77,
        Sequences = new ushort[9],
        Name = item.Kind.Name,
        WeenieClassId = item.Kind.Wcid,
        Icon = 0x06001036,
        ItemType = item.Kind.ItemType,
        Container = item.Location == 0 ? (item.Container != 0 ? item.Container : owner) : null,
        Wielder = item.Location != 0 ? owner : null,
        ValidLocations = item.Kind.ValidLocations == EquipMask.None ? null : (uint)item.Kind.ValidLocations,
        CurrentlyWieldedLocation = item.Location == 0 ? null : item.Location,
        StackSize = item.Kind.MaxStack is null ? null : (ushort)item.Stack,
        MaxStackSize = item.Kind.MaxStack is { } max ? (ushort)max : null,
    };

    private static int RankFor(IReadOnlyList<uint> list, ulong experience)
    {
        var rank = 0;
        for (var i = 1; i < list.Count && list[i] <= experience; i++)
        {
            rank = i;
        }

        return rank;
    }

    private void Send(byte[] body) => _server.Inject(body);

    private void Refuse(string stat) => Send(AceWireWriters.ServerMessage($"Your attempt to raise {stat} has failed.", 0));

    private uint Player => _server.PlayerGuid!.Value;

    private void OnStat(uint type, uint id, uint amount)
    {
        switch (type)
        {
            case GameActionTypes.RaiseAttribute when id is >= 1 and <= 6:
                if (amount == 0 || amount > Available)
                {
                    return;
                }

                var a = (int)id - 1;
                if (amount > _table.AttributeXpList[^1] - _attributeExperience[a])
                {
                    Refuse("attribute " + id);
                    return;
                }

                _attributeExperience[a] += amount;
                _attributeRanks[a] = (uint)RankFor(_table.AttributeXpList, _attributeExperience[a]);
                Available -= amount;
                Send(AceWireWriters.PrivateUpdatePropertyInt64(++_sequence, AvailableExperienceKey, Available));
                Send(AceWireWriters.PrivateUpdateAttribute(++_sequence, id, _attributeRanks[a], _creation[id], _attributeExperience[a]));
                break;
            case GameActionTypes.RaiseVital when id is FighterTemplate.MaxHealth or FighterTemplate.MaxStamina or 5:
                if (amount == 0 || amount > Available)
                {
                    return;
                }

                var v = VitalIndex(id);
                if (amount > _table.VitalXpList[^1] - _vitalExperience[v])
                {
                    Refuse("vital " + id);
                    return;
                }

                _vitalExperience[v] += amount;
                _vitalRanks[v] = (uint)RankFor(_table.VitalXpList, _vitalExperience[v]);
                Available -= amount;
                Send(AceWireWriters.PrivateUpdatePropertyInt64(++_sequence, AvailableExperienceKey, Available));
                Send(AceWireWriters.PrivateUpdateVital(++_sequence, id, _vitalRanks[v], 0, _vitalExperience[v], 10));
                break;
            case GameActionTypes.RaiseSkill when _skills.TryGetValue(id, out var s):
                if (amount == 0 || amount > Available || s.Class < HeadlessCharacter.TrainedSkill)
                {
                    return;
                }

                var list = s.Class == HeadlessCharacter.SpecializedSkill ? _table.SpecializedSkillXpList : _table.TrainedSkillXpList;
                if (amount > list[^1] - s.Experience)
                {
                    Refuse("skill " + id);
                    return;
                }

                var experience = s.Experience + amount;
                var raised = s with { Experience = experience, Ranks = (ushort)RankFor(list, experience) };
                _skills[id] = raised;
                Available -= amount;
                Send(AceWireWriters.PrivateUpdatePropertyInt64(++_sequence, AvailableExperienceKey, Available));
                Send(AceWireWriters.PrivateUpdateSkill(++_sequence, new AceSkill(id, raised.Ranks, raised.Class, raised.Experience, 0)));
                break;
            case GameActionTypes.TrainSkill when _skills.TryGetValue(id, out var t):
                var credits = unchecked((int)amount);
                if (t.Class != HeadlessCharacter.UntrainedSkill || credits <= 0 || credits > Credits || !AceTrainedCost.TryGetValue(id, out var cost) || credits != cost)
                {
                    return;
                }

                _skills[id] = t with { Class = HeadlessCharacter.TrainedSkill };
                Credits -= credits;
                Send(AceWireWriters.PrivateUpdateSkill(++_sequence, new AceSkill(id, 0, HeadlessCharacter.TrainedSkill, 0, 0)));
                Send(AceWireWriters.PrivateUpdatePropertyInt(++_sequence, CreditsKey, Credits));
                break;
            default:
                break;
        }
    }

    private string? OnTool(ToolRequest request)
    {
        switch (request)
        {
            case XpRequest xp:
                XpAsked.Add(xp.Amount);
                var oldLevel = Level;
                Total += xp.Amount;
                Available += xp.Amount;
                while (Level < _table.LevelCap && (ulong)Total >= _table.XpForLevel(Level + 1))
                {
                    Level++;
                }

                Credits += (int)_table.SkillCreditsThroughLevel(Level, oldLevel);
                Send(AceWireWriters.PrivateUpdatePropertyInt64(++_sequence, TotalExperienceKey, Total));
                Send(AceWireWriters.PrivateUpdatePropertyInt64(++_sequence, AvailableExperienceKey, Available));
                Send(AceWireWriters.PrivateUpdatePropertyInt(++_sequence, LevelKey, Level));
                Send(AceWireWriters.PrivateUpdatePropertyInt(++_sequence, CreditsKey, Credits));
                var data = new XpData(xp.Amount, xp.Amount, Total, Available, Level);
                return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Xp, data), data.Describe());
            case SpellLearnRequest learn:
                LearnSizes.Add(learn.SpellIds.Count);
                var outcomes = new List<SpellOutcome>();
                foreach (var id in learn.SpellIds)
                {
                    var inTable = SpellTable.Contains(id);
                    var known = Known.Contains(id);
                    outcomes.Add(new SpellOutcome(id, SpellOutcomes.Classify(inTable, known), inTable ? "spell " + id : null));
                    if (inTable)
                    {
                        Known.Add(id);
                    }
                }

                var spells = SpellLearnData.Of(outcomes);
                return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Spell, spells), spells.Describe());
            case ItemRequest item:
                return Item(item);
            case PlacePoiRequest poi when string.Equals(poi.Name, _holtburg.Name, StringComparison.OrdinalIgnoreCase) || poi.Name.Contains("Holtburg", StringComparison.OrdinalIgnoreCase):
                var at = _holtburg.Location.Position;
                var placed = new PlaceData("poi", poi.Name, ToolsWire.FormatCell(at.CellId), at.Local.X, at.Local.Y, at.Local.Z, 1f, 0f, 0f, 0f);
                _teleportDue = true;
                InHoltburg = true;
                return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.PlacePoi, placed), placed.Describe());
            default:
                return null;
        }
    }

    /// <summary>The spell ids the model's SpellTable holds (what the mage's and the fighters' profiles teach, and the profile of the Archer and the Brawler).</summary>
    private static readonly HashSet<uint> SpellTable = [.. ArenaProfile.SpellsToLearn, .. FighterProfile.Brawler.SpellsToLearn, .. FighterProfile.Archer.SpellsToLearn];

    private string Item(ItemRequest request)
    {
        ItemWcidsAsked.Add(request.Wcid);
        var line = Gear.ItemOf(request.Wcid);
        if (line is null || UnknownWcids.Contains(request.Wcid))
        {
            var error = new ToolError(ToolErrors.UnknownWcid, "no weenie has class id " + request.Wcid.ToString(CultureInfo.InvariantCulture));
            Refusals.Add(error.Message);
            return ToolsWire.Reply(ToolReply.Failure(request.Id, ToolVerbs.Item, error), error.Message);
        }

        var max = line.Item.MaxStack;
        var stack = max is null ? 1 : Math.Min(request.Stack ?? 1, max.Value);
        var clamped = max is null ? request.Stack is not null : request.Stack > max;
        var kind = line.Item with { Stack = stack };
        var guid = _nextGuid++;
        _items.Add(new PackItem(guid, kind, stack, 0));
        var data = new ItemData(guid, kind.Wcid, kind.Name, stack, request.Stack, max, clamped);
        Send(Lifestone.Tests.Shared.Net.AceM3Writers.PutObjInContainer(Player, ++_eventSequence, guid, Player, (uint)_items.Count - 1, kind.ItemType == FighterKit.ItemTypeContainer ? 1u : 0u));
        _itemsAnnounced = _items.Count - 1; // its CreateObject follows the reply, at the next Pump
        return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Item, data), data.Describe());
    }

    /// <summary>
    /// Answers what the session sent that no hook answers: the CreateObject of a granted item (after its reply), a teleport after the place's reply, and each wield (GameEvent 0x0023) or move to the pack.
    /// Call once per step of the run.
    /// </summary>
    public void Pump()
    {
        while (_itemsAnnounced < _items.Count)
        {
            Send(AceWireWriters.ObjectCreate(Created(_items[_itemsAnnounced++], Player)));
        }

        if (_viewContentsDue && _server.PlayerGuid is { } owner)
        {
            _viewContentsDue = false;
            foreach (var pack in _items.Where(i => i.Kind.Wcid == FighterKit.SidePackWcid && _items.Any(c => c.Container == i.Guid)).ToArray())
            {
                Send(AceM3Writers.ViewContents(owner, ++_eventSequence, pack.Guid, [.. _items.Where(c => c.Container == pack.Guid).Select(c => (c.Guid, 0u))]));
            }
        }

        if (_teleportDue)
        {
            _teleportDue = false;
            var at = _holtburg.Location.Position;
            Send(AceWireWriters.PlayerTeleport(++_teleportStamp));
            Send(AceWireWriters.UpdatePosition(Player, new AcePosition(at.CellId, at.Local.X, at.Local.Y, at.Local.Z), true, new AceUpdatePositionSequences(1, ++_positionStamp, _teleportStamp, 1)));
            Send(AceWireWriters.SetState(Player, (uint)(PhysicsState.Gravity | PhysicsState.EdgeSlide), 1, (ushort)(40 + _teleportStamp)));
        }

        var sends = _server.WorldActionSends;
        while (_wieldsSeen < sends.Count)
        {
            var (type, first, second, third) = sends[_wieldsSeen++];
            var index = _items.FindIndex(i => i.Guid == first);
            if (index < 0)
            {
                continue;
            }

            if (type == GameActionTypes.GetAndWieldItem)
            {
                var melee = (uint)EquipMask.MeleeWeapon;
                var launcher = (uint)EquipMask.MissileWeapon;
                if (((second & melee) != 0 && _items.Any(i => (i.Location & launcher) != 0)) || ((second & launcher) != 0 && _items.Any(i => (i.Location & melee) != 0)))
                {
                    CollisionRefusals.Add(first);
                    Send(AceM3Writers.InventoryServerSaysFailed(Player, ++_eventSequence, first, 0));
                    continue;
                }

                _items[index] = _items[index] with { Location = second };
                Send(AceM3Writers.WieldObject(Player, ++_eventSequence, first, unchecked((int)second)));
            }
            else if (type == GameActionTypes.StackableMerge && _items.FindIndex(i => i.Guid == second) is var to and >= 0)
            {
                // M4 P5a fix r4: ACE's whole-stack merge: the source leaves (0024) before the target's new size (0197); a part merge shrinks the source first.
                var moved = Math.Min((int)third, _items[index].Stack);
                var left = _items[index].Stack - moved;
                var target = _items[to] with { Stack = _items[to].Stack + moved };
                _items[to] = target;
                if (left <= 0)
                {
                    _items.RemoveAt(index);
                    if (index < _itemsAnnounced)
                    {
                        _itemsAnnounced--;
                    }

                    Send(AceM3Writers.InventoryRemoveObject(first));
                }
                else
                {
                    _items[index] = _items[index] with { Stack = left };
                    Send(AceM3Writers.SetStackSize(NextStackSequence(first), first, (uint)left, 0));
                }

                MergesSeen++;
                Send(AceM3Writers.SetStackSize(NextStackSequence(second), second, (uint)target.Stack, 0));
            }
            else if (type == GameActionTypes.PutItemInContainer)
            {
                _items[index] = _items[index] with { Location = 0, Container = second == Player ? 0 : second };
                Send(AceM3Writers.PutObjInContainer(Player, ++_eventSequence, first, second, third, 0));
            }
        }
    }
}

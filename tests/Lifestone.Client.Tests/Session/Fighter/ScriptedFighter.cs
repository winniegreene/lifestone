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
    internal static readonly IReadOnlyDictionary<uint, int> AceTrainedCost = new Dictionary<uint, int> { [6] = 10, [21] = 6, [47] = 6 };

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
        _table = table;
        _server = server;
        Name = name;
    }

    private sealed record SkillState(ushort Ranks, uint Class, uint Experience);

    /// <summary>An item the character holds: where it is is its equip location (0: in the pack).</summary>
    internal sealed record PackItem(uint Guid, GearItem Kind, int Stack, uint Location);

    public string Name { get; }

    public long Total { get; set; }

    public long Available { get; set; }

    public int Level { get; set; } = 1;

    public int Credits { get; set; } = 18;

    public bool InHoltburg { get; set; }

    public List<long> XpAsked { get; } = [];

    public List<uint> ItemWcidsAsked { get; } = [];

    public List<string> Refusals { get; } = [];

    public IReadOnlyList<PackItem> Items => _items;

    /// <summary>Wcids the mod refuses as unknown (a test's fault injection).</summary>
    public HashSet<uint> UnknownWcids { get; } = [];

    public static ScriptedFighter Fresh(XpTableData table, ScriptedAceSession server, string name)
    {
        var fighter = new ScriptedFighter(table, server, name);
        foreach (var t in FighterTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill))
        {
            // ACE's PlayerFactory gives a creation-trained skill 5 ranks and 526 XP (Player_Skills.cs:182-186).
            fighter._skills[t.Id] = new SkillState(ScriptedMage.CreationBonusRanks, t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill, ScriptedMage.CreationBonusXp);
        }

        return fighter;
    }

    /// <summary>A built fighter: level 40, the plan spent, the kit held, the Mace wielded, the leather worn, standing in Holtburg.</summary>
    public static ScriptedFighter Ready(XpTableData table, ScriptedAceSession server, string name)
    {
        var fighter = Fresh(table, server, name);
        fighter.Total = (long)table.XpForLevel(FighterTemplate.TargetLevel);
        fighter.Level = FighterTemplate.TargetLevel;
        fighter.Available = 1_000;
        fighter.Credits = 30;
        foreach (var t in FighterTemplate.Targets)
        {
            var experience = t.XpList(table)[t.Ranks];
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

        foreach (var line in FighterKit.Items)
        {
            for (var i = 0; i < (line.Item.MaxStack is null ? line.Item.Stack : 1); i++)
            {
                fighter._items.Add(new PackItem(fighter._nextGuid++, line.Item, line.Item.MaxStack is null ? 1 : line.Item.Stack, line.Wield ? (uint)line.Item.ValidLocations : 0));
            }
        }

        fighter.InHoltburg = true;
        return fighter;
    }

    public void Untrain(uint skill) => _skills[skill] = new SkillState(0, HeadlessCharacter.UntrainedSkill, 0);

    public void DropItems(uint wcid) => _items.RemoveAll(i => i.Kind.Wcid == wcid);

    /// <summary>Wield guids the model refused as ACE's <c>CheckWeaponCollision</c> does: a melee weapon while a launcher is wielded, a launcher while a melee weapon is (M4 3.1 fix2; the live round's WeenieError 0).</summary>
    public List<uint> CollisionRefusals { get; } = [];

    /// <summary>A second item of a kit class in the pack, as the hunt's loot adds one (M4 3.1 fix2); the guid.</summary>
    public uint AddCopy(uint wcid)
    {
        var kind = FighterKit.ItemOf(wcid)!.Item;
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
        var player = _server.Characters.First(c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase)).Guid;
        foreach (var item in _items)
        {
            Send(AceWireWriters.ObjectCreate(Created(item, player)));
        }

        _itemsAnnounced = _items.Count;
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
            Attributes = [.. Enumerable.Range(0, 6).Select(i => (_attributeRanks[i], FighterTemplate.CreationValue((uint)i + 1), _attributeExperience[i]))],
            Vitals = [.. Enumerable.Range(0, 3).Select(i => (_vitalRanks[i], 0u, _vitalExperience[i], 10u))],
            Skills = [.. _skills.Select(s => new AceSkill(s.Key, s.Value.Ranks, s.Value.Class, s.Value.Experience, 0))],
            Inventory = [.. _items.Where(i => i.Location == 0).Select(i => (i.Guid, 0u))],
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
        Container = item.Location == 0 ? owner : null,
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
                Send(AceWireWriters.PrivateUpdateAttribute(++_sequence, id, _attributeRanks[a], FighterTemplate.CreationValue(id), _attributeExperience[a]));
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

    private string Item(ItemRequest request)
    {
        ItemWcidsAsked.Add(request.Wcid);
        var line = FighterKit.ItemOf(request.Wcid);
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
        Send(Lifestone.Tests.Shared.Net.AceM3Writers.PutObjInContainer(Player, ++_eventSequence, guid, Player, (uint)_items.Count - 1, 0));
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
            else if (type == GameActionTypes.PutItemInContainer)
            {
                _items[index] = _items[index] with { Location = 0 };
                Send(AceM3Writers.PutObjInContainer(Player, ++_eventSequence, first, second, third, 0));
            }
        }
    }
}

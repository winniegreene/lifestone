using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.World.Data;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A model of the server's side of the mage build for <see cref="ScriptedAceSession"/> (M3 1.1 s3b): one character's experience, level, skill credits,
/// attributes, vitals, skills and spellbook, and ACE's answers to the mod's <c>xp</c> and <c>spell learn</c> and to the four stat actions (the private
/// updates in ACE's order; an invalid raise or train is dropped silently, as ACE drops it). It reads the template from <see cref="MageTemplate"/> and
/// the spells from <see cref="ArenaProfile"/>, so it follows them when they change. A model of what the scenario expects of ACE, not of ACE.
/// </summary>
internal sealed class ScriptedMage
{
    private const uint TotalExperienceKey = 1;
    private const uint AvailableExperienceKey = 2;
    private const uint CreditsKey = 24;
    private const uint LevelKey = 25;

    /// <summary>The ranks and experience ACE gives a skill trained at creation.</summary>
    internal const ushort CreationBonusRanks = 5;
    internal const uint CreationBonusXp = 526;

    /// <summary>The current value every vital had in the description before M4 fresh F1 (the builds never read it); it stays until a level-up sets the vitals to their maxima.</summary>
    internal const uint DefaultVital = 10;

    /// <summary>A current value at or above any maximum the template reaches, for <see cref="FullVitals"/>.</summary>
    internal const uint FullVital = 1_000_000;

    private readonly XpTableData _table;
    private readonly ScriptedAceSession _server;
    private readonly uint[] _attributeRanks = new uint[6];
    private readonly uint[] _attributeExperience = new uint[6];
    private readonly uint[] _vitalRanks = new uint[3];
    private readonly uint[] _vitalExperience = new uint[3];
    private readonly uint[] _vitalCurrent = [DefaultVital, DefaultVital, DefaultVital];
    private readonly SortedDictionary<uint, SkillState> _skills = [];
    private readonly TeleportDestination _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
    private readonly MageBuild _build;
    private byte _sequence;
    private uint _oldGuid;
    private bool _teleportDue;
    private ushort _teleportStamp = 5;
    private ushort _positionStamp = 30;

    private ScriptedMage(XpTableData table, ScriptedAceSession server, string name, MageBuild build = MageBuild.Gate126)
    {
        _build = build;
        _table = table;
        _server = server;
        Name = name;
        Known = [];
        SpellTable = [.. ArenaProfile.SpellsToLearn];
    }

    private sealed record SkillState(ushort Ranks, uint Class, uint Experience);

    /// <summary>
    /// A FIXED copy of the Aluvian SkillTable trained costs that ACE's <c>HandleActionTrainSkill</c> (Player_Skills.cs:129) insists the credits sent equal.
    /// Deliberately not <see cref="MageTemplate.TrainCredits"/>: a typo there must fail the offline tests, not slip to the live build.
    /// </summary>
    internal static readonly IReadOnlyDictionary<uint, int> AceTrainedCost = new Dictionary<uint, int>
    {
        [6] = 10, [15] = 12, [16] = 6, [21] = 6, [22] = 4, [24] = 4, [31] = 8, [32] = 8, [33] = 12, [34] = 16,
    };

    public string Name { get; }

    public long Total { get; set; }

    public long Available { get; set; }

    public int Level { get; set; } = 1;

    public int Credits { get; set; }

    public HashSet<uint> Known { get; }

    /// <summary>The ids the spell table holds (remove one to make it unknown to the mod).</summary>
    public HashSet<uint> SpellTable { get; }

    /// <summary>Stat actions to drop silently, as ACE drops a refused one (kind and id).</summary>
    public HashSet<(MageStatKind Kind, uint Id)> Dropped { get; } = [];

    /// <summary>The experience amounts asked of the <c>xp</c> verb (a refused grant is listed too).</summary>
    public List<long> XpAsked { get; } = [];

    /// <summary>How many <c>xp</c> grants the model answers before it refuses every later one (M4 fresh F1: 1 lets the build's grant through and refuses the refill's). Never, by default.</summary>
    public int XpGrantsBeforeRefusal { get; set; } = int.MaxValue;

    /// <summary>True plays a server whose level-ups do not refill the vitals (ACE's <c>SetMaxVitals</c> not run), so the refill stage's grant levels the character up and the vitals stay as they were (M4 fresh F1).</summary>
    public bool SkipRefill { get; set; }

    /// <summary>True plays a character whose vitals are already at a value at or above any maximum, whatever the raises do (M4 fresh F1: the refill stage finds nothing to grant).</summary>
    public bool FullVitals { get; set; }

    /// <summary>The number of ids in each <c>spell learn</c> request.</summary>
    public List<int> LearnSizes { get; } = [];

    /// <summary>The synthetic XpTable; <paramref name="levels"/> is the level cap (the real table's 275 by default; M4 fresh F1: a table that ends at the template's level names no level for the refill's level-up).</summary>
    public static XpTableData SyntheticTable(int levels = 275)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(XpTableData.DataId);
            int[] counts = [190, 196, 208, 226, levels];
            foreach (var c in counts)
            {
                w.Write((uint)c);
            }

            uint[] steps = [110, 73, 58, 23];
            for (var list = 0; list < 4; list++)
            {
                for (var i = 0; i <= counts[list]; i++)
                {
                    w.Write((uint)(i * steps[list]));
                }
            }

            for (var level = 0; level <= counts[4]; level++)
            {
                w.Write(level == 0 ? 0UL : (ulong)(level - 1) * 10_000_000UL);
            }

            for (var level = 0; level <= counts[4]; level++)
            {
                w.Write(level >= 2 ? 1u : 0u);
            }
        }

        return XpTableData.Decode(stream.ToArray());
    }

    /// <summary>A character as the creation leaves it: the template's start values, its specialised skills specialised, the later-trained ones untrained, no experience, no spells.</summary>
    public static ScriptedMage Fresh(XpTableData table, ScriptedAceSession server, string name, MageBuild build = MageBuild.Gate126)
    {
        var mage = new ScriptedMage(table, server, name, build);
        mage.InitSkills();
        return mage;
    }

    private void InitSkills()
    {
        foreach (var skill in SkillIds())
        {
            // M5 7 F6: the creation of the build: the gate's War Magic and Melee Defense, or the traditional mage's War Magic and Life Magic specialised and no Melee Defense at all.
            var cls = MageTemplate.TrainedLaterFor(_build).Contains(skill) ? HeadlessCharacter.UntrainedSkill
                : MageTemplate.SpecializedAtCreationFor(_build).Contains(skill) ? HeadlessCharacter.SpecializedSkill
                : skill == MageTemplate.MeleeDefense ? HeadlessCharacter.UntrainedSkill : HeadlessCharacter.TrainedSkill;
            // ACE's PlayerFactory gives a creation-trained skill 5 ranks and 526 XP (TrainSkill with applyCreationBonusXP, Player_Skills.cs:182-186).
            _skills[skill] = MageTemplate.CreationTrained.Contains(skill) ? new SkillState(CreationBonusRanks, cls, CreationBonusXp) : new SkillState(0, cls, 0);
        }
    }

    /// <summary>
    /// A character that meets the template (<see cref="Ready"/>) and that the retail delete replaces (M4 fresh B1): when the server lists a new live character of its name, the model starts over as a fresh character (the template's creation, no experience, no spells, outside Holtburg).
    /// </summary>
    public static ScriptedMage Replaceable(XpTableData table, ScriptedAceSession server, string name)
    {
        var mage = Ready(table, server, name);
        mage._oldGuid = server.Characters.First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) && c.DeleteField == 0).Guid;
        return mage;
    }

    private void BecomeFresh()
    {
        Array.Clear(_attributeRanks);
        Array.Clear(_attributeExperience);
        Array.Clear(_vitalRanks);
        Array.Clear(_vitalExperience);
        Array.Fill(_vitalCurrent, DefaultVital);
        _skills.Clear();
        Known.Clear();
        Total = 0;
        Available = 0;
        Level = 1;
        Credits = 0;
        _oldGuid = 0;
        InitSkills();
    }

    /// <summary>A character that meets the template exactly (level of the table's level 126, or <paramref name="plan"/>'s level at its targets, M5 7 F3; every stat at its target, every skill of the plan at its class, every spell known).</summary>
    public static ScriptedMage Ready(XpTableData table, ScriptedAceSession server, string name, MageLevelPlan? plan = null)
    {
        var mage = Fresh(table, server, name, plan?.Build ?? MageBuild.Gate126);
        mage.Total = (long)table.XpForLevel(plan?.Level ?? MageTemplate.TargetLevel);
        mage.Level = plan?.Level ?? MageTemplate.TargetLevel;
        mage.Available = 1_000;
        foreach (var t in plan?.Targets ?? MageTemplate.Targets)
        {
            var experience = t.XpList(table)[t.Ranks];
            switch (t.Kind)
            {
                case MageStatKind.Attribute:
                    mage._attributeRanks[t.Id - 1] = (uint)t.Ranks;
                    mage._attributeExperience[t.Id - 1] = experience;
                    break;
                case MageStatKind.Vital:
                    mage._vitalRanks[VitalIndex(t.Id)] = (uint)t.Ranks;
                    mage._vitalExperience[VitalIndex(t.Id)] = experience;
                    break;
                default:
                    mage._skills[t.Id] = new SkillState((ushort)t.Ranks, t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill, experience);
                    break;
            }
        }

        foreach (var id in ArenaProfile.SpellsToLearn)
        {
            mage.Known.Add(id);
        }

        return mage;
    }

    /// <summary>Puts a skill back to untrained with no ranks (a half-built mage).</summary>
    public void Untrain(uint skill) => _skills[skill] = new SkillState(0, HeadlessCharacter.UntrainedSkill, 0);

    /// <summary>Sets a skill's advancement class, keeping its ranks.</summary>
    public void SetClass(uint skill, uint advancementClass) => _skills[skill] = _skills[skill] with { Class = advancementClass };

    public (uint Ranks, uint Experience) AttributeOf(uint id) => (_attributeRanks[id - 1], _attributeExperience[id - 1]);

    public (ushort Ranks, uint Class, uint Experience) SkillOf(uint id) => (_skills[id].Ranks, _skills[id].Class, _skills[id].Experience);

    /// <summary>Hooks the model into the server: the description of this character, the stat actions and the mod verbs.</summary>
    public void Attach()
    {
        var fallback = _server.Description;
        _server.Description = name => string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) ? Describe() : fallback(name);
        _server.StatHook = OnStat;
        _server.ToolHook = OnTool;
    }

    private static IEnumerable<uint> SkillIds() =>
        MageTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill).Select(t => t.Id).Concat(MageTemplate.TrainedLater).Distinct();

    private static int VitalIndex(uint id) => id switch
    {
        MageTemplate.MaxHealth => 0,
        MageTemplate.MaxStamina => 1,
        MageTemplate.MaxMana => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "not a vital's maximum id"),
    };

    private AcePlayerDescription Describe()
    {
        if (_oldGuid != 0 && _server.Characters.Any(c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase) && c.DeleteField == 0 && c.Guid != _oldGuid))
        {
            BecomeFresh(); // the old character was deleted and a new one of the name created
        }

        var baseline = AcePlayerDescription.NewCharacter(Name);
        return baseline with
        {
            Ints = [.. baseline.Ints.Where(p => p.Key is not (CreditsKey or LevelKey)), (CreditsKey, Credits), (LevelKey, Level)],
            Int64s = [(TotalExperienceKey, Total), (AvailableExperienceKey, Available)],
            Attributes = [.. Enumerable.Range(0, 6).Select(i => (_attributeRanks[i], MageTemplate.CreationValue((uint)i + 1), _attributeExperience[i]))],
            Vitals = [.. Enumerable.Range(0, 3).Select(i => (_vitalRanks[i], 0u, _vitalExperience[i], CurrentOf(i)))],
            Skills = [.. _skills.Select(s => new AceSkill(s.Key, s.Value.Ranks, s.Value.Class, s.Value.Experience, 0))],
            Spells = [.. Known.Select(id => (int)id)],
        };
    }

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

    private uint CurrentOf(int vital) => FullVitals ? FullVital : _vitalCurrent[vital];

    /// <summary>
    /// A vital's maximum as the client computes it from the description this model sends (<see cref="PlayerQualities.TryGetVitalLevels"/>: start 0 plus the ranks plus the formula over the attributes' creation values and ranks, no enchantments, no gear), so the model can
    /// set a current value to the very maximum the client will compare it with: health half of Endurance rounded up, stamina Endurance, mana Self.
    /// </summary>
    private uint VitalMax(int vital)
    {
        uint Attribute(uint id) => MageTemplate.CreationValue(id) + _attributeRanks[id - 1];
        var bonus = vital switch
        {
            0 => (Attribute(PlayerQualities.EnduranceAttributeId) + 1) / 2,
            1 => Attribute(PlayerQualities.EnduranceAttributeId),
            _ => Attribute(PlayerQualities.SelfAttributeId),
        };
        return _vitalRanks[vital] + bonus;
    }

    /// <summary>
    /// ACE's <c>Player.SetMaxVitals</c> (Player_Vitals.cs 112-124, run by Player_Xp.cs <c>CheckForLevelup</c> after the level update): each vital's current value becomes its maximum AT THAT MOMENT and the three 0x02E9 updates (health 2, stamina 4, mana 6) are sent. Raises
    /// that come after lift the maximum and leave the current value (<c>HandleActionRaiseAttribute</c>, <c>HandleActionRaiseVital</c>), which is what the fresh build leaves and the refill stage mends (M4 fresh F1). <see cref="SkipRefill"/> plays a server that does not run it.
    /// </summary>
    private void SetMaxVitals()
    {
        if (SkipRefill)
        {
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            _vitalCurrent[i] = VitalMax(i);
            Send(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_sequence, (uint)(2 * i + 2), CurrentOf(i)));
        }
    }

    /// <summary>ACE's told refusal of a raise past the list's end (Player_Attributes.cs:86-91, Player_Skills.cs:87-92).</summary>
    private void Refuse(string stat) => Send(AceWireWriters.ServerMessage($"Your attempt to raise {stat} has failed.", 0));

    private static string NameOf(MageStatKind kind, uint id) => MageTemplate.TargetOf(kind, id)?.Name ?? $"{kind} {id}";

    private void OnStat(uint type, uint id, uint amount)
    {
        switch (type)
        {
            case GameActionTypes.RaiseAttribute when id is >= 1 and <= 6:
                if (Dropped.Contains((MageStatKind.Attribute, id)) || amount == 0 || amount > Available)
                {
                    return;
                }

                var a = (int)id - 1;
                if (amount > _table.AttributeXpList[^1] - _attributeExperience[a])
                {
                    Refuse(NameOf(MageStatKind.Attribute, id));
                    return;
                }

                _attributeExperience[a] += amount;
                _attributeRanks[a] = (uint)RankFor(_table.AttributeXpList, _attributeExperience[a]);
                Available -= amount;
                Send(AceWireWriters.PrivateUpdatePropertyInt64(++_sequence, AvailableExperienceKey, Available));
                Send(AceWireWriters.PrivateUpdateAttribute(++_sequence, id, _attributeRanks[a], MageTemplate.CreationValue(id), _attributeExperience[a]));
                break;
            case GameActionTypes.RaiseVital when id is MageTemplate.MaxHealth or MageTemplate.MaxStamina or MageTemplate.MaxMana:
                if (Dropped.Contains((MageStatKind.Vital, id)) || amount == 0 || amount > Available)
                {
                    return;
                }

                var v = VitalIndex(id);
                if (amount > _table.VitalXpList[^1] - _vitalExperience[v])
                {
                    Refuse(NameOf(MageStatKind.Vital, id));
                    return;
                }

                _vitalExperience[v] += amount;
                _vitalRanks[v] = (uint)RankFor(_table.VitalXpList, _vitalExperience[v]);
                Available -= amount;
                Send(AceWireWriters.PrivateUpdatePropertyInt64(++_sequence, AvailableExperienceKey, Available));
                Send(AceWireWriters.PrivateUpdateVital(++_sequence, id, _vitalRanks[v], 0, _vitalExperience[v], CurrentOf(v))); // ACE's raise leaves the current value as it was (Player_Vitals.cs HandleActionRaiseVital sends the update only)
                break;
            case GameActionTypes.RaiseSkill when _skills.TryGetValue(id, out var s):
                if (Dropped.Contains((MageStatKind.Skill, id)) || amount == 0 || amount > Available || s.Class < HeadlessCharacter.TrainedSkill)
                {
                    return;
                }

                var list = s.Class == HeadlessCharacter.SpecializedSkill ? _table.SpecializedSkillXpList : _table.TrainedSkillXpList;
                if (amount > list[^1] - s.Experience)
                {
                    Refuse(NameOf(MageStatKind.Skill, id));
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
                if (XpAsked.Count > XpGrantsBeforeRefusal)
                {
                    var refusal = new ToolError(ToolErrors.Failed, "the experience grant was refused");
                    return ToolsWire.Reply(ToolReply.Failure(request.Id, ToolVerbs.Xp, refusal), refusal.Message);
                }

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
                if (Level > oldLevel)
                {
                    SetMaxVitals();
                }

                var data = new XpData(xp.Amount, xp.Amount, Total, Available, Level);
                return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Xp, data), data.Describe());
            case SpellLearnRequest learn:
                LearnSizes.Add(learn.SpellIds.Count);
                var outcomes = new List<SpellOutcome>();
                foreach (var id in learn.SpellIds)
                {
                    var inTable = SpellTable.Contains(id);
                    var known = Known.Contains(id);
                    outcomes.Add(new SpellOutcome(id, SpellOutcomes.Classify(inTable, known), inTable ? ArenaProfile.Spell(id)?.Name ?? "spell " + id : null));
                    if (inTable)
                    {
                        Known.Add(id);
                    }
                }

                var spells = SpellLearnData.Of(outcomes);
                return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Spell, spells), spells.Describe());
            case PlacePoiRequest poi when poi.Name.Contains("Holtburg", StringComparison.OrdinalIgnoreCase):
                var at = _holtburg.Location.Position;
                var placed = new PlaceData("poi", poi.Name, ToolsWire.FormatCell(at.CellId), at.Local.X, at.Local.Y, at.Local.Z, 1f, 0f, 0f, 0f);
                _teleportDue = true;
                PlacedInHoltburg++;
                return ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.PlacePoi, placed), placed.Describe());
            default:
                return null;
        }
    }

    /// <summary>The <c>place poi</c> requests for Holtburg the model answered (M4 fresh B1: a fresh character is placed there before its log off).</summary>
    public int PlacedInHoltburg { get; private set; }

    /// <summary>Answers what the session sent that no hook answers: the teleport after the place's reply (M4 fresh B1). Call once per step of the run; it does nothing in a run that places nobody.</summary>
    public void Pump()
    {
        if (!_teleportDue || _server.PlayerGuid is not { } player)
        {
            return;
        }

        _teleportDue = false;
        var at = _holtburg.Location.Position;
        Send(AceWireWriters.PlayerTeleport(++_teleportStamp));
        Send(AceWireWriters.UpdatePosition(player, new AcePosition(at.CellId, at.Local.X, at.Local.Y, at.Local.Z), true, new AceUpdatePositionSequences(1, ++_positionStamp, _teleportStamp, 1)));
        Send(AceWireWriters.SetState(player, (uint)(PhysicsState.Gravity | PhysicsState.EdgeSlide), 1, (ushort)(40 + _teleportStamp)));
    }
}

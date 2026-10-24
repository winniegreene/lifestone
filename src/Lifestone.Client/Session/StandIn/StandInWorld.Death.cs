// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Death.cs (OnDeath, InflictVitaePenalty, Die, ThreadSafeTeleportOnDeath, HandleActionDie, HandleSuicide, CalculateDeathItems, GetNumItemsDropped, DropMessage,
// SetLifestoneProtection, HandleLifestoneProtection, LifestoneProtectionTick), Creature_Death.cs (CreateCorpse),
// WorldObjects/Lifestone.cs (ActOnUse), WorldObjects/Managers/EnchantmentManager.cs (UpdateVitae, GetMinVitae),
// Player_Character.cs and ACE.Database CharacterExtensions.cs (AddSpellToBar) (AGPL-3.0) [with changes: the stand-in holds no monster (M4 4.1 t2, D23, D26), so only a suicide kills the player; the stand-in
// has no weenie database, so the corpse, the lifestone and the lost items are named placeholders; the stand-in's own pack is the only pack that can lose items].
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The stand-in's death (M3 3.2 db; M4 4.1 t2: the tusker that attacked is retired): the player's death in ACE's order, the corpse, the teleport to the lifestone and the protection there, the suicide, the lifestone's Use and the spell bars. Every number ACE keeps in
/// its world database or its physics, which the stand-in has not, is a named, logged placeholder.
/// </summary>
/// <remarks>
/// <para><b>Die</b> (<c>Player_Death.cs</c> 150-242): the health to 0 (0x02E9 when it changed), NumDeaths up, the Dead motion F74C, 0x02E9 health 0, 0x02CD NumDeaths, for a suicide the WeenieError YouKilledYourself, DeathLevel and VitaeCpPool (0x02CD),
/// the vitae 0x02C2 (spell 666, 0.95 and 5% lower per death, never below the floor of the level), the purge 0x02C6; then the Dead animation plus 1 s later (<see cref="PlayerDeathAnimationSeconds"/> is the Dead animation's placeholder length) the
/// lost items, the corpse and the teleport; on arrival the stance NonCombat, the protection (60 s), and 3 s later the three vitals at 75% of their maximum.</para>
/// </remarks>
internal sealed partial class StandInWorld
{
    /// <summary>The seed of the death's rolls (the sentence, the lost items).</summary>
    internal const int PlayerDeathSeed = 0x5EED_D1ED;

    // Low word of ACE's MotionCommand.Sanctuary (MotionCommand.cs 94).
    private const uint SanctuaryMotion = 0x10000057;

    /// <summary>ACE's <c>WeenieError.YoureTooBusy</c> (0x001D), the answer to a suicide while dead or teleporting.</summary>
    internal const uint YoureTooBusy = 0x001D;

    /// <summary>ACE's <c>WeenieError.YouKilledYourself</c> (0x004A).</summary>
    internal const uint YouKilledYourself = 0x004A;

    /// <summary>ACE's <c>WeenieError.YouHaveMovedTooFar</c> (0x0498): a Use of a lifestone from too far away.</summary>
    internal const uint YouHaveMovedTooFar = 0x0498;

    /// <summary>The seconds between a suicide's lines and before its death (<c>HandleSuicide</c>: five lines, each followed by 3 s).</summary>
    internal const double SuicideStepSeconds = 3.0;

    /// <summary>ACE's <c>SuicideMessages</c>.</summary>
    internal static readonly string[] SuicideLines = ["I feel faint...", "My sight is growing dim...", "My life is flashing before my eyes...", "I see a light...", "Oh cruel, cruel world!"];

    /// <summary>The Dead animation's length in seconds (the stand-in does not read the motion table: <c>TODO(m4-standin-death-animation)</c>); ACE waits this plus 1 s before the corpse and the teleport.</summary>
    internal const double PlayerDeathAnimationSeconds = 2.0;

    /// <summary>The seconds after the arrival at which the vitals return to 75% (<c>ThreadSafeTeleportOnDeath</c>).</summary>
    internal const double RevivalSeconds = 3.0;

    /// <summary>The vitals' share after a death.</summary>
    internal const float RevivalShare = 0.75f;

    /// <summary>The lifestone protection's length (<c>LifestoneProtectionTime</c>, one minute).</summary>
    internal const double ProtectionSeconds = 60.0;

    /// <summary>The vitae's penalty per death and the deepest penalty ACE allows (<c>vitae_penalty</c> 0.05 and <c>vitae_penalty_max</c> 0.40: the floor of 0.60).</summary>
    internal const float VitaePenalty = 0.05f;

    /// <summary>See <see cref="VitaePenalty"/>.</summary>
    internal const float VitaePenaltyMax = 0.40f;

    /// <summary>The farthest from a lifestone a Use works (placeholder: ACE walks the player to the lifestone's use radius first).</summary>
    internal const float LifestoneUseRadiusMetres = 10f;

    /// <summary>The Magic line a lifestone Use shows: a labelled placeholder, the weenie's own UseMessage being the world database's (<c>TODO(m3-standin-lifestone-weenie)</c>).</summary>
    internal const string PlaceholderUseMessage = "You have bound yourself to the stand-in lifestone (a placeholder: the weenie's own text is the world database's).";

    /// <summary>How long the Sanctuary motion plays before the bind (placeholder: the motion table's length).</summary>
    internal const double SanctuarySeconds = 3.0;

    /// <summary>ACE's <c>PropertyInt.NumDeaths</c> (43).</summary>
    internal const uint PropertyNumDeaths = 43;

    private const uint PropertyDeathLevel = 139;
    private const uint PropertyVitaeCpPool = 129;
    private const uint SoundLifestoneOn = 0x51;
    private const uint SoundUnwieldObject = 0x8D;
    private const uint CorpseWeenieClass = 21; // TODO(m3-standin-corpse-weenie): the corpse weenie's class id is the world database's
    private const uint CorpseItemType = 0x200; // Container
    private const uint FirstCorpseGuid = 0x80030000;
    private const int LevelDropsWielded = 35;

    private readonly Random _playerDeathRandom = new(PlayerDeathSeed);
    private readonly Dictionary<uint, StandInObject> _placed = [];
    private uint _nextCorpseGuid = FirstCorpseGuid;
    private bool _dying;
    private bool _suicideInProgress;
    private bool _protected;
    private int _protectionSerial;
    private (uint Cell, Vector3 Origin)? _sanctuary;
    private List<List<uint>>? _bars;

    /// <summary>Deaths of the player the stand-in has played (a suicide counts).</summary>
    internal int PlayerDeaths { get; private set; }

    /// <summary>The corpses of the player the stand-in has created.</summary>
    internal int Corpses { get; private set; }

    /// <summary>True from the player's death until the vitals come back (ACE's <c>IsInDeathProcess</c> and a dead player).</summary>
    internal bool Dying => _dying;

    /// <summary>True while the lifestone protection holds.</summary>
    internal bool UnderProtection => _protected;

    /// <summary>Where the player's death teleport goes, once a lifestone bound it (null before: the visit's start).</summary>
    internal (uint Cell, Vector3 Origin)? Sanctuary => _sanctuary;

    /// <summary>The items that left the stand-in's pack for the last corpse, by name and count (the tests read it).</summary>
    internal List<(string Name, int Count)> LastDrops { get; } = [];

    private void PlaySound(IAcSessionSink sink, uint guid, uint sound, float volume)
    {
        AnswerLog.Add("F750");
        sink.OnSound(Context(sink), new SoundMsg(guid, sound, volume));
    }

    /// <summary>Takes a placed object out of the world (a check's clean-up) and tells the client with a DeleteObject.</summary>
    internal void RemoveObject(uint guid)
    {
        _placed.Remove(guid);
        Later(sink => DeleteObject(sink, guid));
    }

    // --- the player's death -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ACE's <c>Die</c> for the player (<c>Player_Death.cs</c> 150-242): the Dead motion and the updates, the vitae and the purge, then the corpse and the teleport after the Dead animation plus a second.
    /// </summary>
    private void Die(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        _dying = true;
        _suicideInProgress = false;
        PlayerDeaths++;
        if (self.Qualities.TryGetVitalLevels(1, out var current, out _) && current > 0)
        {
            UpdateVitalDelta(sink, Health, -(int)current); // UpdateVital(Health, 0): sent only when it changed
        }

        var deaths = (self.Qualities.TryGetInt(PropertyNumDeaths, out var before) ? before : 0) + 1;
        SendPlayerMotion(sink, MotionCommands.NonCombat, MotionCommands.Dead);
        AnswerLog.Add("02E9");
        PrivateUpdate(sink, PrivateUpdateKind.VitalCurrent, Health, 0);
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PropertyNumDeaths, deaths);
        AnswerLog.Add("028A");
        sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(YouKilledYourself, null)); // the stand-in holds no monster (M4 4.1 t2): the only death is a suicide

        InflictVitaePenalty(sink);
        AnswerLog.Add("02C6");
        sink.OnMagicPurgeEnchantments(Context(sink), new GameEventHeader(self.Guid, 0, 0x02C6), default); // AugmentationSpellsRemainPastDeath == 0: every enchantment goes (the vitae is not in the lists)
        Placeholder("m4-standin-death-animation", "ACE waits the Dead animation's length (the motion table's) plus a second before the corpse and the teleport: the stand-in waits " + PlayerDeathAnimationSeconds + " s plus 1");
        _reducer.StandIn!.At(_reducer.Clock.Now + PlayerDeathAnimationSeconds + 1.0, CorpseAndTeleport);
    }

    /// <summary><c>InflictVitaePenalty</c> (<c>Player_Death.cs</c> 117-132) with <c>EnchantmentManager.UpdateVitae</c> (413-448).</summary>
    private void InflictVitaePenalty(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        var level = self.Qualities.Level;
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PropertyDeathLevel, level);
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PropertyVitaeCpPool, 0);
        var vitae = self.Qualities.TryGetVitae(out var old) ? old.Enchantment.StatModValue - VitaePenalty : 1f - VitaePenalty;
        vitae = Math.Clamp(vitae, MinimumVitae(level), 1f);
        Placeholder("m3-standin-vitae-spell", "the vitae spell's stat-mod type and key are ACE's world database's, not the dat's: the stand-in sends the vitae bit and the multiplicative bit, and its penalties are ACE's defaults (5% a death, no deeper than 40%)");
        var info = FindSpell(EnchantmentRules.VitaeSpellId);
        var enchantment = new EnchantmentDesc(EnchantmentRules.VitaeSpellId, 0, (ushort)(info?.Category ?? EnchantmentRules.VitaeSpellId), 1, info?.Power ?? 0u, 0.0, -1.0, self.Guid, 0f, 0f, 0.0, EnchantmentRules.VitaeKind | EnchantmentRules.Multiplicative, 0, vitae, 0, EnchantmentBucket.Vitae);
        AnswerLog.Add("02C2");
        sink.OnMagicUpdateEnchantment(Context(sink), new GameEventHeader(self.Guid, 0, 0x02C2), new MagicUpdateEnchantmentEvent(enchantment));
    }

    /// <summary><c>GetMinVitae</c> (<c>EnchantmentManager.cs</c> 391-408): the deepest penalty is 3% a level above the first, between 1% and the global maximum.</summary>
    internal static float MinimumVitae(int level)
    {
        var maxPenalty = Math.Clamp((level - 1) * 3, 1, (int)Math.Round(VitaePenaltyMax * 100));
        return (100 - maxPenalty) / 100f;
    }

    private void SendPlayerMotion(IAcSessionSink sink, uint stance, uint forward)
    {
        var guid = _reducer.State.Self.Guid;
        var stamps = _reducer.State.Objects.TryGet(guid, out var body) ? body.Stamps : default;
        var wire = MotionCommandTable.ToWire(stance);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Forward = forward == MotionCommands.Ready ? null : MotionCommandTable.ToWire(forward),
        };
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    private void CorpseAndTeleport(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        var level = Math.Max(self.Qualities.Level, 1);
        var here = self.Position;
        var wornAtDeath = _reducer.State.Objects.TryGet(self.Guid, out var dying) ? dying.Model : ModelData.Empty; // the corpse wears what the player wore before the drops (Creature_Death.cs 474-480)
        var dropped = LoseItems(sink, level);
        if (here is { } at && AcCoordinates.IsOutdoorCell(at.CellId))
        {
            if (dropped > 0 && AcCoordinates.TryGetMapCoordinates(at.ToGlobal(), out var coordinates))
            {
                Line(sink, "Your corpse is located at (" + AcCoordinates.FormatMapCoordinates(coordinates) + ").");
            }
        }

        if (dropped == 0)
        {
            Line(sink, "You have retained all your items. You do not need to recover your corpse!");
        }

        if (here is { } place)
        {
            CreateCorpse(sink, new AcPosition(place.CellId, place.Origin, place.Orientation), wornAtDeath);
        }

        var jump = _sanctuary is { } bound ? new StandInTeleport(0, bound.Cell, bound.Origin) : new StandInTeleport(0, _visit.Cell, _visit.Origin);
        if (_sanctuary is null)
        {
            Placeholder("m3-standin-instantiation", "a player who never used a lifestone returns to ACE's Instantiation position (where the character was created): the stand-in's is the visit's start");
        }

        Teleport(sink, jump);
        ArriveAfterDeath(sink);
    }

    /// <summary>
    /// ACE's <c>CalculateDeathItems</c> (<c>Player_Death.cs</c> 347-540, <c>GetNumItemsDropped</c> 618-651) over the items the stand-in keeps: none at level 10 or below; one at most from 11 to 20; from 21 level over 20 plus a roll of 0 to 2 (14 at
    /// most); wielded items only from level 35. The order is a seeded shuffle (the stand-in has no values to rank by and no bonding: <c>TODO(m3-standin-death-items)</c>), a stack loses one unit, and the burden update and the pyreals are not sent
    /// (<c>TODO(m3-standin-death-items)</c>). Returns how many items left.
    /// </summary>
    private int LoseItems(IAcSessionSink sink, int level)
    {
        LastDrops.Clear();
        var wanted = level <= 10 ? 0 : level <= 20 ? _playerDeathRandom.Next(0, 2) : Math.Min((level / 20) + _playerDeathRandom.Next(0, 3), 14);
        if (wanted == 0 || (_pack.Count == 0 && _wielded.Count == 0))
        {
            return 0;
        }

        Placeholder("m3-standin-death-items", "ACE ranks the pack by value, halving each repeated item type, skips bonded items and halves the pyreals; the stand-in knows no values or bonding, so it takes its own items (wielded ones from level " + LevelDropsWielded
            + ") in a seeded order, leaves the pyreals alone and sends no burden update");
        var candidates = new List<(uint Guid, bool Wielded)>();
        for (var i = 0; i < _pack.Count; i++)
        {
            candidates.Add((_pack[i].Guid, false));
        }

        if (level >= LevelDropsWielded)
        {
            foreach (var guid in _wielded.Keys)
            {
                candidates.Add((guid, true));
            }
        }

        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = _playerDeathRandom.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var taken = Math.Min(wanted, candidates.Count);
        for (var i = 0; i < taken; i++)
        {
            LastDrops.Add(DropItem(sink, candidates[i].Guid, candidates[i].Wielded));
        }

        if (taken > 0)
        {
            Line(sink, DropMessage());
        }

        return taken;
    }

    private (string Name, int Count) DropItem(IAcSessionSink sink, uint guid, bool wielded)
    {
        if (wielded)
        {
            var entry = _wielded[guid];
            _wielded.Remove(guid);
            Zeros(sink, guid); // TryDequipObjectWithNetworking: the Wielder and CurrentWieldedLocation zeros (Player_Inventory.cs 400-405)
            PlaySound(sink, _reducer.State.Self.Guid, SoundUnwieldObject, 1f);
            DeleteObject(sink, guid); // ToCorpseOnDeath: the item is deleted from the client (the corpse holds it)
            SendLook(sink, entry.Worn);
            return (entry.Name, 1);
        }

        var at = _pack.FindIndex(e => e.Guid == guid);
        var pack = _pack[at];
        if (pack.Stack > 1)
        {
            _pack[at] = pack with { Stack = pack.Stack - 1 };
            StackSize(sink, guid, pack.Stack - 1); // AdjustStack(-1) and GameMessageSetStackSize: one unit of a stack drops
            return (pack.Name, 1);
        }

        Instance(sink, guid, ContainerProperty, 0); // TryRemoveFromInventoryWithNetworking: the Container zero, then the removal (Player_Inventory.cs 217-226)
        AnswerLog.Add("0024");
        sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(guid));
        _pack.RemoveAt(at);
        return (pack.Name, pack.Stack);
    }

    /// <summary>ACE's <c>DropMessage</c> (<c>Player_Death.cs</c> 688-731) for the items the last corpse took (the plural is the name and an "s": <c>TODO(m3-standin-weenies)</c>).</summary>
    private string DropMessage()
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < LastDrops.Count; i++)
        {
            text.Append(i == 0 ? "You've lost " : i == LastDrops.Count - 1 ? ", and " : ", ");
            var (name, count) = LastDrops[i];
            text.Append("your ").Append(count == 1 ? name : count.ToString("N0", CultureInfo.InvariantCulture) + " " + name + "s");
        }

        return text.Append('!').ToString();
    }

    /// <summary>The player's corpse (<c>Creature_Death.cs</c> <c>CreateCorpse</c> 428-600): "Corpse of &lt;name&gt;" with the corpse bit, the player's Setup and motion table, in the Dead pose, where the player fell.</summary>
    private void CreateCorpse(IAcSessionSink sink, in AcPosition place, ModelData worn)
    {
        var self = _reducer.State.Self;
        Placeholder("m3-standin-corpse-weenie", "the corpse weenie's class id, look and physics are ACE's world database's; the stand-in's corpse wears the player's Setup, lies in the Dead pose and holds nothing the client can open");
        var guid = _nextCorpseGuid++;
        Corpses++;
        var dead = MotionCommandTable.ToWire(MotionCommands.NonCombat);
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.MTable | PhysicsDescFlags.Position | PhysicsDescFlags.Movement,
            State = PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
            Position = place,
            SetupId = StandInVisit.HumanSetup,
            MotionTableId = StandInVisit.HumanMotionTable,
            Movement = new ServerMotionState
            {
                MovementType = MovementType.Interpreted,
                Stance = dead,
                Style = dead,
                Forward = MotionCommandTable.ToWire(MotionCommands.Dead),
            },
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "Corpse of " + (self.Name ?? "You"),
            WeenieClassId = CorpseWeenieClass,
            IconId = 0x06000001,
            ItemType = CorpseItemType,
            ObjectDescriptionFlags = WorldObjectTable.CorpseFlag,
        };
        AnswerLog.Add("F745");
        // ACE's CreateCorpse saves the dying creature's CalculateObjDesc on the corpse (Creature_Death.cs 474-480) and Corpse.CalculateObjDesc sends it: the corpse wears what the player wore.
        sink.OnCreateObject(Context(sink), new CreateObjectMsg(guid, worn, physics, weenie), isUpdate: false);
    }

    /// <summary>
    /// <c>ThreadSafeTeleportOnDeath</c>'s callback (<c>Player_Death.cs</c> 247-292): the stance back to NonCombat (the motion and the combat mode), the lifestone protection, and 3 s later the three vitals at 75% of their maximum (health, stamina, mana
    /// as 0x02E9 each).
    /// </summary>
    private void ArriveAfterDeath(IAcSessionSink sink)
    {
        SendPlayerMotion(sink, MotionCommands.NonCombat, MotionCommands.Ready); // SetCombatMode(NonCombat): HandleSwitchToPeaceMode
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, CombatModes.NonCombat);
        SetProtection(sink);
        _reducer.StandIn!.At(_reducer.Clock.Now + RevivalSeconds, Revive);
    }

    private void Revive(IAcSessionSink sink)
    {
        var q = _reducer.State.Self.Qualities;
        SendVital(sink, Health, q, 1);
        SendVital(sink, Stamina, q, 3);
        SendVital(sink, Mana, q, 5);
        _dying = false; // IsInDeathProcess = false
    }

    private void SendVital(IAcSessionSink sink, uint vital, PlayerQualities q, uint maxId)
    {
        if (!q.TryGetVitalLevels(maxId, out _, out var max))
        {
            return;
        }

        AnswerLog.Add("02E9");
        PrivateUpdate(sink, PrivateUpdateKind.VitalCurrent, vital, (uint)Math.Round(max * RevivalShare, MidpointRounding.ToEven));
    }

    // --- the lifestone protection -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private void SetProtection(IAcSessionSink sink)
    {
        _protected = true;
        var serial = ++_protectionSerial;
        _reducer.StandIn!.At(_reducer.Clock.Now + ProtectionSeconds, later =>
        {
            if (_protected && serial == _protectionSerial)
            {
                _protected = false;
                MagicLine(later, "You're no longer protected by the Lifestone's magic!"); // LifestoneProtectionTick
            }
        });
    }

    /// <summary>A teleport dispels the protection (<c>Player_Location.cs</c> 702-703); called by <see cref="Teleport"/> before it sends.</summary>
    internal void DispelProtectionOnTeleport() => Later(DispelProtectionOnTeleport);

    private void DispelProtectionOnTeleport(IAcSessionSink sink)
    {
        if (_protected)
        {
            _protected = false;
            MagicLine(sink, "Your actions have dispelled the Lifestone's magic!"); // LifestoneProtectionDispel
        }
    }

    // --- the suicide ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A Suicide (0x0279): <c>HandleActionDie</c> and <c>HandleSuicide</c> (<c>Player_Death.cs</c> 299-345).</summary>
    internal OutboundTicket Suicide()
    {
        Later(HandleActionDie);
        return Ticket();
    }

    private void HandleActionDie(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        var dead = _dying || (self.Qualities.TryGetVitalLevels(1, out var health, out _) && health == 0);
        if (dead)
        {
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(YoureTooBusy, null));
            return;
        }

        if (_suicideInProgress)
        {
            return;
        }

        _suicideInProgress = true;
        var deaths = PlayerDeaths;
        HandleSuicide(sink, deaths, 0);
    }

    private void HandleSuicide(IAcSessionSink sink, int deaths, int step)
    {
        if (!_suicideInProgress || deaths != PlayerDeaths)
        {
            return;
        }

        if (step < SuicideLines.Length)
        {
            var self = _reducer.State.Self;
            AnswerLog.Add("02BB");
            sink.OnSpeech(Context(sink), new SpeechMsg(SuicideLines[step], self.Name ?? "You", self.Guid, 2, null));
            _reducer.StandIn!.At(_reducer.Clock.Now + SuicideStepSeconds, later => HandleSuicide(later, deaths, step + 1));
            return;
        }

        Die(sink);
    }

    // --- the lifestone ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private bool IsLifestone(uint guid)
    {
        if (_placed.TryGetValue(guid, out var placed))
        {
            return (placed.DescriptionFlags & WorldObjectTable.LifestoneFlag) != 0;
        }

        var objects = _visit.Objects;
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i].Guid == guid)
            {
                return (objects[i].DescriptionFlags & WorldObjectTable.LifestoneFlag) != 0;
            }
        }

        return false;
    }

    /// <summary>
    /// A Use of a lifestone (<c>Lifestone.cs</c> <c>ActOnUse</c> 45-75): a combat mode first goes back to NonCombat, the lifestone sound, the Sanctuary motion, then (the player still within reach) the sanctuary is the player's position, the object's
    /// UseMessage shows as a Magic line and the stamina halves; a player out of reach gets WeenieError YouHaveMovedTooFar. UseDone ends it.
    /// </summary>
    private void UseLifestone(IAcSessionSink sink, uint guid)
    {
        var self = _reducer.State.Self;
        Placeholder("m3-standin-lifestone-weenie", "the lifestone's UseMessage and the Sanctuary animation's length are ACE's world database's and motion table's: the stand-in shows a labelled placeholder line and waits " + SanctuarySeconds + " s");
        if (self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            SendPlayerMotion(sink, MotionCommands.NonCombat, MotionCommands.Ready);
            AnswerLog.Add("02CD");
            PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, CombatModes.NonCombat);
        }

        PlaySound(sink, self.Guid, SoundLifestoneOn, 1f);
        var wire = MotionCommandTable.ToWire(MotionCommands.NonCombat);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Commands = [new MotionItem(MotionCommandTable.ToWire(SanctuaryMotion), 1, false, 1f)],
        };
        var stamps = _reducer.State.Objects.TryGet(self.Guid, out var body) ? body.Stamps : default;
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(self.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
        _reducer.StandIn!.At(_reducer.Clock.Now + SanctuarySeconds, later => BindAtLifestone(later, guid));
    }

    private void BindAtLifestone(IAcSessionSink sink, uint guid)
    {
        var self = _reducer.State.Self;
        if (self.Position is { } here && _reducer.State.Objects.TryGet(guid, out var stone) && stone.Position is { } there && Distance(here.ToGlobal(), there.ToGlobal()) <= LifestoneUseRadiusMetres)
        {
            _sanctuary = (here.CellId, here.Origin);
            MagicLine(sink, PlaceholderUseMessage);
            if (self.Qualities.TryGetVitalLevels(3, out var stamina, out _))
            {
                UpdateVitalDelta(sink, Stamina, (int)Math.Round(stamina / 2f) - (int)stamina); // UpdateVital(Stamina, round(current / 2))
            }
        }
        else
        {
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(YouHaveMovedTooFar, null));
        }

        UseDone(sink);
    }

    private static double Distance(GlobalPosition a, GlobalPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    // --- the spell bars -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// An AddSpellFavorite (0x01E3): ACE adds the spell to the bar at the index (clamped to the bar's length; a spell already on the bar stays where it is) when the bar is 0 to 7 and the spell is known, and answers nothing
    /// (<c>Player_Character.cs</c> 298-305, <c>CharacterExtensions.AddSpellToBar</c> 533-573). The stand-in keeps the bars for its next description (<see cref="WithBars"/>).
    /// </summary>
    internal OutboundTicket AddSpellFavorite(uint spell, uint index, uint bar)
    {
        if (bar > 7 || !Knows(spell))
        {
            return Ticket();
        }

        var list = BarList(bar);
        if (!list.Contains(spell))
        {
            list.Insert((int)Math.Min(index, (uint)list.Count), spell);
        }

        return Ticket();
    }

    /// <summary>
    /// A RemoveSpellFavorite (0x01E4, M3 4.2 p1): ACE takes the spell off the bar when it is on it and answers nothing (<c>Player_Character.cs</c> <c>HandleActionRemoveSpellFavorite</c>,
    /// <c>CharacterExtensions.TryRemoveSpellFromBar</c>); the bars after it close up. A bar above 7 is ignored.
    /// </summary>
    internal OutboundTicket RemoveSpellFavorite(uint spell, uint bar)
    {
        if (bar > 7)
        {
            return Ticket();
        }

        BarList(bar).Remove(spell);
        return Ticket();
    }

    /// <summary>The stand-in's own list for a bar, seeded from the session's bars on first use.</summary>
    private List<uint> BarList(uint bar)
    {
        if (_bars is null)
        {
            _bars = [];
            foreach (var existing in _reducer.State.Self.Spellbook.SpellBars)
            {
                _bars.Add(existing.IsDefault ? [] : [.. existing]);
            }
        }

        while (_bars.Count <= bar)
        {
            _bars.Add([]);
        }

        return _bars[(int)bar];
    }

    /// <summary><paramref name="description"/> with the bars the stand-in holds since an AddSpellFavorite; unchanged when none was added.</summary>
    internal PlayerDescriptionMsg WithBars(PlayerDescriptionMsg description)
    {
        if (_bars is null)
        {
            return description;
        }

        var bars = ImmutableArray.CreateBuilder<ImmutableArray<uint>>(_bars.Count);
        foreach (var list in _bars)
        {
            bars.Add([.. list]);
        }

        return description with { SpellBars = bars.MoveToImmutable() };
    }
}

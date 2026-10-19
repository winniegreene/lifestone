using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The stand-in's answers to the magic, combat-mode and inventory actions (M3 1.2 s4c), in the order and the messages ACE sends (ACEmulator/ACE@95ef426d, <c>Source/ACE.Server</c>), each
/// <see cref="StandInWorld.DelaySeconds"/> later on the session clock, so the trackers of <c>Session/Actions/Trackers</c> complete as they do against a server.
/// <list type="bullet">
/// <item><b>Cast</b> (0x0048, 0x004A; <c>Player_Magic.cs</c> <c>HandleActionMagicCastUnTargetedSpell</c> 270-335, <c>HandleActionCastTargetedSpell</c> 76-205). Outside the magic combat mode ACE
/// answers only UseDone 0 (283-285, 83-92); a spell the character does not know is UseDone <see cref="MagicInvalidSpellType"/> (129-132, 320-322); a spell the character cannot cast
/// fizzles (<c>GetCastingPreCheckStatus</c> 528-540, then WeenieError 0x028A 0x0402 at 917-918). A self enchantment is added to the registry (<c>EnchantmentManager.Add</c>
/// <c>Managers/EnchantmentManager.cs</c> 134-262, <c>WorldObject_Magic.cs</c> 405-452: GameEvent 0x02C2 with the entry the manager built, the spell's own power and duration); a projectile spell creates the
/// projectile (a placeholder object, below); every cast ends with UseDone 0x01C7 one second after the effect (the recoil, <c>Player_Magic.cs</c> 977-990). With a cast planner the effect comes at the end of the words' and the gestures' timeline (<c>StandInWorld.Cast.cs</c>).</item>
/// <item><b>SetCombatMode</b> (0x0053; <c>Player_Combat.cs</c> <c>HandleActionChangeCombatMode</c> 737-776): the private int update 0x02CD for property 40 (<c>Creature_Combat.cs</c> 120, 176, 201, 246).</item>
/// <item><b>Wield</b> (0x001A; <c>Player_Inventory.cs</c> <c>TryEquipObjectWithNetworking</c> 297-306): 0x02DA Wielder 0, 0x02CE CurrentWieldedLocation 0, then GameEvent 0x0023 with the location.
/// An item the player does not hold is GameEvent 0x00A0 (1538-1543).</item>
/// <item><b>Unwield</b> and <b>MoveToPack</b> (0x0019; <c>TryDequipObjectWithNetworking</c> 400-405 for a worn item, <c>Player_Inventory.cs</c> 1359-1361 for the put): a worn item first gets the Wielder and
/// CurrentWieldedLocation zeros, then every move is 0x02DA Container and GameEvent 0x0022 with the container and the placement.</item>
/// <item><b>MergeStack</b> (0x0054; <c>Player_Inventory.cs</c> <c>HandleActionStackableMerge</c> 2849-3040, <c>DoHandleActionStackableMerge</c> 3072-3178): the refusals in ACE's order (an amount of 0 or less 2853-2859, a source or target that is not
/// held 2866 and 2888, items that do not stack 2912, two different weenies 2929, an empty source or a full target 2937 and 2945, an amount above the source 2953, the room for the sum 3088 and 3138), each GameEvent 0x00A0 for the source.
/// A merge inside the pack is the self-contained branch (3066): a whole source stack is the source's 0x0024 (3096) and the target's 0x0197 (3121-3123), and nothing else (ACE's <c>Destroy</c> of a contained item sends no F747: it only
/// removes a world object that is on a landblock, <c>WorldObject.cs</c> 892); a partial merge is the source's 0x0197 (3151-3153), then the target's (3159-3161). The burden update and the pickup sound belong to the branch that moves a
/// stack between the world and the pack (3048-3053), which the stand-in does not have.</item>
/// </list>
/// Placeholders, each named and logged where it is used: the spell's stat modifier (ACE's world database, not the dat: <c>TODO(m3-standin-spell-statmods)</c>), the magic skill the fizzle rule reads
/// (<c>TODO(m3-standin-magic-skill)</c>) and the missing mana and component rules (<c>TODO(m3-standin-cast-mana)</c>, <c>TODO(m3-standin-cast-components)</c>); the words, the gestures, the effect scripts and the vital spells are <c>StandInWorld.Cast.cs</c> (M3 2.1 s2a). A war spell whose projectile the host knows flies and hits as ACE's does (<c>StandInWorld.Combat.cs</c>, M3 3.1 fb); any other is <c>TODO(m3-standin-projectile-model)</c>: a placeholder object named as such, at the player, deleted a moment later.
/// </summary>
internal sealed partial class StandInWorld
{
    /// <summary>ACE's <c>WeenieError.YourSpellFizzled</c> (0x0402), the line and the cause of a fizzle.</summary>
    internal const uint FizzleError = CastTracker.FizzleCode;

    /// <summary>ACE's <c>WeenieError.MagicInvalidSpellType</c> (0x03FC): the UseDone for a spell the character does not know.</summary>
    internal const uint MagicInvalidSpellType = 0x03FC;

    /// <summary>How long after a cast's effect (or fizzle) UseDone follows: ACE's recoil (<c>Player_Magic.cs</c> 977-990).</summary>
    internal const double CastRecoilSeconds = 1.0;

    /// <summary>
    /// The magic skill the stand-in's character casts with (a placeholder: the stand-in's description carries no skill values, <c>TODO(m3-standin-magic-skill)</c>). ACE fizzles a cast whose spell
    /// power exceeds the skill by more than 50 (<c>GetCastingPreCheckStatus</c>, <c>Player_Magic.cs</c> 528-540); the stand-in never rolls, so a spell fizzles exactly when its power is
    /// above this value plus 50 (level VIII spells, powers of 400 and up).
    /// </summary>
    internal const uint PlaceholderMagicSkill = 300;

    /// <summary>How long the projectile placeholder stays before the stand-in deletes it (ACE deletes it on impact).</summary>
    internal const double ProjectileLifeSeconds = 0.5;

    /// <summary>How far in front of the player the projectile placeholder is created (ACE spawns it just in front of the caster).</summary>
    internal const float ProjectileSpawnAheadMetres = 1f;

    /// <summary>The placeholder projectile's speed along the player's heading, metres per second (a stand-in value; ACE's comes from the spell's weenie).</summary>
    internal const float ProjectileSpeed = 20f;

    /// <summary>The first guid the stand-in gives a projectile.</summary>
    internal const uint FirstProjectileGuid = 0x80020000;

    /// <summary>The Setup the projectile placeholder wears (the small item model of <see cref="PlaceholderItemSetup"/>, never a human: <c>TODO(m3-standin-projectile-model)</c>).</summary>
    private const uint PlaceholderProjectileSetup = PlaceholderItemSetup;

    private const float PlaceholderProjectileScale = 0.1f;

    private const uint SingleStat = 0x1000;

    private const double PlaceholderDurationSeconds = 1800.0;

    private const string UnitTag = "(stand-in placeholder)";

    private static readonly string[] BuffAttributeNames = ["Strength", "Endurance", "Coordination", "Quickness", "Focus", "Willpower"];

    private readonly Dictionary<(uint Guid, uint Tag), byte> _itemSequences = [];
    private readonly Dictionary<uint, StandInPackEntry> _wielded = [];
    private uint _nextProjectileGuid = FirstProjectileGuid;

    /// <summary>A lookup that takes the place of the visit's SpellTable (tests: no dats); null uses the table, and none at all uses the placeholder rule below.</summary>
    internal Func<uint, SpellInfo?>? SpellLookup { get; set; }

    /// <summary>Where the stand-in logs the placeholders it uses (the session's log; set by <c>GameSession</c> after the stand-in ran; null: nothing is logged).</summary>
    internal ILogSink? Log { get; set; }

    private readonly HashSet<string> _placeholdersLogged = [];

    /// <summary>Logs a placeholder the stand-in uses outside its answers (the visit's sends), once per name (<c>TODO(name): what</c>).</summary>
    internal void NotePlaceholder(string todo, string what) => Placeholder(todo, what);

    private void Placeholder(string todo, string what)
    {
        if (Log is { } log && _placeholdersLogged.Add(todo))
        {
            log.Info(SessionLog.Category, "TODO(" + todo + "): " + what);
        }
    }

    /// <summary>The wire messages of these answers in the order they were fed to the session (opcode or event type in hex: 01C7, 028A, 02C2, F745, F747, 02CD, 02DA, 02CE, 0023, 0022, 0024, 0197, 00A0); the tests read it.</summary>
    internal List<string> AnswerLog { get; } = [];

    /// <summary>How many casts the stand-in has fizzled.</summary>
    internal int Fizzles { get; private set; }

    /// <summary>How many projectile placeholders the stand-in has created.</summary>
    internal int Projectiles { get; private set; }

    /// <summary>The items the stand-in's character wields (guid to the item), from the wields it answered.</summary>
    internal IReadOnlyDictionary<uint, StandInPackEntry> Wielded => _wielded;

    /// <summary>
    /// The weenie class ids whose wield the stand-in refuses with GameEvent 0x00A0 (the tests' hook for the refusal path; <c>TODO(m3-standin-wield-rules)</c>: the stand-in applies none of ACE's wield rules, so a wield of a held item otherwise
    /// always succeeds).
    /// </summary>
    internal HashSet<uint> RefusedWieldWcids { get; } = [];

    /// <summary>
    /// The weenie class ids whose merge the stand-in refuses with GameEvent 0x00A0 (the tests' hook for the refusal path of <see cref="MergeStacks"/>; ACE refuses for reasons the stand-in does not model, such as a trade or a stuck stack).
    /// </summary>
    internal HashSet<uint> RefusedMergeWcids { get; } = [];

    /// <summary>
    /// Burns <paramref name="count"/> units of the weenie <paramref name="wcid"/> from the first stack of it in the pack, as a cast burns a component (ACE <c>Player_Magic.cs</c> 1203-1213 takes
    /// <c>GetInventoryItemsOfWCID(wcid).FirstOrDefault()</c>, and the consume tells the stack's 0x0197, or its 0x0024 when it empties): the tests' stand-in for the component burn, which the stand-in's casts do not do
    /// (<c>TODO(m3-standin-cast-components)</c>). Throws when the pack holds no such stack of that size.
    /// </summary>
    internal void Burn(uint wcid, int count)
    {
        var at = _pack.FindIndex(e => e.Wcid == wcid);
        if (at < 0 || _pack[at].Stack < count || count <= 0)
        {
            throw new InvalidOperationException("the stand-in's pack holds no stack of weenie " + wcid + " with " + count + " to burn");
        }

        var entry = _pack[at];
        if (entry.Stack == count)
        {
            _pack.RemoveAt(at);
            Later(sink =>
            {
                AnswerLog.Add("0024");
                sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(entry.Guid));
            });
            return;
        }

        _pack[at] = entry with { Stack = entry.Stack - count };
        Later(sink => StackSize(sink, entry.Guid, entry.Stack - count));
    }

    /// <summary>A Cast (0x0048 with <paramref name="target"/> 0, 0x004A otherwise).</summary>
    internal OutboundTicket Cast(uint spellId, uint target)
    {
        Later(sink => CastNow(sink, spellId, target));
        return Ticket();
    }

    /// <summary>A ChangeCombatMode (0x0053).</summary>
    internal OutboundTicket ChangeCombatMode(uint mode)
    {
        Later(sink =>
        {
            AnswerLog.Add("02CD");
            PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, mode);
        });
        return Ticket();
    }

    /// <summary>A GetAndWieldItem (0x001A).</summary>
    internal OutboundTicket GetAndWield(uint item, uint mask)
    {
        Later(sink => WieldNow(sink, item, mask));
        return Ticket();
    }

    /// <summary>A PutItemInContainer (0x0019): an unwield (the container is the player) and a move to a pack are the same message.</summary>
    internal OutboundTicket PutInContainer(uint item, uint container, uint placement)
    {
        Later(sink => PutNow(sink, item, container, placement));
        return Ticket();
    }

    /// <summary>A StackableMerge (0x0054): <paramref name="amount"/> units of the stack <paramref name="from"/> into the stack <paramref name="to"/>.</summary>
    internal OutboundTicket MergeStacks(uint from, uint to, int amount)
    {
        Later(sink => MergeNow(sink, from, to, amount));
        return Ticket();
    }

    private SpellInfo? FindSpell(uint id) =>
        SpellLookup is { } lookup ? lookup(id) : _visit.SpellTable is { } table && table.Spells.TryGetValue(id, out var info) ? info : null;

    private bool HasSpellData => SpellLookup is not null || _visit.SpellTable is not null;

    private bool Knows(uint spellId) =>
        _reducer.State.Self.Spellbook.Knows(spellId) || (_knownSpells.TryGetValue(_reducer.State.Self.Guid, out var known) && known.Contains(spellId));

    private void UseDone(IAcSessionSink sink, uint error = 0)
    {
        AnswerLog.Add("01C7");
        sink.OnUseDone(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x01C7), new UseDoneEvent(error));
    }

    private void UseDoneAfterRecoil() => _reducer.StandIn!.At(_reducer.Clock.Now + CastRecoilSeconds, sink => UseDone(sink));

    private void CastNow(IAcSessionSink sink, uint spellId, uint target)
    {
        var self = _reducer.State.Self;
        Placeholder("m3-standin-cast-mana", "the stand-in's cast costs no mana");
        Placeholder("m3-standin-cast-components", "the stand-in's cast burns no spell components");
        if (self.Qualities.CombatMode != CombatModes.Magic)
        {
            UseDone(sink); // ACE: a cast outside the magic combat mode is ended with a bare UseDone (Player_Magic.cs 83-92, 275-285)
            return;
        }

        var info = FindSpell(spellId);
        var placeholder = !HasSpellData;
        if (!Knows(spellId) || (placeholder ? spellId is 0 or > PlaceholderMaxSpellId : info is null))
        {
            UseDone(sink, MagicInvalidSpellType); // ACE: VerifySpell fails (Player_Magic.cs 129-132, 320-322)
            return;
        }

        // ACE's sequence (StandInWorld.Cast.cs): the words, the gestures, then the effect (or the fizzle) at the plan's EffectTime and UseDone after the recoil. Without a plan: the effect now, UseDone a second after it.
        var plan = info is null ? null : PlanFor(info);
        if (plan is not null)
        {
            PlayCast(sink, spellId, info, target, plan);
            return;
        }

        ResolveCast(sink, spellId, info, target, null);
        UseDoneAfterRecoil();
    }

    /// <summary>
    /// ACE's <c>EnchantmentManager.Add</c> (134-262) for the player as target and caster: the first enchantment of the spell's category takes layer 1; the same spell by the same caster
    /// refreshes its entry (the same layer; the duration restarts); any other takes the next layer of the category. GameEvent 0x02C2 carries the entry.
    /// </summary>
    private void AddEnchantment(IAcSessionSink sink, uint spellId, SpellInfo? info)
    {
        var self = _reducer.State.Self;
        var category = (ushort)(info?.Category ?? spellId);
        ushort layer = 1;
        foreach (var entry in self.Qualities.Enchantments)
        {
            if (entry.SpellCategory != category)
            {
                continue;
            }

            if (entry.SpellId == spellId && entry.Caster == self.Guid)
            {
                layer = entry.Layer;
                break;
            }

            layer = (ushort)Math.Max(layer, entry.Layer + 1);
        }

        var (type, key, value) = StatMod(info);
        Placeholder("m3-standin-spell-statmods", "ACE's stat modifiers are in its world database, not the dat: the stand-in gives an attribute self spell +10 on its attribute and any other self enchantment +10 maximum health");
        var bucket = (type & EnchantmentRules.Multiplicative) != 0 ? EnchantmentBucket.Multiplicative : EnchantmentBucket.Additive;
        var power = info?.Power ?? 1u;
        var duration = info is { Duration: > 0 } ? info.Duration : PlaceholderDurationSeconds;
        var enchantment = new EnchantmentDesc((ushort)spellId, layer, category, 0, power, 0.0, duration, self.Guid, info?.DegradeModifier ?? 0f, info?.DegradeLimit ?? 0f, 0.0, type, key, value, 0, bucket);
        AnswerLog.Add("02C2");
        sink.OnMagicUpdateEnchantment(Context(sink), new GameEventHeader(self.Guid, 0, 0x02C2), new MagicUpdateEnchantmentEvent(enchantment));
    }

    /// <summary>
    /// The stat modifier of a self enchantment: ACE reads it from its world database's spell table, which neither the dat nor the stand-in has (<c>TODO(m3-standin-spell-statmods)</c>), so the
    /// stand-in gives a spell whose name holds an attribute self line ("Strength Self V", "Incantation of Strength Self") an additive +10 on that attribute and any other self enchantment an additive +10 on the maximum health. Type bits as ACE
    /// stores them: beneficial, additive, a single stat, the attribute (1) or the secondary attribute (2).
    /// </summary>
    private static (uint Type, uint Key, float Value) StatMod(SpellInfo? info)
    {
        const uint common = EnchantmentRules.BeneficialKind | EnchantmentRules.Additive | SingleStat;
        if (info is not null)
        {
            for (var i = 0; i < BuffAttributeNames.Length; i++)
            {
                if (info.Name.Contains(BuffAttributeNames[i] + " Self", StringComparison.Ordinal))
                {
                    return (common | EnchantmentRules.AttributeKind, (uint)i + 1, 10f);
                }
            }
        }

        return (common | EnchantmentRules.SecondaryAttributeKind, 1, 10f);
    }

    /// <summary>
    /// The projectile of a war spell: ACE creates the spell projectile at the caster (<c>WorldObject_Magic.cs</c> <c>CreateSpellProjectile</c>) and tells the client with CreateObject 0xF745 in the
    /// Missile physics state. When the host knows the spell's projectile (<see cref="StandInVisit.ProjectileSetup"/>) it is ACE's, with its flight and hit (<c>StandInWorld.Combat.cs</c>, M3 3.1 fb). Otherwise
    /// the object is the old placeholder, labelled in its name and logged, small, and deleted <see cref="ProjectileLifeSeconds"/> later (<c>TODO(m3-standin-projectile-model)</c>, now only for a spell the host has no projectile for).
    /// </summary>
    private void CreateProjectile(IAcSessionSink sink, SpellInfo info, uint target)
    {
        if (ModelFor(info) is { } model)
        {
            LaunchBolt(sink, info, target, model); // M3 3.1 fb: the projectile as ACE sends it (StandInWorld.Combat.cs)
            return;
        }

        var self = _reducer.State.Self;
        var known = _visit.ProjectileSetup?.Invoke(info.Id);
        var guid = _nextProjectileGuid++;
        Projectiles++;
        Placeholder("m3-standin-projectile-model", "the projectile is a stand-in placeholder object (a small human setup named '" + info.Name + " " + UnitTag + "'), deleted " + ProjectileLifeSeconds + " s after its creation");
        var at = self.Position;
        // ACE spawns the projectile in front of the caster and launches it along the aim; the placeholder does the same along the player's heading (the cast tracker's attribution reads both).
        var facing = Quaternion.Normalize(at is { } q ? q.Frame.Orientation : Quaternion.Identity);
        var forward = Vector3.Transform(Vector3.UnitY, facing);
        if (target != 0 && _reducer.State.Objects.TryGet(target, out var victim) && victim.Position is { } goal && at is { } shooter)
        {
            // Along the aim: at a target the stand-in knows, as ACE launches it; else along the player's heading.
            var line = goal.ToGlobal();
            var here = shooter.ToGlobal();
            var aim = new Vector3((float)(line.X - here.X), (float)(line.Y - here.Y), (float)(line.Z - here.Z));
            if (aim.LengthSquared() > 1e-6f)
            {
                forward = Vector3.Normalize(aim);
            }
        }

        var ahead = forward * ProjectileSpawnAheadMetres + new Vector3(0f, 0f, 1f);
        var position = at is { } p ? new AcPosition(p.CellId, p.Frame.Origin + ahead, p.Frame.Orientation) : new AcPosition(_visit.Cell, _visit.Origin + ahead, Quaternion.Identity);
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position | PhysicsDescFlags.ObjScale | PhysicsDescFlags.Velocity,
            Velocity = forward * ProjectileSpeed,
            State = PhysicsState.Missile | PhysicsState.IgnoreCollisions | PhysicsState.Ethereal,
            Position = position,
            SetupId = known ?? PlaceholderProjectileSetup,
            Scale = known is null ? PlaceholderProjectileScale : 1f,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = info.Name + " " + UnitTag,
            WeenieClassId = 2,
            IconId = 0x06000001,
            ItemType = 0,
            ObjectDescriptionFlags = 0,
        };
        AnswerLog.Add("F745");
        sink.OnCreateObject(Context(sink), new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isUpdate: false);
        _reducer.StandIn!.At(
            _reducer.Clock.Now + ProjectileLifeSeconds,
            later =>
            {
                AnswerLog.Add("F747");
                later.OnDeleteObject(Context(later), new DeleteObjectMsg(guid, 1));
            });
    }

    private void WieldNow(IAcSessionSink sink, uint item, uint mask)
    {
        var self = _reducer.State.Self;
        var from = self.Inventory.Find(item);
        if (!from.Held)
        {
            Failed(sink, item); // ACE: "Item not found!" and GameEvent 0x00A0 (Player_Inventory.cs 1538-1543)
            return;
        }

        if (RefusedWieldWcids.Count > 0 && _reducer.State.Objects.TryGet(item, out var refused) && RefusedWieldWcids.Contains(refused.Weenie.WeenieClassId))
        {
            Failed(sink, item);
            return;
        }

        StandInWorn? worn = null;
        if (_pack.FindIndex(e => e.Guid == item) is >= 0 and var at)
        {
            _wielded[item] = _pack[at];
            worn = _pack[at].Worn;
            _pack.RemoveAt(at);
        }

        ParentEventNow(sink, item, mask); // ACE broadcasts the parent event first (M3 4.1 fix A, R6)
        SendLook(sink, worn); // then the ObjDescEvent (Creature_Equipment.cs 365)
        Zeros(sink, item);
        AnswerLog.Add("0023");
        sink.OnWieldObject(Context(sink), new GameEventHeader(self.Guid, 0, 0x0023), new WieldObjectEvent(item, mask));
    }

    private void PutNow(IAcSessionSink sink, uint item, uint container, uint placement)
    {
        var self = _reducer.State.Self;
        var from = self.Inventory.Find(item);
        if (!from.Held || (container != self.Guid && !self.Inventory.IsKnownContainer(container)))
        {
            Failed(sink, item);
            return;
        }

        if (from.Kind == InventoryPlaceKind.Equipped)
        {
            Zeros(sink, item); // ACE: TryDequipObjectWithNetworking (Player_Inventory.cs 400-405)
        }

        if (_wielded.Remove(item, out var entry))
        {
            _pack.Add(entry);
            SendLook(sink, entry.Worn); // TryDequipObjectWithNetworking: the ObjDescEvent after the dequip (Creature_Equipment.cs 438)
        }

        var count = self.Inventory.ItemsIn(container).Count - (from.Container == container && from.Kind != InventoryPlaceKind.Equipped ? 1 : 0);
        var slot = Math.Min(placement, (uint)Math.Max(count, 0));
        Instance(sink, item, ContainerProperty, container); // ACE: the put (Player_Inventory.cs 1359-1361)
        AnswerLog.Add("0022");
        sink.OnInventoryPutObjInContainer(Context(sink), new GameEventHeader(self.Guid, 0, 0x0022), new InventoryPutObjInContainerEvent(item, container, slot, container == self.Guid ? 0u : 1u));
    }

    /// <summary>ACE's <c>WeenieError.YouCannotMergeDifferentStacks</c> (0x0427), the error of the 0x00A0 for two different weenies.</summary>
    internal const uint CannotMergeDifferentStacks = 0x0427;

    private const uint StackSizeTag = 0x20000;

    private void MergeNow(IAcSessionSink sink, uint from, uint to, int amount)
    {
        var self = _reducer.State.Self;
        var inventory = self.Inventory;
        var objects = _reducer.State.Objects;
        if (amount <= 0 || !inventory.Find(from).Held || !inventory.Find(to).Held)
        {
            Failed(sink, from); // ACE: "Merge amount not valid!", "Source stack not found!" or "Target stack not found!", then GameEvent 0x00A0 for the source (Player_Inventory.cs 2856-2858, 2866-2868, 2888-2890)
            return;
        }

        if (!objects.TryGet(from, out var source) || !objects.TryGet(to, out var target) || source.Weenie.MaxStackSize is null || target.Weenie.MaxStackSize is null
            || (RefusedMergeWcids.Count > 0 && RefusedMergeWcids.Contains(source.Weenie.WeenieClassId)))
        {
            Failed(sink, from); // ACE: "You cannot merge those items!" for an item that is not a Stackable (2912-2922)
            return;
        }

        if (source.Weenie.WeenieClassId != target.Weenie.WeenieClassId)
        {
            Failed(sink, from, CannotMergeDifferentStacks); // ACE: "Stacks not compatible!", YouCannotMergeDifferentStacks (2929-2931)
            return;
        }

        var fromAt = _pack.FindIndex(e => e.Guid == from);
        var toAt = _pack.FindIndex(e => e.Guid == to);
        if (fromAt < 0 || toAt < 0)
        {
            Failed(sink, from); // an item the stand-in did not create (a wielded one): not a stack it keeps
            return;
        }

        var maxStack = target.Weenie.MaxStackSize.Value;
        var src = _pack[fromAt];
        var dst = _pack[toAt];
        if (src.Stack <= 0 || dst.Stack <= 0 || dst.Stack == maxStack || src.Stack < amount || dst.Stack + amount > maxStack)
        {
            Failed(sink, from); // ACE: "Stack not valid!", "Target not valid!" (target at its maximum), "Merge amount not valid!" (2937-2955) and the room check in DoHandleActionStackableMerge (3088, 3138)
            return;
        }

        var whole = amount == src.Stack;
        if (whole)
        {
            AnswerLog.Add("0024");
            sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(from)); // 3096 (the Destroy that follows sends nothing for a contained item)
            _pack.RemoveAt(fromAt);
            toAt = _pack.FindIndex(e => e.Guid == to);
        }
        else
        {
            _pack[fromAt] = src with { Stack = src.Stack - amount };
            StackSize(sink, from, src.Stack - amount); // 3151-3153
        }

        _pack[toAt] = dst with { Stack = dst.Stack + amount };
        StackSize(sink, to, dst.Stack + amount); // 3121-3123, 3159-3161
    }

    private void StackSize(IAcSessionSink sink, uint item, int size)
    {
        // The stack's value is 0: the stand-in has no weenie values (TODO(m3-standin-weenies)).
        AnswerLog.Add("0197");
        sink.OnSetStackSize(Context(sink), new SetStackSizeMsg(NextSequence(item, StackSizeTag), item, (uint)size, 0));
    }

    private const uint ContainerProperty = 2;
    private const uint WielderProperty = 3;
    private const uint CurrentWieldedLocationProperty = 10;

    private void Zeros(IAcSessionSink sink, uint item)
    {
        Instance(sink, item, WielderProperty, 0);
        var sequence = NextSequence(item, CurrentWieldedLocationProperty);
        AnswerLog.Add("02CE");
        sink.OnPublicUpdatePropertyInt(Context(sink), new PublicUpdatePropertyIntMsg(sequence, item, CurrentWieldedLocationProperty, 0));
    }

    private void Instance(IAcSessionSink sink, uint item, uint property, uint value)
    {
        AnswerLog.Add("02DA");
        sink.OnPublicUpdateInstanceId(Context(sink), new PublicUpdateInstanceIdMsg(NextSequence(item, 0x10000 | property), item, property, value));
    }

    private byte NextSequence(uint item, uint tag)
    {
        _itemSequences.TryGetValue((item, tag), out var last);
        var next = unchecked((byte)(last + 1));
        _itemSequences[(item, tag)] = next;
        return next;
    }

    private void Failed(IAcSessionSink sink, uint item, uint error = 0)
    {
        AnswerLog.Add("00A0");
        sink.OnInventoryServerSaysFailed(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x00A0), new InventoryServerSaysFailedEvent(item, error));
    }
}

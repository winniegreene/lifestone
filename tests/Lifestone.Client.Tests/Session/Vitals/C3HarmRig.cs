using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// The harm-heal script's scripted rig (M4 P5a lane C3): <see cref="C2VitalsRig"/>'s character (the vitals, the kit, the potions, the casts with Harm Self added, the combat mode) plus, for a fighter, the weapon it
/// wields and ACE's wield and unwield answers with the <c>CheckWeaponCollision</c> rule of <see cref="Session.Fighter.C1bSwapRig"/> (a Held caster is refused while a melee weapon or a launcher is wielded and the other way round),
/// a <see cref="CasterSwap"/> over the Action API, and the <see cref="HarmHealScript"/> that drives them. <see cref="Timeline"/> is the order of everything the script made the server do (<c>unwield:Mace</c>, <c>wield:The Healer's Heart</c>,
/// <c>mode:8</c>, <c>cast:2071</c>, <c>kit</c>, <c>potion</c>, ...), from the swap's and the character's pumps together.
/// </summary>
internal sealed class C3HarmRig
{
    private readonly Dictionary<uint, (string Name, uint Location)> _items = [];
    private int _seenWield;
    private int _seenUsed;

    /// <summary>A rig for <paramref name="character"/>; <paramref name="template"/> is the fighter template (a swap and its weapon), or null for the mage (it wields its wand, no swap).</summary>
    public C3HarmRig(C2Character character, FighterTemplate? template = null, HarmHealOptions? options = null, VitalsOptions? vitals = null)
    {
        Template = template;
        Inner = new C2VitalsRig(character, vitals);
        R = Inner.R;
        if (template is not null)
        {
            _items[C2VitalsRig.WandGuid] = (GearKit.Wand.Name, 0);
            if (template.Kit.HasMelee)
            {
                Add(WeaponGuid, template.Kit.Items.Single(i => i.Role == FighterRole.Weapon).Item, EquipMask.MeleeWeapon);
            }
            else
            {
                Add(WeaponGuid, template.Kit.Items.Single(i => i.Role == FighterRole.Bow).Item, EquipMask.MissileWeapon);
                Add(AmmoGuid, template.Kit.Items.Single(i => i.Role == FighterRole.Ammo).Item, EquipMask.MissileAmmo);
            }

            Swap = new CasterSwap(R.Api, R.H.State, template.Kit, R.H.Log);
        }

        _seenWield = R.H.Net.Sends.Count;
        Script = new HarmHealScript(new View(R), Inner.Table, Inner.Keeper, Swap, options, R.H.Log);
    }

    public const uint WeaponGuid = 0x80000300;
    public const uint AmmoGuid = 0x80000301;

    public FighterTemplate? Template { get; }

    public C2VitalsRig Inner { get; }

    public WorldActionRig R { get; }

    public CasterSwap? Swap { get; }

    public HarmHealScript Script { get; }

    /// <summary>Everything the server was asked, in order (wields, unwields, mode changes, casts, kit, potion), refused wields marked.</summary>
    public List<string> Timeline { get; } = [];

    public double Now => Inner.Now;

    public uint Health => Inner.Health;

    /// <summary>The equip location of the item (0: in the pack).</summary>
    public uint LocationOf(uint guid) => _items[guid].Location;

    /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers (the character's actions, then the wields), the Action API completes, then the script steps.</summary>
    public HarmHealState Frame(double dt = 0.25)
    {
        R.Tick(dt);
        Inner.Pump();
        PumpWield();
        for (; _seenUsed < Inner.Used.Count; _seenUsed++)
        {
            Timeline.Add(Inner.Used[_seenUsed]);
        }

        R.Tick();
        return Script.Step(Now);
    }

    /// <summary>Frames until the script is Done or Failed, or <paramref name="maxSeconds"/> passed; the final state.</summary>
    public HarmHealState RunToEnd(double maxSeconds = 4000, double dt = 0.25)
    {
        var end = Now + maxSeconds;
        var state = Script.State;
        while (Now < end)
        {
            state = Frame(dt);
            if (state is HarmHealState.Done or HarmHealState.Failed)
            {
                return state;
            }
        }

        return state;
    }

    private void Add(uint guid, GearItem kind, EquipMask location)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: kind.Name);
        R.H.R.Reducer.OnCreateObject(
            R.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, ItemType = kind.ItemType, WeenieClassId = kind.Wcid } },
            isUpdate: false);
        R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, (int)location));
        _items[guid] = (kind.Name, (uint)location);
    }

    /// <summary>Answers every wield and unwield the Action API sent since the last call, as ACE does (the mode changes are the character's, <see cref="C2VitalsRig.Pump"/>).</summary>
    private void PumpWield()
    {
        var sends = R.H.Net.Sends;
        while (_seenWield < sends.Count)
        {
            var send = sends[_seenWield++];
            switch (send.Kind)
            {
                case FakeSendKind.GetAndWieldItem when _items.TryGetValue(send.Guid, out var wield):
                    var mask = (uint)send.Amount;
                    var held = (mask & (uint)EquipMask.Held) != 0;
                    var weapon = (mask & (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon)) != 0;
                    var collides = (held && _items.Values.Any(i => (i.Location & (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon)) != 0))
                        || (weapon && _items.Values.Any(i => (i.Location & (uint)EquipMask.Held) != 0));
                    if (collides)
                    {
                        Timeline.Add("wield-refused:" + wield.Name);
                        R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, send.Guid, 0));
                        break;
                    }

                    Timeline.Add("wield:" + wield.Name);
                    _items[send.Guid] = (wield.Name, mask);
                    R.Server((p, s) => AceM3Writers.WieldObject(p, s, send.Guid, unchecked((int)mask)));
                    break;
                case FakeSendKind.PutItemInContainer when _items.TryGetValue(send.Guid, out var put):
                    Timeline.Add("unwield:" + put.Name);
                    _items[send.Guid] = (put.Name, 0);
                    R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, send.Guid, p, 0, 0));
                    break;
            }
        }
    }

    private sealed class View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }
}

using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The vitals keeper with a caster swap on the scripted ACE (M4 P5b lane J3a, closes <c>TODO(m4-keeper-swap)</c>): <see cref="C2VitalsRig"/>'s character (vitals, kit, potions, casts) plus a fighter's weapon wielded and ACE's wield and unwield
/// answers with <c>CheckWeaponCollision</c> (a Held caster is refused while a melee weapon or a launcher is wielded and the other way round; <see cref="RefuseCaster"/> refuses the caster outright), a <see cref="CasterSwap"/> and a
/// <see cref="VitalsKeeper"/> built over it. <see cref="Timeline"/> is the order of everything the keeper made the server do (<c>unwield:Mace</c>, <c>wield:...</c>, <c>mode:8</c>, <c>cast:1158</c>, <c>mode:1</c>, ...), from the character's and the wield pumps together.
/// </summary>
internal sealed class J3aSwapKeeperRig
{
    public const uint WeaponGuid = 0x80000300;

    private readonly Dictionary<uint, (string Name, uint Location)> _items = [];
    private int _seenWield;
    private int _seenUsed;

    public J3aSwapKeeperRig(C2Character character, FighterTemplate template, bool refuseCaster = false, VitalsOptions? options = null)
    {
        RefuseCaster = refuseCaster;
        Inner = new C2VitalsRig(character, options);
        R = Inner.R;
        _items[C2VitalsRig.WandGuid] = (GearKit.Wand.Name, 0);
        Add(WeaponGuid, template.Kit.Items.Single(i => i.Role == FighterRole.Weapon).Item, EquipMask.MeleeWeapon);
        Swap = new CasterSwap(R.Api, R.H.State, template.Kit, R.H.Log);
        Keeper = new VitalsKeeper(new J3aView(R), Inner.Table, Inner.Options, R.H.Log, Swap);
        _seenWield = R.H.Net.Sends.Count;
    }

    public bool RefuseCaster { get; }

    public C2VitalsRig Inner { get; }

    public WorldActionRig R { get; }

    public CasterSwap Swap { get; }

    public VitalsKeeper Keeper { get; }

    public List<string> Timeline { get; } = [];

    public double Now => Inner.Now;

    /// <summary>The equip location of the item (0: in the pack).</summary>
    public uint LocationOf(uint guid) => _items[guid].Location;

    /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers (the character's actions, then the wields), the Action API completes, then the keeper steps.</summary>
    public VitalsKeeperState Frame(double dt = 0.25)
    {
        R.Tick(dt);
        Inner.Pump();
        PumpWield();
        for (; _seenUsed < Inner.Used.Count; _seenUsed++)
        {
            Timeline.Add(Inner.Used[_seenUsed]);
        }

        R.Tick();
        return Keeper.Step(Now);
    }

    /// <summary>Frames until the keeper is Ready or Blocked and idle (no swap half under way), or <paramref name="maxSeconds"/> passed.</summary>
    public VitalsKeeperState RunToEnd(double maxSeconds = 2000, double dt = 0.25)
    {
        var end = Now + maxSeconds;
        var state = Keeper.State;
        while (Now < end)
        {
            state = Frame(dt);
            if (state is VitalsKeeperState.Ready or VitalsKeeperState.Blocked && !Keeper.Busy)
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
                        || (weapon && _items.Values.Any(i => (i.Location & (uint)EquipMask.Held) != 0))
                        || (held && RefuseCaster);
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
}

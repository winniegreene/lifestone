using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The fighter's caster swap on a scripted ACE (M4 P5a C1b): <see cref="ScriptedCaster"/>'s world (the cast answers, the spell table, the regeneration) with the wand taken out of the hand into the pack and the fighter's weapon
/// wielded instead (the Brawler's Mace, or the Archer's Longbow with its Greater Frost Arrows in the ammunition slot), and a <see cref="RebuffDriver"/> over a <see cref="CasterSwap"/>. <see cref="Pump"/> answers each
/// wield and unwield the Action API sent as ACE does: GameEvent 0x0023 or the move to the pack, and ACE's <c>CheckWeaponCollision</c> (<c>Player_Inventory.cs</c>): a Held item is refused while a melee weapon or a launcher
/// is wielded and the other way round (GameEvent 0x00A0, WeenieError 0). <see cref="RefuseCaster"/> makes the server refuse the caster outright. <see cref="Ops"/> is the order of what the server was asked
/// (<c>unwield:Mace</c>, <c>wield:The Healer's Heart</c>, <c>mode:8</c>), refused ones marked.
/// </summary>
internal sealed class C1bSwapRig
{
    public const uint WandGuid = 0x80000200;
    public const uint WeaponGuid = 0x80000300;
    public const uint AmmoGuid = 0x80000301;

    private readonly Dictionary<uint, (string Name, uint Location)> _items = [];
    private int _seen;

    /// <summary>A rig for the fighter template <paramref name="template"/>: the Brawler (a melee weapon) or the Archer (a launcher and its ammunition).</summary>
    /// <param name="fighterProfile">True (M4 P5a C4): the scripted ACE's spell table and the cycle are the fighter's profile (its buff and mana lines), not the mage's.</param>
    public C1bSwapRig(FighterTemplate template, bool refuseCaster = false, bool withSwap = true, bool fighterProfile = false, uint manaPotions = 0)
    {
        Template = template;
        RefuseCaster = refuseCaster;
        R = new WorldActionRig();
        var profile = fighterProfile ? FighterProfile.For(template) : null;
        Caster = new ScriptedCaster(R, profile is null ? 426u : 224u, profile is null ? 275u : 312u, lines: profile?.Lines.Where(l => l.IsBuff || l.IsManaLine).ToList(), fighter: profile is null ? null : template, manaPotions: manaPotions);
        Options = new BuffOptions { RebuffMarginSeconds = 10, Auto = true };
        View = new SwapView(R);
        Cycle = new BuffCycle(R.Api, R.H.State, Caster.Table, Options, R.H.Log, profile: profile);
        Swap = withSwap ? new CasterSwap(R.Api, R.H.State, template.Kit, R.H.Log) : null;
        Driver = new RebuffDriver(Cycle, R.Api, View, Options, R.H.Log, Swap);

        // The scripted mage wields its wand; the fighter has it in the pack (or not at all), no weapon-slot item but its own, and is in peace.
        _items[WandGuid] = (GearKit.Wand.Name, Location: 0);
        R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, WandGuid, p, 0, 0));

        if (template.Kit.HasMelee)
        {
            Add(WeaponGuid, template.Kit.Items.Single(i => i.Role == FighterRole.Weapon).Item, EquipMask.MeleeWeapon);
        }
        else
        {
            Add(WeaponGuid, template.Kit.Items.Single(i => i.Role == FighterRole.Bow).Item, EquipMask.MissileWeapon);
            Add(AmmoGuid, template.Kit.Items.Single(i => i.Role == FighterRole.Ammo).Item, EquipMask.MissileAmmo);
        }

        R.CombatModeIs(CombatModes.NonCombat);
        Ops.Clear();
        _seen = R.H.Net.Sends.Count;
    }

    private sealed class SwapView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    public FighterTemplate Template { get; }

    public bool RefuseCaster { get; set; }

    public WorldActionRig R { get; }

    public ScriptedCaster Caster { get; }

    public BuffOptions Options { get; }

    public IGameSessionView View { get; }

    public BuffCycle Cycle { get; }

    public CasterSwap? Swap { get; }

    public RebuffDriver Driver { get; }

    /// <summary>What the server was asked, in order: <c>unwield:&lt;name&gt;</c>, <c>wield:&lt;name&gt;</c>, <c>wield-refused:&lt;name&gt;</c>, <c>mode:&lt;n&gt;</c>.</summary>
    public List<string> Ops { get; } = [];

    public double Now => R.H.R.Clock.Now;

    /// <summary>The equip location of the item (0: in the pack).</summary>
    public uint LocationOf(uint guid) => _items[guid].Location;

    public int CombatMode => R.H.State.Self.Qualities.CombatMode;

    private void Add(uint guid, GearItem kind, EquipMask location)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: kind.Name);
        R.H.R.Reducer.OnCreateObject(
            R.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, ItemType = kind.ItemType, WeenieClassId = kind.Wcid } },
            isUpdate: false);
        _items[guid] = (kind.Name, 0);
        R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, (int)location));
        _items[guid] = (kind.Name, (uint)location);
    }

    /// <summary>Answers every wield, unwield and mode change the Action API sent since the last call, as ACE does.</summary>
    public void Pump()
    {
        var sends = R.H.Net.Sends;
        while (_seen < sends.Count)
        {
            var send = sends[_seen++];
            switch (send.Kind)
            {
                case FakeSendKind.ChangeCombatMode:
                    Ops.Add("mode:" + (int)send.Amount);
                    break;
                case FakeSendKind.GetAndWieldItem when _items.TryGetValue(send.Guid, out var wield):
                    var mask = (uint)send.Amount;
                    var held = (mask & (uint)EquipMask.Held) != 0;
                    var weapon = (mask & (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon)) != 0;
                    var collides = (held && _items.Values.Any(i => (i.Location & (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon)) != 0))
                        || (weapon && _items.Values.Any(i => (i.Location & (uint)EquipMask.Held) != 0));
                    if (collides || (held && RefuseCaster))
                    {
                        Ops.Add("wield-refused:" + wield.Name);
                        R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, send.Guid, 0));
                        break;
                    }

                    Ops.Add("wield:" + wield.Name);
                    _items[send.Guid] = (wield.Name, mask);
                    R.Server((p, s) => AceM3Writers.WieldObject(p, s, send.Guid, unchecked((int)mask)));
                    break;
                case FakeSendKind.PutItemInContainer when _items.TryGetValue(send.Guid, out var put):
                    Ops.Add("unwield:" + put.Name);
                    _items[send.Guid] = (put.Name, 0);
                    R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, send.Guid, p, 0, 0));
                    break;
            }
        }
    }

    /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers (casts and wields), the Action API completes, then the driver ticks.</summary>
    public void Frame(double dt = 0.25)
    {
        R.Tick(dt);
        Caster.Pump();
        Pump();
        R.Tick();
        Driver.Tick(Now);
    }

    /// <summary>Frames until <paramref name="done"/> holds or <paramref name="maxSeconds"/> of session time have passed; false when the time ran out.</summary>
    public bool RunUntil(Func<bool> done, double maxSeconds = 2000, double dt = 0.25)
    {
        var end = Now + maxSeconds;
        while (Now < end)
        {
            if (done())
            {
                return true;
            }

            Frame(dt);
        }

        return done();
    }

    /// <summary>Runs one half of the swap by hand (the driver is not ticked) until it ends; returns the status.</summary>
    public SwapStatus RunSwap(SwapDirection direction, double maxSeconds = 120)
    {
        Assert.True(Swap!.Begin(direction, Now));
        var end = Now + maxSeconds;
        while (Swap.Status == SwapStatus.Running && Now < end)
        {
            R.Tick(0.25);
            Caster.Pump();
            Pump();
            R.Tick();
            Swap.Step(Now);
        }

        return Swap.Status;
    }

    /// <summary>Runs the driver until its first pass has started and ended (<see cref="RebuffDriver.Passes"/> 1 and the state not Running).</summary>
    public bool RunOnePass(double maxSeconds = 2000) => RunUntil(() => Driver.Passes >= 1 && Driver.State != RebuffDriverState.Running, maxSeconds);
}

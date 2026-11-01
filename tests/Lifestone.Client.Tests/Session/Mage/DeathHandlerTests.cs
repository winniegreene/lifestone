using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The death handler (M5 item 3 d1, D51, DoD 7) over a scripted ACE in ACE's order (<c>Player_Death.cs</c> 34-292 @95ef426d: the victim notice, the Dead motion, health 0, NumDeaths; after the animation the wielded items leave for the corpse, the corpse and the
/// teleport to the lifestone in one tick; the 75% vitals 3 s on), on both dungeon profiles: the whole order tied (Primary Portal Recall, the runner to the corpse with <c>purpose=corpse</c>, the items back, the wand wielded and the piece worn again,
/// the item lines re-cast, the fight resumed), untied (0x04A3, the runner route, its named refusal), a rotted corpse, a corpse not found, a second death before the trip, a death on the trip, and <c>vitae_grants=0</c> with no mod verb in every one.
/// The scripted ACE answers the recall (<see cref="ScriptedCaster.CastHook"/>), the corpse's open, the pick-ups and the wields; the corpse leg is a scripted runner seam (the player walks to the corpse's cell); the inner fight is scripted.
/// </summary>
public sealed class DeathHandlerTests(ITestOutputHelper output)
{
    public static TheoryData<string> Grounds => new() { "black-spawn-den-aluvian", "tusker-temple-fort-tethana" };

    private const uint CorpseGuidBase = 0x80000900;
    private const uint CasterType = 0x8000;
    private const uint MiscType = 0x80;

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

    private sealed class StillWalk : IFightWalk
    {
        public int Prepared { get; private set; }

        public string? Failure => null;

        public void Prepare(IGameSessionView session, double now) => Prepared++;

        public int Legs { get; private set; }

        public int Abandons { get; private set; }

        public int Disposed { get; private set; }

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            Legs++;
            description = "still";
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now) => FightWalkStatus.Idle;

        public void Abandon() => Abandons++;

        public void Dispose() => Disposed++;
    }

    /// <summary>The inner fight: a buff stage of three steps that ends at <c>place</c>, or the magic mode and a kill every second step of the fight.</summary>
    private sealed class Leg(HandlerRig rig, FightStages stages, FightOptions options, bool firstBuff) : IFightLeg
    {
        private int _steps;

        public string Name => "fight";

        public string Stage { get; private set; } = "start";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public int Kills { get; private set; }

        public string BuffsNote { get; private set; } = "n/a";

        public bool BuffsAllUp => (stages & FightStages.Buffs) != 0 && (!firstBuff || rig.RebuffAllUp);

        public IReadOnlyList<string> BuffMisses => firstBuff && !rig.RebuffAllUp ? ["Impenetrability"] : [];

        public string VerdictFields() => "buffs=" + BuffsNote;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            _steps++;
            rig.OnInner?.Invoke(stages, _steps);
            if (Stage == "start")
            {
                Stage = (stages & FightStages.Buffs) != 0 ? "buffs-cycle" : "mode-magic";
            }
            else if (Stage == "buffs-cycle" && _steps >= 4)
            {
                BuffsNote = firstBuff ? "topped-up:24" : "topped-up:2";
                Stage = "place";
            }
            else if (Stage == "mode-magic")
            {
                Stage = "fight";
            }
            else if (Stage == "fight" && _steps % 2 == 0 && ++Kills >= options.Kills)
            {
                Stage = "done";
                return ScenarioStatus.Succeeded;
            }

            return ScenarioStatus.Running;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>The corpse leg on foot: after a few steps the player stands in the destination's cell (a move, no teleport); every request is recorded.</summary>
    private sealed class RunTravel(HandlerRig rig) : IJourneyTravel
    {
        public List<TravelRequest> Requests { get; } = [];

        /// <summary>The frames the leg runs before the player stands in the destination's cell (a huge value: the leg never arrives).</summary>
        public int Steps { get; set; } = 4;

        public int Pauses { get; private set; }

        public int Resumes { get; private set; }

        public int Disposed { get; private set; }

        public bool Busy => false;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            return new RunLeg(rig, this, request);
        }

        private sealed class RunLeg(HandlerRig rig, RunTravel travel, TravelRequest request) : ITravelLeg
        {
            private int _steps;

            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => null;

            public string Fields => "route_m=1.0 run_s=0.5";

            public TravelStatus Step(IGameSessionView session, double now)
            {
                if (++_steps < travel.Steps)
                {
                    return TravelStatus.Running;
                }

                if (_steps == travel.Steps)
                {
                    var at = request.Destination.Location.Position;
                    if (request.Purpose == TravelPurpose.Vendor)
                    {
                        rig.StandAt(at.CellId, at.Local); // the vendor's stop (M5 3 F5: the rearm's kit trip walks to him)
                    }
                    else
                    {
                        rig.WalkTo(at.CellId);
                    }
                }

                return TravelStatus.Arrived;
            }

            public void Pause() => travel.Pauses++;

            public void Resume(double now) => travel.Resumes++;

            public void Dispose() => travel.Disposed++;
        }
    }

    /// <summary>The scripted ACE, the mage (the wand in hand and five pieces worn, <see cref="ScriptedCaster"/>), the ground's home lifestone and the handler over them.</summary>
    private sealed class HandlerRig
    {
        private readonly List<(double Due, Action Do)> _timeline = [];
        private readonly Dictionary<uint, WorldObject> _items = [];
        private readonly byte[] _vitals = new byte[8];
        private int _seen;
        private ushort _teleport = 2;
        private ushort _position = 2;
        private ushort _state = 2;
        private ushort _movement = 1;
        private uint _event = 500;
        private int _numDeaths;
        private CreateObjectMsg? _corpseCreate;
        private uint _corpseCell;
        private byte _unwieldSeq;

        /// <summary>The spare wand the pack holds by default (M5 3 F5: the rearm's pack case; the wielded wand goes to the corpse with the death, so a mage with a caster in the pack wields it).</summary>
        public const uint SpareGuid = 0x80000210;

        private uint _row = 0x80006000;

        /// <param name="groundKey">The dungeon profile.</param>
        /// <param name="spare">True (the default): the pack holds a spare caster, the profile's kit wand line (the rearm wields it); false: the pack holds none.</param>
        /// <param name="alter">A change to the profile (a ground whose vendors do not sell the wand).</param>
        /// <param name="spareWcid">The spare's weenie class, or null for the kit's wand line (M5 3 gate round 3: the new character's Training Wand is another caster).</param>
        public HandlerRig(string groundKey, bool spare = true, Func<HuntingGround, HuntingGround>? alter = null, uint? spareWcid = null)
        {
            Ground = GroundCatalog.Get(groundKey);
            if (alter is not null)
            {
                Ground = alter(Ground);
            }

            Vendor = new VendorRig(0); // the world the scripted ACE and the scripted shops share
            Shops = new ScriptedShop(Vendor);
            R = Vendor.R;
            Caster = new ScriptedCaster(R, 426, 275);
            if (spare)
            {
                Vendor.Arrive(SpareGuid, spareWcid is null ? "Spare Wand" : "Training Wand", spareWcid ?? WandLine.Wcid, CasterType, 50);
                Vendor.Put(SpareGuid);
            }

            View = new View(R);
            R.Server((p, s) => AceM3Writers.MagicUpdateSpell(p, s, (ushort)Ground.Portal!.RecallSpell));
            Caster.CastHook = spell =>
            {
                if (spell != Ground.Portal!.RecallSpell)
                {
                    return false;
                }

                Recalls++;
                At(1.0, () =>
                {
                    if (!Tied)
                    {
                        R.WeenieError(DeathHandler.UntiedRefusal);
                        R.UseDone();
                        return;
                    }

                    R.UseDone();
                    At(2.0, () => Land(ArrivalCell));
                });
                return true;
            };
            var lifestone = ReducerHarness.Create(0x80000700, ReducerHarness.Stamps(), ReducerHarness.Position(Ground.Home.LifestoneCell, 50, 30), name: "Lifestone");
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, lifestone with { Weenie = lifestone.Weenie with { Name = "Lifestone", ObjectDescriptionFlags = WorldObjectTable.LifestoneFlag } }, isUpdate: false);
            Land(CorpseCell); // the mage stands in the ground, where it will die
            Tally.Start(View);
            Travel = new RunTravel(this);
            Kit = GearKit.Of([
                new GearItem(GearKit.Wand.Wcid, "Test Wand", GearKind.Wand, 35, 0x8000, EquipMask.Held, 1, null, 50),
                .. Enumerable.Range(0, ScriptedCaster.PieceNames.Length).Select(i => new GearItem(GearKit.Armour[i].Wcid, ScriptedCaster.PieceNames[i], GearKind.Armour, 0, 0x2, GearKit.Armour[i].ValidLocations, 1, null, 100))]);
        }

        public HuntingGround Ground { get; }

        public VendorRig Vendor { get; }

        public ScriptedShop Shops { get; }

        /// <summary>The ground's kit wielded line (the profile's vendor wand).</summary>
        public KitLine WandLine => Ground.Supplies.Kit.Single(k => k.Slot == KitSlot.Wielded);

        /// <summary>True: <see cref="Handler"/> gives the handler the journey's context (the rearm can shop).</summary>
        public bool JourneyShop { get; set; }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public IGameSessionView View { get; }

        public DeathTally Tally { get; } = new();

        public RunTravel Travel { get; }

        public GearKit Kit { get; }

        public JourneyFacts Facts { get; } = new();

        public double Now => R.H.R.Clock.Now;

        public RecordingLogSink Log => R.H.Log;

        public uint ArrivalCell => Ground.ArrivalDestination().Location.Position.CellId;

        /// <summary>A walkable cell of the ground away from the arrival: where the mage dies and its corpse lies.</summary>
        public uint CorpseCell => Ground.Tour.Count > 1 ? Ground.Tour[^1] : ArrivalCell;

        /// <summary>False: ACE answers the recall with 0x04A3 (no portal tied).</summary>
        public bool Tied { get; set; } = true;

        /// <summary>True: the corpse is never created again for the client (the table forgot it: not found at its cell).</summary>
        public bool CorpseGone { get; set; }

        /// <summary>Seconds after the player's arrival at the corpse's cell that ACE creates the corpse for the client (a corpse that appears late).</summary>
        public double CorpseDelay { get; set; } = 0.2;

        /// <summary>Seconds ACE takes to answer the corpse's open.</summary>
        public double OpenDelay { get; set; } = 0.3;

        /// <summary>True: ACE answers the corpse's open with a refusal and no view.</summary>
        public bool RefuseOpen { get; set; }

        /// <summary>False: the rebuff ends with an item line down (its piece is on the corpse).</summary>
        public bool RebuffAllUp { get; set; }

        public int Recalls { get; private set; }

        public uint CorpseGuid { get; private set; }

        public List<uint> PickUps { get; } = [];

        public List<uint> Wields { get; } = [];

        /// <summary>The unwields of the spare the scripted ACE answered, in order.</summary>
        public List<uint> Unwields { get; } = [];

        public List<FightStages> InnerStages { get; } = [];

        public Action<FightStages, int>? OnInner { get; set; }

        /// <summary>Pick-ups (by guid) ACE never answers: the action times out (R6, M4 2.2 R2).</summary>
        public HashSet<uint> SilentItems { get; } = [];

        /// <summary>Seconds ACE takes to answer a pick-up with its 0x0022 (R6, M4 2.2 R3).</summary>
        public double PickUpDelay { get; set; } = 0.5;

        public StillWalk Walk { get; } = new();

        /// <summary>The scripted inner fight's factory (the rebuff, the item lines, the fight), shared by the handler and the death scenario of the tests.</summary>
        /// <summary>The walk each inner fight was given (M5 7 F23), in order.</summary>
        public List<IFightWalk?> InnerWalks { get; } = [];

        public FightLegFactory LegFactory => (_, _, _, fight, walk, _, _, _, stages) =>
        {
            InnerStages.Add(stages);
            InnerWalks.Add(walk);
            return new Leg(this, stages, fight, firstBuff: InnerStages.Count(s => s == FightStages.Buffs) == 1);
        };

        public DeathHandler Handler(DeathOptions? options = null) =>
            new(Ground, Tally, Travel, LegFactory, Walk, Kit, Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, options ?? new DeathOptions { Facts = Facts, Journey = JourneyShop ? Context() : null }, Log);

        /// <summary>The journey's context over this rig (the log, the travel seam, the spells, the ground and the facts).</summary>
        public JourneyContext Context() => new() { Character = "BSD Mage", Account = "test", Log = Log, Travel = Travel, Spells = Caster.Table, Dungeon = Ground, Facts = Facts };

        /// <summary>
        /// The home vendors of the profile at their places with their shops scripted from their <c>sells</c> (the wand's row an ItemType-Caster ware), and <paramref name="coins"/> pyreals in the purse: the rearm's kit trip shops here. Call before the death.
        /// </summary>
        public void StockVendors(int coins)
        {
            foreach (var v in Ground.Vendors)
            {
                Vendor.PlaceAt(v.Guid, v.Name, v.Cell, v.Point, 3f, v.Wcid);
                var shop = Shops.Add(v.Guid, v.BuysItemTypes);
                foreach (var ware in v.Sells)
                {
                    shop.Rows.Add((-1, ++_row, ware.Name, ware.Wcid, ware.Wcid == WandLine.Wcid ? CasterType : MiscType, ware.Price));
                }
            }

            Vendor.Coins(coins);
            JourneyShop = true;
        }

        /// <summary>The player stands at a cell and point (an own position update on this rig's sequences).</summary>
        public void StandAt(uint cell, Vector3 at) =>
            R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(WorldActionRig.Player, PositionFlags.Grounded, new AcPosition(cell, at, Quaternion.Identity), null, null, 1, ++_position, _teleport, 1));

        public void Frame(DeathHandler? handler, ref ScenarioStatus status, double dt = 0.25)
        {
            R.Tick(dt);
            Caster.Pump();
            Pump();
            Shops.Pump();
            R.Tick();
            if (handler is not null)
            {
                status = handler.Step(View, Now);
            }
        }

        /// <summary>Frames until the death's arrival plus ACE's vitals (the death loop's <c>respawn-vitals</c>).</summary>
        public void Settle(double seconds = 9)
        {
            var status = ScenarioStatus.Running;
            var end = Now + seconds;
            while (Now < end)
            {
                Frame(null, ref status);
            }
        }

        public ScenarioStatus Run(DeathHandler handler, double seconds = 600)
        {
            var status = ScenarioStatus.Running;
            var end = Now + seconds;
            while (status == ScenarioStatus.Running && Now < end)
            {
                Frame(handler, ref status);
            }

            return status;
        }

        /// <summary>
        /// ACE's death (<c>Player_Death.cs</c>): the victim notice, the Dead motion, health 0, NumDeaths; after the animation the wand and the first piece leave for the corpse (<c>TryDequipObjectWithNetworking(ToCorpseOnDeath)</c>: the client
        /// sees them deleted), the corpse in the cell the mage died in and, in the same tick, the teleport to the home lifestone; the client forgets the corpse there; the 75% vitals 3 s after the arrival.
        /// </summary>
        public void Die(string killer = "Tusker Slave")
        {
            R.Server((p, s) => AceM3Writers.VictimNotification(p, s, "You have been killed by " + killer + "."));
            var h = R.H.R;
            h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(WorldActionRig.Player, 1, ++_movement, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = 0x003D, Forward = WorldObjectTable.DeadMotionLowWord }));
            Vital(2, 0);
            R.PrivateInt(DeathTally.NumDeathsProperty, ++_numDeaths);
            At(3.0, () =>
            {
                var cell = R.H.State.Self.Position?.CellId ?? CorpseCell;
                foreach (var held in new uint[] { 0x80000200, 0x80000201 })
                {
                    if (R.H.State.Objects.TryGet(held, out var o) && R.H.State.Self.Inventory.TryGetEquipped(held, out _))
                    {
                        _items[held] = LootRulesTests.Item(o.Weenie.ItemType, 100, 50, held, o.Name, o.Weenie.WeenieClassId);
                        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(held, 1));
                    }
                }

                var corpse = ReducerHarness.Create(CorpseGuidBase + (uint)_numDeaths, ReducerHarness.Stamps(), ReducerHarness.Position(cell, 50, 30), name: "Corpse");
                _corpseCreate = corpse with { Weenie = corpse.Weenie with { Name = "Corpse of " + R.H.State.Self.Name, ObjectDescriptionFlags = WorldObjectTable.CorpseFlag } };
                CorpseGuid = corpse.Guid;
                _corpseCell = cell;
                h.Reducer.OnCreateObject(h.Context, _corpseCreate, isUpdate: false);
                Land(Ground.Home.LifestoneCell);
                h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(CorpseGuid, 1)); // far from the lifestone: the client forgets it
                At(3.0, () =>
                {
                    Vital(2, 400);
                    Vital(6, 300);
                });
            });
        }

        /// <summary>The player walks to a cell (a move, no teleport); at the corpse's cell ACE creates the corpse again for the client.</summary>
        public void WalkTo(uint cell)
        {
            R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, ReducerHarness.Move(WorldActionRig.Player, 1, ++_position, _teleport, 1, cell: cell, x: 50));
            if (!CorpseGone && _corpseCreate is { } create && _corpseCell == cell)
            {
                At(CorpseDelay, () => R.H.R.Reducer.OnCreateObject(R.H.R.Context, create, isUpdate: false));
            }
        }

        private void Land(uint cell)
        {
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(++_teleport));
            h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(WorldActionRig.Player, 1, ++_position, _teleport, 1, cell: cell, x: 50));
            R.H.Unhide(++_state);
        }

        public void SetHealth(uint points) => Vital(2, points);

        private void Vital(uint key, uint value)
        {
            _vitals[key]++;
            R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(_vitals[key], key, value));
        }

        private void At(double seconds, Action step) => _timeline.Add((Now + seconds, step));

        private void Pump()
        {
            var sends = R.H.Net.Sends;
            while (_seen < sends.Count)
            {
                var send = sends[_seen++];
                switch (send.Kind)
                {
                    case FakeSendKind.Use when send.Guid == CorpseGuid && CorpseGuid != 0 && RefuseOpen:
                        At(0.3, () =>
                        {
                            R.WeenieError(0x0036);
                            R.UseDone();
                        });
                        break;
                    case FakeSendKind.Use when send.Guid == CorpseGuid && CorpseGuid != 0:
                        At(OpenDelay, OpenCorpse);
                        break;
                    case FakeSendKind.PutItemInContainer when _items.ContainsKey(send.Guid):
                        PickUps.Add(send.Guid);
                        var item = send.Guid;
                        if (!SilentItems.Contains(item))
                        {
                            At(PickUpDelay, () => R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, item, WorldActionRig.Player, 0, 0)));
                        }

                        break;
                    case FakeSendKind.PutItemInContainer when send.Guid == SpareGuid:
                        // ACE's unwield (Player_Inventory.cs 400-405 and 1359 @95ef426d): 02DA Wielder 0, 02CE CurrentWieldedLocation 0, 02DA Container, then 0022 into the pack.
                        Unwields.Add(send.Guid);
                        At(0.5, () =>
                        {
                            R.Raw(AceM3Writers.PublicUpdateInstanceId(++_unwieldSeq, SpareGuid, 3, 0));
                            R.Raw(AceM3Writers.PublicUpdatePropertyInt(_unwieldSeq, SpareGuid, 10, 0));
                            R.Raw(AceM3Writers.PublicUpdateInstanceId(_unwieldSeq, SpareGuid, 2, WorldActionRig.Player));
                            R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, SpareGuid, WorldActionRig.Player, 0, 0));
                        });
                        break;
                    case FakeSendKind.NoLongerViewingContents:
                        var closing = send.Guid;
                        At(0.1, () => R.H.R.Reducer.OnCloseGroundContainer(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(closing)));
                        break;
                    case FakeSendKind.GetAndWieldItem:
                        Wields.Add(send.Guid);
                        var wielded = send.Guid;
                        var mask = unchecked((int)(uint)send.Amount);
                        At(0.5, () => R.Server((p, s) => AceM3Writers.WieldObject(p, s, wielded, mask)));
                        break;
                }
            }

            for (var i = 0; i < _timeline.Count;)
            {
                if (_timeline[i].Due <= Now)
                {
                    var step = _timeline[i].Do;
                    _timeline.RemoveAt(i);
                    step();
                }
                else
                {
                    i++;
                }
            }
        }

        /// <summary>ACE's open of a corpse: the 0196, the UseDone, then an F745 per item inside it.</summary>
        private void OpenCorpse()
        {
            var h = R.H.R;
            var guid = CorpseGuid;
            h.Reducer.OnViewContents(h.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.ViewContents), new ViewContentsEvent(guid, [.. _items.Values.Select(i => new ViewContentsEntry(i.Guid, 0))]));
            R.UseDone();
            At(0.1, () =>
            {
                foreach (var item in _items.Values)
                {
                    h.Reducer.OnCreateObject(h.Context, item.Created with { Weenie = item.Created.Weenie with { Header = item.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = guid } }, isUpdate: false);
                }
            });
        }
    }

    private static HandlerRig Died(string ground, Action<HandlerRig>? setup = null, bool spare = true, Func<HuntingGround, HuntingGround>? alter = null, uint? spareWcid = null)
    {
        var rig = new HandlerRig(ground, spare, alter, spareWcid);
        setup?.Invoke(rig);
        rig.Die();
        rig.Settle();
        return rig;
    }

    private static DeathHandler Begun(HandlerRig rig, DeathOptions? options = null, DeathFacts? facts = null)
    {
        var handler = rig.Handler(options);
        handler.Begin(rig.View, rig.Now, facts ?? DeathFacts.From(rig.Tally), respawned: true);
        return handler;
    }

    private static List<string> Steps(HandlerRig rig) =>
        [.. rig.Log.Entries.Select(e => e.Message).Where(m => m.StartsWith("handler ", StringComparison.Ordinal)).Select(m => m["handler ".Length..])];

    private static void NoModVerb(HandlerRig rig)
    {
        Assert.DoesNotContain(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.Talk && s.Text is { } t && (t.Contains("place", StringComparison.Ordinal) || t.Contains(" item ", StringComparison.Ordinal) || t.Contains(" xp ", StringComparison.Ordinal)));
    }

    private void Dump(HandlerRig rig, DeathHandler handler)
    {
        foreach (var entry in rig.Log.Entries)
        {
            output.WriteLine(entry.Message);
        }

        output.WriteLine("death handler " + handler.Fields);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void Tied_the_mage_gets_up_rebuffs_recalls_runs_to_its_corpse_takes_its_things_wears_them_recasts_the_item_lines_and_fights_on(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(handler.Failure);
        Assert.Equal(["respawn", "rearm", "rebuff", "heal", "topup", "route", "recall", "corpse-walk", "corpse-seek", "corpse-open", "corpse-loot", "corpse-close", "wear", "relines", "fight", "done"], Steps(rig)); // the wand went to the corpse: the pack's spare is wielded first (M5 3 F5, D53)
        Assert.Equal(DeathRoute.Recall, handler.Route);
        Assert.Equal(1, rig.Recalls);
        Assert.Equal(1, rig.Facts.Recalls);
        Assert.Equal(TravelPurpose.Corpse, Assert.Single(rig.Travel.Requests).Purpose); // inside the ground the runner, never a place
        Assert.Equal(rig.CorpseGuid, rig.Travel.Requests[0].ObjectGuid);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("walking: purpose=corpse (", StringComparison.Ordinal));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Equal(2, handler.ItemsBack);
        Assert.Equal([0x80000200u, 0x80000201u, HandlerRig.SpareGuid], rig.Wields.Order()); // the spare (the rearm), then the kit's wand wielded and the piece worn again from the pack
        Assert.True(handler.GearBack);
        Assert.Equal("gear=wielded worn=5", handler.WornFields);
        Assert.Equal(DeathLegVerdict.RebuffedYes, handler.Rebuffed); // D56: every line is up once the item lines are re-cast
        Assert.Equal(DeathLegVerdict.RebuffedNo, handler.RebuffedPretrip); // the piece's line missed while the piece lay on the corpse
        Assert.Equal(2, handler.Relines);
        Assert.Equal([FightStages.Buffs, FightStages.Buffs, FightStages.Mode], rig.InnerStages); // the rebuff, the item lines, the fight (no place stage: the mage stands in the ground)
        Assert.Equal(4, handler.Kills);
        Assert.Equal(1, rig.Walk.Prepared);
        Assert.Equal("deaths=1 corpse_recovered=yes route=recall items_back=2 vitae_grants=0 rebuffed=yes rebuffed_pretrip=no relines=2 rearm=pack", handler.Fields);
        NoModVerb(rig);
    }

    // ------------------------------------------------------------------------------------------------ rebuffed after the item lines (M5 4 a3, D56)

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_4_a3_D56_rebuffed_is_read_after_the_item_lines_are_re_cast_and_the_value_before_the_trip_is_kept_apart(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(handler));

        Assert.Equal((DeathLegVerdict.RebuffedYes, DeathLegVerdict.RebuffedNo, 2), (handler.Rebuffed, handler.RebuffedPretrip, handler.Relines)); // the rebuff left the item lines down; two re-casts later every line is up
        Assert.Contains(" rebuffed=yes rebuffed_pretrip=no relines=2 ", handler.Fields, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("the item lines re-cast after the items came back: relines=2", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_4_a3_D56_a_rebuff_with_every_line_up_reads_yes_before_and_after(string ground)
    {
        var rig = Died(ground, r =>
        {
            r.Facts.SetPortalTie(ground, PortalTie.Tied);
            r.RebuffAllUp = true;
        });
        var handler = Begun(rig);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(handler));

        Assert.Equal((DeathLegVerdict.RebuffedYes, DeathLegVerdict.RebuffedYes), (handler.Rebuffed, handler.RebuffedPretrip));
    }

    // ------------------------------------------------------------------------------------------------ the entry after a respawn (M5 4 a3, m5-autopilot-death-unscripted)

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_4_a3_a_death_the_autopilot_did_not_script_is_taken_by_the_death_station_at_the_respawn_with_the_observed_tally_and_the_corpse_comes_back(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        rig.Facts.ObservedDeath = rig.Tally; // the autopilot's tally, running since its start, saw this death
        var options = new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false, Fight = false, Travel = rig.Travel, Facts = rig.Facts };
        using var scenario = new DeathScenario(rig.Kit, rig.Caster.Table, options, rig.Walk, rig.Log, endSession: false, stages: DeathStages.Death | DeathStages.Rebuff | DeathStages.Corpse, fightFactory: rig.LegFactory, fight: new FightOptions { Kills = 4, Ground = ground });

        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            rig.Frame(null, ref status);
            status = scenario.Step(rig.View, rig.Now);
        }

        foreach (var entry in rig.Log.Entries)
        {
            output.WriteLine(entry.Message);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Null(rig.Facts.ObservedDeath); // taken
        Assert.Same(rig.Tally, scenario.Tally); // the observed tally replaced the scenario's own
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("entered after a death the autopilot did not script", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("undress", StringComparison.OrdinalIgnoreCase) || e.Message.StartsWith("lie", StringComparison.OrdinalIgnoreCase)); // no walk, no undress, no lie-down
        Assert.NotNull(scenario.Handler);
        Assert.Equal(DeathCorpse.Yes, scenario.Handler!.Corpse);
        Assert.Equal(rig.CorpseGuid, scenario.Handler.Facts.CorpseGuid); // the corpse's facts came from the observed tally
        Assert.Contains("deaths=1 corpse_recovered=yes route=recall", scenario.HandlerFields, StringComparison.Ordinal);
        Assert.Contains("rebuffed=yes rebuffed_pretrip=no relines=2", scenario.HandlerFields, StringComparison.Ordinal);
        Assert.Contains("rebuffed=yes rebuffed_pretrip=no", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("stage_rebuffed=no", scenario.VerdictFields(), StringComparison.Ordinal); // the respawn's own measure keeps the rebuff's value before the trip (3-R2)
        NoModVerb(rig);
    }

    // ------------------------------------------------------------------------------------------------ the rearm (M5 3 F5, D53)

    private static int IndexOf(HandlerRig rig, string part) => rig.Log.Entries.Select(e => e.Message).ToList().FindIndex(m => m.Contains(part, StringComparison.Ordinal));

    private static ModVerbCounts Verbs(HandlerRig rig) => rig.R.Api.Tools.Verbs.Counts;

    private static Func<HuntingGround, HuntingGround> WithoutTheWandOnSale => g =>
    {
        var wand = g.Supplies.Kit.Single(k => k.Slot == KitSlot.Wielded).Wcid;
        return g with { Vendors = [.. g.Vendors.Select(v => v with { Sells = [.. v.Sells.Where(w => w.Wcid != wand)] })] };
    };

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F5_4a_a_caster_in_the_pack_is_wielded_after_the_respawn_with_no_walk_and_no_buy(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied)); // the wielded wand went to the corpse with the death; the pack holds a spare
        Assert.Null(CombatModeGuard.Read(rig.R.H.State).Caster);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRearm.Pack, handler.Rearm);
        Assert.Equal(["respawn", "rearm", "rebuff"], Steps(rig).Take(3));
        Assert.Equal(HandlerRig.SpareGuid, rig.Wields[0]); // the handler's first act: the spare in the held slot
        Assert.Equal((0, 0), (rig.Shops.Uses, rig.Shops.Buys)); // no vendor visit
        Assert.Equal(TravelPurpose.Corpse, Assert.Single(rig.Travel.Requests).Purpose); // no walk but the corpse trip's
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("walking: purpose=vendor", StringComparison.Ordinal));
        Assert.True(IndexOf(rig, "handler: rearm=pack") is var armed and > 0 && armed < IndexOf(rig, "handler rebuff") && IndexOf(rig, "handler rebuff") < IndexOf(rig, "handler recall"));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("the order (D51, D53): respawn, rearm, rebuff, the corpse trip, the items back, the item lines, the fight", StringComparison.Ordinal));
        Assert.Equal(1, rig.Recalls);
        Assert.EndsWith(" relines=2 rearm=pack", handler.Fields, StringComparison.Ordinal);
        Assert.Equal((0, 0, 0), (Verbs(rig).Item, Verbs(rig).Xp, Verbs(rig).Granted)); // the verb counter: no item, no xp
        NoModVerb(rig);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void A_rearm_caster_of_another_weenie_goes_to_the_pack_before_the_kits_wand_is_wielded_again(string ground)
    {
        // M5 3 gate round 3: the rearm wielded the new character's Training Wand, ACE refused the kit's wand into the taken held slot, and the items back read gear=missing.
        const uint OtherCaster = 12748; // a caster that is not the kit's wand line
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied), spareWcid: OtherCaster);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRearm.Pack, handler.Rearm);
        Assert.Equal([HandlerRig.SpareGuid], rig.Unwields);
        var sends = rig.R.H.Net.Sends;
        var unwield = sends.FindIndex(s => s.Kind == FakeSendKind.PutItemInContainer && s.Guid == HandlerRig.SpareGuid);
        var rewield = sends.FindLastIndex(s => s.Kind == FakeSendKind.GetAndWieldItem);
        Assert.True(unwield >= 0 && unwield < rewield, $"the unwield (send {unwield}) comes before the kit wand's wield (send {rewield})");
        Assert.NotEqual(HandlerRig.SpareGuid, rig.Wields[^1]); // the kit's wand, back from the corpse
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("holds the wand's slot", StringComparison.Ordinal));
        Assert.True(handler.GearBack, handler.Fields);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F5_4b_with_no_caster_in_the_pack_one_vendor_visit_buys_one_wand_wields_it_and_the_rebuff_and_the_recall_run(string ground)
    {
        const int Purse = 1_000_000;
        var rig = Died(ground, r =>
        {
            r.Facts.SetPortalTie(ground, PortalTie.Tied);
            r.StockVendors(Purse);
        }, spare: false);
        var line = rig.WandLine;
        var vendor = rig.Ground.VendorSelling(line.Wcid)!;
        var price = rig.Ground.PriceOf(line.Wcid)!.Value;
        Assert.Null(CombatModeGuard.Read(rig.R.H.State).Caster);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRearm.Bought, handler.Rearm);
        Assert.Equal(["respawn", "rearm", "rebuff"], Steps(rig).Take(3));
        Assert.Equal((1, 1), (rig.Shops.Uses, rig.Shops.Buys)); // one vendor visit, one buy
        var buy = Assert.Single(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.Buy);
        Assert.Equal(vendor.Guid, buy.Guid);
        Assert.Equal(1u, Assert.Single(buy.Items!).amount); // one wand
        Assert.Equal([TravelPurpose.Vendor, TravelPurpose.Corpse], rig.Travel.Requests.Select(r => r.Purpose)); // the walk to the vendor, then the corpse trip
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains($"walking: purpose=vendor:{vendor.Name} (1 lines to buy)", StringComparison.Ordinal));
        Assert.Equal(Purse - price, (int)rig.R.H.State.Self.Qualities.CoinValue); // the purse paid the profile's price
        Assert.True(rig.R.H.State.Objects.TryGet(rig.Wields[0], out var wand) && wand.Weenie.WeenieClassId == line.Wcid && (wand.ItemType & CasterType) != 0, "the first wield is the bought kit wand");
        Assert.Equal(1, rig.Recalls);
        Assert.True(IndexOf(rig, "handler: rearm=bought") is var armed and > 0 && armed < IndexOf(rig, "handler rebuff") && IndexOf(rig, "handler rebuff") < IndexOf(rig, "handler recall"));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse); // the original wand comes back on the corpse and is carried as a spare
        Assert.EndsWith(" relines=2 rearm=bought", handler.Fields, StringComparison.Ordinal);
        Assert.Equal((0, 0, 0), (Verbs(rig).Item, Verbs(rig).Xp, Verbs(rig).Granted));
        NoModVerb(rig);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F5_4c_no_vendor_sells_the_kit_wand_a_named_failure_with_no_walk_and_no_grant(string ground)
    {
        var rig = Died(ground, r => r.StockVendors(1_000_000), spare: false, alter: WithoutTheWandOnSale);
        Assert.Null(rig.Ground.VendorSelling(rig.WandLine.Wcid));
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(DeathRearm.Failed, handler.Rearm);
        Assert.Contains($"rearm=failed: no caster is wielded or held, and no vendor of {rig.Ground.Home.Town} sells the kit line {rig.WandLine.Name} (weenie {rig.WandLine.Wcid})", handler.Failure, StringComparison.Ordinal);
        Assert.Equal(["respawn", "rearm"], Steps(rig));
        Assert.Empty(rig.Travel.Requests);
        Assert.Empty(rig.Wields);
        Assert.Equal((0, 0), (rig.Shops.Uses, rig.Shops.Buys));
        Assert.Contains(" rearm=failed", handler.Fields, StringComparison.Ordinal);
        Assert.Equal(0, Verbs(rig).Granted);
        NoModVerb(rig);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F5_4c_a_host_with_no_journey_context_cannot_shop_and_names_the_standalone_todo(string ground)
    {
        var rig = Died(ground, spare: false); // the standalone death leg: no journey context, no caster in the pack
        var handler = Begun(rig);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(handler));

        Assert.Equal(DeathRearm.Failed, handler.Rearm);
        Assert.Contains("TODO(m5-death-rearm-standalone)", handler.Failure, StringComparison.Ordinal);
        Assert.Contains("rearm=failed", handler.Failure, StringComparison.Ordinal);
        Assert.Empty(rig.Travel.Requests);
        Assert.Equal(0, Verbs(rig).Granted);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F5_4c_a_purse_that_cannot_pay_buys_nothing_and_the_handler_fails_by_name(string ground)
    {
        var rig = Died(ground, r => r.StockVendors(0), spare: false); // no coin and no note
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(DeathRearm.Failed, handler.Rearm);
        Assert.Contains($"rearm=failed: the {rig.WandLine.Name} was not bought (bought=0 vendors=1 not_found=0 short=1", handler.Failure, StringComparison.Ordinal);
        Assert.Equal(0, rig.Shops.Buys);
        Assert.Empty(rig.Wields);
        Assert.Equal(0, Verbs(rig).Granted);
        NoModVerb(rig);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void Untied_the_recall_is_refused_with_0x04A3_the_route_switches_to_the_runner_and_its_missing_leg_is_named(string ground)
    {
        var rig = Died(ground, r => r.Tied = false);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(DeathRoute.Runner, handler.Route);
        Assert.Contains("TODO(m6-first-tie-walk)", handler.Failure, StringComparison.Ordinal);
        Assert.Contains("route=runner", handler.Failure, StringComparison.Ordinal);
        Assert.Equal(PortalTie.Untied, rig.Facts.PortalTieOf(ground)); // the answer taught the journey its tie
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("0x04A3", StringComparison.Ordinal));
        Assert.Empty(rig.Travel.Requests);
        Assert.Contains(" vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
        NoModVerb(rig);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void A_known_untied_portal_plans_the_runner_without_casting(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Untied));
        var handler = Begun(rig);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(handler));

        Assert.Equal(0, rig.Recalls);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("route=runner (the journey's tie fact says the portal is not tied)", StringComparison.Ordinal));
        Assert.Contains("TODO(m6-first-tie-walk)", handler.Failure, StringComparison.Ordinal);
    }

    /// <summary>M5 4 fix F2 (4-R3): the corpse's open (and the rest of the trip's sends) go as Automation, so the kill switch withdraws the queued open and the handler gets nothing from the corpse.</summary>
    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_4_f2_the_kill_switch_withdraws_a_queued_corpse_open_and_nothing_more_goes_to_the_corpse(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        var status = ScenarioStatus.Running;
        var api = rig.R.Api;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && !(Steps(rig).Contains("corpse-open") && api.QueuedCount > 0); i++)
        {
            rig.Frame(handler, ref status);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(1, api.QueuedCount);
        Assert.DoesNotContain(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.Use && s.Guid == rig.CorpseGuid); // queued, not sent
        var log = rig.View.ActionLog;

        api.KillSwitch = true; // the person throws it
        Assert.Equal(0, api.QueuedCount);
        var withdrawn = Assert.Single(Enumerable.Range(0, log.Count).Select(i => log[i]), e => e.Kind == ActionKind.OpenContainer);
        Assert.Equal((ActionSource.Automation, ActionOutcomeKind.Cancelled), (withdrawn.Source, withdrawn.Outcome.Kind));

        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            rig.Frame(handler, ref status);
        }

        Dump(rig, handler);
        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.DoesNotContain(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.Use && s.Guid == rig.CorpseGuid); // the open never went out
        Assert.Empty(rig.PickUps);
        Assert.NotEqual(DeathCorpse.Yes, handler.Corpse);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void A_rotted_corpse_is_named_and_the_fight_resumes_without_it(string ground)
    {
        var rig = Died(ground);
        var facts = DeathFacts.From(rig.Tally) with { DiedAt = rig.Now - DeathScenario.PlayerCorpseMinRotSeconds - 1 };
        var handler = Begun(rig, facts: facts);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathCorpse.Rotted, handler.Corpse);
        Assert.Equal("rotted", handler.TripAbort);
        Assert.Empty(rig.Travel.Requests);
        Assert.Empty(rig.PickUps);
        Assert.Equal(4, handler.Kills);
        Assert.False(handler.GearBack); // the wand and the piece stayed on the corpse: nothing spawns them
        Assert.Contains(" corpse_recovered=rotted route=recall items_back=0 vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void A_corpse_not_found_at_its_cell_is_named_and_the_fight_resumes_without_it(string ground)
    {
        var rig = Died(ground, r => r.CorpseGone = true);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathCorpse.NotFound, handler.Corpse);
        Assert.Equal("corpse-not-found", handler.TripAbort);
        Assert.Single(rig.Travel.Requests);
        Assert.Equal(4, handler.Kills);
        Assert.Contains(" corpse_recovered=not-found ", handler.Fields, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void A_second_death_before_the_trip_restarts_the_handler_for_the_newer_corpse_and_names_the_older(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var first = rig.CorpseGuid;
        var handler = Begun(rig);
        var killed = false;
        rig.OnInner = (stages, step) =>
        {
            if (!killed && stages == FightStages.Buffs && step == 2)
            {
                killed = true;
                rig.Die("Tusker Guard"); // an avoided creature reaches the mage: the handler does not ask who killed
            }
        };

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, handler.Deaths);
        Assert.Equal([first], handler.OlderCorpses);
        Assert.NotEqual(first, handler.Facts.CorpseGuid);
        Assert.Equal(rig.CorpseGuid, handler.Facts.CorpseGuid);
        Assert.Equal(rig.CorpseGuid, Assert.Single(rig.Travel.Requests).ObjectGuid); // the trip went for the newer corpse
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("TODO(m5-death-older-corpse)", StringComparison.Ordinal));
        Assert.Contains("second-death", Steps(rig));
        Assert.StartsWith("deaths=2 corpse_recovered=", handler.Fields, StringComparison.Ordinal);
        Assert.Contains(" vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
        NoModVerb(rig);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void A_death_on_the_trip_restarts_the_handler_and_the_newer_corpse_is_gone_back_for(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var first = rig.CorpseGuid;
        var handler = Begun(rig);
        var killed = false;

        var status = ScenarioStatus.Running;
        var end = rig.Now + 600;
        while (status == ScenarioStatus.Running && rig.Now < end)
        {
            rig.Frame(handler, ref status);
            if (!killed && handler.CurrentStep == "corpse-seek")
            {
                killed = true;
                rig.Die();
            }
        }

        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, handler.Deaths);
        Assert.Equal([first], handler.OlderCorpses);
        Assert.Equal(2, rig.Travel.Requests.Count);
        Assert.Equal(rig.CorpseGuid, rig.Travel.Requests[1].ObjectGuid);
        Assert.Equal(2, rig.Recalls); // the route again from the lifestone
        Assert.Contains(" vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_7_F19_a_third_death_in_one_handling_stops_by_name_and_a_second_restarts_cleanly_from_the_respawn(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        var deaths = 0;
        var status = ScenarioStatus.Running;
        var end = rig.Now + 900;
        while (status == ScenarioStatus.Running && rig.Now < end)
        {
            rig.Frame(handler, ref status);
            if (deaths < 2 && handler.CurrentStep == "corpse-seek" && handler.Deaths == deaths + 1)
            {
                deaths++;
                rig.Die();
            }
        }

        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(DeathHandler.MaxDeathsInCycle, handler.Deaths);
        Assert.StartsWith(DeathHandler.ThirdDeathStop + ":", handler.Failure, StringComparison.Ordinal);
        Assert.Equal(1, Steps(rig).Count(m => m == "second-death")); // the second death restarted once from the respawn; the third stopped
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("no arrival within", StringComparison.Ordinal)); // no 60 s watchdog
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_7_F19_the_corpse_trip_fights_a_tusker_near_its_path_before_it_goes_on_and_never_loots_with_one_in_reach(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        var corpse = handler.Facts.CorpsePosition!.Value;
        const uint tusker = 0x80000777;
        var h = rig.R.H.R;
        var basis = ReducerHarness.Create(tusker, ReducerHarness.Stamps(), ReducerHarness.Position(corpse.CellId, corpse.Frame.Origin.X + 3, corpse.Frame.Origin.Y, corpse.Frame.Origin.Z), name: "Tusker Slave");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = rig.Ground.TargetWcids()[0] } }, isUpdate: false);
        var killed = false;
        rig.OnInner = (stages, step) =>
        {
            if (!killed && stages == FightStages.Mode && handler.CurrentStep == "trip-fight" && step >= 2)
            {
                killed = true;
                h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(tusker, 1)); // the trip fight's kill
            }
        };
        var lootedWithOneInReach = false;
        var status = ScenarioStatus.Running;
        var end = rig.Now + 900;
        while (status == ScenarioStatus.Running && rig.Now < end)
        {
            rig.Frame(handler, ref status);
            lootedWithOneInReach |= handler.CurrentStep == "corpse-loot" && rig.R.H.State.Objects.TryGet(tusker, out _);
        }

        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(killed, "the tusker by the corpse was fought");
        Assert.True(handler.TripFights >= 1);
        Assert.False(lootedWithOneInReach);
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("trip-fight") >= 0 && steps.IndexOf("trip-fight") < steps.IndexOf("corpse-loot"), string.Join(",", steps));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: Tusker Slave 0x80000777 is ", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_7_F23_the_corpse_trip_fight_is_given_the_ground_walk_so_a_bolt_that_hits_a_wall_walks_and_the_handler_keeps_its_walk(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        var corpse = handler.Facts.CorpsePosition!.Value;
        const uint tusker = 0x80000778;
        var h = rig.R.H.R;
        var basis = ReducerHarness.Create(tusker, ReducerHarness.Stamps(), ReducerHarness.Position(corpse.CellId, corpse.Frame.Origin.X + 3, corpse.Frame.Origin.Y, corpse.Frame.Origin.Z), name: "Tusker Slave");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = rig.Ground.TargetWcids()[0] } }, isUpdate: false);
        IFightWalk? tripWalk = null;
        var walkedAfterWall = false;
        rig.OnInner = (stages, step) =>
        {
            if (stages != FightStages.Mode || handler.CurrentStep != "trip-fight" || tripWalk is not null)
            {
                return;
            }

            tripWalk = rig.InnerWalks[^1];
            walkedAfterWall = tripWalk is not null && tripWalk.BeginLeg(rig.View, rig.Now, out _); // the inner fight's StartWalk after a bolt hit a wall: it has a walker now
            h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(tusker, 1)); // the trip fight's kill
        };
        var status = ScenarioStatus.Running;
        var end = rig.Now + 900;
        while (status == ScenarioStatus.Running && rig.Now < end)
        {
            rig.Frame(handler, ref status);
        }

        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.NotNull(tripWalk);
        Assert.True(walkedAfterWall, "the trip fight's walker plans a leg (no TODO(m3-fight-no-walker) failure)");
        Assert.Equal(1, rig.Walk.Prepared); // prepared once, by the trip fight (the handler's own fight finds it prepared)
        var abandons = rig.Walk.Abandons;
        tripWalk!.Dispose(); // what the real inner fight does at its end
        Assert.Equal(0, rig.Walk.Disposed); // the inner fight's dispose only drops its leg: the handler's walk lives on for its own fight
        Assert.Equal(abandons + 1, rig.Walk.Abandons);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: the trip fight walks with the ground's walk", StringComparison.Ordinal));
    }

    [Fact]
    public void With_todays_place_seam_the_handler_never_places_and_names_the_missing_runner()
    {
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var handler = new DeathHandler(rig.Ground, rig.Tally, new PlaceTravel(), (_, _, _, fight, _, _, _, _, stages) => new Leg(rig, stages, fight, firstBuff: false), rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 2 }, new DeathOptions { Facts = rig.Facts }, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(handler));

        Assert.Equal(DeathCorpse.No, handler.Corpse);
        Assert.Equal("no-runner", handler.TripAbort);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("TODO(m5-death-corpse-runner)", StringComparison.Ordinal));
        NoModVerb(rig);
    }

    [Fact]
    public void Every_handler_field_reads_n_a_before_a_handler_ran_vitae_grants_included_never_a_constant_0_M5_3_F1_3_R1()
    {
        Assert.Equal("deaths=n/a corpse_recovered=n/a route=n/a items_back=n/a vitae_grants=n/a rebuffed=n/a rebuffed_pretrip=n/a relines=n/a rearm=n/a", DeathHandler.UnmeasuredFields);
        Assert.Equal(-1, new HandlerRig("black-spawn-den-aluvian").Handler().VitaeGrants); // a handler that never began has counted nothing: n/a, not 0
        Assert.Equal("handler-", DeathScenario.HandlerStagePrefix);
        _ = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void m4_death_trip_heal_resume_a_heal_skipped_for_mana_resumes_once_the_mana_regenerates_during_the_wait()
    {
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        rig.Caster.Table[DeathScenario.HealSelfVii] = WorldActionRig.Spell(DeathScenario.HealSelfVii, DatReaderWriter.Enums.SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Heal Self VII", BaseMana = 60 };
        rig.R.Spells(rig.Caster.Table[DeathScenario.HealSelfVii]);
        var recallHook = rig.Caster.CastHook;
        var healCasts = 0;
        rig.Caster.CastHook = spell =>
        {
            if (spell != DeathScenario.HealSelfVii)
            {
                return recallHook!(spell);
            }

            healCasts++;
            rig.R.UseDone(); // the heal is a cast the scripted caster has no profile line for: the test answers it
            return true;
        };
        rig.SetHealth(100); // far under the minimum
        rig.Caster.SetVitals(mana: 2, stamina: 275); // 60 mana needed, 2 held, 2 a second regenerated
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, RecoverMinHealthPercent = 99 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("heal skipped for mana", StringComparison.Ordinal)); // the cast waited, it was not dropped
        Assert.True(healCasts >= 1 && handler.Heals >= 1, "the heal resumed once the mana reached the spell's cost"); // m4-death-trip-heal-resume
        Assert.Equal(DeathCorpse.Yes, handler.Corpse); // and the trip went on after the heal
    }

    private const uint RecallCost = 100;

    /// <summary>The recall spell in the scripted table at <see cref="RecallCost"/> mana, and the hook ACE's check: a cast with less mana than that is refused 0x0401 (the live gate's refusal); one with enough goes on to the rig's own recall.</summary>
    private static (Func<int> Refusals, Func<int> Casts) CostRecall(HandlerRig rig)
    {
        var id = rig.Ground.Portal!.RecallSpell;
        rig.Caster.Table[id] = WorldActionRig.Spell(id, DatReaderWriter.Enums.SpellType.PortalLink, SpellFlags.Beneficial) with { Name = "Primary Portal Recall", BaseMana = RecallCost };
        rig.R.Spells(rig.Caster.Table[id]);
        var inner = rig.Caster.CastHook!;
        int refusals = 0, casts = 0;
        rig.Caster.CastHook = spell =>
        {
            if (spell != id)
            {
                return inner(spell);
            }

            casts++;
            if (rig.Caster.Mana < RecallCost)
            {
                refusals++;
                rig.R.UseDone(0x0401);
                return true;
            }

            return inner(spell);
        };
        return (() => refusals, () => casts);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_4_G1_the_corpse_recall_waits_for_the_mana_its_cast_needs_and_is_never_refused_0x0401_on_both_dungeons(string ground)
    {
        // The live gate: the rebuff spent the respawned mage's mana (1 of 415), the recall went out at once and ACE refused it 0x0401, which failed the station (ERROR). Now the route waits, named, and casts when the mana is there.
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var (refusals, casts) = CostRecall(rig);
        rig.Caster.SetVitals(mana: 1, stamina: 275);
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(0, refusals());
        Assert.Equal(1, casts());
        Assert.Equal(1, rig.Facts.Recalls);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Contains(rig.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Message.StartsWith("handler: the recall waits for mana (", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: the recall's mana is here after ", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Severity >= Lifestone.Client.Diagnostics.LogSeverity.Error);
        Assert.Equal(1, Steps(rig).Count(m => m == "route")); // the wait is the route step, not a new one: the step order is unchanged
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_4_G1_a_recall_whose_mana_never_comes_goes_out_after_the_bound_and_fails_by_the_existing_name(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var (refusals, casts) = CostRecall(rig);
        rig.Caster.SetVitals(mana: 1, stamina: 275);
        rig.Caster.ManaRegenPerSecond = 0;
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts });

        var status = rig.Run(handler, seconds: 2 * DeathHandler.RecallManaSeconds);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("recall: recall:cast", handler.Failure, StringComparison.Ordinal);
        Assert.Equal(1, casts());
        Assert.Equal(1, refusals()); // after RecallManaSeconds, once
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: the recall's mana did not come in 180 s", StringComparison.Ordinal));
    }

    public static TheoryData<bool, bool, bool> OptionSets => new()
    {
        { true, true, true },
        { true, true, false }, // --death-fight off
        { true, false, true }, // --death-recover-corpse off
        { false, true, true }, // --death-rebuff off
        { false, false, false },
    };

    [Theory]
    [MemberData(nameof(OptionSets))]
    public void No_item_xp_or_place_verb_is_sent_from_the_respawn_to_the_end_on_every_option_set_M5_3_d2(bool rebuff, bool recover, bool fight)
    {
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var talksBefore = rig.R.H.Net.Sends.Count(s => s.Kind == FakeSendKind.Talk);
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, Rebuff = rebuff, RecoverCorpse = recover, Fight = fight });

        var status = rig.Run(handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(talksBefore, rig.R.H.Net.Sends.Count(s => s.Kind == FakeSendKind.Talk)); // every PlayerTools verb is a Talk send: none from the respawn to the end
        NoModVerb(rig);
        Assert.Equal(0, handler.VitaeGrants);
        Assert.Contains(" vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
        Assert.Equal(fight ? 4 : 0, handler.Kills);
    }

    // ------------------------------------------------------------------------------------------------ the trip's aborts (M5 3 d4)

    private static void HealSpell(HandlerRig rig, Action onCast) => HealSpell(rig, () =>
    {
        onCast();
        return 0u;
    });

    /// <summary>The scripted heal: <paramref name="answer"/> runs on each cast and returns the UseDone code ACE answers it with (0 = the cast went out; 0x0402 = a fizzle; 0x001D = busy).</summary>
    private static void HealSpell(HandlerRig rig, Func<uint> answer)
    {
        rig.Caster.Table[DeathScenario.HealSelfVii] = WorldActionRig.Spell(DeathScenario.HealSelfVii, DatReaderWriter.Enums.SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Heal Self VII", BaseMana = 10 };
        rig.R.Spells(rig.Caster.Table[DeathScenario.HealSelfVii]);
        var recallHook = rig.Caster.CastHook;
        rig.Caster.CastHook = spell =>
        {
            if (spell != DeathScenario.HealSelfVii)
            {
                return recallHook!(spell);
            }

            rig.R.UseDone(answer());
            return true;
        };
    }

    /// <summary>Frames the handler to its end; <paramref name="perFrame"/> runs after each frame (the scripted ACE's world changing under it).</summary>
    private static ScenarioStatus RunWith(HandlerRig rig, DeathHandler handler, Action perFrame, double seconds = 600)
    {
        var status = ScenarioStatus.Running;
        var end = rig.Now + seconds;
        while (status == ScenarioStatus.Running && rig.Now < end)
        {
            rig.Frame(handler, ref status);
            perFrame();
        }

        return status;
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_d4_a_health_share_under_the_line_on_the_way_holds_the_trip_pauses_the_runner_heals_and_goes_on(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var casts = 0;
        HealSpell(rig, () =>
        {
            casts++;
            rig.SetHealth(170); // the heal brings the share back to the maximum
        });
        var handler = Begun(rig);
        var hit = false;

        var status = RunWith(rig, handler, () =>
        {
            if (!hit && handler.CurrentStep == "corpse-walk")
            {
                hit = true;
                rig.SetHealth(50); // a creature's blow: 50 of the maximum 170, about 29%, under the line of 40
            }
        });
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("corpse-walk") < steps.IndexOf("trip-heal") && steps.IndexOf("trip-heal") < steps.IndexOf("corpse-seek"), string.Join(",", steps));
        Assert.Equal(1, casts);
        Assert.Equal(1, handler.TripHeals);
        Assert.Equal(0, handler.Heals); // the pre-trip heal had nothing to do
        Assert.Equal((1, 1), (rig.Travel.Pauses, rig.Travel.Resumes)); // the runner paused where it stood, then resumed
        Assert.Equal(DeathLegVerdict.TripAbortNone, handler.TripAbort); // a hold that heals is not an abort
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Equal(2, handler.ItemsBack);
        Assert.Equal(4, handler.Kills);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("the trip holds here and heals", StringComparison.Ordinal));
        NoModVerb(rig);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_d4_a_hold_whose_heals_cannot_bring_the_share_back_turns_back_named_health_under_40_and_the_fight_resumes(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var casts = 0;
        HealSpell(rig, () => casts++); // the casts go out and the health stays where it was
        var handler = Begun(rig);
        var hit = false;

        var status = RunWith(rig, handler, () =>
        {
            if (!hit && handler.CurrentStep == "corpse-walk")
            {
                hit = true;
                rig.SetHealth(50);
            }
        });
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("health-under-40", handler.TripAbort);
        Assert.Equal(DeathCorpse.No, handler.Corpse);
        Assert.Equal(DeathScenario.PreTripMaxHeals, casts); // four heals and then the regeneration, as the pre-trip heal
        Assert.Equal(DeathScenario.PreTripMaxHeals, handler.TripHeals);
        Assert.Equal((1, 0, 1), (rig.Travel.Pauses, rig.Travel.Resumes, rig.Travel.Disposed)); // paused, never resumed, stopped
        Assert.Empty(rig.PickUps);
        Assert.Equal(4, handler.Kills); // the fight resumes without the corpse
        Assert.DoesNotContain("corpse-seek", Steps(rig));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("trip aborted (health-under-40) during corpse-walk", StringComparison.Ordinal));
        Assert.Contains(" corpse_recovered=no ", handler.Fields, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_d4_an_abort_health_of_zero_never_holds_or_turns_back_for_health(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, AbortHealthPercent = 0 });
        var hit = false;

        var status = RunWith(rig, handler, () =>
        {
            if (!hit && handler.CurrentStep == "corpse-walk")
            {
                hit = true;
                rig.SetHealth(5);
            }
        });

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.DoesNotContain("trip-heal", Steps(rig));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Equal(DeathLegVerdict.TripAbortNone, handler.TripAbort);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_d4_a_trip_over_its_time_from_the_arrival_in_the_ground_is_given_up_named_trip_over_Ns_and_the_fight_resumes(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        rig.Travel.Steps = 100_000; // the runner never reaches the corpse
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, AbortSeconds = 6 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("trip-over-6s", handler.TripAbort);
        Assert.Equal(DeathCorpse.No, handler.Corpse);
        Assert.Equal(1, rig.Travel.Disposed); // the runner stopped
        Assert.Equal(4, handler.Kills);
        Assert.Empty(rig.PickUps);
        Assert.InRange(handler.TripSeconds ?? 0, 6, 20); // counted from the arrival in the ground, the recall's seconds not among the six
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("trip aborted (trip-over-6s) during corpse-walk", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_d4_a_refused_open_is_named_open_refused_nothing_is_taken_and_the_fight_resumes(string ground)
    {
        var rig = Died(ground, r =>
        {
            r.Facts.SetPortalTie(ground, PortalTie.Tied);
            r.RefuseOpen = true;
        });
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("open-refused", handler.TripAbort);
        Assert.Equal(DeathCorpse.No, handler.Corpse);
        Assert.Empty(rig.PickUps);
        Assert.Null(rig.R.H.State.Containers.Viewed); // no view is left open
        Assert.Equal(4, handler.Kills);
    }

    [Fact]
    public void M5_3_d4_an_open_that_lands_after_the_handler_gave_up_is_closed_when_it_lands()
    {
        var rig = Died("black-spawn-den-aluvian", r =>
        {
            r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied);
            r.OpenDelay = 17; // past the handler's 15 s, inside the action's own 20 s
        });
        var handler = Begun(rig);

        var status = rig.Run(handler);
        for (var i = 0; i < 160; i++)
        {
            rig.Frame(handler, ref status); // the open lands while the fight, or after it
        }

        Assert.Equal("open-refused", handler.TripAbort);
        Assert.Empty(rig.PickUps);
        Assert.Contains(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.NoLongerViewingContents && s.Guid == rig.CorpseGuid); // closed fire-and-forget, not left open
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("the abandoned open of", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_d4_a_corpse_that_appears_late_within_its_15_s_window_is_found_not_given_up(string ground)
    {
        var rig = Died(ground, r =>
        {
            r.Facts.SetPortalTie(ground, PortalTie.Tied);
            r.CorpseDelay = 12;
        });
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathLegVerdict.TripAbortNone, handler.TripAbort);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Equal(2, handler.ItemsBack);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("appeared 12", StringComparison.Ordinal) && e.Message.Contains("found, not given up", StringComparison.Ordinal));
    }

    [Fact]
    public void M5_3_d4_the_abort_options_default_to_M4s_40_percent_and_90_seconds_and_a_bad_value_is_an_error()
    {
        static DeathOptions Parse(params string[] args) => DeathOptions.FromArguments(CommandLineArgument.ParseAll(args));

        var defaults = Parse();
        Assert.Equal((40.0, 90.0), (defaults.AbortHealthPercent, defaults.AbortSeconds));
        Assert.Equal("seconds=600", defaults.ToString()); // the defaults leave the logged options as they were
        var set = Parse("--death-recover-abort-health=55", "--death-recover-abort-seconds=45");
        Assert.Empty(set.Errors);
        Assert.Equal((55.0, 45.0), (set.AbortHealthPercent, set.AbortSeconds));
        Assert.Contains(" abort_health=55 abort_seconds=45", set.ToString(), StringComparison.Ordinal);
        Assert.Equal(["--death-recover-abort-health: '101' is not 0 to 100 percent"], Parse("--death-recover-abort-health=101").Errors);
        Assert.Equal(["--death-recover-abort-seconds: '0' is not a positive number of seconds"], Parse("--death-recover-abort-seconds=0").Errors);
        Assert.Contains(OptionCatalog.Death().Options, o => o.Name == "--death-recover-abort-health");
        Assert.Contains(OptionCatalog.Death().Options, o => o.Name == "--death-recover-abort-seconds");
    }

    // ------------------------------------------------------------------------------------------------ fix F1 (the review's 3-R1, 3-R5 and 3-R6)

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F1_3_R1_vitae_grants_is_the_xp_verbs_the_session_sent_from_the_deaths_start_to_the_handlers_end_never_a_constant(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var tools = rig.View.Tools;
        _ = tools.AddXp(111, ActionSource.Host); // before the handler began: not the death's
        var handler = Begun(rig);
        Assert.Equal(0, handler.VitaeGrants);
        var sent = false;

        var status = RunWith(rig, handler, () =>
        {
            if (!sent && handler.CurrentStep == "corpse-walk")
            {
                sent = true;
                _ = tools.AddXp(500, ActionSource.Host); // a later change re-adds a vitae grant on the death path
            }
        });
        Dump(rig, handler);

        Assert.True(sent);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, handler.VitaeGrants);
        Assert.Contains(" vitae_grants=1 ", handler.Fields, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: deaths=1 ", StringComparison.Ordinal) && e.Message.Contains(" vitae_grants=1 ", StringComparison.Ordinal)); // the done line
        _ = tools.AddXp(7, ActionSource.Host); // after the end: the count stopped with the handler
        Assert.Equal(1, handler.VitaeGrants);
    }

    [Fact]
    public void M5_3_F1_3_R5_the_pre_trip_heal_casts_through_the_cast_seam_once_as_automation_and_logs_its_try_and_its_end()
    {
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var casts = 0;
        HealSpell(rig, () =>
        {
            casts++;
            rig.SetHealth(170);
        });
        rig.SetHealth(100);
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, RecoverMinHealthPercent = 99 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, casts);
        Assert.Equal(1, handler.Heals);
        var log = rig.R.H.ActionLog;
        var heal = Assert.Single(Enumerable.Range(0, log.Count).Select(i => log[i]), e => e.Kind == ActionKind.CastSpell && e.Summary == "Cast spell=" + DeathScenario.HealSelfVii);
        Assert.Equal(ActionSource.Automation, heal.Source);
        Assert.All(Enumerable.Range(0, log.Count).Select(i => log[i]).Where(e => e.Kind == ActionKind.SetCombatMode), e => Assert.Equal(ActionSource.Automation, e.Source));
        var attempts = rig.Log.Entries.Where(e => e.Category == CastAttempt.LogCategory).Select(e => e.Message).ToList();
        Assert.Contains(attempts, l => l.StartsWith("handler heal: try 1 Heal Self VII", StringComparison.Ordinal) && l.EndsWith("(Once, Automation)", StringComparison.Ordinal));
        Assert.Contains(attempts, l => l.StartsWith("handler heal: unproven after 1 tries", StringComparison.Ordinal));
    }

    [Fact]
    public void M5_3_F1_3_R5_a_pre_trip_fizzle_is_logged_with_the_attempts_tally_is_not_one_of_the_four_heals_and_the_stage_casts_again()
    {
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var casts = 0;
        HealSpell(rig, () =>
        {
            casts++;
            if (casts == 1)
            {
                return 0x0402u; // the first cast fizzles
            }

            rig.SetHealth(170);
            return 0u;
        });
        rig.SetHealth(100);
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, RecoverMinHealthPercent = 99 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, casts);
        Assert.Equal(1, handler.Heals); // the fizzle is a named end of its attempt and not one of the PreTripMaxHeals
        var lines = rig.Log.Entries.Select(e => e.Message).ToList();
        Assert.Contains(lines, l => l.StartsWith("handler: heal cast ended fizzled (not counted as a heal) fizzles=1 busy=0", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("handler: heal cast ended unproven (heal 1 of 4)", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries.Where(e => e.Category == CastAttempt.LogCategory).Select(e => e.Message), l => l.StartsWith("handler heal: fizzled after 1 tries", StringComparison.Ordinal));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse); // and the trip went on
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F1_3_R5_a_hold_among_the_tuskers_is_not_spent_by_fizzles_and_a_busy_answer_only_the_casts_that_ran_count_toward_the_four(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var casts = 0;
        HealSpell(rig, () =>
        {
            casts++;
            if (casts <= 3)
            {
                return 0x0402u; // three fizzles: three attempts that end Fizzled
            }

            if (casts == 4)
            {
                return 0x001Du; // busy: the attempt waits and sends again
            }

            rig.SetHealth(170);
            return 0u;
        });
        var handler = Begun(rig);
        var hit = false;

        var status = RunWith(rig, handler, () =>
        {
            if (!hit && handler.CurrentStep == "corpse-walk")
            {
                hit = true;
                rig.SetHealth(50);
            }
        });
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(5, casts); // five sends: before the seam the first four were the four heals and the hold gave up with health-under-40
        Assert.Equal(1, handler.TripHeals);
        Assert.Equal(DeathLegVerdict.TripAbortNone, handler.TripAbort);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Equal(2, handler.ItemsBack);
        Assert.Equal(3, rig.Log.Entries.Count(e => e.Message.StartsWith("handler: trip heal cast ended fizzled (not counted as a heal)", StringComparison.Ordinal)));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: trip heal cast ended unproven (heal 1 of 4) fizzles=0 busy=1", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F1_3_R6_a_pick_up_that_times_out_reads_partial_not_yes(string ground)
    {
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        rig.SilentItems.Add(0x80000201); // the worn piece's pick-up is never answered: it times out, the loop goes on
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathLegVerdict.TripAbortNone, handler.TripAbort); // no abort: the loop ran to its end
        Assert.Equal(2, handler.CorpseEntries);
        Assert.Equal(1, handler.ItemsBack);
        Assert.Equal(DeathCorpse.Partial, handler.Corpse); // the old rule read yes (an end with at least one item)
        Assert.Contains(" corpse_recovered=partial ", handler.Fields, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse_recovered=partial items_back=1 of 2 (", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M5_3_F1_3_R6_an_abort_with_a_pick_up_in_flight_counts_it_when_its_0022_lands_and_the_trip_is_partial(string ground)
    {
        var rig = Died(ground, r =>
        {
            r.Facts.SetPortalTie(ground, PortalTie.Tied);
            r.PickUpDelay = 12; // the first pick-up is still in flight when the trip's time runs out
        });
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, AbortSeconds = 6 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("trip-over-6s", handler.TripAbort);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("trip aborted (trip-over-6s) during corpse-loot", StringComparison.Ordinal));
        Assert.Equal([0x80000200u], rig.PickUps); // nothing new was sent after the abort
        Assert.Equal(1, handler.ItemsBack); // counted when the 0022 landed, after the abort (the count that stopped at the abandon read 0)
        Assert.Equal(DeathCorpse.Partial, handler.Corpse);
        Assert.True(rig.R.H.State.Self.Inventory.Find(0x80000200).Held, "the item is in the pack");
        Assert.Contains(" corpse_recovered=partial ", handler.Fields, StringComparison.Ordinal);
    }
}

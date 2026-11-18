using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Session.Contracts;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
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

        public LivePositions Live { get; set; } = LivePositions.ReportsOnly;
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

        /// <summary>M6 7 AH: true: every walk to a vendor fails at once (the server's move, a blocked way).</summary>
        public bool FailVendorWalks { get; set; }

        /// <summary>M6 6 C2: the next point walks (TravelPurpose.Cell, the rung change's walks to the portal fronts) that fail at once, as the live round's server move did.</summary>
        public int FailCells { get; set; }

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            if (request.Purpose == TravelPurpose.Vendor && FailVendorWalks)
            {
                return new FailedLeg();
            }

            if (request.Purpose == TravelPurpose.Cell && FailCells > 0)
            {
                FailCells--;
                return new FailedLeg();
            }

            return new RunLeg(rig, this, request);
        }

        private sealed class FailedLeg : ITravelLeg
        {
            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => "travel:run:failed: the player was moved 24.9 m in one step (a teleport or a server correction)";

            public string Fields => "route_m=0.0 run_s=0.0";

            public TravelStatus Step(IGameSessionView session, double now) => TravelStatus.Failed;

            public void Pause()
            {
            }

            public void Resume(double now)
            {
            }

            public void Dispose()
            {
            }
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
                    if (request.Purpose == TravelPurpose.Vendor || (rig.ExactStops && request.Purpose != TravelPurpose.Corpse))
                    {
                        rig.StandAt(at.CellId, at.Local); // the vendor's stop (M5 3 F5: the rearm's kit trip walks to him); M6 6 C1: the rung change's walks stop on their points
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
        /// <param name="kitless">M6 0 F3: the character's profile has no kit (the climber on an outdoor ground): <see cref="GearKit.Bare"/> is its kit and the ground has no portal.</param>
        /// <param name="level">M6 6 C1: the character's level (PropertyInt 25), or null (unsent: the scripted mage of the den grounds). Given, the death runs ACE's item rule for that level: <see cref="Drops"/> go to the corpse, ACE's drop line or its
        /// "retained" line is sent with the corpse, and the respawn's vitals are ACE's 75% of the maxima.</param>
        public HandlerRig(string groundKey, bool spare = true, Func<HuntingGround, HuntingGround>? alter = null, uint? spareWcid = null, bool kitless = false, int? level = null)
        {
            Ground = GroundCatalog.Get(groundKey);
            Ground = Ground.PullRest is { FullPercent: > 0 } bar ? Ground with { PullRest = bar with { FullPercent = 0 } } : Ground; // M6 7 AR: the door's full bar has its own tests (RechargeRulesTests); this rig has no recharge
            if (alter is not null)
            {
                Ground = alter(Ground);
            }

            Vendor = new VendorRig(0); // the world the scripted ACE and the scripted shops share
            Shops = new ScriptedShop(Vendor);
            R = Vendor.R;
            Caster = new ScriptedCaster(R, 426, 275, known: ScriptedCaster.BuildBook(manaLines: false)); // M6 15: the buff lines the rebuff casts and no mana line, the book these tests were written on (it was empty, so the top-up's dance had no spell to press; the player's rule now needs every cast spell in the book)
            if (spare)
            {
                Vendor.Arrive(SpareGuid, spareWcid is null ? "Spare Wand" : "Training Wand", spareWcid ?? WandLine.Wcid, CasterType, 50);
                Vendor.Put(SpareGuid);
            }

            View = new View(R);
            Level = level;
            if (level is { } l)
            {
                R.PrivateInt(PlayerQualities.LevelProperty, l); // after the scripted description, which replaces the integer properties
            }

            if (Ground.Portal is { } portal)
            {
                R.Server((p, s) => AceM3Writers.MagicUpdateSpell(p, s, (ushort)portal.RecallSpell));
            }

            Caster.CastHook = spell =>
            {
                if (Ground.Portal is null || spell != Ground.Portal.RecallSpell)
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
            Kit = kitless ? GearKit.Bare : GearKit.Of([
                new GearItem(GearKit.Wand.Wcid, "Test Wand", GearKind.Wand, 35, 0x8000, EquipMask.Held, 1, null, 50),
                .. Enumerable.Range(0, ScriptedCaster.PieceNames.Length).Select(i => new GearItem(GearKit.Armour[i].Wcid, ScriptedCaster.PieceNames[i], GearKind.Armour, 0, 0x2, GearKit.Armour[i].ValidLocations, 1, null, 100))]);
        }

        public HuntingGround Ground { get; }

        /// <summary>M6 6 C1: the character's level, or null.</summary>
        public int? Level { get; }

        /// <summary>M6 6 C1: the held items ACE moves to the corpse at the death (the default: the wand and the first piece, a level-35-and-up death).</summary>
        public List<uint> Drops { get; } = [0x80000200, 0x80000201];

        /// <summary>M6 6 C1: true: the walks of the travel seam other than the corpse's stop on their points (the rung change's portal fronts and runs).</summary>
        public bool ExactStops { get; set; }

        /// <summary>M6 6 C1: the worn memory of this rig (never the process-wide one): the handler's <see cref="DeathOptions.Worn"/>.</summary>
        public WornMemory Worn { get; } = new();

        public VendorRig Vendor { get; }

        public ScriptedShop Shops { get; }

        /// <summary>The ground's kit wielded line (the profile's vendor wand).</summary>
        public KitLine WandLine => Ground.Supplies.Kit.Single(k => k.Slot == KitSlot.Wielded);

        /// <summary>M6 7 AH: the weenie of the wand a kit-less ground's floor sells (a caster ware of a profile vendor).</summary>
        public uint FloorWandWcid => Ground.Vendors.SelectMany(v => v.Sells).First(w => GearKit.FloorWeenieOf(w.Wcid) is { } f && (f.ItemType & CasterType) != 0).Wcid;

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
            new(Ground, Tally, Travel, LegFactory, Walk, Kit, Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, options ?? new DeathOptions { Facts = Facts, Journey = JourneyShop ? Context() : null, Worn = Worn }, Log);

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
                    var isCaster = Ground.Supplies.Kit.Any(k => k.Slot == KitSlot.Wielded) ? ware.Wcid == WandLine.Wcid : GearKit.FloorWeenieOf(ware.Wcid) is { } f && (f.ItemType & CasterType) != 0;
                    var wearable = GearFloor.IsGauntletWare(ware.Wcid) ? ((uint)EquipMask.HandWear, 0x8000u) : GearKit.FloorWeenieOf(ware.Wcid) is { } robe && ((uint)robe.ValidLocations & GearFloor.ChestArmor) != 0 ? ((uint)robe.ValidLocations, 0x17F00u) : ((uint, uint)?)null;
                    if (wearable is { } w)
                    {
                        shop.Wearables[ware.Wcid] = w;
                    }

                    shop.Rows.Add((-1, ++_row, ware.Name, ware.Wcid, isCaster ? CasterType : wearable is null ? MiscType : 0x2, ware.Price));
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
        /// <summary>M6 7 AY: the coin ACE's line names when nothing else dropped; 0: the retained line.</summary>
        public int CoinDrop { get; set; }

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
                var lost = new List<string>();
                foreach (var held in Drops)
                {
                    if (R.H.State.Objects.TryGet(held, out var o) && R.H.State.Self.Inventory.Contains(held))
                    {
                        var onCorpse = LootRulesTests.Item(o.Weenie.ItemType, 100, 50, held, o.Name, o.Weenie.WeenieClassId);
                        var wornAt = R.H.State.Self.Inventory.Equipped.FirstOrDefault(e => e.Guid == held).Location;
                        if ((o.Weenie.ValidLocations ?? (wornAt == 0 ? (uint?)null : wornAt)) is { } valid)
                        {
                            // M6 7 AH: the corpse's piece keeps its slot data (ACE's F745 carries ValidLocations and Priority), so the take-back can tell a replaced slot
                            onCorpse.Created = onCorpse.Created with { Weenie = onCorpse.Weenie with { Header = onCorpse.Weenie.Header | WeenieHeaderFlags.ValidLocations, ValidLocations = valid } };
                        }

                        _items[held] = onCorpse;
                        lost.Add(o.Name);
                        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(held, 1));
                    }
                }

                if (Level is not null)
                {
                    // ACE's lines with the corpse (Player_Death.cs 690-732 DropMessage: "your " before every item but the coins; Creature_Death.cs 578-579 when nothing dropped @95ef426d)
                    R.Raw(AceWireWriters.ServerMessage(lost.Count == 0 ? (CoinDrop > 0 ? DeathDropRule.LostPrefix + CoinDrop.ToString("N0", CultureInfo.InvariantCulture) + " Pyreals!" : DeathDropRule.RetainedLine) : DeathDropRule.LostPrefix + string.Join(", ", lost.Select((n, i) => (i > 0 && i == lost.Count - 1 ? "and " : string.Empty) + "your " + n)) + "!", 1));
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
                    if (Level is null)
                    {
                        Vital(2, 400);
                        Vital(6, 300);
                        return;
                    }

                    // ACE's respawn vitals: each vital set to round(0.75 x its maximum) 3 s after the teleport (Player_Death.cs 242-278 @95ef426d), the maxima read as the client has them
                    foreach (var (max, current) in new[] { (MageTemplate.MaxHealth, 2u), (MageTemplate.MaxMana, 6u) })
                    {
                        _ = R.H.State.Self.Qualities.TryGetVitalLevels(max, out _, out var top);
                        Vital(current, (uint)Math.Round(0.75 * top));
                    }
                });
            });
        }

        private byte _undressSeq = 100;

        /// <summary>M6 7 AE: the death scenario's undress (D37) as ACE answers it: a worn piece goes to the main pack (02DA Wielder 0, 02CE location 0, 02DA Container, then 0022).</summary>
        public void Undress(uint guid)
        {
            R.Raw(AceM3Writers.PublicUpdateInstanceId(++_undressSeq, guid, 3, 0));
            R.Raw(AceM3Writers.PublicUpdatePropertyInt(_undressSeq, guid, 10, 0));
            R.Raw(AceM3Writers.PublicUpdateInstanceId(_undressSeq, guid, 2, WorldActionRig.Player));
            R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, guid, WorldActionRig.Player, 0, 0));
        }

        /// <summary>M6 6 C1: an ordinary item in the main pack (a misc item the death may drop, <see cref="Drops"/>).</summary>
        public void PackItem(uint guid, string name, uint wcid)
        {
            Vendor.Arrive(guid, name, wcid, MiscType, 25);
            Vendor.Put(guid);
        }

        private readonly Dictionary<uint, (uint Cell, Vector3 At)> _portalDrops = [];

        /// <summary>M6 6 C1: the travel file's edges as portal objects answering a Use with their destination (as <c>RungRig.Portals</c>).</summary>
        public void Portals(TravelRoutes routes, params string[] keys)
        {
            foreach (var key in keys)
            {
                var e = routes.Edge(key);
                PortalObject(e.Guid, e.Name, e.Wcid, e.Cell, e.Origin);
                _portalDrops[e.Guid] = (e.DestCell, e.DestOrigin);
            }
        }

        /// <summary>M6 6 C1: the ground's entry portal, dropping at the ground's arrival.</summary>
        public void Entry()
        {
            var entry = Ground.Entry!;
            PortalObject(entry.Guid, Ground.Name + " entry", entry.Wcid, entry.Cell, entry.Origin);
            _portalDrops[entry.Guid] = (Ground.Arrival.Cell, Ground.Arrival.Origin);
        }

        private void PortalObject(uint guid, string name, uint wcid, uint cell, Vector3 at)
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), new AcPosition(cell, at, Quaternion.Identity), name: name);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid } }, isUpdate: false);
        }

        private void LandAt(uint cell, Vector3 at)
        {
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(++_teleport));
            StandAt(cell, at);
            R.H.Unhide(++_state);
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
                    case FakeSendKind.Use when _portalDrops.TryGetValue(send.Guid, out var drop):
                        At(1.0, () => LandAt(drop.Cell, drop.At)); // M6 6 C1: a portal of the rung change
                        R.UseDone();
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

    private static HandlerRig Died(string ground, Action<HandlerRig>? setup = null, bool spare = true, Func<HuntingGround, HuntingGround>? alter = null, uint? spareWcid = null, bool kitless = false)
    {
        var rig = new HandlerRig(ground, spare, alter, spareWcid, kitless);
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
        Assert.Equal("deaths=1 corpse_recovered=yes route=recall items_back=2 vitae_grants=0 rebuffed=yes rebuffed_pretrip=no relines=2 rearm=pack rewear=none replace=none", handler.Fields); // M6 7 AE: nothing of the worn set lay in the pack (the memory is empty here): a no-op
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

    [Fact]
    public void M6_14_the_hosts_step_before_the_rebuff_holds_it_and_the_order_goes_on_when_the_step_ends()
    {
        // The stop at the buff bot (DeathScenario.BuffBotBeforeRebuff): she respawns ten metres from the bot, and its buffs come before her own pass and the corpse trip.
        const string Ground = "black-spawn-den-aluvian";
        var rig = Died(Ground, r => r.Facts.SetPortalTie(Ground, PortalTie.Tied));
        var handler = Begun(rig);
        var (asked, legsWhileHeld, stepWhileHeld) = (0, -1, string.Empty);
        handler.BeforeRebuff = (_, _) =>
        {
            asked++;
            if (asked > 20)
            {
                return false;
            }

            (legsWhileHeld, stepWhileHeld) = (rig.InnerStages.Count, handler.CurrentStep);
            return true;
        };

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(handler));

        Assert.True(asked > 20, $"asked {asked} times");
        Assert.Equal((0, "rebuff"), (legsWhileHeld, stepWhileHeld)); // held in the rebuff's step with no inner leg made: her own pass had not begun
        Assert.Equal([FightStages.Buffs, FightStages.Buffs, FightStages.Mode], rig.InnerStages); // then the order as ever: the rebuff, the item lines, the fight
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Null(handler.Failure);
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

    // ------------------------------------------------------------------------------------------------ M6 7 AG: the trip's fights against the watchdog, a cut's second entry

    private static void CreatureAtCorpse(HandlerRig rig, DeathHandler handler, uint guid)
    {
        var corpse = handler.Facts.CorpsePosition!.Value;
        var h = rig.R.H.R;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(corpse.CellId, corpse.Frame.Origin.X + 3, corpse.Frame.Origin.Y, corpse.Frame.Origin.Z), name: "Tusker Slave");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = rig.Ground.TargetWcids()[0] } }, isUpdate: false);
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M6_7_AG_a_path_that_never_clears_holds_the_trip_only_for_the_budget_and_the_trip_walks_on_to_the_corpse(string ground)
    {
        // gate m6-7-g, the autopilot-mage lane: 40 kills and 650 s of the trip's 725 s in trip fights, 76 s of walking: a creature is always near the path. Here the creature never goes: without the budget the trip fights until the rig's 900 s run out.
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        CreatureAtCorpse(rig, handler, 0x80000777);

        var status = rig.Run(handler, 900);

        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(handler.TripFightBudgetSpent);
        Assert.True(handler.TripFightKills >= 2, "the path was cleared first: " + handler.TripFightKills);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.True(rig.R.H.State.Objects.TryGet(0x80000777, out _), "the creature still stands: the trip walked on, it did not clear it");
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: the trip's fights spent ", StringComparison.Ordinal) && e.Message.EndsWith("from here the trip fights only what attacks it (M5 D88) and walks on to the corpse", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: no attacker for 10 s after ", StringComparison.Ordinal) && e.Message.Contains("trip_fight_s=", StringComparison.Ordinal));
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("trip-fight") < steps.IndexOf("corpse-loot"), string.Join(",", steps));
    }

    [Fact]
    public void M6_7_AG_past_the_budget_the_trip_still_fights_what_attacks_it_and_then_walks_on()
    {
        // M5 D88: always fight back an attacker. The hit comes after the budget is spent, on the walk: the trip stops, fights, and goes on when the attacks stop.
        const string ground = "black-spawn-den-aluvian";
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        CreatureAtCorpse(rig, handler, 0x80000777);
        var status = ScenarioStatus.Running;
        var end = rig.Now + 900;
        var fightsAtHit = -1;
        while (status == ScenarioStatus.Running && rig.Now < end)
        {
            rig.Frame(handler, ref status);
            if (fightsAtHit < 0 && handler.TripFightBudgetSpent && handler.CurrentStep != "trip-fight")
            {
                fightsAtHit = handler.TripFights;
                rig.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 5, 4, false, 0));
            }
        }

        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(fightsAtHit > 0, "the budget was spent with the trip still to walk");
        Assert.True(handler.TripFights > fightsAtHit, $"the attacker was fought back: {handler.TripFights} fights, {fightsAtHit} at the hit");
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    [Fact]
    public void M6_7_AG_a_creature_near_the_path_within_the_budget_still_holds_the_trip_without_an_attack()
    {
        // M5 D92: the corpse trip never runs past tuskers until the budget says so: the budget changes nothing for a path that clears.
        const string ground = "black-spawn-den-aluvian";
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        CreatureAtCorpse(rig, handler, 0x80000777);
        var h = rig.R.H.R;
        var killed = false;
        rig.OnInner = (stages, step) =>
        {
            if (!killed && stages == FightStages.Mode && handler.CurrentStep == "trip-fight" && step >= 2)
            {
                killed = true;
                h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(0x80000777, 1));
            }
        };

        var status = rig.Run(handler, 900);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.False(handler.TripFightBudgetSpent);
        Assert.True(killed);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: no creature within 20 m of the path after ", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Grounds))]
    public void M6_7_AG_a_cut_mid_trip_enters_again_where_the_body_stands_with_no_respawn_and_no_second_death(string ground)
    {
        // gate m6-7-g: the 900 s watchdog cut the station 9.7 m from the corpse; the second entry waited the respawn's 5 s beside a Tusker Guard, printed "respawn: lifestone" with the body in the den and classified the respawn again (an unbound run reads elsewhere there and fails).
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        rig.Facts.ObservedDeath = rig.Tally;
        var options = new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false, Fight = false, Travel = rig.Travel, Facts = rig.Facts };
        DeathScenario Station() => new(rig.Kit, rig.Caster.Table, options, rig.Walk, rig.Log, endSession: false, stages: DeathStages.Death | DeathStages.Rebuff | DeathStages.Corpse, fightFactory: rig.LegFactory, fight: new FightOptions { Kills = 4, Ground = ground });

        var status = ScenarioStatus.Running;
        var first = Station();
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && first.Handler?.CurrentStep != "corpse-walk"; i++)
        {
            rig.Frame(null, ref status);
            status = first.Step(rig.View, rig.Now);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal("corpse-walk", first.Handler?.CurrentStep);
        var deaths = rig.Tally.Deaths;
        first.Dispose(); // the cut
        Assert.True(rig.Tally.Running, "an observed tally is the autopilot's: the cut does not stop it");
        var respawns = rig.Log.Entries.Count(e => e.Message.StartsWith("respawn: lifestone", StringComparison.Ordinal));
        var mark = rig.Log.Entries.Count;

        rig.Facts.ObservedDeath = rig.Tally;
        using var second = Station();
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            rig.Frame(null, ref status);
            status = second.Step(rig.View, rig.Now);
        }

        foreach (var entry in rig.Log.Entries)
        {
            output.WriteLine(entry.Message);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(second.Failure);
        var after = rig.Log.Entries.Skip(mark).Select(e => e.Message).ToList();
        Assert.Contains(after, m => m.StartsWith("death: entered again after a cut: the body stands in cell ", StringComparison.Ordinal));
        Assert.Contains(after, m => m.StartsWith("handler: respawn=read at the station's first entry", StringComparison.Ordinal));
        Assert.DoesNotContain(after, m => m == "stage respawn" || m == "stage respawn-vitals");
        Assert.Equal(respawns, rig.Log.Entries.Count(e => e.Message.StartsWith("respawn: lifestone", StringComparison.Ordinal)));
        Assert.Equal(deaths, rig.Tally.Deaths);
        Assert.Contains("deaths=1 corpse_recovered=yes ", second.HandlerFields, StringComparison.Ordinal); // route=here is the journey's seam (M6_7_U), not this rig's
        Assert.Contains(" respawn=lifestone ", second.Outcome().Line, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_7_AG_a_cut_keeps_the_handlings_deaths_so_a_second_death_before_it_counts_once_and_stays_on_the_way_to_the_third_death_stop()
    {
        const string ground = "black-spawn-den-aluvian";
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        rig.Facts.ObservedDeath = rig.Tally;
        var options = new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false, Fight = false, Travel = rig.Travel, Facts = rig.Facts };
        DeathScenario Station() => new(rig.Kit, rig.Caster.Table, options, rig.Walk, rig.Log, endSession: false, stages: DeathStages.Death | DeathStages.Rebuff | DeathStages.Corpse, fightFactory: rig.LegFactory, fight: new FightOptions { Kills = 4, Ground = ground });

        var status = ScenarioStatus.Running;
        var first = Station();
        var died = false;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && !(died && first.Handler?.CurrentStep == "corpse-walk"); i++)
        {
            if (!died && first.Handler?.CurrentStep == "corpse-walk")
            {
                died = true;
                rig.Die();
            }

            rig.Frame(null, ref status);
            status = first.Step(rig.View, rig.Now);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(2, first.Handler?.Deaths);
        first.Dispose();

        rig.Facts.ObservedDeath = rig.Tally;
        using var second = Station();
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            rig.Frame(null, ref status);
            status = second.Step(rig.View, rig.Now);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, rig.Tally.Deaths);
        Assert.Contains("deaths=2 corpse_recovered=yes ", second.HandlerFields, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_7_AG_an_observed_death_at_the_arrival_still_takes_the_respawn()
    {
        // the first entry: the body stands at the arrival, so it is no cut's second entry.
        const string ground = "black-spawn-den-aluvian";
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        rig.Facts.ObservedDeath = rig.Tally;
        var options = new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false, Fight = false, Travel = rig.Travel, Facts = rig.Facts };
        using var scenario = new DeathScenario(rig.Kit, rig.Caster.Table, options, rig.Walk, rig.Log, endSession: false, stages: DeathStages.Death | DeathStages.Rebuff | DeathStages.Corpse, fightFactory: rig.LegFactory, fight: new FightOptions { Kills = 4, Ground = ground });
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 40 && status == ScenarioStatus.Running; i++)
        {
            rig.Frame(null, ref status);
            status = scenario.Step(rig.View, rig.Now);
        }

        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("entered after a death the autopilot did not script", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("death: entered again after a cut", StringComparison.Ordinal));
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
        Assert.EndsWith(" relines=2 rearm=pack rewear=none replace=none", handler.Fields, StringComparison.Ordinal);
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
        Assert.EndsWith(" relines=2 rearm=bought rewear=none replace=none", handler.Fields, StringComparison.Ordinal);
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

    /// <summary>The host's runner as the rig's seam (M6 4 F1).</summary>
    private sealed class RigRunner(RunTravel travel) : IJourneyRunnerHost
    {
        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => travel.GoTo(session, request, now);
    }

    [Fact]
    public void A_corpse_run_to_a_routed_ground_goes_by_the_rung_change_decided_before_the_act()
    {
        // M6 4 F1 (4-R1): the Temple known untied with a read book: the travel rule (AutopilotTravelRule.Out, the autopilot's) says the rung change, so the corpse trip's way to the ground is the travel seam's Ground leg (the rung change), never a
        // Primary Portal Recall tried and never the runner's TODO stop; the station's own failure (here the entry's level gate, the rig's level being below it) is the handler's failure by name.
        const string ground = "tusker-temple-fort-tethana";
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Untied));
        var book = rig.R.H.State.Self.Spellbook;
        book.IsKnown = true;
        book.Learn(DenTravelStation.PortalRecallSpell);
        var travel = new JourneyTravel("Winnie", rig.Facts, rig.Log, new RigRunner(rig.Travel), dungeon: rig.Ground);
        _ = new JourneyContext { Character = "Winnie", Account = "test", Log = rig.Log, Travel = travel, Spells = rig.Caster.Table, Dungeon = rig.Ground, Facts = rig.Facts };
        var handler = new DeathHandler(rig.Ground, rig.Tally, travel, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts }, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(handler));
        Dump(rig, handler);

        Assert.Equal(DeathRoute.RungChange, handler.Route);
        Assert.Equal(0, rig.Recalls); // decided before the act: no recall tried
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("route=rung-change (the travel rule says the rung change (route ", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("rung_change: ground=tusker-temple-fort-tethana", StringComparison.Ordinal) || e.Message.Contains("rung_change: ground=tusker-temple-fort-tethana", StringComparison.Ordinal));
        Assert.StartsWith("route=rung-change: travel:rung-change:", handler.Failure?.Replace("handler: ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(m6-first-tie-walk)", handler.Failure, StringComparison.Ordinal);
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

    /// <summary>
    /// M6 16 E2b: a corpse trip with a tusker of the ground the server last reported <paramref name="reportMetres"/> east of the corpse; with <paramref name="bodyMetres"/> given, the session draws bodies and the tusker's
    /// stands that far east of the corpse (the den's cells are EnvCells: a body answers within 20 m of the report). A trip fight, when one begins, kills it. Returns the trip's fights.
    /// </summary>
    private int TripWithATusker(float reportMetres, float? bodyMetres, out HandlerRig rig)
    {
        const string ground = "black-spawn-den-aluvian";
        const uint tusker = 0x80000777;
        rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        var corpse = handler.Facts.CorpsePosition!.Value;
        var h = rig.R.H.R;
        var basis = ReducerHarness.Create(tusker, ReducerHarness.Stamps(), ReducerHarness.Position(corpse.CellId, corpse.Frame.Origin.X + reportMetres, corpse.Frame.Origin.Y, corpse.Frame.Origin.Z), name: "Tusker Slave");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = rig.Ground.TargetWcids()[0] } }, isUpdate: false);
        if (bodyMetres is { } metres)
        {
            ((View)rig.View).Live = new ScriptedBodies().StandEast(tusker, corpse, metres).Over(h.Clock);
        }

        var killed = false;
        rig.OnInner = (stages, step) =>
        {
            if (!killed && stages == FightStages.Mode && handler.CurrentStep == "trip-fight" && step >= 2)
            {
                killed = true;
                h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(tusker, 1)); // the trip fight's kill
            }
        };

        var status = rig.Run(handler, 900);
        Dump(rig, handler);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        return handler.TripFights;
    }

    [Fact]
    public void M6_16_E2b_a_tusker_drawn_12_m_from_the_corpse_and_last_reported_30_m_from_it_holds_the_trip_for_its_fight()
    {
        // before (the report alone): 30 m from the corpse is past the 20 m of the path: the trip walked on and looted beside it
        Assert.Equal(0, TripWithATusker(reportMetres: 30, bodyMetres: null, out var before));
        Assert.DoesNotContain(before.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: ", StringComparison.Ordinal));

        // after: its body stands 12 m from the corpse: the trip holds and fights it first, and the line gives the distance to where it is drawn
        Assert.True(TripWithATusker(reportMetres: 30, bodyMetres: 12, out var after) >= 1);
        Assert.Contains(after.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: Tusker Slave 0x80000777 is ", StringComparison.Ordinal) && e.Message.Contains(" m from the path ahead during ", StringComparison.Ordinal));
        var steps = Steps(after);
        Assert.True(steps.IndexOf("trip-fight") >= 0 && steps.IndexOf("trip-fight") < steps.IndexOf("corpse-loot"), string.Join(",", steps));
    }

    /// <summary>
    /// M6 16 E2b, part 2 (report-only): the path read is asked beside the threat board. The trip ends as before; the handler's disposal writes its counts; and the one difference a path read has from a read round her is
    /// named: a tusker near the path ahead and farther than the reach from her.
    /// </summary>
    [Fact]
    public void M6_16_E2b_the_trips_path_read_stands_beside_the_threat_board_and_the_handler_writes_its_counts()
    {
        const string ground = "black-spawn-den-aluvian";
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        var handler = Begun(rig);
        CreatureAtCorpse(rig, handler, 0x80000777);
        var h = rig.R.H.R;
        var killed = false;
        rig.OnInner = (stages, step) =>
        {
            if (!killed && stages == FightStages.Mode && handler.CurrentStep == "trip-fight" && step >= 2)
            {
                killed = true;
                h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(0x80000777, 1));
            }
        };

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(handler, 900));
        Assert.True(killed);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        handler.Dispose();

        var path = Lifestone.Client.Session.Threat.ThreatBoard.For(rig.View).Lane(DeathHandler.PathDetector);
        Assert.True(path.Agree > 0, $"agree {path.Agree} differ {path.Differ}");
        var shadow = rig.Log.Entries.Select(e => e.Message).Where(m => m.Contains(Lifestone.Client.Session.Threat.ThreatShadowLane.Token, StringComparison.Ordinal)).ToList();
        Assert.Equal($"threat-shadow: death: its asks: threat_shadow_agree={path.Agree} threat_shadow_differ={path.Differ} (agreed/differed by detector: death-path {path.Agree}/{path.Differ})", shadow[^1]);
        Assert.All(shadow.Take(shadow.Count - 1), m => Assert.StartsWith("threat-shadow: death-path: ", m, StringComparison.Ordinal));
    }

    [Fact]
    public void M6_16_E2b_a_tusker_last_reported_3_m_from_the_corpse_and_drawn_21_m_off_no_longer_holds_the_trip()
    {
        // before (the report alone): 3 m from the corpse: the trip held and fought it
        Assert.True(TripWithATusker(reportMetres: 3, bodyMetres: null, out _) >= 1);

        // after: its body stands 21 m from the corpse (18 m from the report: trusted), past the 20 m: the trip walks on and loots
        Assert.Equal(0, TripWithATusker(reportMetres: 3, bodyMetres: 21, out var after));
        Assert.DoesNotContain(after.Log.Entries, e => e.Message.StartsWith("handler: trip-fight: ", StringComparison.Ordinal));
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
    public void M6_0_F3_a_death_on_an_outdoor_ground_with_no_kit_walks_to_the_corpse_from_the_lifestone_and_wears_back_what_it_wore_checked_against_the_state()
    {
        var rig = Died("holtburg-outskirts", spare: false, kitless: true); // no kit, no portal, no caster in the pack: the wand went to the corpse
        Assert.Null(CombatModeGuard.Read(rig.R.H.State).Caster);
        var handler = new DeathHandler(rig.Ground, rig.Tally, rig.Travel, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts }, rig.Log) { WornSet = new WornMemory() };
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRearm.Corpse, handler.Rearm); // no kit to buy from, nothing in the pack: the corpse first (named, not a failure)
        Assert.Equal(DeathRoute.Walk, handler.Route); // no portal: the corpse walk from the lifestone, never the runner's TODO stop
        Assert.Equal(TravelPurpose.Corpse, Assert.Single(rig.Travel.Requests).Purpose); // an object goal at the corpse, no recall, no place
        Assert.NotEqual(0u, Assert.Single(rig.Travel.Requests).ObjectGuid);
        Assert.Equal(0, rig.Recalls);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("handler: rearm=corpse", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("route=walk", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("TODO(m6-first-tie-walk)", StringComparison.Ordinal));
        Assert.Equal(DeathLegVerdict.RebuffedSkipped, handler.RebuffedPretrip); // no caster, no rebuff before the trip
        Assert.Contains("rearm=corpse", handler.Fields, StringComparison.Ordinal);
        NoModVerb(rig);
    }

    [Fact]
    public void M6_0_F3_the_kit_less_items_back_check_reads_the_remembered_worn_set_against_the_state_not_a_kit_that_is_vacuously_true()
    {
        var rig = Died("holtburg-outskirts", spare: false, kitless: true);
        var lost = new WornMemory();
        lost.Seed([new WornEntry(0x80004444, 0x200)]); // a piece worn before the death that is nowhere now
        var handler = new DeathHandler(rig.Ground, rig.Tally, rig.Travel, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts }, rig.Log) { WornSet = lost };
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.False(handler.GearBack); // the piece is not worn again: the check after says so, where an empty kit would have said true
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("0x80004444", StringComparison.Ordinal) && e.Message.Contains("not worn again", StringComparison.Ordinal));
        Assert.EndsWith("worn=0", handler.WornFields, StringComparison.Ordinal);
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
    public void M6_7_AE_the_pieces_the_undress_left_in_the_pack_go_on_right_after_the_respawn_before_the_rebuff_each_named_and_checked_and_what_the_corpse_holds_is_left_to_the_take_back()
    {
        const uint boots = 0x80000203, gloves = 0x80000204;
        var rig = new HandlerRig("black-spawn-den-aluvian");
        rig.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied);
        rig.Worn.Snapshot(rig.R.H.State); // the worn set while the mage lived
        Assert.Equal(5, rig.Worn.Set.Count);
        rig.Undress(boots);
        rig.Undress(gloves);
        Assert.True(rig.R.H.State.Self.Inventory.Contains(boots) && !rig.R.H.State.Self.Inventory.TryGetEquipped(boots, out _), "the undress left the boots in the pack");
        rig.Die();
        rig.Settle();
        var before = rig.Worn.Set.ToDictionary(p => p.Key, p => p.Value);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(handler.Failure);
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("rearm") < steps.IndexOf("rewear") && steps.IndexOf("rewear") < steps.IndexOf("rebuff"), string.Join(",", steps)); // after the respawn and rearm, before the rebuff
        Assert.Equal("2of2", handler.Rewear);
        Assert.Contains(" rewear=2of2", handler.Fields, StringComparison.Ordinal);
        Assert.Contains(rig.Wields, w => w == boots);
        Assert.Contains(rig.Wields, w => w == gloves);
        foreach (var guid in new[] { boots, gloves })
        {
            Assert.Contains(rig.Log.Entries, e => e.Message.Contains($"rewear: before: ", StringComparison.Ordinal) && e.Message.Contains($"0x{guid:X8}", StringComparison.Ordinal) && e.Message.EndsWith("in the pack", StringComparison.Ordinal));
            Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("rewear: after: ", StringComparison.Ordinal) && e.Message.Contains($"0x{guid:X8}", StringComparison.Ordinal) && e.Message.EndsWith(": worn", StringComparison.Ordinal));
        }

        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("rewear: before:", StringComparison.Ordinal) && e.Message.Contains("0x80000201", StringComparison.Ordinal) && e.Message.Contains("not held", StringComparison.Ordinal)); // on the corpse: the take-back's
        Assert.Equal(before, rig.Worn.Set.ToDictionary(p => p.Key, p => p.Value)); // the memory is not snapshotted by the rewear: the corpse's pieces stay wanted by the take-back
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        NoModVerb(rig);
    }

    [Fact]
    public void M6_7_AE_a_real_death_leaves_nothing_of_the_worn_set_in_the_pack_so_the_rewear_is_a_logged_no_op_with_no_wield_and_no_step()
    {
        var rig = new HandlerRig("black-spawn-den-aluvian");
        rig.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied);
        rig.Worn.Snapshot(rig.R.H.State); // all five pieces still worn: no undress ran
        rig.Die();
        rig.Settle();
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("none", handler.Rewear);
        Assert.DoesNotContain("rewear", Steps(rig)); // no step entered
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("rewear=none: no remembered worn piece lies in the pack", StringComparison.Ordinal));
        Assert.Contains(" rewear=none", handler.Fields, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Wields, w => w is >= 0x80000202 and <= 0x80000205); // nothing worn again before the take-back's own lines
    }

    [Fact]
    public void Every_handler_field_reads_n_a_before_a_handler_ran_vitae_grants_included_never_a_constant_0_M5_3_F1_3_R1()
    {
        Assert.Equal("deaths=n/a corpse_recovered=n/a route=n/a items_back=n/a vitae_grants=n/a rebuffed=n/a rebuffed_pretrip=n/a relines=n/a rearm=n/a rewear=n/a replace=n/a", DeathHandler.UnmeasuredFields);
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
        rig.R.H.State.Self.Spellbook.Learn(DeathScenario.HealSelfVii); // the den mage knows it (M6 6 FXD1: the read book is checked before the cast)
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

    [Fact]
    public void M6_6_R1G_a_heal_refused_for_components_is_cast_once_then_dropped_for_the_trip_and_the_trip_goes_on()
    {
        // Live round 1: the top-up heal cast Heal Self VII into ACE's 0x0400 about 2,250 times. Now one cast, one named line, no more casts; the trip goes on at the health it has.
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var healCasts = 0;
        HealSpell(rig, () =>
        {
            healCasts++;
            return 0x0400u;
        });
        rig.SetHealth(100); // far under the minimum
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, RecoverMinHealthPercent = 99 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, healCasts);
        Assert.Equal(1, rig.Log.Entries.Count(e => e.Message.Contains("Heal Self dropped for the trip: no components (ACE 0x0400)", StringComparison.Ordinal)));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    // ------------------------------------------------------------------------------------------------ M6 6 FXD1 (6-D1): the heal's spellbook check and its permanent ends

    private const uint HealSelfIi = 1157, HealSelfIv = 1159;

    /// <summary>
    /// Heal Self II, IV and VII in the SpellTable; the read spellbook knows <paramref name="known"/> only (the climber is never granted a spell, D10, D34); each heal cast is counted by its id and answered with <paramref name="answer"/>'s code,
    /// the health brought back to the maximum on a cast that went out.
    /// </summary>
    private static Dictionary<uint, int> HealBook(HandlerRig rig, uint[] known, Func<uint, uint>? answer = null)
    {
        var casts = new Dictionary<uint, int>();
        uint[] levels = [HealSelfIi, HealSelfIv, DeathScenario.HealSelfVii];
        string[] names = ["Heal Self II", "Heal Self IV", "Heal Self VII"];
        for (var i = 0; i < levels.Length; i++)
        {
            rig.Caster.Table[levels[i]] = WorldActionRig.Spell(levels[i], DatReaderWriter.Enums.SpellType.Boost, SpellFlags.SelfTargeted) with { Name = names[i], BaseMana = 10 };
        }

        rig.R.Spells([.. levels.Select(id => rig.Caster.Table[id])]);
        var book = rig.R.H.State.Self.Spellbook;
        book.IsKnown = true;
        foreach (var id in known)
        {
            book.Learn(id);
        }

        var inner = rig.Caster.CastHook;
        rig.Caster.CastHook = spell =>
        {
            if (!levels.Contains(spell))
            {
                return inner!(spell);
            }

            casts[spell] = casts.GetValueOrDefault(spell) + 1;
            var code = answer?.Invoke(spell) ?? 0u;
            if (code == 0)
            {
                rig.SetHealth(170);
            }

            rig.R.UseDone(code);
            return true;
        };
        return casts;
    }

    [Fact]
    public void M6_6_FXD1_a_read_book_without_heal_self_vii_casts_the_highest_heal_self_it_knows_and_never_2073()
    {
        // 6-D1 (the review's blocker): the climber's book at a low rung holds no Heal Self VII; the handler cast 2073 into ACE's 0x03FC every frame. Now the check before the cast picks the highest level the book knows (the keeper's line).
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var casts = HealBook(rig, [HealSelfIi, HealSelfIv]);
        rig.SetHealth(100); // far under the minimum
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, RecoverMinHealthPercent = 99 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.False(casts.ContainsKey(DeathScenario.HealSelfVii)); // zero casts of 2073
        Assert.False(casts.ContainsKey(HealSelfIi));
        Assert.True(casts.GetValueOrDefault(HealSelfIv) >= 1, "the known Heal Self IV is cast");
        Assert.True(handler.Heals >= 1);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: heal: the heal's level is Heal Self IV (1159), the highest Heal Self level the spellbook (", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("Heal Self dropped for the trip", StringComparison.Ordinal));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    [Fact]
    public void M6_6_FXD1_a_read_book_with_no_heal_self_drops_the_heal_once_with_no_cast_and_the_trip_goes_on()
    {
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var casts = HealBook(rig, []);
        rig.SetHealth(100);
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, RecoverMinHealthPercent = 99 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Empty(casts); // no Heal Self cast at all, 2073 included
        Assert.Equal(1, rig.Log.Entries.Count(e => e.Message.Contains("Heal Self dropped for the trip: not in the spellbook: the read spellbook (", StringComparison.Ordinal)));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse); // the trip went on
    }

    [Theory]
    [InlineData(0x03FCu)] // ACE's MagicInvalidSpellType for a spell the caster does not know (Player_Magic.cs 129-132 @95ef426d): ServerError, before FXD1 a recast every frame
    [InlineData(0x0400u)] // no components (R1G's case, now one rule)
    public void M6_6_FXD1_a_permanent_end_of_the_heal_drops_it_after_one_cast_named_and_never_recast(uint code)
    {
        // An unread book is not judged (the den mage's Heal Self VII as before); the refusal is permanent for the trip, every end but Landed, Unproven, Fizzled, Busy and ManaShort.
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var healCasts = 0;
        HealSpell(rig, () =>
        {
            healCasts++;
            return code;
        });
        rig.R.H.State.Self.Spellbook.IsKnown = false; // not read yet: not judged
        rig.SetHealth(100);
        var handler = Begun(rig, new DeathOptions { Facts = rig.Facts, RecoverMinHealthPercent = 99 });

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, healCasts);
        Assert.Equal(1, rig.Log.Entries.Count(e => e.Message.Contains("Heal Self dropped for the trip: ", StringComparison.Ordinal)));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("the spellbook is not read yet: the heal's level is not judged", StringComparison.Ordinal));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    [Theory]
    [InlineData(CastEnd.Landed, true)]
    [InlineData(CastEnd.Unproven, true)]
    [InlineData(CastEnd.Fizzled, true)]
    [InlineData(CastEnd.Busy, true)]
    [InlineData(CastEnd.ManaShort, true)]
    [InlineData(CastEnd.NoComponents, false)]
    [InlineData(CastEnd.ServerError, false)]
    [InlineData(CastEnd.NoEffect, false)]
    [InlineData(CastEnd.Refused, false)]
    [InlineData(CastEnd.Timeout, false)]
    public void M6_6_FXD1_only_landed_unproven_fizzled_busy_and_mana_short_keep_the_heal(CastEnd end, bool keeps) => Assert.Equal(keeps, DeathHandler.HealEndKeeps(end));

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
        rig.R.H.State.Self.Spellbook.Learn(DeathScenario.HealSelfVii); // the den mage knows it (M6 6 FXD1: the read book is checked before the cast)
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

    // ------------------------------------------------------------------------------------------------ M6 6 C1: death at a low rung

    [Fact]
    public void M6_6_C1_an_untied_refusal_asks_the_travel_rule_again_and_goes_by_the_rung_change_with_no_runner_stop()
    {
        // The Temple with its tie not known and Primary Portal Recall in the read book: the travel rule sends the corpse trip to a recall; ACE refuses it 0x04A3. The refusal teaches the tie (untied), so the rule, asked again,
        // says the rung change: route=rung-change through the travel seam's Ground leg, never the runner's TODO stop (the leg's own failure here is the entry's level gate: this rig's level is below it).
        const string ground = "tusker-temple-fort-tethana";
        var rig = Died(ground, r => r.Tied = false);
        var book = rig.R.H.State.Self.Spellbook;
        book.IsKnown = true;
        book.Learn(DenTravelStation.PortalRecallSpell);
        var travel = new JourneyTravel("Winnie", rig.Facts, rig.Log, new RigRunner(rig.Travel), dungeon: rig.Ground);
        _ = new JourneyContext { Character = "Winnie", Account = "test", Log = rig.Log, Travel = travel, Spells = rig.Caster.Table, Dungeon = rig.Ground, Facts = rig.Facts };
        var handler = new DeathHandler(rig.Ground, rig.Tally, travel, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts, Worn = rig.Worn }, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(handler));
        Dump(rig, handler);

        Assert.Equal(1, rig.Recalls); // the recall was tried (the tie was not known) and refused
        Assert.Equal(PortalTie.Untied, rig.Facts.PortalTieOf(ground));
        Assert.Equal(DeathRoute.RungChange, handler.Route);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("0x04A3 YouMustLinkToPortalToRecall): the tie is now known untied and the travel rule, asked again, says the rung change", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("rung_change: ground=tusker-temple-fort-tethana", StringComparison.Ordinal));
        Assert.StartsWith("route=rung-change: travel:rung-change:", handler.Failure?.Replace("handler: ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(m6-first-tie-walk)", handler.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(Steps(rig), s => s == "runner");
    }

    /// <summary>The gate's named test (map-death section 7): a death on the ladder's two low rungs, each at its level, with ACE's item rule for that level and the respawn's 75% vitals.</summary>
    public static TheoryData<string, int> LowRungs => new() { { "holtburg-outskirts", 6 }, { "tusker-burrow", 12 } };

    [Theory]
    [MemberData(nameof(LowRungs))]
    public void M6_6_C1_a_death_at_a_low_rung_needs_no_trip_below_level_11_and_brings_a_dropped_item_back_by_the_rung_change_above_it(string ground, int level)
    {
        const uint charge = 0x80000220; // an ordinary pack item, the one a level-12 death drops (ACE: 0 or 1 unwielded item at 11-20, Player_Death.cs 650-660 @95ef426d)
        var rig = new HandlerRig(ground, spare: false, kitless: true, level: level); // the climber's kit is none on both rungs (the profiles' supplies)
        rig.PackItem(charge, "Mana Charge", 2434);
        rig.Drops.Clear();
        if (level > DeathDropRule.NoItemLevel)
        {
            rig.Drops.Add(charge);
        }

        rig.Worn.Snapshot(rig.R.H.State); // the worn set while the mage lived (the autopilot's snapshot)
        Assert.Equal(5, rig.Worn.Set.Count);
        IJourneyTravel travel = rig.Travel;
        if (ground == "tusker-burrow")
        {
            rig.ExactStops = true;
            rig.Portals(TravelRoutes.Default, "holtburg-to-network", "network-to-refuge");
            rig.Entry();
            var journey = new JourneyTravel("Winnie", rig.Facts, rig.Log, new RigRunner(rig.Travel), dungeon: rig.Ground);
            _ = new JourneyContext { Character = "Winnie", Account = "test", Log = rig.Log, Travel = journey, Spells = rig.Caster.Table, Dungeon = rig.Ground, Facts = rig.Facts, Grounds = [rig.Ground] };
            travel = journey;
        }

        rig.Die();
        rig.Settle();
        var options = new DeathOptions { Facts = rig.Facts, Worn = rig.Worn };
        var handler = new DeathHandler(rig.Ground, rig.Tally, travel, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, options, rig.Log);
        Assert.Same(rig.Worn, handler.WornSet); // the contract: the caller's memory, never the process-wide one
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(handler.Failure);
        Assert.Equal(DeathRearm.None, handler.Rearm); // below 35 the wielded wand stays in the hand (Player_Death.cs 489 @95ef426d)
        Assert.True(rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out _));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith(string.Create(CultureInfo.InvariantCulture, $"handler: corpse check: level={level} "), StringComparison.Ordinal) && e.Message.Contains(" worn_before=5 worn_missing=0 caster_kept=yes ", StringComparison.Ordinal));
        Assert.Equal(4, handler.Kills); // the climb resumed
        Assert.Contains(" vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
        NoModVerb(rig);
        if (level <= DeathDropRule.NoItemLevel)
        {
            Assert.Equal(DeathRoute.Walk, handler.Route); // planned (an outdoor ground hunted from town on foot)...
            Assert.Empty(rig.Travel.Requests); // ...and not walked (this host's seam is no journey travel: the fight's check before names TODO(m6-death-fight-outside-ground); the journey's way back is M6_6_FXD1_route_walk_*)
            Assert.Equal((DeathCorpse.NoneNeeded, DeathLegVerdict.TripAbortNoneNeeded), (handler.Corpse, handler.TripAbort));
            Assert.Equal(0, handler.ItemsBack);
            Assert.Equal(["respawn", "rebuff", "heal", "topup", "route", "wear", "relines", "fight", "done"], Steps(rig));
            Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("TODO(m6-death-fight-outside-ground)", StringComparison.Ordinal));
            Assert.Contains(rig.Log.Entries, e => e.Message.Contains("retained_line=yes", StringComparison.Ordinal) && e.Message.Contains("-> none-needed: ACE's line says every item was retained (level 6 <= 10", StringComparison.Ordinal));
            Assert.Contains("deaths=1 corpse_recovered=none-needed route=walk items_back=0 vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(DeathRoute.RungChange, handler.Route); // untieable (NoRecall): the rung change through the journey's travel, decided before the act
            Assert.Equal(0, rig.Recalls);
            Assert.Contains(rig.Log.Entries, e => e.Message.Contains("rung_change: ground=tusker-burrow", StringComparison.Ordinal));
            Assert.Contains(rig.Log.Entries, e => e.Message.Contains("lost_line='You've lost your Mana Charge!'", StringComparison.Ordinal) && e.Message.Contains("-> walk: ACE's line lists 1 item(s) but pyreals (level 12 > 10", StringComparison.Ordinal));
            Assert.Equal(DeathCorpse.Yes, handler.Corpse);
            Assert.Equal(1, handler.ItemsBack);
            Assert.Equal([charge], rig.PickUps);
            Assert.True(rig.R.H.State.Self.Inventory.Contains(charge), "the item is back in the pack");
            Assert.Contains(rig.Travel.Requests, r => r.Purpose == TravelPurpose.Corpse && r.ObjectGuid == rig.CorpseGuid);
            Assert.Contains("deaths=1 corpse_recovered=yes route=rung-change items_back=1 vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(6, true, null, true, 5, 0, true, true, "ACE's line says every item was retained")]
    [InlineData(6, false, "You've lost 120 Pyreals!", true, 5, 0, true, true, "ACE's line lists pyreals only")]
    [InlineData(6, false, null, true, 5, 0, true, true, "no drop line from ACE, and the level rule agrees with the worn side")]
    [InlineData(6, false, null, true, 0, 0, true, false, "the worn set before the death is not known (an empty memory): the diff cannot be read")]
    [InlineData(6, false, null, false, 5, 0, true, false, "the chat cannot be tied to this death")]
    [InlineData(6, false, "You've lost your Slippery Ring!", true, 5, 0, true, false, "ACE's line lists 1 item(s) but pyreals (level 6 <= 10: ACE drops no item but a Slippery one; against the level rule: a Slippery item)")]
    [InlineData(12, true, null, true, 5, 0, true, true, ": an empty roll")]
    [InlineData(12, false, "You've lost 1,234 Pyreals, and your Mana Charge!", true, 5, 0, true, false, "ACE's line lists 1 item(s) but pyreals")]
    [InlineData(12, false, null, true, 5, 0, true, false, "no drop line from ACE and level 12 > 10: ACE may drop items: the pack side cannot be read")]
    [InlineData(40, true, null, true, 5, 1, true, false, "1 of the 5 pieces worn before the death are not held")]
    [InlineData(40, true, null, true, 5, 0, false, false, "the wielded caster left the hands at the death")]
    [InlineData(null, false, null, true, 5, 0, true, false, "no drop line from ACE and level unknown: the pack side cannot be read")]
    public void M6_6_C1_the_drop_rule_reads_ACEs_lines_and_the_worn_side_cross_checked_with_the_level_and_walks_when_the_diff_is_unread(int? level, bool retained, string? lost, bool linesRead, int wornBefore, int wornMissing, bool casterKept, bool noneNeeded, string why)
    {
        var decision = DeathDropRule.Decide(new DeathDropInput(level, retained, lost, linesRead, wornBefore, wornMissing, casterKept));

        Assert.Equal(noneNeeded, decision.NoneNeeded);
        Assert.Contains(why, decision.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_6_C1_the_corpse_trips_fight_keeps_the_whole_walk_seam_of_the_ground_walk()
    {
        // KeptWalk (the trip fight's view of the handler's walk) forwards the approach trace, the defence spots and the avoided spot to the walk it keeps, not the interface's defaults (map-death section 6).
        var rig = Died("black-spawn-den-aluvian", r => r.Facts.SetPortalTie("black-spawn-den-aluvian", PortalTie.Tied));
        var seam = new SeamWalk();
        var handler = new DeathHandler(rig.Ground, rig.Tally, rig.Travel, rig.LegFactory, seam, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts, Worn = rig.Worn }, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);
        var corpse = handler.Facts.CorpsePosition!.Value;
        const uint tusker = 0x80000779;
        var h = rig.R.H.R;
        var basis = ReducerHarness.Create(tusker, ReducerHarness.Stamps(), ReducerHarness.Position(corpse.CellId, corpse.Frame.Origin.X + 3, corpse.Frame.Origin.Y, corpse.Frame.Origin.Z), name: "Tusker Slave");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = rig.Ground.TargetWcids()[0] } }, isUpdate: false);
        IFightWalk? tripWalk = null;
        rig.OnInner = (stages, _) =>
        {
            if (stages == FightStages.Mode && handler.CurrentStep == "trip-fight" && tripWalk is null)
            {
                tripWalk = rig.InnerWalks[^1];
                h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(tusker, 1)); // the trip fight's kill
            }
        };
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(handler));

        Assert.NotNull(tripWalk);
        Assert.NotSame(seam, tripWalk); // the trip fight's view of the walk (the handler keeps the walk itself)
        var at = new CellFrame(0x01050100, default);
        var cells = new List<uint>();
        Assert.Equal(LineTrace.Traced, tripWalk!.CellsOnLine(in at, in at, cells));
        Assert.Equal([0x01050100u, 0x01050101u], cells);
        Assert.Same(seam.Spots, tripWalk.FloorSpots);
        Assert.True(tripWalk.AvoidSpot(in at, 9, out var avoided));
        Assert.Equal("seam-avoid", avoided);
    }

    /// <summary>A walk whose seam members answer, so a forward is told from the interface's defaults.</summary>
    private sealed class SeamWalk : IFightWalk
    {
        public IReadOnlyList<CellFrame> Spots { get; } = [new CellFrame(0x01050102, default)];

        public string? Failure => null;

        public void Prepare(IGameSessionView session, double now)
        {
        }

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = "seam";
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now) => FightWalkStatus.Idle;

        public void Abandon()
        {
        }

        public void Dispose()
        {
        }

        public LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells)
        {
            cells.Clear();
            cells.Add(0x01050100);
            cells.Add(0x01050101);
            return LineTrace.Traced;
        }

        public IReadOnlyList<CellFrame> FloorSpots => Spots;

        public bool AvoidSpot(in CellFrame at, double until, out string description)
        {
            description = "seam-avoid";
            return true;
        }
    }

    // ------------------------------------------------------------------------------------------------ M6 6 C2: a corpse lost on a leg

    /// <summary>
    /// M6 6 C2: the climber (level 12, no kit) dies on a leg, off the Burrow's landblocks, at <paramref name="deathCell"/>; the travel is the journey's over the travel file with the island's and the Network's portals answering a Use and the
    /// Burrow's entry (the rung change back to the rung after the corpse), the walks stopping on their points. <paramref name="drop"/>: ACE drops the pack's Mana Charge to the corpse (else nothing, the retained line).
    /// </summary>
    private static (HandlerRig Rig, DeathHandler Handler, uint Charge) LegDeath(uint deathCell, Vector3 at, bool drop, double abortSeconds = 90, bool fight = true, (uint Cell, Vector3 At)? standBefore = null)
    {
        const uint charge = 0x80000220;
        var rig = new HandlerRig("tusker-burrow", spare: false, kitless: true, level: 12);
        rig.PackItem(charge, "Mana Charge", 2434);
        rig.Drops.Clear();
        if (drop)
        {
            rig.Drops.Add(charge);
        }

        rig.Worn.Snapshot(rig.R.H.State);
        rig.ExactStops = true;
        rig.Portals(TravelRoutes.Default, "holtburg-to-network", "network-to-refuge", "refuge-to-network", "network-to-holtburg");
        rig.Entry();
        var journey = new JourneyTravel("Winnie", rig.Facts, rig.Log, new RigRunner(rig.Travel), dungeon: rig.Ground);
        _ = new JourneyContext { Character = "Winnie", Account = "test", Log = rig.Log, Travel = journey, Spells = rig.Caster.Table, Dungeon = rig.Ground, Facts = rig.Facts, Grounds = [rig.Ground] };
        rig.StandAt(deathCell, at); // the run on the leg: the mage dies here, off the ground
        rig.Die("Armored Tusker");
        rig.Settle();
        if (standBefore is { } stand)
        {
            rig.StandAt(stand.Cell, stand.At); // M6 7 U: a station entered again: the body is not where the tally's respawn says
        }

        var handler = new DeathHandler(rig.Ground, rig.Tally, journey, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts, Worn = rig.Worn, AbortSeconds = abortSeconds, Fight = fight }, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);
        return (rig, handler, charge);
    }

    private static bool InOrder(List<string> steps, params string[] order)
    {
        var at = -1;
        foreach (var step in order)
        {
            var next = steps.IndexOf(step);
            if (next <= at)
            {
                return false;
            }

            at = next;
        }

        return true;
    }

    /// <summary>
    /// M6 16 Q2: on a travel leg the corpse leg reads its attackers present off the threat board (its own question: 40 m, 10 s, every attack heard, from her server position; the board's answer to that question is held against
    /// the old read through a watch, and its remembered attack, in <c>ThreatShadowTests</c>, whose travel-leg rig asks the very same question). With nothing attacking her the trip runs as before, and its lane is gone.
    /// </summary>
    [Fact]
    public void M6_16_Q2_the_corpse_leg_reads_its_attackers_off_the_threat_board_and_its_lane_is_gone()
    {
        var (rig, handler, _) = LegDeath(0xF6820021, new Vector3(100, 100, 12), drop: true);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(handler));
        Assert.Equal(DeathRoute.Leg, handler.Route);
        handler.Dispose();

        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("death-corpse-leg", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse leg: run past: ", StringComparison.Ordinal)); // nobody attacked her: nothing to judge, as before
    }

    [Fact]
    public void M6_6_C2_a_death_on_the_island_run_goes_by_route_leg_to_the_island_walks_to_the_corpse_and_goes_back_to_the_rung()
    {
        var (rig, handler, charge) = LegDeath(0xF6820021, new Vector3(100, 100, 12), drop: true);
        Assert.Equal(0xF682u, handler.Facts.CorpsePosition!.Value.CellId >> 16); // the corpse lies on the island, off the Burrow

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(handler.Failure);
        Assert.Equal(DeathRoute.Leg, handler.Route);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("route=leg (the corpse 0x", StringComparison.Ordinal) && e.Message.Contains("place=island: the travel file's place island holds the corpse's landblock 0xF682", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("journey travel: to the corpse's place island", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("rung_change: ground=tusker-burrow level=12 route=by-refuge@island hops=2 ", StringComparison.Ordinal) && e.Message.Contains(" ok=yes fail=none direction=out stop_at=island", StringComparison.Ordinal));
        var corpseLeg = rig.Travel.Requests.FindIndex(r => r.Purpose == TravelPurpose.Corpse && r.ObjectGuid == rig.CorpseGuid);
        Assert.True(corpseLeg >= 0, "the corpse leg on foot");
        Assert.Equal(0xF682u, rig.Travel.Requests[corpseLeg].Destination.Location.Position.CellId >> 16);
        Assert.Equal((DeathCorpse.Yes, 1), (handler.Corpse, handler.ItemsBack));
        Assert.Equal([charge], rig.PickUps);
        var steps = Steps(rig);
        Assert.True(InOrder(steps, "leg", "corpse-walk", "wear", "relines", "rung-back", "fight", "done"), string.Join(",", steps));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("rung_change: ground=tusker-burrow level=12 route=by-refuge ", StringComparison.Ordinal) && e.Message.EndsWith("ok=yes fail=none direction=out", StringComparison.Ordinal)); // back to the rung by the normal rung change
        Assert.True(rig.Ground.InLandblocks(rig.R.H.State.Self.Position!.Value.CellId), "the fight is in the Burrow");
        Assert.Equal(4, handler.Kills);
        Assert.Equal(0, rig.Recalls);
        Assert.Contains("deaths=1 corpse_recovered=yes route=leg items_back=1 vitae_grants=0 ", handler.Fields, StringComparison.Ordinal);
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse check: level=12 ", StringComparison.Ordinal)); // the drop rule read once, before the trip
        NoModVerb(rig);
    }

    [Fact]
    public void M6_6_C2_a_corpse_in_no_place_of_the_travel_file_is_named_corpse_off_route_and_the_mage_goes_back_to_the_rung()
    {
        var (rig, handler, _) = LegDeath(0xC0C00001, new Vector3(12, 12, 0), drop: true);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRoute.Leg, handler.Route);
        Assert.Equal((DeathCorpse.No, DeathHandler.CorpseOffRouteAbort), (handler.Corpse, handler.TripAbort));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("handler: corpse_recovered=no: the corpse 0x", StringComparison.Ordinal) && e.Message.Contains("lies in landblock 0xC0C0", StringComparison.Ordinal) && e.Message.Contains("trip_abort=corpse-off-route", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("journey travel: to the corpse's place", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Travel.Requests, r => r.Purpose == TravelPurpose.Corpse);
        Assert.True(InOrder(Steps(rig), "wear", "relines", "rung-back", "fight", "done"), string.Join(",", Steps(rig)));
        Assert.True(rig.Ground.InLandblocks(rig.R.H.State.Self.Position!.Value.CellId), "back on the rung");
        Assert.Equal(4, handler.Kills);
    }

    [Fact]
    public void M6_6_C2_a_leg_death_with_nothing_dropped_makes_no_trip_to_the_corpse_and_goes_to_the_rung_as_any_death()
    {
        var (rig, handler, _) = LegDeath(0xF6820021, new Vector3(100, 100, 12), drop: false);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal((DeathCorpse.NoneNeeded, DeathLegVerdict.TripAbortNoneNeeded), (handler.Corpse, handler.TripAbort));
        Assert.Equal(DeathRoute.RungChange, handler.Route); // decided as for any death: the rung change to the ground, no route=leg
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("(a death on a leg), and nothing is to recover: no trip to it", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("journey travel: to the corpse's place", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Travel.Requests, r => r.Purpose == TravelPurpose.Corpse);
        Assert.DoesNotContain("leg", Steps(rig));
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse check: level=12 ", StringComparison.Ordinal) && e.Message.Contains("-> none-needed", StringComparison.Ordinal));
        Assert.True(rig.Ground.InLandblocks(rig.R.H.State.Self.Position!.Value.CellId));
        Assert.Equal(4, handler.Kills);
    }

    [Theory]
    [InlineData(2, true)] // both walks of the first hop (the facing front and its front-wall recovery): the rung change fails once
    [InlineData(99, false)]
    public void M6_6_C2_a_failed_route_travel_is_decided_again_and_started_once_more_and_a_second_failure_is_the_named_stop(int failedWalks, bool recovered)
    {
        // The lead's live round 1 (cache/m6/6/live/r1-lowrung.log): after a death on the island run the corpse route's rung change failed once at the Town Network walk ("moved 24.9 m in one step") and the corpse was never gone back for.
        // One named recovery: the route decided again from where the body stands and started once more, logged; a second failure stays the named stop.
        const uint charge = 0x80000220;
        var rig = new HandlerRig("tusker-burrow", spare: false, kitless: true, level: 12);
        rig.PackItem(charge, "Mana Charge", 2434);
        rig.Drops.Clear();
        rig.Drops.Add(charge);
        rig.Worn.Snapshot(rig.R.H.State);
        rig.ExactStops = true;
        rig.Portals(TravelRoutes.Default, "holtburg-to-network", "network-to-refuge", "network-to-holtburg");
        rig.Entry();
        var journey = new JourneyTravel("Winnie", rig.Facts, rig.Log, new RigRunner(rig.Travel), dungeon: rig.Ground);
        _ = new JourneyContext { Character = "Winnie", Account = "test", Log = rig.Log, Travel = journey, Spells = rig.Caster.Table, Dungeon = rig.Ground, Facts = rig.Facts, Grounds = [rig.Ground] };
        rig.Die();
        rig.Settle();
        var handler = new DeathHandler(rig.Ground, rig.Tally, journey, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts, Worn = rig.Worn }, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);
        rig.Travel.FailCells = failedWalks;

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(DeathRoute.RungChange, handler.Route);
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: route retry 1 of 1 after route=rung-change: travel:rung-change:", StringComparison.Ordinal));
        if (recovered)
        {
            Assert.Equal(ScenarioStatus.Succeeded, status);
            Assert.Equal((DeathCorpse.Yes, 1), (handler.Corpse, handler.ItemsBack));
            Assert.Equal(4, handler.Kills);
        }
        else
        {
            Assert.Equal(ScenarioStatus.Failed, status);
            Assert.StartsWith("route=rung-change: travel:rung-change:", handler.Failure, StringComparison.Ordinal);
            Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: no route retry (the 1 retry is spent) after route=rung-change: ", StringComparison.Ordinal));
            Assert.Equal(DeathCorpse.No, handler.Corpse);
        }
    }

    // ------------------------------------------------------------------------------------------------ M6 7 U: the night that a death ended (r1f-climb)

    /// <summary>The cells of r1f-climb's two deaths, level 12 here: the island run's, off the ground, and the Burrow's, on it.</summary>
    public static TheoryData<uint, float, float, float> DeathCells => new() { { 0xF6820021, 100f, 100f, 12f }, { 0x5748020B, 148f, -40f, 0f } };

    [Theory]
    [MemberData(nameof(DeathCells))]
    public void M6_7_U_nothing_owed_and_no_fight_of_its_own_makes_no_trip_the_drop_rule_is_read_before_any_walk(uint cell, float x, float y, float z)
    {
        var (rig, handler, _) = LegDeath(cell, new Vector3(x, y, z), drop: false, fight: false);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal((DeathCorpse.NoneNeeded, DeathLegVerdict.TripAbortNoneNeeded), (handler.Corpse, handler.TripAbort));
        Assert.Equal(DeathRoute.RungChange, handler.Route); // the plan stays on the line...
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("rung_change plan:", StringComparison.Ordinal)); // ...and no leg of it ran
        Assert.DoesNotContain("rung-change", Steps(rig));
        Assert.Empty(rig.Travel.Requests);
        Assert.False(rig.Ground.InLandblocks(rig.R.H.State.Self.Position!.Value.CellId)); // the body stands at the lifestone: the known state takes the way out
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse check: level=12 ", StringComparison.Ordinal));
        Assert.True(IndexOf(rig, "handler: corpse check: level=12 ") < IndexOf(rig, "handler: nothing is owed (the drop rule, read before any trip, D67)"));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: --death-fight is off: the handler ends where the body stands (cell 0xA9B4", StringComparison.Ordinal) && e.Message.Contains("off the ground", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_U_with_an_item_owed_the_trip_still_goes_and_the_body_stays_on_the_ground_after_it()
    {
        var (rig, handler, charge) = LegDeath(0x5748020B, new Vector3(148, -40, 0), drop: true, fight: false);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Contains("rung-change", Steps(rig));
        Assert.Contains(rig.Travel.Requests, r => r.Purpose == TravelPurpose.Corpse);
        Assert.True(rig.R.H.State.Self.Inventory.Contains(charge));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: --death-fight is off: the handler ends where the body stands (cell 0x5748", StringComparison.Ordinal) && e.Message.Contains("on the ground", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)] // r1f-climb's re-entry: nothing owed
    [InlineData(true)] // the corpse is owed and lies on the ground: the walk goes from where the body stands
    public void M6_7_U_a_station_entered_again_with_the_body_on_the_ground_takes_no_route_and_never_the_portal_sections_stop(bool drop)
    {
        var (rig, handler, _) = LegDeath(0x5748020B, new Vector3(148, -40, 0), drop, fight: false, standBefore: (0x57480210, new Vector3(148.38f, -2.03f, 0.01f)));

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(handler.Failure);
        Assert.Equal(DeathRoute.Here, handler.Route);
        Assert.DoesNotContain("runner", Steps(rig));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("TODO(m6-first-tie-walk)", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.Contains("rung_change plan:", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("plan: route=here (the body already stands in Tusker Burrow (cell 0x57480210)", StringComparison.Ordinal));
        Assert.Equal(drop ? DeathCorpse.Yes : DeathCorpse.NoneNeeded, handler.Corpse);
        Assert.Equal(drop, rig.Travel.Requests.Any(r => r.Purpose == TravelPurpose.Corpse));
    }

    [Fact]
    public void M6_7_U_a_ground_with_no_route_at_all_keeps_the_named_stop_when_the_corpse_is_owed()
    {
        // The Burrow's route section gone from the travel file's reach: the rig's travel seam has no travel rule to ask, the ground has no portal: the runner's named stop stays (it is the only arm left)
        var rig = new HandlerRig("tusker-burrow", spare: false, kitless: true, level: 12);
        const uint charge = 0x80000220;
        rig.PackItem(charge, "Mana Charge", 2434);
        rig.Drops.Clear();
        rig.Drops.Add(charge);
        rig.Worn.Snapshot(rig.R.H.State);
        rig.Die();
        rig.Settle();
        var handler = rig.Handler(new DeathOptions { Facts = rig.Facts, Worn = rig.Worn, Fight = false });
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("TODO(m6-first-tie-walk)", handler.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_7_U_the_autopilots_station_stays_on_the_ground_and_a_death_it_only_observed_is_not_judged_on_its_hits()
    {
        // r1f-climb: stage leave-den from the ground the climb wanted (rooms-no-path at once), then verdict ok=false hits_taken=0: a Female Tusker's one blow sends the death message and no hit notice (@95ef426d Player_Combat.cs 526-527, 542)
        const string ground = "black-spawn-den-aluvian";
        var rig = Died(ground, r => r.Facts.SetPortalTie(ground, PortalTie.Tied));
        Assert.Equal(0, rig.Tally.HitsTaken);
        rig.Facts.ObservedDeath = rig.Tally;
        var options = new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false, Fight = false, Travel = rig.Travel, Facts = rig.Facts };
        using var scenario = new DeathScenario(rig.Kit, rig.Caster.Table, options, rig.Walk, rig.Log, endSession: false, stages: DeathStages.Death | DeathStages.Rebuff | DeathStages.Corpse, fightFactory: rig.LegFactory, fight: new FightOptions { Kills = 4, Ground = ground });

        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            rig.Frame(null, ref status);
            status = scenario.Step(rig.View, rig.Now);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(rig.Ground.InLandblocks(rig.R.H.State.Self.Position!.Value.CellId));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("leaving the den", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("the body stands on it and the caller's known state takes the next move", StringComparison.Ordinal));
        Assert.True(scenario.Outcome().Ok, scenario.Outcome().Line);
        Assert.Contains(" hits_taken=0 ", scenario.Outcome().Line, StringComparison.Ordinal); // printed, not judged
    }

    // ------------------------------------------------------------------------------------------------ M6 6 FXD1: the leg's clock, the fight's check before, the way back's retry, the worn side

    [Fact]
    public void M6_6_FXD1_on_route_leg_the_trips_clock_adds_the_corpse_legs_straight_line_and_names_the_place()
    {
        // 6-D3: the clock starts at the corpse's travel place (the island), not in the ground; with --death-recover-abort-seconds 1 the corpse leg alone outlasts the bare limit, so only the allowance lets the corpse come back.
        var (rig, handler, charge) = LegDeath(0xF6820021, new Vector3(100, 100, 12), drop: true, abortSeconds: 1);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRoute.Leg, handler.Route);
        var clock = Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: route=leg: the trip's clock starts in island (cell 0x", StringComparison.Ordinal)).Message;
        Assert.Contains("the limit is --death-recover-abort-seconds 1 s plus ", clock, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"at {DeathHandler.LegCorpseMinMetresPerSecond:0.0} m/s"), clock, StringComparison.Ordinal);
        Assert.True(handler.TripSeconds > 1, "the trip outlasted the bare limit");
        Assert.Equal((DeathCorpse.Yes, DeathLegVerdict.TripAbortNone), (handler.Corpse, handler.TripAbort));
        Assert.Equal([charge], rig.PickUps);
        Assert.Equal(WayfindScenario.AssumedSpeedMetresPerSecond / WayfindScenario.RouteFactor, DeathHandler.LegCorpseMinMetresPerSecond);
    }

    [Fact]
    public void M6_6_FXD1_route_walk_with_nothing_to_recover_goes_back_to_the_rung_before_the_fight_and_fights_in_the_ground()
    {
        // 6-D6: on the outskirts (route=walk) a level-6 death needs no trip; the respawned mage stands at the Holtburg lifestone, outside the ground, so the fight's check before sends it back to the rung first (checked after).
        var rig = new HandlerRig("holtburg-outskirts", spare: false, kitless: true, level: 6);
        rig.Drops.Clear();
        rig.Worn.Snapshot(rig.R.H.State);
        rig.Die();
        rig.Settle();
        var respawn = rig.R.H.State.Self.Position!.Value.CellId;
        Assert.False(rig.Ground.InLandblocks(respawn)); // the lifestone is outside the ground's landblocks
        rig.ExactStops = true;
        var journey = new JourneyTravel("Winnie", rig.Facts, rig.Log, new RigRunner(rig.Travel), dungeon: rig.Ground); // the journey's travel: its Ground leg is the rung change (here on foot from the town)
        _ = new JourneyContext { Character = "Winnie", Account = "test", Log = rig.Log, Travel = journey, Spells = rig.Caster.Table, Dungeon = rig.Ground, Facts = rig.Facts, Grounds = [rig.Ground] };
        var handler = new DeathHandler(rig.Ground, rig.Tally, journey, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, new DeathOptions { Facts = rig.Facts, Worn = rig.Worn }, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);
        var fightAt = 0u;

        var status = RunWith(rig, handler, () =>
        {
            if (fightAt == 0 && handler.CurrentStep == "fight")
            {
                fightAt = rig.R.H.State.Self.Position!.Value.CellId;
            }
        });
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal((DeathRoute.Walk, DeathCorpse.NoneNeeded), (handler.Route, handler.Corpse));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith(string.Create(CultureInfo.InvariantCulture, $"handler: before the fight: the mage stands in cell 0x{respawn:X8}, outside "), StringComparison.Ordinal) && e.Message.EndsWith("(route=walk): back to the rung first", StringComparison.Ordinal));
        Assert.True(rig.Ground.InLandblocks(fightAt), string.Create(CultureInfo.InvariantCulture, $"the fight began in the ground, not at 0x{fightAt:X8}"));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: back to the rung: the rung change brought the mage into ", StringComparison.Ordinal));
        Assert.Equal(4, handler.Kills);
    }

    [Fact]
    public void M6_6_FXD1_the_way_back_to_the_rung_has_its_own_retry_after_one_spent_on_the_way_out()
    {
        // 6-D13: a retry spent on the way out to the corpse's place no longer fails a station whose items are already back: the way back counts its own (RouteRetries, 1).
        var (rig, handler, charge) = LegDeath(0xF6820021, new Vector3(100, 100, 12), drop: true);
        rig.Travel.FailCells = 2; // the way out's first hop fails once
        var failedBack = false;

        var status = RunWith(rig, handler, () =>
        {
            if (!failedBack && handler.CurrentStep == "rung-back")
            {
                failedBack = true;
                rig.Travel.FailCells = 2; // and the way back's first hop fails once
            }
        });
        Dump(rig, handler);

        Assert.True(failedBack);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: route retry 1 of 1 after route=leg: ", StringComparison.Ordinal));
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: route retry 1 of 1 (the way back to the rung's own) after route=leg: back to the rung: ", StringComparison.Ordinal));
        Assert.Equal((DeathCorpse.Yes, 1), (handler.Corpse, handler.ItemsBack));
        Assert.Equal([charge], rig.PickUps);
        Assert.True(rig.Ground.InLandblocks(rig.R.H.State.Self.Position!.Value.CellId), "back on the rung");
        Assert.Equal(4, handler.Kills);
    }

    [Theory]
    [InlineData(true, null, 0, "every item was retained", "the worn side unread")]
    [InlineData(true, null, 5, "every item was retained", "the worn side agrees")]
    [InlineData(false, "You've lost 120 Pyreals!", 0, "pyreals only", "the worn side unread")]
    [InlineData(false, "You've lost 120 Pyreals!", 5, "pyreals only", "the worn side agrees")]
    public void M6_6_FXD1_the_drop_rule_says_the_worn_side_is_unread_when_the_worn_memory_is_empty(bool retained, string? lost, int wornBefore, string line, string worn)
    {
        // 6-D11: every journey that passes no worn memory has WornBefore 0; "the worn side agrees" was a field that did not read what it named.
        var decision = DeathDropRule.Decide(new DeathDropInput(6, retained, lost, true, wornBefore, 0, true));

        Assert.True(decision.NoneNeeded);
        Assert.Contains(line, decision.Why, StringComparison.Ordinal);
        Assert.Contains(worn, decision.Why, StringComparison.Ordinal);
        Assert.DoesNotContain(worn == "the worn side agrees" ? "the worn side unread" : "the worn side agrees", decision.Why, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ M6 7 AH: the replace step

    private const uint WandGuidOfRig = 0x80000200;

    /// <summary>M6 7 AH: a level-40 mage on the outskirts (no kit, no portal) that died with the named pieces going to the corpse; its vendors stocked with <paramref name="coins"/> pyreals (negative: no vendors, no journey context).</summary>
    private static (HandlerRig Rig, uint Gloves) ReplaceRig(bool dropGloves, bool dropWand, int coins, Action<HandlerRig>? setup = null)
    {
        var rig = new HandlerRig("holtburg-outskirts", spare: false, kitless: true, level: 40);
        var gloves = rig.R.H.State.Self.Inventory.Equipped.First(e => (e.Location & (uint)EquipMask.HandWear) != 0).Guid;
        rig.Worn.Snapshot(rig.R.H.State);
        rig.Drops.Clear();
        if (dropGloves)
        {
            rig.Drops.Add(gloves);
        }

        if (dropWand)
        {
            rig.Drops.Add(WandGuidOfRig);
        }

        if (coins >= 0)
        {
            rig.StockVendors(coins);
        }

        setup?.Invoke(rig);
        rig.Die();
        rig.Settle();
        return (rig, gloves);
    }

    private static int Count(HandlerRig rig, string prefix) => rig.Log.Entries.Count(e => e.Message.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void M6_7_AH_nothing_missing_is_replace_none_with_no_step_no_walk_and_no_buy()
    {
        var (rig, _) = ReplaceRig(dropGloves: false, dropWand: false, coins: 100_000);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("none", handler.Replace);
        Assert.DoesNotContain("replace", Steps(rig)); // no step entered: no walk
        Assert.Equal(0, rig.Shops.Buys);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("replace=none: nothing of the floor's robe, gauntlets or caster is missing", StringComparison.Ordinal));
        Assert.EndsWith(" rewear=none replace=none", handler.Fields, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Travel.Requests, r => r.Purpose == TravelPurpose.Vendor);
    }

    [Fact]
    public void M6_7_AH_gauntlets_missing_are_bought_at_the_vendor_and_worn_before_the_rebuff_and_the_duplicate_stays_on_the_corpse()
    {
        var (rig, gloves) = ReplaceRig(dropGloves: true, dropWand: false, coins: 100_000);
        var vendor = rig.Ground.VendorSelling(56)!;
        var price = rig.Ground.PriceOf(56)!.Value;
        Assert.Equal(1485, price);
        Assert.False(GearFloor.HandsCoveredBy(SelfEquipJudge.Worn(rig.R.H.State)), "the hands are bare after the death");
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("gauntlets", handler.Replace);
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("replace") >= 0 && steps.IndexOf("replace") < steps.IndexOf("rebuff"), string.Join(",", steps)); // the replace step is before the rebuff
        var buy = Assert.Single(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.Buy);
        Assert.Equal(vendor.Guid, buy.Guid);
        Assert.Equal(100_000 - price, (int)rig.R.H.State.Self.Qualities.CoinValue);
        Assert.True(rig.R.H.State.Objects.TryGet(rig.Wields[0], out var pair) && pair.Weenie.WeenieClassId == 56, "the first wield is the bought pair");
        Assert.True(IndexOf(rig, "replace: after: gauntlets: now worn") is var worn and > 0 && worn < IndexOf(rig, "handler rebuff"));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("equip pass: check after: the hands are covered", StringComparison.Ordinal));
        Assert.Contains(" replace=gauntlets", handler.Fields, StringComparison.Ordinal);
        // the take-back: the corpse's gloves are left, named; no duplicate in the pack, not lost, not waited for
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("take-back: left 'Leather Boots' ", StringComparison.Ordinal) && e.Message.EndsWith(": replaced at the vendor", StringComparison.Ordinal));
        Assert.DoesNotContain(gloves, rig.PickUps);
        Assert.Equal(1, handler.LeftOnCorpse);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
        Assert.Equal(0, handler.ItemsBack);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("worn before the death was left on the corpse (replaced at the vendor): not counted", StringComparison.Ordinal));
        Assert.True(handler.GearBack, handler.WornFields);
        NoModVerb(rig);
    }

    [Fact]
    public void M6_7_AH_a_short_purse_is_replace_short_named_and_the_handling_goes_on_to_the_rebuff_and_the_trip()
    {
        var (rig, gloves) = ReplaceRig(dropGloves: true, dropWand: false, coins: 0);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("short", handler.Replace);
        Assert.Equal(0, rig.Shops.Buys);
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.StartsWith("replace=short:", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("is short of the Leather Gauntlets", StringComparison.Ordinal));
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("replace") < steps.IndexOf("rebuff"), string.Join(",", steps));
        Assert.Contains(gloves, rig.PickUps); // nothing replaced it: the corpse's pair comes back as before
        Assert.Equal(0, handler.LeftOnCorpse);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    [Fact]
    public void M6_7_AH_a_failed_walk_is_replace_failed_named_the_handling_goes_on_and_the_step_ran_once()
    {
        var (rig, gloves) = ReplaceRig(dropGloves: true, dropWand: false, coins: 100_000, r => r.Travel.FailVendorWalks = true);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("failed", handler.Replace);
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.StartsWith("replace=failed: gauntlets was in the plan and is still missing after the trip", StringComparison.Ordinal));
        Assert.Equal(1, Steps(rig).Count(st => st == "replace")); // one try: never a loop
        Assert.Equal(1, Count(rig, "replace: before:"));
        Assert.Equal(1, Count(rig, "replace: buy "));
        Assert.Equal(0, rig.Shops.Buys);
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("replace") < steps.IndexOf("rebuff"), string.Join(",", steps));
        Assert.Contains(gloves, rig.PickUps);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    [Fact]
    public void M6_7_AH_a_host_with_no_journey_context_cannot_shop_so_replace_failed_by_name_and_the_handling_goes_on()
    {
        var (rig, _) = ReplaceRig(dropGloves: true, dropWand: false, coins: 100_000, r => r.JourneyShop = false);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("failed", handler.Replace);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("TODO(m5-death-rearm-standalone)", StringComparison.Ordinal) && e.Message.StartsWith("replace=failed", StringComparison.Ordinal));
        Assert.Equal(0, rig.Shops.Buys);
    }

    [Fact]
    public void M6_7_AH_rearm_corpse_buys_the_floors_wand_first_wields_it_buffs_and_only_then_walks_to_the_corpse_and_the_original_wand_comes_back_as_the_spare()
    {
        var (rig, _) = ReplaceRig(dropGloves: false, dropWand: true, coins: 100_000);
        Assert.Null(CombatModeGuard.Read(rig.R.H.State).Caster);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRearm.Bought, handler.Rearm); // the corpse path became a rearm by the floor's wand
        Assert.Equal("caster", handler.Replace);
        Assert.Equal(1, rig.Shops.Buys);
        Assert.True(rig.R.H.State.Objects.TryGet(rig.Wields[0], out var wand) && wand.Weenie.WeenieClassId == rig.FloorWandWcid && (wand.ItemType & CasterType) != 0, "the first wield is the floor's wand");
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("replace") < steps.IndexOf("rebuff") && steps.IndexOf("rebuff") < steps.IndexOf("route"), string.Join(",", steps)); // the buff first, then the trip
        Assert.NotEqual(DeathLegVerdict.RebuffedSkipped, handler.RebuffedPretrip); // the rebuff ran before the trip
        Assert.Contains(WandGuidOfRig, rig.PickUps); // a caster is never left on the corpse: the original is the spare
        Assert.Equal(0, handler.LeftOnCorpse);
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    [Fact]
    public void M6_7_AH_rearm_corpse_falls_back_by_name_to_the_corpse_first_path_when_the_floors_wand_cannot_be_bought()
    {
        var (rig, _) = ReplaceRig(dropGloves: false, dropWand: true, coins: 0);
        var handler = Begun(rig);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(DeathRearm.Corpse, handler.Rearm);
        Assert.Equal("short", handler.Replace);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("replace: fallback by name: no caster is wielded after the replace step", StringComparison.Ordinal));
        var steps = Steps(rig);
        Assert.True(steps.IndexOf("replace") >= 0 && steps.IndexOf("route") > steps.IndexOf("replace"), string.Join(",", steps));
        Assert.Equal(DeathCorpse.Yes, handler.Corpse);
    }

    [Fact]
    public void M6_7_AH_the_replacement_rule_leaves_only_a_robe_or_gauntlets_whose_slot_is_filled_never_a_caster_and_never_with_nothing_replaced()
    {
        var rig = new HandlerRig("holtburg-outskirts", spare: false, kitless: true, level: 40);
        var state = rig.R.H.State;
        var pair = Item(rig, 0x80007001, 0x2, (uint)EquipMask.HandWear, 0x8000, 56);
        var robe = Item(rig, 0x80007002, 0x2, 0x7F01, 0x17F00, 5851);
        var wand = Item(rig, 0x80007003, CasterType, (uint)EquipMask.Held, 0, 2472);
        GearFloorKind[] both = [GearFloorKind.Robe, GearFloorKind.Gauntlets];
        // the scripted mage wears a vest and gloves: both slots are filled
        Assert.True(DeathReplace.LeftOnCorpse(pair, both, state));
        Assert.True(DeathReplace.LeftOnCorpse(robe, both, state));
        Assert.False(DeathReplace.LeftOnCorpse(wand, [GearFloorKind.Caster, .. both], state)); // a caster is the spare
        Assert.False(DeathReplace.LeftOnCorpse(pair, [], state)); // nothing replaced
        Assert.False(DeathReplace.LeftOnCorpse(pair, [GearFloorKind.Robe], state)); // another kind replaced
        Assert.False(DeathReplace.LeftOnCorpse(robe, [GearFloorKind.Gauntlets], state));
    }

    private static WorldObject Item(HandlerRig rig, uint guid, uint type, uint valid, uint priority, uint wcid)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Piece " + wcid.ToString(CultureInfo.InvariantCulture), itemType: type);
        rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = basis.Weenie.Header | WeenieHeaderFlags.ValidLocations | WeenieHeaderFlags.Priority, WeenieClassId = wcid, ValidLocations = valid, Priority = priority } }, isUpdate: false);
        Assert.True(rig.R.H.State.Objects.TryGet(guid, out var made));
        return made!;
    }

    [Fact]
    public void M6_7_AH_every_ladder_rung_ground_sells_a_chest_piece_and_a_hand_piece_at_the_gear_floor()
    {
        var floorLevel = GearFloor.FloorLevel(LadderLoader.WarMage);
        var grounds = LadderLoader.WarMage.Rungs.Where(r => r.GroundKey is not null).Select(r => r.GroundKey!).Distinct().ToList();
        Assert.NotEmpty(grounds);
        foreach (var key in grounds)
        {
            var ground = GroundCatalog.Get(key);
            var bare = new GearFloorState(60, GearFloor.CastersWanted, ChestCovered: false, ChestPiece: null, Purse: 1_000_000, HandsCovered: false);
            var plan = GearFloor.Plan(ground, bare, floorLevel);
            Assert.True(plan.Lines.Any(l => l.Kind == GearFloorKind.Robe), $"{key} sells no robe: {string.Join("; ", plan.Notes)}");
            Assert.True(plan.Lines.Any(l => l.Kind == GearFloorKind.Gauntlets), $"{key} sells no hand armour: {string.Join("; ", plan.Notes)}");
        }
    }

    [Fact]
    public void M6_7_AH_the_replace_plan_asks_for_no_caster_when_one_is_held_never_a_mana_charge_and_nothing_under_the_floor_level()
    {
        var ground = GroundCatalog.Get("holtburg-outskirts");
        var rig = new HandlerRig("holtburg-outskirts", spare: false, kitless: true, level: 40);
        var held = DeathReplace.Plan(ground, rig.R.H.State, 1_000_000, 5);
        Assert.DoesNotContain(held.Lines, l => l.Kind == GearFloorKind.Caster);
        Assert.DoesNotContain(held.Lines, l => l.Kind == GearFloorKind.ManaCharge);
        Assert.Empty(DeathReplace.Missing(rig.R.H.State, 5));
        Assert.Empty(DeathReplace.Missing(rig.R.H.State, 50)); // under the floor level the floor asks nothing
    }

    // ------------------------------------------------------------------------------------------------ M6 7 AY: a corpse run asks the rung's owner; the coin rule

    private static (HandlerRig Rig, DeathHandler Handler) CorpseRun(string ground, int level, uint? pack, int coin, Ladder ladder, bool gateMet, double abortSeconds = 90)
    {
        var rig = new HandlerRig(ground, spare: false, kitless: true, level: level);
        rig.Drops.Clear();
        if (pack is { } guid)
        {
            rig.PackItem(guid, "Handy Healing Kit", 628); // M6 7 BB: the owed item is one she needs (a kit); a Mana Charge alone is left (nothing-needed)
            rig.Drops.Add(guid);
        }

        rig.CoinDrop = coin;
        rig.Worn.Snapshot(rig.R.H.State);
        rig.R.H.State.Self.Qualities.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        rig.R.H.State.Self.Spellbook.IsKnown = true;
        foreach (var line in ladder.Gates.SelectMany(g => g.Lines))
        {
            rig.R.H.State.Self.Spellbook.Learn(RungGateRule.LineOf(BuffProfiles.Climber, line.Line).Spells[line.Level - 1].Id);
        }

        rig.Die();
        rig.Settle();
        var options = new DeathOptions { Facts = rig.Facts, Worn = rig.Worn, Fight = false, AbortSeconds = abortSeconds, CorpseLadder = ladder, CorpseBuffsForTest = new RungBuffRead(_ => gateMet ? 8 : 0, _ => 4) };
        var handler = new DeathHandler(rig.Ground, rig.Tally, rig.Travel, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, options, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);
        return (rig, handler);
    }

    [Fact]
    public void M6_sprint_vitae_first_the_corpse_is_owed_after_the_rebuff_and_no_way_is_taken()
    {
        // M6 sprint (the owner's decision of 2026-11-18: the vitae burnt first, the corpse after): the journey's rule says burn the vitae first: the handler re-arms and re-buffs, then ends where the body stands with the corpse owed
        var (rig, handler) = CorpseRun("tusker-lodge", 17, 0x80000220, 0, LadderLoader.WarMage, gateMet: true);
        DeathFacts? asked = null;
        rig.Facts.DeferCorpse = (_, facts, _) =>
        {
            asked = facts;
            return "test: the vitae is burnt on the rung below first";
        };

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal((DeathCorpse.Deferred, DeathLegVerdict.TripAbortVitaeFirst), (handler.Corpse, handler.TripAbort));
        Assert.Empty(rig.Travel.Requests);
        Assert.Equal(rig.Tally.LastCorpseGuid, asked?.CorpseGuid);
        Assert.NotEqual(0u, asked?.CorpseGuid);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse_recovered=deferred trip_abort=vitae-first: test: the vitae is burnt", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse run: asked the rung's owner", StringComparison.Ordinal)); // asked before the gate: no way is weighed
        Assert.Equal("corpse_recovered=deferred trip_abort=vitae-first", DeathAnswer.Of(DeathAnswer.VerdictOnly, handler.Corpse, handler.TripAbort)); // a named answer: nothing is owed to this station
    }

    [Fact]
    public void M6_7_AY_a_corpse_owed_an_item_behind_a_shut_gate_is_not_fetched_by_name_and_no_way_is_taken()
    {
        var (rig, handler) = CorpseRun("tusker-lodge", 17, 0x80000220, 0, LadderLoader.WarMage, gateMet: false);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal((DeathCorpse.No, DeathLegVerdict.TripAbortGateShut), (handler.Corpse, handler.TripAbort));
        Assert.Empty(rig.Travel.Requests);
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse run: asked the rung's owner: level=17 ", StringComparison.Ordinal) && e.Message.Contains("ground=tusker-lodge rung=lodge -> shut (gate-held:lodge)", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("handler: corpse_recovered=no: the corpse lies on tusker-lodge, behind a shut gate", StringComparison.Ordinal));
        Assert.NotNull(DeathAnswer.Of(DeathAnswer.VerdictOnly, handler.Corpse, handler.TripAbort)); // an answer: the climb goes on
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 7; live 2026-11-14: her corpse lay at 0xEC7D0019 on the Holding's approach, on no rung's ground): a corpse she needs, in an approach she cannot survive at her state after the rebuff, is not walked to:
    /// <c>trip_abort=approach</c>, the rot's time named (level 62: 18,600 s), and no way is taken.
    /// </summary>
    [Fact]
    public void M6_14_a_needed_corpse_in_an_approach_she_cannot_survive_is_not_walked_to_and_the_rot_is_named()
    {
        var rig = new HandlerRig("tusker-holding", spare: false, alter: g => g with { Tour = [.. g.Tour, 0xEC7D0019u] }, kitless: true, level: 62);
        rig.Drops.Clear();
        rig.PackItem(0x80000220, "Handy Healing Kit", 628); // the owed item is one she needs
        rig.Drops.Add(0x80000220);
        rig.Worn.Snapshot(rig.R.H.State);
        rig.R.H.State.Self.Qualities.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        rig.R.H.State.Self.Spellbook.IsKnown = true;
        rig.R.H.State.Self.Spellbook.Learn(Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell);
        rig.Die();
        rig.Settle();
        var options = new DeathOptions { Facts = rig.Facts, Worn = rig.Worn, Fight = false, CorpseLadder = LadderLoader.WarMage, CorpseBuffsForTest = new RungBuffRead(_ => 8, _ => 4) };
        var handler = new DeathHandler(rig.Ground, rig.Tally, rig.Travel, rig.LegFactory, rig.Walk, rig.Kit, rig.Caster.Table, new BuffOptions(), new FightOptions { Kills = 4 }, options, rig.Log);
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal((DeathCorpse.No, DeathLegVerdict.TripAbortApproach), (handler.Corpse, handler.TripAbort));
        Assert.Empty(rig.Travel.Requests);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("ground=none rung=none -> open (no-rung)", StringComparison.Ordinal)); // no gate guards an approach
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse approach: corpse_cell=0xEC7D0019 approach=tusker-holding -> no walk (approach-unsurvivable)", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("it rots 18600 s after the death", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_BB_a_needed_corpse_deep_in_the_lodge_with_the_gate_open_is_left_by_name_and_no_way_is_taken()
    {
        var (rig, handler) = CorpseRun("tusker-lodge", 17, 0x80000220, 0, LadderLoader.WarMage, gateMet: true);

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal((DeathCorpse.No, DeathLegVerdict.TripAbortCellUnsafe), (handler.Corpse, handler.TripAbort));
        Assert.Empty(rig.Travel.Requests);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse trip: ", StringComparison.Ordinal) && e.Message.EndsWith("-> leave-corpse:cell-unsafe", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("the handler leaves the corpse and goes on", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_AY_a_corpse_owed_an_item_with_the_gate_open_goes_as_before()
    {
        var (rig, handler) = CorpseRun("holtburg-outskirts", 12, 0x80000220, 0, LadderLoader.WarMage, gateMet: false);

        rig.Run(handler);
        Dump(rig, handler);

        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("ground=holtburg-outskirts rung=outskirts -> open", StringComparison.Ordinal));
        Assert.Contains(rig.Travel.Requests, r => r.Purpose == TravelPurpose.Corpse);
    }

    [Fact]
    public void M6_7_AY_on_a_server_that_destroys_the_coin_coin_alone_is_no_fetch_by_name()
    {
        var (rig, handler) = CorpseRun("holtburg-outskirts", 12, null, 4640, LadderLoader.WarMage.WithCorpseCoin(new LadderCorpseCoin(false, 2000)), gateMet: true);

        rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal((DeathCorpse.NoneNeeded, DeathLegVerdict.TripAbortNoneNeeded), (handler.Corpse, handler.TripAbort));
        Assert.Empty(rig.Travel.Requests);
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse fetch: coin=4640 min=2000 on_corpse=no ", StringComparison.Ordinal) && e.Message.EndsWith("-> no-fetch:coin-destroyed", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_finish_the_shipped_ladder_walks_to_a_safe_corpse_for_its_coin()
    {
        var (rig, handler) = CorpseRun("holtburg-outskirts", 12, null, 4640, LadderLoader.WarMage, gateMet: true);

        rig.Run(handler);
        Dump(rig, handler);

        Assert.Contains(rig.Travel.Requests, r => r.Purpose == TravelPurpose.Corpse);
        Assert.NotEqual(DeathCorpse.NoneNeeded, handler.Corpse);
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse fetch: coin=4640 ", StringComparison.Ordinal) && e.Message.EndsWith("-> fetch:met", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_AY_a_small_purse_stays_where_it_fell_even_where_the_coin_is_kept()
    {
        var (rig, handler) = CorpseRun("holtburg-outskirts", 12, null, 1529, LadderLoader.WarMage.WithCorpseCoin(new LadderCorpseCoin(true, 2000)), gateMet: true);

        rig.Run(handler);

        Assert.Empty(rig.Travel.Requests);
        Assert.Contains(rig.Log.Entries, e => e.Message.EndsWith("-> no-fetch:coin-short", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_AY_a_character_the_ladder_does_not_bind_asks_nothing()
    {
        var rig = new HandlerRig("black-spawn-den-aluvian");
        rig.Die();
        rig.Settle();
        var handler = rig.Handler();
        handler.Begin(rig.View, rig.Now, DeathFacts.From(rig.Tally), respawned: true);

        rig.Run(handler);

        Assert.DoesNotContain(rig.Log.Entries, e => e.Message.StartsWith("handler: corpse run:", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ M6 8 I: the corpse trip's limit is no progress

    [Fact]
    public void M6_8_I_a_corpse_worth_fetching_is_given_up_when_the_body_does_not_move_not_when_the_time_runs_out()
    {
        var ladder = LadderLoader.WarMage.WithCorpseCoin(new LadderCorpseCoin(true, 2000, TripStallSeconds: 8, TripStallMetres: 10));
        var (rig, handler) = CorpseRun("holtburg-outskirts", 11, null, 44664, ladder, gateMet: true, abortSeconds: 3);
        rig.Travel.Steps = 100_000; // the runner never moves the body

        var status = rig.Run(handler);
        Dump(rig, handler);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("trip-stalled-8s", handler.TripAbort);
        Assert.InRange(handler.TripSeconds ?? 0, 8, 20); // past the 3 s of --death-recover-abort-seconds, which does not apply here
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: trip limit: fetch=yes -> no progress: 8 s without 10 m of movement", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_8_I_a_trip_whose_body_keeps_moving_is_not_cut_however_long_it_runs()
    {
        var ladder = LadderLoader.WarMage.WithCorpseCoin(new LadderCorpseCoin(true, 2000, TripStallSeconds: 8, TripStallMetres: 10));
        var (rig, handler) = CorpseRun("holtburg-outskirts", 11, null, 44664, ladder, gateMet: true, abortSeconds: 3);
        rig.Travel.Steps = 100_000;
        var here = rig.R.H.State.Self.Position!.Value;
        var x = here.Frame.Origin.X;

        var status = RunWith(rig, handler, () => rig.StandAt(here.CellId, new Vector3(x += 3, here.Frame.Origin.Y, here.Frame.Origin.Z)), seconds: 60); // 12 m a second for a minute: far past the 3 s and the 8 s

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(DeathLegVerdict.TripAbortNone, handler.TripAbort);
    }

    [Fact]
    public void M6_8_I_a_trip_not_worth_fetching_or_a_ladder_with_no_stall_keeps_the_time_limit()
    {
        var (rig, handler) = CorpseRun("holtburg-outskirts", 11, null, 44664, LadderLoader.WarMage.WithCorpseCoin(new LadderCorpseCoin(true, 2000)), gateMet: true, abortSeconds: 6);
        rig.Travel.Steps = 100_000;

        rig.Run(handler);

        Assert.Equal("trip-over-6s", handler.TripAbort);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("handler: trip limit: fetch=yes -> time: 6 s from the arrival", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_8_I_the_shipped_ladder_carries_the_stall_values()
    {
        Assert.Equal((90d, 10d), (LadderLoader.WarMage.CorpseCoin.TripStallSeconds, LadderLoader.WarMage.CorpseCoin.TripStallMetres));
    }
}

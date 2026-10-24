using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The death-and-regear scenario (M3 3.2 lane da, <see cref="DeathScenario"/>) over a scripted ACE on the real read model, reducer and Action API: the Holtburg bind (the nearest lifestone is used), the den's floor rule, a
/// buffed start that suicides first (the suicide is not a <c>died</c>) and an unbuffed one that does not, the tuskers' death in ACE's order (<c>Player_Death.cs</c> 34-292), the respawn at a lifestone and its classification,
/// the time limit, <see cref="DeathOptions.StopAfterHits"/>, the quiet minute that moves the wait on, and the spell bars. The gear and the regear run against the offline stand-in (the scripted rig has no inventory). Nothing here
/// needs the dats or a server.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed partial class DeathScenarioTests(ITestOutputHelper output)
{
    private const uint HoltburgCell = 0xA9B40019;
    private const uint FarCell = 0xA8B40019;
    private const uint DenArrival = 0x01050375;
    private const uint NotOnTheFloor = 0x01050341;
    private const uint NearLifestone = 0x80000700;
    private const uint FarLifestone = 0x80000701;
    private const uint VitaeType = 0x800000 | 0x4000 | 0x2000 | 0x2;
    private const uint NumDeaths = 43;
    private const uint Tusker = 0x80000800;

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

    /// <summary>
    /// A walk that arrives after a few ticks; it counts the legs it was asked for. It keeps two rules of the real walks, so the scenario is seen never to tick a leg it should have dropped: a leg abandoned while it was still walking
    /// answers Failed on the next tick (what <c>RouteDriver.Abandon</c> leaves in a driver a walk keeps: the route is marked Failed and <c>Tick</c> returns that status), and a leg ticked after the player's landblock changed under
    /// it (a death's teleport) answers Failed with the driver's own reason (<c>RouteDriver.TeleportMetres</c>: the player was moved in one frame). <see cref="TicksAfterAbandon"/> counts the ticks made after an abandon: the
    /// scenario makes none, so what the real walks answer then (Idle since they drop the driver) is never read.
    /// </summary>
    private sealed class FakeWalk : IFightWalk
    {
        private int _ticks;
        private bool _walking;
        private bool _abandoned;
        private bool _abandonedMidLeg;
        private uint _legLandblock;

        public int Prepared { get; private set; }

        public int Legs { get; private set; }

        /// <summary>The abandons, and those that came while a leg was still walking.</summary>
        public int Abandons { get; private set; }

        public int AbandonsMidLeg { get; private set; }

        /// <summary>The ticks after an abandon (before the next leg).</summary>
        public int TicksAfterAbandon { get; private set; }

        public int TicksPerLeg { get; set; } = 4;

        public string? Failure { get; private set; }

        public void Prepare(IGameSessionView session, double now) => Prepared++;

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            Legs++;
            description = "fake leg " + Legs;
            _walking = true;
            _abandoned = false;
            _abandonedMidLeg = false;
            _ticks = 0;
            _legLandblock = (session.State.Self.Position?.CellId ?? 0) >> 16;
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            if (_abandoned)
            {
                TicksAfterAbandon++;
            }

            if (_abandonedMidLeg)
            {
                _abandonedMidLeg = false;
                Failure = "route 'fake' lap 0 goal 0: the route was abandoned for a new one";
                return FightWalkStatus.Failed;
            }

            if (!_walking)
            {
                return FightWalkStatus.Idle;
            }

            if ((session.State.Self.Position?.CellId ?? 0) >> 16 != _legLandblock)
            {
                _walking = false;
                Failure = "route 'fake' lap 0 goal 0: the player was moved in one frame (a teleport or a server correction)";
                return FightWalkStatus.Failed;
            }

            if (++_ticks < TicksPerLeg)
            {
                return FightWalkStatus.Walking;
            }

            _walking = false;
            return FightWalkStatus.Arrived;
        }

        public void Abandon()
        {
            Abandons++;
            _abandoned = true;
            if (_walking)
            {
                AbandonsMidLeg++;
                _abandonedMidLeg = true;
            }

            _walking = false;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>An approach walk that arrives (moving the rig's player beside the lifestone) after a few ticks, or ends as the test says.</summary>
    private sealed class FakeApproach(Rig rig) : IApproachWalk
    {
        private int _ticks;
        private bool _walking;

        public int Prepared { get; private set; }

        public int PlacesAtPrepare { get; private set; } = -1;

        public int Begins { get; private set; }

        public uint TargetCell { get; private set; }

        public float StopWithin { get; private set; }

        public int UsesWhileWalking { get; private set; }

        /// <summary>Ticks seen before Begin: the live bug was that nothing ticked the walker until the leg began, so the teleport's position was never applied.</summary>
        public int TicksBeforeBegin { get; private set; }

        /// <summary>When set, Begin says it cannot start (the live message: the walker was not placed).</summary>
        public bool CannotBegin { get; set; }

        public int TicksToArrive { get; set; } = 6;

        /// <summary>The status the walk ends with when it does not arrive (Failed), and where the player stands then.</summary>
        public bool EndsFailed { get; set; }

        public bool Abandoned { get; private set; }

        public bool Disposed { get; private set; }

        public string? Failure { get; private set; }

        public void Prepare(IGameSessionView session, double now)
        {
            Prepared++;
            PlacesAtPrepare = rig.Places.Count;
        }

        public bool Begin(IGameSessionView session, double now, in CellFrame target, float stopWithinMetres, out string description)
        {
            if (CannotBegin)
            {
                Failure = "the approach walker is not placed: no teleport of the player into Holtburg's landblock reached it";
                description = string.Empty;
                return false;
            }

            Begins++;
            (TargetCell, StopWithin) = (target.CellId, stopWithinMetres);
            description = "fake approach";
            _walking = true;
            _ticks = 0;
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            if (!_walking)
            {
                if (Begins == 0)
                {
                    TicksBeforeBegin++;
                }

                return FightWalkStatus.Idle;
            }

            if (rig.Used.Count > 0)
            {
                UsesWhileWalking++;
            }

            if (++_ticks < TicksToArrive)
            {
                return FightWalkStatus.Walking;
            }

            _walking = false;
            if (EndsFailed)
            {
                Failure = "the fake approach is blocked";
                return FightWalkStatus.Failed;
            }

            rig.WalkTo(59f, 29.5f);
            return FightWalkStatus.Arrived;
        }

        public void Abandon()
        {
            Abandoned = true;
            _walking = false;
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>The scripted ACE: it answers the place tool, the Use, the suicide and the spell bars, blows at the player in the den, and kills it in ACE's order.</summary>
    private sealed class Rig
    {
        private readonly List<(double Due, Action Do)> _timeline = [];
        private readonly byte[] _vitalSequence = new byte[8];
        private int _seen;
        private ushort _teleport = 2;
        private ushort _positionSeq = 2;
        private ushort _movement = 1;
        private int _numDeaths;
        private int _blows;
        private bool _dead;
        private double _nextBlow = double.PositiveInfinity;
        private uint _health;

        public Rig(bool buffed, uint startCell = HoltburgCell, bool lifestone = true, float lifestoneX = 60, float? useRadius = null, bool describe = true)
        {
            R = new WorldActionRig();
            View = new View(R);
            if (describe)
            {
                Describe();
            }
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
            h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 2, 2, 1, cell: startCell, x: 50));
            if (buffed)
            {
                R.Enchant(5101);
                R.Enchant(5102);
            }

            R.CombatModeIs(CombatModes.NonCombat);
            _health = Current(1);
            if (lifestone)
            {
                Lifestone(NearLifestone, HoltburgCell, lifestoneX, useRadius);
            }

            if (startCell != HoltburgCell)
            {
                Lifestone(FarLifestone, FarCell, 60);
            }
        }

        public WorldActionRig R { get; }

        /// <summary>True: the PlayerDescription lists the wand as wielded (M4 3.1 g's combat-mode guard refuses the magic mode for a mage that holds no caster); set it, then call <see cref="Describe"/> and create the object.</summary>
        public bool HoldsWand { get; set; }

        /// <summary>The PlayerDescription (the qualities become known).</summary>
        public void Describe() =>
            R.H.Describe(new PlayerDescriptionMsg
            {
                Equipped = HoldsWand ? [new EquippedDesc(WieldedSetups.Wand.Guid, (uint)EquipMask.Held, 0)] : [],
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(NumDeaths, 0),
                Attributes = [.. Enumerable.Range(1, 6).Select(i => new AttributeDesc((uint)i, 0, 55, 0))],
                Vitals = [new VitalDesc(1, 0, 0, 0, 28), new VitalDesc(3, 0, 0, 0, 55), new VitalDesc(5, 0, 0, 0, 55)],
            });

        public IGameSessionView View { get; }

        public RecordingLogSink Log => R.H.Log;

        public double Now => R.H.R.Clock.Now;

        /// <summary>The cell a death's teleport lands in (a lifestone's, by default).</summary>
        public uint RespawnCell { get; set; } = HoltburgCell;

        /// <summary>The cell the den place lands in.</summary>
        public uint DenCell { get; set; } = DenArrival;

        /// <summary>True: the second place (the way out of the den) is never answered.</summary>
        public bool RefusePlaceOutOfDen { get; set; }

        /// <summary>The amounts the xp verb was asked for (the vitae stage).</summary>
        public List<long> XpAsked { get; } = [];

        /// <summary>True: ACE removes the vitae two seconds after an xp grant (the grant paid it in full); false: it stays.</summary>
        public bool XpClearsVitae { get; set; } = true;

        /// <summary>True: the tuskers blow at the player in the den.</summary>
        public bool Tuskers { get; set; } = true;

        /// <summary>True: the blows can kill; false: health stops at 1.</summary>
        public bool Lethal { get; set; } = true;

        public double BlowEvery { get; set; } = 4;

        public uint Damage { get; set; } = 7;

        /// <summary>Every n-th blow is an evade (0 for none).</summary>
        public int EvadeEvery { get; set; }

        /// <summary>When set, a Use on the lifestone is answered with this chat line.</summary>
        public string? UseLine { get; set; } = "The lifestone's attune line (a test's placeholder).";

        /// <summary>Seconds after the Use that ACE's lifestone chain ends (Lifestone.cs 44-75: the stance change and the Sanctuary motion come first): the attune line, the stamina halving and, in that order, the UseDone.</summary>
        public double UseLineDelay { get; set; } = 6;

        /// <summary>
        /// True: the UseDone comes 1 s after the Use, before the attune (the order the review left UNCERTAIN: the UseDone's own timer, Player_Use.cs 235-238, runs for the same stance and motion time as the lifestone chain's);
        /// false (the default, ACE's order as the package-4 brief gives it): the attune line, the stamina halving, then the UseDone, all after the Sanctuary motion.
        /// </summary>
        public bool UseDoneFirst { get; set; }

        /// <summary>True (the default): the attune action halves the stamina (a stamina 0x02E9, Lifestone.cs 68-69) right after the UseMessage line.</summary>
        public bool UseStaminaHalving { get; set; } = true;

        /// <summary>Magic chat lines the server sends 2 s after the Use, between it and the attune (an enchantment's expiry during the bind, the protection and vitae lines).</summary>
        public List<string> UseOtherLines { get; } = [];

        /// <summary>True: three WeenieError 0x051C (the chat channel joins) arrive just before the attune line.</summary>
        public bool UseNoise { get; set; }

        /// <summary>Where (y) a teleport puts the player; the lifestone stands at y 30.</summary>
        public float LandY { get; set; } = 20;

        /// <summary>When set, the chain's range check fails: WeenieError with this code instead of the line (YouHaveMovedTooFar 0x0498).</summary>
        public uint UseWeenieError { get; set; }

        /// <summary>The UseDone code the server answers a Use with (0 is success).</summary>
        public uint UseError { get; set; }

        public List<uint> Used { get; } = [];

        // --- the mage's own corpse (M4 2.2 s1): the container, its contents and the way the server answers the trip

        /// <summary>The guid of the last corpse the mage's death created (0 before one).</summary>
        public uint CorpseGuid { get; private set; }

        /// <summary>The items the corpse holds when the trip opens it (ACE's contents, in the 0196's order).</summary>
        public List<WorldObject> CorpseItems { get; } = [];

        /// <summary>True: the death creates no corpse object (the tally remembers none).</summary>
        public bool NoCorpse { get; set; }

        /// <summary>True: the server answers the corpse's Use with a UseDone error and no 0196 (a refused open).</summary>
        public bool OpenRefused { get; set; }

        /// <summary>True: the corpse's object is deleted when the player leaves for the trip and never comes back (the table forgot it).</summary>
        public bool CorpseGone { get; set; }

        /// <summary>When set, the corpse is created again this many seconds after the trip's arrival (ACE re-creates it for the client); the default is at once.</summary>
        public double CorpseBackAfter { get; set; } = 0.2;

        /// <summary>True: the server never answers a pick-up (each one times out).</summary>
        public bool PickUpSilent { get; set; }

        /// <summary>The items whose pick-up the server never answers (each such pick-up times out, the others are answered).</summary>
        public HashSet<uint> SilentItems { get; } = [];

        /// <summary>What <see cref="DeathScenario.GuardWhileHeld"/> answered on the last held frame (null: the trip goes on).</summary>
        public ScenarioStatus? LastGuard { get; private set; }

        /// <summary>True: the trip's place is answered with an error (the mage never leaves).</summary>
        public bool RefuseTripPlace { get; set; }

        /// <summary>True: the tuskers go on blowing after the trip's arrival (they do not by default: the trip tests are about the trip).</summary>
        public bool TripBlows { get; set; }

        /// <summary>When above 0, a tusker kill this many seconds after the trip's arrival (the mage dies on the trip).</summary>
        public double KillOnTripAfter { get; set; }

        /// <summary>
        /// True: the live order of round 1 (M4 2.2 fix R1): after the respawn's arrival a stamina update of 0 and a mana update of 0 come (the drain of 30 evades reaching the client) BEFORE ACE's 75% restore, which comes 3 s after the teleport.
        /// </summary>
        public bool ZeroesBeforeRestore { get; set; }

        /// <summary>True: after the trip's arrival the stamina and mana go to 0 and the health falls (the trip's blows and drains), which must never move the judged vitals.</summary>
        public bool TripDrainsVitals { get; set; }

        /// <summary>True: <see cref="Frame"/> holds the scenario as the death leg's runner does while a review frame is owed: only <see cref="DeathScenario.GuardWhileHeld"/> runs, never Step.</summary>
        public bool Held { get; set; }

        /// <summary>The cell the trip's place lands in (the den's arrival by default).</summary>
        public uint TripCell { get; set; } = DenArrival;

        public List<uint> Opened { get; } = [];

        public List<uint> PickUps { get; } = [];

        public int Closes { get; private set; }

        /// <summary>The seconds the tuskers' death took the player's session clock to (the corpse's age is measured from its F745).</summary>
        private int _tuskerDeaths;
        private uint _gameEvent = 500;

        /// <summary>Sets the health to <paramref name="percent"/> of its maximum (a 0x02E9).</summary>
        public void SetHealth(uint percent) => Vital(2, Max(1) * percent / 100);

        /// <summary>The weenie classes the mod's item verb was asked for, in order (M4 3.1 d1 F2: the decoy top-up).</summary>
        public List<uint> ItemGrants { get; } = [];

        /// <summary>True: the item verb is never answered (the top-up cannot complete).</summary>
        public bool GrantsSilent { get; set; }

        private uint _nextGrant = 0x80000D00;

        /// <summary>The spells the mage cast untargeted, by id (M4 3.1 d1 F1: the pre-trip heal).</summary>
        public List<uint> Casts { get; } = [];

        /// <summary>The health points an untargeted cast heals (the server answers it with the health update and the UseDone, <see cref="CastSeconds"/> after it); 0 heals nothing.</summary>
        public uint HealPoints { get; set; }

        /// <summary>Seconds from a cast to its health update and UseDone.</summary>
        public double CastSeconds { get; set; } = 1.0;

        /// <summary>Sets the health to <paramref name="percent"/> of its maximum <paramref name="seconds"/> from now (the regeneration the pre-trip wait waits for).</summary>
        public void RegenerateTo(double seconds, uint percent) => At(seconds, () => Vital(2, Max(1) * percent / 100));

        /// <summary>M4 P4 g3fix: sets the mana to <paramref name="percent"/> of its maximum now (a 0x02E7) and, when <paramref name="regenSeconds"/> is above 0, to <paramref name="regenPercent"/> that many seconds from now.</summary>
        public void SetMana(uint percent, double regenSeconds = 0, uint regenPercent = 0)
        {
            Vital(6, Max(5) * percent / 100);
            if (regenSeconds > 0)
            {
                At(regenSeconds, () => Vital(6, Max(5) * regenPercent / 100));
            }
        }

        /// <summary>One blow of the tuskers now, as <see cref="Damage"/> and <see cref="Lethal"/> say (M4 3.1 d1: what a scripted fight takes while it fights).</summary>
        public void Hit() => Blow();

        /// <summary>The tuskers kill the player now, in ACE's order (M4 3.1 d1: a second death, after the one the loop waits for).</summary>
        public void Kill() => Die(suicide: false);

        public List<string> Places { get; } = [];

        public int Suicides { get; private set; }

        public int Blows => _blows;

        public int ModeChanges { get; private set; }

        public ScenarioStatus Frame(DeathScenario scenario, double dt = 0.25)
        {
            R.Tick(dt);
            Pump();
            R.Tick();
            if (Held)
            {
                LastGuard = scenario.GuardWhileHeld(View);
                return LastGuard ?? ScenarioStatus.Running;
            }

            return scenario.Step(View, Now);
        }

        public ScenarioStatus Run(DeathScenario scenario, double maxSeconds = 900, double dt = 0.25)
        {
            var end = Now + maxSeconds;
            var status = ScenarioStatus.Running;
            while (status == ScenarioStatus.Running && Now < end)
            {
                status = Frame(scenario, dt);
            }

            return status;
        }

        public DeathScenario Scenario(IFightWalk? walk, DeathStages stages, DeathOptions? options = null, IApproachWalk? approach = null) =>
            new(GearKit.Of(Array.Empty<GearItem>()), new Dictionary<uint, SpellInfo>(), options ?? new DeathOptions(), walk, R.H.Log, endSession: false, stages: stages, approach: approach);

        private void Pump()
        {
            var now = Now;
            var sends = R.H.Net.Sends;
            while (_seen < sends.Count)
            {
                var send = sends[_seen++];
                switch (send.Kind)
                {
                    case FakeSendKind.ChangeCombatMode:
                        ModeChanges++;
                        R.CombatModeIs((int)send.Amount);
                        break;
                    case FakeSendKind.Talk when send.Text is { } text && text.Contains("place", StringComparison.Ordinal):
                        Places.Add(text);
                        if (RefusePlaceOutOfDen && Places.Count >= 2)
                        {
                            break; // the server never answers the place that leaves the den
                        }

                        var poi = text.Contains("poi", StringComparison.Ordinal);
                        var trip = _tuskerDeaths > 0 && !poi; // the place that follows the tuskers' death and is not Holtburg's point of interest is the corpse trip
                        if (trip && RefuseTripPlace)
                        {
                            var refusedId = text[(text.LastIndexOf('#') + 1)..];
                            R.H.ServerMessage(ToolsWire.Reply(ToolReply.Failure(refusedId, ToolVerbs.PlaceLoc, new ToolError("refused", "no")), "no"));
                            break;
                        }

                        if (trip)
                        {
                            LeaveForTrip();
                        }

                        Arrive(text, poi ? HoltburgCell : (trip ? TripCell : DenCell));
                        if (trip)
                        {
                            ArriveOnTrip();
                        }

                        break;
                    case FakeSendKind.Use when CorpseGuid != 0 && send.Guid == CorpseGuid:
                        Opened.Add(send.Guid);
                        At(0.3, OpenCorpseNow);
                        break;
                    case FakeSendKind.PutItemInContainer when CorpseItems.Any(i => i.Guid == send.Guid):
                        PickUps.Add(send.Guid);
                        if (!PickUpSilent && !SilentItems.Contains(send.Guid))
                        {
                            var item = send.Guid;
                            At(1.35, () => R.Server((p, sequence) => AceM3Writers.PutObjInContainer(p, sequence, item, WorldActionRig.Player, 0, 0)));
                        }

                        break;
                    case FakeSendKind.NoLongerViewingContents:
                        Closes++;
                        var closing = send.Guid;
                        At(0.016, () => R.H.R.Reducer.OnCloseGroundContainer(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_gameEvent, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(closing)));
                        break;
                    case FakeSendKind.Talk when send.Text is { } xpText && xpText.Contains(" xp ", StringComparison.Ordinal):
                        Xp(xpText);
                        break;
                    case FakeSendKind.Use:
                        Used.Add(send.Guid);
                        if (UseDoneFirst)
                        {
                            At(1.0, () => R.UseDone(UseError));
                        }

                        if (UseOtherLines.Count > 0)
                        {
                            At(2.0, () =>
                            {
                                foreach (var other in UseOtherLines)
                                {
                                    R.H.ServerMessage(other, 7);
                                }
                            });
                        }

                        At(1.0 + UseLineDelay, () =>
                        {
                            if (UseNoise)
                            {
                                R.WeenieError(0x051C);
                                R.WeenieError(0x051C);
                                R.WeenieError(0x051C);
                            }

                            if (UseWeenieError != 0)
                            {
                                R.WeenieError(UseWeenieError);
                            }
                            else
                            {
                                // Lifestone.cs 64-69, one action: the UseMessage as a Magic line, then UpdateVital(Stamina, Math.Round(current / 2f)).
                                if (UseLine is { } line)
                                {
                                    R.H.ServerMessage(line, 7);
                                }

                                if (UseStaminaHalving)
                                {
                                    Vital(4, (uint)Math.Round(Current(3) / 2f));
                                }
                            }

                            if (!UseDoneFirst)
                            {
                                R.UseDone(UseError); // TryUseItem's own chain, Player_Use.cs 235-238: after the same stance and motion time
                            }
                        });
                        break;
                    case FakeSendKind.Talk when send.Text is { } itemText && itemText.Contains(" item ", StringComparison.Ordinal):
                        if (!GrantsSilent)
                        {
                            GrantItem(itemText);
                        }

                        break;
                    case FakeSendKind.CastUntargetedSpell:
                        Casts.Add(send.Guid);
                        At(CastSeconds, () =>
                        {
                            Vital(2, Math.Min(Max(1), Current(1) + HealPoints)); // ACE's heal: the health update, then the UseDone that ends the cast
                            R.UseDone(0);
                        });
                        break;
                    case FakeSendKind.Suicide:
                        Suicides++;
                        At(15.0, () =>
                        {
                            if (!_dead)
                            {
                                Die(suicide: true);
                            }
                        });
                        break;
                }
            }

            for (var i = 0; i < _timeline.Count;)
            {
                if (_timeline[i].Due <= now)
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

            if (Tuskers && !_dead && now >= _nextBlow && InDen() && (_tuskerDeaths == 0 || TripBlows))
            {
                _nextBlow = now + BlowEvery;
                Blow();
            }
        }

        /// <summary>
        /// The mod's item verb as ACE answers it (the wire order of the live gate): the 0022 into the pack and the verb's reply first, the item's CreateObject a bit later. The item is the decoy of that weenie class when it is one
        /// (its ItemType, value and burden), else a plain light item.
        /// </summary>
        private void GrantItem(string text)
        {
            var id = text[(text.LastIndexOf('#') + 1)..];
            var at = text.IndexOf(" item ", StringComparison.Ordinal) + 6;
            var end = at;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            var wcid = uint.Parse(text[at..end], CultureInfo.InvariantCulture);
            var guid = _nextGrant++;
            ItemGrants.Add(wcid);
            var decoy = DecoyKit.All.FirstOrDefault(d => d.Wcid == wcid);
            var name = decoy?.Name ?? "Item " + wcid.ToString(CultureInfo.InvariantCulture);
            var item = LootRulesTests.Item(decoy?.ItemType ?? 0x80, decoy is null ? 10u : (uint)decoy.UnitValue, (ushort)(decoy?.Burden ?? 5), guid, name, wcid);
            var data = new ItemData(guid, wcid, name, 1, null, 1, false);
            var placement = (uint)ItemGrants.Count;
            At(0.2, () =>
            {
                R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, guid, WorldActionRig.Player, placement, 0));
                R.H.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Item, data), data.Describe()));
            });
            At(0.4, () =>
            {
                var h = R.H.R;
                h.Reducer.OnCreateObject(h.Context, item.Created, isUpdate: false);
            });
        }

        /// <summary>The mod's xp verb: the reply, and the vitae leaving the registry two seconds on (<c>UpdateXpVitae</c>, <c>Player_Xp.cs</c> 166-171) when the grant paid it.</summary>
        private void Xp(string text)
        {
            var id = text[(text.LastIndexOf('#') + 1)..];
            var from = text.IndexOf(" xp ", StringComparison.Ordinal) + 4;
            var amount = long.Parse(text[from..text.IndexOf(' ', from)], CultureInfo.InvariantCulture);
            XpAsked.Add(amount);
            var data = new XpData(amount, amount, amount, amount, 126);
            At(0.5, () => R.H.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Xp, data), data.Describe())));
            if (XpClearsVitae)
            {
                At(2.5, () => R.Server((p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, 666, 1)));
            }
        }

        /// <summary>The vitae in the registry (666) at <paramref name="multiplier"/>, the level and the experience the stage reads.</summary>
        public void HoldVitae(float multiplier, int level = 126, int pool = 0, long total = 1_000_000)
        {
            R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(666, 1, 666, 0, 0, 0, -1, p, 0f, 0f, 0.0, VitaeType, 0, multiplier, 0)));
            R.PrivateInt(PlayerQualities.LevelProperty, level);
            R.PrivateInt(EffectRules.VitaeLevelProperty, level);
            R.PrivateInt(EffectRules.VitaeCpPoolProperty, pool);
            R.Raw(AceWireWriters.PrivateUpdatePropertyInt64(1, PlayerQualities.TotalExperienceProperty, total));
        }

        /// <summary>ACE's open of a corpse (capture C2): the 0196, the UseDone, then an F745 per item; a refused open is a UseDone error and nothing else.</summary>
        private void OpenCorpseNow()
        {
            if (OpenRefused)
            {
                R.UseDone(0x0046);
                return;
            }

            var h = R.H.R;
            var guid = CorpseGuid;
            h.Reducer.OnViewContents(h.Context, new GameEventHeader(WorldActionRig.Player, ++_gameEvent, GameEventTypes.ViewContents), new ViewContentsEvent(guid, [.. CorpseItems.Select(i => new ViewContentsEntry(i.Guid, 0))]));
            R.UseDone(0);
            At(0.1, () =>
            {
                foreach (var item in CorpseItems)
                {
                    h.Reducer.OnCreateObject(h.Context, item.Created with { Weenie = item.Created.Weenie with { Header = item.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = guid } }, isUpdate: false);
                }
            });
        }

        /// <summary>The player leaves for the corpse: the table forgets the corpse (the client deletes what is out of range after a teleport).</summary>
        private void LeaveForTrip()
        {
            if (CorpseGuid != 0)
            {
                R.H.R.Reducer.OnDeleteObject(R.H.R.Context, new DeleteObjectMsg(CorpseGuid, 1));
            }
        }

        /// <summary>The player has arrived at the corpse: ACE re-creates it for the client (unless <see cref="CorpseGone"/>), and the trip's own events are scheduled.</summary>
        private void ArriveOnTrip()
        {
            if (CorpseGuid != 0 && !CorpseGone && _corpseCreate is { } create)
            {
                At(CorpseBackAfter, () => R.H.R.Reducer.OnCreateObject(R.H.R.Context, create, isUpdate: false));
            }

            if (KillOnTripAfter > 0)
            {
                At(KillOnTripAfter, () =>
                {
                    if (!_dead)
                    {
                        Die(suicide: false);
                    }
                });
            }

            if (TripDrainsVitals)
            {
                At(0.4, () => Vital(4, 0));
                At(0.8, () => Vital(6, 0));
                At(1.2, () => Vital(2, Max(1) / 2));
            }
        }

        private CreateObjectMsg? _corpseCreate;

        private bool InDen() => (R.H.State.Self.Position?.CellId >> 16) == 0x0105 && R.H.State.Self.Travel == SelfTravel.Present;

        private void At(double seconds, Action step) => _timeline.Add((Now + seconds, step));

        private uint Current(uint maxId) => R.H.State.Self.Qualities.TryGetVitalLevels(maxId, out var current, out _) ? current : 0;

        private uint Max(uint maxId) => R.H.State.Self.Qualities.TryGetVitalLevels(maxId, out _, out var max) ? max : 0;

        private void Vital(uint key, uint value)
        {
            _vitalSequence[key]++;
            R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(_vitalSequence[key], key, value));
        }

        private UpdatePositionMsg Moved(ushort position, uint cell, float x, float y) =>
            new(ActionHarness.Amy, PositionFlags.Grounded, ReducerHarness.Position(cell, x, y), null, null, 1, position, _teleport, 1);

        /// <summary>Moves the player (no teleport) to x metres in the Holtburg cell: what the approach walk does.</summary>
        public void WalkTo(float x, float y)
        {
            var h = R.H.R;
            h.Reducer.OnUpdatePosition(h.Context, Moved(++_positionSeq, HoltburgCell, x, y));
        }

        private void Lifestone(uint guid, uint cell, float x, float? useRadius = null)
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(cell, x, 30), name: "Lifestone");
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = "Lifestone", ObjectDescriptionFlags = WorldObjectTable.LifestoneFlag, UseRadius = useRadius } }, isUpdate: false);
        }

        private void Arrive(string request, uint cell)
        {
            var id = request[(request.LastIndexOf('#') + 1)..];
            var data = new PlaceData("loc", null, ToolsWire.FormatCell(cell), 50f, 30f, 0f, 1f, 0f, 0f, 0f);
            R.H.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.PlaceLoc, data), data.Describe()));
            Land(cell);
            if ((cell >> 16) == 0x0105)
            {
                _nextBlow = Now + BlowEvery;
            }
        }

        private void Land(uint cell)
        {
            _teleport++;
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(_teleport));
            h.Reducer.OnUpdatePosition(h.Context, Moved(++_positionSeq, cell, 50, LandY));
            R.Raw(AceWireWriters.SetState(WorldActionRig.Player, (uint)PhysicsState.Gravity, 1, (ushort)(40 + _teleport))); // a state without Hidden ends the arrival
        }

        /// <summary>
        /// A tusker's blow as <c>Player.TakeDamage</c> sends it (<c>Player_Combat.cs</c> 507-529): the health update 0x02E9 first; at 0 or below <c>OnDeath</c>, <c>Die</c> and the return, so the lethal blow has no 0x01B2 and a
        /// one-blow death reads <c>hits_taken=0</c>; a blow the player survives is followed by the DefenderNotification 0x01B2 (542). Every n-th blow is an evade (0x01B4) when <see cref="EvadeEvery"/> says so.
        /// </summary>
        private void Blow()
        {
            _blows++;
            if (EvadeEvery > 0 && _blows % EvadeEvery == 0)
            {
                R.Server((p, s) => AceM3Writers.EvasionDefenderNotification(p, s, "Tusker Slave"));
                return;
            }

            _health = _health > Damage ? _health - Damage : (Lethal ? 0u : 1u);
            Vital(2, _health);
            if (_health == 0)
            {
                Die(suicide: false);
                return;
            }

            R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, Damage, 4, false, 0));
        }

        /// <summary>
        /// ACE's order, <c>Player_Death.cs</c>: the victim notice (<c>OnDeath</c> 34-93; not for a suicide), then <c>Die</c> (150-242): the Dead motion, health 0, NumDeaths, the vitae, the purge; after the Dead animation plus 1 s
        /// (3 s here: the animation's length is a placeholder) the corpse and the teleport in one tick (229-239: <c>CreateCorpse</c>, then <c>ThreadSafeTeleportOnDeath</c>, whose F751 comes with the arrival's position,
        /// <c>Player_Location.cs</c> 686-694); 3 s after the arrival the vitals at 75% (262-282).
        /// </summary>
        private void Die(bool suicide)
        {
            _dead = true;
            _nextBlow = double.PositiveInfinity;
            if (!suicide)
            {
                _tuskerDeaths++;
                R.Server((p, s) => AceM3Writers.VictimNotification(p, s, "You have been killed by Tusker Slave."));
            }

            var h = R.H.R;
            h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(WorldActionRig.Player, 1, ++_movement, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = 0x003D, Forward = WorldObjectTable.DeadMotionLowWord }));
            Vital(2, 0);
            R.PrivateInt(NumDeaths, ++_numDeaths);
            R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(666, 1, 666, 0, 0, 0, -1, p, 0f, 0f, 0.0, VitaeType, 0, 0.95f, 0)));
            R.Server((p, s) => AceM3Writers.MagicPurgeEnchantments(p, s));
            At(3.0, () =>
            {
                if (!NoCorpse)
                {
                    var corpse = ReducerHarness.Create(0x80000900u + (uint)_numDeaths, ReducerHarness.Stamps(), ReducerHarness.Position(R.H.State.Self.Position?.CellId ?? DenArrival, 50, 30), name: "Corpse");
                    _corpseCreate = corpse with { Weenie = corpse.Weenie with { Name = "Corpse of " + R.H.State.Self.Name, ObjectDescriptionFlags = WorldObjectTable.CorpseFlag } };
                    CorpseGuid = corpse.Guid;
                    h.Reducer.OnCreateObject(h.Context, _corpseCreate, isUpdate: false);
                }

                Land(RespawnCell);
                _dead = false;
                _health = Max(1);
                if (ZeroesBeforeRestore)
                {
                    At(0.5, () => Vital(4, 0));
                    At(1.0, () => Vital(6, 0));
                }

                At(3.0, () =>
                {
                    Vital(2, Max(1) * 3 / 4);
                    Vital(4, Max(3) * 3 / 4);
                    Vital(6, Max(5) * 3 / 4);
                    _health = Max(1) * 3 / 4;
                });
            });
        }
    }

    private static DeathStages Core => DeathStages.Bind | DeathStages.Den | DeathStages.Unbuff | DeathStages.Death;

    private static IEnumerable<string> Lines(Rig rig) => rig.Log.Entries.Select(e => e.Message);

    private void Dump(Rig rig, DeathScenario scenario)
    {
        foreach (var line in Lines(rig).Where(l => !l.StartsWith("stage ", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        output.WriteLine("death " + scenario.VerdictFields());
    }

    // --- the whole scripted run ---

    [Fact]
    public void A_buffed_mage_binds_suicides_to_unbuff_dies_to_the_tuskers_and_respawns_at_the_lifestone_with_the_suicide_not_counted_as_died()
    {
        var rig = new Rig(buffed: true);
        rig.Damage = 3; // the tuskers' blows do not kill the buffed mage within the suicide's 15 s
        var walk = new FakeWalk();
        var scenario = rig.Scenario(walk, Core);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        Assert.Equal(1, rig.Suicides);
        Assert.Equal(1, scenario.Suicides);
        Assert.Equal("suicide", scenario.UnbuffNote);
        Assert.Equal(1, scenario.Tally.VictimNotices); // the suicide has no 0x01AC: died counts the tuskers' only
        Assert.Equal(2, scenario.Tally.Deaths); // NumDeaths counts both
        Assert.Equal(2, scenario.Tally.DeathTeleports);
        Assert.True(scenario.Tally.Purges >= 1);
        Assert.Equal(5, scenario.Tally.VitaePercent);
        Assert.Equal(2, scenario.Tally.CorpsesSeen);
        Assert.True(scenario.Tally.HitsTaken >= 1 && scenario.Tally.DamageTaken > 0);
        Assert.InRange(scenario.Tally.VitalsAfterArrivalPercent, 70, 80);
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(scenario.Tally, rig.R.H.State.Objects));
        Assert.Equal(0, CountBuffs(rig));
        Assert.Contains(Lines(rig), l => l.StartsWith("unbuff: the mage is buffed (2 enchantments); a suicide purges them as every death does (ACE Player_Death.cs 211-216)", StringComparison.Ordinal));

        // The Prepare came with the den, and the wait walked once to the nearest tusker cell; no mode change was needed (the mage was in non-combat).
        Assert.Equal(2, walk.Prepared); // once for each visit to the den; the real walk ignores the second
        Assert.Equal(1, walk.Legs);
        Assert.Equal(0, rig.ModeChanges);

        // The verdict: died=1 deaths=2 suicides=1 respawn=lifestone, and a regear-less run reads gear_restored=no (the stage was masked off).
        var fields = scenario.VerdictFields();
        Assert.Contains(" died=1 suicides=1 ", fields, StringComparison.Ordinal);
        Assert.Contains(" respawn=lifestone ", fields, StringComparison.Ordinal);
        Assert.Contains(" deaths=2 ", fields, StringComparison.Ordinal);
        Assert.Contains(" unbuff=suicide bound=yes", fields, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tusker_kill_during_the_suicides_countdown_purges_the_buffs_and_is_the_runs_death()
    {
        var rig = new Rig(buffed: true);
        rig.BlowEvery = 2; // 7 damage a blow: dead in 8 s, before the suicide's fifth line
        var scenario = rig.Scenario(new FakeWalk(), Core);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.Equal("killed", scenario.UnbuffNote);
        Assert.Equal(1, rig.Suicides); // it was sent; the scenario did not wait for it
        Assert.Equal(0, scenario.Suicides);
        Assert.Equal(1, scenario.Tally.VictimNotices);
        Assert.Equal(1, scenario.Tally.Deaths);
        Assert.Equal(0, CountBuffs(rig));
        Assert.True(scenario.Tally.Purges >= 1);
        Assert.Contains(" died=1 suicides=0 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" respawn=lifestone ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unbuffed_mage_sends_no_suicide_and_dies_once()
    {
        var rig = new Rig(buffed: false);
        var scenario = rig.Scenario(new FakeWalk(), Core);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.Equal(0, rig.Suicides);
        Assert.Equal(0, scenario.Suicides);
        Assert.Equal("none", scenario.UnbuffNote);
        Assert.Equal(1, scenario.Tally.VictimNotices);
        Assert.Equal(1, scenario.Tally.Deaths);
        Assert.Equal(1, scenario.Tally.DeathTeleports);
        Assert.Contains(Lines(rig), l => l.StartsWith("unbuff: the mage holds no enchantment a death would purge", StringComparison.Ordinal));
        Assert.DoesNotContain(Lines(rig), l => l.Contains("stage unbuff-dying", StringComparison.Ordinal));
        Assert.Contains(" died=1 suicides=0 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_vitae_alone_is_not_a_buff_and_a_mage_that_carries_only_it_does_not_suicide()
    {
        var rig = new Rig(buffed: false);
        rig.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(666, 1, 666, 0, 0, 0, -1, p, 0f, 0f, 0.0, VitaeType, 0, 0.95f, 0)));
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Unbuff);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Assert.Equal(0, rig.Suicides);
        Assert.Equal("none", scenario.UnbuffNote);
    }

    /// <summary>An item's lasting spell on its wielder: duration -1 and the item as the caster (ACE's death keeps it: <c>RemoveAllEnchantments</c>, <c>EnchantmentManager.cs</c> 358-365; the reducer's purge keeps it too).</summary>
    private static void HoldItemSpell(Rig rig, ushort spell = 1511) =>
        rig.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(spell, 1, 0x20, 0, 1, 0, -1, 0x80000A01, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 10f, 0)));

    /// <summary>A cooldown entry: a spell id above <c>short.MaxValue</c> (ACE's <c>StartCooldown</c>, <c>EnchantmentManager.cs</c> 284-300: <c>SpellCategory_Cooldown</c> 0x8000 plus the item's cooldown id) with the cooldown type bit.</summary>
    private static void HoldCooldown(Rig rig) =>
        rig.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(0x8005, 1, 0x8000, 0, 1, 0, 60, 0x80000A01, 0f, 0f, 0.0, EnchantmentRules.CooldownKind, 0, 0f, 0)));

    [Fact]
    public void A_lasting_item_spell_and_a_cooldown_are_not_buffs_so_a_mage_that_holds_only_them_does_not_suicide()
    {
        var rig = new Rig(buffed: false);
        HoldItemSpell(rig);
        HoldCooldown(rig);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Unbuff);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal(0, rig.Suicides);
        Assert.Equal("none", scenario.UnbuffNote);
        Assert.Equal(2, CountBuffs(rig)); // both are still in the registry: a death would not remove them, so a suicide could not clear them
        Assert.Contains(Lines(rig), l => l.StartsWith("unbuff: the mage holds no enchantment a death would purge", StringComparison.Ordinal));
    }

    [Fact]
    public void After_the_suicide_a_lasting_item_spell_that_the_purge_keeps_does_not_fail_the_run()
    {
        var rig = new Rig(buffed: true);
        rig.Damage = 3;
        HoldItemSpell(rig);
        var scenario = rig.Scenario(new FakeWalk(), Core);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal(1, rig.Suicides);
        Assert.Equal(1, scenario.Suicides);
        Assert.Equal("suicide", scenario.UnbuffNote);
        Assert.Equal([1511], rig.R.H.State.Self.Qualities.ActiveEnchantments.Where(e => e.SpellId != 666).Select(e => (int)e.SpellId)); // the two buffs went with the suicide's purge; the item's spell stayed, as on ACE
        Assert.Contains(Lines(rig), l => l.StartsWith("unbuff: the mage is buffed (2 enchantments)", StringComparison.Ordinal)); // the lasting spell is not counted
        Assert.Contains(" died=1 suicides=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_stages_are_logged_once_each_in_order()
    {
        var rig = new Rig(buffed: true);
        rig.Damage = 3;
        var scenario = rig.Scenario(new FakeWalk(), Core);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        var stages = Lines(rig).Where(l => l.StartsWith("stage ", StringComparison.Ordinal)).Select(l => l[6..]).ToList();
        Assert.Equal(
            ["arriving", "bind-place", "bind-teleporting", "bind-settle", "bind-use", "bind-line", "den-place", "den-teleporting", "den-settle", "unbuff", "unbuff-dying", "unbuff-arrival", "unbuff-vitals", "den-place", "den-teleporting", "den-settle", "death-mode", "death-wait", "respawn", "respawn-vitals"],
            stages);
    }

    // --- the bind ---

    [Fact]
    public void The_bind_places_the_player_at_Holtburg_uses_the_nearest_lifestone_and_logs_the_chat_line()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        var place = Assert.Single(rig.Places);
        Assert.Contains("poi", place, StringComparison.Ordinal);
        Assert.Contains("Holtburg", place, StringComparison.Ordinal);
        Assert.Equal([NearLifestone], rig.Used); // the far one (another landblock) is not the nearest
        Assert.True(scenario.Bound);
        Assert.Equal(NearLifestone, scenario.LifestoneGuid);
        Assert.Equal(HoltburgCell, scenario.LifestonePosition?.CellId);
        Assert.Contains(Lines(rig), l => l.Contains("the lifestone's chat line: The lifestone's attune line", StringComparison.Ordinal));
        Assert.Contains(Lines(rig), l => l.Contains("the attune line and the stamina halving followed the Use; 1 attune line(s), 0 other Magic line(s) ignored", StringComparison.Ordinal));
    }

    [Fact]
    public void The_bind_takes_on_the_UseMessage_and_the_stamina_halving_together_in_ACEs_order_with_the_UseDone_last()
    {
        var rig = new Rig(buffed: false);
        var before = rig.R.H.State.Self.Qualities.TryGetVitalLevels(3, out var stamina, out _) ? stamina : 0u;
        Assert.True(before >= 2);
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.True(scenario.Bound);
        Assert.Equal(HoltburgCell, scenario.LifestonePosition?.CellId);
        Assert.True(rig.R.H.State.Self.Qualities.TryGetVitalLevels(3, out var after, out _));
        Assert.Equal((uint)Math.Round(before / 2f), after);
        var lines = Lines(rig).ToList();
        Assert.Contains(lines, l => l.StartsWith("bind: the stamina halved (" + before.ToString(CultureInfo.InvariantCulture) + " to " + after.ToString(CultureInfo.InvariantCulture) + "): ACE's second attune signal", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("the attune line and the stamina halving followed the Use", StringComparison.Ordinal));
        // The bound line comes after the UseDone (the rig's order): the scenario never binds on the line and the halving alone.
        Assert.True(lines.FindIndex(l => l.StartsWith("bound at the lifestone", StringComparison.Ordinal)) > lines.FindIndex(l => l.StartsWith("bind: the stamina halved", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_UseMessage_without_the_stamina_halving_does_not_bind_and_the_run_fails_naming_the_missing_signal()
    {
        var rig = new Rig(buffed: false);
        rig.UseStaminaHalving = false;
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.False(scenario.Bound);
        Assert.Contains("the bind did not take: the attune line came but the stamina was not halved (ACE halves it in the same action, Lifestone.cs 64-69) within 15 s of the Use (UseDone seen; 0 other Magic line(s) ignored)", scenario.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Places, p => !p.Contains("poi", StringComparison.Ordinal)); // it never left for the den
    }

    [Fact]
    public void An_expiry_line_between_the_Use_and_the_attune_does_not_bind_and_the_range_checks_0x0498_still_fails_the_bind()
    {
        // The review's case (package 2, finding 5): the UseDone first (the UNCERTAIN order), an item buff's expiry while the Sanctuary motion plays, then ACE's range check fails.
        var rig = new Rig(buffed: false);
        rig.UseDoneFirst = true;
        rig.UseOtherLines.Add("The spell Impenetrability VI on Leather Coat has expired.");
        rig.UseWeenieError = 0x0498;
        var scenario = rig.Scenario(null, DeathStages.Bind | DeathStages.Den);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.False(scenario.Bound);
        Assert.Contains("the bind did not take: the server answered WeenieError 0x0498 (YouHaveMovedTooFar", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(Lines(rig), l => l == "bind: ignoring a Magic line that is not the attune: The spell Impenetrability VI on Leather Coat has expired.");
        Assert.DoesNotContain(rig.Places, p => !p.Contains("poi", StringComparison.Ordinal)); // it never left for the den on the expiry line
    }

    [Fact]
    public void The_known_non_attune_Magic_lines_are_ignored_even_with_the_stamina_halved_and_the_timeout_names_the_missing_line()
    {
        // ACE's texts: Player_Death.cs 934 and 942, Player_Xp.cs 164, EnchantmentManager.cs 345.
        var rig = new Rig(buffed: false);
        rig.UseLine = null; // no UseMessage: the four other Magic lines ACE sends must not stand in for it
        rig.UseOtherLines.Add("You're no longer protected by the Lifestone's magic!");
        rig.UseOtherLines.Add("Your actions have dispelled the Lifestone's magic!");
        rig.UseOtherLines.Add("Your experience has reduced your Vitae penalty!");
        rig.UseOtherLines.Add("The spell Armor Self VII on Leather Coat has expired.");
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.False(scenario.Bound);
        Assert.Contains("the bind did not take: the stamina halving came but no attune line (a Magic chat line) within 15 s of the Use (UseDone seen; 4 other Magic line(s) ignored)", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(4, Lines(rig).Count(l => l.StartsWith("bind: ignoring a Magic line that is not the attune: ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("The spell Flame Bolt VII on Tusker Slave has expired.", true)]
    [InlineData("You're no longer protected by the Lifestone's magic!", true)]
    [InlineData("Your actions have dispelled the Lifestone's magic!", true)]
    [InlineData("Your experience has reduced your Vitae penalty!", true)]
    [InlineData("The spell has expired", false)]
    [InlineData("You have attuned your spirit to this Lifestone. You will resurrect here after you die.", false)]
    public void The_non_attune_Magic_lines_are_ACEs_exact_texts(string text, bool nonAttune)
    {
        Assert.Equal(nonAttune, DeathScenario.IsNonAttuneMagicLine(text));
        Assert.Equal("You're no longer protected by the Lifestone's magic!", DeathScenario.ProtectionExpiredLine);
        Assert.Equal("Your actions have dispelled the Lifestone's magic!", DeathScenario.ProtectionDispelledLine);
        Assert.Equal("Your experience has reduced your Vitae penalty!", DeathScenario.VitaeReducedLine);
    }

    [Fact]
    public void The_bind_is_done_on_the_attune_line_not_on_the_UseDone_and_the_den_waits_for_it()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        rig.UseDoneFirst = true; // the order the review left UNCERTAIN: the UseDone 1 s after the Use, ACE's lifestone chain (the Sanctuary motion, then the range check) 6 s later
        rig.UseLineDelay = 6;
        var scenario = rig.Scenario(null, DeathStages.Bind | DeathStages.Den);

        var sawWaiting = false;
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            status = rig.Frame(scenario);
            if (rig.Used.Count == 1 && rig.Now > 0 && scenario.Stage == "bind-line" && !scenario.Bound)
            {
                sawWaiting = true;
                Assert.Single(rig.Places); // no teleport to the den while the line is outstanding
            }
        }

        Assert.True(sawWaiting, "the scenario should have stayed in bind-line after the UseDone");
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(scenario.Bound);
        Assert.Equal(3, rig.Places.Count); // Holtburg first, the den after the line, and Holtburg again: no run ends in the den
    }

    [Fact]
    public void The_bind_walks_the_player_to_the_lifestones_use_radius_before_the_Use_and_logs_the_distances()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var approach = new FakeApproach(rig);
        var scenario = rig.Scenario(null, DeathStages.Bind, approach: approach);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(1, approach.Prepared);
        Assert.Equal(0, approach.PlacesAtPrepare); // prepared before the teleport to Holtburg, so the arrival places the walker
        Assert.Equal(1, approach.Begins);
        Assert.Equal(HoltburgCell, approach.TargetCell);
        Assert.Equal(DeathScenario.LifestoneApproachMetres, approach.StopWithin); // the CreateObject carried no UseRadius: the named placeholder
        Assert.True(approach.TicksBeforeBegin > 0, "the walker must be ticked from the teleport on, or the teleport's position never places it (the live bug)");
        Assert.Equal(0, approach.UsesWhileWalking); // the Use went out after the walk, not during it
        Assert.True(approach.Abandoned);
        Assert.True(approach.Disposed); // released once the bind took
        Assert.Equal([NearLifestone], rig.Used);
        Assert.True(scenario.Bound);
        var lines = Lines(rig).ToList();
        Assert.Contains(lines, l => l.StartsWith("bind: the player is 14.", StringComparison.Ordinal) && l.Contains("m from the lifestone; its UseRadius is not sent (placeholder 1.5 m, TODO(m3-lifestone-radius))", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("bind: the approach ended (arrived); the player is 1.1 m from the lifestone", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("bind: using the lifestone from 1.1 m", StringComparison.Ordinal));
    }

    [Fact]
    public void The_approach_stops_at_the_UseRadius_the_CreateObject_carries()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival, useRadius: 2.5f);
        var approach = new FakeApproach(rig);
        var scenario = rig.Scenario(null, DeathStages.Bind, approach: approach);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Assert.Equal(2.5f, approach.StopWithin);
        Assert.Contains(Lines(rig), l => l.Contains("its UseRadius is 2.5 (from its CreateObject)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_player_already_within_the_stop_radius_is_not_walked()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival, lifestoneX: 50.5f);
        rig.LandY = 29.5f;
        var approach = new FakeApproach(rig);
        var scenario = rig.Scenario(null, DeathStages.Bind, approach: approach);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Assert.Equal(0, approach.Begins);
        Assert.Equal([NearLifestone], rig.Used);
    }

    [Fact]
    public void An_approach_that_fails_still_sends_the_Use_with_a_warning_and_ACEs_answer_decides()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        rig.UseLine = null; // blocked and out of range: ACE says nothing (neither the line nor the stamina halving)
        rig.UseStaminaHalving = false;
        var approach = new FakeApproach(rig) { EndsFailed = true };
        var scenario = rig.Scenario(null, DeathStages.Bind, approach: approach);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Equal([NearLifestone], rig.Used);
        Assert.Contains(Lines(rig), l => l.Contains("the walk said: the fake approach is blocked", StringComparison.Ordinal));
        Assert.Contains(Lines(rig), l => l.Contains("the player did not reach the stop radius", StringComparison.Ordinal));
        Assert.Contains("the bind did not take: no attune line (a Magic chat line) and no stamina halving (0x02E9) within 15 s", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_approach_walk_that_cannot_start_warns_and_the_Use_is_sent_anyway()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var approach = new FakeApproach(rig) { CannotBegin = true };
        var scenario = rig.Scenario(null, DeathStages.Bind, approach: approach);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario)); // the scripted server answers the Use
        Assert.Equal([NearLifestone], rig.Used);
        Assert.Contains(Lines(rig), l => l.StartsWith("bind: no approach walk could start (the approach walker is not placed", StringComparison.Ordinal) && l.EndsWith("the Use is sent from where the player stands", StringComparison.Ordinal));
        Assert.True(scenario.Bound);
    }

    [Fact]
    public void The_chat_channel_joins_after_the_Use_are_ignored_and_the_bind_still_takes()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        rig.UseNoise = true; // WeenieError 0x051C three times, then the attune line
        var scenario = rig.Scenario(null, DeathStages.Bind, approach: new FakeApproach(rig));

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.True(scenario.Bound);
        Assert.Equal(3, Lines(rig).Count(l => l.StartsWith("bind: ignoring WeenieError 0x051C after the Use (not use-related)", StringComparison.Ordinal)));
        Assert.DoesNotContain(0x051Cu, DeathScenario.BindFailureCodes);
        Assert.Contains(0x0498u, DeathScenario.BindFailureCodes);
    }

    [Fact]
    public void A_missing_attune_line_fails_the_bind_after_fifteen_seconds_naming_the_distance()
    {
        var rig = new Rig(buffed: false);
        rig.UseLine = null;
        rig.UseStaminaHalving = false; // ACE sends both in one action: neither comes
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Contains("the bind did not take: no attune line (a Magic chat line) and no stamina halving (0x02E9) within 15 s of the Use", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("m from the lifestone", scenario.Failure, StringComparison.Ordinal);
        Assert.False(scenario.Bound);
    }

    [Fact]
    public void A_WeenieError_after_the_Use_fails_the_bind_naming_its_code()
    {
        var rig = new Rig(buffed: false);
        rig.UseWeenieError = 0x0498; // YouHaveMovedTooFar: the chain's range check failed
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Contains("the bind did not take: the server answered WeenieError 0x0498 (YouHaveMovedTooFar", scenario.Failure, StringComparison.Ordinal);
        Assert.False(scenario.Bound);
        Assert.DoesNotContain(rig.Places, p => !p.Contains("poi", StringComparison.Ordinal)); // it never left for the den
    }

    [Fact]
    public void A_bind_with_no_lifestone_in_sixty_metres_fails_loudly()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival, lifestone: false);
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.StartsWith("bind: no lifestone (an object with the BF_LIFESTONE description flag) within 60 m", scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(rig.Used);
        Assert.False(scenario.Bound);
    }

    [Fact]
    public void A_lifestone_use_the_server_refuses_fails_the_bind()
    {
        var rig = new Rig(buffed: false);
        rig.UseError = 0x0480; // any non-zero UseDone
        var scenario = rig.Scenario(null, DeathStages.Bind);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.StartsWith("bind: using the lifestone: ServerError", scenario.Failure, StringComparison.Ordinal);
        Assert.False(scenario.Bound);
    }

    // --- the den ---

    [Fact]
    public void A_settle_off_the_dens_floor_fails_loudly_as_the_fight_does()
    {
        var rig = new Rig(buffed: false);
        rig.DenCell = NotOnTheFloor;
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Contains("den: the player settled in cell 0x01050341, which is not a floor cell of the den (floor cell no)", scenario.Failure, StringComparison.Ordinal);
    }

    // --- the death wait ---

    [Fact]
    public void The_time_limit_fails_loudly_with_the_hits_evades_and_the_lowest_health_share()
    {
        var rig = new Rig(buffed: false);
        rig.Lethal = false;
        rig.Damage = 1;
        rig.EvadeEvery = 3;
        var scenario = rig.Scenario(new FakeWalk(), Core, new DeathOptions { Seconds = 40 });

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.StartsWith("death: 40 s passed and the tuskers did not kill the player (hits=", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("evades=", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("health_min_pct=", scenario.Failure, StringComparison.Ordinal);
        Assert.True(scenario.Tally.HitsTaken >= 1 && scenario.Tally.Evades >= 1);
        Assert.False(scenario.Outcome().Ok);
    }

    [Fact]
    public void A_quiet_minute_moves_the_wait_to_the_next_tour_cell()
    {
        var rig = new Rig(buffed: false);
        rig.Tuskers = false;
        var walk = new FakeWalk();
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death, new DeathOptions { Seconds = 200 });

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.True(walk.Legs >= 3, "legs " + walk.Legs); // the first leg, then one per quiet minute within 200 s
        Assert.Contains(Lines(rig), l => l.StartsWith("death: no blow in 60 s; moving on: fake leg 2", StringComparison.Ordinal));
        Assert.Contains("did not kill the player", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_death_in_the_middle_of_a_leg_abandons_the_walk_and_the_respawn_is_still_reached_and_judged()
    {
        // The tuskers kill the mage while a leg is still running (likely on a second leg, begun after a quiet minute). The walk is abandoned where the death is read, and no later stage ticks it: a walk kept ticking
        // would drive the dead body (Stuck) and then read the death's teleport as a failure (the player moved more than RouteDriver.TeleportMetres in one frame) and fail the run in respawn, before the respawn was judged.
        var rig = new Rig(buffed: false);
        var walk = new FakeWalk { TicksPerLeg = int.MaxValue }; // the leg never arrives: four blows of 7 kill the mage (28 health) at about 16 s, in the middle of it
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal(1, scenario.Tally.VictimNotices);
        Assert.Equal(1, walk.Legs);
        Assert.Equal(1, walk.AbandonsMidLeg); // abandoned at the death, while walking
        Assert.Equal(0, walk.TicksAfterAbandon); // and never ticked again: respawn, respawn-vitals and the end do not tick the walk
        Assert.DoesNotContain(Lines(rig), l => l.Contains("the walk failed", StringComparison.Ordinal));
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" respawn=lifestone ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_health_share_is_logged_on_each_change_of_ten_percent()
    {
        var rig = new Rig(buffed: false);
        rig.Damage = 3;
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        var shares = Lines(rig).Where(l => l.StartsWith("health ", StringComparison.Ordinal)).ToList();
        Assert.True(shares.Count >= 5, string.Join(" | ", shares)); // 100% down to 0 in blows of about 10%
        Assert.Equal(shares.Count, shares.Distinct().Count());
    }

    [Fact]
    public void StopAfterHits_ends_three_seconds_after_the_first_blow_with_the_health_share_under_full_and_without_a_death()
    {
        var rig = new Rig(buffed: false);
        rig.Lethal = false;
        rig.Damage = 1;
        rig.BlowEvery = 20;
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death | DeathStages.Regear, new DeathOptions { Seconds = 300, StopAfterHits = true });

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.True(scenario.StoppedAfterHits);
        Assert.False(scenario.DiedBeforeStop);
        Assert.Equal(0, scenario.Tally.VictimNotices);
        Assert.Equal(0, scenario.Tally.Deaths);
        Assert.True(scenario.Tally.HitsTaken >= 1);
        Assert.InRange(scenario.DeathSeconds, 15, 30); // the first blow 20 s after the arrival (the wait began a few seconds after it), then 3 s
        Assert.Null(scenario.Regear); // nothing died: no regear
        Assert.Contains(Lines(rig), l => l.StartsWith("death: stopping after", StringComparison.Ordinal));
        Assert.Equal(0, rig.ModeChanges);

        // The stop does not log off in the den (the next login would start among the tuskers): a place to Holtburg first, logged, then the end.
        Assert.Single(Lines(rig), l => l.StartsWith("leaving the den before the log off", StringComparison.Ordinal));
        Assert.Equal(2, rig.Places.Count); // the den, then Holtburg
        Assert.Contains("poi", rig.Places[1], StringComparison.Ordinal);
        Assert.NotEqual(0x0105u, rig.R.H.State.Self.Position!.Value.CellId >> 16);
    }

    [Fact]
    public void StopAfterHits_in_the_middle_of_a_leg_abandons_it_and_the_stages_after_the_stop_do_not_read_the_abandoned_leg_as_a_failure()
    {
        // The first blow lands while the leg is still running. The stop abandons the leg; a walk that kept its driver would answer Failed on its next tick (RouteDriver.Abandon marks the route Failed), so no stage after the stop
        // may tick it: the regear is skipped when nothing died, and the run goes to leave-den and the end.
        var rig = new Rig(buffed: false);
        rig.Lethal = false;
        rig.Damage = 1;
        rig.BlowEvery = 20;
        var walk = new FakeWalk { TicksPerLeg = int.MaxValue };
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death | DeathStages.Regear, new DeathOptions { Seconds = 300, StopAfterHits = true });

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.True(scenario.StoppedAfterHits);
        Assert.Equal(1, walk.Legs);
        Assert.Equal(1, walk.AbandonsMidLeg); // the stop abandoned the running leg (the conclusion's second abandon found nothing walking)
        Assert.Equal(0, walk.TicksAfterAbandon);
        Assert.Null(scenario.Regear);
        Assert.DoesNotContain(Lines(rig), l => l.Contains("the walk failed", StringComparison.Ordinal));
        Assert.Equal("done", scenario.Stage);
        Assert.NotEqual(0x0105u, rig.R.H.State.Self.Position!.Value.CellId >> 16);
    }

    [Fact]
    public void A_run_that_fails_in_the_den_places_out_of_it_before_its_log_off_best_effort()
    {
        var rig = new Rig(buffed: false);
        rig.Lethal = false;
        rig.Damage = 1;
        var scenario = new DeathScenario(GearKit.Of(Array.Empty<GearItem>()), new Dictionary<uint, SpellInfo>(), new DeathOptions { Seconds = 6 }, new FakeWalk(), rig.R.H.Log, endSession: true, stages: DeathStages.Den | DeathStages.Death);

        rig.Run(scenario, maxSeconds: 120); // the tuskers do not kill it within 6 s: the run fails in the den, and the log off is never confirmed in the rig

        Assert.Contains("did not kill the player", scenario.Failure, StringComparison.Ordinal);
        Assert.Single(Lines(rig), l => l.StartsWith("leaving the den before the log off", StringComparison.Ordinal));
        Assert.Equal(2, rig.Places.Count);
        Assert.NotEqual(0x0105u, rig.R.H.State.Self.Position!.Value.CellId >> 16);
        var kinds = rig.R.H.Net.Kinds();
        Assert.Equal(1, kinds.Count(k => k == FakeSendKind.LogOff));
        Assert.True(kinds.LastIndexOf(FakeSendKind.Talk) < kinds.IndexOf(FakeSendKind.LogOff), "the place goes out before the log off");
    }

    [Fact]
    public void A_den_stage_failure_with_the_host_owning_the_session_leaves_the_den_and_only_then_ends_failed()
    {
        // endSession: false is the game's leg (DeathLegRunner), which logs off as soon as the scenario returns Failed: a Failed returned in the den logged the unbuffed mage off among the tuskers, and the next login (a gear run,
        // the next leg) started under attack. The failure is set at once; the run stays Running until the leave is over, and sends no log off of its own.
        var rig = new Rig(buffed: false);
        rig.Lethal = false;
        rig.Damage = 1;
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death, new DeathOptions { Seconds = 6 });

        var status = ScenarioStatus.Running;
        var failedAt = double.NaN;
        for (var i = 0; i < 2000 && status == ScenarioStatus.Running; i++)
        {
            status = rig.Frame(scenario);
            if (double.IsNaN(failedAt) && scenario.Failure is not null)
            {
                failedAt = rig.Now;
                Assert.Equal(ScenarioStatus.Running, status); // not Failed in the den: the leave runs first
            }
        }

        Dump(rig, scenario);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("did not kill the player", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(2, rig.Places.Count); // the den, then Holtburg (the rig answers a send on the frame after it goes out)
        Assert.InRange(rig.Now - failedAt, DenExit.SettleSeconds, DenExit.SettleSeconds + 1); // the rig lands the player at once; then the settle
        Assert.Single(Lines(rig), l => l.StartsWith("leaving the den before the log off", StringComparison.Ordinal));
        Assert.Contains(Lines(rig), l => l.StartsWith("left the den: the player is present in cell 0xA9B40019", StringComparison.Ordinal));
        Assert.Contains("poi", rig.Places[1], StringComparison.Ordinal);
        Assert.NotEqual(0x0105u, rig.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.LogOff)); // the host logs off, not the scenario
        Assert.DoesNotContain(Lines(rig), l => l.StartsWith("logging off before the run ends as failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_runs_leave_that_the_server_never_answers_is_a_warning_and_the_run_still_ends_failed()
    {
        var rig = new Rig(buffed: false);
        rig.Lethal = false;
        rig.Damage = 1;
        rig.RefusePlaceOutOfDen = true;
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death, new DeathOptions { Seconds = 6 });

        var status = ScenarioStatus.Running;
        var failedAt = double.NaN;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            status = rig.Frame(scenario);
            if (double.IsNaN(failedAt) && scenario.Failure is not null)
            {
                failedAt = rig.Now;
            }
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(2, rig.Places.Count);
        Assert.True(rig.Now - failedAt <= DenExit.TeleportSeconds + 1, "the leave is bounded: " + (rig.Now - failedAt).ToString("0.0", CultureInfo.InvariantCulture) + " s");
        Assert.Contains(Lines(rig), l => l.StartsWith("leaving the den: the place was not confirmed", StringComparison.Ordinal) || l.StartsWith("leaving the den: the player did not arrive", StringComparison.Ordinal));
        Assert.Equal(0x0105u, rig.R.H.State.Self.Position!.Value.CellId >> 16); // where it stands
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.LogOff));
    }

    [Fact]
    public void A_refused_place_out_of_the_den_is_a_warning_and_the_run_still_ends()
    {
        var rig = new Rig(buffed: false);
        rig.Lethal = false;
        rig.Damage = 1;
        rig.BlowEvery = 20;
        rig.RefusePlaceOutOfDen = true;
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death, new DeathOptions { Seconds = 300, StopAfterHits = true });

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Contains(Lines(rig), l => l.StartsWith("leaving the den: the place was not confirmed", StringComparison.Ordinal) || l.StartsWith("leaving the den: the player did not arrive", StringComparison.Ordinal));
        Assert.Equal("done", scenario.Stage);
    }

    [Fact]
    public void StopAfterHits_with_an_early_death_passes_reports_it_does_not_judge_the_respawn_and_ends_cleanly()
    {
        var rig = new Rig(buffed: false);
        rig.RespawnCell = 0xB0B40019; // no lifestone near: a judged respawn would fail
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death | DeathStages.Regear, new DeathOptions { StopAfterHits = true });

        // Kill it before the stop: the damage is lethal in one blow, so the death comes with the first blow.
        rig.Damage = 100;
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        Assert.True(scenario.DiedBeforeStop);
        Assert.False(scenario.StoppedAfterHits);
        Assert.Equal(1, scenario.Tally.VictimNotices);
        Assert.Equal(1, scenario.Tally.Deaths);
        Assert.Null(scenario.Regear);
        Assert.Contains(Lines(rig), l => l.Contains("the mage died before the stop", StringComparison.Ordinal));
        Assert.Contains(Lines(rig), l => l.StartsWith("death: the tuskers killed the player after", StringComparison.Ordinal) && l.Contains("(victim notices 1, hits 0, evades 0, protected 0)", StringComparison.Ordinal));
        Assert.Contains(Lines(rig), l => l.StartsWith("respawn: elsewhere (report-only under StopAfterHits)", StringComparison.Ordinal));
        Assert.DoesNotContain(Lines(rig), l => l.StartsWith("leaving the den", StringComparison.Ordinal)); // it is already at the lifestone
        Assert.Single(rig.Places);

        // A one-blow death has no 0x01B2: ACE's TakeDamage updates the health and, at 0, calls OnDeath and Die and returns before the DefenderNotification (Player_Combat.cs 507-529), so the tally reads hits_taken=0 and
        // damage_taken=0 (the 0x01B2 carries the melee damage) while the lethal 0x02E9 is seen (health_min_pct 0) and died=1. D36's judge (hits_taken >= 1, damage_taken > 0) therefore reads false for it, and only for it
        // here: whether a one-blow death is exempted is the owner's question (package 4, finding 7); the judge is unchanged.
        Assert.Equal(0, scenario.Tally.HitsTaken);
        Assert.Equal(0L, scenario.Tally.DamageTaken);
        Assert.Equal(0.0, scenario.Tally.HealthMinPercent);
        var outcome = scenario.Outcome();
        Assert.Contains(" died=1 suicides=0 ", outcome.Line, StringComparison.Ordinal);
        Assert.Contains(" hits_taken=0 damage_taken=0 ", outcome.Line, StringComparison.Ordinal);
        Assert.Contains(outcome.Problems, p => p.StartsWith("hits_taken=0 (>= 1 expected, D36)", StringComparison.Ordinal));
        Assert.Contains(outcome.Problems, p => p.StartsWith("damage_taken=0 (> 0 expected, D36)", StringComparison.Ordinal));
        Assert.DoesNotContain(outcome.Problems, p => p.StartsWith("died=", StringComparison.Ordinal));
    }

    [Fact]
    public void The_measures_are_frozen_in_the_world_at_the_conclusion_so_the_log_offs_empty_object_table_cannot_change_the_verdict()
    {
        var rig = new Rig(buffed: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && scenario.Stage != "respawn-vitals"; i++)
        {
            status = rig.Frame(scenario);
        }

        Assert.Equal("respawn-vitals", scenario.Stage);
        while (status == ScenarioStatus.Running && scenario.Stage == "respawn-vitals")
        {
            status = rig.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Single(Lines(rig), l => l.StartsWith("verdict measures frozen in the world", StringComparison.Ordinal));
        Assert.Contains(" respawn=lifestone ", scenario.VerdictFields(), StringComparison.Ordinal);

        // The log off: the reducer's LeaveWorld clears the object table (the real cause of the old late "elsewhere"), the position and the phase go with it.
        LeaveTheWorld(rig);
        Assert.Equal(0, rig.R.H.State.Objects.Count);
        Assert.Equal(DeathRespawn.Elsewhere, DeathLegVerdict.ClassifyRespawn(scenario.Tally, rig.R.H.State.Objects)); // a re-measure after the log off reads otherwise
        Assert.Contains(" respawn=lifestone ", scenario.VerdictFields(), StringComparison.Ordinal); // the frozen verdict does not
        Assert.Contains(" respawn=lifestone ", scenario.Outcome(38.0, 41.0).Line, StringComparison.Ordinal);
        Assert.Contains(" death_frame_max_ms=38.0 fight_frame_max_ms=41.0 ", scenario.Outcome(38.0, 41.0).Line, StringComparison.Ordinal); // the game's frame figures are applied to the frozen measures
        Assert.DoesNotContain(Lines(rig), l => l.Contains("re-measure", StringComparison.Ordinal)); // no warning: nothing is wrong with the table
        Assert.DoesNotContain("respawn=elsewhere", string.Join("; ", scenario.Outcome().Problems), StringComparison.Ordinal);

        scenario.FreezeMeasures(rig.View); // idempotent, and out of the world a no-op
        Assert.Single(Lines(rig), l => l.StartsWith("verdict measures frozen in the world", StringComparison.Ordinal));
    }

    private static void LeaveTheWorld(Rig rig)
    {
        var h = rig.R.H.R;
        h.Phase(SessionPhase.InWorld, SessionPhase.CharacterSelect);
        h.Reducer.OnCharacterLogOff(h.Context);
    }

    [Fact]
    public void Out_of_the_world_the_late_respawn_check_is_skipped()
    {
        var rig = new Rig(buffed: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && scenario.Stage != "respawn-vitals"; i++)
        {
            status = rig.Frame(scenario);
        }

        // Left in the stage that reads the respawn, the scenario is stopped by a log off before it concludes: nothing is frozen, and the table is empty.
        LeaveTheWorld(rig);
        Assert.NotEqual(SessionPhase.InWorld, rig.R.H.State.Phase);
        scenario.FreezeMeasures(rig.View);
        Assert.DoesNotContain(Lines(rig), l => l.StartsWith("verdict measures frozen", StringComparison.Ordinal));
        _ = scenario.Outcome();
        Assert.DoesNotContain(Lines(rig), l => l.Contains("re-measure", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ the vitae stage (D37)

    [Fact]
    public void The_vitae_stage_pays_the_experience_ACE_s_rule_asks_and_waits_for_the_penalty_to_leave_the_registry()
    {
        var rig = new Rig(buffed: false);
        rig.HoldVitae(0.60f);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae);
        scenario.LevelXp = _ => 1_000_000_000_000UL;

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal([6_875_357L], rig.XpAsked); // 40% from level 126: the sum of the thresholds of 0.60 to 0.99
        var fields = scenario.VerdictFields();
        Assert.Contains(" vitae=40 ", fields, StringComparison.Ordinal); // the measures were frozen before the grant: the penalty the death put on
        Assert.Contains(" vitae_paid=6875357 vitae_after=0", fields, StringComparison.Ordinal);
        Assert.True(scenario.Outcome().Ok); // a run without the death stage judges nothing about the death
        Assert.False(rig.R.H.State.Self.Qualities.TryGetVitae(out _));
        Assert.Contains(Lines(rig), l => l.StartsWith("vitae: paid 6,875,357 experience, 0% left", StringComparison.Ordinal));
    }

    [Fact]
    public void The_vitae_stage_counts_the_pool_against_the_sum_and_uses_the_death_level()
    {
        var rig = new Rig(buffed: false);
        rig.HoldVitae(0.95f, level: 100, pool: 1000);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae);
        scenario.LevelXp = _ => ulong.MaxValue;

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(EffectRules.VitaeExperienceToClear(0.95f, 100, 1000), Assert.Single(rig.XpAsked));
        Assert.Contains(" vitae_paid=" + rig.XpAsked[0].ToString(CultureInfo.InvariantCulture) + " vitae_after=0", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_vitae_stage_grants_across_a_level_logs_the_crossing_and_never_grants_more_than_clears_D42()
    {
        // Replaces M3 D37's refusal test (a grant that would cross level 127 failed by name without being sent): the owner's D42 allows the crossing, so the same rig now sends exactly the clearing amount and logs it.
        var rig = new Rig(buffed: false);
        rig.HoldVitae(0.60f, total: 5_000_000);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae);
        scenario.LevelXp = level => level == 127 ? 10_000_000UL : null; // 5,000,000 + 6,875,357 is past level 127's 10,000,000

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal([6_875_357L], rig.XpAsked); // exactly what clears the 40%, not a point more
        Assert.Contains(Lines(rig), l => l.Contains("vitae_level_crossed=yes level=126->127", StringComparison.Ordinal));
        Assert.Contains(" vitae_paid=6875357 vitae_after=0", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_run_still_pays_the_vitae_with_the_mages_live_numbers_and_ends_failed_with_its_own_reason_P5a_r3()
    {
        // Round 3: both death runs failed in the fight, the success-only vitae stage never ran and BSD Mage's vitae grew to 40% (level 126, 4,286,609,098 experience: the build's grant). The failure path now pays it too,
        // across the level as D42 allows (level 127's threshold is set inside the grant here to prove the crossing is no refusal), and the run still ends Failed with the bind's reason.
        var rig = new Rig(buffed: false);
        rig.UseStaminaHalving = false;
        rig.HoldVitae(0.60f, level: 126, total: 4_286_609_098);
        var scenario = rig.Scenario(null, DeathStages.Bind | DeathStages.Vitae);
        scenario.LevelXp = level => level == 127 ? 4_290_000_000UL : null;

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.StartsWith("bind: ", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal([6_875_357L], rig.XpAsked); // exactly what clears the 40% at level 126
        Assert.False(rig.R.H.State.Self.Qualities.TryGetVitae(out _));
        var lines = Lines(rig);
        Assert.Contains(lines, l => l.StartsWith("vitae after the failure:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("vitae_level_crossed=yes level=126->127", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("vitae: paid 6,875,357 experience, 0% left", StringComparison.Ordinal));
        Assert.Contains(" vitae_paid=6875357 vitae_after=0", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_vitae_grant_that_stays_inside_the_level_logs_no_crossing_D42()
    {
        var rig = new Rig(buffed: false);
        rig.HoldVitae(0.60f, total: 5_000_000);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae);
        scenario.LevelXp = level => level == 127 ? 1_000_000_000_000UL : null;

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.DoesNotContain(Lines(rig), l => l.Contains("vitae_level_crossed", StringComparison.Ordinal));
    }

    [Fact]
    public void The_vitae_stage_fails_when_no_experience_table_is_loaded()
    {
        var rig = new Rig(buffed: false);
        rig.HoldVitae(0.99f);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae); // no LevelXp, and the scripted session has no XpTable

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Empty(rig.XpAsked);
        Assert.Contains("TODO(m3-vitae-xptable)", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_without_the_gear_stage_waits_for_the_description_before_its_first_stage_and_never_logs_in_when_already_in_the_world()
    {
        var rig = new Rig(buffed: false, describe: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae);
        scenario.LevelXp = _ => ulong.MaxValue;

        for (var i = 0; i < 80; i++)
        {
            Assert.Equal(ScenarioStatus.Running, rig.Frame(scenario)); // 20 s: past the vitae stage's own 10 s limit
        }

        Assert.Equal("arriving", scenario.Stage);
        Assert.Null(scenario.Failure);
        Assert.Empty(rig.XpAsked);

        rig.Describe();
        rig.HoldVitae(0.99f);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Single(rig.XpAsked);
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.Login)); // the session was in the world: nothing to log in
    }

    [Fact]
    public void The_vitae_stage_is_skipped_when_the_registry_holds_no_vitae()
    {
        var rig = new Rig(buffed: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Empty(rig.XpAsked);
        Assert.Contains(" vitae_paid=0 vitae_after=0", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(Lines(rig), l => l.StartsWith("vitae: the registry holds none", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_vitae_stage_the_fields_read_n_a_and_the_vitae_stays()
    {
        var rig = new Rig(buffed: false);
        rig.HoldVitae(0.60f);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.None);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Empty(rig.XpAsked);
        Assert.Contains(" vitae_paid=n/a vitae_after=n/a", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.True(rig.R.H.State.Self.Qualities.TryGetVitae(out _));
    }

    [Fact]
    public void A_vitae_that_stays_after_the_grant_fails_the_run_after_ten_seconds_naming_the_percentage()
    {
        var rig = new Rig(buffed: false) { XpClearsVitae = false };
        rig.HoldVitae(0.60f);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Vitae);
        scenario.LevelXp = _ => ulong.MaxValue;

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Single(rig.XpAsked);
        Assert.Contains("vitae: the vitae is still 40% 10 s after the grant", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(" vitae_paid=6875357 vitae_after=40", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_vitae_stage_follows_the_death_in_a_whole_run_and_the_verdict_keeps_the_death_s_vitae()
    {
        var rig = new Rig(buffed: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death | DeathStages.Vitae);
        scenario.LevelXp = _ => ulong.MaxValue;
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && scenario.Stage != "respawn-vitals"; i++)
        {
            status = rig.Frame(scenario);
        }

        rig.R.PrivateInt(PlayerQualities.LevelProperty, 126); // the scripted death does not send the level or its death level
        rig.R.PrivateInt(EffectRules.VitaeLevelProperty, 126);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal(EffectRules.VitaeExperienceToClear(0.95f, 126, 0), Assert.Single(rig.XpAsked));
        var fields = scenario.VerdictFields();
        Assert.Contains(" vitae=5 ", fields, StringComparison.Ordinal);
        Assert.Contains(" vitae_after=0", fields, StringComparison.Ordinal);
        Assert.Contains(" respawn=lifestone ", fields, StringComparison.Ordinal);
    }

    [Fact]
    public void A_death_that_arrives_far_from_any_lifestone_fails_respawn_elsewhere_naming_the_cell()
    {
        var rig = new Rig(buffed: false);
        rig.RespawnCell = 0xB0B40019; // far from the Holtburg lifestone
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Contains("respawn=elsewhere", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("0xB0B40019", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(" respawn=elsewhere ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.False(scenario.Outcome().Ok);
    }

    [Fact]
    public void A_respawn_at_a_lifestone_other_than_the_one_the_bind_used_reads_elsewhere_and_fails_naming_the_bound_cell()
    {
        // Bound at Holtburg's lifestone, the death's teleport lands beside the far lifestone (another landblock): a bind that did not hold, whatever lifestone the arrival is near.
        var rig = new Rig(buffed: false, startCell: DenArrival);
        rig.RespawnCell = FarCell;
        var scenario = rig.Scenario(new FakeWalk(), Core);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.True(scenario.Bound);
        Assert.Equal(HoltburgCell, scenario.LifestonePosition?.CellId);
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(scenario.Tally, rig.R.H.State.Objects)); // the far lifestone is within 15 m of the arrival: the rule of a run without a bind
        Assert.Contains("respawn=elsewhere: the player arrived in cell 0xA8B40019 more than 15 m from the lifestone the bind used (cell 0xA9B40019)", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(" respawn=elsewhere ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(Lines(rig), l => l.Contains("respawn: elsewhere (arrival cell 0xA8B40019, classified against the lifestone the bind used in cell 0xA9B40019", StringComparison.Ordinal));
        Assert.False(scenario.Outcome().Ok);
    }

    [Fact]
    public void Without_a_bind_in_the_run_the_respawn_is_classified_against_any_lifestone_near_the_arrival_as_before()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        rig.RespawnCell = FarCell;
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.False(scenario.Bound);
        Assert.Contains(" respawn=lifestone ", scenario.VerdictFields(), StringComparison.Ordinal); // the rule before the bound-lifestone classification, kept for a run without the bind stage
    }

    [Fact]
    public void A_death_with_no_teleport_in_sixty_seconds_is_respawn_none()
    {
        var rig = new Rig(buffed: false);
        rig.Tuskers = false; // the victim notice below is the only sign of a death: no teleport follows it
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);

        var status = ScenarioStatus.Running;
        var sent = false;
        for (var i = 0; i < 2000 && status == ScenarioStatus.Running; i++)
        {
            status = rig.Frame(scenario);
            if (!sent && scenario.Stage == "death-wait")
            {
                sent = true;
                rig.R.Server((p, s) => AceM3Writers.VictimNotification(p, s, "You have been killed by Tusker Slave."));
            }
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("respawn=none: no death teleport and arrival within 60 s", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wait_among_the_tuskers_allocates_nothing_per_frame()
    {
        var rig = new Rig(buffed: false);
        rig.Tuskers = false;
        var walk = new FakeWalk();
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death, new DeathOptions { Seconds = 1000 });
        for (var i = 0; i < 400 && (scenario.Stage != "death-wait" || walk.Legs == 0); i++)
        {
            rig.Frame(scenario);
        }

        for (var i = 0; i < 40; i++)
        {
            rig.Frame(scenario); // the leg ends and the first health share is logged
        }

        Assert.Equal("death-wait", scenario.Stage);
        scenario.Step(rig.View, rig.Now);
        long allocated = 0;
        for (var i = 0; i < 300; i++)
        {
            rig.R.H.R.Time.Advance(TimeSpan.FromSeconds(0.01));
            var before = GC.GetAllocatedBytesForCurrentThread();
            scenario.Step(rig.View, rig.Now);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_death_stage_with_no_walker_fails_loudly_naming_the_todo()
    {
        var rig = new Rig(buffed: false);
        var scenario = rig.Scenario(null, DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Contains("TODO(m3-fight-no-walker)", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_walk_that_fails_fails_the_run_loudly()
    {
        var rig = new Rig(buffed: false);
        var scenario = rig.Scenario(new FailingWalk(), DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Contains("the walk failed: the walker left the den's floor", scenario.Failure, StringComparison.Ordinal);
    }

    private sealed class FailingWalk : IFightWalk
    {
        private bool _prepared;

        public string? Failure { get; private set; }

        public void Prepare(IGameSessionView session, double now) => _prepared = true;

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = "leg";
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            if (!_prepared)
            {
                return FightWalkStatus.Idle;
            }

            Failure = "the walker left the den's floor";
            return FightWalkStatus.Failed;
        }

        public void Abandon()
        {
        }

        public void Dispose()
        {
        }
    }

    // --- the gear and the regear, against the offline stand-in ---

    private static GearItem Armour(GearWeenie w) =>
        new(w.Wcid, w.Name, GearKind.Armour, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);

    private static GearKit SmallKit() =>
        GearKit.Of([new GearItem(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand.WeenieType, GearKit.Wand.ItemType, GearKit.Wand.ValidLocations, 1, null, GearKit.Wand.Burden), .. GearKit.Armour.Select(Armour)]);

    [Fact]
    public void The_gear_stage_geared_the_mage_and_the_regear_runs_the_gear_scenario_again_and_reads_gear_restored_yes()
    {
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 };
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
                StandInSessions.AnswerWorld(h, visit);
            },
            time,
            log);
        for (var t = 0.0; t < 2.0; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        var scenario = new DeathScenario(SmallKit(), new Dictionary<uint, SpellInfo>(), new DeathOptions(), null, log, endSession: false, stages: DeathStages.Gear | DeathStages.Regear);
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 400 && status == ScenarioStatus.Running; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
            status = scenario.Step(session, session.Clock.Now);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.StartsWith("gear=wielded worn=5", scenario.Gear!.VerdictFields(), StringComparison.Ordinal);
        Assert.NotNull(scenario.Regear);
        Assert.True(scenario.GearRestored);
        Assert.StartsWith("gear=wielded worn=5", scenario.Regear!.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("gear_steps=0", scenario.Regear.VerdictFields(), StringComparison.Ordinal); // the second run found everything wielded and sent nothing
        Assert.Equal("ok", scenario.BarsNote); // the stand-in's book holds no spell (TODO(m3-standin-spellbook-updates)): nothing to put on a bar
        Assert.Contains(" gear_restored=yes ", scenario.VerdictFields() + " ", StringComparison.Ordinal);
    }

    // --- the drop of a death, then the regear ---

    private static ScenarioStatus RunScenario(CastRig cast, IScenario scenario, double limitSeconds)
    {
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < limitSeconds && status == ScenarioStatus.Running; t += 0.1)
        {
            cast.Run(0.1);
            status = scenario.Step(cast.Session, cast.Session.Clock.Now);
        }

        return status;
    }

    [Fact]
    public void A_death_that_drops_wielded_items_leaves_the_inventory_settled_and_the_regear_tops_up_and_wields_again()
    {
        // A level 126 mage drops level / 20 plus 0 to 2 items, the wielded ones included (Player_Death.cs 347-540, 618-651). A wielded item's drop is ACE's TryDequipObjectWithNetworking(ToCorpseOnDeath): 02DA, 02CE, the sound
        // and F747 and no 0024 (Player_Inventory.cs 397-441); the model must let go of it with the F747, or the re-gear counts a held item with no world object ("inventory-unsettled", the third live run).
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Stats = new StandInPlayerStats { Health = 5, Level = 126 } };
        using var cast = new CastRig(visit);
        var server = StandInSessions.Server(cast.Session.Reducer);
        cast.Begin();
        var kit = SmallKit();

        var gear = new GearScenario(kit, cast.Log, endSession: false, cast: false);
        Assert.Equal(ScenarioStatus.Succeeded, RunScenario(cast, gear, 400));
        Assert.StartsWith("gear=wielded worn=5", gear.VerdictFields(), StringComparison.Ordinal);
        var inventory = cast.Session.State.Self.Inventory;
        var equippedBefore = inventory.Equipped.Count;
        Assert.Equal(6, equippedBefore);

        Assert.NotNull(server.PlaceNear(StandInLifestone.Make(0x80000C01), new System.Numerics.Vector3(1f, 0f, 0f)));
        cast.Run(0.5);
        Assert.Equal(ActionOutcomeKind.Confirmed, Use(cast, 0x80000C01));
        cast.Run(0.5);
        cast.Begin();
        var suicide = cast.Session.Actions.Suicide(ActionSource.Host); // M4 4.1 t2: the stand-in holds no monster, so a suicide is the death
        cast.Run(40.0);
        Assert.True(suicide.IsCompleted, suicide.Summary);
        cast.Run(8.0); // the arrival
        Assert.Equal(SelfTravel.TeleportArrived, cast.Session.State.Self.Travel);
        // The stand-in's teleport does not model Hidden; ACE's arrival ends with a state without it (as the scripted rig does). The host's portal space would also send LoginComplete.
        InboundDispatcher.Dispatch(new InboundContext(0, MessageQueue.SmartBox, 0), AceWireWriters.SetState(cast.Session.State.Self.Guid, (uint)PhysicsState.Gravity, 1, 900), cast.Session.Reducer);
        cast.Run(1.0);
        Assert.Equal(SelfTravel.Present, cast.Session.State.Self.Travel);

        Assert.Equal(1, cast.World.PlayerDeaths);
        Assert.True(cast.World.LastDrops.Count >= 5, "drops " + cast.World.LastDrops.Count);
        Assert.True(inventory.Equipped.Count < equippedBefore - 3, "equipped " + inventory.Equipped.Count); // the wielded drops left the model with their F747
        GearInventory.CountAll(cast.Session.State, out var unsettled);
        Assert.Equal(0, unsettled);

        // The re-gear: the settle stage finds nothing unsettled, the gear scenario tops up what the corpse took and wields again.
        var scenario = new DeathScenario(kit, new Dictionary<uint, SpellInfo>(), new DeathOptions(), null, cast.Log, endSession: false, stages: DeathStages.Regear);
        var regearStatus = RunScenario(cast, scenario, 400);
        Assert.True(regearStatus == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(scenario.GearRestored);
        Assert.StartsWith("gear=wielded worn=5", scenario.Regear!.VerdictFields(), StringComparison.Ordinal);
        Assert.True(scenario.Regear.GearSteps > 0, "the re-gear should have spawned and wielded what the death dropped");
    }

    private static ActionOutcomeKind Use(CastRig cast, uint guid)
    {
        var use = cast.Session.Actions.Use(guid, ActionSource.Host);
        cast.Run(5.0);
        return use.Outcome?.Kind ?? ActionOutcomeKind.TimedOut;
    }

    [Fact]
    public void A_held_item_with_no_world_object_is_named_and_the_regear_fails_naming_it_instead_of_waiting_silently()
    {
        var rig = new Rig(buffed: false);
        // The pack lists an item whose object never existed (what a missed delete left behind).
        var h = rig.R.H.R;
        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(WorldActionRig.Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [new ContentDesc(0x80000AAAu, 0)], Equipped = [new EquippedDesc(0x80000BBBu, 0x01000000u, 0)] });
        var scenario = rig.Scenario(null, DeathStages.Regear);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Contains("regear: 2 held item(s) still have no world object after 5 s", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("guid 0x80000AAA", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("guid 0x80000BBB", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("Equipped", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(Lines(rig), l => l.StartsWith("regear: 2 held item(s) have no world object:", StringComparison.Ordinal));
    }

    [Fact]
    public void ObjectDelete_of_a_wielded_item_releases_it_from_the_model_as_ACEs_drop_to_the_corpse_sends_no_0024()
    {
        var r = new WorldActionRig();
        r.Describe(equipped: [new EquippedDesc(WorldActionRig.Staff, WorldActionRig.Wand, 0)]);
        r.Item(WorldActionRig.Staff, WorldActionRig.Pack1); // its CreateObject: the world object ACE's F747 deletes
        Assert.True(r.H.State.Self.Inventory.Contains(WorldActionRig.Staff));

        // TryDequipObjectWithNetworking(ToCorpseOnDeath): 02DA Wielder 0, 02CE CurrentWieldedLocation 0, F74A pickup, the sound, then F747.
        r.Raw(AceWireWriters.ObjectDelete(WorldActionRig.Staff, 1));

        Assert.False(r.H.State.Self.Inventory.Contains(WorldActionRig.Staff));
        Assert.Empty(r.H.State.Self.Inventory.Equipped);
    }

    private static int CountBuffs(Rig rig) => rig.R.H.State.Self.Qualities.ActiveEnchantments.Count(e => e.SpellId != 666);
}

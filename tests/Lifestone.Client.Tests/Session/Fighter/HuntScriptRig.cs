// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Melee.cs (HandleActionTargetedMeleeAttack and its approach, Attack, OnAttackDone), Player_Missile.cs (LaunchMissile, TargetInRange), Healer.cs (HandleActionUseOnTarget, DoHealing, GetHealAmount),
// Food.cs (the potion's chain), Container.cs (Open, Close), Entity/SkillCheck.cs (GetSkillChance), and, for the scripted vendors (HuntScriptRig.Shops.cs), Vendor.cs (ApproachVendor, BuyItems_ValidateTransaction, GetSellCost, GetBuyCost, ProcessItemsForPurchase),
// Player_Commerce.cs (HandleActionBuyItem, HandleActionSellItem, VerifySellItems, SpendCurrency) and Player_Examine.cs (HandleActionIdentifyObject) (AGPL-3.0) [with changes: test code only; the order of the messages is the live captures' (cache/m4/1-2/capture.md C1 to C3, cache/m4/2-1/capture.md C2,
// cache/m4/3-1/capture.md C1 to C4, cache/m4/3-2/capture.md C1 to C3), the swing and shot lengths, the blow's damage and the heal band are placeholders; it is the successor of the retired stand-in creature's behaviour, kept for the hunt's unit tests alone].
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.State;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.World.Data;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 4.1 fix2a (D23, D26): the hunt loop's offline proof over a SCRIPT. The owner retired the stand-in's creature, so the rows that fought, killed and looted one were deleted with it; this rig brings them back as
/// scripted unit tests that DRAW NOTHING: it lives in the tests, nothing in <c>src/</c> can reach it, no play-check or frame uses it, and it places nothing in any stand-in world.
/// </summary>
/// <remarks>
/// <para>It is the real headless runner and the real <see cref="GameSession"/> (reducer, trackers, Action API) over <see cref="ScriptedAceSession"/> and <see cref="ScriptedFighter"/> (the login, the pack, the wields,
/// the item and place verbs), plus a model of the parts of ACE the hunt meets, answered in the orders the live captures record (cache/m4/1-2/capture.md C1 and C2, cache/m4/3-1/capture.md C1 to C4, docs/session.md
/// sections 35 to 37) and ACE's source (Player_Melee.cs, Healer.cs, Food.cs, Container.cs): an attack and its approach, the blows, the target's health pushes, the kill and its corpse, the corpse's contents and their pick-up,
/// the heal (a kit by <c>UseWithTarget</c>, a potion by <c>Use</c>), the vitals and the teleports. Every interval is session-clock seconds. The numbers that ACE rolls (a blow's damage, the heal's amount) are placeholders
/// from a seeded generator: a test that needs a number sets it.</para>
/// <para>Messages go to the reducer as typed messages (the decoder is proved elsewhere); the clock is the test's own.</para>
/// </remarks>
internal sealed partial class HuntScriptRig : IDisposable
{
    /// <summary>The hunter's guid on the scripted server.</summary>
    public const uint FighterGuid = 0x50000030;

    /// <summary>The first guid of a corpse's contents.</summary>
    public const uint FirstContentGuid = 0x80060000;

    /// <summary>The corpse weenie class id and item type (the corpse is a container to the client).</summary>
    public const uint CorpseWcid = 21, CorpseItemType = 0x200;

    /// <summary>The Bowl and the Smock a scripted corpse holds (a Misc value 10 and a Clothing value 25).</summary>
    public const uint BowlWcid = 141, SmockWcid = 2589;

    private const uint Health = 2, Stamina = 4;
    private const double Step = 0.05;
    private const double SwingSeconds = 1.9, BlowSeconds = 0.6, CorpseSeconds = 0.54, ApproachSpeed = 4.0, ApproachStepSeconds = 0.2, StickyMetres = 4.0, StopMetres = 0.6, FailMetres = 15.0;
    private const double ContentsLag = 0.016, PickupSeconds = 1.35;
    private const double HealMotionSeconds = 1.47, HealReadyLag = 0.03, DrinkEffectSeconds = 0.73, DrinkDoneSeconds = 1.47;
    private const uint CommandSlashMed = 0x1000005C, SkillHealSelf = 0x1000010E, DrinkMotion = 0x4000001B;
    private const int PlaceholderHealingSkill = 190, KitBoost = 50, PotionBoost = 25;
    private const uint YoureTooBusy = 0x001D, ActionCancelled = 0x0036, YouChargedTooFar = 0x003D, NotTrainedInHealing = 0x04FC, IsAtFullHealth = 0x04FF;

    private readonly List<(double Due, int Order, Action Run)> _schedule = [];
    private readonly Dictionary<(uint, uint), byte> _sequences = [];
    private readonly Dictionary<uint, Creature> _creatures = [];
    private readonly Dictionary<uint, Corpse> _corpses = [];
    private readonly Dictionary<uint, int> _kitUses = [];
    private readonly Dictionary<uint, int> _potionStacks = [];
    private readonly HashSet<uint> _removed = [];
    private readonly Random _attackRandom = new(0x5EED);
    private readonly TeleportDestination _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
    private int _order;
    private int _actionsSeen;
    private int _belt;
    private ushort _teleportStamp = 100;
    private ushort _positionStamp = 200;
    private bool _teleportDue;
    private bool _vitalsSeeded;
    private double _busyUntil;
    private uint _contentGuid = FirstContentGuid;
    private uint _openCorpse;

    // the attack in progress
    private enum AttackKind
    {
        None,
        Melee,
        Missile,
    }

    private AttackKind _attack;
    private uint _attackTarget;
    private uint _attackHeight;
    private int _attackSerial;
    private bool _swinging;
    private bool _attackCancelled;
    private bool _approaching;
    private CellFrame? _approachFrom;

    private sealed class Creature(uint guid, string name, uint wcid, uint max)
    {
        public uint Guid { get; } = guid;

        public string Name { get; } = name;

        public uint Wcid { get; } = wcid;

        public uint Max { get; } = max;

        public uint Health { get; set; } = max;

        public bool Dead { get; set; }

        public uint Setup { get; init; } = 0x02000964;

        public float Fraction => Max == 0 ? 0f : (float)Health / Max;
    }

    private sealed class Corpse(uint guid, string name)
    {
        public uint Guid { get; } = guid;

        public string Name { get; } = name;

        public List<(uint Guid, uint Wcid, string Name, uint ItemType, uint Value, ushort Burden)> Items { get; } = [];
    }

    private HuntScriptRig(Func<RecordingLogSink, HuntScenario> make, string character, double quitAfter)
    {
        Table = ScriptedMage.SyntheticTable();
        Log = new RecordingLogSink();
        Scenario = make(Log);
        Runner = HeadlessRunner.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = quitAfter },
            Scenario,
            SessionTestData.SecretsV2(),
            SessionTestData.PidsReady(),
            () => SessionTestData.EndOfRetail,
            () => HeadlessTestWorld.Assets,
            _ => World,
            Time,
            Log,
            Recording,
            Sockets.Open,
            (_, sink, tap) =>
            {
                Server = new ScriptedAceSession(sink, tap, Time);
                Server.Characters.Insert(0, new AceCharacter(FighterGuid, character));
                Fighter = ScriptedFighter.Ready(Table, Server, character);
                Fighter.Attach();
                var fighterTool = Server.ToolHook;
                Server.ToolHook = request => Tool(request, fighterTool);
                Server.UseHook = Use;
                foreach (var kit in Fighter.Items.Where(i => i.Kind.Wcid == FighterKit.HealingKitWcid))
                {
                    _kitUses[kit.Guid] = 20;
                }

                foreach (var potion in Fighter.Items.Where(i => i.Kind.Wcid == FighterKit.PotionWcid))
                {
                    _potionStacks[potion.Guid] = potion.Stack;
                }

                return Server;
            });
    }

    /// <summary>A rig for the hunt <paramref name="make"/> builds over the rig's log.</summary>
    /// <param name="make">Builds the scenario (it is handed the log it must use).</param>
    /// <param name="character">The character's name (the hunt acts only for the Holtburg Hunter).</param>
    /// <param name="quitAfter">The runner's limit in session seconds.</param>
    public static HuntScriptRig Create(Func<RecordingLogSink, HuntScenario> make, string character = FighterTemplate.HunterName, double quitAfter = 4000) => new(make, character, quitAfter);

    public XpTableData Table { get; }

    public RecordingLogSink Log { get; }

    public HuntScenario Scenario { get; }

    public ManualTimeProvider Time { get; } = new();

    public MemoryStream Recording { get; } = new();

    public SocketFactorySpy Sockets { get; } = new();

    public HeadlessTestWorld World { get; } = new();

    public HeadlessRunner Runner { get; }

    public GameSession Session => Runner.Session;

    public ScriptedAceSession Server { get; private set; } = null!;

    public ScriptedFighter Fighter { get; private set; } = null!;

    /// <summary>Called on each arrival outside Holtburg's landblock with the arrival's number (1 for the first cell), to place what that cell holds.</summary>
    public Action<HuntScriptRig, int>? OnBelt { get; set; }

    /// <summary>The health the hunter starts with as a share of the maximum (the vitals are seeded once it is present); null: full.</summary>
    public uint? StartHealth { get; set; }

    /// <summary>True (the default): a lifestone stands 1 m east of the hunter in Holtburg once it is present, so the hunt's bind finds one.</summary>
    public bool PlaceLifestone { get; set; } = true;

    /// <summary>The generator the heal's rolls draw from (the roll first, then the amount and the critical).</summary>
    public Random HealRandom { get; set; } = new(0x4EA1);

    /// <summary>False: the hunter's Healing is below trained (a kit use ends UseDone 0x04FC, as ACE's does).</summary>
    public bool HealingTrained { get; set; } = true;

    /// <summary>The chance that the hunter's blow lands (1: always).</summary>
    public double HitRate { get; set; } = 1.0;

    /// <summary>The creatures an arrow cannot reach (a trunk in the way): every shot at one ends on the environment, with ACE's chat line, and never evades or hits (M4 P4 bow).</summary>
    public HashSet<uint> BlockedTargets { get; } = [];

    /// <summary>Whether shot number n (1-based, over the run) hits the environment, whatever the target (M4 P4 bow; null: none).</summary>
    public Func<int, bool>? HitsEnvironment { get; set; }

    /// <summary>The smallest and largest blow.</summary>
    public (uint Min, uint Max) Blow { get; set; } = (20, 40);

    /// <summary>The corpse a kill leaves: false withholds it (a kill whose corpse never comes).</summary>
    public bool CorpseAppears { get; set; } = true;

    /// <summary>True: a Use of a corpse is answered by a UseDone alone (a refused open).</summary>
    public bool OpenAnsweredByUseDoneOnly { get; set; }

    /// <summary>Seconds after the Use at which a corpse's 0196 and UseDone come.</summary>
    public double CorpseOpenDelay { get; set; }

    /// <summary>The Honest log of the arrivals outside Holtburg (the number of place-loc teleports).</summary>
    public int BeltArrivals => _belt;

    public RecordingLogSink Logger => Log;

    public uint OpenContainerGuid => _openCorpse;

    public int Swings { get; private set; }

    public int Kills { get; private set; }

    public List<uint> AttackDoneErrors { get; } = [];

    public IReadOnlyList<(uint Type, uint First, uint Second, uint Third)> Sends => Server.WorldActionSends;

    public double Now => Session.Clock.Now;

    public bool Logged(string text) => Log.Lines.Any(l => l.Contains(text, StringComparison.Ordinal));

    public int LineAt(string text, int from = 0)
    {
        var lines = Log.Lines;
        for (var i = from; i < lines.Count; i++)
        {
            if (lines[i].Contains(text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public int LineCount(string text) => Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));

    public string Tail(int count = 30) => Environment.NewLine + string.Join(Environment.NewLine, Log.Lines.TakeLast(count));

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- running

    /// <summary>Runs the session, the scenario and the script for <paramref name="seconds"/> or until the run ends; the scenario's status.</summary>
    public ScenarioStatus Run(double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9 && !Runner.IsDone; t += Step)
        {
            StepOnce();
        }

        return Status;
    }

    /// <summary>Runs until <paramref name="reached"/> holds, the run ends or <paramref name="limitSeconds"/> pass; true when it held.</summary>
    public bool RunUntil(Func<bool> reached, double limitSeconds = 900)
    {
        for (var t = 0.0; t < limitSeconds && !reached() && !Runner.IsDone; t += Step)
        {
            StepOnce();
        }

        return reached();
    }

    public ScenarioStatus Status => !Runner.IsDone ? ScenarioStatus.Running : Runner.Result.Ok ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;

    private void StepOnce()
    {
        Time.Advance(TimeSpan.FromSeconds(Step));
        Runner.Step(Session.Clock.Now);
        if (Server.PlayerGuid is not null)
        {
            Fighter.Pump();
            Script();
        }
    }

    /// <summary>One step with <paramref name="afterTick"/> between the session's tick and the scenario's step (a test acts on what the tick delivered before the scenario reads it); the scenario's status.</summary>
    public ScenarioStatus RunHooked(Action afterTick)
    {
        Time.Advance(TimeSpan.FromSeconds(Step));
        Session.Tick(Session.Clock.Now);
        afterTick();
        var status = Scenario.Step(Session, Session.Clock.Now);
        Fighter.Pump();
        Script();
        return status;
    }

    /// <summary>The session ticked <paramref name="seconds"/> with the script but the scenario NOT stepped (the leg holds the scenario for a review frame).</summary>
    public void RunHeld(double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += Step)
        {
            Time.Advance(TimeSpan.FromSeconds(Step));
            Session.Tick(Session.Clock.Now);
            Fighter.Pump();
            Script();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- state reads

    public bool Held(uint wcid, out bool equipped)
    {
        equipped = false;
        foreach (var o in Session.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && Session.State.Self.Inventory.Find(o.Guid).Held)
            {
                equipped = Session.State.Self.Inventory.Find(o.Guid).Kind == InventoryPlaceKind.Equipped;
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<uint> Guids(uint wcid) =>
        [.. Session.State.Objects.All.Where(o => o.Weenie.WeenieClassId == wcid && Session.State.Self.Inventory.Find(o.Guid).Held).Select(o => o.Guid)];

    public bool Equipped(uint guid) => Session.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped;

    public int LastSend(uint type, uint guid)
    {
        for (var i = Sends.Count - 1; i >= 0; i--)
        {
            if (Sends[i].Type == type && Sends[i].First == guid)
            {
                return i;
            }
        }

        return -1;
    }

    public (uint Current, uint Max) HealthNow()
    {
        Session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var current, out var max);
        return (current, max);
    }

    /// <summary>The hunter's health becomes <paramref name="value"/> <paramref name="after"/> seconds from now (a PrivateUpdateAttribute2ndLevel).</summary>
    public void SetPlayerHealthLater(double after, uint value) => At(after, () => Vital(Health, value));

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- the world

    /// <summary>Places a creature <paramref name="metres"/> north of the hunter (AC +Y), as a CreateObject.</summary>
    public void PlaceCreature(uint guid, string name, double metres, uint health = 40, uint wcid = 7990, bool refuses = false, double eastMetres = 0)
    {
        var here = Session.State.Self.Position ?? throw new InvalidOperationException("the hunter has no position yet");
        var cell = here.CellId;
        var local = here.Frame.Origin + new Vector3((float)eastMetres, (float)metres, 0f);
        if (AcCoordinates.IsOutdoorCell(cell))
        {
            AcCoordinates.AdjustToOutside(ref cell, ref local);
        }

        var creature = new Creature(guid, name, wcid, health);
        if (!refuses)
        {
            _creatures[guid] = creature; // a creature to the client that the script does not hold: ACE refuses an attack on it (Player_Melee.cs 105-134)
        }

        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position,
            State = PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
            Position = new AcPosition(cell, local, here.Frame.Orientation),
            SetupId = creature.Setup,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = name,
            WeenieClassId = wcid,
            IconId = 0x06000001,
            ItemType = 0x10,
            ObjectDescriptionFlags = 0x10u,
        };
        Sink.OnCreateObject(Ctx, new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isUpdate: false);
    }

    /// <summary>Gives the next corpse these items (name, wcid) instead of the default Bowl and Smock.</summary>
    public List<(string Name, uint Wcid, uint ItemType, uint Value)>? CorpseLoot { get; set; }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- messages

    private GameStateReducer Sink => Session.Reducer;

    private InboundContext Ctx => new(0, MessageQueue.SmartBox, Session.Reducer.Clock.Time.GetTimestamp());

    private uint Self => Session.State.Self.Guid;

    private GameEventHeader Header(uint type) => new(Self, 0, type);

    private void At(double after, Action run) => _schedule.Add((Now + after, _order++, run));

    private void AtSerial(double after, int serial, Action run) => At(after, () =>
    {
        if (serial == _attackSerial)
        {
            run();
        }
    });

    private byte Next(uint kind, uint key)
    {
        _sequences.TryGetValue((kind, key), out var last);
        var next = unchecked((byte)(last + 1));
        _sequences[(kind, key)] = next;
        return next;
    }

    private void Private(PrivateUpdateKind kind, uint key, long value) =>
        Sink.OnPrivateUpdate(Ctx, new PrivateUpdateMsg(kind, Next((uint)kind, key), key, value, default, default, default));

    private void Vital(uint vital, uint value) => Private(PrivateUpdateKind.VitalCurrent, vital, value);

    private int VitalDelta(uint vital, int delta)
    {
        if (!Session.State.Self.Qualities.TryGetVitalLevels(vital - 1, out var current, out var max))
        {
            return 0;
        }

        var next = (uint)Math.Clamp((int)current + delta, 0, (int)max);
        if (next != current)
        {
            Vital(vital, next);
        }

        return (int)next - (int)current;
    }

    private void UseDone(uint error = 0) => Sink.OnUseDone(Ctx, Header(0x01C7), new UseDoneEvent(error));

    private void WeenieError(uint code, string? text = null) => Sink.OnWeenieError(Ctx, Header(text is null ? 0x028Au : 0x028B), new WeenieErrorEvent(code, text));

    private void Line(string text) => Sink.OnServerMessage(Ctx, new ServerMessageMsg(text, 4));

    private void AttackDone(uint error)
    {
        AttackDoneErrors.Add(error);
        Sink.OnAttackDone(Ctx, Header(0x01A7), new AttackDoneEvent(error));
    }

    private void PlayerMotion(ServerMotionState state)
    {
        var stamps = Session.State.Self.Stamps;
        Sink.OnUpdateMotion(Ctx, new UpdateMotionMsg(Self, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    private void StanceMotion(uint stance, uint? forward, uint? command = null)
    {
        var wire = MotionCommandTable.ToWire(stance);
        PlayerMotion(new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Forward = forward is null || forward == MotionCommands.Ready ? null : MotionCommandTable.ToWire(forward.Value),
            Commands = command is { } c ? [new MotionItem(MotionCommandTable.ToWire(c), (ushort)(++_motionStamp & 0x7FFF), false, 1f)] : default,
        });
    }

    private int _motionStamp;

    private uint StanceNow() => CombatStances.ForMode(Session.State.Self.Qualities.CombatMode, Session.State, out _);

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- the script

    private void Script()
    {
        if (!_vitalsSeeded && Session.State.Self.Travel == SelfTravel.Present && Session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max) && max != 0)
        {
            _vitalsSeeded = true;
            Vital(Health, StartHealth ?? max);
            Session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var stamina);
            Vital(Stamina, stamina);
            if (Shops)
            {
                SeedShops();
            }

            if (PlaceLifestone && Session.State.Self.Position is { } at)
            {
                var cell = at.CellId;
                var local = at.Frame.Origin + new Vector3(1f, 0f, 0f);
                if (AcCoordinates.IsOutdoorCell(cell))
                {
                    AcCoordinates.AdjustToOutside(ref cell, ref local);
                }

                Sink.OnCreateObject(Ctx, new CreateObjectMsg(0x80000C01, ModelData.Empty, new PhysicsDesc
                {
                    Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position,
                    State = PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
                    Position = new AcPosition(cell, local, at.Frame.Orientation),
                    SetupId = 0x02000001,
                    Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
                }, new WeenieDesc { Header = WeenieHeaderFlags.None, Name = "Lifestone (scripted)", WeenieClassId = 2, IconId = 0x06000001, ItemType = 0x80, ObjectDescriptionFlags = WorldObjectTable.LifestoneFlag }), isUpdate: false);
            }
        }

        if (_teleportDue)
        {
            _teleportDue = false;
            var at = _where;
            var known = Session.State.Self.Stamps;
            _positionStamp = (ushort)Math.Max(_positionStamp, known.Position); // the approach's own positions moved the client's sequence on
            _teleportStamp = (ushort)Math.Max(_teleportStamp, known.Teleport);
            Server.Inject(AceWireWriters.PlayerTeleport(++_teleportStamp));
            Server.Inject(AceWireWriters.UpdatePosition(Server.PlayerGuid!.Value, at, true, new AceUpdatePositionSequences(1, ++_positionStamp, _teleportStamp, 1)));
            Server.Inject(AceWireWriters.SetState(Server.PlayerGuid!.Value, (uint)(PhysicsState.Gravity | PhysicsState.EdgeSlide), 1, (ushort)(40 + _teleportStamp)));
            _awaitingArrival = true;
        }

        if (_awaitingArrival && Session.State.Self.Travel == SelfTravel.Present && Session.State.Self.Position is { } arrived && arrived.CellId == _where.Cell)
        {
            _awaitingArrival = false;
            if ((arrived.CellId >> 16) != 0xA9B4)
            {
                OnBelt?.Invoke(this, ++_belt);
            }
        }

        var sends = Server.WorldActionSends;
        while (_actionsSeen < sends.Count)
        {
            var (type, first, second, third) = sends[_actionsSeen++];
            Action(type, first, second, third);
        }

        RunDue();
    }

    private bool _awaitingArrival;
    private AcePosition _where;

    private void RunDue()
    {
        while (true)
        {
            var index = -1;
            for (var i = 0; i < _schedule.Count; i++)
            {
                if (_schedule[i].Due <= Now + 1e-9 && (index < 0 || _schedule[i].Due < _schedule[index].Due || (_schedule[i].Due == _schedule[index].Due && _schedule[i].Order < _schedule[index].Order)))
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                return;
            }

            var run = _schedule[index].Run;
            _schedule.RemoveAt(index);
            run();
        }
    }

    private string? Tool(ToolRequest request, Func<ToolRequest, string?>? fighter)
    {
        switch (request)
        {
            case PlaceLocRequest loc:
                return Placed(request.Id, "loc", null, new AcePosition(loc.At.Cell, loc.At.X, loc.At.Y, loc.At.Z, loc.At.Qw, loc.At.Qx, loc.At.Qy, loc.At.Qz));
            case PlacePoiRequest poi when poi.Name.Contains("Holtburg", StringComparison.OrdinalIgnoreCase):
                var p = _holtburg.Location.Position;
                return Placed(request.Id, "poi", poi.Name, new AcePosition(p.CellId, p.Local.X, p.Local.Y, p.Local.Z));
            default:
                return fighter?.Invoke(request);
        }
    }

    private string Placed(string? id, string kind, string? name, AcePosition at)
    {
        var data = new PlaceData(kind, name, ToolsWire.FormatCell(at.Cell), at.X, at.Y, at.Z, at.Qw, at.Qx, at.Qy, at.Qz);
        _where = at;
        _teleportDue = true;
        Fighter.InHoltburg = (at.Cell >> 16) == 0xA9B4;
        return ToolsWire.Reply(ToolReply.Success(id, kind == "loc" ? ToolVerbs.PlaceLoc : ToolVerbs.PlacePoi, data), data.Describe());
    }

    private void Action(uint type, uint first, uint second, uint third)
    {
        switch (type)
        {
            case GameActionTypes.ChangeCombatMode:
                At(0.05, () => Private(PrivateUpdateKind.PropertyInt, CombatModes.Property, first));
                break;
            case GameActionTypes.TargetedMeleeAttack:
                At(0.05, () => BeginAttack(AttackKind.Melee, first, second));
                break;
            case GameActionTypes.TargetedMissileAttack:
                At(0.05, () => BeginAttack(AttackKind.Missile, first, second));
                break;
            case GameActionTypes.StackableMerge:
                At(0.05, () => Merge(first, second, third));
                break;
            case GameActionTypes.CancelAttack:
                At(0.05, CancelAttack);
                break;
            case GameActionTypes.UseWithTarget:
                UseKit(first, second);
                break;
            case GameActionTypes.BuyItems:
            case GameActionTypes.SellItems:
                Trade(type == GameActionTypes.BuyItems, first);
                break;
            case GameActionTypes.IdentifyObject:
                At(IdentifyLagSeconds, () => Identify(first));
                break;
            case GameActionTypes.NoLongerViewingContents:
                At(0.05, () => StopViewing(first));
                break;
            case GameActionTypes.PutItemInContainer when _corpses.Values.Any(c => c.Items.Any(i => i.Guid == first)):
                At(0.05, () => PickUp(first, second, third));
                break;
            default:
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- an attack (Player_Melee.cs; capture C1)

    private bool Interrupted => !TargetLives(_attackTarget);

    private bool TargetLives(uint guid) => _creatures.TryGetValue(guid, out var c) && !c.Dead && Session.State.Objects.Contains(guid);

    private bool TryReach(uint guid, out double metres)
    {
        metres = 0;
        if (Session.State.Self.Position is not { } here || !Session.State.Objects.TryGet(guid, out var body) || body.Position is not { } there)
        {
            return false;
        }

        metres = here.ToGlobal().DistanceTo(there.ToGlobal());
        return true;
    }

    private bool TryAmmo(out uint guid, out int stack)
    {
        guid = Fighter.At(EquipMask.MissileAmmo).Select(i => i.Guid).FirstOrDefault();
        stack = guid != 0 && _ammoLeft.TryGetValue(guid, out var left) ? left : guid != 0 && Session.State.Objects.TryGet(guid, out var o) ? o.Weenie.StackSize ?? 1 : 0;
        return guid != 0 && stack > 0 && Fighter.At(EquipMask.MissileWeapon).Count > 0;
    }

    private readonly Dictionary<uint, int> _ammoLeft = [];

    private const double LauncherRange = 40.8, ArrowSpeed = 60.0, MissileAimSeconds = 1.1, MissileShotSeconds = 1.17;
    private const uint MissileOutOfRange = 0x0550, AimLevelMotion = 0x4000001E, BowCombat = 0x8000003F;

    private void BeginAttack(AttackKind kind, uint target, uint height)
    {
        if (Session.State.Self.Qualities.CombatMode != (kind == AttackKind.Melee ? CombatModes.Melee : CombatModes.Missile) || !TargetLives(target) || (kind == AttackKind.Missile && !TryAmmo(out _, out _)))
        {
            AttackDone(ActionCancelled); // Player_Melee.cs 52-66, 105-134: a wrong mode, a missing, dead or non-attackable target: OnAttackDone only
            return;
        }

        if (_attack != AttackKind.None)
        {
            return;
        }

        _attack = kind;
        _attackTarget = target;
        _attackHeight = height;
        _attackCancelled = false;
        if (kind == AttackKind.Melee)
        {
            Approach(++_attackSerial, first: true);
        }
        else
        {
            Aim(++_attackSerial, subsequent: false);
        }
    }

    /// <summary>A shot (Player_Missile.cs; capture C3): the checks (a range past the launcher's is told 028A 0x0550 then 01A7), the aim, the launch 1.1 s later (the ammunition's stack falls: 0197), the hit after the flight, 01A7 {0} at 1.17 s and the next shot.</summary>
    private void Aim(int serial, bool subsequent)
    {
        if (Interrupted || Session.State.Self.Qualities.CombatMode != CombatModes.Missile || !TryAmmo(out _, out _))
        {
            Finish();
            return;
        }

        if (!TryReach(_attackTarget, out var distance) || distance > LauncherRange)
        {
            WeenieError(MissileOutOfRange);
            Finish();
            return;
        }

        if (subsequent)
        {
            Sink.OnCommenceAttack(Ctx, Header(0x01B8), new CommenceAttackEvent());
        }

        _swinging = true;
        var wire = MotionCommandTable.ToWire(BowCombat);
        PlayerMotion(new ServerMotionState { MovementType = MovementType.Interpreted, Stance = wire, Style = wire, Forward = MotionCommandTable.ToWire(AimLevelMotion) });
        AtSerial(MissileAimSeconds, serial, () => Launch(serial, distance));
    }

    private void Launch(int serial, double distance)
    {
        if (Interrupted || !TryAmmo(out var ammo, out var stack))
        {
            Finish();
            return;
        }

        Shots++;
        VitalDelta(Stamina, -2);
        _ammoLeft[ammo] = stack - 1;
        Sink.OnSetStackSize(Ctx, new SetStackSizeMsg(Next(0x40000 + ammo, 0), ammo, (uint)(stack - 1), (uint)(5 * (stack - 1))));
        AtSerial(Math.Max(0.0, distance - 0.8) / ArrowSpeed, serial, Land);
        AtSerial(MissileShotSeconds, serial, () => ShotOver(serial));
    }

    private void ShotOver(int serial)
    {
        _swinging = false;
        if (!TargetLives(_attackTarget) || _attackCancelled || !TryAmmo(out _, out _))
        {
            Finish();
            return;
        }

        AttackDone(0);
        AtSerial(0.5, serial, () => Aim(serial, subsequent: true));
    }

    public int Shots { get; private set; }

    /// <summary>The Uses of a corpse the script has received (the send itself, which waits for the post-teleport LoginComplete).</summary>
    public int CorpseOpensSent { get; private set; }

    /// <summary>A StackableMerge (0x0054): the amount moves from the first stack to the second; an emptied stack leaves the pack (0024).</summary>
    private void Merge(uint from, uint to, uint amount)
    {
        if (!Session.State.Objects.TryGet(from, out var a) || !Session.State.Objects.TryGet(to, out var b))
        {
            return;
        }

        var fromSize = _ammoLeft.TryGetValue(from, out var fl) ? fl : a.Weenie.StackSize ?? 1;
        var toSize = _ammoLeft.TryGetValue(to, out var tl) ? tl : b.Weenie.StackSize ?? 1;
        var moved = (int)Math.Min(amount, (uint)fromSize);
        _ammoLeft[to] = toSize + moved;
        _ammoLeft[from] = fromSize - moved;
        Sink.OnSetStackSize(Ctx, new SetStackSizeMsg(Next(0x40000 + to, 0), to, (uint)(toSize + moved), (uint)(5 * (toSize + moved))));
        if (fromSize - moved <= 0)
        {
            Sink.OnInventoryRemoveObject(Ctx, new InventoryRemoveObjectMsg(from));
        }
        else
        {
            Sink.OnSetStackSize(Ctx, new SetStackSizeMsg(Next(0x40000 + from, 0), from, (uint)(fromSize - moved), (uint)(5 * (fromSize - moved))));
        }
    }

    private void CancelAttack()
    {
        if (_attack == AttackKind.None)
        {
            return;
        }

        if (_swinging)
        {
            _attackCancelled = true;
        }
        else
        {
            Finish();
        }
    }

    private void Finish()
    {
        _attackSerial++;
        _attack = AttackKind.None;
        _attackTarget = 0;
        _swinging = false;
        _attackCancelled = false;
        _approaching = false;
        _approachFrom = null;
        AttackDone(ActionCancelled);
    }

    /// <summary>A target within the sticky distance is struck at once; a farther one is walked to (the player's MoveToObject, then its positions every 200 ms) and struck on arrival; past 15 m from where the walk began ACE cancels it with YouChargedTooFar (MoveToManager.cs 489).</summary>
    private void Approach(int serial, bool first)
    {
        if (Interrupted || !TryReach(_attackTarget, out var centre))
        {
            Finish();
            return;
        }

        var gap = Math.Max(0.0, centre - 0.8);
        // Without the script's own positions the client's physics ends the MoveTo where its body's radius and the target's put it, not at 0.6 m: the sticky distance is the arrival.
        if (gap <= (first || !ApproachSendsPositions ? StickyMetres : StopMetres))
        {
            _approaching = false;
            Swing(serial, subsequent: false);
            return;
        }

        if (Session.State.Self.Position is { } now)
        {
            if (first)
            {
                _approachFrom = now;
            }
            else if (_approachFrom is { } from && now.ToGlobal().DistanceTo(from.ToGlobal()) > FailMetres)
            {
                WeenieError(YouChargedTooFar);
                Finish();
                return;
            }
        }

        if (!_approaching)
        {
            _approaching = true;
            var goal = Session.State.Objects.TryGet(_attackTarget, out var body) && body.Position is { } at ? at : default;
            var wire = MotionCommandTable.ToWire(MotionCommands.HandCombat);
            PlayerMotion(new ServerMotionState
            {
                MovementType = MovementType.MoveToObject,
                Stance = wire,
                MoveTo = new MoveToData(_attackTarget, goal.CellId, goal.Frame.Origin, new MoveToParameters(0x0001EE0F, (float)StopMetres, 0f, float.MaxValue, 1f, 15f, 0f), 1f),
            });
        }

        if (ApproachSendsPositions)
        {
            StepPlayerToward(centre, Math.Min(ApproachSpeed * ApproachStepSeconds, gap - (StopMetres * 0.5)));
        }

        AtSerial(ApproachStepSeconds, serial, () => Approach(serial, first: false));
    }

    /// <summary>True (the default): the approach also walks the player with the script's own F748 positions every 200 ms (capture C1). False plays what ACE does live (round 3): the player's MoveToObject and no F748, the client's own physics carrying it out (<see cref="ClientPosition"/>).</summary>
    public bool ApproachSendsPositions { get; set; } = true;

    /// <summary>The client's autonomous position while the script does not walk the player: ACE takes it as the player's position (an F748 of the player). The headless run sends no movement, so a test's movement sink calls this.</summary>
    public void ClientPosition(in CellFrame position)
    {
        if (ApproachSendsPositions || !_approaching)
        {
            return;
        }

        var stamps = Session.State.Self.Stamps;
        Sink.OnUpdatePosition(Ctx, new UpdatePositionMsg(Self, PositionFlags.Grounded, new AcPosition(position.CellId, position.Frame.Origin, position.Frame.Orientation), null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 1)), stamps.Teleport, stamps.ForcePosition));
    }

    private void StepPlayerToward(double centre, double step)
    {
        var self = Session.State.Self;
        if (self.Position is not { } here || !Session.State.Objects.TryGet(_attackTarget, out var body) || body.Position is not { } there || centre <= 1e-6)
        {
            return;
        }

        var a = here.ToGlobal();
        var b = there.ToGlobal();
        var dx = (float)((b.X - a.X) / centre);
        var dy = (float)((b.Y - a.Y) / centre);
        var cell = here.CellId;
        var origin = here.Frame.Origin + new Vector3(dx * (float)step, dy * (float)step, 0f);
        if (AcCoordinates.IsOutdoorCell(cell))
        {
            AcCoordinates.AdjustToOutside(ref cell, ref origin);
        }

        var stamps = self.Stamps;
        Sink.OnUpdatePosition(Ctx, new UpdatePositionMsg(self.Guid, PositionFlags.Grounded, new AcPosition(cell, origin, here.Frame.Orientation), null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 1)), stamps.Teleport, stamps.ForcePosition));
    }

    private void Swing(int serial, bool subsequent)
    {
        if (Interrupted || Session.State.Self.Qualities.CombatMode != CombatModes.Melee)
        {
            Finish();
            return;
        }

        if (subsequent)
        {
            Sink.OnCommenceAttack(Ctx, Header(0x01B8), new CommenceAttackEvent());
        }

        _swinging = true;
        Swings++;
        VitalDelta(Stamina, -2);
        var wire = MotionCommandTable.ToWire(MotionCommands.HandCombat);
        PlayerMotion(new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            MotionFlags = MotionFlags.StickToObject,
            Stance = wire,
            Style = wire,
            Forward = MotionCommandTable.ToWire(CommandSlashMed),
            ForwardSpeed = 1f,
            StickyGuid = _attackTarget,
        });
        AtSerial(BlowSeconds, serial, Land);
        AtSerial(SwingSeconds, serial, () => SwingOver(serial));
    }

    private void SwingOver(int serial)
    {
        _swinging = false;
        var alive = TargetLives(_attackTarget);
        var near = alive && TryReach(_attackTarget, out var centre) && centre - 0.8 <= StickyMetres;
        if (!alive || !near || _attackCancelled)
        {
            Finish();
            return;
        }

        AttackDone(0);
        AtSerial(0.5, serial, () => Swing(serial, subsequent: true));
    }

    private void Land()
    {
        if (!_creatures.TryGetValue(_attackTarget, out var creature) || creature.Dead)
        {
            return;
        }

        if (_attack == AttackKind.Missile && (BlockedTargets.Contains(_attackTarget) || HitsEnvironment?.Invoke(Shots) == true))
        {
            Line("Your missile attack hit the environment.");
            return;
        }

        if (_attackRandom.NextDouble() >= HitRate)
        {
            Sink.OnEvasionAttackerNotification(Ctx, Header(0x01B3), new EvasionAttackerNotificationEvent(creature.Name));
            return;
        }

        var damage = (uint)_attackRandom.Next((int)Blow.Min, (int)Blow.Max + 1);
        var taken = Math.Min(damage, creature.Health);
        creature.Health -= taken;
        Sink.OnUpdateHealth(Ctx, Header(0x01C0), new UpdateHealthEvent(creature.Guid, creature.Fraction));
        if (creature.Health == 0)
        {
            Kill(creature);
            return;
        }

        Sink.OnAttackerNotification(Ctx, Header(0x01B1), new AttackerNotificationEvent(creature.Name, 0x1, (double)taken / creature.Max, taken, false, 0));
    }

    /// <summary>The kill in the live order (capture C1): 01AD, the experience, the Dead motion, then the corpse and the creature's removal 0.54 s later.</summary>
    private void Kill(Creature creature)
    {
        Kills++;
        creature.Dead = true;
        Sink.OnKillerNotification(Ctx, Header(0x01AD), new KillerNotificationEvent($"You slay {creature.Name}!"));
        var q = Session.State.Self.Qualities;
        Private(PrivateUpdateKind.PropertyInt64, PlayerQualities.TotalExperienceProperty, q.TotalExperience + 30);
        Private(PrivateUpdateKind.PropertyInt64, PlayerQualities.AvailableExperienceProperty, q.AvailableExperience + 30);
        var stamps = Session.State.Objects.TryGet(creature.Guid, out var body) ? body.Stamps : default;
        var wire = MotionCommandTable.ToWire(MotionCommands.NonCombat);
        Sink.OnUpdateMotion(Ctx, new UpdateMotionMsg(creature.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Forward = MotionCommandTable.ToWire(MotionCommands.Dead),
        }));
        var place = body?.Position is { } at ? new AcPosition(at.CellId, at.Frame.Origin, at.Frame.Orientation) : default;
        At(CorpseSeconds, () =>
        {
            if (CorpseAppears)
            {
                MakeCorpse(creature, place);
            }

            Sink.OnDeleteObject(Ctx, new DeleteObjectMsg(creature.Guid, 1));
        });
    }

    private uint _nextCorpse = 0x80000F01;

    private void MakeCorpse(Creature creature, in AcPosition place)
    {
        var guid = _nextCorpse++;
        var corpse = new Corpse(guid, "Corpse of " + creature.Name);
        if (CorpseLoot is { } loot)
        {
            foreach (var (name, wcid, type, value) in loot)
            {
                corpse.Items.Add((_contentGuid++, wcid, name, type, value, 15));
            }
        }
        else
        {
            corpse.Items.Add((_contentGuid++, BowlWcid, "Bowl", 0x80, 10, 15));
            corpse.Items.Add((_contentGuid++, SmockWcid, "Smock", 0x4, 25, 30));
        }

        _corpses[guid] = corpse;
        Sink.OnCreateObject(Ctx, new CreateObjectMsg(guid, ModelData.Empty, new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position,
            State = PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
            Position = place,
            SetupId = creature.Setup,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        }, new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = corpse.Name,
            WeenieClassId = CorpseWcid,
            IconId = 0x06000001,
            ItemType = CorpseItemType,
            ObjectDescriptionFlags = WorldObjectTable.CorpseFlag,
        }), isUpdate: false);
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- a corpse (capture C2)

    private bool Use(uint guid)
    {
        if (UseVendor(guid))
        {
            return true;
        }

        if (_corpses.TryGetValue(guid, out var corpse))
        {
            CorpseOpensSent++;
            At(0.05, () => UseCorpse(corpse));
            return true;
        }

        if (_potionStacks.ContainsKey(guid) && !_removed.Contains(guid))
        {
            At(0.05, () => Drink(guid));
            return true;
        }

        return false;
    }

    private void UseCorpse(Corpse corpse)
    {
        if (_openCorpse == corpse.Guid)
        {
            _openCorpse = 0;
            Sink.OnCloseGroundContainer(Ctx, Header(0x0052), new CloseGroundContainerEvent(corpse.Guid));
            UseDone();
            return;
        }

        if (OpenAnsweredByUseDoneOnly)
        {
            UseDone();
            return;
        }

        if (_openCorpse != 0)
        {
            Sink.OnCloseGroundContainer(Ctx, Header(0x0052), new CloseGroundContainerEvent(_openCorpse));
            _openCorpse = 0;
        }

        StanceMotion(StanceNow(), null);
        if (CorpseOpenDelay > 0)
        {
            At(CorpseOpenDelay, () => Reveal(corpse));
        }
        else
        {
            Reveal(corpse);
        }
    }

    private void Reveal(Corpse corpse)
    {
        _openCorpse = corpse.Guid;
        Sink.OnViewContents(Ctx, Header(0x0196), new ViewContentsEvent(corpse.Guid, [.. corpse.Items.Select(i => new ViewContentsEntry(i.Guid, 0))]));
        UseDone();
        var items = corpse.Items.ToArray();
        At(ContentsLag, () =>
        {
            if (_openCorpse != corpse.Guid)
            {
                return;
            }

            foreach (var item in items)
            {
                if (corpse.Items.Any(i => i.Guid == item.Guid))
                {
                    Sink.OnCreateObject(Ctx, ItemObject(item, corpse.Guid), isUpdate: false);
                }
            }
        });
    }

    private static CreateObjectMsg ItemObject((uint Guid, uint Wcid, string Name, uint ItemType, uint Value, ushort Burden) item, uint container) =>
        new(item.Guid, ModelData.Empty, new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup,
            State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal,
            SetupId = 0x02000B77,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        }, new WeenieDesc
        {
            Header = WeenieHeaderFlags.Value | WeenieHeaderFlags.Burden | WeenieHeaderFlags.Container,
            Name = item.Name,
            WeenieClassId = item.Wcid,
            IconId = 0x06001036,
            ItemType = item.ItemType,
            ObjectDescriptionFlags = 0,
            ContainerId = container,
            Value = item.Value,
            Burden = item.Burden,
        });

    private void StopViewing(uint container)
    {
        if (_openCorpse != container)
        {
            return;
        }

        _openCorpse = 0;
        Sink.OnCloseGroundContainer(Ctx, Header(0x0052), new CloseGroundContainerEvent(container));
    }

    /// <summary>A pick-up from a corpse: 1.35 s after the motion ACE puts the item in the pack (0022); the corpse no longer holds it.</summary>
    private void PickUp(uint item, uint container, uint placement)
    {
        if (_openCorpse == 0)
        {
            Sink.OnInventoryServerSaysFailed(Ctx, Header(0x00A0), new InventoryServerSaysFailedEvent(item, 0));
            return;
        }

        var from = _corpses.Values.FirstOrDefault(c => c.Items.Any(i => i.Guid == item));
        StanceMotion(StanceNow(), null);
        At(PickupSeconds, () =>
        {
            if (from is null || _openCorpse != from.Guid)
            {
                return; // the corpse was closed meanwhile: ACE gives the item up no more (the retreat's abandon; a test may land the late 0022 itself)
            }

            from.Items.RemoveAll(i => i.Guid == item);
            Sink.OnInventoryPutObjInContainer(Ctx, Header(0x0022), new InventoryPutObjInContainerEvent(item, container == 0 ? Self : container, placement, 0));
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- the heal (Healer.cs, Food.cs; capture 3-1 C1 to C4)

    private bool Busy => Now < _busyUntil - 1e-9;

    private bool InCombatMode => Session.State.Self.Qualities.CombatMode is not (0 or CombatModes.NonCombat);

    private static double HealChance(int effective, int difficulty) => 1.0 / (1.0 + Math.Exp(-0.03 * (effective - difficulty)));

    /// <summary>The kit's uses left (the rig's own count).</summary>
    public int KitUses(uint kit) => _kitUses.GetValueOrDefault(kit);

    /// <summary>The potions left in the stack.</summary>
    public int PotionsLeft(uint stack) => _removed.Contains(stack) ? 0 : _potionStacks.GetValueOrDefault(stack);

    private bool SkillSpecialized()
    {
        return Session.State.Self.Qualities.TryGetSkill(21, out var skill) && skill.AdvancementClass >= 3;
    }

    private void UseKit(uint kit, uint target)
    {
        if (!_kitUses.ContainsKey(kit) || _removed.Contains(kit) || target == 0)
        {
            At(0.05, () => UseDone());
            return;
        }

        if (!HealingTrained)
        {
            At(0.05, () => UseDone(NotTrainedInHealing));
            return;
        }

        if (Busy)
        {
            At(0.05, () => UseDone(YoureTooBusy));
            return;
        }

        if (target != Self)
        {
            At(0.05, () => UseDone(0x04FE));
            return;
        }

        var (health, max) = HealthNow();
        if (health >= max)
        {
            At(0.05, () =>
            {
                WeenieError(IsAtFullHealth, Session.State.Self.Name ?? "You");
                UseDone();
            });
            return;
        }

        var missing = max - health;
        _busyUntil = Now + 0.05 + HealMotionSeconds + HealReadyLag;
        At(0.05, () => StanceMotion(StanceNow(), MotionCommands.Ready, SkillHealSelf));
        At(0.05 + HealMotionSeconds, () => DoHealing(kit, missing));
        At(0.05 + HealMotionSeconds + HealReadyLag, () => StanceMotion(StanceNow(), MotionCommands.Ready));
    }

    private void DoHealing(uint kit, uint missing)
    {
        var uses = _kitUses[kit] - 1;
        _kitUses[kit] = uses;
        var name = "Handy Healing Kit";
        var remaining = uses > 0 ? $" Your {name} has {uses} {(uses == 1 ? "use" : "uses")} left." : $" Your {name} is used up.";
        var difficulty = (int)Math.Round(missing * 2 * (InCombatMode ? 1.1f : 1.0f));
        var effective = (int)Math.Round((PlaceholderHealingSkill + KitBoost) * (SkillSpecialized() ? 1.5f : 1.1f));
        if (!(HealChance(effective, difficulty) > HealRandom.NextDouble()))
        {
            Line("You fail to heal yourself." + remaining);
            Structure(kit, uses);
            ConsumeIfSpent(kit, uses);
            UseDone();
            return;
        }

        var low = PlaceholderHealingSkill * 0.2f;
        var high = PlaceholderHealingSkill * 0.5f;
        var amount = low + ((float)HealRandom.NextDouble() * (high - low));
        var critical = HealRandom.NextDouble() < 0.1;
        if (critical)
        {
            amount *= 2;
        }

        amount = Math.Min(amount, missing);
        Session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _);
        var cost = (uint)MathF.Round(amount / 5.0f);
        if (cost > stamina)
        {
            cost = stamina;
            amount = cost * 5;
        }

        var points = (uint)MathF.Round(amount);
        VitalDelta(Stamina, -(int)cost);
        VitalDelta(Health, (int)points);
        Line($"You {(critical ? "expertly " : string.Empty)}heal yourself for {points} Health points." + remaining);
        Structure(kit, uses);
        ConsumeIfSpent(kit, uses);
        UseDone();
    }

    private void Structure(uint kit, int uses) =>
        Sink.OnPublicUpdatePropertyInt(Ctx, new PublicUpdatePropertyIntMsg(Next(0x30000 + kit, 92), kit, 92, uses));

    private void ConsumeIfSpent(uint kit, int uses)
    {
        if (uses > 0)
        {
            return;
        }

        _removed.Add(kit);
        Sink.OnInventoryRemoveObject(Ctx, new InventoryRemoveObjectMsg(kit));
    }

    private void Drink(uint potion)
    {
        if (Busy)
        {
            WeenieError(YoureTooBusy);
            UseDone();
            return;
        }

        _busyUntil = Now + DrinkDoneSeconds;
        StanceMotion(StanceNow(), DrinkMotion);
        At(DrinkEffectSeconds, () =>
        {
            var change = VitalDelta(Health, PotionBoost);
            Line($"The Potion of Healing restores {Math.Max(change, 0)} points of your Health.");
            var left = _potionStacks[potion] - 1;
            _potionStacks[potion] = left;
            if (left > 0)
            {
                Sink.OnSetStackSize(Ctx, new SetStackSizeMsg(Next(0x40000 + potion, 0), potion, (uint)left, (uint)(170 * left)));
            }
            else
            {
                _removed.Add(potion);
                Sink.OnInventoryRemoveObject(Ctx, new InventoryRemoveObjectMsg(potion));
            }

            At(0.02, () => StanceMotion(StanceNow(), MotionCommands.Ready));
        });
        At(DrinkDoneSeconds, () => UseDone());
    }

    public void Dispose() => Runner.Dispose();
}

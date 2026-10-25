using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// A scripted ACE for the travel stations (M4 P5b J3b): the mage (<see cref="ScriptedCaster"/> on a <see cref="WorldActionRig"/>) or a fighter (<see cref="C1bSwapRig"/>: the weapon in hand, the caster in the pack) in the world beside a lifestone
/// (description flag 0x4000) and the den's portal (weenie 7242), answering as ACE does (research W0, WorldObject_Magic.cs at 95ef426d): Use of the lifestone says it attuned the spirit; Lifestone Tie 2644 at it says it linked; Primary Portal Tie 47 at
/// the portal likewise; Lifestone Recall 1635 and Primary Portal Recall 48 teleport 2 s after the cast (the player unhidden 1 s later) unless the character is not bound (0x049E) or not tied (0x04A3); Use of the portal teleports into the den.
/// The travel seam is <see cref="Travel"/>: it records every request and arrives at once on foot. <see cref="Places"/> counts the mod's <c>place</c> tool the action log saw: no station may make one.
/// </summary>
internal sealed class J3bRig
{
    public const uint LifestoneGuid = 0x7A9B404F;
    public const uint PortalGuid = 0x71B90000;
    public const uint HoltburgCell = 0xA9B40019;
    public const uint DenCell = 0x01050375;
    public const uint AcademyCell = 0x860201AD;

    private readonly C1bSwapRig? _swapRig;
    private readonly List<(double Due, Action What)> _later = [];
    private int _seen;
    private ushort _teleportStamp = 2;
    private ushort _positionStamp = 2;
    private ushort _stateStamp = 2;
    private int _tieFizzled;
    private bool _lifestonePending;

    /// <summary>A rig for the fighter template <paramref name="fighter"/>, or for the mage when it is null.</summary>
    public J3bRig(FighterTemplate? fighter = null, bool lifestoneInView = true)
    {
        _lifestonePending = !lifestoneInView;
        if (fighter is not null)
        {
            _swapRig = new C1bSwapRig(fighter);
            R = _swapRig.R;
            Caster = _swapRig.Caster;
            View = _swapRig.View;
            Character = fighter.Name;
            Account = fighter.Key == "melee" ? "test-melee" : "test-archer";
        }
        else
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            View = new MageView(R);
            Character = "BSD Mage";
            Account = "test";
        }

        foreach (var spell in new ushort[] { 1635, 2644, 47, 48 })
        {
            R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, spell));
        }

        if (!_lifestonePending)
        {
            Object(LifestoneGuid, "Lifestone", WorldObjectTable.LifestoneFlag, 1, 7242, 12);
        }

        Object(PortalGuid, "Black Spawn Den", 0, 7242, 7242, 15);
        _seen = R.H.Net.Sends.Count;
        Travel = new RecordingTravel(this);
    }

    private sealed class MageView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    public WorldActionRig R { get; }

    public ScriptedCaster Caster { get; }

    public IGameSessionView View { get; }

    public C1bSwapRig? SwapRig => _swapRig;

    public string Character { get; }

    public string Account { get; }

    public RecordingTravel Travel { get; }

    /// <summary>False: the character is bound to no lifestone (a Lifestone Recall answers 0x049E).</summary>
    public bool Bound { get; set; } = true;

    /// <summary>False: the character is tied to no portal (a Primary Portal Recall answers 0x04A3).</summary>
    public bool PortalTied { get; set; } = true;

    /// <summary>When set, a Primary Portal Recall answers this WeenieError instead.</summary>
    public uint PortalRecallRefusal { get; set; }

    /// <summary>
    /// True: a Lifestone Recall arrives inside a Holtburg lifestone, as ACE recalls to the tied lifestone's own location (WorldObject_Magic.cs 1087-1095): a
    /// lifestone object (<see cref="HoltburgLifestoneGuid"/>) is created at the arrival's position just before the teleport (M4 4.2 FS).
    /// </summary>
    public bool RecallIntoLifestone { get; set; }

    /// <summary>The Holtburg lifestone <see cref="RecallIntoLifestone"/> creates.</summary>
    public const uint HoltburgLifestoneGuid = 0x7A9B4050;

    /// <summary>True: Use of the lifestone says nothing.</summary>
    public bool UseSilent { get; set; }

    /// <summary>When set, Lifestone Tie answers this WeenieError (0x0400: no components) instead of linking.</summary>
    public uint TieRefusal { get; set; }

    /// <summary>The first this-many Lifestone Tie casts fizzle (0x0402).</summary>
    public int TieFizzles { get; set; }

    /// <summary>Everything the server was asked, in order: <c>use:&lt;guid&gt;</c>, <c>cast:&lt;spell&gt;</c> or <c>cast:&lt;spell&gt;@&lt;guid&gt;</c>, <c>mode:&lt;n&gt;</c>, <c>teleport:&lt;cell&gt;</c>.</summary>
    public List<string> Events { get; } = [];

    public double Now => R.H.R.Clock.Now;

    /// <summary>The mod's <c>place</c> tool sent so far (no station may send one).</summary>
    public int Places
    {
        get
        {
            var log = View.ActionLog;
            var n = 0;
            for (var i = 0; i < log.Count; i++)
            {
                if (log[i].Kind == ActionKind.ToolPlace)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>The journey's context over this rig (a real <see cref="JourneyFacts"/>, the recording travel).</summary>
    public JourneyContext Context(Action<JourneyFacts>? facts = null)
    {
        var f = new JourneyFacts();
        facts?.Invoke(f);
        return new JourneyContext { Character = Character, Account = Account, Log = R.H.Log, Travel = Travel, Spells = Caster.Table, Facts = f };
    }

    /// <summary>Runs a station to its end; returns the run and its final status.</summary>
    public (IStationRun Run, ScenarioStatus Status) RunStation(IStation station, JourneyContext context, double maxSeconds = 400, double dt = 0.25)
    {
        var run = station.Start(context);
        var end = Now + maxSeconds;
        var status = ScenarioStatus.Running;
        while (status == ScenarioStatus.Running && Now < end)
        {
            R.Tick(dt);
            _swapRig?.Pump();
            Pump();
            R.Tick();
            status = run.Step(View, Now);
        }

        Assert.NotEqual(ScenarioStatus.Running, status);
        return (run, status);
    }

    private void Object(uint guid, string name, uint flags, uint wcid, uint unused, float y)
    {
        _ = unused;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(AcademyCell, 10, y), name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, ObjectDescriptionFlags = flags, WeenieClassId = guid == PortalGuid ? 7242u : wcid } }, isUpdate: false);
    }

    private void Chat(string text) => R.Raw(AceWireWriters.ServerMessage(text, 0));

    private void After(double seconds, Action what) => _later.Add((Now + seconds, what));

    private void Teleport(uint cell)
    {
        Events.Add("teleport:" + cell.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        R.H.R.Reducer.OnPlayerTeleport(R.H.R.Context, new PlayerTeleportMsg(++_teleportStamp));
        R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, ReducerHarness.Move(WorldActionRig.Player, 1, ++_positionStamp, _teleportStamp, 1, cell: cell));
        After(1.0, () => R.H.Unhide(++_stateStamp));
    }

    /// <summary>Answers every send since the last call as ACE does, and runs what is due.</summary>
    private void Pump()
    {
        var sends = R.H.Net.Sends;
        while (_seen < sends.Count)
        {
            var send = sends[_seen++];
            switch (send.Kind)
            {
                case FakeSendKind.ChangeCombatMode:
                    Events.Add("mode:" + (int)send.Amount);
                    R.CombatModeIs((int)send.Amount);
                    break;
                case FakeSendKind.Use when send.Guid == LifestoneGuid:
                    Events.Add("use:" + LifestoneGuid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
                    if (!UseSilent)
                    {
                        Chat("You have attuned your spirit to this Lifestone. You will resurrect here after you die.");
                    }

                    R.UseDone();
                    break;
                case FakeSendKind.Use when send.Guid == PortalGuid:
                    Events.Add("use:" + PortalGuid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
                    After(1.0, () => Teleport(DenCell));
                    R.UseDone();
                    break;
                case FakeSendKind.CastTargetedSpell:
                    CastTargeted((uint)send.Index, send.Guid);
                    break;
                case FakeSendKind.CastUntargetedSpell:
                    CastUntargeted((uint)send.Guid);
                    break;
            }
        }

        for (var i = 0; i < _later.Count; i++)
        {
            if (_later[i].Due <= Now)
            {
                var due = _later[i].What;
                _later.RemoveAt(i--);
                due();
            }
        }
    }

    private void CastTargeted(uint spell, uint target)
    {
        Events.Add($"cast:{spell}@{target:X8}");
        After(1.0, () =>
        {
            if (spell == 2644 && _tieFizzled < TieFizzles)
            {
                _tieFizzled++;
                R.WeenieError(0x0402);
            }
            else if (spell == 2644 && TieRefusal != 0)
            {
                R.WeenieError(TieRefusal);
            }
            else if (spell == 2644 && target == LifestoneGuid)
            {
                Chat("You have successfully linked with the life stone.");
            }
            else if (spell == 47 && target == PortalGuid)
            {
                Chat("You have successfully linked with the portal.");
                PortalTied = true;
            }

            R.UseDone();
        });
    }

    private void CastUntargeted(uint spell)
    {
        Events.Add("cast:" + spell);
        After(1.0, () =>
        {
            if (spell == 1635)
            {
                if (!Bound)
                {
                    R.WeenieError(0x049E);
                }
                else
                {
                    After(2.0, () =>
                    {
                        if (RecallIntoLifestone)
                        {
                            // The arrival's position is ReducerHarness.Move's (x 11, y 20, z 30); the lifestone stands on it.
                            var at = ReducerHarness.Position(HoltburgCell, 11, 20, 30);
                            var basis = ReducerHarness.Create(HoltburgLifestoneGuid, ReducerHarness.Stamps(), at, name: "Life Stone");
                            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = "Life Stone", ObjectDescriptionFlags = WorldObjectTable.LifestoneFlag, WeenieClassId = 509 } }, isUpdate: false);
                        }

                        Teleport(HoltburgCell);
                    });
                }
            }
            else if (spell == 48)
            {
                if (PortalRecallRefusal != 0)
                {
                    R.WeenieError(PortalRecallRefusal);
                }
                else if (!PortalTied)
                {
                    R.WeenieError(0x04A3);
                }
                else
                {
                    After(2.0, () => Teleport(DenCell));
                }
            }

            R.UseDone();
        });
    }

    /// <summary>The journey's travel seam as a test double: it records each request and, as a run on foot, arrives on the next step (or fails with <see cref="FailWith"/>).</summary>
    public sealed class RecordingTravel(J3bRig rig) : IJourneyTravel
    {
        /// <summary>Every request, in order.</summary>
        public List<TravelRequest> Requests { get; } = [];

        /// <summary>When set, every leg fails with this reason.</summary>
        public string? FailWith { get; set; }

        /// <inheritdoc />
        public bool Busy => false;

        /// <inheritdoc />
        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            rig.Events.Add("travel:" + request.Purpose);
            if (rig._lifestonePending && request.ObjectGuid == 0)
            {
                rig._lifestonePending = false; // the walk to the lifestone's point brings it into view
                rig.Object(LifestoneGuid, "Lifestone", WorldObjectTable.LifestoneFlag, 1, 7242, 12);
            }

            return new Leg(FailWith);
        }

        private sealed class Leg(string? failure) : ITravelLeg
        {
            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => failure;

            public string Fields => "route_m=3.0";

            public TravelStatus Step(IGameSessionView session, double now) => failure is null ? TravelStatus.Arrived : TravelStatus.Failed;

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
    }
}

using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// Lifestone Recall on a scripted ACE (M4 P5a C1c): a fighter on <see cref="C1bSwapRig"/> (the weapon in hand, the caster in the pack, in peace) or the mage (the wand in hand, the magic mode) with a <see cref="RecallHome"/> over it.
/// The scripted caster hands every cast of spell 1635 to this rig, which answers as ACE does (<c>WorldObject_Magic.cs</c> 1087-1095 at 95ef426d): UseDone, then 2 s later the teleport (PlayerTeleport, the position in the destination cell,
/// then the SetState that unhides the player), or, when <see cref="Bound"/> is false, WeenieError 0x049E and UseDone. <see cref="Events"/> is the order of everything the server saw and did (the swap's asks, the cast, the teleport).
/// </summary>
internal sealed class C1cRecallRig
{
    public const uint HoltburgCell = 0xA9B40019;

    private readonly C1bSwapRig? _swapRig;
    private int _opsSeen;
    private double? _castAt;
    private double? _teleportAt;
    private double? _unhideAt;
    private ushort _teleportStamp = 2;
    private ushort _positionStamp = 2;
    private ushort _stateStamp = 2;

    /// <summary>A rig for the fighter template <paramref name="template"/>, or for the mage when it is null; <paramref name="spell"/> and <paramref name="homeLandblock"/> pick the recall (M4 P5b J1: Primary Portal Recall 48 to the den, 0x0105).</summary>
    public C1cRecallRig(FighterTemplate? template, bool knowsSpell = true, uint spell = RecallHome.LifestoneRecallSpell, uint homeLandblock = RecallHome.HoltburgLandblock)
    {
        Spell = spell;
        if (template is not null)
        {
            _swapRig = new C1bSwapRig(template);
            R = _swapRig.R;
            Caster = _swapRig.Caster;
            View = _swapRig.View;
            Recall = new RecallHome(View, _swapRig.Swap, R.H.Log, homeLandblock, spell);
        }
        else
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            View = new MageView(R);
            Recall = new RecallHome(View, null, R.H.Log, homeLandblock, spell);
        }

        // The character knows Lifestone Recall (the description of the scripted mage lists no spells; ACE's LearnSpell event 0x02C1 adds it to the spellbook).
        if (knowsSpell)
        {
            R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, (ushort)spell));
        }

        Caster.CastHook = spell =>
        {
            if (spell != Spell)
            {
                return false;
            }

            Casts++;
            Events.Add("cast:" + spell);
            _castAt = Now;
            return true;
        };
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

    /// <summary>The recall spell the scripted ACE answers (1635 by default, 48 for Primary Portal Recall).</summary>
    public uint Spell { get; }

    /// <summary>The WeenieError ACE sends when <see cref="Bound"/> is false: 0x049E (no lifestone) by default, 0x04A3 (no portal tied) for Primary Portal Recall.</summary>
    public uint UnboundRefusal { get; set; } = 0x049E;

    public ScriptedCaster Caster { get; }

    public IGameSessionView View { get; }

    public RecallHome Recall { get; }

    public C1bSwapRig? SwapRig => _swapRig;

    /// <summary>False: the character is bound to no lifestone (ACE answers WeenieError 0x049E).</summary>
    public bool Bound { get; set; } = true;

    /// <summary>The cell the teleport arrives in (Holtburg's by default).</summary>
    public uint DestinationCell { get; set; } = HoltburgCell;

    /// <summary>True: ACE never teleports (the cast ends and nothing follows).</summary>
    public bool NoTeleport { get; set; }

    /// <summary>True: the teleport begins but the player is never unhidden (stuck in portal space).</summary>
    public bool StayInPortal { get; set; }

    /// <summary>True: the cast fizzles (WeenieError 0x0402, UseDone).</summary>
    public bool Fizzle { get; set; }

    /// <summary>The first this-many casts fizzle (WeenieError 0x0402, UseDone), the later ones land: P5a-R4's retried fizzle.</summary>
    public int FizzleFirst { get; set; }

    /// <summary>Seconds the cast takes before ACE answers it.</summary>
    public double CastSeconds { get; set; } = 1.0;

    /// <summary>Seconds from the cast's end to the teleport (ACE's recall delay).</summary>
    public double RecallDelaySeconds { get; set; } = 2.0;

    public int Casts { get; private set; }

    private int _fizzled;

    /// <summary>The order of what happened: the swap's asks (<c>unwield:</c>, <c>wield:</c>, <c>mode:</c>), <c>cast:1635</c>, <c>teleport</c>, <c>present</c>.</summary>
    public List<string> Events { get; } = [];

    public double Now => R.H.R.Clock.Now;

    /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers (casts, wields, the recall), the Action API completes, then the recall steps.</summary>
    public void Frame(double dt = 0.25)
    {
        R.Tick(dt);
        Caster.Pump();
        _swapRig?.Pump();
        if (_swapRig is not null)
        {
            for (; _opsSeen < _swapRig.Ops.Count; _opsSeen++)
            {
                Events.Add(_swapRig.Ops[_opsSeen]);
            }
        }

        Script();
        R.Tick();
        Recall.Step(Now);
    }

    /// <summary>Frames until the recall is no longer running (or the time ran out); the status.</summary>
    public RecallStatus RunRecall(double maxSeconds = 200, double dt = 0.25)
    {
        Assert.True(Recall.Begin(Now));
        var end = Now + maxSeconds;
        while (Recall.Status == RecallStatus.Running && Now < end)
        {
            Frame(dt);
        }

        return Recall.Status;
    }

    private void Script()
    {
        if (_castAt is { } cast && Now >= cast + CastSeconds)
        {
            _castAt = null;
            if (Fizzle || _fizzled < FizzleFirst)
            {
                _fizzled++;
                R.WeenieError(0x0402);
            }
            else if (!Bound)
            {
                R.WeenieError(UnboundRefusal);
            }
            else if (!NoTeleport)
            {
                _teleportAt = Now + RecallDelaySeconds;
            }

            R.UseDone();
        }

        if (_teleportAt is { } tele && Now >= tele)
        {
            _teleportAt = null;
            Events.Add("teleport");
            R.H.R.Reducer.OnPlayerTeleport(R.H.R.Context, new PlayerTeleportMsg(++_teleportStamp));
            R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, ReducerHarness.Move(WorldActionRig.Player, 1, ++_positionStamp, _teleportStamp, 1, cell: DestinationCell));
            if (!StayInPortal)
            {
                _unhideAt = Now + 1.0;
            }
        }

        if (_unhideAt is { } unhide && Now >= unhide)
        {
            _unhideAt = null;
            R.H.Unhide(++_stateStamp);
            Events.Add("present");
        }
    }
}

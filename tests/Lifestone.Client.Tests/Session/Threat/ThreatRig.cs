using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// The threat board's test world: the fight tests' scripted session (the den's ground, a mage standing on its floor) with creatures placed east of her, the server's notices and lines sent as the reducer publishes them, and the
/// two detectors the board stands beside (the outer loop's <see cref="HostileWatch"/>, the travel legs' <see cref="AttackWatch"/> read through <see cref="HostileReach"/>) built on the same session.
/// </summary>
internal sealed class ThreatRig
{
    /// <summary>The outer loop's reach (<c>DeathOptions.DefaultTripAggroMetres</c>).</summary>
    public const double OuterReach = DeathOptions.DefaultTripAggroMetres;

    /// <summary>The travel legs' reach (<c>HostileReach.DefaultMetres</c>).</summary>
    public const double LegReach = HostileReach.DefaultMetres;

    /// <summary>The travel legs' heard window (<c>HuntScenario.DefendHeardSeconds</c>).</summary>
    public const double LegSeconds = HuntScenario.DefendHeardSeconds;

    /// <summary>A weenie class on no ground's target list.</summary>
    public const uint Unlisted = 999_001;

    private uint _next = 0x80007000;

    public FightManaTests.Rig Rig { get; } = new(mana: 300, stamina: 200);

    public IGameSessionView View => Rig.View;

    public double Now => Rig.Now;

    public HuntingGround Ground => Rig.Fight.GroundProfile;

    /// <summary>The ground's target list, as the outer loop's watch reads it.</summary>
    public IReadOnlyList<uint> Targets { get; }

    /// <summary>A weenie class of the ground's target list.</summary>
    public uint Listed => Targets[0];

    public ThreatBoard Board => ThreatBoard.For(View);

    public ThreatRig() => Targets = FightTargets.Wcids(Ground);

    /// <summary>A living creature <paramref name="metres"/> east of her.</summary>
    public uint Creature(string name, uint wcid, float metres, uint flags = WorldObjectTable.AttackableFlag)
    {
        var guid = _next++;
        var at = View.State.Self.Position!.Value;
        var h = Rig.R.H.R;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Origin.X + metres, at.Origin.Y, at.Origin.Z), name: name);
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = wcid, ObjectDescriptionFlags = flags } }, isUpdate: false);
        return guid;
    }

    /// <summary>The server takes <paramref name="guid"/> out of her view.</summary>
    public void Gone(uint guid) => Rig.R.H.R.Reducer.OnDeleteObject(Rig.R.H.R.Context, new DeleteObjectMsg(guid, 1));

    /// <summary>The session clock moves on.</summary>
    public void Wait(double seconds) => Rig.R.Tick(seconds);

    /// <summary>GameEvent 0x01B2: <paramref name="who"/> hit her for <paramref name="damage"/>.</summary>
    public void Hit(string who, uint damage = 7) =>
        Rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, who, 1, damage, 0.05, false, 4, false, 0, $"{who} hits you for {damage} points of slashing damage!"));

    /// <summary>GameEvent 0x01B4: she evaded <paramref name="who"/>.</summary>
    public void Evade(string who) =>
        Rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseEvaded, who, 0, 0, 0, false, -1, true, 0, $"You evaded {who}!"));

    /// <summary>A Magic-typed system line, as the server writes a creature's spell to her.</summary>
    public void Magic(string text) => Rig.R.H.ServerMessage(text, (int)FightTally.MagicChatType);

    /// <summary>A swing order of <paramref name="guid"/> that names her as the object it is stuck to (ACE <c>Monster_Melee.cs</c> <c>DoSwingMotion</c>).</summary>
    public void Swing(uint guid) =>
        Rig.R.H.R.Bus.Publish(new ObjectMotionChanged(guid, false, false, new ServerMotionState { MovementType = MovementType.Interpreted, StickyGuid = View.State.Self.Guid }));

    /// <summary>The Dead motion of <paramref name="guid"/>.</summary>
    public void DeadMotion(uint guid) =>
        Rig.R.H.R.Bus.Publish(new ObjectMotionChanged(guid, false, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = WorldObjectTable.DeadMotionLowWord }));

    /// <summary>A watch as the outer loop builds it.</summary>
    public HostileWatch OuterWatch() => new(View, Ground, OuterReach);

    /// <summary>A watch as a travel leg builds it, started.</summary>
    public AttackWatch LegWatch()
    {
        var watch = new AttackWatch(View, static _ => { });
        watch.Start();
        return watch;
    }

    /// <summary>The travel legs' read of the attackers present: the names <paramref name="watch"/> heard within the window, alive and attackable within the reach.</summary>
    public List<PresentAttacker> Present(AttackWatch watch)
    {
        var present = new List<PresentAttacker>();
        HostileReach.Present(View, LegReach, watch.HeardWithin(LegSeconds), present);
        return present;
    }

    /// <summary>The outer loop's question to the board.</summary>
    public ThreatAnswer AskInReach() => Board.Ask(View, ThreatQuery.InReach(OuterReach, HostileWatch.AttackQuietSeconds, Targets), Now);

    /// <summary>The travel legs' question to the board.</summary>
    public ThreatAnswer AskOnHer() => Board.Ask(View, ThreatQuery.OnHer(LegReach, LegSeconds), Now);
}

using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Diagnostics;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>An <see cref="IEventBus"/> whose events the test publishes, synchronously, in subscription order.</summary>
internal sealed class FakeEventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly Dictionary<Type, long> _counts = [];

    public IDisposable Subscribe<T>(BusHandler<T> handler)
        where T : struct, IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryGetValue(typeof(T), out var list))
        {
            _handlers[typeof(T)] = list = [];
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    public IDisposable SubscribeRaw(RawMessageHandler handler) => throw new NotSupportedException("the portal bridge does not tap raw messages");

    public long Count<T>()
        where T : struct, IGameEvent => _counts.GetValueOrDefault(typeof(T));

    /// <summary>How many handlers listen to <typeparamref name="T"/>.</summary>
    public int Subscribers<T>()
        where T : struct, IGameEvent => _handlers.TryGetValue(typeof(T), out var list) ? list.Count : 0;

    /// <summary>Publishes <paramref name="e"/> to every handler of its type.</summary>
    public void Publish<T>(T e)
        where T : struct, IGameEvent
    {
        _counts[typeof(T)] = Count<T>() + 1;
        if (_handlers.TryGetValue(typeof(T), out var list))
        {
            foreach (var handler in list.ToArray())
            {
                ((BusHandler<T>)handler)(in e);
            }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

/// <summary>An <see cref="IActionApi"/> that records LoginComplete; every other action is not part of portal space.</summary>
internal sealed class FakeActions : IActionApi
{
    private long _nextId;

    public List<(ActionSource Source, double At)> LoginCompletes { get; } = [];

    /// <summary>The time stamped on a LoginComplete (the test sets it to its frame time).</summary>
    public double Now { get; set; }

    public IMovementSink Movement => throw Unexpected();

    public MoveIntents Move => throw Unexpected();

    public bool KillSwitch { get; set; }

    public bool DryRun { get; set; }

    public ActionHandle LoginComplete(ActionSource source = ActionSource.Client)
    {
        LoginCompletes.Add((source, Now));
        return new ActionHandle(++_nextId, ActionKind.LoginComplete, source, "LoginComplete", Now);
    }

    public ActionHandle Login(ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle SelectCharacter(uint guid, ActionSource source) => throw Unexpected();

    public ActionHandle CreateCharacter(CharacterCreateIntent intent, ActionSource source) => throw Unexpected();

    public ActionHandle DeleteCharacter(uint guid, ActionSource source) => throw Unexpected();

    public ActionHandle RestoreCharacter(uint guid, ActionSource source) => throw Unexpected();

    public ActionHandle EnterWorld(uint guid, ActionSource source) => throw Unexpected();

    public ActionHandle Use(uint guid, ActionSource source) => throw Unexpected();

    public ActionHandle QueryHealth(uint guid, ActionSource source) => throw Unexpected();

    public ActionHandle RaiseAttribute(uint attribute, uint xpToSpend, ActionSource source) => throw Unexpected();

    public ActionHandle RaiseVital(uint vital, uint xpToSpend, ActionSource source) => throw Unexpected();

    public ActionHandle RaiseSkill(uint skill, uint xpToSpend, ActionSource source) => throw Unexpected();

    public ActionHandle TrainSkill(uint skill, int creditsToSpend, ActionSource source) => throw Unexpected();

    public ActionHandle Cast(uint spellId, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle Cast(uint spellId, uint target, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle SetCombatMode(CombatMode mode, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle Wield(uint item, EquipMask location, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle Unwield(uint item, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle MoveToPack(uint item, uint container = 0, uint placement = 0, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle MergeStack(uint from, uint to, int amount, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle Suicide(ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle AddSpellFavorite(uint spellId, int index, int bar, ActionSource source = ActionSource.Host) => throw Unexpected();
    public ActionHandle RemoveSpellFavorite(uint spellId, int bar, ActionSource source = ActionSource.Host) => throw Unexpected();

    public ActionHandle Talk(string text, ActionSource source) => throw Unexpected();

    public ActionHandle Tell(string target, string text, ActionSource source) => throw Unexpected();

    public ActionHandle LogOff(ActionSource source) => throw Unexpected();

    public ActionHandle Quit(ActionSource source) => throw Unexpected();

    public void SetSessionOption(int word, uint mask, bool value) => throw Unexpected();

    private static NotSupportedException Unexpected() => new("portal space only sends LoginComplete");
}

/// <summary>A session view over <see cref="FakeEventBus"/> and <see cref="FakeActions"/>.</summary>
internal sealed class FakeSession : IGameSessionView
{
    public FakeEventBus Bus { get; } = new();

    public FakeActions FakeActions { get; } = new();

    public GameState State { get; } = new();

    public IEventBus Events => Bus;

    public IActionApi Actions => FakeActions;

    public PlayerTools Tools => throw new NotSupportedException("portal space does not use the player tools");

    public SessionClock Clock { get; } = new(new ManualTimeProvider());

    public ActionLog ActionLog { get; } = new();

    public LinkStats Link => default;
}

/// <summary>Records what the director hands to its sinks, with the frame time.</summary>
internal sealed class PortalSinks : IPortalNoticeSink, ILoginCompleteSink
{
    public double Now { get; set; }

    public List<(string Literal, uint TextType, double At)> Notices { get; } = [];

    public List<double> LoginCompletes { get; } = [];

    public void PostNotice(string literal, uint textType) => Notices.Add((literal, textType, Now));

    public void SendLoginComplete() => LoginCompletes.Add(Now);
}

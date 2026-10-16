using System.Reflection;
using System.Runtime.CompilerServices;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session;

/// <summary>
/// Compiles the session's message path on a worker thread before the first server message arrives, so the world-entry burst does not pay for the just-in-time compiler on the game
/// thread (DoD 15, <c>TODO(login-burst-frame)</c>; docs/session.md section 27). The first PlayerDescription of a login (the graduated test character's: 138 enchantments, hundreds of
/// spells) measured 51 ms live in one frame: offline, the same message decoded in 21 ms and reduced in 16 ms the first time and 0.23 ms in all the second, so the cost was compilation of
/// the decoder's and the reducer's code (the immutable tables and lists over a dozen value types, the log line's LINQ), not the message's size.
/// </summary>
/// <remarks>
/// <para>M3 1.2 s5: step 1 also runs each of the 25 handlers s0 added (enchantments, inventory, combat, the environ) once on the throwaway reducer (<see cref="RunHandlers"/>), because a handler's generic
/// instantiations over the new event and value types (the event bus's publish, the registry lists, the object table) are only compiled by running them, which step 2 cannot do for an open generic; and
/// step 2 also prepares the action trackers (<c>Lifestone.Client.Session.Actions</c>) and the stand-in's answers (<c>Lifestone.Client.Session.StandIn</c>).</para>
/// <para>Two steps, both off the game thread and at below-normal priority. (1) <b>Run once</b>: a small well-formed PlayerDescription (<see cref="WarmUpMessages.PlayerDescription"/>, one entry in
/// every table) is decoded by the real decoder and reduced by a throwaway reducer over a state, bus and clock of its own, which compiles every generic instantiation the message uses.
/// Nothing is shared with the live session but static code (<see cref="EventTypeId{T}"/> hands out its indices atomically), and the throwaway log is silent. (2)
/// <b>Prepare</b>: <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/> over every method of the message codecs, the dispatcher, the reducer, the event bus and the contract types, which
/// covers the other messages of the burst (the first CreateObjects and updates). No code is executed by step 2. A method the game thread needs while the worker is compiling it waits for that
/// method, as it would compile it itself.</para>
/// </remarks>
public static class SessionJitWarmUp
{
    private static readonly string[] Namespaces =
    [
        "Lifestone.Net.Messages", "Lifestone.Net.Dispatch", "Lifestone.Net.Wire",
        "Lifestone.Client.Session.State", "Lifestone.Client.Session.Bus", "Lifestone.Client.Session.Contracts",

        // M3 1.2 s5: the action trackers' observation paths (OnInventory, OnEnchantment, OnCombatMode, OnObjectCreated, ...) and the stand-in's answers, which run on the game thread at arrival offline.
        "Lifestone.Client.Session.Actions", "Lifestone.Client.Session.StandIn",
    ];

    /// <summary>
    /// M3 2.1 fix A (2.1-R4): single types outside <see cref="Namespaces"/> whose methods the game thread would otherwise compile at the first chat line, motion or cast of an arrival: the avatar adapter's cast path
    /// (<c>OnChat</c>, <c>OnUseDone</c>, <c>IsCastGesture</c> through <c>OnMotion</c>, <c>ApplyCast</c>). The type, not its namespace: the rest of <c>Lifestone.Client.Avatars</c> (the stress and preview code) is not the arrival's.
    /// </summary>
    private static readonly Type[] ExtraTypes = [typeof(Lifestone.Client.Avatars.SessionAvatarAdapter)];

    private static int _started;
    private static volatile Result? _result;

    /// <summary>What the worker did, or null while it runs (or before <see cref="Start"/>).</summary>
    public static Result? Done => _result;

    /// <summary>The worker's outcome.</summary>
    /// <param name="DescriptionDecoded">The sample PlayerDescription was decoded and reduced (false means the sample no longer matches the decoder: <c>TODO(net)</c> fix the sample).</param>
    /// <param name="Methods">Methods prepared.</param>
    /// <param name="Skipped">Methods that could not be prepared.</param>
    /// <param name="RunMilliseconds">Wall-clock milliseconds of step 1.</param>
    /// <param name="Milliseconds">Wall-clock milliseconds of both steps.</param>
    public sealed record Result(bool DescriptionDecoded, int Methods, int Skipped, double RunMilliseconds, double Milliseconds);

    /// <summary>Starts the worker once per process.</summary>
    /// <param name="time">The clock that times the worker (the host's; the session layer reads no ambient time).</param>
    public static void Start(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var thread = new Thread(() => Work(time)) { IsBackground = true, Name = "SessionJitWarmUp", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    /// <summary>Runs the warm-up on the calling thread (tests and tools); returns what it did.</summary>
    /// <param name="time">The clock that times the run.</param>
    public static Result RunHere(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        Work(time);
        return _result!;
    }

    private static void Work(TimeProvider time)
    {
        var started = time.GetTimestamp();
        var decoded = false;
        try
        {
            decoded = RunSample(time);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or TypeLoadException)
        {
            // best effort: the live session compiles what this did not
        }

        var run = time.GetElapsedTime(started).TotalMilliseconds;
        var methods = 0;
        var skipped = 0;
        try
        {
            foreach (var type in Candidates())
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                             .Cast<MethodBase>()
                             .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                    {
                        continue;
                    }

                    try
                    {
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                        methods++;
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidProgramException or TypeLoadException or FileNotFoundException)
                    {
                        skipped++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or TypeLoadException or FileNotFoundException)
        {
            skipped++;
        }

        _result = new Result(decoded, methods, skipped, run, time.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>Step 1: decode the sample with the real decoder and reduce it in a reducer nothing else uses.</summary>
    private static bool RunSample(TimeProvider time)
    {
        var state = new GameState();
        var bus = new EventBus(NullLogSink.Instance);
        var reducer = new GameStateReducer(state, bus, new SessionClock(time), NullLogSink.Instance);
        var body = WarmUpMessages.PlayerDescription(0x50000001);
        var result = InboundDispatcher.Dispatch(new InboundContext(0, MessageQueue.Ui, 0), body, reducer);
        var described = result.Outcome == DispatchOutcome.Decoded && state.Self.Qualities.IsKnown;
        try
        {
            RunHandlers(reducer, 0x50000001);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or TypeLoadException or KeyNotFoundException or NullReferenceException)
        {
            // best effort, as step 1 is: the live session compiles what this did not
        }

        return described;
    }

    /// <summary>
    /// Runs each of the 25 handlers s0 added once on a throwaway reducer (the player is created, then every enchantment, inventory, combat and environ message), so their code and generic instantiations are compiled
    /// on the worker. The values are placeholders: only the code path matters.
    /// </summary>
    internal static void RunHandlers(GameStateReducer reducer, uint player)
    {
        var context = new InboundContext(0, MessageQueue.SmartBox, 0);
        var header = new GameEventHeader(player, 2, 0);
        const uint item = 0x80000901;
        const uint pack = 0x80000902;
        reducer.OnPlayerCreate(context, new PlayerCreateMsg(player));
        reducer.OnPlayerDescription(context, header, new PlayerDescriptionMsg { Inventory = [new ContentDesc(pack, 1)] });

        // Enchantments (0x02C1 to 0x02C8, 0x0312).
        EnchantmentDesc Enchant(ushort spell, ushort layer) =>
            new(spell, layer, 3, 0, 10, 0.0, 600.0, player, 0f, 0f, 0.0, 0x2009001, 1, 5f, 0, EnchantmentBucket.Additive);
        reducer.OnMagicUpdateSpell(context, header, new MagicUpdateSpellEvent(1, 1));
        reducer.OnMagicUpdateEnchantment(context, header, new MagicUpdateEnchantmentEvent(Enchant(2, 1)));
        reducer.OnMagicUpdateEnchantment(context, header, new MagicUpdateEnchantmentEvent(Enchant(2, 2)));
        reducer.OnMagicUpdateMultipleEnchantments(context, header, new MagicUpdateMultipleEnchantmentsEvent([Enchant(3, 1), Enchant(4, 1), Enchant(5, 1)]));
        reducer.OnMagicDispelEnchantment(context, header, new MagicDispelEnchantmentEvent(3, 1));
        reducer.OnMagicDispelMultipleEnchantments(context, header, new MagicDispelMultipleEnchantmentsEvent([new LayeredSpellDesc(4, 1)]));
        reducer.OnMagicRemoveEnchantment(context, header, new MagicRemoveEnchantmentEvent(2, 2));
        reducer.OnMagicRemoveMultipleEnchantments(context, header, new MagicRemoveMultipleEnchantmentsEvent([new LayeredSpellDesc(5, 1)]));
        reducer.OnMagicPurgeBadEnchantments(context, header, new MagicPurgeBadEnchantmentsEvent());
        reducer.OnMagicPurgeEnchantments(context, header, new MagicPurgeEnchantmentsEvent());

        // Inventory (0x0024, 0x0197, 0x02CE, 0x02DA, 0x0022, 0x0023, 0x019A, 0x00A0): an object created, put in the pack, wielded, put back, stacked, updated, refused, dropped and removed.
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = "warm-up", WeenieClassId = 3, IconId = 0x06000001, ItemType = 1, ObjectDescriptionFlags = 0, ContainerId = player };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.Gravity, Timestamps = default };
        reducer.OnCreateObject(context, new CreateObjectMsg(item, ModelData.Empty, physics, weenie), isUpdate: false);
        reducer.OnInventoryPutObjInContainer(context, header, new InventoryPutObjInContainerEvent(item, player, 0, 0));
        reducer.OnPublicUpdatePropertyInt(context, new PublicUpdatePropertyIntMsg(1, item, 12, 3));
        reducer.OnPublicUpdateInstanceId(context, new PublicUpdateInstanceIdMsg(1, item, 2, player));
        reducer.OnSetStackSize(context, new SetStackSizeMsg(1, item, 5, 50));
        reducer.OnWieldObject(context, header, new WieldObjectEvent(item, 0x100000));
        reducer.OnInventoryPutObjInContainer(context, header, new InventoryPutObjInContainerEvent(item, player, 0, 0));
        reducer.OnInventoryServerSaysFailed(context, header, new InventoryServerSaysFailedEvent(item, 0x1E));
        reducer.OnInventoryPutObjectIn3D(context, header, new InventoryPutObjectIn3DEvent(item));
        reducer.OnInventoryRemoveObject(context, new InventoryRemoveObjectMsg(item));

        // Combat (0x01AC, 0x01AD, 0x01B1 to 0x01B4, 0x019E).
        reducer.OnVictimNotification(context, header, new VictimNotificationEvent("warm-up"));
        reducer.OnKillerNotification(context, header, new KillerNotificationEvent("warm-up"));
        reducer.OnAttackerNotification(context, header, new AttackerNotificationEvent("warm-up", 1, 0.5, 5, false, 0));
        reducer.OnDefenderNotification(context, header, new DefenderNotificationEvent("warm-up", 1, 0.5, 5, 1, false, 0));
        reducer.OnEvasionAttackerNotification(context, header, new EvasionAttackerNotificationEvent("warm-up"));
        reducer.OnEvasionDefenderNotification(context, header, new EvasionDefenderNotificationEvent("warm-up"));
        reducer.OnPlayerKilled(context, new PlayerKilledMsg("warm-up", player, 0x80000903));

        // The environs (M3 1.3 s2b): a clear and a sound, whose handler publishes the event.
        reducer.OnAdminEnvirons(context, new AdminEnvironsMsg(0));
        reducer.OnAdminEnvirons(context, new AdminEnvironsMsg(101));
    }

    private static IEnumerable<Type> Candidates()
    {
        var assemblies = new[] { typeof(WarmUpMessages).Assembly, typeof(SessionJitWarmUp).Assembly };
        var queue = new Queue<Type>(assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace is { } ns && Namespaces.Any(n => ns.StartsWith(n, StringComparison.Ordinal)) && !t.ContainsGenericParameters)
            .Concat(ExtraTypes));
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            yield return type;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!nested.ContainsGenericParameters)
                {
                    queue.Enqueue(nested);
                }
            }
        }
    }
}

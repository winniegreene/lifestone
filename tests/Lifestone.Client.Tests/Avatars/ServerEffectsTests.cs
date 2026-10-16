using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using DatPlayScript = DatReaderWriter.Enums.PlayScript;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 1.3 s0: the scripts (F755) and sounds (F750) the server plays on an object, from the reducer's real event stream through the adapter to the avatar set
/// (<c>SmartBox::HandlePlayScriptType</c>, 11.6096 0x004520B0; <c>SmartBox::HandleSoundEvent</c>, 11.6096 0x00452000), on the synthetic avatar world.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ServerEffectsTests
{
    private const uint Npc = 0x80000200;
    private const uint Cell = AvatarRig.HoltburgCell;
    private const uint HumanSoundTable = 0x20000001;
    private const uint EnchantUp = 90;
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new System.Numerics.Vector3(96, 96, 0)));

    [Fact]
    public void A_script_type_the_bodys_table_has_plays_on_its_effects_with_the_scripts_own_hooks()
    {
        var w = new World();
        w.Enter();
        w.Create(Npc);
        w.H.Reducer.OnPlayEffect(w.H.Context, new PlayEffectMsg(Npc, (uint)DatPlayScript.LevelUp, 1f));
        w.Adapter.Drain();

        Assert.Equal(1, w.Avatars.Stats.ServerScriptsPlayed);
        Assert.Equal(0, w.Avatars.Stats.ServerScriptsMissing);
        Assert.True(w.Avatars.TryGet(Npc, out var npc));
        Assert.Contains(npc.Body.Effects.Scripts.Queue, q => q.ScriptId == AvatarRig.LevelUpScript);

        // The script's own hook runs when the body is stepped, and reaches the sink as any hook event does.
        var sink = new RecordingSink();
        w.Avatars.EffectEvents = sink;
        w.Avatars.Advance(0.1, Viewer, long.MaxValue);
        var heard = Assert.Single(sink.Events);
        var hook = Assert.IsType<SoundTableHook>(heard.Hook);
        Assert.Equal(AvatarRig.LevelUpHookSoundType, hook.SoundType);
        Assert.Equal(HumanSoundTable, Assert.Single(sink.Tables)); // a SoundTable hook plays from the table of the body that fired it
    }

    [Fact]
    public void A_script_type_the_table_lacks_is_counted_and_logged_once_per_table_and_type()
    {
        var log = new RecordingLogSink();
        var w = new World(log);
        w.Enter();
        w.Create(Npc);
        w.Create(Npc + 1, scriptTable: false);

        w.H.Reducer.OnPlayEffect(w.H.Context, new PlayEffectMsg(Npc, 0x7777, 1f));
        w.H.Reducer.OnPlayEffect(w.H.Context, new PlayEffectMsg(Npc, 0x7777, 0.5f));
        w.H.Reducer.OnPlayEffect(w.H.Context, new PlayEffectMsg(Npc, 0x7778, 1f));
        w.H.Reducer.OnPlayEffect(w.H.Context, new PlayEffectMsg(Npc + 1, (uint)DatPlayScript.LevelUp, 1f)); // a body with no table at all
        w.Adapter.Drain();

        Assert.Equal(0, w.Avatars.Stats.ServerScriptsPlayed);
        Assert.Equal(4, w.Avatars.Stats.ServerScriptsMissing);
        Assert.Equal(0, w.Adapter.Stats.Failed);
        var lines = log.Entries.Where(e => e.Message.Contains("PhysicsScriptTable", StringComparison.Ordinal) && e.Severity == LogSeverity.Warning).ToList();
        Assert.Equal(3, lines.Count); // (table, 0x7777) once although it came twice, (table, 0x7778), (no table, LevelUp)
        Assert.Contains("type 30583", lines[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sound_becomes_a_sound_table_event_on_the_body_and_reaches_the_sink_with_the_bodys_table_and_the_volume()
    {
        var w = new World();
        w.Enter();
        w.Create(Npc);
        var sink = new RecordingSink();
        w.Avatars.EffectEvents = sink;
        w.H.Reducer.OnSound(w.H.Context, new SoundMsg(Npc, EnchantUp, 0.25f));
        w.Adapter.Drain();
        Assert.Equal(1, w.Avatars.Stats.ServerSoundsPlayed);
        Assert.Empty(sink.Events); // queued on the body: it is handed over with the frame's other hook events

        w.Avatars.Advance(0.1, Viewer, long.MaxValue);

        var heard = Assert.Single(sink.Events);
        var hook = Assert.IsType<ServerSoundTableHook>(heard.Hook);
        Assert.Equal(new ServerSoundTableHook(HumanSoundTable, EnchantUp, 0.25f), hook);
        Assert.Equal(HumanSoundTable, Assert.Single(sink.Tables)); // the emitting body's sound table travels with the event
        Assert.Equal(1, w.Avatars.Stats.EffectEventsDelivered);
        Assert.Equal(0, w.Avatars.Stats.EffectEventsDropped);

        // Delivered once: a second frame hears nothing more.
        w.Avatars.Advance(0.2, Viewer, long.MaxValue);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void A_sound_without_a_sink_is_counted_played_and_counted_dropped_never_lost_silently()
    {
        var w = new World();
        w.Enter();
        w.Create(Npc);
        w.H.Reducer.OnSound(w.H.Context, new SoundMsg(Npc, EnchantUp, 1f));
        w.Adapter.Drain();
        w.Avatars.Advance(0.1, Viewer, long.MaxValue);

        Assert.Equal(1, w.Avatars.Stats.ServerSoundsPlayed);
        Assert.Equal(1, w.Avatars.Stats.EffectEventsDropped);
        Assert.Equal(0, w.Avatars.Stats.EffectEventsDelivered);
    }

    [Fact]
    public void A_script_and_a_sound_that_arrive_before_the_objects_create_are_parked_by_the_reducer_and_applied_after_it()
    {
        var w = new World();
        w.Enter();
        var sink = new RecordingSink();
        w.Avatars.EffectEvents = sink;

        // ACE's UIQueue bundle goes out before the SmartboxQueue one, so an effect can come ahead of (or without) its object's CreateObject.
        w.H.Reducer.OnPlayEffect(w.H.Context, new PlayEffectMsg(Npc, (uint)DatPlayScript.LevelUp, 1f));
        w.H.Reducer.OnSound(w.H.Context, new SoundMsg(Npc, EnchantUp, 0.5f));
        w.Adapter.Drain();
        Assert.Equal(0, w.Avatars.Stats.ServerScriptsPlayed);
        Assert.Equal(0, w.Avatars.Stats.ServerSoundsPlayed);
        Assert.Equal(0, w.Avatars.Stats.ServerEffectsDropped);

        w.Create(Npc);
        w.Avatars.Advance(0.1, Viewer, long.MaxValue);

        Assert.Equal(1, w.Avatars.Stats.ServerScriptsPlayed);
        Assert.Equal(1, w.Avatars.Stats.ServerSoundsPlayed);
        Assert.Equal(0, w.Avatars.Stats.ServerEffectsDropped);
        Assert.Equal(0, w.Adapter.Stats.Failed);
        Assert.Equal(2, sink.Events.Count); // the script's own SoundTable hook and the server's sound
        Assert.Contains(sink.Events, e => e.Hook is ServerSoundTableHook { Volume: 0.5f });
    }

    [Fact]
    public void An_effect_for_an_object_the_set_does_not_hold_is_dropped_and_counted_not_thrown()
    {
        var log = new RecordingLogSink();
        var set = new AvatarSet(AvatarRig.Sources(), log);

        Assert.Equal(AvatarApply.UnknownObject, set.PlayScript(0x80000999, (uint)DatPlayScript.LevelUp, 1f));
        Assert.Equal(AvatarApply.UnknownObject, set.PlaySound(0x80000999, EnchantUp, 1f));
        Assert.Equal(AvatarApply.UnknownObject, set.PlaySound(0x80000999, EnchantUp, 1f));

        Assert.Equal(3, set.Stats.ServerEffectsDropped);
        Assert.Equal(0, set.Stats.ServerScriptsPlayed + set.Stats.ServerSoundsPlayed);
        Assert.Single(log.Entries, e => e.Message.Contains("does not have", StringComparison.Ordinal)); // logged once
    }

    [Fact]
    public void Handing_a_server_sound_to_the_sink_allocates_nothing_and_idle_frames_after_it_neither()
    {
        var w = new World();
        w.Enter();
        w.Create(Npc);
        var sink = new RecordingSink();
        w.Avatars.EffectEvents = sink;
        for (var i = 0; i < 4; i++)
        {
            w.Avatars.PlaySound(Npc, EnchantUp, 1f);
            w.Avatars.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        sink.Events.Clear();
        sink.Tables.Clear();
        w.Avatars.PlaySound(Npc, EnchantUp, 1f); // the message's own event is allocated here, once per message, outside the measured frames
        var before = GC.GetAllocatedBytesForCurrentThread();
        w.Avatars.Advance(1.0, Viewer, long.MaxValue); // the delivery frame: handing the event to the sink allocates nothing
        for (var i = 1; i <= 30; i++)
        {
            w.Avatars.Advance(1.0 + i / 30.0, Viewer, long.MaxValue); // idle frames
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Single(sink.Events);
        Assert.Equal(0, bytes);
    }

    private sealed class RecordingSink : IEffectEventSink
    {
        public List<EffectEvent> Events { get; } = new(16);

        public List<uint> Tables { get; } = new(16);

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId)
        {
            Events.Add(effectEvent);
            Tables.Add(soundTableId);
        }
    }

    private sealed class World
    {
        public World(RecordingLogSink? log = null)
        {
            H = new ReducerHarness();
            Avatars = new AvatarSet(AvatarRig.Sources(), log);
            Adapter = new SessionAvatarAdapter(H.Bus, H.State, Avatars, () => 0.0, log);
        }

        public ReducerHarness H { get; }

        public AvatarSet Avatars { get; }

        public SessionAvatarAdapter Adapter { get; }

        public void Enter()
        {
            H.Reducer.OnPlayerCreate(H.Context, new PlayerCreateMsg(ReducerHarness.PlayerGuid));
            H.Reducer.OnCreateObject(H.Context, Human(ReducerHarness.PlayerGuid, 5f, scriptTable: true), false);
            Adapter.Drain();
        }

        public void Create(uint guid, bool scriptTable = true)
        {
            H.Reducer.OnCreateObject(H.Context, Human(guid, 12f, scriptTable), false);
            Adapter.Drain();
        }

        /// <summary>A creature on the rig's human Setup, with the rig's script table and the human sound table when asked (what ACE sends for a person).</summary>
        private static CreateObjectMsg Human(uint guid, float x, bool scriptTable)
        {
            var msg = ReducerHarness.Create(guid, Stamps(instance: 1), Position(Cell, x: x), name: "Guard", setup: AvatarRig.Human);
            var flags = msg.Physics.Flags | PhysicsDescFlags.MTable | (scriptTable ? PhysicsDescFlags.PeTable | PhysicsDescFlags.STable : PhysicsDescFlags.None);
            return msg with
            {
                Physics = msg.Physics with
                {
                    MotionTableId = AvatarRig.Table,
                    PhysicsEffectTableId = scriptTable ? AvatarRig.ScriptTable : null,
                    SoundTableId = scriptTable ? HumanSoundTable : null,
                    Flags = flags,
                    State = PhysicsState.Gravity | PhysicsState.ReportCollisions,
                },
                Weenie = msg.Weenie with { ItemType = 0x10 },
            };
        }
    }
}

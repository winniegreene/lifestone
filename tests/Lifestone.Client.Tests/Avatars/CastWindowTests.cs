using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using DatPlayScript = DatReaderWriter.Enums.PlayScript;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 2.1 s2b: the cast window of the avatar set and its counters <see cref="AvatarStats.CastScripts"/> and <see cref="AvatarStats.CastSounds"/>. The session adapter opens the window on what the bus shows of the player's cast
/// (ACE's HearSpeech of the words, chat type Spellcasting, or the first UpdateMotion gesture in the Magic stance) and closes it on the cast's UseDone, in order with the F755 and F750 events queued around them; the counters
/// count the scripts the table played and the voices that started (the sink's own count, <see cref="ISoundVoiceCounter"/>) inside it, on the player's body (and the cast target's when the host names one).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class CastWindowTests
{
    private const uint Npc = 0x80000200;
    private const uint Cell = AvatarRig.HoltburgCell;
    private const uint HumanSoundTable = 0x20000001;
    private const uint Spellcasting = 0x11;
    private const uint EnchantUp = 90;
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new System.Numerics.Vector3(96, 96, 0)));
    private static readonly ushort Magic = MotionCommandTable.ToWire(MotionCommands.Magic);
    private static readonly ushort Ready = MotionCommandTable.ToWire(MotionCommands.Ready);

    [Fact]
    public void The_players_words_open_the_window_and_the_use_done_closes_it_after_the_frames_effects_are_delivered()
    {
        var w = new World();
        w.Enter();
        Assert.False(w.Avatars.CastWindowOpen);
        w.Script(ReducerHarness.PlayerGuid); // before the words: not a cast's
        w.Avatars.Advance(0.05, Viewer, long.MaxValue); // its hook's voice is delivered here, outside any window
        Assert.Equal(1, w.Sink.Voices);
        w.Words();
        w.Script(ReducerHarness.PlayerGuid);
        w.Script(ReducerHarness.PlayerGuid);
        Assert.True(w.Avatars.CastWindowOpen);
        Assert.Equal(1, w.Avatars.Stats.CastWindows);
        Assert.Equal(2, w.Avatars.Stats.CastScripts);

        // The cast's last message queues a sound and then UseDone follows at once: the voice is still counted (the close waits for the frame's delivery).
        w.H.Reducer.OnSound(w.H.Context, new SoundMsg(ReducerHarness.PlayerGuid, EnchantUp, 1f));
        w.UseDone();
        Assert.True(w.Avatars.CastWindowOpen);
        w.Avatars.Advance(0.1, Viewer, long.MaxValue);
        Assert.False(w.Avatars.CastWindowOpen);
        Assert.Equal(3, w.Avatars.Stats.CastSounds); // the two scripts' SoundTable hooks and the server's F750 (the first script's voice, before the words, is not counted)
        Assert.Equal(1, w.Adapter.Stats.CastWindowsOpened);
        Assert.Equal(1, w.Adapter.Stats.CastWindowsClosed);

        // Outside a window nothing counts: a script and its sound after the close.
        w.Script(ReducerHarness.PlayerGuid);
        w.Avatars.Advance(0.2, Viewer, long.MaxValue);
        Assert.Equal(2, w.Avatars.Stats.CastScripts);
        Assert.Equal(3, w.Avatars.Stats.CastSounds);
        Assert.Equal(4, w.Avatars.Stats.ServerScriptsPlayed);
    }

    [Fact]
    public void The_first_cast_gesture_in_the_magic_stance_opens_the_window_and_the_stances_own_ready_does_not()
    {
        var w = new World();
        w.Enter();
        w.Motion(Ready); // the mode change to Magic: stance Magic, the Ready command
        w.Motion(null);
        Assert.False(w.Avatars.CastWindowOpen);

        w.Motion(0x0078); // a wind-up
        Assert.True(w.Avatars.CastWindowOpen);
        w.Motion(0x002D); // the talisman's gesture
        w.Script(ReducerHarness.PlayerGuid);
        w.Motion(Ready);
        w.UseDone();
        w.Avatars.Advance(0.1, Viewer, long.MaxValue);
        Assert.False(w.Avatars.CastWindowOpen);
        Assert.Equal(1, w.Avatars.Stats.CastWindows); // the second gesture did not open a second window
        Assert.Equal(1, w.Avatars.Stats.CastScripts);
    }

    [Fact]
    public void Other_peoples_speech_and_other_types_do_not_open_it_and_a_use_done_with_no_window_is_nothing()
    {
        var w = new World();
        w.Enter();
        w.Create(Npc);
        w.H.Reducer.OnSpeech(w.H.Context, new SpeechMsg("Zojak Wasq", "Guard", Npc, Spellcasting, null)); // another caster's words
        w.H.Reducer.OnSpeech(w.H.Context, new SpeechMsg("hello", "Tester", ReducerHarness.PlayerGuid, 0, null)); // the player's own chat
        w.Adapter.Drain();
        w.UseDone();
        Assert.False(w.Avatars.CastWindowOpen);
        Assert.Equal(0, w.Avatars.Stats.CastWindows);
        Assert.Equal(0, w.Adapter.Stats.CastWindowsClosed);
    }

    [Fact]
    public void Scripts_and_voices_of_another_object_count_only_when_the_host_names_it_the_cast_target()
    {
        var w = new World();
        w.Enter();
        w.Create(Npc);
        w.Words();
        w.Script(Npc);
        w.H.Reducer.OnSound(w.H.Context, new SoundMsg(Npc, EnchantUp, 1f));
        w.Adapter.Drain();
        w.Avatars.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(0, w.Avatars.Stats.CastScripts);
        Assert.Equal(0, w.Avatars.Stats.CastSounds);
        Assert.Equal(2, w.Sink.Voices); // the sink did hear both: they are just not the cast's

        w.Avatars.CastTarget = Npc;
        w.Script(Npc);
        w.H.Reducer.OnSound(w.H.Context, new SoundMsg(Npc, EnchantUp, 1f));
        w.Adapter.Drain();
        w.Avatars.Advance(0.2, Viewer, long.MaxValue);
        Assert.Equal(1, w.Avatars.Stats.CastScripts);
        Assert.Equal(2, w.Avatars.Stats.CastSounds); // the script's hook and the server's sound
    }

    [Fact]
    public void A_sink_that_counts_no_voices_leaves_the_sound_counter_at_zero_and_a_missing_script_is_not_a_cast_script()
    {
        var w = new World(countVoices: false);
        w.Enter();
        w.Words();
        w.Script(ReducerHarness.PlayerGuid);
        w.H.Reducer.OnPlayEffect(w.H.Context, new PlayEffectMsg(ReducerHarness.PlayerGuid, 0x7777, 1f)); // a type the table lacks plays nothing
        w.Adapter.Drain();
        w.Avatars.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(1, w.Avatars.Stats.CastScripts);
        Assert.Equal(1, w.Avatars.Stats.ServerScriptsMissing);
        Assert.Equal(0, w.Avatars.Stats.CastSounds);
        Assert.Equal(1, w.Sink.Events);
    }

    [Fact]
    public void Opening_and_closing_windows_and_counting_inside_them_allocates_nothing_once_warm()
    {
        var w = new World();
        w.Enter();
        for (var i = 0; i < 3; i++)
        {
            w.Avatars.OpenCastWindow();
            w.Avatars.PlayScript(ReducerHarness.PlayerGuid, (uint)DatPlayScript.LevelUp, 1f);
            w.Avatars.PlaySound(ReducerHarness.PlayerGuid, EnchantUp, 1f);
            w.Avatars.Advance(i / 30.0, Viewer, long.MaxValue);
            w.Avatars.CloseCastWindow();
            w.Avatars.Advance((i / 30.0) + 0.01, Viewer, long.MaxValue);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++)
        {
            w.Avatars.OpenCastWindow();
            w.Avatars.Advance(1.0 + (i / 30.0), Viewer, long.MaxValue);
            w.Avatars.CloseCastWindow();
            w.Avatars.Advance(1.0 + (i / 30.0) + 0.01, Viewer, long.MaxValue);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class CountingSink(bool countVoices) : IEffectEventSink, ISoundVoiceCounter
    {
        public int Events { get; private set; }

        public int Voices { get; private set; }

        public int VoicesStarted => countVoices ? Voices : 0;

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId)
        {
            Events++;
            if (effectEvent.Hook is SoundTableHook or ServerSoundTableHook)
            {
                Voices++;
            }
        }
    }

    private sealed class World
    {
        private ushort _movementSeq = 1;

        public World(bool countVoices = true)
        {
            H = new ReducerHarness();
            Avatars = new AvatarSet(AvatarRig.Sources());
            Sink = new CountingSink(countVoices);
            Avatars.EffectEvents = Sink;
            Adapter = new SessionAvatarAdapter(H.Bus, H.State, Avatars, () => 0.0);
        }

        public ReducerHarness H { get; }

        public AvatarSet Avatars { get; }

        public SessionAvatarAdapter Adapter { get; }

        public CountingSink Sink { get; }

        public void Enter()
        {
            H.Reducer.OnPlayerCreate(H.Context, new PlayerCreateMsg(ReducerHarness.PlayerGuid));
            H.Reducer.OnCreateObject(H.Context, Human(ReducerHarness.PlayerGuid, 5f), false);
            Adapter.Drain();
        }

        public void Create(uint guid)
        {
            H.Reducer.OnCreateObject(H.Context, Human(guid, 12f), false);
            Adapter.Drain();
        }

        public void Words()
        {
            H.Reducer.OnSpeech(H.Context, new SpeechMsg("Malar Zhavik", "Tester", ReducerHarness.PlayerGuid, Spellcasting, null));
            Adapter.Drain();
        }

        public void UseDone()
        {
            H.Reducer.OnUseDone(H.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 0, 0x01C7), new UseDoneEvent(0));
            Adapter.Drain();
        }

        public void Script(uint guid)
        {
            H.Reducer.OnPlayEffect(H.Context, new PlayEffectMsg(guid, (uint)DatPlayScript.LevelUp, 1f));
            Adapter.Drain();
        }

        /// <summary>One UpdateMotion of the player in the Magic stance (not autonomous, the player's own stamps plus one, as the stand-in sends it).</summary>
        public void Motion(ushort? forward)
        {
            _movementSeq++;
            H.Reducer.OnUpdateMotion(H.Context, new UpdateMotionMsg(ReducerHarness.PlayerGuid, 1, _movementSeq, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = Magic, Style = Magic, Forward = forward }));
            Adapter.Drain();
        }

        private static CreateObjectMsg Human(uint guid, float x)
        {
            var msg = ReducerHarness.Create(guid, Stamps(instance: 1), Position(Cell, x: x), name: "Guard", setup: AvatarRig.Human);
            return msg with
            {
                Physics = msg.Physics with
                {
                    MotionTableId = AvatarRig.Table,
                    PhysicsEffectTableId = AvatarRig.ScriptTable,
                    SoundTableId = HumanSoundTable,
                    Flags = msg.Physics.Flags | PhysicsDescFlags.MTable | PhysicsDescFlags.PeTable | PhysicsDescFlags.STable,
                    State = PhysicsState.Gravity | PhysicsState.ReportCollisions,
                },
                Weenie = msg.Weenie with { ItemType = 0x10 },
            };
        }
    }
}

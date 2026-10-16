using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Audio;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Audio;

/// <summary>Step PS10: the three sound hooks become sound calls; every other hook is ignored.</summary>
public sealed class SoundHookRouterTests
{
    [Fact]
    public void Each_sound_hook_goes_to_its_retail_entry_point()
    {
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);
        var origin = new SoundOrigin(7f, 30f);

        router.OnEffectEvent(Event(new SoundTweakedHook(AnimHookDirection.Both, 0x0A000316, 1f, 0.75f, 0.3f)), origin, 0x20000010);
        router.OnEffectEvent(Event(new SoundHook(AnimHookDirection.Forward, 0x0A000001)), origin, 0x20000010);
        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Backward, 0x1F)), origin, 0x20000010);

        Assert.Equal(
            [
                "wave 0x0A000316 1 0.75 0.3 7 30", // PlayWave with the hook's tweaks
                "wave 0x0A000001 0 1 1 7 30", // PlaySound (forwarded by the default member)
                "table 0x20000010 0x1F 7 1", // PlayFromTable at the entry's volume (forwarded)
            ],
            sink.Calls);
        Assert.Equal((3, 0), (router.Routed, router.Ignored));
        Assert.Equal((3, 0, 0), (router.HooksPlayed, router.Footsteps, router.ServerSounds));
    }

    [Fact]
    public void A_table_hook_plays_from_the_table_of_the_object_that_fired_it()
    {
        // SoundTableHook::Execute (11.6096 0x005276B0) hands the emitting object to the sound manager: one router, two objects, two tables.
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);
        var hook = Event(new SoundTableHook(AnimHookDirection.Both, 0x1F));

        router.OnEffectEvent(hook, new SoundOrigin(2f, 0f), 0x20000001);
        router.OnEffectEvent(hook, new SoundOrigin(3f, 0f), 0x20000002);
        router.OnEffectEvent(hook, new SoundOrigin(4f, 0f), 0);

        Assert.Equal(["table 0x20000001 0x1F 2 1", "table 0x20000002 0x1F 3 1"], sink.Calls);
        Assert.Equal((3, 2, 1), (router.Routed, router.HooksPlayed, router.NoTable));
    }

    [Fact]
    public void Footstep_hooks_are_counted_apart_from_other_hooks_and_from_the_servers_sounds()
    {
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);

        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Both, SoundHookRouter.Footstep1)), default, 0x20000001);
        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Both, SoundHookRouter.Footstep2)), default, 0x20000001);
        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Both, 0x1F)), default, 0x20000001);
        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Both, SoundHookRouter.Footstep1)), default, 0); // no table: nothing handed over, not counted

        Assert.Equal((3, 2, 0, 1), (router.HooksPlayed, router.Footsteps, router.ServerSounds, router.NoTable));
        Assert.Equal(3, sink.Calls.Count);
    }

    [Fact]
    public void The_servers_sound_plays_from_its_table_at_the_servers_volume_and_is_counted_apart()
    {
        // SmartBox::HandleSoundEvent (11.6096 0x00452000) -> CPhysicsObj::play_sound -> SoundManager::PlaySoundA(SoundType, obj, volume) (0x00551700).
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);

        router.OnEffectEvent(Event(new ServerSoundTableHook(0x20000001, 90, 0.75f)), new SoundOrigin(6f, 20f), 0x20000001);
        router.OnEffectEvent(Event(new ServerSoundTableHook(0, 90, 0.75f)), default, 0); // no table: retail plays nothing

        Assert.Equal(["table 0x20000001 0x5A 6 0.75"], sink.Calls);
        Assert.Equal((0, 0, 1, 1), (router.HooksPlayed, router.Footsteps, router.ServerSounds, router.NoTable));
    }

    [Fact]
    public void A_table_hook_on_an_object_without_a_table_is_silent()
    {
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);

        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Both, 0x1F)), default, 0);

        Assert.Empty(sink.Calls);
        Assert.Equal((1, 0), (router.Routed, router.HooksPlayed));
    }

    [Fact]
    public void Other_hooks_are_ignored()
    {
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);

        router.OnEffectEvent(Event(new DefaultScriptHook(AnimHookDirection.Both)), default, 0);
        router.OnEffectEvent(Event(new EtherealHook(AnimHookDirection.Both, true)), default, 0);

        Assert.Empty(sink.Calls);
        Assert.Equal((0, 2), (router.Routed, router.Ignored));
        Assert.False(SoundHookRouter.Route(Event(new DefaultScriptHook(AnimHookDirection.Both)), default, sink, 0x20000010));
    }

    [Fact]
    public void The_router_plays_through_the_retail_player()
    {
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, RetailSoundPlayerTests.Source(), new ScriptedDice(), new ScriptedCrt());

        Assert.True(SoundHookRouter.Route(Event(new SoundTableHook(AnimHookDirection.Both, 0x6B)), new SoundOrigin(1f, 0f), player, 0x2000004B));
        Assert.True(SoundHookRouter.Route(Event(new SoundHook(AnimHookDirection.Both, 0x0A000246)), new SoundOrigin(1f, 0f), player, 0));

        // The table entry's own volume (0.3 in the fake table), and the plain hook at the slider's volume.
        Assert.Equal([new VoicePlay(0, 0x0A000245, 0, -10, AudioBus.Effects), new VoicePlay(1, 0x0A000246, 0, 0, AudioBus.Effects)], output.Plays);
    }

    [Fact]
    public void The_players_footsteps_are_counted_apart_from_every_objects_and_handed_apart_from_sounded()
    {
        // The human table 0x20000001 has no variant for footstep type 55 (retail uses the object's own table only); table 0x20000010 has one.
        var output = new RecordingAudioOutput();
        var source = RetailSoundPlayerTests.Source().AddWave(0x0A000010)
            .AddTable(0x20000001, (0x1Fu, [new SoundEntry(0x0A000010, 0.9f, 1f, 1f)]))
            .AddTable(0x20000010, (SoundHookRouter.Footstep1, [new SoundEntry(0x0A000010, 0.9f, 1f, 1f)]));
        var router = new SoundHookRouter(new RetailSoundPlayer(output, source, new ScriptedDice(), new ScriptedCrt()));
        var step = Event(new SoundTableHook(AnimHookDirection.Both, SoundHookRouter.Footstep1));
        Assert.True(router.CountsSounded);

        router.OnEffectEvent(step, default, 0x20000001, fromPlayer: true);  // the player, human table: handed, no wave, silent
        router.OnEffectEvent(step, default, 0x20000010, fromPlayer: false); // an NPC with a footstep wave: handed and sounded
        router.OnEffectEvent(step, default, 0x20000010);                    // the three-argument form is not the player

        Assert.Equal((3, 3, 1), (router.HooksPlayed, router.Footsteps, router.PlayerFootsteps));
        Assert.Equal((2, 0), (router.HooksSounded, router.PlayerFootstepsSounded));
        Assert.Equal(2, output.Plays.Count);

        router.OnEffectEvent(step, default, 0x20000010, fromPlayer: true); // the player with a table that has the sound: sounded
        Assert.Equal((4, 2, 3, 1), (router.Footsteps, router.PlayerFootsteps, router.HooksSounded, router.PlayerFootstepsSounded));
    }

    [Fact]
    public void A_server_sound_is_sounded_only_when_it_starts_a_voice_and_a_plain_sink_cannot_say()
    {
        var output = new RecordingAudioOutput();
        var source = RetailSoundPlayerTests.Source().AddWave(0x0A000010).AddTable(0x20000010, (90u, [new SoundEntry(0x0A000010, 0.9f, 1f, 1f)]));
        var router = new SoundHookRouter(new RetailSoundPlayer(output, source, new ScriptedDice(), new ScriptedCrt()));

        router.OnEffectEvent(Event(new ServerSoundTableHook(0x20000010, 90, 1f)), default, 0);
        router.OnEffectEvent(Event(new ServerSoundTableHook(0x20000010, 91, 1f)), default, 0); // no variant of type 91: handed, silent

        Assert.Equal((2, 1), (router.ServerSounds, router.ServerSounded));

        var plain = new SoundHookRouter(new MinimalSoundSink());
        plain.OnEffectEvent(Event(new SoundHook(AnimHookDirection.Both, 0x0A000001)), default, 0);
        Assert.False(plain.CountsSounded);
        Assert.Equal((1, 0), (plain.HooksPlayed, plain.HooksSounded));
    }

    [Fact]
    public void A_tweaked_hook_sounds_by_its_own_probability_and_falls_silent_by_distance_as_retail()
    {
        // SoundManager::PlaySoundA(gid, obj, prio, prob, vol) (11.6096 0x005513B0) rolls the hook's own probability, then GetAttenuation (11.6096 0x00550C30): beyond 5 m
        // gain = 25 * volume / d^2, and under -50 dB (gain < 0.00316) nothing plays: at volume 1 that is past about 94 m (the dB is rounded up), at volume 0.01 past about 9.4 m.
        var output = new RecordingAudioOutput();
        var source = RetailSoundPlayerTests.Source().AddWave(0x0A000010);
        var router = new SoundHookRouter(new RetailSoundPlayer(output, source, new ScriptedDice(), new ScriptedCrt()));

        router.OnEffectEvent(Event(new SoundTweakedHook(AnimHookDirection.Both, 0x0A000010, 0f, 1f, 1f)), new SoundOrigin(93f, 0f), 0);   // near enough: sounds
        router.OnEffectEvent(Event(new SoundTweakedHook(AnimHookDirection.Both, 0x0A000010, 0f, 1f, 1f)), new SoundOrigin(96f, 0f), 0);   // too far at volume 1
        router.OnEffectEvent(Event(new SoundTweakedHook(AnimHookDirection.Both, 0x0A000010, 0f, 1f, 0.01f)), new SoundOrigin(10f, 0f), 0); // too far at volume 0.01
        router.OnEffectEvent(Event(new SoundTweakedHook(AnimHookDirection.Both, 0x0A000010, 1f, 0f, 1f)), new SoundOrigin(1f, 0f), 0);    // probability 0: the roll refuses

        Assert.Equal((4, 1), (router.HooksPlayed, router.HooksSounded));
        var d = router.Diagnostics;
        Assert.Equal((1, 2, 1), (d.Count(2, 0), d.Count(2, 2), d.Count(2, 1))); // sounded, attenuation, roll
    }

    private static EffectEvent Event(AnimHook hook) => new(1.5, hook);
}

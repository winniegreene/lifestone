using Lifestone.Client.Audio;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Audio;

/// <summary>Step PS10: the three sound hooks become sound calls; every other hook is ignored.</summary>
public sealed class SoundHookRouterTests
{
    [Fact]
    public void Each_sound_hook_goes_to_its_retail_entry_point()
    {
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink) { SoundTableId = 0x20000010 };
        var origin = new SoundOrigin(7f, 30f);

        router.OnEffectEvent(Event(new SoundTweakedHook(AnimHookDirection.Both, 0x0A000316, 1f, 0.75f, 0.3f)), origin);
        router.OnEffectEvent(Event(new SoundHook(AnimHookDirection.Forward, 0x0A000001)), origin);
        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Backward, 0x1F)), origin);

        Assert.Equal(
            [
                "wave 0x0A000316 1 0.75 0.3 7 30", // PlayWave with the hook's tweaks
                "wave 0x0A000001 0 1 1 7 30", // PlaySound (forwarded by the default member)
                "table 0x20000010 0x1F 7 1", // PlayFromTable at the entry's volume (forwarded)
            ],
            sink.Calls);
        Assert.Equal((3, 0), (router.Routed, router.Ignored));
    }

    [Fact]
    public void A_table_hook_on_an_object_without_a_table_is_silent()
    {
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);

        router.OnEffectEvent(Event(new SoundTableHook(AnimHookDirection.Both, 0x1F)), default);

        Assert.Empty(sink.Calls);
        Assert.Equal(1, router.Routed);
    }

    [Fact]
    public void Other_hooks_are_ignored()
    {
        var sink = new MinimalSoundSink();
        var router = new SoundHookRouter(sink);

        router.OnEffectEvent(Event(new DefaultScriptHook(AnimHookDirection.Both)), default);
        router.OnEffectEvent(Event(new EtherealHook(AnimHookDirection.Both, true)), default);

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

    private static EffectEvent Event(AnimHook hook) => new(1.5, hook);
}

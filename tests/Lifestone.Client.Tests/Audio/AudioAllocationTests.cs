using DatReaderWriter.Enums;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Audio;

/// <summary>
/// Step PS10: a steady sound allocates nothing (the rules, the voice pool, the player and the hook router), measured on
/// this thread in the non-parallel <see cref="AllocationMeasurementCollection"/>; a window with a GC inside is not judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class AudioAllocationTests
{
    [Fact]
    public void Steady_ui_and_hook_sounds_allocate_nothing()
    {
        const uint uiTable = 0x2000004B;
        const uint enterWave = 0x0A000246;
        var output = new CountingAudioOutput();
        var player = new RetailSoundPlayer(output, RetailSoundPlayerTests.Source(), new RetailRandom(3), new MsvcCrtRandom(3));
        var router = new SoundHookRouter(player);
        var hum = new EffectEvent(0.0, new SoundTweakedHook(AnimHookDirection.Both, enterWave, 1f, 1f, 0.3f));
        var whoosh = new EffectEvent(0.0, new SoundTableHook(AnimHookDirection.Both, (uint)Sound.UI_EnterPortal));
        var server = new EffectEvent(0.0, new ServerSoundTableHook(uiTable, (uint)Sound.UI_EnterPortal, 0.75f));
        var origin = new SoundOrigin(10f, 45f);

        void Frame()
        {
            player.PlayFromCenter(uiTable, (uint)Sound.UI_EnterPortal);
            router.OnEffectEvent(hum, origin, uiTable);
            router.OnEffectEvent(whoosh, default, uiTable);
            router.OnEffectEvent(server, origin, uiTable);
        }

        Frame(); // warm up (decoding, JIT)

        var allocated = 0L;
        var gcFrames = 0;
        for (var frame = 0; frame < 2000; frame++)
        {
            var window = AllocationWindow.Open();
            Frame();
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allocated += bytes;
        }

        Assert.Equal(4 * 2001, output.Plays);
        Assert.Equal((2 * 2001, 2001), (router.HooksPlayed, router.ServerSounds));
        Assert.True(gcFrames <= 20, $"{gcFrames} of 2000 frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"steady sounds allocated {allocated} B over 2000 frames");
    }
}

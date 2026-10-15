using DatReaderWriter.Enums;
using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Audio;
using Lifestone.Data.Effects;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Audio;

/// <summary>Step PS10: the retail sound manager over a recording output, with fakes and on the real dats.</summary>
public sealed class RetailSoundPlayerTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint UiTable = 0x2000004B;
    private const uint EnterWave = 0x0A000246;
    private const uint ExitWave = 0x0A000245;
    private const uint Hum = 0x0A000316;
    private const uint EnterPortal = (uint)Sound.UI_EnterPortal;
    private const uint ExitPortal = (uint)Sound.UI_ExitPortal;

    [Fact]
    public void A_ui_sound_plays_its_entry_centred_on_the_ui_bus_at_the_entrys_volume()
    {
        var (player, output, _, _) = Rig();

        player.PlayFromCenter(UiTable, EnterPortal);
        player.PlayFromCenter(UiTable, ExitPortal);

        Assert.Equal(
            [new VoicePlay(0, EnterWave, 0, 0, AudioBus.Ui), new VoicePlay(1, ExitWave, 0, -10, AudioBus.Ui)],
            output.Plays);
        Assert.Equal((2, 2, 0, 0, 0, 0), (player.Requested, player.Played, player.Silent, player.Missing, player.Disabled, player.Dropped));
    }

    [Fact]
    public void A_table_sound_rolls_the_variant_then_the_probability_once_each()
    {
        var source = Source().AddWave(0x0A000010).AddWave(0x0A000011).AddWave(0x0A000012)
            .AddTable(0x20000010, ((uint)Sound.Swoosh2, [new(0x0A000010, 0.9f, 1f, 1f), new(0x0A000011, 0.9f, 1f, 1f), new(0x0A000012, 0.9f, 1f, 1f)]));
        var dice = new ScriptedDice(0.0, 0.6, 0.99999988);
        var crt = new ScriptedCrt(0, 0, 0);
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, source, dice, crt);

        for (var i = 0; i < 3; i++)
        {
            player.PlayFromTable(0x20000010, (uint)Sound.Swoosh2, new SoundOrigin(0f, 0f));
        }

        // ⌊2 · roll⌋: 0, 1 and 1. The third variant never plays.
        Assert.Equal([0x0A000010u, 0x0A000011u, 0x0A000011u], output.Plays.Select(p => p.WaveId));
        Assert.All(output.Plays, p => Assert.Equal(AudioBus.Effects, p.Bus));
        Assert.Equal((3, 3), (dice.Draws, crt.Draws));
    }

    [Fact]
    public void The_probability_roll_keeps_a_sound_silent()
    {
        var source = Source().AddTable(0x20000010, (0x1Fu, [new SoundEntry(EnterWave, 0.9f, 0.5f, 1f)]));
        var crt = new ScriptedCrt(16384, 16383);
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, source, new ScriptedDice(), crt);

        player.PlayFromTable(0x20000010, 0x1F, default);
        player.PlayFromTable(0x20000010, 0x1F, default);

        Assert.Single(output.Plays);
        Assert.Equal((1, 1), (player.Silent, player.Played));
    }

    [Fact]
    public void Only_the_table_and_tweaked_calls_roll_and_only_table_calls_pick_a_variant()
    {
        var (player, _, dice, crt) = Rig();

        player.PlayWaveFromCenter(EnterWave, 1f); // no roll at all
        player.PlaySound(EnterWave, default); // no roll at all
        Assert.Equal((0, 0), (dice.Draws, crt.Draws));

        player.PlayWave(EnterWave, 1f, 1f, 1f, default); // the probability only
        Assert.Equal((0, 1), (dice.Draws, crt.Draws));

        player.PlayFromCenter(UiTable, EnterPortal); // the variant, then the probability
        player.PlayFromTable(UiTable, EnterPortal, default, 1f);
        Assert.Equal((2, 3), (dice.Draws, crt.Draws));

        player.PlayFromCenter(UiTable, 0x1F); // no such type: nothing is drawn
        Assert.Equal((2, 3), (dice.Draws, crt.Draws));
    }

    [Fact]
    public void Interface_sounds_follow_the_effects_slider()
    {
        var (player, output, _, _) = Rig();
        player.Levels = AudioLevels.Default with { EffectVolume = 0.5f };

        player.PlayFromCenter(UiTable, EnterPortal);
        player.PlayWaveFromCenter(EnterWave, 0.6f); // 0.3: −10.46

        Assert.Equal([-6, -10], output.Plays.Select(p => p.AttenuationDb));
    }

    [Fact]
    public void A_sound_hook_is_scaled_by_the_slider_twice_as_in_retail()
    {
        var (player, output, _, _) = Rig();
        player.Levels = AudioLevels.Default with { EffectVolume = 0.5f };

        player.PlaySound(EnterWave, default); // volume 0.5, then × 0.5: 0.25
        player.PlayWave(EnterWave, 0.9f, 1f, 1f, default); // volume 1 × 0.5

        Assert.Equal([-12, -6], output.Plays.Select(p => p.AttenuationDb));
    }

    [Fact]
    public void A_positioned_sound_attenuates_and_pans_from_5_m()
    {
        var (player, output, _, _) = Rig();

        player.PlayWave(EnterWave, 0.9f, 1f, 1f, new SoundOrigin(10f, 90f));
        player.PlayWave(EnterWave, 0.9f, 1f, 1f, new SoundOrigin(4f, 90f));
        player.PlayWave(EnterWave, 0.9f, 1f, 1f, new SoundOrigin(100f, 0f)); // −52 dB: silent

        Assert.Equal([(-14, -12), (0, 0)], output.Plays.Select(p => (p.Pan, p.AttenuationDb)));
        Assert.Equal(1, player.Silent);

        player.Levels = AudioLevels.Default with { Mono = true };
        player.PlayWave(EnterWave, 0.9f, 1f, 1f, new SoundOrigin(10f, 90f));
        Assert.Equal(0, output.Plays[^1].Pan);
    }

    [Fact]
    public void Disabled_categories_and_an_inactive_window_play_nothing()
    {
        var (player, output, _, crt) = Rig();

        player.Levels = AudioLevels.Default with { InterfaceEnabled = false };
        player.PlayFromCenter(UiTable, EnterPortal);
        player.PlayWaveFromCenter(EnterWave, 1f);
        player.PlayWave(EnterWave, 0.9f, 1f, 1f, default); // effects still on
        Assert.Single(output.Plays);

        player.Levels = AudioLevels.Default with { EffectsEnabled = false };
        player.PlayWave(EnterWave, 0.9f, 1f, 1f, default);
        player.PlaySound(EnterWave, default);
        player.PlayFromTable(UiTable, EnterPortal, default);
        player.PlayFromCenter(UiTable, EnterPortal); // interface on again
        Assert.Equal(2, output.Plays.Count);

        player.Levels = AudioLevels.Default with { PlayOnlyWhenActive = true };
        player.ApplicationActive = false;
        player.PlayFromCenter(UiTable, EnterPortal);
        Assert.Equal(2, output.Plays.Count);
        player.ApplicationActive = true;
        player.PlayFromCenter(UiTable, EnterPortal);
        Assert.Equal(3, output.Plays.Count);

        Assert.Equal(6, player.Disabled);
        Assert.Equal(3, crt.Draws); // a refused call draws nothing
    }

    [Fact]
    public void A_missing_table_type_or_wave_and_an_unsupported_format_warn_once_each()
    {
        var log = new RecordingLogSink();
        var source = Source().AddWave(0x0A000099, formatTag: 2);
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, source, new ScriptedDice(), new ScriptedCrt(), log: log);

        for (var i = 0; i < 3; i++)
        {
            player.PlayFromCenter(0x20000099, EnterPortal); // no such table
            player.PlayFromCenter(UiTable, 0x1F); // no such type
            player.PlayWave(0x0A000098, 0.9f, 1f, 1f, default); // no such wave
            player.PlayWaveFromCenter(0x0A000099, 1f); // ADPCM
        }

        Assert.Empty(output.Plays);
        Assert.Equal(12, player.Missing);
        Assert.Equal(4, log.Entries.Count);
        Assert.All(log.Entries, e => Assert.Equal((LogSeverity.Warning, RetailSoundPlayer.LogCategory), (e.Severity, e.Category)));
        Assert.Contains(log.Lines, l => l.Contains("0x20000099 cannot play: the sound table does not exist", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("sound type 0x1F of sound table 0x2000004B", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("0x0A000098 cannot play: the wave does not exist", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("TODO(audio): wave 0x0A000099 has format tag 0x0002", StringComparison.Ordinal));
    }

    [Fact]
    public void A_malformed_table_or_wave_warns_once_and_is_not_asked_for_again()
    {
        var log = new RecordingLogSink();
        var source = Source();
        source.Malformed.Add(0x2000009A);
        source.Malformed.Add(0x0A00009A);
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, source, new ScriptedDice(), new ScriptedCrt(), log: log);

        for (var i = 0; i < 3; i++)
        {
            player.PlayFromCenter(0x2000009A, EnterPortal);
            player.PlayWaveFromCenter(0x0A00009A, 1f);
        }

        Assert.Empty(output.Plays);
        Assert.Equal(6, player.Missing);
        Assert.Equal(1, source.TableReads);
        Assert.Equal(1, source.WaveReads);
        Assert.Equal(2, log.Entries.Count);
        Assert.Contains(log.Lines, l => l.Contains("0x2000009A cannot play: SoundTable 0x2000009A is malformed (test).", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("0x0A00009A cannot play: Wave 0x0A00009A is malformed (test).", StringComparison.Ordinal));
    }

    [Fact]
    public void Seventeen_sounds_reuse_voice_0_and_stop_all_stops_the_voices()
    {
        var (player, output, _, _) = Rig();

        for (var i = 0; i < 17; i++)
        {
            player.PlayWaveFromCenter(EnterWave, 1f);
        }

        Assert.Equal([.. Enumerable.Range(0, 16), 0], output.Plays.Select(p => p.Voice));
        Assert.Equal(0, player.Dropped);

        player.StopAll();
        Assert.Equal(16, output.Stops.Count);
        Assert.Equal(0, player.Voices.Cursor);
    }

    [Fact]
    public void Default_members_forward_for_a_sink_that_implements_only_the_contract()
    {
        ISoundSink sink = new MinimalSoundSink();

        sink.PlayWaveFromCenter(EnterWave, 0.5f);
        sink.PlaySound(EnterWave, new SoundOrigin(7f, 30f));
        sink.PlayFromTable(UiTable, EnterPortal, new SoundOrigin(2f, 0f));

        Assert.Equal(
            ["wave 0x0A000246 0 1 0.5 0 0", "wave 0x0A000246 0 1 1 7 30", "table 0x2000004B 0x6A 2 1"],
            ((MinimalSoundSink)sink).Calls);
    }

    [RequiresDatsFact]
    public void On_the_real_dats_the_enter_whoosh_plays_wave_0x0A000246_at_full_volume()
    {
        var dats = fixture.Dats;
        var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, new DatAudioSource(dats), new RetailRandom(1), new MsvcCrtRandom());

        player.PlayFromCenter(assets.UiSoundTableId, EnterPortal);
        player.PlayFromCenter(assets.UiSoundTableId, ExitPortal);

        Assert.Equal(
            [new VoicePlay(0, EnterWave, 0, 0, AudioBus.Ui), new VoicePlay(1, ExitWave, 0, 0, AudioBus.Ui)],
            output.Plays);
    }

    [RequiresDatsFact]
    public void On_the_real_dats_the_tunnel_hum_plays_centred_at_minus_10_db()
    {
        var dats = fixture.Dats;
        var tunnel = TunnelObject.Create(dats, PortalSpaceAssets.Resolve(dats.EnumMaps));
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, new DatAudioSource(dats), new RetailRandom(1), new MsvcCrtRandom());
        var router = new SoundHookRouter(player);
        var events = new List<EffectEvent>();

        tunnel.Restart();
        for (var k = 1; k <= 330; k++)
        {
            tunnel.AdvanceTo(k / 60.0);
            events.Clear();
            tunnel.DrainEvents(events);
            foreach (var e in events)
            {
                router.OnEffectEvent(e, PortalSpaceScene.TunnelHumOrigin);
            }
        }

        // Two hums in 5.5 s (frame 2 is crossed at 0.05 s and 3.025 s, heard on the spin's next step), each from the tunnel camera
        // 2.85 m away (under 5 m: no pan, no falloff), at the hook's volume 0.3.
        Assert.Equal(2, router.Routed);
        Assert.Equal(
            [new VoicePlay(0, Hum, 0, -10, AudioBus.Effects), new VoicePlay(1, Hum, 0, -10, AudioBus.Effects)],
            output.Plays);
    }

    internal static FakeAudioSource Source() => new FakeAudioSource()
        .AddWave(EnterWave)
        .AddWave(ExitWave)
        .AddTable(UiTable, (EnterPortal, [new SoundEntry(EnterWave, 0.9f, 1f, 1f)]), (ExitPortal, [new SoundEntry(ExitWave, 0.9f, 1f, 0.3f)]));

    private static (RetailSoundPlayer Player, RecordingAudioOutput Output, ScriptedDice Dice, ScriptedCrt Crt) Rig()
    {
        var output = new RecordingAudioOutput();
        var dice = new ScriptedDice();
        var crt = new ScriptedCrt();
        return (new RetailSoundPlayer(output, Source(), dice, crt), output, dice, crt);
    }
}

using System.Buffers.Binary;
using Lifestone.Client.Audio;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Audio;

/// <summary>The wave reader of the record-audio check (M3 1.3 s3), on synthetic waves, and the per-source sound fields.</summary>
public sealed class WaveLevelsTests
{
    private const int Rate = 44100;

    /// <summary>A PCM wave file of 16-bit samples, interleaved, with Godot's 44-byte header.</summary>
    private static byte[] Wav16(short[] interleaved, int channels = 2, int rate = Rate, bool wrongDataSize = false)
    {
        var bytes = new byte[44 + (interleaved.Length * 2)];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)rate);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)(rate * channels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), (ushort)(channels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), wrongDataSize ? 0x7FFFFFF0u : (uint)(interleaved.Length * 2));
        for (var i = 0; i < interleaved.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + (i * 2)), interleaved[i]);
        }

        return bytes;
    }

    /// <summary>Stereo samples: a sine of the given amplitude (0..1) from <paramref name="from"/> to <paramref name="to"/> seconds, else zero.</summary>
    private static short[] Stereo(double seconds, double amplitude, double from = 0.0, double to = double.MaxValue, double hz = 440.0)
    {
        var frames = (int)(seconds * Rate);
        var samples = new short[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            var t = f / (double)Rate;
            var v = t >= from && t < to ? (short)Math.Round(Math.Sin(2 * Math.PI * hz * t) * amplitude * 32767.0) : (short)0;
            samples[f * 2] = v;
            samples[(f * 2) + 1] = v;
        }

        return samples;
    }

    [Fact]
    public void Silence_has_the_floor_level_no_active_window_and_is_not_non_silent()
    {
        var levels = WaveLevels.Analyze(Wav16(Stereo(1.0, 0.0)));
        Assert.Equal(1.0, levels.Seconds, 3);
        Assert.Equal(WaveLevels.SilenceDbfs, levels.PeakDbfs);
        Assert.Equal(WaveLevels.SilenceDbfs, levels.RmsDbfs);
        Assert.Equal(0, levels.ActiveWindows);
        Assert.Equal(20, levels.Windows);
        Assert.False(levels.NonSilent);
        Assert.Equal(0.0, levels.ActiveShare);
    }

    [Fact]
    public void A_full_scale_tone_reads_its_peak_and_rms_and_every_window_is_active()
    {
        // A sine at 0.5 of full scale: peak -6.02 dBFS, RMS -9.03 dBFS.
        var levels = WaveLevels.Analyze(Wav16(Stereo(2.0, 0.5)));
        Assert.Equal(2.0, levels.Seconds, 3);
        Assert.Equal(-6.02, levels.PeakDbfs, 1);
        Assert.Equal(-9.03, levels.RmsDbfs, 1);
        Assert.Equal(levels.Windows, levels.ActiveWindows);
        Assert.Equal(40, levels.Windows);
        Assert.Equal(1.0, levels.ActiveShare, 6);
        Assert.True(levels.NonSilent);
        Assert.Equal(Rate, levels.SampleRate);
        Assert.Equal(2, levels.Channels);
    }

    [Fact]
    public void A_short_blip_in_silence_has_few_active_windows_and_is_non_silent()
    {
        // 0.12 s of tone from 0.5 s in a 2 s recording: windows 10 to 12 (50 ms each) hold it, window 12 only partly (the blip ends at 0.62 s).
        var levels = WaveLevels.Analyze(Wav16(Stereo(2.0, 0.8, from: 0.5, to: 0.62)));
        Assert.Equal(-1.94, levels.PeakDbfs, 1);
        Assert.InRange(levels.ActiveWindows, 2, 4);
        Assert.Equal(40, levels.Windows);
        Assert.True(levels.RmsDbfs < -14.0 && levels.RmsDbfs > -20.0, $"rms {levels.RmsDbfs}");
        Assert.True(levels.NonSilent);
        Assert.InRange(levels.ActiveShare, 0.04, 0.11);
    }

    [Fact]
    public void A_whisper_under_the_floor_has_no_active_window_and_a_floor_can_be_given()
    {
        // Amplitude 0.0005 is -66 dBFS: under the default -60 floor, above a -80 floor.
        var wav = Wav16(Stereo(1.0, 0.0005));
        var quiet = WaveLevels.Analyze(wav);
        Assert.Equal(0, quiet.ActiveWindows);
        Assert.False(quiet.NonSilent);
        var heard = WaveLevels.Analyze(wav, floorDbfs: -80.0);
        Assert.Equal(heard.Windows, heard.ActiveWindows);
    }

    [Fact]
    public void A_mono_wave_and_a_partial_last_window_are_read()
    {
        var mono = new short[(int)(0.075 * Rate)];
        Array.Fill(mono, (short)16384);
        var levels = WaveLevels.Analyze(Wav16(mono, channels: 1));
        Assert.Equal(2, levels.Windows); // 50 ms and the 25 ms rest
        Assert.Equal(2, levels.ActiveWindows);
        Assert.Equal(-6.02, levels.PeakDbfs, 1);
        Assert.Equal(-6.02, levels.RmsDbfs, 1); // a constant 0.5
    }

    [Fact]
    public void A_data_size_larger_than_the_file_is_clamped_and_an_empty_wave_has_no_windows()
    {
        var levels = WaveLevels.Analyze(Wav16(Stereo(0.5, 0.5), wrongDataSize: true));
        Assert.Equal(0.5, levels.Seconds, 3);
        var empty = WaveLevels.Analyze(Wav16([]));
        Assert.Equal(0, empty.Windows);
        Assert.Equal(0.0, empty.Seconds);
        Assert.False(empty.NonSilent);
        Assert.Equal(0.0, empty.ActiveShare);
    }

    [Fact]
    public void Not_a_wave_or_an_unsupported_layout_is_an_invalid_data_exception()
    {
        Assert.Throws<InvalidDataException>(() => WaveLevels.Analyze("not a wave file at all"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => WaveLevels.Analyze("RIFF\0\0\0\0WAVE"u8.ToArray())); // no fmt chunk
        var adpcm = Wav16(Stereo(0.1, 0.5));
        BinaryPrimitives.WriteUInt16LittleEndian(adpcm.AsSpan(20), 0x11); // IMA ADPCM
        var ex = Assert.Throws<InvalidDataException>(() => WaveLevels.Analyze(adpcm));
        Assert.Contains("format 17", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_levels_line_names_every_field_and_the_sources_token_has_no_spaces()
    {
        var levels = WaveLevels.Analyze(Wav16(Stereo(1.0, 0.5)));
        var sources = new SoundSourceCounts(Server: 1, Hooks: 199, Footsteps: 3, Ui: 0, Ambient: null).Describe();
        Assert.Equal("server:1,hooks:199,footsteps:3,footsteps_sounded:n/a,ui:0,ambient:n/a,server_handed:n/a,hooks_handed:n/a", sources);
        var line = WaveLevels.LevelsLine("cache/x/record-audio.wav", levels, sources);
        Assert.Equal(
            "audio recording levels file=cache/x/record-audio.wav seconds=1.00 peak_dbfs=-6.0 rms_dbfs=-9.0 active_windows=20 sources=server:1,hooks:199,footsteps:3,footsteps_sounded:n/a,ui:0,ambient:n/a,server_handed:n/a,hooks_handed:n/a windows=20 clipped_samples=0",
            line);
        Assert.EndsWith(" muted=true", WaveLevels.LevelsLine("a.wav", levels, sources, muted: true), StringComparison.Ordinal);
        Assert.EndsWith(" muted=false", WaveLevels.LevelsLine("a.wav", levels, sources, muted: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Samples_at_full_scale_are_counted_across_channels_and_the_line_prints_them()
    {
        // 100 frames of stereo: 3 frames hold 32767 on the left and -32768 on the right, one holds 32766 (not clipped), the rest are quiet.
        var interleaved = new short[200];
        for (var f = 0; f < 3; f++)
        {
            interleaved[f * 2] = 32767;
            interleaved[(f * 2) + 1] = -32768;
        }

        interleaved[6] = 32766;
        var levels = WaveLevels.Analyze(Wav16(interleaved));

        Assert.Equal(6, levels.ClippedSamples);
        Assert.Contains(" clipped_samples=6", WaveLevels.LevelsLine("a.wav", levels, "server:n/a"), StringComparison.Ordinal);
        Assert.Equal(0, WaveLevels.Analyze(Wav16(Stereo(1.0, 0.0))).ClippedSamples);
    }

    [Fact]
    public void A_source_that_does_not_exist_prints_na_and_never_zero()
    {
        Assert.Equal(" sounds_server=1 sounds_hooks=199 sounds_ui=0 ambient=n/a sounds_server_handed=n/a sounds_hooks_handed=n/a", new SoundSourceCounts(1, 199, 3, 0, null).LoginCheckFields);
        Assert.Equal(" sounds_server=n/a sounds_hooks=n/a sounds_ui=n/a ambient=n/a sounds_server_handed=n/a sounds_hooks_handed=n/a", default(SoundSourceCounts).LoginCheckFields);
        Assert.Equal("n/a", SoundSourceCounts.Value(null));
        Assert.Equal("0", SoundSourceCounts.Value(0));
        // The login check's verdict carries them through its scenario helper.
        Assert.Equal(new SoundSourceCounts(2, 5, null, 7, 11).LoginCheckFields, LoginCheckScenario.SoundFields(new SoundSourceCounts(2, 5, null, 7, 11)));
        Assert.Equal(" sounds_server=2 sounds_hooks=5 sounds_ui=7 ambient=11 sounds_server_handed=n/a sounds_hooks_handed=n/a", LoginCheckScenario.SoundFields(new SoundSourceCounts(2, 5, null, 7, 11)));
    }

    [Fact]
    public void The_per_leg_tally_reads_each_leg_not_the_run_total_and_ignores_what_played_before_the_enter()
    {
        // M3 1.3 fix B (R2): DoD 13 judges sounds_server in the Academy arrival and ambient in the Holtburg laps. Ambient heard before login (frame 1-2) or in the
        // arrival must not pass the laps; the counter of a new scene's player starts at zero again (a reset adds its new value).
        var tally = new SoundLegTally();
        tally.Step(null, new SoundSourceCounts(0, 0, 0, 0, 4, 0, 0, 0));       // the character list: an ambient draw already played, no leg yet
        tally.Step(null, new SoundSourceCounts(null, null, null, 0, 6));
        tally.Step(SoundLegNames.Arrival, new SoundSourceCounts(1, 10, 2, 3, 7, 1, 12, 0)); // arrival: ambient 6 -> 7, router created now (0 -> 1, 10)
        tally.Step(SoundLegNames.Arrival, new SoundSourceCounts(2, 12, 3, 3, 7, 2, 15, 0));
        tally.Step(SoundLegNames.Laps, new SoundSourceCounts(2, 20, 5, 4, 9, 2, 23, 1));  // the laps: ambient 7 -> 9
        tally.Step(SoundLegNames.Laps, new SoundSourceCounts(2, 25, 6, 4, 2, 2, 29, 1));  // a new ambient player starts again at 2

        var arrival = tally.Of(SoundLegNames.Arrival);
        Assert.Equal((2, 1, 3), (arrival.Server, arrival.Ambient, arrival.Ui)); // only the growth inside the leg (6 -> 7), not the 6 heard before it
        var laps = tally.Of(SoundLegNames.Laps);
        Assert.Equal(2 + 2, laps.Ambient); // 7 -> 9, then the reset adds 2
        Assert.Equal(" sounds_server=2 sounds_hooks=25 sounds_ui=4 ambient=4 sounds_server_handed=2 sounds_hooks_handed=29", LoginCheckScenario.SoundFields(tally.Report(SoundLegNames.Arrival, SoundLegNames.Laps)));
        Assert.Null(tally.Of("never-ran").Ambient);
        Assert.Contains("ambient=n/a", LoginCheckScenario.SoundFields(tally.Report(SoundLegNames.Arrival, "never-ran")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_source_that_is_absent_for_a_frame_resumes_without_counting_its_total_again()
    {
        var tally = new SoundLegTally();
        tally.Step("leg", new SoundSourceCounts(null, null, null, null, 5));
        tally.Step("leg", default); // the ambient node left the tree for a frame
        tally.Step("leg", new SoundSourceCounts(null, null, null, null, 6));

        Assert.Equal(6, tally.Of("leg").Ambient); // 0 -> 5 on the first frame (new source), then +1
    }

    private static class SoundLegNames
    {
        public const string Arrival = "arrival";
        public const string Laps = "laps";
    }
}

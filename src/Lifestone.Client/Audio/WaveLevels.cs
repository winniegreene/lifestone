using System.Buffers.Binary;
using System.Globalization;

namespace Lifestone.Client.Audio;

/// <summary>What <see cref="WaveLevels"/> measured in a recording.</summary>
/// <param name="Seconds">The recording's length.</param>
/// <param name="PeakDbfs">The loudest sample, in dB relative to full scale (<see cref="WaveLevels.SilenceDbfs"/> for silence).</param>
/// <param name="RmsDbfs">The RMS of every sample of every channel, in dBFS (<see cref="WaveLevels.SilenceDbfs"/> for silence).</param>
/// <param name="ActiveWindows">The 50 ms windows whose RMS is above the floor.</param>
/// <param name="Windows">All 50 ms windows (the last may be shorter).</param>
/// <param name="SampleRate">Frames per second.</param>
/// <param name="Channels">Channels in the file.</param>
public readonly record struct WaveLevelsResult(double Seconds, double PeakDbfs, double RmsDbfs, int ActiveWindows, int Windows, int SampleRate, int Channels)
{
    /// <summary>The share of windows above the floor, from 0 to 1 (0 for an empty recording).</summary>
    public double ActiveShare => Windows == 0 ? 0.0 : ActiveWindows / (double)Windows;

    /// <summary>The judge of the audio check: the peak is above <see cref="WaveLevels.NonSilentPeakDbfs"/> and at least one window is active.</summary>
    public bool NonSilent => PeakDbfs > WaveLevels.NonSilentPeakDbfs && ActiveWindows >= 1;
}

/// <summary>
/// Reads the bytes of the PCM <c>.wav</c> file <c>--record-audio</c> writes (the host reads the file: the client does no file I/O; Godot's <c>AudioStreamWav.SaveToWav</c> of an <c>AudioEffectRecord</c>: 16-bit
/// stereo PCM at the mix rate) and measures it: the duration, the peak and the RMS in dBFS, and the share of 50 ms windows above a floor. The
/// record-audio check judges a recording non-silent with it (M3 1.3 s3: the smoke <c>record-audio</c> step; it closed <c>TODO(record-audio-check)</c>). Engine-agnostic.
/// </summary>
/// <remarks>
/// <para>PCM of 8 bits (unsigned), 16, 24 or 32 bits (signed) and 32-bit float are read, as the plain format (1) or WAVE_FORMAT_EXTENSIBLE
/// (0xFFFE) with one of those; anything else is an <see cref="InvalidDataException"/> naming what it is. A <c>data</c> chunk whose size is
/// wrong (a writer that did not patch it) is clamped to the bytes in the file. Levels are over the samples scaled to −1..1; a window's level is
/// the RMS over all its sample frames and channels.</para>
/// <para>Silence is reported as <see cref="SilenceDbfs"/> (−120), not minus infinity, so the numbers print and parse.</para>
/// </remarks>
public static class WaveLevels
{
    /// <summary>The level reported for digital silence.</summary>
    public const double SilenceDbfs = -120.0;

    /// <summary>The window length of the active-window count, in seconds (50 ms).</summary>
    public const double WindowSeconds = 0.050;

    /// <summary>The default floor of an active window's RMS, in dBFS: retail's attenuation floor is −50 dB, so a sound too quiet to play is below it.</summary>
    public const double DefaultFloorDbfs = -60.0;

    /// <summary>A recording whose peak is not above this is silent (the audio check's judge).</summary>
    public const double NonSilentPeakDbfs = -60.0;

    /// <summary>Measures the bytes of a PCM <c>.wav</c> file.</summary>
    /// <param name="file">The whole file.</param>
    /// <param name="floorDbfs">An active window's RMS must be above this.</param>
    /// <exception cref="InvalidDataException">The bytes are not a PCM wave this reads.</exception>
    public static WaveLevelsResult Analyze(ReadOnlySpan<byte> file, double floorDbfs = DefaultFloorDbfs)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("not a RIFF/WAVE file");
        }

        var channels = 0;
        var rate = 0;
        var bits = 0;
        var tag = 0;
        var haveFormat = false;
        var data = ReadOnlySpan<byte>.Empty;
        var haveData = false;
        var at = 12;
        while (at + 8 <= file.Length)
        {
            var id = file.Slice(at, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 4)..]);
            var body = at + 8;
            var available = (long)file.Length - body;
            var length = (int)Math.Min(size, available);
            if (id.SequenceEqual("fmt "u8))
            {
                if (length < 16)
                {
                    throw new InvalidDataException("the fmt chunk is shorter than 16 bytes");
                }

                var fmt = file.Slice(body, length);
                tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                rate = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]), int.MaxValue);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                if (tag == 0xFFFE && length >= 26)
                {
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]); // the extensible format's sub-format GUID starts with the plain tag
                }

                haveFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = file.Slice(body, length); // a size larger than the file (an unpatched header) is clamped
                haveData = true;
                break;
            }

            at = body + length + (length & 1); // chunks are word aligned
        }

        if (!haveFormat)
        {
            throw new InvalidDataException("no fmt chunk");
        }

        if (!haveData)
        {
            throw new InvalidDataException("no data chunk");
        }

        var isFloat = tag == 3;
        if (!(tag == 1 && bits is 8 or 16 or 24 or 32) && !(isFloat && bits == 32))
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"unsupported wave layout: format {tag}, {bits} bits (PCM of 8, 16, 24 or 32 bits and 32-bit float are read)"));
        }

        if (channels is < 1 or > 8 || rate <= 0)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"unsupported wave layout: {channels} channels at {rate} Hz"));
        }

        var bytesPerSample = bits / 8;
        var frameBytes = bytesPerSample * channels;
        var frames = data.Length / frameBytes;
        var windowFrames = Math.Max(1, (int)Math.Round(WindowSeconds * rate));
        var floorPower = Math.Pow(10.0, floorDbfs / 10.0);
        var peak = 0.0;
        var total = 0.0;
        var windowSum = 0.0;
        var windowCount = 0;
        var inWindow = 0;
        var windows = 0;
        var active = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var offset = frame * frameBytes;
            for (var channel = 0; channel < channels; channel++)
            {
                var value = Sample(data[(offset + (channel * bytesPerSample))..], bits, isFloat);
                var magnitude = Math.Abs(value);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }

                var square = value * value;
                total += square;
                windowSum += square;
                windowCount++;
            }

            if (++inWindow == windowFrames)
            {
                windows++;
                active += windowSum / windowCount > floorPower ? 1 : 0;
                windowSum = 0.0;
                windowCount = 0;
                inWindow = 0;
            }
        }

        if (inWindow > 0)
        {
            windows++;
            active += windowSum / windowCount > floorPower ? 1 : 0;
        }

        var samples = (long)frames * channels;
        var rms = samples == 0 ? 0.0 : Math.Sqrt(total / samples);
        return new WaveLevelsResult(frames / (double)rate, ToDbfs(peak), ToDbfs(rms), active, windows, rate, channels);
    }

    /// <summary>The audio check's log line: <c>audio recording levels file= seconds= peak_dbfs= rms_dbfs= active_windows= sources=</c>, then <c>windows=</c> (all 50 ms windows) and, when asked for, <c>muted=</c>.</summary>
    /// <param name="file">The recording's path as the run was given it (relative to the repository).</param>
    /// <param name="levels">What <see cref="Analyze"/> measured.</param>
    /// <param name="sources">The per-source counters at the end of the run (<see cref="SoundSourceCounts.Describe"/>).</param>
    /// <param name="muted">True when the Master bus was silent for the run (M2 D8: an automated run); printed as a trailing <c>muted=</c> that proves a check ran without a sound card output, or null to leave it out.</param>
    public static string LevelsLine(string file, in WaveLevelsResult levels, string sources, bool? muted = null) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"audio recording levels file={file} seconds={levels.Seconds:0.00} peak_dbfs={levels.PeakDbfs:0.0} rms_dbfs={levels.RmsDbfs:0.0} active_windows={levels.ActiveWindows} sources={sources} windows={levels.Windows}{(muted is { } m ? (m ? " muted=true" : " muted=false") : string.Empty)}");

    private static double ToDbfs(double linear) => linear <= 1e-6 ? SilenceDbfs : Math.Max(SilenceDbfs, 20.0 * Math.Log10(linear));

    private static double Sample(ReadOnlySpan<byte> bytes, int bits, bool isFloat) => bits switch
    {
        8 => (bytes[0] - 128) / 128.0,
        16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0,
        24 => (((bytes[0] << 8) | (bytes[1] << 16) | (bytes[2] << 24)) >> 8) / 8388608.0,
        _ when isFloat => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
        _ => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0,
    };
}

/// <summary>
/// The sound counters per source, for the record-audio check's <c>sources=</c> and the login check's
/// <c>sounds_server= sounds_hooks= sounds_ui= ambient=</c>. A source that does not exist in the run is <see langword="null"/> and prints
/// <c>n/a</c>, never 0. <b>Sounded</b> counts (<paramref name="Server"/>, <paramref name="Hooks"/>, <paramref name="FootstepsSounded"/>) are voices a hand-off
/// started; <b>handed</b> counts (<paramref name="ServerHanded"/>, <paramref name="HooksHanded"/>, <paramref name="Footsteps"/>) are calls handed to the sound
/// player, which may choose no wave (M3 1.3 fix B, review 1.3-R2).
/// </summary>
/// <param name="Server">The voices the server's sounds (F750) started: <c>SoundHookRouter.ServerSounded</c>.</param>
/// <param name="Hooks">The voices the animation and script sound hooks started (footsteps included): <c>SoundHookRouter.HooksSounded</c>.</param>
/// <param name="Footsteps">The player's own Footstep hooks handed to the sound player: <c>SoundHookRouter.PlayerFootsteps</c> (the human sound table has no wave for them).</param>
/// <param name="Ui">The retail UI's sounds handed to the sound player: <c>RetailUiLayer.SoundStats.Played</c>.</param>
/// <param name="Ambient">The ambient chance draws that played: <c>AmbientSoundNode.Current.Player.Played</c>.</param>
/// <param name="ServerHanded">The server's sounds handed to the sound player: <c>SoundHookRouter.ServerSounds</c>.</param>
/// <param name="HooksHanded">The animation and script sound hooks handed to the sound player: <c>SoundHookRouter.HooksPlayed</c>.</param>
/// <param name="FootstepsSounded">The voices the player's own Footstep hooks started: <c>SoundHookRouter.PlayerFootstepsSounded</c>.</param>
public readonly record struct SoundSourceCounts(int? Server, int? Hooks, int? Footsteps, int? Ui, int? Ambient, int? ServerHanded = null, int? HooksHanded = null, int? FootstepsSounded = null)
{
    /// <summary>A count for a field value: the number, or <c>n/a</c> when the source does not exist in the run.</summary>
    public static string Value(int? count) => count is { } n ? n.ToString(CultureInfo.InvariantCulture) : "n/a";

    /// <summary>
    /// The login check's report-only fields with their leading space: <c> sounds_server=&lt;n|n/a&gt; sounds_hooks=&lt;n|n/a&gt; sounds_ui=&lt;n|n/a&gt; ambient=&lt;n|n/a&gt;
    /// sounds_server_handed=&lt;n|n/a&gt; sounds_hooks_handed=&lt;n|n/a&gt;</c>. <c>sounds_server</c> and <c>sounds_hooks</c> are voices started; the <c>*_handed</c> fields are calls handed over.
    /// </summary>
    public string LoginCheckFields => string.Create(
        CultureInfo.InvariantCulture,
        $" sounds_server={Value(Server)} sounds_hooks={Value(Hooks)} sounds_ui={Value(Ui)} ambient={Value(Ambient)} sounds_server_handed={Value(ServerHanded)} sounds_hooks_handed={Value(HooksHanded)}");

    /// <summary>The record-audio line's <c>sources=</c> value, one token: <c>server:1,hooks:199,footsteps:3,footsteps_sounded:0,ui:0,ambient:n/a,server_handed:1,hooks_handed:203</c>.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"server:{Value(Server)},hooks:{Value(Hooks)},footsteps:{Value(Footsteps)},footsteps_sounded:{Value(FootstepsSounded)},ui:{Value(Ui)},ambient:{Value(Ambient)},server_handed:{Value(ServerHanded)},hooks_handed:{Value(HooksHanded)}");
}

/// <summary>
/// The login check's sound counters per leg (M3 1.3 fix B, review 1.3-R2): each frame the runner hands it the counters and the name of the leg the frame
/// belongs to (the Academy arrival, then each movement leg), and it adds the growth of every counter to that leg. A counter that resets (the ambient player of
/// a new scene) adds its new value; a source that is absent adds nothing and stays <c>n/a</c> in a leg it never appeared in. Frames before the Enter belong to
/// no leg (pass null), so what played on the character list is not counted. Allocation-free after a leg's first frame.
/// </summary>
public sealed class SoundLegTally
{
    private readonly List<Leg> _legs = [];
    private SoundSourceCounts _last;

    /// <summary>
    /// One frame: the counters now and the leg this frame belongs to (null before the Enter). The growth since the previous frame is added to that leg.
    /// </summary>
    /// <param name="leg">The leg's name, or null when the frame belongs to no leg.</param>
    /// <param name="now">The counters now.</param>
    public void Step(string? leg, in SoundSourceCounts now)
    {
        if (leg is not null)
        {
            var slot = Find(leg);
            var total = slot.Total;
            slot.Total = new SoundSourceCounts(
                Add(total.Server, _last.Server, now.Server),
                Add(total.Hooks, _last.Hooks, now.Hooks),
                Add(total.Footsteps, _last.Footsteps, now.Footsteps),
                Add(total.Ui, _last.Ui, now.Ui),
                Add(total.Ambient, _last.Ambient, now.Ambient),
                Add(total.ServerHanded, _last.ServerHanded, now.ServerHanded),
                Add(total.HooksHanded, _last.HooksHanded, now.HooksHanded),
                Add(total.FootstepsSounded, _last.FootstepsSounded, now.FootstepsSounded));
        }

        // A source absent this frame keeps its last value, so it resumes without counting its whole total again.
        _last = new SoundSourceCounts(
            now.Server ?? _last.Server,
            now.Hooks ?? _last.Hooks,
            now.Footsteps ?? _last.Footsteps,
            now.Ui ?? _last.Ui,
            now.Ambient ?? _last.Ambient,
            now.ServerHanded ?? _last.ServerHanded,
            now.HooksHanded ?? _last.HooksHanded,
            now.FootstepsSounded ?? _last.FootstepsSounded);
    }

    /// <summary>What one leg added; every field <c>n/a</c> for a leg that never ran.</summary>
    /// <param name="leg">The leg's name.</param>
    public SoundSourceCounts Of(string leg)
    {
        foreach (var l in _legs)
        {
            if (l.Name == leg)
            {
                return l.Total;
            }
        }

        return default;
    }

    /// <summary>What all legs added together (the run since the Enter).</summary>
    public SoundSourceCounts Sum()
    {
        SoundSourceCounts sum = default;
        foreach (var l in _legs)
        {
            var t = l.Total;
            sum = new SoundSourceCounts(
                Plus(sum.Server, t.Server),
                Plus(sum.Hooks, t.Hooks),
                Plus(sum.Footsteps, t.Footsteps),
                Plus(sum.Ui, t.Ui),
                Plus(sum.Ambient, t.Ambient),
                Plus(sum.ServerHanded, t.ServerHanded),
                Plus(sum.HooksHanded, t.HooksHanded),
                Plus(sum.FootstepsSounded, t.FootstepsSounded));
        }

        return sum;
    }

    /// <summary>
    /// The login check's report fields from the legs (DoD 13): <c>sounds_server</c> in the <paramref name="arrival"/> leg, <c>sounds_hooks</c> and <c>sounds_ui</c> over every
    /// leg since the Enter, <c>ambient</c> in the <paramref name="ambientLeg"/> only (<c>n/a</c> when that leg never ran), and the two <c>*_handed</c> fields over every leg
    /// (the server's in the arrival leg).
    /// </summary>
    /// <param name="arrival">The arrival leg's name.</param>
    /// <param name="ambientLeg">The leg the ambient count is read in.</param>
    public SoundSourceCounts Report(string arrival, string ambientLeg)
    {
        var sum = Sum();
        var arrived = Of(arrival);
        var laps = Of(ambientLeg);
        return new SoundSourceCounts(arrived.Server, sum.Hooks, sum.Footsteps, sum.Ui, laps.Ambient, arrived.ServerHanded, sum.HooksHanded, sum.FootstepsSounded);
    }

    private static int? Add(int? total, int? last, int? now)
    {
        if (now is not { } n)
        {
            return total; // the source is absent this frame: nothing to add
        }

        var grew = last is { } l && n >= l ? n - l : n; // a new source or a reset counter starts from zero
        return (total ?? 0) + grew;
    }

    private static int? Plus(int? a, int? b) => a is null ? b : b is null ? a : a + b;

    private Leg Find(string name)
    {
        foreach (var l in _legs)
        {
            if (l.Name == name)
            {
                return l;
            }
        }

        var leg = new Leg(name);
        _legs.Add(leg);
        return leg;
    }

    private sealed class Leg(string name)
    {
        public string Name { get; } = name;

        public SoundSourceCounts Total { get; set; }
    }
}

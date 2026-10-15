using Godot;
using Lifestone.Client.Audio;
using Lifestone.Data.Audio;
using Lifestone.Game.Core;

namespace Lifestone.Game.Audio;

/// <summary>
/// Waves as Godot audio streams: <see cref="AudioStreamWav"/> for PCM (the dats' sounds are 11,025 Hz mono 16-bit) and
/// <see cref="AudioStreamMP3"/> for MPEG Layer 3, built on the worker pool and handed to the main thread, with a byte-bounded
/// cache. Any other format is a loud <c>TODO(audio)</c> and silence (retail converted those with MSACM,
/// <c>SoundBuf::CopyWaveToBuffer</c>).
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> The streams are CPU-side data objects (a byte array and a few fields; no <c>RenderingServer</c> or
/// scene object), built on workers like CPU-side <c>Image</c>s are (docs/render-contract.md §0); the cache, the in-flight
/// table and every callback are on the main thread only (a worker hands its result over through
/// <see cref="MainThreadQueue"/>).</para>
/// <para><b>Pan and 8-bit.</b> <see cref="PcmStreams"/> (Lifestone.Client, tested) turns PCM into samples: retail's pan, a
/// per-channel attenuation of up to 15 dB, is baked into 16-bit stereo because Godot's <see cref="AudioStreamPlayer"/> has no pan
/// (one cached stream per wave and pan), and retail's unsigned 8-bit samples are re-centred to Godot's signed ones. An MP3 wave
/// cannot be rebaked and plays centred whatever its pan (<c>TODO(audio)</c>, warned once per wave). Portal space and the
/// interface sounds are centred, so they take the unbaked path.</para>
/// </remarks>
public sealed class WaveStreams
{
    /// <summary>The cache's size bound, in bytes of audio data.</summary>
    public const long CacheBudgetBytes = 64L * 1024 * 1024;

    private readonly IAudioSource _source;
    private readonly GameServices _services;
    private readonly Dictionary<(uint Wave, int Pan), CacheEntry> _cache = [];
    private readonly Queue<(uint Wave, int Pan)> _order = new();
    private readonly Dictionary<(uint Wave, int Pan), List<Action<AudioStream?>>> _inFlight = [];
    private readonly HashSet<uint> _warned = [];
    private long _cachedBytes;

    private readonly record struct CacheEntry(AudioStream? Stream, long Bytes);

    /// <summary>Creates the cache over a wave source.</summary>
    public WaveStreams(IAudioSource source, GameServices services)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Streams built (not served from the cache) since creation.</summary>
    public int Built { get; private set; }

    /// <summary>Streams in the cache.</summary>
    public int Count => _cache.Count;

    /// <summary>Waves that could not be turned into a stream (missing, unsupported or malformed), each warned once.</summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Main thread: hands <paramref name="ready"/> the stream of a wave at a pan (null: silence, already logged). From the cache
    /// it runs at once; otherwise a worker builds the stream and <paramref name="ready"/> runs on the main thread when it is done.
    /// </summary>
    public void Request(uint waveId, int pan, Action<AudioStream?> ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        var key = (waveId, pan);
        if (_cache.TryGetValue(key, out var entry))
        {
            ready(entry.Stream);
            return;
        }

        if (_inFlight.TryGetValue(key, out var waiting))
        {
            waiting.Add(ready);
            return;
        }

        _inFlight[key] = [ready];
        var mainThread = _services.MainThread;
        _ = _services.Workers.RunSync(
            _ =>
            {
                AudioStream? stream = null;
                try
                {
                    stream = Build(waveId, pan);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    // Any non-fatal failure is silence for this wave, never a request that is never completed (a waiting caller, the portal's prefetch, would wait for ever).
                    Warn(waveId, $"the wave could not be built: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    var built = stream;
                    mainThread.Enqueue(() => Complete(key, built), MainThreadQueue.Urgent);
                }

                return true;
            });
    }

    /// <summary>Main thread: drops every cached stream (a running voice keeps the one it plays).</summary>
    public void Clear()
    {
        _cache.Clear();
        _order.Clear();
        _cachedBytes = 0;
    }

    /// <summary>
    /// Builds the stream of a wave at a pan (a worker's job; also the checks'). Null with a logged warning when the wave is missing
    /// or its format is unsupported.
    /// </summary>
    public AudioStream? Build(uint waveId, int pan)
    {
        if (_source.GetWave(waveId) is not { } wave)
        {
            Warn(waveId, "the wave does not exist");
            return null;
        }

        if (wave.IsMp3 && pan != 0)
        {
            Warn(waveId, $"TODO(audio): MP3 wave 0x{waveId:X8} cannot be panned (pan {pan}); it plays centred");
        }

        var stream = Build(wave, pan, out var problem);
        if (stream is null && problem is not null)
        {
            Warn(waveId, problem);
        }

        return stream;
    }

    /// <summary>
    /// Pure conversion of a decoded wave (no cache, no logging): PCM to <see cref="AudioStreamWav"/>, MPEG Layer 3 to
    /// <see cref="AudioStreamMP3"/>; <paramref name="problem"/> says why a wave gave null.
    /// </summary>
    /// <param name="wave">The decoded wave.</param>
    /// <param name="pan">Retail's pan index, −15 to 15 (see the remarks of the type).</param>
    /// <param name="problem">Why there is no stream, or null.</param>
    public static AudioStream? Build(WaveData wave, int pan, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(wave);
        problem = wave.Unsupported;
        if (problem is not null)
        {
            return null;
        }

        return wave.IsMp3 ? BuildMp3(wave, out problem) : BuildPcm(wave, pan, out problem);
    }

    private static AudioStreamMP3? BuildMp3(WaveData wave, out string? problem)
    {
        var stream = new AudioStreamMP3 { Data = wave.Samples.ToArray() };
        if (stream.GetLength() <= 0.0)
        {
            problem = $"TODO(audio): Godot found no MPEG audio frames in wave 0x{wave.Id:X8}";
            return null;
        }

        problem = null;
        return stream;
    }

    private static AudioStreamWav? BuildPcm(WaveData wave, int pan, out string? problem)
    {
        if (!PcmStreams.TryBuild(wave, pan, out var pcm, out problem))
        {
            return null;
        }

        return new AudioStreamWav
        {
            Format = pcm.Is8Bit ? AudioStreamWav.FormatEnum.Format8Bits : AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = pcm.MixRate,
            Stereo = pcm.Stereo,
            Data = pcm.Data,
        };
    }

    private void Complete((uint Wave, int Pan) key, AudioStream? stream)
    {
        var bytes = stream switch
        {
            AudioStreamWav wav => wav.Data.Length,
            AudioStreamMP3 mp3 => mp3.Data.Length,
            _ => 0,
        };

        if (stream is null)
        {
            Failed++;
        }
        else
        {
            Built++;
        }

        _cache[key] = new CacheEntry(stream, bytes);
        _order.Enqueue(key);
        _cachedBytes += bytes;
        while (_cachedBytes > CacheBudgetBytes && _order.Count > 1)
        {
            var oldest = _order.Dequeue();
            if (oldest != key && _cache.Remove(oldest, out var evicted))
            {
                _cachedBytes -= evicted.Bytes;
            }
        }

        if (_inFlight.Remove(key, out var waiting))
        {
            foreach (var ready in waiting)
            {
                ready(stream);
            }
        }
    }

    private void Warn(uint waveId, string message)
    {
        lock (_warned)
        {
            if (!_warned.Add(waveId))
            {
                return;
            }
        }

        Log.Warn("audio", message, ("wave", $"0x{waveId:X8}"));
    }
}

using Godot;
using Lifestone.Client.Audio;
using Lifestone.Data.Audio;
using Lifestone.Data.Effects;
using Lifestone.Game.Core;

namespace Lifestone.Game.Audio;

/// <summary>
/// The sound card (docs/portal-space.md §11.3): retail's 16 voices as 16 <see cref="AudioStreamPlayer"/>s, on two run-time
/// buses, <see cref="UiBusName"/> and <see cref="EffectsBusName"/>, both sending to Master; <c>--volume</c> / <c>--mute</c>
/// set Master's level. Implements <see cref="IAudioOutput"/> for <see cref="RetailSoundPlayer"/>, which holds the retail rules
/// (what plays, how loud, which voice): this node only plays what it is told.
/// </summary>
/// <remarks>
/// <para><b>Play.</b> <see cref="Play"/> stops the voice and asks <see cref="WaveStreams"/> for the wave's stream (built on a
/// worker unless cached); when it arrives the voice's <c>volume_db</c> is set to the attenuation, its bus to the voice's, and it
/// plays. A voice with a request in flight counts as playing (<see cref="IsPlaying"/>), so the retail rules see a started
/// sound; a later <see cref="Play"/> or <see cref="Stop"/> on the voice supersedes the request. An unsupported or missing wave
/// is a loud warning (once per wave) and silence.</para>
/// <para><b>Silence.</b> An automated run is silent unless <c>--volume</c> is given (owner decision D8:
/// <see cref="AudioOptions.Resolve"/>); a silent master bus is muted and at −80 dB, and the voices still run, so the retail
/// rules and the checks behave the same. <c>-Offscreen</c> additionally selects Godot's Dummy audio driver, so nothing reaches
/// the sound card then either.</para>
/// <para>Main thread. One instance per process (<see cref="Current"/>, <see cref="GameServices.Audio"/>).</para>
/// </remarks>
public partial class AudioService : Node, IAudioOutput
{
    /// <summary>The interface bus.</summary>
    public const string UiBusName = "UI";

    /// <summary>The world and animation bus.</summary>
    public const string EffectsBusName = "Effects";

    /// <summary>Retail's voice count.</summary>
    public const int VoiceCount = RetailSoundRules.VoiceCount;

    private const string MasterBusName = "Master";

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[VoiceCount];
    private readonly int[] _requests = new int[VoiceCount];
    private readonly bool[] _pending = new bool[VoiceCount];
    private WaveStreams _streams = null!;
    private ulong _busyUntilMsec;
    private AudioEffectRecord? _record;
    private string? _recordPath;

    /// <summary>The installed service, or null (check suites, the spell viewer, a missing dat).</summary>
    public static AudioService? Current { get; private set; }

    /// <summary>The master bus's settings in force.</summary>
    public AudioSettings Settings { get; private set; } = AudioSettings.Default;

    /// <summary>The retail rules over this output: the <see cref="ISoundSink"/> the portal space and the retail UI play through.</summary>
    public RetailSoundPlayer Sound { get; private set; } = null!;

    /// <summary>The wave streams (cache and builder).</summary>
    public WaveStreams Streams => _streams;

    /// <summary>The waves and sound tables (the dats).</summary>
    public IAudioSource Source { get; private set; } = null!;

    /// <summary>The Master bus index.</summary>
    public int MasterBus { get; private set; }

    /// <summary>The <see cref="UiBusName"/> bus index.</summary>
    public int UiBus { get; private set; }

    /// <summary>The <see cref="EffectsBusName"/> bus index.</summary>
    public int EffectsBus { get; private set; }

    /// <summary>Starts requested (<see cref="Play"/> calls).</summary>
    public int Starts { get; private set; }

    /// <summary>Starts that reached a stream and played.</summary>
    public int Played { get; private set; }

    /// <summary>Starts that got no stream (a missing or unsupported wave), or were superseded before it arrived.</summary>
    public int Silenced { get; private set; }

    /// <summary>
    /// Builds the service: the buses, the master level, the 16 voices and the retail rules over them. Seeds the variant and
    /// probability rolls with <paramref name="seed"/> (a clock in live play, fixed for captures and tests).
    /// </summary>
    public static AudioService Create(GameServices services, AudioSettings settings, int seed)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        var dats = services.Dats ?? throw new InvalidOperationException("The audio service needs the dats.");
        var service = new AudioService { Name = "AudioService" };
        var source = new DatAudioSource(dats);
        service.Source = source;
        service._streams = new WaveStreams(source, services);
        service.Sound = new RetailSoundPlayer(service, source, new RetailRandom(seed), new MsvcCrtRandom((uint)seed), AudioLevels.Default, GodotLogSink.Instance);
        service.BuildBuses();
        service.Apply(settings);
        for (var i = 0; i < VoiceCount; i++)
        {
            var player = new AudioStreamPlayer { Name = $"Voice{i:D2}", Bus = UiBusName };
            service._players[i] = player;
            service.AddChild(player);
        }

        return service;
    }

    /// <summary>Sets Master's level from <paramref name="settings"/>: muted and at −80 dB when silent, else the linear volume in decibels.</summary>
    public void Apply(AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        AudioServer.SetBusVolumeDb(MasterBus, settings.MasterDecibels);
        AudioServer.SetBusMute(MasterBus, settings.Muted);
    }

    /// <inheritdoc/>
    public void Play(in VoicePlay play)
    {
        var voice = CheckVoice(play.Voice);
        _players[voice].Stop();
        Starts++;
        var request = ++_requests[voice];
        _pending[voice] = true;
        var (volume, bus, pan, wave) = (play.AttenuationDb, play.Bus, play.Pan, play.WaveId);
        _streams.Request(play.WaveId, pan, stream =>
        {
            if (_requests[voice] != request)
            {
                Silenced++;
                return; // superseded or stopped while the stream was being built
            }

            _pending[voice] = false;
            if (stream is null)
            {
                Silenced++;
                return; // the wave could not be built: already warned
            }

            var player = _players[voice];
            player.Stream = stream;
            player.VolumeDb = Math.Max(volume, RetailSoundRules.VolMin);
            player.Bus = bus == AudioBus.Ui ? UiBusName : EffectsBusName;
            player.Play();
            Played++;
            _busyUntilMsec = Math.Max(_busyUntilMsec, Time.GetTicksMsec() + (ulong)(stream.GetLength() * 1000.0) + 50);
            Log.Debug("audio", "voice started", ("voice", voice), ("wave", $"0x{wave:X8}"), ("db", player.VolumeDb), ("bus", player.Bus), ("pan", pan), ("seconds", stream.GetLength()));
        });
    }

    /// <summary>
    /// Builds and caches (on workers) the centred streams of <paramref name="waveIds"/> so their first play is immediate: a stream
    /// first asked for while the world streams in queues behind that work on the worker pool (the portal whooshes and the hum).
    /// </summary>
    public void Prefetch(IEnumerable<uint> waveIds)
    {
        ArgumentNullException.ThrowIfNull(waveIds);
        foreach (var id in waveIds)
        {
            _streams.Request(id, 0, static _ => { });
        }
    }

    /// <summary>The waves a sound table plays for the given sound types (every variant), for <see cref="Prefetch"/>; none for an unknown table or type.</summary>
    public IEnumerable<uint> WavesOf(uint soundTableId, params uint[] soundTypes)
    {
        var table = Source.GetSoundTable(soundTableId);
        foreach (var type in soundTypes)
        {
            foreach (var entry in table?.Lookup(type) ?? [])
            {
                yield return entry.WaveId;
            }
        }
    }

    /// <inheritdoc/>
    public void Stop(int voice)
    {
        voice = CheckVoice(voice);
        _requests[voice]++; // supersedes a request in flight
        _pending[voice] = false;
        _players[voice].Stop();
    }

    /// <inheritdoc/>
    public bool IsPlaying(int voice)
    {
        voice = CheckVoice(voice);
        return _pending[voice] || _players[voice].Playing;
    }

    /// <summary>The voice's player (checks).</summary>
    public AudioStreamPlayer PlayerOf(int voice) => _players[CheckVoice(voice)];

    public override void _EnterTree()
    {
        Current = this;
        GameServices.Instance.Audio = this;
    }

    public override void _ExitTree()
    {
        if (Current == this)
        {
            Current = null;
        }

        if (GameServices.Instance.Audio == this)
        {
            GameServices.Instance.Audio = null;
        }
    }

    /// <summary>
    /// Stops every voice while they are still in the tree and waits for the mixer to free their playbacks. Called by
    /// <see cref="ShutdownGuard"/> before the voices leave the tree: a voice that is audible when its player exits the tree is
    /// stopped by the engine, which then leaves the playback and its stream to the leak report ("ObjectDB instances were leaked at
    /// exit"). Only does anything when a sound was audible a moment ago.
    /// </summary>
    public void StopAllAndWait()
    {
        for (var i = 0; i < VoiceCount; i++)
        {
            _requests[i]++;
            _pending[i] = false;
        }

        var busy = Time.GetTicksMsec() < _busyUntilMsec;
        for (var i = 0; i < VoiceCount; i++)
        {
            _players[i]?.Stop();
        }

        if (busy)
        {
            var waited = Time.GetTicksMsec();
            WaitForMixer();
            Log.Debug("audio", "voices stopped for shutdown; waited for the mixer", ("ms", Time.GetTicksMsec() - waited));
        }
    }

    /// <summary>
    /// <c>--record-audio</c>: records the Master bus from now until the game quits (<see cref="FinishRecording"/>, through
    /// <see cref="ShutdownGuard"/>). The record effect is Master's last effect, and a bus's effects run before its level and its
    /// mute, so a silent automated run records the full mix too. Logs the start's wall-clock time (<c>unix_ms</c>), which lines the
    /// sound up with a screen capture.
    /// </summary>
    /// <param name="path">A relative <c>.wav</c> path under <c>cache/</c> (<see cref="AudioOptions.RecordPath"/>), from the repository root.</param>
    public void StartRecording(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _recordPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Diagnostics.ScreenshotWriter.RepositoryRoot, path));
        _record = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
        AudioServer.AddBusEffect(MasterBus, _record);
        _record.SetRecordingActive(true);
        Log.Info("audio", "audio recording started", ("path", path), ("unix_ms", (long)(Time.GetUnixTimeFromSystem() * 1000.0)), ("mix_rate", AudioServer.GetMixRate()));
    }

    /// <summary>True while a <see cref="StartRecording"/> recording runs.</summary>
    public bool IsRecording => _record is not null;

    /// <summary>Stops a <see cref="StartRecording"/> recording and writes it; a failure is an error in the log, never a crash at exit.</summary>
    public void FinishRecording()
    {
        if (_record is null || _recordPath is null)
        {
            return;
        }

        var record = _record;
        _record = null;
        try
        {
            record.SetRecordingActive(false);
            OS.DelayMsec(10); // the recorder's own thread drains its buffer every 0.5 ms and the stop does not join it: let it finish first
            var recording = record.GetRecording();
            if (recording is null)
            {
                Log.Error("audio", "audio recording is empty; nothing written", ("path", _recordPath));
                return;
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_recordPath)!);
            var error = recording.SaveToWav(_recordPath);
            if (error == Error.Ok)
            {
                Log.Info("audio", "audio recording written", ("path", _recordPath), ("seconds", recording.GetLength()), ("unix_ms", (long)(Time.GetUnixTimeFromSystem() * 1000.0)));
            }
            else
            {
                Log.Error("audio", "audio recording could not be written", ("path", _recordPath), ("error", error));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("audio", "audio recording could not be written", ("path", _recordPath), ("problem", ex.Message), ("type", ex.GetType().Name));
        }
    }

    /// <summary>
    /// A sibling node added after the service: the tree exits children in reverse order, so its <c>_ExitTree</c> runs before the
    /// service's voices leave the tree (<see cref="StopAllAndWait"/>).
    /// </summary>
    public sealed partial class ShutdownGuard : Node
    {
        private AudioService? _service;

        /// <summary>Creates the guard for <paramref name="service"/>.</summary>
        public static ShutdownGuard For(AudioService service) => new() { Name = "AudioShutdownGuard", _service = service };

        public override void _ExitTree()
        {
            // The service may have been freed already (a check suite builds and frees its own): nothing to stop then.
            if (_service is not null && GodotObject.IsInstanceValid(_service))
            {
                try
                {
                    _service.FinishRecording();
                }
                finally
                {
                    _service.StopAllAndWait();
                }
            }
        }
    }

    /// <summary>
    /// Waits, at most 100 ms per step, for the mixer thread to run twice (one step applies the stop, the next frees the playbacks).
    /// </summary>
    private static void WaitForMixer()
    {
        for (var step = 0; step < 2; step++)
        {
            var before = AudioServer.GetTimeSinceLastMix();
            var deadline = Time.GetTicksMsec() + 100;
            while (Time.GetTicksMsec() < deadline && AudioServer.GetTimeSinceLastMix() >= before)
            {
                OS.DelayMsec(2);
            }
        }
    }

    private static int CheckVoice(int voice) =>
        (uint)voice < VoiceCount ? voice : throw new ArgumentOutOfRangeException(nameof(voice), voice, $"Voices are 0 to {VoiceCount - 1}.");

    /// <summary>Run-time buses: UI and Effects, each sending to Master (reused when the project already has them).</summary>
    private void BuildBuses()
    {
        MasterBus = AudioServer.GetBusIndex(MasterBusName);
        UiBus = EnsureBus(UiBusName);
        EffectsBus = EnsureBus(EffectsBusName);
    }

    private static int EnsureBus(string name)
    {
        var index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            index = AudioServer.BusCount;
            AudioServer.AddBus(index);
            AudioServer.SetBusName(index, name);
        }

        AudioServer.SetBusSend(index, MasterBusName);
        AudioServer.SetBusVolumeDb(index, 0f);
        AudioServer.SetBusMute(index, false);
        return index;
    }
}

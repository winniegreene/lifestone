using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Effects;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.Render;
using Lifestone.Game.Sessions;

namespace Lifestone.Game.Audio;

/// <summary>
/// Retail's outdoor ambient sound in the play and explore worlds (M3 1.3 s2b): feeds <see cref="AmbientSoundPlayer"/> the camera's
/// position every frame while the world is shown and the player is in it (M3 1.3 fix A, 1.3-R6), and plays the sound half of the server's AdminEnvirons through it. The sounds go to
/// <see cref="AudioService.Sound"/>, so they are muted and at -80 dB under M2 D8 (automated runs are silent unless <c>--volume</c>
/// is given) and still counted: <see cref="Player"/>'s <see cref="AmbientSoundPlayer.Draws"/>, <see cref="AmbientSoundPlayer.Played"/> and
/// <see cref="AmbientSoundPlayer.Skipped"/> do not depend on the volume.
/// </summary>
/// <remarks>
/// <para>The listener is the camera (retail's <c>SoundManager::player_position_</c> is the viewer), outdoors when it is not in an
/// EnvCell (<c>TODO(ambient-seen-outside)</c> in <see cref="AmbientSoundPlayer"/>). The landblock terrain it reads is cached here
/// (one dat read per landblock, on a pool thread, never the frame thread; the 3 x 3 around the camera). The session is looked up each frame until it
/// exists; its <see cref="AdminEnvironChanged"/> events are then applied: a sound plays from the UI table, a fog option logs
/// <c>TODO(m3-environs-fog)</c> once.</para>
/// <para><b>In the world.</b> In a session (play mode) the ambient runs only while the world is drawn (<see cref="WorldVisibility.Visible"/>: not under the
/// tunnel or a full-screen screen) and the player has an object (<see cref="SelfTravel"/> is not <c>NotInWorld</c>: not under the character list, not after a log off);
/// otherwise <see cref="AmbientSoundPlayer.Reset"/> runs once (retail's ambient follows the viewer's world cell, and a 0 cell resets) and neither the scan nor
/// <c>Ambient::UseTime</c> runs. Without a session (the explore world) the world's visibility is the only gate. <see cref="DrawsBeforeWorld"/> counts the draws made before the
/// first open frame and must read 0.</para>
/// <para>Main thread. One instance per process (<see cref="Current"/>).</para>
/// </remarks>
public partial class AmbientSoundNode : Node
{
    private FlyCamera _camera = null!;
    private GameServices _services = null!;
    private IDisposable? _subscription;
    private int _gatedFrames;
    private int _drawsBeforeWorld = -1;
    private bool _wasInWorld;
    private readonly HashSet<uint> _fogLogged = [];

    /// <summary>The node of this run, or null.</summary>
    public static AmbientSoundNode? Current { get; private set; }

    /// <summary>The player (its counters are the public stats: <c>Draws</c>, <c>Played</c>, <c>Skipped</c>, <c>EnvironSounds</c>).</summary>
    public AmbientSoundPlayer Player { get; private set; } = null!;

    /// <summary>Frames the gate was closed (the world not shown, or no player in it): no scan, no <c>UseTime</c>.</summary>
    public int GatedFrames => _gatedFrames;

    /// <summary>Ambient draws made before the first frame the gate was open (0 once the gate holds); -1 until it opened.</summary>
    public int DrawsBeforeWorld => _drawsBeforeWorld;

    private AmbientTerrainCache Terrain { get; set; } = null!;

    /// <summary>Seconds on the clock the player runs on (monotonic real time).</summary>
    public static double Now() => Time.GetTicksUsec() * 1e-6;

    /// <summary>
    /// Builds the node over <paramref name="audio"/> and adds it to <paramref name="parent"/>. Returns null (with a loud log) when the dats
    /// have no usable region sound data; the game is then without ambient sound, never without a world.
    /// </summary>
    public static AmbientSoundNode? Install(Node parent, GameServices services, AudioService audio, FlyCamera camera, int seed)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(camera);
        if (services.Dats is not { } dats)
        {
            return null;
        }

        try
        {
            var region = RegionSoundData.FromRegion(dats.Region);
            uint uiTable;
            try
            {
                uiTable = PortalSpaceAssets.Resolve(dats.EnumMaps).UiSoundTableId;
            }
            catch (InvalidDataException ex)
            {
                uiTable = 0;
                Log.Warn("audio", "the UI sound table could not be resolved; the environ sounds are dropped", ("problem", ex.Message));
            }

            var terrain = new AmbientTerrainCache(dats);
            var player = new AmbientSoundPlayer(region, dats.TerrainRegion.LandHeightTable, terrain, audio.Sound, new RetailRandom(seed ^ 0x5A5A), uiTable);
            var node = new AmbientSoundNode { Name = "AmbientSound", _camera = camera, _services = services, Player = player, Terrain = terrain };
            parent.AddChild(node);
            Log.Info("audio", "ambient sound installed", ("tables", region.Tables.Count), ("terrain_types", region.TerrainTypeCount), ("ui_table", $"0x{uiTable:X8}"));
            LogUnknowns();
            return node;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            Log.Error("audio", "the region's ambient sounds could not be read; the game has no ambient sound", ("problem", ex.Message), ("type", ex.GetType().Name));
            return null;
        }
    }

    /// <summary>The ambient sound's unknowns, each logged once per run (CLAUDE.md, "Loud stubs"): they are doc comments in <see cref="AmbientSoundPlayer"/> otherwise.</summary>
    private static void LogUnknowns()
    {
        Log.Warn("audio", "TODO(ambient-seen-outside): the ambient sound counts a listener cell as outdoors only if it is an outdoor land cell; retail also counts an EnvCell that sees outside (a doorway), which Lifestone does not know, so a doorway is silent. Logged once");
        Log.Warn("audio", "TODO(ambient-constant-defaults): ConstantSound has no PlayNow or GetSoundPos of its own in the symbol table; the base class's are taken as 'always play' and 'no position' (a constant ambient sound plays from the centre). Logged once");
        Log.Warn("audio", "TODO(ambient-slider): retail's ambient volume slider and enable flag are not in AudioLevels; an ambient sound plays on the effects slider and bus, with the sink's own probability roll. Logged once");
    }

    public override void _EnterTree() => Current = this;

    public override void _ExitTree()
    {
        Log.Info(
            "audio",
            "ambient sound summary",
            ("ambient_draws", Player.Draws),
            ("ambient_played", Player.Played),
            ("ambient_skipped", Player.Skipped),
            ("frames", Player.Frames),
            ("scans", Player.Scans),
            ("outdoor_frames", Player.OutdoorFrames),
            ("gated_frames", _gatedFrames),
            ("draws_before_world", _drawsBeforeWorld),
            ("terrain_reads", Terrain.ReadsFinished),
            ("terrain_reads_on_frame_thread", Terrain.ReadsOnCallerThread),
            ("terrain_read_failures", Terrain.Failures),
            ("environ_sounds", Player.EnvironSounds));
        _subscription?.Dispose();
        _subscription = null;
        if (Current == this)
        {
            Current = null;
        }
    }

    public override void _Process(double delta)
    {
        if (_subscription is null)
        {
            TrySubscribe();
        }

        if (!InWorld())
        {
            _gatedFrames++;
            if (_wasInWorld)
            {
                _wasInWorld = false;
                Player.Reset();
                Log.Info("audio", "ambient sound: the world is not shown or the player is not in it; the ambient is reset", ("draws", Player.Draws));
            }

            return;
        }

        if (!_wasInWorld)
        {
            _wasInWorld = true;
            if (_drawsBeforeWorld < 0)
            {
                _drawsBeforeWorld = Player.Draws;
            }

            Log.Info("audio", "ambient sound: the world is shown and the player is in it", ("draws_before_world", _drawsBeforeWorld), ("gated_frames", _gatedFrames));
        }

        var position = _camera.WorldPosition;
        if (!AcCoordinates.TryGetOutdoorCell(position, out var cell))
        {
            return; // off the map: nothing to hear
        }

        var heading = -_camera.Heading * (Math.PI / 180.0);
        var orientation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, (float)heading);
        var listener = new CellFrame(cell.CellId, new AcFrame(cell.Local, orientation));
        Player.Update(listener, !_camera.IsIndoors, Now());
    }

    /// <summary>The world is drawn and, in a session, the player has an object (the arrival has begun and the log off has not).</summary>
    private bool InWorld()
    {
        if (!_services.WorldVisibility.Visible)
        {
            return false;
        }

        return SessionHost.Instance is not { } host || host.Session is { } session && session.State.Self.Travel != SelfTravel.NotInWorld;
    }

    private void TrySubscribe()
    {
        if (SessionHost.Current is not { } session)
        {
            return;
        }

        _subscription = session.Events.Subscribe<AdminEnvironChanged>(OnEnviron);
    }

    private void OnEnviron(in AdminEnvironChanged e)
    {
        var applied = Player.ApplyEnviron(new EnvironEffect(e.Kind, e.SoundType));
        if (e.Kind is EnvironKind.Fog or EnvironKind.Clear && _fogLogged.Add(e.Environ))
        {
            Log.Warn("audio", "TODO(m3-environs-fog): an AdminEnvirons fog option arrived; the fog and ambient override is not drawn", ("environ", e.Environ));
        }

        Log.Info("audio", "environ", ("environ", e.Environ), ("kind", e.Kind.ToString()), ("sound", $"0x{e.SoundType:X2}"), ("played", applied));
    }
}

/// <summary>The landblock terrain around the listener, read from the dats once per landblock on a pool thread (<see cref="BackgroundAmbientTerrain"/>).</summary>
public sealed class AmbientTerrainCache(DatSet dats, bool background = true) : BackgroundAmbientTerrain(dats.GetLandblockTerrain, background);

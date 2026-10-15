using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.UI;

/// <summary>
/// Moves the fly camera (and with it the streaming focus and the floating origin) to a place: teleport destinations,
/// map coordinates, or an exact camera pose (golden viewpoints).
/// </summary>
/// <remarks>
/// <para><b>Outdoor places</b>: <see cref="WorldRoot.Teleport"/> (drops every landblock and recentres the floating
/// origin), then the camera gets the start view (<see cref="CameraPlacement.Overview"/>) or, in walk mode, the eye on
/// the ground, and the interiors' cell tracker is told the viewer is outdoors.</para>
/// <para><b>Indoor places</b> (EnvCells: dungeons and building interiors) go through the interiors
/// (<see cref="InteriorController.PlaceCamera"/>, docs/interiors.md §6): the world teleports, the eye stands
/// <see cref="FlyCamera.EyeHeight"/> above the floor point and the tracker knows the cell at once (read from the dat),
/// so a dungeon streams indoor from the first frame (its landblock only) instead of loading the landscape around it
/// first. Without the interiors (<c>--interiors=off</c>) the camera stands at the cell position and a warning says
/// why nothing is drawn.</para>
/// </remarks>
public sealed class Navigator
{
    private readonly GameServices _services;
    private readonly WorldRoot _world;
    private readonly FlyCamera _camera;
    private readonly InteriorController? _interiors;

    public Navigator(GameServices services, WorldRoot world, FlyCamera camera, TeleportCatalog catalog, InteriorController? interiors)
    {
        _services = services;
        _world = world;
        _camera = camera;
        _interiors = interiors;
        Catalog = catalog;
    }

    /// <summary>
    /// The main scene's navigator: the full catalogue (<see cref="TeleportCatalog.Default"/>: ACE's places plus the
    /// curated dungeons and interiors; ACE's indoor places classified by their cell's seen_outside flag, as the position
    /// HUD names them) and the interiors controller the interiors track installed (null with <c>--interiors=off</c>).
    /// </summary>
    public static Navigator Create(Node main, GameServices services, WorldRoot world, FlyCamera camera)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        var catalog = TeleportCatalog.Default();
        if (services.Dats is { } dats)
        {
            catalog = catalog.ClassifyIndoor(cellId =>
            {
                try
                {
                    return EnvCellData.Read(dats, cellId)?.SeenOutside;
                }
                catch (InvalidDataException)
                {
                    return null;
                }
            });
        }

        return new Navigator(services, world, camera, catalog, main.GetNodeOrNull<InteriorController>("Interiors"));
    }

    public TeleportCatalog Catalog { get; }

    /// <summary>The interiors controller (null without interiors).</summary>
    public InteriorController? Interiors => _interiors;

    /// <summary>The last destination jumped to (for the panel).</summary>
    public string? LastJump { get; private set; }

    /// <summary>The EnvCell the viewer is in (the interiors' cell tracker), or 0 outdoors or without interiors.</summary>
    public uint ViewerCellId => _interiors?.Tracker is { } tracker ? tracker.CellId : 0u;

    /// <summary>Ground height at a position, or null (retail TerrainSampler, on demand outside streamed landblocks).</summary>
    public double? GroundAt(GlobalPosition position) => _services.Ground.TryGetHeight(position, out var height) ? height : null;

    /// <summary>Jumps to a destination (indoor ones through the interiors, see the class remarks).</summary>
    public void Teleport(TeleportDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Kind == DestinationKind.Indoor)
        {
            PlaceIndoors(destination.Location, destination.Name);
            return;
        }

        Teleport(destination.Location, destination.Name);
    }

    /// <summary>Jumps to an outdoor location (start view, or standing in walk mode).</summary>
    public void Teleport(WorldLocation location, string label)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (AcCoordinates.IsEnvCell(location.Position.CellId))
        {
            PlaceIndoors(location, label);
            return;
        }

        // With --offline-player the camera is the player's: the player goes to the place (indoor places are placed by the interiors).
        PlayerController.Current?.Teleport(OfflinePlayerSetup.SpawnOf(_services, location));
        var spot = CameraPlacement.Spot(location, GroundAt);
        var pose = _camera.WalkMode
            ? CameraPlacement.Standing(spot, location.HeadingDegrees, GroundAt, FlyCamera.EyeHeight)
            : CameraPlacement.Overview(spot, location.HeadingDegrees, GroundAt);
        Place(pose, label);
    }

    /// <summary>
    /// Stands on the floor of an EnvCell location (feet at its origin, eyes <see cref="FlyCamera.EyeHeight"/> above),
    /// facing its heading, with the interiors told the cell (<see cref="InteriorController.PlaceCamera"/>).
    /// </summary>
    public void PlaceIndoors(WorldLocation location, string label)
    {
        ArgumentNullException.ThrowIfNull(location);
        LastJump = label;
        if (_interiors is null)
        {
            Log.Warn("nav", "indoor destination without the interiors (--interiors=off): standing at the cell position, nothing is drawn there",
                ("destination", label), ("cell", $"0x{location.Position.CellId:X8}"));
            var pose = CameraPlacement.Standing(location.Global, location.HeadingDegrees, _ => null, FlyCamera.EyeHeight);
            _world.Teleport(pose.Eye);
            _camera.PlaceAt(pose.Eye, pose.HeadingDegrees, pose.PitchDegrees);
            return;
        }

        _interiors.PlaceCamera(location, teleport: true);
        Log.Info("nav", "teleport", ("to", label), ("cell", $"0x{location.Position.CellId:X8}"), ("eye", _camera.WorldPosition), ("heading", location.HeadingDegrees), ("indoor", true));
    }

    /// <summary>Jumps to the cell named by retail map coordinates, keeping the camera's heading.</summary>
    public bool TeleportToMap(MapCoordinates coordinates)
    {
        if (!MapCoordinateParser.TryGetCellCentre(coordinates, out var centre))
        {
            return false;
        }

        Teleport(new WorldLocation(coordinates.ToString(), centre, false, _camera.Heading), coordinates.ToString());
        return true;
    }

    /// <summary>Puts the camera at an exact outdoor pose (recentring the world there); the viewer is outdoors.</summary>
    public void Place(CameraPose pose, string label)
    {
        _world.Teleport(pose.Eye);
        _camera.PlaceAt(pose.Eye, pose.HeadingDegrees, pose.PitchDegrees);

        // The tracker would otherwise keep a stale EnvCell until it noticed that cell's landblock unload.
        _interiors?.PlaceViewer(0, pose.Eye);
        LastJump = label;
        var map = AcCoordinates.TryGetMapCoordinates(pose.Eye, out var coordinates) ? coordinates.ToString() : "-";
        Log.Info("nav", "teleport", ("to", label), ("eye", pose.Eye), ("map", map), ("heading", pose.HeadingDegrees), ("pitch", pose.PitchDegrees));
    }
}

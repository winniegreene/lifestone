using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>
/// A building doorway the viewer looks into this frame (retail <c>PView::DrawPortal</c>), in its landblock's local
/// frame: its plane and the cone from the eye through it.
/// </summary>
/// <param name="Coord">The building's landblock.</param>
/// <param name="Plane">The doorway polygon's plane (landblock space).</param>
/// <param name="Cone">Planes through the eye and the doorway's edges, positive inside (landblock space).</param>
/// <param name="DistanceSquared">Squared distance from the eye to the doorway's nearest corner (the nearest doorways
/// get the rows of the doorway-plane table first, <see cref="DoorwayRows"/>).</param>
public sealed record DoorwayView(LandblockCoord Coord, CellPlane Plane, IReadOnlyList<CellPlane> Cone, float DistanceSquared = 0f);

/// <summary>What the interiors draw this frame.</summary>
/// <param name="VisibleCells">Full ids of the EnvCells to draw (every other loaded cell is hidden).</param>
/// <param name="ViewerCells">The cells of the viewer's own portal walk (a subset of <paramref name="VisibleCells"/>;
/// empty outdoors). Retail draws them after clearing Z (<c>PView::DrawCells</c>).</param>
/// <param name="Doorways">The building doorways looked into this frame.</param>
/// <param name="CellDoorways">For the cells seen through doorways: the indices into <paramref name="Doorways"/> (at most
/// <see cref="InteriorVisibility.MaxDoorwaysPerCell"/>) whose cones reach the cell.</param>
/// <param name="LandscapeVisible">Draw the outdoor world (terrain, outdoor objects, sky): the viewer is outdoors, or an
/// outside portal is in view from its cell (retail <c>PView::DrawCells</c> draws <c>LScape::draw</c> only when
/// <c>outside_view</c> is not empty).</param>
/// <param name="IndoorAmbient">The world ambient is retail's indoor 0.2 white: the viewer is in an EnvCell that is not
/// seen_outside (<c>CellManager::ChangePosition</c>).</param>
/// <param name="ViewerCellId">The viewer's EnvCell, or 0 outdoors.</param>
/// <param name="Walks">Portal walks run (one from the viewer's cell, one per building looked into).</param>
/// <param name="DoorwaysInView">Building doorways that passed the side and frustum tests.</param>
/// <param name="Milliseconds">Time spent computing this view.</param>
public sealed record InteriorView(
    IReadOnlySet<uint> VisibleCells,
    IReadOnlySet<uint> ViewerCells,
    IReadOnlyList<DoorwayView> Doorways,
    IReadOnlyDictionary<uint, int[]> CellDoorways,
    bool LandscapeVisible,
    bool IndoorAmbient,
    uint ViewerCellId,
    int Walks,
    int DoorwaysInView,
    double Milliseconds)
{
    /// <summary>Outdoors with nothing indoor in view.</summary>
    public static readonly InteriorView Outdoors = new(
        new HashSet<uint>(), new HashSet<uint>(), [], new Dictionary<uint, int[]>(), true, false, 0, 0, 0, 0);
}

/// <summary>Tuning of <see cref="InteriorVisibility"/>.</summary>
/// <param name="BuildingRings">Look into the buildings of landblocks at most this many rings (Chebyshev) from the
/// eye's landblock. Retail looks into every building it draws (<c>RenderDeviceD3D::DrawBuilding</c> →
/// <c>PView::DrawPortal</c>); a doorway two landblocks away covers a few pixels, so Lifestone stops there.</param>
/// <param name="MaxDoorwayDistance">Skip doorways farther than this (metres).</param>
public sealed record InteriorVisibilitySettings(int BuildingRings = 2, float MaxDoorwayDistance = 400f)
{
    public static readonly InteriorVisibilitySettings Default = new();
}

/// <summary>
/// Retail portal visibility over the loaded interiors (docs/interiors.md §3): which EnvCells to draw and whether to
/// draw the landscape, from the viewer's cell, the eye and the camera frustum.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Viewer in an EnvCell</b> (<c>PView::DrawInside</c>): <see cref="PortalVisibility.FromInside"/> from that
/// cell, limited to its stab list, clipped by the frustum. The landscape is drawn when an outside portal is in view
/// (<see cref="CellVisibility.OutsideVisible"/>); the buildings seen through it are then looked into as from outdoors
/// (retail's <c>LScape::draw</c> draws them with their <c>DrawPortal</c> views).</item>
/// <item><b>Viewer outdoors</b> (<c>LScape::draw</c>): every building in range is looked into through its doorways
/// (<see cref="PortalVisibility.FromOutside"/>: <c>PView::DrawPortal</c> with the doorway's stab list). Each visible
/// doorway is reported with its cone (<see cref="DoorwayView"/>), and every cell seen from outdoors is tagged with the
/// doorways whose cones reach its bounds, so the renderer can clip it to them and reset depth behind them as retail
/// does (<c>D3DPolyRender::DrawPortalPolyInternal(polygon, zClear = 1)</c>).</item>
/// </list>
/// <para>Retail clips the landscape and the outdoor objects to the outside views and the cells to their portal views in
/// screen space. Lifestone shows or hides whole cells and the whole landscape, and the renderer approximates the
/// clipping with depth (docs/interiors.md §3.4).</para>
/// </remarks>
public static class InteriorVisibility
{
    /// <summary>A cell seen from outdoors is clipped to at most this many doorway cones.</summary>
    public const int MaxDoorwaysPerCell = 2;

    /// <summary>Computes the view.</summary>
    /// <param name="registry">Loaded interiors.</param>
    /// <param name="tracker">The viewer's cell.</param>
    /// <param name="eye">The eye (global metres).</param>
    /// <param name="frustum">The camera's view pyramid (AC axes).</param>
    /// <param name="settings">Ranges; null for the defaults.</param>
    public static InteriorView Compute(InteriorRegistry registry, CellTracker tracker, GlobalPosition eye, ViewFrustum frustum, InteriorVisibilitySettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(tracker);
        settings ??= InteriorVisibilitySettings.Default;
        var start = Stopwatch.GetTimestamp();
        var visible = new HashSet<uint>();
        var viewerCells = new HashSet<uint>();
        var doorways = new List<DoorwayView>();
        var cellDoorways = new Dictionary<uint, int[]>();
        var walks = 0;
        var doorwaysInView = 0;
        var landscape = true;
        var indoorAmbient = false;

        if (tracker.IsIndoors && tracker.Cell is { } viewer)
        {
            indoorAmbient = !viewer.SeenOutside;
            var block = registry.ForCell(viewer.Id);
            if (block?.Cells.GetCell(viewer.Id) is not null)
            {
                var local = block.ToLocal(eye);
                var walk = PortalVisibility.FromInside(block.Cells, viewer.Id, local, frustum.PlanesAt(local));
                walks++;
                foreach (var cell in walk.Cells)
                {
                    visible.Add(cell.Cell.Id);
                    viewerCells.Add(cell.Cell.Id);
                }

                landscape = walk.OutsideVisible;
            }
            else
            {
                // The cell's landblock is not streamed yet: draw nothing indoor, and no landscape from a dungeon cell.
                landscape = viewer.SeenOutside;
            }
        }

        if (landscape)
        {
            LookIntoBuildings(registry, eye, frustum, settings, viewerCells, visible, doorways, cellDoorways, ref walks, ref doorwaysInView);
        }

        return new InteriorView(
            visible,
            viewerCells,
            doorways,
            cellDoorways,
            landscape,
            indoorAmbient,
            tracker.CellId,
            walks,
            doorwaysInView,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private static void LookIntoBuildings(
        InteriorRegistry registry,
        GlobalPosition eye,
        ViewFrustum frustum,
        InteriorVisibilitySettings settings,
        HashSet<uint> viewerCells,
        HashSet<uint> visible,
        List<DoorwayView> doorways,
        Dictionary<uint, int[]> cellDoorways,
        ref int walks,
        ref int doorwaysInView)
    {
        var eyeBlock = AcCoordinates.ClampedLandblockAt(eye);
        var maxDistanceSq = settings.MaxDoorwayDistance * settings.MaxDoorwayDistance;
        var inView = new List<int>();
        foreach (var block in registry.All)
        {
            if (block.Buildings.Count == 0
                || Math.Max(Math.Abs(block.Coord.X - eyeBlock.X), Math.Abs(block.Coord.Y - eyeBlock.Y)) > settings.BuildingRings)
            {
                continue;
            }

            var local = block.ToLocal(eye);
            CellPlane[]? planes = null;
            foreach (var building in block.Buildings)
            {
                // The doorways retail would look through (PView::ConstructView(CBldPortal*): eye on the outer side), near
                // enough and in front of the camera, with what of them is inside the frustum.
                inView.Clear();
                foreach (var doorway in building.Doorways)
                {
                    if (!doorway.CanLookIn(local))
                    {
                        continue;
                    }

                    var distanceSq = NearestDistanceSquared(doorway.Vertices, local);
                    if (distanceSq > maxDistanceSq || !InFront(doorway.Vertices, local, frustum))
                    {
                        continue;
                    }

                    planes ??= frustum.PlanesAt(local);
                    if (ViewVolume.ThroughPortal(local, doorway.Vertices, new ViewVolume(planes)) is null)
                    {
                        continue;
                    }

                    inView.Add(doorways.Count);
                    doorways.Add(new DoorwayView(block.Coord, doorway.Plane, ViewVolume.Through(local, doorway.Vertices).Planes, distanceSq));
                    doorwaysInView++;
                }

                if (inView.Count == 0)
                {
                    continue;
                }

                planes ??= frustum.PlanesAt(local);
                var walk = PortalVisibility.FromOutside(block.Cells, building, local, planes);
                walks++;
                foreach (var cell in walk.Cells)
                {
                    var id = cell.Cell.Id;
                    if (viewerCells.Contains(id))
                    {
                        continue; // seen from inside already
                    }

                    visible.Add(id);
                    if (!block.TryGetCellBounds(id, out var bounds))
                    {
                        continue;
                    }

                    // At most MaxDoorwaysPerCell (2) indices, kept without a per-cell list.
                    var first = -1;
                    var second = -1;
                    if (cellDoorways.TryGetValue(id, out var previous))
                    {
                        first = previous[0];
                        second = previous.Length > 1 ? previous[1] : -1;
                    }

                    foreach (var index in inView)
                    {
                        if (second >= 0)
                        {
                            break;
                        }

                        if (index != first && ConeTouchesBox(doorways[index].Cone, bounds))
                        {
                            if (first < 0)
                            {
                                first = index;
                            }
                            else
                            {
                                second = index;
                            }
                        }
                    }

                    if (first < 0)
                    {
                        first = inView[0]; // rounding: the walk reached it through one of these doorways
                    }

                    if (previous is null || previous[0] != first || (second >= 0 && (previous.Length < 2 || previous[1] != second)))
                    {
                        cellDoorways[id] = second >= 0 ? [first, second] : [first];
                    }
                }
            }
        }
    }

    /// <summary>Conservative cone/box test: false only when the box lies wholly outside one of the cone's planes.</summary>
    public static bool ConeTouchesBox(IReadOnlyList<CellPlane> cone, Lifestone.Data.Geometry.BoundingBox box)
    {
        ArgumentNullException.ThrowIfNull(cone);
        foreach (var plane in cone)
        {
            // The box corner farthest along the plane normal.
            var p = new Vector3(
                plane.Normal.X >= 0f ? box.Max.X : box.Min.X,
                plane.Normal.Y >= 0f ? box.Max.Y : box.Min.Y,
                plane.Normal.Z >= 0f ? box.Max.Z : box.Min.Z);
            if (plane.Distance(p) < 0f)
            {
                return false;
            }
        }

        return true;
    }

    private static float NearestDistanceSquared(IReadOnlyList<Vector3> polygon, Vector3 eye)
    {
        var best = float.MaxValue;
        foreach (var corner in polygon)
        {
            best = Math.Min(best, Vector3.DistanceSquared(corner, eye));
        }

        return best;
    }

    private static bool InFront(IReadOnlyList<Vector3> polygon, Vector3 eye, ViewFrustum frustum)
    {
        foreach (var corner in polygon)
        {
            if (Vector3.Dot(corner - eye, frustum.Forward) > 0f)
            {
                return true;
            }
        }

        return false;
    }
}

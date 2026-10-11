using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Player;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.UI;

/// <summary>
/// The navigation and UX wiring of the main scene, called by the sky track's <c>SkyTrack.Attach</c> (which owns the
/// panels' canvas layer): the interiors-aware <see cref="Navigator"/>, the viewer's cell for the camera and the
/// position HUD, the About panel (F1), the teleport tour harness, and hiding every HUD for captures.
/// </summary>
public static class NavigationUi
{
    /// <summary>
    /// Creates the main scene's navigator (<see cref="Navigator.Create"/>) and gives the camera (and through it the
    /// position HUD) the interiors' cell tracker, so fly mode keeps above the ground outdoors while cells below the
    /// ground stay reachable (<see cref="Lifestone.Client.Navigation.FlyGroundClearance"/>) and the HUD shows the
    /// dungeon's own cell.
    /// </summary>
    public static Navigator CreateNavigator(Node main, GameServices services, WorldRoot world, FlyCamera camera)
    {
        var navigator = Navigator.Create(main, services, world, camera);
        if (navigator.Interiors is { } interiors)
        {
            camera.ViewerCell = new TrackerCell(interiors);
        }

        return navigator;
    }

    /// <summary>
    /// Adds the About panel with its button next to the teleport button when the tool panels are shown
    /// (<paramref name="showUi"/>), and the teleport tour when <c>--teleport-tour</c> asks for it.
    /// </summary>
    public static void AttachExtras(Node main, GameServices services, WorldRoot world, FlyCamera camera, Navigator navigator, bool showUi)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (showUi)
        {
            // Beside "Teleport [T]" (TeleportPanel.OpenButton at 12, 120, about 80 px wide).
            AboutPanel.Attach(main, withButton: true, new Vector2(100, 120));
        }

        var errors = new List<string>();
        var tour = TeleportTourRunner.Requested(OS.GetCmdlineUserArgs(), errors);
        foreach (var error in errors)
        {
            Log.Warn("nav", "bad argument", ("problem", error));
        }

        if (tour is { } requested)
        {
            var runner = new TeleportTourRunner { Name = "TeleportTour" };
            main.AddChild(runner);
            runner.Initialize(services, world, camera, navigator, requested.Laps, requested.Stops);
        }
    }

    /// <summary>
    /// Shows or hides every HUD and panel (each <see cref="CanvasLayer"/> in the scene tree: the core HUD, the
    /// interiors' and objects' status lines, the tool panels, the About panel), for captures such as the golden
    /// viewpoints. Layers hidden here are shown again by a later call with <c>true</c>; layers hidden by someone else
    /// stay hidden.
    /// </summary>
    public static void SetHudVisible(Node anyNode, bool visible)
    {
        ArgumentNullException.ThrowIfNull(anyNode);
        var root = anyNode.GetTree().Root;
        if (!visible)
        {
            foreach (var layer in root.FindChildren("*", nameof(CanvasLayer), recursive: true, owned: false).OfType<CanvasLayer>())
            {
                if (layer.Visible)
                {
                    layer.Visible = false;
                    HiddenByUs.Add(layer);
                }
            }

            return;
        }

        foreach (var layer in HiddenByUs)
        {
            if (GodotObject.IsInstanceValid(layer))
            {
                layer.Visible = true;
            }
        }

        HiddenByUs.Clear();
    }

    private static readonly List<CanvasLayer> HiddenByUs = new();

    /// <summary>The interiors' cell tracker as the camera's <see cref="IViewerCell"/>.</summary>
    private sealed class TrackerCell(InteriorController interiors) : IViewerCell
    {
        public uint CellId => interiors.Tracker is { } tracker ? tracker.CellId : 0u;

        public bool? SeenOutside => interiors.Tracker?.Cell?.SeenOutside;

        public double? CellTop =>
            interiors.Tracker is { IsIndoors: true, CellId: var id }
            && interiors.Registry.ForCell(id) is { } block
            && block.TryGetCellBounds(id, out var bounds)
                ? block.Origin.Z + bounds.Max.Z
                : null;

        /// <summary>The tracker's own first tests (<c>CellTracker.UpdateIndoors</c>): the cell, its portal neighbours, its stab list.</summary>
        public bool StaysIndoors(GlobalPosition eye)
        {
            if (interiors.Tracker is not { IsIndoors: true } tracker)
            {
                return false;
            }

            if (interiors.Registry.ForCell(tracker.CellId) is not { } block)
            {
                return true; // the cell's landblock is not streamed yet (just placed): the tracker keeps the cell too
            }

            if (block.Cells.GetCell(tracker.CellId) is not { } cell)
            {
                return false;
            }

            var local = block.ToLocal(eye);
            if (block.Contains(cell, local))
            {
                return true;
            }

            foreach (var portal in cell.Portals)
            {
                if (!portal.LeadsOutside && block.Cells.GetCell(portal.OtherCellId) is { } other && block.Contains(other, local))
                {
                    return true;
                }
            }

            foreach (var id in cell.VisibleCellIds)
            {
                if (block.Cells.GetCell(id) is { } visible && block.Contains(visible, local))
                {
                    return true;
                }
            }

            return false;
        }

        public bool NearCells(LandblockCoord landblock, GlobalPosition eye, float margin) =>
            interiors.Registry.Get(landblock) is { } block && block.BoundsContain(block.ToLocal(eye), margin);
    }
}

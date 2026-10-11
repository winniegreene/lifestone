using System.Globalization;
using Godot;

namespace Lifestone.Game.World.Interiors;

/// <summary>Bottom-left status lines: the viewer's cell and what the portal walk drew.</summary>
public partial class InteriorHud : Label
{
    private InteriorController? _owner;
    private double _since = double.MaxValue;

    public void Initialize(InteriorController owner) => _owner = owner;

    public override void _Ready()
    {
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = 12f;
        OffsetTop = -64f;
        OffsetRight = 1000f;
        OffsetBottom = -10f;
        AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.75f));
        AddThemeColorOverride("font_outline_color", Colors.Black);
        AddThemeConstantOverride("outline_size", 4);
        AddThemeFontSizeOverride("font_size", 14);
    }

    public override void _Process(double delta)
    {
        _since += delta;
        if (_since < 0.1 || _owner?.Tracker is not { } tracker)
        {
            return;
        }

        _since = 0;
        var o = _owner;
        var view = o.View;
        var where = !tracker.IsIndoors
            ? "Outdoors"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"EnvCell 0x{tracker.CellId:X8}{(tracker.Cell is { } c ? $"  env 0x{c.EnvironmentId:X8}[{c.CellStructureIndex}]  {(c.SeenOutside ? "seen outside" : "dungeon cell: ambient 0.2")}" : string.Empty)}");
        Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{where}\nInteriors: {view.VisibleCells.Count} cells drawn of {o.AttachedCellNodes} meshes ({o.AttachedCells} of {o.ExpectedCells} meshed, {o.LoadedCells} as data, {o.Registry.Count} landblocks, {o.Lights} lights, {o.OwnMeshBytes / (1024.0 * 1024.0):0.0} MB meshes)  " +
            $"walks {view.Walks}  {view.Milliseconds:0.00} ms (max {o.MaxViewMilliseconds:0.00})  landscape {(view.LandscapeVisible ? "on" : "off")}" +
            $"{(o.UnclippedCells > 0 ? $"  {o.UnclippedCells} cells unclipped (doorway table full)" : string.Empty)}" +
            $"{(o.ShowAllCells ? "  [F9: all cells]" : string.Empty)}   F7: next interior");
    }
}

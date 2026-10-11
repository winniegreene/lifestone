using System.Globalization;
using Godot;

namespace Lifestone.Game.World.Objects;

/// <summary>
/// One HUD line under the perf overlay with the objects system's counters (F3 toggles it with the perf overlay).
/// </summary>
public partial class ObjectsOverlay : Label
{
    private readonly ObjectsSystem? _system;
    private double _sinceUpdate = double.MaxValue;

    public ObjectsOverlay()
    {
    }

    public ObjectsOverlay(ObjectsSystem system) => _system = system;

    public override void _Ready()
    {
        Name = "ObjectsOverlay";
        AnchorLeft = 1f;
        AnchorRight = 1f;
        OffsetLeft = -1012f;
        OffsetRight = -12f;
        OffsetTop = 292f;
        OffsetBottom = 352f;
        GrowHorizontal = GrowDirection.Begin;
        HorizontalAlignment = HorizontalAlignment.Right;
        AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.75f));
        AddThemeColorOverride("font_outline_color", Colors.Black);
        AddThemeConstantOverride("outline_size", 4);
        AddThemeFontSizeOverride("font_size", 13);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 })
        {
            Visible = !Visible;
        }
    }

    public override void _Process(double delta)
    {
        _sinceUpdate += delta;
        if (_system is null || !Visible || _sinceUpdate < 0.25)
        {
            return;
        }

        _sinceUpdate = 0;
        var s = _system.Stats;
        var ci = CultureInfo.InvariantCulture;
        Text = string.Create(
            ci,
            $"objects ({_system.Radius} ring{(_system.Radius == 1 ? string.Empty : "s")} around {s.Center?.ToString() ?? "-"}): active {s.Active}  attaching {s.Attaching}  decoding {s.Decoding}  prepared {s.Prepared}  preparing {s.Preparing}   " +
            $"statics {s.Statics}  buildings {s.Buildings}  scenery {s.Scenery}") + "\n" + string.Create(
            ci,
            $"parts {s.Parts} (drawn {s.DrawnParts}, facing {s.FacingParts})   batches {s.Nodes} (shown {s.VisibleNodes}, {s.VisibleInstances} instances)   meshes {s.Meshes}   " +
            $"lod {s.LastUpdateMs:0.00} ms (max {s.MaxUpdateMs:0.00})   attach step max {s.MaxAttachStepMs:0.00} ms") + LivingLine(ci);
    }

    /// <summary>The living statics' line (docs/objects.md §1.7), when they run.</summary>
    private static string LivingLine(CultureInfo ci)
    {
        if (LivingStaticsSystem.Instance is not { Enabled: true } living)
        {
            return string.Empty;
        }

        var l = living.Stats;
        return "\n" + string.Create(
            ci,
            $"living statics {l.Statics} ({l.Sources} sources, {l.Stepped} stepped)   emitters {l.Emitters} ({l.DegradedEmitters} out of range)   particles {l.DrawnParticles} drawn of {l.LiveParticles}, " +
            $"{l.Batches} batches   step {l.StepMs:0.00} ms (max {l.MaxStepMs:0.00})   draw {l.DrawMs:0.00} ms (max {l.MaxDrawMs:0.00})");
    }
}

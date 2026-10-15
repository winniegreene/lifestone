using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;
using Lifestone.Data.World;

namespace Lifestone.Client.RetailUi.Radar;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmRadarUI::UseTime 0x004D98A0, ::UpdateCompassTokens 0x004D9060, ::DrawObjects 0x004D9380,
// ::AddObject 0x004D9A00, ::PostInit 0x004D9D30, ::DrawChildren 0x004D9720; CPlayerSystem::GetRadarRadius 0x0055D880.
/// <summary>One blip the radar shows: where, what shape, in which colour, and whether it is the selected object's.</summary>
/// <param name="Guid">The object.</param>
/// <param name="X">The pixel's x in the radar window.</param>
/// <param name="Y">The pixel's y in the radar window.</param>
/// <param name="Shape">The shape.</param>
/// <param name="Argb">The colour drawn, after the intensity (0xAARRGGBB).</param>
/// <param name="Selected">True when the blip also gets the selection mark.</param>
public readonly record struct RadarBlip(uint Guid, int X, int Y, RadarBlipShape Shape, uint Argb, bool Selected);

/// <summary>
/// The in-game radar (<c>gmRadarUI</c>, window 0x100006D2; docs/retail-ui.md section 21.4). Every <see cref="UpdateInterval"/> seconds (<c>UseTime</c>, 11.6096 0x004DA500: the next update is the
/// time plus the double at 0x007BEA58, 0.025) it takes the range from the player's cell (75 m outdoors, 25 m indoors; <c>CPlayerSystem::GetRadarRadius</c> 0x0055E5A0), turns the four
/// compass tokens round their ring to the player's heading (<c>UpdateCompassTokens</c> 0x004D9CC0) and collects the blips (<c>DrawObjects</c> 0x004D9FE0 over the tracked objects of
/// <c>AddObject</c> 0x004DA660); the element then draws them over its children and puts the player's bright green marker at the centre.
/// </summary>
/// <remarks>
/// Retail marks the radar dirty on every update; this one only when a blip, the heading or the selection changed, which paints the same pixels. Not built (named TODOs):
/// <c>g4-radar-tooltip</c> (the name of the blip nearest the mouse), <c>g4-radar-coords</c> (the coordinate fields, <c>UpdateCoordinates</c>, the option <c>CoordinatesOnRadar</c>),
/// <c>g4-radar-lock</c> (the lock button 0x10000619, <c>UpdateLockedStatus</c>), <c>g4-radar-look</c> (a changed look, <c>UpdateRadarLook</c>, is read from the object each update here).
/// </remarks>
public sealed class GameRadar
{
    /// <summary>The seconds between updates (the double added to <c>m_ttNextUpdateTime</c>; D55 read, docs/research/retail-addresses-m2.md section 1.6).</summary>
    public const double UpdateInterval = 0.025;

    /// <summary>The radar's drag button (hidden by <c>PostInit</c>).</summary>
    public const uint DragButtonId = 0x100006A3;

    private readonly UiElementManager _manager;
    private readonly IGameSessionView _session;
    private readonly List<RadarBlip> _blips = [];
    private readonly List<RadarBlip> _next = [];
    private readonly UiElement?[] _tokens = new UiElement?[4]; // north, east, south, west
    private readonly double[] _magnitudes = new double[4];
    private double _nextUpdate;
    private float _playerHeading;
    private bool _drawn;

    /// <summary>The radar of <paramref name="window"/> (0x100006D2), or an inert one when the window is missing.</summary>
    public GameRadar(UiElementManager manager, IGameSessionView session, UiElement? window)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(session);
        _manager = manager;
        _session = session;
        Window = window;
        PlayerProvider = () => session.State.Self.Position;
        if (window is null)
        {
            return;
        }

        // gmRadarUI::PostInit (11.6096 0x004DA990).
        if (window.InqProperty(UiPropertyIds.RadarCenterPoint, out var centre) && centre is UiStructProperty members
            && members.Members.TryGetValue(UiPropertyIds.RadarX, out var cx) && cx is UiIntegerProperty x
            && members.Members.TryGetValue(UiPropertyIds.RadarY, out var cy) && cy is UiIntegerProperty y)
        {
            CentreX = x.Value;
            CentreY = y.Value;
        }

        if (window.TryGetIntegerProperty(UiPropertyIds.RadarRadius, out var radius))
        {
            Radius = radius;
        }

        window.GetChildRecursive(DragButtonId)?.SetVisible(false);
        ReadOnlySpan<uint> tokenProperties = [UiPropertyIds.RadarNorthToken, UiPropertyIds.RadarEastToken, UiPropertyIds.RadarSouthToken, UiPropertyIds.RadarWestToken];
        for (var i = 0; i < tokenProperties.Length; i++)
        {
            if (window.TryGetEnumProperty(tokenProperties[i], out var id) && window.GetChildRecursive(id) is { } token)
            {
                _tokens[i] = token;
                var box = token.Box; // the distance from the radar's centre to the token's centre (each token keeps its own orbit)
                var dx = CentreX - ((box.X0 + box.X1) * 0.5);
                var dy = CentreY - ((box.Y0 + box.Y1) * 0.5);
                _magnitudes[i] = Math.Sqrt((dy * dy) + (dx * dx));
            }
        }

        if (window is UiRadarElement element)
        {
            element.Painter = Draw;
        }
        else
        {
            manager.LogOnce("radar:class", LogSeverity.Warning, $"gmRadarUI: the radar window {window.ElementId:X8} is not the radar element class; it shows no blips (TODO(g4-radar-class))");
        }
    }

    /// <summary>The radar window, or null.</summary>
    public UiElement? Window { get; }

    /// <summary>The radar's radius in pixels (property 0x1000002D).</summary>
    public int Radius { get; }

    /// <summary>The centre's x in the window (property 0x1000002E's member 0x1000002F).</summary>
    public int CentreX { get; }

    /// <summary>The centre's y in the window.</summary>
    public int CentreY { get; }

    /// <summary>The range in metres the last update used (<c>m_fRadarRange</c>); 75 until the first update (the constructor's <c>RADAR_OUTDOOR_RADIUS</c>).</summary>
    public double Range { get; private set; } = RadarRules.OutdoorRange;

    /// <summary>The blips the last update found, in the order the session lists the objects.</summary>
    public IReadOnlyList<RadarBlip> Blips => _blips;

    /// <summary>The player's heading, in degrees, the compass tokens were last placed for (<c>m_fPlayerHeading</c>).</summary>
    public float PlayerHeading => _playerHeading;

    /// <summary>How many updates ran (the timer firing with a player present).</summary>
    public int Updates { get; private set; }

    /// <summary>The time (the UI clock, seconds) the next update is due (<c>m_ttNextUpdateTime</c>).</summary>
    public double NextUpdateTime => _nextUpdate;

    /// <summary>True when the client's radar is blanked (<c>ClientUISystem::m_bRadarBlank</c>): no blip is drawn, the player's marker still is.</summary>
    public bool Blank { get; set; }

    /// <summary>The fellowship and the allegiance (none in M2).</summary>
    public IRadarRelations Relations { get; set; } = IRadarRelations.None;

    /// <summary>The player's position: the session's last server position unless the host gives the live one (the local physics body).</summary>
    public Func<CellFrame?> PlayerProvider { get; set; }

    /// <summary>The selected object's id, 0 for none (<c>ACCWeenieObject::selectedID</c>); the play mode gives its <c>ObjectSelection</c>.</summary>
    public Func<uint>? SelectedProvider { get; set; }

    /// <summary>
    /// <c>gmRadarUI::UseTime</c>: when the clock has reached the next update time and the player exists, sets the range, turns the compass tokens, collects the blips and repaints the radar if
    /// anything changed; the next update is the time plus <see cref="UpdateInterval"/> (not the old due time plus it).
    /// </summary>
    /// <returns>True when an update ran.</returns>
    public bool Update()
    {
        if (Window is null)
        {
            return false;
        }

        var now = _manager.Now;
        if (now < _nextUpdate)
        {
            return false;
        }

        _nextUpdate = now + UpdateInterval;
        if (PlayerProvider() is not { } player)
        {
            return false;
        }

        Updates++;
        Range = RadarRules.RangeFor(player);
        UpdateCompassTokens(player);
        Collect(player, SelectedProvider?.Invoke() ?? 0);
        if (!_drawn || !_blips.SequenceEqual(_next))
        {
            _blips.Clear();
            _blips.AddRange(_next);
            _drawn = true;
            Window.MakeRootDirtyHere();
        }

        return true;
    }

    /// <summary>
    /// <c>gmRadarUI::UpdateCompassTokens</c> (0x004D9CC0): when the heading changed, each token goes to its place on its ring: the angle is the heading in radians plus 180 degrees for north,
    /// 90 for east, 0 for south and 270 for west; the token's centre is (sin * orbit + centre x, cos * orbit + centre y) with y down, so north sits at the top while the player faces north.
    /// </summary>
    private void UpdateCompassTokens(in CellFrame player)
    {
        var heading = (float)player.HeadingDegrees;
        if (heading == _playerHeading)
        {
            return;
        }

        _playerHeading = heading;
        var radians = heading * 0.017453292;
        ReadOnlySpan<double> offsets = [Math.PI, 1.5707964, 0.0, 4.712389]; // north, east, south, west
        for (var i = 0; i < _tokens.Length; i++)
        {
            if (_tokens[i] is not { } token)
            {
                continue;
            }

            var angle = radians + offsets[i];
            var x = (Math.Sin(angle) * _magnitudes[i]) + CentreX - (token.Box.Width * 0.5);
            var y = (Math.Cos(angle) * _magnitudes[i]) + CentreY - (token.Box.Height * 0.5);
            token.MoveTo((int)x, (int)y);
        }
    }

    // gmRadarUI::AddObject and DrawObjects: an object is tracked when it is not the player, has a description that is not hidden from the UI, a radar behaviour of 2, 3 or 4 and a shape; it is drawn when
    // it has a position, is inside the range less one metre and lands on the radar.
    private void Collect(in CellFrame player, uint selected)
    {
        _next.Clear();
        var objects = _session.State.Objects;
        var selfGuid = _session.State.Self.Guid;
        objects.TryGet(selfGuid, out var self);
        foreach (var obj in objects.All)
        {
            if (obj.IsSelf || obj.Guid == selfGuid || obj.Position is not { } position || obj.Parent is not null)
            {
                continue;
            }

            var weenie = new RadarObjectDesc(obj.DescriptionFlags, obj.ItemType, obj.RadarBlipColor, obj.RadarBehavior);
            if (RadarRules.IsUiHidden(weenie) || !RadarRules.IsShowable(weenie))
            {
                continue;
            }

            var shape = RadarRules.ShapeOf(obj.Guid, weenie, self is null ? null : new RadarObjectDesc(self.DescriptionFlags, self.ItemType, self.RadarBlipColor, self.RadarBehavior), Relations);
            if (shape == RadarBlipShape.Undefined)
            {
                continue;
            }

            var local = RadarRules.ToPlayerSpace(player, position);
            if (!RadarRules.TryPlace(local, Range, Radius, CentreX, CentreY, out var x, out var y))
            {
                continue;
            }

            var color = RadarRules.Scaled(RadarRules.ColorOf(obj.Guid, weenie, Relations), RadarRules.IntensityOf(local.Z));
            _next.Add(new RadarBlip(obj.Guid, x, y, shape, color.ToColor32(), obj.Guid == selected));
        }
    }

    // gmRadarUI::DrawChildren's part after UIRegion::DrawChildren (0x004DA380): the blips (DrawObjects, DrawBlip), then the player's marker in bright green.
    private void Draw(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
    {
        if (!Blank)
        {
            foreach (var blip in _blips)
            {
                foreach (var (dx, dy) in RadarShapes.OffsetsOf(blip.Shape))
                {
                    Plot(painter, self, clip, dirty, blip.X + dx, blip.Y + dy, blip.Argb);
                }

                if (blip.Selected)
                {
                    foreach (var (dx, dy) in RadarShapes.SelectedOffsets)
                    {
                        Plot(painter, self, clip, dirty, blip.X + dx, blip.Y + dy, blip.Argb);
                    }
                }
            }
        }

        var marker = RadarPalette.BrightGreen.ToColor32();
        foreach (var (dx, dy) in RadarShapes.PlayerOffsets)
        {
            Plot(painter, self, clip, dirty, CentreX + dx, CentreY + dy, marker);
        }
    }

    private static void Plot(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty, int x, int y, uint argb)
    {
        var px = self.X0 + x;
        var py = self.Y0 + y;
        if (px < clip.X0 || px > clip.X1 || py < clip.Y0 || py > clip.Y1)
        {
            return;
        }

        foreach (var piece in dirty)
        {
            if (px >= piece.X0 && px <= piece.X1 && py >= piece.Y0 && py <= piece.Y1)
            {
                painter.Fill(new UiBox(px, py, px, py), argb);
                return;
            }
        }
    }
}

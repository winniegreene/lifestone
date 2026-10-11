using System.Globalization;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Navigation;

/// <summary>
/// The position lines of the HUD: where the viewer is, as a landblock, a cell and landblock-local coordinates, plus
/// retail map coordinates outdoors.
/// </summary>
/// <remarks>
/// <para><b>Outdoors</b> the cell is the 24 m land cell under the eye and the map coordinates are the ones retail's
/// radar shows for it (<c>CPlayerSystem::InqPlayerCoords</c>, acclient.c:398264, via <see cref="AcCoordinates"/>).</para>
/// <para><b>Indoors</b> (the interiors' cell tracker reports an EnvCell) the landblock and the local frame are the
/// cell's own: dungeon cells often lie outside their landblock's 192 m square (the Town Network's drop point is at
/// y = -60), so converting the global position would name a neighbouring landblock. Retail shows no map coordinates
/// in any EnvCell: <c>InqPlayerCoords</c> succeeds only when <c>LandDefs::gid_to_lcoord</c> does
/// (acclient.c:209521), and that refuses cell indices of 0x100 and above, so the radar hides them. Neither does the
/// HUD; it says "indoors" instead.</para>
/// </remarks>
public static class PositionReadout
{
    /// <summary>
    /// The lines for an eye position and the viewer's cell (an EnvCell id from the cell tracker, or 0 / a land cell
    /// outdoors).
    /// </summary>
    /// <param name="eye">The eye (global metres).</param>
    /// <param name="viewerCellId">The tracker's cell: an EnvCell id indoors, 0 outdoors.</param>
    /// <param name="seenOutside">For an EnvCell: whether retail treats it as seen from outside (a building interior); null
    /// when unknown.</param>
    public static IReadOnlyList<string> Describe(GlobalPosition eye, uint viewerCellId, bool? seenOutside = null)
    {
        var lines = new List<string>(2);
        if (AcCoordinates.IsEnvCell(viewerCellId))
        {
            var landblock = LandblockCoord.FromId(viewerCellId);
            var origin = AcCoordinates.LandblockOrigin(landblock);
            var where = seenOutside switch
            {
                true => "indoors (building)",
                false => "indoors (dungeon)",
                null => "indoors",
            };
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Landblock 0x{viewerCellId >> 16:X4}   cell 0x{viewerCellId:X8}   {where}"));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Local  x {eye.X - origin.X:0.00}  y {eye.Y - origin.Y:0.00}  z {eye.Z - origin.Z:0.00}"));
            return lines;
        }

        if (AcCoordinates.TryGetOutdoorCell(eye, out var cell))
        {
            var map = AcCoordinates.TryGetMapCoordinates(cell.CellId, out var coordinates) ? coordinates.ToString() : "-";
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Landblock 0x{cell.CellId >> 16:X4}   cell 0x{cell.CellId:X8}   {map}"));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Local  x {cell.Local.X:0.00}  y {cell.Local.Y:0.00}  z {cell.Local.Z:0.00}"));
        }
        else
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Off the map  {eye}"));
        }

        return lines;
    }
}

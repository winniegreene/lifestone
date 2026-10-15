using Lifestone.Client.RetailUi.Radar;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Selection;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmToolbarUI::HandleSelectionChanged 0x004BF380; VividTargetIndicator::SetSelected 0x004F5CE0;
// gmRadarUI::GetBlipColor 0x004D76F0; CM_Combat::Event_QueryHealth 0x006A9B40.
/// <summary>
/// Which selected objects the shell asks the server about and draws the indicator for (docs/retail-ui.md section 21.7). The rules are retail's
/// <c>gmToolbarUI::HandleSelectionChanged</c> (11.6096 0x004C0010) and <c>VividTargetIndicator::SetSelected</c> (0x004F6980); the virtual calls that decide
/// "attackable" are not named in the decompile, so the creature test is the object description's attackable bit, the same bit the radar uses.
/// </summary>
public static class SelectionRules
{
    /// <summary>
    /// True when selecting <paramref name="obj"/> asks the server for its health (<c>CM_Combat::Event_QueryHealth</c>, 11.6096 0x006AAAA0): an object whose description carries the
    /// attackable bit (<see cref="RadarRules.AttackableBit"/>). ACE answers only for a creature, so another object would time out; <c>TODO(g5b-attackable)</c>: retail's own test (a few
    /// virtual calls whose names are lost) is approximated by the description bit.
    /// </summary>
    public static bool QueriesHealth(WorldObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return !obj.IsSelf && (obj.DescriptionFlags & RadarRules.AttackableBit) != 0;
    }

    /// <summary>
    /// True when the vivid target indicator is drawn for <paramref name="obj"/>: <c>VividTargetIndicator::SetSelected</c> (11.6096 0x004F6980) clears the target for the player itself and for an
    /// object the player holds or wields. (It also clears it for a weenie of one more kind, field value 2, whose meaning is not read: <c>TODO(g5b-vti-ignore)</c>.)
    /// </summary>
    public static bool ShowsIndicator(WorldObject obj, uint playerGuid)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.IsSelf || obj.Guid == playerGuid)
        {
            return false;
        }

        return obj.ContainerId != playerGuid && obj.WielderId != playerGuid;
    }

    /// <summary>The indicator colour: the target radar colour (<c>gmRadarUI::GetBlipColor</c>, 11.6096 0x004D8350, through <see cref="RadarRules.ColorOf"/>).</summary>
    public static UiRgbaColor ColorOf(WorldObject obj, IRadarRelations relations)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(relations);
        return RadarRules.ColorOf(obj.Guid, new RadarObjectDesc(obj.DescriptionFlags, obj.ItemType, obj.RadarBlipColor, obj.RadarBehavior), relations);
    }
}

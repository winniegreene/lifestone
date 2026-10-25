using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The creature layer's feed from the client's object table (nav-creature-avoid): every hostile creature the client sees, as the radar paints
/// one red (<c>RadarRules</c>: the creature item type, the attackable bit, not a player) and the census counts one (not a vendor): its CreateObject's
/// ItemType has the creature bit (0x10), its ObjectDescriptionFlags the attackable bit (0x10) and neither the player (0x8) nor the vendor (0x200)
/// bit, it has a position, and it is not known to be dead (a health fraction above zero or none yet; its forward motion is not Dead). Each is a
/// disc of <see cref="NavCreatures.AggroRadius"/> over its <see cref="CreatureAwareness"/>. ACE alerts only an idle creature without a
/// move-exclude tolerance (<c>AlertMonster</c>, Creature_Combat.cs:1121-1151 @95ef426d); the client sees neither, so every hostile creature counts.
/// Allocates nothing once the list has grown.
/// </summary>
/// <param name="objects">The session's object table.</param>
/// <param name="self">The player's id (never on the layer).</param>
/// <param name="log">The log (the first creature's <c>TODO(nav-creature-radius)</c> line), or null.</param>
public sealed class HostileCreatureFeed(WorldObjectTable objects, uint self, ILogSink? log = null) : INavCreatureFeed
{
    /// <summary>The ItemType bit of a creature (ACE <c>ItemType.Creature</c>).</summary>
    public const uint CreatureItemType = 0x10;

    /// <summary>The ObjectDescriptionFlags bit of an attackable object (<c>BF_ATTACKABLE</c>).</summary>
    public const uint AttackableFlag = 0x10;

    /// <summary>The ObjectDescriptionFlags bit of a vendor (<c>BF_VENDOR</c>).</summary>
    public const uint VendorFlag = 0x200;

    private readonly WorldObjectTable _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private bool _radiusLogged;

    /// <summary>Whether <paramref name="o"/> is a hostile creature as the feed counts one (alive, placed, not the player).</summary>
    /// <param name="o">The object.</param>
    /// <param name="self">The player's id.</param>
    public static bool IsHostile(WorldObject o, uint self)
    {
        ArgumentNullException.ThrowIfNull(o);
        var flags = o.DescriptionFlags;
        return o.Guid != self && !o.IsSelf && o.Position is not null
            && (o.ItemType & CreatureItemType) != 0 && (flags & AttackableFlag) != 0 && (flags & (WorldObjectTable.PlayerFlag | VendorFlag)) == 0
            && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord };
    }

    /// <inheritdoc/>
    public void Fill(Vector2 from, float maxMetres, List<NavCreature> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        float max2 = maxMetres * maxMetres;
        foreach (var o in _objects.All)
        {
            if (!IsHostile(o, self))
            {
                continue;
            }

            var centre = NavMap.FromCellFrame(o.Position!.Value).Global;
            if (Vector2.DistanceSquared(centre, from) > max2)
            {
                continue;
            }

            into.Add(new NavCreature(o.Guid, centre, NavCreatures.AggroRadius(CreatureAwareness.MetresOf(o.Weenie.WeenieClassId))));
            if (!_radiusLogged)
            {
                _radiusLogged = true;
                log?.Warn("wayfinding", "TODO(nav-creature-radius): a creature's own radius is not read; its aggro disc assumes 1 m", new LogField("creature", o.Name), new LogField("assumed_m", NavCreatures.UnknownCreatureRadiusMetres));
            }
        }
    }
}

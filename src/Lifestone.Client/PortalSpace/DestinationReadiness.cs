using System.Globalization;
using Lifestone.Client.Objects;
using Lifestone.Client.Streaming;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.PortalSpace;

/// <summary>Options of the destination-readiness rule.</summary>
/// <param name="OutdoorRingForSeenOutsideCells">
/// An interior cell that can see outside also waits for the outdoor 3×3 around it (D29; retail's
/// <c>CEnvCell::PreFetchCells</c> also waits for the landscape there).
/// </param>
public readonly record struct ReadinessPolicy(bool OutdoorRingForSeenOutsideCells)
{
    /// <summary>The M2 default: seen-outside interiors wait for the outdoor ring too (D29).</summary>
    public static ReadinessPolicy Default => new(OutdoorRingForSeenOutsideCells: true);
}

/// <summary>Which piece of the destination a <see cref="ReadinessReport"/> names.</summary>
public enum ReadinessItem : byte
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>A landblock's terrain (the id is the landblock's cell id 0xXXYYFFFF).</summary>
    Terrain,

    /// <summary>A landblock's static objects and scenery.</summary>
    Objects,

    /// <summary>A landblock's collision.</summary>
    Collision,

    /// <summary>An EnvCell's geometry.</summary>
    Cell,

    /// <summary>An EnvCell's visible-cell list (not known yet).</summary>
    CellList,

    /// <summary>The player's cell id is not a valid cell (0, or off the map).</summary>
    InvalidCell,
}

/// <summary>
/// The answer of <see cref="DestinationReadiness.Evaluate"/>: whether every piece the destination needs has settled,
/// with counts and the first piece still waiting or failed. A struct; <see cref="Describe"/> builds text only on demand.
/// </summary>
/// <remarks>
/// <see cref="IsReady"/> is exactly <see cref="Waiting"/> == 0. An invalid player cell counts as one waiting piece of kind
/// <see cref="ReadinessItem.InvalidCell"/>, so it is never ready.
/// </remarks>
public readonly record struct ReadinessReport
{
    /// <summary>Every needed piece has settled (attached, failed or disabled).</summary>
    public bool IsReady { get; init; }

    /// <summary>Pieces still <see cref="AttachState.Pending"/>.</summary>
    public int Waiting { get; init; }

    /// <summary>Pieces that <see cref="AttachState.Failed"/> (settled, but reported).</summary>
    public int Failed { get; init; }

    /// <summary>Pieces whose layer is <see cref="AttachState.Disabled"/> in this host.</summary>
    public int Disabled { get; init; }

    /// <summary>The pieces evaluated (the attached ones are those neither waiting, failed nor disabled).</summary>
    public int Checked { get; init; }

    /// <summary>The kind of the first piece still waiting, or <see cref="ReadinessItem.None"/>.</summary>
    public ReadinessItem FirstWaitingKind { get; init; }

    /// <summary>The id of the first piece still waiting (a landblock or cell id), or 0.</summary>
    public uint FirstWaitingId { get; init; }

    /// <summary>The kind of the first failed piece, or <see cref="ReadinessItem.None"/>.</summary>
    public ReadinessItem FirstFailedKind { get; init; }

    /// <summary>The id of the first failed piece, or 0.</summary>
    public uint FirstFailedId { get; init; }

    /// <summary>The pieces attached.</summary>
    public int Attached => Checked - Waiting - Failed - Disabled;

    /// <summary>A one-line description for the log (allocates; call it only to log).</summary>
    public string Describe()
    {
        if (FirstWaitingKind == ReadinessItem.InvalidCell)
        {
            return string.Create(CultureInfo.InvariantCulture, $"not ready: 0x{FirstWaitingId:X8} is not a valid player cell");
        }

        var head = IsReady
            ? string.Create(CultureInfo.InvariantCulture, $"ready: {Checked} pieces settled")
            : string.Create(CultureInfo.InvariantCulture, $"waiting for {Waiting} of {Checked} pieces, first {FirstWaitingKind} 0x{FirstWaitingId:X8}");
        var settled = string.Create(CultureInfo.InvariantCulture, $"; attached {Attached}, failed {Failed}, disabled {Disabled}");
        return FirstFailedKind == ReadinessItem.None
            ? head + settled
            : head + settled + string.Create(CultureInfo.InvariantCulture, $", first failed {FirstFailedKind} 0x{FirstFailedId:X8}");
    }
}

/// <summary>
/// Lifestone's stand-in for retail's "the cell manager is not blocking for cells" (<c>SmartBox::UseTime</c>, 11.4186
/// 0x00455410, 11.6096 0x004554B0): the destination is ready when every piece the player's cell needs has settled
/// (m2-log-in.md §4.7). Retail's flag is a data-prefetch state; this attach-based rule is a Lifestone adaptation.
/// </summary>
/// <remarks>
/// <para><b>Outdoors</b> (an outdoor cell, low word 0x0001 to 0x0040): the player's landblock and its 8 neighbours, the
/// 3×3 that has objects in retail (<see cref="ObjectRings.RetailRadius"/>), clipped at the map's edge
/// (<see cref="LandDefs.IsOnMap"/>: landblock indices 0 to 254). Each landblock needs its terrain, objects and collision.
/// The order is the centre first, then the neighbours from south-west to north-east; per landblock, terrain, objects,
/// collision.</para>
/// <para><b>Indoors</b> (an EnvCell, low word 0x0100 to 0xFFFD): the cell, then every cell of its visible-cell list
/// (<see cref="IWorldAttachments.VisibleCells"/>), need their geometry; a list that is not known yet is one waiting piece
/// (<see cref="ReadinessItem.CellList"/>). When the cell sees outside and
/// <see cref="ReadinessPolicy.OutdoorRingForSeenOutsideCells"/> is set (D29), the outdoor 3×3 around its landblock is
/// needed too. Retail's <c>CellManager::PreFetchCells</c> (11.4186 0x00455820, 11.6096 0x00455940) and
/// <c>CEnvCell::PreFetchCells</c> (11.4186 0x0052C460, 11.6096 0x0052D070) are the prefetch this replaces.</para>
/// <para><b>Settling.</b> Failed and disabled pieces count as settled (<see cref="AttachStateExtensions.IsSettled"/>), so a
/// bad landblock or a layer the host lacks can never trap the tunnel; they are counted, and the first failed one is
/// named. Server objects are never waited for: they arrive during the 2 s hold, as in retail.</para>
/// <para><b>Not a cell.</b> Cell id 0 (no position yet), an outdoor cell of a landblock off the map, and an id that is
/// neither an outdoor cell nor an EnvCell are never ready: one waiting piece of kind
/// <see cref="ReadinessItem.InvalidCell"/>. An EnvCell's landblock need not be on the map (a dungeon is not on the
/// landscape); it then has no outdoor ring.</para>
/// <para>Static and allocation-free (no LINQ, no enumerators): one call per frame.</para>
/// </remarks>
public static class DestinationReadiness
{
    /// <summary>Evaluates the destination around <paramref name="playerCellId"/> against <paramref name="world"/>.</summary>
    public static ReadinessReport Evaluate(uint playerCellId, IWorldAttachments world, in ReadinessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(world);
        var landblock = LandblockCoord.FromId(playerCellId);
        var tally = default(Tally);
        if (LandDefs.IsOutdoorCell(playerCellId))
        {
            if (!LandDefs.IsOnMap(landblock))
            {
                return Invalid(playerCellId);
            }

            Ring(ref tally, landblock, world);
        }
        else if (LandDefs.IsEnvCell(playerCellId))
        {
            tally.Add(ReadinessItem.Cell, playerCellId, world.Cell(playerCellId));
            var visible = world.VisibleCells(playerCellId);
            if (visible is null)
            {
                tally.Add(ReadinessItem.CellList, playerCellId, AttachState.Pending);
            }
            else
            {
                for (var i = 0; i < visible.Count; i++)
                {
                    var cell = visible[i];
                    tally.Add(ReadinessItem.Cell, cell, world.Cell(cell));
                }
            }

            if (policy.OutdoorRingForSeenOutsideCells && LandDefs.IsOnMap(landblock) && world.SeenOutside(playerCellId))
            {
                Ring(ref tally, landblock, world);
            }
        }
        else
        {
            return Invalid(playerCellId);
        }

        return tally.Report();
    }

    /// <summary>The 3×3 around <paramref name="centre"/>, clipped at the map's edge: the centre first.</summary>
    private static void Ring(ref Tally tally, LandblockCoord centre, IWorldAttachments world)
    {
        Landblock(ref tally, centre, world);
        for (var dy = -ObjectRings.RetailRadius; dy <= ObjectRings.RetailRadius; dy++)
        {
            for (var dx = -ObjectRings.RetailRadius; dx <= ObjectRings.RetailRadius; dx++)
            {
                if ((dx != 0 || dy != 0) && centre.Offset(dx, dy) is { } neighbour && LandDefs.IsOnMap(neighbour))
                {
                    Landblock(ref tally, neighbour, world);
                }
            }
        }
    }

    private static void Landblock(ref Tally tally, LandblockCoord landblock, IWorldAttachments world)
    {
        var id = landblock.LandBlockId;
        tally.Add(ReadinessItem.Terrain, id, world.Terrain(landblock));
        tally.Add(ReadinessItem.Objects, id, world.Objects(landblock));
        tally.Add(ReadinessItem.Collision, id, world.Collision(landblock));
    }

    private static ReadinessReport Invalid(uint playerCellId) => new()
    {
        IsReady = false,
        Waiting = 1,
        Checked = 1,
        FirstWaitingKind = ReadinessItem.InvalidCell,
        FirstWaitingId = playerCellId,
    };

    /// <summary>The running counts of one evaluation.</summary>
    private struct Tally
    {
        private int _checked;
        private int _waiting;
        private int _failed;
        private int _disabled;
        private ReadinessItem _firstWaitingKind;
        private uint _firstWaitingId;
        private ReadinessItem _firstFailedKind;
        private uint _firstFailedId;

        public void Add(ReadinessItem kind, uint id, AttachState state)
        {
            _checked++;
            switch (state)
            {
                case AttachState.Attached:
                    break;
                case AttachState.Failed:
                    if (_failed++ == 0)
                    {
                        _firstFailedKind = kind;
                        _firstFailedId = id;
                    }

                    break;
                case AttachState.Disabled:
                    _disabled++;
                    break;
                default:
                    // Pending, and any value outside the enum: not settled.
                    if (_waiting++ == 0)
                    {
                        _firstWaitingKind = kind;
                        _firstWaitingId = id;
                    }

                    break;
            }
        }

        public readonly ReadinessReport Report() => new()
        {
            IsReady = _waiting == 0,
            Waiting = _waiting,
            Failed = _failed,
            Disabled = _disabled,
            Checked = _checked,
            FirstWaitingKind = _firstWaitingKind,
            FirstWaitingId = _firstWaitingId,
            FirstFailedKind = _firstFailedKind,
            FirstFailedId = _firstFailedId,
        };
    }
}

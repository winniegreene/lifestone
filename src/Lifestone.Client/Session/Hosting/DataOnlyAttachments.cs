using Lifestone.Client.Diagnostics;
using Lifestone.Client.Objects;
using Lifestone.Client.Streaming;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Session.Hosting;

/// <summary>
/// The world as a data-only host has it (step ST15; m2-plan §4.5): the headless host draws nothing and places no objects,
/// so what portal space waits for at a destination (<c>DestinationReadiness</c>) is the dat data the destination needs,
/// decoded. The host calls <see cref="Focus"/> with the player's cell once per frame; the queries then read what was
/// decoded and never start a load.
/// </summary>
/// <remarks>
/// <para><b>Answers.</b></para>
/// <list type="bullet">
/// <item><b>Terrain</b> of a landblock: <see cref="AttachState.Attached"/> once its LandBlock record (0xXXYYFFFF) decoded,
/// <see cref="AttachState.Failed"/> (with a WARNING) when the cell dat has none or it is malformed,
/// <see cref="AttachState.Pending"/> before <see cref="Focus"/> reached it.</item>
/// <item><b>Objects</b>: <see cref="AttachState.Disabled"/>: a data-only host has no objects layer.</item>
/// <item><b>Collision</b>: <see cref="AttachState.Disabled"/> until physics' collision streamer lands in the headless host
/// (PH7; docs/physics.md).</item>
/// <item><b>Cell</b>, <b>VisibleCells</b>, <b>SeenOutside</b> of an EnvCell: from its decoded EnvCell record
/// (<see cref="EnvCellData"/>); a missing or malformed record is Failed, with a WARNING, and has no visible cells.</item>
/// </list>
/// <para><b>Focus</b> decodes, for an outdoor cell, the terrain of its landblock and the eight around it (retail's 3×3);
/// for an EnvCell, the cell and every cell it can see, plus the 3×3 terrain when it can see outside. Decoded pieces are
/// kept for the host's life. Game thread only.</para>
/// </remarks>
public sealed class DataOnlyAttachments : IWorldAttachments
{
    private readonly ICellSource _source;
    private readonly ILogSink _log;
    private readonly Dictionary<uint, AttachState> _terrain = [];
    private readonly Dictionary<uint, EnvCellInfo?> _cells = [];
    private uint _focus;

    /// <summary>Attachments decoded from <paramref name="dats"/>.</summary>
    /// <param name="dats">The opened dats.</param>
    /// <param name="log">Where a missing or malformed record is reported (category <c>session</c>).</param>
    public DataOnlyAttachments(DatSet dats, ILogSink log)
        : this(new DatCellSource(dats ?? throw new ArgumentNullException(nameof(dats))), log)
    {
    }

    /// <summary>Attachments over any source of cell data (tests).</summary>
    internal DataOnlyAttachments(ICellSource source, ILogSink log)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The cell <see cref="Focus"/> last decoded for, or 0.</summary>
    public uint FocusCell => _focus;

    /// <summary>Landblocks and EnvCells decoded so far.</summary>
    public int Decoded => _terrain.Count + _cells.Count;

    /// <summary>
    /// Decodes what a destination at <paramref name="cellId"/> needs (see the remarks); nothing when the cell is the same
    /// as last time, or 0 (no player yet). Cheap to call every frame.
    /// </summary>
    /// <param name="cellId">The player's cell.</param>
    public void Focus(uint cellId)
    {
        if (cellId == _focus || cellId == 0)
        {
            return;
        }

        _focus = cellId;
        var landblock = LandblockCoord.FromId(cellId);
        if (LandDefs.IsOutdoorCell(cellId))
        {
            Ring(landblock);
        }
        else if (LandDefs.IsEnvCell(cellId))
        {
            var info = DecodeCell(cellId);
            if (info is { } cell)
            {
                foreach (var visible in cell.VisibleCells)
                {
                    DecodeCell(visible);
                }

                if (cell.SeenOutside)
                {
                    Ring(landblock);
                }
            }
        }
    }

    /// <inheritdoc/>
    public AttachState Terrain(LandblockCoord landblock) =>
        _terrain.TryGetValue(landblock.LandBlockId, out var state) ? state : AttachState.Pending;

    /// <inheritdoc/>
    public AttachState Objects(LandblockCoord landblock) => AttachState.Disabled;

    /// <inheritdoc/>
    public AttachState Collision(LandblockCoord landblock) => AttachState.Disabled;

    /// <inheritdoc/>
    public AttachState Cell(uint envCellId) =>
        _cells.TryGetValue(envCellId, out var info) ? (info is null ? AttachState.Failed : AttachState.Attached) : AttachState.Pending;

    /// <inheritdoc/>
    public IReadOnlyList<uint>? VisibleCells(uint envCellId) =>
        _cells.TryGetValue(envCellId, out var info) ? info?.VisibleCells ?? [] : null;

    /// <inheritdoc/>
    public bool SeenOutside(uint envCellId) => _cells.TryGetValue(envCellId, out var info) && info is { SeenOutside: true };

    private void Ring(LandblockCoord centre)
    {
        for (var dy = -ObjectRings.RetailRadius; dy <= ObjectRings.RetailRadius; dy++)
        {
            for (var dx = -ObjectRings.RetailRadius; dx <= ObjectRings.RetailRadius; dx++)
            {
                if (centre.Offset(dx, dy) is { } landblock && LandDefs.IsOnMap(landblock))
                {
                    DecodeTerrain(landblock);
                }
            }
        }
    }

    private void DecodeTerrain(LandblockCoord landblock)
    {
        var id = landblock.LandBlockId;
        if (_terrain.ContainsKey(id))
        {
            return;
        }

        AttachState state;
        try
        {
            state = _source.HasLandblock(landblock) ? AttachState.Attached : AttachState.Failed;
            if (state == AttachState.Failed)
            {
                _log.Warn(SessionLog.Category, $"data-only world: landblock 0x{id:X8} is not in the cell dat");
            }
        }
        catch (InvalidDataException e)
        {
            state = AttachState.Failed;
            _log.Warn(SessionLog.Category, $"data-only world: landblock 0x{id:X8} is malformed: {e.Message}");
        }

        _terrain[id] = state;
    }

    private EnvCellInfo? DecodeCell(uint cellId)
    {
        if (_cells.TryGetValue(cellId, out var known))
        {
            return known;
        }

        EnvCellInfo? info;
        try
        {
            info = _source.EnvCell(cellId);
            if (info is null)
            {
                _log.Warn(SessionLog.Category, $"data-only world: EnvCell 0x{cellId:X8} is not in the cell dat");
            }
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
        {
            info = null;
            _log.Warn(SessionLog.Category, $"data-only world: EnvCell 0x{cellId:X8} is malformed: {e.Message}");
        }

        _cells[cellId] = info;
        return info;
    }
}

/// <summary>What <see cref="DataOnlyAttachments"/> needs of an EnvCell.</summary>
/// <param name="VisibleCells">The full ids of the cells it can see.</param>
/// <param name="SeenOutside">True when the landscape can be seen from it.</param>
internal readonly record struct EnvCellInfo(IReadOnlyList<uint> VisibleCells, bool SeenOutside);

/// <summary>Where <see cref="DataOnlyAttachments"/> reads cells (the dats, or a test's table).</summary>
internal interface ICellSource
{
    /// <summary>True when the cell dat has the landblock's LandBlock record. Throws <see cref="InvalidDataException"/> when it is malformed.</summary>
    bool HasLandblock(LandblockCoord landblock);

    /// <summary>The EnvCell, or null when the cell dat has none. Throws <see cref="InvalidDataException"/> when it is malformed.</summary>
    EnvCellInfo? EnvCell(uint cellId);
}

/// <summary>The dats as a <see cref="ICellSource"/>.</summary>
internal sealed class DatCellSource(DatSet dats) : ICellSource
{
    /// <inheritdoc/>
    public bool HasLandblock(LandblockCoord landblock) => dats.GetLandBlock(landblock) is not null;

    /// <inheritdoc/>
    public EnvCellInfo? EnvCell(uint cellId) =>
        EnvCellData.Read(dats, cellId) is { } cell ? new EnvCellInfo(cell.VisibleCellIds, cell.SeenOutside) : null;
}

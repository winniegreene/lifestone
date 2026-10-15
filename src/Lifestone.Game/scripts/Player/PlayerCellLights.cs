using Lifestone.Client.Interiors;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;

namespace Lifestone.Game.Player;

/// <summary>
/// The static lights of the cell the player stands in (retail's <c>Render::world_lights.sorted_static_lights</c> while the viewer
/// is in that cell: <see cref="CellStaticLighting.SelectFor"/>), read from the dats on a worker when the player's cell changes.
/// The interiors read the same lights for the cells they mesh, for the viewer; the player may be in a cell the viewer is not
/// (a free camera outside), so it reads its own.
/// </summary>
/// <remarks>
/// Until the worker has answered, <see cref="Get"/> returns no lights: the player shows its ambient only for the first frames in a
/// new cell, never a stall. A cell that cannot be read is logged once and has no lights. Main thread only.
/// </remarks>
public sealed class PlayerCellLights
{
    private readonly GameServices _services;
    private uint _readyCell;
    private IReadOnlyList<CellLight> _lights = [];
    private Task<IReadOnlyList<CellLight>>? _pending;
    private uint _pendingCell;

    public PlayerCellLights(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>The cell the lights in <see cref="Get"/> belong to (0 before the first answer).</summary>
    public uint ReadyCell => _readyCell;

    /// <summary>True while a worker is reading a cell's lights.</summary>
    public bool Reading => _pending is { IsCompleted: false };

    /// <summary>The lights of EnvCell <paramref name="cellId"/> (nearest to the cell first), or none until they are read.</summary>
    public IReadOnlyList<CellLight> Get(uint cellId)
    {
        if (cellId == _readyCell)
        {
            return _lights;
        }

        if (_pending is { IsCompleted: true } done && _pendingCell == cellId)
        {
            _pending = null;
            if (done.IsCompletedSuccessfully)
            {
                _lights = done.Result;
                _readyCell = cellId;
                Log.Debug("play", "the player's cell lights are read", ("cell", $"0x{cellId:X8}"), ("lights", _lights.Count));
                return _lights;
            }

            Log.Error("play", "reading the player's cell lights failed; the player is lit by the ambient only", ("cell", $"0x{cellId:X8}"), ("error", done.Exception?.GetBaseException().Message));
            _lights = [];
            _readyCell = cellId;
            return _lights;
        }

        if (_pending is null || _pendingCell != cellId)
        {
            _pendingCell = cellId;
            _pending = _services.Workers.RunSync(_ => Read(_services, cellId));
        }

        return [];
    }

    /// <summary>Worker: the light list of <paramref name="cellId"/> as retail keeps it while the viewer is there.</summary>
    internal static IReadOnlyList<CellLight> Read(GameServices services, uint cellId)
    {
        var dats = services.Dats!;
        if (EnvCellData.Read(dats, cellId) is not { } cell)
        {
            Log.Error("play", "the player's cell does not exist in the dats; no cell lights", ("cell", $"0x{cellId:X8}"));
            return [];
        }

        var byCell = new Dictionary<uint, IReadOnlyList<CellLight>>();
        IReadOnlyList<CellLight> LightsOf(uint id)
        {
            if (!byCell.TryGetValue(id, out var lights))
            {
                lights = EnvCellData.Read(dats, id) is { } c ? CellStaticLighting.LightsOf(c, setup => services.Setups.Get(setup)?.Lights) : [];
                byCell.Add(id, lights);
            }

            return lights;
        }

        return CellStaticLighting.SelectFor(cell, LightsOf);
    }
}

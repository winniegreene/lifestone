using Lifestone.Client.Interiors;
using Lifestone.Game.Core;
using Lifestone.Game.Player;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// The static lights of the EnvCells the server's objects stand in (what <see cref="PlayerCellLights"/> keeps for the one cell the player stands in): read from
/// the dats on a worker the first time a cell is asked for, and kept. Until a cell's answer arrives <see cref="TryGet"/> says so (null), and <see cref="Version"/>
/// rises when one lands, so a caller that wrote a row without those lights knows to write it again. Main thread only (the read itself runs on a worker).
/// </summary>
public sealed class ServerObjectCellLights
{
    private readonly GameServices _services;
    private readonly Dictionary<uint, Entry> _cells = new();

    public ServerObjectCellLights(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>Rises whenever a cell's lights arrive.</summary>
    public long Version { get; private set; }

    /// <summary>Cells whose lights are being read.</summary>
    public int Reading { get; private set; }

    /// <summary>The lights of EnvCell <paramref name="cellId"/> (nearest to the cell first), or null while the worker reads them.</summary>
    public IReadOnlyList<CellLight>? TryGet(uint cellId)
    {
        if (_cells.TryGetValue(cellId, out var entry))
        {
            if (entry.Lights is not null)
            {
                return entry.Lights;
            }

            if (entry.Task is { IsCompleted: true } done)
            {
                entry.Task = null;
                Reading--;
                if (done.IsCompletedSuccessfully)
                {
                    entry.Lights = done.Result;
                }
                else
                {
                    Log.Error("serverobjects", "reading a cell's lights failed; objects there are lit by the ambient only", ("cell", $"0x{cellId:X8}"), ("error", done.Exception?.GetBaseException().Message));
                    entry.Lights = [];
                }

                Version++;
                return entry.Lights;
            }

            return null;
        }

        _cells.Add(cellId, new Entry { Task = _services.Workers.RunSync(_ => PlayerCellLights.Read(_services, cellId)) });
        Reading++;
        return null;
    }

    private sealed class Entry
    {
        public Task<IReadOnlyList<CellLight>>? Task;
        public IReadOnlyList<CellLight>? Lights;
    }
}

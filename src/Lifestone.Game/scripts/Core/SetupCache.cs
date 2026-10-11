using System.Collections.Concurrent;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;

namespace Lifestone.Game.Core;

/// <summary>
/// Thread-safe cache of <see cref="ModelAssembly"/> (Setup 0x02 or bare GfxObj 0x01) by id, built at most once each.
/// Unbounded: the end-of-retail portal has 5,935 Setups and an assembly is a few hundred bytes of frames.
/// </summary>
public sealed class SetupCache
{
    private readonly DatSet _dats;
    private readonly GfxObjMeshCache _meshes;
    private readonly ConcurrentDictionary<uint, Lazy<ModelAssembly?>> _assemblies = new();

    public SetupCache(DatSet dats, GfxObjMeshCache meshes)
    {
        _dats = dats;
        _meshes = meshes;
    }

    public int Count => _assemblies.Count;

    /// <summary>The assembly of a Setup or GfxObj id, or null when the dat has no such record.</summary>
    public ModelAssembly? Get(uint id) =>
        _assemblies.GetOrAdd(id, key => new Lazy<ModelAssembly?>(
            () => ModelAssembly.Load(_dats, key, _meshes),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}

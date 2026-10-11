using System.Collections.Concurrent;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using DatEmitter = DatReaderWriter.DBObjs.ParticleEmitter;
using DatScript = DatReaderWriter.DBObjs.PhysicsScript;
using DatScriptTable = DatReaderWriter.DBObjs.PhysicsScriptTable;

namespace Lifestone.Data.Effects;

/// <summary>
/// Thread-safe cache of PhysicsScripts (0x33, raw-decoded by <see cref="PhysicsScriptData.Decode"/>),
/// PhysicsScriptTables (0x34) and ParticleEmitterInfos (0x32) read from the user's dats.
/// </summary>
public sealed class DatEffectSource(DatSet dats) : IEffectSource
{
    private readonly ConcurrentDictionary<uint, PhysicsScriptData?> _scripts = new();
    private readonly ConcurrentDictionary<uint, PhysicsScriptTableData?> _tables = new();
    private readonly ConcurrentDictionary<uint, ParticleEmitterInfoData?> _emitters = new();

    public DatSet Dats { get; } = dats;

    public PhysicsScriptData? GetScript(uint scriptId) =>
        _scripts.GetOrAdd(scriptId, id => DatRaw.Portal(Dats, id) is { } raw ? PhysicsScriptData.Decode(id, raw) : null);

    public PhysicsScriptTableData? GetScriptTable(uint tableId) =>
        _tables.GetOrAdd(tableId, id => Dats.TryGetPortal<DatScriptTable>(id, out var table) ? PhysicsScriptTableData.FromDat(table) : null);

    public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) =>
        _emitters.GetOrAdd(emitterInfoId, id => Dats.TryGetPortal<DatEmitter>(id, out var e) ? ParticleEmitterInfoData.FromDat(e) : null);

    public IReadOnlyList<uint> AllScriptIds() => DatRaw.PortalIds<DatScript>(Dats);

    public IReadOnlyList<uint> AllScriptTableIds() => DatRaw.PortalIds<DatScriptTable>(Dats);

    public IReadOnlyList<uint> AllEmitterInfoIds() => DatRaw.PortalIds<DatEmitter>(Dats);
}

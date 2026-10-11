using DatReaderWriter.Enums;
using DatScriptTable = DatReaderWriter.DBObjs.PhysicsScriptTable;

namespace Lifestone.Data.Effects;

/// <summary>One (intensity threshold, PhysicsScript) pair of a PhysicsScriptTable entry (retail <c>ScriptAndModData</c>).</summary>
public readonly record struct ScriptModEntry(float Mod, uint ScriptId);

/// <summary>
/// A PhysicsScriptTable (0x34): for each <see cref="PlayScript"/>, a list of scripts keyed by intensity ("mod").
/// Decoded through DatReaderWriter (layout matches ACE <c>ACE.DatLoader/FileTypes/PhysicsScriptTable.cs</c>).
/// Objects get their table from their Setup (<c>DefaultScriptTable</c>), or — for characters and most creatures —
/// from the weenie (<c>PropertyDataId.PhysicsEffectTable</c>, sent in the server's PhysicsDesc); characters' tables are
/// in CharGen (<c>SexCG.PhysicsTable</c>).
/// </summary>
public sealed class PhysicsScriptTableData
{
    private PhysicsScriptTableData(uint id, IReadOnlyDictionary<PlayScript, IReadOnlyList<ScriptModEntry>> scripts)
    {
        Id = id;
        Scripts = scripts;
    }

    public uint Id { get; }

    /// <summary>Entries per PlayScript, in dat order (retail searches them in this order).</summary>
    public IReadOnlyDictionary<PlayScript, IReadOnlyList<ScriptModEntry>> Scripts { get; }

    public static PhysicsScriptTableData FromDat(DatScriptTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var scripts = table.ScriptTable.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<ScriptModEntry>)kv.Value.Scripts.Select(s => new ScriptModEntry(s.Mod, s.ScriptId.DataId)).ToArray());
        return new PhysicsScriptTableData(table.Id, scripts);
    }

    /// <summary>
    /// Retail <c>PhysicsScriptTable::GetScript(type, mod)</c> → <c>PhysicsScriptTableData::GetScript(mod)</c>
    /// (acclient 11.4186 PORTAL/canim/CPhysicsScriptTable.c): the first entry, in stored order, whose threshold is
    /// <c>≥ mod</c> (the scan continues while <c>mod &gt; entry.mod</c>). Returns 0 when the type is absent or
    /// <paramref name="mod"/> exceeds every threshold — retail then plays nothing.
    /// </summary>
    public uint GetScript(PlayScript type, float mod)
    {
        if (!Scripts.TryGetValue(type, out var entries))
        {
            return 0;
        }

        foreach (var entry in entries)
        {
            if (!(mod > entry.Mod))
            {
                return entry.ScriptId;
            }
        }

        return 0;
    }
}

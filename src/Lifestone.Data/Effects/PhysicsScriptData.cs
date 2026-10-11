using Lifestone.Data.Animation;

namespace Lifestone.Data.Effects;

/// <summary>One timeline entry of a PhysicsScript: a hook that fires <see cref="StartTime"/> seconds after the script starts.</summary>
public readonly record struct ScriptHookEntry(double StartTime, AnimHook Hook);

/// <summary>
/// A decoded PhysicsScript (0x33), re-expressing retail <c>PhysicsScript::UnPack</c> (acclient 11.4186
/// PORTAL/canim/CPhysicsScript.c): <c>u32 id, u32 n, n × (f64 startTime, CAnimHook)</c>, then 4-byte alignment.
/// Retail then sorts the entries with <c>qsort(PhysicsScriptData::Sort)</c> (by start time) and sets
/// <c>length</c> to the last entry's start time.
/// </summary>
/// <remarks>
/// Ties: retail's comparator never returns 0 and MSVC <c>qsort</c> is not stable, so entries with equal start times may
/// be reordered by retail in a way we do not reproduce; we sort stably (dat order kept for ties). Hooks at the same time
/// all execute in the same <c>ScriptManager::UpdateScripts</c> call, so only their relative order (random-number draw
/// order, create/destroy of the same emitter id) can differ. The exhaustive test <c>PhysicsScriptTests</c> counts the
/// scripts with equal start times (see docs/spell-visuals.md).
/// </remarks>
public sealed class PhysicsScriptData
{
    private PhysicsScriptData(uint id, IReadOnlyList<ScriptHookEntry> hooks, bool fileOrderSorted)
    {
        Id = id;
        Hooks = hooks;
        FileOrderSorted = fileOrderSorted;
        Length = hooks.Count > 0 ? hooks[^1].StartTime : 0.0;
    }

    public uint Id { get; }

    /// <summary>Entries sorted by start time.</summary>
    public IReadOnlyList<ScriptHookEntry> Hooks { get; }

    /// <summary>Retail <c>PhysicsScript::length</c>: the start time of the last (sorted) entry.</summary>
    public double Length { get; }

    /// <summary>True when the dat already stores the entries in non-decreasing start-time order.</summary>
    public bool FileOrderSorted { get; }

    public static PhysicsScriptData Decode(uint id, byte[] raw)
    {
        var r = new DatByteReader(raw, id);
        var storedId = r.ReadUInt32();
        if (storedId != id)
        {
            throw new InvalidDataException($"PhysicsScript 0x{id:X8}: record starts with id 0x{storedId:X8}.");
        }

        var count = checked((int)r.ReadUInt32());
        if (count > 10_000)
        {
            throw new InvalidDataException($"PhysicsScript 0x{id:X8}: implausible hook count {count}.");
        }

        var entries = new ScriptHookEntry[count];
        var sorted = true;
        for (var i = 0; i < count; i++)
        {
            var start = r.ReadDouble();
            entries[i] = new ScriptHookEntry(start, AnimHookDecoder.Read(r));
            if (i > 0 && start < entries[i - 1].StartTime)
            {
                sorted = false;
            }
        }

        r.Align4();
        r.RequireEnd("PhysicsScript");

        // Stable sort by start time (see remarks about retail's unstable qsort).
        var ordered = entries.Select((e, i) => (e, i)).OrderBy(x => x.e.StartTime).ThenBy(x => x.i).Select(x => x.e).ToArray();
        return new PhysicsScriptData(id, ordered, sorted);
    }
}

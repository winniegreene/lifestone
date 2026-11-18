using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Contracts;

/// <summary>
/// M6 16 E2b: the bodies a test draws, in the place of play mode's avatar set (<see cref="ILiveBodies"/>): a creature's body stands where the test puts it, whatever the server last reported. With it a test states the one
/// state the live positions exist for: a creature whose drawn body and reported place differ.
/// </summary>
internal sealed class ScriptedBodies : ILiveBodies
{
    private readonly Dictionary<uint, LiveBody> _bodies = [];

    /// <summary>A placed, drawn body of <paramref name="guid"/> at <paramref name="at"/>.</summary>
    public ScriptedBodies Stand(uint guid, CellFrame at)
    {
        _bodies[guid] = new LiveBody(at, InWorld: true, Hidden: false, ServerPositionPending: false);
        return this;
    }

    /// <summary>A placed, drawn body of <paramref name="guid"/> <paramref name="metres"/> east of <paramref name="of"/>, in its cell.</summary>
    public ScriptedBodies StandEast(uint guid, CellFrame of, float metres) => Stand(guid, East(of, metres));

    /// <summary>The place <paramref name="metres"/> east of <paramref name="of"/>, in its cell.</summary>
    public static CellFrame East(CellFrame of, float metres) => new(of.CellId, of.Frame with { Origin = of.Origin + new Vector3(metres, 0, 0) });

    /// <summary>The live positions of a session that draws these bodies.</summary>
    public LivePositions Over(SessionClock clock, RecordingLogSink? log = null) => new(this, clock, log);

    /// <inheritdoc/>
    public bool TryGetBody(uint guid, out LiveBody body) => _bodies.TryGetValue(guid, out body);
}

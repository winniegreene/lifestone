using Lifestone.Client.PortalSpace;
using Lifestone.Client.Streaming;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A world whose every piece is settled at once (terrain and cells attached, objects and collision disabled, no visible
/// cells, no outside view), for headless runs without dats; and stand-in portal-space assets (the headless host plays no
/// sound, so the ids are never looked up).
/// </summary>
internal sealed class HeadlessTestWorld : IWorldAttachments
{
    /// <summary>Stand-in UIASSET ids (no dat record is read: the headless director has no sound sink).</summary>
    public static PortalSpaceAssets Assets { get; } = new(0x02000001, 0x03000001, 0x20000001);

    /// <summary>Readiness queries answered.</summary>
    public int Queries { get; private set; }

    public AttachState Terrain(LandblockCoord landblock) => Count(AttachState.Attached);

    public AttachState Objects(LandblockCoord landblock) => Count(AttachState.Disabled);

    public AttachState Collision(LandblockCoord landblock) => Count(AttachState.Disabled);

    public AttachState Cell(uint envCellId) => Count(AttachState.Attached);

    public IReadOnlyList<uint>? VisibleCells(uint envCellId) => [];

    public bool SeenOutside(uint envCellId) => false;

    private AttachState Count(AttachState state)
    {
        Queries++;
        return state;
    }
}

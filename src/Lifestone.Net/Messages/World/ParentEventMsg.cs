namespace Lifestone.Net.Messages;

/// <summary>F749 ParentEvent (docs/protocol.md §5.3): an object is attached to a parent.</summary>
/// <param name="ParentGuid">The parent.</param>
/// <param name="ChildGuid">The child.</param>
/// <param name="ParentLocation">The parent location (holding slot).</param>
/// <param name="Placement">The child's placement.</param>
/// <param name="ParentInstanceSeq">The parent's instance sequence.</param>
/// <param name="ChildPositionSeq">The child's position sequence.</param>
public readonly record struct ParentEventMsg(
    uint ParentGuid,
    uint ChildGuid,
    int ParentLocation,
    int Placement,
    ushort ParentInstanceSeq,
    ushort ChildPositionSeq);

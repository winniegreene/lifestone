using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>F625 ObjDescEvent (docs/protocol.md §5.3): an object's appearance changed.</summary>
/// <param name="Guid">The object.</param>
/// <param name="Model">The new ModelData.</param>
/// <param name="InstanceSeq">The instance sequence.</param>
/// <param name="ObjDescSeq">The visual-description sequence.</param>
public sealed record ObjDescEventMsg(uint Guid, ModelData Model, ushort InstanceSeq, ushort ObjDescSeq);

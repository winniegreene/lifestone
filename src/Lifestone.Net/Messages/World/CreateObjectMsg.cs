using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// F745 ObjectCreate and F7DB UpdateObject (the same body; docs/protocol.md §5.3): the object's GUID, its ModelData,
/// PhysicsDesc and WeenieDesc. The dispatcher's <c>isUpdate</c> flag tells them apart; F7DB is a forced re-create.
/// </summary>
/// <param name="Guid">The object's GUID.</param>
/// <param name="Model">Its palette, texture and part changes.</param>
/// <param name="Physics">Its physics description, including the spawn position and the nine sequences.</param>
/// <param name="Weenie">Its weenie description (name, class, icon, item fields).</param>
public sealed record CreateObjectMsg(uint Guid, ModelData Model, PhysicsDesc Physics, WeenieDesc Weenie);

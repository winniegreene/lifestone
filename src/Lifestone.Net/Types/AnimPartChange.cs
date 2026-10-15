namespace Lifestone.Net.Types;

/// <summary>One part change of a <see cref="ModelData"/>: setup part <paramref name="PartIndex"/> draws GfxObj
/// <paramref name="GfxObjId"/> (a full 0x01xxxxxx id) instead of its default.</summary>
/// <param name="PartIndex">The setup part to replace.</param>
/// <param name="GfxObjId">The GfxObj to draw for it.</param>
public readonly record struct AnimPartChange(byte PartIndex, uint GfxObjId);

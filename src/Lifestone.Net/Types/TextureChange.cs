namespace Lifestone.Net.Types;

/// <summary>One texture change of a <see cref="ModelData"/>: on part <paramref name="PartIndex"/>, surface texture
/// <paramref name="OldTexture"/> is replaced by <paramref name="NewTexture"/> (full 0x05xxxxxx ids).</summary>
/// <param name="PartIndex">The setup part the change applies to.</param>
/// <param name="OldTexture">The texture to replace.</param>
/// <param name="NewTexture">Its replacement.</param>
public readonly record struct TextureChange(byte PartIndex, uint OldTexture, uint NewTexture);

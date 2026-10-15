namespace Lifestone.Net.Types;

/// <summary>
/// One subpalette swap of a <see cref="ModelData"/>: palette <paramref name="PaletteId"/> replaces a range of the base
/// palette. <paramref name="Offset"/> and <paramref name="Length"/> are in the wire's units of 8 colours, unconverted
/// (retail multiplies by 8 when it applies them).
/// </summary>
/// <param name="PaletteId">The subpalette (a full 0x04xxxxxx id).</param>
/// <param name="Offset">Start of the replaced range, in units of 8 colours.</param>
/// <param name="Length">Length of the replaced range, in units of 8 colours.</param>
public readonly record struct SubPaletteSwap(uint PaletteId, byte Offset, byte Length);

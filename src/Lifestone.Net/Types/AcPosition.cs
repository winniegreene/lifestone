using System.Numerics;

namespace Lifestone.Net.Types;

/// <summary>
/// A position as the wire carries it (docs/protocol.md §5.3 and §5.4; retail <c>Position::Pack</c>): a cell id, the
/// origin within that cell's landblock frame, and the rotation. 32 bytes on the wire: u32 cell, 3 × f32 origin, then the
/// quaternion as W, X, Y, Z.
/// </summary>
/// <param name="CellId">The full cell id (landblock in the high word, cell index in the low word).</param>
/// <param name="Origin">The position in the landblock's frame, in metres (AC axes; the conversion to Godot happens in
/// Lifestone.Game's AcToGodot only).</param>
/// <param name="Rotation">
/// The rotation exactly as received or to be sent, never normalised or validated here. <see cref="Quaternion"/> keeps
/// (X, Y, Z, W); the wire order is W, X, Y, Z, which the codecs handle.
/// </param>
public readonly record struct AcPosition(uint CellId, Vector3 Origin, Quaternion Rotation);

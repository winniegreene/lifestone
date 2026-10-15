using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Tools;

/// <summary>
/// A position as ACE writes and reads it in chat: <c>0xA9B40019 [84.000000 7.100000 94.000000] 0.996917 0.000000
/// 0.000000 -0.078459</c>, the cell, the origin in brackets and the quaternion as w, x, y, z (ACE <c>Position.ToLOCString</c>,
/// which <c>@loc</c> prints and <c>@teleloc</c> reads back; ACEmulator/ACE@95ef426d). The tools panel shows the server's
/// "where am I" this way, and a player can paste ACE's text into it. Invariant culture both ways.
/// </summary>
public static class AceLocText
{
    /// <summary><paramref name="position"/> in ACE's form, six decimals per number as ACE prints them.</summary>
    public static string Format(in CellFrame position)
    {
        var o = position.Frame.Origin;
        var q = position.Frame.Orientation;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"0x{position.CellId:X8} [{o.X:F6} {o.Y:F6} {o.Z:F6}] {q.W:F6} {q.X:F6} {q.Y:F6} {q.Z:F6}");
    }

    /// <summary>
    /// Reads ACE's form, as <c>@teleloc</c> accepts it: the cell as hexadecimal with or without <c>0x</c>, then x, y and z
    /// (the brackets optional), then optionally the quaternion as w, x, y, z (the identity when left out). Numbers are
    /// invariant-culture text; anything else is refused with a reason.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The position read (the quaternion as written, not normalised).</param>
    /// <param name="error">Why it was refused, or null.</param>
    /// <returns>True when it was read.</returns>
    public static bool TryParse(string? text, out CellFrame position, [NotNullWhen(false)] out string? error)
    {
        position = default;
        var words = (text ?? string.Empty).Replace('[', ' ').Replace(']', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is not (4 or 8))
        {
            error = "expected a cell, x y z and optionally qw qx qy qz (ACE's @loc form)";
            return false;
        }

        var cellText = words[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? words[0][2..] : words[0];
        if (cellText.Length is 0 or > 8 ||
            !uint.TryParse(cellText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var cell))
        {
            error = "the cell is not a hexadecimal id";
            return false;
        }

        Span<float> numbers = stackalloc float[7];
        numbers[3] = 1f;
        for (var i = 1; i < words.Length; i++)
        {
            if (!float.TryParse(words[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            {
                error = "number " + i.ToString(CultureInfo.InvariantCulture) + " is not a finite number";
                return false;
            }

            numbers[i - 1] = value;
        }

        position = CellFrame.FromWire(cell, new Vector3(numbers[0], numbers[1], numbers[2]), numbers[3], numbers[4], numbers[5], numbers[6]);
        error = null;
        return true;
    }
}

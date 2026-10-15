using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// The light sets of the moving objects in cells (the global sampler <c>ac_object_lights</c>,
/// <see cref="SurfaceLightingCode.ObjectLights"/>): one row of <see cref="ObjectLightSets.RowWidth"/> RGBA32F texels per object
/// (docs/render-contract.md §2.1, <see cref="SurfaceLighting.DynamicCellObject"/>). An object takes a row with
/// <see cref="Allocate"/>, writes the lights that reach it every frame, and gives the row back with <see cref="Release"/>; the
/// instance data of the object names the row (<c>INSTANCE_CUSTOM.y</c> is one plus the row, 0 for none).
/// </summary>
/// <remarks>
/// <para>Positions are Godot world space, so a row is rewritten after the floating origin moves (the player writes its row every
/// frame). The texture is uploaded by <see cref="Flush"/>, and only when a row changed; the table does that itself in
/// <see cref="RenderingServer.FramePreDraw"/>, after the frame's process calls wrote their rows and before it draws, so no owner
/// has to remember to (M2 5b b0: nothing ever called it in the game, the rows never reached the GPU, and every moving object in
/// a cell was lit by the ambient alone). Main thread only.</para>
/// <para>Exhaustion is loud and degrades to no lights: <see cref="Allocate"/> logs once and answers -1, an object drawn with no
/// light set shows only its ambient (the shader's row 0 means none).</para>
/// </remarks>
public sealed class ObjectLightTable : IDisposable
{
    /// <summary>The default number of rows: objects lit by cell lights at once.</summary>
    public const int DefaultRows = 256;

    private const int Stride = ObjectLightSets.RowWidth * 4;

    private readonly Image _image;
    private readonly ImageTexture _texture;
    private readonly Stack<int> _free = new();
    private readonly bool[] _used;
    private readonly float[] _shadow; // what each row holds, RGBA per texel: a write that changes nothing is skipped
    private bool _dirty;
    private bool _disposed;
    private bool _exhaustedLogged;
    private bool _reachLogged;

    /// <summary>A table of <paramref name="rows"/> rows, set as the shader global.</summary>
    public ObjectLightTable(int rows = DefaultRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        Rows = rows;
        _used = new bool[rows];
        _shadow = new float[rows * Stride];
        for (var r = rows - 1; r >= 0; r--)
        {
            _free.Push(r);
        }

        _image = Image.CreateEmpty(ObjectLightSets.RowWidth, rows, false, Image.Format.Rgbaf);
        _image.Fill(new Color(0f, 0f, 0f, 0f));
        _texture = ImageTexture.CreateFromImage(_image);
        _texture.ResourceName = "lifestone_object_lights";
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.ObjectLights, _texture);
        RenderingServer.FramePreDraw += OnFramePreDraw;
    }

    /// <summary>Times the texture was uploaded (a frame in which some row changed).</summary>
    public long Uploads { get; private set; }

    /// <summary>Rows in the table.</summary>
    public int Rows { get; }

    /// <summary>Rows in use.</summary>
    public int InUse => Rows - _free.Count;

    /// <summary>A free row, or -1 when there is none (logged once).</summary>
    public int Allocate()
    {
        if (_free.Count == 0)
        {
            if (!_exhaustedLogged)
            {
                _exhaustedLogged = true;
                Log.Error("objects", "object light table full: objects are drawn without cell lights", ("rows", Rows));
            }

            return -1;
        }

        var row = _free.Pop();
        _used[row] = true;
        return row;
    }

    /// <summary>Gives a row back; its lights are cleared.</summary>
    public void Release(int row)
    {
        if (_disposed || (uint)row >= (uint)Rows || !_used[row])
        {
            return;
        }

        Clear(row);
        _used[row] = false;
        _free.Push(row);
    }

    /// <summary>
    /// Writes the lights of <paramref name="row"/>: <paramref name="count"/> of them, positions already in Godot world space
    /// (<see cref="ObjectLight.Position"/>), the rest of the row cleared (a reach of 0 ends the list in the shader).
    /// </summary>
    public void Write(int row, ReadOnlySpan<ObjectLight> lights, int count)
    {
        if (_disposed || (uint)row >= (uint)Rows)
        {
            return;
        }

        if (!_reachLogged)
        {
            _reachLogged = true;
            Log.Warn("objects", "TODO(interiors-light-reach): a moving object in a cell is lit by the cell's lights within falloff + its radius (what Render::minimize_object_lighting, 0x0054E090, is decompiled to compute); the comparison itself is lost in the decompile, so lights between falloff + radius and falloff x 1.5 + radius, which the hardware range would reach, are left out unverified");
        }

        count = Math.Min(count, ObjectLightSets.MaxLights);
        Span<float> next = stackalloc float[Stride];
        next.Clear();
        for (var i = 0; i < count; i++)
        {
            var light = lights[i];
            var at = i * ObjectLightSets.TexelsPerLight * 4;
            next[at] = light.Position.X;
            next[at + 1] = light.Position.Y;
            next[at + 2] = light.Position.Z;
            next[at + 3] = light.Reach;
            next[at + 4] = light.Radiance.X;
            next[at + 5] = light.Radiance.Y;
            next[at + 6] = light.Radiance.Z;
        }

        // A row that did not change costs nothing: an object standing still writes the same lights every frame and the texture is not uploaded for it.
        var shadow = _shadow.AsSpan(row * Stride, Stride);
        if (next.SequenceEqual(shadow))
        {
            return;
        }

        next.CopyTo(shadow);
        for (var t = 0; t < ObjectLightSets.RowWidth; t++)
        {
            _image.SetPixel(t, row, new Color(next[t * 4], next[(t * 4) + 1], next[(t * 4) + 2], next[(t * 4) + 3]));
        }

        _dirty = true;
    }

    /// <summary>Uploads the table when a row changed since the last call. The table calls it before every draw; calling it sooner is harmless.</summary>
    public void Flush()
    {
        if (_dirty && !_disposed)
        {
            _dirty = false;
            _texture.Update(_image);
            Uploads++;
        }
    }

    private void OnFramePreDraw() => Flush();

    private void Clear(int row)
    {
        var shadow = _shadow.AsSpan(row * Stride, Stride);
        if (!shadow.ContainsAnyExcept(0f))
        {
            return;
        }

        shadow.Clear();
        for (var x = 0; x < ObjectLightSets.RowWidth; x++)
        {
            _image.SetPixel(x, row, new Color(0f, 0f, 0f, 0f));
        }

        _dirty = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RenderingServer.FramePreDraw -= OnFramePreDraw;
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.ObjectLights, new Variant());
        _texture.Dispose();
        _image.Dispose();
    }
}

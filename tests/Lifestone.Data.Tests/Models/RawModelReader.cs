using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests.Models;

/// <summary>
/// Independent, dependency-free reader for GfxObj (0x01), Setup (0x02) and GfxObjDegradeInfo (0x11) records, used to
/// cross-check DatReaderWriter's decoding. It is a C# transcription of the research reader
/// (session scratch <c>research/datspec/scripts/acparse.py</c>, which consumed every end-of-retail record byte-exactly)
/// and of the layouts in <c>docs/research/dat-format.md</c> §4, re-checked against retail <c>CGfxObj::Serialize</c>,
/// <c>CPolygon::UnPack</c>, <c>CSetup::UnPack</c> (<c>CCylSphere::UnPack</c>, <c>CSphere::UnPack</c>) and
/// <c>GfxObjDegradeInfo::UnPack</c>. It shares no code with DatReaderWriter.
/// </summary>
internal static class RawModelReader
{
    internal sealed record RawPolygon(
        byte Stippling,
        int SidesType,
        short PosSurface,
        short NegSurface,
        short[] VertexIds,
        byte[] PosUv,
        byte[] NegUv);

    internal sealed record RawVertex(ushort Key, Vector3 Origin, Vector3 Normal, Vector2[] Uvs);

    internal sealed record RawGfxObj(
        uint Id,
        uint Flags,
        uint[] Surfaces,
        RawVertex[] Vertices,
        int PhysicsPolygonCount,
        Vector3 SortCenter,
        (ushort Key, RawPolygon Polygon)[] Polygons,
        uint DegradeId,
        int Consumed);

    internal readonly record struct RawFrame(Vector3 Origin, Quaternion Orientation);

    internal sealed record RawLight(int Type, RawFrame Frame, uint Color, float Intensity, float Falloff, uint ConeAngleBits);

    internal sealed record RawSetup(
        uint Id,
        uint Flags,
        uint[] Parts,
        uint[]? Parents,
        Vector3[]? Scales,
        (int Key, int PartId, RawFrame Frame)[] Holding,
        (int Key, int PartId, RawFrame Frame)[] Connection,
        (int Key, RawFrame[] Frames, int Hooks)[] Placements,
        (Vector3 LowPoint, float Radius, float Height)[] CylSpheres,
        Vector4[] Spheres,
        float Height,
        float Radius,
        float StepUp,
        float StepDown,
        Vector4 Sorting,
        Vector4 Selection,
        RawLight[] Lights,
        uint[] Defaults,
        int Consumed);

    internal sealed record RawDegrade(uint Id, (uint GfxObj, uint Mode, float Min, float Ideal, float Max)[] Levels, int Consumed);

    /// <summary>The unparsed bytes of portal record <paramref name="id"/>; throws when the record is missing.</summary>
    public static byte[] PortalBytes(DatSet dats, uint id) =>
        dats.Read(d => d.Portal.TryGetFileBytes(id, out var bytes) ? bytes : null)
        ?? throw new InvalidDataException($"Portal record 0x{id:X8} is missing.");

    public static RawGfxObj ReadGfxObj(byte[] bytes)
    {
        var r = new Reader(bytes);
        var id = r.U32();
        var flags = r.U32();
        var surfaces = new uint[r.CompressedUInt()];
        for (var i = 0; i < surfaces.Length; i++)
        {
            surfaces[i] = r.U32();
        }

        var vertexType = r.I32();
        if (vertexType != 1)
        {
            throw new InvalidDataException($"vertex type {vertexType}");
        }

        var vertices = new RawVertex[r.U32()];
        for (var i = 0; i < vertices.Length; i++)
        {
            var key = r.U16();
            var uvCount = r.U16();
            var origin = r.Vec3();
            var normal = r.Vec3();
            var uvs = new Vector2[uvCount];
            for (var j = 0; j < uvCount; j++)
            {
                uvs[j] = new Vector2(r.F32(), r.F32());
            }

            vertices[i] = new RawVertex(key, origin, normal, uvs);
        }

        var physicsPolygons = 0;
        if ((flags & 1) != 0)
        {
            physicsPolygons = (int)r.CompressedUInt();
            for (var i = 0; i < physicsPolygons; i++)
            {
                r.U16();
                ReadPolygon(r);
            }

            SkipBsp(r, BspKind.Physics);
        }

        var sortCenter = r.Vec3();
        var polygons = Array.Empty<(ushort, RawPolygon)>();
        if ((flags & 2) != 0)
        {
            polygons = new (ushort, RawPolygon)[r.CompressedUInt()];
            for (var i = 0; i < polygons.Length; i++)
            {
                var key = r.U16();
                polygons[i] = (key, ReadPolygon(r));
            }

            SkipBsp(r, BspKind.Drawing);
        }

        var degrade = (flags & 8) != 0 ? r.U32() : 0u;
        return new RawGfxObj(id, flags, surfaces, vertices, physicsPolygons, sortCenter, polygons, degrade, r.Position);
    }

    public static RawSetup ReadSetup(byte[] bytes)
    {
        var r = new Reader(bytes);
        var id = r.U32();
        var flags = r.U32();
        var count = (int)r.U32();
        var parts = new uint[count];
        for (var i = 0; i < count; i++)
        {
            parts[i] = r.U32();
        }

        uint[]? parents = null;
        if ((flags & 1) != 0)
        {
            parents = new uint[count];
            for (var i = 0; i < count; i++)
            {
                parents[i] = r.U32();
            }
        }

        Vector3[]? scales = null;
        if ((flags & 2) != 0)
        {
            scales = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                scales[i] = r.Vec3();
            }
        }

        var holding = ReadLocations(r);
        var connection = ReadLocations(r);
        var placements = new (int, RawFrame[], int)[r.I32()];
        for (var i = 0; i < placements.Length; i++)
        {
            var key = r.I32();
            var frames = new RawFrame[count];
            for (var j = 0; j < count; j++)
            {
                frames[j] = r.Frame();
            }

            var hooks = (int)r.U32();
            for (var h = 0; h < hooks; h++)
            {
                SkipHook(r);
            }

            placements[i] = (key, frames, hooks);
        }

        // CCylSphere::UnPack: low point x, y, z, radius, height. CSphere::UnPack: centre x, y, z, radius.
        var cylSpheres = new (Vector3, float, float)[r.U32()];
        for (var i = 0; i < cylSpheres.Length; i++)
        {
            cylSpheres[i] = (r.Vec3(), r.F32(), r.F32());
        }

        var spheres = new Vector4[r.U32()];
        for (var i = 0; i < spheres.Length; i++)
        {
            spheres[i] = new Vector4(r.F32(), r.F32(), r.F32(), r.F32());
        }

        var height = r.F32();
        var radius = r.F32();
        var stepUp = r.F32();
        var stepDown = r.F32();
        var sorting = new Vector4(r.F32(), r.F32(), r.F32(), r.F32());
        var selection = new Vector4(r.F32(), r.F32(), r.F32(), r.F32());
        var lights = new RawLight[r.I32()];
        for (var i = 0; i < lights.Length; i++)
        {
            lights[i] = new RawLight(r.I32(), r.Frame(), r.U32(), r.F32(), r.F32(), r.U32());
        }

        var defaults = new uint[5];
        for (var i = 0; i < 5; i++)
        {
            defaults[i] = r.U32();
        }

        return new RawSetup(
            id, flags, parts, parents, scales, holding, connection, placements, cylSpheres, spheres, height, radius,
            stepUp, stepDown, sorting, selection, lights, defaults, r.Position);
    }

    public static RawDegrade ReadDegrade(byte[] bytes)
    {
        var r = new Reader(bytes);
        var id = r.U32();
        var levels = new (uint, uint, float, float, float)[r.U32()];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = (r.U32(), r.U32(), r.F32(), r.F32(), r.F32());
        }

        return new RawDegrade(id, levels, r.Position);
    }

    /// <summary>
    /// <c>CSurface::type</c>: the first u32 of a Surface (<c>0x08</c>) record, which has no id header
    /// (<c>CSurface::Serialize</c>).
    /// </summary>
    public static uint ReadSurfaceType(byte[] bytes) => new Reader(bytes).U32();

    private static (int, int, RawFrame)[] ReadLocations(Reader r)
    {
        var result = new (int, int, RawFrame)[r.I32()];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (r.I32(), r.I32(), r.Frame());
        }

        return result;
    }

    private static RawPolygon ReadPolygon(Reader r)
    {
        var n = r.U8();
        var stippling = r.U8();
        var sides = r.I32();
        var pos = r.I16();
        var neg = r.I16();
        var vertexIds = new short[n];
        for (var i = 0; i < n; i++)
        {
            vertexIds[i] = r.I16();
        }

        var posUv = (stippling & 4) == 0 ? r.Bytes(n) : [];
        var negUv = sides == 2 && (stippling & 8) == 0 ? r.Bytes(n) : [];
        return new RawPolygon(stippling, sides, pos, neg, vertexIds, posUv, negUv);
    }

    private enum BspKind
    {
        Drawing,
        Physics,
    }

    /// <summary>
    /// Walks a BSP tree. Tags are stored as little-endian u32 whose bytes spell the tag backwards. Only BPnn/BPIn,
    /// BpIN/BpnN and BPIN/BPnN have children; BpIn/BPOL/BPFL (common in drawing trees) have none.
    /// </summary>
    private static void SkipBsp(Reader r, BspKind kind)
    {
        var tagBytes = r.Bytes(4);
        Array.Reverse(tagBytes);
        var tag = Encoding.ASCII.GetString(tagBytes);
        if (tag == "LEAF")
        {
            r.I32();
            if (kind == BspKind.Physics)
            {
                r.I32();
                r.Skip(16);
                r.Skip(2 * (int)r.U32());
            }

            return;
        }

        if (tag == "PORT")
        {
            r.Skip(16);
            SkipBsp(r, kind);
            SkipBsp(r, kind);
            if (kind == BspKind.Drawing)
            {
                r.Skip(16);
                var polys = (int)r.U32();
                var portals = (int)r.U32();
                r.Skip((2 * polys) + (4 * portals));
            }

            return;
        }

        r.Skip(16);
        switch (tag)
        {
            case "BPnn" or "BPIn" or "BpIN" or "BpnN":
                SkipBsp(r, kind);
                break;
            case "BPIN" or "BPnN":
                SkipBsp(r, kind);
                SkipBsp(r, kind);
                break;
        }

        r.Skip(16);
        if (kind == BspKind.Drawing)
        {
            r.Skip(2 * (int)r.U32());
        }
    }

    /// <summary>Animation hook payload sizes (u32 type, i32 direction, payload), from acparse.py / ACE AnimationHooks.</summary>
    private static void SkipHook(Reader r)
    {
        var type = r.U32();
        r.I32();
        if (type == 5)
        {
            r.U16();
            var v = r.U16();
            if ((v & 0x8000) != 0)
            {
                r.U16();
            }

            return;
        }

        int[] sizes = [0, 4, 4, 28, 0, -1, 4, 16, 12, 16, 12, 16, 8, 40, 4, 4, 4, 0, 4, 8, 12, 16, 12, 8, 12, 4, 40];
        r.Skip(sizes[type]);
    }

    private sealed class Reader(byte[] bytes)
    {
        public int Position { get; private set; }

        public byte U8() => bytes[Position++];

        public ushort U16()
        {
            var v = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(Position));
            Position += 2;
            return v;
        }

        public short I16() => unchecked((short)U16());

        public uint U32()
        {
            var v = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Position));
            Position += 4;
            return v;
        }

        public int I32() => unchecked((int)U32());

        public float F32() => BitConverter.UInt32BitsToSingle(U32());

        public Vector3 Vec3() => new(F32(), F32(), F32());

        /// <summary>Frame: origin x, y, z then quaternion w, x, y, z.</summary>
        public RawFrame Frame()
        {
            var origin = Vec3();
            var w = F32();
            var x = F32();
            var y = F32();
            var z = F32();
            return new RawFrame(origin, new Quaternion(x, y, z, w));
        }

        /// <summary>AC compressed u32: 1, 2 or 4 bytes (ACE <c>ReadCompressedUInt32</c>).</summary>
        public uint CompressedUInt()
        {
            var b0 = U8();
            if ((b0 & 0x80) == 0)
            {
                return b0;
            }

            var b1 = U8();
            if ((b0 & 0x40) == 0)
            {
                return (uint)(((b0 & 0x7F) << 8) | b1);
            }

            var low = U16();
            return (uint)(((((b0 & 0x3F) << 8) | b1) << 16) | low);
        }

        public byte[] Bytes(int count)
        {
            var result = bytes.AsSpan(Position, count).ToArray();
            Position += count;
            return result;
        }

        public void Skip(int count) => Position += count;
    }
}

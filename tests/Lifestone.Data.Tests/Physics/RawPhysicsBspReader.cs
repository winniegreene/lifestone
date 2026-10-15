using System.Buffers.Binary;
using System.Numerics;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// An independent, dependency-free reader of the collision data in GfxObj (0x01) and Environment (0x0D) records, used to
/// cross-check the physics decode (DatReaderWriter plus <c>Lifestone.Data.Physics</c>). Grown from
/// <c>Models/RawModelReader.SkipBsp</c> into a reader that keeps the physics trees, and written from the retail unpack
/// functions (11.4186: <c>CGfxObj::Serialize</c>, <c>CCellStruct::UnPack</c>, <c>CPolygon::UnPack</c>,
/// <c>BSPTREE::UnPack</c>, <c>BSPNODE::UnPack</c>/<c>UnPackChild</c>, <c>BSPLEAF::UnPackLeaf</c>,
/// <c>BSPPORTAL::UnPackPortal</c>). It shares no code with DatReaderWriter.
/// </summary>
internal static class RawPhysicsBspReader
{
    /// <summary>The LEAF tag.</summary>
    public const uint LeafTag = 0x4C454146;

    /// <summary>The PORT tag.</summary>
    public const uint PortTag = 0x504F5254;

    internal sealed record RawPolygon(ushort Id, byte Stippling, int SidesType, short PosSurface, short NegSurface, ushort[] VertexIds);

    /// <summary>A physics BSP node: for a leaf the index, solid flag, sphere and polygon indices; else plane, children, sphere.</summary>
    internal sealed record RawNode(uint Tag, Vector4 Plane, RawNode? Pos, RawNode? Neg, Vector4 Sphere, int LeafIndex, int Solid, ushort[] Polygons);

    /// <summary>One collision shape: vertex keys and positions, physics polygons and the physics BSP.</summary>
    internal sealed record RawShape(ushort[] VertexKeys, Vector3[] Vertices, RawPolygon[] Polygons, RawNode Root);

    /// <summary>The GfxObj's collision shape, or null when it has no physics flag; the whole record must be consumed.</summary>
    public static RawShape? ReadGfxObj(byte[] bytes)
    {
        var r = new Reader(bytes);
        r.U32(); // id
        var flags = r.U32();
        var surfaces = r.CompressedUInt();
        r.Skip(4 * (int)surfaces);
        var (keys, vertices) = ReadVertexArray(r);
        RawShape? shape = null;
        if ((flags & 1) != 0)
        {
            var polygons = ReadPolygons(r, (int)r.CompressedUInt());
            shape = new RawShape(keys, vertices, polygons, ReadPhysicsNode(r));
        }

        r.Skip(12); // sort centre
        if ((flags & 2) != 0)
        {
            ReadPolygons(r, (int)r.CompressedUInt());
            SkipDrawingNode(r);
        }

        if ((flags & 8) != 0)
        {
            r.U32();
        }

        r.RequireEnd("GfxObj");
        return shape;
    }

    /// <summary>Every struct's collision shape in an Environment, in stored order; the whole record must be consumed.</summary>
    public static (uint Key, RawShape Shape)[] ReadEnvironment(byte[] bytes)
    {
        var r = new Reader(bytes);
        r.U32(); // id
        var result = new (uint, RawShape)[r.U32()];
        for (var i = 0; i < result.Length; i++)
        {
            var key = r.U32();
            var polygonCount = (int)r.U32();
            var physicsCount = (int)r.U32();
            var portalCount = (int)r.U32();
            var (keys, vertices) = ReadVertexArray(r);
            ReadPolygons(r, polygonCount);
            r.Skip(2 * portalCount);
            r.Align4();
            SkipCellNode(r);
            var physics = ReadPolygons(r, physicsCount);
            var root = ReadPhysicsNode(r);
            if (r.U32() != 0)
            {
                SkipDrawingNode(r);
            }

            r.Align4();
            result[i] = (key, new RawShape(keys, vertices, physics, root));
        }

        r.RequireEnd("Environment");
        return result;
    }

    /// <summary>Retail <c>BSPNODE::UnPack</c>'s child rule: which children a node type has.</summary>
    public static (bool Pos, bool Neg) Children(uint tag) => TagName(tag) switch
    {
        "BPnn" or "BPIn" => (true, false),
        "BpIN" or "BpnN" => (false, true),
        "BPIN" or "BPnN" => (true, true),
        _ => (false, false),
    };

    /// <summary>The tag's four letters.</summary>
    public static string TagName(uint tag) =>
        new([(char)((tag >> 24) & 0xFF), (char)((tag >> 16) & 0xFF), (char)((tag >> 8) & 0xFF), (char)(tag & 0xFF)]);

    private static (ushort[] Keys, Vector3[] Positions) ReadVertexArray(Reader r)
    {
        var type = r.I32();
        if (type != 1)
        {
            throw new InvalidDataException($"vertex type {type}");
        }

        var count = (int)r.U32();
        var keys = new ushort[count];
        var positions = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            keys[i] = r.U16();
            var uvCount = r.U16();
            positions[i] = r.Vec3();
            r.Skip(12 + (8 * uvCount)); // normal, UVs
        }

        return (keys, positions);
    }

    /// <summary><c>CPolygon::UnPack</c>: id, corner count, stippling, sides type, surfaces, vertex ids, UV indices.</summary>
    private static RawPolygon[] ReadPolygons(Reader r, int count)
    {
        var result = new RawPolygon[count];
        for (var i = 0; i < count; i++)
        {
            var id = r.U16();
            var corners = r.U8();
            var stippling = r.U8();
            var sides = r.I32();
            var pos = r.I16();
            var neg = r.I16();
            var vertexIds = new ushort[corners];
            for (var v = 0; v < corners; v++)
            {
                vertexIds[v] = r.U16();
            }

            if ((stippling & 4) == 0)
            {
                r.Skip(corners);
            }

            if (sides == 2 && (stippling & 8) == 0)
            {
                r.Skip(corners);
            }

            result[i] = new RawPolygon(id, stippling, sides, pos, neg, vertexIds);
        }

        return result;
    }

    /// <summary>A physics tree child (<c>pack_tree_type</c> 1): no PORT nodes.</summary>
    private static RawNode ReadPhysicsNode(Reader r)
    {
        var tag = r.U32();
        if (tag == LeafTag)
        {
            var index = r.I32();
            var solid = r.I32();
            var sphere = r.Vec4();
            var polygons = new ushort[r.U32()];
            for (var i = 0; i < polygons.Length; i++)
            {
                polygons[i] = r.U16();
            }

            return new RawNode(tag, default, null, null, sphere, index, solid, polygons);
        }

        if (tag == PortTag)
        {
            throw new InvalidDataException("PORT node in a physics BSP");
        }

        var plane = r.Vec4();
        var (hasPos, hasNeg) = Children(tag);
        var pos = hasPos ? ReadPhysicsNode(r) : null;
        var neg = hasNeg ? ReadPhysicsNode(r) : null;
        return new RawNode(tag, plane, pos, neg, r.Vec4(), 0, 0, []);
    }

    /// <summary>A cell tree child (<c>pack_tree_type</c> 2): a leaf is its index; an internal node a plane and children.</summary>
    private static void SkipCellNode(Reader r)
    {
        var tag = r.U32();
        if (tag == LeafTag)
        {
            r.I32();
            return;
        }

        if (tag == PortTag)
        {
            throw new InvalidDataException("PORT node in a cell BSP");
        }

        r.Skip(16);
        var (hasPos, hasNeg) = Children(tag);
        if (hasPos)
        {
            SkipCellNode(r);
        }

        if (hasNeg)
        {
            SkipCellNode(r);
        }
    }

    /// <summary>A drawing tree child (<c>pack_tree_type</c> 0).</summary>
    private static void SkipDrawingNode(Reader r)
    {
        var tag = r.U32();
        if (tag == LeafTag)
        {
            r.I32();
            return;
        }

        r.Skip(16);
        if (tag == PortTag)
        {
            SkipDrawingNode(r);
            SkipDrawingNode(r);
            r.Skip(16);
            var polygons = (int)r.U32();
            var portals = (int)r.U32();
            r.Skip((2 * polygons) + (4 * portals));
            return;
        }

        var (hasPos, hasNeg) = Children(tag);
        if (hasPos)
        {
            SkipDrawingNode(r);
        }

        if (hasNeg)
        {
            SkipDrawingNode(r);
        }

        r.Skip(16);
        r.Skip(2 * (int)r.U32());
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

        public Vector4 Vec4() => new(F32(), F32(), F32(), F32());

        /// <summary>AC compressed u32: 1, 2 or 4 bytes.</summary>
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

        public void Skip(int count) => Position += count;

        /// <summary>Pads to a multiple of four bytes from the record's start.</summary>
        public void Align4() => Position = (Position + 3) & ~3;

        public void RequireEnd(string what)
        {
            if (Position != bytes.Length)
            {
                throw new InvalidDataException($"{what}: consumed {Position} of {bytes.Length} bytes");
            }
        }
    }
}

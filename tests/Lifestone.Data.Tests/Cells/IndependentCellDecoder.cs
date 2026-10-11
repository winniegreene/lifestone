using System.Buffers.Binary;
using System.Numerics;

namespace Lifestone.Data.Tests.Cells;

/// <summary>
/// A second, independent decoder for EnvCell and LandBlockInfo records, written directly from the retail unpack
/// routines (<c>CEnvCell::UnPack</c>, <c>CCellPortal::UnPack</c>, <c>CLandBlockInfo::UnPack</c>,
/// <c>CBldPortal::UnPack</c>, <c>Frame::UnPack</c>) with plain little-endian reads. It shares no code with
/// DatReaderWriter or Lifestone.Data, so agreement between the two is a real cross-check.
/// </summary>
internal static class IndependentCellDecoder
{
    public sealed record Frame(Vector3 Origin, float Qw, float Qx, float Qy, float Qz);

    public sealed record Stab(uint Id, Frame Frame);

    public sealed record CellPortal(ushort Flags, ushort PolygonId, uint OtherCellId, short OtherPortalId, ushort RawOther);

    public sealed record EnvCell(
        uint Id,
        uint Flags,
        uint[] Surfaces,
        uint EnvironmentId,
        ushort CellStruct,
        Frame Frame,
        CellPortal[] Portals,
        uint[] Visible,
        Stab[] Statics,
        uint RestrictionObj,
        int Consumed);

    public sealed record BldPortal(ushort Flags, uint OtherCellId, short OtherPortalId, uint[] Stabs);

    public sealed record Building(uint ModelId, Frame Frame, uint NumLeaves, BldPortal[] Portals);

    public sealed record LandBlockInfo(uint Id, uint NumCells, Stab[] Objects, Building[] Buildings, Dictionary<uint, uint> Restrictions, int Consumed);

    public static EnvCell DecodeEnvCell(byte[] data)
    {
        var r = new Reader(data);
        var id = r.U32();
        var flags = r.U32();
        var id2 = r.U32();
        if (id2 != id)
        {
            throw new InvalidDataException("second id differs");
        }

        var blockMask = id & 0xFFFF0000u;
        var numSurfaces = r.U8();
        var numPortals = r.U8();
        var numVisible = r.U16();
        var surfaces = new uint[numSurfaces];
        for (var i = 0; i < numSurfaces; i++)
        {
            surfaces[i] = r.U16() | 0x08000000u;
        }

        var env = r.U16() | 0x0D000000u;
        var cellStruct = r.U16();
        var frame = r.Frame();
        var portals = new CellPortal[numPortals];
        for (var i = 0; i < numPortals; i++)
        {
            var f = r.U16();
            var poly = r.U16();
            var other = r.U16();
            var otherPortal = (short)r.U16();
            portals[i] = new CellPortal(f, poly, (f & 4) != 0 ? 0xFFFFFFFFu : blockMask | other, otherPortal, other);
        }

        var visible = new uint[numVisible];
        for (var i = 0; i < numVisible; i++)
        {
            visible[i] = blockMask | r.U16();
        }

        var statics = Array.Empty<Stab>();
        if ((flags & 2) != 0)
        {
            statics = new Stab[r.U32()];
            for (var i = 0; i < statics.Length; i++)
            {
                statics[i] = new Stab(r.U32(), r.Frame());
            }
        }

        var restriction = (flags & 8) != 0 ? r.U32() : 0u;
        return new EnvCell(id, flags, surfaces, env, cellStruct, frame, portals, visible, statics, restriction, r.Position);
    }

    public static LandBlockInfo DecodeLandBlockInfo(byte[] data)
    {
        var r = new Reader(data);
        var id = r.U32();
        var blockMask = id & 0xFFFF0000u;
        var numCells = r.U32();
        var objects = new Stab[r.U32()];
        for (var i = 0; i < objects.Length; i++)
        {
            objects[i] = new Stab(r.U32(), r.Frame());
        }

        var packed = r.U32();
        var buildings = new Building[packed & 0xFFFF];
        var packMask = packed >> 16;
        for (var b = 0; b < buildings.Length; b++)
        {
            var model = r.U32();
            var frame = r.Frame();
            var leaves = r.U32();
            var portals = new BldPortal[r.U32()];
            for (var p = 0; p < portals.Length; p++)
            {
                var f = r.U16();
                var other = r.U16();
                var otherPortal = (short)r.U16();
                var stabs = new uint[r.U16()];
                for (var s = 0; s < stabs.Length; s++)
                {
                    stabs[s] = blockMask | r.U16();
                }

                if ((stabs.Length & 1) != 0)
                {
                    r.Skip(2); // CBldPortal::UnPack: an odd stab count is padded to 4 bytes
                }

                portals[p] = new BldPortal(f, blockMask | other, otherPortal, stabs);
            }

            buildings[b] = new Building(model, frame, leaves, portals);
        }

        var restrictions = new Dictionary<uint, uint>();
        if ((packMask & 1) != 0)
        {
            // PackableHashTable<u32,u32>: u16 count, u16 bucket count, then count (key, value) pairs.
            var count = r.U16();
            r.U16();
            for (var i = 0; i < count; i++)
            {
                restrictions.Add(r.U32(), r.U32());
            }
        }

        return new LandBlockInfo(id, numCells, objects, buildings, restrictions, r.Position);
    }

    private sealed class Reader(byte[] data)
    {
        public int Position { get; private set; }

        public byte U8() => data[Position++];

        public ushort U16()
        {
            var v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Position, 2));
            Position += 2;
            return v;
        }

        public uint U32()
        {
            var v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Position, 4));
            Position += 4;
            return v;
        }

        public float F32()
        {
            var v = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(Position, 4));
            Position += 4;
            return v;
        }

        public void Skip(int n) => Position += n;

        // Frame::UnPack: origin x, y, z then quaternion w, x, y, z.
        public Frame Frame() => new(new Vector3(F32(), F32(), F32()), F32(), F32(), F32(), F32());
    }
}

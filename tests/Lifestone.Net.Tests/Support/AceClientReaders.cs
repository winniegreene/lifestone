using System.Text;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// Client-to-server message bodies read the way ACE's handlers read them (ACEmulator/ACE @95ef426d, read, not copied):
/// a <see cref="BinaryReader"/> over UTF-8, String16L by character count (<c>BinaryReaderExtensions.ReadString16L</c>),
/// no check for trailing bytes. The codec tests check Lifestone's builders against these, and the fake server
/// (<c>FakeAce</c>) handles client messages with them. Like ACE they throw on a truncated body
/// (<see cref="EndOfStreamException"/>); each returns the bytes it left unread, which ACE ignores.
/// </summary>
internal static class AceClientReaders
{
    /// <summary>A reader positioned after the opcode, which must be <paramref name="opcode"/>.</summary>
    public static BinaryReader Open(byte[] message, uint opcode)
    {
        var reader = new BinaryReader(new MemoryStream(message, writable: false), Encoding.UTF8);
        var read = reader.ReadUInt32();
        if (read != opcode)
        {
            throw new InvalidDataException($"expected opcode 0x{opcode:X4}, read 0x{read:X4}");
        }

        return reader;
    }

    /// <summary>ACE's <c>ReadString16L</c>: a u16 count, that many UTF-8 characters, then the pad computed from the count.</summary>
    public static string ReadString16L(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        var text = length != 0 ? new string(reader.ReadChars(length)) : string.Empty;
        reader.BaseStream.Position += (4 - ((2 + length) & 3)) & 3;
        return text;
    }

    /// <summary>The bytes after the reader's position.</summary>
    public static int Unread(BinaryReader reader) => (int)(reader.BaseStream.Length - reader.BaseStream.Position);

    /// <summary>F656 as <c>CharacterHandler.CharacterCreate</c> and <c>CharacterCreateInfo.Unpack</c> read it.</summary>
    public static CharacterCreateInfo ReadCharacterCreate(byte[] message)
    {
        using var reader = Open(message, 0xF656);
        var account = ReadString16L(reader);
        var packVersion = reader.ReadUInt32();  // ACE skips it: "Unknown constant (1)"
        var heritage = reader.ReadUInt32();
        var gender = reader.ReadUInt32();
        var appearance = new uint[14];
        for (var i = 0; i < appearance.Length; i++)
        {
            appearance[i] = reader.ReadUInt32();
        }

        var shades = new double[6];
        for (var i = 0; i < shades.Length; i++)
        {
            shades[i] = reader.ReadDouble();
        }

        var template = reader.ReadInt32();
        var attributes = new uint[6];
        for (var i = 0; i < attributes.Length; i++)
        {
            attributes[i] = reader.ReadUInt32();
        }

        var slot = reader.ReadUInt32();
        var classId = reader.ReadUInt32();
        var skillCount = reader.ReadUInt32();
        var skills = new uint[skillCount];
        for (var i = 0; i < skills.Length; i++)
        {
            skills[i] = reader.ReadUInt32();
        }

        var name = ReadString16L(reader);
        var startArea = reader.ReadUInt32();
        var isAdmin = reader.ReadUInt32() == 1;
        var isSentinel = reader.ReadUInt32() == 1;
        var unread = reader.BaseStream.Length - reader.BaseStream.Position;
        uint? trailing = unread >= 4 ? reader.ReadUInt32() : null;
        return new CharacterCreateInfo(account, packVersion, heritage, gender, appearance, shades, template, attributes, slot, classId, skills, name,
            startArea, isAdmin, isSentinel, trailing, Unread(reader));
    }

    /// <summary>F7E6 as <c>DDDHandler.DDD_InterrogationResponse</c> reads it (<c>ReadCAllIterationList</c>).</summary>
    public static DddResponse ReadDddInterrogationResponse(byte[] message)
    {
        using var reader = Open(message, 0xF7E6);
        var language = reader.ReadUInt32();
        var count = reader.ReadInt32();
        var lists = new List<DddList>();
        for (var i = 0; i < count; i++)
        {
            var datType = reader.ReadInt32();
            var datId = reader.ReadInt32();
            var iterations = reader.ReadInt32();
            var ints = new List<int>();
            // CMostlyConsecutiveIntSet: a run's negated length counts its length minus one, the value after it one more.
            var covered = 0;
            while (covered != iterations)
            {
                var x = reader.ReadInt32();
                covered += x < 0 ? Math.Abs(x) - 1 : 1;
                ints.Add(x);
            }

            lists.Add(new DddList(datType, datId, iterations, ints));
        }

        return new DddResponse(language, lists, Unread(reader));
    }

    /// <summary>F655 as <c>CharacterHandler.CharacterDelete</c> reads it.</summary>
    public static (string Account, uint Slot, int Unread) ReadCharacterDelete(byte[] message)
    {
        using var reader = Open(message, 0xF655);
        var account = ReadString16L(reader);
        var slot = reader.ReadUInt32();
        return (account, slot, Unread(reader));
    }

    /// <summary>F7D9 as <c>CharacterHandler.CharacterRestore</c> reads it: the GUID alone.</summary>
    public static (uint Guid, int Unread) ReadCharacterRestore(byte[] message)
    {
        using var reader = Open(message, 0xF7D9);
        var guid = reader.ReadUInt32();
        return (guid, Unread(reader));
    }

    /// <summary>F657 as <c>CharacterHandler.CharacterEnterWorld</c> reads it.</summary>
    public static (uint Guid, string Account, int Unread) ReadCharacterEnterWorld(byte[] message)
    {
        using var reader = Open(message, 0xF657);
        var guid = reader.ReadUInt32();
        var account = ReadString16L(reader);
        return (guid, account, Unread(reader));
    }

    /// <summary>The F7B1 header as <c>GameActionPacket.HandleGameAction</c> reads it; the reader is left on the payload.</summary>
    public static (uint Sequence, uint ActionType, BinaryReader Payload) ReadGameAction(byte[] message)
    {
        var reader = Open(message, 0xF7B1);
        var sequence = reader.ReadUInt32();
        var actionType = reader.ReadUInt32();
        return (sequence, actionType, reader);
    }

    /// <summary>ACE's <c>Position(BinaryReader)</c>: cell, origin, then the quaternion W, X, Y, Z.</summary>
    public static AceReadPosition ReadPosition(BinaryReader reader) =>
        new(reader.ReadUInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    // ---- ACE's reader mirror for the world actions (m2-plan §3.3 N7) ----

    /// <summary>
    /// 0xF61C MoveToState as <c>GameActionMoveToState</c> reads it (<c>Motion/MoveToState.cs</c>): ACE's
    /// <c>RawMotionState</c> reader (packed flags, 11 flag bits and the count from bit 11 up, the flagged u32 or f32 fields,
    /// then 8-byte <c>MotionItem</c>s), <c>Position(BinaryReader)</c>, four u16 sequences, the contact/long-jump byte,
    /// then <c>Align</c> from the message's start. Fields ACE did not read are null. The reader is the message's own, so
    /// a client that packs actions 4 bytes long (OpenAC's defect 1) shows up as a wrong cell.
    /// </summary>
    public static AceMoveToState ReadMoveToState(byte[] message)
    {
        var (sequence, actionType, reader) = ReadGameAction(message);
        using (reader)
        {
            ExpectAction(actionType, 0xF61C);
            var packed = reader.ReadUInt32();
            var flags = packed & 0x7FF;
            var count = (ushort)(packed >> 11);
            uint? U32(uint bit) => (flags & bit) != 0 ? reader.ReadUInt32() : null;
            float? F32(uint bit) => (flags & bit) != 0 ? reader.ReadSingle() : null;
            var state = new AceRawMotionState(
                packed,
                U32(0x1),
                U32(0x2),
                U32(0x4),
                U32(0x8),
                F32(0x10),
                U32(0x20),
                U32(0x40),
                F32(0x80),
                U32(0x100),
                U32(0x200),
                F32(0x400),
                Enumerable.Range(0, count).Select(_ => (reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadSingle())).ToList());
            var position = ReadPosition(reader);
            var sequences = (reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
            var contactLongJump = reader.ReadByte();
            AlignFromStart(reader);
            return new AceMoveToState(sequence, state, position, sequences, contactLongJump, Unread(reader));
        }
    }

    /// <summary>
    /// 0xF753 AutonomousPosition as <c>GameActionAutonomousPosition</c> reads it: <c>Position(BinaryReader)</c>, four u16
    /// sequences, the contact byte, <c>Align</c>.
    /// </summary>
    public static AceAutonomousPosition ReadAutonomousPosition(byte[] message)
    {
        var (sequence, actionType, reader) = ReadGameAction(message);
        using (reader)
        {
            ExpectAction(actionType, 0xF753);
            var position = ReadPosition(reader);
            var sequences = (reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
            var contact = reader.ReadByte() != 0;
            AlignFromStart(reader);
            return new AceAutonomousPosition(sequence, position, sequences, contact, Unread(reader));
        }
    }

    /// <summary>
    /// 0xF61B Jump as <c>GameActionJump</c> reads it: ACE's <c>JumpPack(BinaryReader)</c> (f32 extent, three f32 of
    /// velocity, then four u16 it calls sequences, which over retail's 56-byte pack are the first 8 bytes of the position),
    /// then a u32 "object GUID" and a u32 "spell id". 32 bytes; ACE uses the extent and the velocity only.
    /// </summary>
    public static AceJump ReadJump(byte[] message)
    {
        var (sequence, actionType, reader) = ReadGameAction(message);
        using (reader)
        {
            ExpectAction(actionType, 0xF61B);
            var extent = reader.ReadSingle();
            var velocity = (reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var sequences = (reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
            var objectGuid = reader.ReadUInt32();
            var spellId = reader.ReadUInt32();
            return new AceJump(sequence, extent, velocity, sequences, objectGuid, spellId, Unread(reader));
        }
    }

    /// <summary>0x0036 Use (<c>GameActionUseItem</c>) or 0x01BF QueryHealth (<c>GameActionQueryHealth</c>): one u32 GUID.</summary>
    public static (uint Sequence, uint ActionType, uint Guid, int Unread) ReadObjectAction(byte[] message)
    {
        var (sequence, actionType, reader) = ReadGameAction(message);
        using (reader)
        {
            var guid = reader.ReadUInt32();
            return (sequence, actionType, guid, Unread(reader));
        }
    }

    /// <summary>
    /// 0x0035 UseWithTarget (<c>GameActionUseWithTarget.cs:10-15</c>): a u32 <c>sourceObjectGuid</c> (the item), then a u32 <c>targetObjectGuid</c>; ACE hands both to
    /// <c>Player.HandleActionUseWithTarget</c> and reads nothing more.
    /// </summary>
    public static (uint Sequence, uint ActionType, uint Source, uint Target, int Unread) ReadUseWithTarget(byte[] message)
    {
        var (sequence, actionType, reader) = ReadGameAction(message);
        using (reader)
        {
            var source = reader.ReadUInt32();
            var target = reader.ReadUInt32();
            return (sequence, actionType, source, target, Unread(reader));
        }
    }

    /// <summary>0x0015 Talk (<c>GameActionTalk</c>): one String16L read by UTF-8 characters.</summary>
    public static (uint Sequence, string Text, int Unread) ReadTalk(byte[] message)
    {
        var (sequence, actionType, reader) = ReadGameAction(message);
        using (reader)
        {
            ExpectAction(actionType, 0x0015);
            var text = ReadString16L(reader);
            return (sequence, text, Unread(reader));
        }
    }

    /// <summary>0x005D Tell (<c>GameActionTell</c>): the message, then the target, each by UTF-8 characters.</summary>
    public static (uint Sequence, string Text, string Target, int Unread) ReadTell(byte[] message)
    {
        var (sequence, actionType, reader) = ReadGameAction(message);
        using (reader)
        {
            ExpectAction(actionType, 0x005D);
            var text = ReadString16L(reader);
            var target = ReadString16L(reader);
            return (sequence, text, target, Unread(reader));
        }
    }

    /// <summary>ACE's <c>BinaryReader.Align</c>: skips to a multiple of 4 from the message's start.</summary>
    private static void AlignFromStart(BinaryReader reader) =>
        reader.BaseStream.Position += (4 - (reader.BaseStream.Position & 3)) & 3;

    private static void ExpectAction(uint actual, uint expected)
    {
        if (actual != expected)
        {
            throw new InvalidDataException($"expected game action 0x{expected:X4}, read 0x{actual:X4}");
        }
    }

    /// <summary>What ACE's <c>RawMotionState</c> reader read; a field it skipped (flag clear) is null.</summary>
    public sealed record AceRawMotionState(
        uint PackedFlags,
        uint? CurrentHoldKey,
        uint? CurrentStyle,
        uint? ForwardCommand,
        uint? ForwardHoldKey,
        float? ForwardSpeed,
        uint? SidestepCommand,
        uint? SidestepHoldKey,
        float? SidestepSpeed,
        uint? TurnCommand,
        uint? TurnHoldKey,
        float? TurnSpeed,
        IReadOnlyList<(ushort Command, ushort PackedSequence, float Speed)> Commands);

    /// <summary>What <see cref="ReadMoveToState"/> read; <paramref name="Unread"/> is what ACE leaves.</summary>
    public sealed record AceMoveToState(
        uint ActionSequence,
        AceRawMotionState State,
        AceReadPosition Position,
        (ushort Instance, ushort ServerControl, ushort Teleport, ushort ForcePosition) Sequences,
        byte ContactLongJump,
        int Unread);

    /// <summary>What <see cref="ReadAutonomousPosition"/> read.</summary>
    public sealed record AceAutonomousPosition(
        uint ActionSequence,
        AceReadPosition Position,
        (ushort Instance, ushort ServerControl, ushort Teleport, ushort ForcePosition) Sequences,
        bool Contact,
        int Unread);

    /// <summary>What <see cref="ReadJump"/> read: ACE's view of the pack, and the 24 bytes it leaves.</summary>
    public sealed record AceJump(
        uint ActionSequence,
        float Extent,
        (float X, float Y, float Z) Velocity,
        (ushort A, ushort B, ushort C, ushort D) Sequences,
        uint ObjectGuid,
        uint SpellId,
        int Unread);

    /// <summary>What <see cref="ReadCharacterCreate"/> read, field for field, plus ACE's unread tail.</summary>
    public sealed record CharacterCreateInfo(
        string Account,
        uint PackVersion,
        uint Heritage,
        uint Gender,
        uint[] Appearance,
        double[] Shades,
        int Template,
        uint[] Attributes,
        uint Slot,
        uint ClassId,
        uint[] Skills,
        string Name,
        uint StartArea,
        bool IsAdmin,
        bool IsSentinel,
        uint? TrailingWord,
        int Unread);

    /// <summary>What <see cref="ReadDddInterrogationResponse"/> read; <paramref name="Unread"/> is what ACE leaves (the
    /// lists without keys and the flags).</summary>
    public sealed record DddResponse(uint ClientLanguage, IReadOnlyList<DddList> Lists, int Unread);

    /// <summary>One tagged iteration list: ACE keeps the count (<c>Iterations</c>) and the raw ints.</summary>
    public sealed record DddList(int DatType, int DatId, int Iterations, IReadOnlyList<int> Ints);

    /// <summary>A position as ACE reads it.</summary>
    public readonly record struct AceReadPosition(uint Cell, float X, float Y, float Z, float Qw, float Qx, float Qy, float Qz);
}

using System.Text;

namespace Lifestone.Net.Messages;

/// <summary>
/// A small, well-formed server message for warming the decoders' code before the first real one (DoD 15: the first PlayerDescription of a login cost about 40 ms of just-in-time
/// compilation on the game thread, <c>TODO(login-burst-frame)</c>; docs/session.md section 27). The bytes are laid out as ACE writes the event (the same layout
/// <c>PlayerDescriptionCodecTests</c> checks against the test writers), with one entry in every table and list so that every generic instantiation the decoder uses is run once.
/// </summary>
public static class WarmUpMessages
{
    /// <summary>
    /// A PlayerDescription (F7B0, event 0x0013) for <paramref name="playerGuid"/> with one entry in every table, a skill, a spell, two enchantments, a shortcut, a spell
    /// bar, a desired component, an inventory entry and an equipped one.
    /// </summary>
    /// <param name="playerGuid">The player's object id in the envelope.</param>
    /// <returns>The whole message body, opcode first.</returns>
    public static byte[] PlayerDescription(uint playerGuid)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(MessageOpcodes.GameEvent);
            w.Write(playerGuid);
            w.Write(1u);
            w.Write(GameEventTypes.PlayerDescription);
            w.Write(0x1u | 0x80u | 0x2u | 0x4u | 0x10u | 0x8u | 0x40u | 0x20u); // int, int64, bool, float, string, data id, instance id, position
            w.Write(10u);
            Table(w, 1, 64, () => { w.Write(0x19u); w.Write(1); });
            Table(w, 1, 64, () => { w.Write(0x01u); w.Write(2L); });
            Table(w, 1, 32, () => { w.Write(0x34u); w.Write(1u); });
            Table(w, 1, 32, () => { w.Write(0x89u); w.Write(0.5d); });
            Table(w, 1, 32, () => { w.Write(0x01u); w.Write((ushort)2); w.Write("Hi"u8); }); // string16L: two bytes of length and two of text are already a multiple of 4
            Table(w, 1, 32, () => { w.Write(0x01u); w.Write(0x02000001u); });
            Table(w, 1, 32, () => { w.Write(0x02u); w.Write(0x80000101u); });
            Table(w, 1, 16, () =>
            {
                w.Write(0x0Eu);
                w.Write(0xA9B40019u);
                w.Write(84f);
                w.Write(7f);
                w.Write(94f);
                w.Write(1f);
                w.Write(0f);
                w.Write(0f);
                w.Write(0f);
            });
            w.Write(0x1u | 0x2u | 0x100u | 0x200u); // attributes, skills, spells, enchantments
            w.Write(1u);
            w.Write(0x1FFu);
            for (var i = 0; i < 6; i++)
            {
                w.Write(0u);
                w.Write(50u);
                w.Write(0u);
            }

            for (var i = 0; i < 3; i++)
            {
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(30u);
            }

            Table(w, 1, 32, () =>
            {
                w.Write(6u);
                w.Write((ushort)5);
                w.Write((ushort)1);
                w.Write(2u);
                w.Write(0u);
                w.Write(10u);
                w.Write(0u);
                w.Write(0d);
            });
            Table(w, 1, 64, () => { w.Write(0x3Bu); w.Write(2f); });
            w.Write(0x1u | 0x2u); // multiplicative and additive
            for (var list = 0; list < 2; list++)
            {
                w.Write(1);
                w.Write((ushort)0x3B);
                w.Write((ushort)(list + 1));
                w.Write((ushort)0x11);
                w.Write((ushort)0);
                w.Write(100u);
                w.Write(-3d);
                w.Write(3600d);
                w.Write(0x50000001u);
                w.Write(0f);
                w.Write(0f);
                w.Write(0d);
                w.Write(0x00008002u);
                w.Write(2u);
                w.Write(10f);
            }

            w.Write(0x1u | 0x8u | 0x20u | 0x40u | 0x400u); // shortcuts, desired components, filters, options 2, eight spell lists
            w.Write(0x50C4A54Au);
            w.Write(1);
            w.Write(0u);
            w.Write(0x80000101u);
            w.Write((ushort)0);
            w.Write((ushort)0);
            for (var bar = 0; bar < 8; bar++)
            {
                w.Write(bar == 0 ? 1 : 0);
                if (bar == 0)
                {
                    w.Write(0x3Bu);
                }
            }

            Table(w, 1, 256, () => { w.Write(0x0Bu); w.Write(25); });
            w.Write(0x3FFFu);
            w.Write(0x00948700u);
            w.Write(1u);
            w.Write(0x80000101u);
            w.Write(0u);
            w.Write(1u);
            w.Write(0x80000102u);
            w.Write(0x200000u);
            w.Write(0u);
        }

        return stream.ToArray();
    }

    private static void Table(BinaryWriter w, ushort count, ushort buckets, Action entries)
    {
        w.Write(count);
        w.Write(buckets);
        entries();
    }
}

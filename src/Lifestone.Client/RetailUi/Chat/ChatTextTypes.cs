using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Chat;

/// <summary>
/// The text types of retail's chat (the <c>i_type</c> of <c>RecvNotice_DisplayFinalStringInfo</c>: 0 to 33, which is the server's chat type, ACE's
/// <c>ChatMessageType</c>), the default text-type mask of each chat window, and the chat colour table (docs/retail-ui.md section 21.2).
/// </summary>
public static class ChatTextTypes
{
    /// <summary>Speech heard (the speaker's line).</summary>
    public const int Speech = 2;

    /// <summary>A tell to the player.</summary>
    public const int Tell = 3;

    /// <summary>The confirmation of a tell the player sent.</summary>
    public const int OutgoingTell = 4;

    /// <summary>System text.</summary>
    public const int System = 5;

    /// <summary>Emotes.</summary>
    public const int Emote = 12;

    /// <summary>The spew box's text type (0x1A): "In Portal Space - Please Wait...", "Cannot log off while in mid-air."; no chat window shows it.</summary>
    public const int SpewBox = 26;

    /// <summary>The number of text types the colour table holds (0 to 33).</summary>
    public const int Count = 34;

    /// <summary>The prefix the Lifestone server mod puts on its machine replies (<c>#lifestone/1 </c>): never shown in a window.</summary>
    public const string MachineChannelPrefix = "#lifestone/1";

    /// <summary>True for a line of the mod's machine channel.</summary>
    public static bool IsMachineLine(string? text) => text is not null && text.AsSpan().TrimStart().StartsWith(MachineChannelPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The text-type mask a chat window starts with, by the window id its master property 0x1000007E holds. <c>ChatInterface::ChatInterface</c> (11.6096
    /// 0x004F51B0) starts every window with every type but 26; <c>ChatInterface::PostInit</c> (11.6096 0x004F4A30) then sets, for ids 1 and 8 (the main
    /// chat) every type below 32 but 26, for 2 types 2, 3, 4 and 12 (speech, tells, emotes), for 3 types 10, 11 and 18, for 4 type 19, for 5 types 27 to
    /// 30, and leaves the constructor's mask for any other id. Read from the user's <c>acclient.exe</c> (docs/research/retail-addresses-m2.md, D55).
    /// </summary>
    public static ulong DefaultMask(uint windowId) => windowId switch
    {
        1 or 8 => 0xFBFFFFFFUL,
        2 => 0x101CUL,
        3 => 0x40C00UL,
        4 => 0x80000UL,
        5 => 0x78000000UL,
        _ => ulong.MaxValue & ~(1UL << SpewBox),
    };

    /// <summary><c>ChatInterface::TypeIsActive</c> (11.6096 0x004F3B70): the mask's bit for <paramref name="textType"/>.</summary>
    public static bool IsActive(ulong mask, int textType) => (uint)textType < 64 && ((mask >> textType) & 1UL) != 0;

    private static readonly UiRgbaColor Green = new(0.5f, 1f, 0.498f, 1f);

    private static readonly UiRgbaColor[] Colors = BuildColors();

    /// <summary>
    /// The colour of text type <paramref name="textType"/> (<c>ChatInterface::BuildChatColorLookupTable</c>, 11.6096 0x004F3E20): the log's colour list
    /// starts all green and is then overridden per type. The floats and the type each takes were read from the user's <c>acclient.exe</c> (D55).
    /// </summary>
    public static UiRgbaColor ColorOf(int textType) => (uint)textType < Count ? Colors[textType] : Green;

    private static UiRgbaColor[] BuildColors()
    {
        var table = new UiRgbaColor[Count];
        Array.Fill(table, Green);
        void Set(float r, float g, float b, params int[] types)
        {
            foreach (var type in types)
            {
                table[type] = new UiRgbaColor(r, g, b, 1f);
            }
        }

        Set(1f, 1f, 1f, 2);
        Set(0.824f, 0.824f, 0.784f, 12);
        Set(1f, 1f, 0.247f, 3, 31, 10, 19);
        Set(0.824f, 0.824f, 0.392f, 4, 11);
        Set(1f, 0.588f, 0.588f, 8, 9);
        Set(0.933f, 0.573f, 0.118f, 18, 33);
        Set(0.706f, 0.863f, 0.941f, 27, 28, 29, 30, 32, 14);
        Set(1f, 0.247f, 0.247f, 15, 6, 21);
        Set(0.96f, 0.459f, 0.447f, 22);
        Set(0.247f, 0.749f, 1f, 7, 17);
        Set(0.247f, 0.863f, 0.863f, 13);
        Set(1f, 0.498f, 1f, 5);
        Set(1f, 0f, 0f, 26);
        return table;
    }
}

/// <summary>What a session chat line becomes in the chat windows: its text type and its text, or nothing for a line that is not shown.</summary>
public static class ChatLineText
{
    /// <summary>
    /// The text type and text of <paramref name="line"/>; false for the mod's machine lines (<see cref="ChatTextTypes.MachineChannelPrefix"/>). Speech and
    /// tells carry a speaker, and retail composes "Name says, ..." from its string table; Lifestone writes the English literal
    /// (<c>TODO(g4-chat-format)</c>: the table's ids are not read yet). A type outside 0 to 33 becomes System.
    /// </summary>
    public static bool TryFormat(in ChatLine line, uint selfGuid, out int textType, out string text)
    {
        textType = ChatTextTypes.System;
        text = line.Text;
        if (ChatTextTypes.IsMachineLine(line.Text))
        {
            return false;
        }

        var name = line.Sender ?? "Someone";
        switch (line.Kind)
        {
            case ChatKind.Speech or ChatKind.RangedSpeech:
                textType = Valid(line.ChatType, ChatTextTypes.Speech);
                text = selfGuid != 0 && line.SenderGuid == selfGuid ? $"You say, \"{line.Text}\"" : $"{name} says, \"{line.Text}\"";
                break;
            case ChatKind.Tell:
                textType = Valid(line.ChatType, ChatTextTypes.Tell);
                text = $"{name} tells you, \"{line.Text}\"";
                break;
            case ChatKind.Emote or ChatKind.SoulEmote:
                textType = ChatTextTypes.Emote;
                text = line.Text.StartsWith(name, StringComparison.Ordinal) ? line.Text : $"{name} {line.Text}";
                break;
            case ChatKind.System or ChatKind.ToolReply:
                textType = Valid(line.ChatType, ChatTextTypes.System);
                break;
            default:
                break;
        }

        return true;
    }

    private static int Valid(uint chatType, int fallback) => chatType is > 0 and < ChatTextTypes.Count ? (int)chatType : fallback;
}

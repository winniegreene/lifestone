// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharacterCreate.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharacterCreateCodec; BuildRequestBody(account, Request, skills) returning a new array became Write(PacketWriter,
// account, CharacterCreateRequest), the N0 record that carries the skills; the 55-skill rule and the name rule are the
// record's Validate; the account and name go through the ASCII guard; is-admin and is-envoy are always 0 (Lifestone never
// asks for privileges); the checksum, checked against retail's ACCharGenResult::CG_Pack, is kept.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// F656 CharacterCreate (docs/protocol.md §5.2): String16L account, then retail's <c>ACCharGenResult</c> as
/// <c>ACCharGenResult::CG_Pack</c> (11.4186 0x005C7200, 11.6096 0x005C81E0) writes it: u32 1; heritage; gender;
/// 14 × u32 appearance; 6 × f64 shades; i32 template; 6 × u32 attributes; u32 slot; u32 class id; u32 skill count and
/// the skills; String16L name; u32 start area; u32 is-admin; u32 is-envoy; u32 checksum.
/// </summary>
/// <remarks>
/// <para><c>Proto_UI::SendCharGenResult</c> (11.4186 0x00546A70, 11.6096 0x00547630) puts the opcode and the account in
/// front. ACE reads up to is-sentinel and ignores the checksum (<c>CharacterCreateInfo.Unpack</c>); retail writes it, so
/// Lifestone does too.</para>
/// <para>The checksum is the u32 sum (wrapping) of heritage, gender, the eyes, nose and mouth strips, hair colour, eye
/// colour, hair, headgear, shirt, trousers and footwear styles, the template and the six attributes. The headgear, shirt,
/// trousers and footwear colours, the shades, the slot and the class id are not in it.</para>
/// </remarks>
internal static class CharacterCreateCodec
{
    /// <summary>The constant first word of <c>CG_Pack</c>.</summary>
    public const uint PackVersion = 1;

    /// <summary>
    /// Writes the whole message into <paramref name="writer"/>. <paramref name="account"/> is the account string of the
    /// latest CharacterList, verbatim (ACE silently ignores a create whose account differs).
    /// </summary>
    /// <exception cref="ArgumentException">The request fails <see cref="CharacterCreateRequest.Validate"/> (not exactly
    /// 55 skills, or a name that is empty or not printable ASCII), or the account is not ASCII.</exception>
    public static void Write(PacketWriter writer, ReadOnlySpan<char> account, in CharacterCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(writer);
        request.Validate();
        if (account.IsEmpty)
        {
            throw new ArgumentException("a CharacterCreate needs the account string of the character list", nameof(account));
        }

        var appearance = request.Appearance;
        writer.WriteUInt32(MessageOpcodes.CharacterCreate);
        writer.WriteAsciiString16L(account, nameof(account));
        writer.WriteUInt32(PackVersion);
        writer.WriteUInt32(request.Heritage);
        writer.WriteUInt32(request.Gender);
        writer.WriteUInt32(appearance.Eyes);
        writer.WriteUInt32(appearance.Nose);
        writer.WriteUInt32(appearance.Mouth);
        writer.WriteUInt32(appearance.HairColor);
        writer.WriteUInt32(appearance.EyeColor);
        writer.WriteUInt32(appearance.HairStyle);
        writer.WriteUInt32(appearance.HeadgearStyle);
        writer.WriteUInt32(appearance.HeadgearColor);
        writer.WriteUInt32(appearance.ShirtStyle);
        writer.WriteUInt32(appearance.ShirtColor);
        writer.WriteUInt32(appearance.TrousersStyle);
        writer.WriteUInt32(appearance.TrousersColor);
        writer.WriteUInt32(appearance.FootwearStyle);
        writer.WriteUInt32(appearance.FootwearColor);
        writer.WriteDouble(appearance.SkinShade);
        writer.WriteDouble(appearance.HairShade);
        writer.WriteDouble(appearance.HeadgearShade);
        writer.WriteDouble(appearance.ShirtShade);
        writer.WriteDouble(appearance.TrousersShade);
        writer.WriteDouble(appearance.FootwearShade);
        writer.WriteInt32(request.TemplateOption);
        writer.WriteUInt32(request.Strength);
        writer.WriteUInt32(request.Endurance);
        writer.WriteUInt32(request.Coordination);
        writer.WriteUInt32(request.Quickness);
        writer.WriteUInt32(request.Focus);
        writer.WriteUInt32(request.Self);
        writer.WriteUInt32(request.Slot);
        writer.WriteUInt32(request.ClassId);
        writer.WriteUInt32((uint)request.SkillAdvancementClasses.Length);
        foreach (var skill in request.SkillAdvancementClasses)
        {
            writer.WriteUInt32(skill);
        }

        writer.WriteAsciiString16L(request.Name, nameof(request.Name));
        writer.WriteUInt32(request.StartArea);
        writer.WriteUInt32(0);  // is-admin: never asked for (owner decision, m2 §7)
        writer.WriteUInt32(0);  // is-envoy
        writer.WriteUInt32(ComputeChecksum(request));
    }

    /// <summary>The trailing checksum <c>CG_Pack</c> writes (see the remarks for the fields it sums).</summary>
    public static uint ComputeChecksum(in CharacterCreateRequest request)
    {
        var a = request.Appearance;
        return unchecked(
            request.Heritage
            + request.Gender
            + a.Eyes
            + a.Nose
            + a.Mouth
            + a.HairColor
            + a.EyeColor
            + a.HairStyle
            + a.HeadgearStyle
            + a.ShirtStyle
            + a.TrousersStyle
            + a.FootwearStyle
            + (uint)request.TemplateOption
            + request.Strength
            + request.Endurance
            + request.Coordination
            + request.Quickness
            + request.Focus
            + request.Self);
    }
}

// Partly ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Messages/CharacterCreateTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Messages; the field
// order, the exact bytes for a short account and name, the checksum's set of fields and the skill-count cases are kept,
// against CharacterCreateCodec.Write and the N0 record CharacterCreateRequest; the field walk is ACE's reader
// (Support.AceClientReaders) instead of an inline one; the null-argument and admin/envoy cases are not taken (the record
// has no nulls and Lifestone never sets the privilege words); the ASCII cases are Lifestone's.

using System.Collections.Immutable;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Messages;

/// <summary>
/// F656 CharacterCreate: retail's <c>CG_Pack</c> layout behind the account string, read back field for field by ACE's
/// <c>CharacterCreateInfo.Unpack</c>, the trailing checksum over exactly retail's fields, and the 55-skill rule.
/// </summary>
public sealed class CharacterCreateCodecTests
{
    private static ImmutableArray<uint> Skills(uint seed = 0, int count = CharacterCreateRequest.RequiredSkillCount) =>
        [.. Enumerable.Range(0, count).Select(i => seed + (uint)i)];

    private static CharacterCreateRequest Request() => new(
        Heritage: 1,
        Gender: 0,
        Appearance: new CharacterCreateAppearance(
            Eyes: 2, Nose: 3, Mouth: 4, HairColor: 5, EyeColor: 6, HairStyle: 7, HeadgearStyle: 8, HeadgearColor: 9,
            ShirtStyle: 10, ShirtColor: 11, TrousersStyle: 12, TrousersColor: 13, FootwearStyle: 14, FootwearColor: 15,
            SkinShade: 0.1, HairShade: 0.2, HeadgearShade: 0.3, ShirtShade: 0.4, TrousersShade: 0.5, FootwearShade: 0.6),
        TemplateOption: 16,
        Strength: 17,
        Endurance: 18,
        Coordination: 19,
        Quickness: 20,
        Focus: 21,
        Self: 22,
        Slot: 0,
        ClassId: 1,
        SkillAdvancementClasses: Skills(),
        Name: "Testcdream",
        StartArea: 23);

    [Fact]
    public void Ace_reads_back_every_field_in_retail_order()
    {
        var request = Request();
        var info = AceClientReaders.ReadCharacterCreate(Write("testaccount", request));

        var a = request.Appearance;
        Assert.Equal("testaccount", info.Account);
        Assert.Equal(1u, info.PackVersion);
        Assert.Equal((request.Heritage, request.Gender), (info.Heritage, info.Gender));
        Assert.Equal(
            new[] { a.Eyes, a.Nose, a.Mouth, a.HairColor, a.EyeColor, a.HairStyle, a.HeadgearStyle, a.HeadgearColor, a.ShirtStyle,
                a.ShirtColor, a.TrousersStyle, a.TrousersColor, a.FootwearStyle, a.FootwearColor },
            info.Appearance);
        Assert.Equal(new[] { a.SkinShade, a.HairShade, a.HeadgearShade, a.ShirtShade, a.TrousersShade, a.FootwearShade }, info.Shades);
        Assert.Equal(request.TemplateOption, info.Template);
        Assert.Equal(
            new[] { request.Strength, request.Endurance, request.Coordination, request.Quickness, request.Focus, request.Self },
            info.Attributes);
        Assert.Equal((request.Slot, request.ClassId), (info.Slot, info.ClassId));
        Assert.Equal(request.SkillAdvancementClasses, info.Skills);
        Assert.Equal(request.Name, info.Name);
        Assert.Equal(request.StartArea, info.StartArea);
        Assert.False(info.IsAdmin);
        Assert.False(info.IsSentinel);

        // ACE stops before the checksum; it is the one word left.
        Assert.Equal(CharacterCreateCodec.ComputeChecksum(request), info.TrailingWord);
        Assert.Equal(0, info.Unread);
    }

    [Fact]
    public void A_short_account_and_name_produce_these_exact_bytes()
    {
        var request = new CharacterCreateRequest(1, 0, default, 0, 0, 0, 0, 0, 0, 0, 0, 1,
            [.. new uint[CharacterCreateRequest.RequiredSkillCount]], "ab", 0);

        var body = Write("cd", request);

        var expected = new List<byte>();
        void U32(uint v) => expected.AddRange(BitConverter.GetBytes(v));
        U32(0xF656);
        expected.AddRange([2, 0, (byte)'c', (byte)'d']);  // String16L "cd": already a multiple of 4
        U32(1);
        U32(1);   // heritage
        U32(0);   // gender
        for (var i = 0; i < 14; i++) U32(0);
        expected.AddRange(new byte[6 * 8]);
        U32(0);   // template
        for (var i = 0; i < 6; i++) U32(0);
        U32(0);   // slot
        U32(1);   // class id
        U32(55);
        for (var i = 0; i < 55; i++) U32(0);
        expected.AddRange([2, 0, (byte)'a', (byte)'b']);
        U32(0);   // start area
        U32(0);   // is-admin
        U32(0);   // is-envoy
        U32(1);   // checksum: heritage 1, all else 0
        Assert.Equal(expected, body);
    }

    [Fact]
    public void The_checksum_sums_exactly_retails_fields()
    {
        var request = Request();
        var a = request.Appearance;
        var expected = unchecked(
            request.Heritage + request.Gender + a.Eyes + a.Nose + a.Mouth + a.HairColor + a.EyeColor + a.HairStyle
            + a.HeadgearStyle + a.ShirtStyle + a.TrousersStyle + a.FootwearStyle + (uint)request.TemplateOption
            + request.Strength + request.Endurance + request.Coordination + request.Quickness + request.Focus + request.Self);

        Assert.Equal(expected, CharacterCreateCodec.ComputeChecksum(request));
        Assert.Equal(205u, expected);
    }

    [Fact]
    public void The_checksum_leaves_out_the_colours_shades_slot_and_class()
    {
        var baseline = Request();
        var mutated = baseline with
        {
            Appearance = baseline.Appearance with
            {
                HeadgearColor = 1009,
                ShirtColor = 1011,
                TrousersColor = 1013,
                FootwearColor = 1015,
                SkinShade = 5.1,
                HairShade = 5.2,
            },
            Slot = 7,
            ClassId = 8,
        };

        Assert.Equal(CharacterCreateCodec.ComputeChecksum(baseline), CharacterCreateCodec.ComputeChecksum(mutated));
    }

    [Fact]
    public void The_checksum_wraps_and_counts_a_negative_template_as_its_bits()
    {
        var request = Request() with { TemplateOption = -1, Strength = uint.MaxValue };
        var expectedWithoutThose = CharacterCreateCodec.ComputeChecksum(Request() with { TemplateOption = 0, Strength = 0 });
        Assert.Equal(unchecked(expectedWithoutThose + uint.MaxValue + uint.MaxValue), CharacterCreateCodec.ComputeChecksum(request));
    }

    [Theory]
    [InlineData(54)]
    [InlineData(56)]
    [InlineData(0)]
    public void Any_skill_count_but_55_throws(int count)
    {
        var request = Request() with { SkillAdvancementClasses = Skills(count: count) };
        var thrown = Assert.Throws<ArgumentException>(() => Write("acct", request));
        Assert.Equal(nameof(CharacterCreateRequest.SkillAdvancementClasses), thrown.ParamName);
    }

    [Fact]
    public void A_default_skill_array_throws()
    {
        Assert.Throws<ArgumentException>(() => Write("acct", Request() with { SkillAdvancementClasses = default }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ælfric")]
    [InlineData("Tab\tName")]
    public void A_name_that_is_not_printable_ascii_throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Write("acct", Request() with { Name = name }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("äccount")]
    public void An_account_that_is_empty_or_not_ascii_throws(string account)
    {
        var thrown = Assert.Throws<ArgumentException>(() => Write(account, Request()));
        Assert.Equal("account", thrown.ParamName);
    }

    private static byte[] Write(string account, CharacterCreateRequest request)
    {
        var writer = new PacketWriter();
        CharacterCreateCodec.Write(writer, account, request);
        return writer.ToArray();
    }
}

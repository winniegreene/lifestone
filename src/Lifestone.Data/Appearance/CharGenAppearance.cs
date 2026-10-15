using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Textures;
using SubPalette = Lifestone.Data.Textures.SubPalette;

namespace Lifestone.Data.Appearance;

/// <summary>
/// The character a set of creation choices produces, as CharGen's 3D view builds it.
/// </summary>
/// <param name="SetupId">The body Setup: the hair style's alternate Setup when it has one, else the sex's Setup
/// (<c>CharGenState::GetSetupID</c>); <see cref="CharGenAppearance.HumanSetupId"/> when both are null.</param>
/// <param name="MotionTableId">The sex's MotionTable, which the created character uses. The view itself plays sequence
/// animations on the Setup and needs none.</param>
/// <param name="PhysicsScriptTableId">The sex's PhysicsScriptTable, as for the created character.</param>
/// <param name="ObjDesc">The ObjDesc the view applies with <see cref="PartArrayAppearance.DoObjDescChangesFromDefault"/>.</param>
/// <param name="BackgroundSetupId">The heritage's environment Setup, which the view adds behind the character (0 for
/// none).</param>
/// <param name="Result">Every step that failed; retail ignores the failures, so they are loud only.</param>
public sealed record CharGenComposition(
    uint SetupId,
    uint MotionTableId,
    uint PhysicsScriptTableId,
    ObjDesc ObjDesc,
    uint BackgroundSetupId,
    AppearanceResult Result);

/// <summary>
/// Composes the CharGen character: <c>gmCG3DView::Update</c> (11.4186 0x004EE9D0; 11.6096 0x004EF660).
/// </summary>
/// <remarks>
/// <para><b>Order</b>, which decides what wins where entries replace each other:</para>
/// <list type="number">
/// <item>The ObjDesc palette id is the sex's <c>BasePalette</c>.</item>
/// <item>The sex's base ObjDesc, then the hair style's (each with <c>ObjDesc::operator+=</c>).</item>
/// <item>Headgear, trousers, shirt, footwear, in that order, each through <see cref="ClothingObjDesc.Build"/> with its
/// palette template and a <see cref="ShadePackage"/> of its one shade. A garment whose ClothingTable is missing is
/// skipped.</item>
/// <item>The eye strip's ObjDesc, or its bald variant when the hair style is bald (<c>CharGenState::GetBaldState</c>,
/// 11.4186 0x005C52A0; 11.6096 0x005C6280); then the nose strip, then the mouth strip.</item>
/// <item>Three sub-palettes, in colours: the skin PaletteSet at the skin shade over entries 0-191; the hair-colour
/// PaletteSet at the hair shade over 192-255; the eye-colour palette over 256-319.</item>
/// </list>
/// <para><b>Setup</b> (<c>CharGenState::GetSetupID</c>, 11.4186 0x005C5A90; 11.6096 0x005C6A70): with a hair style, its
/// <c>AlternateSetup</c> when non-zero, else the sex's Setup. <c>gmCG3DView::Update</c> would also prefer its own
/// <c>m_alternateSetupID</c>, which its constructor (11.4186 0x004EE680) sets to the null id and no function read sets
/// otherwise, so it is not modelled. A null result falls back to <see cref="HumanSetupId"/> (no CharGen sex of the
/// end-of-retail data has a null Setup).</para>
/// <para>The view's animations (<c>gmCG3DView::StartAnimation</c> / <c>StopAnimation</c>) and lighting are
/// <c>CreaturePreview</c>'s, a later step of this track.</para>
/// </remarks>
public static class CharGenAppearance
{
    /// <summary>First entry of the skin sub-palette (colours).</summary>
    public const int SkinOffset = 0;

    /// <summary>Entries of the skin sub-palette.</summary>
    public const int SkinColors = 192;

    /// <summary>First entry of the hair sub-palette.</summary>
    public const int HairOffset = 192;

    /// <summary>Entries of the hair sub-palette.</summary>
    public const int HairColors = 64;

    /// <summary>First entry of the eye sub-palette.</summary>
    public const int EyeOffset = 256;

    /// <summary>Entries of the eye sub-palette.</summary>
    public const int EyeColors = 64;

    /// <summary>
    /// <c>HUMAN_SETUP_ID</c> (11.6096 0x0081D1A8, loaded at 0x004EF6F1): the Setup <c>gmCG3DView::Update</c> uses when its
    /// alternate Setup and CharGenState's are both the null id (docs/research/retail-addresses-m2.md §1.4).
    /// </summary>
    public const uint HumanSetupId = 0x02000001;

    /// <summary>Composes the character <paramref name="look"/> describes.</summary>
    /// <param name="charGen">The CharGen record (<c>0x0E000002</c>).</param>
    /// <param name="look">The choices.</param>
    /// <param name="dats">Where ClothingTables and PaletteSets are read.</param>
    /// <exception cref="ArgumentOutOfRangeException">The heritage or sex does not exist, or an index (other than -1) is
    /// outside its list.</exception>
    public static CharGenComposition Compose(CharGen charGen, in CharGenLook look, IAppearanceDats dats) =>
        Compose(charGen, look, dats, new ObjDescBuilder());

    /// <summary>The sex record a look uses.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The heritage or sex does not exist.</exception>
    public static SexCG GetSex(CharGen charGen, int heritage, int sex)
    {
        ArgumentNullException.ThrowIfNull(charGen);
        if (heritage < 0 || !charGen.HeritageGroups.TryGetValue((uint)heritage, out var group))
        {
            throw new ArgumentOutOfRangeException(nameof(heritage), heritage, "No such CharGen heritage group.");
        }

        if (!group.Genders.TryGetValue(sex, out var sexRecord))
        {
            throw new ArgumentOutOfRangeException(nameof(sex), sex, $"Heritage {heritage} has no such sex.");
        }

        return sexRecord;
    }

    /// <summary>
    /// <c>CharGenState::GetSetupID</c>: the hair style's alternate Setup when it has one, else the sex's Setup; 0 when
    /// both are null (the view then uses <see cref="ResolveViewSetupId"/>'s fallback).
    /// </summary>
    public static uint ResolveSetupId(SexCG sex, int hairStyle)
    {
        ArgumentNullException.ThrowIfNull(sex);
        if (hairStyle == -1)
        {
            return sex.SetupId.DataId;
        }

        var alternate = Item(sex.HairStyles, hairStyle, nameof(hairStyle)).AlternateSetup;
        return alternate != 0 ? alternate : sex.SetupId.DataId;
    }

    /// <summary>
    /// The Setup <c>gmCG3DView::Update</c> builds the character on: <see cref="ResolveSetupId"/>, or
    /// <see cref="HumanSetupId"/> when that is the null id.
    /// </summary>
    public static uint ResolveViewSetupId(SexCG sex, int hairStyle)
    {
        var setupId = ResolveSetupId(sex, hairStyle);
        return setupId != 0 ? setupId : HumanSetupId;
    }

    /// <summary>The composition, built into <paramref name="into"/> (tests pass a builder with another duplicate rule).</summary>
    internal static CharGenComposition Compose(CharGen charGen, in CharGenLook look, IAppearanceDats dats, ObjDescBuilder into)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(into);
        var sex = GetSex(charGen, look.Heritage, look.Sex);
        var group = charGen.HeritageGroups[(uint)look.Heritage];
        var problems = new List<string>();

        var setupId = ResolveViewSetupId(sex, look.HairStyle);

        into.PaletteId = sex.BasePalette.DataId;
        Add(into, sex.BaseObjDesc);
        if (look.HairStyle != -1)
        {
            Add(into, Item(sex.HairStyles, look.HairStyle, nameof(look.HairStyle)).ObjDesc);
        }

        AddGarment(into, sex.Headgears, look.Headgear, setupId, dats, problems, "headgear");
        AddGarment(into, sex.Pants, look.Pants, setupId, dats, problems, "trousers");
        AddGarment(into, sex.Shirts, look.Shirt, setupId, dats, problems, "shirt");
        AddGarment(into, sex.Footwear, look.Footwear, setupId, dats, problems, "footwear");

        if (look.EyeStrip != -1)
        {
            var eyes = Item(sex.EyeStrips, look.EyeStrip, nameof(look.EyeStrip));
            var bald = look.HairStyle != -1 && sex.HairStyles[look.HairStyle].Bald;
            Add(into, bald ? eyes.BaldObjDesc : eyes.ObjDesc);
        }

        if (look.NoseStrip != -1)
        {
            Add(into, Item(sex.NoseStrips, look.NoseStrip, nameof(look.NoseStrip)).ObjDesc);
        }

        if (look.MouthStrip != -1)
        {
            Add(into, Item(sex.MouthStrips, look.MouthStrip, nameof(look.MouthStrip)).ObjDesc);
        }

        AddShade(into, sex.SkinPalSet.DataId, look.SkinShade, SkinOffset, SkinColors, dats, problems, "skin");
        if (look.HairColor != -1)
        {
            AddShade(into, Item(sex.HairColors, look.HairColor, nameof(look.HairColor)), look.HairShade, HairOffset, HairColors, dats, problems, "hair");
        }

        if (look.EyeColor != -1)
        {
            into.AddSubPalette(new SubPalette(Item(sex.EyeColors, look.EyeColor, nameof(look.EyeColor)), EyeOffset, EyeColors));
        }

        return new CharGenComposition(
            setupId,
            sex.MotionTable.DataId,
            sex.PhysicsTable.DataId,
            into.Build(),
            group.EnvironmentSetupId.DataId,
            AppearanceResult.From(problems));
    }

    private static void Add(ObjDescBuilder into, DatReaderWriter.Types.ObjDesc? record)
    {
        if (record is not null)
        {
            into.Add(ObjDesc.FromDat(record));
        }
    }

    private static void AddGarment(
        ObjDescBuilder into, List<GearCG> list, GearChoice choice, uint setupId, IAppearanceDats dats, List<string> problems, string what)
    {
        if (choice.Index == -1)
        {
            return;
        }

        var gear = Item(list, choice.Index, what);
        var table = dats.GetClothingTable(gear.ClothingTable.DataId);
        if (table is null)
        {
            // gmCG3DView::Update skips a garment whose table does not load.
            problems.Add($"{what} {choice.Index}: ClothingTable 0x{gear.ClothingTable.DataId:X8} not found");
            return;
        }

        var result = ClothingObjDesc.Build(table, setupId, choice.PaletteTemplate, new ShadePackage(choice.Shade), dats, into);
        foreach (var problem in result.Problems)
        {
            problems.Add($"{what} {choice.Index}: {problem}");
        }
    }

    private static void AddShade(
        ObjDescBuilder into, uint palSetId, double shade, int offset, int colors, IAppearanceDats dats, List<string> problems, string what)
    {
        var palSet = dats.GetPalSet(palSetId);
        if (palSet is null)
        {
            problems.Add($"{what}: PaletteSet 0x{palSetId:X8} not found");
            return;
        }

        into.AddSubPalette(new SubPalette(PaletteSets.GetPaletteId(palSet, shade), offset, colors));
    }

    private static T Item<T>(List<T> list, int index, string name) =>
        index >= 0 && index < list.Count
            ? list[index]
            : throw new ArgumentOutOfRangeException(name, index, $"Index outside the {list.Count} CharGen entries.");
}

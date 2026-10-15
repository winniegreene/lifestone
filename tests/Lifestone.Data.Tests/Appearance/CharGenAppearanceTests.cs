using System.Globalization;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using ObjDesc = Lifestone.Data.Appearance.ObjDesc;
using SubPalette = Lifestone.Data.Textures.SubPalette;

namespace Lifestone.Data.Tests.Appearance;

/// <summary>
/// <see cref="CharGenAppearance.Compose"/>: retail <c>gmCG3DView::Update</c> over the user's CharGen record, and the
/// world look ACE would send for the same character.
/// </summary>
public sealed class CharGenAppearanceTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void An_Aluvian_male_composes_in_retail_order_with_skin_hair_and_eye_palettes()
    {
        var cg = CharGen();
        var dats = new DatAppearanceDats(Dats);
        var look = FirstChoiceLooks(cg, dats).First(l => l.Heritage == 1 && l.Sex == 1) with { SkinShade = 0.25, HairShade = 0.75 };
        var sex = CharGenAppearance.GetSex(cg, 1, 1);
        var c = CharGenAppearance.Compose(cg, look, dats);

        Assert.True(c.Result.AllApplied, c.Result.ToString());
        Assert.Equal(0x02000001u, c.SetupId);
        Assert.Equal(0x09000001u, c.MotionTableId);
        Assert.Equal(0x34000004u, c.PhysicsScriptTableId);
        Assert.Equal(0x020005A1u, c.BackgroundSetupId);
        Assert.Equal(sex.BasePalette.DataId, c.ObjDesc.PaletteId);

        // The last three sub-palettes are skin, hair and eyes, in that order, after the clothing's.
        var subs = c.ObjDesc.SubPalettes;
        var skin = PaletteSets.GetPaletteId(dats.GetPalSet(sex.SkinPalSet.DataId)!, 0.25);
        var hair = PaletteSets.GetPaletteId(dats.GetPalSet(sex.HairColors[0])!, 0.75);
        Assert.Equal(new SubPalette(skin, 0, 192), subs[^3]);
        Assert.Equal(new SubPalette(hair, 192, 64), subs[^2]);
        Assert.Equal(new SubPalette(sex.EyeColors[0], 256, 64), subs[^1]);

        // The hair style's part change (the head) and the face strips' texture changes are in.
        var hairStyle = ObjDesc.FromDat(sex.HairStyles[0].ObjDesc);
        Assert.All(hairStyle.PartChanges, p => Assert.Contains(p, c.ObjDesc.PartChanges));
        Assert.Contains(ObjDesc.FromDat(sex.NoseStrips[0].ObjDesc).TextureChanges[0], c.ObjDesc.TextureChanges);
        Assert.Contains(ObjDesc.FromDat(sex.MouthStrips[0].ObjDesc).TextureChanges[0], c.ObjDesc.TextureChanges);
    }

    [RequiresDatsFact]
    public void A_bald_hair_style_takes_the_eye_strips_bald_ObjDesc()
    {
        var cg = CharGen();
        var dats = new DatAppearanceDats(Dats);
        var sex = CharGenAppearance.GetSex(cg, 1, 1);
        var bald = sex.HairStyles.FindIndex(h => h.Bald);
        Assert.True(bald >= 0, "no bald Aluvian male hair style");
        var eyes = sex.EyeStrips.FindIndex(e => !ObjDesc.FromDat(e.ObjDesc).CacheKey.Equals(ObjDesc.FromDat(e.BaldObjDesc).CacheKey, StringComparison.Ordinal));
        Assert.True(eyes >= 0, "no eye strip whose bald variant differs");
        var look = FirstChoiceLooks(cg, dats).First(l => l.Heritage == 1 && l.Sex == 1) with { HairStyle = bald, EyeStrip = eyes };

        var c = CharGenAppearance.Compose(cg, look, dats);
        Assert.All(ObjDesc.FromDat(sex.EyeStrips[eyes].BaldObjDesc).TextureChanges, t => Assert.Contains(t, c.ObjDesc.TextureChanges));
    }

    [RequiresDatsFact]
    public void A_hair_style_with_an_alternate_Setup_changes_the_body()
    {
        var cg = CharGen();
        var dats = new DatAppearanceDats(Dats);
        var tumerok = CharGenAppearance.GetSex(cg, 7, 1);
        Assert.Equal(0x02001A1Au, CharGenAppearance.ResolveSetupId(tumerok, 0));
        var undead = CharGenAppearance.GetSex(cg, 11, 1);
        var skeleton = undead.HairStyles.FindIndex(h => h.AlternateSetup == 0x02001A9C);
        Assert.True(skeleton >= 0);
        Assert.Equal(0x02001A9Cu, CharGenAppearance.ResolveSetupId(undead, skeleton));
        Assert.Equal(undead.SetupId.DataId, CharGenAppearance.ResolveSetupId(undead, -1));

        var look = FirstChoiceLooks(cg, dats).First(l => l.Heritage == 11 && l.Sex == 1) with { HairStyle = skeleton };
        var c = CharGenAppearance.Compose(cg, look, dats);
        Assert.Equal(0x02001A9Cu, c.SetupId);

        // The Undead clothing tables have no entry for the skeleton Setup: retail's heritage fallback dresses it as the
        // Undead male body 0x02001A0E.
        Assert.True(c.Result.AllApplied, c.Result.ToString());
        var shirt = dats.GetClothingTable(undead.Shirts[look.Shirt.Index].ClothingTable.DataId)!;
        Assert.DoesNotContain(shirt.ClothingBaseEffects.Keys, k => k.DataId == 0x02001A9C);
        var dressed = new ObjDescBuilder();
        Assert.True(ClothingObjDesc.Build(shirt, 0x02001A0E, look.Shirt.PaletteTemplate, new ShadePackage(look.Shirt.Shade), dats, dressed).AllApplied);
        Assert.NotEmpty(dressed.PartChanges);
        Assert.All(dressed.Build().PartChanges, p => Assert.Contains(p, c.ObjDesc.PartChanges));
    }

    [Fact]
    public void A_null_CharGen_Setup_falls_back_to_HUMAN_SETUP_ID()
    {
        var sex = new SexCG { SetupId = new QualifiedDataId<Setup> { DataId = 0 } };
        sex.HairStyles.Add(new HairStyleCG { AlternateSetup = 0 });
        sex.HairStyles.Add(new HairStyleCG { AlternateSetup = 0x02001A9C });
        Assert.Equal(0u, CharGenAppearance.ResolveSetupId(sex, 0));
        Assert.Equal(CharGenAppearance.HumanSetupId, CharGenAppearance.ResolveViewSetupId(sex, 0));
        Assert.Equal(CharGenAppearance.HumanSetupId, CharGenAppearance.ResolveViewSetupId(sex, -1));
        Assert.Equal(0x02001A9Cu, CharGenAppearance.ResolveViewSetupId(sex, 1));
        Assert.Equal(0x02000001u, CharGenAppearance.HumanSetupId);
    }

    [RequiresDatsFact]
    public void Invalid_choices_throw()
    {
        var cg = CharGen();
        var dats = new DatAppearanceDats(Dats);
        var look = FirstChoiceLooks(cg, dats).First();
        Assert.Throws<ArgumentOutOfRangeException>(() => CharGenAppearance.Compose(cg, look with { Heritage = 99 }, dats));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharGenAppearance.Compose(cg, look with { Sex = 3 }, dats));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharGenAppearance.Compose(cg, look with { MouthStrip = 999 }, dats));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharGenAppearance.Compose(cg, look with { Shirt = look.Shirt with { Index = 999 } }, dats));
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_heritage_and_sex_composes_every_choice_and_applies_to_its_Setup()
    {
        var cg = CharGen();
        var source = new DatAppearanceSource(new TextureLoader(Dats));
        var composed = 0;
        var problems = new SortedDictionary<int, SortedSet<string>>();
        foreach (var look in EveryChoiceLooks(cg, source))
        {
            var c = CharGenAppearance.Compose(cg, look, source);
            var setup = ModelAssembly.Load(Dats, c.SetupId)!;
            var parts = PartArrayAppearance.Create(setup, source);
            Assert.True(parts.IsLoadable, $"Setup 0x{c.SetupId:X8}");
            var applied = parts.DoObjDescChangesFromDefault(c.ObjDesc);
            foreach (var p in c.Result.Problems.Concat(applied.Problems))
            {
                if (!problems.TryGetValue(look.Heritage, out var set))
                {
                    problems[look.Heritage] = set = [];
                }

                set.Add(p);
            }

            composed++;
        }

        // Per heritage, every problem in full.
        var report = string.Join('\n', problems.Select(kv => $"{kv.Key}: " + string.Join(" | ", kv.Value)));
        Assert.Equal(PinnedLookCount, composed);
        Assert.True(PinnedProblems == report, "problems per heritage changed:\n" + report);
    }

    [RequiresDatsFact]
    public void The_world_look_ACE_sends_matches_the_CharGen_preview_except_the_listed_differences()
    {
        var cg = CharGen();
        var source = new DatAppearanceSource(new TextureLoader(Dats));
        var differences = new List<string>();
        foreach (var look in FirstChoiceLooks(cg, source))
        {
            var preview = CharGenAppearance.Compose(cg, look, source);
            var previewParts = PartArrayAppearance.Create(ModelAssembly.Load(Dats, preview.SetupId)!, source);
            previewParts.DoObjDescChangesFromDefault(preview.ObjDesc);

            var player = AceObjDescOracle.CreatePlayer(
                cg,
                look.Heritage,
                look.Sex,
                new AceObjDescOracle.AceAppearance(
                    (uint)look.EyeStrip, (uint)look.NoseStrip, (uint)look.MouthStrip, (uint)look.HairColor, (uint)look.EyeColor,
                    (uint)look.HairStyle, look.SkinShade, look.HairShade),
                source.GetPalSet);
            var sex = CharGenAppearance.GetSex(cg, look.Heritage, look.Sex);
            var worn = AceStarterClothing(look, sex);
            var ace = AceObjDescOracle.CalculateObjDesc(player, worn, cg, source.GetClothingTable, source.GetPalSet, SetupParts);
            Assert.True(ObjDesc.TryUnpack(AceObjDescOracle.SerializeModelData(ace), out var wire, out _, out var failure), failure);
            var worldParts = PartArrayAppearance.Create(ModelAssembly.Load(Dats, player.SetupTableId)!, source);
            worldParts.DoObjDescChangesFromDefault(wire);

            var a = Snapshot(previewParts);
            var b = Snapshot(worldParts);
            Assert.Equal(a.Count, b.Count);
            var byAspect = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < a.Count; i++)
            {
                var aspects = string.Join(
                    '+',
                    new[] { ("gfxobj", a[i].GfxObj != b[i].GfxObj), ("textures", a[i].Textures != b[i].Textures), ("palette", a[i].Palette != b[i].Palette) }
                        .Where(x => x.Item2)
                        .Select(x => x.Item1));
                if (aspects.Length > 0)
                {
                    (byAspect.TryGetValue(aspects, out var parts) ? parts : byAspect[aspects] = []).Add(i);
                }
            }

            if (byAspect.Count > 0)
            {
                differences.Add($"{look.Heritage}/{look.Sex}: " + string.Join("; ", byAspect.Select(kv => $"{kv.Key} {Ranges(kv.Value)}")));
            }
        }

        var actual = string.Join('\n', differences);
        Assert.True(PinnedAceDifferences == actual, "differences changed:\n" + actual);
    }

    private const int PinnedLookCount = 2932;

    /// <summary>
    /// The problems of every composition: none. The Undead skeleton and zombie bodies take retail's heritage fallback
    /// (ClothingObjDesc.GetHeritageFallbackSetupId), and the Gear Knight and Olthoi bodies, in no fallback group, get no
    /// clothing and no problem, as in retail.
    /// </summary>
    private const string PinnedProblems = "";

    /// <summary>
    /// Where ACE's world look differs from the CharGen preview at the first choices (docs/avatars.md): Gear Knights get
    /// no starter clothing from ACE, so their shirt and trousers dyes are missing (palette). Every other heritage matches
    /// part for part, the Undead skeleton bodies included: ACE's SetupConst mapping gives them retail's fallback (it
    /// differs from retail only for Umbraen female void, which no first choice uses).
    /// </summary>
    private const string PinnedAceDifferences =
        "6/1: palette 0-15\n" +
        "6/2: palette 0-15";

    private CharGen CharGen()
    {
        Assert.True(Dats.TryGetPortal<CharGen>(ObjDescTests.CharGenId, out var cg));
        return cg;
    }

    private IReadOnlyList<uint> SetupParts(uint setupId) => ModelAssembly.Load(Dats, setupId)!.Parts.Select(p => p.GfxObjId).ToArray();

    /// <summary>
    /// The starter clothing ACE equips for <paramref name="look"/>, in the order its CalculateObjDesc applies it. ACE
    /// creates each garment from the CharGen list's default weenie, whose ClothingBase is assumed to be the list's
    /// ClothingTable. The weenies' ClothingPriority (CoverageMask) values are not available here, so the order is
    /// assumed: trousers (underwear legs and abdomen) before shirts (underwear chest and arms), both clothing and sorted
    /// ahead of headgear and footwear (extremity slots, ACE's armour list). Gear Knights and Olthoi get none
    /// (PlayerFactory).
    /// </summary>
    private static List<AceObjDescOracle.AceClothing> AceStarterClothing(CharGenLook look, SexCG sex)
    {
        var worn = new List<AceObjDescOracle.AceClothing>();
        if (look.Heritage is 6 or 12 or 13)
        {
            return worn;
        }

        void Wear(List<GearCG> list, GearChoice choice)
        {
            if (choice.Index >= 0)
            {
                worn.Add(new(list[choice.Index].ClothingTable.DataId, choice.PaletteTemplate, choice.Shade));
            }
        }

        Wear(sex.Pants, look.Pants);
        Wear(sex.Shirts, look.Shirt);
        Wear(sex.Headgears, look.Headgear);
        Wear(sex.Footwear, look.Footwear);
        return worn;
    }

    /// <summary>Per part: the GfxObj, the texture swaps and the palette's colours (as a hash), comparable across ObjDescs.</summary>
    private static List<(string GfxObj, string Textures, string Palette)> Snapshot(PartArrayAppearance parts) =>
        [.. parts.Parts.Select(p => (
            $"0x{p.Chain.RootGfxObjId:X8}",
            string.Concat(p.SurfaceTextureBySurfaceId.OrderBy(kv => kv.Key).Select(kv => $" {kv.Key:X8}={kv.Value:X8}")),
            p.Palette is null ? "" : PaletteHash(p.Palette)))];

    /// <summary>Part indices as ranges: "0-11,13".</summary>
    private static string Ranges(List<int> indices)
    {
        var runs = new List<string>();
        for (var i = 0; i < indices.Count;)
        {
            var j = i;
            while (j + 1 < indices.Count && indices[j + 1] == indices[j] + 1)
            {
                j++;
            }

            runs.Add(i == j ? $"{indices[i]}" : $"{indices[i]}-{indices[j]}");
            i = j + 1;
        }

        return string.Join(',', runs);
    }

    private static string PaletteHash(PaletteData p)
    {
        var hash = new HashCode();
        foreach (var c in p.Argb)
        {
            hash.Add(c);
        }

        return hash.ToHashCode().ToString("X8", CultureInfo.InvariantCulture);
    }

    /// <summary>Index 0 of every list (or none for an empty list), mid shades, the first colour each garment offers.</summary>
    internal static IEnumerable<CharGenLook> FirstChoiceLooks(CharGen cg, IAppearanceDats dats)
    {
        foreach (var (heritage, group) in cg.HeritageGroups.OrderBy(kv => kv.Key))
        {
            foreach (var (sexId, sex) in group.Genders.OrderBy(kv => kv.Key))
            {
                yield return new CharGenLook(
                    (int)heritage,
                    sexId,
                    First(sex.HairStyles),
                    First(sex.HairColors),
                    0.5,
                    First(sex.EyeStrips),
                    First(sex.EyeColors),
                    First(sex.NoseStrips),
                    First(sex.MouthStrips),
                    0.5,
                    Gear(sex, sex.Headgears, 0, dats),
                    Gear(sex, sex.Shirts, 0, dats),
                    Gear(sex, sex.Pants, 0, dats),
                    Gear(sex, sex.Footwear, 0, dats));
            }
        }
    }

    /// <summary><see cref="FirstChoiceLooks"/> with each hair style in turn.</summary>
    internal static IEnumerable<CharGenLook> EveryHairStyleLooks(CharGen cg, IAppearanceDats dats)
    {
        foreach (var look in FirstChoiceLooks(cg, dats))
        {
            var sex = CharGenAppearance.GetSex(cg, look.Heritage, look.Sex);
            for (var h = 0; h < sex.HairStyles.Count; h++)
            {
                yield return look with { HairStyle = h };
            }
        }
    }

    /// <summary>Every entry of every list, one list at a time, the others at their first choice.</summary>
    private static IEnumerable<CharGenLook> EveryChoiceLooks(CharGen cg, IAppearanceDats dats)
    {
        foreach (var look in FirstChoiceLooks(cg, dats))
        {
            var sex = CharGenAppearance.GetSex(cg, look.Heritage, look.Sex);
            for (var i = 0; i < sex.HairStyles.Count; i++)
            {
                yield return look with { HairStyle = i };
            }

            for (var i = 0; i < sex.EyeStrips.Count; i++)
            {
                yield return look with { EyeStrip = i };
                if (sex.HairStyles.FindIndex(h => h.Bald) is var bald and >= 0)
                {
                    yield return look with { EyeStrip = i, HairStyle = bald };
                }
            }

            for (var i = 0; i < sex.NoseStrips.Count; i++)
            {
                yield return look with { NoseStrip = i };
            }

            for (var i = 0; i < sex.MouthStrips.Count; i++)
            {
                yield return look with { MouthStrip = i };
            }

            for (var i = 0; i < sex.HairColors.Count; i++)
            {
                yield return look with { HairColor = i };
            }

            for (var i = 0; i < sex.EyeColors.Count; i++)
            {
                yield return look with { EyeColor = i };
            }

            for (var i = 0; i < sex.Headgears.Count; i++)
            {
                yield return look with { Headgear = Gear(sex, sex.Headgears, i, dats) };
            }

            for (var i = 0; i < sex.Shirts.Count; i++)
            {
                yield return look with { Shirt = Gear(sex, sex.Shirts, i, dats) };
            }

            for (var i = 0; i < sex.Pants.Count; i++)
            {
                yield return look with { Pants = Gear(sex, sex.Pants, i, dats) };
            }

            for (var i = 0; i < sex.Footwear.Count; i++)
            {
                yield return look with { Footwear = Gear(sex, sex.Footwear, i, dats) };
            }

            yield return look with { Headgear = GearChoice.None, Shirt = GearChoice.None, Pants = GearChoice.None, Footwear = GearChoice.None };
        }
    }

    private static int First<T>(List<T> list) => list.Count > 0 ? 0 : -1;

    /// <summary>
    /// Garment <paramref name="index"/> dyed with the first of the sex's clothing colours its table offers (retail's colour
    /// buttons list exactly those templates, CharGenState::StoreColorInformation), at shade 0.5.
    /// </summary>
    private static GearChoice Gear(SexCG sex, List<GearCG> list, int index, IAppearanceDats dats)
    {
        if (index >= list.Count)
        {
            return GearChoice.None;
        }

        var table = dats.GetClothingTable(list[index].ClothingTable.DataId);
        var template = table is null
            ? 0
            : sex.ClothingColors.FirstOrDefault(c => table.ClothingSubPalEffects.TryGetValue(c, out var e) && e.CloSubPalettes.Count > 0);
        return new GearChoice(index, template, 0.5);
    }
}

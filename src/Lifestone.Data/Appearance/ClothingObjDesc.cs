using DatReaderWriter.DBObjs;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Appearance;

/// <summary>
/// The shades a piece of clothing is dyed with: one value per sub-palette effect, the last one repeating
/// (<c>ShadePackage</c>, 11.4186 0x005A8290 / 0x005A82C0; 11.6096 0x005A9340 / 0x005A9370).
/// </summary>
/// <remarks>
/// <c>ShadePackage::ShadePackage(val)</c> fills all four slots with <c>val</c>, which is how CharGen builds one for each
/// garment (<c>gmCG3DView::Update</c>). <c>ShadePackage::GetVal(i)</c> returns slot <c>i</c>, or slot 3 for any
/// <c>i</c> of 4 or more.
/// </remarks>
public readonly record struct ShadePackage(double Shade0, double Shade1, double Shade2, double Shade3)
{
    /// <summary><c>ShadePackage::ShadePackage(val)</c>: every slot holds <paramref name="shade"/>.</summary>
    public ShadePackage(double shade)
        : this(shade, shade, shade, shade)
    {
    }

    /// <summary><c>ShadePackage::GetVal</c>: slot <paramref name="index"/>, or the last slot from 4 on.</summary>
    public double GetVal(int index) => index switch
    {
        0 => Shade0,
        1 => Shade1,
        2 => Shade2,
        _ => Shade3,
    };
}

/// <summary>
/// What a ClothingTable adds to an ObjDesc for one Setup: <c>ClothingTable::BuildObjDesc</c> (11.4186 0x005A7900;
/// 11.6096 0x005A89B0).
/// </summary>
/// <remarks>
/// <para><b>Steps</b> (retail order):</para>
/// <list type="number">
/// <item>The Setup's entry in the table's base effects (<c>ClothingBaseEffects</c>). On a miss, retail maps the
/// Umbraen, Penumbraen and Undead body Setups and the two Anakshay Setups to per-heritage default clothing Setups
/// (<see cref="GetHeritageFallbackSetupId"/>) and looks that up instead; any other Setup, or a default the table lacks
/// too, adds nothing and succeeds.</item>
/// <item><c>ClothingBase::ApplyPartAndTextureChanges</c> (11.4186 0x005A8EB0; 11.6096 0x005A9F60): for each object
/// effect in order, a part change (part, GfxObj), then a texture change per texture effect. A null old or new texture
/// is still added but fails the call, which then stops before the palettes.</item>
/// <item>A palette template of 0 stops here with success. A template the table lacks fails (ACE falls back to the
/// table's first template; retail does not).</item>
/// <item>For sub-palette effect <c>i</c>: its PaletteSet (a missing one fails the call), the palette for shade
/// <c>GetVal(i)</c> (<see cref="PaletteSets.GetPaletteId"/>, the null id for a shade outside [0, 1]), and one
/// sub-palette per range. Ranges are in colours already (<see cref="SubPalette.FromColorRange"/>).</item>
/// </list>
/// <para>Every entry goes through the <see cref="ObjDescBuilder"/> rules, as retail's <c>ObjDesc::Add*</c> calls do.</para>
/// </remarks>
public static class ClothingObjDesc
{
    /// <summary>
    /// True: the heritage fallback Setups of <c>ClothingTable::BuildObjDesc</c> are read (owner decision D4, step L10;
    /// docs/research/retail-addresses-m2.md §1.4) and applied by <see cref="Build"/>.
    /// </summary>
    public const bool HeritageFallbacksKnown = true;

    /// <summary><c>HUMAN_MALE_CLOTHING_DEFAULT</c> (11.6096 0x008220D4): the Anakshay male fallback.</summary>
    public const uint HumanMaleClothingDefault = 0x02000001;

    /// <summary><c>HUMAN_FEMALE_CLOTHING_DEFAULT</c> (11.6096 0x008220D8): the Anakshay female fallback.</summary>
    public const uint HumanFemaleClothingDefault = 0x0200004E;

    /// <summary>
    /// The Setup <c>ClothingTable::BuildObjDesc</c> (11.4186 0x005A7900, 11.6096 0x005A89B0) looks up when the table has
    /// no entry for <paramref name="setupId"/>, or 0 (retail's null id: nothing is applied) for a Setup in none of its
    /// groups. The values were read from the 11.6096 executable's data (docs/research/retail-addresses-m2.md §1.4,
    /// compared at 0x005A89F8-0x005A8B1D, in this order):
    /// <list type="bullet">
    /// <item>Umbraen male (crown, crown gen, no crown, void) → its crown Setup 0x0200196F; female → 0x02001970.</item>
    /// <item>Penumbraen male → 0x0200196E; female → 0x0200196D.</item>
    /// <item>Undead male (gen, skeleton, skeleton without flame, zombie, zombie without flame) → 0x02001A0E; female →
    /// 0x02001A0C.</item>
    /// <item>Anakshay male 0x02001AA3 → <see cref="HumanMaleClothingDefault"/>; female 0x02001AA4 →
    /// <see cref="HumanFemaleClothingDefault"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// ACE's <c>Creature.CalculateObjDesc</c> maps the same groups through its <c>SetupConst</c>, except Umbraen female void,
    /// which ACE spells 0x2001A6EE (retail compares 0x02001A6E).
    /// </remarks>
    public static uint GetHeritageFallbackSetupId(uint setupId) => setupId switch
    {
        0x0200196F or 0x02001972 or 0x02001A5F or 0x02001A6F => 0x0200196F,
        0x02001970 or 0x02001A5E or 0x02001A6E => 0x02001970,
        0x0200196E or 0x02001971 or 0x02001A5D or 0x02001A70 => 0x0200196E,
        0x0200196D or 0x02001A5C or 0x02001A71 => 0x0200196D,
        0x02001A0F or 0x02001A9C or 0x02001A9E or 0x02001A9D or 0x02001A96 => 0x02001A0E,
        0x02001A0D or 0x02001AA0 or 0x02001A9F or 0x02001AA1 or 0x02001AA2 => 0x02001A0C,
        0x02001AA3 => HumanMaleClothingDefault,
        0x02001AA4 => HumanFemaleClothingDefault,
        _ => 0,
    };

    /// <summary>
    /// Adds what <paramref name="table"/> gives Setup <paramref name="setupId"/>, dyed with palette template
    /// <paramref name="paletteTemplate"/> and <paramref name="shades"/>, to <paramref name="into"/>.
    /// </summary>
    /// <param name="table">The ClothingTable (<c>0x10xxxxxx</c>).</param>
    /// <param name="setupId">The wearer's Setup (after any alternate Setup).</param>
    /// <param name="paletteTemplate">The palette template (a key of <c>ClothingSubPalEffects</c>), 0 for none.</param>
    /// <param name="shades">The shades, one per sub-palette effect.</param>
    /// <param name="dats">Where PaletteSets are read.</param>
    /// <param name="into">The ObjDesc being built.</param>
    /// <returns>Retail's return value, with a line per failure. Retail's callers ignore it; a failure here is loud
    /// only.</returns>
    public static AppearanceResult Build(
        ClothingTable table, uint setupId, uint paletteTemplate, ShadePackage shades, IAppearanceDats dats, ObjDescBuilder into)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(into);

        var baseEffect = Find(table, setupId);
        if (baseEffect is null)
        {
            // Retail: a Setup in no fallback group, or a fallback the table lacks too, applies nothing and succeeds.
            var fallback = GetHeritageFallbackSetupId(setupId);
            baseEffect = fallback == 0 ? null : Find(table, fallback);
            if (baseEffect is null)
            {
                return AppearanceResult.Success;
            }
        }

        List<string>? problems = null;
        foreach (var objectEffect in baseEffect.CloObjectEffects)
        {
            if (objectEffect.Index > byte.MaxValue)
            {
                // Retail keeps the 32-bit index and CPartArray::SetPart refuses it later; a byte cannot hold it.
                (problems ??= []).Add($"ClothingTable 0x{table.Id:X8}: part index {objectEffect.Index} does not fit an ObjDesc change");
                continue;
            }

            var part = (byte)objectEffect.Index;
            into.AddAnimPartChange(new AnimPartChange(part, objectEffect.ModelId.DataId));
            foreach (var textureEffect in objectEffect.CloTextureEffects)
            {
                uint oldTexture = textureEffect.OldTexture.DataId, newTexture = textureEffect.NewTexture.DataId;
                if (oldTexture == 0 || newTexture == 0)
                {
                    (problems ??= []).Add(
                        $"ClothingTable 0x{table.Id:X8}: part {part} texture effect 0x{oldTexture:X8} -> 0x{newTexture:X8} has a null id "
                        + "(ClothingBase::ApplyPartAndTextureChanges)");
                }

                into.AddTextureMapChange(new TextureMapChange(part, oldTexture, newTexture));
            }
        }

        if (problems is not null)
        {
            return AppearanceResult.From(problems);
        }

        if (paletteTemplate == 0)
        {
            return AppearanceResult.Success;
        }

        if (!table.ClothingSubPalEffects.TryGetValue(paletteTemplate, out var subPalEffect))
        {
            return AppearanceResult.Failure($"ClothingTable 0x{table.Id:X8} has no palette template {paletteTemplate}");
        }

        for (var i = 0; i < subPalEffect.CloSubPalettes.Count; i++)
        {
            var cloSubPalette = subPalEffect.CloSubPalettes[i];
            var palSet = dats.GetPalSet(cloSubPalette.PaletteSet.DataId);
            if (palSet is null)
            {
                return AppearanceResult.Failure(
                    $"ClothingTable 0x{table.Id:X8} template {paletteTemplate}: PaletteSet 0x{cloSubPalette.PaletteSet.DataId:X8} not found");
            }

            var paletteId = PaletteSets.GetPaletteId(palSet, shades.GetVal(i));
            foreach (var range in cloSubPalette.Ranges)
            {
                into.AddSubPalette(SubPalette.FromColorRange(paletteId, range.Offset, range.NumColors));
            }
        }

        return AppearanceResult.Success;
    }

    /// <summary>The table's <c>ClothingBase</c> for <paramref name="setupId"/>, or null.</summary>
    private static DatReaderWriter.Types.ClothingBaseEffect? Find(ClothingTable table, uint setupId)
    {
        foreach (var (key, effect) in table.ClothingBaseEffects)
        {
            if (key.DataId == setupId)
            {
                return effect;
            }
        }

        return null;
    }
}

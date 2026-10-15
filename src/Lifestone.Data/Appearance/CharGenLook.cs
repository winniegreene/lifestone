namespace Lifestone.Data.Appearance;

/// <summary>One garment chosen in character creation.</summary>
/// <param name="Index">Index in the sex's list (<c>Headgears</c>, <c>Shirts</c>, <c>Pants</c> or <c>Footwear</c>), or -1
/// for none (CharGenState's "no style").</param>
/// <param name="PaletteTemplate">
/// The palette template (a key of the garment ClothingTable's <c>ClothingSubPalEffects</c>), 0 for none. Retail's colour
/// buttons index the templates that <c>CharGenState::StoreColorInformation</c> (11.4186 0x005C44D0; 11.6096 0x005C54B0)
/// collects; <c>CharGenState::Get*PaletteTemplateID</c> (11.4186 0x005C38F0-0x005C3980) turns a colour index into the
/// template given here. That mapping belongs to the CharGen screen state.
/// </param>
/// <param name="Shade">The garment shade in [0, 1].</param>
public readonly record struct GearChoice(int Index, uint PaletteTemplate, double Shade)
{
    /// <summary>No garment.</summary>
    public static GearChoice None { get; } = new(-1, 0, 0);
}

/// <summary>
/// Every appearance choice of character creation, as the CharGen state holds it (the fields <c>gmCG3DView::Update</c>
/// reads). An index of -1 means "none"; the shades are in [0, 1].
/// </summary>
/// <param name="Heritage">Heritage group (key of <c>CharGen.HeritageGroups</c>, 1-13).</param>
/// <param name="Sex">Sex (key of <c>HeritageGroupCG.Genders</c>: 1 male, 2 female).</param>
/// <param name="HairStyle">Index in <c>SexCG.HairStyles</c>, or -1.</param>
/// <param name="HairColor">Index in <c>SexCG.HairColors</c> (PaletteSets), or -1.</param>
/// <param name="HairShade">Shade picked in the hair PaletteSet.</param>
/// <param name="EyeStrip">Index in <c>SexCG.EyeStrips</c>, or -1.</param>
/// <param name="EyeColor">Index in <c>SexCG.EyeColors</c> (palettes), or -1.</param>
/// <param name="NoseStrip">Index in <c>SexCG.NoseStrips</c>, or -1.</param>
/// <param name="MouthStrip">Index in <c>SexCG.MouthStrips</c>, or -1.</param>
/// <param name="SkinShade">Shade picked in the skin PaletteSet.</param>
/// <param name="Headgear">The headgear.</param>
/// <param name="Shirt">The shirt.</param>
/// <param name="Pants">The trousers.</param>
/// <param name="Footwear">The footwear.</param>
public readonly record struct CharGenLook(
    int Heritage,
    int Sex,
    int HairStyle,
    int HairColor,
    double HairShade,
    int EyeStrip,
    int EyeColor,
    int NoseStrip,
    int MouthStrip,
    double SkinShade,
    GearChoice Headgear,
    GearChoice Shirt,
    GearChoice Pants,
    GearChoice Footwear);

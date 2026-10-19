using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Client.Avatars;
using Lifestone.Data.Appearance;
using Lifestone.Data.World;

namespace Lifestone.Client.Play;

/// <summary>
/// The looks of explore mode's player. There is no character offline, so the player is a stand-in: an Aluvian man in the first
/// garments character creation lists, with the first hair, eyes, nose and mouth it offers and middle shades. It is a placeholder
/// and the game says so on screen; nothing here is retail's choice (a character created online brings its own appearance,
/// and the offline stand-in is replaced by it).
/// </summary>
/// <remarks>
/// The look goes through the same <see cref="CharGenAppearance.Compose"/> that character creation's 3D view and a created
/// character's ObjDesc use, so the stand-in is dressed by exactly the code that dresses a real character. Deterministic: the
/// same dats always give the same look. The hair style is the first whose Setup is the sex's own (a style with an alternate Setup
/// would change the body the physics player is built on, which offline is the human Setup 0x02000001).
/// </remarks>
public static class OfflinePlayerLook
{
    /// <summary>The Aluvian heritage group.</summary>
    public const int Heritage = 1;

    /// <summary>The male sex.</summary>
    public const int Sex = 1;

    /// <summary>The shade chosen in every palette set (the middle).</summary>
    public const double Shade = 0.5;

    /// <summary>The stand-in's guid (the first of ACE's player range; offline nothing else has one).</summary>
    public const uint PlayerGuid = 0x50000001;

    /// <summary>What the on-screen label calls the character.</summary>
    public const string Label = "OFFLINE STAND-IN CHARACTER: placeholder look, placeholder Run and Jump skills, runs by default (placeholder)";

    /// <summary>The stand-in's choices.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The dats have no Aluvian man.</exception>
    public static CharGenLook Create(CharGen charGen, IAppearanceDats dats) => Create(charGen, dats, 0, Shade);

    /// <summary>
    /// The look with the <paramref name="garment"/>th shirt, trousers and footwear CharGen lists (clamped to each list's length) in <paramref name="garmentShade"/>: the stand-in server's placeholder humans
    /// (<c>TODO(m3-standin-human-look)</c>) wear other garments than the player.
    /// </summary>
    public static CharGenLook Create(CharGen charGen, IAppearanceDats dats, int garment, double garmentShade)
    {
        ArgumentNullException.ThrowIfNull(charGen);
        ArgumentNullException.ThrowIfNull(dats);
        var sex = CharGenAppearance.GetSex(charGen, Heritage, Sex);
        var hair = -1;
        for (var i = 0; i < sex.HairStyles.Count && hair < 0; i++)
        {
            if (CharGenAppearance.ResolveSetupId(sex, i) == sex.SetupId.DataId)
            {
                hair = i;
            }
        }

        if (hair < 0)
        {
            hair = Pick(sex.HairStyles.Count);
        }

        return new CharGenLook(
            Heritage,
            Sex,
            hair,
            Pick(sex.HairColors.Count),
            Shade,
            Pick(sex.EyeStrips.Count),
            Pick(sex.EyeColors.Count),
            Pick(sex.NoseStrips.Count),
            Pick(sex.MouthStrips.Count),
            Shade,
            GearChoice.None,
            Garment(sex, sex.Shirts, dats, garment, garmentShade),
            Garment(sex, sex.Pants, dats, garment, garmentShade),
            Garment(sex, sex.Footwear, dats, garment, garmentShade));
    }

    /// <summary>
    /// The player's own object as a server would create it: a creature with the composition's Setup, motion table, script table and
    /// ObjDesc at <paramref name="position"/>, with gravity (the state a created player carries) and its own guid.
    /// </summary>
    public static AvatarCreate Describe(CharGenComposition composition, in CellFrame position) => new()
    {
        Guid = PlayerGuid,
        IsSelf = true,
        IsCreature = true,
        SetupId = composition.SetupId,
        MotionTableId = composition.MotionTableId,
        PhysicsScriptTableId = composition.PhysicsScriptTableId,
        ObjDesc = composition.ObjDesc,
        PhysicsState = AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions,
        Position = position,
    };

    private static int Pick(int count) => count > 0 ? 0 : -1;

    /// <summary>The first garment of a list, in the first colour its table offers (<c>ClothingColors</c>).</summary>
    private static GearChoice Garment(SexCG sex, List<GearCG> list, IAppearanceDats dats, int index, double shade)
    {
        if (list.Count == 0)
        {
            return GearChoice.None;
        }

        index = Math.Clamp(index, 0, list.Count - 1);
        var table = dats.GetClothingTable(list[index].ClothingTable.DataId);
        var template = 0u;
        if (table is not null)
        {
            foreach (var colour in sex.ClothingColors)
            {
                if (table.ClothingSubPalEffects.TryGetValue(colour, out var effect) && effect.CloSubPalettes.Count > 0)
                {
                    template = colour;
                    break;
                }
            }
        }

        return new GearChoice(index, template, shade);
    }
}

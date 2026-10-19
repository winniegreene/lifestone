using System.Collections.Immutable;
using Lifestone.Data.Appearance;
using Lifestone.Data.Textures;
using Lifestone.Net.Types;
using NetAnimPartChange = Lifestone.Net.Types.AnimPartChange;

namespace Lifestone.Client.Session.StandIn;

/// <summary>A worn piece as the stand-in dresses a wearer with it: the piece's ClothingTable, its own Setup (a held or placed piece is drawn on it) and the palette template and shade it was created with.</summary>
/// <param name="ClothingBase">The ClothingTable id (0x10xxxxxx).</param>
/// <param name="ItemSetup">The piece's own Setup (0x02xxxxxx).</param>
/// <param name="Palette">The palette template, 0 for none.</param>
/// <param name="Shade">The shade, 0 to 1 (ACE: 0 when the weenie has none).</param>
public readonly record struct StandInWorn(uint ClothingBase, uint ItemSetup, int Palette, double Shade);

/// <summary>
/// What the host (which has the dats) gives the stand-in so that its humans wear what they wear: ACE's <c>Creature.CalculateObjDesc</c> over the worn pieces (<c>Creature_Networking.cs</c> 35-240) and
/// <c>WorldObject.CalculateObjDesc</c> for a piece's own ObjDesc (<c>WorldObject_Networking.cs</c> 906-975).
/// </summary>
public interface IStandInDresser
{
    /// <summary>The player's ObjDesc wearing <paramref name="worn"/> over its base look.</summary>
    ObjDesc Player(IReadOnlyList<StandInWorn> worn);

    /// <summary>The piece's own ObjDesc (its ClothingBase applied to its own Setup).</summary>
    ObjDesc Item(StandInWorn piece);
}

/// <summary>
/// The look the stand-in server gives its humans (M3 4.2 p6): the ModelData a real server sends in a human's CreateObject, so the player, the placeholder NPCs and the corpse are drawn dressed and not as the base
/// body (the bare figures of the Holtburg frames). The look is composed by the host from the dats (<c>CharGenAppearance.Compose</c>, as the offline player is) and converted here; the stand-in has no
/// weenie database, so every choice of garment is a placeholder (<c>TODO(m3-standin-human-look)</c>, logged once per role by <see cref="StandInVisit.LookFor"/>'s caller).
/// </summary>
public static class StandInHumanLook
{
    /// <summary>The head's part index in the human Setup: the face, hair and eye strips retexture it, which a body with no clothing has too.</summary>
    public const int HeadPart = 0x10;

    /// <summary>
    /// True when <paramref name="model"/> carries clothing: a texture change on a body part other than the head. The rule: clothing is the only thing that retextures the torso, arms, legs, hands or feet (ClothingTable
    /// texture effects, <c>ClothingTable::BuildObjDesc</c> 11.6096 0x005A89B0), whereas the face's texture changes sit on the head and part changes alone say nothing (ACE adds the base GfxObj of every uncovered
    /// part to a naked body, <c>Creature_Networking.cs</c> 226-234, and a piece of armour with no texture effect is a rarity this rule misses).
    /// </summary>
    public static bool IsDressed(ModelData model)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach (var change in model.TextureChanges)
        {
            if (change.PartIndex != HeadPart)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The wire ModelData of <paramref name="desc"/>: the inverse of <c>AvatarWire.ToObjDesc</c>. The base palette goes only with at least one sub-palette (ACE <c>SerializeModelData</c>; retail
    /// <c>ObjDesc::UnPack</c> 11.6096 0x005ACDA0 reads it only then); offsets and lengths go in groups of 8 colours, a whole 2,048-colour length as the byte 0.
    /// </summary>
    public static ModelData FromObjDesc(ObjDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (desc.IsEmpty)
        {
            return ModelData.Empty;
        }

        var subs = ImmutableArray.CreateBuilder<SubPaletteSwap>(desc.SubPalettes.Count);
        foreach (var s in desc.SubPalettes)
        {
            if (s.Offset % SubPalette.WireUnit != 0 || s.Length % SubPalette.WireUnit != 0 || s.Offset / SubPalette.WireUnit > 255)
            {
                throw new ArgumentException($"sub-palette 0x{s.PaletteId:X8} [{s.Offset}, +{s.Length}) is not in whole groups of {SubPalette.WireUnit} colours", nameof(desc));
            }

            subs.Add(new SubPaletteSwap(s.PaletteId, (byte)(s.Offset / SubPalette.WireUnit), (byte)(s.Length / SubPalette.WireUnit)));
        }

        var textures = ImmutableArray.CreateBuilder<TextureChange>(desc.TextureChanges.Count);
        foreach (var t in desc.TextureChanges)
        {
            textures.Add(new TextureChange(t.PartIndex, t.OldSurfaceTextureId, t.NewSurfaceTextureId));
        }

        var parts = ImmutableArray.CreateBuilder<NetAnimPartChange>(desc.PartChanges.Count);
        foreach (var p in desc.PartChanges)
        {
            parts.Add(new NetAnimPartChange(p.PartIndex, p.GfxObjId));
        }

        return new ModelData(subs.Count > 0 ? desc.PaletteId : null, subs.MoveToImmutable(), textures.MoveToImmutable(), parts.MoveToImmutable());
    }
}

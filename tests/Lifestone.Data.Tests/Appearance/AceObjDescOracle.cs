// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Creature_Networking.cs (CalculateObjDesc),
// WorldObject_Networking.cs (SerializeModelData, AddBaseModelData), ACE.Server/Factories/PlayerFactory.cs (the
// appearance part of Create), ACE.Server/Network/Extensions.cs (WritePackedDword, WritePackedDwordOfKnownType, Align),
// ACE.Entity/ObjDesc.cs, ACE.Entity/Enum/SetupConst.cs, ACE.DatLoader/Entity/SexCG.cs (the Get* helpers) and
// ACE.DatLoader/FileTypes/PaletteSet.cs (GetPaletteID) (AGPL-3.0) [with changes: reduced to what a newly created player
// sends; works on DatReaderWriter records; the worn clothing is given in ACE's final order (its sort needs weenie
// properties) as ClothingBase, PaletteTemplate and Shade; the helm option is on, as for a new character; no database,
// biota or icon handling].

using System.Text;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace Lifestone.Data.Tests.Appearance;

/// <summary>
/// An independent oracle for object descriptions: ACE's server-side composition of a player's ObjDesc and its ModelData
/// serialisation. Tests compare what a client builds from these bytes with the retail CharGen preview.
/// </summary>
internal static class AceObjDescOracle
{
    /// <summary>ACE <c>HeritageGroup.Gearknight</c>.</summary>
    private const int Gearknight = 6;

    /// <summary>ACE.Entity <c>ObjDesc</c>, with ACE's list types reduced to tuples.</summary>
    internal sealed class AceObjDesc
    {
        public uint PaletteID { get; set; }

        public List<(uint SubPaletteId, ushort Offset, ushort Length)> SubPalettes { get; } = [];

        public List<(byte PartIndex, uint OldTexture, uint NewTexture)> TextureChanges { get; } = [];

        public List<(byte Index, uint AnimationId)> AnimPartChanges { get; } = [];

        /// <summary>Helper function to ensure we don't add redundant parts to the list.</summary>
        public void AddTextureChange((byte PartIndex, uint OldTexture, uint NewTexture) tm)
        {
            if (!TextureChanges.Any(c => c.PartIndex == tm.PartIndex && c.OldTexture == tm.OldTexture && c.NewTexture == tm.NewTexture))
            {
                TextureChanges.Add(tm);
            }
        }

        /// <summary>Helper function to ensure we only have one AnimationPartChange.PartId in the list.</summary>
        public void AddAnimPartChange((byte Index, uint AnimationId) ap)
        {
            var i = AnimPartChanges.FindIndex(c => c.Index == ap.Index && c.AnimationId == ap.AnimationId);
            if (i >= 0)
            {
                AnimPartChanges.RemoveAt(i);
            }

            AnimPartChanges.Add(ap);
        }
    }

    /// <summary>The appearance properties <c>PlayerFactory.Create</c> sets on a new player.</summary>
    internal sealed record AcePlayer(
        int Heritage,
        int Gender,
        uint SetupTableId,
        uint PaletteBaseDID,
        int? HairStyle,
        uint? HeadObjectDID,
        uint EyesTextureDID,
        uint DefaultEyesTextureDID,
        uint NoseTextureDID,
        uint DefaultNoseTextureDID,
        uint MouthTextureDID,
        uint DefaultMouthTextureDID,
        uint HairTexture,
        uint DefaultHairTexture,
        uint SkinPaletteDID,
        uint HairPaletteDID,
        uint EyesPaletteDID);

    /// <summary>A worn item: its ClothingBase, PaletteTemplate and Shade properties.</summary>
    internal readonly record struct AceClothing(uint? ClothingBase, uint? PaletteTemplate, double? Shade);

    /// <summary>The character-creation choices ACE receives (<c>CharacterCreateInfo.Appearance</c>, the fields used here).</summary>
    internal readonly record struct AceAppearance(
        uint Eyes, uint Nose, uint Mouth, uint HairColor, uint EyeColor, uint HairStyle, double SkinHue, double HairHue);

    /// <summary>The appearance part of <c>PlayerFactory.Create</c>.</summary>
    public static AcePlayer CreatePlayer(CharGen cg, int heritage, int gender, AceAppearance appearance, Func<uint, PalSet?> palSets)
    {
        var heritageGroup = cg.HeritageGroups[(uint)heritage];

        // pull character data from the dat file
        var sex = heritageGroup.Genders[gender];
        uint setup = sex.SetupId.DataId;

        // Get the hair first, because we need to know if you're bald, and that's the name of that tune!
        var hairstyle = sex.HairStyles[(int)appearance.HairStyle];

        // Olthoi and Gear Knights have a "Body Style" instead of a hair style. These styles have multiple model/texture
        // changes, instead of a single head/hairstyle.
        int? hairStyleProperty = hairstyle.ObjDesc.AnimPartChanges.Count > 1 ? (int)appearance.HairStyle : null;

        // Certain races (Undead, Tumeroks, Others?) have multiple body styles available. This is controlled via the "hair style".
        if (hairstyle.AlternateSetup > 0)
        {
            setup = hairstyle.AlternateSetup;
        }

        // Skin is stored as PaletteSet (list of Palettes), so we need to read in the set to get the specific palette
        var skinPalSet = palSets(sex.SkinPalSet.DataId)!;

        // Hair is stored as PaletteSet (list of Palettes), so we need to read in the set to get the specific palette
        var hairPalSet = palSets(sex.HairColors[(int)appearance.HairColor])!;

        return new AcePlayer(
            heritage,
            gender,
            setup,
            sex.BasePalette.DataId,
            hairStyleProperty,
            GetHeadObject(sex, appearance.HairStyle),
            GetEyeTexture(sex, appearance.Eyes, hairstyle.Bald),
            GetDefaultEyeTexture(sex, appearance.Eyes, hairstyle.Bald),
            sex.NoseStrips[(int)appearance.Nose].ObjDesc.TextureChanges[0].NewTexture.DataId,
            sex.NoseStrips[(int)appearance.Nose].ObjDesc.TextureChanges[0].OldTexture.DataId,
            sex.MouthStrips[(int)appearance.Mouth].ObjDesc.TextureChanges[0].NewTexture.DataId,
            sex.MouthStrips[(int)appearance.Mouth].ObjDesc.TextureChanges[0].OldTexture.DataId,
            GetHairTexture(sex, appearance.HairStyle) ?? 0,
            GetDefaultHairTexture(sex, appearance.HairStyle) ?? 0,
            GetPaletteID(skinPalSet, appearance.SkinHue),
            GetPaletteID(hairPalSet, appearance.HairHue),
            sex.EyeColors[(int)appearance.EyeColor]);
    }

    /// <summary>
    /// <c>Creature.CalculateObjDesc</c> for a player wearing <paramref name="worn"/> (in ACE's final order).
    /// </summary>
    public static AceObjDesc CalculateObjDesc(
        AcePlayer player,
        IReadOnlyList<AceClothing> worn,
        CharGen cg,
        Func<uint, ClothingTable?> clothingTables,
        Func<uint, PalSet?> palSets,
        Func<uint, IReadOnlyList<uint>> setupParts)
    {
        var objDesc = new AceObjDesc();

        AddBaseModelData(player, cg, objDesc);

        var coverage = new List<uint>();

        uint thisSetupId = player.SetupTableId;

        // Some player races use an AlternateSetupDid, either at creation or via Barber options.
        // BUT -- those values do not correspond with entries in the Clothing Table.
        // So, we need to make some adjustments to look up something that DOES exist and is appropriate for the AlternateSetup model.
        switch (thisSetupId)
        {
            case SetupConst.UmbraenMaleCrownGen:
            case SetupConst.UmbraenMaleNoCrown:
            case SetupConst.UmbraenMaleVoid:
                thisSetupId = SetupConst.UmbraenMaleCrown;
                break;

            case SetupConst.UmbraenFemaleNoCrown:
            case SetupConst.UmbraenFemaleVoid:
                thisSetupId = SetupConst.UmbraenFemaleCrown;
                break;

            case SetupConst.PenumbraenMaleCrownGen:
            case SetupConst.PenumbraenMaleNoCrown:
            case SetupConst.PenumbraenMaleVoid:
                thisSetupId = SetupConst.PenumbraenMaleCrown;
                break;

            case SetupConst.PenumbraenFemaleNoCrown:
            case SetupConst.PenumbraenFemaleVoid:
                thisSetupId = SetupConst.PenumbraenFemaleCrown;
                break;

            case SetupConst.UndeadMaleUndeadGen:
            case SetupConst.UndeadMaleSkeleton:
            case SetupConst.UndeadMaleSkeletonNoFlame:
            case SetupConst.UndeadMaleZombie:
            case SetupConst.UndeadMaleZombieNoFlame:
                thisSetupId = SetupConst.UndeadMaleUndead;
                break;

            case SetupConst.UndeadFemaleUndeadGen:
            case SetupConst.UndeadFemaleSkeleton:
            case SetupConst.UndeadFemaleSkeletonNoFlame:
            case SetupConst.UndeadFemaleZombie:
            case SetupConst.UndeadFemaleZombieNoFlame:
                thisSetupId = SetupConst.UndeadFemaleUndead;
                break;

            case SetupConst.AnakshayMale:
                thisSetupId = SetupConst.HumanMale;
                break;

            case SetupConst.AnakshayFemale:
                thisSetupId = SetupConst.HumanFemale;
                break;
        }

        foreach (var w in worn)
        {
            if (w.ClothingBase is not { } clothingBase)
            {
                continue; // ACE uses the item's Setup as a pseudo ClothingBase; a starter garment always has one.
            }

            var item = clothingTables(clothingBase)!;
            var effect = Lookup(item, player.SetupTableId) ?? Lookup(item, thisSetupId);
            if (effect is null)
            {
                continue;
            }

            // Add the model and texture(s). Check if the original model has AnimParts defined, otherwise use the fallback if different
            foreach (var t in effect.CloObjectEffects)
            {
                var partNum = (byte)t.Index;
                coverage.Add(partNum);

                objDesc.AddAnimPartChange(((byte)t.Index, t.ModelId.DataId));

                foreach (var t1 in t.CloTextureEffects)
                {
                    objDesc.AddTextureChange(((byte)t.Index, t1.OldTexture.DataId, t1.NewTexture.DataId));
                }
            }

            if (item.ClothingSubPalEffects.Count > 0)
            {
                CloSubPalEffect itemSubPal;
                var palOption = 0;
                if (w.PaletteTemplate.HasValue)
                {
                    palOption = (int)w.PaletteTemplate.Value;
                }

                if (item.ClothingSubPalEffects.TryGetValue((uint)palOption, out var found))
                {
                    itemSubPal = found;
                }
                else
                {
                    itemSubPal = item.ClothingSubPalEffects[item.ClothingSubPalEffects.Keys.ElementAt(0)];
                }

                float shade = 0;
                if (w.Shade.HasValue)
                {
                    shade = (float)w.Shade.Value;
                }

                for (var i = 0; i < itemSubPal.CloSubPalettes.Count; i++)
                {
                    var itemPalSet = palSets(itemSubPal.CloSubPalettes[i].PaletteSet.DataId)!;
                    var itemPal = (ushort)GetPaletteID(itemPalSet, shade);

                    for (var j = 0; j < itemSubPal.CloSubPalettes[i].Ranges.Count; j++)
                    {
                        var palOffset = (ushort)(itemSubPal.CloSubPalettes[i].Ranges[j].Offset / 8);
                        var numColors = (ushort)(itemSubPal.CloSubPalettes[i].Ranges[j].NumColors / 8);
                        objDesc.SubPalettes.Add((itemPal, palOffset, numColors));
                    }
                }
            }
        }

        // Add the "naked" body parts. These are the ones not already covered.
        // Note that this is the original SetupTableId, not thisSetupId.
        if (player.SetupTableId > 0)
        {
            var baseSetup = setupParts(player.SetupTableId);
            for (byte i = 0; i < baseSetup.Count; i++)
            {
                // Don't add body parts for those that are already covered. Also don't add the head, that was already
                // covered by AddCharacterBaseModelData()
                if (!coverage.Contains(i) && i != 0x10)
                {
                    objDesc.AnimPartChanges.Add((i, baseSetup[i]));
                }
            }
        }

        return objDesc;
    }

    /// <summary><c>WorldObject.SerializeModelData</c>: the ModelData bytes of an ObjDesc.</summary>
    public static byte[] SerializeModelData(AceObjDesc objDesc)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((byte)0x11);
        writer.Write((byte)objDesc.SubPalettes.Count);
        writer.Write((byte)objDesc.TextureChanges.Count);
        writer.Write((byte)objDesc.AnimPartChanges.Count);

        if (objDesc.SubPalettes.Count > 0)
        {
            WritePackedDwordOfKnownType(writer, objDesc.PaletteID, 0x4000000);
        }

        foreach (var palette in objDesc.SubPalettes)
        {
            WritePackedDwordOfKnownType(writer, palette.SubPaletteId, 0x4000000);
            writer.Write((byte)palette.Offset);
            writer.Write((byte)palette.Length);
        }

        foreach (var texture in objDesc.TextureChanges)
        {
            writer.Write(texture.PartIndex);
            WritePackedDwordOfKnownType(writer, texture.OldTexture, 0x5000000);
            WritePackedDwordOfKnownType(writer, texture.NewTexture, 0x5000000);
        }

        foreach (var model in objDesc.AnimPartChanges)
        {
            writer.Write(model.Index);
            WritePackedDwordOfKnownType(writer, model.AnimationId, 0x1000000);
        }

        // Align
        writer.Flush();
        while (stream.Length % 4 != 0)
        {
            writer.Write((byte)0);
            writer.Flush();
        }

        return stream.ToArray();
    }

    /// <summary><c>WorldObject.AddBaseModelData</c> for a player.</summary>
    private static void AddBaseModelData(AcePlayer player, CharGen cg, AceObjDesc objDesc)
    {
        // Hair/head

        // This Heritage check has been added for backwards compatibility. It works around the butthead Gear Knights appearance.
        if (player.HeadObjectDID.HasValue && !player.HairStyle.HasValue && player.Heritage != Gearknight)
        {
            objDesc.AnimPartChanges.Add((0x10, player.HeadObjectDID.Value));
        }
        else if (player.HairStyle.HasValue)
        {
            // This indicates we have a Gear Knight or Olthoi(that is, player types treat "hairstyle" as a "Body Style")

            // Load the CharGen data. It has all the anim & texture changes for the Body Style defined within it
            var sex = cg.HeritageGroups[(uint)player.Heritage].Genders[player.Gender];
            if (sex.HairStyles.Count > player.HairStyle.Value) // just check for a valid entry...
            {
                var hairstyle = sex.HairStyles[player.HairStyle.Value];

                // Add all the texture changes
                foreach (var tm in hairstyle.ObjDesc.TextureChanges)
                {
                    objDesc.TextureChanges.Add((tm.PartIndex, tm.OldTexture.DataId, tm.NewTexture.DataId));
                }

                // Add all the animation part changes
                foreach (var part in hairstyle.ObjDesc.AnimPartChanges)
                {
                    objDesc.AnimPartChanges.Add((part.PartIndex, part.PartId.DataId));
                }
            }
        }

        objDesc.TextureChanges.Add((0x10, player.DefaultHairTexture, player.HairTexture));
        objDesc.SubPalettes.Add((player.HairPaletteDID, 0x18, 0x8));

        // Skin
        objDesc.PaletteID = player.PaletteBaseDID;
        objDesc.SubPalettes.Add((player.SkinPaletteDID, 0x0, 0x18));

        // Eyes
        objDesc.TextureChanges.Add((0x10, player.DefaultEyesTextureDID, player.EyesTextureDID));
        objDesc.SubPalettes.Add((player.EyesPaletteDID, 0x20, 0x8));

        // Nose & Mouth
        objDesc.TextureChanges.Add((0x10, player.DefaultNoseTextureDID, player.NoseTextureDID));
        objDesc.TextureChanges.Add((0x10, player.DefaultMouthTextureDID, player.MouthTextureDID));
    }

    private static ClothingBaseEffect? Lookup(ClothingTable table, uint setupId)
    {
        foreach (var (key, value) in table.ClothingBaseEffects)
        {
            if (key.DataId == setupId)
            {
                return value;
            }
        }

        return null;
    }

    private static void WritePackedDword(BinaryWriter writer, uint value)
    {
        if (value <= 32767)
        {
            var networkValue = Convert.ToUInt16(value);
            writer.Write(BitConverter.GetBytes(networkValue));
        }
        else
        {
            var packedValue = (value << 16) | ((value >> 16) | 0x8000);
            writer.Write(BitConverter.GetBytes(packedValue));
        }
    }

    private static void WritePackedDwordOfKnownType(BinaryWriter writer, uint value, uint type)
    {
        if ((value & type) > 0)
        {
            value -= type;
        }

        WritePackedDword(writer, value);
    }

    // SexCG helpers (ACE.DatLoader/Entity/SexCG.cs).
    private static uint GetEyeTexture(SexCG sex, uint eyesStrip, bool isBald) =>
        (isBald ? sex.EyeStrips[(int)eyesStrip].BaldObjDesc : sex.EyeStrips[(int)eyesStrip].ObjDesc).TextureChanges[0].NewTexture.DataId;

    private static uint GetDefaultEyeTexture(SexCG sex, uint eyesStrip, bool isBald) =>
        (isBald ? sex.EyeStrips[(int)eyesStrip].BaldObjDesc : sex.EyeStrips[(int)eyesStrip].ObjDesc).TextureChanges[0].OldTexture.DataId;

    private static uint? GetHeadObject(SexCG sex, uint hairStyle)
    {
        var hairstyle = sex.HairStyles[(int)hairStyle];

        // Gear Knights, both Olthoi types have multiple anim part changes.
        return hairstyle.ObjDesc.AnimPartChanges.Count == 1 ? hairstyle.ObjDesc.AnimPartChanges[0].PartId.DataId : null;
    }

    private static uint? GetHairTexture(SexCG sex, uint hairStyle)
    {
        var hairstyle = sex.HairStyles[(int)hairStyle];

        // OlthoiAcid has no TextureChanges
        return hairstyle.ObjDesc.TextureChanges.Count > 0 ? hairstyle.ObjDesc.TextureChanges[0].NewTexture.DataId : null;
    }

    private static uint? GetDefaultHairTexture(SexCG sex, uint hairStyle)
    {
        var hairstyle = sex.HairStyles[(int)hairStyle];

        // OlthoiAcid has no TextureChanges
        return hairstyle.ObjDesc.TextureChanges.Count > 0 ? hairstyle.ObjDesc.TextureChanges[0].OldTexture.DataId : null;
    }

    /// <summary>ACE.DatLoader <c>PaletteSet.GetPaletteID</c> (which clamps the index, unlike retail).</summary>
    private static uint GetPaletteID(PalSet set, double hue)
    {
        // Make sure the PaletteList has valid data and the hue is within valid ranges
        if (set.Palettes.Count == 0 || hue < 0 || hue > 1)
        {
            return 0;
        }

        // Hue is stored in DB as a percent of the total, so do some math to figure out the int position
        var palIndex = (int)((set.Palettes.Count - 0.000001) * hue);

        // Since the hue numbers are a little odd, make sure we're in the bounds.
        if (palIndex < 0)
        {
            palIndex = 0;
        }

        if (palIndex > set.Palettes.Count - 1)
        {
            palIndex = set.Palettes.Count - 1;
        }

        return set.Palettes[palIndex].DataId;
    }

    /// <summary>ACE.Entity <c>SetupConst</c>.</summary>
    private static class SetupConst
    {
        public const uint HumanMale = 0x02000001;
        public const uint HumanFemale = 0x0200004E;
        public const uint UndeadMaleUndead = 0x02001A0E;
        public const uint UndeadMaleUndeadGen = 0x02001A0F;
        public const uint UndeadMaleSkeleton = 0x02001A9C;
        public const uint UndeadMaleSkeletonNoFlame = 0x02001A9E;
        public const uint UndeadMaleZombie = 0x02001A9D;
        public const uint UndeadMaleZombieNoFlame = 0x02001A96;
        public const uint UndeadFemaleUndead = 0x02001A0C;
        public const uint UndeadFemaleUndeadGen = 0x02001A0D;
        public const uint UndeadFemaleSkeleton = 0x02001AA0;
        public const uint UndeadFemaleSkeletonNoFlame = 0x02001A9F;
        public const uint UndeadFemaleZombie = 0x02001AA1;
        public const uint UndeadFemaleZombieNoFlame = 0x02001AA2;
        public const uint UmbraenMaleCrown = 0x0200196F;
        public const uint UmbraenMaleCrownGen = 0x02001972;
        public const uint UmbraenMaleNoCrown = 0x02001A5F;
        public const uint UmbraenMaleVoid = 0x02001A6F;
        public const uint UmbraenFemaleCrown = 0x02001970;
        public const uint UmbraenFemaleNoCrown = 0x02001A5E;
        public const uint UmbraenFemaleVoid = 0x2001A6EE;
        public const uint PenumbraenMaleCrown = 0x0200196E;
        public const uint PenumbraenMaleCrownGen = 0x02001971;
        public const uint PenumbraenMaleNoCrown = 0x02001A5D;
        public const uint PenumbraenMaleVoid = 0x02001A70;
        public const uint PenumbraenFemaleCrown = 0x0200196D;
        public const uint PenumbraenFemaleNoCrown = 0x02001A5C;
        public const uint PenumbraenFemaleVoid = 0x02001A71;
        public const uint AnakshayMale = 0x02001AA3;
        public const uint AnakshayFemale = 0x02001AA4;
    }
}

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.World;

namespace Lifestone.Data.CharacterCreation;

/// <summary>A starting town and the places a new character of it is put (retail's <c>StartArea_CG</c>).</summary>
/// <param name="Index">The start area's index, which a character create sends.</param>
/// <param name="Name">The start area's name (Holtburg, Shoushi, Yaraq, Sanamar, OlthoiLair).</param>
/// <param name="Locations">The candidate positions, in dat order.</param>
public sealed record CharGenStartArea(int Index, string Name, IReadOnlyList<CellFrame> Locations);

/// <summary>A sub-palette swap of an appearance (retail's <c>Subpalette</c>), as the dat stores it.</summary>
/// <param name="PaletteId">The palette the colours come from.</param>
/// <param name="Offset">The first colour replaced, in groups of 8 colours (the dat's byte).</param>
/// <param name="NumColors">How many colours are replaced, in groups of 8 colours (the dat's byte).</param>
public readonly record struct CharGenSubPalette(uint PaletteId, byte Offset, byte NumColors);

/// <summary>A texture swap on one part (retail's <c>TextureMapChange</c>).</summary>
/// <param name="PartIndex">The part.</param>
/// <param name="OldTexture">The SurfaceTexture replaced.</param>
/// <param name="NewTexture">The SurfaceTexture that replaces it.</param>
public readonly record struct CharGenTextureChange(byte PartIndex, uint OldTexture, uint NewTexture);

/// <summary>A model swap on one part (retail's <c>AnimPartChange</c>).</summary>
/// <param name="PartIndex">The part.</param>
/// <param name="PartId">The GfxObj that replaces it.</param>
public readonly record struct CharGenAnimPartChange(byte PartIndex, uint PartId);

/// <summary>
/// An appearance change of CharGen data (retail's <c>ObjDesc</c>) as the dat stores it. The avatars track applies it
/// (<c>ObjDesc.FromDat</c> and the CharGen composition); this is the immutable projection of the record.
/// </summary>
/// <param name="PaletteId">The base palette (0 when the desc swaps no sub-palette).</param>
/// <param name="SubPalettes">The sub-palette swaps.</param>
/// <param name="TextureChanges">The texture swaps.</param>
/// <param name="AnimPartChanges">The model swaps.</param>
public sealed record CharGenObjDesc(
    uint PaletteId,
    IReadOnlyList<CharGenSubPalette> SubPalettes,
    IReadOnlyList<CharGenTextureChange> TextureChanges,
    IReadOnlyList<CharGenAnimPartChange> AnimPartChanges)
{
    /// <summary>A desc that changes nothing.</summary>
    public static CharGenObjDesc Empty { get; } = new(0, [], [], []);

    internal static CharGenObjDesc From(ObjDesc? desc) => desc is null
        ? Empty
        : new CharGenObjDesc(
            desc.PaletteId.DataId,
            Array.AsReadOnly(desc.SubPalettes.Select(p => new CharGenSubPalette(p.SubId.DataId, p.Offset, p.NumColors)).ToArray()),
            Array.AsReadOnly(desc.TextureChanges.Select(t => new CharGenTextureChange(t.PartIndex, t.OldTexture.DataId, t.NewTexture.DataId)).ToArray()),
            Array.AsReadOnly(desc.AnimPartChanges.Select(a => new CharGenAnimPartChange(a.PartIndex, a.PartId.DataId)).ToArray()));
}

/// <summary>A hair style choice (retail's <c>HairStyle_CG</c>).</summary>
/// <param name="IconId">Its icon.</param>
/// <param name="Bald">Whether the style is bald (the eye strip then uses its bald variant).</param>
/// <param name="AlternateSetup">A Setup that replaces the gender's (<c>CharGenState::GetSetupID</c>), or 0.</param>
/// <param name="ObjDesc">The appearance change.</param>
public sealed record CharGenHairStyle(uint IconId, bool Bald, uint AlternateSetup, CharGenObjDesc ObjDesc);

/// <summary>An eye strip choice (retail's <c>EyeStrip_CG</c>): a normal and a bald variant.</summary>
/// <param name="IconId">Its icon.</param>
/// <param name="BaldIconId">Its icon with a bald hair style.</param>
/// <param name="ObjDesc">The appearance change.</param>
/// <param name="BaldObjDesc">The appearance change with a bald hair style.</param>
public sealed record CharGenEyeStrip(uint IconId, uint BaldIconId, CharGenObjDesc ObjDesc, CharGenObjDesc BaldObjDesc);

/// <summary>A nose or mouth strip choice (retail's <c>FaceStrip_CG</c>).</summary>
/// <param name="IconId">Its icon.</param>
/// <param name="ObjDesc">The appearance change.</param>
public sealed record CharGenFaceStrip(uint IconId, CharGenObjDesc ObjDesc);

/// <summary>A clothing choice (retail's <c>Style_CG</c> / <c>GearCG</c>): headgear, shirt, trousers or footwear.</summary>
/// <param name="Name">Its name.</param>
/// <param name="ClothingTableId">The ClothingTable (0x10xxxxxx) that dresses it and holds its colours.</param>
/// <param name="WeenieDefault">The weenie class the server gives the item.</param>
public sealed record CharGenGear(string Name, uint ClothingTableId, uint WeenieDefault);

/// <summary>A clothing colour choice: a palette template of a gear's ClothingTable and the PalSet it shades.</summary>
/// <param name="PaletteTemplate">The palette template (a ClothingTable key), which a character create sends.</param>
/// <param name="PalSetId">The PalSet of its first sub-palette effect, which the shade picks a palette from.</param>
public readonly record struct CharGenClothingColor(uint PaletteTemplate, uint PalSetId);

/// <summary>One gender of a heritage (retail's <c>Sex_CG</c>): its model, tables and appearance choices.</summary>
/// <param name="Id">The gender id (1 male, 2 female).</param>
/// <param name="Name">Its name.</param>
/// <param name="Scale">Its scale in percent.</param>
/// <param name="SetupId">Its Setup.</param>
/// <param name="SoundTableId">Its SoundTable.</param>
/// <param name="IconId">Its icon.</param>
/// <param name="BasePaletteId">Its base palette.</param>
/// <param name="SkinPalSetId">The PalSet the skin shade picks from.</param>
/// <param name="PhysicsTableId">Its PhysicsScriptTable.</param>
/// <param name="MotionTableId">Its MotionTable.</param>
/// <param name="CombatTableId">Its CombatTable.</param>
/// <param name="BaseObjDesc">The appearance change every character of it starts from.</param>
/// <param name="HairColors">Hair colour choices: PalSets the hair shade picks from.</param>
/// <param name="HairStyles">Hair style choices.</param>
/// <param name="EyeColors">Eye colour choices: palettes.</param>
/// <param name="EyeStrips">Eye strip choices.</param>
/// <param name="NoseStrips">Nose strip choices.</param>
/// <param name="MouthStrips">Mouth strip choices.</param>
/// <param name="Headgears">Headgear choices (a character may also wear none).</param>
/// <param name="Shirts">Shirt choices.</param>
/// <param name="Pants">Trousers choices.</param>
/// <param name="Footwear">Footwear choices.</param>
/// <param name="ClothingColors">The palette templates a gear's colours are taken from, in dat order.</param>
public sealed record CharGenGender(
    uint Id,
    string Name,
    uint Scale,
    uint SetupId,
    uint SoundTableId,
    uint IconId,
    uint BasePaletteId,
    uint SkinPalSetId,
    uint PhysicsTableId,
    uint MotionTableId,
    uint CombatTableId,
    CharGenObjDesc BaseObjDesc,
    IReadOnlyList<uint> HairColors,
    IReadOnlyList<CharGenHairStyle> HairStyles,
    IReadOnlyList<uint> EyeColors,
    IReadOnlyList<CharGenEyeStrip> EyeStrips,
    IReadOnlyList<CharGenFaceStrip> NoseStrips,
    IReadOnlyList<CharGenFaceStrip> MouthStrips,
    IReadOnlyList<CharGenGear> Headgears,
    IReadOnlyList<CharGenGear> Shirts,
    IReadOnlyList<CharGenGear> Pants,
    IReadOnlyList<CharGenGear> Footwear,
    IReadOnlyList<uint> ClothingColors);

/// <summary>A heritage's cost for a skill, which replaces the SkillTable's (retail's <c>Skill_CG</c>).</summary>
/// <param name="SkillId">The skill.</param>
/// <param name="NormalCost">Credits to train it.</param>
/// <param name="PrimaryCost">Credits to specialise it, in total.</param>
public readonly record struct CharGenSkill(uint SkillId, int NormalCost, int PrimaryCost);

/// <summary>A profession template (retail's <c>Template_CG</c>): attributes and skills the profession page applies.</summary>
/// <param name="Name">Its name.</param>
/// <param name="IconId">Its icon.</param>
/// <param name="Title">The character title it grants.</param>
/// <param name="Strength">Strength.</param>
/// <param name="Endurance">Endurance.</param>
/// <param name="Coordination">Coordination.</param>
/// <param name="Quickness">Quickness.</param>
/// <param name="Focus">Focus.</param>
/// <param name="Self">Self.</param>
/// <param name="NormalSkills">The skills it trains.</param>
/// <param name="PrimarySkills">The skills it specialises.</param>
public sealed record CharGenTemplate(
    string Name,
    uint IconId,
    uint Title,
    int Strength,
    int Endurance,
    int Coordination,
    int Quickness,
    int Focus,
    int Self,
    IReadOnlyList<uint> NormalSkills,
    IReadOnlyList<uint> PrimarySkills);

/// <summary>A heritage group (retail's <c>HeritageGroup_CG</c>).</summary>
/// <param name="Id">The heritage id (1-13).</param>
/// <param name="Name">Its name.</param>
/// <param name="IconId">Its icon.</param>
/// <param name="SetupId">The Setup CharGen's view starts from.</param>
/// <param name="EnvironmentSetupId">The Setup of CharGen's 3D backdrop.</param>
/// <param name="AttributeCredits">Attribute points to spend (330; 60 for the Olthoi).</param>
/// <param name="SkillCredits">Skill credits to spend (52; 68 for the Olthoi).</param>
/// <param name="PrimaryStartAreas">Start areas a random character of it starts in.</param>
/// <param name="SecondaryStartAreas">Other start areas it may choose.</param>
/// <param name="Skills">Its own skill costs.</param>
/// <param name="Templates">Its profession templates (index 0 is the custom one).</param>
/// <param name="Genders">Its genders by id.</param>
public sealed record CharGenHeritage(
    uint Id,
    string Name,
    uint IconId,
    uint SetupId,
    uint EnvironmentSetupId,
    uint AttributeCredits,
    uint SkillCredits,
    IReadOnlyList<int> PrimaryStartAreas,
    IReadOnlyList<int> SecondaryStartAreas,
    IReadOnlyList<CharGenSkill> Skills,
    IReadOnlyList<CharGenTemplate> Templates,
    IReadOnlyDictionary<uint, CharGenGender> Genders)
{
    /// <summary>The gender <paramref name="genderId"/>; false when the heritage has none with that id.</summary>
    public bool TryGetGender(uint genderId, [NotNullWhen(true)] out CharGenGender? gender) => Genders.TryGetValue(genderId, out gender);
}

/// <summary>
/// CharGen data (0x0E000002 in <c>client_portal.dat</c>, UNIQUEDB enum 0xE <c>CharGen_CharacterData</c>): the start
/// areas, the heritages with their genders, templates and skill costs, and the clothing colours a gear offers.
/// Immutable and safe from any thread; the avatars track builds CharGen's 3D composition on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Skill costs.</b> <c>ACCharGenData::GetSkillTrainedCost</c> and <c>GetSkillSpecializedCost</c> (11.4186 0x005C26D0 and
/// 0x005C27D0, 11.6096 0x005C36B0 and 0x005C37B0) take a heritage's own cost when it lists the skill (every heritage
/// lists Arcane Lore at 0 to train and 2 to specialise), else the SkillTable's, else -1 when the SkillTable has no such
/// skill. The specialised cost is the total, not the step from trained.
/// </para>
/// <para>
/// <b>Clothing colours.</b> <c>CharGenState::StoreColorInformation</c> (11.4186 0x005C44D0, 11.6096 0x005C54B0) lists a
/// gear's colours by walking its ClothingTable's palette templates in the order of retail's
/// <c>PackableHashTable</c>: bucket by bucket (key modulo the table size the dat stores), each bucket from its newest
/// entry (<c>PackableHashTable::add</c>, 11.4186 0x005A7E30, 11.6096 0x005A8EE0, puts each entry at the head of its
/// bucket; <c>PackableHashTable::begin</c>, 11.4186 0x005A73F0, 11.6096 0x005A8530, walks the buckets in order). A template is a
/// colour when the gender's <see cref="CharGenGender.ClothingColors"/> holds it and it has at least one sub-palette
/// effect; its PalSet is the first effect's. A character create sends the template, and CharGen's colour index counts
/// in this order (<see cref="GetClothingColors"/>).
/// </para>
/// <para>
/// <b>Namespace.</b> The files live in <c>Data/CharGen/</c>, but the namespace is <c>Lifestone.Data.CharacterCreation</c>:
/// a namespace named <c>CharGen</c> under <c>Lifestone.Data</c> would hide DatReaderWriter's <c>CharGen</c> record from
/// every <c>Lifestone.Data.*</c> file that names it unqualified (CS0118), as existing tests and the avatars track do.
/// </para>
/// </remarks>
public sealed class CharGenData
{
    /// <summary>The CharGen record's DID.</summary>
    public const uint DataId = 0x0E000002u;

    /// <summary>The UNIQUEDB (group 2) enum retail finds it by (<c>DBCache::GetFromEnumStatic(14, 2, ...)</c>).</summary>
    public const uint UniqueDbEnum = 0xE;

    /// <summary>The UNIQUEDB enum group (map 0x25000002).</summary>
    public static readonly EnumGroup UniqueDb = new(2);

    private readonly FrozenDictionary<uint, CharGenHeritage> _heritages;
    private readonly Func<uint, ClothingTable?> _readClothing;
    private readonly ConcurrentDictionary<uint, Lazy<ClothingColors?>> _clothing = new();

    private CharGenData(IReadOnlyList<CharGenStartArea> startAreas, IEnumerable<CharGenHeritage> heritages, Func<uint, ClothingTable?> readClothing)
    {
        StartAreas = startAreas;
        Heritages = heritages.OrderBy(h => h.Id).ToArray();
        _heritages = Heritages.ToFrozenDictionary(h => h.Id);
        _readClothing = readClothing;
    }

    /// <summary>The start areas, by index (5).</summary>
    public IReadOnlyList<CharGenStartArea> StartAreas { get; }

    /// <summary>The heritages, by ascending id (13).</summary>
    public IReadOnlyList<CharGenHeritage> Heritages { get; }

    /// <summary>Reads the CharGen record UNIQUEDB enum 0xE names; ClothingTables are read on first use.</summary>
    /// <exception cref="InvalidDataException">The enum does not resolve, or the record is absent.</exception>
    public static CharGenData Load(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var did = dats.EnumMaps.Resolve(UniqueDb, UniqueDbEnum);
        return dats.TryGetPortal<DatReaderWriter.DBObjs.CharGen>(did, out var record)
            ? FromRecord(record, id => dats.TryGetPortal<ClothingTable>(id, out var table) ? table : null)
            : throw new InvalidDataException($"TODO(retailui-data): CharGen 0x{did:X8} is not in {DatSet.PortalFileName}.");
    }

    /// <summary>Converts a decoded CharGen record; <paramref name="readClothing"/> reads a ClothingTable or returns null.</summary>
    internal static CharGenData FromRecord(DatReaderWriter.DBObjs.CharGen record, Func<uint, ClothingTable?> readClothing)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(readClothing);
        var areas = record.StartingAreas
            .Select((a, i) => new CharGenStartArea(i, a.Name.Value, List(a.Locations.Select(l => new CellFrame(l.CellId, new AcFrame(l.Frame.Origin, l.Frame.Orientation))))))
            .ToArray();
        var heritages = record.HeritageGroups.Select(kv => Heritage(kv.Key, kv.Value));
        return new CharGenData(areas, heritages, readClothing);
    }

    /// <summary>The heritage <paramref name="heritageId"/>; false when there is none (retail's <c>ACCharGenData::GetHG</c> then gives an empty one).</summary>
    public bool TryGetHeritage(uint heritageId, [NotNullWhen(true)] out CharGenHeritage? heritage) => _heritages.TryGetValue(heritageId, out heritage);

    /// <summary>The heritage <paramref name="heritageId"/>, or a loud failure.</summary>
    /// <exception cref="InvalidDataException">There is no such heritage.</exception>
    public CharGenHeritage GetHeritage(uint heritageId) =>
        TryGetHeritage(heritageId, out var heritage)
            ? heritage
            : throw new InvalidDataException($"TODO(retailui-data): CharGen 0x{DataId:X8} has no heritage {heritageId}.");

    /// <summary><c>ACCharGenData::GetSkillTrainedCost</c>: credits to train <paramref name="skillId"/> as <paramref name="heritageId"/>, or -1.</summary>
    public int GetSkillTrainedCost(uint skillId, uint heritageId, SkillTableData skills) => SkillCost(skillId, heritageId, skills, trained: true);

    /// <summary><c>ACCharGenData::GetSkillSpecializedCost</c>: total credits to specialise <paramref name="skillId"/>, or -1.</summary>
    public int GetSkillSpecializedCost(uint skillId, uint heritageId, SkillTableData skills) => SkillCost(skillId, heritageId, skills, trained: false);

    /// <summary>
    /// <c>CharGenState::StoreColorInformation</c>: the colours <paramref name="gender"/> may choose for the gear dressed by
    /// ClothingTable <paramref name="clothingTableId"/>, in retail's order (the class remarks); empty when the table is
    /// absent (retail then offers none).
    /// </summary>
    public IReadOnlyList<CharGenClothingColor> GetClothingColors(uint clothingTableId, CharGenGender gender)
    {
        ArgumentNullException.ThrowIfNull(gender);
        if (Clothing(clothingTableId) is not { } colors)
        {
            return [];
        }

        return colors.Ordered.Where(c => gender.ClothingColors.Contains(c.PaletteTemplate)).ToArray();
    }

    /// <summary>
    /// <c>ClothingTable::GetCloPaletteTemplate</c> (11.4186 0x005A74B0, 11.6096 0x005A85F0) as the verifier uses it: whether
    /// ClothingTable <paramref name="clothingTableId"/> exists and holds palette template <paramref name="paletteTemplate"/>.
    /// </summary>
    public bool HasPaletteTemplate(uint clothingTableId, uint paletteTemplate) =>
        Clothing(clothingTableId) is { } colors && colors.Templates.Contains(paletteTemplate);

    /// <summary>Retail's iteration order of a <c>PackableHashTable</c> from its keys in dat order (the class remarks).</summary>
    internal static IEnumerable<T> PackableHashOrder<T>(IReadOnlyList<T> inDatOrder, Func<T, uint> key, uint tableSize) =>
        tableSize == 0
            ? []
            : inDatOrder.Select((item, i) => (item, i)).OrderBy(e => key(e.item) % tableSize).ThenByDescending(e => e.i).Select(e => e.item);

    private ClothingColors? Clothing(uint tableId) =>
        _clothing.GetOrAdd(
            tableId,
            static (id, read) => new Lazy<ClothingColors?>(() => read(id) is { } table ? ClothingColors.From(table) : null, LazyThreadSafetyMode.ExecutionAndPublication),
            _readClothing).Value;

    private int SkillCost(uint skillId, uint heritageId, SkillTableData skills, bool trained)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (_heritages.TryGetValue(heritageId, out var heritage))
        {
            foreach (var skill in heritage.Skills)
            {
                if (skill.SkillId == skillId)
                {
                    return trained ? skill.NormalCost : skill.PrimaryCost;
                }
            }
        }

        return skills.TryGetSkill(skillId, out var entry) ? (trained ? entry.TrainedCost : entry.SpecializedCost) : -1;
    }

    private static CharGenHeritage Heritage(uint id, HeritageGroupCG h) => new(
        id,
        h.Name.Value,
        h.IconId.DataId,
        h.SetupId.DataId,
        h.EnvironmentSetupId.DataId,
        h.AttributeCredits,
        h.SkillCredits,
        List(h.PrimaryStartAreas),
        List(h.SecondaryStartAreas),
        List(h.Skills.Select(s => new CharGenSkill((uint)s.Id, s.NormalCost, s.PrimaryCost))),
        List(h.Templates.Select(t => new CharGenTemplate(
            t.Name.Value, t.IconId.DataId, t.Title, t.Strength, t.Endurance, t.Coordination, t.Quickness, t.Focus, t.Self,
            List(t.NormalSkills.Select(s => (uint)s)), List(t.PrimarySkills.Select(s => (uint)s))))),
        h.Genders.ToFrozenDictionary(g => (uint)g.Key, g => Gender((uint)g.Key, g.Value)));

    private static CharGenGender Gender(uint id, SexCG g) => new(
        id,
        g.Name.Value,
        g.Scale,
        g.SetupId.DataId,
        g.SoundTable.DataId,
        g.IconId.DataId,
        g.BasePalette.DataId,
        g.SkinPalSet.DataId,
        g.PhysicsTable.DataId,
        g.MotionTable.DataId,
        g.CombatTable.DataId,
        CharGenObjDesc.From(g.BaseObjDesc),
        List(g.HairColors),
        List(g.HairStyles.Select(s => new CharGenHairStyle(s.IconId.DataId, s.Bald, s.AlternateSetup, CharGenObjDesc.From(s.ObjDesc)))),
        List(g.EyeColors),
        List(g.EyeStrips.Select(s => new CharGenEyeStrip(s.IconId.DataId, s.BaldIconId, CharGenObjDesc.From(s.ObjDesc), CharGenObjDesc.From(s.BaldObjDesc)))),
        List(g.NoseStrips.Select(s => new CharGenFaceStrip(s.IconId.DataId, CharGenObjDesc.From(s.ObjDesc)))),
        List(g.MouthStrips.Select(s => new CharGenFaceStrip(s.IconId.DataId, CharGenObjDesc.From(s.ObjDesc)))),
        Gear(g.Headgears),
        Gear(g.Shirts),
        Gear(g.Pants),
        Gear(g.Footwear),
        List(g.ClothingColors));

    private static IReadOnlyList<CharGenGear> Gear(IEnumerable<GearCG> gear) =>
        List(gear.Select(x => new CharGenGear(x.Name.Value, x.ClothingTable.DataId, x.WeenieDefault)));

    private static IReadOnlyList<T> List<T>(IEnumerable<T> items)
    {
        var array = items.ToArray();
        return array.Length == 0 ? [] : Array.AsReadOnly(array);
    }

    /// <summary>A ClothingTable's palette templates, in retail's iteration order.</summary>
    private sealed class ClothingColors
    {
        private ClothingColors(IReadOnlyList<CharGenClothingColor> ordered, FrozenSet<uint> templates)
        {
            Ordered = ordered;
            Templates = templates;
        }

        /// <summary>The templates that have a sub-palette effect, in retail's iteration order.</summary>
        public IReadOnlyList<CharGenClothingColor> Ordered { get; }

        /// <summary>Every template key.</summary>
        public FrozenSet<uint> Templates { get; }

        public static ClothingColors From(ClothingTable table)
        {
            var effects = table.ClothingSubPalEffects;
            var inDatOrder = effects.Keys.ToArray();
            var ordered = PackableHashOrder(inDatOrder, k => k, effects.BucketSize)
                .Where(k => effects[k].CloSubPalettes.Count > 0)
                .Select(k => new CharGenClothingColor(k, effects[k].CloSubPalettes[0].PaletteSet.DataId))
                .ToArray();
            return new ClothingColors(ordered, inDatOrder.ToFrozenSet());
        }
    }
}

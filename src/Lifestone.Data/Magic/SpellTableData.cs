using System.Text;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Magic;

/// <summary>
/// The SpellTable (0x0E00000E), decoded from the raw record with retail semantics
/// (<c>CSpellTable::UnPack</c>, <c>CSpellBase::UnPack</c>, <c>CSpellBase::InqSpellFormula</c>; acclient 11.4186
/// AC/accmagic). Layout: <c>u32 id</c>; spells as a packed hash table (<c>u16 count, u16 buckets</c>, then
/// <c>count × (u32 key, CSpellBase)</c>); spell sets as a packed hash table of packed hash tables of
/// <c>u32 n, n × u32</c> spell ids (ACE <c>ACE.DatLoader/FileTypes/SpellTable.cs</c>).
/// </summary>
/// <remarks>
/// We decode raw bytes instead of using DatReaderWriter's <c>SpellBase</c> because its component list is lossy: like
/// ACE <c>SpellBase.DecryptFormula</c> it masks any decrypted id above 198 with <c>&amp; 0xFF</c> and drops empty slots.
/// Retail subtracts the key from every non-zero slot and nothing else (<c>SpellFormula::Decrypt</c>).
/// </remarks>
public sealed class SpellTableData
{
    public const uint FileId = 0x0E00000E;

    internal static readonly Encoding Windows1252 = CreateWindows1252();

    private SpellTableData(IReadOnlyDictionary<uint, SpellInfo> spells, IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>> spellSets)
    {
        Spells = spells;
        SpellSets = spellSets;
    }

    public IReadOnlyDictionary<uint, SpellInfo> Spells { get; }

    /// <summary>Equipment set id → (piece count → active spell ids).</summary>
    public IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>> SpellSets { get; }

    /// <summary>Reads and decodes the SpellTable through <see cref="DatSet"/>.</summary>
    public static SpellTableData Load(DatSet dats)
    {
        var raw = DatRaw.Portal(dats, FileId) ?? throw new InvalidDataException($"SpellTable 0x{FileId:X8} not found.");
        return Decode(raw);
    }

    public static SpellTableData Decode(byte[] raw)
    {
        var r = new DatByteReader(raw, FileId);
        var id = r.ReadUInt32();
        if (id != FileId)
        {
            throw new InvalidDataException($"SpellTable: record starts with id 0x{id:X8}.");
        }

        var count = r.ReadUInt16();
        r.ReadUInt16(); // bucket count
        var spells = new Dictionary<uint, SpellInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var key = r.ReadUInt32();
            spells.Add(key, ReadSpell(r, key));
        }

        var setCount = r.ReadUInt16();
        r.ReadUInt16();
        var sets = new Dictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>>(setCount);
        for (var i = 0; i < setCount; i++)
        {
            var setId = r.ReadUInt32();
            var tierCount = r.ReadUInt16();
            r.ReadUInt16();
            var tiers = new Dictionary<uint, IReadOnlyList<uint>>(tierCount);
            for (var t = 0; t < tierCount; t++)
            {
                var pieces = r.ReadUInt32();
                var n = checked((int)r.ReadUInt32());
                var list = new uint[n];
                for (var s = 0; s < n; s++)
                {
                    list[s] = r.ReadUInt32();
                }

                tiers.Add(pieces, list);
            }

            sets.Add(setId, tiers);
        }

        r.RequireEnd("SpellTable");
        return new SpellTableData(spells, sets);
    }

    /// <summary>Retail <c>CSpellBase::UnPack</c> plus the formula decryption of <c>InqSpellFormula</c>.</summary>
    private static SpellInfo ReadSpell(DatByteReader r, uint key)
    {
        var nameBytes = ReadObfuscated(r);
        var descBytes = ReadObfuscated(r);
        var school = (MagicSchool)r.ReadUInt32();
        var icon = r.ReadUInt32();
        var category = r.ReadUInt32();
        var bitfield = r.ReadUInt32();
        var baseMana = r.ReadUInt32();
        var rangeConstant = r.ReadSingle();
        var rangeMod = r.ReadSingle();
        var power = r.ReadUInt32();
        var economy = r.ReadSingle();
        var formulaVersion = r.ReadUInt32();
        var componentLoss = r.ReadSingle();

        // _meta_spell: CMetaSpell::UnPack reads the type, the spell id and the type-specific fields.
        var metaType = (SpellType)r.ReadUInt32();
        var metaId = r.ReadUInt32();
        double duration = 0, portalLifetime = 0;
        float degradeModifier = 0, degradeLimit = 0;
        switch (metaType)
        {
            case SpellType.Enchantment:
            case SpellType.FellowEnchantment:
                duration = r.ReadDouble();
                degradeModifier = r.ReadSingle();
                degradeLimit = r.ReadSingle();
                break;
            case SpellType.PortalSummon:
                portalLifetime = r.ReadDouble();
                break;
        }

        var encrypted = new uint[8];
        for (var i = 0; i < 8; i++)
        {
            encrypted[i] = r.ReadUInt32();
        }

        var formula = SpellFormulaRules.Decrypt(encrypted, SpellFormulaRules.FormulaKey(nameBytes, descBytes));

        return new SpellInfo
        {
            Id = key,
            Name = Windows1252.GetString(nameBytes),
            Description = Windows1252.GetString(descBytes),
            School = school,
            IconId = icon,
            Category = category,
            Flags = (SpellFlags)bitfield,
            BaseMana = baseMana,
            BaseRangeConstant = rangeConstant,
            BaseRangeMod = rangeMod,
            Power = power,
            SpellEconomyMod = economy,
            FormulaVersion = formulaVersion,
            ComponentLoss = componentLoss,
            MetaSpellType = metaType,
            MetaSpellId = metaId,
            Duration = duration,
            DegradeModifier = degradeModifier,
            DegradeLimit = degradeLimit,
            PortalLifetime = portalLifetime,
            EncryptedFormula = encrypted,
            Formula = formula,
            CasterEffect = (PlayScript)r.ReadUInt32(),
            TargetEffect = (PlayScript)r.ReadUInt32(),
            FizzleEffect = (PlayScript)r.ReadUInt32(),
            RecoveryInterval = r.ReadDouble(),
            RecoveryAmount = r.ReadSingle(),
            DisplayOrder = r.ReadUInt32(),
            NonComponentTargetType = r.ReadUInt32(),
            ManaMod = r.ReadUInt32(),
        };
    }

    /// <summary>
    /// <c>AC1Legacy::PStringBase&lt;char&gt;::UnPack</c> (u16 length, bytes, 4-byte alignment) followed by the
    /// nibble swap that <c>CSpellBase::InqSpellFormula</c> applies (<c>c = c &lt;&lt; 4 | c &gt;&gt; 4</c>).
    /// </summary>
    internal static byte[] ReadObfuscated(DatByteReader r)
    {
        // PStringBase<char>::UnPack: a u16 length of 0xFFFF is followed by the real u32 length.
        int length = r.ReadUInt16();
        if (length == 0xFFFF)
        {
            length = checked((int)r.ReadUInt32());
        }

        var bytes = r.ReadBytes(length).ToArray();
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((bytes[i] << 4) | (bytes[i] >> 4));
        }

        r.Align4();

        // UnPack drops one stored trailing NUL from the string length (compute_hash stops at a NUL anyway).
        return bytes.Length > 0 && bytes[^1] == 0 ? bytes[..^1] : bytes;
    }

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}

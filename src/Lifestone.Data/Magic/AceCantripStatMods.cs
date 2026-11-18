using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lifestone.Data.Magic;

/// <summary>
/// One cantrip's row of ACE's spell table: what the spell raises (<c>stat_Mod_Type</c>, ACE's <c>EnchantmentTypeFlags</c>; <c>stat_Mod_Key</c>, the attribute, skill or property id). The client dats' spell table holds no stat mod
/// (<see cref="SpellInfo"/>), so the server's table is the source.
/// </summary>
/// <param name="Id">The spell id.</param>
/// <param name="Name">The spell's name in the world table (for the log; never read for the target).</param>
/// <param name="Type">ACE's <c>stat_Mod_Type</c> (<c>EnchantmentTypeFlags</c>).</param>
/// <param name="Key">ACE's <c>stat_Mod_Key</c>.</param>
public readonly record struct CantripStatMod(uint Id, string Name, uint Type, uint Key)
{
    /// <summary>ACE <c>EnchantmentTypeFlags.Attribute</c> (ACE.Entity/Enum/EnchantmentTypeFlags.cs @95ef426d).</summary>
    public const uint AttributeFlag = 0x1;

    /// <summary>ACE <c>EnchantmentTypeFlags.SecondAtt</c>: a vital.</summary>
    public const uint VitalFlag = 0x2;

    /// <summary>ACE <c>EnchantmentTypeFlags.Int</c>: a PropertyInt.</summary>
    public const uint IntFlag = 0x4;

    /// <summary>ACE <c>EnchantmentTypeFlags.Float</c>: a PropertyFloat.</summary>
    public const uint FloatFlag = 0x8;

    /// <summary>ACE <c>EnchantmentTypeFlags.Skill</c>.</summary>
    public const uint SkillFlag = 0x10;

    /// <summary>ACE <c>EnchantmentTypeFlags.BodyArmorValue</c>.</summary>
    public const uint BodyArmorFlag = 0x80;

    /// <summary>ACE <c>EnchantmentTypeFlags.MultipleStat</c>: the row raises more than one stat (its key does not name one).</summary>
    public const uint MultipleStatFlag = 0x2000;

    /// <summary>True when the row raises an attribute (<see cref="Key"/> is the attribute id, 1 Strength to 6 Self).</summary>
    public bool IsAttribute => (Type & AttributeFlag) != 0;

    /// <summary>True when the row raises a skill (<see cref="Key"/> is the skill id).</summary>
    public bool IsSkill => (Type & SkillFlag) != 0;

    /// <summary>True when the row raises more than one stat.</summary>
    public bool IsMultipleStat => (Type & MultipleStatFlag) != 0;
}

/// <summary>
/// M6 14 (the owner, 2026-11-16: keep only the useful cantrips): the loader of <c>Magic/Data/ace-world-cantrip-stat-mods.json</c>, a small AGPL-3.0 extract of ACE's world database (ACE-World-Database v0.9.296, table <c>spell</c>): every
/// Minor, Major, Epic and Legendary spell's <c>stat_Mod_Type</c> and <c>stat_Mod_Key</c>, by spell id. The file names the SELECT that made it. Embedded in this assembly (Lifestone.Data.csproj embeds <c>Magic/Data/*.json</c>).
/// </summary>
public static class AceCantripStatMods
{
    /// <summary>The extract's file name (under <c>src/Lifestone.Data/Magic/Data/</c>).</summary>
    public const string FileName = "ace-world-cantrip-stat-mods.json";

    /// <summary>The extract's manifest name.</summary>
    public const string ResourceName = "Lifestone.Data.Magic.Data." + FileName;

    private static readonly Lazy<IReadOnlyDictionary<uint, CantripStatMod>> Rows = new(Load);

    /// <summary>The rows by spell id.</summary>
    public static IReadOnlyDictionary<uint, CantripStatMod> ById => Rows.Value;

    /// <summary>The row of <paramref name="spellId"/>, or null when the extract holds none.</summary>
    /// <param name="spellId">The spell id.</param>
    public static CantripStatMod? Of(uint spellId) => ById.TryGetValue(spellId, out var row) ? row : null;

    /// <summary>Parses an extract's text.</summary>
    /// <param name="json">The JSON.</param>
    public static IReadOnlyDictionary<uint, CantripStatMod> Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json, JsonOptions) ?? throw new InvalidDataException($"{FileName}: empty document.");
        var rows = new Dictionary<uint, CantripStatMod>();
        foreach (var s in doc.Spells ?? [])
        {
            rows[s.Id] = new CantripStatMod(s.Id, s.Name ?? throw new InvalidDataException($"{FileName}: spell {s.Id} has no name."), s.Type, s.Key);
        }

        return rows;
    }

    private static IReadOnlyDictionary<uint, CantripStatMod> Load()
    {
        using var stream = typeof(AceCantripStatMods).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"{FileName} is not embedded in {typeof(AceCantripStatMods).Assembly.GetName().Name} (Lifestone.Data.csproj embeds Magic/Data/*.json).");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private sealed class Document
    {
        [JsonPropertyName("spells")]
        public List<Row>? Spells { get; set; }
    }

    private sealed class Row
    {
        [JsonPropertyName("id")]
        public uint Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public uint Type { get; set; }

        [JsonPropertyName("key")]
        public uint Key { get; set; }
    }
}

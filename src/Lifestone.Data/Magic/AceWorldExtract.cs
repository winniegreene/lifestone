using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lifestone.Data.Magic;

/// <summary>
/// War-spell projectile data that the client dats do not contain: which weenie a spell launches and that weenie's
/// visual properties, from ACE's world database (table <c>spell</c> and the projectile weenie's properties).
/// </summary>
public sealed record WarProjectileEntry
{
    public required uint SpellId { get; init; }

    public required string SpellName { get; init; }

    /// <summary>Weenie class id of the projectile (ACE <c>spell.wcid</c>).</summary>
    public required uint Wcid { get; init; }

    public string? WeenieClassName { get; init; }

    public string? WeenieName { get; init; }

    /// <summary>Projectile Setup (0x02, <c>PropertyDataId.Setup</c>).</summary>
    public required uint SetupId { get; init; }

    public uint SoundTableId { get; init; }

    /// <summary>Projectile PhysicsScriptTable (<c>PropertyDataId.PhysicsEffectTable</c>); null when the weenie has none.</summary>
    public uint? PhysicsEffectTableId { get; init; }

    /// <summary><c>PropertyFloat.DefaultScale</c>; null = 1.</summary>
    public float? DefaultScale { get; init; }

    /// <summary><c>PropertyFloat.MaximumVelocity</c>: ACE's base projectile speed (m/s).</summary>
    public float? MaximumVelocity { get; init; }

    /// <summary><c>PropertyFloat.RotationSpeed</c> (revolutions per second about local X; disables AlignPath in ACE).</summary>
    public float? RotationSpeed { get; init; }

    public int? NumProjectiles { get; init; }

    public int? NumProjectilesVariance { get; init; }

    public float? SpreadAngle { get; init; }

    public float? VerticalAngle { get; init; }

    public float? DefaultLaunchAngle { get; init; }

    public bool? NonTracking { get; init; }

    public Vector3? CreateOffset { get; init; }

    public Vector3? Padding { get; init; }

    public Vector3? Dims { get; init; }

    public Vector3? Peturbation { get; init; }
}

/// <summary>A creature's visual identity from its ACE weenie (the dats have no weenies).</summary>
public sealed record CreatureVisualEntry
{
    public required uint Wcid { get; init; }

    public string? WeenieClassName { get; init; }

    public required string Name { get; init; }

    public required uint SetupId { get; init; }

    public required uint MotionTableId { get; init; }

    public uint SoundTableId { get; init; }

    public required uint PhysicsEffectTableId { get; init; }

    public float? DefaultScale { get; init; }
}

/// <summary>
/// Loader for <c>Magic/Data/ace-world-spell-visuals.json</c>, a small AGPL-3.0 extract of ACE's world database
/// (ACE-World-Database v0.9.296) with the projectile weenies of the showcase war spells and one target creature
/// (Drudge Skulker). See docs/spell-visuals.md for what the dats alone can and cannot provide.
/// </summary>
public sealed class AceWorldExtract
{
    /// <summary>File name of the extract (under <c>src/Lifestone.Data/Magic/Data/</c>).</summary>
    public const string FileName = "ace-world-spell-visuals.json";

    /// <summary>Manifest name the extract will have once the project embeds it (requested from the lead).</summary>
    public const string ResourceName = "Lifestone.Data.Magic.Data." + FileName;

    private AceWorldExtract(IReadOnlyList<WarProjectileEntry> projectiles, IReadOnlyList<CreatureVisualEntry> creatures)
    {
        Projectiles = projectiles;
        Creatures = creatures;
        ProjectilesBySpell = projectiles.ToDictionary(p => p.SpellId);
    }

    public IReadOnlyList<WarProjectileEntry> Projectiles { get; }

    public IReadOnlyDictionary<uint, WarProjectileEntry> ProjectilesBySpell { get; }

    public IReadOnlyList<CreatureVisualEntry> Creatures { get; }

    public static AceWorldExtract Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json, JsonOptions)
            ?? throw new InvalidDataException($"{FileName}: empty document.");
        var projectiles = (doc.Projectiles ?? []).Select(p => new WarProjectileEntry
        {
            SpellId = p.SpellId,
            SpellName = p.SpellName ?? throw new InvalidDataException($"{FileName}: projectile without spellName."),
            Wcid = p.Wcid,
            WeenieClassName = p.WeenieClassName,
            WeenieName = p.WeenieName,
            SetupId = Hex(p.SetupId) ?? throw new InvalidDataException($"{FileName}: spell {p.SpellId} has no setupId."),
            SoundTableId = Hex(p.SoundTableId) ?? 0,
            PhysicsEffectTableId = Hex(p.PhysicsEffectTableId),
            DefaultScale = p.DefaultScale,
            MaximumVelocity = p.MaximumVelocity,
            RotationSpeed = p.RotationSpeed,
            NumProjectiles = p.NumProjectiles,
            NumProjectilesVariance = p.NumProjectilesVariance,
            SpreadAngle = p.SpreadAngle,
            VerticalAngle = p.VerticalAngle,
            DefaultLaunchAngle = p.DefaultLaunchAngle,
            NonTracking = p.NonTracking,
            CreateOffset = Vec(p.CreateOffset),
            Padding = Vec(p.Padding),
            Dims = Vec(p.Dims),
            Peturbation = Vec(p.Peturbation),
        }).ToArray();
        var creatures = (doc.Creatures ?? []).Select(c => new CreatureVisualEntry
        {
            Wcid = c.Wcid,
            WeenieClassName = c.WeenieClassName,
            Name = c.Name ?? throw new InvalidDataException($"{FileName}: creature {c.Wcid} has no name."),
            SetupId = Hex(c.SetupId) ?? throw new InvalidDataException($"{FileName}: creature {c.Wcid} has no setupId."),
            MotionTableId = Hex(c.MotionTableId) ?? throw new InvalidDataException($"{FileName}: creature {c.Wcid} has no motionTableId."),
            SoundTableId = Hex(c.SoundTableId) ?? 0,
            PhysicsEffectTableId = Hex(c.PhysicsEffectTableId) ?? throw new InvalidDataException($"{FileName}: creature {c.Wcid} has no physicsEffectTableId."),
            DefaultScale = c.DefaultScale,
        }).ToArray();
        return new AceWorldExtract(projectiles, creatures);
    }

    public static AceWorldExtract LoadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// The extract embedded in this assembly (Lifestone.Data.csproj embeds <c>Magic/Data/*.json</c>), or null when a build
    /// left it out; <see cref="FindSourceFile"/> finds the JSON in a source checkout instead.
    /// </summary>
    public static AceWorldExtract? TryLoadEmbedded()
    {
        using var stream = typeof(AceWorldExtract).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Looks for <c>src/Lifestone.Data/Magic/Data/ace-world-spell-visuals.json</c> in <paramref name="startDirectory"/>
    /// (default: the application base directory) and its parents — for tests and tools run from a checkout.
    /// </summary>
    public static string? FindSourceFile(string? startDirectory = null)
    {
        for (var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Lifestone.Data", "Magic", "Data", FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Embedded resource first, then <see cref="FindSourceFile"/>; throws when neither is available.</summary>
    public static AceWorldExtract Load()
    {
        var embedded = TryLoadEmbedded();
        if (embedded is not null)
        {
            return embedded;
        }

        var path = FindSourceFile() ?? throw new FileNotFoundException(
            $"{FileName} is neither embedded in {Assembly.GetExecutingAssembly().GetName().Name} nor found under src/Lifestone.Data/Magic/Data/ above {AppContext.BaseDirectory}.");
        return LoadFile(path);
    }

    private static uint? Hex(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static Vector3? Vec(float[]? v) =>
        v is null ? null : v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : throw new InvalidDataException($"{FileName}: expected a 3-vector.");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private sealed class Document
    {
        [JsonPropertyName("projectiles")]
        public List<ProjectileJson>? Projectiles { get; set; }

        [JsonPropertyName("creatures")]
        public List<CreatureJson>? Creatures { get; set; }
    }

    private sealed class ProjectileJson
    {
        [JsonPropertyName("spellId")] public uint SpellId { get; set; }
        [JsonPropertyName("spellName")] public string? SpellName { get; set; }
        [JsonPropertyName("wcid")] public uint Wcid { get; set; }
        [JsonPropertyName("weenieClassName")] public string? WeenieClassName { get; set; }
        [JsonPropertyName("weenieName")] public string? WeenieName { get; set; }
        [JsonPropertyName("setupId")] public string? SetupId { get; set; }
        [JsonPropertyName("soundTableId")] public string? SoundTableId { get; set; }
        [JsonPropertyName("physicsEffectTableId")] public string? PhysicsEffectTableId { get; set; }
        [JsonPropertyName("defaultScale")] public float? DefaultScale { get; set; }
        [JsonPropertyName("maximumVelocity")] public float? MaximumVelocity { get; set; }
        [JsonPropertyName("rotationSpeed")] public float? RotationSpeed { get; set; }
        [JsonPropertyName("numProjectiles")] public int? NumProjectiles { get; set; }
        [JsonPropertyName("numProjectilesVariance")] public int? NumProjectilesVariance { get; set; }
        [JsonPropertyName("spreadAngle")] public float? SpreadAngle { get; set; }
        [JsonPropertyName("verticalAngle")] public float? VerticalAngle { get; set; }
        [JsonPropertyName("defaultLaunchAngle")] public float? DefaultLaunchAngle { get; set; }
        [JsonPropertyName("nonTracking")] public bool? NonTracking { get; set; }
        [JsonPropertyName("createOffset")] public float[]? CreateOffset { get; set; }
        [JsonPropertyName("padding")] public float[]? Padding { get; set; }
        [JsonPropertyName("dims")] public float[]? Dims { get; set; }
        [JsonPropertyName("peturbation")] public float[]? Peturbation { get; set; }
    }

    private sealed class CreatureJson
    {
        [JsonPropertyName("wcid")] public uint Wcid { get; set; }
        [JsonPropertyName("weenieClassName")] public string? WeenieClassName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("setupId")] public string? SetupId { get; set; }
        [JsonPropertyName("motionTableId")] public string? MotionTableId { get; set; }
        [JsonPropertyName("soundTableId")] public string? SoundTableId { get; set; }
        [JsonPropertyName("physicsEffectTableId")] public string? PhysicsEffectTableId { get; set; }
        [JsonPropertyName("defaultScale")] public float? DefaultScale { get; set; }
    }
}

using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace Lifestone.Client.Session.Grounds;

/// <summary>A profile that cannot load: the file and the field's JSON path are named (D43).</summary>
public sealed class GroundProfileException : Exception
{
    /// <summary>A profile error.</summary>
    /// <param name="file">The profile's file name.</param>
    /// <param name="path">The field's JSON path (for example <c>$.storeys[0].cells</c>).</param>
    /// <param name="problem">What is wrong.</param>
    public GroundProfileException(string file, string path, string problem)
        : base($"hunting-ground profile {file}: {path} {problem}")
    {
        File = file;
        JsonPath = path;
    }

    /// <summary>The profile's file name.</summary>
    public string File { get; }

    /// <summary>The field's JSON path.</summary>
    public string JsonPath { get; }
}

/// <summary>
/// The hunting grounds (M5 DoD 4, D22, D43): every profile embedded under <c>Session/Grounds/profiles/</c>, read once with <c>System.Text.Json</c> (the BCL; no package). A missing field, a field
/// the profile shape does not have, a duplicated field and an out-of-range value (a cell outside the profile's landblocks, a spell plan whose level ranges overlap, a negative delay, a tour
/// cell that is not walkable) fail at load with a <see cref="GroundProfileException"/> naming the file and the field's JSON path. <see cref="Get"/> of an unknown key throws, naming it.
/// </summary>
/// <remarks>
/// The format: cell ids, guids, landblocks and item-type masks are hex strings (<c>"0x01050375"</c>); a cell list (a storey's cells, the no-go set) may also hold an inclusive range
/// (<c>"0x01050100-0x01050121"</c>); weenie class ids, spell ids, levels and prices are numbers. <c>null</c> is a value only for <c>portal</c> and <c>outdoor</c>. An outdoor creature has a
/// <c>spawn_mass</c> (0 to 1, item 2 g3): the chance that one roll of a spot's generator spawns it. The <c>supplies</c> section (item 3 m0, D48, D49) has <c>stock</c> lines (wcid, name, keep, trip_under, stop_under with
/// keep &gt;= trip_under &gt;= stop_under &gt;= 0), <c>kit</c> lines (wcid, name, slot worn or wielded), <c>burden_trip_percent</c> and the <c>purse</c> (coin and trade notes); a line's vendor and price are the vendors' <c>sells</c>
/// and nothing else (a ware has one vendor and a vendor lists only supply lines), and every row of the spell plan must have components at its level that are all stock lines (<c>TODO(ground-plan-gap)</c>).
/// </remarks>
public static class GroundCatalog
{
    /// <summary>The logical-name prefix of the embedded profiles (Lifestone.Client.csproj).</summary>
    internal const string ResourcePrefix = "Lifestone.Client.Session.Grounds.profiles.";

    private static readonly Lazy<IReadOnlyList<HuntingGround>> Loaded = new(LoadAll);

    /// <summary>Every profile, in key order.</summary>
    public static IReadOnlyList<HuntingGround> All => Loaded.Value;

    /// <summary>The profile of <paramref name="key"/>.</summary>
    /// <param name="key">The profile's key (its file name without <c>.json</c>).</param>
    /// <exception cref="KeyNotFoundException">No profile has the key.</exception>
    public static HuntingGround Get(string key) =>
        TryGet(key) ?? throw new KeyNotFoundException($"no hunting-ground profile '{key}' (known: {string.Join(", ", All.Select(g => g.Key))})");

    /// <summary>The profile of <paramref name="key"/>, or null when no profile has it (M6 item 0 a1: the ladder's pending rungs are checked against it).</summary>
    /// <param name="key">The profile's key (its file name without <c>.json</c>).</param>
    public static HuntingGround? TryGet(string key)
    {
        foreach (var g in All)
        {
            if (string.Equals(g.Key, key, StringComparison.Ordinal))
            {
                return g;
            }
        }

        return null;
    }

    /// <summary>The embedded profile text of a file name (the key plus <c>.json</c>), or null.</summary>
    /// <param name="file">The file name.</param>
    internal static string? ResourceText(string file)
    {
        using var stream = typeof(GroundCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + file);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads one profile; throws <see cref="GroundProfileException"/> on any error.</summary>
    /// <param name="file">The file name (the key plus <c>.json</c>), for the messages and the key check.</param>
    /// <param name="json">The profile's text.</param>
    internal static HuntingGround Parse(string file, string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            throw new GroundProfileException(file, "$", $"is not JSON: {e.Message}");
        }

        using (doc)
        {
            return new ProfileReader(file).Read(doc.RootElement);
        }
    }

    private static IReadOnlyList<HuntingGround> LoadAll()
    {
        var assembly = typeof(GroundCatalog).Assembly;
        var grounds = new List<HuntingGround>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var file = name[ResourcePrefix.Length..];
            grounds.Add(Parse(file, ResourceText(file)!));
        }

        if (grounds.Count == 0)
        {
            throw new InvalidOperationException($"no hunting-ground profile is embedded (resource prefix {ResourcePrefix}; Lifestone.Client.csproj)");
        }

        return grounds.AsReadOnly();
    }

    /// <summary>A JSON object being read: takes fields by name and, at <see cref="End"/>, fails on a field it did not take.</summary>
    private sealed class Section(ProfileReader reader, JsonElement element, string path)
    {
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        public JsonElement Take(string name)
        {
            _taken.Add(name);
            var found = false;
            var value = default(JsonElement);
            foreach (var p in element.EnumerateObject())
            {
                if (p.NameEquals(name))
                {
                    if (found)
                    {
                        throw reader.Fail($"{path}.{name}", "appears twice");
                    }

                    found = true;
                    value = p.Value;
                }
            }

            if (!found)
            {
                throw reader.Fail($"{path}.{name}", "is missing (a required field)");
            }

            return value;
        }

        public void End()
        {
            foreach (var p in element.EnumerateObject())
            {
                if (!_taken.Contains(p.Name))
                {
                    throw reader.Fail($"{path}.{p.Name}", "is not a field of the hunting-ground profile (unknown field)");
                }
            }
        }

        public string Text(string name) => reader.Text(Take(name), $"{path}.{name}");

        public int Int(string name, int min, int max) => reader.Int(Take(name), $"{path}.{name}", min, max);

        /// <summary>A known optional whole number: 0 when the field is absent, else read as strictly as <see cref="Int"/>.</summary>
        public int OptionalInt(string name, int min, int max)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (p.NameEquals(name))
                {
                    return Int(name, min, max);
                }
            }

            return 0;
        }

        public double Number(string name, double min, double max) => reader.Number(Take(name), $"{path}.{name}", min, max);

        /// <summary>A known optional number (M5 7 F29): null when the field is absent, else read as strictly as <see cref="Number"/>.</summary>
        public double? OptionalNumber(string name, double min, double max)
        {
            _taken.Add(name);
            return element.EnumerateObject().Any(p => p.NameEquals(name)) ? Number(name, min, max) : null;
        }

        public uint Hex(string name) => reader.Hex(Take(name), $"{path}.{name}");

        /// <summary>M6 3 a1: a known optional hex mask: 0 when the field is absent, else read as strictly as <see cref="Hex"/> and above 0 (a mask of 0 says nothing: leave the field out).</summary>
        public uint OptionalHex(string name)
        {
            _taken.Add(name);
            if (!Has(name))
            {
                return 0;
            }

            var v = Hex(name);
            return v != 0 ? v : throw reader.Fail($"{path}.{name}", "is 0 (leave the field out when the mask is not given)");
        }

        /// <summary>M6 3 a1: a required whole number that may be null (a property the weenie does not have): null for a JSON null, else read as strictly as <see cref="Int"/>.</summary>
        public int? NullableInt(string name, int min, int max)
        {
            var e = Take(name);
            return e.ValueKind == JsonValueKind.Null ? null : reader.Int(e, $"{path}.{name}", min, max);
        }

        /// <summary>M6 3 a1: a required true or false.</summary>
        public bool Bool(string name)
        {
            var e = Take(name);
            return e.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw reader.Fail($"{path}.{name}", $"is {e.ValueKind}, not true or false"),
            };
        }

        public Vector3 Point(string name) => reader.Point(Take(name), $"{path}.{name}");

        /// <summary>A known optional point: null when the field is absent, else read as strictly as <see cref="Point"/> (a JSON null or a malformed point fails, naming the field).</summary>
        public Vector3? OptionalPoint(string name)
        {
            _taken.Add(name);
            return element.EnumerateObject().Any(p => p.NameEquals(name)) ? Point(name) : null;
        }

        /// <summary>A known optional text (M5 7 F8): null when the field is absent, else read as strictly as <see cref="Text"/> (a JSON null or an empty string fails, naming the field).</summary>
        public string? OptionalText(string name)
        {
            _taken.Add(name);
            return element.EnumerateObject().Any(p => p.NameEquals(name)) ? Text(name) : null;
        }

        /// <summary>
        /// M6 16 Q12: this creature's optional <c>from_bestiary</c> list: the facts the loader reads from the bestiary's tables instead of the profile (a creature is named by its weenie class, its numbers are the bestiary's). Each name
        /// must be one of <paramref name="known"/>, listed once and not also stated here (one source for a fact). Empty when the field is absent.
        /// </summary>
        /// <param name="known">The fact names this kind of creature may take from the bestiary.</param>
        public HashSet<string> FromBestiary(params string[] known)
        {
            _taken.Add("from_bestiary");
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (!Has("from_bestiary"))
            {
                return names;
            }

            reader.List(Take("from_bestiary"), $"{path}.from_bestiary", (e, p) =>
            {
                var name = reader.Text(e, p);
                if (!known.Contains(name, StringComparer.Ordinal))
                {
                    throw reader.Fail(p, $"'{name}' is out of range ({string.Join(", ", known)})");
                }

                if (Has(name))
                {
                    throw reader.Fail(p, $"'{name}' is also stated in this creature (a fact has one source: leave it out or take it from the bestiary)");
                }

                if (!names.Add(name))
                {
                    throw reader.Fail(p, $"'{name}' is listed twice");
                }

                return name;
            });
            return names;
        }

        /// <summary>True when the object has a field of this name (an optional section).</summary>
        public bool Has(string name) => element.EnumerateObject().Any(p => p.NameEquals(name));

        /// <summary>M5 7 F30: this loot section's optional <c>keep_aside</c> block, or null; its bonus, tiers and cantrips are required, the lists non-empty and without repeats.</summary>
        public GroundKeepAside? KeepAside()
        {
            if (!Has("keep_aside"))
            {
                return null;
            }

            var block = Object("keep_aside")!;
            var bonus = block.Number("min_war_bonus_percent", 0.01, 1000);
            var tiers = block.List("cantrip_tiers", (e, p) => GroundKeepAside.KnownTiers.Contains(reader.Text(e, p), StringComparer.Ordinal) ? reader.Text(e, p) : throw reader.Fail(p, $"'{reader.Text(e, p)}' is out of range (Minor, Major, Epic or Legendary)"));
            var cantrips = block.List("cantrips", (e, p) => reader.Text(e, p).Trim() is { Length: > 0 } family ? family : throw reader.Fail(p, "is blank"));
            block.OptionalText("source");
            block.End();
            Distinct(tiers, $"{path}.keep_aside.cantrip_tiers");
            Distinct(cantrips, $"{path}.keep_aside.cantrips");
            return new GroundKeepAside(bonus, tiers, cantrips);
        }

        private void Distinct(IReadOnlyList<string> names, string at)
        {
            if (names.Count == 0)
            {
                throw reader.Fail(at, "is empty (name at least one)");
            }

            for (var i = 0; i < names.Count; i++)
            {
                if (names.Take(i).Contains(names[i], StringComparer.OrdinalIgnoreCase))
                {
                    throw reader.Fail($"{at}[{i}]", $"'{names[i]}' is listed twice");
                }
            }
        }

        public Section? Object(string name, bool nullable = false) => reader.Object(Take(name), $"{path}.{name}", nullable);

        public IReadOnlyList<T> List<T>(string name, Func<JsonElement, string, T> item) => reader.List(Take(name), $"{path}.{name}", item);
    }

    private sealed class ProfileReader(string file)
    {
        /// <summary>The largest count a stock line or a purse note may hold (a sanity bound; a stack is at most 1,000).</summary>
        private const int MaxCount = 100_000;

        /// <summary>The most pyreals one purse value may hold (a sanity bound).</summary>
        private const int MaxPyreals = 100_000_000;

        private IReadOnlyList<ushort> _landblocks = [];

        public GroundProfileException Fail(string path, string problem) => new(file, path, problem);

        /// <summary>M6 16 Q12: the bestiary's tables' numbers of a creature a profile takes numbers from (<c>from_bestiary</c>); the load fails, naming the creature, when a table does not hold it.</summary>
        private Creatures.TableFacts TableOf(uint wcid, string path) =>
            Creatures.Bestiary.TableFactsOf(wcid) ?? throw Fail($"{path}.from_bestiary", $"weenie {wcid} is not in the bestiary's tables (a creature named by reference must be a kind of the bestiary)");

        /// <summary>M6 16 Q12: a number the bestiary gives, held to the range the profile's own field is read in (a value out of it fails the load, naming the field).</summary>
        private int Checked(int value, int min, int max, string path) => value >= min && value <= max ? value : throw Fail(path, $"{value} (the bestiary's) is out of range ({min} to {max})");

        private double Checked(double value, double min, double max, string path) => value >= min && value <= max ? value : throw Fail(path, $"{value} (the bestiary's) is out of range ({min} to {max})");

        private long Checked(long value, long min, long max, string path) => value >= min && value <= max ? value : throw Fail(path, $"{value} (the bestiary's) is out of range ({min} to {max})");

        /// <summary>
        /// M6 7 AB: the optional rest before the next pull of <c>fight</c>: null when neither floor is written (no rest rule, the den's case); with a floor the until share, the posture and the sight reach are required, the until at least both floors.
        /// </summary>
        private Vitals.GroundPullRest? PullRestOf(Section fight)
        {
            var mana = fight.OptionalInt("pull_mana_percent", 1, 100);
            var health = fight.OptionalInt("pull_health_percent", 1, 100);
            var stamina = fight.OptionalInt("pull_stamina_percent", 1, 100); // M6 7 AN
            var full = fight.OptionalInt("pull_full_percent", 1, 100); // M6 7 AN: the owner's fullness bar
            if (mana == 0 && health == 0 && stamina == 0 && full == 0)
            {
                return null;
            }

            var until = fight.Int("pull_rest_until_percent", 1, 100);
            if (until < mana || until < health || until < stamina)
            {
                throw Fail("$.fight.pull_rest_until_percent", $"{until} is under a floor (pull_mana_percent {mana}, pull_health_percent {health}, pull_stamina_percent {stamina})");
            }

            var posture = fight.Text("pull_rest_posture") switch
            {
                "sit" => Vitals.RestPosture.Sit,
                "lie" => Vitals.RestPosture.Lie,
                "stand" => Vitals.RestPosture.Stand,
                var other => throw Fail("$.fight.pull_rest_posture", $"'{other}' is not sit, lie or stand"),
            };
            var potions = fight.OptionalInt("pull_rest_potions", 1, 1) == 1; // M6 8: a held Mana Potion is drunk before a rest for mana
            var potionMana = fight.OptionalInt("pull_potion_mana_percent", 1, 100); // M6 8 (the owner, 2026-11-11): the mana's line while potions are in use
            var potionStamina = fight.OptionalInt("pull_potion_stamina_percent", 1, 100); // M6 8: the stamina's floor in the bar's place
            if (!potions && (potionMana != 0 || potionStamina != 0))
            {
                throw Fail("$.fight.pull_potion_mana_percent", "a potion line needs pull_rest_potions 1");
            }

            return new Vitals.GroundPullRest(mana, health, until, posture, fight.Number("pull_aggro_metres", 1, 100), stamina, full, potions, potionMana, potionStamina);
        }

        public HuntingGround Read(JsonElement root)
        {
            var top = Object(root, "$", nullable: false)!;
            var key = top.Text("key");
            if (key != Path.GetFileNameWithoutExtension(file))
            {
                throw Fail("$.key", $"'{key}' does not match the file name {file}");
            }

            var name = top.Text("name");
            var kind = top.Text("kind") switch
            {
                "dungeon" => GroundKind.Dungeon,
                "outdoor" => GroundKind.Outdoor,
                var other => throw Fail("$.kind", $"'{other}' is out of range (dungeon or outdoor)"),
            };

            var sourcesSection = top.Object("sources")!;
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var section in HuntingGround.SectionNames)
            {
                sources[section] = sourcesSection.Text(section);
            }

            sourcesSection.End();

            _landblocks = top.List("landblocks", (e, p) =>
            {
                var lb = Hex(e, p);
                return lb <= 0xFFFF ? (ushort)lb : throw Fail(p, $"0x{lb:X} is out of range (a landblock is 0x0000 to 0xFFFF)");
            });
            if (_landblocks.Count == 0)
            {
                throw Fail("$.landblocks", "is empty (a ground has at least one landblock)");
            }

            if (_landblocks.Distinct().Count() != _landblocks.Count)
            {
                throw Fail("$.landblocks", "holds a landblock twice");
            }

            var arrivalSection = top.Object("arrival")!;
            var arrivalCell = OwnCell(arrivalSection.Take("cell"), "$.arrival.cell");
            var origin = arrivalSection.Point("origin");
            var heading = arrivalSection.Object("heading")!;
            var arrival = new GroundArrival(arrivalCell, origin, (float)heading.Number("w", -1, 1), (float)heading.Number("z", -1, 1));
            heading.End();
            arrivalSection.End();

            var walkSection = top.Object("walk")!;
            var walk = new WalkModel(
                (float)walkSection.Number("doorway_lean", 0, 1),
                (float)walkSection.Number("stop_within_m", 0.01, 1000),
                walkSection.Number("stop_dwell_s", 0, 3600),
                walkSection.Number("leg_s", 0.01, 86400));
            walkSection.End();

            var storeys = top.List("storeys", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var storey = new GroundStorey(s.Text("name"), (float)s.Number("z_min", -10000, 10000), (float)s.Number("z_max", -10000, 10000), Cells(s.Take("cells"), $"{p}.cells"));
                if (storey.ZMin > storey.ZMax)
                {
                    throw Fail($"{p}.z_min", $"{storey.ZMin} is above z_max {storey.ZMax}");
                }

                s.End();
                return storey;
            });
            for (var i = 0; i < storeys.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    var both = storeys[i].Cells.Cells.FirstOrDefault(storeys[j].Cells.Contains);
                    if (both != 0)
                    {
                        throw Fail($"$.storeys[{i}].cells", $"0x{both:X8} is also in $.storeys[{j}] (a cell is in one storey)");
                    }
                }
            }

            var noGo = Cells(top.Take("no_go"), "$.no_go");

            var targets = top.List("targets", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var roleText = s.Text("role");
                var role = roleText switch
                {
                    "target" or "kill-first" => GroundRole.Target, // M6 3 a1: a kill-first creature is a target the pick orders first (KillFirst), so every Role == Target check holds
                    "avoid" => GroundRole.Avoid,
                    var other => throw Fail($"{p}.role", $"'{other}' is out of range (target, kill-first or avoid)"),
                };
                var wcid = (uint)s.Int("wcid", 1, int.MaxValue);
                var name = s.Text("name");
                var level = s.Int("level", 1, 10000);
                var moved = s.FromBestiary("max_health", "magic_defence", "xp", "resist_health_drain"); // M6 16 Q12: the creature's numbers by reference
                var table = moved.Count > 0 ? TableOf(wcid, p) : default;
                var t = new GroundTarget(wcid, name, level, role, moved.Contains("max_health") ? Checked(table.MaxHealth, 1, 1_000_000, $"{p}.max_health") : s.OptionalInt("max_health", 1, 1_000_000), moved.Contains("magic_defence") ? Checked(table.MagicDefence, 1, 10_000, $"{p}.magic_defence") : s.OptionalInt("magic_defence", 1, 10_000), s.OptionalText("opener"))
                {
                    KillFirst = roleText == "kill-first",
                    Attributes = TargetAttributes(s), // M6 3 a1: the target stats, each optional
                    DamageTypes = s.OptionalHex("damage_types"),
                    SwingSeconds = s.OptionalNumber("swing_s", 0.01, 60),
                    ResistHealthDrain = moved.Contains("resist_health_drain") ? Checked(table.ResistHealthDrain, 0, 10, $"{p}.resist_health_drain") : s.OptionalNumber("resist_health_drain", 0, 10) ?? 1.0, // M6 14 drain resistance: ResistHealthDrain (float 125), named in sources.targets
                };
                if (t.Opener is not null && role != GroundRole.Target)
                {
                    throw Fail($"{p}.opener", "an avoided creature has no opener (only a target is fought)");
                }

                // M6 14 holding safety: the attack's facts the derived fight line reads (FightLines), each pair with its source; a d_val needs the d_var, the Strength (attributes) and the source that names the SELECT
                if (s.Has("d_val") || s.Has("d_var") || s.Has("attack_source"))
                {
                    var dVal = s.Int("d_val", 1, 100_000);
                    var dVar = s.Number("d_var", 0, 1);
                    _ = s.Text("attack_source");
                    if (t.Attributes is null)
                    {
                        throw Fail($"{p}.d_val", "an attack's facts need the creature's attributes (its Strength scales the hit)");
                    }

                    t = t with { DVal = dVal, DVar = dVar };
                }

                if (s.Has("xp"))
                {
                    t = t with { Xp = s.Int("xp", 1, int.MaxValue) }; // M6 14 approach cost: the kill's experience (XpOverride), a rung's planned rate
                }
                else if (moved.Contains("xp"))
                {
                    t = t with { Xp = Checked(table.Xp, 1, int.MaxValue, $"{p}.xp") };
                }

                s.End();
                return t;
            });
            Unique(targets.Select(t => t.Wcid), "$.targets", "wcid");

            var band = top.Object("level_band")!;
            var levelBand = new LevelBand(band.Int("min", 1, 10000), band.Int("max", 1, 10000));
            if (levelBand.Min > levelBand.Max)
            {
                throw Fail("$.level_band.min", $"{levelBand.Min} is above max {levelBand.Max}");
            }

            band.End();

            var portalSection = top.Object("portal", nullable: true);
            GroundPortal? portal = null;
            if (portalSection is not null)
            {
                portal = new GroundPortal(
                    portalSection.Hex("guid"),
                    portalSection.Hex("cell"),
                    (uint)portalSection.Int("tie_spell", 1, int.MaxValue),
                    (uint)portalSection.Int("recall_spell", 1, int.MaxValue),
                    (uint)portalSection.Int("wcid", 1, int.MaxValue),
                    portalSection.Point("origin"),
                    portalSection.Hex("front_cell"),
                    portalSection.Point("front_point"));
                portalSection.End();
            }

            var entry = top.Has("entry") ? Entry(top.Object("entry")!, portal) : null; // M6 3 a1: the entry portal's gate (optional)
            var exit = top.Has("exit") ? Exit(top.Object("exit")!, kind) : null; // M6 3 a1: the exit's kind (optional)
            var jumps = top.Has("jumps") ? top.List("jumps", Jump) : []; // M6 14 pits: the pits a running jump crosses (optional)
            var plates = top.Has("plates") ? Plates(top.Object("plates")!) : null; // M6 14 holding plates: the pressure plates the walks keep off (optional)
            GroundGalleries? galleries = null;
            if (top.Has("galleries"))
            {
                // M6 redoubt: the floorless cells a body stands in and the cells that hold their floors (optional)
                var gallerySection = top.Object("galleries")!;
                var galleryCells = Cells(gallerySection.Take("cells"), "$.galleries.cells");
                var galleryFloors = Cells(gallerySection.Take("floors"), "$.galleries.floors");
                if (galleryCells.Count == 0 || galleryFloors.Count == 0)
                {
                    throw Fail("$.galleries", "names no cell or no floor (a gallery is a floorless cell and the cells that hold its floor)");
                }

                foreach (var cell in galleryCells.Cells)
                {
                    if (!storeys.Any(s => s.Cells.Contains(cell)))
                    {
                        throw Fail("$.galleries.cells", $"0x{cell:X8} is on no storey (a body stands in a gallery's cell)");
                    }
                }

                foreach (var cell in galleryFloors.Cells)
                {
                    if (storeys.Any(s => s.Cells.Contains(cell)))
                    {
                        throw Fail("$.galleries.floors", $"0x{cell:X8} is on a storey (a cell that holds a gallery's floor is no body's cell)");
                    }
                }

                galleries = new GroundGalleries(galleryCells, galleryFloors, gallerySection.Text("source"));
                gallerySection.End();
            }


            var homeSection = top.Object("home")!;
            var home = new GroundHome(homeSection.Text("town"), homeSection.Hex("lifestone_cell"), homeSection.Point("lifestone_point"));
            homeSection.End();

            var vendors = top.List("vendors", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var v = new GroundVendor(
                    s.Text("name"),
                    (uint)s.Int("wcid", 1, int.MaxValue),
                    s.Hex("guid"),
                    s.Hex("cell"),
                    s.Point("point"),
                    s.Hex("stop_cell"),
                    s.OptionalPoint("stop_point"),
                    s.Hex("buys_item_types"),
                    s.List("sells", (we, wp) =>
                    {
                        var w = Object(we, wp, nullable: false)!;
                        var ware = new VendorWare((uint)w.Int("wcid", 1, int.MaxValue), w.Text("name"), w.Int("price", 1, int.MaxValue));
                        w.End();
                        return ware;
                    }))
                {
                    BuyPrice = s.OptionalNumber("buy_price", 0.01, 2) ?? GroundVendor.DefaultBuyPrice, // M5 7 F29 (D96): what the vendor pays of an item's Value (ACE's BuyPrice, float property 37)
                };
                s.End();
                return v;
            });
            Unique(vendors.Select(v => v.Wcid), "$.vendors", "wcid");

            var plan = top.List("spell_plan", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var row = new SpellPlanRow(s.Int("min_level", 1, 10000), s.Int("max_level", 1, 10000), s.Text("family"), s.Int("spell_level", 1, 8));
                if (row.MinLevel > row.MaxLevel)
                {
                    throw Fail($"{p}.min_level", $"{row.MinLevel} is above max_level {row.MaxLevel}");
                }

                s.End();
                return row;
            });
            for (var i = 0; i < plan.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (plan[i].MinLevel <= plan[j].MaxLevel && plan[j].MinLevel <= plan[i].MaxLevel)
                    {
                        throw Fail($"$.spell_plan[{i}]", $"levels {plan[i].MinLevel}-{plan[i].MaxLevel} overlap $.spell_plan[{j}]'s {plan[j].MinLevel}-{plan[j].MaxLevel}");
                    }
                }
            }

            var components = top.List("components", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var c = new GroundComponent((uint)s.Int("wcid", 1, int.MaxValue), s.Text("name"), s.Int("spell_level", 1, 8), s.Int("per_cast", 1, 8));
                s.End();
                return c;
            });

            var suppliesSection = top.Object("supplies")!;
            var stock = suppliesSection.List("stock", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var line = new StockLine((uint)s.Int("wcid", 1, int.MaxValue), s.Text("name"), s.Int("keep", 1, MaxCount), s.Int("trip_under", 0, MaxCount), s.Int("stop_under", 0, MaxCount));
                if (line.TripUnder > line.Keep)
                {
                    throw Fail($"{p}.trip_under", $"{line.TripUnder} is above keep {line.Keep} (keep >= trip_under >= stop_under >= 0)");
                }

                if (line.StopUnder > line.TripUnder)
                {
                    throw Fail($"{p}.stop_under", $"{line.StopUnder} is above trip_under {line.TripUnder} (keep >= trip_under >= stop_under >= 0)");
                }

                s.End();
                return line;
            });
            Unique(stock.Select(l => l.Wcid), "$.supplies.stock", "wcid");
            var kit = suppliesSection.List("kit", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var wcid = (uint)s.Int("wcid", 1, int.MaxValue);
                var name = s.Text("name");
                var slot = s.Text("slot") switch
                {
                    "worn" => KitSlot.Worn,
                    "wielded" => KitSlot.Wielded,
                    "pack" => KitSlot.Pack,
                    var other => throw Fail($"{p}.slot", $"'{other}' is out of range (worn, wielded or pack)"),
                };
                var count = s.OptionalInt("count", 1, Lifestone.Client.Session.Mage.GearKit.ContainerCapacity);
                if (count > 1 && slot != KitSlot.Pack)
                {
                    throw Fail($"{p}.count", $"{count} is out of range (only a pack line holds more than one)");
                }

                var line = new KitLine(wcid, name, slot, Math.Max(1, count));
                s.End();
                return line;
            });
            Unique(kit.Select(l => l.Wcid), "$.supplies.kit", "wcid");
            if (kit.Where(l => l.Slot == KitSlot.Pack).Sum(l => l.Count) > Lifestone.Client.Session.Mage.GearKit.ContainerCapacity)
            {
                throw Fail("$.supplies.kit", $"the pack lines hold more than the {Lifestone.Client.Session.Mage.GearKit.ContainerCapacity} containers a player can carry");
            }

            var burdenTripPercent = suppliesSection.Int("burden_trip_percent", 1, 200) /* M6 0 lead: up to ACE's 200%, where the burden mod stops the run (EncumbranceSystem.GetBurdenMod); a climber that loots nothing trips above 100% */;
            var purseSection = suppliesSection.Object("purse")!;
            var purse = new GroundPurse(
                purseSection.Int("coin", 0, MaxPyreals),
                purseSection.List("notes", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var note = new PurseNote((uint)s.Int("wcid", 1, int.MaxValue), s.Text("name"), s.Int("value", 1, MaxPyreals), s.Int("count", 1, MaxCount));
                    s.End();
                    return note;
                }),
                purseSection.OptionalInt("min_note", 1, MaxPyreals) is var minNote and > 0 ? minNote : GroundPurse.DefaultMinNote, // M5 7 D136 follow-up: the smallest note the trips buy
                purseSection.OptionalInt("trip_spend_percent", 1, 100) is var spend and > 0 ? spend : 100) // M6 7 V: the trip's stock budget
            {
                LearnTripCoin = purseSection.OptionalInt("learn_trip_coin", 1, MaxPyreals) is var learnCoin and > 0 ? learnCoin : GroundPurse.DefaultLearnTripCoin, // M6 7 AT: a learn trip waits for two scrolls' coin or this
            };
            Unique(purse.Notes.Select(n => n.Wcid), "$.supplies.purse.notes", "wcid");
            purseSection.End();
            suppliesSection.End();
            var supplies = new GroundSupplies(stock, kit, burdenTripPercent, purse);

            var holds = top.List("hold_spots", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var h = new HoldSpot(OwnCell(s.Take("cell"), $"{p}.cell"), s.Point("point"));
                s.End();
                return h;
            });

            var respawnSection = top.Object("respawn")!;
            var generators = respawnSection.List("generators", (e, p) =>
            {
                var s = Object(e, p, nullable: false)!;
                var g = new GeneratorModel(s.Hex("guid"), OwnCell(s.Take("cell"), $"{p}.cell"), s.Number("delay_s", 0, 1e7), s.Number("tick_s", 0, 1e7));
                s.End();
                return g;
            });
            Unique(generators.Select(g => g.Guid), "$.respawn.generators", "guid");
            var respawn = new RespawnModel(
                generators,
                respawnSection.List("slots", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var generator = s.Hex("generator");
                    if (!generators.Any(g => g.Guid == generator))
                    {
                        throw Fail($"{p}.generator", $"0x{generator:X8} is not one of $.respawn.generators");
                    }

                    var slot = new RespawnSlot(generator, OwnCell(s.Take("cell"), $"{p}.cell"), (uint)s.Int("wcid", 1, int.MaxValue));
                    s.End();
                    return slot;
                }));
            respawnSection.End();

            int? fightRetreat = null;
            int? fightRingMin = null;
            Vitals.GroundPullRest? pullRest = null;
            if (top.Has("fight"))
            {
                var fightSection = top.Object("fight")!;
                fightRetreat = fightSection.Has("retreat_health_percent") ? fightSection.Int("retreat_health_percent", 0, 100) : null; // M6 7 AN: optional: the outskirts' section has no opinion on it
                fightRingMin = fightSection.OptionalInt("ring_min_targets", 1, 9) is var ringMin and > 0 ? ringMin : null; // M5 7 F22: the war picker's floor for Cassius' Ring of Fire
                pullRest = PullRestOf(fightSection); // M6 7 AB: the rest before the next pull (absent: no rest rule)
                fightSection.OptionalText("source"); // the note that says where the value comes from (not a section of the sources map: the section is optional)
                fightSection.End();
            }

            GroundHunt? hunt = null;
            if (top.Has("hunt"))
            {
                var huntSection = top.Object("hunt")!; // M6 7 AR: what the plain hunt leaves alone
                var keepOut = Cells(huntSection.Take("keep_out"), "$.hunt.keep_out");
                foreach (var cell in keepOut.Cells)
                {
                    if (!storeys.Any(s => s.Cells.Contains(cell)))
                    {
                        throw Fail("$.hunt.keep_out", $"0x{cell:X8} is on no storey (a keep-out that keeps nothing out)");
                    }
                }

                hunt = new GroundHunt(keepOut, huntSection.Int("keep_out_under_level", 1, 1000), huntSection.List("pulls", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var pull = new GroundPull((uint)s.Int("item", 1, int.MaxValue), s.Text("name"), [.. Cells(s.Take("cells"), $"{p}.cells").Cells]);
                    if (s.Has("stand"))
                    {
                        var d = s.Object("stand")!; // M6 7 AX: the pull's data
                        var stand = new PullStand(d.Text("quest"), d.Int("from_level", 1, 1000), d.Int("to_level", 1, 1000), d.Hex("cell"), d.Point("point"), (uint)d.Int("holder_wcid", 1, int.MaxValue), d.Hex("holder_cell"), d.Number("wait_s", 1, 3600), d.Int("tries", 1, 9));
                        d.OptionalText("source");
                        d.End();
                        if (!keepOut.Contains(stand.HolderCell) || keepOut.Contains(stand.Cell))
                        {
                            throw Fail($"{p}.stand", "the holder must stand in the keep-out and the stand point outside it");
                        }

                        pull = pull with { Stand = stand };
                    }

                    s.End();
                    return pull;
                }), huntSection.Has("drop_back_keep_out") ? Cells(huntSection.Take("drop_back_keep_out"), "$.hunt.drop_back_keep_out") : CellSet.Empty);
                foreach (var cell in hunt.KeepOut.Cells)
                {
                    if (hunt.HasDropBack && !hunt.DropBack.Contains(cell))
                    {
                        throw Fail("$.hunt.drop_back_keep_out", $"leaves 0x{cell:X8} of keep_out (the drop-back's list is all she leaves alone)");
                    }
                }

                huntSection.OptionalText("source");
                huntSection.End();
            }

            GroundCorpseFetch? corpseFetch = null;
            if (top.Has("corpse_fetch"))
            {
                var fetchSection = top.Object("corpse_fetch")!; // M6 7 AY: where a corpse is safe to reach for its coin
                var anywhere = fetchSection.Bool("anywhere");
                var fetchCells = fetchSection.Has("cells") ? Cells(fetchSection.Take("cells"), "$.corpse_fetch.cells") : CellSet.Empty;
                if (anywhere == (fetchCells.Cells.Count > 0))
                {
                    throw Fail("$.corpse_fetch", "names neither or both of anywhere and cells");
                }

                foreach (var cell in fetchCells.Cells)
                {
                    if (hunt is { } kept && (kept.KeepOut.Contains(cell) || kept.DropBack.Contains(cell)))
                    {
                        throw Fail("$.corpse_fetch.cells", $"0x{cell:X8} is inside the keep-out (a safe cell is outside it)");
                    }
                }

                corpseFetch = new GroundCorpseFetch(anywhere, fetchCells);
                fetchSection.OptionalText("source");
                fetchSection.End();
            }

            GroundApproach? approach = null;
            if (top.Has("approach"))
            {
                // M6 14 entry recharge: the outdoor approach to the entry and the hostiles that stand there, facts with their source (never a fight number)
                var approachSection = top.Object("approach")!;
                var approachLandblocks = approachSection.List("landblocks", (e, p) =>
                {
                    var lb = Hex(e, p);
                    return lb <= 0xFFFF ? (ushort)lb : throw Fail(p, $"0x{lb:X} is out of range (a landblock is 0x0000 to 0xFFFF)");
                });
                if (approachLandblocks.Count == 0)
                {
                    throw Fail("$.approach.landblocks", "is empty (an approach lies in at least one landblock)");
                }

                var hostiles = approachSection.List("hostiles", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var hostile = new GroundApproachHostile((uint)s.Int("wcid", 1, int.MaxValue), s.Text("name"));
                    var moved = s.FromBestiary("health", "magic_defence", "resist", "awareness_m", "xp", "pace", "resist_health_drain"); // M6 16 Q12: the creature's numbers by reference
                    var table = moved.Count > 0 ? TableOf(hostile.Wcid, p) : default;
                    string[] facts = ["level", "health", "damage_types", "d_val", "d_var", "strength", "pack", "attack_source"];
                    if (facts.Any(s.Has) || moved.Contains("health"))
                    {
                        // M6 14 retreat attackers: the creature's facts, all or none, with the source that names the SELECT (never a fight number)
                        hostile = hostile with
                        {
                            Level = s.Int("level", 1, 10_000),
                            MaxHealth = moved.Contains("health") ? Checked(table.MaxHealth, 1, 1_000_000, $"{p}.health") : s.Int("health", 1, 1_000_000),
                            DamageTypes = s.Hex("damage_types") is var types and > 0 and <= 0x7F ? types : throw Fail($"{p}.damage_types", "is out of range (the DamageType bits 0x1 to 0x40)"),
                            DVal = s.Int("d_val", 1, 100_000),
                            DVar = s.Number("d_var", 0, 1),
                            Strength = s.Int("strength", 1, 10_000),
                            Pack = s.Int("pack", 1, 50),
                        };
                        _ = s.Text("attack_source");
                    }

                    string[] crossing = ["magic_defence", "resist", "awareness_m", "per_roll", "xp"];
                    if (crossing.Any(s.Has) || moved.Overlaps(["magic_defence", "resist", "awareness_m", "xp"]))
                    {
                        // M6 14 approach cost: the crossing's facts, all or none, from the pinned world SQL (the approach's cost_source names the SELECTs); never a fight number
                        if (hostile.MaxHealth is null)
                        {
                            throw Fail($"{p}.magic_defence", "the crossing's facts need the creature's attack facts (its health)");
                        }

                        var resist = new Dictionary<string, double>(StringComparer.Ordinal);
                        if (moved.Contains("resist"))
                        {
                            foreach (var element in Mage.SpellPlan.BoltLineNames.Keys)
                            {
                                resist[element] = Checked(Creatures.Bestiary.ResistOf(hostile.Wcid, element), 0, 10, $"{p}.resist.{element}");
                            }
                        }
                        else
                        {
                            var resistSection = s.Object("resist")!;
                            foreach (var element in Mage.SpellPlan.BoltLineNames.Keys)
                            {
                                resist[element] = resistSection.Number(element, 0, 10);
                            }

                            resistSection.End();
                        }

                        hostile = hostile with
                        {
                            MagicDefence = moved.Contains("magic_defence") ? Checked(table.MagicDefence, 0, 10_000, $"{p}.magic_defence") : s.Int("magic_defence", 0, 10_000),
                            Resist = resist,
                            AwarenessMetres = moved.Contains("awareness_m") ? Checked(table.AwarenessMetres, 0.1, 500, $"{p}.awareness_m") : s.Number("awareness_m", 0.1, 500),
                            PerRoll = s.Number("per_roll", 0, 100),
                            Xp = moved.Contains("xp") ? Checked(table.Xp, 0, int.MaxValue, $"{p}.xp") : s.Int("xp", 0, int.MaxValue),
                        };
                    }

                    if (moved.Contains("pace"))
                    {
                        // M6 16 Q12: the pace by reference: the Run skill ACE computes from the weenie's Run row and Quickness, the motion table and the scale, from the bestiary's tables
                        var run = table.RunSkill ?? throw Fail($"{p}.from_bestiary", $"weenie {hostile.Wcid} has no motion table in the bestiary's tables (its pace is not known)");
                        hostile = hostile with
                        {
                            RunSkill = Checked(run, 0, 10_000, $"{p}.pace.run_skill"),
                            MotionTable = table.MotionTable is >= 0x09000000 and <= 0x09FFFFFF ? table.MotionTable : throw Fail($"{p}.pace.motion_table", "is not a motion table (0x09000000 to 0x09FFFFFF)"),
                            Scale = Checked(table.Scale, 0.05, 20, $"{p}.pace.scale"),
                        };
                    }
                    else if (s.Has("pace"))
                    {
                        // M6 14 run past (D137): its Run skill, motion table and scale, all three, with the SELECTs named (its speed is derived at run time from the dats)
                        var pace = s.Object("pace")!;
                        hostile = hostile with
                        {
                            RunSkill = pace.Int("run_skill", 0, 10_000),
                            MotionTable = pace.Hex("motion_table") is var motionTable and >= 0x09000000 and <= 0x09FFFFFF ? motionTable : throw Fail($"{p}.pace.motion_table", "is not a motion table (0x09000000 to 0x09FFFFFF)"),
                            Scale = pace.Number("scale", 0.05, 20),
                        };
                        _ = pace.Text("source");
                        pace.End();
                    }

                    if (s.Has("ranged"))
                    {
                        // M6 14 run past: its thrown weapon's damage, type, the thrower's Strength and the range, with the SELECTs named
                        var ranged = s.Object("ranged")!;
                        hostile = hostile with
                        {
                            Ranged = new GroundRanged(
                                ranged.Int("d_val", 1, 100_000),
                                ranged.Number("d_var", 0, 1),
                                ranged.Hex("damage_types") is var bits and > 0 and <= 0x7F ? bits : throw Fail($"{p}.ranged.damage_types", "is out of range (the DamageType bits 0x1 to 0x40)"),
                                ranged.Int("strength", 1, 10_000),
                                ranged.Number("range_m", 1, 200))
                            {
                                Share = ranged.Has("share") ? ranged.Number("share", 0, 1) : 1.0,
                                Spell = ranged.Has("spell") && ranged.Bool("spell"),
                            },
                        };
                        _ = ranged.Text("source");
                        ranged.End();
                    }

                    if (s.OptionalNumber("resist_health_drain", 0, 10) is { } drainResist)
                    {
                        hostile = hostile with { ResistHealthDrain = drainResist }; // M6 14 drain resistance: ResistHealthDrain (float 125), named in the approach's source
                    }
                    else if (moved.Contains("resist_health_drain"))
                    {
                        hostile = hostile with { ResistHealthDrain = Checked(table.ResistHealthDrain, 0, 10, $"{p}.resist_health_drain") };
                    }

                    s.End();
                    return hostile;
                });
                if (hostiles.Count == 0)
                {
                    throw Fail("$.approach.hostiles", "is empty (an approach is recorded for its hostiles)");
                }

                Unique(hostiles.Select(h => h.Wcid), "$.approach.hostiles", "wcid");
                approach = new GroundApproach(approachLandblocks, hostiles, approachSection.Text("source"));
                string[] costFacts = ["encounters", "per_roll", "cost_source"];
                if (costFacts.Any(approachSection.Has))
                {
                    // M6 14 approach cost: the approach's own crossing facts, all or none: its encounter generators and the creatures a roll stands up, with the SELECTs named
                    approach = approach with { Encounters = approachSection.Int("encounters", 0, 100_000), PerRoll = approachSection.Number("per_roll", 0, 100), CostSource = approachSection.Text("cost_source") };
                    if (approach.Hostiles.Where(h => h.PerRoll is not null).Sum(h => h.PerRoll!.Value) > approach.PerRoll!.Value + 1e-6)
                    {
                        throw Fail("$.approach.per_roll", $"{approach.PerRoll} is under the listed hostiles' per_roll sum (every kind a roll stands up is counted in it)");
                    }
                }

                approachSection.End();
            }

            IReadOnlyList<GroundAvoidRegion> avoidRegions = [];
            if (top.Has("avoid_regions"))
            {
                // M5 7 P7i (D144): the floor regions the walk's planner avoids (boxes, landblock-local)
                avoidRegions = top.List("avoid_regions", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var lb = s.Hex("landblock");
                    var region = new GroundAvoidRegion(s.Text("name"), lb <= 0xFFFF && _landblocks.Contains((ushort)lb) ? (ushort)lb : throw Fail($"{p}.landblock", $"0x{lb:X} is not one of the ground's landblocks"), s.Point("min"), s.Point("max"));
                    s.OptionalText("source");
                    s.End();
                    return region;
                });
            }

            GroundLoot? lootSetting = null;
            if (top.Has("loot"))
            {
                var lootSection = top.Object("loot")!;
                lootSetting = new GroundLoot(lootSection.Number("min_value_per_burden", 0, 100_000)) // M5 7 F28 (D95): the keep floor, in pyreals of Value per burden unit
                {
                    // M5 7 F29 (D96): the payout floor, the per-item burden cap, the sell-trip cadence and the load line; 0 (absent) is off for each
                    MinPayout = lootSection.OptionalInt("min_payout", 0, 100_000_000),
                    MaxItemBurden = lootSection.OptionalInt("max_item_burden", 0, 100_000),
                    TripEveryBuffCycles = lootSection.OptionalInt("trip_every_buff_cycles", 0, 1000),
                    MinFreeSlots = lootSection.OptionalInt("min_free_slots", 0, 1000),
                    LoadLinePercent = lootSection.OptionalInt("load_line_percent", 0, 200), // M6 7 AR: past the capacity is a choice: ACE slows her, the trip sells it
                    SaleTripValue = lootSection.OptionalInt("sale_trip_value", 1, 100_000_000) is var saleValue and > 0 ? saleValue : GroundLoot.DefaultSaleTripValue, // M6 7 AT: the sale trip's line, in Value
                    RadiusMetres = lootSection.OptionalInt("radius_m", 0, 100), // M6 7 AR: the walk to a kill's corpse; 0 keeps --fight-loot-radius-m
                    KeepAside = lootSection.KeepAside(), // M5 7 F30 (the owner): the wands and cantrip pieces kept aside and never sold
                };
                lootSection.OptionalText("source"); // where the value comes from (the section is optional, so it has no entry in the sources map)
                lootSection.End();
            }

            IReadOnlyList<LearnLine> learn = [];
            if (top.Has("learn"))
            {
                // M5 7 P7j (D143): the spells learned by play (a scroll bought from a profile vendor and read on a town trip)
                learn = top.List("learn", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var line = new LearnLine((uint)s.Int("spell", 1, int.MaxValue), s.Text("name"), (uint)s.Int("scroll_wcid", 1, int.MaxValue), (uint)s.Int("vendor_wcid", 1, int.MaxValue));
                    var vendor = vendors.FirstOrDefault(v => v.Wcid == line.VendorWcid) ?? throw Fail($"{p}.vendor_wcid", $"{line.VendorWcid} is not a vendor of $.vendors");
                    if (!vendor.Sells.Any(w => w.Wcid == line.ScrollWcid))
                    {
                        throw Fail($"{p}.scroll_wcid", $"{line.ScrollWcid} is not in the sells of {vendor.Name} (the scroll's one vendor and price)");
                    }

                    s.OptionalText("source");
                    s.End();
                    return line;
                });
                Unique(learn.Select(l => l.SpellId), "$.learn", "spell");
            }

            IReadOnlyList<GroundWant> wantsKnown = [];
            if (top.Has("wants_known"))
            {
                wantsKnown = top.List("wants_known", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var want = new GroundWant((uint)s.Int("spell", 1, int.MaxValue), s.Text("name"), s.Text("why"));
                    s.End();
                    return want;
                });
                Unique(wantsKnown.Select(w => w.SpellId), "$.wants_known", "spell");
            }

            var tour = top.List("tour", (e, p) => OwnCell(e, p));

            var outdoorSection = top.Object("outdoor", nullable: true);
            OutdoorGround? outdoor = null;
            if (outdoorSection is not null)
            {
                outdoor = new OutdoorGround(
                    outdoorSection.List("cells", (e, p) =>
                    {
                        var s = Object(e, p, nullable: false)!;
                        var terrain = s.Text("terrain") switch
                        {
                            "unread" => GroundTerrain.Unread,
                            "dry" => GroundTerrain.Dry,
                            "water" => GroundTerrain.Water,
                            "steep" => GroundTerrain.Steep,
                            var other => throw Fail($"{p}.terrain", $"'{other}' is out of range (unread, dry, water or steep)"),
                        };
                        var c = new OutdoorCell(OwnCell(s.Take("cell"), $"{p}.cell"), s.Number("north", -102, 102), s.Number("east", -102, 102), terrain, s.Point("spot"));
                        s.End();
                        return c;
                    }),
                    outdoorSection.List("creatures", (e, p) =>
                    {
                        var s = Object(e, p, nullable: false)!;
                        var wcid = (uint)s.Int("wcid", 1, int.MaxValue);
                        var name = s.Text("name");
                        var level = s.Int("level", 1, 10000);
                        var moved = s.FromBestiary("max_health", "resist_health_drain"); // M6 16 Q12: the creature's numbers by reference
                        var table = moved.Count > 0 ? TableOf(wcid, p) : default;
                        var c = new GroundCreature(wcid, name, level)
                        {
                            MaxHealth = moved.Contains("max_health") ? Checked(table.MaxHealth, 1, 1_000_000, $"{p}.max_health") : s.OptionalInt("max_health", 1, 1_000_000),
                            SpawnMass = s.Number("spawn_mass", 0, 1),
                            ResistHealthDrain = moved.Contains("resist_health_drain") ? Checked(table.ResistHealthDrain, 0, 10, $"{p}.resist_health_drain") : s.OptionalNumber("resist_health_drain", 0, 10) ?? 1.0,
                        };
                        s.End();
                        return c;
                    }),
                    (float)outdoorSection.Number("acquire_radius_m", 0.001, 10000),
                    outdoorSection.Int("cell_cap", 1, 10000),
                    outdoorSection.Number("expected_yield_per_spot", 0, 10000));
                outdoorSection.End();
            }

            IReadOnlyList<TownStockLine> townStock = [];
            if (top.Has("town_stock"))
            {
                // M6 14 T: the wares bought in another town on the way out; no stock line and no home vendor's ware
                townStock = top.List("town_stock", (e, p) =>
                {
                    var s = Object(e, p, nullable: false)!;
                    var wcid = (uint)s.Int("wcid", 1, int.MaxValue);
                    var wareName = s.Text("name");
                    var keep = s.Int("keep", 1, MaxCount);
                    var tripUnder = s.Int("trip_under", 1, MaxCount);
                    if (tripUnder > keep)
                    {
                        throw Fail($"{p}.trip_under", $"{tripUnder} is above keep {keep} (keep >= trip_under >= 1)");
                    }

                    var price = s.Int("price", 1, MaxPyreals);
                    var burden = s.Int("burden", 0, 100_000);
                    var town = s.Text("town");
                    if (string.Equals(town, home.Town, StringComparison.Ordinal))
                    {
                        throw Fail($"{p}.town", $"{town} is the home town: a ware sold at home is a line of supplies.stock");
                    }

                    var v = s.Object("vendor")!;
                    var cell = v.Hex("cell");
                    var vendor = new GroundVendor(v.Text("name"), (uint)v.Int("wcid", 1, int.MaxValue), v.Hex("guid"), cell, v.Point("point"), cell, null, 0, [new VendorWare(wcid, wareName, price)]);
                    v.End();
                    s.OptionalText("source");
                    s.End();
                    if (supplies.StockOf(wcid) is not null)
                    {
                        throw Fail($"{p}.wcid", $"{wcid} is a line of supplies.stock too (a ware is bought at home or in a town, not both)");
                    }

                    if (vendors.Any(h => h.Wcid == vendor.Wcid))
                    {
                        throw Fail($"{p}.vendor.wcid", $"{vendor.Wcid} is a home vendor");
                    }

                    return new TownStockLine(wcid, wareName, keep, tripUnder, price, burden, town, vendor);
                });
                Unique(townStock.Select(t => t.Wcid), "$.town_stock", "wcid");
            }

            top.End();

            if (kind == GroundKind.Dungeon && storeys.Count == 0)
            {
                throw Fail("$.storeys", "is empty (a dungeon has at least one storey)");
            }

            if (kind == GroundKind.Dungeon && outdoor is not null)
            {
                throw Fail("$.outdoor", "is not null for a dungeon");
            }

            if (kind == GroundKind.Outdoor && outdoor is null)
            {
                throw Fail("$.outdoor", "is null for an outdoor ground");
            }

            CheckSupplies(supplies, vendors, plan, components, learn, townStock);

            var ground = new HuntingGround(
                key, name, kind, sources.AsReadOnly(), _landblocks, arrival, walk, storeys, noGo, targets, levelBand, portal, home, vendors, plan, components, holds, respawn, tour, outdoor)
            {
                Supplies = supplies,
                FightRetreatHealthPercent = fightRetreat,
                PullRest = pullRest,
                Hunt = hunt,
                CorpseFetch = corpseFetch,
                Approach = approach,
                TownStock = BuffBotFee.With(townStock, supplies, home.Town, vendors, ScrivenerCatalog.Default), // D138: the bot's fee, a Platinum Scarab a stop from level 60, on every ground
                FightRingMinTargets = fightRingMin,
                Loot = lootSetting,
                AvoidRegions = avoidRegions,
                Learn = learn,
                WantsKnown = wantsKnown,
                Entry = entry,
                Exit = exit,
                Jumps = jumps,
                Galleries = galleries,
                Plates = plates,
                Scriveners = [.. ScrivenerCatalog.Default.VendorsIn(home.Town).Where(c => !vendors.Any(v => v.Wcid == c.Wcid))], // M6 1 c (D40): the home town's catalog scriveners join the vendor view (additive; a profile's own wins)
            };
            for (var i = 0; i < jumps.Count; i++)
            {
                foreach (var (side, sideName) in new[] { (jumps[i].A, "sides[0]"), (jumps[i].B, "sides[1]") })
                {
                    if (!ground.IsWalkable(side.Cell))
                    {
                        throw Fail($"$.jumps[{i}].{sideName}.cell", $"0x{side.Cell:X8} is not walkable (in no storey, or no-go): a jump lands in a room the walk stands in");
                    }
                }
            }

            for (var i = 0; i < tour.Count; i++)
            {
                if (kind == GroundKind.Dungeon && !ground.IsWalkable(tour[i]))
                {
                    throw Fail($"$.tour[{i}]", $"0x{tour[i]:X8} is not walkable (in no storey, or no-go)");
                }
            }

            return ground;
        }

        /// <summary>
        /// M6 14 pits: one <c>jumps</c> entry: <c>name</c>, <c>pit_cell</c> (the floorless cell), <c>gap_m</c> (brink to brink), <c>need_m</c> (the level-ground flight a jump needs, at least the gap), two <c>sides</c>
        /// (each <c>cell</c>, <c>brink</c>, <c>run_up</c> and <c>landing</c>, landblock-local points) and the <c>source</c>.
        /// </summary>
        private Lifestone.Client.Movement.Routes.PitJump Jump(JsonElement e, string path)
        {
            var s = Object(e, path, nullable: false)!;
            var name = s.Text("name");
            var pit = OwnCell(s.Take("pit_cell"), $"{path}.pit_cell");
            var gap = (float)s.Number("gap_m", 0.1, 100);
            var need = (float)s.Number("need_m", 0.1, 100);
            if (need < gap)
            {
                throw Fail($"{path}.need_m", $"{need} is under gap_m {gap} (a jump lands past the far brink)");
            }

            var sides = s.List("sides", (se, sp) =>
            {
                var side = Object(se, sp, nullable: false)!;
                var made = new Lifestone.Client.Movement.Routes.PitJumpSide(OwnCell(side.Take("cell"), $"{sp}.cell"), side.Point("brink"), side.Point("run_up"), side.Point("landing"));
                side.End();
                return made;
            });
            if (sides.Count != 2)
            {
                throw Fail($"{path}.sides", $"has {sides.Count} sides (a pit has two)");
            }

            s.Text("source");
            s.End();
            return new Lifestone.Client.Movement.Routes.PitJump(name, pit, gap, need, sides[0], sides[1]);
        }

        /// <summary>
        /// M6 14 holding plates: the <c>plates</c> section: <c>wcid</c>, <c>name</c>, <c>shape_m</c> and <c>top_m</c> (the Setup's shape seen from above and the height her feet clear it at), <c>spells</c> (each <c>id</c> and <c>name</c>),
        /// <c>points</c> (each <c>guid</c>, <c>cell</c> and landblock-local <c>point</c>) and the <c>source</c>.
        /// </summary>
        private GroundPlates Plates(Section s)
        {
            var wcid = (uint)s.Int("wcid", 1, int.MaxValue);
            var name = s.Text("name");
            var shape = (float)s.Number("shape_m", 0.01, 10);
            var top = (float)s.Number("top_m", 0, 10);
            var spells = s.List("spells", (e, p) =>
            {
                var spell = Object(e, p, nullable: false)!;
                var made = new GroundPlateSpell((uint)spell.Int("id", 1, int.MaxValue), spell.Text("name"));
                spell.End();
                return made;
            });
            var points = s.List("points", (e, p) =>
            {
                var plate = Object(e, p, nullable: false)!;
                var made = new GroundPlate(plate.Hex("guid"), OwnCell(plate.Take("cell"), $"{p}.cell"), plate.Point("point"));
                plate.End();
                return made;
            });
            if (points.Count == 0)
            {
                throw Fail("$.plates.points", "is empty (a plates section is recorded for its plates)");
            }

            Unique(points.Select(p => p.Guid), "$.plates.points", "guid");
            var source = s.Text("source");
            s.End();
            return new GroundPlates(wcid, name, shape, top, spells, points, source);
        }

        /// <summary>M6 3 a1: a target's optional <c>attributes</c> block (the six attributes, each required inside it), or null.</summary>
        private static GroundAttributes? TargetAttributes(Section target)
        {
            if (!target.Has("attributes"))
            {
                return null;
            }

            var a = target.Object("attributes")!;
            var attributes = new GroundAttributes(
                a.Int("strength", 1, 10_000), a.Int("endurance", 1, 10_000), a.Int("coordination", 1, 10_000), a.Int("quickness", 1, 10_000), a.Int("focus", 1, 10_000), a.Int("self", 1, 10_000));
            a.End();
            return attributes;
        }

        /// <summary>
        /// M6 3 a1: the <c>entry</c> section: the entry portal's place (wcid, guid, cell, origin, front_cell, front_point), its gate (min_level and max_level, each a number or null), <c>tieable</c> and the <c>source</c>.
        /// A portal that cannot be tied carries no tie record: a profile whose <c>entry.tieable</c> is false and whose <c>portal</c> is not null fails at <c>$.portal</c>; when both are present they name one portal.
        /// </summary>
        private GroundEntry Entry(Section s, GroundPortal? portal)
        {
            var entry = new GroundEntry(
                (uint)s.Int("wcid", 1, int.MaxValue),
                s.Hex("guid"),
                s.Hex("cell"),
                s.Point("origin"),
                s.Hex("front_cell"),
                s.Point("front_point"),
                s.NullableInt("min_level", 1, 10_000),
                s.NullableInt("max_level", 1, 10_000),
                s.Bool("tieable"));
            s.Text("source");
            s.End();
            if (entry is { MinLevel: { } min, MaxLevel: { } max } && min > max)
            {
                throw Fail("$.entry.min_level", $"{min} is above max_level {max}");
            }

            if (!entry.Tieable && portal is not null)
            {
                throw Fail("$.portal", $"is a tie record, but $.entry.tieable is false: the entry portal (wcid {entry.Wcid}) carries NoRecall in its PortalBitmask, so it cannot be tied (ACE Portal.NoTie); write \"portal\": null");
            }

            if (portal is not null && (portal.Wcid != entry.Wcid || portal.Guid != entry.Guid))
            {
                throw Fail("$.entry.guid", $"wcid {entry.Wcid} 0x{entry.Guid:X8} is not $.portal's wcid {portal.Wcid} 0x{portal.Guid:X8} (the entry and the tie record name one portal)");
            }

            return entry;
        }

        /// <summary>
        /// M6 3 a1: the <c>exit</c> section: <c>kind</c> (portal, recall or walk) and the <c>source</c>; a portal exit also has the exit portal's wcid, cell (one of the ground's), origin and destination cell and the optional
        /// <c>from_arrival_m</c> hint; the other kinds have none of these. A dungeon is not left on foot and an outdoor ground has no exit portal.
        /// </summary>
        private GroundExitSection Exit(Section s, GroundKind kind)
        {
            var exitKind = s.Text("kind") switch
            {
                "portal" => GroundExitKind.Portal,
                "recall" => GroundExitKind.Recall,
                "walk" => GroundExitKind.Walk,
                var other => throw Fail("$.exit.kind", $"'{other}' is out of range (portal, recall or walk)"),
            };
            if (exitKind == GroundExitKind.Walk && kind == GroundKind.Dungeon)
            {
                throw Fail("$.exit.kind", "'walk' is an outdoor ground's exit, and this ground is a dungeon (portal or recall)");
            }

            if (exitKind == GroundExitKind.Portal && kind == GroundKind.Outdoor)
            {
                throw Fail("$.exit.kind", "'portal' is a dungeon's exit, and this ground is outdoor (walk or recall)");
            }

            GroundExitSection exit;
            if (exitKind == GroundExitKind.Portal)
            {
                exit = new GroundExitSection(exitKind, (uint)s.Int("wcid", 1, int.MaxValue), OwnCell(s.Take("cell"), "$.exit.cell"), s.Point("origin"), s.Hex("destination"), s.OptionalNumber("from_arrival_m", 0, 100_000));
            }
            else
            {
                foreach (var field in new[] { "wcid", "cell", "origin", "destination", "from_arrival_m" })
                {
                    if (s.Has(field))
                    {
                        throw Fail($"$.exit.{field}", $"is a field of a portal exit only (this exit's kind is {exitKind.ToString().ToLowerInvariant()})");
                    }
                }

                exit = new GroundExitSection(exitKind, 0, 0, null, 0, null);
            }

            s.Text("source");
            s.End();
            return exit;
        }

        /// <summary>
        /// The supplies section against the rest of the profile (D48): a vendor's ware is a stock or kit line and has one vendor (one source of a price); a line the vendor lists has the vendor's name for it; and every
        /// row of the spell plan has components at its spell level, each a stock line or (M6 item 14, m6-goldscarab) a line of the town stock (<c>TODO(ground-plan-gap)</c> when not).
        /// </summary>
        private void CheckSupplies(GroundSupplies supplies, IReadOnlyList<GroundVendor> vendors, IReadOnlyList<SpellPlanRow> plan, IReadOnlyList<GroundComponent> components, IReadOnlyList<LearnLine> learn, IReadOnlyList<TownStockLine> townStock)
        {
            var sold = new Dictionary<uint, (int Vendor, int Ware)>();
            for (var i = 0; i < vendors.Count; i++)
            {
                for (var j = 0; j < vendors[i].Sells.Count; j++)
                {
                    var ware = vendors[i].Sells[j];
                    if (sold.TryGetValue(ware.Wcid, out var first))
                    {
                        throw Fail($"$.vendors[{i}].sells[{j}].wcid", $"{ware.Wcid} ({ware.Name}) is also sold by $.vendors[{first.Vendor}] (a ware has one home vendor and one price)");
                    }

                    sold[ware.Wcid] = (i, j);
                    // M5 7 G1: a purse note may be sold too, the sell trip buys it; M5 7 P7j: a learn line's scroll; M6 2 b: a gear floor ware (a caster, a robe, a Mana Charge: Mage.GearFloor; M6 7 AH: hand armour, GearFloor.IsGauntletWare) or one of ACE's trade notes (Scenarios.Journey.PurseNotes)
                    if (supplies.StockOf(ware.Wcid) is null && supplies.KitOf(ware.Wcid) is null && !supplies.Purse.Notes.Any(n => n.Wcid == ware.Wcid) && !learn.Any(l => l.ScrollWcid == ware.Wcid)
                        && !Lifestone.Client.Session.Mage.GearFloor.IsFloorWare(ware.Wcid) && !Lifestone.Client.Session.Mage.GearFloor.IsGauntletWare(ware.Wcid) && !Lifestone.Client.Session.Scenarios.Journey.PurseNotes.All.Any(n => n.Wcid == ware.Wcid)
                        && ware.Wcid != Lifestone.Client.Session.Fighter.FighterKit.SidePackWcid /* M6 8 D: the side pack a short layout buys (ResupplyPlanner.PackBuy), no stock or kit line of the ground */
                        && !Lifestone.Data.Magic.ComponentWeenies.TryGetByWcid(ware.Wcid, out _) /* M6 sprint (2026-11-18): a spell component: the learn step's shopping list buys it by the vendor's row (ResupplyStation.ScrollKit), with or without a stock line */)
                    {
                        throw Fail($"$.vendors[{i}].sells[{j}]", $"wcid {ware.Wcid} ({ware.Name}) is not a line of $.supplies.stock, $.supplies.kit, a note of $.supplies.purse or a scroll of $.learn, nor a gear floor ware, a trade note or a spell component (a vendor lists only the wares the supplies need)");
                    }
                }
            }

            for (var i = 0; i < supplies.Stock.Count; i++)
            {
                NameAgrees(sold, vendors, supplies.Stock[i].Wcid, supplies.Stock[i].Name, $"$.supplies.stock[{i}].name");
            }

            for (var i = 0; i < supplies.Kit.Count; i++)
            {
                NameAgrees(sold, vendors, supplies.Kit[i].Wcid, supplies.Kit[i].Name, $"$.supplies.kit[{i}].name");
            }

            for (var i = 0; i < plan.Count; i++)
            {
                var burned = components.Where(c => c.SpellLevel == plan[i].SpellLevel).ToList();
                if (burned.Count == 0)
                {
                    throw Fail($"$.spell_plan[{i}]", $"TODO(ground-plan-gap): spell level {plan[i].SpellLevel} of the row for levels {plan[i].MinLevel}-{plan[i].MaxLevel} ({plan[i].Family}) has no component in $.components, so no supply line can cover it");
                }

                foreach (var c in burned)
                {
                    // M6 item 14 (m6-goldscarab): a component bought in another town (a line of $.town_stock: the Gold Scarab at Uziz) covers the plan's level too; the pack may hold none, and the level floor then casts the level under it
                    if (supplies.StockOf(c.Wcid) is null && !townStock.Any(t => t.Wcid == c.Wcid))
                    {
                        throw Fail($"$.spell_plan[{i}]", $"TODO(ground-plan-gap): spell level {plan[i].SpellLevel} of the row for levels {plan[i].MinLevel}-{plan[i].MaxLevel} ({plan[i].Family}) burns component {c.Wcid} ({c.Name}), which is not a line of $.supplies.stock or of $.town_stock");
                    }
                }
            }
        }

        private void NameAgrees(Dictionary<uint, (int Vendor, int Ware)> sold, IReadOnlyList<GroundVendor> vendors, uint wcid, string name, string path)
        {
            if (sold.TryGetValue(wcid, out var at) && !string.Equals(vendors[at.Vendor].Sells[at.Ware].Name, name, StringComparison.Ordinal))
            {
                throw Fail(path, $"'{name}' is not the name '{vendors[at.Vendor].Sells[at.Ware].Name}' that $.vendors[{at.Vendor}].sells[{at.Ware}] gives wcid {wcid} (one source of a name)");
            }
        }

        public Section? Object(JsonElement e, string path, bool nullable)
        {
            if (e.ValueKind == JsonValueKind.Null && nullable)
            {
                return null;
            }

            return e.ValueKind == JsonValueKind.Object ? new Section(this, e, path) : throw Fail(path, $"is {e.ValueKind}, not an object");
        }

        public IReadOnlyList<T> List<T>(JsonElement e, string path, Func<JsonElement, string, T> item)
        {
            if (e.ValueKind != JsonValueKind.Array)
            {
                throw Fail(path, $"is {e.ValueKind}, not a list");
            }

            var list = new List<T>();
            var i = 0;
            foreach (var x in e.EnumerateArray())
            {
                list.Add(item(x, $"{path}[{i++}]"));
            }

            return list.AsReadOnly();
        }

        public string Text(JsonElement e, string path) =>
            e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s : throw Fail(path, "is not a non-empty string");

        public int Int(JsonElement e, string path, int min, int max)
        {
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var v))
            {
                throw Fail(path, $"is {e.ValueKind}, not an integer");
            }

            return v >= min && v <= max ? v : throw Fail(path, $"{v} is out of range ({min} to {max})");
        }

        public double Number(JsonElement e, string path, double min, double max)
        {
            if (e.ValueKind != JsonValueKind.Number)
            {
                throw Fail(path, $"is {e.ValueKind}, not a number");
            }

            var v = e.GetDouble();
            return v >= min && v <= max ? v : throw Fail(path, string.Create(CultureInfo.InvariantCulture, $"{v} is out of range ({min} to {max})"));
        }

        public uint Hex(JsonElement e, string path)
        {
            var s = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return s is not null && TryHex(s, out var v) ? v : throw Fail(path, $"is not a hex string such as \"0x01050375\"");
        }

        public Vector3 Point(JsonElement e, string path)
        {
            var s = Object(e, path, nullable: false)!;
            var v = new Vector3((float)s.Number("x", -100000, 100000), (float)s.Number("y", -100000, 100000), (float)s.Number("z", -100000, 100000));
            s.End();
            return v;
        }

        /// <summary>A cell id that must lie in the profile's landblocks.</summary>
        public uint OwnCell(JsonElement e, string path)
        {
            var cell = Hex(e, path);
            return _landblocks.Contains((ushort)(cell >> 16)) ? cell : throw Fail(path, $"0x{cell:X8} is outside the profile's landblocks ({string.Join(", ", _landblocks.Select(l => $"0x{l:X4}"))})");
        }

        /// <summary>A cell list: hex cell ids or inclusive ranges <c>"0xAAAAAAAA-0xBBBBBBBB"</c> within one landblock, all in the profile's landblocks.</summary>
        public CellSet Cells(JsonElement e, string path)
        {
            var cells = new List<uint>();
            List(e, path, (x, p) =>
            {
                var s = x.ValueKind == JsonValueKind.String ? x.GetString()! : throw Fail(p, "is not a hex cell id or range");
                var dash = s.IndexOf('-', StringComparison.Ordinal);
                if (dash < 0)
                {
                    cells.Add(OwnCell(x, p));
                    return 0;
                }

                if (!TryHex(s[..dash], out var from) || !TryHex(s[(dash + 1)..], out var to))
                {
                    throw Fail(p, $"'{s}' is not a range such as \"0x01050100-0x01050121\"");
                }

                if ((from >> 16) != (to >> 16) || from > to)
                {
                    throw Fail(p, $"'{s}' is out of range (one landblock, low to high)");
                }

                if (!_landblocks.Contains((ushort)(from >> 16)))
                {
                    throw Fail(p, $"0x{from:X8} is outside the profile's landblocks");
                }

                for (var c = from; c <= to; c++)
                {
                    cells.Add(c);
                }

                return 0;
            });
            if (cells.Distinct().Count() != cells.Count)
            {
                throw Fail(path, "holds a cell twice");
            }

            return CellSet.Of(cells);
        }

        private void Unique(IEnumerable<uint> values, string path, string field)
        {
            var seen = new HashSet<uint>();
            foreach (var v in values)
            {
                if (!seen.Add(v))
                {
                    throw Fail(path, $"holds {field} {v} twice");
                }
            }
        }

        private static bool TryHex(string s, out uint value)
        {
            value = 0;
            return s.Length > 2 && s.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(s.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
    }
}

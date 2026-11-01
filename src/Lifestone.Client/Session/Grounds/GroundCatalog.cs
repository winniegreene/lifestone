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
    public static HuntingGround Get(string key)
    {
        foreach (var g in All)
        {
            if (string.Equals(g.Key, key, StringComparison.Ordinal))
            {
                return g;
            }
        }

        throw new KeyNotFoundException($"no hunting-ground profile '{key}' (known: {string.Join(", ", All.Select(g => g.Key))})");
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
                var role = s.Text("role") switch
                {
                    "target" => GroundRole.Target,
                    "avoid" => GroundRole.Avoid,
                    var other => throw Fail($"{p}.role", $"'{other}' is out of range (target or avoid)"),
                };
                var t = new GroundTarget((uint)s.Int("wcid", 1, int.MaxValue), s.Text("name"), s.Int("level", 1, 10000), role, s.OptionalInt("max_health", 1, 1_000_000), s.OptionalInt("magic_defence", 1, 10_000), s.OptionalText("opener"));
                if (t.Opener is not null && role != GroundRole.Target)
                {
                    throw Fail($"{p}.opener", "an avoided creature has no opener (only a target is fought)");
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

            var burdenTripPercent = suppliesSection.Int("burden_trip_percent", 1, 100);
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
                purseSection.OptionalInt("min_note", 1, MaxPyreals) is var minNote and > 0 ? minNote : GroundPurse.DefaultMinNote); // M5 7 D136 follow-up: the smallest note the trips buy
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
            if (top.Has("fight"))
            {
                var fightSection = top.Object("fight")!;
                fightRetreat = fightSection.Int("retreat_health_percent", 0, 100);
                fightRingMin = fightSection.OptionalInt("ring_min_targets", 1, 9) is var ringMin and > 0 ? ringMin : null; // M5 7 F22: the war picker's floor for Cassius' Ring of Fire
                fightSection.OptionalText("source"); // the note that says where the value comes from (not a section of the sources map: the section is optional)
                fightSection.End();
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
                    LoadLinePercent = lootSection.OptionalInt("load_line_percent", 0, 100),
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
                        var c = new GroundCreature((uint)s.Int("wcid", 1, int.MaxValue), s.Text("name"), s.Int("level", 1, 10000)) { SpawnMass = s.Number("spawn_mass", 0, 1) };
                        s.End();
                        return c;
                    }),
                    (float)outdoorSection.Number("acquire_radius_m", 0.001, 10000),
                    outdoorSection.Int("cell_cap", 1, 10000),
                    outdoorSection.Number("expected_yield_per_spot", 0, 10000));
                outdoorSection.End();
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

            CheckSupplies(supplies, vendors, plan, components, learn);

            var ground = new HuntingGround(
                key, name, kind, sources.AsReadOnly(), _landblocks, arrival, walk, storeys, noGo, targets, levelBand, portal, home, vendors, plan, components, holds, respawn, tour, outdoor)
            {
                Supplies = supplies,
                FightRetreatHealthPercent = fightRetreat,
                FightRingMinTargets = fightRingMin,
                Loot = lootSetting,
                AvoidRegions = avoidRegions,
                Learn = learn,
            };
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
        /// The supplies section against the rest of the profile (D48): a vendor's ware is a stock or kit line and has one vendor (one source of a price); a line the vendor lists has the vendor's name for it; and every
        /// row of the spell plan has components at its spell level, each a stock line (<c>TODO(ground-plan-gap)</c> when not).
        /// </summary>
        private void CheckSupplies(GroundSupplies supplies, IReadOnlyList<GroundVendor> vendors, IReadOnlyList<SpellPlanRow> plan, IReadOnlyList<GroundComponent> components, IReadOnlyList<LearnLine> learn)
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
                    if (supplies.StockOf(ware.Wcid) is null && supplies.KitOf(ware.Wcid) is null && !supplies.Purse.Notes.Any(n => n.Wcid == ware.Wcid) && !learn.Any(l => l.ScrollWcid == ware.Wcid)) // M5 7 G1: a purse note may be sold too, the sell trip buys it; M5 7 P7j: a learn line's scroll
                    {
                        throw Fail($"$.vendors[{i}].sells[{j}]", $"wcid {ware.Wcid} ({ware.Name}) is not a line of $.supplies.stock, $.supplies.kit, a note of $.supplies.purse or a scroll of $.learn (a vendor lists only the wares the supplies need)");
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
                    if (supplies.StockOf(c.Wcid) is null)
                    {
                        throw Fail($"$.spell_plan[{i}]", $"TODO(ground-plan-gap): spell level {plan[i].SpellLevel} of the row for levels {plan[i].MinLevel}-{plan[i].MaxLevel} ({plan[i].Family}) burns component {c.Wcid} ({c.Name}), which is not a line of $.supplies.stock");
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

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// Loads the embedded ladders (M6 DoD 1) under <c>Session/Grounds/ladder/</c> with <see cref="GroundCatalog"/>'s strictness: a missing field, a field the ladder shape does not have, a
/// duplicated field or an out-of-range value fails at load with a <see cref="LadderException"/> naming the file and the field's JSON path. The bands must run in order without a gap or an
/// overlap from level 1 to the file's <c>top_level</c>; a ground rung that is not pending must resolve through <see cref="GroundCatalog"/>; a pending rung names the item that owns its
/// profile (<c>pending_reason</c> "profile due in item N"), and a pending rung whose ground does resolve is an error (a stale pending). Alternates follow the same two rules.
/// </summary>
public static partial class LadderLoader
{
    /// <summary>The war mage's ladder (M6 D17, the island road of the spec §5.2).</summary>
    public const string WarMageKey = "war-mage";

    /// <summary>The logical-name prefix of the embedded ladders (Lifestone.Client.csproj).</summary>
    internal const string ResourcePrefix = "Lifestone.Client.Session.Grounds.ladder.";

    /// <summary>The highest level a ladder may top out at: a sanity bound (retail's and ACE's level cap is 275).</summary>
    private const int MaxTopLevel = 275;

    /// <summary>The highest war spell level (retail's spell levels run I to VIII).</summary>
    private const int MaxSpellLevel = 8;
    private const int MaxTargetMargin = 50;

    /// <summary>The most hits a ladder's <c>health_hits</c> may ask for: a sanity bound.</summary>
    private const double MaxHealthHits = 10;

    /// <summary>The largest pack a rung's <c>health.pack</c> may name: a sanity bound (the largest alert set of the surveys is 9, the Lodge's slots wake at most 8 others).</summary>
    private const int MaxPack = 20;

    /// <summary>The most metres a <c>big_pack.reach_m</c> may name: a sanity bound.</summary>
    private const double MaxBigPackReach = 100;

    /// <summary>The most a <c>big_pack.mana_grows</c> may ask: a sanity bound.</summary>
    private const int MaxManaGrows = 1000;

    /// <summary>The largest <c>d_val</c> or <c>strength</c> a hitter may name: a sanity bound.</summary>
    private const int MaxHitterStat = 10_000;

    /// <summary>The least <c>plan_cast_chance</c> the loader takes: a sanity bound (a chance at or near 0 would plan every level).</summary>
    private const double MinPlanCastChance = 0.01;

    private static readonly ConcurrentDictionary<string, Ladder> Cache = new(StringComparer.Ordinal);

    /// <summary>The war mage's ladder.</summary>
    public static Ladder WarMage => Get(WarMageKey);

    /// <summary>The ladder of <paramref name="key"/>, loaded once.</summary>
    /// <param name="key">The ladder's key (its file name without <c>.json</c>).</param>
    /// <exception cref="KeyNotFoundException">No ladder is embedded under the key.</exception>
    /// <exception cref="LadderException">The ladder does not load.</exception>
    public static Ladder Get(string key) => Cache.GetOrAdd(key, k =>
    {
        var file = k + ".json";
        var text = ResourceText(file) ?? throw new KeyNotFoundException($"no ladder '{k}' is embedded (resource {ResourcePrefix}{file}; Lifestone.Client.csproj)");
        return Parse(file, text);
    });

    /// <summary>The embedded ladder text of a file name (the key plus <c>.json</c>), or null.</summary>
    /// <param name="file">The file name.</param>
    internal static string? ResourceText(string file)
    {
        using var stream = typeof(LadderLoader).Assembly.GetManifestResourceStream(ResourcePrefix + file);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads one ladder, its grounds resolved through <see cref="GroundCatalog.TryGet"/>.</summary>
    /// <param name="file">The file name (the key plus <c>.json</c>), for the messages and the key check.</param>
    /// <param name="json">The ladder's text.</param>
    internal static Ladder Parse(string file, string json) => Parse(file, json, GroundCatalog.TryGet);

    /// <summary>Reads one ladder; throws <see cref="LadderException"/> on any error.</summary>
    /// <param name="file">The file name (the key plus <c>.json</c>).</param>
    /// <param name="json">The ladder's text.</param>
    /// <param name="resolve">Resolves a ground key to its profile, or null.</param>
    internal static Ladder Parse(string file, string json, Func<string, HuntingGround?> resolve)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            throw new LadderException(file, "$", $"is not JSON: {e.Message}");
        }

        using (doc)
        {
            return new Reader(file, resolve).Read(doc.RootElement);
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^profile due in item [0-9]+(; .+)?$")]
    private static partial Regex PendingPattern();

    private sealed class Reader(string file, Func<string, HuntingGround?> resolve)
    {
        public LadderException Fail(string path, string problem) => new(file, path, problem);

        public Ladder Read(JsonElement root)
        {
            var top = Object(root, "$");
            var key = top.Key("key");
            if (key != Path.GetFileNameWithoutExtension(file))
            {
                throw Fail("$.key", $"'{key}' does not match the file name {file}");
            }

            var name = top.Text("name");
            var sources = top.Sources("sources", Ladder.SourceNames);
            var topLevel = top.Int("top_level", 2, MaxTopLevel);
            var planCastChance = top.Number("plan_cast_chance", MinPlanCastChance, 1); // M6 1 a (D4, D41): the plan's chance, a ladder value
            var planBuffChance = top.Number("plan_buff_chance", MinPlanCastChance, 1); // M6 7 AK: the bar of a buff, a ladder value
            var vitalsSection = top.Object("vitals")!; // M6 7 AN: the fight's vitals rules, data with a source sentence
            var vitals = new LadderVitals(vitalsSection.Int("drain_under_percent", 1, 100), vitalsSection.Int("mana_loop_under", 1, 1000), vitalsSection.Int("mana_loop_stamina_percent", 1, 100), vitalsSection.Int("stamina_drain_min_give", 1, 1000), vitalsSection.Number("buff_pass_wait_seconds", 1, 3600), [.. vitalsSection.Raw("start_only_lines").EnumerateArray().Select(l => l.GetString() ?? string.Empty)], vitalsSection.Int("pass_home_over_lines", 0, 100), vitalsSection.Int("refresh_mana_reserve", 0, 1000), vitalsSection.Number("mana_press_min_bolts", 0, 10), vitalsSection.Number("stamina_drain_min_bolts", 0, 10), vitalsSection.Int("mana_loop_max_attackers", 0, 100), vitalsSection.Number("mana_loop_bolt_first_hits", 0, 10), vitalsSection.Int("mana_loop_bolts_between", 0, 100), vitalsSection.Int("stamina_keep_percent", 1, 100), vitalsSection.Int("stamina_drain_max_attackers", 0, 100), vitalsSection.Int("mana_press_fill_percent", 1, 100)); // M6 14: the stamina line and the press that fills
            vitalsSection.End();
            var firstScrolls = top.List(Ladder.FirstScrollsField, (e, p) => ReadScrollGoal(e, p));
            var gates = top.List("rung_gates", ReadGate); // M6 7 AK, AR: climbing order; a rung has at most one
            var healthHits = top.Number("health_hits", 0.1, MaxHealthHits); // M6 7 R: the health floor's N, a ladder value
            var healthPay = top.Int("health_pay_percent", 1, 100); // M6 7 R: the most of the experience earned the floor may take
            var capSection = top.Object("stat_cap")!; // M6 7 finish: the owner's cap on each attribute and Max Health
            var statCap = new Mage.StatCap(capSection.Int("aim_percent", 1, 100), capSection.Int("max_percent", 1, 100), (ulong)capSection.Int("grace_xp", 0, int.MaxValue));
            capSection.End();
            var coinSection = top.Object("corpse_coin")!; // M6 7 AY: the coin half of a corpse run, data with a source sentence
            var corpseCoin = new LadderCorpseCoin(coinSection.Bool("coin_on_corpse"), coinSection.Int("min_coin", 1, int.MaxValue), coinSection.Int("min_item_value", 1, int.MaxValue), coinSection.Number("trip_stall_seconds", 1, 3600), coinSection.Number("trip_stall_metres", 1, 100));
            coinSection.End();
            var packSection = top.Object("big_pack")!; // M6 7 AY: the big pack rule
            var bigPack = new LadderBigPack(packSection.Int("awake", 2, MaxPack), packSection.Number("reach_m", 1, MaxBigPackReach), packSection.Int("deaths", 1, MaxPack), packSection.Int("mana_grows", 1, MaxManaGrows));
            packSection.End();
            var survivalSection = top.Object("survival")!; // M6 8 K: the survival layer's lines
            var survival = new LadderSurvival(survivalSection.Int("wait_health_percent", 0, 100), survivalSection.Int("back_health_percent", 0, 100), survivalSection.Int("wait_mana_percent", 0, 100), survivalSection.Int("back_mana_percent", 0, 100), survivalSection.Int("wait_stamina_percent", 0, 100), survivalSection.Int("back_stamina_percent", 0, 100), survivalSection.Int("leave_health_percent", 0, 100), survivalSection.Int("leave_mana_percent", 0, 100), survivalSection.Int("start_health_percent", 0, 100), survivalSection.Int("start_mana_percent", 0, 100), survivalSection.Int("start_stamina_percent", 0, 100));
            survivalSection.End();
            var policySection = top.ObjectOrNull("vitals_policy"); // M6 14 (m6-vitals-policy): the climber's fills by the numbers; absent: off
            var vitalsPolicy = policySection is null ? LadderVitalsPolicy.Off : ReadVitalsPolicy(policySection);
            var casterSection = top.ObjectOrNull("caster_score"); // M6 14 (m6-caster): the caster pick's weights; absent: LadderCasterScore.Default
            var casterScore = casterSection is null ? LadderCasterScore.Default : ReadCasterScore(casterSection);
            var wearSection = top.ObjectOrNull("wear_score"); // M6 14 (m6-armour): the armour and jewellery pick's weights; absent: LadderWearScore.Default
            var wearScore = wearSection is null ? LadderWearScore.Default : ReadWearScore(wearSection);
            var rungs = top.List("rungs", (e, p) => ReadRung(e, p, topLevel));
            top.End();

            if (rungs.Count == 0)
            {
                throw Fail("$.rungs", "is empty (a ladder has at least one rung)");
            }

            var expectedFrom = 1;
            for (var i = 0; i < rungs.Count; i++)
            {
                var band = rungs[i].Band;
                if (band.From != expectedFrom)
                {
                    throw Fail($"$.rungs[{i}].band.from", i == 0
                        ? $"{band.From} is not 1 (the first rung starts at level 1)"
                        : $"{band.From} does not follow $.rungs[{i - 1}].band.to {expectedFrom - 1} (the bands run in order without a gap or an overlap)");
                }

                expectedFrom = band.To + 1;
                for (var j = 0; j < i; j++)
                {
                    if (string.Equals(rungs[j].Key, rungs[i].Key, StringComparison.Ordinal))
                    {
                        throw Fail($"$.rungs[{i}].key", $"'{rungs[i].Key}' is also $.rungs[{j}].key (a rung key is unique)");
                    }
                }
            }

            if (expectedFrom - 1 != topLevel)
            {
                throw Fail($"$.rungs[{rungs.Count - 1}].band.to", $"{expectedFrom - 1} is not top_level {topLevel} (the last rung ends at the top level)");
            }

            foreach (var gate in gates)
            {
                if (rungs.All(r => !string.Equals(r.Key, gate.Rung, StringComparison.Ordinal)))
                {
                    throw Fail("$.rung_gates", $"'{gate.Rung}' is no rung (a gate that no level reaches never opens)");
                }
            }

            return new Ladder(key, name, topLevel, planCastChance, rungs, sources, healthHits, healthPay, planBuffChance, firstScrolls, gates, vitals, corpseCoin, bigPack, statCap, survival) { VitalsPolicy = vitalsPolicy, CasterScore = casterScore, WearScore = wearScore };
        }

        private LadderCasterScore ReadCasterScore(Section section)
        {
            var score = new LadderCasterScore(section.Number("damage", 0, 10), section.Number("mana", 0, 10), section.Number("melee_defence", 0, 10), section.Number("magic_defence", 0, 10), section.Number("swap_margin", 0, 100));
            section.End();
            return score;
        }

        private LadderWearScore ReadWearScore(Section section)
        {
            var score = new LadderWearScore(
                section.Number("life_magic", 0, 10),
                section.Number("creature_enchantment", 0, 10),
                section.Number("item_enchantment", 0, 10),
                section.Number("mana_pool", 0, 10),
                section.Number("mana_regen_a_second", 0, 100),
                section.Number("mana_point_seconds", 0, 10),
                section.Number("attacks_a_second", 0, 100),
                section.Number("health_point_seconds", 0, 10),
                section.Number("burden", 0, 10),
                section.Number("swap_margin", 0, 100));
            section.End();
            return score;
        }

        private LadderVitalsPolicy ReadVitalsPolicy(Section section)
        {
            var policy = new LadderVitalsPolicy(section.Bool("on"), section.Number("bolt_first_chance", 0.05, 1), section.Number("health_burst_factor", 1, 10), section.Number("seed_overhead_seconds", 0, 120), section.Int("seed_kills", 1, 100), Seconds(section, "cast_seconds_self"), Seconds(section, "cast_seconds_other"), Seconds(section, "cast_seconds_bolt"), section.Bool("press_waits_for_best"));
            section.End();
            return policy;
        }

        private IReadOnlyList<double> Seconds(Section section, string name)
        {
            var list = section.List(name, (e, p) => Number(e, p, 0.3, 15));
            return list.Count == 0 ? throw Fail($"$.vitals_policy.{name}", "is empty (a cast's seconds by level, I first)") : list;
        }

        private RungGate ReadGate(JsonElement e, string path)
        {
            var s = Object(e, path);
            var gate = new RungGate(s.Text("rung"), s.List("skills", (x, p) => (uint)Int(x, p, 1, 100)), s.List("lines", ReadGateLine));
            s.End();
            return gate;
        }

        private GateLine ReadGateLine(JsonElement e, string path)
        {
            var s = Object(e, path);
            var line = new GateLine(s.Text("line"), s.Int("level", 1, 8), s.Int("highest", 0, 1) == 1); // M6 14 (D135): to VIII, the climber's profile lines' length; the Holding's gate asks the buff bot's level VII
            s.End();
            return line;
        }

        private ScrollGoal ReadScrollGoal(JsonElement e, string path)
        {
            var s = Object(e, path);
            var goal = new ScrollGoal(s.Text("line"), s.Int("level", 1, 6));
            s.End();
            return goal;
        }

        private LadderRung ReadRung(JsonElement e, string path, int topLevel)
        {
            var s = Object(e, path);
            var key = s.Key("key");
            var name = s.Text("name");
            var kind = s.Text("kind") switch
            {
                "ground" => LadderRungKind.Ground,
                "quest" => LadderRungKind.Quest,
                var other => throw Fail($"{path}.kind", $"'{other}' is out of range (ground or quest)"),
            };

            var bandSection = s.Object("band");
            var band = new LadderBand(bandSection.Int("from", 1, topLevel), bandSection.Int("to", 1, topLevel));
            bandSection.End();
            if (band.From > band.To)
            {
                throw Fail($"{path}.band.from", $"{band.From} is above to {band.To}");
            }

            var ground = s.OptionalKeyOrNull("ground");
            var quests = s.List("quests", (q, p) => KeyText(q, p));
            var landblock = s.LandblockOrNull("landblock");
            var pending = s.Bool("pending");
            var pendingReason = s.TextOrNull("pending_reason");
            var alternates = s.List("alternates", ReadAlternate);

            var moveOnSection = s.Object("move_on");
            var moveOn = new LadderMoveOn(
                moveOnSection.Int("level", 1, topLevel),
                moveOnSection.IntOrNull("kills_an_hour_floor", 1, 100_000),
                moveOnSection.NumberOrNull("casts_a_kill_ceiling", 0.1, 1_000));
            moveOnSection.End();

            var spellSection = s.Object("spell_level");
            var spell = new LadderSpellLevel(spellSection.Int("min", 1, MaxSpellLevel), spellSection.Int("max", 1, MaxSpellLevel));
            spellSection.End();

            var element = s.TextOrNull("element");
            var targetMargin = s.IntOrNull("target_margin", 0, MaxTargetMargin);
            var route = s.Text("route");
            var health = s.ObjectOrNull("health") is { } healthSection ? ReadHealth(healthSection) : null;
            var sources = s.Sources("sources", Ladder.RungSourceNames);
            s.End();

            if (spell.Min > spell.Max)
            {
                throw Fail($"{path}.spell_level.min", $"{spell.Min} is above max {spell.Max}");
            }

            var lastMove = Math.Min(band.To + 1, topLevel);
            if (moveOn.Level <= band.From || moveOn.Level > lastMove)
            {
                throw Fail($"{path}.move_on.level", $"{moveOn.Level} is out of range ({band.From + 1} to {lastMove}: above the band's first level, at most one past its last, never past top_level)");
            }

            if (element is not null && !Ladder.Elements.Contains(element, StringComparer.Ordinal))
            {
                throw Fail($"{path}.element", $"'{element}' is out of range ({string.Join(", ", Ladder.Elements)})");
            }

            CheckRoute(route, $"{path}.route");

            if (kind == LadderRungKind.Quest)
            {
                if (ground is not null)
                {
                    throw Fail($"{path}.ground", "is set on a quest rung (a quest rung names quests, not a ground)");
                }

                if (quests.Count == 0)
                {
                    throw Fail($"{path}.quests", "is empty (a quest rung names at least one quest)");
                }

                if (targetMargin is not null)
                {
                    throw Fail($"{path}.target_margin", "is set on a quest rung (a quest rung hunts nothing of its own)");
                }

                if (pending || pendingReason is not null)
                {
                    throw Fail($"{path}.pending", "is set on a quest rung (a quest key is resolved against the quest catalog, not marked pending)");
                }

                if (alternates.Count > 0)
                {
                    throw Fail($"{path}.alternates", "is set on a quest rung (alternates are grounds)");
                }
            }
            else
            {
                if (ground is null)
                {
                    throw Fail($"{path}.ground", "is null on a ground rung (a ground rung names its profile key)");
                }

                if (quests.Count > 0)
                {
                    throw Fail($"{path}.quests", "is set on a ground rung (empty it, or make the rung a quest rung)");
                }

                if (moveOn.KillsAnHourFloor is null || moveOn.CastsAKillCeiling is null)
                {
                    throw Fail($"{path}.move_on", "has a null floor or ceiling on a ground rung (only a quest rung has neither)");
                }

                if (element is null)
                {
                    throw Fail($"{path}.element", "is null on a ground rung (a hunt names its element)");
                }

                CheckGround(ground, landblock, pending, pendingReason, path);
                if (targetMargin is null && !pending)
                {
                    throw Fail($"{path}.target_margin", "is null on a non-pending ground rung (the fight's pick reads the rung's margin; a pending rung's is item 3's)");
                }
            }

            if (kind == LadderRungKind.Quest && (moveOn.KillsAnHourFloor is not null || moveOn.CastsAKillCeiling is not null))
            {
                throw Fail($"{path}.move_on", "has a floor or ceiling on a quest rung (a quest rung moves on by level only)");
            }

            for (var i = 0; i < alternates.Count; i++)
            {
                if (string.Equals(alternates[i].GroundKey, ground, StringComparison.Ordinal) || alternates.Take(i).Any(a => string.Equals(a.GroundKey, alternates[i].GroundKey, StringComparison.Ordinal)))
                {
                    throw Fail($"{path}.alternates[{i}].ground", $"'{alternates[i].GroundKey}' is the rung's ground or another alternate (each ground once)");
                }
            }

            if (kind == LadderRungKind.Quest && health is not null)
            {
                throw Fail($"{path}.health", "is set on a quest rung (a quest rung hunts nothing of its own)");
            }

            return new LadderRung(key, name, kind, band, ground, quests, landblock, alternates, moveOn, spell, element, targetMargin, route, pending, pendingReason, sources, health);
        }

        /// <summary>M6 7 R: a rung's <c>health</c>: its hardest hitter and, where the route runs the island, the road's.</summary>
        private static LadderHealth ReadHealth(Section s)
        {
            var hitter = ReadHitter(s.Object("hitter"));
            var road = s.ObjectOrNull("road_hitter") is { } section ? ReadHitter(section) : null;
            var pack = s.Int("pack", 1, MaxPack); // M6 7 AY (the owner): the pack the rung sends; its reason is in the rung's sources.health
            s.End();
            return new LadderHealth(hitter, road, pack);
        }

        private static LadderHitter ReadHitter(Section s)
        {
            var hitter = new LadderHitter(s.Text("name"), (uint)s.Int("wcid", 1, int.MaxValue), s.Int("d_val", 1, MaxHitterStat), s.Number("d_var", 0, 1), s.Int("strength", 1, MaxHitterStat));
            s.End();
            return hitter;
        }

        private LadderAlternate ReadAlternate(JsonElement e, string path)
        {
            var s = Object(e, path);
            var ground = s.Key("ground");
            var landblock = s.LandblockOrNull("landblock");
            var pending = s.Bool("pending");
            var reason = s.TextOrNull("pending_reason");
            s.End();
            CheckGround(ground, landblock, pending, reason, path);
            return new LadderAlternate(ground, landblock, pending, reason);
        }

        /// <summary>A ground that is not pending resolves (and holds the named landblock); a pending one does not resolve and names the item that owns its profile.</summary>
        private void CheckGround(string ground, ushort? landblock, bool pending, string? reason, string path)
        {
            var profile = resolve(ground);
            if (pending)
            {
                if (reason is null || !PendingPattern().IsMatch(reason))
                {
                    throw Fail($"{path}.pending_reason", $"'{reason ?? "null"}' does not name the item that owns the profile (\"profile due in item N\", optionally \"; \" and more)");
                }

                if (profile is not null)
                {
                    throw Fail($"{path}.pending", $"is true but the ground '{ground}' resolves to a profile (a stale pending: clear it and its reason)");
                }

                return;
            }

            if (reason is not null)
            {
                throw Fail($"{path}.pending_reason", "is set on a rung that is not pending (null it)");
            }

            if (profile is null)
            {
                throw Fail($"{path}.ground", $"'{ground}' does not resolve through GroundCatalog (mark it pending with the item that owns the profile, or fix the key; known: {string.Join(", ", GroundCatalog.All.Select(g => g.Key))})");
            }

            if (landblock is { } lb && !profile.Landblocks.Contains(lb))
            {
                throw Fail($"{path}.landblock", $"0x{lb:X4} is not a landblock of the profile '{ground}' ({string.Join(", ", profile.Landblocks.Select(l => $"0x{l:X4}"))})");
            }
        }

        private void CheckRoute(string route, string path)
        {
            var colon = route.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || !Ladder.RouteKinds.Contains(route[..colon], StringComparer.Ordinal) || !KeyPattern().IsMatch(route[(colon + 1)..]))
            {
                throw Fail(path, $"'{route}' is not a route key (<kind>:<place>, the kind one of {string.Join(", ", Ladder.RouteKinds)}, the place lower-case words joined by '-')");
            }
        }

        public Section Object(JsonElement e, string path) =>
            e.ValueKind == JsonValueKind.Object ? new Section(this, e, path) : throw Fail(path, $"is {e.ValueKind}, not an object");

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
            e.ValueKind == JsonValueKind.String && e.GetString() is { } t && t.Trim().Length > 0 ? t : throw Fail(path, $"is {e.ValueKind}, not a non-empty text");

        public string KeyText(JsonElement e, string path)
        {
            var t = Text(e, path);
            return KeyPattern().IsMatch(t) ? t : throw Fail(path, $"'{t}' is not a key (lower-case words joined by '-')");
        }

        public int Int(JsonElement e, string path, int min, int max)
        {
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var v))
            {
                throw Fail(path, $"is {e.ValueKind}, not a whole number");
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
            return v >= min && v <= max ? v : throw Fail(path, $"{v.ToString(CultureInfo.InvariantCulture)} is out of range ({min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)})");
        }

        public ushort Landblock(JsonElement e, string path)
        {
            var t = Text(e, path);
            if (!t.StartsWith("0x", StringComparison.Ordinal) || !uint.TryParse(t.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
            {
                throw Fail(path, $"'{t}' is not a hex number (\"0x5748\")");
            }

            return v <= 0xFFFF ? (ushort)v : throw Fail(path, $"0x{v:X} is out of range (a landblock is 0x0000 to 0xFFFF)");
        }
    }

    /// <summary>A JSON object being read: takes fields by name and, at <see cref="End"/>, fails on a field it did not take.</summary>
    private sealed class Section(Reader reader, JsonElement element, string path)
    {
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        public JsonElement Raw(string name) => Take(name);

        private JsonElement Take(string name)
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

            return found ? value : throw reader.Fail($"{path}.{name}", "is missing (a required field)");
        }

        private JsonElement? TakeOrNull(string name)
        {
            var e = Take(name);
            return e.ValueKind == JsonValueKind.Null ? null : e;
        }

        public void End()
        {
            foreach (var p in element.EnumerateObject())
            {
                if (!_taken.Contains(p.Name))
                {
                    throw reader.Fail($"{path}.{p.Name}", "is not a field of the ladder (unknown field)");
                }
            }
        }

        public string Text(string name) => reader.Text(Take(name), $"{path}.{name}");

        public string Key(string name) => reader.KeyText(Take(name), $"{path}.{name}");

        public string? TextOrNull(string name) => TakeOrNull(name) is { } e ? reader.Text(e, $"{path}.{name}") : null;

        public string? OptionalKeyOrNull(string name) => TakeOrNull(name) is { } e ? reader.KeyText(e, $"{path}.{name}") : null;

        public int Int(string name, int min, int max) => reader.Int(Take(name), $"{path}.{name}", min, max);

        public int? IntOrNull(string name, int min, int max) => TakeOrNull(name) is { } e ? reader.Int(e, $"{path}.{name}", min, max) : null;

        public double Number(string name, double min, double max) => reader.Number(Take(name), $"{path}.{name}", min, max);

        public double? NumberOrNull(string name, double min, double max) => TakeOrNull(name) is { } e ? reader.Number(e, $"{path}.{name}", min, max) : null;

        public ushort? LandblockOrNull(string name) => TakeOrNull(name) is { } e ? reader.Landblock(e, $"{path}.{name}") : null;

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

        public Section Object(string name) => reader.Object(Take(name), $"{path}.{name}");

        public Section? ObjectOrNull(string name) => TakeOrNull(name) is { } e ? reader.Object(e, $"{path}.{name}") : null;

        public IReadOnlyList<T> List<T>(string name, Func<JsonElement, string, T> item) => reader.List(Take(name), $"{path}.{name}", item);

        /// <summary>A sources object: exactly the named fields, each a non-empty text.</summary>
        public IReadOnlyDictionary<string, string> Sources(string name, IReadOnlyList<string> fields)
        {
            var s = Object(name);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                map[field] = s.Text(field);
            }

            s.End();
            return map.AsReadOnly();
        }
    }
}

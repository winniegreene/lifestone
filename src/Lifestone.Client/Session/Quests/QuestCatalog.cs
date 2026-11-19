using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Session.Creatures;

namespace Lifestone.Client.Session.Quests;

/// <summary>A quest file that cannot load: the file and the field's JSON path are named (M6 DoD 7, D26; the loudness of D43's grounds).</summary>
public sealed class QuestFileException : Exception
{
    /// <summary>A quest file error.</summary>
    /// <param name="file">The quest's file name.</param>
    /// <param name="path">The field's JSON path (for example <c>$.steps[2].item.wcid</c>).</param>
    /// <param name="problem">What is wrong.</param>
    public QuestFileException(string file, string path, string problem)
        : base($"quest file {file}: {path} {problem}")
    {
        File = file;
        JsonPath = path;
    }

    /// <summary>The quest's file name.</summary>
    public string File { get; }

    /// <summary>The field's JSON path.</summary>
    public string JsonPath { get; }
}

/// <summary>
/// The quests (M6 DoD 7, D26: a shape is code, a quest is a file): every file embedded under <c>Session/Quests/quests/</c>, read once
/// with <c>System.Text.Json</c> (the BCL; no package), as strictly as <see cref="Grounds.GroundCatalog"/>. A missing field, a field the
/// quest shape does not have, a duplicated field, a wrong type or an out-of-range value fails at load with a <see cref="QuestFileException"/>
/// naming the file and the field's JSON path; so does a step kind of the vocabulary not yet implemented (<c>not yet (item n)</c>, <see cref="QuestStepKinds"/>)
/// and a step whose kind's fields are not there. <see cref="Get"/> of an unknown key throws, naming it.
/// </summary>
/// <remarks>
/// The format: every field is required and appears once, except the optional ones named here; <c>null</c> is a value where the record allows it (a
/// step's <c>place</c>, <c>target</c>, <c>item</c>, <c>todo</c>, an expectation's fields, a thing's <c>wcid</c>, a place's <c>point</c>, the quest's
/// <c>giver</c> and <c>timer_seconds</c>). Cells are hex strings (<c>"0x86020169"</c>); weenie class ids are numbers; a point is
/// <c>[x, y, z]</c>. A null wcid, or a talk, use or give without a check, needs the step's <c>todo</c>, a <c>TODO(name): ...</c>.
/// Optional (item 5, Q1): a step's <c>count</c> (a whole number above 0: kill, hunt-until, buy), <c>ground</c> (a key <see cref="Grounds.GroundCatalog"/>
/// knows: kill, hunt-until), <c>spell</c> (a spell id above 0: recall) and <c>answer</c> (<c>"yes"</c> or <c>"no"</c>: confirm), each refused by its path
/// on a kind that does not use it and never null when present; the quest's <c>level</c> <c>{min, max}</c> (max null for no top) and <c>experience</c>
/// <c>{amount, kind, max, percent, source}</c> (<see cref="QuestExperience"/>), null when absent. Optional (M6 5 F1): a talk step's <c>armed_reply</c> (a task already armed: accepted as the arm) and
/// <c>wait_reply</c> (a task on its timer: the station stops <c>talk:on-timer</c>), refused on the other kinds. Optional (item 8): a combine's or an apply's <c>count</c> (the repeats), <c>fail_item</c> (<c>{wcid, name}</c>, never null when present) and
/// <c>fail_reply</c> (the recipe row's whole fail line), the last two refused on the other kinds; an apply needs <c>fail_reply</c>, and a <c>fail_item</c> needs it too (the field rules are on <see cref="QuestStep"/>). Optional (item 8, D118): the quest's <c>ladder</c> <c>{from_level, before_level, once}</c> (<see cref="QuestLadder"/>: all three required, <c>before_level</c> above <c>from_level</c>), null when absent. M6 0's optional <c>carry</c> and M6 2's <c>returns</c> stay.
/// </remarks>
public static class QuestCatalog
{
    /// <summary>The logical-name prefix of the embedded quest files (Lifestone.Client.csproj).</summary>
    internal const string ResourcePrefix = "Lifestone.Client.Session.Quests.quests.";

    private static readonly Lazy<IReadOnlyList<Quest>> Loaded = new(LoadAll);

    /// <summary>Every quest, in key order.</summary>
    public static IReadOnlyList<Quest> All => Loaded.Value;

    /// <summary>The quest of <paramref name="key"/>.</summary>
    /// <param name="key">The quest's key (its file name without <c>.json</c>).</param>
    /// <exception cref="KeyNotFoundException">No quest has the key.</exception>
    public static Quest Get(string key)
    {
        foreach (var q in All)
        {
            if (string.Equals(q.Key, key, StringComparison.Ordinal))
            {
                return q;
            }
        }

        throw new KeyNotFoundException($"no quest file '{key}' (known: {string.Join(", ", All.Select(q => q.Key))})");
    }

    /// <summary>The embedded text of a quest file (the key plus <c>.json</c>), or null.</summary>
    /// <param name="file">The file name.</param>
    internal static string? ResourceText(string file)
    {
        using var stream = typeof(QuestCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + file);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads one quest file; throws <see cref="QuestFileException"/> on any error.</summary>
    /// <param name="file">The file name (the key plus <c>.json</c>), for the messages and the key check.</param>
    /// <param name="json">The file's text.</param>
    internal static Quest Parse(string file, string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            throw new QuestFileException(file, "$", $"is not JSON: {e.Message}");
        }

        using (doc)
        {
            return new Reader(file).Quest(doc.RootElement);
        }
    }

    private static IReadOnlyList<Quest> LoadAll()
    {
        var assembly = typeof(QuestCatalog).Assembly;
        var quests = new List<Quest>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var file = name[ResourcePrefix.Length..];
            quests.Add(Parse(file, ResourceText(file)!));
        }

        if (quests.Count == 0)
        {
            throw new InvalidOperationException($"no quest file is embedded (resource prefix {ResourcePrefix}; Lifestone.Client.csproj)");
        }

        return quests.AsReadOnly();
    }

    /// <summary>A JSON object being read: takes fields by name and, at <see cref="End"/>, fails on a field it did not take.</summary>
    private sealed class Section
    {
        private readonly Reader _reader;
        private readonly JsonElement _element;
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        public Section(Reader reader, JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw reader.Fail(path, $"is {element.ValueKind}, not an object");
            }

            _reader = reader;
            _element = element;
            Path = path;
        }

        public string Path { get; }

        public JsonElement Take(string name)
        {
            _taken.Add(name);
            var found = false;
            var value = default(JsonElement);
            foreach (var p in _element.EnumerateObject())
            {
                if (p.NameEquals(name))
                {
                    if (found)
                    {
                        throw _reader.Fail($"{Path}.{name}", "appears twice");
                    }

                    found = true;
                    value = p.Value;
                }
            }

            if (!found)
            {
                throw _reader.Fail($"{Path}.{name}", "is missing (a required field)");
            }

            return value;
        }

        public bool IsNull(string name) => Take(name).ValueKind == JsonValueKind.Null;

        /// <summary>The field when the file has it, else null (an optional field, M6 0 F8: a file written before it need not name it).</summary>
        public JsonElement? TakeOptional(string name)
        {
            foreach (var p in _element.EnumerateObject())
            {
                if (p.NameEquals(name))
                {
                    return Take(name);
                }
            }

            return null;
        }

        public void End()
        {
            foreach (var p in _element.EnumerateObject())
            {
                if (!_taken.Contains(p.Name))
                {
                    throw _reader.Fail($"{Path}.{p.Name}", "is not a field of the quest file (unknown field)");
                }
            }
        }

        public string Text(string name) => _reader.Text(Take(name), $"{Path}.{name}");

        public string? OptionalText(string name) => IsNull(name) ? null : Text(name);

        public Section Object(string name) => new(_reader, Take(name), $"{Path}.{name}");

        public Section? OptionalObject(string name) => IsNull(name) ? null : Object(name);
    }

    private sealed class Reader(string file)
    {
        public QuestFileException Fail(string path, string problem) => new(file, path, problem);

        public string Text(JsonElement e, string path)
        {
            if (e.ValueKind != JsonValueKind.String)
            {
                throw Fail(path, $"is {e.ValueKind}, not a string");
            }

            var s = e.GetString()!;
            if (s.Trim().Length == 0)
            {
                throw Fail(path, "is empty");
            }

            return s;
        }

        public Quest Quest(JsonElement root)
        {
            var q = new Section(this, root, "$");
            var key = q.Text("key");
            var stem = file.EndsWith(".json", StringComparison.Ordinal) ? file[..^5] : file;
            if (!string.Equals(key, stem, StringComparison.Ordinal))
            {
                throw Fail("$.key", $"is '{key}', not the file's name '{stem}'");
            }

            var name = q.Text("name");
            var giver = Thing(q, "giver", nullable: true, todo: null);
            var start = Place(q.Object("start"));
            var stepsElement = q.Take("steps");
            if (stepsElement.ValueKind != JsonValueKind.Array || stepsElement.GetArrayLength() == 0)
            {
                throw Fail("$.steps", "is not a non-empty array");
            }

            var steps = new List<QuestStep>();
            var i = 0;
            foreach (var s in stepsElement.EnumerateArray())
            {
                steps.Add(Step(new Section(this, s, $"$.steps[{i}]"), i));
                i++;
            }

            var done = Done(q.Object("done"));
            var timer = TimerSeconds(q.Take("timer_seconds"));
            var timerFrom = TimerFrom(q.TakeOptional("timer_from")); // M6 audit: where the timer starts (the tusks: at the pickup)
            var sources = Sources(q.Take("sources"));
            var carry = Carry(q.TakeOptional("carry"));
            _ = q.TakeOptional("returns"); // M6 2 d: the pieces an NPC takes back, read as data by GearReturnTable (Mage/GearReturn.cs), not part of the quest's shape
            _ = q.TakeOptional("armour"); // M6 item 14: the chest's armour set worn in place of the robe, read as data by ArmourSet (Mage/ArmourSet.cs), not part of the quest's shape
            _ = q.TakeOptional("armour_plate_off"); // M6 item 14: the plate set, off by the owner's order: kept as data (ArmourSet.OfKey), read by nothing as the armour form
            _ = q.TakeOptional("leather"); // M6 item 14: the leather set bought in the giver's town and worn in the robe's place, read as data by LeatherSet (Mage/LeatherSet.cs)
            var level = Level(q.TakeOptional("level"));
            var experience = Experience(q.TakeOptional("experience"));
            var ladder = Ladder(q.TakeOptional("ladder"));
            q.End();
            return new Quest
            {
                Key = key, Name = name, Giver = giver, Start = start, Steps = steps.AsReadOnly(), Done = done, TimerSeconds = timer, Sources = sources, Carry = carry,
                Level = level, Experience = experience, Ladder = ladder, TimerFrom = timerFrom,
            };
        }

        private QuestStep Step(Section s, int index)
        {
            var word = s.Text("kind");
            var kind = QuestStepKinds.Parse(word)
                ?? throw Fail($"{s.Path}.kind", $"is '{word}', not a word of the step vocabulary ({string.Join(", ", QuestStepKinds.Words)})");
            if (!QuestStepKinds.IsImplemented(kind))
            {
                throw Fail($"{s.Path}.kind", $"is '{word}': not yet (item {QuestStepKinds.BuiltBy(kind)}); M6 implements only {string.Join(", ", QuestStepKinds.Implemented.Select(QuestStepKinds.Word))} (TODO(m6-quest-step-{word}))");
            }

            var what = s.Text("what");
            var todo = s.OptionalText("todo");
            if (todo is not null && !todo.StartsWith("TODO(", StringComparison.Ordinal))
            {
                throw Fail($"{s.Path}.todo", "is not a named TODO (TODO(name): ...)");
            }

            var placeSection = s.OptionalObject("place");
            var place = placeSection is null ? null : Place(placeSection);
            var target = Thing(s, "target", nullable: true, todo);
            var item = Thing(s, "item", nullable: true, todo);
            var expect = Expect(s.Object("expect"), todo);
            var source = s.Text("source");
            var count = Count(s.TakeOptional("count"), s.Path + ".count");
            var ground = GroundKey(s.TakeOptional("ground"), s.Path + ".ground");
            var spell = Spell(s.TakeOptional("spell"), s.Path + ".spell");
            var answer = Answer(s.TakeOptional("answer"), s.Path + ".answer");
            var armedReply = s.TakeOptional("armed_reply") is { } armedElement ? Text(armedElement, s.Path + ".armed_reply") : null;
            var waitReply = s.TakeOptional("wait_reply") is { } waitElement ? Text(waitElement, s.Path + ".wait_reply") : null;
            var failItem = s.TakeOptional("fail_item") is { } failItemElement ? ThingOf(new Section(this, failItemElement, s.Path + ".fail_item"), todo) : null;
            var failReply = s.TakeOptional("fail_reply") is { } failReplyElement ? Text(failReplyElement, s.Path + ".fail_reply") : null;
            var recipeSkill = Count(s.TakeOptional("skill"), s.Path + ".skill"); // M6 14 (D136): the recipe row's skill and difficulty, both or neither, on a combine or an apply with a roll
            var recipeDifficulty = Count(s.TakeOptional("difficulty"), s.Path + ".difficulty");
            var carriers = Carriers(s.TakeOptional("carriers"), s.Path + ".carriers"); // M6 14 want loot: the creatures whose create list can hold the hunted item, each with its row's shade
            var killRate = KillRate(s.TakeOptional("kill_rate"), s.Path + ".kill_rate");
            var way = Way(s, kind, place);
            s.End();

            // Item 5 (Q1): the new fields only on the kinds that use them (the contract between the slices, cache/m6/5/notes.md).
            Unused(count, kind is QuestStepKind.Kill or QuestStepKind.HuntUntil or QuestStepKind.Buy or QuestStepKind.Combine or QuestStepKind.Apply, word, s.Path + ".count", "count", "kill, hunt-until, buy, combine and apply");
            Unused(ground, kind is QuestStepKind.Kill or QuestStepKind.HuntUntil, word, s.Path + ".ground", "ground", "kill and hunt-until");
            Unused(spell, kind is QuestStepKind.Recall, word, s.Path + ".spell", "spell", "recall");
            Unused(answer, kind is QuestStepKind.Confirm, word, s.Path + ".answer", "answer", "confirm");
            Unused(armedReply, kind is QuestStepKind.Talk, word, s.Path + ".armed_reply", "armed_reply", "talk");
            Unused(waitReply, kind is QuestStepKind.Talk, word, s.Path + ".wait_reply", "wait_reply", "talk");
            Unused(failItem, kind is QuestStepKind.Combine or QuestStepKind.Apply, word, s.Path + ".fail_item", "fail_item", "combine and apply");
            Unused(failReply, kind is QuestStepKind.Combine or QuestStepKind.Apply, word, s.Path + ".fail_reply", "fail_reply", "combine and apply");
            Unused(recipeSkill, kind is QuestStepKind.Combine or QuestStepKind.Apply, word, s.Path + ".skill", "skill", "combine and apply");
            Unused(recipeDifficulty, kind is QuestStepKind.Combine or QuestStepKind.Apply, word, s.Path + ".difficulty", "difficulty", "combine and apply");
            Unused(carriers, kind is QuestStepKind.HuntUntil, word, s.Path + ".carriers", "carriers", "hunt-until");
            Unused(killRate, kind is QuestStepKind.HuntUntil, word, s.Path + ".kill_rate", "kill_rate", "hunt-until");
            Require(killRate is null || (carriers is not null && carriers.Exists(c => c.Wcid == killRate.Wcid)), s.Path + ".kill_rate.wcid", "names no carrier of the step: the rate is of a creature whose create list gives the item");
            Require((recipeSkill is null) == (recipeDifficulty is null), s.Path + ".skill", "and difficulty come together: a recipe with a roll names both (RecipeManager.HasDifficulty), one with none names neither");

            switch (kind)
            {
                case QuestStepKind.Go:
                    Require(place is not null || target is not null, s.Path, "a go step needs a place or a target");
                    Forbid(item, s.Path + ".item", "a go step carries no item");
                    break;
                case QuestStepKind.Talk:
                    Require(target is not null, s.Path + ".target", "is null: a talk step needs the NPC");
                    Forbid(item, s.Path + ".item", "a talk step carries no item (a give step gives one)");
                    Require(!expect.IsEmpty || todo is not null, s.Path + ".expect", "is empty: a talk step needs a reply or stamp to check, or a todo naming why not");
                    break;
                case QuestStepKind.Use:
                    Require(target is not null, s.Path + ".target", "is null: a use step needs the object used");
                    Require(!expect.IsEmpty || place is not null || todo is not null, s.Path + ".expect", "is empty: a use step needs a check (a reply, stamp, item or arrival place), or a todo naming why not");
                    break;
                case QuestStepKind.Give:
                    Require(target is not null, s.Path + ".target", "is null: a give step needs the NPC");
                    Require(item is not null, s.Path + ".item", "is null: a give step needs the item given");
                    Require(!expect.IsEmpty || todo is not null, s.Path + ".expect", "is empty: a give step needs a reply, stamp or item to check, or a todo naming why not");
                    break;
                case QuestStepKind.PickUp:
                    Require(item is not null, s.Path + ".item", "is null: a pick-up step needs the item taken");
                    Forbid(place, s.Path + ".place", "a pick-up step has no place (a go step reaches it)");
                    break;
                case QuestStepKind.Kill:
                    Require(target is not null, s.Path + ".target", "is null: a kill step needs the creature");
                    Require(count is not null, s.Path + ".count", "is missing: a kill step needs how many");
                    Require(ground is not null, s.Path + ".ground", "is missing: a kill step needs the ground profile it hunts");
                    Require(expect.Reply is not null, s.Path + ".expect.reply", "is null: a kill step's check is the kill counter's complete text (Your task is complete!)");
                    Forbid(item, s.Path + ".item", "a kill step carries no item (a hunt-until step hunts for one)");
                    Forbid(place, s.Path + ".place", "a kill step hunts its ground (the ground field), not a place");
                    break;
                case QuestStepKind.HuntUntil:
                    Require(item is not null, s.Path + ".item", "is null: a hunt-until step needs the item hunted for");
                    Require(count is not null, s.Path + ".count", "is missing: a hunt-until step needs how many of the item");
                    Require(ground is not null, s.Path + ".ground", "is missing: a hunt-until step needs the ground profile it hunts");
                    Forbid(place, s.Path + ".place", "a hunt-until step hunts its ground (the ground field), not a place");
                    break;
                case QuestStepKind.Buy:
                    Require(target is not null, s.Path + ".target", "is null: a buy step needs the vendor");
                    Require(item is not null, s.Path + ".item", "is null: a buy step needs the item bought");
                    Require(count is not null, s.Path + ".count", "is missing: a buy step needs how many");
                    Forbid(place, s.Path + ".place", "a buy step has no place (a go step reaches the vendor)");
                    break;
                case QuestStepKind.Recall:
                    Require(spell is not null, s.Path + ".spell", "is missing: a recall step needs the spell id");
                    Require(place is not null, s.Path + ".place", "is null: a recall step needs the arrival place");
                    Forbid(target, s.Path + ".target", "a recall step acts on no object");
                    Forbid(item, s.Path + ".item", "a recall step carries no item");
                    break;
                case QuestStepKind.Confirm:
                    Require(expect.Reply is not null, s.Path + ".expect.reply", "is null: a confirm step needs the question's text");
                    Require(answer is not null, s.Path + ".answer", "is missing: a confirm step needs its answer (yes or no)");
                    Forbid(target, s.Path + ".target", "a confirm step acts on no object (the step before it asks the question)");
                    Forbid(item, s.Path + ".item", "a confirm step carries no item");
                    Forbid(place, s.Path + ".place", "a confirm step has no place");
                    break;
                case QuestStepKind.Wear:
                    Require(item is not null, s.Path + ".item", "is null: a wear step needs the item worn");
                    Forbid(target, s.Path + ".target", "a wear step acts on no object");
                    Forbid(place, s.Path + ".place", "a wear step has no place");
                    break;
                case QuestStepKind.Combine:
                case QuestStepKind.Apply:
                    Require(item is not null, s.Path + ".item", $"is null: a {word} step needs the source item");
                    Require(target is not null, s.Path + ".target", $"is null: a {word} step needs the target item");
                    Require(expect.Item is not null, s.Path + ".expect.item", $"is null: a {word} step's check is the result arriving (the items decide: a recipe's two lines can be one text)");
                    Require(expect.Reply is not null, s.Path + ".expect.reply", $"is null: a {word} step needs the recipe's whole success line");
                    Forbid(expect.Stamp, s.Path + ".expect.stamp", $"a {word} step's check is the items, not a stamp");
                    Forbid(place, s.Path + ".place", $"a {word} step has no place (both items are held)");
                    Require(kind != QuestStepKind.Apply || failReply is not null, s.Path + ".fail_reply", "is missing: an apply step may roll, so it names the recipe's whole fail line");
                    Require(failItem is null || failReply is not null, s.Path + ".fail_reply", $"is missing: a {word} step that names a fail result names the fail line too");
                    break;
                default:
                    throw new InvalidOperationException($"TODO(m6-quest-step-{word}): the kind passed the implemented check but has no field rules");
            }

            return new QuestStep
            {
                Index = index, Kind = kind, What = what, Place = place, Target = target, Item = item, Expect = expect, Source = source, Todo = todo,
                Way = way, Count = count, Ground = ground, Spell = spell, Answer = answer, ArmedReply = armedReply, WaitReply = waitReply, FailItem = failItem, FailReply = failReply,
                RecipeSkill = (uint?)recipeSkill, RecipeDifficulty = (uint?)recipeDifficulty, Carriers = carriers, KillRate = killRate,
            };
        }

        /// <summary>M6 8 B2c: a go step's optional <c>way</c>: at least two points, the last one the step's place.</summary>
        private List<QuestPlace>? Way(Section s, QuestStepKind kind, QuestPlace? place)
        {
            if (s.TakeOptional("way") is not { } e)
            {
                return null;
            }

            var path = s.Path + ".way";
            Require(kind == QuestStepKind.Go, path, "is set: only a go step has a way");
            Require(e.ValueKind == JsonValueKind.Array, path, "is not an array");
            var way = new List<QuestPlace>();
            foreach (var point in e.EnumerateArray())
            {
                var section = new Section(this, point, $"{path}[{way.Count}]");
                way.Add(Place(section));
            }

            Require(way.Count >= 2 && place is not null && way[^1].Cell == place.Cell && way[^1].Point == place.Point && way.All(w => w.Point is not null), path, "needs at least two points with a point each, the last the step's place");
            return way;
        }

        private void Unused(object? value, bool used, string word, string path, string field, string users)
        {
            if (value is not null && !used)
            {
                throw Fail(path, $"is set: a {word} step has no {field} (only {users} use it)");
            }
        }

        private int? Count(JsonElement? e, string path)
        {
            if (e is not { } element)
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var count) || count <= 0)
            {
                throw Fail(path, "is not a whole number above 0 (leave the field out on a step without one)");
            }

            return count;
        }

        /// <summary>M6 14 want loot: a hunt-until's <c>carriers</c>: each <c>{"wcid", "shade", "row"}</c>, a creature of the creature table (its name from there), the row's shade in (0, 1] and the create-list row's id.</summary>
        private List<QuestCarrier>? Carriers(JsonElement? e, string path)
        {
            if (e is not { } element)
            {
                return null;
            }

            Require(element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0, path, "is not a non-empty array");
            var list = new List<QuestCarrier>();
            var i = 0;
            foreach (var c in element.EnumerateArray())
            {
                var cp = string.Create(CultureInfo.InvariantCulture, $"{path}[{i++}]");
                var section = new Section(this, c, cp);
                var wcidElement = section.Take("wcid");
                var shadeElement = section.Take("shade");
                var rowElement = section.Take("row");
                section.End();
                if (wcidElement.ValueKind != JsonValueKind.Number || !wcidElement.TryGetUInt32(out var wcid) || wcid == 0)
                {
                    throw Fail(cp + ".wcid", "is not a weenie class id (a whole number above 0)");
                }

                var creature = Bestiary.Find(wcid) ?? throw Fail(cp + ".wcid", string.Create(CultureInfo.InvariantCulture, $"{wcid} is no creature weenie of the creature table (creatures.json)"));
                if (shadeElement.ValueKind != JsonValueKind.Number || !shadeElement.TryGetDouble(out var shade) || shade is <= 0 or > 1)
                {
                    throw Fail(cp + ".shade", "is not a create-list shade in (0, 1]");
                }

                if (rowElement.ValueKind != JsonValueKind.Number || !rowElement.TryGetInt32(out var row) || row <= 0)
                {
                    throw Fail(cp + ".row", "is not a create-list row id (a whole number above 0)");
                }

                Require(!list.Exists(x => x.Wcid == wcid), cp + ".wcid", string.Create(CultureInfo.InvariantCulture, $"names {wcid} twice"));
                list.Add(new QuestCarrier(wcid, creature.Identity.Name, shade, row));
            }

            return list;
        }

        /// <summary>M6 14 want loot: a hunt-until's <c>kill_rate</c>: <c>{"wcid", "kills", "minutes", "source"}</c>, the kills of one carrier measured live over the minutes, with where it was measured.</summary>
        private QuestKillRate? KillRate(JsonElement? e, string path)
        {
            if (e is not { } element)
            {
                return null;
            }

            var section = new Section(this, element, path);
            var wcidElement = section.Take("wcid");
            var kills = Count(section.Take("kills"), path + ".kills")!.Value;
            var minutesElement = section.Take("minutes");
            var source = section.Text("source");
            section.End();
            if (wcidElement.ValueKind != JsonValueKind.Number || !wcidElement.TryGetUInt32(out var wcid) || wcid == 0)
            {
                throw Fail(path + ".wcid", "is not a weenie class id (a whole number above 0)");
            }

            if (minutesElement.ValueKind != JsonValueKind.Number || !minutesElement.TryGetDouble(out var minutes) || minutes <= 0)
            {
                throw Fail(path + ".minutes", "is not a number of minutes above 0");
            }

            return new QuestKillRate(wcid, kills, minutes, source);
        }

        private string? GroundKey(JsonElement? e, string path)
        {
            if (e is not { } element)
            {
                return null;
            }

            var key = Text(element, path);
            if (Grounds.GroundCatalog.TryGet(key) is null)
            {
                throw Fail(path, $"is '{key}', not a ground profile key (known: {string.Join(", ", Grounds.GroundCatalog.All.Select(g => g.Key))})");
            }

            return key;
        }

        private uint? Spell(JsonElement? e, string path)
        {
            if (e is not { } element)
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetUInt32(out var spell) || spell == 0)
            {
                throw Fail(path, "is not a spell id (a whole number above 0)");
            }

            return spell;
        }

        private bool? Answer(JsonElement? e, string path)
        {
            if (e is not { } element)
            {
                return null;
            }

            var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            return text switch
            {
                "yes" => true,
                "no" => false,
                _ => throw Fail(path, $"is {(text is null ? element.ValueKind.ToString() : $"'{text}'")}, not \"yes\" or \"no\""),
            };
        }

        private QuestLevel? Level(JsonElement? e)
        {
            if (e is not { } element || element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var section = new Section(this, element, "$.level");
            var minElement = section.Take("min");
            var maxElement = section.Take("max");
            section.End();
            if (minElement.ValueKind != JsonValueKind.Number || !minElement.TryGetInt32(out var min) || min < 1)
            {
                throw Fail("$.level.min", "is not a whole level of 1 or more");
            }

            int? max = null;
            if (maxElement.ValueKind != JsonValueKind.Null)
            {
                if (maxElement.ValueKind != JsonValueKind.Number || !maxElement.TryGetInt32(out var m) || m < min)
                {
                    throw Fail("$.level.max", $"is not a whole level of {min} (the min) or more, or null (no top)");
                }

                max = m;
            }

            return new QuestLevel(min, max);
        }

        private QuestLadder? Ladder(JsonElement? e)
        {
            if (e is not { } element || element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var section = new Section(this, element, "$.ladder");
            var fromElement = section.Take("from_level");
            var beforeElement = section.Take("before_level");
            var onceElement = section.Take("once");
            section.End();
            if (fromElement.ValueKind != JsonValueKind.Number || !fromElement.TryGetInt32(out var from) || from < 1)
            {
                throw Fail("$.ladder.from_level", "is not a whole level of 1 or more");
            }

            if (beforeElement.ValueKind != JsonValueKind.Number || !beforeElement.TryGetInt32(out var before) || before <= from)
            {
                throw Fail("$.ladder.before_level", $"is not a whole level above {from} (the from_level)");
            }

            if (onceElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw Fail("$.ladder.once", $"is {onceElement.ValueKind}, not true or false");
            }

            return new QuestLadder(from, before, onceElement.GetBoolean());
        }

        private QuestExperience? Experience(JsonElement? e)
        {
            if (e is not { } element || element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var section = new Section(this, element, "$.experience");
            var amount = OptionalLong(section.Take("amount"), "$.experience.amount");
            var kindText = section.Text("kind");
            var max = OptionalLong(section.Take("max"), "$.experience.max");
            var percentElement = section.Take("percent");
            var source = section.Text("source");
            section.End();
            double? percent = null;
            if (percentElement.ValueKind != JsonValueKind.Null)
            {
                if (percentElement.ValueKind != JsonValueKind.Number || !double.IsFinite(percentElement.GetDouble()) || percentElement.GetDouble() <= 0)
                {
                    throw Fail("$.experience.percent", "is not a share above 0 (1.33 is 133% of the next level's experience), or null");
                }

                percent = percentElement.GetDouble();
            }

            switch (kindText)
            {
                case "fixed":
                    Require(amount is > 0, "$.experience.amount", "is not above 0: a fixed award needs its amount");
                    Forbid(max, "$.experience.max", "a fixed award has no cap");
                    Forbid(percent, "$.experience.percent", "a fixed award has no share");
                    return new QuestExperience(amount, QuestExperienceKind.Fixed, null, null, source);
                case "proportional":
                    Require(percent is not null, "$.experience.percent", "is null: a proportional award needs its share of the next level's experience");
                    Require(max is null or > 0, "$.experience.max", "is not above 0: a proportional award's cap is above 0, or null for none");
                    return new QuestExperience(amount, QuestExperienceKind.Proportional, max, percent, source);
                default:
                    throw Fail("$.experience.kind", $"is '{kindText}', not \"fixed\" or \"proportional\"");
            }
        }

        private long? OptionalLong(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out var value) || value < 0)
            {
                throw Fail(path, "is not a whole amount of 0 or more, or null");
            }

            return value;
        }

        private void Require(bool ok, string path, string problem)
        {
            if (!ok)
            {
                throw Fail(path, problem);
            }
        }

        private void Forbid(object? value, string path, string problem)
        {
            if (value is not null)
            {
                throw Fail(path, $"is set: {problem}");
            }
        }

        private QuestThing? Thing(Section parent, string name, bool nullable, string? todo)
        {
            var section = nullable ? parent.OptionalObject(name) : parent.Object(name);
            return section is null ? null : ThingOf(section, todo);
        }

        private QuestThing ThingOf(Section section, string? todo)
        {
            var wcidElement = section.Take("wcid");
            uint? wcid = null;
            if (wcidElement.ValueKind == JsonValueKind.Null)
            {
                if (todo is null)
                {
                    throw Fail($"{section.Path}.wcid", "is null without a todo naming why it is not established");
                }
            }
            else if (wcidElement.ValueKind != JsonValueKind.Number || !wcidElement.TryGetUInt32(out var w) || w == 0)
            {
                throw Fail($"{section.Path}.wcid", "is not a weenie class id (a whole number above 0)");
            }
            else
            {
                wcid = w;
            }

            var label = section.Text("name");
            section.End();
            return new QuestThing(wcid, label);
        }

        private QuestPlace Place(Section p)
        {
            var cellText = p.Text("cell");
            if (!cellText.StartsWith("0x", StringComparison.Ordinal)
                || !uint.TryParse(cellText.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var cell)
                || cellText.Length != 10)
            {
                throw Fail($"{p.Path}.cell", $"is '{cellText}', not a cell id (0xLLLLCCCC)");
            }

            Vector3? point = null;
            var pe = p.Take("point");
            if (pe.ValueKind != JsonValueKind.Null)
            {
                if (pe.ValueKind != JsonValueKind.Array || pe.GetArrayLength() != 3 || pe.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                {
                    throw Fail($"{p.Path}.point", "is not [x, y, z] or null");
                }

                var v = pe.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
                point = new Vector3(v[0], v[1], v[2]);
            }

            var name = p.Text("name");
            p.End();
            return new QuestPlace(cell, point, name);
        }

        private QuestExpect Expect(Section e, string? todo)
        {
            var reply = e.OptionalText("reply");
            var stamp = e.OptionalText("stamp");
            var item = Thing(e, "item", nullable: true, todo);
            e.End();
            return new QuestExpect(reply, stamp, item);
        }

        private QuestDone Done(Section d)
        {
            var stamp = d.OptionalText("stamp");
            var reply = d.OptionalText("reply");
            var item = Thing(d, "item", nullable: true, todo: null);
            d.End();
            if (stamp is null && reply is null && item is null)
            {
                throw Fail(d.Path, "names no stamp, reply or item: a quest needs a done condition");
            }

            return new QuestDone(stamp, reply, item);
        }

        private QuestCarry? Carry(JsonElement? e)
        {
            if (e is not { } element || element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var section = new Section(this, element, "$.carry");
            var line = section.Take("line_percent");
            var reserve = section.Take("reserve_burden");
            var always = section.TakeOptional("always");
            section.End();
            if (line.ValueKind != JsonValueKind.Number || !line.TryGetInt32(out var linePercent) || linePercent is < 1 or > 100)
            {
                throw Fail("$.carry.line_percent", "is not a whole percent of the capacity from 1 to 100");
            }

            if (reserve.ValueKind != JsonValueKind.Number || !reserve.TryGetInt32(out var reserveBurden) || reserveBurden < 0)
            {
                throw Fail("$.carry.reserve_burden", "is not a whole burden of 0 or more");
            }

            List<uint>? alwaysWcids = null;
            if (always is { ValueKind: not JsonValueKind.Null } list)
            {
                if (list.ValueKind != JsonValueKind.Array)
                {
                    throw Fail("$.carry.always", "is not a list of wcids");
                }

                alwaysWcids = [];
                foreach (var w in list.EnumerateArray())
                {
                    if (w.ValueKind != JsonValueKind.Number || !w.TryGetUInt32(out var wcid) || wcid == 0)
                    {
                        throw Fail("$.carry.always", "holds an entry that is not a wcid");
                    }

                    alwaysWcids.Add(wcid);
                }
            }

            return new QuestCarry(linePercent, reserveBurden, alwaysWcids);
        }

        /// <summary>M6 audit: the optional <c>timer_from</c>: <c>"turn-in"</c> (the default when absent) or <c>"pickup"</c>.</summary>
        private QuestTimerFrom TimerFrom(JsonElement? e)
        {
            if (e is not { } element)
            {
                return QuestTimerFrom.TurnIn;
            }

            return element.ValueKind == JsonValueKind.String ? element.GetString() switch
            {
                "turn-in" => QuestTimerFrom.TurnIn,
                "pickup" => QuestTimerFrom.Pickup,
                _ => throw Fail("$.timer_from", "is not \"turn-in\" or \"pickup\""),
            } : throw Fail("$.timer_from", "is not \"turn-in\" or \"pickup\"");
        }

        private int? TimerSeconds(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var seconds) || seconds <= 0)
            {
                throw Fail("$.timer_seconds", "is not a whole number of seconds above 0, or null (done once)");
            }

            return seconds;
        }

        private IReadOnlyList<string> Sources(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() == 0)
            {
                throw Fail("$.sources", "is not a non-empty array of strings");
            }

            var list = new List<string>();
            var i = 0;
            foreach (var s in e.EnumerateArray())
            {
                list.Add(Text(s, $"$.sources[{i}]"));
                i++;
            }

            return list.AsReadOnly();
        }
    }
}

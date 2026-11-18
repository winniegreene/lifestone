using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lifestone.ServerMod;

/// <summary>One payer's credits in the bot's ledger: the character's object id, its name when it last paid or asked, and the credits it holds.</summary>
/// <param name="Guid">The character's object id (ACE keeps it across restarts).</param>
/// <param name="Name">The character's name, for the logs.</param>
/// <param name="Credits">The credits held.</param>
internal sealed record BuffBotCredit(uint Guid, string Name, int Credits);

/// <summary>
/// D138 (m6-plan.md): the buff bot's pack, purse and credit ledger, and the small state file that keeps them across a server restart. ACE never saves the bot (a static id from no database row,
/// WorldObjects/WorldObject_Database.cs 103-130 @95ef426d), so what a player would carry in a pack (its Prismatic Tapers and Platinum Scarabs, its pyreals) and the credits its payers hold live here. The file is
/// <c>server/runtime/lifestone-buff-bot.json</c> (gitignored with the rest of the runtime), written whole to a side file and moved over the old one, so a crash mid-write leaves the last good copy.
/// </summary>
/// <remarks>
/// <para><b>The outfit</b> (no file yet): the monarchy's start, <see cref="OutfitTapers"/> tapers, <see cref="OutfitScarabs"/> scarabs and <see cref="OutfitPyreals"/> pyreals: a bot run by a monarchy is set up
/// with a kit, and the free rounds under the fee's level bring it nothing, so the purse pays for their tapers until paid rounds bring scarabs. A paid round brings one scarab, worth 9,000 pyreals at Holtburg's
/// Archmage Cindrue against about 13 tapers (390 pyreals) a round, so the paid rounds carry the free ones.</para>
/// <para><b>A file it cannot read</b> is never replaced: the bot is not placed and the error names the file (the credits in it are the payers' own).</para>
/// </remarks>
internal sealed class BuffBotLedger
{
    /// <summary>The state file's name, in ACE's runtime folder (the parent of the mods folder).</summary>
    public const string FileName = "lifestone-buff-bot.json";

    /// <summary>The file's format.</summary>
    public const int Version = 1;

    /// <summary>The outfit's Prismatic Tapers.</summary>
    public const int OutfitTapers = 500;

    /// <summary>The outfit's Platinum Scarabs: the bot's own keep (<see cref="BuffBotRules.ScarabKeep"/>).</summary>
    public const int OutfitScarabs = 10;

    /// <summary>The outfit's pyreals: the monarchy's float for the free rounds.</summary>
    public const long OutfitPyreals = 50_000;

    private readonly SortedDictionary<uint, BuffBotCredit> _credits = [];

    /// <summary>The Prismatic Tapers in the pack.</summary>
    public int Tapers { get; set; }

    /// <summary>The Platinum Scarabs in the pack (the outfit's, and every one a payer gave).</summary>
    public int Scarabs { get; set; }

    /// <summary>The pyreals in the purse.</summary>
    public long Pyreals { get; set; }

    /// <summary>Every payer's credits, by object id.</summary>
    public IReadOnlyCollection<BuffBotCredit> Payers => _credits.Values;

    /// <summary>The monarchy's outfit (see the class).</summary>
    public static BuffBotLedger Outfit() => new() { Tapers = OutfitTapers, Scarabs = OutfitScarabs, Pyreals = OutfitPyreals };

    /// <summary>The credits <paramref name="guid"/> holds (0 when it never paid).</summary>
    /// <param name="guid">The character's object id.</param>
    public int CreditsOf(uint guid) => _credits.TryGetValue(guid, out var c) ? c.Credits : 0;

    /// <summary>One credit more for <paramref name="guid"/>: the count after it.</summary>
    /// <param name="guid">The character's object id.</param>
    /// <param name="name">Its name.</param>
    public int AddCredit(uint guid, string name)
    {
        var after = CreditsOf(guid) + 1;
        _credits[guid] = new BuffBotCredit(guid, name, after);
        return after;
    }

    /// <summary>Takes one credit of <paramref name="guid"/>, or none when it holds none.</summary>
    /// <param name="guid">The character's object id.</param>
    /// <param name="name">Its name.</param>
    /// <param name="left">The credits left after it (0 when none was taken).</param>
    public bool TakeCredit(uint guid, string name, out int left)
    {
        var held = CreditsOf(guid);
        if (held <= 0)
        {
            left = 0;
            return false;
        }

        left = held - 1;
        _credits[guid] = new BuffBotCredit(guid, name, left);
        return true;
    }

    /// <summary>The file's text: every field, the payers by id.</summary>
    /// <param name="utc">The time written.</param>
    public string ToJson(DateTime utc)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteString("written_utc", utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            w.WriteNumber("tapers", Tapers);
            w.WriteNumber("scarabs", Scarabs);
            w.WriteNumber("pyreals", Pyreals);
            w.WriteStartArray("credits");
            foreach (var c in _credits.Values)
            {
                w.WriteStartObject();
                w.WriteString("guid", "0x" + c.Guid.ToString("X8", CultureInfo.InvariantCulture));
                w.WriteString("name", c.Name);
                w.WriteNumber("credits", c.Credits);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads the file's text; throws <see cref="FormatException"/> naming the field at fault.</summary>
    /// <param name="json">The text.</param>
    public static BuffBotLedger FromJson(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FormatException("not JSON: " + e.Message, e);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the top is not an object");
            }

            var version = Int(root, "version");
            if (version != Version)
            {
                throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"version {version}, not {Version}"));
            }

            var ledger = new BuffBotLedger { Tapers = (int)Int(root, "tapers"), Scarabs = (int)Int(root, "scarabs"), Pyreals = Int(root, "pyreals") };
            if (!root.TryGetProperty("credits", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("credits is not a list");
            }

            foreach (var e in list.EnumerateArray())
            {
                var text = e.TryGetProperty("guid", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString()! : throw new FormatException("a credit has no guid");
                if (!text.StartsWith("0x", StringComparison.Ordinal) || !uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var guid))
                {
                    throw new FormatException("a credit's guid '" + text + "' is not 0x and eight hex digits");
                }

                var name = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : throw new FormatException("the credit of " + text + " has no name");
                var credits = (int)Int(e, "credits");
                if (!ledger._credits.TryAdd(guid, new BuffBotCredit(guid, name, credits)))
                {
                    throw new FormatException("the guid " + text + " is listed twice");
                }
            }

            return ledger;
        }
    }

    /// <summary>The ledger in <paramref name="path"/>, or the outfit when there is no file (<paramref name="outfit"/> true); throws <see cref="FormatException"/> or an I/O error when the file cannot be read.</summary>
    /// <param name="path">The state file.</param>
    /// <param name="outfit">True when the file did not exist.</param>
    public static BuffBotLedger Load(string path, out bool outfit)
    {
        outfit = !File.Exists(path);
        return outfit ? Outfit() : FromJson(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>Writes the ledger to <paramref name="path"/>: whole, to a side file first, then moved over the old one.</summary>
    /// <param name="path">The state file.</param>
    /// <param name="utc">The time written.</param>
    public void Save(string path, DateTime utc)
    {
        var side = path + ".writing";
        File.WriteAllText(side, ToJson(utc), new UTF8Encoding(false));
        File.Move(side, path, overwrite: true);
    }

    /// <summary>One line for the logs: the pack, the purse and the payers.</summary>
    public string Summary() => string.Create(
        CultureInfo.InvariantCulture,
        $"tapers={Tapers} scarabs={Scarabs} pyreals={Pyreals} payers={_credits.Count} credits={_credits.Values.Sum(c => c.Credits)}");

    private static long Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out var n) || n < 0)
        {
            throw new FormatException(name + " is not a whole number of 0 or more");
        }

        return n;
    }
}

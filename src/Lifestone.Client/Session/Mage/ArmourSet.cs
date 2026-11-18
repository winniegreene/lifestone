using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Data.Motion;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// One piece of a quest file's <c>armour</c> block (M6 item 14, the owner: armour instead of the robe). The numbers are the world database's rows for the weenie (read-only SELECT, cited in the block's <c>sources</c>): PropertyInt 4
/// ClothingPriority, 28 ArmorLevel, 5 EncumbranceVal, and the Strength its own lasting spell adds while it is worn (its <c>weenie_properties_spell_book</c> row, the spell's <c>stat_Mod_Val</c>; 0 for none).
/// </summary>
/// <param name="Wcid">The piece's weenie class.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Coverage">Its ClothingPriority (ACE's <c>CoverageMask</c>).</param>
/// <param name="ArmourLevel">Its ArmorLevel.</param>
/// <param name="Burden">Its burden.</param>
/// <param name="Strength">The Strength its own spell adds while worn, 0 for none.</param>
public sealed record ArmourSetPiece(uint Wcid, string Name, uint Coverage, int ArmourLevel, int Burden, int Strength);

/// <summary>
/// The <c>armour</c> block of a quest file (M6 item 14): the armour pieces of the quest's chest that are taken and worn in place of the pieces named by <see cref="InsteadOf"/> (the robe), in the order they are lifted. Data read here, as
/// <see cref="GearReturnTable"/> reads <c>returns</c>; the quest loader skips the block.
/// </summary>
/// <param name="Quest">The quest file's key.</param>
/// <param name="Pieces">The pieces, in the order they are lifted and worn.</param>
/// <param name="InsteadOf">The pick-up pieces of the quest's steps the set replaces: not taken when the set is.</param>
public sealed record ArmourSet(string Quest, IReadOnlyList<ArmourSetPiece> Pieces, IReadOnlyList<uint> InsteadOf)
{
    /// <summary>The set's whole burden.</summary>
    public int Burden => Pieces.Sum(p => p.Burden);

    /// <summary>The parts of the body the set covers.</summary>
    public uint Coverage => Pieces.Aggregate(0u, (a, p) => a | p.Coverage);

    /// <summary>The piece of <paramref name="wcid"/>, or null.</summary>
    /// <param name="wcid">The weenie class.</param>
    public ArmourSetPiece? PieceOf(uint wcid) => Pieces.FirstOrDefault(p => p.Wcid == wcid);

    /// <summary>
    /// The pieces the armour form takes from the chest, in the order they are lifted and worn: the set's, then the quest's own pick-up pieces that are neither of the set nor replaced by it (the trinket).
    /// </summary>
    /// <param name="questPieces">The quest's pick-up pieces, in the file's order.</param>
    public List<QuestThing> PiecesWith(IEnumerable<QuestThing> questPieces)
    {
        ArgumentNullException.ThrowIfNull(questPieces);
        return [.. Pieces.Select(p => new QuestThing(p.Wcid, p.Name)), .. questPieces.Where(p => p.Wcid is { } w && PieceOf(w) is null && !InsteadOf.Contains(w))];
    }

    /// <summary>The set of the quest <paramref name="key"/>, or null when its file has no <c>armour</c> block.</summary>
    /// <param name="key">The quest's key.</param>
    public static ArmourSet? Of(string key) => OfBlock(key, Block);

    /// <summary>The block the Pathwarden station reads as the armour form.</summary>
    public const string Block = "armour";

    /// <summary>
    /// The block the plate set is kept under since the owner's order of 2026-11-12 (leather in place of the plate: <see cref="LeatherSet"/>): data nothing reads as the armour form, so the chest gives the robe again and the stop at the buff bot
    /// before the chest (<see cref="ArmourForm.BotFirst"/>) does not run. Renaming it back to <see cref="Block"/> in the quest file turns the plate form on.
    /// </summary>
    public const string PlateOffBlock = "armour_plate_off";

    /// <summary>The set under <paramref name="block"/> of the quest <paramref name="key"/>'s file, or null when the file has no such block.</summary>
    /// <param name="key">The quest's key.</param>
    /// <param name="block">The block's name (<see cref="Block"/>, <see cref="PlateOffBlock"/>).</param>
    public static ArmourSet? OfBlock(string key, string block)
    {
        ArgumentNullException.ThrowIfNull(key);
        return QuestCatalog.ResourceText(key + ".json") is { } text ? Parse(text, block) : null;
    }

    /// <summary>Reads the <c>armour</c> block of a quest file's text, or null when the file has none; a malformed block throws, naming the field.</summary>
    /// <param name="questJson">The quest file's text.</param>
    /// <param name="blockName">The block's name (<see cref="Block"/> by default).</param>
    public static ArmourSet? Parse(string questJson, string blockName = Block)
    {
        ArgumentNullException.ThrowIfNull(questJson);
        ArgumentNullException.ThrowIfNull(blockName);
        using var doc = JsonDocument.Parse(questJson);
        if (!doc.RootElement.TryGetProperty(blockName, out var block))
        {
            return null;
        }

        var key = doc.RootElement.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : throw Fail("$.key", "is not a text");
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("pieces", out var piecesElement) || piecesElement.ValueKind != JsonValueKind.Array || piecesElement.GetArrayLength() == 0)
        {
            throw Fail("$.armour.pieces", "is not a non-empty array");
        }

        var pieces = new List<ArmourSetPiece>();
        var covered = 0u;
        var i = 0;
        foreach (var p in piecesElement.EnumerateArray())
        {
            var path = $"$.armour.pieces[{i++}]";
            var name = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s ? s : throw Fail(path + ".name", "is not a text");
            var piece = new ArmourSetPiece((uint)Whole(p, "wcid", path, 1), name, (uint)Whole(p, "coverage", path, 1), Whole(p, "armour_level", path, 1), Whole(p, "burden", path, 0), Whole(p, "strength", path, 0));
            if ((piece.Coverage & ~GearCoverage.Body) != 0)
            {
                throw Fail(path + ".coverage", $"0x{piece.Coverage:X} holds a bit outside the outerwear body 0x{GearCoverage.Body:X}");
            }

            if ((piece.Coverage & covered) != 0)
            {
                throw Fail(path + ".coverage", $"0x{piece.Coverage:X} overlaps an earlier piece's (0x{piece.Coverage & covered:X}): ACE would refuse the second (Creature_Equipment.cs:50-58, 93-101 @95ef426d)");
            }

            if (pieces.Any(q => q.Wcid == piece.Wcid))
            {
                throw Fail(path + ".wcid", "names one piece twice");
            }

            covered |= piece.Coverage;
            pieces.Add(piece);
        }

        var insteadOf = new List<uint>();
        if (block.TryGetProperty("instead_of", out var instead))
        {
            if (instead.ValueKind != JsonValueKind.Array)
            {
                throw Fail("$.armour.instead_of", "is not a list of wcids");
            }

            foreach (var w in instead.EnumerateArray())
            {
                insteadOf.Add(w.ValueKind == JsonValueKind.Number && w.TryGetUInt32(out var wcid) && wcid != 0 ? wcid : throw Fail("$.armour.instead_of", "holds an entry that is not a wcid"));
            }
        }

        return new ArmourSet(key, pieces, insteadOf);
    }

    private static int Whole(JsonElement e, string name, string path, int min) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= min ? n : throw Fail($"{path}.{name}", $"is not a whole number of {min} or more");

    private static FormatException Fail(string path, string what) => new($"TODO(m6-armour-file): {path} {what}");
}

/// <summary>Which pieces a quest's chest gives the character.</summary>
public enum ArmourFormKind
{
    /// <summary>The armour set: every piece of the <see cref="ArmourSet"/>, in its order.</summary>
    Armour,

    /// <summary>The quest's own steps (the robe): the form before M6 item 14, and the named fallback when ACE would not let her lift the set.</summary>
    Robe,
}

/// <summary>What the Pathwarden station does about the buff bot before the chest (<see cref="ArmourForm.BotFirst"/>).</summary>
public enum BotFirstKind
{
    /// <summary>The quest names no armour set: nothing.</summary>
    NoSet,

    /// <summary>The stop at the bot does not apply to the run: the chest on her own Strength.</summary>
    Off,

    /// <summary>Her Strength holds the bot's line already: no stop.</summary>
    AlreadyUp,

    /// <summary>The stop runs before the chest.</summary>
    Stop,
}

/// <summary>The plan of <see cref="ArmourForm.BotFirst"/>.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Field">The verdict's <c>bot_first=</c> before the stop's own check after (<c>n/a</c>, <c>off</c>, <c>up</c>; a stop reads <c>failed</c> until its check after says <c>ok</c>).</param>
/// <param name="Why">The decision in one line.</param>
public sealed record BotFirstPlan(BotFirstKind Kind, string Field, string Why);

/// <summary>What <see cref="ArmourForm.Choose"/> decided, with the numbers.</summary>
/// <param name="Kind">The form.</param>
/// <param name="BurdenAfter">The burden once the form's pieces are worn and the replaced pieces shed (the armour form), else the burden asked with.</param>
/// <param name="StrengthAfter">The Strength once the set is worn (its own Strength line counted), else the Strength asked with.</param>
/// <param name="Why">The decision in one line.</param>
public sealed record ArmourFormChoice(ArmourFormKind Kind, int BurdenAfter, int StrengthAfter, string Why)
{
    /// <summary>The capacity at <see cref="StrengthAfter"/>.</summary>
    public int CapacityAfter => EncumbranceSystem.EncumbranceCapacity(StrengthAfter, 0);
}

/// <summary>
/// M6 item 14 (the owner's order of 2026-11-11: armour instead of the robe): whether a quest chest's armour set is taken in place of the robe, and what it weighs. Pure.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> The set is taken whenever ACE lets her lift it. ACE refuses an item that would put the burden over three times the capacity (<c>Player.HasEnoughBurdenToAddToInventory</c>, Player_Inventory.cs:53-76 @95ef426d:
/// <c>EncumbranceVal + burden &lt;= GetEncumbranceCapacity() * 3</c>; the capacity is 150 x the current Strength, <see cref="EncumbranceSystem.EncumbranceCapacity"/>), so the pieces are lifted and worn one at a time in the set's order, the
/// worn pieces they replace shed first, and a piece's own Strength line (the Pathwarden Plate Hauberk casts Strength Self II, +15, on its wearer) raises the capacity for the pieces after it. A set that cannot be lifted that way is not
/// taken: the form is the robe's, named with the piece and the numbers, never a part of the set (a part of the body left bare is worse than the robe, which the armour cannot be worn over).</para>
/// <para><b>The burden is said, not hidden.</b> The run slows above a load of 1 (retail <c>EncumbranceSystem::LoadMod</c>, 0x004FD810 in 11.6096: 1 below a load of 1, falling to 0 at 2; ACE <c>GetBurdenMod</c>), and the same modifier
/// multiplies the two defence skills (ACE <c>Creature.GetEffectiveDefenseSkill</c>, Creature_Combat.cs:480-499). <see cref="LoadText"/> prints the load at her Strength now and at the Strength the buff bot's line gives, so a set worn
/// over the line is logged with its numbers every time.</para>
/// </remarks>
public static class ArmourForm
{
    /// <summary>ACE's limit on what she can lift: this many times the capacity (Player_Inventory.cs:53-76 @95ef426d).</summary>
    public const int LiftLimit = 3;

    /// <summary>The form for <paramref name="set"/>.</summary>
    /// <param name="set">The armour set, or null for a quest with none (the robe form).</param>
    /// <param name="burden">Her burden now (the server's EncumbranceVal).</param>
    /// <param name="shed">The burden of the worn pieces the set replaces, dropped before the first piece is lifted.</param>
    /// <param name="rawStrength">Her Strength with no enchantment (the creation value plus ranks).</param>
    /// <param name="strength">Her Strength now, enchantments in.</param>
    public static ArmourFormChoice Choose(ArmourSet? set, int burden, int shed, int rawStrength, int strength)
    {
        if (set is null)
        {
            return new ArmourFormChoice(ArmourFormKind.Robe, burden, strength, "armour form: the quest file has no armour block: the quest's own pieces (the robe form)");
        }

        var carried = Math.Max(0, burden - shed);
        var now = strength;
        foreach (var piece in set.Pieces)
        {
            var limit = (long)LiftLimit * EncumbranceSystem.EncumbranceCapacity(now, 0);
            if (carried + (long)piece.Burden > limit)
            {
                return new ArmourFormChoice(ArmourFormKind.Robe, burden, strength, string.Create(CultureInfo.InvariantCulture,
                    $"armour form: NOT taken, the robe form by name: the {piece.Name} ({piece.Wcid}) cannot be lifted: burden {carried} + {piece.Burden} is over ACE's limit {limit} ({LiftLimit} x the capacity of Strength {now}); a part of the set would leave the rest of the body bare, which is worse than the robe"));
            }

            carried += piece.Burden;
            now = Math.Max(now, rawStrength + piece.Strength); // one Strength line counts, the strongest (one spell category)
        }

        return new ArmourFormChoice(ArmourFormKind.Armour, carried, now, string.Create(CultureInfo.InvariantCulture,
            $"armour form: the {set.Pieces.Count} pieces of the set (burden {set.Burden}, coverage 0x{set.Coverage:X}) in place of the robe: each can be lifted under ACE's limit of {LiftLimit} x the capacity in the set's order; burden {burden} - {shed} shed + {set.Burden} = {carried}; {LoadText(carried, now)}"));
    }

    /// <summary>
    /// M6 item 14 (the lead's order of 2026-11-11, after the first live run): whether the stop at the buff bot runs before the chest. It does when the quest names an armour set, the stop applies to the run (the option on, the home the
    /// bot's town: <c>JourneyFacts.BuffBotOn</c>) and her Strength does not hold the bot's line yet. The bot stands ten metres from Holtburg's lifestone; with its Strength line (+40) a Strength of 10 lifts 22,500, not 4,500, and she is
    /// never at twice her load between the chest and the bot.
    /// </summary>
    /// <param name="set">The quest's armour set, or null.</param>
    /// <param name="botApplies">True while the stop at the buff bot applies to the run.</param>
    /// <param name="rawStrength">Her Strength with no enchantment.</param>
    /// <param name="strength">Her Strength now.</param>
    public static BotFirstPlan BotFirst(ArmourSet? set, bool botApplies, int rawStrength, int strength)
    {
        if (set is null)
        {
            return new BotFirstPlan(BotFirstKind.NoSet, "n/a", "bot first: the quest file has no armour block: no stop at the buff bot before the chest");
        }

        if (!botApplies)
        {
            return new BotFirstPlan(BotFirstKind.Off, "off", string.Create(CultureInfo.InvariantCulture,
                $"bot first: the stop at the buff bot does not apply to this run (the option --autopilot-buff-bot is off, or the host did not say): the chest is opened on her own Strength {strength} (the capacity {EncumbranceSystem.EncumbranceCapacity(strength, 0)}), the shed first"));
        }

        if (BotStrengthUp(rawStrength, strength))
        {
            return new BotFirstPlan(BotFirstKind.AlreadyUp, "up", string.Create(CultureInfo.InvariantCulture, $"bot first: her Strength {strength} (raw {rawStrength}) holds the bot's line (+{StandingStrength.BotPoints}) already: no stop; the chest follows"));
        }

        return new BotFirstPlan(BotFirstKind.Stop, "failed", string.Create(CultureInfo.InvariantCulture,
            $"bot first: the armour set ({set.Burden} burden) is wanted and the stop at the buff bot applies: the stop runs before the chest: Strength {strength} (raw {rawStrength}, the capacity {EncumbranceSystem.EncumbranceCapacity(strength, 0)}) is to hold the bot's line (+{StandingStrength.BotPoints}: {rawStrength + StandingStrength.BotPoints}, the capacity {EncumbranceSystem.EncumbranceCapacity(rawStrength + StandingStrength.BotPoints, 0)})"));
    }

    /// <summary>True when a Strength of <paramref name="strength"/> over a raw <paramref name="rawStrength"/> holds the buff bot's Strength line (<see cref="StandingStrength.BotPoints"/>) or a stronger one.</summary>
    /// <param name="rawStrength">Her Strength with no enchantment.</param>
    /// <param name="strength">Her Strength now.</param>
    public static bool BotStrengthUp(int rawStrength, int strength) => StandingStrength.BotPoints is var bot and > 0 && strength >= rawStrength + bot;

    /// <summary>The load as a line: <c>burden B of the capacity C (Strength S): load L%, run modifier M</c>, and over 100% the words that say so.</summary>
    /// <param name="burden">The burden.</param>
    /// <param name="strength">The Strength the capacity is read at.</param>
    public static string LoadText(int burden, int strength)
    {
        var capacity = EncumbranceSystem.EncumbranceCapacity(strength, 0);
        var load = capacity > 0 ? (double)burden / capacity : double.PositiveInfinity;
        var mod = EncumbranceSystem.LoadMod((float)load);
        return string.Create(CultureInfo.InvariantCulture, $"burden {burden} of the capacity {capacity} (Strength {strength}): load {load * 100:0}%, run modifier {mod:0.00}{(load > 1 ? " (OVER the line where the run slows)" : string.Empty)}");
    }
}

/// <summary>
/// M6 item 14: the Strength the Strength floor may count beside her own ranks (<see cref="StrengthFloor"/>, <see cref="XpSpendRule"/>). The floor reads the base Strength "because a buff lapses"; two lines do not lapse that way, and the
/// armour set's burden (6,540 for the Pathwarden plate) is carried by them, not by ranks bought before every share:
/// <list type="bullet">
/// <item>a worn piece's own lasting spell (an enchantment with no end, duration below 0: ACE keeps an item's enchantments through a death, <c>EnchantmentManager.RemoveAllEnchantments</c>, Managers/EnchantmentManager.cs:354-365 @95ef426d, "except for vitae and item spells"); and</item>
/// <item>the buff bot's Strength line while the stop at the bot applies to the run (m6-plan.md section 3.1: the line is renewed at home under 45 minutes and a trip home is called under 7; after a death the stop comes before the rebuff), counted whether
/// or not it is in effect at the instant of the read, so a spend between a death and the stop does not buy forty ranks.</item>
/// </list>
/// One Strength line counts, the strongest (ACE keeps the highest power of a spell category in effect, Managers/EnchantmentManager.cs:134-204).
/// </summary>
public static class StandingStrength
{
    /// <summary>ACE's PropertyAttribute Strength.</summary>
    public const uint Strength = 1;

    /// <summary>The bot's Strength line (the <c>Strength Self</c> row of its profiles, <see cref="BuffBotWire.LineNamed"/>: one spell in every profile, and every profile holds it; the Other spell, one id under the Self spell of the same power, whose value the buff table holds).</summary>
    public static int BotPoints
    {
        get
        {
            return BuffBotWire.LineNamed("Strength Self") is { } line && BuffStatMods.BySpell.TryGetValue(line.SpellId + 1u, out var row) && row.IsAttribute && row.Key == Strength ? (int)row.Value : 0;
        }
    }

    /// <summary>The Strength points the floor counts beside the ranks: the lasting lines' (worn pieces' own spells), and the bot's when <paramref name="botApplies"/>; the larger of the two.</summary>
    /// <param name="enchantments">The player's enchantment registry.</param>
    /// <param name="raw">The Strength with no enchantment.</param>
    /// <param name="botApplies">True while the stop at the buff bot applies to the run.</param>
    /// <param name="why">What was counted, for the log.</param>
    public static int Points(ImmutableArray<EnchantmentDesc> enchantments, uint raw, bool botApplies, out string why)
    {
        var lasting = enchantments.IsDefaultOrEmpty ? [] : enchantments.Where(e => e.Duration < 0.0).ToImmutableArray();
        var worn = (int)EnchantmentRules.EnchantAttribute(lasting, Strength, raw) - (int)raw;
        var bot = botApplies ? BotPoints : 0;
        var points = Math.Max(0, Math.Max(worn, bot));
        why = string.Create(CultureInfo.InvariantCulture, $"standing +{points} (the worn pieces' own lasting Strength line {worn:+0;-0;+0}, the buff bot's line {(botApplies ? "+" + bot.ToString(CultureInfo.InvariantCulture) : "not counted: the stop at the bot does not apply")})");
        return points;
    }
}

namespace Lifestone.ServerTools;

/// <summary>One line of a buff bot profile: the level VII spell cast on the teller, and the buff line it stands over.</summary>
/// <param name="SpellId">The spell the bot casts: the Other spell of the line at level VII.</param>
/// <param name="Spell">Its name, for the logs.</param>
/// <param name="Line">The line it covers, by the name the climber's own buff pass gives its Self line (<c>Focus Self</c>, <c>Armor Self</c>); for a line she does not hold, the dat's name of the Self line (<c>Sprint Self</c>).</param>
public sealed record BuffBotLine(uint SpellId, string Spell, string Line);

/// <summary>A buff bot profile: the words that ask for it and the lines it casts, in the order cast.</summary>
/// <param name="Key">The profile's key (its first word).</param>
/// <param name="Words">Every word that asks for it; a tell matches a word whole, ignoring case.</param>
/// <param name="Purpose">What the profile is for, as the bot's two lines say it after "spells for" (<c>the tuskers</c>, <c>every ground</c>).</param>
/// <param name="Lines">The lines, in the order cast.</param>
public sealed record BuffBotProfile(string Key, IReadOnlyList<string> Words, string Purpose, IReadOnlyList<BuffBotLine> Lines);

/// <summary>
/// M6 item 14 (m6-plan.md section 3.1, D124-D127): what the buff bot is, shared by the server mod, which places it, and the client, whose autopilot stops at it. In retail a buff bot was another player's
/// character run by a script: a tell with a word, and it cast a set of buffs on the teller. Lifestone has one player, so the bot is a mage the mod places by Holtburg's lifestone.
/// </summary>
/// <remarks>
/// <para><b>How it hears.</b> ACE sends a tell by name to online players only (Network/GameAction/Actions/GameActionTell.cs 27-34 @95ef426d), so the bot is told by its object id: the direct talk
/// (GameActionTalkDirect.cs 21-50), which runs the creature's own answer to the text. The word is the whole text.</para>
/// <para><b>Paid, and a player's rules (D138, an owner ruling, 2026-11-14).</b> From <see cref="FeeLevel"/> a round costs one credit; a credit is one <see cref="FeeName"/> handed to the bot by a player's give (ACE takes one of a
/// stack: <see cref="Credits"/> answers). The bot casts as a player does: from its own mana, with a fizzle chance from its own skill, burning the scarab and the Prismatic Tapers of its foci formula from its own pack; under
/// 100 tapers, after a round and ten seconds, it says <see cref="AwayAnnouncement"/>, walks to Archmage Cindrue's shop, buys and walks back (<see cref="BackAnnouncement"/>). Every line both sides read is here.</para>
/// <para><b>What it casts (D127).</b> Only what one player can cast on another: the Other spell of each line at level VII, each for the spell's own hour. An Impenetrability or a bane cast at another
/// player lands on a shield and on nothing else, so the bot casts none and the climber's armour lines stay her own.</para>
/// <para><b>The ids.</b> Each line's two level VII spells sit side by side in the spell table; the higher id is the Self spell the climber's own profile lists (ArenaProfile), the lower the Other spell.
/// Run and Jump (Sprint, Jumping Mastery) are lines the climber does not hold: their Self spells (2301, 2257) are the dat's alone.
/// <c>BuffBotSpellsDatTests</c> proves each from the dat: not self-targeted, the line's category, power 300, an hour, not resistable.</para>
/// <para><b>Two profiles (an owner ruling, 2026-11-12).</b> <see cref="Tusker"/> holds one protection, the tuskers' (Bludgeoning). <see cref="All"/> is every line of it, in its order, and then the six other elemental
/// protections, Magic Resistance, Impregnability and Arcane Enlightenment: the profile for a ground where every damage type is met. It is the one the client asks for by default (<see cref="Default"/>); the tuskers'
/// stays, by its word. A line has one spell in every profile that holds it (<see cref="LineNamed"/>), and every profile begins with the tuskers' lines, so whatever reads a line of the tuskers' finds it in each.
/// A profile by ground and by element is still <c>TODO(m6-buffbot-profiles)</c>.</para>
/// </remarks>
public static class BuffBotWire
{
    /// <summary>The bot's name. A placeholder until the owner names it (m6-plan.md, item 14, slice S4).</summary>
    public const string Name = "Koji the Enchanter";

    /// <summary>
    /// The bot's object id: a static id of Holtburg's landblock (0x7A9B4xxx) that the world database does not use (its instances there end at 0x7A9B46B1; read 2026-11-11). ACE never saves a static object
    /// that did not come from the database (WorldObjects/WorldObject_Database.cs 103-130), so the bot is in no save.
    /// </summary>
    public const uint Guid = 0x7A9B4F14;

    /// <summary>Holtburg's landblock.</summary>
    public const ushort Landblock = 0xA9B4;

    /// <summary>The outdoor cell the bot stands in: the lifestone's.</summary>
    public const uint Cell = 0xA9B40019;

    /// <summary>
    /// Where the bot stands, in the landblock's metres: west-south-west of the lifestone (81.3, 11.8), ten metres from the lifestone point (84, 7.7) every pass at home walks to, south of the line to the
    /// town and the Network portal, and away from Alcott (83.3, 14.0) and the Pathwarden's corner to the north-east. The mod sets the height from the terrain.
    /// </summary>
    public const float X = 74.5f;

    /// <summary>See <see cref="X"/>.</summary>
    public const float Y = 6.0f;

    /// <summary>The height asked for before the terrain's is read (the hilltop's).</summary>
    public const float Z = 94.0f;

    /// <summary>How near the autopilot stands before it tells the bot, in metres (a caster's reach to spare).</summary>
    public const double ReachMetres = 5.0;

    /// <summary>The word that lists the profiles.</summary>
    public const string HelpWord = "help";

    /// <summary>The tuskers' profile: every attribute, every mastery the mage uses, Invulnerability, Armor, Bludgeoning Protection, the three regenerations, and Run and Jump.</summary>
    public static BuffBotProfile Tusker { get; } = new(
        "tusker",
        ["tusker", "tuskers"],
        "the tuskers",
        [
            new(2066, "Calming Gaze", "Focus Self"),
            new(2090, "Bolstered Will", "Willpower Self"),
            new(2086, "Might of the 5 Mules", "Strength Self"),
            new(2060, "Temeritous Touch", "Endurance Self"),
            new(2058, "Boon of Refinement", "Coordination Self"),
            new(2080, "Ogfoot", "Quickness Self"),
            new(2214, "Adja's Boon", "Creature Enchantment Mastery Self"),
            new(2266, "Harlune's Boon", "Life Magic Mastery Self"),
            new(2322, "Hieromancer's Boon", "War Magic Mastery Self"),
            new(2248, "Celcynd's Boon", "Item Enchantment Mastery Self"),
            new(2286, "Nuhmudira's Boon", "Mana Mastery Self"),
            new(2244, "Web of Defense", "Invulnerability Self"),
            new(2052, "Executor's Boon", "Armor Self"),
            new(2152, "Boon of the Mace Turner", "Bludgeon Protection Self"),
            new(2184, "Hydra's Head", "Regeneration Self"),
            new(2186, "Tenaciousness", "Rejuvenation Self"),
            new(2182, "Battlemage's Boon", "Mana Renewal Self"),
            new(2300, "Saladur's Boon", "Sprint Self"), // the owner, 2026-11-11: the bot buffs Run and Jump (she holds neither line herself: the names are the Self spells' lines)
            new(2256, "Jahannan's Boon", "Jumping Mastery Self"),
        ]);

    /// <summary>
    /// Every buff a level VII bot casts on a mage (the owner, 2026-11-12: a run through the Direlands at level 40 meets every damage type, and a creature with piercing and lightning attacks killed her in 50 seconds
    /// under the tuskers' one protection): every line of <see cref="Tusker"/>, in its order, then the six other elemental protections, Magic Resistance (Magic Defense), Impregnability (Missile Defense) and
    /// Arcane Enlightenment (Arcane Lore): 28 lines.
    /// </summary>
    /// <remarks>
    /// <para><b>The ids</b> are the world copy's spell table's (ACE-World-Database v0.9.296, read 2026-11-12): each line's Other spell at level VII, one id under its Self spell, as the tuskers' are. The protections'
    /// Self spells are 2151 Blessing of the Blade Turner, 2161 Blessing of the Arrow Turner, 2157 Fiery Blessing, 2155 Icy Blessing, 2149 Caustic Blessing and 2159 Storm's Blessing (each leaves 0.35 of a hit of
    /// its element, as Bludgeoning's does); the three skills' are 2281 Aura of Resistance, 2243 Aura of Deflection and 2195 Aliester's Blessing (+40 each). She holds no line of the six protections, so each goes by
    /// the dat's name of its Self line; the three skill lines are lines of her own profile (<c>ArenaProfile</c>), by its names, which no scrivener of the catalog sells her. Arcane Enlightenment is in because a rule
    /// of hers reads Arcane Lore (<c>SelfEquip</c>: an item's spells need it to light). <c>BuffBotSpellsDatTests</c> proves each from the dat.</para>
    /// <para><b>Left out, by name.</b> Mana Conversion Mastery is not left out: it is the tuskers' <c>Mana Mastery Self</c> line (2286). Deception Mastery (2226 Ketnan's Boon): no rule of hers reads Deception, which
    /// only hides her from another's assessment. Healing, Leadership, Fealty, the two assessment skills, the weapon and the trade skills: not a mage's. Impenetrability and the banes: only a shield takes them from
    /// another's hand (D127), so her armour lines stay her own.</para>
    /// </remarks>
    public static BuffBotProfile All { get; } = new(
        "all",
        ["all"],
        "every ground",
        [
            .. Tusker.Lines,
            new(2150, "Boon of the Blade Turner", "Blade Protection Self"),
            new(2160, "Boon of the Arrow Turner", "Piercing Protection Self"),
            new(2156, "Fiery Boon", "Fire Protection Self"),
            new(2154, "Icy Boon", "Cold Protection Self"),
            new(2148, "Caustic Boon", "Acid Protection Self"),
            new(2158, "Storm's Boon", "Lightning Protection Self"),
            new(2280, "Web of Resistance", "Magic Resistance Self"),
            new(2242, "Web of Deflection", "Impregnability Self"),
            new(2194, "Aliester's Boon", "Arcane Enlightenment Self"),
        ]);

    /// <summary>
    /// M6 14 (the owner, 2026-11-14: a word for the Phyntos Menace's wasps): every line of <see cref="Tusker"/>, in its order, then the protections the wasps call for, read from the world copy (ACE-World-Database v0.9.296,
    /// read-only, 2026-11-14): the White Phyntos Wasp (weenie 7105) bites with Pierce and stings with Slash (weenie_properties_body_part d_Type 2 and 1, d_Val 25) and casts Frost Bolt IV and Frost Volley IV (Cold); the
    /// dungeon's twenty Sewer hotspots (4979) burn Acid; then Magic Resistance against their spells and Arcane Enlightenment, which keeps her items' spells lit (<c>SelfEquip</c> reads Arcane Lore): 25 lines, the three of
    /// <see cref="All"/> it leaves out being Fire and Lightning Protection and Impregnability, which nothing there calls for.
    /// </summary>
    public static BuffBotProfile Wasp { get; } = new(
        "wasp",
        ["wasp", "wasps", "phyntos"],
        "the wasps",
        [
            .. Tusker.Lines,
            new(2150, "Boon of the Blade Turner", "Blade Protection Self"),
            new(2160, "Boon of the Arrow Turner", "Piercing Protection Self"),
            new(2154, "Icy Boon", "Cold Protection Self"),
            new(2148, "Caustic Boon", "Acid Protection Self"),
            new(2280, "Web of Resistance", "Magic Resistance Self"),
            new(2194, "Aliester's Boon", "Arcane Enlightenment Self"),
        ]);

    /// <summary>Every profile, in the order the bot's answers are matched. A profile chosen by ground and by element is <c>TODO(m6-buffbot-profiles)</c>: the client still says <see cref="Default"/>.</summary>
    public static IReadOnlyList<BuffBotProfile> Profiles { get; } = [Tusker, All, Wasp];

    /// <summary>The profile the client asks for when no word is named (the owner, 2026-11-12: she says <c>all</c>): <see cref="All"/>.</summary>
    public static BuffBotProfile Default => All;

    /// <summary>The bot's line that stands over the Self line named <paramref name="line"/>, or null when no profile holds one. A line has one spell in every profile that holds it (pinned by <c>BuffBotRulesTests</c>).</summary>
    /// <param name="line">The Self line's name, as a profile's lines name it.</param>
    public static BuffBotLine? LineNamed(string line)
    {
        foreach (var profile in Profiles)
        {
            foreach (var held in profile.Lines)
            {
                if (string.Equals(held.Line, line, StringComparison.Ordinal))
                {
                    return held;
                }
            }
        }

        return null;
    }

    /// <summary>The profile a word asks for, or null: the word whole, ignoring case and the spaces at its ends.</summary>
    /// <param name="word">The tell's text.</param>
    public static BuffBotProfile? ProfileOf(string? word)
    {
        var trimmed = word?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        foreach (var profile in Profiles)
        {
            foreach (var known in profile.Words)
            {
                if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }
        }

        return null;
    }

    /// <summary>What the bot tells the teller before a profile's first cast.</summary>
    /// <param name="profile">The profile.</param>
    public static string Begins(BuffBotProfile profile) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Stand by me: {profile.Lines.Count} spells for {profile.Purpose}, an hour each.");

    /// <summary>What the bot tells the teller after a profile's last cast: the station's sign that the bot is done. Each profile's line is its own (the count and the purpose), so one profile's sign is never read as another's.</summary>
    /// <param name="profile">The profile.</param>
    public static string Done(BuffBotProfile profile) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Done: {profile.Lines.Count} spells for {profile.Purpose}. Come back within the hour.");

    /// <summary>How <see cref="Help"/> begins, whatever words follow: the client's sign that the bot did not know the word it was told (a server mod installed before the word was added answers so, and casts nothing).</summary>
    public const string HelpStart = "Tell me one of these words and I will buff you: ";

    /// <summary>What the bot answers to <see cref="HelpWord"/> and to any word it does not know: its words, the fee (D138) and the credit word.</summary>
    public static string Help { get; } =
        HelpStart + string.Join(", ", Profiles.SelectMany(p => p.Words)) + ". " + FeeLine + " I do not cast on armour: only a shield takes a bane from another's hand.";

    // ------------------------------------------------------------------------------------------------ D138: the fee, the credits, the away and rest lines

    /// <summary>
    /// D138 (the owner, 2026-11-14: a paid bot, as retail's monarchy bots were): the level from which a round costs one credit. Below it a round is free. The level is the teller's own, read by the mod from ACE when
    /// the word is told (<c>Player.Level</c>), so the client's reading of it only decides whether to ask for its credits first.
    /// </summary>
    public const int FeeLevel = 60;

    /// <summary>The ware a credit is bought with: the Platinum Scarab (weenie 8897), the scarab of the bot's level VII formula, so a scarab given feeds the bot's own casting.</summary>
    public const uint FeeWcid = 8897;

    /// <summary>The fee ware's name, as ACE's give line prints it.</summary>
    public const string FeeName = "Platinum Scarab";

    /// <summary>The word that asks the bot for the teller's credit count (answered with <see cref="Credits"/>).</summary>
    public const string CreditWord = "credits";

    /// <summary>The fee, as the help line says it (computed when read: <see cref="Help"/> is set before this part of the class).</summary>
    public static string FeeLine => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"From level {FeeLevel} a round costs a credit: hand me a {FeeName} for each. Tell me {CreditWord} for your count.");

    /// <summary>The bot's answer to a give of the fee ware and to <see cref="CreditWord"/>: the teller's credit count (<c>You have 1 credit.</c>, <c>You have 0 credits.</c>).</summary>
    /// <param name="credits">The count.</param>
    public static string Credits(int credits) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"You have {credits} credit{(credits == 1 ? string.Empty : "s")}.");

    /// <summary>Reads <see cref="Credits"/>: the whole text, nothing before or after it.</summary>
    /// <param name="text">A line's text.</param>
    /// <param name="credits">The count read, or 0.</param>
    public static bool TryReadCredits(string? text, out int credits)
    {
        credits = 0;
        const string Start = "You have ";
        var t = text?.Trim();
        if (t is null || !t.StartsWith(Start, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = t[Start.Length..];
        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || !int.TryParse(rest[..space], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return false;
        }

        if (!string.Equals(t, Credits(n), StringComparison.Ordinal))
        {
            return false;
        }

        credits = n;
        return true;
    }

    /// <summary>What the bot tells a teller at or over <see cref="FeeLevel"/> who holds no credit, and casts nothing.</summary>
    public static string NeedCredit { get; } = $"You need a credit: hand me a {FeeName}.";

    /// <summary>What the bot tells a payer when a round takes a credit, before its first line (<see cref="Begins"/>).</summary>
    /// <param name="left">The credits left after this round's.</param>
    public static string Spent(int left) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"One credit spent, {left} left.");

    /// <summary>The bot's say in local chat (white, everyone near hears it) when it leaves for tapers: the round before it ended, ten seconds have passed (D138). The client reads it as "the bot is away".</summary>
    public const string AwayAnnouncement = "I am out of tapers. Back shortly.";

    /// <summary>The bot's say when it stands at its post again after the shop: the client's sign that the bot is back.</summary>
    public const string BackAnnouncement = "I am back. Tell me your word.";

    /// <summary>What the bot tells a teller while it is away (or about to leave) for tapers; the word is answered when it is back.</summary>
    public const string AwayTell = "I am away for tapers: I will answer you when I am back.";

    /// <summary>What the bot tells a teller while a round runs; nothing is queued for the same teller.</summary>
    public const string Busy = "I am casting a round now: wait for my done line.";

    /// <summary>How <see cref="Rest"/> begins.</summary>
    public const string RestStart = "My mana is low: I rest ";

    /// <summary>
    /// What the bot tells the payer when its mana does not pay the next cast and its dance cannot run (the owner, 2026-11-17: a caster draws on its stamina first, <see cref="Dance"/>): the seconds it rests (ACE
    /// regenerates its mana), then it goes on, and why it rests when it says so.
    /// </summary>
    /// <param name="seconds">The seconds.</param>
    /// <param name="why">Why the dance cannot run, a sentence in the bot's voice (<c>My stamina is spent.</c>), or null.</param>
    public static string Rest(int seconds, string? why = null) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{RestStart}{seconds} seconds, then I go on.{(string.IsNullOrWhiteSpace(why) ? string.Empty : " " + why.Trim())}");

    /// <summary>Reads <see cref="Rest"/>: the seconds told (with or without its reason).</summary>
    /// <param name="text">A line's text.</param>
    /// <param name="seconds">The seconds read, or 0.</param>
    public static bool TryReadRest(string? text, out int seconds)
    {
        seconds = 0;
        var t = text?.Trim();
        if (t is null || !t.StartsWith(RestStart, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = t[RestStart.Length..];
        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || !int.TryParse(rest[..space], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seconds))
        {
            seconds = 0;
            return false;
        }

        var bare = Rest(seconds);
        if (string.Equals(t, bare, StringComparison.Ordinal) || (t.Length > bare.Length + 1 && t.StartsWith(bare + " ", StringComparison.Ordinal)))
        {
            return true;
        }

        seconds = 0;
        return false;
    }

    /// <summary>What the bot tells the payer when its mana is short of the next cast and it recovers it by the stamina-to-mana dance (once a short, however many casts it takes).</summary>
    public const string Dance = "My mana is low: I draw on my stamina.";

    /// <summary>What the bot tells the payer when a round waits on its own buffs (a self line lapsed or near its lapse; between rounds it renews them unheard).</summary>
    public const string Renew = "A moment: I renew my own spells.";

    /// <summary>How <see cref="OutOf"/> begins (not "I am out of", which is <see cref="AwayAnnouncement"/>'s start).</summary>
    public const string OutOfStart = "I have no more ";

    /// <summary>What the bot tells the payer when its pack does not hold the components of the next cast: it stops the round there, and a credit the round took is given back.</summary>
    /// <param name="component">The component's plural name (<c>Prismatic Tapers</c>).</param>
    public static string OutOf(string component) => $"{OutOfStart}{component}: I stop here and your credit is returned.";

    /// <summary>What the bot tells the payer when the teller is past the spell's reach (a player's range rule): it stops the round there, and a credit the round took is given back.</summary>
    public const string OutOfReach = "You are out of my reach: I stop here and your credit is returned.";
}

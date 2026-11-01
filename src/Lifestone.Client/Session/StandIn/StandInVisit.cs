using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.State;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>One teleport of a <see cref="StandInVisit"/>.</summary>
/// <param name="After">Seconds after the visit's unhide (session clock).</param>
/// <param name="Cell">The destination cell.</param>
/// <param name="Origin">The destination's local position.</param>
public readonly record struct StandInTeleport(double After, uint Cell, Vector3 Origin);

/// <summary>One object the server places beside the player in a <see cref="StandInVisit"/>.</summary>
/// <param name="Guid">The object id (a server object's: 0x80000000 and up).</param>
/// <param name="Name">The object's name.</param>
/// <param name="Offset">Where it stands, from the player's creation position (AC axes, metres).</param>
/// <param name="Setup">Its Setup (the human's by default: the base body, a placeholder look).</param>
/// <param name="MotionTable">Its motion table (the human's), or 0 for none.</param>
/// <param name="PortalTo">Where a Use of this object takes the player (a stand-in portal; answered only when the host opted in with <see cref="StandInSessions.AnswerWorld"/>), or null for an object that is not a portal.</param>
/// <param name="DescriptionFlags">Object description bits the object's weenie carries besides the portal bit a portal gets (g3b: 0x10 makes an attackable creature, a gold radar blip).</param>
/// <param name="BlipColor">The server's radar blip colour byte (1 to 10), or 0 to send none.</param>
/// <param name="UnhideAfter">Null for an object created in plain sight. Otherwise the server creates it Hidden (with the human's script table, as a real server does for an object it materialises: the Hidden script's bubbles and
/// the UnHide script's fade) and clears the bit this many seconds after the player's unhide (D63: the materialise fade of an object, drawn by the same path as the player's).</param>
/// <param name="WeenieClassId">The weenie class id the object's description carries (2 for the stand-in's own placeholder objects).</param>
public readonly record struct StandInObject(
    uint Guid,
    string Name,
    Vector3 Offset,
    uint Setup = StandInVisit.HumanSetup,
    uint MotionTable = StandInVisit.HumanMotionTable,
    StandInTeleport? PortalTo = null,
    uint DescriptionFlags = 0,
    byte BlipColor = 0,
    double? UnhideAfter = null,
    uint WeenieClassId = 2);

/// <summary>
/// The stand-in's lifestone (M3 3.2 db): an object whose description flags carry <c>BF_LIFESTONE</c> (0x4000, <see cref="Contracts.WorldObjectTable.LifestoneFlag"/>). A Use of it binds the player (<c>StandInWorld.Death.cs</c>: the Sanctuary
/// motion, a Magic chat line, the stand-in's sanctuary set to the player's position, half the stamina; ACE <c>Lifestone.cs</c> <c>ActOnUse</c> 45-75). The name is Lifestone-authored and the look is a placeholder (the human Setup):
/// the stand-in has no weenie database (<c>TODO(m3-standin-lifestone-weenie)</c>).
/// </summary>
public static class StandInLifestone
{
    /// <summary>The name of the stand-in's lifestone (Lifestone-authored; ACE's weenie is called "Lifestone").</summary>
    public const string Name = "Stand-in Lifestone";

    /// <summary>A lifestone for a placement (<see cref="StandInServer.Place"/>): <see cref="Contracts.WorldObjectTable.LifestoneFlag"/> in its description flags, on the human Setup with no motion table.</summary>
    /// <param name="guid">The object id.</param>
    public static StandInObject Make(uint guid) => new(guid, Name, default, StandInVisit.HumanSetup, 0, DescriptionFlags: Contracts.WorldObjectTable.LifestoneFlag, WeenieClassId: 2);
}

/// <summary>A weenie the stand-in knows for the server mod's <c>item</c> request (a placeholder: the stand-in has no weenie database; <c>TODO(m3-standin-weenies)</c>).</summary>
/// <param name="Name">The item's name.</param>
/// <param name="MaxStack">The most one stack holds, or null for an item that does not stack.</param>
/// <param name="Creatable">False for a weenie the server never spawns into a pack (answered <c>not-creatable</c>).</param>
public sealed record StandInItemKind(string Name, int? MaxStack, bool Creatable = true)
{
    /// <summary>ACE's <c>coinstack</c> (273), the one weenie the stand-in knows by default: Pyreal, stacking to 25,000.</summary>
    public const uint PyrealWcid = 273;

    /// <summary>What the stand-in knows when the visit names no weenies.</summary>
    public static IReadOnlyDictionary<uint, StandInItemKind> Default { get; } = new Dictionary<uint, StandInItemKind> { [PyrealWcid] = new("Pyreal", 25_000) };
}

/// <summary>A line someone nearby says in a scripted visit (a HearSpeech, text type 2).</summary>
/// <param name="After">Seconds after the player is unhidden.</param>
/// <param name="Speaker">The speaker's name.</param>
/// <param name="SpeakerGuid">The speaker's object id.</param>
/// <param name="Text">What is said.</param>
public readonly record struct StandInSpeech(double After, string Speaker, uint SpeakerGuid, string Text);

/// <summary>An F755 PlayEffect the stand-in server sends (M3 1.3 s0): ACE's <c>GameMessageScript</c> (a level-up, a buff, a spell effect) for an object.</summary>
/// <param name="After">Seconds after the visit's unhide (session clock).</param>
/// <param name="Guid">The object the script plays on; 0 is the player.</param>
/// <param name="ScriptType">The script type (retail <c>PScriptType</c>), resolved through the object's PhysicsScriptTable.</param>
/// <param name="Speed">The intensity (retail's <c>mod</c>; ACE sends 1 by default).</param>
public readonly record struct StandInScript(double After, uint Guid, uint ScriptType, float Speed);

/// <summary>An F750 Sound the stand-in server sends (M3 1.3 s0): ACE's <c>GameMessageSound</c> for an object.</summary>
/// <param name="After">Seconds after the visit's unhide (session clock).</param>
/// <param name="Guid">The object the sound plays on; 0 is the player.</param>
/// <param name="SoundType">The sound type (retail <c>SoundType</c>), looked up in the object's sound table.</param>
/// <param name="Volume">The volume, 0 to 1.</param>
public readonly record struct StandInSound(double After, uint Guid, uint SoundType, float Volume);

/// <summary>
/// A scripted visit of the world, as the server would send it, for the play-mode checks (<c>--play-checks</c>) that run without a server: the player's
/// F746 and CreateObject (hidden, in the cell named), the unhide (a SetState without Hidden, which a real server sends after LoginComplete), teleports
/// (F751 then the new position with a newer teleport sequence) and the logoff (the phases back to the character list and F658). Everything goes through
/// the session's own reducer, so the events are the ones a live session publishes; nothing here is retail's or the dats'. The player is the
/// first stand-in character (<see cref="StandInSessions.FirstCharacterGuid"/>) on the human Setup and motion table, with no description of its own (the
/// base body: a placeholder look).
/// </summary>
public sealed record StandInVisit
{
    /// <summary>The human Setup (the walker's collision is built from it).</summary>
    public const uint HumanSetup = 0x02000001;

    /// <summary>The human motion table.</summary>
    public const uint HumanMotionTable = 0x09000001;

    /// <summary>
    /// The human's PhysicsScriptTable (PeTable), which ACE puts in a player's PhysicsDesc: its Hidden and UnHide scripts are the bubbles round a character that is
    /// arriving (docs/avatars.md section 10.2).
    /// </summary>
    public const uint HumanScriptTable = 0x34000004;

    /// <summary>
    /// The human's SoundTable (STable), which ACE puts in a player's PhysicsDesc: the table the sound types of the sounds the server plays on the player (F750) are looked up in. The Setup names none
    /// (its default sound table is 0), so the stand-in sends this one as the server does.
    /// </summary>
    public const uint HumanSoundTable = 0x20000001;

    /// <summary>The cell the player is created in: Holtburg's town drop point (an outdoor cell).</summary>
    public uint Cell { get; init; } = 0xA9B40019;

    /// <summary>The player's local position in <see cref="Cell"/>.</summary>
    public Vector3 Origin { get; init; } = new(84f, 7.1f, 94f);

    /// <summary>
    /// The objects the server creates right after the player, standing near it where it is created: creatures on the base body with a placeholder look. None by
    /// default.
    /// </summary>
    public ImmutableArray<StandInObject> Objects { get; init; } = [];

    /// <summary>Seconds after the character list when the player enters the world.</summary>
    public double EnterAfter { get; init; } = 0.5;

    /// <summary>
    /// True when the visit does not start by itself: the host calls <see cref="StandInServer.Release"/> (the checks release it when the retail character list is on the screen, so the server
    /// answers as fast as a live one does, about 0.1 s after the list shows). <see cref="EnterAfter"/> is then the seconds from the release.
    /// </summary>
    public bool HoldEntry { get; init; }

    /// <summary>Seconds after the player's creation when the server unhides it (a real server does after LoginComplete, once portal space has played).</summary>
    public double UnhideAfter { get; init; } = 9.0;

    /// <summary>What others say after the unhide (speech the chat windows show). None by default.</summary>
    public ImmutableArray<StandInSpeech> Speech { get; init; } = [];

    /// <summary>Server questions (GameEvent 0x0274) asked <see cref="StandInConfirmation.After"/> seconds after the unhide (M5 6 k1); the client's answer is recorded and an unanswered one is closed by a 0x0276 after ACE's 30 s.</summary>
    public ImmutableArray<StandInConfirmation> Confirmations { get; init; } = [];

    /// <summary>The teleports, in order.</summary>
    public ImmutableArray<StandInTeleport> Teleports { get; init; } = [];

    /// <summary>
    /// The player's PlayerDescription, sent as the player is created (what a server sends at login): attributes, vitals and the character's option bits. Lifestone-authored
    /// numbers (placeholders, not any character's); null sends none (the qualities stay unknown).
    /// </summary>
    public StandInPlayerStats? Stats { get; init; } = new();

    /// <summary>
    /// The XpTable (0x0E000018) the stand-in prices a raise by, as ACE does (<c>Player.CalcAttributeRank</c> and its siblings read the dat's lists): the host loads it from the dats
    /// (<c>XpTableData.Load</c>; settable because the play checks load it once the dats are open, after the visit exists). Null: a raise is answered with the refusal line plus <c>TODO(m3-standin-xp-table)</c> (the stand-in does not guess a price).
    /// </summary>
    public XpTableData? XpTable { get; set; }

    /// <summary>
    /// The SpellTable (0x0E00000E) the stand-in answers <c>spell learn</c> by (an id the table does not hold is unknown, as the mod answers by ACE's table): the host loads it from the dats
    /// (<c>SpellTableData.Load</c>; settable like <see cref="XpTable"/>). Null: a fixed placeholder rule (<c>TODO(m3-standin-spell-table)</c>, see <see cref="StandInWorld.PlaceholderMaxSpellId"/>).
    /// </summary>
    public SpellTableData? SpellTable { get; set; }

    /// <summary>
    /// The plan of a spell's cast (M3 2.1 s2a): the words, the wind-up and cast gestures with ACE's timings, the effect's moment (<see cref="StandInCastPlanner.Plan"/>, over the dats' SpellComponentTable and the player's motion table). The host
    /// sets it once the dats are open (settable like <see cref="XpTable"/>). Null: a cast is its effect and ACE's UseDone with no words, gestures or scripts (<c>TODO(m3-standin-cast-plan)</c>, logged once).
    /// </summary>
    public Func<SpellInfo, CastPlan?>? CastPlanner { get; set; }

    /// <summary>
    /// M3 3.1 fb (<c>TODO(m3-standin-cast-plan-lazy)</c> closed): makes the planner when a cast finds <see cref="CastPlanner"/> still null (the host's worker is not done, or no host made one), once, on the casting thread. The planner
    /// then plans any spell the stand-in knows when it is cast (<see cref="StandInCastPlanner.Plan"/> caches by spell id and is thread-safe), not only the ones a host planned ahead. Null: a cast with no planner plays none, as before.
    /// </summary>
    public Func<StandInCastPlanner>? CastPlannerFactory { get; set; }

    /// <summary>
    /// The Setup of the projectile a war spell launches (the ACE world extract the visuals use; null: not known). The stand-in dresses its projectile placeholder with it, so the cast tracker, which
    /// takes a projectile only when it wears the spell's Setup (M3 1.2 fix R2), attributes it as it would ACE's; with none the placeholder wears a small human Setup and a host that gave the
    /// Action API the Setups would refuse it (<c>TODO(m3-standin-projectile-model)</c>).
    /// </summary>
    public Func<uint, uint?>? ProjectileSetup { get; set; }

    /// <summary>
    /// The ModelData of the stand-in's player and of every human-Setup object that has no look of its own in <see cref="HumanLooks"/> (M3 4.2 p6): a real server sends a human's clothing, palettes and
    /// texture changes in its CreateObject, and the base body is what draws without them. Empty (the default) sends the base body, the bare figure the Holtburg frames showed;
    /// the host composes a look from the dats (<c>TODO(m3-standin-human-look)</c>: every garment is a placeholder).
    /// </summary>
    public ModelData HumanModel { get; set; } = ModelData.Empty;

    /// <summary>Looks by object name for the stand-in's placeholder humans (a role: guard, vendor); a name not held wears <see cref="HumanModel"/>.</summary>
    public IReadOnlyDictionary<string, ModelData>? HumanLooks { get; set; }

    /// <summary>
    /// Dresses the player in the pieces it wears and gives a created piece its own ObjDesc (M3 4.2 p6 follow-up): with it the stand-in sends an ObjDescEvent (F625) on a wield, an unwield and a lost wielded item, as ACE does
    /// (<c>Creature_Equipment.cs</c> 365, 438). Null: the player keeps the look it was created with (<c>TODO(m3-standin-objdesc-on-wield)</c>).
    /// </summary>
    public IStandInDresser? Dresser { get; set; }

    /// <summary>Sets <see cref="HumanModel"/> and <see cref="HumanLooks"/> from ObjDescs (a host that may not name <c>Lifestone.Net</c> composes them from the dats).</summary>
    public void SetHumanLooks(Lifestone.Data.Appearance.ObjDesc player, IEnumerable<KeyValuePair<string, Lifestone.Data.Appearance.ObjDesc>> roles)
    {
        HumanModel = StandInHumanLook.FromObjDesc(player);
        var looks = new Dictionary<string, ModelData>();
        foreach (var role in roles)
        {
            looks[role.Key] = StandInHumanLook.FromObjDesc(role.Value);
        }

        HumanLooks = looks;
    }

    /// <summary>The ModelData a human-Setup object called <paramref name="name"/> is created with.</summary>
    internal ModelData LookFor(string name) => HumanLooks is not null && HumanLooks.TryGetValue(name, out var look) ? look : HumanModel;

    /// <summary>
    /// The weenies the stand-in knows for <c>item</c>, by class id. Null: <see cref="StandInItemKind.Default"/> (<c>TODO(m3-standin-weenies)</c>: the stand-in has no weenie database;
    /// a class id it does not hold answers <c>unknown-wcid</c>).
    /// </summary>
    public IReadOnlyDictionary<uint, StandInItemKind>? Items { get; set; }

    /// <summary>
    /// A real PlayerDescription (and its contained objects) to send in place of <see cref="Stats"/>'s synthetic one (M3 1.2 s5: <see cref="RecordedDescription.TryFind"/> reads it from <c>cache/sessions</c>; the play checks set it when
    /// a recording exists). Null: <see cref="Stats"/> is sent as before. Settable like <see cref="XpTable"/>: the host loads it before the session starts.
    /// </summary>
    public RecordedDescription? Recorded { get; set; }

    /// <summary>
    /// The stand-in's player is announced with a Handy Healing Kit (20 uses) and five Potions of Healing in its main pack, as the Holtburg Hunter carries them (M4 3.1 s2, <c>TODO(m4-standin-heal-items)</c>: the items are the
    /// world database's, the art is a placeholder), in the way a recorded description's pack is: listed in the PlayerDescription's pack and created right after the player. Off by default: the existing checks count the pack.
    /// Turns <see cref="Heals"/> on as well.
    /// </summary>
    public bool HealItems { get; set; }

    /// <summary>
    /// The stand-in holds the two Holtburg vendors, Thelnoth Cort the Healer and Shopkeeper Renald the Elder, at their real cells and positions with a placeholder model, answers a Use of them with their stock and the buy and the sell, and
    /// announces the player with 10,000 pyreals, a Handy Healing Kit and Leather Bracers (M4 3.2 s3, <c>TODO(m4-standin-vendor-model)</c>, <c>TODO(m4-standin-vendor-stock)</c>, <c>TODO(m4-standin-pyreals)</c>). Off by default: the existing
    /// checks count the pack. The vendors are the only NPCs the stand-in adds (D23); a check reaches one by <c>place loc</c> into its shop.
    /// </summary>
    public bool Vendors { get; set; }

    /// <summary>
    /// True when the visit's starting health is a placeholder of the rows that use it (M4 3.1 fix, review R24: the play checks' replay visit starts its synthetic player with <see cref="StandInPlayerStats.Health"/> 5 so the heal rows have a partial
    /// vital): the login burst then logs <c>TODO(m4-standin-start-vitals)</c> once, naming the number. Off by default.
    /// </summary>
    public bool StartVitalsPlaceholder { get; set; }

    /// <summary>
    /// The stand-in answers the heals (<c>StandInWorld.Heal.cs</c>, M4 3.1 s2): a Use of a held Potion of Healing drinks it, and a UseWithTarget of a held Handy Healing Kit on the player heals (the roll, the vitals, the uses, the refusals).
    /// Off by default: a potion's Use is the bare UseDone it was before 3.1 (the hunt's tests count on a potion that heals nothing) and a UseWithTarget is a <see cref="NotSupportedException"/> naming <c>TODO(m4-standin-heal)</c>.
    /// </summary>
    public bool Heals { get; set; }

    /// <summary>The F755 scripts the server plays on objects after the unhide (Smartbox queue). None by default.</summary>
    public ImmutableArray<StandInScript> Scripts { get; init; } = [];

    /// <summary>The F750 sounds the server plays on objects after the unhide (Smartbox queue). None by default.</summary>
    public ImmutableArray<StandInSound> Sounds { get; init; } = [];

    /// <summary>The vital updates the server sends after the unhide, in time order. None by default.</summary>
    public ImmutableArray<StandInVitalUpdate> VitalUpdates { get; init; } = [];

    /// <summary>Seconds after the last teleport (or the unhide) when the logoff starts; null: the player stays.</summary>
    public double? LogOffAfter { get; init; }

    /// <summary>Seconds a logoff takes before the character list is back.</summary>
    public double LogOffDelay { get; init; } = 0.5;
}

/// <summary>A PrivateUpdateAttribute2ndLevel the stand-in server sends: <paramref name="After"/> seconds after the unhide, the vital whose current value has id <paramref name="CurrentId"/> (2 health, 4 stamina, 6 mana) becomes <paramref name="Value"/>.</summary>
/// <param name="After">Seconds after the unhide.</param>
/// <param name="CurrentId">The secondary attribute id of the vital's current value.</param>
/// <param name="Value">The new current value.</param>
public readonly record struct StandInVitalUpdate(double After, uint CurrentId, uint Value);

/// <summary>
/// The stand-in player's description (<see cref="StandInVisit.Stats"/>): a Lifestone-authored placeholder, not a character's. Health 5 ranks over half of Endurance 120 is 65; stamina
/// is Endurance, 120; mana is Self 100. The option bits are ACE's default for a new character (0x50C4A54A: bit 21, side-by-side vitals, is clear, so the stacked window shows).
/// </summary>
public sealed record StandInPlayerStats
{
    /// <summary>ACE's default CharacterOptions1 (its <c>CharacterOptions1</c> default for a new character; the PlayerDescription codec's wire writers use the same).</summary>
    public const uint AceDefaultOptions1 = 0x50C4A54A;

    /// <summary>The character's option bits.</summary>
    public uint Options1 { get; init; } = AceDefaultOptions1;

    /// <summary>Strength.</summary>
    public uint Strength { get; init; } = 100;

    /// <summary>Endurance (health and stamina).</summary>
    public uint Endurance { get; init; } = 120;

    /// <summary>Coordination.</summary>
    public uint Coordination { get; init; } = 90;

    /// <summary>Quickness.</summary>
    public uint Quickness { get; init; } = 80;

    /// <summary>Focus.</summary>
    public uint Focus { get; init; } = 70;

    /// <summary>Self (mana).</summary>
    public uint Self { get; init; } = 100;

    /// <summary>Health ranks raised.</summary>
    public uint HealthRanks { get; init; } = 5;

    /// <summary>Current health.</summary>
    public uint Health { get; init; } = 52;

    /// <summary>Current stamina.</summary>
    public uint Stamina { get; init; } = 120;

    /// <summary>Current mana.</summary>
    public uint Mana { get; init; } = 75;

    /// <summary>The character's level (PropertyInt 25); 0 sends none (the default: the stand-in's description carries no experience of its own).</summary>
    public int Level { get; init; }

    /// <summary>The experience the character has earned (PropertyInt64 1); with <see cref="AvailableExperience"/> sent only when either is non-zero.</summary>
    public long TotalExperience { get; init; }

    /// <summary>The experience the character can spend (PropertyInt64 2): a raise at the stand-in spends it (a placeholder amount; the stand-in has no experience of its own).</summary>
    public long AvailableExperience { get; init; }

    /// <summary>The skill credits the character can spend (PropertyInt 24); sent only when non-zero.</summary>
    public int AvailableSkillCredits { get; init; }

    /// <summary>The skills the description carries (none by default); a RaiseSkill or TrainSkill at the stand-in works on these.</summary>
    public ImmutableArray<SkillDesc> Skills { get; init; } = [];

    /// <summary>The description's enchantment registry (what a buffed character's PlayerDescription carries; none by default).</summary>
    public ImmutableArray<EnchantmentDesc> Enchantments { get; init; } = [];

    /// <summary>The spells the description carries, each with its power (PlayerDescription's spell table); none by default. When set (<see cref="ProbeSpells"/>), it replaces a replayed recording's spells (<c>TODO(m3-standin-probe-spellbook)</c>, logged once).</summary>
    public ImmutableDictionary<uint, float> Spells { get; init; } = ImmutableDictionary<uint, float>.Empty;

    /// <summary>The spellbook filter word the description carries (<see cref="ProbeSpellbookFilters"/> for the probe book); null sends none (or leaves a recording's).</summary>
    public uint? SpellbookFilters { get; init; }

    /// <summary>Enchantments appended to the registry the description carries, whether the stand-in's own (<see cref="Enchantments"/>) or a replayed recording's: <see cref="ProbeEnchantments"/> gives the play checks a harmful one and a vitae.</summary>
    public ImmutableArray<EnchantmentDesc> ExtraEnchantments { get; init; } = [];

    /// <summary>
    /// The probe spellbook's rows (M3 2.2 fix B, 2.2-R5): 40 real spell ids of the retail SpellTable, the lowest id of each school and rough level 1 to 8 (the table holds no spell with another level), each with the filter bits it needs
    /// (the school's bit and the level's bit; <c>IsFilteredOut</c>, 0x0048B190) written here by hand from a dump of the table, so the spellbook check's count is not computed by the page's own rule. Ids 2, 3, 1182 and 4325 are not in it:
    /// the play checks learn them. Lifestone-authored: a placeholder book, not a character's.
    /// </summary>
    public static ImmutableArray<(uint Id, uint FilterBits)> ProbeSpellbook { get; } =
    [
        (1, 0x1 | 0x10), (230, 0x1 | 0x20), (231, 0x1 | 0x40), (232, 0x1 | 0x80), (233, 0x1 | 0x100), (234, 0x1 | 0x200), (2054, 0x1 | 0x400), (4292, 0x1 | 0x800), // creature, levels 1 to 8
        (35, 0x2 | 0x10), (1470, 0x2 | 0x20), (47, 0x2 | 0x40), (157, 0x2 | 0x80), (158, 0x2 | 0x100), (1474, 0x2 | 0x200), (2092, 0x2 | 0x400), (4342, 0x2 | 0x800), // item
        (5, 0x4 | 0x10), (160, 0x4 | 0x20), (161, 0x4 | 0x40), (162, 0x4 | 0x80), (163, 0x4 | 0x100), (164, 0x4 | 0x200), (2052, 0x4 | 0x400), (3976, 0x4 | 0x800), // life
        (27, 0x8 | 0x10), (59, 0x8 | 0x20), (60, 0x8 | 0x40), (61, 0x8 | 0x80), (62, 0x8 | 0x100), (57, 0x8 | 0x200), (2120, 0x8 | 0x400), (2912, 0x8 | 0x800), // war
        (5339, 0x2000 | 0x10), (5340, 0x2000 | 0x20), (5341, 0x2000 | 0x40), (5342, 0x2000 | 0x80), (5343, 0x2000 | 0x100), (5344, 0x2000 | 0x200), (5337, 0x2000 | 0x400), (5338, 0x2000 | 0x800), // void
    ];

    /// <summary>A spell id the retail SpellTable does not hold (its highest id is 6340): the probe book carries it, and the spellbook lists nothing for it (a spell with no table entry has no name, icon or level to show).</summary>
    public const uint ProbeUnknownSpell = 60001;

    /// <summary>The probe book's filter word: the default 0x3FFF with the level 8 bit (0x800) cleared, so five of the forty spells (one for each school) are hidden by the filter, and the unknown spell is skipped by the page.</summary>
    public const uint ProbeSpellbookFilters = 0x3FFF & ~0x800u;

    /// <summary>The probe spellbook as a description's spell table: the 40 rows and <see cref="ProbeUnknownSpell"/>, each with power 2.</summary>
    public static ImmutableDictionary<uint, float> ProbeSpells()
    {
        var book = ImmutableDictionary.CreateBuilder<uint, float>();
        foreach (var (id, _) in ProbeSpellbook)
        {
            book[id] = 2f;
        }

        book[ProbeUnknownSpell] = 2f;
        return book.ToImmutable();
    }

    /// <summary>Spell 230, "Vulnerability Other II" (a harmful spell in the retail table): the harmful enchantment of the probe registry (an additive integer of a property key no check reads, permanent).</summary>
    public const ushort ProbeHarmfulSpell = 230;

    /// <summary>The vitae the probe registry carries: a 5 percent penalty (multiplier 0.95).</summary>
    public const float ProbeVitaeValue = 0.95f;

    /// <summary>
    /// The enchantments the play checks append to a registry (M3 2.2 fix B, 2.2-R6): one harmful enchantment (<see cref="ProbeHarmfulSpell"/>, in its own category, no effect on a stat any check reads) and the vitae (<see cref="ProbeVitaeValue"/>),
    /// so page 5 and the vitae page have something to show. The vitae multiplies the secondary attributes and the attributes, so the checks derive what they expect from the registry as sent.
    /// </summary>
    public static ImmutableArray<EnchantmentDesc> ProbeEnchantments() =>
    [
        new EnchantmentDesc(ProbeHarmfulSpell, 1, 1500, 0, 20, 0.0, -1.0, 0x50000001, 0f, 0f, 0.0, 0x8004, 40, -1f, 0, EnchantmentBucket.Additive),
        new EnchantmentDesc(Contracts.EnchantmentRules.VitaeSpellId, 1, 666, 0, 0, 0.0, -1.0, 0x50000001, 0f, 0f, 0.0, Contracts.EnchantmentRules.Multiplicative, 0, ProbeVitaeValue, 0, EnchantmentBucket.Vitae),
    ];

    /// <summary>
    /// A registry shaped like a buffed live character's (138 enchantments with layered duplicates of the same spell, as the live gate's PlayerDescription): float and integer modifiers of
    /// items, a four-layer Endurance buff, and a five-layer stamina-maximum buff whose top layer (+20) is the only one that counts. Lifestone-authored numbers, not any character's.
    /// </summary>
    public static ImmutableArray<EnchantmentDesc> LiveShapedEnchantments()
    {
        var builder = ImmutableArray.CreateBuilder<EnchantmentDesc>(138);
        EnchantmentDesc Make(ushort spell, ushort layer, ushort category, uint power, uint type, uint key, float value, EnchantmentBucket bucket) =>
            new(spell, layer, category, 0, power, 0.0, -1.0, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, bucket);
        for (ushort spell = 0; spell < 25; spell++)
        {
            for (ushort layer = 1; layer <= 4; layer++)
            {
                builder.Add(Make((ushort)(4459 + spell), layer, (ushort)(100 + spell), 50u + layer, 0x2005008, 60u + spell, 0.32f, EnchantmentBucket.Multiplicative)); // multiplicative float of a property key
            }
        }
        for (ushort layer = 1; layer <= 4; layer++)
        {
            builder.Add(Make(1344, layer, 11, 10u * layer, 0x2009001, 2, 5f * layer, EnchantmentBucket.Additive)); // additive Endurance (attribute 2); the top layer (+20) applies
        }
        for (ushort layer = 1; layer <= 5; layer++)
        {
            builder.Add(Make(1360, layer, 12, 10u * layer, 0x2009002, 3, 4f * layer, EnchantmentBucket.Additive)); // additive maximum stamina (secondary attribute 3); the top layer (+20) applies
        }
        for (ushort layer = 1; layer <= 29; layer++)
        {
            builder.Add(Make((ushort)(2000 + layer), 1, (ushort)(200 + layer), 20u, 0x2008004, 40u, 1f, EnchantmentBucket.Additive)); // additive integer of a property key (not a vital)
        }
        return builder.ToImmutable();
    }
}

public static partial class StandInSessions
{
    /// <summary>
    /// Schedules <paramref name="visit"/> on an offline session whose character list the stand-in has already fed (<see cref="CharacterSelect"/>): the
    /// session must be one <c>GameSession.Offline</c> made (its reducer holds the schedule). The visit begins when the list is in; the first stand-in
    /// character enters.
    /// </summary>
    public static void PlayerVisits(IInboundMessageHandler handler, StandInVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (handler is not GameStateReducer { StandIn: { } schedule } reducer)
        {
            throw new ArgumentException("a stand-in visit needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        var guid = FirstCharacterGuid;
        var name = reducer.State.Characters.Entries.Count > 0 ? reducer.State.Characters.Entries[0].Name : DefaultCharacters[0].Name;
        if (visit.HoldEntry)
        {
            schedule.HeldEntry = after => ScheduleVisit(schedule, reducer, visit, guid, name, reducer.Clock.Now + after);
            return;
        }

        ScheduleVisit(schedule, reducer, visit, guid, name, reducer.Clock.Now + visit.EnterAfter);
    }

    /// <summary>Lets a held visit (<see cref="StandInVisit.HoldEntry"/>) begin: the player enters <paramref name="after"/> seconds from now (session clock). False when nothing is held.</summary>
    internal static bool ReleaseEntry(IInboundMessageHandler handler, double after)
    {
        if (handler is not GameStateReducer { StandIn: { HeldEntry: { } held } schedule })
        {
            return false;
        }

        schedule.HeldEntry = null;
        held(after);
        return true;
    }

    private static void ScheduleVisit(StandInSchedule schedule, GameStateReducer reducer, StandInVisit visit, uint guid, string name, double enterAt)
    {
        schedule.At(enterAt, sink => Enter(sink, guid, name, visit));
        var unhideAt = enterAt + visit.UnhideAfter;
        schedule.At(unhideAt, sink => Unhide(sink, guid));
        foreach (var obj in visit.Objects)
        {
            if (obj.UnhideAfter is { } objectWait)
            {
                var guidToUnhide = obj.Guid;
                schedule.At(unhideAt + objectWait, sink => UnhideObject(sink, guidToUnhide));
            }
        }

        foreach (var line in visit.Speech)
        {
            schedule.At(unhideAt + line.After, sink => sink.OnSpeech(new InboundContext(0, MessageQueue.SmartBox, sink is GameStateReducer r ? r.Clock.Time.GetTimestamp() : 0), new SpeechMsg(line.Text, line.Speaker, line.SpeakerGuid, 2, null)));
        }

        foreach (var question in visit.Confirmations)
        {
            schedule.At(unhideAt + question.After, sink => (sink as GameStateReducer)?.LocalResponder?.World?.AskQuestionAt(sink, question.Type, question.Text));
        }

        // M3 1.3 s0: the scripts first, then the sounds, at the same time as ACE queues them (steps due together run in the order scheduled); all on the Smartbox queue.
        foreach (var effect in visit.Scripts)
        {
            var target = effect.Guid == 0 ? guid : effect.Guid;
            schedule.At(unhideAt + effect.After, sink => sink.OnPlayEffect(WorldContext(sink), new PlayEffectMsg(target, effect.ScriptType, effect.Speed)));
        }

        foreach (var effect in visit.Sounds)
        {
            var target = effect.Guid == 0 ? guid : effect.Guid;
            schedule.At(unhideAt + effect.After, sink => sink.OnSound(WorldContext(sink), new SoundMsg(target, effect.SoundType, effect.Volume)));
        }

        foreach (var update in visit.VitalUpdates)
        {
            var sequence = (byte)(1 + visit.VitalUpdates.IndexOf(update));
            schedule.At(unhideAt + update.After, sink => sink.OnPrivateUpdate(WorldContext(sink), new PrivateUpdateMsg(PrivateUpdateKind.VitalCurrent, sequence, update.CurrentId, update.Value, default, default, default)));
        }

        ushort teleport = 1;
        var at = unhideAt;
        foreach (var jump in visit.Teleports)
        {
            at += jump.After;
            var sequence = ++teleport;
            schedule.At(at, sink => SendTeleport(sink, guid, jump, sequence));
        }

        if (visit.LogOffAfter is { } wait)
        {
            schedule.At(at + wait, sink => BeginLogOff(sink, visit.LogOffDelay, schedule, reducer));
        }
    }

    /// <summary>
    /// Lets the offline session answer the world's actions the way a server would for <paramref name="visit"/>: a server-mod <c>place</c> request, a Use of
    /// one of the visit's portal objects (<see cref="StandInObject.PortalTo"/>) and a log off, <paramref name="delay"/> later on the session clock (default
    /// <see cref="DefaultWorldDelay"/>); the tools then work on this session. Opt-in: the play-mode checks call it; every other offline session refuses
    /// those actions as before. Calling it again (a reconnect runs the stand-in again) replaces the visit.
    /// </summary>
    /// <param name="handler">The offline session's reducer (the handler <c>GameSession.Offline</c> passes).</param>
    /// <param name="visit">The visit whose portals and log-off delay the answers use.</param>
    /// <param name="delay">How long an answer takes.</param>
    public static void AnswerWorld(IInboundMessageHandler handler, StandInVisit visit, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (handler is not GameStateReducer { StandIn: not null } reducer)
        {
            throw new ArgumentException("answering the world needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        var wait = delay ?? DefaultWorldDelay;
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero, nameof(delay));
        reducer.LocalResponder ??= new StandInResponder(reducer, DefaultAnswerDelay.TotalSeconds);
        reducer.LocalResponder.World = new StandInWorld(reducer, visit, wait.TotalSeconds);
    }

    /// <summary>
    /// Ends the session <paramref name="after"/> seconds from now (session clock) in failure, as the network session would: a boot (F7DC,
    /// <see cref="SessionFailureKind.Booted"/>) or a server that went quiet (<see cref="SessionFailureKind.Silence"/>) while the player is in the world. The
    /// retail disconnected screen reports it; its button reconnects (D43).
    /// </summary>
    /// <param name="handler">The offline session's reducer.</param>
    /// <param name="after">Seconds from now on the session clock.</param>
    /// <param name="failure">The failure (its message is shown; Lifestone-authored text only).</param>
    public static void FailLater(IInboundMessageHandler handler, double after, SessionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (handler is not GameStateReducer { StandIn: { } schedule } reducer)
        {
            throw new ArgumentException("a stand-in failure needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        schedule.At(reducer.Clock.Now + after, sink => sink.OnSessionEvent(SessionEvent.Failed(Now(sink), failure)));
    }

    /// <summary>
    /// The server boots the player <paramref name="after"/> seconds from now (session clock): F7DC AccountBoot with <paramref name="reason"/>, then the session
    /// fails with <see cref="SessionFailureKind.Booted"/>, as the network session does (the play flow says "the server booted you", the disconnected screen has
    /// the reason).
    /// </summary>
    /// <param name="handler">The offline session's reducer.</param>
    /// <param name="after">Seconds from now on the session clock.</param>
    /// <param name="reason">The boot's reason text (Lifestone-authored), or null.</param>
    public static void BootLater(IInboundMessageHandler handler, double after, string? reason)
    {
        if (handler is not GameStateReducer { StandIn: { } schedule } reducer)
        {
            throw new ArgumentException("a stand-in boot needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        schedule.At(reducer.Clock.Now + after, sink =>
        {
            sink.OnAccountBoot(Context(sink), new AccountBootMsg(reason));
            sink.OnSessionEvent(SessionEvent.Failed(Now(sink), new SessionFailure(SessionFailureKind.Booted, SessionPhase.InWorld, reason ?? "the stand-in server booted the session")));
        });
    }

    /// <summary>
    /// The stand-in server's levers for a host that does not name the network layer (the game): <see cref="StandInServer.Boot"/> and
    /// <see cref="StandInServer.GoQuiet"/>.
    /// </summary>
    /// <param name="handler">The offline session's reducer (the handler <c>GameSession.Offline</c> passes).</param>
    public static StandInServer Server(IInboundMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new StandInServer(handler);
    }

    /// <summary>How long a world answer takes when the host does not say.</summary>
    public static readonly TimeSpan DefaultWorldDelay = TimeSpan.FromSeconds(0.3);

    private static InboundContext WorldContext(IAcSessionSink sink) => new(0, MessageQueue.SmartBox, Now(sink));

    internal static void Enter(IAcSessionSink sink, uint guid, string name, StandInVisit visit)
    {
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.CharacterSelect, SessionPhase.EnteringWorld));
        sink.OnEnterWorldServerReady(Context(sink));
        sink.OnPlayerCreate(WorldContext(sink), new PlayerCreateMsg(guid));
        SidePackBurst? packs = null;
        var heal = visit.HealItems ? StandInWorld.HealItems(guid) : new HealItemBurst([], []);
        if (visit.HealItems)
        {
            (sink as GameStateReducer)?.LocalResponder?.World?.NotePlaceholder("m4-standin-heal-items", StandInWorld.HealItemsPlaceholder);
        }

        if (visit.Vendors)
        {
            heal = StandInWorld.WithVendorItems(heal, guid);
            (sink as GameStateReducer)?.LocalResponder?.World?.NotePlaceholder("m4-standin-vendor-model", StandInWorld.VendorsPlaceholder);
        }

        if (visit.StartVitalsPlaceholder)
        {
            (sink as GameStateReducer)?.LocalResponder?.World?.NotePlaceholder(
                "m4-standin-start-vitals",
                $"the player starts with {visit.Stats?.Health.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "an unset"} health, a placeholder of the heal rows (no recorded description and no later vital update; the reconnect rows run on this synthetic description too, not on the machine's recorded character), not a character's");
        }

        if (visit.Recorded is { } recorded)
        {
            // The recorded description as the server sent it (it holds no guid: the event's header names this player).
            packs = recorded.SidePacks(guid);
            var sent = WithBars(sink, RecordedFor(visit, recorded, sink));
            var listed = (packs.Value.Extra.IsDefaultOrEmpty ? ImmutableArray<ContentDesc>.Empty : packs.Value.Listed).AddRange(heal.Listed);

            sink.OnPlayerDescription(WorldContext(sink), new GameEventHeader(guid, 1, 0x0013), listed.IsEmpty ? sent : sent with { Inventory = sent.Inventory.AddRange(listed) });
            // M4 2.1 s3 (capture.md C1): ACE sends a 0196 per side pack in the first bundle, right after the description and before every CreateObject.
            if (packs.Value.Added)
            {
                (sink as GameStateReducer)?.LocalResponder?.World?.NotePlaceholder(
                    "m4-standin-side-pack-item",
                    "the recorded description carries no side pack: the stand-in adds a Pack holding one Pyreal stack, so the 0196 and the contents' CreateObjects of a side pack are exercised");
            }

            foreach (var view in packs.Value.Views)
            {
                sink.OnViewContents(WorldContext(sink), new GameEventHeader(guid, 0, 0x0196), view);
            }
        }
        else if (visit.Stats is { } stats)
        {
            var plain = WithBars(sink, Description(stats));
            sink.OnPlayerDescription(WorldContext(sink), new GameEventHeader(guid, 1, 0x0013), heal.Listed.IsEmpty ? plain : plain with { Inventory = plain.Inventory.AddRange(heal.Listed) });
        }

        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.EnteringWorld, SessionPhase.InWorld));
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.MTable | PhysicsDescFlags.STable | PhysicsDescFlags.PeTable | PhysicsDescFlags.Position,
            State = PhysicsState.Hidden | PhysicsState.IgnoreCollisions | PhysicsState.Gravity | PhysicsState.EdgeSlide,
            Position = new AcPosition(visit.Cell, visit.Origin, Quaternion.Identity),
            SetupId = StandInVisit.HumanSetup,
            MotionTableId = StandInVisit.HumanMotionTable,
            SoundTableId = StandInVisit.HumanSoundTable,
            PhysicsEffectTableId = StandInVisit.HumanScriptTable,
            Timestamps = Stamps(1, 1),
        };
        var weenie = StandInWeenieArt.Apply(new WeenieDesc { Header = WeenieHeaderFlags.None, Name = name, WeenieClassId = 1, IconId = StandInWeenieArt.PlaceholderIcon, ItemType = 0x10, ObjectDescriptionFlags = 0x8 }, null);
        sink.OnCreateObject(WorldContext(sink), new CreateObjectMsg(guid, visit.HumanModel, physics, weenie), isUpdate: false);
        if (visit.Recorded is { } contents)
        {
            // The objects the description lists, so that every one of them exists (the portal wait of retail's SendLoginCompleteNotification); the recorded player's guid in them becomes this player's.
            // The order of capture.md C1: the main pack's items (the packs among them), the side packs' contents (container = the pack), then the equipped objects.
            var carried = new HashSet<uint>(contents.Description.Inventory.Select(c => c.Guid));
            var items = contents.ItemsFor(guid).ToList();
            foreach (var item in items.Where(i => carried.Contains(i.Guid)).Concat(heal.Objects).Concat(packs!.Value.Extra).Concat(packs.Value.Contents).Concat(items.Where(i => !carried.Contains(i.Guid))))
            {
                sink.OnCreateObject(WorldContext(sink), item, isUpdate: false);
            }
        }
        else if (!heal.Objects.IsEmpty)
        {
            // M4 3.1 s2: the kit and the potions of a visit with no recording are created right after the player, the main pack's items as in the recorded burst.
            foreach (var item in heal.Objects)
            {
                sink.OnCreateObject(WorldContext(sink), item, isUpdate: false);
            }
        }

        foreach (var obj in visit.Objects)
        {
            sink.OnCreateObject(WorldContext(sink), ObjectMessage(obj, new AcPosition(visit.Cell, visit.Origin + obj.Offset, Quaternion.Identity), visit.LookFor(obj.Name)), isUpdate: false);
        }

        if (visit.Vendors)
        {
            // M4 3.2 s3: the two vendors stand at their real cells inside their shops (D23: the only NPCs the stand-in adds).
            foreach (var vendor in StandInWorld.VendorObjects(visit))
            {
                sink.OnCreateObject(WorldContext(sink), vendor, isUpdate: false);
            }
        }
    }

    /// <summary>The description with the favourite-spell bars the stand-in holds since an <c>AddSpellFavorite</c> (M3 3.2 db; <see cref="StandInWorld.WithBars"/>), unchanged when none was added.</summary>
    private static PlayerDescriptionMsg WithBars(IAcSessionSink sink, PlayerDescriptionMsg description) =>
        (sink as GameStateReducer)?.LocalResponder?.World is { } world ? world.WithVendorState(world.WithOptions(world.WithBars(description))) : description; // M4 3.2 s3: the shortcut list and the purse ride the same funnel

    /// <summary>
    /// The weenie ItemType the stand-in gives <paramref name="obj"/> (M4 4.1 t2, D23: no stand-in object but a person reads as a creature): a chest is a Container (0x200); a person is a Creature (0x10), that is an object whose description
    /// carries the Player bit (0x8, another player), the Vendor bit (0x200, the stand-in's two vendors) or the Attackable bit (0x10, a unit-test fixture); every other object (a lifestone, a portal, a statue, an apparition) is Misc (0x80).
    /// </summary>
    /// <param name="obj">The object.</param>
    internal static uint ItemTypeOf(in StandInObject obj) =>
        obj.WeenieClassId == StandInChest.Wcid ? 0x200u : (obj.DescriptionFlags & (PlayerFlag | StandInWorld.VendorFlag | AttackableFlag)) != 0 ? CreatureItemType : MiscItemType;

    /// <summary>ACE's <c>ObjectDescriptionFlags.Player</c> (BF_PLAYER).</summary>
    internal const uint PlayerFlag = 0x8;

    /// <summary>ACE's <c>ObjectDescriptionFlags.Attackable</c> (BF_ATTACKABLE): an attackable object is a creature in the stand-in's model (a unit-test fixture only; no check places one).</summary>
    internal const uint AttackableFlag = 0x10;

    /// <summary>ITEM_TYPE::TYPE_CREATURE.</summary>
    internal const uint CreatureItemType = 0x10;

    /// <summary>ITEM_TYPE::TYPE_MISC.</summary>
    internal const uint MiscItemType = 0x80;

    /// <summary>
    /// The CreateObject the server sends for one object of a visit (<paramref name="obj"/>) at <paramref name="position"/>: the visit's objects at their offsets from the player's creation position, and an object a check places (M3 3.1 fb,
    /// <see cref="StandInServer.Place"/>) where it is told.
    /// </summary>
    internal static CreateObjectMsg ObjectMessage(StandInObject obj, AcPosition position, ModelData? humanLook = null)
    {
        var hiddenAtCreation = obj.UnhideAfter is not null;
        var objectPhysics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position | (obj.MotionTable != 0 ? PhysicsDescFlags.MTable : PhysicsDescFlags.None) | (hiddenAtCreation ? PhysicsDescFlags.PeTable : PhysicsDescFlags.None),
            State = hiddenAtCreation
                ? PhysicsState.Hidden | PhysicsState.IgnoreCollisions | PhysicsState.Gravity | PhysicsState.EdgeSlide
                : PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
            PhysicsEffectTableId = hiddenAtCreation ? StandInVisit.HumanScriptTable : null,
            Position = position,
            SetupId = obj.Setup,
            MotionTableId = obj.MotionTable != 0 ? obj.MotionTable : null,
            Timestamps = Stamps(1, 1),
        };
        // g3b: every stand-in object is shown on the radar (RadarBehavior 4, show always; a placeholder: the stand-in's weenie carries no data); a portal has the portal bit, and a server blip colour is sent when the object names one.
        var objectWeenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.RadarBehavior | (obj.BlipColor != 0 ? WeenieHeaderFlags.RadarBlipColor : WeenieHeaderFlags.None),
            Name = obj.Name,
            WeenieClassId = obj.WeenieClassId,
            IconId = StandInWeenieArt.PlaceholderIcon,
            ItemType = ItemTypeOf(obj), // a chest is a Container (M4 2.1 s3), a lifestone, a portal and a statue are Misc (M4 4.1 t2: D23, a placeholder that is not a person must not read as a creature); only another player and a vendor are Creature
            ObjectDescriptionFlags = obj.DescriptionFlags | (obj.PortalTo is not null ? 0x40000u : 0u),
            RadarBehavior = 4,
            RadarBlipColor = obj.BlipColor != 0 ? obj.BlipColor : null,
        };
        if (StandInWeenieArt.TryGet(obj.WeenieClassId, out _))
        {
            // M4 4.1 v4: the chest and the two vendors carry their weenie's own icon (a creature, the lifestone and a placement the table does not hold keep the placeholder icon without a TODO: the examine panel and the radar are all that read it).
            objectWeenie = StandInWeenieArt.Apply(objectWeenie, null);
        }

        return new CreateObjectMsg(obj.Guid, humanLook is not null && obj.Setup == StandInVisit.HumanSetup ? humanLook : ModelData.Empty, objectPhysics, objectWeenie);
    }

    /// <summary>
    /// The recorded description as sent. One placeholder, named: when the visit's <see cref="StandInVisit.Stats"/> carry experience (the raise check spends it), the experience the recorded character
    /// lacks is made up to that (PropertyInt64 1 and 2, only upwards), because a graduated test character has none to spend and the stand-in would, rightly, refuse its raise (<c>TODO(m3-standin-recorded-xp)</c>).
    /// </summary>
    private static PlayerDescriptionMsg RecordedFor(StandInVisit visit, RecordedDescription recorded, IAcSessionSink sink)
    {
        var description = recorded.Description;
        if (visit.Stats is not { } stats)
        {
            return description;
        }

        if (stats.TotalExperience != 0 || stats.AvailableExperience != 0)
        {
            var int64s = description.Int64Properties.ToBuilder();
            var recordedTotal = int64s.GetValueOrDefault(Contracts.PlayerQualities.TotalExperienceProperty);
            var recordedAvailable = int64s.GetValueOrDefault(Contracts.PlayerQualities.AvailableExperienceProperty);
            int64s[Contracts.PlayerQualities.TotalExperienceProperty] = Math.Max(stats.TotalExperience, recordedTotal);
            int64s[Contracts.PlayerQualities.AvailableExperienceProperty] = Math.Max(stats.AvailableExperience, recordedAvailable);
            if (stats.TotalExperience > recordedTotal || stats.AvailableExperience > recordedAvailable)
            {
                // M3 1.2 review R8: the placeholder is not silent. Logged once per session through the stand-in world's placeholder log (the session's log).
                (sink as GameStateReducer)?.LocalResponder?.World?.NotePlaceholder(
                    "m3-standin-recorded-xp",
                    "the recorded character's experience (PropertyInt64 1 total, 2 available) is made up to the visit's stats because a graduated test character has none to spend; a placeholder, not the recorded values");
            }

            description = description with { Int64Properties = int64s.ToImmutable() };
        }

        if (!stats.Spells.IsEmpty || !stats.ExtraEnchantments.IsDefaultOrEmpty)
        {
            (sink as GameStateReducer)?.LocalResponder?.World?.NotePlaceholder(
                "m3-standin-probe-spellbook",
                "the visit's probe spellbook (replacing the recorded character's spells and spellbook filter) and probe enchantments (a harmful one and a vitae, appended to the recorded registry) are made up for the play checks; placeholders, not the recorded values");
        }

        return WithProbe(description, stats);
    }

    /// <summary>
    /// <paramref name="description"/> with the visit's probe parts applied (M3 2.2 fix B, 2.2-R5 and R6): the stats' <see cref="StandInPlayerStats.Spells"/> and <see cref="StandInPlayerStats.SpellbookFilters"/> replace the description's, and their
    /// <see cref="StandInPlayerStats.ExtraEnchantments"/> are appended to the registry. A description is unchanged when the stats hold none (the default). The synthetic description and a replayed recording both go through it, so the play checks
    /// see the same extras on either path and derive their expectations from the result.
    /// </summary>
    /// <param name="description">The description to be sent.</param>
    /// <param name="stats">The visit's stats.</param>
    public static PlayerDescriptionMsg WithProbe(PlayerDescriptionMsg description, StandInPlayerStats? stats)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (stats is null)
        {
            return description;
        }

        if (!stats.Spells.IsEmpty)
        {
            description = description with { Spells = stats.Spells };
        }

        if (stats.SpellbookFilters is { } filters)
        {
            description = description with { SpellbookFilters = filters };
        }

        if (!stats.ExtraEnchantments.IsDefaultOrEmpty)
        {
            description = description with { Enchantments = description.Enchantments.IsDefault ? stats.ExtraEnchantments : description.Enchantments.AddRange(stats.ExtraEnchantments) };
        }

        return description;
    }

    /// <summary>The synthetic description the stand-in sends for <paramref name="stats"/> when no recording is replayed, probe parts included (<see cref="WithProbe"/>).</summary>
    /// <param name="stats">The stats.</param>
    public static PlayerDescriptionMsg SyntheticDescription(StandInPlayerStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return Description(stats);
    }

    private static PlayerDescriptionMsg Description(StandInPlayerStats s)
    {
        var ints = ImmutableDictionary.CreateBuilder<uint, int>();
        if (s.Level > 0)
        {
            ints[Contracts.PlayerQualities.LevelProperty] = s.Level;
        }

        if (s.AvailableSkillCredits != 0)
        {
            ints[Contracts.PlayerQualities.AvailableSkillCreditsProperty] = s.AvailableSkillCredits;
        }

        var int64s = ImmutableDictionary.CreateBuilder<uint, long>();
        if (s.TotalExperience != 0 || s.AvailableExperience != 0)
        {
            int64s[Contracts.PlayerQualities.TotalExperienceProperty] = s.TotalExperience;
            int64s[Contracts.PlayerQualities.AvailableExperienceProperty] = s.AvailableExperience;
        }

        return WithProbe(Describe(s) with { IntProperties = ints.ToImmutable(), Int64Properties = int64s.ToImmutable(), Skills = s.Skills }, s);
    }

    private static PlayerDescriptionMsg Describe(StandInPlayerStats s) => new()
    {
        Options1 = s.Options1,
        Enchantments = s.Enchantments,
        Attributes =
        [
            new AttributeDesc(1, 0, s.Strength, 0),
            new AttributeDesc(2, 0, s.Endurance, 0),
            new AttributeDesc(3, 0, s.Quickness, 0),
            new AttributeDesc(4, 0, s.Coordination, 0),
            new AttributeDesc(5, 0, s.Focus, 0),
            new AttributeDesc(6, 0, s.Self, 0),
        ],
        Vitals =
        [
            new VitalDesc(1, s.HealthRanks, 0, 0, s.Health),
            new VitalDesc(3, 0, 0, 0, s.Stamina),
            new VitalDesc(5, 0, 0, 0, s.Mana),
        ],
    };

    private static void UnhideObject(IAcSessionSink sink, uint guid) =>
        sink.OnSetState(WorldContext(sink), new SetStateMsg(guid, PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide, 1, 2));

    private static void Unhide(IAcSessionSink sink, uint guid) =>
        sink.OnSetState(WorldContext(sink), new SetStateMsg(guid, PhysicsState.Gravity | PhysicsState.EdgeSlide, 1, 2));

    /// <summary>The server's teleport of the player: F751 with <paramref name="sequence"/>, then the new position (a newer teleport sequence). ACE sends the position with the object's next position sequence (<c>ObjSeqs</c> increments per update, apart from the teleport's), so <paramref name="positionSequence"/> is the player's current one plus 1 when a run of F748 updates has advanced it (the melee approach); null keeps the scripted visit's.</summary>
    internal static void SendTeleport(IAcSessionSink sink, uint guid, StandInTeleport jump, ushort sequence, ushort? positionSequence = null)
    {
        sink.OnPlayerTeleport(WorldContext(sink), new PlayerTeleportMsg(sequence));
        var position = new AcPosition(jump.Cell, jump.Origin, Quaternion.Identity);
        sink.OnUpdatePosition(
            WorldContext(sink),
            new UpdatePositionMsg(guid, PositionFlags.Grounded, position, null, null, 1, positionSequence ?? sequence, sequence, 1));
    }

    /// <summary>
    /// The server's log off: the phase LoggingOff, then F653 and the character list after <paramref name="delay"/> seconds, without the
    /// characters in <paramref name="purged"/> (a stand-in purge removed them).
    /// </summary>
    internal static void BeginLogOff(IAcSessionSink sink, double delay, StandInSchedule schedule, GameStateReducer reducer, IReadOnlySet<uint>? purged = null)
    {
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.InWorld, SessionPhase.LoggingOff));
        schedule.At(reducer.Clock.Now + delay, done =>
        {
            done.OnCharacterLogOff(WorldContext(done));
            done.OnCharacterList(Context(done), ListOf(null, purged));
            done.OnSessionEvent(SessionEvent.PhaseChanged(Now(done), SessionPhase.LoggingOff, SessionPhase.CharacterSelect));
        });
    }

    private static PhysicsTimestamps Stamps(ushort all, ushort position) => new(position, all, all, all, all, all, all, all, all);
}

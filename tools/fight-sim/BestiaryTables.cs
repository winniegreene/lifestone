// The bestiary's tables (BestiaryModel.cs): THE ONE PLACE that lists what each tracked creature table holds. A column is its name, its source table, the property by its name and number in ACE's enum
// (ACEmulator/ACE@95ef426d, Source/ACE.Entity/Enum; BestiaryWorld.VerifyNames checks every pair against the ACE build at each run) and what it prints where the world database has no row. A new column is one line
// here; a new table is a column list and a TableFile. No ACE type; also compiled into Lifestone.Client.Tests.

namespace Lifestone.FightSim;

/// <summary>The tracked creature tables: their columns and their layout.</summary>
internal static class BestiaryTables
{
    /// <summary>Where the tracked tables are, from the checkout's root.</summary>
    public const string TrackedFolder = "src/Lifestone.Client/Session/Grounds";

    /// <summary>creatures.json, <c>creatures</c>: one row a creature weenie (a weenie of a type of <see cref="AceIds.CreatureTypes"/>).</summary>
    public static readonly IReadOnlyList<Column> Creatures =
    [
        // Identity, health and what it resists a spell with.
        Col.Key("wcid"),
        Col.String("name", "Name", 1),
        Col.Int("level", "Level", 25, absent: 0),
        Col.Vital("health_start", "MaxHealth", 1, Field.Start),
        Col.Vital("health_ranks", "MaxHealth", 1, Field.Ranks),
        Col.Attribute("endurance", "Endurance", 2),
        Col.Float("resist_health_drain", "ResistHealthDrain", 125, absent: 1),
        Col.Rule("damage_types", "the OR of the attacking body parts' d_Type (d_Val not 0); 0 when none attacks", Bestiary.DamageTypes),
        Col.Skill("defence_start", "MagicDefense", 15, Field.Start),
        Col.Skill("defence_ranks", "MagicDefense", 15, Field.Ranks),
        Col.Skill("defence_sac", "MagicDefense", 15, Field.Sac),
        Col.Attribute("focus", "Focus", 5),
        Col.Attribute("self", "Self", 6),

        // Its attack, as ACE's monster damage reads it.
        Col.Attribute("strength", "Strength", 1),
        Col.Attribute("coordination", "Coordination", 4),
        Col.Part("part_dval", Field.DVal),
        Col.Part("part_dvar", Field.DVar),
        Col.Part("part_dtype", Field.DType),
        Col.Key("weapon_wcid", Subject.Weapon),
        Col.Type("weapon_kind", Subject.Weapon),
        Col.Int("weapon_damage", "Damage", 44, of: Subject.Weapon),
        Col.Float("weapon_damage_mod", "DamageMod", 63, of: Subject.Weapon),
        Col.Float("weapon_variance", "DamageVariance", 22, of: Subject.Weapon),
        Col.Int("weapon_dtype", "DamageType", 45, of: Subject.Weapon),
        Col.Float("weapon_crit_multiplier", "CriticalMultiplier", 136, of: Subject.Weapon),
        Col.Float("weapon_crit_frequency", "CriticalFrequency", 147, of: Subject.Weapon),
        Col.Key("launcher_wcid", Subject.Launcher),
        Col.Float("launcher_damage_mod", "DamageMod", 63, of: Subject.Launcher),
        Col.Float("launcher_crit_multiplier", "CriticalMultiplier", 136, of: Subject.Launcher),
        Col.Float("launcher_crit_frequency", "CriticalFrequency", 147, of: Subject.Launcher),
        Col.Int("ammo_damage", "Damage", 44, of: Subject.Ammunition),
        Col.Float("ammo_variance", "DamageVariance", 22, of: Subject.Ammunition),
        Col.Int("ammo_dtype", "DamageType", 45, of: Subject.Ammunition),

        // Its attack timing, as ACE's monster AI reads it.
        Col.DataId("motion_table", "MotionTable", 2),
        Col.DataId("combat_table", "CombatTable", 4),
        Col.Float("powerup_time", "PowerupTime", 34),
        Col.Float("magic_delay", "AiUseMagicDelay", 80),
        Col.Attribute("quickness", "Quickness", 3),
        Col.Bool("human_magic", "AiUseHumanMagicAnimations", 7),
        Col.Int("weapon_combat_style", "DefaultCombatStyle", 46, of: Subject.Weapon),
        Col.Int("weapon_attack_type", "AttackType", 47, of: Subject.Weapon),
        Col.Int("launcher_combat_style", "DefaultCombatStyle", 46, of: Subject.Launcher),
        Col.Rule("melee_weapons", "how many wielded rows of its create list are a MeleeWeapon", Bestiary.MeleeWeapons),
        Col.Rule("shields", "how many wielded rows of its create list are a shield (PropertyInt.CombatUse is CombatUse.Shield)", Bestiary.Shields),
        Col.DataId("wielded_treasure", "WieldedTreasureType", 32),
        Col.Rule("spells", "its spell book in id order, [spell, probability] a row", Bestiary.SpellBook),
    ];

    /// <summary>creatures.json, <c>spells</c>: one row a spell of any creature's book, from the spell table.</summary>
    public static readonly IReadOnlyList<Column> Spells =
    [
        Col.Key("id"),
        Col.Spell("name", "Name", string.Empty),
        Col.Spell("element", "EType", -1),
        Col.Spell("base_intensity", "BaseIntensity", -1),
        Col.Spell("variance", "Variance", -1),
        Col.Spell("projectiles", "NumProjectiles", -1),
        Col.Spell("boost", "Boost", 0),
        Col.Spell("boost_variance", "BoostVariance", 0),
        Col.Spell("damage_type", "DamageType", -1),
        Col.Spell("transfer_source", "Source", -1),
        Col.Spell("transfer_cap", "TransferCap", -1),
    ];

    /// <summary>creature-resists.json: one row a creature weenie that records any of the seven; 1 is ACE's default (Creature.GetResistanceMod reads <c>ResistX ?? 1.0</c>).</summary>
    public static readonly IReadOnlyList<Column> Resists =
    [
        Col.Key("wcid"),
        Col.Float("slash", "ResistSlash", 64, absent: 1),
        Col.Float("pierce", "ResistPierce", 65, absent: 1),
        Col.Float("bludgeon", "ResistBludgeon", 66, absent: 1),
        Col.Float("fire", "ResistFire", 67, absent: 1),
        Col.Float("cold", "ResistCold", 68, absent: 1),
        Col.Float("acid", "ResistAcid", 69, absent: 1),
        Col.Float("electric", "ResistElectric", 70, absent: 1),
    ];

    /// <summary>creature-skills.json, <c>skills</c>: the skills ACE's two defence rolls read of a creature (its magic schools and its weapon skills, the retired ones included).</summary>
    public static readonly IReadOnlyList<Column> Skills =
    [
        Col.Skill("axe", "Axe", 1, Field.Row),
        Col.Skill("bow", "Bow", 2, Field.Row),
        Col.Skill("crossbow", "Crossbow", 3, Field.Row),
        Col.Skill("dagger", "Dagger", 4, Field.Row),
        Col.Skill("mace", "Mace", 5, Field.Row),
        Col.Skill("sling", "Sling", 8, Field.Row),
        Col.Skill("spear", "Spear", 9, Field.Row),
        Col.Skill("staff", "Staff", 10, Field.Row),
        Col.Skill("sword", "Sword", 11, Field.Row),
        Col.Skill("thrown_weapon", "ThrownWeapon", 12, Field.Row),
        Col.Skill("unarmed_combat", "UnarmedCombat", 13, Field.Row),
        Col.Skill("creature_enchantment", "CreatureEnchantment", 31, Field.Row),
        Col.Skill("item_enchantment", "ItemEnchantment", 32, Field.Row),
        Col.Skill("life_magic", "LifeMagic", 33, Field.Row),
        Col.Skill("war_magic", "WarMagic", 34, Field.Row),
        Col.Skill("two_handed_combat", "TwoHandedCombat", 41, Field.Row),
        Col.Skill("void_magic", "VoidMagic", 43, Field.Row),
        Col.Skill("heavy_weapons", "HeavyWeapons", 44, Field.Row),
        Col.Skill("light_weapons", "LightWeapons", 45, Field.Row),
        Col.Skill("finesse_weapons", "FinesseWeapons", 46, Field.Row),
        Col.Skill("missile_weapons", "MissileWeapons", 47, Field.Row),
    ];

    /// <summary>creature-skills.json, <c>weapons</c>: the WeaponSkill of every weapon and launcher a creature of creatures.json wields, where the weapon records one.</summary>
    public static readonly IReadOnlyList<Column> Weapons =
    [
        Col.Key("wcid"),
        Col.Int("weapon_skill", "WeaponSkill", 48),
    ];

    /// <summary>
    /// creature-behaviour.json, <c>behaviour</c> (M6 item 16 E1d): one row a creature weenie, the facts of its behaviour the first three tables do not hold. A weenie without a property gets what ACE gives it, and
    /// the column's line says where ACE's source states that (a file is under Source/ACE.Server/WorldObjects at ACEmulator/ACE@95ef426d). Where ACE has no value for a weenie without the property (a kind of
    /// creature, a friend, a foe, a hearing range of its own) the column prints -1. A value ACE computes from several rows is not stored: the table holds the stated terms (a vital's and a skill's start and ranks) and
    /// the client's reader computes it with the attributes creatures.json holds (Endurance, Self, Quickness). M6 item 16 E1e: the heartbeat's seconds are beside the three rates, which are vital points a heartbeat.
    /// </summary>
    public static readonly IReadOnlyList<Column> Behaviour =
    [
        Col.Key("wcid"),

        // What it is, and its body.
        Col.Int("creature_type", "CreatureType", 2).Ace("ACE holds none: WorldObject_Properties.cs:2231"),
        Col.DataId("setup", "Setup", 1, absent: 0).Ace("WorldObject_Properties.cs:909"),
        Col.Float("scale", "DefaultScale", 39, absent: 1).Ace("Monster_Navigation.cs:334"),

        // How near it notices, whom it attacks and whom it calls.
        Col.Float("visual_awareness", "VisualAwarenessRange", 31, absent: 18).Ace("VisualAwarenessRange_Default: Monster_Awareness.cs:377, read at 404"),
        Col.Float("aural_awareness", "AuralAwarenessRange", 32).Ace("ACE then hears as far as it sees: Monster_Awareness.cs:421"),
        Col.Int("tolerance", "Tolerance", 67, absent: 0).Ace("Monster_Awareness.cs:66"),
        Col.Int("targeting_tactic", "TargetingTactic", 68, absent: 0).Ace("Monster_Awareness.cs:75; with none ACE picks Random or TopDamager, 99-101"),
        Col.Int("friend_type", "FriendType", 72).Ace("ACE holds none: WorldObject_Properties.cs:2237"),
        Col.Int("foe_type", "FoeType", 73).Ace("ACE holds none: WorldObject_Properties.cs:2243"),

        // Its stamina and mana, how fast its vitals come back, and what a drain takes of them.
        Col.Vital("stamina_start", "MaxStamina", 3, Field.Start, absent: 0).Ace("a vital with no row is made empty: Entity/CreatureVital.cs:29-33"),
        Col.Vital("stamina_ranks", "MaxStamina", 3, Field.Ranks, absent: 0).Ace("Entity/CreatureVital.cs:29-33"),
        Col.Vital("mana_start", "MaxMana", 5, Field.Start, absent: 0).Ace("Entity/CreatureVital.cs:29-33"),
        Col.Vital("mana_ranks", "MaxMana", 5, Field.Ranks, absent: 0).Ace("Entity/CreatureVital.cs:29-33"),
        Col.Float("health_rate", "HealthRate", 3, absent: 0).Ace("Entity/CreatureVital.cs:38; a rate of 0 never ticks, Creature_Vitals.cs:117"),
        Col.Float("stamina_rate", "StaminaRate", 4, absent: 0).Ace("Entity/CreatureVital.cs:41"),
        Col.Float("mana_rate", "ManaRate", 5, absent: 0).Ace("Entity/CreatureVital.cs:44"),
        Col.Float("heartbeat_interval", "HeartbeatInterval", 1, absent: 5).Ace("WorldObject_Tick.cs:47-48; an interval of 0 or less never beats: 56-68"),
        Col.Bool("uses_mana", "AiUsesMana", 6, absent: 1).Ace("Monster_Magic.cs:23"),
        Col.Float("resist_stamina_drain", "ResistStaminaDrain", 72, absent: 1).Ace("Creature_Properties.cs:212"),
        Col.Float("resist_mana_drain", "ResistManaDrain", 74, absent: 1).Ace("Creature_Properties.cs:216"),

        // How fast it runs: its Run skill's terms (its speed needs the dats' motion table, read at run time).
        Col.Skill("run_start", "Run", 24, Field.Start, absent: 0).Ace("a skill with no row is added untrained and empty: Creature_Skills.cs:27-35"),
        Col.Skill("run_ranks", "Run", 24, Field.Ranks, absent: 0).Ace("Creature_Skills.cs:27-35"),
        Col.Skill("run_sac", "Run", 24, Field.Sac, absent: 1).Ace("SkillAdvancementClass.Untrained: Creature_Skills.cs:31"),

        // What its kill gives.
        Col.Int("xp", "XpOverride", 146, absent: 0).Ace("Creature_Death.cs:193"),
    ];

    /// <summary>The date the behaviour table was last written from the world database (UTC); a lead moves it at each refresh.</summary>
    public const string BehaviourRead = "2026-11-17";

    /// <summary>
    /// creature-behaviour.json's <c>sources</c> header, stated here (the first three tables carry theirs from the tracked file): what was read, from which world database, by which command and when; then every
    /// column's own line, so a default and its place in ACE's source are written once, in the column list.
    /// </summary>
    public static readonly string BehaviourSources = BestiaryWriter.Sources(
        ("behaviour",
            "every creature weenie of the world database, one row each, by weenie class: weenie.type 10 (WeenieType.Creature) and, from M6 item 16 E1e, weenie.type 15 (WeenieType.Cow), which ACE builds as a "
            + "Creature subclass (Cow.cs:7; the other types it builds so, Vendor, GamePiece, Pet and CombatPet, are left out, each with why in tools/fight-sim/BestiaryRows.cs, AceIds.LeftOut). The rows are "
            + "the server's own copy of ACE's open world database, loaded from the pinned world SQL "
            + "(ACE-World-16PY-Patches v0.9.296, server/versions.json). Read through ACE's own models, queries only, and written by the generator: pwsh -File tools/fight-sim.ps1 bestiary "
            + "(the columns are listed in tools/fight-sim/BestiaryTables.cs; bestiary --check writes the table again and compares it with this file). Written on " + BehaviourRead + "."),
        ("columns",
            "each column, its table and property in ACE's enums and what it holds for a weenie without the property, which is what ACE gives such a creature, with the place in ACE's source that says so "
            + "(a file is under Source/ACE.Server/WorldObjects at ACEmulator/ACE@95ef426d); -1 where ACE holds no value. " + string.Join(". ", Behaviour.Select(c => c.Describe())) + "."),
        ("computed",
            "three values ACE computes are not stored; the reader (Session/Grounds/CreatureBehaviour.cs) computes them from this table's terms and the attributes creatures.json holds: maximum stamina is "
            + "stamina_start + stamina_ranks + Endurance and maximum mana is mana_start + mana_ranks + Self, each never under 1 (ACE's CreatureVital.MaxValue, Entity/CreatureVital.cs:126-174, with the dats' vital "
            + "formulas); the Run skill is run_start + run_ranks, plus Quickness when the skill is usable: trained, specialized, or untrained, the dats' skill table letting Run be used untrained (ACE's "
            + "CreatureSkill.Current and IsUsable, Entity/CreatureSkill.cs:48-63 and 154-163). An attribute in these formulas is never under 1, an unstated one included (ACE's CreatureAttribute.Current, "
            + "Entity/CreatureAttribute.cs:149-160). The hearing range of a row with -1 is its visual range. The three rates are vital points a heartbeat of the creature, and heartbeat_interval is the "
            + "heartbeat's seconds (M6 item 16 E1e; ACE's 5 where the weenie states none, WorldObject_Tick.cs:47-48): a creature's heartbeat runs its vital tick (Creature_Tick.cs:14-49, "
            + "Creature_Vitals.cs:81-95) and sets the next one that many seconds on (WorldObject_Tick.cs:98-108), so a rate over the interval is vital points a second, which the reader gives; with an "
            + "interval of 0 or less ACE never beats (WorldObject_Tick.cs:56-68) and a vital never comes back."),
        ("use",
            "M6 item 16 E1d (docs/automation-engine.md section 2.1): the bestiary's record of a creature kind holds these beside what the automation reads today (the pinned awareness ranges, the ground profiles' "
            + "pace and experience). The walker's creature feed reads the visual range beside the pinned one (E1f, --walk-awareness, default pinned); no other reader of the automation reads this table "
            + "yet; the next slices move them one at a time."));

    /// <summary>The tracked tables, each with its layout as it stands in the repository.</summary>
    public static readonly IReadOnlyList<TableFile> Files =
    [
        new("creatures.json", MemberLines: false,
        [
            new("columns", Shape.Names, new WeenieRows(Scope.Creatures, Creatures)),
            new("spell_columns", Shape.Names, new SpellRows(Spells)),
            new("spells", Shape.Inline, new SpellRows(Spells)),
            new("creatures", Shape.Tight, new WeenieRows(Scope.Creatures, Creatures)),
        ]),
        new("creature-resists.json", MemberLines: true,
        [
            new("columns", Shape.Names, new WeenieRows(Scope.Creatures, Resists, OnlyStated: true)),
            new("resists", Shape.Block, new WeenieRows(Scope.Creatures, Resists, OnlyStated: true)),
        ]),
        new("creature-skills.json", MemberLines: true,
        [
            new("skills", Shape.Block, new SkillRows(Skills)),
            new("weapons", Shape.Block, new WeenieRows(Scope.WieldedWeapons, Weapons, OnlyStated: true)),
        ]),
        new("creature-behaviour.json", MemberLines: true,
        [
            new("columns", Shape.Names, new WeenieRows(Scope.Creatures, Behaviour)),
            new("behaviour", Shape.Block, new WeenieRows(Scope.Creatures, Behaviour)),
        ])
        { StatedSources = BehaviourSources },
    ];
}

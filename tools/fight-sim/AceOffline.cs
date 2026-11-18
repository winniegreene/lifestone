// Follows ACEmulator/ACE@95ef426d Source/ACE.Server/Managers/WorldManager.cs (DoPlayerEnterWorld's restore of a player, the static constructor's physics engine) and Managers/GuidManager.cs (the dynamic allocator's
// fields) (AGPL-3.0), and Lifestone's loot simulator (tools/loot-sim on m5/loot-sim: the offline boot). No ACE source is copied (docs/third-party/fight-sim.md).

using System.Reflection;
using System.Runtime.CompilerServices;
using ACE.Common;
using ACE.Database;
using ACE.Database.Adapter;
using ACE.DatLoader;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Physics;
using ACE.Server.Physics.Common;
using ACE.Server.WorldObjects;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Layout;

namespace Lifestone.FightSim;

/// <summary>
/// Just enough of ACE to build real server objects and call its combat code, all in memory (Program.cs says what is never touched). The boot is the loot simulator's (M5 item 7), with the character's restore and a muted
/// session added.
/// </summary>
internal static class AceOffline
{
    /// <summary>Boots ACE's configuration, the dats and an in-memory GUID allocator.</summary>
    /// <param name="aceDir">The folder of the ACE build (its Config.js beside it).</param>
    public static void Boot(string aceDir)
    {
        // log4net: ACE's own loggers go to the console, errors only (a creature whose weenie lacks a row logs a warning per object, which would drown the report).
        // They go to the error stream, so that the report on the output stream stays the report.
        var repository = LogManager.GetRepository(Assembly.GetEntryAssembly()!);
        var appender = new ConsoleAppender { Target = ConsoleAppender.ConsoleError, Layout = new PatternLayout("ACE %level %logger{1}: %message%newline") };
        appender.ActivateOptions();
        BasicConfigurator.Configure(repository, appender);
        repository.Threshold = log4net.Core.Level.Error;

        // The dats' strings are code page 1252 (ACE.DatLoader BinaryReaderExtensions.ReadObfuscatedString), as ACE's Program.Main registers too.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // The server's own Config.js: the world and shard connections (127.0.0.1) and the dat folder. Never printed.
        ConfigManager.Initialize(Path.Combine(aceDir, "Config.js"));

        // The dats: the spell table (every spell's school, power, mana and formula), the motion tables (attack and cast animation lengths) and the combat tables (a creature's maneuvers). The cell dat is not needed.
        DatManager.Initialize(ConfigManager.Config.Server.DatFilesDirectory, keepOpen: true, loadCell: false);
        if (DatManager.PortalDat == null)
        {
            throw new InvalidOperationException("the portal dat did not open (ConfigManager.Config.Server.DatFilesDirectory)");
        }

        // The physics engine object, as WorldManager.Initialize makes it (server mode: no render polygons): a spell projectile's setup builds its physics parts through it. No landblock is ever loaded and nothing ticks.
        _ = new PhysicsEngine(new ObjectMaint(), new SmartBox()) { Server = true };

        InstallMemoryGuids();
    }

    /// <summary>
    /// WorldObjectFactory.CreateNewWorldObject takes a GUID from GuidManager's dynamic allocator (GuidManager.cs:353), whose constructor reads the shard's biota table (GuidManager.cs:143-190) and is built by
    /// GuidManager.Initialize, which the simulator never calls. This puts in its place an allocator object whose constructor is skipped, with an empty recycle queue and no sequence gaps, so Alloc (GuidManager.cs:202)
    /// counts up from ObjectGuid.DynamicMin in memory. The GUIDs mean nothing outside this process: no object is ever saved or entered into a world.
    /// </summary>
    private static void InstallMemoryGuids()
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var allocatorType = typeof(GuidManager).GetNestedType("DynamicGuidAllocator", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GuidManager.DynamicGuidAllocator is not in this ACE build");
        var allocator = RuntimeHelpers.GetUninitializedObject(allocatorType);

        void Set(string field, object value) => (allocatorType.GetField(field, Instance)
            ?? throw new InvalidOperationException($"DynamicGuidAllocator.{field} is not in this ACE build")).SetValue(allocator, value);
        Set("min", ObjectGuid.DynamicMin);
        Set("max", ObjectGuid.DynamicMax);
        Set("current", ObjectGuid.DynamicMin);
        Set("name", "fight-sim (memory only)");
        Set("recycledGuids", new Queue<Tuple<DateTime, uint>>());
        Set("availableIDs", new LinkedList<(uint start, uint end)>());

        (typeof(GuidManager).GetField("dynamicAlloc", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GuidManager.dynamicAlloc is not in this ACE build")).SetValue(null, allocator);
    }

    /// <summary>
    /// A session that sends nothing: ACE's player code reports to its client through <c>Session.Network.EnqueueSend</c> (a resisted spell, a hit taken), and a null session would stop it. Both objects are made without
    /// their constructors (no listener, no socket, no endpoint) and the network session is marked released, so every send returns at its first line (NetworkSession.cs:119).
    /// </summary>
    public static Session MutedSession()
    {
        var network = (NetworkSession)RuntimeHelpers.GetUninitializedObject(typeof(NetworkSession));
        (typeof(NetworkSession).GetField("isReleased", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NetworkSession.isReleased is not in this ACE build")).SetValue(network, true);
        var session = (Session)RuntimeHelpers.GetUninitializedObject(typeof(Session));
        session.Network = network;
        return session;
    }

    /// <summary>
    /// The character as the server last saved her (ACE saves a player every few minutes and at logout): her own row, her inventory and what she wields, read from the shard database with ACE's own reads (no SaveChanges
    /// is ever called), and restored with the constructor a login uses (WorldManager.cs:157). Her enchantments are the saved registry: the spells on her at the save.
    /// </summary>
    /// <param name="name">The character's name.</param>
    public static Player RestorePlayer(string name)
    {
        var shard = new ShardDatabase();
        var stub = shard.GetCharacterStubByName(name) ?? throw new ArgumentException($"no character named '{name}' in the shard database (deleted characters do not count)");
        var character = shard.GetCharacter(stub.Id);
        var row = shard.GetBiota(stub.Id, doNotAddToCache: true) ?? throw new ArgumentException($"the character '{name}' has no saved row");
        var possessions = shard.GetPossessedBiotasInParallel(stub.Id);
        var session = MutedSession();
        var player = new Player(BiotaConverter.ConvertToEntityBiota(row), possessions.Inventory, possessions.WieldedItems, character, session);

        // A spell's component order, and so its gestures and cast time, is hashed from the account's name (SpellFormula.GetPlayerFormula reads Session.Account). The name goes from the account row ACE's constructor
        // loaded into the muted session; it is never printed.
        (typeof(Session).GetProperty(nameof(Session.Account)) ?? throw new InvalidOperationException("Session.Account is not in this ACE build")).SetValue(session, player.Account.AccountName);

        // How she stands in a fight, stated: on the ground (not jumping: Player.IsJumping halves her defence), in magic combat mode with her caster out (Player_Combat.cs GetDefenseStanceMod is 1 in a combat mode;
        // SpellProjectile.GetAbsorbMod reads the mode for a caster's absorption). The physics object is the one entering the world makes; she is placed nowhere.
        player.InitPhysicsObj();
        player.PhysicsObj.TransientState |= TransientStateFlags.Contact | TransientStateFlags.OnWalkable;
        player.SetProperty(PropertyInt.CombatMode, (int)CombatMode.Magic);

        // M6 16 E5e3: the mode ACE's combat code reads is the creature's own CombatMode (Creature_Combat.cs:36, set to NonCombat by the constructor, Creature.cs:91 @95ef426d), not the property above: in NonCombat
        // her caster's Mana Conversion modifier and its melee defence modifier are not hers (WorldObject_Weapon.cs:86-90 and 241), so every mana cost and every evade was computed without them. Its setter is
        // protected and the public way in (Player.SetCombatMode) plays a motion in a world, so it is set as the constructor sets it.
        (typeof(Creature).GetProperty(nameof(Creature.CombatMode)) ?? throw new InvalidOperationException("Creature.CombatMode is not in this ACE build")).SetValue(player, CombatMode.Magic);
        return player;
    }
}

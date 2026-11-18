using System.Globalization;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Mods;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// M6 item 14 (m6-plan.md section 3.1, D124-D127, D138): the buff bot, a mage the mod places by Holtburg's lifestone, who answers a direct talk with a profile's word by casting that profile on the teller, from
/// level 60 for a credit (a Platinum Scarab given), by a player's rules (<see cref="BuffBotWire"/> says what it is, <see cref="BuffBotRules"/> holds the rules and the why, <see cref="BuffBotDesk"/> its day,
/// <see cref="BuffBotBody"/> its body in the world). ACE's public API only, and no patch: the bot is the mod's own subclass of ACE's creature built from a weenie held in memory; its emote sets hand each talk and
/// give to its code (<see cref="BuffBotRules.Answers"/>).
/// </summary>
/// <remarks>
/// <para><b>Nothing is written to a database.</b> The body is read from ACE's weenie cache (a read of the world data ACE loaded); the bot's weenie is a new object whose create list and emote list are
/// new lists (a cached weenie shares those two with every object made from it, ACE.Entity/Adapter/WeenieConverter.cs 77-81 @95ef426d, so they are never touched). Its object id is static and did not
/// come from the database, so ACE's landblock save passes it over (WorldObjects/WorldObject_Database.cs 103-130), and what it wears is saved only through a wielder that is saved. A buff it casts sits in
/// the player's own enchantments, which ACE saves with her as it saves her own casts. The bot's pack, purse and credits are the mod's state file (<see cref="BuffBotLedger"/>), under ACE's runtime folder.</para>
/// <para><b>Placing it again.</b> ACE destroys an unsaved object when its landblock unloads (Entity/Landblock.cs 1141-1145). A look each <see cref="BuffBotRules.LookSeconds"/>, in the world loop's
/// DelayManager stage as <see cref="ArrivalGuard"/>'s, places the bot when it does not stand and a player is on Holtburg's landblock; the placing itself runs on that landblock's own queue, as every
/// change to a landblock's objects does. A bot placed again starts at its post and rested; a round or a trip it had in hand is forgotten (its ledger is not).</para>
/// <para>Runs on the world thread and the landblock's. An exception would take the world loop down, so each is caught, logged and ends the bot's upkeep; a bot already standing stays.</para>
/// </remarks>
internal sealed class BuffBot
{
    /// <summary>The failed placings after which the upkeep stops (each is logged).</summary>
    private const int MaxFailures = 3;

    private readonly string statePath;
    private volatile bool stopped;
    private volatile bool placing;
    private volatile BuffBotBody? bot;
    private int failures;

    private BuffBot(string statePath, BuffBotLedger ledger)
    {
        this.statePath = statePath;
        Desk = new BuffBotDesk(ledger);
    }

    /// <summary>The bot's day: its phase, its pack and its payers.</summary>
    public BuffBotDesk Desk { get; }

    /// <summary>
    /// Starts the bot's upkeep unless <see cref="BuffBotRules.EnvironmentVariable"/> turns it off, or its state file cannot be read (named, and left as it is: the credits in it are the payers'); one line says which.
    /// Null when off.
    /// </summary>
    public static BuffBot? Start()
    {
        if (!BuffBotRules.IsOn(Environment.GetEnvironmentVariable(BuffBotRules.EnvironmentVariable)))
        {
            ModManager.Log($"{ModLog.BuffBot}off ({BuffBotRules.EnvironmentVariable}={BuffBotRules.Off}): no bot is placed", ModManager.LogLevel.Warn);
            return null;
        }

        string path;
        BuffBotLedger ledger;
        bool outfit;
        try
        {
            var mods = new DirectoryInfo(Path.GetFullPath(ModManager.ModPath));
            path = Path.Combine((mods.Parent ?? mods).FullName, BuffBotLedger.FileName);
            ledger = BuffBotLedger.Load(path, out outfit);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ModManager.Log($"{ModLog.BuffBot}not started: its state file ({BuffBotLedger.FileName} beside the mods folder) cannot be read: {e.Message}. Nothing is placed and the file is left as it is: fix or move it, then restart the server.", ModManager.LogLevel.Error);
            return null;
        }

        var keeper = new BuffBot(path, ledger);
        if (outfit)
        {
            keeper.Save("the monarchy's outfit: no state file yet");
        }

        ModManager.Log(string.Create(
            CultureInfo.InvariantCulture,
            $"{ModLog.BuffBot}on: {BuffBotWire.Name} (0x{BuffBotWire.Guid:X8}) is placed in cell 0x{BuffBotWire.Cell:X8} at ({BuffBotWire.X:0.#}, {BuffBotWire.Y:0.#}) while a player is in Holtburg; words: {string.Join(", ", BuffBotWire.Profiles.SelectMany(p => p.Words))}, {BuffBotWire.CreditWord}, {BuffBotWire.HelpWord}; the fee from level {BuffBotWire.FeeLevel}, a credit a {BuffBotWire.FeeName}; restock under {BuffBotRules.RestockUnder} tapers at Archmage Cindrue; state {path}{(outfit ? " (new: the outfit)" : string.Empty)}: {ledger.Summary()} ({BuffBotRules.EnvironmentVariable}={BuffBotRules.Off} keeps it out)"));
        keeper.Arm();
        return keeper;
    }

    /// <summary>Stops the upkeep; the look in flight is the last. A bot that stands stays until its landblock unloads or the server stops: it is in no save.</summary>
    public void Stop() => stopped = true;

    /// <summary>Writes the ledger to the state file; a failure is logged by name and the bot goes on (the next change writes it again).</summary>
    /// <param name="why">What changed, for the log on a failure.</param>
    public void Save(string why)
    {
        try
        {
            Desk.Ledger.Save(statePath, DateTime.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ModManager.Log($"{ModLog.BuffBot}the state file {statePath} was not written ({why}): {e.Message}", ModManager.LogLevel.Error);
        }
    }

    private void Arm() => WorldManager.DelayManager.EnqueueAction(new Pass(this));

    private void Step()
    {
        try
        {
            if (PlayerManager.GetOnlineCount() > 0)
            {
                Look();
            }

            if (!stopped)
            {
                Arm();
            }
        }
        catch (Exception e)
        {
            stopped = true;
            ModManager.Log($"{ModLog.BuffBot}the upkeep failed and stopped: {e}", ModManager.LogLevel.Error);
        }
    }

    private void Look()
    {
        var stands = placing || bot is { IsDestroyed: false, CurrentLandblock: not null };
        Landblock? block = null;
        foreach (var player in PlayerManager.GetAllOnline())
        {
            if (player.CurrentLandblock is { } on && on.Id.Landblock == BuffBotWire.Landblock)
            {
                block = on;
                break;
            }
        }

        if (!BuffBotRules.PlaceNow(stands, block is not null))
        {
            return;
        }

        placing = true;
        block!.EnqueueAction(new ActionEventDelegate(Place));
    }

    /// <summary>Check, act, check, on the landblock's queue: the body is in ACE's cache; the bot is made and enters the world; it stands on a landblock, dressed, with its answers and its skills.</summary>
    private void Place()
    {
        try
        {
            var body = DatabaseManager.World.GetCachedWeenie(BuffBotRules.BodyWcid);
            if (body is null)
            {
                Failed($"ACE's world data holds no weenie {BuffBotRules.BodyWcid} for the body");
                return;
            }

            var creature = new BuffBotBody(WeenieOf(body), new ObjectGuid(BuffBotWire.Guid), this);
            var at = new Position(BuffBotWire.Cell, BuffBotWire.X, BuffBotWire.Y, BuffBotWire.Z, 0f, 0f, 0f, 1f);
            at.PositionZ = at.GetTerrainZ();
            creature.Location = at;
            if (!creature.EnterWorld() || creature.CurrentLandblock is null)
            {
                creature.Destroy();
                Failed(string.Create(CultureInfo.InvariantCulture, $"the bot did not enter the world at cell 0x{BuffBotWire.Cell:X8} ({at.PositionX:0.#}, {at.PositionY:0.#}, {at.PositionZ:0.#})"));
                return;
            }

            Desk.Reset();
            bot = creature;
            failures = 0;
            creature.Begin(at);
            ModManager.Log(string.Create(
                CultureInfo.InvariantCulture,
                $"{ModLog.BuffBot}placed {creature.Name} 0x{creature.Guid.Full:X8} in cell 0x{creature.Location.Cell:X8} at ({at.PositionX:0.#}, {at.PositionY:0.#}, {at.PositionZ:0.##}); wears {creature.EquippedObjects.Count} of {BuffBotRules.Worn.Count} piece(s); {creature.Biota.PropertiesEmote?.Count ?? 0} answer(s); {creature.Stats()}; {Desk.Ledger.Summary()}; static id, in no save"));
        }
        catch (Exception e)
        {
            Failed("placing threw: " + e);
        }
        finally
        {
            placing = false;
        }
    }

    private void Failed(string why)
    {
        failures++;
        var last = failures >= MaxFailures;
        if (last)
        {
            stopped = true;
        }

        ModManager.Log($"{ModLog.BuffBot}not placed (try {failures} of {MaxFailures}{(last ? ", the upkeep stops" : string.Empty)}): {why}", ModManager.LogLevel.Error);
    }

    /// <summary>
    /// The bot's weenie: the body's values (its own copies of the dictionaries changed here, the rest the cache's, which ACE copies into the object as for any creature), a new create list of what it wears and a new
    /// emote list of its answers. Not attackable and with no targeting tactic it is an NPC to ACE (WorldObjects/Creature.cs 146), and it winds up a cast as a human does. D138: it accepts a give of the fee's ware
    /// (<c>AllowGive</c>), opens a door it walks into (<c>AiOptions</c> 1, Door.cs 252-262), its activation target is itself (the hooks), its magic skills, mana and mana rate are a mage's
    /// (<see cref="BuffBotRules"/>), and its book holds its own buffs and its dance (<see cref="BuffBotSelf.Book"/>: the owner, 2026-11-17; not attackable and with no targeting tactic it never fights, so
    /// ACE's monster magic, which alone reads a creature's book, Monster_Magic.cs 72-78, never casts from it).
    /// </summary>
    private static Weenie WeenieOf(Weenie body)
    {
        var bools = new Dictionary<PropertyBool, bool>(body.PropertiesBool ?? new Dictionary<PropertyBool, bool>())
        {
            [PropertyBool.Attackable] = false,
            [PropertyBool.AiUseHumanMagicAnimations] = true,
            [PropertyBool.AllowGive] = true,
        };
        bools.Remove(PropertyBool.AiAcceptEverything);
        var strings = new Dictionary<PropertyString, string>(body.PropertiesString ?? new Dictionary<PropertyString, string>())
        {
            [PropertyString.Name] = BuffBotWire.Name,
        };
        strings.Remove(PropertyString.Template);
        var ints = new Dictionary<PropertyInt, int>(body.PropertiesInt ?? new Dictionary<PropertyInt, int>())
        {
            [PropertyInt.AiOptions] = 1,
        };
        ints.Remove(PropertyInt.TargetingTactic);
        var iids = new Dictionary<PropertyInstanceId, uint>(body.PropertiesIID ?? new Dictionary<PropertyInstanceId, uint>())
        {
            [PropertyInstanceId.ActivationTarget] = BuffBotWire.Guid,
        };
        var floats = new Dictionary<PropertyFloat, double>(body.PropertiesFloat ?? new Dictionary<PropertyFloat, double>())
        {
            [PropertyFloat.ManaRate] = BuffBotRules.ManaRate,
        };
        var skills = new Dictionary<Skill, PropertiesSkill>();
        foreach (var (skill, value) in body.PropertiesSkill ?? new Dictionary<Skill, PropertiesSkill>())
        {
            skills[skill] = value.Clone();
        }

        skills[Skill.CreatureEnchantment] = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = BuffBotRules.MagicInitLevel };
        skills[Skill.LifeMagic] = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = BuffBotRules.MagicInitLevel };
        skills[Skill.ManaConversion] = new PropertiesSkill { SAC = SkillAdvancementClass.Trained, InitLevel = BuffBotRules.ManaConversionInitLevel };
        var vitals = new Dictionary<PropertyAttribute2nd, PropertiesAttribute2nd>();
        foreach (var (vital, value) in body.PropertiesAttribute2nd ?? new Dictionary<PropertyAttribute2nd, PropertiesAttribute2nd>())
        {
            vitals[vital] = value.Clone();
        }

        var self = body.PropertiesAttribute?.TryGetValue(PropertyAttribute.Self, out var s) == true ? s.InitLevel : 0;
        vitals[PropertyAttribute2nd.MaxMana] = new PropertiesAttribute2nd { InitLevel = BuffBotRules.ManaInitLevel, CurrentLevel = self + BuffBotRules.ManaInitLevel };

        return new Weenie
        {
            WeenieClassId = body.WeenieClassId,
            ClassName = body.ClassName,
            WeenieType = body.WeenieType,
            PropertiesBool = bools,
            PropertiesString = strings,
            PropertiesInt = ints,
            PropertiesDID = body.PropertiesDID,
            PropertiesFloat = floats,
            PropertiesIID = iids,
            PropertiesInt64 = body.PropertiesInt64,
            PropertiesAnimPart = body.PropertiesAnimPart,
            PropertiesPalette = body.PropertiesPalette,
            PropertiesTextureMap = body.PropertiesTextureMap,
            PropertiesAttribute = body.PropertiesAttribute,
            PropertiesAttribute2nd = vitals,
            PropertiesBodyPart = body.PropertiesBodyPart,
            PropertiesSkill = skills,
            PropertiesSpellBook = BuffBotSelf.Book.ToDictionary(id => (int)id, _ => 1f),
            PropertiesCreateList = [.. BuffBotRules.Worn.Select(wcid => new PropertiesCreateList { DestinationType = DestinationType.Wield, WeenieClassId = wcid, StackSize = 1 })],
            PropertiesEmote = [.. BuffBotRules.Answers().Select(EmoteOf)],
        };
    }

    /// <summary>One answer as ACE's emote set: a give of its ware, or a direct talk whose text is the word (no word: any text); certain; its steps the set's actions.</summary>
    private static PropertiesEmote EmoteOf(BuffBotAnswer answer) => new()
    {
        Category = answer.GiveWcid != 0 ? EmoteCategory.Give : EmoteCategory.ReceiveTalkDirect,
        WeenieClassId = answer.GiveWcid != 0 ? answer.GiveWcid : null,
        Probability = 1f,
        Quest = answer.Word,
        PropertiesEmoteAction = [.. answer.Steps.Select(ActionOf)],
    };

    private static PropertiesEmoteAction ActionOf(BuffBotStep step) => step.Kind switch
    {
        BuffBotStepKind.Turn => new PropertiesEmoteAction { Type = (uint)EmoteType.TurnToTarget },
        BuffBotStepKind.Tell => new PropertiesEmoteAction { Type = (uint)EmoteType.Tell, Message = step.Text },
        BuffBotStepKind.Stamp => new PropertiesEmoteAction { Type = (uint)EmoteType.StampMyQuest, Message = step.Text },
        BuffBotStepKind.Activate => new PropertiesEmoteAction { Type = (uint)EmoteType.Activate },
        _ => throw new InvalidOperationException($"TODO(m6-buffbot-step): no emote for the step {step.Kind}"),
    };

    /// <summary>One look: an action in the DelayManager stage, armed again after each.</summary>
    private sealed class Pass(BuffBot keeper) : DelayAction(BuffBotRules.LookSeconds)
    {
        public override Tuple<IActor, IAction> Act()
        {
            keeper.Step();
            return base.Act();
        }
    }
}

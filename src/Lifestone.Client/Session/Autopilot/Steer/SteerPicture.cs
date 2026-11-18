using System.Text.Json;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot.Steer;

/// <summary>What the live picture is drawn from.</summary>
/// <param name="Session">The session.</param>
/// <param name="Logic">The dial.</param>
/// <param name="Utc">The time.</param>
/// <param name="Sequence">The picture's number, rising.</param>
/// <param name="Run">The run folder's name.</param>
/// <param name="Build">This build's version.</param>
/// <param name="DeskOpen">True while the desk answers orders.</param>
/// <param name="RebuffMarginSeconds">The seconds left under which a buff counts as due.</param>
/// <param name="OverlayVersion">The standing rules' version.</param>
/// <param name="SeatedOverlay">The version in force in the scenario running; null before any.</param>
/// <param name="RunningOrder">The id of an order that has acted and not ended; null when none.</param>
/// <param name="ObserveOnly">True when this run refuses every order that could change what the character does.</param>
public readonly record struct SteerStatusInput(IGameSessionView Session, AutopilotHostLogic Logic, DateTimeOffset Utc, long Sequence, string Run, string Build, bool DeskOpen, double RebuffMarginSeconds, int OverlayVersion, int? SeatedOverlay, string? RunningOrder, bool ObserveOnly = false);

/// <summary>
/// The character and the dial as JSON (the steer contract, sections 6 and 8): <c>status.json</c> and the sections of <c>sheet</c>. Read-only over the session's read model: nothing here sends anything. A value the state does not hold yet is <c>null</c>, never a
/// guess. Fields that belong to later slices are written with their empty value: <c>rung.held</c> false, <c>desk.waiting</c> empty.
/// </summary>
public static class SteerPicture
{
    /// <summary>The sections <c>sheet</c> answers.</summary>
    public static IReadOnlyList<string> Sections { get; } = ["skills", "attributes", "spells", "buffs", "inventory", "equipped", "quests", RecentSection];

    /// <summary>The section the desk answers from its own history (<see cref="SteerHistory"/>): the last 15 minutes of game stats.</summary>
    public const string RecentSection = "recent";

    private const uint HealthMax = 1;
    private const uint StaminaMax = 3;
    private const uint ManaMax = 5;

    /// <summary>The live picture.</summary>
    /// <param name="input">Its sources.</param>
    public static string Status(in SteerStatusInput input)
    {
        var (session, logic, utc, sequence, run, build, deskOpen, margin, overlayVersion, seatedOverlay, runningOrder, observeOnly) = input;
        var state = session.State;
        var self = state.Self;
        var q = self.Qualities;
        var dial = logic.Status;
        var scenario = logic.Scenario;
        return SteerJson.Text(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("v", SteerOrder.Version);
            w.WriteString("at_utc", SteerJson.Utc(utc));
            w.WriteNumber("seq", sequence);
            w.WriteString("run", run);
            w.WriteString("build", build);
            w.WriteBoolean("observe_only", observeOnly);

            w.WriteStartObject("character");
            w.WriteString("name", self.Name);
            w.WriteString("guid", self.Guid == 0 ? null : SteerJson.Guid(self.Guid));
            w.WriteEndObject();

            w.WriteStartObject("session");
            w.WriteString("phase", state.Phase.ToString());
            w.WriteEndObject();

            w.WriteStartObject("dial");
            w.WriteString("mode", dial.Mode.ToString().ToLowerInvariant());
            w.WriteString("state", dial.State);
            w.WriteString("decision", dial.LastDecision);
            w.WriteString("next_trigger", dial.NextTrigger);
            w.WriteNumber("cycle", dial.Cycle);
            w.WriteString("job", dial.Job);
            WriteNullable(w, "seated_overlay", seatedOverlay);
            w.WriteBoolean("person_hold", logic.PersonHold);
            w.WriteBoolean("kill_switch", session.Actions.KillSwitch);
            w.WriteEndObject();

            w.WriteStartObject("place");
            w.WriteString("class", scenario is null ? "unknown" : AutopilotScenario.Classify(state, scenario.Ground).ToString().ToLowerInvariant());
            w.WriteString("cell", self.Position is { } at ? SteerJson.Guid(at.CellId) : null);
            w.WriteEndObject();

            var level = AutopilotHostLogic.LevelOf(session);
            w.WriteNumber("level", level);
            w.WriteStartObject("xp");
            w.WriteNumber("total", q.TotalExperience);
            w.WriteNumber("unassigned", q.AvailableExperience);
            WriteNullable(w, "to_next", XpToNext(session.XpTable?.CharacterLevelXpList, level, q.TotalExperience));
            w.WriteEndObject();

            if (q.TryGetVitae(out var vitae))
            {
                w.WriteNumber("vitae", vitae.Enchantment.StatModValue);
            }
            else
            {
                w.WriteNumber("vitae", 1.0);
            }

            w.WriteStartObject("vitals");
            WriteVital(w, "health", q, HealthMax);
            WriteVital(w, "stamina", q, StaminaMax);
            WriteVital(w, "mana", q, ManaMax);
            w.WriteEndObject();

            if (scenario?.Rung is { } rung)
            {
                w.WriteStartObject("rung");
                w.WriteString("key", rung.Key);
                w.WriteString("ground", rung.GroundKey);
                w.WriteNull("reason");
                w.WriteBoolean("held", false);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("rung");
            }

            WriteStock(w, state, scenario);
            WriteNullable(w, "burden", self.Inventory.Burden);
            WriteNullable(w, "free_slots", scenario?.Counters.FreeSlots is { } free and >= 0 ? free : null);
            w.WriteNumber("pyreals", q.CoinValue);

            CountBuffs(q, session.Clock.Now, margin, out var up, out var due);
            w.WriteStartObject("buffs");
            w.WriteNumber("up", up);
            w.WriteNumber("due", due);
            w.WriteEndObject();

            w.WriteStartObject("overlay");
            w.WriteNumber("version", overlayVersion);
            w.WriteEndObject();

            w.WriteStartObject("desk");
            w.WriteBoolean("open", deskOpen);
            w.WriteStartArray("waiting");
            w.WriteEndArray();
            w.WriteString("running", runningOrder);
            w.WriteEndObject();

            w.WriteEndObject();
        });
    }

    /// <summary>One section of the character sheet.</summary>
    /// <param name="section">One of <see cref="Sections"/>.</param>
    /// <param name="session">The session.</param>
    /// <param name="timers">The quest timers, or null when none are known.</param>
    public static string Sheet(string section, IGameSessionView session, QuestTimers? timers)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(session);
        var utc = session.Clock.Time.GetUtcNow();
        return SteerJson.Text(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("v", SteerOrder.Version);
            w.WriteString("section", section);
            w.WriteString("at_utc", SteerJson.Utc(utc));
            switch (section)
            {
                case "skills":
                    WriteSkills(w, session);
                    break;
                case "attributes":
                    WriteAttributes(w, session.State.Self.Qualities);
                    break;
                case "spells":
                    WriteSpells(w, session);
                    break;
                case "buffs":
                    WriteBuffs(w, session);
                    break;
                case "inventory":
                    WriteInventory(w, session.State);
                    break;
                case "equipped":
                    WriteEquipped(w, session.State);
                    break;
                case "quests":
                    WriteQuests(w, timers, utc);
                    break;
                default:
                    throw new ArgumentException($"no such section: {section}", nameof(section));
            }

            w.WriteEndObject();
        });
    }

    /// <summary>The experience still needed for the next level by the XpTable's cumulative list, or null without a table or past its end.</summary>
    /// <param name="levelXp">The XpTable's <c>CharacterLevelXpList</c>, or null.</param>
    /// <param name="level">The level.</param>
    /// <param name="total">The total experience.</param>
    public static long? XpToNext(IReadOnlyList<ulong>? levelXp, int level, long total)
    {
        if (levelXp is null || level < 1 || level + 1 >= levelXp.Count)
        {
            return null;
        }

        return Math.Max(0, (long)levelXp[level + 1] - total);
    }

    /// <summary>How many timed enchantments are up and how many are inside the re-buff margin; the permanent ones and the vitae are neither.</summary>
    /// <param name="q">The qualities.</param>
    /// <param name="now">Session-clock seconds.</param>
    /// <param name="margin">The margin in seconds.</param>
    /// <param name="up">The enchantments up.</param>
    /// <param name="due">Those due.</param>
    public static void CountBuffs(PlayerQualities q, double now, double margin, out int up, out int due)
    {
        ArgumentNullException.ThrowIfNull(q);
        (up, due) = (0, 0);
        foreach (var active in q.ActiveEnchantments)
        {
            if (active.IsPermanent || active.Enchantment.Bucket == Lifestone.Net.Messages.EnchantmentBucket.Vitae)
            {
                continue;
            }

            up++;
            if (active.ExpirySeconds - now < margin)
            {
                due++;
            }
        }
    }

    private static void WriteNullable(Utf8JsonWriter w, string name, long? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static void WriteVital(Utf8JsonWriter w, string name, PlayerQualities q, uint maxId)
    {
        if (q.TryGetVitalLevels(maxId, out var current, out var max))
        {
            w.WriteStartArray(name);
            w.WriteNumberValue(current);
            w.WriteNumberValue(max);
            w.WriteEndArray();
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static void WriteStock(Utf8JsonWriter w, GameState state, AutopilotScenario? scenario)
    {
        w.WriteStartArray("stock");
        if (scenario is { Counters: { Active: true } counters })
        {
            var scratch = new List<uint>();
            for (var i = 0; i < counters.LineCount; i++)
            {
                var line = counters.LineAt(i);
                w.WriteStartObject();
                w.WriteString("name", line.Name);
                w.WriteNumber("held", GearInventory.CountOf(state, scratch, line.Wcid));
                w.WriteNumber("trip_under", line.TripUnder);
                w.WriteNumber("stop_under", line.StopUnder);
                w.WriteEndObject();
            }
        }

        w.WriteEndArray();
    }

    private static void WriteSkills(Utf8JsonWriter w, IGameSessionView session)
    {
        var q = session.State.Self.Qualities;
        var table = session.SkillTable;
        w.WriteStartArray("skills");
        foreach (var skill in q.Skills.Values.OrderBy(s => s.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("id", skill.Id);
            var entry = table?.Skills.FirstOrDefault(s => s.Id == skill.Id);
            w.WriteString("name", entry?.Name);
            w.WriteNumber("class", skill.AdvancementClass);
            w.WriteNumber("ranks", skill.Ranks);
            w.WriteNumber("xp", skill.Experience);
            if (entry is not null && SkillRules.TryBase(q, skill.Id, entry.Formula, entry.MinLevel, out var at))
            {
                w.WriteNumber("base", at);
            }
            else
            {
                w.WriteNull("base");
            }

            if (entry is not null && SkillRules.TryCurrent(q, skill.Id, entry.Formula, entry.MinLevel, out var now))
            {
                w.WriteNumber("current", now);
            }
            else
            {
                w.WriteNull("current");
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteNumber("credits", q.AvailableSkillCredits);
    }

    private static void WriteAttributes(Utf8JsonWriter w, PlayerQualities q)
    {
        w.WriteStartArray("attributes");
        foreach (var a in q.Attributes.Values.OrderBy(a => a.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("id", a.Id);
            w.WriteNumber("start", a.Start);
            w.WriteNumber("ranks", a.Ranks);
            w.WriteNumber("xp", a.Experience);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("vitals");
        foreach (var v in q.Vitals.Values.OrderBy(v => v.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("id", v.Id);
            w.WriteNumber("start", v.Start);
            w.WriteNumber("ranks", v.Ranks);
            w.WriteNumber("xp", v.Experience);
            w.WriteNumber("current", v.Current);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteSpells(Utf8JsonWriter w, IGameSessionView session)
    {
        w.WriteBoolean("table_ready", session.SpellTableReady);
        w.WriteStartArray("spells");
        foreach (var id in session.State.Self.Spellbook.Spells.Keys.OrderBy(k => k))
        {
            var spell = session.Spell(id);
            w.WriteStartObject();
            w.WriteNumber("id", id);
            w.WriteString("name", spell?.Name);
            w.WriteString("school", spell?.School.ToString());
            if (spell is not null)
            {
                w.WriteNumber("power", spell.Power);
                w.WriteNumber("mana", spell.BaseMana);
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteBuffs(Utf8JsonWriter w, IGameSessionView session)
    {
        var now = session.Clock.Now;
        w.WriteStartArray("buffs");
        foreach (var active in session.State.Self.Qualities.ActiveEnchantments.OrderBy(a => a.ExpirySeconds))
        {
            w.WriteStartObject();
            w.WriteNumber("spell", active.SpellId);
            w.WriteString("name", session.Spell(active.SpellId)?.Name);
            w.WriteNumber("layer", active.Layer);
            w.WriteString("bucket", active.Enchantment.Bucket.ToString());
            w.WriteNumber("power", active.Enchantment.PowerLevel);
            if (active.IsPermanent)
            {
                w.WriteNull("remaining_s");
            }
            else
            {
                w.WriteNumber("remaining_s", Math.Round(active.ExpirySeconds - now, 1));
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteInventory(Utf8JsonWriter w, GameState state)
    {
        var inventory = state.Self.Inventory;
        w.WriteBoolean("lists_complete", inventory.ListsComplete);
        w.WriteStartArray("pack");
        foreach (var entry in inventory.Pack)
        {
            WriteItem(w, state, entry.Guid, inventory.Owner);
        }

        w.WriteEndArray();
        w.WriteStartArray("side_packs");
        foreach (var pack in inventory.SidePacks)
        {
            w.WriteStartObject();
            w.WriteString("guid", SteerJson.Guid(pack.Guid));
            w.WriteString("name", state.Objects.TryGet(pack.Guid, out var named) ? named.Name : null);
            w.WriteStartArray("items");
            foreach (var entry in inventory.ItemsIn(pack.Guid))
            {
                WriteItem(w, state, entry.Guid, pack.Guid);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteItem(Utf8JsonWriter w, GameState state, uint guid, uint container)
    {
        w.WriteStartObject();
        w.WriteString("guid", SteerJson.Guid(guid));
        w.WriteString("container", SteerJson.Guid(container));
        if (state.Objects.TryGet(guid, out var item))
        {
            w.WriteString("name", item.Name);
            w.WriteNumber("wcid", item.Weenie.WeenieClassId);
        }
        else
        {
            w.WriteNull("name");
            w.WriteNull("wcid");
        }

        if (state.Self.Inventory.TryGetStack(guid, out var stack, out var value))
        {
            w.WriteNumber("stack", stack);
            w.WriteNumber("value", value);
        }

        w.WriteEndObject();
    }

    private static void WriteEquipped(Utf8JsonWriter w, GameState state)
    {
        w.WriteStartArray("equipped");
        foreach (var worn in state.Self.Inventory.Equipped)
        {
            w.WriteStartObject();
            w.WriteString("guid", SteerJson.Guid(worn.Guid));
            w.WriteNumber("location", worn.Location);
            w.WriteNumber("priority", worn.Priority);
            if (state.Objects.TryGet(worn.Guid, out var item))
            {
                w.WriteString("name", item.Name);
                w.WriteNumber("wcid", item.Weenie.WeenieClassId);
            }
            else
            {
                w.WriteNull("name");
                w.WriteNull("wcid");
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteQuests(Utf8JsonWriter w, QuestTimers? timers, DateTimeOffset utc)
    {
        w.WriteBoolean("timers_known", timers is not null);
        w.WriteStartArray("quests");
        foreach (var quest in QuestCatalog.All.OrderBy(q => q.Key, StringComparer.Ordinal))
        {
            var entry = timers?.Get(quest.Key);
            w.WriteStartObject();
            w.WriteString("key", quest.Key);
            w.WriteString("name", quest.Name);
            WriteNullable(w, "timer_s", quest.TimerSeconds);
            w.WriteNumber("completions", entry?.Completions ?? 0);
            w.WriteString("last_completed_utc", entry is null ? null : SteerJson.Utc(entry.LastCompleted));
            w.WriteString("next_due_utc", entry?.NextDue is { } due ? SteerJson.Utc(due) : null);
            w.WriteString("how", entry?.How);
            w.WriteBoolean("done_once_only", entry is { NextDue: null });
            w.WriteBoolean("due_now", entry is null || entry.NextDue is { } next && utc >= next);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }
}

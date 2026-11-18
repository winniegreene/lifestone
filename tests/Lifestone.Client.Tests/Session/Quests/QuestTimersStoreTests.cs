using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// The quest timers' store on the journey's context (M6 item 5 Q2; closes <c>m6-pathwarden-timers-save</c>): a station's completion is recorded, saved through the host's store and logged;
/// a fresh context built on the same store loads it; a store file that does not parse stops the run by name and is left as it is; a context with no store logs <c>TODO(m6-quest-timers-no-store)</c> once and keeps the timers in memory.
/// The Pathwarden's own completion goes through the same <see cref="JourneyContext.CompleteQuest"/> call (<c>PathwardenRun</c>), proved here with its quest file.
/// </summary>
public sealed class QuestTimersStoreTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 4, 14, 0, 0, TimeSpan.Zero);

    private static JourneyContext Context(Recording log, IAutopilotCheckpointStore? store) =>
        JourneyScenario.NewContext(JourneyPlans.Mage, log, new Dictionary<uint, SpellInfo>(), questStore: store);

    [Fact]
    public void A_completion_is_saved_through_the_store_logged_and_loaded_by_a_fresh_context()
    {
        var store = new MemoryCheckpointStore();
        var log = new Recording();
        var quest = QuestCatalog.Get("pathwarden"); // the Pathwarden station's completion: Context.CompleteQuest(quest, now, HowItem)
        var context = Context(log, store);
        Assert.NotNull(context.Quests);
        Assert.Same(store, context.QuestStore);

        var entry = context.CompleteQuest(quest, At, QuestTimers.HowItem);

        Assert.Equal(1, store.Writes);
        Assert.Equal(1, entry.Completions);
        Assert.Contains(log.Lines, l => l.StartsWith("quest key=pathwarden", StringComparison.Ordinal) && l.Contains("saved=yes", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("m6-quest-timers-no-store", StringComparison.Ordinal));

        var fresh = Context(new Recording(), store);
        var loaded = Assert.IsType<QuestTimerEntry>(fresh.Quests!.Get("pathwarden"));
        Assert.Equal(At, loaded.LastCompleted);
        Assert.Equal(QuestTimers.HowItem, loaded.How);
        Assert.False(fresh.Quests.IsDue(quest, At.AddDays(30))); // done once: never due again
    }

    [Fact]
    public void A_malformed_store_file_stops_by_name_and_is_never_overwritten()
    {
        var store = new MemoryCheckpointStore { Json = "{ \"schema\": 1, not json" };
        var stop = Assert.Throws<InvalidDataException>(() => Context(new Recording(), store));
        Assert.Contains("TODO(m6-quest-timers-unreadable)", stop.Message, StringComparison.Ordinal);
        Assert.Contains(store.Describe, stop.Message, StringComparison.Ordinal);
        Assert.Equal("{ \"schema\": 1, not json", store.Json);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void A_context_with_no_store_logs_the_todo_once_and_keeps_the_timers_in_memory()
    {
        var log = new Recording();
        var quest = QuestCatalog.Get("pathwarden");
        var context = Context(log, null);
        Assert.Null(context.Quests);

        context.CompleteQuest(quest, At, QuestTimers.HowItem);
        var again = context.WithDungeon(GroundCatalog.All.First(g => !ReferenceEquals(g, context.Dungeon))); // the sibling contexts of one run share the note and the timers
        again.CompleteQuest(quest, At.AddHours(1), QuestTimers.HowItem);

        Assert.Single(log.Lines, l => l.Contains("TODO(m6-quest-timers-no-store)", StringComparison.Ordinal));
        Assert.Equal(2, context.QuestTimersInUse.Get("pathwarden")!.Completions);
        Assert.Same(context.QuestTimersInUse, again.QuestTimersInUse);
        Assert.Contains(log.Lines, l => l.Contains("saved=no", StringComparison.Ordinal));
    }

    [Fact]
    public void A_write_that_fails_is_logged_by_name_and_thrown()
    {
        var store = new MemoryCheckpointStore { FailWrites = true };
        var log = new Recording();
        var context = Context(log, store);
        Assert.Throws<IOException>(() => context.CompleteQuest(QuestCatalog.Get("pathwarden"), At, QuestTimers.HowItem));
        Assert.Contains(log.Lines, l => l.Contains("TODO(m6-quest-timers-save-failed)", StringComparison.Ordinal));
    }

    private sealed class Recording : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }
}

using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.2 s5: the committed loader of the recorded PlayerDescription (<see cref="RecordedDescription"/>) and the stand-in sending it. The loader tests over a real recording skip without one (M2 D14: recordings are
/// never committed); the others use a description built here.
/// </summary>
public sealed class RecordedDescriptionTests(ITestOutputHelper output)
{
    private const uint RecordedPlayer = 0x50000077;
    private const uint PackItem = 0x80000101;
    private const uint WornItem = 0x80000102;

    [RequiresRecordingFact]
    public void The_loader_finds_the_recording_whose_description_holds_the_most_enchantments_and_logs_its_name_and_the_count()
    {
        var folder = Path.GetDirectoryName(RealRecordings.Find()[0])!;
        var expected = RealRecordings.Find().Max(path => RealRecordings.QualitiesAtDescription(path)?.ActiveEnchantments.Length ?? -1);
        var log = new RecordingLogSink();

        var recorded = RecordedDescription.TryFind(RealRecordings.Sources(), log);

        Assert.NotNull(recorded);
        output.WriteLine($"{recorded.FileName}: {recorded.EnchantmentCount} enchantments, {recorded.Description.Spells.Count} spells, pack {recorded.Description.Inventory.Length}, equipped {recorded.Description.Equipped.Length}, item objects {recorded.Items.Length}, not in the recording {recorded.MissingItems.Length}");
        Assert.Equal(expected, recorded.EnchantmentCount);
        Assert.True(recorded.EnchantmentCount > 0);
        var line = Assert.Single(log.Entries, e => e.Message.Contains("recorded PlayerDescription", StringComparison.Ordinal)).Line;
        Assert.Contains("file=" + recorded.FileName, line, StringComparison.Ordinal);
        Assert.Contains("enchantments=" + recorded.EnchantmentCount.ToString(CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
                Assert.DoesNotContain(folder, line, StringComparison.OrdinalIgnoreCase);

        // The rewrite: nothing is left of the recorded player's guid, and the contained objects are the description's.
        const uint player = StandInSessions.FirstCharacterGuid;
        var listed = recorded.Description.Inventory.Select(c => c.Guid).Concat(recorded.Description.Equipped.Select(e => e.Guid)).ToHashSet();
        foreach (var item in recorded.ItemsFor(player))
        {
            Assert.Contains(item.Guid, listed);
            if (recorded.RecordedPlayer != player) // the first character of a fresh ACE has the stand-in's own guid: then the rewrite is the identity
            {
                Assert.NotEqual(recorded.RecordedPlayer, item.Weenie.ContainerId);
                Assert.NotEqual(recorded.RecordedPlayer, item.Weenie.WielderId);
                Assert.NotEqual(recorded.RecordedPlayer, item.Physics.Parent?.Guid);
            }
        }
    }

    [Fact]
    public void Without_a_recording_the_loader_says_so_once_and_returns_nothing()
    {
        var log = new RecordingLogSink();

        Assert.Null(RecordedDescription.TryFind([], log));

        var line = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Info, line.Severity);
        Assert.Contains("synthetic description", line.Line, StringComparison.Ordinal);
        Assert.Contains("synthetic_enchantments=138", line.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_source_that_is_not_a_recording_or_cannot_be_opened_is_skipped_not_thrown()
    {
        var log = new RecordingLogSink();
        RecordingSource[] sources =
        [
            new("20260101T000000Z-headless.lsrec", () => new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20])),
            new("20260102T000000Z-headless.lsrec", () => new MemoryStream([])),
            new("20260103T000000Z-headless.lsrec", () => throw new IOException("locked")),
        ];

        Assert.Null(RecordedDescription.TryFind(sources, log));

        Assert.Contains(log.Entries, e => e.Fields.Any(f => f.Key == "unreadable_or_without_description" && Equals(f.Value, 3)));
    }

    [Fact]
    public void The_stand_in_sends_the_recorded_description_for_its_own_player_with_the_listed_objects_rewritten()
    {
        var registry = StandInPlayerStats.LiveShapedEnchantments();
        var recorded = RecordedDescription.Of(
            "20260101T000000Z-headless.lsrec",
            new PlayerDescriptionMsg
            {
                Enchantments = registry,
                Attributes = [new AttributeDesc(2, 0, 77, 0)],
                Inventory = [new ContentDesc(PackItem, 0)],
                Equipped = [new EquippedDesc(WornItem, 0x100000, 1)],
            },
            RecordedPlayer,
            [Item(PackItem, container: RecordedPlayer, wielder: null), Item(WornItem, container: null, wielder: RecordedPlayer, parent: RecordedPlayer)]);
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Recorded = recorded };
        var clock = new ManualTimeProvider();
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
            },
            clock,
            new RecordingLogSink());

        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        var q = session.State.Self.Qualities;
        Assert.Equal(StandInSessions.FirstCharacterGuid, session.State.Self.Guid);
        Assert.Equal(registry.Length, q.Enchantments.Length);
        Assert.Equal(registry.Length, q.ActiveEnchantments.Length);
        Assert.True(q.TryGetAttribute(2, out var endurance) && endurance.Start == 77, "the recorded attribute, not the synthetic Endurance 120");
        Assert.Equal([PackItem], session.State.Self.Inventory.Pack.Select(e => e.Guid));
        Assert.True(session.State.Objects.TryGet(PackItem, out var pack));
        Assert.Equal(StandInSessions.FirstCharacterGuid, pack.ContainerId);
        Assert.True(session.State.Objects.TryGet(WornItem, out var worn));
        Assert.Equal(StandInSessions.FirstCharacterGuid, worn.Weenie.WielderId);
        Assert.Equal(StandInSessions.FirstCharacterGuid, worn.Physics.Parent?.Guid);
    }

    private static CreateObjectMsg Item(uint guid, uint? container, uint? wielder, uint? parent = null) => new(
        guid,
        ModelData.Empty,
        new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.Gravity, Parent = parent is { } p ? new ObjectLink(p, 1) : null, Timestamps = default },
        new WeenieDesc { Header = WeenieHeaderFlags.None, WeenieClassId = 3, IconId = 0x06000001, ItemType = 1, ObjectDescriptionFlags = 0, Name = "Item " + guid.ToString("X", CultureInfo.InvariantCulture), ContainerId = container, WielderId = wielder });
}

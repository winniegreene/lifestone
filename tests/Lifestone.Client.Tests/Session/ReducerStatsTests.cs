using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The player's level, experience and skill credits (M3 1.1 s1b; spec 5.3): read from the PlayerDescription's PropertyInt 25 and 24 and PropertyInt64 1 and 2,
/// kept current by PrivateUpdatePropertyInt (0x02CD) and PrivateUpdatePropertyInt64 (0x02CF), and announced as the other quality updates are
/// (<see cref="QualitiesChanged"/>).
/// </summary>
public sealed class ReducerStatsTests
{
    private const long BigExperience = 4_286_609_098; // the XP to level 126: it does not fit an Int32

    private static PlayerDescriptionMsg Description() => new()
    {
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(25, 7).Add(24, 4),
        Int64Properties = ImmutableDictionary<uint, long>.Empty.Add(1, BigExperience).Add(2, 1_234),
    };

    private static ReducerHarness Known(out List<QualitiesChanged> changes)
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        var seen = new List<QualitiesChanged>();
        h.Bus.Subscribe<QualitiesChanged>((in QualitiesChanged c) => seen.Add(c));
        changes = seen;
        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription), Description());
        return h;
    }

    private static void Update(ReducerHarness h, PrivateUpdateKind kind, byte sequence, uint key, long value) =>
        h.Reducer.OnPrivateUpdate(h.Context, new PrivateUpdateMsg(kind, sequence, key, value, default, default, default));

    [Fact]
    public void Unknown_until_a_description_arrives_all_four_read_zero()
    {
        var q = new ReducerHarness().State.Self.Qualities;
        Assert.Equal((0, 0, 0L, 0L), (q.Level, q.AvailableSkillCredits, q.AvailableExperience, q.TotalExperience));
    }

    [Fact]
    public void The_description_sets_the_four_values_and_total_experience_is_a_64_bit_number()
    {
        var h = Known(out var changes);
        var q = h.State.Self.Qualities;
        Assert.Equal((7, 4, 1_234L, BigExperience), (q.Level, q.AvailableSkillCredits, q.AvailableExperience, q.TotalExperience));
        Assert.Equal([new QualitiesChanged(QualityChange.Description, 0)], changes);
        Assert.True(q.TryGetInt(PlayerQualities.LevelProperty, out var level) && level == 7);
    }

    [Fact]
    public void The_private_updates_keep_the_values_current_and_each_raises_its_quality_event()
    {
        var h = Known(out var changes);
        changes.Clear();
        var q = h.State.Self.Qualities;

        Update(h, PrivateUpdateKind.PropertyInt, 1, PlayerQualities.LevelProperty, 8);
        Update(h, PrivateUpdateKind.PropertyInt, 1, PlayerQualities.AvailableSkillCreditsProperty, 6);
        Update(h, PrivateUpdateKind.PropertyInt64, 1, PlayerQualities.AvailableExperienceProperty, 99);
        Update(h, PrivateUpdateKind.PropertyInt64, 1, PlayerQualities.TotalExperienceProperty, BigExperience + 500);

        Assert.Equal((8, 6, 99L, BigExperience + 500), (q.Level, q.AvailableSkillCredits, q.AvailableExperience, q.TotalExperience));
        Assert.Equal(
            [
                new QualitiesChanged(QualityChange.Int, PlayerQualities.LevelProperty),
                new QualitiesChanged(QualityChange.Int, PlayerQualities.AvailableSkillCreditsProperty),
                new QualitiesChanged(QualityChange.Int64, PlayerQualities.AvailableExperienceProperty),
                new QualitiesChanged(QualityChange.Int64, PlayerQualities.TotalExperienceProperty),
            ],
            changes);
    }

    [Fact]
    public void A_stale_update_is_dropped_and_changes_nothing()
    {
        var h = Known(out var changes);
        var q = h.State.Self.Qualities;
        Update(h, PrivateUpdateKind.PropertyInt64, 10, PlayerQualities.AvailableExperienceProperty, 500);
        changes.Clear();
        Update(h, PrivateUpdateKind.PropertyInt64, 9, PlayerQualities.AvailableExperienceProperty, 9_999); // older than the one applied
        Assert.Equal(500, q.AvailableExperience);
        Assert.Empty(changes);
    }

    [Fact]
    public void A_new_description_replaces_the_values_and_a_description_without_them_reads_zero()
    {
        var h = Known(out _);
        var q = h.State.Self.Qualities;
        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg());
        Assert.Equal((0, 0, 0L, 0L), (q.Level, q.AvailableSkillCredits, q.AvailableExperience, q.TotalExperience));
    }
}

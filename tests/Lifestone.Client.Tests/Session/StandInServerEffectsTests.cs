using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.3 s0: the stand-in server sends an F755 and an F750 on the player in ACE's order, after the unhide (<see cref="StandInVisit.Scripts"/>, <see cref="StandInVisit.Sounds"/>), and the recorded-XP placeholder
/// (M3 1.2 review R8) is not silent.
/// </summary>
public sealed class StandInServerEffectsTests
{
    private const uint LevelUp = 138;
    private const uint EnchantUp = 90;

    [Fact]
    public void The_stand_in_plays_a_script_then_a_sound_on_the_player_after_the_unhide_and_gives_the_player_the_human_sound_table()
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            Scripts = [new StandInScript(0.5, 0, LevelUp, 1f)],
            Sounds = [new StandInSound(0.5, 0, EnchantUp, 0.75f)],
        };
        var clock = new ManualTimeProvider();
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
            },
            clock,
            new RecordingLogSink());
        var order = new List<string>();
        var times = new List<double>();
        using var scripts = session.Events.Subscribe((in ObjectScriptPlayed e) =>
        {
            order.Add($"script {e.Guid:X8} {e.ScriptType} {e.Speed}");
            times.Add(session.Clock.Now);
        });
        using var sounds = session.Events.Subscribe((in ObjectSoundPlayed e) =>
        {
            order.Add($"sound {e.Guid:X8} {e.SoundId} {e.Volume}");
            times.Add(session.Clock.Now);
        });

        for (var i = 0; i < 40; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        var player = StandInSessions.FirstCharacterGuid;
        Assert.Equal([$"script {player:X8} {LevelUp} 1", $"sound {player:X8} {EnchantUp} 0.75"], order);
        Assert.True(times[0] >= 1.7 - 1e-9, $"after the unhide (0.2 + 1.0) plus 0.5 s, was {times[0]}");
        Assert.True(session.State.Objects.TryGet(player, out var self));
        Assert.Equal(StandInVisit.HumanSoundTable, self.Physics.SoundTableId);
    }

    [Fact]
    public void A_visit_without_effects_sends_none()
    {
        var clock = new ManualTimeProvider();
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 });
            },
            clock,
            new RecordingLogSink());
        var heard = 0;
        using var scripts = session.Events.Subscribe((in ObjectScriptPlayed _) => heard++);
        using var sounds = session.Events.Subscribe((in ObjectSoundPlayed _) => heard++);
        for (var i = 0; i < 40; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        Assert.Equal(0, heard);
    }

    [Fact]
    public void Making_up_the_recorded_characters_experience_is_logged_once_as_a_named_todo()
    {
        var recorded = RecordedDescription.Of("20260101T000000Z-headless.lsrec", new PlayerDescriptionMsg(), 0x50000077, []);
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            Stats = new StandInPlayerStats { Level = 1, TotalExperience = 5_000, AvailableExperience = 5_000 },
            Recorded = recorded,
        };
        var log = new RecordingLogSink();
        var clock = new ManualTimeProvider();
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.AnswerWorld(h, visit);
                StandInSessions.PlayerVisits(h, visit);
            },
            clock,
            log);
        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        var line = Assert.Single(log.Entries, e => e.Message.Contains("TODO(m3-standin-recorded-xp)", StringComparison.Ordinal));
        Assert.Contains("PropertyInt64 1", line.Message, StringComparison.Ordinal);
        Assert.Equal(5_000, session.State.Self.Qualities.TotalExperience);
    }
}

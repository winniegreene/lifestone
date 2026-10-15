using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The parts of the read model the contract already implements: the session clock, the logs' rings, the server clock,
/// the character list lookups, the player's echoed sequences and the object table's queries.
/// </summary>
public sealed class SessionReadModelTests
{
    [Fact]
    public void The_session_clock_starts_at_zero_and_follows_its_provider()
    {
        var time = new ManualTimeProvider();
        time.Advance(TimeSpan.FromSeconds(100));
        var clock = new SessionClock(time);
        Assert.Equal(0, clock.Now);

        var before = time.GetTimestamp();
        time.Advance(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(1.5, clock.Now, 9);
        Assert.Equal(0, clock.ToSeconds(before), 9);
        Assert.Equal(-100, clock.ToSeconds(before - (100 * time.TimestampFrequency)), 9);
        Assert.Same(time, clock.Time);
    }

    [Fact]
    public void The_chat_keeps_the_last_500_lines_oldest_first()
    {
        var chat = new ChatLog();
        for (var i = 0; i < ChatLog.Capacity + 3; i++)
        {
            chat.Add(new ChatLine(i, ChatKind.Speech, 2, "Tester", 1, "line " + i));
        }

        Assert.Equal(ChatLog.Capacity, chat.Count);
        Assert.Equal(ChatLog.Capacity + 3, chat.Total);
        Assert.Equal("line 3", chat[0].Text);
        Assert.Equal("line 502", chat[chat.Count - 1].Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => chat[chat.Count]);
        chat.Clear();
        Assert.Equal(0, chat.Count);
    }

    [Fact]
    public void The_action_log_keeps_the_last_1024_entries()
    {
        var log = new ActionLog();
        for (var i = 1; i <= ActionLog.Capacity + 1; i++)
        {
            log.Add(new ActionLogEntry(i, ActionKind.Talk, ActionSource.Automation, "Talk chars=5", new ActionOutcome(ActionOutcomeKind.Confirmed), i, i + 0.25));
        }

        Assert.Equal(ActionLog.Capacity, log.Count);
        Assert.Equal(ActionLog.Capacity + 1, log.Total);
        Assert.Equal(2, log[0].Id);
        Assert.Equal(250, log[0].Milliseconds, 9);
    }

    [Fact]
    public void The_server_clock_runs_on_from_the_last_time_heard()
    {
        var time = new ServerTimeState();
        Assert.Null(time.ServerSecondsAt(5));
        time.Update(serverSeconds: 1000, clientSeconds: 2);
        Assert.Equal(1003.5, time.ServerSecondsAt(5.5));
        time.Reset();
        Assert.False(time.IsKnown);
    }

    [Fact]
    public void The_character_list_finds_a_character_s_server_index()
    {
        var list = new CharacterListState();
        list.Mutable.Add(new CharacterSlot(0x50000001, "First", 0));
        list.Mutable.Add(new CharacterSlot(0x50000002, "Second", 1));
        Assert.Equal(1, list.IndexOf(0x50000002));
        Assert.Equal(-1, list.IndexOf(0x50000003));
        Assert.False(list.Entries[0].PendingDeletion);
        Assert.True(list.Entries[1].PendingDeletion);
    }

    [Fact]
    public void The_player_echoes_its_instance_server_control_teleport_and_force_position_sequences()
    {
        var self = new SelfState { Stamps = new PhysicsTimestamps(1, 2, 3, 4, Teleport: 5, ServerControl: 6, ForcePosition: 7, ObjDesc: 8, Instance: 9) };
        Assert.Equal(new ObjSeqs(Instance: 9, ServerControl: 6, Teleport: 5, ForcePosition: 7), self.Sequences);
        self.PhysicsState = PhysicsState.Hidden | PhysicsState.Gravity;
        Assert.True(self.Hidden);
    }

    [Fact]
    public void The_object_table_finds_objects_within_a_distance_across_landblocks()
    {
        var table = new WorldObjectTable();
        table.Set(Placed(1, 0xA9B40001, new Vector3(1, 1, 0)));
        table.Set(Placed(2, 0xA9B40001, new Vector3(11, 1, 0)));
        table.Set(Placed(3, 0xA8B40008, new Vector3(190, 1, 0))); // 3 m west, across the landblock edge
        table.Set(new WorldObject(Create(4), isSelf: false, createdAt: 0)); // no position: inventory

        var near = new List<uint>();
        Assert.Equal(2, table.Within(Frame(0xA9B40001, new Vector3(1, 1, 0)), 5f, near));
        Assert.Equal([1u, 3u], near.Order());
        Assert.True(table.TryGet(4, out var held));
        Assert.False(held.InWorld);
        Assert.True(table.Remove(4));
        Assert.Equal(3, table.Count);
    }

    private static CellFrame Frame(uint cell, Vector3 origin) => new(cell, new AcFrame(origin, Quaternion.Identity));

    private static WorldObject Placed(uint guid, uint cell, Vector3 origin) =>
        new(Create(guid), isSelf: false, createdAt: 0) { Position = Frame(cell, origin) };

    private static CreateObjectMsg Create(uint guid) => new(
        guid,
        ModelData.Empty,
        new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = default },
        new WeenieDesc { Header = default, Name = "Thing " + guid, WeenieClassId = 1, IconId = 0x06000001, ItemType = 0, ObjectDescriptionFlags = 0 });
}

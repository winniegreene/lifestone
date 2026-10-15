using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The player's own state on a session in the fake server's world (m2-plan §3.3 N10; owner decision D16): entering the
/// world delivers the PlayerDescription decoded, before PlayerCreate, as ACE's <c>Player.SendSelf</c> sends it; the
/// PrivateUpdate messages reach the sink; and a description whose gameplay options leave the inventory undecoded is
/// logged as a warning.
/// </summary>
public sealed class SelfStateTests
{
    [Fact]
    public void Entering_the_world_delivers_the_description_before_PlayerCreate()
    {
        using var rig = new SessionRig();
        rig.ToWorld();

        var description = Assert.Single(rig.Sink.Descriptions);
        Assert.Equal("Hero", description.StringProperties[1]);
        Assert.Equal(AcePlayerDescription.NewCharacter("Hero").Skills.Count, description.Skills.Length);
        Assert.Equal(3, description.Vitals.Length);
        Assert.False(description.InventoryUndecoded);

        var opcodes = rig.Sink.Opcodes.ToList();
        Assert.True(opcodes.IndexOf(MessageOpcodes.GameEvent) < opcodes.IndexOf(MessageOpcodes.PlayerCreate), string.Join(",", opcodes.Select(o => o.ToString("X4"))));
        Assert.Empty(rig.Sink.Malformed);
    }

    [Fact]
    public void Private_updates_reach_the_sink_in_order()
    {
        using var rig = new SessionRig();
        rig.ToWorld();

        rig.ServerSession.EnqueueSend(
            (AceWireWriters.PrivateUpdateAttribute2ndLevel(1, 2, 29), FakeAceSession.UiQueue),
            (AceWireWriters.PrivateUpdatePropertyInt64(1, 0x01, 1000), FakeAceSession.UiQueue),
            (AceWireWriters.PrivateUpdateSkill(1, new AceSkill(0x18, 1, 2, 10, 10)), FakeAceSession.UiQueue));
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(
            [PrivateUpdateKind.VitalCurrent, PrivateUpdateKind.PropertyInt64, PrivateUpdateKind.Skill],
            rig.Sink.PrivateUpdates.Select(u => u.Kind).ToArray());
        Assert.Equal(29, rig.Sink.PrivateUpdates[0].Value);
        Assert.Equal(1000, rig.Sink.PrivateUpdates[1].Value);
        Assert.Equal(0x18u, rig.Sink.PrivateUpdates[2].Skill.Id);
    }

    [Fact]
    public void Gameplay_options_are_logged_as_a_warning()
    {
        var script = new FakeAceScript { PlayerDescription = name => AcePlayerDescription.NewCharacter(name) with { GameplayOptions = [1, 2, 3, 4] } };
        script.AddAccount(SessionRig.Account, SessionRig.Password).Characters.Add(new FakeCharacter(SessionRig.HeroGuid, "Hero") { Position = script.StartPosition });
        using var rig = new SessionRig(script);
        rig.ToWorld();

        var description = Assert.Single(rig.Sink.Descriptions);
        Assert.True(description.InventoryUndecoded);
        Assert.Contains(rig.Log.At(NetLogLevel.Warning), line => line.Contains("gameplay options", StringComparison.Ordinal) && line.Contains("TODO(net)", StringComparison.Ordinal));
        Assert.Equal(SessionPhase.InWorld, rig.Session.Phase);
    }
}

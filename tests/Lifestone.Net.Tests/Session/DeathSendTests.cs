using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// <c>SendSuicide</c> and <c>SendAddSpellFavorite</c> on a session in the fake server's world (M3 3.2 s0): each goes out on the weenie queue with the next GameAction sequence, as ACE's
/// <c>GameActionDie</c> (no payload) and <c>GameActionAddSpellFavorite</c> (spell, position, bar) read them, and both refuse a session outside the world.
/// </summary>
public sealed class DeathSendTests
{
    [Fact]
    public void The_suicide_and_the_favourite_reach_the_server_as_the_ACE_layouts_on_the_weenie_queue()
    {
        using var rig = new SessionRig();
        rig.ToWorld();

        var tickets = new[]
        {
            rig.Session.SendSuicide(),
            rig.Session.SendAddSpellFavorite(0x0B4F, 3, 2),
        };
        rig.RunFor(TimeSpan.FromMilliseconds(200));

        var actions = rig.Tap.Messages.Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction).ToList();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(new[] { 12, 24 }, actions.Select(a => a.Body.Length));
        Assert.Equal(new uint[] { 1, 2 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(new uint[] { 0x0279, 0x01E3 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(
            (0x0B4Fu, 3u, 2u),
            (BitConverter.ToUInt32(actions[1].Body, 12), BitConverter.ToUInt32(actions[1].Body, 16), BitConverter.ToUInt32(actions[1].Body, 20)));
        Assert.Equal(tickets.Select(t => t.MessageSequence), actions.Select(a => a.Sequence));
    }

    [Fact]
    public void The_remove_favourite_reaches_the_server_as_the_ACE_layout_on_the_weenie_queue()
    {
        using var rig = new SessionRig();
        rig.ToWorld();

        var ticket = rig.Session.SendRemoveSpellFavorite(0x0B4F, 1);
        rig.RunFor(TimeSpan.FromMilliseconds(200));

        var action = Assert.Single(rig.Tap.Messages, m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction);
        Assert.Equal(MessageQueue.Weenie, action.Queue);
        Assert.Equal(20, action.Body.Length);
        Assert.Equal((0x01E4u, 0x0B4Fu, 1u), (BitConverter.ToUInt32(action.Body, 8), BitConverter.ToUInt32(action.Body, 12), BitConverter.ToUInt32(action.Body, 16)));
        Assert.Equal(ticket.MessageSequence, action.Sequence);
    }

    [Fact]
    public void Both_refuse_a_session_outside_the_world()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var sent = rig.Tap.Outbound.Count();

        Action[] sends =
        [
            () => rig.Session.SendSuicide(),
            () => rig.Session.SendAddSpellFavorite(1, 0, 0),
            () => rig.Session.SendRemoveSpellFavorite(1, 0),
        ];

        Assert.All(sends, s => Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(s).Message, StringComparison.Ordinal));
        Assert.Equal(sent, rig.Tap.Outbound.Count());
    }
}

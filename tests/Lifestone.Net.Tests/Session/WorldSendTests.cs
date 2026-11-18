using System.Numerics;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The world sends of N7 on a session in the fake server's world (m2-plan §3.3 N7): each goes out on the weenie queue
/// with the next GameAction sequence and is read by the fake as ACE reads it; the fake's answers (the position echo,
/// UseDone, UpdateHealth, a Tell, HearSpeech) come back decoded; the sends refuse a session outside the world; and input
/// a codec refuses sends nothing and uses no sequence.
/// </summary>
public sealed class WorldSendTests
{
    private static readonly AcPosition Spot = new(0xA9B40019, new Vector3(84f, 7.1f, 94f), new Quaternion(0f, 0f, -0.0784591f, 0.996917f));

    [Fact]
    public void Every_world_send_reaches_the_server_in_order_on_the_weenie_queue()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        var running = RawMotionStateWire.Default with { CurrentHoldKey = 2, ForwardCommand = 0x45000005, ForwardHoldKey = 2 };

        var tickets = new[]
        {
            rig.Session.SendLoginComplete(),
            rig.Session.SendMoveToState(running, Spot, new ObjSeqs(1, 0, 0, 0), contact: true, standingLongJump: false),
            rig.Session.SendAutonomousPosition(Spot, new ObjSeqs(1, 0, 0, 0), contact: true),
            rig.Session.SendJump(0.5f, new Vector3(0, 0, 4f), Spot, new ObjSeqs(1, 0, 0, 0)),
            rig.Session.SendUse(0x7A9B4002),
            rig.Session.SendQueryHealth(0x7A9B4002),
            rig.Session.SendTalk("hello"),
            rig.Session.SendTell("Hero", "psst"),
        };
        rig.RunFor(TimeSpan.FromMilliseconds(200));

        var actions = rig.Tap.Messages.Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction).ToList();
        Assert.Equal(8, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (uint)i), actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(
            new uint[] { 0x00A1, 0xF61C, 0xF753, 0xF61B, 0x0036, 0x01BF, 0x0015, 0x005D },
            actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(tickets.Select(t => t.MessageSequence), actions.Select(a => a.Sequence));

        var handled = rig.ServerSession.HandledMessages.Where(m => BitConverter.ToUInt32(m) == MessageOpcodes.GameAction).ToList();
        Assert.Equal(8, handled.Count);
        Assert.Equal(0xA9B40019u, AceClientReaders.ReadMoveToState(handled[1]).Position.Cell);
        Assert.Equal(0, AceClientReaders.ReadAutonomousPosition(handled[2]).Unread);
        Assert.Equal(0.5f, AceClientReaders.ReadJump(handled[3]).Extent);
        Assert.Equal(0x7A9B4002u, AceClientReaders.ReadObjectAction(handled[4]).Guid);
        Assert.Equal(("psst", "Hero"), (AceClientReaders.ReadTell(handled[7]).Text, AceClientReaders.ReadTell(handled[7]).Target));
        Assert.Equal(1, rig.ServerSession.JumpsAccepted);
        Assert.Equal(new[] { 0x7A9B4002u }, rig.ServerSession.UsedObjects);
    }

    /// <summary>
    /// M6 14: a direct talk (GameAction 0x0032) goes out on the weenie queue as ACE reads it (the message, then the id), and ACE's two answers come back decoded: for a creature on the landblock (here
    /// the player itself) the F7E0 confirmation line and, the creature being a player, its tell; for any other id WeenieError 0x052B.
    /// </summary>
    [Fact]
    public void A_direct_talk_reaches_the_server_by_id_and_its_answers_come_back()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));
        var before = rig.Sink.Dispatched.Count(d => d.Opcode == MessageOpcodes.ServerMessage);

        var known = rig.Session.SendTalkDirect(SessionRig.HeroGuid, "to myself");
        var unknown = rig.Session.SendTalkDirect(0x7A9B4F14, "tusker");
        Assert.True(rig.RunUntil(() => rig.Sink.DecodedEvents.Count(e => e.Event is TellEvent or WeenieErrorEvent) >= 2, TimeSpan.FromSeconds(1)));

        var sent = rig.Tap.Messages.Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction && BitConverter.ToUInt32(m.Body, 8) == GameActionTypes.TalkDirect).ToList();
        Assert.Equal(2, sent.Count);
        Assert.All(sent, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal([known.MessageSequence, unknown.MessageSequence], sent.Select(a => a.Sequence));
        var handled = rig.ServerSession.HandledMessages.Where(m => BitConverter.ToUInt32(m) == MessageOpcodes.GameAction && BitConverter.ToUInt32(m, 8) == GameActionTypes.TalkDirect).ToList();
        Assert.Equal([("to myself", SessionRig.HeroGuid, 0), ("tusker", 0x7A9B4F14u, 0)], handled.Select(m => AceClientReaders.ReadTalkDirect(m)).Select(r => (r.Text, r.Target, r.Unread)));

        Assert.Equal(before + 1, rig.Sink.Dispatched.Count(d => d.Opcode == MessageOpcodes.ServerMessage));
        var events = rig.Sink.DecodedEvents.Select(e => e.Event).ToList();
        Assert.Contains(new TellEvent("to myself", "Hero", SessionRig.HeroGuid, SessionRig.HeroGuid, 3, 0), events);
        Assert.Contains(new WeenieErrorEvent(0x052B, null), events);
        Assert.Empty(rig.Sink.Malformed);
    }

    [Fact]
    public void The_servers_answers_come_back_decoded()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.Session.SendAutonomousPosition(Spot with { Origin = new Vector3(85f, 8f, 94f) }, default, contact: true);
        rig.Session.SendUse(0x7A9B4002);
        rig.Session.SendQueryHealth(0x80000001);
        rig.Session.SendTell("hero", "to myself");
        rig.Session.SendTell("Nobody", "anyone?");
        rig.Session.SendTalk("hello world");
        Assert.True(rig.RunUntil(() => rig.Sink.Speech.Count == 1 && rig.Sink.DecodedEvents.Count >= 4 && rig.Sink.Positions.Count > 0, TimeSpan.FromSeconds(1)));

        var echo = rig.Sink.Positions[^1];
        Assert.Equal((SessionRig.HeroGuid, 0xA9B40019u, new Vector3(85f, 8f, 94f), true), (echo.Guid, echo.Position.CellId, echo.Position.Origin, echo.Grounded));
        var events = rig.Sink.DecodedEvents.Select(e => e.Event).ToList();
        Assert.Contains(new UseDoneEvent(0), events);
        Assert.Contains(new UpdateHealthEvent(0x80000001, 1f), events);
        Assert.Contains(new TellEvent("to myself", "Hero", SessionRig.HeroGuid, SessionRig.HeroGuid, 3, 0), events);
        Assert.Contains(new WeenieErrorEvent(0x052B, null), events);
        Assert.Equal(new SpeechMsg("hello world", "Hero", SessionRig.HeroGuid, 2, null), rig.Sink.Speech[0]);
        Assert.All(rig.Sink.DecodedEvents, e => Assert.Equal(SessionRig.HeroGuid, e.Header.PlayerGuid));
        Assert.Empty(rig.Sink.Malformed);
    }

    [Fact]
    public void The_world_arrives_typed_on_entering()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromMilliseconds(200));

        var self = Assert.Single(rig.Sink.Creates, c => c.Message.Guid == SessionRig.HeroGuid);
        Assert.False(self.IsUpdate);
        Assert.Equal("Hero", self.Message.Weenie.Name);
        Assert.Equal(PhysicsState.IgnoreCollisions | PhysicsState.Gravity | PhysicsState.Hidden | PhysicsState.EdgeSlide, self.Message.Physics.State);
        Assert.Equal(0x02000001u, self.Message.Physics.SetupId);
        Assert.Equal(1 + rig.Script.WorldObjectsOnEnter, rig.Sink.Creates.Count);
        Assert.Empty(rig.Sink.Malformed);

        // LoginComplete clears Hidden and IgnoreCollisions: the fake's SetState arrives decoded.
        rig.Session.SendLoginComplete();
        Assert.True(rig.RunUntil(() => rig.Sink.States.Count > 0, TimeSpan.FromSeconds(1)));
        Assert.Equal(PhysicsState.Gravity | PhysicsState.EdgeSlide, rig.Sink.States[^1].State);
    }

    [Fact]
    public void A_decoded_teleport_arms_the_next_LoginComplete()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.ServerSession.EnqueueSend((AceWireWriters.PlayerTeleport(1), FakeAceSession.SmartboxQueue));
        Assert.True(rig.RunUntil(() => rig.Sink.Teleports.Count == 1, TimeSpan.FromSeconds(1)));
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(new PlayerTeleportMsg(1), rig.Sink.Teleports[0]);
        Assert.DoesNotContain(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.LoginCompleteRepeated);
    }

    [Fact]
    public void A_self_UpdateObject_does_not_arm_LoginComplete_but_a_self_ObjectCreate_does()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.ServerSession.EnqueueSend((AceWireWriters.UpdateObject(new AceCreateObject { Guid = SessionRig.HeroGuid, Name = "Hero" }), FakeAceSession.SmartboxQueue));
        Assert.True(rig.RunUntil(() => rig.Sink.Creates.Any(c => c.IsUpdate), TimeSpan.FromSeconds(1)));
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(50));
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.LoginCompleteRepeated);

        rig.ServerSession.EnqueueSend((AceWireWriters.ObjectCreate(new AceCreateObject { Guid = SessionRig.HeroGuid, Name = "Hero" }), FakeAceSession.SmartboxQueue));
        Assert.True(rig.RunUntil(() => rig.Sink.Creates.Count(c => c.Message.Guid == SessionRig.HeroGuid && !c.IsUpdate) == 2, TimeSpan.FromSeconds(1)));
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(50));
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.LoginCompleteRepeated);
    }

    [Fact]
    public void The_stat_sends_reach_the_server_as_the_four_ACE_layouts_on_the_weenie_queue()
    {
        using var rig = new SessionRig();
        rig.ToWorld();

        var tickets = new[]
        {
            rig.Session.SendRaiseAttribute(5, 4_286_609_098),
            rig.Session.SendRaiseVital(1, 250),
            rig.Session.SendRaiseSkill(34, 1000),
            rig.Session.SendTrainSkill(31, 4),
        };
        rig.RunFor(TimeSpan.FromMilliseconds(200));

        var actions = rig.Tap.Messages.Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction).ToList();
        Assert.Equal(4, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.All(actions, a => Assert.Equal(20, a.Body.Length));
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(new uint[] { 0x0045, 0x0044, 0x0046, 0x0047 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(
            new[] { (5u, 4_286_609_098u), (1u, 250u), (34u, 1000u), (31u, 4u) },
            actions.Select(a => (BitConverter.ToUInt32(a.Body, 12), BitConverter.ToUInt32(a.Body, 16))));
        Assert.Equal(tickets.Select(t => t.MessageSequence), actions.Select(a => a.Sequence));
    }

    [Fact]
    public void The_magic_combat_and_inventory_sends_reach_the_server_as_the_ACE_layouts_on_the_weenie_queue()
    {
        using var rig = new SessionRig();
        rig.ToWorld();

        var tickets = new[]
        {
            rig.Session.SendCastUntargetedSpell(0x0B4F),
            rig.Session.SendCastTargetedSpell(0x80000123, 0x0B50),
            rig.Session.SendChangeCombatMode(8),
            rig.Session.SendGetAndWieldItem(0x80000200, 0x01000000),
            rig.Session.SendPutItemInContainer(0x80000200, 0x50000001, 3),
            rig.Session.SendStackableMerge(0x80000300, 0x80000200, 12),
        };
        rig.RunFor(TimeSpan.FromMilliseconds(200));

        var actions = rig.Tap.Messages.Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction).ToList();
        Assert.Equal(6, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(new[] { 16, 20, 16, 20, 24, 24 }, actions.Select(a => a.Body.Length));
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5, 6 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(new uint[] { 0x0048, 0x004A, 0x0053, 0x001A, 0x0019, 0x0054 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(0x0B4Fu, BitConverter.ToUInt32(actions[0].Body, 12));
        Assert.Equal((0x80000123u, 0x0B50u), (BitConverter.ToUInt32(actions[1].Body, 12), BitConverter.ToUInt32(actions[1].Body, 16)));
        Assert.Equal(8u, BitConverter.ToUInt32(actions[2].Body, 12));
        Assert.Equal((0x80000200u, 0x01000000u), (BitConverter.ToUInt32(actions[3].Body, 12), BitConverter.ToUInt32(actions[3].Body, 16)));
        Assert.Equal(
            (0x80000200u, 0x50000001u, 3u),
            (BitConverter.ToUInt32(actions[4].Body, 12), BitConverter.ToUInt32(actions[4].Body, 16), BitConverter.ToUInt32(actions[4].Body, 20)));
        Assert.Equal(
            (0x80000300u, 0x80000200u, 12u),
            (BitConverter.ToUInt32(actions[5].Body, 12), BitConverter.ToUInt32(actions[5].Body, 16), BitConverter.ToUInt32(actions[5].Body, 20)));
        Assert.Equal(tickets.Select(t => t.MessageSequence), actions.Select(a => a.Sequence));
    }

    [Fact]
    public void The_magic_combat_and_inventory_sends_refuse_a_session_outside_the_world()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var sent = rig.Tap.Outbound.Count();

        Action[] sends =
        [
            () => rig.Session.SendCastUntargetedSpell(1),
            () => rig.Session.SendCastTargetedSpell(1, 1),
            () => rig.Session.SendChangeCombatMode(2),
            () => rig.Session.SendGetAndWieldItem(1, 1),
            () => rig.Session.SendPutItemInContainer(1, 1, 0),
            () => rig.Session.SendStackableMerge(1, 2, 1),
        ];

        Assert.All(sends, s => Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(s).Message, StringComparison.Ordinal));
        Assert.Equal(sent, rig.Tap.Outbound.Count());
    }

    [Fact]
    public void The_stat_sends_refuse_a_session_outside_the_world()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var sent = rig.Tap.Outbound.Count();

        Action[] sends =
        [
            () => rig.Session.SendRaiseAttribute(5, 1),
            () => rig.Session.SendRaiseVital(1, 1),
            () => rig.Session.SendRaiseSkill(34, 1),
            () => rig.Session.SendTrainSkill(31, 1),
        ];

        Assert.All(sends, s => Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(s).Message, StringComparison.Ordinal));
        Assert.Equal(sent, rig.Tap.Outbound.Count());
    }

    [Fact]
    public void The_world_sends_refuse_a_session_outside_the_world()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var sent = rig.Tap.Outbound.Count();

        Action[] sends =
        [
            () => rig.Session.SendMoveToState(RawMotionStateWire.Default, Spot, default, true, false),
            () => rig.Session.SendAutonomousPosition(Spot, default, true),
            () => rig.Session.SendJump(1f, default, Spot, default),
            () => rig.Session.SendUse(1),
            () => rig.Session.SendQueryHealth(1),
            () => rig.Session.SendTalk("hi"),
            () => rig.Session.SendTell("Hero", "hi"),
            () => rig.Session.SendTalkDirect(SessionRig.HeroGuid, "hi"),
        ];

        Assert.All(sends, s => Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(s).Message, StringComparison.Ordinal));
        Assert.Equal(sent, rig.Tap.Outbound.Count());
    }

    [Fact]
    public void Input_the_wire_cannot_carry_sends_nothing_and_uses_no_sequence()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        var tooMany = RawMotionStateWire.Default with { Actions = [.. Enumerable.Repeat(new RawMotionAction(0x13000087, 1, false, 1f), 32)] };

        Assert.Throws<ArgumentException>(() => rig.Session.SendMoveToState(tooMany, Spot, default, true, false));
        Assert.Throws<ArgumentException>(() => rig.Session.SendTell("Héro", "hi"));
        Assert.Throws<ArgumentException>(() => rig.Session.SendTell("Hero", "naïve"));
        Assert.Throws<ArgumentException>(() => rig.Session.SendTell(string.Empty, "hi"));
        Assert.Throws<ArgumentException>(() => rig.Session.SendTalkDirect(0, "hi"));
        Assert.Throws<ArgumentException>(() => rig.Session.SendTalkDirect(SessionRig.HeroGuid, "na\u00EFve"));
        Assert.Throws<ArgumentException>(() => rig.Session.SendTalk("日本"));
        Assert.DoesNotContain(MessageOpcodes.GameAction, rig.Tap.OutboundOpcodes);

        rig.Session.SendTalk("now it goes");
        var action = Assert.Single(rig.Tap.Outbound, b => BitConverter.ToUInt32(b) == MessageOpcodes.GameAction);
        Assert.Equal(1u, BitConverter.ToUInt32(action, 4));
        Assert.Equal(SessionPhase.InWorld, rig.Session.Phase);
        Assert.Empty(rig.Log.At(NetLogLevel.Error));
    }

    /// <summary>
    /// M5 6 w2: the seven sends of the first retail tranche go out on the weenie queue with the next action sequences, in ACE's layouts (slice w1's writers):
    /// 0x0275 {type, context, accepted}, 0x01A1 {flags 0x60, options1, an empty bar, filters, options2}, 0x0063 and 0x028D with no payload, 0x01DF and 0x0010 a padded
    /// String16L, 0x000F {afk}.
    /// </summary>
    [Fact]
    public void The_retail_tranche_sends_reach_the_server_as_the_ACE_layouts_on_the_weenie_queue()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        var block = new CharacterOptionsBlock(0x50C4A54Au, 0x00948700u, 0x3FFFu, [], [[]], []);

        var tickets = new[]
        {
            rig.Session.SendConfirmationResponse(7, 0x11, accepted: true),
            rig.Session.SendSetCharacterOptions(block),
            rig.Session.SendTeleToLifestone(),
            rig.Session.SendTeleToMarketPlace(),
            rig.Session.SendEmote("waves"),
            rig.Session.SendSetAfkMessage("brb"),
            rig.Session.SendSetAfkMode(true),
        };
        rig.RunFor(TimeSpan.FromMilliseconds(200));

        var actions = rig.Tap.Messages.Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction).ToList();
        Assert.Equal(7, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(Enumerable.Range(1, 7).Select(i => (uint)i), actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(new uint[] { 0x0275, 0x01A1, 0x0063, 0x028D, 0x01DF, 0x0010, 0x000F }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(new[] { 24, 32, 12, 12, 20, 20, 16 }, actions.Select(a => a.Body.Length));
        Assert.Equal((7u, 0x11u, 1u), (BitConverter.ToUInt32(actions[0].Body, 12), BitConverter.ToUInt32(actions[0].Body, 16), BitConverter.ToUInt32(actions[0].Body, 20)));
        Assert.Equal(
            new uint[] { 0x60, 0x50C4A54A, 0, 0x3FFF, 0x00948700 },
            Enumerable.Range(0, 5).Select(i => BitConverter.ToUInt32(actions[1].Body, 12 + (4 * i))));
        Assert.Equal((ushort)5, BitConverter.ToUInt16(actions[4].Body, 12));
        Assert.Equal("waves", System.Text.Encoding.ASCII.GetString(actions[4].Body, 14, 5));
        Assert.Equal("brb", System.Text.Encoding.ASCII.GetString(actions[5].Body, 14, 3));
        Assert.Equal(1u, BitConverter.ToUInt32(actions[6].Body, 12));
        Assert.Equal(tickets.Select(t => t.MessageSequence), actions.Select(a => a.Sequence));
    }

    /// <summary>M5 6 w2: each of the seven refuses a session outside the world and sends nothing.</summary>
    [Fact]
    public void The_retail_tranche_sends_refuse_a_session_outside_the_world()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var sent = rig.Tap.Outbound.Count();
        var block = new CharacterOptionsBlock(0, 0, 0, [], [[]], []);

        Action[] sends =
        [
            () => rig.Session.SendConfirmationResponse(7, 1, accepted: false),
            () => rig.Session.SendSetCharacterOptions(block),
            () => rig.Session.SendTeleToLifestone(),
            () => rig.Session.SendTeleToMarketPlace(),
            () => rig.Session.SendEmote("waves"),
            () => rig.Session.SendSetAfkMode(false),
            () => rig.Session.SendSetAfkMessage("brb"),
        ];

        Assert.All(sends, s => Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(s).Message, StringComparison.Ordinal));
        Assert.Equal(sent, rig.Tap.Outbound.Count());
    }
}

using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// Character select's operations (m2-plan §3.3 N6): create (with ACE's double NameInUse folded), delete by
/// range-checked list index, restore, one operation at a time, and the wait that gives up.
/// </summary>
public sealed class CharacterOpsTests
{
    [Fact]
    public void Create_answers_ok_and_the_character_is_added_to_the_list()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();

        var ticket = rig.Session.CreateCharacter(Request("Newcomer"));
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(2)));

        var response = rig.Sink.CharGenResponses.Single();
        Assert.Equal((CharGenResult.Ok, "Newcomer"), (response.Result, response.Name));
        var list = rig.Session.CharacterList!;
        Assert.Equal(new[] { "Hero", "Newcomer" }, list.Characters.Select(c => c.Name));
        Assert.Equal(response.Guid, list.Characters[1].Guid);
        Assert.Single(rig.Sink.CharacterLists);  // ACE sends no new list: the session added it

        var created = rig.Script.Accounts[SessionRig.Account].Characters.Single(c => c.Name == "Newcomer");
        Assert.Equal(CharacterCreateRequest.RequiredSkillCount, created.Skills.Length);
        var sent = rig.Tap.Messages.Single(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.CharacterCreate);
        Assert.Equal((ticket.MessageSequence, MessageQueue.Login), (sent.Sequence, sent.Queue));
        Assert.Equal(SessionRig.Account, AceClientReaders.ReadCharacterCreate(sent.Body).Account);
    }

    [Fact]
    public void A_taken_name_answers_one_NameInUse_and_aces_duplicate_is_folded()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();

        rig.Session.CreateCharacter(Request("hero"));
        Assert.True(rig.RunUntil(() => rig.Tap.Messages.Count(IsCharGenResponse) == 2, TimeSpan.FromSeconds(2)));
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(CharGenResult.NameInUse, rig.Sink.CharGenResponses.Single().Result);
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.DuplicateAnswerFolded);
        Assert.Equal(new[] { "Hero" }, rig.Session.CharacterList!.Characters.Select(c => c.Name));

        // The operation is over: another may start.
        rig.Session.CreateCharacter(Request("Second Try"));
    }

    /// <summary>
    /// A create sent from inside the first NameInUse's callback (automation trying the next name at once) is still
    /// waiting when ACE's duplicate arrives, often in the same packet. The duplicate is folded, not taken as the new
    /// create's answer, so the new create gets its own Ok and the character is listed.
    /// </summary>
    [Fact]
    public void A_create_sent_from_the_NameInUse_callback_gets_its_own_answer_not_aces_duplicate()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var retried = false;
        rig.Sink.OnAnyMessage = opcode =>
        {
            if (opcode == MessageOpcodes.CharacterCreateResponse && !retried)
            {
                retried = true;
                rig.Session.CreateCharacter(Request("Second Choice"));
            }
        };

        rig.Session.CreateCharacter(Request("hero"));
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 2, TimeSpan.FromSeconds(2)));
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.True(retried);
        Assert.Equal(3, rig.Tap.Messages.Count(IsCharGenResponse));
        Assert.Equal(new[] { CharGenResult.NameInUse, CharGenResult.Ok }, rig.Sink.CharGenResponses.Select(r => r.Result));
        Assert.Equal("Second Choice", rig.Sink.CharGenResponses[1].Name);
        Assert.Equal(new[] { "Hero", "Second Choice" }, rig.Session.CharacterList!.Characters.Select(c => c.Name));
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.DuplicateAnswerFolded);
        Assert.DoesNotContain(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.UnexpectedMessage);

        // Nothing waits any more.
        rig.Session.DeleteCharacter(1);
    }

    [Fact]
    public void An_Ok_that_comes_after_the_wait_gave_up_still_lists_the_character()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Harness.Network.Drop = (from, to, datagram) => LoginFailureTests.ClientOpcode(datagram) == MessageOpcodes.CharacterCreate;
        rig.Session.CreateCharacter(Request("Latecomer"));
        rig.RunFor(TimeSpan.FromSeconds(15.1));
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.CharacterOperationTimedOut);

        // As if the create had reached ACE after all: its Ok arrives with nothing waiting.
        rig.ServerSession.EnqueueSend((Lifestone.Tests.Shared.Net.AceWireWriters.CharacterCreateResponse(1, 0x50000042, "Latecomer"), FakeAceSession.UiQueue));
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(1)));

        Assert.Equal(new[] { "Hero", "Latecomer" }, rig.Session.CharacterList!.Characters.Select(c => c.Name));
        Assert.Equal(0x50000042u, rig.Session.CharacterList.Characters[1].Guid);
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.UnexpectedMessage);
    }

    [Fact]
    public void A_single_NameInUse_is_answered_without_a_warning()
    {
        var script = new FakeAceScript { DuplicateNameInUse = false };
        script.AddAccount(SessionRig.Account, SessionRig.Password).Characters.Add(new FakeCharacter(SessionRig.HeroGuid, "Hero"));
        using var rig = new SessionRig(script);
        rig.ToCharacterSelect();

        rig.Session.CreateCharacter(Request("HERO"));
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(2)));
        rig.RunFor(TimeSpan.FromSeconds(3));

        Assert.Equal(CharGenResult.NameInUse, rig.Sink.CharGenResponses.Single().Result);
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.Warning));
    }

    [Fact]
    public void An_unrequested_F643_reaches_the_sink_with_a_warning()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.ServerSession.EnqueueSend((Lifestone.Tests.Shared.Net.AceWireWriters.CharacterCreateResponse(3), FakeAceSession.UiQueue));
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(1)));
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.UnexpectedMessage);
    }

    [Fact]
    public void One_operation_waits_at_a_time()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Session.CreateCharacter(Request("First"));

        Assert.Throws<InvalidOperationException>(() => rig.Session.CreateCharacter(Request("Second")));
        Assert.Throws<InvalidOperationException>(() => rig.Session.DeleteCharacter(0));
        Assert.Throws<InvalidOperationException>(() => rig.Session.RestoreCharacter(SessionRig.HeroGuid));
        Assert.Throws<InvalidOperationException>(() => rig.Session.EnterWorld(SessionRig.HeroGuid));
    }

    [Fact]
    public void Delete_index_0_is_acknowledged_and_the_new_list_greys_the_character()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();

        rig.Session.DeleteCharacter(0);
        Assert.True(rig.RunUntil(() => rig.Sink.CharacterLists.Count == 2, TimeSpan.FromSeconds(2)));

        Assert.Equal(new[] { MessageOpcodes.CharacterDelete, MessageOpcodes.CharacterList }, rig.Sink.Opcodes.TakeLast(2));
        Assert.Equal(1u, rig.Session.CharacterList!.Characters.Single().DeleteTimeout);
        var (account, slot, _) = AceClientReaders.ReadCharacterDelete(rig.ServerSession.HandledMessages.Last());
        Assert.Equal((SessionRig.Account, 0u), (account, slot));

        // The delete completed with the list: the restore may go.
        rig.Session.RestoreCharacter(SessionRig.HeroGuid);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Delete_outside_the_list_is_refused_and_nothing_is_sent(int index)
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var sent = rig.Session.Link.PacketsSent;
        var tapped = rig.Tap.Messages.Count;

        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => rig.Session.DeleteCharacter(index));
        Assert.Contains("1 character(s)", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(sent, rig.Session.Link.PacketsSent);
        Assert.Equal(tapped, rig.Tap.Messages.Count);

        rig.RunFor(TimeSpan.FromMilliseconds(100));
        Assert.Null(rig.ServerSession.TerminationReason);
        Assert.Equal(0, rig.Server.Counters.HandlerExceptions);
    }

    [Fact]
    public void Restore_brings_a_deleted_character_back_with_retails_empty_strings()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Session.DeleteCharacter(0);
        Assert.True(rig.RunUntil(() => rig.Sink.CharacterLists.Count == 2, TimeSpan.FromSeconds(2)));

        var ticket = rig.Session.RestoreCharacter(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(2)));

        Assert.Equal(new CharGenResponseMsg(CharGenResult.Ok, SessionRig.HeroGuid, "Hero", 0), rig.Sink.CharGenResponses[0]);
        Assert.Equal(0u, rig.Session.CharacterList!.Characters.Single().DeleteTimeout);
        var sent = rig.Tap.Messages.Single(m => m.Sequence == ticket.MessageSequence && m.Direction == MessageDirection.Outbound);
        Assert.Equal(MessageQueue.Control, sent.Queue);
        var expected = new Lifestone.Net.Packets.PacketWriter();
        CharacterRestoreCodec.Write(expected, SessionRig.HeroGuid, "", "");  // CPlayerSystem::RestoreCharacter's null strings
        Assert.Equal(expected.ToArray(), sent.Body);
    }

    [Fact]
    public void Restore_of_a_name_taken_meanwhile_answers_NameInUse()
    {
        var script = SessionRig.DefaultScript();
        script.AddAccount("other", SessionRig.Password).Characters.Add(new FakeCharacter(0x50000077, "Hero"));
        using var rig = new SessionRig(script);
        rig.ToCharacterSelect();
        rig.Session.DeleteCharacter(0);
        Assert.True(rig.RunUntil(() => rig.Sink.CharacterLists.Count == 2, TimeSpan.FromSeconds(2)));

        rig.Session.RestoreCharacter(SessionRig.HeroGuid);
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(2)));
        Assert.Equal(CharGenResult.NameInUse, rig.Sink.CharGenResponses[0].Result);
        Assert.Equal(1u, rig.Session.CharacterList!.Characters.Single().DeleteTimeout);
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.Warning));
    }

    [Fact]
    public void Restore_and_enter_world_need_a_listed_character()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        Assert.Throws<ArgumentException>(() => rig.Session.RestoreCharacter(0x50000099));
        Assert.Throws<ArgumentException>(() => rig.Session.EnterWorld(0x50000099));
    }

    [Fact]
    public void Character_operations_need_character_select()
    {
        using var rig = new SessionRig();
        Assert.Throws<InvalidOperationException>(() => rig.Session.CreateCharacter(Request("Early")));
        Assert.Throws<InvalidOperationException>(() => rig.Session.DeleteCharacter(0));
        Assert.Throws<InvalidOperationException>(() => rig.Session.RestoreCharacter(SessionRig.HeroGuid));
        Assert.Throws<InvalidOperationException>(() => rig.Session.EnterWorld(SessionRig.HeroGuid));

        rig.ToWorld();
        Assert.Throws<InvalidOperationException>(() => rig.Session.CreateCharacter(Request("Late")));
    }

    [Fact]
    public void A_lost_create_stops_waiting_after_fifteen_seconds_and_the_session_stays()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.Harness.Network.Drop = (from, to, datagram) => LoginFailureTests.ClientOpcode(datagram) == MessageOpcodes.CharacterCreate;

        rig.Session.CreateCharacter(Request("Lost"));
        rig.RunFor(TimeSpan.FromSeconds(14.9));
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.Warning));
        rig.RunFor(TimeSpan.FromSeconds(0.2));

        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.CharacterOperationTimedOut);
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
    }

    [Fact]
    public void A_character_error_in_character_select_answers_the_operation()
    {
        var script = SessionRig.DefaultScript();
        using var rig = new SessionRig(script);
        rig.ToCharacterSelect();
        script.WorldOpen = false;  // ACE refuses a create in a closed world with 0x15

        rig.Session.CreateCharacter(Request("Closed"));
        Assert.True(rig.RunUntil(() => rig.Sink.CharacterErrors.Count == 1, TimeSpan.FromSeconds(2)));
        var error = Assert.Single(rig.Sink.OfKind(SessionEventKind.CharacterErrorReceived));
        Assert.Equal((0x15u, SessionPhase.CharacterSelect), (error.Code, error.Phase));
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
        rig.Session.DeleteCharacter(0);  // nothing waits any more
    }

    /// <summary>A valid create request for <paramref name="name"/>.</summary>
    internal static CharacterCreateRequest Request(string name) => new(
        Heritage: 1, Gender: 1, Appearance: default, TemplateOption: 0, Strength: 10, Endurance: 10, Coordination: 10,
        Quickness: 10, Focus: 10, Self: 10, Slot: 0, ClassId: 1, SkillAdvancementClasses: [.. new uint[CharacterCreateRequest.RequiredSkillCount]],
        Name: name, StartArea: 0);

    private static bool IsCharGenResponse((MessageDirection Direction, long Timestamp, uint Sequence, MessageQueue Queue, byte[] Body) message) =>
        message.Direction == MessageDirection.Inbound && BitConverter.ToUInt32(message.Body) == MessageOpcodes.CharacterCreateResponse;
}

using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Support;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// The fake server's scripted game, which the session tests (N5, N6) stand on: the login failures ACE produces, the
/// character operations, entering the world, LoginComplete, the movement echo, logoff, the tool replies and the
/// character-select ping. Driven by a <see cref="RawAceClient"/> with messages from the product's codecs.
/// </summary>
public sealed class FakeAceScriptTests
{
    private const string Account = "tester";
    private const string Password = "test-password-1";

    [Fact]
    public void An_unknown_account_is_auto_created_lower_cased_and_the_list_carries_that_name()
    {
        var harness = new SessionHarness(new FakeAceScript());
        using var client = Login(harness, "NewPlayer", Password);

        Assert.True(CharacterListCodec.TryParse(Message(client, MessageOpcodes.CharacterList), out var list));
        Assert.Equal("newplayer", list.Account);
        Assert.Empty(list.Characters);
        Assert.True(harness.Script.Accounts.ContainsKey("newplayer"));
    }

    [Fact]
    public void Without_auto_creation_an_unknown_account_gets_the_connect_request_then_F659_0A()
    {
        var harness = new SessionHarness(new FakeAceScript { AllowAutoAccountCreation = false });
        using var client = new RawAceClient(harness);
        client.SendLoginRequest("nobody", Password);
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.Drain();

        Assert.NotNull(client.ConnectRequestPacket);
        Assert.Equal(0x0Au, CharacterError(client));
        Assert.Equal(2u, client.Received.Last().Sequence);
        Assert.Equal(FakeAceTermination.NotAuthorizedAccountNotFound, harness.Server.History.Single().TerminationReason);
    }

    [Fact]
    public void A_second_login_boots_the_first_and_both_get_F659_01()
    {
        var harness = Harness();
        using var first = Login(harness, Account, Password);
        using var second = new RawAceClient(harness);
        second.SendLoginRequest(Account, Password);
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        first.Drain();
        second.Drain();

        Assert.Equal(0x01u, CharacterError(first));
        Assert.Equal(0x01u, CharacterError(second));
        Assert.Equal(1, harness.Server.Counters.Terminations[FakeAceTermination.AccountLoggedIn]);
        Assert.Equal(1, harness.Server.Counters.Terminations[FakeAceTermination.AccountInUse]);
    }

    [Fact]
    public void A_closed_world_refuses_the_connect_response_with_F659_15()
    {
        var script = new FakeAceScript { WorldOpen = false };
        script.AddAccount(Account, Password);
        var harness = new SessionHarness(script);
        using var client = new RawAceClient(harness);
        client.SendLoginRequest(Account, Password);
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.Drain();
        client.SendConnectResponse();
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.Drain();

        Assert.Equal(0x15u, CharacterError(client));
        Assert.Equal(FakeAceTermination.WorldClosed, harness.Server.History.Single().TerminationReason);
    }

    [Fact]
    public void A_wrong_client_version_is_booted_without_a_connect_request()
    {
        var harness = Harness();
        using var client = new RawAceClient(harness);
        client.SendLoginRequest(Account, Password, clientVersion: "1801");
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.Drain();

        Assert.Null(client.ConnectRequestPacket);
        var boot = Assert.Single(client.Received);
        Assert.True(boot.Has(RawAceClient.EncryptedChecksum));
        Assert.Equal(0u, boot.Sequence);  // ACE's sequence wraps from its unprimed maximum to 0
        Assert.Equal(FakeAceTermination.ClientVersionIncorrect, harness.Server.History.Single().TerminationReason);
    }

    [Fact]
    public void A_banned_account_gets_F7C1_with_the_seconds_left()
    {
        var script = new FakeAceScript();
        var account = script.AddAccount(Account, Password);
        var harness = new SessionHarness(script);
        account.BanExpires = harness.Time.GetUtcNow().AddHours(1);
        account.BanReason = "testing";
        using var client = new RawAceClient(harness);
        client.SendLoginRequest(Account, Password);
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.Drain();

        Assert.True(AccountBannedCodec.TryParse(Message(client, MessageOpcodes.AccountBanned), out var banned));
        Assert.InRange(banned.SecondsRemaining, 3590u, 3600u);
        Assert.Equal(" - testing", banned.Reason);
    }

    [Fact]
    public void The_server_full_reject_comes_from_a_throwaway_session()
    {
        var script = new FakeAceScript { MaximumSessions = 1 };
        script.AddAccount(Account, Password);
        script.AddAccount("other", Password);
        var harness = new SessionHarness(script);
        using var first = Login(harness, Account, Password);
        using var second = new RawAceClient(harness);
        second.SendLoginRequest("other", Password);
        harness.Step();
        second.Drain();

        Assert.Equal(0x15u, CharacterError(second));
        Assert.Single(harness.Server.Sessions);
    }

    [Fact]
    public void Create_answers_ok_and_a_taken_name_answers_NameInUse_twice_as_ace_does()
    {
        var harness = Harness();
        using var client = Login(harness, Account, Password);
        var fragmentsBefore = client.MessagesBySequence.Count;

        Send(harness, client, 2, Create("Newcomer", 55));
        Assert.True(CharGenResponseCodec.TryParse(client.Messages.Skip(fragmentsBefore).Single(), out var created));
        Assert.Equal(CharGenResult.Ok, created.Result);
        Assert.Equal("Newcomer", created.Name);
        var character = harness.Script.Accounts[Account].Characters.Single();
        Assert.Equal(created.Guid, character.Guid);
        Assert.Equal(55, character.Skills.Length);

        var before = client.MessagesBySequence.Count;
        Send(harness, client, 3, Create("newcomer", 55));
        var answers = client.Messages.Skip(before).ToList();
        Assert.Equal(2, answers.Count);
        Assert.All(answers, a => Assert.Equal(AceWireWriters.CharacterCreateResponse(3), a));
    }

    [Fact]
    public void Create_with_54_skills_boots_the_session()
    {
        var harness = Harness();
        using var client = Login(harness, Account, Password);

        Send(harness, client, 2, Create("Shortskills", 54));

        Assert.Equal(FakeAceTermination.ClientVersionIncorrect, harness.Server.History.Single().TerminationReason);
        Assert.Contains(MessageOpcodes.AccountBoot, client.Opcodes);
    }

    [Fact]
    public void Delete_acknowledges_then_lists_the_character_greyed_and_restore_brings_it_back()
    {
        var harness = Harness(withCharacter: true);
        using var client = Login(harness, Account, Password);

        var before = client.MessagesBySequence.Count;
        Send(harness, client, 2, Build(w => CharacterDeleteCodec.Write(w, Account, 0)));
        var answers = client.Messages.Skip(before).ToList();
        Assert.Equal(AceWireWriters.CharacterDeleteAck(), answers[0]);
        Assert.True(CharacterListCodec.TryParse(answers[1], out var list));
        Assert.Equal(1u, list.Characters.Single().DeleteTimeout);

        before = client.MessagesBySequence.Count;
        Send(harness, client, 3, Build(w => CharacterRestoreCodec.Write(w, 0x50000001, "Hero", Account)));
        Assert.True(CharGenResponseCodec.TryParse(client.Messages.Skip(before).Single(), out var restored));
        Assert.Equal(new CharGenResponseMsg(CharGenResult.Ok, 0x50000001, "Hero", 0), restored);
        Assert.Null(harness.Script.Accounts[Account].Characters.Single().DeleteTime);
    }

    [Fact]
    public void Entering_the_world_logging_in_moving_and_logging_off_follow_ace()
    {
        var harness = Harness(withCharacter: true);
        using var client = Login(harness, Account, Password);
        var session = harness.Server.Sessions.Single();

        Send(harness, client, 2, Build(CharacterEnterWorldCodec.WriteRequest));
        Assert.Equal(MessageOpcodes.CharacterEnterWorldServerReady, client.Opcodes[^1]);

        Send(harness, client, 3, Build(w => CharacterEnterWorldCodec.Write(w, 0x50000001, Account)));
        var opcodes = client.Opcodes;
        Assert.Equal(MessageOpcodes.PlayerCreate, opcodes[^(2 + harness.Script.WorldObjectsOnEnter)]);
        Assert.Equal(1 + harness.Script.WorldObjectsOnEnter, opcodes.Count(o => o == MessageOpcodes.ObjectCreate));
        Assert.Equal(FakeAceSessionState.WorldConnected, session.State);
        Assert.True(session.Teleporting);
        var self = client.Messages.First(m => BinaryPrimitives.ReadUInt32LittleEndian(m) == MessageOpcodes.ObjectCreate);
        Assert.Equal(FakeAceSession.EnteringPhysicsState, BinaryPrimitives.ReadUInt32LittleEndian(self.AsSpan(4 + 4 + 4 + 4)));

        // Before LoginComplete a position is ignored.
        Send(harness, client, 4, AutonomousPosition(new AcePosition(0x860201AD, 13, -28, 0.005f)));
        Assert.Empty(session.AcceptedPositions);

        Send(harness, client, 5, Build(w => LoginCompleteCodec.Write(w, 1)));
        Assert.False(session.Teleporting);
        var setState = client.Messages.Last();
        Assert.Equal(AceWireWriters.SetState(0x50000001, 0x400 | 0x400000, 1, 1), setState);

        Send(harness, client, 6, AutonomousPosition(new AcePosition(0x860201AD, 14, -28, 0.005f)));
        Assert.Equal(14f, session.AcceptedPositions.Single().X);
        Assert.Equal(MessageOpcodes.UpdatePosition, client.Opcodes[^1]);

        Send(harness, client, 7, Build(w => CharacterLogOffCodec.Write(w, 0x50000001)));
        var before = client.MessagesBySequence.Count;
        harness.RunFor(TimeSpan.FromSeconds(5.9));
        client.Drain();
        Assert.Equal(before, client.MessagesBySequence.Count);
        harness.RunFor(TimeSpan.FromSeconds(0.3));
        client.Drain();
        Assert.Equal(
            new[] { MessageOpcodes.CharacterLogOff, MessageOpcodes.CharacterList, MessageOpcodes.ServerName },
            client.Opcodes.Skip(before));
        Assert.Equal(FakeAceSessionState.AuthConnected, session.State);
    }

    [Fact]
    public void Enter_world_with_another_account_string_is_refused_with_F659_0F()
    {
        var harness = Harness(withCharacter: true);
        using var client = Login(harness, Account, Password);
        Send(harness, client, 2, Build(w => CharacterEnterWorldCodec.Write(w, 0x50000001, "Tester")));
        Assert.Equal(0x0Fu, CharacterError(client));
    }

    [Fact]
    public void A_lifestone_talk_gets_the_scripted_tool_replies()
    {
        var script = new FakeAceScript { ToolReplies = text => [$"#lifestone/1 echo {text.Length}", "#lifestone/1 done"] };
        script.AddAccount(Account, Password).Characters.Add(new FakeCharacter(0x50000001, "Hero") { Position = script.StartPosition });
        var harness = new SessionHarness(script);
        using var client = Login(harness, Account, Password);
        Send(harness, client, 2, Build(w => CharacterEnterWorldCodec.Write(w, 0x50000001, Account)));
        Send(harness, client, 3, Build(w => LoginCompleteCodec.Write(w, 1)));

        var talk = Build(w =>
        {
            GameActionEnvelope.WriteHeader(w, 2, GameActionTypes.Talk);
            w.WriteString16L("@lifestone status");
        });
        var before = client.MessagesBySequence.Count;
        Send(harness, client, 4, talk);

        var replies = client.Messages.Skip(before).Select(m =>
        {
            Assert.True(ServerMessageCodec.TryParse(m, out var reply));
            return reply.Text;
        });
        Assert.Equal(new[] { "#lifestone/1 echo 17", "#lifestone/1 done" }, replies);
    }

    [Fact]
    public void Character_select_gets_a_ping_response_every_100_seconds_if_the_client_stays()
    {
        var harness = Harness();
        using var client = Login(harness, Account, Password);
        var keepAlive = harness.Time.Elapsed;
        harness.AddParticipant(() =>
        {
            client.Drain();
            if (harness.Time.Elapsed >= keepAlive)
            {
                client.Send(client.BuildAckOnly(1, 2));
                keepAlive += TimeSpan.FromSeconds(10);
            }
        });

        harness.RunFor(TimeSpan.FromSeconds(205));

        var pings = client.Messages
            .Where(m => GameEventEnvelope.TryReadHeader(m, out var h) && h.EventType == 0x01EA)
            .ToList();
        Assert.Equal(2, pings.Count);
    }

    [Fact]
    public void Shutdown_sends_F659_04()
    {
        var harness = Harness();
        using var client = Login(harness, Account, Password);
        harness.Server.Shutdown();
        harness.RunFor(TimeSpan.FromMilliseconds(50));
        client.Drain();
        Assert.Equal(0x04u, CharacterError(client));
    }

    private static SessionHarness Harness(bool withCharacter = false)
    {
        var script = new FakeAceScript();
        var account = script.AddAccount(Account, Password);
        if (withCharacter)
        {
            account.Characters.Add(new FakeCharacter(0x50000001, "Hero") { Position = script.StartPosition });
        }

        return new SessionHarness(script);
    }

    /// <summary>A client through the handshake, with the character list, server name and interrogation received.</summary>
    private static RawAceClient Login(SessionHarness harness, string account, string password)
    {
        var client = new RawAceClient(harness);
        client.SendLoginRequest(account, password);
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.Drain();
        client.SendConnectResponse();
        Assert.True(harness.RunUntil(() => { client.Drain(); return client.MessagesBySequence.Count >= 3; }, TimeSpan.FromSeconds(1)));
        return client;
    }

    private static void Send(SessionHarness harness, RawAceClient client, uint sequence, byte[] message)
    {
        client.Send(client.BuildData(sequence, message));
        harness.RunFor(TimeSpan.FromMilliseconds(30));
        client.Drain();
    }

    private static byte[] Message(RawAceClient client, uint opcode) =>
        client.Messages.Single(m => BinaryPrimitives.ReadUInt32LittleEndian(m) == opcode);

    private static uint CharacterError(RawAceClient client)
    {
        Assert.True(CharacterErrorCodec.TryParse(Message(client, MessageOpcodes.CharacterError), out var error));
        return error.Code;
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }

    /// <summary>A CharacterCreate from the product's codec, or, for any other skill count, the same layout by hand.</summary>
    private static byte[] Create(string name, int skills)
    {
        if (skills == CharacterCreateRequest.RequiredSkillCount)
        {
            return Build(w => CharacterCreateCodec.Write(w, Account, new CharacterCreateRequest(
                1, 0, default, 0, 10, 10, 10, 10, 10, 10, 0, 1, [.. new uint[skills]], name, 0)));
        }

        return Build(w =>
        {
            w.WriteUInt32(MessageOpcodes.CharacterCreate);
            w.WriteString16L(Account);
            w.WriteUInt32(1);
            w.Pad((2 + 14) * 4 + 6 * 8 + 7 * 4 + 2 * 4);  // heritage .. class id, all zero
            w.WriteUInt32((uint)skills);
            w.Pad(skills * 4);
            w.WriteString16L(name);
            w.Pad(4 * 4);  // start area, is-admin, is-envoy, checksum
        });
    }

    private static byte[] AutonomousPosition(AcePosition p) => Build(w =>
    {
        GameActionEnvelope.WriteHeader(w, 9, GameActionTypes.AutonomousPosition);
        w.WriteUInt32(p.Cell);
        w.WriteFloat(p.X);
        w.WriteFloat(p.Y);
        w.WriteFloat(p.Z);
        w.WriteFloat(p.Qw);
        w.WriteFloat(p.Qx);
        w.WriteFloat(p.Qy);
        w.WriteFloat(p.Qz);
        w.WriteUInt32(0);  // instance and server-control sequences
        w.WriteUInt32(0);  // teleport and force-position sequences
        w.WriteByte(1);    // contact
        w.AlignTo4();
    });
}

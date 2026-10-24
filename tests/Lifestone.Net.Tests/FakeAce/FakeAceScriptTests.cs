using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Support;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// The fake server's scripted game that no session test reaches (the session tests, N5 and N6, drive the product against
/// the same fake and cover the login failures, character operations, entering the world, movement and logoff): the closed
/// world, a 54-skill create, another account's enter-world, the tool replies and the character-select ping. Driven by a
/// <see cref="RawAceClient"/> with messages from the product's codecs.
/// </summary>
public sealed class FakeAceScriptTests
{
    private const string Account = "tester";
    private const string Password = "test-password-1";

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
    public void Create_with_54_skills_boots_the_session()
    {
        var harness = Harness();
        using var client = Login(harness, Account, Password);

        Send(harness, client, 2, Create("Shortskills", 54));

        Assert.Equal(FakeAceTermination.ClientVersionIncorrect, harness.Server.History.Single().TerminationReason);
        Assert.Contains(MessageOpcodes.AccountBoot, client.Opcodes);
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
}

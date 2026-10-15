using System.Security.Cryptography;
using System.Text;
using Lifestone.Client.Session.Credentials;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// The golden-capture store of the live net tests (m2-plan N11, D14): the password guard refuses a body that holds the password in any form, a capture
/// round-trips, and, when the live tests have captured fixtures (<c>cache/net-fixtures/&lt;CacheKey&gt;/</c>, never committed), every captured message
/// decodes again with today's codecs and none holds the real password. The first tests need no backend; the reparse tests skip without fixtures.
/// </summary>
public sealed class NetFixtureTests
{
    private const string SentinelPassword = "Zq7-sentinel-password-9Xk";

    [Fact]
    public void Save_refuses_a_body_holding_the_password_in_any_form_and_writes_nothing()
    {
        using var credentials = new AcCredentials("sentinel-account", SentinelPassword);
        var needles = NetFixtureStore.PasswordNeedles(credentials);
        Assert.Equal(3, needles.Count);

        var ascii = Encoding.ASCII.GetBytes(SentinelPassword);
        var forms = new[]
        {
            ascii,
            Encoding.Unicode.GetBytes(SentinelPassword),
            [.. BitConverter.GetBytes(ascii.Length + 1), (byte)ascii.Length, .. ascii],
        };
        foreach (var form in forms)
        {
            var path = TempFile();
            byte[] body = [0x01, 0x02, .. form, 0x03];
            var e = Assert.Throws<PasswordInFixtureException>(() => NetFixtureStore.Save(path, [Message(body)], needles));
            Assert.False(File.Exists(path), "a refused capture must not leave a file");
            Assert.DoesNotContain(SentinelPassword, e.Message, StringComparison.Ordinal);
            Assert.Contains("1 message body holds", e.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_clean_capture_round_trips_byte_for_byte()
    {
        using var credentials = new AcCredentials("sentinel-account", SentinelPassword);
        var needles = NetFixtureStore.PasswordNeedles(credentials);
        CapturedMessage[] messages =
        [
            new(MessageDirection.Inbound, 10, 1, MessageQueue.Event, [0x58, 0xF6, 0x00, 0x00, 1, 2, 3]),
            new(MessageDirection.Outbound, 20, 7, MessageQueue.Weenie, []),
            new(MessageDirection.Inbound, 30, 2, MessageQueue.Control, Enumerable.Range(0, 300).Select(i => (byte)i).ToArray()),
        ];
        Assert.Equal(0, NetFixtureStore.CountPasswordHits(messages, needles));

        var path = TempFile();
        try
        {
            NetFixtureStore.Save(path, messages, needles);
            var loaded = NetFixtureStore.Load(path);
            Assert.Equal(messages.Length, loaded.Count);
            for (var i = 0; i < messages.Length; i++)
            {
                Assert.Equal(messages[i].Direction, loaded[i].Direction);
                Assert.Equal(messages[i].Queue, loaded[i].Queue);
                Assert.Equal(messages[i].FragmentSequence, loaded[i].FragmentSequence);
                Assert.Equal(messages[i].Timestamp, loaded[i].Timestamp);
                Assert.Equal(messages[i].Body, loaded[i].Body);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_rejects_a_file_that_is_not_a_fixture_or_is_cut_short()
    {
        var path = TempFile();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            Assert.Throws<InvalidDataException>(() => NetFixtureStore.Load(path));

            using var credentials = new AcCredentials("sentinel-account", SentinelPassword);
            NetFixtureStore.Save(path, [Message([1, 2, 3, 4, 5])], NetFixtureStore.PasswordNeedles(credentials));
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..^2]);
            Assert.Throws<InvalidDataException>(() => NetFixtureStore.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_cache_key_names_the_pinned_ace_commit_and_the_client_version()
    {
        var key = NetFixtureStore.CacheKey();
        Assert.NotNull(key);
        Assert.Matches("^ace-[0-9a-f]{12}-client1802$", key);
        Assert.EndsWith(Path.Combine("cache", "net-fixtures", key!), NetFixtureStore.Directory(), StringComparison.Ordinal);
    }

    [RequiresNetFixturesFact]
    public void Every_captured_message_decodes_again_with_todays_codecs()
    {
        var inbound = 0;
        var decoded = new HashSet<uint>();
        var problems = new List<string>();
        foreach (var file in NetFixtureStore.Files())
        {
            foreach (var m in NetFixtureStore.Load(file))
            {
                if (m.Direction != MessageDirection.Inbound)
                {
                    continue;
                }

                inbound++;
                var result = InboundDispatcher.Dispatch(new InboundContext(m.FragmentSequence, m.Queue, m.Timestamp), m.Body, new RecordingHandler(problems));
                if (result.Outcome == DispatchOutcome.Malformed)
                {
                    problems.Add($"{Path.GetFileName(file)}: opcode 0x{result.Opcode:X8} is malformed");
                }
                else if (result.Outcome == DispatchOutcome.Decoded)
                {
                    decoded.Add(result.Opcode);
                }
            }
        }

        Assert.True(inbound > 0, "the fixtures hold no inbound message");
        Assert.True(problems.Count == 0, "captured messages that no longer decode:\n  " + string.Join("\n  ", problems.Distinct()));

        // The login's messages are among them: the character list, the player's id and its own object.
        Assert.Contains(MessageOpcodes.CharacterList, decoded);
        Assert.Contains(MessageOpcodes.PlayerCreate, decoded);
        Assert.Contains(MessageOpcodes.ObjectCreate, decoded);
    }

    [RequiresNetFixturesFact]
    public void No_captured_message_holds_the_real_password()
    {
        var secretsFile = BackendProbe.MainCheckoutFiles.SecretsFile;
        if (!File.Exists(secretsFile))
        {
            return; // no password on this machine: nothing to search for (the capture-time guard needed it)
        }

        var secrets = File.ReadAllBytes(secretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(BackendProbe.TestAccountKey, AccountChoice.Default));
            var needles = NetFixtureStore.PasswordNeedles(credentials);
            try
            {
                var hits = NetFixtureStore.Files().Sum(f => NetFixtureStore.CountPasswordHits(NetFixtureStore.Load(f), needles));
                Assert.Equal(0, hits);
            }
            finally
            {
                foreach (var needle in needles)
                {
                    CryptographicOperations.ZeroMemory(needle);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }
    }

    private static CapturedMessage Message(byte[] body) => new(MessageDirection.Inbound, 1, 1, MessageQueue.Event, body);

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "lifestone-netfx-" + Guid.NewGuid().ToString("N") + NetFixtureStore.Extension);

    /// <summary>A handler that takes everything (every member of the interface has a no-op default) and notes the messages the dispatcher called malformed.</summary>
    private sealed class RecordingHandler(List<string> problems) : IInboundMessageHandler
    {
        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => problems.Add($"0x{opcode:X8} ({length} bytes): {reason}");
    }
}

using System.Globalization;
using System.Reflection;
using System.Text;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// DoD 2's password rule through a whole live session (step ST13; docs/session.md §10): with the test account's sentinel
/// password in secrets.json, a login, entering the world, LoginComplete, the player tools, a chat line, a logoff and the
/// session's disposal leave the sentinel in none of: the recording (ASCII, UTF-8, UTF-16LE, or as a LoginRequest's
/// String32L), the log, the action log, the text of every published event and handle, and the exceptions the gates
/// throw. The session's credentials are zeroed when it is disposed.
/// </summary>
/// <remarks>
/// The network session's own copy of the password is zeroed at character select by net (Lifestone.Net.Tests
/// <c>SentinelPasswordTests</c>); here the network session is <see cref="ScriptedAceSession"/>, which never reads the
/// password, so these tests prove the session layer itself keeps it out of everything it writes.
/// </remarks>
public sealed class PasswordHygieneTests
{
    [Fact]
    public void Sentinel_password_appears_nowhere()
    {
        var texts = new List<string>();
        byte[] recording;
        var exceptions = new List<Exception>();
        using (var h = new LiveHarness())
        {
            SubscribeToEveryEvent(h.Session.Bus, texts);
            h.Present();
            var status = h.Session.Tools.Status(ActionSource.Ui, showInChat: true);
            var where = h.Session.Tools.WhereAmI(ActionSource.Ui);
            var talk = h.Actions.Talk("hello from the hygiene test", ActionSource.Ui);
            Assert.True(h.RunUntil(() => status.IsCompleted && where.IsCompleted && talk.IsCompleted));
            Assert.Equal(ActionOutcomeKind.Confirmed, status.Outcome!.Value.Kind);
            var logOff = h.Actions.LogOff(ActionSource.Ui);
            Assert.True(h.RunUntil(() => logOff.IsCompleted));
            var quit = h.Actions.Quit(ActionSource.Ui);
            h.Run();
            Assert.Equal(ActionOutcomeKind.Sent, quit.Outcome!.Value.Kind);

            for (var i = 0; i < h.Session.ActionLog.Count; i++)
            {
                var entry = h.Session.ActionLog[i];
                texts.Add(entry.ToString());
                texts.Add(entry.Summary);
                texts.Add(entry.Outcome.Detail ?? string.Empty);
            }

            texts.AddRange(new[] { status.ToString(), where.ToString(), talk.ToString(), logOff.ToString(), quit.ToString() });
            texts.Add(h.Server.Credentials[0].ToString());
            h.Session.Dispose();
            texts.AddRange(h.Log.Lines);
            recording = h.RecordingBytes();
        }

        // The gates' exceptions for the same secrets: an unknown account, and a refused server.
        exceptions.Add(Assert.ThrowsAny<Exception>(() => PlayAccounts.Parse(SessionTestData.SecretsV2(), AccountChoice.FromOption("nobody", AccountChoice.Default))));
        exceptions.Add(Assert.ThrowsAny<Exception>(() => new LiveHarness(options: new SessionOptions { Account = "test", Server = SessionTestData.RefusedIpById["private-10"] })));

        Assert.True(recording.Length > 1000, "the recording is suspiciously small");
        Assert.True(texts.Count > 50, $"only {texts.Count} texts were collected");
        foreach (var sentinel in SessionTestData.Sentinels)
        {
            Assert.Equal(0, Count(recording, Encoding.ASCII.GetBytes(sentinel)));
            Assert.Equal(0, Count(recording, Encoding.UTF8.GetBytes(sentinel)));
            Assert.Equal(0, Count(recording, Encoding.Unicode.GetBytes(sentinel)));
            Assert.Equal(0, Count(recording, String32L(sentinel)));
            Assert.DoesNotContain(texts, t => t.Contains(sentinel, StringComparison.Ordinal));
            Assert.DoesNotContain(exceptions, e => e.ToString().Contains(sentinel, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_session_holds_the_password_only_as_zeroable_chars_and_zeroes_them_on_dispose()
    {
        var h = new LiveHarness(record: false);
        var login = h.Actions.Login();
        Assert.True(h.RunUntil(() => login.IsCompleted));
        var credentials = Assert.Single(h.Server.Credentials);
        Assert.Equal("lifestone-test", credentials.Account);
        Assert.Equal("account=lifestone-test, password=(redacted)", credentials.ToString());
        Assert.Equal(SessionTestData.TestSentinel, new string(PasswordChars(credentials)));

        h.Session.Dispose();

        Assert.True(credentials.IsDisposed);
        Assert.All(PasswordChars(credentials), c => Assert.Equal('\0', c));
    }

    [Fact]
    public void The_recording_never_keeps_a_client_body_off_the_allowlist()
    {
        byte[] recording;
        using (var h = new LiveHarness())
        {
            h.Present();
            var talk = h.Actions.Talk("Sentinel-TALK-text", ActionSource.Ui);
            Assert.True(h.RunUntil(() => talk.IsCompleted));
            h.Session.Dispose();
            Assert.True(h.Session.Recorder!.OmittedBodies == 0, "every client message the scripted server sends is on the allowlist");
            recording = h.RecordingBytes();
        }

        // Talk is a GameAction (allowlisted): the text is in the recording, as ACE's echo is; the allowlist itself is
        // RecordingFormatTests' business. This pins that the scripted traffic reaches the recorder both ways.
        Assert.True(Count(recording, Encoding.ASCII.GetBytes("Sentinel-TALK-text")) >= 1);
    }

    /// <summary>The credentials' private password array (reflection: tests only).</summary>
    internal static char[] PasswordChars(AcCredentials credentials)
    {
        var field = typeof(AcCredentials).GetField("_password", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AcCredentials has no _password field any more; update the hygiene test");
        return (char[])field.GetValue(credentials)!;
    }

    /// <summary>A LoginRequest's String32L of <paramref name="text"/>: a u32 byte count, a u8 length prefix, the bytes.</summary>
    private static byte[] String32L(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var result = new byte[5 + bytes.Length];
        BitConverter.GetBytes(bytes.Length + 1).CopyTo(result, 0);
        result[4] = (byte)bytes.Length;
        bytes.CopyTo(result, 5);
        return result;
    }

    private static int Count(byte[] haystack, byte[] needle)
    {
        var count = 0;
        for (var i = haystack.AsSpan().IndexOf(needle); i >= 0;)
        {
            count++;
            var next = haystack.AsSpan(i + 1).IndexOf(needle);
            i = next < 0 ? -1 : i + 1 + next;
        }

        return count;
    }

    /// <summary>Subscribes to every event type of the contract, adding each event's text (invariant culture).</summary>
    private static void SubscribeToEveryEvent(EventBus bus, List<string> texts)
    {
        var track = typeof(PasswordHygieneTests).GetMethod(nameof(Track), BindingFlags.Static | BindingFlags.NonPublic)!;
        var types = typeof(IGameEvent).Assembly.GetTypes().Where(t => t.IsValueType && typeof(IGameEvent).IsAssignableFrom(t)).ToList();
        Assert.True(types.Count >= 30, $"only {types.Count} event types found");
        foreach (var type in types)
        {
            track.MakeGenericMethod(type).Invoke(null, [bus, texts]);
        }
    }

    private static void Track<T>(EventBus bus, List<string> texts)
        where T : struct, IGameEvent =>
        bus.Subscribe((in T e) => texts.Add(string.Format(CultureInfo.InvariantCulture, "{0}", e)));
}

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// The headless host against the bundled ACE (step ST15; DoD 13, DoD 2): log in with the test account, enter the world,
/// materialise through portal space, check the server mod's status, log off and quit; and the recording and the log of
/// that run hold no trace of the password. Skipped, with the probe's reason, unless the backend runs with the Lifestone
/// server mod ready. Each test creates its own session inside its body (no fixtures: the loopback gate runs first).
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
public sealed class HeadlessLoopbackTests(ITestOutputHelper output)
{
    /// <summary>The character the loopback runs use, created on the test account when missing.</summary>
    private const string Character = "Lifestone Loopback";

    // TODO(m4-headless-digest): this test failed on the digest in P4's round 1 (shared) and round 2 (per-test) and passed in the other two runs; the path it runs (HeadlessRunner.Start + LoginScenario, one login, no
    // Arm, no keep-alive, no StayInWorld) is none of P4's changes, so the cause is outside P4 and timing-dependent (the live digest is taken in GameSession.Dispose after the quit; a message or event that
    // reaches the state there and is not in the recording, or the reverse, would differ run to run). The failure carried no evidence, so it now carries the trailer and both digests; the next failure names the cause.
    [LoopbackFact]
    public void Headless_login_enters_and_leaves_the_world()
    {
        using var run = LiveRun.Start(output);
        var result = run.Result;
        Assert.True(result.Ok, result.Failure);
        Assert.Equal(1, result.LoginCompletes);
        Assert.Contains(SessionPhase.InWorld, result.Phases);
        Assert.Equal(SessionPhase.CharacterSelect, result.Phases[^1]);
        Assert.True(run.Replay.DigestMatches, "the recording does not replay to its own digest: " + run.Replay.DigestDiagnosis());
        Assert.Equal(0, run.Replay.Malformed);
    }

    [LoopbackFact]
    public void Headless_recording_contains_no_password()
    {
        using var run = LiveRun.Start(output);
        Assert.True(run.Result.Ok, run.Result.Failure);

        // The real password, in memory only: counted, never printed.
        var hits = 0;
        var secrets = File.ReadAllBytes(BackendProbe.MainCheckoutFiles.SecretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(BackendProbe.TestAccountKey, AccountChoice.Default));
            var password = PasswordOf(credentials);
            try
            {
                foreach (var needle in Encodings(password))
                {
                    hits += Count(run.Recording, needle) + Count(run.LogBytes, needle);
                    CryptographicOperations.ZeroMemory(needle);
                }
            }
            finally
            {
                Array.Clear(password);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }

        output.WriteLine($"password occurrences in the recording and the log: {hits}");
        Assert.Equal(0, hits);
    }

    /// <summary>The password as ASCII, UTF-16LE and a LoginRequest's String32L (u32 count, u8 length, bytes).</summary>
    private static IEnumerable<byte[]> Encodings(char[] password)
    {
        var ascii = Encoding.ASCII.GetBytes(password);
        yield return ascii;
        yield return Encoding.Unicode.GetBytes(password);
        var string32 = new byte[5 + ascii.Length];
        BitConverter.GetBytes(ascii.Length + 1).CopyTo(string32, 0);
        string32[4] = (byte)ascii.Length;
        ascii.CopyTo(string32, 5);
        yield return string32;
    }

    /// <summary>A copy of the credentials' password (reflection: tests only; the caller zeroes it).</summary>
    private static char[] PasswordOf(AcCredentials credentials)
    {
        var field = typeof(AcCredentials).GetField("_password", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AcCredentials has no _password field any more; update the loopback hygiene test");
        return (char[])((char[])field.GetValue(credentials)!).Clone();
    }

    private static int Count(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var span = haystack.AsSpan();
        for (var at = span.IndexOf(needle); at >= 0; at = span.IndexOf(needle))
        {
            count++;
            span = span[(at + 1)..];
        }

        return count;
    }

    /// <summary>One headless login run against the backend, with its recording and log kept in memory.</summary>
    private sealed class LiveRun : IDisposable
    {
        private LiveRun(HeadlessRunResult result, byte[] recording, byte[] log, ReplayResult replay)
        {
            Result = result;
            Recording = recording;
            LogBytes = log;
            Replay = replay;
        }

        public HeadlessRunResult Result { get; }

        public byte[] Recording { get; }

        public byte[] LogBytes { get; }

        public ReplayResult Replay { get; }

        public static LiveRun Start(ITestOutputHelper output)
        {
            var files = BackendProbe.MainCheckoutFiles;
            var log = new RecordingLogSink();
            using var dats = DatSet.Open();
            using var recording = new MemoryStream();
            HeadlessRunResult result;
            LiveCharacter.Release(output, BackendProbe.TestAccountKey);
        var secrets = File.ReadAllBytes(files.SecretsFile);
            HeadlessRunner runner;
            try
            {
                runner = HeadlessRunner.Start(
                    new HeadlessRunOptions
                    {
                        Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                        QuitAfterSeconds = 120,
                    },
                    new LoginScenario(createCharacter: Character, skillClasses: HeadlessCharacter.RetailSkillClasses(dats)),
                    secrets,
                    File.ReadAllBytes(files.PidsFile),
                    dats,
                    TimeProvider.System,
                    log,
                    recording);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secrets);
            }

            var context = new GameThreadSynchronizationContext();
            using (context.Install())
            using (runner)
            {
                result = runner.Run(context, CancellationToken.None);
            }

            output.WriteLine(result.SummaryLine("memory"));
            var bytes = recording.ToArray();
            var replay = new SessionReplayer().Replay(new MemoryStream(bytes));
            return new LiveRun(result, bytes, Encoding.UTF8.GetBytes(string.Join('\n', log.Lines)), replay);
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(Recording);
            CryptographicOperations.ZeroMemory(LogBytes);
        }
    }
}

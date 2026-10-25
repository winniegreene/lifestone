using System.Diagnostics;
using System.Globalization;
using Lifestone.Client.Tests;

namespace Lifestone.Tests.Shared.Loopback;

/// <summary>
/// The cross-process lock that serialises loopback tests per test account (M4 D31): every worktree and every <c>dotnet test</c>
/// shares the one localhost ACE, but ACE boots a second login of one account, so only one process at a time may run the loopback tests
/// of an account. Different accounts (<c>test</c>, <c>test-melee</c>, <c>test-archer</c>, <c>test-tools</c>) hold different locks, so their processes run at
/// once. Inside a process the <see cref="LoopbackCollection"/> runs tests one at a time.
/// </summary>
/// <remarks>
/// <para>The lock is a file per account, <c>&lt;main checkout&gt;\cache\loopback-&lt;account&gt;.lock</c> (<see cref="PathFor"/>), opened with
/// <see cref="FileShare.None"/> and <see cref="FileOptions.DeleteOnClose"/>. The OS releases it when the holder exits or dies, and a crashed holder
/// leaves at most an unlocked file behind. (A named mutex is thread-affine, which xUnit's fixture disposal breaks, and a
/// semaphore does not get its count back when its holder crashes.)</para>
/// <para>A process takes an account's lock when its first loopback test of that account is about to run (<see cref="EnsureHeldByThisProcess(string)"/>),
/// waits at most <see cref="DefaultMaxWait"/> for it, and keeps it until it exits. If the wait runs out, every loopback test of that account in the
/// process skips with the reason, which names the account.</para>
/// </remarks>
public sealed class LoopbackLock : IDisposable
{
    /// <summary>How long a run waits for another run's lock before its loopback tests skip.</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly object ProcessGate = new();
    private static readonly Dictionary<string, (LoopbackLock? Held, string? Failure)> Processes = new(StringComparer.Ordinal);

    private readonly FileStream _file;

    private LoopbackLock(FileStream file, string path)
    {
        _file = file;
        Path = path;
    }

    /// <summary>The lock file of the <c>test</c> account in the main checkout (<see cref="RepositoryFiles.MainCheckout"/>).</summary>
    public static string DefaultPath => PathFor(BackendProbe.TestAccountKey);

    /// <summary>The lock file of <paramref name="account"/> in the main checkout: <c>cache\loopback-&lt;account&gt;.lock</c>.</summary>
    /// <param name="account">An account key (lower-case letters, digits and '-').</param>
    public static string PathFor(string account)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        foreach (var c in account)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            {
                throw new ArgumentException("an account key is lower-case letters, digits and '-'", nameof(account));
            }
        }

        return System.IO.Path.Combine(RepositoryFiles.MainCheckout, "cache", "loopback-" + account + ".lock");
    }

    /// <summary>True when this process holds the main checkout's lock of the <c>test</c> account.</summary>
    public static bool IsHeldByThisProcess => IsHeldByThisProcessFor(BackendProbe.TestAccountKey);

    /// <summary>True when this process holds the main checkout's lock of <paramref name="account"/>.</summary>
    /// <param name="account">The account key.</param>
    public static bool IsHeldByThisProcessFor(string account)
    {
        lock (ProcessGate)
        {
            return Processes.TryGetValue(account, out var state) && state.Held is not null;
        }
    }

    /// <summary>The lock file this instance holds.</summary>
    public string Path { get; }

    /// <summary>As <see cref="EnsureHeldByThisProcess(string)"/> for the <c>test</c> account.</summary>
    public static string? EnsureHeldByThisProcess() => EnsureHeldByThisProcess(BackendProbe.TestAccountKey);

    /// <summary>
    /// Takes <paramref name="account"/>'s lock for this process if it does not hold it yet (waiting at most
    /// <see cref="DefaultMaxWait"/>), and keeps it until the process exits. Returns null when the lock is held, or the
    /// skip reason (naming the account) when it could not be taken; a failure is remembered per account, so the remaining tests do not wait again.
    /// </summary>
    /// <param name="account">The account key.</param>
    public static string? EnsureHeldByThisProcess(string account)
    {
        var path = PathFor(account);
        lock (ProcessGate)
        {
            if (!Processes.TryGetValue(account, out var state))
            {
                var held = TryAcquire(path, DefaultMaxWait, out var failure);
                state = (held, failure is null ? null : $"account '{account}': {failure}");
                Processes[account] = state;
                if (held is not null)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => held.Dispose();
                }
            }

            return state.Held is null ? state.Failure : null;
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> exclusively, retrying while another holder has it, for at most
    /// <paramref name="maxWait"/>. Returns the held lock (dispose it to release and delete the file), or null with
    /// <paramref name="failure"/> saying why.
    /// </summary>
    public static LoopbackLock? TryAcquire(string path, TimeSpan maxWait, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = System.IO.Path.GetFullPath(path);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure = $"the loopback lock folder for {full} cannot be created ({e.GetType().Name})";
            return null;
        }

        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var file = new FileStream(full, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                failure = null;
                return new LoopbackLock(file, full);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && waited.Elapsed < maxWait)
            {
                // Another run holds it (a sharing violation, IOException), or its holder has just released it and the
                // file is still delete-pending, which Windows reports as access denied (UnauthorizedAccessException)
                // while another handle (an antivirus or indexer scan) is open. Either way, wait and try again.
            }
            catch (IOException)
            {
                failure = $"another loopback test run (another worktree or process) has held {full} for {FormatWait(maxWait)}: " +
                          "loopback tests of one test account run one process at a time (ACE boots a second login of an account)";
                return null;
            }
            catch (UnauthorizedAccessException e)
            {
                failure = $"the loopback lock {full} cannot be opened ({e.GetType().Name}, still after {FormatWait(maxWait)})";
                return null;
            }

            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>Releases the lock; the file is deleted as it closes.</summary>
    public void Dispose() => _file.Dispose();

    private static string FormatWait(TimeSpan wait) => wait.TotalMinutes >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{wait.TotalMinutes:0.#} min")
        : string.Create(CultureInfo.InvariantCulture, $"{wait.TotalMilliseconds:0} ms");
}

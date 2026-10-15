using System.Diagnostics;
using System.Globalization;
using Lifestone.Client.Tests;

namespace Lifestone.Tests.Shared.Loopback;

/// <summary>
/// The cross-process lock that serialises loopback tests: every worktree and every <c>dotnet test</c> shares the one
/// localhost ACE and its test account, so only one process at a time may run loopback tests. Inside a process the
/// <see cref="LoopbackCollection"/> runs them one at a time.
/// </summary>
/// <remarks>
/// <para>The lock is a file, <c>&lt;main checkout&gt;\cache\loopback.lock</c>, opened with <see cref="FileShare.None"/>
/// and <see cref="FileOptions.DeleteOnClose"/>. The OS releases it when the holder exits or dies, and a crashed holder
/// leaves at most an unlocked file behind. (A named mutex is thread-affine, which xUnit's fixture disposal breaks, and a
/// semaphore does not get its count back when its holder crashes.)</para>
/// <para>A process takes the lock when its first loopback test is about to run (<see cref="EnsureHeldByThisProcess"/>),
/// waits at most <see cref="DefaultMaxWait"/> for it, and keeps it until it exits. If the wait runs out, every loopback
/// test of the process skips with the reason.</para>
/// </remarks>
public sealed class LoopbackLock : IDisposable
{
    /// <summary>How long a run waits for another run's lock before its loopback tests skip.</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly object ProcessGate = new();
    private static LoopbackLock? _processLock;
    private static string? _processFailure;
    private static bool _processAttempted;

    private readonly FileStream _file;

    private LoopbackLock(FileStream file, string path)
    {
        _file = file;
        Path = path;
    }

    /// <summary>The lock file of the main checkout (<see cref="RepositoryFiles.MainCheckout"/>).</summary>
    public static string DefaultPath => System.IO.Path.Combine(RepositoryFiles.MainCheckout, "cache", "loopback.lock");

    /// <summary>True when this process holds the main checkout's lock.</summary>
    public static bool IsHeldByThisProcess
    {
        get
        {
            lock (ProcessGate)
            {
                return _processLock is not null;
            }
        }
    }

    /// <summary>The lock file this instance holds.</summary>
    public string Path { get; }

    /// <summary>
    /// Takes the main checkout's lock for this process if it does not hold it yet (waiting at most
    /// <see cref="DefaultMaxWait"/>), and keeps it until the process exits. Returns null when the lock is held, or the
    /// skip reason when it could not be taken; a failure is remembered, so the remaining tests do not wait again.
    /// </summary>
    public static string? EnsureHeldByThisProcess()
    {
        lock (ProcessGate)
        {
            if (!_processAttempted)
            {
                _processAttempted = true;
                _processLock = TryAcquire(DefaultPath, DefaultMaxWait, out _processFailure);
                if (_processLock is { } held)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => held.Dispose();
                }
            }

            return _processLock is null ? _processFailure : null;
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
                          "loopback tests run one process at a time against the single localhost ACE";
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

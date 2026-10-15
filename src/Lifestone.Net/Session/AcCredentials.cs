namespace Lifestone.Net.Session;

/// <summary>
/// An account name and its password, held so that the password never becomes a <see cref="string"/>: it is copied into
/// a private <see cref="char"/> array, never printed (<see cref="ToString"/> is redacted), and zeroed on
/// <see cref="Dispose"/>. Hosts build one from secrets.json with <c>Utf8JsonReader.CopyString(Span&lt;char&gt;)</c>.
/// </summary>
/// <remarks>
/// The session copies the password once for its login retries, encodes it straight into a pooled LoginRequest buffer
/// that is cleared after the send, and zeroes its copy on reaching character select or on disposal (N5). The caller
/// disposes its own instance.
/// </remarks>
public sealed class AcCredentials : IDisposable
{
    /// <summary>The longest account name ACE accepts in a LoginRequest (docs/protocol.md §4.1).</summary>
    public const int MaxAccountLength = 50;

    private readonly char[] _password;
    private bool _disposed;

    /// <summary>
    /// Copies <paramref name="password"/>; nothing keeps a reference to the caller's span. The account must be 1 to
    /// <see cref="MaxAccountLength"/> printable ASCII characters (only ASCII round-trips through ACE, which lower-cases
    /// it); the password must not be empty.
    /// </summary>
    /// <exception cref="ArgumentException">The account or the password is not acceptable. The message never contains the
    /// password.</exception>
    public AcCredentials(string account, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Length is 0 or > MaxAccountLength)
        {
            throw new ArgumentException($"the account name must be 1 to {MaxAccountLength} characters long", nameof(account));
        }

        foreach (var c in account)
        {
            if (c is < ' ' or > '~')
            {
                throw new ArgumentException("the account name must be printable ASCII", nameof(account));
            }
        }

        if (password.IsEmpty)
        {
            throw new ArgumentException("the password is empty (ACE refuses an account-only login)", nameof(password));
        }

        Account = account;
        _password = password.ToArray();
    }

    /// <summary>The account name.</summary>
    public string Account { get; }

    /// <summary>True once <see cref="Dispose"/> has zeroed the password.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>The password, for the session's LoginRequest encoder only. Throws after <see cref="Dispose"/>.</summary>
    internal ReadOnlySpan<char> Password
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _password;
        }
    }

    /// <summary>Zeroes the password. Safe to call more than once.</summary>
    public void Dispose()
    {
        Array.Clear(_password);
        _disposed = true;
    }

    /// <summary>The account and a redacted password, e.g. <c>account=lifestone-test, password=(redacted)</c>.</summary>
    public override string ToString() => $"account={Account}, password=(redacted)";
}

using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// The fake server's configuration and its small scripted world: accounts, characters, the server's dat iterations,
/// the delays that make ACE's races reproducible, and the replies to the server mod's <c>@lifestone</c> commands.
/// Defaults are ACE's (config defaults and <c>PropertyManager</c> defaults) unless a member says otherwise.
/// </summary>
/// <remarks>Test passwords only. The fake compares them and never logs, prints or stores them anywhere else.</remarks>
internal sealed class FakeAceScript
{
    /// <summary>The seed of everything random in the fake: ISAAC seeds, cookies.</summary>
    public int Seed { get; init; } = 0x5EED;

    /// <summary>The world's name in F7E1.</summary>
    public string WorldName { get; init; } = "Lifestone Fake";

    /// <summary>Whether the world is open; a closed world refuses ConnectResponse (F659 0x15), CharacterCreate and EnterWorld.</summary>
    public bool WorldOpen { get; set; } = true;

    /// <summary>ACE's <c>AllowAutoAccountCreation</c>: an unknown account with a password is created, lower-cased.</summary>
    public bool AllowAutoAccountCreation { get; init; } = true;

    /// <summary>ACE's <c>account_login_boots_in_use</c>: a second login boots the first (both get F659 0x01).</summary>
    public bool AccountLoginBootsInUse { get; init; } = true;

    /// <summary>ACE's <c>allow_highres_dat</c>: F7E5's product id 5 instead of 1.</summary>
    public bool AllowHighResDat { get; init; }

    /// <summary>The server's portal dat iteration.</summary>
    public int PortalIteration { get; init; } = 2072;

    /// <summary>The server's cell dat iteration.</summary>
    public int CellIteration { get; init; } = 982;

    /// <summary>The server's language dat iteration.</summary>
    public int LanguageIteration { get; init; } = 994;

    /// <summary>The server's high-res dat iteration, or null when it has none.</summary>
    public int? HighResIteration { get; init; }

    /// <summary>The boot text after " because " when the client's dats are older (the fake's own wording).</summary>
    public string DatOlderMessage { get; init; } = "the client's dat files are older than the server's (fake)";

    /// <summary>The boot text after " because " when the client's dats are newer (the fake's own wording).</summary>
    public string DatNewerMessage { get; init; } = "the client's dat files are newer than the server's (fake)";

    /// <summary>How long the account lookup (ACE's <c>DoLogin</c> task) takes before the ConnectRequest is queued.</summary>
    public TimeSpan AccountLookupDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// The database write between queuing the ConnectRequest and entering AuthConnectResponse: a ConnectResponse that
    /// arrives inside it is ignored (docs/protocol.md §4.1 row 3).
    /// </summary>
    public TimeSpan DbWriteDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a session may go without a valid packet before AuthConnectResponse (ACE <c>DefaultAuthTimeout</c>).</summary>
    public TimeSpan AuthTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a session may go without a valid packet afterwards (ACE <c>DefaultSessionTimeout</c>).</summary>
    public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a terminating session keeps flushing before it goes silent (ACE's two seconds).</summary>
    public TimeSpan TerminationFlush { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The logoff animation: F653, F658 and F7E1 follow a CharacterLogOff after this (ACE: 6 s).</summary>
    public TimeSpan LogOffDelay { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>ACE's <c>char_delete_time</c>: how long a deleted character can be restored.</summary>
    public TimeSpan CharacterDeleteTime { get; init; } = TimeSpan.FromSeconds(3600);

    /// <summary>ACE's <c>MaximumAllowedSessions</c>.</summary>
    public int MaximumSessions { get; init; } = 128;

    /// <summary>ACE's <c>max_chars_per_account</c>.</summary>
    public uint SlotCount { get; init; } = 11;

    /// <summary>ACE's <c>use_turbine_chat</c>.</summary>
    public bool UseTurbineChat { get; init; } = true;

    /// <summary>
    /// ACE answers a create with a taken name twice with NameInUse (its first availability check does not stop the
    /// create; <c>CharacterHandler.cs</c>). False sends one.
    /// </summary>
    public bool DuplicateNameInUse { get; init; } = true;

    /// <summary>
    /// CharacterError codes to answer the next F657 CharacterEnterWorld requests with, one per request, before any other
    /// check (for example 0x0D, a character still in the world after a crash). Empty by default.
    /// </summary>
    public Queue<uint> EnterWorldErrors { get; } = new();

    /// <summary>Names the taboo table and the creature-name check refuse (F643 NameBanned).</summary>
    public HashSet<string> BannedNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The accounts, by name (case-insensitive, as ACE's database looks them up).</summary>
    public Dictionary<string, FakeAccount> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a new character starts (docs/protocol.md §4.3: the Training Academy cell for start area 0).</summary>
    public AcePosition StartPosition { get; init; } = new(0x860201AD, 12.32f, -28.482f, 0.005f, 0.3389f, 0f, 0f, 0.9408f);

    /// <summary>How many other objects the fake creates around a player entering the world.</summary>
    public int WorldObjectsOnEnter { get; init; } = 3;

    /// <summary>
    /// The PlayerDescription sent to a player entering the world, given the character's name; null sends
    /// <see cref="AcePlayerDescription.NewCharacter"/>.
    /// </summary>
    public Func<string, AcePlayerDescription>? PlayerDescription { get; init; }

    /// <summary>
    /// Replies to a Talk that starts with <c>@lifestone</c>: given the text, the F7E0 lines to send back. Null answers
    /// nothing (an unknown command on a server without the mod).
    /// </summary>
    public Func<string, IReadOnlyList<string>>? ToolReplies { get; init; }

    /// <summary>The F7E0 chat type the tool replies use.</summary>
    public int ToolReplyChatType { get; init; }

    /// <summary>The server's in-game clock at start, in seconds (ACE's <c>PortalYearTicks</c>).</summary>
    public double PortalYearStart { get; init; } = 10_000_000;

    /// <summary>Adds an account with a test password and returns it.</summary>
    public FakeAccount AddAccount(string name, string password)
    {
        var account = new FakeAccount(name, password);
        Accounts[name] = account;
        return account;
    }
}

/// <summary>An account of the fake. Test password only.</summary>
internal sealed class FakeAccount(string name, string password)
{
    /// <summary>The account name as stored (the string the CharacterList carries).</summary>
    public string Name { get; } = name;

    /// <summary>True when <paramref name="candidate"/> is the password.</summary>
    public bool PasswordMatches(string? candidate) => string.Equals(password, candidate, StringComparison.Ordinal);

    /// <summary>The characters, in the order ACE lists them (most recent login first).</summary>
    public List<FakeCharacter> Characters { get; } = [];

    /// <summary>When a ban ends, if banned.</summary>
    public DateTimeOffset? BanExpires { get; set; }

    /// <summary>The ban's reason.</summary>
    public string? BanReason { get; set; }

    /// <summary>Redacted: the password never reaches a string.</summary>
    public override string ToString() => $"FakeAccount({Name})";
}

/// <summary>A character of the fake.</summary>
internal sealed class FakeCharacter(uint guid, string name)
{
    /// <summary>The GUID.</summary>
    public uint Guid { get; } = guid;

    /// <summary>The name.</summary>
    public string Name { get; } = name;

    /// <summary>When a pending deletion completes; null when the character is active.</summary>
    public DateTimeOffset? DeleteTime { get; set; }

    /// <summary>Deleted for good.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>The 55 skill advancement classes it was created with.</summary>
    public uint[] Skills { get; init; } = [];

    /// <summary>Where it is.</summary>
    public AcePosition Position { get; set; }

    /// <summary>The last login, for ACE's list order.</summary>
    public DateTimeOffset LastLogin { get; set; }
}

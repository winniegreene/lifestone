using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Credentials;

/// <summary>How <see cref="AccountChoice"/> picks an account.</summary>
public enum AccountChoiceKind
{
    /// <summary>secrets.json's <c>defaultAccount</c> (schema 2), else the play account; schema 1's <c>testAccount</c>.</summary>
    Default,

    /// <summary>The play account: <c>accounts.play</c> (schema 2); schema 1's <c>testAccount</c>, which migration makes the play account.</summary>
    Play,

    /// <summary>The test account, <c>accounts.test</c> (schema 2 only): the headless host's and the loopback tests' default.</summary>
    Test,

    /// <summary>The account <c>--account</c> names: a key of <c>accounts</c>, else a username (case-insensitive).</summary>
    Named,
}

/// <summary>
/// Which account of secrets.json to log in with (owner decision D10). Its <see cref="ToString"/> never repeats a
/// <see cref="AccountChoiceKind.Named"/> choice's name: a mistyped option could have been given a password.
/// </summary>
public readonly record struct AccountChoice
{
    private AccountChoice(AccountChoiceKind kind, string? name)
    {
        Kind = kind;
        Name = name;
    }

    /// <summary>secrets.json's default account (<see cref="AccountChoiceKind.Default"/>).</summary>
    public static AccountChoice Default => new(AccountChoiceKind.Default, null);

    /// <summary>The play account (<see cref="AccountChoiceKind.Play"/>).</summary>
    public static AccountChoice Play => new(AccountChoiceKind.Play, null);

    /// <summary>The test account (<see cref="AccountChoiceKind.Test"/>).</summary>
    public static AccountChoice Test => new(AccountChoiceKind.Test, null);

    /// <summary>How the account is picked.</summary>
    public AccountChoiceKind Kind { get; }

    /// <summary>The key or username of a <see cref="AccountChoiceKind.Named"/> choice; otherwise null.</summary>
    public string? Name { get; }

    /// <summary>The account with key or username <paramref name="name"/> (<see cref="AccountChoiceKind.Named"/>).</summary>
    public static AccountChoice Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new AccountChoice(AccountChoiceKind.Named, name);
    }

    /// <summary>The choice for <c>--account</c>'s value: <paramref name="fallback"/> when none was given, else <see cref="Named"/>.</summary>
    public static AccountChoice FromOption(string? account, AccountChoice fallback) =>
        string.IsNullOrWhiteSpace(account) ? fallback : Named(account);

    /// <summary>The choice in words, never with a named choice's name.</summary>
    public override string ToString() => Kind switch
    {
        AccountChoiceKind.Default => "the default account",
        AccountChoiceKind.Play => "the play account",
        AccountChoiceKind.Test => "the test account",
        _ => "the account named by --account",
    };
}

/// <summary>One account of secrets.json, without its password.</summary>
/// <param name="Key">Its key under <c>accounts</c> (schema 2), or <c>testAccount</c> (schema 1).</param>
/// <param name="Username">Its ACE account name, or null when the entry has none.</param>
public readonly record struct PlayAccountInfo(string Key, string? Username);

/// <summary>
/// Thrown when secrets.json cannot give the chosen account. The message names keys and usernames only, never a value
/// of the file otherwise.
/// </summary>
public sealed class PlayAccountException : InvalidOperationException
{
    /// <summary>The exception with <paramref name="message"/> (no secret in it).</summary>
    public PlayAccountException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Reads the login account from server\runtime\secrets.json, which the host read as bytes (owner decision D10;
/// docs/session.md §4): schema 1's <c>testAccount{username,password}</c> (server/setup-server.ps1 before the server-mod
/// track) and schema 2's <c>accounts.{play,test,&lt;name&gt;}</c> with <c>defaultAccount</c>.
/// </summary>
/// <remarks>
/// <para><b>The password never becomes a string.</b> It is copied out of the bytes with
/// <see cref="Utf8JsonReader.CopyString(Span{char})"/> into a pooled <see cref="char"/> array, wrapped in net's
/// <see cref="AcCredentials"/> (which copies it into its own zeroable array), and the pooled array is zeroed before it is
/// returned. The caller zeroes its byte buffer after the call (<c>CryptographicOperations.ZeroMemory</c>).</para>
/// <para><b>Errors</b> (<see cref="PlayAccountException"/>) name keys and usernames, never a value; a JSON syntax error
/// is reported by line, never with the reader's message, which can quote the file. The test account is never the play
/// account: ACE boots a second login of the same account, so the tests must not share it.</para>
/// </remarks>
public static class PlayAccounts
{
    /// <summary>The file, for messages.</summary>
    public const string FileName = "secrets.json";

    /// <summary>The play account's key (schema 2).</summary>
    public const string PlayKey = "play";

    /// <summary>The test account's key (schema 2).</summary>
    public const string TestKey = "test";

    /// <summary>Schema 1's one account.</summary>
    public const string SchemaOneKey = "testAccount";

    /// <summary>The newest schema this reader understands.</summary>
    public const int NewestSchema = 2;

    private const string SetupHint = "run server\\setup-server.ps1 from the main checkout";

    /// <summary>
    /// The credentials of the account <paramref name="choice"/> picks. The caller disposes them (which zeroes the
    /// password).
    /// </summary>
    /// <param name="secretsJson">The bytes of server\runtime\secrets.json (empty when it does not exist).</param>
    /// <param name="choice">Which account.</param>
    /// <exception cref="PlayAccountException">The file cannot give that account; the message says why, without values.</exception>
    public static AcCredentials Parse(ReadOnlySpan<byte> secretsJson, AccountChoice choice)
    {
        var layout = ReadLayout(secretsJson);
        var target = Resolve(layout, choice);
        return ReadCredentials(secretsJson, target);
    }

    /// <summary><see cref="Parse"/> without the exception: false with the reason (no values in it).</summary>
    public static bool TryParse(ReadOnlySpan<byte> secretsJson, AccountChoice choice, out AcCredentials? credentials, out string? problem)
    {
        try
        {
            credentials = Parse(secretsJson, choice);
            problem = null;
            return true;
        }
        catch (PlayAccountException e)
        {
            credentials = null;
            problem = e.Message;
            return false;
        }
    }

    /// <summary>The accounts of the file, keys and usernames only (for an account picker or a message), in file order.</summary>
    /// <exception cref="PlayAccountException">The file is malformed.</exception>
    public static IReadOnlyList<PlayAccountInfo> List(ReadOnlySpan<byte> secretsJson)
    {
        var layout = ReadLayout(secretsJson);
        return layout.Schema == NewestSchema ? layout.Accounts : layout.SchemaOne is { } one ? [one] : [];
    }

    private static Layout ReadLayout(ReadOnlySpan<byte> json)
    {
        if (SecretsJson.IsBlank(json))
        {
            throw new PlayAccountException($"{FileName} is missing or empty ({SetupHint})");
        }

        var layout = new Layout();
        try
        {
            var reader = SecretsJson.Reader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new PlayAccountException($"{FileName} is not a JSON object ({SetupHint})");
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("schema"u8))
                {
                    reader.Read();
                    layout.Schema = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var schema)
                        ? schema
                        : throw new PlayAccountException($"{FileName}'s schema is not a whole number");
                }
                else if (reader.ValueTextEquals("defaultAccount"u8))
                {
                    reader.Read();
                    layout.DefaultAccount = reader.TokenType switch
                    {
                        JsonTokenType.String => reader.GetString(),
                        JsonTokenType.Null => null,
                        _ => throw new PlayAccountException($"{FileName}'s defaultAccount is not a string"),
                    };
                }
                else if (reader.ValueTextEquals("accounts"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        throw new PlayAccountException($"{FileName}'s accounts is not an object");
                    }

                    layout.HasAccounts = true;
                    ReadAccounts(ref reader, layout.Accounts);
                }
                else if (reader.ValueTextEquals(SchemaOneKey))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        throw new PlayAccountException($"{FileName}'s {SchemaOneKey} is not an object");
                    }

                    layout.SchemaOne = new PlayAccountInfo(SchemaOneKey, ReadUsername(ref reader));
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            // Anything after the root object (a stray brace) is a syntax error: the reader throws on it.
            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                throw new PlayAccountException($"{FileName} has content after its object");
            }
        }
        catch (JsonException e)
        {
            throw new PlayAccountException(SecretsJson.NotValidJson(FileName, e));
        }

        return layout;
    }

    private static void ReadAccounts(ref Utf8JsonReader reader, List<PlayAccountInfo> accounts)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var key = reader.GetString() ?? string.Empty;
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new PlayAccountException($"{FileName}'s accounts.{Printable(key)} is not an object");
            }

            var username = ReadUsername(ref reader);
            if (!accounts.Exists(a => a.Key == key))
            {
                accounts.Add(new PlayAccountInfo(key, username));
            }
        }
    }

    /// <summary>The <c>username</c> of the account object the reader is at; every other member is skipped unread.</summary>
    private static string? ReadUsername(ref Utf8JsonReader reader)
    {
        string? username = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isUsername = reader.ValueTextEquals("username"u8);
            reader.Read();
            if (isUsername && reader.TokenType == JsonTokenType.String)
            {
                username ??= reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return username;
    }

    private static Target Resolve(Layout layout, AccountChoice choice)
    {
        if (layout.Schema is { } schema && schema != 1 && schema != NewestSchema)
        {
            throw new PlayAccountException(schema > NewestSchema
                ? $"{FileName} is schema {schema}, newer than this Lifestone reads (1 and 2); update Lifestone"
                : $"{FileName} has an unknown schema {schema} ({SetupHint})");
        }

        return layout.Schema == NewestSchema ? ResolveSchemaTwo(layout, choice) : ResolveSchemaOne(layout, choice);
    }

    private static Target ResolveSchemaTwo(Layout layout, AccountChoice choice)
    {
        if (!layout.HasAccounts)
        {
            throw new PlayAccountException($"{FileName} (schema 2) has no accounts ({SetupHint})");
        }

        switch (choice.Kind)
        {
            case AccountChoiceKind.Default:
                var key = layout.DefaultAccount ?? PlayKey;
                return ByKey(layout, key)
                    ?? throw new PlayAccountException(
                        $"{FileName}'s defaultAccount names '{Printable(key)}', which is not one of its accounts ({Describe(layout)})");
            case AccountChoiceKind.Play:
                return ByKey(layout, PlayKey) ?? throw Missing(layout, PlayKey);
            case AccountChoiceKind.Test:
                return ByKey(layout, TestKey) ?? throw Missing(layout, TestKey);
            default:
                var name = choice.Name ?? string.Empty;
                if (ByKey(layout, name) is { } byKey)
                {
                    return byKey;
                }

                foreach (var account in layout.Accounts)
                {
                    if (string.Equals(account.Username, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new Target(account.Key, IsSchemaOne: false);
                    }
                }

                throw NoMatch(layout);
        }
    }

    private static Target ResolveSchemaOne(Layout layout, AccountChoice choice)
    {
        if (layout.SchemaOne is not { } one)
        {
            throw new PlayAccountException(layout.HasAccounts
                ? $"{FileName} has accounts but no \"schema\": 2 ({SetupHint})"
                : $"{FileName} (schema 1) has no {SchemaOneKey} ({SetupHint})");
        }

        var target = new Target(SchemaOneKey, IsSchemaOne: true);
        return choice.Kind switch
        {
            AccountChoiceKind.Default or AccountChoiceKind.Play => target,
            AccountChoiceKind.Test => throw new PlayAccountException(
                $"{FileName} is schema 1, which has no test account (accounts.{TestKey}); {SetupHint} to migrate it to schema 2, " +
                "which adds one (the tests never share the play account: ACE boots a second login of an account)"),
            _ => choice.Name == PlayKey || string.Equals(one.Username, choice.Name, StringComparison.OrdinalIgnoreCase) ? target : throw NoMatch(layout),
        };
    }

    private static Target? ByKey(Layout layout, string key) =>
        layout.Accounts.Exists(a => a.Key == key) ? new Target(key, IsSchemaOne: false) : null;

    private static PlayAccountException Missing(Layout layout, string key) =>
        new($"{FileName} has no accounts.{key} ({Describe(layout)}); {SetupHint}");

    private static PlayAccountException NoMatch(Layout layout) =>
        new($"no account in {FileName} has the key or username given to --account (the name is not repeated here); it has {Describe(layout)}");

    /// <summary>The accounts as <c>key (username)</c>, never anything else.</summary>
    private static string Describe(Layout layout)
    {
        var accounts = layout.Schema == NewestSchema ? layout.Accounts : layout.SchemaOne is { } one ? [one] : [];
        return accounts.Count == 0
            ? "no accounts"
            : string.Join(", ", accounts.Select(a => $"{Printable(a.Key)} ({(a.Username is null ? "no username" : Printable(a.Username))})"));
    }

    /// <summary>A key or username for a message: as is when it is short printable ASCII, else a placeholder.</summary>
    private static string Printable(string text) =>
        text.Length is > 0 and <= 64 && text.All(c => c is >= ' ' and <= '~') ? text : "(unprintable)";

    private static AcCredentials ReadCredentials(ReadOnlySpan<byte> json, Target target)
    {
        var path = target.IsSchemaOne ? SchemaOneKey : $"accounts.{Printable(target.Key)}";
        char[]? rented = null;
        try
        {
            var reader = SecretsJson.Reader(json);
            reader.Read();
            if (!MoveToTarget(ref reader, target))
            {
                throw new PlayAccountException($"{FileName}'s {path} could not be read again");
            }

            string? username = null;
            var length = -1;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("username"u8))
                {
                    reader.Read();
                    username ??= reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    reader.Skip();
                }
                else if (reader.ValueTextEquals("password"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.String)
                    {
                        throw new PlayAccountException($"{FileName}'s {path}.password is not a string");
                    }

                    if (rented is null)
                    {
                        // UTF-8 never needs more UTF-16 code units than it has bytes, escapes included.
                        rented = ArrayPool<char>.Shared.Rent(Math.Max(1, reader.ValueSpan.Length));
                        length = reader.CopyString(rented);
                    }
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (username is null)
            {
                throw new PlayAccountException($"{FileName}'s {path}.username is missing or not a string");
            }

            if (rented is null)
            {
                throw new PlayAccountException($"{FileName}'s {path}.password is missing");
            }

            try
            {
                return new AcCredentials(username, rented.AsSpan(0, length));
            }
            catch (ArgumentException e) when (e.ParamName == "password")
            {
                throw new PlayAccountException($"{FileName}'s {path}.password is empty");
            }
            catch (ArgumentException)
            {
                throw new PlayAccountException(
                    $"{FileName}'s {path}.username is not an account name ACE accepts (1 to {AcCredentials.MaxAccountLength} printable ASCII characters)");
            }
        }
        catch (JsonException e)
        {
            throw new PlayAccountException(SecretsJson.NotValidJson(FileName, e));
        }
        catch (InvalidOperationException e) when (e is not PlayAccountException)
        {
            // CopyString refuses text that is not valid UTF-8; its message is not passed on.
            throw new PlayAccountException($"{FileName}'s {path}.password is not valid UTF-8 text");
        }
        finally
        {
            if (rented is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(rented.AsSpan()));
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Moves the reader (at the root object's start) to the start of the target account's object.</summary>
    private static bool MoveToTarget(ref Utf8JsonReader reader, Target target)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (target.IsSchemaOne && reader.ValueTextEquals(SchemaOneKey))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.StartObject;
            }

            if (!target.IsSchemaOne && reader.ValueTextEquals("accounts"u8))
            {
                reader.Read();
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var isTarget = reader.ValueTextEquals(target.Key);
                    reader.Read();
                    if (isTarget)
                    {
                        return reader.TokenType == JsonTokenType.StartObject;
                    }

                    reader.Skip();
                }

                return false;
            }

            reader.Read();
            reader.Skip();
        }

        return false;
    }

    /// <summary>The account to read: a key of <c>accounts</c>, or schema 1's <c>testAccount</c>.</summary>
    private readonly record struct Target(string Key, bool IsSchemaOne);

    /// <summary>What the first pass found: everything but the passwords.</summary>
    private sealed class Layout
    {
        public int? Schema { get; set; }

        public string? DefaultAccount { get; set; }

        public bool HasAccounts { get; set; }

        public List<PlayAccountInfo> Accounts { get; } = [];

        public PlayAccountInfo? SchemaOne { get; set; }
    }
}

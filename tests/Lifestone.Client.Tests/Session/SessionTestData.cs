using System.Net;
using System.Text;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// Stand-in backend files for the session tests: secrets.json in both schemas with a sentinel password (a value no real
/// file holds, so a test can prove it reached no message, log line or recording), and pids.json with and without the
/// server mod's readiness. Nothing here is a real credential.
/// </summary>
internal static class SessionTestData
{
    /// <summary>The play account's sentinel password.</summary>
    public const string PlaySentinel = "Sentinel-PLAY-pw-1f6e0c";

    /// <summary>The test account's sentinel password.</summary>
    public const string TestSentinel = "Sentinel-TEST-pw-9a2d47";

    /// <summary>Every sentinel, for "appears nowhere" checks.</summary>
    public static readonly string[] Sentinels = [PlaySentinel, TestSentinel];

    /// <summary>A schema 2 secrets.json with the play and test accounts and one added account.</summary>
    public static byte[] SecretsV2(string defaultAccount = "play") => Encoding.UTF8.GetBytes($$"""
        {
          "schema": 2,
          "note": "stand-in for the session tests",
          "mariadb": { "host": "127.0.0.1", "port": 3316, "rootUser": "root", "rootPassword": "{{TestSentinel}}-db", "aceUser": "ace", "acePassword": "{{TestSentinel}}-ace" },
          "defaultAccount": "{{defaultAccount}}",
          "accounts": {
            "play": { "username": "lifestone", "password": "{{PlaySentinel}}" },
            "test": { "password": "{{TestSentinel}}", "username": "lifestone-test" },
            "alice": { "username": "Alice-Alt", "password": "{{PlaySentinel}}-alice" }
          }
        }
        """);

    /// <summary>A schema 2 secrets.json with the play account and an added one but no test account (the account gate refuses the test key).</summary>
    public static byte[] SecretsV2WithoutTest() => Encoding.UTF8.GetBytes($$"""
        {
          "schema": 2,
          "note": "stand-in for the session tests",
          "mariadb": { "host": "127.0.0.1", "port": 3316, "rootUser": "root", "rootPassword": "{{TestSentinel}}-db", "aceUser": "ace", "acePassword": "{{TestSentinel}}-ace" },
          "defaultAccount": "play",
          "accounts": {
            "play": { "username": "lifestone", "password": "{{PlaySentinel}}" },
            "alice": { "username": "Alice-Alt", "password": "{{PlaySentinel}}-alice" }
          }
        }
        """);

    /// <summary>A schema 1 secrets.json (setup-server.ps1 before the server-mod track): one <c>testAccount</c>.</summary>
    public static byte[] SecretsV1() => Encoding.UTF8.GetBytes($$"""
        {
          "note": "stand-in for the session tests",
          "generatedUtc": "2026-10-12T00:00:00Z",
          "mariadb": { "host": "127.0.0.1", "port": 3316, "rootUser": "root", "rootPassword": "{{TestSentinel}}-db" },
          "testAccount": { "username": "lifestone", "password": "{{PlaySentinel}}", "note": "stand-in" }
        }
        """);

    /// <summary>pids.json of a backend whose server mod is ready.</summary>
    public static byte[] PidsReady() => Encoding.UTF8.GetBytes("""
        { "mariadbd": { "pid": 11 }, "ace": { "pid": 12, "path": "ACE.Server.exe", "modReady": true, "modVersion": "0.1.0", "modWire": 1, "readyUtc": "2026-10-12T00:00:00Z" } }
        """);

    /// <summary>pids.json of an ACE started without the server mod (no <c>modReady</c>).</summary>
    public static byte[] PidsWithoutMod() => Encoding.UTF8.GetBytes("""{ "ace": { "pid": 12 } }""");

    /// <summary>The allowed endpoints of the S8 table: loopback IP literals.</summary>
    public static TheoryData<string> AllowedEndPoints => [.. AllowedById.Keys];

    /// <summary>The refused IP endpoints of the S8 table (built at run time: no non-loopback literal in the sources).</summary>
    public static TheoryData<string> RefusedIpEndPoints => [.. RefusedIpById.Keys];

    /// <summary>The allowed endpoints by id.</summary>
    public static readonly IReadOnlyDictionary<string, IPEndPoint> AllowedById = new Dictionary<string, IPEndPoint>
    {
        ["v4-loopback"] = new(IPAddress.Loopback, 9000),
        ["v4-loopback-2"] = new(new IPAddress([127, 0, 0, 2]), 9000),
        ["v4-mapped-loopback"] = new(IPAddress.Loopback.MapToIPv6(), 9000),
        ["v6-loopback"] = new(IPAddress.IPv6Loopback, 9000),
    };

    /// <summary>The refused IP endpoints by id.</summary>
    public static readonly IReadOnlyDictionary<string, IPEndPoint> RefusedIpById = new Dictionary<string, IPEndPoint>
    {
        ["test-net-1"] = new(new IPAddress([192, 0, 2, 10]), 9000),
        ["unspecified-v4"] = new(new IPAddress(0), 9000),
        ["unspecified-v6"] = new(new IPAddress(new byte[16]), 9000),
        ["mapped-public"] = new(new IPAddress([198, 51, 100, 7]).MapToIPv6(), 9000),
        ["private-10"] = new(IPAddress.Parse("10.0.0.5"), 9000),
        ["private-192"] = new(IPAddress.Parse("192.168.1.2"), 9000),
        ["broadcast"] = new(IPAddress.Broadcast, 9000),
        ["multicast"] = new(new IPAddress([224, 0, 0, 1]), 9000),
        ["v4-just-above-127"] = new(new IPAddress([128, 0, 0, 0]), 9000),
        ["v4-just-below-127"] = new(new IPAddress([126, 255, 255, 255]), 9000),
        ["v6-link-local"] = new(IPAddress.Parse("fe80::1"), 9000),
        ["v6-global"] = new(IPAddress.Parse("2001:db8::7"), 9000),
    };

    /// <summary>The refused endpoints that are host names (never resolved).</summary>
    public static readonly IReadOnlyDictionary<string, EndPoint> RefusedNameById = new Dictionary<string, EndPoint>
    {
        ["dns-localhost"] = new DnsEndPoint("localhost", 9000),
        ["dns-example"] = new DnsEndPoint("example.test", 9000),
    };

    /// <summary>The end-of-retail data-check iterations.</summary>
    public static DddIterations EndOfRetail => new(2072, 982, 994, 497);
}

/// <summary>
/// A socket factory that records every call and throws: passed where a refused endpoint must never get as far as a
/// socket (A0's S8).
/// </summary>
internal sealed class SocketFactorySpy
{
    /// <summary>Calls so far.</summary>
    public int Calls { get; private set; }

    /// <summary>The factory.</summary>
    public IDatagramSocket Open(IPEndPoint server)
    {
        Calls++;
        throw new InvalidOperationException($"the socket factory was invoked for {server}");
    }
}

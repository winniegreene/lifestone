#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace Lifestone.Net.Sockets;

/// <summary>
/// The loopback gate (CLAUDE.md, "Network"; M2 DoD 1; m2-plan §4.6): the one rule every caller uses to decide whether an
/// endpoint may be contacted. Only loopback IP literals pass: 127.0.0.0/8, ::1 and their IPv4-mapped forms. A host name
/// is never resolved (so <c>localhost</c> is refused: a resolver could answer anything), and the wildcard (unspecified)
/// addresses, private-range and public addresses are refused. Nothing here opens a socket or contacts the endpoint.
/// </summary>
/// <remarks>
/// <para>Callers: <c>Lifestone.Net.Session.AcSession.Create</c> (before any socket factory is touched), the state track's
/// <c>LoopbackGate</c> (which logs and delegates here), its <c>--server</c> option parser (<see cref="TryParse"/>), and the
/// server mod, which compile-links this file and <see cref="LoopbackRequiredException"/> to check ACE's configured host
/// (<see cref="IsPinnedConfigHost"/>). So both files depend on the base class library only, name their usings and their
/// nullable context themselves (the mod's project may set neither), refer to no other Lifestone.Net type, and format
/// nothing culture-sensitively.</para>
/// <para>Lifestone's own rule, not a retail behaviour: the retail client connects wherever the launcher points it.</para>
/// </remarks>
public static class LoopbackPolicy
{
    /// <summary>The one host ACE's <c>Server.Network.Host</c> may be configured with: the IPv4 loopback literal.</summary>
    public const string PinnedHost = "127.0.0.1";

    /// <summary>
    /// True when <paramref name="endPoint"/> may be contacted: an <see cref="IPEndPoint"/> whose address is loopback
    /// (<see cref="IsLoopback"/>). Otherwise false, with <paramref name="refusal"/> saying why in one sentence that names
    /// the endpoint. The port is not judged here.
    /// </summary>
    public static bool IsAllowed([NotNullWhen(true)] EndPoint? endPoint, [NotNullWhen(false)] out string? refusal)
    {
        switch (endPoint)
        {
            case null:
                refusal = "no endpoint was given";
                return false;
            case DnsEndPoint dns:
                refusal = $"the host name '{dns.Host}' is refused: host names are never resolved; use the IP literal {PinnedHost}";
                return false;
            case IPEndPoint ip when IsLoopback(ip.Address):
                refusal = null;
                return true;
            case IPEndPoint ip when IsUnspecified(ip.Address):
                refusal = $"{Describe(ip)} is the unspecified (wildcard) address, which means every interface; use {PinnedHost}";
                return false;
            case IPEndPoint ip:
                refusal = $"{Describe(ip)} is not a loopback address; Lifestone talks to the loopback only (the backend binds {PinnedHost})";
                return false;
            default:
                refusal = $"a {endPoint.GetType().Name} is refused; only a loopback IP endpoint is allowed ({PinnedHost} is the backend's)";
                return false;
        }
    }

    /// <summary>
    /// Returns a fresh copy of <paramref name="endPoint"/> when <see cref="IsAllowed"/> passes, with an IPv4-mapped
    /// address (<c>::ffff:127.0.0.1</c>) turned into its IPv4 form; otherwise throws
    /// <see cref="LoopbackRequiredException"/>. Use the returned endpoint, never the argument: an
    /// <see cref="IPEndPoint"/> is mutable, so the argument could change after the check.
    /// </summary>
    /// <exception cref="LoopbackRequiredException">The endpoint is not a loopback IP endpoint.</exception>
    public static IPEndPoint Require(EndPoint? endPoint)
    {
        // Copy first, then judge the copy, so no change to the argument can slip between the check and the result.
        var candidate = endPoint is IPEndPoint ip ? Copy(ip) : endPoint;
        if (!IsAllowed(candidate, out var refusal))
        {
            throw new LoopbackRequiredException(endPoint, refusal);
        }

        var allowed = (IPEndPoint)candidate;
        return allowed.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(allowed.Address.MapToIPv4(), allowed.Port) : allowed;
    }

    /// <summary>
    /// Parses <c>127.0.0.1:9000</c> or <c>[::1]:9000</c> into an endpoint that <see cref="Require"/> accepts. The text
    /// must be exactly an IP literal in its canonical form (no host names, no shorthand such as <c>127.1</c>, no
    /// whitespace, no list), a colon and a port from 1 to 65535. On failure <paramref name="error"/> says why.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out IPEndPoint? endPoint, [NotNullWhen(false)] out string? error)
    {
        endPoint = null;
        if (string.IsNullOrEmpty(text))
        {
            error = $"no server endpoint was given; expected {PinnedHost}:<port>";
            return false;
        }

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c is ',' or ';')
            {
                error = $"'{text}' is not a single endpoint (whitespace or a list); expected {PinnedHost}:<port>";
                return false;
            }
        }

        string host;
        string port;
        if (text[0] == '[')
        {
            var close = text.IndexOf("]:", StringComparison.Ordinal);
            if (close < 0)
            {
                error = $"'{text}' has no ']:<port>' after the bracketed IPv6 address";
                return false;
            }

            host = text[1..close];
            port = text[(close + 2)..];
        }
        else
        {
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || colon != text.LastIndexOf(':'))
            {
                error = colon < 0
                    ? $"'{text}' has no port; expected {PinnedHost}:<port>"
                    : $"'{text}' is ambiguous; write an IPv6 address in brackets, e.g. [::1]:9000";
                return false;
            }

            host = text[..colon];
            port = text[(colon + 1)..];
        }

        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > IPEndPoint.MaxPort)
        {
            error = $"'{text}' has no valid port (1 to {IPEndPoint.MaxPort.ToString(CultureInfo.InvariantCulture)})";
            return false;
        }

        if (!IPAddress.TryParse(host, out var address) || !string.Equals(address.ToString(), host, StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{host}' is not an IP literal in canonical form (host names are never resolved); use {PinnedHost}";
            return false;
        }

        var parsed = new IPEndPoint(address, number);
        if (!IsAllowed(parsed, out error))
        {
            return false;
        }

        endPoint = Require(parsed);
        return true;
    }

    /// <summary>
    /// True only for exactly <c>"127.0.0.1"</c>: the host ACE must be configured with. Any other spelling (a host name,
    /// <c>::1</c>, surrounding whitespace, a list, another 127.x address) is refused, because ACE falls back to every
    /// interface when it cannot parse its host.
    /// </summary>
    public static bool IsPinnedConfigHost(string? host) => string.Equals(host, PinnedHost, StringComparison.Ordinal);

    /// <summary>
    /// True for a loopback address: 127.0.0.0/8 or ::1, also in IPv4-mapped form (<c>::ffff:127.x.y.z</c>).
    /// </summary>
    public static bool IsLoopback(IPAddress? address) =>
        address is not null && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static bool IsUnspecified(IPAddress address)
    {
        var mapped = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        foreach (var b in mapped.GetAddressBytes())
        {
            if (b != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(IPEndPoint endPoint) => endPoint.ToString();

    /// <summary>A new endpoint with a new address equal to <paramref name="endPoint"/>'s.</summary>
    private static IPEndPoint Copy(IPEndPoint endPoint)
    {
        var port = endPoint.Port;
        var source = endPoint.Address;
        var bytes = source.GetAddressBytes();
        var address = bytes.Length == 4 ? new IPAddress(bytes) : new IPAddress(bytes, source.ScopeId);
        return new IPEndPoint(address, port);
    }
}

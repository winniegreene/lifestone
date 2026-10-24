using System.Net;
using Lifestone.Net.Sockets;

namespace Lifestone.Net.Tests.Sockets;

/// <summary>
/// The loopback gate at the session layer (S8; CLAUDE.md "Network"; M2 DoD 1): only loopback IP literals pass. The
/// wildcard and public vectors are built at run time (the repository may not spell a wildcard address, and allows only
/// the two private-range samples below as non-loopback literals).
/// </summary>
public sealed class LoopbackPolicyTests
{
    private static readonly IPAddress Ipv4Unspecified = new(0);
    private static readonly IPAddress Ipv6Unspecified = new(new byte[16]);
    private static readonly IPAddress PublicAddress = new([8, 8, 8, 8]);

    public static TheoryData<string> LoopbackAddresses => ["127.0.0.1", "127.5.5.5", "::1", "::ffff:127.0.0.1"];

    public static TheoryData<string> RefusedAddresses => [
        Ipv4Unspecified.ToString(),
        Ipv6Unspecified.ToString(),
        "10.0.0.5",
        "192.168.1.2",
        PublicAddress.ToString(),
        "::ffff:10.0.0.5",
        $"::ffff:{Ipv4Unspecified}",
        "fe80::1",
    ];

    [Theory]
    [MemberData(nameof(LoopbackAddresses))]
    public void Loopback_addresses_are_allowed(string address)
    {
        var endPoint = new IPEndPoint(IPAddress.Parse(address), 9000);
        Assert.True(LoopbackPolicy.IsAllowed(endPoint, out var refusal));
        Assert.Null(refusal);
        Assert.True(LoopbackPolicy.IsLoopback(endPoint.Address));
    }

    [Theory]
    [MemberData(nameof(RefusedAddresses))]
    public void Other_addresses_are_refused(string address)
    {
        var endPoint = new IPEndPoint(IPAddress.Parse(address), 9000);
        Assert.False(LoopbackPolicy.IsAllowed(endPoint, out var refusal));
        Assert.Contains(endPoint.ToString(), refusal, StringComparison.Ordinal);
        Assert.False(LoopbackPolicy.IsLoopback(endPoint.Address));

        var thrown = Assert.Throws<LoopbackRequiredException>(() => LoopbackPolicy.Require(endPoint));
        Assert.Same(endPoint, thrown.EndPoint);
        Assert.Equal(refusal, thrown.Refusal);
        Assert.Contains(endPoint.ToString(), thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_unspecified_addresses_are_named_as_wildcards()
    {
        foreach (var address in new[] { Ipv4Unspecified, Ipv6Unspecified })
        {
            Assert.False(LoopbackPolicy.IsAllowed(new IPEndPoint(address, 9000), out var refusal));
            Assert.Contains("wildcard", refusal, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Host_names_are_never_resolved_and_always_refused()
    {
        Assert.False(LoopbackPolicy.IsAllowed(new DnsEndPoint("localhost", 9000), out var refusal));
        Assert.Contains("localhost", refusal, StringComparison.Ordinal);
        Assert.Throws<LoopbackRequiredException>(() => LoopbackPolicy.Require(new DnsEndPoint("localhost", 9000)));
    }

    [Fact]
    public void No_endpoint_and_no_address_are_refused()
    {
        Assert.False(LoopbackPolicy.IsAllowed(null, out var refusal));
        Assert.NotNull(refusal);
        var thrown = Assert.Throws<LoopbackRequiredException>(() => LoopbackPolicy.Require(null));
        Assert.Null(thrown.EndPoint);
        Assert.False(LoopbackPolicy.IsLoopback(null));
    }

    [Fact]
    public void Require_returns_a_fresh_copy_that_later_changes_to_the_argument_cannot_reach()
    {
        var endPoint = new IPEndPoint(IPAddress.Loopback, 9000);
        var checkedEndPoint = LoopbackPolicy.Require(endPoint);
        Assert.NotSame(endPoint, checkedEndPoint);
        Assert.NotSame(endPoint.Address, checkedEndPoint.Address);

        endPoint.Address = IPAddress.Parse("10.0.0.5");
        endPoint.Port = 1;
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), checkedEndPoint);
    }

    [Fact]
    public void Require_turns_an_ipv4_mapped_loopback_address_into_ipv4()
    {
        var checkedEndPoint = LoopbackPolicy.Require(new IPEndPoint(IPAddress.Parse("::ffff:127.0.0.1"), 9000));
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), checkedEndPoint);
        Assert.Equal(new IPEndPoint(IPAddress.IPv6Loopback, 9000), LoopbackPolicy.Require(new IPEndPoint(IPAddress.IPv6Loopback, 9000)));
    }

    [Theory]
    [InlineData("127.0.0.1:9000", "127.0.0.1:9000")]
    [InlineData("127.5.5.5:1", "127.5.5.5:1")]
    [InlineData("127.0.0.1:65535", "127.0.0.1:65535")]
    [InlineData("[::1]:9000", "[::1]:9000")]
    [InlineData("[::ffff:127.0.0.1]:9000", "127.0.0.1:9000")]
    public void TryParse_accepts_loopback_literals_with_a_port(string text, string expected)
    {
        Assert.True(LoopbackPolicy.TryParse(text, out var endPoint, out var error), error);
        Assert.Equal(expected, endPoint.ToString());
    }

    public static TheoryData<string?> RefusedTexts => [
        null,
        "",
        "localhost:9000",
        "127.0.0.1",
        "127.0.0.1:0",
        "127.0.0.1:65536",
        "127.0.0.1:-1",
        "127.0.0.1:+9000",
        "127.0.0.1:9000x",
        " 127.0.0.1:9000",
        "127.0.0.1:9000 ",
        "127.0.0.1,10.0.0.5",
        "127.0.0.1:9000,10.0.0.5:9000",
        "127.0.0.1:9000;192.168.1.2:9000",
        "127.1:9000",
        "127.000.000.001:9000",
        "2130706433:9000",
        "::1:9000",
        "[::1]",
        "[::1]9000",
        "10.0.0.5:9000",
        "192.168.1.2:9000",
        $"{PublicAddress}:9000",
        $"{Ipv4Unspecified}:9000",
        $"[{Ipv6Unspecified}]:9000",
    ];

    [Theory]
    [MemberData(nameof(RefusedTexts))]
    public void TryParse_refuses_names_lists_shorthand_bad_ports_and_other_addresses(string? text)
    {
        Assert.False(LoopbackPolicy.TryParse(text, out var endPoint, out var error));
        Assert.Null(endPoint);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Only_the_exact_ipv4_loopback_literal_is_the_pinned_config_host()
    {
        Assert.True(LoopbackPolicy.IsPinnedConfigHost("127.0.0.1"));
        Assert.Equal("127.0.0.1", LoopbackPolicy.PinnedHost);

        string?[] refused =
        [
            null, "", " 127.0.0.1", "127.0.0.1 ", "::1", "[::1]", "localhost", "LOCALHOST", "127.0.0.2", "127.000.000.001",
            "127.1", "127.0.0.1,10.0.0.5", "127.0.0.1:9000", "192.168.1.2", Ipv4Unspecified.ToString(), Ipv6Unspecified.ToString(),
        ];
        foreach (var host in refused)
        {
            Assert.False(LoopbackPolicy.IsPinnedConfigHost(host), host ?? "(null)");
        }
    }
}

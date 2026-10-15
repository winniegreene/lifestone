using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Net.Sockets;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The session layer's loopback gate (ST2): only loopback IP literals pass, a host name is never resolved, every refusal
/// logs one WARNING naming the runner and the reason, and the decision is always net's <see cref="LoopbackPolicy"/>.
/// </summary>
public sealed class LoopbackGateTests
{
    public static TheoryData<string> Allowed => [.. SessionTestData.AllowedById.Keys];

    public static TheoryData<string> Refused => [.. SessionTestData.RefusedIpById.Keys, .. SessionTestData.RefusedNameById.Keys];

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Gate_allows_loopback_ip_literals_without_a_log_line(string id)
    {
        var log = new RecordingLogSink();
        var endPoint = SessionTestData.AllowedById[id];
        var allowed = LoopbackGate.Require(endPoint, "test runner", log);

        Assert.True(LoopbackPolicy.IsLoopback(allowed.Address));
        Assert.False(allowed.Address.IsIPv4MappedToIPv6);
        Assert.Equal(endPoint.Port, allowed.Port);
        Assert.NotSame(endPoint, allowed);
        Assert.Empty(log.Entries);
        Assert.True(LoopbackGate.TryRequire(endPoint, "test runner", log, out var again, out var refusal));
        Assert.Equal(allowed, again);
        Assert.Null(refusal);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void Gate_refuses_everything_else_with_one_warning_naming_the_runner(string id)
    {
        var log = new RecordingLogSink();
        var endPoint = EndPointOf(id);
        var e = Assert.Throws<LoopbackRequiredException>(() => LoopbackGate.Require(endPoint, "HeadlessRunner", log));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, entry.Severity);
        Assert.Equal("session", entry.Category);
        Assert.Contains("HeadlessRunner", entry.Message, StringComparison.Ordinal);
        Assert.Contains(e.Refusal, entry.Message, StringComparison.Ordinal);
        Assert.Equal(new LogField("runner", "HeadlessRunner"), Assert.Single(entry.Fields));

        Assert.False(LoopbackGate.TryRequire(endPoint, "PlayerTools.Place", log, out var allowed, out var refusal));
        Assert.Null(allowed);
        Assert.Equal(e.Refusal, refusal);
        Assert.Equal(2, log.Entries.Count);
        Assert.Contains("PlayerTools.Place", log.Entries[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_never_resolves_host_names()
    {
        // "localhost" would resolve to loopback, yet it is refused: a resolver could answer anything.
        var log = new RecordingLogSink();
        var e = Assert.Throws<LoopbackRequiredException>(() => LoopbackGate.Require(new DnsEndPoint("localhost", 9000), "test runner", log));
        Assert.Contains("never resolved", e.Refusal, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", e.Refusal, StringComparison.Ordinal);
        Assert.Throws<LoopbackRequiredException>(() => LoopbackGate.Require(null, "test runner", log));
    }

    [Fact]
    public void Gate_and_net_policy_agree()
    {
        // The gate only adds the log line; the decision is net's, so the two cannot drift.
        var log = new RecordingLogSink();
        foreach (var endPoint in SessionTestData.AllowedById.Values.Concat<EndPoint>(SessionTestData.RefusedIpById.Values).Concat(SessionTestData.RefusedNameById.Values))
        {
            var policy = LoopbackPolicy.IsAllowed(endPoint, out var policyRefusal);
            Assert.Equal(policy, LoopbackGate.IsAllowed(endPoint, out var gateRefusal));
            Assert.Equal(policyRefusal, gateRefusal);
            Assert.Equal(policy, LoopbackGate.TryRequire(endPoint, "agreement", log, out _, out var tryRefusal));
            Assert.Equal(policyRefusal, tryRefusal);
        }

        Assert.Equal(SessionTestData.RefusedIpById.Count + SessionTestData.RefusedNameById.Count, log.Entries.Count);
    }

    [Fact]
    public void The_gate_needs_a_runner_name_and_a_log()
    {
        Assert.ThrowsAny<ArgumentException>(() => LoopbackGate.Require(SessionTestData.AllowedById["v4-loopback"], " ", NullLogSink.Instance));
        Assert.Throws<ArgumentNullException>(() => LoopbackGate.Require(SessionTestData.AllowedById["v4-loopback"], "runner", null!));
    }

    internal static EndPoint EndPointOf(string id) =>
        SessionTestData.AllowedById.TryGetValue(id, out var allowed) ? allowed
        : SessionTestData.RefusedIpById.TryGetValue(id, out var refused) ? refused
        : SessionTestData.RefusedNameById[id];
}

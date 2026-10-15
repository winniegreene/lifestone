using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Credentials;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The mod-ready gate (ST5; owner decision D11): only pids.json's <c>ace.modReady</c> as the JSON literal <c>true</c>
/// lets a live session start; anything else refuses with a reason that names the remedy.
/// </summary>
public sealed class BackendReadinessTests
{
    [Fact]
    public void ModReady_true_passes_and_reports_the_mod()
    {
        Assert.True(BackendReadiness.ModReady(SessionTestData.PidsReady(), out var reason));
        Assert.Contains("version 0.1.0, wire 1", reason, StringComparison.Ordinal);
        var report = BackendReadiness.Read(SessionTestData.PidsReady());
        Assert.Equal(new BackendReadinessReport(true, "0.1.0", 1, reason), report);

        var log = new RecordingLogSink();
        Assert.Equal(report, BackendReadiness.Require(SessionTestData.PidsReady(), "runner", log));
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("", "missing or empty")]
    [InlineData("﻿ \r\n", "missing or empty")]
    [InlineData("[1]", "not a JSON object")]
    [InlineData("{ \"ace\": ", "not valid JSON (line 1")]
    [InlineData("{ \"mariadbd\": { \"pid\": 11 } }", "has no 'ace' entry")]
    [InlineData("{ \"ace\": 12 }", "'ace' entry is not an object")]
    [InlineData("{ \"ace\": { \"pid\": 12 } }", "has no ace.modReady")]
    [InlineData("{ \"ace\": { \"pid\": 12, \"modReady\": false } }", "ace.modReady is not true")]
    [InlineData("{ \"ace\": { \"pid\": 12, \"modReady\": \"true\" } }", "ace.modReady is not true")]
    [InlineData("{ \"ace\": { \"pid\": 12, \"modReady\": 1 } }", "ace.modReady is not true")]
    [InlineData("{ \"ace\": { \"pid\": 12, \"modReady\": null } }", "ace.modReady is not true")]
    public void ModReady_false_or_missing_refuses_with_reason(string pids, string expected)
    {
        var bytes = Encoding.UTF8.GetBytes(pids);
        Assert.False(BackendReadiness.ModReady(bytes, out var reason));
        Assert.Contains(expected, reason, StringComparison.Ordinal);
        Assert.Contains(".ps1", reason, StringComparison.Ordinal);

        var log = new RecordingLogSink();
        var e = Assert.Throws<BackendNotReadyException>(() => BackendReadiness.Require(bytes, "HeadlessRunner", log));
        Assert.Equal(reason, e.Reason);
        Assert.StartsWith("Refused to log in: ", e.Message, StringComparison.Ordinal);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.Contains("HeadlessRunner", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mod_that_is_not_ready_names_the_admin_risk()
    {
        Assert.False(BackendReadiness.ModReady(SessionTestData.PidsWithoutMod(), out var reason));
        Assert.Contains("Admin", reason, StringComparison.Ordinal);
        Assert.Contains("start-server.ps1", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_entries_and_fields_are_ignored()
    {
        var pids = Encoding.UTF8.GetBytes("""{ "x": [1, {"ace": 1}], "ace": { "nested": { "modReady": false }, "modReady": true, "extra": [true] } }""");
        Assert.True(BackendReadiness.ModReady(pids, out _));
    }
}

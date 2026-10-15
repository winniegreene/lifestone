using Xunit.Abstractions;
using Xunit.Sdk;

namespace Lifestone.Tests.Shared.Loopback;

/// <summary>The two halves of the loopback gate the attributes and test cases apply.</summary>
public static class LoopbackRun
{
    /// <summary>How a skip reason from <see cref="BackendProbe"/> starts.</summary>
    public const string NotReadyPrefix = "Loopback backend not ready: ";

    /// <summary>How a skip reason from <see cref="LoopbackLock"/> starts.</summary>
    public const string NoLockPrefix = "Loopback lock not taken: ";

    /// <summary>The skip reason when tests are discovered: the process's first <see cref="BackendProbe"/> (no lock taken), or null when ready.</summary>
    public static string? SkipReasonAtDiscovery() =>
        BackendProbe.AtDiscovery is { IsReady: false } probe ? NotReadyPrefix + probe.Reason : null;

    /// <summary>The prefix of the skip reason once the run was aborted (<see cref="Abort"/>).</summary>
    public const string AbortedPrefix = "Loopback run aborted: ";

    private static volatile string? _abortReason;

    /// <summary>Why the run was aborted, or null.</summary>
    public static string? AbortReason => _abortReason;

    /// <summary>
    /// Aborts the loopback run: every loopback test that has not started yet is skipped with <paramref name="reason"/> (the first call wins). The N11
    /// status test calls it when the test account is not a plain Player or ACE's first-account auto-promotion is on (owner decision D11), so no later test
    /// logs in to a server in that state. The failing test itself stays red.
    /// </summary>
    /// <param name="reason">Why; no secret and no message body.</param>
    public static void Abort(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _abortReason ??= reason;
    }

    /// <summary>
    /// The skip reason just before a loopback test runs: null when this process holds the <see cref="LoopbackLock"/>
    /// (taken here on the first call; it may wait) and a fresh <see cref="BackendProbe"/> says the backend is ready.
    /// </summary>
    public static string? SkipReasonNow()
    {
        if (_abortReason is { } aborted)
        {
            return AbortedPrefix + aborted;
        }

        if (LoopbackLock.EnsureHeldByThisProcess() is { } noLock)
        {
            return NoLockPrefix + noLock;
        }

        var probe = BackendProbe.Probe();
        return probe.IsReady ? null : NotReadyPrefix + probe.Reason;
    }
}

/// <summary>
/// A loopback [Fact], or one pre-enumerated row of a loopback [Theory]: unless already skipped at discovery, it takes
/// the loopback lock and re-probes the backend just before it runs, and is reported as skipped when either fails.
/// </summary>
public sealed class LoopbackTestCase : XunitTestCase
{
    /// <summary>For the xUnit de-serializer only.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public LoopbackTestCase()
    {
    }

    /// <summary>A loopback test case for <paramref name="testMethod"/> (with the data row, for a theory row).</summary>
    public LoopbackTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
    {
        if (SkipReason is null && LoopbackRun.SkipReasonNow() is { } skip)
        {
            return new XunitTestCaseRunner(this, DisplayName, skip, constructorArguments, TestMethodArguments, messageBus, aggregator, cancellationTokenSource).RunAsync();
        }

        return base.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator, cancellationTokenSource);
    }
}

/// <summary>A loopback [Theory] whose data rows are enumerated when it runs; gated like <see cref="LoopbackTestCase"/>.</summary>
public sealed class LoopbackTheoryTestCase : XunitTheoryTestCase
{
    /// <summary>For the xUnit de-serializer only.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public LoopbackTheoryTestCase()
    {
    }

    /// <summary>A loopback theory test case for <paramref name="testMethod"/>.</summary>
    public LoopbackTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
    }

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
    {
        if (SkipReason is null && LoopbackRun.SkipReasonNow() is { } skip)
        {
            return new XunitTheoryTestCaseRunner(this, DisplayName, skip, constructorArguments, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource).RunAsync();
        }

        return base.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator, cancellationTokenSource);
    }
}

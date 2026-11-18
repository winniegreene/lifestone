using Lifestone.Data.Dat;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 3 F1 (review 3-R4): the test project runs xunit 2.9.3, which cannot skip a test from inside its body, so a check that finds a named gap and returns passes silently. Throwing <see cref="DynamicSkipException"/> from a
/// <see cref="DatsGapTheoryAttribute"/> test reports it as SKIPPED with the reason (the TODO and the measured numbers). No package is added: this is the message-bus trick of the usual skippable-fact helpers, in 60 lines.
/// </summary>
public sealed class DynamicSkipException(string reason) : Exception(reason)
{
    /// <summary>Skip the running test with <paramref name="reason"/>.</summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static void Skip(string reason) => throw new DynamicSkipException(reason);
}

/// <summary>A theory that needs the retail dats (skipped without them, as <c>RequiresDatsTheory</c>) and may skip a row from its body with <see cref="DynamicSkipException.Skip"/>.</summary>
[XunitTestCaseDiscoverer("Lifestone.Client.Tests.Session.Grounds.DatsGapTheoryDiscoverer", "Lifestone.Client.Tests")]
public sealed class DatsGapTheoryAttribute : TheoryAttribute
{
    /// <summary>Skipped without the dats.</summary>
    public DatsGapTheoryAttribute()
    {
        if (!DatSet.Exists())
        {
            Skip = $"Retail dats not found in '{DatSet.ResolveDirectory()}' (set {DatSet.DirectoryEnvironmentVariable}).";
        }
    }
}

/// <summary>Finds a <see cref="DatsGapTheoryAttribute"/>'s test cases as the stock theory discoverer does, with cases that turn a dynamic skip into a skip.</summary>
public sealed class DatsGapTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    /// <inheritdoc />
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute) =>
        [new DatsGapTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod)];

    /// <inheritdoc />
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow) =>
        [new DatsGapRowTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow)];
}

/// <summary>A pre-enumerated row case that reports a <see cref="DynamicSkipException"/> as skipped.</summary>
public sealed class DatsGapRowTestCase : XunitTestCase
{
    /// <summary>For the deserialiser.</summary>
    [Obsolete("Called by the xunit deserialiser", error: true)]
    public DatsGapRowTestCase()
    {
    }

    /// <summary>A row case.</summary>
    public DatsGapRowTestCase(IMessageSink sink, TestMethodDisplay display, TestMethodDisplayOptions options, ITestMethod method, object[] row)
        : base(sink, display, options, method, row)
    {
    }

    /// <inheritdoc />
    public override async Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        await DynamicSkipBus.RunAsync(messageBus, bus => base.RunAsync(diagnosticMessageSink, bus, constructorArguments, aggregator, cancellationTokenSource));
}

/// <summary>A theory case that reports a <see cref="DynamicSkipException"/> of any row as skipped.</summary>
public sealed class DatsGapTheoryTestCase : XunitTheoryTestCase
{
    /// <summary>For the deserialiser.</summary>
    [Obsolete("Called by the xunit deserialiser", error: true)]
    public DatsGapTheoryTestCase()
    {
    }

    /// <summary>A theory case.</summary>
    public DatsGapTheoryTestCase(IMessageSink sink, TestMethodDisplay display, TestMethodDisplayOptions options, ITestMethod method)
        : base(sink, display, options, method)
    {
    }

    /// <inheritdoc />
    public override async Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        await DynamicSkipBus.RunAsync(messageBus, bus => base.RunAsync(diagnosticMessageSink, bus, constructorArguments, aggregator, cancellationTokenSource));
}

/// <summary>The message bus that turns a failed test whose exception is a <see cref="DynamicSkipException"/> into a skipped one.</summary>
internal sealed class DynamicSkipBus(IMessageBus inner) : IMessageBus
{
    private int _skipped;

    public static async Task<RunSummary> RunAsync(IMessageBus messageBus, Func<IMessageBus, Task<RunSummary>> run)
    {
        using var bus = new DynamicSkipBus(messageBus);
        var summary = await run(bus);
        if (bus._skipped > 0)
        {
            summary.Failed -= bus._skipped;
            summary.Skipped += bus._skipped;
        }

        return summary;
    }

    public bool QueueMessage(IMessageSinkMessage message)
    {
        if (message is ITestFailed failed && failed.ExceptionTypes.FirstOrDefault() == typeof(DynamicSkipException).FullName)
        {
            _skipped++;
            return inner.QueueMessage(new TestSkipped(failed.Test, failed.Messages.FirstOrDefault() ?? "dynamic skip"));
        }

        return inner.QueueMessage(message);
    }

    public void Dispose()
    {
    }
}

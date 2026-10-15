using Xunit.Abstractions;
using Xunit.Sdk;

namespace Lifestone.Tests.Shared.Loopback;

/// <summary>
/// The [Theory] counterpart of <see cref="LoopbackFactAttribute"/>: skipped, with the reason, unless the localhost
/// backend is ready when tests are discovered and again (after the <see cref="LoopbackLock"/> is taken) when it runs.
/// </summary>
[XunitTestCaseDiscoverer("Lifestone.Tests.Shared.Loopback.LoopbackTheoryDiscoverer", "Lifestone.Loopback.Tests")]
public sealed class LoopbackTheoryAttribute : TheoryAttribute
{
    public LoopbackTheoryAttribute()
    {
        Skip = LoopbackRun.SkipReasonAtDiscovery();
    }
}

/// <summary>
/// Discovers <see cref="LoopbackTheoryAttribute"/> tests: each pre-enumerated data row as a <see cref="LoopbackTestCase"/>,
/// a theory whose rows are enumerated at run time as a <see cref="LoopbackTheoryTestCase"/>; both re-check the gate
/// when they run.
/// </summary>
public sealed class LoopbackTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    /// <inheritdoc />
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow) =>
        [new LoopbackTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow)];

    /// <inheritdoc />
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute) =>
        [new LoopbackTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod)];
}

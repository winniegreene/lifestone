using Xunit.Abstractions;
using Xunit.Sdk;

namespace Lifestone.Tests.Shared.Loopback;

/// <summary>
/// A [Fact] that talks to the localhost backend (ACE with the Lifestone server mod on 127.0.0.1). It is reported as
/// skipped, with the reason, unless the backend is ready (<see cref="BackendProbe"/>): once when tests are discovered,
/// and again just before it runs, after this process has taken the <see cref="LoopbackLock"/> (so a backend stopped in
/// between, or another run that keeps the lock for too long, also skips it instead of failing).
/// </summary>
/// <remarks>Its class carries <c>[Collection(LoopbackCollection.Name)]</c> and <c>[Trait("Category", "Loopback")]</c>
/// and lives in Lifestone.Loopback.Tests, the only project that compiles tests/Shared/Loopback.</remarks>
[XunitTestCaseDiscoverer("Lifestone.Tests.Shared.Loopback.LoopbackFactDiscoverer", "Lifestone.Loopback.Tests")]
public sealed class LoopbackFactAttribute : FactAttribute
{
    public LoopbackFactAttribute()
    {
        Skip = LoopbackRun.SkipReasonAtDiscovery();
    }
}

/// <summary>Discovers <see cref="LoopbackFactAttribute"/> tests as <see cref="LoopbackTestCase"/>s, which re-check the gate when they run.</summary>
public sealed class LoopbackFactDiscoverer(IMessageSink diagnosticMessageSink) : FactDiscoverer(diagnosticMessageSink)
{
    /// <inheritdoc />
    protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute) =>
        new LoopbackTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
}

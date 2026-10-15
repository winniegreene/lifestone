using Xunit.Abstractions;
using Xunit.Sdk;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>The position of a live test in its class: the live net tests run in step order, and the first checks the account the others depend on.</summary>
/// <param name="step">The step, from 1.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveStepAttribute(int step) : Attribute
{
    /// <summary>The step, from 1.</summary>
    public int Step { get; } = step;
}

/// <summary>Orders a class's test cases by <see cref="LiveStepAttribute"/>, the ones without a step last, then by method name.</summary>
public sealed class LiveStepOrderer : ITestCaseOrderer
{
    /// <inheritdoc/>
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(StepOf).ThenBy(t => t.TestMethod.Method.Name, StringComparer.Ordinal);

    private static int StepOf<TTestCase>(TTestCase testCase)
        where TTestCase : ITestCase =>
        testCase.TestMethod.Method.GetCustomAttributes(typeof(LiveStepAttribute)).FirstOrDefault()?.GetConstructorArguments().OfType<int>().FirstOrDefault() ?? int.MaxValue;
}

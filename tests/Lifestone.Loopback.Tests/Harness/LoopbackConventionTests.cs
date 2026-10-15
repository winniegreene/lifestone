using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lifestone.Client.Tests;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Harness;

/// <summary>
/// The loopback conventions, enforced: every <see cref="LoopbackFactAttribute"/> or <see cref="LoopbackTheoryAttribute"/>
/// test carries <c>[Trait("Category", "Loopback")]</c> (so default runs can exclude it) and sits in the serialised
/// <see cref="LoopbackCollection"/>; no other test claims that category or collection (it would run against the backend
/// ungated); no loopback class or collection has an xUnit fixture (it would run before the gate); and loopback tests
/// live in this project only.
/// </summary>
public sealed partial class LoopbackConventionTests
{
    private const BindingFlags AllDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [GeneratedRegex(@"\[\s*Loopback(Fact|Theory)\b|Collection\(\s*""Loopback""\s*\)|Trait\(\s*""Category""\s*,\s*""Loopback""\s*\)")]
    private static partial Regex LoopbackMark();

    [Fact]
    public void Every_loopback_test_is_marked_and_serialised()
    {
        var loopbackTests = 0;
        var problems = new List<string>();
        foreach (var type in typeof(LoopbackConventionTests).Assembly.GetTypes().Where(t => !IsConventionSample(t)))
        {
            problems.AddRange(FixtureProblems(type));
            foreach (var method in type.GetMethods(AllDeclared))
            {
                var name = $"{type.FullName}.{method.Name}";
                var gated = IsGated(method);
                var category = HasLoopbackCategory(method) || HasLoopbackCategory(type);
                var collection = IsInLoopbackCollection(type);
                if (gated)
                {
                    loopbackTests++;
                    if (!category)
                    {
                        problems.Add($"{name}: no [Trait(\"Category\", \"Loopback\")] on the test or its class");
                    }

                    if (!collection)
                    {
                        problems.Add($"{name}: its class has no [Collection(LoopbackCollection.Name)]");
                    }
                }
                else if (method.IsDefined(typeof(FactAttribute), true) && (category || collection))
                {
                    problems.Add($"{name}: a test in the Loopback category or collection must be a [LoopbackFact] or [LoopbackTheory], which skip unless the backend is ready");
                }
            }
        }

        Assert.True(loopbackTests > 0, "no [LoopbackFact] or [LoopbackTheory] test found: the scan would pass vacuously");
        Assert.True(problems.Count == 0, "Loopback convention broken:\n  " + string.Join("\n  ", problems));
    }

    /// <summary>
    /// The fixture rule catches a class fixture however the class is marked as a loopback class: by the collection, by
    /// the category, or only by its gated tests.
    /// </summary>
    [Fact]
    public void A_fixture_on_a_loopback_class_is_reported()
    {
        foreach (var type in new[] { typeof(ConventionSamples.InTheCollection), typeof(ConventionSamples.InTheCategory), typeof(ConventionSamples.WithGatedTests) })
        {
            var problem = Assert.Single(FixtureProblems(type));
            Assert.Contains($"IClassFixture<{nameof(ConventionSamples.SharedLogin)}>", problem, StringComparison.Ordinal);
            Assert.Contains("before the loopback gate", problem, StringComparison.Ordinal);
        }

        Assert.Empty(FixtureProblems(typeof(ConventionSamples.NotLoopback)));
        Assert.Empty(FixtureProblems(typeof(LoopbackCollection)));
    }

    [Fact]
    public void Loopback_tests_live_only_in_this_project()
    {
        var offenders = Directory.EnumerateFiles(RepositoryFiles.PathOf("tests"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepositoryFiles.Root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
            .Where(f => !f.StartsWith("tests/Lifestone.Loopback.Tests/", StringComparison.Ordinal) && !f.StartsWith("tests/Shared/Loopback/", StringComparison.Ordinal))
            .Where(f => f.EndsWith(".csproj", StringComparison.Ordinal)
                ? XDocument.Load(RepositoryFiles.PathOf(f)).Descendants("Compile").Any(c => ((string?)c.Attribute("Include") ?? string.Empty).Replace('/', '\\').Contains(@"Shared\Loopback", StringComparison.OrdinalIgnoreCase))
                : LoopbackMark().IsMatch(File.ReadAllText(RepositoryFiles.PathOf(f))))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(offenders.Length == 0, "Loopback tests and the tests/Shared/Loopback gate belong to tests/Lifestone.Loopback.Tests only:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// A loopback class may not implement <see cref="IClassFixture{TFixture}"/>, and the Loopback collection definition
    /// may not implement <see cref="ICollectionFixture{TFixture}"/>. In xUnit 2.9.3 the test class and collection runners
    /// build those fixtures before any test case's <c>RunAsync</c>, where the gate is (<see cref="LoopbackRun.SkipReasonNow"/>),
    /// and even when every test of the class skips: a fixture that logs in would hold no loopback lock (two worktrees
    /// could log the one test account in at once and boot each other), skip the fresh backend probe, and touch the
    /// backend and secrets when the backend was not ready. Shared per-class state is created lazily from inside a test
    /// body, after the gate.
    /// </summary>
    private static IEnumerable<string> FixtureProblems(Type type)
    {
        var loopbackClass = HasLoopbackCategory(type)
            || IsInLoopbackCollection(type)
            || type.GetMethods(AllDeclared).Any(m => IsGated(m) || HasLoopbackCategory(m));
        var loopbackDefinition = type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(CollectionDefinitionAttribute)
            && a.ConstructorArguments.Count >= 1
            && Equals(a.ConstructorArguments[0].Value, LoopbackCollection.Name));
        foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
        {
            var definition = contract.GetGenericTypeDefinition();
            if ((loopbackClass && definition == typeof(IClassFixture<>)) || (loopbackDefinition && definition == typeof(ICollectionFixture<>)))
            {
                var kind = definition == typeof(IClassFixture<>) ? "IClassFixture" : "ICollectionFixture";
                yield return $"{type.FullName}: implements {kind}<{contract.GetGenericArguments()[0].Name}>, which xUnit builds before the loopback gate " +
                             "(no lock, no fresh backend probe, even when every test skips); create shared state lazily inside a test body instead";
            }
        }
    }

    private static bool IsGated(MethodInfo method) =>
        method.IsDefined(typeof(LoopbackFactAttribute), false) || method.IsDefined(typeof(LoopbackTheoryAttribute), false);

    private static bool HasLoopbackCategory(MemberInfo member) =>
        member.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(TraitAttribute)
            && a.ConstructorArguments.Count == 2
            && Equals(a.ConstructorArguments[0].Value, "Category")
            && Equals(a.ConstructorArguments[1].Value, LoopbackCollection.Category));

    private static bool IsInLoopbackCollection(Type type) =>
        type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(CollectionAttribute)
            && a.ConstructorArguments.Count == 1
            && Equals(a.ConstructorArguments[0].Value, LoopbackCollection.Name));

    private static bool IsConventionSample(Type type)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            if (t == typeof(ConventionSamples))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Deliberately broken classes for <see cref="A_fixture_on_a_loopback_class_is_reported"/>, left out of the
    /// assembly scan. xUnit never discovers them (it looks at exported types only, and these are nested in a private
    /// class), so it never runs their tests or builds their fixtures.
    /// </summary>
    private static class ConventionSamples
    {
        /// <summary>The fixture a careless loopback class might use to log in once.</summary>
        public sealed class SharedLogin;

        [Collection(LoopbackCollection.Name)]
        public sealed class InTheCollection : IClassFixture<SharedLogin>;

        [Trait("Category", LoopbackCollection.Category)]
        public sealed class InTheCategory : IClassFixture<SharedLogin>;

        public sealed class WithGatedTests : IClassFixture<SharedLogin>
        {
            [LoopbackFact]
            private static void Logs_in() => throw new InvalidOperationException("a convention sample; never run");
        }

        public sealed class NotLoopback : IClassFixture<SharedLogin>;
    }
}

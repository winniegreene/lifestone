using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lifestone.Client.Session.Credentials;
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
    public void P4_rfixb_R13_the_process_exit_log_off_line_names_the_character_and_the_seconds()
    {
        Assert.Equal("[live-session] logged Holtburg Brawler off at process exit (1.5 s)", Session.LiveCharacter.ProcessExitLine("Holtburg Brawler", 1.52));
    }

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
    /// M4 D31: every class in the Loopback collection carries exactly one <c>[Trait("Account", ...)]</c>, one of the four test accounts, and it is the account of
    /// every character the class names with <see cref="LoopbackCharacterAttribute"/> (on the class or a method; a class that names none uses <c>test</c>). The lock per
    /// account, and the gate's lanes, read that account. A <c>Lane</c> trait may only be <c>solo</c> (the gate runs those alone, after the parallel phase).
    /// </summary>
    [Fact]
    public void Every_loopback_class_names_its_one_account_and_its_characters_live_on_it()
    {
        var checkedClasses = 0;
        var problems = new List<string>();
        foreach (var type in typeof(LoopbackConventionTests).Assembly.GetTypes().Where(t => !IsConventionSample(t) && IsInLoopbackCollection(t)))
        {
            checkedClasses++;
            var accounts = TraitValues(type, "Account");
            if (accounts.Count != 1)
            {
                problems.Add($"{type.FullName}: {accounts.Count} [Trait(\"Account\", ...)] attributes, exactly one is required (one of {string.Join(", ", TestAccounts.Keys)})");
                continue;
            }

            var account = accounts[0];
            if (!TestAccounts.Keys.Contains(account))
            {
                problems.Add($"{type.FullName}: Account '{account}' is not a test account ({string.Join(", ", TestAccounts.Keys)})");
            }

            var characters = CharacterNames(type).Concat(type.GetMethods(AllDeclared).SelectMany(CharacterNames)).Distinct().ToList();
            foreach (var character in characters.Count == 0 ? [null] : characters.Cast<string?>())
            {
                var key = LoopbackAccounts.For(character);
                if (key != account)
                {
                    problems.Add($"{type.FullName}: Account '{account}' but {character ?? "(no [LoopbackCharacter]: the test account)"} lives on '{key}'");
                }
            }

            foreach (var lane in TraitValues(type, "Lane").Concat(type.GetMethods(AllDeclared).SelectMany(m => TraitValues(m, "Lane"))))
            {
                if (lane != "solo")
                {
                    problems.Add($"{type.FullName}: Lane '{lane}' is not 'solo'");
                }
            }
        }

        Assert.True(checkedClasses > 0, "no class in the Loopback collection found: the scan would pass vacuously");
        Assert.True(problems.Count == 0, "Loopback account convention broken:\n  " + string.Join("\n  ", problems));
    }

    /// <summary>The convention above, on samples: a class with two accounts, a character on another account than the class's, and a class with none.</summary>
    [Fact]
    public void A_wrong_or_missing_account_trait_is_seen()
    {
        Assert.Equal(["test", "test-melee"], TraitValues(typeof(ConventionSamples.TwoAccounts), "Account"));
        Assert.Equal(["Holtburg Brawler"], CharacterNames(typeof(ConventionSamples.BrawlerOnTest)));
        Assert.Equal("test-melee", LoopbackAccounts.For("Holtburg Brawler"));
        Assert.Equal("test-tools", LoopbackAccounts.For("Lifestone Throwaway")); // M4 D39: the throwaway classes carry Account test-tools and [LoopbackCharacter("Lifestone Throwaway")]
        Assert.Empty(TraitValues(typeof(ConventionSamples.InTheCollection), "Account"));
    }

    private static List<string> TraitValues(MemberInfo member, string name) =>
        member.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(TraitAttribute) && a.ConstructorArguments.Count == 2 && Equals(a.ConstructorArguments[0].Value, name))
            .Select(a => (string)a.ConstructorArguments[1].Value!)
            .ToList();

    private static List<string> CharacterNames(MemberInfo member) =>
        member.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(LoopbackCharacterAttribute))
            .Select(a => (string)a.ConstructorArguments[0].Value!)
            .ToList();

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

        [Collection(LoopbackCollection.Name)]
        [Trait("Account", "test")]
        [Trait("Account", "test-melee")]
        public sealed class TwoAccounts;

        [Collection(LoopbackCollection.Name)]
        [Trait("Account", "test")]
        [LoopbackCharacter("Holtburg Brawler")]
        public sealed class BrawlerOnTest;
    }
}

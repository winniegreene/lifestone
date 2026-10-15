namespace Lifestone.Tests.Shared.Loopback;

/// <summary>
/// The xUnit collection of every loopback test class. A loopback test class carries
/// <c>[Collection(LoopbackCollection.Name)]</c> and <c>[Trait("Category", "Loopback")]</c>, and its tests use
/// <see cref="LoopbackFactAttribute"/> or <see cref="LoopbackTheoryAttribute"/>; a reflection test in
/// Lifestone.Loopback.Tests enforces all three. The collection runs alone and one test at a time (the backend is one
/// shared process with one test account); <see cref="LoopbackLock"/> does the same across processes and worktrees.
/// </summary>
/// <remarks>
/// <para>Conventions for the loopback tests: default runs exclude them with the filter <c>Category!=Loopback</c>, and
/// long ones (the lossy soak) also carry <c>[Trait("Tier", "Soak")]</c>, which default runs exclude too.</para>
/// <para><b>No fixtures.</b> The gate (lock, then a fresh <see cref="BackendProbe"/>) runs as each test case starts
/// (<see cref="LoopbackRun.SkipReasonNow"/>), and xUnit builds class and collection fixtures before that, even for a
/// class whose tests all skip. So neither this collection nor a loopback class has a fixture (the reflection test
/// enforces it): shared per-class state, such as one login for several tests, is created lazily from inside a test
/// body, after the gate, never in a fixture constructor.</para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LoopbackCollection
{
    /// <summary>The collection's name, as <c>[Collection]</c> spells it.</summary>
    public const string Name = "Loopback";

    /// <summary>The <c>Category</c> trait value every loopback test carries.</summary>
    public const string Category = "Loopback";
}

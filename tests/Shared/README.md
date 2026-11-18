# tests/Shared

Helpers that more than one test project needs. There is one copy of each, compile-linked into the test projects (no
copies, and no shared test assembly). Each project links only what it can compile:

| Folder | Needs | Linked into |
|---|---|---|
| `*.cs` and any other folder | BCL, xunit, Lifestone.Data | every test project |
| `Client/` | Lifestone.Client | the projects that reference Lifestone.Client: Client.Tests, Loopback.Tests, ServerMod.Tests |
| `Loopback/` | the loopback gate | Lifestone.Loopback.Tests only |

The link lines in each test project are:

```xml
<Compile Include="..\Shared\**\*.cs" Exclude="..\Shared\Client\**;..\Shared\Loopback\**" LinkBase="Shared" />
<Compile Include="..\Shared\Client\**\*.cs" LinkBase="Shared\Client" />     <!-- projects that reference Lifestone.Client -->
<Compile Include="..\Shared\Loopback\**\*.cs" LinkBase="Shared\Loopback" /> <!-- Lifestone.Loopback.Tests only -->
```

The moved helpers keep the namespaces they had (`Lifestone.Data.Tests`, `Lifestone.Data.Tests.Textures`,
`Lifestone.Client.Tests`). New helpers use `Lifestone.Tests.Shared` and `Lifestone.Tests.Shared.Loopback`.

## Contents

- `RequiresDatsFactAttribute.cs` (with `DatSetFixture`), `RequiresDatsTheoryAttribute.cs`,
  `RequiresHighResDatsFactAttribute.cs` (Fact and Theory): skip when the retail dats are absent.
- `RepositoryFiles.cs`: the checkout the tests were built from, and the main checkout around a worktree.
- `AllocationMeasurement.cs`: the non-parallel collection and window for zero-allocation tests.
- `ManualTimeProvider.cs`: a `TimeProvider` that moves only on `Advance`; its timers fire there.
- `Loopback/`: `LoopbackFact` and `LoopbackTheory`, which skip unless the localhost backend is ready (`BackendProbe`);
  `LoopbackCollection`; and `LoopbackLock`, the cross-process lock file in the main checkout's `cache/`.

## Conventions

- Tests that talk to the localhost backend are `[LoopbackFact]` or `[LoopbackTheory]`. Their class carries
  `[Trait("Category", "Loopback")]` and `[Collection(LoopbackCollection.Name)]`, and they live in
  `tests/Lifestone.Loopback.Tests/<Track>/`. A reflection test enforces this. Default runs exclude them with
  `--filter "Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Tier!=Tool&Status!=KnownFailure"`; the Exhaustive tier (every test that reads the retail dats outside the retail UI, session and avatar tests: the sweeps and the decoder and asset pins; its count is the baseline file's "exhaustive") has its own slice-check stage (`-Exhaustive`), run at every gate; a slice that touches `src/Lifestone.Data` (or one of the Client folders whose tests moved (`src/Lifestone.Client/` `Audio`, `Effects`, `Interiors`, `Movement`, `Navigation`, `Objects`, `Play`, `PortalSpace`, `Rendering` or `Sky`)) runs `slice-check -Exhaustive`.
- Loopback classes have no `IClassFixture` (and the Loopback collection no `ICollectionFixture`): xUnit builds fixtures
  before the gate takes the lock and re-probes the backend, even when every test skips. Per-class shared state (one
  login for several tests) is created lazily from inside a test body, after the gate, never in a fixture constructor.
  The reflection test enforces this too.
- `[Trait("Tier", "Soak")]` marks long tests (fuzzing at full size, the lossy soaks), which default runs exclude.
  `[Trait("Tier", "Exhaustive")]` marks the tests that read the retail dats: the full sweeps and the decoder and asset pins (the retail UI, session and avatar tests keep the dats only as an input and stay in the default run).
- `[Trait("Tier", "Timing")]` marks a test whose verdict is wall-clock time (a frame budget, a latency, a rate) and so only
  means something on a quiet machine. The default filter leaves it out; the lead runs it alone (`--filter "Tier=Timing"`), with no other Godot or
  dotnet process up. It covers allocation counts too (a zero-byte assertion over a hot path gains bytes from tiered JIT and from a loaded
  machine, M6 item 0 slice t). Every Timing test carries a one-line comment beside its trait naming the row that moved it (`grep -rn 'Tier", "Timing' tests`
  lists them). Two allocation tests joined the tier in M6 item 0's fix F3, each with its reason beside its trait: `FightPickerTests.A_decision_allocates_nothing` (m6-alloc-flake-fightpicker) and `IndoorCreatureLayerTests.A_warm_indoor_runner_with_a_creature_layer_allocates_nothing_a_step` (m6-alloc-flake-indoor-layer). Each is a zero-byte count that failed once under the parallel suite and passes alone (tiered JIT and the other tests on the thread pool). Not Timing: a test of the Exhaustive tier (it reads the dats and runs at every gate) and a Loopback test (it needs the backend).
- `[Trait("Status", "KnownFailure")]` marks a test that is known to fail. It never counts in the green total (the default filter leaves it out),
  and its `Trait` reason or a comment names the `TODO(...)` row that owns the failure, so it cannot be forgotten. Run them with `--filter "Status=KnownFailure"`.

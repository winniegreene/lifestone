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
  `--filter "Category!=Loopback&Tier!=Soak"`.
- Loopback classes have no `IClassFixture` (and the Loopback collection no `ICollectionFixture`): xUnit builds fixtures
  before the gate takes the lock and re-probes the backend, even when every test skips. Per-class shared state (one
  login for several tests) is created lazily from inside a test body, after the gate, never in a fixture constructor.
  The reflection test enforces this too.
- `[Trait("Tier", "Soak")]` marks long tests (fuzzing at full size, the lossy soaks), which default runs exclude.
  `[Trait("Tier", "Exhaustive")]` marks full sweeps over the dats.

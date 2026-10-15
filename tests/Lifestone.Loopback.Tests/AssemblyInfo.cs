// The localhost backend is one process with one test account: nothing in this assembly runs in parallel. Across
// processes and worktrees the loopback lock file does the same (tests/Shared/Loopback/LoopbackLock.cs).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

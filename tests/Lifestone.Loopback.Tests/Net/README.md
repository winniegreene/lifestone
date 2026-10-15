Live tests of the net track (login, character list, world entry, logoff and quit, teleports, a walk ACE accepts, the lossy soak) against the localhost ACE.

Status: N11 (live phase A) and N12 (live phase B) are written and have run against the real ACE (G6 stage 2; 5b slice b1). m2-plan §8 traces DoD 2, 8 and 12 to them.
Everything they drive was first checked against the in-memory FakeAce in `tests/Lifestone.Net.Tests`.

Shared by both phases: `LiveNetClient` (one `AcSession` against the backend, with the sink that records what the server sent; `Create(dats, retail, character, lossy)`), `LiveSteps` (login, enter, unhide, the log off,
and `Ask`/`AskData` for the Lifestone mod's `@lifestone` verbs), `LiveStepOrder` (`[LiveStep(n)]` orders a class's tests).

N11 as built:

- `LivePhaseATests` (character `Lifestone Smoke`): the tests run in step order (`LiveStepAttribute`). Step 1 asks the mod for `@lifestone status` and calls `LoopbackRun.Abort` when the access is not Player or
  ACE's first-account auto-promotion is on, so no later test logs in to a server in that state. Step 2 is the life of a session: login, the data check, a character created when missing,
  the world entry into the Academy's landblock (0x8602) with the player hidden, LoginComplete making the server clear Hidden, the log off, Quit, an immediate re-login and one after ACE's 2 s.
- The immediate re-login meets error 0x01 once. ACE keeps a quit or booted session findable for 2 s (`SessionTerminationDetails.TerminationEndTicks`) and, while it does, answers a login
  of the same account with 0x01 (`account_login_boots_in_use`, docs/protocol.md section 4.4). With the default login policy the first retry gets in; with
  `LoginRetryPolicy.Retail` (one attempt) it fails. After 2 s there is no 0x01. `LiveNetClient.WaitOutAceLinger` is the pause a test uses when it must not meet the 0x01.
- `NetFixtureStore`: the golden captures. The client's raw tap (`NetCapture`) is saved into `cache/net-fixtures/<CacheKey>/` (CacheKey: `ace-<12 characters of the pinned ACE
  commit>-client1802`), never committed; a body holding the password in ASCII, UTF-16 or a LoginRequest-style String32L is refused whole and nothing is written. `NetFixtureTests` checks the guard
  with a sentinel password, the round trip and, with `[RequiresNetFixturesFact]`, that every captured message decodes again and none holds the real password.

N12 as built (`LivePhaseBTests`, character `Lifestone Loopback`, the one the headless loopback tests use; it is created when missing):

| Step | Test | What it checks |
|---|---|---|
| 10 | `Place_poi_Holtburg_...` | `@lifestone place poi "Holtburg"` brings an F751 and an F748 in landblock 0xA9B4 at the place reply's coordinates, LoginComplete takes the server out of Teleporting, `where` agrees with the F748, the landblock's objects arrive; `place loc` sends the character back to its start. |
| 11 | `Walking_Holtburg_on_the_retail_cadence_...` | 15 s of walking on retail's AutonomousPosition cadence: `where` within 0.5 m of the last position sent after each 5 s leg and at the end, every F748 echo is a position sent, the force-position and teleport sequences never change, no further F751. |
| 12 | `Duplicated_encrypted_client_packets_...` | The positive control of the soak's oracle: 30 s over a link that only duplicates (3% each way). ACE's bad-checksum delta equals the link's duplicate encrypted deliveries, and both are above 0. |
| 20 | `Two_minute_walk_over_a_five_percent_loss_...` | `Tier=Soak`: 2 minutes over the 5% drop, 2% reorder link (`LossyDatagramSocket`, compile-linked from `Lifestone.Net.Tests/Support`), an AutonomousPosition every 200 ms, walking 20 s and standing 3 s. The session survives, `where` is exact, the client sees 0 checksum failures and 0 gap skips, and the `@lifestone net` bad-checksum delta equals the socket's duplicate encrypted deliveries. |

- `KinematicWalker` is the test-only mover: a fixed ground speed (`MotionInterpreter.WalkAnimSpeed`, 3.12 m/s) round a route, standing on `TerrainSampler`'s height, facing its way. It collides with nothing, because ACE forces
  the requested position (docs/protocol.md section 6). `PositionCadence` is retail's send rule (`CommandInterpreter::ShouldSendPositionEvent`, 11.6096 0x006B5520): at once on a new cell or terrain triangle, otherwise strictly
  more than 1.0 s after the last message of either kind with a changed frame; the soak swaps the 1.0 s for 200 ms. `WalkDriver` runs both through a live session. `KinematicWalkerTests` checks them without a backend.
- ACE's counters (`@lifestone net`) are server-wide since boot, so the lossy tests read them in a clean session before and another after the lossy one. The lossy session is the only thing in between.
- **The character ends where it began.** Every test sends it back to its start with `place loc`, and a failing test tries to do the same in its `finally` (`RestoreBestEffort`). A character found away from the Academy
  at the start of a test (a failed run) is sent to the Academy start first (`EnsureAtStart`; `TODO(loopback-start-frame)`: that fallback's rotation is the identity). `Lifestone Smoke` is never moved here.
- Run: the plain loopback run (`--filter "Category=Loopback&Tier!=Soak"`) includes steps 10, 11 and 12; the soak is `--filter "Category=Loopback&Tier=Soak"`.

Fixtures and recordings stay under `cache/` and are never committed (D14).

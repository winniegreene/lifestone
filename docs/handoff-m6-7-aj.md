# Handoff: M6 item 7 slice AJ (the body and the server part ways)

Branch `m6/7-aj`, cut from m6/7 at 268f7e75. Doc notes: `cache/m6/7/doc-notes-aj.md`.

## Done

- The cause, from the climb's log, ACE's log and the code:
  - Proved: the "wall" was retail's constraint, the 50 m leash to the last server position (`route_m=49.9`, the timing fits the fade).
  - Proved: from the arrival at 12:27:33.7 the server stopped ticking the player alone: no echoes, no five-minute save after 07:24:07 (ACE's clock), vitals and gear mana frozen, while Identify still answered.
  - Proved by the code: only leaving the landblock stops all of those; dormancy does not.
  - Proved live on the second case, the Lodge ramp in stretch 2: the mod answered `not-in-world` at 13:05:51 and 13:06:11 UTC. The arrival came 301.1 s after she left the Lodge; the plateau's came 300.9 s after. On the plateau no `where` was asked, so it rests on the same signature.
  - A corpse run lands in that window by its own length: expect it again.
  - Every direction fails because the constraint adds up distance moved in any direction and only a new server position sets it back. Keys, side-steps and walking back all give 0.
  - The dat replay on the real ramp, no bodies: stops at (50.00, -131.36, 2.27) against live (50.002, -131.774, 2.02); all four directions 0.000 m; one echo frees her; with echoes she runs on.
- Of 21 arrivals in both logs, the only two within 5 s of 300 s are the two sticks. The nearest clean ones are 279.5 s and 328.6 s.
- Server fix: `ArrivalGuard` in the mod, public API only (the mod's no-patching rule stands). It runs between an arrival and the block's unload check and marks her block active. Closes portals, respawns and logins; for spell recalls the race shrinks to one tick. `LIFESTONE_ACE_UNLOAD_RACE=ace` restores ACE. Compiled against the real ACE; not run. The lead deploys with `-ModOnly`.
- The client rule: `BodyApartWatch` and `AutopilotScenario.BodyApart.cs`. A gap past the constraint's start (10 m outdoors, 5 m in a room) with no new server position for 15 s is a finding; one `where`; `not-in-world` re-logs in at once (the orphan's machine, M6 6 MH); otherwise a second finding re-logs in. A gap within 300 s of a re-login stops `failed:body-server-apart`, no recall.
- Tests: 6 pure in `BodyApartWatchTests`, 7 in `AutopilotOrphanTests`, 6 in `ArrivalKeepRulesTests`, 1 dat replay in `IslandDungeonWalkDatTests` (Exhaustive tier). With the rule switched off, 6 of the 7 fail; the seventh proves a run with no body reads nothing.

## Next

- The lead deploys the mod and proves both live. Look for `Lifestone arrival guard:` in ACE's log and `autopilot: body apart:` in the client's.
- Stretch 2's Winnie stays orphaned until a re-login: `/auto`, then about 40 s, lets the orphan probe do it.
- For the owner, only if a spell recall ever sticks: a Harmony prefix would close the last tick, but it overturns the M2 no-patching rule.

## Gotchas

- A body that stands still shows no gap: `m6-orphan-standing`.
- The re-login's lines take their name from `_reloginName`; the orphan's text is unchanged.
- ACE's log is in UTC-5.
- Corpses at 0xF481 and 0xF380 and a tusker at 0xF382 reloaded in the 75 ms after the arrival: not explained, no bearing found.

## Test count and TODOs

Default 13,199; exhaustive 1,030. Open: `m6-body-apart` (live proof), `m6-orphan-standing` (new).

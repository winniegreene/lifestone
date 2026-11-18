# Handoff: M6 6 FXD2 (journey and retreat fixes)

Done (review-death.md): 6-D2, 6-D4, 6-D7 (smallest), 6-D10, 6-D12.
- JourneyScenario: no second death station after a planned death station fails; the inserted one is counted (JourneyVerdict.NoteInserted; `inserted=n` on the done line when above 0).
- TravelLegSafety: the retreat's recall is checked (book, mana, pack components, tie); refusal 0x0400/0x049E falls back once to the walk back (`way=walk-back after recall refused`).
- HealStations / QuestStepLeg: component refusal reads the chat code since the cast (private `ChatRefusalSince`).
- LowRungDeathLiveTests case (b): planned lie-down by position, report-only note when the rung change died first.

Tests added: 9 (J1 2, RungChange 5 incl. a 2-case theory, P7p 1, QuestKill 1); default count 12,766 (base 12,757).
Loopback: case (b) changed only in what it prints; the lead runs it.

Gotchas
- Recall's school (Portal) has no focus item, so `HeldForAccount(..., null)` is null for it: "components=not judged". The pre-check bites only for a school with a held focus; for Winnie the refusal fallback is the guard. The tests give the recall a War school entry to exercise the check.
- RungRig got `RecallRefusal` (UseDone error for an untargeted cast, no teleport).
- The D7 tests send the 0x0400 after the UseDone; both fail with the chat argument removed.

Open: 6-D7 proper (one shared fight-back core; the station has no health line and casts raw), the account name for the recall's pre-check.

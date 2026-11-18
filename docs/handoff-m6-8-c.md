# Handoff: M6 8 slice C (the small locked-door check, D117)

Branch m6/8-c, cut from m6/8.

## Done
- ACE's door appraisal carries the lock (Locked is an assessment bool, `AppraiseInfo.cs:135-165`); the reducer now copies it to the door (`WorldObject.Locked`, `LockedFrom`).
- NavRunner asks the server once per run for a shut door of unknown lock (`INavDoorFeed.ReadLock`), stops by name when it is locked (log line with guid, name, cell, source; failure text names them).
- A refused Use (sound 0x94 on the door) is read by `DoorRefusals` and ends the walk at the first Use with no timeout.
- `QuestStepLeg.LockedText` prints the source.

## Tests
- Six new: five in `DoorLegTests` (four dat-based Exhaustive legs and one more, appraised open, appraised locked, refused use, unknown both ways, found open never appraised) and one default in `DoorFeedTests` (locked sound). Default count baseline 14,135 plus 1.

## Gotchas
- A "not locked" answer is never acted on and is overwritten by the next 0x02D2 (ACE re-locks ~30 s after a use).
- `NavDoor` gained two optional trailing fields (`LockedFrom`, `Name`).

## Open
- Not run live (no backend): appraisal and sound 0x94 on the wire.
- Placed M7: `door-locked-replan`, `door-locked-behind`. `door-locked-unknown` is closed.

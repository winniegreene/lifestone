Live tests of the server-mod track (the @lifestone verbs: status, where, place, xp, net, places, quest, spell learn, item).

- `ModVerbsLiveTests` (steps 30 to 34): `status`, `places` (56 destinations under 62 names), every error reply a client can provoke
  (`not-found`, `invalid-cell`, `bad-args`, `unknown-verb`, `busy` while teleporting) and one `place loc` to a curated interior row
  (the Holtburg shop). Safe: nothing is levelled; every test that moves the character ends with it back at its start.
- `ModVerbsLiveTests` steps 35 and 36 (M3 1.1 s2a; written and compiled, run by the lead after `setup-server.ps1 -ModOnly`; step 30 now expects 13 verbs): both on `Lifestone Throwaway` only. Step 35, `spell learn`: one cheap spell is `learned` (a fresh throwaway) or `known`, a repeat is `known` (how a ready mage is verified), an id outside ACE's spell table is `unknown` while the rest of the list is still answered and the request is `ok`, and 129 ids are `bad-args`. Step 36, `item`: a small Pyreal stack is created, a stack above the maximum is clamped and says `clamped`, and an unknown wcid is `unknown-wcid`.
- `ModXpLiveTests` (step 40): `xp`. It adds real experience and so raises the loopback character's level for good, so it carries a fixed
  `Skip` and `Tier=OwnerApproved`: it runs only with the owner's go-ahead, by removing the `Skip` argument in a working copy.
- Not coverable from a client on this computer: `not-in-world`, `not-loopback`, `needs-player` and `status` at ACE's console
  (the mod's routing unit tests cover them).

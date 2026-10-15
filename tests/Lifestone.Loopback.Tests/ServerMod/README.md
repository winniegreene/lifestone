Live tests of the server-mod track (the @lifestone verbs: status, where, place, xp, net, places, quest).

- `ModVerbsLiveTests` (steps 30 to 34): `status`, `places` (56 destinations under 62 names), every error reply a client can provoke
  (`not-found`, `invalid-cell`, `bad-args`, `unknown-verb`, `busy` while teleporting) and one `place loc` to a curated interior row
  (the Holtburg shop). Safe: nothing is levelled; every test that moves the character ends with it back at its start.
- `ModXpLiveTests` (step 40): `xp`. It adds real experience and so raises the loopback character's level for good, so it carries a fixed
  `Skip` and `Tier=OwnerApproved`: it runs only with the owner's go-ahead, by removing the `Skip` argument in a working copy.
- Not coverable from a client on this computer: `not-in-world`, `not-loopback`, `needs-player` and `status` at ACE's console
  (the mod's routing unit tests cover them).

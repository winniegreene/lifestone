# Retail addresses for M2: 11.4186 and 11.6096

Step L10 of the M2 plan ([m2-plan.md](../milestones/m2-plan.md) §2.2), under owner decision D4 (§10). It gives, for
every retail function the M2 design cites, its address in the 2013 build 11.4186 (the one with a PDB and the
decompile) and in the user's end-of-retail build 11.6096 (the `acclient.exe` Lifestone is faithful to). It also
settles the four constants D4 allowed to be read from the user's `acclient.exe`.

The tracks cite retail behaviour as function, 11.4186 address and 11.6096 address (m2-plan §6.1 and §6.3).
End-of-track merges reject a remaining `TODO(...): 11.6096` citation, so a track that cites a function not listed
here adds it with `tools/research/Find-RetailSymbol.ps1` and a row in §2.

Summary:

- **234 functions** are cited by the design (the synthesis that became m2-plan.md, and the ten track plans).
  - 229 are in the pinned symbol table: 227 as exact or verified rows, 2 as predicted rows (§3).
  - 1 more, `ClientCombatSystem::DoJump`, is not in the table; its 11.6096 address was settled from the bytes (§1.2).
  - 4 are unmapped and stay named TODOs (§3).
- **G3 U5 (second stage) added 46 rows** (§2 now has 725): the cited functions of `gmCGAppearancePage`, `gmCGTownPage`, `gmCGSummaryPage`, `gmCG3DView`,
  `gmCharGenMainUI`'s finish and verification handling, the surface calls the colour wheel makes (`SurfaceWindow::ReplaceColor`, `BlitAndColor`, `RGBAColor::GetColor32`,
  `ClientCharGenState::GetColorFromPal`), `DialogFactory::DialogDone` and the few helpers they name, each matched by both addresses in the pinned table. The ones
  the table already had (for the earlier tracks) are not repeated. `NameInputFilter` is not in the table (`TextFilters.Name` says so).
- **G3 U5 (first stage) added 37 rows** (§2 now has 679): the cited functions of `gmCharGenMainUI`, `gmCGHeritagePage`, `gmCGProfessionPage` and
  `gmCGSkillsPage`, `CharGenState::GetAttributeName`, `UIElement_Text::SetStringInfoWithFont` and `AppendStringInfoWithFont` and `NumberInputFilter`, each matched
  by both addresses in the pinned table. The constructors of the three pages are not cited.
- **G2 U3-U4 added 28 rows** (§2 now has 642): the cited functions of `gmDataPatchUI`, `gmDisconnectedUI`, `gmEpilogueUI`,
  `gmCharacterManagementUI`, `UIFlow::Update` and `UIFramework::QueueUIMode`, each matched by both addresses in the pinned table. The
  constructors of the data check, the disconnected screen, character management and the epilogue are not in it and stay named TODOs (§3).
- **Wave 1 added 367 rows** (§2 now has 614), for the functions the tracks cite in their code and documents. They come
  from docs/avatars.md §8, docs/physics.md, docs/retail-ui-data.md §11 and the net, state and portal tracks' requests;
  the Tracks column names the track. Every new row is one row of the pinned table, matched by both of its addresses
  (`tools/research/Find-RetailSymbol.ps1` gives the same rows), with the table's name and confidence. Two are predicted
  (`CPartArray::GetNumSphere`, `Proto_UI::UICounterFailedSend`). The functions the table does not map are named TODOs
  (§3), and §3.1 lists the byte reads still wanted.
- **The four D4 constants are settled** (§1):
  - the portal exit window admits **frames 68–76**;
  - `MIN_JUMP_EXTENT` is **0.001f**;
  - `CAMERA_DEFAULT_PIVOT_Z` is **1.5f**;
  - the ClothingTable heritage fallbacks are the eight setup groups in §1.4.

## The pin

| | |
|---|---|
| Repository | Mag-nus/acclient-AI-RE (MIT label; the names are Turbine's, from the 11.4186 PDB; reference only) |
| Path | `2015-10 11.6096/docs/symbol_transplant_11.6096.tsv` |
| Commit | `01ccd5be2d8fff40e25151d804fa97b3e8f27ec6` (main on 2026-10-12); the file last changed in `b3ea22533b6ae5d6199d95c8c2193a485ebcb712` |
| Git blob | `f0b2aae95c687ece822638208e7152821e2744a3`, 3,036,979 bytes (re-checked locally) |
| SHA-256 | `cc19490d457fb8906c3c2bde58bd210830a64d5b632a1d696b401a639ff654d7` |
| Fetched | 2026-10-12 17:16 UTC, with `gh api` (the blob endpoint, base64 decoded locally) |
| Local copy | `<repo>\cache\research\symbol_transplant_11.6096.tsv` (gitignored), with `symbol_transplant_11.6096.pin.json` beside it holding the fields above |

The table is untrusted data: it is hashed and split into columns, never executed. Its columns are `va_11.6096`,
`va_11.4186`, `name`, `module` (the 11.4186 `.obj`), `confidence`, `candidates` and `alt_names`. It has 36,087 rows:
9,520 exact, 23,783 verified, 1,259 predicted and 1,525 unmapped (11.6096 bodies with no name).

To fetch it on another machine, with the owner's approval: request the blob above from
`repos/Mag-nus/acclient-AI-RE/git/blobs/f0b2aae95c687ece822638208e7152821e2744a3` with `gh api --jq .content`,
base64-decode it into `<repo>\cache\research\symbol_transplant_11.6096.tsv`, and check the SHA-256. The blob id is
content-addressed, so it pins the bytes by itself.

**Lookup.** `tools/research/Find-RetailSymbol.ps1` checks the SHA-256 first and refuses a file that differs. It looks
in the cache of the outermost enclosing checkout, so a worktree nested in the main checkout uses the main checkout's
copy.

```powershell
pwsh -File tools\research\Find-RetailSymbol.ps1 -Name 'gmSmartBoxUI::UseTime,CameraSet::SetDefaultOffsets'
pwsh -File tools\research\Find-RetailSymbol.ps1 -Address 0x00458F80 -Build 11.4186
pwsh -NoProfile -Command "& tools\research\Find-RetailSymbol.ps1 -Name 'CMotionInterp::*jump*' -Like | Format-Table"
```

**Rules for using the addresses:**

- There is no constant offset between the builds. Never compute one address from the other; look it up (doc 13 §4 of
  the same repository).
- Predicted rows are not confirmed. Before code relies on one, confirm it against the bytes.
- 46 functions changed between the builds (doc 13 §5), among them three in `CMotionInterp` that gate jumping. For
  these, the 11.4186 decompile is not the 11.6096 behaviour.

## 1. The D4 constants

**How these were read.**

- The user's `<repo>\client\acclient.exe` is 4,841,472 bytes, SHA-256
  `bca95bbebed4b9ed1ff09d0da83144e2fc4208f63ad7ada5cb47c3ca207ccba9` (11.6096; docs/research/rea-and-symbols.md §1).
- It was opened with `[System.IO.File]::OpenRead` only, and only the bytes named below were read. It was never
  executed or written.
- Virtual addresses map to file offsets through the PE section table:
  - `.text`: VA 0x00401000, raw 0x00001000;
  - `.rdata`: VA 0x00793000, raw 0x00393000;
  - `.data`: VA 0x0080B000, raw 0x0040B000. Its raw size is 0x2D000, so `.data` from 0x00838000 up is zero-filled
    at load.
  - For these three sections, file offset = VA − 0x00400000.
- The functions were located with the pinned table and read alongside the 11.4186 per-file decompile (§6).
- No decompiled code is reproduced here.

### 1.1 Portal exit window (portal)

`gmSmartBoxUI::UseTime` (11.4186 0x004D6E30, 11.6096 0x004D7A90). **Retail admits frames 68–76**, nine frames. The
decompile's float reading is what the machine code does. D27's default (68–76) is retail, SP8 is closed, and
`ExitWindow` needs no TODO.

| Step | 11.6096 address | Value |
|---|---|---|
| The 2.0 s hold in state 4 (compare in double) | 0x004D7EDB | double 2.0 at 0x007BE3D8 |
| The tunnel frame | call at 0x004D7EF2 | `CPhysicsObj::get_curr_frame_number` (0x00510250) |
| 120 − frame, as an unsigned 32-bit integer | 0x004D7EF7–0x004D7F0A | immediate 120; 2^32 (float at 0x0079920C) is added if negative |
| ÷ 40 | 0x004D7F10 | float 40.0 at 0x007BE3F0; the same constant is the tunnel's frame rate (loaded at 0x004D7BB8 for `set_sequence_animation`) |
| Time left stored to a 4-byte slot (rounded to float) | 0x004D7F16 | |
| The 5.0 s cap: past it, state 5 regardless | 0x004D7F24 | double 5.0 at 0x007BE3E0 |
| Lower bound, strict: 1.0 + c1 < time left | 0x004D7F31–0x004D7F48 | double 1.0 at 0x007BE3E8; c1 = double 0.10000000149011612 (`(double)0.1f`) at 0x007BE808 |
| Upper bound, strict: 1.0 + c2 > time left | 0x004D7F4A–0x004D7F61 | c2 = double 0.30000001192092896 (`(double)0.3f`) at 0x007BE800 |
| State 5, stamped with now | 0x004D7F63 | |

**Precision.**

- `ClientMain::ConfigureFPU` (0x00401120 in both builds) calls MSVCR70 `_control87` at 0x0040113D with `_PC_53`
  (0x10000) under mask `_MCW_PC` (0x30000), then sets round-to-nearest.
- The device keeps that setting (D3DCREATE_FPU_PRESERVE; docs/third-party/sky.md, scenery.md).
- So the x87 runs at 53 bits, and the bounds are exact: 1.1000000014901161 and 1.3000000119209290.

**Result.** Time left is t(f) = float((120 − f) / 40).

| Frame | t(f) | In the window? |
|---|---|---|
| 76 | float(1.1) = 1.10000002384 | yes: it is above 1.10000000149 |
| 77 | 1.075 | no |
| 68 | float(1.3) = 1.29999995232 | yes: it is below 1.30000001192 |
| 67 | 1.325 | no |

So the window is frames 68–76.

- Exact arithmetic (strictly between 1.1 and 1.3 s, the wording of m2-log-in.md §4.5) would give 69–75.
- So would a 24-bit FPU, where the sums round to float(1.1) and float(1.3), the latter a tie to even. Retail does
  not run at 24 bits.

### 1.2 MIN_JUMP_EXTENT (physics)

**`MIN_JUMP_EXTENT` = 0.001f** (bits 0x3A83126F, 0.0010000000474974513). It lives at 11.6096 VA 0x007D01C8 in
`.rdata`, which is read-only. That is why the 11.4186 decompile prints the literal 0.001 where it compares against
the constant.

All six references read it:

- `ClientCombatSystem::GetJumpPowerLevel` (11.4186 0x0056B050, 11.6096 0x0056BD90): the compare at 0x0056BDA1 and the
  address load at 0x0056BDAC;
- `ClientCombatSystem::DoJump` (11.6096 0x0056BE50), once in each branch: 0x0056BE75 and 0x0056BE80 (not autonomous),
  0x0056BED5 and 0x0056BEE0 (autonomous).

**Behaviour.** The jump extent is the power-bar level when the level (the x87 value `GetPowerBarLevel` returns) is
at least `MIN_JUMP_EXTENT`; otherwise it is `MIN_JUMP_EXTENT`. The extent is stored and sent as a float.

**`DoJump` in 11.6096 is not in the table.** 0x0056BE50 is identified from the bytes:

- It sits after `ClientCombatSystem`'s scalar deleting destructor (0x0056BE20) and before `GetDefaultCombatMode`
  (0x0056C050). That is the 11.4186 order: 0x0056B0E0, 0x0056B110, 0x0056B310.
- It takes one stack argument (`autonomous`, tested first) and returns with `ret 4`.
- It calls what the 11.4186 function calls, no more:
  - `FinishJump` (0x0056B6F0) and `GetPowerBarLevel` (0x0056BB20), then reads `MIN_JUMP_EXTENT`;
  - `CM_Movement::Event_Jump_NonAutonomous` (0x006B0A00);
  - `CPhysicsObj::get_minterp` (0x00512B90), `CMotionInterp::jump` (0x00529390) and
    `CPhysicsObj::get_local_physics_velocity` (0x00512C10);
  - `JumpPack::JumpPack` (0x00517770) and `CM_Movement::Event_Jump` (0x006B0940);
  - `ClientSystem::AddTextToScroll` (0x005649F0) three times, for the three refusal messages.

### 1.3 CAMERA_DEFAULT_PIVOT_Z (godot-world)

**`CAMERA_DEFAULT_PIVOT_Z` = 1.5f** (bits 0x3FC00000). It lives at 11.6096 VA 0x0079CC2C in `.rdata`. Two functions
read it:

- `CameraSet::SetDefaultOffsets` (11.4186 0x00458F80, 11.6096 0x004590A0) calls
  `CameraManager::SetPivotObject` (0x00455FC0) with the player. It then loads the constant at 0x004590FD and stores
  the vector (0, 0, 1.5), unscaled, at CameraManager +0x18. The godot-world plan reads this as the pivot on the
  player.
- `CameraSet::Farther` (11.6096 0x004589B0) loads it at 0x00458A97.

In `SetDefaultOffsets` the viewer offset is (0, −2.5 × scale, 0.75 × scale). The factors are the floats −2.5 at
0x0079CC24 and 0.75 at 0x0079CC28.

This settles the FlyCamera.cs TODO the godot-world plan traced to `CameraSet::SetInHead`: the in-head offset
(0, 0.18, 0) is taken from the pivot (0, 0, 1.5).

**Read for G5's chase camera** (the owner's `acclient.exe` 11.6096, read-only, with the 11.4186 per-file decompile of `PORTAL/smartbox/CameraSet.c`
and `CameraManager.c` from the pinned repository): `angle`, the rotation step of `CameraSet::Rotate`, `Raise` and `Lower`, is a global at 0x0083E034
that is set at start-up by the function at 0x006EBAC0 (`180 / 8`, then `pi / that`: **8 degrees**, 0.1396 rad); `CAMERA_MOUSELOOK_LIMIT` at 0x0079CC08 is
**0.8**; the `CameraManager` constructor sets the translational and rotational stiffness to bits 0x3EB33333 (**0.35**), `m_rCameraAdjustmentSpeed` to
0x42240000 (**41.0**) and `m_bAlignCameraToSlope` to true. The first-person eye is 0.18 m in front of the pivot; `Farther` leaves it for
(0, -1.2, 0.5). `ChaseCameraModel` (`Client/Camera`) re-expresses `SetDefaultOffsets`, `Closer`, `Farther`, `Rotate`, `Raise`, `Lower`,
`MouseLookHandler`, `SetInHead`, `UpdateCamera` and the viewer half of `CameraManager::UpdateCamera`; the overhead and map views, the slope alignment and
the debug offset movements are named TODOs in the code.

### 1.4 ClothingTable heritage fallbacks (avatars)

`ClothingTable::BuildObjDesc` (11.4186 0x005A7900, 11.6096 0x005A89B0) first looks the setup up in the table's
ClothingBase hash. On a miss, it maps the setup to a fallback key and looks that up instead.

- A setup in none of the groups returns success with nothing applied.
- So does a fallback that is the null id, or one the table has no ClothingBase for. The null id is the
  zero-initialised global at 0x008EEA04, which no code writes; the comparison is at 0x005A8B52.
- The group comparisons run at 0x005A89F8–0x005A8B1D, in the order of the table below. The fallback is loaded at
  0x005A8B10–0x005A8B4D.
- Every setup value is initialised `.data` in the file, and every reference to the table is a read.
- Four groups get their fallback by copying one of their own setups at start-up. The copy is done by static
  initialisers (`$E178`–`$E193` in ClothingTable.obj, 0x0070E480–0x0070E520), each into a zero-initialised global.

| Group (names from the 11.4186 decompile) | Setups compared: 11.6096 address = value | Fallback key: address = value |
|---|---|---|
| Umbraen male (`CROWN`, `CROWN_GEN`, `NOCROWN`, `VOID`) | 0x00822094 = 0x0200196F, 0x00822098 = 0x02001972, 0x0082209C = 0x02001A5F, 0x008220A0 = 0x02001A6F | 0x008EE9DC = **0x0200196F** (copied from 0x00822094 by `$E184`, 0x0070E4C0) |
| Umbraen female (same four) | 0x008220A4 = 0x02001970, 0x008220A8 = 0x02001970, 0x008220AC = 0x02001A5E, 0x008220B0 = 0x02001A6E | 0x008EE9E0 = **0x02001970** (from 0x008220A4, `$E187`, 0x0070E4E0) |
| Penumbraen male (same four) | 0x008220B4 = 0x0200196E, 0x008220B8 = 0x02001971, 0x008220BC = 0x02001A5D, 0x008220C0 = 0x02001A70 | 0x008EE9E4 = **0x0200196E** (from 0x008220B4, `$E190`, 0x0070E500) |
| Penumbraen female (same four) | 0x008220C4 = 0x0200196D, 0x008220C8 = 0x0200196D, 0x008220CC = 0x02001A5C, 0x008220D0 = 0x02001A71 | 0x008EE9E8 = **0x0200196D** (from 0x008220C4, `$E193`, 0x0070E520) |
| Undead male (`UNDEAD_GEN`, `SKELETON`, `SKELETON_NOFLAME`, `ZOMBIE`, `ZOMBIE_NOFLAME`) | 0x00822068 = 0x02001A0F, 0x0082206C = 0x02001A9C, 0x00822070 = 0x02001A9E, 0x00822074 = 0x02001A9D, 0x00822078 = 0x02001A96 | 0x008EE9D4 = **0x02001A0E** (from 0x00822064, `$E178`, 0x0070E480) |
| Undead female (same five) | 0x00822080 = 0x02001A0D, 0x00822084 = 0x02001AA0, 0x00822088 = 0x02001A9F, 0x0082208C = 0x02001AA1, 0x00822090 = 0x02001AA2 | 0x008EE9D8 = **0x02001A0C** (from 0x0082207C, `$E181`, 0x0070E4A0) |
| Anakshay male | 0x008220DC = 0x02001AA3 | `HUMAN_MALE_CLOTHING_DEFAULT` 0x008220D4 = **0x02000001** |
| Anakshay female | 0x008220E0 = 0x02001AA4 | `HUMAN_FEMALE_CLOTHING_DEFAULT` 0x008220D8 = **0x0200004E** |

**Notes on the table.**

- Membership and fallback come straight from the bytes.
- The per-setup names (`CROWN` … `ZOMBIE_NOFLAME`) assume that 11.6096 compares in the 11.4186 decompile's order. A
  wrong name would not change behaviour.
- The shipped data repeats two values. Umbraen female `CROWN` and `CROWN_GEN` are both 0x02001970. Penumbraen female
  `CROWN` and `CROWN_GEN` are both 0x0200196D.

**`gmCG3DView::Update`** (11.4186 0x004EE9D0, 11.6096 0x004EF660) has one setup fallback of its own:

- When the view's alternate setup and CharGenState's setup are both the null id, it uses `HUMAN_SETUP_ID`. That is
  0x02000001, at 0x0081D1A8 in `.data`, loaded at 0x004EF6F1.
- Its null id is the zero-initialised global at 0x00841CCC, which no code writes.
- It then calls `ClothingTable::BuildObjDesc` with the chosen setup for headgear, trousers, shirt and footwear.
- Also read (AV12): its distant light has intensity 2.0 and direction (0.3, **+1.9**, 0.65). These are immediates at
  0x004EF954–0x004EF972.

### 1.5 The chat windows' masks, colours and opacity (G4 g2, D55)

Read-only, as §1: the `acclient.exe` SHA-256 was checked against §1's value first (it matched), the file was opened for reading only and never run, and the
bytes below were read with a short Python script through the PE section table (ImageBase 0x00400000, `.text` file offset = VA - 0x00400000). No byte dumps are kept.

| Function | 11.6096 address | What was read | Value |
|---|---|---|---|
| `ChatInterface::PostInit` | 0x004F4A30 | A switch on the window id (master property 0x1000007E, read into the field at `this+0x5F8`): `id - 1` is range-checked against 7 and indexes a jump table at 0x004F4BA0 | ids 1 and 8 select the first case, 2, 3, 4 and 5 their own, 6 and 7 the "default" (nothing assigned) |
| the same, the cases | 0x004F4A5A-0x004F4A9B | each case stores a 32-bit immediate at `this+0x614` and falls to one store of zero at `this+0x618`: the low and high dwords of the 64-bit text-type mask (`m_llTextTypeFilter`; the 11.4186 decompile typed the field as a UI pointer and zeroed the wrong word) | ids 1 and 8: 0xFBFFFFFF (every type below 32 but 26); id 2: 0x101C (types 2, 3, 4, 12: speech, tells, emotes); id 3: 0x40C00 (types 10, 11, 18); id 4: 0x80000 (type 19); id 5: 0x78000000 (types 27 to 30); the high dword is 0 in every case |
| the same, children | 0x004F4A9D-0x004F4AF9 | three `GetChildRecursive` calls and where each result goes | 0x10000011 (cast to Text) is the **log**, 0x1000048C (a button) the "new text" indicator, 0x10000016 (cast to Text) the **entry**; the research's section 3 had log and entry swapped |
| `ChatInterface::ChatInterface` | 0x004F51B0 | the initial fields | mask all ones, then bit 26 cleared (so a window with no case above shows every type but the spew box's); default opacity 0.5 and current opacity 0.5 (0x3F000000), active opacity 1.0 (0x3F800000) |
| `ChatInterface::BuildChatColorLookupTable` | 0x004F3E20 | a loop of 34 stores of one colour, then pairs of (push colour address, call set-colour) and (push index, call set-element) | the default is green (0.5, 1.0, 0.498) at 0x0081D588; the pairs (read in order) are 2: white; 12: grey (0.824, 0.824, 0.784); 3, 31, 10, 19: yellow (1, 1, 0.247); 4, 11: tan (0.824, 0.824, 0.392); 8, 9: pink (1, 0.588, 0.588); 18, 33: orange (0.933, 0.573, 0.118); 27, 28, 29, 30, 32, 14: blue-grey (0.706, 0.863, 0.941); 15, 6, 21: red (1, 0.247, 0.247); 22: light red (0.96, 0.459, 0.447); 7, 17: light blue (0.247, 0.749, 1); 13: cyan (0.247, 0.863, 0.863); 5: purple (1, 0.498, 1); 26: pure red (1, 0, 0); the others stay green. The floats are static data in `.data` (initialised in the file, not by start-up code), so they were read directly |

**Result.** The research's section 3 reading of `BuildChatColorLookupTable` is confirmed in full, the per-window masks are settled, and the opacity defaults (0.5 idle, 1.0
while the entry has the focus) are the constructor's. `ChatTextTypes` (`Client/RetailUi/Chat/ChatTextTypes.cs`) carries these values; `GameChatTests` pins them.
Not read: the saved per-window text-type masks and opacities of the character's `PlayerModule` options, which override the defaults when present (`TODO(g4-chat-options)`).

### 1.6 The radar's update interval and palette (G4 g3b, D55)

Read-only, as §1.5: the `acclient.exe` SHA-256 was checked first (it matched), the file was opened for reading only and never run, and the bytes were read with the same short script
(through the PE section table). No byte dumps are kept.

| Function | 11.6096 address | What was read | Value |
|---|---|---|---|
| `gmRadarUI::UseTime` | 0x004DA500 | the timer test compares `Timer::cur_time` (the double at 0x008379A8) with `m_ttNextUpdateTime` (`this+0x660`); the new due time is `fld qword [0x007BEA58]` plus the current time | the double at 0x007BEA58 is `0x3F9999999999999A`: **0.025 seconds** (the 11.4186 decompile's `0.025` is right) |
| `gmRadarUI::GetBlipColor` | 0x004D8350 | the static colours its switch loads, 16 bytes (r, g, b, a floats) each from 0x0081C69C | blue (0.25, 0.66, 1, 1), gold (1, 0.67, 0, 1), yellow (1, 1, 0.5, 1), white (1, 1, 1, 1), red (1, 0.25, 0.39, 1), purple (0.75, 0.39, 1, 1), pink (1, 0.66, 0.75, 1), green (0, 0.5, 0.25, 1), cyan (0, 1, 1, 1), bright green (0, 1, 0, 1), at 0x0081C69C, 6AC, 6BC, 6CC, 6DC, 6EC, 6FC, 70C, 71C, 72C |

**Result.** The update interval is 0.025 s and the palette is the research's, so the 11.4186 values hold for 11.6096. Not read: the named roles' own globals
(`RGBAColor_RadarPortal`, `_Creature` and so on, copies assigned at start-up), whose roles are the research's (`RadarPalette` in `Client/RetailUi/Radar/`).

### 1.7 The link status's constants and packet-loss accessor (G4 g4, D55)

Read-only, as §1.5 and §1.6: the `acclient.exe` SHA-256 was checked first (it matched), the file was opened for reading only and never run, and the bytes were read with the same short script. No byte dumps are kept.

| Function | 11.6096 address | What was read | Value |
|---|---|---|---|
| `LinkStatusHolder::GetPacketLossPercentage` (a predicted row in the symbol table) | 0x004116D0 | the four bytes of the body: `fld dword [ecx+0x1C]; ret` | it is the accessor of a **32-bit float** field of the holder (the 11.4186 decompile's `m_fPacketLoss` returned as a double); the 11.4186 function (0x00411370) is the same distance from `GetConnectionStatus` (0x00411380 and 0x004116E0), so the row is right |
| `gmUIElement_LinkStatusIndicator::UpdateLinkState` | 0x004E73C0 | its three compares are `fcomp qword [m]` (opcode `DC`: 64-bit operands) at 0x007C2108, 0x007C2110, 0x007C2118 | doubles **5.0, 20.0, 40.0** |
| `gmUIElement_LinkStatusIndicator::UseTime` | 0x004E7450 | the update interval compare, `fcomp qword [0x007C20F8]` | the double **4.0** seconds (the flash interval 0.75 s sits at 0x007C2100, not read) |
| `LinkStatusHolder::GetConnectionStatus` | 0x004116E0 | `connected = (state == 3)`; the result is `Timer::cur_time` (the double at 0x008379A8) minus the double at `this+0x10` when connected, else 0.0; capped at 15.0 when the no-drop flag (`this+0x2C`) is set | as the decompile |

**Result.** The thresholds are doubles 5, 20 and 40 and the update interval is 4 seconds, so the 11.4186 values hold for 11.6096. The packet-loss accessor returns a float; its **units** are not settled by the accessor
(`CLinkStatusAverages::GetAveragePacketLoss` fills it from the server's heartbeat averages, not read): the tooltip text that shows it (`ID_LinkStatus_PacketLoss`, two decimals) is not built in M2, so nothing
uses it yet (`TODO(g4-link-units)`). Lifestone's own `LinkStats.LossPercent` is retail's running average in percent.

### 1.8 The vivid target indicator's globals and option bit (G4 g5b, D55)

Read-only, as §1.5 to §1.7: the `acclient.exe` SHA-256 was checked first (it matched), the file was opened for reading only and never run, and the bytes were read with the same short script (through the PE section table, plus a scan of the function's own bytes for the float loads). No byte dumps are kept.

| Function | 11.6096 address | What was read | Value |
|---|---|---|---|
| `VividTargetIndicator::CopyImage` | 0x004F6A70 | the global `s_BlitMethod` is read at 0x004F6ACB (`mov eax,[0x0081D640]`, then `dec eax`, a compare against 3 and a jump table at 0x004F6BD0 with four cases); the global's own 4 bytes | the dword at 0x0081D640 is **4**: the case that colours with `SurfaceWindow::BlitAndColor` in the colorize mode and the colour's 32-bit value (the other three cases, multiply, colorize and screen over a plain copy, are never taken) |
| `VividTargetIndicator::OnDraw` | 0x004F6EF0 | the function's `fld dword [abs]` loads and what they point at | the float constant `min_dist` is at **0x007C61F4**, bytes `00 00 00 41`: **8.0** (used at 0x004F7211; 0.0175 at 0x007C61F0 is the degree-to-radian factor, and 45, 90, 180, 270, 360 are shared constants). The decompile's literal 8.0 in the same function is the on-screen clamp, so both insets are 8 pixels |
| `PlayerModule::VividTargetingIndicator` | 0x005D3EE0 | the 13 bytes of the body | `mov eax,[ecx+0x8C]; shr eax,15; and eax,1; ret`: the option is **bit 15** of the options word (0x8000), as the 11.4186 decompile; `PlayerQualities.VividTargetingIndicatorOption` |

**Result.** The tint is the colorize blit (`ColoringLoopNoScale__BlitOp_Colorize_BlitFormat_Fill_BlitFormat_ARGB_`, whose 11.4186 body was read: the pixel's brightness painted in the colour's hue and saturation, alpha untouched), the off-screen arrow's inset is 8 pixels
and the option is bit 15. `VividColorize` (`Client/RetailUi/Selection/`) and `VividTargetMath.EdgeInset` carry these; their tests pin them. Not settled: the exact rounding when the 32-bit colour is made from the radar's float colour (`RGBAColor::GetColor32` is the existing
`UiRgbaColor.ToColor32`, truncating) and the colorize loop's handling of a colour with alpha below 1 (the radar colours are all opaque).

### 1.9 The logout dialog's properties and the busy pointer's precedence (G4 g6, D55)

Read-only, as §1.5 to §1.7: the `acclient.exe` SHA-256 was checked first (it matched), the file was opened for reading only and never run, and the bytes were read with the same short script (two reads). No byte dumps are kept.

| Function | 11.6096 address | What was read | Value |
|---|---|---|---|
| `gmGamePlayUI::MakeLogoutConfirmationDialog` | 0x004EC330 | it refuses when the member at `this+0xB4` (`m_logoutContext`) is not 0, clears the byte at `this+0xC7` (`m_bLogoutConfirmed`) and calls virtual slot 9 with 1 (the base UI's gameplay-modal state); then it builds the collection: `push 0x8E ... push 1; call [edx+0xAC]` (a value setter, slot 43) for property 0x8E, `push 0xC3 ... push 1; call [edx+0xAC]` for property 0xC3, and a StringInfo setter (slot 49, `[edx+0xC4]`) for property 0xC5 with the caller's string | **0x8E = 1** (a confirmation dialog), **0xC3 = 1** (`DialogFactory`'s immediate queue), 0xC5 = the StringInfo the caller passes (`ID_Client_EndCharacterSessionConfirm` from `RecvNotice_EndCharacterSession`, `ID_Client_LogoffConfirm` from `RecvNotice_Logoff`) |
| `ClientUISystem::UpdateCursorState` | 0x005653D0 | after `SmartBox::get_found_object_id` (`setnz`), the first test is `[this+0x14]` (`m_cBusy`) not 0: it then picks `(found ? 1 : 0) + 14` and jumps to the end; the target mode at `[this+0x2C]` is tested only after | **the busy pointer wins over the target mode**: cursor enum 14, or 15 over an object; the decompile's order was right |

**Result.** The dialog's two properties are both 1 (`GameLogout` sets the type through `UiDialogData.Of(Confirmation, ...)` and the queue with `WithQueue(DialogFactory.ImmediateQueue)`), and `UiBusyCount.CursorIndex` returns 14 or 15 while the count is up, ahead of every other pointer.
Not settled: the dat pointer image behind cursor enum 14 and 15 (`TODO(g6-busy-cursor-image)`), and what the virtual slot 9 call with 1 does beyond "gameplay-modal" (the research's reading; no behaviour here depends on it).

## 2. Address table

One row per function and overload, sorted by name. The columns:

- **TSV** is the table's confidence. "Not in TSV; bytes" means the 11.6096 address was settled in §1.
- **Tracks** are the plans that cite the function.
- **Design cites** gives the address the design wrote next to the function, if any, and which build it belongs to.
  "Wrong" means it is neither build's address (§5).
- `SmartArray::add_unique` is the instance LayoutDesc uses. The table has three more instances, in Logger.obj,
  Device.obj and gmRadarUI.obj.
- A wave-1 row carries the table's name where a track's document uses another. `FileNodeName_UInt32::Serialize`
  (0x0069D700 / 0x0069E510) is what the 11.4186 decompile and docs/retail-ui-data.md call `MediaDesc::Serialize`: the 2015
  linker folded the two bodies. `PositionManager::HandleExitWorld` (0x005551E0 / 0x00555E00) is docs/avatars.md's
  `UnStick`.

| Function | 11.4186 | 11.6096 | TSV | Tracks | Design cites |
|---|---|---|---|---|---|
| `CharGenState::GetAttributeName` | 0x005C3A20 | 0x005C4A00 | exact | retail-ui |  |
| `NumberInputFilter` | 0x004663F0 | 0x00466520 | verified | retail-ui |  |
| `UIElement_Text::AppendStringInfoWithFont` | 0x00469DE0 | 0x0046A070 | exact | retail-ui |  |
| `UIElement_Text::SetStringInfoWithFont` | 0x0046A0E0 | 0x0046A370 | exact | retail-ui |  |
| `gmCGHeritagePage::InitializePage` | 0x00483A10 | 0x00483DD0 | exact | retail-ui |  |
| `gmCGHeritagePage::Update` | 0x00483210 | 0x004835D0 | exact | retail-ui |  |
| `gmCGProfessionPage::ClearLocks` | 0x004820D0 | 0x00482490 | exact | retail-ui |  |
| `gmCGProfessionPage::GetSliderIndex` | 0x00481F90 | 0x00482350 | exact | retail-ui |  |
| `gmCGProfessionPage::InitializePage` | 0x00482D50 | 0x00483110 | exact | retail-ui |  |
| `gmCGProfessionPage::ListenToElementMessage` | 0x004829C0 | 0x00482D80 | exact | retail-ui |  |
| `gmCGProfessionPage::SetAttribValue` | 0x00482890 | 0x00482C50 | exact | retail-ui |  |
| `gmCGProfessionPage::SetLock` | 0x00482000 | 0x004823C0 | exact | retail-ui |  |
| `gmCGProfessionPage::Update` | 0x00482830 | 0x00482BF0 | exact | retail-ui |  |
| `gmCGProfessionPage::UpdateAttributeValues` | 0x00482450 | 0x00482810 | exact | retail-ui |  |
| `gmCGProfessionPage::UpdateProfession` | 0x004821B0 | 0x00482570 | exact | retail-ui |  |
| `gmCGProfessionPage::UpdateToDefaultAttributes` | 0x00482860 | 0x00482C20 | exact | retail-ui |  |
| `gmCGSkillsPage::DecreaseSkillLevel` | 0x00480D60 | 0x00481120 | exact | retail-ui |  |
| `gmCGSkillsPage::DoSkillRecords` | 0x004817E0 | 0x00481BA0 | exact | retail-ui |  |
| `gmCGSkillsPage::IncreaseSkillLevel` | 0x00480CA0 | 0x00481060 | exact | retail-ui |  |
| `gmCGSkillsPage::InitializePage` | 0x00481DD0 | 0x00482190 | exact | retail-ui |  |
| `gmCGSkillsPage::InsertEntrySorted` | 0x00480A40 | 0x00480E00 | exact | retail-ui |  |
| `gmCGSkillsPage::ListenToElementMessage` | 0x004814C0 | 0x00481880 | exact | retail-ui |  |
| `gmCGSkillsPage::MakeSkillFormula` | 0x00480E10 | 0x004811D0 | exact | retail-ui |  |
| `gmCGSkillsPage::SetSkillText` | 0x00480600 | 0x004809C0 | exact | retail-ui |  |
| `gmCGSkillsPage::ShowSkillsText` | 0x00481250 | 0x00481610 | exact | retail-ui |  |
| `gmCGSkillsPage::Update` | 0x00481DC0 | 0x00482180 | verified | retail-ui |  |
| `gmCGSkillsPage::UpdateAllTrainingValues` | 0x00480950 | 0x00480D10 | exact | retail-ui |  |
| `gmCGSkillsPage::UpdateCreditsMeter` | 0x004808F0 | 0x00480CB0 | exact | retail-ui |  |
| `gmCGSkillsPage::UpdateSkillEntry` | 0x00480BF0 | 0x00480FB0 | exact | retail-ui |  |
| `gmCharGenMainUI::CloseRandomizeWarningDialog` | 0x004E8400 | 0x004E9090 | verified | retail-ui |  |
| `gmCharGenMainUI::DoExit` | 0x004E8650 | 0x004E92E0 | exact | retail-ui |  |
| `gmCharGenMainUI::ListenToElementMessage` | 0x004E9450 | 0x004EA0E0 | exact | retail-ui |  |
| `gmCharGenMainUI::ListenToGlobalMessage` | 0x004E9000 | 0x004E9C90 | exact | retail-ui |  |
| `gmCharGenMainUI::MakeRandomizeWarningDialog` | 0x004E8A90 | 0x004E9720 | exact | retail-ui |  |
| `gmCharGenMainUI::MakeToDWarningDialog` | 0x004E8E40 | 0x004E9AD0 | exact | retail-ui |  |
| `gmCharGenMainUI::RecvNotice_CloseDialog` | 0x004E9780 | 0x004EA410 | exact | retail-ui |  |
| `gmCharGenMainUI::SetProgressState` | 0x004E7A10 | 0x004E86A0 | exact | retail-ui |  |
| `gmCharacterManagementUI::CloseDeleteCharacterDialog` | 0x004ED4A0 | 0x004EE130 | exact | retail-ui |  |
| `gmCharacterManagementUI::ListenToElementMessage` | 0x004ED5A0 | 0x004EE230 | exact | retail-ui |  |
| `gmCharacterManagementUI::MakeConfirmExitDialog` | 0x004ED250 | 0x004EDEE0 | exact | retail-ui |  |
| `gmCharacterManagementUI::MakeDeleteCharacterConfirmationDialog` | 0x004ECCA0 | 0x004ED930 | exact | retail-ui |  |
| `gmCharacterManagementUI::MakeEnteringWorldDialog` | 0x004ED090 | 0x004EDD20 | exact | retail-ui |  |
| `gmCharacterManagementUI::MakeErrorMessageDialog` | 0x004ECB10 | 0x004ED7A0 | exact | retail-ui |  |
| `gmCharacterManagementUI::MakePleaseWaitDialog` | 0x004ECED0 | 0x004EDB60 | exact | retail-ui |  |
| `gmCharacterManagementUI::OnAction` | 0x004ED410 | 0x004EE0A0 | verified | retail-ui |  |
| `gmCharacterManagementUI::RebuildCharacterList` | 0x004EC3A0 | 0x004ED030 | exact | retail-ui |  |
| `gmCharacterManagementUI::RecvNotice_BeginEnterWorld` | 0x004E79F0 | 0x004E8680 | verified | retail-ui |  |
| `gmCharacterManagementUI::RecvNotice_CharGenVerificationResponse` | 0x004EBF60 | 0x004ECBF0 | exact | retail-ui |  |
| `gmCharacterManagementUI::RecvNotice_CloseDialog` | 0x004ED760 | 0x004EE3F0 | exact | retail-ui |  |
| `gmCharacterManagementUI::ResetPreviouslySelectedCharacterSlot` | 0x004EBFF0 | 0x004ECC80 | exact | retail-ui |  |
| `gmCharacterManagementUI::SelectCharacter` | 0x004EC160 | 0x004ECDF0 | exact | retail-ui |  |
| `gmCharacterManagementUI::Update` | 0x004ECAB0 | 0x004ED740 | exact | retail-ui |  |
| `gmCharacterManagementUI::UpdateButtons` | 0x004EC240 | 0x004ECED0 | exact | retail-ui |  |
| `gmCharacterManagementUI::UpdateWorldName` | 0x004EC120 | 0x004ECDB0 | exact | retail-ui |  |
| `gmDataPatchUI::ListenToElementMessage` | 0x004EDE30 | 0x004EEAC0 | exact | retail-ui |  |
| `gmDataPatchUI::OnDDDEvent` | 0x004EDFF0 | 0x004EEC80 | exact | retail-ui |  |
| `gmDataPatchUI::SetConnectLevel` | 0x004EDE60 | 0x004EEAF0 | exact | retail-ui |  |
| `gmDataPatchUI::SetPatchLevel` | 0x004EDEE0 | 0x004EEB70 | exact | retail-ui |  |
| `gmDataPatchUI::UseTime` | 0x004EDF10 | 0x004EEBA0 | exact | retail-ui |  |
| `gmDisconnectedUI::ListenToElementMessage` | 0x004E99C0 | 0x004EA650 | exact | retail-ui |  |
| `gmDisconnectedUI::SetErrorMsg` | 0x004E99F0 | 0x004EA680 | verified | retail-ui |  |
| `gmEpilogueUI::ListenToElementMessage` | 0x004E9C40 | 0x004EA8D0 | verified | retail-ui |  |
| `gmEpilogueUI::ListenToGlobalMessage` | 0x004E9C30 | 0x004EA8C0 | verified | retail-ui |  |
| `UIFlow::Update` | 0x00479430 | 0x00479830 | verified | retail-ui |  |
| `UIFramework::QueueUIMode` | 0x006A0920 | 0x006A1740 | verified | retail-ui |  |
| `_stringmeta_lex` | 0x0067EAC0 | 0x0067FAC0 | exact | retail-ui |  |
| `ACCharGenData::FormatName` | 0x005BD5D0 | 0x005BE6A0 | exact | retail-ui |  |
| `ACCharGenData::GetHG` | 0x005C23A0 | 0x005C3380 | exact | retail-ui |  |
| `ACCharGenData::GetSkillSpecializedCost` | 0x005C27D0 | 0x005C37B0 | exact | retail-ui |  |
| `ACCharGenData::GetSkillTrainedCost` | 0x005C26D0 | 0x005C36B0 | exact | retail-ui |  |
| `ACCharGenResult::CG_Pack` | 0x005C7200 | 0x005C81E0 | exact | net, retail-ui |  |
| `ACCharGenResult::VerifyCharacterGenerationResult` | 0x005C79F0 | 0x005C89D0 | exact | retail-ui |  |
| `ACCharGenResult::VerifyCharacterGenerationResult` | 0x005C80E0 | 0x005C90C0 | verified | retail-ui |  |
| `ACCmdInterp::FinishJump` | 0x0058B1D0 | 0x0058C000 | verified | physics |  |
| `ACCmdInterp::OnAction` | 0x0058B370 | 0x0058C1A0 | exact | physics |  |
| `ACCmdInterp::SetMotion` | 0x0058B310 | 0x0058C140 | exact | physics |  |
| `ACCmdInterp::TakeControlFromServer` | 0x0058B160 | 0x0058BF90 | verified | physics |  |
| `ACCObjectMaint::CreateObject` | 0x00558870 | 0x005594B0 | exact | avatars |  |
| `ACCObjectMaint::SetVisualDesc` | 0x00557660 | 0x005582A0 | verified | avatars |  |
| `ActionMap::GetConflictingInputMaps` | 0x00684EE0 | 0x00685F30 | exact | retail-ui |  |
| `ActionMap::IsUserBindable` | 0x006854E0 | 0x00686530 | exact | retail-ui |  |
| `ActionMap::Serialize` | 0x00685F10 | 0x00686F60 | exact | retail-ui |  |
| `ActionMapValue::Serialize` | 0x00684A30 | 0x00685A80 | exact | retail-ui |  |
| `AnimPartChange::UnPack` | 0x005ADA10 | 0x005AEAC0 | exact | avatars |  |
| `ArrayPropertyValue::Serialize` | 0x00427FD0 | 0x004282C0 | verified | retail-ui |  |
| `BaseProperty::Serialize` | 0x0042A090 | 0x0042A330 | exact | retail-ui |  |
| `BSPNODE::sphere_intersects_poly` | 0x0053CA30 | 0x0053D790 | exact | physics |  |
| `BSPNODE::UnPack` | 0x0053C540 | 0x0053D2A0 | exact | physics |  |
| `BSPTREE::adjust_to_plane` | 0x00539BF0 | 0x0053A950 | exact | physics |  |
| `CameraManager::UpdateCamera` | 0x00456660 | 0x00456780 | exact | godot-world | 0x00456660 (11.4186) |
| `CameraSet::Closer` | 0x004586D0 | 0x004587F0 | exact | godot-world | 0x004586D0 (11.4186) |
| `CameraSet::Farther` | 0x00458890 | 0x004589B0 | exact | godot-world | 0x00458890 (11.4186) |
| `CameraSet::InHead` | 0x00457450 | 0x00457570 | exact | avatars |  |
| `CameraSet::InHead` | 0x00457680 | 0x004577A0 | exact | avatars |  |
| `CameraSet::Lower` | 0x00457CF0 | 0x00457E10 | exact | godot-world | 0x00457CF0 (11.4186) |
| `CameraSet::MouseLookHandler` | 0x00458D80 | 0x00458EA0 | exact | godot-world |  |
| `CameraSet::Raise` | 0x00457980 | 0x00457AA0 | exact | godot-world | 0x00457980 (11.4186) |
| `CameraSet::Rotate` | 0x00458310 | 0x00458430 | exact | godot-world | 0x00458310 (11.4186) |
| `CameraSet::SetDefaultOffsets` | 0x00458F80 | 0x004590A0 | exact | godot-world |  |
| `CameraSet::SetInHead` | 0x00458CE0 | 0x00458E00 | exact | godot-world |  |
| `CameraSet::UpdateCamera` | 0x00458AE0 | 0x00458C00 | exact | godot-world | 0x00458AE0 (11.4186) |
| `CellManager::PreFetchCells` | 0x00455820 | 0x00455940 | exact | portal |  |
| `CEnvCell::PreFetchCells` | 0x0052C460 | 0x0052D070 | exact | portal |  |
| `CEnvCell::PreFetchCells` | 0x0052D1E0 | 0x0052DE40 | exact | portal |  |
| `CharGenState::ApplyDefaultTemplate` | 0x005C4990 | 0x005C5970 | exact | retail-ui |  |
| `CharGenState::ApplyTemplate` | 0x005C5080 | 0x005C6060 | exact | retail-ui |  |
| `CharGenState::BalanceAttributes` | 0x005C3DF0 | 0x005C4DD0 | exact | retail-ui |  |
| `CharGenState::CharGenState` | 0x005C6A50 | 0x005C7A30 | exact | retail-ui |  |
| `CharGenState::ConstrainAllByGender` | 0x005C5B80 | 0x005C6B60 | exact | retail-ui |  |
| `CharGenState::ConstrainAllByHeritage` | 0x005C6590 | 0x005C7570 | exact | retail-ui |  |
| `CharGenState::FitTemplateToCharacter` | 0x005C6130 | 0x005C7110 | exact | retail-ui |  |
| `CharGenState::GetAbsRemainingCredits` | 0x005C3B20 | 0x005C4B00 | exact | retail-ui |  |
| `CharGenState::GetAttribute` | 0x005C39B0 | 0x005C4990 | exact | retail-ui |  |
| `CharGenState::GetBaldState` | 0x005C52A0 | 0x005C6280 | exact | avatars, retail-ui |  |
| `CharGenState::GetCharGenResult` | 0x005C4030 | 0x005C5010 | exact | retail-ui |  |
| `CharGenState::GetFootwearPaletteTemplateID` | 0x005C3980 | 0x005C4960 | exact | avatars |  |
| `CharGenState::GetHeadgearPaletteTemplateID` | 0x005C38F0 | 0x005C48D0 | exact | avatars |  |
| `CharGenState::GetRandomReal` | 0x00563940 | 0x005646E0 | verified | retail-ui |  |
| `CharGenState::GetSetupID` | 0x005C5A90 | 0x005C6A70 | exact | avatars, retail-ui |  |
| `CharGenState::GetShirtPaletteTemplateID` | 0x005C3920 | 0x005C4900 | exact | avatars |  |
| `CharGenState::GetSkillLevel` | 0x005C3C00 | 0x005C4BE0 | verified | retail-ui |  |
| `CharGenState::GetSkillScore` | 0x005C4B50 | 0x005C5B30 | exact | retail-ui |  |
| `CharGenState::GetTrousersPaletteTemplateID` | 0x005C3950 | 0x005C4930 | exact | avatars |  |
| `CharGenState::LockAttribute` | 0x005C3BE0 | 0x005C4BC0 | verified | retail-ui |  |
| `CharGenState::RandomizeAppearance` | 0x005C4F10 | 0x005C5EF0 | exact | retail-ui |  |
| `CharGenState::RandomizeCharacter` | 0x005C6D80 | 0x005C7D60 | exact | retail-ui |  |
| `CharGenState::RandomizeClothing` | 0x005C6770 | 0x005C7750 | exact | retail-ui |  |
| `CharGenState::RandomizeFootwear` | 0x005C6070 | 0x005C7050 | exact | retail-ui |  |
| `CharGenState::RandomizeHeadgear` | 0x005C5E10 | 0x005C6DF0 | exact | retail-ui |  |
| `CharGenState::RandomizeHeritageGroup` | 0x005C6A20 | 0x005C7A00 | exact | retail-ui |  |
| `CharGenState::RandomizeSkills` | 0x005C57E0 | 0x005C67C0 | exact | retail-ui |  |
| `CharGenState::RandomizeStartArea` | 0x005C59E0 | 0x005C69C0 | exact | retail-ui |  |
| `CharGenState::RandomizeTemplate` | 0x005C6500 | 0x005C74E0 | exact | retail-ui |  |
| `CharGenState::Reset` | 0x005C68A0 | 0x005C7880 | exact | retail-ui |  |
| `CharGenState::ResetAttributeLock` | 0x005C3BC0 | 0x005C4BA0 | verified | retail-ui |  |
| `CharGenState::ResetSkillLevels` | 0x005C43B0 | 0x005C5390 | exact | retail-ui |  |
| `CharGenState::SetEyeColor` | 0x005C38D0 | 0x005C48B0 | verified | retail-ui |  |
| `CharGenState::SetFootwearStyle` | 0x005C56C0 | 0x005C66A0 | exact | retail-ui |  |
| `CharGenState::SetGender` | 0x005C64A0 | 0x005C7480 | exact | retail-ui |  |
| `CharGenState::SetHeadgearStyle` | 0x005C5350 | 0x005C6330 | exact | retail-ui |  |
| `CharGenState::SetHeritageGroup` | 0x005C67A0 | 0x005C7780 | exact | retail-ui |  |
| `CharGenState::SetName` | 0x005C3D10 | 0x005C4CF0 | exact | retail-ui |  |
| `CharGenState::SetSelf` | 0x005C48E0 | 0x005C58C0 | exact | retail-ui |  |
| `CharGenState::SetSkillLevel` | 0x005C3C20 | 0x005C4C00 | exact | retail-ui |  |
| `CharGenState::SetSkinShade` | 0x005C3850 | 0x005C4830 | exact | retail-ui |  |
| `CharGenState::SetStartArea` | 0x005C4000 | 0x005C4FE0 | exact | retail-ui |  |
| `CharGenState::SetStrength` | 0x005C4660 | 0x005C5640 | exact | retail-ui |  |
| `CharGenState::SetTemplate` | 0x005C5A60 | 0x005C6A40 | exact | retail-ui |  |
| `CharGenState::StoreColorInformation` | 0x005C44D0 | 0x005C54B0 | exact | avatars, retail-ui |  |
| `CharGenState::UpdateRemainingSkillCredits` | 0x005C37C0 | 0x005C47A0 | exact | retail-ui |  |
| `CInputMap::Serialize` | 0x0068EE60 | 0x0068FDB0 | exact | retail-ui |  |
| `CLandCell::add_cell_block` | 0x005331D0 | 0x00533F10 | exact | physics | 0x00532F20 (wrong: §5) |
| `ClientCharGenState::UpdateTrueFacePal` | 0x00563A20 | 0x005647C0 | exact | retail-ui |  |
| `ClientCombatSystem::CommenceJump` | 0x0056AF90 | 0x0056BCD0 | exact | physics |  |
| `ClientCombatSystem::DoJump` | 0x0056B110 | 0x0056BE50 | not in TSV; bytes (§1.2) | lead, physics | 0x0056B110 (11.4186) |
| `ClientCombatSystem::GetJumpPowerLevel` | 0x0056B050 | 0x0056BD90 | exact | physics | 0x0056B050 (11.4186) |
| `ClientCombatSystem::GetPowerBarLevel` | 0x0056ADE0 | 0x0056BB20 | exact | lead, physics | 0x0056ADE0 (11.4186) |
| `ClientNet::ProcessConnection` | 0x00545450 | 0x00546010 | exact | net | 0x00545450 (11.4186) |
| `ClientUISystem::GetUISoundTable` | 0x00563FB0 | 0x00564D50 | exact | portal |  |
| `ClothingBase::ApplyPartAndTextureChanges` | 0x005A8EB0 | 0x005A9F60 | exact | avatars |  |
| `ClothingTable::BuildObjDesc` | 0x005A7900 | 0x005A89B0 | exact | avatars | 0x005A7900 (11.4186) |
| `ClothingTable::GetCloPaletteTemplate` | 0x005A74B0 | 0x005A85F0 | exact | retail-ui |  |
| `CM_Combat::Event_QueryHealth` | 0x006A9B40 | 0x006AAAA0 | exact | net |  |
| `CM_Communication::Event_TalkDirectByName` | 0x006A4780 | 0x006A55A0 | exact | net |  |
| `CM_Inventory::Event_UseEvent` | 0x006AC3B0 | 0x006AD310 | exact | net |  |
| `CMasterInputMap::MetaModeFromKey` | 0x0068D850 | 0x0068E7A0 | exact | retail-ui |  |
| `CMasterInputMap::Serialize` | 0x0068FCE0 | 0x00690C30 | exact | retail-ui |  |
| `CMotionInterp::adjust_motion` | 0x00528010 | 0x00528C20 | exact | avatars | 0x00528010 (11.4186) |
| `CMotionInterp::apply_current_movement` | 0x00528870 | 0x00529480 | exact | avatars |  |
| `CMotionInterp::apply_interpreted_movement` | 0x00528600 | 0x00529210 | exact | avatars, physics | 0x00528600 (11.4186) |
| `CMotionInterp::apply_raw_movement` | 0x005287E0 | 0x005293F0 | exact | avatars | 0x005287E0 (11.4186) |
| `CMotionInterp::apply_run_to_command` | 0x00527BE0 | 0x005287F0 | exact | avatars | 0x00527BE0 (11.4186) |
| `CMotionInterp::charge_jump` | 0x005281C0 | 0x00528DD0 | predicted | avatars, physics | 0x005281C0 (11.4186) |
| `CMotionInterp::contact_allows_move` | 0x00528240 | 0x00528E50 | exact | avatars, physics | 0x00528240 (11.4186) |
| `CMotionInterp::Create` | 0x00528C00 | 0x00529810 | exact | avatars |  |
| `CMotionInterp::DoInterpretedMotion` | 0x00528360 | 0x00528F70 | exact | avatars | 0x00528360 (11.4186) |
| `CMotionInterp::DoMotion` | 0x00528D20 | 0x00529930 | exact | avatars | 0x00528D20 (11.4186) |
| `CMotionInterp::enter_default_state` | 0x00528C80 | 0x00529890 | exact | avatars | 0x00528C80 (11.4186) |
| `CMotionInterp::get_adjusted_max_speed` | 0x00527D00 | 0x00528910 | exact | avatars | 0x00527D00 (11.4186) |
| `CMotionInterp::get_jump_v_z` | 0x00527AA0 | 0x005286B0 | exact | avatars, physics |  |
| `CMotionInterp::get_leave_ground_velocity` | 0x005280C0 | 0x00528CD0 | exact | avatars, physics |  |
| `CMotionInterp::get_max_speed` | 0x00527CB0 | 0x005288C0 | exact | avatars | 0x00527CB0 (11.4186) |
| `CMotionInterp::get_state_velocity` | 0x00527D50 | 0x00528960 | exact | avatars | 0x00527D50 (11.4186) |
| `CMotionInterp::HitGround` | 0x00528AC0 | 0x005296D0 | exact | avatars, physics | 0x00528AC0 (11.4186) |
| `CMotionInterp::is_standing_still` | 0x00527FA0 | 0x00528BB0 | exact | avatars |  |
| `CMotionInterp::jump` | 0x00528780 | 0x00529390 | exact | avatars, physics |  |
| `CMotionInterp::jump_charge_is_allowed` | 0x00527A50 | **unmapped** | not in TSV | physics |  |
| `CMotionInterp::jump_is_allowed` | 0x005282B0 | 0x00528EC0 | exact | avatars |  |
| `CMotionInterp::LeaveGround` | 0x00528B00 | 0x00529710 | exact | avatars, physics | 0x00528B00 (11.4186) |
| `CMotionInterp::motion_allows_jump` | 0x005279E0 | 0x005285E0 | predicted | avatars, physics |  |
| `CMotionInterp::move_to_interpreted_state` | 0x005289C0 | 0x005295D0 | exact | avatars | 0x005289C0 (11.4186) |
| `CMotionInterp::PerformMovement` | 0x00528E80 | 0x00529A90 | exact | avatars |  |
| `CMotionInterp::ReportExhaustion` | 0x005288D0 | 0x005294E0 | exact | avatars |  |
| `CMotionInterp::set_hold_run` | 0x00528B70 | 0x00529780 | exact | avatars |  |
| `CMotionInterp::SetHoldKey` | 0x00528BB0 | 0x005297C0 | exact | avatars |  |
| `CMotionInterp::SetWeenieObject` | 0x00528920 | 0x00529530 | exact | avatars |  |
| `CMotionInterp::StopCompletely` | 0x00527E40 | 0x00528A50 | exact | avatars |  |
| `CMotionInterp::StopInterpretedMotion` | 0x00528470 | 0x00529080 | exact | avatars | 0x00528470 (11.4186) |
| `CMotionInterp::StopMotion` | 0x00528530 | 0x00529140 | exact | avatars |  |
| `CObjCell::hide_object` | 0x0052BE30 | 0x0052CA40 | verified | avatars |  |
| `CObjCell::unhide_object` | 0x0052BE40 | 0x0052CA50 | exact | avatars |  |
| `CObjectMaint::QueueBlobForObject` | 0x005092D0 | 0x00509DA0 | exact | avatars |  |
| `CObjectMaint::SetChildren` | 0x00509370 | 0x00509E40 | exact | avatars |  |
| `CommandInterpreter::AddCommand` | 0x006B39A0 | 0x006B48E0 | exact | physics |  |
| `CommandInterpreter::ApplyCurrentMovement` | 0x006B3430 | 0x006B4370 | exact | physics |  |
| `CommandInterpreter::ApplyHoldKeysToCommand` | 0x006B3340 | 0x006B4280 | exact | physics |  |
| `CommandInterpreter::BookkeepCommandAndModifyIfNecessary` | 0x006B3AD0 | 0x006B4A10 | exact | physics |  |
| `CommandInterpreter::CommandInterpreter` | 0x006B3E40 | 0x006B4D80 | exact | lead, physics | 0x006B3E40 (11.4186) |
| `CommandInterpreter::Disable` | 0x006B3250 | 0x006B4190 | exact | physics |  |
| `CommandInterpreter::Enable` | 0x006B3290 | 0x006B41D0 | verified | physics |  |
| `CommandInterpreter::HandleKeyboardCommand` | 0x006B3690 | 0x006B45D0 | exact | physics |  |
| `CommandInterpreter::HandleNewForwardMovement` | 0x006B3D60 | 0x006B4CA0 | verified | physics |  |
| `CommandInterpreter::LoseControlToServer` | 0x006B3170 | 0x006B40B0 | exact | physics | 0x006B3170 (11.4186) |
| `CommandInterpreter::LoseKeyboardFocus` | 0x006B31F0 | 0x006B4130 | exact | physics |  |
| `CommandInterpreter::MovePlayer` | 0x006B3F40 | 0x006B4E80 | exact | physics |  |
| `CommandInterpreter::NukeCommand` | 0x006B3A20 | 0x006B4960 | exact | physics |  |
| `CommandInterpreter::PlayerTeleported` | 0x006B32B0 | 0x006B41F0 | verified | physics |  |
| `CommandInterpreter::SendMovementEvent` | 0x006B4680 | 0x006B55C0 | exact | physics | 0x006B4680 (11.4186) |
| `CommandInterpreter::SendPositionEvent` | 0x006B4770 | 0x006B56B0 | exact | lead, physics | 0x006B4770 (11.4186) |
| `CommandInterpreter::SetAutoRun` | 0x006B4850 | 0x006B5790 | exact | physics |  |
| `CommandInterpreter::SetHoldRun` | 0x006B3370 | 0x006B42B0 | exact | physics |  |
| `CommandInterpreter::SetHoldSidestep` | 0x006B3410 | 0x006B4350 | exact | physics |  |
| `CommandInterpreter::ShouldSendPositionEvent` | 0x006B45E0 | 0x006B5520 | exact | physics, state |  |
| `CommandInterpreter::StopCompletely` | 0x006B3B50 | 0x006B4A90 | exact | physics |  |
| `CommandInterpreter::TakeControlFromServer` | 0x006B32D0 | 0x006B4210 | exact | physics | 0x006B32D0 (11.4186) |
| `CommandInterpreter::ToggleAutoRun` | 0x006B3CC0 | 0x006B4C00 | verified | physics |  |
| `CommandInterpreter::UsePositionFromServer` | 0x006B3B40 | 0x006B4A80 | verified | physics | 0x006B3B40 (11.4186) |
| `CommandInterpreter::UseTime` | 0x006B3BF0 | 0x006B4B30 | exact | physics |  |
| `CommandInterpreter::WhichList` | 0x006B3950 | 0x006B4890 | exact | physics |  |
| `CommandList::AddCommand` | 0x006B4B80 | 0x006B5AC0 | exact | physics |  |
| `CommandList::RemoveCommand` | 0x006B4960 | 0x006B58A0 | exact | physics |  |
| `compute_str_hash` | 0x00413110 | 0x004016B0 | exact | retail-ui |  |
| `ConstraintManager::adjust_offset` | 0x00556180 | 0x00556DA0 | exact | avatars, physics | 0x00556180 (11.4186) |
| `ConstraintManager::ConstrainTo` | 0x00556240 | **unmapped** | not in TSV | physics | 0x00556240 (11.4186) |
| `ContentProfile::operator==` | 0x006AFEB0 | 0x006B0D80 | verified | avatars |  |
| `ControlActivationTypeToString` | 0x0068A940 | 0x0068B940 | exact | retail-ui |  |
| `ControlSpecification::ToFileNode` | 0x0068AB80 | 0x0068BB80 | exact | retail-ui |  |
| `CPartArray::DestroyPals` | 0x00518210 | 0x00518D40 | exact | avatars |  |
| `CPartArray::DoInterpretedMotion` | 0x00518750 | 0x00519280 | exact | avatars |  |
| `CPartArray::DoObjDescChanges` | 0x00519210 | 0x00519D40 | exact | avatars | 0x00519210 (11.4186) |
| `CPartArray::DoObjDescChangesFromDefault` | 0x00519480 | 0x00519FB0 | exact | avatars | 0x00519480 (11.4186) |
| `CPartArray::GetHeight` | 0x005180B0 | 0x00518BE0 | verified | avatars |  |
| `CPartArray::GetNumSphere` | 0x00518060 | 0x00518B90 | predicted | avatars |  |
| `CPartArray::GetRadius` | 0x005180A0 | 0x00518BD0 | verified | avatars |  |
| `CPartArray::HandleExitWorld` | 0x00517D90 | 0x005188C0 | verified | avatars, physics |  |
| `CPartArray::HandleMovement` | 0x00517D60 | 0x00518890 | verified | avatars |  |
| `CPartArray::InitDefaults` | 0x00518980 | 0x005194B0 | exact | avatars |  |
| `CPartArray::InitObjDescChanges` | 0x00518540 | 0x00519070 | exact | avatars |  |
| `CPartArray::InitPals` | 0x005181C0 | 0x00518CF0 | exact | avatars |  |
| `CPartArray::InitParts` | 0x00517F40 | 0x00518A70 | exact | avatars |  |
| `CPartArray::RemoveLinkAnimations` | 0x00517D70 | 0x005188A0 | verified | avatars |  |
| `CPartArray::SetNoDrawInternal` | 0x00518300 | 0x00518E30 | verified | avatars |  |
| `CPartArray::SetPalette` | 0x00518E10 | 0x00519940 | exact | avatars | 0x00518E10 (11.4186) |
| `CPartArray::SetPart` | 0x00518580 | 0x005190B0 | exact | avatars | 0x00518580 (11.4186) |
| `CPartArray::SetPlacementFrame` | 0x005193D0 | 0x00519F00 | exact | avatars |  |
| `CPartArray::SetTextureMap` | 0x005185E0 | 0x00519110 | exact | avatars |  |
| `CPartArray::StopCompletelyInternal` | 0x00518890 | 0x005193C0 | exact | avatars |  |
| `CPartArray::StopInterpretedMotion` | 0x005187F0 | 0x00519320 | exact | avatars |  |
| `CPhysics::SetObjectMovement` | 0x00509690 | 0x0050A160 | exact | avatars | 0x00509690 (11.4186) |
| `CPhysics::SetObjectMovement` | 0x00509790 | 0x0050A260 | exact | avatars | 0x00509690 (11.4186) |
| `CPhysics::UseTime` | 0x00509950 | 0x0050A420 | exact | physics |  |
| `CPhysicsObj::add_child` | 0x0050F870 | 0x00510340 | exact | avatars | 0x0050F870 (11.4186) |
| `CPhysicsObj::add_child` | 0x0050F8F0 | 0x005103C0 | exact | avatars | 0x0050F870 (11.4186) |
| `CPhysicsObj::add_voyeur` | 0x0050EE00 | 0x0050F8D0 | exact | avatars |  |
| `CPhysicsObj::animate_static_object` | 0x00513DF0 | 0x005148F0 | exact | avatars |  |
| `CPhysicsObj::cancel_moveto` | 0x005101F0 | 0x00510CC0 | verified | avatars |  |
| `CPhysicsObj::clear_sequence_anims` | 0x0050F760 | 0x00510230 | verified | avatars, portal |  |
| `CPhysicsObj::clear_target` | 0x0050ED90 | 0x0050F860 | verified | avatars |  |
| `CPhysicsObj::ConstrainTo` | 0x00510520 | 0x00510FF0 | verified | avatars |  |
| `CPhysicsObj::DoObjDescChanges` | 0x0050FA30 | 0x00510500 | verified | avatars |  |
| `CPhysicsObj::DoObjDescChangesFromDefault` | 0x0050F9B0 | 0x00510480 | verified | avatars |  |
| `CPhysicsObj::exit_world` | 0x00514E60 | 0x00515960 | exact | avatars |  |
| `CPhysicsObj::get_curr_frame_number` | 0x0050F780 | 0x00510250 | verified | godot-world, portal |  |
| `CPhysicsObj::get_heading` | 0x00511540 | 0x00512010 | verified | avatars, physics |  |
| `CPhysicsObj::get_target_quantum` | 0x0050EDC0 | 0x0050F890 | verified | avatars |  |
| `CPhysicsObj::get_velocity` | 0x005113C0 | 0x00511E90 | exact | avatars |  |
| `CPhysicsObj::GetAutonomyBlipDistance` | 0x0050EB70 | 0x0050F640 | verified | avatars | 0x0050EB70 (11.4186) |
| `CPhysicsObj::GetMaxConstraintDistance` | 0x0050EC10 | 0x0050F6E0 | verified | avatars, physics | 0x0050EC10 (11.4186) |
| `CPhysicsObj::GetStartConstraintDistance` | 0x0050EBC0 | 0x0050F690 | verified | avatars, physics | 0x0050EBC0 (11.4186) |
| `CPhysicsObj::HandleUpdateTarget` | 0x00512BC0 | 0x005136C0 | exact | avatars |  |
| `CPhysicsObj::HasAnims` | 0x0050F770 | 0x00510240 | verified | avatars |  |
| `CPhysicsObj::InitDefaults` | 0x005139D0 | 0x005144D0 | exact | avatars |  |
| `CPhysicsObj::InterpolateTo` | 0x005104F0 | 0x00510FC0 | exact | avatars |  |
| `CPhysicsObj::is_newer` | 0x00451AD0 | 0x00451B10 | exact | lead, state | 0x00451AD0 (11.4186) |
| `CPhysicsObj::leave_world` | 0x005155A0 | 0x005160A0 | exact | avatars |  |
| `CPhysicsObj::MakeMovementManager` | 0x00510270 | 0x00510D40 | exact | avatars |  |
| `CPhysicsObj::makeObject` | 0x00513970 | 0x00514470 | exact | avatars |  |
| `CPhysicsObj::MakePositionManager` | 0x00510210 | 0x00510CE0 | exact | avatars |  |
| `CPhysicsObj::MoveOrTeleport` | 0x00516330 | 0x00516E30 | exact | avatars | 0x00516330 (11.4186) |
| `CPhysicsObj::MoveToObject` | 0x00512860 | 0x00513360 | exact | avatars |  |
| `CPhysicsObj::newer_event` | 0x00451B10 | 0x00451B50 | exact | state | 0x00451B10 (11.4186) |
| `CPhysicsObj::prepare_to_enter_world` | 0x00511FA0 | 0x00512A70 | exact | avatars |  |
| `CPhysicsObj::receive_target_update` | 0x0050EDE0 | 0x0050F8B0 | verified | avatars |  |
| `CPhysicsObj::remove_voyeur` | 0x0050EE50 | 0x0050F920 | verified | avatars |  |
| `CPhysicsObj::RemoveLinkAnimations` | 0x0050FE20 | 0x005108F0 | verified | avatars |  |
| `CPhysicsObj::set_active` | 0x0050FC40 | 0x00510710 | exact | avatars |  |
| `CPhysicsObj::set_description` | 0x00514F40 | 0x00515A40 | exact | avatars |  |
| `CPhysicsObj::set_heading` | 0x00514160 | 0x00514C60 | exact | avatars |  |
| `CPhysicsObj::set_hidden` | 0x00514C60 | 0x00515760 | exact | avatars, godot-world, portal | 0x00515760 (11.6096) |
| `CPhysicsObj::set_lights` | 0x0050FCF0 | 0x005107C0 | exact | avatars |  |
| `CPhysicsObj::set_local_velocity` | 0x005114D0 | 0x00511FA0 | exact | avatars |  |
| `CPhysicsObj::set_nodraw` | 0x0050FCA0 | 0x00510770 | exact | avatars |  |
| `CPhysicsObj::set_on_walkable` | 0x00511310 | 0x00511DE0 | exact | avatars |  |
| `CPhysicsObj::set_parent` | 0x00515A90 | 0x00516590 | exact | avatars | 0x00515A90 (11.4186) |
| `CPhysicsObj::set_parent` | 0x00515B50 | 0x00516650 | exact | avatars | 0x00515A90 (11.4186) |
| `CPhysicsObj::set_sequence_animation` | 0x0050F6F0 | 0x005101C0 | exact | avatars, godot-world, portal | 0x005101C0 (11.6096) |
| `CPhysicsObj::set_state` | 0x00514DD0 | 0x005158D0 | exact | avatars, portal | 0x00514DD0 (11.4186) |
| `CPhysicsObj::set_target` | 0x0050ED30 | 0x0050F800 | exact | avatars |  |
| `CPhysicsObj::set_target_quantum` | 0x0050EDA0 | 0x0050F870 | verified | avatars |  |
| `CPhysicsObj::set_velocity` | 0x005113F0 | 0x00511EC0 | exact | avatars |  |
| `CPhysicsObj::SetMotionTableID` | 0x00512780 | 0x00513280 | exact | avatars |  |
| `CPhysicsObj::SetPlacementFrame` | 0x00511380 | 0x00511E50 | exact | avatars |  |
| `CPhysicsObj::SetPosition` | 0x005160C0 | 0x00516BC0 | exact | avatars |  |
| `CPhysicsObj::SetPositionSimple` | 0x005162B0 | 0x00516DB0 | exact | avatars |  |
| `CPhysicsObj::stick_to_object` | 0x005127E0 | 0x005132E0 | exact | avatars |  |
| `CPhysicsObj::StopCompletely` | 0x00510180 | 0x00510C50 | exact | avatars |  |
| `CPhysicsObj::teleport_hook` | 0x00514ED0 | 0x005159D0 | exact | avatars |  |
| `CPhysicsObj::transition` | 0x00512DC0 | 0x005138C0 | exact | avatars |  |
| `CPhysicsObj::TurnToHeading` | 0x00512980 | 0x00513480 | exact | avatars |  |
| `CPhysicsObj::TurnToObject` | 0x00512940 | 0x00513440 | exact | avatars |  |
| `CPhysicsObj::unpack_movement` | 0x00512040 | 0x00512B10 | exact | avatars |  |
| `CPhysicsObj::unset_parent` | 0x00513470 | 0x00513F70 | exact | avatars |  |
| `CPhysicsObj::unstick_from_object` | 0x0050EAE0 | 0x0050F5B0 | verified | avatars |  |
| `CPhysicsObj::update_object` | 0x00515D10 | 0x00516810 | exact | avatars, physics |  |
| `CPhysicsObj::update_position` | 0x00514520 | 0x00515020 | exact | avatars, portal |  |
| `CPhysicsObj::UpdateChild` | 0x00512D50 | 0x00513850 | exact | avatars | 0x00512D50 (11.4186) |
| `CPhysicsObj::UpdateChildrenInternal` | 0x00513DA0 | 0x005148A0 | exact | avatars |  |
| `CPhysicsObj::UpdateObjectInternal` | 0x005156B0 | 0x005161B0 | exact | avatars |  |
| `CPhysicsObj::UpdatePhysicsInternal` | 0x00510700 | 0x005111D0 | exact | avatars |  |
| `CPhysicsObj::UpdatePositionInternal` | 0x00512C30 | 0x00513730 | exact | avatars | 0x00512C30 (11.4186) |
| `CPhysicsPart::CopySurfaces` | 0x0050D6A0 | 0x0050E170 | exact | avatars |  |
| `CPhysicsPart::DetermineBasePal` | 0x0050DC80 | 0x0050E750 | exact | avatars | 0x0050DC80 (11.4186) |
| `CPhysicsPart::InitObjDescChanges` | 0x0050E3B0 | 0x0050EE80 | exact | avatars |  |
| `CPhysicsPart::LoadGfxObjArray` | 0x0050DCF0 | 0x0050E7C0 | exact | avatars, physics |  |
| `CPhysicsPart::RestorePalette` | 0x0050D5B0 | 0x0050E080 | exact | avatars |  |
| `CPhysicsPart::RestoreSurfaces` | 0x0050D730 | 0x0050E200 | exact | avatars |  |
| `CPhysicsPart::SetGfxObjArray` | 0x0050E2E0 | 0x0050EDB0 | exact | avatars | 0x0050E2E0 (11.4186) |
| `CPhysicsPart::SetPart` | 0x0050E700 | 0x0050F1D0 | exact | avatars | 0x0050E700 (11.4186) |
| `CPhysicsPart::SetTextureMap` | 0x0050DE30 | 0x0050E900 | exact | avatars | 0x0050DE30 (11.4186) |
| `CPhysicsPart::UpdateViewerDistance` | 0x0050E030 | 0x0050EB00 | exact | avatars |  |
| `CPhysicsPart::UsePalette` | 0x0050DED0 | 0x0050E9A0 | exact | avatars |  |
| `CPlayerSystem::AttemptSendLoginCompleteNotification` | 0x00562F20 | 0x00563CC0 | exact | portal |  |
| `CPlayerSystem::RequestLogOff` | 0x00562DD0 | 0x00563B70 | exact | godot-world, portal, state | 0x00563B70 (11.6096) |
| `CPlayerSystem::SendLoginCompleteNotification` | 0x00562E90 | 0x00563C30 | exact | godot-world, portal |  |
| `CPlayerSystem::SetLogOffStarted` | 0x0055D7D0 | 0x0055E4F0 | verified | portal |  |
| `CPlayerSystem::SetTeleportInProgress` | 0x0055D580 | 0x0055E2A0 | exact | godot-world, portal | 0x0055E2A0 (11.6096) |
| `CPolygon::adjust_sphere_to_poly` | 0x00538170 | 0x00538EB0 | exact | physics |  |
| `CreatureMode::AddLight` | 0x004555B0 | 0x00455650 | exact | godot-world, portal |  |
| `CreatureMode::CreatureMode` | 0x00454390 | 0x00454430 | exact | godot-world, portal | 0x00454430 (11.6096) |
| `CreatureMode::Render` | 0x004529D0 | 0x00452A10 | exact | avatars, godot-world, portal | 0x00452A10 (11.6096) |
| `CreatureMode::SetCameraDirection_Degrees` | 0x00453760 | 0x004537A0 | exact | portal |  |
| `CSequence::clear` | 0x005255B0 | 0x005261B0 | verified | portal |  |
| `CSoundTable::Lookup` | 0x00552100 | 0x00552D20 | verified | portal |  |
| `CSoundTable::UnPack` | 0x00551CD0 | 0x005528F0 | exact | portal |  |
| `CSurface::GetOriginalPaletteID` | 0x005363F0 | 0x00537130 | verified | avatars |  |
| `CSurface::GetOriginalTextureMapID` | 0x005363E0 | 0x00537120 | verified | avatars |  |
| `CSurface::InitEnd` | 0x00536400 | 0x00537140 | exact | avatars |  |
| `CSurface::Serialize` | 0x005365F0 | 0x00537330 | exact | avatars |  |
| `CSurface::UsePalette` | 0x00535F20 | 0x00536C60 | exact | avatars |  |
| `CSurface::UseTextureMap` | 0x005362E0 | 0x00537020 | exact | avatars |  |
| `CTransition::check_walkable` | 0x0050AFF0 | 0x0050BAC0 | exact | physics |  |
| `CTransition::find_transitional_position` | 0x0050BDF0 | 0x0050C8C0 | exact | physics |  |
| `CTransition::makeTransition` | 0x0050B150 | 0x0050BC20 | exact | physics |  |
| `CTransition::step_down` | 0x0050B2A0 | 0x0050BD70 | exact | physics |  |
| `DBCache::GetDIDFromEnum` | 0x00413940 | 0x00413BF0 | exact | godot-ui, lead, portal, retail-ui | 0x00413BF0 (11.6096) |
| `DBObj::GetByEnum` | 0x00415490 | 0x00415730 | exact | retail-ui |  |
| `DBObj::GetDIDByEnum` | 0x004153A0 | 0x00415640 | verified | godot-ui, lead, portal, retail-ui | 0x00415640 (11.6096) |
| `DBWave::UnPack` | 0x00551B90 | **unmapped** | not in TSV | portal |  |
| `Device::ForceDisplayResolution` | 0x0043A750 | 0x0043A990 | exact | retail-ui |  |
| `DeviceTypeToString` | 0x0068A8D0 | 0x0068B8D0 | exact | retail-ui |  |
| `DialogFactory::MakeDialog` | 0x00477E90 | 0x00478290 | exact | retail-ui |  |
| `ECM_UI::SendNotice_DisplayStringInfo` | 0x006925B0 | 0x00693500 | exact | retail-ui | 0x00693500 (11.6096) |
| `ElementDesc::Incorporate` | 0x0069B5A0 | 0x0069C430 | exact | retail-ui |  |
| `ElementDesc::Serialize` | 0x0069BBA0 | 0x0069CA30 | exact | retail-ui |  |
| `ElementDesc::UpdateSizeAndPosition` | 0x0069A830 | 0x0069B6A0 | exact | retail-ui | 0x0069B6A0 (11.6096) |
| `EncumbranceSystem::EncumbranceCapacity` | 0x004FCC00 | 0x004FD7A0 | exact | avatars |  |
| `EncumbranceSystem::Load` | 0x004FCC40 | 0x004FD7E0 | exact | avatars |  |
| `EncumbranceSystem::LoadMod` | 0x004FCC70 | 0x004FD810 | exact | avatars |  |
| `EnumIDMap::EnumToDID` | 0x00415970 | 0x00415C10 | exact | portal |  |
| `EnumIDMap::Serialize` | 0x004165F0 | 0x00416890 | exact | portal |  |
| `FileNodeName_UInt32::Serialize` | 0x0069D700 | 0x0069E510 | exact | retail-ui |  |
| `Font::ContainsChar` | 0x00443420 | 0x00443580 | exact | retail-ui |  |
| `Font::GenerateCharacterMap` | 0x00443470 | 0x004435D0 | exact | retail-ui |  |
| `Font::GetCharDesc` | 0x00443360 | 0x004434C0 | exact | retail-ui | 0x00443360 (11.4186) |
| `Font::GetCharWidthA` | 0x004433F0 | 0x00443550 | exact | retail-ui | 0x004433F0 (11.4186) |
| `Font::Serialize` | 0x00443650 | 0x004437B0 | exact | retail-ui |  |
| `Frame::get_heading` | 0x00535760 | 0x005364A0 | exact | avatars |  |
| `Frame::rotate` | 0x004525B0 | 0x004525F0 | exact | portal |  |
| `Frame::set_heading` | 0x00535E40 | 0x00536B80 | exact | avatars |  |
| `Frame::set_vector_heading` | 0x00535DB0 | 0x00536AF0 | exact | avatars |  |
| `Frame::subtract2` | 0x00535650 | 0x00536390 | exact | avatars |  |
| `Glyph::SetFont` | 0x004749A0 | 0x00474D60 | exact | portal, retail-ui |  |
| `GlyphList::Recalculate` | 0x00473800 | 0x00473BC0 | exact | retail-ui |  |
| `gmCG3DView::gmCG3DView` | 0x004EE680 | 0x004EF310 | exact | avatars |  |
| `gmCG3DView::Initialize` | 0x004EE760 | 0x004EF3F0 | exact | avatars |  |
| `gmCG3DView::SetPlayerHeading` | 0x004EE590 | 0x004EF220 | verified | avatars |  |
| `gmCG3DView::StartAnimation` | 0x004EE600 | 0x004EF290 | exact | avatars | 0x004EE600 (11.4186) |
| `gmCG3DView::StopAnimation` | 0x004EE640 | 0x004EF2D0 | verified | avatars | 0x004EE640 (11.4186) |
| `gmCG3DView::Update` | 0x004EE9D0 | 0x004EF660 | exact | avatars | 0x004EE9D0 (11.4186) |
| `gmCGHeritagePage::ListenToElementMessage` | 0x00483860 | 0x00483C20 | exact | retail-ui |  |
| `gmCharacterManagementUI::EnterGame` | 0x004ED440 | 0x004EE0D0 | exact | retail-ui | 0x004EE0D0 (11.6096) |
| `gmCharGenMainUI::Create` | 0x004E83E0 | 0x004E9070 | verified | godot-ui, retail-ui | 0x004E9070 (11.6096) |
| `gmCharGenMainUI::DoRandom` | 0x004E7D70 | 0x004E8A00 | exact | retail-ui |  |
| `gmCharGenMainUI::gmCharGenMainUI` | 0x004E7EB0 | 0x004E8B40 | exact | godot-ui, retail-ui | 0x004E8B40 (11.6096) |
| `gmGamePlayUI::Create` | 0x004EA190 | 0x004EAE20 | verified | godot-ui, retail-ui | 0x004EAE20 (11.6096) |
| `gmGamePlayUI::gmGamePlayUI` | 0x004EA010 | 0x004EACA0 | exact | godot-ui, retail-ui | 0x004EACA0 (11.6096) |
| `gmGamePlayUI::LoadScreenLayout` | 0x004EA8F0 | 0x004EB580 | exact | godot-ui |  |
| `gmGamePlayUI::SetupChildren` | 0x004E9EC0 | 0x004EAB50 | exact | retail-ui |  |
| `gmGamePlayUI::~gmGamePlayUI` | 0x004EA2A0 | 0x004EAF30 | exact | retail-ui |  |
| `gmMainChatUI::gmMainChatUI` | 0x004CD0F0 | 0x004CDD60 | exact | retail-ui | 0x004CDD60 (11.6096) |
| `gmRadarUI::Create` | 0x004D8970 | 0x004D95D0 | verified | retail-ui | 0x004D95D0 (11.6096) |
| `gmSmartBoxUI::BeginTeleportAnimation` | 0x004D6300 | 0x004D6F60 | exact | portal | 0x004D6F60 (11.6096) |
| `gmSmartBoxUI::EndTeleportAnimation` | 0x004D65A0 | 0x004D7200 | exact | portal | 0x004D7200 (11.6096) |
| `gmSmartBoxUI::PostInit` | 0x004D6B60 | 0x004D77C0 | exact | godot-world, portal, retail-ui | 0x004D77C0 (11.6096) |
| `gmSmartBoxUI::RecvNotice_UpdateGameView` | 0x004D62C0 | 0x004D6F20 | exact | godot-ui |  |
| `gmSmartBoxUI::ResizeTo` | 0x004D6650 | 0x004D72B0 | exact | godot-ui |  |
| `gmSmartBoxUI::UseTime` | 0x004D6E30 | 0x004D7A90 | exact | godot-world, portal | 0x004D7A90 (11.6096) |
| `gmToolbarUI::Create` | 0x004BD8C0 | 0x004BE550 | exact | retail-ui | 0x004BE550 (11.6096) |
| `gmUIFlow::gmUIFlow` | 0x0047A610 | 0x0047AA10 | exact | godot-ui, retail-ui | 0x0047AA10 (11.6096) |
| `gmUIFlow::RecvNotice_CharacterError` | 0x0047A7C0 | 0x0047ABC0 | exact | retail-ui |  |
| `gmUIFlow::RecvNotice_CharacterError` | 0x0047AA10 | 0x0047AE10 | verified | retail-ui |  |
| `gmUIHelper::RegisterElements` | 0x0047A460 | 0x0047A860 | verified | retail-ui | 0x0047A860 (11.6096) |
| `gmVitalsUI::Create` | 0x004BFDE0 | 0x004C0A60 | verified | retail-ui | 0x004C0A60 (11.6096) |
| `Graphic::Draw` | 0x00693B20 | 0x00694A40 | exact | godot-ui, retail-ui |  |
| `heading_diff` | 0x00528FB0 | 0x00529BC0 | exact | avatars |  |
| `heading_greater` | 0x00528F60 | 0x00529B70 | exact | avatars |  |
| `HeritageGroup_CG::GetSX` | 0x005C1BC0 | 0x005C2B30 | exact | retail-ui |  |
| `InterpolationManager::adjust_offset` | 0x00555D30 | 0x00556950 | exact | avatars, physics | 0x00555D30 (11.4186) |
| `InterpolationManager::InterpolateTo` | 0x00555B20 | 0x00556740 | exact | avatars | 0x00555B20 (11.4186) |
| `InterpolationManager::NodeCompleted` | 0x005559A0 | 0x005565C0 | exact | avatars |  |
| `InterpolationManager::StopInterpolating` | 0x00555950 | 0x00556570 | exact | avatars |  |
| `InterpolationManager::UseTime` | 0x00555F20 | 0x00556B40 | exact | avatars | 0x00555F20 (11.4186) |
| `InterpretedMotionState::AddAction` | 0x0051E9E0 | 0x0051F4F0 | exact | avatars |  |
| `InterpretedMotionState::ApplyMotion` | 0x0051EA40 | 0x0051F550 | exact | avatars |  |
| `InterpretedMotionState::copy_movement_from` | 0x0051E750 | 0x0051F260 | exact | avatars |  |
| `InterpretedMotionState::GetNumActions` | 0x0051EB00 | 0x0051F610 | verified | avatars |  |
| `InterpretedMotionState::Pack` | 0x0051F1D0 | 0x0051FCE0 | exact | avatars |  |
| `InterpretedMotionState::RemoveAction` | 0x0051EAD0 | 0x0051F5E0 | exact | avatars |  |
| `InterpretedMotionState::RemoveMotion` | 0x0051E790 | 0x0051F2A0 | exact | avatars |  |
| `InterpretedMotionState::UnPack` | 0x0051F400 | 0x0051FF10 | exact | avatars, net |  |
| `IsNumberLabel` | 0x0067E630 | 0x0067F630 | exact | retail-ui |  |
| `LayoutDesc::AccessElementDesc` | 0x00699990 | 0x0069A850 | exact | retail-ui | 0x00699990 (11.4186) |
| `LayoutDesc::InqFullDesc` | 0x0069A520 | 0x0069B400 | exact | retail-ui | 0x0069A520 (11.4186) |
| `LayoutDesc::Serialize` | 0x0069A490 | 0x0069B370 | exact | retail-ui |  |
| `LIGHTINFO::SetDirection` | 0x00452700 | 0x00452740 | exact | portal |  |
| `LInt_StringInfoData::ToString` | 0x0042F7E0 | 0x0042FA00 | exact | retail-ui |  |
| `LScape::get_landcell` | 0x00505F10 | 0x00506970 | exact | physics |  |
| `MasterDBMap::DivineType` | 0x0041BE40 | 0x0041C260 | verified | retail-ui |  |
| `MasterProperty::AccessPropertyDesc` | 0x004281C0 | 0x004284B0 | exact | retail-ui |  |
| `MasterProperty::AllocatePropertyValue` | 0x00429120 | 0x004293C0 | exact | retail-ui |  |
| `MasterProperty::InqPropertyNameStringStatic` | 0x004265B0 | 0x004268D0 | exact | retail-ui |  |
| `MasterProperty::Serialize` | 0x00428BF0 | 0x00428E90 | exact | retail-ui |  |
| `MediaDesc::CreateMediaType` | 0x0069D420 | 0x0069E230 | exact | retail-ui |  |
| `MediaMachine::Update` | 0x00465BA0 | 0x00465CD0 | exact | retail-ui |  |
| `MotionTableManager::CheckForCompletedMotions` | 0x0051BE00 | 0x0051C900 | exact | avatars |  |
| `MotionTableManager::HandleExitWorld` | 0x0051BDA0 | 0x0051C8D0 | exact | avatars, physics |  |
| `MotionTableManager::RemoveLinkAnimations` | 0x0051BDD0 | 0x0051CAD0 | exact | avatars |  |
| `MouseHitTester::Recurse` | 0x00459370 | 0x00459490 | exact | retail-ui |  |
| `MovementManager::CancelMoveTo` | 0x005241B0 | 0x00524DB0 | verified | avatars |  |
| `MovementManager::HandleExitWorld` | 0x00524350 | 0x00524F50 | verified | avatars |  |
| `MovementManager::HandleUpdateTarget` | 0x00524790 | 0x00525390 | verified | avatars |  |
| `MovementManager::HitGround` | 0x00524300 | 0x00524F00 | verified | avatars |  |
| `MovementManager::IsMovingTo` | 0x00524260 | 0x00524E60 | verified | avatars |  |
| `MovementManager::LeaveGround` | 0x00524320 | 0x00524F20 | verified | avatars |  |
| `MovementManager::MakeMoveToManager` | 0x00524000 | 0x00524C00 | verified | avatars |  |
| `MovementManager::PerformMovement` | 0x005240D0 | 0x00524CD0 | exact | avatars |  |
| `MovementManager::unpack_movement` | 0x00524440 | 0x00525040 | exact | avatars, physics | 0x00524440 (11.4186) |
| `MovementManager::UseTime` | 0x005242F0 | 0x00524EF0 | verified | avatars |  |
| `MovementParameters::get_command` | 0x0052AA00 | 0x0052B610 | exact | avatars |  |
| `MovementParameters::get_desired_heading` | 0x0052AAD0 | 0x0052B6E0 | exact | avatars |  |
| `MovementParameters::MovementParameters` | 0x00524380 | 0x00524F80 | exact | avatars |  |
| `MovementParameters::towards_and_away` | 0x0052A9A0 | 0x0052B5B0 | exact | avatars |  |
| `MovementParameters::UnPackNet` | 0x0052AC50 | 0x0052B860 | exact | avatars |  |
| `MovementSystem::GetJumpHeight` | 0x006B09B0 | 0x006B1880 | exact | avatars |  |
| `MovementSystem::GetRunRate` | 0x006B0950 | 0x006B1820 | exact | avatars |  |
| `MovementSystem::JumpStaminaCost` | 0x006B0A40 | 0x006B1910 | exact | avatars |  |
| `MoveToManager::_DoMotion` | 0x00529010 | 0x00529C20 | verified | avatars |  |
| `MoveToManager::_StopMotion` | 0x00529080 | 0x00529C90 | verified | avatars |  |
| `MoveToManager::AddMoveToPositionNode` | 0x00529580 | 0x0052A190 | exact | avatars |  |
| `MoveToManager::AddTurnToHeadingNode` | 0x00529530 | 0x0052A140 | exact | avatars |  |
| `MoveToManager::BeginMoveForward` | 0x00529A00 | 0x0052A610 | exact | avatars |  |
| `MoveToManager::BeginNextNode` | 0x00529CB0 | 0x0052A8C0 | exact | avatars |  |
| `MoveToManager::BeginTurnToHeading` | 0x00529B90 | 0x0052A7A0 | exact | avatars |  |
| `MoveToManager::CancelMoveTo` | 0x00529930 | 0x0052A540 | exact | avatars |  |
| `MoveToManager::CheckProgressMade` | 0x005290F0 | 0x00529D00 | exact | avatars |  |
| `MoveToManager::CleanUp` | 0x005295C0 | 0x0052A1D0 | exact | avatars |  |
| `MoveToManager::CleanUpAndCallWeenie` | 0x00529650 | 0x0052A260 | exact | avatars |  |
| `MoveToManager::GetCurrentDistance` | 0x005291B0 | 0x00529DC0 | exact | avatars |  |
| `MoveToManager::HandleMoveToPosition` | 0x00529D80 | 0x0052A990 | exact | avatars |  |
| `MoveToManager::HandleTurnToHeading` | 0x0052A0C0 | 0x0052ACD0 | exact | avatars |  |
| `MoveToManager::HandleUpdateTarget` | 0x0052A7D0 | 0x0052B3E0 | exact | avatars |  |
| `MoveToManager::InitializeLocalVariables` | 0x00529250 | 0x00529E60 | exact | avatars |  |
| `MoveToManager::is_moving_to` | 0x00529220 | 0x00529E30 | verified | avatars |  |
| `MoveToManager::MoveToObject` | 0x00529680 | 0x0052A290 | exact | avatars |  |
| `MoveToManager::MoveToObject_Internal` | 0x0052A400 | 0x0052B010 | exact | avatars |  |
| `MoveToManager::MoveToPosition` | 0x0052A240 | 0x0052AE50 | exact | avatars |  |
| `MoveToManager::PerformMovement` | 0x0052A900 | 0x0052B510 | exact | avatars |  |
| `MoveToManager::RemovePendingActionsHead` | 0x00529380 | 0x00529F90 | exact | avatars |  |
| `MoveToManager::TurnToHeading` | 0x0052A630 | 0x0052B240 | exact | avatars |  |
| `MoveToManager::TurnToObject` | 0x005297D0 | 0x0052A3E0 | exact | avatars |  |
| `MoveToManager::TurnToObject_Internal` | 0x0052A550 | 0x0052B160 | exact | avatars |  |
| `ObjDesc::AddAnimPartChange` | 0x005ABA80 | 0x005ACB30 | exact | avatars | 0x005ABA80 (11.4186) |
| `ObjDesc::AddAnimPartChange` | 0x005AC050 | 0x005AD100 | exact | avatars | 0x005ABA80 (11.4186) |
| `ObjDesc::AddSubpalette` | 0x005AB970 | 0x005ACA20 | exact | avatars |  |
| `ObjDesc::AddSubpalette` | 0x005ABF90 | 0x005AD040 | verified | avatars |  |
| `ObjDesc::AddTextureMapChange` | 0x005ABA10 | 0x005ACAC0 | exact | avatars | 0x005ABA10 (11.4186) |
| `ObjDesc::AddTextureMapChange` | 0x005ABFF0 | 0x005AD0A0 | verified | avatars | 0x005ABA10 (11.4186) |
| `ObjDesc::operator+=` | 0x005AC0A0 | 0x005AD150 | exact | avatars |  |
| `ObjDesc::RemoveDuplicateAnimPartChange` | 0x005AB720 | 0x005AC7D0 | exact | avatars |  |
| `ObjDesc::RemoveDuplicateSubpalette` | 0x005AB640 | 0x005AC6F0 | exact | avatars |  |
| `ObjDesc::RemoveDuplicateTextureMapChange` | 0x005AB6B0 | 0x005AC760 | exact | avatars |  |
| `ObjDesc::UnPack` | 0x005ABCF0 | 0x005ACDA0 | exact | avatars | 0x005ABCF0 (11.4186) |
| `ObjDesc::Wipe` | 0x005AB820 | 0x005AC8D0 | exact | avatars |  |
| `Pack_AsDataIDOfKnownType` | 0x00684480 | 0x00685480 | exact | net |  |
| `PackableHashTable<unsigned long,CloPaletteTemplate>::add` | 0x005A7E30 | 0x005A8EE0 | exact | retail-ui |  |
| `PackableHashTable<unsigned long,CloPaletteTemplate>::begin` | 0x005A73F0 | 0x005A8530 | verified | retail-ui |  |
| `Palette::makeModifiedPalette` | 0x0053E3C0 | 0x0053F120 | exact | avatars |  |
| `Palette::Modify` | 0x0053E160 | 0x0053EEC0 | exact | avatars |  |
| `Palette::Modify` | 0x0053E1F0 | 0x0053EF50 | exact | avatars |  |
| `PalSet::GetPaletteID` | 0x005AC570 | 0x005AD620 | exact | avatars |  |
| `Plane::operator==` | 0x006B3DD0 | 0x006B4D10 | exact | physics |  |
| `Position::cylinder_distance` | 0x005A97F0 | 0x005AA8A0 | exact | avatars |  |
| `Position::cylinder_distance_no_z` | 0x005A98B0 | 0x005AA960 | exact | avatars |  |
| `Position::distance` | 0x005A94B0 | 0x005AA560 | exact | avatars |  |
| `Position::heading` | 0x005A9520 | 0x005AA5D0 | exact | avatars |  |
| `Position::subtract2` | 0x00555A90 | 0x005566B0 | exact | avatars |  |
| `PositionManager::adjust_offset` | 0x00555190 | 0x00555DB0 | exact | avatars | 0x00555190 (11.4186) |
| `PositionManager::InterpolateTo` | 0x005551F0 | 0x00555E10 | exact | avatars |  |
| `PositionManager::StopInterpolating` | 0x00555220 | 0x00555E40 | verified | avatars |  |
| `PositionManager::UnConstrain` | 0x005552B0 | 0x00555ED0 | verified | avatars |  |
| `PositionManager::UseTime` | 0x00555160 | 0x00555D80 | exact | avatars |  |
| `PositionPack::UnPack` | 0x00516740 | 0x00517240 | exact | net |  |
| `PropertyCollection::Serialize` | 0x00681420 | 0x00682420 | exact | retail-ui |  |
| `PropertyCollection::UpdateCollection` | 0x006807E0 | 0x006817E0 | exact | retail-ui |  |
| `Proto_UI::GetNextUICounter` | 0x005467E0 | 0x005473A0 | verified | net |  |
| `Proto_UI::SendAdminRestoreCharacter` | 0x00546CF0 | 0x005478B0 | exact | net |  |
| `Proto_UI::SetEventCounter` | 0x00546800 | 0x005473C0 | verified | net |  |
| `Proto_UI::UICounterFailedSend` | 0x005467F0 | 0x005473B0 | predicted | net |  |
| `PStringBase<unsigned short>::trim` | 0x004D5C60 | 0x004D68C0 | exact | retail-ui |  |
| `PSUtils::TrimExcessSpaces` | 0x0067B7F0 | 0x0067C7F0 | exact | retail-ui |  |
| `PSUtils::wcschr_skipescapes` | 0x0040AFE0 | 0x0040B340 | verified | retail-ui |  |
| `QualifiedControl::operator==` | 0x0068A1D0 | 0x0068B260 | exact | retail-ui |  |
| `RandDouble` | 0x006843D0 | 0x006853D0 | exact | portal | 0x006843D0 (11.4186) |
| `RandInt` | 0x00684400 | 0x00685400 | verified | retail-ui |  |
| `RandInt` | 0x00684420 | 0x00685420 | exact | retail-ui |  |
| `Random::rand` | 0x0042C4C0 | 0x0042C720 | exact | retail-ui |  |
| `Random::RollDice` | 0x0042C5C0 | 0x0042C820 | exact | portal, retail-ui |  |
| `Random::RollDice` | 0x0042C600 | 0x0042C860 | exact | portal |  |
| `RawMotionState::AddAction` | 0x0051E840 | 0x0051F350 | exact | avatars |  |
| `RawMotionState::ApplyMotion` | 0x0051EB60 | 0x0051F670 | exact | avatars |  |
| `RawMotionState::Pack` | 0x0051ED10 | 0x0051F820 | exact | net |  |
| `RawMotionState::RemoveAction` | 0x0051E8A0 | 0x0051F3B0 | exact | avatars |  |
| `RawMotionState::RemoveMotion` | 0x0051E6E0 | 0x0051F1F0 | exact | avatars |  |
| `RawMotionState::UnPack` | 0x0051EFC0 | 0x0051FAD0 | exact | net |  |
| `Render::minimize_object_lighting` | 0x0054D480 | 0x0054E090 | exact | godot-world |  |
| `Render::set_vdst` | 0x0054B240 | 0x0054BE50 | exact | godot-world, portal | 0x0054BE50 (11.6096) |
| `Render::SetFOVRad` | 0x0054B2D0 | 0x0054BEE0 | exact | portal |  |
| `RenderUI::ComputeGameViewport` | 0x00448770 | 0x00448910 | exact | retail-ui |  |
| `ShadePackage::GetVal` | 0x005A82C0 | 0x005A9370 | verified | avatars |  |
| `ShadePackage::ShadePackage` | 0x005A8290 | 0x005A9340 | exact | avatars |  |
| `SharedNet::UseTime` | 0x00542450 | 0x00543030 | exact | net |  |
| `SkillFormula::Calculate` | 0x00591960 | 0x00592660 | exact | retail-ui |  |
| `SmartArray::add_unique` | 0x00699B40 | 0x0069AA00 | exact | retail-ui |  |
| `SmartBox::BlipPlayer` | 0x00453940 | 0x004539E0 | exact | physics |  |
| `SmartBox::DoParentEvent` | 0x00452290 | 0x004522D0 | exact | avatars | 0x00452290 (11.4186) |
| `SmartBox::DoPickupEvent` | 0x00452240 | 0x00452280 | exact | avatars | 0x00452240 (11.4186) |
| `SmartBox::DoSetState` | 0x004520D0 | 0x00452110 | exact | avatars, portal, state | 0x00452110 (11.6096) |
| `SmartBox::DoVectorUpdate` | 0x004521C0 | 0x00452200 | exact | avatars, state |  |
| `SmartBox::GetOverrideFovDistance` | 0x00451BE0 | 0x00451C20 | exact | godot-world, portal | 0x00451C20 (11.6096) |
| `SmartBox::HandleCreateObject` | 0x00454C80 | 0x00454D20 | exact | avatars, state |  |
| `SmartBox::HandleDeleteObject` | 0x00451EA0 | 0x00451EE0 | exact | avatars, state |  |
| `SmartBox::HandleObjDescEvent` | 0x00453340 | 0x00453380 | verified | avatars, state | 0x00453340 (11.4186) |
| `SmartBox::HandleParentEvent` | 0x004535D0 | 0x00453610 | exact | state | 0x004535D0 (11.4186) |
| `SmartBox::HandlePickupEvent` | 0x00453530 | 0x00453570 | exact | state | 0x00453530 (11.4186) |
| `SmartBox::HandlePlayerTeleport` | 0x00452150 | 0x00452190 | exact | godot-world, portal, state | 0x00452190 (11.6096) |
| `SmartBox::HandleReceivedPosition` | 0x00453FD0 | 0x00454070 | exact | avatars, physics, state | 0x00453FD0 (11.4186) |
| `SmartBox::HandleSetState` | 0x004533E0 | 0x00453420 | verified | state | 0x004533E0 (11.4186) |
| `SmartBox::HandleUpdateObject` | 0x00455580 | 0x00455620 | exact | avatars, lead, state | 0x00455580 (11.4186) |
| `SmartBox::HandleVectorUpdate` | 0x00453480 | 0x004534C0 | exact | state | 0x00453480 (11.4186) |
| `SmartBox::Hide` | 0x00451D40 | 0x00451D80 | verified | godot-world |  |
| `SmartBox::PlayerPositionUpdated` | 0x00453870 | 0x00453910 | exact | godot-world, portal | 0x00453910 (11.6096) |
| `SmartBox::ProcessObjectNetBlobs` | 0x00454B20 | 0x00454BC0 | exact | lead, state | 0x00454B20 (11.4186) |
| `SmartBox::Show` | 0x00451D30 | 0x00451D70 | verified | portal |  |
| `SmartBox::teleport_in_progress` | 0x00451C20 | 0x00451C60 | verified | portal | 0x00451C60 (11.6096) |
| `SmartBox::teleport_occured` | 0x00451C40 | 0x00451C80 | verified | godot-world, portal |  |
| `SmartBox::TeleportPlayer` | 0x00453910 | 0x004539B0 | exact | godot-world, physics, portal | 0x004539B0 (11.6096) |
| `SmartBox::UnpackPositionEvent` | 0x004542C0 | 0x00454360 | exact | physics, state |  |
| `SmartBox::update_viewer` | 0x00453CE0 | 0x00453D80 | exact | godot-world | 0x00453CE0 (11.4186) |
| `SmartBox::UpdateVisualDesc` | 0x00451F40 | 0x00451F80 | exact | avatars, state |  |
| `SmartBox::UseTime` | 0x00455410 | 0x004554B0 | exact | godot-world, physics, portal, state | 0x004554B0 (11.6096) |
| `SoundManager::GetSound` | 0x00550680 | 0x00551290 | exact | portal |  |
| `SoundManager::PlayProbability` | 0x005500E0 | 0x00550CF0 | exact | portal |  |
| `SoundManager::PlaySoundFromCenter` | 0x00550950 | 0x00551560 | exact | godot-world |  |
| `SoundManager::PlaySoundFromCenter` | 0x005509E0 | 0x005515F0 | exact | godot-world |  |
| `SoundTableData::Lookup` | 0x005520A0 | **unmapped** | not in TSV | portal |  |
| `SoundTableData::UnPack` | 0x00552370 | 0x00552F90 | exact | portal |  |
| `StateDesc::ConcatenateMedia` | 0x0069C9B0 | 0x0069D840 | exact | retail-ui |  |
| `StateDesc::Incorporate` | 0x0069CCA0 | 0x0069DB30 | exact | retail-ui |  |
| `StateDesc::Serialize` | 0x0069C5F0 | 0x0069D480 | exact | retail-ui |  |
| `StateDesc::UpdateSizeAndPosition` | 0x0069BF20 | 0x0069CDB0 | exact | retail-ui | 0x0069BF20 (11.4186), 0x0069CDB0 (11.6096) |
| `StickyManager::adjust_offset` | 0x00555430 | 0x00556050 | exact | avatars |  |
| `StickyManager::Create` | 0x00555800 | 0x00556420 | exact | avatars |  |
| `StickyManager::HandleUpdateTarget` | 0x00555780 | 0x005563A0 | exact | avatars |  |
| `StickyManager::StickTo` | 0x00555710 | 0x00556330 | exact | avatars |  |
| `StringInfo::AddVariable_String` | 0x0042E6C0 | 0x0042E8E0 | exact | retail-ui |  |
| `StringInfo::GetString` | 0x0042E760 | 0x0042E980 | exact | retail-ui | 0x0042E980 (11.6096) |
| `StringInfo::InqString` | 0x0042E490 | 0x0042E6B0 | exact | retail-ui |  |
| `StringInfo::InqStringInternal` | 0x0042E020 | 0x0042E240 | exact | retail-ui |  |
| `StringInfo::Serialize` | 0x0042DEB0 | 0x0042E0D0 | exact | retail-ui | 0x0042DEB0 (11.4186) |
| `StringInfo::SetLiteralValue` | 0x0042C980 | 0x0042CBE0 | exact | portal |  |
| `StringInfo::SetStringIDandTableEnum` | 0x0042C760 | 0x0042C9C0 | exact | retail-ui | 0x0042C9C0 (11.6096) |
| `StringInfo::SetTableEnum` | 0x0042C720 | 0x0042C980 | exact | portal, retail-ui |  |
| `StringInfo_StringInfoData::ToString` | 0x0042F700 | 0x0042F920 | exact | retail-ui |  |
| `StringInfoDataHelper::NumToString` | 0x0042F280 | 0x0042F4A0 | exact | retail-ui |  |
| `stringmeta_parse` | 0x0067DFE0 | 0x0067EFE0 | exact | retail-ui |  |
| `StringTable::GetString` | 0x004300D0 | 0x00430270 | exact | retail-ui |  |
| `StringTable::GetStringTableString` | 0x00430000 | 0x004301A0 | exact | retail-ui |  |
| `StringTable::Serialize` | 0x004309C0 | 0x00430B60 | exact | retail-ui |  |
| `StringTableMetaLanguage::EscapeString` | 0x0067BBC0 | 0x0067CBC0 | exact | retail-ui |  |
| `StringTableMetaLanguage::GetEscapedChar` | 0x0067B6C0 | 0x0067C6C0 | exact | retail-ui |  |
| `StringTableMetaLanguage::GetUnEscapedChar` | 0x0067B750 | 0x0067C750 | exact | retail-ui |  |
| `StringTableMetaLanguage::IsEscapeChar` | 0x0067B690 | 0x0067C690 | exact | retail-ui |  |
| `StringTableMetaLanguage::LoadTokenizer` | 0x0067F020 | 0x00680020 | exact | retail-ui |  |
| `StringTableMetaLanguage::r_Text` | 0x0067DA40 | 0x0067EA40 | exact | retail-ui |  |
| `StringTableMetaLanguage::r_Var` | 0x0067DAB0 | 0x0067EAB0 | exact | retail-ui |  |
| `StringTableMetaLanguage::RenderNodes` | 0x0067C3C0 | 0x0067D3C0 | exact | retail-ui |  |
| `StringTableMetaLanguage::RenderString` | 0x0067D150 | 0x0067E150 | exact | retail-ui |  |
| `StringTableMetaLanguage::StripMetaLetters` | 0x0067BAF0 | 0x0067CAF0 | exact | retail-ui |  |
| `StringTableMetaLanguage::TrimExcessSpaces` | 0x0067B9B0 | 0x0067C9B0 | exact | retail-ui |  |
| `StringTableMetaLanguage::UnescapeString` | 0x0067BDC0 | 0x0067CDC0 | exact | retail-ui |  |
| `StringTableString::Serialize` | 0x00430480 | 0x00430620 | exact | retail-ui |  |
| `StructPropertyValue::Serialize` | 0x00429490 | 0x00429730 | exact | retail-ui |  |
| `SubControlIndexToString` | 0x0068AAD0 | 0x0068BAD0 | exact | retail-ui |  |
| `Subpalette::replaces` | 0x005AD830 | 0x005AE8E0 | exact | avatars |  |
| `Subpalette::supercedes` | 0x005AD870 | 0x005AE920 | exact | avatars |  |
| `Subpalette::UnPack` | 0x005ADAB0 | 0x005AEB60 | exact | avatars |  |
| `SurfaceWindow::Blit` | 0x00442B10 | 0x00442C70 | exact | godot-ui |  |
| `SurfaceWindow::Blit` | 0x00443130 | 0x00443290 | exact | godot-ui |  |
| `SurfaceWindow::DrawCharacter` | 0x00442BD0 | 0x00442D30 | exact | godot-ui, retail-ui |  |
| `SurfaceWindow::LegacyBlit` | 0x00441C50 | 0x00441DF0 | exact | godot-ui |  |
| `TargetManager::AddVoyeur` | 0x0051A830 | 0x0051B360 | exact | avatars |  |
| `TargetManager::ClearTarget` | 0x0051A7E0 | 0x0051B310 | exact | avatars |  |
| `TargetManager::HandleTargetting` | 0x0051AA90 | 0x0051B5C0 | exact | avatars |  |
| `TargetManager::ReceiveUpdate` | 0x0051A930 | 0x0051B460 | exact | avatars |  |
| `TargetManager::SetTarget` | 0x0051AC30 | 0x0051B760 | exact | avatars |  |
| `TargetManager::SetTargetQuantum` | 0x0051A4A0 | 0x0051B040 | exact | avatars |  |
| `TextureMapChange::replaces` | 0x005AD980 | 0x005AEA30 | exact | avatars |  |
| `TextureMapChange::UnPack` | 0x005AD920 | 0x005AE9D0 | exact | avatars |  |
| `Tokenize` | 0x00401A10 | 0x00401A20 | verified | retail-ui |  |
| `UIElement::OnSetAttribute` | 0x00462D80 | 0x00462E60 | exact | retail-ui |  |
| `UIElement::RegisterElementClass` | 0x00460190 | 0x00460270 | verified | retail-ui | 0x00460270 (11.6096) |
| `UIElement::SetState` | 0x00464E70 | 0x00464FA0 | exact | retail-ui |  |
| `UIElement::UpdateForParentSizeChange` | 0x00462640 | 0x00462720 | exact | retail-ui |  |
| `UIElement_Button::OnSetAttribute` | 0x00471F40 | 0x00472300 | exact | retail-ui |  |
| `UIElement_Text::OnSetAttribute` | 0x0046A640 | 0x0046A8D0 | exact | retail-ui |  |
| `UIElement_Viewport::SetLight` | 0x0046BB90 | 0x0046BE40 | exact | avatars |  |
| `UIElementManager::ActivateNext` | 0x00459910 | 0x00459980 | exact | retail-ui |  |
| `UIElementManager::CheckCursor` | 0x0045ABF0 | 0x0045AD00 | exact | retail-ui |  |
| `UIElementManager::CreateElementRecursiveFromPartialDesc` | 0x0045BFB0 | 0x0045C090 | exact | retail-ui |  |
| `UIElementManager::DrawDirtyRegions` | 0x0045B820 | 0x0045B900 | exact | retail-ui |  |
| `UIElementManager::SetCursor` | 0x0045A800 | 0x0045A910 | exact | godot-ui, retail-ui | 0x0045A910 (11.6096) |
| `UIFlow::QueueUIMode` | 0x004793C0 | 0x004797C0 | exact | godot-ui, retail-ui | 0x004797C0 (11.6096) |
| `UIFlow::RegisterFrameworkClass` | 0x00479850 | 0x00479C50 | exact | retail-ui | 0x00479C50 (11.6096) |
| `UIGlobals::GetAnimLevel` | 0x004EE540 | 0x004EF1D0 | exact | portal | 0x004EF1D0 (11.6096) |
| `UIGlobals::Init` | 0x004EE470 | 0x004EF100 | exact | portal | 0x004EF100 (11.6096) |
| `UIMainFramework::CreateAndAddRootElement` | 0x006A0C00 | 0x006A1A20 | exact | retail-ui | 0x006A1A20 (11.6096) |
| `UIObject::AddDirtyRect` | 0x00699670 | 0x0069A530 | exact | godot-ui |  |
| `UIRegion::AddChild` | 0x006A0760 | 0x006A1580 | exact | godot-ui, retail-ui |  |
| `UIRegion::AddDirtyRect` | 0x0069F080 | 0x0069FF00 | exact | godot-ui |  |
| `UIRegion::CompareZLevel` | 0x0069F000 | 0x0069FE80 | exact | retail-ui |  |
| `UIRegion::DrawChildren` | 0x0069FCA0 | 0x006A0B20 | exact | godot-ui, retail-ui | 0x0069FCA0 (11.4186) |
| `UIRegion::DrawHere` | 0x0069FA30 | 0x006A08B0 | exact | godot-ui |  |
| `UIRegion::DrawSelf` | 0x0069F1A0 | 0x006A0020 | exact | retail-ui |  |
| `UIRegion::EraseBackground` | 0x0069ED70 | 0x0069FBF0 | exact | godot-ui |  |
| `UIRegion::SetAlphaBlendMod` | 0x0069F5E0 | 0x006A0460 | exact | godot-ui |  |
| `UIRegion::SetClampGameViewEdge` | 0x0069EDF0 | 0x0069FC70 | exact | retail-ui |  |
| `UIRegion::SetImageByDID` | 0x0069F960 | 0x006A07E0 | exact | godot-ui, retail-ui |  |
| `UISurface::CreateSurface` | 0x00440990 | 0x00440B30 | exact | godot-ui |  |
| `Unpack_AsDataIDOfKnownType` | 0x006844F0 | 0x006854F0 | exact | avatars, net |  |
| `ClientCharGenState::GetColorFromPal` | 0x00563990 | 0x00564730 | exact | retail-ui |  |
| `CPlayerSystem::LogOnCharacter` | 0x0055F890 | 0x00560600 | exact | retail-ui |  |
| `DialogFactory::DialogDone` | 0x004773C0 | 0x004777C0 | exact | retail-ui |  |
| `gmCG3DView::SetCamera` | 0x004EE5B0 | 0x004EF240 | exact | retail-ui |  |
| `gmCGAppearancePage::DoColorSpots` | 0x0047D850 | 0x0047DC00 | exact | retail-ui |  |
| `gmCGAppearancePage::DoGradDisk` | 0x0047DA90 | 0x0047DE40 | exact | retail-ui |  |
| `gmCGAppearancePage::DoRotation` | 0x0047CA80 | 0x0047CE30 | exact | retail-ui |  |
| `gmCGAppearancePage::DoZoomAnimation` | 0x0047C960 | 0x0047CD10 | exact | retail-ui |  |
| `gmCGAppearancePage::InitializePage` | 0x0047FDD0 | 0x00480180 | exact | retail-ui |  |
| `gmCGAppearancePage::ListenToElementMessage` | 0x0047EF30 | 0x0047F2E0 | exact | retail-ui |  |
| `gmCGAppearancePage::ListenToGlobalMessage` | 0x0047CED0 | 0x0047D280 | exact | retail-ui |  |
| `gmCGAppearancePage::Rotate` | 0x0047CB50 | 0x0047CF00 | exact | retail-ui |  |
| `gmCGAppearancePage::SetChoice` | 0x0047D4B0 | 0x0047D860 | exact | retail-ui |  |
| `gmCGAppearancePage::SetColor` | 0x0047DD50 | 0x0047E100 | exact | retail-ui |  |
| `gmCGAppearancePage::SetSelection` | 0x0047E260 | 0x0047E610 | exact | retail-ui |  |
| `gmCGAppearancePage::SetShade` | 0x0047C860 | 0x0047CC10 | exact | retail-ui |  |
| `gmCGAppearancePage::SetupParts` | 0x0047E020 | 0x0047E3D0 | exact | retail-ui |  |
| `gmCGAppearancePage::Update` | 0x0047E8F0 | 0x0047ECA0 | exact | retail-ui |  |
| `gmCGAppearancePage::ZoomIn` | 0x0047CF00 | 0x0047D2B0 | exact | retail-ui |  |
| `gmCGAppearancePage::ZoomOut` | 0x0047D050 | 0x0047D400 | exact | retail-ui |  |
| `gmCGSummaryPage::DoNameLimitDialog` | 0x0047BD80 | 0x0047C140 | exact | retail-ui |  |
| `gmCGSummaryPage::InitializePage` | 0x0047BBF0 | 0x0047BFB0 | exact | retail-ui |  |
| `gmCGSummaryPage::ListenToElementMessage` | 0x0047BF40 | 0x0047C300 | exact | retail-ui |  |
| `gmCGSummaryPage::SetHowToText` | 0x0047AE20 | 0x0047B1E0 | exact | retail-ui |  |
| `gmCGSummaryPage::SetSummaryText` | 0x0047B1D0 | 0x0047B590 | exact | retail-ui |  |
| `gmCGSummaryPage::Update` | 0x0047BAA0 | 0x0047BE60 | exact | retail-ui |  |
| `gmCGTownPage::InitializePage` | 0x0047C6D0 | 0x0047CA80 | exact | retail-ui |  |
| `gmCGTownPage::ListenToElementMessage` | 0x0047C480 | 0x0047C830 | exact | retail-ui |  |
| `gmCGTownPage::SetTown` | 0x0047C360 | 0x0047C710 | exact | retail-ui |  |
| `gmCGTownPage::SetTownString` | 0x0047C1F0 | 0x0047C5A0 | exact | retail-ui |  |
| `gmCGTownPage::Update` | 0x0047C460 | 0x0047C810 | verified | retail-ui |  |
| `gmCharGenMainUI::DoFinish` | 0x004E9170 | 0x004E9E00 | exact | retail-ui |  |
| `gmCharGenMainUI::MakeCreditWarningDialog` | 0x004E8870 | 0x004E9500 | exact | retail-ui |  |
| `gmCharGenMainUI::MakeErrorMessageDialog` | 0x004E8CB0 | 0x004E9940 | exact | retail-ui |  |
| `gmCharGenMainUI::RecvNotice_CharGenVerificationResponse` | 0x004E9030 | 0x004E9CC0 | exact | retail-ui |  |
| `gmCharGenMainUI::Update` | 0x004E8460 | 0x004E90F0 | exact | retail-ui |  |
| `PalSet::GetPaletteID_Integral` | 0x005AC5F0 | 0x005AD6A0 | exact | retail-ui |  |
| `Proto_UI::SendCharGenResult` | 0x00546A70 | 0x00547630 | exact | retail-ui |  |
| `RGBAColor::GetColor32` | 0x00423D00 | 0x00424000 | exact | retail-ui |  |
| `SurfaceWindow::BlitAndColor` | 0x00442750 | 0x004428B0 | exact | retail-ui |  |
| `SurfaceWindow::ReplaceColor` | 0x00441530 | 0x004416D0 | exact | retail-ui |  |
| `SurfaceWindow::ReplaceColor` | 0x004415B0 | 0x00441750 | exact | retail-ui |  |
| `UIElement::TakeFocus` | 0x004601E0 | 0x004602C0 | exact | retail-ui |  |
| `UIElement_Viewport::SetCamera` | 0x0046BB60 | 0x0046BE10 | exact | retail-ui |  |
| `UIRegion::ClearImage` | 0x0069F7E0 | 0x006A0660 | exact | retail-ui |  |
| `UIRegion::SetImage` | 0x0069F790 | 0x006A0610 | exact | retail-ui |  |

## 3. Unmapped and unconfirmed: named TODOs

| Function | 11.4186 | 11.6096 | Owner | Note |
|---|---|---|---|---|
| `CMotionInterp::jump_charge_is_allowed` | 0x00527A50 | unmapped | TODO(avatars) (cited by physics) | One of the 46 changed functions (doc 13 §5: Sanctuary added). The table has an unmapped 11.6096 row at 0x00528660, in the same slot as in 11.4186 (between `motion_allows_jump` and `get_jump_v_z`). That has not been checked against the bytes. Not the same function as `CMotionInterp::jump_is_allowed` (0x005282B0 / 0x00528EC0). |
| `CMotionInterp::motion_allows_jump` | 0x005279E0 | 0x005285E0 (predicted) | TODO(avatars) | Changed in 11.6096 (gating range 0x1000012B–0x10000134, plus Sanctuary 0x10000057 and AI_TelegraphCast 0x1000019B). Confirm the address against the bytes before citing it as exact. |
| `CMotionInterp::charge_jump` | 0x005281C0 | 0x00528DD0 (predicted) | TODO(avatars) | Changed in 11.6096 (Sanctuary added). As above. |
| `ConstraintManager::ConstrainTo` | 0x00556240 | unmapped | TODO(avatars) | The table has no row between `ConstraintManager::adjust_offset` (0x00556DA0) and `CCommunicationSystem::Init` (0x00556EB0). The avatars track's `ConstraintManager` is the code that cites it (the physics track's first TODO). |
| `ConstraintManager::UnConstrain` / `IsFullyConstrained` | 0x005560C0 / 0x005560D0 | unmapped | TODO(avatars) | The same gap. |
| `PositionManager::ConstrainTo` / `IsFullyConstrained` / `IsInterpolating` / `GetStickyObjectID` | 0x00555280 / 0x005552C0 / 0x005553B0 / 0x00555270 | unmapped | TODO(avatars) | |
| `MoveToManager::HitGround` / `UseTime` | 0x00529D70 / 0x0052A780 | unmapped | TODO(avatars) | |
| `StickyManager::HandleExitWorld` / `UseTime` | 0x00555400 / 0x00555610 | unmapped | TODO(avatars) | |
| `TargetManager::RemoveVoyeur` | 0x0051AD90 | unmapped | TODO(avatars) | |
| `CMotionInterp::HandleExitWorld` | 0x00527F30 | unmapped | TODO(avatars) | |
| `CMotionInterp::InqStyle` | 0x00527B10 | folded | TODO(avatars) | The table maps this 11.4186 address only to `CBaseFilter::GetPinVersion` (11.6096 0x00528720, predicted): the body is folded into another function. |
| `CPartArray::InitializeMotionTables` / `CheckForCompletedMotions` | 0x00517D10 / 0x00517D50 | unmapped | TODO(avatars) | |
| `MotionTableManager::UseTime` | none (not in the per-file decompile) | unmapped | TODO(avatars) | Called by `CPartArray::HandleMovement` (0x00517D60 / 0x00518890). The table names its body only as the second candidate of `MotionTableManager::RemoveLinkAnimations` (0x0051CAD0), which cannot be it. |
| `CPartArray::GetNumSphere` | 0x00518060 | 0x00518B90 (predicted) | TODO(avatars) | The table's own predicted row, and the address the +0xB30 shift of its neighbours `InitParts` and `SetPart` gives. Confirm it against the bytes before citing it as exact. |
| `MovementManager::get_minterp` | 0x005242A0 | unmapped | TODO(physics) | |
| `CPhysicsObj::clear_transient_states` | 0x00511BF0 | unmapped | TODO(physics) | |
| `SPHEREPATH::init_sphere` / `init_path` | 0x0050C670 / 0x0050CE20 | unmapped | TODO(physics) | |
| `CBuildingObj::find_building_collisions` and the two `find_building_transit_cells` | 0x006B5300, 0x006B5230, 0x006B52A0 | unmapped | TODO(physics) | |
| `CACQualities::InqRunRate` / `InqJumpVelocity` | 0x00592800 / 0x00592980 | unmapped | TODO(physics) | What the explore-mode player weenie's placeholder skills stand in for (owner decision D16). |
| `gmDataPatchUI::gmDataPatchUI` | 0x004EE2B0 | unmapped (predicted 0x004EEF40) | TODO(retailui-data) | Not in the table. The predictions in these three rows add 0xC90, the shift of every mapped neighbour in the same object file on both sides (for example `vector_deleting_destructor` 0x004EE2A0 / 0x004EEF30 and `Register` 0x004EE450 / 0x004EF0E0); no byte was read, so they are unconfirmed. |
| `gmDisconnectedUI::gmDisconnectedUI` | 0x004E9A10 | unmapped (predicted 0x004EA6A0) | TODO(retailui-data) | Neighbours `SetErrorMsg` (0x004E99F0 / 0x004EA680) and `scalar_deleting_destructor` (0x004E9AA0 / 0x004EA730). |
| `gmCharacterManagementUI::gmCharacterManagementUI` | 0x004EC8F0 | unmapped (predicted 0x004ED580) | TODO(retailui-data) | Neighbours `RebuildCharacterList` (0x004EC3A0 / 0x004ED030) and `Update` (0x004ECAB0 / 0x004ED740). |
| `gmEpilogueUI::gmEpilogueUI` | 0x004E9B00 | unmapped (predicted 0x004EA790) | TODO(retailui-data) | Not in the table. Its neighbour `Create` is 0x004E9B90 / 0x004EA820, the same 0xC90 shift as the three rows above; no byte was read. |
| `DBWave::UnPack` | 0x00551B90 | unmapped | TODO(portal) | `DBWave::DBWave` (0x005526C0) and `DBWave::Get` (0x005534A0) are mapped. The `UnPack` slot of the vtable that `DBWave::DBWave` installs holds it. |
| `SoundTableData::Lookup` | 0x005520A0 | unmapped | TODO(portal) | `CSoundTable::Lookup` (0x00552100 / 0x00552D20) and `SoundTableData::UnPack` are mapped. The forwarder `CSoundTable::Lookup` jumps to it. Every mapped symbol between 0x005526C0 and 0x00552F90 is displaced from 11.4186 by 0xC20, a hint and not an address. |

The 11.4186 addresses of the unmapped functions come from the per-file decompile headers (§6). A track that needs
one of these in 11.6096 confirms it from the bytes, as §1.2 did for `DoJump`, and adds the result here.

### 3.1 Byte reads and behaviours still wanted

Owner decision D4 allowed four reads of the user's `acclient.exe`, and all four are done (§1). These are open; each needs
the owner's go-ahead, because it reads bytes the four did not.

| Item | Where it is cited | What is wanted |
|---|---|---|
| `CPolygon::adjust_sphere_to_poly` (0x00538170 / 0x00538EB0) | physics (docs/physics.md §4) | The operands of the comparison that picks the sign of the contact distance; the decompile kept only its flags. |
| `BSPTREE::adjust_to_plane` (0x00539BF0 / 0x0053A950) | physics | The operands of the test that ends its contact-time loop early (read as "the contact time is 1"). |
| `DEFAULT_FRICTION`, `DEFAULT_ELASTICITY`, `DUMMY_SPHERE_RADIUS` | physics (`PhysicsGlobals`) | The 11.6096 data. The 11.4186 declarations give 0.94999999, 0.050000001 and 0.1 (from the review's reading, not from files the physics track may read). |
| `ContentProfile::operator==` (0x006AFEB0 / 0x006B0D80) | avatars | Whether the code compares `part_index` only, or `part_index` and `part_id`. |
| `Render::minimize_object_lighting` (0x0054D480 / 0x0054E090) and `Render::remove_object_light` (0x0054C1B0 / 0x0054CDC0) | interiors (docs/interiors.md §4.4, `TODO(interiors-light-reach)`) | The operands and sense of the comparison that keeps a static light for an object's drawing sphere. Both decompiles keep only the sum `falloff + radius` and the x87 flags of the test; Lifestone assumes distance at most `falloff + radius`. Wanted: whether it is that, or reaches as far as the hardware range (`falloff × 1.5 + radius`). About 100 bytes at 0x0054E090. |
| `MAX_INTERPOLATED_VELOCITY` (7.5), `BIG_DISTANCE` (999,999), `fUseAdjustedSpeed_` (true) | avatars | The 11.6096 data; today ACE's values. |
| `MovementSystem::GetRunRate`'s comparison with 800, and `CMotionInterp::jump_charge_is_allowed`'s Sanctuary test | avatars | The comparisons, from the 11.6096 bytes. |
| `s_WPString_Whitespace` (`StringMeta`), `FormatName`'s `CharCase` values, `BalanceAttributes`' `startAtrb` | retailui-data | The 11.6096 data. The `CharCase` and `startAtrb` citations rest on the review's reading of whole-program headers (acclient.h line 7735, acclient_data.h line 6902); 11.6096 is assumed unchanged. |
| `SoundTableData::Lookup` and `DBWave::UnPack` (§3) | portal | The jump target of the forwarder `CSoundTable::Lookup` (11.6096 0x00552D20), and the `UnPack` slot of the vtable stored by `DBWave::DBWave` (0x005526C0). |
| The three UI constructors (§3) | retailui-data | Their 11.6096 addresses. |
| The vtable slot of `SmartBox::cmdinterp` that `ReleasePressedKeys` calls; what retail's own mouse event does during that release in mouse-look mode (control 1 of the virtual mouse device, value 0x80) | retail UI runtime | Both, in 11.6096. The runtime takes the slot as `CommandInterpreter::LoseKeyboardFocus`. |
| `UIElement_ListBox::StartDragSelect` (11.4186 0x0046DB50) and `MouseUpEvent` (0x0045DD50) | retail UI runtime | Whether the list box keeping the mouse capture after the release is retail behaviour or released elsewhere (`UIElement::MouseUp`?); and whether `UIElementManager::MouseUpEvent` clears `m_bMouseHasLeftTheWindow` when the point is over the root (Lifestone's does not). |

## 4. Names in the design that are not functions

- `SoundBufRef::data_` (portal): a data member.
- `DBCache::m_MasterMapID` (portal): a static data member.
- `LayoutDesc::s_DescCache` (retail-ui): a static data member.
- `CPlayerSystem::teleportInProgress` (portal): a data member.
- `SmartBox::Handle*` (avatars): a family. Each `SmartBox::Handle…` member the plans name is in §2.
- `MIN_JUMP_EXTENT` and `CAMERA_DEFAULT_PIVOT_Z`: constants (§1).

## 5. Corrections to the design's citations

- **`CLandCell::add_cell_block`**: the physics plan cites 0x00532F20. That address is
  `CLandCell::find_env_collisions` in 11.4186. `add_cell_block` is 11.4186 0x005331D0, 11.6096 0x00533F10.
- **`SmartBox::UseTime`**: the physics survey's 11.4186 0x004542C0 is `SmartBox::UnpackPositionEvent`, as the state
  plan noted. `UseTime` is 11.4186 0x00455410, 11.6096 0x004554B0.
- **`ConstraintManager`**: the retail class is `ConstraintManager`, in module CConstraintManager.obj. The plans'
  "CConstraintManager adjust_offset" is `ConstraintManager::adjust_offset` (0x00556180 / 0x00556DA0).
- **`gmMainChatUI`**: 0x004CDD60 is the constructor `gmMainChatUI::gmMainChatUI` in 11.6096; it is 0x004CD0F0 in
  11.4186.
- **The exit window** in m2-log-in.md §4.5 ("strictly between 1.1 and 1.3 s") is 69–75 in exact arithmetic. Retail
  admits 68–76 (§1.1).
- **Unlabelled addresses.** Many plan citations name no build, and the plans mix builds: for example, R3 cites
  `LayoutDesc::InqFullDesc` in 11.4186 and `StateDesc::UpdateSizeAndPosition` in 11.6096. §2's last column says which
  build each one is.
- **`ConstraintManager::ConstrainTo`** is a TODO of the avatars track, not the physics track's: the avatars track's
  `ConstraintManager` is the code that cites it, and physics calls it through `PositionManager`.
- **`CPartArray::GetNumSphere`** (avatars) is not unmapped: the table has a predicted row at 11.6096 0x00518B90, the
  address the avatars track had guessed.
- **The godot-world camera risk** ("11.6096 addresses are known only in 11.4186") is resolved. Every `CameraSet` and
  `CameraManager` function it lists is an exact row.

## 6. Method and provenance

**The list.**

- Every `Class::method` name in the M2 synthesis (now m2-plan.md) and the ten track plans: 163 names.
- Plus the functions those plans cite by method name without the class, with or without an address: 77, each
  resolved by hand from its context. One of them, `ConstraintManager::adjust_offset`, is also named in full.
- Minus the four data members of §4, and with the `SmartBox::Handle*` family split into its members.
- Each name was looked up in the pinned table. Every address a plan wrote next to a function was matched against
  both columns.

**Read for §1 and §3.**

- From Mag-nus/acclient-AI-RE at the pinned commit, these per-file 11.4186 decompile sources:
  - `GAME/game_ui_misc/gmSmartBoxUI.c` and `gmCG3DView.c`;
  - `AC/accui_misc/CombatSystem.c`;
  - `PORTAL/smartbox/CameraSet.c`;
  - `PORTAL/clothing/ClothingTable.c`;
  - `PORTAL/canim/CMotionInterp.c`, `PORTAL/cpmanager/CConstraintManager.c`, `PORTAL/sound/DBWave.c` and
    `PORTAL/sound/cstable.c`. Only the function headers of these four were read.
- The 2015 document `13-client-differences-2013-vs-2015.md`.
- No whole-program file (acclient.c, acclient.h, the data headers) was read.
- They were fetched into session scratch, not into the repository.

**Wave 1 (the end-of-wave documentation pass).** The rows added to §2 were found by matching every pair of adjacent
addresses (and every pair of equal-length address lists in one table row) in docs/avatars.md, docs/physics.md and
docs/retail-ui-data.md against the pinned table: a pair is kept only when one table row holds both addresses. The net,
state and portal rows the tracks asked for were added by name, and `tools/research/Find-RetailSymbol.ps1` returned the
same addresses for each. The 11.4186 names in those documents agree with the table's, apart from the two folded
functions noted in §2.

**The user's `acclient.exe`** was read only as §1 describes.

Nothing in this document is decompiled code: behaviour is described in words and cited by function and address.

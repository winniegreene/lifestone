# REA tooling and acclient.exe symbol availability: findings for the Godot single-player port

## 0. Summary
- **The local `acclient.exe` is the End-of-Retail build 11.6096.** Its MD5 and SHA-256 match the hashes published in Mag-nus/acclient-AI-RE byte for byte. **No PDB exists for this build.**
- **A real Turbine PDB exists only for the September 2013 build 11.4186.** Turbine's original names have already been carried over to 11.6096 addresses by byte matching. There are three public sets:
  - a TSV with 34,562 named functions;
  - Yonneh's .map with 10,821 mangled names;
  - Chorizite/ACBindings, which has struct layouts and method addresses.
- **REA can analyse this exe on Windows**, but only through the experimental, read-only Ghidra provider. It needs rea-agents 6.0.0 or later (6.3.0 is current), Ghidra 12.1.x and a 64-bit JDK 21 or newer.
  - It cannot apply symbols, types or PDBs.
  - It re-imports and re-analyses the binary in every session.
  - Its inventory calls return everything in one response.
  - For a 3.6 MB `.text` this makes it a query layer at best. A persistent Ghidra project with the community symbol map imported is the better main tool.
- **Most client RE is already done by the community.** Physics, collision, animation, terrain, scenery, the dat formats and UI layouts are covered. The client also has **no game rules** (the server is authoritative), so single-player logic must come from ACE plus the ACE-World DB, not from acclient.
- **Direct prior art:** an earlier private prototype by the author is a single-player Godot 4 + .NET 8 AC (AGPL-3.0).

## 1. Local binary facts (verified)
I parsed the binary with the scratch script `<scratch>\research\pe_info.py`, run as `python -I`.

**Identity**
- Size 4,841,472 bytes.
- MD5 `116D9A66A70B6AF449DC3A28D82F2F6D`.
- SHA-256 `bca95bbebed4b9ed1ff09d0da83144e2fc4208f63ad7ada5cb47c3ca207ccba9`.
- FileVersion and ProductVersion are both `00.00.11.6096`. CompanyName is "Turbine, Inc.".

**Headers**
- Machine 0x014C (i386), PE32.
- TimeDateStamp 0x557A956C = 2015-06-12 08:16:44 UTC.
- Characteristics 0x012E, which includes LARGE_ADDRESS_AWARE.
- Linker 7.0, subsystem GUI 4.0.
- ImageBase 0x00400000, EntryPoint RVA 0x001DF16A, SizeOfImage 0x56D000.
- **DllCharacteristics 0x0000: no ASLR and no DEP**, so runtime addresses equal static virtual addresses.

**Data directories present:** IMPORT, RESOURCE, BASERELOC (0x48028), DEBUG, IAT. There is no TLS, no LOAD_CONFIG/SafeSEH and no exports.

**Sections**

| Section | VA | VSize | Raw offset | RawSize |
|---|---|---|---|---|
| .text | 0x1000 | 0x391CE0 | 0x1000 | |
| .rdata | 0x393000 | 0x7755E | | |
| .data | 0x40B000 | 0xFB7D4 | | 0x2D000 (large BSS) |
| .data1 | 0x507000 | | 0x438000 | |
| .rsrc | 0x509000 | | | |
| .reloc | 0x515000 | 0x57400 | | |

File offset equals RVA only for `.text`, `.rdata` and `.data`.

**Debug directory: one CODEVIEW/RSDS entry**
- GUID `08e25c14-e2a1-46d5-b056-92b2e43a7234`, age 1.
- PDB path `d:\ac1_acxp\output\bin\GAME\WIN32\retail\acclient.pdb`.
- Symbol-server key `acclient.pdb/08E25C14E2A146D5B05692B2E43A72341/`. No public PDB with this GUID is known.

**Imports: 18 DLLs, 372 functions**

| DLL | Functions |
|---|---|
| KERNEL32 | 103 |
| MSVCR70 | 112 |
| USER32 | 55 |
| WINMM | 25 |
| WSOCK32 | 18 (by ordinal) |
| RPCRT4 | 12 |
| GDI32 | 12 |
| ole32 | 7 |
| ADVAPI32 | 6 |
| MSACM32 | 6 |
| IMM32, VERSION, OLEAUT32 | 3 each |
| SHELL32 | 2 |
| zlib1.dll (`uncompress`, `compress2`) | 2 |
| COMCTL32, MSVCP70, DSOUND (#1) | 1 each |

- There is **no d3d9 import**: Direct3D 9 is loaded with `LoadLibraryA`, which matches the Mag-nus docs.
- The VC7.0 runtime (MSVCR70/MSVCP70) and Rich-header build 9466 suggest a Visual Studio .NET 2002 toolchain. Builds 3077/4035 are probably static libraries such as D3DX9 or Intel JPEG. This is an inference.

**RTTI:** only 2 RTTI type descriptors exist (`_com_error`, `type_info`). The game code was built without RTTI, so Ghidra's RecoverClassesFromRTTIScript will recover nothing useful.

## 2. REA (morluto/rea) on the development machine
**Repository facts**
- MIT licence. Main is at commit 7aa4d768, 2026-10-10.
- npm `rea-agents` latest is **6.3.0** (released 2026-10-09). Its engine range is `^22.19.0 || ^24.11.0 || >=26.0.0`, so Node 24.18 is fine.

**x86 support status**
- `docs/windows-ghidra-p0.md` still says 32-bit PE is "main only", but that text is stale.
- PR #1030 "support native x86 PE applications on Windows" merged on 2026-10-07 and shipped in **6.0.0** (2026-10-08). Use 6.0.0 or later.
- It was verified only on a tiny generated fixture: Node 22.19, Ghidra 12.1.4, JDK 21, language `x86:LE:32:default`, about 690–951 MiB peak working set. It has not been verified on a binary of this size.

**Providers on Windows**
- **Hopper:** not available on Windows.
- **Ghidra:** the "experimental Windows P0" provider.
  - Read-only, 25 operations.
  - Admits only non-DLL, non-managed x86/x64 PE applications on local fixed NTFS volumes.
  - Rejects reparse points, alternate data streams, UNC and SUBST paths, and cmd metacharacters in launch paths.
- **IDA:** works only by adapting an existing licensed IDA + Hex-Rays + Python install with `mrexodia/ida-pro-mcp`.
  - Two profiles: the legacy 1.4.0 GUI "attached" profile, or the modern idalib "database supervisor" headless profile (verified with IDA 9.3 on Windows x64).
  - Configure it by setting `REA_IDA_MCP_CONFIG` to an upstream JSON registration and `REA_ANALYSIS_PROVIDER=ida`.

**Ghidra prerequisites**
- Ghidra 12.1.x. Version 12.1.4 was released 2026-09-21 as `ghidra_12.1.4_PUBLIC_20260921.zip`, 569,649,598 bytes.
- A **64-bit full JDK** within that Ghidra's `application.java.min/max` (JDK 21 or newer, no maximum). `javac.exe` must exist.
- The native decompiler for the host must be present under `Ghidra/Features/Decompiler/os/win_x86_64/`.
- Python and PyGhidra are not needed; REA uses a packaged Java HeadlessScript bridge. Setup never installs Ghidra or Java.

**Environment variables** (complete list from `src/config/environment.ts`)
- `REA_ANALYSIS_PROVIDER`: `auto`, `ghidra` or `ida`.
- `GHIDRA_INSTALL_DIR` and `JAVA_HOME`: must be absolute paths.
- `REA_GHIDRA_STARTUP_TIMEOUT_MS`: 1 to 2,147,483,647. Default 330,000 ms, covering import + auto-analysis + bridge + health.
- `REA_MCP_MAX_RESPONSE_BYTES`: at least 10,485,760.
- `REA_IDA_MCP_CONFIG`, `REA_GHIDRA_NATIVEAOT_JAR`, `REA_ILSPY_CMD_PATH`.
- `HOPPER_*`, `REA_LOG_LEVEL`, `REA_REFERENCE_SECRET_PATTERNS_JSON`.
- Heap: `GHIDRA_HEADLESS_MAXMEM`, then `GHIDRA_MAXMEM`, otherwise `2G`.
- REA clears `_JAVA_OPTIONS`, `JAVA_TOOL_OPTIONS`, `JDK_JAVA_OPTIONS` and `GHIDRA_JAVA_OPTIONS`.

**Headless launch** (`src/ghidra/GhidraLauncher.ts`)
- Command: `analyzeHeadless.bat <runtime>\project rea-project -import <private snapshot copy> -readOnly -deleteProject -log … -scriptPath … -postScript ReaGhidraBridge.java <descriptor>`, with default analysis.
- `APPDATA` and `LOCALAPPDATA` are redirected into a private DACL-protected runtime.
- The Ghidra process tree runs in a kill-on-close Job Object.
- Transport is authenticated over 127.0.0.1 with a 256-bit bearer token.

**How `npx rea-agents setup` registers with Claude Code** (`SupportedClients.ts`, `ClientRegistrationIdentity.ts`)
- It writes `mcpServers.rea` into `%USERPROFILE%\.claude.json`, or `$CLAUDE_CONFIG_DIR\.claude.json` if that is set.
- On Windows the command is `cmd.exe /d /c npx -y rea-agents@<exact version> mcp`.
- It preserves any detected valid `GHIDRA_INSTALL_DIR` and `JAVA_HOME` in the registration env.
- It installs the skill to `~/.claude/skills/reverse-engineer-anything`.
- Useful flags: `--client claude_code`, `--dry-run --json`, `--yes`, `--skill=false`. Readiness check: `rea doctor --provider ghidra --json`.
- `REA_GHIDRA_STARTUP_TIMEOUT_MS`, `GHIDRA_HEADLESS_MAXMEM` and `REA_ANALYSIS_PROVIDER` must be added to the registration's `env` by hand. An unrelated shell's environment does not reach the MCP server.

**CLI for native analysis** (each call is its own process, with no MCP deadline)
- `rea providers --json` and `rea capabilities --json`.
- `rea analyze|inspect <exe> --provider ghidra --json`
- `rea search <exe> "text" --provider ghidra --json`
- `rea function <exe> <name|0xVA> …`, `rea decompile <exe> 0x50A940 …`, `rea xrefs …`, `rea trace <exe> "text" …`, `rea instructions …`
- `--snapshot <file.json>` caches exact query results, not the Ghidra database.

**MCP tools on the Ghidra path**
- `open_binary` with `{path, provider_id:"ghidra"}`. It only binds the target; the first query starts import and analysis.
- Inventory and navigation: `binary_overview`, `list_procedures`, `list_names`, `list_strings`, `list_segments`, `list_documents`, `search_procedures`, `search_strings` (literal or Java regex).
- Function analysis:
  - `analyze_function`: a dossier with pseudocode, assembly, callers/callees, typed references, a BasicBlockModel CFG, and up to 3,000 high-p-code ops and 12,000 def-use edges.
  - `decompile`, `read_function_instructions`, `xrefs`, `resolve_containing_procedure`, `trace_native_values`, `trace_call_path`.
- Memory: `read_bytes`, `inspect_native_load_image`.
- Evidence: `binary_session` (shows `tool_availability`), `close_binary`, `export_evidence_bundle`.
- **Result shape:** a canonical Evidence record in `structuredContent.normalized_result`, plus `evidence_id` (`ev_<64 hex>`) and `analysis_profile` (always present).
- **Errors:** `isError: true` with `{error:{code, details…}}` as text, for example `provider_timeout`, `resource_constraint` with `details.resource:"transport"`, and `cleanup_incomplete`.

**Windows caveats that matter for acclient**
1. **Read-only.** `annotate_native_function` (rename/comment) is Linux/macOS only, and even there it is discarded on close. There is no operation to apply symbols, type libraries or PDBs.
2. **External names will not appear.** Because Ghidra imports a private snapshot copy with an isolated home and APPDATA, Ghidra's PDB analyzer almost certainly cannot find a sidecar PDB or user symbol-server settings. This is an inference from the launcher code; it is moot for 11.6096, which has no PDB anyway. REA output will therefore use `FUN_xxxxxxxx` names; map them manually against the 11.6096 symbol TSV (section 3).
3. **Every session re-imports and re-analyses.** Open issue #1048 asks to "open a pre-imported Ghidra project". It records a 64 MB PE that never finished within 330 s.
   - Estimate (unverified): set `REA_GHIDRA_STARTUP_TIMEOUT_MS=3600000` and `GHIDRA_HEADLESS_MAXMEM=4G` for this 3.6 MB `.text`.
4. **Inventories have no pagination.** `list_procedures`, `list_names` and `list_strings` return everything (about 36k functions), which risks REA's 10 MiB MCP frame limit and Claude Code's `MAX_MCP_OUTPUT_TOKENS` (default 25,000; text over 50k chars is saved to a file). Prefer `search_procedures` with a regex, or CLI `--json > file`.
5. **Claude Code limits during cold start** (verified in Claude Code's env-var docs): `MCP_TOOL_TIMEOUT` defaults to about 28 h; `CLAUDE_CODE_MCP_TOOL_IDLE_TIMEOUT` is 30 min for stdio servers; `CLAUDE_CODE_MCP_AUTO_BACKGROUND_MS` backgrounds a call after 120 s.
6. **No Windows runtime or dynamic tooling.** Process capture is Linux/macOS only, there is no Frida adapter (feature request #968), and `import-reference-source` returns `unsupported_host` on Windows.
7. **PE load-image attestation is "unsupported"**; measurements are returned without it.

## 3. Symbols and headers
**The real PDB is for 11.4186 only** (Mag-nus/acclient-AI-RE, MIT, created 2026-07-28, pushed 2026-07-31).
- Matching binary: September 2013, linked 2013-09-06, 4,837,376 bytes, MD5 `2e30cd620b26f9787b4e6a6257a9e581`, build root `d:\ac1_sep13\`.
- PDB size 29.6 MB.
- Contents:
  - 16,232 global and 23,716 static function symbols, each with VA, length and owning `.obj`;
  - 1,091 module records;
  - full C11 line information (730 source paths).
- The PDB and exe are **not in any public repo** I found. They circulate in the community as an "ac-headers" bundle, described in `salvia420-bit/WorldBuilder-ACME-Edition/.../CHORIZITE_PORTING_PLAN.md`. The bundle contains:
  - `acclient.c`: Hex-Rays, 938k lines;
  - `acclient.h`: 6,936 structs and about 350–410 enums;
  - `acclient.txt`: a cvdump of the PDB, 79 MB;
  - `acclient_2013.bndb_pseudo_c.txt`: Binary Ninja output.
- How the PDB originally leaked is unknown.

**Public artefacts bound to 11.6096 addresses** (all checked against the local exe's addresses)

| Artefact | Repo | What it gives |
|---|---|---|
| `2015-10 11.6096/docs/symbol_transplant_11.6096.tsv` (3.0 MB) | Mag-nus/acclient-AI-RE | Columns `va_11.6096, va_11.4186, name, module(.obj), confidence, candidates, alt_names`. 36,087 rows: 9,520 exact, 23,783 verified, 1,259 predicted, 1,525 unmapped. Example: `0x0050A940 → CTransition::insert_into_cell` (exact). |
| `2015-10 11.6096/acclient.c` (25 MB) | Mag-nus/acclient-AI-RE | Name-stripped decompilation; `sub_XXXXXX` is the real VA. |
| `acclient 6.6.14.605 Diffed.c` (29 MB) | Mag-nus/acclient-AI-RE | BinDiff-ported names. Has known mis-ports, e.g. `PlayerModule::DisplayDateOfBirth`, which is actually the HearPKDeaths getter. |
| `2013-09 11.4186/acclient-src/` | Mag-nus/acclient-AI-RE | The 2013 decompilation split into 795 files along Turbine's PORTAL/ENGINE/GAME/AC `.obj` tree, plus `acclient.h`, `func_index.tsv`, `struct_index.txt`, and 16 subsystem reports (`docs/00`…`15`). |
| `data/yonneh.map` | Chorizite/ida-scripts (2025-03) | 10,821 MSVC-mangled names at 11.6096 VAs; 10,370 overlap the TSV. |
| `data/acclient.idc-pdb-types.zip` | Chorizite/ida-scripts | PDB types as IDC. Also includes IDA 8.2 + Diaphora scripts for porting PDB data to the EoR client. |
| `Generated/*.cs` (1,910 tree entries) | Chorizite/ACBindings (pushed 2026-01-16, no licence detected) | Unsafe C# structs and method pointers at 11.6096 VAs, e.g. `CTransition::init_path` at 0x0050A930 and static `transition_level` at 0x00842C6C. Field order comes from the PDB types; doc-comments are LLM-written. Generated by trevis/ACDecompileHallucinator. |

Further tools:
- trevis/FakePDB (Apache-2.0, a fork of Mixaill/FakePDB) can emit a PDB from an IDA database.
- ACClientLib/ACSigGen is a signature-porting proof of concept.

**Build differences, 11.4186 versus 11.6096**
- **Only 46 functions differ**, out of a population of roughly 10.2k–20.3k depending on how folded bodies are counted. Crypto, dat, rendering, UI framework, object maintenance and audio are unchanged. Physics has 3 changed functions, all `CMotionInterp` jump gating.
- **Command IDs at ordinal 0x10F and above shifted by +3** in 11.6096. This silently breaks 2013-based constants, which ACProtocol enums use.
- There is no constant address offset between the builds (+0x360 to +0x1000 across 471 steps). Use the TSV or byte matching, never arithmetic.

**Applying the names (outside REA)**
- Ghidra: import the TSV rows into a persistent project with `ImportSymbolsScript.py`, or import `yonneh.map` and let the demangler run. Optionally parse cleaned `acclient-src/include/*.h` through Ghidra's C parser for types.
- IDA: run the Chorizite scripts.

## 4. What to RE versus what already exists
**Key architectural fact** (Mag-nus `00-architecture.md` §5 and §7)
- The client is mostly an interpreter for the DAT files.
- The **server is authoritative for everything except the player's own movement**. Combat rolls, damage, spell resist and AI are absent from the client; even combat-maneuver tables, `_casting_likelihood` and `CEmoteTable` are parsed but never read.
- So single-player game rules come from **ACE** (C#, AGPL-3.0, pushed 2026-10-08, 345 stars) and the **ACE-World-16PY-Patches** DB (AGPL-3.0, pushed 2026-10-08). acclient has nothing to give here.

**Subsystem coverage**

| Subsystem | Already ported or documented | RE still worth doing |
|---|---|---|
| DAT container, BTree, zlib, all DBObj formats | ACE.DatLoader (195 files incl. LayoutDesc, Region, Scene, MotionTable, Setup, GfxObj, EnvCell); Chorizite/DatReaderWriter (MIT, release 2.1.9 on 2026-08-29; targets net8.0/netstandard2.0/net48; supports reading and writing); Mag-nus `07-dat-resources.md` | None. Use DatReaderWriter from Godot C#. |
| Physics, collision (CTransition, BSP, sphere/cylsphere, step up/down, walkable slope) | ACE `Source/ACE.Server/Physics` (162 files, about 918 KB; decompiler-derived constants such as FloorZ 0.66417414, LandingZ 0.0871557); ACViewer copy; OpenAC `AcDream.Core/Physics` (MIT) | Spot-verify only. ACE deliberately deviates: `MaxQuantum = 0.1`, where the client uses 1/5. The client gates `CPhysics::UseTime` at 30 Hz and keeps a 10-transition static pool. |
| Animation and motion (CSequence, MotionTable, MotionInterp, MoveToManager) | ACE/ACViewer `Physics/Animation`; OpenAC `MotionInterpreter.cs` (cites struct offsets); Mag-nus `01-physics` §4–5 (no keyframe interpolation; root motion and velocity quantized to frames) | Low. Mainly check the 3 jump-gating changes made in 11.6096. |
| Terrain mesh, SWtoNE diagonal hash, scenery placement, texture merge, road alpha | ACE `LandblockStruct.ConstructPolygons` (214614067/1813693831 hash), `Landblock.get_land_scenes`, `Common/TexMerge.cs`; WorldBuilder (code copied into OpenAC under MIT per its NOTICE); ACClientLib/ACTerrainWebGL (TypeScript/WebGL) | Low. Per-vertex lighting (`CLandBlockStruct::calc_lighting`) and detail-texture fade over 10–50 units are documented in Mag-nus `06-rendering` §5. |
| Rendering (fixed-function D3D9, alpha lists, LOD, portal/PView indoor visibility, sky, linear fog, day/night from the region dat) | Mag-nus `06-rendering.md`; OpenAC (Vulkan); dereth-network/dereth (Rust, MIT); acswarm (Rust, GPL-3.0); AC:Unreal (UE5) | Medium-low. The exact sky-object handling (z=−120, zfar×4), alpha blend modes and LOD degrade thresholds are documented with line refs, but expect to check them against the binary. |
| Particles and PhysicsScript | ACE `Physics/Particles`, `Physics/Scripts`; ACViewer ParticleViewer | Low. |
| UI (LayoutDesc 0x21xxxxxx trees, StateDesc, MediaMachine bytecode with opcodes 1–0xB) | ACE.DatLoader LayoutDesc; Mag-nus `05-ui.md`; OpenAC "original UI" | Medium. The widget behaviours of `UIElement_*` (2,384 functions) are the largest undocumented-in-code area. Camera behaviour (`05-ui` §8) and input maps also fall here. |
| Game rules: combat, magic, loot, AI, emotes, quests | ACE server and ACE-World DB only | **Not in acclient.** RE is impossible and unnecessary. |

Existing reimplementations to reuse or reference:
- **OpenAC** (eriknihlen, MIT, pushed 2026-10-08): a full playable client in .NET 10.
- **An earlier private prototype**: Godot 4.6.2 mono + .NET 8, single-player, reads `world.sqlite` imported from the CE16PY SQL.
- **Vitaeum_AC** (Rust).
- **acswarm**: Rust; uses a persistent-project Ghidra export pipeline (`reference/scripts/ghidra/ExportAll.java`, `run_export.sh`).
- **holtburger**: Rust/WASM.

**Verifying ports against the real client:** the exe has no ASLR, it can run against a local ACE server, and ACBindings, Chorizite.Injector and Decal give in-process hooks at fixed VAs. Logging retail `CPhysicsObj` state each tick and diffing it against the Godot port is a cheaper oracle than static RE. REA has no Windows dynamic instrumentation.

## 5. Recommended RE setup
1. **Primary tool: a persistent Ghidra 12.1.4 project** (JDK 21, `MAXMEM` 4–8G) for `<repo>\client\acclient.exe`.
   - Import `symbol_transplant_11.6096.tsv` names for exact, verified and predicted rows, with predicted names prefixed.
   - Export per-function C files and TSV dumps plus an FTS index, in the style of acswarm.
2. **Optional: REA 6.3.0 as an agent query layer.** Register with `--client claude_code` and add `env {REA_ANALYSIS_PROVIDER:"ghidra", GHIDRA_INSTALL_DIR, JAVA_HOME, REA_GHIDRA_STARTUP_TIMEOUT_MS:"3600000", GHIDRA_HEADLESS_MAXMEM:"4G"}`. Expect unnamed functions, a cold re-analysis each session, and no annotations.
3. **Read before decompiling:** start with Mag-nus `2015-10 11.6096/docs/README.md` and `13-client-differences`, then the 2013 docs `00`–`10`, then ACE Physics.

## Local artefacts
- `<scratch>\research\pe_info.py`
- `...\research\mag\symbol_transplant_11.6096.tsv` and the Mag-nus docs `00`, `01`, `05`, `06`, `07`, `13`, `15`
- `...\research\yonneh.map`
- `...\research\rea-src\` (shallow clone of REA at commit 7aa4d768)
- `...\research\rea\` (REA docs)
- `...\research\ace-tree.txt`, `openac-tree.txt`, `acclient-ai-re-tree.txt`

## KEY FACTS
- Local acclient.exe: 4,841,472 bytes, MD5 116D9A66A70B6AF449DC3A28D82F2F6D, SHA-256 bca95bbebed4b9ed1ff09d0da83144e2fc4208f63ad7ada5cb47c3ca207ccba9, FileVersion 00.00.11.6096, i386 PE32, TimeDateStamp 2015-06-12 08:16:44 UTC, ImageBase 0x400000, DllCharacteristics 0 (no ASLR/DEP), LARGE_ADDRESS_AWARE set. Source: local parse with scratch pe_info.py.
- Local exe RSDS debug entry: GUID 08e25c14-e2a1-46d5-b056-92b2e43a7234, age 1, path d:\ac1_acxp\output\bin\GAME\WIN32\retail\acclient.pdb. Source: local parse.
- Local exe imports 18 DLLs and 372 functions, including MSVCR70 (112), KERNEL32 (103) and zlib1.dll. There is no d3d9 import (D3D9 is loaded dynamically), there are no exports, and there are only 2 RTTI descriptors (the code was built without RTTI). Source: local parse; Mag-nus 15-graphics-and-media-stack.md.
- The local exe hashes exactly match Mag-nus/acclient-AI-RE's '2015-10 11.6096' build, which that repo states has no PDB. Source: github.com/Mag-nus/acclient-AI-RE README.md and CLAUDE.md.
- The only real Turbine PDB is for build 11.4186 (Sept 2013, MD5 2e30cd620b26f9787b4e6a6257a9e581, linked 2013-09-06, build root d:\ac1_sep13\, 29.6 MB). It has 16,232 global and 23,716 static function symbols, 1,091 modules and line info. It is not hosted publicly. Source: Mag-nus/acclient-AI-RE '2013-09 11.4186/docs/README.md'.
- The PDB names have been ported to 11.6096 by byte matching in symbol_transplant_11.6096.tsv: 36,087 rows (9,520 exact, 23,783 verified, 1,259 predicted, 1,525 unmapped). Source: Mag-nus/acclient-AI-RE '2015-10 11.6096/docs/symbol_transplant_11.6096.tsv' (counted locally).
- Only 46 functions changed between 11.4186 and 11.6096. Command IDs at ordinal 0x10F and above shifted by +3. Physics has 3 changed functions (CMotionInterp jump gating). Source: Mag-nus '2015-10 11.6096/docs/README.md' and '13-client-differences-2013-vs-2015.md'.
- Chorizite/ida-scripts data/yonneh.map has 10,821 mangled symbols at 11.6096 VAs (10,370 overlap the TSV), plus acclient.idc-pdb-types.zip and IDA 8.2 + Diaphora porting scripts. Source: github.com/Chorizite/ida-scripts.
- Chorizite/ACBindings provides C# structs and method pointers at 11.6096 VAs, e.g. CTransition::cleanupTransition at 0x0050A890 (the 11.6096 VA; 11.4186 had it at 0x00509DC0). Pushed 2026-01-16; no licence detected. Source: github.com/Chorizite/ACBindings Generated/CTransition.cs, cross-checked against the TSV.
- rea-agents latest on npm is 6.3.0, requiring Node ^22.19 || ^24.11 || >=26; MIT licence. Source: npm view rea-agents; morluto/rea package.json.
- Windows Ghidra support for 32-bit x86 PE arrived in rea-agents 6.0.0 (PR #1030, merged 2026-10-07), verified only on a small fixture with Ghidra 12.1.4 and JDK 21. Source: morluto/rea CHANGELOG.md and PR #1030.
- The Windows Ghidra provider is experimental and read-only (25 operations). It needs Ghidra 12.1.x and a 64-bit full JDK 21+ with javac; Python is not needed. Targets must be non-DLL x86/x64 PE apps on local NTFS without reparse/UNC/SUBST paths. Source: morluto/rea docs/windows-ghidra-p0.md and docs/installation.md.
- REA's Ghidra launch imports a private snapshot copy with -readOnly -deleteProject and default analysis, in a fresh project each session. The startup deadline defaults to 330,000 ms (REA_GHIDRA_STARTUP_TIMEOUT_MS). Pre-imported projects are an open request (issue #1048). Source: morluto/rea src/ghidra/GhidraLauncher.ts, docs/installation.md, issue #1048.
- REA's Ghidra inventories (list_procedures, list_names, list_strings) return the complete collection with no pagination, and MCP responses are capped at 10 MiB by default (REA_MCP_MAX_RESPONSE_BYTES). Source: morluto/rea docs/ghidra-provider.md, docs/mcp-contracts.md.
- npx rea-agents setup for Claude Code writes mcpServers.rea into %USERPROFILE%\.claude.json (or CLAUDE_CONFIG_DIR) as 'cmd.exe /d /c npx -y rea-agents@<ver> mcp', and installs the skill under ~/.claude/skills. Source: morluto/rea src/application/SupportedClients.ts, ClientRegistrationIdentity.ts, docs/installation.md.
- REA environment variables: REA_ANALYSIS_PROVIDER, REA_MCP_MAX_RESPONSE_BYTES, REA_IDA_MCP_CONFIG, GHIDRA_INSTALL_DIR, JAVA_HOME, REA_GHIDRA_NATIVEAOT_JAR, REA_GHIDRA_STARTUP_TIMEOUT_MS, REA_ILSPY_CMD_PATH, HOPPER_*, REA_LOG_LEVEL, REA_REFERENCE_SECRET_PATTERNS_JSON; Ghidra heap via GHIDRA_HEADLESS_MAXMEM, then GHIDRA_MAXMEM, else 2G. Source: morluto/rea src/config/environment.ts, docs/installation.md.
- Ghidra 12.1.4 was released 2026-09-21 (ghidra_12.1.4_PUBLIC_20260921.zip, 569,649,598 bytes). Source: gh release list -R NationalSecurityAgency/ghidra.
- Claude Code: MCP_TOOL_TIMEOUT defaults to about 28 h; the stdio idle timeout is 30 min (CLAUDE_CODE_MCP_TOOL_IDLE_TIMEOUT); calls move to the background after 120 s; MAX_MCP_OUTPUT_TOKENS defaults to 25,000. Source: code.claude.com/docs/en/env-vars.
- The client is authoritative only for its own movement. Combat, magic and AI are server-side, and several rule tables are parsed but never read. Source: Mag-nus '2013-09 11.4186/docs/00-architecture.md' section 7.
- ACE physics port: FloorZ 0.66417414618662751, LandingZ 0.0871557, MinQuantum 1/30, but MaxQuantum 0.1 versus the client's 1/5. It also contains get_land_scenes (scenery) and the ConstructPolygons diagonal hash. Source: ACEmulator/ACE Source/ACE.Server/Physics/PhysicsGlobals.cs, Common/Landblock.cs, Common/LandblockStruct.cs; Mag-nus 01-physics.md.
- OpenAC (eriknihlen/OpenAC, MIT, pushed 2026-10-08) is a playable .NET 10/Vulkan AC client that reproduces terrain, scenery, sky, weather, physics, spells and the original UI against ACE. Its rendering and dat code is partly copied from Chorizite/WorldBuilder under MIT. Source: OpenAC README.md and NOTICE.md.
- An earlier private prototype by the author (AGPL-3.0) is a single-player AC recreation in Godot 4.6.2 mono + .NET 8, using retail DATs plus an ACE-World CE16PY SQL import into world.sqlite.
- Chorizite/DatReaderWriter (MIT, release 2.1.9 on 2026-08-29) reads and writes all end-of-retail dats and targets net8.0/netstandard2.0/net48. Source: github.com/Chorizite/DatReaderWriter README.

## RECOMMENDATIONS
- Do not wait for a PDB matching 11.6096; none exists. Use Mag-nus symbol_transplant_11.6096.tsv (34,562 named entries, filtered to exact/verified first) as the name source, with yonneh.map and ACBindings as cross-checks.
- For the main RE workflow, install Ghidra 12.1.4 and a 64-bit JDK 21 (user approval needed) and keep a persistent Ghidra project for acclient.exe with the TSV names imported. Export per-function decompilation to text and an index (as acswarm does) so agents can grep it without re-analysis.
- If REA is used, require rea-agents >= 6.0.0 (pin 6.3.0) and register with 'npx rea-agents@6.3.0 setup --client claude_code --dry-run --json' first. Add env REA_ANALYSIS_PROVIDER=ghidra, GHIDRA_INSTALL_DIR, JAVA_HOME, REA_GHIDRA_STARTUP_TIMEOUT_MS=3600000 and GHIDRA_HEADLESS_MAXMEM=4G to the registration's env block, then restart Claude Code.
- Within REA, avoid list_procedures, list_names and list_strings on acclient. Use search_procedures with a regex, analyze_function or decompile with explicit 0x VAs taken from the TSV, and the CLI with --json redirected to files for bulk output. Expect unnamed functions and one cold analysis per session.
- Treat ACE physics, ACE.DatLoader, Chorizite/DatReaderWriter, Mag-nus docs 00–15 and OpenAC (MIT) as the starting point. Limit binary RE to verifying specific constants and behaviours where the ports diverge (e.g. ACE MaxQuantum 0.1 vs the client's 0.2) and to UI widget behaviour and camera.
- Source single-player game rules (combat, magic, loot, AI, emotes) from ACE plus the ACE-World-16PY-Patches DB, not from acclient, because the client has no server-side logic. Plan around AGPL-3.0 if any ACE code is reused rather than only read.
- Use the retail client against a local ACE server as a runtime oracle. Since there is no ASLR, hook fixed VAs (ACBindings, Chorizite.Injector or Decal) to log physics and animation state per tick and diff it against the Godot implementation.
- Do not use Ghidra's RTTI class-recovery scripts; the binary has only 2 RTTI descriptors. Get class layouts from acclient.h, ACBindings or the PDB-types IDC instead.

## RISKS
- REA's Windows x86 path has only been verified on a tiny fixture. How long a cold Ghidra auto-analysis of acclient's 3.6 MB .text takes under REA on the development machine is unmeasured; the 1-hour deadline and 4G heap are estimates.
- That REA's Ghidra import cannot find sidecar PDBs or symbol-server settings is inferred from the launcher code (private snapshot copy, isolated APPDATA/home), not tested. It matters only for the 11.4186 exe+PDB, which the user does not have.
- The symbol_transplant TSV's 'predicted' rows (1,259) and the BinDiff-ported 2015 decompilation contain known mis-ports. Names should be confirmed against the bytes before relying on them.
- The provenance and redistribution status of the 11.4186 PDB and the Hex-Rays decompilations (acclient.c/.h) are unclear. They are decompiled Turbine code, so copying them into a distributable Godot project could be a legal problem; use them as reference only.
- Licences differ: ACE and ACE-World are AGPL-3.0, ACViewer GPL-3.0, OpenAC and DatReaderWriter MIT, the earlier prototype AGPL-3.0, Mag-nus repo MIT (wrapping decompiled code). ACBindings and Chorizite/WorldBuilder had no licence detected by the GitHub API, although OpenAC's NOTICE says WorldBuilder is MIT.
- client_highres.dat is absent from <repo>\client. DatReaderWriter's DatCollection, OpenAC and the earlier prototype expect it; it may be inside ac-updates.zip or ac1install.exe (not inspected).
- The Rich-header toolchain attribution (VS2002 build 9466 plus VS2003-era static libs) is inferred, and whether Ghidra Function ID databases cover the VC7.0 CRT is unknown.
- GitHub code search was rate-limited (shared token). Other public mirrors of acclient.h/.c or other Godot/Unity ports may exist that were not found. No Unity or three.js AC client turned up beyond ACClientLib/ACTerrainWebGL (terrain-only WebGL) and holtburger-web (Rust/WASM + Three.js).
- Mag-nus documentation was produced with AI assistance and self-reports earlier errors; its line-number citations refer to the 11.4186 decompilation, not the local 11.6096 exe.

## SOURCES
- https://github.com/morluto/rea (README.md, docs/installation.md, docs/windows-ghidra-p0.md, docs/cli.md, docs/ida-provider.md, docs/mcp-contracts.md, docs/ghidra-provider.md, CHANGELOG.md, package.json, skill-src/reverse-engineer-anything/SKILL.md, src/config/environment.ts, src/ghidra/GhidraLauncher.ts, src/ghidra/GhidraProviderCapabilities.ts, src/application/SupportedClients.ts, src/application/ClientRegistrationIdentity.ts) at commit 7aa4d768
- https://github.com/morluto/rea/pull/1030
- https://github.com/morluto/rea/issues/1048
- https://github.com/morluto/rea/issues/1290
- https://github.com/morluto/rea/issues/968
- npm registry: rea-agents dist-tags (latest 6.3.0)
- https://github.com/NationalSecurityAgency/ghidra/releases/tag/Ghidra_12.1.4_build
- https://code.claude.com/docs/en/env-vars
- https://github.com/Mag-nus/acclient-AI-RE (README.md, CLAUDE.md, 2015-10 11.6096/docs/README.md, 13-client-differences-2013-vs-2015.md, symbol_transplant_11.6096.tsv, 2013-09 11.4186/docs/README.md, 00-architecture.md, 01-physics.md, 05-ui.md, 06-rendering.md, 07-dat-resources.md)
- https://github.com/Chorizite/ida-scripts (README.md, data/yonneh.map)
- https://github.com/Chorizite/ACBindings (README.md, Generated/CTransition.cs)
- https://github.com/trevis/ACDecompileHallucinator
- https://github.com/trevis/FakePDB
- https://github.com/ACClientLib/ACSigGen
- https://github.com/ACClientLib/ACTerrainWebGL
- https://github.com/salvia420-bit/WorldBuilder-ACME-Edition/blob/HEAD/external/holtburger/apps/holtburger-web/CHORIZITE_PORTING_PLAN.md
- https://github.com/ACEmulator/ACE (Source/ACE.Server/Physics/PhysicsGlobals.cs, Physics/Common/Landblock.cs, Physics/Common/LandblockStruct.cs, Physics/Animation/MotionInterp.cs, ACE.DatLoader/FileTypes)
- https://github.com/ACEmulator/ACViewer
- https://github.com/ACEmulator/ACE-World-16PY-Patches
- https://github.com/Chorizite/DatReaderWriter
- https://github.com/eriknihlen/OpenAC (README.md, NOTICE.md, src/AcDream.Core/Physics/MotionInterpreter.cs)
- https://github.com/enknamel/acswarm (reference/README.md, reference/scripts/ghidra/run_export.sh)
- https://github.com/dereth-network/dereth
- https://github.com/CrimsonMage/Vitaeum_AC
- https://github.com/lkasser/Decal-ACUnreal
- https://github.com/Mag-nus/Mag-ACClientPatcher
- https://github.com/amoeba/alembic
- Local: <repo>\client\acclient.exe (parsed via <scratch>\research\pe_info.py)
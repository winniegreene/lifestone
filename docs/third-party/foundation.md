# Third-party sources: M2 foundation (lead, wave 0)

Wave 0 of M2 ([m2-plan.md](../milestones/m2-plan.md) §2) adds the shared types and helpers every M2 track builds on:
the language-dat records in `DatSet`, the enum-map resolver, the world and sync types, the motion data types, the
logging seam, the readiness contract and the shared test helpers. One group of files is ported, the motion data types;
everything else is re-expressed from the retail decompile or written for Lifestone. Lifestone is AGPL-3.0, and every
source below is compatible with that. Nothing derived from the user's dats is committed.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d (`vendor-eval/ACE`) | **Ported:** the motion data types (below). |
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` (Hex-Rays output with PDB names) | Turbine/WB IP; reference only | fetched 2026-10-10 | Behaviour of the functions listed below. Nothing copied. |
| Mag-nus/acclient-AI-RE, `symbol_transplant_11.6096.tsv` | Turbine/WB IP; reference only | commit 01ccd5be, SHA-256 cc19490d…; [retail-addresses-m2.md](../research/retail-addresses-m2.md), "The pin" | The 11.6096 addresses cited below. |
| Chorizite/DatReaderWriter | MIT | NuGet 2.1.9 (already a project dependency) | Used, not ported: `DatSet` decodes language-dat records with a `DatBinReader` bound to the portal database, whose master property table `BaseProperty.UnpackGeneric` reads. |

## Files

### Ported (header names the source)

ACEmulator/ACE, AGPL-3.0, @95ef426d. Each file carries its own single-source header (the first lines of the file):

```text
RawMotionState.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/RawMotionState.cs (AGPL-3.0)
// with changes: data and defaults only (the fields, InitDefaults and a Clone); the behaviour (ApplyMotion, RemoveMotion
// and the rest) is added by the motion owner in its own partial files.

InterpretedMotionState.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/InterpretedMotionState.cs (AGPL-3.0)
// with changes: data and defaults only (the fields, InitDefaults and a Clone); the behaviour (ApplyMotion, RemoveMotion
// and the rest) is added by the motion owner in its own partial files.

MotionActionNode.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/ActionNode.cs (AGPL-3.0)
// with changes: data and defaults only; an immutable record struct, and the stamp is the 16-bit wire stamp (ACE keeps
// an int).

HoldKey.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/HoldKey.cs (AGPL-3.0)
// with changes: data and defaults only; XML docs added.
```

| Lifestone file | ACE source (under `Source/`) | Notes |
|---|---|---|
| `src/Lifestone.Data/Motion/RawMotionState.cs` | `ACE.Server/Physics/Animation/RawMotionState.cs` | The fields, `InitDefaults` and a `Clone`: hold key None, style NonCombat 0x8000003D, forward command Ready 0x41000003, no sidestep or turn, per-motion hold keys Invalid, speeds 1, and the action list. A partial class; the behaviour (`ApplyMotion`, `RemoveMotion` and the rest) is the avatars track's. |
| `src/Lifestone.Data/Motion/InterpretedMotionState.cs` | `ACE.Server/Physics/Animation/InterpretedMotionState.cs` | The fields, `InitDefaults` and a `Clone`: style, forward, sidestep and turn commands with their speeds, and the action list. A partial class, as above. |
| `src/Lifestone.Data/Motion/MotionActionNode.cs` | `ACE.Server/Physics/Animation/ActionNode.cs` | Renamed. A readonly record struct of the action, its speed, its 16-bit stamp (ACE keeps an int) and its autonomous flag. |
| `src/Lifestone.Data/Motion/HoldKey.cs` | `ACE.Entity/Enum/HoldKey.cs` | The `HoldKey` enum: Invalid 0, None 1, Run 2. |

The motion behaviour that uses these types (`CMotionInterp` and the position managers) is re-expressed from the
retail decompile by the avatars track and listed in its own file.

### Re-expressed retail behaviour (each type cites its functions)

| Lifestone file | Retail functions | Address |
|---|---|---|
| `src/Lifestone.Data/Dat/EnumIdMaps.cs` | `DBObj::GetDIDByEnum`, `DBCache::GetDIDFromEnum` (client map first, then the server map: retail's `EnumToIDInternal`) | 11.6096 0x00415640, 0x00413BF0 |
| `src/Lifestone.Data/World/Sequence16.cs` | `CPhysicsObj::is_newer`, `CPhysicsObj::newer_event` | 11.4186 0x00451AD0, 0x00451B10; 11.6096 0x00451B10, 0x00451B50 (L10 table, [retail-addresses-m2.md](../research/retail-addresses-m2.md)) |

`src/Lifestone.Data/World/LandDefs.cs` moves the `LandDefs` code out of `AcCoordinates.cs` unchanged; its sources are
listed in [core.md](core.md). `src/Lifestone.Data/Effects/ObjectClock.cs` is extracted from `EffectActor.cs`; its
sources are listed in [effects.md](effects.md).

## Own implementations (no source copied)

- **Language-dat records** (`DatSet.TryGetLocal`, `RequireLocal`): own code over DatReaderWriter's public reader.
- **`CellFrame`**, **`CheckoutLocator`**, the **logging seam** (`ILogSink`), the **readiness contract**
  (`IWorldAttachments`, `AttachState`) and the shared **test helpers** (`ManualTimeProvider`, `RecordingLogSink`, the
  loopback gate and lock): written for Lifestone.

- Declared engine errors (M5 0 fix): InteriorEngineChecks logs `[interiorcheck] expected engine errors begin text=... count=<n>` (two kinds, n=11 each,
  `GlobalParameterBlocks = 1`) and `... end`; smoke matches Godot error lines by text anywhere in the console (stdout and stderr are two log blocks)
  and prints `godot_errors=0 expected_engine_errors=22/22`; seen != declared, an unclosed declaration or any other error fails. `tools/soak-watch.ps1` is in StepPaths NoGodot.

@{
    # tools\slice-check.ps1 -Changed: which backend-free smoke steps a changed file can affect (M3 4.2 p5a, C7; PACKAGE-3 cut 10). Prefixes are repository-relative with forward slashes.
    # Fail-safe: a changed path under src/ or tools/ that no list below matches runs EVERY backend-free step (and is named), never none. Steps named with -Steps stay a floor.
    # The guard tests (StepPathMapTests) pin: every key is a smoke step, every backend-free smoke step is a key, every source folder is in some list.

    Steps = @{
        'cli-help'               = @('src/Lifestone.Client/Options/', 'src/Lifestone.Game/scripts/Core/GameOptions.cs', 'src/Lifestone.Game/scripts/Main.cs', 'src/Lifestone.Game/scripts/Session/Autopilot')   # M5 4 c1: AutopilotHost.cs / AutopilotLegRunner.cs register and read the --autopilot-* options of the Main scene
        'cli-bad-argument'       = @('src/Lifestone.Client/Options/', 'src/Lifestone.Game/scripts/Core/GameOptions.cs', 'src/Lifestone.Game/scripts/Main.cs', 'src/Lifestone.Game/scripts/Session/Autopilot')
        'terrain-golden'         = @('src/Lifestone.Game/shaders/', 'src/Lifestone.Game/scripts/Render/', 'src/Lifestone.Client/Rendering/', 'src/Lifestone.Data/Terrain/', 'src/Lifestone.Data/Textures/', 'tools/terrain-golden.ps1')
        'engine-checks'          = @('src/Lifestone.Game/scripts/Render/', 'src/Lifestone.Client/Rendering/', 'src/Lifestone.Game/scenes/')
        'interior-engine-checks' = @('src/Lifestone.Game/scripts/World/', 'src/Lifestone.Client/Interiors/', 'src/Lifestone.Data/World/')
        'sky-checks'             = @('src/Lifestone.Game/scripts/World/', 'src/Lifestone.Client/Sky/')
        'blend-checks'           = @('src/Lifestone.Game/scripts/Render/', 'src/Lifestone.Game/shaders/', 'src/Lifestone.Client/Textures/', 'src/Lifestone.Client/Rendering/', 'src/Lifestone.Data/Textures/')
        'portal-space-checks'    = @('src/Lifestone.Game/scripts/PortalSpace/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/Cinematic/', 'src/Lifestone.Game/cinematics/', 'src/Lifestone.Client/PortalSpace/', 'src/Lifestone.Client/Audio/', 'src/Lifestone.Client/Cinematic/', 'src/Lifestone.Data/Audio/')
        'portal-space'           = @('src/Lifestone.Game/scripts/PortalSpace/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/Cinematic/', 'src/Lifestone.Game/cinematics/', 'src/Lifestone.Client/PortalSpace/', 'src/Lifestone.Client/Audio/', 'src/Lifestone.Client/Cinematic/', 'src/Lifestone.Data/Audio/')
        'retail-ui'              = @('src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Client/RetailUi/', 'src/Lifestone.Data/Ui/')
        'offline-player-checks'  = @('src/Lifestone.Game/scripts/Player/', 'src/Lifestone.Game/scripts/CreatureMode/', 'src/Lifestone.Client/Avatars/', 'src/Lifestone.Client/Movement/', 'src/Lifestone.Client/Navigation/', 'src/Lifestone.Client/Camera/', 'src/Lifestone.Client/Objects/', 'src/Lifestone.Client/Physics/', 'src/Lifestone.Data/Physics/', 'src/Lifestone.Data/Motion/', 'src/Lifestone.Data/Animation/', 'src/Lifestone.Data/Appearance/', 'src/Lifestone.Data/CharGen/')
        'offline-player'         = @('src/Lifestone.Game/scripts/Player/', 'src/Lifestone.Game/scripts/CreatureMode/', 'src/Lifestone.Client/Avatars/', 'src/Lifestone.Client/Movement/', 'src/Lifestone.Client/Navigation/', 'src/Lifestone.Client/Camera/', 'src/Lifestone.Client/Objects/', 'src/Lifestone.Client/Physics/', 'src/Lifestone.Data/Physics/', 'src/Lifestone.Data/Motion/', 'src/Lifestone.Data/Animation/', 'src/Lifestone.Data/Appearance/', 'src/Lifestone.Data/CharGen/')
        'play-checks'            = @('src/Lifestone.Game/scripts/Session/Autopilot', 'src/Lifestone.Game/scripts/Main.cs', 'src/Lifestone.Client/Camera/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Client/Play/', 'src/Lifestone.Client/RetailUi/', 'src/Lifestone.Client/Audio/', 'src/Lifestone.Client/Effects/', 'src/Lifestone.Client/Avatars/', 'src/Lifestone.Client/Objects/', 'src/Lifestone.Net/', 'src/Lifestone.Data/Magic/', 'src/Lifestone.Data/Effects/')
        'record-audio'           = @('src/Lifestone.Game/scripts/Main.cs', 'src/Lifestone.Client/Camera/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Client/Play/', 'src/Lifestone.Client/RetailUi/', 'src/Lifestone.Client/Audio/', 'src/Lifestone.Client/Effects/', 'src/Lifestone.Client/Avatars/', 'src/Lifestone.Client/Objects/', 'src/Lifestone.Net/', 'src/Lifestone.Data/Magic/', 'src/Lifestone.Data/Effects/')
        'shell-frames'           = @('src/Lifestone.Game/scripts/Main.cs', 'src/Lifestone.Client/Camera/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Client/Play/', 'src/Lifestone.Client/RetailUi/', 'src/Lifestone.Client/Audio/', 'src/Lifestone.Client/Effects/', 'src/Lifestone.Client/Avatars/', 'src/Lifestone.Client/Objects/', 'src/Lifestone.Net/', 'src/Lifestone.Data/Magic/', 'src/Lifestone.Data/Effects/')
        'spell-showcase'         = @('src/Lifestone.Game/scripts/SpellViewer/', 'src/Lifestone.Client/Effects/', 'src/Lifestone.Data/Magic/', 'src/Lifestone.Data/Effects/')
        'golden-viewpoints'      = @('src/Lifestone.Game/scripts/World/', 'src/Lifestone.Game/scripts/Render/', 'src/Lifestone.Client/Streaming/', 'src/Lifestone.Client/World/', 'src/Lifestone.Client/Interiors/', 'src/Lifestone.Client/Sky/', 'src/Lifestone.Data/World/', 'src/Lifestone.Data/Terrain/')
        'fly-path'               = @('src/Lifestone.Game/scripts/World/', 'src/Lifestone.Game/scripts/Render/', 'src/Lifestone.Client/Streaming/', 'src/Lifestone.Client/World/', 'src/Lifestone.Client/Interiors/', 'src/Lifestone.Client/Sky/', 'src/Lifestone.Data/World/', 'src/Lifestone.Data/Terrain/')
    }

    # Cross-cutting paths: a change here can move any step, so every backend-free step runs.
    Everything = @(
        'src/Lifestone.Data/Dat/', 'src/Lifestone.Data/Geometry/', 'src/Lifestone.Data/Models/', 'src/Lifestone.Data/Checkout/',
        'src/Lifestone.Game/scripts/Core/', 'src/Lifestone.Game/scripts/UI/', 'src/Lifestone.Game/scripts/Diagnostics/',
        'src/Lifestone.Client/Diagnostics/', 'src/Lifestone.Client/Caching/', 'src/Lifestone.Client/Scheduling/',
        'src/Lifestone.Game/project.godot', 'src/Lifestone.Game/Lifestone.Game.csproj', 'src/Lifestone.Client/Lifestone.Client.csproj', 'src/Lifestone.Data/Lifestone.Data.csproj',
        'src/Lifestone.Net/Lifestone.Net.csproj', 'Directory.Build.props', 'global.json', 'Lifestone.slnx', 'tools/smoke.ps1', 'tools/godot.ps1', 'tools/lib/Toolchain.psm1'
    )

    # Paths no Godot step can notice (documents, tests, the server, tooling that is not a step).
    NoGodot = @(
        'docs/', 'tests/', 'server/', 'README.md', 'CLAUDE.md', 'LICENSE', '.github/', '.gitignore', '.gitattributes',
        'src/Lifestone.Headless/', 'src/Lifestone.Client/LegalNotices.cs',
        'src/Lifestone.Game/scripts/Main.cs.uid',
        'tools/slice-check.ps1', 'tools/commit.ps1', 'tools/live-gate.ps1', 'tools/lib/GateAreas.psd1', 'tools/hero-video.ps1', 'tools/installer/', 'tools/protocol/', 'tools/research/', 'tools/data/',
        'tools/lib/StepPaths.psd1', 'tools/lib/TestTimes.psm1', 'tools/lib/GateJudge.psm1', 'tools/lib/GateHost.psm1', 'tools/soak-watch.ps1',
        'tools/fight-sim.ps1', 'tools/fight-sim/',   # the fight simulator (docs/fight-sim.md): ACE's combat code run offline, outside Lifestone.slnx; no Godot step reads it
        'src/Lifestone.Client/Session/Autopilot/'   # M5 4 c1: the autopilot's logic is covered by the tests (AutopilotTests, AutopilotHostTests, the scripted ACE); its Godot rows are play-checks' AutopilotDialAsync, which the Session/ prefixes above already select
    )

    # The steps that need the backend: slice-check never runs them; a change that touches these paths only prints one advisory line.
    Live = @{
        'login-check' = @('src/Lifestone.Net/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/')
        'login'       = @('src/Lifestone.Net/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/')
        # M4 P5b G1: the three journeys (backend steps on the fighters' accounts; they replace the retired buff, den, death and hunt steps); they run only at the lead's gate, so slice-check prints the advisory.
        'journey-mage' = @('src/Lifestone.Net/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/')
        'journey-melee' = @('src/Lifestone.Net/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/')
        'journey-archer' = @('src/Lifestone.Net/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/')
        # M5 item 4 c1 (D19): the autopilot lane (smoke autopilot-mage, 30 minutes on BSD Mage); the lead's gate runs it, so slice-check prints the advisory.
        'autopilot-mage' = @('src/Lifestone.Net/', 'src/Lifestone.Client/Session/', 'src/Lifestone.Game/scripts/Session/', 'src/Lifestone.Game/scripts/RetailUi/', 'src/Lifestone.Game/scripts/Audio/', 'src/Lifestone.Game/scripts/PortalSpace/')
    }
}

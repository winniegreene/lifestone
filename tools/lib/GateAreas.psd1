@{
    # tools\live-gate.ps1 -Loopback core|<areas>: which loopback test classes a gate runs (M4 P4 gate, D31). Read by the runner and pinned by LiveGateRunnerTests.
    # The classes themselves (their account, whether they are solo-lane) are read from the test sources, so this file holds only names: an unknown name makes the runner throw.
    # Prefixes are repository-relative with forward slashes, matched as tools\lib\StepPaths.psd1 matches them. A changed tests\Lifestone.Loopback.Tests file selects the classes it declares.

    # Always run under -Loopback core: the cheap sentinel of each lane that still has loopback classes (P5b G2: the test-melee lane has none; its journey is the gate's; LivePhaseB is the solo phase's precision sentinel, the last of HoltburgLaps' job).
    Core = @('LoopbackGateTests', 'HeadlessLoopbackTests', 'LivePhaseBTests', 'InventoryLiveTests', 'ShortcutLiveTests')

    # P5b G2 (the journeys gate, design cache/m4-p5-design.md 3.4/3.5): classes whose class-level Lane=solo trait the gate does not honour for the account lane (they run there, with their solo walks left out by RetiredFacts).
    InLane = @('AcademyExitLoopbackTests', 'HoltburgLapsLoopbackTests')   # HoltburgLaps: its jumps are not in login-check's legs yet, so its laps and jumps run in the test lane (the lead, D28)

    # Single facts (a test method's name or a unique part of it) that no lane runs, appended to every lane filter as FullyQualifiedName!~<fact>. None since P5b J6 deleted the three it listed.
    RetiredFacts = @()

    # area name (what -Loopback accepts) -> the source folders that select it and the classes it runs.
    Areas = @{
        'net'       = @{
            Paths   = @('src/Lifestone.Net/')
            Classes = @('LoopbackGateTests', 'HeadlessLoopbackTests', 'LivePhaseATests', 'LivePhaseBTests', 'AcademyExitLoopbackTests')
        }
        'movement'  = @{
            Paths   = @('src/Lifestone.Client/Movement/', 'src/Lifestone.Client/Navigation/', 'src/Lifestone.Client/Camera/', 'src/Lifestone.Data/Physics/', 'src/Lifestone.Data/Motion/', 'src/Lifestone.Data/Animation/')
            Classes = @('AcademyExitLoopbackTests', 'LivePhaseBTests')
        }
        'actions'   = @{
            Paths   = @('src/Lifestone.Client/Session/Actions/', 'src/Lifestone.Client/Session/Contracts/', 'src/Lifestone.Client/Session/State/', 'src/Lifestone.Client/Objects/')
            Classes = @('InventoryLiveTests', 'ItemMovesLiveTests', 'StatsLiveTests', 'ContainerLiveTests', 'ShortcutLiveTests', 'KeyChestLiveTests')
        }
        # The fighters' and the mage's abilities are exercised by the journeys (the default gate always runs all three, smoke journey-*), not by loopback classes: these two areas keep their folders and run only what is left.
        'fighter'   = @{
            Paths   = @('src/Lifestone.Client/Session/Fighter/', 'src/Lifestone.Game/scripts/Session/HuntLegRunner.cs', 'src/Lifestone.Game/scripts/Session/GameApproachWalk.cs', 'src/Lifestone.Game/scripts/Session/JourneyLegRunner.cs')
            Classes = @('FighterBuildLoopbackTests')
        }
        'mage'      = @{
            Paths   = @('src/Lifestone.Client/Session/Mage/', 'src/Lifestone.Client/Magic/', 'src/Lifestone.Client/Session/Scenarios/Journey/', 'src/Lifestone.Client/Session/Autopilot/', 'src/Lifestone.Game/scripts/Session/Autopilot')
            # M5 0 c4, M5 1 K3, M5 4 c1: Lane=solo, so the account lanes leave them out (the lead runs them); named so that R12 sees them in an area. AutopilotLiveTests (M5 4 h1) was tagged Tier=Soak only to
            # get past R12; R12 exempts a Soak class because the soak phase (Category=Loopback&Tier=Soak) runs it, and this one is a 40-minute lead's run on the mage, not the soak (item 7's night), so its tag is gone and
            # it is named here, in the mage area, with the autopilot's folders: a change to the autopilot selects it under -Loopback core, and the lane filter's Lane!=solo still leaves it out of the account lanes.
            Classes = @('DenMeasureLiveTests', 'KillSwitchBuffLiveTests', 'AutopilotLiveTests')
        }
        'servermod' = @{
            Paths   = @('server/mod/')
            Classes = @('ModVerbsLiveTests', 'ModVerbsThrowawayLiveTests', 'ModCharPurgeLiveTests', 'ModXpLiveTests', 'FighterBuildLoopbackTests')
        }
    }

    # A changed path here can move any class: -Loopback core then runs the whole suite and says so (the fail-safe, as StepPaths.psd1's Everything).
    Everything = @(
        'Directory.Build.props', 'global.json', 'Lifestone.slnx', 'src/Lifestone.Net/Lifestone.Net.csproj', 'src/Lifestone.Client/Lifestone.Client.csproj',
        'src/Lifestone.Client/Session/GameSession.cs', 'src/Lifestone.Client/Session/SessionBootstrap.cs', 'src/Lifestone.Client/Session/SessionOptions.cs', 'src/Lifestone.Client/Session/Hosting/',
        'src/Lifestone.Client/Session/Credentials/', 'src/Lifestone.Client/Session/Scenarios/', 'src/Lifestone.Headless/', 'tests/Shared/Loopback/', 'tools/live-gate.ps1', 'tools/lib/GateAreas.psd1'
    )
}

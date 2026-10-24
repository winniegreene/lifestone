@{
    # tools\live-gate.ps1 -Loopback core|<areas>: which loopback test classes a gate runs (M4 P4 gate, D31). Read by the runner and pinned by LiveGateRunnerTests.
    # The classes themselves (their account, whether they are solo-lane) are read from the test sources, so this file holds only names: an unknown name makes the runner throw.
    # Prefixes are repository-relative with forward slashes, matched as tools\lib\StepPaths.psd1 matches them. A changed tests\Lifestone.Loopback.Tests file selects the classes it declares.

    # Always run under -Loopback core: the cheap sentinel of each account and of the solo precision runs.
    Core = @('LoopbackGateTests', 'HeadlessLoopbackTests', 'InventoryLiveTests', 'MeleeLiveTests', 'MissileLiveTests', 'HoltburgLapsLoopbackTests')

    # area name (what -Loopback accepts) -> the source folders that select it and the classes it runs.
    Areas = @{
        'net'       = @{
            Paths   = @('src/Lifestone.Net/')
            Classes = @('LoopbackGateTests', 'HeadlessLoopbackTests', 'LivePhaseATests', 'LivePhaseBTests', 'AcademyExitLoopbackTests', 'HoltburgLapsLoopbackTests')
        }
        'movement'  = @{
            Paths   = @('src/Lifestone.Client/Movement/', 'src/Lifestone.Client/Navigation/', 'src/Lifestone.Client/Camera/', 'src/Lifestone.Data/Physics/', 'src/Lifestone.Data/Motion/', 'src/Lifestone.Data/Animation/')
            Classes = @('HoltburgLapsLoopbackTests', 'AcademyExitLoopbackTests', 'LivePhaseBTests')
        }
        'actions'   = @{
            Paths   = @('src/Lifestone.Client/Session/Actions/', 'src/Lifestone.Client/Session/Contracts/', 'src/Lifestone.Client/Session/State/', 'src/Lifestone.Client/Objects/')
            Classes = @('InventoryLiveTests', 'ItemMovesLiveTests', 'StatsLiveTests', 'ContainerLiveTests', 'ContainerCorpseLiveTests', 'AppraiseLiveTests', 'ShortcutLiveTests', 'VendorLiveTests', 'HealLiveTests', 'HealBrawlerLiveTests', 'HealArcherLiveTests')
        }
        'fighter'   = @{
            Paths   = @('src/Lifestone.Client/Session/Fighter/', 'src/Lifestone.Game/scripts/Session/HuntLegRunner.cs', 'src/Lifestone.Game/scripts/Session/GameApproachWalk.cs')
            Classes = @('HuntLiveTests', 'MeleeLiveTests', 'MissileLiveTests', 'HealBrawlerLiveTests', 'HealArcherLiveTests', 'ContainerCorpseLiveTests', 'VendorLiveTests', 'AppraiseLiveTests', 'FighterBuildLoopbackTests', 'FighterBrawlerBuildLoopbackTests', 'FighterArcherBuildLoopbackTests', 'FighterBrawlerBuffLiveTests', 'FighterArcherBuffLiveTests')
        }
        'mage'      = @{
            Paths   = @('src/Lifestone.Client/Session/Mage/', 'src/Lifestone.Game/scripts/Session/BuffLegRunner.cs', 'src/Lifestone.Game/scripts/Session/FightLegRunner.cs', 'src/Lifestone.Game/scripts/Session/DeathLegRunner.cs', 'src/Lifestone.Client/Magic/')
            Classes = @('MageBuildLoopbackTests', 'BuffLoopLiveTests', 'RebuffLiveTests', 'FightLiveTests', 'GearLiveTests', 'CastLiveTests', 'DeathLiveTests', 'HealLiveTests')
        }
        'servermod' = @{
            Paths   = @('server/mod/')
            Classes = @('ModVerbsLiveTests', 'ModCharPurgeLiveTests', 'ModXpLiveTests', 'FighterBuildLoopbackTests', 'MageBuildLoopbackTests')
        }
    }

    # A changed path here can move any class: -Loopback core then runs the whole suite and says so (the fail-safe, as StepPaths.psd1's Everything).
    Everything = @(
        'Directory.Build.props', 'global.json', 'Lifestone.slnx', 'src/Lifestone.Net/Lifestone.Net.csproj', 'src/Lifestone.Client/Lifestone.Client.csproj',
        'src/Lifestone.Client/Session/GameSession.cs', 'src/Lifestone.Client/Session/SessionBootstrap.cs', 'src/Lifestone.Client/Session/SessionOptions.cs', 'src/Lifestone.Client/Session/Hosting/',
        'src/Lifestone.Client/Session/Credentials/', 'src/Lifestone.Client/Session/Scenarios/', 'src/Lifestone.Headless/', 'tests/Shared/Loopback/', 'tools/live-gate.ps1', 'tools/lib/GateAreas.psd1'
    )
}

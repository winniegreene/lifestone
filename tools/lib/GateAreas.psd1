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
        # M6 item 0 F5 (review 0-R2): the climber's Academy and Pathwarden run is a ~10-minute live round of its own (AcademyChestLiveTests, item 0's named DoD 2 check, plan 4.1), not an actions regression test: it is selected by what the climber is made of (the quest files and
        # loader, the two stations, the creation scenario and their tests), so another item's Session/Actions change does not select it. A change anywhere under Scenarios/ still reads as Everything below (the fail-safe), as before.
        'm6-climber' = @{
            Paths   = @('src/Lifestone.Client/Session/Quests/', 'src/Lifestone.Client/Session/Scenarios/Journey/AcademyStation.cs', 'src/Lifestone.Client/Session/Scenarios/Journey/QuestStepLeg.cs', 'src/Lifestone.Client/Session/Scenarios/Journey/PathwardenStation.cs', 'src/Lifestone.Client/Session/Scenarios/ClimberCreateScenario.cs', 'tests/Lifestone.Client.Tests/Session/Journey/AcademyStationTests.cs', 'tests/Lifestone.Client.Tests/Session/Mage/FreshClimberTests.cs', 'tests/Lifestone.Client.Tests/Session/Quests/QuestProfileTests.cs', 'tests/Lifestone.Loopback.Tests/Session/FreshClimberOptionsTests.cs')
            Classes = @('AcademyChestLiveTests')
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
        # M6 item 1 c (D39, D40): the first VI learned at its band on Lifestone Throwaway (a fresh build, one budgeted teleport to Fort Tethana, the resupply's learn by the spell plan): a long lead's run of its own, selected by the scrivener
        # catalog, the learn plan and the learn step (and its own tests), so another item's change does not select it. M6 item 1 d (D12): the buy-trip line on the same Throwaway (BuyTripLiveTests), selected by the catalog's scarab sellers
        # and its own tests (its station lives under Scenarios/, the fail-safe's).
        'scriveners' = @{
            Paths   = @('src/Lifestone.Client/Session/Grounds/scriveners.json', 'src/Lifestone.Client/Session/Grounds/ScrivenerCatalog.cs', 'src/Lifestone.Client/Session/Mage/ScrivenerLearnPlan.cs', 'tests/Lifestone.Client.Tests/Session/Grounds/ScrivenerCatalogTests.cs', 'tests/Lifestone.Client.Tests/Session/Journey/ScrivenerLearnTests.cs', 'tests/Lifestone.Loopback.Tests/Session/ScrivenerLearnLiveTests.cs', 'tests/Lifestone.Client.Tests/Session/Journey/BuyTripTests.cs', 'tests/Lifestone.Loopback.Tests/Session/BuyTripLiveTests.cs')
            Classes = @('ScrivenerLearnLiveTests', 'BuyTripLiveTests')
        }
        # M6 item 4 t3b (D50, D51, DoD 5): the rung change by play on Lifestone Throwaway (the Burrow by the Refuge at 10 and home by portals; the Holding by the Facility Hub's Barracks drop at 60): a long lead's run, selected by the travel
        # file, the rung change's station and legs, the autopilot's travel rule and its own tests.
        'rung-change' = @{
            Paths   = @('src/Lifestone.Client/Session/Travel/', 'src/Lifestone.Client/Session/Scenarios/Journey/RungChangeStation.cs', 'src/Lifestone.Client/Session/Scenarios/Journey/RungRoute.cs', 'src/Lifestone.Client/Session/Scenarios/Journey/PortalHopLeg.cs', 'src/Lifestone.Client/Session/Autopilot/AutopilotTravelRule.cs', 'tests/Lifestone.Loopback.Tests/Session/RungChangeLiveTests.cs')
            Classes = @('RungChangeLiveTests')
        }
        # M6 item 5 (D54, D56, DoD 7 and 8): the quest station's loopbacks on Lifestone Throwaway (the Blood Shreth kill task at Shoushi and the Cave of Alabree; the tusk turned in to Brighteyes): long lead's runs, selected by the
        # quest station, the shared step runner, the quest files and their own tests (the Academy's AcademyChestLiveTests stays m6-climber's; QuestStepLeg selects both).
        # M6 item 16 E5b: the quest buy paid from a trade note (QuestBuyFromNotesLiveTests, the Throwaway at Holtburg's archmage), selected by the vendor counter and its own test.
        'm6-quests' = @{
            Paths   = @('src/Lifestone.Client/Session/Quests/quests/', 'src/Lifestone.Client/Session/Scenarios/Journey/QuestStation.cs', 'src/Lifestone.Client/Session/Scenarios/Journey/QuestStepLeg.cs', 'tests/Lifestone.Client.Tests/Session/Journey/QuestStationTests.cs', 'tests/Lifestone.Client.Tests/Session/Journey/KillTaskTests.cs', 'tests/Lifestone.Loopback.Tests/Session/KillTaskLiveTests.cs', 'src/Lifestone.Client/Session/Autopilot/AutopilotQuestRule.cs', 'tests/Lifestone.Client.Tests/Session/Autopilot/TrophyQuestTests.cs', 'tests/Lifestone.Loopback.Tests/Session/TrophyLiveTests.cs', 'src/Lifestone.Client/Session/Quartermaster/VendorCounter.cs', 'tests/Lifestone.Loopback.Tests/Session/QuestBuyFromNotesLiveTests.cs', 'src/Lifestone.Client/Session/Grounds/profiles/cave-of-alabree.json', 'src/Lifestone.Client/Session/Scenarios/FightScenario.cs', 'src/Lifestone.Client/Session/Scenarios/FightOptions.cs', 'src/Lifestone.Client/Session/Quests/QuestReads.cs')
            Classes = @('KillTaskLiveTests', 'TrophyLiveTests', 'QuestBuyFromNotesLiveTests')
        }
        # M6 item 6 slice H (DoD 13): the scripted death at a low rung on Lifestone Throwaway (the outskirts at 6, the Burrow at 12, report-only): selected by the death handler, its scenario and the journey's death station.
        'm6-scripted-death' = @{
            Paths   = @('src/Lifestone.Client/Session/Mage/DeathHandler.cs', 'src/Lifestone.Client/Session/Scenarios/DeathScenario.cs', 'src/Lifestone.Client/Session/Scenarios/DeathLegVerdict.cs', 'tests/Lifestone.Loopback.Tests/Session/LowRungDeathLiveTests.cs')
            Classes = @('LowRungDeathLiveTests')
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

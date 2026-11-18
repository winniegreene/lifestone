using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 16 E3.1 (docs/automation-engine.md E3, <c>TODO(engine-guard-over-subfight)</c>): what an outer loop reads of a fight a station steps inside it (a quest's kill or hunt-until step) before it lets the fight keep her
/// alive by its own deciders: whether its emergency is on, and the ground it stands on (the retreat line is that ground's, not the rung's). Reads only.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>True when the fight's own emergency heals her under its line (<c>--fight-emergency-floor</c> above 0, and not the arena's stand-in fight, where the emergency never runs).</summary>
    public bool EmergencyOn => _emergency.Enabled && !_options.ArenaHere;

    /// <summary>The ground this fight runs on (its targets, its cells, its retreat line).</summary>
    public HuntingGround Ground => _ground;
}

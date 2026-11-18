namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The world walk's options (docs/world-walk.md; parsed by src/Lifestone.WorldWalk/WorldWalkOptions.cs). Its first argument is the verb: census, pilot, graph, walks, oracle, detour, rooms, long, places, moves, map or compare.
    /// </summary>
    public static OptionGroup WorldWalk() => new("World walk", "dotnet Lifestone.WorldWalk.dll census|pilot|graph|walks|oracle|detour|rooms|long|places|moves|map|compare",
    [
        new("--out", "DIR", "The run's folder. Required. A run resumes from what the folder already holds, so a crash or restart loses only the landblocks in progress. Its contents derive from the dats: keep it under cache/."),
        new("--threads", "N", "Walking threads, 1 to 4 (default 4). Four is the owner's Light mode, and there is no heavier one."),
        new("--pick-seed", "N", "The seed that picks the pilot's sample (default 1). The same seed picks the same landblocks."),
        new("--sample", "N", "How many landblocks the pilot takes, spread over every kind (default 100)."),
        new("--landblocks", "XXYY,XXYY", "Instead of a sample, exactly these landblocks (hexadecimal)."),
        new("--other", "DIR", "For compare: the second run's folder. Compares the two runs' verdicts, leaving out timing. For detour: the walks run to read."),
        new("--census", "DIR", "For map, rooms and moves: the census run's folder."),
        new("--walks", "DIR", "For map: the walks run's folder, for the Walks view (optional)."),
        new("--oracle", "DIR", "For map: the oracle run's folder, for the Physics view (optional)."),
        new("--graph", "DIR", "For walks, oracle, long and moves: the graph run's folder (required). For map: the same (optional)."),
        new("--routes", "FILE", "For long and walks: the world routes file a graph run wrote (world-routes.bin). Without it a walk knows only the tiles attached round it."),
        new("--trace", "FILE", "Writes every walk's full log to this file. For one landblock on one thread: it is large and slow."),
        new("--world-sql", "ZIP", "For places: the pinned world data's zip (required). It is read directly; nothing goes near a database."),
        new("--ignore-climb", "on|off", "on: keep running while tools/climb.ps1 runs (default off: pause for it). Live gates, smoke, soak, slice-check gates and loopback tests always pause the walk."),
        new("--predictive", "on|off", "For rooms: steer the dungeon legs predictively (NavRunnerOptions.PredictiveSteering; default off: the ordinary steering, the world walk's measure)."),
        new("--trace-leg", "FROM-TO", "For rooms: trace this room leg step by step (the cells' low words in hex, as the results name them) to leg-<landblock>-FROM-TO.txt in the run's folder, walked where the run walks it. One landblock, one thread."),
    ], OptionScenes.WorldWalk);
}

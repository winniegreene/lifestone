using System.Globalization;
using Lifestone.Data.Terrain;

namespace Lifestone.WorldWalk;

/// <summary>The world walk's command line (registered in OptionCatalog.WorldWalk.cs).</summary>
internal sealed record WorldWalkOptions
{
    /// <summary>The most walking threads there are: the owner's Light mode.</summary>
    public const int MaxThreads = 4;

    public string Verb { get; private init; } = string.Empty;

    public string Out { get; private init; } = string.Empty;

    public string? Other { get; private init; }

    public string? CensusRun { get; private init; }

    public string? GraphRun { get; private init; }

    public string? WalksRun { get; private init; }

    public string? OracleRun { get; private init; }

    public int Threads { get; private init; } = MaxThreads;

    public int Seed { get; private init; } = 1;

    public int Sample { get; private init; } = 100;

    public IReadOnlyList<LandblockCoord>? Landblocks { get; private init; }

    public string? DatDir { get; private init; }

    public string? WorldSql { get; private init; }

    public string? Trace { get; private init; }

    public string? Routes { get; private init; }

    public bool IgnoreClimb { get; private init; }

    public bool PredictiveSteering { get; private init; }

    public string? TraceLeg { get; private init; }

    /// <summary>Parses the arguments.</summary>
    /// <returns>The options; a bad argument throws <see cref="ArgumentException"/> with its name.</returns>
    public static WorldWalkOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is not ("census" or "pilot" or "graph" or "walks" or "oracle" or "detour" or "rooms" or "long" or "places" or "moves" or "map" or "compare"))
        {
            throw new ArgumentException("the first argument is the verb: census, pilot, graph, walks, oracle, detour, rooms, long, places, moves, map or compare");
        }

        var o = new WorldWalkOptions { Verb = args[0] };
        foreach (var arg in args.Skip(1))
        {
            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            var name = eq < 0 ? arg : arg[..eq];
            var value = eq < 0 ? string.Empty : arg[(eq + 1)..];
            switch (name)
            {
                case "--out":
                    o = o with { Out = value };
                    break;
                case "--other":
                    o = o with { Other = value };
                    break;
                case "--census":
                    o = o with { CensusRun = value };
                    break;
                case "--graph":
                    o = o with { GraphRun = value };
                    break;
                case "--walks":
                    o = o with { WalksRun = value };
                    break;
                case "--oracle":
                    o = o with { OracleRun = value };
                    break;
                case "--threads":
                    var threads = int.Parse(value, CultureInfo.InvariantCulture);
                    if (threads is < 1 or > MaxThreads)
                    {
                        throw new ArgumentException($"--threads is 1 to {MaxThreads}: the owner's Light mode has no heavier setting");
                    }

                    o = o with { Threads = threads };
                    break;
                case "--pick-seed":
                    o = o with { Seed = int.Parse(value, CultureInfo.InvariantCulture) };
                    break;
                case "--sample":
                    o = o with { Sample = int.Parse(value, CultureInfo.InvariantCulture) };
                    break;
                case "--landblocks":
                    o = o with { Landblocks = value.Split(',').Select(ParseLandblock).ToArray() };
                    break;
                case "--routes":
                    o = o with { Routes = value };
                    break;
                case "--trace":
                    o = o with { Trace = value };
                    break;
                case "--world-sql":
                    o = o with { WorldSql = value };
                    break;
                case "--dat-dir":
                    o = o with { DatDir = value };
                    break;
                case "--ignore-climb":
                    o = o with { IgnoreClimb = value switch { "on" => true, "off" => false, _ => throw new ArgumentException("--ignore-climb is on or off") } };
                    break;
                case "--predictive":
                    o = o with { PredictiveSteering = value switch { "on" => true, "off" => false, _ => throw new ArgumentException("--predictive is on or off") } };
                    break;
                case "--trace-leg":
                    o = o with { TraceLeg = value.Contains('-', StringComparison.Ordinal) ? value.ToUpperInvariant() : throw new ArgumentException("--trace-leg is FROM-TO, the cells' low words in hex (0109-0105)") };
                    break;
                default:
                    throw new ArgumentException($"unknown option {name}");
            }
        }

        return o.Out.Length == 0 ? throw new ArgumentException("--out=DIR is required") : o;
    }

    private static LandblockCoord ParseLandblock(string text)
    {
        var id = ushort.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new LandblockCoord((byte)(id >> 8), (byte)id);
    }
}

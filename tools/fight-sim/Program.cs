// The fight simulator (docs/fight-sim.md): ACE's own combat code, run offline in memory on real server objects.
//
// What it does: boots just enough of ACE (95ef426d, the build in server\runtime\ace\ of the main checkout) to build a creature from its world-database weenie (WorldObjectFactory.CreateNewWorldObject, as a generator does) and
// the character from her saved shard rows (the constructor a login uses), then calls the server's own functions on them: SpellProjectile.CalculateDamage for a spell's strike (the resist roll included),
// DamageEvent.CalculateDamage for a physical attack, the monster loop's own choices (GetNextAttackType, GetCombatManeuver) and the dats' animation lengths for the timing. A number it prints is therefore the server's, not a
// re-telling of a formula. Where the simulator has to re-express a sequence (the order of a monster's attacks, a kill's casts), the code cites the ACE file it follows.
//
// What it never does: write to any database (the world and shard are read through ACE's own reads; no SaveChanges, no PropertyManager.Initialize, no GuidManager.Initialize: AceOffline.cs), open a socket or a listener,
// enter an object into a world or a landblock, or save an object. The character's session is muted (AceOffline.MutedSession). The dats are opened read-only, so it runs beside the live server.
//
// How to run it: pwsh -File tools\fight-sim.ps1 <command> ... (the script builds it when stale and sets the CLAUDE.md dotnet variables). Commands: Usage below.

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Lifestone.FightSim;

internal static class Program
{
    private const string Usage = """
        fight-sim: ACE's own combat code, offline. Every number is the server's.

          her      [--her NAME]                          her sheet as last saved: skills, vitals, enchantments, what she wields, her attack spells
          cast     SPELL|ID [--her NAME]                 one of her spells: its cast as the server times it, piece by piece, and its chance to succeed
          creature NAME|WCID                             a creature as the server builds it: health, defences, resistances, attacks, spells, experience
          matchup  NAME|WCID [--her NAME] [--casts N] [--seconds S] [--cast-overhead S] [--at-once N] [--healed-fights N]
                                                         her spells on it, its attacks on her, the race between them, the fight with her heals
          matchup  --pack "WCID x N,WCID x N,..." [--her NAME] [--healed-fights N]
                                                         a mixed pack on her at once, with her heals and with bolts only
          rank     [--her NAME] [--min-level A] [--max-level B] [--top N] [--casts N] [--seconds S] [--cast-overhead S] [--out FILE]
                                                         every creature the world database places, by experience for her fighting time
          bestiary [--check] [--columns] [--weenie A,B]  the tracked creature tables, written again from the world database under cache/fight-sim/bestiary/

          --her NAME          the character (default: the character that logged in last)
          --casts N           sampled casts of each of her spells (default 20000)
          --seconds S         simulated seconds of the creature attacking her, split into separate fights (default 6000)
          --fight-seconds S   how long each of those fights runs; each starts with the creature fresh, its mana full (default 60)
          --with "A;B"        matchup, rank: also try these spells as if she knew them (named exactly, separated by semicolons): what a scroll would buy; a bolt joins the bolt table, and a Stamina to Mana Self, Drain Health Other, Heal Self or Drain Stamina Other level joins the fight with her heals (marked *)
          --wield NAME|0xID   her, cast, matchup, rank: as if she held this caster of her packs in place of the one in her hands (in memory only; its spells cast by ACE, the old one's taken off)
          --bars "health=H,stamina=S,mana=M"
                              her, cast, matchup, rank: her maximum vitals set to these (any of the three), by her vital ranks in memory, so ACE itself reads the new maximum: a saved character stood in for a climber whose bars a live log gives (her skills and attributes stay hers)
          --explain           show ACE's working for the creature's attacks, and their spread by body part
          --cast-overhead S   seconds the client's fight loop adds to each cast, measured from a live log (default 0: the server's own minimum)
          --at-once N         matchup: a row of the fight with her heals for N on her at once, beside one to six (a ground's pack size)
          --healed-fights N   matchup and rank: fights of each pack with her heals (default: matchup 2000, rank 300)
          --trace             matchup: every cast, hit taken and vital of the first fight with her heals of each pack
          --pack "A x N,..."  matchup: the creatures on her at once, each a weenie class id (or an exact name) and how many, e.g. "11x3,22510x2,236x1"
          --check             bestiary: compare each written table with the tracked one: identical, or the weenie classes and fields that differ (exit code 1)
          --columns           bestiary: list every table's columns with their source and default, and write nothing
          --weenie A,B        bestiary: for each weenie class named, its weenie type, whether each table has a row for it and what the columns read of it; writes nothing
        """;

    private static string _aceDir = string.Empty;

    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var options = Options.Parse(args);
            _aceDir = options.AceDir ?? (Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "LifestoneAceDir")?.Value ?? string.Empty);
            if (!File.Exists(Path.Combine(_aceDir, "ACE.Server.dll")))
            {
                Console.Error.WriteLine($"no ACE.Server.dll in '{_aceDir}': pass --ace <folder of the ACE build>");
                return 2;
            }

            // ACE and its dependencies (EF Core, MySqlConnector, log4net, ...) load from the ACE folder, never copied here.
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var path = Path.Combine(_aceDir, name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            };
            return Run(options);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    // Kept out of Main so that no ACE type is touched before the resolver is in place.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(Options options)
    {
        AceOffline.Boot(_aceDir);
        return options.Command switch
        {
            "her" => Commands.Her(options),
            "creature" => Commands.Creature(options),
            "cast" => Commands.Cast(options),
            "matchup" => options.Pack is null ? Commands.Matchup(options) : Commands.MatchupPack(options),
            "rank" => Commands.Rank(options),
            "bestiary" => BestiaryWorld.Run(options),
            _ => throw new ArgumentException($"unknown command '{options.Command}'\n{Usage}"),
        };
    }
}

/// <summary>The command line.</summary>
internal sealed record Options
{
    public string Command { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public string? Her { get; init; }
    public int Casts { get; init; } = 20000;
    public double Seconds { get; init; } = 6000;
    public double FightSeconds { get; init; } = 60;

    /// <summary>How many separate fights the creature's attacks are sampled over.</summary>
    public int Fights => Math.Max(1, (int)Math.Round(Seconds / FightSeconds));
    public double CastOverhead { get; init; }
    public int MinLevel { get; init; }
    public int MaxLevel { get; init; } = int.MaxValue;
    public int Top { get; init; } = 40;
    public string? Out { get; init; }
    public string? AceDir { get; init; }
    public bool Explain { get; init; }

    /// <summary>bestiary: compare the written tables with the tracked ones.</summary>
    public bool Check { get; init; }

    /// <summary>bestiary: list the columns and write nothing.</summary>
    public bool Columns { get; init; }

    /// <summary>bestiary: the weenie classes to describe, separated by commas; writes nothing.</summary>
    public string? Weenie { get; init; }

    /// <summary>Spells to try that need not be in her book, by name, separated by semicolons.</summary>
    public string? With { get; init; }

    /// <summary>M6 16 E5e3: a caster she carries, by name or object id (0x...), to put in her hands before the command runs (<see cref="Wield.Swap"/>).</summary>
    public string? Wield { get; init; }

    /// <summary>Her maximum vitals as a stand-in, "health=H,stamina=S,mana=M" (any of the three), set by her vital ranks in memory (<see cref="World.SetBars"/>).</summary>
    public string? Bars { get; init; }

    /// <summary>matchup: a pack size to add to the fight with her heals, beside one to six (0: none).</summary>
    public int AtOnce { get; init; }

    /// <summary>matchup and rank: the fights of each pack with her heals (null: the command's default).</summary>
    public int? HealedFights { get; init; }

    /// <summary>matchup: every step of the first fight with her heals of each pack, printed.</summary>
    public bool Trace { get; init; }

    /// <summary>matchup: a mixed pack, "WCID x N" entries separated by commas (<see cref="Commands.ParsePack"/>).</summary>
    public string? Pack { get; init; }

    public static Options Parse(string[] args)
    {
        var o = new Options { Command = args[0] };
        for (var i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            int Int() => int.Parse(Next(), CultureInfo.InvariantCulture);
            double Double() => double.Parse(Next(), CultureInfo.InvariantCulture);
            o = args[i] switch
            {
                "--her" => o with { Her = Next() },
                "--casts" => o with { Casts = Int() },
                "--seconds" => o with { Seconds = Double() },
                "--fight-seconds" => o with { FightSeconds = Double() },
                "--cast-overhead" => o with { CastOverhead = Double() },
                "--min-level" => o with { MinLevel = Int() },
                "--max-level" => o with { MaxLevel = Int() },
                "--top" => o with { Top = Int() },
                "--out" => o with { Out = Path.GetFullPath(Next()) },
                "--ace" => o with { AceDir = Path.GetFullPath(Next()) },
                "--explain" => o with { Explain = true },
                "--check" => o with { Check = true },
                "--columns" => o with { Columns = true },
                "--weenie" => o with { Weenie = Next() },
                "--with" => o with { With = Next() },
                "--wield" => o with { Wield = Next() },
                "--bars" => o with { Bars = Next() },
                "--at-once" => o with { AtOnce = Int() },
                "--healed-fights" => o with { HealedFights = Int() },
                "--pack" => o with { Pack = Next() },
                "--trace" => o with { Trace = true },
                var a when a.StartsWith("--", StringComparison.Ordinal) => throw new ArgumentException($"unknown option {a}"),
                var a => o.Subject is null ? o with { Subject = a } : o with { Subject = o.Subject + " " + a },
            };
        }

        return o;
    }
}

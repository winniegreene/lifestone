using System.Globalization;
using System.Text.Json;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Autopilot.Steer;

/// <summary>
/// The last 15 minutes of game stats, sampled on the game thread and held in memory: the <c>recent</c> section of <c>sheet</c>. The changes and rates are worked out here, so a reader never does the arithmetic. A tally that falls back (a rung move starts the scenario's
/// tallies again) counts only its rises.
/// </summary>
public sealed class SteerHistory
{
    /// <summary>Seconds between two samples.</summary>
    public const double SampleSeconds = 10;

    /// <summary>The seconds of history held.</summary>
    public const double WindowSeconds = 900;

    /// <summary>The fewest seconds a rate is worked out over; less than this and it is null.</summary>
    public const double MinRateSeconds = 60;

    private const uint HealthMax = 1;
    private const uint StaminaMax = 3;
    private const uint ManaMax = 5;

    private readonly Queue<Sample> _samples = new();
    private double _last = double.NegativeInfinity;

    private readonly record struct Sample(double Now, DateTimeOffset Utc, int Level, long Xp, long Unassigned, int? Health, int? Stamina, int? Mana, long Pyreals, int Kills, int Deaths, int Retreats, int Trips, int Fizzles, long SpentXp, int Cycle, string Mode, string Stage, string? Ground);

    /// <summary>One frame: takes a sample when one is due.</summary>
    /// <param name="session">The session.</param>
    /// <param name="logic">The dial.</param>
    /// <param name="utc">The time.</param>
    public void Tick(IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        var now = session.Clock.Now;
        var q = session.State.Self.Qualities;
        if (now - _last < SampleSeconds || !q.IsKnown)
        {
            return;
        }

        _last = now;
        var scenario = logic.Scenario;
        var (kills, deaths, retreats, trips, fizzles, spent) = scenario is null
            ? (0, 0, 0, 0, 0, 0L)
            : (scenario.Totals.Kills + scenario.CycleTally.Kills, scenario.Totals.Deaths + scenario.CycleTally.Deaths, scenario.Totals.Retreats + scenario.CycleTally.Retreats, scenario.Totals.Trips + scenario.CycleTally.Trips, scenario.Totals.Fizzles + scenario.CycleTally.Fizzles, scenario.Totals.SpentXp + scenario.CycleTally.SpentXp);
        _samples.Enqueue(new Sample(now, utc, AutopilotHostLogic.LevelOf(session), q.TotalExperience, q.AvailableExperience, Vital(q, HealthMax), Vital(q, StaminaMax), Vital(q, ManaMax), q.CoinValue, kills, deaths, retreats, trips, fizzles, spent, scenario?.Cycle ?? 0, logic.Mode.ToString().ToLowerInvariant(), scenario?.Stage ?? string.Empty, scenario?.Ground.Key));
        while (_samples.Count > 0 && now - _samples.Peek().Now > WindowSeconds)
        {
            _samples.Dequeue();
        }
    }

    private static int? Vital(PlayerQualities q, uint maxId) => q.TryGetVitalLevels(maxId, out var current, out _) ? (int)current : null;

    /// <summary>The sheet's text.</summary>
    /// <param name="utc">The time.</param>
    public string Json(DateTimeOffset utc)
    {
        var held = _samples.ToArray();
        var covered = held.Length > 1 ? held[^1].Now - held[0].Now : 0;
        return SteerJson.Text(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("v", SteerOrder.Version);
            w.WriteString("section", SteerPicture.RecentSection);
            w.WriteString("at_utc", SteerJson.Utc(utc));
            w.WriteNumber("window_s", WindowSeconds);
            w.WriteNumber("sample_s", SampleSeconds);
            w.WriteNumber("covered_s", Math.Round(covered, 1));
            w.WriteStartObject("change");
            if (held.Length > 0)
            {
                w.WriteNumber("levels", held[^1].Level - held[0].Level);
                w.WriteNumber("pyreals", held[^1].Pyreals - held[0].Pyreals);
                WriteChange(w, "xp", Rises(held, s => s.Xp), covered);
                WriteChange(w, "kills", Rises(held, s => s.Kills), covered);
                WriteChange(w, "deaths", Rises(held, s => s.Deaths), covered);
                WriteChange(w, "retreats", Rises(held, s => s.Retreats), covered);
                WriteChange(w, "trips", Rises(held, s => s.Trips), covered);
                WriteChange(w, "fizzles", Rises(held, s => s.Fizzles), covered);
                WriteChange(w, "spent_xp", Rises(held, s => s.SpentXp), covered);
            }

            w.WriteEndObject();
            w.WriteStartArray("samples");
            foreach (var s in held)
            {
                w.WriteStartObject();
                w.WriteString("at", SteerJson.Utc(s.Utc));
                w.WriteNumber("level", s.Level);
                w.WriteNumber("xp", s.Xp);
                w.WriteNumber("unassigned", s.Unassigned);
                WriteNullable(w, "health", s.Health);
                WriteNullable(w, "stamina", s.Stamina);
                WriteNullable(w, "mana", s.Mana);
                w.WriteNumber("pyreals", s.Pyreals);
                w.WriteNumber("kills", s.Kills);
                w.WriteNumber("deaths", s.Deaths);
                w.WriteNumber("retreats", s.Retreats);
                w.WriteNumber("trips", s.Trips);
                w.WriteNumber("cycle", s.Cycle);
                w.WriteString("mode", s.Mode);
                w.WriteString("stage", s.Stage);
                w.WriteString("ground", s.Ground);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    private static long Rises(Sample[] held, Func<Sample, long> read)
    {
        long total = 0;
        for (var i = 1; i < held.Length; i++)
        {
            total += Math.Max(0, read(held[i]) - read(held[i - 1]));
        }

        return total;
    }

    private static void WriteChange(Utf8JsonWriter w, string name, long total, double covered)
    {
        w.WriteNumber(name, total);
        var rate = name + "_per_hour";
        if (covered >= MinRateSeconds)
        {
            w.WriteNumber(rate, Math.Round(total * 3600 / covered, 1));
        }
        else
        {
            w.WriteNull(rate);
        }
    }

    private static void WriteNullable(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteNull(name);
        }
    }
}

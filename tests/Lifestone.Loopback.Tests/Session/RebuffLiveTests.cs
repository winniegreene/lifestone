using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 4.1 lane c (DoD 14), the re-buff driver against the bundled ACE. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD. On <c>BSD Mage</c> only (the test account; it must exist: run the mage build first) the headless runner enters
/// the mage, has the gear on, enters the magic combat mode and runs the buff cycle, then runs <b>no forced pass</b>: <see cref="BuffScenario"/> with <c>--buff-auto on</c> sets the driver's margin from the shortest line (its remaining
/// time less 30 s) and ticks the <see cref="RebuffDriver"/> until its own pass ends, then returns to non-combat. Time limit 3,000 s. Since M4 P4 it runs over the mage's shared session (one login per test process; the mage is put in peace mode first), and the run does not log off.
/// </summary>
/// <remarks>
/// <para><b>What it proves</b> (the real expiry is 1,800 s, so the margin is what brings the pass inside the test, as the plan's DoD 14 says).
/// (a) <c>gear=wielded</c>, <c>buffs=24/24</c> and <c>misses=none</c> after the driver's pass: the pass left every line active.
/// (b) <c>rebuffed=yes forced=no</c> and <c>driver=on</c>: the pass recast at least one line while it was still active (an enchantment's refresh, GameEvent 0x02C2 again for a line already on the player), it was the driver's
/// (<see cref="BuffScenario.ForcedResult"/> is null) and not the forced pass.
/// (c) The driver's pass came within about the 30 s lead of the cycle's end (<c>rebuff_s</c> from the lead less the check interval up to a few minutes: the pass itself takes casts' time) and cast at least one line
/// (<c>rebuff_lines</c>).
/// (d) The chat notices: "[Lifestone] re-buffing &lt;lines&gt;" then "[Lifestone] re-buffed &lt;n&gt; lines", in that order, and <c>n</c> is the pass's cast count.
/// </para>
/// <para>It prints <c>rebuff_live &lt;the verdict fields&gt;</c> and the notices for the lead. The casts burn components and mana. Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[LiveSharedSession]
public sealed class RebuffLiveTests(ITestOutputHelper output)
{
    /// <summary>The headless limit of the run, in seconds (a cycle is about 1,300 s at the live rate, and the driver's pass follows within about 30 s of session time plus its casts).</summary>
    private const double QuitAfterSeconds = 3000;

    [LoopbackFact]
    public void The_driver_makes_the_rebuff_pass_from_the_margin_of_the_shortest_line_and_no_forced_pass_runs()
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var log = new RecordingLogSink();
        var notices = new List<string>();
        var scenario = new BuffScenario(spells, LiveMage.KitAccount(), new BuffOptions { Auto = true }, log, endSession: false) { Notice = notices.Add, RequireCharacter = true };
        var result = LiveMage.Run(output, scenario, "rebuff", QuitAfterSeconds).Result;

        var verdict = scenario.VerdictFields();
        output.WriteLine("rebuff_live " + verdict);
        foreach (var line in log.Lines.Where(l => l.Contains("[buff]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        foreach (var notice in notices)
        {
            output.WriteLine("rebuff_live_notice " + notice);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);

        // (a) the pass left every line active and nothing missed.
        Assert.StartsWith("gear=wielded ", verdict, StringComparison.Ordinal);
        Assert.Contains(" buffs=24/24 ", verdict, StringComparison.Ordinal);
        Assert.Contains(" misses=none ", verdict + " ", StringComparison.Ordinal);

        // (b) the driver's pass, not the forced one.
        Assert.Contains(" rebuffed=yes forced=no ", verdict, StringComparison.Ordinal);
        Assert.Null(scenario.ForcedResult);
        Assert.True(scenario.DriverOn);
        var pass = scenario.DriverResult!;
        Assert.False(pass.Forced);
        Assert.True(pass.Rebuffed, "the driver's pass recast no line while it was still active");
        var tail = Regex.Match(verdict, @" driver=on rebuff_lines=(?<lines>\d+) rebuff_s=(?<seconds>\d+)$");
        Assert.True(tail.Success, verdict);

        // (c) within about the lead of the cycle's end, with at least one line cast.
        Assert.True(int.Parse(tail.Groups["lines"].Value, CultureInfo.InvariantCulture) >= 1);
        var seconds = int.Parse(tail.Groups["seconds"].Value, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, (int)BuffScenario.DriverLeadSeconds - 6, 600);

        // (d) the notices, in order, with the pass's cast count.
        Assert.Equal(2, notices.Count);
        Assert.StartsWith("[Lifestone] re-buffing ", notices[0], StringComparison.Ordinal);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"[Lifestone] re-buffed {pass.Cast} lines"), notices[1]);
    }
}

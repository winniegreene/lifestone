using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Headless;
using Lifestone.Client.Tests;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M5 item 4 h1: the autopilot's short live run, run by the LEAD ONLY (the backend up and the Lifestone server mod ready; <c>dotnet test tests/Lifestone.Loopback.Tests --filter "FullyQualifiedName~AutopilotLiveTests"</c>). A slice only compiles it.
/// <para><b>What it proves.</b> <c>A_short_autopilot_runs_a_forced_trip_and_ends_ok</c> runs the headless host (<c>Program.Run</c>, <c>--scenario=autopilot</c>) on BSD Mage on the test account with <c>--autopilot-minutes</c> small
/// (<see cref="Minutes"/>) and the forced trip after 2 kills (<c>--autopilot-trip-after-kills=2</c>, D52's live half): the autopilot buffs, goes to the Black Spawn Den, fights open-ended, is asked to stop after 2 kills, runs the logistics trip (recall home, the
/// vendors, the experience, the recall back), and stops by its minutes rule. It asserts the process exit code 0, the <c>autopilot done</c> line with <c>ok=yes</c>, <c>cycles&gt;=1</c> (a trip ended a cycle) and <c>stop=minutes</c>, and one
/// <c>autopilot cycle</c> line with <c>state=trip</c>. The numbers it prints (kills, sold, bought, spent_xp, seconds) are reported, not judged. It changes the character: kills, experience, a trip's spend (BSD Mage on test is levelled by its loops, never reset).</para>
/// <para>Solo: it logs in on its own session, so it takes the <c>test</c> account's lane alone (<c>Lane=solo</c>); never part of the account lanes' shared session. It carries no <c>Tier=Soak</c> tag (M5 4 c1): R12 exempts a Soak class because the gate's soak phase runs it, and this is the lead's own run, not the overnight soak; tools/lib/GateAreas.psd1 names it in the mage area, and <c>Lane=solo</c> keeps it out of the account lanes.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[Trait("Lane", "solo")]
public sealed partial class AutopilotLiveTests(ITestOutputHelper output)
{
    /// <summary>The autopilot's duration stop rule, minutes of the session clock: long enough for the buff, the walk to the den, 2 kills, the trip and the return (the trip is the slow part), short enough to stay a test.</summary>
    private const double Minutes = 40;

    [GeneratedRegex(@"^autopilot done ok=(?<ok>yes|no) cycles=(?<cycles>\d+) stop=(?<stop>\S+) (?<rest>.*)$", RegexOptions.Multiline)]
    private static partial Regex DoneLine();

    [GeneratedRegex(@"^autopilot cycle n=\d+ state=(?<state>\S+) ", RegexOptions.Multiline)]
    private static partial Regex CycleLine();

    [LoopbackFact]
    public void A_short_autopilot_runs_a_forced_trip_and_ends_ok()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var environment = new HeadlessEnvironment(null, [RepositoryFiles.MainCheckout], TimeProvider.System);

        var code = Program.Run(
            [
                "--scenario=autopilot",
                string.Create(CultureInfo.InvariantCulture, $"--autopilot-minutes={Minutes}"),
                "--autopilot-trip-after-kills=2",
            ],
            stdout,
            stderr,
            environment);

        var text = stdout.ToString();
        output.WriteLine(text);
        var errors = stderr.ToString();
        output.WriteLine(errors.Length > 4000 ? "(stderr, last 4000 characters) " + errors[^4000..] : errors);

        var done = DoneLine().Match(text);
        Assert.True(done.Success, "the run printed no 'autopilot done' line");
        Assert.True(done.Groups["ok"].Value == "yes", "the autopilot's done line says ok=no: " + done.Value);
        var cycles = int.Parse(done.Groups["cycles"].Value, CultureInfo.InvariantCulture);
        Assert.True(cycles >= 1, "no cycle ended: the forced trip after 2 kills never ran (" + done.Value + ")");
        Assert.Equal("minutes", done.Groups["stop"].Value);
        Assert.Contains(CycleLine().Matches(text), m => m.Groups["state"].Value == "trip");
        Assert.Equal(0, code);
    }
}

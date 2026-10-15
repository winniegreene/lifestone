using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Client.Tests;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// DoD 8 against the bundled ACE (step ST16): the headless host logs in as <c>Lifestone Loopback</c>, is sent to a ring
/// around Holtburg's arrival point, walks three laps with its own run skill on the physics port, and ACE must not correct
/// it once (force-position corrections 0) while its where-am-I answers stay within 0.5 m of the client's. The scenario then
/// sends the character back to where the login found it (the Academy start), which the test checks. Skipped, with the
/// probe's reason, unless the backend runs with the Lifestone server mod ready.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class HoltburgLapsLoopbackTests(ITestOutputHelper output)
{
    /// <summary>The character the loopback runs use, created on the test account when missing.</summary>
    private const string Character = "Lifestone Loopback";

    private static readonly Regex Verdict = new(
        @"^movement route=holtburg-laps laps=3 corrections=0 where_max_m=\d+\.\d\d result=PASS$",
        RegexOptions.CultureInvariant);

    [LoopbackFact]
    public void Three_laps_around_Holtburg_are_accepted_with_no_corrections_and_the_character_is_put_back()
    {
        using var dats = DatSet.Open();

        // The laps' home is wherever the login finds the character, so it starts at its own start and is put back there whatever the run did.
        LivePlacement.PutTheCharacterAtTheAcademyStart(dats, output);
        try
        {
            ThreeLaps(dats);
        }
        finally
        {
            LivePlacement.PutTheCharacterAtTheAcademyStart(dats, output);
        }
    }

    /// <summary>
    /// The jump leg against the bundled ACE (m2 wave 3, dod-8-9-2): the first live run of <c>WriteJump</c>'s 56-byte Jump pack. After the three laps the character does a standing jump and a running
    /// jump (the keys a person presses), where-am-I is asked, and ACE must not have corrected the character once nor disagree with the client by more than 0.5 m; the Jump action must have gone out
    /// twice. Same character, same put-back as the laps test.
    /// </summary>
    [LoopbackFact]
    public void A_standing_and_a_running_jump_after_the_laps_are_accepted_with_no_corrections()
    {
        using var dats = DatSet.Open();
        LivePlacement.PutTheCharacterAtTheAcademyStart(dats, output);
        try
        {
            ThreeLaps(dats, jumps: true);
        }
        finally
        {
            LivePlacement.PutTheCharacterAtTheAcademyStart(dats, output);
        }
    }

    private void ThreeLaps(DatSet dats, bool jumps = false)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        using var recording = new MemoryStream();
        var scenario = new HoltburgLapsScenario(dats, log, createCharacter: Character, skillClasses: HeadlessCharacter.RetailSkillClasses(dats), jumps: jumps);
        output.WriteLine($"ring: {scenario.Route.Goals.Count} points, start {scenario.Route.Start}");

        HeadlessRunResult result;
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = 300,
                },
                scenario,
                secrets,
                File.ReadAllBytes(files.PidsFile),
                dats,
                TimeProvider.System,
                log,
                recording);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }

        var context = new GameThreadSynchronizationContext();
        using (context.Install())
        using (runner)
        {
            result = runner.Run(context, CancellationToken.None);
        }

        output.WriteLine(result.SummaryLine("memory"));
        output.WriteLine(scenario.Play.SummaryLine ?? "no movement verdict line");
        output.WriteLine($"audit: {scenario.Play.Audit}; unmatched {scenario.Play.Unmatched}");
        foreach (var line in log.Lines.Where(l => l.Contains("scenario", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Matches(Verdict, scenario.Play.SummaryLine);
        var audit = scenario.Play.Audit!;
        Assert.Equal(0, audit.Corrections);
        Assert.Equal(0, result.Corrections);
        Assert.Equal(3, audit.Laps);
        Assert.InRange(audit.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.True(audit.WhereChecks >= 4, $"{audit.WhereChecks} where-am-I checks");
        Assert.Equal(audit.ForcePositionSequenceFirst, audit.ForcePositionSequenceLast);
        Assert.True(audit.MoveToStateSent >= 10, $"{audit.MoveToStateSent} MoveToState");
        Assert.True(audit.MaxAutonomousPositionIntervalSeconds <= 1.0 + 0.1, $"{audit.MaxAutonomousPositionIntervalSeconds:F3} s between positions");
        Assert.Equal(0, result.Errors);
        Assert.Equal("done", result.Stage);
        if (jumps)
        {
            Assert.NotNull(scenario.Play.Jump);
            Assert.Null(scenario.Play.Jump.Failure);
            Assert.Equal(2, scenario.Play.Jump.Jumps);
            Assert.Equal(2, scenario.Play.Jump.Landed);
            Assert.Equal(2, audit.JumpSent);
            Assert.True(audit.WhereChecks >= 5, $"{audit.WhereChecks} where-am-I checks (one more after the jumps)");
        }
    }
}

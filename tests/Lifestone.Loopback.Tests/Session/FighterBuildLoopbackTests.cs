using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 1.1's fighter build against the bundled ACE (<c>--scenario=fighter-build</c>, spec 5.10, DoD 2): the host logs in on the test account, finds "Holtburg Hunter" or creates it, and builds it to the
/// template (the mod's <c>xp</c> to level 40, every target raised, the kit granted, the Mace wielded, the leather worn, the character placed in Holtburg). The scenario runs twice in a row. The first run
/// ends <c>fighter=ready</c> (already built) or <c>fighter=built</c>, with <c>level=40</c> (or more) and <c>template=ok</c> and the kit held, the Mace wielded and the five pieces worn; the second must end
/// <c>fighter=ready</c> with nothing sent (no xp, item, raise, train, wield or place): the scenario is idempotent and a ready fighter is only verified.
/// <para><b>The advancement chat, asserted live</b> (<c>m3-advancement-chat-check</c>, ACE <c>Player_Xp.cs</c> 307 and <c>Player_Attributes.cs</c> 49, <c>Player_Skills.cs</c> 57, <c>Player_Vitals.cs</c> 60): on a run
/// that sent the <c>xp</c> step, ACE's ServerMessage of chat type 0x0D (Advancement) "You are now level N!" arrives with N at least 40; on a run that sent raises, one Advancement line "Your base ... is now N!"
/// arrives per raise; on the second run no Advancement line arrives at all. The test fails by name if ACE's wording has moved on.</para>
/// <para><b>When the assertions are live:</b> the xp and the advancement-chat assertions are live only on a build run (a run that sent <c>xp</c> or raises). A verify-only run (<c>fighter=ready</c> on the first run, as every run is once Holtburg Hunter is built) passes them vacuously, so a green verify-only run proves nothing about the advancement chat.</para>
/// Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod (with the <c>xp</c> and <c>item</c> verbs) ready.
/// </summary>
/// <remarks>
/// This is the only test allowed to build Holtburg Hunter (owner decision D1: the fighter is levelled only by its build and the loops' kills, never reset or purged). It enters that character and no other,
/// creates nothing else and deletes nothing. If the first run fails creating it, <c>TODO(m4-healing-minlevel)</c> (ACE refusing Healing trained at level 1) is the first suspect.
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
public sealed class FighterBuildLoopbackTests(ITestOutputHelper output)
{
    /// <summary>ChatMessageType.Advancement, ACE <c>ChatMessageType.cs</c> 162.</summary>
    private const uint AdvancementChatType = 0x0D;

    private static readonly Regex Built = new(
        @"^fighter=(ready|built) level=(?<level>\d+) template=ok kit=ok wielded=(?<wielded>\d+) worn=(?<worn>\d+)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex LevelLine = new(@"^You are now level (?<n>\d+)!", RegexOptions.CultureInvariant);

    private static readonly Regex RaiseLine = new(@"^Your base .+ is now \d+( and has reached its upper limit)?!$", RegexOptions.CultureInvariant);

    private sealed record Outcome(string Verdict, IReadOnlyList<string> Steps, IReadOnlyList<string> Advancement);

    [LoopbackFact]
    public void The_fighter_is_built_or_verified_and_a_second_run_changes_nothing()
    {
        using var dats = DatSet.Open();
        var first = RunOnce(dats, "first");
        var firstMatch = Built.Match(first.Verdict);
        Assert.True(firstMatch.Success, first.Verdict);
        Assert.True(int.Parse(firstMatch.Groups["level"].Value, CultureInfo.InvariantCulture) >= FighterTemplate.TargetLevel, first.Verdict);
        Assert.Equal(FighterKit.MaceWcid.ToString(CultureInfo.InvariantCulture), firstMatch.Groups["wielded"].Value);
        Assert.Equal("5", firstMatch.Groups["worn"].Value);

        // The advancement chat, as ACE sends it for what this run did.
        if (first.Steps.Any(s => s.StartsWith("xp ", StringComparison.Ordinal)))
        {
            var levels = first.Advancement.Select(l => LevelLine.Match(l)).Where(m => m.Success).ToList();
            Assert.True(levels.Count >= 1, "no 'You are now level N!' Advancement line arrived after the xp step: " + string.Join(" | ", first.Advancement));
            Assert.True(levels.Max(m => int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture)) >= FighterTemplate.TargetLevel, string.Join(" | ", first.Advancement));
        }

        var raises = first.Steps.Count(s => s.StartsWith("raise ", StringComparison.Ordinal));
        Assert.True(first.Advancement.Count(l => RaiseLine.IsMatch(l)) >= raises, $"{raises} raises sent, Advancement lines: " + string.Join(" | ", first.Advancement));

        var second = RunOnce(dats, "second");
        var secondMatch = Built.Match(second.Verdict);
        Assert.True(secondMatch.Success, second.Verdict);
        Assert.StartsWith("fighter=ready ", second.Verdict, StringComparison.Ordinal);
        Assert.Equal(firstMatch.Groups["level"].Value, secondMatch.Groups["level"].Value);
        Assert.Empty(second.Steps); // nothing sent: no xp, item, raise, train, wield or place
        Assert.Empty(second.Advancement); // and ACE announced no advancement
    }

    private Outcome RunOnce(DatSet dats, string label)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        using var recording = new MemoryStream();
        var scenario = new FighterBuildScenario(XpTableData.Load(dats), HeadlessCharacter.RetailSkillClasses(dats), log);

        HeadlessRunResult result;
        LiveCharacter.Release(output, BackendProbe.TestAccountKey);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = 600,
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

        var advancement = new List<string>();
        var context = new GameThreadSynchronizationContext();
        using (context.Install())
        using (runner)
        using (runner.Session.Events.Subscribe<ChatReceived>((in ChatReceived e) =>
        {
            if (e.Line.ChatType == AdvancementChatType)
            {
                advancement.Add(e.Line.Text);
            }
        }))
        {
            result = runner.Run(context, CancellationToken.None);
        }

        output.WriteLine($"[{label}] {result.SummaryLine("memory")}");
        output.WriteLine($"[{label}] {scenario.VerdictFields()}");
        foreach (var step in scenario.SentSteps)
        {
            output.WriteLine($"[{label}] step: {step}");
        }

        foreach (var line in advancement)
        {
            output.WriteLine($"[{label}] advancement: {line.ReplaceLineEndings(" / ")}");
        }

        foreach (var line in log.Lines.Where(l => l.Contains("fighter", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        return new Outcome(scenario.VerdictFields(), scenario.SentSteps, advancement);
    }
}

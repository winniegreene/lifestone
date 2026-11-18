using System.Security.Cryptography;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 item 0 a3a and a3b (DoD 2), against the bundled ACE: the fresh climber's first hour of the quest line by play, on the account <c>test</c>, alone (D25: Winnie never runs beside BSD Mage). The lead runs it; no slice does.
/// <para><b>What it proves</b> (the summary line <c>academy_done=yes pathwarden_done=yes trinket_worn=yes granted=0 place=0 recovery_place=0</c>, then <c>level=&lt;n&gt;</c>): (1) <see cref="ClimberCreateScenario"/> deletes the live Winnie through the retail delete, creates a new one at level 1 with the climber's
/// template (War Magic and Life Magic specialised, no Alchemy (D34), no Melee Defense; the verdict <c>template=ok</c>), purges the old copies with the server mod (<c>fresh=recreated|created purged=&lt;n&gt;</c>) and sends no grant; (2) the <c>climber</c> journey, cheat-free (<see cref="JourneyPlan.CheatFree"/>: no place
/// of any kind, the known state is the Academy's start cell or the journey stops by name), plays the Training Academy by play (<see cref="AcademyStation"/>: the Calling Stone, the three leather pieces worn, Samuel, the Training Master, a Sparring Golem killed with war spells and its token given, the
/// Central Courtyard portal, the Sentry, an Adolescent Olthoi killed and its orb given, the Exit to Holtburg on foot; <c>academy_done=yes</c>), then the <c>pathwarden</c> station in Holtburg (the token to Thorolf, the key, the chest, the robe, gauntlets, sollerets and trinket worn as ACE lets it; <c>pathwarden_done=yes trinket_worn=yes</c>);
/// (3) <c>granted</c>, <c>place</c> and <c>recovery_place</c> are the mod-verb counter's (<see cref="ModVerbCounts"/>): the creation's grants plus the journey's climber line, counted from the journey's start, all 0, never a literal; (4) the level is the character's own property (the climber line's <c>level=</c>), reported, not judged:
/// DoD 2 names 5, and the Academy's fixed awards reach 3 (1,000 and a whole level's span) or 4 (if the Sentry's 5,000 is the award), <c>TODO(m6-academy-sentry-percent)</c>; nothing is granted to make it up.
/// The <see cref="AcademyExitScenario"/> bridge (ToolPlace in and out of the exit's wing) is no longer used for Winnie.</para>
/// <para>The test changes the account <c>test</c>'s character list (Winnie is deleted, created and purged) and Winnie; it touches no other character.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[Trait("Lane", "solo")]
public sealed class AcademyChestLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void A_fresh_Winnie_plays_the_Academy_opens_the_Pathwarden_chest_and_wears_the_four_pieces_with_nothing_granted_and_no_place()
    {
        using var dats = DatSet.Open();

        // (1) the creation: the retail delete of the live copy, the new copy, the purge of the old.
        var createLog = new RecordingLogSink();
        var created = Run("create", new ClimberCreateScenario(HeadlessCharacter.RetailSkillClasses(dats), ClimberCreateScenario.PlanFromDats(dats, new Lifestone.Client.Session.Autopilot.AutopilotOptions().BuffBot), createLog), dats, createLog, 600, out var createScenario);
        var climber = (ClimberCreateScenario)createScenario;
        output.WriteLine("[climber] verdict " + climber.VerdictFields());
        Assert.True(created.Ok, created.Failure);
        Assert.True(climber.Check is { AllMet: true }, climber.Check?.Summary);
        Assert.Contains(createLog.Lines, l => l.Contains("verdict fresh=recreated", StringComparison.Ordinal) || l.Contains("verdict fresh=created", StringComparison.Ordinal));
        Assert.Equal(0, climber.Granted);

        // (2) the climber journey: the Academy by play from its start cell, then the Pathwarden; no place of any kind.
        var journeyLog = new RecordingLogSink();
        using var journey = new HeadlessJourneyScenario(JourneyPlans.Climber, [], dats, SpellTableData.Load(dats).Spells, journeyLog, null, new HeadlessJourneyRunnerHost(dats, journeyLog));
        var journeyResult = Run("journey", journey, dats, journeyLog, 2400, out _);
        var academy = journey.Lines.FirstOrDefault(l => l.Contains("journey station=academy", StringComparison.Ordinal)) ?? "no academy station line";
        var station = journey.Lines.FirstOrDefault(l => l.Contains("journey station=pathwarden", StringComparison.Ordinal)) ?? "no pathwarden station line";
        var done = journey.Lines.FirstOrDefault(l => l.Contains("journey done", StringComparison.Ordinal)) ?? "no journey done line";
        var climberLine = journey.Lines.FirstOrDefault(l => l.Contains("journey climber", StringComparison.Ordinal)) ?? "no journey climber line";
        output.WriteLine(academy);
        output.WriteLine(station);
        output.WriteLine(done);
        output.WriteLine(climberLine);
        var academyDone = academy.Contains("academy_done=yes", StringComparison.Ordinal);
        var pathwardenDone = station.Contains("pathwarden_done=yes", StringComparison.Ordinal);
        var trinketWorn = station.Contains("trinket_worn=yes", StringComparison.Ordinal);
        var granted = ReadInt(climberLine, "granted=");
        var place = ReadInt(climberLine, "place=");
        var recoveryPlace = ReadInt(climberLine, "recovery_place=");
        var summary = $"academy_done={(academyDone ? "yes" : "no")} pathwarden_done={(pathwardenDone ? "yes" : "no")} trinket_worn={(trinketWorn ? "yes" : "no")} granted={climber.Granted + granted} place={place} recovery_place={recoveryPlace}";
        output.WriteLine(summary);
        output.WriteLine($"route={ReadWord(academy, "route=")} level={ReadInt(climberLine, "level=")} (the character's own property; D37: the skip's award of 11,000 XP reaches about level 5, reported here, never assumed) academy xp={ReadWord(academy, "xp=")}");
        Assert.True(journeyResult.Ok, journeyResult.Failure ?? journey.Failure);
        Assert.Equal("academy_done=yes pathwarden_done=yes trinket_worn=yes granted=0 place=0 recovery_place=0", summary);
    }

    private static string ReadWord(string line, string key)
    {
        var at = line.IndexOf(" " + key, StringComparison.Ordinal);
        if (at < 0)
        {
            return "?";
        }

        var from = at + 1 + key.Length;
        var end = line.IndexOf(' ', from);
        return end < 0 ? line[from..] : line[from..end];
    }

    private static int ReadInt(string line, string key)
    {
        var at = line.IndexOf(" " + key, StringComparison.Ordinal); // a whole key: "place=" is not the end of "recovery_place="
        if (at < 0)
        {
            return -1;
        }

        var rest = line[(at + 1 + key.Length)..];
        var end = rest.IndexOf(' ');
        return int.TryParse(end < 0 ? rest : rest[..end], out var n) ? n : -1;
    }

    /// <summary>Runs <paramref name="scenario"/> live as the account <c>test</c> and prints its summary and EVERY line of its log sink (INFO, WARN and ERROR) under a header for <paramref name="phase"/> (F1, the log-everything rule: live round 2 saw only the WARN lines).</summary>
    private HeadlessRunResult Run(string phase, IScenario scenario, DatSet dats, RecordingLogSink log, double quitAfterSeconds, out IScenario ran)
    {
        ran = scenario;
        var files = BackendProbe.MainCheckoutFiles;
        using var recording = new MemoryStream();
        LiveCharacter.Release(output, BackendProbe.TestAccountKey);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = quitAfterSeconds,
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

        HeadlessRunResult result;
        var context = new GameThreadSynchronizationContext();
        try
        {
            using (context.Install())
            using (runner)
            {
                result = runner.Run(context, CancellationToken.None);
            }

            output.WriteLine(result.SummaryLine("memory"));
        }
        finally
        {
            // Printed even when the run throws: the log is the only evidence of a live round.
            var lines = log.Lines;
            output.WriteLine($"===== {phase}: {lines.Count} log lines ({lines.Count(l => l.StartsWith("WARN", StringComparison.Ordinal))} WARN, {lines.Count(l => l.StartsWith("ERROR", StringComparison.Ordinal))} ERROR) =====");
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }

            output.WriteLine($"===== end of {phase} =====");
        }

        return result;
    }
}

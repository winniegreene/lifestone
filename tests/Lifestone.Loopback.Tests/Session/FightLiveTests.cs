using System.Globalization;
using System.Security.Cryptography;
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
/// M3 3.1 lane fa, the fight scenario against the bundled ACE (<c>--scenario=fight</c>). WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD (it needs the backend and the BSD Mage, built and buffed: run
/// the mage build and the buff scenario first; a buff top-up is part of the run when a line has under 600 s left). On <c>BSD Mage</c> only (the test account) the headless runner enters the mage, has the
/// gear on, tops the buffs up, sends the mage to the Aluvian Black Spawn Den (the Places row), enters the magic combat mode, casts Flame Bolt at the nearest Tusker Slave of the arrival's floor until one kill
/// (walking the floor when none is in range or the casts miss), returns to non-combat and logs off. Time limit 3,600 s; the fight itself 300 s.
/// </summary>
/// <remarks>
/// <para><b>What it proves</b> (each assertion names the ACE behaviour it relies on).
/// (a) <c>kills&gt;=1 damage_dealt&gt;0</c> from <see cref="FightTally"/>: the 0x01AD KillerNotification named a kill and at least one spell-damage line (<c>SpellProjectile.cs</c> 810-845, Magic chat) preceded it (the killing
/// blow has no line, 846-851).
/// (b) The war cast's outcome is Confirmed by its projectile: <c>projectiles&gt;=1</c> (a Missile-state CreateObject during the fight, which is what confirms a war cast in the Action API) and at least one
/// cast was not logged as a miss. This is the live check of <c>TODO(m3-cast-projectile-attribution)</c> (decision D21): the log lines <c>[fight] projectile created</c> carry each projectile's create position (cell and
/// local origin), velocity and Setup id; they are printed so the attribution rule (the first Missile object that started within 5 m of the player and flies away from it along the aim) can be read off the real values.
/// (c) The run ended <c>done</c> with no error line and the player alive (a death fails the scenario).
/// </para>
/// <para>It prints <c>fight_live kills= damage_dealt= spells_cast= misses= xp_gained= damage_taken= hits_taken= level= range_m= projectiles=</c> for the lead. The casts burn components and mana and the kill
/// earns experience (the mage levels only by this and the loops' kills). The account name the kit is built for is read from the secrets file at run time and is in no output. Skipped, with the probe's reason, unless the
/// backend runs with the Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class FightLiveTests(ITestOutputHelper output)
{
    /// <summary>The headless limit of the run, in seconds (the gear, a buff top-up of up to 1,300 s, the fight's 300 s and the log off).</summary>
    private const double QuitAfterSeconds = 3600;

    [LoopbackFact]
    public void The_mage_kills_a_tusker_in_the_Black_Spawn_Den_with_Flame_Bolt_and_the_cast_is_confirmed_by_its_projectile()
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        var secrets = File.ReadAllBytes(files.SecretsFile);
        FightScenario scenario;
        HeadlessRunner runner;
        try
        {
            string account;
            using (var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(BackendProbe.TestAccountKey, AccountChoice.Default)))
            {
                account = credentials.Account;
            }

            scenario = new FightScenario(spells, account, new BuffOptions(), new FightOptions { Kills = 1, Seconds = FightOptions.DefaultSeconds }, DenWalk.Create(dats, log), log);
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = QuitAfterSeconds,
                },
                scenario,
                secrets,
                File.ReadAllBytes(files.PidsFile),
                dats,
                TimeProvider.System,
                log,
                recording: null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }

        HeadlessRunResult result;
        var context = new GameThreadSynchronizationContext();
        using (context.Install())
        using (runner)
        {
            result = runner.Run(context, CancellationToken.None);
        }

        var tally = scenario.Tally;
        output.WriteLine(result.SummaryLine("memory"));
        output.WriteLine("fight " + scenario.VerdictFields());
        var lines = log.Lines.Where(l => l.Contains("[fight]", StringComparison.Ordinal) || l.Contains("[gear]", StringComparison.Ordinal) || l.Contains("[buff]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).ToList();
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"fight_live kills={tally.Kills} damage_dealt={tally.DamageDealt} spells_cast={tally.Casts} misses={tally.Misses} xp_gained={tally.XpGained} damage_taken={tally.DamageTaken} hits_taken={tally.HitsTaken} level={scenario.Level} range_m={scenario.RangeMetres:0.#} projectiles={scenario.Projectiles}"));

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);

        // (a) A kill, and damage before it.
        Assert.True(tally.Kills >= 1, "kills " + tally.Kills);
        Assert.True(tally.DamageDealt > 0, "damage_dealt " + tally.DamageDealt + " (the killing blow has no line; the scenario fights on past a one-shot kill until a line, so 0 means the seconds limit ended it)");

        // (b) The war cast was confirmed by its projectile: TODO(m3-cast-projectile-attribution)'s live check.
        Assert.True(scenario.Projectiles >= 1, "projectiles " + scenario.Projectiles);
        var projectileLines = lines.Where(l => l.Contains("projectile created", StringComparison.Ordinal)).ToList();
        Assert.True(projectileLines.Count >= 1, "no projectile line was logged");
        output.WriteLine($"projectile check: {projectileLines.Count} projectile(s) created; each line above carries guid, setup, cell, at and velocity");
        var notConfirmed = lines.Count(l => l.Contains("miss: the cast was not confirmed", StringComparison.Ordinal));
        Assert.True(tally.Casts > notConfirmed, $"every cast was a miss without a projectile: casts {tally.Casts}, not confirmed {notConfirmed}");

        // (c) Alive, and the level is one of I-VII.
        Assert.DoesNotContain(lines, l => l.Contains("the player died", StringComparison.Ordinal));
        Assert.Contains(scenario.Level, new[] { "I", "II", "III", "IV", "V", "VI", "VII" });
    }
}

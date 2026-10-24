using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 P4's two new fighter builds against the bundled ACE (<c>--scenario=fighter-build --fighter=melee|archer</c>, plan D31): <see cref="FighterTemplate.Melee"/> ("Holtburg Brawler", account <c>test-melee</c>) and
/// <see cref="FighterTemplate.Archer"/> ("Holtburg Archer", account <c>test-archer</c>). What each fact PROVES: the character is found or created on its own account with the template's creation (Heavy Weapons or Missile
/// Weapons specialised, Melee Defense and Healing trained), taken to level 40 with every target raised (<c>template=ok</c>), given exactly the template's kit (<c>kit=ok</c>: the Brawler's has no Shortbow and no Arrows, the
/// Archer's has no Mace and holds 250 Greater Frost Arrows), has the leather set worn (<c>worn=5</c>) and the template's weapon wielded (<c>wielded=</c> the Mace for the Brawler, nothing for the Archer, whose Shortbow stays in the pack until
/// a hunt swaps), and is placed in Holtburg. Since M4 P5a C1b the kit also holds the caster (The Healer's Heart, weenie 8552) in the pack, never wielded (<c>FighterBuildScenario.Caster</c> reads <c>pack</c> after each run; a rerun is how the existing Brawler and Archer get it, granted once). A second run in a row must end <c>fighter=ready</c> with nothing sent but the spellbook check (idempotent). Since M4 P5a (D33) the build also takes the character to level 48, trains Creature Enchantment and Life Magic, raises Focus, Self and the two schools and teaches the <c>FighterProfile</c>'s spells: <c>magic=trained spells=n/n</c> proves both schools are trained and the mod answered known-or-learned for every id of the profile (a rerun of a level-40 character is how the existing Brawler and Archer get there). The advancement-chat assertions of <see cref="FighterBuildLoopbackTests"/> are not repeated.
/// <para>The lead runs these (a slice never does): backend up with the server mod, accounts <c>test-melee</c> and <c>test-archer</c> in secrets.json (key names only), and the account rule of lane acct merged (the headless runner
/// refuses any account but the three test accounts).</para>
/// </summary>
/// <remarks>
/// These are the only tests allowed to build Holtburg Brawler and Holtburg Archer (D31: levelled only by their build and the loops' kills, never reset or purged). Each enters its own character and no other.
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-melee")]
[LoopbackCharacter("Holtburg Brawler")]
public sealed class FighterBrawlerBuildLoopbackTests(ITestOutputHelper output)
{
    /// <summary>Holtburg Brawler on <c>test-melee</c>: the build, then a second run that changes nothing (a build scenario: it logs in on its own, M4 D31).</summary>
    [LoopbackFact]
    public void The_brawler_is_built_or_verified_with_its_kit_and_a_second_run_changes_nothing() => new FighterTemplatesBuildRun(output).Verify("melee");
}

/// <summary>Holtburg Archer on <c>test-archer</c>; see <see cref="FighterBrawlerBuildLoopbackTests"/>.</summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-archer")]
[LoopbackCharacter("Holtburg Archer")]
public sealed class FighterArcherBuildLoopbackTests(ITestOutputHelper output)
{
    /// <summary>Holtburg Archer on <c>test-archer</c>: the build, then a second run that changes nothing (a build scenario: it logs in on its own, M4 D31).</summary>
    [LoopbackFact]
    public void The_archer_is_built_or_verified_with_its_kit_and_a_second_run_changes_nothing() => new FighterTemplatesBuildRun(output).Verify("archer");
}

/// <summary>The body both classes run (one account each, so each holds its own account's lock).</summary>
internal sealed class FighterTemplatesBuildRun(ITestOutputHelper output)
{
    private static readonly Regex Built = new(
        @"^fighter=(ready|built) level=(?<level>\d+) template=ok kit=ok wielded=(?<wielded>\d+) worn=(?<worn>\d+) magic=(?<magic>trained):(?<schools>creature\+item\+life\+mana) spells=(?<known>\d+)/(?<ids>\d+) gear=(?<gear>\d+)/(?<gearTotal>\d+) gear_magic=(?<gmagic>on|off|n/a) spells_active=(?<active>\d+)/(?<wanted>\d+) pack=(?<pack>\d+)/102$",
        RegexOptions.CultureInvariant);

    /// <summary>ChatMessageType.Advancement, ACE <c>ChatMessageType.cs</c> 162.</summary>
    private const uint AdvancementChatType = 0x0D;

    private sealed record Outcome(string Verdict, IReadOnlyList<string> Steps, IReadOnlyList<string> Advancement);

    internal void Verify(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        using var dats = DatSet.Open();
        var first = RunOnce(dats, template, "first");
        var firstMatch = Built.Match(first.Verdict);
        Assert.True(firstMatch.Success, first.Verdict);
        Assert.True(int.Parse(firstMatch.Groups["level"].Value, CultureInfo.InvariantCulture) >= template.Level, first.Verdict); // M4 P5a (D33, D38): 55 for the Brawler and the Archer, whether built from 40 or 48 by this run or already there
        Assert.Equal(template.WieldedWeaponWcid.ToString(CultureInfo.InvariantCulture), firstMatch.Groups["wielded"].Value);
        Assert.Equal("7", firstMatch.Groups["worn"].Value); // M4 P5a C6: seven mismatched magic armour pieces
        Assert.Equal(template.Kit.Gear!.Pieces.Count.ToString(CultureInfo.InvariantCulture), firstMatch.Groups["gear"].Value); // every gear piece worn, wielded or (the Archer's bow) held
        Assert.Equal(firstMatch.Groups["gearTotal"].Value, firstMatch.Groups["gear"].Value);
        Assert.Equal("on", firstMatch.Groups["gmagic"].Value); // the worn items' spells are active on the character (report-only in its first gate; a TODO(m4-gear-mana) warning names a piece out of mana)
        Assert.Equal(firstMatch.Groups["wanted"].Value, firstMatch.Groups["active"].Value);
        Assert.True(int.Parse(firstMatch.Groups["wanted"].Value, CultureInfo.InvariantCulture) > 0, first.Verdict);
        Assert.True(int.Parse(firstMatch.Groups["pack"].Value, CultureInfo.InvariantCulture) <= 102 - FighterKit.MainPackHeadroom, first.Verdict); // the main pack keeps its headroom

        // M4 P5a (D33, D38): Creature Enchantment, Item Enchantment and Life Magic are all trained (magic=trained:creature+item+life reads the three skills' advancement class and names them) and every spell id of the profile is known, Lifestone
        // Recall (1635) among them: the mod answered known-or-learned for all of them, so the fighter can cast itself home (RecallHome).
        Assert.Equal("creature+item+life+mana", firstMatch.Groups["schools"].Value); // M4 P5a C7: Mana Conversion (skill 16) is trained and raised too, for the fighters' casts at its reduced cost
        Assert.Equal(2, template.Kit.VitalPotions.Count()); // M4 P5a C7: kit=ok above counts the 100 Mana Potions (weenie 379) and the 50 Stamina Potions (378) in the side packs; the rerun below grants nothing
        Assert.Contains(RecallHome.LifestoneRecallSpell, FighterProfile.For(template)!.SpellsToLearn);
        var profileIds = FighterProfile.For(template)!.SpellsToLearn.Count.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(profileIds, firstMatch.Groups["known"].Value);
        Assert.Equal(profileIds, firstMatch.Groups["ids"].Value);

        var second = RunOnce(dats, template, "second");
        var secondMatch = Built.Match(second.Verdict);
        Assert.True(secondMatch.Success, second.Verdict);
        Assert.StartsWith("fighter=ready ", second.Verdict, StringComparison.Ordinal);
        Assert.Equal(firstMatch.Groups["level"].Value, secondMatch.Groups["level"].Value);
        Assert.All(second.Steps, step => Assert.StartsWith("learn spells ", step, StringComparison.Ordinal)); // nothing changed: no xp, item, raise, train, wield or place; the spellbook is only asked about and every id answers known
        Assert.Equal(firstMatch.Groups["known"].Value, secondMatch.Groups["known"].Value);
        Assert.Equal(firstMatch.Groups["gear"].Value, secondMatch.Groups["gear"].Value);
    }

    /// <summary>The account's stored name (the secrets file's login name for the key), for the components' taper colours only: hashed by the kit and never logged.</summary>
    private static string StoredAccountName(string accountKey)
    {
        var secrets = File.ReadAllBytes(BackendProbe.MainCheckoutFiles.SecretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(accountKey, AccountChoice.Default));
            return credentials.Account;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }
    }

    private Outcome RunOnce(DatSet dats, FighterTemplate template, string label)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        using var recording = new MemoryStream();
        // M4 P5a fix F-abil (live round 1: Harm Self refused with 0x0400): the build also grants the spell components of the profile's spells in the formula the test account casts (the account name is hashed and never logged).
        var scenario = new FighterBuildScenario(XpTableData.Load(dats), HeadlessCharacter.RetailSkillClasses(dats), log, template: template, spells: SpellTableData.Load(dats).Spells, account: StoredAccountName(template.AccountKey));

        HeadlessRunResult result;
        LiveCharacter.Release(output, template.AccountKey);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = template.AccountKey, Server = BackendProbe.Probe().AceEndPoint! },
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
        Assert.Equal("pack", scenario.Caster); // M4 P5a C1b: The Healer's Heart (weenie 8552) is in the pack, never in the hand; kit=ok already counts it
        Assert.Equal(2, scenario.Kit.VitalPotions.Count()); // M4 P5a C7: the potions are lines of the kit, so kit=ok counts them (a short stack reads kit=Mana_Potion:40/100)
        Assert.NotEmpty(scenario.Kit.Components); // M4 P5a fix F-abil: kit=ok counts every component line too (a short one reads kit=Lead_Scarab:3/5), so Harm Self, Heal Self and the buffs are no longer refused with 0x0400
        output.WriteLine($"[{label}] {FighterComponents.Describe([.. scenario.Kit.Components])}; kit burden {scenario.Kit.TotalBurden} of {FighterKit.Capacity(FighterKit.CapacityStrength)}, {scenario.Kit.Items.Count} lines");
        return new Outcome(scenario.VerdictFields(), scenario.SentSteps, advancement);
    }
}

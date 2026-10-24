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
/// M3 2.1 s1, the buff scenario against the bundled ACE (<c>--scenario=buff</c>). WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD (it takes about as long as a cycle at the live regeneration rate: 1,300 s of session time, plus
/// the second run). On <c>BSD Mage</c> only (the test account; it must exist: run the mage build first) the headless runner enters the mage, has the gear on, enters the magic combat mode, casts the profile's 24 buff lines in the
/// owner's order (the mana loop's Stamina to Mana and Revitalize casts between them, a regeneration wait spent in non-combat), upgrades the lines the buffed skill now gives a higher level, recasts the line closest to expiry while it is
/// still active (D25 as narrowed: one cast), returns to non-combat. Time limit 3,000 s a run. Since M4 P4 the first run is over the mage's shared session (one login per test process; the mage is put in peace mode first); the
/// second run keeps a login of its own (<c>ownLogin</c>): its point is that the registry then comes from the server's PlayerDescription.
/// </summary>
/// <remarks>
/// <para><b>What it proves</b> (each assertion names the ACE behaviour it relies on).
/// (a) <c>gear=wielded worn=5</c>: the wand and the leather set are on (GameEvent 0x0023), the cast needs the wielded caster (<c>Player_Combat.cs</c> 872-880).
/// (b) <c>buffs=24/24</c> and <c>misses=none</c>: every self line landed as a GameEvent 0x02C2 enchantment on the player (<c>WorldObject_Magic.cs</c> 448-452) and every armour line named every worn piece in ACE's chat line
/// "You cast &lt;spell&gt; on &lt;item&gt;" (D23; <c>WorldObject_Magic.cs</c> 430-444, 2067-2121).
/// (c) Each buff cast was at the level the rule gives from the skill read at that cast: the highest level whose ACE chance (<c>SkillCheck.GetMagicSkillChance</c>, <c>SkillCheck.cs</c> 7-22) is at least 0.9 (D10), taken from the cycle's
/// own per-cast log line (the spell id and the skill it read), except the one cast after a logged fizzle, which is one level down.
/// (d) After the upgrade pass every line is at VII or better (the plan's prediction: s0a's chain, from skill 316: Focus and Willpower VI first, VII after the buffs have raised the skill); the numbers are printed when it is not.
/// (e) <c>rebuffed=yes forced=yes</c> and <c>min_remaining_s</c> above 0: a line was recast while still active, by a margin of its remaining time plus one second, and no line was left expired.
/// (f) The second run, on a new login of its own (the registry now comes from the server's PlayerDescription): its ordered pass casts no self line (<c>self_casts=0</c>: the buff casts of the ordered pass, the forced pass excluded since it
/// always casts one, and the two armour lines excluded since a new cycle object does not know an earlier session's armour casts, <c>TODO(m3-buff-armour-memory)</c>: <c>armour_casts</c> is at most 2); it still ends
/// <c>buffs=24/24</c> with no miss. How it counts: <see cref="BuffScenario.CycleCast"/> minus <see cref="BuffScenario.CycleArmourCast"/>, which is the cycle's own count of landed casts in the ordered pass.
/// </para>
/// <para>It prints <c>buff_live &lt;the verdict fields&gt;</c> for the lead, then <c>buff_live_second self_casts=&lt;n&gt; armour_casts=&lt;n&gt; &lt;the second verdict fields&gt;</c>. The casts burn components and mana, and the skills may
/// gain proficiency experience (ranks and level only rise). The account name the kit is built for is read from the secrets file at run time and is in no output. Skipped, with the probe's reason, unless the backend runs with the
/// Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[LiveSharedSession]
public sealed class BuffLoopLiveTests(ITestOutputHelper output)
{
    /// <summary>The headless limit of the runs, in seconds (a cycle is about 1,300 s at the live rate).</summary>
    private const double QuitAfterSeconds = 3000;

    private static readonly Regex Verdict = new(
        @"^gear=(?<gear>\w+) worn=(?<worn>\d+) buffs=(?<active>\d+)/(?<wanted>\d+) levels=(?<levels>\S+) rebuffed=(?<rebuffed>yes|no) forced=(?<forced>yes|no) min_remaining_s=(?<min>\d+|n/a) transfers=(?<fired>\d+)/(?<tried>\d+) revitalizes=(?<revitalizes>\d+) fizzles=(?<fizzles>\d+) mana_min=(?<mana>\d+|n/a) cycle_s=(?<cycle>\d+) misses=(?<misses>\S+) surpassed=(?<surpassed>\d+)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CastLine = new(
        @"cast (?<name>.+?) \((?<id>\d+)\) (?:on the caster|at the caster \(every worn piece, D23\)): \w+ skill (?<skill>\d+), chance",
        RegexOptions.CultureInvariant);

    [LoopbackFact]
    public void The_buff_cycle_casts_the_24_lines_at_the_rule_s_levels_upgrades_them_recasts_one_while_active_and_a_second_run_casts_no_self_line()
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;

        var first = RunOnce(spells, "first", ownLogin: false, out var firstLog);
        var match = Verdict.Match(first.VerdictFields());
        Assert.True(match.Success, first.VerdictFields());
        output.WriteLine("buff_live " + first.VerdictFields());

        Assert.Equal("wielded", match.Groups["gear"].Value);
        Assert.Equal("5", match.Groups["worn"].Value);
        Assert.Equal("24", match.Groups["wanted"].Value);
        Assert.Equal("24", match.Groups["active"].Value); // buffs=24/24
        Assert.Equal("none", match.Groups["misses"].Value);
        Assert.Equal("yes", match.Groups["rebuffed"].Value);
        Assert.Equal("yes", match.Groups["forced"].Value);
        Assert.True(int.TryParse(match.Groups["min"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var min) && min > 0, "min_remaining_s: " + match.Groups["min"].Value);

        // (d) Every line at VII or better after the upgrade pass: the numbers go to the output either way.
        var levels = match.Groups["levels"].Value.Split(',');
        var below = levels.Where(l => l.Split(':')[1] is not ("VII" or "VIII")).ToArray();
        Assert.True(below.Length == 0, "lines below VII after the upgrade pass: " + string.Join(", ", below) + "; levels " + match.Groups["levels"].Value + "; " + first.VerdictFields());

        // (c) Each cast at the level the rule gives from the skill the cycle read at that cast. An unbuffed mage casts all 24 lines and the two upgrades (the first live run,
        // M3 2.1 round e2: 26 casts); the cycle's enchantments last an hour and persist on the server, so a mage that arrives buffed casts only the lines under the margin, and
        // every cast it does make is checked (the cycle's own count, and at least the forced pass's one). Printed, so a short first run is visible, never silent.
        var due = Math.Max(1, first.CycleCast);
        output.WriteLine($"buff_live_first cycle_casts={first.CycleCast} (24 or more on an unbuffed mage; fewer when it arrives buffed)");
        AssertRuleLevels(firstLog, due);

        // (f) The second run, on a new login of its own: no self line is cast by its ordered pass.
        var second = RunOnce(spells, "second", ownLogin: true, out var secondLog);
        var again = Verdict.Match(second.VerdictFields());
        Assert.True(again.Success, second.VerdictFields());
        var selfCasts = second.CycleCast - second.CycleArmourCast;
        output.WriteLine($"buff_live_second self_casts={selfCasts} armour_casts={second.CycleArmourCast} {second.VerdictFields()}");
        Assert.Equal("wielded", again.Groups["gear"].Value);
        Assert.Equal("24", again.Groups["active"].Value);
        Assert.Equal("none", again.Groups["misses"].Value);
        Assert.Equal(0, selfCasts);
        Assert.True(second.CycleArmourCast <= 2, "armour casts of the second run: " + second.CycleArmourCast + " (TODO(m3-buff-armour-memory): a new cycle object recasts the two armour lines, nothing else)");
        AssertRuleLevels(secondLog, 1); // the forced pass's one cast, at least
    }

    /// <summary>Every logged buff cast is at the level the rule gives from the skill it logged: the highest level whose chance is at least 0.9, one lower for the cast after a logged fizzle.</summary>
    private void AssertRuleLevels(IReadOnlyList<string> lines, int minCasts)
    {
        var lowered = false;
        var checkedCasts = 0;
        var wrong = new List<string>();
        foreach (var line in lines)
        {
            if (line.Contains("fizzled; one retry", StringComparison.Ordinal))
            {
                lowered = true;
                continue;
            }

            var m = CastLine.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var id = uint.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture);
            var skill = uint.Parse(m.Groups["skill"].Value, CultureInfo.InvariantCulture);
            var profileLine = ArenaProfile.Lines.First(l => l.IsBuff && l.Spells.Any(s => s.Id == id));
            var at = profileLine.Spells.Select((s, i) => (s.Id, Index: i)).First(s => s.Id == id).Index;
            var rule = -1;
            for (var i = profileLine.Spells.Count - 1; i >= 0 && rule < 0; i--)
            {
                if (GearCasts.Chance(skill, profileLine.Spells[i].Power) >= BuffOptions.DefaultCastChance)
                {
                    rule = i;
                }
            }

            var expected = lowered ? rule - 1 : rule;
            lowered = false;
            checkedCasts++;
            if (at != expected)
            {
                wrong.Add($"{m.Groups["name"].Value}: cast at index {at}, the rule gives {expected} from skill {skill}");
            }
        }

        output.WriteLine($"rule check: {checkedCasts} buff casts compared with the rule's level from the skill each logged");
        Assert.True(checkedCasts >= minCasts, $"only {checkedCasts} buff cast lines were logged, at least {minCasts} expected");
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    private BuffScenario RunOnce(IReadOnlyDictionary<uint, SpellInfo> spells, string label, bool ownLogin, out IReadOnlyList<string> buffLines)
    {
        var log = new RecordingLogSink();
        var scenario = new BuffScenario(spells, LiveMage.KitAccount(), new BuffOptions(), log, endSession: false) { RequireCharacter = true };
        var result = LiveMage.Run(output, scenario, "buff-" + label, QuitAfterSeconds, ownLogin).Result;

        output.WriteLine($"[{label}] buff {scenario.VerdictFields()}");
        var lines = log.Lines.Where(l => l.Contains("[buff]", StringComparison.Ordinal) || l.Contains("[gear]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).ToList();
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        buffLines = lines;
        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        return scenario;
    }
}

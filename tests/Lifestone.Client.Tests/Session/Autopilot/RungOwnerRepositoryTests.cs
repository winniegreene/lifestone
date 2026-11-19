using System.Text.RegularExpressions;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>M6 7 AW: one place decides the rung (<see cref="RungOwner"/>). The Godot host cannot reach the band's calls (they are internal); this test fails any other client file that calls them, and the unit cases read the owner's reasons.</summary>
public sealed partial class RungOwnerRepositoryTests
{
    /// <summary>Each line that may name a band call, with why it chooses no ground. Everything else in src fails.</summary>
    private static readonly (string File, string Line, string Why)[] Allowed =
    [
        ("Session/Autopilot/RungOwner.cs", string.Empty, "the owner"),
        ("Session/Grounds/Ladder.cs", "public LadderRung RungFor(int level)", "its definition"),
        ("Session/Mage/RungGateRule.cs", string.Empty, "the gate's own rule, which the owner walks"),
        ("Session/Mage/ClimberHealthRule.cs", "NeedFor(ladder, ladder.RungFor(level))", "the rung's health line for a level: no ground (M6 audit: NeedAt reads it through NeedFor, which the spend also gives the rung she hunts)"),
        ("Session/Mage/GearFloor.cs", "ladder.RungFor(1).MoveOn.Level", "the first rung's data: no ground"),
        ("Session/Mage/WornRestorer.cs", "ladder.RungFor(level) is { } r && (string.Equals(r.GroundKey, ground.Key", "reads the rung of the ground it is given: chooses none"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "internal static LadderDecision DecideRung(", "its definition"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "var rung = ladder.RungFor(level);", "DecideRung's own band"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "internal static HuntingGround? LadderGroundFor(", "its definition"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "var inBand = level >= 1 && level <= ladder.TopLevel ? ladder.RungFor(level) : null;", "LadderDecisionFor, the owner's band read"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "return DecideRung(ladder, level, asked?.GroundKey", "LadderDecisionFor"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "internal static LadderDecision LadderDecisionFor(", "its definition"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "var decision = LadderDecisionFor(LadderLoader.WarMage, level, questDone);", "LadderGroundFor, for the band's own tests"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "ladder.RungFor(level) is not { Kind: LadderRungKind.Quest } rung", "reads the quest stamps of a quest rung: no ground"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "internal static LadderDecision? MoveOnDue(", "its definition"),
        ("Session/Autopilot/AutopilotScenario.Ladder.cs", "var decision = DecideRung(ladder, level, groundKey, null, questDone);", "MoveOnDue, whose level the owner gives"),
    ];

    [GeneratedRegex(@"(?<!cref="")(?<!RungOwner\.)\b(RungFor|DecideRung|LadderDecisionFor|LadderGroundFor|BandGround|LevelUpGround|MoveOnDue)\(|RungGateRule\.(Walk|HeldLevel)\(")]
    private static partial Regex BandCall();

    private static string Src([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lifestone.Client")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src");
    }

    [Fact]
    public void No_path_outside_the_owner_chooses_a_rung_by_the_level_band()
    {
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Path.Combine(Src(), "Lifestone.Client"), file).Replace('\\', '/');
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                var code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || !BandCall().IsMatch(code))
                {
                    continue;
                }

                if (rel.EndsWith("AutopilotHostLogic.cs", StringComparison.Ordinal) && code.Contains("internal static HuntingGround? LevelUpGround(int level, out string why) => RungOwner.BandGround(", StringComparison.Ordinal))
                {
                    continue; // the band's own tests' name for it
                }

                if (!Allowed.Any(a => rel == a.File && code.Contains(a.Line, StringComparison.Ordinal)))
                {
                    found.Add($"{rel}:{n}: {code}");
                }
            }
        }

        Assert.True(found.Count == 0, "a path chooses a rung by the band; ask RungOwner.Decide:\n" + string.Join("\n", found));
    }

    private static Lifestone.Client.Session.Contracts.PlayerQualities Trained(bool item = true)
    {
        var q = new Lifestone.Client.Session.Contracts.PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new Lifestone.Net.Messages.SkillDesc(MageTemplate.ItemEnchantment, 21, 1, item ? 2u : 1u, 0, 0u, 0, 0);
        return q;
    }

    private static readonly Func<string, int, bool> WholeBook = static (_, _) => true;

    private static RungChoice Ask(int level, uint? cell = null, bool up = false, string? named = null, string? run = null, bool leaving = false, Func<string, int, bool>? knows = null) =>
        RungOwner.Decide(LadderLoader.WarMage, new RungAsk(level, cell, Trained(), knows ?? WholeBook, up ? new RungBuffRead(_ => 8, _ => 4) : null, named) { RunGround = run, Leaving = leaving });

    private static uint CellIn(string ground) => (uint)GroundCatalog.Get(ground).Landblocks[0] << 16 | 0x0100;

    [Fact]
    public void Every_answer_names_its_reason()
    {
        Assert.Equal("band", Ask(9).Reason); // M6 14 (D135): the outskirts to 9, the Burrow from 10
        Assert.Equal("gate-held:lodge", Ask(17).Reason); // the first gate that shut, the walk's top
        Assert.Equal("gate-held:lodge", Ask(17, CellIn("tusker-burrow")).Reason);
        Assert.Equal("gate-held:burrow", Ask(12).Reason);
        Assert.Equal("gate-open:lodge", Ask(17, up: true).Reason);
        Assert.Equal("stands-on:tusker-lodge", Ask(17, CellIn("tusker-lodge")).Reason);
        Assert.Equal("stop-named:burrow", Ask(19, named: "burrow").Reason);
    }

    [Fact]
    public void Unknown_buffs_never_open_a_gate_on_a_whole_book()
    {
        var choice = Ask(21);

        Assert.Equal("holtburg-outskirts", choice.Ground?.Key);
        Assert.All(choice.Gates.SelectMany(g => g.Conditions).Where(c => c.Name.EndsWith(" up", StringComparison.Ordinal)), c => Assert.False(c.Met));
    }

    [Fact]
    public void A_named_rung_is_read_for_the_book_and_never_past_a_shut_gate_or_above_her_level()
    {
        Assert.Equal("tusker-retreat", Ask(17, named: "burrow").Ground?.Key); // the run read its buffs live (M6 retreat: the burrow rung's ground is the Tusker Retreat)
        Assert.Equal("holtburg-redoubt", Ask(17, named: "lodge", knows: static (_, _) => false).Ground?.Key); // a book that does not hold the gate's lines: held, whoever named it
        Assert.Equal("holtburg-redoubt", Ask(9, named: "lodge").Ground?.Key); // above her level: ignored (no plan read: no rung ahead of the band, RungOwner.Ahead)
    }

    [Fact]
    public void A_run_does_not_reread_its_own_gate_until_she_leaves_for_it()
    {
        Assert.Equal(LadderStepKind.Hunt, Ask(17, run: "tusker-lodge").Decision.Kind);
        var leaving = Ask(17, run: "tusker-lodge", leaving: true);
        Assert.Equal(LadderStepKind.Moved, leaving.Decision.Kind);
        Assert.Equal("rung-moved:outskirts", leaving.Decision.Stop);
    }

    [Fact]
    public void A_level_unknown_or_past_the_top_is_a_named_stop_and_no_ground()
    {
        Assert.Null(Ask(0).Ground);
        Assert.StartsWith("failed:rung-level-unknown", Ask(0).Why, StringComparison.Ordinal);
        Assert.Null(Ask(LadderLoader.WarMage.TopLevel + 1).Ground);
    }

    private static CellFrame At(uint cell, float x, float y) => new(cell, new Lifestone.Data.Animation.AcFrame(new System.Numerics.Vector3(x, y, 94), System.Numerics.Quaternion.Identity));

    [Theory]
    [InlineData(10, 4, RestBlock.Wait, false, false, PassPlace.WalkHome)]
    [InlineData(5, 4, RestBlock.ShootFirst, false, false, PassPlace.WalkHome)]
    [InlineData(4, 4, RestBlock.Wait, false, false, PassPlace.Here)] // a short pass stays
    [InlineData(10, 4, RestBlock.None, false, false, PassPlace.Here)] // nothing near: she lies down where she is
    [InlineData(10, 4, RestBlock.Wait, true, false, PassPlace.Here)] // home already
    [InlineData(10, 4, RestBlock.Wait, false, true, PassPlace.Here)] // one walk a pass, never a loop
    public void A_full_pass_with_a_creature_near_walks_home_once(int lines, int over, RestBlock block, bool atHome, bool walked, PassPlace expected)
    {
        Assert.Equal(expected, PassPlaceRule.Decide(lines, over, block, atHome, walked, out var why));
        Assert.NotEmpty(why);
    }

    [Fact]
    public void Home_is_the_quiet_distance_of_the_lifestone_point_in_its_own_landblock()
    {
        var home = GroundCatalog.Get("holtburg-outskirts").Home;

        Assert.True(PassPlaceRule.AtHome(At(0xA9B40019, 84, 20), home));
        Assert.False(PassPlaceRule.AtHome(At(0xA9B40019, 150, 7.7f), home));
        Assert.False(PassPlaceRule.AtHome(At(0xA8B50034, 84, 7.7f), home));
    }

    [Fact]
    public void The_pass_line_is_a_ladder_value_with_its_source()
    {
        Assert.Equal(4, LadderLoader.WarMage.Vitals.PassHomeOverLines);
        Assert.Contains("pass_home_over_lines", LadderLoader.WarMage.Sources["vitals"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_rest_reports_what_it_gained_with_the_presses_added_back()
    {
        Assert.Equal(0.6, VitalsKeeper.GainedPerSecond(27, 43, 110, 209.1), 2); // stretch 12: 0.08 a second net, ACE's standing 0.6
        Assert.Equal(1.8, VitalsKeeper.GainedPerSecond(28, 106, 0, 43.4), 1); // stretch 13, lying
        Assert.Equal(0, VitalsKeeper.GainedPerSecond(28, 28, 0, 0));
    }
}

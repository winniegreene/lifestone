using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2d (docs/automation-engine.md section 3, "shadow first"): a model of a target's health beside the one picture. The model's read decides; the picture is asked for the same creature, an ask is counted as agreeing
/// or differing by a named tolerance, and a difference is one line when it appears, when its cause changes and when it ends.
/// </summary>
public sealed class HealthShadowTests
{
    private const string Guard = "Tusker Guard";

    private static HealthRead Picture(HealthBasis basis, double health, int max, double dealt = 0, double regen = 0, double baseAge = 5, double tick = 0) =>
        new(basis, health, max, HealthMaxSource.Bestiary, health + dealt - regen, baseAge, dealt, regen, tick / 5, tick);

    [Fact]
    public void The_tolerance_is_a_point_a_hundredth_of_the_larger_maximum_or_one_heartbeats_regeneration()
    {
        Assert.Equal(1.0, HealthShadow.Tolerance(50, Picture(HealthBasis.Full, 50, 50)));
        Assert.Equal(6.0, HealthShadow.Tolerance(400, Picture(HealthBasis.Full, 600, 600)));
        Assert.Equal(25.0, HealthShadow.Tolerance(600, Picture(HealthBasis.Counted, 300, 600, tick: 25)));
    }

    [Fact]
    public void Two_reads_inside_the_tolerance_agree_and_an_unknown_agrees_with_a_full_one()
    {
        var full = Picture(HealthBasis.Full, 600, 600);
        Assert.Equal(HealthDifference.None, HealthShadow.Judge(true, 600, 600, full));
        Assert.Equal(HealthDifference.None, HealthShadow.Judge(true, 594, 600, full)); // 6 points: the hundredth
        Assert.Equal(HealthDifference.None, HealthShadow.Judge(false, 0, 600, full)); // a model told nothing says nothing against its maximum
        Assert.Equal(HealthDifference.None, HealthShadow.Judge(false, 0, 0, default)); // neither knows it
        Assert.Equal(HealthDifference.None, HealthShadow.Judge(true, 300, 600, Picture(HealthBasis.Reported, 305, 600)));
    }

    [Fact]
    public void Each_cause_of_a_difference_is_named_from_what_both_sides_state()
    {
        var hurt = Picture(HealthBasis.Counted, 400, 600, dealt: 200);
        Assert.Equal(HealthDifference.ModelUnknown, HealthShadow.Judge(false, 0, 600, hurt));
        Assert.Equal(HealthDifference.PictureUnknown, HealthShadow.Judge(true, 300, 400, default));
        Assert.Equal(HealthDifference.Maximum, HealthShadow.Judge(true, 400, 400, Picture(HealthBasis.Full, 600, 600))); // both full, of two maxima
        Assert.Equal(HealthDifference.Maximum | HealthDifference.Damage, HealthShadow.Judge(true, 400, 400, hurt, modelDealt: 0)); // two maxima, and the shares differ too

        // the picture stands higher by exactly what regenerated
        Assert.Equal(HealthDifference.Regeneration, HealthShadow.Judge(true, 400, 600, Picture(HealthBasis.Counted, 450, 600, dealt: 200, regen: 50), modelDealt: 200));
        // the model counted 120 of hers the picture never heard
        Assert.Equal(HealthDifference.Damage, HealthShadow.Judge(true, 280, 600, hurt, modelDealt: 320, modelWordAge: double.PositiveInfinity));
        // the picture took a word of the server 2 s ago; the model's last is 30 s old
        Assert.Equal(HealthDifference.ServerUpdate, HealthShadow.Judge(true, 500, 600, Picture(HealthBasis.Reported, 300, 600, baseAge: 2), modelDealt: 0, modelWordAge: 30));
        // the model stands on a word of the server and the picture on none
        Assert.Equal(HealthDifference.ServerUpdate, HealthShadow.Judge(true, 300, 600, Picture(HealthBasis.Counted, 600, 600), modelDealt: 0, modelWordAge: 4));
        // the same word on both sides, the same count: a gap nothing stated explains is one of the two, not told apart
        Assert.Equal(HealthDifference.Damage | HealthDifference.ServerUpdate, HealthShadow.Judge(true, 500, 600, Picture(HealthBasis.Reported, 300, 600, baseAge: 2), modelDealt: 0, modelWordAge: 2.1));
        Assert.Equal(HealthDifference.Damage | HealthDifference.ServerUpdate, HealthShadow.Judge(true, 500, 600, hurt));
    }

    [Fact]
    public void A_difference_is_one_line_when_it_appears_changes_and_ends_and_every_ask_is_counted()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var shadow = picture.Shadow;
        var guid = t.Creature(Guard, t.Listed, 6);
        var max = Bestiary.Find(t.Listed)!.Vitals.MaxHealth!.Value;
        var lines = new List<string>();
        Action<string> line = lines.Add;
        const HealthReader Lane = HealthReader.FightPicker;

        shadow.Compare(Lane, guid, false, 0, max, t.Now, line); // never told on either side
        shadow.Compare(Lane, guid, true, max, max, t.Now, line);
        Assert.Equal((2L, 0L), (shadow.Agree(Lane), shadow.Differ(Lane)));
        Assert.Empty(lines);

        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI."); // the picture hears it; the model below never does
        for (var i = 0; i < 5; i++)
        {
            shadow.Compare(Lane, guid, true, max, max, t.Now, line, modelDealt: 0, modelWordAge: double.PositiveInfinity);
            t.Wait(0.3);
        }

        Assert.Equal((2L, 5L), (shadow.Agree(Lane), shadow.Differ(Lane)));
        var first = Assert.Single(lines);
        Assert.StartsWith($"health-shadow: fight-picker: Tusker Guard 0x{guid:X8}: its model says {max} of {max}, the one picture says ", first, StringComparison.Ordinal);
        Assert.Contains($" of {max} (", first, StringComparison.Ordinal);
        Assert.Contains("damage one did not hear: the picture counts 82 of hers since its base, its model counts 0)", first, StringComparison.Ordinal);

        // the cause changes: the server tells a share the model does not take
        HealthPictureTests.Update(t, guid, 0.5f);
        t.Wait(HealthShadow.LineGapSeconds);
        shadow.Compare(Lane, guid, true, max, max, t.Now, line, modelDealt: 0, modelWordAge: double.PositiveInfinity);
        shadow.Compare(Lane, guid, true, max, max, t.Now, line, modelDealt: 0, modelWordAge: double.PositiveInfinity);
        Assert.Equal(2, lines.Count);
        Assert.Contains("a server update one did not take: the picture's is ", lines[1], StringComparison.Ordinal);
        Assert.Contains(" from a health update ", lines[1], StringComparison.Ordinal);
        Assert.Contains(", its model holds none", lines[1], StringComparison.Ordinal);

        // it ends: the model reads what the picture reads
        t.Wait(HealthShadow.LineGapSeconds);
        var now = picture.Read(guid, t.Now);
        shadow.Compare(Lane, guid, true, now.Health, max, t.Now, line);
        Assert.Equal(3, lines.Count);
        Assert.Matches($@"^health-shadow: fight-picker: Tusker Guard 0x{guid:X8}: agrees again after [0-9.]+ s \(7 ask\(s\) differed\)$", lines[2]);
        Assert.Equal((3L, 7L, 3L), (shadow.Agree(Lane), shadow.Differ(Lane), shadow.Lines));
        Assert.Equal((0L, 0L), (shadow.Agree(HealthReader.FightOpener), shadow.Differ(HealthReader.FightOpener))); // a lane counts its own asks alone
    }

    [Fact]
    public void A_lane_writes_one_line_a_second_at_most_and_names_what_it_did_not_write()
    {
        var t = new ThreatRig();
        var shadow = t.Board.Health.Shadow;
        var max = Bestiary.Find(t.Listed)!.Vitals.MaxHealth!.Value;
        var one = t.Creature(Guard, t.Listed, 4);
        var two = t.Creature("Tusker Slave", t.Listed, 8);
        var lines = new List<string>();
        Action<string> line = lines.Add;
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        t.Magic("You scorch Tusker Slave for 60 points with Flame Bolt VI.");
        shadow.Compare(HealthReader.FightEmergency, one, true, max, max, t.Now, line, 0, double.PositiveInfinity);
        shadow.Compare(HealthReader.FightEmergency, two, true, max, max, t.Now, line, 0, double.PositiveInfinity); // inside the gap: held back
        Assert.Single(lines);
        shadow.Compare(HealthReader.FightEmergency, two, true, max - 60, max, t.Now, line, 60, double.PositiveInfinity); // and it ended before its line
        t.Wait(HealthShadow.LineGapSeconds);
        shadow.Compare(HealthReader.FightEmergency, one, true, max - 82, max, t.Now, line, 82, double.PositiveInfinity);
        Assert.Equal(2, lines.Count);
        Assert.EndsWith($" (+1 change(s) inside {HealthShadow.LineGapSeconds:0.#} s not written)", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_owner_reports_its_share_of_the_sessions_lanes_since_its_mark()
    {
        var t = new ThreatRig();
        var shadow = t.Board.Health.Shadow;
        var max = Bestiary.Find(t.Listed)!.Vitals.MaxHealth!.Value;
        var guid = t.Creature(Guard, t.Listed, 6);
        Action<string> line = static _ => { };
        shadow.Compare(HealthReader.FightPicker, guid, true, max, max, t.Now, line); // before the owner began: the session's, not its
        var share = new HealthShadowShare(shadow, HealthReader.FightPicker, HealthReader.FightEmergency, HealthReader.FightWar);
        Assert.Null(share.Line("fight"));

        shadow.Compare(HealthReader.FightPicker, guid, true, max, max, t.Now, line);
        shadow.Compare(HealthReader.FightPicker, guid, true, max, max, t.Now, line);
        shadow.Compare(HealthReader.FightEmergency, guid, true, max / 2.0, max, t.Now, line);
        shadow.Compare(HealthReader.FightOpener, guid, true, max, max, t.Now, line); // a lane outside this owner's share
        Assert.Equal((2L, 1L), share.Counts);
        Assert.Equal("health-shadow: fight: its asks: health_shadow_agree=2 health_shadow_differ=1 (agreed/differed by reader: fight-picker 2/0, fight-emergency 0/1)", share.Line("fight"));
        share.Remark();
        Assert.Null(share.Line("fight"));
        Assert.Equal("health_shadow_agree=3 health_shadow_differ=0", HealthShadow.FieldsOf(shadow.Agree(HealthReader.FightPicker), shadow.Differ(HealthReader.FightPicker)));
    }

    [Fact]
    public void A_read_after_a_creatures_death_is_compared_with_its_health_just_before()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = Bestiary.Find(t.Listed)!.Vitals.MaxHealth!.Value;
        var guid = t.Creature(Guard, t.Listed, 6);
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        HealthPictureTests.Die(t, guid);
        var lines = new List<string>();
        picture.Shadow.Compare(HealthReader.FightWar, guid, true, max - 82, max, t.Now, lines.Add); // a read after its death: the creature's health before the blow
        Assert.Equal((1L, 0L), (picture.Shadow.Agree(HealthReader.FightWar), picture.Shadow.Differ(HealthReader.FightWar)));
        picture.Shadow.Compare(HealthReader.FightWar, guid, true, max, max, t.Now, lines.Add); // a model that never heard the 82: counted, no memory to write it by
        Assert.Equal((1L, 1L), (picture.Shadow.Agree(HealthReader.FightWar), picture.Shadow.Differ(HealthReader.FightWar)));
        Assert.Empty(lines);
    }

    [Fact]
    public void Every_reader_has_a_word_of_its_own()
    {
        var words = Enum.GetValues<HealthReader>().Select(HealthShadow.Word).ToArray();
        Assert.Equal(HealthShadow.ReaderCount, words.Length);
        Assert.Equal(words.Length, words.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(("fight-picker", "fight-emergency", "fight-war"), (HealthShadow.Word(HealthReader.FightPicker), HealthShadow.Word(HealthReader.FightEmergency), HealthShadow.Word(HealthReader.FightWar))); // M6 16 Q5: the guard's and the travel legs' lanes are gone
    }
}

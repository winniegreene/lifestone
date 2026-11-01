using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The examine window's ratings and set block (M5 7 polish, D137; <see cref="ExamineRatingLines"/>): <c>Appraisal_ShowRatings</c>'s "Ratings: " line in retail's order of the gear ratings (11.6096 0x004AF400), the vitality line, the blank
/// lines retail writes after them and after the set, and the set line before them; each over a scripted 00C9 answer as ACE writes it, through the real reducer.
/// </summary>
public sealed class ExamineRatingLinesTests
{
    private const uint Item = 0x80000B01;

    internal static Appraisal Answered(Dictionary<uint, int> ints)
    {
        var r = new WorldActionRig();
        r.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, Item, new AceAppraisal { Success = true, Ints = ints }));
        Assert.True(r.H.State.Appraisals.TryGet(Item, out var appraisal));
        return appraisal;
    }

    [Fact]
    public void Every_gear_rating_shows_in_retails_order_with_its_words()
    {
        // Ids given in id order; retail prints crit damage before crit resist, the overpower and PK ratings before the heal boost, and life resist last.
        var a = Answered(new Dictionary<uint, int>
        {
            [370] = 1, [371] = 2, [372] = 3, [373] = 4, [374] = 5, [375] = 6, [376] = 7, [377] = 8, [378] = 9, [383] = 10, [384] = 11, [388] = 12, [389] = 13,
        });
        Assert.Equal(
            "Ratings: Dam 1, Dam Resist 2, Crit 3, Crit Dam 5, Crit Resist 4, Crit Dam Resist 6, Overpower% 12, Overpower Reduction% 13, PK Dam 10, PK Dam Resist 11, Heal Boost 7, Nether Resist 8, Life Resist 9.",
            ExamineRatingLines.RatingsLine(a));
    }

    [Fact]
    public void A_single_rating_has_no_comma_and_a_closing_full_stop_then_retails_blank_line()
    {
        var a = Answered(new Dictionary<uint, int> { [AppraisalIds.Value] = 5000, [ExamineRatingLines.GearCritDamage] = 2 });
        Assert.Equal(["Ratings: Crit Dam 2.", string.Empty], ExamineRatingLines.RatingLines(a));
    }

    [Fact]
    public void A_rating_of_zero_or_below_is_not_shown()
    {
        var a = Answered(new Dictionary<uint, int> { [ExamineRatingLines.GearDamage] = 0, [ExamineRatingLines.GearCrit] = -1, [ExamineRatingLines.GearDamageResist] = 3 });
        Assert.Equal("Ratings: Dam Resist 3.", ExamineRatingLines.RatingsLine(a));
    }

    [Fact]
    public void The_vitality_line_follows_the_ratings_and_shows_alone_without_them()
    {
        var both = Answered(new Dictionary<uint, int> { [ExamineRatingLines.GearDamage] = 2, [ExamineRatingLines.GearMaxHealth] = 10 });
        Assert.Equal(["Ratings: Dam 2.", "This item adds 10 Vitality.", string.Empty], ExamineRatingLines.RatingLines(both));

        var alone = Answered(new Dictionary<uint, int> { [ExamineRatingLines.GearMaxHealth] = 5 });
        Assert.Null(ExamineRatingLines.RatingsLine(alone));
        Assert.Equal(["This item adds 5 Vitality.", string.Empty], ExamineRatingLines.RatingLines(alone));
    }

    [Fact]
    public void An_item_without_ratings_or_set_adds_no_line_at_all()
    {
        var a = Answered(new Dictionary<uint, int> { [AppraisalIds.Value] = 10, [AppraisalIds.Workmanship] = 4, [ExamineRatingLines.GearMaxHealth] = 0 });
        Assert.Empty(ExamineRatingLines.RatingLines(a));
        Assert.Empty(ExamineRatingLines.SetAndRatings(a));
    }

    [Fact]
    public void A_creatures_own_ratings_are_not_the_item_windows()
    {
        // ACE's AppraiseInfo.AddRatings writes 307 to 315 for a creature; retail's item window reads only the gear ratings.
        var a = Answered(new Dictionary<uint, int> { [307] = 20, [308] = 10, [314] = 5 });
        Assert.Empty(ExamineRatingLines.SetAndRatings(a));
    }

    [Fact]
    public void The_block_is_the_set_then_the_ratings_then_retails_two_blank_lines()
    {
        var a = Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 14, [ExamineRatingLines.GearCrit] = 1, [ExamineRatingLines.GearMaxHealth] = 3 });
        Assert.Equal(["Set: Adept's", "Ratings: Crit 1.", "This item adds 3 Vitality.", string.Empty, string.Empty], ExamineRatingLines.SetAndRatings(a));
    }

    [Fact]
    public void A_set_alone_is_followed_by_one_blank_line()
    {
        var a = Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 13 });
        Assert.Equal(["Set: Soldier's", string.Empty], ExamineRatingLines.SetAndRatings(a));
    }

    [Fact]
    public void Ratings_alone_are_followed_by_their_blank_line_and_the_blocks()
    {
        var a = Answered(new Dictionary<uint, int> { [ExamineRatingLines.GearHealingBoost] = 4 });
        Assert.Equal(["Ratings: Heal Boost 4.", string.Empty, string.Empty], ExamineRatingLines.SetAndRatings(a));
    }

    [Fact]
    public void A_set_retail_does_not_name_writes_no_set_line_and_no_blank()
    {
        var a = Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 140 }); // ACE's ParagonMelee: not in 11.6096's table
        Assert.Empty(ExamineRatingLines.SetAndRatings(a));
    }

    [Fact]
    public void The_added_set_lines_go_right_after_the_set_line_and_only_with_a_set()
    {
        var withSet = Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 15, [ExamineRatingLines.GearDamage] = 1 });
        Assert.Equal(["Set: Archer's", "Pieces worn: 2", "Ratings: Dam 1.", string.Empty, string.Empty], ExamineRatingLines.SetAndRatings(withSet, ["Pieces worn: 2"]));

        var noSet = Answered(new Dictionary<uint, int> { [ExamineRatingLines.GearDamage] = 1 });
        Assert.Equal(["Ratings: Dam 1.", string.Empty, string.Empty], ExamineRatingLines.SetAndRatings(noSet, ["Pieces worn: 2"]));
    }
}

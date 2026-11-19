using DatReaderWriter.Enums;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6, the recall's components (live 2026-11-18 20:29:54 UTC, the sprint's step3-levelup.log: the learn trip's way home from Dryreach was Lifestone Recall because the book held it, ACE refused the cast 0x0400 and the station failed
/// <c>learn: town trip: the way home from Dryreach by Lifestone Recall</c>). The learn trip's way home (<c>TownTripHome</c>) on the outskirts' rig, whose trip to Dryreach ends at its first hop (the rig holds no portal), so the way home
/// is taken from Holtburg: what these prove is the decision and its lines, not a walk. A recall the pack is short for is planned and taken by portals and the station does not fail; a recall the check could not judge, refused for a
/// component by the travel seam, is recovered once by the portals; any other failure of the recall still fails the station by name.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const string FormulaAccount = "formula-test-account";

    /// <summary>The outskirts' climber of the V trip (War Magic 250: Flame Bolt V is sold at Dryreach) with Lifestone Recall in the book, and no Item Enchantment focus in the pack.</summary>
    private static Home RecallHomeRig()
    {
        var climber = Climber(warRanks: 200);
        return new Home(Outskirts, 2_000_000, description: climber with { Spells = climber.Spells.Add(RecallHome.LifestoneRecallSpell, 2f) });
    }

    /// <summary>The spell table the trip is given: Lifestone Recall's row in the player-castable layout, Item Enchantment.</summary>
    private static Dictionary<uint, SpellInfo> RecallTable() => new()
    {
        [RecallHome.LifestoneRecallSpell] = WorldActionRig.Spell(RecallHome.LifestoneRecallSpell, SpellType.Enchantment) with { Name = "Lifestone Recall", School = MagicSchool.ItemEnchantment, Formula = [6, 63, 7, 63, 25, 37, 63, 49], FormulaVersion = 1 },
    };

    private static string TripDump(Home home) => string.Join(" | ", home.Lines.Where(l => l.Contains("town trip", StringComparison.Ordinal) || l.Contains("learn_trip", StringComparison.Ordinal) || l.Contains("purpose=home", StringComparison.Ordinal) || l.Contains("FAIL", StringComparison.Ordinal)));

    [Fact]
    public void M6_recall_components_a_known_recall_the_pack_is_short_for_is_planned_and_taken_home_by_portals_and_the_station_does_not_fail()
    {
        var home = RecallHomeRig();
        JourneyFacts? facts = null;
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory(), AccountName: FormulaAccount), RecallTable(), f => facts = f);
        var status = home.Stations.Run(run, 600, pump: home.Shops.Pump);
        var dump = TripDump(home);
        Assert.True(status == ScenarioStatus.Succeeded, dump);

        // The plan: a recall she cannot cast is no way home, so the town must have its own way back by portals (as for a book without the spell).
        Assert.True(home.Logged("by the Town Network to Dryreach (travel=town-network)"), dump);
        Assert.True(home.Logged("home by portals:dryreach (the first admitted)"), dump);
        Assert.DoesNotContain(home.Lines, l => l.Contains("home by lifestone-recall", StringComparison.Ordinal));

        // The way home: the check before names the read and the way, and the recall is never asked of the travel seam.
        Assert.True(home.Logged("learn: town trip: the way home: check before: lifestone_recall=yes components=short -> way=portals (Lifestone Recall is in the spellbook and the pack is short for one cast: "), dump);
        Assert.True(home.Logged("walking: purpose=home (the learn trip to Dryreach: Lifestone Recall is in the spellbook and the pack is short for one cast: "), dump);
        Assert.True(home.Logged("(recall-short:components); the rung change direction=home by portals to holtburg)"), dump);
        Assert.True(home.Logged("learn: town trip: the way home by portals: "), dump);
        Assert.DoesNotContain(home.Travel.Requests, r => r.Purpose == TravelPurpose.Home);
        Assert.Null(run.Failure);

        // The station's account reaches the run's facts (the rung change home reads the same formula), and is never logged.
        Assert.Equal(FormulaAccount, facts!.FormulaAccount);
        Assert.DoesNotContain(home.Lines, l => l.Contains(FormulaAccount, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M6_recall_components_a_recall_the_check_could_not_judge_and_ACE_refuses_for_a_component_is_recovered_by_the_portals_home()
    {
        var home = RecallHomeRig();
        JourneyFacts? facts = null;
        const string refusal = "travel:recall:cast: ServerError (code 0x400) YouDontHaveAllTheComponents 0x0400: the server refused the cast: the player lacks a spell component (WeenieError 0x0400)";
        home.Travel.FailFor = name =>
        {
            if (name != "home-lifestone")
            {
                return null;
            }

            facts!.LifestoneRecallUnavailable = true; // as the travel seam's recall leg names it (J1JourneyTravelTests)
            return refusal;
        };
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()), RecallTable(), f => facts = f); // no account name and no focus: the pack is not judged
        var status = home.Stations.Run(run, 600, pump: home.Shops.Pump);
        var dump = TripDump(home);
        Assert.True(status == ScenarioStatus.Succeeded, dump);

        Assert.True(home.Logged("home by lifestone-recall (the first admitted)"), dump);
        Assert.True(home.Logged("learn: town trip: the way home: check before: lifestone_recall=yes components=not-judged -> way=recall"), dump);
        Assert.Single(home.Travel.Requests, r => r.Purpose == TravelPurpose.Home); // the recall is tried once
        Assert.True(home.Logged($"learn: town trip: recovery: Lifestone Recall was refused for a component ({refusal}): the way home by portals instead"), dump);
        Assert.True(home.Logged("walking: purpose=home (the learn trip to Dryreach: Lifestone Recall is in the spellbook and ACE refused it for a component earlier this run (recall-unavailable:components); the rung change direction=home by portals to holtburg)"), dump);
        Assert.True(home.Logged("learn: town trip: check after the way home: at "), dump);
        Assert.Null(run.Failure);
        Assert.DoesNotContain(home.Lines, l => l.Contains("the way home from Dryreach by Lifestone Recall", StringComparison.Ordinal)); // the live failure's line
    }

    [Fact]
    public void M6_recall_components_a_recall_that_fails_for_another_reason_still_fails_the_station_by_name()
    {
        var home = RecallHomeRig();
        home.Travel.FailFor = name => name == "home-lifestone" ? "travel:recall:teleport: no teleport began" : null;
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()), RecallTable());
        var status = home.Stations.Run(run, 600, pump: home.Shops.Pump);
        var dump = TripDump(home);
        Assert.True(status == ScenarioStatus.Failed, dump);

        Assert.Contains("learn: town trip: the way home from Dryreach by Lifestone Recall: travel:recall:teleport: no teleport began", run.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(home.Lines, l => l.Contains("learn: town trip: recovery: Lifestone Recall was refused", StringComparison.Ordinal));
    }
}

using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Repository;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 0 a6: the way home and the way to an outdoor ground for a character with no Lifestone Recall (Winnie early in the climb). The rule as pure functions (a read spellbook that lacks the recall walks from an outdoor ground reachable on foot and stops by
/// name anywhere else; an unread book or a book that holds the recall casts, as BSD Mage always has); the recall-home station's walk (a point goal through the travel seam, Bind, with its purpose logged and its arrival checked) and its named stop; the den-travel's
/// outdoor arrival (a walk to the first cell, never a portal or a place); Winnie's buff profile (every line learned by play); the grant gate and the recall for Winnie; and slice-check's backend step as a SKIP line.
/// </summary>
public sealed class WayHomeTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string Belt = "low-b-belt";

    private static Spellbook Book(bool known, params uint[] spells)
    {
        var book = new Spellbook { IsKnown = known };
        foreach (var spell in spells)
        {
            book.Learn(spell);
        }

        return book;
    }

    [Fact]
    public void The_rule_casts_when_the_book_holds_the_recall_or_is_unread_and_walks_from_the_belt_when_it_lacks_it()
    {
        var belt = GroundCatalog.Get(Belt);

        Assert.Equal(WayHomeKind.Recall, WayHomeRule.Choose(belt, Book(true, RecallHome.LifestoneRecallSpell)).Kind);
        Assert.Equal(WayHomeKind.Recall, WayHomeRule.Choose(belt, Book(false)).Kind); // not read yet: the recall is tried, as before
        var walk = WayHomeRule.Choose(belt, Book(true, 2644));
        Assert.Equal(WayHomeKind.Walk, walk.Kind);
        Assert.Contains("Holtburg", walk.Why, StringComparison.Ordinal);
        Assert.Contains("Lifestone Recall", walk.Why, StringComparison.Ordinal);
        Assert.True(WayHomeRule.HomeOnFoot(belt));
        Assert.Equal(belt.Outdoor!.Cells[0], WayHomeRule.FirstCell(belt));
    }

    [Fact]
    public void Any_other_case_without_the_recall_is_a_named_stop_that_points_at_the_first_tie_walk()
    {
        var den = GroundCatalog.Get(FightOptions.DefaultGround);
        var stop = WayHomeRule.Choose(den, Book(true));

        Assert.Equal(WayHomeKind.Stop, stop.Kind);
        Assert.Contains("TODO(m6-first-tie-walk)", stop.Why, StringComparison.Ordinal);
        Assert.False(WayHomeRule.HomeOnFoot(den));
        Assert.Null(WayHomeRule.FirstCell(den));
        Assert.Equal(WayHomeKind.Recall, WayHomeRule.Choose(den, Book(true, RecallHome.LifestoneRecallSpell)).Kind); // BSD Mage's build teaches it: unchanged
    }

    private static void LeaveBookWithout(J3bRig rig)
    {
        var book = rig.R.H.State.Self.Spellbook;
        book.Clear();
        book.IsKnown = true; // read, and it holds no spell: the climber at level 5
    }

    [Fact]
    public void The_recall_home_station_walks_to_the_lifestone_point_as_a_point_goal_with_its_purpose_logged_and_checks_the_arrival()
    {
        var t = new J3bRig();
        LeaveBookWithout(t);
        var ctx = t.Context(dungeon: GroundCatalog.Get(Belt));
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        var request = Assert.Single(t.Travel.Requests);
        Assert.Equal((TravelPurpose.Bind, 0u), (request.Purpose, request.ObjectGuid)); // a point goal on foot, never a recall or a place
        var home = GroundCatalog.Get(Belt).Home;
        Assert.Equal(home.LifestoneCell, request.Destination.Location.Position.CellId);
        Assert.Equal(home.LifestonePoint, request.Destination.Location.Position.Local);
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal));
        Assert.Equal(0, t.Places);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("walk check before: purpose: home to Holtburg's lifestone point on foot", StringComparison.Ordinal));
        // The rig's walk does not move the player (the Academy cell), so the after-check names the miss: check, act, check.
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("arrival-off-point", run.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_recall_off_an_outdoor_ground_the_station_stops_by_name_and_walks_nowhere()
    {
        var t = new J3bRig();
        LeaveBookWithout(t);
        var (run, status) = t.RunStation(new RecallHomeStation(), t.Context());

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("recall-home: no way home", run.Failure, StringComparison.Ordinal);
        Assert.Contains("m6-first-tie-walk", run.Failure, StringComparison.Ordinal);
        Assert.Empty(t.Travel.Requests);
    }

    [RequiresDatsFact]
    public void The_den_travel_arrives_at_an_outdoor_ground_by_a_walk_to_its_first_cell_never_a_portal_or_a_place()
    {
        var t = new J3bRig { Dats = fixture.Dats };
        var belt = GroundCatalog.Get(Belt);
        var (run, status) = t.RunStation(new DenTravelStation(), t.Context(dungeon: belt));

        var request = Assert.Single(t.Travel.Requests);
        Assert.Equal((TravelPurpose.Cell, 0u), (request.Purpose, request.ObjectGuid));
        Assert.Equal(belt.Outdoor!.Cells[0].Cell, request.Destination.Location.Position.CellId);
        Assert.Equal(belt.Outdoor.Cells[0].Spot.X, request.Destination.Location.Position.Local.X);
        Assert.True(request.Destination.Location.Position.Local.Z > 0f, "the height is the terrain's plus the stop offset, read from the dats");
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal));
        Assert.Equal(0, t.Places);
        Assert.Equal(ScenarioStatus.Failed, status); // the rig's walk does not move the player: the after-check names it
        Assert.Contains("arrival-off-point", run.Failure, StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("an outdoor ground has no portal and no place", StringComparison.Ordinal));
    }

    [Fact]
    public void Winnies_buff_profile_is_the_arena_profiles_lines_all_learned_by_play_and_found_by_the_hosts_rule()
    {
        var winnie = BuffProfiles.For("winnie")!;

        Assert.Same(winnie, BuffProfiles.For("Winnie"));
        Assert.Equal(MageTemplate.ClimberName, winnie.CharacterName);
        Assert.Equal([.. ArenaProfile.Buffs.Lines.Select(l => l.Name), .. ArmourBanes.Names], winnie.Lines.Select(l => l.Name)); // M6 14 (m6-banes): and the six other armour banes, hers alone
        Assert.All(winnie.Lines, l => Assert.True(l.ByPlay, l.Name));
        Assert.Equal(ArenaProfile.Buffs.BuffOrder.Select(l => l.Name).Concat(ArmourBanes.Names).Order(StringComparer.Ordinal), winnie.BuffOrder.Select(l => l.Name).Order(StringComparer.Ordinal)); // M6 7 AK: the same lines in the owner's order; M6 14: with the six banes
        Assert.Equal(["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "Life Magic Mastery Self", "War Magic Mastery Self", "Item Enchantment Mastery Self", "Mana Mastery Self", "Mana Renewal Self", "Rejuvenation Self", "Regeneration Self", "Armor Self", "Bludgeon Protection Self", "Impenetrability", "Bludgeon Bane", "Piercing Bane", "Blade Bane", "Lightning Bane", "Flame Bane", "Frost Bane", "Acid Bane", "Endurance Self", "Strength Self", "Quickness Self", "Coordination Self", "Invulnerability Self"], winnie.BuffOrder.Take(25).Select(l => l.Name)); // M6 14 (m6-banes): the eight armour lines together
        Assert.DoesNotContain(winnie.SpellsToLearn, id => ArenaProfile.SpellsToLearn.Contains(id) && !ArenaProfile.FightExtras.Contains(id)); // no line is taught: only the fight extras
        Assert.Contains(ArenaProfile.Buffs.Lines, l => !l.ByPlay); // the arena profile itself is unchanged: BSD Mage's lines are build-taught
        Assert.Same(ArenaProfile.Buffs, BuffProfiles.For(MageTemplate.CharacterName));
    }

    [Fact]
    public void The_recall_and_the_grant_gate_know_Winnie_and_the_build_gate_stays_BSD_Mage_alone()
    {
        var t = new J3bRig();

        Assert.NotNull(RecallHome.ForCharacter(t.View, "Winnie", t.R.H.Log));
        Assert.Null(RecallHome.ForCharacter(t.View, "Winnie", t.R.H.Log)!.Swap);
        Assert.Null(RecallHome.ForCharacter(t.View, "Holtburg Hunter", t.R.H.Log));
        // The rig's player has no name: neither gate lets it through, and the build gate's text is the old one (a test elsewhere pins "not BSD Mage").
        Assert.Contains("not BSD Mage or Winnie", GearScenario.WrongCharacter(t.View), StringComparison.Ordinal);
        Assert.Contains("not BSD Mage: nothing is sent", GearScenario.WrongBuildCharacter(t.View), StringComparison.Ordinal);
    }

    [Fact]
    public void Slice_check_reports_a_backend_step_as_a_skip_line_and_no_longer_throws()
    {
        var script = File.ReadAllText(RepositoryFiles.PathOf("tools", "slice-check.ps1"));

        Assert.DoesNotContain("needs the backend, which slice-check never starts.\"", script, StringComparison.Ordinal);
        Assert.Contains("backend step ${backend}: slice-check never starts the backend", script, StringComparison.Ordinal);
        Assert.Contains("Write-Stage 'steps' 'SKIP'", script, StringComparison.Ordinal);
        Assert.Contains("$backendSteps", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_on_foot_is_a_profile_fact_so_the_forest_and_the_belt_walk_and_the_dungeons_do_not()
    {
        foreach (var ground in GroundCatalog.All)
        {
            Assert.True((ground.Kind == GroundKind.Outdoor && ground.Portal is null && ground.Outdoor!.Cells.Count > 0) == WayHomeRule.HomeOnFoot(ground), ground.Key);
            Assert.True(WayHomeRule.HomeOnFoot(ground) == (WayHomeRule.FirstCell(ground) is not null), ground.Key);
        }

        Assert.Equal(new Vector3(84f, 7.7f, 94f), GroundCatalog.Get(Belt).Home.LifestonePoint);
    }
}

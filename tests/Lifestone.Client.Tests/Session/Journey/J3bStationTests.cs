using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The travel stations (M4 P5b J3b) over <see cref="J3bRig"/>: the bind and the lifestone tie (the facts set from ACE's texts, the fighters inside a caster swap, the mage in magic mode), the den travel (a Primary Portal Recall that lands inside the den; the untied path's
/// one Ground request, the tie and the portal on foot), the recall home counting recalls, every refusal named, and no station making the mod's <c>place</c>.
/// </summary>
public sealed class J3bStationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static string Id(uint guid) => guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------ bind

    [Fact]
    public void The_mage_binds_then_ties_the_lifestone_and_the_texts_set_the_facts()
    {
        var t = new J3bRig();
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.True(ctx.Facts.Bound);
        Assert.True(ctx.Facts.LifestoneTied);
        Assert.Equal(PortalTie.Unknown, ctx.Facts.PortalTieOf(J3bRig.DenKey));
        Assert.Matches(@"^bound=yes tied=yes swap_s=0\.0 tie_fizzles=0$", run.VerdictFields());

        // On foot to the lifestone as an object goal (the Bind purpose), then the Use, then the tie at it, in peace first and back in peace at the end.
        var request = Assert.Single(t.Travel.Requests);
        Assert.Equal((TravelPurpose.Bind, J3bRig.LifestoneGuid), (request.Purpose, request.ObjectGuid));
        Assert.True(request.Range > 0);
        Assert.Equal(["travel:Bind", "mode:1", "use:" + Id(J3bRig.LifestoneGuid), "mode:8", "cast:2644@" + Id(J3bRig.LifestoneGuid), "mode:1"], t.Events);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.Equal(0, t.Places);
    }

    /// <summary>
    /// M6 6 R1F (live round 1: the level-13 climber's tie fizzled four times and failed the bind): a read book without Lifestone Recall 1635 makes no Lifestone Tie, the one spell it serves. The station is ok on the bind alone
    /// (<c>tied=skipped</c>), sends no cast and no magic mode, leaves the tie fact unset and says why in one line; a fighter skips its caster swap too.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("melee")]
    public void A_book_without_lifestone_recall_skips_the_tie_and_the_station_is_ok_on_the_bind_alone_R1F(string? fighter)
    {
        var t = new J3bRig(fighter is null ? null : FighterTemplate.ForKey(fighter));
        var book = t.R.H.State.Self.Spellbook;
        book.Clear();
        foreach (var spell in new uint[] { BindStation.LifestoneTieSpell, 47, 48 })
        {
            book.Learn(spell);
        }

        book.IsKnown = true;
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^bound=yes tied=skipped swap_s=0\.0 tie_fizzles=0$", run.VerdictFields());
        Assert.True(ctx.Facts.Bound);
        Assert.Null(ctx.Facts.LifestoneTied);
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) || e == "mode:8");
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("tie skipped: the spellbook (3 spells) holds no Lifestone Recall 1635", StringComparison.Ordinal));
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        if (t.SwapRig is { } swap)
        {
            Assert.DoesNotContain(swap.Ops, o => o.StartsWith("wield:", StringComparison.Ordinal)); // no caster swap without a tie
        }
    }

    /// <summary>M6 6 R1F: with Lifestone Recall in the book the tie is cast as before, and a tie that is cast and does not link stays the named failure (<c>tied=no</c>, never <c>skipped</c>).</summary>
    [Fact]
    public void A_book_with_lifestone_recall_still_ties_and_a_failed_tie_stays_named_R1F()
    {
        var t = new J3bRig { TieFizzles = 4 };
        t.R.H.State.Self.Spellbook.IsKnown = true;
        Assert.True(t.R.H.State.Self.Spellbook.Knows(RecallHome.LifestoneRecallSpell));
        var (run, _) = t.RunStation(new BindStation(), t.Context());
        Assert.True(run.Ok, run.Failure + " / " + run.VerdictFields()); // M6 7 BC: a tie that does not link leaves the bind standing, tied=no never fails the station
        Assert.StartsWith("bound=yes tied=no ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Entries, e => e.Message.Contains("Lifestone Tie did not link", StringComparison.Ordinal) && e.Message.Contains("the bind stands", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Entries, e => e.Message.Contains("tie skipped", StringComparison.Ordinal));
    }

    /// <summary>M6 7 BC (live, gate A climb 19: 45 mana held, the tie costs 50): a tie short of mana rests in place until the cost is held, then ties, with a check before and after, both logged.</summary>
    [Fact]
    public void A_tie_short_of_mana_rests_in_place_then_ties_BC()
    {
        var t = new J3bRig();
        t.Caster.Table[BindStation.LifestoneTieSpell] = WorldActionRig.Spell(BindStation.LifestoneTieSpell, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Tie", BaseMana = 50 };
        t.Caster.SetVitals(45, 275);
        t.ManaPerSecond = 0.5;
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.StartsWith("bound=yes tied=yes ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.True(ctx.Facts.LifestoneTied);
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("mana 45 of", StringComparison.Ordinal) && e.Message.Contains("under Lifestone Tie's 50", StringComparison.Ordinal));
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("mana for Lifestone Tie:", StringComparison.Ordinal) && e.Message.Contains(": met (check after)", StringComparison.Ordinal));
        Assert.Contains(t.Events, e => e.StartsWith("cast:2644@", StringComparison.Ordinal));
    }

    /// <summary>M6 7 BC: the mana that never comes back within the limit leaves the bind standing: no cast, tied=no, the station ok, the named TODO in the log, the facts of the bind kept.</summary>
    [Fact]
    public void A_tie_whose_mana_does_not_come_back_leaves_the_bind_standing_BC()
    {
        var t = new J3bRig();
        t.Caster.Table[BindStation.LifestoneTieSpell] = WorldActionRig.Spell(BindStation.LifestoneTieSpell, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Tie", BaseMana = 50 };
        t.Caster.SetVitals(10, 275);
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.StartsWith("bound=yes tied=no ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.True(ctx.Facts.Bound);
        Assert.Null(ctx.Facts.LifestoneTied);
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Entries, e => e.Message.Contains("TODO(m6-bind-tie-mana)", StringComparison.Ordinal) && e.Message.Contains("the bind stands", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Entries, e => e.Message.Contains(": not met (check after)", StringComparison.Ordinal));
    }

    /// <summary>M6 7 BC (live: the recall at 47 mana for a 50 cost, the station's own need being 20% of the pool): the recall rests in place for its cost first, then casts.</summary>
    [Fact]
    public void A_recall_home_short_of_mana_rests_in_place_then_casts_BC()
    {
        var t = new J3bRig();
        t.Caster.Table[RecallHome.LifestoneRecallSpell] = WorldActionRig.Spell(RecallHome.LifestoneRecallSpell, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", BaseMana = 50 };
        t.Caster.SetVitals(47, 275);
        t.ManaPerSecond = 0.5;
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("mana 47 of", StringComparison.Ordinal) && e.Message.Contains("under Lifestone Recall's 50", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 item 14 (m6-recall-waits-arrival; live 2026-11-13: the recovery's recall began 40 ms into a portal space and ended recall:present, three times, a second before she arrived): a recall begun in portal space waits for the
    /// arrival and then casts; one whose arrival never comes ends recall:present after the bounded wait.
    /// </summary>
    [Fact]
    public void A_recall_home_begun_in_portal_space_waits_for_the_arrival_and_then_casts()
    {
        var t = new J3bRig();
        t.R.H.State.Self.Travel = SelfTravel.TeleportArrived;
        var ctx = t.Context();
        var frames = 0;
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx, perFrame: _ =>
        {
            if (++frames == 40)
            {
                t.R.H.State.Self.Travel = SelfTravel.Present; // the arrival
            }
        });

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("recall-home: check before: the character is in TeleportArrived", StringComparison.Ordinal));
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("recall-home: check after: travel Present after", StringComparison.Ordinal));

        var never = new J3bRig();
        never.R.H.State.Self.Travel = SelfTravel.TeleportArrived;
        var (stuck, failed) = never.RunStation(new RecallHomeStation(), never.Context());
        Assert.Equal(ScenarioStatus.Failed, failed);
        Assert.Contains("present", stuck.Failure, StringComparison.Ordinal);
        Assert.Single(never.R.H.Log.Entries, e => e.Message.Contains("still not present", StringComparison.Ordinal));
    }

    /// <summary>M6 7 BD: the recall's rest for its mana says it waits (the autopilot's one rule reads it), and the cast does not.</summary>
    [Fact]
    public void A_recall_homes_rest_for_its_mana_is_a_wait_by_name_and_the_cast_is_not()
    {
        var t = new J3bRig();
        t.Caster.Table[RecallHome.LifestoneRecallSpell] = WorldActionRig.Spell(RecallHome.LifestoneRecallSpell, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", BaseMana = 50 };
        t.Caster.SetVitals(47, 275);
        t.ManaPerSecond = 0.5;
        var waits = new HashSet<string>(StringComparer.Ordinal);
        var unwaited = 0;
        var (run, status) = t.RunStation(new RecallHomeStation(), t.Context(), perFrame: r =>
        {
            if (((IStationWait)r).Waiting is { } wait)
            {
                waits.Add(wait);
            }
            else
            {
                unwaited++;
            }
        });

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal("the rest in place for Lifestone Recall's mana", Assert.Single(waits));
        Assert.True(unwaited > 0);
        Assert.Null(((IStationWait)run).Waiting);
    }

    [Fact]
    public void Bind_walks_to_the_lifestones_known_point_first_when_it_is_not_in_view_and_logs_the_fallback_once_F2()
    {
        var t = new J3bRig(lifestoneInView: false); // the Archer started by Thelnoth's shop: the lifestone appears only after the walk
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(2, t.Travel.Requests.Count);
        Assert.Equal((TravelPurpose.Bind, 0u), (t.Travel.Requests[0].Purpose, t.Travel.Requests[0].ObjectGuid)); // the point goal
        Assert.Equal(0xA9B40019u, t.Travel.Requests[0].Destination.Location.Position.CellId);
        Assert.Equal((TravelPurpose.Bind, J3bRig.LifestoneGuid), (t.Travel.Requests[1].Purpose, t.Travel.Requests[1].ObjectGuid)); // then the object goal as before
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("fallback walk to its known point", StringComparison.Ordinal));
        Assert.True(ctx.Facts.Bound);
        Assert.Equal(0, t.Places);
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_casts_the_tie_inside_a_caster_swap_and_ends_with_its_weapon_in_hand(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var t = new J3bRig(template);
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.True(ctx.Facts.Bound && ctx.Facts.LifestoneTied == true);
        Assert.Matches(@"^bound=yes tied=yes swap_s=\d+\.\d tie_fizzles=0$", run.VerdictFields());
        Assert.DoesNotContain("swap_s=0.0", run.VerdictFields());

        // The order: the Use, then the swap in (the caster wielded, the magic mode), the cast, the swap out (peace, the weapon back).
        var ops = t.SwapRig!.Ops;
        var cast = t.Events.FindIndex(e => e.StartsWith("cast:2644@", StringComparison.Ordinal));
        Assert.True(ops.IndexOf("wield:The Healer's Heart") >= 0 && cast > t.Events.IndexOf("use:" + Id(J3bRig.LifestoneGuid)), string.Join(" > ", t.Events) + " / " + string.Join(" > ", ops));
        Assert.True(t.Events.IndexOf("mode:8") is var magic && magic >= 0 && magic < cast && t.Events.LastIndexOf("mode:1") > cast);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.NotEqual(0u, t.SwapRig.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0u, t.SwapRig.LocationOf(C1bSwapRig.WandGuid));
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void A_fizzled_tie_is_cast_again_and_counted_but_a_tie_refused_for_its_components_is_named_and_leaves_the_fact_unset()
    {
        var fizzle = new J3bRig { TieFizzles = 1 };
        var ctx = fizzle.Context();
        var (run, status) = fizzle.RunStation(new BindStation(), ctx);
        Assert.True(status == ScenarioStatus.Succeeded, run.Failure + " / " + run.VerdictFields());
        Assert.True(ctx.Facts.LifestoneTied);
        Assert.EndsWith("tie_fizzles=1", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(2, fizzle.Events.Count(e => e.StartsWith("cast:2644@", StringComparison.Ordinal)));

        var refused = new J3bRig { TieRefusal = 0x0400 };
        var refusedCtx = refused.Context();
        var (refusedRun, _) = refused.RunStation(new BindStation(), refusedCtx);
        Assert.True(refusedRun.Ok, refusedRun.Failure + " / " + refusedRun.VerdictFields()); // M6 7 BC: named in the log, the bind stands
        Assert.True(refusedCtx.Facts.Bound);
        Assert.Null(refusedCtx.Facts.LifestoneTied);
        Assert.StartsWith("bound=yes tied=no ", refusedRun.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(refused.R.H.Log.Entries, e => e.Message.Contains("YouDontHaveAllTheComponents 0x0400", StringComparison.Ordinal) && e.Message.Contains("TODO(m6-bind-tie-mana)", StringComparison.Ordinal));
        Assert.Equal(0, refused.Places);
    }

    [Fact]
    public void A_use_that_does_not_attune_the_spirit_is_a_failed_check_and_a_failed_walk_ends_the_station_with_the_seams_reason()
    {
        var silent = new J3bRig { UseSilent = true };
        var ctx = silent.Context();
        var (run, _) = silent.RunStation(new BindStation(), ctx);
        Assert.False(run.Ok);
        Assert.False(ctx.Facts.Bound);
        Assert.StartsWith("bound=no tied=yes ", run.VerdictFields(), StringComparison.Ordinal);

        var stuck = new J3bRig();
        stuck.Travel.FailWith = "travel:run:blocked";
        var (walkRun, walkStatus) = stuck.RunStation(new BindStation(), stuck.Context());
        Assert.Equal(ScenarioStatus.Failed, walkStatus);
        Assert.Contains("travel:run:blocked", walkRun.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(stuck.Events, e => e.StartsWith("use:", StringComparison.Ordinal)); // nothing was used or cast
        Assert.Equal(0, stuck.Places);
    }

    /// <summary>
    /// M6 6 FXD1 (6-D5): the bind records where it bound (<see cref="JourneyFacts.BoundAt"/>, the used lifestone's cell and point, ACE's Sanctuary) at the Use that attuned the spirit, whatever its town; a Use that did not attune records
    /// nothing, and the tie never sets it (a tie does not change where death sends one).
    /// </summary>
    [Fact]
    public void M6_6_FXD1_the_bind_records_the_lifestone_it_bound_at_and_a_use_that_did_not_attune_records_nothing()
    {
        var t = new J3bRig();
        var ctx = t.Context();
        Assert.Null(ctx.Facts.BoundAt);
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.True(t.R.H.State.Objects.TryGet(J3bRig.LifestoneGuid, out var stone) && stone.Position is not null);
        var at = Assert.NotNull(ctx.Facts.BoundAt);
        Assert.Equal(stone.Position!.Value, at);

        var silent = new J3bRig { UseSilent = true };
        var silentCtx = silent.Context();
        _ = silent.RunStation(new BindStation(), silentCtx);
        Assert.False(silentCtx.Facts.Bound);
        Assert.Null(silentCtx.Facts.BoundAt);
    }

    // ------------------------------------------------------------------------------------------------ den travel

    [Fact]
    public void A_den_recall_that_lands_moves_the_one_tie_slot_and_unties_the_other_grounds()
    {
        var t = new J3bRig();
        var ctx = t.Context(f =>
        {
            f.SetPortalTie(J3bRig.DenKey, PortalTie.Tied);
            f.SetPortalTie("tie-slot-other", PortalTie.Tied); // M6 4 R2 (F1): a stale record of another ground the recall must not leave tied
        });
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(J3bRig.DenKey, ctx.Facts.PrimaryPortalSlot);
        Assert.Equal(PortalTie.Tied, ctx.Facts.PortalTieOf(J3bRig.DenKey));
        Assert.Equal(PortalTie.Untied, ctx.Facts.PortalTieOf("tie-slot-other"));
    }

    [Fact]
    public void A_tied_mage_recalls_into_the_den_with_one_cast_one_recall_and_no_place_and_no_walk()
    {
        var t = new J3bRig();
        var ctx = t.Context(f => f.SetPortalTie(J3bRig.DenKey, PortalTie.Tied));
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(["cast:48", "teleport:" + Id(J3bRig.DenCell)], t.Events.Where(e => e.StartsWith("cast:", StringComparison.Ordinal) || e.StartsWith("teleport:", StringComparison.Ordinal)));
        Assert.Empty(t.Travel.Requests);
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.Equal(PortalTie.Tied, ctx.Facts.PortalTieOf(J3bRig.DenKey));
        Assert.Equal(0, ctx.Facts.Teleports);
        Assert.Equal(0x0105u, t.R.H.State.Self.Position!.Value.CellId >> 16); // inside the den
        Assert.Equal(SelfTravel.Present, t.R.H.State.Self.Travel);
        Assert.Matches(@"^den=recall portal_tied=yes recalls=1 den_s=\d+\.\d$", run.VerdictFields());
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void An_untied_mage_is_refused_0x04A3_then_makes_the_one_ground_request_ties_the_portal_and_walks_into_it()
    {
        var t = new J3bRig { PortalTied = false };
        var ctx = t.Context(); // the facts do not know yet (null): the probing recall is cast first
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        var order = t.Events.Where(e => !e.StartsWith("mode:", StringComparison.Ordinal)).ToList();
        Assert.Equal(["cast:48", "travel:Ground", "cast:47@" + Id(J3bRig.PortalGuid), "travel:Seek", "use:" + Id(J3bRig.PortalGuid), "teleport:" + Id(J3bRig.DenCell)], order);

        // The one budgeted request: Ground to the portal's front; the portal itself is an object goal on foot.
        Assert.Equal(2, t.Travel.Requests.Count);
        var ground = t.Travel.Requests[0];
        Assert.Equal(TravelPurpose.Ground, ground.Purpose);
        Assert.Equal(0u, ground.ObjectGuid);
        var portal = ctx.Dungeon.Portal!;
        Assert.Equal(portal.FrontCell, ground.Destination.Location.Position.CellId);
        Assert.Equal(portal.Front, ground.Destination.Location.Position.Local);
        Assert.Equal((0x1B90002Cu, new Vector3(122.40f, 84.43f, 30.01f)), (portal.FrontCell, portal.Front)); // the default (the den) ground: W0's measured stand
        var seek = t.Travel.Requests[1];
        Assert.Equal((TravelPurpose.Seek, J3bRig.PortalGuid), (seek.Purpose, seek.ObjectGuid));

        Assert.Equal(PortalTie.Tied, ctx.Facts.PortalTieOf(J3bRig.DenKey)); // false after the 0x04A3, true after the tie
        Assert.Equal(0, ctx.Facts.Recalls); // the portal walk is no recall
        Assert.Equal(0x0105u, t.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Matches(@"^den=portal portal_tied=yes recalls=0 den_s=\d+\.\d$", run.VerdictFields());
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void When_the_facts_already_say_the_portal_is_untied_no_recall_is_probed()
    {
        var t = new J3bRig { PortalTied = false };
        var ctx = t.Context(f => f.SetPortalTie(J3bRig.DenKey, PortalTie.Untied));
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.DoesNotContain("cast:48", t.Events);
        Assert.Contains("cast:47@" + Id(J3bRig.PortalGuid), t.Events);
        Assert.Equal(PortalTie.Tied, ctx.Facts.PortalTieOf(J3bRig.DenKey));
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void A_portal_recall_refused_for_any_other_reason_fails_the_station_naming_ACEs_refusal_and_leaves_the_facts_alone()
    {
        var t = new J3bRig { PortalRecallRefusal = 0x04AD };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Contains("YouCannotRecallPortal 0x04AD", run.Failure, StringComparison.Ordinal);
        Assert.Equal(PortalTie.Unknown, ctx.Facts.PortalTieOf(J3bRig.DenKey));
        Assert.Equal(0, ctx.Facts.Recalls);
        Assert.Empty(t.Travel.Requests);
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void The_hero_den_travel_steps_off_the_lifestone_before_the_portal_recall_and_the_gates_does_not()
    {
        // The M4 hero video: the bind leaves the mage in the lifestone's use range, where P5b J7 filmed the recall's cast through the crystal.
        var t = new J3bRig { RecallIntoLifestone = true };
        var ctx = t.Context();
        var (home, _) = t.RunStation(new RecallHomeStation(), ctx); // the mage in Holtburg, in its lifestone (the rig's walks do not move it)
        Assert.True(home.Ok, home.Failure);
        t.Travel.Requests.Clear();
        t.Events.Clear();

        var (run, status) = t.RunStation(new DenTravelStation(stepOffLifestone: true), ctx);
        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^den=recall portal_tied=yes recalls=1 den_s=\d+\.\d stepped_off=yes$", run.VerdictFields());
        var walk = Assert.Single(t.Travel.Requests);
        Assert.Equal((TravelPurpose.Bind, "home-lifestone-point", 0u), (walk.Purpose, walk.Destination.Name, walk.ObjectGuid));
        Assert.True(t.Events.IndexOf("travel:Bind") < t.Events.IndexOf("cast:48"), string.Join(" > ", t.Events));
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("stepping off it before Primary Portal Recall's cast", StringComparison.Ordinal));

        // The plan: only the hero mage's den-travel steps off; the gate's keeps its fields.
        Assert.Equal("1", JourneyPlans.HeroMage.Stations.Single(s => s.Kind == StationKinds.DenTravel).Arguments[JourneyPlans.StepOffArgument]);
        Assert.False(JourneyPlans.Mage.Stations.Single(s => s.Kind == StationKinds.DenTravel).Arguments.ContainsKey(JourneyPlans.StepOffArgument));
        Assert.False(new DenTravelStation().StepOff);
    }

    [Fact]
    public void A_hero_den_travel_with_no_lifestone_near_walks_nothing_and_says_so()
    {
        var t = new J3bRig(); // the Academy's lifestone stands 8 m from the mage
        var (run, status) = t.RunStation(new DenTravelStation(stepOffLifestone: true), t.Context());

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^den=recall portal_tied=yes recalls=1 den_s=\d+\.\d stepped_off=no$", run.VerdictFields());
        Assert.Empty(t.Travel.Requests);
    }

    // ------------------------------------------------------------------------------------------------ recall home

    [Fact]
    public void The_mage_recalls_home_alone_and_the_recall_is_counted_and_says_the_lifestone_is_tied()
    {
        var t = new J3bRig();
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.True(ctx.Facts.LifestoneTied);
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=0 stepped_off=no$", run.VerdictFields());
        Assert.Equal(0xA9B4u, t.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Empty(t.Travel.Requests);
        Assert.Equal(0, t.Places);

        // A second station on the same journey adds a second recall.
        var (again, _) = t.RunStation(new RecallHomeStation(), ctx);
        Assert.True(again.Ok, again.Failure);
        Assert.Equal(2, ctx.Facts.Recalls);
    }

    [Fact]
    public void A_recall_that_arrives_inside_the_lifestone_steps_off_it_to_the_lifestones_known_point_before_the_station_ends()
    {
        // M4 4.2 FS (live gate round 1): ACE recalls to the lifestone's own location and the client's body stayed inside it (no host kept a physics object for it), so a character that logged off there
        // was created inside it on its next login and ACE's placement moved it 2.2 m. This rig has no dats and no body view: the station cannot measure the body against the stone, so the server's
        // position stands in for it and the named fallback walks (M5 item 5 l1 keeps it for that case; the dat tests below measure).
        var t = new J3bRig { RecallIntoLifestone = true };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=0 stepped_off=yes$", run.VerdictFields());
        var walk = Assert.Single(t.Travel.Requests);
        Assert.Equal(TravelPurpose.Bind, walk.Purpose);
        Assert.Equal("home-lifestone-point", walk.Destination.Name);
        Assert.Equal(0u, walk.ObjectGuid);
        Assert.Equal(0xA9B40019u, walk.Destination.Location.Position.CellId);
        Assert.Equal(0, t.Places);
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("stepping off it before the station ends", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_recall_whose_arrival_the_body_already_slid_out_of_the_lifestone_walks_nothing_and_says_stepped_off_no()
    {
        // M5 item 5 l1: the lifestone is a physics object in the body's world, so the arrival's sliding placement leaves the body 2.22 m north of the stone (ACE's own spot,
        // LifestonePlacementTests); the stone's own sphere (centre 1.5 m up, radius 1.5 m) and the body's sphere do not touch, so the step-off does not run.
        var t = new J3bRig { RecallIntoLifestone = true, Dats = fixture.Dats, Body = J3bRig.BodyNorthOfArrival(2.22f) };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=0 stepped_off=no$", run.VerdictFields());
        Assert.Empty(t.Travel.Requests);
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("the body stands clear of lifestone", StringComparison.Ordinal) && l.Contains("outside the stone's collision", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("stepping off it", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_recall_whose_body_is_still_inside_the_lifestone_steps_off_it_as_the_named_fallback_and_says_stepped_off_yes()
    {
        // The body is where the server put the player: the stone's own centre, inside its sphere.
        var t = new J3bRig { RecallIntoLifestone = true, Dats = fixture.Dats, Body = J3bRig.BodyNorthOfArrival(0f) };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=0 stepped_off=yes$", run.VerdictFields());
        var walk = Assert.Single(t.Travel.Requests);
        Assert.Equal("home-lifestone-point", walk.Destination.Name);
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("the body is", StringComparison.Ordinal) && l.Contains("inside the stone's collision", StringComparison.Ordinal) && l.Contains("stepping off it", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_host_that_cannot_say_where_the_body_stands_leaves_the_server_position_standing_in_for_it_and_the_step_off_runs_named()
    {
        // A host with no body view (a body not yet placed, or a scripted host): the server's position (the stone's centre after a recall) is the body.
        var t = new J3bRig { RecallIntoLifestone = true, Dats = fixture.Dats };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.EndsWith("stepped_off=yes", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("the host cannot say where the body stands", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("TODO(m5-godot-body-position)", StringComparison.Ordinal)); // closed by M5 item 5 c1
    }

    [RequiresDatsTheory]
    [InlineData(2.22f, "no", false)]
    [InlineData(0f, "yes", true)]
    public void A_host_that_answers_the_body_alone_the_Godot_hosts_shape_has_the_station_read_that_body_not_the_servers_position(float metresNorth, string steppedOff, bool walks)
    {
        // M5 item 5 c1 (closes TODO(m5-godot-body-position)): the Godot journey host has no hunt follower but answers BodyPosition from the player's walker. The server's position is the stone's centre either way
        // (RecallIntoLifestone), so a body that slid 2.22 m out reads stepped_off=no (the old server-position stand-in would have walked), and a body still on the stone walks.
        var t = new J3bRig { RecallIntoLifestone = true, Dats = fixture.Dats, Body = J3bRig.BodyNorthOfArrival(metresNorth), BodyFromWalker = true };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.EndsWith("stepped_off=" + steppedOff, run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(walks ? 1 : 0, t.Travel.Requests.Count);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("the host cannot say where the body stands", StringComparison.Ordinal));
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("the body is", StringComparison.Ordinal) && l.Contains(walks ? "inside the stone's collision" : "outside the stone's collision", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_recalls_home_through_the_caster_swap_and_ends_in_peace_with_its_weapon(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var t = new J3bRig(template);
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.Contains("cast:1635", t.Events);
        Assert.Contains("wield:The Healer's Heart", t.SwapRig!.Ops);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.NotEqual(0u, t.SwapRig.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void A_character_that_is_not_tied_to_a_lifestone_fails_the_recall_named_0x049E_and_the_fact_goes_false()
    {
        var t = new J3bRig { Bound = false };
        var ctx = t.Context(f => f.LifestoneTied = true);
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Contains("recall:cast", run.Failure, StringComparison.Ordinal);
        Assert.Contains("YouMustLinkToLifestoneToRecall 0x049E", run.Failure, StringComparison.Ordinal);
        Assert.False(ctx.Facts.LifestoneTied);
        Assert.Equal(0, ctx.Facts.Recalls);
        Assert.Contains("recall_why=recall:cast", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void The_stations_are_public_sealed_with_plain_constructors_and_the_catalogs_names()
    {
        foreach (var (station, name) in new (IStation, string)[] { (new BindStation(), "bind"), (new DenTravelStation(), "den-travel"), (new RecallHomeStation(), "recall-home"), (new BuildVerifyStation(), "build-verify") })
        {
            Assert.Equal(name, station.Name);
            Assert.True(station.GetType().IsPublic && station.GetType().IsSealed, station.GetType().Name);
        }

        Assert.Equal(StationWhere.Holtburg, new BindStation().Needs.Where);
        Assert.Equal(StationWhere.Holtburg, new BuildVerifyStation().Needs.Where);
    }
}

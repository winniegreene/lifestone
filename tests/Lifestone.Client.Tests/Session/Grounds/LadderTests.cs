using System.Text.Json.Nodes;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The ladder as data (M6 DoD 1, slice a1): the war mage's ladder loads from its embedded file; its rungs run in order with contiguous bands from 1 to 126; <see cref="Ladder.RungFor"/>
/// answers at every band edge and fails loud outside; every pending rung names the item that owns its profile; a missing or unknown field fails naming the file and the JSON path; every
/// non-pending ground resolves through <see cref="GroundCatalog"/>, and a pending ground that resolves is an error (a stale pending).
/// </summary>
public sealed class LadderTests
{
    private const string File = LadderLoader.WarMageKey + ".json";

    [Fact]
    public void TheWarMagesLadderLoadsFromItsEmbeddedFile()
    {
        var ladder = LadderLoader.WarMage;
        Assert.Equal("war-mage", ladder.Key);
        Assert.Equal(126, ladder.TopLevel);
        Assert.Equal(
            new[] { "academy-and-chest", "outskirts", "burrow", "lodge", "cave", "abode", "quarters", "holding" }, // M6 14 (the owner, 2026-11-13): the Temple 20-40 left the ladder
            ladder.Rungs.Select(r => r.Key));
        Assert.Same(ladder, LadderLoader.Get(LadderLoader.WarMageKey)); // loaded once
        Assert.Throws<KeyNotFoundException>(() => LadderLoader.Get("no-such-ladder"));
        Assert.All(Ladder.SourceNames, s => Assert.False(string.IsNullOrWhiteSpace(ladder.Sources[s])));
        Assert.All(ladder.Rungs, r => Assert.All(Ladder.RungSourceNames, s => Assert.False(string.IsNullOrWhiteSpace(r.Sources[s]), $"{r.Key}: sources.{s}")));
    }

    [Fact]
    public void TheBandsRunInOrderWithoutAGapFromOneTo126()
    {
        var rungs = LadderLoader.WarMage.Rungs;
        Assert.Equal(1, rungs[0].Band.From);
        for (var i = 1; i < rungs.Count; i++)
        {
            Assert.Equal(rungs[i - 1].Band.To + 1, rungs[i].Band.From);
        }

        Assert.Equal(126, rungs[^1].Band.To);
        Assert.All(rungs, r => Assert.InRange(r.MoveOn.Level, r.Band.From + 1, Math.Min(r.Band.To + 1, 126)));
    }

    [Theory]
    [InlineData(1, "academy-and-chest")]
    [InlineData(4, "academy-and-chest")]
    [InlineData(5, "outskirts")]
    [InlineData(9, "outskirts")] // M6 14 (D135, the owner, 2026-11-13): the island from 10; the hold outside until 16 of 2026-11-06 is reversed
    [InlineData(10, "burrow")] // by the Refuge's walk: the Town Network's portals admit her from 10, the Facility Hub's tusker portals from 15
    [InlineData(14, "burrow")]
    [InlineData(15, "lodge")] // the hub's portals
    [InlineData(20, "lodge")]
    [InlineData(21, "cave")]
    [InlineData(24, "cave")]
    [InlineData(25, "abode")] // the Abode from 25 on Flame Bolt V under the buff bot's lines
    [InlineData(41, "abode")]
    [InlineData(42, "quarters")] // the Quarters from 42 (the trial at 50: 4.55 M XP/h against the Abode's 3.35 to 3.49)
    [InlineData(64, "quarters")]
    [InlineData(65, "holding")]
    [InlineData(126, "holding")]
    public void RungForAnswersAtEveryBandEdge(int level, string rung) => Assert.Equal(rung, LadderLoader.WarMage.RungFor(level).Key);

    /// <summary>M6 3 F1 (review 3-R2): a rung's band lies inside its ground's entry gate, for the ground and for every live alternate (a ground with no entry admits every level): ACE refuses a player above a portal's MaxLevel.</summary>
    /// <summary>The (rung, ground) pairs of the war mage's ladder: each non-pending ground rung's ground and its live alternates.</summary>
    public static TheoryData<string, string> LiveRungGrounds()
    {
        var data = new TheoryData<string, string>();
        foreach (var rung in LadderLoader.WarMage.Rungs.Where(r => !r.Pending && r.GroundKey is not null))
        {
            data.Add(rung.Key, rung.GroundKey!);
            foreach (var alternate in rung.Alternates.Where(a => !a.Pending))
            {
                data.Add(rung.Key, alternate.GroundKey);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LiveRungGrounds))]
    public void Every_rungs_band_is_admitted_by_its_ground_and_every_live_alternate(string rungKey, string groundKey)
    {
        var rung = LadderLoader.WarMage.Rungs.Single(r => r.Key == rungKey);
        var ground = GroundCatalog.Get(groundKey);
        for (var level = rung.Band.From; level <= rung.Band.To; level++)
        {
            Assert.True(ground.AdmitsLevel(level), $"rung {rung.Key} band {rung.Band.From}-{rung.Band.To}: {groundKey} refuses level {level}");
        }
    }

    /// <summary>
    /// M6 14 (the owner, 2026-11-13): the Temple 20-40 is no rung and no alternate of the ladder; the Abode holds its band by the island road. The ground's profile stays (its portal still admits 20 to 40), and the Temple's
    /// pending alternate, the Hills Citadel, moved to the Abode's rung. D135 (the owner, 2026-11-13, the same day): the Cave holds 21 to 24 and moves on at 25, the Abode 25 to 41.
    /// </summary>
    [Fact]
    public void The_Temple_20_40_is_no_rung_the_Cave_runs_to_24_and_25_is_the_Abode()
    {
        var ladder = LadderLoader.WarMage;
        Assert.DoesNotContain(ladder.Rungs, r => r.Key == "temple-20-40" || r.GroundKey == "tusker-temple-20-40" || r.Alternates.Any(a => a.GroundKey == "tusker-temple-20-40"));
        Assert.True(GroundCatalog.Get("tusker-temple-20-40").AdmitsLevel(40)); // the ground stays: only the rung left
        Assert.False(GroundCatalog.Get("tusker-temple-20-40").AdmitsLevel(41)); // the portal's MaxLevel 40 (ACE Portal.cs:172-176 @95ef426d)
        Assert.Equal(("cave", 21, 24, 25), (ladder.RungFor(24).Key, ladder.RungFor(24).Band.From, ladder.RungFor(24).Band.To, ladder.RungFor(24).MoveOn.Level));
        Assert.Equal(("abode", 25, 41, 42), (ladder.RungFor(25).Key, ladder.RungFor(25).Band.From, ladder.RungFor(25).Band.To, ladder.RungFor(25).MoveOn.Level));
        Assert.Equal("town-network:oolutanga-refuge", ladder.RungFor(32).Route); // the island road, not the walk from New Colier
        Assert.DoesNotContain(ladder.Rungs, r => r.Route.StartsWith("walk:new-colier", StringComparison.Ordinal));
        Assert.Equal(["tusker-habitat", "hills-citadel"], ladder.RungFor(40).Alternates.Select(a => a.GroundKey));
        Assert.Contains("the owner, 2026-11-13", ladder.RungFor(40).Sources["band"], StringComparison.Ordinal);
        Assert.Contains("D135", ladder.RungFor(24).Sources["band"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(127)]
    public void RungForOutsideTheLadderFailsLoud(int level)
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => LadderLoader.WarMage.RungFor(level));
        Assert.Contains("war-mage", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPendingRungAndAlternateNamesTheItemThatOwnsItsProfile()
    {
        var ladder = LadderLoader.WarMage;
        var pending = ladder.Rungs.Where(r => r.Pending).Select(r => r.Key).ToArray();
        Assert.Empty(pending); // M6 3 p2: every spine rung has its profile
        Assert.All(ladder.Rungs.Where(r => r.Pending), r => Assert.StartsWith("profile due in item 3", r.PendingReason, StringComparison.Ordinal));
        Assert.All(ladder.Rungs.Where(r => !r.Pending), r => Assert.Null(r.PendingReason));
        Assert.All(ladder.Rungs.SelectMany(r => r.Alternates), a => Assert.Equal(a.Pending, a.PendingReason is not null));
        Assert.All(ladder.Rungs.SelectMany(r => r.Alternates).Where(a => a.Pending), a => Assert.Matches(@"^profile due in item \d+", a.PendingReason!));
        // M6 3 end: Hills Citadel (D13) is the one pending alternate, placed in item 10 for the owner's answer (TODO(m6-hills-citadel))
        Assert.Equal(new[] { "hills-citadel" }, ladder.Rungs.SelectMany(r => r.Alternates).Where(a => a.Pending).Select(a => a.GroundKey).ToArray());
        Assert.Equal("abode", ladder.Rungs.Single(r => r.Alternates.Any(a => a.Pending)).Key); // M6 14: moved with the Temple 20-40's rung (the 40-55 band's alternation, D13)
        Assert.Contains("TODO(m6-outskirts-profile)", ladder.RungFor(5).Sources["ground"], StringComparison.Ordinal);
        Assert.Contains("TODO(m6-ladder-move-on)", ladder.Sources["move_on_rule"], StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNonPendingGroundResolvesAndIsCoveredByTheDatTests()
    {
        var ladder = LadderLoader.WarMage;
        var keys = ladder.Rungs.Where(r => r.Kind == LadderRungKind.Ground && !r.Pending).Select(r => r.GroundKey!)
            .Concat(ladder.Rungs.SelectMany(r => r.Alternates).Where(a => !a.Pending).Select(a => a.GroundKey))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "holtburg-redoubt", "tusker-retreat", "tusker-lodge", "tusker-cave", "tusker-abode", "tusker-quarters", "tusker-holding", "holtburg-outskirts", "tusker-burrow", "tusker-cavern", "tusker-habitat", "tusker-temple-fort-tethana", "tusker-barracks", "tusker-armory", "black-spawn-den-aluvian" }, keys); // M6 3 p2 and p3: every rung and the Cave, Abode and Quarters rungs' alternates are live (the rungs' alternates follow the rungs in the list); M6 14 armory: the Holding rung's first alternate; M6 redoubt: the rung outskirts' own ground is the Redoubt and its alternate the outdoor ground, its fallback

        // HuntingGroundDatTests iterates every dungeon of the catalog and the named outdoor keys (the belt, the forest and, since M6 item 0, the outskirts); each ground the ladder resolves is one of them (DoD 1: the dat checks over every rung).
        var outdoor = ((IEnumerable<object[]>)HuntingGroundDatTests.OutdoorKeys()).Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);
        Assert.All(keys, k =>
        {
            var ground = GroundCatalog.Get(k);
            Assert.True(ground.Kind == GroundKind.Dungeon || outdoor.Contains(k), $"{k} is not covered by HuntingGroundDatTests");
        });
    }

    [Fact]
    public void QuestAndGroundRungsCarryTheirOwnFields()
    {
        var ladder = LadderLoader.WarMage;
        var academy = ladder.RungFor(1);
        Assert.Equal(LadderRungKind.Quest, academy.Kind);
        Assert.Equal(new[] { "academy-skip", "pathwarden" }, academy.QuestKeys);
        Assert.Null(academy.GroundKey);
        Assert.Equal(new LadderMoveOn(5, null, null), academy.MoveOn);
        Assert.Equal("start:character-creation", academy.Route);

        Assert.All(ladder.Rungs.Where(r => r.Kind == LadderRungKind.Ground), r =>
        {
            Assert.NotNull(r.GroundKey);
            Assert.Empty(r.QuestKeys);
            Assert.NotNull(r.MoveOn.KillsAnHourFloor);
            Assert.NotNull(r.MoveOn.CastsAKillCeiling);
            Assert.Equal("fire", r.Element);
            Assert.InRange(r.SpellLevel.Min, 1, r.SpellLevel.Max);
            Assert.Contains(r.Route[..r.Route.IndexOf(':', StringComparison.Ordinal)], Ladder.RouteKinds);
        });

        // M6 0 F5 (review 0-R5, D26): the outskirts' margin is the rung's data (5, with its reason in sources); a quest rung and a pending ground rung carry none yet.
        Assert.Equal(5, ladder.RungFor(5).TargetMargin);
        Assert.Contains("smallest round margin", ladder.RungFor(5).Sources["target_margin"], StringComparison.Ordinal);
        Assert.Null(academy.TargetMargin);
        Assert.All(ladder.Rungs.Where(r => r.Pending || r.Kind == LadderRungKind.Quest), r => Assert.Null(r.TargetMargin));
        Assert.All(ladder.Rungs.Where(r => r.Kind == LadderRungKind.Ground && !r.Pending), r => Assert.NotNull(r.TargetMargin));

        var holding = ladder.RungFor(100);
        Assert.Equal((ushort)0x5B48, holding.Landblock);
        Assert.Equal(new LadderSpellLevel(5, 7), holding.SpellLevel); // M6 14 (D135): from 65, base 290: V

        // M6 1 a (D4, D41): the spell levels are computed by the fizzle rule (SpellPlanTests pins every rung against the computation); the plan's chance is the ladder's value with its source. (M6 7 AC: the outskirts run to level 14 (I-II) and the dropped Burrow rung (II-II) is no row)
        Assert.Equal(
            new[] { (1, 1), (1, 2), (2, 2), (2, 3), (3, 3), (3, 4), (5, 5), (5, 7) }, // M6 14 (D135): the study's bands (the Burrow 10-14 on 110 to 133, the Lodge from 15 on 139, the Cave 21-24, the Abode 25-41 on 185 to 241, the Quarters 42-64 on 245 to 288, the Holding from 65 on 290). Before it, M6 14 (2026-11-13): the Temple 20-40's IV-IV left with its rung; the Cave to 39 (base 234: IV), the Abode from 40 (237: IV). M6 item 14: the Burrow is III (144 at level 16 on the climber's split; it was II on 141). M6 7 AY: the Lodge starts at II: the pack's want
            ladder.Rungs.Select(r => (r.SpellLevel.Min, r.SpellLevel.Max)));
        Assert.Equal(0.95, ladder.PlanCastChance);
        Assert.Contains("D41", ladder.Sources["plan_cast_chance"], StringComparison.Ordinal);
        Assert.Equal((2.0, 5), (ladder.HealthHits, ladder.HealthPayPercent));
        Assert.Equal(new Lifestone.Client.Session.Mage.StatCap(5, 10, 277), ladder.StatCap); // M6 7 finish (the owner, 2026-11-08): each stat aims at 5%, none passes 10%
        Assert.True(ladder.Sources.ContainsKey("stat_cap")); // M6 7 R: named ladder values, their reasons in the sources
        Assert.Contains("the hit that lands while a heal winds up", ladder.Sources["health_hits"], StringComparison.Ordinal);
        Assert.Contains("the owner's decision of 2026-11-08", ladder.Sources["health_pay_percent"], StringComparison.Ordinal);
        Assert.All(ladder.Rungs.Where(r => r.Key is not ("outskirts" or "burrow")), r => Assert.Null(r.Health?.RoadHitter)); // M6 7 AC, AR: only the outskirts and the Burrow name one
        Assert.Equal("Goldenback Tusker", ladder.RungFor(9).Health!.RoadHitter!.Name); // M6 14 (D135): the Burrow at 10 by the Refuge's walk: that road's hardest hitter, as in M6 7 R
        Assert.Equal("Goldenback Tusker", ladder.RungFor(10).Health!.RoadHitter!.Name);
        Assert.All(ladder.Rungs.Where(r => r.Kind == LadderRungKind.Quest), r => Assert.Null(r.Health));
        Assert.Null(ladder.RungFor(75).Health); // the Holding: its reason in sources.health
        Assert.Contains("out-hits any health the XpTable can sell", ladder.RungFor(75).Sources["health"], StringComparison.Ordinal);
        Assert.All(ladder.Rungs.Where(r => r.Health is not null), r => Assert.Contains("SELECT", r.Sources["health"], StringComparison.Ordinal)); // D28: each hitter's row
        Assert.Contains("43 over the power is 0.9530", ladder.Sources["spell_level"], StringComparison.Ordinal);
        Assert.Contains("tusker-temple-fort-tethana", holding.Alternates.Select(a => a.GroundKey));
    }

    [Theory]
    [InlineData("top_level", "$.top_level")]
    [InlineData("plan_cast_chance", "$.plan_cast_chance")]
    [InlineData("sources.plan_cast_chance", "$.sources.plan_cast_chance")]
    [InlineData("sources", "$.sources")]
    [InlineData("rungs[2].move_on", "$.rungs[2].move_on")]
    [InlineData("rungs[2].route", "$.rungs[2].route")]
    [InlineData("rungs[3].pending_reason", "$.rungs[3].pending_reason")]
    [InlineData("rungs[4].alternates[0].landblock", "$.rungs[4].alternates[0].landblock")]
    [InlineData("rungs[5].move_on.casts_a_kill_ceiling", "$.rungs[5].move_on.casts_a_kill_ceiling")]
    [InlineData("rungs[6].sources.element", "$.rungs[6].sources.element")]
    [InlineData("rungs[1].target_margin", "$.rungs[1].target_margin")]
    [InlineData("rungs[1].sources.target_margin", "$.rungs[1].sources.target_margin")]
    [InlineData("vitals.stamina_keep_percent", "$.vitals.stamina_keep_percent")] // M6 14: the stamina line
    [InlineData("vitals.stamina_drain_max_attackers", "$.vitals.stamina_drain_max_attackers")]
    [InlineData("vitals.mana_press_fill_percent", "$.vitals.mana_press_fill_percent")] // M6 14: the press fills
    public void AMissingFieldFailsNamingTheFileAndThePath(string field, string path)
    {
        var copy = Copy();
        var (parent, name) = Locate(copy, field);
        parent.Remove(name);
        var e = Assert.Throws<LadderException>(() => Parse(copy));
        Assert.Equal(File, e.File);
        Assert.Equal(path, e.JsonPath);
        Assert.Contains(File, e.Message, StringComparison.Ordinal);
        Assert.Contains("missing", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "$.colour")]
    [InlineData("rungs[1]", "$.rungs[1].colour")]
    [InlineData("rungs[1].band", "$.rungs[1].band.colour")]
    [InlineData("rungs[6].alternates[0]", "$.rungs[6].alternates[0].colour")]
    [InlineData("rungs[0].sources", "$.rungs[0].sources.colour")]
    public void AnUnknownFieldFailsNamingTheFileAndThePath(string at, string path)
    {
        var copy = Copy();
        var target = at.Length == 0 ? copy : Node(copy, at);
        target["colour"] = "red";
        var e = Assert.Throws<LadderException>(() => Parse(copy));
        Assert.Equal(File, e.File);
        Assert.Equal(path, e.JsonPath);
        Assert.Contains("unknown field", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APendingRungWhoseGroundResolvesIsAStalePending()
    {
        var copy = Copy();
        Node(copy, "rungs[5]")["pending"] = true; // M6 3 p2: no rung is pending now, so the Abode is made pending here with a ground that resolves (M6 14: the Abode is rungs[5] since the Temple 20-40 left)
        Node(copy, "rungs[5]")["pending_reason"] = "profile due in item 3";
        Assert.Equal("$.rungs[5].pending", Assert.Throws<LadderException>(() => Parse(copy)).JsonPath);

        var alternate = Copy();
        Node(alternate, "rungs[4].alternates[0]")["ground"] = "black-spawn-den-aluvian";
        Node(alternate, "rungs[4].alternates[0]")["landblock"] = null; // M6 3 p3: the Cave rung's alternate (the Cavern, 0x5C43) is live and holds a landblock the den's profile does not
        Node(alternate, "rungs[4].alternates[0]")["pending"] = true; // and is no longer pending: a pending alternate whose ground resolves is the stale one
        Node(alternate, "rungs[4].alternates[0]")["pending_reason"] = "profile due in item 3";
        Assert.Equal("$.rungs[4].alternates[0].pending", Assert.Throws<LadderException>(() => Parse(alternate)).JsonPath);
    }

    [Fact]
    public void AGroundThatIsNotPendingMustResolve()
    {
        var copy = Copy();
        Node(copy, "rungs[6]")["ground"] = "tusker-nowhere"; // M6 3 p2: no rung is pending now, so a ground that does not resolve stands for the pending one
        var e = Assert.Throws<LadderException>(() => Parse(copy));
        Assert.Equal("$.rungs[6].ground", e.JsonPath);
        Assert.Contains("tusker-nowhere", e.Message, StringComparison.Ordinal);

        var landblock = Copy();
        Node(landblock, "rungs[6].alternates[0]")["landblock"] = "0x5B4A"; // the Temple 40+ profile holds 0x5C4A, not the Quarters' landblock
        Assert.Equal("$.rungs[6].alternates[0].landblock", Assert.Throws<LadderException>(() => Parse(landblock)).JsonPath);

        // A resolver with no profiles at all: the two resolving ground rungs and alternates fail, so the check is the resolver's, not a constant list.
        var none = Copy();
        Assert.Equal("$.rungs[1].alternates[0].ground", Assert.Throws<LadderException>(() => LadderLoader.Parse(File, none.ToJsonString(), _ => null)).JsonPath); // M6 redoubt: the first name the loader meets is the rung outskirts' alternate, its outdoor ground (a rung's alternates are read before its own ground is resolved)
    }

    [Fact]
    public void TheTargetMarginIsRequiredOnAPlayableGroundRungAndRefusedOnAQuestRung()
    {
        var playable = Copy();
        Node(playable, "rungs[1]")["target_margin"] = null;
        var e = Assert.Throws<LadderException>(() => Parse(playable));
        Assert.Equal("$.rungs[1].target_margin", e.JsonPath);
        Assert.Contains("non-pending ground rung", e.Message, StringComparison.Ordinal);

        var quest = Copy();
        Node(quest, "rungs[0]")["target_margin"] = 5;
        Assert.Equal("$.rungs[0].target_margin", Assert.Throws<LadderException>(() => Parse(quest)).JsonPath);

        var wild = Copy();
        Node(wild, "rungs[1]")["target_margin"] = 51;
        Assert.Equal("$.rungs[1].target_margin", Assert.Throws<LadderException>(() => Parse(wild)).JsonPath);
    }

    [Fact]
    public void APendingRungMustNameTheItemThatOwnsItsProfile()
    {
        foreach (var reason in new JsonNode?[] { null, "later", "profile due" })
        {
            var copy = Copy();
            Node(copy, "rungs[6]")["ground"] = "tusker-nowhere"; // M6 3 p2: a pending rung needs a ground that does not resolve
            Node(copy, "rungs[6]")["pending"] = true;
            Node(copy, "rungs[6]")["pending_reason"] = reason;
            Assert.Equal("$.rungs[6].pending_reason", Assert.Throws<LadderException>(() => Parse(copy)).JsonPath);
        }

        var cleared = Copy();
        Node(cleared, "rungs[1]")["pending_reason"] = "profile due in item 3"; // a reason on a rung that is not pending
        Assert.Equal("$.rungs[1].pending_reason", Assert.Throws<LadderException>(() => Parse(cleared)).JsonPath);
    }

    [Fact]
    public void TheBandsMustBeContiguousAndEndAtTheTopLevel()
    {
        var gap = Copy();
        Node(gap, "rungs[3].band")["from"] = 16;
        Assert.Equal("$.rungs[3].band.from", Assert.Throws<LadderException>(() => Parse(gap)).JsonPath);

        var first = Copy();
        Node(first, "rungs[0].band")["from"] = 2;
        Assert.Equal("$.rungs[0].band.from", Assert.Throws<LadderException>(() => Parse(first)).JsonPath);

        var shortTop = Copy();
        shortTop["top_level"] = 127;
        Assert.Equal("$.rungs[7].band.to", Assert.Throws<LadderException>(() => Parse(shortTop)).JsonPath); // M6 14: eight rungs, the Holding last

        var moveOn = Copy();
        Node(moveOn, "rungs[1].move_on")["level"] = 17; // one past the band's end (15, so 16 is the most) is over (M6 7 AK: the outskirts are the rung with the band 5-15)
        Assert.Equal("$.rungs[1].move_on.level", Assert.Throws<LadderException>(() => Parse(moveOn)).JsonPath);
    }

    [Fact]
    public void RouteElementAndKindAreCheckedAgainstTheirVocabulary()
    {
        foreach (var (field, value, path) in new[]
        {
            ("route", "teleport:holtburg", "$.rungs[2].route"),
            ("route", "walk:", "$.rungs[2].route"),
            ("route", "Walk:Holtburg", "$.rungs[2].route"),
            ("element", "lava", "$.rungs[2].element"),
            ("kind", "dungeon", "$.rungs[2].kind"),
        })
        {
            var copy = Copy();
            Node(copy, "rungs[2]")[field] = value;
            Assert.Equal(path, Assert.Throws<LadderException>(() => Parse(copy)).JsonPath);
        }

        var quest = Copy();
        Node(quest, "rungs[0]")["quests"] = new JsonArray();
        Assert.Equal("$.rungs[0].quests", Assert.Throws<LadderException>(() => Parse(quest)).JsonPath);

        var floor = Copy();
        Node(floor, "rungs[2].move_on")["kills_an_hour_floor"] = null;
        Assert.Equal("$.rungs[2].move_on", Assert.Throws<LadderException>(() => Parse(floor)).JsonPath);

        var spell = Copy();
        Node(spell, "rungs[1].spell_level")["min"] = 3;
        Assert.Equal("$.rungs[1].spell_level.min", Assert.Throws<LadderException>(() => Parse(spell)).JsonPath);

        foreach (var chance in new[] { 0.0, 1.01, -0.5 })
        {
            var planChance = Copy();
            planChance["plan_cast_chance"] = chance; // M6 1 a: the plan's chance is in (0, 1]
            Assert.Equal("$.plan_cast_chance", Assert.Throws<LadderException>(() => Parse(planChance)).JsonPath);
        }

        var text = Copy();
        text["plan_cast_chance"] = "0.95";
        Assert.Equal("$.plan_cast_chance", Assert.Throws<LadderException>(() => Parse(text)).JsonPath);
    }

    [Fact]
    public void ADuplicatedFieldAndNonJsonFail()
    {
        var text = LadderLoader.ResourceText(File)!;
        var twice = text.Replace("\"top_level\": 126,", "\"top_level\": 126,\n  \"top_level\": 126,", StringComparison.Ordinal);
        Assert.NotEqual(text, twice);
        Assert.Equal("$.top_level", Assert.Throws<LadderException>(() => LadderLoader.Parse(File, twice)).JsonPath);
        Assert.Equal("$", Assert.Throws<LadderException>(() => LadderLoader.Parse(File, "{ not json")).JsonPath);
        Assert.Equal("$.key", Assert.Throws<LadderException>(() => LadderLoader.Parse("other.json", text)).JsonPath);
    }

    private static JsonObject Copy() => JsonNode.Parse(LadderLoader.ResourceText(File)!)!.AsObject();

    private static Ladder Parse(JsonNode copy) => LadderLoader.Parse(File, copy.ToJsonString());

    /// <summary>The object at a dotted path with list indexes (<c>rungs[2].move_on</c>).</summary>
    private static JsonObject Node(JsonObject root, string at)
    {
        JsonNode node = root;
        foreach (var part in at.Split('.'))
        {
            var open = part.IndexOf('[', StringComparison.Ordinal);
            node = open < 0 ? node[part]! : node[part[..open]]![int.Parse(part[(open + 1)..^1], System.Globalization.CultureInfo.InvariantCulture)]!;
        }

        return node.AsObject();
    }

    private static (JsonObject Parent, string Name) Locate(JsonObject root, string field)
    {
        var dot = field.LastIndexOf('.');
        return dot < 0 ? (root, field) : (Node(root, field[..dot]), field[(dot + 1)..]);
    }
}

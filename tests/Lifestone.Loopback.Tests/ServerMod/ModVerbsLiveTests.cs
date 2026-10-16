using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Interiors;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Dat;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.ServerMod;

/// <summary>
/// SM9 (m2-plan section 3, "server mod"; wave 3 fix dod-10-11-14-15-1): the <c>@lifestone</c> verbs the Tools panel (G7) and
/// <c>PlayerTools</c> depend on, driven through the real mod on the bundled ACE on 127.0.0.1 with the test account and the loopback
/// character (<see cref="LiveNetClient.LoopbackCharacter"/>): <c>status</c>, <c>places</c> (56 destinations under 62 names), every error
/// reply that can be asked from a client (<c>not-found</c>, <c>invalid-cell</c>, <c>busy</c> while teleporting, <c>bad-args</c>,
/// <c>unknown-verb</c>), and one <c>place loc</c> to a curated interior row (the Holtburg shop). Every test that moves the character ends with
/// it back at its start (the Training Academy), so the other live tests find it where they expect it. Skipped, with the probe's reason, unless
/// the backend runs with the mod ready.
/// </summary>
/// <remarks>
/// <para>Nothing here levels the character: the <c>xp</c> verb is in <see cref="ModXpLiveTests"/>, which carries a fixed Skip because it
/// permanently raises a character's level and needs the owner's go-ahead. The error replies for <c>xp</c> (an amount of 0 or past the limit)
/// are safe and are here.</para>
/// <para>The tests run in step order after the net track's (<see cref="LiveStepAttribute"/>, steps 1 to 20).</para>
/// <para><b>Not covered, and why.</b> <c>not-in-world</c>, <c>not-loopback</c> and <c>needs-player</c> need a session that is not in a landblock, one from
/// another computer, or ACE's console; a client on this computer cannot make them. The mod's routing tests (<c>ToolRoutingTests</c>) cover those.
/// <c>status</c> at ACE's console is the same reply with no account and no character; it cannot be asked from here either.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[TestCaseOrderer("Lifestone.Loopback.Tests.Net.LiveStepOrderer", "Lifestone.Loopback.Tests")]
public sealed partial class ModVerbsLiveTests(ITestOutputHelper output)
{
    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex Commit();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Sha256();

    /// <summary>
    /// What it proves: the mod answers <c>status</c> with the wire version this client speaks, the ACE it was built for, the pinned loopback host,
    /// a plain Player session (so ACE's first-account promotion cannot have made the test account an Admin), the character's name, an open world and
    /// every verb of <see cref="ToolsWire.Verbs"/>; and the chat line the player sees is the mod's sentence.
    /// </summary>
    [LoopbackFact]
    [LiveStep(30)]
    public void Status_describes_the_mod_the_pinned_host_a_plain_Player_and_all_verbs()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        try
        {
            EnterTheWorld(client);
            Unhide(client);
            var id = NextId();
            var parse = AskText(client, ToolsWire.Request(new StatusRequest(id)), ToolVerbs.Status, id);
            Assert.True(parse.Reply!.Ok, parse.Reply.Message);
            var status = parse.Reply.DataAs<StatusData>()!;
            output.WriteLine(status.Describe());

            Assert.Equal(ToolsWire.Version, status.Wire);
            Assert.False(string.IsNullOrWhiteSpace(status.Mod));
            Assert.Matches(Commit(), status.AceCommit);
            Assert.Matches(Sha256(), status.AceServerDllSha256);
            Assert.Equal(LoopbackPolicyHost, status.Host);
            Assert.Equal(BackendProbe.Probe().AceEndPoint?.Port, status.Port);
            Assert.Equal("Player", status.Access);
            Assert.False(status.AutoPromotion, "ACE's first-account Admin auto-promotion is on");
            Assert.False(string.IsNullOrWhiteSpace(status.Account));
            Assert.Equal(LiveNetClient.LoopbackCharacter, status.Character);
            Assert.Equal("Open", status.World);
            Assert.Equal(ToolsWire.Verbs.Order(StringComparer.Ordinal), status.Verbs.Order(StringComparer.Ordinal));

            Assert.StartsWith(ToolsWire.ChatPrefix, parse.ChatLine, StringComparison.Ordinal);
            Assert.Equal(ToolsWire.ChatPrefix + status.Describe(), parse.ChatLine);

            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves: <c>places</c> lists ACE's 56 destinations under 62 names (DoD 10), each with a portal weenie, a parseable destination cell and
    /// finite coordinates; every name is one <c>place poi</c> accepts by the request rules (the picker can send any of them); Holtburg is among them;
    /// and the chat sentence says so. The worst-case reply length is the unit tests' (<c>PlacesReplySizeTests</c>); here the real reply parses.
    /// </summary>
    [LoopbackFact]
    [LiveStep(31)]
    public void Places_lists_56_destinations_under_62_names_that_place_poi_accepts()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        try
        {
            EnterTheWorld(client);
            Unhide(client);
            var id = NextId();
            var parse = AskText(client, ToolsWire.Request(new PlacesRequest(id)), ToolVerbs.Places, id);
            Assert.True(parse.Reply!.Ok, parse.Reply.Message);
            var places = parse.Reply.DataAs<PlacesData>()!;
            output.WriteLine(places.Describe());

            Assert.Equal(56, places.Count);
            Assert.Equal(62, places.Names);
            Assert.Equal(56, places.Places.Count);
            Assert.Equal(62, places.Places.Sum(p => p.Names.Count));
            Assert.All(places.Places, p =>
            {
                Assert.NotEmpty(p.Names);
                Assert.True(p.Wcid > 0, "a destination without a portal weenie");
                Assert.True(ToolsWire.TryParseCell(p.Cell, out _), $"'{p.Cell}' is not a cell id");
                Assert.All(new[] { p.X, p.Y, p.Z, p.Qw, p.Qx, p.Qy, p.Qz }, v => Assert.True(float.IsFinite(v)));
                Assert.All(p.Names, name => Assert.True(ToolRequestParser.IsValidPoiName(name), $"place poi would refuse the name '{name}'"));
            });

            var names = places.Places.SelectMany(p => p.Names).ToList();
            Assert.Contains(names, n => string.Equals(n, "Holtburg", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(ToolsWire.ChatPrefix + places.Describe(), parse.ChatLine);

            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves: each refusal a client can provoke comes back as a failed reply with its own error code, the request's verb and id and no data,
    /// and changes nothing (the server still has the player where it was): an unknown point of interest (<c>not-found</c>), a cell that is not in ACE's
    /// cell dat (<c>invalid-cell</c>), arguments the parser refuses (<c>bad-args</c>, including an <c>xp</c> of 0 and of one past the limit) and an unknown
    /// verb (<c>unknown-verb</c>, echoing the lower-cased word).
    /// </summary>
    [LoopbackFact]
    [LiveStep(32)]
    public void Refusals_come_back_as_failed_replies_with_their_error_codes_and_change_nothing()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var restored = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var before = AskData<WhereData>(client, new WhereRequest(NextId()));

            var cases = new (string Text, string Verb, string Error)[]
            {
                // A point of interest nobody named; a cell no dat holds: 0x0001FFFD passes the parser and PlaceRules (an EnvCell index, the last one,
                // in a landblock on the map) but no retail landblock has 65,000 EnvCells (PlaceRulesTests and PlaceLocRefusalCellTests pin both halves).
                // Not 0x00010150: that EnvCell is in the dat, and ACE carried the teleport out.
                ("place poi \"No Such Place In Dereth\"", ToolVerbs.Place, ToolErrors.NotFound),
                ("place loc 0x0001FFFD 10 10 0", ToolVerbs.Place, ToolErrors.InvalidCell),
                // The parser's refusals.
                ("xp 0", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("xp 1000000000000", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("xp -5", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("xp lots", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("place loc nonsense", ToolVerbs.Place, ToolErrors.BadArgs),
                ("place poi", ToolVerbs.Place, ToolErrors.BadArgs),
                ("where now", ToolVerbs.Where, ToolErrors.BadArgs),
                // Not a verb.
                ("frobnicate", "frobnicate", ToolErrors.UnknownVerb),
            };

            var asked = cases.Select(c =>
            {
                var id = NextId();
                return (Text: $"@{ToolsWire.Command} {c.Text} #{id}", c.Verb, Id: id);
            }).ToList();

            var replies = AskTexts(client, asked);
            for (var i = 0; i < cases.Length; i++)
            {
                var reply = replies[i].Reply!;
                output.WriteLine($"{cases[i].Text} -> {reply.Error}: {reply.Message}");
                Assert.False(reply.Ok, $"'{cases[i].Text}' was carried out");
                Assert.Equal(cases[i].Error, reply.Error);
                Assert.Equal(cases[i].Verb, reply.Verb);
                Assert.Equal(asked[i].Id, reply.Id);
                Assert.Null(reply.Data);
                Assert.False(string.IsNullOrWhiteSpace(reply.Message));
                Assert.StartsWith(ToolsWire.ChatPrefix, replies[i].ChatLine, StringComparison.Ordinal);
                Assert.Contains(ToolErrors.All, e => e == reply.Error);
            }

            var after = AskData<WhereData>(client, new WhereRequest(NextId()));
            Assert.Equal(before.Cell, after.Cell);
            Assert.Equal((before.X, before.Y, before.Z), (after.X, after.Y, after.Z));
            Assert.False(after.Teleporting);
            Assert.Equal(before.TeleportSeq, after.TeleportSeq);
            restored = true;
            LogOffAndQuit(client);
        }
        finally
        {
            if (!restored)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves: while the server still has the player teleporting (after the F751, before LoginComplete) both <c>place</c> forms are refused
    /// <c>busy</c> and do not start a second teleport; after LoginComplete the server leaves Teleporting and the next <c>place</c> works (the character goes
    /// back to its start).
    /// </summary>
    [LoopbackFact]
    [LiveStep(33)]
    public void Place_is_refused_busy_while_the_player_is_teleporting_and_works_again_after_LoginComplete()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var back = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var started = StartTeleport(client, new PlacePoiRequest("Holtburg", NextId()));
            var teleports = client.Sink.Teleports;

            var mid = AskData<WhereData>(client, new WhereRequest(NextId()));
            Assert.True(mid.Teleporting, "the server does not have the player teleporting between the F751 and LoginComplete");

            var poi = Ask(client, new PlacePoiRequest("Holtburg", NextId()));
            var loc = Ask(client, new PlaceLocRequest(ToLocation(start), NextId()));
            foreach (var reply in new[] { poi, loc })
            {
                output.WriteLine($"place while teleporting -> {reply.Error}: {reply.Message}");
                Assert.False(reply.Ok);
                Assert.Equal(ToolErrors.Busy, reply.Error);
                Assert.Equal(ToolVerbs.Place, reply.Verb);
                Assert.Null(reply.Data);
            }

            client.PumpUntil(() => false, TimeSpan.FromSeconds(2));
            Assert.Equal(teleports, client.Sink.Teleports);

            var arrival = FinishArrival(client, started);
            Assert.Equal(0xA9B4u, LandblockOf(arrival.Where.Cell));
            Assert.False(arrival.Where.Teleporting);

            ReturnToStart(client, start);
            back = true;
            LogOffAndQuit(client);
        }
        finally
        {
            if (!back)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves (the Places panel's curated interior rows, DoD 10): <c>place loc</c> to the Holtburg shop's interior cell
    /// (<see cref="InteriorLocations.HoltburgShop"/>, an EnvCell inside a town building, not a dungeon) is carried out; the reply names the cell, there
    /// is an F751 and an F748 in Holtburg's landblock, and after LoginComplete the server has the player in that indoor cell within half a metre. The
    /// character then goes back to its start. The client's <c>PlayerTools.Place</c> and the panel's picker send exactly this request; they are tested
    /// against the stand-in (<c>ToolsModelTests</c>) and, live, by the login check's tools leg.
    /// </summary>
    [LoopbackFact]
    [LiveStep(34)]
    public void Place_loc_to_a_curated_interior_cell_puts_the_player_inside_the_Holtburg_shop()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var back = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var shop = InteriorLocations.HoltburgShop;
            var target = new AcPosition(shop.CellId, shop.Location.Position.Local, Quaternion.Identity);
            var arrival = PlaceAndArrive(client, new PlaceLocRequest(ToLocation(target), NextId()));
            output.WriteLine($"place loc {shop.Name}: {arrival.Place.Describe()}; where {arrival.Where.Cell} ({arrival.Where.X:F2}, {arrival.Where.Y:F2}, {arrival.Where.Z:F2})");

            Assert.Equal(ToolVerbs.PlaceLoc, arrival.Place.Kind);
            Assert.Null(arrival.Place.Name);
            Assert.Equal(ToolsWire.FormatCell(shop.CellId), arrival.Place.Cell);
            Assert.True(arrival.Teleports >= 1, "no F751 PlayerTeleport");
            Assert.Equal(shop.CellId >> 16, arrival.Position.Position.CellId >> 16);
            Assert.True(ToolsWire.TryParseCell(arrival.Where.Cell, out var cell));
            Assert.Equal(shop.CellId >> 16, cell >> 16);
            Assert.True(PlaceRules.IsEnvCell(cell), $"the server has the player in 0x{cell:X8}, which is not an indoor cell");
            Assert.True(Distance(arrival.Where, target) <= 0.5f, $"where is {Distance(arrival.Where, target):F2} m from the interior point");

            ReturnToStart(client, start);
            back = true;
            LogOffAndQuit(client);
        }
        finally
        {
            if (!back)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    /// <summary>The character the M3 verb rows use: the throwaway, never a graduated character (m3-plan 7, D3). Created on the test account when missing.</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>A cheap level-one spell: ACE's SpellId enum counts StrengthSelf1 as 2 (ACE.Entity/Enum/SpellId.cs, after Undef and StrengthOther1).</summary>
    private const uint CheapSpell = 2;

    /// <summary>Not in the spell table: above a u16, which the spell-update event cannot carry, and the highest u16.</summary>
    private const uint UnknownSpellHigh = 4_000_000_000;

    private const uint UnknownSpellLow = 65_535;

    /// <summary>ACE's <c>coinstack</c> (the pyreal), a stackable weenie: ACE.Server/Factories/Enum/WeenieClassName.cs:253.</summary>
    private const uint Pyreal = 273;

    /// <summary>
    /// WRITTEN AND COMPILED, RUN BY THE LEAD (m3 1.1 s2a). What it proves, on <c>Lifestone Throwaway</c>: <c>spell learn</c> of one cheap spell is answered
    /// <c>learned</c> (a fresh throwaway) or <c>known</c> (one that has it from an earlier run) and a second request for it is answered <c>known</c>, which is how
    /// a ready mage is verified; the reply names the spell and its counts match the outcomes; an id mixed into a list that is not in ACE's spell table
    /// (above and below the u16 range) is answered <c>unknown</c> with no name while the others in the list are still answered, the whole request is
    /// <c>ok</c>, and the chat line is the mod's sentence; a list of 129 ids is refused as <c>bad-args</c> with no data.
    /// </summary>
    [LoopbackFact]
    [LiveStep(35)]
    public void Spell_learn_answers_learned_then_known_and_marks_an_unknown_id_without_learning_it()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, Throwaway);
        try
        {
            EnterTheWorld(client);
            Unhide(client);

            var first = AskData<SpellLearnData>(client, new SpellLearnRequest([CheapSpell], NextId()));
            output.WriteLine("first: " + first.Describe());
            var one = Assert.Single(first.Spells);
            Assert.Equal(CheapSpell, one.Id);
            Assert.Contains(one.Outcome, new[] { SpellOutcomes.Learned, SpellOutcomes.Known });
            Assert.False(string.IsNullOrWhiteSpace(one.Name));
            Assert.Equal(one.Outcome == SpellOutcomes.Learned ? (1, 0, 0) : (0, 1, 0), (first.Learned, first.Known, first.Unknown));

            var id = NextId();
            var chat = AskText(client, ToolsWire.Request(new SpellLearnRequest([CheapSpell], id)), ToolVerbs.Spell, id);
            Assert.True(chat.Reply!.Ok, chat.Reply.Message);
            var second = chat.Reply.DataAs<SpellLearnData>()!;
            output.WriteLine("second: " + second.Describe());
            Assert.Equal([new SpellOutcome(CheapSpell, SpellOutcomes.Known, one.Name)], second.Spells);
            Assert.Equal((0, 1, 0), (second.Learned, second.Known, second.Unknown));
            Assert.Equal(ToolsWire.ChatPrefix + second.Describe(), chat.ChatLine);

            // An id the spell table lacks is marked unknown and nothing is learned for it; the ids around it are still answered.
            var mixed = AskData<SpellLearnData>(client, new SpellLearnRequest([CheapSpell, UnknownSpellLow, UnknownSpellHigh, CheapSpell], NextId()));
            output.WriteLine("mixed: " + mixed.Describe());
            Assert.Equal(
                [SpellOutcomes.Known, SpellOutcomes.Unknown, SpellOutcomes.Unknown, SpellOutcomes.Known],
                mixed.Spells.Select(s => s.Outcome));
            Assert.Equal([CheapSpell, UnknownSpellLow, UnknownSpellHigh, CheapSpell], mixed.Spells.Select(s => s.Id));
            Assert.All(mixed.Spells.Where(s => s.Outcome == SpellOutcomes.Unknown), s => Assert.Null(s.Name));
            Assert.Equal((0, 2, 2), (mixed.Learned, mixed.Known, mixed.Unknown));

            // More ids than the verb takes is refused, with no data.
            var tooMany = NextId();
            var refused = AskText(client, "@lifestone spell learn " + string.Join(',', Enumerable.Repeat(CheapSpell, ToolRequestParser.MaxSpellIds + 1)) + " #" + tooMany, ToolVerbs.Spell, tooMany);
            Assert.False(refused.Reply!.Ok);
            Assert.Equal(ToolErrors.BadArgs, refused.Reply.Error);
            Assert.Null(refused.Reply.Data);

            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// WRITTEN AND COMPILED, RUN BY THE LEAD (m3 1.1 s2a). What it proves, on <c>Lifestone Throwaway</c>: <c>item</c> of a plain stackable weenie (the pyreal)
    /// creates it in the pack, and the reply names a new object id, the wcid, the name and the stack asked for (not clamped, with the weenie's own maximum);
    /// a stack above the weenie's maximum is clamped to it as ACE's <c>@ci</c> does and the reply says so (<c>clamped</c>, <c>requested</c> and <c>maxStack</c>
    /// agree with <c>stack</c>); and a weenie class id the world database lacks is refused as <c>unknown-wcid</c> with no data and nothing created.
    /// </summary>
    [LoopbackFact]
    [LiveStep(36)]
    public void Item_creates_a_stack_in_the_pack_clamps_a_big_one_and_refuses_an_unknown_wcid()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, Throwaway);
        try
        {
            EnterTheWorld(client);
            Unhide(client);

            var id = NextId();
            var chat = AskText(client, ToolsWire.Request(new ItemRequest(Pyreal, 5, id)), ToolVerbs.Item, id);
            Assert.True(chat.Reply!.Ok, chat.Reply.Message);
            var made = chat.Reply.DataAs<ItemData>()!;
            output.WriteLine(made.Describe());
            Assert.Equal(Pyreal, made.Wcid);
            Assert.NotEqual(0u, made.Guid);
            Assert.False(string.IsNullOrWhiteSpace(made.Name));
            Assert.Equal(5, made.Stack);
            Assert.Equal(5, made.Requested);
            Assert.False(made.Clamped);
            Assert.True(made.MaxStack is >= 5, "a pyreal stack should hold at least 5");
            Assert.Equal(ToolsWire.ChatPrefix + made.Describe(), chat.ChatLine);

            var big = AskData<ItemData>(client, new ItemRequest(Pyreal, ToolRequestParser.MaxItemStack, NextId()));
            output.WriteLine(big.Describe());
            Assert.NotEqual(made.Guid, big.Guid);
            Assert.Equal(ToolRequestParser.MaxItemStack, big.Requested);
            Assert.NotNull(big.MaxStack);
            Assert.Equal(Math.Min(ToolRequestParser.MaxItemStack, big.MaxStack!.Value), big.Stack);
            Assert.Equal(big.Stack < ToolRequestParser.MaxItemStack, big.Clamped);

            var missing = Ask(client, new ItemRequest(4_000_000_000, null, NextId()));
            Assert.False(missing.Ok);
            Assert.Equal(ToolErrors.UnknownWcid, missing.Error);
            Assert.Equal(ToolVerbs.Item, missing.Verb);
            Assert.Null(missing.Data);

            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// WRITTEN AND COMPILED, RUN BY THE LEAD (m3 1.1 fix, 1.1-R9). What it proves, on <c>Lifestone Throwaway</c> only: the mage build's largest request goes out and
    /// comes back. One <c>spell learn</c> of exactly the first 128 ids of <see cref="ArenaProfile.SpellsToLearn"/> is a Talk of about 640 bytes, more than one
    /// fragment holds (448), so it leaves as a multi-fragment game message; ACE and the mod accept it, the reply carries 128 outcomes in the order asked, and
    /// every one is <c>learned</c> or <c>known</c> (none <c>unknown</c>: every id of the profile is in the spell table). It teaches only the Throwaway, a
    /// character the rules let the loops level freely; BSD Mage is not touched before its permanent build.
    /// </summary>
    [LoopbackFact]
    [LiveStep(37)]
    public void Spell_learn_of_the_first_128_profile_ids_goes_out_as_a_multi_fragment_request_and_answers_128_outcomes()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, Throwaway);
        try
        {
            EnterTheWorld(client);
            Unhide(client);

            var ids = ArenaProfile.SpellsToLearn.Take(ToolRequestParser.MaxSpellIds).ToArray();
            Assert.Equal(ToolRequestParser.MaxSpellIds, ids.Length);
            Assert.True(ToolsWire.Request(new SpellLearnRequest(ids, "1")).Length > 448, "the request should need more than one fragment");

            var learned = AskData<SpellLearnData>(client, new SpellLearnRequest(ids, NextId()));
            output.WriteLine("128 ids: " + learned.Describe());
            Assert.Equal(ids.Length, learned.Spells.Count);
            Assert.Equal(ids, learned.Spells.Select(s => s.Id).ToArray());
            Assert.All(learned.Spells, s => Assert.Contains(s.Outcome, new[] { SpellOutcomes.Learned, SpellOutcomes.Known }));
            Assert.Equal(0, learned.Unknown);
            Assert.Equal(ids.Length, learned.Learned + learned.Known);

            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>The host the mod reports (the pinned loopback host, 127.0.0.1).</summary>
    private const string LoopbackPolicyHost = Lifestone.Net.Sockets.LoopbackPolicy.PinnedHost;
}

using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 P7n (D145 point 4, D141): the start-up audit widened into a full state audit over the scripted ACE of <see cref="AutopilotRig"/>. Each gap the owner's live proof names is found from the live state and fixed by its named
/// recovery, checked after: a caster put in a pack, a peace stance, a walk off the spot (and a place with no route), a buff line missing or low, an open vendor or corpse, an attacker on the mage, a trip due. Each start says what
/// it found and does in one chat line (<see cref="AutopilotScenario.ChatNotice"/>), and a stop says why (<see cref="AutopilotScenario.Stopped"/>). A stop in place then a resume in each phase (the fight, the loot, the trip's walk, the
/// vendor) leaves no stale state. Each landed damage spell logs the caster held and the Spirit Drinker aura (D145 point 6).
/// </summary>
public sealed class AutopilotFullAuditTests(ITestOutputHelper output)
{
    private const uint CasterType = 0x8000;
    private const uint Held = 0x01000000;
    private const uint SceptreWcid = 31336; // a scripted caster's weenie (the scripted ACE judges none)
    private const uint Guard = 0x80000F45;
    private const uint Corpse = 0x80000C45;

    private static void Proven(JourneyContext c, string key)
    {
        c.Facts.LifestoneTied = true;
        c.Facts.SetPortalTie(key, PortalTie.Tied);
    }

    /// <summary>A caster wielded in the hands, as the server's 0023 leaves it.</summary>
    private static uint Wielded(ApWorld w, string name = "Fire Sceptre")
    {
        var caster = w.Trip.Own(name, SceptreWcid, CasterType, 100);
        w.Trip.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, caster, (int)Held));
        w.Trip.Vendor.R.Tick();
        Assert.Equal(caster, w.Trip.Vendor.State.Self.Inventory.EquippedAt(Held));
        return caster;
    }

    /// <summary>A living target creature of the ground <paramref name="metres"/> from the mage (as <see cref="AutopilotF23Tests"/>).</summary>
    private static void Hostile(ApWorld w, uint guid, float metres)
    {
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + metres, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = w.Ground.TargetWcids()[0] } }, isUpdate: false);
    }

    /// <summary>A corpse the player has open (its 0196 ViewContents, as the fight's loot step leaves it).</summary>
    private static void OpenCorpse(ApWorld w)
    {
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(Corpse, ReducerHarness.Stamps(), null, name: "Corpse of Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis, isUpdate: false);
        r.Reducer.OnViewContents(r.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 900, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, []));
        Assert.Equal(Corpse, w.Trip.View.State.Containers.ViewedGuid);
    }

    /// <summary>The scripted ACE answers each NoLongerViewingContents with its 0052 (as <c>ContainerTrackerTests</c>).</summary>
    private static void AnswerCloses(ApWorld w, ref int answered)
    {
        var r = w.Trip.Vendor.H.R;
        var closes = w.Trip.Vendor.H.Net.Of(FakeSendKind.NoLongerViewingContents);
        for (; answered < closes.Count; answered++)
        {
            r.Reducer.OnCloseGroundContainer(r.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 901, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent((uint)closes[answered].Guid));
        }
    }

    private static string AuditLine(ApWorld w, string check) => Assert.Single(w.Lines, l => l.StartsWith($"autopilot audit {check}: ", StringComparison.Ordinal));

    // ------------------------------------------------------------------------------------------------ each gap, found and fixed

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_caster_put_in_a_pack_is_wielded_again_by_the_recovery_tables_fix_and_checked_held_after(string key)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        var wand = w.Trip.Own("Wand", SceptreWcid, CasterType, 100); // in the main pack, not in the hands
        var autopilot = w.Autopilot(new AutopilotOptions());
        var notices = new List<string>();
        autopilot.ChatNotice += notices.Add;

        w.Run(autopilot, a => a.Audits >= 1, perFrame: _ => AnswerWieldsAndModes(w));

        Assert.Equal(wand, w.Trip.Vendor.State.Self.Inventory.EquippedAt(Held)); // the after-check: in the hands
        Assert.Equal(wand, (uint)Assert.Single(w.Trip.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem)).Guid);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit waits: caster: the hands hold no caster and the packs hold 1: the recovery table's wield-caster fix runs", StringComparison.Ordinal));
        Assert.StartsWith("autopilot audit caster: the hands held no caster; fix=wield-caster: wield 'Wand' 0x", AuditLine(w, "caster"), StringComparison.Ordinal);
        Assert.Contains("a caster is held now", AuditLine(w, "caster"), StringComparison.Ordinal);
        Assert.Contains(" caster=rewielded ", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.Contains(notices, n => n.StartsWith("Autopilot started: ", StringComparison.Ordinal) && n.Contains("caster re-wielded ('Wand')", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_peace_stance_on_the_ground_is_set_to_magic_and_checked_after_and_a_server_that_keeps_peace_is_a_named_gap(string key)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        Wielded(w);
        w.Trip.Vendor.R.CombatModeIs(CombatModes.NonCombat); // the person's peace before /auto
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Audits >= 1, perFrame: _ => AnswerWieldsAndModes(w));

        Assert.Equal(CombatModes.Magic, w.Trip.Vendor.State.Self.Qualities.CombatMode);
        Assert.Equal(CombatModes.Magic, (int)w.Trip.Vendor.H.Net.Of(FakeSendKind.ChangeCombatMode)[0].Amount);
        Assert.Equal("autopilot audit stance: peace; fix=the magic mode asked: Confirmed (combat mode 1 -> 8); after: magic", AuditLine(w, "stance"));
        Assert.Contains(" stance=magic-set ", autopilot.LastAudit, StringComparison.Ordinal);

        // the server turns the magic mode into peace: the audit names the gap and goes on (the fight's own mode step asks again)
        var g = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        Wielded(g);
        g.Trip.Vendor.R.CombatModeIs(CombatModes.NonCombat);
        var stubborn = g.Autopilot(new AutopilotOptions());
        g.Run(stubborn, a => a.Audits >= 1);

        Assert.Contains("after: peace: gap: the fight's own mode step asks again", AuditLine(g, "stance"), StringComparison.Ordinal);
        Assert.Contains(" stance=gap ", stubborn.LastAudit, StringComparison.Ordinal);
        Assert.Null(stubborn.StopName);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Walked_off_the_spot_in_the_ground_the_fight_resumes_where_he_stands_and_from_a_place_with_no_route_he_says_so_and_recalls(string key)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        var arrival = w.Ground.ArrivalDestination().Location.Position;
        w.Trip.Vendor.StandAt(arrival.CellId, arrival.Local + new Vector3(6, 0, 0)); // walked 6 m off the arrival, still in the den
        w.Trip.Vendor.R.Enchant(2); // a buff up, so the arrow is the fight
        var autopilot = w.Autopilot(new AutopilotOptions());
        var notices = new List<string>();
        autopilot.ChatNotice += notices.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Contains(", 6 m from ", AuditLine(w, "place"), StringComparison.Ordinal);
        Assert.Contains("fix=none: the fight resumes from where it stands and names each walk it makes (D47", AuditLine(w, "place"), StringComparison.Ordinal);
        Assert.Equal(["den-fight"], Kinds(w));
        Assert.Same(AutopilotScenario.FightResumeSpec, w.Factory.Made[0]); // no walk back to the arrival, no recall: the fight's own walks are named
        Assert.Contains(notices, n => n.EndsWith("; back to the fight from where it stands.", StringComparison.Ordinal));

        // a place with no route: neither home nor the ground
        var e = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        e.Trip.Vendor.StandAt(0xC6A90001, new Vector3(12, 12, 0));
        var lost = e.Autopilot(new AutopilotOptions());
        var said = new List<string>();
        lost.ChatNotice += said.Add;
        e.Run(lost, a => a.Audits >= 1);

        Assert.Equal("recall-home", Kinds(e)[0]);
        Assert.Contains(said, n => n.StartsWith("Autopilot: it is neither in ", StringComparison.Ordinal) && n.Contains("no route from here, so it recalls home, then takes the way back.", StringComparison.Ordinal));
        Assert.Contains(e.Lines, l => l.StartsWith("autopilot audit place: elsewhere (cell 0xC6A90001)", StringComparison.Ordinal) && l.Contains("no route walks back from an unknown place, so it never guesses one", StringComparison.Ordinal));
        Assert.Contains("it was away from home and the den: recalled home", said[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_buff_line_missing_or_low_in_the_ground_runs_the_buff_pass_before_the_fight(string key)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        w.Trip.Vendor.R.Enchant(2); // one buff up: before P7n the audit went straight to the fight
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // the lines are judged one by one: every wanted line but none is down in the scripted world
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Equal(["buff", "den-fight"], Kinds(w));
        var buffs = AuditLine(w, "buffs");
        Assert.StartsWith("autopilot audit buffs: 1 enchantment(s) up; vitae 0%", buffs, StringComparison.Ordinal);
        Assert.Contains(" down", buffs, StringComparison.Ordinal);
        Assert.Contains("; due under the ", buffs, StringComparison.Ordinal);
        Assert.Contains("fix=the buff pass before the first fight (renews ", buffs, StringComparison.Ordinal);
        Assert.Matches(" buffs=buff .* buffs_due=[1-9]", autopilot.LastAudit);
    }

    [Fact]
    public void The_spirit_drinker_aura_is_read_from_the_spellbook_and_the_enchantments()
    {
        var w = new ApWorld(Den, output, atHome: false) { Prime = c => Proven(c, Den) };
        var state = w.Trip.Vendor.State;
        Assert.Equal(0, SpiritDrinker.LearnedLevel(state));
        state.Self.Spellbook.Learn(SpiritDrinker.FirstSpellId + 3); // IV
        Assert.Equal(4, SpiritDrinker.LearnedLevel(state));
        Assert.False(SpiritDrinker.TryAura(state, w.Now, out _, out _));
        w.Trip.Vendor.R.Enchant((ushort)(SpiritDrinker.FirstSpellId + 3));
        Assert.True(SpiritDrinker.TryAura(state, w.Now, out var level, out var left));
        Assert.Equal(4, level);
        Assert.InRange(left, 1790, 1800);

        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Audits >= 1);
        Assert.StartsWith("autopilot audit spirit_drinker: learned to IV; up: level IV, ", AuditLine(w, "spirit_drinker"), StringComparison.Ordinal);
        Assert.Contains(" spirit=up:IV ", autopilot.LastAudit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void An_open_vendor_and_an_open_corpse_are_closed_and_checked_closed(string key)
    {
        var w = new ApWorld(key, output) { Prime = c => Proven(c, key) };
        w.OwnKit();
        var vendor = w.Ground.Vendors[0];
        w.Trip.Vendor.Stock(vendor.Guid, vendor.BuysItemTypes, []); // the vendor a stopped trip left open
        OpenCorpse(w);
        Assert.NotNull(w.Trip.View.State.Vendor);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var notices = new List<string>();
        autopilot.ChatNotice += notices.Add;
        var answered = 0;
        w.Run(autopilot, a => a.Audits >= 1, perFrame: _ => AnswerCloses(w, ref answered));

        Assert.Null(w.Trip.View.State.Vendor);
        Assert.Equal(0u, w.Trip.View.State.Containers.ViewedGuid);
        Assert.Equal(Corpse, (uint)Assert.Single(w.Trip.Vendor.H.Net.Of(FakeSendKind.NoLongerViewingContents)).Guid);
        Assert.Contains("fix=closed (the client's own close: ACE has no close message", AuditLine(w, "vendor"), StringComparison.Ordinal);
        Assert.Contains("after: no vendor open", AuditLine(w, "vendor"), StringComparison.Ordinal);
        Assert.Contains("fix=closed (NoLongerViewingContents): Confirmed", AuditLine(w, "container"), StringComparison.Ordinal);
        Assert.EndsWith("; after: no container open", AuditLine(w, "container"), StringComparison.Ordinal);
        Assert.Contains("TODO(m6-trade-window)", AuditLine(w, "trade"), StringComparison.Ordinal);
        Assert.Contains(" windows=vendor-closed+corpse-closed ", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.Contains(notices, n => n.Contains("vendor window closed, open corpse closed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void With_an_attacker_on_him_the_fight_fights_back_first_and_the_trip_the_sort_the_worn_set_and_the_buff_pass_wait(string key)
    {
        var w = new ApWorld(key, output, held: l => l.Name == "Prismatic Taper" ? l.TripUnder / 2 : l.Keep, atHome: false) { Prime = c => Proven(c, key) };
        Hostile(w, Guard, 6);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var notices = new List<string>();
        autopilot.ChatNotice += notices.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Equal(["den-fight"], Kinds(w)); // not the trip, not the buff pass
        Assert.StartsWith("autopilot audit hostiles: in-reach: Tusker Guard 0x80000F45 at 6 m", AuditLine(w, "hostiles"), StringComparison.Ordinal);
        Assert.Contains("fix=self-defence first (D88)", AuditLine(w, "hostiles"), StringComparison.Ordinal);
        Assert.Contains("fix=deferred: a hostile is on it: the fight fights back first (D88) and its counters call the trip", AuditLine(w, "kit"), StringComparison.Ordinal);
        Assert.Contains("fix=deferred", AuditLine(w, "packs"), StringComparison.Ordinal);
        Assert.Contains("fix=deferred", AuditLine(w, "worn"), StringComparison.Ordinal);
        Assert.Contains("fix=deferred: a hostile is on it", AuditLine(w, "buffs"), StringComparison.Ordinal);
        Assert.Matches("kit=trip-deferred .* buffs=fight-first .* hostiles=in-reach ", autopilot.LastAudit);
        Assert.Contains(notices, n => n.Contains("a hostile on it: fighting back first", StringComparison.Ordinal) && n.EndsWith("; fighting back from where it stands.", StringComparison.Ordinal));
        Assert.Contains("audit: a hostile is on it (Tusker Guard 0x80000F45 at 6 m", w.Lines.First(l => l.StartsWith("autopilot state known-state -> fight", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    /// <summary>M6 7 BB: a hostile on her and the fight's own needs (health, mana) unmet: the fight starts at once, named; no keeper's pass and no precondition stands before it (live 2026-11-07: 47 hits taken in the keeper's dance, no bolt cast).</summary>
    [Fact]
    public void With_a_hostile_on_her_and_the_mana_short_the_fight_starts_at_once_with_no_need_before_it()
    {
        var w = new ApWorld(Den, output, held: l => l.Name == "Prismatic Taper" ? l.TripUnder / 2 : l.Keep, atHome: false) { Prime = c => Proven(c, Den) };
        w.Factory.FightNeeds = new StationNeeds(StationWhere.Den, MinHealthPercent: 100, MinManaPercent: 100);
        Hostile(w, Guard, 6);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("autopilot fight: station den-fight starts", StringComparison.Ordinal)), frames: 400);

        var fightBack = Assert.Single(w.Lines, l => l.StartsWith("autopilot fight-back: a hostile is on her or in reach (Tusker Guard", StringComparison.Ordinal));
        Assert.Contains("no keeper's vitals pass and no health, mana or buff need first", fightBack, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.Contains("autopilot fight: station den-fight starts", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("precondition", StringComparison.Ordinal) && l.Contains("den-fight", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Fight, autopilot.State);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_trip_due_with_no_hostile_runs_the_trip_first_and_says_so(string key)
    {
        var w = new ApWorld(key, output, held: l => l.Name == "Prismatic Taper" ? l.TripUnder / 2 : l.Keep) { Prime = c => Proven(c, key) };
        w.OwnKit();
        var autopilot = w.Autopilot(new AutopilotOptions());
        var notices = new List<string>();
        autopilot.ChatNotice += notices.Add;
        w.Run(autopilot, a => a.CurrentRun is not null);

        Assert.Equal(["logistics"], Kinds(w));
        Assert.Contains(notices, n => n.StartsWith("Autopilot: a supply trip is due (Prismatic Taper ", StringComparison.Ordinal) && n.EndsWith($"a trip to {w.Ground.Home.Town} first.", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ the chat lines and the stop

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Each_start_gets_one_chat_summary_and_a_stop_says_why_and_raises_stopped(string key)
    {
        var w = new ApWorld(key, output) { Prime = c => Proven(c, key) };
        w.OwnKit();
        var autopilot = w.Autopilot(new AutopilotOptions());
        var notices = new List<string>();
        var stopped = new List<string>();
        autopilot.ChatNotice += notices.Add;
        autopilot.Stopped += stopped.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));

        var summary = Assert.Single(notices, n => n.StartsWith("Autopilot started: ", StringComparison.Ordinal));
        Assert.Equal($"Autopilot started: no caster in its hands or packs; buffing, then the way to {w.Ground.Name}.", summary);
        Assert.DoesNotContain(notices, static n => n.StartsWith("Autopilot stopped", StringComparison.Ordinal)); // the stop is logged; the host's unlock line is the chat's one stop line (D145)
        Assert.Contains(w.Lines, l => l == AutopilotScenario.ChatLinePrefix + "(log only) Autopilot stopped (test): the fight stopped for the stop rule. The controls are yours.");
        Assert.Equal(["request:test"], stopped);
        Assert.Contains(w.Lines, l => l == AutopilotScenario.ChatLinePrefix + summary); // every chat line is logged too

        // a stop the run makes by itself says so
        var g = new ApWorld(Den, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Den, PortalTie.Untied); } }; // M6 4 t3b: the den's gap (a routed ground goes by the rung change)
        var gap = g.Autopilot(new AutopilotOptions());
        var said = new List<string>();
        var why = new List<string>();
        gap.ChatNotice += said.Add;
        gap.Stopped += why.Add;
        Assert.Equal(ScenarioStatus.Failed, g.Run(gap));
        Assert.Equal([StartupAudit.PortalGapStop], why);
        var stopLine = Assert.Single(g.Lines, static l => l.Contains("(log only) Autopilot stopped by itself", StringComparison.Ordinal));
        Assert.Contains($"Autopilot stopped by itself ({StartupAudit.PortalGapStop}): the ", stopLine, StringComparison.Ordinal);
        Assert.EndsWith(". The controls are yours.", stopLine, StringComparison.Ordinal);
        Assert.DoesNotContain(said, static n => n.StartsWith("Autopilot stopped", StringComparison.Ordinal));
    }

    [Fact]
    public void A_note_the_next_step_repeats_is_said_once_in_the_summary()
    {
        // The drill of 2026-10-30 13:45 UTC: "the cut trip runs again from its start; the cut trip runs again from its start."
        var audit = new StartupAudit(StartupAudit.ResumeWhy, 0);
        audit.Note("bound at the Holtburg lifestone");
        audit.Note("the cut trip runs again from its start");
        Assert.Equal("Autopilot resumed: bound at the Holtburg lifestone; the cut trip runs again from its start.", audit.ChatSummary("the cut trip runs again from its start"));
    }

    [Fact]
    public void The_summary_names_the_start_and_what_was_fixed_in_plain_words()
    {
        var audit = new StartupAudit(StartupAudit.ResumeWhy, 0);
        Assert.Equal("Autopilot resumed: every check passed; back to the fight from where it stands.", audit.ChatSummary("back to the fight from where it stands"));
        audit.Note("caster re-wielded ('Fire Sceptre')");
        audit.Note("magic stance set");
        audit.Note("2 buff line(s) due: renewed first");
        audit.Note("magic stance set"); // once each
        Assert.Equal("Autopilot resumed: caster re-wielded ('Fire Sceptre'), magic stance set, 2 buff line(s) due: renewed first; buffing, then back to the fight from where it stands.", audit.ChatSummary("buffing, then back to the fight from where it stands"));
        Assert.StartsWith("Autopilot back after a reconnect: ", new StartupAudit("reconnect", 0).ChatSummary("x"), StringComparison.Ordinal);
        Assert.StartsWith("Autopilot started: ", new StartupAudit("start", 0).ChatSummary("x"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ a stop in place, then a resume, in each phase

    /// <summary>The dial over the scripted world, as the play host runs it (one frame: the world's ticks, then the dial's), with the chat lines and the stops it raises.</summary>
    private sealed class Dial
    {
        public Dial(ITestOutputHelper output, string key, AutopilotOptions options, Func<StockLine, int>? held = null, bool atHome = true)
        {
            World = new ApWorld(key, output, held, atHome) { Prime = c => Proven(c, key) };
            World.OwnKit();
            Host = new AutopilotHostLogic(
                carry =>
                {
                    var scenario = World.Autopilot(carry.Remaining(options), seed: carry.Seed);
                    carry.Apply(scenario);
                    Made.Add(scenario);
                    return scenario;
                },
                World.Trip.Context().Log,
                options,
                World.Ground);
            Host.ChatNotice += Notices.Add;
            Host.Stopped += Stops.Add;
        }

        public ApWorld World { get; }

        public AutopilotHostLogic Host { get; }

        public List<AutopilotScenario> Made { get; } = [];

        public List<string> Notices { get; } = [];

        public List<string> Stops { get; } = [];

        public IGameSessionView View => World.Trip.View;

        public Action? PerFrame { get; set; }

        public bool RunUntil(Func<bool> reached, int frames = 20000)
        {
            for (var i = 0; i < frames && !reached(); i++)
            {
                var t = World.Trip;
                t.Vendor.R.Tick(0.25);
                t.Shops.Pump();
                t.Xp.Pump();
                t.Vendor.R.Tick();
                PerFrame?.Invoke();
                Host.Tick(View, World.Now);
            }

            return reached();
        }

        /// <summary>A stop in place, as <c>/stop</c> asks it today: the kill switch drops the run where it stands (its queued actions cancelled, its keys released, its fight folded in, a cut trip or death kept); then the switch is off.</summary>
        public void StopInPlace()
        {
            View.Actions.KillSwitch = true;
            RunUntil(() => Host.Mode == AutopilotMode.Halted, 4);
            Assert.Equal(AutopilotMode.Halted, Host.Mode);
            View.Actions.KillSwitch = false;
        }

        /// <summary>The resume (<c>/auto</c>): a fresh scenario reads every state a fight needs.</summary>
        public AutopilotScenario Resume()
        {
            Assert.True(Host.HandBack(View));
            var fresh = Host.Scenario!;
            Assert.Equal(StartupAudit.ResumeWhy, fresh.StartReason);
            Assert.True(RunUntil(() => fresh.Audits >= 1));
            Assert.EndsWith(" why=resume", fresh.LastAudit, StringComparison.Ordinal);
            return fresh;
        }
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Stopped_in_place_mid_fight_the_resume_audits_everything_and_fights_on_from_where_he_stands_with_no_stale_state(string key)
    {
        var d = new Dial(output, key, new AutopilotOptions());
        d.Host.Start(d.View);
        Assert.True(d.RunUntil(() => d.Host.Scenario is { State: AutopilotState.Fight, CurrentRun: IStoppableFight { Kills: >= 1 } }));
        var made = d.World.Factory.Made.Count;
        d.StopInPlace();
        Assert.Equal([AutopilotHostLogic.HaltReason], d.Stops);

        var fresh = d.Resume();
        Assert.True(d.RunUntil(() => fresh.State == AutopilotState.Fight && fresh.CurrentRun is IStoppableFight));
        Assert.DoesNotContain(d.World.Factory.Made.Skip(made), s => s.Kind is StationKinds.RecallHome or StationKinds.DenTravel); // no recall, no travel: he stands in the den
        Assert.Contains(d.World.Factory.Made.Skip(made), s => ReferenceEquals(s, AutopilotScenario.FightResumeSpec));
        Assert.Contains(d.World.Lines, l => l.StartsWith("autopilot audit actions: no automation action queued or in flight", StringComparison.Ordinal));
        Assert.Contains(d.Notices, n => n.StartsWith("Autopilot resumed: ", StringComparison.Ordinal) && n.Contains("back to the fight from where it stands", StringComparison.Ordinal));
        Assert.Equal(1, d.Host.Halts);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Stopped_in_place_mid_loot_the_resume_closes_the_half_open_corpse_and_fights_on(string key)
    {
        var d = new Dial(output, key, new AutopilotOptions());
        var answered = 0;
        d.PerFrame = () => AnswerCloses(d.World, ref answered);
        d.Host.Start(d.View);
        Assert.True(d.RunUntil(() => d.Host.Scenario is { State: AutopilotState.Fight, CurrentRun: IStoppableFight { Kills: >= 1 } }));
        OpenCorpse(d.World); // the fight's loot step had the corpse open
        d.StopInPlace();

        var fresh = d.Resume();
        Assert.Equal(0u, d.View.State.Containers.ViewedGuid);
        Assert.Contains(" windows=corpse-closed ", fresh.LastAudit, StringComparison.Ordinal);
        Assert.Contains(d.World.Lines, l => l.StartsWith("autopilot audit loot_queue: ", StringComparison.Ordinal));
        Assert.True(d.RunUntil(() => fresh.State == AutopilotState.Fight));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Stopped_in_place_on_the_trips_walk_the_resume_restarts_the_trip_from_its_start_and_it_ends_well(string key)
    {
        var d = new Dial(output, key, new AutopilotOptions { TripAfterKills = 1 }, atHome: false);
        d.Host.Start(d.View);
        Assert.True(d.RunUntil(() => d.Host.Scenario is { State: AutopilotState.Trip, CurrentRun: not null }));
        var trips = d.World.Factory.Made.Count(s => s.Kind == StationKinds.Logistics);
        d.StopInPlace();

        var fresh = d.Resume();
        Assert.Contains(d.World.Lines, l => l.StartsWith("autopilot audit kit: a trip that was cut is still to be made; fix=the trip again from its start", StringComparison.Ordinal));
        Assert.True(d.RunUntil(() => fresh.Totals.Trips + fresh.CycleTally.Trips >= 1 || d.Host.Lines.Any(l => l.Contains("state=trip", StringComparison.Ordinal))));
        Assert.Equal(trips + 1, d.World.Factory.Made.Count(s => s.Kind == StationKinds.Logistics)); // the logistics station again, from its start
        Assert.Contains(d.Host.Lines, l => l.StartsWith("autopilot cycle n=1 state=trip ", StringComparison.Ordinal));
        Assert.Contains(d.Notices, n => n.StartsWith("Autopilot resumed: ", StringComparison.Ordinal) && n.Contains("the cut trip runs again from its start", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Stopped_in_place_at_a_vendor_the_resume_closes_the_vendor_and_the_trip_runs_again(string key)
    {
        var d = new Dial(output, key, new AutopilotOptions { TripAfterKills = 1 }, atHome: false);
        d.Host.Start(d.View);
        Assert.True(d.RunUntil(() => d.Host.Scenario is { State: AutopilotState.Trip, CurrentRun: not null }));
        var vendor = d.World.Ground.Vendors[0];
        d.World.Trip.Vendor.Stock(vendor.Guid, vendor.BuysItemTypes, []); // the trip's vendor answered its Use: the window is open when the stop comes
        d.StopInPlace();
        Assert.NotNull(d.View.State.Vendor); // the stop leaves the window as it was: the resume's audit is what closes it

        var fresh = d.Resume();
        Assert.StartsWith("autopilot audit vendor: ", d.World.Lines.Last(l => l.StartsWith("autopilot audit vendor: ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains(" windows=vendor-closed ", fresh.LastAudit, StringComparison.Ordinal);
        Assert.True(d.RunUntil(() => d.Host.Lines.Any(l => l.StartsWith("autopilot cycle n=1 state=trip ", StringComparison.Ordinal))));
    }

    [Fact]
    public void Auto_with_the_resume_reason_is_a_persons_start_not_an_unattended_run_and_its_audit_is_a_resume()
    {
        var d = new Dial(output, Den, new AutopilotOptions());
        Assert.True(d.Host.Start(d.View, AutopilotHostLogic.ResumeReason));
        Assert.False(d.Host.StartedByOption);
        Assert.True(d.RunUntil(() => d.Host.Scenario!.Audits >= 1));
        Assert.EndsWith(" why=resume", d.Host.Scenario!.LastAudit, StringComparison.Ordinal);
        Assert.Contains(d.Notices, n => n.StartsWith("Autopilot resumed: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_that_stops_by_itself_raises_the_hosts_stopped_with_its_reason()
    {
        var d = new Dial(output, Den, new AutopilotOptions { Minutes = 0.5 }, atHome: false);
        d.Host.Start(d.View);
        Assert.True(d.RunUntil(() => d.Host.Mode == AutopilotMode.Done));
        Assert.Equal(["minutes"], d.Stops);
        Assert.Contains(d.Notices, n => n.StartsWith("Lifestone: the autopilot stopped (finished: stop=minutes)", StringComparison.Ordinal)); // the host's unlock line (P7m) is the chat's stop line
    }

    // ------------------------------------------------------------------------------------------------ the Flame Bolt split (D145 point 6)

    [Fact]
    public void Each_landed_damage_spell_logs_the_caster_held_and_the_spirit_drinker_aura()
    {
        var w = new ApWorld(Den, output, atHome: false) { Prime = c => Proven(c, Den) };
        var sceptre = Wielded(w);
        var state = w.Trip.Vendor.State;
        state.Self.Spellbook.Learn(SpiritDrinker.FirstSpellId + 3);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Audits >= 1, perFrame: _ => AnswerWieldsAndModes(w));
        var bus = w.Trip.Vendor.H.R.Bus;

        bus.Publish(new ChatReceived(new ChatLine(w.Now, ChatKind.System, 7, null, 0, "You scorch Tusker Guard for 123 points with Flame Bolt VI.")));
        Assert.Contains(w.Lines, l => l == $"{AutopilotScenario.HitLinePrefix}spell='Flame Bolt VI' damage=123 critical=no target='Tusker Guard' caster='Fire Sceptre' wcid={SceptreWcid} guid=0x{sceptre:X8} spirit_drinker=down");

        w.Trip.Vendor.R.Enchant((ushort)(SpiritDrinker.FirstSpellId + 3));
        bus.Publish(new ChatReceived(new ChatLine(w.Now, ChatKind.System, 7, null, 0, "Critical hit! You incinerate Tusker Guard for 250 points with Flame Bolt VI.")));
        Assert.Contains(w.Lines, l => l.StartsWith($"{AutopilotScenario.HitLinePrefix}spell='Flame Bolt VI' damage=250 critical=yes target='Tusker Guard' caster='Fire Sceptre' wcid={SceptreWcid} guid=0x{sceptre:X8} spirit_drinker=up level=4 left_s=", StringComparison.Ordinal));

        bus.Publish(new ChatReceived(new ChatLine(w.Now, ChatKind.System, 7, null, 0, "Tusker Guard scorches you for 40 points with Flame Bolt II."))); // taken, not dealt: no line
        bus.Publish(new ChatReceived(new ChatLine(w.Now, ChatKind.Speech, 7, "Someone", 1, "You scorch Tusker Guard for 9 points with Flame Bolt VI."))); // not the server's line: no line
        Assert.Equal(2, w.Lines.Count(l => l.StartsWith(AutopilotScenario.HitLinePrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_hit_line_names_no_caster_and_an_unlearned_aura_plainly()
    {
        var w = new ApWorld(Den, output, atHome: false);
        Assert.Equal(
            "autopilot hit: spell='Flame Bolt VI' damage=7 critical=no target='Tusker Slave' caster=none wcid=0 guid=0x00000000 spirit_drinker=unlearned",
            AutopilotScenario.HitLine(w.Trip.Vendor.State, "You scorch Tusker Slave for 7 points with Flame Bolt VI.", 7, false, w.Now));
    }
}

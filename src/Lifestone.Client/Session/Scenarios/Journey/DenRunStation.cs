using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// <c>den-run</c> (M6 item 14, the owner's order of 2026-11-12): one run to a Black Spawn Den's entrance, light and buffed, that never stops to fight, so that the den is reached by Primary Portal Recall ever after.
/// Every step is check, act, check (M5 D141) and fails by its own name; nothing is tried twice but what a hop already tries (its one use-again).
/// <list type="number">
/// <item><b>Check</b> (<c>check</c>): the ground (<see cref="GroundArgument"/>, by default <see cref="DefaultGround"/>) has a tie record, an entry and an exit portal, and the travel file its route <see cref="RouteKey"/>; the level is at
/// least <see cref="MinLevel"/> and passes every portal of the route and the entry; the book holds Primary Portal Tie, Primary Portal Recall and Lifestone Recall; the body is alive and stands in the route's start place. Each miss
/// refuses by name (<c>den-run:refused:*</c>) before a step is taken.</item>
/// <item><b>The road</b> (<c>road</c>), run only (<see cref="JourneyFacts.RunOnly"/>, <c>TravelLegRunOnly.cs</c>): the route's portals as hops (<see cref="TravelLeg.HopThrough"/>), then its run: the file's bend (the via point) and the
/// entry's front, each straight (<see cref="TravelLeg.RunStraight"/>). No attacker is answered and nothing holds her; a health drink is drunk on the move.</item>
/// <item><b>In</b> (<c>enter</c>): the entry portal at once (a hop: no recharge at the door, no tie before it; taken on touch counts), arriving in the den's landblock.</item>
/// <item><b>The minute</b> (<c>inside</c>): <see cref="InsideSeconds"/> at the arrival, standing; the run-only rule is off, so the autopilot's vitals guard heals her by its ordinary rules. After: alive and still in the den.</item>
/// <item><b>Out</b> (<c>exit</c>): the den's own exit portal (the profile's <c>exit</c>), a hop, run only.</item>
/// <item><b>The tie</b> (<c>tie</c>): on foot to the tie point (the profile's portal front), the portal found, the mana for the spell rested for, Primary Portal Tie cast on it, "linked with the portal" read: the journey's facts say the ground
/// is tied (<see cref="JourneyFacts.SetPrimaryPortalTie"/>).</item>
/// <item><b>Home</b> (<c>home</c>): Lifestone Recall, the arrival in the home lifestone's landblock. A recall that fails leaves the tie standing: the verdict says <c>home=failed:&lt;name&gt;</c> and the station is ok on the tie (the
/// autopilot's stop recalls home again by its own rule).</item>
/// </list>
/// A death anywhere ends the station (<c>died:&lt;leg&gt;:respawned=&lt;cell&gt;</c> from a walk, or the autopilot's own death notice): one attempt, never a second.
/// Lines: <c>den_run plan:</c>, one <c>den_run_leg: n= kind= edge= from= to= metres= s= ok=</c> a leg, and the verdict <c>den_run: ground= level= route= hops= ran_m= attackers= drinks= straight= inside_s= tied= home= ok= fail=</c>.
/// </summary>
public sealed class DenRunStation : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "den-run";

    /// <summary>The spec argument naming the ground (a catalog key).</summary>
    public const string GroundArgument = "ground";

    /// <summary>The den the run goes to: the one whose entrance a level-40 character's portals reach nearest (travel-routes.json, the route's note).</summary>
    public const string DefaultGround = "black-spawn-den-sho";

    /// <summary>The key of the run's route in the travel file (taken by name: it is never the ground's first route).</summary>
    public const string RouteKey = "den-run";

    /// <summary>The lowest level the run starts at (the owner's order: at level 40).</summary>
    public const int MinLevel = 40;

    /// <summary>The seconds she waits inside the den (the owner's order: one minute).</summary>
    public const double InsideSeconds = 60;

    /// <summary>The prefix of a refusal before any step.</summary>
    public const string RefusedPrefix = "den-run:refused:";

    /// <summary>
    /// The least chance a try of the tie and of the recall home must have on her Item Enchantment skill for the run to start (Lifestone's own: with the travel cast's three more tries after a fizzle, a half a try is 94% over the four).
    /// ACE's pre-check is <c>SkillCheck.GetMagicSkillChance</c> over the spell's power (<see cref="SpellPlan.Chance"/>): Primary Portal Tie is power 150, Lifestone Recall and Primary Portal Recall 100.
    /// </summary>
    public const double MinCastChance = 0.5;

    /// <summary>The edge whose portal a rehearsal runs toward and does not use (<see cref="JourneyFacts.DenRunRehearse"/>): the Desert March Portal at Danby's Outpost, the last harmless ground of the road (live 2026-11-13: Monouga, 3 to 5 points a hit).</summary>
    public const string RehearseBeforeEdge = "danbys-to-desert-march";

    /// <summary>How far short of that portal the rehearsal's run ends, in metres (Lifestone's own: far outside a portal's touch of 1.84 m and the walk's wobble).</summary>
    public const float RehearseShortMetres = 25f;

    /// <summary>The stop of a rehearsal that ran its road and came home.</summary>
    public const string RehearsedName = "den-run:rehearsed";

    /// <summary>A den run to <paramref name="ground"/>.</summary>
    /// <param name="ground">The ground's key, or null: <see cref="DefaultGround"/>.</param>
    public DenRunStation(string? ground = null)
    {
        Ground = string.IsNullOrEmpty(ground) ? DefaultGround : ground;
    }

    /// <summary>The ground's key.</summary>
    public string Ground { get; }

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, Ground);
    }

    /// <summary>
    /// The refusal of a den run before any step, or null: the level under <see cref="MinLevel"/>, a spell the book lacks, the pure part of the station's check that the autopilot's order reads too (one rule, two readers).
    /// </summary>
    /// <param name="level">The character's level.</param>
    /// <param name="knowsTie">The book holds the ground's Primary Portal Tie.</param>
    /// <param name="knowsRecall">The book holds the ground's Primary Portal Recall.</param>
    /// <param name="knowsLifestoneRecall">The book holds Lifestone Recall.</param>
    public static string? Refusal(int level, bool knowsTie, bool knowsRecall, bool knowsLifestoneRecall) =>
        level < MinLevel ? string.Create(CultureInfo.InvariantCulture, $"{RefusedPrefix}level:{level}<{MinLevel}")
        : !knowsTie ? RefusedPrefix + "no-spell:primary-portal-tie"
        : !knowsRecall ? RefusedPrefix + "no-spell:primary-portal-recall"
        : !knowsLifestoneRecall ? RefusedPrefix + "no-spell:lifestone-recall"
        : null;

    private sealed class Run(JourneyContext context, string groundKey) : TravelLeg(StationName, context)
    {
        private readonly List<string> _legs = [];
        private int _level;
        private int _hops;
        private double _ran;
        private double _insideSeconds;
        private bool _tied;
        private string _home = "no";
        private bool _finished;

        public override bool Ok => base.Ok && _tied;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"ground={groundKey} level={_level} route={RouteKey} hops={_hops} ran_m={_ran:0} attackers={RanFrom} drinks={RunDrinks} straight={StraightPieces}/{StraightRefused} inside_s={_insideSeconds:0} tied={(_tied ? "yes" : "no")} home={_home} ok={(_finished && _tied && Failure is null ? "yes" : "no")} fail={(_finished && Failure is null ? "none" : (FailName ?? HopStep ?? Stage).Replace(' ', '_'))}");

        protected override IEnumerable<ActionHandle?> Body()
        {
            try
            {
                foreach (var h in Steps())
                {
                    yield return h;
                }

                _finished = true;
            }
            finally
            {
                Context.Facts.RunOnly = null; // never left on by a failure
                Log("den_run: " + VerdictFields());
            }
        }

        private IEnumerable<ActionHandle?> Steps()
        {
            // ---------------------------------------------------------------- check before
            Stage = "check";
            var routes = Context.Routes;
            _level = CharacterLevel;
            if (GroundCatalog.TryGet(groundKey) is not { } den)
            {
                Fail(RefusedPrefix + "no-ground", $"no ground profile has the key '{groundKey}'");
                yield break;
            }

            if (den.Portal is not { } tieRecord || den.Entry is not { } entry || den.Exit is not { Kind: GroundExitKind.Portal, Origin: { } exitOrigin } exit)
            {
                Fail(RefusedPrefix + "profile", $"the profile {den.Key} lacks a tie record, an entry or an exit portal (portal={(den.Portal is null ? "none" : "yes")} entry={(den.Entry is null ? "none" : "yes")} exit={den.Exit?.Kind.ToString().ToLowerInvariant() ?? "none"})");
                yield break;
            }

            if (routes.RoutesFor(den.Key).FirstOrDefault(r => r.Key == RouteKey) is not { Run: { } run } route)
            {
                Fail(RefusedPrefix + "no-route", $"the travel file ({TravelRoutes.FileName}) has no route '{RouteKey}' with a run for {den.Key}");
                yield break;
            }

            var book = S.State.Self.Spellbook;
            if (DenRunStation.Refusal(_level, book.Knows(tieRecord.TieSpell), book.Knows(tieRecord.RecallSpell), book.Knows(RecallHome.LifestoneRecallSpell)) is { } refused)
            {
                Fail(refused, string.Create(CultureInfo.InvariantCulture, $"level {_level} (at least {MinLevel}); the book: Primary Portal Tie {tieRecord.TieSpell} {Yes(book.Knows(tieRecord.TieSpell))}, Primary Portal Recall {tieRecord.RecallSpell} {Yes(book.Knows(tieRecord.RecallSpell))}, Lifestone Recall {RecallHome.LifestoneRecallSpell} {Yes(book.Knows(RecallHome.LifestoneRecallSpell))}"));
            }

            // M6, the recall's components: the run's way home is Lifestone Recall, cast in the den; one the pack is short for, or ACE refused earlier this run, refuses the run before it begins (RecallReadiness).
            if (RecallReadiness.Of(S.State, Context.Facts, Context.Spells) is { Castable: false, WhyNot: { } notCastable })
            {
                Fail(RefusedPrefix + "lifestone-recall:not-castable", notCastable);
            }

            // The skill each cast of this run needs against hers (check before; the portal recall is cast on a later day, on the skill she has then).
            foreach (var (spell, word) in new[] { (tieRecord.TieSpell, "primary-portal-tie"), (RecallHome.LifestoneRecallSpell, "lifestone-recall") })
            {
                if (!Context.Spells.TryGetValue(spell, out var info) || info.School != DatReaderWriter.Enums.MagicSchool.ItemEnchantment || !GearCasts.TryCurrentSkill(S.State.Self.Qualities, MageTemplate.ItemEnchantment, out var skill, out _))
                {
                    Log($"den_run: check before: the chance of spell {spell} ({word}) is not read (the spell table or the Item Enchantment skill is not at hand): the cast's own fizzle rule decides");
                    continue;
                }

                var chance = SpellPlan.Chance(skill, info.Power);
                Log(string.Create(CultureInfo.InvariantCulture, $"den_run: check before: {info.Name}: power {info.Power} against Item Enchantment {skill}: a chance of {chance * 100:0}% a try, {MaxFizzles} more tries after a fizzle (at least {MinCastChance * 100:0}% to start)"));
                if (chance < MinCastChance)
                {
                    Fail(RefusedPrefix + "skill:" + word, string.Create(CultureInfo.InvariantCulture, $"{info.Name} (power {info.Power}) has a chance of {chance * 100:0}% a try on Item Enchantment {skill}, under {MinCastChance * 100:0}%: the run is not begun for a tie she may not land"));
                }
            }

            if (!route.Admits(_level, entry.MinLevel, entry.MaxLevel))
            {
                Fail(RefusedPrefix + "gate", string.Create(CultureInfo.InvariantCulture, $"level {_level} does not pass the route's portals and the entry (gate {route.Gate.With(entry.MinLevel, entry.MaxLevel)})"));
            }

            if (S.State.Self.Position is not { } start || S.State.Self.Travel != SelfTravel.Present)
            {
                Fail(RefusedPrefix + "not-present", $"the player is not present in the world (travel {S.State.Self.Travel})");
                yield break;
            }

            var place = routes.PlaceOf(RungRoute.Hex(start.CellId >> 16))?.Key;
            if (place != route.Start)
            {
                Fail(RefusedPrefix + "not-at-start", string.Create(CultureInfo.InvariantCulture, $"the player stands in cell 0x{start.CellId:X8} ({place ?? "no travel place"}), not in the route's start place {route.Start}"));
            }

            ReadLife(StationName, "the den run");
            var entryPortal = RungRoute.EntryPortal(den, run)!;
            var bend = run.Via is { } via && _level < via.BelowLevel ? via.Points : [];
            Log(string.Create(
                CultureInfo.InvariantCulture,
                $"den_run plan: ground={den.Key} level={_level} route={route.Key} legs=[{string.Join(",", route.Steps.Select(s => "hop:" + s.Edge.Key))},{string.Join(",", bend.Select((_, i) => "run:via-" + (i + 1).ToString(CultureInfo.InvariantCulture)))}{(bend.Count == 0 ? string.Empty : ",")}run:entry-front,enter:{entryPortal.Key},wait:{InsideSeconds:0}s,hop:exit:{den.Key},tie:{tieRecord.TieSpell},home:lifestone-recall] run_m={run.Metres:0} bends={bend.Count}; why: the owner's order: one run, never stopping to fight, the tie at the entrance, then home; purpose: after it {den.Name} is reached by Primary Portal Recall"));

            // ---------------------------------------------------------------- the road, run only
            Stage = "road";
            Context.Facts.RunOnly = $"the den run to {den.Name}";
            var n = 0;
            foreach (var step in route.Steps)
            {
                var portal = RungPortal.Of(step.Edge, routes);
                if (Context.Facts.DenRunRehearse && step.Edge.Key == RehearseBeforeEdge)
                {
                    foreach (var h in Rehearse(++n, portal))
                    {
                        yield return h;
                    }

                    yield break;
                }

                foreach (var h in Hop(++n, portal, $"{route.Key} to {den.Key}, run only"))
                {
                    yield return h;
                }
            }

            var keepOff = entryPortal.KeepOffTheOthers();
            var arrival = den.ArrivalLandblock();
            var enterMark = TravelMark;
            var taken = false;
            for (var i = 0; i <= bend.Count && !taken; i++)
            {
                var last = i == bend.Count;
                var (label, cell, at) = last ? ("entry-front", entry.FrontCell, entry.Front) : (string.Create(CultureInfo.InvariantCulture, $"via-{i + 1}"), bend[i].Cell, bend[i].Origin);
                HopStep = "run:" + label;
                var from = S.State.Self.Position;
                var began = S.Clock.Now;
                var ok = false;
                try
                {
                    foreach (var h in RunStraight("run", StationName, label, cell, at, last ? $"to the front of {den.Name}'s entry portal, run only" : $"{label} of the run to {den.Name}: {bend[i].Why}", arrival, keepOff))
                    {
                        yield return h;
                    }

                    ok = true;
                    taken = WalkTakenFrom is not null;
                }
                finally
                {
                    var to = WalkTakenFrom ?? S.State.Self.Position;
                    var metres = from is { } a && to is { } b && MetresBetween(a, b) is var m && !double.IsNaN(m) ? m : 0;
                    _ran += metres;
                    LegLine(++n, "run", label, from, S.State.Self.Position, metres, began, ok, taken ? " taken=touch" : string.Empty);
                }
            }

            // ---------------------------------------------------------------- in, at once
            Stage = "enter";
            if (taken)
            {
                HopStep = entryPortal.Key + ":use";
                Log($"hop {entryPortal.Key}: {entryPortal.Name} took the player on touch during the run (taken=touch): no Use is sent; the arrival is checked as a Use's");
                var began = S.Clock.Now;
                foreach (var h in ArriveNamed(arrival, enterMark, "hop:" + entryPortal.Key))
                {
                    yield return h;
                }

                _hops++;
                LegLine(++n, "enter", entryPortal.Key, null, S.State.Self.Position, 0, began, true, " taken=touch");
            }
            else
            {
                foreach (var h in Hop(++n, entryPortal, $"into {den.Name} at once: no stop outside, no tie before the door", "enter"))
                {
                    yield return h;
                }
            }

            HopStep = null;

            // ---------------------------------------------------------------- the minute inside
            Stage = "inside";
            Context.Facts.RunOnly = null; // standing by design: the autopilot's vitals guard heals her by its ordinary rules
            var inside = S.State.Self.Position;
            var entered = S.Clock.Now;
            Log(string.Create(CultureInfo.InvariantCulture, $"den_run: inside: check before: present in {CellText(inside)} (the arrival of {den.Key}, cell 0x{den.Arrival.Cell:X8}); she stands {InsideSeconds:0} s; the run-only rule is off and the vitals guard heals her by its ordinary rules; health {Vital(MageTemplate.MaxHealth)}, mana {Vital(MageTemplate.MaxMana)}"));
            foreach (var h in Until(() => S.Clock.Now - entered >= InsideSeconds, InsideSeconds * 4)) // the clock's minute: the frames a guard's heal held the station count too
            {
                yield return h;
            }

            _insideSeconds = S.Clock.Now - entered;
            var after = S.State.Self.Position;
            var alive = !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) || health > 0;
            if (!alive || after is not { } still || !den.InLandblocks(still.CellId))
            {
                Fail("den-run:inside:" + (alive ? "moved" : "dead"), string.Create(CultureInfo.InvariantCulture, $"after {_insideSeconds:0} s inside the player stands in {CellText(after)} with health {Vital(MageTemplate.MaxHealth)}, not alive in {den.Key}'s landblock"));
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"den_run: inside: check after: {_insideSeconds:0} s in {CellText(after)}; health {Vital(MageTemplate.MaxHealth)}, mana {Vital(MageTemplate.MaxMana)}"));

            // ---------------------------------------------------------------- out by the den's own exit
            Stage = "exit";
            Context.Facts.RunOnly = $"the den run: out of {den.Name} to its entrance";
            var exitPortal = new RungPortal("exit:" + den.Key, den.Name + " exit", exit.Wcid, 0, exit.Cell, exitOrigin, exit.Destination >> 16, null, null, den.Landblocks);
            foreach (var h in Hop(++n, exitPortal, $"out of {den.Name} by its own exit, to tie at the entrance"))
            {
                yield return h;
            }

            // ---------------------------------------------------------------- the tie at the entrance
            Stage = "tie";
            HopStep = "tie:walk";
            var tieFrom = S.State.Self.Position;
            var tieBegan = S.Clock.Now;
            foreach (var h in WalkGuarded("tie", StationName, "tie-point", tieRecord.FrontCell, tieRecord.Front, TravelPurpose.Cell, $"to the tie point 3 m in front of {den.Name}'s entrance, to cast Primary Portal Tie"))
            {
                yield return h;
            }

            Context.Facts.RunOnly = null; // the cast needs her standing
            HopStep = "tie:find";
            var portalCell = CellAt(tieRecord.Cell, tieRecord.Origin);
            var found = 0u;
            foreach (var h in Until(() => (found = FindPortalObject(tieRecord.Guid, tieRecord.Wcid, portalCell, tieRecord.Origin)) != 0, FindSeconds))
            {
                yield return h;
            }

            if (found == 0)
            {
                Fail("den-run:tie:no-portal", string.Create(CultureInfo.InvariantCulture, $"no object of weenie {tieRecord.Wcid} (guid 0x{tieRecord.Guid:X8}) near the entrance within {FindSeconds:0} s"));
            }

            HopStep = "tie:mana";
            foreach (var h in RestForSpell(tieRecord.TieSpell))
            {
                yield return h;
            }

            if (!SpellManaMet)
            {
                Fail("den-run:tie:no-mana", string.Create(CultureInfo.InvariantCulture, $"the mana did not come back for Primary Portal Tie {tieRecord.TieSpell} within {SpellManaSeconds:0} s (mana {Vital(MageTemplate.MaxMana)})"));
            }

            HopStep = "tie:cast";
            foreach (var h in MagicNow())
            {
                yield return h;
            }

            var tie = new CastResult();
            foreach (var h in Cast(tieRecord.TieSpell, found, tie))
            {
                yield return h;
            }

            foreach (var h in Until(() => TextSince(tie.Mark, "linked with the portal") || Refusal(this, tie) != 0, TextSeconds))
            {
                yield return h;
            }

            if (!TextSince(tie.Mark, "linked with the portal"))
            {
                var refusal = Refusal(this, tie);
                Fail("den-run:tie:not-linked", $"Primary Portal Tie on {den.Name}'s entrance 0x{found:X8} did not link ({(refusal != 0 ? Named(refusal) : Describe(tie.Handle!))})");
            }

            _tied = true;
            Context.Facts.SetPrimaryPortalTie(den.Key);
            Log($"den_run: tie: check after: linked with {den.Name}'s entrance 0x{found:X8}: {den.Key} is tied (the primary tie is one slot: every other ground's tie is untied); Primary Portal Recall {tieRecord.RecallSpell} lands at its arrival from now on");
            LegLine(++n, "tie", "entry:" + den.Key, tieFrom, S.State.Self.Position, 0, tieBegan, true, " tied=yes");

            // ---------------------------------------------------------------- home
            Stage = "home";
            HopStep = "home:recall";
            var homeBegan = S.Clock.Now;
            string? homeFailed = null;
            foreach (var h in Catching(RecallHomeNow(), f => homeFailed = f))
            {
                yield return h;
            }

            if (homeFailed is not null)
            {
                // The tie is made: the station stays ok on it, and the failure is named for the autopilot's stop, whose own recall home tries again.
                _home = "failed:" + (FailName ?? "den-run:home:recall").Replace(' ', '_');
                FailName = null;
                Context.Log.Warn("journey", $"[live {StationName}] home: Lifestone Recall after the tie failed ({homeFailed}): the tie stands (tied=yes); the autopilot's stop recalls home by its own rule");
            }
            else
            {
                _home = "recall";
            }

            LegLine(++n, "home", "lifestone-recall", null, S.State.Self.Position, 0, homeBegan, homeFailed is null, string.Empty);
            HopStep = null;
        }

        /// <summary>
        /// The rehearsal's last leg, check, act, check: before, the body outdoors at the hop's drop and the point <see cref="RehearseShortMetres"/> short of <paramref name="portal"/> on the dats' ground; the act, the run-only straight
        /// run to it (the road's own rules: no answer, no hold, a drink only under the line); after, the leg's line with its metres, seconds and metres a second. Then the run-only rule off, Lifestone Recall home, and the stop
        /// <see cref="RehearsedName"/>. The portal is never used.
        /// </summary>
        private IEnumerable<ActionHandle?> Rehearse(int n, RungPortal portal)
        {
            HopStep = "rehearse:" + portal.Key;
            var from = S.State.Self.Position;
            var began = S.Clock.Now;
            var goal = new CellFrame(portal.Cell, new AcFrame(portal.Origin, Quaternion.Identity));
            var noGround = "the body's place is not read";
            if (from is not { } here || PointShortOf(here, goal, RehearseShortMetres, out noGround) is not { } end)
            {
                Fail("den-run:rehearse:no-ground", string.Create(CultureInfo.InvariantCulture, $"the rehearsal's point {RehearseShortMetres:0} m short of {portal.Name} (cell 0x{portal.Cell:X8}) is not read on the dats' ground from {CellText(from)}: {noGround}"));
                yield break;
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"den_run: rehearsal: check before: from {CellText(from)} a run-only straight run to {CellText(end)}, {RehearseShortMetres:0} m short of {portal.Name} (not used); health {Vital(MageTemplate.MaxHealth)}"));
            var ok = false;
            try
            {
                foreach (var h in RunStraight("rehearse", StationName, "short-of-" + portal.Key, end.CellId, end.Frame.Origin, $"the rehearsal's run toward {portal.Name}, run only, ending {RehearseShortMetres:0} m short of it", null, portal.KeepOffTheOthers()))
                {
                    yield return h;
                }

                ok = true;
            }
            finally
            {
                var to = S.State.Self.Position;
                var metres = from is { } a && to is { } b && MetresBetween(a, b) is var m && !double.IsNaN(m) ? m : 0;
                var seconds = S.Clock.Now - began;
                _ran += metres;
                LegLine(n, "rehearse", portal.Key, from, to, metres, began, ok, string.Create(CultureInfo.InvariantCulture, $" mps={(seconds > 0 ? metres / seconds : 0):0.00}"));
            }

            Context.Facts.RunOnly = null; // the recall needs her standing
            Stage = "home";
            HopStep = "home:recall";
            var homeBegan = S.Clock.Now;
            foreach (var h in RecallHomeNow())
            {
                yield return h;
            }

            _home = "recall";
            LegLine(n + 1, "home", "lifestone-recall", null, S.State.Self.Position, 0, homeBegan, true, string.Empty);
            HopStep = null;
            Fail(RehearsedName, string.Create(CultureInfo.InvariantCulture, $"the rehearsal ran its road as far as {portal.Name}'s approach and came home by order: {_hops} hop(s), {_ran:0} m, {RanFrom} attacker(s) not answered, {RunDrinks} drink(s); no tie was meant"));
        }

        private IEnumerable<ActionHandle?> RecallHomeNow()
        {
            var home = HomeLifestone.Cell >> 16;
            foreach (var h in RestForSpell(RecallHome.LifestoneRecallSpell))
            {
                yield return h;
            }

            foreach (var h in MagicNow())
            {
                yield return h;
            }

            var cast = new CastResult();
            foreach (var h in Cast(RecallHome.LifestoneRecallSpell, 0, cast))
            {
                yield return h;
            }

            foreach (var h in Until(() => Refusal(this, cast) != 0 || TeleportSince(cast.TravelMark), TeleportStartSeconds))
            {
                yield return h;
            }

            var refusal = Refusal(this, cast);
            if (refusal != 0 && !TeleportSince(cast.TravelMark))
            {
                if (refusal == 0x049E)
                {
                    Context.Facts.LifestoneTied = false;
                }

                Fail("den-run:home:refused", "Lifestone Recall was refused: " + Named(refusal));
            }

            foreach (var h in ArriveNamed(home, cast.TravelMark, "den-run:home"))
            {
                yield return h;
            }

            Context.Facts.LifestoneTied = true;
            Context.Facts.Recalls++;
            foreach (var h in PeaceNow())
            {
                yield return h;
            }
        }

        /// <summary>One portal of the run, by the hop's own checks (<see cref="TravelLeg.HopThrough"/>), with its leg line.</summary>
        private IEnumerable<ActionHandle?> Hop(int n, RungPortal portal, string why, string kind = "hop")
        {
            var from = S.State.Self.Position;
            var began = S.Clock.Now;
            var outcome = new HopOutcome();
            var ok = false;
            try
            {
                foreach (var h in HopThrough(portal, outcome, why))
                {
                    yield return h;
                }

                ok = true;
                _hops++;
            }
            finally
            {
                _ran += outcome.WalkMetres;
                LegLine(n, kind, portal.Key, from, S.State.Self.Position, outcome.WalkMetres, began, ok, (outcome.Taken ? " taken=yes" : string.Empty) + (outcome.Recoveries > 0 ? " recovery=use-again" : string.Empty));
            }
        }

        private void LegLine(int n, string kind, string edge, CellFrame? from, CellFrame? to, double metres, double began, bool ok, string extra)
        {
            var line = string.Create(CultureInfo.InvariantCulture, $"den_run_leg: n={n} kind={kind} edge={edge} from={Lb(from)} to={Lb(to)} metres={metres:0} s={S.Clock.Now - began:0.0} ok={(ok ? "yes" : "no")} attackers={RanFrom} drinks={RunDrinks} health={Vital(MageTemplate.MaxHealth)}{extra}");
            _legs.Add(line);
            Log(line);
        }

        private static string Lb(CellFrame? at) => at is { } p ? RungRoute.Hex(p.CellId >> 16) : "none";

        private static string CellText(CellFrame? at) => at is { } p ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{p.CellId:X8} ({p.Frame.Origin.X:0.#}, {p.Frame.Origin.Y:0.#}, {p.Frame.Origin.Z:0.#})") : "no cell";

        private string Vital(uint id) => S.State.Self.Qualities.TryGetVitalLevels(id, out var cur, out var max) ? string.Create(CultureInfo.InvariantCulture, $"{cur}/{max}") : "unread";

        private static string Yes(bool b) => b ? "yes" : "no";
    }
}

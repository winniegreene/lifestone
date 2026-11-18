using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The start-up state audit (M5 7, the owner's rule: make very sure the autopilot can resume from nearly any state, so the mage makes sure it has a lifestone and portal tie and components, and fixes it if not). One audit runs at every autopilot
/// start and after every reconnect, before the first fight, inside the known state (<see cref="AutopilotScenario"/>); it holds the findings of its checks and the summary line. The checks, in the order their fixes run: a corpse of the mage's
/// still to recover (the death path first: the recovery brings the kit back), where the mage is (<see cref="AutopilotScenario.Classify"/>), the lifestone bind, the portal tie, the components and the kit, then the buffs and the vitae. A check
/// that passes reads only; each logs one line with its finding and the fix it chose.
/// </summary>
public sealed class StartupAudit
{
    /// <summary>The log prefix of the audit's lines.</summary>
    public const string LinePrefix = "autopilot audit";

    /// <summary>
    /// Seconds the kit check waits for the pack's list and the held items' descriptions after a login (Lifestone's own; under the known state's watchdog bound of 90 s): past it the kit is <c>unread</c>, a named gap, and the run goes on (the
    /// fight's own counters judge the stock once it reads).
    /// </summary>
    public const double KitReadSeconds = 15;

    /// <summary>
    /// The stop name when the ground's portal is known untied and no route reaches its front without a mod place (<c>TODO(m5-portal-retie-route)</c>). M6 4 t3b narrows it to a ground whose travel-file route has no portal and no run (the den):
    /// a routed ground goes by the rung change (<see cref="AutopilotTravelRule.Out"/>, the same rule the Travel state reads), whose failure is its own <c>failed:travel:rung-change:&lt;fail&gt;</c>.
    /// </summary>
    public const string PortalGapStop = "failed:audit:portal-untied";

    /// <summary>The refusal ACE sends when Primary Portal Recall is cast with no tie (<c>YouMustLinkToPortalToRecall</c>, @95ef426d ACE.Server <c>WorldObject_Magic.cs</c> 1107-1112, ACE.Entity <c>WeenieError.cs</c> 683).</summary>
    public const string NoPortalTieRefusal = "0x04A3";

    /// <summary>An audit begun for <paramref name="why"/> at <paramref name="startedAt"/>.</summary>
    /// <param name="why">What began it: <c>start</c>, <c>reconnect</c> or <c>resume</c> (M5 7 P7n: <c>/auto</c> or a hand-back).</param>
    /// <param name="startedAt">The session clock.</param>
    public StartupAudit(string why, double startedAt)
    {
        Why = why;
        StartedAt = startedAt;
    }

    /// <summary>What began the audit: <c>start</c>, <c>reconnect</c> or <c>resume</c> (M5 7 P7n, D145).</summary>
    public string Why { get; }

    /// <summary>The session clock when it began.</summary>
    public double StartedAt { get; }

    /// <summary>The place check's finding (<c>home</c>, <c>ground</c>, <c>elsewhere</c>, <c>dead</c>, <c>unknown</c>).</summary>
    public string Place { get; set; } = "unknown";

    /// <summary>The bind check's finding: <c>proven</c>, <c>bound-now</c> or <c>unknown</c>.</summary>
    public string Bind { get; set; } = "unknown";

    /// <summary>The portal check's finding: <c>proven</c>, <c>pending</c> (the den travel's recall proves it), <c>route</c> (known untied: the rung change's route takes the climber, M6 4 t3b), <c>gap</c> or <c>none</c> (a ground with no portal).</summary>
    public string PortalTie { get; set; } = "pending";

    /// <summary>The kit check's finding: <c>ok</c>, <c>trip</c>, <c>gap</c> or <c>unread</c>.</summary>
    public string Kit { get; set; } = "unread";

    /// <summary>The corpse check's finding: <c>none</c> or <c>recover</c>.</summary>
    public string Corpse { get; set; } = "none";

    /// <summary>The buff check's finding: <c>pass</c> (enchantments up: the fight refreshes the due lines between kills) or <c>buff</c> (the buff pass runs first).</summary>
    public string Buffs { get; set; } = "pass";

    /// <summary>M5 7 D121: the packs check's finding: <c>pending</c> before it ran, then the sort's <c>&lt;moved&gt;/&lt;planned&gt;</c> (<see cref="Lifestone.Client.Session.Mage.PackSorter.Finding"/>), or <c>unread</c> when the kit never read.</summary>
    public string Packs { get; set; } = "pending";

    /// <summary>M5 7 S2d: the pieces the worn-set check put back on (<see cref="Lifestone.Client.Session.Mage.WornRestorer"/>), or -1 before it ran.</summary>
    public int WornRestored { get; set; } = -1;

    /// <summary>True once the bind station ran for this audit (it runs once).</summary>
    public bool BindTried { get; set; }

    /// <summary>True once the kit check started a trip for this audit (it starts one).</summary>
    public bool TripTried { get; set; }

    /// <summary>True once the corpses in view were looked at (report-only, once an audit).</summary>
    public bool CorpsesLooked { get; set; }

    /// <summary>M5 7 P7c: the wait on the pack's list and the held items' descriptions (the kit check).</summary>
    public const string WaitKit = "kit";

    /// <summary>
    /// M6 6 MF (<c>m5-audit-kit-unread</c>): the kit's one re-ask after <see cref="KitReadSeconds"/>. ACE sends the pack's lists and the held items' descriptions only at login (@95ef426d ACE.Server <c>Player_Networking.cs</c> 241-262,
    /// <c>SendInventoryAndWieldedItems</c>) and a Use of an owned pack opens nothing (<c>Container.cs</c> 708-709), so no request makes the server send them again: the re-ask is one more read of the client's model, at most
    /// <see cref="KitReadSeconds"/> more, logged once; then the kit is named <c>unread</c>.
    /// </summary>
    public const string WaitKitReask = "kit-reask";

    /// <summary>M5 7 P7c: the wait on the pack sort's moves.</summary>
    public const string WaitPacks = "packs";

    /// <summary>M5 7 P7c: the wait on the worn-set restore (its appraisals of the held casters and wearables, then its wears).</summary>
    public const string WaitWorn = "worn";

    /// <summary>
    /// M5 7 P7c (D139 row h): the step the audit waits on (<see cref="WaitKit"/>, <see cref="WaitPacks"/> or <see cref="WaitWorn"/>), or null. While it is set the audit's frames resume at that step: the checks before it ran once and
    /// are not run (or logged) again. Round 13 (2026-10-30 03:01:31-33 UTC) logged the place, bind, portal and kit lines 94 times each while the worn-set restore appraised 10 casters.
    /// </summary>
    public string? WaitingOn { get; private set; }

    /// <summary>The session clock when <see cref="WaitingOn"/> was set.</summary>
    public double WaitStartedAt { get; private set; }

    /// <summary>The runs of the checks from the top (the place, the bind, the portal tie): one a pass, never one a frame of a wait.</summary>
    public int Evaluations { get; set; }

    /// <summary>The waits begun (each logged once).</summary>
    public int Waits { get; private set; }

    /// <summary>Begins the wait on <paramref name="what"/>; true when it is new (the caller logs its one line), false while the same wait goes on.</summary>
    /// <param name="what">The step waited on.</param>
    /// <param name="now">The session clock.</param>
    public bool BeginWait(string what, double now)
    {
        if (WaitingOn == what)
        {
            return false;
        }

        WaitingOn = what;
        WaitStartedAt = now;
        Waits++;
        return true;
    }

    /// <summary>Ends the wait (the step it waited on is in).</summary>
    public void EndWait() => WaitingOn = null;

    /// <summary>The one line of a wait: <c>autopilot audit waits: &lt;what&gt;: &lt;detail&gt;</c>.</summary>
    /// <param name="what">The step waited on.</param>
    /// <param name="detail">What it waits for.</param>
    public static string WaitLine(string what, string detail) => $"{LinePrefix} waits: {what}: {detail}";

    // ------------------------------------------------------------------------------------------------ M5 7 P7n (D145 point 4): the full state audit

    /// <summary>D145: the reason of a start by <c>/auto</c> (a resume after <c>/stop</c>, or a hand-back): the audit's <see cref="Why"/>.</summary>
    public const string ResumeWhy = "resume";

    /// <summary>D145: the wait on the actions the session still has queued or in flight at the audit's start (the stopped run's, or the person's).</summary>
    public const string WaitActions = "actions";

    /// <summary>D145: the wait on the close of an open corpse or chest.</summary>
    public const string WaitWindows = "windows";

    /// <summary>D145: the wait on the caster's wield (the recovery table's <see cref="RecoveryFix.WieldCaster"/> fix).</summary>
    public const string WaitCaster = "caster";

    /// <summary>D145: the wait on the magic mode asked of the server.</summary>
    public const string WaitStance = "stance";

    /// <summary>D145: the seconds the audit waits for the session's queued and in-flight actions to end before it reads the state (Lifestone's own: the Action API's longest timeout is a pick-up's 25 s); past it the audit goes on, named.</summary>
    public const double ActionsSettleSeconds = 30;

    /// <summary>D145: the seconds a close of a corpse or the magic mode may take before the audit names the gap and goes on (the Action API's own timeouts are 5 s; Lifestone's own).</summary>
    public const double FixActionSeconds = 10;

    /// <summary>The alive check's finding: <c>alive</c>, <c>dead</c> or <c>unknown</c> (no health known).</summary>
    public string Alive { get; set; } = "unread";

    /// <summary>The portal-space check's finding: <c>present</c> or <c>portal-space</c> (the known state waits for the arrival, <see cref="AutopilotScenario.PresentSeconds"/>).</summary>
    public string Travel { get; set; } = "unread";

    /// <summary>The hostiles check's finding: <c>clear</c>, <c>in-reach</c> or <c>attacked</c>.</summary>
    public string Hostiles { get; set; } = "unread";

    /// <summary>What the hostiles check found on the mage (an attacker or a target creature in reach), or null: the slow fixes are deferred and the fight fights back first (D88).</summary>
    public string? Attacker { get; set; }

    /// <summary>The windows check's finding: <c>none</c>, or what it closed (<c>vendor-closed</c>, <c>corpse-closed</c>, both joined by <c>+</c>), or <c>gap</c>.</summary>
    public string Windows { get; set; } = "unread";

    /// <summary>The caster check's finding: <c>pick</c> (held, the gear plan's pick), <c>held</c> (held, another would beat it), <c>rewielded</c>, <c>none</c> (no caster anywhere) or <c>gap</c>.</summary>
    public string Caster { get; set; } = "unread";

    /// <summary>The stance check's finding: <c>magic</c>, <c>magic-set</c>, <c>left</c> (the next station sets its own) or <c>gap</c>.</summary>
    public string Stance { get; set; } = "unread";

    /// <summary>The buff lines due under the re-buff margin when the buff check read them (0 when none, or when the armour memory was not seeded).</summary>
    public int BuffsDue { get; set; }

    /// <summary>The Spirit Drinker check's finding: <c>unlearned</c>, <c>up:&lt;level&gt;</c> or <c>down</c>.</summary>
    public string Spirit { get; set; } = "unread";

    /// <summary>The vitals check's finding: <c>ok</c>, <c>heal</c> (the heal in place first) or <c>unread</c>.</summary>
    public string Vitals { get; set; } = "unread";

    /// <summary>True when the vitals check asks for the heal in place before the fight.</summary>
    public bool HealFirst { get; set; }

    /// <summary>The loot-queue check's finding: the creature corpses in view (the stopped fight's pairs come from the checkpoint: M6 6 F3).</summary>
    public int CorpsesInView { get; set; } = -1;

    /// <summary>True once the actions check ran (once an audit: the stopped run's actions settle before anything is read).</summary>
    public bool ActionsSettled { get; set; }

    /// <summary>True once the place check sent its chat notice for this audit (dead, elsewhere).</summary>
    public bool PlaceNoticed { get; set; }

    /// <summary>True once the portal-space wait was logged for this audit.</summary>
    public bool TravelLogged { get; set; }

    /// <summary>Per pass from the top (<see cref="BeginPass"/>): the windows step ran.</summary>
    public bool WindowsDone { get; set; }

    /// <summary>Per pass: the kit step ran.</summary>
    public bool KitDone { get; set; }

    /// <summary>Per pass: the caster step ran.</summary>
    public bool CasterDone { get; set; }

    /// <summary>Per pass: the stance step ran.</summary>
    public bool StanceDone { get; set; }

    /// <summary>M6 6 MF (2-R12): per pass, the gear floor's stop step ran (after the kit step and its trip: the stop is judged after the trip's sale).</summary>
    public bool FloorDone { get; set; }

    private readonly List<string> _notes = [];

    /// <summary>The fixes made and the gaps found, in plain words, for the chat summary (<see cref="ChatSummary"/>).</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Adds a plain phrase to the chat summary (once each).</summary>
    /// <param name="note">What was fixed or found.</param>
    public void Note(string note)
    {
        if (!_notes.Contains(note))
        {
            _notes.Add(note);
        }
    }

    /// <summary>A pass of the checks from the top begins: the per-pass steps run again (the once-an-audit steps, the packs and the worn set, do not).</summary>
    public void BeginPass() => (WindowsDone, KitDone, CasterDone, StanceDone, FloorDone) = (false, false, false, false, false);

    /// <summary>
    /// D145: the one line for the chat window when the audit ends: how the autopilot started, what the audit put right or found (<see cref="Notes"/>), and what it does next, in plain words (the owner's example, in effect: the autopilot resumed, the
    /// caster re-wielded, war stance, 2 buffs renewed, back to the den spot).
    /// </summary>
    /// <param name="next">What the autopilot does next.</param>
    public string ChatSummary(string next)
    {
        var verb = Why switch
        {
            ResumeWhy => "Autopilot resumed",
            "reconnect" => "Autopilot back after a reconnect",
            _ => "Autopilot started",
        };
        var notes = _notes.Where(n => n != next).ToList(); // a note the next step repeats is said once (the drill's "the cut trip runs again from its start" twice)
        var found = notes.Count == 0 ? "every check passed" : string.Join(", ", notes);
        return $"{verb}: {found}; {next}.";
    }

    /// <summary>The summary line.</summary>
    public string Summary() => $"{LinePrefix}: place={Place} bind={Bind} portal_tie={PortalTie} kit={Kit} corpse={Corpse} buffs={Buffs} packs={(Packs == "pending" ? "unread" : Packs)} worn_restored={(WornRestored < 0 ? "unread" : WornRestored.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
        + string.Create(CultureInfo.InvariantCulture, $" alive={Alive} travel={Travel} hostiles={Hostiles} windows={Windows} caster={Caster} stance={Stance} buffs_due={BuffsDue} spirit={Spirit} vitals={Vitals} corpses_in_view={(CorpsesInView < 0 ? "unread" : CorpsesInView.ToString(CultureInfo.InvariantCulture))} why={Why}");

    /// <summary>One check's line: <c>autopilot audit &lt;check&gt;: &lt;finding&gt;; fix=&lt;fix&gt;</c>.</summary>
    /// <param name="check">The check's name.</param>
    /// <param name="finding">What it found and what proved it.</param>
    /// <param name="fix">The fix it chose (<c>none</c> when it passed).</param>
    public static string Line(string check, string finding, string fix) => $"{LinePrefix} {check}: {finding}; fix={fix}";

    /// <summary>
    /// The kit check's reading of <paramref name="state"/>: the ground's stock lines through <paramref name="counters"/> (the logistics counters' own counts and verdict) and the kit lines and the mage's foci by weenie, wherever the player holds them
    /// (worn, wielded, the main pack, the side packs and what is in them).
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="ground">The ground (its supplies).</param>
    /// <param name="counters">The ground's logistics counters.</param>
    /// <param name="lootOn">M6 2 f (2-R1): the fight's loot setting (<see cref="AutopilotOptions.LootOn"/>); off, a short floor is a named note and no trip (<see cref="ReadFloor"/>).</param>
    /// <param name="saleMade">M6 6 MF (2-R12): true once a trip's sale has run for this reading (the audit's trip); see <see cref="ReadFloor"/>.</param>
    public static KitReading ReadKit(GameState state, HuntingGround ground, LogisticsCounters counters, bool lootOn = true, bool saleMade = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(counters);
        var verdict = counters.Check(state);
        if (verdict.Kind == LogisticsKind.Unread)
        {
            return new KitReading(verdict, true, [], [], [], []);
        }

        var shortLines = new List<string>();
        var lowLines = new List<string>();
        var carriedShort = new List<string>();
        var atFloor = new List<string>();
        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            var held = counters.HeldOf(i);
            if (counters.IsSuspended(i))
            {
                continue; // M6 14 pack rules: the account formula's line while every focus is held: no stop, no trip
            }

            if (held < line.StopUnder)
            {
                (counters.IsFloorOnly(i) ? atFloor : counters.IsCarried(i) ? carriedShort : shortLines).Add(string.Create(CultureInfo.InvariantCulture, $"{line.Name} {held}<{line.StopUnder}"));
            }
            else if (!counters.IsCarried(i) && held < line.TripUnder)
            {
                lowLines.Add(string.Create(CultureInfo.InvariantCulture, $"{line.Name} {held}<{line.TripUnder}"));
            }
        }

        // M6 6 MF (m5-audit-kit-restock): a kit line that is a caster or a robe goes through item 2's gear floor (GearFloor: two casters, a chest-covering piece), which a loot trip already buys; the rest (the gloves, the packs, the foci)
        // stays named. A line the floor covers (two casters held, the chest covered) is not missing.
        var missing = new List<string>();
        var floorRouted = new List<string>();
        var kitRest = new List<string>();
        foreach (var line in ground.Supplies.Kit)
        {
            var held = CountHeld(state, line.Wcid);
            if (held < line.Count)
            {
                var what = string.Create(CultureInfo.InvariantCulture, $"{line.Name} {held}/{line.Count}");
                if (FloorRoute(state, ground, line.Wcid, lootOn, out var covered) is { } route)
                {
                    floorRouted.Add(what + ": " + route);
                    if (!covered)
                    {
                        missing.Add(what);
                    }
                }
                else
                {
                    missing.Add(what);
                    kitRest.Add(what);
                }
            }
        }

        if (ground.Supplies.Kit.Count > 0)
        {
            foreach (var focus in GearKit.MageFociWcids)
            {
                if (CountHeld(state, focus) == 0)
                {
                    var what = string.Create(CultureInfo.InvariantCulture, $"focus {focus} 0/1");
                    missing.Add(what);
                    kitRest.Add(what);
                }
            }
        }

        // M6 item 0 F2 (belt's finding): a ground with no worn kit (Winnie's) still casts on the foci formula while the starter foci are in the pack (ACE Player.HasFoci, Player_Spells.cs 458-488 @95ef426d); the pack losing one (a death's drop, a sale)
        // changes the formula under the stock lines, so the foci the character's casts assume are audited here and a missing one is a stop by name (TODO(m6-account-formula-plan): planning with the account's full formula instead).
        var fociMissing = new List<string>();
        if (ground.Supplies.Kit.Count == 0)
        {
            foreach (var focus in AssumedFoci(state))
            {
                if (CountHeld(state, focus) == 0)
                {
                    fociMissing.Add(GearKit.FociName(focus));
                    missing.Add(string.Create(CultureInfo.InvariantCulture, $"focus {focus} 0/1"));
                    kitRest.Add(missing[^1]);
                }
            }
        }

        var (floorShort, floorStop, floorNote) = ReadFloor(state, ground, lootOn, saleMade);
        return new KitReading(verdict, false, shortLines, lowLines, carriedShort, missing, atFloor) { FociMissing = fociMissing, FloorShort = floorShort, FloorStop = floorStop, FloorNote = floorNote, FloorRouted = floorRouted, KitRest = kitRest };
    }

    /// <summary>
    /// M6 6 MF (<c>m5-audit-kit-restock</c>): the gear floor's word on a kit line of weenie <paramref name="wcid"/>, or null when the floor does not buy that kind (a glove, a pack, a focus: <see cref="GearFloor.IsFloorWare"/> is false). A caster:
    /// covered when <see cref="GearFloor.CastersWanted"/> casters are held, else the floor's caster lines (<see cref="GearFloor.Plan"/>); a robe: covered when a worn piece covers the chest (<see cref="GearFloor.ChestPiece"/>), else the floor's
    /// robe line. A line the floor cannot buy says why (the floor level, the stop, the note). A read: nothing is bought here; a loot trip buys the floor (the resupply's <c>PlanFloor</c>).
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="ground">The ground (its vendors).</param>
    /// <param name="wcid">The kit line's weenie.</param>
    /// <param name="lootOn">The fight's loot setting: the floor is bought only on a loot trip.</param>
    /// <param name="covered">True when the floor already covers the line (nothing is missing).</param>
    internal static string? FloorRoute(GameState state, HuntingGround ground, uint wcid, bool lootOn, out bool covered)
    {
        covered = false;
        if (CasterManaRule.ChargeMana.ContainsKey(wcid) || !GearFloor.IsFloorWare(wcid) || GearKit.FloorWeenieOf(wcid) is not { } ware)
        {
            return null;
        }

        var caster = (ware.ItemType & SelfEquipJudge.CasterType) != 0;
        var worn = SelfEquipJudge.Worn(state);
        var held = GearFloor.CastersHeld(state);
        if (caster && held >= GearFloor.CastersWanted)
        {
            covered = true;
            return string.Create(CultureInfo.InvariantCulture, $"covered by the gear floor's casters ({held} held, {GearFloor.CastersWanted} wanted)");
        }

        if (!caster && GearFloor.ChestPiece(worn) is { } chest)
        {
            covered = true;
            return $"covered by the gear floor's robe rule (the worn '{chest.Name}' covers the chest)";
        }

        var floorLevel = GearFloor.FloorLevel(LadderLoader.WarMage);
        var level = state.Self.Qualities.Level;
        if (level < floorLevel)
        {
            return string.Create(CultureInfo.InvariantCulture, $"not the gear floor's yet: level {level} is under the floor level {floorLevel}");
        }

        var plan = GearFloor.Plan(ground, GearFloor.StateOf(state, FloorPurse(state)), floorLevel, worn: worn);
        var kind = caster ? GearFloorKind.Caster : GearFloorKind.Robe;
        var buys = plan.Lines.Where(l => l.Kind == kind).ToList();
        if (buys.Count > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"the gear floor buys {buys.Sum(l => l.Count)} x {buys[0].Name} at {buys[0].Vendor.Name} for {buys[0].Price} {(lootOn ? "on a loot trip" : "only on a loot trip, and loot is off (TODO(m6-floor-needs-loot))")}");
        }

        return caster ? plan.Stop ?? plan.Why : plan.Notes.FirstOrDefault(n => n.StartsWith("gear floor: robe:", StringComparison.Ordinal)) ?? plan.Why;
    }

    /// <summary>
    /// M6 2 e: the gear floor's reading for the audit (<see cref="GearFloor.Plan"/>, a read: nothing is bought). A mage under <see cref="GearFloor.CastersWanted"/> casters from the floor level is either <b>short</b> (a profile vendor sells a caster and the
    /// purse pays for the missing ones: the audit's trip is due, named) or <b>stopped</b> (<see cref="GearFloor.NoCaster"/>: none sells one, or the purse is short: the audit stops by name). A ground that lists no vendor has no shop to buy at: the floor is
    /// not audited there, a named note (<c>TODO(m6-floor-vendorless-ground)</c>), never a stop.
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="ground">The ground (its vendors).</param>
    /// <param name="lootOn">M6 2 f (2-R1): the fight's loot setting. The floor is bought only on a loot trip, so with loot off a buyable shortfall is not <c>Short</c> (no trip is forced for it) but a note, <c>floor short, loot off: no trip</c>; an unbuyable one is still the stop.</param>
    /// <param name="saleMade">
    /// M6 6 MF (2-R12): true once a trip's sale has run (the default: a reading with no trip to come). False before the audit's trip: a stop only because the purse is short of a caster's price, with loot on and an item in the pack the sale may
    /// offer (<see cref="SaleOffers"/>), is deferred past the sale as <c>Short</c> (the trip is due; it sells, then the floor buys what the purse pays), and the audit judges the stop again after the trip, from the purse it then reads.
    /// </param>
    /// <returns>The short reason (null: not short or not buyable), the stop reason (null: no stop) and a note (null: none).</returns>
    public static (string? Short, string? Stop, string? Note) ReadFloor(GameState state, HuntingGround ground, bool lootOn = true, bool saleMade = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        if (!state.Self.Inventory.IsKnown)
        {
            return (null, null, null); // the pack is not read: a count of 0 would be a guess
        }

        var held = GearFloor.CastersHeld(state);
        var level = state.Self.Qualities.Level;
        var floorLevel = GearFloor.FloorLevel(LadderLoader.WarMage);
        if (level < floorLevel || held >= GearFloor.CastersWanted)
        {
            return (null, null, null);
        }

        if (ground.Vendors.Count == 0)
        {
            return (null, null, string.Create(CultureInfo.InvariantCulture, $"gear floor: {held} caster(s) held, {GearFloor.CastersWanted} wanted from level {floorLevel}, but {ground.Key} lists no vendor: not audited here (TODO(m6-floor-vendorless-ground))"));
        }

        var plan = GearFloor.Plan(ground, GearFloor.StateOf(state, FloorPurse(state)), floorLevel);
        if (plan.Stop is { } stop)
        {
            // 2-R12: the purse is read before the trip's sale. Check, act, check: the stop waits for the act that can change it (the sale), then reads the purse again; no estimate of the sale's take is made here (M6 16 E5c2: the
            // gate asks the sale's own verdict whether it will sell anything at all). Deferred only when the purse is the reason (an unlimited purse buys the casters), loot is on (the floor is bought only on a loot trip) and the pack
            // holds something the sale may offer.
            if (!saleMade && lootOn && GearFloor.Plan(ground, GearFloor.StateOf(state, long.MaxValue), floorLevel).Stop is null && SaleOffers(state, ground) is { Count: > 0 } offers)
            {
                return (string.Create(CultureInfo.InvariantCulture, $"gear floor: {held} caster(s) held, {GearFloor.CastersWanted} wanted: {stop}; the trip's sale comes first: the pack holds {offers.Count} item(s) it may offer ({string.Join(", ", offers.Take(4))}{(offers.Count > 4 ? ", ..." : string.Empty)}), so the loot trip sells, then the floor buys what the purse pays, and the stop is judged after the sale (2-R12)"), null, null);
            }

            return (null, stop, null);
        }

        var buys = plan.Lines.Where(l => l.Kind == GearFloorKind.Caster).ToList();
        if (buys.Count == 0)
        {
            return (null, null, null);
        }

        var reason = string.Create(CultureInfo.InvariantCulture, $"gear floor: {held} caster(s) held, {GearFloor.CastersWanted} wanted: {string.Join(", ", buys.GroupBy(l => (l.Name, l.Price, Vendor: l.Vendor.Name)).Select(g => $"{g.Sum(l => l.Count)} x {g.Key.Name} at {g.Key.Vendor} for {g.Key.Price}"))}");
        return lootOn
            ? (reason, null, null)
            : (null, null, reason + "; floor short, loot off: no trip (the floor is bought only on a loot trip: --fight-loot=on; TODO(m6-floor-needs-loot))");
    }

    /// <summary>
    /// M6 6 MF (2-R12): the names of the items a trip's sale will sell, in the order the sale reads them (the main pack, then the side packs). A gate, not an estimate: it says only whether the sale can change the purse at all.
    /// M6 16 E5c2: the rule book's answer (<see cref="Quartermaster.SaleVerdict.WillSell"/> over the session's context, <see cref="Quartermaster.SaleDesk.BookOn"/>: a sell verdict on a type a vendor of the ground buys), where
    /// the gate had a list of its own (an item with a Value that is no stock or kit line, focus, caster, coin or note, not noted as kept aside and not unsellable by flag or class). That list offered what the sale keeps (gear
    /// inside the side packs, rotation gear, the pieces kept for review, a quest file's item, a kept scroll, the kit's mana stones, the drinks she uses, an attuned piece, a town stock line, an item dropped before) and an item
    /// of a type no vendor of the ground buys, and left out a caster her gear rules do not need: with only such items held, a purse-short stop was put off past a sale that could sell nothing.
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="ground">The ground (its lines and its vendors' masks).</param>
    internal static List<string> SaleOffers(GameState state, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        var book = Quartermaster.SaleDesk.For(state).BookOn(ground);
        var offers = new List<string>();
        foreach (var (guid, item, inSidePack) in Quartermaster.SaleVerdict.Read(state))
        {
            if (Quartermaster.SaleVerdict.WillSell(guid, item, inSidePack, book))
            {
                offers.Add(item.Name);
            }
        }

        return offers;
    }

    /// <summary>
    /// The pyreals the audit counts as the floor's purse: the coin (unread counted as 0) and the face of every trade note held, the side packs' too (the resupply's <c>FloorPurse</c>; M6 16 E5a1: the quartermaster's
    /// <see cref="Quartermaster.PurseView.EverythingHeldUnreadAsZero"/>, the same sum as before).
    /// </summary>
    /// <param name="state">The session's state.</param>
    public static long FloorPurse(GameState state) => Quartermaster.PurseView.EverythingHeldUnreadAsZero(state);

    /// <summary>
    /// The foci a character's casts assume although its ground has no worn kit: the climber's (War and Life, the schools its starter pack holds the foci of, <c>starterGear.json</c> = ACE's PlayerFactory.cs 225-300 @95ef426d). Any other character: none
    /// (BSD Mage's foci are audited with its kit; the fighters cast no foci formula).
    /// </summary>
    /// <param name="state">The session's state (the character's name).</param>
    internal static IReadOnlyList<uint> AssumedFoci(GameState state) =>
        CharacterPolicy.For(state.Self.Name).StarterFoci
            ? [LevelFloor.FocusOf(DatReaderWriter.Enums.MagicSchool.WarMagic), LevelFloor.FocusOf(DatReaderWriter.Enums.MagicSchool.LifeMagic)]
            : [];

    /// <summary>How many items of weenie <paramref name="wcid"/> the player holds (worn or wielded, in the main pack, as side packs or foci, and inside the side packs).</summary>
    private static int CountHeld(GameState state, uint wcid)
    {
        var inventory = state.Self.Inventory;
        var n = 0;
        foreach (var e in inventory.Equipped)
        {
            n += Is(state, e.Guid, wcid);
        }

        foreach (var e in inventory.Pack)
        {
            n += Is(state, e.Guid, wcid);
        }

        foreach (var side in inventory.SidePacks)
        {
            n += Is(state, side.Guid, wcid);
            foreach (var e in inventory.ItemsIn(side.Guid))
            {
                n += Is(state, e.Guid, wcid);
            }
        }

        return n;
    }

    private static int Is(GameState state, uint guid, uint wcid) => state.Objects.TryGet(guid, out var o) && o.Weenie.WeenieClassId == wcid ? 1 : 0;
}

/// <summary>The kit check's reading (<see cref="StartupAudit.ReadKit"/>).</summary>
/// <param name="Verdict">The logistics counters' verdict.</param>
/// <param name="Unread">True while the pack's list or a held item's description has not arrived (nothing is judged).</param>
/// <param name="Short">The restockable stock lines under their stop line (<c>name held&lt;stop</c>).</param>
/// <param name="Low">The restockable stock lines at or over their stop line and under their trip line.</param>
/// <param name="CarriedShort">The carried stock lines (no home vendor sells them) under their stop line.</param>
/// <param name="Missing">The kit lines and the foci held fewer than their count (report-only in this first gate); a caster or robe line the gear floor covers is not missing (M6 6 MF, <see cref="FloorRouted"/>).</param>
/// <param name="AtFloor">M5 7 P7s (D148): the floor-only carried lines under their stop line (<see cref="LogisticsCounters.IsFloorOnly"/>): no stop, the spells that need them are dropped; null: none.</param>
public sealed record KitReading(LogisticsVerdict Verdict, bool Unread, IReadOnlyList<string> Short, IReadOnlyList<string> Low, IReadOnlyList<string> CarriedShort, IReadOnlyList<string> Missing, IReadOnlyList<string>? AtFloor = null)
{
    /// <summary>M6 item 0 F2: the foci the character's casts assume (<see cref="StartupAudit.AssumedFoci"/>) that the pack lacks, by name; a ground with a worn kit audits its foci through <see cref="Missing"/> and leaves this empty.</summary>
    public IReadOnlyList<string> FociMissing { get; init; } = [];

    /// <summary>True when a trip should run first: a restockable line under its stop or trip line, or the counters' own trip (the burden, the free slots).</summary>
    public bool TripDue => !Unread && CarriedShort.Count == 0 && (Short.Count > 0 || Low.Count > 0 || Verdict.IsTrip || FloorShort is not null);

    /// <summary>M6 2 e: the gear floor is short and buyable (<see cref="StartupAudit.ReadFloor"/>: under two casters from the floor level, a profile vendor sells one and the purse pays): the trip is due, this is its reason; null: not short.</summary>
    public string? FloorShort { get; init; }

    /// <summary>M6 2 e: the gear floor is short and cannot be bought (no profile vendor sells a caster, or the purse is short): the audit's <c>no-caster</c> stop, this is its reason; null: no stop.</summary>
    public string? FloorStop { get; init; }

    /// <summary>M6 2 e: a named note on the floor's audit (a ground with no vendor is not audited); null: none.</summary>
    public string? FloorNote { get; init; }

    /// <summary>M6 6 MF (<c>m5-audit-kit-restock</c>): the kit lines held under their count that the gear floor answers (a caster, a robe), each <c>&lt;line&gt;: &lt;the floor's word&gt;</c> (<see cref="StartupAudit.FloorRoute"/>).</summary>
    public IReadOnlyList<string> FloorRouted { get; init; } = [];

    /// <summary>M6 6 MF: the kit lines and foci held under their count that no trip buys (the gloves, the packs, the foci): named, report-only.</summary>
    public IReadOnlyList<string> KitRest { get; init; } = [];
}

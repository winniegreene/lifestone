using System.Globalization;
using System.Numerics;
using System.Text;
using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests;
using Lifestone.Client.World;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.World.Data;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M5 item 0 (c4), the den measuring class: ONE test per measure, run by the LEAD ONLY, one at a time, with the backend up and the Lifestone server mod ready:
/// <c>dotnet test tests/Lifestone.Loopback.Tests --filter "FullyQualifiedName~DenMeasureLiveTests.&lt;Name&gt;"</c>. They run on BSD Mage on <c>test</c> (decision D5: the copy the last gate left, level 127; never re-created, reset or levelled here beyond the
/// kills it makes), over the shared session, and send no mod verb except <c>place</c> (the Action API and the Place tool only; the kit's components are only read). Each prints one <c>[measure] &lt;name&gt; k=v ...</c> line (and some detail lines) and writes them to
/// <c>cache/m5/0/measures/&lt;name&gt;.txt</c> of the running checkout. A test asserts only that it measured: a measure that could not be taken prints why and fails, named; no number is ever guessed.
/// <para><b>Not in the gate:</b> the class carries <c>Lane=solo</c> (tools/live-gate.ps1 leaves a Solo class out of the account lanes unless GateAreas.psd1's InLane lists it, and it does not) and <c>Tier=Measure</c>; it is never Soak.</para>
/// <list type="bullet">
/// <item><c>RespawnAndBurn</c> (about 35 min): the buff top-up, then the existing fight loop (<see cref="FightScenario"/>, Flame Bolt at the chosen level) on the arrival floor for 1,800 s of fight time. It prints: the kills (0x01AD times) and kills per hour; the Tusker
/// creates on the floor; the RESPAWN delays, each paired with the earliest unpaired earlier death, a respawn being a create of a dynamic guid above the highest dynamic guid of the first sweep (the first 10 s of the fight) or a re-create of a guid that already died (a first sighting of a
/// tusker that was alive but unseen is never a respawn), with n, min, median, max, how many paired, how many deaths stayed unpaired and the configured 900 s beside them; casts by spell and the Flame Bolt VII casts (landed and fizzled apart) with the pack's component counts before and after
/// and the drop per Flame Bolt VII cast (the fight is checked to cast spell 2128; other casts in the window mark the burn not clean); damage per landed line; mana before the request and the lowest value up to 3 s after the confirmation; the seconds the mana reading was under the cast
/// ceiling (the mana wait); and the run speed on legs over 10 m from the position samples.</item>
/// <item><c>ManaRegen</c> (about 8 min, in Holtburg): after a stamina spend (Meditative Trance) and mana spends (Strength Self casts), 60 s resting in non-combat and 60 s in magic stance, no casts, as points per second from the vital pushes. "Magic stance" is the stance standing in
/// Holtburg, a proxy for ACE's rule that stance changes stamina regeneration only; it is not regeneration during a fight.</item>
/// <item><c>SpellTable</c> (about 45 min, mana-bound): for Flame Bolt I-VII, the highest Flame Streak and Flame Arc the book holds and Cassius' Ring of Fire, the fight loop itself (<see cref="FightScenario"/> with the spell, so its target pick, range rule, walk and mana wait) casts until 5 casts
/// reach the server; per spell it prints the spell's range, request to projectile seconds, damage per line (compared with the fight tally's line count), mana before and lowest within 3 s after the projectile for each cast, fizzles, 0x0550 refusals with the target's distance, and the first
/// spell's raw chat lines.</item>
/// <item><c>RingCluster</c> (up to 10 min, 5 casts): bolt up to 3 tuskers once each (a damage line proves the line was clear), wait up to 90 s, and cast Cassius' Ring of Fire when 3 or more tuskers are within 8 m; per cast the tuskers within 3, 5 and 8 m, projectiles, damage lines and kills
/// within 8 s. If the tuskers never gather, the bolted tuskers' distances over time are the finding (the test does not fail for that).</item>
/// <item><c>WingProbe</c> (about 4 min, bounded): the mage is placed on the Virindi's spawn point in cell 0x0105016C at z -24 (a point a creature stands on; the den map names no stairs cell) and stands up to 120 s; it prints the spell damage taken per line, the first hit and the time to 50% health, and at 50%
/// goes back to the arrival with <c>place</c>. A death is reported <c>died=yes</c> and not recovered. The kill switch is set at the end of the stand as a flag only: it refuses Automation actions and these measures send Host ones, so it guards nothing here.</item>
/// <item><c>ManaDance</c> (about 25 min, Holtburg): the owner's stamina to mana dance. Per dance: wait up to 120 s for stamina of 60% or more; spend mana to 15 or less by refreshing self buffs (a refused spend cast is logged with its code and that spell dropped); then press Stamina to Mana Self (the highest level the book holds, power 300 or under) every 0.25 s after each failed try, up to 40 tries. 5 dances at mana 15 or less and 1 reference dance at mana 40-60. Per try: time, mana and stamina before and after and the outcome (landed, refused short of mana, fizzled, busy, or the exact kind and code); per dance and overall: landing chance per try, mana gained and stamina taken per landed cast, mana per second of dancing against the measured 0.44 regeneration.</item>
/// <item><c>KitAndVitals</c> (short): the components by weenie id, the burden, the four foci (15268-15271), the vitals, War Magic buffed and base, Mana Conversion.</item>
/// </list>
/// The readers are <c>WorldSqlFactsRunTests</c> (offline) and the unit test <c>MageLevel80SkillTests</c> (Lifestone.Client.Tests).
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Trait("Tier", "Measure")]
[Trait("Lane", "solo")]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[LiveSharedSession]
public sealed class DenMeasureLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void RespawnAndBurn() => Run(DenLeg.Which.Respawn, "RespawnAndBurn", 3300);

    [LoopbackFact]
    public void ManaRegen() => Run(DenLeg.Which.Regen, "ManaRegen", 900);

    [LoopbackFact]
    public void SpellTable() => Run(DenLeg.Which.Spells, "SpellTable", 4200);

    [LoopbackFact]
    public void RingCluster() => Run(DenLeg.Which.Ring, "RingCluster", 1500);

    [LoopbackFact]
    public void WingProbe() => Run(DenLeg.Which.Wing, "WingProbe", 600);

    [LoopbackFact]
    public void ManaDance() => Run(DenLeg.Which.Dance, "ManaDance", 4200);

    [LoopbackFact]
    public void KitAndVitals() => Run(DenLeg.Which.Kit, "KitAndVitals", 120);

    private void Run(DenLeg.Which which, string name, double seconds)
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var sink = new RecordingLogSink();
        var leg = new DenLeg(which, name, dats, spells, sink, output.WriteLine);
        var result = LiveMage.Run(output, leg, "den-" + name, seconds).Result;
        foreach (var line in sink.Lines.Where(l => l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).Take(40))
        {
            output.WriteLine(line);
        }

        if (leg.Fields is { } fields)
        {
            MeasureText.Emit(output, name, fields, leg.More);
        }
        else
        {
            output.WriteLine($"[measure] {name} NOT MEASURED: {leg.Failure ?? (leg.Problems.Count > 0 ? string.Join("; ", leg.Problems) : result.Failure ?? "no fields")}");
        }

        Assert.True(leg.Fields is not null, $"{name}: not measured: {leg.Failure ?? string.Join("; ", leg.Problems)}");
        Assert.Null(leg.Failure);
        Assert.Empty(leg.Problems);
    }

    /// <summary>The measuring leg: it finds BSD Mage in the world (the shared session put it there) and runs one measure's script.</summary>
    internal sealed class DenLeg : LiveLeg
    {
        internal enum Which { Respawn, Regen, Spells, Ring, Wing, Kit, Dance }

        private const double FightSeconds = 1800;
        private static readonly uint[] Foci = [15268, 15269, 15270, 15271];

        private readonly Which _which;
        private readonly string _name;
        private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
        private readonly RecordingLogSink _sink;
        private readonly DenMeasure _m = new();
        private readonly List<string> _more = [];

        public DenLeg(Which which, string name, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, RecordingLogSink sink, Action<string> write)
            : base("den-" + name, [], write, dats)
        {
            (_which, _name, _spells, _sink) = (which, name, spells, sink);
        }

        /// <summary>The measure's one line of <c>k=v</c> fields, or null when it was not taken.</summary>
        internal string? Fields { get; private set; }

        /// <summary>Detail lines for the file.</summary>
        internal IReadOnlyList<string> More => _more;

        protected override IEnumerable<ActionHandle?> Script()
        {
            Stage = "running";
            Require(LiveMage.WrongCharacter(S, MageTemplate.CharacterName) is null, LiveMage.WrongCharacter(S, MageTemplate.CharacterName) ?? string.Empty);
            Require(AlreadyInWorld, "the shared session did not put BSD Mage in the world");
            if (_which != Which.Respawn)
            {
                Follow ??= new HuntFollow(Dats, new RecordingLogSink()); // the fight scenario's DenWalk follows server moves itself
            }

            _m.Start(S);
            try
            {
                IEnumerable<ActionHandle?> body = _which switch
                {
                    Which.Respawn => RespawnAndBurn(),
                    Which.Regen => ManaRegen(),
                    Which.Spells => SpellTable(),
                    Which.Ring => RingCluster(),
                    Which.Wing => WingProbe(),
                    Which.Dance => ManaDance(),
                    _ => KitAndVitals(),
                };
                foreach (var h in body)
                {
                    _m.Tick();
                    yield return h;
                }
            }
            finally
            {
                _m.Stop();
            }
        }

        // ---------------------------------------------------------------------------------------------- common pieces

        private (uint Cur, uint Max) Vital(uint maxId) =>
            S.State.Self.Qualities.TryGetVitalLevels(maxId, out var c, out var m) ? (c, m) : throw new InvalidOperationException("the read model has no vital " + maxId);

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool InDen(uint cell) => cell >> 16 == BlackSpawnDen.Landblock;

        private IEnumerable<ActionHandle?> GoTo(TeleportDestination where, string what)
        {
            foreach (var h in PlaceAndSettle(where, what)) { yield return h; }
        }

        private IEnumerable<ActionHandle?> GoHoltburg()
        {
            if ((S.State.Self.Position?.CellId ?? 0) >> 16 == 0xA9B4)
            {
                yield break;
            }

            foreach (var h in GoTo(TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg")), "Holtburg")) { yield return h; }
        }

        private IEnumerable<ActionHandle?> GoDen()
        {
            if (BlackSpawnDen.IsFloorCell(S.State.Self.Position?.CellId ?? 0))
            {
                yield break;
            }

            foreach (var h in GoTo(TeleportCatalog.FromInterior(InteriorLocations.BlackSpawnDenAluvian), "the Black Spawn Den arrival")) { yield return h; }
        }

        private IEnumerable<ActionHandle?> Mode(CombatMode mode)
        {
            var set = S.Actions.SetCombatMode(mode, ActionSource.Host);
            yield return set;
            Check(set.Outcome is { Succeeded: true }, $"combat mode {mode}: {Describe(set)}");
        }

        /// <summary>The buff top-up the fight does (the cycle, lines with under ten minutes left), logged; refills nothing else.</summary>
        private IEnumerable<ActionHandle?> TopUp()
        {
            var cycle = new Lifestone.Client.Session.Mage.BuffCycle(S, _spells, new BuffOptions { RebuffMarginSeconds = FightScenario.TopUpSeconds }, _sink, ArmourBuffLedger.For(S.State));
            var due = cycle.RebuffDue(S.Clock.Now, FightScenario.TopUpSeconds);
            if (due.Count == 0)
            {
                Log($"buff top-up skipped: all {cycle.Wanted} lines have at least {FightScenario.TopUpSeconds:0} s left");
                yield break;
            }

            Log($"buff top-up: {due.Count} of {cycle.Wanted} lines due ({string.Join(';', due)})");
            foreach (var h in Mode(CombatMode.Magic)) { yield return h; }

            Require(cycle.Start(S.Clock.Now), "buff top-up: the cycle did not start");
            var until = S.Clock.Now + 900;
            for (;;)
            {
                var status = cycle.Step(S.Clock.Now);
                if (status != BuffStatus.Running)
                {
                    Require(status == BuffStatus.Done, "buff top-up failed: " + cycle.Result?.Failure);
                    break;
                }

                Require(S.Clock.Now < until, "buff top-up: 900 s passed");
                yield return null;
            }

            Log($"buff top-up done: {cycle.Result?.Cast ?? 0} casts, {cycle.Result?.Active ?? 0} of {cycle.Result?.Wanted ?? 0} lines active");
        }

        private IEnumerable<ActionHandle?> WaitMana(uint need, double maxSeconds)
        {
            var until = S.Clock.Now + maxSeconds;
            while (Vital(MageTemplate.MaxMana).Cur < need && S.Clock.Now < until)
            {
                yield return null;
            }

            Check(Vital(MageTemplate.MaxMana).Cur >= need, $"waited {maxSeconds:0} s for mana {need} and had {Vital(MageTemplate.MaxMana).Cur}");
        }

        private uint NearestTusker(double metres)
        {
            if (S.State.Self.Position is not { } here)
            {
                return 0;
            }

            return S.State.Objects.NearestCreature(ArenaProfile.TargetWcids, in here, (float)metres, BlackSpawnDen.IsFloorCell, out var guid) ? guid : 0;
        }

        private int TuskersWithin(double metres)
        {
            if (S.State.Self.Position is not { } here)
            {
                return 0;
            }

            var found = new List<uint>();
            S.State.Objects.Within(in here, (float)metres, found);
            return found.Count(g => S.State.Objects.TryGet(g, out var o) && o.Weenie.WeenieClassId is 1628 or 1629 && o.Health is null or > 0f && o.Motion?.Forward != WorldObjectTable.DeadMotionLowWord);
        }

        private const uint FlameBoltVii = 2128;

        private static bool IsCastOf(ActionLogEntry a, uint spell) => a.Summary == $"Cast spell={spell}" || a.Summary.StartsWith($"Cast spell={spell} ", StringComparison.Ordinal);

        private static string CellText(uint cell) => $"0x{cell & 0xFFFF:X4}";

        // ---------------------------------------------------------------------------------------------- 1 RespawnAndBurn

        private IEnumerable<ActionHandle?> RespawnAndBurn()
        {
            var fightOptions = new FightOptions { Kills = FightOptions.MaxKills, Seconds = FightSeconds, LeaveOnFailure = false, LeaveAtEnd = false };
            var fight = new FightScenario(_spells, LiveMage.KitAccount(), new BuffOptions(), fightOptions, DenWalk.Create(Dats, _sink), _sink, endSession: false, stages: FightStages.Buffs | FightStages.Place | FightStages.Mode) { RequireCharacter = true };
            SortedDictionary<uint, int>? before = null;
            double start = double.NaN;
            var manaStart = 0u;
            var status = ScenarioStatus.Running;
            var sealedSweep = false;
            while (status == ScenarioStatus.Running)
            {
                status = fight.Step(S, S.Clock.Now);
                if (!sealedSweep && !double.IsNaN(start) && S.Clock.Now >= start + 10)
                {
                    _m.SealSweep(); // the first sweep: what the den had loaded when the fight began
                    sealedSweep = true;
                }

                if (double.IsNaN(start) && fight.Stage == "fight")
                {
                    start = S.Clock.Now;
                    before = DenMeasure.Components(S);
                    manaStart = Vital(MageTemplate.MaxMana).Cur;
                    Log($"the fight begins; components before: {DenMeasure.Format(before)}");
                }

                yield return null;
            }

            var end = S.Clock.Now;
            fight.Dispose();
            Require(!double.IsNaN(start), "the fight never began: " + fight.Failure);
            var endedBySeconds = fight.Failure is { } f && f.Contains(" s passed with kills=", StringComparison.Ordinal);
            Require(status == ScenarioStatus.Succeeded || endedBySeconds, "the fight ended early: " + fight.Failure);
            var after = DenMeasure.Components(S);
            var span = end - start;
            var tally = fight.Tally;
            var kills = _m.KillTexts.Count(t => t >= start);
            var deaths = _m.Deaths.Where(d => d.T >= start).ToList();
            var (inOrder, byCell, respawnCreates, unpairedDeaths) = _m.Respawns(start);
            var floorCreates = _m.Creates.Where(c => c.T >= start && BlackSpawnDen.IsFloorCell(c.Cell)).ToList();
            var bolts = _m.Magic.Where(l => l.T >= start && l.Kind == MagicLineKind.DamageDealt).Select(l => (double)l.Amount).ToList();
            // Flame Bolt VII only (2128): any other spell cast in the window is listed, and the burn is then not clean.
            var windowCasts = _m.Actions.Where(a => a.Kind == ActionKind.CastSpell && a.QueuedAt >= start).ToList();
            var bySpell = windowCasts.GroupBy(a => a.Summary.Split(' ')[1]).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}").ToList();
            var casts = windowCasts.Where(a => IsCastOf(a, FlameBoltVii)).ToList();
            var otherCasts = windowCasts.Count - casts.Count;
            var landed7 = casts.Count(a => a.Outcome.Kind == ActionOutcomeKind.Confirmed);
            var fizzles = casts.Count(a => a.Outcome.Kind == ActionOutcomeKind.ServerError && a.Outcome.Code == FightScenario.FizzleCode);
            Check(fight.SpellId == FlameBoltVii, $"the fight cast spell {fight.SpellId} ({fight.Level}), not Flame Bolt VII ({FlameBoltVii}): the burn is not Flame Bolt VII's");
            var spent = new List<double>();
            foreach (var c in casts.Where(a => a.Outcome.Kind == ActionOutcomeKind.Confirmed))
            {
                if (_m.VitalAt(c.QueuedAt) is { } b)
                {
                    var low = _m.Vitals.Where(v => v.T >= c.QueuedAt && v.T <= c.CompletedAt + 3.0).Select(v => (double)v.Mana).DefaultIfEmpty(b.Mana).Min();
                    spent.Add(Math.Max(0, b.Mana - low));
                }
            }

            var info = _spells.GetValueOrDefault(fight.SpellId);
            var ceiling = info is null ? 0u : GearCasts.ManaCeiling(info, 0);
            var wait = _m.SecondsManaBelow(ceiling, start, end);
            var legs = _m.Legs(10, start).ToList();
            var burn = new List<string>();
            foreach (var id in before!.Keys.Union(after.Keys).Order())
            {
                var drop = before.GetValueOrDefault(id) - after.GetValueOrDefault(id);
                burn.Add($"{id}:{before.GetValueOrDefault(id)}->{after.GetValueOrDefault(id)}(-{drop}{(casts.Count > 0 ? $", {(double)drop / casts.Count:0.####}/Flame_Bolt_VII_cast" : string.Empty)})");
            }

            Fields = string.Create(
                CultureInfo.InvariantCulture,
                $"fight_s={span:0} ended={(endedBySeconds ? "seconds" : "kills")} level={fight.Level} spell={fight.SpellId} kills_0x01AD={kills} kills_per_hour={(span > 0 ? kills / span * 3600 : 0):0.#} dead_motions={deaths.Count} floor_creates={floorCreates.Count} respawn_creates={respawnCreates} respawn_paired={inOrder.Count} deaths_unpaired={unpairedDeaths} first_sweep_max_guid=0x{_m.FirstSweepMaxGuid:X8} "
                + $"respawn_delay_s[{MeasureText.Dist(inOrder)}] configured_delay_s=900 (generator profile row delay; RegenerationInterval 60; world SQL facts) respawn_rule=({DenMeasure.RespawnRule}) casts_by_spell=[{string.Join(' ', bySpell)}] flame_bolt_vii_casts={casts.Count} landed={landed7} fizzled={fizzles} other_spell_casts={otherCasts} burn_clean={(otherCasts == 0 ? "yes" : "no")} tally_casts={tally.Casts} misses={tally.Misses} landed_lines={bolts.Count} damage_per_landed_bolt[{MeasureText.Dist(bolts)}] mana_per_cast[{MeasureText.Dist(spent)}] "
                + $"mana_start={manaStart} mana_ceiling={ceiling} mana_wait_s={wait:0.#} walk_legs_10m={legs.Count} run_speed_mps[{MeasureText.Dist(legs.Select(l => l.Speed).ToList())}] components_burn=[{string.Join(' ', burn)}]");
            _more.Add($"components before: {DenMeasure.Format(before)}; after: {DenMeasure.Format(after)}; casts in the window {casts.Count}");
            _more.Add("kills (t from fight start): " + string.Join(' ', _m.KillTexts.Where(t => t >= start).Select(t => F(t - start))));
            _more.Add("creates on the floor (t, guid, wcid, cell; NEW = first sighting, RESPAWN = by the rule): " + string.Join("; ", floorCreates.Select(c => $"{F(c.T - start)} {c.Guid:X8} {c.Wcid} {CellText(c.Cell)}{(c.Respawn ? " RESPAWN" : c.NewGuid ? " NEW" : string.Empty)}")));
            _more.Add("dead motions (t, guid, cell): " + string.Join("; ", deaths.Select(d => $"{F(d.T - start)} {d.Guid:X8} {CellText(d.Cell)}")));
            _more.Add("respawn delay per cell (the pair's cells agree; secondary): " + (byCell.Count == 0 ? "no pairs" : string.Join("; ", byCell.OrderBy(k => k.Key).Select(k => $"{CellText(k.Key)}: {MeasureText.Dist(k.Value)}"))));
            _more.Add("walk legs over 10 m (metres, seconds, m/s): " + string.Join("; ", legs.Select(l => $"{l.Metres:0.#} {l.Seconds:0.#} {l.Speed:0.##}")));
            Log("RespawnAndBurn measured: " + Fields);
        }

        // ---------------------------------------------------------------------------------------------- 2 ManaRegen

        private IEnumerable<ActionHandle?> ManaRegen()
        {
            foreach (var h in GoHoltburg()) { yield return h; }

            foreach (var h in Mode(CombatMode.Magic)) { yield return h; }

            var book = S.State.Self.Spellbook;
            var spendLog = new List<string>();

            // The stamina spend: Meditative Trance (Stamina to Mana Self VII, 2345) drains half the current stamina.
            if (book.Knows(2345) && _spells.TryGetValue(2345, out var trance))
            {
                foreach (var h in WaitMana(GearCasts.ManaCeiling(trance, 0), 120)) { yield return h; }

                var cast = S.Actions.Cast(2345);
                yield return cast;
                spendLog.Add($"Meditative Trance: {Describe(cast)}");
            }
            else
            {
                spendLog.Add("Meditative Trance 2345: not known, so stamina was not spent by a cast");
            }

            // The mana spend: the highest known level of a self buff line, until mana is under 60% (at most 12 casts).
            var line = ArenaProfile.LineNamed("Strength Self")!;
            var buff = line.Spells.Where(s => book.Knows(s.Id) && s.Power <= 300).OrderByDescending(s => s.Power).FirstOrDefault();
            Require(buff is not null && _spells.TryGetValue(buff.Id, out _), "no known Strength Self level to spend mana with");
            for (var i = 0; i < 12 && Vital(MageTemplate.MaxMana).Cur * 100 > Vital(MageTemplate.MaxMana).Max * 60u; i++)
            {
                var before = Vital(MageTemplate.MaxMana).Cur;
                var cast = S.Actions.Cast(buff!.Id);
                yield return cast;
                foreach (var h in Pause(2)) { yield return h; }

                spendLog.Add($"{buff.Name}: {Describe(cast)}; mana {before} -> {Vital(MageTemplate.MaxMana).Cur}");
            }

            var (m0, mm) = Vital(MageTemplate.MaxMana);
            var (s0, sm) = Vital(MageTemplate.MaxStamina);
            _more.AddRange(spendLog);
            _more.Add($"after the spends: mana {m0}/{mm}, stamina {s0}/{sm}");
            Check(m0 + 60 < mm, $"mana is {m0} of {mm}: too near the maximum to see 60 s of regeneration");

            var rest = new List<string>();
            foreach (var (label, mode) in new[] { ("rest_nonCombat", CombatMode.NonCombat), ("magic_stance", CombatMode.Magic) })
            {
                foreach (var h in Mode(mode)) { yield return h; }

                foreach (var h in Pause(2)) { yield return h; }

                var from = S.Clock.Now;
                foreach (var h in Pause(60)) { yield return h; }

                var to = S.Clock.Now;
                var window = _m.Vitals.Where(v => v.T >= from && v.T <= to).ToList();
                Check(window.Count >= 2, $"{label}: only {window.Count} vital reading(s) in the window");
                if (window.Count >= 2)
                {
                    var a = window[0];
                    var b = window[^1];
                    var dt = b.T - a.T;
                    rest.Add(string.Create(CultureInfo.InvariantCulture, $"{label}_mana_per_s={(b.Mana - a.Mana) / dt:0.###} {label}_stamina_per_s={((double)b.Stamina - a.Stamina) / dt:0.###} {label}_health_per_s={((double)b.Health - a.Health) / dt:0.###} {label}_readings={window.Count} {label}_window_s={dt:0.#} {label}_mana={a.Mana}->{b.Mana} {label}_stamina={a.Stamina}->{b.Stamina}"));
                    _more.Add($"{label} readings (t, mana, stamina): " + string.Join("; ", window.Select(v => $"{v.T - from:0.#} {v.Mana} {v.Stamina}")));
                }
            }

            Fields = $"mana_start={m0}/{mm} stamina_start={s0}/{sm} {string.Join(' ', rest)}";
        }

        // ---------------------------------------------------------------------------------------------- 3 SpellTable

        private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];

        /// <summary>Metres from the mage to a world object (3-D, global frame), or NaN.</summary>
        private double DistanceTo(uint guid)
        {
            if (S.State.Self.Position is not { } here || !S.State.Objects.TryGet(guid, out var o) || o.Position is not { } at)
            {
                return double.NaN;
            }

            var a = here.ToGlobal();
            var b = at.ToGlobal();
            return Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));
        }

        /// <summary>
        /// Each spell is cast by the fight loop itself (<see cref="FightScenario"/> with <c>SpellId</c>: its target pick, its range rule, its walk along the floor, its mana wait), stopped after 5 casts that reached the server (landed or fizzled); this class only watches.
        /// A cast ACE refused with 0x0550 (MissileOutOfRange) is not one of the 5; it is printed with the target's distance and the spell's range.
        /// </summary>
        private IEnumerable<ActionHandle?> SpellTable()
        {
            var book = S.State.Self.Spellbook;
            var plan = new List<(string Label, uint Id)>();
            var bolt = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!;
            for (var i = 0; i < 7; i++)
            {
                plan.Add(($"Flame Bolt {Numerals[i]}", bolt.Spells[i].Id));
            }

            foreach (var name in new[] { "Flame Streak", "Flame Arc" })
            {
                var line = ArenaProfile.LineNamed(name)!;
                var best = line.Spells.Where(s => s.Power <= 300 && book.Knows(s.Id)).OrderByDescending(s => s.Power).FirstOrDefault();
                plan.Add(best is null ? ($"{name} (not known)", 0u) : ($"{name} ({best.Name}, {best.Id})", best.Id));
            }

            plan.Add(($"Cassius' Ring of Fire ({ArenaProfile.RingOfFireId})", ArenaProfile.RingOfFireId));
            Require(S.State.Self.Qualities.TryGetSkill(MageTemplate.WarMagic, out var war), "the read model has no War Magic skill");
            var results = new List<string>();
            var firstFight = true;
            var firstRaw = true;
            var lowHealth = false;
            foreach (var (label, id) in plan)
            {
                if (lowHealth)
                {
                    results.Add($"[{label}: not cast, health under 35%]");
                    continue;
                }

                if (id == 0 || !book.Knows(id) || !_spells.TryGetValue(id, out var info))
                {
                    results.Add($"[{label}: not known]");
                    continue;
                }

                var range = FightScenario.CastRange(info, war.Init + war.Ranks);
                var options = new FightOptions { Kills = FightOptions.MaxKills, Seconds = 900, SpellId = id, LeaveOnFailure = false, LeaveAtEnd = false };
                var stages = (firstFight ? FightStages.Buffs : FightStages.None) | FightStages.Place | FightStages.Mode;
                firstFight = false;
                var fight = new FightScenario(_spells, LiveMage.KitAccount(), new BuffOptions(), options, DenWalk.Create(Dats, _sink), _sink, endSession: false, stages: stages) { RequireCharacter = true };
                var actions0 = _m.Actions.Count;
                var magic0 = _m.Magic.Count;
                var refusals = new List<string>();
                var seenEntries = actions0;
                var status = ScenarioStatus.Running;
                var reached = 0;
                var rangeSeen = float.NaN;
                while (status == ScenarioStatus.Running && reached < 5)
                {
                    status = fight.Step(S, S.Clock.Now);
                    if (fight.Stage == "fight" && float.IsNaN(rangeSeen))
                    {
                        rangeSeen = fight.RangeMetres;
                    }

                    yield return null;
                    for (; seenEntries < _m.Actions.Count; seenEntries++)
                    {
                        var e = _m.Actions[seenEntries];
                        if (e.Kind != ActionKind.CastSpell || !IsCastOf(e, id))
                        {
                            continue;
                        }

                        if (e.Outcome.Kind == ActionOutcomeKind.Confirmed || (e.Outcome.Kind == ActionOutcomeKind.ServerError && e.Outcome.Code == FightScenario.FizzleCode))
                        {
                            reached++;
                        }
                        else if (e.Outcome.Code == FightScenario.MissileOutOfRange)
                        {
                            refusals.Add($"0x0550 at t={e.QueuedAt:0.#} target {fight.Target:X8} {DistanceTo(fight.Target):0.#} m away, spell range {range:0.#} m (fight range {rangeSeen:0.#} m)");
                        }
                    }
                }

                var tallyLines = fight.Tally.DamageLinesDealt;
                var tallyDamage = fight.Tally.DamageDealt;
                var failure = fight.Failure;
                fight.Dispose();
                foreach (var h in Pause(8)) { yield return h; }

                var entries = _m.Actions.Skip(actions0).Where(e => e.Kind == ActionKind.CastSpell && IsCastOf(e, id)).OrderBy(e => e.QueuedAt).ToList();
                var landed = entries.Where(e => e.Outcome.Kind == ActionOutcomeKind.Confirmed || (e.Outcome.Kind == ActionOutcomeKind.ServerError && e.Outcome.Code == FightScenario.FizzleCode)).ToList();
                var latency = new List<double>();
                var damage = new List<double>();
                var spent = new List<double>();
                var pairs = new List<string>();
                var fizzles = 0;
                for (var i = 0; i < landed.Count; i++)
                {
                    var e = landed[i];
                    var t0 = e.QueuedAt;
                    var next = i + 1 < landed.Count ? landed[i + 1].QueuedAt : t0 + 16;
                    var fizzled = e.Outcome.Kind == ActionOutcomeKind.ServerError;
                    fizzles += fizzled ? 1 : 0;
                    var proj = _m.Missiles.FirstOrDefault(x => x.T >= t0 && x.T < next);
                    if (proj.Guid != 0)
                    {
                        latency.Add(proj.T - t0);
                    }

                    var lines = _m.Magic.Skip(magic0).Where(l => l.T >= t0 && l.T < next && l.Kind == MagicLineKind.DamageDealt).ToList();
                    damage.AddRange(lines.Select(l => (double)l.Amount));
                    var before = _m.VitalAt(t0)?.Mana ?? 0u;
                    var until = (proj.Guid != 0 ? proj.T : e.CompletedAt) + 3.0;
                    var low = _m.Vitals.Where(v => v.T >= t0 && v.T <= until).Select(v => v.Mana).DefaultIfEmpty(before).Min();
                    spent.Add(Math.Max(0, (double)before - low));
                    pairs.Add($"{before}->{low}");
                    _more.Add($"{label} cast {i + 1} at t={t0:0.#}: {(fizzled ? "fizzled" : "landed")}, projectile after {(proj.Guid != 0 ? F(proj.T - t0) + " s" : "none")}, mana {before}->{low}, damage lines {lines.Count}: {string.Join(", ", lines.Select(l => l.Amount))}");
                }

                if (firstRaw)
                {
                    firstRaw = false;
                    _more.Add($"RAW magic-chat lines while casting {label} (the first spell; kind, amount, text; {_m.Magic.Count - magic0} lines):");
                    _more.AddRange(_m.Magic.Skip(magic0).Take(14).Select(l => $"    t={l.T:0.#} {l.Kind} {l.Amount}: {l.Text}"));
                }

                results.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{label}: range_m={range:0.#} reached_server={landed.Count} request_to_projectile_s[{MeasureText.Dist(latency)}] damage_per_line[{MeasureText.Dist(damage)}] (my lines {damage.Count}, tally lines {tallyLines}, tally damage {tallyDamage}) mana_before_to_lowest_3s_after=[{string.Join(' ', pairs)}] mana_per_cast[{MeasureText.Dist(spent)}] fizzles={fizzles} refused_0x0550={refusals.Count} ended={(failure is null ? "after 5 casts" : failure)}]"));
                _more.AddRange(refusals.Select(r => $"{label}: {r}"));
                var health = Vital(MageTemplate.MaxHealth);
                if (health.Cur * 100 < health.Max * 35u)
                {
                    lowHealth = true;
                    Check(false, $"{label}: health {health.Cur}/{health.Max} fell under 35%: the table is cut short");
                }
            }

            Fields = string.Join(' ', results);
            _more.InsertRange(0, results);
        }

        // ---------------------------------------------------------------------------------------------- 4 RingCluster

        /// <summary>
        /// Round: bolt up to 3 tuskers once each (a bolt that draws a damage line proves its line is clear; up to 8 are tried), then wait up to 90 s; Cassius' Ring of Fire whenever 3 or more tuskers are within 8 m. At most 5 casts and 10 minutes.
        /// If the tuskers never gather the finding is their distance over time, not a failure.
        /// </summary>
        private IEnumerable<ActionHandle?> RingCluster()
        {
            foreach (var h in GoDen()) { yield return h; }

            foreach (var h in TopUp()) { yield return h; }

            foreach (var h in Mode(CombatMode.Magic)) { yield return h; }

            var ring = ArenaProfile.RingOfFireId;
            Require(S.State.Self.Spellbook.Knows(ring) && _spells.TryGetValue(ring, out _), "Cassius' Ring of Fire 1785 is not known: nothing is cast");
            var ringInfo = _spells[ring];
            var level = FightScenario.ChooseLevel(S.State.Self.Qualities, S.State.Self.Spellbook, BuffOptions.DefaultCastChance, out _, out var why);
            Require(level >= 0, "no Flame Bolt level to aggro with: " + why);
            var boltId = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells[level].Id;
            var started = S.Clock.Now;
            var rows = new List<string>();
            var trail = new List<string>();
            var finding = "none";
            for (var round = 1; rows.Count < 5 && S.Clock.Now - started < 600; round++)
            {
                var tried = new List<uint>();
                var proven = new List<uint>();
                while (proven.Count < 3 && tried.Count < 8 && S.State.Self.Position is { } here)
                {
                    if (!S.State.Objects.NearestCreature(ArenaProfile.TargetWcids, in here, 60f, BlackSpawnDen.IsFloorCell, tried, out var target))
                    {
                        break;
                    }

                    tried.Add(target);
                    foreach (var h in WaitMana(GearCasts.ManaCeiling(_spells[boltId], 0), 300)) { yield return h; }

                    var l0 = _m.Magic.Count;
                    var cast = S.Actions.Cast(boltId, target);
                    yield return cast;
                    var deadline = S.Clock.Now + 6;
                    while (S.Clock.Now < deadline && !_m.Magic.Skip(l0).Any(l => l.Kind == MagicLineKind.DamageDealt))
                    {
                        yield return null;
                    }

                    var hit = _m.Magic.Skip(l0).Any(l => l.Kind == MagicLineKind.DamageDealt);
                    Log($"round {round} bolt at {target:X8} ({DistanceTo(target):0.#} m): {Describe(cast)}; damage line {(hit ? "yes (line clear)" : "no (no clear line, or it missed)")}");
                    if (hit)
                    {
                        proven.Add(target);
                    }
                }

                if (proven.Count == 0)
                {
                    finding = $"round {round}: none of {tried.Count} bolts drew a damage line (no clear line to a tusker from where the mage stands)";
                    break;
                }

                var waitStart = S.Clock.Now;
                var lastTrail = double.NegativeInfinity;
                var cast8 = false;
                while (S.Clock.Now - waitStart < 90 && S.Clock.Now - started < 600)
                {
                    if (S.Clock.Now - lastTrail >= 2)
                    {
                        lastTrail = S.Clock.Now;
                        trail.Add($"round {round} t={S.Clock.Now - waitStart:0}s: " + string.Join(' ', proven.Select(g => $"{g:X8}={(double.IsNaN(DistanceTo(g)) ? "gone" : DistanceTo(g).ToString("0.#", CultureInfo.InvariantCulture) + "m")}")) + $" | within 3/5/8 m: {TuskersWithin(3)}/{TuskersWithin(5)}/{TuskersWithin(8)}");
                    }

                    if (TuskersWithin(8) >= 3)
                    {
                        foreach (var h in WaitMana(GearCasts.ManaCeiling(ringInfo, 0), 300)) { yield return h; }

                        var w3 = TuskersWithin(3);
                        var w5 = TuskersWithin(5);
                        var w8 = TuskersWithin(8);
                        var t0 = S.Clock.Now;
                        var m0 = _m.Missiles.Count;
                        var l0 = _m.Magic.Count;
                        var k0 = _m.KillTexts.Count;
                        var d0 = _m.Deaths.Count;
                        var cast = S.Actions.Cast(ring);
                        yield return cast;
                        foreach (var h in Pause(8)) { yield return h; }

                        var made = _m.Missiles.Skip(m0).Count(x => x.T >= t0);
                        var lines = _m.Magic.Skip(l0).Where(l => l.Kind == MagicLineKind.DamageDealt).ToList();
                        var kills = Math.Max(_m.KillTexts.Skip(k0).Count(t => t <= t0 + 8), _m.Deaths.Skip(d0).Count(d => d.T <= t0 + 8));
                        rows.Add(string.Create(CultureInfo.InvariantCulture, $"cast{rows.Count + 1}: within_3m={w3} within_5m={w5} within_8m={w8} outcome={cast.Outcome?.Kind}{(cast.Outcome?.Code is > 0 ? $"/0x{cast.Outcome.Value.Code:X}" : string.Empty)} projectiles={made} damage_lines={lines.Count} damage_sum={lines.Sum(l => l.Amount)} kills_in_8s={kills}"));
                        cast8 = true;
                        break;
                    }

                    yield return null;
                }

                var health = Vital(MageTemplate.MaxHealth);
                if (health.Cur * 100 < health.Max * 35u)
                {
                    Check(false, $"health {health.Cur}/{health.Max} fell under 35% with the tuskers on the mage: the probe stops");
                    break;
                }

                if (!cast8)
                {
                    finding = $"round {round}: the {proven.Count} bolted tuskers did not bring 3 within 8 m in 90 s (distances over time in the detail lines)";
                    break;
                }
            }

            Fields = $"spell={ring} casts={rows.Count} finding={finding} {string.Join(" | ", rows)}";
            _more.AddRange(rows);
            _more.AddRange(trail);
        }

        // ---------------------------------------------------------------------------------------------- 5 WingProbe

        /// <summary>The wing's upper floor, from the pinned world data (landblock_instance: the Virindi 23 at cell 0x016C, z -23.97): a point a creature stands on, so the cell is valid. Chosen from cache/m3/3-1/den-map.md (the wing's cells 0x016C-0x018B at z -24).</summary>
        private static readonly TeleportDestination WingTop = Destination("north wing, upper floor (cell 0x0105016C)", 0x0105016C, new Vector3(61.6973f, -70.5784f, -23.971f));

        private IEnumerable<ActionHandle?> WingProbe()
        {
            foreach (var h in GoDen()) { yield return h; }

            foreach (var h in TopUp()) { yield return h; }

            foreach (var h in Mode(CombatMode.Magic)) { yield return h; }

            var health0 = Vital(MageTemplate.MaxHealth);
            Log($"(the kill switch guards Automation actions only; these Host actions do not pass it) health before {health0.Cur}/{health0.Max}");
            var tallyAtStart = _m.Magic.Count;
            foreach (var h in GoTo(WingTop, "the north wing, upper floor")) { yield return h; }

            var stand = S.Clock.Now;
            var deadline = stand + 120;
            var half = double.NaN;
            var died = false;
            var firstHit = double.NaN;
            while (S.Clock.Now < deadline)
            {
                var health = Vital(MageTemplate.MaxHealth);
                if (_m.PlayerDeaths.Count > 0 || health.Cur == 0)
                {
                    died = true;
                    break;
                }

                if (double.IsNaN(firstHit) && _m.Magic.Skip(tallyAtStart).Any(l => l.Kind == MagicLineKind.DamageTaken))
                {
                    firstHit = S.Clock.Now - stand;
                }

                if (health.Cur * 2 <= health.Max)
                {
                    half = S.Clock.Now - stand;
                    break;
                }

                yield return null;
            }

            S.Actions.KillSwitch = true;
            Log($"kill switch flag set at {S.Clock.Now - stand:0.#} s (died={died}, half={(double.IsNaN(half) ? "no" : F(half))})");
            var after = Vital(MageTemplate.MaxHealth);
            var lines = _m.Magic.Skip(tallyAtStart).Where(l => l.Kind is MagicLineKind.DamageTaken or MagicLineKind.DrainTaken).ToList();
            if (!died)
            {
                foreach (var h in GoTo(TeleportCatalog.FromInterior(InteriorLocations.BlackSpawnDenAluvian), "the arrival (the retreat)")) { yield return h; }
            }
            else
            {
                Check(false, "TODO(m5-wing-death-recovery): BSD Mage died in the north wing; its corpse is there. No recovery was attempted (the corpse trip is a stage inside DeathScenario, not a callable piece): the lead recovers it (the mage journey's death station)");
            }

            S.Actions.KillSwitch = false;
            var damage = lines.Where(l => l.Kind == MagicLineKind.DamageTaken).Select(l => (double)l.Amount).ToList();
            Fields = string.Create(
                CultureInfo.InvariantCulture,
                $"spot=0x0105016C stood_s={(S.Clock.Now - stand):0} died={(died ? "yes" : "no")} health_before={health0.Cur}/{health0.Max} health_after={after.Cur}/{after.Max} first_hit_s={(double.IsNaN(firstHit) ? "none" : F(firstHit))} to_50_percent_s={(double.IsNaN(half) ? "not reached" : F(half))} spell_damage_taken={lines.Where(l => l.Kind == MagicLineKind.DamageTaken).Sum(l => l.Amount)} spell_lines={lines.Count} damage_per_line[{MeasureText.Dist(damage)}] retreat={(died ? "none (dead)" : "placed back at the arrival")}");
            _more.AddRange(lines.Select(l => $"{l.T - stand:0.#} s {l.Kind} {l.Amount}{(l.Critical ? " (critical)" : string.Empty)}: {l.Text}"));
        }

        // ---------------------------------------------------------------------------------------------- 8 ManaDance

        private const double RegenReferencePerSecond = 0.44;

        /// <summary>The outcome of a dance try in one phrase, with the exact code or tracker kind (and detail) when it is not a clean refusal, fizzle or busy.</summary>
        private static string DanceOutcome(ActionHandle h) => h.Outcome switch
        {
            null => "other:not settled",
            { Code: 0x0401 } => "refused_short_of_mana(0x0401)",
            { Code: 0x0402 } => "fizzled(0x0402)",
            { Code: 0x001D } => "busy(0x001D)",
            { Kind: ActionOutcomeKind.Confirmed } => "confirmed",
            { Kind: ActionOutcomeKind.Sent } => "sent",
            var o => $"other:{o.Value.Kind}/0x{o.Value.Code:X}{(o.Value.Detail is null ? string.Empty : " " + o.Value.Detail)}",
        };

        /// <summary>
        /// Spends mana down to <paramref name="limit"/> or less by refreshing self buffs the book holds, dearest affordable first. A spend cast the server refused is logged with its code and that spell is not used again; so is a spell whose casts
        /// are accepted three times without the mana falling. <paramref name="used"/> counts the casts by spell name.
        /// </summary>
        private IEnumerable<ActionHandle?> SpendDown(uint limit, List<SpellInfo> spenders, Dictionary<string, int> used, int number)
        {
            var bad = new HashSet<uint>();
            var flat = new Dictionary<uint, int>();
            for (var i = 0; i < 150 && Vital(MageTemplate.MaxMana).Cur > limit; i++)
            {
                var mana = Vital(MageTemplate.MaxMana).Cur;
                var pick = spenders.Where(x => !bad.Contains(x.Id) && GearCasts.ManaCeiling(x, 0) <= mana).OrderByDescending(x => GearCasts.ManaCeiling(x, 0)).FirstOrDefault();
                if (pick is null)
                {
                    _more.Add($"dance {number}: no spend spell left that is affordable at mana {mana} (bad: {string.Join(",", bad)})");
                    yield break;
                }

                var cast = S.Actions.Cast(pick.Id);
                yield return cast;
                var outcome = DanceOutcome(cast);
                foreach (var h in Pause(0.5)) { yield return h; }

                used[pick.Name] = used.GetValueOrDefault(pick.Name) + 1;
                if (outcome is not ("confirmed" or "sent"))
                {
                    bad.Add(pick.Id);
                    _more.Add($"dance {number}: spend cast {pick.Name} ({pick.Id}) refused: {outcome}; not used again");
                    continue;
                }

                if (Vital(MageTemplate.MaxMana).Cur >= mana)
                {
                    flat[pick.Id] = flat.GetValueOrDefault(pick.Id) + 1;
                    if (flat[pick.Id] >= 3)
                    {
                        bad.Add(pick.Id);
                        _more.Add($"dance {number}: spend cast {pick.Name} ({pick.Id}) was accepted ({outcome}) 3 times without the mana falling ({mana}); not used again");
                    }
                }
                else
                {
                    flat[pick.Id] = 0;
                }
            }
        }

        /// <summary>
        /// The stamina to mana dance (the owner's): wait for stamina of 60% or more (up to 120 s), spend mana down to 15 or less (self buffs refreshed, logged), then press Stamina to Mana Self every 0.25 s after each failed try, up to 40 tries, until a cast lands (mana rose).
        /// 5 dances at mana 15 or less, then 1 reference dance at mana 40-60, all with the highest level the book holds (power 300 or under). Everything is in Holtburg, in the magic stance, buffed first.
        /// </summary>
        private IEnumerable<ActionHandle?> ManaDance()
        {
            foreach (var h in GoHoltburg()) { yield return h; }

            foreach (var h in TopUp()) { yield return h; }

            foreach (var h in Mode(CombatMode.Magic)) { yield return h; }

            var book = S.State.Self.Spellbook;
            var line = ArenaProfile.LineNamed("Stamina to Mana Self")!;
            var spell = line.Spells.Where(s => s.Power <= 300 && book.Knows(s.Id)).OrderByDescending(s => s.Power).FirstOrDefault();
            Require(spell is not null, "no Stamina to Mana Self level (power 300 or under) in the spellbook");
            var spenders = new List<SpellInfo>();
            foreach (var l in ArenaProfile.Lines.Where(l => l.IsBuff && l.Role is ProfileRole.CreatureBuff or ProfileRole.LifeBuff))
            {
                foreach (var sp in l.Spells.Where(sp => sp.Power <= 300 && book.Knows(sp.Id)))
                {
                    if (_spells.TryGetValue(sp.Id, out var inf) && GearCasts.FormOf(inf) == CastForm.Self)
                    {
                        spenders.Add(inf);
                    }
                }
            }

            Require(spenders.Count > 0, "no self buff in the book to spend mana with");
            var danceLines = new List<string>();
            var allTries = 0;
            var allLanded = 0;
            var gains = new List<double>();
            var staminaTaken = new List<double>();
            var rates = new List<double>();
            const int dances = 6;
            for (var number = 1; number <= dances; number++)
            {
                var reference = number == dances;
                var limit = reference ? 60u : 15u;
                var floorMana = reference ? 40u : 0u;

                // Stamina to 60% or more first, so the dance has stamina to convert.
                var stamUntil = S.Clock.Now + 120;
                while (Vital(MageTemplate.MaxStamina).Cur * 10 < Vital(MageTemplate.MaxStamina).Max * 6u && S.Clock.Now < stamUntil)
                {
                    yield return null;
                }

                _more.Add($"dance {number}: stamina {Vital(MageTemplate.MaxStamina).Cur}/{Vital(MageTemplate.MaxStamina).Max} when the spend begins");
                var used = new Dictionary<string, int>();
                foreach (var h in SpendDown(limit, spenders, used, number)) { yield return h; }

                if (reference)
                {
                    var wait = S.Clock.Now + 120;
                    while (Vital(MageTemplate.MaxMana).Cur < floorMana && S.Clock.Now < wait)
                    {
                        yield return null; // below 40: regeneration brings it into the 40-60 band
                    }
                }

                _more.Add($"dance {number}: mana spent down with self buffs: {string.Join(", ", used.Select(kv => $"{kv.Key} x{kv.Value}"))}");
                var m0 = Vital(MageTemplate.MaxMana);
                var s0 = Vital(MageTemplate.MaxStamina);
                if (m0.Cur > limit || m0.Cur < floorMana)
                {
                    Check(false, $"dance {number}: mana {m0.Cur}/{m0.Max} is outside the dance's band ({floorMana}-{limit})");
                }

                var tries = 0;
                var refusals = 0;
                var fizzles = 0;
                var busy = 0;
                var other = 0;
                var landed = false;
                var manaAfter = m0.Cur;
                var stamAfter = s0.Cur;
                var tStart = S.Clock.Now;
                var tEnd = tStart;
                while (tries < 40 && !landed)
                {
                    tries++;
                    var mB = Vital(MageTemplate.MaxMana).Cur;
                    var sB = Vital(MageTemplate.MaxStamina).Cur;
                    var t = S.Clock.Now;
                    var cast = S.Actions.Cast(spell!.Id);
                    yield return cast;
                    var outcome = DanceOutcome(cast);
                    foreach (var h in Pause(0.25)) { yield return h; }

                    var mA = Vital(MageTemplate.MaxMana).Cur;
                    var sA = Vital(MageTemplate.MaxStamina).Cur;
                    var accepted = outcome is "confirmed" or "sent";
                    var rose = accepted && mA > mB;
                    var verdict = rose ? "landed"
                        : accepted ? "sent_no_mana_rise"
                        : outcome.StartsWith("refused", StringComparison.Ordinal) ? "refused"
                        : outcome.StartsWith("fizzled", StringComparison.Ordinal) ? "fizzled"
                        : outcome.StartsWith("busy", StringComparison.Ordinal) ? "busy" : outcome;
                    switch (verdict)
                    {
                        case "refused": refusals++; break;
                        case "fizzled": fizzles++; break;
                        case "busy": busy++; break;
                        case "landed": break;
                        default: other++; break;
                    }

                    _more.Add($"dance {number} try {tries} t={t:0.00}: mana {mB} stamina {sB} -> {verdict} ({outcome}); mana {mA} stamina {sA} after {S.Clock.Now - t:0.0} s");
                    if (rose)
                    {
                        landed = true;
                        manaAfter = mA;
                        stamAfter = sA;
                        tEnd = S.Clock.Now;
                        foreach (var h in Pause(2)) { yield return h; }

                        manaAfter = Vital(MageTemplate.MaxMana).Cur;
                        stamAfter = Vital(MageTemplate.MaxStamina).Cur;
                    }
                }

                if (!reference)
                {
                    allTries += tries;
                    allLanded += landed ? 1 : 0;
                }

                if (landed && !reference)
                {
                    var gain = (double)manaAfter - m0.Cur;
                    gains.Add(gain);
                    staminaTaken.Add((double)s0.Cur - stamAfter);
                    rates.Add(gain / Math.Max(0.01, tEnd - tStart));
                }

                danceLines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"dance{number}{(reference ? "(reference, mana 40-60)" : string.Empty)}({spell!.Name} {spell.Id}): tries_to_land={(landed ? tries.ToString(CultureInfo.InvariantCulture) : "never in " + tries)} refusals={refusals} fizzles={fizzles} busy={busy} other={other} mana_before={m0.Cur}/{m0.Max} mana_after={(landed ? manaAfter.ToString(CultureInfo.InvariantCulture) : "n/a")} stamina_spent={(landed ? (long)s0.Cur - stamAfter : 0)} (stamina {s0.Cur}->{(landed ? stamAfter : s0.Cur)}) net_mana_gained={(landed ? (long)manaAfter - m0.Cur : 0)} seconds={tEnd - tStart:0.#}"));
            }

            Check(allTries > 0, "no try was made");
            Fields = string.Create(
                CultureInfo.InvariantCulture,
                $"dances={dances} (5 at mana <= 15, 1 reference at 40-60) tries_at_15_or_less={allTries} landed={allLanded} landing_chance_per_try={(allTries > 0 ? (double)allLanded / allTries : 0):0.###} mana_gained_per_landed[{MeasureText.Dist(gains)}] stamina_taken_per_landed[{MeasureText.Dist(staminaTaken)}] mana_per_second_of_dancing[{MeasureText.Dist(rates)}] regeneration_reference_mps={RegenReferencePerSecond} | {string.Join(" | ", danceLines)}");
            _more.InsertRange(0, danceLines);
        }

        // ---------------------------------------------------------------------------------------------- 7 KitAndVitals

        private IEnumerable<ActionHandle?> KitAndVitals()
        {
            var q = S.State.Self.Qualities;
            var comps = DenMeasure.Components(S);
            var foci = Foci.Select(id => $"{id}:{(S.State.Objects.All.Any(o => o.Weenie.WeenieClassId == id && S.State.Self.Inventory.Contains(o.Guid)) ? "present" : "absent")}");
            var hp = Vital(MageTemplate.MaxHealth);
            var st = Vital(MageTemplate.MaxStamina);
            var mn = Vital(MageTemplate.MaxMana);
            Require(GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var buffed, out var why), "War Magic: " + why);
            Require(q.TryGetSkill(MageTemplate.WarMagic, out var war), "the read model has no War Magic skill");
            Require(q.TryGetAttributeLevels(MageTemplate.Focus, out _, out var focusRaw), "no Focus");
            Require(q.TryGetAttributeLevels(MageTemplate.Self, out _, out var selfRaw), "no Self");
            var warBase = GearCasts.SkillValue(focusRaw, selfRaw, war.Init + war.Ranks, 1.0, null, 0);
            q.TryGetSkill(MageTemplate.ManaConversion, out var conv);
            Fields = string.Create(
                CultureInfo.InvariantCulture,
                $"level={q.Level} components=[{DenMeasure.Format(comps)}] component_types={comps.Count} component_units={comps.Values.Sum()} burden={S.State.Self.Inventory.Burden?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} foci=[{string.Join(' ', foci)}] "
                + $"health={hp.Cur}/{hp.Max} stamina={st.Cur}/{st.Max} mana={mn.Cur}/{mn.Max} war_magic_buffed={buffed} war_magic_base={warBase} war_magic_init={war.Init} war_magic_ranks={war.Ranks} "
                + $"mana_conversion_init={conv.Init} mana_conversion_ranks={conv.Ranks} mana_conversion_class={conv.AdvancementClass} (the formula part of a skill is not in the Mana Conversion figure) vitae={(q.TryGetVitae(out _) ? "yes" : "no")}");
            yield break;
        }
    }
}

using System.Numerics;
using Time = Godot.Time;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Models;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;

namespace Lifestone.Game.Sessions;

// M3 3.1 lane fb (the stand-in's fight) fills this file and no other of PlayChecks. The hook is called once from CheckAllAsync, after the tools teleport and the panel checks (PlayChecks.cs): a long phase
// before the tools teleport makes the stand-in's fixed teleport time miss its tunnel (the 2.2 lesson), so nothing of the fight runs earlier.
public sealed partial class PlayChecks
{
    /// <summary>The stand-in tusker's object id (a server object's: 0x80000000 and up).</summary>
    public const uint TuskerGuid = 0x80000B01;

    /// <summary>How far ahead of the player the tusker stands, in metres.</summary>
    public const float TuskerAheadMetres = 15f;

    /// <summary>The most casts the fight check makes before it fails.</summary>
    public const int FightCastLimit = 10;

    /// <summary>
    /// M3 3.1 fb: a stand-in tusker 15 m ahead of the player, the combat mode Magic, Flame Bolt at it through <c>IActionApi.Cast(spell, target)</c> until it dies (at most <see cref="FightCastLimit"/> casts). Judged by s0's
    /// <see cref="FightTally"/> (kills 1, damage dealt more than 0, every cast Confirmed) and by what the tusker's own object shows (its health fraction before, its Dead motion, its removal); each a plain <c>Check</c> named for what it judges.
    /// Offline: it proves the client's casting path and the stand-in's answers in ACE's messages and order, not ACE's acceptance (the live fight leg does that). One line with the counts.
    /// </summary>
    private async Task FightChecksAsync()
    {
        const string setupName = "the-fight-has-a-session-the-dats-and-a-flame-bolt-the-stand-in-can-cast";
        const string placedName = "a-stand-in-tusker-is-placed-ahead-of-the-player-and-answers-query-health-with-a-full-bar";
        const string killName = "flame-bolt-kills-the-stand-in-tusker-within-ten-casts-kills-1";
        const string castsName = "every-flame-bolt-cast-at-the-stand-in-tusker-is-confirmed";
        const string damageName = "the-flame-bolt-casts-deal-damage-to-the-stand-in-tusker-damage-dealt-over-0";
        const string deadName = "the-stand-in-tusker-gets-the-dead-motion-and-the-kills-experience";
        if (_host.Session is not { } session || _host.StandIn is not { } server || _services.Dats is not { } dats)
        {
            Check(setupName, false, $"session={_host.Session is not null} stand_in_server={_host.StandIn is not null} dats={_services.Dats is not null}");
            return;
        }

        Visit.SpellTable ??= SpellTableData.Load(dats);
        Visit.ProjectileSetup ??= id => AceWorldExtract.Load().ProjectilesBySpell.TryGetValue(id, out var projectile) ? projectile.SetupId : null;
        Visit.CastPlannerFactory ??= () => StandInCastPlanner.FromDats(dats); // the lazy planner (TODO(m3-standin-cast-plan-lazy) closed): used only if the host's worker did not deliver one
        if (Visit.CastPlanner is null)
        {
            await WaitFor(() => Visit.CastPlanner is not null, 20.0);
        }

        // The highest Flame Bolt level the stand-in's character casts without a fizzle (its fixed skill plus 50: StandInCastPlanner.CastablePower).
        var spell = Visit.SpellTable.Spells.Values
            .Where(s => s.Name.StartsWith("Flame Bolt ", StringComparison.Ordinal) && s.Power <= StandInCastPlanner.CastablePower)
            .OrderByDescending(s => s.Power)
            .FirstOrDefault();
        if (spell is null)
        {
            Check(setupName, false, "the SpellTable holds no Flame Bolt the stand-in can cast");
            return;
        }

        var learn = session.Tools.LearnSpells([spell.Id], ActionSource.Host);
        var learned = await WaitFor(() => learn.IsCompleted, 10.0);
        var caster = await HoldCasterAsync(session); // M4 3.1 g: the magic mode needs a caster in hand (the combat-mode guard); the wand stays in hand through the whole-loop check below and goes back to the pack after it
        var mode = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var modeDone = await WaitFor(() => mode.IsCompleted, 10.0);
        Check(setupName, learned && modeDone && session.State.Self.Qualities.CombatMode == CombatModes.Magic, $"spell={spell.Name} id={spell.Id} power={spell.Power} learn={learn.Outcome?.Kind} mode={mode.Outcome?.Kind} combat_mode={session.State.Self.Qualities.CombatMode}");

        // The tusker: 15 m ahead along the player's heading, on the Setup's own default motion table and height (the stand-in has no weenie database: TODO(m3-standin-tusker-stats)).
        var model = ModelAssembly.Load(dats, StandInTusker.Setup);
        var heading = session.State.Self.Position is { } here ? Vector3.Transform(Vector3.UnitY, here.Orientation) : Vector3.UnitY;
        heading = new Vector3(heading.X, heading.Y, 0f);
        heading = heading.LengthSquared() > 1e-6f ? Vector3.Normalize(heading) : Vector3.UnitY;
        var tusker = StandInTusker.Make(TuskerGuid, default, model?.DefaultMotionTableId ?? 0, model?.Height ?? 0f);
        var placedAt = server.PlaceNear(tusker, heading * TuskerAheadMetres);
        var present = await WaitFor(() => session.State.Objects.Contains(TuskerGuid), 10.0);
        var drawn = present && await WaitFor(() => _mode.Layer?.IsDrawn(TuskerGuid) == true, 30.0);
        var query = session.Actions.QueryHealth(TuskerGuid, ActionSource.Host);
        var queried = await WaitFor(() => query.IsCompleted, 10.0);
        var fullBar = session.State.Objects.TryGet(TuskerGuid, out var placed) && placed.Health is { } fraction && Math.Abs(fraction - 1f) < 1e-4f;
        Check(
            placedName,
            placedAt is not null && present && drawn && queried && fullBar,
            $"placed={placedAt is not null} present={present} drawn={drawn} query={query.Outcome?.Kind} health={placed?.Health} setup=0x{StandInTusker.Setup:X8} motion_table=0x{model?.DefaultMotionTableId ?? 0:X8} height={model?.Height:0.00}");

        // The fight: Flame Bolt at the tusker until it dies. The tally counts the server's lines; the tusker's own object shows the Dead motion.
        using var tally = new FightTally();
        tally.Start(session);
        var deadMotion = false;
        var deadWord = MotionCommandTable.ToWire(MotionCommands.Dead);
        using var motionSubscription = session.Events.Subscribe((in ObjectMotionChanged e) =>
        {
            if (e.Guid == TuskerGuid && e.Motion.Forward == deadWord)
            {
                deadMotion = true;
            }
        });
        var casts = 0;
        var confirmed = 0;
        var perCast = new System.Text.StringBuilder(); // one entry per cast (lines so far/hits/misses/kills): what a cast that dealt nothing looks like
        string? failure = null;
        while (tally.Kills == 0 && casts < FightCastLimit && failure is null)
        {
            var cast = session.Actions.Cast(spell.Id, TuskerGuid, ActionSource.Host);
            tally.NoteCast();
            casts++;
            if (!await WaitFor(() => cast.IsCompleted, 30.0))
            {
                failure = $"cast {casts} did not complete";
            }
            else if (cast.Outcome?.Kind == ActionOutcomeKind.Confirmed)
            {
                confirmed++;
            }
            else
            {
                failure = $"cast {casts} was {cast.Outcome?.Kind}: {cast.Outcome?.Detail}";
            }

            // The cast is Confirmed when its projectile is created; the bolt then flies for a second and lands (a damage line, or the kill). The next cast waits for that, as the scenario's loop will.
            var landed = await WaitFor(() => server.BoltsHit + server.BoltsMissed >= server.BoltsLaunched, 15.0);
            failure ??= landed ? null : $"the bolt of cast {casts} did not land";
            perCast.Append(casts).Append(':').Append(tally.DamageLinesDealt).Append('/').Append(server.BoltsHit).Append('/').Append(server.BoltsMissed).Append('/').Append(tally.Kills).Append(' ');
        }

        var sawDead = await WaitFor(() => deadMotion, 5.0);
        var gone = await WaitFor(() => !session.State.Objects.Contains(TuskerGuid), 10.0);
        var total = session.State.Self.Qualities.TotalExperience;
        var health = server.HealthOf(TuskerGuid);
        Check(killName, tally.Kills == 1 && failure is null, $"kills={tally.Kills} casts={casts}/{FightCastLimit} last_kill='{tally.LastKillText}' failure={failure}");
        Check(castsName, casts > 0 && confirmed == casts, $"confirmed={confirmed}/{casts} failure={failure}");
        Check(damageName, tally.DamageDealt > 0 && tally.DamageLinesDealt == casts - 1 && tally.UnparsedMagicLines == 0, $"damage_dealt={tally.DamageDealt} damage_lines={tally.DamageLinesDealt} casts={casts} unparsed_magic_lines={tally.UnparsedMagicLines} (the killing blow has no line)");
        Check(
            deadName,
            sawDead && gone && health == 0f && tally.XpGained == StandInTusker.XpOverride,
            $"dead_motion={sawDead} removed={gone} health={health} xp_gained={tally.XpGained}/{StandInTusker.XpOverride} total_experience={total}");

        Log.Info(
            LogCategory,
            "fight done",
            ("spell", spell.Name),
            ("casts", tally.Casts),
            ("confirmed", confirmed),
            ("kills", tally.Kills),
            ("damage_dealt", tally.DamageDealt),
            ("damage_lines", tally.DamageLinesDealt),
            ("xp_gained", tally.XpGained),
            ("misses", tally.Misses),
            ("per_cast_lines_hits_missed_kills", perCast.ToString()),
            ("bolts_hit", server.BoltsHit),
            ("bolts_missed", server.BoltsMissed),
            ("last_kill", tally.LastKillText ?? "none"));

        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted, 10.0);
        await FightLoopChecksAsync(session, server, dats);
        await StowAsync(session, caster);
    }

    /// <summary>The stand-in tusker of the whole-loop check (a second one: the first is gone two seconds after it died).</summary>
    public const uint LoopTuskerGuid = 0x80000B02;

    /// <summary>The seconds the whole-loop check may take (the loop is about 20 s of session time; the limit is real time).</summary>
    public const double FightLoopSeconds = 90.0;

    /// <summary>
    /// M3 3.1 fd (item 4): the whole <see cref="FightScenario"/> against the stand-in's tusker. "Arena here" (<see cref="FightOptions.ArenaHere"/>): no gear, no buff top-up, no teleport to the den, the floor rule off for the
    /// stand-in's cell, the Flame Bolt level and the range from the stand-in's fixed magic skill; the scenario picks the target with <c>NearestCreature</c>, casts, waits for the damage lines and the kill notice, and ends in
    /// non-combat. Judged (D28): the verdict's <c>kills&gt;=1 damage_dealt&gt;0</c> (<see cref="FightLegVerdict"/>, the leg's own judge) and the avatar set's counters <c>projectiles_shown&gt;=1
    /// projectile_explodes&gt;=1</c> over the fight. Reported: the cast and fight frame maxima (<see cref="FightFrameSampler"/>), the level, the range, the casts and the misses. Offline this proves the loop and the
    /// game's projectile on screen, not ACE's acceptance (the live leg does that).
    /// </summary>
    private async Task FightLoopChecksAsync(IGameSessionView session, StandInServer server, Lifestone.Data.Dat.DatSet dats)
    {
        const string setupName = "the-whole-fight-loop-has-a-stand-in-tusker-in-range-and-the-war-line-learned";
        const string verdictName = "the-whole-fight-scenario-kills-the-stand-in-tusker-kills-1-damage-dealt-over-0";
        const string shownName = "the-whole-fight-scenario-draws-its-bolts-and-their-hits-projectiles-shown-1-projectile-explodes-1";
        if (_mode.Avatars is not { } avatars || Visit.SpellTable is not { } table || ArenaProfile.LineNamed(ArenaProfile.WarLineName) is not { } line)
        {
            Check(setupName, false, $"avatar_set={_mode.Avatars is not null} spell_table={Visit.SpellTable is not null}");
            return;
        }

        // The war line's seven levels in the spellbook (the stand-in's probe book holds only the first), then the tusker ahead, inside the range the scenario will work out from the stand-in's fixed skill.
        var levelIds = new uint[FightScenario.Levels];
        for (var i = 0; i < levelIds.Length; i++)
        {
            levelIds[i] = line.Spells[i].Id;
        }

        var learn = session.Tools.LearnSpells(levelIds, ActionSource.Host);
        var learned = await WaitFor(() => learn.IsCompleted, 10.0);
        var options = new FightOptions { ArenaHere = true, Kills = 1, Seconds = FightLoopSeconds };
        var rangeOfFirst = table.Spells.TryGetValue(levelIds[0], out var first) ? FightScenario.CastRange(first, options.ArenaSkill) : 0f;
        var ahead = Math.Min(TuskerAheadMetres, rangeOfFirst * 0.6f);
        var model = ModelAssembly.Load(dats, StandInTusker.Setup);
        var heading = session.State.Self.Position is { } here ? Vector3.Transform(Vector3.UnitY, here.Orientation) : Vector3.UnitY;
        heading = new Vector3(heading.X, heading.Y, 0f);
        heading = heading.LengthSquared() > 1e-6f ? Vector3.Normalize(heading) : Vector3.UnitY;
        var tusker = StandInTusker.Make(LoopTuskerGuid, default, model?.DefaultMotionTableId ?? 0, model?.Height ?? 0f);
        var placedAt = server.PlaceNear(tusker, heading * ahead);
        var present = await WaitFor(() => session.State.Objects.Contains(LoopTuskerGuid), 10.0);
        var drawn = present && await WaitFor(() => _mode.Layer?.IsDrawn(LoopTuskerGuid) == true, 30.0);
        Check(setupName, learned && placedAt is not null && present && drawn && ahead > 1f, $"learned={learned} placed={placedAt is not null} present={present} drawn={drawn} tusker_ahead_m={ahead:0.0} range_of_level_I_m={rangeOfFirst:0.0}");

        // The whole scenario, stepped once per frame as the leg steps it. The sampler gets the engine's delta, the avatar set's windows and the scenario's target.
        using var scenario = new FightScenario(table.Spells, "stand-in-loop", new BuffOptions(), options, walk: null, GodotLogSink.Instance, endSession: false, stages: FightStages.All);
        var sampler = new FightFrameSampler();
        var lightSampler = new LightFrameSampler(); // M3 4.1 lane c: bolt_light, dyn_lights_max, viewer_light over the stand-in's fight
        var stats = avatars.Stats;
        var (createdBefore, shownBefore, explodesBefore, launchesBefore) = (stats.ProjectilesCreated, stats.ProjectilesShown, stats.ProjectileExplodes, stats.ProjectileLaunches);
        var status = ScenarioStatus.Running;
        var deadline = Time.GetTicksMsec() + (ulong)(FightLoopSeconds * 1000.0);
        while (status == ScenarioStatus.Running && Time.GetTicksMsec() < deadline)
        {
            await Frames(1);
            status = scenario.Step(session, PortalSpaceSetup.Now());
            sampler.Step(GetProcessDeltaTime(), avatars.CastWindowOpen, avatars.ProjectileWindowOpen, scenario.Fighting && scenario.Target != 0, scenario.Fighting, inDen: false, skip: false);
            lightSampler.Step(avatars);
        }

        lightSampler.Finish();
        var tally = scenario.Tally;
        var failure = status == ScenarioStatus.Running ? $"the scenario was still running after {FightLoopSeconds:0} s (stage {scenario.Stage})" : scenario.Failure;
        var projectilesShown = (int)Math.Max(0, stats.ProjectilesShown - shownBefore);
        var explodes = Math.Max(0, stats.ProjectileExplodes - explodesBefore);
        var measures = new FightLegMeasures(
            tally.Kills, tally.DamageDealt, tally.Casts, tally.Misses, tally.XpGained, tally.DamageTaken, tally.HitsTaken, tally.Evades, projectilesShown, sampler.CastFrameMaxMs, sampler.FightFrameMaxMs, sampler.BsdMinFps, double.IsNaN(scenario.FightSeconds) ? null : scenario.FightSeconds, lightSampler.BoltLight, lightSampler.DynamicLightsMax, lightSampler.ViewerLight, BlockedPicks: tally.BlockedPicks, WallHits: tally.WallHits);
        var outcome = FightLegVerdict.Compose(failure, measures, judgeBoltLight: true); // offline the stand-in's bolts are deterministic: bolt_light=yes is judged here (report-only in the live leg)
        Check(
            verdictName,
            status == ScenarioStatus.Succeeded && outcome.Ok && tally.Kills >= 1 && tally.DamageDealt > 0,
            $"{outcome.Line}; stage={scenario.Stage} level={scenario.Level} range_m={scenario.RangeMetres:0.#} failure={failure} problems=[{string.Join("; ", outcome.Problems)}]");
        Check(
            shownName,
            projectilesShown >= 1 && explodes >= 1,
            $"projectiles_shown={projectilesShown} projectile_explodes={explodes} projectiles_created={stats.ProjectilesCreated - createdBefore} projectile_launches={stats.ProjectileLaunches - launchesBefore} scenario_projectiles={scenario.Projectiles}");
        Log.Info(
            LogCategory,
            "fight loop done",
            ("verdict", outcome.Line),
            ("frames", sampler.Frames),
            ("light_flights_judged", lightSampler.FlightsJudged),
            ("light_flights_lit", lightSampler.FlightsLit),
            ("light_short_flights", lightSampler.ShortFlights),
            ("light_state_off_flights", lightSampler.StateOffFlights),
            ("light_flights_alive_lit", lightSampler.FlightsText()),
            ("scenario_verdict", scenario.VerdictFields()));
    }
}

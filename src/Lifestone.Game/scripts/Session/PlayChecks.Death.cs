using System.Numerics;
using Time = Godot.Time;
using Lifestone.Client.Avatars;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Animation;
using Lifestone.Data.Models;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

// M3 3.2 lane db fills this file and no other of PlayChecks; lane dd adds the whole DeathScenario offline here. The hook is called once from CheckAllAsync, after the fight checks (PlayChecks.cs).
public sealed partial class PlayChecks
{
    /// <summary>The stand-in lifestone's object id (a server object's: 0x80000000 and up).</summary>
    public const uint LifestoneGuid = 0x80000C01;

    /// <summary>The attacking tusker's object id.</summary>
    public const uint DeathTuskerGuid = 0x80000C02;

    /// <summary>The seconds of real time the tusker may take to kill the stand-in player (about 20 s of session time at the placeholder rhythm).</summary>
    public const double DeathSeconds = 120.0;

    /// <summary>
    /// M3 3.2 db: the stand-in lifestone is placed beside the player and Used (the bind), then a stand-in tusker that attacks (<c>StandInTusker.Make(..., attacks: true)</c>) is placed within reach and kills the player. Judged by s0's
    /// <see cref="DeathTally"/> and <see cref="DeathLegVerdict.ClassifyRespawn"/> (<c>died=1 respawn=lifestone vitae&gt;0 corpse=1</c>, hits taken at least 1, the vitals about 75% after the arrival, and the lifestone protection turning
    /// the tusker's next blow away), each a plain <c>Check</c>; one <c>death done</c> line. The lifestone binds where the player stands, so the death's teleport brings the player back to the same place and the checks after this one find
    /// the world as they left it (the tusker and the lifestone are removed). Offline: it proves the stand-in's messages in ACE's order and the client's reading of them, not ACE's acceptance (the live leg does that).
    /// </summary>
    private async Task DeathChecksAsync()
    {
        const string setupName = "the-death-has-a-session-a-stand-in-and-a-player-position";
        const string bindName = "the-stand-in-lifestone-is-used-and-binds-the-player";
        const string diedName = "the-stand-in-tusker-kills-the-player-died-1-hits-taken-1";
        const string respawnName = "the-player-respawns-at-the-lifestone-respawn-lifestone-corpse-1";
        const string vitaeName = "the-death-leaves-vitae-and-the-vitals-return-to-about-75-percent";
        const string protectedName = "the-lifestone-protection-turns-the-next-blow-away-protected-1";
        if (_host.Session is not { } session || _host.StandIn is not { } server || session.State.Self.Position is not { } here)
        {
            Check(setupName, false, $"session={_host.Session is not null} stand_in_server={_host.StandIn is not null} position={_host.Session?.State.Self.Position is not null}");
            return;
        }

        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted, 10.0);

        // The lifestone one metre east of the player, Used; the bind is the player's position (the death's teleport returns here).
        var stone = server.PlaceNear(StandInLifestone.Make(LifestoneGuid), new Vector3(1f, 0f, 0f));
        var stonePresent = stone is not null && await WaitFor(() => session.State.Objects.Contains(LifestoneGuid), 10.0);
        Check(setupName, stonePresent && session.State.Self.Qualities.TryGetVitalLevels(1, out var health, out _) && health > 0, $"lifestone_placed={stone is not null} present={stonePresent} cell=0x{here.CellId:X8}");
        if (!stonePresent)
        {
            return;
        }

        var use = session.Actions.Use(LifestoneGuid, ActionSource.Host);
        var used = await WaitFor(() => use.IsCompleted, 20.0);
        var bindLine = LastMagicLine(session);
        Check(bindName, used && use.Outcome?.Kind == ActionOutcomeKind.Confirmed && server.Bound, $"use={use.Outcome?.Kind} bound={server.Bound} line='{bindLine}'");

        // The tusker: within a metre of the player, on the Setup's own motion table, attacking.
        var model = _services.Dats is { } dats ? ModelAssembly.Load(dats, StandInTusker.Setup) : null;
        using var tally = new DeathTally();
        tally.Start(session);
        var tusker = StandInTusker.Make(DeathTuskerGuid, default, model?.DefaultMotionTableId ?? 0, model?.Height ?? 0f, attacks: true);
        var tunnelsBefore = _tunnelShowings;
        var completesBefore = _portal?.Director.LoginCompletesSent ?? 0;
        var placed = server.PlaceNear(tusker, new Vector3(0f, 1f, 0f));
        var started = Time.GetTicksMsec();
        var killed = placed is not null && await WaitFor(() => tally.VictimNotices >= 1, DeathSeconds);
        var seconds = (Time.GetTicksMsec() - started) / 1000.0;
        Check(diedName, killed && tally.VictimNotices == 1 && tally.DeadMotions >= 1 && tally.HitsTaken >= 1, $"died={tally.VictimNotices} dead_motions={tally.DeadMotions} deaths={tally.Deaths} hits_taken={tally.HitsTaken} damage_taken={tally.DamageTaken} evades={tally.Evades} health_min_pct={tally.HealthMinPercent:0.#} seconds={seconds:0.#}");

        // The corpse (after the Dead animation plus a second), the arrival, and the vitals three seconds later.
        var arrived = killed && await WaitFor(() => tally.HasArrival && tally.CorpsesSeen >= 1 && !double.IsNaN(tally.VitalsAfterArrivalPercent), 30.0);
        var respawn = DeathLegVerdict.ClassifyRespawn(tally, session.State.Objects);
        Check(respawnName, arrived && respawn == DeathRespawn.Lifestone && tally.CorpsesSeen == 1 && tally.DeathTeleports == 1, $"respawn={respawn} corpse={tally.CorpsesSeen} teleports={tally.DeathTeleports} arrival_cell=0x{tally.ArrivalCell:X8}");
        var vitals = tally.VitalsAfterArrivalPercent;
        Check(vitaeName, arrived && tally.VitaePercent > 0 && vitals is >= 70.0 and <= 80.0 && tally.Purges >= 1, $"vitae={tally.VitaePercent} vitals_pct={vitals:0.#} (health {tally.HealthAfterArrivalPercent:0.#} stamina {tally.StaminaAfterArrivalPercent:0.#} mana {tally.ManaAfterArrivalPercent:0.#}) purged={tally.Purges}");

        // The tusker is still beside the lifestone: its next blow meets the protection (0x028A 0x0502).
        var protectedBlow = arrived && await WaitFor(() => tally.ProtectedHits >= 1, 30.0);
        Check(protectedName, protectedBlow && server.UnderProtection, $"protected_hits={tally.ProtectedHits} under_protection={server.UnderProtection} server_protected_blows={server.ProtectedBlows} tusker_hits={server.TuskerHits} tusker_evades={server.TuskerEvades}");

        Log.Info(
            LogCategory,
            "death done",
            ("died", tally.VictimNotices),
            ("deaths", tally.Deaths),
            ("vitae", tally.VitaePercent),
            ("respawn", respawn.ToString().ToLowerInvariant()),
            ("corpse", tally.CorpsesSeen),
            ("purged", tally.Purges),
            ("protected", tally.ProtectedHits),
            ("vitals_pct", double.IsNaN(vitals) ? "none" : vitals.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)),
            ("hits_taken", tally.HitsTaken),
            ("damage_taken", tally.DamageTaken),
            ("evades", tally.Evades),
            ("tusker_hits", server.TuskerHits),
            ("seconds", seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));

        // Clean-up: the tusker stops attacking and both objects go, so the checks after this one find the world as before.
        server.Remove(DeathTuskerGuid);
        server.Remove(LifestoneGuid);
        await WaitFor(() => !session.State.Objects.Contains(DeathTuskerGuid) && !session.State.Objects.Contains(LifestoneGuid), 10.0);

        // The death's teleport is a teleport to the client too: its tunnel, its arrival and its LoginComplete must be over before the checks after this one (the picking and the portal) start, or they run inside it.
        var tunnelShown = await WaitFor(() => _tunnelShowings > tunnelsBefore, 10.0);
        var tunnelOver = await WaitFor(() => _portal is { Director.Machine: { TunnelVisible: false, WorldVisible: true } }, 30.0);
        var completed = await WaitFor(() => _portal is { Director.LoginCompletesSent: > 0 } && _portal.Director.LoginCompletesSent > completesBefore, 15.0);
        Check("the-death-teleports-through-portal-space-and-the-world-is-back", tunnelShown && tunnelOver && completed, $"tunnel_showings={_tunnelShowings - tunnelsBefore} tunnel_over={tunnelOver} login_completes={_portal?.Director.LoginCompletesSent - completesBefore}");

        // M3 3.2 dd: the whole DeathScenario (offline, no teleports) and the death on screen.
        await DeathLoopChecksAsync(session, server);
    }

    /// <summary>The second stand-in lifestone's object id (the first is removed at the end of db's check).</summary>
    public const uint DeathLoopLifestoneGuid = 0x80000C03;

    /// <summary>The second attacking tusker's object id.</summary>
    public const uint DeathLoopTuskerGuid = 0x80000C04;

    /// <summary>The seconds of real time the whole offline death loop may take (the wait, the respawn and the re-gear on the stand-in's throttle).</summary>
    public const double DeathLoopSeconds = 420.0;

    /// <summary><c>--death-seconds</c> of the offline loop: the session seconds the tusker may take to kill the stand-in player (about 20 s at the placeholder rhythm, 60 s more under the lifestone protection).</summary>
    public const double DeathLoopWaitSeconds = 180.0;

    /// <summary>
    /// The den's walk of the offline loop: the stand-in tusker stands within a metre of the player and attacks, so there is nothing to walk to (<see cref="IFightWalk"/> needs a walker for the death stage). A leg
    /// is "begun" and never moves; the scenario's idle-leg rule asks again after a minute without a blow.
    /// </summary>
    private sealed class StandInStillWalk : IFightWalk
    {
        public string? Failure => null;

        public void Prepare(IGameSessionView session, double now)
        {
        }

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = "stand-in: the tusker is within reach, no walk";
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now) => FightWalkStatus.Idle;

        public void Abandon()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>What the loop's frames showed of the death on screen (read every frame the scenario is stepped).</summary>
    private sealed class DeathWatch
    {
        public bool DeadPose;
        public bool StandUp;
        public double DeadPoseAtSeconds = double.NaN;
        public uint CorpseGuid;
        public bool CorpseDrawn;
        public bool CorpseDead;
        public bool CorpseSameSetup;
        public uint CorpseSetup;
        public uint PlayerSetup;
        public bool TunnelInDeath;
        public float HealthMeterMax = float.NaN;
        public float HealthMeterMin = float.NaN;
        public float HealthMeterBeforeDead = float.NaN;
        public bool RegearSampled;
        public float HealthAfter = float.NaN;
        public float StaminaAfter = float.NaN;
        public float ManaAfter = float.NaN;
        public string LabelAfter = "n/a";
        public uint SubstateAtEnd;
    }

    /// <summary>
    /// M3 3.2 dd (items 2 and 5): the whole <see cref="DeathScenario"/> against db's stand-in tusker and a second lifestone, and the death on screen read from the same frames. "Arena here": the stages that teleport
    /// (<see cref="DeathStages.Gear"/> with its spell bars, <see cref="DeathStages.Bind"/> and <see cref="DeathStages.Den"/>) do not run; the check does the bind itself (a lifestone beside the player, Used, as db's does), and
    /// the stages that run are <see cref="DeathStages.Death"/> (a still walk: the tusker is in reach) and <see cref="DeathStages.Regear"/>, plus <see cref="DeathStages.Unbuff"/> when the stand-in's player holds no enchantment
    /// (the stage subscribes its tally only in a place or death stage, so its suicide path needs the teleports: with enchantments it is left out and the log says so). Judged by <see cref="DeathLegVerdict.Compose"/>'s judged
    /// fields (<c>died=1 respawn=lifestone gear_restored=yes hits_taken&gt;=1 damage_taken&gt;0</c>) through <see cref="DeathScenario.Outcome"/>. The death on screen is judged from the frames the scenario is stepped in: the Dead
    /// motion on the player's own body (the server's F74C, stance NonCombat) and the stand-up after the respawn, the corpse drawn in the dead pose on the player's Setup, portal space on the death's F751, and the vitals
    /// meters (<see cref="GameVitals"/>) falling with the 0x02E9 updates and standing at about 75% after the arrival. Offline this proves the stand-in's messages in ACE's order and the client's reading and drawing of
    /// them, not ACE's acceptance (the live leg does that). <c>TODO(m3-standin-spellbook-updates)</c> keeps the stand-in's spell bars out of it.
    /// </summary>
    private async Task DeathLoopChecksAsync(IGameSessionView session, StandInServer server)
    {
        const string setupName = "the-whole-death-loop-has-a-lifestone-bound-and-a-session";
        const string verdictName = "the-whole-death-scenario-on-the-stand-in-died-1-respawn-lifestone-gear-restored";
        const string deadName = "the-player-plays-the-dead-motion-on-its-own-body-and-stands-up-after-the-respawn";
        const string corpseName = "the-corpse-is-drawn-in-the-dead-pose-on-the-players-setup";
        const string portalName = "the-death-plays-portal-space-on-its-f751";
        const string vitalsName = "the-vitals-meters-fall-with-the-updates-and-stand-at-about-75-percent-after-the-arrival";
        if (_mode.Avatars is not { } avatars || Visit.SpellTable is not { } table)
        {
            Check(setupName, false, $"avatar_set={_mode.Avatars is not null} spell_table={Visit.SpellTable is not null}");
            return;
        }

        // The bind: a lifestone one metre east, Used where the player stands (the death's teleport returns here), as db's check does.
        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted, 10.0);
        var stone = server.PlaceNear(StandInLifestone.Make(DeathLoopLifestoneGuid), new Vector3(1f, 0f, 0f));
        var stonePresent = stone is not null && await WaitFor(() => session.State.Objects.Contains(DeathLoopLifestoneGuid), 10.0);
        var use = stonePresent ? session.Actions.Use(DeathLoopLifestoneGuid, ActionSource.Host) : null;
        var bound = use is not null && await WaitFor(() => use.IsCompleted, 20.0) && use.Outcome?.Kind == ActionOutcomeKind.Confirmed && server.Bound;
        var buffs = 0;
        foreach (var enchantment in session.State.Self.Qualities.ActiveEnchantments)
        {
            buffs += enchantment.SpellId == DeathTally.VitaeSpell ? 0 : 1;
        }

        var stages = DeathStages.Death | DeathStages.Regear | (buffs == 0 ? DeathStages.Unbuff : DeathStages.None);
        Check(setupName, bound && session.State.Self.Position is not null, $"lifestone_placed={stone is not null} present={stonePresent} bound={bound} enchantments={buffs} stages={stages}");
        if (!bound)
        {
            server.Remove(DeathLoopLifestoneGuid);
            return;
        }

        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var watch = new DeathWatch { PlayerSetup = avatars.Player?.Body.Setup.Id ?? 0 };
        using var scenario = new DeathScenario(table.Spells, "stand-in-death-loop", new DeathOptions { Seconds = DeathLoopWaitSeconds }, new StandInStillWalk(), GodotLogSink.Instance, endSession: false, stages: stages);
        using var deathSampler = new DeathFrameSampler();
        deathSampler.Start(session);
        var fightSampler = new FightFrameSampler();
        var model = _services.Dats is { } dats ? ModelAssembly.Load(dats, StandInTusker.Setup) : null;
        var tunnelsBefore = _tunnelShowings;
        var completesBefore = _portal?.Director.LoginCompletesSent ?? 0;
        server.UnhideAfterTeleport = true; // the offline session refuses LoginComplete, so the stand-in unhides the player after the death's teleport as ACE does (StandInServer.UnhideAfterTeleport)
        var tuskerPlaced = false;
        var status = ScenarioStatus.Running;
        var started = Time.GetTicksMsec();
        var deadline = started + (ulong)(DeathLoopSeconds * 1000.0);
        while (status == ScenarioStatus.Running && Time.GetTicksMsec() < deadline)
        {
            await Frames(1);
            status = scenario.Step(session, PortalSpaceSetup.Now());
            var stage = scenario.Stage;
            if (!tuskerPlaced && stage is "death-mode" or "death-wait")
            {
                // The tusker comes when the scenario is waiting for it: within reach, attacking.
                tuskerPlaced = true;
                var tusker = StandInTusker.Make(DeathLoopTuskerGuid, default, model?.DefaultMotionTableId ?? 0, model?.Height ?? 0f, attacks: true);
                server.PlaceNear(tusker, new Vector3(0f, 1f, 0f));
            }

            var delta = GetProcessDeltaTime();
            var waiting = stage is "death-mode" or "death-wait";
            fightSampler.Step(delta, avatars.CastWindowOpen, avatars.ProjectileWindowOpen, waiting, waiting, inDen: false, skip: false);
            deathSampler.Step(delta, skip: false);
            ObserveDeath(watch, scenario, deathSampler, avatars, shell, stage, (Time.GetTicksMsec() - started) / 1000.0);
        }

        var tally = scenario.Tally;
        var failure = status == ScenarioStatus.Running ? $"the scenario was still running after {DeathLoopSeconds:0} s (stage {scenario.Stage})" : scenario.Failure;
        var outcome = scenario.Outcome(deathSampler.DeathFrameMaxMs, fightSampler.FightFrameMaxMs);
        if (failure is not null && outcome.Ok)
        {
            outcome = DeathLegVerdict.Compose(failure, default);
        }

        Check(
            verdictName,
            status == ScenarioStatus.Succeeded && outcome.Ok && tally.VictimNotices == 1 && scenario.GearRestored,
            $"{outcome.Line}; stage={scenario.Stage} stages={stages} unbuff={scenario.UnbuffNote} failure={failure} problems=[{string.Join("; ", outcome.Problems)}]");

        // The Dead motion on the player's own body, and the stand-up: the motion table's substate (the pose the avatar draws) read every frame.
        Check(
            deadName,
            watch.DeadPose && watch.StandUp,
            $"dead_motions={deathSampler.DeadMotions} dead_pose_seen={watch.DeadPose} at_s={watch.DeadPoseAtSeconds:0.0} stood_up_after_respawn={watch.StandUp} substate_at_end=0x{watch.SubstateAtEnd:X8} (Dead 0x{MotionCommands.Dead:X8}, Ready 0x{MotionCommands.Ready:X8})");

        // The corpse: its own avatar (the CreateObject's Setup and motion table, the player's, in the Dead forward motion), drawn by the server-objects layer, not the player's body.
        Check(
            corpseName,
            tally.CorpsesSeen >= 1 && watch.CorpseGuid != 0 && watch.CorpseSameSetup && watch.CorpseDead && watch.CorpseDrawn,
            $"corpses={tally.CorpsesSeen} guid=0x{watch.CorpseGuid:X8} setup=0x{watch.CorpseSetup:X8} player_setup=0x{watch.PlayerSetup:X8} dead_pose={watch.CorpseDead} drawn={watch.CorpseDrawn} corpse_before_portal={deathSampler.CorpseBeforePortal}");

        // Portal space on the death's F751, as a teleport's: the tunnel showed between the F751 and the arrival, and its showing was counted.
        Check(
            portalName,
            deathSampler.Portals >= 1 && deathSampler.Arrivals >= 1 && watch.TunnelInDeath && _tunnelShowings > tunnelsBefore,
            $"portals={deathSampler.Portals} arrivals={deathSampler.Arrivals} tunnel_visible_between={watch.TunnelInDeath} tunnel_showings={_tunnelShowings - tunnelsBefore}");

        // The vitals meters: health fell to the 0x02E9's zero and, after the arrival, all three stand at the 75% (of the maximum with the vitae applied).
        bool Near(float v) => v is >= 0.70f and <= 0.80f;
        Check(
            vitalsName,
            shell?.Vitals?.Chosen is not null && watch.HealthMeterBeforeDead > 0.5f && watch.HealthMeterMin <= 0.001f && watch.RegearSampled && Near(watch.HealthAfter) && Near(watch.StaminaAfter) && Near(watch.ManaAfter),
            $"health_meter_before_death={watch.HealthMeterBeforeDead:0.00} min={watch.HealthMeterMin:0.00} after_arrival health={watch.HealthAfter:0.00} stamina={watch.StaminaAfter:0.00} mana={watch.ManaAfter:0.00} label='{watch.LabelAfter}' sampled={watch.RegearSampled}");

        Log.Info(
            LogCategory,
            "death loop done",
            ("verdict", outcome.Line),
            ("stages", stages.ToString()),
            ("death_frame_max_ms", deathSampler.DeathFrameMaxMs ?? double.NaN),
            ("fight_frame_max_ms", fightSampler.FightFrameMaxMs ?? double.NaN),
            ("death_frames", deathSampler.Frames),
            ("scenario_verdict", scenario.VerdictFields()));

        server.Remove(DeathLoopTuskerGuid);
        server.Remove(DeathLoopLifestoneGuid);
        await WaitFor(() => !session.State.Objects.Contains(DeathLoopTuskerGuid) && !session.State.Objects.Contains(DeathLoopLifestoneGuid), 10.0);

        // The death's teleport is a teleport to the client too: its tunnel, its arrival and its LoginComplete must be over before the checks after this one start.
        await WaitFor(() => _portal is { Director.Machine: { TunnelVisible: false, WorldVisible: true } }, 30.0);
        await WaitFor(() => _portal is { Director.LoginCompletesSent: > 0 } && _portal.Director.LoginCompletesSent > completesBefore, 15.0);
        server.UnhideAfterTeleport = false;
    }

    /// <summary>One frame of the death on screen: the player's pose, the corpse, the tunnel and the vitals meters.</summary>
    private void ObserveDeath(DeathWatch watch, DeathScenario scenario, DeathFrameSampler sampler, AvatarSet avatars, GamePlayScreen? shell, string stage, double seconds)
    {
        var substate = avatars.Player?.Body.Parts?.State.Substate ?? 0;
        watch.SubstateAtEnd = substate;
        if (sampler.DeadMotions > 0 && substate == MotionCommands.Dead && !watch.DeadPose)
        {
            watch.DeadPose = true;
            watch.DeadPoseAtSeconds = seconds;
        }

        if (watch.DeadPose && sampler.Arrivals > 0 && substate == MotionCommands.Ready)
        {
            watch.StandUp = true;
        }

        var guid = scenario.Tally.LastCorpseGuid;
        if (guid != 0)
        {
            watch.CorpseGuid = guid;
            if (avatars.TryGet(guid, out var corpse))
            {
                watch.CorpseSetup = corpse.Body.Setup.Id;
                watch.CorpseSameSetup = watch.CorpseSetup == watch.PlayerSetup && watch.PlayerSetup != 0;
                watch.CorpseDead |= corpse.Body.Parts?.State.Substate == MotionCommands.Dead;
            }

            watch.CorpseDrawn |= _mode.Layer?.IsDrawn(guid) == true;
        }

        if (sampler.Portals > 0 && _portal is { Director.Machine.TunnelVisible: true })
        {
            watch.TunnelInDeath = true;
        }

        if (shell?.Vitals?.Chosen is not { HealthMeter: { } health } window)
        {
            return;
        }

        var position = health.Position;
        if (sampler.DeadMotions == 0)
        {
            watch.HealthMeterBeforeDead = float.IsNaN(watch.HealthMeterBeforeDead) ? position : MathF.Max(watch.HealthMeterBeforeDead, position);
        }

        if (sampler.DeadMotions > 0 && (float.IsNaN(watch.HealthMeterMin) || position < watch.HealthMeterMin))
        {
            watch.HealthMeterMin = position;
        }

        if (!watch.RegearSampled && stage == "regear" && window.StaminaMeter is { } stamina && window.ManaMeter is { } mana)
        {
            watch.RegearSampled = true;
            (watch.HealthAfter, watch.StaminaAfter, watch.ManaAfter) = (position, stamina.Position, mana.Position);
            watch.LabelAfter = window.HealthLabel?.Text.Trim() ?? "n/a";
        }
    }

    private static string LastMagicLine(IGameSessionView session)
    {
        for (var i = session.State.Chat.Count - 1; i >= 0; i--)
        {
            var line = session.State.Chat[i];
            if (line.ChatType == FightTally.MagicChatType)
            {
                return line.Text;
            }
        }

        return "none";
    }
}

using System.Globalization;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The journey's in-station frames and showpiece bursts (M4 P5b J7, closing <c>TODO(m4-journey-station-frames)</c>): the frames the old legs took inside their stations, named <c>&lt;stem&gt;-&lt;station&gt;-&lt;slot&gt;.png</c>: the hunt's melee window frames
/// (<c>c1-combat</c>, <c>c1-powerbar</c>, <c>meter</c>: <see cref="HuntMeleeShotTriggers"/>, the camera of <see cref="JourneyShotCamera"/> and the hunt's side turn when a frame's target is hidden), the den fight's bolt, hit and kill
/// (<see cref="FightShotPlan"/>), the death's (<see cref="DeathShotPlan"/>) and the buff's <c>mid-cast</c>; and, with <c>--journey-shots</c>, the bursts at the showpiece events (<see cref="ShowpieceWatch"/>). The plans are the legs' own and pure;
/// this file only reads the engine and saves. Nothing here changes what a station does: a frame is taken at its moment and a frame whose condition fails is dropped, never waited for.
/// </summary>
public sealed partial class JourneyLegRunner
{
    private readonly JourneyShotCamera _camera = new();
    private readonly ShowpieceWatch _watch = new();
    private NavRunner? _runCounted;
    private int _runs;
    private string? _cameraStation;
    private IDisposable? _powerSubscription;
    private float _runPower;
    private uint _selectedFor;
    private float _meterAtSelection = float.NaN;
    private bool _playerBoxRefusalLogged;
    private double _delta;

    private FightShotPlan _fightPlan = new();
    private string? _fightStation;
    private System.Numerics.Vector3 _fightLast;
    private bool _fightHavePosition;

    private readonly DeathShotPlan _deathPlan = new();
    private readonly DeathFrameSampler _deathSampler = new();
    private string? _deathStation;
    private bool _deathArmed;

    private string? _buffStation;
    private bool _buffBaseSet;
    private long _buffScriptsBase;
    private double _buffEffectAt = double.NaN;
    private bool _buffDone;

    /// <summary>The seconds after the cast effect the buff's <c>mid-cast</c> frame is taken (BuffLegRunner's second frame).</summary>
    private const double MidCastSeconds = 0.4;

    private bool ArcherPlan => string.Equals(_plan?.Character, FighterTemplate.ArcherName, StringComparison.Ordinal);

    /// <summary>The frame's work for the station that is running: the clock of the bursts, the camera, the showpiece events and the stations' own frames. Called once a frame after the journey stepped.</summary>
    private void StepInStationFrames(IGameSessionView session, JourneyScenario scenario)
    {
        _observer.Tick(_runSeconds);
        var station = _observer.Current;
        StepShotCamera(station);
        StepShowpieces(session, scenario, station);
        if (_stem is null || station is null)
        {
            return;
        }

        switch (scenario.CurrentRun)
        {
            case IStationInner { Scenario: FightScenario fight }:
                StepFightFrames(session, station, fight);
                break;
            case IStationInner { Scenario: DeathScenario death }:
                StepDeathFrames(session, station, death);
                break;
            case IStationInner { Scenario: BuffScenario buff }:
                StepBuffFrame(station, buff);
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------ the camera

    private void StepShotCamera(string? station)
    {
        if (_cameraStation == station)
        {
            return;
        }

        _camera.Restore(_mode);
        _cameraStation = station;
        if (station is null || _stem is null)
        {
            return;
        }

        var shots = _observer.Shots.Enabled;
        var kind = station switch
        {
            JourneyStationNames.Hunt => ArcherPlan ? JourneyCameraKind.MissileSide : JourneyCameraKind.MeleeFramed,
            JourneyStationNames.DenFight when shots => JourneyCameraKind.SideTurn,
            _ => JourneyCameraKind.Retail,
        };
        if (_camera.Frame(_mode, kind, _runSeconds))
        {
            if (kind == JourneyCameraKind.MeleeFramed && shots)
            {
                _camera.TurnNext(_mode, _runSeconds); // the hero shots of the Mace are not all from behind: retail's camera rotation, a side
            }

            Log.Info(LogCategory, $"journey: the review camera is framed for station {station} ({kind})", ("scale", _camera.Scale), ("heading_offset", _camera.SideDegrees));
        }
    }

    // ------------------------------------------------------------------------------------------------ the showpiece events

    private void StepShowpieces(IGameSessionView session, JourneyScenario scenario, string? station)
    {
        var self = session.State.Self;
        var inDen = self.Travel == SelfTravel.Present && self.Position is { } at && (at.CellId >> 16) == BlackSpawnDen.Landblock;
        var casts = 0;
        uint spell = 0;
        if (scenario.CurrentRun is IStationInner { Scenario: FightScenario fight })
        {
            (casts, spell) = (fight.Tally.Casts, fight.SpellId);
        }

        if (_runners?.CurrentRunner is { State: NavRunState.Running, RouteMetres: >= ShowpieceEvents.RunShotMetres } runner && !ReferenceEquals(runner, _runCounted))
        {
            _runCounted = runner;
            _runs++; // each run on foot once, once it is under way
        }

        var attack = session.Attack;
        var input = new ShowpieceInput(station, attack.Swings, attack.Shots, casts, spell, _mode.Avatars?.CastWindowOpen == true, inDen, _runs);
        foreach (var name in _watch.Observe(in input))
        {
            if (station is null)
            {
                continue;
            }

            _observer.StationEvent(station, name, _runSeconds);
            Log.Info(LogCategory, $"journey: showpiece '{name}' in station {station}" + (_observer.Shots.Enabled ? $": a burst of {_observer.Shots.BurstFrames} frame(s) every {_observer.Shots.StepSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s" : " (--journey-shots is off)"));
        }
    }

    // ------------------------------------------------------------------------------------------------ the hunt's melee window frames

    /// <summary>Triggers the melee hunt's three window frames (slots 0 to 2 of <see cref="HuntUiShotPlan"/>) from the combat window, the attack sequence and the selection display; selects the hunt's target as a click would.</summary>
    private void TriggerMeleeShots(IGameSessionView session, HuntScenario hunt, GameCombatPanel? combat, GamePlayScreen? shell)
    {
        _powerSubscription ??= session.Events.Subscribe<AttackRequested>((in AttackRequested e) => _runPower = e.Power);
        var display = shell?.SelectionUi;
        var target = hunt.Target;
        if (target != 0 && _selectedFor != target)
        {
            _selectedFor = target;
            _meterAtSelection = float.NaN;
            _ = _mode.Selection.Select(target);
        }

        var attack = session.Attack;
        if (display is { HealthShown: true, HealthFraction: { } shown } && display.Selected == target && target != 0 && float.IsNaN(_meterAtSelection) && attack.Swings == 0)
        {
            _meterAtSelection = shown;
        }

        var meter = combat?.GetChildRecursive(GameCombatPanel.MeterId) as UiMeter;
        var near = ObjectDistanceMetres(session, target) is { } d && d <= HuntUiShotPlan.TargetRangeMetres;
        var melee = combat is { IsShown: true } && combat.Mode == CombatModes.Melee;
        float? health = display is { HealthShown: true, HealthFraction: { } now } && display.Selected == target ? now : null;
        var slots = HuntMeleeShotTriggers.Evaluate(near, attack.Active, attack.Swings, melee, meter?.Position ?? float.NaN, _runPower, health, _meterAtSelection);
        if ((slots & HuntMeleeSlots.Combat) != 0)
        {
            _uiPlan.Trigger(0, _runSeconds);
        }

        if ((slots & HuntMeleeSlots.PowerBar) != 0)
        {
            _uiPlan.Trigger(1, _runSeconds);
        }

        if ((slots & HuntMeleeSlots.Meter) != 0)
        {
            _uiPlan.Trigger(2, _runSeconds);
        }
    }

    /// <summary>Takes the frame of a melee slot (0 to 2) when its condition still holds at its moment; else drops the trigger (and turns the camera to the next side when the target is hidden).</summary>
    private void SaveMeleeShot(IGameSessionView session, HuntScenario hunt, string station, int slot, GameCombatPanel? combat, GamePlayScreen? shell)
    {
        var display = shell?.SelectionUi;
        var meter = combat?.GetChildRecursive(GameCombatPanel.MeterId) as UiMeter;
        var attack = session.Attack;
        var target = hunt.Target;
        var near = ObjectDistanceMetres(session, target) is { } d && d <= HuntUiShotPlan.TargetRangeMetres;
        var melee = combat is { IsShown: true } && combat.Mode == CombatModes.Melee;
        var level = meter?.Position ?? float.NaN;
        var healthNow = display?.HealthFraction ?? float.NaN;
        var pose = _mode.Rig?.Chase.Pose;
        var pivot = pose is { } p ? PivotDistanceMetres(p) : float.NaN;
        var framed = !_camera.Framed || (pose is { } q && HuntUiShotPlan.CameraFramed(pivot, q.Blocked, _camera.Scale));
        var holds = near && framed && combat is not null && slot switch
        {
            0 => melee && attack.Active,
            1 => melee && attack.Active && !float.IsNaN(level) && HuntUiShotPlan.MidCharge(level, _runPower),
            _ => display is not null && display.Selected == target && HuntUiShotPlan.MeterLowered(_meterAtSelection, healthNow),
        };
        var targetBox = default(ScreenRect);
        var playerBox = default(ScreenRect);
        var overlap = 0f;
        var playerKnown = false;
        if (holds && combat is not null)
        {
            var projected = _mode.Picking.TryProject(target, out targetBox, out var onScreen);
            playerKnown = _mode.Picking.TryProjectIncludingPlayer(session.State.Self.Guid, out playerBox, out _);
            if (!playerKnown && !_playerBoxRefusalLogged)
            {
                _playerBoxRefusalLogged = true;
                Log.Warn(LogCategory, "journey: the player's box did not project at the first melee review frame; the projection rule judges no overlap until it does", ("why", _mode.Picking.WhyNotProjected(session.State.Self.Guid, allowPlayer: true) ?? "no input refused it now"));
            }

            if (!(projected && HuntUiShotPlan.TargetVisible(in targetBox, onScreen, in playerBox, playerKnown, combat.ScreenBox.Y0, out overlap)))
            {
                var settled = _runSeconds - _camera.SideSetAt >= HuntUiShotPlan.SideSettleSeconds;
                if (settled)
                {
                    _camera.TurnNext(_mode, _runSeconds);
                }

                Log.Info(LogCategory, $"journey: the {HuntUiShotPlan.Names[slot]} frame's target is hidden or off screen at its moment; " + (settled ? "the camera turns to the next side" : "the camera is still settling"), ("target_box", targetBox.ToString()), ("overlap", overlap), ("heading_offset", _camera.SideDegrees));
                _uiPlan.Drop(slot);
                return;
            }
        }

        if (!holds)
        {
            Log.Info(LogCategory, $"journey: the {HuntUiShotPlan.Names[slot]} frame's condition no longer held at its moment; waiting for the next trigger", ("near", near), ("camera_framed", framed), ("combat_window_melee", melee), ("run_active", attack.Active), ("meter", level), ("run_power", _runPower));
            _uiPlan.Drop(slot);
            return;
        }

        var saved = SaveStationFrame(station, HuntUiShotPlan.Names[slot], HuntUiShotPlan.Triggers[slot], ("target", hunt.TargetName.Length == 0 ? "n/a" : hunt.TargetName), ("player_overlap", overlap), ("heading_offset", _camera.SideDegrees), ("combat_meter", level), ("run_power", _runPower), ("health_meter_now", healthNow));
        if (saved)
        {
            _uiPlan.MarkTaken(slot);
        }
        else
        {
            _uiPlan.MarkFailed(slot, "the frame could not be saved");
        }
    }

    // ------------------------------------------------------------------------------------------------ the fight's three

    private void StepFightFrames(IGameSessionView session, string station, FightScenario fight)
    {
        if (_mode.Avatars is not { } avatars)
        {
            return;
        }

        var stats = avatars.Stats;
        if (_fightStation != station)
        {
            _fightStation = station;
            _fightPlan = new FightShotPlan();
            _fightPlan.Begin(stats.ProjectilesCreated, stats.ProjectileExplodes, fight.Tally.DamageLinesDealt, 0);
            _fightHavePosition = false;
        }

        var self = session.State.Self;
        var inDen = self.Travel == SelfTravel.Present && self.Position is { } at && (at.CellId >> 16) == BlackSpawnDen.Landblock;
        var here = self.Position is { } p ? p.Frame.Origin : default;
        var moving = !_fightHavePosition || (here - _fightLast).LengthSquared() > 0.0025f;
        (_fightLast, _fightHavePosition) = (here, self.Position is not null);
        var due = _fightPlan.Step(_runSeconds, stats.ProjectilesCreated, stats.ProjectileExplodes, fight.Tally.DamageLinesDealt, fight.Tally.Kills, avatars.ProjectileWindowOpen, inDen, fight.Walks >= 1, moving);
        if (due < 0)
        {
            return;
        }

        if (SaveStationFrame(station, FightShotPlan.Names[due], FightShotPlan.Triggers[due], ("cast_window_open", avatars.CastWindowOpen), ("projectile_window_open", avatars.ProjectileWindowOpen)))
        {
            _fightPlan.MarkTaken(due);
        }
        else
        {
            _fightPlan.MarkFailed(due, "the frame could not be saved");
        }
    }

    // ------------------------------------------------------------------------------------------------ the death's

    private void StepDeathFrames(IGameSessionView session, string station, DeathScenario death)
    {
        if (_deathStation != station)
        {
            _deathStation = station;
            _deathArmed = false;
            _deathSampler.Dispose();
            _deathSampler.Start(session);
        }

        _deathSampler.Step(_delta, _skipNextFrame);
        if (!_deathArmed)
        {
            if (death.Stage is not ("death-mode" or "death-wait") && death.Tally.VictimNotices == 0)
            {
                return;
            }

            _deathArmed = true;
            _deathPlan.Begin(death.Tally.HitsTaken, _deathSampler.DeadMotions, _deathSampler.Corpses, _deathSampler.Arrivals);
        }

        var self = session.State.Self;
        var underFull = self.Qualities.TryGetVitalLevels(1, out var current, out var max) && max != 0 && current < max;
        var due = _deathPlan.Step(_runSeconds, death.Tally.HitsTaken, underFull, _deathSampler.DeadMotions, _deathSampler.Corpses, _deathSampler.CorpseBeforePortal, _deathSampler.Arrivals, self.Travel != SelfTravel.Present, death.CorpseOpen);
        if (due < 0)
        {
            return;
        }

        if (SaveStationFrame(station, DeathShotPlan.Names[due], "the death plan's trigger (DeathShotPlan)", ("stage", death.Stage)))
        {
            _deathPlan.MarkTaken(due);
        }
        else
        {
            _deathPlan.MarkFailed(due, "the frame could not be saved");
        }
    }

    // ------------------------------------------------------------------------------------------------ the buff's mid-cast

    private void StepBuffFrame(string station, BuffScenario buff)
    {
        if (_mode.Avatars is not { } avatars)
        {
            return;
        }

        if (_buffStation != station)
        {
            (_buffStation, _buffBaseSet, _buffEffectAt, _buffDone) = (station, false, double.NaN, false);
        }

        if (_buffDone)
        {
            return;
        }

        if (double.IsNaN(_buffEffectAt))
        {
            var watched = buff.DriverOn
                ? string.Equals(buff.Stage, "driver", StringComparison.Ordinal)
                : string.Equals(buff.Stage, "cycle", StringComparison.Ordinal) || string.Equals(buff.Stage, "rebuff", StringComparison.Ordinal);
            if (!watched)
            {
                return;
            }

            if (!_buffBaseSet)
            {
                (_buffBaseSet, _buffScriptsBase) = (true, avatars.Stats.CastScripts);
            }
            else if (avatars.Stats.CastScripts > _buffScriptsBase)
            {
                _buffEffectAt = _runSeconds; // the first cast that plays a script on the player (a self buff's effect)
            }

            return;
        }

        if (_runSeconds < _buffEffectAt + MidCastSeconds)
        {
            return;
        }

        _buffDone = true;
        SaveStationFrame(station, JourneyFrameNames.BuffMidCast, "the first cast of the cycle that plays a script on the player + 0.4 s", ("cast_window_open", avatars.CastWindowOpen));
    }

    // ------------------------------------------------------------------------------------------------ helpers

    /// <summary>Saves one in-station frame as <c>&lt;stem&gt;-&lt;station&gt;-&lt;slot&gt;.png</c> and records it; true when saved.</summary>
    private bool SaveStationFrame(string station, string slot, string trigger, params (string Key, object? Value)[] fields)
    {
        HideDeveloperHud();
        var name = JourneyFrameNames.InStation(station, slot);
        var path = JourneyFrameNames.PathFor(_stem!, name);
        var all = new (string Key, object? Value)[fields.Length + 4];
        all[0] = ("frame", "journey-" + name);
        all[1] = ("station", station);
        all[2] = ("trigger", trigger);
        all[3] = ("developer_hud", HudShowing() ? "VISIBLE" : "hidden");
        fields.CopyTo(all, 4);
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, all);
        Log.Info(LogCategory, (saved ? "journey: review frame " : "journey: the review frame could not be saved: ") + name + " (" + trigger + "): " + path);
        _skipNextFrame = true;
        if (saved)
        {
            _framesSaved++;
            _observer.NoteTaken(name);
        }

        return saved;
    }

    private static double? ObjectDistanceMetres(IGameSessionView session, uint guid)
    {
        var state = session.State;
        if (guid == 0 || state.Self.Position is not { } mine || !state.Objects.TryGet(guid, out var target) || target.Position is not { } theirs)
        {
            return null;
        }

        return PositionAgreement.DistanceMetres(in mine, in theirs);
    }

    private static float PivotDistanceMetres(in Lifestone.Client.Camera.CameraPose pose)
    {
        var viewer = DrawInterpolation.InSpaceOf(pose.Viewer, pose.Pivot.CellId);
        return System.Numerics.Vector3.Distance(pose.Pivot.Origin, viewer.Origin);
    }
}

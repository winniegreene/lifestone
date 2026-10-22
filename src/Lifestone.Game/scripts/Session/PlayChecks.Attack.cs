using System.Numerics;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

// M4 1.2 s3 filled this file and no other of PlayChecks beyond the one hook call in CheckAllAsync (after the kit checks, before the boot). M4 1.2 s2 moved the rows' sends onto the Action API
// (IActionApi.Attack and MissileAttack): each row judges the tracker's outcome (Confirmed) beside the stand-in's counts it judged before.
public sealed partial class PlayChecks
{
    /// <summary>The weenie class ids of the stand-in's mace, bow and arrows (stand-in item kinds; the stand-in has no weenie database: <c>TODO(m3-standin-weenies)</c>).</summary>
    private const uint AttackMaceWcid = 90_001;

    private const uint AttackBowWcid = 90_002;

    private const uint AttackArrowWcid = 90_003;

    /// <summary>The stand-in tuskers of the melee and the missile row.</summary>
    public const uint MeleeTuskerGuid = 0x80000B03;

    /// <summary>See <see cref="MeleeTuskerGuid"/>.</summary>
    public const uint MissileTuskerGuid = 0x80000B04;

    /// <summary>See <see cref="MeleeTuskerGuid"/>: the tusker of the health-meter row (M4 1.3 s0).</summary>
    public const uint MeterTuskerGuid = 0x80000B05;

    /// <summary>The seconds a row may wait for its attack sequence to end (the wait is real time; a sequence is about 8 s of session time).</summary>
    public const double AttackSequenceSeconds = 40.0;

    /// <summary>
    /// M4 1.2 s3: a melee kill and a missile hit on the stand-in. Melee: a mace wielded, the Melee mode, a tusker with 60 health 2 m ahead, ONE Action API Attack (a TargetedMeleeAttack); judged: the tracker's outcome Confirmed, the sequence ended with 01A7 {0x0036}
    /// (<c>AttackSequencesEnded</c> and <c>LastAttackDoneError</c>), more than one swing came of the one send (the repeat), the tusker died (kills +1, gone). Missile: the mace to the pack, a bow and arrows wielded, the
    /// Missile mode, a tusker 4 m ahead with 40 health, ONE Action API MissileAttack (a TargetedMissileAttack); judged: the tracker's outcome Confirmed, the sequence ended, at least one arrow launched and one hit, the tusker died. Both offline: they prove the client's side of
    /// the order and the stand-in's answers (<c>cache/m4/1-2/capture.md</c> C1, C3), not ACE's acceptance (the live loops do that).
    /// </summary>
    private async Task AttackChecksAsync(IGameSessionView session)
    {
        const string setupName = "the-attack-rows-have-a-stand-in-server-and-a-mace-a-bow-and-arrows";
        const string meleeName = "one-melee-attack-send-repeats-and-kills-the-stand-in-tusker-and-ends-with-01a7-0x0036";
        const string missileName = "one-missile-attack-send-launches-arrows-hits-and-kills-the-stand-in-tusker";
        if (_host.StandIn is not { } server)
        {
            Check(setupName, false, "the stand-in session has no server levers");
            return;
        }

        var items = new Dictionary<uint, StandInItemKind>(Visit.Items ?? new Dictionary<uint, StandInItemKind>())
        {
            [AttackMaceWcid] = new("Mace (stand-in)", null),
            [AttackBowWcid] = new("Short Bow (stand-in)", null),
            [AttackArrowWcid] = new("Arrow (stand-in)", 250),
        };
        Visit.Items = items;
        var mace = await SpawnAttackItemAsync(session, AttackMaceWcid, null);
        var bow = await SpawnAttackItemAsync(session, AttackBowWcid, null);
        var arrows = await SpawnAttackItemAsync(session, AttackArrowWcid, 5);
        Check(setupName, mace != 0 && bow != 0 && arrows != 0, $"mace={mace != 0} bow={bow != 0} arrows={arrows != 0}");
        if (mace == 0 || bow == 0 || arrows == 0)
        {
            return;
        }

        server.PlayerHitRate = 1.0; // the rows are about the order and the repeat, not the dice

        // M4 1.3 s2: attack_frame_max_ms, the worst frame from an attack's send to its run's Done (AttackFrameSampler), printed on the rows and not judged (D11: judged from 2.1).
        var frameSampler = new AttackFrameSampler();
        using var frameSubscription = frameSampler.Attach(session.Events);
        var frameProbe = new AttackFrameProbe(frameSampler);
        AddChild(frameProbe);

        // M4 1.3 s0 (1.2 review R8): the rows judge the session's own state (IGameSessionView: the attack sequence, the kill notice (01AD), the target gone from the object table, the ammunition stack); the stand-in's counters below are diagnostic text only.
        var killNotices = 0;
        using var killSubscription = session.Events.Subscribe((in PlayerKilledSomething e) => killNotices++);

        // --- melee ---
        // M4 3.1 g: a caster an earlier row left in hand (the cast row's wand, the death scenario's gear) is put away first: ACE refuses a melee mode and the wield of a mace while one is in hand, and so does the combat-mode guard.
        var casterAway = await StowHeldAsync(session);
        var wield = session.Actions.Wield(mace, EquipMask.MeleeWeapon, ActionSource.Host);
        var wielded = await WaitFor(() => wield.IsCompleted, 10.0);
        var melee = session.Actions.SetCombatMode(CombatMode.Melee, ActionSource.Host);
        var inMelee = await WaitFor(() => melee.IsCompleted, 10.0) && session.State.Self.Qualities.CombatMode == CombatModes.Melee;
        var killsBefore = server.Kills;
        var killNoticesBefore = killNotices;
        var endedBefore = server.AttackSequencesEnded;
        PlaceAttackTusker(session, server, MeleeTuskerGuid, 2f, 60);
        var present = await WaitFor(() => session.State.Objects.Contains(MeleeTuskerGuid), 10.0);
        var attack = session.Actions.Attack(MeleeTuskerGuid, AttackHeight.Medium, 0.5f, ActionSource.Host);
        var attackConfirmed = await WaitFor(() => attack.IsCompleted, 25.0) && attack.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        // The verdict reads the session: the sequence is Done with the 01A7 {0x0036}, it counted the swings, the 01AD notice arrived, the tusker left the object table (the stand-in's counters are diagnostic text).
        var ended = await WaitFor(() => session.Attack.State == AttackSequenceState.Done && session.Attack.Number > 0, AttackSequenceSeconds);
        var gone = await WaitFor(() => !session.State.Objects.Contains(MeleeTuskerGuid), 10.0);
        var swings = session.Attack.Swings;
        var meleeDone = session.Attack.LastWeenieError;
        var meleeKills = killNotices - killNoticesBefore;
        Check(
            meleeName,
            wielded && inMelee && present && attackConfirmed && ended && gone && meleeKills == 1 && swings >= 2 && meleeDone == 0x36,
            $"attack_frame_max_ms={FrameMs(frameSampler)} caster_away={casterAway} wielded={wielded} melee_mode={inMelee} present={present} attack_outcome={attack.Outcome?.Kind.ToString() ?? "pending"} sequence_done={ended} gone={gone} kill_notices={meleeKills} swings={swings} last_01a7=0x{meleeDone:X4} (stand_in: kills={server.Kills - killsBefore} swings={server.Swings} ended={server.AttackSequencesEnded - endedBefore})");

        // --- the health meter (M4 1.3 s0): select a fighting tusker, read the toolbar meter ELEMENT's position, attack it, and the meter after the first hit is lower ---
        await HealthMeterRowAsync(session, server);

        // --- missile ---
        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted, 10.0);
        var stow = session.Actions.MoveToPack(mace, 0, 0, ActionSource.Host);
        var stowed = await WaitFor(() => stow.IsCompleted, 10.0);
        var bowWield = session.Actions.Wield(bow, EquipMask.MissileWeapon, ActionSource.Host);
        var bowDone = await WaitFor(() => bowWield.IsCompleted, 10.0);
        var arrowWield = session.Actions.Wield(arrows, EquipMask.MissileAmmo, ActionSource.Host);
        var arrowDone = await WaitFor(() => arrowWield.IsCompleted, 10.0);
        var missile = session.Actions.SetCombatMode(CombatMode.Missile, ActionSource.Host);
        var inMissile = await WaitFor(() => missile.IsCompleted, 10.0) && session.State.Self.Qualities.CombatMode == CombatModes.Missile;
        var killsBeforeShot = server.Kills;
        var arrowsBefore = server.ArrowsLaunched;
        var hitsBefore = server.PlayerHits;
        var endedBeforeShot = server.AttackSequencesEnded;
        var ammoBefore = session.Ammo?.Stack ?? 0u;
        var killNoticesBeforeShot = killNotices;
        var sequenceBefore = session.Attack.Number;
        PlaceAttackTusker(session, server, MissileTuskerGuid, 4f, 40);
        var presentShot = await WaitFor(() => session.State.Objects.Contains(MissileTuskerGuid), 10.0);
        var shot = session.Actions.MissileAttack(MissileTuskerGuid, AttackHeight.Medium, 0.5f, ActionSource.Host);
        var shotConfirmed = await WaitFor(() => shot.IsCompleted, 15.0) && shot.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        // The verdict reads the session: the new sequence is Done with 01A7 {0x0036}, it counted the shots, the held ammunition's stack fell by them (a spent last arrow deletes the stack: no ammunition held), the 01AD notice arrived, the tusker left.
        var endedShot = await WaitFor(() => session.Attack.Number > sequenceBefore && session.Attack.State == AttackSequenceState.Done, AttackSequenceSeconds);
        var goneShot = await WaitFor(() => !session.State.Objects.Contains(MissileTuskerGuid), 10.0);
        var shots = session.Attack.Shots;
        var shotDone = session.Attack.LastWeenieError;
        var ammoSpent = (int)ammoBefore - (int)(session.Ammo?.Stack ?? 0u);
        var shotKills = killNotices - killNoticesBeforeShot;
        Check(
            missileName,
            stowed && bowDone && arrowDone && inMissile && presentShot && shotConfirmed && endedShot && goneShot && shotKills == 1 && shots >= 1 && ammoSpent == shots && shotDone == 0x36,
            $"attack_frame_max_ms={FrameMs(frameSampler)} mace_stowed={stowed} bow={bowDone} arrows={arrowDone} missile_mode={inMissile} present={presentShot} attack_outcome={shot.Outcome?.Kind.ToString() ?? "pending"} sequence_done={endedShot} gone={goneShot} kill_notices={shotKills} shots={shots} ammo_spent={ammoSpent} last_01a7=0x{shotDone:X4} (stand_in: kills={server.Kills - killsBeforeShot} arrows={server.ArrowsLaunched - arrowsBefore} hits={server.PlayerHits - hitsBefore} ended={server.AttackSequencesEnded - endedBeforeShot})");

        // Back to peace, the bow and arrows put away, so the checks after this one see the player as the kit checks left it.
        var rest = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => rest.IsCompleted, 10.0);
        foreach (var held in new[] { bow, arrows })
        {
            var put = session.Actions.MoveToPack(held, 0, 0, ActionSource.Host);
            await WaitFor(() => put.IsCompleted, 10.0);
        }

        Log.Info(LogCategory, "attack rows done", ("melee_swings", swings), ("shots", shots), ("kill_notices", killNotices), ("attack_frame_max_ms", FrameMs(frameSampler)), ("attack_windows", frameSampler.Windows), ("attack_frames", frameSampler.Frames));
        frameProbe.QueueFree();
    }

    private static string FrameMs(AttackFrameSampler sampler) => sampler.AttackFrameMaxMs is { } ms ? ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "n/a";

    /// <summary>
    /// M4 1.3 s0 (docs/retail-ui.md section 21.7): the toolbar's health meter follows the pushes. The tusker (2000 health, so no hit kills it) is selected through the shell's selection, the meter element's position is read
    /// (<c>GameSelection.HealthFraction</c> is the UiMeter's own Position, not the stand-in's value), one Action API Attack (source Host) is sent, and the row passes when the meter's position after the first hit is lower than at selection.
    /// The first hit reaches the meter because ACE (and the stand-in) push a 01C0 with every 01B1 to the player that selected the creature.
    /// </summary>
    private async Task HealthMeterRowAsync(IGameSessionView session, StandInServer server)
    {
        const string name = "the-toolbar-health-meter-drops-after-the-first-hit-on-the-selected-tusker";
        var display = (_ui?.Screens?.Flow.CurrentScreen as GamePlayScreen)?.SelectionUi;
        if (display is null)
        {
            Check(name, false, "the shell has no selection display");
            return;
        }

        PlaceAttackTusker(session, server, MeterTuskerGuid, 2f, 2000);
        var present = await WaitFor(() => session.State.Objects.Contains(MeterTuskerGuid), 10.0);
        _mode.Selection.Select(MeterTuskerGuid);
        var selected = await WaitFor(() => display.Selected == MeterTuskerGuid && display.HealthShown, 10.0);
        var atSelection = display.HealthFraction ?? float.NaN;
        var swingsBefore = session.Attack.Swings;
        var numberBefore = session.Attack.Number;
        var attack = session.Actions.Attack(MeterTuskerGuid, AttackHeight.Medium, 0.5f, ActionSource.Host);
        var lowered = await WaitFor(() => display.HealthFraction is { } now && now < atSelection - 1e-4f, 15.0);
        var afterHit = display.HealthFraction ?? float.NaN;
        var hitSeen = session.Attack.Number > numberBefore || session.Attack.Swings > swingsBefore;
        Check(
            name,
            present && selected && lowered && afterHit < atSelection && hitSeen,
            $"present={present} selected={selected} meter_at_selection={atSelection:0.000} meter_after_first_hit={afterHit:0.000} attack_outcome={attack.Outcome?.Kind.ToString() ?? "pending"} swings={session.Attack.Swings} (stand_in_health={server.HealthOf(MeterTuskerGuid)?.ToString("0.000") ?? "none"}, diagnostic only)");

        // Put the fight away: cancel the run, clear the selection.
        var cancel = session.Actions.CancelAttack(ActionSource.Host);
        await WaitFor(() => cancel.IsCompleted, 10.0);
        await WaitFor(() => session.Attack.State == AttackSequenceState.Done || !session.Attack.Active, 15.0);
        _mode.Selection.Clear();
        await WaitFor(() => display.Selected == 0, 5.0);
    }

    private async Task<uint> SpawnAttackItemAsync(IGameSessionView session, uint wcid, int? stack)
    {
        var spawn = session.Tools.SpawnItem(wcid, stack, ActionSource.Host);
        return await WaitFor(() => spawn.IsCompleted, 10.0) && spawn.Result is { } result ? result.Guid : 0u;
    }

    private static void PlaceAttackTusker(IGameSessionView session, StandInServer server, uint guid, float metres, uint health)
    {
        var heading = session.State.Self.Position is { } here ? Vector3.Transform(Vector3.UnitY, here.Orientation) : Vector3.UnitY;
        heading = new Vector3(heading.X, heading.Y, 0f);
        heading = heading.LengthSquared() > 1e-6f ? Vector3.Normalize(heading) : Vector3.UnitY;
        server.PlaceNear(StandInTusker.Make(guid, default, maxHealth: health), heading * metres);
    }
}

/// <summary>Feeds the attack rows' frame sampler one engine delta per frame (the sampler has no clock of its own; M4 1.3 s2).</summary>
internal sealed partial class AttackFrameProbe(AttackFrameSampler sampler) : Godot.Node
{
    /// <inheritdoc/>
    public override void _Process(double delta) => sampler.Step(delta, false);
}

using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.World;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.World;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Sessions;

// M3 4.1 lane r1 fills this file. The hook is called once from CheckAllAsync, after the portal has put the player in the Academy (a cell with a static light set of its own), where a Flame Bolt is cast.
public sealed partial class PlayChecks
{
    /// <summary>The viewer light's radiance (white x <see cref="DynamicLightList.ViewerIntensity"/>) in a row.</summary>
    private const float ViewerRadiance = DynamicLightList.ViewerIntensity;

    /// <summary>How near (metres) a row entry must be to the bolt's frame entry, converted the same way, to be that entry.</summary>
    private const float BoltEntryToleranceMetres = 0.05f;

    /// <summary>The viewer light must be <see cref="DynamicLightList.ViewerHeight"/> above the player within this (m).</summary>
    private const float ViewerRiseToleranceMetres = 0.01f;

    /// <summary>The bolt's review frame is taken on this matched frame (the bolt is some way into its flight, so the wall near it is lit).</summary>
    private const int BoltFrameAfterRows = 4;

    private static bool SameRadiance(AcVector3 a, AcVector3 b) => MathF.Abs(a.X - b.X) < 0.01f && MathF.Abs(a.Y - b.Y) < 0.01f && MathF.Abs(a.Z - b.Z) < 0.01f;

    /// <summary>
    /// M3 4.1 r1: the objects lit. The stand-in's Flame Bolt flies in a dungeon cell. While it flies the player's light row, read back from the table the shader samples (not from the avatar set), holds an entry at the bolt
    /// (<c>dyn_lights_rows</c> counts the frames that did, judged &gt;= 1), ahead of the cell's static lights; with the bolt gone the row holds the viewer light (<c>viewer_light_row</c>, judged 1: one entry of the viewer's
    /// radiance 2 m above the player). Report-only: the frames the bolt was in the row of the stand-in's guard.
    /// </summary>
    private async Task LightsChecksAsync()
    {
        try
        {
            await LightsChecksCoreAsync();
        }
        finally
        {
            RestoreLightsHud();
        }
    }

    private readonly List<(Node Node, bool Visible)> _lightsHidden = [];

    /// <summary>With <c>--play-frames</c>: hides the developer HUD (the names <c>--play-screenshot</c> hides; the shell's own panels stay) so the review frames show the cell, not the diagnostics.</summary>
    private void HideLightsHud()
    {
        foreach (var name in PlayMode.PlayShotDeveloperHudNames)
        {
            switch (GetTree().Root.FindChild(name, recursive: true, owned: false))
            {
                case CanvasLayer layer:
                    _lightsHidden.Add((layer, layer.Visible));
                    layer.Visible = false;
                    break;
                case CanvasItem item:
                    _lightsHidden.Add((item, item.Visible));
                    item.Visible = false;
                    break;
            }
        }
    }

    private void RestoreLightsHud()
    {
        foreach (var (node, visible) in _lightsHidden)
        {
            if (!IsInstanceValid(node))
            {
                continue;
            }

            switch (node)
            {
                case CanvasLayer layer:
                    layer.Visible = visible;
                    break;
                case CanvasItem item:
                    item.Visible = visible;
                    break;
            }
        }

        _lightsHidden.Clear();
    }

    /// <summary>Saves one lights review frame (<c>lights-bolt-in-cell</c>, <c>lights-viewer-in-cell</c>) under the frames folder's <c>extra</c> sub-folder, whose path the writer logs; false when none was asked for or it could not be saved.</summary>
    private bool SaveLightsFrame(string name, int boltEntries)
    {
        if (_framesDirectory is not { } directory)
        {
            return false;
        }

        _tunnelTallySkip = 2;
        var path = $"{ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y)}/extra/{ShellFramesPlan.FileName(name)}";
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", name), ("cell", $"0x{_mode.Rig?.Controller.Position.CellId ?? 0:X8}"), ("bolt_entries_in_row", boltEntries), ("developer_hud", "hidden"));
        if (!saved)
        {
            FrameFailed($"{name} could not be saved");
        }

        return saved;
    }

    private async Task LightsChecksCoreAsync()
    {
        const string name = "a-bolt-and-the-viewer-light-are-in-the-players-light-row";
        if (_host.Session is not { } session || _mode.Avatars is not { } avatars || _mode.Rig is not { } rig || _mode.Layer is null)
        {
            Check(name, false, "no session, avatar set, player or server-objects layer");
            return;
        }

        if (rig.Controller.Position.CellId >> 16 != AcademyCell >> 16)
        {
            Check(name, false, $"cell=0x{rig.Controller.Position.CellId:X8}: the player is not in the Academy");
            return;
        }

        Visit.SpellTable ??= Lifestone.Data.Magic.SpellTableData.Load(_services.Dats!);
        Visit.ProjectileSetup ??= id => Lifestone.Data.Magic.AceWorldExtract.Load().ProjectilesBySpell.TryGetValue(id, out var projectile) ? projectile.SetupId : null;
        var learn = session.Tools.LearnSpells([FlameBoltSpellId], ActionSource.Host);
        var learned = await WaitFor(() => learn.IsCompleted, 10.0);
        var magic = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var inMagic = await WaitFor(() => magic.IsCompleted, 10.0) && session.State.Self.Qualities.CombatMode == CombatModes.Magic;

        if (_framesDirectory is not null)
        {
            HideLightsHud();
            await Frames(2);
        }

        var boltFrameSaved = false;
        var createdBefore = avatars.Stats.ProjectilesCreated;
        var cast = session.Actions.Cast(FlameBoltSpellId, ActionSource.Host);
        var guid = 0u;
        var framesAlive = 0;
        var boltRows = 0;
        var boltFirst = 0; // the bolt's entry came before every static light in the row (the dynamic lights go first)
        var maxInRow = 0;
        var inFrame = 0; // frames in which the bolt's own light was in the avatar set's frame (not yet the row)
        var maxFrame = 0;
        var nearest = double.MaxValue;
        uint bolt0State = 0;
        var deadline = Time.GetTicksMsec() + 20_000UL;
        while (Time.GetTicksMsec() < deadline)
        {
            await Frames(1);
            if (guid == 0)
            {
                if (avatars.Stats.ProjectilesCreated <= createdBefore)
                {
                    continue;
                }

                guid = avatars.LastProjectileGuid;
            }

            if (!avatars.TryGet(guid, out var bolt))
            {
                break; // F747 removed it
            }

            framesAlive++;
            bolt0State = bolt.Body.State;
            var snapshot = avatars.DynamicLightFrame;
            maxFrame = Math.Max(maxFrame, snapshot.Count);
            for (var k = 0; k < snapshot.Count; k++)
            {
                if (snapshot[k].OwnerId == guid)
                {
                    inFrame++;
                    break;
                }
            }

            // The bolt's own entry (R5): the frame's light whose owner is the bolt, converted with the same ToRender the row's lights go through, then looked for in the row by
            // position and radiance, so a static cell light near the bolt's path cannot stand in for it.
            var found = -1;
            var inRow = 0;
            var hasBoltLight = false;
            var boltRender = default(AcVector3);
            var boltRadiance = default(AcVector3);
            for (var k = 0; k < snapshot.Count; k++)
            {
                if (snapshot[k].OwnerId == guid)
                {
                    ref readonly var light = ref snapshot[k];
                    var w = _world.ToRender(AcCoordinates.LandblockOrigin(LandblockCoord.FromId(light.CellId)) + light.Position);
                    boltRender = new AcVector3(w.X, w.Y, w.Z);
                    boltRadiance = light.Radiance;
                    hasBoltLight = true;
                    break;
                }
            }

            for (var i = 0; i < ObjectLightSets.MaxLights && rig.View.TryReadLight(i, out var p, out _, out var radiance); i++)
            {
                inRow++;
                if (!hasBoltLight)
                {
                    continue;
                }

                var d = AcVector3.Distance(p, boltRender);
                nearest = Math.Min(nearest, d);
                if (found < 0 && d <= BoltEntryToleranceMetres && SameRadiance(radiance, boltRadiance))
                {
                    found = i;
                }
            }

            maxInRow = Math.Max(maxInRow, inRow);
            if (found >= 0)
            {
                boltRows++;
                boltFirst += found == 0 ? 1 : 0;
                if (!boltFrameSaved && boltRows >= BoltFrameAfterRows && _framesDirectory is not null)
                {
                    boltFrameSaved = SaveLightsFrame("lights-bolt-in-cell", boltRows);
                }
            }
        }

        var castDone = await WaitFor(() => cast.IsCompleted, 10.0);
        await WaitFor(() => !avatars.ProjectileWindowOpen, 5.0);
        await Frames(3);

        // The bolt is gone: the row holds the viewer light (white, the viewer's intensity, 2 m above the player).
        var viewer = 0;
        var rowNow = 0;
        var playerAt = rig.Controller.Position;
        var playerWorld = _world.ToRender(AcCoordinates.LandblockOrigin(LandblockCoord.FromId(playerAt.CellId)) + playerAt.Origin);
        var rise = 0f;
        for (var i = 0; i < ObjectLightSets.MaxLights && rig.View.TryReadLight(i, out var p, out _, out var radiance); i++)
        {
            rowNow++;
            if (MathF.Abs(radiance.X - ViewerRadiance) < 0.01f && MathF.Abs(radiance.Y - ViewerRadiance) < 0.01f && MathF.Abs(radiance.Z - ViewerRadiance) < 0.01f)
            {
                viewer++;
                rise = p.Y - playerWorld.Y; // Godot up is +Y
            }
        }

        var viewerFrameSaved = false;
        if (_framesDirectory is not null)
        {
            await Frames(2);
            viewerFrameSaved = SaveLightsFrame("lights-viewer-in-cell", 0);
        }

        var riseOk = MathF.Abs(rise - DynamicLightList.ViewerHeight) <= ViewerRiseToleranceMetres;

        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted, 10.0);

        Check(
            name,
            learned && inMagic && guid != 0 && boltRows >= 1 && viewer == 1 && riseOk,
            $"cell=0x{playerAt.CellId:X8} cast_outcome={(castDone ? cast.Outcome?.Kind.ToString() ?? "none" : "pending")} bolt_frames={framesAlive} bolt_in_frame={inFrame} max_frame_lights={maxFrame} bolt_state=0x{bolt0State:X} dyn_lights_rows={boltRows} bolt_entry_first={boltFirst} max_lights_in_row={maxInRow} nearest_entry_m={nearest:0.00} "
            + $"viewer_light_row={viewer} row_lights_after={rowNow} viewer_light_above_player_m={rise:0.000} viewer_rise_ok={riseOk} frame_lights={avatars.DynamicLightFrame.Count} "
            + $"frames: lights-bolt-in-cell={(_framesDirectory is null ? "none (no --play-frames)" : boltFrameSaved ? "saved" : "NOT TAKEN")} lights-viewer-in-cell={(_framesDirectory is null ? "none (no --play-frames)" : viewerFrameSaved ? "saved" : "NOT TAKEN")} "
            + "(judged: dyn_lights_rows>=1 matched by the bolt's own frame entry (owner id, position, radiance), viewer_light_row=1, the viewer 2.0 m above the player within 0.01 m; the row is read from the table the shader samples)");
    }
}

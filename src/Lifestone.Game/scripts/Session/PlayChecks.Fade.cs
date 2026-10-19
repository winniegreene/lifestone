using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Camera;
using Lifestone.Client.Play;
using Lifestone.Game.Core;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Sessions;

// M3 4.1 lane f fills this file. The hook is called once from CheckAllAsync, after the lights check.
public sealed partial class PlayChecks
{
    /// <summary>The camera's distances from the pivot the fade check visits: clear of the fade, inside it, and inside the player.</summary>

    /// <summary>The wand's Setup (weenie 8552 "The Healer's Heart", ACE world database 0.9.296 <c>weenie_properties_d_i_d</c> type 1).</summary>
    private const uint WandSetupId = 0x020009AD;

    private static readonly float[] FadeDistances = [1.0f, 0.3f, 0.15f];

    /// <summary>
    /// M3 4.1 f: the first-person fade (<c>CameraSet::UpdateCamera</c>, 11.6096 0x00458C00). The chase camera is brought to 1.0, 0.3 and 0.15 m from its pivot (its scale, then settled and measured from the pose);
    /// at each the translucency written to the player's drawn instances (<c>PlayerView.AppliedTranslucency</c>, the <c>INSTANCE_CUSTOM.w</c> it hands the batches) and the wand's (the avatar set's frame, which
    /// <c>ServerObjectsLayer</c> draws from) are read back. Judged: 0 at 1.0 m; strictly between 0 and 1 at 0.3 m (and the player drawn through the blended twin); 1 (not drawn) at 0.15 m; the wand's the same at each.
    /// The camera is put back after.
    /// </summary>
    private async Task FadeChecksAsync()
    {
        const string name = "the-player-and-the-wand-fade-as-the-camera-closes-in";
        if (_host.Session is not { } session || _mode.Avatars is not { } avatars || _mode.Rig is not { } rig || avatars.Player is not { } player)
        {
            Check(name, false, "no session, avatar set or player");
            return;
        }

        var spawn = session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
        var spawned = await WaitFor(() => spawn.IsCompleted && spawn.Result is not null, 20.0);
        var wand = spawned ? spawn.Result?.Guid ?? 0 : 0;
        var inventory = session.State.Self.Inventory;
        await WaitFor(() => wand != 0 && session.State.Objects.Contains(wand), 10.0);
        session.Actions.Wield(wand, GearKit.Wand.ValidLocations, ActionSource.Host);
        var wielded = await WaitFor(() => wand != 0 && inventory.TryGetEquipped(wand, out _), 20.0);
        // The stand-in sends the parent event on a wield, as ACE does (M3 4.1 fix A, R6; closes TODO(m3-standin-wield-parent)): the avatar set holds the real wand as the player's child.
        var haveChild = await WaitFor(() => player.Body.Children.Count > 0, 3.0);
        var childIsWand = false;
        var childGuids = string.Empty;
        var childSetup = 0u;
        for (var i = 0; i < player.Body.Children.Count; i++)
        {
            childIsWand |= player.Body.Children[i].Guid == wand;
            childSetup = player.Body.Children[i].Guid == wand ? player.Body.Children[i].Setup.Id : childSetup;
            childGuids += $" 0x{player.Body.Children[i].Guid:X8}";
        }
        // The held child is drawn with the wand's own Setup (weenie 8552, 0x020009AD), never the player's: a copy of the player's model in the hand was the stand-in's placeholder art (M3 4.1 fix A2).
        var setupIsWand = childIsWand && childSetup == WandSetupId && childSetup != player.Body.Setup.Id;
        if (!childIsWand || !setupIsWand)
        {
            Log.Error("play", "the wield produced no child avatar for the wand", ("wand", $"0x{wand:X8}"), ("have_child", haveChild), ("children", childGuids), ("child_setup", $"0x{childSetup:X8}"), ("player_setup", $"0x{player.Body.Setup.Id:X8}"));
        }

        await Frames(10);
        var model = rig.Chase.Model;
        var scale = model.Scale;
        var inHead = model.InHead;
        var rows = new List<string>();
        var ok = spawned && wielded && childIsWand && setupIsWand;
        var wandSeen = false;
        float? playerPrevious = null;
        foreach (var metres in FadeDistances)
        {
            model.Scale = metres / 2.6125f; // |(0, -2.5, 0.75)| times the scale is the distance from the pivot
            model.SetDefaultOffsets();
            var settled = await WaitFor(() => Math.Abs(PivotDistance(rig.Chase.Pose) - metres) < (metres < 0.2f ? 0.05f : 0.02f), 5.0);
            await Frames(4);
            var measured = PivotDistance(rig.Chase.Pose);
            var applied = rig.View.AppliedTranslucency;
            var wandApplied = WandTranslucency(avatars, player, out var found);
            wandSeen |= found;
            var twin = rig.View.TwinParts;
            bool good;
            if (metres >= 0.45f)
            {
                good = applied == 0f && (!found || wandApplied == 0f);
            }
            else if (metres > 0.2f)
            {
                good = applied is > 0f and < 1f && twin > 0 && (!found || wandApplied is > 0f and < 1f);
            }
            else
            {
                good = applied >= 1f && (!found || wandApplied >= 1f);
            }

            ok &= settled && good;
            playerPrevious = applied;
            rows.Add($"at {metres:0.00} m (measured {measured:0.000}, settled={settled}): player={applied:0.00} twin_parts={twin} wand={(found ? wandApplied.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "n/a")} {(good ? "ok" : "BAD")}");
        }

        model.Scale = scale;
        model.SetDefaultOffsets();
        if (inHead)
        {
            model.SetInHead();
        }

        await Frames(30);
        var back = rig.View.AppliedTranslucency;
        var unwield = session.Actions.Unwield(wand, ActionSource.Host);
        await WaitFor(() => unwield.IsCompleted, 10.0);
        ok &= wandSeen && back == 0f;
        Check(name, ok, $"{string.Join("; ", rows)}; wand_wielded={wielded} wand_setup=0x{childSetup:X8} (want 0x{WandSetupId:X8}, not the player's 0x{player.Body.Setup.Id:X8}) setup_is_wand={setupIsWand} wand_child={(childIsWand ? "the wand's own avatar (the stand-in sent the parent event)" : "MISSING")} wand_avatar_seen={wandSeen} back_at_default_player={back:0.00} last_player={playerPrevious:0.00} "
            + "(the player's value is read from the instances PlayerView wrote; the wand's from the avatar set's frame, which ServerObjectsLayer draws from)");
    }

    private static float PivotDistance(in CameraPose pose)
    {
        var viewer = DrawInterpolation.InSpaceOf(pose.Viewer, pose.Pivot.CellId);
        return AcVector3.Distance(pose.Pivot.Origin, viewer.Origin);
    }

    /// <summary>The largest part translucency of the player's held items in the avatar set's frame.</summary>
    private static float WandTranslucency(AvatarSet avatars, PlayerAvatar player, out bool found)
    {
        found = false;
        var largest = 0f;
        var frame = avatars.Frame;
        foreach (var draw in frame.Avatars)
        {
            if (draw.IsPlayer || !avatars.TryGet(draw.Guid, out var avatar) || avatar.Body.TopLevelId != player.Guid)
            {
                continue;
            }

            found = true;
            foreach (var t in frame.PartTranslucency.Slice(draw.PartStart, draw.PartCount))
            {
                largest = Math.Max(largest, t);
            }

            largest = Math.Max(largest, draw.Translucency);
        }

        return largest;
    }
}

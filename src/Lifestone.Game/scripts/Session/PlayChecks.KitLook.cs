using Lifestone.Client.Play;
using Godot;
using Lifestone.Game.Core;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Game.Diagnostics;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M3 4.2 p6 follow-up, with <c>--play-frames</c> only: BSD Mage's kit (D43) is worn by the stand-in's player and the owner's review frames are taken: the player in the chase camera from behind and from the front, and the
/// paperdoll. The stand-in sends the player's ObjDescEvent on each wield (<c>StandInWorld.Dress.cs</c>) and the host dresses it from the dats, so the frames show the kit as a live ACE would send it. The pieces stay worn
/// afterwards (the panel frames that follow show the paperdoll too); nothing is judged here beyond the wields and the frames being taken.
/// </summary>
public sealed partial class PlayChecks
{
    private async Task KitLookFramesAsync(IGameSessionView session, GamePlayScreen? shell)
    {
        if (_framesDirectory is not { } directory || _mode.Rig is not { } rig)
        {
            return;
        }

        const string name = "the-stand-in-wears-the-kit-and-its-review-frames-are-taken";
        var kit = new List<(uint Wcid, EquipMask Where)> { (GearKit.Wand.Wcid, GearKit.Wand.ValidLocations) };
        foreach (var piece in GearKit.Armour)
        {
            kit.Add((piece.Wcid, piece.ValidLocations));
        }

        // The review frames are taken in Holtburg's open air (the Academy cell the earlier checks leave the player in is too narrow for the front view): a tool Place there, as the owner's tools panel does.
        if (Lifestone.Client.Navigation.TeleportCatalog.Default().Find("Holtburg") is { } holtburg)
        {
            var place = session.Tools.Place(holtburg, ActionSource.Host);
            await WaitFor(() => place.IsCompleted, 15.0);
            await WaitFor(() => _portal is { Director.Machine: { TunnelVisible: false, WorldVisible: true } }, 20.0);
            await Frames(90);
        }

        var wielded = 0;
        foreach (var (wcid, where) in kit)
        {
            uint guid = 0;
            foreach (var obj in session.State.Objects.All)
            {
                if (obj.Weenie.WeenieClassId == wcid && obj.IsSelf is false && session.State.Self.Inventory.Find(obj.Guid).Held)
                {
                    guid = obj.Guid;
                    break;
                }
            }

            if (guid == 0)
            {
                continue;
            }

            var inventory = session.State.Self.Inventory;
            if (inventory.TryGetEquipped(guid, out _))
            {
                wielded++;
                continue;
            }

            session.Actions.Wield(guid, where, ActionSource.Host);
            if (await WaitFor(() => inventory.TryGetEquipped(guid, out _), 10.0))
            {
                wielded++;
            }
        }

        await Frames(30);
        if (_mode.Avatars is { } everyone)
        {
            foreach (var figure in everyone.All)
            {
                if (figure.Body.Setup.Id == Lifestone.Client.Session.StandIn.StandInVisit.HumanSetup && session.State.Objects.TryGet(figure.Guid, out var human) && (human.Weenie.ItemType == 0x10 || human.Name.StartsWith("Corpse", StringComparison.Ordinal)))
                {
                    var swapped = 0;
                    foreach (var part in figure.Body.Appearance.Parts)
                    {
                        swapped += part.SurfaceTextureBySurfaceId.Count > 0 ? 1 : 0;
                    }

                    Log.Info(LogCategory, "a human in the kit frames", ("guid", $"0x{figure.Guid:X8}"), ("name", session.State.Objects.TryGet(figure.Guid, out var named) ? named.Name : "?"), ("parts_with_texture_changes", swapped), ("desc_empty", figure.Created.ObjDesc.IsEmpty), ("model", session.State.Objects.TryGet(figure.Guid, out var withModel) ? $"parts={withModel.Model.AnimPartChanges.Length} tex={withModel.Model.TextureChanges.Length} subs={withModel.Model.SubPalettes.Length}" : "?"), ("created_desc", $"parts={figure.Created.ObjDesc.PartChanges.Count} tex={figure.Created.ObjDesc.TextureChanges.Count} subs={figure.Created.ObjDesc.SubPalettes.Count}"));
                }
            }
        }

        var taken = 0;
        HideLightsHud();
        try
        {
            var model = rig.Chase.Model;
            model.Scale = 1.7f; // farther than the default (a larger scale is farther): the whole figure and its armour fill the frame
            model.SetDefaultOffsets();
            await Frames(40);
            taken += SaveKitFrame(directory, "kit-back") ? 1 : 0;
            for (var i = 0; i < 22; i++)
            {
                model.Rotate(left: true, Lifestone.Game.Player.PlayerController.Clock(), keepGoing: false, modOverride: 1f, cameraKeys: true);
                await Frames(2);
            }

            await Frames(40);
            taken += SaveKitFrame(directory, "kit-front") ? 1 : 0;
        }
        finally
        {
            RestoreLightsHud();
        }

        if (shell is not null && await OpenPanelAsync(shell, 7, 20))
        {
            await Frames(20);
            taken += SaveKitFrame(directory, "kit-paperdoll") ? 1 : 0;
            await ClosePanelAsync(shell, 7);
        }

        Check(name, wielded == kit.Count && taken == 3, $"wielded={wielded}/{kit.Count} frames={taken}/3");
    }

    private bool SaveKitFrame(string directory, string frame)
    {
        _tunnelTallySkip = 2;
        var path = $"{ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y)}/extra/{ShellFramesPlan.FileName(frame)}";
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", frame));
        if (!saved)
        {
            FrameFailed($"{frame} could not be saved");
        }

        return saved;
    }

    /// <summary>
    /// M3 4.2 p6, judged: a wielded or held object never wears a human's or a creature's Setup. The stand-in once gave every item it did not know the human Setup, so the player waved a copy of itself (M3 4.1, and again when D43
    /// moved the caster to weenie 8552 while the stand-in's art was keyed on the old one). The caster is created and wielded, and every avatar held by another (the player's children) is compared with the Setups of every
    /// creature on screen (item type Creature, the player included, and the corpses).
    /// </summary>
    private async Task HeldItemSetupCheckAsync(IGameSessionView session)
    {
        const string name = "no-held-item-wears-a-creature-setup";
        if (_mode.Avatars is not { } avatars)
        {
            Check(name, false, "no avatar set");
            return;
        }

        var spawn = session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
        var made = await WaitFor(() => spawn.IsCompleted && spawn.Result is not null, 20.0);
        var wand = made ? spawn.Result!.Guid : 0u;
        await WaitFor(() => wand != 0 && session.State.Objects.Contains(wand), 10.0);
        session.Actions.Wield(wand, GearKit.Wand.ValidLocations, ActionSource.Host);
        var held = await WaitFor(() => avatars.TryGet(wand, out var child) && child.Body.Parent is not null, 10.0);
        var creatureSetups = new HashSet<uint>();
        foreach (var figure in avatars.All)
        {
            if (session.State.Objects.TryGet(figure.Guid, out var obj) && ((obj.Weenie.ItemType & 0x10) != 0 || obj.Name.StartsWith("Corpse", StringComparison.Ordinal)) && figure.Body.Parent is null)
            {
                creatureSetups.Add(figure.Body.Setup.Id);
            }
        }

        var heldCount = 0;
        var clashes = new List<string>();
        foreach (var figure in avatars.All)
        {
            if (figure.Body.Parent is not null)
            {
                heldCount++;
                if (creatureSetups.Contains(figure.Body.Setup.Id))
                {
                    clashes.Add($"0x{figure.Guid:X8} wears 0x{figure.Body.Setup.Id:X8}");
                }
            }
        }

        Check(name, made && held && heldCount >= 1 && clashes.Count == 0, $"held={heldCount} creature_setups={creatureSetups.Count} clashes=[{string.Join("; ", clashes)}] wand_made={made} wand_held={held}");
        if (wand != 0)
        {
            var back = session.Actions.Unwield(wand, ActionSource.Host);
            await WaitFor(() => back.IsCompleted, 10.0);
        }
    }
}

using System.Diagnostics;
using Godot;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Game.Core;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Slice b2 (the paperdoll of inventory page 7), docs/milestones/m3-plan.md section 4.6 row "b2". The stand-in grants the kit's wand and leather cap and coat through the item tool and wields them (the wand held, the armour at
/// its valid locations); the page is then opened and judged against <see cref="PlayerInventory.Equipped"/> and the world objects, slot by slot with the rule of <c>gmPaperDollUI::SetUIItemIntoLocation</c> (a slot shows the last
/// equipped item whose locations include one of its own): every slot holds what the model says, the coat shows in each slot its locations name. The items are then taken off through the Action API (the client's unwield) and the slots empty. The
/// page is closed again. Slice b4 adds the figure: the page opens on the player's 3D figure (the viewport 0x100001D5 has a source, shows, and its picture holds a drawn body; the Setup and appearance are the
/// player's; the nine armour slots are hidden), and the frames of the first opening are timed (reported, not judged).
/// </summary>
public sealed partial class PlayChecks
{
    private async partial Task PanelB2ChecksAsync(GamePlayScreen shell)
    {
        const string shownName = "the-paperdoll-shows-the-stand-ins-wielded-items-in-their-slots-as-the-inventory-model-has-them";
        const string takenOffName = "the-paperdoll-empties-a-slot-when-its-item-is-taken-off-and-draws-nothing-while-hidden";
        var session = _host.Session;
        var doll = shell.PanelControllers?.ControllersOf(GamePaperDoll.Panel).OfType<GamePaperDoll>().FirstOrDefault();
        if (session is null || doll is null)
        {
            Check(shownName, false, $"session={session is not null} paperdoll_controller={doll is not null}");
            return;
        }

        var kit = new[] { GearKit.Wand, GearKit.Armour[0], GearKit.Armour[1] };
        var spawns = kit.Select(w => session.Tools.SpawnItem(w.Wcid, null, ActionSource.Host)).ToArray();
        var granted = await WaitFor(() => spawns.All(static s => s.IsCompleted && s.Result is not null), 20.0);
        var guids = spawns.Select(static s => s.Result?.Guid ?? 0u).ToArray();
        var objects = session.State.Objects;
        var inventory = session.State.Self.Inventory;
        var created = await WaitFor(() => guids.All(g => g != 0 && objects.Contains(g)), 10.0);
        for (var i = 0; i < kit.Length; i++)
        {
            session.Actions.Wield(guids[i], kit[i].ValidLocations, ActionSource.Host);
        }

        var wielded = await WaitFor(() => guids.All(g => inventory.TryGetEquipped(g, out _)), 20.0);

        // The first opening: the figure is made, handed to the host and drawn for the first time; each of the first frames is timed (a report: a first-open stall would show here).
        var frameMs = new List<double>();
        if (shell.PanelHost is { } panelHost && panelHost.CurrentPanel != GamePaperDoll.Panel)
        {
            panelHost.Toggle(GamePaperDoll.Panel);
            for (var i = 0; i < 8; i++)
            {
                var watch = Stopwatch.StartNew();
                await Frames(1);
                frameMs.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        var opened = await OpenPanelAsync(shell, GamePaperDoll.Panel, 6);
        var slotsFilled = await WaitFor(() => guids.All(g => g != 0 && SlotShowing(doll, g)), 5.0);
        var mismatches = new List<string>();
        var occupied = 0;
        foreach (var slot in GamePaperDoll.SlotTable)
        {
            uint want = 0;
            foreach (var item in inventory.Equipped)
            {
                if ((item.Location & slot.Mask) != 0 && objects.Contains(item.Guid))
                {
                    want = item.Guid;
                }
            }

            var shown = doll.ItemIn(slot.ElementId);
            occupied += shown != 0 ? 1 : 0;
            if (shown != want)
            {
                mismatches.Add($"{slot.Name}: shows 0x{shown:X8}, model has 0x{want:X8}");
            }
        }

        // the coat covers the slots its valid locations name (the Amuli coat since D43: chest and both arms; the old leather coat had four)
        var coatSlots = GamePaperDoll.SlotTable.Where(slot => (slot.Mask & (uint)GearKit.Armour[1].ValidLocations) != 0).Select(slot => slot.ElementId).ToArray();
        var coatEverywhere = guids[2] != 0 && coatSlots.All(id => doll.ItemIn(id) == guids[2]);
        Check(
            shownName,
            granted && created && wielded && opened && slotsFilled && mismatches.Count == 0 && doll.ItemIn(0x100001DF) == guids[0] && doll.ItemIn(0x100005AB) == guids[1] && coatEverywhere,
            $"granted={granted} created={created} wielded={wielded} opened={opened} equipped={inventory.Equipped.Count} slots_occupied={occupied} weapon_slot=0x{doll.ItemIn(0x100001DF):X8} head_slot=0x{doll.ItemIn(0x100005AB):X8} coat_in_four_slots={coatEverywhere} slots_view={doll.SlotsViewShown} mismatches=[{string.Join("; ", mismatches)}] (the stand-in's icons are the placeholder 0x06000001)");

        FigureCheck(shell, doll, frameMs);

        // --- the review frames `doll-worn-figure` and `doll-worn-slots` (M5 7 D122, with --play-frames): the kit worn, on the figure view (the wand in the weapon slot, the
        // armour on the body) and on the slots view (the cap and the coat in the armour slots they cover). Review only, never judged. ---
        if (_framesDirectory is not null)
        {
            HideLightsHud();
            await Frames(4);
            SaveExtraFrame("doll-worn-figure");
            doll.ShowSlotsView(true);
            await Frames(4);
            SaveExtraFrame("doll-worn-slots");
            doll.ShowSlotsView(false);
            await Frames(2);
            RestoreLightsHud();
        }

        // Taken off through the Action API: the slots empty as the model's equipped list does.
        for (var i = 0; i < kit.Length; i++)
        {
            session.Actions.MoveToPack(guids[i], 0, 0, ActionSource.Host);
        }

        var off = await WaitFor(() => guids.All(g => !inventory.TryGetEquipped(g, out _)), 20.0);
        await Frames(3);
        var emptied = guids.All(g => !SlotShowing(doll, g));

        // Closed: a change draws nothing until the page shows again.
        await ClosePanelAsync(shell, GamePaperDoll.Panel);
        var refreshes = doll.Refreshes;
        await Frames(6);
        var open = shell.PanelHost?.IsOpen ?? false;
        Check(takenOffName, off && emptied && !open && doll.Refreshes == refreshes, $"off={off} slots_emptied={emptied} open={open} refreshes={refreshes}->{doll.Refreshes}");

        await LiveWornSetFramesAsync(shell, doll);
        await StackCountFrameAsync(shell);
    }

    /// <summary>
    /// The review frame <c>pack-stack-25000</c> (M5 7 D132, with --play-frames; review only, never judged): two pyreal stacks of 25,000 in the main pack beside the stand-in's
    /// 10,000, so the fitted five-digit count shows in the grid (<c>--retail-stack-count</c> restores retail's clipped one).
    /// </summary>
    private async Task StackCountFrameAsync(GamePlayScreen shell)
    {
        var session = _host.Session;
        if (_framesDirectory is null || session is null)
        {
            return;
        }

        var spawns = new[] { session.Tools.SpawnItem(Lifestone.Client.Session.StandIn.StandInItemKind.PyrealWcid, 25000, ActionSource.Host), session.Tools.SpawnItem(Lifestone.Client.Session.StandIn.StandInItemKind.PyrealWcid, 25000, ActionSource.Host) };
        await WaitFor(() => spawns.All(static s => s.IsCompleted), 20.0);
        await OpenPanelAsync(shell, GamePaperDoll.Panel, 6);
        HideLightsHud();
        await Frames(20);
        SaveExtraFrame("pack-stack-25000");
        RestoreLightsHud();
        await ClosePanelAsync(shell, GamePaperDoll.Panel);
        Log.Info(LogCategory, "pack stack count frame", ("stacks", string.Join(" ", spawns.Select(static s => $"0x{s.Result?.Guid ?? 0:X8}"))));
    }

    /// <summary>
    /// The review frames <c>doll-live-figure</c> and <c>doll-live-slots</c> (M5 7 D122, with --play-frames; review only, never judged): BSD Mage's worn set as the live doll
    /// logged it on 2026-10-29 (the stand-in's own Shirt and Wide Breeches, with the Faran Robe with Hood 5851 at 0x7F01, Leather Gauntlets 56 at 0x20 and the Wand 2472 held;
    /// the stand-in's boots come off first, as the robe covers the feet), on the figure view retail opens on and on the slots view. Everything is put back afterwards.
    /// </summary>
    private async Task LiveWornSetFramesAsync(GamePlayScreen shell, GamePaperDoll doll)
    {
        var session = _host.Session;
        if (_framesDirectory is null || session is null)
        {
            return;
        }

        var inventory = session.State.Self.Inventory;
        var boots = inventory.Equipped.FirstOrDefault(static e => (e.Location & 0x100) != 0);
        if (boots.Guid != 0)
        {
            session.Actions.MoveToPack(boots.Guid, 0, 0, ActionSource.Host);
            await WaitFor(() => !inventory.TryGetEquipped(boots.Guid, out _), 10.0);
        }

        (uint Wcid, EquipMask Location)[] set = [(5851, (EquipMask)0x7F01), (56, EquipMask.HandWear), (2472, EquipMask.Held)];
        var spawns = set.Select(s => session.Tools.SpawnItem(s.Wcid, null, ActionSource.Host)).ToArray();
        await WaitFor(() => spawns.All(static s => s.IsCompleted), 20.0);
        var guids = spawns.Select(static s => s.Result?.Guid ?? 0u).ToArray();
        await WaitFor(() => guids.All(g => g != 0 && session.State.Objects.Contains(g)), 10.0);
        for (var i = 0; i < set.Length; i++)
        {
            session.Actions.Wield(guids[i], set[i].Location, ActionSource.Host);
        }

        var worn = await WaitFor(() => guids.All(g => inventory.TryGetEquipped(g, out _)), 20.0);
        await OpenPanelAsync(shell, GamePaperDoll.Panel, 6);
        doll.ShowSlotsView(false);
        HideLightsHud();
        await Frames(30); // the figure re-dresses from the ObjDescEvent the wields send
        SaveExtraFrame("doll-live-figure");
        doll.ShowSlotsView(true);
        await Frames(4);
        SaveExtraFrame("doll-live-slots");
        doll.ShowSlotsView(false);
        RestoreLightsHud();
        await ClosePanelAsync(shell, GamePaperDoll.Panel);
        Log.Info(LogCategory, "doll live set frames", ("worn", worn), ("guids", string.Join(" ", guids.Select(static g => $"0x{g:X8}"))), ("boots", $"0x{boots.Guid:X8}"));

        foreach (var guid in guids.Where(static g => g != 0))
        {
            session.Actions.MoveToPack(guid, 0, 0, ActionSource.Host);
        }

        await WaitFor(() => guids.All(g => !inventory.TryGetEquipped(g, out _)), 20.0);
        if (boots.Guid != 0)
        {
            session.Actions.Wield(boots.Guid, (EquipMask)boots.Location, ActionSource.Host);
            await WaitFor(() => inventory.TryGetEquipped(boots.Guid, out _), 10.0);
        }
    }

    /// <summary>
    /// The figure (slice b4): page 7 is open on the player's body. The paperdoll serves it (a model of the player's Setup and appearance), the nine armour slots are hidden and the viewport and the click mask shown,
    /// the viewport has a source and a visible hole, and the picture the source draws holds a body (more than a few flat colours, as the CharGen frames judge a drawn character). The first-open frame times are reported.
    /// </summary>
    private void FigureCheck(GamePlayScreen shell, GamePaperDoll doll, List<double> frameMs)
    {
        const string name = "the-paperdoll-opens-on-the-players-3d-figure-served-to-its-viewport-with-the-armour-slots-hidden";
        var session = _host.Session;
        var layer = _ui;
        var figure = doll.Figure;
        var preview = figure?.Preview;
        var self = session?.State.Self.Guid ?? 0;
        var player = session is not null && session.State.Objects.TryGet(self, out var found) ? found : null;
        var expected = player?.FigureFields();
        var sourced = layer?.Viewports.HasSource(GamePaperDoll.ViewportId) ?? false;
        var hole = layer is not null && layer.Viewports.TryGetHole(GamePaperDoll.ViewportId, out _, out var visible) && visible;
        var binder = layer?.GetNodeOrNull<CharGenViewportBinder>($"CharGenViewport_0x{GamePaperDoll.ViewportId:X8}");
        var colours = -1;
        if (layer?.Viewports.PictureOf(GamePaperDoll.ViewportId)?.Texture is { } texture)
        {
            using var picture = texture.GetImage();
            var seen = new HashSet<uint>();
            for (var gy = 0; gy < 32; gy++)
            {
                for (var gx = 0; gx < 32; gx++)
                {
                    seen.Add(picture.GetPixel((((gx * 2) + 1) * picture.GetWidth()) / 64, (((gy * 2) + 1) * picture.GetHeight()) / 64).ToRgba32() >> 8);
                }
            }

            colours = seen.Count;
        }

        var armourHidden = doll.SlotList(0x100005AB) is { Visible: false } && doll.SlotList(0x100005B3) is { Visible: false };
        var appearanceMatches = preview is not null && expected is { } fields
            && preview.Setup.Id == fields.SetupId
            && preview.Composition.ObjDesc.PartChanges.Count == fields.ObjDesc.PartChanges.Count
            && preview.Composition.ObjDesc.TextureChanges.Count == fields.ObjDesc.TextureChanges.Count
            && preview.Composition.ObjDesc.SubPalettes.Count == fields.ObjDesc.SubPalettes.Count;
        var first = frameMs.Count > 0 ? frameMs[0] : -1.0;
        var worst = frameMs.Count > 0 ? frameMs.Max() : -1.0;
        Check(
            name,
            doll.FigureServed && preview is not null && appearanceMatches && sourced && hole && binder is { Visible: true, Redraws: > 0 } && colours >= 8 && !doll.SlotsViewShown && armourHidden && (doll.Viewport?.Visible ?? false) && (doll.ClickMask?.Visible ?? false),
            $"served={doll.FigureServed} setup=0x{preview?.Setup.Id ?? 0:X8} player_setup=0x{expected?.SetupId ?? 0:X8} appearance_matches={appearanceMatches} parts={preview?.Draws.Length ?? 0} heading={preview?.Heading:0.####} camera={figure?.CameraPosition} heritage={figure?.Heritage} builds={figure?.Builds} source={sourced} hole_visible={hole} binder_redraws={binder?.Redraws} picture_colours={colours} slots_view={doll.SlotsViewShown} armour_hidden={armourHidden} first_open_frame_ms={first:0.0} worst_of_first_8_ms={worst:0.0} (report-only; the part under the cursor does not light: TODO(m3-paperdoll-part-lighting))");
    }

    private static bool SlotShowing(GamePaperDoll doll, uint guid)
    {
        foreach (var slot in GamePaperDoll.SlotTable)
        {
            if (doll.ItemIn(slot.ElementId) == guid)
            {
                return true;
            }
        }

        return false;
    }
}

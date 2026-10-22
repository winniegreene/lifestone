using System.Numerics;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Game.Sessions;

// M4 2.1 s4a: the container rows (opening the kill's corpse, picking an item up, dropping it). The only hook is one call in CheckAllAsync after the attack rows. Each row judges the session's state (the Action API's
// outcome, GameState.Containers, PlayerInventory, the object table), never the stand-in's counters (1.3 review R8); the stand-in's own count is text for the detail only.
public sealed partial class PlayChecks
{
    /// <summary>The tusker the container rows kill for its corpse.</summary>
    public const uint CorpseTuskerGuid = 0x80000B06;

    /// <summary>The second tusker (M4 2.2 s4): the melee kill whose corpse the loot row opens (0x80000B07 is the attack frame's).</summary>
    public const uint LootTuskerGuid = 0x80000B08;

    /// <summary>The stand-in chest the loot-rule row opens.</summary>
    public const uint LootChestGuid = 0x80000B09;

    /// <summary>The weenie class of the stand-in's Bowl (<c>StandInWorld.BowlWcid</c>, internal to the client assembly): a chest stocked with it holds an item the default rule takes.</summary>
    private const uint StandInBowlWcid = 141;

    /// <summary>A weenie class the stand-in has no description for (<c>TODO(m4-standin-corpse-contents)</c>): its item comes with no Value, so the default rule leaves it.</summary>
    private const uint StandInUnvaluedWcid = 9_999;

    /// <summary>
    /// The default loot rule with its burden clause off (<c>--loot-max-burden 0</c>, no limit), for the stand-in's rows: the stand-in has no weights (<c>TODO(m4-standin-item-burden)</c>) and the gear-kit rows before these leave its burden over its
    /// capacity, so the default 100% clause would skip every item for burden. The value and type clauses are the default's (value 1 to 1,000,000, the vendor mask); the burden clause is <c>LootRulesTests</c>' row.
    /// </summary>
    private static readonly LootRules StandInLootRules = (LootOptions.Default with { MaxBurdenPercent = 0 }).Rules();

    /// <summary>The inventory page's panel id (the toolbar's backpack and paperdoll page; <c>7-inventory-backpack-and-paperdoll.png</c> in the panel frames).</summary>
    private const uint InventoryPanelId = 7;

    /// <summary>The stand-in corpse's two items (StandInWorld.BowlWcid and SmockWcid, internal to the client assembly: <c>TODO(m4-standin-corpse-contents)</c>).</summary>
    private static readonly string[] CorpseItemNames = ["Bowl", "Smock"];

    /// <summary>
    /// M4 2.1 s4a: on the stand-in, a melee kill leaves a corpse, the Action API opens it (<c>container-corpse-opened</c>: Confirmed, and the session's viewed container is that corpse with the Bowl and the Smock as its
    /// entries), picks the Bowl up (<c>container-pickup</c>: Confirmed, the item is in the player's pack in the inventory model and no longer in the corpse's view) and drops it (<c>item-drop</c>: Confirmed, the item is no
    /// longer held and sits on the ground in the object table). Offline: they prove the client's side and the stand-in's answers in the live order of cache/m4/2-1/capture.md (C2), not ACE's acceptance (s4b's live tests do that).
    /// </summary>
    private async Task ContainerChecksAsync(IGameSessionView session)
    {
        await ContainerOpenPickupDropAsync(session);
    }

    private async Task ContainerOpenPickupDropAsync(IGameSessionView session)
    {
        const string openedName = "container-corpse-opened";
        const string pickupName = "container-pickup";
        const string dropName = "item-drop";
        if (_host.StandIn is not { } server)
        {
            Check(openedName, false, "the stand-in session has no server levers");
            return;
        }

        await StowHeldAsync(session); // M4 3.1 g: a caster in hand forbids the melee mode (the combat-mode guard; ACE too)
        var mace = await SpawnAttackItemAsync(session, AttackMaceWcid, null);
        var wield = mace == 0 ? null : session.Actions.Wield(mace, EquipMask.MeleeWeapon, ActionSource.Host);
        var wielded = wield is not null && await WaitFor(() => wield.IsCompleted, 10.0);
        var melee = session.Actions.SetCombatMode(CombatMode.Melee, ActionSource.Host);
        var inMelee = await WaitFor(() => melee.IsCompleted, 10.0) && session.State.Self.Qualities.CombatMode == CombatModes.Melee;
        server.PlayerHitRate = 1.0;
        var corpsesBefore = session.State.Objects.All.Where(o => (o.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0).Select(o => o.Guid).ToHashSet(); // the attack rows' kills left corpses too
        PlaceAttackTusker(session, server, CorpseTuskerGuid, 2f, 60);
        var present = await WaitFor(() => session.State.Objects.Contains(CorpseTuskerGuid), 10.0);
        var attack = session.Actions.Attack(CorpseTuskerGuid, AttackHeight.Medium, 0.5f, ActionSource.Host);
        var attackConfirmed = await WaitFor(() => attack.IsCompleted, 25.0) && attack.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var corpse = 0u;
        var appeared = await WaitFor(() => (corpse = FindNewCorpse(session, corpsesBefore)) != 0, AttackSequenceSeconds);
        var ended = await WaitFor(() => session.Attack.State == AttackSequenceState.Done && session.Attack.Number > 0, AttackSequenceSeconds);
        if (!(wielded && inMelee && present && attackConfirmed && appeared))
        {
            Check(openedName, false, $"setup: wielded={wielded} melee_mode={inMelee} present={present} attack_outcome={attack.Outcome?.Kind.ToString() ?? "pending"} corpse_appeared={appeared}");
            return;
        }

        // --- the open, in the combat stance (ACE accepted it: capture C2) ---
        var open = session.Actions.OpenContainer(corpse, ActionSource.Host);
        var openConfirmed = await WaitFor(() => open.IsCompleted, 15.0) && open.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var view = session.State.Containers.Viewed;
        var viewed = view is not null && view.Guid == corpse;
        var resolved = viewed && await WaitFor(() => session.State.Containers.Viewed is { } v && v.IsResolved && v.Entries.Count >= CorpseItemNames.Length, 10.0);
        var names = new List<string>();
        if (session.State.Containers.Viewed is { } shown && shown.Guid == corpse)
        {
            foreach (var entry in shown.Entries)
            {
                names.Add(session.State.Objects.TryGet(entry.Guid, out var item) ? item.Name : "?");
            }
        }

        var contentsMatch = names.Count == CorpseItemNames.Length && CorpseItemNames.All(n => names.Contains(n));
        Check(
            openedName,
            openConfirmed && viewed && resolved && contentsMatch && ended,
            $"corpse=0x{corpse:X8} open_outcome={open.Outcome?.Kind.ToString() ?? "pending"} ({open.Outcome?.Detail}) viewed={viewed} resolved={resolved} entries=[{string.Join(", ", names)}] sequence_done={ended}");

        // --- the review frame `corpse` (M4 2.2 s4, with --play-frames; PLACEHOLDER content: the stand-in corpse's Bowl and Smock): the offline stand-in of the hunt leg's `corpse` frame, taken as the hunt takes it, in the combat stance with the
        // corpse open, once the container window is drawn (GameContainerPanel.DrawState). Review only, never judged. The combat floaty is hidden while the environment floaty shows the container window (they share the place 0,400, one at a
        // time: UiFloatyCombatPanel, M4 2.2 fix2 R12), so this frame shows the container window alone over the world view in melee, with no combat window through it (the log line's combat_window_shown reads False). ---
        if (_framesDirectory is not null && openConfirmed && viewed && _ui?.Screens?.Flow.CurrentScreen is GamePlayScreen corpseShell)
        {
            var corpseWindow = corpseShell.Root?.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel;
            var corpseDrawn = corpseWindow is not null && await WaitFor(() => corpseWindow.DrawState == ContainerWindowDraw.Drawn, 10.0);
            if (corpseDrawn)
            {
                HideLightsHud(); // the developer HUD would cover the window; the shell's own windows stay
                await Frames(4);
                SaveExtraFrame(PanelFramesPlan.CorpseFrame);
                RestoreLightsHud();
            }

            // The panel shots' finder (--play-panel-shots opens the nearest container within 10 m) over this real stand-in world: a corpse the attack rows left, not a creature, a pack or the player.
            var nearest = PanelFramesPlan.ContainerWithin(session.State, PanelFramesPlan.ContainerFrameMetres);
            var nearestObject = session.State.Objects.TryGet(nearest, out var near) ? near : null;
            Check(
                "panel-shots-find-a-container-within-10-m",
                nearestObject is not null && (nearestObject.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0 && nearestObject.Name.StartsWith("Corpse of ", StringComparison.Ordinal),
                $"nearest=0x{nearest:X8} name='{nearestObject?.Name}' the_open_corpse=0x{corpse:X8}");

            var corpseObject = session.State.Objects.TryGet(corpse, out var found) ? found : null;
            var combatWindow = corpseShell.Root?.GetChildRecursive(GameCombatPanel.WindowId);
            Log.Info(
                LogCategory,
                "corpse frame: " + (corpseDrawn ? "the frame was saved" : "the window did not draw; no frame"),
                ("corpse", $"0x{corpse:X8}"),
                ("title", corpseWindow?.Title ?? "n/a"),
                ("cells", corpseWindow?.ItemRow?.CellCount ?? -1),
                ("window_draw", corpseWindow?.DrawState.ToString() ?? "no window"),
                ("combat_window_shown", combatWindow?.IsShown == true),
                ("objdesc", corpseObject is null ? "n/a" : HuntUiShotPlan.ObjDescFacts(corpseObject.Created.Model, !ReferenceEquals(corpseObject.Model, corpseObject.Created.Model))),
                ("placeholder", "stand-in corpse contents, TODO(m4-standin-corpse-contents)"));
        }

        // --- the review frame `container-window` (M4 2.2 s2, with --play-frames; PLACEHOLDER content: the stand-in corpse's Bowl and Smock, TODO(m4-standin-corpse-contents)). Review only, never judged. It is taken in the stance the rows
        // are in (melee): the combat floaty is hidden while the container window shows, so the shot no longer switches to peace to dodge the overlap (M4 2.2 fix2, R12). ---
        if (_framesDirectory is not null && openConfirmed && viewed && _ui?.Screens?.Flow.CurrentScreen is GamePlayScreen windowShell)
        {
            var window = windowShell.Root?.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel;
            var drawn = window is not null && await WaitFor(() => window.Visible && window.ShownEntries.Count() >= CorpseItemNames.Length && window.ItemRow!.Items.OfType<Lifestone.Client.RetailUi.Widgets.UiItem>().All(static c => !c.IconPending), 10.0);
            if (drawn)
            {
                HideLightsHud(); // the developer HUD would cover the window; the shell's own windows stay
                await Frames(4);
                SaveExtraFrame(PanelFramesPlan.ContainerWindowFrame);
                RestoreLightsHud();
            }

            Log.Info(LogCategory, "container window frame: " + (drawn ? "the frame was saved" : "the window did not draw; no frame"), ("corpse", $"0x{corpse:X8}"), ("title", window?.Title ?? "n/a"), ("entries", window?.ShownEntries.Count() ?? 0), ("combat_window_shown", windowShell.Root?.GetChildRecursive(GameCombatPanel.WindowId)?.IsShown == true), ("placeholder", "stand-in corpse contents, TODO(m4-standin-corpse-contents)"));
        }

        // --- the pick-up of the Bowl into the pack (the 0022 into the player) ---
        var bowl = 0u;
        if (session.State.Containers.Viewed is { } open2)
        {
            foreach (var entry in open2.Entries)
            {
                if (session.State.Objects.TryGet(entry.Guid, out var item) && item.Name == "Bowl")
                {
                    bowl = entry.Guid;
                }
            }
        }

        var pick = bowl == 0 ? null : session.Actions.PickUp(bowl, 0, 0, ActionSource.Host);
        var pickConfirmed = pick is not null && await WaitFor(() => pick.IsCompleted, 20.0) && pick.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var place = session.State.Self.Inventory.Find(bowl);
        var inPack = bowl != 0 && place.Held && place.Kind == InventoryPlaceKind.MainPack && place.Container == session.State.Self.Inventory.Owner;
        var leftCorpse = session.State.Containers.Viewed is { } after && after.Guid == corpse && !after.Entries.Any(e => e.Guid == bowl);
        Check(
            pickupName,
            bowl != 0 && pickConfirmed && inPack && leftCorpse,
            $"bowl=0x{bowl:X8} pickup_outcome={pick?.Outcome?.Kind.ToString() ?? "pending"} ({pick?.Outcome?.Detail}) in_pack={inPack} place={place.Kind} left_corpse_view={leftCorpse}");

        // --- the review frame `pickup` (2.1 F1b, with --play-frames): the inventory page (panel 7) open with the picked-up Bowl in the pack. Review only, never judged: whether the page draws the item is for the eye. ---
        if (_framesDirectory is not null && inPack && _ui?.Screens?.Flow.CurrentScreen is GamePlayScreen shell)
        {
            var opened = await OpenPanelAsync(shell, InventoryPanelId, 12);
            if (opened)
            {
                HideLightsHud(); // the developer HUD would cover the page; the shell's own panels stay
                await Frames(4);
                SaveExtraFrame("pickup");
                RestoreLightsHud();
            }

            Log.Info(LogCategory, "pickup frame: the inventory page " + (opened ? "opened and the frame was saved" : "did not open; no frame"), ("bowl", $"0x{bowl:X8}"), ("page", (object)InventoryPanelId));
            await ClosePanelAsync(shell, InventoryPanelId);
        }

        // --- the drop (the 019A, the item on the ground) ---
        var drop = bowl == 0 || !inPack ? null : session.Actions.Drop(bowl, ActionSource.Host);
        var dropConfirmed = drop is not null && await WaitFor(() => drop.IsCompleted, 20.0) && drop.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var stillHeld = session.State.Self.Inventory.Find(bowl).Held;
        var onGround = session.State.Objects.TryGet(bowl, out var dropped) && dropped.Position is not null;
        Check(
            dropName,
            drop is not null && dropConfirmed && !stillHeld && onGround,
            $"bowl=0x{bowl:X8} drop_outcome={drop?.Outcome?.Kind.ToString() ?? "pending"} ({drop?.Outcome?.Detail}) still_held={stillHeld} on_ground={onGround}");

        // Close the corpse and go back to peace, so the checks after these see the player as the attack rows left it.
        if (session.State.Containers.ViewedGuid != 0)
        {
            var close = session.Actions.CloseContainer(session.State.Containers.ViewedGuid, ActionSource.Host);
            await WaitFor(() => close.IsCompleted, 10.0);
        }

        // --- M4 2.2 s4: the loot rows (a second melee kill; the player is still in melee with the mace wielded and nothing is open) ---
        await ContainerLootChecksAsync(session, server);

        var rest = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => rest.IsCompleted, 10.0);
        var stow = session.Actions.MoveToPack(mace, 0, 0, ActionSource.Host);
        await WaitFor(() => stow.IsCompleted, 10.0);
        Log.Info(LogCategory, "container rows done", ("corpse", $"0x{corpse:X8}"), ("bowl", $"0x{bowl:X8}"));
    }

    /// <summary>
    /// M4 2.2 s4: the loot rows, judged on the session's state (the object table, the inventory model and the container model), never on the stand-in's counters. <c>container-loot</c>: a second melee kill's corpse is opened and
    /// the default rule without its burden clause (<see cref="StandInLootRules"/>) over its entries takes what the rule wants through <see cref="CorpseLooter"/> (the stand-in's Bowl and Smock, whose Value and ItemType are <b>placeholders</b>,
    /// <c>TODO(m4-standin-corpse-contents)</c>): both are in the player's main pack in the inventory model, neither is in the corpse's view and the looter counted <c>looted=2</c>.
    /// <c>container-loot-rule</c>: a stand-in chest (<c>TODO(m4-standin-chest)</c>; the stand-in's corpse is fixed, so the chest carries the mix) holds the Bowl, a Pyreal stack (no Value, a type outside the mask) and an item the stand-in
    /// gives no Value; the same rule takes the Bowl and leaves the other two where they are. (The stand-in gives a Value to no other item, so a type outside the mask with a Value is <c>LootRulesTests</c>' row, not this one.)
    /// Both rows close what they opened and leave the player in melee, the mace wielded, for the rows that follow.
    /// </summary>
    private async Task ContainerLootChecksAsync(IGameSessionView session, StandInServer server)
    {
        const string lootName = "container-loot";
        const string ruleName = "container-loot-rule";
        var corpsesBefore = session.State.Objects.All.Where(o => (o.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0).Select(o => o.Guid).ToHashSet();
        PlaceAttackTusker(session, server, LootTuskerGuid, 2f, 60);
        var present = await WaitFor(() => session.State.Objects.Contains(LootTuskerGuid), 10.0);
        var attack = session.Actions.Attack(LootTuskerGuid, AttackHeight.Medium, 0.5f, ActionSource.Host);
        var corpse = 0u;
        var appeared = present && await WaitFor(() => (corpse = FindNewCorpse(session, corpsesBefore)) != 0, AttackSequenceSeconds);
        var ended = await WaitFor(() => !session.Attack.Active, AttackSequenceSeconds);
        if (!(present && appeared))
        {
            Check(lootName, false, $"setup: tusker_present={present} attack_outcome={attack.Outcome?.Kind.ToString() ?? "pending"} corpse_appeared={appeared} sequence_ended={ended}");
            return;
        }

        var open = session.Actions.OpenContainer(corpse, ActionSource.Host);
        var opened = await WaitFor(() => open.IsCompleted, 15.0) && open.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var resolved = opened && await WaitFor(() => session.State.Containers.Viewed is { } v && v.Guid == corpse && v.IsResolved && v.Entries.Count >= CorpseItemNames.Length, 10.0);
        var before = ViewedItems(session, corpse);
        var load = LootContext.Of(session.State); // the load the rule reads: a stand-in player over its capacity would skip everything for burden, which the detail then shows
        var looter = new CorpseLooter(GodotLogSink.Instance, LogCategory, corpse, StandInLootRules);
        var done = resolved && await WaitFor(() => looter.Step(session, session.Clock.Now) == LootStatus.Done, 60.0);
        var inventory = session.State.Self.Inventory;
        var inPack = before.Count > 0 && before.Keys.All(guid => inventory.Find(guid) is { Held: true, Kind: InventoryPlaceKind.MainPack } place && place.Container == inventory.Owner);
        var inView = session.State.Containers.Viewed is { } after && after.Guid == corpse ? after.Entries.Count(e => before.ContainsKey(e.Guid)) : -1;
        Check(
            lootName,
            opened && resolved && done && before.Count == CorpseItemNames.Length && CorpseItemNames.All(before.Values.Contains) && inPack && inView == 0 && looter.Looted == CorpseItemNames.Length && looter.Skipped == 0 && looter.StoppedBy == "end",
            $"corpse=0x{corpse:X8} open_outcome={open.Outcome?.Kind.ToString() ?? "pending"} resolved={resolved} entries=[{string.Join(", ", before.Values)}] done={done} in_main_pack={inPack} still_in_corpse_view={inView} burden={load.Burden} capacity={load.Capacity} {looter.Summary} (rule: the default's value and type clauses, no burden clause, TODO(m4-standin-item-burden); placeholder Value and ItemType: TODO(m4-standin-corpse-contents))");
        await CloseViewedAsync(session);

        // --- the rule: a chest of a Bowl (taken), a Pyreal stack (Value 0 and type Money: left) and an item with no Value (left) ---
        var placed = server.PlaceChest(LootChestGuid, new Vector3(0f, 3f, 0f), ("Bowl", StandInBowlWcid, 1), ("Pyreal", StandInItemKind.PyrealWcid, 7), ("Stand-in unvalued item", StandInUnvaluedWcid, 1));
        var chestPresent = placed is not null && await WaitFor(() => session.State.Objects.Contains(LootChestGuid), 10.0);
        var chestOpen = chestPresent ? session.Actions.OpenContainer(LootChestGuid, ActionSource.Host) : null;
        var chestOpened = chestOpen is not null && await WaitFor(() => chestOpen.IsCompleted, 20.0) && chestOpen.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var chestResolved = chestOpened && await WaitFor(() => session.State.Containers.Viewed is { } v && v.Guid == LootChestGuid && v.IsResolved && v.Entries.Count == 3, 10.0);
        var chestBefore = ViewedItems(session, LootChestGuid);

        // --- the review frame `container-chest` (M4 3.2 lane e, with --play-frames; PLACEHOLDER content: the stand-in's chest, TODO(m4-standin-chest), holding a Bowl, a Pyreal stack and an unvalued item; D23: a chest, never a creature
        // corpse): the container window over the world view with the chest's three entries, then the item row's empty slot boxes. Review only, never judged here (the unit rows count the drawn empty cells). ---
        if (_framesDirectory is not null && chestOpened && chestResolved && _ui?.Screens?.Flow.CurrentScreen is GamePlayScreen chestShell)
        {
            var chestWindow = chestShell.Root?.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel;
            var chestDrawn = chestWindow is not null && await WaitFor(() => chestWindow.DrawState == ContainerWindowDraw.Drawn, 10.0);
            if (chestDrawn)
            {
                HideLightsHud(); // the developer HUD would cover the window; the shell's own windows stay
                await Frames(4);
                SaveExtraFrame(PanelFramesPlan.ContainerChestFrame);
                RestoreLightsHud();
            }

            Log.Info(LogCategory, "container chest frame: " + (chestDrawn ? "the frame was saved" : "the window did not draw; no frame"), ("chest", $"0x{LootChestGuid:X8}"), ("title", chestWindow?.Title ?? "n/a"), ("entries", chestWindow?.ShownEntries.Count() ?? 0), ("cells", chestWindow?.ItemRow?.CellCount ?? -1), ("empty_cells", chestWindow?.ItemRow?.EmptyCount ?? -1), ("placeholder", "stand-in chest, TODO(m4-standin-chest)"));
        }
        var chestLoad = LootContext.Of(session.State);
        var chestLooter = new CorpseLooter(GodotLogSink.Instance, LogCategory, LootChestGuid, StandInLootRules);
        var chestDone = chestResolved && await WaitFor(() => chestLooter.Step(session, session.Clock.Now) == LootStatus.Done, 60.0);
        var taken = chestBefore.Where(kv => kv.Value == "Bowl").Select(kv => kv.Key).ToList();
        var left = chestBefore.Where(kv => kv.Value != "Bowl").Select(kv => kv.Key).ToList();
        var bowlInPack = taken.Count == 1 && inventory.Find(taken[0]) is { Held: true } held && held.Container == inventory.Owner;
        var stillListed = session.State.Containers.Viewed is { } chestView && chestView.Guid == LootChestGuid ? left.Count(guid => chestView.Entries.Any(e => e.Guid == guid)) : -1;
        var leftNotHeld = left.Count == 2 && left.All(guid => !inventory.Find(guid).Held);
        Check(
            ruleName,
            chestOpened && chestResolved && chestDone && bowlInPack && stillListed == 2 && leftNotHeld && chestLooter.Looted == 1 && chestLooter.Skipped == 2 && chestLooter.SkippedBy(LootDecision.Value) == 2,
            $"chest=0x{LootChestGuid:X8} open_outcome={chestOpen?.Outcome?.Kind.ToString() ?? "pending"} entries=[{string.Join(", ", chestBefore.Values)}] done={chestDone} bowl_in_pack={bowlInPack} left_still_listed={stillListed} left_not_held={leftNotHeld} burden={chestLoad.Burden} capacity={chestLoad.Capacity} {chestLooter.Summary} (the chest and the unvalued item are placeholders: TODO(m4-standin-chest) TODO(m4-standin-corpse-contents))");
        await CloseViewedAsync(session);
    }

    /// <summary>The entries of the viewed container <paramref name="container"/> that have their objects, by guid with their names (empty when another container or none is viewed).</summary>
    private static Dictionary<uint, string> ViewedItems(IGameSessionView session, uint container)
    {
        var items = new Dictionary<uint, string>();
        if (session.State.Containers.Viewed is { } view && view.Guid == container)
        {
            foreach (var entry in view.Entries)
            {
                if (session.State.Objects.TryGet(entry.Guid, out var item))
                {
                    items[entry.Guid] = item.Name;
                }
            }
        }

        return items;
    }

    /// <summary>Closes the container the model views (the Action API), if any, and waits for the close to settle and the view to go.</summary>
    private async Task CloseViewedAsync(IGameSessionView session)
    {
        if (session.State.Containers.ViewedGuid != 0)
        {
            var close = session.Actions.CloseContainer(session.State.Containers.ViewedGuid, ActionSource.Host);
            await WaitFor(() => close.IsCompleted, 10.0);
            await WaitFor(() => session.State.Containers.ViewedGuid == 0, 5.0);
        }
    }

    private static uint FindNewCorpse(IGameSessionView session, HashSet<uint> before)
    {
        foreach (var o in session.State.Objects.All)
        {
            if (!before.Contains(o.Guid) && (o.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0 && o.Name.StartsWith("Corpse of ", StringComparison.Ordinal) && o.Position is not null)
            {
                return o.Guid;
            }
        }

        return 0;
    }
}

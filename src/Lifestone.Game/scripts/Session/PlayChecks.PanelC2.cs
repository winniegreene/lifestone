using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Slice pb (the spellcasting panel), docs/milestones/m3-plan.md section 4.8 row "pb". The window is a floaty window of the gameplay layout, not a host page: the checks reach it through the Action API's combat mode on the
/// stand-in. Peace hides it; the stand-in is asked for favourite spells through <c>AddSpellFavorite</c> (it keeps them for its next description, so the session's bars are whatever the description held); the Magic mode shows
/// the window with its eight tabs and the bar the session holds listed in order (judged against the session's own bars, never against a guess); a self spell cast from the window goes through <c>IActionApi.Cast</c> at the
/// player, a targeted one with nothing selected says so in the chat and sends nothing, and with the stand-in's drudge selected goes at it; peace hides the window again. With <c>--play-frames</c> the review frame
/// <c>c2-spellcasting</c> (Magic mode, then the shot) is saved under <c>panels/</c>. Ends in peace. Nothing here clicks a spell on a live server (D29's rule): the stand-in only.
/// </summary>
public sealed partial class PlayChecks
{
    private async partial Task PanelC2ChecksAsync(GamePlayScreen shell)
    {
        const string hiddenName = "the-spellcasting-window-is-hidden-outside-magic-mode";
        const string favouriteName = "add-spell-favorite-goes-through-the-action-api-and-the-stand-in-answers";
        const string magicName = "magic-mode-shows-the-spellcasting-window-with-its-eight-tabs-and-the-session-bars-listed-in-order";
        const string selfName = "a-self-spell-cast-from-the-window-goes-through-the-action-api-at-the-player";
        const string targetedName = "a-targeted-spell-says-so-with-nothing-selected-and-casts-at-the-selected-object-otherwise";
        const string peaceName = "peace-hides-the-spellcasting-window-and-nothing-is-drawn";
        var session = _host.Session;
        var root = shell.Root;
        var window = root?.GetChildRecursive(GameSpellcastingPanel.WindowId) as GameSpellcastingPanel;
        if (session is null || root is null || window is null)
        {
            foreach (var name in new[] { hiddenName, favouriteName, magicName, selfName, targetedName, peaceName })
            {
                Check(name, false, $"session={session is not null} spellcasting_window={window is not null}");
            }

            return;
        }

        var floaty = window.Parent as UiFloatyCombatPanel;
        var q = session.State.Self.Qualities;
        if (q.CombatMode is not (0 or CombatModes.NonCombat))
        {
            var reset = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            await WaitFor(() => reset.IsCompleted, 10.0);
        }

        await Frames(3);
        Check(
            hiddenName,
            !window.Visible && !window.IsShown && floaty?.Visible == false,
            $"mode={q.CombatMode} window_visible={window.Visible} window_shown={window.IsShown} floaty_visible={floaty?.Visible}");

        // The favourites: the stand-in is asked for up to three known spells on bar 0 and one on bar 1, as the spellbook's drag would (D13: no drag in M3, so the Action API is the only way).
        var book = session.State.Self.Spellbook;
        var known = book.Spells.Keys.Where(id => session.Spell(id) is not null).Order().Take(4).ToArray();
        var handles = new List<ActionHandle>();
        for (var i = 0; i < known.Length; i++)
        {
            handles.Add(session.Actions.AddSpellFavorite(known[i], i < 3 ? i : 0, i < 3 ? 0 : 1, ActionSource.Host));
        }

        var answered = await WaitFor(() => handles.TrueForAll(static h => h.IsCompleted), 10.0);
        Check(
            favouriteName,
            known.Length >= 2 && answered && handles.TrueForAll(static h => h.Outcome?.Kind == ActionOutcomeKind.Sent),
            $"known_spells={book.Count} asked={known.Length} answered={answered} outcomes=[{string.Join(",", handles.Select(static h => h.Outcome?.Kind.ToString() ?? "none"))}]");

        var caster = await HoldCasterAsync(session); // M4 3.1 g: the magic mode needs a caster in hand (the combat-mode guard); the wand goes back to the pack after the peace row
        var magic = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var inMagic = await WaitFor(() => magic.IsCompleted && q.CombatMode == CombatModes.Magic, 10.0);
        await Frames(4);
        var heldBars = session.State.Self.Spellbook.SpellBars;
        var held0 = heldBars.IsDefaultOrEmpty ? [] : heldBars[0].Where(id => session.State.Self.Spellbook.Knows(id)).ToArray();
        var listed0 = window.ShownSpells(0);
        var tabsPresent = true;
        for (var tab = 0; tab < GameSpellcastingPanel.TabCount; tab++)
        {
            tabsPresent &= window.TabElement(tab) is not null && window.ListOf(tab) is not null;
        }

        if (held0.Length == 0)
        {
            // The stand-in keeps the favourites for its next description (StandInWorld.AddSpellFavorite): the session's bars stay as they were until one arrives, so nothing can be listed yet. Never a pass by a guess: the listing is
            // judged against the session's own bars, which are empty here, and this line says so.
            Log.Warn(LogCategory, "the session holds no favourite bar to list (the stand-in sends bars only with its next description): TODO(m3-standin-bars-live)");
        }

        Check(
            magicName,
            inMagic && window.Visible && window.IsShown && floaty?.Visible == true && tabsPresent && window.OpenTab == 0 && listed0.SequenceEqual(held0) && !window.AwaitingSpellTable,
            $"mode={q.CombatMode} window_visible={window.Visible} window_shown={window.IsShown} floaty_visible={floaty?.Visible} tabs={tabsPresent} open_tab={window.OpenTab} held=[{string.Join(",", held0)}] listed=[{string.Join(",", listed0)}] builds={window.Builds}");

        // A self spell from the window: the stand-in's probe book holds one (SelfTargeted in the SpellTable), cast at the player through the Action API.
        var self = book.Spells.Keys.Where(id => session.Spell(id) is { } info && (info.Flags & Lifestone.Data.Magic.SpellFlags.SelfTargeted) != 0).Order().FirstOrDefault();
        var castsBefore = window.Casts;
        var selfHandle = self == 0 ? null : window.Cast(self);
        var selfDone = selfHandle is not null && await WaitFor(() => selfHandle.IsCompleted, 12.0);
        var player = session.State.Self.Guid;
        Check(
            selfName,
            selfHandle is not null && selfDone && selfHandle.Kind == ActionKind.CastSpell && selfHandle.Source == ActionSource.Ui && selfHandle.Summary.Contains($"target=0x{player:X8}", StringComparison.Ordinal) && window.Casts == castsBefore + 1
            && selfHandle.Outcome?.Kind is ActionOutcomeKind.Confirmed or ActionOutcomeKind.Sent,
            $"spell={self} summary='{selfHandle?.Summary}' outcome={selfHandle?.Outcome?.Kind} casts={window.Casts}");

        // A targeted spell (Flame Bolt I, id 27, in the probe book): with nothing selected the chat says so and nothing is sent; with the stand-in's drudge selected it goes at it.
        const uint bolt = 27;
        const uint drudge = 0x80000A04;
        var previous = _mode.Selection.Selected;
        var chat = new List<string>();
        var oldNotice = window.ChatNotice;
        window.ChatNotice = chat.Add;
        _mode.Selection.Clear();
        var noneSent = window.Casts;
        var none = window.Cast(bolt);
        var noneOk = none is null && window.Casts == noneSent && chat.Count == 1 && chat[0] == GameSpellcastingPanel.NoTargetText;
        _mode.Selection.Select(drudge);
        var at = window.Cast(bolt);
        var atDone = at is not null && await WaitFor(() => at.IsCompleted, 12.0);
        window.ChatNotice = oldNotice;
        _mode.Selection.Select(previous);
        Check(
            targetedName,
            noneOk && at is not null && atDone && at.Kind == ActionKind.CastSpell && at.Source == ActionSource.Ui && at.Summary.Contains($"target=0x{drudge:X8}", StringComparison.Ordinal) && chat.Count == 1,
            $"none_sent={none is not null} chat=[{string.Join("|", chat)}] summary='{at?.Summary}' outcome={at?.Outcome?.Kind}");

        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        var inPeace = await WaitFor(() => peace.IsCompleted && q.CombatMode == CombatModes.NonCombat, 10.0);
        await Frames(4);
        Check(
            peaceName,
            inPeace && !window.Visible && !window.IsShown && floaty?.Visible == false && !window.Children.Any(static c => c.IsShown),
            $"mode={q.CombatMode} window_visible={window.Visible} window_shown={window.IsShown} floaty_visible={floaty?.Visible}");
        await StowAsync(session, caster);

        if (_framesDirectory is { } directory)
        {
            var folder = PanelFramesPlan.FolderFor(ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y));
            var frame = PanelFramesPlan.SpellcastingFrame;
            var combat = root.GetChildRecursive(GameCombatPanel.WindowId) as GameCombatPanel;
            if (combat is null)
            {
                Log.Warn(LogCategory, "the gameplay layout has no combat window 0x1000005C: the frame cannot stage the toolbar's magic button (TODO(m3-combat-window-missing))");
            }

            var restoreCombat = combat?.Mode ?? 0;
            var restore = window.Mode;
            combat?.SetCombatMode(frame.CombatMode); // the combat window's notice drives the toolbar's button for the mode (and hides itself in Magic), as the panel shots stage the frame (M3 3.2 review finding 10)
            window.SetCombatMode(frame.CombatMode); // the UI's own mode notice, as pa's frames do (the session's character keeps its mode)
            var earlierTab = window.OpenTab;
            var choice = PanelFramesPlan.OpenSpellcastingFrameTab(window); // M3 3.2 fix B (3.2-R7): the first tab whose bar holds spells, a tab click
            Log.Info(LogCategory, $"spellcasting frame tab={choice.Tab} holds_spells={choice.HoldsSpells} opened={choice.Opened}");
            HideLightsHud(); // the developer HUD would cover the window (M4 3.2 lane e: both frames of the window are taken without it)
            await Frames(8);
            var shot = window.IsShown && SaveCheckedFrame($"{folder}/{PanelFramesPlan.FileName(frame)}", frame.Name);
            if (!shot)
            {
                FrameFailed($"{frame.Name}: the window did not show (shown={window.IsShown})");
            }

            // M4 3.2 lane e: the same window on the first tab whose favourites bar holds no spell, so the frame shows the bar's empty slot boxes (c2-spellcasting-empty; review only).
            var emptyFrame = PanelFramesPlan.SpellcastingEmptyFrame;
            var emptyChoice = PanelFramesPlan.OpenSpellcastingEmptyFrameTab(window);
            Log.Info(LogCategory, $"spellcasting empty frame tab={emptyChoice.Tab} holds_spells={emptyChoice.HoldsSpells} opened={emptyChoice.Opened} cells={window.ListOf(emptyChoice.Tab)?.CellCount ?? -1} empty_cells={window.ListOf(emptyChoice.Tab)?.EmptyCount ?? -1}");
            await Frames(8);
            var emptyShot = window.IsShown && SaveCheckedFrame($"{folder}/{PanelFramesPlan.FileName(emptyFrame)}", emptyFrame.Name);
            if (!emptyShot)
            {
                FrameFailed($"{emptyFrame.Name}: the window did not show (shown={window.IsShown})");
            }

            RestoreLightsHud();
            window.OpenTabByClick(earlierTab);
            window.SetCombatMode(restore);
            combat?.SetCombatMode(restoreCombat);
            await Frames(2);
            Log.Info(LogCategory, $"spellcasting frame done captured={(shot ? 1 : 0)}/1 empty_captured={(emptyShot ? 1 : 0)}/1");
        }
    }
}

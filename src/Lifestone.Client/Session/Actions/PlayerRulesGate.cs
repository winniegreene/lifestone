using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.PlayerRules;

namespace Lifestone.Client.Session.Actions;

/// <summary>
/// The player's rules on the automation (M6 item 15, the owner's order; <c>--autopilot-player-rules</c>, on by default): every spell an <see cref="ActionSource.Automation"/> action casts is on one of the character's spell bars,
/// and every potion or tincture it drinks is on the shortcut bar, as a person playing retail has them. The Action API asks this gate at its cast and drink entry points, so every automated path (the fight, the buff cycle,
/// the vitals keeper, the death handler, the recalls, the journey's legs) passes it without a change of its own. The rules themselves are <see cref="SpellBarRule"/> and <see cref="DrinkBarRule"/>.
/// </summary>
/// <remarks>
/// <para><b>A cast.</b> When the cast is asked (<see cref="BeforeCast"/>), a spell on no bar is put on one as a player puts it: RemoveSpellFavorite (0x01E4) for the lower level it replaces, then AddSpellFavorite (0x01E3) at
/// the position, both as the automation's own actions, queued ahead of the cast and logged by name. At the cast's turn (<see cref="CastRefusal"/>, the cast tracker's before-check) a spell that is still on no bar is refused by
/// name. When the cast goes out (<see cref="CastSent"/>) <see cref="SpellBarCast"/> tells the spellcasting panel, which shows the spell's tab and selects the spell as a person's double click does.</para>
/// <para><b>A drink.</b> When the drink is asked (<see cref="BeforeDrink"/>) the item drunk is the one the kind's shortcut holds (another stack of the same kind is swapped for it); a kind with no shortcut gets one first,
/// AddShortcut (0x019C) on the slot <see cref="DrinkBarRule.SlotFor"/> names. At the drink's turn (<see cref="DrinkRefusal"/>) an item that is on no slot is refused by name; when the drink goes out (<see cref="DrinkSent"/>)
/// <see cref="ShortcutUsed"/> tells the toolbar, which shows the slot busy as a person's key press does. While the automation is at work
/// (<see cref="AutomationWindowSeconds"/> since its last action) every listed kind that is held and has no shortcut is given one as soon as the inventory shows it (<see cref="Update"/>).</para>
/// <para><b>What is not judged</b>, each named once in the log: a cast before the PlayerDescription brought the bars (<c>TODO(m6-15-bars-unread)</c>: only a sparse test rig, a live session casts after it); the wielded caster's
/// own spell, which a person casts by using the caster and which no spellbook holds.</para>
/// </remarks>
internal sealed class PlayerRulesGate
{
    /// <summary>The log category.</summary>
    internal const string LogCategory = "player-rules";

    /// <summary>How long after the automation's last accepted action the drinks are still kept on the bar for it, in session-clock seconds.</summary>
    internal const double AutomationWindowSeconds = 60;

    private readonly ActionApi _api;
    private readonly HashSet<uint> _spellAdds = [];
    private readonly HashSet<uint> _drinkAdds = [];
    private readonly HashSet<int> _slotsAsked = [];
    private readonly Dictionary<uint, int> _drinkSlots = [];
    private readonly HashSet<ulong> _named = [];
    private double _automationAt = double.NaN;
    private bool _drinksDirty = true;
    private bool _unreadNoted;

    /// <summary>The gate of an Action API.</summary>
    internal PlayerRulesGate(ActionApi api) => _api = api;

    /// <summary>True (the default) while the rules are enforced; false for <c>--autopilot-player-rules=off</c>.</summary>
    internal bool On { get; set; } = true;

    /// <summary>How many favourites the gate asked to add (tests, the inspector).</summary>
    internal int SpellsAdded { get; private set; }

    /// <summary>How many shortcuts the gate asked to add.</summary>
    internal int ShortcutsAdded { get; private set; }

    /// <summary>How many casts and drinks the gate refused.</summary>
    internal int Refusals { get; private set; }

    private bool Covers(ActionSource source) => On && source == ActionSource.Automation;

    /// <summary>An automation action was accepted: the automation is at work (the drinks are kept on the bar for it).</summary>
    internal void AutomationSeen()
    {
        var now = _api.Clock.Now;
        if (double.IsNaN(_automationAt) || now - _automationAt > AutomationWindowSeconds)
        {
            _drinksDirty = true;
        }

        _automationAt = now;
    }

    /// <summary>The inventory or the shortcut bar changed: the drinks are looked at again at the next update.</summary>
    internal void Touch() => _drinksDirty = true;

    /// <summary>The cast of <paramref name="spellId"/> is being asked (or, with <paramref name="when"/>, the spell was just learned): a spell that is on no bar is put on one first (see the remarks of the class).</summary>
    internal void BeforeCast(uint spellId, ActionSource source, string when = "before its cast")
    {
        if (!Covers(source) || spellId == 0 || _api.KillSwitch)
        {
            return; // under the kill switch the cast is refused where it is asked: nothing to put on a bar for
        }

        var book = _api.State.Self.Spellbook;
        var place = SpellBarRule.Place(book, spellId, _api.FindSpell);
        if (place.Verdict == SpellBarVerdict.BookUnread)
        {
            NoteUnread();
            return;
        }

        if (place.Verdict != SpellBarVerdict.Add || !_spellAdds.Add(spellId))
        {
            return; // on a bar, not addable (the cast's turn names it), or its add is already asked
        }

        var spell = SpellBarRule.Describe(spellId, _api.FindSpell(spellId));
        var how = "appended";
        if (place.Replaces != 0 && _api.CastWaiting(place.Replaces))
        {
            // A cast of the lower level is asked and not sent yet: it stays on the bar (its cast is from there), and the new level goes in ahead of it.
            how = "ahead of " + Named(place.Replaces) + ", the line's lower level, which stays while its own cast waits";
        }
        else if (place.Replaces != 0)
        {
            _api.RemoveSpellFavorite(place.Replaces, place.Bar, ActionSource.Automation);
            how = "in the place of " + Named(place.Replaces) + ", the line's lower level, which comes off";
        }
        else if (place.After != 0)
        {
            how = "after " + Named(place.After) + ", the line's higher level";
        }

        _api.AddSpellFavorite(spellId, place.Index, place.Bar, ActionSource.Automation);
        SpellsAdded++;
        _api.Log.Info(
            LogCategory,
            string.Create(CultureInfo.InvariantCulture, $"spell bar: {spell.Name} (spell {spellId}) goes on {SpellBarRule.TabLabel(place.Bar)} at position {place.Index + 1}, {how}, {when} (AddSpellFavorite 0x01E3)"));
        if (!spell.Described && _named.Add(Key(3, spellId)))
        {
            _api.Log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"TODO(m6-15-spell-undescribed): spell {spellId} is in neither the SpellTable nor the mage's profile; it goes on the utility tab by number, with no line"));
        }
    }

    /// <summary>The cast tracker's before-check: why the rule refuses the cast of <paramref name="spellId"/> at its turn (the spell is on no bar), or null.</summary>
    internal string? CastRefusal(uint spellId, ActionSource source)
    {
        if (!Covers(source) || spellId == 0)
        {
            return null;
        }

        var book = _api.State.Self.Spellbook;
        if (!book.IsKnown)
        {
            NoteUnread();
            return null;
        }

        if (SpellBarRule.TryFind(book, spellId, out _, out _))
        {
            return null;
        }

        if (IsWieldedCastersSpell(spellId))
        {
            if (_named.Add(Key(1, spellId)))
            {
                _api.Log.Info(LogCategory, "spell bar: " + Named(spellId) + " is the wielded caster's own spell: a person casts it by using the caster, and no spellbook holds it, so it is on no bar");
            }

            return null;
        }

        Refusals++;
        var why = book.Knows(spellId)
            ? " is on no spell bar after its add was tried"
            : " is not in the spellbook, so it cannot be put on a spell bar (retail and ACE take a known spell only)";
        var refusal = "player rules: " + Named(spellId) + why + ": a person could not cast it, so the automation does not (--autopilot-player-rules=off lifts the rule)";
        if (_named.Add(Key(2, spellId)))
        {
            _api.Log.Error(LogCategory, refusal);
        }

        return refusal;
    }

    /// <summary>The cast of <paramref name="spellId"/> is going out: the spellcasting panel is told which tab and position it is cast from.</summary>
    internal void CastSent(uint spellId, ActionSource source)
    {
        if (Covers(source) && SpellBarRule.TryFind(_api.State.Self.Spellbook, spellId, out var bar, out var index))
        {
            _api.PublishSpellBarCast(spellId, bar, index);
        }
    }

    /// <summary>The drink of <paramref name="item"/> is being asked: returns the item to drink, the one the kind's shortcut holds (see the remarks of the class), after asking for the shortcut when the kind has none.</summary>
    internal uint BeforeDrink(uint item, ActionSource source)
    {
        if (!Covers(source) || item == 0 || _api.KillSwitch)
        {
            return item;
        }

        var state = _api.State;
        var bar = state.Self.Shortcuts;
        if (!bar.IsKnown)
        {
            NoteUnread();
            return item;
        }

        var wcid = DrinkBarRule.KindOf(state, item);
        if (wcid == 0)
        {
            return item; // not held, or not in the object table: the drink's own before-check names it
        }

        var on = bar.SlotOf(item);
        if (on >= 0)
        {
            _drinkSlots[wcid] = on;
            return item;
        }

        var slot = DrinkBarRule.SlotOfKind(state, wcid);
        if (slot >= 0)
        {
            _drinkSlots[wcid] = slot;
            if (_api.Log.IsEnabled(LogSeverity.Debug))
            {
                _api.Log.Debug(LogCategory, string.Create(CultureInfo.InvariantCulture, $"shortcut bar: the drink is the {DrinkBarRule.NameOf(state, item)} on slot {slot + 1} (the shortcut's stack, not the stack asked for)"));
            }

            return bar[slot].ObjectId;
        }

        AskShortcut(item, wcid, "before its drink");
        return item;
    }

    /// <summary>The drink of <paramref name="item"/> is going out: the toolbar is told which slot it is the use of.</summary>
    internal void DrinkSent(uint item, ActionHandle action)
    {
        if (Covers(action.Source) && _api.State.Self.Shortcuts.SlotOf(item) is >= 0 and var slot)
        {
            _api.PublishShortcutUsed(slot, item, action);
        }
    }

    /// <summary>The drink tracker's before-check: why the rule refuses the drink of <paramref name="item"/> at its turn (it is on no shortcut slot), or null.</summary>
    internal string? DrinkRefusal(uint item, ActionSource source)
    {
        if (!Covers(source) || item == 0)
        {
            return null;
        }

        var state = _api.State;
        if (!state.Self.Shortcuts.IsKnown || state.Self.Shortcuts.SlotOf(item) >= 0)
        {
            return null;
        }

        Refusals++;
        var full = state.Self.Shortcuts.FirstEmpty() < 0 ? " (the bar has no free slot)" : string.Empty;
        var refusal = "player rules: the " + DrinkBarRule.NameOf(state, item) + " is on no shortcut slot after its add was tried" + full +
            ": a person drinks from the shortcut bar, so the automation does not drink it (--autopilot-player-rules=off lifts the rule)";
        if (_named.Add(Key(4, DrinkBarRule.KindOf(state, item))))
        {
            _api.Log.Error(LogCategory, refusal);
        }

        return refusal;
    }

    /// <summary>
    /// Once an update, after its dispatch: the adds asked before it have gone out (or were refused), so they are forgotten; then, while the automation is at work and something changed, every listed kind of drink that is
    /// held and has no shortcut is given one.
    /// </summary>
    internal void Update()
    {
        _spellAdds.Clear();
        _drinkAdds.Clear();
        _slotsAsked.Clear();
        if (!On || !_drinksDirty || double.IsNaN(_automationAt) || _api.Clock.Now - _automationAt > AutomationWindowSeconds || _api.KillSwitch || _api.DryRun)
        {
            return;
        }

        var state = _api.State;
        if (!state.Self.Shortcuts.IsKnown || !state.Self.Inventory.IsKnown || WorldTrackers.NotInWorld(_api) is not null)
        {
            return; // looked at again when the world is there (the flag stays up)
        }

        _drinksDirty = false;
        var kinds = DrinkBarRule.Kinds;
        for (var i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];
            var slot = DrinkBarRule.SlotOfKind(state, kind.Wcid);
            if (slot >= 0)
            {
                _drinkSlots[kind.Wcid] = slot;
            }
            else if (DrinkBarRule.FirstHeld(state, kind.Wcid) is var held && held != 0)
            {
                AskShortcut(held, kind.Wcid, "while it is held");
            }
        }
    }

    /// <summary>Forgets what was asked and seen (the session was reset); the slots the kinds were on are kept, so a kind goes back to its slot.</summary>
    internal void Reset()
    {
        _spellAdds.Clear();
        _drinkAdds.Clear();
        _slotsAsked.Clear();
        _automationAt = double.NaN;
        _drinksDirty = true;
    }

    private void AskShortcut(uint item, uint wcid, string when)
    {
        if (!_drinkAdds.Add(wcid))
        {
            return; // the kind's add is already asked
        }

        var state = _api.State;
        var slot = DrinkBarRule.SlotFor(state.Self.Shortcuts, wcid, _drinkSlots.GetValueOrDefault(wcid, -1), _slotsAsked);
        if (slot < 0)
        {
            return; // a full bar: the drink's turn names it
        }

        _slotsAsked.Add(slot);
        _drinkSlots[wcid] = slot;
        _api.AddShortcut(slot, item, ActionSource.Automation);
        ShortcutsAdded++;
        _api.Log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"shortcut bar: the {DrinkBarRule.NameOf(state, item)} (weenie class {wcid}) goes on slot {slot + 1} {when} (AddShortcut 0x019C)"));
    }

    private bool IsWieldedCastersSpell(uint spellId)
    {
        foreach (var held in _api.State.Self.Inventory.Equipped)
        {
            if (_api.State.Objects.TryGet(held.Guid, out var item) && item.Weenie.SpellId == spellId)
            {
                return true;
            }
        }

        return false;
    }

    private void NoteUnread()
    {
        if (!_unreadNoted)
        {
            _unreadNoted = true;
            _api.Log.Warn(LogCategory, "TODO(m6-15-bars-unread): no PlayerDescription brought the spell bars and the shortcut bar yet, so the automation's casts and drinks are not judged until it does (a live session casts after it; this is a sparse test rig)");
        }
    }

    private string Named(uint spellId) => SpellBarRule.Describe(spellId, _api.FindSpell(spellId)).Name + " (spell " + spellId.ToString(CultureInfo.InvariantCulture) + ")";

    private static ulong Key(uint what, uint id) => ((ulong)what << 32) | id;
}

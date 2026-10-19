using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The enchantment and spellbook messages (M3 1.2 lane s1): GameEvent 0x02C1 to 0x02C8 and 0x0312 change <see cref="PlayerQualities.Enchantments"/> and <see cref="SelfState.Spellbook"/> the way retail's
/// <c>ClientMagicSystem::Handle_Magic__*</c> change the player's registry and spell book (11.4186 0x005677F0 to 0x00568B20, 11.6096 0x00568590 to 0x005698C0). The registry rules are in
/// <see cref="EnchantmentRules"/>; each applied message publishes <see cref="EnchantmentChanged"/> or <see cref="SpellbookChanged"/>.
/// </summary>
/// <remarks>
/// <para>No timer removes an enchantment: the server's 02C3 does (ACE sends it at expiry with the expire sound). An entry kept more than <see cref="EnchantmentRules.OverdueGraceSeconds"/> past its expiry
/// is counted once and logged at WARN (<c>TODO(m3-enchant-overdue)</c>, report-only), when the next enchantment message or description is applied and by
/// <see cref="PlayerQualities.CountOverdueEnchantments"/> whenever a reader asks.</para>
/// <para>Retail composes "&lt;spell&gt; has expired." itself for a 02C3 or 02C5 (<c>NotifyOfEnchantmentRemoval</c>, 11.6096 0x00569460; ACE sends no chat for the player's own expiry): the reducer adds the line with
/// the spell's name from <see cref="SpellLookup"/> (<see cref="ChatKind.Notice"/>, chat type 7 Magic, " penalty" after the name of the vitae, spell ids from 0x8000 up print nothing). Without the table the line is
/// not added and the reducer logs once (<c>TODO(m3-enchant-expiry-name)</c>).
/// <c>TODO(m3-enchant-qualities-event)</c>: a panel that refreshes on <see cref="QualitiesChanged"/> alone (the vitals window) does not see a registry change until it also subscribes to
/// <see cref="EnchantmentChanged"/> (or the lead adds a <see cref="QualityChange"/> for it).</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    /// <summary>Enchantments the registry refused (retail's first test in <c>UpdateEnchantment</c>, <c>TODO(m3-enchant-type-parity)</c>): none of the multiplicative, additive and cooldown bits, or two of them.</summary>
    internal long EnchantmentsRejected;

    /// <summary>Removals and dispels that named an enchantment the registry did not hold.</summary>
    internal long EnchantmentRemovalsMissed;

    /// <summary>Enchantments noticed past their expiry with no removal (<c>TODO(m3-enchant-overdue)</c>; each is counted once).</summary>
    internal long EnchantmentsOverdue;

    private readonly HashSet<(ushort Spell, ushort Layer, double Received)> _overdueReported = [];

    private bool _expiryNameLogged;

    /// <summary>The retail SpellTable's lookup (the session sets it; null in a host without dats): the spell's name for the expiry line.</summary>
    internal Func<uint, Lifestone.Data.Magic.SpellInfo?>? SpellLookup { get; set; }

    /// <summary>Retail's chat type for the expiry line: <c>AddTextToScroll(..., 7)</c> in <c>NotifyOfEnchantmentRemoval</c> (Magic).</summary>
    internal const uint ExpiryChatType = 7;

    /// <summary>The spell id of the vitae (retail's <c>NotifyOfEnchantmentRemoval</c> adds " penalty" to its name).</summary>
    internal const ushort VitaeSpellId = 666;

    /// <summary><c>NotifyOfEnchantmentRemoval</c> (11.6096 0x00569460): "&lt;spell&gt; has expired." for a spell id below 0x8000 that the SpellTable knows.</summary>
    private void NoteExpiry(ushort spellId)
    {
        if (spellId >= 0x8000)
        {
            return;
        }

        var spell = SpellLookup?.Invoke(spellId);
        if (spell is null)
        {
            if (!_expiryNameLogged)
            {
                _expiryNameLogged = true;
                _log.Warn(SessionLog.Category, "TODO(m3-enchant-expiry-name): an enchantment expired and the SpellTable has no name for it, so retail's expiry line is not added", new LogField("spell", spellId));
            }

            return;
        }

        AddChat(new ChatLine(_clock.Now, ChatKind.Notice, ExpiryChatType, null, 0, spell.Name + (spellId == VitaeSpellId ? " penalty" : string.Empty) + " has expired."));
    }

    /// <summary>
    /// GameEvent 0x02C1: the spell joins the book with the value 0, as <c>ClientMagicSystem::Handle_Magic__UpdateSpell</c> (0x00568590) asks <c>CACQualities::AddSpell</c>. ACE's "You learn the X spell." is its own
    /// chat message, so it is not composed here.
    /// </summary>
    public void OnMagicUpdateSpell(in InboundContext context, in GameEventHeader header, in MagicUpdateSpellEvent message)
    {
        var book = _state.Self.Spellbook;
        var isNew = book.Learn(message.SpellId);
        Changed();
        Publish(new SpellbookChanged(SpellbookChange.Learned, message.SpellId, book.Count, isNew));
    }

    /// <summary>The client sent AddSpellFavorite: the read model's bars change at once (<see cref="Spellbook.AddFavorite"/>), as retail's player module does before the action goes out. Publishes when a bar changed.</summary>
    internal void ApplyFavoriteAdd(uint spellId, int index, int bar)
    {
        var book = _state.Self.Spellbook;
        if (book.AddFavorite(spellId, index, bar))
        {
            Changed();
            Publish(new SpellbookChanged(SpellbookChange.Favorites, spellId, book.Count, true));
        }
    }

    /// <summary>The client sent RemoveSpellFavorite: the read model's bars change at once (<see cref="Spellbook.RemoveFavorite"/>). Publishes when a bar changed.</summary>
    internal void ApplyFavoriteRemove(uint spellId, int bar)
    {
        var book = _state.Self.Spellbook;
        if (book.RemoveFavorite(spellId, bar))
        {
            Changed();
            Publish(new SpellbookChanged(SpellbookChange.Favorites, spellId, book.Count, true));
        }
    }

    /// <summary>GameEvent 0x02C2: <c>Handle_Magic__UpdateEnchantment</c> (0x005686D0): the enchantment is added or replaces its twin.</summary>
    public void OnMagicUpdateEnchantment(in InboundContext context, in GameEventHeader header, in MagicUpdateEnchantmentEvent message) =>
        UpdateEnchantments(context, [message.Enchantment]);

    /// <summary>GameEvent 0x02C3: <c>Handle_Magic__RemoveEnchantment</c> (0x005696F0) with the expiry notice: the enchantment is removed.</summary>
    public void OnMagicRemoveEnchantment(in InboundContext context, in GameEventHeader header, in MagicRemoveEnchantmentEvent message) =>
        RemoveEnchantments([new LayeredSpellDesc(message.SpellId, message.Layer)], EnchantmentChange.Removed, notify: true);

    /// <summary>GameEvent 0x02C4: <c>Handle_Magic__UpdateMultipleEnchantments</c> (0x00568780): each enchantment as 0x02C2 does.</summary>
    public void OnMagicUpdateMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicUpdateMultipleEnchantmentsEvent message) =>
        UpdateEnchantments(context, message.Enchantments.IsDefault ? default : message.Enchantments.AsSpan());

    /// <summary>GameEvent 0x02C5: <c>Handle_Magic__RemoveMultipleEnchantments</c> (0x005697D0) with the expiry notice.</summary>
    public void OnMagicRemoveMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicRemoveMultipleEnchantmentsEvent message) =>
        RemoveEnchantments(message.Spells.IsDefault ? default : message.Spells.AsSpan(), EnchantmentChange.Removed, notify: true);

    /// <summary>GameEvent 0x02C6: <c>Handle_Magic__PurgeEnchantments</c> (0x00568810): every enchantment of the multiplicative and additive lists except the lasting ones goes; the vitae and the cooldowns stay.</summary>
    public void OnMagicPurgeEnchantments(in InboundContext context, in GameEventHeader header, in MagicPurgeEnchantmentsEvent message) =>
        PurgeEnchantments(badOnly: false);

    /// <summary>GameEvent 0x02C7: <c>Handle_Magic__DispelEnchantment</c> (0x005698B0): as the removal, without the expiry notice.</summary>
    public void OnMagicDispelEnchantment(in InboundContext context, in GameEventHeader header, in MagicDispelEnchantmentEvent message) =>
        RemoveEnchantments([new LayeredSpellDesc(message.SpellId, message.Layer)], EnchantmentChange.Dispelled, notify: false);

    /// <summary>GameEvent 0x02C8: <c>Handle_Magic__DispelMultipleEnchantments</c> (0x005698C0): as 0x02C5, without the expiry notice.</summary>
    public void OnMagicDispelMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicDispelMultipleEnchantmentsEvent message) =>
        RemoveEnchantments(message.Spells.IsDefault ? default : message.Spells.AsSpan(), EnchantmentChange.Dispelled, notify: false);

    /// <summary>GameEvent 0x0312: <c>Handle_Magic__PurgeBadEnchantments</c> (0x005688A0): as the purge, and the beneficial enchantments stay.</summary>
    public void OnMagicPurgeBadEnchantments(in InboundContext context, in GameEventHeader header, in MagicPurgeBadEnchantmentsEvent message) =>
        PurgeEnchantments(badOnly: true);

    /// <summary>
    /// Fills the spellbook from the PlayerDescription (one call from <c>OnPlayerDescription</c>, after the qualities are applied; the registry was stamped with the description's arrival time when it was set, so
    /// each enchantment's remaining time counts from now, <see cref="ActiveEnchantment"/>) and reports the overdue ones.
    /// </summary>
    private void ApplyDescriptionSpellbook(PlayerDescriptionMsg message)
    {
        var self = _state.Self;
        self.Spellbook.ApplyDescription(message);
        _overdueReported.Clear();
        NoteOverdueEnchantments(_clock.Now);
        Publish(new SpellbookChanged(SpellbookChange.Description, 0, self.Spellbook.Count, true));
    }

    /// <summary>The registry's <c>UpdateEnchantment</c> for each of <paramref name="entries"/> in order, then the events.</summary>
    private void UpdateEnchantments(in InboundContext context, ReadOnlySpan<EnchantmentDesc> entries)
    {
        var q = _state.Self.Qualities;
        var received = _clock.ToSeconds(context.ReceivedTimestamp);
        var registry = new List<ActiveEnchantment>(q.ActiveEnchantments);
        var added = ImmutableArray.CreateBuilder<LayeredSpellDesc>();
        var replaced = ImmutableArray.CreateBuilder<LayeredSpellDesc>();
        var vitaeAdded = false;
        var vitaeReplaced = false;
        foreach (var entry in entries)
        {
            var layered = new LayeredSpellDesc(entry.SpellId, entry.Layer);
            switch (EnchantmentRules.Update(registry, new ActiveEnchantment(entry, received)))
            {
                case EnchantmentRules.UpdateOutcome.Added:
                    added.Add(layered);
                    vitaeAdded |= IsVitae(registry, entry);
                    break;
                case EnchantmentRules.UpdateOutcome.Replaced:
                    replaced.Add(layered);
                    vitaeReplaced |= IsVitae(registry, entry);
                    break;
                default:
                    EnchantmentsRejected++;
                    _log.Warn(
                        SessionLog.Category,
                        "an enchantment the registry refuses is ignored, as retail's UpdateEnchantment ignores it (TODO(m3-enchant-type-parity))",
                        new LogField("spell", entry.SpellId),
                        new LogField("layer", entry.Layer),
                        new LogField("type", "0x" + entry.StatModType.ToString("X", System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
        }

        if (added.Count == 0 && replaced.Count == 0)
        {
            return;
        }

        q.SetActiveEnchantments([.. registry]);
        Changed();
        Publish(new QualitiesChanged(QualityChange.Enchantments, 0));
        if (added.Count > 0)
        {
            Publish(new EnchantmentChanged(EnchantmentChange.Added, added.ToImmutable(), registry.Count, false, vitaeAdded));
        }

        if (replaced.Count > 0)
        {
            Publish(new EnchantmentChanged(EnchantmentChange.Replaced, replaced.ToImmutable(), registry.Count, false, vitaeReplaced));
        }

        NoteOverdueEnchantments(_clock.Now);
    }

    /// <summary>The removal of each layered id of <paramref name="spells"/> (<c>CEnchantmentRegistry::RemoveEnchantments</c>), then the event.</summary>
    private void RemoveEnchantments(ReadOnlySpan<LayeredSpellDesc> spells, EnchantmentChange change, bool notify)
    {
        var q = _state.Self.Qualities;
        var registry = new List<ActiveEnchantment>(q.ActiveEnchantments);
        var removed = ImmutableArray.CreateBuilder<LayeredSpellDesc>();
        var vitae = false;
        foreach (var spell in spells)
        {
            if (EnchantmentRules.Remove(registry, spell.SpellId, spell.Layer) is { } gone)
            {
                removed.Add(spell);
                vitae |= gone.Enchantment.Bucket == EnchantmentBucket.Vitae;
            }
            else
            {
                EnchantmentRemovalsMissed++;
                _log.Warn(
                    SessionLog.Category,
                    "a removal named an enchantment the registry does not hold; nothing changes",
                    new LogField("spell", spell.SpellId),
                    new LogField("layer", spell.Layer),
                    new LogField("change", change));
            }
        }

        if (removed.Count == 0)
        {
            return;
        }

        q.SetActiveEnchantments([.. registry]);
        Changed();
        Publish(new QualitiesChanged(QualityChange.Enchantments, 0));
        var removedSpells = removed.ToImmutable();
        Publish(new EnchantmentChanged(change, removedSpells, registry.Count, notify, vitae));
        if (notify)
        {
            foreach (var spell in removedSpells)
            {
                NoteExpiry(spell.SpellId);
            }
        }

        NoteOverdueEnchantments(_clock.Now);
    }

    /// <summary><c>CEnchantmentRegistry::PurgeEnchantments</c> or <c>PurgeBadEnchantments</c>, then the event (<c>TODO(m3-purge-empty)</c> closed: a 0x02C6 that removes nothing still publishes an empty <see cref="EnchantmentChange.Purged"/>; the bad-only purge does not, as ACE never sends it).</summary>
    private void PurgeEnchantments(bool badOnly)
    {
        var q = _state.Self.Qualities;
        var registry = new List<ActiveEnchantment>(q.ActiveEnchantments);
        var removed = EnchantmentRules.Purge(registry, badOnly);
        if (removed.Count == 0)
        {
            // ACE sends 0x02C6 at every player death (Player_Death.cs 211-216), buffed or not: the message was received, so it is published (the death tally's purged=). The state did not change.
            if (!badOnly)
            {
                Publish(new EnchantmentChanged(EnchantmentChange.Purged, ImmutableArray<LayeredSpellDesc>.Empty, registry.Count, false, false));
            }

            return;
        }

        q.SetActiveEnchantments([.. registry]);
        Changed();
        Publish(new QualitiesChanged(QualityChange.Enchantments, 0));
        Publish(new EnchantmentChanged(
            badOnly ? EnchantmentChange.PurgedBad : EnchantmentChange.Purged,
            [.. removed.Select(e => new LayeredSpellDesc(e.SpellId, e.Layer))],
            registry.Count,
            false,
            false));
        NoteOverdueEnchantments(_clock.Now);
    }

    /// <summary>True when the entry just applied sits in the registry as the vitae.</summary>
    private static bool IsVitae(List<ActiveEnchantment> registry, EnchantmentDesc entry) =>
        registry.Exists(e => e.Enchantment.Bucket == EnchantmentBucket.Vitae && e.SpellId == entry.SpellId && e.Layer == entry.Layer);

    /// <summary>Counts and logs, once each, the enchantments kept past their expiry (<c>TODO(m3-enchant-overdue)</c>; report-only).</summary>
    private void NoteOverdueEnchantments(double now)
    {
        foreach (var entry in _state.Self.Qualities.ActiveEnchantments)
        {
            if (entry.IsOverdue(now) && _overdueReported.Add((entry.SpellId, entry.Layer, entry.ReceivedSeconds)))
            {
                EnchantmentsOverdue++;
                _log.Warn(
                    SessionLog.Category,
                    "an enchantment is past its expiry and the server sent no removal; it is kept (TODO(m3-enchant-overdue))",
                    new LogField("spell", entry.SpellId),
                    new LogField("layer", entry.Layer),
                    new LogField("expired_seconds_ago", now - entry.ExpirySeconds));
            }
        }
    }
}

using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The enchantment messages applied to the player's registry (M3 1.2 lane s1; <c>ClientMagicSystem::Handle_Magic__*</c> and <c>CEnchantmentRegistry</c>): 02C2 and 02C4 add or replace, 02C3 and 02C5 remove, 02C7 and 02C8
/// dispel, 02C6 and 0312 purge, over a description that holds the live shape's 138 layered enchantments, so the replace and remove paths run at the real count. The messages are ACE's own bytes through the dispatcher.
/// </summary>
public sealed class ReducerEnchantmentTests
{
    private const uint Player = ReducerHarness.PlayerGuid;

    /// <summary>Type bits: additive (0x8000), single stat (0x1000), secondary attribute (0x2), beneficial (0x2000000).</summary>
    private const uint AddSecondaryBeneficial = 0x2000000 | 0x8000 | 0x1000 | 0x2;

    private const uint AddSecondaryHarmful = 0x8000 | 0x1000 | 0x2;
    private const uint MultSecondaryBeneficial = 0x2000000 | 0x4000 | 0x1000 | 0x2;
    private const uint VitaeType = 0x800000 | 0x4000 | 0x2000 | 0x2;
    private const uint CooldownType = 0x1000000;

    private uint _sequence = 100;

    private static AceEnchantment Ench(ushort spell, ushort layer, ushort category, uint power, uint type, uint key, float value, double start = 0, double duration = 1800) =>
        new(spell, layer, category, 0, power, start, duration, Player, 0f, 0f, 0.0, type, key, value, 0);

    private static AceEnchantment Health(ushort spell, ushort layer, ushort category, uint power, float value, double start = 0, double duration = 1800, uint type = AddSecondaryBeneficial) =>
        Ench(spell, layer, category, power, type, 1, value, start, duration);

    private static EnchantmentDesc Desc(ushort spell, ushort layer, ushort category, uint power, uint type, uint key, float value, double start, double duration, EnchantmentBucket bucket) =>
        new(spell, layer, category, 0, power, start, duration, Player, 0f, 0f, 0.0, type, key, value, 0, bucket);

    private static PlayerDescriptionMsg Description(ImmutableArray<EnchantmentDesc> enchantments) => new()
    {
        Attributes = [.. Enumerable.Range(1, 6).Select(i => new AttributeDesc((uint)i, 0, 55, 0))],
        Vitals = [new VitalDesc(1, 0, 0, 0, 28), new VitalDesc(3, 0, 0, 0, 55), new VitalDesc(5, 0, 0, 0, 55)],
        Enchantments = enchantments,
    };

    /// <summary>A world with the live shape's 138 enchantments applied (health maximum 38, stamina 95, mana 55: docs of <see cref="EnchantmentRulesTests"/>).</summary>
    private static ReducerHarness Buffed(ImmutableArray<EnchantmentDesc>? registry = null)
    {
        var h = Rig();
        h.EnterWorld();
        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription), Description(registry ?? StandInPlayerStats.LiveShapedEnchantments()));
        return h;
    }

    /// <summary>A harness that also records the magic events (the shared harness tracks a fixed list of events).</summary>
    private static ReducerHarness Rig()
    {
        var h = new ReducerHarness();
        return h;
    }

    private DispatchResult Send(ReducerHarness h, Func<uint, uint, byte[]> body) => InboundDispatcher.Dispatch(h.Context, body(Player, ++_sequence), h.Reducer);

    private static uint HealthMaximum(ReducerHarness h)
    {
        Assert.True(h.State.Self.Qualities.TryGetVitalLevels(1, out _, out var max));
        return max;
    }

    private static int IndexOf(ImmutableArray<EnchantmentDesc> list, ushort spell, ushort layer) => list.IndexOf(list.Single(e => e.SpellId == spell && e.Layer == layer));

    // ------------------------------------------------------------------ 02C2

    [Fact]
    public void A_cast_adds_the_enchantment_at_the_head_of_its_list_and_the_maximum_follows()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        Assert.Equal(138, q.Enchantments.Length);
        Assert.Equal(38u, HealthMaximum(h));
        h.ClearEvents();

        var result = Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f)));

        Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
        Assert.Equal(139, q.Enchantments.Length);
        Assert.Equal(100, IndexOf(q.Enchantments, 5000, 1)); // after the 100 multiplicative entries: the head of the additive list
        Assert.Equal(EnchantmentBucket.Additive, q.Enchantments[100].Bucket);
        Assert.Equal(60u, HealthMaximum(h));
        var changed = Assert.Single(h.Of<EnchantmentChanged>());
        Assert.Equal(new EnchantmentChanged(EnchantmentChange.Added, changed.Spells, 139, false, false), changed);
        Assert.Equal([new LayeredSpellDesc(5000, 1)], changed.Spells.ToArray());
    }

    [Fact]
    public void The_max_health_buff_round_trips_through_the_removal()
    {
        var h = Buffed();
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f)));
        Assert.Equal(60u, HealthMaximum(h));
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, 5000, 1));

        Assert.Equal(138, h.State.Self.Qualities.Enchantments.Length);
        Assert.Equal(38u, HealthMaximum(h));
        var changed = Assert.Single(h.Of<EnchantmentChanged>());
        Assert.Equal((EnchantmentChange.Removed, 138, true, false), (changed.Change, changed.Count, changed.Notify, changed.VitaeChanged));
        Assert.Equal([new LayeredSpellDesc(5000, 1)], changed.Spells.ToArray());
    }

    [Fact]
    public void The_same_spell_and_layer_replaces_in_place()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f)));
        var at = IndexOf(q.Enchantments, 5000, 1);
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 30f, start: -5)));

        Assert.Equal(139, q.Enchantments.Length);
        Assert.Equal(at, IndexOf(q.Enchantments, 5000, 1));
        Assert.Equal(30f, q.Enchantments[at].StatModValue);
        Assert.Equal(68u, HealthMaximum(h));
        Assert.Equal(EnchantmentChange.Replaced, Assert.Single(h.Of<EnchantmentChanged>()).Change);
    }

    [Fact]
    public void A_timed_spell_at_another_layer_replaces_the_timed_entry_of_that_spell()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 10f)));
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 2, 300, 400, 30f)));

        Assert.Equal(139, q.Enchantments.Length);
        Assert.DoesNotContain(q.Enchantments, e => e.SpellId == 5000 && e.Layer == 1);
        Assert.Contains(q.Enchantments, e => e.SpellId == 5000 && e.Layer == 2);
        Assert.Equal(EnchantmentChange.Replaced, Assert.Single(h.Of<EnchantmentChanged>()).Change);
    }

    [Fact]
    public void A_lasting_spell_at_a_new_layer_joins_the_layers_of_its_spell()
    {
        // The registry's four layers of spell 4459 last (duration -1): a fifth layer is added beside them, not swapped for one of them.
        var h = Buffed();
        var q = h.State.Self.Qualities;

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Ench(4459, 5, 100, 99, 0x2000000 | 0x4000 | 0x1000 | 8, 60, 0.32f, duration: -1)));

        Assert.Equal(139, q.Enchantments.Length);
        Assert.Equal(5, q.Enchantments.Count(e => e.SpellId == 4459));
        Assert.Equal(0, IndexOf(q.Enchantments, 4459, 5)); // the head of the multiplicative list
        Assert.Equal(EnchantmentChange.Added, Assert.Single(h.Of<EnchantmentChanged>()).Change);
    }

    [Fact]
    public void An_enchantment_of_a_replaced_layer_at_the_real_count_leaves_the_other_137_alone()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        var before = q.Enchantments;

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Ench(4460, 3, 101, 53, 0x2000000 | 0x4000 | 0x1000 | 8, 61, 0.5f, duration: -1)));

        Assert.Equal(138, q.Enchantments.Length);
        var replaced = IndexOf(q.Enchantments, 4460, 3);
        Assert.Equal(0.5f, q.Enchantments[replaced].StatModValue);
        for (var i = 0; i < before.Length; i++)
        {
            if (i != replaced)
            {
                Assert.Equal(before[i], q.Enchantments[i]);
            }
        }
    }

    // ------------------------------------------------------------------ 02C4

    [Fact]
    public void The_list_form_adds_some_and_replaces_others_with_one_event_each()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f)));
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicUpdateMultipleEnchantments(p, s, [Health(5000, 1, 300, 300, 25f), Health(5001, 1, 301, 200, 5f)]));

        Assert.Equal(140, q.Enchantments.Length);
        var events = h.Of<EnchantmentChanged>();
        Assert.Equal([EnchantmentChange.Added, EnchantmentChange.Replaced], events.Select(e => e.Change));
        Assert.Equal([new LayeredSpellDesc(5001, 1)], events[0].Spells.ToArray());
        Assert.Equal([new LayeredSpellDesc(5000, 1)], events[1].Spells.ToArray());
        Assert.All(events, e => Assert.Equal(140, e.Count));
        Assert.Equal(68u, HealthMaximum(h)); // categories 300 (+25) and 301 (+5) both apply: 38 + 25 + 5
    }

    // ------------------------------------------------------------------ the removals

    [Fact]
    public void A_removal_takes_one_layer_and_keeps_the_others_of_the_spell()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, 4459, 4));

        Assert.Equal(137, q.Enchantments.Length);
        Assert.Equal([1, 2, 3], q.Enchantments.Where(e => e.SpellId == 4459).Select(e => (int)e.Layer).Order());
    }

    [Fact]
    public void A_removal_of_an_enchantment_the_registry_lacks_changes_nothing_and_is_counted()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        var version = h.State.Version;
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, 9999, 1));

        Assert.Equal(138, q.Enchantments.Length);
        Assert.Empty(h.Of<EnchantmentChanged>());
        Assert.Equal(1, h.Reducer.EnchantmentRemovalsMissed);
        Assert.Equal(version, h.State.Version);
    }

    [Fact]
    public void The_list_removal_notifies_and_the_dispels_are_silent()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicRemoveMultipleEnchantments(p, s, [(4459, 1), (4459, 2)]));
        Send(h, (p, s) => AceM3Writers.MagicDispelEnchantment(p, s, 4459, 3));
        Send(h, (p, s) => AceM3Writers.MagicDispelMultipleEnchantments(p, s, [(4459, 4), (4460, 1)]));

        Assert.Equal(133, q.Enchantments.Length);
        var events = h.Of<EnchantmentChanged>();
        Assert.Equal(
            [(EnchantmentChange.Removed, true, 2, 136), (EnchantmentChange.Dispelled, false, 1, 135), (EnchantmentChange.Dispelled, false, 2, 133)],
            events.Select(e => (e.Change, e.Notify, e.Spells.Length, e.Count)));
    }

    // ------------------------------------------------------------------ the vitae, the cooldowns and the purges

    /// <summary>The live shape plus a timed multiplicative, two timed additive (one harmful), a cooldown and the vitae: 143 entries.</summary>
    private ReducerHarness BuffedWithLiveEntries()
    {
        var h = Buffed();
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Ench(5100, 1, 310, 100, MultSecondaryBeneficial, 3, 1.1f)));
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5101, 1, 311, 100, 5f)));
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5102, 1, 312, 100, -5f, type: AddSecondaryHarmful)));
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Ench(0x8005, 1, 0x8000, 0, CooldownType, 0, 0f, duration: 30)));
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Ench(666, 0, 666, 0, VitaeType, 0, 0.95f, duration: -1)));
        Assert.Equal(143, h.State.Self.Qualities.Enchantments.Length);
        h.ClearEvents();
        return h;
    }

    [Fact]
    public void The_registry_keeps_the_vitae_last_and_the_cooldown_before_it()
    {
        var h = BuffedWithLiveEntries();
        var list = h.State.Self.Qualities.Enchantments;
        Assert.Equal(EnchantmentBucket.Vitae, list[^1].Bucket);
        Assert.Equal(EnchantmentBucket.Cooldown, list[^2].Bucket);
        Assert.True(h.State.Self.Qualities.TryGetVitae(out var vitae));
        Assert.Equal(666, vitae.SpellId);
    }

    [Fact]
    public void A_purge_takes_the_timed_enchantments_and_keeps_the_lasting_ones_the_cooldown_and_the_vitae()
    {
        var h = BuffedWithLiveEntries();
        var q = h.State.Self.Qualities;

        Send(h, AceM3Writers.MagicPurgeEnchantments);

        Assert.Equal(140, q.Enchantments.Length);
        Assert.Equal(138, q.Enchantments.Count(e => e.Duration < 0 && e.SpellId != 666));
        Assert.Contains(q.Enchantments, e => e.SpellId == 0x8005);
        Assert.Contains(q.Enchantments, e => e.SpellId == 666);
        var changed = Assert.Single(h.Of<EnchantmentChanged>());
        Assert.Equal((EnchantmentChange.Purged, 140, false), (changed.Change, changed.Count, changed.VitaeChanged));
        Assert.Equal([5100, 5101, 5102], changed.Spells.Select(s => (int)s.SpellId).Order());
    }

    [Fact]
    public void A_purge_of_the_bad_takes_only_the_harmful_timed_enchantments()
    {
        var h = BuffedWithLiveEntries();
        var q = h.State.Self.Qualities;

        Send(h, AceM3Writers.MagicPurgeBadEnchantments);

        Assert.Equal(142, q.Enchantments.Length);
        Assert.DoesNotContain(q.Enchantments, e => e.SpellId == 5102);
        Assert.Contains(q.Enchantments, e => e.SpellId == 5100);
        Assert.Contains(q.Enchantments, e => e.SpellId == 5101);
        var changed = Assert.Single(h.Of<EnchantmentChanged>());
        Assert.Equal((EnchantmentChange.PurgedBad, 142), (changed.Change, changed.Count));
        Assert.Equal([new LayeredSpellDesc(5102, 1)], changed.Spells.ToArray());
    }

    [Fact]
    public void A_purge_with_nothing_to_take_changes_nothing()
    {
        var h = Buffed();
        var version = h.State.Version;
        h.ClearEvents();

        Send(h, AceM3Writers.MagicPurgeEnchantments);
        Send(h, AceM3Writers.MagicPurgeBadEnchantments);

        Assert.Equal(138, h.State.Self.Qualities.Enchantments.Length);
        Assert.Empty(h.Of<EnchantmentChanged>());
        Assert.Equal(version, h.State.Version);
    }

    [Fact]
    public void The_vitae_is_replaced_by_its_update_and_removed_at_layer_zero()
    {
        var h = BuffedWithLiveEntries();
        var q = h.State.Self.Qualities;

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Ench(666, 0, 666, 0, VitaeType, 0, 0.9f, duration: -1)));
        Assert.Equal(143, q.Enchantments.Length);
        Assert.Equal(0.9f, q.Enchantments[^1].StatModValue);
        var replaced = Assert.Single(h.Of<EnchantmentChanged>());
        Assert.Equal((EnchantmentChange.Replaced, true), (replaced.Change, replaced.VitaeChanged));

        Send(h, (p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, 666, 0));
        Assert.Equal(142, q.Enchantments.Length);
        Assert.False(q.TryGetVitae(out _));
        Assert.True(h.Of<EnchantmentChanged>()[^1].VitaeChanged);
    }

    // ------------------------------------------------------------------ the bucket rule (TODO(m3-enchant-bucket))

    [Theory]
    [InlineData(VitaeType, (ushort)5, EnchantmentBucket.Vitae)]
    [InlineData(0x4000u, (ushort)666, EnchantmentBucket.Vitae)] // the vitae spell by number, as retail's IsEnchanted tests it
    [InlineData(CooldownType, (ushort)0x8005, EnchantmentBucket.Cooldown)]
    [InlineData(MultSecondaryBeneficial, (ushort)5, EnchantmentBucket.Multiplicative)]
    [InlineData(AddSecondaryBeneficial, (ushort)5, EnchantmentBucket.Additive)]
    public void The_bucket_is_retails_by_the_type_bits(uint type, ushort spell, EnchantmentBucket expected)
    {
        var d = Desc(spell, 1, 1, 1, type, 1, 1f, 0, 10, EnchantmentBucket.Additive);
        Assert.True(EnchantmentRules.TryClassify(d, out var bucket));
        Assert.Equal(expected, bucket);
    }

    [Theory]
    [InlineData(0x2u)] // none of the three bits
    [InlineData(0x4000u | 0x8000u | 0x2u)] // two of them
    public void An_enchantment_retail_refuses_is_ignored_loudly(uint type)
    {
        var h = Buffed();
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Ench(5200, 1, 320, 1, type, 1, 5f)));

        Assert.Equal(138, h.State.Self.Qualities.Enchantments.Length);
        Assert.Empty(h.Of<EnchantmentChanged>());
        Assert.Equal(1, h.Reducer.EnchantmentsRejected);
        Assert.Contains(h.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Message.Contains("m3-enchant-type-parity", StringComparison.Ordinal));
    }

    [Fact]
    public void The_registry_applies_the_type_rule_where_net_derived_the_bucket_from_the_category()
    {
        // Category 0x8000 with an additive type: net's codec calls it a cooldown (its derived rule); retail's rule, which the reducer applies, files it by the type's bits.
        var h = Buffed();
        var body = AceM3Writers.MagicUpdateEnchantment(Player, 7, Health(0x5300, 1, 0x8000, 50, 7f));
        var decoded = new List<EnchantmentDesc>();
        InboundDispatcher.Dispatch(h.Context, body, new CaptureUpdate(decoded));
        Assert.Equal(EnchantmentBucket.Cooldown, Assert.Single(decoded).Bucket);

        InboundDispatcher.Dispatch(h.Context, body, h.Reducer);

        var entry = h.State.Self.Qualities.Enchantments.Single(e => e.SpellId == 0x5300);
        Assert.Equal(EnchantmentBucket.Additive, entry.Bucket);
        Assert.Equal(100, h.State.Self.Qualities.Enchantments.IndexOf(entry));
    }

    private sealed class CaptureUpdate(List<EnchantmentDesc> into) : IInboundMessageHandler
    {
        public void OnMagicUpdateEnchantment(in InboundContext context, in GameEventHeader header, in MagicUpdateEnchantmentEvent message) => into.Add(message.Enchantment);
    }

    // ------------------------------------------------------------------ time

    [Fact]
    public void An_entry_keeps_its_start_and_duration_and_counts_down_on_the_session_clock()
    {
        var h = Buffed();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        h.State.Time.Update(5000, 0); // the server read 5000 at session time 0: it reads 5010 now
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f, start: -100, duration: 300)));
        var q = h.State.Self.Qualities;

        Assert.True(q.TryGetEnchantment(5000, 1, out var entry));
        Assert.Equal((-100.0, 300.0), (entry.Enchantment.StartTime, entry.Enchantment.Duration)); // as sent
        Assert.Equal(10.0, entry.ReceivedSeconds);
        Assert.Equal(-90.0, entry.StartSeconds);
        Assert.Equal(210.0, entry.ExpirySeconds);
        Assert.Equal(200.0, entry.RemainingSeconds(10));
        Assert.Equal(150.0, entry.RemainingSeconds(60));
        Assert.Equal(0.0, entry.RemainingSeconds(500));
        Assert.Equal((double?)5210.0, entry.ExpiryServerSeconds(h.State.Time));
        Assert.Null(entry.ExpiryServerSeconds(new ServerTimeState()));
        Assert.True(q.TryGetEnchantment(4459, 1, out var lasting));
        Assert.True(lasting.IsPermanent);
        Assert.Equal(double.PositiveInfinity, lasting.RemainingSeconds(1e9));
        Assert.False(lasting.IsOverdue(1e9));
    }

    [Fact]
    public void The_description_stamps_its_entries_with_the_time_it_arrived()
    {
        var h = Rig();
        h.EnterWorld();
        h.Time.Advance(TimeSpan.FromSeconds(30));

        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription), Description([Desc(7000, 1, 1, 10, AddSecondaryBeneficial, 1, 5f, -10, 100, EnchantmentBucket.Additive)]));

        Assert.True(h.State.Self.Qualities.TryGetEnchantment(7000, 1, out var entry));
        Assert.Equal(90.0, entry.RemainingSeconds(30)); // started at 20, ends at 120
        Assert.Equal(30.0, entry.ReceivedSeconds);
    }

    [Fact]
    public void The_duel_compares_when_enchantments_started_not_the_start_numbers_as_sent()
    {
        // Two spells of one category and one power, both sent with start 0; the second arrived 100 s later, so it started later and wins the duel (retail adds its clock to the start on arrival).
        var h = Buffed();
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5001, 1, 301, 300, 10f)));
        Assert.Equal(48u, HealthMaximum(h));
        h.Time.Advance(TimeSpan.FromSeconds(100));

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5002, 1, 301, 300, 30f)));

        Assert.Equal(68u, HealthMaximum(h));
    }

    [Fact]
    public void An_entry_past_its_expiry_with_no_removal_is_kept_and_counted_once()
    {
        var h = Buffed();
        var q = h.State.Self.Qualities;
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f, duration: 60)));
        h.Time.Advance(TimeSpan.FromSeconds(65));
        Assert.Equal(0, q.CountOverdueEnchantments(h.Clock.Now)); // 5 s past: inside the allowance
        h.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, q.CountOverdueEnchantments(h.Clock.Now));
        Assert.Equal(0, h.Reducer.EnchantmentsOverdue);

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5001, 1, 301, 300, 5f)));
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5002, 1, 302, 300, 5f)));

        Assert.Equal(1, h.Reducer.EnchantmentsOverdue);
        Assert.True(q.TryGetEnchantment(5000, 1, out _)); // no timer removes it: only the server's 02C3 does
        Assert.Contains(h.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Message.Contains("m3-enchant-overdue", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the counter and the clear

    public static TheoryData<byte[]> AppliedBodies => new()
    {
        { AceM3Writers.MagicUpdateSpell(Player, 21, 0x0C8B) },
        { AceM3Writers.MagicUpdateEnchantment(Player, 22, Health(5000, 1, 300, 300, 22f)) },
        { AceM3Writers.MagicRemoveEnchantment(Player, 23, 5000, 1) },
        { AceM3Writers.MagicUpdateMultipleEnchantments(Player, 24, [Health(5000, 1, 300, 300, 22f)]) },
        { AceM3Writers.MagicRemoveMultipleEnchantments(Player, 25, [(5000, 1)]) },
        { AceM3Writers.MagicPurgeEnchantments(Player, 26) },
        { AceM3Writers.MagicDispelEnchantment(Player, 27, 5000, 1) },
        { AceM3Writers.MagicDispelMultipleEnchantments(Player, 28, [(5000, 1)]) },
        { AceM3Writers.MagicPurgeBadEnchantments(Player, 29) },
    };

    [Theory]
    [MemberData(nameof(AppliedBodies))]
    public void An_applied_code_no_longer_counts_as_decoded_not_yet_applied(byte[] body)
    {
        var h = Rig();
        h.EnterWorld();

        var result = InboundDispatcher.Dispatch(h.Context, body, h.Reducer);

        Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
        Assert.Equal(0, h.Reducer.Counters.DecodedNotApplied);
        Assert.Empty(h.Of<UnknownMessage>());
        Assert.Empty(h.Of<MessageDropped>());
    }

    [Fact]
    public void A_new_description_replaces_the_registry_and_the_events_stop_naming_the_old_one()
    {
        var h = Buffed();
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f)));
        Assert.Equal(139, h.State.Self.Qualities.Enchantments.Length);

        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(Player, 9, GameEventTypes.PlayerDescription), Description([]));

        Assert.Empty(h.State.Self.Qualities.Enchantments);
        Assert.Empty(h.State.Self.Qualities.ActiveEnchantments);
        Assert.Equal(28u, HealthMaximum(h)); // the base: (55 + 1) / 2 = 28
    }

    [Fact]
    public void Every_registry_change_publishes_QualitiesChanged_Enchantments_beside_the_enchantment_event_and_a_missed_removal_publishes_none()
    {
        var h = Buffed();
        h.ClearEvents();

        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5000, 1, 300, 300, 22f)));
        Send(h, (p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, 5000, 1));
        Send(h, (p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, 5000, 1)); // already gone: nothing changes
        Send(h, (p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, Health(5001, 1, 300, 300, 22f)));
        Send(h, (p, s) => AceM3Writers.MagicPurgeEnchantments(p, s));

        Assert.Equal(
            [EnchantmentChange.Added, EnchantmentChange.Removed, EnchantmentChange.Added, EnchantmentChange.Purged],
            h.Of<EnchantmentChanged>().Select(e => e.Change));
        Assert.Equal(
            Enumerable.Repeat(new QualitiesChanged(QualityChange.Enchantments, 0), 4),
            h.Of<QualitiesChanged>());
    }
}

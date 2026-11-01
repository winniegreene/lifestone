// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Examine.cs (HandleActionIdentifyObject), Source/ACE.Server/Network/GameEvent/Events/GameEventIdentifyObjectResponse.cs and
// Source/ACE.Server/Entity/AppraiseInfo.cs (BuildFlags 700-735, the sections 743-786) (AGPL-3.0)
// [with changes: the order and the lengths are the ones the live capture records (cache/m4/3-2/capture.md C3); the stand-in rolls no appraisal skill (an NPC creature is always appraised) and writes a labelled subset of the tables].
using System.Collections.Immutable;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

// M4 3.2 s3: the stand-in's appraisal (TODO(m4-standin-vendor), closed): an Identify 0x00C8 of an item the player holds or a vendor lists (success, the tables the stand-in knows), of a vendor (an NPC creature: success with the creature profile and
// the armour levels, as capture C3 showed for Thelnoth), of a guid the world does not hold (flags 0, success 0) and of guid 0 (no reply: the target is cleared). No other creature is added (D23).
internal sealed partial class StandInWorld
{
    /// <summary>ACE's <c>ItemType.Creature</c>.</summary>
    private const uint CreatureItemType = 0x10;

    /// <summary>The creature profile flag that says the attributes follow (<c>CreatureProfile.ShowAttributes</c>, 0x8).</summary>
    private const uint ShowAttributes = 0x8;

    private readonly Dictionary<uint, StandInMagicItem> _magicItems = [];
    private readonly Dictionary<uint, (string Text, string Scribe)> _inscriptions = [];

    /// <summary>The inscriptions the stand-in holds, by item (M5 7 D137; tests).</summary>
    internal IReadOnlyDictionary<uint, (string Text, string Scribe)> Inscriptions => _inscriptions;

    /// <summary>True when the stocked item <paramref name="guid"/> is inscribable (ObjectDescriptionFlags 0x2): the stand-in's magic items are, as ACE's loot is.</summary>
    internal bool IsInscribable(uint guid) => _magicItems.ContainsKey(guid);

    /// <summary>
    /// A SetInscription (0x00BF; ACE <c>Player_Inventory.HandleActionSetInscription</c> 3634ff, @95ef426d): an inscribable item the player holds, uninscribed or inscribed by this character, takes the text and the player's name as
    /// its scribe; an empty text clears both. ACE answers nothing, and neither does the stand-in; the next appraisal carries the strings 7 (Inscription) and 8 (ScribeName).
    /// </summary>
    internal OutboundTicket SetInscription(uint item, string text)
    {
        var self = _reducer.State.Self;
        var held = _reducer.State.Objects.TryGet(item, out var obj) && (obj.Weenie.ContainerId == self.Guid || obj.Weenie.WielderId == self.Guid || self.Inventory.Contains(item));
        var mine = !_inscriptions.TryGetValue(item, out var existing) || existing.Scribe == self.Name;
        if (IsInscribable(item) && held && mine)
        {
            if (string.IsNullOrEmpty(text))
            {
                _inscriptions.Remove(item);
            }
            else
            {
                _inscriptions[item] = (text, self.Name ?? string.Empty);
            }
        }

        return Ticket();
    }

    /// <summary>Test lever (M5 7 D137): the next appraisal of a creature fails as ACE's failed roll does (success 0, the profile's health alone).</summary>
    internal bool FailNextCreatureAppraisal { get; set; }

    /// <summary>Every appraisal the stand-in has answered, in order (tests).</summary>
    internal List<IdentifyResponseMsg> Appraised { get; } = [];

    /// <summary>
    /// Makes the stocked item <paramref name="guid"/> a magic item (M5 7 S6, the offline review frame of the examination; a placeholder, <c>TODO(m5-standin-magic-item)</c>): its CreateObject glows magical (UiEffects 1) and its
    /// appraisal carries <paramref name="magic"/>'s spell book, Arcane Lore, spellcraft, mana and workmanship, as ACE's loot does.
    /// </summary>
    internal void MarkMagic(uint guid, StandInMagicItem magic)
    {
        Placeholder("m5-standin-magic-item", "a stand-in magic item: a real weenie's art with a spell book, Arcane Lore, spellcraft, mana and workmanship the check gives it, not ACE's loot roll");
        _magicItems[guid] = magic;
    }

    /// <summary>
    /// An Identify (GA 0x00C8; <c>Player_Examine.HandleActionIdentifyObject</c>): guid 0 clears the target and gets no reply; a guid the world does not hold gets GameEvent 0x00C9 with flags 0 and success 0; anything else the
    /// stand-in knows gets its 00C9 with success 1 (capture C3: every reply came 1 to 10 ms after the send, no busy hold). The repeat of the current successful target is answered again with no roll, as ACE does. The player's
    /// Assess and a creature's Deception are not rolled (<c>TODO(m4-appraise-fail-live)</c>: a failed creature roll and its 5 s cooldown were not seen live).
    /// </summary>
    internal OutboundTicket Identify(uint guid)
    {
        if (guid != 0)
        {
            Later(sink => IdentifyNow(sink, guid));
        }

        return Ticket();
    }

    private void IdentifyNow(IAcSessionSink sink, uint guid)
    {
        var answer = Vendors.TryGetValue(guid, out var vendor) && _visit.Vendors ? VendorAppraisal(vendor) : TryAppraisedItem(guid, out var item) ? ItemAppraisal(guid, item) : MissingAppraisal(guid);
        Appraised.Add(answer);
        AnswerLog.Add("00C9");
        sink.OnIdentifyResponse(Context(sink), Header(0x00C9), answer);
    }

    private static IdentifyResponseMsg MissingAppraisal(uint guid) => Appraisal(guid, false);

    private static IdentifyResponseMsg Appraisal(
        uint guid,
        bool success,
        ImmutableDictionary<uint, int>? ints = null,
        ImmutableDictionary<uint, double>? floats = null,
        ImmutableDictionary<uint, string>? strings = null,
        ImmutableDictionary<uint, uint>? dids = null,
        AppraisalCreatureProfile? creature = null,
        AppraisalArmorLevels? levels = null,
        ImmutableArray<uint> spells = default)
    {
        spells = spells.IsDefault ? [] : spells;
        ints ??= ImmutableDictionary<uint, int>.Empty;
        floats ??= ImmutableDictionary<uint, double>.Empty;
        strings ??= ImmutableDictionary<uint, string>.Empty;
        dids ??= ImmutableDictionary<uint, uint>.Empty;
        var flags = IdentifyResponseFlags.None; // AppraiseInfo.BuildFlags: a section is announced when it is not empty
        flags |= ints.IsEmpty ? 0 : IdentifyResponseFlags.IntStatsTable;
        flags |= floats.IsEmpty ? 0 : IdentifyResponseFlags.FloatStatsTable;
        flags |= strings.IsEmpty ? 0 : IdentifyResponseFlags.StringStatsTable;
        flags |= dids.IsEmpty ? 0 : IdentifyResponseFlags.DidStatsTable;
        flags |= spells.IsEmpty ? 0 : IdentifyResponseFlags.SpellBook;
        flags |= creature is null ? 0 : IdentifyResponseFlags.CreatureProfile;
        flags |= levels is null ? 0 : IdentifyResponseFlags.ArmorLevels;
        return new IdentifyResponseMsg(
            guid,
            flags,
            success,
            ints,
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            floats,
            strings,
            dids,
            spells,
            null,
            creature,
            null,
            null,
            null,
            null,
            null,
            levels);
    }

    // --- an NPC vendor ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A vendor's appraisal (capture C3 for Thelnoth: flags 0x510D, success 1, the creature profile and the armour levels). The numbers are the world database's: the six attributes, the vitals (a vital is its start plus a half of Endurance, or
    /// Endurance, or Self), the level, the creature type, the use radius and the model ids; the armour levels are zeros and the tables are a subset (<c>TODO(m4-standin-appraise-tables)</c>: the capture's int table had six rows, ours has the six listed here).
    /// </summary>
    private IdentifyResponseMsg VendorAppraisal(StandInVendor vendor)
    {
        Placeholder("m4-standin-appraise-tables", "an appraisal carries a labelled subset of ACE's tables (the item's type, burden, value, stack and uses; an NPC's six ints, its radius, name, model ids, attributes and vitals from the world database; zero armour levels): no weapon or armour profile, no spell book");
        var thelnoth = vendor.Guid == ThelnothGuid;
        var attributes = thelnoth ? new uint[] { 20, 60, 60, 75, 90, 65 } : [20, 60, 65, 60, 30, 20];
        var health = (thelnoth ? 55u : 0u) + (attributes[1] / 2);
        var stamina = (thelnoth ? 60u : 0u) + attributes[1];
        var mana = (thelnoth ? 40u : 0u) + attributes[5];
        if (FailNextCreatureAppraisal)
        {
            // M5 7 D137 (a lever for the offline frame of a failed roll): ACE's failed creature roll, CreatureProfile(creature, false): flags 0, the health alone; ArmorLevels withheld (AppraiseInfo.BuildCreature).
            FailNextCreatureAppraisal = false;
            var failedInts = ImmutableDictionary<uint, int>.Empty.Add(1, (int)CreatureItemType).Add(2, 31).Add(25, thelnoth ? 8 : 5);
            return Appraisal(vendor.Guid, false, failedInts, strings: ImmutableDictionary<uint, string>.Empty.Add(1, vendor.Name), creature: new AppraisalCreatureProfile(0, health * 3 / 4, health, null, null, null, null, null, null, null, null, null, null, null, null));
        }

        var profile = new AppraisalCreatureProfile(ShowAttributes, health, health, attributes[0], attributes[1], attributes[2], attributes[3], attributes[4], attributes[5], stamina, mana, stamina, mana, null, null);
        var ints = ImmutableDictionary<uint, int>.Empty
            .Add(1, (int)CreatureItemType).Add(2, 31).Add(8, 120).Add(25, thelnoth ? 8 : 5).Add(133, 4).Add(134, 16);
        var floats = ImmutableDictionary<uint, double>.Empty.Add(54, thelnoth ? 3.0 : 5.0);
        var strings = ImmutableDictionary<uint, string>.Empty.Add(1, vendor.Name);
        var dids = ImmutableDictionary<uint, uint>.Empty.Add(1, 33554433).Add(2, 150994945).Add(3, 536870913).Add(4, 805306368).Add(8, 100667446);
        return Appraisal(vendor.Guid, true, ints, floats, strings, dids, profile, new AppraisalArmorLevels(0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    // --- an item ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>An item the stand-in can appraise: a pack or world object of the stand-in (a creature excluded), a stock row of a vendor, an item a vendor holds after a sale.</summary>
    private bool TryAppraisedItem(uint guid, out WeenieDesc desc)
    {
        if (_reducer.State.Objects.TryGet(guid, out var known) && known.Weenie.ItemType != CreatureItemType)
        {
            desc = known.Weenie;
            return true;
        }

        if (_visit.Vendors)
        {
            foreach (var vendor in Vendors.Values)
            {
                if (vendor.Rows.TryGetValue(guid, out var row))
                {
                    desc = ItemDesc(row, 0, null);
                    return true;
                }

                if (vendor.Uniques.TryGetValue(guid, out var unique))
                {
                    desc = unique;
                    return true;
                }
            }
        }

        desc = null!;
        return false;
    }

    /// <summary>
    /// An item's appraisal (capture C3 for the kit: flags 0x5, success 1, no profile): the integer table (ItemType 1, burden 5, Value 19, and for a stack its limit 11 and size 12, for a kit its uses 91 and 92) and for a healing kit the float table
    /// (<c>HealkitMod</c> 100: the world database's 1.0). A weapon or armour profile is not written (<c>TODO(m4-standin-appraise-tables)</c>).
    /// </summary>
    private IdentifyResponseMsg ItemAppraisal(uint guid, WeenieDesc desc)
    {
        Placeholder("m4-standin-appraise-tables", "an appraisal carries a labelled subset of ACE's tables (the item's type, burden, value, stack and uses; an NPC's six ints, its radius, name, model ids, attributes and vitals from the world database; zero armour levels): no weapon or armour profile, no spell book");
        var ints = ImmutableDictionary<uint, int>.Empty.Add(1, (int)desc.ItemType);
        if (desc.Burden is { } burden)
        {
            ints = ints.Add(5, burden);
        }

        if (desc.Value is { } value)
        {
            ints = ints.Add(19, (int)value);
        }

        if (desc.MaxStackSize is { } maxStack)
        {
            ints = ints.Add(11, maxStack);
            ints = ints.Add(12, desc.StackSize ?? 1);
        }

        if (desc.MaxStructure is { } maxUses)
        {
            ints = ints.Add(91, maxUses);
            ints = ints.Add(92, KitUsesLeft(guid));
        }

        var floats = desc.WeenieClassId is HandyKitWcid or 629 ? ImmutableDictionary<uint, double>.Empty.Add(100, 1.0) : ImmutableDictionary<uint, double>.Empty;
        if (_magicItems.TryGetValue(guid, out var magic))
        {
            ints = ints.SetItem(19, magic.Value).SetItem(105, magic.Workmanship).SetItem(106, magic.Spellcraft).SetItem(107, magic.Mana).SetItem(108, magic.MaxMana).SetItem(109, magic.ArcaneLore);
            foreach (var (id, extra) in magic.ExtraInts ?? ImmutableDictionary<uint, int>.Empty)
            {
                ints = ints.SetItem(id, extra); // the check's wield requirements and activation heritage (M5 7 D137)
            }

            var strings = _inscriptions.TryGetValue(guid, out var inscription) ? ImmutableDictionary<uint, string>.Empty.Add(7, inscription.Text).Add(8, inscription.Scribe) : null;
            return Appraisal(guid, true, ints, floats, strings, spells: magic.Spells);
        }

        return Appraisal(guid, true, ints, floats);
    }
}

/// <summary>A stand-in magic item's appraisal tables (M5 7 S6; a placeholder for ACE's loot roll): the spell ids, Arcane Lore (ItemDifficulty 109), spellcraft (106), mana now and at most (107, 108), workmanship (105) and Value (19).</summary>
public sealed record StandInMagicItem(ImmutableArray<uint> Spells, int ArcaneLore, int Spellcraft, int Mana, int MaxMana, int Workmanship, int Value)
{
    /// <summary>More PropertyInts the appraisal carries (M5 7 D137: the wield requirements and the activation heritage of the offline frames), or null.</summary>
    public ImmutableDictionary<uint, int>? ExtraInts { get; init; }
}

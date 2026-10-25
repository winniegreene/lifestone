using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The fighters' gear recharge (closes <c>TODO(m4-gear-mana)</c>; <see cref="GearRecharge"/>) on a scripted ACE: the set worn (the Archer's bow in the pack), every piece's ItemCurMana kept as ACE keeps it, burned by <see cref="RechargeRig.Burn"/> at the
/// piece's ManaRate (<c>Player_Tick.cs:623-659</c>) with its spells removed at 0 (<c>HandleManaDepleted</c>), an appraisal answered with ItemCurMana and ItemMaxMana, a charge used as <c>ManaStone.HandleActionUseOnTarget</c> does (the even ration over the equipped items
/// below their maximum, or one item filled; consumed), and a wield casting the piece's spells only with mana (<c>Player_Inventory.cs:313-315</c>), an unwield removing them.
/// </summary>
public sealed class GearRechargeTests
{
    [Fact]
    public void Full_gear_is_only_appraised_nothing_is_used_and_the_verdict_says_so()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler);
        var step = rig.Recharge();

        Assert.True(rig.Run(step));

        Assert.Equal(" gear_mana_min=100 recharged=0", step.VerdictFields());
        Assert.Empty(rig.Uses);
        Assert.Equal(FighterGearSet.Brawler.Pieces.Count + 1, rig.Identified.Distinct().Count()); // the 13 worn pieces and the charge
        Assert.Empty(rig.Sent); // nothing that changes the character: a ready fighter stays ready
    }

    [Fact]
    public void Three_hours_of_burn_put_Branith_s_Shirt_under_half_and_one_charge_on_the_player_fills_every_piece()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler);
        rig.Burn(3 * 3600); // the Shirt: 500 at 0.025 a second, 230 left (46%); the 800s at 0.033, 444 (55%)
        var step = rig.Recharge();

        Assert.True(rig.Run(step));

        Assert.Equal(46, step.MinBefore);
        Assert.Equal(" gear_mana_min=100 recharged=1", step.VerdictFields());
        var use = Assert.Single(rig.Uses);
        Assert.Equal((RechargeRig.FirstStone, WorldActionRig.Player), use); // on the player: every equipped item below its maximum
        Assert.All(rig.Worn, guid => Assert.Equal(rig.MaxManaOf(guid), rig.ManaOf(guid)));
        Assert.False(rig.Holds(RechargeRig.FirstStone)); // a retail charge is consumed
        Assert.StartsWith("use a mana charge", Assert.Single(rig.Sent), StringComparison.Ordinal);
        Assert.Empty(step.Rewielded); // no piece had run dry
    }

    [Theory]
    [InlineData(250, 0)] // exactly half: not under the trigger
    [InlineData(249, 1)]
    public void The_trigger_is_strictly_under_half_of_a_piece_s_maximum(int shirtMana, int uses)
    {
        var rig = new RechargeRig(FighterGearSet.Brawler);
        rig.SetMana(2032, shirtMana); // Branith's Shirt, 500 full
        var step = rig.Recharge();

        Assert.True(rig.Run(step));

        Assert.Equal(uses, rig.Uses.Count);
    }

    [Fact]
    public void Six_hours_of_burn_empty_the_Shirt_one_charge_lifts_every_piece_over_half_and_the_Shirt_is_worn_again_to_wake_its_spells()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler, charges: 2);
        rig.Burn(6 * 3600); // the Shirt runs dry (its spells removed); about 9,000 short in all: the charge rations 384 to each of the 13 pieces
        Assert.Contains("Branith's Shirt", rig.SilentNames());
        var step = rig.Recharge(maxCharges: 2);

        Assert.True(rig.Run(step));

        Assert.Equal(0, step.MinBefore);
        Assert.Equal(" gear_mana_min=59 recharged=1", step.VerdictFields()); // the 800s at 472: no piece under half, so the second charge is kept
        Assert.Single(rig.Uses);
        Assert.Single(new[] { RechargeRig.FirstStone, RechargeRig.FirstStone + 1 }, rig.Holds); // the second charge is kept
        Assert.Equal(["Branith's Shirt"], step.Rewielded);
        Assert.Empty(rig.SilentNames()); // spells_active is full again
        // The order: the charge, then the Shirt off and on at the slots it had.
        Assert.Equal(["use", "unwield:Branith's Shirt", "wield:Branith's Shirt@0xA00"], rig.Ops);
    }

    [Fact]
    public void Eight_hours_of_burn_take_both_charges_and_the_four_pieces_that_ran_dry_are_worn_again()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler, charges: 2);
        rig.Burn(8 * 3600); // the Gauntlets, the Bracers, the Shirt and the necklace run dry; about 11,450 short
        var step = rig.Recharge(maxCharges: 2);

        Assert.True(rig.Run(step));

        Assert.Equal(2, step.Charges);
        Assert.True(step.MinAfter >= 50, step.VerdictFields());
        Assert.Equal(["Ancient Armored Gauntlets", "Ancient Armored Bracers", "Branith's Shirt", "Tears of Jvikti Kheval"], step.Rewielded);
        Assert.Empty(rig.SilentNames());
        Assert.Equal(["use", "use"], rig.Ops.Take(2));
    }

    [Fact]
    public void With_one_charge_a_deep_shortfall_stays_low_and_is_named_and_the_step_still_ends_without_failing()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler);
        rig.Burn(8 * 3600); // about 11,450 short: one charge rations 384 to each of the 13 pieces, so Footman's Boots (50 of 1,000 left) end at 43%
        var step = rig.Recharge();

        Assert.True(rig.Run(step));

        Assert.Single(rig.Uses);
        Assert.Equal(1, step.Charges);
        Assert.Equal(43, step.MinAfter);
        Assert.Contains(rig.R.H.Log.Entries, e => e.Message.Contains("still low after the kit's 1 charge(s)", StringComparison.Ordinal));
        Assert.Matches(@"^ gear_mana_min=\d+ recharged=1$", step.VerdictFields());
    }

    [Fact]
    public void An_empty_stone_is_never_used_an_empty_stone_on_an_item_destroys_the_item()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler);
        rig.Burn(3 * 3600);
        rig.SetMana(FighterKit.ManaChargeWcid, null); // a drained stone: no ItemCurMana
        var step = rig.Recharge();

        Assert.True(rig.Run(step));

        Assert.Empty(rig.Uses);
        Assert.Equal(" gear_mana_min=46 recharged=0:no-charge", step.VerdictFields());
    }

    [Fact]
    public void The_Archer_s_bow_kept_in_the_pack_is_charged_on_its_own_when_only_it_is_low()
    {
        var rig = new RechargeRig(FighterGearSet.Archer);
        var bow = rig.SetMana(30303, 600); // Serpent's Flight, 2,000 full: 30%
        var step = rig.Recharge();

        Assert.True(rig.Run(step));

        Assert.Equal((RechargeRig.FirstStone, bow), Assert.Single(rig.Uses));
        Assert.Equal(2000, rig.ManaOf(bow));
        Assert.Equal(" gear_mana_min=100 recharged=1", step.VerdictFields());
    }

    [Fact]
    public void Without_a_spell_table_a_charged_piece_whose_spells_were_removed_is_not_worn_again_and_says_why()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler, charges: 2);
        rig.Burn(6 * 3600);
        var step = rig.Recharge(maxCharges: 2, withSpells: false);

        Assert.True(rig.Run(step));

        Assert.Single(rig.Uses);
        Assert.Empty(step.Rewielded);
        Assert.DoesNotContain(rig.Ops, o => o.StartsWith("unwield", StringComparison.Ordinal));
        Assert.Contains(rig.R.H.Log.Entries, e => e.Message.Contains("TODO(m4-gear-mana-reactivate)", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unanswered_appraisal_leaves_the_gear_unread_and_nothing_is_charged()
    {
        var rig = new RechargeRig(FighterGearSet.Brawler) { Unanswered = RechargeRig.FirstPiece + 6 }; // Branith's Shirt
        rig.Burn(3 * 3600);
        var step = rig.Recharge();

        Assert.True(rig.Run(step));

        Assert.Empty(rig.Uses);
        Assert.Equal(" gear_mana_min=unread recharged=0:unread", step.VerdictFields());
        Assert.Contains(rig.R.H.Log.Entries, e => e.Message.Contains("Branith's Shirt (TimedOut", StringComparison.Ordinal));
    }
}

/// <summary>The scripted ACE of <see cref="GearRechargeTests"/>: one fighter's set worn (the Archer's bow in the pack), the kit's charges in the pack, a spell table of the set's spells (each its own category, Creature Enchantment).</summary>
internal sealed class RechargeRig
{
    public const uint FirstPiece = 0x80003000;
    public const uint FirstStone = 0x80004000;

    private readonly Dictionary<uint, (GearItem Kind, GearPiece? Piece, uint Location, ushort Layer)> _items = [];
    private readonly Dictionary<uint, int?> _mana = [];
    private readonly HashSet<(uint Guid, uint Spell)> _active = [];
    private int _seen;

    public RechargeRig(FighterGearSet set, int charges = 1)
    {
        Set = set;
        R = new WorldActionRig();
        R.Describe();
        Spells = set.Pieces.SelectMany(p => p.Spells).Select(s => s.Id).Distinct().ToDictionary(id => id, id => WorldActionRig.Spell(id, SpellType.Enchantment) with { School = MagicSchool.CreatureEnchantment, Category = id });
        View = new RigView(R);
        var rings = 0;
        var bracelets = 0;
        for (var i = 0; i < set.Pieces.Count; i++)
        {
            var piece = set.Pieces[i];
            var kind = piece.ToKitItem().Item;
            var location = piece.Kind switch
            {
                GearPieceKind.Weapon when !piece.IsMelee => 0u, // the Archer's bow is kept in the pack
                GearPieceKind.Ring => rings++ == 0 ? 0x40000u : 0x80000u,
                GearPieceKind.Bracelet => bracelets++ == 0 ? 0x10000u : 0x20000u,
                _ => piece.Locations,
            };
            Add(FirstPiece + (uint)i, kind, piece, location, (ushort)(i + 1));
        }

        var charge = FighterKit.Melee.ItemOf(FighterKit.ManaChargeWcid)!.Item;
        for (var i = 0; i < charges; i++)
        {
            Add(FirstStone + (uint)i, charge, null, 0, 0);
        }
    }

    public FighterGearSet Set { get; }

    public WorldActionRig R { get; }

    public IGameSessionView View { get; }

    public IReadOnlyDictionary<uint, SpellInfo> Spells { get; }

    /// <summary>A guid whose appraisal ACE never answers (the Identify times out), or 0.</summary>
    public uint Unanswered { get; init; }

    /// <summary>The guids appraised, in order.</summary>
    public List<uint> Identified { get; } = [];

    /// <summary>The charges used: the stone and the target.</summary>
    public List<(uint Stone, uint Target)> Uses { get; } = [];

    /// <summary>What the server was asked to change, in order: <c>use</c>, <c>unwield:&lt;name&gt;</c>, <c>wield:&lt;name&gt;@0x&lt;mask&gt;</c>.</summary>
    public List<string> Ops { get; } = [];

    /// <summary>The labels the step told its caller (the build's step list).</summary>
    public List<string> Sent { get; } = [];

    /// <summary>The worn pieces' guids.</summary>
    public IEnumerable<uint> Worn => _items.Where(i => i.Value.Piece is not null && i.Value.Location != 0).Select(i => i.Key);

    public GearRecharge Recharge(int maxCharges = 1, bool withSpells = true) => new(Set, FighterKit.ManaChargeWcid, maxCharges, withSpells ? Spells : null, R.H.Log, "fighter", Sent.Add);

    public bool Holds(uint guid) => _items.ContainsKey(guid);

    public int? ManaOf(uint guid) => _mana.TryGetValue(guid, out var m) ? m : _items[guid].Piece?.ItemMana ?? (_items[guid].Kind.Wcid == FighterKit.ManaChargeWcid ? FighterKit.ManaChargeMana : null);

    public int? MaxManaOf(uint guid) => _items[guid].Piece?.ItemMana;

    /// <summary>Sets the mana of the first item of <paramref name="wcid"/>; returns its guid. At 0 a worn piece's spells are removed, as ACE's depletion does.</summary>
    public uint SetMana(uint wcid, int? mana)
    {
        var guid = _items.First(i => i.Value.Kind.Wcid == wcid).Key;
        _mana[guid] = mana;
        if (mana is 0)
        {
            Dispel(guid);
        }

        return guid;
    }

    /// <summary>The worn pieces burn <paramref name="seconds"/> at their ManaRate; one that reaches 0 loses its spells (<c>HandleManaDepleted</c>).</summary>
    public void Burn(double seconds)
    {
        foreach (var guid in Worn.ToArray())
        {
            var piece = _items[guid].Piece!;
            var left = Math.Max(0, ManaOf(guid)!.Value - (int)Math.Floor(-piece.ManaRate * seconds));
            SetMana(piece.Wcid, left);
        }
    }

    /// <summary>The worn pieces none of whose wearer spells is active, by name (<see cref="FighterGearCheck"/>).</summary>
    public IReadOnlyList<string> SilentNames()
    {
        R.Tick();
        return [.. FighterGearCheck.Read(R.H.State, Set, Spells, []).Silent.Select(p => p.Name)];
    }

    /// <summary>Steps the recharge until it ends; false when <paramref name="maxSeconds"/> of session time passed first.</summary>
    public bool Run(GearRecharge step, double maxSeconds = 120)
    {
        for (var t = 0.0; t < maxSeconds; t += 0.1)
        {
            if (step.Step(View) is null)
            {
                return true;
            }

            R.Tick(0.1);
            Pump();
            R.Tick();
        }

        return false;
    }

    private void Add(uint guid, GearItem kind, GearPiece? piece, uint location, ushort layer)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: kind.Name);
        R.H.R.Reducer.OnCreateObject(
            R.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, ItemType = kind.ItemType, WeenieClassId = kind.Wcid } },
            isUpdate: false);
        _items[guid] = (kind, piece, location, layer);
        if (location == 0)
        {
            R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, guid, p, 0, 0));
            return;
        }

        R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, (int)location));
        Cast(guid);
    }

    private void Cast(uint guid)
    {
        var (_, piece, _, layer) = _items[guid];
        foreach (var spell in piece!.Spells)
        {
            _active.Add((guid, spell.Id));
            R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)spell.Id, layer, (ushort)spell.Id, 0, 1, 0, -1, guid, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 10f, 0)));
        }
    }

    private void Dispel(uint guid)
    {
        foreach (var spell in _active.Where(a => a.Guid == guid).ToArray())
        {
            _active.Remove(spell);
            R.Server((p, s) => AceM3Writers.MagicRemoveEnchantment(p, s, (ushort)spell.Spell, _items[guid].Layer));
        }
    }

    /// <summary>Answers what the Action API sent since the last call, as ACE does.</summary>
    private void Pump()
    {
        var sends = R.H.Net.Sends;
        while (_seen < sends.Count)
        {
            var send = sends[_seen++];
            switch (send.Kind)
            {
                case FakeSendKind.Identify when send.Guid != Unanswered && _items.ContainsKey(send.Guid):
                    Identified.Add(send.Guid);
                    var ints = new Dictionary<uint, int>();
                    if (ManaOf(send.Guid) is { } cur)
                    {
                        ints[AppraisalIds.ManaCurrent] = cur;
                    }

                    if (MaxManaOf(send.Guid) is { } max)
                    {
                        ints[AppraisalIds.ManaMax] = max;
                    }

                    R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, send.Guid, new AceAppraisal { Success = true, Ints = ints }));
                    break;
                case FakeSendKind.UseWithTarget:
                    Use(send.Guid, uint.Parse(send.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    break;
                case FakeSendKind.PutItemInContainer when _items.TryGetValue(send.Guid, out var off):
                    Ops.Add("unwield:" + off.Kind.Name);
                    Dispel(send.Guid);
                    _items[send.Guid] = off with { Location = 0 };
                    R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, send.Guid, p, 0, 0));
                    break;
                case FakeSendKind.GetAndWieldItem when _items.TryGetValue(send.Guid, out var on):
                    Ops.Add(string.Create(CultureInfo.InvariantCulture, $"wield:{on.Kind.Name}@0x{send.Amount:X}"));
                    _items[send.Guid] = on with { Location = send.Amount };
                    R.Server((p, s) => AceM3Writers.WieldObject(p, s, send.Guid, unchecked((int)send.Amount)));
                    if (ManaOf(send.Guid) > 0)
                    {
                        Cast(send.Guid); // TryActivateSpells, then ItemCurMana-- (Player_Inventory.cs:313-315, :359-363)
                        _mana[send.Guid] = ManaOf(send.Guid) - 1;
                    }

                    break;
            }
        }
    }

    /// <summary><c>ManaStone.HandleActionUseOnTarget</c> for a charge (<c>ManaStone.cs:119-233</c>): the ration over the equipped items, or one item filled; the line; the stone consumed; UseDone 0.</summary>
    private void Use(uint stone, uint target)
    {
        Ops.Add("use");
        Uses.Add((stone, target));
        if (ManaOf(stone) is not { } left || left <= 0)
        {
            R.UseDone(0x0036);
            return;
        }

        int given;
        if (target == WorldActionRig.Player)
        {
            var needing = Worn.Where(g => ManaOf(g) < MaxManaOf(g)).ToList();
            given = 0;
            while (left > 0)
            {
                var now = needing.Where(g => ManaOf(g) < MaxManaOf(g)).ToList();
                if (now.Count == 0)
                {
                    break;
                }

                var ration = Math.Max(left / now.Count, 1);
                foreach (var g in now)
                {
                    var add = Math.Min(ration, MaxManaOf(g)!.Value - ManaOf(g)!.Value);
                    left -= add;
                    given += add;
                    _mana[g] = ManaOf(g) + add;
                    if (left <= 0)
                    {
                        break;
                    }
                }
            }

            var more = needing.Sum(g => MaxManaOf(g)!.Value - ManaOf(g)!.Value);
            R.H.ServerMessage(string.Create(CultureInfo.InvariantCulture, $"The Mana Stone gives {given:N0} points of mana to the following items: {string.Join(", ", needing.Select(g => _items[g].Kind.Name))}.{(more > 0 ? "\nYou need " + more.ToString("N0", CultureInfo.InvariantCulture) + " more mana to fully charge your items." : "\nYour items are fully charged.")}"), 0);
        }
        else
        {
            given = Math.Min(MaxManaOf(target)!.Value - ManaOf(target)!.Value, left);
            _mana[target] = ManaOf(target) + given;
            R.H.ServerMessage(string.Create(CultureInfo.InvariantCulture, $"The Mana Stone gives {given:N0} points of mana to the {_items[target].Kind.Name}."), 0);
        }

        _items.Remove(stone);
        R.Raw(AceM3Writers.InventoryRemoveObject(stone));
        R.H.ServerMessage("The Mana Stone is destroyed.", 0);
        R.UseDone();
    }

    private sealed class RigView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }
}

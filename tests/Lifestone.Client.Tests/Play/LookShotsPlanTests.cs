using System.Collections.Immutable;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// <see cref="LookShotsPlan"/> (M4 4.1 lane v0, D27; <c>--play-look-shots=DIR</c>): the steps made from a scripted inventory (a mace, a bow with arrows and an orb), the order of the operations of each step (peace, the hands stowed, the
/// wield, frame A, the default mode, frame B, peace, the stow, frame C), the put-back of the start state, the kinds read from a description (and not guessed), the look line and the option. The Godot half (the camera, the shots, the avatar facts)
/// is proved by the offline run's frames and lines (<c>cache/m4/4-1/look-offline</c>) and live by the lead.
/// </summary>
public sealed class LookShotsPlanTests
{
    private const uint Mace = 0x80001001;
    private const uint Bow = 0x80001002;
    private const uint Arrows = 0x80001003;
    private const uint Orb = 0x80001004;
    private const uint Sword = 0x80001005;
    private const uint Mystery = 0x80001006;

    private static LookRig Full() => LookRig.Make(
    [
        new(Mace, "Mace", 11, 0x1, 1, EquipMask.MeleeWeapon),
        new(Bow, "Shortbow", 12, 0x100, 2, EquipMask.MissileWeapon),
        new(Arrows, "Arrow", 13, 0x100, 3, EquipMask.MissileAmmo),
        new(Orb, "Healer's Heart", 8552, 0x8000, null, EquipMask.Held),
    ]);

    [Fact]
    public void A_mace_a_bow_with_arrows_and_an_orb_in_the_pack_make_three_steps_in_the_order_melee_missile_caster()
    {
        var plan = LookShotsPlan.Build(Full().State);

        Assert.Equal(["melee", "missile", "caster"], plan.Steps.Select(s => s.Name));
        Assert.Equal([LookKind.Melee, LookKind.Launcher, LookKind.Caster], plan.Steps.Select(s => s.Kind));
        Assert.Equal(Mace, plan.Steps[0].Item.Guid);
        Assert.Equal((uint)EquipMask.MeleeWeapon, plan.Steps[0].Item.Slot);
        Assert.Equal(Bow, plan.Steps[1].Item.Guid);
        Assert.Equal(Arrows, plan.Steps[1].Ammo?.Guid);
        Assert.Equal((uint)EquipMask.MissileAmmo, plan.Steps[1].Ammo?.Slot);
        Assert.Equal(Orb, plan.Steps[2].Item.Guid);
        Assert.Equal(8552u, plan.Steps[2].Item.Wcid);
        Assert.Equal((uint)EquipMask.Held, plan.Steps[2].Item.Slot);
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.StartHands);
    }

    [Fact]
    public void Each_step_is_peace_the_hands_stowed_the_wield_frame_a_the_default_mode_frame_b_peace_the_stow_and_frame_c()
    {
        var plan = LookShotsPlan.Build(Full().State);

        // The missile step: nothing is in the hands, so no stow comes before the wield; the ammunition is wielded after the bow and stowed before it.
        var missile = plan.Ops.Where(o => o.Step == 1).Select(o => (o.Kind, o.Item?.Guid ?? 0, o.Frame)).ToArray();
        Assert.Equal(
            [
                (LookOpKind.Peace, 0u, ' '),
                (LookOpKind.Wield, Bow, ' '),
                (LookOpKind.Wield, Arrows, ' '),
                (LookOpKind.Frame, 0u, 'a'),
                (LookOpKind.EnterDefaultMode, 0u, ' '),
                (LookOpKind.Frame, 0u, 'b'),
                (LookOpKind.Peace, 0u, ' '),
                (LookOpKind.Stow, Arrows, ' '),
                (LookOpKind.Stow, Bow, ' '),
                (LookOpKind.Frame, 0u, 'c'),
            ],
            missile);

        // Three frames per step, a, b and c, and the files they are saved to.
        var frames = plan.Ops.Where(o => o.Kind == LookOpKind.Frame).Select(o => LookShotsPlan.FileName(plan.Steps[o.Step].Name, o.Frame)).ToArray();
        Assert.Equal(["melee-a.png", "melee-b.png", "melee-c.png", "missile-a.png", "missile-b.png", "missile-c.png", "caster-a.png", "caster-b.png", "caster-c.png"], frames);
    }

    [Fact]
    public void What_the_hands_hold_at_the_start_is_stowed_before_the_first_wield_and_put_back_at_the_end_in_the_same_slots()
    {
        // The character starts with the mace wielded (the way a fighter does), the rest in the pack.
        var rig = LookRig.Make(
        [
            new(Mace, "Mace", 11, 0x1, 1, EquipMask.MeleeWeapon, Wielded: true),
            new(Bow, "Shortbow", 12, 0x100, 2, EquipMask.MissileWeapon),
            new(Arrows, "Arrow", 13, 0x100, 3, EquipMask.MissileAmmo),
            new(Orb, "Healer's Heart", 8552, 0x8000, null, EquipMask.Held),
        ]);
        var plan = LookShotsPlan.Build(rig.State);

        Assert.Equal([Mace], plan.StartHands.Select(h => h.Guid));
        Assert.Equal((uint)EquipMask.MeleeWeapon, plan.StartHands[0].Slot);
        Assert.Equal(["melee", "missile", "caster"], plan.Steps.Select(s => s.Name)); // the wielded mace is a step too
        var first = plan.Ops.Where(o => o.Step == 0).Take(4).Select(o => (o.Kind, o.Item?.Guid ?? 0)).ToArray();
        Assert.Equal([(LookOpKind.Peace, 0u), (LookOpKind.Stow, Mace), (LookOpKind.Wield, Mace), (LookOpKind.Frame, 0u)], first); // stowed first, then wielded again: a real wield
        Assert.DoesNotContain(plan.Ops.Where(o => o.Step == 1), o => o.Kind == LookOpKind.Stow && o.Item?.Guid == Mace); // the hands are empty by the second step

        var tail = plan.Ops.Where(o => o.Step == -1).Select(o => (o.Kind, o.Item?.Guid ?? 0, o.Item?.Slot ?? 0)).ToArray();
        Assert.Equal([(LookOpKind.Peace, 0u, 0u), (LookOpKind.Wield, Mace, (uint)EquipMask.MeleeWeapon)], tail);
        Assert.Equal(LookOpKind.Wield, plan.Ops[^1].Kind); // no RestoreMode: the run began in peace
    }

    [Fact]
    public void A_bow_and_its_arrows_are_put_back_bow_first_and_a_combat_mode_the_run_began_in_is_put_back_last()
    {
        var rig = LookRig.Make(
            [
                new(Arrows, "Arrow", 13, 0x100, 3, EquipMask.MissileAmmo, Wielded: true),
                new(Bow, "Shortbow", 12, 0x100, 2, EquipMask.MissileWeapon, Wielded: true),
            ],
            CombatModes.Missile);
        var plan = LookShotsPlan.Build(rig.State);

        Assert.Equal(CombatModes.Missile, (int)plan.StartMode);
        Assert.Equal([Bow, Arrows], plan.StartHands.Select(h => h.Guid)); // the ammunition last, whatever order the server listed the hands in
        Assert.Equal(LookOpKind.RestoreMode, plan.Ops[^1].Kind);
        Assert.Equal([LookOpKind.Peace, LookOpKind.Wield, LookOpKind.Wield, LookOpKind.RestoreMode], plan.Ops.Where(o => o.Step == -1).Select(o => o.Kind));
    }

    [Fact]
    public void A_character_with_nothing_to_wield_has_no_steps_and_no_operations()
    {
        var plan = LookShotsPlan.Build(LookRig.Make([new(Sword + 100, "Bandage", 99, 0x80, null, EquipMask.None)]).State);

        Assert.Empty(plan.Steps);
        Assert.Empty(plan.Ops);
    }

    [Fact]
    public void A_launcher_without_ammunition_is_a_step_without_arrows_and_the_plan_says_so()
    {
        var plan = LookShotsPlan.Build(LookRig.Make([new(Bow, "Shortbow", 12, 0x100, 2, EquipMask.MissileWeapon)]).State);

        var step = Assert.Single(plan.Steps);
        Assert.Null(step.Ammo);
        Assert.Single(plan.Ops, o => o.Kind == LookOpKind.Wield);
        Assert.Contains(plan.Notes, n => n.Contains("no ammunition", StringComparison.Ordinal));
    }

    [Fact]
    public void The_item_the_hands_hold_wins_over_the_pack_and_a_second_melee_weapon_is_no_step()
    {
        var rig = LookRig.Make(
        [
            new(Sword, "Sword", 21, 0x1, 1, EquipMask.MeleeWeapon),
            new(Mace, "Mace", 11, 0x1, 1, EquipMask.MeleeWeapon, Wielded: true),
        ]);
        var plan = LookShotsPlan.Build(rig.State);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(Mace, step.Item.Guid);
    }

    [Fact]
    public void A_kind_the_description_does_not_say_is_noted_and_never_guessed_unless_the_stand_in_names_it()
    {
        var rig = LookRig.Make([new(Mystery, "Mystery Club", LookShotsPlan.StandInMaceWcid, 0x1, null, EquipMask.None)]);

        var plain = LookShotsPlan.Build(rig.State);
        Assert.Empty(plain.Steps);
        var note = Assert.Single(plain.Notes);
        Assert.Contains("TODO(m4-look-kind-unknown)", note, StringComparison.Ordinal);
        Assert.Contains("Mystery Club", note, StringComparison.Ordinal);

        var hinted = LookShotsPlan.Build(rig.State, LookShotsPlan.StandInHints);
        var step = Assert.Single(hinted.Steps);
        Assert.Equal("melee", step.Name);
        Assert.Equal((uint)EquipMask.MeleeWeapon, step.Item.Slot);
        Assert.Empty(hinted.Notes);
    }

    [Fact]
    public void The_stand_in_grants_are_the_real_mace_shortbow_and_arrow_wcids_by_hint()
    {
        var rig = LookRig.Make(
        [
            new(Mace, "Mace", LookShotsPlan.StandInMaceWcid, 0x80, null, EquipMask.None),
            new(Bow, "Shortbow", LookShotsPlan.StandInBowWcid, 0x80, null, EquipMask.None),
            new(Arrows, "Arrow", LookShotsPlan.StandInArrowWcid, 0x80, null, EquipMask.None),
        ]);
        var plan = LookShotsPlan.Build(rig.State, LookShotsPlan.StandInHints);

        Assert.Equal(["melee", "missile"], plan.Steps.Select(s => s.Name));
        Assert.Equal(Arrows, plan.Steps[1].Ammo?.Guid);
        Assert.Empty(LookShotsPlan.Build(rig.State).Steps); // without the hints the placeholders say nothing about their kind
    }

    [Fact]
    public void An_item_from_the_pack_remembers_its_slot_for_the_stow_and_an_item_from_the_hands_has_none_M4_4_1_fix2b_R10()
    {
        var plan = LookShotsPlan.Build(Full().State);

        // The pack order is the PlayerDescription's: Mace 0, Bow 1, Arrows 2, Orb 3 (all in the main pack).
        Assert.Equal([(0u, 0u), (0u, 1u), (0u, 2u), (0u, 3u)], new[] { plan.Steps[0].Item, plan.Steps[1].Item, plan.Steps[1].Ammo!.Value, plan.Steps[2].Item }.Select(i => (i.HomeContainer, i.HomePlacement)));

        var hands = LookShotsPlan.Build(LookRig.Make([new(Mace, "Mace", 11, 0x1, 1, EquipMask.MeleeWeapon, Wielded: true), new(Bow, "Shortbow", 12, 0x100, 2, EquipMask.MissileWeapon)]).State);
        Assert.Equal((0u, 0u), (hands.StartHands.Single().HomeContainer, hands.StartHands.Single().HomePlacement));
        Assert.Equal((0u, 0u), (hands.Steps[0].Item.HomeContainer, hands.Steps[0].Item.HomePlacement)); // the mace was in the hand: the old rule, slot 0 of the main pack
        Assert.Equal(0u, hands.Steps[1].Item.HomePlacement); // the bow is the only pack item left: slot 0
    }

    [Fact]
    public void The_look_run_stows_to_the_items_home_and_the_option_text_is_gone_M4_4_1_fix2b()
    {
        var run = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "PlayMode.LookShots.cs"));
        Assert.Contains("MoveToPack(held.Guid, held.HomeContainer, held.HomePlacement", run, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveToPack(held.Guid, 0, 0", run, StringComparison.Ordinal);
        Assert.Equal((331u, 307u, 300u), (LookShotsPlan.StandInMaceWcid, LookShotsPlan.StandInBowWcid, LookShotsPlan.StandInArrowWcid)); // ACE's Mace, Shortbow and Arrow (the art table's real Setups)
    }

    [Fact]
    public void A_look_line_has_the_fields_in_order_and_prints_unknown_for_a_field_it_could_not_read()
    {
        var line = LookShotsPlan.Line(new LookFacts("Healer's Heart", 8552, 'b', "ok", true, "off", 3, "Magic(0x0049)", "Magic"));
        Assert.Equal("look item=Healer's Heart wcid=8552 frame=b parent=ok drawn=yes light=off particles=3 stance=Magic(0x0049) mode=Magic", line);

        var unknown = LookShotsPlan.Line(new LookFacts("Shortbow", 12, 'a', "failed(1)", null, null, null, null, "Missile", Ammo: "Arrow(parent=ok,drawn=no)"));
        Assert.Equal("look item=Shortbow wcid=12 frame=a parent=failed(1) drawn=unknown light=unknown particles=unknown stance=unknown mode=Missile ammo=Arrow(parent=ok,drawn=no)", unknown);
    }

    [Fact]
    public void The_parent_phrase_reads_the_avatar_set_s_notice_and_the_names_of_modes_and_stances_are_retail_s()
    {
        Assert.Equal("ok", LookShotsPlan.ParentPhrase(true, null));
        Assert.Equal("none", LookShotsPlan.ParentPhrase(false, null));
        Assert.Equal("failed(1)", LookShotsPlan.ParentPhrase(false, "parent 0x50000001 has no holding location 1"));
        Assert.Equal("failed(?)", LookShotsPlan.ParentPhrase(false, "something else"));

        Assert.Equal("NonCombat", LookShotsPlan.ModeName(0));
        Assert.Equal("NonCombat", LookShotsPlan.ModeName(CombatModes.NonCombat));
        Assert.Equal("Melee", LookShotsPlan.ModeName(CombatModes.Melee));
        Assert.Equal("Missile", LookShotsPlan.ModeName(CombatModes.Missile));
        Assert.Equal("Magic", LookShotsPlan.ModeName(CombatModes.Magic));
        Assert.Equal("HandCombat(0x003C)", LookShotsPlan.StanceName(0x003C));
        Assert.Equal("Magic(0x0049)", LookShotsPlan.StanceName(0x0049));
    }

    [Fact]
    public void The_option_is_registered_parsed_and_a_missing_folder_is_an_error()
    {
        Assert.Equal("cache/x", PlayOptions.Parse(["--play-look-shots=cache/x"]).LookShotsDirectory);
        Assert.Null(PlayOptions.Parse([]).LookShotsDirectory);
        Assert.NotEmpty(PlayOptions.Parse(["--play-look-shots"]).Errors);
        Assert.Contains(OptionCatalog.Play().Options, o => o.Name == "--play-look-shots");
        Assert.Equal("look shots done frames=9 failures=0 steps=3", LookShotsPlan.Verdict(9, 0, 3));
    }
}

/// <summary>A session read model with the player and a scripted pack, for <see cref="LookShotsPlanTests"/> (named after the lane: no other suite shares it).</summary>
internal sealed class LookRig
{
    /// <summary>One scripted item: its ids, the weenie description as ACE sends it, and where it is.</summary>
    /// <param name="Guid">The object id.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Wcid">The weenie class id.</param>
    /// <param name="ItemType">The ItemType.</param>
    /// <param name="CombatUse">The CombatUse, or null for none.</param>
    /// <param name="Valid">The ValidLocations (None: the description sends none).</param>
    /// <param name="Wielded">True when the item starts wielded at <paramref name="Valid"/>; false when it is in the pack.</param>
    internal readonly record struct Piece(uint Guid, string Name, uint Wcid, uint ItemType, sbyte? CombatUse, EquipMask Valid, bool Wielded = false);

    private LookRig(GameState state) => State = state;

    /// <summary>The read model.</summary>
    public GameState State { get; }

    /// <summary>Makes the rig: the player in the world, the PlayerDescription with the pack and the wielded list, each item's CreateObject.</summary>
    public static LookRig Make(Piece[] pieces, int? combatMode = null)
    {
        var harness = new ReducerHarness(track: false);
        harness.EnterWorld();
        var description = new PlayerDescriptionMsg
        {
            Inventory = [.. pieces.Where(p => !p.Wielded).Select(p => new ContentDesc(p.Guid, 0))],
            Equipped = [.. pieces.Where(p => p.Wielded).Select(p => new EquippedDesc(p.Guid, (uint)p.Valid, 0))],
            IntProperties = combatMode is { } mode ? ImmutableDictionary<uint, int>.Empty.Add(CombatModes.Property, mode) : ImmutableDictionary<uint, int>.Empty,
        };
        harness.Reducer.OnPlayerDescription(harness.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription), description);
        foreach (var piece in pieces)
        {
            var basis = ReducerHarness.Create(piece.Guid, ReducerHarness.Stamps(), null, name: piece.Name, itemType: piece.ItemType);
            var header = basis.Weenie.Header;
            var weenie = basis.Weenie with
            {
                WeenieClassId = piece.Wcid,
                CombatUse = piece.CombatUse,
                Header = piece.CombatUse is null ? header : header | WeenieHeaderFlags.CombatUse,
                ValidLocations = piece.Valid == EquipMask.None ? null : (uint)piece.Valid,
            };
            harness.Reducer.OnCreateObject(harness.Context, basis with { Weenie = weenie }, isUpdate: false);
        }

        return new LookRig(harness.State);
    }
}

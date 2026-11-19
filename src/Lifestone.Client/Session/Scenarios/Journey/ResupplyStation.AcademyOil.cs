using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 sprint (owner ruling 2026-11-19: the Training Wand is upgraded with the oil she carries from the start; until now the pack rules tried to drop the oil as worthless): the Oil of Rendering (weenie 12711, "Use this oil on a Training
/// Weapon to create an Academy Weapon", its Use text) used on the Training Wand (12748) makes the Academy Wand (12759). The recipe is the world database's (read-only SELECT of ace_world.recipe joined to cook_book: source 12711, target
/// 12748, recipe 2470, success 12759, skill 0, difficulty 0: it cannot fail; source and target each destroyed on success). The Academy Wand carries Focus Other I, Willpower Other I and Mana Conversion Mastery Other I (each +10;
/// ace_world.weenie_properties_spell_book and spell.stat_Mod_Val), 400 item mana (PropertyInt 107/108), ItemDifficulty 15 (109), Bonded and IsSellable 0; the Training Wand carries no spell.
/// <para>ACE's craft (<c>RecipeManager.UseObjectOnTarget</c> @95ef426d): refused while busy or out of peace (<c>allow_combat_mode_crafting</c> off), no confirmation dialog for a recipe with no difficulty; the comment at its
/// <c>UpdateObj</c> notes retail likely wanted sources and targets in the pack, not equipped, so a wielded Training Wand goes to the pack first. Check before (the oil and the wand held, peace), act (the use), check after (the
/// Academy Wand held, the Training Wand gone). The caster pick that follows (<c>SwapBestCaster</c>) wields the best caster held, the Academy Wand among them.</para>
/// </summary>
public sealed partial class ResupplyRun
{
    /// <summary>The Oil of Rendering's weenie class (ace_world weenie 12711, class oilacademy).</summary>
    public const uint OilOfRenderingWcid = 12711;

    /// <summary>The Training Wand's weenie class (12748, class wandtraining): the oil's target for a mage (cook_book source 12711, target 12748).</summary>
    public const uint TrainingWandWcid = 12748;

    /// <summary>The Academy Wand's weenie class (12759, class wandacademy): recipe 2470's success_W_C_I_D.</summary>
    public const uint AcademyWandWcid = 12759;

    /// <summary>The wait for the Academy Wand to show in the pack after the use (Lifestone's own: the craft's ClapHands motion and the created object's 0xF745).</summary>
    private const double AcademyArrivalSeconds = 5;

    /// <summary>True once the oil was used on the wand this trip (or found refused): the step runs once a trip.</summary>
    private bool _academyTried;

    /// <summary>The upgrade (see the class remarks); nothing when she holds no oil or no Training Wand.</summary>
    private IEnumerable<ActionHandle?> AcademyOil()
    {
        if (_academyTried)
        {
            yield break;
        }

        var held = new List<uint>();
        GearInventory.Collect(S.State, held);
        uint oil = 0, wand = 0;
        foreach (var guid in held)
        {
            if (!S.State.Objects.TryGet(guid, out var o))
            {
                continue;
            }

            oil = oil == 0 && o.Weenie.WeenieClassId == OilOfRenderingWcid ? guid : oil;
            wand = wand == 0 && o.Weenie.WeenieClassId == TrainingWandWcid ? guid : wand;
        }

        if (oil == 0 || wand == 0)
        {
            yield break;
        }

        _academyTried = true;
        if (InCombatMode())
        {
            var peace = S.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
            yield return peace;
            if (InCombatMode())
            {
                Log(Text($"gear: academy oil: no use: the combat mode is {S.State.Self.Qualities.CombatMode}, not peace, after asking peace (ACE refuses a craft out of peace): the next visit tries again"));
                _academyTried = false;
                yield break;
            }
        }

        Log(Text($"gear: academy oil: check before: Oil of Rendering 0x{oil:X8} and the Training Wand 0x{wand:X8} held, peace: the oil makes it an Academy Wand (recipe 2470, no skill, no difficulty: Focus, Willpower and Mana Conversion Mastery I, 400 mana) (m6-academy-oil)"));
        if (S.State.Self.Inventory.Find(wand).Kind == InventoryPlaceKind.Equipped)
        {
            var off = S.Actions.MoveToPack(wand, S.State.Self.Guid, 0, ActionSource.Automation);
            yield return off;
            if (S.State.Self.Inventory.Find(wand).Kind == InventoryPlaceKind.Equipped)
            {
                Log(Text($"gear: academy oil: the Training Wand stays wielded ({Describe(off)}): no use this visit (ACE's craft wants it in the pack)"));
                _academyTried = false;
                yield break;
            }
        }

        var use = S.Actions.UseWithTarget(oil, wand, ActionSource.Automation);
        yield return use;
        foreach (var h in Until(() => FirstHeld(AcademyWandWcid) != 0, AcademyArrivalSeconds)) { yield return h; }

        var academy = FirstHeld(AcademyWandWcid);
        Log(academy != 0
            ? Text($"gear: academy oil: check after: the Academy Wand 0x{academy:X8} is in the pack and the Training Wand {(S.State.Self.Inventory.Contains(wand) ? "is still held (TODO(m6-academy-oil-target): ACE should have destroyed it)" : "is gone")} ({Describe(use)}); the caster pick next wields the best caster held (m6-academy-oil)")
            : Text($"gear: academy oil: check after: no Academy Wand in the pack {AcademyArrivalSeconds:0} s after the use ({Describe(use)}); the Training Wand {(S.State.Self.Inventory.Contains(wand) ? "is still held" : "is gone")}: TODO(m6-academy-oil-failed): not tried again this trip (m6-academy-oil)"));
    }

    /// <summary>A held item of weenie class <paramref name="wcid"/>, or 0 (<see cref="ScriptLeg.HeldOf"/>'s first).</summary>
    private uint FirstHeld(uint wcid) => HeldOf(wcid).FirstOrDefault();
}

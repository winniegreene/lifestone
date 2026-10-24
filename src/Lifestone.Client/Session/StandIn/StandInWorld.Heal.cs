// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Healer.cs (HandleActionUseOnTarget 49-127, DoHealMotion 129-170, DoHealing 172-237, DoSkillCheck 240-257, GetHealAmount 260-297),
// Source/ACE.Server/WorldObjects/Food.cs (the potion's chain, 42-105), Source/ACE.Server/Entity/Proficiency.cs (the skill-use experience, 16-109) and Source/ACE.Server/Entity/SkillCheck.cs (GetSkillChance) (AGPL-3.0)
// [with changes: the order and the lengths are the ones the live capture records (cache/m4/3-1/capture.md C1 to C3 and capture1.log); the stand-in keeps the kit's uses and the potion's stack itself, the player's
// Healing is a placeholder, and there is no heal rating, no critical-heal enchantment and no second player].
using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>What the stand-in's player holds for the heal checks, announced in a login burst (<see cref="StandInWorld.HealItems"/>): the kit and the potions' CreateObjects and their entries in the description's pack list.</summary>
/// <param name="Listed">The description's pack entries (the kit, then the potions).</param>
/// <param name="Objects">The CreateObjects of those items (container = the player).</param>
internal readonly record struct HealItemBurst(ImmutableArray<ContentDesc> Listed, ImmutableArray<CreateObjectMsg> Objects);

// M4 3.1 s2: the stand-in's heal (TODO(m4-standin-heal), closed for a visit that asks: StandInVisit.Heals or HealItems): a UseWithTarget 0x0035 of a held Healing Kit on the player's own guid and a Use 0x0036 of a held Potion of Healing, in the order and with the lengths of the
// live capture (capture.md C1 to C3), the roll by ACE's formula from a seeded draw, and the refusals of spec 5.7.
internal sealed partial class StandInWorld
{
    /// <summary>The weenie class id of the Handy Healing Kit (Healer, ItemType 0x80, 20 uses, BoostValue 50; the world database).</summary>
    internal const uint HandyKitWcid = 628;

    /// <summary>The weenie class id of the Potion of Healing (Food, ItemType 0x80, stack limit 100, BoostValue 25).</summary>
    internal const uint HealingPotionWcid = 377;

    /// <summary>The name of the Handy Healing Kit and of the Potion of Healing (the announced items; a held item of the same class id keeps the name it came with).</summary>
    internal const string HandyKitName = "Handy Healing Kit", HealingPotionName = "Potion of Healing";

    /// <summary>The guid of the kit the player is announced with, and of its stack of potions (the stand-in's own range).</summary>
    internal const uint HealKitGuid = 0x80070000, HealPotionGuid = 0x80070001;

    /// <summary>The Handy Healing Kit: its uses (Structure and MaxStructure), BoostValue, burden and Value (the world database; the burden is read from the SELECT, not measured).</summary>
    internal const int HandyKitUses = 20, HandyKitBoost = 50, HandyKitBurden = 50, HandyKitValue = 10;

    /// <summary>The Potion of Healing: the health a drink restores (BoostValue), the burden and Value of one, the stack limit, and the stack the player is announced with.</summary>
    internal const int PotionBoost = 25, PotionBurden = 15, PotionValue = 170, PotionMaxStack = 100, PotionStackAtLogin = 5;

    /// <summary>The ItemType of a kit and of a potion (<c>Misc</c>, 0x80: the SELECT).</summary>
    internal const uint HealItemType = 0x80;

    /// <summary>The motion the kit's use plays on the player (<c>SkillHealSelf</c>, 11.6096 command 0x1000010E): capture C1, F74C with the Ready forward and this command.</summary>
    internal const uint SkillHealSelfMotion = 0x1000010E;

    /// <summary>The motion a drink plays (<c>Drink</c>, 0x4000001B): capture C3, F74C with this forward and no command.</summary>
    internal const uint DrinkMotion = 0x4000001B;

    /// <summary>How long after the heal's F74C the effect and the UseDone come (capture C1: the use costs 1.47 to 1.48 s of busy; the F74C itself comes 10 ms after the send).</summary>
    internal const double HealMotionSeconds = 1.47;

    /// <summary>How long after the heal's UseDone the Ready F74C follows (capture C1: 10 to 30 ms; ACE is busy until it).</summary>
    internal const double HealReadyLagSeconds = 0.03;

    /// <summary>How long after the drink's F74C the effect comes (capture C3: +0.74 s from the send).</summary>
    internal const double DrinkEffectSeconds = 0.73;

    /// <summary>How long after the drink's effect the sound and the Ready F74C follow (capture C3: 6 ms and 16 ms; rounded to a tick).</summary>
    internal const double DrinkSoundLagSeconds = 0.01, DrinkReadyLagSeconds = 0.02;

    /// <summary>How long after the drink's F74C the UseDone comes (capture C3: +1.48 s from the send: the sum of the chain's lengths).</summary>
    internal const double DrinkDoneSeconds = 1.47;

    /// <summary>The sound the drink plays on the player (capture C3: F750 id 0x41).</summary>
    internal const uint SoundDrink = 0x41;

    /// <summary>ACE's <c>WeenieError.YouArentTrainedInHealing</c> (0x04FC), the UseDone of a kit used by a character whose Healing is below trained (capture C4).</summary>
    internal const uint NotTrainedInHealing = 0x04FC;

    /// <summary>ACE's <c>WeenieError.YouCantHealThat</c> (0x04FE): the UseDone of a kit used on a target that is not a player.</summary>
    internal const uint CantHealThat = 0x04FE;

    /// <summary>ACE's <c>WeenieErrorWithString._IsAtFullHealth</c> (0x04FF): the 028B of a kit used at a full health, with the target's name.</summary>
    internal const uint IsAtFullHealth = 0x04FF;

    /// <summary>ACE's Structure (<c>PropertyInt.Structure</c> 92): the uses a kit has left.</summary>
    private const uint StructureProperty = 92;

    /// <summary>The Healing skill's id.</summary>
    internal const uint HealingSkillId = 21;

    /// <summary>
    /// The Healing skill the stand-in's player heals with (a placeholder, <c>TODO(m4-standin-heal-skill)</c>: the stand-in's description carries no skill values). 190 makes the Handy Healing Kit's effective skill 264,
    /// the Holtburg Hunter's number in the capture ((190 + 50) x 1.1).
    /// </summary>
    internal const int PlaceholderHealingSkill = 190;

    /// <summary>The seed of the generator the heal's rolls draw from (ACE rolls with <c>ThreadSafeRandom</c>; the stand-in's is seeded, so a run repeats and a test pins the sequence).</summary>
    internal const int HealSeed = 0x4EA1_0001;

    /// <summary>ACE's chance for a critical heal (<c>Healer.GetHealAmount</c> 272) and the band's ends as shares of the skill times the kit's mod (<c>healMin = 0.2</c>, <c>healMax = 0.5</c>, "todo: determine applicable range from pcaps").</summary>
    internal const float CriticalHealChance = 0.1f, HealBandLow = 0.2f, HealBandHigh = 0.5f;

    /// <summary>ACE's skill-use experience: a gain needs a rise in difficulty or this many seconds since the last one (<c>Proficiency.cs</c> 14).</summary>
    internal const double ProficiencyRepeatSeconds = 900.0;

    private readonly Dictionary<uint, int> _kitUses = [];
    private double? _healXpAt;
    private int _healXpDifficulty;
    private ushort _healActionStamp;

    /// <summary>The generator the heal's rolls draw from: the roll first, then (on a win) the amount and the critical. A test sets its own to pin a draw.</summary>
    internal Random HealRandom { get; set; } = new(HealSeed);

    /// <summary>
    /// False makes the player's Healing untrained: a kit use ends UseDone 0x04FC (capture C4; BSD Mage). True (the default) leaves it to the description: a Healing skill the description lists below trained also refuses
    /// (<c>TODO(m4-standin-heal-skill)</c>; with no Healing entry the stand-in's player is trained).
    /// </summary>
    internal bool HealingTrained { get; set; } = true;

    /// <summary>Test lever: the uses the kit <paramref name="kit"/> has left, as if earlier uses had spent it (no message goes out).</summary>
    internal void SetKitUses(uint kit, int uses) => _kitUses[kit] = uses;

    /// <summary>The uses the kit <paramref name="kit"/> has left as the stand-in knows them (its own count, then the Structure the model holds, then a new kit's twenty).</summary>
    internal int KitUsesLeft(uint kit) =>
        _kitUses.TryGetValue(kit, out var uses) ? uses : _reducer.State.Objects.TryGet(kit, out var o) && o.Weenie.Structure is { } structure ? structure : HandyKitUses;

    /// <summary>
    /// The kit and the potions the stand-in's player is announced with when the visit asks (<see cref="StandInVisit.HealItems"/>, <c>TODO(m4-standin-heal-items)</c>): a Handy Healing Kit (20 uses) and five Potions of Healing in
    /// the main pack, as the hunter carries them. The class ids, names, burdens, values and uses are the world database's; the art (Setup and icon) is the weenies' own from <see cref="StandInWeenieArt"/>.
    /// </summary>
    /// <param name="player">The stand-in player's guid.</param>
    internal static HealItemBurst HealItems(uint player)
    {
        var kitPhysics = new PhysicsDesc { Flags = PhysicsDescFlags.CSetup, State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal, SetupId = StandInWeenieArt.SetupOr(HandyKitWcid, PlaceholderItemSetup), Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        var potionPhysics = kitPhysics with { SetupId = StandInWeenieArt.SetupOr(HealingPotionWcid, PlaceholderItemSetup) };
        var kit = new CreateObjectMsg(
            HealKitGuid,
            ModelData.Empty,
            kitPhysics,
            StandInWeenieArt.Apply(new WeenieDesc
            {
                Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | WeenieHeaderFlags.Burden | WeenieHeaderFlags.Structure | WeenieHeaderFlags.MaxStructure,
                Name = HandyKitName,
                WeenieClassId = HandyKitWcid,
                IconId = StandInWeenieArt.PlaceholderIcon,
                ItemType = HealItemType,
                ObjectDescriptionFlags = 0,
                ContainerId = player,
                Value = HandyKitValue,
                Burden = HandyKitBurden,
                Structure = HandyKitUses,
                MaxStructure = HandyKitUses,
            }, null));
        var potions = new CreateObjectMsg(
            HealPotionGuid,
            ModelData.Empty,
            potionPhysics,
            StandInWeenieArt.Apply(new WeenieDesc
            {
                Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | WeenieHeaderFlags.Burden | WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize,
                Name = HealingPotionName,
                WeenieClassId = HealingPotionWcid,
                IconId = StandInWeenieArt.PlaceholderIcon,
                ItemType = HealItemType,
                ObjectDescriptionFlags = 0,
                ContainerId = player,
                Value = (uint)(PotionValue * PotionStackAtLogin),
                Burden = (ushort)(PotionBurden * PotionStackAtLogin),
                StackSize = PotionStackAtLogin,
                MaxStackSize = PotionMaxStack,
            }, null));
        return new HealItemBurst([new ContentDesc(HealKitGuid, 0), new ContentDesc(HealPotionGuid, 0)], [kit, potions]);
    }

    /// <summary>The text of the placeholder the announced items are (logged once at the login burst).</summary>
    internal const string HealItemsPlaceholder = "the player is announced with a Handy Healing Kit (20 uses) and five Potions of Healing in its main pack (the visit's HealItems): ids, names, values, burdens and art (Setup, icon) are the world database's";

    // --- the rules ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>ACE's <c>SkillCheck.GetSkillChance</c> for the heal: <c>1 / (1 + e^(-0.03 (effective - difficulty)))</c>.</summary>
    internal static double HealChance(int effective, int difficulty) => 1.0 / (1.0 + Math.Exp(-0.03 * (effective - difficulty)));

    /// <summary>ACE's effective skill (<c>Healer.DoSkillCheck</c> 250): the player's Healing plus the kit's BoostValue, times 1.5 for a specialised skill and 1.1 otherwise.</summary>
    internal static int HealEffective(int healing, int boost, bool specialized) => (int)Math.Round((healing + boost) * (specialized ? 1.5f : 1.1f));

    /// <summary>ACE's difficulty (<c>Healer.DoSkillCheck</c> 251): twice the missing vital, times 1.1 in a combat mode and 1.0 out of one.</summary>
    internal static int HealDifficulty(uint missing, bool combat) => (int)Math.Round(missing * 2 * (combat ? 1.1f : 1.0f));

    /// <summary>True when the visit asks for the heals (<see cref="StandInVisit.Heals"/>, or <see cref="StandInVisit.HealItems"/>, which implies them).</summary>
    internal bool HealRules => _visit.Heals || _visit.HealItems;

    private bool TryVital(uint vital, out uint current, out uint max) => _reducer.State.Self.Qualities.TryGetVitalLevels(vital - 1, out current, out max);

    private bool InCombatMode => _reducer.State.Self.Qualities.CombatMode is not (0 or CombatModes.NonCombat);

    private bool SkillIsTrained(out bool specialized)
    {
        specialized = false;
        if (!HealingTrained)
        {
            return false;
        }

        if (_reducer.State.Self.Qualities.TryGetSkill(HealingSkillId, out var skill))
        {
            specialized = skill.AdvancementClass >= SpecializedClass;
            return skill.AdvancementClass >= TrainedClass;
        }

        return true;
    }

    // --- the kit ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A UseWithTarget (0x0035; <c>Healer.HandleActionUseOnTarget</c> 49-127, <c>Player_Use.cs</c> 29-170). Decided when it is sent (the stand-in's busy window is measured from the send, as the item moves'), in ACE's order: a kit
    /// that is not held, an item that is not a kit or a target the world does not know is a silent UseDone 0; Healing below trained is UseDone 0x04FC; busy is UseDone 0x001D (no 028A); a target that is not the player is UseDone
    /// 0x04FE; a full health is 028B {0x04FF, the name} then UseDone 0 (<c>TODO(m4-heal-full-vital-live)</c>: not captured). Otherwise the heal starts, answered at the stand-in's delay: F74C with SkillHealSelf, and 1.47 s
    /// later the heal and the UseDone, the Ready F74C 30 ms after (capture C1); the stand-in is busy until that Ready.
    /// </summary>
    internal OutboundTicket UseWithTarget(uint item, uint target)
    {
        if (!HealRules)
        {
            throw new NotSupportedException("the stand-in answers a UseWithTarget only for a visit that asks for the heals (StandInVisit.Heals or HealItems): TODO(m4-standin-heal)");
        }

        var self = _reducer.State.Self;
        if (!InPack(item, out var kit) || kit.Wcid != HandyKitWcid || target == 0 || (target != self.Guid && !_reducer.State.Objects.Contains(target)))
        {
            Later(sink => UseDone(sink)); // Player_Use.cs 29-170: an item not held, or a missing target, is a bare UseDone; the stand-in has one kind of kit (TODO(m4-standin-heal-items))
            return Ticket();
        }

        if (!SkillIsTrained(out _))
        {
            return RefuseUse(NotTrainedInHealing); // Healer.cs 51-55; capture C4: +2 ms, nothing else
        }

        if (Busy)
        {
            return RefuseUse(YoureTooBusy); // Healer.cs 57-61; capture C2: +2 ms, no 028A and no 028B
        }

        if (target != self.Guid)
        {
            return RefuseUse(CantHealThat); // Healer.cs 63-67
        }

        if (!TryVital(Health, out var health, out var maxHealth) || health >= maxHealth)
        {
            Placeholder("m4-heal-full-vital-live", "a kit used at a full health answers 028B {0x04FF, the player's name} then UseDone 0 as ACE's Healer.cs 90-110 does; the live capture did not reach a full vital, so the order and the timing are the source's, not a recording's");
            Later(sink =>
            {
                AnswerLog.Add("028B");
                sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028B), new WeenieErrorEvent(IsAtFullHealth, self.Name ?? "You"));
                UseDone(sink);
            });
            return Ticket();
        }

        // DoHealMotion (Healer.cs 129-170): IsBusy, the motion, and the missing health read now; DoHealing runs when the motion ends.
        Placeholder("m4-standin-heal-stance", "the heal and the drink play in the player's current stance: ACE answers a combat mode with a stance change first (spec 5.7 for the drink; the capture heals out of combat)");
        var missing = maxHealth - health;
        var start = _reducer.Clock.Now + DelaySeconds;
        var done = start + HealMotionSeconds;
        var schedule = _reducer.StandIn!;
        _busyUntil = Math.Max(_busyUntil, done + HealReadyLagSeconds);
        schedule.At(start, SendHealMotion);
        schedule.At(done, later => DoHealing(later, item, missing));
        schedule.At(done + HealReadyLagSeconds, later => SendPlayerMotion(later, StanceNow(), MotionCommands.Ready));
        return Ticket();
    }

    private OutboundTicket RefuseUse(uint error)
    {
        Later(sink => UseDone(sink, error));
        return Ticket();
    }

    /// <summary>The kit's F74C (capture C1): the Ready forward and the SkillHealSelf command, in the player's stance.</summary>
    private void SendHealMotion(IAcSessionSink sink)
    {
        var guid = _reducer.State.Self.Guid;
        var stamps = _reducer.State.Objects.TryGet(guid, out var body) ? body.Stamps : default;
        var wire = MotionCommandTable.ToWire(StanceNow());
        _healActionStamp = (ushort)((_healActionStamp + 1) & 0x7FFF);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Commands = [new MotionItem(MotionCommandTable.ToWire(SkillHealSelfMotion), _healActionStamp, false, 1f)],
        };
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    /// <summary>
    /// <c>Healer.DoHealing</c> (172-237) then the UseDone (<c>DoHealMotion</c> 155-165): a use is spent first, win or lose; the roll is read against <paramref name="missing"/> (the health missing when the use began). A loss is the
    /// line and the Structure; a win is stamina, health, the proficiency's experience, the line and the Structure, then, with no use left, the kit's removal (0024 and the burden).
    /// </summary>
    private void DoHealing(IAcSessionSink sink, uint item, uint missing)
    {
        if (!InPack(item, out var kit))
        {
            UseDone(sink); // the kit went before the motion ended (nothing in the stand-in can do that while busy)
            return;
        }

        var uses = KitUsesLeft(item) - 1;
        _kitUses[item] = uses;
        var remaining = uses > 0 ? " Your " + kit.Name + " has " + uses + (uses == 1 ? " use" : " uses") + " left." : " Your " + kit.Name + " is used up.";
        Placeholder("m4-standin-heal-skill", "the stand-in's player heals with a Healing of " + PlaceholderHealingSkill + " (trained unless the description lists the skill below trained): its description carries no skill values");
        var specialized = SkillIsTrained(out var s) && s;
        var difficulty = HealDifficulty(missing, InCombatMode);
        var chance = HealChance(HealEffective(PlaceholderHealingSkill, HandyKitBoost, specialized), difficulty);
        if (!(chance > HealRandom.NextDouble()))
        {
            HealLine(sink, "You fail to heal yourself." + remaining);
            StructureNow(sink, item, uses);
            ConsumeIfSpent(sink, kit, uses, HandyKitBurden);
            UseDone(sink);
            return;
        }

        var amount = HealAmount(missing, out var critical, out var staminaCost);
        UpdateVitalDelta(sink, Stamina, -(int)staminaCost);
        UpdateVitalDelta(sink, Health, (int)amount);
        GrantHealExperience(sink, difficulty);
        HealLine(sink, "You " + (critical ? "expertly " : "") + "heal yourself for " + amount + " Health points." + remaining);
        StructureNow(sink, item, uses);
        ConsumeIfSpent(sink, kit, uses, HandyKitBurden);
        UseDone(sink);
    }

    /// <summary>
    /// <c>Healer.GetHealAmount</c> (260-297): a draw from 0.2 to 0.5 of the skill times the kit's mod (1.0), doubled on a critical, capped at the health missing, and capped again by the stamina (a fifth of the heal, or the stamina
    /// times five). The band is ACE's own ("todo: determine applicable range from pcaps"): <c>TODO(m4-standin-heal-band)</c> for the skill it is taken of.
    /// </summary>
    private uint HealAmount(uint missing, out bool critical, out uint staminaCost)
    {
        Placeholder("m4-standin-heal-band", "the heal amount is a draw from " + HealBandLow + " to " + HealBandHigh + " of the placeholder Healing skill (" + PlaceholderHealingSkill + ") times the kit's mod 1.0, ACE's own band (its source says the range is a guess): the capture saw 65 to 73 at a Healing near that");
        var healBase = PlaceholderHealingSkill * 1.0f;
        var low = healBase * HealBandLow;
        var high = healBase * HealBandHigh;
        var amount = low + ((float)HealRandom.NextDouble() * (high - low));
        critical = HealRandom.NextDouble() < CriticalHealChance;
        if (critical)
        {
            amount *= 2;
        }

        if (amount > missing)
        {
            amount = missing;
        }

        TryVital(Stamina, out var stamina, out _);
        staminaCost = (uint)MathF.Round(amount / 5.0f);
        if (staminaCost > stamina)
        {
            staminaCost = stamina;
            amount = staminaCost * 5;
        }

        return (uint)MathF.Round(amount);
    }

    /// <summary>
    /// <c>Proficiency.OnSuccessUse</c> (<c>Proficiency.cs</c> 16-109; cache/m4/3-1/xp-rule.md): a trained skill earns round(1.1 x round(difficulty x scale)) experience when the difficulty rose since the last gain or 15 minutes passed (the
    /// scale is 1.0 after 15 minutes, else the share of them elapsed). Sent as the available experience, as capture C1 shows its first 02CF. <c>TODO(m4-standin-heal-xp)</c>: ACE also sends the total and the available experience again
    /// 30 ms later with the Ready (the queued grant) and a skill update (02DD): not modelled.
    /// </summary>
    private void GrantHealExperience(IAcSessionSink sink, int difficulty)
    {
        var now = _reducer.Clock.Now;
        var elapsed = _healXpAt is { } last ? now - last : double.PositiveInfinity;
        if (difficulty <= _healXpDifficulty && elapsed < ProficiencyRepeatSeconds)
        {
            return;
        }

        var scale = elapsed >= ProficiencyRepeatSeconds ? 1.0 : elapsed / ProficiencyRepeatSeconds;
        _healXpDifficulty = difficulty;
        _healXpAt = now;
        var award = (long)Math.Round(1.1 * Math.Round(difficulty * scale));
        if (award <= 0)
        {
            return;
        }

        Placeholder("m4-standin-heal-xp", "a won heal sends one 02CF of the available experience with the award added; ACE's capture also sends the total and the available again with the Ready (the queued grant) and a skill update (02DD)");
        AnswerLog.Add("02CF");
        PrivateInt64(sink, PlayerQualities.AvailableExperienceProperty, _reducer.State.Self.Qualities.AvailableExperience + award);
    }

    private void HealLine(IAcSessionSink sink, string text)
    {
        AnswerLog.Add("F7E0");
        sink.OnServerMessage(Context(sink), new ServerMessageMsg(text, BroadcastChat));
    }

    private void StructureNow(IAcSessionSink sink, uint item, int uses)
    {
        AnswerLog.Add("02CE");
        sink.OnPublicUpdatePropertyInt(Context(sink), new PublicUpdatePropertyIntMsg(NextSequence(item, StructureProperty), item, StructureProperty, uses));
    }

    /// <summary>The last use: the kit leaves the pack (0024 {kit}) and the burden follows (02CD), as <c>TryConsumeFromInventoryWithNetworking</c> tells it.</summary>
    private void ConsumeIfSpent(IAcSessionSink sink, StandInPackEntry item, int uses, int burden)
    {
        if (uses > 0)
        {
            return;
        }

        RemoveEntry(item.Guid);
        _kitUses.Remove(item.Guid);
        AnswerLog.Add("0024");
        sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(item.Guid));
        BurdenNow(sink, -burden * item.Stack);
    }

    // --- the potion ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private bool TryPotion(uint guid, out StandInPackEntry potion) => InPack(guid, out potion) && potion.Wcid == HealingPotionWcid;

    /// <summary>
    /// A Use (0x0036) of a held Potion of Healing (<c>Food.cs</c> 42-105; capture C3): busy is the 028A 0x001D and a UseDone 0 (spec 5.7, not captured for a potion); otherwise F74C Drink at the answer, at +0.73 s the health (capped at the
    /// maximum), the line "The Potion of Healing restores N points of your Health." (N the actual rise, 0 at a full health: the potion is spent anyway), the stack message and the burden, then the sound and the Ready F74C, and the UseDone
    /// at +1.47 s. The stand-in stays busy until the UseDone (<c>TODO(m4-standin-potion-busy)</c>).
    /// </summary>
    private OutboundTicket Drink(StandInPackEntry potion)
    {
        var guid = potion.Guid;
        if (Busy)
        {
            Later(sink =>
            {
                AnswerLog.Add("028A");
                sink.OnWeenieError(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x028A), new WeenieErrorEvent(YoureTooBusy, null)); // Food.cs: the refusal is a WeenieError, then UseDone 0
                UseDone(sink);
            });
            return Ticket();
        }

        Placeholder("m4-standin-heal-stance", "the heal and the drink play in the player's current stance: ACE answers a combat mode with a stance change first (spec 5.7 for the drink; the capture heals out of combat)");
        Placeholder("m4-standin-potion-busy", "the stand-in is busy from a drink until its UseDone (1.47 s); the capture shows the Ready F74C at 0.76 s and does not say whether ACE refuses a use between the two");
        var start = _reducer.Clock.Now + DelaySeconds;
        var effect = start + DrinkEffectSeconds;
        var schedule = _reducer.StandIn!;
        _busyUntil = Math.Max(_busyUntil, start + DrinkDoneSeconds);
        schedule.At(start, sink => SendPlayerMotion(sink, StanceNow(), DrinkMotion));
        schedule.At(effect, sink => DrinkEffect(sink, guid));
        schedule.At(effect + DrinkSoundLagSeconds, sink => PlaySound(sink, _reducer.State.Self.Guid, SoundDrink, 1f));
        schedule.At(effect + DrinkReadyLagSeconds, sink => SendPlayerMotion(sink, StanceNow(), MotionCommands.Ready));
        schedule.At(start + DrinkDoneSeconds, UseDoneNow);
        return Ticket();
    }

    private void UseDoneNow(IAcSessionSink sink) => UseDone(sink);

    private void DrinkEffect(IAcSessionSink sink, uint guid)
    {
        if (!TryPotion(guid, out var potion))
        {
            return;
        }

        var change = UpdateVitalDelta(sink, Health, PotionBoost);
        AnswerLog.Add("F7E0");
        sink.OnServerMessage(Context(sink), new ServerMessageMsg("The " + potion.Name + " restores " + Math.Max(change, 0) + " points of your Health.", BroadcastChat));
        if (potion.Stack > 1)
        {
            SetEntry(potion with { Stack = potion.Stack - 1 });
            AnswerLog.Add("0197");
            sink.OnSetStackSize(Context(sink), new SetStackSizeMsg(NextSequence(guid, StackSizeTag), guid, (uint)(potion.Stack - 1), (uint)(PotionValue * (potion.Stack - 1))));
        }
        else
        {
            RemoveEntry(guid);
            AnswerLog.Add("0024");
            sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(guid));
        }

        BurdenNow(sink, -PotionBurden);
    }
}

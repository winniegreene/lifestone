using System.Globalization;
using System.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The kit-refused station (M4 P5b J3a; ports <c>HealLiveTests</c> test two, :42-55): the mage's Healing is untrained (M3 D19), so a Handy Healing Kit from the item verb used on itself through <see cref="IActionApi.UseWithTarget"/>
/// is a <see cref="ActionOutcomeKind.ServerError"/> with 0x04FC (YouAreNotTrainedInHealing) and a kit whose uses are unchanged; then the kit is dropped and the pack's item count (and the count with the side packs' contents and the worn items) is as before.
/// The kit is the station's own test item (an item, D23) and is dropped again, so it is not counted as a grant (<c>granted=</c> judges what a station needs and an earlier test used up).
/// </summary>
public sealed class KitRefusedStation : IStation
{
    /// <inheritdoc />
    public string Name => "kit-refused";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new KitRefusedRun(context);
}

/// <summary>The kit-refused station's run (see <see cref="KitRefusedStation"/>).</summary>
public sealed class KitRefusedRun : StationLeg
{
    /// <summary>YouAreNotTrainedInHealing.</summary>
    public const uint NotTrainedInHealing = 0x04FC;

    private string _outcome = "none";
    private string _uses = "n/a";

    internal KitRefusedRun(JourneyContext context)
        : base("heal-mage", context)
    {
    }

    /// <summary>The UseDone code the refusal carried (0 when none).</summary>
    public uint Code { get; private set; }

    /// <summary>The main pack's item count before the kit.</summary>
    public int PackBefore { get; private set; } = -1;

    /// <summary>The main pack's item count after the drop.</summary>
    public int PackAfter { get; private set; } = -1;

    /// <summary>The held item count (pack, side packs, worn) before.</summary>
    public int HeldBefore { get; private set; } = -1;

    /// <summary>The held item count after.</summary>
    public int HeldAfter { get; private set; } = -1;

    /// <inheritdoc />
    public override string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"outcome='{_outcome}' code=0x{Code:X4} pack_before={PackBefore} pack_after={PackAfter} held_before={HeldBefore} held_after={HeldAfter} kit_uses={_uses} problems={Problems.Count}")
        + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        PackBefore = S.State.Self.Inventory.Pack.Count;
        HeldBefore = S.State.Self.Inventory.Count;
        Log($"the pack before: {PackBefore} items in the main pack, {HeldBefore} held in all");
        var spawn = S.Tools.SpawnItem(StationItems.KitWcid, null, ActionSource.Host);
        yield return spawn;
        if (Check(spawn.Result is not null, "the Handy Healing Kit grant failed: " + Describe(spawn)))
        {
            var kit = spawn.Result!.Guid;
            foreach (var h in WaitHeld(kit, "the kit")) { yield return h; }

            if (Check(S.State.Self.Inventory.Contains(kit), "the kit is not in the pack after the grant"))
            {
                foreach (var h in Pause(1)) { yield return h; } // the grant's own messages settle before the use

                var usesBefore = Structure(kit);
                var use = S.Actions.UseWithTarget(kit, S.State.Self.Guid, ActionSource.Host);
                yield return use;
                _outcome = use.Outcome is { } o ? $"{o.Kind}/0x{o.Code:X4}/{o.Detail ?? "none"}" : "unsettled";
                Code = use.Outcome?.Code ?? 0;
                _uses = Structure(kit)?.ToString(CultureInfo.InvariantCulture) ?? "?";
                Log($"UseWithTarget on itself with Healing untrained: {_outcome}; kit uses {usesBefore?.ToString(CultureInfo.InvariantCulture) ?? "?"} -> {_uses}");
                Check(use.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: NotTrainedInHealing }, $"the use was not refused with 0x{NotTrainedInHealing:X4} (YouAreNotTrainedInHealing): {Describe(use)}");
                Check(use.Result is null, "an untrained use carried a HealResult: " + use.Result);
                Check(usesBefore == Structure(kit), $"the kit's uses changed: {usesBefore?.ToString(CultureInfo.InvariantCulture) ?? "?"} -> {_uses}");

                foreach (var h in Pause(1)) { yield return h; }

                var drop = S.Actions.Drop(kit, ActionSource.Host);
                yield return drop;
                Log("Drop: " + Describe(drop));
                foreach (var h in Until(() => !S.State.Self.Inventory.Contains(kit), 5)) { yield return h; }

                Check(!S.State.Self.Inventory.Contains(kit), "the kit is still in the pack after the drop");
                foreach (var h in Pause(2)) { yield return h; } // the 019A and the ground object settle

                PackAfter = S.State.Self.Inventory.Pack.Count;
                HeldAfter = S.State.Self.Inventory.Count;
                Log($"the pack after: {PackAfter} items in the main pack, {HeldAfter} held in all (before: {PackBefore} and {HeldBefore})");
                Check(PackAfter == PackBefore && HeldAfter == HeldBefore, $"the pack is not as before: {PackBefore} -> {PackAfter} in the main pack, {HeldBefore} -> {HeldAfter} held in all");
            }
        }
    }

    private int? Structure(uint guid) => S.State.Objects.TryGet(guid, out var o) && o.Weenie.Structure is { } s ? s : null;
}

/// <summary>
/// The harm-heal station (M4 P5b J3a; <see cref="HarmHealScript"/> as a station, the body of the loopback <c>HarmHealLeg</c> without its entry and its log off): the character harms itself to about half with Harm Self, heals by kit and potion (a fighter, in one
/// <see cref="CasterSwap"/> around all the casts) or by Heal Self (the mage), and the vitals keeper restores the health. It runs where the character stands, in peace; a fighter's kit and potion an earlier station used up are granted
/// (<see cref="ScriptLeg.GrantIfMissing"/>, counted, an item: D23). The keeper is the context's when it has one (the journey's counters then include this station's heals), else the station builds its own for the character's means.
/// </summary>
/// <param name="options">The script's options, or null for the defaults.</param>
public sealed class HarmHealStation(HarmHealOptions? options = null) : IStation
{
    /// <inheritdoc />
    public string Name => "harm-heal";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Here, Peace: true, MinHealthPercent: JourneyPlans.FightHealthPercent, MinManaPercent: JourneyPlans.CastManaPercent);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new HarmHealRun(context, options);
}

/// <summary>The harm-heal station's run (see <see cref="HarmHealStation"/>).</summary>
public sealed class HarmHealRun : StationLeg
{
    private readonly HarmHealOptions? _options;

    internal HarmHealRun(JourneyContext context, HarmHealOptions? options)
        : base("harm-heal", context)
    {
        _options = options;
    }

    /// <summary>The script (null before the run reached it).</summary>
    public HarmHealScript? Harm { get; private set; }

    /// <inheritdoc />
    public override bool Ok => base.Ok && Harm is { State: HarmHealState.Done };

    /// <summary>The script's verdict line (<c>harm-heal done ok= ...</c>) and <c>granted=</c>.</summary>
    public string VerdictLine() => (Harm?.VerdictLine() ?? "harm-heal done ok=false why=not-started") + " granted=" + Granted.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string VerdictFields()
    {
        var line = VerdictLine();
        const string head = "harm-heal done ";
        return (line.StartsWith(head, StringComparison.Ordinal) ? line[head.Length..] : line) + $" problems={Problems.Count}";
    }

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        var swap = CasterSwap.ForCharacter(Context.Character, S.Actions, S.State, Context.Log);
        if (swap is not null)
        {
            // The fighters' kit holds 2 Handy Healing Kits and 5 Potions of Healing; the hunts and heals of earlier stations use them, so the station grants what it needs (an item, D23) and says so.
            foreach (var h in GrantIfMissing(PickKit, StationItems.KitWcid, "Handy Healing Kit 628 with uses left", _ => { })) { yield return h; }

            foreach (var h in GrantIfMissing(() => PotionStack().Guid, StationItems.PotionWcid, "Potion of Healing 377", _ => { })) { yield return h; }
        }

        // M4 P5a C7: a caster swap is a fighter: its mana order is a Mana Potion, then a rest. The context's keeper wins when the journey has one.
        var keeper = Context.Keeper ?? new VitalsKeeper(S, Context.Spells, swap is null ? VitalsOptions.Default : VitalsOptions.Default.ForFighter(), Context.Log);
        Harm = new HarmHealScript(S, Context.Spells, keeper, swap, _options ?? new HarmHealOptions(), Context.Log);
        Stage = "harm-heal";
        Log($"health {HealthText()}; kits {InPack(StationItems.KitWcid).Count}; potion stack {PotionStack().Size}; {(swap is null ? "no swap (the caster wields its wand)" : "the caster swap runs once around the casts")}");
        while (Harm.Step(S.Clock.Now) == HarmHealState.Running)
        {
            yield return null;
        }

        Stage = "running";
        Log(Harm.VerdictLine());
        Check(Harm.State == HarmHealState.Done, "the harm-heal script ended " + Harm.State + " why=" + (Harm.Why ?? "none"));
    }

    private string HealthText() => S.State.Self.Qualities.TryGetVitalLevels(1, out var current, out var max) ? $"{current}/{max}" : "unknown";

    /// <summary>The held stack of Potions of Healing (the first in the main pack), its guid and size, or 0 and -1.</summary>
    private (uint Guid, int Size) PotionStack()
    {
        var guid = InPack(StationItems.PotionWcid).FirstOrDefault();
        return guid == 0 ? (0u, -1) : (guid, StackOf(guid));
    }
}

/// <summary>
/// The cast-facts station (M4 P5b J3a; ports <c>CastLiveTests</c> :60 for BSD Mage): the first casts through <see cref="IActionApi"/>: a Creature Enchantment and a Life Magic self buff and an Item Enchantment of the wand lines (the highest level whose
/// cast chance is at least <see cref="GearCasts.MinChance"/>), a certain fizzle by the skill margin (0x0402), the mana that falls, and the component burn (asserted only when the chance that nothing burns is under 1e-3), then the gear scenario's top-up
/// (one stack per component, the pack's slot count unchanged). The gear scenario runs first as an embedded half (its own cast off), so the wand is wielded and the kit held.
/// <para><b>The mana wait.</b> The casts need up to the sum of their ceilings; when the mage has less, the station first asks the context's vitals keeper for it (a Mana Potion where held, else Stamina to Mana Self, never a rest:
/// <c>NoManaRest</c>), and only then, still short, waits for regeneration for at most <see cref="CastFactsRun.ManaWaitSeconds"/> s (the old fact's bound, named, logged, no retry).</para>
/// </summary>
/// <param name="kitAccount">The account name the gear kit is built for (ACE customises the taper slots from it; hashed, never stored or printed). The host reads it at run time; the journey's account key is not it.</param>
public sealed class CastFactsStation(string kitAccount) : IStation
{
    /// <inheritdoc />
    public string Name => "cast-facts";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(StationWhere.Holtburg, Peace: true, MinManaPercent: JourneyPlans.CastManaPercent);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new CastFactsRun(context, kitAccount);
}

/// <summary>The cast-facts station's run (see <see cref="CastFactsStation"/>).</summary>
public sealed class CastFactsRun : StationLeg
{
    /// <summary>The most the run waits for the mana the casts need to regenerate, after the keeper had its say (the old fact's 480 s: from empty to the 200 needed takes about 467 s under vitae).</summary>
    public const double ManaWaitSeconds = 480;

    /// <summary>The most the run gives the vitals keeper for the mana, in seconds.</summary>
    public const double KeeperSeconds = 120;

    private readonly string _account;
    private readonly List<(SpellInfo Info, uint Skill)> _made = [];
    private GearKit? _kit;
    private uint _manaNeeded;

    internal CastFactsRun(JourneyContext context, string account)
        : base("cast-leg", context)
    {
        _account = account;
    }

    /// <summary>The summary body (<c>creature= life= item= fizzle= mana_needed= mana_before= mana_after= p_no_burn= burned= burn_check= merges= slots=</c>) once every assertion held, else null.</summary>
    public string? Summary { get; private set; }

    /// <inheritdoc />
    public override string VerdictFields() => (Summary ?? "creature=n/a") + $" problems={Problems.Count}" + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <inheritdoc />
    public override bool Ok => base.Ok && Summary is not null;

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        Require(GearScenario.WrongCharacter(S) is null, GearScenario.WrongCharacter(S) ?? string.Empty);
        _kit = GearKit.Build(Context.Spells, _account);
        var spells = Context.Spells;
        var inventory = S.State.Self.Inventory;

        // The gear first (the wand wielded, the kit held): ACE holds the magic mode only with a wielded caster (Player_Combat.cs 872-880 @95ef426d). Its own cast is off.
        Stage = "gear";
        GearScenario? gear = null;
        foreach (var h in RunGear("gear", g => gear = g)) { yield return h; }

        Log("gear: " + gear!.VerdictFields());
        Require(gear.VerdictFields().StartsWith("gear=wielded worn=5 ", StringComparison.Ordinal), "the gear scenario did not end wielded: " + gear.VerdictFields());
        var q = S.State.Self.Qualities;

        // The plan: every cast chosen from the read model before anything is spent.
        Stage = "plan";
        var creaturePick = GearCasts.Pick(q, ProfileRole.CreatureBuff, out var creatureWhy);
        Require(creaturePick is not null, $"creature: no spell passes the {GearCasts.MinChance} rule: {creatureWhy}");
        var lifePick = GearCasts.Pick(q, ProfileRole.LifeBuff, out var lifeWhy);
        Require(lifePick is not null, $"life: no spell passes the {GearCasts.MinChance} rule: {lifeWhy}");
        var itemPick = GearCasts.Pick(q, ProfileRole.ItemBuffWand, out var itemWhy);
        Require(itemPick is not null, $"item: no spell passes the {GearCasts.MinChance} rule: {itemWhy}");
        var fizzlePick = GearCasts.PickFizzle(q, S.State.Self.Spellbook, out var fizzleWhy);

        // A buffed mage has no known spell more than 50 above its skill: the certain fizzle cannot be made then. That is reported (fizzle=n/a:buffed), never skipped silently.
        var buffed = fizzlePick is null && HoldsMasteryBuff(q);
        Require(fizzlePick is not null || buffed, "fizzle: " + fizzleWhy);
        if (fizzlePick is null)
        {
            Log("fizzle: n/a: the mage holds a magic-skill mastery buff, so no known spell's power is more than 50 above its current skill (" + fizzleWhy + "); the certain fizzle is not cast in this run");
        }

        var creatureInfo = InfoOf("creature", creaturePick!.Spell.Id);
        var lifeInfo = InfoOf("life", lifePick!.Spell.Id);
        var itemInfo = InfoOf("item", itemPick!.Spell.Id);
        var fizzleInfo = fizzlePick is null ? null : InfoOf("fizzle", fizzlePick.Spell.Id);
        var worn = inventory.Equipped.Count;
        var (creatureMana, lifeMana, itemMana) = (GearCasts.ManaCeiling(creatureInfo, worn), GearCasts.ManaCeiling(lifeInfo, worn), GearCasts.ManaCeiling(itemInfo, worn));
        _manaNeeded = creatureMana + lifeMana + itemMana + (fizzlePick is null ? 0 : GearCasts.FizzleMana);
        Log($"plan: creature {creaturePick.Spell.Name} ({creatureMana}), life {lifePick.Spell.Name} ({lifeMana}), item {itemPick.Spell.Name} ({itemMana}), fizzle {(fizzlePick is null ? "n/a (buffed)" : $"{fizzlePick.Spell.Name} ({GearCasts.FizzleMana})")}; mana needed up to {_manaNeeded}");

        // The mana precondition: nothing has been cast, so waiting is no retry. The keeper first (Stamina to Mana, never a rest), then a bounded wait, loud when the mana never comes.
        Stage = "mana";
        var (manaNow, manaMax) = Mana();
        Require(manaMax >= _manaNeeded, $"the casts need up to {_manaNeeded} mana and the maximum is {manaMax} (current {manaNow}): they can never all be paid");
        if (manaNow < _manaNeeded && Context.Keeper is { } keeper)
        {
            Log($"mana: {manaNow} of {manaMax}, up to {_manaNeeded} needed: the vitals keeper first (a Mana Potion or Stamina to Mana Self, no rest), at most {KeeperSeconds:0} s");
            var keeperUntil = S.Clock.Now + KeeperSeconds;
            if (!keeper.NeedsFor(new VitalsNeeds(NextCastMana: _manaNeeded, NoManaRest: true)))
            {
                for (;;)
                {
                    var state = keeper.Step(S.Clock.Now);
                    if ((state is VitalsKeeperState.Ready or VitalsKeeperState.Blocked && !keeper.Busy) || S.Clock.Now >= keeperUntil)
                    {
                        break;
                    }

                    yield return null;
                }
            }

            Log($"mana: the keeper ended {keeper.State}{(keeper.Why is null ? string.Empty : " why=" + keeper.Why)}; mana now {Mana().Current}; {keeper.VerdictFields()}");
        }

        if (Mana().Current < _manaNeeded)
        {
            var waitStart = S.Clock.Now;
            Log($"mana: {Mana().Current} of {manaMax}, up to {_manaNeeded} needed: waiting for regeneration, at most {ManaWaitSeconds:0} s");
            while (Mana().Current < _manaNeeded)
            {
                Require(S.Clock.Now - waitStart <= ManaWaitSeconds, $"the mana did not reach {_manaNeeded} within {ManaWaitSeconds:0} s: {Mana().Current} of {manaMax}");
                yield return null;
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"mana: {Mana().Current} reached the {_manaNeeded} needed after {S.Clock.Now - waitStart:0.#} s"));
        }

        Stage = "magic-mode";
        var mode = S.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        yield return mode;
        Require(mode.Outcome is { Kind: ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local }, "entering the magic combat mode: " + Describe(mode));

        var manaBefore = Mana().Current;
        var slotsBefore = inventory.Pack.Count;
        var countsBefore = GearInventory.CountAll(S.State, out _);
        Log($"before the casts: mana {manaBefore}, needed up to {_manaNeeded}");

        string creature = string.Empty, life = string.Empty, item = string.Empty;
        foreach (var step in CastAndAssert("creature", creaturePick, creatureInfo, r => creature = r)) { yield return step; }

        foreach (var step in CastAndAssert("life", lifePick, lifeInfo, r => life = r)) { yield return step; }

        foreach (var step in CastAndAssert("item", itemPick, itemInfo, r => item = r)) { yield return step; }

        // (d) Mana after the successful casts.
        var manaAfter = Mana().Current;
        Log($"after the successful casts: mana {manaAfter}");
        Require(manaAfter < manaBefore, $"mana did not fall over the successful casts: {manaBefore} -> {manaAfter}");

        // (c) The certain fizzle.
        if (fizzlePick is not null && fizzleInfo is not null)
        {
            Stage = "fizzle";
            Log(string.Create(CultureInfo.InvariantCulture, $"fizzle: {fizzlePick.Spell.Name} ({fizzlePick.Spell.Id}) has power {fizzlePick.Spell.Power} against {fizzlePick.Line.School} skill {fizzlePick.Skill}: margin {fizzlePick.Spell.Power - (long)fizzlePick.Skill}, mana {Mana().Current}"));
            var fizzleMark = S.State.Chat.Total;
            var fizzle = S.Actions.Cast(fizzlePick.Spell.Id, ActionSource.Host);
            yield return fizzle;
            _made.Add((fizzleInfo, fizzlePick.Skill));
            Require(
                fizzle.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: GearCasts.FizzleCode },
                $"fizzle: expected a ServerError with WeenieError 0x0402 for {fizzlePick.Spell.Name} ({fizzlePick.Spell.Id}), got {Said(fizzle, fizzleMark)}");
            Log("fizzle: " + Describe(fizzle));
        }

        // (e) The component burn, read once the last cast's inventory messages have arrived.
        Stage = "burn";
        foreach (var h in Pause(2.0)) { yield return h; }

        var countsAfter = GearInventory.CountAll(S.State, out _);
        var (burned, which) = Burned(countsBefore, countsAfter);
        var noBurn = 1.0;
        foreach (var (info, skill) in _made)
        {
            noBurn *= GearCasts.NoBurnProbability(info, _account, skill);
        }

        var asserted = noBurn < 1e-3;
        Log(string.Create(CultureInfo.InvariantCulture, $"burned: {which}; the chance that the {_made.Count} casts burn nothing was {noBurn:0.0000} (ACE Spell.TryBurnComponents), so the burn check is {(asserted ? "asserted" : "report-only")}"));
        if (asserted)
        {
            Require(burned >= 1, $"no kit component's stack fell over the casts although the chance of that was only {noBurn:0.000000} (before {countsBefore.Count} weenies, after {countsAfter.Count})");
        }

        Stage = "peace";
        var peace = S.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        yield return peace;
        Require(peace.Outcome is { Kind: ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local }, "returning to Non-combat: " + Describe(peace));

        // (f) The top-up after the burn: the gear scenario again (its cast off) tops the burned lines up; the main pack's slot count is what it was before the casts, each component weenie is one stack, and a rerun tops up nothing.
        Stage = "topup";
        GearScenario? topUp = null;
        foreach (var step in RunGear("topup", g => topUp = g)) { yield return step; }

        foreach (var h in Pause(2.0)) { yield return h; }

        var merges = topUp!.TopUp;
        var slotsAfter = inventory.Pack.Count;
        var stacks = new Dictionary<uint, int>();
        foreach (var entry in inventory.Pack)
        {
            if (S.State.Objects.TryGet(entry.Guid, out var held))
            {
                stacks[held.Weenie.WeenieClassId] = stacks.GetValueOrDefault(held.Weenie.WeenieClassId) + 1;
            }
        }

        var split = new StringBuilder();
        foreach (var component in _kit.Components)
        {
            if (stacks.GetValueOrDefault(component.Wcid) > 1)
            {
                split.Append(CultureInfo.InvariantCulture, $"{component.Name} (weenie {component.Wcid}) in {stacks[component.Wcid]} stacks; ");
            }
        }

        Log($"topup: the gear scenario sent {topUp.TopUp} component requests (merges={merges}); main-pack slots {slotsBefore}->{slotsAfter}; {topUp.VerdictFields()}");
        Require(topUp.Failure is null, "the top-up run failed (a merge failure fails SpawnGear): " + topUp.Failure);
        Require(slotsAfter == slotsBefore, $"the main pack's slot count changed over the casts and the top-up: {slotsBefore} -> {slotsAfter} (a top-up that is not merged takes a slot)");
        Require(split.Length == 0, "a component is in more than one stack: " + split);
        GearScenario? again = null;
        foreach (var step in RunGear("topup-rerun", g => again = g)) { yield return step; }

        Require(again!.TopUp == 0 && again.GearSteps == 0, $"a rerun after the top-up still sends requests: {again.VerdictFields()}");

        Summary = string.Create(
            CultureInfo.InvariantCulture,
            $"creature={creature} life={life} item={item} fizzle={(fizzlePick is null ? "n/a:buffed" : fizzlePick.Spell.Id.ToString(CultureInfo.InvariantCulture) + ":0x0402")} mana_needed={_manaNeeded} mana_before={manaBefore} mana_after={manaAfter} p_no_burn={noBurn:0.0000} burned={burned} burn_check={(asserted ? "asserted" : "report-only")} merges={merges} slots={slotsBefore}->{slotsAfter}");
    }

    /// <summary>Whether the registry holds a spell of one of the profile's magic-skill mastery lines (War, Creature, Item, Life Magic Mastery Self): a buffed mage.</summary>
    private static bool HoldsMasteryBuff(PlayerQualities q)
    {
        foreach (var line in ArenaProfile.Lines)
        {
            if (line.Role != ProfileRole.CreatureBuff || !line.Name.EndsWith("Mastery Self", StringComparison.Ordinal) || line.Name.StartsWith("Mana", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var spell in line.Spells)
            {
                foreach (var e in q.Enchantments)
                {
                    if (e.SpellId == spell.Id)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private (uint Current, uint Max) Mana() =>
        S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var current, out var max) ? (current, max) : throw new InvalidOperationException("the read model has no mana vital");

    /// <summary>What the server said during a cast that did not do its work: the outcome's code, named, and the chat lines since <paramref name="chatMark"/>.</summary>
    private string Said(ActionHandle handle, long chatMark) =>
        handle.Outcome is { } o ? GearCasts.Explain(o, S.State, chatMark, _manaNeeded) : "(no outcome)";

    private SpellInfo InfoOf(string label, uint spellId) =>
        Context.Spells.TryGetValue(spellId, out var info) ? info : throw new InvalidOperationException($"{label}: spell {spellId} is not in the SpellTable");

    /// <summary>The units burned: for each kit component, how far its held count fell (a component that rose, or a top-up, counts 0), and which.</summary>
    private (int Burned, string Which) Burned(Dictionary<uint, int> before, Dictionary<uint, int> after)
    {
        var burned = 0;
        var which = new StringBuilder();
        foreach (var item in _kit!.Components)
        {
            var drop = before.GetValueOrDefault(item.Wcid) - after.GetValueOrDefault(item.Wcid);
            if (drop > 0)
            {
                burned += drop;
                which.Append(CultureInfo.InvariantCulture, $"{item.Name} (weenie {item.Wcid}) -{drop}; ");
            }
        }

        return (burned, which.Length == 0 ? "(none)" : which.ToString());
    }

    /// <summary>The gear scenario over the same session (its cast off, embedded: it ends in the world), stepped to its end; <paramref name="done"/> gets it first.</summary>
    private IEnumerable<ActionHandle?> RunGear(string label, Action<GearScenario> done)
    {
        var gear = new GearScenario(_kit!, Context.Log, endSession: false, cast: false) { RequireCharacter = true };
        done(gear);
        for (;;)
        {
            var status = gear.Step(S, S.Clock.Now);
            if (status == ScenarioStatus.Succeeded)
            {
                yield break;
            }

            Require(status != ScenarioStatus.Failed, $"{label}: the gear scenario failed: {gear.Failure}");
            yield return null;
        }
    }

    /// <summary>
    /// Casts <paramref name="pick"/> in the form its SpellTable target type gives and asserts it: a self cast is Confirmed by the registry (the enchantment, with the SpellTable's duration); a cast at the wand is proved by the
    /// UseDone 0 and ACE's chat line. Every refusal names what the server said. The outcome kind and form go to <paramref name="done"/>.
    /// </summary>
    private IEnumerable<ActionHandle?> CastAndAssert(string label, CastPick pick, SpellInfo info, Action<string> done)
    {
        var q = S.State.Self.Qualities;
        var form = GearCasts.FormOf(info);
        Log($"{label}: {pick.Describe()}, cast as a {(form == CastForm.Self ? "self cast (the spell targets the caster)" : "cast at the wand (the spell targets an item)")}, NonComponentTargetType 0x{info.NonComponentTargetType:X}, base mana {info.BaseMana}");
        Stage = label;
        var chatMark = S.State.Chat.Total;
        ActionHandle cast;
        var wandName = string.Empty;
        if (form == CastForm.Self)
        {
            cast = S.Actions.Cast(pick.Spell.Id, ActionSource.Host);
        }
        else
        {
            uint wand = 0;
            foreach (var equipped in S.State.Self.Inventory.Equipped)
            {
                if (S.State.Objects.TryGet(equipped.Guid, out var o) && o.Weenie.WeenieClassId == GearKit.Wand.Wcid)
                {
                    wand = equipped.Guid;
                    break;
                }
            }

            Require(wand != 0, $"{label}: the wand is not among the equipped items");
            wandName = S.State.Objects.TryGet(wand, out var wandObject) ? wandObject.Name : GearKit.Wand.Name;
            cast = S.Actions.Cast(pick.Spell.Id, wand, ActionSource.Host);
        }

        yield return cast;
        _made.Add((info, pick.Skill));
        if (form == CastForm.Self)
        {
            Require(cast.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"{label} cast of {pick.Spell.Name} ({pick.Spell.Id}): {Said(cast, chatMark)}");
            var entry = q.Enchantments.Where(e => e.SpellId == pick.Spell.Id).Select(e => (EnchantmentDesc?)e).FirstOrDefault();
            Require(entry is not null, $"{label}: the registry holds no enchantment of {pick.Spell.Name} ({pick.Spell.Id}) after a confirmed cast");
            Require(
                Math.Abs(entry!.Value.Duration - pick.Spell.DurationSeconds) < 0.5,
                string.Create(CultureInfo.InvariantCulture, $"{label}: the enchantment lasts {entry.Value.Duration} s, the SpellTable's duration for {pick.Spell.Name} is {pick.Spell.DurationSeconds} s"));
            Log(string.Create(CultureInfo.InvariantCulture, $"{label}: {pick.Spell.Name} in the registry, layer {entry.Value.Layer}, duration {entry.Value.Duration} s: {Describe(cast)}"));
            done($"{pick.Spell.Id}:{cast.Outcome!.Value.Kind}:self");
            yield break;
        }

        // A spell on an item: ACE sends no 0x02C2 to the player, so the UseDone 0 and the chat line are the proof that exists on this server.
        Require(cast.Outcome is { Kind: ActionOutcomeKind.Sent, Code: 0 }, $"{label} cast of {pick.Spell.Name} ({pick.Spell.Id}) at the wand: {Said(cast, chatMark)}");
        var expectedLine = $"You cast {pick.Spell.Name} on {wandName}";
        var fresh = (int)Math.Min(S.State.Chat.Count, S.State.Chat.Total - chatMark);
        var seen = false;
        var tail = new StringBuilder();
        for (var i = S.State.Chat.Count - fresh; i < S.State.Chat.Count; i++)
        {
            var text = S.State.Chat[i].Text;
            tail.Append(CultureInfo.InvariantCulture, $"[{text}] ");
            seen |= text.StartsWith(expectedLine, StringComparison.Ordinal);
        }

        Require(seen, $"{label}: ACE's chat line '{expectedLine}' did not arrive; {Said(cast, chatMark)}; the {fresh} new chat lines: {tail}");
        Log($"{label}: {expectedLine} (chat) and {Describe(cast)}");
        done($"{pick.Spell.Id}:{cast.Outcome!.Value.Kind}:item");
    }
}

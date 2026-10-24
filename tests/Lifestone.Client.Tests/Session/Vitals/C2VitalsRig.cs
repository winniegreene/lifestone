using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>The character a <see cref="C2VitalsRig"/> describes (lane C2's test data, not a template's).</summary>
internal sealed record C2Character
{
    /// <summary>Healing's advancement class: 1 untrained, 2 trained, 3 specialised.</summary>
    public uint HealingClass { get; init; } = 2;

    /// <summary>Life Magic's advancement class (1: untrained, no spell is known then).</summary>
    public uint LifeMagicClass { get; init; } = 2;

    /// <summary>Life Magic's ranks: the skill is 50 (Focus 100 + Self 100, over 4) plus these.</summary>
    public ushort LifeMagicRanks { get; init; } = 150;

    /// <summary>The known spells.</summary>
    public uint[] Spells { get; init; } = [];

    /// <summary>The kit's uses (0: no kit held).</summary>
    public int KitUses { get; init; }

    /// <summary>The potions held (0: none).</summary>
    public uint Potions { get; init; }

    /// <summary>The Mana Potions held, one stack (M4 P5a C7; 0: none). Each drink restores 25 mana.</summary>
    public uint ManaPotions { get; init; }

    /// <summary>The Stamina Potions held, one stack (M4 P5a C7; 0: none). Each drink restores 25 stamina.</summary>
    public uint StaminaPotions { get; init; }

    /// <summary>True when a wand is wielded (the magic mode is allowed); false: nothing is wielded and <c>CombatModeGuard</c> refuses the magic mode.</summary>
    public bool Caster { get; init; } = true;

    /// <summary>True when the caster is held in the pack, not wielded (a fighter's kit, M4 P5a C1b): <see cref="Caster"/> is then false and the magic mode is still refused.</summary>
    public bool CasterInPack { get; init; }

    /// <summary>The current health (the maximum is 200).</summary>
    public uint Health { get; init; } = 200;

    /// <summary>The current stamina (the maximum is 200).</summary>
    public uint Stamina { get; init; } = 200;

    /// <summary>The current mana (the maximum is 200).</summary>
    public uint Mana { get; init; } = 200;

    /// <summary>The maximum health (M4 P5a fix r4: round 4's Brawler has 231; default 200).</summary>
    public uint MaxHealth { get; init; } = 200;

    /// <summary>The maximum stamina (default 200).</summary>
    public uint MaxStamina { get; init; } = 200;

    /// <summary>The maximum mana (M4 P5a fix r4: round 4's Brawler has 287; default 200).</summary>
    public uint MaxMana { get; init; } = 200;

    /// <summary>The combat mode at the start.</summary>
    public int Mode { get; init; } = CombatModes.NonCombat;
}

/// <summary>
/// The vitals keeper's scripted rig (M4 P5a lane C2): a character on a <see cref="WorldActionRig"/> (the real read model, clock, bus and Action API), a movement controller spy bound to
/// <see cref="IActionApi.Move"/>, and a <see cref="Pump"/> that answers what the keeper sent as ACE does, through the real decoder: a kit use (the vital, the line, the Structure, UseDone, the Ready F74C; the
/// capture's order, as <see cref="HealRig"/>), a drink (the vital, the line, the stack, the Ready, UseDone), a self cast (the cost, the effect: Heal Self's points, Revitalize's boost, Stamina to Mana by
/// <see cref="BuffStatMods.Transfer"/>; a fizzle on demand), and a combat mode change (0x02CD). Vitals regenerate every second at the rig's rates times ACE's stance factors (<c>Creature_Vitals.cs</c> 210-236 at
/// 95ef426d: 0.5 in a combat mode, sitting 2.5, lying down 3.0, health and stamina only), with the posture read from the commands the spy took. All three maxima are 200. The regeneration rates are ACE's (<see cref="AceHealthPerTick"/> and its siblings, M4 P5a fix F-abil); the costs are the rig's.
/// </summary>
internal sealed class C2VitalsRig
{
    public const uint Kit = 0x80000C01;
    public const uint Potion = 0x80000C11;

    /// <summary>The Mana Potion stack and the Stamina Potion stack (M4 P5a C7).</summary>
    public const uint ManaPotion = 0x80000C12, StaminaPotion = 0x80000C13;
    public const uint WandGuid = 0x80000C21;
    public const uint HealthKey = 2;
    public const uint StaminaKey = 4;
    public const uint ManaKey = 6;

    /// <summary>The points a won kit roll heals.</summary>
    public const uint KitPoints = 60;

    /// <summary>The points Heal Self heals.</summary>
    public const uint HealSelfPoints = 50;

    private readonly Dictionary<uint, byte> _sequence = [];
    private readonly Queue<bool> _kitRolls = new();
    private readonly Dictionary<uint, int> _fizzles = [];
    private int _seen;
    private byte _seq;
    private double _last = double.NaN;
    private double _hAcc;
    private double _sAcc;
    private double _mAcc;

    public C2VitalsRig(C2Character character, VitalsOptions? options = null, bool bindController = true)
    {
        Character = character;
        R = new WorldActionRig();
        Health = character.Health;
        LowestHealth = character.Health;
        Stamina = character.Stamina;
        Mana = character.Mana;
        KitUses = character.KitUses;
        Potions = character.Potions;
        ManaPotions = character.ManaPotions;
        StaminaPotions = character.StaminaPotions;
        Table = BuildTable();
        R.Spells([.. Table.Values]);
        Describe();
        R.CombatModeIs(character.Mode);
        if (bindController)
        {
            R.Api.Move.Bind(Spy, false);
        }

        Options = options ?? new VitalsOptions();
        Keeper = new VitalsKeeper(new View(R), Table, Options, R.H.Log);
    }

    public C2Character Character { get; }

    public WorldActionRig R { get; }

    public VitalsOptions Options { get; }

    public VitalsKeeper Keeper { get; }

    public Dictionary<uint, SpellInfo> Table { get; }

    public PostureSpy Spy { get; } = new();

    public uint Health { get; private set; }

    public uint Stamina { get; private set; }

    public uint Mana { get; private set; }

    public int KitUses { get; private set; }

    public uint Potions { get; private set; }

    /// <summary>True when the server's vital update after a Mana or Stamina Potion is lost: the stack falls and the line comes, the vital does not change (M4 P5a C7).</summary>
    public bool PotionVitalLost { get; set; }

    /// <summary>The Mana Potions left (M4 P5a C7).</summary>
    public uint ManaPotions { get; private set; }

    /// <summary>The Stamina Potions left (M4 P5a C7).</summary>
    public uint StaminaPotions { get; private set; }

    /// <summary>ACE's stored regeneration rates, a tick (M4 P5a fix F-abil, P5a-R5; research.md section 6, <c>Creature_Vitals.cs</c> 78-168 at 95ef426d): health 0.3, stamina 3, mana 1.</summary>
    public const double AceHealthPerTick = 0.3, AceStaminaPerTick = 3.0, AceManaPerTick = 1.0;

    /// <summary>ACE ticks the vitals every 5 s (<c>Creature_Tick.cs</c> 33 at 95ef426d); the rig spreads a tick over the seconds.</summary>
    public const double AceTickSeconds = 5.0;

    /// <summary>
    /// ACE's attribute factor for health regeneration (<c>Creature_Vitals.cs</c> 78-168: 1 when Strength + 2 Endurance is 200 or less, else 1 + (S - 200) / 600, at most 2.1). The rig's character has Strength 100 and Endurance 100
    /// (<see cref="Describe"/>), so S is 300 and the factor 1.1667; stamina and mana have no attribute factor.
    /// </summary>
    public static double HealthAttributeFactor(uint strength = 100, uint endurance = 100) => Math.Clamp(1.0 + ((((double)strength + (2.0 * endurance)) - 200.0) / 600.0), 1.0, 2.1);

    /// <summary>Health regenerated per second standing in peace: ACE's 0.3 a 5 s tick times the attribute factor (0.07 a second here).</summary>
    public double HealthRegen { get; set; } = AceHealthPerTick / AceTickSeconds * HealthAttributeFactor();

    /// <summary>Stamina regenerated per second standing in peace: ACE's 3 a 5 s tick (0.6 a second).</summary>
    public double StaminaRegen { get; set; } = AceStaminaPerTick / AceTickSeconds;

    /// <summary>Mana regenerated per second (no stance factor): ACE's 1 a 5 s tick (0.2 a second).</summary>
    public double ManaRegen { get; set; } = AceManaPerTick / AceTickSeconds;

    /// <summary>Lane C3: a Harm Self takes its level's largest harm instead of the mean.</summary>
    public bool HarmWorstCase { get; set; }

    /// <summary>P5a fix F-abil (R11): the factor a drain resistance scales a harm by (1.0: none; above 1.0 a harm lands past its table maximum).</summary>
    public double HarmScale { get; set; } = 1.0;

    /// <summary>P5a fix F-abil (R6): how many potions a drink takes off the stack (1, as ACE does); another number breaks the script's stack-down-by-one check on purpose.</summary>
    public uint PotionsPerDrink { get; set; } = 1;

    /// <summary>Lane C3: the lowest health the rig held (0 would be a death: ACE clamps the vital there).</summary>
    public uint LowestHealth { get; private set; }

    /// <summary>The spells the rig answered, in order (true: fizzled).</summary>
    public List<(uint Spell, bool Fizzled)> Casts { get; } = [];

    /// <summary>The mode each cast was answered in.</summary>
    public List<int> CastModes { get; } = [];

    /// <summary>The posture the spy held when each kit, potion or cast was answered (0: standing).</summary>
    public List<uint> PosturesAtUse { get; } = [];

    /// <summary>What the keeper used, in order: kit, potion, cast:&lt;id&gt;, mode:&lt;n&gt;.</summary>
    public List<string> Used { get; } = [];

    public double Now => R.H.R.Clock.Now;

    /// <summary>The next kit rolls (true wins); with none queued a roll wins.</summary>
    public void KitRolls(params bool[] rolls)
    {
        foreach (var roll in rolls)
        {
            _kitRolls.Enqueue(roll);
        }
    }

    /// <summary>The next <paramref name="count"/> casts of <paramref name="spellId"/> fizzle.</summary>
    public void FizzleNext(uint spellId, int count) => _fizzles[spellId] = count;

    /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers, the Action API completes, then the keeper steps.</summary>
    public VitalsKeeperState Frame(double dt = 0.25)
    {
        R.Tick(dt);
        Pump();
        R.Tick();
        return Keeper.Step(Now);
    }

    /// <summary>Frames until the keeper is Ready or Blocked, or <paramref name="maxSeconds"/> passed; the final state.</summary>
    public VitalsKeeperState RunToEnd(double maxSeconds = 2000, double dt = 0.25)
    {
        var end = Now + maxSeconds;
        var state = Keeper.State;
        while (Now < end)
        {
            state = Frame(dt);
            if (state is VitalsKeeperState.Ready or VitalsKeeperState.Blocked && !Keeper.Busy)
            {
                return state;
            }
        }

        return state;
    }

    /// <summary>The player's health becomes <paramref name="value"/> (the server's 0x02E9), e.g. damage taken.</summary>
    public void SetHealth(uint value)
    {
        Health = value;
        SendVital(HealthKey, Health);
    }

    /// <summary>The player's stamina becomes <paramref name="value"/>.</summary>
    public void SetStamina(uint value)
    {
        Stamina = value;
        SendVital(StaminaKey, Stamina);
    }

    /// <summary>Answers what was sent since the last pump, then regenerates.</summary>
    public void Pump()
    {
        var now = Now;
        Regenerate(now);
        var sends = R.H.Net.Sends;
        while (_seen < sends.Count)
        {
            var send = sends[_seen++];
            switch (send.Kind)
            {
                case FakeSendKind.UseWithTarget:
                    Assert.Equal(Kit, send.Guid);
                    UseKit();
                    break;
                case FakeSendKind.Use:
                    if (send.Guid == ManaPotion || send.Guid == StaminaPotion)
                    {
                        DrinkVitalPotion(send.Guid == ManaPotion);
                        break;
                    }

                    Assert.Equal(Potion, send.Guid);
                    DrinkPotion();
                    break;
                case FakeSendKind.CastUntargetedSpell:
                    Answer(send.Guid);
                    break;
                case FakeSendKind.CastTargetedSpell:
                    Answer((uint)send.Index);
                    break;
                case FakeSendKind.ChangeCombatMode:
                    Used.Add("mode:" + send.Amount);
                    R.CombatModeIs((int)send.Amount);
                    break;
            }
        }
    }

    private void UseKit()
    {
        Used.Add("kit");
        PosturesAtUse.Add(Spy.Posture);
        if (Character.HealingClass < 2)
        {
            R.UseDone(0x04FC); // ACE YouArentTrainedInHealing (capture C4)
            return;
        }

        var win = _kitRolls.Count == 0 || _kitRolls.Dequeue();
        KitUses--;
        if (win)
        {
            var before = Health;
            Health = Math.Min(Character.MaxHealth, Health + KitPoints);
            SendVital(HealthKey, Health);
            Line($"You heal yourself for {Health - before} Health points. Your Handy Healing Kit has {KitUses} uses left.");
        }
        else
        {
            Line($"You fail to heal yourself. Your Handy Healing Kit has {KitUses} uses left.");
        }

        R.Raw(AceM3Writers.PublicUpdatePropertyInt(++_seq, Kit, 92, KitUses));
        R.UseDone();
        Ready();
    }

    /// <summary>A Mana or Stamina Potion (M4 P5a C7; ACE <c>Food.BoostVital</c>): +25 of the vital, the line, the stack, the use done.</summary>
    private void DrinkVitalPotion(bool mana)
    {
        Used.Add(mana ? "mana-potion" : "stamina-potion");
        PosturesAtUse.Add(Spy.Posture);
        var guid = mana ? ManaPotion : StaminaPotion;
        var before = mana ? Mana : Stamina;
        var max = mana ? Character.MaxMana : Character.MaxStamina;
        var nominal = Math.Min(max, before + (uint)FighterKit.VitalPotionBoost);
        var after = PotionVitalLost ? before : nominal;
        if (mana)
        {
            Mana = after;
        }
        else
        {
            Stamina = after;
        }

        if (!PotionVitalLost)
        {
            SendVital(mana ? ManaKey : StaminaKey, after);
        }

        Line($"The {(mana ? "Mana" : "Stamina")} Potion restores {nominal - before} points of your {(mana ? "Mana" : "Stamina")}.");
        var left = (mana ? ManaPotions : StaminaPotions) > 1 ? (mana ? ManaPotions : StaminaPotions) - 1 : 0;
        if (mana)
        {
            ManaPotions = left;
        }
        else
        {
            StaminaPotions = left;
        }

        if (left == 0)
        {
            R.Raw(AceM3Writers.InventoryRemoveObject(guid));
        }
        else
        {
            R.Raw(AceM3Writers.SetStackSize(++_seq, guid, left, left * 100));
        }

        Ready();
        R.UseDone();
    }

    private void DrinkPotion()
    {
        Used.Add("potion");
        PosturesAtUse.Add(Spy.Posture);
        var before = Health;
        Health = Math.Min(Character.MaxHealth, Health + (uint)RecoveryRules.PotionPoints);
        SendVital(HealthKey, Health);
        Line($"The Potion of Healing restores {Health - before} points of your Health.");
        Potions = Potions > PotionsPerDrink ? Potions - PotionsPerDrink : 0;
        if (Potions == 0)
        {
            R.Raw(AceM3Writers.InventoryRemoveObject(Potion));
        }
        else
        {
            R.Raw(AceM3Writers.SetStackSize(++_seq, Potion, Potions, Potions * 170));
        }

        Ready();
        R.UseDone();
    }

    private void Answer(uint spellId)
    {
        var info = Table[spellId];
        Used.Add("cast:" + spellId);
        PosturesAtUse.Add(Spy.Posture);
        CastModes.Add(R.H.State.Self.Qualities.CombatMode);
        if (_fizzles.TryGetValue(spellId, out var left) && left > 0)
        {
            _fizzles[spellId] = left - 1;
            Casts.Add((spellId, true));
            Mana = Mana >= 5 ? Mana - 5 : 0;
            SendVital(ManaKey, Mana);
            R.WeenieError(0x0402);
            R.UseDone();
            return;
        }

        Casts.Add((spellId, false));
        if (Mana < info.BaseMana)
        {
            R.UseDone(0x0401);
            return;
        }

        Mana -= info.BaseMana;
        SendVital(ManaKey, Mana);
        if (VitalsMeans.HealSelf.Contains(spellId))
        {
            Health = Math.Min(Character.MaxHealth, Health + HealSelfPoints);
            SendVital(HealthKey, Health);
        }
        else if (HarmSelfPlan.For(spellId) is { } harm)
        {
            // Lane C3: ACE's Harm Self rolls between its two table ends and clamps the vital at 0; the rig takes the mean (or the worst end when HarmWorstCase is set).
            var points = (uint)Math.Round((HarmWorstCase ? harm.MaxHarm : (int)Math.Round(harm.MeanHarm)) * HarmScale);
            Health = Health > points ? Health - points : 0;
            SendVital(HealthKey, Health);
        }
        else if (VitalsMeans.RevitalizeSelf.Contains(spellId))
        {
            var boost = BuffStatMods.Revitalize.First(b => b.Id == spellId);
            Stamina = Math.Min(Character.MaxStamina, Stamina + (uint)(boost.Boost + (boost.Variance / 2)));
            SendVital(StaminaKey, Stamina);
        }
        else
        {
            var row = BuffStatMods.TransferOf(spellId)!.Value;
            BuffStatMods.Transfer(in row, Stamina, Character.MaxMana - Mana, out var drained, out var given);
            Stamina -= drained;
            Mana += given;
            SendVital(StaminaKey, Stamina);
            SendVital(ManaKey, Mana);
        }

        R.UseDone();
    }

    private void Regenerate(double now)
    {
        if (!double.IsNaN(_last) && now > _last)
        {
            var dt = now - _last;
            var combat = R.H.State.Self.Qualities.CombatMode is not (0 or CombatModes.NonCombat);
            var stance = combat ? 0.5 : Spy.Posture switch { VitalsKeeper.SitCommand => 2.5, VitalsKeeper.LieCommand => 3.0, _ => 1.0 };
            _hAcc += dt * HealthRegen * stance;
            _sAcc += dt * StaminaRegen * stance;
            _mAcc += dt * ManaRegen;
            Health = Grow(Health, ref _hAcc, HealthKey);
            Stamina = Grow(Stamina, ref _sAcc, StaminaKey);
            Mana = Grow(Mana, ref _mAcc, ManaKey);
        }

        _last = now;
    }

    private uint Grow(uint current, ref double acc, uint key)
    {
        if (acc < 1)
        {
            return current;
        }

        var whole = (uint)acc;
        acc -= whole;
        var max = key == HealthKey ? Character.MaxHealth : key == StaminaKey ? Character.MaxStamina : Character.MaxMana;
        if (current >= max)
        {
            return current;
        }

        current = Math.Min(max, current + whole);
        SendVital(key, current);
        return current;
    }

    private void SendVital(uint key, uint current)
    {
        if (key == HealthKey && current < LowestHealth)
        {
            LowestHealth = current;
        }

        _sequence[key] = (byte)(_sequence.GetValueOrDefault(key) + 1);
        R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(_sequence[key], key, current));
    }

    private void Line(string text) => R.H.ServerMessage(text, 4);

    private void Ready() =>
        R.H.R.Bus.Publish(new ObjectMotionChanged(WorldActionRig.Player, true, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = 0x0003 }));

    private static Dictionary<uint, SpellInfo> BuildTable()
    {
        var table = new Dictionary<uint, SpellInfo>();
        string[] numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];
        foreach (var line in new[] { VitalsLine.HealSelf, VitalsLine.RevitalizeSelf, VitalsLine.StaminaToManaSelf, VitalsLine.HarmSelf })
        {
            var ids = VitalsMeans.IdsOf(line);
            for (var i = 0; i < ids.Count; i++)
            {
                table[ids[i]] = WorldActionRig.Spell(ids[i], line == VitalsLine.StaminaToManaSelf ? SpellType.Transfer : SpellType.Boost, line == VitalsLine.HarmSelf ? SpellFlags.SelfTargeted : SpellFlags.Beneficial | SpellFlags.SelfTargeted) with
                {
                    Name = VitalsMeans.NameOf(line) + " " + numerals[i],
                    School = MagicSchool.LifeMagic,
                    Power = i == 0 ? 1u : 50u * (uint)i, // ArenaProfile's powers by level: 1, 50, 100 ... 300
                    BaseMana = 10u * (uint)(i + 1),
                };
            }
        }

        return table;
    }

    private void Describe()
    {
        var c = Character;
        var pack = new List<ContentDesc>();
        if (c.KitUses > 0)
        {
            Create(Kit, "Handy Healing Kit", FighterKit.HealingKitWcid, 0x80);
            pack.Add(new ContentDesc(Kit, 0));
        }

        if (c.Potions > 0)
        {
            Create(Potion, "Potion of Healing", FighterKit.PotionWcid, 0x80);
            pack.Add(new ContentDesc(Potion, 0));
        }

        if (c.ManaPotions > 0)
        {
            Create(ManaPotion, "Mana Potion", FighterKit.ManaPotionWcid, 0x80);
            pack.Add(new ContentDesc(ManaPotion, 0));
        }

        if (c.StaminaPotions > 0)
        {
            Create(StaminaPotion, "Stamina Potion", FighterKit.StaminaPotionWcid, 0x80);
            pack.Add(new ContentDesc(StaminaPotion, 0));
        }

        var equipped = new List<EquippedDesc>();
        if (c.Caster || c.CasterInPack)
        {
            var basis = ReducerHarness.Create(WandGuid, ReducerHarness.Stamps(), null, name: "Test Wand");
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x8000, WeenieClassId = GearKit.Wand.Wcid } }, isUpdate: false);
            if (c.Caster)
            {
                equipped.Add(new EquippedDesc(WandGuid, WorldActionRig.Wand, 0));
            }
            else
            {
                pack.Add(new ContentDesc(WandGuid, 0));
            }
        }

        var attributes = ImmutableArray.CreateBuilder<AttributeDesc>();
        foreach (var id in new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self })
        {
            attributes.Add(new AttributeDesc(id, 0, 100, 0));
        }

        // Healing (Focus + Coordination) / 3 = 66, plus 100 ranks; Life Magic (Focus + Self) / 4 = 50, plus the ranks.
        var skills = ImmutableArray.Create(
            new SkillDesc(RecoveryRules.HealingSkillId, 100, 1, c.HealingClass, 0, 0, 0, 0),
            new SkillDesc(MageTemplate.LifeMagic, c.LifeMagicRanks, 1, c.LifeMagicClass, 0, 0, 0, 0));

        // Maxima 200 each: health 150 ranks + Endurance 100 / 2, stamina 100 + Endurance 100, mana 100 + Self 100.
        var vitals = ImmutableArray.Create(
            new VitalDesc(MageTemplate.MaxHealth, c.MaxHealth - 50, 0, 0, Health),
            new VitalDesc(MageTemplate.MaxStamina, c.MaxStamina - 100, 0, 0, Stamina),
            new VitalDesc(MageTemplate.MaxMana, c.MaxMana - 100, 0, 0, Mana));
        R.H.Describe(new PlayerDescriptionMsg
        {
            Attributes = attributes.ToImmutable(),
            Skills = skills,
            Vitals = vitals,
            Spells = c.Spells.ToImmutableDictionary(s => s, _ => 2f),
            Inventory = [.. pack],
            Equipped = [.. equipped],
        });
        if (c.KitUses > 0)
        {
            R.Raw(AceM3Writers.PublicUpdatePropertyInt(++_seq, Kit, 92, c.KitUses));
        }

        if (c.Potions > 0)
        {
            R.Raw(AceM3Writers.SetStackSize(++_seq, Potion, c.Potions, c.Potions * 170));
        }

        if (c.ManaPotions > 0)
        {
            R.Raw(AceM3Writers.SetStackSize(++_seq, ManaPotion, c.ManaPotions, c.ManaPotions * 170));
        }

        if (c.StaminaPotions > 0)
        {
            R.Raw(AceM3Writers.SetStackSize(++_seq, StaminaPotion, c.StaminaPotions, c.StaminaPotions * 75));
        }
    }

    private void Create(uint guid, string name, uint wcid, uint itemType)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        R.H.R.Reducer.OnCreateObject(
            R.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid, ItemType = itemType } },
            isUpdate: false);
    }

    /// <summary>The session view over the rig.</summary>
    private sealed class View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    /// <summary>A movement controller that records the commands it takes and the posture they leave (an emote press holds it, the stop key or a locomotion command ends it).</summary>
    internal sealed class PostureSpy : IPlayerMovementIntents
    {
        public List<string> Calls { get; } = [];

        public uint Posture { get; private set; }

        public CellFrame Position => default;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f)
        {
            Calls.Add(FormattableString.Invariant($"0x{command:X8} {(start ? "down" : "up")}"));
            if (!start)
            {
                return;
            }

            Posture = command is VitalsKeeper.SitCommand or VitalsKeeper.LieCommand ? command : command == MotionCommands.Ready ? 0 : Posture;
        }

        public void SetHoldRun(bool on) => Calls.Add("run " + on);

        public void SetHoldSidestep(bool on) => Calls.Add("sidestep " + on);

        public void ToggleAutoRun() => Calls.Add("autorun");

        public void CommenceJump(double now) => Calls.Add("charge");

        public void DoJump(double now) => Calls.Add("jump");
    }
}

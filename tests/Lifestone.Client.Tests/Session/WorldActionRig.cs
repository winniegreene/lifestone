using System.Collections.Immutable;
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The Action API in the world with the server's side scripted from ACE's own writers (<see cref="AceM3Writers"/>, <see cref="AceWireWriters"/>) through the real
/// decoder, for the world actions' tracker tests (M3 1.2 s4b): <see cref="Server"/> feeds one message, <see cref="Tick"/> runs the Action API's update.
/// </summary>
internal sealed class WorldActionRig
{
    public const uint Player = ReducerHarness.PlayerGuid;
    public const uint Pack1 = 0x80000010;
    public const uint Sword = 0x80000101;
    public const uint Staff = 0x80000102;
    public const uint Gem = 0x80000104;
    public const uint Robe = 0x80000105;
    public const uint MainHand = 0x00100000;
    public const uint Wand = 0x01000000;

    private uint _event;
    private byte _private;

    public WorldActionRig(bool present = true)
    {
        H = new ActionHarness();
        if (present)
        {
            H.Present();
            H.Tick();
        }
    }

    public ActionHarness H { get; }

    public ActionApi Api => H.Api;

    /// <summary>One message from the server: ACE's bytes for the player, with the next event sequence.</summary>
    public void Server(Func<uint, uint, byte[]> body) => Raw(body(Player, ++_event));

    /// <summary>One message as ACE writes it (a GameMessage, or a GameEvent the caller addressed).</summary>
    public void Raw(byte[] message) => Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(H.R.Context, message, H.R.Reducer).Outcome);

    /// <summary>ACE's 0x02CD private int update of the player (the next sequence).</summary>
    public void PrivateInt(uint property, int value) => Raw(AceWireWriters.PrivateUpdatePropertyInt(++_private, property, value));

    /// <summary>The combat mode ACE announces (property 40).</summary>
    public void CombatModeIs(int mode) => PrivateInt(CombatModes.Property, mode);

    /// <summary>GameEvent 0x01C7 UseDone.</summary>
    public void UseDone(uint error = 0) => Server((p, s) => AceWireWriters.UseDone(p, s, error));

    /// <summary>GameEvent 0x028A WeenieError.</summary>
    public void WeenieError(uint error) => Server((p, s) => AceWireWriters.WeenieError(p, s, error));

    /// <summary>GameEvent 0x02C2 for an enchantment of the player (additive, beneficial, health).</summary>
    public void Enchant(ushort spell, ushort layer = 1) =>
        Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(spell, layer, 0x20, 0, 1, 0, 1800, Player, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 10f, 0)));

    /// <summary>
    /// A CreateObject of a projectile (the Missile state) at x metres in the player's cell, or another object when <paramref name="missile"/> is false. A projectile flies along +Y at 20 m/s unless
    /// <paramref name="velocity"/> says otherwise (the player stands at x 10, y 20 facing +Y, so by default it is the player's, launched along the player's heading); <paramref name="y"/> and
    /// <paramref name="setup"/> place and dress it.
    /// </summary>
    public void Create(uint guid, float x, bool missile = true, Vector3? velocity = null, float y = 20, uint setup = 0x02000001) =>
        H.R.Reducer.OnCreateObject(
            H.R.Context,
            ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD, x, y), state: missile ? PhysicsState.Missile | PhysicsState.Gravity : PhysicsState.Gravity, setup: setup, velocity: missile ? velocity ?? new Vector3(0, 20, 0) : null),
            isUpdate: false);

    /// <summary>A PlayerDescription with the lists.</summary>
    public void Describe(ContentDesc[]? pack = null, EquippedDesc[]? equipped = null) =>
        H.R.Reducer.OnPlayerDescription(
            H.R.Context,
            new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [.. pack ?? []], Equipped = [.. equipped ?? []] });

    /// <summary>The item's own CreateObject, held by <paramref name="container"/>.</summary>
    public void Item(uint guid, uint container)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Item");
        H.R.Reducer.OnCreateObject(
            H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = container } },
            isUpdate: false);
    }

    /// <summary>Advances the clock, then runs the update.</summary>
    public void Tick(double seconds = 0) => H.Tick(seconds);

    /// <summary>A spell of the SpellTable's shape, for a lookup.</summary>
    public static SpellInfo Spell(uint id, SpellType meta, SpellFlags flags = SpellFlags.None) => new()
    {
        Id = id,
        Name = "Spell " + id,
        Description = string.Empty,
        School = MagicSchool.WarMagic,
        IconId = 0,
        Category = 1,
        Flags = flags,
        BaseMana = 1,
        BaseRangeConstant = 0,
        BaseRangeMod = 0,
        Power = 1,
        SpellEconomyMod = 1,
        FormulaVersion = 1,
        ComponentLoss = 0,
        MetaSpellType = meta,
        MetaSpellId = id,
        EncryptedFormula = [],
        Formula = [],
        CasterEffect = PlayScript.Invalid,
        TargetEffect = PlayScript.Invalid,
        FizzleEffect = PlayScript.Invalid,
        RecoveryInterval = 0,
        RecoveryAmount = 0,
        DisplayOrder = 0,
        NonComponentTargetType = 0,
        ManaMod = 0,
    };

    /// <summary>Gives the Action API a spell table of these spells.</summary>
    public void Spells(params SpellInfo[] spells)
    {
        var byId = spells.ToImmutableDictionary(s => s.Id);
        Api.SpellLookup = id => byId.GetValueOrDefault(id);
    }
}

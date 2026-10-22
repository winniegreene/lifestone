using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The world with a player holding two healing kits (20 uses each) and a stack of five potions, and the server's side of a heal scripted from the orders the lead captured live (M4 3.1: cache/m4/3-1/capture.md C1 to C4).
/// Every feed goes through ACE's own writers and the real decoder, except the player's motion, which is published as the reducer would (the F74C's decoding is tested elsewhere).
/// </summary>
internal sealed class HealRig
{
    public const uint Player = WorldActionRig.Player;
    public const uint Kit = 0x80000A01;
    public const uint Kit2 = 0x80000A02;
    public const uint Potion = 0x80000A11;

    /// <summary>ACE's <c>PropertyAttribute2nd</c> ids of the current vitals (the 0x02E9 update).</summary>
    public const uint Health = 2;

    /// <summary>The current stamina's id.</summary>
    public const uint Stamina = 4;

    /// <summary>The SkillHealSelf command's low word (0x1000010E).</summary>
    public const ushort SkillHealSelf = 0x010E;

    /// <summary>The Drink command's low word (0x4000001B).</summary>
    public const ushort Drink = 0x001B;

    private uint _event = 100;
    private byte _seq;

    public HealRig(int uses = 20, uint potions = 5)
    {
        R = new WorldActionRig();
        R.Describe([new ContentDesc(Kit, 0), new ContentDesc(Kit2, 0), new ContentDesc(Potion, 0)], []);
        R.Item(Kit, Player);
        R.Item(Kit2, Player);
        R.Item(Potion, Player);
        Structure(Kit, uses);
        Structure(Kit2, uses);
        Stack(Potion, potions);
    }

    public WorldActionRig R { get; }

    public ActionApi Api => R.Api;

    public int Sent(FakeSendKind kind) => R.H.Net.Of(kind).Count;

    /// <summary>Advances the clock, then runs the Action API's update.</summary>
    public void Tick(double seconds = 0) => R.Tick(seconds);

    /// <summary>GameMessage 0x02CE: the kit's Structure (the uses left).</summary>
    public void Structure(uint kit, int uses) => R.Raw(AceM3Writers.PublicUpdatePropertyInt(++_seq, kit, 92, uses));

    /// <summary>GameMessage 0x0197: a stack's size.</summary>
    public void Stack(uint item, uint size) => R.Raw(AceM3Writers.SetStackSize(++_seq, item, size, size * 170));

    /// <summary>GameMessage 0x0024: the item is gone (the last use of a kit, the last potion).</summary>
    public void Removed(uint item) => R.Raw(AceM3Writers.InventoryRemoveObject(item));

    /// <summary>GameEvent 0x02E9: a current vital of the player.</summary>
    public void Vital(uint vital, uint current) => R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_seq, vital, current));

    /// <summary>F7E0 ServerMessage in the broadcast type, as ACE sends the heal and potion lines.</summary>
    public void Line(string text) => R.H.ServerMessage(text, 4);

    /// <summary>GameEvent 0x01C7 UseDone.</summary>
    public void UseDone(uint code = 0) => R.UseDone(code);

    /// <summary>GameEvent 0x028A WeenieError.</summary>
    public void Error(uint code) => R.Raw(AceWireWriters.WeenieError(Player, ++_event, code));

    /// <summary>GameEvent 0x028B WeenieErrorWithString.</summary>
    public void ErrorText(uint code, string text) => R.Raw(AceWireWriters.WeenieErrorWithString(Player, ++_event, code, text));

    /// <summary>GameEvent 0x02EB CommunicationTransientString.</summary>
    public void Transient(string text) => R.Raw(AceWireWriters.TransientString(Player, ++_event, text));

    /// <summary>The player's own F74C carrying <paramref name="command"/> (the kit's SkillHealSelf, the potion's Drink): forward Ready, one command.</summary>
    public void ActionMotion(ushort command) =>
        R.H.R.Bus.Publish(new ObjectMotionChanged(Player, true, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = 0x0003, Commands = [new MotionItem(command, 1, false, 1f)] }));

    /// <summary>The player's own Ready F74C: forward Ready, no command (capture C1: "F74C Ready (no command)").</summary>
    public void Ready() =>
        R.H.R.Bus.Publish(new ObjectMotionChanged(Player, true, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = 0x0003 }));

    /// <summary>The kit heal, queued and sent: the send is at the current clock time.</summary>
    public HealHandle HealSelf(uint kit = Kit, ActionSource source = ActionSource.Ui)
    {
        var handle = Api.UseWithTarget(kit, Player, source);
        Tick();
        return handle;
    }

    /// <summary>The potion, queued and sent.</summary>
    public HealHandle DrinkPotion(ActionSource source = ActionSource.Ui)
    {
        var handle = Api.Drink(Potion, source);
        Tick();
        return handle;
    }
}

using System.Collections.Immutable;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The stance rules of M3 4.2 p3 over an offline session (no dats): the portal-space refusal and <c>CompatibleCombatMode</c> in <see cref="CombatModeRules"/> (<c>ClientCombatSystem::SetCombatMode</c> 0x0056CB80, 0x0056B2C0), each with the line
/// retail prints and no mode change asked of the session.
/// </summary>
public sealed class StanceRulesTests
{
    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();
        public IInboundMessageHandler? Handler;
        public GameSession Session { get; }

        public Rig()
        {
            var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 };
            Session = GameSession.Offline(
                h =>
                {
                    Handler = h;
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, visit);
                    StandInSessions.AnswerWorld(h, visit);
                },
                _time,
                new RecordingLogSink());
            Run(2.0);
        }

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public void Equip(params (uint Guid, EquipMask Slot, string Name, uint ItemType)[] items)
        {
            var player = Session.State.Self.Guid;
            Handler!.OnPlayerDescription(Context, new GameEventHeader(player, ++_seq, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Equipped = [.. items.Select(static i => new EquippedDesc(i.Guid, (uint)i.Slot, 0))] });
            foreach (var item in items)
            {
                var create = ReducerHarness.Create(item.Guid, ReducerHarness.Stamps(), null, name: item.Name);
                Handler.OnCreateObject(Context, create with { Weenie = create.Weenie with { ItemType = item.ItemType } }, isUpdate: false);
            }
        }

        public int SetModeAsked()
        {
            var n = 0;
            for (var i = 0; i < Session.ActionLog.Count; i++)
            {
                n += Session.ActionLog[i].Kind == ActionKind.SetCombatMode ? 1 : 0;
            }

            return n;
        }

        private uint _seq = 10;

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void A_combat_press_while_a_teleport_is_in_portal_space_says_retails_line_and_asks_nothing_and_after_arrival_asks()
    {
        using var r = new Rig();
        Assert.Equal(SelfTravel.Present, r.Session.State.Self.Travel);
        r.Handler!.OnPlayerTeleport(Context, new PlayerTeleportMsg(2));
        Assert.True(CombatModeRules.InPortalSpace(r.Session.State.Self.Travel), r.Session.State.Self.Travel.ToString());

        var scene = new UiScene([]);
        var chat = new List<string>();
        Assert.Null(CombatModeRules.Toggle(r.Session, scene.Manager, chat.Add));
        Assert.Equal(["You can't enter combat mode while in portal space"], chat);
        r.Run(1.0);
        Assert.Equal(0, r.SetModeAsked());
        Assert.Contains(scene.Log.Lines, static l => l.Contains("portal space", StringComparison.Ordinal));
    }

    [Fact]
    public void Compatible_combat_mode_follows_the_inventory_mask_and_a_refusal_names_the_wielded_item()
    {
        using var r = new Rig();
        var s = r.Session.State;
        Assert.True(CombatModeRules.Compatible(s, CombatMode.Melee, out _)); // fists
        Assert.True(CombatModeRules.Compatible(s, CombatMode.NonCombat, out _));
        Assert.False(CombatModeRules.Compatible(s, CombatMode.Missile, out _));
        Assert.False(CombatModeRules.Compatible(s, CombatMode.Magic, out _));

        r.Equip((0x80000801, EquipMask.MissileWeapon, "Short Bow", 0x100));
        Assert.True(CombatModeRules.Compatible(s, CombatMode.Missile, out _));
        Assert.False(CombatModeRules.Compatible(s, CombatMode.Melee, out var name)); // something is wielded, but not a melee weapon
        Assert.Equal("Short Bow", name);
        Assert.False(CombatModeRules.Compatible(s, CombatMode.Magic, out _));

        r.Equip((0x80000802, EquipMask.Held, "Wand", CombatModeRules.ItemTypeCaster));
        Assert.True(CombatModeRules.Compatible(s, CombatMode.Magic, out _));
        Assert.False(CombatModeRules.Compatible(s, CombatMode.Missile, out _));
        Assert.False(CombatModeRules.Compatible(s, CombatMode.Melee, out name));
        Assert.Equal("Wand", name);
        Assert.Equal("combat", CombatModeRules.ModeWord(CombatMode.NonCombat));
        Assert.Equal("melee", CombatModeRules.ModeWord(CombatMode.Melee));
    }
}

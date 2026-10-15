using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>The world-content census: what kind of thing each object the server creates is, and what a world holds (m2-plan G5 W10).</summary>
public sealed class WorldContentCensusTests
{
    private const uint Cell = 0x860201AD;
    private const uint Creature = 0x10;
    private const uint Misc = 0x80;

    private static WeenieDesc Weenie(string name, uint itemType, uint flags) =>
        new() { Header = WeenieHeaderFlags.None, Name = name, WeenieClassId = 1, IconId = 0x06000001, ItemType = itemType, ObjectDescriptionFlags = flags };

    [Theory]
    [InlineData("Portal to Holtburg", Misc, 0x40000u, WorldContentKind.Portal)]
    [InlineData("Lifestone", Misc, 0x4000u, WorldContentKind.Lifestone)]
    [InlineData("Door", Misc, 0x1000u | 0x1u, WorldContentKind.Door)]
    [InlineData("Corpse of a Drudge", Misc, 0x2000u, WorldContentKind.Corpse)]
    [InlineData("Another Adventurer", Creature, 0x8u | 0x10u, WorldContentKind.Player)]
    [InlineData("Drudge Skulker", Creature, 0x10u, WorldContentKind.Creature)]
    [InlineData("Aerlinthe Guide", Creature, 0x0u, WorldContentKind.Npc)]
    [InlineData("Weapons Merchant", Creature, 0x10u | 0x200u, WorldContentKind.Npc)]
    [InlineData("Holtburg Sign", Misc, 0x4u | 0x2u, WorldContentKind.Sign)]
    [InlineData("Statue", 0u, 0x4u, WorldContentKind.Other)]
    [InlineData("Training Sword", 0x1u, 0x0u, WorldContentKind.Item)]
    public void Each_kind_is_told_from_the_weenie_description(string name, uint itemType, uint flags, WorldContentKind expected) =>
        Assert.Equal(expected, WorldContentCensus.Classify(Weenie(name, itemType, flags)));

    [Fact]
    public void The_census_counts_placed_objects_other_than_the_player_and_leaves_the_pack_out()
    {
        var h = new ReducerHarness();
        h.EnterWorld(Cell);
        uint guid = 0x80000200;
        void Add(string name, uint itemType, uint flags, bool placed = true)
        {
            var msg = Create(guid++, Stamps(), placed ? Position(Cell) : null, name: name);
            h.Reducer.OnCreateObject(h.Context, msg with { Weenie = msg.Weenie with { ItemType = itemType, ObjectDescriptionFlags = flags } }, false);
        }

        Add("Guide", Creature, 0);
        Add("Guide Two", Creature, 0);
        Add("Rat", Creature, 0x10);
        Add("Exit Portal", Misc, 0x40000);
        Add("Door", Misc, 0x1000);
        Add("Door", Misc, 0x1000);
        Add("Door", Misc, 0x1000);
        Add("Lifestone", Misc, 0x4000);
        Add("Training Sign", Misc, 0x4);
        Add("Dagger", 0x1, 0);
        Add("Pack Dagger", 0x1, 0, placed: false);

        var census = WorldContentCensus.Count(h.State);
        Assert.Equal(new WorldContent(Npcs: 2, Creatures: 1, Players: 0, Portals: 1, Doors: 3, Lifestones: 1, Signs: 1, Corpses: 0, Items: 1, Others: 0), census);
        Assert.Equal(10, census.Total);
        Assert.Equal("npcs=2 creatures=1 players=0 portals=1 doors=3 lifestones=1 signs=1 corpses=0 items=1 others=0", census.Describe());
    }
}

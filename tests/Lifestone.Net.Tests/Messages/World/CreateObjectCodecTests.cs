using System.Buffers.Binary;
using System.Numerics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.World;

/// <summary>
/// F745 ObjectCreate and F7DB UpdateObject against ACE's <c>SerializeCreateObject</c> (<see cref="AceWireWriters"/>,
/// m2-plan §3.3 N7): the four shapes ACE sends most (the player, an NPC, a static with an AnimationFrame, a wielded item
/// with a Parent), every PhysicsDesc field and every WeenieDesc optional field one flag at a time, the second header,
/// the house restrictions, and the movement buffer, whose body must fill its length exactly.
/// </summary>
public sealed class CreateObjectCodecTests
{
    private static readonly AcePosition Holtburg = new(0xA9B40019, 84f, 7.1f, 94f, 0.996917f, 0f, 0f, -0.0784591f);

    /// <summary>The player as ACE describes it on login (the fake server's shape, plus the movement ACE adds).</summary>
    internal static AceCreateObject Player => new()
    {
        Guid = 0x50000001,
        PhysicsState = 0x00404410,  // IgnoreCollisions | Gravity | Hidden | EdgeSlide: the pink bubble
        Movement = new AceMovementData { Forward = 0x0003 },
        MovementIsAutonomous = false,
        Position = Holtburg,
        MotionTable = 0x09000001,
        SoundTable = 0x20000001,
        PhysicsEffectTable = 0x34000004,
        Setup = 0x02000001,
        ObjScale = 1f,
        Sequences = [1, 2, 3, 4, 5, 6, 7, 8, 9],
        BasePalette = 0x0400007E,
        SubPalettes = [(0x04001071, 0, 24), (0x040002BC, 24, 8)],
        TextureChanges = [(16, 0x05000BE8, 0x0500186E)],
        AnimPartChanges = [(16, 0x01001F9D), (9, 0x0100005C)],
        Name = "Hero",
        WeenieClassId = 1,
        Icon = 0x06001036,
        ItemType = 0x10,
        ObjectDescriptionFlags = 0x8 | 0x1000000,
        Usable = 0x1,
        UseRadius = 0f,
        RadarBlipColor = 9,
        RadarBehavior = 4,
    };

    [Fact]
    public void The_player_round_trips()
    {
        var body = AceWireWriters.ObjectCreate(Player);

        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create));
        Assert.Equal(0x50000001u, create.Guid);

        var model = create.Model;
        Assert.Equal(0x0400007Eu, model.BasePaletteId);
        Assert.Equal(new[] { new SubPaletteSwap(0x04001071, 0, 24), new SubPaletteSwap(0x040002BC, 24, 8) }, model.SubPalettes);
        Assert.Equal(new[] { new TextureChange(16, 0x05000BE8, 0x0500186E) }, model.TextureChanges);
        Assert.Equal(new[] { new AnimPartChange(16, 0x01001F9D), new AnimPartChange(9, 0x0100005C) }, model.AnimPartChanges);

        var physics = create.Physics;
        Assert.Equal(
            PhysicsDescFlags.Movement | PhysicsDescFlags.Position | PhysicsDescFlags.MTable | PhysicsDescFlags.STable | PhysicsDescFlags.PeTable
            | PhysicsDescFlags.CSetup | PhysicsDescFlags.ObjScale,
            physics.Flags);
        Assert.Equal(PhysicsState.IgnoreCollisions | PhysicsState.Gravity | PhysicsState.Hidden | PhysicsState.EdgeSlide, physics.State);
        Assert.Equal((ushort)0x0003, physics.Movement!.Forward);
        Assert.False(physics.MovementIsAutonomous);
        Assert.Null(physics.AnimationFrame);
        Assert.Equal(new AcPosition(0xA9B40019, new Vector3(84f, 7.1f, 94f), new Quaternion(0f, 0f, -0.0784591f, 0.996917f)), physics.Position);
        Assert.Equal((0x09000001u, 0x20000001u, 0x34000004u, 0x02000001u), (physics.MotionTableId!.Value, physics.SoundTableId!.Value, physics.PhysicsEffectTableId!.Value, physics.SetupId!.Value));
        Assert.Equal(1f, physics.Scale);
        Assert.Equal(new PhysicsTimestamps(1, 2, 3, 4, 5, 6, 7, 8, 9), physics.Timestamps);

        var weenie = create.Weenie;
        Assert.Equal("Hero", weenie.Name);
        Assert.Equal((1u, 0x06001036u, 0x10u), (weenie.WeenieClassId, weenie.IconId, weenie.ItemType));
        Assert.Equal(0x8u | 0x1000000u, weenie.ObjectDescriptionFlags);
        Assert.Equal(WeenieHeaderFlags.Usable | WeenieHeaderFlags.UseRadius | WeenieHeaderFlags.RadarBlipColor | WeenieHeaderFlags.RadarBehavior, weenie.Header);
        Assert.Equal(WeenieHeaderFlags2.None, weenie.Header2);
        Assert.Equal((1u, 0f, (byte)9, (byte)4), (weenie.Usable!.Value, weenie.UseRadius!.Value, weenie.RadarBlipColor!.Value, weenie.RadarBehavior!.Value));
        Assert.Null(weenie.Value);

        ObjectEventCodecTests.AssertExact(body, static b => CreateObjectCodec.TryParse(b, MessageOpcodes.ObjectCreate, out _));
    }

    [Fact]
    public void An_npc_with_motion_commands_and_a_plural_name_round_trips()
    {
        var npc = new AceCreateObject
        {
            Guid = 0x7A9B4001,
            PhysicsState = 0x00000408,
            Movement = new AceMovementData { Stance = 0x003D, Forward = 0x0003, Commands = [new AceMotionItem(0x0087, 3, Autonomous: false)] },
            MovementIsAutonomous = true,
            Position = Holtburg with { X = 90f },
            MotionTable = 0x09000001,
            Setup = 0x02000001,
            Sequences = new ushort[9],
            Name = "Town Crier",
            PluralName = "Town Criers",
            WeenieClassId = 0x8000,  // two u16s as a PackedDword
            Icon = 0x06001036,
            ItemType = 0x10,
            ObjectDescriptionFlags = 0x10,
        };
        var body = AceWireWriters.ObjectCreate(npc);

        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create));
        Assert.True(create.Physics.MovementIsAutonomous);
        Assert.Equal(new[] { new MotionItem(0x0087, 3, false, 1f) }, create.Physics.Movement!.Commands);
        Assert.Equal("Town Criers", create.Weenie.PluralName);
        Assert.Equal(0x8000u, create.Weenie.WeenieClassId);
        ObjectEventCodecTests.AssertExact(body, static b => CreateObjectCodec.TryParse(b, MessageOpcodes.ObjectCreate, out _));
    }

    [Fact]
    public void A_static_with_an_AnimationFrame_round_trips()
    {
        var lifestone = new AceCreateObject
        {
            Guid = 0x7A9B4002,
            PhysicsState = 0x1,
            AnimationFrame = 0x65,
            Position = Holtburg,
            Setup = 0x020002EE,
            DefaultScript = 0x1F,
            DefaultScriptIntensity = 0.5f,
            Sequences = new ushort[9],
            Name = "Life Stone",
            WeenieClassId = 509,
            Icon = 0x06001355,
            ItemType = 0x10000000,
            ObjectDescriptionFlags = 0x14,
            Usable = 0x20,
        };
        var body = AceWireWriters.ObjectCreate(lifestone);

        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create));
        Assert.Equal(PhysicsDescFlags.AnimationFrame, create.Physics.Flags & (PhysicsDescFlags.AnimationFrame | PhysicsDescFlags.Movement));
        Assert.Equal(0x65u, create.Physics.AnimationFrame);
        Assert.Null(create.Physics.Movement);
        Assert.Equal((0x1Fu, 0.5f), (create.Physics.DefaultScript!.Value, create.Physics.DefaultScriptIntensity!.Value));
        Assert.Equal(PhysicsState.Static, create.Physics.State);
        ObjectEventCodecTests.AssertExact(body, static b => CreateObjectCodec.TryParse(b, MessageOpcodes.ObjectCreate, out _));
    }

    [Fact]
    public void A_wielded_item_with_a_parent_round_trips()
    {
        var sword = new AceCreateObject
        {
            Guid = 0x80000123,
            PhysicsState = 0x00000020,
            AnimationFrame = 0x65,
            Parent = (0x50000001, 1),
            Setup = 0x02000163,
            Sequences = new ushort[9],
            Name = "Training Sword",
            WeenieClassId = 12345,
            Icon = 0x06001F2B,
            ItemType = 0x1,
            ObjectDescriptionFlags = 0x10,
            Wielder = 0x50000001,
            ValidLocations = 0x100000,
            CurrentlyWieldedLocation = 0x100000,
            Priority = 0,
            Burden = 450,
        };
        var body = AceWireWriters.ObjectCreate(sword);

        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create));
        Assert.Equal(new ObjectLink(0x50000001, 1), create.Physics.Parent);
        Assert.Null(create.Physics.Position);
        Assert.Equal((0x50000001u, 0x100000u, 0x100000u, 0u, (ushort)450), (create.Weenie.WielderId!.Value, create.Weenie.ValidLocations!.Value, create.Weenie.CurrentlyWieldedLocation!.Value, create.Weenie.Priority!.Value, create.Weenie.Burden!.Value));
        ObjectEventCodecTests.AssertExact(body, static b => CreateObjectCodec.TryParse(b, MessageOpcodes.ObjectCreate, out _));
    }

    [Fact]
    public void UpdateObject_is_the_same_body_under_F7DB()
    {
        var body = AceWireWriters.UpdateObject(Player);
        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.UpdateObject, out var update));
        Assert.Equal("Hero", update.Weenie.Name);
        Assert.False(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out _));
        Assert.Equal(AceWireWriters.ObjectCreate(Player)[4..], body[4..]);
    }

    /// <summary>Each PhysicsDesc field alone (with the fields every create needs), in ACE's write order.</summary>
    public static TheoryData<string> PhysicsFields => new()
    {
        "Movement", "AnimationFrame", "Position", "MTable", "STable", "PeTable", "CSetup", "Parent", "Children", "ObjScale", "Friction",
        "Elasticity", "Translucency", "Velocity", "Acceleration", "Omega", "DefaultScript", "DefaultScriptIntensity",
    };

    [Theory]
    [MemberData(nameof(PhysicsFields))]
    public void Every_PhysicsDesc_field_round_trips_alone(string field)
    {
        var desc = field switch
        {
            "Movement" => Minimal with { Movement = new AceMovementData { MovementType = 9, ParameterFlags = 1, Speed = 2f, DesiredHeading = 3f }, MovementIsAutonomous = true },
            "AnimationFrame" => Minimal with { AnimationFrame = 0x12345678 },
            "Position" => Minimal with { Position = Holtburg },
            "MTable" => Minimal with { MotionTable = 0x09000002 },
            "STable" => Minimal with { SoundTable = 0x20000002 },
            "PeTable" => Minimal with { PhysicsEffectTable = 0x34000002 },
            "CSetup" => Minimal with { Setup = 0x02000002 },
            "Parent" => Minimal with { Parent = (0x50000002, 3) },
            "Children" => Minimal with { Children = [(0x80000001, 1), (0x80000002, 2)] },
            "ObjScale" => Minimal with { ObjScale = 1.5f },
            "Friction" => Minimal with { Friction = 0.95f },
            "Elasticity" => Minimal with { Elasticity = 0.05f },
            "Translucency" => Minimal with { Translucency = 0.5f },
            "Velocity" => Minimal with { Velocity = (1f, 2f, 3f) },
            "Acceleration" => Minimal with { Acceleration = (0f, 0f, -9.8f) },
            "Omega" => Minimal with { Omega = (0f, 0f, 1f) },
            "DefaultScript" => Minimal with { DefaultScript = 0x24 },
            "DefaultScriptIntensity" => Minimal with { DefaultScriptIntensity = 0.75f },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var body = AceWireWriters.ObjectCreate(desc);

        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create), field);
        var p = create.Physics;
        object? read = field switch
        {
            "Movement" => (p.Movement!.TurnTo, p.MovementIsAutonomous),
            "AnimationFrame" => p.AnimationFrame,
            "Position" => p.Position,
            "MTable" => p.MotionTableId,
            "STable" => p.SoundTableId,
            "PeTable" => p.PhysicsEffectTableId,
            "CSetup" => p.SetupId,
            "Parent" => p.Parent,
            "Children" => string.Join(",", p.Children),
            "ObjScale" => p.Scale,
            "Friction" => p.Friction,
            "Elasticity" => p.Elasticity,
            "Translucency" => p.Translucency,
            "Velocity" => p.Velocity,
            "Acceleration" => p.Acceleration,
            "Omega" => p.Omega,
            "DefaultScript" => p.DefaultScript,
            "DefaultScriptIntensity" => p.DefaultScriptIntensity,
            _ => null,
        };
        object expected = field switch
        {
            "Movement" => ((TurnToData?)new TurnToData(null, null, new TurnToParameters(1, 2f, 3f)), true),
            "AnimationFrame" => 0x12345678u,
            "Position" => new AcPosition(0xA9B40019, new Vector3(84f, 7.1f, 94f), new Quaternion(0f, 0f, -0.0784591f, 0.996917f)),
            "MTable" => 0x09000002u,
            "STable" => 0x20000002u,
            "PeTable" => 0x34000002u,
            "CSetup" => 0x02000002u,
            "Parent" => new ObjectLink(0x50000002, 3),
            "Children" => string.Join(",", new[] { new ObjectLink(0x80000001, 1), new ObjectLink(0x80000002, 2) }),
            "ObjScale" => 1.5f,
            "Friction" => 0.95f,
            "Elasticity" => 0.05f,
            "Translucency" => 0.5f,
            "Velocity" => new Vector3(1f, 2f, 3f),
            "Acceleration" => new Vector3(0f, 0f, -9.8f),
            "Omega" => new Vector3(0f, 0f, 1f),
            "DefaultScript" => 0x24u,
            "DefaultScriptIntensity" => 0.75f,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.Equal(expected, read);
        Assert.Equal(1, System.Numerics.BitOperations.PopCount((uint)p.Flags));
        ObjectEventCodecTests.AssertExact(body, static b => CreateObjectCodec.TryParse(b, MessageOpcodes.ObjectCreate, out _));
    }

    /// <summary>Each WeenieDesc optional field alone, with its first or second header flag.</summary>
    public static TheoryData<string> WeenieFields => new()
    {
        "PluralName", "ItemsCapacity", "ContainersCapacity", "AmmoType", "Value", "Usable", "UseRadius", "TargetType", "UiEffects",
        "CombatUse", "Structure", "MaxStructure", "StackSize", "MaxStackSize", "Container", "Wielder", "ValidLocations",
        "CurrentlyWieldedLocation", "Priority", "RadarBlipColor", "RadarBehavior", "PScript", "Workmanship", "Burden", "Spell",
        "HouseOwner", "HouseRestrictions", "HookItemTypes", "Monarch", "HookType", "IconOverlay", "IconUnderlay", "MaterialType",
        "CooldownId", "CooldownDuration", "PetOwner",
    };

    [Theory]
    [MemberData(nameof(WeenieFields))]
    public void Every_WeenieDesc_field_round_trips_alone(string field)
    {
        var restrictions = new AceRestrictionDb(0x10000002, 1, 0x50000009, [(0x50000003, 1), (0x50000004, 0)]);
        var desc = field switch
        {
            "PluralName" => Minimal with { PluralName = "Pyreals" },
            "ItemsCapacity" => Minimal with { ItemsCapacity = 102 },
            "ContainersCapacity" => Minimal with { ContainersCapacity = 7 },
            "AmmoType" => Minimal with { AmmoType = 2 },
            "Value" => Minimal with { Value = -25 },
            "Usable" => Minimal with { Usable = 0x80000 },
            "UseRadius" => Minimal with { UseRadius = 3.5f },
            "TargetType" => Minimal with { TargetType = 0x10 },
            "UiEffects" => Minimal with { UiEffects = 0x20 },
            "CombatUse" => Minimal with { CombatUse = -1 },
            "Structure" => Minimal with { Structure = 40 },
            "MaxStructure" => Minimal with { MaxStructure = 50 },
            "StackSize" => Minimal with { StackSize = 25000 },
            "MaxStackSize" => Minimal with { MaxStackSize = 65535 },
            "Container" => Minimal with { Container = 0x50000001 },
            "Wielder" => Minimal with { Wielder = 0x50000001 },
            "ValidLocations" => Minimal with { ValidLocations = 0x3F07FFF },
            "CurrentlyWieldedLocation" => Minimal with { CurrentlyWieldedLocation = 0x200 },
            "Priority" => Minimal with { Priority = 0x1C },
            "RadarBlipColor" => Minimal with { RadarBlipColor = 12 },
            "RadarBehavior" => Minimal with { RadarBehavior = 2 },
            "PScript" => Minimal with { PScript = 0x8A },
            "Workmanship" => Minimal with { Workmanship = 7.5f },
            "Burden" => Minimal with { Burden = 1200 },
            "Spell" => Minimal with { Spell = 2345 },
            "HouseOwner" => Minimal with { HouseOwner = 0x50000005 },
            "HouseRestrictions" => Minimal with { HouseRestrictions = restrictions },
            "HookItemTypes" => Minimal with { HookItemTypes = 0x7 },
            "Monarch" => Minimal with { Monarch = 0x50000006 },
            "HookType" => Minimal with { HookType = 4 },
            "IconOverlay" => Minimal with { IconOverlay = 0x06005B0C },
            "IconUnderlay" => Minimal with { IconUnderlay = 0x0600109A },
            "MaterialType" => Minimal with { MaterialType = 0x3D },
            "CooldownId" => Minimal with { CooldownId = -3 },
            "CooldownDuration" => Minimal with { CooldownDuration = 12.5 },
            "PetOwner" => Minimal with { PetOwner = 0x50000007 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var body = AceWireWriters.ObjectCreate(desc);

        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create), field);
        var w = create.Weenie;
        object? read = field switch
        {
            "PluralName" => w.PluralName,
            "ItemsCapacity" => w.ItemsCapacity,
            "ContainersCapacity" => w.ContainersCapacity,
            "AmmoType" => w.AmmoType,
            "Value" => w.Value,
            "Usable" => w.Usable,
            "UseRadius" => w.UseRadius,
            "TargetType" => w.TargetType,
            "UiEffects" => w.UiEffects,
            "CombatUse" => w.CombatUse,
            "Structure" => w.Structure,
            "MaxStructure" => w.MaxStructure,
            "StackSize" => w.StackSize,
            "MaxStackSize" => w.MaxStackSize,
            "Container" => w.ContainerId,
            "Wielder" => w.WielderId,
            "ValidLocations" => w.ValidLocations,
            "CurrentlyWieldedLocation" => w.CurrentlyWieldedLocation,
            "Priority" => w.Priority,
            "RadarBlipColor" => w.RadarBlipColor,
            "RadarBehavior" => w.RadarBehavior,
            "PScript" => w.PhysicsScript,
            "Workmanship" => w.Workmanship,
            "Burden" => w.Burden,
            "Spell" => w.SpellId,
            "HouseOwner" => w.HouseOwnerId,
            "HouseRestrictions" => w.HouseRestrictions is { } r ? (r.Version, r.OpenStatus, r.MonarchId, r.BucketCount, string.Join(",", r.Guests)) : null,
            "HookItemTypes" => w.HookItemTypes,
            "Monarch" => w.MonarchId,
            "HookType" => w.HookType,
            "IconOverlay" => w.IconOverlayId,
            "IconUnderlay" => w.IconUnderlayId,
            "MaterialType" => w.MaterialType,
            "CooldownId" => w.CooldownId,
            "CooldownDuration" => w.CooldownDuration,
            "PetOwner" => w.PetOwnerId,
            _ => null,
        };
        object expected = field switch
        {
            "PluralName" => "Pyreals",
            "ItemsCapacity" => (byte)102,
            "ContainersCapacity" => (byte)7,
            "AmmoType" => (ushort)2,
            "Value" => unchecked((uint)-25),
            "Usable" => 0x80000u,
            "UseRadius" => 3.5f,
            "TargetType" => 0x10u,
            "UiEffects" => 0x20u,
            "CombatUse" => (sbyte)-1,
            "Structure" => (ushort)40,
            "MaxStructure" => (ushort)50,
            "StackSize" => (ushort)25000,
            "MaxStackSize" => (ushort)65535,
            "Container" => 0x50000001u,
            "Wielder" => 0x50000001u,
            "ValidLocations" => 0x3F07FFFu,
            "CurrentlyWieldedLocation" => 0x200u,
            "Priority" => 0x1Cu,
            "RadarBlipColor" => (byte)12,
            "RadarBehavior" => (byte)2,
            "PScript" => (ushort)0x8A,
            "Workmanship" => 7.5f,
            "Burden" => (ushort)1200,
            "Spell" => (ushort)2345,
            "HouseOwner" => 0x50000005u,
            "HouseRestrictions" => (0x10000002u, 1u, 0x50000009u, (ushort)768, string.Join(",", new[] { new HouseGuest(0x50000003, 1), new HouseGuest(0x50000004, 0) })),
            "HookItemTypes" => 0x7u,
            "Monarch" => 0x50000006u,
            "HookType" => (ushort)4,
            "IconOverlay" => 0x06005B0Cu,
            "IconUnderlay" => 0x0600109Au,
            "MaterialType" => 0x3Du,
            "CooldownId" => -3,
            "CooldownDuration" => 12.5,
            "PetOwner" => 0x50000007u,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.Equal(expected, read);

        var secondHeader = field is "IconUnderlay" or "CooldownId" or "CooldownDuration" or "PetOwner";
        Assert.Equal(secondHeader, (w.ObjectDescriptionFlags & WeenieDesc.IncludesSecondHeader) != 0);
        Assert.Equal(1, System.Numerics.BitOperations.PopCount((uint)w.Header) + System.Numerics.BitOperations.PopCount((uint)w.Header2));
        ObjectEventCodecTests.AssertExact(body, static b => CreateObjectCodec.TryParse(b, MessageOpcodes.ObjectCreate, out _));
    }

    [Fact]
    public void A_second_header_of_zero_is_still_read_when_its_bit_is_set()
    {
        // ACE sets bit 0x04000000 only with a non-zero second header; retail reads the word whenever the bit is set.
        var body = AceWireWriters.ObjectCreate(Minimal);
        var flagsAt = WeenieDescriptionFlagsOffset(body);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(flagsAt));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(flagsAt), flags | WeenieDesc.IncludesSecondHeader);
        Assert.False(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out _));

        byte[] withWord = [.. body, 0, 0, 0, 0];
        Assert.True(CreateObjectCodec.TryParse(withWord, MessageOpcodes.ObjectCreate, out var create));
        Assert.Equal(WeenieHeaderFlags2.None, create.Weenie.Header2);
    }

    [Fact]
    public void A_movement_body_that_does_not_fill_its_length_is_refused()
    {
        var body = AceWireWriters.ObjectCreate(Minimal with { Movement = new AceMovementData { Forward = 0x0003 } });
        var lengthAt = 4 + 4 + 4 + 8;  // opcode, GUID, empty ModelData, PhysicsDesc flags and state
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(lengthAt));
        Assert.Equal(12u, length);  // type, flags, stance, the state word, the forward command, the pad

        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(lengthAt), length + 4);
        Assert.False(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out _));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(lengthAt), length - 4);
        Assert.False(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out _));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(lengthAt), uint.MaxValue);
        Assert.False(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out _));
    }

    [Fact]
    public void A_sticky_movement_inside_a_create_is_aligned_like_UpdateMotion()
    {
        var body = AceWireWriters.ObjectCreate(Minimal with { Movement = new AceMovementData { MotionFlags = 0x1, Forward = 0x0007, StickyGuid = 0x80001234 } });
        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create));
        Assert.Equal(0x80001234u, create.Physics.Movement!.StickyGuid);
    }

    [Fact]
    public void An_empty_movement_flag_carries_no_autonomous_word()
    {
        // Movement flagged with length 0: retail and ACE then read no body and no is-autonomous word.
        var body = AceWireWriters.ObjectCreate(Minimal);
        var flagsAt = 4 + 4 + 4;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(flagsAt), (uint)PhysicsDescFlags.Movement);
        byte[] withLength = [.. body[..(flagsAt + 8)], 0, 0, 0, 0, .. body[(flagsAt + 8)..]];

        Assert.True(CreateObjectCodec.TryParse(withLength, MessageOpcodes.ObjectCreate, out var create));
        Assert.Equal(PhysicsDescFlags.Movement, create.Physics.Flags);
        Assert.Null(create.Physics.Movement);
        Assert.False(create.Physics.MovementIsAutonomous);
    }

    [Fact]
    public void A_children_count_larger_than_the_message_is_refused_before_allocating()
    {
        var body = AceWireWriters.ObjectCreate(Minimal with { Children = [(1, 1)] });
        var countAt = 4 + 4 + 4 + 8;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(countAt), int.MaxValue);
        Assert.False(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out _));
    }

    [Fact]
    public void A_string_with_windows_1252_characters_decodes_as_ace_wrote_it()
    {
        var body = AceWireWriters.ObjectCreate(Minimal with { Name = "Ëmberglass Ñecklace" });
        Assert.True(CreateObjectCodec.TryParse(body, MessageOpcodes.ObjectCreate, out var create));
        Assert.Equal("Ëmberglass Ñecklace", create.Weenie.Name);
    }

    /// <summary>The smallest create ACE sends: no physics or weenie options.</summary>
    private static AceCreateObject Minimal => new()
    {
        Guid = 0x80000001,
        Sequences = new ushort[9],
        Name = "Thing",
        WeenieClassId = 7,
        Icon = 0x06000001,
        ItemType = 0x80,
    };

    /// <summary>Where the ObjectDescriptionFlags word of a <see cref="Minimal"/>-shaped create is (no physics fields).</summary>
    private static int WeenieDescriptionFlagsOffset(byte[] body)
    {
        var weenieStart = 4 + 4 + 4 + 8 + 18 + 2;  // opcode, GUID, empty ModelData, flags and state, sequences, Align
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(weenieStart + 4));
        var afterName = weenieStart + 4 + 2 + nameLength;
        afterName += (4 - ((2 + nameLength) & 3)) & 3;
        return afterName + 2 + 2 + 4;  // the class (one u16 here), the icon (one u16), the item type
    }
}

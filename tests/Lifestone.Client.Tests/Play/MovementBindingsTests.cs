using Lifestone.Client.Camera;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// The movement and camera actions of the game key map (KEYMAP 0x14000000, the user's dats) reaching <see cref="IPlayerMovementIntents"/> and the
/// chase camera through the real input router: the keys, the action names the dats give them, and what each delivery does.
/// </summary>
public sealed class MovementBindingsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Each_movement_action_sends_its_command_as_a_press_and_a_release()
    {
        var intents = new RecordingIntents();
        var bindings = new MovementBindings(intents, () => 5.0);

        bindings.OnInputAction(4, MovementBindings.MoveForward, Activation(true));
        bindings.OnInputAction(4, MovementBindings.MoveForward, Activation(false));
        bindings.OnInputAction(4, MovementBindings.MoveBackward, Activation(true));
        bindings.OnInputAction(4, MovementBindings.TurnLeft, Activation(true));
        bindings.OnInputAction(4, MovementBindings.TurnRight, Activation(true));
        bindings.OnInputAction(4, MovementBindings.StrafeLeft, Activation(true));
        bindings.OnInputAction(4, MovementBindings.StrafeRight, Activation(false));

        Assert.Equal(
            [
                "cmd 45000005 True", "cmd 45000005 False", "cmd 45000006 True", "cmd 6500000E True", "cmd 6500000D True", "cmd 65000010 True", "cmd 6500000F False",
            ],
            intents.Calls);
    }

    [Fact]
    public void Stop_moving_is_a_ready_press_the_run_key_is_the_hold_run_modifier_and_jump_is_the_bar()
    {
        var intents = new RecordingIntents();
        var now = 5.0;
        var bindings = new MovementBindings(intents, () => now);

        bindings.OnInputAction(4, MovementBindings.StopMoving, Activation(true));
        bindings.OnInputAction(4, MovementBindings.StopMoving, Activation(false));
        bindings.OnInputAction(4, MovementBindings.RunKey, Activation(true));
        bindings.OnInputAction(4, MovementBindings.RunKey, Activation(false));
        bindings.OnInputAction(4, MovementBindings.Jump, Activation(true));
        now = 5.7;
        bindings.OnInputAction(4, MovementBindings.Jump, Activation(false));

        Assert.Equal(["cmd 41000003 True", "run True", "run False", "commence 5", "dojump 5.7"], intents.Calls);
    }

    [Fact]
    public void Autorun_follows_the_controllers_state_when_it_is_given_and_toggles_when_it_is_not()
    {
        var intents = new RecordingIntents();
        var on = false;
        var aware = new MovementBindings(intents, () => 0.0, () => on);
        aware.OnInputAction(4, MovementBindings.AutoRun, Activation(true)); // starts: toggles it on
        on = true;
        aware.OnInputAction(4, MovementBindings.AutoRun, Activation(true)); // already on: nothing
        aware.OnInputAction(4, MovementBindings.AutoRun, Activation(false)); // ends: toggles it off
        on = false;
        aware.OnInputAction(4, MovementBindings.AutoRun, Activation(false)); // already off: nothing (a teleport cleared it)

        var plain = new MovementBindings(intents, () => 0.0);
        plain.OnInputAction(4, MovementBindings.AutoRun, Activation(true));
        plain.OnInputAction(4, MovementBindings.AutoRun, Activation(false));

        Assert.Equal(["autorun", "autorun", "autorun", "autorun"], intents.Calls);
    }

    [Fact]
    public void The_emote_actions_send_the_motion_of_their_name_as_a_press()
    {
        var intents = new RecordingIntents();
        var bindings = new MovementBindings(intents, () => 0.0);

        foreach (var action in new uint[] { 0x10000094, 0x10000095, 0x10000096, 0x10000097 })
        {
            Assert.True(bindings.OnInputAction(4, action, Activation(true)));
            bindings.OnInputAction(4, action, Activation(false));
        }

        Assert.Equal(["cmd 41000003 True", "cmd 41000012 True", "cmd 41000013 True", "cmd 41000014 True"], intents.Calls);
    }

    [Fact]
    public void Other_maps_and_unknown_actions_are_not_taken()
    {
        var intents = new RecordingIntents();
        var bindings = new MovementBindings(intents, () => 0.0);

        Assert.False(bindings.OnInputAction(5, MovementBindings.MoveForward, Activation(true)));
        Assert.False(bindings.OnInputAction(4, 0x99, Activation(true)));
        Assert.Empty(intents.Calls);
    }

    [Fact]
    public void The_camera_actions_act_on_the_model_and_the_mouse_and_alternate_keys_reach_the_host()
    {
        var model = new ChaseCameraModel();
        var host = new RecordingCameraHost();
        var now = 10.0;
        var bindings = new CameraBindings(model, () => now, host);

        bindings.OnInputAction(5, CameraBindings.ZoomIn, Activation(true));
        var zoomed = model.ViewerOffset;
        bindings.OnInputAction(5, CameraBindings.ZoomIn, Activation(false));
        bindings.OnInputAction(5, CameraBindings.FirstPerson, Activation(true));
        var inHead = model.InHead;
        bindings.OnInputAction(5, CameraBindings.DefaultView, Activation(true));
        bindings.OnInputAction(5, CameraBindings.MouseLook, Activation(true));
        bindings.OnInputAction(5, CameraBindings.MouseLook, Activation(false));
        bindings.OnInputAction(5, CameraBindings.AlternateMode, Activation(true));
        bindings.OnInputAction(5, CameraBindings.AlternateMode, Activation(false));
        bindings.OnInputAction(6, CameraBindings.RotateLeft, Activation(true));
        now = 10.1;
        model.Update(now, new CellFrame(0x40400001, new AcFrame(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity)), null);
        bindings.OnInputAction(6, CameraBindings.RotateLeft, Activation(false));

        Assert.True(zoomed.Length() < 2.6f);
        Assert.True(inHead);
        Assert.False(model.InHead); // the default view
        Assert.Equal(["look True", "look False", "alt True", "look True", "alt False", "look False"], host.Calls);
        Assert.False(bindings.OnInputAction(4, CameraBindings.ZoomIn, Activation(true)));
        Assert.True(model.ViewerOffset.X < 0f); // rotated left
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void The_actions_have_the_names_and_the_toggle_types_the_dats_give_them()
    {
        var actions = RetailActionMap.Load(fixture.Dats);
        var tables = new UiStringTables(fixture.Dats);
        Assert.True(tables.TryGetTable(actions.StringTableId, out var table));
        string NameOf(uint map, uint action)
        {
            Assert.True(actions.TryGetAction(map, action, out var a), $"map {map} action 0x{action:X}");
            Assert.True(table!.TryGetEntry(a.NameStringId, out var entry));
            return string.Concat(entry.Parts).Trim();
        }

        Assert.Equal("Move Forward", NameOf(4, MovementBindings.MoveForward));
        Assert.Equal("Move Backward", NameOf(4, MovementBindings.MoveBackward));
        Assert.Equal("Stop Moving", NameOf(4, MovementBindings.StopMoving));
        Assert.Equal("Strafe Right", NameOf(4, MovementBindings.StrafeRight));
        Assert.Equal("Strafe Left", NameOf(4, MovementBindings.StrafeLeft));
        Assert.Equal("Turn Right", NameOf(4, MovementBindings.TurnRight));
        Assert.Equal("Turn Left", NameOf(4, MovementBindings.TurnLeft));
        Assert.Equal("Autorun", NameOf(4, MovementBindings.AutoRun));
        Assert.Equal("Jump", NameOf(4, MovementBindings.Jump));
        Assert.Equal("Toggle Run/Walk", NameOf(4, MovementBindings.RunKey));
        Assert.Equal("Ready", NameOf(4, 0x10000094));
        Assert.Equal("Crouch", NameOf(4, 0x10000095));
        Assert.Equal("Sit", NameOf(4, 0x10000096));
        Assert.Equal("Lay Down", NameOf(4, 0x10000097));
        Assert.Equal("Zoom Camera In", NameOf(5, CameraBindings.ZoomIn));
        Assert.Equal("Zoom Camera Out", NameOf(5, CameraBindings.ZoomOut));
        Assert.Equal("Rotate Camera Left", NameOf(5, CameraBindings.RotateLeft));
        Assert.Equal("Rotate Camera Right", NameOf(5, CameraBindings.RotateRight));
        Assert.Equal("Rotate Camera Up", NameOf(5, CameraBindings.RotateUp));
        Assert.Equal("Rotate Camera Down", NameOf(5, CameraBindings.RotateDown));
        Assert.Equal("Move Camera to Default", NameOf(5, CameraBindings.DefaultView));
        Assert.Equal("First Person Camera", NameOf(5, CameraBindings.FirstPerson));
        Assert.Equal("Overhead View", NameOf(5, CameraBindings.Overhead));
        Assert.Equal("Map View", NameOf(5, CameraBindings.MapView));
        Assert.Equal("Toggle Mouselook", NameOf(5, CameraBindings.MouseLook));
        Assert.Equal("Toggle Alternate Camera Mode", NameOf(5, CameraBindings.AlternateMode));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsTheory]
    [InlineData(DikScanCodes.W, "cmd 45000005")]
    [InlineData(DikScanCodes.Up, "cmd 45000005")]
    [InlineData(DikScanCodes.X, "cmd 45000006")]
    [InlineData(DikScanCodes.Down, "cmd 45000006")]
    [InlineData(DikScanCodes.A, "cmd 6500000E")]
    [InlineData(DikScanCodes.Left, "cmd 6500000E")]
    [InlineData(DikScanCodes.D, "cmd 6500000D")]
    [InlineData(DikScanCodes.Right, "cmd 6500000D")]
    [InlineData(DikScanCodes.Z, "cmd 65000010")]
    [InlineData(DikScanCodes.C, "cmd 6500000F")]
    public void A_key_of_the_game_map_sends_its_command_when_pressed_and_when_released(int dik, string press)
    {
        var rig = Rig(); var (scene, intents) = (rig.Scene, rig.Intents);

        scene.Tap(dik);

        Assert.Equal([$"{press} True", $"{press} False"], intents.Calls);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Strafe_has_its_alt_arrow_bindings_and_the_alt_key_does_not_turn()
    {
        var rig = Rig(); var (scene, intents) = (rig.Scene, rig.Intents);

        scene.Input.KeyDown(DikScanCodes.LMenu);
        scene.Tap(DikScanCodes.A); // Alt+A: Strafe Left
        scene.Tap(DikScanCodes.D); // Alt+D: Strafe Right
        scene.Input.KeyUp(DikScanCodes.LMenu);

        Assert.Equal(["cmd 65000010 True", "cmd 65000010 False", "cmd 6500000F True", "cmd 6500000F False"], intents.Calls);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Space_charges_and_releases_the_jump_the_run_key_holds_and_s_stops()
    {
        var rig = Rig(); var (scene, intents) = (rig.Scene, rig.Intents);

        scene.Input.KeyDown(DikScanCodes.Space);
        scene.Frame(0.25);
        scene.Input.KeyUp(DikScanCodes.Space);
        scene.Input.KeyDown(DikScanCodes.LShift);
        scene.Input.KeyUp(DikScanCodes.LShift);
        scene.Tap(DikScanCodes.S);

        Assert.Equal(2, intents.Calls.Count(c => c.StartsWith("commence", StringComparison.Ordinal) || c.StartsWith("dojump", StringComparison.Ordinal)));
        var commence = double.Parse(intents.Calls[0]["commence ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        var dojump = double.Parse(intents.Calls[1]["dojump ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(dojump - commence, 0.24, 0.26);
        Assert.Equal(["run True", "run False", "cmd 41000003 True"], intents.Calls.Skip(2));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Q_toggles_autorun_and_a_movement_key_ends_it_through_the_router()
    {
        var rig = Rig(); var (scene, intents, state) = (rig.Scene, rig.Intents, rig.State);

        scene.Tap(DikScanCodes.Q); // on
        state.AutoRun = true;
        scene.Tap(DikScanCodes.W); // the router ends the toggle first: off, then the forward command

        Assert.Equal(["autorun", "autorun", "cmd 45000005 True", "cmd 45000005 False"], intents.Calls);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsTheory]
    [InlineData(DikScanCodes.Subtract, "closer")]
    [InlineData(DikScanCodes.Add, "farther")]
    [InlineData(DikScanCodes.NumPad4, "left")]
    [InlineData(DikScanCodes.NumPad6, "right")]
    [InlineData(DikScanCodes.NumPad8, "raise")]
    [InlineData(DikScanCodes.NumPad2, "lower")]
    [InlineData(DikScanCodes.NumPad0, "default")]
    [InlineData(DikScanCodes.Decimal, "head")]
    public void The_number_pad_works_the_camera(int dik, string effect)
    {
        var rig = Rig();
        var (scene, model) = (rig.Scene, rig.Model);
        var before = model.ViewerOffset;
        scene.Frame(0.05);

        scene.Input.KeyDown(dik);
        scene.Frame(0.05);
        model.Update(scene.Now, new CellFrame(0x40400001, new AcFrame(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity)), null);
        scene.Input.KeyUp(dik);

        var after = model.ViewerOffset;
        switch (effect)
        {
            case "closer":
                Assert.True(after.Length() < before.Length());
                break;
            case "farther":
                Assert.True(after.Length() > before.Length());
                break;
            case "left":
                Assert.True(after.X < 0f);
                break;
            case "right":
                Assert.True(after.X > 0f);
                break;
            case "raise":
                Assert.True(after.Z > before.Z);
                break;
            case "lower":
                Assert.True(after.Z < before.Z);
                break;
            case "default":
                Assert.Equal(before, after);
                break;
            case "head":
                Assert.True(model.InHead);
                break;
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void The_middle_button_is_the_mouse_look_and_f2_the_alternate_camera_mode_with_the_arrow_keys_working_the_camera()
    {
        var rig = Rig();
        var (scene, intents, model, host) = (rig.Scene, rig.Intents, rig.Model, rig.Host);

        scene.Input.MouseMove(10, 10);
        scene.Input.MouseButton(UiMouseButton.Middle, down: true);
        scene.Input.MouseButton(UiMouseButton.Middle, down: false);
        scene.Input.KeyDown(DikScanCodes.F2);
        scene.Input.KeyDown(DikScanCodes.Left); // map 6 while F2 is held: rotate the camera left, not turn the player
        scene.Input.KeyUp(DikScanCodes.Left);
        scene.Input.KeyUp(DikScanCodes.F2);
        scene.Tap(DikScanCodes.Left); // after: the player turns

        Assert.Contains("look True", host.Calls);
        Assert.Contains("alt True", host.Calls);
        Assert.Contains("alt False", host.Calls);
        Assert.True(model.ViewerOffset.X < 0f);
        Assert.Equal(["cmd 6500000E True", "cmd 6500000E False"], intents.Calls);
    }

    private static RetailActivation Activation(bool start) => new(start, start ? 1f : 0f, RetailToggleType.Momentary, start ? 1u : 2u, 0);

    /// <summary>A router over the user's key maps with the controller's sinks registered the way the game registers them.</summary>
    private Rigging Rig()
    {
        var scene = new UiScene([]);
        scene.Input.Bindings = RetailKeyBindings.Load(fixture.Dats);
        var intents = new RecordingIntents();
        var state = new AutoRunState();
        var model = new ChaseCameraModel();
        var host = new RecordingCameraHost(scene.Input);
        var movement = new MovementBindings(intents, () => scene.Now, () => state.AutoRun);
        var camera = new CameraBindings(model, () => scene.Now, host);
        var sink = new Both(movement, camera);
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, sink, 1000);
        scene.Input.RegisterInputMap(CameraBindings.CameraMap, sink, 1000);
        host.Sink = sink;
        return new Rigging(scene, intents, model, state, host);
    }

    private sealed record Rigging(UiScene Scene, RecordingIntents Intents, ChaseCameraModel Model, AutoRunState State, RecordingCameraHost Host);

    private sealed class AutoRunState
    {
        public bool AutoRun { get; set; }
    }

    private sealed class Both(MovementBindings movement, CameraBindings camera) : IInputActionSink
    {
        public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation) =>
            movement.OnInputAction(inputMapId, actionId, activation) || camera.OnInputAction(inputMapId, actionId, activation);
    }

    private sealed class RecordingCameraHost(UiInputRouter? router = null) : ICameraInputHost
    {
        public List<string> Calls { get; } = [];

        public IInputActionSink? Sink { get; set; }

        public void SetMouseLook(bool on) => Calls.Add($"look {on}");

        public void SetAlternateKeys(bool on)
        {
            Calls.Add($"alt {on}");
            if (router is null || Sink is null)
            {
                return;
            }

            if (on)
            {
                router.RegisterInputMap(CameraBindings.AlternateMap, Sink, 2000);
            }
            else
            {
                router.UnregisterInputMap(CameraBindings.AlternateMap, Sink);
            }
        }
    }

    private sealed class RecordingIntents : IPlayerMovementIntents
    {
        public List<string> Calls { get; } = [];

        public CellFrame Position => default;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f) => Calls.Add($"cmd {command:X8} {start}");

        public void SetHoldRun(bool on) => Calls.Add($"run {on}");

        public void SetHoldSidestep(bool on) => Calls.Add($"side {on}");

        public void ToggleAutoRun() => Calls.Add("autorun");

        public void CommenceJump(double now) => Calls.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"commence {now:0.###}"));

        public void DoJump(double now) => Calls.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"dojump {now:0.###}"));
    }
}

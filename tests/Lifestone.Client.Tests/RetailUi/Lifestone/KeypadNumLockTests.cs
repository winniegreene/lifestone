using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Ui;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.LifestonePanelTests;

/// <summary>
/// M5 4 h3, D59's caveat: Num Lock is the autopilot's key, and it also flips the keypad between digits and arrows in the operating system. The keypad's movement keys must do the same with it on and off. Lifestone translates a host key by
/// its <b>physical</b> key code (the key's position, <see cref="GodotKeyCodes"/>), never by the character or the logical key the operating system reports, so the state of Num Lock cannot change what a keypad key is. These tests pin the
/// translation (a pure function of the physical code: there is no Num Lock input to it) and read what retail's key map does with each keypad key.
/// </summary>
public sealed class KeypadNumLockTests(ITestOutputHelper output, DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const int Special = 0x400000;

    /// <summary>The keypad's Godot physical codes (KP_0 to KP_9) and the scan codes they must translate to, whatever the state of Num Lock.</summary>
    public static TheoryData<int, int> Keypad => new()
    {
        { Special | 0x86, DikScanCodes.NumPad0 },
        { Special | 0x87, DikScanCodes.NumPad1 },
        { Special | 0x88, DikScanCodes.NumPad2 },
        { Special | 0x89, DikScanCodes.NumPad3 },
        { Special | 0x8A, DikScanCodes.NumPad4 },
        { Special | 0x8B, DikScanCodes.NumPad5 },
        { Special | 0x8C, DikScanCodes.NumPad6 },
        { Special | 0x8D, DikScanCodes.NumPad7 },
        { Special | 0x8E, DikScanCodes.NumPad8 },
        { Special | 0x8F, DikScanCodes.NumPad9 },
    };

    [Theory]
    [MemberData(nameof(Keypad))]
    public void A_keypad_key_translates_by_its_physical_code_the_same_with_Num_Lock_on_and_off(int physical, int dik)
    {
        // With Num Lock on the host reports the digit as the logical key and the keypad key's physical code; with it off, the arrow (or Home, End, Insert...) as the logical key and the SAME physical code. The translation reads the physical code
        // only (RetailUiInput.HandleKey: physical first, the logical key only for an event with no physical code), so both states give one scan code, and the keypad's arrows are never the arrow keys' 0xC8-0xD0.
        Assert.Equal(dik, GodotKeyCodes.ToDik(physical));
        Assert.Equal(dik, GodotKeyCodes.ToDik(physical, UiKeyLocation.Left));
        Assert.Equal(dik, GodotKeyCodes.ToDik(physical, UiKeyLocation.Right));
        Assert.True(dik is >= DikScanCodes.NumPad7 and <= DikScanCodes.Decimal, "the keypad's scan codes are in the keypad block");
    }

    [Fact]
    public void Num_Lock_itself_translates_to_its_own_scan_code_and_no_keypad_key_translates_to_it()
    {
        Assert.Equal(DikScanCodes.NumLock, GodotKeyCodes.ToDik(Special | 0x1A));
        Assert.DoesNotContain(GodotKeyCodes.MappedKeycodes, code => code != (Special | 0x1A) && GodotKeyCodes.ToDik(code) == DikScanCodes.NumLock);
    }

    /// <summary>
    /// What retail's key map does with each keypad key, read from the dats and reported in the test output (the finding for the lead): the keypad is the <b>camera's</b> (input map 5: look up, down, left, right, centre...), not the player's
    /// movement map (4), whose keys are the letters and the arrows. The table is keyed by scan code only, so there is no Num Lock state in it; the autopilot's key is no keypad binding.
    /// </summary>
    [RequiresLocalDatsFact]
    public void The_keypad_keys_retail_binds_are_the_cameras_map_5_by_scan_code_and_none_moves_the_player_and_Num_Lock_is_unbound()
    {
        var retail = RetailKeyBindings.Load(fixture.Dats);
        var bound = new SortedDictionary<int, List<string>>();
        foreach (var binding in retail.Bindings)
        {
            if (binding.Control.DeviceIndex != RetailKeyBindings.KeyboardDevice)
            {
                continue;
            }

            var dik = binding.Control.Semantic;
            if (dik is >= DikScanCodes.NumPad7 and <= DikScanCodes.Decimal or DikScanCodes.NumPadEnter or DikScanCodes.Divide)
            {
                if (!bound.TryGetValue(dik, out var list))
                {
                    bound[dik] = list = [];
                }

                list.Add($"map {binding.InputMapId} action 0x{binding.ActionId:X} ({(binding.InputMapId == UiInputRouter.MovementMap ? MovementBindings.NameOf(binding.ActionId) : "other map")}) meta {binding.MetaMode} activation {binding.Activation}");
            }
        }

        foreach (var (dik, lines) in bound)
        {
            output.WriteLine($"keypad scan code 0x{dik:X2}: {string.Join("; ", lines)}");
        }

        output.WriteLine($"keypad scan codes with no binding in any map: {string.Join(", ", Enumerable.Range(DikScanCodes.NumPad7, DikScanCodes.Decimal - DikScanCodes.NumPad7 + 1).Where(d => !bound.ContainsKey(d)).Select(d => "0x" + d.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)))}");
        Assert.DoesNotContain(retail.Bindings, b => b.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice && b.Control.Semantic == DikScanCodes.NumLock);
        Assert.DoesNotContain(retail.Bindings, b => b.InputMapId == UiInputRouter.MovementMap && b.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice && b.Control.Semantic is >= DikScanCodes.NumPad7 and <= DikScanCodes.Decimal);
        Assert.NotEmpty(bound); // the keypad is bound, and only in the camera's map
        Assert.All(retail.Bindings.Where(b => b.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice && b.Control.Semantic is >= DikScanCodes.NumPad7 and <= DikScanCodes.Decimal), b => Assert.Equal(5u, b.InputMapId));
    }
}

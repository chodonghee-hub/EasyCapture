using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+S", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x53)]
    [InlineData("ctrl+shift+s", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x53)]
    [InlineData("  Ctrl + Alt + F9 ", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x78)]
    [InlineData("Control+Windows+1", HotkeyModifiers.Ctrl | HotkeyModifiers.Win, 0x31)]
    [InlineData("Super+Escape", HotkeyModifiers.Win, 0x1B)]
    [InlineData("Win+Esc", HotkeyModifiers.Win, 0x1B)]
    [InlineData("Alt+Enter", HotkeyModifiers.Alt, 0x0D)]
    [InlineData("Ctrl+PageDown", HotkeyModifiers.Ctrl, 0x22)]
    [InlineData("Ctrl+Alt+Left", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x25)]
    [InlineData("Ctrl+`", HotkeyModifiers.Ctrl, 0xC0)]
    [InlineData("Ctrl+-", HotkeyModifiers.Ctrl, 0xBD)]
    [InlineData("Ctrl+=", HotkeyModifiers.Ctrl, 0xBB)]
    [InlineData("Ctrl+[", HotkeyModifiers.Ctrl, 0xDB)]
    [InlineData("Ctrl+]", HotkeyModifiers.Ctrl, 0xDD)]
    [InlineData("Ctrl+\\", HotkeyModifiers.Ctrl, 0xDC)]
    [InlineData("Ctrl+;", HotkeyModifiers.Ctrl, 0xBA)]
    [InlineData("Ctrl+'", HotkeyModifiers.Ctrl, 0xDE)]
    [InlineData("Ctrl+,", HotkeyModifiers.Ctrl, 0xBC)]
    [InlineData("Ctrl+.", HotkeyModifiers.Ctrl, 0xBE)]
    [InlineData("Ctrl+/", HotkeyModifiers.Ctrl, 0xBF)]
    [InlineData("Ctrl+F24", HotkeyModifiers.Ctrl, 0x87)]
    [InlineData("F9", HotkeyModifiers.None, 0x78)]
    [InlineData("PrintScreen", HotkeyModifiers.None, 0x2C)]
    [InlineData("Shift+PrintScreen", HotkeyModifiers.Shift, 0x2C)]
    public void TryParse_Valid(string text, HotkeyModifiers modifiers, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(vk, hotkey.VirtualKey);
        Assert.Equal((int)modifiers, hotkey.Win32Modifiers);
        Assert.Equal(hotkey, Hotkey.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+Control+A")]
    [InlineData("S")]
    [InlineData("Shift+")]
    [InlineData("+S")]
    [InlineData("Ctrl++S")]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+NumPad1")]
    [InlineData("Enter")]
    [InlineData("Space")]
    public void TryParse_Invalid(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(default, hotkey);
        Assert.Throws<FormatException>(() => Hotkey.Parse(text));
    }

    [Theory]
    [InlineData("Ctrl+Shift+S", "Ctrl+Shift+S")]
    [InlineData("shift+ctrl+s", "Ctrl+Shift+S")]
    [InlineData("Win+Shift+Alt+Control+Delete", "Ctrl+Alt+Shift+Win+Delete")]
    [InlineData("windows + esc", "Win+Esc")]
    [InlineData("ctrl+pagedown", "Ctrl+PageDown")]
    [InlineData("f12", "F12")]
    public void ToString_IsCanonicalAndRoundTrips(string input, string expected)
    {
        var hotkey = Hotkey.Parse(input);

        Assert.Equal(expected, hotkey.ToString());
        Assert.Equal(hotkey, Hotkey.Parse(hotkey.ToString()));
    }

    [Fact]
    public void EveryTableKey_RoundTripsThroughToString()
    {
        for (var vk = 0; vk < 256; vk++)
        {
            if (!HotkeyKeys.TryGetName(vk, out _))
            {
                continue;
            }

            Assert.True(Hotkey.TryCreate(HotkeyModifiers.Ctrl, vk, out var hotkey), $"vk=0x{vk:X2}");
            Assert.Equal(hotkey, Hotkey.Parse(hotkey.ToString()));
        }
    }

    [Fact]
    public void TryCreate_RejectsUnknownKeyAndModifierlessNormalKey()
    {
        Assert.False(Hotkey.TryCreate(HotkeyModifiers.Ctrl, 0x07, out _));
        Assert.False(Hotkey.TryCreate(HotkeyModifiers.None, 0x53, out _));
        Assert.True(Hotkey.TryCreate(HotkeyModifiers.None, 0x78, out _));
        Assert.False(Hotkey.TryCreate((HotkeyModifiers)16, 0x53, out _));
        Assert.False(default(Hotkey).IsValid);
    }

    [Fact]
    public void DefaultHotkeys_AreAllValid()
    {
        foreach (var (action, text) in AppSettings.DefaultHotkeys)
        {
            Assert.True(Hotkey.TryParse(text, out var hotkey), $"{action}: {text}");
            Assert.Equal(text, hotkey.ToString());
        }
    }

    [Fact]
    public void Win32Modifiers_MatchRegisterHotKeyValues()
    {
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Ctrl);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Win);
        Assert.Equal(6, Hotkey.Parse("Ctrl+Shift+S").Win32Modifiers);
    }

    [Fact]
    public void Evaluate_ClassifiesInput()
    {
        Assert.Equal(HotkeyInputKind.Incomplete, HotkeyInput.Evaluate(HotkeyModifiers.Ctrl, 0x11).Kind);
        Assert.Equal(HotkeyInputKind.Incomplete, HotkeyInput.Evaluate(HotkeyModifiers.Shift, 0xA0).Kind);
        Assert.Equal(HotkeyInputKind.Incomplete, HotkeyInput.Evaluate(HotkeyModifiers.Win, 0x5B).Kind);

        Assert.Equal(HotkeyInputKind.Clear, HotkeyInput.Evaluate(HotkeyModifiers.None, 0x08).Kind);
        Assert.Equal(HotkeyInputKind.Clear, HotkeyInput.Evaluate(HotkeyModifiers.None, 0x2E).Kind);

        var ctrlDelete = HotkeyInput.Evaluate(HotkeyModifiers.Ctrl, 0x2E);
        Assert.Equal(HotkeyInputKind.Set, ctrlDelete.Kind);
        Assert.Equal("Ctrl+Delete", ctrlDelete.Hotkey.ToString());

        Assert.Equal(HotkeyInputKind.Invalid, HotkeyInput.Evaluate(HotkeyModifiers.None, 0x53).Kind);
        Assert.Equal(HotkeyInputKind.Invalid, HotkeyInput.Evaluate(HotkeyModifiers.Ctrl, 0x07).Kind);

        var f9 = HotkeyInput.Evaluate(HotkeyModifiers.None, 0x78);
        Assert.Equal(HotkeyInputKind.Set, f9.Kind);
        Assert.Equal("F9", f9.Hotkey.ToString());
    }

    [Fact]
    public void FormatProgress_ShowsPressedModifiers()
    {
        Assert.Equal("Ctrl+Shift+…", HotkeyInput.FormatProgress(HotkeyModifiers.Shift | HotkeyModifiers.Ctrl));
        Assert.Equal(string.Empty, HotkeyInput.FormatProgress(HotkeyModifiers.None));
    }
}

public sealed class HotkeyConflictsTests
{
    [Fact]
    public void Defaults_HaveNoConflicts()
    {
        Assert.Empty(HotkeyConflicts.Find(new AppSettings()));
    }

    [Fact]
    public void SameCombinationWithDifferentSpelling_IsConflict()
    {
        var settings = new AppSettings();
        settings.Hotkeys[HotkeyAction.OpenPanel] = "ctrl + shift + s";

        var conflict = Assert.Single(HotkeyConflicts.Find(settings));
        Assert.Equal(Hotkey.Parse("Ctrl+Shift+S"), conflict.Hotkey);
        Assert.Equal(["캡처 시작", "클립보드 패널 열기"], conflict.Names);
    }

    [Fact]
    public void PresetHotkey_ConflictsWithActionAndOtherPreset()
    {
        var settings = new AppSettings();
        settings.Presets[0].Hotkey = "Shift+Ctrl+C";
        settings.Presets.Add(new CapturePreset { Name = "둘째", Width = 10, Height = 10, Hotkey = "Ctrl+Shift+C" });
        settings.Presets.Add(new CapturePreset { Name = "셋째", Width = 10, Height = 10, Hotkey = "Ctrl+Alt+F1" });

        var conflict = Assert.Single(HotkeyConflicts.Find(settings));
        Assert.Equal(Hotkey.Parse("Ctrl+Shift+C"), conflict.Hotkey);
        Assert.Equal(3, conflict.Names.Count);
        Assert.Contains("마지막 캡처 다시 복사", conflict.Names);
        Assert.Contains("프리셋 '기본 영역'", conflict.Names);
        Assert.Contains("프리셋 '둘째'", conflict.Names);
    }

    [Fact]
    public void UnparsableEntries_AreSkipped()
    {
        var settings = new AppSettings();
        settings.Hotkeys[HotkeyAction.OpenPanel] = "garbage";
        settings.Hotkeys[HotkeyAction.CopyLast] = "also garbage";
        settings.Presets[0].Hotkey = "garbage";

        Assert.Empty(HotkeyConflicts.Find(settings));
        var entry = Assert.Single(HotkeyConflicts.CollectEntries(settings));
        Assert.Equal(HotkeyAction.StartCapture, entry.Action);
    }

    [Fact]
    public void DisabledActionHotkeys_AreIgnored_AndNeverConflict()
    {
        var settings = new AppSettings();
        settings.Hotkeys[HotkeyAction.StartCapture] = "";
        settings.Hotkeys[HotkeyAction.OpenPanel] = "";
        settings.Hotkeys[HotkeyAction.CopyLast] = null!;
        settings.Presets[0].Hotkey = "";

        Assert.Empty(HotkeyConflicts.CollectEntries(settings));
        Assert.Empty(HotkeyConflicts.Find(settings));
    }

    [Fact]
    public void CollectEntries_OrdersActionsThenPresets()
    {
        var settings = new AppSettings();
        settings.Presets[0].Hotkey = "Ctrl+Alt+F1";

        var entries = HotkeyConflicts.CollectEntries(settings);

        Assert.Equal(
            [HotkeyAction.StartCapture, HotkeyAction.OpenPanel, HotkeyAction.CopyLast, null],
            entries.Select(e => e.Action));
        Assert.Same(settings.Presets[0], entries[3].Preset);
    }
}

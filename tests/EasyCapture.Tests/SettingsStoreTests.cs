using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "EasyCapture.Tests." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(_dir, "EasyCapture", "settings.json");

    private SettingsStore NewStore() => new(SettingsPath);

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, json);
    }

    [Fact]
    public void Defaults_AreConsistent()
    {
        var s = new AppSettings();

        Assert.Equal("Ctrl+Shift+S", s.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal("Ctrl+Shift+V", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("Ctrl+Shift+C", s.Hotkeys[HotkeyAction.CopyLast]);
        Assert.Equal(3, s.Hotkeys.Count);

        var preset = Assert.Single(s.Presets);
        Assert.Equal("기본 영역", preset.Name);
        Assert.Null(preset.MonitorDeviceName);
        Assert.Equal((100, 100, 800, 600), (preset.X, preset.Y, preset.Width, preset.Height));
        Assert.Null(preset.Hotkey);
        Assert.Equal(preset.Id, s.DefaultPresetId);

        Assert.Equal(CopyMode.FileAndImage, s.CopyMode);
        Assert.True(s.AutoCopyLatest);
        Assert.Equal(200, s.MaxItems);
        Assert.Equal(0, s.MaxAgeDays);
        Assert.False(s.StartWithWindows);
        Assert.Equal(1, s.Version);
    }

    [Fact]
    public void AppPaths_UseGivenRoots()
    {
        Assert.Equal(Path.Combine("R", "EasyCapture", "settings.json"), AppPaths.SettingsFile("R"));
        Assert.Equal(Path.Combine("L", "EasyCapture", "captures"), AppPaths.CapturesDirectory("L"));
        Assert.Equal(Path.Combine("L", "EasyCapture", "captures", "index.json"), AppPaths.IndexFile("L"));
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = NewStore().Load();

        Assert.False(File.Exists(SettingsPath));
        Assert.Single(s.Presets);
        Assert.Equal(s.Presets[0].Id, s.DefaultPresetId);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_AndKeepsKoreanUnescaped()
    {
        var original = new AppSettings
        {
            CopyMode = CopyMode.ImageOnly,
            AutoCopyLatest = false,
            MaxItems = 50,
            MaxAgeDays = 7,
            StartWithWindows = true,
        };
        original.Hotkeys[HotkeyAction.OpenPanel] = "Ctrl+Alt+P";
        var second = new CapturePreset
        {
            Name = "두 번째 영역",
            MonitorDeviceName = @"\\.\DISPLAY2",
            X = 10,
            Y = 20,
            Width = 300,
            Height = 200,
            Hotkey = "Ctrl+Alt+1",
        };
        original.Presets.Add(second);
        original.DefaultPresetId = second.Id;

        var store = NewStore();
        store.Save(original);
        var loaded = store.Load();

        var raw = File.ReadAllText(SettingsPath);
        Assert.Contains("두 번째 영역", raw);
        Assert.Contains("기본 영역", raw);
        Assert.DoesNotContain("\\u", raw);
        Assert.Contains("\"ImageOnly\"", raw);

        Assert.Equal(original.Hotkeys, loaded.Hotkeys);
        Assert.Equal(2, loaded.Presets.Count);
        Assert.Equal(original.Presets[0].Id, loaded.Presets[0].Id);
        var loadedSecond = loaded.Presets[1];
        Assert.Equal(second.Id, loadedSecond.Id);
        Assert.Equal("두 번째 영역", loadedSecond.Name);
        Assert.Equal(@"\\.\DISPLAY2", loadedSecond.MonitorDeviceName);
        Assert.Equal((10, 20, 300, 200), (loadedSecond.X, loadedSecond.Y, loadedSecond.Width, loadedSecond.Height));
        Assert.Equal("Ctrl+Alt+1", loadedSecond.Hotkey);
        Assert.Equal(second.Id, loaded.DefaultPresetId);
        Assert.Equal(CopyMode.ImageOnly, loaded.CopyMode);
        Assert.False(loaded.AutoCopyLatest);
        Assert.Equal(50, loaded.MaxItems);
        Assert.Equal(7, loaded.MaxAgeDays);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(1, loaded.Version);
    }

    [Fact]
    public void Load_BrokenJson_ReturnsDefaults_AndMovesFile()
    {
        WriteRaw("{ not json !!");

        var s = NewStore().Load();

        Assert.Single(s.Presets);
        Assert.False(File.Exists(SettingsPath));
        var broken = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "settings.json.broken-*"));
        Assert.Equal("{ not json !!", File.ReadAllText(broken));
    }

    [Fact]
    public void Load_Utf8BomValidJson_IsParsed_AndNotMovedAsBroken()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "MaxItems": 42, "Hotkeys": { "OpenPanel": "Ctrl+Alt+O" } }""",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(SettingsPath).Take(3).ToArray());

        var s = NewStore().Load();

        Assert.Equal(42, s.MaxItems);
        Assert.Equal("Ctrl+Alt+O", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.True(File.Exists(SettingsPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.broken-*"));
    }

    [Fact]
    public void Load_NoBomValidJson_IsParsed_AndNotMovedAsBroken()
    {
        WriteRaw("""{ "MaxItems": 42, "Hotkeys": { "OpenPanel": "Ctrl+Alt+O" } }""");
        Assert.NotEqual(0xEF, File.ReadAllBytes(SettingsPath)[0]);

        var s = NewStore().Load();

        Assert.Equal(42, s.MaxItems);
        Assert.Equal("Ctrl+Alt+O", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.broken-*"));
    }

    [Fact]
    public void Load_Utf8BomBrokenJson_IsStillMovedAsBroken()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json !!", new System.Text.UTF8Encoding(true));

        var s = NewStore().Load();

        Assert.Equal(200, s.MaxItems);
        Assert.False(File.Exists(SettingsPath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "settings.json.broken-*"));
    }

    [Fact]
    public void Save_WritesUtf8WithoutBom()
    {
        NewStore().Save(new AppSettings());

        var bytes = File.ReadAllBytes(SettingsPath);
        Assert.Equal((byte)'{', bytes[0]);
    }

    [Fact]
    public void Load_JsonNull_IsTreatedAsBroken()
    {
        WriteRaw("null");

        var s = NewStore().Load();

        Assert.Single(s.Presets);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "settings.json.broken-*"));
    }

    [Fact]
    public void Load_UnknownEnumAndKeys_FallBackToDefaults_WithoutBreaking()
    {
        WriteRaw("""
            {
              "CopyMode": "Bogus",
              "Hotkeys": { "Nonsense": "Ctrl+X", "OpenPanel": "Ctrl+Alt+O", "CopyLast": 5 },
              "MaxItems": 10
            }
            """);

        var s = NewStore().Load();

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.broken-*"));
        Assert.Equal(CopyMode.FileAndImage, s.CopyMode);
        Assert.Equal("Ctrl+Alt+O", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("Ctrl+Shift+C", s.Hotkeys[HotkeyAction.CopyLast]);
        Assert.Equal("Ctrl+Shift+S", s.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal(3, s.Hotkeys.Count);
        Assert.Equal(10, s.MaxItems);
    }

    [Fact]
    public void Normalize_FillsMissingHotkeys()
    {
        var s = new AppSettings();
        s.Hotkeys.Remove(HotkeyAction.CopyLast);
        s.Hotkeys[HotkeyAction.StartCapture] = "Ctrl+Alt+Q";

        SettingsStore.Normalize(s);

        Assert.Equal("Ctrl+Shift+C", s.Hotkeys[HotkeyAction.CopyLast]);
        Assert.Equal("Ctrl+Shift+V", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("Ctrl+Alt+Q", s.Hotkeys[HotkeyAction.StartCapture]);
    }

    [Fact]
    public void Normalize_BlankOrNullActionHotkey_IsKeptAsDisabledEmptyString()
    {
        var s = new AppSettings();
        s.Hotkeys[HotkeyAction.StartCapture] = "  ";
        s.Hotkeys[HotkeyAction.OpenPanel] = "";
        s.Hotkeys[HotkeyAction.CopyLast] = null!;

        SettingsStore.Normalize(s);

        Assert.Equal("", s.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal("", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("", s.Hotkeys[HotkeyAction.CopyLast]);
        Assert.Equal(3, s.Hotkeys.Count);
    }

    [Fact]
    public void SaveThenLoad_DisabledHotkey_StaysDisabled()
    {
        var original = new AppSettings();
        original.Hotkeys[HotkeyAction.OpenPanel] = "";
        original.Hotkeys[HotkeyAction.CopyLast] = null!;

        var store = NewStore();
        store.Save(original);
        var loaded = store.Load();

        Assert.Equal("", loaded.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("", loaded.Hotkeys[HotkeyAction.CopyLast]);
        Assert.Equal("Ctrl+Shift+S", loaded.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal(3, loaded.Hotkeys.Count);

        // 다시 저장해도 유지된다.
        store.Save(loaded);
        Assert.Equal("", store.Load().Hotkeys[HotkeyAction.OpenPanel]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Load_HotkeyValueNullOrBlank_IsDisabled(string jsonValue)
    {
        WriteRaw("{ \"Hotkeys\": { \"OpenPanel\": " + jsonValue + " } }");

        var s = NewStore().Load();

        Assert.Equal("", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("Ctrl+Shift+S", s.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal("Ctrl+Shift+C", s.Hotkeys[HotkeyAction.CopyLast]);
        Assert.Equal(3, s.Hotkeys.Count);
    }

    [Fact]
    public void Load_MissingHotkeyKeys_AreFilledWithDefaults()
    {
        WriteRaw("""{ "Hotkeys": { "StartCapture": "" } }""");
        var partial = NewStore().Load();

        Assert.Equal("", partial.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal("Ctrl+Shift+V", partial.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("Ctrl+Shift+C", partial.Hotkeys[HotkeyAction.CopyLast]);

        WriteRaw("""{ "MaxItems": 5 }""");
        var none = NewStore().Load();

        Assert.Equal(AppSettings.DefaultHotkeys, none.Hotkeys);
    }

    [Fact]
    public void Load_UnparsableNonEmptyHotkey_IsReplacedWithDefault()
    {
        WriteRaw("""{ "Hotkeys": { "StartCapture": "Ctrl+Shift", "OpenPanel": "", "CopyLast": 7 } }""");

        var s = NewStore().Load();

        Assert.Equal("Ctrl+Shift+S", s.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal("", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("Ctrl+Shift+C", s.Hotkeys[HotkeyAction.CopyLast]);
    }

    [Fact]
    public void Normalize_UnparsableActionHotkeys_ReplacedWithDefaults()
    {
        var s = new AppSettings();
        s.Hotkeys[HotkeyAction.StartCapture] = "Ctrl+Shift";
        s.Hotkeys[HotkeyAction.OpenPanel] = "S";
        s.Hotkeys[HotkeyAction.CopyLast] = "  control + alt + L ";

        SettingsStore.Normalize(s);

        Assert.Equal("Ctrl+Shift+S", s.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal("Ctrl+Shift+V", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("control + alt + L", s.Hotkeys[HotkeyAction.CopyLast]);
    }

    [Fact]
    public void Normalize_PresetHotkey_UnparsableBecomesNull_ValidKept()
    {
        var s = new AppSettings();
        s.Presets[0].Hotkey = "Ctrl+Alt+Nope";
        s.Presets.Add(new CapturePreset { Name = "B", Width = 10, Height = 10, Hotkey = " Ctrl+Alt+F9 " });
        s.Presets.Add(new CapturePreset { Name = "C", Width = 10, Height = 10, Hotkey = "X" });

        SettingsStore.Normalize(s);

        Assert.Null(s.Presets[0].Hotkey);
        Assert.Equal("Ctrl+Alt+F9", s.Presets[1].Hotkey);
        Assert.Null(s.Presets[2].Hotkey);
    }

    [Fact]
    public void Normalize_EmptyPresets_RestoresDefault()
    {
        var s = new AppSettings { Presets = new List<CapturePreset>(), DefaultPresetId = null };

        SettingsStore.Normalize(s);

        var preset = Assert.Single(s.Presets);
        Assert.Equal("기본 영역", preset.Name);
        Assert.Equal(preset.Id, s.DefaultPresetId);
    }

    [Fact]
    public void Normalize_InvalidDefaultPresetId_FallsBackToFirst()
    {
        var s = new AppSettings();
        s.Presets.Add(new CapturePreset { Name = "B", Width = 10, Height = 10 });
        s.DefaultPresetId = Guid.NewGuid();

        SettingsStore.Normalize(s);

        Assert.Equal(s.Presets[0].Id, s.DefaultPresetId);
    }

    [Fact]
    public void Normalize_RemovesPresetsWithInvalidSize()
    {
        var s = new AppSettings();
        var keep = s.Presets[0];
        s.Presets.Add(new CapturePreset { Name = "zero", Width = 0, Height = 10 });
        s.Presets.Add(new CapturePreset { Name = "neg", Width = 10, Height = -5 });
        s.DefaultPresetId = s.Presets[1].Id;

        SettingsStore.Normalize(s);

        Assert.Same(keep, Assert.Single(s.Presets));
        Assert.Equal(keep.Id, s.DefaultPresetId);
    }

    [Fact]
    public void Normalize_AllPresetsInvalid_RestoresDefault()
    {
        var s = new AppSettings();
        s.Presets[0].Width = 0;

        SettingsStore.Normalize(s);

        var preset = Assert.Single(s.Presets);
        Assert.Equal(800, preset.Width);
        Assert.Equal(preset.Id, s.DefaultPresetId);
    }

    [Fact]
    public void Normalize_FixesRetentionValues_AndEnum()
    {
        var s = new AppSettings { MaxItems = 0, MaxAgeDays = -3, CopyMode = (CopyMode)99 };

        SettingsStore.Normalize(s);

        Assert.Equal(200, s.MaxItems);
        Assert.Equal(0, s.MaxAgeDays);
        Assert.Equal(CopyMode.FileAndImage, s.CopyMode);
    }

    [Fact]
    public void Load_AppliesNormalize_ToFileContents()
    {
        WriteRaw("""{ "Presets": [], "MaxItems": -1, "MaxAgeDays": -1 }""");

        var s = NewStore().Load();

        Assert.Single(s.Presets);
        Assert.Equal(s.Presets[0].Id, s.DefaultPresetId);
        Assert.Equal(200, s.MaxItems);
        Assert.Equal(0, s.MaxAgeDays);
    }

    [Fact]
    public void Load_ReadLocked_ReturnsDefaults_AndKeepsFile()
    {
        WriteRaw("""{ "MaxItems": 77 }""");
        var dir = Path.GetDirectoryName(SettingsPath)!;

        // 쓰기 핸들을 열어 두면 읽기는 공유 위반이 되지만 이동(삭제 공유)은 가능하다.
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Write,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            var s = NewStore().Load();

            Assert.Single(s.Presets);
            Assert.Equal(200, s.MaxItems);
        }

        Assert.True(File.Exists(SettingsPath));
        Assert.Empty(Directory.GetFiles(dir, "*.broken-*"));
        Assert.Equal(77, NewStore().Load().MaxItems);
    }

    [Fact]
    public void Normalize_DuplicatePresetIds_KeepsFirstAndReissuesRest()
    {
        var s = new AppSettings();
        var id = s.Presets[0].Id;
        s.Presets.Add(new CapturePreset { Name = "B", Width = 10, Height = 10, Id = id });
        s.Presets.Add(new CapturePreset { Name = "C", Width = 10, Height = 10, Id = id });
        s.DefaultPresetId = id;

        SettingsStore.Normalize(s);

        Assert.Equal(3, s.Presets.Count);
        Assert.Equal(3, s.Presets.Select(p => p.Id).Distinct().Count());
        Assert.DoesNotContain(s.Presets, p => p.Id == Guid.Empty);
        Assert.Equal(id, s.Presets[0].Id);
        Assert.Equal(id, s.DefaultPresetId);
    }

    [Fact]
    public void Normalize_SamePresetInstanceTwice_RemovesDuplicateAndKeepsIdsUnique()
    {
        var s = new AppSettings();
        s.Presets.Add(s.Presets[0]);

        SettingsStore.Normalize(s);

        var preset = Assert.Single(s.Presets);
        Assert.Equal(preset.Id, s.DefaultPresetId);
        Assert.Single(s.Presets.Select(p => p.Id).Distinct());
    }

    [Fact]
    public void Save_AfterReadFailure_PreservesOriginalAsBroken_ThenSaves()
    {
        WriteRaw("""{ "MaxItems": 77 }""");
        var dir = Path.GetDirectoryName(SettingsPath)!;
        var store = NewStore();

        AppSettings loaded;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Write,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            loaded = store.Load();
        }

        Assert.Equal(200, loaded.MaxItems);

        store.Save(loaded);

        // 보존은 이동이 아니라 복사이므로 새 settings.json 이 저장되고 보존본은 원본 내용 그대로다.
        var broken = Assert.Single(Directory.GetFiles(dir, "settings.json.broken-*"));
        Assert.Equal("""{ "MaxItems": 77 }""", File.ReadAllText(broken));
        Assert.True(File.Exists(SettingsPath));
        Assert.Equal(200, NewStore().Load().MaxItems);

        // 상태가 해제됐으므로 다음 저장은 더 이상 .broken-* 을 만들지 않는다.
        store.Save(loaded);
        Assert.Single(Directory.GetFiles(dir, "settings.json.broken-*"));
    }

    [Fact]
    public void Save_AfterReadFailure_WhenReplaceFails_Throws_AndOriginalStaysInPlace()
    {
        const string original = """{ "MaxItems": 77 }""";
        WriteRaw(original);
        var dir = Path.GetDirectoryName(SettingsPath)!;
        var store = NewStore();

        // 쓰기 핸들(삭제 공유)을 연 채로 두면 Load 는 실패하고, 보존 복사는 되지만 교체는 거부된다.
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Write,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            var loaded = store.Load();
            Assert.Equal(200, loaded.MaxItems);

            Assert.ThrowsAny<Exception>(() => store.Save(loaded));

            Assert.True(File.Exists(SettingsPath));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }

        // 저장이 실패해도 원본은 원래 자리에 그대로 있고, 보존본은 원본 복사본이다.
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(dir, "settings.json.broken-*"))));
        Assert.Equal(77, NewStore().Load().MaxItems);

        // 핸들이 닫힌 뒤 같은 인스턴스로 다시 저장하면 플래그가 유지돼 있어 성공한다.
        store.Save(new AppSettings { MaxItems = 55 });

        Assert.Equal(55, NewStore().Load().MaxItems);
        Assert.All(Directory.GetFiles(dir, "settings.json.broken-*"), f => Assert.Equal(original, File.ReadAllText(f)));
    }

    [Fact]
    public void Save_AfterReadFailure_WhenPreserveCopyFails_Throws_AndKeepsOriginal_ThenRetrySucceeds()
    {
        const string original = """{ "MaxItems": 77 }""";
        WriteRaw(original);
        var dir = Path.GetDirectoryName(SettingsPath)!;
        var store = NewStore();

        // 공유 없이 열어 두면 Load 의 읽기도, Save 의 보존 복사도 공유 위반으로 실패한다.
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var loaded = store.Load();
            Assert.Equal(200, loaded.MaxItems);

            Assert.ThrowsAny<IOException>(() => store.Save(loaded));

            Assert.Empty(Directory.GetFiles(dir, "*.broken-*"));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }

        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(dir, "*.broken-*"));

        // 핸들이 닫힌 뒤 다시 저장하면 원본이 보존되고 새 설정이 저장된다.
        store.Save(new AppSettings { MaxItems = 55 });

        var broken = Assert.Single(Directory.GetFiles(dir, "settings.json.broken-*"));
        Assert.Equal(original, File.ReadAllText(broken));
        Assert.Equal(55, NewStore().Load().MaxItems);
    }

    [Fact]
    public void Save_AfterSuccessfulLoad_DoesNotCreateBrokenFile()
    {
        var store = NewStore();
        store.Save(new AppSettings { MaxItems = 33 });

        var loaded = store.Load();
        store.Save(loaded);

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.broken-*"));
        Assert.Equal(33, NewStore().Load().MaxItems);
    }

    [Fact]
    public void Save_AfterReadFailureThenSuccessfulLoad_DoesNotCreateBrokenFile()
    {
        WriteRaw("""{ "MaxItems": 77 }""");
        var store = NewStore();

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Write,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            store.Load();
        }

        var loaded = store.Load();
        store.Save(loaded);

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.broken-*"));
        Assert.Equal(77, NewStore().Load().MaxItems);
    }

    [Fact]
    public void Load_DuplicatePresetIds_AreMadeUnique()
    {
        var id = Guid.NewGuid();
        WriteRaw($$"""
            { "Presets": [
                { "Id": "{{id}}", "Name": "A", "Width": 10, "Height": 10 },
                { "Id": "{{id}}", "Name": "B", "Width": 10, "Height": 10 } ],
              "DefaultPresetId": "{{id}}" }
            """);

        var s = NewStore().Load();

        Assert.Equal(2, s.Presets.Select(p => p.Id).Distinct().Count());
        Assert.Equal(id, s.Presets[0].Id);
        Assert.Equal(id, s.DefaultPresetId);
    }

    [Fact]
    public void Load_CompositeOrNumericHotkeyKeys_AreIgnored()
    {
        WriteRaw("""
            {
              "Hotkeys": { "StartCapture, OpenPanel": "Ctrl+X", "1": "Ctrl+Y", "copylast": "Ctrl+Alt+L" }
            }
            """);

        var s = NewStore().Load();

        Assert.Equal("Ctrl+Shift+V", s.Hotkeys[HotkeyAction.OpenPanel]);
        Assert.Equal("Ctrl+Shift+S", s.Hotkeys[HotkeyAction.StartCapture]);
        Assert.Equal("Ctrl+Alt+L", s.Hotkeys[HotkeyAction.CopyLast]);
        Assert.Equal(3, s.Hotkeys.Count);
    }

    [Theory]
    [InlineData("FileOnly, FileAndImage")]
    [InlineData("1")]
    [InlineData("FileOnly, ImageOnly")]
    public void Load_CompositeOrNumericCopyMode_FallsBackToDefault(string value)
    {
        WriteRaw($$"""{ "CopyMode": "{{value}}" }""");

        var s = NewStore().Load();

        Assert.Equal(CopyMode.FileAndImage, s.CopyMode);
    }

    [Fact]
    public void Load_CopyModeName_IsCaseInsensitive()
    {
        WriteRaw("""{ "CopyMode": "fileonly" }""");

        Assert.Equal(CopyMode.FileOnly, NewStore().Load().CopyMode);
    }

    [Fact]
    public void Save_LeavesNoTempFiles()
    {
        var store = NewStore();

        store.Save(new AppSettings());

        var files = Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!);
        Assert.Equal(SettingsPath, Assert.Single(files));
    }

    [Fact]
    public void Save_OverExistingFile_Works()
    {
        var store = NewStore();
        store.Save(new AppSettings { MaxItems = 11 });
        store.Save(new AppSettings { MaxItems = 22 });

        Assert.Equal(22, store.Load().MaxItems);
        Assert.Equal(SettingsPath, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!)));
    }
}

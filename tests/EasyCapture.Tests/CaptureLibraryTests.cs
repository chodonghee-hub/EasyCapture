using System.Buffers.Binary;
using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class CaptureLibraryTests : IDisposable
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 1, 30, 45, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "EasyCapture.Tests." + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(T0);

    private string Dir => Path.Combine(_root, "captures");

    private string IndexPath => Path.Combine(Dir, "index.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            // 테스트가 중단돼 거부 ACL이 남았어도 삭제되도록 먼저 복구를 시도한다(실패는 무시).
            TryRestoreDeniedListAcl(new DirectoryInfo(_root));
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void TryRestoreDeniedListAcl(DirectoryInfo directory)
    {
        try
        {
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            var security = directory.GetAccessControl();
            var changed = false;
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in
                     security.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
            {
                if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny
                    && rule.FileSystemRights.HasFlag(System.Security.AccessControl.FileSystemRights.ListDirectory)
                    && rule.IdentityReference.Equals(user))
                {
                    security.RemoveAccessRule(rule);
                    changed = true;
                }
            }

            if (changed)
            {
                directory.SetAccessControl(security);
            }
        }
        catch (Exception)
        {
            // 복구는 최선 노력이다.
        }

        try
        {
            foreach (var child in directory.EnumerateDirectories())
            {
                TryRestoreDeniedListAcl(child);
            }
        }
        catch (Exception)
        {
            // 열거하지 못하면 하위 복구는 건너뛴다.
        }
    }

    private CaptureLibrary NewLibrary() => new(Dir, _clock);

    private CaptureItem AddAt(CaptureLibrary library, DateTimeOffset when, string? preset = null)
    {
        _clock.Now = when;
        return library.Add(OnePixelPng, 1, 1, preset);
    }

    private static byte[] PngWithSize(int width, int height)
    {
        var bytes = (byte[])OnePixelPng.Clone();
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private string[] PngFiles() =>
        Directory.GetFiles(Dir, "*.png").Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private string[] BrokenFiles() => Directory.GetFiles(Dir, "index.json.broken-*");

    // ---- Add ----

    [Fact]
    public void Add_WritesFile_UpdatesIndex_AndUsesDefaultName()
    {
        var library = NewLibrary();

        var item = library.Add(OnePixelPng, 1, 1, "기본 영역");

        Assert.Equal("캡처_20261023_013045.png", item.FileName);
        Assert.Equal("캡처_20261023_013045", item.Name);
        Assert.Equal(T0, item.CreatedAt);
        Assert.Equal((1, 1), (item.Width, item.Height));
        Assert.False(item.Pinned);
        Assert.Equal("기본 영역", item.PresetName);
        Assert.NotEqual(Guid.Empty, item.Id);

        var path = library.GetFilePath(item.Id);
        Assert.Equal(Path.Combine(Dir, item.FileName), path);
        Assert.Equal(OnePixelPng, File.ReadAllBytes(path));
        Assert.True(File.Exists(IndexPath));
        Assert.Equal(item, Assert.Single(library.Items));
        Assert.Empty(Directory.GetFiles(Dir, "*.tmp-*"));
    }

    [Fact]
    public void Add_SameSecond_AppendsSuffix()
    {
        var library = NewLibrary();

        var first = library.Add(OnePixelPng, 1, 1);
        var second = library.Add(OnePixelPng, 1, 1);
        var third = library.Add(OnePixelPng, 1, 1);

        Assert.Equal("캡처_20261023_013045.png", first.FileName);
        Assert.Equal("캡처_20261023_013045_2.png", second.FileName);
        Assert.Equal("캡처_20261023_013045_3.png", third.FileName);
        Assert.Equal(3, PngFiles().Length);
    }

    [Fact]
    public void Items_AreNewestFirst()
    {
        var library = NewLibrary();

        var a = AddAt(library, T0);
        var b = AddAt(library, T0.AddMinutes(2));
        var c = AddAt(library, T0.AddMinutes(1));

        Assert.Equal(new[] { b.Id, c.Id, a.Id }, library.Items.Select(i => i.Id));
    }

    [Fact]
    public void Add_InvalidInput_Throws()
    {
        var library = NewLibrary();

        Assert.Throws<ArgumentException>(() => library.Add(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 1, 1));
        Assert.Throws<ArgumentException>(() => library.Add(Array.Empty<byte>(), 1, 1));
        Assert.Throws<ArgumentException>(() => library.Add(OnePixelPng, 0, 1));
        Assert.Throws<ArgumentException>(() => library.Add(OnePixelPng, 1, -1));
        Assert.Throws<ArgumentNullException>(() => library.Add(null!, 1, 1));
        Assert.Empty(library.Items);
        Assert.Empty(Directory.Exists(Dir) ? PngFiles() : Array.Empty<string>());
    }

    [Fact]
    public void Add_FromManyThreads_KeepsEveryItem()
    {
        var library = NewLibrary();

        Parallel.For(0, 20, _ => library.Add(OnePixelPng, 1, 1));

        Assert.Equal(20, library.Items.Count);
        Assert.Equal(20, library.Items.Select(i => i.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(20, NewLibrary().Items.Count);
    }

    [Fact]
    public void GetFilePath_UnknownId_Throws_AndTryReturnsFalse()
    {
        var library = NewLibrary();

        Assert.Throws<KeyNotFoundException>(() => library.GetFilePath(Guid.NewGuid()));
        Assert.False(library.TryGetFilePath(Guid.NewGuid(), out var path));
        Assert.Equal(string.Empty, path);
    }

    // ---- Rename ----

    [Fact]
    public void Rename_Succeeds_AndRenamesFileOnDisk()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.True(library.Rename(item.Id, "  회의 자료  "));

        var renamed = Assert.Single(library.Items);
        Assert.Equal("회의 자료", renamed.Name);
        Assert.Equal("회의 자료.png", renamed.FileName);
        Assert.Equal(item.Id, renamed.Id);
        Assert.Equal(new[] { "회의 자료.png" }, PngFiles());
        Assert.Equal(Path.Combine(Dir, "회의 자료.png"), library.GetFilePath(item.Id));
        Assert.Equal("회의 자료.png", Assert.Single(NewLibrary().Items).FileName);
    }

    [Fact]
    public void Rename_RemovesForbiddenCharacters_AndTrailingDotsAndSpaces()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.True(library.Rename(item.Id, "a\\b/c:d*e?f\"g<h>i|j\u0007k. . "));

        Assert.Equal("abcdefghijk", Assert.Single(library.Items).Name);
        Assert.Equal(new[] { "abcdefghijk.png" }, PngFiles());
    }

    [Fact]
    public void Rename_DoesNotDuplicatePngExtension()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.True(library.Rename(item.Id, "report.PNG"));

        Assert.Equal("report", Assert.Single(library.Items).Name);
        Assert.Equal(new[] { "report.png" }, PngFiles());
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("Aux")]
    [InlineData("NUL.png")]
    [InlineData("com1")]
    [InlineData("LPT9")]
    [InlineData("con.txt")]
    public void Rename_ReservedName_IsRejected(string name)
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.False(library.Rename(item.Id, name));

        Assert.Equal(item, Assert.Single(library.Items));
        Assert.Equal(new[] { item.FileName }, PngFiles());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    [InlineData("..")]
    [InlineData(". .")]
    [InlineData(".png")]
    public void Rename_EmptyAfterSanitizing_IsRejected(string name)
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.False(library.Rename(item.Id, name));

        Assert.Equal(item, Assert.Single(library.Items));
    }

    [Fact]
    public void Rename_UnknownId_ReturnsFalse()
    {
        var library = NewLibrary();

        Assert.False(library.Rename(Guid.NewGuid(), "x"));
    }

    [Fact]
    public void Rename_CaseOnlyChange_Works()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);
        Assert.True(library.Rename(item.Id, "photo"));

        Assert.True(library.Rename(item.Id, "PHOTO"));

        Assert.Equal("PHOTO.png", Assert.Single(library.Items).FileName);
        Assert.Equal(new[] { "PHOTO.png" }, PngFiles());
        Assert.Equal(OnePixelPng, File.ReadAllBytes(library.GetFilePath(item.Id)));
    }

    [Fact]
    public void Rename_SameName_ReturnsTrue_WithoutChange()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);
        Assert.True(library.Rename(item.Id, "same"));

        Assert.True(library.Rename(item.Id, "same"));

        Assert.Equal(new[] { "same.png" }, PngFiles());
    }

    [Fact]
    public void Rename_Collision_GetsNumberedSuffix()
    {
        var library = NewLibrary();
        var a = AddAt(library, T0);
        var b = AddAt(library, T0.AddSeconds(1));
        var c = AddAt(library, T0.AddSeconds(2));

        Assert.True(library.Rename(a.Id, "x"));
        Assert.True(library.Rename(b.Id, "x"));
        Assert.True(library.Rename(c.Id, "X"));

        var byId = library.Items.ToDictionary(i => i.Id);
        Assert.Equal("x.png", byId[a.Id].FileName);
        Assert.Equal("x_2.png", byId[b.Id].FileName);
        Assert.Equal("X_3.png", byId[c.Id].FileName);
        Assert.Equal("x_2", byId[b.Id].Name);
        Assert.Equal(new[] { "X_3.png", "x.png", "x_2.png" }, PngFiles());
    }

    [Fact]
    public void Rename_CollidesWithUnindexedFileOnDisk_GetsSuffix()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);
        File.WriteAllBytes(Path.Combine(Dir, "taken.png"), OnePixelPng);

        Assert.True(library.Rename(item.Id, "taken"));

        Assert.Equal("taken_2.png", Assert.Single(library.Items).FileName);
        Assert.Equal(2, PngFiles().Length);
    }

    [Fact]
    public void Rename_LimitsLengthTo100()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.True(library.Rename(item.Id, new string('가', 150)));

        Assert.Equal(100, Assert.Single(library.Items).Name.Length);
    }

    [Fact]
    public void Rename_CollisionSuffix_StaysWithin100Characters()
    {
        var library = NewLibrary();
        var a = AddAt(library, T0);
        var b = AddAt(library, T0.AddSeconds(1));
        var longName = new string('x', 100);

        Assert.True(library.Rename(a.Id, longName));
        Assert.True(library.Rename(b.Id, longName));

        var byId = library.Items.ToDictionary(i => i.Id);
        Assert.Equal(100, byId[a.Id].Name.Length);
        Assert.Equal(new string('x', 98) + "_2", byId[b.Id].Name);
        Assert.All(library.Items, i => Assert.True(i.Name.Length <= 100));
        Assert.Equal(2, PngFiles().Length);
    }

    [Fact]
    public void Rename_CutInsideSurrogatePair_KeepsPairIntact()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.True(library.Rename(item.Id, new string('a', 99) + "\U0001F600"));

        var renamed = Assert.Single(library.Items);
        Assert.False(char.IsHighSurrogate(renamed.Name[^1]));
        Assert.Equal(new string('a', 99), renamed.Name);
        Assert.True(File.Exists(library.GetFilePath(item.Id)));
        Assert.Equal(new[] { renamed.FileName }, PngFiles());
    }

    [Fact]
    public void Rename_EmojiFitsExactly_IsKept()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);
        var name = new string('a', 98) + "\U0001F600";

        Assert.True(library.Rename(item.Id, name));

        Assert.Equal(name, Assert.Single(library.Items).Name);
    }

    [Fact]
    public void Rename_LockedFile_ReturnsFalse_AndKeepsState()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        using (new FileStream(library.GetFilePath(item.Id), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(library.Rename(item.Id, "new"));
        }

        Assert.Equal(item, Assert.Single(library.Items));
        Assert.Equal(new[] { item.FileName }, PngFiles());
    }

    // ---- SetPinned ----

    [Fact]
    public void SetPinned_TogglesAndPersists()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        Assert.True(library.SetPinned(item.Id, true));
        Assert.True(Assert.Single(library.Items).Pinned);
        Assert.True(Assert.Single(NewLibrary().Items).Pinned);

        Assert.True(library.SetPinned(item.Id, false));
        Assert.False(Assert.Single(NewLibrary().Items).Pinned);

        Assert.False(library.SetPinned(Guid.NewGuid(), true));
    }

    // ---- Delete ----

    [Fact]
    public void Delete_RemovesFileAndItem()
    {
        var library = NewLibrary();
        var keep = AddAt(library, T0);
        var drop = AddAt(library, T0.AddSeconds(1));

        Assert.True(library.Delete(drop.Id));

        Assert.Equal(keep, Assert.Single(library.Items));
        Assert.Equal(new[] { keep.FileName }, PngFiles());
        Assert.Equal(keep.Id, Assert.Single(NewLibrary().Items).Id);
        Assert.False(library.Delete(drop.Id));
    }

    [Fact]
    public void Delete_FileAlreadyGone_StillRemovesItem()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);
        File.Delete(library.GetFilePath(item.Id));

        Assert.True(library.Delete(item.Id));

        Assert.Empty(library.Items);
        Assert.Empty(NewLibrary().Items);
    }

    [Fact]
    public void Delete_LockedFile_ReturnsFalse_AndKeepsIndexEntry()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);

        using (new FileStream(library.GetFilePath(item.Id), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(library.Delete(item.Id));
        }

        Assert.Equal(item.Id, Assert.Single(library.Items).Id);
        Assert.Equal(item.Id, Assert.Single(NewLibrary().Items).Id);
        Assert.True(library.Delete(item.Id));
    }

    // ---- ApplyRetention ----

    [Fact]
    public void ApplyRetention_RemovesOldestBeyondMaxItems()
    {
        var library = NewLibrary();
        var items = Enumerable.Range(0, 5).Select(i => AddAt(library, T0.AddMinutes(i))).ToList();

        var deleted = library.ApplyRetention(3, 0);

        Assert.Equal(2, deleted);
        Assert.Equal(new[] { items[4].Id, items[3].Id, items[2].Id }, library.Items.Select(i => i.Id));
        Assert.Equal(3, PngFiles().Length);
        Assert.Equal(3, NewLibrary().Items.Count);
    }

    [Fact]
    public void ApplyRetention_NothingToDelete_ReturnsZero()
    {
        var library = NewLibrary();
        AddAt(library, T0);

        Assert.Equal(0, library.ApplyRetention(10, 0));
        Assert.Single(library.Items);
    }

    [Fact]
    public void ApplyRetention_RemovesOlderThanMaxAgeDays()
    {
        var library = NewLibrary();
        var old = AddAt(library, T0);
        var recent = AddAt(library, T0.AddDays(8));
        _clock.Now = T0.AddDays(10);

        var deleted = library.ApplyRetention(100, 5);

        Assert.Equal(1, deleted);
        Assert.Equal(recent.Id, Assert.Single(library.Items).Id);
        Assert.DoesNotContain(old.FileName, PngFiles());
    }

    [Fact]
    public void ApplyRetention_NeverDeletesPinned()
    {
        var library = NewLibrary();
        var oldPinned = AddAt(library, T0);
        var old = AddAt(library, T0.AddMinutes(1));
        var newer = AddAt(library, T0.AddDays(9));
        var newest = AddAt(library, T0.AddDays(9).AddMinutes(1));
        library.SetPinned(oldPinned.Id, true);
        _clock.Now = T0.AddDays(10);

        // 기간으로 old가 지워지고, 개수(2)는 고정 1 + 최신 1로 맞춘다.
        var deleted = library.ApplyRetention(2, 5);

        Assert.Equal(2, deleted);
        Assert.Equal(new[] { newest.Id, oldPinned.Id }, library.Items.Select(i => i.Id));
        Assert.DoesNotContain(old.FileName, PngFiles());
        Assert.DoesNotContain(newer.FileName, PngFiles());
    }

    [Fact]
    public void ApplyRetention_PinnedOverLimit_KeepsAllPinned()
    {
        var library = NewLibrary();
        var a = AddAt(library, T0);
        var b = AddAt(library, T0.AddMinutes(1));
        var c = AddAt(library, T0.AddMinutes(2));
        library.SetPinned(a.Id, true);
        library.SetPinned(b.Id, true);

        var deleted = library.ApplyRetention(1, 0);

        Assert.Equal(1, deleted);
        Assert.Equal(new[] { b.Id, a.Id }, library.Items.Select(i => i.Id));
        Assert.DoesNotContain(c.FileName, PngFiles());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ApplyRetention_MaxItemsBelowOne_Throws(int maxItems)
    {
        var library = NewLibrary();
        AddAt(library, T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => library.ApplyRetention(maxItems, 0));
        Assert.Single(library.Items);
    }

    [Fact]
    public void ApplyRetention_LockedFile_IsSkipped_AndContinues()
    {
        var library = NewLibrary();
        var oldest = AddAt(library, T0);
        var middle = AddAt(library, T0.AddMinutes(1));
        var newest = AddAt(library, T0.AddMinutes(2));

        int deleted;
        using (new FileStream(library.GetFilePath(oldest.Id), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deleted = library.ApplyRetention(1, 0);
        }

        // 가장 오래된 것은 잠겨 건너뛰고, 다음으로 오래된 것을 지워 가능한 만큼 줄인다.
        Assert.Equal(1, deleted);
        Assert.Equal(new[] { newest.Id, oldest.Id }, library.Items.Select(i => i.Id));
        Assert.DoesNotContain(middle.FileName, PngFiles());
        Assert.Equal(2, NewLibrary().Items.Count);
    }

    // ---- 영속화 ----

    [Fact]
    public void NewInstance_ReadsSameItems_AndKeepsKoreanUnescaped()
    {
        var library = NewLibrary();
        var a = AddAt(library, T0, "기본 영역");
        var b = AddAt(library, T0.AddMinutes(1));
        library.SetPinned(a.Id, true);
        library.Rename(b.Id, "두번째");

        var reloaded = NewLibrary();

        Assert.Equal(library.Items, reloaded.Items);
        var text = File.ReadAllText(IndexPath);
        Assert.Contains("기본 영역", text);
        Assert.Contains("두번째", text);
        Assert.DoesNotContain("\\u", text);
        Assert.False(File.ReadAllBytes(IndexPath).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void Load_MissingFolder_ReturnsEmpty_AndCreatesNothing()
    {
        var library = NewLibrary();

        Assert.Empty(library.Items);
        Assert.False(Directory.Exists(Dir));
    }

    [Fact]
    public void Load_IndexEntryWithoutFile_IsRemovedAndIndexRewritten()
    {
        var library = NewLibrary();
        var keep = AddAt(library, T0);
        var gone = AddAt(library, T0.AddSeconds(1));
        File.Delete(library.GetFilePath(gone.Id));

        var reloaded = NewLibrary();

        Assert.Equal(keep.Id, Assert.Single(reloaded.Items).Id);
        Assert.DoesNotContain(gone.FileName, File.ReadAllText(IndexPath));
    }

    [Fact]
    public void Load_BrokenIndex_IsMovedAside_AndRebuiltFromPngs()
    {
        Directory.CreateDirectory(Dir);
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var pathA = Path.Combine(Dir, "첫 캡처.png");
        var pathB = Path.Combine(Dir, "broken-header.png");
        File.WriteAllBytes(pathA, PngWithSize(5, 3));
        File.WriteAllBytes(pathB, new byte[] { 1, 2, 3 });
        File.SetCreationTimeUtc(pathA, created);
        File.WriteAllText(Path.Combine(Dir, "notes.txt"), "x");
        File.WriteAllText(IndexPath, "{ this is not json");

        var library = NewLibrary();

        var brokenFile = Assert.Single(BrokenFiles());
        Assert.Equal("{ this is not json", File.ReadAllText(brokenFile));
        Assert.Matches(@"index\.json\.broken-20261023013045$", brokenFile);

        Assert.Equal(2, library.Items.Count);
        var a = library.Items.Single(i => i.FileName == "첫 캡처.png");
        Assert.Equal("첫 캡처", a.Name);
        Assert.Equal((5, 3), (a.Width, a.Height));
        Assert.Equal(created, a.CreatedAt.UtcDateTime, TimeSpan.FromSeconds(2));
        Assert.False(a.Pinned);
        Assert.Null(a.PresetName);

        var b = library.Items.Single(i => i.FileName == "broken-header.png");
        Assert.Equal((0, 0), (b.Width, b.Height));

        // 재구성 결과가 인덱스로 저장되어 다음 로드에서 그대로 읽힌다.
        Assert.Equal(2, NewLibrary().Items.Count);
        Assert.Single(BrokenFiles());
    }

    [Fact]
    public void Load_IndexMissing_PngsPresent_RebuildsFromFolder_AndCreatesIndex()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, "a.png"), PngWithSize(4, 2));
        File.WriteAllBytes(Path.Combine(Dir, "b.png"), OnePixelPng);

        var library = NewLibrary();

        Assert.Equal(2, library.Items.Count);
        var a = library.Items.Single(i => i.FileName == "a.png");
        Assert.Equal("a", a.Name);
        Assert.Equal((4, 2), (a.Width, a.Height));
        Assert.True(File.Exists(IndexPath));
        Assert.Empty(BrokenFiles());
        Assert.Equal(2, NewLibrary().Items.Count);
    }

    [Fact]
    public void Load_IndexMissing_PngsPresent_SaveFails_KeepsMemoryList()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, "a.png"), OnePixelPng);
        // index.json 자리에 폴더를 두어 저장을 실패시킨다.
        Directory.CreateDirectory(IndexPath);

        var library = NewLibrary();

        Assert.Equal("a.png", Assert.Single(library.Items).FileName);
    }

    [Fact]
    public void Load_IndexMissing_NoPngs_StaysEmpty_AndCreatesNoIndex()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "notes.txt"), "x");

        var library = NewLibrary();

        Assert.Empty(library.Items);
        Assert.False(File.Exists(IndexPath));
        Assert.Empty(BrokenFiles());
    }

    [Fact]
    public void Load_IndexMissing_DirectoryMissing_StaysEmpty()
    {
        var library = NewLibrary();

        Assert.Empty(library.Items);
        Assert.False(Directory.Exists(Dir));
    }

    [Fact]
    public void Load_ValidIndex_DoesNotAdoptPngsMissingFromIndex()
    {
        var first = NewLibrary();
        var tracked = first.Add(OnePixelPng, 1, 1);
        File.WriteAllBytes(Path.Combine(Dir, "stray.png"), OnePixelPng);

        var reloaded = NewLibrary();

        Assert.Equal(tracked.Id, Assert.Single(reloaded.Items).Id);
        Assert.DoesNotContain("stray.png", File.ReadAllText(IndexPath));
    }

    [Fact]
    public void Load_DuplicateIdAfterMissingFile_KeepsEntryWithExistingFile()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, "present.png"), OnePixelPng);
        var id = Guid.NewGuid();
        File.WriteAllText(IndexPath, $$"""
            {
              "Version": 1,
              "Items": [
                { "Id": "{{id}}", "FileName": "missing.png", "CreatedAt": "2026-10-23T01:30:45+00:00" },
                { "Id": "{{id}}", "FileName": "present.png", "CreatedAt": "2026-10-23T01:30:45+00:00", "Width": 1, "Height": 1 }
              ]
            }
            """);

        var library = NewLibrary();

        var item = Assert.Single(library.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal("present.png", item.FileName);
        Assert.Equal("present.png", Assert.Single(NewLibrary().Items).FileName);
    }

    [Fact]
    public void Load_BrokenIndex_FolderUnreadable_DoesNotPersistIncompleteIndex()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, "a.png"), OnePixelPng);
        File.WriteAllBytes(Path.Combine(Dir, "b.png"), OnePixelPng);
        File.WriteAllText(IndexPath, "{ this is not json");

        var dirInfo = new DirectoryInfo(Dir);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var denyList = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = dirInfo.GetAccessControl();
        security.AddAccessRule(denyList);
        dirInfo.SetAccessControl(security);
        try
        {
            var library = NewLibrary();

            // 폴더를 읽지 못해 불완전한 결과는 디스크에 저장하지 않고, 깨진 인덱스도 그대로 둔다.
            Assert.Empty(library.Items);
            Assert.Equal("{ this is not json", File.ReadAllText(IndexPath));
        }
        finally
        {
            security = dirInfo.GetAccessControl();
            security.RemoveAccessRule(denyList);
            dirInfo.SetAccessControl(security);
        }

        Assert.Empty(BrokenFiles());

        // 접근이 복구된 다음 로드에서는 다시 재구성해 PNG를 모두 찾는다.
        Assert.Equal(2, NewLibrary().Items.Count);
        Assert.Single(BrokenFiles());
    }

    [Fact]
    public void Load_IndexWithBom_IsReadNormally()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);
        var bytes = File.ReadAllBytes(IndexPath);
        File.WriteAllBytes(IndexPath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray());

        var reloaded = NewLibrary();

        Assert.Equal(item.Id, Assert.Single(reloaded.Items).Id);
        Assert.Empty(BrokenFiles());
    }

    [Fact]
    public void Load_UnsafeFileNames_AreIgnored()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(_root, "evil.png"), OnePixelPng);
        Directory.CreateDirectory(Path.Combine(Dir, "sub"));
        File.WriteAllBytes(Path.Combine(Dir, "sub", "x.png"), OnePixelPng);
        File.WriteAllBytes(Path.Combine(Dir, "good.png"), OnePixelPng);
        File.WriteAllText(IndexPath, $$"""
            {
              "Version": 1,
              "Items": [
                { "Id": "{{Guid.NewGuid()}}", "FileName": "..\\evil.png", "CreatedAt": "2026-10-23T01:30:45+00:00" },
                { "Id": "{{Guid.NewGuid()}}", "FileName": "../evil.png", "CreatedAt": "2026-10-23T01:30:45+00:00" },
                { "Id": "{{Guid.NewGuid()}}", "FileName": "sub/x.png", "CreatedAt": "2026-10-23T01:30:45+00:00" },
                { "Id": "{{Guid.NewGuid()}}", "FileName": "index.json", "CreatedAt": "2026-10-23T01:30:45+00:00" },
                { "Id": "{{Guid.NewGuid()}}", "FileName": "..", "CreatedAt": "2026-10-23T01:30:45+00:00" },
                { "Id": "{{Guid.NewGuid()}}", "FileName": "", "CreatedAt": "2026-10-23T01:30:45+00:00" },
                null,
                { "Id": "{{Guid.NewGuid()}}", "FileName": "good.png", "CreatedAt": "2026-10-23T01:30:45+00:00", "Width": 1, "Height": 1 }
              ]
            }
            """);

        var library = NewLibrary();

        var item = Assert.Single(library.Items);
        Assert.Equal("good.png", item.FileName);
        Assert.Equal("good", item.Name);
        Assert.True(File.Exists(Path.Combine(_root, "evil.png")));
        Assert.True(File.Exists(Path.Combine(Dir, "sub", "x.png")));
    }

    [Fact]
    public void Load_LockedIndex_StartsEmpty_AndPreservesOriginalBeforeFirstSave()
    {
        var first = NewLibrary();
        var original = first.Add(OnePixelPng, 1, 1);
        var originalBytes = File.ReadAllBytes(IndexPath);

        CaptureLibrary locked;
        using (new FileStream(IndexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            locked = NewLibrary();
            Assert.Empty(locked.Items);
            Assert.Empty(BrokenFiles());

            // 원본을 복사해 둘 수 없으면 저장을 중단하고, 방금 쓴 PNG도 남기지 않는다.
            Assert.ThrowsAny<IOException>(() => locked.Add(OnePixelPng, 1, 1));
            Assert.Empty(locked.Items);
        }

        Assert.Equal(originalBytes, File.ReadAllBytes(IndexPath));
        Assert.Equal(new[] { original.FileName }, PngFiles());
        Assert.Empty(BrokenFiles());

        // 잠금이 풀린 뒤 첫 저장은 원본을 .broken-* 으로 복사 보존한 다음 덮어쓴다.
        var added = locked.Add(OnePixelPng, 1, 1);

        var brokenFile = Assert.Single(BrokenFiles());
        Assert.Equal(originalBytes, File.ReadAllBytes(brokenFile));
        Assert.Equal(added.Id, Assert.Single(NewLibrary().Items).Id);

        // 보존은 한 번만 한다.
        locked.Add(OnePixelPng, 1, 1);
        Assert.Single(BrokenFiles());
    }

    [Fact]
    public void Operations_LeaveNoTempFilesBehind()
    {
        var library = NewLibrary();
        var item = library.Add(OnePixelPng, 1, 1);
        library.Rename(item.Id, "a");
        library.SetPinned(item.Id, true);
        library.Delete(item.Id);

        Assert.Empty(Directory.GetFiles(Dir, "*.tmp-*"));
    }

    private sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

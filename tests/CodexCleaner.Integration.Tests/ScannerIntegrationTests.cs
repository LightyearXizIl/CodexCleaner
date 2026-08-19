using CodexCleaner.Core;
using CodexCleaner.Services;

namespace CodexCleaner.Integration.Tests;

public sealed class ScannerIntegrationTests
{
    [Fact]
    public async Task Scanner_classifies_test_disk_without_touching_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerTestDisk", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllBytesAsync(Path.Combine(root, "node_modules", "package.bin"), new byte[4096]);
        await File.WriteAllTextAsync(Path.Combine(root, "src", "app.cs"), "class App {}");
        try
        {
            var size = new DirectorySizer();
            var result = await size.GetSizeAsync(root, CancellationToken.None);
            Assert.True(result.Bytes >= 4096);
            Assert.Equal(RiskLevel.Rebuildable, RiskRules.Classify(Path.Combine(root, "node_modules")).Risk);
            Assert.Equal(RiskLevel.Protected, RiskRules.Classify(Path.Combine(root, "src")).Risk);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Cleanup_only_deletes_selected_safe_test_cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerCleanup", Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache"); Directory.CreateDirectory(cache); await File.WriteAllTextAsync(Path.Combine(cache, "safe.log"), "cache");
        try
        {
            var item = new StorageItem(cache, "cache", 5, ItemCategory.Cache, RiskLevel.Safe, DateTimeOffset.UtcNow, Source: "开发环境");
            var service = new CleanupService(); var plan = service.CreatePlan([new CleanupCandidate(item, true, "test")]);
            var result = await service.ExecuteAsync(plan, CancellationToken.None);
            Assert.True(result.Items.Single().Success);
            Assert.False(Directory.Exists(cache));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SQLite_persists_snapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerHistory", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "history.db"); var service = new SqliteHistoryService(database);
            var result = new ScanResult(Guid.NewGuid(), ScanMode.Quick, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new DriveSummary("C", 100, 40), [new CategorySummary(ItemCategory.Cache, 10, 1, RiskLevel.Safe)], [], [new StorageItem("C:\\test\\cache", "cache", 10, ItemCategory.Cache, RiskLevel.Safe, DateTimeOffset.UtcNow)], [], true);
            await service.SaveAsync(result, CancellationToken.None);
            Assert.Single(await service.GetRecentAsync(1, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Cleanup_refuses_project_cache_even_when_its_name_is_safe()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerProtectedCleanup", Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache"); Directory.CreateDirectory(cache); await File.WriteAllTextAsync(Path.Combine(cache, "keep.txt"), "project content");
        try
        {
            var settings = new AppSettingsService(Path.Combine(root, "settings.json"));
            var history = new PortableJsonHistoryService(Path.Combine(root, "history.json"));
            var service = new CleanupService(settings, new RiskService(), new GitStatusService(new ExternalCommandRunner()), history);
            var item = new StorageItem(cache, "cache", 15, ItemCategory.Cache, RiskLevel.Safe, Directory.GetLastWriteTimeUtc(cache), Source: "项目目录");
            var result = await service.ExecuteAsync(service.CreatePlan([new CleanupCandidate(item, true, "project cache")]), CancellationToken.None);
            Assert.False(result.Items.Single().Success);
            Assert.True(Directory.Exists(cache));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Deep_storage_scan_returns_real_large_file_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerLargeFiles", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var large = Path.Combine(root, "large.bin"); await using (var stream = File.Create(large)) stream.SetLength(100L * 1024 * 1024);
            var results = await new StorageScanner(new DirectorySizer()).ScanAsync([root], ScanMode.Deep, null, CancellationToken.None);
            var entry = Assert.Single(results);
            Assert.Equal(large, entry.Path);
            Assert.False(entry.IsDirectory);
            Assert.Equal(100L * 1024 * 1024, entry.SizeBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Duplicate_scan_requires_full_hash_match()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerDuplicate", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.bin"), "identical content");
            await File.WriteAllTextAsync(Path.Combine(root, "b.bin"), "identical content");
            await File.WriteAllTextAsync(Path.Combine(root, "different.bin"), "different content");
            var groups = await new DuplicateService().FindDuplicatesAsync([root], 1, CancellationToken.None);
            var group = Assert.Single(groups);
            Assert.Equal(2, group.Paths.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Deep_storage_scan_honors_cancellation_before_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerCancelledScan", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "file.bin"), "not scanned");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => new StorageScanner(new DirectorySizer()).ScanAsync([root], ScanMode.Deep, null, cancellation.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Settings_round_trip_preserves_protection_and_ignore_rules()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerSettings", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var settings = new AppSettingsService(Path.Combine(root, "settings.json"));
            var expected = new AppSettings("D:\\Codex", ["D:\\Projects"], "Dark", 30, true, true, true, ["D:\\Projects\\Keep"], ["D:\\Projects\\Ignore"]);
            await settings.SaveAsync(expected, CancellationToken.None);
            var actual = await settings.LoadAsync(CancellationToken.None);
            Assert.Equal(expected.CodexHome, actual.CodexHome);
            Assert.Equal(expected.AdditionalRoots, actual.AdditionalRoots);
            Assert.Equal(expected.Theme, actual.Theme);
            Assert.Equal(expected.QuarantineDays, actual.QuarantineDays);
            Assert.Equal(expected.ConfirmBeforeDelete, actual.ConfirmBeforeDelete);
            Assert.Equal(expected.CheckGitBeforeDelete, actual.CheckGitBeforeDelete);
            Assert.Equal(expected.ReduceMotion, actual.ReduceMotion);
            Assert.Equal(expected.ProtectedPaths, actual.ProtectedPaths);
            Assert.Equal(expected.IgnoredPaths, actual.IgnoredPaths);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Migration_copies_and_verifies_without_deleting_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerMigration", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "npm-cache"); var targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(source); await File.WriteAllTextAsync(Path.Combine(source, "cache.bin"), "cache");
        try
        {
            var service = new MigrationService(new DirectorySizer(), new FakeCommands(source));
            var item = new StorageItem(source, "npm 缓存", new FileInfo(Path.Combine(source, "cache.bin")).Length, ItemCategory.Cache, RiskLevel.Safe, Directory.GetLastWriteTimeUtc(source), FileCount: 1, Detail: "npm 缓存", IsDirectory: true, Source: "开发环境");
            var candidates = await service.DiscoverAsync(targetRoot, [item], CancellationToken.None);
            var plan = new MigrationPlan(Guid.NewGuid(), targetRoot, DateTimeOffset.UtcNow, candidates);
            var result = await service.ExecuteAsync(plan, CancellationToken.None);
            Assert.Equal(MigrationState.AwaitingSourceCleanup, result.State);
            Assert.True(File.Exists(Path.Combine(source, "cache.bin")));
            Assert.True(File.Exists(Path.Combine(candidates.Single().TargetPath, "cache.bin")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("v0.0.1", true)]
    [InlineData("0.1.0", true)]
    [InlineData("v0.0.10", false)]
    [InlineData("v0.0.1-beta", false)]
    public void Update_version_parser_accepts_only_release_digit_versions(string tag, bool expected)
        => Assert.Equal(expected, GitHubUpdateService.TryParseVersion(tag, out _));

    private sealed class FakeCommands(string initialCache) : IExternalCommandRunner
    {
        private string _cache = initialCache;
        public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (executable == "npm" && arguments.SequenceEqual(["config", "get", "cache"])) return Task.FromResult(new CommandResult(true, 0, _cache, string.Empty, false));
            if (executable == "npm" && arguments.Count == 4 && arguments[0] == "config" && arguments[1] == "set" && arguments[2] == "cache") { _cache = arguments[3]; return Task.FromResult(new CommandResult(true, 0, string.Empty, string.Empty, false)); }
            return Task.FromResult(new CommandResult(false, -1, string.Empty, string.Empty, false));
        }
    }
}

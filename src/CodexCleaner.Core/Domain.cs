using System.Security.Cryptography;

namespace CodexCleaner.Core;

public enum ScanMode { Quick, Deep }
public enum RiskLevel { Safe, Rebuildable, Review, Protected }
public enum ItemCategory { CodexData, Worktree, Dependency, BuildArtifact, Cache, VirtualEnvironment, AiModel, InstalledTool, LargeFile, Duplicate, System, UserFiles, Database, Configuration, Other }
public enum TaskActivity { Active, Recent, Normal, Review, Stale, Unknown }
public enum AttributionLevel { Confirmed, High, Possible, Unknown }
public enum ScanState { Idle, Running, Completed, Partial, Cancelled, Failed }
public enum CleanupDisposition { PermanentDelete, RecycleBin, Quarantine, GitWorktreeRemove, Disabled }
public enum ScanCompleteness { Complete, Partial, Cancelled, Failed }

public sealed record StorageItem(string Path, string Name, long SizeBytes, ItemCategory Category, RiskLevel Risk, DateTimeOffset LastWriteTime, int FileCount = 0, string? Detail = null, bool IsDirectory = true, string? Source = null, bool IsReparsePoint = false);
public sealed record GitStatus(bool IsRepository, string? Branch, bool HasChanges, bool HasUntracked, string? Error = null) { public static readonly GitStatus NotRepository = new(false, null, false, false); }
public sealed record CodexTask(string Id, string Name, string RootPath, long SizeBytes, DateTimeOffset LastActivity, TaskActivity Activity, bool IsGitWorktree, bool HasChanges, bool HasUntracked, IReadOnlyList<StorageItem> Artifacts, int SessionCount = 1, string? Branch = null, int FileCount = 0);
public sealed record DriveSummary(string Name, long TotalBytes, long FreeBytes, string FileSystem = "未知") { public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes); public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100d / TotalBytes; }
public sealed record CategorySummary(ItemCategory Category, long SizeBytes, int ItemCount, RiskLevel Risk);
public sealed record CategoryDelta(ItemCategory Category, long DeltaBytes);
public sealed record ScanIssue(string Path, string Stage, string Message, bool IsPermissionIssue = false);
public sealed record ScanCoverage(ScanMode Mode, IReadOnlyList<string> Roots, ScanCompleteness Completeness, int FailedPathCount, int PermissionDeniedCount, long UncountedBytes = 0);
public sealed record ScanResult(Guid SessionId, ScanMode Mode, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, DriveSummary Drive, IReadOnlyList<CategorySummary> Categories, IReadOnlyList<CodexTask> Tasks, IReadOnlyList<StorageItem> Candidates, IReadOnlyList<string> Warnings, bool IsComplete, IReadOnlyList<CategoryDelta>? Changes = null, ScanCoverage? Coverage = null, IReadOnlyList<ScanIssue>? Issues = null, IReadOnlyList<InstalledTool>? Tools = null) { public bool CanContributeToHistory => IsComplete && (Coverage is null || Coverage.Completeness == ScanCompleteness.Complete); }
public sealed record ScanProgress(string Stage, int Completed, int Total, string? CurrentPath = null);

public sealed record CleanupCandidate(StorageItem Item, bool Selected, string Impact, bool RequiresElevation = false, bool SendToRecycleBin = false, CleanupDisposition Disposition = CleanupDisposition.Disabled);
public sealed record CleanupPlanItem(string Path, string NormalizedPath, string VolumeRoot, long ExpectedBytes, DateTimeOffset ExpectedLastWriteTime, RiskLevel Risk, CleanupDisposition Disposition, bool WasDirectory, bool WasReparsePoint, string Impact, string? Source = null);
public sealed record CleanupPlan(Guid Id, IReadOnlyList<CleanupCandidate> Items, DateTimeOffset CreatedAt, IReadOnlyList<CleanupPlanItem>? ImmutableItems = null) { public long SelectedBytes => Items.Where(x => x.Selected).Sum(x => x.Item.SizeBytes); }
public sealed record CleanupItemResult(string Path, bool Success, long ReleasedBytes, string? Error, CleanupDisposition Disposition = CleanupDisposition.Disabled, bool Recoverable = false);
public sealed record CleanupResult(Guid PlanId, DateTimeOffset CompletedAt, IReadOnlyList<CleanupItemResult> Items);
public sealed record CleanupRecord(Guid PlanId, string Path, long ReleasedBytes, RiskLevel Risk, CleanupDisposition Disposition, bool Success, string? Error, DateTimeOffset CompletedAt);
public sealed record QuarantineEntry(Guid Id, string OriginalPath, string QuarantinePath, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, long SizeBytes);
public enum MigrationKind { NpmCache, PipCache, UvCache, PlaywrightBrowsers }
public enum MigrationState { Planned, Copied, Configured, Verified, AwaitingSourceCleanup, Completed, Failed, Cancelled }
public sealed record MigrationItem(MigrationKind Kind, string DisplayName, string SourcePath, string TargetPath, long ExpectedBytes, int ExpectedFiles, DateTimeOffset ExpectedLastWriteTime, string ConfigKey, string? OriginalValue = null);
public sealed record MigrationPlan(Guid Id, string TargetRoot, DateTimeOffset CreatedAt, IReadOnlyList<MigrationItem> Items);
public sealed record MigrationResult(Guid PlanId, MigrationState State, IReadOnlyList<MigrationRecord> Records, string? Error = null);
public sealed record MigrationRecord(Guid PlanId, MigrationKind Kind, string SourcePath, string TargetPath, long Bytes, MigrationState State, string? Message, DateTimeOffset CompletedAt);
public sealed record UpdateInfo(Version Version, string Tag, string Name, string Notes, DateTimeOffset PublishedAt, Uri ReleaseUrl, Uri? InstallerUrl);
public sealed record UpdateCheckResult(bool Success, bool IsUpdateAvailable, UpdateInfo? Update, string Message, DateTimeOffset CheckedAt);

public sealed record AttributionEvidence(string Kind, string Description, int Score, DateTimeOffset? Timestamp = null);
public sealed record AttributionResult(AttributionLevel Level, int Score, IReadOnlyList<AttributionEvidence> Evidence);
public sealed record DuplicateGroup(string Hash, long SizeBytes, IReadOnlyList<string> Paths);
public sealed record PathSnapshot(string Path, ItemCategory Category, long SizeBytes, int FileCount, DateTimeOffset CapturedAt);
public sealed record InstalledTool(string Id, string Name, string? Version, string? InstallPath, long SizeBytes, DateTimeOffset? InstalledAt, AttributionResult Attribution, bool IsDetected, string Kind, string? UninstallCommand = null);
public sealed record AppSettings(string? CodexHome, IReadOnlyList<string> AdditionalRoots, string Theme, int QuarantineDays, bool ConfirmBeforeDelete, bool CheckGitBeforeDelete, bool ReduceMotion, IReadOnlyList<string> ProtectedPaths, IReadOnlyList<string> IgnoredPaths, bool AutoCheckUpdates = true, DateTimeOffset? LastUpdateCheckAt = null) { public static AppSettings Default { get; } = new(null, [], "System", 14, true, true, false, [], [], true); }
public sealed record NavigationRequest(AppPageId Page, string? EntityId = null);
public enum AppPageId { Dashboard, CodexUsage, Tasks, WorktreeDetails, Cleanup, Developer, Tools, SpaceChanges, LargeFiles, Duplicates, History, Settings }

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];
    public static string Format(long bytes) { var value = Math.Max(0, bytes); var unit = 0; double display = value; while (display >= 1024 && unit < Units.Length - 1) { display /= 1024; unit++; } return unit == 0 ? $"{display:0} {Units[unit]}" : $"{display:0.0} {Units[unit]}"; }
}

public static class RiskRules
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase) { ".git", ".env", ".env.local", "src", "app", "pages", "components", "assets", "public", "lib", "tests", "database", "data", "documents", "pictures", "videos", "desktop", "downloads" };
    private static readonly HashSet<string> SafeNames = new(StringComparer.OrdinalIgnoreCase) { "temp", "tmp", "cache", "logs", "crashdumps", "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache", "http-cache", "plugins-cache" };
    private static readonly HashSet<string> RebuildableNames = new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".next", ".nuxt", "dist", "build", "out", "coverage", "bin", "obj", "target" };
    private static readonly string[] ModelExtensions = [".safetensors", ".gguf", ".ckpt", ".pt", ".pth", ".onnx"];
    private static readonly string[] DatabaseExtensions = [".db", ".sqlite", ".sqlite3", ".mdb", ".accdb"];
    private static readonly string[] SourceExtensions = [".cs", ".csx", ".xaml", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".java", ".kt", ".kts", ".py", ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte", ".swift", ".rb", ".php", ".fs", ".fsx"];
    private static readonly string[] ConfigurationExtensions = [".sln", ".csproj", ".fsproj", ".vcxproj", ".props", ".targets", ".json", ".jsonc", ".yaml", ".yml", ".toml", ".ini", ".config", ".xml", ".lock"];
    private static readonly string[] UserFileExtensions = [".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".md", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".mp3", ".wav", ".mp4", ".mov", ".zip", ".7z", ".rar"];
    public static (ItemCategory Category, RiskLevel Risk, string Detail) Classify(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (ProtectedNames.Contains(name) || name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) return (name.Contains("database", StringComparison.OrdinalIgnoreCase) ? ItemCategory.Database : ItemCategory.Other, RiskLevel.Protected, "源码、Git、配置或用户数据默认保护");
        if (RebuildableNames.Contains(name)) return (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ? ItemCategory.Dependency : ItemCategory.BuildArtifact, RiskLevel.Rebuildable, "可重新生成，后续构建可能耗时");
        if (SafeNames.Contains(name) || name.EndsWith("cache", StringComparison.OrdinalIgnoreCase)) return (ItemCategory.Cache, RiskLevel.Safe, "明确的缓存或临时数据");
        if (name.Equals(".venv", StringComparison.OrdinalIgnoreCase) || name.Equals("venv", StringComparison.OrdinalIgnoreCase) || name.Equals("env", StringComparison.OrdinalIgnoreCase)) return (ItemCategory.VirtualEnvironment, RiskLevel.Review, "项目虚拟环境，删除后需要重新创建");
        if (ModelExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return (ItemCategory.AiModel, RiskLevel.Protected, "AI 模型主文件默认保护");
        if (DatabaseExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return (ItemCategory.Database, RiskLevel.Protected, "数据库文件默认保护");
        if (SourceExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return (ItemCategory.Other, RiskLevel.Protected, "项目源码默认保护");
        if (ConfigurationExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return (ItemCategory.Configuration, RiskLevel.Protected, "项目配置默认保护");
        if (UserFileExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return (ItemCategory.UserFiles, RiskLevel.Protected, "用户资源或文档默认保护");
        return (ItemCategory.Other, RiskLevel.Review, "需要确认用途后再清理");
    }
    public static bool IsProtectedPath(string path, IEnumerable<string>? userProtected = null)
    {
        var full = Path.GetFullPath(path); var forbiddenRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
        if (forbiddenRoots.Any(root => !string.IsNullOrWhiteSpace(root) && IsSameOrChild(full, root))) return true;
        if (userProtected?.Any(root => !string.IsNullOrWhiteSpace(root) && IsSameOrChild(full, root)) == true) return true;
        return full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => ProtectedNames.Contains(p) || p.StartsWith(".env", StringComparison.OrdinalIgnoreCase));
    }
    public static bool IsSameOrChild(string path, string root) { var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
}

public static class AttributionEngine
{
    public static AttributionResult Score(IEnumerable<AttributionEvidence> evidence) { var list = evidence.ToList(); var score = Math.Clamp(list.Sum(x => x.Score), 0, 100); var level = list.Any(x => x.Kind.Equals("direct-command", StringComparison.OrdinalIgnoreCase) || x.Kind.Equals("command", StringComparison.OrdinalIgnoreCase)) ? AttributionLevel.Confirmed : score switch { >= 70 => AttributionLevel.High, >= 40 => AttributionLevel.Possible, _ => AttributionLevel.Unknown }; return new AttributionResult(level, score, list); }
}

public static class Hashing
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken) { await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)); }
    public static async Task<string> QuickHashAsync(string path, CancellationToken cancellationToken) { const int blockSize = 64 * 1024; await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); var first = new byte[Math.Min(blockSize, checked((int)Math.Min(stream.Length, blockSize)))]; _ = await stream.ReadAsync(first, cancellationToken); var last = Array.Empty<byte>(); if (stream.Length > blockSize) { stream.Position = Math.Max(0, stream.Length - blockSize); last = new byte[Math.Min(blockSize, checked((int)(stream.Length - stream.Position)))]; _ = await stream.ReadAsync(last, cancellationToken); } return Convert.ToHexString(SHA256.HashData(first.Concat(last).ToArray())); }
}

namespace CodexCleaner.Core;

public interface IScanCoordinator { IAsyncEnumerable<ScanProgress> ScanAsync(ScanMode mode, CancellationToken cancellationToken); ScanResult? LastResult { get; } }
public interface ICodexScanner { Task<(string Home, IReadOnlyList<StorageItem> Items, IReadOnlyList<string> Warnings)> ScanAsync(CancellationToken cancellationToken); }
public interface ICodexProjectService { Task<IReadOnlyList<CodexTask>> DiscoverAsync(CancellationToken cancellationToken); }
public interface IProjectScanner : ICodexProjectService { }
public interface IStorageScanner { Task<IReadOnlyList<StorageItem>> ScanAsync(IEnumerable<string> roots, ScanMode mode, IProgress<ScanIssue>? issues, CancellationToken cancellationToken); }
public interface IDeveloperEnvironmentScanner { Task<IReadOnlyList<StorageItem>> ScanAsync(CancellationToken cancellationToken); }
public interface IInstalledToolScanner { Task<IReadOnlyList<InstalledTool>> ScanAsync(CancellationToken cancellationToken); }
public interface IAttributionService { AttributionResult Attribute(InstalledTool tool, IEnumerable<CodexTask> tasks); }
public interface IRiskService { (ItemCategory Category, RiskLevel Risk, string Detail) Classify(string path); bool IsProtected(string path, IEnumerable<string>? protectedRoots = null); }
public interface IGitStatusService { Task<GitStatus> GetStatusAsync(string path, CancellationToken cancellationToken); Task<IReadOnlyList<string>> GetWorktreesAsync(string path, CancellationToken cancellationToken); }
public interface IDuplicateService { Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(IEnumerable<string> roots, long minimumSize, CancellationToken cancellationToken); }
public interface IHistoryService { Task SaveAsync(ScanResult result, CancellationToken cancellationToken); Task<IReadOnlyList<PathSnapshot>> GetRecentAsync(int days, CancellationToken cancellationToken); Task<IReadOnlyList<CleanupRecord>> GetCleanupRecordsAsync(CancellationToken cancellationToken); Task SaveCleanupAsync(CleanupResult result, IReadOnlyList<CleanupPlanItem> items, CancellationToken cancellationToken); }
public interface ISettingsService { Task<AppSettings> LoadAsync(CancellationToken cancellationToken); Task SaveAsync(AppSettings settings, CancellationToken cancellationToken); }
public interface IInsightService { IReadOnlyList<string> BuildInsights(ScanResult result); }
public interface ICleanupService { CleanupPlan CreatePlan(IEnumerable<CleanupCandidate> candidates); Task<CleanupResult> ExecuteAsync(CleanupPlan plan, CancellationToken cancellationToken); Task<IReadOnlyList<QuarantineEntry>> GetQuarantineAsync(CancellationToken cancellationToken); Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken); }
public interface IMigrationService { Task<IReadOnlyList<MigrationItem>> DiscoverAsync(string targetRoot, IEnumerable<StorageItem> items, CancellationToken cancellationToken); Task<MigrationResult> ExecuteAsync(MigrationPlan plan, CancellationToken cancellationToken); Task<IReadOnlyList<MigrationRecord>> GetRecordsAsync(CancellationToken cancellationToken); }
public interface IUpdateService { Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken); }
public interface IExternalCommandRunner { Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken); }
public sealed record CommandResult(bool Found, int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

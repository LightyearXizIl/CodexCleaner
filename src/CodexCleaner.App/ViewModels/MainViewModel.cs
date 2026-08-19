using CommunityToolkit.Mvvm.ComponentModel;
using CodexCleaner.Core;
using CodexCleaner.Services;

namespace CodexCleaner.App.ViewModels;

public partial class MainViewModel(IScanCoordinator scanner, ICleanupService cleanup, IDuplicateService duplicates, IHistoryService history, ISettingsService settings, IMigrationService migration, IUpdateService updates) : ObservableObject
{
    private CancellationTokenSource? _scanCancellation;
    [ObservableProperty] private ScanState scanState = ScanState.Idle;
    [ObservableProperty] private string scanMessage = "准备扫描";
    [ObservableProperty] private double scanProgress;
    [ObservableProperty] private ScanResult? result;
    [ObservableProperty] private string? selectedTaskId;
    [ObservableProperty] private IReadOnlyList<DuplicateGroup> duplicateGroups = [];
    [ObservableProperty] private IReadOnlyList<CleanupRecord> cleanupRecords = [];
    [ObservableProperty] private AppSettings currentSettings = AppSettings.Default;
    [ObservableProperty] private string duplicateMessage = "尚未执行重复文件检测。";
    [ObservableProperty] private IReadOnlyList<MigrationRecord> migrationRecords = [];
    [ObservableProperty] private IReadOnlyList<MigrationItem> migrationCandidates = [];
    [ObservableProperty] private string migrationMessage = "请选择其他磁盘上的目标文件夹以生成迁移计划。";
    [ObservableProperty] private string migrationTargetPath = string.Empty;
    [ObservableProperty] private UpdateCheckResult? updateResult;
    // The unpackaged portable build keeps its own atomic local history store;
    // the MSIX build uses the SQLite/WAL implementation registered by its host.
    public string DatabaseLabel => history is SqliteHistoryService ? "数据库：codexcleaner.db" : "本地历史：portable-history.json";
    public CodexTask? SelectedTask => Result?.Tasks.FirstOrDefault(x => x.Id == SelectedTaskId);
    public Version CurrentVersion => new(0, 0, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        CurrentSettings = await settings.LoadAsync(cancellationToken);
        await LoadHistoryAsync(cancellationToken);
        MigrationRecords = await migration.GetRecordsAsync(cancellationToken);
        if (CurrentSettings.AutoCheckUpdates) _ = CheckForUpdatesAsync(cancellationToken);
    }

    public async Task RefreshAsync(ScanMode mode = ScanMode.Quick, CancellationToken cancellationToken = default)
    {
        _scanCancellation?.Cancel(); _scanCancellation?.Dispose(); _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ScanState = ScanState.Running; ScanProgress = 0;
        try
        {
            await foreach (var progress in scanner.ScanAsync(mode, _scanCancellation.Token)) { ScanMessage = progress.Stage; ScanProgress = progress.Total == 0 ? 0 : progress.Completed * 100d / progress.Total; }
            Result = scanner.LastResult; if (SelectedTaskId is not null && SelectedTask is null) SelectedTaskId = null;
            ScanState = Result?.IsComplete == true ? ScanState.Completed : ScanState.Partial;
        }
        catch (OperationCanceledException) { ScanState = ScanState.Cancelled; ScanMessage = "扫描已取消"; }
        catch (Exception ex) { ScanState = ScanState.Failed; ScanMessage = $"扫描失败：{ex.Message}"; }
    }

    public void CancelScan() => _scanCancellation?.Cancel();
    public void SelectTask(string id) { SelectedTaskId = id; OnPropertyChanged(nameof(SelectedTask)); }
    public CleanupPlan CreateCleanupPlan()
    {
        var candidates = Result?.Candidates.Where(x => x.Risk == RiskLevel.Rebuildable || (x.Risk == RiskLevel.Safe && x.Source is "Codex Home" or "开发环境")).OrderByDescending(x => x.SizeBytes).Take(80).Select(x => new CleanupCandidate(x, x.Risk == RiskLevel.Safe, x.Detail ?? "清理前会再次检查路径", Disposition: x.Risk == RiskLevel.Safe ? CleanupDisposition.PermanentDelete : CleanupDisposition.Quarantine)) ?? [];
        return cleanup.CreatePlan(candidates);
    }
    public CleanupPlan CreateCleanupPlan(IEnumerable<CleanupCandidate> candidates) => cleanup.CreatePlan(candidates);
    public async Task<CleanupResult> ExecuteAsync(CleanupPlan plan, CancellationToken cancellationToken) { var result = await cleanup.ExecuteAsync(plan, cancellationToken); await LoadHistoryAsync(cancellationToken); return result; }
    public async Task FindDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        var roots = Result?.Tasks.Select(x => x.RootPath).Concat(CurrentSettings.AdditionalRoots).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (roots.Count == 0) { DuplicateGroups = []; DuplicateMessage = "没有可访问的项目根可用于哈希检测。"; return; }
        DuplicateMessage = "正在按大小、快速哈希和 SHA-256 检测重复文件…";
        try { DuplicateGroups = await duplicates.FindDuplicatesAsync(roots, 10L * 1024 * 1024, cancellationToken); DuplicateMessage = DuplicateGroups.Count == 0 ? "未发现完全重复的可访问文件。" : $"发现 {DuplicateGroups.Count} 组完全重复文件。"; }
        catch (OperationCanceledException) { DuplicateMessage = "重复文件检测已取消。"; }
        catch (Exception ex) { DuplicateMessage = $"重复文件检测失败：{ex.Message}"; }
    }
    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default) => CleanupRecords = await history.GetCleanupRecordsAsync(cancellationToken);
    public async Task SaveSettingsAsync(AppSettings value, CancellationToken cancellationToken = default) { await settings.SaveAsync(value, cancellationToken); CurrentSettings = value; }
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        UpdateResult = await updates.CheckAsync(CurrentVersion, cancellationToken);
        if (CurrentSettings.AutoCheckUpdates) await SaveSettingsAsync(CurrentSettings with { LastUpdateCheckAt = UpdateResult.CheckedAt }, cancellationToken);
    }
    public async Task DiscoverMigrationAsync(string targetRoot, CancellationToken cancellationToken = default)
    {
        MigrationTargetPath = targetRoot;
        MigrationCandidates = await migration.DiscoverAsync(targetRoot, Result?.Candidates ?? [], cancellationToken);
        MigrationMessage = MigrationCandidates.Count == 0 ? "没有可安全迁移的 npm、pip、uv 或 Playwright 缓存。" : $"已找到 {MigrationCandidates.Count} 项可迁移缓存；源缓存会保留至二次确认。";
    }
    public async Task<MigrationResult> ExecuteMigrationAsync(string targetRoot, IEnumerable<MigrationItem> selected, CancellationToken cancellationToken = default)
    {
        var plan = new MigrationPlan(Guid.NewGuid(), targetRoot, DateTimeOffset.UtcNow, selected.ToList());
        var result = await migration.ExecuteAsync(plan, cancellationToken); MigrationRecords = await migration.GetRecordsAsync(cancellationToken); MigrationMessage = result.Error ?? "迁移完成，C 盘源缓存仍保留，尚未释放空间。"; return result;
    }
}

using System.Text.Json;
using CodexCleaner.Core;
using Microsoft.Data.Sqlite;

namespace CodexCleaner.Services;

public sealed class SqliteHistoryService : IHistoryService
{
    private readonly string _databasePath;
    public SqliteHistoryService(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner", "codexcleaner.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
    }

    public async Task SaveAsync(ScanResult result, CancellationToken cancellationToken)
    {
        if (!result.CanContributeToHistory) return;
        await using var connection = await OpenAsync(cancellationToken); await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertAsync(connection, tx, "INSERT OR REPLACE INTO ScanSessions (Id,StartedAt,FinishedAt,DriveUsed,DriveFree,IsComplete,Mode,Completeness) VALUES ($id,$start,$end,$used,$free,1,$mode,$completeness)", new Dictionary<string, object?> { ["$id"] = result.SessionId.ToString(), ["$start"] = result.StartedAt.UtcDateTime, ["$end"] = result.FinishedAt.UtcDateTime, ["$used"] = result.Drive.UsedBytes, ["$free"] = result.Drive.FreeBytes, ["$mode"] = (int)result.Mode, ["$completeness"] = (int)(result.Coverage?.Completeness ?? ScanCompleteness.Complete) }, cancellationToken);
        foreach (var item in result.Candidates.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(x => x.First())) await InsertAsync(connection, tx, "INSERT INTO PathSnapshots (SessionId,Path,Category,SizeBytes,FileCount,CapturedAt) VALUES ($session,$path,$category,$size,$count,$captured)", new Dictionary<string, object?> { ["$session"] = result.SessionId.ToString(), ["$path"] = item.Path, ["$category"] = (int)item.Category, ["$size"] = item.SizeBytes, ["$count"] = item.FileCount, ["$captured"] = result.FinishedAt.UtcDateTime }, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PathSnapshot>> GetRecentAsync(int days, CancellationToken cancellationToken)
    {
        var list = new List<PathSnapshot>(); if (!File.Exists(_databasePath)) return list; await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Path,Category,SizeBytes,FileCount,CapturedAt FROM PathSnapshots WHERE CapturedAt >= $since ORDER BY CapturedAt DESC"; command.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.AddDays(-days).UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) list.Add(new PathSnapshot(reader.GetString(0), (ItemCategory)reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetDateTime(4)));
        return list;
    }

    public async Task SaveCleanupAsync(CleanupResult result, IReadOnlyList<CleanupPlanItem> items, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken); var map = items.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in result.Items)
        {
            map.TryGetValue(entry.Path, out var plan); await InsertAsync(connection, tx, "INSERT INTO CleanupItems (PlanId,Path,ReleasedBytes,Risk,Disposition,Success,Error,CompletedAt) VALUES ($plan,$path,$released,$risk,$disposition,$success,$error,$completed)", new Dictionary<string, object?> { ["$plan"] = result.PlanId.ToString(), ["$path"] = entry.Path, ["$released"] = entry.ReleasedBytes, ["$risk"] = (int)(plan?.Risk ?? RiskLevel.Protected), ["$disposition"] = (int)entry.Disposition, ["$success"] = entry.Success ? 1 : 0, ["$error"] = entry.Error, ["$completed"] = result.CompletedAt.UtcDateTime }, cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CleanupRecord>> GetCleanupRecordsAsync(CancellationToken cancellationToken)
    {
        var list = new List<CleanupRecord>(); if (!File.Exists(_databasePath)) return list; await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand(); command.CommandText = "SELECT PlanId,Path,ReleasedBytes,Risk,Disposition,Success,Error,CompletedAt FROM CleanupItems ORDER BY CompletedAt DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) list.Add(new CleanupRecord(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2), (RiskLevel)reader.GetInt32(3), (CleanupDisposition)reader.GetInt32(4), reader.GetInt32(5) == 1, reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDateTime(7)));
        return list;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(ct);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY); CREATE TABLE IF NOT EXISTS ScanSessions (Id TEXT PRIMARY KEY, StartedAt TEXT NOT NULL, FinishedAt TEXT NOT NULL, DriveUsed INTEGER NOT NULL, DriveFree INTEGER NOT NULL, IsComplete INTEGER NOT NULL, Mode INTEGER NOT NULL DEFAULT 0, Completeness INTEGER NOT NULL DEFAULT 0); CREATE TABLE IF NOT EXISTS PathSnapshots (SessionId TEXT NOT NULL, Path TEXT NOT NULL, Category INTEGER NOT NULL, SizeBytes INTEGER NOT NULL, FileCount INTEGER NOT NULL, CapturedAt TEXT NOT NULL); CREATE TABLE IF NOT EXISTS CleanupItems (PlanId TEXT NOT NULL, Path TEXT NOT NULL, ReleasedBytes INTEGER NOT NULL, Risk INTEGER NOT NULL, Disposition INTEGER NOT NULL, Success INTEGER NOT NULL, Error TEXT NULL, CompletedAt TEXT NOT NULL); CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS ProtectedPaths (Path TEXT PRIMARY KEY); CREATE TABLE IF NOT EXISTS IgnoreRules (Path TEXT PRIMARY KEY);");
        // Existing installations used the first six ScanSessions columns. Apply the
        // additive migration before issuing inserts, so their history remains readable.
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(ScanSessions)";
            await using var reader = await pragma.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
        }
        if (!columns.Contains("Mode")) await ExecuteAsync(connection, "ALTER TABLE ScanSessions ADD COLUMN Mode INTEGER NOT NULL DEFAULT 0;");
        if (!columns.Contains("Completeness")) await ExecuteAsync(connection, "ALTER TABLE ScanSessions ADD COLUMN Completeness INTEGER NOT NULL DEFAULT 0;");
        await ExecuteAsync(connection, "INSERT OR IGNORE INTO SchemaMigrations (Version) VALUES (2);");
        return connection;
    }
    private static async Task ExecuteAsync(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task InsertAsync(SqliteConnection connection, SqliteTransaction tx, string sql, IReadOnlyDictionary<string, object?> values, CancellationToken ct) { using var command = connection.CreateCommand(); command.Transaction = tx; command.CommandText = sql; foreach (var value in values) command.Parameters.AddWithValue(value.Key, value.Value ?? DBNull.Value); await command.ExecuteNonQueryAsync(ct); }
}

/// <summary>
/// Runs only when the application is launched without MSIX package identity.
/// Microsoft.Data.Sqlite probes the WinRT package store during type initialization
/// on this Windows App SDK combination, which terminates an unpackaged process.
/// The portable edition therefore keeps the same real local history semantics in
/// an atomic JSON store; the MSIX edition continues to use SQLite/WAL above.
/// </summary>
public sealed class PortableJsonHistoryService : IHistoryService
{
    private readonly string _path;
    public PortableJsonHistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner", "portable-history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }
    public async Task SaveAsync(ScanResult result, CancellationToken cancellationToken)
    {
        if (!result.CanContributeToHistory) return;
        var state = await LoadAsync(cancellationToken);
        state.Snapshots.RemoveAll(x => x.CapturedAt < DateTimeOffset.UtcNow.AddDays(-90));
        state.Snapshots.AddRange(result.Candidates.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).Select(x => new PathSnapshot(x.Path, x.Category, x.SizeBytes, x.FileCount, result.FinishedAt)));
        await SaveAsync(state, cancellationToken);
    }
    public async Task<IReadOnlyList<PathSnapshot>> GetRecentAsync(int days, CancellationToken cancellationToken)
        => (await LoadAsync(cancellationToken)).Snapshots.Where(x => x.CapturedAt >= DateTimeOffset.UtcNow.AddDays(-days)).OrderByDescending(x => x.CapturedAt).ToList();
    public async Task SaveCleanupAsync(CleanupResult result, IReadOnlyList<CleanupPlanItem> items, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken); var plans = items.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        state.CleanupRecords.AddRange(result.Items.Select(x => new CleanupRecord(result.PlanId, x.Path, x.ReleasedBytes, plans.TryGetValue(x.Path, out var item) ? item.Risk : RiskLevel.Protected, x.Disposition, x.Success, x.Error, result.CompletedAt)));
        await SaveAsync(state, cancellationToken);
    }
    public async Task<IReadOnlyList<CleanupRecord>> GetCleanupRecordsAsync(CancellationToken cancellationToken)
        => (await LoadAsync(cancellationToken)).CleanupRecords.OrderByDescending(x => x.CompletedAt).ToList();
    private async Task<State> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new State();
        try { await using var stream = File.OpenRead(_path); return await JsonSerializer.DeserializeAsync<State>(stream, cancellationToken: cancellationToken) ?? new State(); }
        catch (IOException) { return new State(); }
        catch (JsonException) { return new State(); }
    }
    private async Task SaveAsync(State state, CancellationToken cancellationToken)
    {
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken);
        File.Move(temp, _path, true);
    }
    private sealed class State
    {
        public List<PathSnapshot> Snapshots { get; set; } = [];
        public List<CleanupRecord> CleanupRecords { get; set; } = [];
    }
}

public sealed class InsightService : IInsightService
{
    public IReadOnlyList<string> BuildInsights(ScanResult result)
    {
        var lines = new List<string>(); var largest = result.Categories.OrderByDescending(x => x.SizeBytes).FirstOrDefault(); if (largest is not null) lines.Add($"当前最大来源是{CategoryName(largest.Category)}，占用 {ByteSizeFormatter.Format(largest.SizeBytes)}。");
        var safe = result.Candidates.Where(x => x.Risk == RiskLevel.Safe).Sum(x => x.SizeBytes); if (safe > 0) lines.Add($"已识别 {ByteSizeFormatter.Format(safe)} 的明确缓存；清理前仍需确认。");
        if (result.Changes?.Count > 0) { var growth = result.Changes.OrderByDescending(x => x.DeltaBytes).First(); if (growth.DeltaBytes > 0) lines.Add($"相较上一份完整快照，{CategoryName(growth.Category)} 增加 {ByteSizeFormatter.Format(growth.DeltaBytes)}。"); }
        return lines;
    }
    private static string CategoryName(ItemCategory category) => category switch { ItemCategory.Worktree => "Codex Worktree", ItemCategory.Dependency => "项目依赖", ItemCategory.BuildArtifact => "构建产物", ItemCategory.Cache => "开发缓存", ItemCategory.AiModel => "AI 模型", _ => "其他内容" };
}

public sealed class CleanupService(ISettingsService settings, IRiskService risk, IGitStatusService git, IHistoryService history) : ICleanupService
{
    public CleanupService() : this(new AppSettingsService(), new RiskService(), new GitStatusService(new ExternalCommandRunner()), new SqliteHistoryService()) { }
    private readonly string _quarantineDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner", "Quarantine");
    private readonly string _quarantineIndex = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner", "quarantine.json");
    public CleanupPlan CreatePlan(IEnumerable<CleanupCandidate> candidates)
    {
        var selected = candidates.Where(x => x.Selected).ToList(); var frozen = selected.Select(ToPlanItem).ToList(); return new CleanupPlan(Guid.NewGuid(), candidates.ToList(), DateTimeOffset.UtcNow, frozen);
    }

    public async Task<CleanupResult> ExecuteAsync(CleanupPlan plan, CancellationToken cancellationToken)
    {
        if (plan.ImmutableItems is null || plan.ImmutableItems.Count == 0) return new CleanupResult(plan.Id, DateTimeOffset.UtcNow, [new CleanupItemResult("", false, 0, "清理计划无效或未包含已冻结的安全项。")]);
        var appSettings = await settings.LoadAsync(cancellationToken); var results = new List<CleanupItemResult>();
        foreach (var item in plan.ImmutableItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Validate(item, appSettings); var release = item.ExpectedBytes;
                switch (item.Disposition)
                {
                    case CleanupDisposition.PermanentDelete when item.Risk == RiskLevel.Safe:
                        if (item.WasDirectory) Directory.Delete(item.NormalizedPath, true); else File.Delete(item.NormalizedPath);
                        results.Add(new CleanupItemResult(item.Path, true, release, null, item.Disposition)); break;
                    case CleanupDisposition.Quarantine when item.Risk is RiskLevel.Rebuildable or RiskLevel.Review:
                        if (appSettings.CheckGitBeforeDelete) { var status = await git.GetStatusAsync(Path.GetDirectoryName(item.NormalizedPath) ?? item.NormalizedPath, cancellationToken); if (status.HasChanges || status.HasUntracked) throw new InvalidOperationException("项目存在未提交或未跟踪文件，已阻止移动到隔离区。"); }
                        var entry = await MoveToQuarantineAsync(item, appSettings.QuarantineDays, cancellationToken); results.Add(new CleanupItemResult(item.Path, true, IsOnSystemDrive(entry.QuarantinePath) ? 0 : release, IsOnSystemDrive(entry.QuarantinePath) ? "已移入 C 盘隔离区，空间待隔离区清除后释放。" : null, item.Disposition, true)); break;
                    default: throw new InvalidOperationException("该项不符合可执行清理策略。");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FileNotFoundException) { results.Add(new CleanupItemResult(item.Path, false, 0, ex.Message, item.Disposition)); }
        }
        var result = new CleanupResult(plan.Id, DateTimeOffset.UtcNow, results); await history.SaveCleanupAsync(result, plan.ImmutableItems, cancellationToken); return result;
    }

    public async Task<IReadOnlyList<QuarantineEntry>> GetQuarantineAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_quarantineIndex)) return []; try { await using var stream = File.OpenRead(_quarantineIndex); return await JsonSerializer.DeserializeAsync<List<QuarantineEntry>>(stream, cancellationToken: cancellationToken) ?? []; } catch (JsonException) { return []; } catch (IOException) { return []; }
    }

    public async Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var entries = (await GetQuarantineAsync(cancellationToken)).ToList(); var entry = entries.FirstOrDefault(x => x.Id == id); if (entry is null || !Directory.Exists(entry.QuarantinePath) && !File.Exists(entry.QuarantinePath) || Directory.Exists(entry.OriginalPath) || File.Exists(entry.OriginalPath)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!); if (Directory.Exists(entry.QuarantinePath)) Directory.Move(entry.QuarantinePath, entry.OriginalPath); else File.Move(entry.QuarantinePath, entry.OriginalPath); entries.Remove(entry); await SaveQuarantineAsync(entries, cancellationToken); return true;
    }

    private CleanupPlanItem ToPlanItem(CleanupCandidate candidate)
    {
        var item = candidate.Item; var path = Path.GetFullPath(item.Path); var disposition = candidate.Disposition != CleanupDisposition.Disabled ? candidate.Disposition : item.Risk == RiskLevel.Safe ? CleanupDisposition.PermanentDelete : item.Risk == RiskLevel.Rebuildable ? CleanupDisposition.Quarantine : CleanupDisposition.Disabled;
        // A generic directory named "cache" inside a project is not evidence that
        // it is safe to permanently remove. Only scanner-owned cache roots get the
        // Safe deletion path; project content requires a separate review flow.
        if (disposition == CleanupDisposition.PermanentDelete && item.Source is not ("Codex Home" or "开发环境")) disposition = CleanupDisposition.Disabled;
        return new CleanupPlanItem(item.Path, path, Path.GetPathRoot(path) ?? string.Empty, item.SizeBytes, item.LastWriteTime, item.Risk, disposition, item.IsDirectory, item.IsReparsePoint, candidate.Impact, item.Source);
    }
    private void Validate(CleanupPlanItem item, AppSettings appSettings)
    {
        if (item.Risk == RiskLevel.Protected || risk.IsProtected(item.NormalizedPath, appSettings.ProtectedPaths)) throw new InvalidOperationException("该路径受保护，无法清理。");
        if (item.Disposition == CleanupDisposition.PermanentDelete && item.Source is not ("Codex Home" or "开发环境")) throw new InvalidOperationException("未验证为受管缓存来源，已阻止永久删除。");
        if (!Path.GetPathRoot(item.NormalizedPath)!.Equals(item.VolumeRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("卷发生变化，已阻止清理。");
        if (DirectorySizer.IsReparsePoint(item.NormalizedPath) || HasReparseParent(item.NormalizedPath)) throw new InvalidOperationException("路径或父级包含重解析点，已阻止清理。");
        if (item.WasDirectory && !Directory.Exists(item.NormalizedPath) || !item.WasDirectory && !File.Exists(item.NormalizedPath)) throw new FileNotFoundException("路径已不存在", item.NormalizedPath);
        var modified = item.WasDirectory ? Directory.GetLastWriteTimeUtc(item.NormalizedPath) : File.GetLastWriteTimeUtc(item.NormalizedPath); if (Math.Abs((modified - item.ExpectedLastWriteTime.UtcDateTime).TotalSeconds) > 2) throw new InvalidOperationException("路径在生成计划后已修改，请重新扫描。");
    }
    private static bool HasReparseParent(string path) { var parent = Directory.GetParent(path); while (parent is not null) { if (DirectorySizer.IsReparsePoint(parent.FullName)) return true; parent = parent.Parent; } return false; }
    private async Task<QuarantineEntry> MoveToQuarantineAsync(CleanupPlanItem item, int days, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_quarantineDirectory); var target = Path.Combine(_quarantineDirectory, $"{Guid.NewGuid():N}_{Path.GetFileName(item.NormalizedPath)}"); if (item.WasDirectory) Directory.Move(item.NormalizedPath, target); else File.Move(item.NormalizedPath, target); var entries = (await GetQuarantineAsync(cancellationToken)).ToList(); var entry = new QuarantineEntry(Guid.NewGuid(), item.NormalizedPath, target, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(Math.Clamp(days, 7, 30)), item.ExpectedBytes); entries.Add(entry); await SaveQuarantineAsync(entries, cancellationToken); return entry;
    }
    private async Task SaveQuarantineAsync(IReadOnlyList<QuarantineEntry> entries, CancellationToken cancellationToken) { Directory.CreateDirectory(Path.GetDirectoryName(_quarantineIndex)!); var temp = _quarantineIndex + ".tmp"; await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: cancellationToken); File.Move(temp, _quarantineIndex, true); }
    private static bool IsOnSystemDrive(string path) => (Path.GetPathRoot(path) ?? string.Empty).Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
}

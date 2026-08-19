using System.Net.Http.Headers;
using System.Text.Json;
using CodexCleaner.Core;

namespace CodexCleaner.Services;

public sealed class GitHubUpdateService(HttpClient? client = null) : IUpdateService
{
    private const string LatestRelease = "https://api.github.com/repos/LightyearXizIl/CodexCleaner/releases/latest";
    private readonly HttpClient _client = client ?? CreateClient();

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            using var response = await _client.GetAsync(LatestRelease, cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, false, null, $"暂时无法检查更新（GitHub 返回 {(int)response.StatusCode}）。", checkedAt);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() || root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return new(true, false, null, "当前没有可用的稳定更新。", checkedAt);
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!TryParseVersion(tag, out var remote)) return new(false, false, null, "最新 Release 的版本号格式无效。", checkedAt);
            var assets = root.TryGetProperty("assets", out var assetList) ? assetList.EnumerateArray().ToList() : [];
            var installer = assets.FirstOrDefault(x => (x.GetProperty("name").GetString() ?? string.Empty).EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase));
            var installerUrl = installer.ValueKind == JsonValueKind.Undefined ? null : installer.GetProperty("browser_download_url").GetString();
            var release = new UpdateInfo(remote, tag, root.GetProperty("name").GetString() ?? tag, root.GetProperty("body").GetString() ?? string.Empty, root.GetProperty("published_at").GetDateTimeOffset(), new Uri(root.GetProperty("html_url").GetString()!), installerUrl is null ? null : new Uri(installerUrl));
            return new(true, remote > currentVersion, release, remote > currentVersion ? $"发现新版本 {remote}" : "当前已是最新版本。", checkedAt);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(false, false, null, "暂时无法检查更新。", checkedAt); }
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        var value = tag.Trim().TrimStart('v', 'V');
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts.Any(x => !int.TryParse(x, out var n) || n < 0 || n > 9)) { version = new Version(0, 0, 0); return false; }
        if (Version.TryParse(value, out var parsed)) { version = parsed; return true; }
        version = new Version(0, 0, 0);
        return false;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CodexCleaner", "0.0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

public sealed class MigrationService(DirectorySizer sizer, IExternalCommandRunner commands) : IMigrationService
{
    private readonly string _recordsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner", "migration-records.json");

    public async Task<IReadOnlyList<MigrationItem>> DiscoverAsync(string targetRoot, IEnumerable<StorageItem> items, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetRoot) || !Path.IsPathFullyQualified(targetRoot) || (Directory.Exists(targetRoot) && DirectorySizer.IsReparsePoint(targetRoot))) return [];
        var root = Path.GetFullPath(targetRoot);
        var result = new List<MigrationItem>();
        foreach (var item in items.Where(x => x.Source == "开发环境" && x.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = GetKind(item);
            if (kind is null || DirectorySizer.IsReparsePoint(item.Path)) continue;
            var target = Path.Combine(root, "Caches", kind.Value.ToString(), Path.GetFileName(item.Path));
            if (RiskRules.IsSameOrChild(item.Path, root) || Directory.Exists(target) || File.Exists(target)) continue;
            var original = kind switch
            {
                MigrationKind.PipCache => Environment.GetEnvironmentVariable("PIP_CACHE_DIR", EnvironmentVariableTarget.User),
                MigrationKind.UvCache => Environment.GetEnvironmentVariable("UV_CACHE_DIR", EnvironmentVariableTarget.User),
                MigrationKind.PlaywrightBrowsers => Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", EnvironmentVariableTarget.User),
                _ => null
            };
            if (kind == MigrationKind.NpmCache)
            {
                var current = await commands.RunAsync("npm", ["config", "get", "cache"], TimeSpan.FromSeconds(8), cancellationToken);
                if (!current.Found || current.ExitCode != 0) continue;
                original = current.StandardOutput.Trim();
            }
            result.Add(new MigrationItem(kind.Value, DisplayName(kind.Value), item.Path, target, item.SizeBytes, item.FileCount, item.LastWriteTime, ConfigKey(kind.Value), original));
        }
        return result;
    }

    public async Task<MigrationResult> ExecuteAsync(MigrationPlan plan, CancellationToken cancellationToken)
    {
        var records = new List<MigrationRecord>();
        var configured = new List<MigrationItem>();
        var copiedTargets = new List<string>();
        try
        {
            ValidatePlan(plan);
            foreach (var item in plan.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CopyDirectory(item.SourcePath, item.TargetPath, cancellationToken);
                copiedTargets.Add(item.TargetPath);
                var (bytes, files) = await sizer.GetSizeAsync(item.TargetPath, cancellationToken);
                if (bytes != item.ExpectedBytes || files != item.ExpectedFiles) throw new IOException($"{item.DisplayName} 复制校验失败，已保留源目录。");
                await ApplyConfigurationAsync(item, cancellationToken);
                configured.Add(item);
                await VerifyConfigurationAsync(item, cancellationToken);
                records.Add(new MigrationRecord(plan.Id, item.Kind, item.SourcePath, item.TargetPath, item.ExpectedBytes, MigrationState.AwaitingSourceCleanup, "已复制并验证；C 盘源缓存仍保留，需在安全清理页二次确认。", DateTimeOffset.UtcNow));
            }
            await AppendRecordsAsync(records, cancellationToken);
            return new(plan.Id, MigrationState.AwaitingSourceCleanup, records);
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(configured);
            RemoveIncompleteTargets(copiedTargets, plan.TargetRoot);
            await AppendRecordsAsync(records, CancellationToken.None); return new(plan.Id, MigrationState.Cancelled, records, "迁移已取消；原缓存未删除。");
        }
        catch (Exception ex)
        {
            await RollbackAsync(configured);
            RemoveIncompleteTargets(copiedTargets, plan.TargetRoot);
            await AppendRecordsAsync(records, CancellationToken.None); return new(plan.Id, MigrationState.Failed, records, ex.Message);
        }
    }

    public async Task<IReadOnlyList<MigrationRecord>> GetRecordsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_recordsPath)) return [];
        try { await using var stream = File.OpenRead(_recordsPath); return await JsonSerializer.DeserializeAsync<List<MigrationRecord>>(stream, cancellationToken: cancellationToken) ?? []; }
        catch (IOException) { return []; } catch (JsonException) { return []; }
    }

    private static MigrationKind? GetKind(StorageItem item)
    {
        var text = $"{item.Path} {item.Detail}";
        if (text.Contains("npm 缓存", StringComparison.OrdinalIgnoreCase)) return MigrationKind.NpmCache;
        if (text.Contains("pip 缓存", StringComparison.OrdinalIgnoreCase)) return MigrationKind.PipCache;
        if (text.Contains("uv 缓存", StringComparison.OrdinalIgnoreCase)) return MigrationKind.UvCache;
        if (text.Contains("Playwright", StringComparison.OrdinalIgnoreCase)) return MigrationKind.PlaywrightBrowsers;
        return null;
    }
    private static string DisplayName(MigrationKind kind) => kind switch { MigrationKind.NpmCache => "npm 缓存", MigrationKind.PipCache => "pip 缓存", MigrationKind.UvCache => "uv 缓存", _ => "Playwright 浏览器" };
    private static string ConfigKey(MigrationKind kind) => kind switch { MigrationKind.NpmCache => "npm config cache", MigrationKind.PipCache => "PIP_CACHE_DIR", MigrationKind.UvCache => "UV_CACHE_DIR", _ => "PLAYWRIGHT_BROWSERS_PATH" };
    private static void ValidatePlan(MigrationPlan plan)
    {
        if (plan.Items.Count == 0 || !Path.IsPathFullyQualified(plan.TargetRoot) || (Directory.Exists(plan.TargetRoot) && DirectorySizer.IsReparsePoint(plan.TargetRoot))) throw new InvalidOperationException("迁移计划或目标目录无效。");
        var targetDrive = new DriveInfo(Path.GetPathRoot(plan.TargetRoot)!); if (!targetDrive.IsReady || targetDrive.AvailableFreeSpace <= plan.Items.Sum(x => x.ExpectedBytes)) throw new IOException("目标盘剩余空间不足。");
        foreach (var item in plan.Items)
        {
            if (!Directory.Exists(item.SourcePath) || DirectorySizer.IsReparsePoint(item.SourcePath) || Directory.Exists(item.TargetPath) || File.Exists(item.TargetPath)) throw new InvalidOperationException("源路径、目标路径或重解析点校验失败。");
            if (Math.Abs((Directory.GetLastWriteTimeUtc(item.SourcePath) - item.ExpectedLastWriteTime.UtcDateTime).TotalSeconds) > 2) throw new InvalidOperationException("源缓存已变化，请重新生成迁移计划。");
        }
    }
    private static void CopyDirectory(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })) { ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); }
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })) { ct.ThrowIfCancellationRequested(); var output = Path.Combine(target, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(file, output, false); }
    }
    private async Task ApplyConfigurationAsync(MigrationItem item, CancellationToken ct)
    {
        if (item.Kind == MigrationKind.NpmCache)
        {
            var result = await commands.RunAsync("npm", ["config", "set", "cache", item.TargetPath], TimeSpan.FromSeconds(10), ct);
            if (!result.Found || result.ExitCode != 0) throw new InvalidOperationException("npm 缓存配置写入失败。");
            return;
        }
        var variable = item.Kind switch { MigrationKind.PipCache => "PIP_CACHE_DIR", MigrationKind.UvCache => "UV_CACHE_DIR", MigrationKind.PlaywrightBrowsers => "PLAYWRIGHT_BROWSERS_PATH", _ => throw new InvalidOperationException() };
        Environment.SetEnvironmentVariable(variable, item.TargetPath, EnvironmentVariableTarget.User);
    }
    private async Task VerifyConfigurationAsync(MigrationItem item, CancellationToken ct)
    {
        if (item.Kind == MigrationKind.NpmCache)
        {
            var result = await commands.RunAsync("npm", ["config", "get", "cache"], TimeSpan.FromSeconds(8), ct);
            if (!result.Found || result.ExitCode != 0 || !Path.TrimEndingDirectorySeparator(result.StandardOutput.Trim()).Equals(Path.TrimEndingDirectorySeparator(item.TargetPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("npm 未切换到新缓存路径。");
            return;
        }
        var variable = item.Kind switch { MigrationKind.PipCache => "PIP_CACHE_DIR", MigrationKind.UvCache => "UV_CACHE_DIR", _ => "PLAYWRIGHT_BROWSERS_PATH" };
        if (!string.Equals(Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User), item.TargetPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("缓存环境变量验证失败。");
    }
    private async Task RollbackAsync(IEnumerable<MigrationItem> items)
    {
        foreach (var item in items.Reverse())
        {
            try
            {
                if (item.Kind == MigrationKind.NpmCache && !string.IsNullOrWhiteSpace(item.OriginalValue))
                    await commands.RunAsync("npm", ["config", "set", "cache", item.OriginalValue], TimeSpan.FromSeconds(8), CancellationToken.None);
                else if (item.Kind != MigrationKind.NpmCache)
                {
                    var variable = item.Kind switch { MigrationKind.PipCache => "PIP_CACHE_DIR", MigrationKind.UvCache => "UV_CACHE_DIR", _ => "PLAYWRIGHT_BROWSERS_PATH" };
                    Environment.SetEnvironmentVariable(variable, item.OriginalValue, EnvironmentVariableTarget.User);
                }
            }
            catch { /* Original cache stays intact; rollback failure is recorded by the caller. */ }
        }
    }
    private static void RemoveIncompleteTargets(IEnumerable<string> targets, string targetRoot)
    {
        foreach (var target in targets.OrderByDescending(x => x.Length))
        {
            try
            {
                if (RiskRules.IsSameOrChild(target, targetRoot) && Directory.Exists(target) && !DirectorySizer.IsReparsePoint(target)) Directory.Delete(target, true);
            }
            catch { /* Retain incomplete copy when it cannot be safely removed. */ }
        }
    }
    private async Task AppendRecordsAsync(IEnumerable<MigrationRecord> records, CancellationToken ct)
    {
        var all = (await GetRecordsAsync(ct)).ToList(); all.AddRange(records); Directory.CreateDirectory(Path.GetDirectoryName(_recordsPath)!); var temp = _recordsPath + ".tmp"; await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, all, cancellationToken: ct); File.Move(temp, _recordsPath, true);
    }
}

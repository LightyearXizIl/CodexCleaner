using System.Runtime.CompilerServices;
using System.Text.Json;
using CodexCleaner.Core;
using Microsoft.Win32;

namespace CodexCleaner.Services;

public sealed class DirectorySizer
{
    public async Task<(long Bytes, int Files)> GetSizeAsync(string root, CancellationToken cancellationToken) => await Task.Run(() => GetSize(root, cancellationToken), cancellationToken);
    public (long Bytes, int Files) GetSize(string root, CancellationToken cancellationToken)
    {
        long bytes = 0; var files = 0;
        try
        {
            if (File.Exists(root)) return (new FileInfo(root).Length, 1);
            if (!Directory.Exists(root) || IsReparsePoint(root)) return (0, 0);
            foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { bytes = checked(bytes + new FileInfo(file).Length); files++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        return (bytes, files);
    }
    public static bool IsReparsePoint(string path) { try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch (IOException) { return true; } catch (UnauthorizedAccessException) { return true; } }
}

public sealed class RiskService : IRiskService
{
    public (ItemCategory Category, RiskLevel Risk, string Detail) Classify(string path) => RiskRules.Classify(path);
    public bool IsProtected(string path, IEnumerable<string>? protectedRoots = null) => RiskRules.IsProtectedPath(path, protectedRoots);
}

public sealed class AppSettingsService : ISettingsService
{
    private readonly string _path;
    public AppSettingsService(string? path = null) { _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner", "settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(_path)!); }
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return AppSettings.Default;
        try { await using var stream = File.OpenRead(_path); return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken) ?? AppSettings.Default; } catch (JsonException) { return AppSettings.Default; } catch (IOException) { return AppSettings.Default; }
    }
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var temp = _path + ".tmp"; await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken); File.Move(temp, _path, true);
    }
}

public sealed class CodexScanner(DirectorySizer sizer, ISettingsService settings) : ICodexScanner
{
    public async Task<(string Home, IReadOnlyList<StorageItem> Items, IReadOnlyList<string> Warnings)> ScanAsync(CancellationToken cancellationToken)
    {
        var configured = await settings.LoadAsync(cancellationToken); var home = Environment.GetEnvironmentVariable("CODEX_HOME"); if (string.IsNullOrWhiteSpace(home)) home = configured.CodexHome; if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var items = new List<StorageItem>(); var warnings = new List<string>(); if (!Directory.Exists(home)) { warnings.Add("未找到 Codex Home，可在设置中指定路径。"); return (home, items, warnings); }
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(home).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested(); if (DirectorySizer.IsReparsePoint(path)) { warnings.Add($"已跳过重解析点：{path}"); continue; }
                var (bytes, files) = await sizer.GetSizeAsync(path, cancellationToken); var name = Path.GetFileName(path); var category = name is "cache" or "tmp" ? ItemCategory.Cache : ItemCategory.CodexData; var risk = category == ItemCategory.Cache ? RiskLevel.Safe : RiskLevel.Review;
                items.Add(new StorageItem(path, name, bytes, category, risk, File.GetLastWriteTimeUtc(path), files, category == ItemCategory.Cache ? "Codex 缓存" : $"Codex {name}", Directory.Exists(path), "Codex Home"));
            }
        }
        catch (UnauthorizedAccessException ex) { warnings.Add($"Codex Home 部分目录无权限：{ex.Message}"); } catch (IOException ex) { warnings.Add($"Codex Home 部分目录无法读取：{ex.Message}"); }
        return (home, items, warnings);
    }
}

public sealed class GitStatusService(IExternalCommandRunner commands) : IGitStatusService
{
    public async Task<GitStatus> GetStatusAsync(string path, CancellationToken cancellationToken)
    {
        var status = await commands.RunAsync("git", ["-C", path, "status", "--porcelain", "--branch"], TimeSpan.FromSeconds(8), cancellationToken); if (!status.Found) return new GitStatus(false, null, false, false, "未检测到 Git"); if (status.ExitCode != 0) return GitStatus.NotRepository;
        var lines = status.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries); var branch = lines.FirstOrDefault(x => x.StartsWith("## ", StringComparison.Ordinal))?.Replace("## ", string.Empty).Split("...")[0];
        return new GitStatus(true, branch, lines.Any(x => !x.StartsWith("## ", StringComparison.Ordinal) && !x.StartsWith("??", StringComparison.Ordinal)), lines.Any(x => x.StartsWith("??", StringComparison.Ordinal)));
    }
    public async Task<IReadOnlyList<string>> GetWorktreesAsync(string path, CancellationToken cancellationToken)
    {
        var result = await commands.RunAsync("git", ["-C", path, "worktree", "list", "--porcelain"], TimeSpan.FromSeconds(8), cancellationToken); if (!result.Found || result.ExitCode != 0) return [];
        return result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Where(x => x.StartsWith("worktree ", StringComparison.Ordinal)).Select(x => x[9..]).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class ProjectScanner(DirectorySizer sizer, ISettingsService settings, IGitStatusService git) : IProjectScanner
{
    public async Task<IReadOnlyList<CodexTask>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var config = await settings.LoadAsync(cancellationToken); var home = Environment.GetEnvironmentVariable("CODEX_HOME"); if (string.IsNullOrWhiteSpace(home)) home = config.CodexHome; if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var indexedNames = ReadIndex(Path.Combine(home, "session_index.jsonl")); var sessions = await ReadSessionRootsAsync(Path.Combine(home, "sessions"), cancellationToken); var roots = new Dictionary<string, List<SessionMeta>>(StringComparer.OrdinalIgnoreCase);
        // Every input source can spell the same root differently (trailing slash,
        // worktree output, junction-normalised session cwd). Use one canonical key
        // before grouping so a project is never billed once per Codex session.
        foreach (var session in sessions) { var root = ResolveProjectRoot(session.Cwd); if (root is null) continue; root = NormalizeRoot(root); if (IsIgnored(root, config.IgnoredPaths)) continue; if (!roots.TryGetValue(root, out var entries)) roots[root] = entries = []; entries.Add(session); }
        foreach (var root in config.AdditionalRoots.Where(Directory.Exists).Select(NormalizeRoot).Where(root => !IsIgnored(root, config.IgnoredPaths))) if (!roots.ContainsKey(root)) roots[root] = [];
        foreach (var root in roots.Keys.ToArray()) foreach (var worktree in await git.GetWorktreesAsync(root, cancellationToken)) { var normalized = NormalizeRoot(worktree); if (!roots.ContainsKey(normalized)) roots[normalized] = []; }
        var tasks = new List<CodexTask>();
        foreach (var pair in roots)
        {
            cancellationToken.ThrowIfCancellationRequested(); var root = pair.Key; var entries = pair.Value; var artifacts = await ScanArtifactsAsync(root, cancellationToken); var status = await git.GetStatusAsync(root, cancellationToken); var activity = entries.Count == 0 ? Directory.GetLastWriteTimeUtc(root) : entries.Max(x => x.Activity); var latest = entries.OrderByDescending(x => x.Activity).FirstOrDefault(); var rawTitle = latest is not null && indexedNames.TryGetValue(latest.Id, out var indexed) ? indexed : null;
            tasks.Add(new CodexTask(latest?.Id ?? CreateStableId(root), NormalizeTitle(rawTitle, root), root, artifacts.Sum(x => x.SizeBytes), activity, GetActivity(activity), status.IsRepository, status.HasChanges, status.HasUntracked, artifacts, entries.Count, status.Branch, artifacts.Sum(x => x.FileCount)));
        }
        return tasks.OrderByDescending(x => x.LastActivity).ToList();
    }
    private async Task<IReadOnlyList<StorageItem>> ScanArtifactsAsync(string root, CancellationToken cancellationToken)
    {
        var result = new List<StorageItem>();
        try { foreach (var path in Directory.EnumerateFileSystemEntries(root)) { cancellationToken.ThrowIfCancellationRequested(); if (DirectorySizer.IsReparsePoint(path)) continue; var (category, risk, detail) = RiskRules.Classify(path); var (bytes, files) = await sizer.GetSizeAsync(path, cancellationToken); result.Add(new StorageItem(path, Path.GetFileName(path), bytes, category, risk, File.GetLastWriteTimeUtc(path), files, detail, Directory.Exists(path), "项目目录")); } } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return result;
    }
    private static string? ResolveProjectRoot(string cwd)
    {
        if (!Directory.Exists(cwd)) return null; var current = new DirectoryInfo(cwd); var markers = new[] { "package.json", "pyproject.toml", "requirements.txt", "Cargo.toml", "pom.xml", "build.gradle", "build.gradle.kts" };
        for (var level = 0; current is not null && level < 12; level++, current = current.Parent) if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")) || markers.Any(file => File.Exists(Path.Combine(current.FullName, file))) || Directory.EnumerateFiles(current.FullName, "*.sln", SearchOption.TopDirectoryOnly).Any()) return current.FullName;
        return null;
    }
    private static string NormalizeRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool IsIgnored(string path, IEnumerable<string> ignoredPaths) => ignoredPaths.Any(root => !string.IsNullOrWhiteSpace(root) && RiskRules.IsSameOrChild(path, root));
    private static string NormalizeTitle(string? value, string root) { var title = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim(); return string.IsNullOrWhiteSpace(title) || title.Length > 80 ? Path.GetFileName(root) : title; }
    private static string CreateStableId(string root) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..24];
    private static TaskActivity GetActivity(DateTimeOffset when) { var days = (DateTimeOffset.UtcNow - when).TotalDays; return days switch { < 1 => TaskActivity.Active, <= 7 => TaskActivity.Recent, < 30 => TaskActivity.Normal, < 90 => TaskActivity.Review, _ => TaskActivity.Stale }; }
    private sealed record SessionMeta(string Id, string Cwd, DateTimeOffset Activity);
    private static async Task<List<SessionMeta>> ReadSessionRootsAsync(string sessionsRoot, CancellationToken cancellationToken)
    {
        var result = new List<SessionMeta>(); if (!Directory.Exists(sessionsRoot)) return result;
        try { foreach (var file in Directory.EnumerateFiles(sessionsRoot, "*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(5000)) { cancellationToken.ThrowIfCancellationRequested(); try { var first = File.ReadLines(file).FirstOrDefault() ?? string.Empty; using var doc = JsonDocument.Parse(first); var payload = doc.RootElement.TryGetProperty("payload", out var p) ? p : doc.RootElement; if (!payload.TryGetProperty("cwd", out var cwd) || cwd.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cwd.GetString()) || !Path.IsPathFullyQualified(cwd.GetString()!)) continue; var id = payload.TryGetProperty("session_id", out var session) ? session.GetString() ?? file : file; result.Add(new SessionMeta(id, Path.GetFullPath(cwd.GetString()!), File.GetLastWriteTimeUtc(file))); } catch (JsonException) { } catch (IOException) { } catch (UnauthorizedAccessException) { } } } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return result;
    }
    private static Dictionary<string, string> ReadIndex(string path) { var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); if (!File.Exists(path)) return result; try { foreach (var line in File.ReadLines(path)) try { using var doc = JsonDocument.Parse(line); if (doc.RootElement.TryGetProperty("id", out var id) && doc.RootElement.TryGetProperty("thread_name", out var name)) result[id.GetString() ?? string.Empty] = name.GetString() ?? string.Empty; } catch (JsonException) { } } catch (IOException) { } catch (UnauthorizedAccessException) { } return result; }
}

public sealed class StorageScanner(DirectorySizer sizer) : IStorageScanner
{
    public async Task<IReadOnlyList<StorageItem>> ScanAsync(IEnumerable<string> roots, ScanMode mode, IProgress<ScanIssue>? issues, CancellationToken cancellationToken)
    {
        var items = new List<StorageItem>();
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested(); if (DirectorySizer.IsReparsePoint(root)) { issues?.Report(new ScanIssue(root, "文件系统", "已跳过重解析点")); continue; }
            if (mode == ScanMode.Quick) { foreach (var child in SafeEntries(root, issues)) { cancellationToken.ThrowIfCancellationRequested(); var (category, risk, detail) = RiskRules.Classify(child); if (category is ItemCategory.Other or ItemCategory.Configuration) continue; var (size, count) = await sizer.GetSizeAsync(child, cancellationToken); items.Add(new StorageItem(child, Path.GetFileName(child), size, category, risk, File.GetLastWriteTimeUtc(child), count, detail, Directory.Exists(child), root)); } continue; }
            foreach (var file in EnumerateFiles(root, issues, cancellationToken)) { var info = new FileInfo(file); if (info.Length < 100L * 1024 * 1024) continue; var (category, risk, detail) = RiskRules.Classify(file); items.Add(new StorageItem(file, info.Name, info.Length, category == ItemCategory.Other ? ItemCategory.LargeFile : category, risk, info.LastWriteTimeUtc, 1, detail, false, root)); }
        }
        return items;
    }
    private static IEnumerable<string> SafeEntries(string root, IProgress<ScanIssue>? issues) { try { return Directory.EnumerateFileSystemEntries(root).ToList(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues?.Report(new ScanIssue(root, "文件系统", ex.Message, ex is UnauthorizedAccessException)); return []; } }
    private static IEnumerable<string> EnumerateFiles(string root, IProgress<ScanIssue>? issues, CancellationToken ct)
    {
        // Do not materialise an entire drive before producing the first result.
        // A stack-based walk is cancellable between directories, reports paths
        // that could not be read, and never follows junctions or symbolic links.
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (DirectorySizer.IsReparsePoint(directory))
            {
                issues?.Report(new ScanIssue(directory, "文件系统", "已跳过重解析点"));
                continue;
            }

            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues?.Report(new ScanIssue(directory, "文件系统", ex.Message, ex is UnauthorizedAccessException));
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!TryGetEntryKind(entry, out var isDirectory, out var error))
                {
                    issues?.Report(new ScanIssue(entry, "文件系统", error?.Message ?? "无法读取路径", error is UnauthorizedAccessException));
                    continue;
                }
                if (DirectorySizer.IsReparsePoint(entry))
                {
                    issues?.Report(new ScanIssue(entry, "文件系统", "已跳过重解析点"));
                    continue;
                }
                if (isDirectory) pending.Push(entry);
                else yield return entry;
            }
        }
    }
    private static bool TryGetEntryKind(string path, out bool isDirectory, out Exception? error)
    {
        try
        {
            isDirectory = Directory.Exists(path); error = null;
            return isDirectory || File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            isDirectory = false; error = ex; return false;
        }
    }
}

public sealed class DeveloperEnvironmentScanner(DirectorySizer sizer, IExternalCommandRunner commands) : IDeveloperEnvironmentScanner
{
    public async Task<IReadOnlyList<StorageItem>> ScanAsync(CancellationToken cancellationToken)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var paths = new List<(string Path, string Detail)> {
            (Path.Combine(local, "npm-cache"), "npm 缓存"), (Path.Combine(local, "pnpm", "store"), "pnpm Store（多个项目共享）"), (Path.Combine(local, "Yarn", "Cache"), "Yarn 缓存"), (Path.Combine(local, "Yarn", "Berry", "cache"), "Yarn Berry 缓存"), (Path.Combine(local, "ms-playwright"), "Playwright 浏览器"), (Path.Combine(local, "pip", "Cache"), "pip 缓存"), (Path.Combine(home, ".cache", "uv"), "uv 缓存"), (Path.Combine(home, ".cache", "torch"), "Torch 缓存"), (Path.Combine(home, ".cache", "modelscope"), "ModelScope 缓存"),
            (Path.Combine(home, ".nuget", "packages"), "NuGet 全局包（建议确认）"), (Path.Combine(home, ".cargo", "registry"), "Cargo registry"), (Path.Combine(home, ".cargo", "git"), "Cargo git cache"), (Path.Combine(home, ".rustup", "toolchains"), "Rustup 工具链（仅分析）"), (Path.Combine(home, ".gradle", "caches"), "Gradle 缓存（建议确认）"), (Path.Combine(home, ".m2", "repository"), "Maven 本地仓库（建议确认）"),
            (Path.Combine(home, ".cache", "huggingface", "hub"), "Hugging Face 缓存"), (Path.Combine(local, "huggingface", "hub"), "Hugging Face 缓存"), (Path.Combine(local, "ollama", "models"), "Ollama 模型（仅分析）"), (Path.Combine(local, "Programs", "Ollama", "models"), "Ollama 模型（仅分析）"), (Path.Combine(local, "LM Studio", "models"), "LM Studio 模型（仅分析）"), (Path.Combine(home, ".lmstudio", "models"), "LM Studio 模型（仅分析）"),
            (Path.Combine(local, "Docker", "wsl", "data"), "Docker WSL 数据（仅分析）"), (Path.Combine(local, "Docker"), "Docker 本地数据（仅分析）"),
            (Path.Combine(programFiles, "Java"), "Java JDK（仅分析）"), (Path.Combine(programFiles, "Eclipse Adoptium"), "Java JDK（仅分析）"), (Path.Combine(programFilesX86, "Microsoft Visual Studio"), "Visual Studio / Build Tools（仅分析）"), (Path.Combine(programFiles, "Microsoft Visual Studio"), "Visual Studio / Build Tools（仅分析）"), (Path.Combine(programFilesX86, "Windows Kits"), "Windows SDK（仅分析）") };
        var androidRoot = Path.Combine(local, "Android", "Sdk");
        foreach (var name in new[] { "platforms", "build-tools", "system-images", "emulator", "cmdline-tools" }) paths.Add((Path.Combine(androidRoot, name), $"Android SDK {name}（仅分析）"));
        var electronCache = Path.Combine(local, "electron", "Cache"); paths.Add((electronCache, "Electron 下载缓存"));
        var puppeteerCache = Path.Combine(local, "puppeteer", "Cache"); paths.Add((puppeteerCache, "Puppeteer 下载缓存"));
        // WSL distributions are virtual disks. List them individually and keep
        // them protected; no cleanup path is ever generated for a VHDX.
        var packageRoot = Path.Combine(local, "Packages");
        if (Directory.Exists(packageRoot)) try { foreach (var vhdx in Directory.EnumerateFiles(packageRoot, "ext4.vhdx", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })) paths.Add((vhdx, "WSL Linux 虚拟磁盘（仅分析）")); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var npm = await commands.RunAsync("npm", ["config", "get", "cache"], TimeSpan.FromSeconds(5), cancellationToken); if (npm.Found && npm.ExitCode == 0 && Directory.Exists(npm.StandardOutput.Trim())) paths.Add((npm.StandardOutput.Trim(), "npm 缓存（工具报告路径）"));
        var pnpm = await commands.RunAsync("pnpm", ["store", "path"], TimeSpan.FromSeconds(5), cancellationToken); if (pnpm.Found && pnpm.ExitCode == 0 && Directory.Exists(pnpm.StandardOutput.Trim())) paths.Add((pnpm.StandardOutput.Trim(), "pnpm Store（工具报告路径）"));
        var list = new List<StorageItem>(); foreach (var (path, detail) in paths.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Where(x => Directory.Exists(x.Path) || File.Exists(x.Path))) { cancellationToken.ThrowIfCancellationRequested(); var (size, files) = await sizer.GetSizeAsync(path, cancellationToken); var (category, risk, _) = RiskRules.Classify(path); if (detail.Contains("NuGet") || detail.Contains("Gradle") || detail.Contains("Maven") || detail.Contains("Store") || detail.Contains("Hugging", StringComparison.OrdinalIgnoreCase)) risk = RiskLevel.Review; if (detail.Contains("模型") || detail.Contains("Model", StringComparison.OrdinalIgnoreCase)) { category = ItemCategory.AiModel; risk = RiskLevel.Protected; } if (detail.Contains("仅分析") || detail.Contains("WSL")) risk = RiskLevel.Protected; list.Add(new StorageItem(path, Path.GetFileName(path), size, category == ItemCategory.Other ? ItemCategory.Cache : category, risk, File.GetLastWriteTimeUtc(path), files, detail, Directory.Exists(path), "开发环境")); }
        return list;
    }
}

public sealed class InstalledToolScanner(DirectorySizer sizer) : IInstalledToolScanner
{
    public async Task<IReadOnlyList<InstalledTool>> ScanAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, InstalledTool>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows()) foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine }) foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" }) using (var key = hive.OpenSubKey(path))
        {
            if (key is null) continue;
            foreach (var childName in key.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested(); using var child = key.OpenSubKey(childName);
                if (child?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                var install = child.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install)) install = null;
                var size = 0L; if (install is not null) (size, _) = await sizer.GetSizeAsync(install, cancellationToken);
                DateTimeOffset? installDate = DateTime.TryParseExact(child.GetValue("InstallDate") as string, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) ? new DateTimeOffset(date) : null;
                var id = $"{hive.Name}:{path}:{childName}";
                result[id] = new InstalledTool(id, name, child.GetValue("DisplayVersion") as string, install, size, installDate, AttributionEngine.Score([]), true, "Windows 已安装应用", child.GetValue("UninstallString") as string);
            }
        }

        // Some user-scoped tools (for example Python, Node and Ollama) do not
        // register an uninstall key. Discover immediate Local Programs folders
        // as a clearly labelled fallback without treating a plain folder as proof
        // of an install source.
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(root).ToArray(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DirectorySizer.IsReparsePoint(directory) || result.Values.Any(x => x.InstallPath is not null && RiskRules.IsSameOrChild(directory, x.InstallPath))) continue;
                var hasExecutable = false;
                try { hasExecutable = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Any(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (!hasExecutable) continue;
                var (size, _) = await sizer.GetSizeAsync(directory, cancellationToken); var name = Path.GetFileName(directory); var id = $"folder:{directory}";
                result[id] = new InstalledTool(id, name, null, directory, size, Directory.GetCreationTimeUtc(directory), AttributionEngine.Score([]), true, "程序目录发现（无卸载注册信息）");
            }
        }
        return result.Values.OrderByDescending(x => x.SizeBytes).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class AttributionService : IAttributionService
{
    public AttributionResult Attribute(InstalledTool tool, IEnumerable<CodexTask> tasks)
    {
        var evidence = new List<AttributionEvidence>(); if (tool.InstalledAt is { } installed && tasks.Any(x => Math.Abs((x.LastActivity - installed).TotalDays) <= 2)) evidence.Add(new AttributionEvidence("time", "安装时间接近 Codex 任务活动", 25, installed)); if (tool.Name.Contains("Node", StringComparison.OrdinalIgnoreCase) && tasks.Any(x => x.Artifacts.Any(a => a.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)))) evidence.Add(new AttributionEvidence("project-type", "发现 Node 项目依赖", 35)); return AttributionEngine.Score(evidence);
    }
}

public sealed class ScanCoordinator(ICodexScanner codex, IProjectScanner projects, IDeveloperEnvironmentScanner developer, IInstalledToolScanner tools, IStorageScanner storage, IHistoryService history, ISettingsService settings) : IScanCoordinator
{
    public ScanResult? LastResult { get; private set; }
    public async IAsyncEnumerable<ScanProgress> ScanAsync(ScanMode mode, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow; var warnings = new List<string>(); var issues = new List<ScanIssue>(); var roots = new List<string>(); var appSettings = await settings.LoadAsync(cancellationToken); yield return new ScanProgress("正在读取 C 盘容量", 0, 6); var drive = new DriveInfo("C"); var driveSummary = new DriveSummary("C: 系统盘", drive.TotalSize, drive.AvailableFreeSpace, drive.DriveFormat);
        yield return new ScanProgress("正在扫描 Codex 数据", 1, 6); var (home, codexItems, codexWarnings) = await codex.ScanAsync(cancellationToken); warnings.AddRange(codexWarnings); roots.Add(home);
        yield return new ScanProgress("正在追踪 Codex 任务", 2, 6); var tasks = await projects.DiscoverAsync(cancellationToken); roots.AddRange(tasks.Select(x => x.RootPath));
        yield return new ScanProgress("正在扫描开发环境", 3, 6); var devItems = await developer.ScanAsync(cancellationToken);
        yield return new ScanProgress("正在分析已安装工具", 4, 6); var installed = await tools.ScanAsync(cancellationToken);
        var candidates = codexItems.Concat(devItems).Concat(tasks.SelectMany(x => x.Artifacts)).Where(item => !appSettings.IgnoredPaths.Any(root => !string.IsNullOrWhiteSpace(root) && RiskRules.IsSameOrChild(item.Path, root))).GroupBy(x => Path.GetFullPath(x.Path), StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.SizeBytes).First()).Select(item => appSettings.ProtectedPaths.Any(root => !string.IsNullOrWhiteSpace(root) && RiskRules.IsSameOrChild(item.Path, root)) ? item with { Risk = RiskLevel.Protected, Detail = "用户已保护，无法清理" } : item).ToList();
        if (mode == ScanMode.Deep) { yield return new ScanProgress("正在深度分析 C 盘大文件", 5, 6); var reporter = new Progress<ScanIssue>(issues.Add); candidates.AddRange(await storage.ScanAsync([@"C:\"], ScanMode.Deep, reporter, cancellationToken)); roots.Add(@"C:\"); }
        var categories = candidates.GroupBy(x => x.Category).Select(x => new CategorySummary(x.Key, x.Sum(y => y.SizeBytes), x.Count(), x.Any(y => y.Risk == RiskLevel.Safe) ? RiskLevel.Safe : x.Max(y => y.Risk))).OrderByDescending(x => x.SizeBytes).ToList(); var snapshots = await history.GetRecentAsync(7, cancellationToken); var priorTime = snapshots.Select(x => x.CapturedAt).Where(x => x < started).DefaultIfEmpty().Max(); var current = categories.ToDictionary(x => x.Category, x => x.SizeBytes); var prior = priorTime == default ? new Dictionary<ItemCategory, long>() : snapshots.Where(x => x.CapturedAt == priorTime).GroupBy(x => x.Category).ToDictionary(x => x.Key, x => x.Sum(y => y.SizeBytes)); var changes = priorTime == default ? [] : current.Keys.Union(prior.Keys).Select(x => new CategoryDelta(x, current.GetValueOrDefault(x) - prior.GetValueOrDefault(x))).Where(x => x.DeltaBytes != 0).OrderByDescending(x => Math.Abs(x.DeltaBytes)).ToList(); var completeness = issues.Count == 0 ? ScanCompleteness.Complete : ScanCompleteness.Partial;
        LastResult = new ScanResult(Guid.NewGuid(), mode, started, DateTimeOffset.UtcNow, driveSummary, categories, tasks, candidates, warnings, completeness == ScanCompleteness.Complete, changes, new ScanCoverage(mode, roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), completeness, issues.Count, issues.Count(x => x.IsPermissionIssue)), issues, installed); if (LastResult.CanContributeToHistory) { yield return new ScanProgress("正在保存本地快照", 5, 6); await history.SaveAsync(LastResult, cancellationToken); } yield return new ScanProgress(completeness == ScanCompleteness.Complete ? "扫描完成" : "扫描部分完成", 6, 6);
    }
}

public sealed class DuplicateService : IDuplicateService
{
    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(IEnumerable<string> roots, long minimumSize, CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>(); foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)) try { files.AddRange(Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Select(x => new FileInfo(x)).Where(x => x.Length >= minimumSize)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var groups = new List<DuplicateGroup>(); foreach (var sizeGroup in files.GroupBy(x => x.Length).Where(x => x.Count() > 1)) { var distinctFiles = sizeGroup.GroupBy(file => FileIdentity.Get(file.FullName) ?? file.FullName, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList(); if (distinctFiles.Count < 2) continue; var quick = new Dictionary<string, List<FileInfo>>(); foreach (var file in distinctFiles) { cancellationToken.ThrowIfCancellationRequested(); try { var hash = await Hashing.QuickHashAsync(file.FullName, cancellationToken); (quick.TryGetValue(hash, out var list) ? list : quick[hash] = []).Add(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } } foreach (var quickGroup in quick.Values.Where(x => x.Count > 1)) { var full = new Dictionary<string, List<FileInfo>>(); foreach (var file in quickGroup) try { var hash = await Hashing.Sha256Async(file.FullName, cancellationToken); (full.TryGetValue(hash, out var list) ? list : full[hash] = []).Add(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } groups.AddRange(full.Where(x => x.Value.Count > 1).Select(x => new DuplicateGroup(x.Key, x.Value[0].Length, x.Value.Select(f => f.FullName).ToList()))); } }
        return groups;
    }
}

internal static class FileIdentity
{
    public static string? Get(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var handle = CreateFile(path, 0x80000000, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) return null;
            return $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out ByHandleFileInformation fileInformation);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public FileAttributes FileAttributes; public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime; public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime; public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber; public uint FileSizeHigh; public uint FileSizeLow; public uint NumberOfLinks; public uint FileIndexHigh; public uint FileIndexLow;
    }
}

# Codex Cleaner

> 看看 Codex 到底往你的 C 盘写了什么。

Codex Cleaner 是一个完全本地运行的 Windows 11 桌面软件，用于分析 Codex、开发任务、项目依赖、构建产物和开发缓存造成的磁盘占用，并在清理前解释来源、风险和影响。

## 主要功能

- 读取真实 C 盘容量，区分 Codex 数据、项目依赖、构建产物、缓存与其他内容。
- 自动解析 `CODEX_HOME` 或 `%USERPROFILE%\.codex`，从 Codex session 的工作目录和 Git worktree 发现任务。
- 分析项目中的 `node_modules`、`.next`、`dist`、`build`、`target`、`bin`、`obj`、Python 缓存和虚拟环境。
- 扫描 npm、pnpm、Yarn、Playwright、pip、uv、NuGet、Cargo、Gradle、Maven、Hugging Face、Ollama、LM Studio、Android SDK、Visual Studio、Windows SDK、Docker 本地数据与 WSL 虚拟磁盘等可访问位置；缺少工具只显示未检测到。
- MSIX 版保存 SQLite/WAL 扫描快照；首次扫描只保存基线，后续扫描才计算真实空间变化。Portable 版使用同样仅本地的原子历史文件，以适配无包标识运行环境。
- 按大小、快速哈希、完整 SHA-256 识别重复文件。
- 风险分级：`Safe`、`Rebuildable`、`Review`、`Protected`。
- 只有明确缓存允许默认预选；源码、`.git`、`.env`、数据库、系统目录与 AI 模型主文件默认保护。
- 将主入口收敛为「概览 / Codex 任务 / 释放空间 / 开发环境 / 空间分析」；详情与记录改为页内标签或上下文视图。
- 支持 npm、pip、uv、Playwright 的缓存迁移计划：先复制、校验、切换配置和只读验证，C 盘源缓存会保留，必须由用户随后确认才会清理。
- 设置页默认在启动时查询 GitHub 公共 Release；请求不会携带扫描路径、任务、日志或账户信息。

## 安全机制

- 所有扫描都在本机执行，软件不上传文件、路径、文件名、项目代码或账户数据。
- 任何清理都先生成计划并在执行前再次检查路径、风险和保护规则。
- 有未提交修改或未跟踪文件的 Git Worktree 不允许整体删除。
- 主程序不以管理员权限长期运行；需要权限的操作预留给独立 `CodexCleaner.ElevatedHelper`。
- 安装在 Program Files、Windows Kits、Visual Studio、WSL、Docker 或应用目录中的内容只分析，不直接删除。

## 技术栈

- C# / .NET 10
- WinUI 3 / Windows App SDK 1.7（当前本机可用的自包含运行时）
- CommunityToolkit.Mvvm
- Microsoft.Data.Sqlite
- xUnit

## 开发环境

- Windows 11 x64，最低 build 22000
- .NET SDK 10
- Windows SDK 与 MSIX 工具
- 官方 WinUI 模板：`dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`

## 构建与测试

```powershell
dotnet restore CodexCleaner.slnx
dotnet build CodexCleaner.slnx --no-restore
dotnet test CodexCleaner.slnx --no-build --no-restore
```

以开发模式启动（需要 Windows 开发者模式或测试包标识）：

```powershell
dotnet run --project src\CodexCleaner.App\CodexCleaner.App.csproj -r win-x64
```

## 打包

Portable 包：

```powershell
.\scripts\Publish-Portable.ps1
```

脚本输出 `artifacts\portable\CodexCleaner-win-x64.zip`，其中包含主程序和独立提权助手。

Inno Setup 安装器：

```powershell
winget install --id JRSoftware.InnoSetup --exact
.\scripts\Build-Installer.ps1
```

安装器输出 `artifacts\installer\CodexCleaner-0.0.1-Setup.exe`，安装到当前用户的 LocalAppData，不需要管理员权限，并提供开始菜单和卸载入口。公开 v0.0.1 不包含 MSIX：该格式需要可信的代码签名证书。

MSIX 需要受信任的代码签名证书。开发环境可使用本地测试证书；私钥不得提交到仓库。正式发布前必须替换为可信证书。

## 目录结构

```text
src/CodexCleaner.App              WinUI 3 界面、导航和 ViewModel
src/CodexCleaner.Core             模型、风险规则、哈希和接口
src/CodexCleaner.Services         扫描、Git、SQLite、清理与外部命令
src/CodexCleaner.ElevatedHelper   独立提权助手
tests/                            单元与集成测试
scripts/                          发布脚本
```

## 已知限制

- 首次扫描只建立历史基线，无法凭空给出“过去 7 天”的真实增长。
- 重复文件检查会读取候选文件并计算完整 SHA-256，不会在启动时自动运行。
- Docker、WSL、Windows SDK、Visual Studio 和系统应用默认只分析；请使用它们各自的官方管理工具清理。
- 访问受限、锁定、Reparse Point 或不存在的路径会作为警告跳过，不会导致整次扫描失败。
- Portable 版因 Windows App SDK 的无包标识限制不会打开 SQLite；MSIX 版使用 `codexcleaner.db`。两者均不上传扫描数据。
- 自动更新只提示和打开官方安装器下载；不会静默下载、安装或覆盖现有版本。

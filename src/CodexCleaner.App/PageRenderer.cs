using CodexCleaner.App.ViewModels;
using CodexCleaner.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace CodexCleaner.App;

internal static class PageRenderer
{
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static readonly Thickness PagePadding = new(28, 16, 28, 24);

    public static UIElement Create(AppPage page, MainViewModel vm, Action<AppPage> navigate) => page switch
    {
        AppPage.Dashboard => Dashboard(vm, navigate),
        AppPage.CodexUsage => CodexUsage(vm, navigate),
        AppPage.Tasks => Tasks(vm, navigate),
        AppPage.WorktreeDetails => WorktreeDetails(vm, navigate),
        AppPage.Cleanup => Cleanup(vm, navigate),
        AppPage.Migration => Migration(vm, navigate),
        AppPage.Developer => Developer(vm, navigate),
        AppPage.Tools => Tools(vm, navigate),
        AppPage.SpaceChanges => SpaceChanges(vm, navigate),
        AppPage.LargeFiles => LargeFiles(vm, navigate),
        AppPage.Duplicates => Duplicates(vm, navigate),
        AppPage.History => History(vm, navigate),
        AppPage.Settings => Settings(vm),
        _ => Dashboard(vm, navigate)
    };

    private static ScrollViewer Page(string title, string subtitle, UIElement content)
    {
        var root = new StackPanel { Spacing = 16, Padding = PagePadding };
        root.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("InkBrush") });
        root.Children.Add(new TextBlock { Text = subtitle, FontSize = 14, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, -10, 0, 4) });
        root.Children.Add(content);
        return new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    private static Border Card(UIElement child, double minHeight = 0)
        => new() { Background = Brush("SurfaceBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), MinHeight = minHeight, Child = child };

    private static TextBlock Text(string text, double size = 14, Brush? brush = null, bool strong = false)
        => new() { Text = text, FontSize = size, Foreground = brush ?? Brush("InkBrush"), FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    private static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static Button Action(string label, Action action, bool primary = false)
    {
        var lines = label.Split('\n', 2);
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(Text(lines[0], 13, primary ? new SolidColorBrush(Colors.White) : Brush("InkBrush"), true));
        if (lines.Length > 1) content.Children.Add(Text(lines[1], 11, primary ? new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)) : Brush("MutedBrush")));
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = primary ? Brush("BrandBrush") : Brush("SurfaceBrush"),
            Foreground = primary ? new SolidColorBrush(Colors.White) : Brush("InkBrush"),
            BorderBrush = primary ? Brush("BrandBrush") : Brush("LineBrush"),
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static UIElement Tabs(params (string Label, Action Navigate)[] tabs)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var tab in tabs) row.Children.Add(Action(tab.Label, tab.Navigate));
        return row;
    }

    private static Grid ThreeColumns(UIElement a, UIElement b, UIElement c)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(a); grid.Children.Add(b); grid.Children.Add(c);
        if (b is FrameworkElement bElement) Grid.SetColumn(bElement, 1);
        if (c is FrameworkElement cElement) Grid.SetColumn(cElement, 2);
        return grid;
    }

    private static UIElement Dashboard(MainViewModel vm, Action<AppPage> navigate)
    {
        var result = vm.Result;
        var drive = result?.Drive;
        var root = new StackPanel { Spacing = 16, Padding = new Thickness(28, 16, 28, 18) };
        var scanActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (vm.ScanState == ScanState.Running) scanActions.Children.Add(Action("取消扫描", vm.CancelScan));
        else { scanActions.Children.Add(Action("快速扫描", () => _ = vm.RefreshAsync(ScanMode.Quick))); scanActions.Children.Add(Action("深度扫描", () => _ = vm.RefreshAsync(ScanMode.Deep), true)); }
        root.Children.Add(scanActions);
        root.Children.Add(ThreeColumns(DriveCard(drive), UsageCard(result, navigate), ChangeCard(result, navigate)));
        root.Children.Add(ThreeColumns(SourcesCard(result, navigate), QuickActions(navigate), TasksCard(vm, result, navigate)));
        var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var privacy = Text("◈ 所有分析均在本机完成，不会上上传任何数据。", 12, Brush("MutedBrush"));
        var database = Text(vm.DatabaseLabel, 12, Brush("MutedBrush")); database.HorizontalAlignment = HorizontalAlignment.Center;
        var status = Text(vm.ScanState == ScanState.Completed ? "● 扫描完成" : vm.ScanMessage, 12, vm.ScanState == ScanState.Completed ? Brush("SuccessBrush") : Brush("MutedBrush")); status.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(privacy); footer.Children.Add(database); footer.Children.Add(status); Grid.SetColumn(database, 1); Grid.SetColumn(status, 2);
        root.Children.Add(footer);
        return new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    private static UIElement DriveCard(DriveSummary? drive)
    {
        var percent = drive?.UsedPercent ?? 0;
        var percentage = Text(drive is null ? "—" : $"{percent:0}%", 28, Brush("InkBrush"), true); percentage.HorizontalAlignment = HorizontalAlignment.Center;
        var used = Text("已使用", 13, Brush("MutedBrush")); used.HorizontalAlignment = HorizontalAlignment.Center;
        var ringContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 }; ringContent.Children.Add(percentage); ringContent.Children.Add(used);
        var ring = new Border { Width = 148, Height = 148, CornerRadius = new CornerRadius(74), BorderBrush = Brush("BrandBrush"), BorderThickness = new Thickness(12), Child = ringContent };
        var info = Stack(Text("C: 系统盘", 16, Brush("InkBrush"), true), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ring, Stack(Text(drive is null ? "等待扫描" : $"{ByteSizeFormatter.Format(drive.UsedBytes)} / {ByteSizeFormatter.Format(drive.TotalBytes)}", 20, Brush("InkBrush"), true), Text(drive is null ? "" : $"剩余 {ByteSizeFormatter.Format(drive.FreeBytes)}", 14, Brush("MutedBrush")), new ProgressBar { Value = percent, Maximum = 100, Width = 200, Height = 6, Foreground = Brush("BrandBrush") }) } }, new Border { Height = 1, Background = Brush("LineBrush"), Margin = new Thickness(0, 10, 0, 0) }, Text("上次扫描：" + (drive is null ? "未完成" : "刚刚"), 13, Brush("MutedBrush")));
        return Card(info, 310);
    }

    private static UIElement UsageCard(ScanResult? result, Action<AppPage> navigate)
    {
        var total = result?.Categories.Where(x => x.Category is not ItemCategory.System).Sum(x => x.SizeBytes) ?? 0;
        var panel = Stack(Text("Codex / 开发相关占用", 16, Brush("InkBrush"), true), Text(result is null ? "正在读取真实数据…" : ByteSizeFormatter.Format(total), 30, Brush("InkBrush"), true));
        foreach (var category in result?.Categories.Take(6) ?? [])
        {
            var row = new Grid { ColumnSpacing = 10 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.Children.Add(Text(CategoryName(category.Category), 13, Brush("MutedBrush"))); var bar = new ProgressBar { Maximum = Math.Max(1, total), Value = category.SizeBytes, Height = 6, VerticalAlignment = VerticalAlignment.Center, Foreground = CategoryBrush(category.Category) }; row.Children.Add(bar); Grid.SetColumn(bar, 1); var value = Text(ByteSizeFormatter.Format(category.SizeBytes), 13, Brush("MutedBrush")); row.Children.Add(value); Grid.SetColumn(value, 2); panel.Children.Add(row);
        }
        panel.Children.Add(Action("进入 Codex 占用页面  ›", () => navigate(AppPage.CodexUsage)));
        return Card(panel, 310);
    }

    private static UIElement ChangeCard(ScanResult? result, Action<AppPage> navigate)
    {
        var changes = result?.Changes ?? [];
        var totalChange = changes.Sum(x => x.DeltaBytes);
        var message = result is null || changes.Count == 0 ? "首次完整扫描会建立空间变化基线" : "基于上一份完整本地快照计算，不使用排版样例数据。";
        var headline = result is null || changes.Count == 0 ? "等待首次快照" : $"{(totalChange >= 0 ? "+" : "")}{ByteSizeFormatter.Format(totalChange)}";
        // A category delta is not a seven-day trend. Until enough completed
        // snapshots exist, show its real status instead of invented chart dots.
        return Card(Stack(Text("最近 7 天空间变化", 16, Brush("InkBrush"), true), Text(headline, 26, Brush("InkBrush"), true), Empty(changes.Count == 0 ? "尚无两次完整快照，暂不绘制趋势图。" : "当前显示与上一份完整快照的真实差额。"), Text(message, 13, Brush("MutedBrush")), Action("查看空间变化  ›", () => navigate(AppPage.SpaceChanges))), 310);
    }

    private static UIElement SourcesCard(ScanResult? result, Action<AppPage> navigate)
    {
        var panel = Stack(Text("主要空间来源（占用排序）", 16, Brush("InkBrush"), true));
        foreach (var category in result?.Categories.Take(6) ?? [])
        {
            var row = new Grid { Padding = new Thickness(0, 8, 0, 8), ColumnSpacing = 10 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.Children.Add(Text(CategoryName(category.Category), 14)); var size = Text(ByteSizeFormatter.Format(category.SizeBytes), 14, Brush("MutedBrush")); row.Children.Add(size); Grid.SetColumn(size, 1); var tag = Text(RiskName(category.Risk), 12, RiskBrush(category.Risk), true); tag.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(tag); Grid.SetColumn(tag, 2); panel.Children.Add(row);
        }
        panel.Children.Add(Action("查看全部来源  ›", () => navigate(AppPage.CodexUsage)));
        return Card(panel, 400);
    }

    private static UIElement QuickActions(Action<AppPage> navigate)
    {
        var panel = Stack(Text("快速操作", 16, Brush("InkBrush"), true));
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        var actions = new (string Label, string Hint, AppPage Page)[] { ("扫描 Codex 占用", "分析 Codex 相关数据", AppPage.CodexUsage), ("智能清理", "推荐安全清理", AppPage.Cleanup), ("大文件分析", "查找占用大文件", AppPage.LargeFiles), ("重复文件", "查看重复内容", AppPage.Duplicates), ("空间分析", "查看全盘分布", AppPage.SpaceChanges), ("已安装工具", "查看开发工具占用", AppPage.Tools) };
        for (var i = 0; i < actions.Length; i++) { var action = actions[i]; var button = Action(action.Label + "\n" + action.Hint, () => navigate(action.Page)); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.MinHeight = 78; grid.Children.Add(button); Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); }
        panel.Children.Add(grid); return Card(panel, 400);
    }

    private static UIElement TasksCard(MainViewModel vm, ScanResult? result, Action<AppPage> navigate)
    {
        var panel = Stack(Text("最近的 Codex 任务", 16, Brush("InkBrush"), true));
        var tasks = result?.Tasks.Take(5).ToList() ?? [];
        if (tasks.Count == 0) panel.Children.Add(Empty("尚未发现包含可访问工作目录的 Codex 任务。"));
        foreach (var task in tasks)
        {
            var row = new Grid { Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromArgb(32, 255, 196, 69)), CornerRadius = new CornerRadius(8), ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.Children.Add(Stack(Text(task.Name, 14, Brush("InkBrush"), true), Text(ByteSizeFormatter.Format(task.SizeBytes), 12, Brush("MutedBrush")))); var status = Action(ActivityName(task.Activity), () => { vm.SelectTask(task.Id); navigate(AppPage.WorktreeDetails); }); status.Padding = new Thickness(6, 3, 6, 3); row.Children.Add(status); Grid.SetColumn(status, 1); panel.Children.Add(row);
        }
        panel.Children.Add(Action("查看全部任务  ›", () => navigate(AppPage.Tasks))); return Card(panel, 400);
    }

    private static UIElement CodexUsage(MainViewModel vm, Action<AppPage> navigate)
    {
        var result = vm.Result; var body = new Grid { ColumnSpacing = 16 }; body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(Card(Stack(Text("分类占用", 16, Brush("InkBrush"), true), CategoryRows(result?.Categories ?? [])), 500)); var paths = Stack(Text("Codex 数据位置", 16, Brush("InkBrush"), true)); foreach (var item in result?.Candidates.Where(x => x.Category == ItemCategory.CodexData || x.Category == ItemCategory.Cache).OrderByDescending(x => x.SizeBytes).Take(12) ?? []) paths.Children.Add(Row(item.Name, ByteSizeFormatter.Format(item.SizeBytes), item.Path)); var pathCard = Card(paths, 500); body.Children.Add(pathCard); Grid.SetColumn(pathCard, 1); return Page("Codex 任务", "从本机 Codex Home 与相关缓存读取真实目录大小。", Stack(Tabs(("占用总览", () => navigate(AppPage.CodexUsage)), ("任务列表", () => navigate(AppPage.Tasks))), body));
    }

    private static UIElement Tasks(MainViewModel vm, Action<AppPage> navigate)
    {
        var panel = Stack(TableHeader("项目名称", "状态", "最后活动", "占用空间", "可清理"));
        foreach (var task in vm.Result?.Tasks.OrderByDescending(x => x.SizeBytes) ?? Enumerable.Empty<CodexTask>())
        {
            var row = new Grid { Padding = new Thickness(8, 12, 8, 12), ColumnSpacing = 10 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.Children.Add(Stack(Text(task.Name, 14, Brush("InkBrush"), true), Text(task.RootPath, 11, Brush("MutedBrush")))); Add(row, Text(ActivityName(task.Activity), 12, RiskBrush(task.Activity is TaskActivity.Active or TaskActivity.Recent ? RiskLevel.Safe : RiskLevel.Review), true), 1); Add(row, Text(TimeAgo(task.LastActivity), 12, Brush("MutedBrush")), 2); Add(row, Text(ByteSizeFormatter.Format(task.SizeBytes), 13, Brush("InkBrush"), true), 3); Add(row, Action("查看", () => { vm.SelectTask(task.Id); navigate(AppPage.WorktreeDetails); }), 4); panel.Children.Add(row);
        }
        if (vm.Result?.Tasks.Count == 0) panel.Children.Add(Empty("尚未发现任务。Codex 会话中的工作目录必须仍可访问。"));
        return Page("Codex 任务", "按照 Codex 任务和 Worktree 列出空间、活动状态与可再生成内容。", Stack(Tabs(("占用总览", () => navigate(AppPage.CodexUsage)), ("任务列表", () => navigate(AppPage.Tasks))), Card(panel)));
    }

    private static UIElement WorktreeDetails(MainViewModel vm, Action<AppPage> navigate)
    {
        var task = vm.SelectedTask;
        if (task is null) return Page("Worktree 详情", "选择任务后显示目录分类、Git 状态和安全清理范围。", Card(Empty("还没有可查看的 Worktree。")));
        var left = Stack(Text(task.Name, 20, Brush("InkBrush"), true), Text(task.RootPath, 12, Brush("MutedBrush")), Row("总占用", ByteSizeFormatter.Format(task.SizeBytes)), Row("最后活动", TimeAgo(task.LastActivity)), Row("Git 状态", task.IsGitWorktree ? (task.HasChanges ? "存在未提交修改，整体删除已阻止" : "clean") : "普通目录"), Row("未跟踪文件", task.HasUntracked ? "存在，整体删除已阻止" : "无"), Action("打开文件夹", () => _ = Windows.System.Launcher.LaunchFolderPathAsync(task.RootPath)), Action("返回任务追踪", () => navigate(AppPage.Tasks)));
        var right = Stack(Text("空间分布", 16, Brush("InkBrush"), true)); foreach (var item in task.Artifacts.OrderByDescending(x => x.SizeBytes)) right.Children.Add(Row(item.Name, ByteSizeFormatter.Format(item.SizeBytes), item.Detail));
        var grid = new Grid { ColumnSpacing = 16 }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) }); var a = Card(left, 500); var b = Card(right, 500); grid.Children.Add(a); grid.Children.Add(b); Grid.SetColumn(b, 1);
        return Page("Worktree 详情", "仅显示扫描所得目录；源码、Git、配置与数据库默认保护。", grid);
    }

    private static UIElement Cleanup(MainViewModel vm, Action<AppPage> navigate)
    {
        var basePlan = vm.CreateCleanupPlan(); var checks = new List<(CleanupCandidate Candidate, CheckBox CheckBox)>(); var list = Stack(Text("可释放空间（需确认）", 16, Brush("InkBrush"), true), Text($"预选安全项：{ByteSizeFormatter.Format(basePlan.SelectedBytes)}。缓存会永久删除；可再生成内容只会移入隔离区。", 14, Brush("MutedBrush")));
        foreach (var candidate in basePlan.Items)
        {
            var check = new CheckBox { IsChecked = candidate.Selected, VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 4, 0, 4) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.Children.Add(check); var content = Row(candidate.Item.Name, ByteSizeFormatter.Format(candidate.Item.SizeBytes), $"{RiskName(candidate.Item.Risk)} · {candidate.Impact}"); row.Children.Add(content); Grid.SetColumn((FrameworkElement)content, 1); list.Children.Add(row); checks.Add((candidate, check));
        }
        list.Children.Add(Action("查看清理确认", async () =>
        {
            var plan = vm.CreateCleanupPlan(checks.Select(x => x.Candidate with { Selected = x.CheckBox.IsChecked == true }));
            var dialog = new ContentDialog { XamlRoot = App.Window.Content.XamlRoot, Title = "准备清理", Content = $"将处理 {plan.Items.Count(x => x.Selected)} 项，预计影响 {ByteSizeFormatter.Format(plan.SelectedBytes)}。受保护路径、重解析点和计划后发生变化的路径会被阻止。", PrimaryButtonText = "开始清理", CloseButtonText = "取消" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await vm.ExecuteAsync(plan, CancellationToken.None);
        }, true));
        return Page("释放空间", "只列出已识别的缓存和可再生成内容；执行前会再次验证路径安全。", Stack(Tabs(("安全清理", () => navigate(AppPage.Cleanup)), ("缓存迁移", () => navigate(AppPage.Migration)), ("记录", () => navigate(AppPage.History))), Card(list)));
    }

    private static UIElement Migration(MainViewModel vm, Action<AppPage> navigate)
    {
        var target = new TextBox { Text = vm.MigrationTargetPath, PlaceholderText = "选择其他磁盘的目标文件夹，例如 D:\\CodexCleanerCaches" };
        var selected = new List<(MigrationItem Item, CheckBox Box)>();
        var list = Stack(Text(vm.MigrationMessage, 14, Brush("MutedBrush")), Action("选择目标文件夹", async () =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) target.Text = folder.Path;
        }), Action("生成迁移计划", () => _ = vm.DiscoverMigrationAsync(target.Text.Trim()), true));
        foreach (var item in vm.MigrationCandidates)
        {
            var check = new CheckBox { IsChecked = false, VerticalAlignment = VerticalAlignment.Center };
            selected.Add((item, check));
            var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(check); var info = Row(item.DisplayName, ByteSizeFormatter.Format(item.ExpectedBytes), $"{item.SourcePath} → {item.TargetPath}"); row.Children.Add(info); Grid.SetColumn((FrameworkElement)info, 1); list.Children.Add(row);
        }
        list.Children.Add(Action("复制并切换配置", async () =>
        {
            var items = selected.Where(x => x.Box.IsChecked == true).Select(x => x.Item).ToList();
            if (items.Count == 0) return;
            var dialog = new ContentDialog { XamlRoot = App.Window.Content.XamlRoot, Title = "确认缓存迁移", Content = "将复制选定缓存、切换工具缓存配置并进行验证。C 盘源缓存会保留，不会在此操作中删除。", PrimaryButtonText = "开始迁移", CloseButtonText = "取消" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await vm.ExecuteMigrationAsync(target.Text.Trim(), items);
        }, true));
        return Page("释放空间 · 缓存迁移", "仅支持 npm、pip、uv 和 Playwright 的明确缓存配置；安装目录、SDK、Docker/WSL 和模型只分析。", Stack(Tabs(("安全清理", () => navigate(AppPage.Cleanup)), ("缓存迁移", () => navigate(AppPage.Migration)), ("记录", () => navigate(AppPage.History))), Card(Stack(target, list))));
    }

    private static UIElement Developer(MainViewModel vm, Action<AppPage> navigate)
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        for (var i = 0; i < 3; i++) { grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.RowDefinitions.Add(new RowDefinition()); }
        var items = vm.Result?.Candidates.Where(x => x.Source == "开发环境").ToList() ?? [];
        static bool Matches(StorageItem item, params string[] keys) => keys.Any(key => item.Path.Contains(key, StringComparison.OrdinalIgnoreCase) || (item.Detail?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false));
        var namedGroups = new (string Label, Func<StorageItem, bool> Match)[]
        {
            ("Node.js", x => Matches(x, "node", "npm", "pnpm", "yarn", "bun", "playwright", "electron", "puppeteer")),
            ("Python", x => Matches(x, "python", "pip", "uv", ".venv")),
            (".NET / NuGet", x => Matches(x, "dotnet", "nuget")),
            ("Rust / Cargo", x => Matches(x, "cargo", "rustup")),
            ("Java / Android", x => Matches(x, "gradle", "maven", ".m2", "android", "jdk")),
            ("Visual Studio / SDK", x => Matches(x, "visual studio", "windows kits", "msvc", "build tools")),
            ("Docker / WSL", x => Matches(x, "docker", "wsl", "ext4.vhdx")),
            ("AI / 模型", x => x.Category == ItemCategory.AiModel || Matches(x, "hugging", "ollama", "lm studio", "modelscope", "comfy", "torch"))
        };
        var groups = namedGroups.Concat(new[]
        {
            (Label: "其他开发环境", Match: (Func<StorageItem, bool>)(item => !namedGroups.Any(group => group.Match(item))))
        }).ToArray();
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i]; var matching = items.Where(group.Match).ToList(); var size = matching.Sum(x => x.SizeBytes);
            var detail = matching.Count == 0 ? "未检测到可访问目录" : $"{matching.Count} 个真实扫描目录";
            var card = Card(Stack(Text(group.Label, 16, Brush("InkBrush"), true), Text(ByteSizeFormatter.Format(size), 22, Brush("InkBrush"), true), Text(detail, 12, Brush("MutedBrush"))), 150);
            grid.Children.Add(card); Grid.SetColumn(card, i % 3); Grid.SetRow(card, i / 3);
        }
        return Page("开发环境", "按真实检测路径分组；SDK、Docker 数据和 WSL 虚拟磁盘只分析，不提供直接删除。", Stack(Tabs(("缓存与依赖", () => navigate(AppPage.Developer)), ("已安装工具", () => navigate(AppPage.Tools))), grid));
    }

    private static UIElement Tools(MainViewModel vm, Action<AppPage> navigate)
    {
        var tools = vm.Result?.Tools ?? [];
        var panel = Stack(TableHeader("工具 / 路径", "版本", "占用", "Codex 关联", "操作"));
        foreach (var tool in tools.Take(200))
        {
            var row = new Grid { Padding = new Thickness(8, 10, 8, 10), ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.Children.Add(Stack(Text(tool.Name, 14, Brush("InkBrush"), true), Text(tool.InstallPath ?? "安装路径不可用", 11, Brush("MutedBrush"))));
            Add(row, Text(tool.Version ?? "未知", 12, Brush("MutedBrush")), 1); Add(row, Text(ByteSizeFormatter.Format(tool.SizeBytes), 12, Brush("MutedBrush")), 2);
            var relation = tool.Attribution.Level switch { AttributionLevel.Confirmed => "明确关联", AttributionLevel.High => "高度可能", AttributionLevel.Possible => "可能相关", _ => "无法确认" };
            Add(row, Text(relation, 12, Brush("MutedBrush")), 3); Add(row, tool.InstallPath is null ? Text("路径不可用", 12, Brush("MutedBrush")) : Action("打开位置", () => _ = Windows.System.Launcher.LaunchFolderPathAsync(tool.InstallPath)), 4); panel.Children.Add(row);
        }
        if (tools.Count == 0) panel.Children.Add(Empty("尚未完成工具扫描，或未从 Windows 已安装应用中发现可访问条目。"));
        return Page("开发环境 · 已安装工具", "从 Windows 已安装应用读取版本、路径、大小和安装时间；不会直接删除安装目录。", Stack(Tabs(("缓存与依赖", () => navigate(AppPage.Developer)), ("已安装工具", () => navigate(AppPage.Tools))), Card(panel)));
    }

    private static UIElement SpaceChanges(MainViewModel vm, Action<AppPage> navigate)
    {
        var changes = vm.Result?.Changes ?? [];
        var headline = changes.Count == 0 ? "已建立扫描基线" : $"{(changes.Sum(x => x.DeltaBytes) >= 0 ? "+" : "")}{ByteSizeFormatter.Format(changes.Sum(x => x.DeltaBytes))}";
        var panel = Stack(Text("过去 7 天", 16, Brush("InkBrush"), true), Text(headline, 30, Brush("InkBrush"), true), Text(changes.Count == 0 ? "下一次完整扫描将依据本地快照计算真实增长。" : "仅比较完整本地快照，权限不足或中断扫描不会参与结论。", 14, Brush("MutedBrush")));
        if (changes.Count == 0) foreach (var category in vm.Result?.Categories.Take(8) ?? []) panel.Children.Add(Row(CategoryName(category.Category), ByteSizeFormatter.Format(category.SizeBytes), "当前扫描占用"));
        else foreach (var change in changes) panel.Children.Add(Row(CategoryName(change.Category), $"{(change.DeltaBytes >= 0 ? "+" : "")}{ByteSizeFormatter.Format(change.DeltaBytes)}", "相较上一份完整快照"));
        panel.Children.Add(Tabs(("变化", () => navigate(AppPage.SpaceChanges)), ("大文件", () => navigate(AppPage.LargeFiles)), ("重复文件", () => navigate(AppPage.Duplicates))));
        return Page("空间分析", "只比较完整本地快照；没有足够历史时不会生成虚假增长数据。", Card(panel));
    }

    private static UIElement LargeFiles(MainViewModel vm, Action<AppPage> navigate)
    {
        var files = vm.Result?.Candidates.Where(x => !x.IsDirectory && x.SizeBytes >= 100 * 1024 * 1024).OrderByDescending(x => x.SizeBytes).ToList() ?? [];
        var panel = Stack(TableHeader("文件", "大小", "类型", "路径")); if (files.Count == 0) panel.Children.Add(Empty("快速扫描未发现超过 100 MB 的可访问单文件。执行深度扫描后会包含 C 盘文件。"));
        foreach (var file in files) panel.Children.Add(Row(file.Name, ByteSizeFormatter.Format(file.SizeBytes), file.Path));
        return Page("空间分析 · 大文件", "按真实文件大小列出可访问文件；大文件不会自动等同于垃圾。", Stack(Tabs(("变化", () => navigate(AppPage.SpaceChanges)), ("大文件", () => navigate(AppPage.LargeFiles)), ("重复文件", () => navigate(AppPage.Duplicates))), Card(panel)));
    }

    private static UIElement Duplicates(MainViewModel vm, Action<AppPage> navigate)
    {
        var content = Stack(Text(vm.DuplicateMessage, 14, Brush("MutedBrush")), Action("开始重复文件检测", () => _ = vm.FindDuplicatesAsync(), true));
        foreach (var group in vm.DuplicateGroups) content.Children.Add(Row($"{group.Paths.Count} 个完全重复文件", ByteSizeFormatter.Format(group.SizeBytes * (group.Paths.Count - 1)), string.Join("\n", group.Paths.Take(3))));
        return Page("空间分析 · 重复文件", "只在大小、首尾快速哈希与完整 SHA-256 均一致时显示为完全重复；不会自动清理。", Stack(Tabs(("变化", () => navigate(AppPage.SpaceChanges)), ("大文件", () => navigate(AppPage.LargeFiles)), ("重复文件", () => navigate(AppPage.Duplicates))), Card(content)));
    }

    private static UIElement History(MainViewModel vm, Action<AppPage> navigate)
    {
        var content = Stack(Text("每次清理都记录实际结果；移入 C 盘隔离区的内容只显示待释放。", 13, Brush("MutedBrush")));
        foreach (var record in vm.CleanupRecords) content.Children.Add(Row(Path.GetFileName(record.Path), ByteSizeFormatter.Format(record.ReleasedBytes), $"{record.CompletedAt.LocalDateTime:g} · {RiskName(record.Risk)} · {(record.Success ? "成功" : record.Error ?? "失败")}"));
        if (vm.CleanupRecords.Count == 0) content.Children.Add(Empty("尚无清理记录。"));
        foreach (var record in vm.MigrationRecords) content.Children.Add(Row(record.Kind switch { MigrationKind.NpmCache => "npm 缓存迁移", MigrationKind.PipCache => "pip 缓存迁移", MigrationKind.UvCache => "uv 缓存迁移", _ => "Playwright 迁移" }, ByteSizeFormatter.Format(record.Bytes), $"{record.CompletedAt.LocalDateTime:g} · {record.State} · {record.Message}"));
        return Page("释放空间 · 记录", "读取本机清理与迁移记录。", Stack(Tabs(("安全清理", () => navigate(AppPage.Cleanup)), ("缓存迁移", () => navigate(AppPage.Migration)), ("记录", () => navigate(AppPage.History))), Card(content)));
    }

    private static UIElement Settings(MainViewModel vm)
    {
        var current = vm.CurrentSettings;
        var home = new TextBox { Text = current.CodexHome ?? string.Empty, PlaceholderText = "自动检测：CODEX_HOME 或 %USERPROFILE%\\.codex" };
        var roots = new TextBox { Text = string.Join(Environment.NewLine, current.AdditionalRoots), AcceptsReturn = true, MinHeight = 84, PlaceholderText = "每行一个附加扫描根" };
        var protectedPaths = new TextBox { Text = string.Join(Environment.NewLine, current.ProtectedPaths), AcceptsReturn = true, MinHeight = 84, PlaceholderText = "每行一个永久保护路径" };
        var ignoredPaths = new TextBox { Text = string.Join(Environment.NewLine, current.IgnoredPaths), AcceptsReturn = true, MinHeight = 84, PlaceholderText = "每行一个忽略扫描路径" };
        var theme = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = current.Theme };
        var quarantine = new ComboBox { ItemsSource = new[] { 7, 14, 30 }, SelectedItem = current.QuarantineDays };
        var confirm = new ToggleSwitch { Header = "删除前确认", IsOn = current.ConfirmBeforeDelete };
        var git = new ToggleSwitch { Header = "整项目删除前检查 Git", IsOn = current.CheckGitBeforeDelete };
        var reduced = new ToggleSwitch { Header = "减少动画", IsOn = current.ReduceMotion };
        var autoUpdate = new ToggleSwitch { Header = "启动时检查 GitHub 更新", IsOn = current.AutoCheckUpdates };
        static IReadOnlyList<string> Lines(TextBox box) => box.Text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var panel = Stack(
            Text("扫描设置", 16, Brush("InkBrush"), true), Text("Codex Home", 13, Brush("MutedBrush"), true), home,
            Text("附加扫描根", 13, Brush("MutedBrush"), true), roots,
            Text("保护与忽略", 16, Brush("InkBrush"), true), Text("永久保护路径", 13, Brush("MutedBrush"), true), protectedPaths, Text("忽略扫描路径", 13, Brush("MutedBrush"), true), ignoredPaths,
            Text("外观", 16, Brush("InkBrush"), true), theme,
            Text("安全与动效", 16, Brush("InkBrush"), true), Text("隔离区保留天数", 13, Brush("MutedBrush"), true), quarantine, confirm, git, reduced,
            Text("自动更新", 16, Brush("InkBrush"), true), autoUpdate, Text($"当前版本：{vm.CurrentVersion}。仅请求 GitHub 公共 Release，不上传本机扫描信息。{(current.LastUpdateCheckAt is null ? "" : $" 上次检查：{current.LastUpdateCheckAt.Value.LocalDateTime:g}")}", 13, Brush("MutedBrush")), Text(vm.UpdateResult?.Message ?? "尚未检查更新。", 13, Brush("MutedBrush")), Action("立即检查更新", () => _ = vm.CheckForUpdatesAsync()),
            Action("保存设置", () => _ = vm.SaveSettingsAsync(current with { CodexHome = string.IsNullOrWhiteSpace(home.Text) ? null : home.Text.Trim(), AdditionalRoots = Lines(roots), Theme = theme.SelectedItem as string ?? "System", QuarantineDays = quarantine.SelectedItem is int days ? days : 14, ConfirmBeforeDelete = confirm.IsOn, CheckGitBeforeDelete = git.IsOn, ReduceMotion = reduced.IsOn, ProtectedPaths = Lines(protectedPaths), IgnoredPaths = Lines(ignoredPaths), AutoCheckUpdates = autoUpdate.IsOn }, CancellationToken.None), true));
        return Page("设置", "所有路径和扫描数据仅保存在本机。", Card(panel));
    }

    private static UIElement CategoryRows(IReadOnlyList<CategorySummary> categories)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var category in categories) panel.Children.Add(Row(CategoryName(category.Category), ByteSizeFormatter.Format(category.SizeBytes), RiskName(category.Risk)));
        if (categories.Count == 0) panel.Children.Add(Empty("正在扫描或没有可显示的数据。"));
        return panel;
    }

    private static UIElement Row(string title, string value, string? note = null)
    {
        var grid = new Grid { Padding = new Thickness(0, 6, 0, 6), ColumnSpacing = 10 }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); grid.Children.Add(Stack(Text(title, 14, Brush("InkBrush"), true), string.IsNullOrWhiteSpace(note) ? new TextBlock() : Text(note, 11, Brush("MutedBrush")))); var right = Text(value, 13, Brush("MutedBrush")); grid.Children.Add(right); Grid.SetColumn(right, 1); return grid;
    }

    private static UIElement TableHeader(params string[] labels)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32, Padding = new Thickness(8, 0, 8, 10) }; foreach (var label in labels) panel.Children.Add(Text(label, 12, Brush("MutedBrush"), true)); return panel;
    }
    private static UIElement Empty(string message) => new Border { Background = new SolidColorBrush(Color.FromArgb(20, 36, 107, 253)), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = Text(message, 14, Brush("MutedBrush")) };
    private static void Add(Grid grid, UIElement element, int column) { grid.Children.Add(element); if (element is FrameworkElement frameworkElement) Grid.SetColumn(frameworkElement, column); }
    private static Brush CategoryBrush(ItemCategory category) => category switch { ItemCategory.Worktree => Brush("BrandBrush"), ItemCategory.Dependency => new SolidColorBrush(Color.FromArgb(255, 72, 145, 255)), ItemCategory.BuildArtifact => Brush("WarningBrush"), ItemCategory.Cache => Brush("SuccessBrush"), ItemCategory.AiModel => new SolidColorBrush(Color.FromArgb(255, 139, 92, 246)), _ => Brush("MutedBrush") };
    private static Brush RiskBrush(RiskLevel risk) => risk switch { RiskLevel.Safe => Brush("SuccessBrush"), RiskLevel.Rebuildable => Brush("BrandBrush"), RiskLevel.Review => Brush("WarningBrush"), _ => Brush("DangerBrush") };
    private static string CategoryName(ItemCategory category) => category switch { ItemCategory.CodexData => "Codex 数据", ItemCategory.Worktree => "Codex Worktree", ItemCategory.Dependency => "项目依赖", ItemCategory.BuildArtifact => "构建产物", ItemCategory.Cache => "开发缓存", ItemCategory.VirtualEnvironment => "虚拟环境", ItemCategory.AiModel => "AI 模型 / 下载缓存", ItemCategory.InstalledTool => "开发工具", _ => "其他" };
    private static string RiskName(RiskLevel risk) => risk switch { RiskLevel.Safe => "安全清理", RiskLevel.Rebuildable => "可再生成", RiskLevel.Review => "建议检查", _ => "已保护" };
    private static string ActivityName(TaskActivity activity) => activity switch { TaskActivity.Active => "正在使用", TaskActivity.Recent => "近期使用", TaskActivity.Normal => "正常保留", TaskActivity.Review => "建议检查", TaskActivity.Stale => "可能清理", _ => "未知" };
    private static string TimeAgo(DateTimeOffset time) { var span = DateTimeOffset.UtcNow - time; return span.TotalDays switch { < 1 => "今天", < 7 => $"{(int)span.TotalDays} 天前", _ => time.LocalDateTime.ToString("yyyy-MM-dd") }; }
    private static string? FindOnPath(string executable) => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Select(x => Path.Combine(x, executable + ".exe")).FirstOrDefault(File.Exists);
}

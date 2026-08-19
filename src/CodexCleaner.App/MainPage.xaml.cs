using CodexCleaner.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace CodexCleaner.App;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = (MainViewModel)App.Services.GetService(typeof(MainViewModel))!;
    private AppPage _currentPage = AppPage.Dashboard;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(MainViewModel.CurrentSettings)) App.ApplyTheme(ViewModel.CurrentSettings.Theme); if (args.PropertyName is nameof(MainViewModel.Result) or nameof(MainViewModel.ScanState) or nameof(MainViewModel.SelectedTaskId) or nameof(MainViewModel.DuplicateGroups) or nameof(MainViewModel.CleanupRecords) or nameof(MainViewModel.CurrentSettings) or nameof(MainViewModel.MigrationRecords) or nameof(MainViewModel.MigrationCandidates) or nameof(MainViewModel.MigrationMessage) or nameof(MainViewModel.UpdateResult)) ShowPage(_currentPage); };
        ShowPage(_currentPage);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) { await ViewModel.InitializeAsync(); App.ApplyTheme(ViewModel.CurrentSettings.Theme); await ViewModel.RefreshAsync(); }
    private void Navigate_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<AppPage>(tag, out var page)) ShowPage(page); }
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var query = new TextBox { PlaceholderText = "搜索项目名或已扫描路径" };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "搜索本机扫描结果", Content = query, PrimaryButtonText = "搜索", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var text = query.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        var result = ViewModel.Result;
        var taskMatches = result?.Tasks.Where(x => x.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || x.RootPath.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(12).ToList() ?? [];
        var pathMatches = result?.Candidates.Where(x => x.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || x.Path.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(24).ToList() ?? [];
        var rows = new StackPanel { Spacing = 8 };
        ContentDialog? resultsDialog = null;
        foreach (var task in taskMatches)
        {
            var button = new Button { Content = $"任务：{task.Name}\n{task.RootPath}", HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => { resultsDialog?.Hide(); ViewModel.SelectTask(task.Id); ShowPage(AppPage.WorktreeDetails); };
            rows.Children.Add(button);
        }
        foreach (var item in pathMatches) rows.Children.Add(new TextBlock { Text = $"路径：{item.Path}", TextWrapping = TextWrapping.Wrap });
        if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "没有与本次扫描结果匹配的项目或路径。" });
        resultsDialog = new ContentDialog { XamlRoot = XamlRoot, Title = $"搜索结果：{text}", Content = new ScrollViewer { Content = rows, MaxHeight = 420 }, CloseButtonText = "关闭" };
        await resultsDialog.ShowAsync();
    }
    internal void ShowPage(AppPage page)
    {
        _currentPage = page;
        var reduceMotion = ViewModel.CurrentSettings.ReduceMotion;
        PageHost.Opacity = reduceMotion ? 1 : 0;
        var translation = new TranslateTransform { Y = reduceMotion ? 0 : 8 };
        PageHost.RenderTransform = translation;
        PageHost.Content = PageRenderer.Create(page, ViewModel, ShowPage);
        if (!reduceMotion)
        {
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(180)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fade, PageHost); Storyboard.SetTargetProperty(fade, "Opacity");
            var slide = new DoubleAnimation { From = 8, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(180)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(slide, translation); Storyboard.SetTargetProperty(slide, "Y");
            var storyboard = new Storyboard(); storyboard.Children.Add(fade); storyboard.Children.Add(slide); storyboard.Begin();
        }
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        var buttons = new Dictionary<AppPage, Button>
        {
            [AppPage.Dashboard] = NavDashboard,
            [AppPage.CodexUsage] = NavCodex,
            [AppPage.Tasks] = NavCodex,
            [AppPage.WorktreeDetails] = NavCodex,
            [AppPage.Cleanup] = NavReleaseSpace,
            [AppPage.Migration] = NavReleaseSpace,
            [AppPage.Developer] = NavDeveloper,
            [AppPage.Tools] = NavDeveloper,
            [AppPage.SpaceChanges] = NavSpaceChanges,
            [AppPage.LargeFiles] = NavSpaceChanges,
            [AppPage.Duplicates] = NavSpaceChanges,
            [AppPage.History] = NavReleaseSpace,
            [AppPage.Settings] = NavDashboard
        };
        foreach (var button in buttons.Values.Distinct())
        {
            button.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
            button.Foreground = (Brush)Application.Current.Resources["InkBrush"];
        }
        var selected = buttons[_currentPage];
        selected.Background = (Brush)Application.Current.Resources["BrandSoftBrush"];
        selected.Foreground = (Brush)Application.Current.Resources["BrandBrush"];
    }
}

internal enum AppPage { Dashboard, CodexUsage, Tasks, WorktreeDetails, Cleanup, Migration, Developer, Tools, SpaceChanges, LargeFiles, Duplicates, History, Settings }

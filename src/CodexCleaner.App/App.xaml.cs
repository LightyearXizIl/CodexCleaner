using Microsoft.UI.Xaml;
using CodexCleaner.Core;
using CodexCleaner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Runtime.InteropServices;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CodexCleaner.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _services;
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public static IServiceProvider Services => ((App)Current)._services;

    public static void ApplyTheme(string preference)
    {
        var dark = preference.Equals("Dark", StringComparison.OrdinalIgnoreCase) || (preference.Equals("System", StringComparison.OrdinalIgnoreCase) && new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128);
        var colors = dark
            ? new Dictionary<string, Color> { ["CanvasBrush"] = Color.FromArgb(255, 17, 21, 30), ["SurfaceBrush"] = Color.FromArgb(255, 27, 33, 45), ["InkBrush"] = Color.FromArgb(255, 242, 245, 251), ["MutedBrush"] = Color.FromArgb(255, 164, 176, 196), ["LineBrush"] = Color.FromArgb(255, 52, 62, 79), ["BrandBrush"] = Color.FromArgb(255, 91, 145, 255), ["BrandSoftBrush"] = Color.FromArgb(255, 32, 54, 93), ["SuccessBrush"] = Color.FromArgb(255, 69, 191, 132), ["WarningBrush"] = Color.FromArgb(255, 242, 180, 74), ["DangerBrush"] = Color.FromArgb(255, 246, 108, 122) }
            : new Dictionary<string, Color> { ["CanvasBrush"] = Color.FromArgb(255, 246, 248, 252), ["SurfaceBrush"] = Colors.White, ["InkBrush"] = Color.FromArgb(255, 18, 24, 38), ["MutedBrush"] = Color.FromArgb(255, 102, 112, 133), ["LineBrush"] = Color.FromArgb(255, 229, 234, 242), ["BrandBrush"] = Color.FromArgb(255, 36, 107, 253), ["BrandSoftBrush"] = Color.FromArgb(255, 238, 244, 255), ["SuccessBrush"] = Color.FromArgb(255, 22, 165, 106), ["WarningBrush"] = Color.FromArgb(255, 242, 169, 59), ["DangerBrush"] = Color.FromArgb(255, 237, 92, 107) };
        foreach (var (key, color) in colors) if (Current.Resources[key] is SolidColorBrush brush) brush.Color = color;
    }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
        // Microsoft.Data.Sqlite resolves |DataDirectory| through ApplicationData
        // when it is not set. An unpackaged portable app has no package identity,
        // so that WinRT call terminates the process before managed error handling.
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner");
        Directory.CreateDirectory(localData);
        AppContext.SetData("DataDirectory", localData);
        UnhandledException += (_, args) =>
        {
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCleaner", "Logs");
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(Path.Combine(logDirectory, "app-errors.log"), $"{DateTimeOffset.UtcNow:O} {args.Message}{Environment.NewLine}");
            }
            catch { }
        };
        var collection = new ServiceCollection();
        collection.AddSingleton<DirectorySizer>();
        collection.AddSingleton<IExternalCommandRunner, ExternalCommandRunner>();
        collection.AddSingleton<ISettingsService, AppSettingsService>();
        collection.AddSingleton<IRiskService, RiskService>();
        collection.AddSingleton<IGitStatusService, GitStatusService>();
        collection.AddSingleton<ICodexScanner, CodexScanner>();
        collection.AddSingleton<IProjectScanner, ProjectScanner>();
        collection.AddSingleton<ICodexProjectService>(provider => (ICodexProjectService)provider.GetRequiredService<IProjectScanner>());
        collection.AddSingleton<IStorageScanner, StorageScanner>();
        collection.AddSingleton<IDeveloperEnvironmentScanner, DeveloperEnvironmentScanner>();
        collection.AddSingleton<IInstalledToolScanner, InstalledToolScanner>();
        collection.AddSingleton<IAttributionService, AttributionService>();
        collection.AddSingleton<IInsightService, InsightService>();
        collection.AddSingleton<IUpdateService, GitHubUpdateService>();
        collection.AddSingleton<IMigrationService, MigrationService>();
        // MSIX uses SQLite/WAL. An unpackaged portable executable has no package
        // identity on some Windows App SDK builds, so it uses an atomic local
        // history store rather than crashing during SQLite's WinRT probe.
        collection.AddSingleton<IHistoryService>(_ => PackageIdentity.HasIdentity() ? new SqliteHistoryService() : new PortableJsonHistoryService());
        collection.AddSingleton<IDuplicateService, DuplicateService>();
        collection.AddSingleton<ICleanupService, CleanupService>();
        collection.AddSingleton<IScanCoordinator, ScanCoordinator>();
        collection.AddSingleton<ViewModels.MainViewModel>();
        _services = collection.BuildServiceProvider();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
    }
}

internal static class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder? packageFullName);
    public static bool HasIdentity()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, null) == ErrorInsufficientBuffer;
    }
}

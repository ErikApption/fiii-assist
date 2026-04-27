using Microsoft.UI.Xaml;
using QfxWatcher.Services;
using QfxWatcher.ViewModels;

namespace QfxWatcher;

/// <summary>
/// Provides application-specific behaviour and bootstraps the service layer.
/// </summary>
public partial class App : Application
{
    // Simple manual DI – avoids pulling in a full DI container
    internal static SettingsService     SettingsService     { get; } = new();
    internal static ActualBudgetService ActualBudgetService { get; } = new();
    internal static FileWatcherService  FileWatcherService  { get; } = new();

    internal static DashboardViewModel DashboardViewModel { get; } =
        new(SettingsService, FileWatcherService, ActualBudgetService);

    internal static SettingsViewModel SettingsViewModel { get; } =
        new(SettingsService, ActualBudgetService, FileWatcherService);

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}

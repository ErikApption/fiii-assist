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
    internal static FireflyIIIService FireflyService { get; } = new();
    internal static FileWatcherService  FileWatcherService  { get; } = new();

    internal static ImportWizardViewModel ImportWizardViewModel { get; } =
        new(FireflyService, SettingsService);

    internal static DashboardViewModel DashboardViewModel { get; } =
        new(SettingsService, FileWatcherService, FireflyService, ImportWizardViewModel);

    internal static SettingsViewModel SettingsViewModel { get; } =
        new(SettingsService, FireflyService, FileWatcherService);

    internal static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}

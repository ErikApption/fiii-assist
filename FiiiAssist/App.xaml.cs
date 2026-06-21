using Microsoft.UI.Xaml;
using FiiiAssist.Services;
using FiiiAssist.ViewModels;

namespace FiiiAssist;

/// <summary>
/// Provides application-specific behaviour and bootstraps the service layer.
/// </summary>
public partial class App : Application
{
    // Simple manual DI – avoids pulling in a full DI container
    internal static SettingsService     SettingsService     { get; } = new();
    internal static BankAccountMappingService BankAccountMappingService { get; } = new();
    internal static FireflyIIIService FireflyService { get; } = new();
    internal static FileWatcherService  FileWatcherService  { get; } = new();
    internal static QfxFileTrackingService QfxFileTrackingService { get; } = new();

    internal static ImportWizardViewModel ImportWizardViewModel { get; } =
        new(FireflyService, SettingsService);

    internal static DashboardViewModel DashboardViewModel { get; } =
        new(SettingsService, FileWatcherService, FireflyService, ImportWizardViewModel);

    internal static SettingsViewModel SettingsViewModel { get; } =
        new(SettingsService, FireflyService, FileWatcherService);

    internal static BankAccountsViewModel BankAccountsViewModel { get; } =
        new(BankAccountMappingService, FireflyService);

    internal static Window? MainWindow { get; private set; }

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

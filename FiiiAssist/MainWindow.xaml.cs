using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FiiiAssist.Models;
using FiiiAssist.Pages;
using FiiiAssist.Services;
using FiiiAssist.ViewModels;

namespace FiiiAssist;

public sealed partial class MainWindow : Window
{
    public SettingsViewModel SettingsVM => App.SettingsViewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Navigate to Settings by default so the user can configure the connection first.
        // Dashboard is disabled until a successful connection test.
        NavView.SelectedItem = NavView.MenuItems[2];
        ContentFrame.Navigate(typeof(SettingsPage));

        // If credentials are already saved, auto-connect on launch (deferred until UI is ready)
        ContentFrame.Loaded += OnContentFrameLoaded;
    }

    private async void OnContentFrameLoaded(object sender, RoutedEventArgs e)
    {
        ContentFrame.Loaded -= OnContentFrameLoaded;

        if (!string.IsNullOrWhiteSpace(SettingsVM.ServerUrl) &&
            !string.IsNullOrWhiteSpace(SettingsVM.ServerToken))
        {
            await SettingsVM.TestConnectionCommand.ExecuteAsync(null);

            // Let the dashboard know the connection is ready
            if (SettingsVM.IsConnected)
            {
                App.DashboardViewModel.NotifyConnected();

                // Show pending QFX files popup after connection is confirmed
                await ShowPendingFilesDialogAsync();
            }
        }
    }

    // ── Pending QFX files popup ───────────────────────────────────────────────

    private async Task ShowPendingFilesDialogAsync()
    {
        var settings = App.SettingsService.Load();
        var watchFolder = string.IsNullOrWhiteSpace(settings.WatchFolder)
            ? FileWatcherService.DetectEdgeDownloadsFolder()
            : settings.WatchFolder;

        var tracker = App.QfxFileTrackingService;
        tracker.CleanupStaleEntries();
        var pendingFiles = tracker.GetPendingFiles(watchFolder);

        if (pendingFiles.Count == 0)
            return;

        // Show a dialog for each pending file
        foreach (var file in pendingFiles)
        {
            var result = await ShowSingleFilePromptAsync(file);

            if (result == ContentDialogResult.Primary)
            {
                // User chose to import — trigger the import wizard with this file
                tracker.MarkImported(file.FilePath);
                var wizard = App.ImportWizardViewModel;
                wizard.OpenWizardCommand.Execute(null);
                await wizard.FileSelectedAsync(file.FilePath);

                // Navigate to Dashboard so the user can see the wizard
                NavView.SelectedItem = NavView.MenuItems[0];
                ContentFrame.Navigate(typeof(DashboardPage));

                // Only process one file at a time via the wizard — break out
                // so the user can complete the import before being prompted again
                break;
            }
            else
            {
                // User chose to skip this file
                tracker.MarkSkipped(file.FilePath);
            }
        }
    }

    private async Task<ContentDialogResult> ShowSingleFilePromptAsync(QfxFileEntry file)
    {
        var panel = new StackPanel { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = file.FileName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 16,
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Account ID:  {(string.IsNullOrWhiteSpace(file.AccountId) ? "(not found)" : file.AccountId)}",
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"File date:  {file.TimestampText}",
        });

        var dialog = new ContentDialog
        {
            Title = "QFX File Found — Import?",
            Content = panel,
            PrimaryButtonText = "Import",
            SecondaryButtonText = "Skip",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ContentFrame.XamlRoot,
        };

        return await dialog.ShowAsync();
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        // Block navigation to Dashboard/BankAccounts when not connected
        var tag = item.Tag?.ToString();
        if ((tag == "Dashboard" || tag == "BankAccounts") && !SettingsVM.IsConnected)
        {
            // Re-select Settings
            NavView.SelectedItem = NavView.MenuItems[2];
            return;
        }

        _ = tag switch
        {
            "Dashboard"    => ContentFrame.Navigate(typeof(DashboardPage)),
            "BankAccounts" => ContentFrame.Navigate(typeof(BankAccountsPage)),
            "Settings"     => ContentFrame.Navigate(typeof(SettingsPage)),
            _              => false,
        };
    }
}

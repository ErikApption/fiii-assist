using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FiiiAssist.Models;
using FiiiAssist.Pages;
using FiiiAssist.Services;
using FiiiAssist.ViewModels;
using System.Threading.Tasks;

namespace FiiiAssist;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class MainWindow : Window
{
    public SettingsViewModel SettingsVM => App.SettingsViewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Navigate to Settings by default so the user can configure the connection first.
        // Dashboard is disabled until a successful connection test.
        NavView.SelectedItem = NavView.MenuItems[4];
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
            }
        }

        // Always check for pending QFX files on startup, regardless of connection status.
        // The user should be informed about unprocessed files even if the server is unreachable.
        await ShowPendingFilesDialogAsync();
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
                if (!SettingsVM.IsConnected)
                {
                    // Can't import without a connection — inform the user and leave file as pending
                    var errorDialog = new ContentDialog
                    {
                        Title = "Cannot Import",
                        Content = "Not connected to Firefly III. The file will remain pending for next startup.",
                        CloseButtonText = "OK",
                        XamlRoot = ContentFrame.XamlRoot,
                    };
                    await errorDialog.ShowAsync();
                    break;
                }

                // User chose to import — open the wizard (don't mark as imported yet;
                // the wizard/import flow will handle status once the import succeeds or fails)
                var wizard = App.ImportWizardViewModel;
                wizard.OpenWizardCommand.Execute(null);
                await wizard.FileSelectedAsync(file.FilePath);

                // Navigate to Dashboard so the user can see the wizard
                NavView.SelectedItem = NavView.MenuItems[0];
                ContentFrame.Navigate(typeof(ImportPage));

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

        // Block navigation to Dashboard/BankAccounts/DuplicateChecker/SubscriptionDetector when not connected
        var tag = item.Tag?.ToString();
        if ((tag == "Dashboard" || tag == "BankAccounts" || tag == "DuplicateChecker" || tag == "SubscriptionDetector") && !SettingsVM.IsConnected)
        {
            // Re-select Settings
            NavView.SelectedItem = NavView.MenuItems[4];
            return;
        }

        _ = tag switch
        {
            "Dashboard"            => ContentFrame.Navigate(typeof(ImportPage)),
            "BankAccounts"         => ContentFrame.Navigate(typeof(BankAccountsPage)),
            "DuplicateChecker"     => ContentFrame.Navigate(typeof(DuplicateCheckerPage)),
            "SubscriptionDetector" => ContentFrame.Navigate(typeof(SubscriptionDetectorPage)),
            "Settings"             => ContentFrame.Navigate(typeof(SettingsPage)),
            _                      => false,
        };
    }
}

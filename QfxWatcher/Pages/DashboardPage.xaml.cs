using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using QfxWatcher.ViewModels;

namespace QfxWatcher.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel => App.DashboardViewModel;

    private DispatcherQueue? _dispatcher;

    public DashboardPage()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        ViewModel.ImportRequested += OnImportRequested;
    }

    protected override void OnNavigatedFrom(
        Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        ViewModel.ImportRequested -= OnImportRequested;
        base.OnNavigatedFrom(e);
    }

    // ── Import dialog ─────────────────────────────────────────────────────────

    private void OnImportRequested(object? sender, string filePath)
    {
        // Must dispatch to the UI thread
        _dispatcher?.TryEnqueue(async () =>
            await ShowImportDialogAsync(filePath));
    }

    private async Task ShowImportDialogAsync(string filePath)
    {
        // Parse the file first so we can show a transaction count
        IReadOnlyList<FIIITransaction> transactions;
        try
        {
            transactions = Services.QfxParserService.ParseFile(filePath);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Failed to parse {System.IO.Path.GetFileName(filePath)}:\n{ex.Message}");
            return;
        }

        // Load accounts
        IReadOnlyList<AccountSingle> accounts = [];
        if (ViewModel.IsConnected)
        {
            try { accounts = await App.FireflyService.GetAccountsAsync(); }
            catch { /* will still allow import if account manually typed */ }
        }

        var dialog = new ContentDialog
        {
            Title              = "Import QFX File",
            PrimaryButtonText  = "Import",
            CloseButtonText    = "Skip",
            DefaultButton      = ContentDialogButton.Primary,
            XamlRoot           = this.XamlRoot,
        };

        // Build dialog content
        var panel = new StackPanel { Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = $"File: {System.IO.Path.GetFileName(filePath)}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Transactions found: {transactions.Count}",
        });

        var accountCombo = new ComboBox
        {
            Header            = "Import into account",
            PlaceholderText   = "Select an account…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            DisplayMemberPath = "Name",
        };

        if (accounts.Count > 0)
        {
            foreach (var a in accounts.Where(a => a.Data.Attributes is { Active: true }))
                accountCombo.Items.Add(a);

            // Pre-select default if configured
            var cfg = App.SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(cfg.DefaultAccountId))
            {
                var def = accounts.FirstOrDefault(a => a.Data.Id == cfg.DefaultAccountId);
                if (def != null) accountCombo.SelectedItem = def;
            }
        }
        else
        {
            // No accounts loaded – show a text box for manual entry
            accountCombo.IsEnabled = false;
            panel.Children.Add(new InfoBar
            {
                Severity  = InfoBarSeverity.Warning,
                Title     = "Could not load accounts from Firefly III.",
                IsOpen    = true,
                IsClosable= false,
            });
        }

        panel.Children.Add(accountCombo);
        dialog.Content = panel;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selected = accountCombo.SelectedItem as AccountSingle;
            await ViewModel.ExecuteImportAsync(
                filePath,
                selected?.Data.Id ?? string.Empty,
                selected?.Data.Attributes?.Name ?? System.IO.Path.GetFileName(filePath));
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title            = "Error",
            Content          = message,
            CloseButtonText  = "OK",
            XamlRoot         = this.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QfxWatcher.Pages;
using QfxWatcher.ViewModels;

namespace QfxWatcher;

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
            }
        }
    }

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

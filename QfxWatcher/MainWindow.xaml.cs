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
        NavView.SelectedItem = NavView.MenuItems[1];
        ContentFrame.Navigate(typeof(SettingsPage));
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        // Block navigation to Dashboard when not connected
        var tag = item.Tag?.ToString();
        if (tag == "Dashboard" && !SettingsVM.IsConnected)
        {
            // Re-select Settings
            NavView.SelectedItem = NavView.MenuItems[1];
            return;
        }

        _ = tag switch
        {
            "Dashboard" => ContentFrame.Navigate(typeof(DashboardPage)),
            "Settings"  => ContentFrame.Navigate(typeof(SettingsPage)),
            _           => false,
        };
    }
}

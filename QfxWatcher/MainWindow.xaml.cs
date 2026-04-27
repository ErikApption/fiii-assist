using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QfxWatcher.Pages;

namespace QfxWatcher;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Navigate to Dashboard by default
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var tag = item.Tag?.ToString();
        _ = tag switch
        {
            "Dashboard" => ContentFrame.Navigate(typeof(DashboardPage)),
            "Settings"  => ContentFrame.Navigate(typeof(SettingsPage)),
            _           => false,
        };
    }
}

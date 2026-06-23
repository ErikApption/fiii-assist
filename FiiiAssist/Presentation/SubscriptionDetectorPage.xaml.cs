using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FiiiAssist.Models;
using FiiiAssist.ViewModels;

namespace FiiiAssist.Pages;

public sealed partial class SubscriptionDetectorPage : Page
{
    public SubscriptionDetectorViewModel ViewModel => App.SubscriptionDetectorViewModel;

    public SubscriptionDetectorPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Load accounts from Firefly III when the page is first shown
        if (ViewModel.Accounts.Count == 0)
        {
            await ViewModel.LoadAccountsCommand.ExecuteAsync(null);
        }
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DetectedSubscriptionRow row })
        {
            await ViewModel.CreateSubscriptionCommand.ExecuteAsync(row);
        }
    }

    private void OnIgnoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DetectedSubscriptionRow row })
        {
            ViewModel.IgnoreSubscriptionCommand.Execute(row);
        }
    }

    private void OnIgnorePayeeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DetectedSubscriptionRow row })
        {
            ViewModel.IgnorePayeeCommand.Execute(row);
        }
    }
}

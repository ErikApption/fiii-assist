using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FiiiAssist.ViewModels;

namespace FiiiAssist.Pages;

public sealed partial class BankAccountsPage : Page
{
    public BankAccountsViewModel ViewModel => App.BankAccountsViewModel;

    public BankAccountsPage()
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
}

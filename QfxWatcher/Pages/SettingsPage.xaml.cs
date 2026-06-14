using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QfxWatcher.ViewModels;

namespace QfxWatcher.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _initialized;

    public SettingsViewModel ViewModel => App.SettingsViewModel;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Populate the PasswordBox manually since it doesn't support x:Bind properly.
        TokenBox.Password = ViewModel.ServerToken;

        // Mark initialized — only after this will we forward UI changes to the ViewModel.
        _initialized = true;
    }

    private void ServerUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;

        if (ViewModel.ServerUrl != ServerUrlBox.Text)
        {
            ViewModel.ServerUrl = ServerUrlBox.Text;
        }
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        if (ViewModel.ServerToken != TokenBox.Password)
        {
            ViewModel.ServerToken = TokenBox.Password;
        }
    }
}

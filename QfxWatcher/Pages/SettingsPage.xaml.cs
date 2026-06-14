using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QfxWatcher.ViewModels;

namespace QfxWatcher.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _tokenBoxInitialized;

    public SettingsViewModel ViewModel => App.SettingsViewModel;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Populate the PasswordBox from the ViewModel after the page is fully loaded.
        // This avoids the PasswordBox TwoWay binding issue that pushes empty values
        // back to the source during initialization.
        TokenBox.Password = ViewModel.ServerToken;
        _tokenBoxInitialized = true;
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Ignore the event fired during initial population
        if (!_tokenBoxInitialized) return;

        ViewModel.ServerToken = TokenBox.Password;
    }
}

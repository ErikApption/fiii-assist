using Microsoft.UI.Xaml.Controls;
using QfxWatcher.ViewModels;

namespace QfxWatcher.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel => App.SettingsViewModel;

    public SettingsPage()
    {
        InitializeComponent();
    }
}

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using FiiiAssist.FireflyIII;
using FiiiAssist.ViewModels;
using Windows.Storage.Pickers;
using System.Threading.Tasks;

namespace FiiiAssist.Controls;

public sealed partial class ImportWizardPanel : UserControl
{
    private bool _isFilePickerOpen;

    public ImportWizardViewModel ViewModel => App.ImportWizardViewModel;

    public ImportWizardPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportWizardViewModel.CurrentStep)
            && ViewModel.CurrentStep == WizardStep.FileSelection
            && !_isFilePickerOpen)
        {
            await OpenFilePickerAsync();
        }
    }

    private async Task OpenFilePickerAsync()
    {
        _isFilePickerOpen = true;
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".qfx");
            picker.FileTypeFilter.Add(".ofx");

#if WINDOWS
            // WinUI 3 / WinAppSDK requires initializing the picker with the window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
#endif

            var file = await picker.PickSingleFileAsync();

            if (file != null)
            {
                await ViewModel.FileSelectedAsync(file.Path);
            }
            else
            {
                // User cancelled the picker — close the wizard
                ViewModel.CloseCommand.Execute(null);
            }
        }
        finally
        {
            _isFilePickerOpen = false;
        }
    }

    private void AccountListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView)
        {
            var selected = listView.SelectedItem as AccountRead;
            ViewModel.SelectAccountCommand.Execute(selected);
        }
    }
}

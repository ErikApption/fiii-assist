using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using QfxWatcher.FireflyIII;
using QfxWatcher.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QfxWatcher.Controls;

public sealed partial class ImportWizardPanel : UserControl
{
    public ImportWizardViewModel ViewModel => App.ImportWizardViewModel;

    public ImportWizardPanel()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportWizardViewModel.CurrentStep)
            && ViewModel.CurrentStep == WizardStep.FileSelection)
        {
            await OpenFilePickerAsync();
        }
    }

    private async Task OpenFilePickerAsync()
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".qfx");
        picker.FileTypeFilter.Add(".ofx");

        // WinUI 3 requires initializing the picker with the window handle
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();

        if (file != null)
        {
            ViewModel.FileSelected(file.Path);
        }
        else
        {
            // User cancelled the picker — go back to account selection
            ViewModel.GoBackCommand.Execute(null);
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

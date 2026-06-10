using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AsiAirController.ViewModels;

namespace AsiAirController.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseRoofFile_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;

        var options = new FilePickerOpenOptions
        {
            Title = "Select Roof Status File",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } } }
        };

        if (System.IO.File.Exists(vm.RoofStatusFilePath))
        {
            var folder = System.IO.Path.GetDirectoryName(vm.RoofStatusFilePath);
            if (folder != null)
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);
        }

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
            vm.RoofStatusFilePath = files[0].Path.LocalPath;
    }
}

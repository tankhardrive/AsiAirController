using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AsiAirController.Models;
using AsiAirController.ViewModels;

namespace AsiAirController.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        using var iconStream = AssetLoader.Open(new Uri("avares://AsiAirController/Assets/icon.png"));
        Icon = new WindowIcon(iconStream);

        var settings = AppSettings.Load();
        Width  = settings.WindowWidth;
        Height = settings.WindowHeight;

        Closing += (_, _) =>
        {
            var s = AppSettings.Load();
            s.WindowWidth  = Width;
            s.WindowHeight = Height;
            s.Save();
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            vm.LogEntries.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                    Dispatcher.UIThread.Post(() => LogScrollViewer.ScrollToEnd(),
                        DispatcherPriority.Background);
            };
        };
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

    private async void BrowseWeatherFile_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;

        var options = new FilePickerOpenOptions
        {
            Title = "Select Weather Data File",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } } }
        };

        if (System.IO.File.Exists(vm.WeatherFilePath))
        {
            var folder = System.IO.Path.GetDirectoryName(vm.WeatherFilePath);
            if (folder != null)
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);
        }

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
            vm.WeatherFilePath = files[0].Path.LocalPath;
    }

    private async void BrowseSyncSource_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        var options = new FolderPickerOpenOptions { Title = "Select Image Sync Source Folder", AllowMultiple = false };
        if (System.IO.Directory.Exists(vm.ImageSyncSourcePath))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(vm.ImageSyncSourcePath);
        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
            vm.ImageSyncSourcePath = folders[0].Path.LocalPath;
    }

    private async void BrowseSyncDest_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        var options = new FolderPickerOpenOptions { Title = "Select Image Sync Destination Folder", AllowMultiple = false };
        if (System.IO.Directory.Exists(vm.ImageSyncDestPath))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(vm.ImageSyncDestPath);
        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
            vm.ImageSyncDestPath = folders[0].Path.LocalPath;
    }
}

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
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.GuidePoints))
                    GuideGraph.Redraw();
                if (e.PropertyName == nameof(MainWindowViewModel.SunTimes) && vm.SunTimes != null)
                    SunTimeline.SetTimes(vm.SunTimes);
            };
        };
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

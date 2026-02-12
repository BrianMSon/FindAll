using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FindAll.Models;
using FindAll.Services;
using FindAll.Helpers;
using FindAll.ViewModels;

namespace FindAll.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            Icon = IconGenerator.CreateWindowIcon();

            var searchService = new FileSearchService();
            _viewModel = new MainWindowViewModel(searchService);
            DataContext = _viewModel;

            var list = this.FindControl<ListBox>("ResultsList");
            if (list != null)
            {
                list.DoubleTapped += OnListDoubleTapped;
                list.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel);
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Debug.WriteLine($"UNHANDLED EXCEPTION: {ex?.Message}\n{ex?.StackTrace}");
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in MainWindow constructor: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // --- Double-click: toggle group or open file in Explorer ---

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e?.Source is not Visual visual) return;
            var item = GetDataContextFromVisual(visual);
            if (item == null) return;

            if (item is DirectoryGroup group)
            {
                _viewModel?.ToggleGroup(group);
            }
            else if (item is SearchResult result && !string.IsNullOrEmpty(result.FullPath))
            {
                OpenFileInExplorer(result.FullPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in OnListDoubleTapped: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // --- Right-click context menu ---

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (e?.InitialPressMouseButton != MouseButton.Right) return;
            if (e.Source is not Visual visual) return;

            var item = GetDataContextFromVisual(visual);
            if (item == null) return;

            if (item is DirectoryGroup group)
                ShowDirectoryContextMenu(group, e);
            else if (item is SearchResult result)
                ShowFileContextMenu(result, e);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in OnListPointerReleased: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ShowDirectoryContextMenu(DirectoryGroup group, PointerReleasedEventArgs e)
    {
        var menu = new ContextMenu();

        var openExplorer = new MenuItem { Header = "Open in Explorer" };
        openExplorer.Click += (_, _) => OpenFolderInExplorer(group.Directory);
        menu.Items.Add(openExplorer);

        menu.Items.Add(new Separator());

        var toggle = new MenuItem { Header = group.IsExpanded ? "Collapse" : "Expand" };
        toggle.Click += (_, _) => _viewModel?.ToggleGroup(group);
        menu.Items.Add(toggle);

        menu.Items.Add(new Separator());

        var expandAll = new MenuItem { Header = "Expand All" };
        expandAll.Click += (_, _) => _viewModel?.SetAllGroupsExpanded(true);
        menu.Items.Add(expandAll);

        var collapseAll = new MenuItem { Header = "Collapse All" };
        collapseAll.Click += (_, _) => _viewModel?.SetAllGroupsExpanded(false);
        menu.Items.Add(collapseAll);

        menu.Items.Add(new Separator());

        var copyPath = new MenuItem { Header = "Copy Path" };
        copyPath.Click += async (_, _) =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(group.Directory);
        };
        menu.Items.Add(copyPath);

        ShowContextMenuAt(menu);
    }

    private void ShowFileContextMenu(SearchResult result, PointerReleasedEventArgs e)
    {
        var menu = new ContextMenu();

        var openFile = new MenuItem { Header = "Open File" };
        openFile.Click += (_, _) => OpenFile(result.FullPath);
        menu.Items.Add(openFile);

        var openExplorer = new MenuItem { Header = "Open in Explorer" };
        openExplorer.Click += (_, _) => OpenFileInExplorer(result.FullPath);
        menu.Items.Add(openExplorer);

        var openFolder = new MenuItem { Header = "Open Folder" };
        openFolder.Click += (_, _) => OpenFolderInExplorer(result.Directory);
        menu.Items.Add(openFolder);

        menu.Items.Add(new Separator());

        var parentGroup = _viewModel?.GroupedResults.FirstOrDefault(g => g.Items.Contains(result));
        if (parentGroup != null)
        {
            var toggle = new MenuItem { Header = parentGroup.IsExpanded ? "Collapse Group" : "Expand Group" };
            toggle.Click += (_, _) => _viewModel?.ToggleGroup(parentGroup);
            menu.Items.Add(toggle);

            menu.Items.Add(new Separator());
        }

        var expandAll = new MenuItem { Header = "Expand All" };
        expandAll.Click += (_, _) => _viewModel?.SetAllGroupsExpanded(true);
        menu.Items.Add(expandAll);

        var collapseAll = new MenuItem { Header = "Collapse All" };
        collapseAll.Click += (_, _) => _viewModel?.SetAllGroupsExpanded(false);
        menu.Items.Add(collapseAll);

        menu.Items.Add(new Separator());

        var copyPath = new MenuItem { Header = "Copy File Path" };
        copyPath.Click += async (_, _) =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(result.FullPath);
        };
        menu.Items.Add(copyPath);

        var copyName = new MenuItem { Header = "Copy File Name" };
        copyName.Click += async (_, _) =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(result.FileName);
        };
        menu.Items.Add(copyName);

        ShowContextMenuAt(menu);
    }

    private void ShowContextMenuAt(ContextMenu menu)
    {
        var list = this.FindControl<ListBox>("ResultsList");
        if (list == null) return;
        menu.PlacementTarget = list;
        menu.Open(list);
    }

    // --- Expand All / Collapse All buttons ---

    private void OnExpandAllClick(object? sender, RoutedEventArgs e) => _viewModel?.SetAllGroupsExpanded(true);
    private void OnCollapseAllClick(object? sender, RoutedEventArgs e) => _viewModel?.SetAllGroupsExpanded(false);

    // --- Browse ---

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var options = new FolderPickerOpenOptions { Title = "Select Search Folder" };

            if (_viewModel != null && !string.IsNullOrWhiteSpace(_viewModel.SearchPath))
            {
                var currentPath = _viewModel.SearchPath;
                if (Directory.Exists(currentPath))
                {
                    try
                    {
                        var folder = await StorageProvider.TryGetFolderFromPathAsync(currentPath);
                        if (folder != null)
                        {
                            options.SuggestedStartLocation = folder;
                        }
                    }
                    catch { }
                }
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(options);

            if (folders.Count > 0 && _viewModel != null)
            {
                var localPath = folders[0].TryGetLocalPath();
                if (localPath != null)
                {
                    _viewModel.SearchPath = localPath;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Browse error: {ex.Message}");
        }
    }

    // --- Helpers ---

    private static void OpenFile(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
        }
        catch { }
    }

    private static void OpenFileInExplorer(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", $"-R \"{filePath}\"");
            else
            {
                var dir = Path.GetDirectoryName(filePath);
                if (dir != null) Process.Start("xdg-open", dir);
            }
        }
        catch { }
    }

    private static void OpenFolderInExplorer(string folderPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"\"{folderPath}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", folderPath);
            else
                Process.Start("xdg-open", folderPath);
        }
        catch { }
    }

    private static object? GetDataContextFromVisual(Visual visual)
    {
        var current = visual;
        while (current != null)
        {
            if (current is ListBoxItem lbi)
                return lbi.DataContext;
            current = current.GetVisualParent() as Visual;
        }
        return null;
    }
}

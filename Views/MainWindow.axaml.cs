using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
        InitializeComponent();
        Icon = IconGenerator.CreateWindowIcon();

        var searchService = new FileSearchService();
        _viewModel = new MainWindowViewModel(searchService);
        DataContext = _viewModel;

        var tree = this.FindControl<TreeView>("ResultsTree");
        if (tree != null)
        {
            tree.SelectionChanged += OnTreeSelectionChanged;
            tree.DoubleTapped += OnTreeDoubleTapped;
            tree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel);
        }
    }

    // --- Selection ---

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SearchResult result)
        {
            _viewModel.SelectedResult = result;
        }
    }

    // --- Double-click: open in Explorer ---

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual visual) return;
        var item = GetDataContextFromVisual(visual);

        if (item is DirectoryGroup group)
        {
            OpenFolderInExplorer(group.Directory);
        }
        else if (item is SearchResult result)
        {
            OpenFileInExplorer(result.FullPath);
        }
    }

    // --- Right-click context menu ---

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (e.Source is not Visual visual) return;

        var item = GetDataContextFromVisual(visual);
        var treeViewItem = FindParentTreeViewItem(visual);

        if (item is DirectoryGroup group)
        {
            ShowDirectoryContextMenu(group, treeViewItem, e);
        }
        else if (item is SearchResult result)
        {
            ShowFileContextMenu(result, treeViewItem, e);
        }
    }

    private void ShowDirectoryContextMenu(DirectoryGroup group, TreeViewItem? treeViewItem, PointerReleasedEventArgs e)
    {
        var menu = new ContextMenu();

        var openExplorer = new MenuItem { Header = "Open in Explorer" };
        openExplorer.Click += (_, _) => OpenFolderInExplorer(group.Directory);
        menu.Items.Add(openExplorer);

        menu.Items.Add(new Separator());

        if (treeViewItem != null)
        {
            var expand = new MenuItem { Header = "Expand" };
            expand.Click += (_, _) => treeViewItem.IsExpanded = true;
            menu.Items.Add(expand);

            var collapse = new MenuItem { Header = "Collapse" };
            collapse.Click += (_, _) => treeViewItem.IsExpanded = false;
            menu.Items.Add(collapse);
        }

        menu.Items.Add(new Separator());

        var expandAll = new MenuItem { Header = "Expand All" };
        expandAll.Click += (_, _) => SetAllExpanded(true);
        menu.Items.Add(expandAll);

        var collapseAll = new MenuItem { Header = "Collapse All" };
        collapseAll.Click += (_, _) => SetAllExpanded(false);
        menu.Items.Add(collapseAll);

        menu.Items.Add(new Separator());

        var copyPath = new MenuItem { Header = "Copy Path" };
        copyPath.Click += async (_, _) =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(group.Directory);
        };
        menu.Items.Add(copyPath);

        ShowContextMenuAt(menu, e);
    }

    private void ShowFileContextMenu(SearchResult result, TreeViewItem? treeViewItem, PointerReleasedEventArgs e)
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

        // Find parent TreeViewItem (directory group level)
        var parentGroupItem = FindParentGroupTreeViewItem(treeViewItem);
        if (parentGroupItem != null)
        {
            var expand = new MenuItem { Header = "Expand Group" };
            expand.Click += (_, _) => parentGroupItem.IsExpanded = true;
            menu.Items.Add(expand);

            var collapse = new MenuItem { Header = "Collapse Group" };
            collapse.Click += (_, _) => parentGroupItem.IsExpanded = false;
            menu.Items.Add(collapse);

            menu.Items.Add(new Separator());
        }

        var expandAll = new MenuItem { Header = "Expand All" };
        expandAll.Click += (_, _) => SetAllExpanded(true);
        menu.Items.Add(expandAll);

        var collapseAll = new MenuItem { Header = "Collapse All" };
        collapseAll.Click += (_, _) => SetAllExpanded(false);
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

        ShowContextMenuAt(menu, e);
    }

    private void ShowContextMenuAt(ContextMenu menu, PointerReleasedEventArgs e)
    {
        var tree = this.FindControl<TreeView>("ResultsTree");
        if (tree == null) return;
        menu.PlacementTarget = tree;
        menu.Open(tree);
    }

    // --- Expand All / Collapse All buttons ---

    private void OnExpandAllClick(object? sender, RoutedEventArgs e) => SetAllExpanded(true);
    private void OnCollapseAllClick(object? sender, RoutedEventArgs e) => SetAllExpanded(false);

    private void SetAllExpanded(bool expanded)
    {
        var tree = this.FindControl<TreeView>("ResultsTree");
        if (tree == null) return;

        foreach (var item in tree.GetLogicalDescendants().OfType<TreeViewItem>())
        {
            // Only top-level items (directory groups)
            if (item.DataContext is DirectoryGroup)
            {
                item.IsExpanded = expanded;
            }
        }
    }

    // --- Browse ---

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Select Search Folder" });

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

    // --- Helper: open file / folder in Explorer ---

    private static void OpenFile(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static void OpenFileInExplorer(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", $"-R \"{filePath}\"");
            }
            else
            {
                // Linux: open parent directory
                var dir = Path.GetDirectoryName(filePath);
                if (dir != null)
                    Process.Start("xdg-open", dir);
            }
        }
        catch { }
    }

    private static void OpenFolderInExplorer(string folderPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"\"{folderPath}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", folderPath);
            }
            else
            {
                Process.Start("xdg-open", folderPath);
            }
        }
        catch { }
    }

    // --- Helper: find data context from visual tree ---

    private static object? GetDataContextFromVisual(Visual visual)
    {
        // Walk up to find the TreeViewItem and get its DataContext
        var current = visual;
        while (current != null)
        {
            if (current is TreeViewItem tvi)
                return tvi.DataContext;
            current = current.GetVisualParent() as Visual;
        }
        return null;
    }

    private static TreeViewItem? FindParentTreeViewItem(Visual visual)
    {
        var current = visual;
        while (current != null)
        {
            if (current is TreeViewItem tvi)
                return tvi;
            current = current.GetVisualParent() as Visual;
        }
        return null;
    }

    private static TreeViewItem? FindParentGroupTreeViewItem(TreeViewItem? childItem)
    {
        if (childItem == null) return null;

        // If this item is already a directory group, return it
        if (childItem.DataContext is DirectoryGroup)
            return childItem;

        // Walk up to find the parent TreeViewItem that is a DirectoryGroup
        var current = childItem.GetVisualParent();
        while (current != null)
        {
            if (current is TreeViewItem tvi && tvi.DataContext is DirectoryGroup)
                return tvi;
            current = current.GetVisualParent();
        }
        return null;
    }
}

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
                list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);

                if (_viewModel != null)
                {
                    _viewModel.ResultsUpdated += () =>
                    {
                        if (list.SelectedIndex >= 0)
                        {
                            var container = list.ContainerFromIndex(list.SelectedIndex);
                            if (container is ListBoxItem lbi)
                                lbi.Focus();
                            else
                                list.Focus();
                        }
                        else
                        {
                            list.Focus();
                        }
                    };
                }
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

    // --- Page Up/Down: move selection by one page ---

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (sender is not ListBox list || _viewModel == null) return;

            var items = _viewModel.FlatDisplayItems;
            if (items.Count == 0) return;

            int currentIndex = list.SelectedIndex;
            int lastIndex = items.Count - 1;
            int targetIndex;

            switch (e.Key)
            {
                case Key.PageDown:
                {
                    int pageSize = Math.Max(1, (int)(list.Bounds.Height / 24));
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                        pageSize *= 10;
                    targetIndex = currentIndex < 0 ? 0 : Math.Min(lastIndex, currentIndex + pageSize);
                    break;
                }
                case Key.PageUp:
                {
                    int pageSize = Math.Max(1, (int)(list.Bounds.Height / 24));
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                        pageSize *= 10;
                    targetIndex = currentIndex <= 0 ? 0 : Math.Max(0, currentIndex - pageSize);
                    break;
                }
                case Key.Home:
                    targetIndex = 0;
                    break;
                case Key.End:
                    targetIndex = lastIndex;
                    break;
                case Key.Up when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                {
                    // Move to previous directory group
                    targetIndex = currentIndex;
                    for (int i = currentIndex - 1; i >= 0; i--)
                    {
                        if (items[i] is DirectoryGroup)
                        {
                            targetIndex = i;
                            break;
                        }
                    }
                    break;
                }
                case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                {
                    // Move to next directory group
                    targetIndex = currentIndex;
                    for (int i = currentIndex + 1; i <= lastIndex; i++)
                    {
                        if (items[i] is DirectoryGroup)
                        {
                            targetIndex = i;
                            break;
                        }
                    }
                    break;
                }
                case Key.Left:
                case Key.Right:
                {
                    if (currentIndex >= 0 && currentIndex < items.Count)
                    {
                        var selectedItem = items[currentIndex];
                        if (selectedItem is DirectoryGroup group)
                        {
                            bool expand = e.Key == Key.Right;
                            if (group.IsExpanded != expand)
                                _viewModel.ToggleGroup(group);
                        }
                        else if (e.Key == Key.Left && selectedItem is SearchResult sr)
                        {
                            // Left on a file item: select parent group
                            var parentGroup = _viewModel.GroupedResults.FirstOrDefault(g => g.Items.Contains(sr));
                            if (parentGroup != null)
                            {
                                int groupIndex = _viewModel.FlatDisplayItems.IndexOf(parentGroup);
                                if (groupIndex >= 0)
                                {
                                    list.SelectedIndex = groupIndex;
                                    list.ScrollIntoView(parentGroup);
                                }
                            }
                        }
                    }
                    // Restore focus
                    var idx = list.SelectedIndex;
                    if (idx >= 0)
                    {
                        var c = list.ContainerFromIndex(idx);
                        if (c is ListBoxItem item)
                            item.Focus();
                        else
                            list.Focus();
                    }
                    else
                        list.Focus();
                    e.Handled = true;
                    return;
                }
                default:
                    return;
            }

            list.SelectedIndex = targetIndex;
            list.ScrollIntoView(items[targetIndex]);

            // Keep focus on the selected item container
            var container = list.ContainerFromIndex(targetIndex);
            if (container is ListBoxItem lbi)
                lbi.Focus();
            else
                list.Focus();

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in OnListKeyDown: {ex.Message}");
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

    private void OnExpandCollapseToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        _viewModel.SetAllGroupsExpanded(!_viewModel.AllExpanded);
        _viewModel.AllExpanded = !_viewModel.AllExpanded;
    }
    private void OnSortToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        _viewModel.SortAscending = !_viewModel.SortAscending;
        _viewModel.SortResults(_viewModel.SortAscending);

        var list = this.FindControl<ListBox>("ResultsList");
        if (list != null)
        {
            var idx = list.SelectedIndex;
            if (idx >= 0)
            {
                var c = list.ContainerFromIndex(idx);
                if (c is ListBoxItem item)
                    item.Focus();
                else
                    list.Focus();
            }
            else
                list.Focus();
        }
    }

    private async void OnCopyVisibleClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel == null || Clipboard == null) return;

            var sb = new System.Text.StringBuilder();
            foreach (var item in _viewModel.FlatDisplayItems)
            {
                if (item is DirectoryGroup group)
                {
                    sb.AppendLine(group.Directory);
                }
                else if (item is SearchResult result)
                {
                    if (result.LineNumber.HasValue)
                        sb.AppendLine($"\t{result.FileName}\t{result.LineNumber}\t{result.MatchingLine}");
                    else
                        sb.AppendLine($"\t{result.FileName}");
                }
            }

            await Clipboard.SetTextAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in OnCopyVisibleClick: {ex.Message}");
        }
    }

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel == null || Clipboard == null) return;

            var sb = new System.Text.StringBuilder();
            foreach (var group in _viewModel.GroupedResults)
            {
                sb.AppendLine(group.Directory);
                foreach (var item in group.Items)
                {
                    if (item.LineNumber.HasValue)
                        sb.AppendLine($"\t{item.FileName}\t{item.LineNumber}\t{item.MatchingLine}");
                    else
                        sb.AppendLine($"\t{item.FileName}");
                }
            }

            await Clipboard.SetTextAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in OnCopyAllClick: {ex.Message}");
        }
    }

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

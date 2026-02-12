using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using FindAll.Models;
using FindAll.Services;
using ReactiveUI;

namespace FindAll.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IFileSearchService _searchService;
    private CancellationTokenSource? _cts;

    private string _searchPath = Environment.CurrentDirectory;
    public string SearchPath
    {
        get => _searchPath;
        set => this.RaiseAndSetIfChanged(ref _searchPath, value);
    }

    private string _filePattern = "*.*";
    public string FilePattern
    {
        get => _filePattern;
        set => this.RaiseAndSetIfChanged(ref _filePattern, value);
    }

    private string _excludePattern = string.Empty;
    public string ExcludePattern
    {
        get => _excludePattern;
        set => this.RaiseAndSetIfChanged(ref _excludePattern, value);
    }

    private string _fileNameSearch = string.Empty;
    public string FileNameSearch
    {
        get => _fileNameSearch;
        set => this.RaiseAndSetIfChanged(ref _fileNameSearch, value);
    }

    private string _textSearch = string.Empty;
    public string TextSearch
    {
        get => _textSearch;
        set => this.RaiseAndSetIfChanged(ref _textSearch, value);
    }

    private bool _useRegex;
    public bool UseRegex
    {
        get => _useRegex;
        set => this.RaiseAndSetIfChanged(ref _useRegex, value);
    }

    private bool _caseSensitive;
    public bool CaseSensitive
    {
        get => _caseSensitive;
        set => this.RaiseAndSetIfChanged(ref _caseSensitive, value);
    }

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set => this.RaiseAndSetIfChanged(ref _isSearching, value);
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set => this.RaiseAndSetIfChanged(ref _isPaused, value);
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private SearchResult? _selectedResult;
    public SearchResult? SelectedResult
    {
        get => _selectedResult;
        set => this.RaiseAndSetIfChanged(ref _selectedResult, value);
    }

    private object? _selectedItem;
    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
            SelectedResult = value as SearchResult;
        }
    }

    private string _previewText = string.Empty;
    public string PreviewText
    {
        get => _previewText;
        set => this.RaiseAndSetIfChanged(ref _previewText, value);
    }

    private bool _isPreviewVisible;
    public bool IsPreviewVisible
    {
        get => _isPreviewVisible;
        set => this.RaiseAndSetIfChanged(ref _isPreviewVisible, value);
    }

    private bool _isTextSearch;
    public bool IsTextSearch
    {
        get => _isTextSearch;
        set => this.RaiseAndSetIfChanged(ref _isTextSearch, value);
    }

    private ObservableCollection<DirectoryGroup> _groupedResults = new();
    public ObservableCollection<DirectoryGroup> GroupedResults
    {
        get => _groupedResults;
        set => this.RaiseAndSetIfChanged(ref _groupedResults, value);
    }

    private ObservableCollection<object> _flatDisplayItems = new();
    public ObservableCollection<object> FlatDisplayItems
    {
        get => _flatDisplayItems;
        set => this.RaiseAndSetIfChanged(ref _flatDisplayItems, value);
    }

    public event Action? ResultsUpdated;

    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> PauseResumeCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }

    public MainWindowViewModel(IFileSearchService searchService)
    {
        _searchService = searchService;

        var canSearch = this.WhenAnyValue(
            x => x.SearchPath, x => x.IsSearching,
            (path, searching) => !string.IsNullOrWhiteSpace(path) && !searching);

        var canCancel = this.WhenAnyValue(x => x.IsSearching);
        var canPauseResume = this.WhenAnyValue(x => x.IsSearching);

        SearchCommand = ReactiveCommand.CreateFromTask(ExecuteSearchAsync, canSearch);
        CancelCommand = ReactiveCommand.Create(ExecuteCancel, canCancel);
        PauseResumeCommand = ReactiveCommand.Create(ExecutePauseResume, canPauseResume);
        OpenFileCommand = ReactiveCommand.Create(ExecuteOpenFile);

        this.WhenAnyValue(x => x.TextSearch)
            .Select(t => !string.IsNullOrWhiteSpace(t))
            .Subscribe(v => IsTextSearch = v);

        this.WhenAnyValue(x => x.SelectedResult)
            .Where(r => r != null)
            .Subscribe(r =>
            {
                try
                {
                    LoadPreview(r!);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in LoadPreview subscription: {ex.Message}");
                }
            });
    }

    private async Task ExecuteSearchAsync()
    {
        GroupedResults = new ObservableCollection<DirectoryGroup>();
        FlatDisplayItems = new ObservableCollection<object>();
        IsPreviewVisible = false;
        PreviewText = string.Empty;
        SelectedItem = null;
        _cts = new CancellationTokenSource();
        IsSearching = true;
        IsPaused = false;
        StatusText = "Searching...";

        var options = new SearchOptions
        {
            SearchPath = SearchPath,
            FilePattern = string.IsNullOrWhiteSpace(FilePattern) ? "*.*" : FilePattern,
            ExcludePattern = ExcludePattern ?? string.Empty,
            FileNameSearch = string.IsNullOrWhiteSpace(FileNameSearch) ? null : FileNameSearch,
            TextSearch = string.IsNullOrWhiteSpace(TextSearch) ? null : TextSearch,
            UseRegex = UseRegex,
            CaseSensitive = CaseSensitive
        };

        if (options.UseRegex)
        {
            try
            {
                if (options.TextSearch != null)
                    _ = new System.Text.RegularExpressions.Regex(options.TextSearch);
                if (options.FileNameSearch != null)
                    _ = new System.Text.RegularExpressions.Regex(options.FileNameSearch);
            }
            catch (System.Text.RegularExpressions.RegexParseException ex)
            {
                StatusText = $"Invalid regex: {ex.Message}";
                IsSearching = false;
                return;
            }
        }

        var allResults = new ConcurrentBag<SearchResult>();
        int fileCount = 0;
        int resultCount = 0;

        // Use lightweight counter instead of Progress<T> to avoid flooding UI thread
        var progress = new Progress<int>(count =>
        {
            Volatile.Write(ref fileCount, count);
        });

        var searchTask = Task.Run(async () =>
        {
            await foreach (var result in _searchService.SearchAsync(options, progress, _cts.Token))
            {
                allResults.Add(result);
                Interlocked.Increment(ref resultCount);
            }
        }, _cts.Token);

        try
        {
            // Periodically refresh results and status during search
            while (!searchTask.IsCompleted)
            {
                // Wait up to 2 seconds, checking completion every 100ms
                for (int i = 0; i < 20; i++)
                {
                    if (searchTask.IsCompleted) break;

                    if (IsPaused)
                    {
                        StatusText = $"Paused. {Volatile.Read(ref fileCount)} files scanned, {Volatile.Read(ref resultCount)} matches";
                        while (IsPaused && !_cts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(100, CancellationToken.None);
                        }
                    }

                    try { await Task.Delay(100, _cts.Token); }
                    catch (OperationCanceledException) { break; }
                }

                // Update status text once per cycle (every ~2s) instead of per-file
                var fc = Volatile.Read(ref fileCount);
                var rc = Volatile.Read(ref resultCount);
                if (!IsPaused)
                {
                    StatusText = $"Searching... {fc} files scanned, {rc} matches";
                    await BuildGroupedResultsAsync(allResults, fc);
                }
            }

            await searchTask;

            await BuildGroupedResultsAsync(allResults, fileCount);
            StatusText = $"Done. {allResults.Count} results in {GroupedResults.Count} folders ({fileCount} files scanned)";
        }
        catch (OperationCanceledException)
        {
            await BuildGroupedResultsAsync(allResults, fileCount);
            StatusText = $"Cancelled. {allResults.Count} results ({fileCount} files scanned)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
            IsPaused = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task BuildGroupedResultsAsync(ConcurrentBag<SearchResult> allResults, int fileCount)
    {
        if (allResults.IsEmpty)
        {
            StatusText = $"Searching... {fileCount} files scanned, 0 matches";
            return;
        }

        // Save current selection path
        var selectedPath = SelectedResult?.FullPath;

        // Heavy grouping/sorting in background thread
        var groups = await Task.Run(() =>
            allResults
                .GroupBy(r => r.Directory)
                .OrderBy(g => g.Key)
                .Select(g => new DirectoryGroup { Directory = g.Key, Items = g.ToList() })
                .ToList()
        );

        // Update UI on the UI thread - single assignment triggers one PropertyChanged
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                GroupedResults = new ObservableCollection<DirectoryGroup>(groups);
                RebuildFlatDisplayItems();

                StatusText = $"Searching... {fileCount} files scanned, {allResults.Count} matches";

                // Restore or set selection
                SearchResult? matchToSelect = null;

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    foreach (var group in GroupedResults)
                    {
                        matchToSelect = group.Items.FirstOrDefault(r => r.FullPath == selectedPath);
                        if (matchToSelect != null)
                            break;
                    }
                }

                if (matchToSelect == null && GroupedResults.Count > 0 && GroupedResults[0].Items.Count > 0)
                {
                    matchToSelect = GroupedResults[0].Items[0];
                }

                if (matchToSelect != null)
                {
                    SelectedItem = matchToSelect;
                }

                ResultsUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating results: {ex.Message}");
            }
        });
    }

    public void RebuildFlatDisplayItems()
    {
        var items = new List<object>();
        foreach (var group in GroupedResults)
        {
            items.Add(group);
            if (group.IsExpanded)
                items.AddRange(group.Items);
        }
        FlatDisplayItems = new ObservableCollection<object>(items);
    }

    public void ToggleGroup(DirectoryGroup group)
    {
        var prevSelection = SelectedItem;
        group.IsExpanded = !group.IsExpanded;
        RebuildFlatDisplayItems();
        RestoreSelection(prevSelection);
    }

    public void SetAllGroupsExpanded(bool expanded)
    {
        var prevSelection = SelectedItem;
        foreach (var g in GroupedResults)
            g.IsExpanded = expanded;
        RebuildFlatDisplayItems();
        RestoreSelection(prevSelection);
    }

    private void RestoreSelection(object? prevSelection)
    {
        if (prevSelection == null) return;
        if (FlatDisplayItems.Contains(prevSelection))
            SelectedItem = prevSelection;
        else if (prevSelection is SearchResult sr)
        {
            var parentGroup = GroupedResults.FirstOrDefault(g => g.Items.Contains(sr));
            if (parentGroup != null)
                SelectedItem = parentGroup;
        }
    }

    private void ExecuteCancel()
    {
        _cts?.Cancel();
    }

    private void ExecutePauseResume()
    {
        IsPaused = !IsPaused;
    }

    private void ExecuteOpenFile()
    {
        if (SelectedResult == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SelectedResult.FullPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void LoadPreview(SearchResult result)
    {
        try
        {
            if (result == null)
            {
                IsPreviewVisible = false;
                return;
            }

            if (string.IsNullOrEmpty(result.FullPath))
            {
                PreviewText = "Invalid file path";
                IsPreviewVisible = true;
                return;
            }

            if (result.LineNumber.HasValue)
            {
                var lines = _searchService?.GetContextLines(result.FullPath, result.LineNumber.Value);
                if (lines != null && lines.Any())
                {
                    PreviewText = string.Join(Environment.NewLine, lines);
                    IsPreviewVisible = true;
                }
                else
                {
                    PreviewText = "No preview available";
                    IsPreviewVisible = true;
                }
            }
            else
            {
                PreviewText = $"File: {result.FullPath}\nSize: {result.FileSize:N0} bytes\nModified: {result.ModifiedDate:yyyy-MM-dd HH:mm:ss}";
                IsPreviewVisible = true;
            }
        }
        catch (Exception ex)
        {
            PreviewText = $"Error loading preview: {ex.Message}";
            IsPreviewVisible = true;
            System.Diagnostics.Debug.WriteLine($"LoadPreview error: {ex.Message}\n{ex.StackTrace}");
        }
    }
}

public class DirectoryGroup : ViewModelBase
{
    public string Directory { get; set; } = string.Empty;
    public List<SearchResult> Items { get; set; } = new();

    public int ItemCount => Items.Count;

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }
}

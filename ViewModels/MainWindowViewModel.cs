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

    private string _searchPath = string.Empty;
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

    public ObservableCollection<SearchResult> Results { get; } = new();

    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }

    // Folder browse is handled in code-behind via StorageProvider
    public Interaction<Unit, string?> BrowseFolderInteraction { get; } = new();

    public MainWindowViewModel(IFileSearchService searchService)
    {
        _searchService = searchService;

        var canSearch = this.WhenAnyValue(
            x => x.SearchPath, x => x.IsSearching,
            (path, searching) => !string.IsNullOrWhiteSpace(path) && !searching);

        var canCancel = this.WhenAnyValue(x => x.IsSearching);

        SearchCommand = ReactiveCommand.CreateFromTask(ExecuteSearchAsync, canSearch);
        CancelCommand = ReactiveCommand.Create(ExecuteCancel, canCancel);
        OpenFileCommand = ReactiveCommand.Create(ExecuteOpenFile);

        // Track whether text search is active
        this.WhenAnyValue(x => x.TextSearch)
            .Select(t => !string.IsNullOrWhiteSpace(t))
            .Subscribe(v => IsTextSearch = v);

        // Load preview when selection changes
        this.WhenAnyValue(x => x.SelectedResult)
            .Where(r => r != null)
            .Subscribe(r => LoadPreview(r!));
    }

    private async Task ExecuteSearchAsync()
    {
        Results.Clear();
        IsPreviewVisible = false;
        PreviewText = string.Empty;
        _cts = new CancellationTokenSource();
        IsSearching = true;
        int fileCount = 0;
        StatusText = "Searching...";

        var options = new SearchOptions
        {
            SearchPath = SearchPath,
            FilePattern = string.IsNullOrWhiteSpace(FilePattern) ? "*.*" : FilePattern,
            TextSearch = string.IsNullOrWhiteSpace(TextSearch) ? null : TextSearch,
            UseRegex = UseRegex,
            CaseSensitive = CaseSensitive
        };

        // Validate regex
        if (options.UseRegex && options.TextSearch != null)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(options.TextSearch);
            }
            catch (System.Text.RegularExpressions.RegexParseException ex)
            {
                StatusText = $"Invalid regex: {ex.Message}";
                IsSearching = false;
                return;
            }
        }

        var progress = new Progress<int>(count =>
        {
            fileCount = count;
            StatusText = $"Searching... {count} files scanned, {Results.Count} matches";
        });

        try
        {
            await foreach (var result in _searchService.SearchAsync(options, progress, _cts.Token))
            {
                Results.Add(result);
            }
            StatusText = $"Done. {Results.Count} results found ({fileCount} files scanned)";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Cancelled. {Results.Count} results found ({fileCount} files scanned)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ExecuteCancel()
    {
        _cts?.Cancel();
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
        if (result.LineNumber.HasValue)
        {
            var lines = _searchService.GetContextLines(result.FullPath, result.LineNumber.Value);
            PreviewText = string.Join(Environment.NewLine, lines);
            IsPreviewVisible = true;
        }
        else
        {
            // File-name-only search: show file info
            PreviewText = $"File: {result.FullPath}\nSize: {result.FileSize:N0} bytes\nModified: {result.ModifiedDate:yyyy-MM-dd HH:mm:ss}";
            IsPreviewVisible = true;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FindAll.Services;
using FindAll.ViewModels;

namespace FindAll.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var searchService = new FileSearchService();
        _viewModel = new MainWindowViewModel(searchService);
        DataContext = _viewModel;

        // Toggle text search columns visibility
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsTextSearch))
            {
                var lineCol = this.FindControl<DataGrid>("ResultsGrid")?.Columns
                    .FirstOrDefault(c => c.Header?.ToString() == "Line#");
                var matchCol = this.FindControl<DataGrid>("ResultsGrid")?.Columns
                    .FirstOrDefault(c => c.Header?.ToString() == "Match");

                if (lineCol != null) lineCol.IsVisible = _viewModel.IsTextSearch;
                if (matchCol != null) matchCol.IsVisible = _viewModel.IsTextSearch;
            }
        };

        // Double-click to open file
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid != null)
        {
            grid.DoubleTapped += OnGridDoubleTapped;
        }

        // Initial column visibility
        Opened += (_, _) =>
        {
            var lineCol = this.FindControl<DataGrid>("ResultsGrid")?.Columns
                .FirstOrDefault(c => c.Header?.ToString() == "Line#");
            var matchCol = this.FindControl<DataGrid>("ResultsGrid")?.Columns
                .FirstOrDefault(c => c.Header?.ToString() == "Match");

            if (lineCol != null) lineCol.IsVisible = false;
            if (matchCol != null) matchCol.IsVisible = false;
        };
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Search Folder" });

        if (folders.Count > 0 && _viewModel != null)
        {
            _viewModel.SearchPath = folders[0].Path.LocalPath;
        }
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        _viewModel?.OpenFileCommand.Execute().Subscribe();
    }
}

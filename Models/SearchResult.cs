using ReactiveUI;

namespace FindAll.Models;

public class SearchResult : ReactiveObject
{
    public int DisplayIndex { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ModifiedDate { get; set; }
    public int? LineNumber { get; set; }
    public string? MatchingLine { get; set; }
    public int? MatchStartIndex { get; set; }
    public int? MatchLength { get; set; }
    public string? EncodingName { get; set; }

    private bool _isSelectedForConvert;
    public bool IsSelectedForConvert
    {
        get => _isSelectedForConvert;
        set => this.RaiseAndSetIfChanged(ref _isSelectedForConvert, value);
    }
}

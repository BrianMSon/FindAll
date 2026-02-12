namespace FindAll.Models;

public class SearchOptions
{
    public string SearchPath { get; set; } = string.Empty;
    public string FilePattern { get; set; } = "*.*";
    public string? TextSearch { get; set; }
    public bool UseRegex { get; set; }
    public bool CaseSensitive { get; set; }
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
}

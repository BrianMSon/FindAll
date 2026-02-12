using FindAll.Models;

namespace FindAll.Services;

public interface IFileSearchService
{
    IAsyncEnumerable<SearchResult> SearchAsync(
        SearchOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken);

    List<string> GetContextLines(string filePath, int lineNumber, int contextRadius = 5);
}

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FindAll.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace FindAll.Services;

public class FileSearchService : IFileSearchService
{
    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchOptions options,
        IProgress<int>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SearchResult>(
            new UnboundedChannelOptions { SingleReader = true });

        var matcher = BuildGlobMatcher(options.FilePattern);
        bool isTextSearch = !string.IsNullOrWhiteSpace(options.TextSearch);

        Regex? regex = null;
        if (isTextSearch && options.UseRegex)
        {
            var regexOptions = RegexOptions.Compiled;
            if (!options.CaseSensitive)
                regexOptions |= RegexOptions.IgnoreCase;
            regex = new Regex(options.TextSearch!, regexOptions);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    MatchCasing = MatchCasing.CaseInsensitive
                };

                var files = Directory.EnumerateFiles(options.SearchPath, "*", enumOptions)
                    .Where(f => MatchesGlob(matcher, options.SearchPath, f));

                int count = 0;

                await Parallel.ForEachAsync(files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    },
                    async (filePath, innerCt) =>
                    {
                        var results = ProcessFile(filePath, options, isTextSearch, regex);
                        foreach (var result in results)
                        {
                            await channel.Writer.WriteAsync(result, innerCt);
                        }
                        progress?.Report(Interlocked.Increment(ref count));
                    });
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return result;
        }
    }

    public List<string> GetContextLines(string filePath, int lineNumber, int contextRadius = 5)
    {
        var lines = new List<string>();
        try
        {
            int startLine = Math.Max(1, lineNumber - contextRadius);
            int endLine = lineNumber + contextRadius;
            int currentLine = 0;

            foreach (var line in File.ReadLines(filePath))
            {
                currentLine++;
                if (currentLine < startLine) continue;
                if (currentLine > endLine) break;

                string prefix = currentLine == lineNumber ? ">>> " : "    ";
                lines.Add($"{prefix}{currentLine,6}: {line}");
            }
        }
        catch { }
        return lines;
    }

    private static Matcher BuildGlobMatcher(string filePattern)
    {
        var matcher = new Matcher();
        var patterns = filePattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pattern in patterns)
        {
            matcher.AddInclude(pattern.Contains('/') || pattern.Contains('\\') ? pattern : "**/" + pattern);
        }
        return matcher;
    }

    private static bool MatchesGlob(Matcher matcher, string basePath, string filePath)
    {
        var relativePath = Path.GetRelativePath(basePath, filePath).Replace('\\', '/');
        return matcher.Match(relativePath).HasMatches;
    }

    private static List<SearchResult> ProcessFile(
        string filePath, SearchOptions options, bool isTextSearch, Regex? regex)
    {
        var results = new List<SearchResult>();

        try
        {
            var fileInfo = new FileInfo(filePath);

            if (!isTextSearch)
            {
                results.Add(new SearchResult
                {
                    FileName = fileInfo.Name,
                    FullPath = filePath,
                    Directory = fileInfo.DirectoryName ?? string.Empty,
                    FileSize = fileInfo.Length,
                    ModifiedDate = fileInfo.LastWriteTime
                });
                return results;
            }

            // Text search
            if (fileInfo.Length > options.MaxFileSizeBytes) return results;
            if (BinaryFileDetector.IsBinary(filePath)) return results;

            var comparison = options.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int lineNum = 0;
            foreach (var line in File.ReadLines(filePath))
            {
                lineNum++;

                if (regex != null)
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        results.Add(new SearchResult
                        {
                            FileName = fileInfo.Name,
                            FullPath = filePath,
                            Directory = fileInfo.DirectoryName ?? string.Empty,
                            FileSize = fileInfo.Length,
                            ModifiedDate = fileInfo.LastWriteTime,
                            LineNumber = lineNum,
                            MatchingLine = line.TrimStart(),
                            MatchStartIndex = match.Index,
                            MatchLength = match.Length
                        });
                    }
                }
                else
                {
                    int idx = line.IndexOf(options.TextSearch!, comparison);
                    if (idx >= 0)
                    {
                        results.Add(new SearchResult
                        {
                            FileName = fileInfo.Name,
                            FullPath = filePath,
                            Directory = fileInfo.DirectoryName ?? string.Empty,
                            FileSize = fileInfo.Length,
                            ModifiedDate = fileInfo.LastWriteTime,
                            LineNumber = lineNum,
                            MatchingLine = line.TrimStart(),
                            MatchStartIndex = idx,
                            MatchLength = options.TextSearch!.Length
                        });
                    }
                }
            }
        }
        catch { }

        return results;
    }
}

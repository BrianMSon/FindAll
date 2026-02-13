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

        var includeMatcher = BuildGlobMatcher(options.FilePattern, isInclude: true);
        var excludeMatcher = BuildExcludeMatcher(options.ExcludePattern);
        bool isTextSearch = !string.IsNullOrWhiteSpace(options.TextSearch);
        bool isFileNameSearch = !string.IsNullOrWhiteSpace(options.FileNameSearch);

        Regex? textRegex = null;
        if (isTextSearch && options.UseRegex)
        {
            var regexOptions = RegexOptions.Compiled;
            if (!options.CaseSensitive)
                regexOptions |= RegexOptions.IgnoreCase;
            textRegex = new Regex(options.TextSearch!, regexOptions);
        }

        Regex? fileNameRegex = null;
        if (isFileNameSearch && options.UseRegex)
        {
            var regexOptions = RegexOptions.Compiled;
            if (!options.CaseSensitive)
                regexOptions |= RegexOptions.IgnoreCase;
            try { fileNameRegex = new Regex(options.FileNameSearch!, regexOptions); }
            catch { fileNameRegex = null; }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    MatchCasing = MatchCasing.CaseInsensitive
                };

                var files = EnumerateFilesSkippingSymlinks(options.SearchPath, enumOptions)
                    .Where(f => MatchesGlob(includeMatcher, options.SearchPath, f))
                    .Where(f => !IsExcluded(excludeMatcher, options.SearchPath, f));

                int count = 0;

                await Parallel.ForEachAsync(files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    },
                    async (filePath, innerCt) =>
                    {
                        var results = ProcessFile(filePath, options, isTextSearch, isFileNameSearch, textRegex, fileNameRegex);
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
                var displayLine = line.Length > 500 ? line.Substring(0, 500) + "..." : line;
                lines.Add($"{prefix}{currentLine,6}: {displayLine}");
            }
        }
        catch { }
        return lines;
    }

    private static Matcher BuildGlobMatcher(string filePattern, bool isInclude)
    {
        var matcher = new Matcher();
        var patterns = filePattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pattern in patterns)
        {
            var p = pattern.Contains('/') || pattern.Contains('\\') ? pattern : "**/" + pattern;
            if (isInclude)
                matcher.AddInclude(p);
            else
                matcher.AddExclude(p);
        }
        return matcher;
    }

    private static Matcher? BuildExcludeMatcher(string excludePattern)
    {
        if (string.IsNullOrWhiteSpace(excludePattern))
            return null;

        var matcher = new Matcher();
        matcher.AddInclude("**/*"); // match all first
        var patterns = excludePattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pattern in patterns)
        {
            var p = pattern;
            // "bin/", "obj/" -> "**/bin/**", "**/obj/**"
            if (p.EndsWith('/') || p.EndsWith('\\'))
            {
                matcher.AddExclude("**/" + p.TrimEnd('/', '\\') + "/**");
            }
            else if (!p.Contains('/') && !p.Contains('\\') && !p.Contains('*') && !p.Contains('.'))
            {
                // bare directory name like "bin" -> also exclude as directory
                matcher.AddExclude("**/" + p + "/**");
            }
            else
            {
                matcher.AddExclude(p.Contains('/') || p.Contains('\\') ? p : "**/" + p);
            }
        }
        return matcher;
    }

    private static IEnumerable<string> EnumerateFilesSkippingSymlinks(string rootPath, EnumerationOptions enumOptions)
    {
        // Enumerate files in current directory
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(rootPath, "*", enumOptions); }
        catch { yield break; }

        foreach (var file in files)
            yield return file;

        // Recurse into subdirectories, skipping symbolic links
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(rootPath); }
        catch { yield break; }

        foreach (var dir in dirs)
        {
            try
            {
                var dirInfo = new DirectoryInfo(dir);
                if (dirInfo.LinkTarget != null) continue;
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch { continue; }

            foreach (var file in EnumerateFilesSkippingSymlinks(dir, enumOptions))
                yield return file;
        }
    }

    private static bool MatchesGlob(Matcher matcher, string basePath, string filePath)
    {
        var relativePath = Path.GetRelativePath(basePath, filePath).Replace('\\', '/');
        return matcher.Match(relativePath).HasMatches;
    }

    private static bool IsExcluded(Matcher? excludeMatcher, string basePath, string filePath)
    {
        if (excludeMatcher == null) return false;
        var relativePath = Path.GetRelativePath(basePath, filePath).Replace('\\', '/');
        return !excludeMatcher.Match(relativePath).HasMatches;
    }

    private static List<SearchResult> ProcessFile(
        string filePath, SearchOptions options,
        bool isTextSearch, bool isFileNameSearch,
        Regex? textRegex, Regex? fileNameRegex)
    {
        var results = new List<SearchResult>();

        try
        {
            var fileInfo = new FileInfo(filePath);

            // File name / folder name search filter
            if (isFileNameSearch)
            {
                var cmp = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                bool match = false;

                if (options.SearchFileNames)
                {
                    match = fileNameRegex != null
                        ? fileNameRegex.IsMatch(fileInfo.Name)
                        : fileInfo.Name.Contains(options.FileNameSearch!, cmp);
                }

                if (!match && options.SearchFolderNames)
                {
                    var dirName = fileInfo.DirectoryName ?? string.Empty;
                    match = fileNameRegex != null
                        ? fileNameRegex.IsMatch(dirName)
                        : dirName.Contains(options.FileNameSearch!, cmp);
                }

                if (!match) return results;
            }

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

            var comparison2 = options.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int lineNum = 0;
            foreach (var line in File.ReadLines(filePath))
            {
                lineNum++;

                if (textRegex != null)
                {
                    var match = textRegex.Match(line);
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
                            MatchingLine = line.TrimStart() is var trimmed1 && trimmed1.Length > 500 ? trimmed1.Substring(0, 200) + "..." : trimmed1,
                            MatchStartIndex = match.Index,
                            MatchLength = match.Length
                        });
                    }
                }
                else
                {
                    int idx = line.IndexOf(options.TextSearch!, comparison2);
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
                            MatchingLine = line.TrimStart() is var trimmed2 && trimmed2.Length > 500 ? trimmed2.Substring(0, 200) + "..." : trimmed2,
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

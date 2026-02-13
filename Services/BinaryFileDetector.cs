namespace FindAll.Services;

public static class BinaryFileDetector
{
    private const int SampleSize = 8192;

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fonts
        ".ttf", ".otf", ".woff", ".woff2", ".eot", ".fon",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", ".tiff", ".tif", ".psd",
        // Audio/Video
        ".mp3", ".mp4", ".wav", ".avi", ".mkv", ".flac", ".ogg", ".wmv", ".mov", ".webm",
        // Archives
        ".zip", ".gz", ".tar", ".rar", ".7z", ".bz2", ".xz", ".zst",
        // Executables/Libraries
        ".exe", ".dll", ".so", ".dylib", ".pdb", ".lib", ".obj", ".o", ".a",
        // Documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        // .NET / Java
        ".nupkg", ".snk", ".jar", ".class", ".war",
        // Other binary
        ".bin", ".dat", ".db", ".sqlite", ".mdb", ".iso", ".img", ".cab", ".msi", ".wasm",
    };

    public static bool IsBinaryExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }

    public static bool IsBinary(string filePath)
    {
        // Fast path: skip known binary extensions without reading the file
        //if (IsBinaryExtension(filePath))
        //    return true;

        try
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(SampleSize, stream.Length)];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0) return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }
}

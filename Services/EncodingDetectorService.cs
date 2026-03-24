using System.Text;

namespace FindAll.Services;

public enum EncodingType
{
    Unknown,
    ASCII,
    UTF8,
    UTF8_BOM,
    UTF16_LE,
    UTF16_BE,
    UTF32_LE,
    UTF32_BE,
    ANSI,
    Binary
}

public static class EncodingTypeExtensions
{
    public static string ToDisplayString(this EncodingType type) => type switch
    {
        EncodingType.ASCII => "ASCII",
        EncodingType.UTF8 => "UTF-8",
        EncodingType.UTF8_BOM => "UTF-8 BOM",
        EncodingType.UTF16_LE => "UTF-16 LE",
        EncodingType.UTF16_BE => "UTF-16 BE",
        EncodingType.UTF32_LE => "UTF-32 LE",
        EncodingType.UTF32_BE => "UTF-32 BE",
        EncodingType.ANSI => "ANSI",
        EncodingType.Binary => "Binary",
        _ => "Unknown"
    };
}

public static class EncodingDetectorService
{
    private const int SampleSize = 64 * 1024;

    public static EncodingType DetectEncoding(string filePath)
    {
        try
        {
            // Known binary extensions → skip content analysis
            if (BinaryFileDetector.IsBinaryExtension(filePath))
                return EncodingType.Binary;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0)
                return EncodingType.ASCII;

            var bufferSize = (int)Math.Min(fs.Length, SampleSize);
            var buffer = new byte[bufferSize];
            var bytesRead = fs.Read(buffer, 0, bufferSize);
            if (bytesRead < bufferSize)
                Array.Resize(ref buffer, bytesRead);

            // Check BOM
            if (bytesRead >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return EncodingType.UTF8_BOM;
            if (bytesRead >= 4 && buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
                return EncodingType.UTF32_LE;
            if (bytesRead >= 4 && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
                return EncodingType.UTF32_BE;
            if (bytesRead >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
                return EncodingType.UTF16_LE;
            if (bytesRead >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
                return EncodingType.UTF16_BE;

            // No BOM: analyze content
            bool hasHighBytes = false;
            bool hasNullBytes = false;

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0x00) hasNullBytes = true;
                if (buffer[i] >= 0x80) hasHighBytes = true;
            }

            if (hasNullBytes)
            {
                int evenNulls = 0, oddNulls = 0;
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0x00)
                    {
                        if (i % 2 == 0) evenNulls++;
                        else oddNulls++;
                    }
                }
                if (oddNulls > bytesRead / 8 && evenNulls < oddNulls / 4)
                    return EncodingType.UTF16_LE;
                if (evenNulls > bytesRead / 8 && oddNulls < evenNulls / 4)
                    return EncodingType.UTF16_BE;
                return EncodingType.Binary;
            }

            if (!hasHighBytes)
                return EncodingType.ASCII;

            if (IsValidUtf8(buffer, bytesRead))
                return EncodingType.UTF8;

            return EncodingType.ANSI;
        }
        catch
        {
            return EncodingType.Unknown;
        }
    }

    public static string GetBomDescription(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[4];
            int read = fs.Read(header, 0, Math.Min(4, (int)fs.Length));
            if (read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
                return "EF BB BF (UTF-8 BOM)";
            if (read >= 4 && header[0] == 0xFF && header[1] == 0xFE && header[2] == 0x00 && header[3] == 0x00)
                return "FF FE 00 00 (UTF-32 LE BOM)";
            if (read >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0xFE && header[3] == 0xFF)
                return "00 00 FE FF (UTF-32 BE BOM)";
            if (read >= 2 && header[0] == 0xFF && header[1] == 0xFE)
                return "FF FE (UTF-16 LE BOM)";
            if (read >= 2 && header[0] == 0xFE && header[1] == 0xFF)
                return "FE FF (UTF-16 BE BOM)";
        }
        catch { }
        return "None";
    }

    public static Encoding GetSystemEncoding(EncodingType type)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return type switch
        {
            EncodingType.UTF8 => new UTF8Encoding(false),
            EncodingType.UTF8_BOM => new UTF8Encoding(true),
            EncodingType.UTF16_LE => Encoding.Unicode,
            EncodingType.UTF16_BE => Encoding.BigEndianUnicode,
            EncodingType.UTF32_LE => Encoding.UTF32,
            EncodingType.UTF32_BE => new UTF32Encoding(true, true),
            EncodingType.ASCII => Encoding.ASCII,
            EncodingType.ANSI => Encoding.GetEncoding(949),
            _ => Encoding.Default
        };
    }

    public static void ConvertFile(string filePath, EncodingType sourceType, EncodingType targetType)
    {
        var sourceEncoding = GetSystemEncoding(sourceType);
        var targetEncoding = GetSystemEncoding(targetType);
        var content = File.ReadAllText(filePath, sourceEncoding);
        File.WriteAllText(filePath, content, targetEncoding);
    }

    private static bool IsValidUtf8(byte[] buffer, int length)
    {
        int i = 0;
        bool hasMultibyte = false;
        while (i < length)
        {
            byte b = buffer[i];
            int expectedBytes;

            if (b <= 0x7F) { i++; continue; }
            else if ((b & 0xE0) == 0xC0)
            {
                expectedBytes = 2;
                if (b < 0xC2) return false;
            }
            else if ((b & 0xF0) == 0xE0)
                expectedBytes = 3;
            else if ((b & 0xF8) == 0xF0)
            {
                expectedBytes = 4;
                if (b > 0xF4) return false;
            }
            else
                return false;

            if (i + expectedBytes > length)
                return false;

            for (int j = 1; j < expectedBytes; j++)
            {
                if ((buffer[i + j] & 0xC0) != 0x80)
                    return false;
            }

            hasMultibyte = true;
            i += expectedBytes;
        }
        return hasMultibyte;
    }
}

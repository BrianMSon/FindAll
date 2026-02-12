namespace FindAll.Services;

public static class BinaryFileDetector
{
    private const int SampleSize = 8192;

    public static bool IsBinary(string filePath)
    {
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
